"""Audit existing moving-scene evidence without invoking game or provider code."""

import argparse
import collections
import copy
import hashlib
import json
import math
from pathlib import Path
import re
import statistics
from datetime import datetime, timezone


REN, SORA, FISH = "npc.fisher_ren", "npc.cook_sora", "fish.carp"
FILES = ("manifest.json", "status.json", "unity-scene.json", "contexts.jsonl",
         "provider-starts.jsonl", "provider-responses.jsonl", "proxy.jsonl")
DEFAULT_DIRS = ["Logs/moving-observation-live-1-2026-09-14", "Logs/moving-observation-live-2-2026-09-14"]


def digest(value):
    return hashlib.sha256(value).hexdigest()


def canonical(value):
    return json.dumps(value, ensure_ascii=False, allow_nan=False, sort_keys=True, separators=(",", ":"))


def identity(value):
    return value.replace("-", "").lower() if isinstance(value, str) else value


def counts(values):
    return dict(sorted(collections.Counter(str(value) for value in values).items()))


def measure(values):
    valid = sorted(v for v in values if type(v) in (int, float) and math.isfinite(v))
    return {"recorded": len(values), "available": len(valid), "missing": len(values) - len(valid),
            "sum": sum(valid) if valid else None, "min": min(valid) if valid else None,
            "mean": statistics.mean(valid) if valid else None,
            "median": statistics.median(valid) if valid else None,
            "p95NearestRank": valid[math.ceil(len(valid) * .95) - 1] if valid else None,
            "max": max(valid) if valid else None}


def embedded(record, field):
    if field not in record:
        return {"present": False, "value": None, "error": None, "sha256": None}
    raw = record[field]
    if raw is None:
        return {"present": True, "value": None, "error": None, "sha256": None}
    if not isinstance(raw, str):
        return {"present": True, "value": raw, "error": "expected_json_string", "sha256": None}
    try:
        value, error = json.loads(raw), None
    except (ValueError, TypeError) as exception:
        value, error = None, str(exception)
    return {"present": True, "value": value, "error": error, "sha256": digest(raw.encode("utf-8"))}


def time_names(value):
    if isinstance(value, list):
        return [time_names(item) for item in value]
    if not isinstance(value, dict):
        return value
    result = {key: time_names(item) for key, item in value.items() if key != "observedAtGameTotalMinutes"}
    if "observedAtGameTotalMinutes" in value:
        if "observedAtTotalMinutes" in result and result["observedAtTotalMinutes"] != value["observedAtGameTotalMinutes"]:
            result["conflictingObservedAtGameTotalMinutes"] = value["observedAtGameTotalMinutes"]
        else:
            result["observedAtTotalMinutes"] = value["observedAtGameTotalMinutes"]
    return result


def differences(left, right, path="$"):
    """Keep actual values and distinguish absent fields from JSON null."""
    if isinstance(left, dict) and isinstance(right, dict):
        result = []
        for key in sorted(set(left) | set(right)):
            target = path + "." + key
            if key not in left or key not in right:
                result.append({"path": target, "dispatchPresent": key in left,
                               "executionPresent": key in right, "dispatch": left.get(key), "execution": right.get(key)})
            else:
                result.extend(differences(left[key], right[key], target))
        return result
    if isinstance(left, list) and isinstance(right, list):
        result = []
        for index in range(max(len(left), len(right))):
            target = path + "[" + str(index) + "]"
            if index >= len(left) or index >= len(right):
                result.append({"path": target, "dispatchPresent": index < len(left), "executionPresent": index < len(right),
                               "dispatch": left[index] if index < len(left) else None,
                               "execution": right[index] if index < len(right) else None})
            else:
                result.extend(differences(left[index], right[index], target))
        return result
    if left == right and not (isinstance(left, bool) != isinstance(right, bool)):
        return []
    return [{"path": path, "dispatchPresent": True, "executionPresent": True, "dispatch": left, "execution": right}]


def group_differences(changes):
    groups = {}
    for item in changes:
        values = {key: value for key, value in item.items() if key != "path"}
        key = canonical(values)
        if key not in groups:
            groups[key] = {**values, "paths": []}
        groups[key]["paths"].append(item["path"])
    return list(groups.values())


