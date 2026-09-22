import json
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request
from pathlib import Path
from contextlib import ExitStack
from concurrent.futures import ThreadPoolExecutor

from decision_proxy import ProxyError
from run_four_agent_integration import FourAgentIntegrationRunner


class FourAgentIntegrationRunnerTests(unittest.TestCase):
    def test_hung_provider_cleanup_aborts_within_its_own_deadline_and_retains_late_evidence(self):
        for stop_kind in ("close", "deadline", "result"):
            with self.subTest(stop=stop_kind), ExitStack() as cleanup:
                temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
                directory = Path(temporary) / "study"
                entered, release, stopped = threading.Event(), threading.Event(), threading.Event()
                now, responses, errors = [0], [], []
                def provider(payload):
                    entered.set()
                    if not release.wait(timeout=5):
                        raise AssertionError("The fixture provider was not released.")
                    return {"choices": [{"message": {"content": '{"schemaVersion":4,"operation":"wait"}'}}]}
                runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                    evaluation_plan_reference="frozen-plan-v1", stage="live", transport=provider,
                    clock=lambda: now[0], cleanup_timeout_seconds=0.05)
                cleanup.callback(runner.close)
                runner.tick()
                options = self.active_options(directory)
                self.initialize(runner, options)
                world_dir = Path(options["outputDirectory"])
                request = {"schemaVersion": 4, "npcId": "npc.shopkeeper_mina", "allowedOperations": ["wait"],
                    "decisionId": "hung-fixture", "step": 1, "expression": {"schemaVersion": 1, "mode": "free_text"}}
                def send_request():
                    try: responses.append(self.http(options["proxyEndpoint"], request))
                    except Exception as error: errors.append(error)
                caller = threading.Thread(target=send_request, daemon=True)
                caller.start()
                stopper = None
                try:
                    self.assertTrue(entered.wait(timeout=2))
                    if stop_kind == "deadline":
                        now[0] = 660
                    elif stop_kind == "result":
                        self.write(world_dir / "result.json", {"schemaVersion": 1, "worldId": options["worldId"],
                            "status": "incomplete", "packageDirectory": "", "reason": "fixture.stopped",
                            "hostViolations": [], "completedGameMinutes": 720, "realSeconds": 10, "replayPassed": False})
                    def stop():
                        try: runner.close() if stop_kind == "close" else runner.tick()
                        except Exception as error: errors.append(error)
                        finally: stopped.set()
                    stopper = threading.Thread(target=stop, daemon=True)
                    stopper.start()
                    self.assertTrue(stopped.wait(timeout=1), "Cleanup must not wait indefinitely for the provider.")
                    self.assertEqual(errors, [])
                    status = self.read(directory / "status.json")
                    self.assertEqual(status["state"], "aborted")
                    self.assertEqual(status["worlds"][0]["status"], "host_failure")
                    self.assertEqual(status["worlds"][0]["reason"], "experiment.proxy_cleanup_timeout")
                    pending = status["worlds"][0]["proxyCleanup"]
                    self.assertFalse(pending["drained"])
                    self.assertEqual(pending["activeRequests"], 1)
                    self.assertEqual(status["attemptedProviderCalls"], 1)
                    self.assertEqual(sum(row["status"] == "not_started" for row in status["worlds"]), 15)
                    self.assertEqual(self.read(directory / "active-world.json")["state"], "aborted")
                    self.assertEqual(responses, [])
                    runner.tick()
                    self.assertEqual(self.active_options(directory)["ordinal"], 1)
                finally:
                    release.set()
                    caller.join(timeout=2)
                    if stopper is not None: stopper.join(timeout=2)
                self.assertEqual(errors, [])
                self.assertEqual(responses[0][0], 200)
                trace = [json.loads(line) for line in (world_dir / "proxy.jsonl").read_text(encoding="utf-8").splitlines()]
                self.assertEqual(len(trace), 1)
                self.assertEqual(trace[0]["decisionId"], "hung-fixture")

    def test_request_admitted_before_deadline_cannot_begin_a_provider_call_after_it(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            directory = Path(temporary) / "study"
            times, calls = [100], []
            def clock():
                return times.pop(0) if len(times) > 1 else times[0]
            def provider(payload):
                calls.append(payload)
                return {"choices": [{"message": {"content": '{"schemaVersion":4,"operation":"wait"}'}}]}
            runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                evaluation_plan_reference="frozen-plan-v1", stage="live", transport=provider, clock=clock)
            cleanup.callback(runner.close)
            runner.tick()
            options = self.active_options(directory)
            self.initialize(runner, options)
            times[:] = [699.9, 700]
            request = {"schemaVersion": 4, "npcId": "npc.shopkeeper_mina", "allowedOperations": ["wait"],
                "decisionId": "fixture", "step": 1, "expression": {"schemaVersion": 1, "mode": "free_text"}}
            code, body = self.http(options["proxyEndpoint"], request)
            self.assertEqual((code, body["error"] if "error" in body else None), (503, "experiment.deadline"))
            self.assertEqual(calls, [])
            self.assertEqual(self.read(directory / "status.json")["attemptedProviderCalls"], 0)

    def test_unity_mina_social_request_uses_its_actual_top_level_protocol_without_losing_observation(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            directory = Path(temporary) / "study"
            request = self.read(Path(__file__).parent / "fixtures" / "four_agent_mina_social_request.json")
            self.assertEqual(request["schemaVersion"], 2)
            self.assertTrue(request["hasObservation"])
            calls = []
            def provider(payload):
                actual = json.loads(payload["messages"][1]["content"])
                calls.append(actual)
                return {"choices": [{"message": {"content": json.dumps({
                    "schemaVersion": actual["schemaVersion"], "operation": "wait"})}}]}
            runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                evaluation_plan_reference="frozen-plan-v1", stage="live", transport=provider)
            cleanup.callback(runner.close)
            runner.tick()
            options = self.active_options(directory)
            self.initialize(runner, options)
            code, body = self.http(options["proxyEndpoint"], request)
            self.assertEqual((code, body), (200, {"schemaVersion": 2, "operation": "wait"}))
            self.assertEqual(calls, [request])
            self.assertEqual(self.read(directory / "status.json")["attemptedProviderCalls"], 1)

    def test_live_requires_a_candidate_sha_and_registered_plan_before_creating_a_study(self):
        with tempfile.TemporaryDirectory() as temporary:
            for revision, reference in (("unknown", "frozen-plan-v1"), ("a" * 40, "none")):
                with self.subTest(revision=revision, reference=reference):
                    directory = Path(temporary) / reference
                    with self.assertRaises(ValueError):
                        FourAgentIntegrationRunner(directory, source_revision=revision,
                            evaluation_plan_reference=reference, stage="live", transport=lambda payload: {})
                    self.assertFalse(directory.exists())

    def test_two_physical_requests_share_the_proxy_while_a_third_is_rejected_without_provider_spend(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            directory = Path(temporary) / "study"
            release, both_started = threading.Event(), threading.Event()
            calls = []
            lock = threading.Lock()
            def provider(payload):
                with lock:
                    calls.append(payload)
                    if len(calls) == 2:
                        both_started.set()
                if not release.wait(timeout=5):
                    raise AssertionError("The fixture provider was not released.")
                return {"choices": [{"message": {"content": '{"schemaVersion":4,"operation":"wait"}'}}]}
            runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                evaluation_plan_reference="frozen-plan-v1", stage="live", transport=provider)
            cleanup.callback(runner.close)
            runner.tick()
            options = self.active_options(directory)
            self.initialize(runner, options)
            request = {"schemaVersion": 4, "npcId": "npc.shopkeeper_mina", "allowedOperations": ["wait"],
                "decisionId": "fixture", "step": 1, "expression": {"schemaVersion": 1, "mode": "free_text"}}
            with ThreadPoolExecutor(max_workers=2) as clients:
                first = clients.submit(self.http, options["proxyEndpoint"], request | {"decisionId": "one"})
                second = clients.submit(self.http, options["proxyEndpoint"], request | {"decisionId": "two"})
                try:
                    self.assertTrue(both_started.wait(timeout=2))
                    runner.tick()
                    code, body = self.http(options["proxyEndpoint"], request | {"decisionId": "three"})
                    self.assertEqual((code, body["error"]), (503, "proxy.busy"))
                    self.assertEqual(self.read(directory / "status.json")["attemptedProviderCalls"], 2)
                finally:
                    release.set()
                self.assertEqual(first.result(timeout=3)[0], 200)
                self.assertEqual(second.result(timeout=3)[0], 200)
            self.assertEqual(len(calls), 2)

    def test_sixteen_live_world_caps_total_five_hundred_twelve_attempts_with_no_budget_refund(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            directory = Path(temporary) / "study"
            calls = []
            def provider(payload):
                calls.append(payload)
                return {"choices": [{"message": {"content": '{"schemaVersion":4,"operation":"wait"}'}}]}
            runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                evaluation_plan_reference="frozen-plan-v1", stage="live", transport=provider)
            cleanup.callback(runner.close)
            runner.tick()
            for ordinal in range(1, 17):
                options = self.active_options(directory)
                self.assertEqual(options["ordinal"], ordinal)
                self.initialize(runner, options)
                request = {"schemaVersion": 4, "npcId": "npc.shopkeeper_mina", "allowedOperations": ["wait"],
                    "step": 1, "expression": {"schemaVersion": 1,
                        "mode": "free_text" if options["speechMode"] == "F" else "structured_facts"}}
                for call in range(32):
                    request["decisionId"] = str(ordinal) + "-" + str(call)
                    self.assertEqual(self.http(options["proxyEndpoint"], request)[0], 200)
                self.assertEqual(self.http(options["proxyEndpoint"], request)[0], 429)
                world_dir = Path(options["outputDirectory"])
                self.write(world_dir / "result.json", {"schemaVersion": 1, "worldId": options["worldId"],
                    "status": "incomplete", "packageDirectory": "", "reason": "fixture.budget_reached",
                    "hostViolations": [], "completedGameMinutes": 740, "realSeconds": 50, "replayPassed": False})
                runner.tick()
            status = self.read(directory / "status.json")
            self.assertEqual(status["state"], "finished")
            self.assertEqual(status["attemptedProviderCalls"], 512)
            self.assertEqual([world["attemptedProviderCalls"] for world in status["worlds"]], [32] * 16)
            events = [json.loads(line) for line in (directory / "ledger.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertEqual([row["providerCall"] for row in events if row["event"] == "provider_started"], list(range(1, 513)))
            runner.tick()
            self.assertEqual(len(calls), 512)

    def test_live_proxy_rejects_a_foreign_arm_protocol_or_resident_before_spending_budget(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            directory = Path(temporary) / "study"
            calls = []
            def provider(payload):
                calls.append(payload)
                return {"choices": [{"message": {"content": '{"schemaVersion":4,"operation":"wait"}'}}]}
            runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                evaluation_plan_reference="frozen-plan-v1", stage="live", transport=provider)
            cleanup.callback(runner.close)
            runner.tick()
            options = self.active_options(directory)
            self.initialize(runner, options)
            request = {"schemaVersion": 4, "npcId": "npc.shopkeeper_mina", "allowedOperations": ["wait"],
                "decisionId": "fixture", "step": 1, "expression": {"schemaVersion": 1, "mode": "free_text"}}
            for changed in (request | {"schemaVersion": 5}, request | {"npcId": "player"},
                    request | {"expression": {"schemaVersion": 1, "mode": "structured_facts"}}):
                code, body = self.http(options["proxyEndpoint"], changed)
                self.assertEqual(code, 400)
                self.assertIn(body["error"], ("experiment.context_mismatch", "proxy.context_invalid"))
            self.assertEqual(calls, [])
            self.assertEqual(self.read(directory / "status.json")["attemptedProviderCalls"], 0)

    def test_claimed_completion_requires_replay_clean_host_results_and_local_package_evidence(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            for damage in ("host-violation", "replay-failed", "outside-package", "missing-package", "bad-real-time"):
                with self.subTest(damage=damage):
                    directory = Path(temporary) / damage
                    runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                        evaluation_plan_reference="frozen-plan-v1")
                    cleanup.callback(runner.close)
                    runner.tick()
                    options = self.active_options(directory)
                    self.initialize(runner, options)
                    world_dir = Path(options["outputDirectory"])
                    package = world_dir / "package"
                    if damage != "missing-package":
                        package.mkdir()
                        (package / "manifest.json").write_text("{}", encoding="utf-8")
                    result = {"schemaVersion": 1, "worldId": options["worldId"], "status": "completed",
                        "packageDirectory": str(package), "reason": None, "hostViolations": [],
                        "completedGameMinutes": 2400, "realSeconds": 451, "replayPassed": True}
                    if damage == "host-violation": result["hostViolations"] = ["resource.conservation_failed"]
                    if damage == "replay-failed": result["replayPassed"] = False
                    if damage == "outside-package": result["packageDirectory"] = str(directory)
                    if damage == "bad-real-time": result["realSeconds"] = float("nan")
                    self.write(world_dir / "result.json", result)
                    runner.tick()

                    self.assertEqual(self.read(directory / "active-world.json")["state"], "aborted")
                    self.assertEqual(self.read(directory / "status.json")["worlds"][0]["status"], "host_failure")
                    runner.close()

    def test_missing_unity_handshakes_and_interruption_stop_the_study_without_replacing_worlds(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            for failure in ("missing-init", "result-only", "missing-result", "interrupt"):
                with self.subTest(failure=failure):
                    directory = Path(temporary) / failure
                    now = [0.0]
                    runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                        evaluation_plan_reference="frozen-plan-v1", clock=lambda: now[0])
                    cleanup.callback(runner.close)
                    runner.tick()
                    options = self.active_options(directory)
                    world_dir = Path(options["outputDirectory"])
                    if failure == "missing-init":
                        now[0] = 600
                    elif failure == "result-only":
                        self.write(world_dir / "result.json", {"schemaVersion": 1, "worldId": options["worldId"],
                            "status": "initialization_failed", "packageDirectory": None, "reason": "fixture.scene_load_failed",
                            "hostViolations": [], "completedGameMinutes": 0, "realSeconds": 0, "replayPassed": False})
                    elif failure == "missing-result":
                        self.initialize(runner, options)
                        now[0] = 600
                        runner.tick()
                        self.assertTrue((world_dir / "stop.json").exists())
                        now[0] = 659.9
                        runner.tick()
                        self.assertEqual(self.read(directory / "active-world.json")["state"], "ready")
                        now[0] = 660
                    else:
                        self.initialize(runner, options)
                        runner.close()
                    runner.tick()

                    self.assertEqual(self.read(directory / "active-world.json")["state"], "aborted")
                    status = self.read(directory / "status.json")
                    if failure == "result-only":
                        self.assertEqual(status["worlds"][0]["status"], "initialization_failed")
                        self.assertEqual(status["worlds"][0]["reason"], "fixture.scene_load_failed")
                    self.assertEqual(sum(row["status"] == "not_started" for row in status["worlds"]), 15)
                    self.assertEqual(status["attemptedProviderCalls"], 0)
                    runner.close()

    def test_invalid_initialization_packets_abort_instead_of_starting_a_different_or_unverified_world(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            for damage in ("schema", "world", "missing-check", "outside-snapshot"):
                with self.subTest(damage=damage):
                    directory = Path(temporary) / damage
                    runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                        evaluation_plan_reference="frozen-plan-v1")
                    cleanup.callback(runner.close)
                    runner.tick()
                    options = self.active_options(directory)
                    world_dir = Path(options["outputDirectory"])
                    initial = world_dir / "initial.json"
                    initial.write_text("{}", encoding="utf-8")
                    packet = {"schemaVersion": 1, "worldId": options["worldId"], "initialized": True,
                        "initialSnapshotPath": str(initial), "reason": ""}
                    if damage == "schema": packet["schemaVersion"] = 2
                    if damage == "world": packet["worldId"] = "a-prior-world"
                    if damage == "missing-check": del packet["initialized"]
                    if damage == "outside-snapshot": packet["initialSnapshotPath"] = str(directory / "plan.json")
                    self.write(world_dir / "initialized.json", packet)
                    runner.tick()

                    self.assertEqual(self.read(directory / "active-world.json")["state"], "aborted")
                    self.assertFalse((world_dir / "start.json").exists())
                    self.assertEqual(self.read(directory / "status.json")["attemptedProviderCalls"], 0)
                    runner.close()

    def test_fixed_cli_completes_all_sixteen_file_handshakes_without_a_credential_file(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            directory = Path(temporary) / "study"
            process = subprocess.Popen([sys.executable, "-B", str(Path(__file__).with_name("run_four_agent_integration.py")),
                "--stage", "fixed", "--output-dir", str(directory), "--source-revision", "a" * 40,
                "--evaluation-plan-reference", "frozen-plan-v1"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            def wait_for(predicate):
                deadline = time.monotonic() + 10
                while time.monotonic() < deadline:
                    try:
                        if predicate():
                            return
                    except PermissionError:
                        pass
                    if process.poll() is not None:
                        self.fail("Runner exited before completing its handshakes: " + process.communicate()[1])
                    time.sleep(0.01)
                self.fail("Runner did not publish the expected handshake.")
            try:
                for ordinal in range(1, 17):
                    active_path = directory / "active-world.json"
                    wait_for(lambda: active_path.exists() and self.read(active_path)["ordinal"] == ordinal)
                    options = self.active_options(directory)
                    world_dir = Path(options["outputDirectory"])
                    initial = world_dir / "initial.json"
                    initial.write_text("{}", encoding="utf-8")
                    self.write(world_dir / "initialized.json", {"schemaVersion": 1, "worldId": options["worldId"],
                        "initialized": True, "initialSnapshotPath": str(initial), "reason": None})
                    wait_for(lambda: (world_dir / "start.json").exists())
                    (world_dir / "package").mkdir()
                    (world_dir / "package" / "manifest.json").write_text("{}", encoding="utf-8")
                    self.write(world_dir / "result.json", {"schemaVersion": 1, "worldId": options["worldId"],
                        "status": "completed", "packageDirectory": str(world_dir / "package"), "reason": None,
                        "hostViolations": [], "completedGameMinutes": 2400, "realSeconds": 451, "replayPassed": True})
                stdout, stderr = process.communicate(timeout=10)
                self.assertEqual(process.returncode, 0, stderr)
                self.assertEqual(self.read(directory / "active-world.json")["state"], "finished")
                status = self.read(directory / "status.json")
                self.assertEqual([world["status"] for world in status["worlds"]], ["completed"] * 16)
                self.assertEqual(status["attemptedProviderCalls"], 0)
                self.assertNotIn("credential", stdout.lower())
            finally:
                if process.poll() is None:
                    process.terminate()
                process.communicate(timeout=5)

    def test_initialization_or_host_failure_aborts_remaining_worlds_and_existing_evidence_cannot_restart(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            for failure in ("initialization_failed", "host_failure"):
                with self.subTest(failure=failure):
                    directory = Path(temporary) / failure
                    runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                        evaluation_plan_reference="frozen-plan-v1")
                    cleanup.callback(runner.close)
                    runner.tick()
                    options = self.active_options(directory)
                    world_dir = Path(options["outputDirectory"])
                    if failure == "initialization_failed":
                        self.write(world_dir / "initialized.json", {"schemaVersion": 1, "worldId": options["worldId"],
                            "initialized": False, "initialSnapshotPath": "", "reason": "fixture.initial_state_mismatch"})
                    else:
                        self.initialize(runner, options)
                        self.write(world_dir / "result.json", {"schemaVersion": 1, "worldId": options["worldId"],
                            "status": failure, "packageDirectory": "", "reason": "fixture.asset_violation",
                            "hostViolations": ["fixture.asset_violation"], "completedGameMinutes": 740,
                            "realSeconds": 10, "replayPassed": False})
                    runner.tick()
                    self.assertEqual(self.read(directory / "active-world.json")["state"], "aborted")
                    status = self.read(directory / "status.json")
                    self.assertEqual(status["worlds"][0]["status"], failure)
                    self.assertEqual(sum(row["status"] == "not_started" for row in status["worlds"]), 15)
                    original = (directory / "plan.json").read_bytes()
                    with self.assertRaises(FileExistsError):
                        FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                            evaluation_plan_reference="frozen-plan-v1")
                    self.assertEqual((directory / "plan.json").read_bytes(), original)
                    runner.close()

    def test_deadline_blocks_provider_without_waiting_for_poll_and_keeps_measurements_available(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            directory = Path(temporary) / "study"
            now = [100.0]
            calls = []
            def provider(payload):
                calls.append(payload)
                return {"choices": [{"message": {"content": '{"schemaVersion":4,"operation":"wait"}'}}]}
            runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                evaluation_plan_reference="frozen-plan-v1", stage="live", transport=provider, clock=lambda: now[0])
            cleanup.callback(runner.close)
            try:
                runner.tick()
                options = self.active_options(directory)
                self.initialize(runner, options)
                request = {"schemaVersion": 4, "npcId": "npc.shopkeeper_mina", "allowedOperations": ["wait"],
                    "decisionId": "fixture", "step": 1, "expression": {"schemaVersion": 1, "mode": "free_text"}}
                now[0] = 699.9
                self.assertEqual(self.http(options["proxyEndpoint"], request)[0], 200)
                now[0] = 700.0
                code, error = self.http(options["proxyEndpoint"], request)
                self.assertEqual(code, 503)
                self.assertEqual(error["error"], "experiment.deadline")
                runner.tick()
                world_dir = Path(options["outputDirectory"])
                self.assertEqual(self.read(world_dir / "stop.json")["reason"], "experiment.deadline")
                code, measurements = self.http(options["proxyEndpoint"].replace("/decide", "/measurements"))
                self.assertEqual(code, 200)
                self.assertEqual(len(measurements["measurements"]), 1)
                self.write(world_dir / "result.json", {"schemaVersion": 1, "worldId": options["worldId"],
                    "status": "incomplete", "packageDirectory": str(world_dir / "package"), "reason": "experiment.deadline",
                    "hostViolations": [], "completedGameMinutes": 2000, "realSeconds": 600, "replayPassed": False})
                runner.tick()
                next_options = self.active_options(directory)
                self.assertEqual(next_options["ordinal"], 2)
                self.assertEqual(self.read(directory / "status.json")["attemptedProviderCalls"], 1)
                self.assertEqual(self.http(next_options["proxyEndpoint"].replace("/decide", "/status"))[1]["attemptedProviderCalls"], 0)
                self.assertEqual(len(calls), 1)
            finally:
                runner.close()

    def test_live_proxy_waits_for_initialization_and_persists_all_thirty_two_provider_attempts(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            directory = Path(temporary) / "study"
            calls = []
            def provider(payload):
                ledger = [json.loads(line) for line in (directory / "ledger.jsonl").read_text(encoding="utf-8").splitlines()]
                self.assertEqual(sum(row["event"] == "provider_started" for row in ledger), len(calls) + 1)
                calls.append(payload)
                if len(calls) == 2:
                    raise ProxyError("provider.fixture_failure")
                return {"choices": [{"message": {"content": '{"schemaVersion":4,"operation":"wait"}'}}]}
            runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                evaluation_plan_reference="frozen-plan-v1", stage="live", transport=provider)
            cleanup.callback(runner.close)
            try:
                runner.tick()
                options = self.active_options(directory)
                endpoint = options["proxyEndpoint"]
                self.assertTrue(endpoint.startswith("http://127.0.0.1:"))
                self.assertNotIn(":0/", endpoint)
                request = {"schemaVersion": 4, "npcId": "npc.shopkeeper_mina", "allowedOperations": ["wait"],
                    "decisionId": "fixture", "step": 1, "expression": {"schemaVersion": 1, "mode": "free_text"}}
                code, body = self.http(endpoint, request)
                self.assertEqual(code, 503)
                self.assertEqual(body["error"], "experiment.not_started")
                self.assertEqual(calls, [])
                self.initialize(runner, options)
                initial = self.read(directory / "status.json")["worlds"][0].get("initialProxyStatus")
                self.assertIsNotNone(initial)
                self.assertEqual((initial["attemptedProviderCalls"], initial["inflight"], initial["remainingCalls"]), (0, 0, 32))
                ledger = [json.loads(line) for line in (directory / "ledger.jsonl").read_text(encoding="utf-8").splitlines()]
                self.assertEqual(next(row for row in ledger if row["event"] == "world_started")["initialProxyStatus"], initial)
                for number in range(32):
                    code, body = self.http(endpoint, request | {"decisionId": str(number)})
                    self.assertEqual(code, 502 if number == 1 else 200)
                code, body = self.http(endpoint, request)
                self.assertEqual(code, 429)
                self.assertEqual(body["error"], "proxy.call_limit")
                self.assertEqual(len(calls), 32)
                self.assertEqual(calls[0]["model"], "deepseek-v4-flash")
                self.assertEqual(calls[0]["max_tokens"], 512)
                status = self.read(directory / "status.json")
                self.assertEqual(status["attemptedProviderCalls"], 32)
                self.assertEqual(status["worlds"][0]["attemptedProviderCalls"], 32)
                proxy_status = self.http(endpoint.replace("/decide", "/status"))[1]
                self.assertEqual(proxy_status["remainingCalls"], 0)
                self.assertEqual(proxy_status["inflight"], 0)
            finally:
                runner.close()

    def initialize(self, runner, options):
        world_dir = Path(options["outputDirectory"])
        initial_path = world_dir / "initial.json"
        initial_path.write_text("{}", encoding="utf-8")
        self.write(world_dir / "initialized.json", {"schemaVersion": 1, "worldId": options["worldId"],
            "initialized": True, "initialSnapshotPath": str(initial_path), "reason": ""})
        runner.tick()

    @staticmethod
    def http(endpoint, value=None):
        request = urllib.request.Request(endpoint, data=None if value is None else json.dumps(value).encode("utf-8"),
            headers={} if value is None else {"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(request, timeout=3) as response:
                return response.status, json.load(response)
        except urllib.error.HTTPError as error:
            with error:
                return error.code, json.load(error)

    def test_initialized_world_starts_once_and_a_finished_result_advances_without_replacing_evidence(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            directory = Path(temporary) / "study"
            runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                evaluation_plan_reference="frozen-plan-v1")
            cleanup.callback(runner.close)
            runner.tick()
            options = self.active_options(directory)
            world_dir = Path(options["outputDirectory"])
            initial_path = world_dir / "initial.json"
            initial_path.write_text("{}", encoding="utf-8")
            self.write(world_dir / "initialized.json", {"schemaVersion": 1, "worldId": options["worldId"],
                "initialized": True, "initialSnapshotPath": str(initial_path), "reason": ""})
            runner.tick()
            self.assertEqual(self.read(world_dir / "start.json"), {"worldId": options["worldId"]})
            runner.tick()
            self.assertEqual(self.active_options(directory)["worldId"], options["worldId"])
            (world_dir / "package").mkdir()
            (world_dir / "package" / "manifest.json").write_text("{}", encoding="utf-8")
            self.write(world_dir / "result.json", {"schemaVersion": 1, "worldId": options["worldId"],
                "status": "completed", "packageDirectory": str(world_dir / "package"), "reason": "",
                "hostViolations": [], "completedGameMinutes": 2400, "realSeconds": 451, "replayPassed": True})
            runner.tick()

            next_options = self.active_options(directory)
            self.assertEqual(next_options["ordinal"], 2)
            self.assertNotEqual(next_options["worldId"], options["worldId"])
            self.assertEqual(next_options["pairId"], options["pairId"])
            status = self.read(directory / "status.json")
            self.assertEqual(status["worlds"][0]["status"], "completed")
            self.assertEqual(status["worlds"][1]["status"], "initializing")
            self.assertEqual(sum(world["status"] == "not_started" for world in status["worlds"]), 14)
            self.assertEqual(status["attemptedProviderCalls"], 0)
            events = [json.loads(line) for line in (directory / "ledger.jsonl").read_text(encoding="utf-8").splitlines()]
            self.assertEqual(sum(row["event"] == "world_started" for row in events), 1)
            self.assertTrue((world_dir / "result.json").exists())
            runner.close()

    @staticmethod
    def write(path, value):
        temporary = path.with_suffix(".unity.tmp")
        temporary.write_text(json.dumps(value), encoding="utf-8")
        temporary.replace(path)

    @staticmethod
    def read(path):
        return json.loads(path.read_text(encoding="utf-8"))

    @classmethod
    def active_options(cls, directory):
        return cls.read(Path(cls.read(directory / "active-world.json")["optionsPath"]))

    def test_new_study_registers_sixteen_fresh_worlds_and_publishes_the_first_fixed_input(self):
        with ExitStack() as cleanup:
            temporary = cleanup.enter_context(tempfile.TemporaryDirectory())
            directory = Path(temporary) / "study"
            runner = FourAgentIntegrationRunner(directory, source_revision="a" * 40,
                evaluation_plan_reference="docs/verification/frozen-plan.md@" + "a" * 40)
            cleanup.callback(runner.close)
            runner.tick()

            plan = json.loads((directory / "plan.json").read_text(encoding="utf-8"))
            self.assertEqual(plan["maxProviderCalls"], 512)
            self.assertEqual(set(plan["sourceSha256"]), {"run_four_agent_integration.py", "decision_proxy.py"})
            self.assertEqual(set(plan["systemPromptSha256"]), {"F", "S"})
            for digest in (*plan["sourceSha256"].values(), *plan["systemPromptSha256"].values()):
                self.assertRegex(digest, r"\A[0-9a-f]{64}\Z")
            self.assertEqual(len(plan["worlds"]), 16)
            self.assertEqual(len({world["worldId"] for world in plan["worlds"]}), 16)
            self.assertEqual(len({world["pairId"] for world in plan["worlds"]}), 8)
            for scenario in ("available", "seller_empty", "buyer_poor", "need_satisfied"):
                for repetition, order in ((1, ["F", "S"]), (2, ["S", "F"])):
                    pair = [world for world in plan["worlds"]
                        if world["scenarioId"] == scenario and world["repetition"] == repetition]
                    self.assertEqual([world["speechMode"] for world in pair], order)
                    self.assertEqual(pair[0]["pairId"], pair[1]["pairId"])
                    self.assertTrue(all(world["armOrder"] == order for world in pair))
            active = json.loads((directory / "active-world.json").read_text(encoding="utf-8"))
            self.assertEqual(active["state"], "ready")
            self.assertEqual(active["ordinal"], 1)
            options_path = Path(active["optionsPath"])
            self.assertTrue(options_path.is_absolute())
            options = json.loads(options_path.read_text(encoding="utf-8"))
            self.assertEqual(options["worldId"], plan["worlds"][0]["worldId"])
            self.assertEqual(options["stage"], "fixed")
            self.assertEqual(options["scriptVersion"], "cross-day-v1")
            self.assertEqual(options["maxProviderCalls"], 32)
            self.assertEqual(options["maxRealSeconds"], 600)
            self.assertEqual(options["expressionContractVersion"], "1.0")
            self.assertEqual(options["proxyEndpoint"], "")
            self.assertFalse((options_path.parent / "start.json").exists())
            runner.close()


if __name__ == "__main__":
    unittest.main()
