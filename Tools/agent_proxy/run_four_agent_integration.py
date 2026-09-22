"""Coordinate bounded four-resident experiments through files shared with Unity."""

import argparse
import hashlib
import json
import math
import os
import re
import threading
import time
import uuid
from pathlib import Path
from datetime import datetime, timezone

from decision_proxy import DeepSeekTransport, EXPRESSION_PROMPTS, SYSTEM_PROMPT, ProxyError, ProxyService, create_server, load_cozytown_key


def _write_json(path, value):
    temporary = path.with_suffix(".tmp")
    with temporary.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, allow_nan=False, sort_keys=True, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    for attempt in range(100):
        try:
            temporary.replace(path)
            return
        except PermissionError:
            if attempt == 99:
                raise
            time.sleep(0.01)


class _WorldProxy:
    def __init__(self, runner, world, trace):
        self.runner = runner
        self.speech_mode = "free_text" if world["speechMode"] == "F" else "structured_facts"
        self.accepting = False
        self.active = 0
        self.trace_after_completion = None
        self.proxy = ProxyService(lambda payload: runner._call_provider(world, payload),
            max_calls=32, max_parallel=2, trace=trace)

    @property
    def status(self):
        return self.proxy.status

    @property
    def measurements(self):
        return self.proxy.measurements

    def decide(self, context):
        with self.runner._condition:
            if not self.accepting:
                raise ProxyError("experiment.not_started", 503)
            if self.runner._clock() >= self.runner._started_at + 600:
                raise ProxyError("experiment.deadline", 503)
            if (not isinstance(context, dict)
                    or context.get("npcId") not in ("npc.shopkeeper_mina", "npc.farmer_eli", "npc.fisher_ren", "npc.cook_sora")
                    or not isinstance(context.get("expression"), dict)
                    or context["expression"].get("mode") != self.speech_mode):
                raise ProxyError("experiment.context_mismatch", 400)
            self.active += 1
        try:
            return self.proxy.decide(context)
        finally:
            with self.runner._condition:
                self.active -= 1
                if self.active == 0 and self.trace_after_completion is not None:
                    self.trace_after_completion.close()
                    self.trace_after_completion = None
                self.runner._condition.notify_all()


