"""Record paired NPC expression requests through a shared bounded proxy."""

import copy
import argparse
import hashlib
import json
import os
import subprocess
import threading
from contextlib import ExitStack
from datetime import datetime, timezone
from pathlib import Path

from decision_proxy import EXPRESSION_PROMPTS, SYSTEM_PROMPT, ProxyService, ProxyError, create_server, DeepSeekTransport, load_cozytown_key


MODES = {"F": "free_text", "S": "structured_facts"}


def _json(value):
    return json.dumps(value, ensure_ascii=False, allow_nan=False, sort_keys=True, separators=(",", ":"))


def _sha(value):
    return hashlib.sha256(value).hexdigest()


class ExpressionExperiment:
    """Freeze experiment inputs and retain original candidates outside production traces."""

    def __init__(self, transport, output_dir, corpus_path, *, stage, source_files=(), fixed_evidence_dir=None):
        if stage not in ("fixed", "scene"):
            raise ValueError("stage must be fixed or scene")
        corpus_bytes = Path(corpus_path).read_bytes()
        corpus = json.loads(corpus_bytes.decode("utf-8-sig"))
        cases = corpus.get("cases") if isinstance(corpus, dict) else None
        if (not isinstance(cases, list) or len(cases) != 8
                or any(not isinstance(case, dict) or not isinstance(case.get("id"), str) or not case["id"].strip() for case in cases)
                or len({case["id"] for case in cases}) != 8):
            raise ValueError("The expression corpus requires eight distinct named cases.")
        sources = {name: Path(__file__).with_name(name) for name in ("expression_experiment.py", "decision_proxy.py")}
        repository = Path(__file__).resolve().parents[2]
        for source in source_files:
            path = Path(source).resolve()
            name = path.relative_to(repository).as_posix() if path.is_relative_to(repository) else path.as_posix()
            sources[name] = path
        source_paths = [str(path.resolve()) for path in sources.values()]
        revision = subprocess.run(["git", "rev-parse", "HEAD"], cwd=repository, check=True, capture_output=True).stdout.decode().strip()
        diff = subprocess.run(["git", "diff", "HEAD", "--", *source_paths], cwd=repository, check=True, capture_output=True).stdout
        self.manifest = {"stage": stage, "startedAtUtc": datetime.now(timezone.utc).isoformat(),
            "requestedModel": "deepseek-v4-flash", "maxCalls": 48, "plannedTotalCallCeiling": 96,
            "arms": MODES, "repetitionsPerFixedCase": 3, "retriesPerRequest": 0,
            "maxParallel": 2, "maxTokens": 512, "providerTimeoutSeconds": 7,
            "corpusSha256": _sha(corpus_bytes),
            "gitCommit": revision, "sourceDiffSha256": _sha(diff),
            "sourceSha256": {name: _sha(path.read_bytes()) for name, path in sources.items()},
            "systemPromptSha256": {arm: _sha((SYSTEM_PROMPT + EXPRESSION_PROMPTS[mode]).encode("utf-8"))
                                   for arm, mode in MODES.items()},
            "normalization": "Sorted compact UTF-8 JSON, removing only expression.mode from the effective context."}
        self._previous_calls = 0
        if stage == "scene":
            if fixed_evidence_dir is None:
                raise ValueError("Scene stage requires completed fixed-stage evidence.")
            fixed = Path(fixed_evidence_dir)
            fixed_manifest_bytes = (fixed / "manifest.json").read_bytes()
            fixed_manifest = json.loads(fixed_manifest_bytes)
            fixed_status = json.loads((fixed / "status.json").read_text(encoding="utf-8"))
            if (fixed_manifest.get("stage") != "fixed" or fixed_status.get("runState") != "completed"
                    or fixed_status.get("decisionRequests") != 48
                    or fixed_status.get("finishedDecisionRequests") != 48 or fixed_status.get("inflight") != 0
                    or type(fixed_status.get("attemptedProviderCalls")) is not int
                    or not 0 <= fixed_status["attemptedProviderCalls"] <= 48
                    or any(fixed_manifest.get(field) != self.manifest[field]
                           for field in ("sourceSha256", "systemPromptSha256", "corpusSha256", "maxCalls"))):
                raise ValueError("Fixed-stage evidence must be completed with matching source, prompts, corpus and bounded usage.")
            fixed_logs = {name: [json.loads(line) for line in (fixed / name).read_text(encoding="utf-8").splitlines()]
                          for name in ("contexts.jsonl", "provider-starts.jsonl", "provider-responses.jsonl", "proxy.jsonl")}
            admitted = [record["request"] for record in fixed_logs["contexts.jsonl"] if "request" in record]
            provider_calls = fixed_status["attemptedProviderCalls"]
            if (sorted(admitted) != list(range(1, 49))
                    or any(len(fixed_logs[name]) != provider_calls for name in ("provider-starts.jsonl", "provider-responses.jsonl", "proxy.jsonl"))
                    or sorted(record["providerCall"] for record in fixed_logs["provider-starts.jsonl"]) != list(range(1, provider_calls + 1))
                    or sorted(record["providerCall"] for record in fixed_logs["provider-responses.jsonl"]) != list(range(1, provider_calls + 1))
                    or _sha((fixed / "corpus.json").read_bytes()) != self.manifest["corpusSha256"]):
                raise ValueError("Fixed-stage usage records must account for all admitted requests and provider attempts.")
            self._previous_calls = fixed_status["attemptedProviderCalls"]
            self.manifest["fixedEvidence"] = {"directory": Path(os.path.relpath(fixed.resolve(), Path(output_dir).resolve())).as_posix(),
                                               "pathRelativeTo": "scene evidence directory", "manifestSha256": _sha(fixed_manifest_bytes),
                                               "attemptedProviderCalls": self._previous_calls}
        self.output_dir = Path(output_dir)
        self.output_dir.mkdir(parents=True, exist_ok=False)
        if stage == "scene":
            with (Path(fixed_evidence_dir) / "scene-claim.json").open("x", encoding="utf-8") as claim:
                claim.write(_json({"sceneOutputDirectory": Path(os.path.relpath(self.output_dir.resolve(), Path(fixed_evidence_dir).resolve())).as_posix(),
                                   "pathRelativeTo": "fixed evidence directory", "maxCalls": 48}) + "\n")
        self._lock = threading.RLock()
        self._condition = threading.Condition(self._lock)
        self._active = 0
        self._closed = False
        self._closing = False
        self._request_thread = threading.local()
        self._files = ExitStack()
        self._requests = 0
        self._finished = 0
        self._provider_calls = 0
        self._state = "running"
        self._worlds = {}
        try:
            (self.output_dir / "manifest.json").write_text(_json(self.manifest) + "\n", encoding="utf-8")
            (self.output_dir / "corpus.json").write_bytes(corpus_bytes)
            self._traces = {name: self._files.enter_context((self.output_dir / name).open("x", encoding="utf-8"))
                            for name in ("contexts.jsonl", "provider-starts.jsonl", "provider-responses.jsonl", "proxy.jsonl")}
            self._transport = transport
            self.proxy = ProxyService(self._record_response, max_calls=48, max_parallel=2, trace=self._traces["proxy.jsonl"])
            self._save_status()
        except Exception:
            self._files.close()
            raise

    @property
    def status(self):
        with self._lock:
            return {**self.proxy.status, "stage": self.manifest["stage"], "runState": self._state,
                    "decisionRequests": self._requests, "worlds": copy.deepcopy(self._worlds),
                    "finishedDecisionRequests": self._finished,
                    "totalAttemptedProviderCalls": self._previous_calls + self.proxy.status["attemptedProviderCalls"]}

    def _save_status(self):
        temporary = self.output_dir / "status.tmp"
        temporary.write_text(_json(self.status) + "\n", encoding="utf-8")
        temporary.replace(self.output_dir / "status.json")

    def _write(self, name, record):
        with self._lock:
            self._traces[name].write(_json(record) + "\n")
            self._traces[name].flush()

    def _record_response(self, payload):
        context = json.loads(payload["messages"][1]["content"])
        with self._lock:
            self._provider_calls += 1
            number = self._provider_calls
        record = {"providerCall": number, "request": self._request_thread.number,
            "arm": next(arm for arm, mode in MODES.items() if mode == context["expression"]["mode"]),
            "worldRunId": context.get("worldRunId"), "decisionId": context.get("decisionId"), "step": context.get("step"),
            "status": "provider.failure"}
        self._write("provider-starts.jsonl", record | {"status": "started", "startedAtUtc": datetime.now(timezone.utc).isoformat()})
        with self._lock:
            self._save_status()
        try:
            response = self._transport(payload)
            try:
                content = response["choices"][0]["message"]["content"]
                record["status"] = "content_recorded" if isinstance(content, str) else "provider.content_unavailable"
                if isinstance(content, str):
                    record["content"] = content
            except (KeyError, IndexError, TypeError):
                record["status"] = "provider.content_unavailable"
            return response
        except ProxyError as error:
            record["status"] = error.code
            raise
        finally:
            self._write("provider-responses.jsonl", record)

    def decide(self, arm, context):
        if arm not in MODES:
            raise ValueError("arm must be F or S")
        effective = copy.deepcopy(context)
        effective["expression"] = {"schemaVersion": 1, "mode": MODES[arm]}
        normalized = copy.deepcopy(effective)
        del normalized["expression"]["mode"]
        record = {"arm": arm, "stage": self.manifest["stage"], "originalContext": copy.deepcopy(context),
                  "effectiveContext": effective, "normalizedContextSha256": _sha(_json(normalized).encode("utf-8")),
                  "status": "provider.failure"}
        with self._lock:
            if self._closing or self._closed:
                raise ProxyError("experiment.stopped", 503)
            self._active += 1
        try:
            with self._lock:
                if self._requests >= 48:
                    raise ProxyError("experiment.call_limit", 429)
                if self.manifest["stage"] == "scene":
                    world_id = context.get("worldRunId")
                    if not isinstance(world_id, str) or not world_id.strip():
                        raise ProxyError("experiment.world_required", 400)
                    world = self._worlds.get(world_id)
                    if world is None:
                        if len(self._worlds) >= 4:
                            raise ProxyError("experiment.world_limit", 429)
                        world = {"arm": arm, "decisionRequests": 0}
                        self._worlds[world_id] = world
                    if world["arm"] != arm:
                        raise ProxyError("experiment.world_arm_mismatch", 400)
                    if world["decisionRequests"] >= 12:
                        raise ProxyError("experiment.world_call_limit", 429)
                    world["decisionRequests"] += 1
                self._requests += 1
                record["request"] = self._requests
                self._request_thread.number = self._requests
            candidate = self.proxy.decide(effective)
            record.update(status="passed", candidate=candidate)
            return candidate
        except ProxyError as error:
            record["status"] = error.code
            record["error"] = {"code": error.code, "status": error.status}
            if error.candidate_error_code is not None:
                record["error"]["candidateErrorCode"] = error.candidate_error_code
            raise
        finally:
            try:
                self._write("contexts.jsonl", record)
            finally:
                with self._lock:
                    if "request" in record:
                        self._finished += 1
                    self._active -= 1
                    self._condition.notify_all()
                    self._save_status()

    def close(self):
        with self._lock:
            if self._closed:
                return
            self._closing = True
            while self._active:
                self._condition.wait()
            self._state = "completed" if self._finished == 48 else "stopped"
            self._save_status()
            self._files.close()
            self._closed = True

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()