def observation_comparison(context, call):
    execution = embedded(call, "executionObservationJson")
    dispatch = context.get("observation") if context.get("hasObservation") else None
    current = execution["value"]
    def summary(value):
        if not isinstance(value, dict):
            return None
        normalized = time_names(value)
        return {key: normalized.get(key) for key in ("observerId", "worldRunId", "observedAtTotalMinutes", "x", "y", "spaceId", "regionId", "listenerId")}
    result = {"dispatchSource": "contextJson.observation", "executionSource": "executionObservationJson",
              "dispatchSummary": summary(dispatch), "executionSummary": summary(current),
              "dispatchCanonicalSha256": digest(canonical(dispatch).encode("utf-8")),
              "executionCanonicalSha256": digest(canonical(current).encode("utf-8")) if execution["error"] is None and execution["present"] else None,
              "dispatchFactCount": len(dispatch.get("facts") or []) if isinstance(dispatch, dict) else None,
              "executionFactCount": len(current.get("facts") or []) if isinstance(current, dict) else None,
              "executionIsNull": current is None if execution["present"] and execution["error"] is None else None,
              "executionFieldPresent": execution["present"], "executionParseError": execution["error"],
              "executionJsonSha256": execution["sha256"], "differences": None,
              "comparisonStatus": "compared", "sameCoordinates": None, "sameKnownRegion": None}
    if not execution["present"]:
        result["comparisonStatus"] = "execution_field_missing"
    elif execution["error"]:
        result["comparisonStatus"] = "execution_json_invalid"
    elif current is None:
        result["comparisonStatus"] = "execution_null"
    elif not isinstance(dispatch, dict) or not isinstance(current, dict):
        result["comparisonStatus"] = "observation_shape_unavailable"
    else:
        changes = differences(time_names(dispatch), time_names(current))
        for item in changes:
            fact_match = re.match(r"\$\.facts\[(\d+)\]\.(\w+)$", item["path"])
            old_fact = (dispatch.get("facts") or [])[int(fact_match[1])] if fact_match and int(fact_match[1]) < len(dispatch.get("facts") or []) else {}
            new_fact = (current.get("facts") or [])[int(fact_match[1])] if fact_match and int(fact_match[1]) < len(current.get("facts") or []) else {}
            if item["path"] in ("$.schemaVersion", "$.hasRegion"):
                item["category"] = "dispatch_wire_metadata"
            elif item["dispatchPresent"] and item["executionPresent"] and (
                    (item["dispatch"] is None and item["execution"] == "")
                    or (item["execution"] is None and item["dispatch"] == "")):
                item["category"] = "null_vs_empty_string_retained"
                item["documentedNullableWireField"] = (item["path"] == "$.listenerId"
                    or (item["path"] == "$.regionId" and dispatch.get("hasRegion") is False)
                    or (fact_match is not None and (fact_match[2] in ("unit", "speakerId")
                        or (fact_match[2] == "value" and old_fact.get("valueType") == new_fact.get("valueType") == "unknown"))))
            elif item["path"].endswith(".observedAtTotalMinutes"):
                item["category"] = "historical_fact_time" if fact_match and (
                    old_fact.get("knowledge") in ("statement", "receipt") or new_fact.get("knowledge") in ("statement", "receipt")) else "resampling_time"
            elif item["path"] in ("$.x", "$.y"):
                item["category"] = "observer_coordinate"
            else:
                item["category"] = "other_field_change"
        result["differences"] = group_differences(changes)
        result["changedFieldCount"] = len(changes)
        result["differenceCategories"] = counts(item["category"] for item in changes)
        remaining = [item for item in changes if item["category"] not in ("dispatch_wire_metadata", "observer_coordinate", "resampling_time")
                     and not (item["category"] == "null_vs_empty_string_retained" and item.get("documentedNullableWireField"))]
        result["nonCoordinateRuntimeDifferences"] = group_differences(remaining)
        result["sameOtherFieldsAfterDocumentedWireAndResamplingNormalization"] = not remaining
        result["sameCoordinates"] = all(dispatch.get(key) == current.get(key) for key in ("x", "y"))
        result["sameKnownRegion"] = bool(dispatch.get("hasRegion") and dispatch.get("regionId")
                                          and dispatch.get("regionId") == current.get("regionId")
                                          and dispatch.get("spaceId") == current.get("spaceId"))
        result["executionBinding"] = {
            "observerMatchesRequest": current.get("observerId") == context.get("npcId"),
            "worldMatchesRequest": identity(current.get("worldRunId")) == identity(context.get("worldRunId")),
            "listenerMatchesRequest": current.get("listenerId") == (context.get("social") or {}).get("partnerId")}
    return result


def asset_map(rows):
    if not isinstance(rows, list) or not rows:
        return None
    result = {}
    for row in rows:
        stock = collections.Counter()
        for item in row["items"]:
            item_id, quantity = item.rsplit(":", 1)
            stock[item_id] += int(quantity)
        if row["id"] in result:
            raise ValueError("duplicate asset owner: " + row["id"])
        result[row["id"]] = {"coins": row["coins"], "items": dict(stock)}
    return result


