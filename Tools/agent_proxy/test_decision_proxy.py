import json
import unittest
import threading
import io
import urllib.error
import urllib.request
import tempfile
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor

from decision_proxy import DeepSeekTransport, ProxyError, ProxyService, create_server, load_cozytown_key


class ProxyServiceTests(unittest.TestCase):
    def test_correction_is_a_separate_host_request_with_the_original_identity_and_limits(self):
        sent = []

        def provider(payload):
            sent.append(payload)
            candidate = {"schemaVersion": 4, "operation": "invite"}
            if len(sent) == 2:
                candidate["planId"] = "host-plan"
            return {"choices": [{"message": {"content": json.dumps(candidate)}}]}

        service = ProxyService(provider, max_calls=2)
        context = self.context() | {"schemaVersion": 4, "allowedOperations": ["invite", "wait"],
                                    "social": {"planId": "host-plan"}, "remainingCalls": 1}
        with self.assertRaises(ProxyError):
            service.decide(context)
        self.assertEqual(len(sent), 1)
        correction = context | {"step": 2, "remainingCalls": 0, "candidateErrorCode": "candidate.plan_id_required"}
        self.assertEqual(service.decide(correction), {"schemaVersion": 4, "operation": "invite", "planId": "host-plan"})
        self.assertEqual(json.loads(sent[0]["messages"][1]["content"]), context)
        self.assertEqual(json.loads(sent[1]["messages"][1]["content"]), correction)
        self.assertEqual([record["status"] for record in service.measurements], ["provider.candidate_invalid", "passed"])
        self.assertEqual(service.measurements[0]["candidateErrorCode"], "candidate.plan_id_required")
        self.assertEqual(service.status["attemptedProviderCalls"], 2)
        for payload in sent:
            self.assertEqual(payload["model"], "deepseek-v4-flash")
            self.assertEqual(payload["thinking"], {"type": "disabled"})
            self.assertEqual(payload["max_tokens"], 512)

    def test_unparsed_nonobject_and_oversized_candidates_have_no_correction_code(self):
        for content in ("", "{broken}", "[]", "null", "x" * 16385):
            with self.subTest(contentLength=len(content)):
                service = ProxyService(lambda payload: {"choices": [{"message": {"content": content}}]})
                with self.assertRaises(ProxyError) as caught:
                    service.decide(self.context())
                self.assertEqual(caught.exception.code, "provider.candidate_invalid")
                self.assertEqual(caught.exception.status, 502)
                self.assertIsNone(caught.exception.candidate_error_code)
                self.assertNotIn("candidateErrorCode", service.measurements[0])

    def test_http_candidate_errors_expose_only_the_diagnostic_and_never_retry(self):
        for content, status, expected in (
            ('{"schemaVersion":4,"operation":"invite","arguments":{"planId":"private-candidate-value"}}',
             422, {"error": "provider.candidate_invalid", "candidateErrorCode": "candidate.plan_id_required"}),
            ("not-json-private-candidate-value", 502, {"error": "provider.candidate_invalid"}),
        ):
            with self.subTest(status=status):
                trace = io.StringIO()
                service = ProxyService(lambda payload: {"choices": [{"message": {"content": content}}]},
                                       max_calls=1, trace=trace)
                server = create_server(service)
                worker = threading.Thread(target=server.serve_forever, daemon=True)
                worker.start()
                try:
                    context = self.context() | {"schemaVersion": 4, "allowedOperations": ["invite"],
                                                "social": {"planId": "host-plan"}}
                    request = urllib.request.Request("http://127.0.0.1:" + str(server.server_port) + "/decide",
                        data=json.dumps(context).encode(), headers={"Content-Type": "application/json"})
                    with self.assertRaises(urllib.error.HTTPError) as caught:
                        urllib.request.urlopen(request, timeout=2)
                    with caught.exception as response:
                        self.assertEqual(response.code, status)
                        self.assertEqual(json.load(response), expected)
                    self.assertEqual(service.status["attemptedProviderCalls"], 1)
                    self.assertNotIn("private-candidate-value", trace.getvalue())
                finally:
                    server.shutdown()
                    server.server_close()
                    worker.join(timeout=2)

    def test_speech_requires_nonempty_text_with_at_most_240_utf16_units(self):
        context = self.context() | {"schemaVersion": 4, "allowedOperations": ["say"],
                                    "social": {"meetingId": "1" * 32}}
        candidate = {"schemaVersion": 4, "operation": "say", "meetingId": "1" * 32}
        for fields in ({}, {"text": None}, {"text": False}, {"text": " "}, {"text": 5},
                       {"text": "x" * 241}, {"text": "\U0001f41f" * 121}, {"text": "\ud800"}):
            with self.subTest(fields=fields):
                self.assert_candidate_error(candidate | fields, context, "candidate.text_invalid")
        for speech in ("x" * 240, "\U0001f41f" * 120):
            valid = candidate | {"text": speech}
            service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(valid)}}]})
            self.assertEqual(service.decide(context), valid)

    def test_visit_duration_requires_an_explicit_finite_number_within_both_limits(self):
        context = self.context() | {"allowedOperations": ["visit"], "knownLocationIds": ["ren.rest"],
                                    "allowedActivities": ["resting"], "maxActivityDurationGameMinutes": 30}
        candidate = {"schemaVersion": 1, "operation": "visit", "locationId": "ren.rest", "activity": "resting"}
        for fields in ({}, {"durationGameMinutes": None}, {"durationGameMinutes": True},
                       {"durationGameMinutes": "20"}, {"durationGameMinutes": 0}, {"durationGameMinutes": -1},
                       {"durationGameMinutes": 31}, {"durationGameMinutes": float("nan")},
                       {"durationGameMinutes": float("inf")}, {"durationGameMinutes": 10 ** 400}):
            with self.subTest(fields=fields):
                self.assert_candidate_error(candidate | fields, context, "candidate.duration_invalid")
        self.assert_candidate_error(candidate | {"durationGameMinutes": 1441},
                                    context | {"maxActivityDurationGameMinutes": 3000}, "candidate.duration_invalid")
        for duration in (0.5, 30):
            with self.subTest(validDuration=duration):
                valid = candidate | {"durationGameMinutes": duration}
                service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(valid)}}]})
                self.assertEqual(service.decide(context), valid)

    def test_visit_activity_must_be_supported_and_offered_in_the_request(self):
        context = self.context() | {"allowedOperations": ["visit"], "knownLocationIds": ["ren.rest"],
                                    "allowedActivities": ["working"], "maxActivityDurationGameMinutes": 30}
        candidate = {"schemaVersion": 1, "operation": "visit", "locationId": "ren.rest", "durationGameMinutes": 20}
        for fields in ({}, {"activity": None}, {"activity": " "}, {"activity": 5},
                       {"activity": "home"}, {"activity": "resting"}):
            with self.subTest(fields=fields):
                self.assert_candidate_error(candidate | fields, context, "candidate.activity_invalid")
        candidate["activity"] = "working"
        service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(candidate)}}]})
        self.assertEqual(service.decide(context), candidate)

    def test_location_candidates_require_a_known_top_level_location(self):
        for operation in ("inspect_location", "visit"):
            context = self.context() | {"allowedOperations": [operation], "knownLocationIds": ["ren.rest"]}
            for fields, error in (({}, "candidate.location_id_required"),
                                  ({"locationId": " "}, "candidate.location_id_required"),
                                  ({"locationId": 5}, "candidate.location_id_required"),
                                  ({"arguments": {"locationId": "ren.rest"}}, "candidate.location_id_required"),
                                  ({"locationId": "sora.work"}, "candidate.location_unknown")):
                with self.subTest(operation=operation, fields=fields):
                    self.assert_candidate_error({"schemaVersion": 1, "operation": operation} | fields, context, error)
        context = self.context() | {"allowedOperations": ["inspect_location"], "knownLocationIds": ["ren.rest"]}
        candidate = {"schemaVersion": 1, "operation": "inspect_location", "locationId": "ren.rest"}
        service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(candidate)}}]})
        self.assertEqual(service.decide(context), candidate)

    def test_meeting_identifiers_require_the_current_nonzero_guid_value(self):
        context = self.context() | {"schemaVersion": 4, "allowedOperations": ["deliver"],
                                    "social": {"meetingId": "abcdef0123456789abcdef0123456789"}}
        for meeting_id, error in (("bad-guid", "candidate.meeting_id_invalid"),
                                  ("0" * 32, "candidate.meeting_id_invalid"),
                                  ("1" * 32, "candidate.meeting_id_mismatch")):
            with self.subTest(meetingId=meeting_id):
                self.assert_candidate_error({"schemaVersion": 4, "operation": "deliver", "meetingId": meeting_id},
                                            context, error)
        candidate = {"schemaVersion": 4, "operation": "deliver",
                     "meetingId": "ABCDEF01-2345-6789-ABCD-EF0123456789"}
        service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(candidate)}}]})
        self.assertEqual(service.decide(context), candidate)

    def test_meeting_operations_require_a_nonempty_top_level_meeting_identifier(self):
        for operation in ("accept_invite", "decline_invite", "say", "end_conversation", "deliver", "cancel_exchange"):
            for fields in ({}, {"meetingId": None}, {"meetingId": " "}, {"meetingId": 5},
                           {"arguments": {"meetingId": "1" * 32}}):
                with self.subTest(operation=operation, fields=fields):
                    context = self.context() | {"schemaVersion": 4, "allowedOperations": [operation],
                                                "social": {"meetingId": "1" * 32}}
                    self.assert_candidate_error({"schemaVersion": 4, "operation": operation} | fields,
                                                context, "candidate.meeting_id_required")

    def test_invitation_cannot_select_another_plan_or_rewrite_the_host_identifier(self):
        context = self.context() | {"schemaVersion": 4, "allowedOperations": ["invite"],
                                    "social": {"planId": "host-plan"}}
        for plan_id in ("other-plan", "host-plan ", "HOST-PLAN"):
            with self.subTest(planId=plan_id):
                self.assert_candidate_error({"schemaVersion": 4, "operation": "invite", "planId": plan_id},
                                            context, "candidate.plan_id_mismatch")
        candidate = {"schemaVersion": 4, "operation": "invite", "planId": "host-plan"}
        service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(candidate)}}]})
        self.assertEqual(service.decide(context), candidate)

    def test_candidate_schema_and_operation_must_match_the_original_request(self):
        for version in (None, True, 1.0, "1", 2, 5):
            with self.subTest(schemaVersion=version):
                self.assert_candidate_error({"schemaVersion": version, "operation": "wait"},
                                            self.context(), "candidate.schema_mismatch")
        for operation in (None, "", "visit", "give_coins", ["wait"]):
            with self.subTest(operation=operation):
                self.assert_candidate_error({"schemaVersion": 1, "operation": operation},
                                            self.context(), "candidate.operation_unavailable")

    def test_invitation_requires_a_nonempty_top_level_plan_identifier(self):
        for fields in ({}, {"planId": None}, {"planId": " "}, {"planId": 5},
                       {"arguments": {"planId": "private-nested-plan"}}):
            with self.subTest(fields=fields):
                context = self.context() | {"schemaVersion": 4, "allowedOperations": ["invite"],
                                            "social": {"planId": "host-plan"}}
                self.assert_candidate_error({"schemaVersion": 4, "operation": "invite"} | fields,
                                            context, "candidate.plan_id_required")

    def test_supported_context_versions_reach_the_provider_and_return_matching_candidates(self):
        for version in (1, 2, 3, 4):
            with self.subTest(schemaVersion=version):
                sent = []
                context = self.context() | {"schemaVersion": version}
                if version == 4:
                    context["selfAssessment"] = {"role": "buyer", "canMeetKnownTerms": False,
                                                 "missingCoins": 15}
                candidate = {"schemaVersion": version, "operation": "wait"}

                def provider(payload):
                    sent.append(json.loads(payload["messages"][1]["content"]))
                    return {"choices": [{"message": {"content": json.dumps(candidate)}}]}

                self.assertEqual(ProxyService(provider).decide(context), candidate)
                self.assertEqual(sent, [context])

    def test_v4_resource_candidates_cannot_bypass_host_allowed_operations(self):
        for operation in ("invite", "accept_invite", "deliver"):
            with self.subTest(operation=operation):
                candidate = {"schemaVersion": 4, "operation": operation,
                             "planId": "proposed-plan", "meetingId": "accepted-meeting"}
                service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(candidate)}}]})
                context = self.context() | {"schemaVersion": 4, "allowedOperations": ["wait", "decline_invite", "cancel_exchange"]}
                with self.assertRaisesRegex(ProxyError, "provider.candidate_invalid"):
                    service.decide(context)

    def test_resource_candidate_preserves_only_the_accepted_meeting_identifier(self):
        for operation in ("deliver", "cancel_exchange"):
            with self.subTest(operation=operation):
                candidate = {"schemaVersion": 3, "operation": operation, "meetingId": "11111111111111111111111111111111",
                             "buyerId": "player", "quantity": 999, "totalPrice": 0}
                service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(candidate)}}]})
                context = self.context() | {"schemaVersion": 3, "allowedOperations": ["deliver", "cancel_exchange"],
                                            "social": {"meetingId": "11111111111111111111111111111111"}}
                self.assertEqual(service.decide(context), {"schemaVersion": 3, "operation": operation,
                                                         "meetingId": "11111111111111111111111111111111"})

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
        for context in (None, [], {}, self.context() | {"schemaVersion": 5},
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

    def assert_candidate_error(self, candidate, context, expected_code):
        trace = io.StringIO()
        service = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(candidate)}}]},
                               max_calls=1, trace=trace)
        with self.assertRaisesRegex(ProxyError, "provider.candidate_invalid") as caught:
            service.decide(context)
        self.assertEqual(caught.exception.status, 422)
        self.assertEqual(caught.exception.candidate_error_code, expected_code)
        self.assertEqual(service.measurements[0]["candidateErrorCode"], expected_code)
        self.assertEqual(json.loads(trace.getvalue())["candidateErrorCode"], expected_code)
        self.assertNotIn("candidate", service.measurements[0])
        self.assertEqual(service.status["attemptedProviderCalls"], 1)
        self.assertEqual(service.status["remainingCalls"], 0)


if __name__ == "__main__":
    unittest.main()
