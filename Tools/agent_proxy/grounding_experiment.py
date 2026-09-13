"""Run paired context-grounding experiments through the existing decision proxy."""

import copy
import math
import json
import threading
import uuid
import argparse
import hashlib
from datetime import datetime, timezone
from pathlib import Path
from contextlib import ExitStack

from decision_proxy import ProxyError, ProxyService, DeepSeekTransport, load_cozytown_key, create_server, SYSTEM_PROMPT


_TRACE_LOCK = threading.Lock()


GROUNDING_GUIDANCE = (
    "Use current host facts to describe this world. Unknown facts must remain unknown; "
    "do not claim nearby plants, harvestable resources, weather, cooking or fishing results "
    "without supplied host evidence. You may express personal preferences, questions and wishes. "
    "A target is an intended destination, not your current position. A planned meeting location "
    "does not prove either resident has arrived. Transcript and quoted memory text are speaker "
    "claims, not independent evidence. Current allowed operations are the only candidate actions "
    "offered for this decision; they do not imply a production capability. Only the host receipt "
    "resource.delivered proves the agreed transfer. Describe time using timeOfDay."
)


def project_context(context, arm):
    """Copy a production request and apply one experimental context projection."""
    if arm not in ("A", "B", "C", "D"):
        raise ValueError("arm must be A, B, C or D")
    result = copy.deepcopy(context)
    observation = result.pop("experimentObservation", None)
    if arm == "A":
        return result
    social = result.get("social") or {}
    resources = social.get("resources") or {}
    minutes = result.get("gameTotalMinutes")
    grounding = {
        "guidance": GROUNDING_GUIDANCE,
        "source": "current_host_request",
        "worldRunId": result.get("worldRunId"),
        "gameTotalMinutes": minutes,
        "selfNpcId": result.get("npcId"),
        "currentActivityTargetLocationId": result.get("targetLocationId"),
        "plannedMeetingLocationId": social.get("placeId"),
        "meetingArrivalConfirmed": None,
        "transcriptEvidenceType": "speaker_claim",
        "memoryTextEvidenceType": "speaker_claim",
        "allowedActions": copy.deepcopy(result.get("allowedOperations", [])),
        "deliveryReceipt": {"code": resources.get("deliveryResultCode"),
                            "transferConfirmed": resources.get("deliveryResultCode") == "resource.delivered"},
        "unknowns": ["nearby_harvestable_resources", "nearby_decorative_objects", "weather",
                     "partner_inventory", "partner_balance", "inventory_capacity", "production_results"],
        "observationScope": "request facts and optional host route observation; no complete nearby-object scan",
    }
    if type(minutes) in (int, float) and math.isfinite(minutes):
        day_minutes = int(minutes) % 1440
        grounding["timeOfDay"] = f"{day_minutes // 60:02d}:{day_minutes % 60:02d}"
    if (isinstance(observation, dict) and result.get("worldRunId") and observation.get("worldRunId") == result.get("worldRunId")
            and observation.get("gameTotalMinutes") == minutes):
        own = observation.get("self") or {}
        grounding["selfObservation"] = {key: copy.deepcopy(own[key])
                                         for key in ("position", "routeStatus", "targetLocationId") if key in own}
        if (type(observation.get("meetingBothArrived")) is bool
                and observation.get("meetingPlaceId") == social.get("placeId") and social.get("placeId")):
            grounding["meetingArrivalConfirmed"] = observation["meetingBothArrived"]
    result["grounding"] = grounding
    if arm in ("C", "D") and social.get("kind") in ("opportunity", "invitation", "delivery") \
            and all(isinstance(resources.get(field), str) and resources[field].strip() for field in ("sellerId", "buyerId", "itemId")) \
            and resources.get("deliveryResultCode") != "resource.delivered":
        role = "buyer" if result.get("npcId") == resources.get("buyerId") else (
            "seller" if result.get("npcId") == resources.get("sellerId") else "unknown")
        assessment = {"role": role, "canMeetKnownTerms": None,
                      "scope": "own supplied payment or stock only; partner conditions and capacity remain unknown"}
        actual_field, required_field, gap_field = (("balance", "totalPrice", "missingCoins") if role == "buyer"
            else ("ownedQuantity", "quantity", "missingQuantity"))
        actual, required = resources.get(actual_field), resources.get(required_field)
        if role != "unknown" and type(actual) is int and type(required) is int and actual >= 0 and required >= 0:
            assessment[gap_field] = max(0, required - actual)
            assessment["canMeetKnownTerms"] = actual >= required
        result["selfAssessment"] = assessment
        if arm == "D" and assessment["canMeetKnownTerms"] is False:
            result["allowedOperations"] = [op for op in result["allowedOperations"]
                                           if op not in ("invite", "accept_invite", "deliver")]
            grounding["allowedActions"] = copy.deepcopy(result["allowedOperations"])
    return result