def conservation(trial):
    try:
        initial, final = asset_map(trial.get("initialAssets")), asset_map(trial.get("finalAssets"))
    except (ValueError, KeyError, TypeError) as error:
        return {"available": False, "parseError": str(error), "conserved": None}
    if initial is None or final is None:
        return {"available": False, "conserved": None}
    def totals(owners):
        items = collections.Counter()
        for row in owners.values():
            items.update(row["items"])
        return {"coins": sum(row["coins"] for row in owners.values()), "items": dict(items)}
    left, right = totals(initial), totals(final)
    item_delta = {key: right["items"].get(key, 0) - left["items"].get(key, 0)
                  for key in sorted(set(left["items"]) | set(right["items"]))}
    other_same = {key: initial.get(key) == final.get(key) for key in set(initial) | set(final) if key not in (REN, SORA)}
    nonnegative = all(row["coins"] >= 0 and all(q >= 0 for q in row["items"].values()) for row in final.values())
    result = {"available": True, "initialTotals": left, "finalTotals": right,
              "coinDelta": right["coins"] - left["coins"], "itemDeltas": item_delta,
              "sameOwners": set(initial) == set(final), "otherOwnersUnchanged": other_same,
              "allFinalAmountsNonnegative": nonnegative, "participantDeltas": {}, "checkpointChecks": []}
    for owner in (REN, SORA):
        if owner in initial and owner in final:
            keys = set(initial[owner]["items"]) | set(final[owner]["items"])
            result["participantDeltas"][owner] = {"coins": final[owner]["coins"] - initial[owner]["coins"],
                "items": {key: final[owner]["items"].get(key, 0) - initial[owner]["items"].get(key, 0) for key in sorted(keys)}}
    start = trial.get("initial")
    points = [("observations[" + str(i) + "]", point) for i, point in enumerate(trial.get("observations") or [])]
    points += [(name, trial.get(name)) for name in ("terminal", "recovery")]
    if isinstance(start, dict):
        for name, point in points:
            if not isinstance(point, dict):
                continue
            transfer = 1 if point.get("receipt") == "resource.delivered" else 0
            expected = {"renFish": start.get("renFish", 0) - transfer, "soraFish": start.get("soraFish", 0) + transfer,
                        "renCoins": start.get("renCoins", 0) + 25 * transfer, "soraCoins": start.get("soraCoins", 0) - 25 * transfer}
            result["checkpointChecks"].append({"source": name, "gameTotalMinutes": point.get("gameTotalMinutes"),
                "receipt": point.get("receipt"), "actual": {key: point.get(key) for key in expected}, "expectedFromRecordedReceipt": expected,
                "matchesReceipt": all(point.get(key) == value for key, value in expected.items())})
    result["conserved"] = result["coinDelta"] == 0 and not any(item_delta.values()) and result["sameOwners"]
    return result


def call_key(context):
    return identity(context.get("worldRunId")), identity(context.get("decisionId")), context.get("step")


def index_rows(rows, key):
    result = collections.defaultdict(list)
    for number, row in enumerate(rows, 1):
        result[key(row)].append((number, row))
    return result


