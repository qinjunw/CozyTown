import copy
import io
import json
import unittest
import tempfile
import threading
import urllib.request
import urllib.error
from pathlib import Path

from decision_proxy import ProxyService, ProxyError, SYSTEM_PROMPT
from grounding_experiment import project_context, ExperimentService, run_fixed, create_experiment_servers, main


def context():
    return {"schemaVersion": 3, "npcId": "sora", "decisionId": "decision-1", "worldRunId": "world-1",
            "step": 1, "gameTotalMinutes": 735, "targetLocationId": "sora-home",
            "allowedOperations": ["invite", "wait"], "social": {
                "kind": "opportunity", "planId": "fish-plan", "partnerId": "ren", "placeId": "pond",
                "meetingId": "11111111111111111111111111111111", "transcript": [], "memories": [],
                "resources": {"sellerId": "ren", "buyerId": "sora", "itemId": "carp", "quantity": 1,
                              "totalPrice": 25, "ownedQuantity": 0, "balance": 10, "deliveryResultCode": ""}}}


class GroundingExperimentTests(unittest.TestCase):
    def test_control_preserves_production_context_and_does_not_mutate_input(self):
        original = context()
        original["experimentObservation"] = {"partner": {"position": {"x": 999, "y": 999}}}
        before = copy.deepcopy(original)
        projected = project_context(original, "A")
        self.assertEqual(projected, context())
        projected["social"]["resources"]["balance"] = 900
        self.assertEqual(original, before)

    def test_grounding_distinguishes_targets_claims_and_fresh_self_observation(self):
        original = context()
        original["social"]["transcript"] = [{"speakerId": "ren", "text": "There are herbs beside the pond."}]
        original["experimentObservation"] = {
            "worldRunId": "world-1", "gameTotalMinutes": 735,
            "self": {"position": {"x": 1, "y": 2}, "routeStatus": "Moving", "targetLocationId": "sora-home"},
            "partner": {"npcId": "ren", "position": {"x": 999, "y": 999}},
            "meetingPlaceId": "pond", "meetingBothArrived": False}
        grounded = project_context(original, "B")
        facts = grounded["grounding"]
        self.assertEqual(facts["timeOfDay"], "12:15")
        self.assertEqual(facts["selfObservation"]["position"], {"x": 1, "y": 2})
        self.assertEqual(facts["plannedMeetingLocationId"], "pond")
        self.assertEqual(facts["transcriptEvidenceType"], "speaker_claim")
        self.assertFalse(facts["meetingArrivalConfirmed"])
        self.assertIn("nearby_harvestable_resources", facts["unknowns"])
        self.assertNotIn("999", json.dumps(grounded))
        self.assertNotIn("experimentObservation", grounded)
        self.assertNotIn("selfAssessment", grounded)
        self.assertEqual(grounded["allowedOperations"], ["invite", "wait"])
        original["experimentObservation"]["worldRunId"] = "previous-world"
        stale = project_context(original, "B")["grounding"]
        self.assertNotIn("selfObservation", stale)
        self.assertIsNone(stale["meetingArrivalConfirmed"])

    def test_missing_observation_keeps_arrival_unknown_and_empty_resource_payload_has_no_assessment(self):
        request = context()
        self.assertIsNone(project_context(request, "B")["grounding"]["meetingArrivalConfirmed"])
        request["experimentObservation"] = {"worldRunId": "world-1", "gameTotalMinutes": 735,
                                             "meetingPlaceId": "pond", "meetingBothArrived": True}
        self.assertTrue(project_context(request, "B")["grounding"]["meetingArrivalConfirmed"])
        request["experimentObservation"]["gameTotalMinutes"] = 734
        self.assertIsNone(project_context(request, "B")["grounding"]["meetingArrivalConfirmed"])
        request["social"]["resources"] = {"sellerId": "", "buyerId": "", "itemId": "", "quantity": 0, "balance": 0}
        self.assertNotIn("selfAssessment", project_context(request, "C"))

    def test_assessment_uses_only_own_known_terms_and_skips_completed_delivery(self):
        buyer = project_context(context(), "C")
        self.assertEqual(buyer["selfAssessment"]["role"], "buyer")
        self.assertEqual(buyer["selfAssessment"]["missingCoins"], 15)
        self.assertFalse(buyer["selfAssessment"]["canMeetKnownTerms"])
        self.assertEqual(buyer["allowedOperations"], ["invite", "wait"])
        seller = context()
        seller["npcId"] = "ren"
        seller["social"]["resources"]["balance"] = 0
        seller["social"]["resources"]["ownedQuantity"] = 2
        assessment = project_context(seller, "C")["selfAssessment"]
        self.assertEqual(assessment["role"], "seller")
        self.assertTrue(assessment["canMeetKnownTerms"])
        self.assertEqual(assessment["missingQuantity"], 0)
        self.assertNotIn("missingCoins", assessment)
        seller["social"]["resources"]["ownedQuantity"] = 0
        self.assertEqual(project_context(seller, "C")["selfAssessment"]["missingQuantity"], 1)
        seller["social"]["kind"] = "conversation"
        seller["social"]["resources"]["deliveryResultCode"] = "resource.delivered"
        self.assertNotIn("selfAssessment", project_context(seller, "C"))
        del seller["social"]["resources"]["ownedQuantity"]
        seller["social"]["kind"] = "invitation"
        seller["social"]["resources"]["deliveryResultCode"] = ""
        self.assertIsNone(project_context(seller, "C")["selfAssessment"]["canMeetKnownTerms"])

    def test_filter_removes_only_commitments_blocked_by_known_own_resources(self):
        original = context()
        original["allowedOperations"] = ["invite", "accept_invite", "deliver", "wait", "decline_invite", "cancel_exchange"]
        filtered = project_context(original, "D")
        self.assertEqual(filtered["allowedOperations"], ["wait", "decline_invite", "cancel_exchange"])
        self.assertEqual(filtered["grounding"]["allowedActions"], filtered["allowedOperations"])
        original["social"]["resources"]["balance"] = 25
        self.assertEqual(project_context(original, "D")["allowedOperations"], original["allowedOperations"])

    def test_buyer_balance_boundaries_preserve_or_filter_each_commitment(self):
        for kind, operation in (("opportunity", "invite"), ("delivery", "deliver")):
            for balance, eligible, missing in ((24, False, 1), (25, True, 0), (26, True, 0)):
                for arm in ("C", "D"):
                    with self.subTest(kind=kind, balance=balance, arm=arm):
                        request = context()
                        request["social"]["kind"] = kind
                        request["social"]["resources"]["balance"] = balance
                        request["allowedOperations"] = [operation, "wait", "cancel_exchange"]
                        before = copy.deepcopy(request)
                        projected = project_context(request, arm)
                        self.assertEqual(projected["selfAssessment"]["canMeetKnownTerms"], eligible)
                        self.assertEqual(projected["selfAssessment"]["missingCoins"], missing)
                        expected = request["allowedOperations"] if arm == "C" or eligible else ["wait", "cancel_exchange"]
                        self.assertEqual(projected["allowedOperations"], expected)
                        self.assertEqual(projected["grounding"]["allowedActions"], expected)
                        self.assertEqual(request, before)

    def test_seller_stock_boundaries_do_not_depend_on_seller_wallet(self):
        for stock, eligible, missing in ((0, False, 1), (1, True, 0), (2, True, 0)):
            for arm in ("C", "D"):
                with self.subTest(stock=stock, arm=arm):
                    request = context()
                    request["npcId"] = "ren"
                    request["social"]["kind"] = "invitation"
                    request["social"]["resources"].update(ownedQuantity=stock, balance=0)
                    request["allowedOperations"] = ["accept_invite", "decline_invite"]
                    projected = project_context(request, arm)
                    self.assertEqual(projected["selfAssessment"]["canMeetKnownTerms"], eligible)
                    self.assertEqual(projected["selfAssessment"]["missingQuantity"], missing)
                    self.assertNotIn("missingCoins", projected["selfAssessment"])
                    expected = request["allowedOperations"] if arm == "C" or eligible else ["decline_invite"]
                    self.assertEqual(projected["allowedOperations"], expected)

    def test_service_preserves_control_prompt_and_records_filtered_call_separately(self):
        sent = []
        def provider(payload):
            sent.append(payload)
            return {"choices": [{"message": {"content": '{"schemaVersion":3,"operation":"wait"}'}}]}
        trace = io.StringIO()
        proxy = ProxyService(provider, max_calls=2)
        original = context()
        ExperimentService(proxy, "A", trace).decide(original)
        ExperimentService(proxy, "D", trace).decide(original)
        self.assertEqual(len(sent), 2)
        self.assertEqual(sent[0]["messages"][0]["content"], SYSTEM_PROMPT)
        self.assertEqual(json.loads(sent[0]["messages"][1]["content"]), original)
        self.assertEqual(sent[0]["max_tokens"], sent[1]["max_tokens"])
        records = [json.loads(line) for line in trace.getvalue().splitlines()]
        self.assertEqual(records[0]["filteredOperations"], [])
        self.assertEqual(records[1]["filteredOperations"], ["invite"])
        self.assertEqual(records[1]["originalContext"], original)
        self.assertEqual(records[1]["effectiveContext"]["allowedOperations"], ["wait"])
        self.assertNotIn("arm", records[1]["effectiveContext"])
        self.assertFalse(records[1]["protocolInvalid"])
        self.assertEqual(proxy.status["remainingCalls"], 0)

    def test_service_diagnoses_candidate_fields_without_repair_or_retry(self):
        for candidate, expected_issue in (
            ({"schemaVersion": 3, "operation": "invite"}, "planId.required"),
            ({"schemaVersion": 3, "operation": "invite", "planId": 5}, "planId.required"),
            ({"schemaVersion": 3, "operation": "say", "meetingId": "bad", "text": "hello"}, "meetingId.guid"),
            ({"schemaVersion": 3, "operation": "say", "meetingId": "1" * 32, "text": "x" * 241}, "text.length"),
        ):
            with self.subTest(candidate=candidate):
                trace = io.StringIO()
                proxy = ProxyService(lambda payload: {"choices": [{"message": {"content": json.dumps(candidate)}}]})
                request = context()
                request["allowedOperations"] = [candidate["operation"]]
                reply = ExperimentService(proxy, "B", trace).decide(request)
                self.assertEqual(reply, candidate)
                record = json.loads(trace.getvalue())
                self.assertTrue(record["protocolInvalid"])
                self.assertIn(expected_issue, record["protocolIssues"])
                self.assertEqual(proxy.status["attemptedProviderCalls"], 1)

    def test_service_preserves_provider_error_and_consumed_budget(self):
        def unavailable(payload):
            raise RuntimeError("private exception data")
        trace = io.StringIO()
        proxy = ProxyService(unavailable, max_calls=1)
        service = ExperimentService(proxy, "C", trace)
        with self.assertRaisesRegex(ProxyError, "provider.failure"):
            service.decide(context())
        self.assertEqual(json.loads(trace.getvalue())["error"]["code"], "provider.failure")
        self.assertNotIn("private exception data", trace.getvalue())
        self.assertEqual(service.status["remainingCalls"], 0)

    def test_fixed_corpus_runs_160_fresh_paired_calls_and_retains_failures(self):
        sent = []
        def provider(payload):
            sent.append(json.loads(payload["messages"][1]["content"]))
            if len(sent) == 1:
                raise ProxyError("provider.transport_failure")
            return {"choices": [{"message": {"content": '{"schemaVersion":3,"operation":"wait"}'}}]}
        corpus = {"cases": [{"id": f"case-{i}", "category": "buyer", "context": context(),
                             "expected": {"answer": "not sent"}} for i in range(8)]}
        before = copy.deepcopy(corpus)
        trace = io.StringIO()
        proxy = ProxyService(provider, max_calls=160)
        run_fixed(proxy, corpus, trace)
        records = [json.loads(line) for line in trace.getvalue().splitlines()]
        self.assertEqual(len(sent), 160)
        self.assertEqual(len({request["decisionId"] for request in sent}), 160)
        self.assertEqual(len({request["worldRunId"] for request in sent}), 40)
        self.assertEqual([record["arm"] for record in records[:8]], ["A", "B", "C", "D", "B", "C", "D", "A"])
        self.assertEqual(len({request["worldRunId"] for request in sent[:4]}), 1)
        self.assertEqual(records[0]["error"]["code"], "provider.transport_failure")
        self.assertEqual(records[-1]["stage"], "fixed:case-7:repeat:5")
        self.assertNotIn("not sent", json.dumps(sent))
        self.assertTrue(all(request["social"]["transcript"] == [] for request in sent))
        self.assertEqual(corpus, before)

    def test_four_loopback_arms_share_one_provider_budget(self):
        proxy = ProxyService(lambda payload: {"choices": [{"message": {
            "content": '{"schemaVersion":3,"operation":"wait"}'}}]}, max_calls=3)
        trace = io.StringIO()
        servers = create_experiment_servers(proxy, trace, 0)
        workers = [threading.Thread(target=server.serve_forever, daemon=True) for server in servers]
        for worker in workers:
            worker.start()
        try:
            for index, server in enumerate(servers):
                address = f"http://127.0.0.1:{server.server_port}"
                request = urllib.request.Request(address + "/decide", data=json.dumps(context()).encode(),
                                                 headers={"Content-Type": "application/json"})
                if index < 3:
                    with urllib.request.urlopen(request, timeout=2) as response:
                        self.assertEqual(json.load(response)["operation"], "wait")
                else:
                    with self.assertRaises(urllib.error.HTTPError) as caught:
                        urllib.request.urlopen(request, timeout=2)
                    self.assertEqual(caught.exception.code, 429)
                    caught.exception.close()
                with urllib.request.urlopen(address + "/status", timeout=2) as response:
                    self.assertEqual(json.load(response)["attemptedProviderCalls"], min(index + 1, 3))
        finally:
            for server in servers:
                server.shutdown()
                server.server_close()
            for worker in workers:
                worker.join(timeout=2)


    def test_fixed_cli_writes_new_evidence_and_never_overwrites_an_existing_directory(self):
        sent = []
        def provider(payload):
            sent.append(payload)
            return {"model": "deepseek-flash", "usage": {"total_tokens": 40}, "choices": [{"message": {
                "content": '{"schemaVersion":3,"operation":"wait","extraField":"raw-evidence"}',
                "reasoning_content": "private reasoning"}}]}
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            credential = root / "key.json"
            credential.write_text(json.dumps({"test": {"Todo": "CozyTown", "testingAPIKey": "fake-test-key"}}))
            corpus = root / "corpus.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(i), "context": context()} for i in range(8)]}))
            output = root / "evidence"
            argv = ["--mode", "fixed", "--credential-file", str(credential), "--output-dir", str(output),
                    "--corpus", str(corpus), "--max-calls", "160"]
            main(argv, transport=provider)
            status = json.loads((output / "status.json").read_text())
            self.assertEqual(status["attemptedProviderCalls"], 160)
            self.assertEqual(status["inflight"], 0)
            self.assertEqual(json.loads((output / "manifest.json").read_text())["plannedCalls"], 160)
            raw = (output / "provider-responses.jsonl").read_text()
            self.assertTrue(all(row["step"] == 1 for row in map(json.loads, raw.splitlines())))
            self.assertIn("raw-evidence", raw)
            self.assertNotIn("private reasoning", raw)
            combined = "".join(file.read_text() for file in output.iterdir())
            self.assertNotIn("fake-test-key", combined)
            self.assertEqual(len((output / "proxy.jsonl").read_text().splitlines()), 160)
            self.assertEqual(len((output / "contexts.jsonl").read_text().splitlines()), 160)
            with self.assertRaises(SystemExit):
                main(argv, transport=provider)
            self.assertEqual(len(sent), 160)


if __name__ == "__main__":
    unittest.main()