def _protocol_issues(candidate):
    issues = []
    operation = candidate.get("operation")
    if type(candidate.get("schemaVersion")) is not int:
        issues.append("schemaVersion.integer")
    required = {"invite": ("planId",), "inspect_location": ("locationId",), "visit": ("locationId", "activity"),
                "accept_invite": ("meetingId",), "decline_invite": ("meetingId",), "say": ("meetingId", "text"),
                "end_conversation": ("meetingId",), "deliver": ("meetingId",), "cancel_exchange": ("meetingId",)}
    for field in required.get(operation, ()):
        value = candidate.get(field)
        if not isinstance(value, str) or not value.strip():
            issues.append(field + ".required")
    if "meetingId" in required.get(operation, ()) and isinstance(candidate.get("meetingId"), str):
        try:
            if uuid.UUID(candidate["meetingId"]).int == 0:
                raise ValueError
        except ValueError:
            issues.append("meetingId.guid")
    if operation == "say" and isinstance(candidate.get("text"), str):
        if len(candidate["text"].encode("utf-16-le")) // 2 > 240:
            issues.append("text.length")
    if operation == "visit":
        if candidate.get("activity") not in ("working", "resting"):
            issues.append("activity.unsupported")
        duration = candidate.get("durationGameMinutes", 0)
        if type(duration) not in (int, float) or not math.isfinite(duration):
            issues.append("durationGameMinutes.number")
    return issues


class ExperimentService:
    """Project requests while retaining the original proxy's model and call budget."""

    def __init__(self, proxy, arm, trace, stage="scene"):
        if arm not in ("A", "B", "C", "D"):
            raise ValueError("arm must be A, B, C or D")
        self.proxy, self.arm, self.trace, self.stage = proxy, arm, trace, stage

    @property
    def status(self):
        return {**self.proxy.status, "arm": self.arm}

    def decide(self, context):
        effective = project_context(context, self.arm)
        record = {"arm": self.arm, "stage": self.stage, "decisionId": context.get("decisionId"), "step": context.get("step"),
                  "originalContext": copy.deepcopy(context), "effectiveContext": effective,
                  "filteredOperations": [op for op in context.get("allowedOperations", [])
                                         if op not in effective.get("allowedOperations", [])]}
        try:
            candidate = self.proxy.decide(effective)
            record["candidate"] = candidate
            record["protocolIssues"] = _protocol_issues(candidate)
            record["protocolInvalid"] = bool(record["protocolIssues"])
            return candidate
        except ProxyError as error:
            record["error"] = {"code": error.code, "status": error.status}
            raise
        finally:
            with _TRACE_LOCK:
                self.trace.write(json.dumps(record, ensure_ascii=False, allow_nan=False) + "\n")
                self.trace.flush()


