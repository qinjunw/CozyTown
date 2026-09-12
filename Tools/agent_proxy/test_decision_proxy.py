import json
import unittest
import threading
import io
import urllib.request
import tempfile
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor

from decision_proxy import DeepSeekTransport, ProxyError, ProxyService, create_server, load_cozytown_key


class ProxyServiceTests(unittest.TestCase):
    def test_resource_candidate_preserves_only_the_accepted_meeting_identifier(self):
        for operation in ("deliver", "cancel_exchange"):
            with self.subTest(operation=operation):
                candidate = {"schemaVersion": 3, "operation": operation, "meetingId": "accepted-id",
                             "buyerId": "player", "quantity": 999, "totalPrice": 0}
                service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(candidate)}}]})
                context = self.context() | {"schemaVersion": 3, "allowedOperations": ["deliver", "cancel_exchange"]}
                self.assertEqual(service.decide(context), {"schemaVersion": 3, "operation": operation, "meetingId": "accepted-id"})

    def test_trace_and_status_count_failures_and_exclude_unrecognized_candidate_fields(self):
        trace = io.StringIO()
        service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps({
            "schemaVersion": 1, "operation": "wait", "unrecognized": "not-a-game-candidate"})}}]}, max_calls=1, trace=trace)
        self.assertEqual(service.decide(self.context()), {"schemaVersion": 1, "operation": "wait"})
        self.assertEqual(service.status["remainingCalls"], 0)
        self.assertEqual(service.status["inflight"], 0)
        self.assertNotIn("not-a-game-candidate", trace.getvalue())
        self.assertEqual(json.loads(trace.getvalue())["status"], "passed")

    def test_private_file_selects_only_one_cozytown_credential(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "credentials.json"
            path.write_text(json.dumps({"unrelated": {"Todo": "Other", "testingAPIKey": "other"},
                "target": {"Todo": "CozyTown", "testingAPIKey": "fake-cozy-key", "APIDocument_URI": "https://untrusted.invalid"}}))
            self.assertEqual(load_cozytown_key(path), "fake-cozy-key")
            path.write_text(json.dumps({"a": {"Todo": "CozyTown", "testingAPIKey": "one"},
                                       "b": {"Todo": "CozyTown", "testingAPIKey": "two"}}))
            with self.assertRaisesRegex(ProxyError, "proxy.credential_ambiguous"):
                load_cozytown_key(path)

    def test_invalid_context_never_reaches_the_provider(self):
        for context in (None, [], {}, self.context() | {"schemaVersion": 4},
                        self.context() | {"allowedOperations": ["give_coins"]},
                        self.context() | {"persona": "界" * 12000}):
            with self.subTest(context_type=type(context).__name__):
                called = []
                service = ProxyService(lambda payload: called.append(payload))
                with self.assertRaisesRegex(ProxyError, "proxy.context_invalid"):
                    service.decide(context)
                self.assertEqual(called, [])

    def test_loopback_http_endpoint_returns_the_provider_candidate(self):
        service = ProxyService(lambda payload: {"choices": [{"message": {
            "content": '{"schemaVersion":1,"operation":"wait"}'}}]})
        server = create_server(service)
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        try:
            address = "http://127.0.0.1:" + str(server.server_port) + "/decide"
            request = urllib.request.Request(address, data=json.dumps(self.context()).encode(),
                                             headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(request, timeout=2) as response:
                self.assertEqual(json.load(response), {"schemaVersion": 1, "operation": "wait"})
        finally:
            server.shutdown()
            server.server_close()
            worker.join(timeout=2)

    def test_measurement_records_reported_model_and_usage_without_raw_prompt_or_reasoning(self):
        def provider(payload):
            return {"model": "deepseek-flash", "usage": {"prompt_tokens": 30, "completion_tokens": 10, "total_tokens": 40},
                    "choices": [{"message": {"content": '{"schemaVersion":1,"operation":"wait"}',
                                             "reasoning_content": "private-provider-reasoning"}}]}

        service = ProxyService(provider)
        context = self.context() | {"persona": "private-resident-context"}
        service.decide(context)
        self.assertEqual(len(service.measurements), 1)
        record = service.measurements[0]
        self.assertEqual(record["requestedModel"], "deepseek-v4-flash")
        self.assertEqual(record["returnedModel"], "deepseek-flash")
        self.assertEqual(record["totalTokens"], 40)
        self.assertGreaterEqual(record["elapsedMilliseconds"], 0)
        self.assertNotIn("private-provider-reasoning", json.dumps(record))
        self.assertNotIn("private-resident-context", json.dumps(record))

    def test_invalid_or_unoffered_candidates_are_rejected(self):
        for content in ("", "[]", "null", '{"schemaVersion":2,"operation":"wait"}',
                        '{"schemaVersion":1,"operation":"give_coins"}',
                        '{"schemaVersion":1,"operation":"visit","durationGameMinutes":NaN}'):
            with self.subTest(content=content):
                service = ProxyService(lambda payload: {"choices": [{"message": {"content": content}}]})
                with self.assertRaisesRegex(ProxyError, "provider.candidate_invalid"):
                    service.decide(self.context())

    def test_provider_transport_uses_the_fixed_api_and_keeps_the_key_out_of_json(self):
        captured = []

        class Opener:
            def open(self, request, timeout):
                captured.append((request, timeout))
                return io.BytesIO(b'{"model":"deepseek-v4.1-flash","choices":[]}')

        response = DeepSeekTransport("local-test-token", opener=Opener())({"model": "deepseek-v4-flash"})
        self.assertEqual(response["model"], "deepseek-v4.1-flash")
        request, timeout = captured[0]
        self.assertEqual(request.full_url, "https://api.deepseek.com/chat/completions")
        self.assertEqual(request.get_header("Authorization"), "Bearer local-test-token")
        self.assertNotIn(b"local-test-token", request.data)
        self.assertEqual(timeout, 7)

    def test_inflight_limit_prevents_an_extra_provider_call(self):
        entered = threading.Event()
        release = threading.Event()
        calls = []

        def provider(payload):
            calls.append(payload)
            if len(calls) == 1:
                entered.set()
                release.wait(3)
            return {"choices": [{"message": {"content": '{"schemaVersion":1,"operation":"wait"}'}}]}

        service = ProxyService(provider, max_parallel=1)
        with ThreadPoolExecutor(max_workers=1) as executor:
            first = executor.submit(service.decide, self.context())
            try:
                self.assertTrue(entered.wait(2))
                with self.assertRaisesRegex(ProxyError, "proxy.busy"):
                    service.decide(self.context())
                self.assertEqual(len(calls), 1)
            finally:
                release.set()
                first.result(timeout=3)

    def test_failed_provider_attempt_consumes_the_session_call_budget(self):
        calls = []

        def unavailable(payload):
            calls.append(payload)
            raise ProxyError("provider.http_401")

        service = ProxyService(unavailable, max_calls=1)
        with self.assertRaisesRegex(ProxyError, "provider.http_401"):
            service.decide(self.context())
        with self.assertRaisesRegex(ProxyError, "proxy.call_limit"):
            service.decide(self.context())
        self.assertEqual(len(calls), 1)

    def test_decision_context_becomes_one_bounded_json_model_call(self):
        sent = []

        def provider(payload):
            sent.append(payload)
            return {
                "model": "deepseek-v4.1-flash",
                "usage": {"prompt_tokens": 50, "completion_tokens": 12},
                "choices": [{"finish_reason": "stop", "message": {
                    "content": '{"schemaVersion":1,"operation":"wait"}'}}],
            }

        reply = ProxyService(provider).decide(self.context())

        self.assertEqual(reply, {"schemaVersion": 1, "operation": "wait"})
        self.assertEqual(len(sent), 1)
        self.assertEqual(sent[0]["model"], "deepseek-v4-flash")
        self.assertEqual(sent[0]["thinking"], {"type": "disabled"})
        self.assertEqual(sent[0]["max_tokens"], 512)
        self.assertEqual(sent[0]["response_format"], {"type": "json_object"})
        self.assertEqual(json.loads(sent[0]["messages"][1]["content"])["npcId"], "ren")

    @staticmethod
    def context():
        return {"schemaVersion": 1, "npcId": "ren", "decisionId": "decision-1",
                "step": 1, "allowedOperations": ["wait"]}


if __name__ == "__main__":
    unittest.main()