def audit_batch(directory):
    directory = Path(directory)
    data, hashes, load_errors, missing = {}, {}, [], []
    for name in FILES:
        path = directory / name
        if not path.exists():
            missing.append(name)
            continue
        raw = path.read_bytes()
        hashes[name] = digest(raw)
        try:
            data[name] = [json.loads(line) for line in raw.decode("utf-8-sig").splitlines()] if name.endswith(".jsonl") else json.loads(raw)
        except (ValueError, UnicodeError) as error:
            load_errors.append({"file": name, "error": str(error)})
    report = {"directory": directory.as_posix(), "missingFiles": missing, "loadErrors": load_errors,
              "evidenceSha256": hashes, "evidenceState": "awaiting_evidence" if not data else "partial",
              "worlds": [], "checks": {}, "counts": {name: len(value) for name, value in data.items() if name.endswith(".jsonl")}}
    unity, manifest, status = (data.get(name) for name in ("unity-scene.json", "manifest.json", "status.json"))
    report.update(unityStatus=unity.get("status") if isinstance(unity, dict) else None, manifest=manifest, proxyStatus=status)
    report["counts"]["actualProviderCalls"] = status.get("attemptedProviderCalls") if isinstance(status, dict) else None
    if missing or load_errors or not all(isinstance(item, dict) for item in (unity, manifest, status)):
        return report
    contexts, starts, responses, proxies = (data[name] for name in ("contexts.jsonl", "provider-starts.jsonl", "provider-responses.jsonl", "proxy.jsonl"))
    context_index = index_rows(contexts, lambda row: call_key(row.get("effectiveContext") or row.get("originalContext") or {}))
    start_index = index_rows(starts, lambda row: row.get("request"))
    response_index = index_rows(responses, lambda row: row.get("request"))
    proxy_index = index_rows(proxies, lambda row: row.get("call"))
    joined_requests, all_calls = [], []
    for trial_number, trial in enumerate(unity.get("trials") or [], 1):
        world = {key: copy.deepcopy(trial.get(key)) for key in ("ordinal", "scenario", "arm", "speechMode", "repetition", "worldRunId", "worldSeed",
                 "initialized", "behavior", "stopReason", "recovered", "deliveryObserved", "activelyEnded", "deadlineReleased", "terminalActivitiesReleased",
                 "schedulerCalls", "initial", "terminal", "recovery", "hostViolations", "renMemories", "soraMemories", "renMemoryRecords", "soraMemoryRecords")}
        world["source"] = "unity-scene.json:trials[" + str(trial_number - 1) + "]"
        world["assetAudit"] = conservation(trial)
        world["calls"] = []
        for call_number, call in enumerate(trial.get("calls") or [], 1):
            parsed = embedded(call, "contextJson")
            context = parsed["value"] if isinstance(parsed["value"], dict) else {}
            entries = context_index.get(call_key(context), [])
            c_line, candidate_context = entries[0] if len(entries) == 1 else (None, {})
            request = candidate_context.get("request")
            pstarts, presponses = start_index.get(request, []) if request is not None else [], response_index.get(request, []) if request is not None else []
            ps_line, ps = pstarts[0] if len(pstarts) == 1 else (None, {})
            pr_line, pr = presponses[0] if len(presponses) == 1 else (None, {})
            ptraces = proxy_index.get(ps.get("providerCall"), []) if ps else []
            pt_line, pt = ptraces[0] if len(ptraces) == 1 else (None, {})
            raw_candidate = embedded(pr, "content")
            unity_body = embedded(call, "rawReplyJson")
            normalized = copy.deepcopy(candidate_context.get("effectiveContext"))
            if isinstance(normalized, dict) and isinstance(normalized.get("expression"), dict):
                normalized["expression"].pop("mode", None)
            sent = embedded(call, "sentContextJson")
            if request is not None:
                joined_requests.append(request)
            record = {key: copy.deepcopy(call.get(key)) for key in ("npcId", "decisionId", "step", "phase", "operation", "text", "speechFrame", "error",
                      "hostCode", "candidateErrorCode", "decisionOutcomeCode", "responseStatusCode", "rawReplyTruncated", "elapsedMilliseconds")}
            record.update(source=world["source"] + ".calls[" + str(call_number - 1) + "]", arm=trial.get("arm"), worldRunId=trial.get("worldRunId"),
                contextParseError=parsed["error"], contextJsonSha256=parsed["sha256"], proxyRequest=request,
                matchingContextRows=len(entries), matchingProviderStarts=len(pstarts), matchingProviderResponses=len(presponses), matchingProxyRows=len(ptraces),
                sourceRows={"contexts.jsonl": c_line, "provider-starts.jsonl": ps_line, "provider-responses.jsonl": pr_line, "proxy.jsonl": pt_line},
                sentContextMatchesRecorded=sent["value"] == context if sent["error"] is None and sent["present"] and sent["value"] is not None else None,
                effectiveContextMatchesUnity=candidate_context.get("effectiveContext") == context if candidate_context else None,
                normalizedContextHashMatches=digest(canonical(normalized).encode("utf-8")) == candidate_context.get("normalizedContextSha256") if normalized is not None else None,
                providerCall=ps.get("providerCall"), providerStatus=pr.get("status"), protocolStatus=pt.get("status"), proxyCandidateErrorCode=pt.get("candidateErrorCode"),
                providerRawCandidate=raw_candidate["value"], providerCandidateParseError=raw_candidate["error"], providerContentSha256=raw_candidate["sha256"],
                proxyReturnedCandidate=pt.get("candidate"),
                unityResponseBody=unity_body["value"], unityResponseBodySha256=unity_body["sha256"], unityResponseBodyParseError=unity_body["error"],
                unityResponseBodyPresent=isinstance(call.get("rawReplyJson"), str) and bool(call["rawReplyJson"]),
                providerCandidateMatchesUnityBody=raw_candidate["value"] == unity_body["value"]
                    if raw_candidate["error"] is None and unity_body["error"] is None and unity_body["value"] is not None else None,
                proxyCandidateMatchesUnityBody=pt.get("candidate") == unity_body["value"]
                    if pt.get("candidate") is not None and unity_body["error"] is None and unity_body["value"] is not None else None,
                requestedModel=pt.get("requestedModel"), returnedModel=pt.get("returnedModel"),
                usage={key: pt.get(key) for key in ("promptTokens", "completionTokens", "totalTokens")},
                proxyElapsedMilliseconds=pt.get("elapsedMilliseconds"),
                requestMetadata={key: copy.deepcopy(context.get(key)) for key in ("worldRunId", "decisionId", "step", "remainingCalls", "revision",
                    "gameTotalMinutes", "previousResultCode", "candidateErrorCode", "triggers", "allowedOperations")},
                requestMaxConversationTurns=(context.get("social") or {}).get("maxTurns"),
                observation=observation_comparison(context, call))
            record["joinIdentityMatches"] = all(identity(row.get("decisionId")) == identity(context.get("decisionId")) and row.get("step") == context.get("step")
                                               for row in (ps, pr, pt)) if ps and pr and pt else None
            before_candidate, after_candidate = raw_candidate["value"], pt.get("candidate")
            record["proxyFieldFiltering"] = None
            if isinstance(before_candidate, dict) and isinstance(after_candidate, dict):
                record["proxyFieldFiltering"] = {"removedFields": {key: value for key, value in before_candidate.items() if key not in after_candidate},
                    "addedFields": {key: value for key, value in after_candidate.items() if key not in before_candidate},
                    "changedKeptFields": {key: {"provider": before_candidate[key], "proxy": after_candidate[key]}
                                          for key in set(before_candidate) & set(after_candidate) if before_candidate[key] != after_candidate[key]}}
            world["calls"].append(record)
            all_calls.append(record)
        world_calls = world["calls"]
        previous_by_npc = {}
        world["postSpeechFailureDecisions"] = []
        world["postFailureDecisions"] = []
        for call in world_calls:
            prior = previous_by_npc.get(call.get("npcId"))
            prior_failed = prior and (bool(prior.get("error")) or (isinstance(prior.get("hostCode"), str)
                and prior["hostCode"].startswith("speech.")))
            if prior_failed:
                old_request, new_request = prior["requestMetadata"], call["requestMetadata"]
                same_decision = identity(old_request.get("decisionId")) == identity(new_request.get("decisionId"))
                prior_execution = prior["observation"]["executionSummary"]
                followup = {"failedCall": prior["source"], "failureHostCode": prior["hostCode"], "failureClientError": prior.get("error"),
                    "failedOriginalCandidate": prior["providerRawCandidate"], "failedRequest": old_request,
                    "failedExecutionGameMinute": prior_execution.get("observedAtTotalMinutes") if isinstance(prior_execution, dict) else None,
                    "followingCall": call["source"], "followingRequest": new_request,
                    "sameDecisionId": same_decision, "hasCandidateCorrectionFeedback": bool(new_request.get("candidateErrorCode")),
                    "classification": "same_decision_continuation" if same_decision else
                        "new_decision_with_recorded_world_event" if new_request.get("triggers") else "new_decision_without_recorded_event"}
                world["postFailureDecisions"].append(followup)
                if isinstance(prior.get("hostCode"), str) and prior["hostCode"].startswith("speech."):
                    world["postSpeechFailureDecisions"].append(copy.deepcopy(followup))
            previous_by_npc[call.get("npcId")] = call
        ending_calls = [call["source"] for call in world_calls if call.get("operation") == "end_conversation" and call.get("hostCode") == "meeting.end_conversation"]
        final_transcript = (trial.get("terminal") or {}).get("transcript") or []
        turn_limits = [c["requestMaxConversationTurns"] for c in world_calls if type(c["requestMaxConversationTurns"]) is int]
        at_limit = bool(turn_limits and len(final_transcript) >= turn_limits[-1])
        world["endingEvidence"] = {"recordedActivelyEnded": trial.get("activelyEnded"), "successfulEndConversationCalls": ending_calls,
            "terminalState": (trial.get("terminal") or {}).get("state"), "terminalTranscriptLines": len(final_transcript),
            "lastRecordedMaxTurns": turn_limits[-1] if turn_limits else None,
            "classification": "candidate_end_conversation" if ending_calls else "host_turn_limit_completion"
                if (trial.get("terminal") or {}).get("state") == "Completed" and at_limit else "no_successful_end_conversation_candidate"}
        initial = trial.get("initial") or {}
        expected_fish = {"available": 2, "seller_empty": 0}.get(trial.get("scenario"))
        world["initializationChecks"] = {"hostInitialized": trial.get("initialized"),
            "recordedMinute735": initial.get("gameTotalMinutes") == 735,
            "noInitialMeeting": initial.get("state") == "None" and not initial.get("transcript"),
            "expectedParticipantAssets": expected_fish is not None and all(initial.get(key) == value for key, value in
                 {"renFish": expected_fish, "soraFish": 0, "renCoins": 0, "soraCoins": 50}.items())}
        world["callCounts"] = {"unity": len(world_calls), "withSentContext": sum(embedded(c, "sentContextJson")["value"] is not None for c in trial.get("calls") or []),
            "providerStarts": sum(1 for row in starts if identity(row.get("worldRunId")) == identity(trial.get("worldRunId"))),
            "continuedSteps": sum(type(c.get("step")) is int and c["step"] > 1 for c in world_calls),
            "within12UnityRequests": len(world_calls) <= 12}
        world["candidateToHost"] = counts(str((c.get("providerRawCandidate") or {}).get("operation") if isinstance(c.get("providerRawCandidate"), dict) else None)
                                         + " -> " + str(c.get("hostCode")) for c in world_calls)
        world["dispatchExecutionComparisons"] = counts(c["observation"]["comparisonStatus"] for c in world_calls)
        world["rawModelTokens"] = {key: measure([c["usage"][key] for c in world_calls]) for key in ("promptTokens", "completionTokens", "totalTokens")}
        speech = {}
        for source in (trial.get("observations") or []) + [trial.get("terminal") or {}, trial.get("recovery") or {}]:
            for text in source.get("transcript") or []:
                speech[text] = True
        world["distinctTranscriptLines"] = list(speech)
        world["recordedTimeline"] = [{"source": world["source"] + ".observations[" + str(index) + "]",
            **{key: point.get(key) for key in ("gameTotalMinutes", "state", "receipt", "renRoute", "soraRoute", "renTarget", "soraTarget")},
            "transcriptLineCount": len(point.get("transcript") or [])} for index, point in enumerate(trial.get("observations") or [])]
        world["recordedSpokenMemories"] = {npc: [m for m in trial.get(field) or [] if m.get("kind") == "meeting.spoken"]
                                           for npc, field in ((REN, "renMemoryRecords"), (SORA, "soraMemoryRecords"))}
        report["worlds"].append(world)
    calls = status.get("attemptedProviderCalls")
    admitted = [row["request"] for row in contexts if "request" in row]
    expected_order = ["available:F", "available:S", "seller_empty:S", "seller_empty:F"]
    checks = {"manifestIsStandaloneScene48": manifest.get("stage") == "scene" and manifest.get("standaloneScene") is True
              and manifest.get("plannedTotalCallCeiling") == 48 and "fixedEvidence" not in manifest,
              "providerCallsAtMost48": type(calls) is int and 0 <= calls <= 48,
              "admittedRequestsAtMost48": len(admitted) <= 48,
              "allProviderCountsMatchStatus": len(starts) == len(responses) == len(proxies) == calls,
              "providerIdsContiguous": sorted(row.get("providerCall", -1) for row in starts) == list(range(1, len(starts) + 1))
                 and sorted(row.get("providerCall", -1) for row in responses) == list(range(1, len(responses) + 1)),
              "admittedRequestIdsContiguous": sorted(admitted) == list(range(1, len(admitted) + 1)),
              "noDuplicateProviderRequests": len({row.get("request") for row in starts}) == len(starts),
              "allAdmittedRequestsHaveOneUnityCall": sorted(joined_requests) == sorted(admitted),
              "allMatchedContextHashesRecompute": all(c["normalizedContextHashMatches"] is True for c in all_calls if c["proxyRequest"] is not None),
              "allReceivedSuccessfulBodiesMatchProxyCandidates": all(c["proxyCandidateMatchesUnityBody"] is True for c in all_calls
                  if c["responseStatusCode"] == 200 and c["unityResponseBodyPresent"]),
              "proxyOnlyRemovesFieldsWithoutChangingKeptValues": all(c["proxyFieldFiltering"] is not None
                  and not c["proxyFieldFiltering"]["addedFields"] and not c["proxyFieldFiltering"]["changedKeptFields"]
                  for c in all_calls if c["protocolStatus"] == "passed"),
              "allWorldsWithin12UnityRequests": all(w["callCounts"]["within12UnityRequests"] for w in report["worlds"]),
              "allWorldsWithin12ProviderCalls": all(w["callCounts"]["providerStarts"] <= 12 for w in report["worlds"]),
              "fourDistinctWorlds": len(report["worlds"]) == len({identity(w["worldRunId"]) for w in report["worlds"]}) == 4,
              "registeredTrialOrderMatches": [str(w["scenario"]) + ":" + str(w["arm"]) for w in report["worlds"]] == expected_order,
              "allInitialized": all(w["initialized"] is True for w in report["worlds"]),
              "allRecovered": all(w["recovered"] is True for w in report["worlds"]),
              "allAssetTotalsConserved": all(w["assetAudit"].get("conserved") is True for w in report["worlds"]),
              "allOtherAssetOwnersUnchanged": all(w["assetAudit"].get("available") is True
                  and all(w["assetAudit"]["otherOwnersUnchanged"].values()) for w in report["worlds"]),
              "allOtherParticipantItemsUnchanged": all(w["assetAudit"].get("available") is True and all(
                  value == 0 for delta in w["assetAudit"]["participantDeltas"].values() for item, value in delta["items"].items() if item != FISH)
                  for w in report["worlds"]),
              "allRecordedReceiptCheckpointDeltasMatch": all(w["assetAudit"].get("available") is True
                  and all(c["matchesReceipt"] for c in w["assetAudit"]["checkpointChecks"]) for w in report["worlds"]),
              "allHostViolationListsEmpty": all(w["hostViolations"] == [] for w in report["worlds"]),
              "tokenFieldsPresentAndConsistent": all(all(type(r.get(k)) is int and r[k] >= 0 for k in ("promptTokens", "completionTokens", "totalTokens"))
                   and r["promptTokens"] + r["completionTokens"] == r["totalTokens"] for r in proxies),
              "sourceFilesUnchangedWhileReading": all((directory / name).exists() and digest((directory / name).read_bytes()) == value for name, value in hashes.items())}
    report["checks"] = checks
    terminal = status.get("runState") in ("completed", "stopped") and status.get("inflight") == 0 and unity.get("status") in ("host_checks_passed", "host_checks_failed")
    report["evidenceState"] = "complete" if terminal and len(report["worlds"]) == 4 else "partial"
    report["failedChecks"] = [name for name, passed in checks.items() if not passed]
    report["unmatchedAdmittedRequests"] = sorted(set(admitted) - set(joined_requests))
    report["unmatchedUnityCalls"] = [c["source"] for c in all_calls if c["matchingContextRows"] != 1]
    report["counts"].update(unityTrials=len(report["worlds"]), unityCalls=len(all_calls), admittedRequests=len(admitted), actualProviderCalls=calls,
        initializedWorlds=sum(w["initialized"] is True for w in report["worlds"]), recoveredWorlds=sum(w["recovered"] is True for w in report["worlds"]),
        deliveredWorlds=sum(w["deliveryObserved"] is True for w in report["worlds"]), worldsWithTranscript=sum(bool(w["distinctTranscriptLines"]) for w in report["worlds"]))
    report["modelMeasurements"] = {"requestedModels": counts(r.get("requestedModel") for r in proxies),
        "returnedModels": counts(r.get("returnedModel") for r in proxies), "protocolStatuses": counts(r.get("status") for r in proxies),
        "candidateErrors": counts(r.get("candidateErrorCode") for r in proxies if r.get("candidateErrorCode") is not None),
        "tokens": {key: measure([r.get(key) for r in proxies]) for key in ("promptTokens", "completionTokens", "totalTokens")},
        "proxyElapsedMilliseconds": measure([r.get("elapsedMilliseconds") for r in proxies])}
    report["modelMeasurements"]["byArm"] = {arm: {
        "matchedProviderCalls": sum(c["matchingProxyRows"] == 1 for c in all_calls if c["arm"] == arm),
        "tokens": {key: measure([c["usage"][key] for c in all_calls if c["arm"] == arm])
                   for key in ("promptTokens", "completionTokens", "totalTokens")},
        "proxyElapsedMilliseconds": measure([c["proxyElapsedMilliseconds"] for c in all_calls if c["arm"] == arm]),
        "hostCodesByCall": counts(c["hostCode"] for c in all_calls if c["arm"] == arm)} for arm in ("F", "S")}
    applied_codes = {"meeting.invited", "meeting.accept_invite", "meeting.decline_invite", "meeting.say",
                     "meeting.deliver", "meeting.cancel_exchange", "meeting.end_conversation"}
    report["responseLayers"] = {"providerContentRecorded": sum(r.get("status") == "content_recorded" for r in responses),
        "proxyProtocolAccepted": sum(r.get("status") == "passed" for r in proxies),
        "unityHttp200Recorded": sum(c["responseStatusCode"] == 200 for c in all_calls),
        "unityBodiesRecorded": sum(c["unityResponseBodyPresent"] for c in all_calls),
        "unityParsedOperations": sum(bool(c["operation"]) for c in all_calls),
        "appliedMeetingActions": sum(c["hostCode"] in applied_codes for c in all_calls),
        "semanticSpeechRejections": [c["source"] for c in all_calls if isinstance(c["hostCode"], str) and c["hostCode"].startswith("speech.")],
        "providerAcceptedButNoUnityBody": [{"source": c["source"], "decisionId": c["decisionId"], "providerCall": c["providerCall"],
            "hostCode": c["hostCode"], "clientError": c["error"], "responseStatusCode": c["responseStatusCode"],
            "rawCandidate": c["providerRawCandidate"], "usage": c["usage"]} for c in all_calls
            if c["protocolStatus"] == "passed" and not c["unityResponseBodyPresent"]]}
    report["candidateFiltering"] = [{"source": c["source"], "proxyRequest": c["proxyRequest"], "providerCall": c["providerCall"],
        "operation": c["providerRawCandidate"].get("operation"), "removedFieldNames": sorted(c["proxyFieldFiltering"]["removedFields"])}
        for c in all_calls if c["proxyFieldFiltering"] and c["proxyFieldFiltering"]["removedFields"]]
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directories", nargs="*", help="Evidence directories; defaults to the two registered moving-scene batches.")
    parser.add_argument("--output", type=Path, default=Path("Logs/moving-scene-audit.json"))
    args = parser.parse_args()
    directories = args.directories or DEFAULT_DIRS
    source_paths = {str((Path(directory) / name).resolve()) for directory in directories for name in FILES}
    if str(args.output.resolve()) in source_paths or args.output.resolve() == Path(__file__).resolve():
        parser.error("The output must not replace input evidence or this script.")
    batches = [audit_batch(directory) for directory in directories]
    provider_counts = [batch.get("counts", {}).get("actualProviderCalls") for batch in batches]
    known_counts = [value for value in provider_counts if type(value) is int]
    worlds = [world for batch in batches for world in batch["worlds"]]
    all_available = all(batch["evidenceState"] == "complete" for batch in batches)
    report = {"schemaVersion": 1, "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "scriptSha256": digest(Path(__file__).read_bytes()),
        "scope": "Read existing local evidence only. No model calls, credential access, production imports or game execution.",
        "study": {"registeredBatches": 2, "registeredWorlds": 8, "perBatchCallCap": 48, "perWorldRequestCap": 12, "totalCallCap": 96},
        "batches": batches, "summary": {"providedDirectories": len(batches), "completeBatches": sum(b["evidenceState"] == "complete" for b in batches),
            "observedWorlds": len(worlds), "distinctObservedWorldIds": len({identity(w["worldRunId"]) for w in worlds}),
            "allEightWorldsHaveDistinctIds": len(worlds) == len({identity(w["worldRunId"]) for w in worlds}) == 8 if all_available and len(batches) == 2 else None,
            "knownActualProviderCalls": sum(known_counts) if known_counts else None, "missingBatchCallCounts": len(provider_counts) - len(known_counts),
            "actualProviderCalls": sum(known_counts) if len(known_counts) == len(batches) else None,
            "withinRegisteredTotalCap": sum(known_counts) <= 96 if len(known_counts) == len(batches) else None,
            "initializedWorlds": sum(w["initialized"] is True for w in worlds) if worlds else None,
            "recoveredWorlds": sum(w["recovered"] is True for w in worlds) if worlds else None,
            "deliveredWorlds": sum(w["deliveryObserved"] is True for w in worlds) if worlds else None,
            "worldsWithTranscript": sum(bool(w["distinctTranscriptLines"]) for w in worlds) if worlds else None,
            "completeRegisteredStudyEvidence": all_available and len(batches) == 2 and len(worlds) == 8},
        "interpretation": ["Missing files or unfinished batches are reported as awaiting_evidence or partial; no absent result is inferred as zero or success.",
            "Observed-time aliases are normalized for comparison only. Original objects remain in the source evidence; call references, SHA-256 hashes and field differences retain provenance and null values. Missing fields have separate presence flags.",
            "Difference entries group paths only when category, presence flags and exact before/after values all match. Expand paths to reconstruct every field difference; changedFieldCount counts fields rather than groups.",
            "Dispatch uses JsonUtility empty-string substitutions; execution uses null-preserving DataContract JSON. Null/empty differences are retained and classified, never silently removed.",
            "The additional runtime-field summary excludes only dispatch metadata, documented nullable wire encodings, coordinate changes and nonhistorical sampling-time refresh. Statement and receipt times remain material differences.",
            "ExecutionObservation is the result of the execution-time read, including rejected invalid objects. Null does not imply acceptance; consult hostCode and decisionOutcomeCode.",
            "hostCode may be the final decision outcome projected onto earlier query steps. candidateErrorCode remains per call; step and all source row numbers are retained.",
            "Provider raw candidates and proxy return candidates are separate layers. The pre-existing action whitelist can remove unused fields from non-say candidates; removed fields and any altered retained values are reported separately. This is not automatic rewriting of expression fields.",
            "Token totals come from proxy measurements. A missing usage field is not zero. Continued steps are reported separately from duplicate transport requests.",
            "Asset audits compare recorded endpoints and checkpoints; they do not establish unrecorded intermediate state. Host violations and physical arrival records are retained.",
            "Transcript presence, host checks and recovery do not measure factual correctness or conversation quality. No expression-quality labels are inferred."]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, allow_nan=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": args.output.as_posix(), "summary": report["summary"], "states": [b["evidenceState"] for b in batches]}, ensure_ascii=True))


if __name__ == "__main__":
    main()