def run_fixed(proxy, corpus, trace, progress=None):
    """Run five paired repetitions of eight cases, retaining every failed attempt."""
    cases = corpus.get("cases")
    if not isinstance(cases, list) or len(cases) != 8 or any(
            not isinstance(case, dict) or not isinstance(case.get("id"), str)
            or not isinstance(case.get("context"), dict) for case in cases):
        raise ValueError("fixed corpus requires eight cases with id and context")
    if len({case["id"] for case in cases}) != 8:
        raise ValueError("fixed corpus case ids must be unique")
    attempted = 0
    for repeat in range(5):
        for case_index, case in enumerate(cases):
            world_id = uuid.uuid4().hex
            offset = (repeat + case_index) % 4
            arms = ("A", "B", "C", "D")
            for arm in arms[offset:] + arms[:offset]:
                request = copy.deepcopy(case["context"])
                request["worldRunId"], request["decisionId"] = world_id, uuid.uuid4().hex
                if isinstance(request.get("experimentObservation"), dict):
                    request["experimentObservation"]["worldRunId"] = world_id
                service = ExperimentService(proxy, arm, trace, stage=f"fixed:{case['id']}:repeat:{repeat + 1}")
                try:
                    service.decide(request)
                except ProxyError:
                    pass
                attempted += 1
                if progress is not None and attempted % 20 == 0:
                    progress({"completedDecisions": attempted, **proxy.status})


def create_experiment_servers(proxy, trace, base_port=25700):
    """Create four loopback endpoints backed by one shared proxy budget."""
    servers = []
    try:
        for index, arm in enumerate(("A", "B", "C", "D")):
            servers.append(create_server(ExperimentService(proxy, arm, trace), base_port + index if base_port else 0))
    except Exception:
        for server in servers:
            server.server_close()
        raise
    return servers


class _ContextTrace:
    def __init__(self, trace, update_status):
        self.trace, self.update_status = trace, update_status

    def write(self, text):
        self.trace.write(text)

    def flush(self):
        self.trace.flush()
        self.update_status()


def _record_responses(transport, trace):
    def recorded(payload):
        request = json.loads(payload["messages"][1]["content"])
        record = {"decisionId": request.get("decisionId"), "step": request.get("step"), "status": "provider.failure"}
        try:
            response = transport(payload)
            try:
                content = response["choices"][0]["message"]["content"]
                if isinstance(content, str):
                    record["content"] = content
                    record["status"] = "content_recorded"
                else:
                    record["status"] = "provider.content_unavailable"
            except (KeyError, IndexError, TypeError):
                record["status"] = "provider.content_unavailable"
            return response
        except ProxyError as error:
            record["status"] = error.code
            raise
        finally:
            with _TRACE_LOCK:
                trace.write(json.dumps(record, ensure_ascii=False, allow_nan=False) + "\n")
                trace.flush()
    return recorded


