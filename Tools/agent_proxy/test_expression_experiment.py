import copy
import hashlib
import json
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor

from decision_proxy import ProxyError
from expression_experiment import ExpressionExperiment


def context():
    return {"schemaVersion": 4, "npcId": "sora", "worldRunId": "1" * 32, "decisionId": "decision-1",
            "step": 1, "allowedOperations": ["say"], "social": {"meetingId": "2" * 32},
            "hasObservation": False, "unchangedFact": {"value": "unknown", "canExpress": False}}


def response(payload):
    request = json.loads(payload["messages"][1]["content"])
    candidate = {"schemaVersion": 4, "operation": "say", "meetingId": "2" * 32}
    candidate.update({"speechIntent": "express_wish", "factId": "@talk", "tone": "warm"}
                     if request["expression"]["mode"] == "structured_facts" else {"text": "I would like to talk with you."})
    return {"choices": [{"message": {"content": json.dumps(candidate)}}]}


class ExpressionExperimentTests(unittest.TestCase):
    def test_background_server_can_stop_via_a_local_file_without_spending_unused_calls(self):
        from expression_experiment import main
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus = root / "cases.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(index)} for index in range(8)]}))
            stop = root / "stop"
            stop.touch()
            main(["--stage", "fixed", "--corpus", str(corpus), "--output-dir", str(root / "fixed"),
                  "--base-port", "0", "--stop-file", str(stop)], transport=response)
            status = json.loads((root / "fixed" / "status.json").read_text())
            self.assertEqual(status["runState"], "stopped")
            self.assertEqual(status["attemptedProviderCalls"], 0)

    def test_both_arms_share_the_existing_two_provider_slot_limit(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus = root / "cases.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(index)} for index in range(8)]}))
            release = threading.Event()
            entered = [threading.Event(), threading.Event()]
            def provider(payload):
                mode = json.loads(payload["messages"][1]["content"])["expression"]["mode"]
                entered[0 if mode == "free_text" else 1].set()
                release.wait(3)
                return response(payload)
            with ExpressionExperiment(provider, root / "fixed", corpus, stage="fixed") as experiment:
                with ThreadPoolExecutor(max_workers=2) as workers:
                    first = workers.submit(experiment.decide, "F", context())
                    second = workers.submit(experiment.decide, "S", context())
                    try:
                        self.assertTrue(all(event.wait(2) for event in entered))
                        with self.assertRaisesRegex(ProxyError, "proxy.busy"):
                            experiment.decide("F", context())
                        self.assertEqual(experiment.status["attemptedProviderCalls"], 2)
                    finally:
                        release.set()
                    self.assertIn("text", first.result(timeout=3))
                    self.assertIn("speechIntent", second.result(timeout=3))
            starts = [json.loads(line) for line in (root / "fixed" / "provider-starts.jsonl").read_text().splitlines()]
            self.assertEqual({record["request"] for record in starts}, {1, 2})

    def test_scene_requires_completed_fixed_evidence_with_matching_usage_records_and_frozen_inputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus = root / "cases.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(index)} for index in range(8)]}))
            fixed = root / "fixed"
            with ExpressionExperiment(response, fixed, corpus, stage="fixed") as experiment:
                for index in range(48):
                    experiment.decide("F" if index % 2 == 0 else "S", context())
            starts_path = fixed / "provider-starts.jsonl"
            starts = starts_path.read_text()
            starts_path.write_text("\n".join(starts.splitlines()[1:]) + "\n")
            with self.assertRaises(ValueError):
                with ExpressionExperiment(response, root / "missing-call", corpus, stage="scene", fixed_evidence_dir=fixed):
                    pass
            self.assertFalse((root / "missing-call").exists())
            starts_path.write_text(starts)
            before = corpus.read_bytes()
            corpus.write_text(json.dumps({"cases": [{"id": "changed-" + str(index)} for index in range(8)]}))
            with self.assertRaises(ValueError):
                ExpressionExperiment(response, root / "changed-corpus", corpus, stage="scene", fixed_evidence_dir=fixed)
            corpus.write_bytes(before)
            status_path = fixed / "status.json"
            status = json.loads(status_path.read_text())
            status_path.write_text(json.dumps(status | {"runState": "stopped"}))
            with self.assertRaises(ValueError):
                ExpressionExperiment(response, root / "stopped-fixed", corpus, stage="scene", fixed_evidence_dir=fixed)

    def test_stopping_waits_for_inflight_raw_evidence_and_rejects_new_requests(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus = root / "cases.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(index)} for index in range(8)]}))
            started, release = threading.Event(), threading.Event()
            def provider(payload):
                started.set()
                release.wait(3)
                return response(payload)
            with ExpressionExperiment(provider, root / "fixed", corpus, stage="fixed") as experiment:
                with ThreadPoolExecutor(max_workers=2) as workers:
                    pending = workers.submit(experiment.decide, "F", context())
                    self.assertTrue(started.wait(2))
                    stopping = workers.submit(experiment.close)
                    try:
                        self.assertFalse(stopping.done())
                    finally:
                        release.set()
                    self.assertIn("text", pending.result(timeout=3))
                    stopping.result(timeout=3)
                with self.assertRaisesRegex(ProxyError, "experiment.stopped"):
                    experiment.decide("S", context())
            status = json.loads((root / "fixed" / "status.json").read_text())
            self.assertEqual(status["attemptedProviderCalls"], 1)
            self.assertEqual(status["finishedDecisionRequests"], 1)
            self.assertEqual(len((root / "fixed" / "provider-responses.jsonl").read_text().splitlines()), 1)

    def test_provider_attempt_is_flushed_before_dispatch_so_interruption_does_not_hide_usage(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus = root / "cases.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(index)} for index in range(8)]}))
            output = root / "fixed"
            def provider(payload):
                starts = [json.loads(line) for line in (output / "provider-starts.jsonl").read_text().splitlines()]
                self.assertEqual(len(starts), 1)
                self.assertEqual(starts[0]["request"], 1)
                status = json.loads((output / "status.json").read_text())
                self.assertEqual(status["attemptedProviderCalls"], 1)
                self.assertEqual(status["decisionRequests"], 1)
                return response(payload)
            with ExpressionExperiment(provider, output, corpus, stage="fixed") as experiment:
                self.assertIn("text", experiment.decide("F", context()))
            raw = json.loads((output / "provider-responses.jsonl").read_text())
            self.assertEqual(raw["request"], 1)

    def test_cli_freezes_repository_sources_and_stops_without_reading_credentials_when_transport_is_injected(self):
        from expression_experiment import main
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus = root / "cases.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(index)} for index in range(8)]}))
            stop = threading.Event()
            stop.set()
            calls = []
            main(["--mode", "serve", "--stage", "fixed", "--corpus", str(corpus),
                  "--output-dir", str(root / "fixed"), "--base-port", "0"], transport=lambda payload: calls.append(payload), stop_event=stop)
            self.assertEqual(calls, [])
            manifest = json.loads((root / "fixed" / "manifest.json").read_text())
            self.assertEqual(len(manifest["gitCommit"]), 40)
            self.assertEqual(len(manifest["sourceDiffSha256"]), 64)
            self.assertIn("Assets/CozyTown/Runtime/NpcAgents/NpcFactSpeech.cs", manifest["sourceSha256"])
            self.assertIn("Assets/CozyTown/Unity/Npc/ProxyNpcDecisionJsonCodec.cs", manifest["sourceSha256"])
            self.assertIn("Assets/CozyTown/Tests/PlayMode/NpcResourceScenarioPlayModeTests.cs", manifest["sourceSha256"])
            self.assertIn("docs/verification/npc-expression-comparison-plan-2026-09-14.md", manifest["sourceSha256"])
            self.assertEqual(json.loads((root / "fixed" / "status.json").read_text())["runState"], "stopped")

    def test_two_loopback_ports_share_the_budget_and_keep_candidate_diagnostics(self):
        from expression_experiment import create_experiment_servers
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus = root / "cases.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(index)} for index in range(8)]}))
            def provider(payload):
                answer = response(payload)
                if json.loads(payload["messages"][1]["content"])["expression"]["mode"] == "structured_facts":
                    answer["choices"][0]["message"]["content"] = '{"schemaVersion":4,"operation":"say","meetingId":"' + '2' * 32 + '"}'
                return answer
            with ExpressionExperiment(provider, root / "fixed", corpus, stage="fixed") as experiment:
                servers = create_experiment_servers(experiment, base_port=0)
                workers = [threading.Thread(target=server.serve_forever, daemon=True) for server in servers]
                for worker in workers:
                    worker.start()
                try:
                    for index, server in enumerate(servers):
                        self.assertEqual(server.server_address[0], "127.0.0.1")
                        request = urllib.request.Request(f"http://127.0.0.1:{server.server_port}/decide",
                            data=json.dumps(context()).encode(), headers={"Content-Type": "application/json"})
                        if index == 0:
                            with urllib.request.urlopen(request) as reply:
                                self.assertIn("text", json.load(reply))
                        else:
                            with self.assertRaises(urllib.error.HTTPError) as caught:
                                urllib.request.urlopen(request)
                            self.assertEqual(caught.exception.code, 422)
                            self.assertEqual(json.load(caught.exception)["candidateErrorCode"], "candidate.speech_frame_invalid")
                    for arm, server in zip(("F", "S"), servers):
                        with urllib.request.urlopen(f"http://127.0.0.1:{server.server_port}/status") as reply:
                            status = json.load(reply)
                        self.assertEqual(status["remainingCalls"], 46)
                        self.assertEqual(status["arm"], arm)
                finally:
                    for server in servers:
                        server.shutdown()
                        server.server_close()
                    for worker in workers:
                        worker.join(timeout=2)

    def test_scene_shares_48_calls_across_four_worlds_and_cannot_reuse_a_fixed_stage(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus = root / "cases.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(index)} for index in range(8)]}))
            fixed = root / "fixed"
            with ExpressionExperiment(response, fixed, corpus, stage="fixed") as experiment:
                for index in range(48):
                    experiment.decide("F" if index % 2 == 0 else "S", context())
            with ExpressionExperiment(response, root / "scene", corpus, stage="scene", fixed_evidence_dir=fixed) as experiment:
                first = context()
                experiment.decide("F", first)
                with self.assertRaisesRegex(ProxyError, "experiment.world_arm_mismatch"):
                    experiment.decide("S", first)
                for _ in range(11):
                    experiment.decide("F", first)
                with self.assertRaisesRegex(ProxyError, "experiment.world_call_limit"):
                    experiment.decide("F", first)
                for world, arm in (("3" * 32, "S"), ("4" * 32, "F"), ("5" * 32, "S")):
                    experiment.decide(arm, context() | {"worldRunId": world})
                with self.assertRaisesRegex(ProxyError, "experiment.world_limit"):
                    experiment.decide("F", context() | {"worldRunId": "6" * 32})
                for world, arm in (("3" * 32, "S"), ("4" * 32, "F"), ("5" * 32, "S")):
                    for _ in range(11):
                        experiment.decide(arm, context() | {"worldRunId": world})
                self.assertEqual(experiment.status["attemptedProviderCalls"], 48)
                self.assertEqual(experiment.status["totalAttemptedProviderCalls"], 96)
                self.assertEqual(len(experiment.status["worlds"]), 4)
            manifest = json.loads((root / "scene" / "manifest.json").read_text())
            self.assertEqual(manifest["fixedEvidence"]["directory"], "../fixed")
            claim = json.loads((fixed / "scene-claim.json").read_text())
            self.assertEqual(claim["sceneOutputDirectory"], "../scene")
            with self.assertRaises(FileExistsError):
                ExpressionExperiment(response, root / "scene-restart", corpus, stage="scene", fixed_evidence_dir=fixed)

    def test_fixed_stage_keeps_failures_and_stops_at_48_requests_without_replenishing_them(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus = root / "cases.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(index)} for index in range(8)]}))
            calls = []
            def provider(payload):
                calls.append(payload)
                if len(calls) == 1:
                    return {"choices": [{"message": {"content": '{"schemaVersion":4,"operation":"say","meetingId":"' + '2' * 32 + '","text":"keep-invalid-raw","factId":"@talk"}'}}]}
                if len(calls) == 2:
                    raise ProxyError("provider.transport_failure")
                return response(payload)
            with ExpressionExperiment(provider, root / "fixed", corpus, stage="fixed") as experiment:
                for index in range(48):
                    try:
                        experiment.decide("F", context())
                    except ProxyError:
                        self.assertLess(index, 2)
                with self.assertRaisesRegex(ProxyError, "experiment.call_limit"):
                    experiment.decide("S", context())
                self.assertEqual(experiment.status["decisionRequests"], 48)
                self.assertEqual(experiment.status["attemptedProviderCalls"], 48)
            output = root / "fixed"
            records = [json.loads(line) for line in (output / "contexts.jsonl").read_text().splitlines()]
            raw = [json.loads(line) for line in (output / "provider-responses.jsonl").read_text().splitlines()]
            self.assertEqual(len(raw), 48)
            self.assertIn("keep-invalid-raw", raw[0]["content"])
            self.assertEqual(records[0]["error"]["candidateErrorCode"], "candidate.expression_mode_mismatch")
            self.assertEqual(records[1]["status"], "provider.transport_failure")
            self.assertEqual(records[-1]["status"], "experiment.call_limit")
            self.assertEqual(json.loads((output / "status.json").read_text())["runState"], "completed")
            with self.assertRaises(FileExistsError):
                ExpressionExperiment(provider, output, corpus, stage="fixed")
            self.assertEqual(len(calls), 48)

    def test_paired_requests_preserve_context_and_write_frozen_inputs_and_separate_responses(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            corpus = root / "cases.json"
            corpus.write_text(json.dumps({"cases": [{"id": str(index)} for index in range(8)]}), encoding="utf-8")
            output = root / "fixed"
            original = context()
            before = copy.deepcopy(original)
            sent = []
            def provider(payload):
                sent.append(payload)
                return response(payload)
            with ExpressionExperiment(provider, output, corpus, stage="fixed") as experiment:
                self.assertIn("text", experiment.decide("F", original))
                self.assertIn("speechIntent", experiment.decide("S", original))
                self.assertEqual(experiment.status["attemptedProviderCalls"], 2)
            self.assertEqual(original, before)
            records = [json.loads(line) for line in (output / "contexts.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertEqual([record["arm"] for record in records], ["F", "S"])
            self.assertEqual(records[0]["normalizedContextSha256"], records[1]["normalizedContextSha256"])
            for record in records:
                self.assertEqual(record["originalContext"], before)
                effective = record["effectiveContext"].copy()
                self.assertEqual(effective.pop("expression")["schemaVersion"], 1)
                self.assertEqual(effective, before)
            raw = [json.loads(line) for line in (output / "provider-responses.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertEqual([record["arm"] for record in raw], ["F", "S"])
            self.assertEqual(json.loads(raw[1]["content"])["factId"], "@talk")
            manifest = json.loads((output / "manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(manifest["maxCalls"], 48)
            self.assertEqual(manifest["corpusSha256"], hashlib.sha256(corpus.read_bytes()).hexdigest())
            self.assertEqual(manifest["plannedTotalCallCeiling"], 96)
            self.assertEqual(set(manifest["systemPromptSha256"]), {"F", "S"})
            self.assertIn("decision_proxy.py", manifest["sourceSha256"])
            self.assertEqual(json.loads((output / "status.json").read_text())["decisionRequests"], 2)


if __name__ == "__main__":
    unittest.main()
