using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using CozyTown.Runtime.Save;
using UnityEngine;

namespace CozyTown.Unity.Experiments
{
    internal static class ExperimentReplayComparison
    {
        internal static string Results(IReadOnlyList<AgentExperimentResult> expected, IReadOnlyList<AgentExperimentResult> actual)
        {
            if (expected == null || actual == null || expected.Count != actual.Count)
                return "Replay decision result count differs from the recording.";
            var recordedIds = new DecisionTraceIdentifiers();
            var actualIds = new DecisionTraceIdentifiers();
            for (int i = 0; i < expected.Count; i++)
            {
                var before = expected[i];
                var after = actual[i];
                if (before == null || after == null || before.npcId != after.npcId || before.code != after.code
                    || before.calls != after.calls || before.startedRealSeconds != after.startedRealSeconds
                    || before.finishedRealSeconds != after.finishedRealSeconds || before.candidateErrors == null
                    || after.candidateErrors == null || !before.candidateErrors.SequenceEqual(after.candidateErrors))
                    return "Replay decision result " + i + " differs in resident, outcome, calls, timing or candidate failures.";
                try
                {
                    if (string.IsNullOrEmpty(before.requestJson) || string.IsNullOrEmpty(after.requestJson)
                        || recordedIds.Normalize(before.requestJson) != actualIds.Normalize(after.requestJson))
                        return "Replay decision result " + i + " has a different request context.";
                    string beforeIdentity = JsonUtility.ToJson(new ResultIdentity { worldRunId = before.worldRunId, decisionId = before.decisionId });
                    string afterIdentity = JsonUtility.ToJson(new ResultIdentity { worldRunId = after.worldRunId, decisionId = after.decisionId });
                    if (recordedIds.Normalize(beforeIdentity) != actualIds.Normalize(afterIdentity))
                        return "Replay decision result " + i + " has different request correlation.";
                    if (string.IsNullOrEmpty(before.replyJson) != string.IsNullOrEmpty(after.replyJson))
                        return "Replay decision result " + i + " has different reply presence.";
                    if (!string.IsNullOrEmpty(before.replyJson)
                        && JsonTree(recordedIds.RemapResponse(before.replyJson, actualIds)) != JsonTree(after.replyJson))
                        return "Replay decision result " + i + " has a different reply.";
                    if (string.IsNullOrEmpty(before.executionObservationJson) != string.IsNullOrEmpty(after.executionObservationJson))
                        return "Replay decision result " + i + " has different execution observation presence.";
                    if (!string.IsNullOrEmpty(before.executionObservationJson)
                        && recordedIds.Normalize("{\"observation\":" + before.executionObservationJson + "}")
                            != actualIds.Normalize("{\"observation\":" + after.executionObservationJson + "}"))
                        return "Replay decision result " + i + " has different execution facts.";
                }
                catch (Exception exception) when (exception is ArgumentException || exception is XmlException
                    || exception is FormatException || exception is InvalidOperationException)
                {
                    return "Replay decision result " + i + " contains invalid context or response data.";
                }
            }
            return null;
        }

        internal static string Snapshots(GameSaveSnapshot expected, GameSaveSnapshot actual)
        {
            if (expected == null || actual == null) return "Replay requires both recorded and current snapshots.";
            try
            {
                return NormalizeSnapshot(JsonFileSaveStorage.SerializeSnapshot(expected))
                    == NormalizeSnapshot(JsonFileSaveStorage.SerializeSnapshot(actual))
                    ? null : "Replay world state differs from the recorded snapshot.";
            }
            catch (Exception exception) when (exception is ArgumentException || exception is XmlException
                || exception is FormatException || exception is InvalidOperationException)
            {
                return "Replay snapshot cannot be compared because its data is invalid.";
            }
        }

        private static string NormalizeSnapshot(string json)
        {
            using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json),
                new XmlDictionaryReaderQuotas { MaxDepth = 64, MaxStringContentLength = Math.Max(32768, json.Length) });
            var document = new XmlDocument();
            document.Load(reader);
            var meetings = new Dictionary<Guid, string>();
            var activities = new Dictionary<Guid, string>();
            foreach (string path in new[] {
                "/root/completeWorld/meetings/meetings/item/id",
                "/root/completeWorld/meetings/latestResults/item/id",
                "/root/completeWorld/meetings/latestByResident/item/meetingId",
                "/root/completeWorld/meetings/memories/item/memories/item/meetingId",
                "/root/completeWorld/decisions/residents/item/current/meetingId",
                "/root/completeWorld/decisions/residents/item/pending/meetingId",
                "/root/completeWorld/decisions/residents/item/waitingTurn/meetingId" })
                NormalizeIds(path, meetings, "meeting");
            foreach (string path in new[] {
                "/root/completeWorld/world/residents/item/activity/activityId",
                "/root/completeWorld/meetings/meetings/item/initiatorActivityId",
                "/root/completeWorld/meetings/meetings/item/partnerActivityId" })
                NormalizeIds(path, activities, "activity");
            return document.DocumentElement.OuterXml;

            void NormalizeIds(string path, Dictionary<Guid, string> identifiers, string kind)
            {
                foreach (XmlNode node in document.SelectNodes(path))
                {
                    if (!Guid.TryParse(node.InnerText, out Guid id))
                        throw new FormatException("Snapshot identity is not a GUID.");
                    if (id == Guid.Empty) continue;
                    if (!identifiers.TryGetValue(id, out string value)) identifiers.Add(id, value = kind + ":" + identifiers.Count);
                    node.InnerText = value;
                }
            }
        }

        private static string JsonTree(string json) => DecisionTraceIdentifiers.ReadJson(json).DocumentElement.OuterXml;

        [Serializable]
        private sealed class ResultIdentity { public string worldRunId, decisionId; }
    }
}