class FourAgentIntegrationRunner:
    def __init__(self, output_dir, *, source_revision, evaluation_plan_reference, stage="fixed",
                 transport=None, clock=None, cleanup_timeout_seconds=30):
        if stage not in ("fixed", "live") or (stage == "live" and not callable(transport)):
            raise ValueError("Choose fixed or supply a provider transport for live.")
        if type(cleanup_timeout_seconds) not in (int, float) or not 0 < cleanup_timeout_seconds <= 30:
            raise ValueError("Proxy cleanup must allow more than zero and at most 30 wall-clock seconds.")
        if not isinstance(source_revision, str) or not source_revision.strip():
            raise ValueError("Record the candidate source revision.")
        if (not isinstance(evaluation_plan_reference, str) or not evaluation_plan_reference.strip()
                or len(evaluation_plan_reference) > 4096):
            raise ValueError("Record a nonempty evaluation plan reference of at most 4096 characters.")
        if stage == "live" and (re.fullmatch(r"[0-9a-fA-F]{40}", source_revision) is None or evaluation_plan_reference.strip() == "none"):
            raise ValueError("Live requires a full candidate SHA and a registered evaluation plan.")
        self.output_dir = Path(output_dir).resolve()
        self.output_dir.mkdir(parents=True, exist_ok=False)
        self._clock = clock or time.monotonic
        self._transport = transport
        self._cleanup_timeout_seconds = cleanup_timeout_seconds
        self._cleanup_failure = None
        self._condition = threading.Condition(threading.RLock())
        self._proxy = self._server = self._server_thread = self._trace = None
        self._ordinal = 0
        self._closed = False
        self._started_at = None
        self._stopped_at = None
        study_id = uuid.uuid4().hex
        worlds = []
        for repetition in (1, 2):
            order = ["F", "S"] if repetition == 1 else ["S", "F"]
            for scenario in ("available", "seller_empty", "buyer_poor", "need_satisfied"):
                pair_id = uuid.uuid4().hex
                for arm in order:
                    world_id = uuid.uuid4().hex
                    ordinal = len(worlds) + 1
                    worlds.append({"schemaVersion": 1, "studyId": study_id, "worldId": world_id,
                        "ordinal": ordinal, "repetition": repetition, "pairId": pair_id,
                        "armOrder": order, "scenarioId": scenario, "speechMode": arm,
                        "stage": stage, "runMode": stage, "scriptVersion": "cross-day-v1",
                        "sourceRevision": source_revision, "expressionContractVersion": "1.0",
                        "evaluationPlanReference": evaluation_plan_reference,
                        "proxyEndpoint": "", "clientConfigurationJson": "" if stage == "fixed" else json.dumps({
                            "model": "deepseek-v4-flash", "promptVersion": "Tools/agent_proxy/decision_proxy.py@" + source_revision,
                            "protocolVersion": 4, "maxTokens": 512, "thinking": "disabled",
                            "responseFormat": "json_object", "stream": False}, sort_keys=True),
                        "maxProviderCalls": 32, "maxRealSeconds": 600,
                        "outputDirectory": str(self.output_dir / (str(ordinal).zfill(2) + "-" + world_id))})
        self.plan = {"schemaVersion": 1, "studyId": study_id, "stage": stage,
            "createdAtUtc": datetime.now(timezone.utc).isoformat(),
            "sourceRevision": source_revision, "evaluationPlanReference": evaluation_plan_reference,
            "expressionContractVersion": "1.0", "scriptVersion": "cross-day-v1",
            "supportedRequestSchemaVersions": [1, 2, 3, 4], "requestSchemaPolicy": "copy_top_level_request",
            "sourceSha256": {name: hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
                for name in ("run_four_agent_integration.py", "decision_proxy.py")},
            "systemPromptSha256": {arm: hashlib.sha256((SYSTEM_PROMPT + EXPRESSION_PROMPTS[mode]).encode("utf-8")).hexdigest()
                for arm, mode in (("F", "free_text"), ("S", "structured_facts"))},
            "maxProviderCalls": 512, "initializationTimeoutSeconds": 600, "resultGraceSeconds": 60,
            "proxyCleanupTimeoutSeconds": cleanup_timeout_seconds, "worlds": worlds}
        _write_json(self.output_dir / "plan.json", self.plan)
        self._status = {"schemaVersion": 1, "studyId": study_id, "state": "running", "attemptedProviderCalls": 0,
            "worlds": [{"worldId": world["worldId"], "ordinal": world["ordinal"],
                "status": "not_started", "attemptedProviderCalls": 0} for world in worlds]}
        self._save_status()

    @property
    def state(self):
        return self._status["state"]

    def tick(self):
        with self._condition:
            try:
                self._tick()
            except (OSError, ValueError, TypeError):
                if self._ordinal == 0:
                    raise
                self._abort("host_failure", "experiment.communication_invalid")

    def _tick(self):
        if self._closed or self._status["state"] != "running":
            return
        if self._ordinal == 0:
            self._publish_next()
            return
        world = self.plan["worlds"][self._ordinal - 1]
        directory = Path(world["outputDirectory"])
        result_path = directory / "result.json"
        if self._started_at is None:
            if result_path.exists():
                result = self._read_packet(result_path, world)
                if result is None:
                    return
                if result.get("status") not in ("initialization_failed", "host_failure"):
                    raise ValueError("A world cannot complete before its initialization handshake.")
                self._consume_result(result, world)
                return
            if self._clock() >= self._published_at + 600:
                self._abort("initialization_failed", "experiment.initialization_timeout")
                return
            initialized = directory / "initialized.json"
            if not initialized.exists():
                return
            value = self._read_packet(initialized, world)
            if value is None:
                return
            if type(value.get("initialized")) is not bool:
                raise ValueError("Unity must confirm the current world initialization.")
            if not value["initialized"]:
                self._status["worlds"][self._ordinal - 1]["initialization"] = value
                self._abort("initialization_failed", value.get("reason") or "experiment.initialization_failed")
                return
            if not self._inside(value.get("initialSnapshotPath"), directory) or not Path(value["initialSnapshotPath"]).is_file():
                raise ValueError("The initialized snapshot must exist inside the current world directory.")
            initial_proxy = {}
            if self._proxy is not None:
                status = self._proxy.status
                if (status["attemptedProviderCalls"], status["inflight"], status["remainingCalls"]) != (0, 0, 32):
                    raise ValueError("A fresh world requires an unused proxy budget with no pending requests.")
                initial_proxy["initialProxyStatus"] = status
                self._status["worlds"][self._ordinal - 1]["initialProxyStatus"] = status
            self._started_at = self._clock()
            self._status["worlds"][self._ordinal - 1]["status"] = "running"
            self._record("world_started", world, **initial_proxy)
            self._save_status()
            _write_json(directory / "start.json", {"worldId": world["worldId"]})
            if self._proxy is not None:
                self._proxy.accepting = True
            return
        if self._clock() >= self._started_at + 600 and self._stopped_at is None:
            self._stopped_at = self._clock()
            if self._proxy is not None:
                self._proxy.accepting = False
            _write_json(directory / "stop.json", {"worldId": world["worldId"], "reason": "experiment.deadline"})
            self._record("world_stopped", world, reason="experiment.deadline")
        if result_path.exists():
            result = self._read_packet(result_path, world)
            if result is None:
                return
            self._consume_result(result, world)
        elif self._clock() >= self._started_at + 660:
            self._abort("host_failure", "experiment.result_timeout")

    def _consume_result(self, result, world):
        if result.get("status") not in ("completed", "incomplete", "initialization_failed", "host_failure"):
            raise ValueError("Unity must report the current world result.")
        if (not isinstance(result.get("hostViolations"), list)
                or any(not isinstance(item, str) for item in result["hostViolations"])
                or type(result.get("replayPassed")) is not bool
                or any(type(result.get(field)) not in (int, float) or not math.isfinite(result[field]) or result[field] < 0
                    for field in ("completedGameMinutes", "realSeconds"))):
            raise ValueError("Unity results require finite timing and explicit host and replay checks.")
        package = result.get("packageDirectory")
        if package is None and result["status"] != "completed":
            package = ""
        if not isinstance(package, str) or (package and not self._inside(package, Path(world["outputDirectory"]))):
            raise ValueError("Package evidence must remain inside its world directory.")
        if result["status"] == "completed" and (not package or not (Path(package) / "manifest.json").is_file()):
            raise ValueError("Completed worlds require package evidence.")
        if result["hostViolations"] or (result["status"] == "completed" and not result["replayPassed"]):
            self._status["worlds"][self._ordinal - 1]["result"] = result
            self._abort("host_failure", "experiment.host_or_replay_failed")
            return
        self._status["worlds"][self._ordinal - 1].update(status=result["status"], result=result)
        self._record("world_finished", world, status=result["status"])
        if not self._stop_proxy():
            self._abort("host_failure", "experiment.proxy_cleanup_timeout")
            return
        if result["status"] in ("initialization_failed", "host_failure"):
            self._abort(result["status"], result.get("reason") or "experiment.host_failure")
            return
        self._publish_next()

    @staticmethod
    def _inside(value, directory):
        return (isinstance(value, str) and bool(value.strip()) and Path(value).is_absolute()
            and Path(value).resolve().is_relative_to(directory.resolve()))

    @staticmethod
    def _read_packet(path, world):
        try:
            if path.stat().st_size > 1024 * 1024:
                raise ValueError("Unity communication packets must fit within 1 MiB.")
            value = json.loads(path.read_text(encoding="utf-8-sig"))
            json.dumps(value, allow_nan=False)
        except PermissionError:
            return None
        if (not isinstance(value, dict) or type(value.get("schemaVersion")) is not int or value["schemaVersion"] != 1
                or value.get("worldId") != world["worldId"]
                or (value.get("reason") is not None and not isinstance(value["reason"], str))):
            raise ValueError("Unity communication requires schema 1 and the current world identity.")
        return value

    def _abort(self, status, reason):
        world = self.plan["worlds"][self._ordinal - 1]
        directory = Path(world["outputDirectory"])
        self._status["state"] = "aborted"
        self._status["worlds"][self._ordinal - 1].update(status=status, reason=reason)
        if not (directory / "stop.json").exists():
            _write_json(directory / "stop.json", {"worldId": world["worldId"], "reason": reason})
        if not self._stop_proxy():
            status, reason = "host_failure", "experiment.proxy_cleanup_timeout"
            self._status["worlds"][self._ordinal - 1].update(status=status, reason=reason)
        self._record("study_aborted", world, status=status, reason=reason)
        _write_json(self.output_dir / "active-world.json", {"schemaVersion": 1,
            "optionsPath": str(directory / "options.json"), "ordinal": self._ordinal, "state": "aborted"})
        self._save_status()

    def _publish_next(self):
        if self._ordinal == len(self.plan["worlds"]):
            self._status["state"] = "finished"
            _write_json(self.output_dir / "active-world.json", {"schemaVersion": 1,
                "optionsPath": "", "ordinal": self._ordinal, "state": "finished"})
            self._save_status()
            return
        self._ordinal += 1
        self._started_at = None
        self._stopped_at = None
        self._published_at = self._clock()
        world = self.plan["worlds"][self._ordinal - 1]
        directory = Path(world["outputDirectory"])
        directory.mkdir()
        if self.plan["stage"] == "live":
            self._trace = (directory / "proxy.jsonl").open("x", encoding="utf-8")
            self._proxy = _WorldProxy(self, world, self._trace)
            self._server = create_server(self._proxy, 0)
            self._server_thread = threading.Thread(target=self._server.serve_forever,
                kwargs={"poll_interval": 0.05}, daemon=True)
            self._server_thread.start()
            world["proxyEndpoint"] = "http://127.0.0.1:" + str(self._server.server_port) + "/decide"
        _write_json(directory / "options.json", world)
        _write_json(self.output_dir / "active-world.json", {"schemaVersion": 1,
            "optionsPath": str(directory / "options.json"), "ordinal": self._ordinal, "state": "ready"})
        self._status["worlds"][self._ordinal - 1]["status"] = "initializing"
        self._record("world_published", world)
        self._save_status()

    def _save_status(self):
        _write_json(self.output_dir / "status.json", self._status)

    def _record(self, event, world, **fields):
        with (self.output_dir / "ledger.jsonl").open("a", encoding="utf-8") as stream:
            json.dump({"event": event, "studyId": self.plan["studyId"], "worldId": world["worldId"],
                "ordinal": world["ordinal"], "recordedAtUtc": datetime.now(timezone.utc).isoformat(),
                **fields}, stream, ensure_ascii=False, allow_nan=False, sort_keys=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())

    def _call_provider(self, world, payload):
        context = json.loads(payload["messages"][1]["content"])
        with self._condition:
            if self._proxy is None or not self._proxy.accepting or world["ordinal"] != self._ordinal:
                raise ProxyError("experiment.not_started", 503)
            if self._clock() >= self._started_at + world["maxRealSeconds"]:
                raise ProxyError("experiment.deadline", 503)
            row = self._status["worlds"][world["ordinal"] - 1]
            if self._status["attemptedProviderCalls"] >= 512 or row["attemptedProviderCalls"] >= 32:
                raise ProxyError("experiment.call_limit", 429)
            number = self._status["attemptedProviderCalls"] + 1
            self._record("provider_started", world, providerCall=number, worldProviderCall=row["attemptedProviderCalls"] + 1,
                npcId=context.get("npcId"), decisionId=context.get("decisionId"), step=context.get("step"))
            self._status["attemptedProviderCalls"] = number
            row["attemptedProviderCalls"] += 1
            self._save_status()
        return self._transport(payload)

    def _stop_proxy(self):
        if self._proxy is None:
            return self._cleanup_failure is None
        started = time.monotonic()
        deadline = started + self._cleanup_timeout_seconds
        with self._condition:
            self._proxy.accepting = False
            self._server.shutdown()
            self._server.server_close()
            self._server_thread.join()
            while self._proxy.active:
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    break
                self._condition.wait(timeout=remaining)
            drained = self._proxy.active == 0
            if drained:
                self._trace.close()
            else:
                self._cleanup_failure = {"drained": False, "activeRequests": self._proxy.active,
                    "timeoutSeconds": self._cleanup_timeout_seconds, "elapsedSeconds": time.monotonic() - started}
                world = self.plan["worlds"][self._ordinal - 1]
                self._status["worlds"][self._ordinal - 1]["proxyCleanup"] = self._cleanup_failure
                self._record("proxy_cleanup_timed_out", world, **self._cleanup_failure)
                self._proxy.trace_after_completion = self._trace
        self._proxy = self._server = self._server_thread = self._trace = None
        return drained

    def close(self):
        with self._condition:
            if self._closed:
                return
            self._closed = True
            if self._status["state"] == "running" and self._ordinal:
                self._abort("incomplete", "experiment.interrupted")
            else:
                self._stop_proxy()


