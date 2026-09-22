import io
import json
import threading
import unittest
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager

from decision_proxy import ProxyError, ProxyService, create_server


class DecisionTelemetryTests(unittest.TestCase):
    def test_raw_candidate_limit_preserves_utf8_prefix_and_marks_only_capture_truncation(self):
        for raw, expected, truncated in (("{invalid}", "{invalid}", False),
                                         ("x" * 16384, "x" * 16384, False),
                                         ("界" * 5462, "界" * 5461, True)):
            with self.subTest(size=len(raw.encode("utf-8"))):
                service = ProxyService(lambda payload: {"choices": [{"message": {"content": raw}}]})
                with self.assertRaisesRegex(ProxyError, "provider.candidate_invalid"):
                    service.decide(self.context())
                record = service.measurements[0]
                self.assertEqual(record["rawCandidate"], expected)
                self.assertEqual(record["rawCandidateTruncated"], truncated)
                self.assertLessEqual(len(record["rawCandidate"].encode("utf-8")), 16384)

    def test_measurements_hide_inflight_calls_then_include_completed_transport_failure(self):
        entered, release = threading.Event(), threading.Event()

        def unavailable(payload):
            entered.set()
            release.wait(3)
            raise ProxyError("provider.http_503")

        service = ProxyService(unavailable)
        with self.server(service) as address, ThreadPoolExecutor(max_workers=1) as executor:
            pending = executor.submit(service.decide, self.context())
            try:
                self.assertTrue(entered.wait(2))
                with urllib.request.urlopen(address + "/measurements?decisionId=decision-1&step=1", timeout=2) as response:
                    self.assertEqual(json.load(response), {"measurements": []})
                self.assertEqual(service.status["attemptedProviderCalls"], 1)
                self.assertEqual(service.status["inflight"], 1)
            finally:
                release.set()
            with self.assertRaisesRegex(ProxyError, "provider.http_503"):
                pending.result(timeout=3)
            with urllib.request.urlopen(address + "/measurements?decisionId=decision-1&step=1", timeout=2) as response:
                record = json.load(response)["measurements"][0]
            self.assertEqual(record["status"], "provider.http_503")
            self.assertIsNone(record["rawCandidate"])
            self.assertFalse(record["rawCandidateTruncated"])
            self.assertIsNone(record["returnedModel"])
            self.assertIsNone(record["totalTokens"])

    def test_measurements_reject_ambiguous_filters_and_unknown_paths_without_provider_calls(self):
        calls = []
        service = ProxyService(lambda payload: calls.append(payload))
        with self.server(service) as address:
            for suffix in ("?step=1", "?decisionId=", "?decisionId=x&step=0", "?decisionId=x&step=-1",
                           "?decisionId=x&step=1.0", "?decisionId=x&step=", "?decisionId=x&unknown=1",
                           "?decisionId=x&decisionId=y", "?decisionId=x&step=1&step=2", "?decisionId"):
                with self.subTest(query=suffix), self.assertRaises(urllib.error.HTTPError) as caught:
                    urllib.request.urlopen(address + "/measurements" + suffix, timeout=2)
                with caught.exception as response:
                    self.assertEqual(response.code, 400)
                    self.assertEqual(json.load(response), {"error": "proxy.measurement_query_invalid"})
            for path in ("/measurements/", "/measurements-other", "/unknown?decisionId=x"):
                with self.subTest(path=path), self.assertRaises(urllib.error.HTTPError) as caught:
                    urllib.request.urlopen(address + path, timeout=2)
                with caught.exception as response:
                    self.assertEqual(response.code, 404)
        self.assertEqual(calls, [])
        self.assertEqual(service.status["attemptedProviderCalls"], 0)

    def test_provider_metadata_objects_are_not_exported_as_model_or_usage(self):
        raw = '{"schemaVersion":1,"operation":"wait"}'
        service = ProxyService(lambda payload: {
            "model": {"Authorization": "excluded-header"}, "usage": ["excluded-object"],
            "choices": [{"message": {"content": raw}}]})

        self.assertEqual(service.decide(self.context()), {"schemaVersion": 1, "operation": "wait"})

        record = service.measurements[0]
        self.assertIsNone(record["returnedModel"])
        self.assertIsNone(record["totalTokens"])
        self.assertEqual(record["rawCandidate"], raw)
        self.assertNotIn("excluded-", json.dumps(record))

    def test_loopback_measurements_filter_completed_calls_without_dispatching(self):
        candidate = {"schemaVersion": 1, "operation": "wait"}
        service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(candidate)}}]})
        for decision, step in (("decision-1", 1), ("decision-1", 2), ("decision-2", 1)):
            self.assertEqual(service.decide(self.context(decision, step)), candidate)
        before = service.status

        with self.server(service) as address:
            with urllib.request.urlopen(address + "/measurements?decisionId=decision-1&step=2", timeout=2) as response:
                result = json.load(response)
            self.assertEqual(list(result), ["measurements"])
            self.assertEqual(len(result["measurements"]), 1)
            record = result["measurements"][0]
            self.assertEqual((record["decisionId"], record["step"], record["call"]), ("decision-1", 2, 2))
            self.assertEqual(record["candidate"], candidate)
            self.assertEqual(json.loads(record["rawCandidate"]), candidate)
            with urllib.request.urlopen(address + "/measurements?decisionId=decision-1", timeout=2) as response:
                self.assertEqual(len(json.load(response)["measurements"]), 2)
            with urllib.request.urlopen(address + "/measurements", timeout=2) as response:
                self.assertEqual(len(json.load(response)["measurements"]), 3)

        self.assertEqual(service.status, before)

    def test_rejected_candidate_keeps_its_exact_text_and_reported_usage(self):
        raw = ' {"schemaVersion":1,"operation":"visit","arguments":{"locationId":"ren.rest"}} '
        trace = io.StringIO()
        service = ProxyService(lambda payload: {
            "model": "deepseek-flash",
            "usage": {"prompt_tokens": 30, "completion_tokens": 10, "total_tokens": 40},
            "choices": [{"message": {"content": raw, "reasoning_content": "excluded-reasoning"}}],
            "headers": {"Authorization": "excluded-provider-header"},
        }, trace=trace)
        context = {"schemaVersion": 1, "npcId": "ren", "decisionId": "decision-1", "step": 1,
                   "allowedOperations": ["visit"], "knownLocationIds": ["ren.rest"],
                   "persona": "excluded-context"}

        with self.assertRaises(ProxyError) as rejected:
            service.decide(context)

        self.assertEqual(rejected.exception.candidate_error_code, "candidate.location_id_required")
        record = service.measurements[0]
        self.assertEqual(record["rawCandidate"], raw)
        self.assertFalse(record["rawCandidateTruncated"])
        self.assertEqual(record["status"], "provider.candidate_invalid")
        self.assertEqual(record["returnedModel"], "deepseek-flash")
        self.assertEqual(record["totalTokens"], 40)
        self.assertNotIn("candidate", record)
        self.assertEqual(json.loads(trace.getvalue()), record)
        self.assertNotIn("excluded-", trace.getvalue())

    @staticmethod
    def context(decision="decision-1", step=1):
        return {"schemaVersion": 1, "npcId": "ren", "decisionId": decision, "step": step,
                "allowedOperations": ["wait"]}

    @staticmethod
    @contextmanager
    def server(service):
        server = create_server(service)
        if server.server_address[0] != "127.0.0.1":
            raise AssertionError("Experiment telemetry must remain on loopback.")
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        try:
            yield "http://127.0.0.1:" + str(server.server_port)
        finally:
            server.shutdown()
            server.server_close()
            worker.join(timeout=2)


if __name__ == "__main__":
    unittest.main()