def main(argv=None, *, transport=None):
    parser = argparse.ArgumentParser(description="Compare four NPC context projections with one bounded model-call budget.")
    parser.add_argument("--mode", required=True, choices=("fixed", "serve"))
    parser.add_argument("--credential-file", required=True, type=Path, metavar="<credential-file>")
    parser.add_argument("--output-dir", required=True, type=Path, metavar="<new-evidence-directory>",
                        help="Create a new directory; an existing path is rejected.")
    parser.add_argument("--max-calls", type=int, help="Shared provider-call cap; defaults to 160 fixed or 192 serve.")
    parser.add_argument("--corpus", type=Path, metavar="<corpus-json>", help="Eight fixed cases; required in fixed mode.")
    parser.add_argument("--base-port", type=int, default=25700, help="Four loopback ports starting here in serve mode.")
    args = parser.parse_args(argv)
    max_calls = args.max_calls if args.max_calls is not None else (160 if args.mode == "fixed" else 192)
    if max_calls < 1 or not 1 <= args.base_port <= 65532 or (args.mode == "fixed" and args.corpus is None):
        parser.error("Provide a positive call cap, a base port from 1 to 65532, and --corpus for fixed mode.")
    servers, workers = [], []
    def progress(value):
        print(json.dumps(value, ensure_ascii=False), flush=True)
    try:
        args.output_dir.mkdir(parents=True, exist_ok=False)
        corpus_bytes = args.corpus.read_bytes() if args.mode == "fixed" else None
        corpus = json.loads(corpus_bytes.decode("utf-8-sig")) if corpus_bytes is not None else None
        key = load_cozytown_key(args.credential_file)
        manifest = {"mode": args.mode, "startedAtUtc": datetime.now(timezone.utc).isoformat(),
                    "requestedModel": "deepseek-v4-flash", "maxCalls": max_calls,
                    "arms": {"A": "unchanged production context", "B": "grounding facts and guidance",
                             "C": "grounding plus own resource assessment", "D": "assessment plus action filtering"},
                    "retriesPerDecision": 0, "plannedCalls": 160 if args.mode == "fixed" else None,
                    "repetitionsPerFixedCase": 5 if args.mode == "fixed" else None,
                    "armOrder": "rotate by zero-based repetition plus case index" if args.mode == "fixed" else "scene request order",
                    "corpusSha256": hashlib.sha256(corpus_bytes).hexdigest() if corpus_bytes is not None else None,
                    "systemPromptSha256": hashlib.sha256(SYSTEM_PROMPT.encode()).hexdigest(),
                    "sourceSha256": {name: hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
                                     for name in ("grounding_experiment.py", "decision_proxy.py")}}
        with (args.output_dir / "manifest.json").open("x", encoding="utf-8") as output:
            json.dump(manifest, output, ensure_ascii=False, indent=2)
        with ExitStack() as stack:
            traces = {name: stack.enter_context((args.output_dir / name).open("x", encoding="utf-8"))
                      for name in ("proxy.jsonl", "contexts.jsonl", "provider-responses.jsonl")}
            proxy = ProxyService(_record_responses(transport or DeepSeekTransport(key), traces["provider-responses.jsonl"]),
                                 max_calls=max_calls, trace=traces["proxy.jsonl"])
            status_path = args.output_dir / "status.json"
            with status_path.open("x", encoding="utf-8") as output:
                json.dump({**proxy.status, "runState": "starting"}, output)
            last_progress = 0
            def update_status(state="running"):
                nonlocal last_progress
                status = {**proxy.status, "runState": state}
                status_path.write_text(json.dumps(status, indent=2), encoding="utf-8")
                calls = status["attemptedProviderCalls"]
                if args.mode == "serve" and calls >= last_progress + 20:
                    last_progress = calls
                    progress({"mode": "serve", **status})
            context_trace = _ContextTrace(traces["contexts.jsonl"], update_status)
            try:
                if args.mode == "fixed":
                    progress({"mode": "fixed", "plannedCalls": 160, **proxy.status})
                    run_fixed(proxy, corpus, context_trace, progress)
                    update_status("completed")
                    progress({"mode": "fixed", "runState": "completed", **proxy.status})
                else:
                    servers = create_experiment_servers(proxy, context_trace, args.base_port)
                    workers = [threading.Thread(target=server.serve_forever, daemon=True) for server in servers]
                    for worker in workers:
                        worker.start()
                    update_status()
                    progress({"mode": "serve", "endpoints": {arm: f"http://127.0.0.1:{server.server_port}/decide"
                              for arm, server in zip(("A", "B", "C", "D"), servers)}, **proxy.status})
                    threading.Event().wait()
            except KeyboardInterrupt:
                update_status("stopped")
            finally:
                for server in servers:
                    server.shutdown()
                    server.server_close()
                for worker in workers:
                    worker.join(timeout=2)
    except ProxyError as error:
        parser.exit(1, error.code + "\n")
    except (OSError, ValueError, TypeError):
        parser.exit(1, "experiment.startup_or_evidence_failure\n")


if __name__ == "__main__":
    main()