def create_experiment_servers(experiment, base_port=25800):
    """Expose F and S on loopback ports with the experiment's shared limits."""
    class ArmService:
        def __init__(self, arm):
            self.arm = arm

        @property
        def status(self):
            return {**experiment.status, "arm": self.arm}

        def decide(self, context):
            return experiment.decide(self.arm, context)

    servers = []
    try:
        for index, arm in enumerate(MODES):
            servers.append(create_server(ArmService(arm), base_port + index if base_port else 0))
    except Exception:
        for server in servers:
            server.server_close()
        raise
    return servers


def main(argv=None, *, transport=None, stop_event=None):
    parser = argparse.ArgumentParser(description="Serve paired NPC expression modes with a 48-call stage cap and frozen evidence.")
    parser.add_argument("--mode", choices=("serve",), default="serve")
    parser.add_argument("--stage", choices=("fixed", "scene"), required=True)
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True, help="New evidence directory; existing paths are rejected.")
    parser.add_argument("--credential-file", type=Path)
    parser.add_argument("--fixed-evidence-dir", type=Path, help="Completed fixed stage; required for the scene stage and usable once.")
    parser.add_argument("--stop-file", type=Path, help="Stop after in-flight evidence is saved when this local file exists.")
    parser.add_argument("--base-port", type=int, default=25800, help="F and S loopback ports start here; 0 assigns unused ports.")
    parser.add_argument("--source-file", type=Path, action="append", default=[], help="Additional source to freeze alongside NPC production code and probes.")
    args = parser.parse_args(argv)
    if not 0 <= args.base_port <= 65534 or (transport is None and args.credential_file is None):
        parser.error("Use a base port from 0 to 65534 and supply --credential-file for the real transport.")
    repository = Path(__file__).resolve().parents[2]
    patterns = ("Assets/CozyTown/Runtime/NpcAgents/*.cs", "Assets/CozyTown/Unity/Npc/*.cs",
                "Assets/CozyTown/Tests/UnityEditMode/*Expression*.cs", "Assets/CozyTown/Tests/PlayMode/*Expression*.cs")
    sources = sorted({path for pattern in patterns for path in repository.glob(pattern)}
                     | {repository / "Tools/agent_proxy/expression_cases.json",
                        repository / "Assets/CozyTown/Tests/PlayMode/NpcResourceScenarioPlayModeTests.cs",
                        repository / "docs/verification/npc-expression-comparison-plan-2026-09-14.md"})
    sources.extend(args.source_file)
    try:
        effective_transport = transport if transport is not None else DeepSeekTransport(load_cozytown_key(args.credential_file))
        with ExpressionExperiment(effective_transport, args.output_dir, args.corpus, stage=args.stage,
                                  source_files=sources, fixed_evidence_dir=args.fixed_evidence_dir) as experiment:
            servers = create_experiment_servers(experiment, args.base_port)
            workers = [threading.Thread(target=server.serve_forever, daemon=True) for server in servers]
            try:
                for worker in workers:
                    worker.start()
                print(_json({"endpoints": {arm: f"http://127.0.0.1:{server.server_port}/decide"
                                            for arm, server in zip(MODES, servers)}, **experiment.status}), flush=True)
                stop = stop_event or threading.Event()
                while not stop.wait(0.25):
                    if experiment.status["finishedDecisionRequests"] == 48 or (args.stop_file is not None and args.stop_file.exists()):
                        break
            except KeyboardInterrupt:
                pass
            finally:
                for server in servers:
                    server.shutdown()
                    server.server_close()
                for worker in workers:
                    worker.join(timeout=2)
        print(_json(experiment.status), flush=True)
    except ProxyError as error:
        parser.exit(1, error.code + "\n")
    except (OSError, ValueError, TypeError, subprocess.CalledProcessError):
        parser.exit(1, "experiment.startup_or_evidence_failure\n")


if __name__ == "__main__":
    main()