def main():
    parser = argparse.ArgumentParser(description="Publish sixteen four-resident Unity experiments with per-world provider limits.")
    parser.add_argument("--stage", required=True, choices=("fixed", "live"))
    parser.add_argument("--output-dir", required=True, type=Path, help="New study directory; existing evidence cannot be resumed or overwritten.")
    parser.add_argument("--source-revision", required=True, help="Candidate revision recorded in every experiment.")
    parser.add_argument("--evaluation-plan-reference", required=True, help="Frozen plan document reference recorded with expression contract 1.0.")
    parser.add_argument("--credential-file", type=Path, help="External CozyTown credential file; required only for live.")
    args = parser.parse_args()
    if args.output_dir.exists():
        parser.error("Use a new study directory; previously registered worlds cannot be retried here.")
    if (args.stage == "live") != (args.credential_file is not None):
        parser.error("Live requires a credential file; fixed runs accept none.")
    runner = None
    try:
        transport = None if args.stage == "fixed" else DeepSeekTransport(load_cozytown_key(args.credential_file))
        runner = FourAgentIntegrationRunner(args.output_dir, source_revision=args.source_revision,
            evaluation_plan_reference=args.evaluation_plan_reference, stage=args.stage, transport=transport)
        print(json.dumps({"outputDirectory": str(runner.output_dir), "state": runner.state}), flush=True)
        while runner.state == "running":
            runner.tick()
            if runner.state == "running":
                time.sleep(0.05)
        print(json.dumps({"outputDirectory": str(runner.output_dir), "state": runner.state}), flush=True)
        if runner.state == "aborted":
            parser.exit(1, "experiment.aborted\n")
    except ProxyError as error:
        parser.exit(1, error.code + "\n")
    except (OSError, ValueError):
        parser.exit(1, "experiment.runner_failed\n")
    except KeyboardInterrupt:
        parser.exit(1, "experiment.interrupted\n")
    finally:
        if runner is not None:
            runner.close()


if __name__ == "__main__":
    main()
