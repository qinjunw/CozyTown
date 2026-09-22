using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Npc;

namespace CozyTown.Unity.Experiments
{
    [Serializable]
    public sealed class AgentExperimentContextDivergence
    {
        public string path, reason;
        public bool hasFreeTextCall, hasStructuredFactsCall;
        public bool hasFreeTextGameMinutes, hasStructuredFactsGameMinutes;
        public int freeTextCallOrdinal = -1, structuredFactsCallOrdinal = -1;
        public int freeTextTick = -1, structuredFactsTick = -1;
        public double freeTextGameMinutes, structuredFactsGameMinutes;
        public double freeTextRealSeconds, structuredFactsRealSeconds;
    }

    public static class AgentExperimentPairComparison
    {
        // This fresh-start comparison does not replace runtime checks of actual dispatch counters.
        public static string CompareInitial(GameSaveSnapshot freeText, GameSaveSnapshot structuredFacts,
            double freeTextRealSchedulerTime, double structuredFactsRealSchedulerTime)
        {
            if (freeTextRealSchedulerTime != 0 || structuredFactsRealSchedulerTime != 0)
                return "Initial realSchedulerTime must be zero in both worlds.";
            try
            {
                string invalid = InitialIssue(freeText, NpcSpeechMode.FreeText) ?? InitialIssue(structuredFacts, NpcSpeechMode.StructuredFacts);
                if (invalid != null) return invalid;
                var first = ReadSnapshot(freeText);
                var second = ReadSnapshot(structuredFacts);
                second.SelectSingleNode("/root/completeWorld/decisions/speechMode").InnerText =
                    first.SelectSingleNode("/root/completeWorld/decisions/speechMode").InnerText;
                string path = FirstDifference(first.DocumentElement, second.DocumentElement, string.Empty);
                return path == null ? null : "Initial snapshots differ at " + path + ".";
            }
            catch (Exception exception) when (DataError(exception))
            {
                return "Initial snapshot cannot be compared: " + exception.Message;
            }
        }

        // Null describes the recorded request prefix only, not outputs, final state or future contexts.
        public static AgentExperimentContextDivergence FirstContextDivergence(DecisionTrace freeText, DecisionTrace structuredFacts)
        {
            if (freeText == null || structuredFacts == null || freeText.schemaVersion != 1 || structuredFacts.schemaVersion != 1)
                return Difference("/schemaVersion", "Two schema 1 traces are required.");
            if (freeText.calls == null || structuredFacts.calls == null)
                return Difference("/calls", "Both recorded call collections are required.");
            if (string.IsNullOrWhiteSpace(freeText.clientConfiguration) || freeText.clientConfiguration != structuredFacts.clientConfiguration)
                return Difference("/clientConfiguration", "The paired decision client configurations differ or are missing.");
            var freeIds = new DecisionTraceIdentifiers();
            var structuredIds = new DecisionTraceIdentifiers();
            for (int index = 0; index < Math.Max(freeText.calls.Count, structuredFacts.calls.Count); index++)
            {
                var first = index < freeText.calls.Count ? freeText.calls[index] : null;
                var second = index < structuredFacts.calls.Count ? structuredFacts.calls[index] : null;
                if (first == null || second == null)
                    return Difference("/calls/" + index, "The recorded request prefix has a missing call.", first, second);
                if (first.callOrdinal != index || second.callOrdinal != index)
                    return Difference("/callOrdinal", "Calls must remain in their recorded dispatch order.", first, second);
                XmlDocument firstRequest = null, secondRequest = null;
                try
                {
                    firstRequest = ReadRequest(first.requestJson);
                    secondRequest = ReadRequest(second.requestJson);
                    string invalid = RequestIssue(firstRequest, "free_text") ?? RequestIssue(secondRequest, "structured_facts");
                    if (invalid != null) return Difference(invalid, "A request has an invalid comparison field.", first, second, firstRequest, secondRequest);
                    var freeNormalized = NormalizeRequest(firstRequest, freeIds);
                    var structuredNormalized = NormalizeRequest(secondRequest, structuredIds);
                    string path = FirstDifference(freeNormalized.DocumentElement, structuredNormalized.DocumentElement, string.Empty);
                    if (path != null) return Difference(path, "Recorded request contexts first differ at this call; later contexts are not compared.",
                        first, second, firstRequest, secondRequest);
                }
                catch (Exception exception) when (DataError(exception))
                {
                    return Difference("/requestJson", "A recorded request cannot be compared: " + exception.Message,
                        first, second, firstRequest, secondRequest);
                }
            }
            return null;
        }

        private static string InitialIssue(GameSaveSnapshot snapshot, NpcSpeechMode mode)
        {
            if (snapshot == null || snapshot.SchemaVersion != 4 || snapshot.SourceSchemaVersion != 4 || snapshot.CompleteWorld == null)
                return "A fresh schema 4 initial snapshot is required.";
            var complete = snapshot.CompleteWorld;
            if (!complete.DecisionsEnabled || complete.Decisions == null || complete.Decisions.SpeechMode != mode)
                return "Initial /completeWorld/decisions/speechMode must be F then S.";
            var ids = new[] { DefaultMvpIds.Npcs.Shopkeeper, DefaultMvpIds.Npcs.Farmer, DefaultMvpIds.Npcs.Fisher, DefaultMvpIds.Npcs.Cook };
            bool HasFour(IEnumerable<string> residents) => residents != null && residents.OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(ids.OrderBy(id => id, StringComparer.Ordinal));
            if (!HasFour(complete.World?.Residents?.Select(resident => resident?.NpcId))
                || !HasFour(complete.Residents?.Select(resident => resident?.NpcId))
                || !HasFour(complete.Decisions.Residents?.Select(resident => resident?.NpcId)))
                return "Initial world, bodies and decision profiles must contain all four configured residents.";
            if (!complete.MeetingsEnabled || complete.Meetings == null)
                return "Initial /completeWorld/meetings must be configured.";
            var meetings = complete.Meetings;
            if (complete.World.Residents.Any(resident => resident.Activity != null)
                || meetings.Meetings == null || meetings.Meetings.Count != 0
                || meetings.LatestResults == null || meetings.LatestResults.Count != 0
                || meetings.LatestByResident == null || meetings.LatestByResident.Count != 0
                || meetings.Memories == null || meetings.Memories.Any(resident => resident?.Memories == null || resident.Memories.Count != 0))
                return "Initial activity, meeting and memory history must be empty.";
            if (complete.Decisions.Residents.Any(resident => resident.RemainingCooldownSeconds != 0
                || !string.IsNullOrEmpty(resident.LastResultCode) || resident.WaitingTurn != null
                || HasExecuted(resident.Current) || HasExecuted(resident.Pending)))
                return "Initial decision history must contain no dispatched calls, prior result, waiting turn or cooldown.";
            return null;
        }

        private static bool HasExecuted(NpcDecisionProgressSnapshot progress) => progress != null
            && (progress.Calls != 0 || progress.NextStep != 1 || progress.HasLocationDetails
                || !string.IsNullOrEmpty(progress.PreviousResultCode) || !string.IsNullOrEmpty(progress.CandidateErrorCode)
                || progress.CandidateErrorCodes == null || progress.CandidateErrorCodes.Count != 0 || progress.MeetingId != Guid.Empty);

        private static XmlDocument ReadSnapshot(GameSaveSnapshot snapshot)
        {
            string json = JsonFileSaveStorage.SerializeSnapshot(snapshot);
            using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json),
                new XmlDictionaryReaderQuotas { MaxDepth = 64, MaxStringContentLength = Math.Max(32768, json.Length) });
            var document = new XmlDocument();
            document.Load(reader);
            return document;
        }

        private static XmlDocument ReadRequest(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > ProxyNpcDecisionJsonCodec.MaximumRequestBytes)
                throw new FormatException("A bounded request JSON object is required.");
            var document = DecisionTraceIdentifiers.ReadJson(json);
            if (document.DocumentElement?.GetAttribute("type") != "object") throw new FormatException("The request must be an object.");
            return document;
        }

        private static string RequestIssue(XmlDocument document, string mode)
        {
            if (document.SelectSingleNode("/root/expression/mode")?.InnerText != mode) return "/expression/mode";
            var version = document.SelectSingleNode("/root/expression/schemaVersion") as XmlElement;
            if (version?.GetAttribute("type") != "number" || version.InnerText != "1") return "/expression/schemaVersion";
            foreach (string path in new[] { "/worldRunId", "/decisionId", "/observation/worldRunId", "/social/meetingId" })
            {
                var node = document.SelectSingleNode("/root" + path) as XmlElement;
                if (node == null && (path == "/observation/worldRunId" || path == "/social/meetingId")) continue;
                if (path == "/social/meetingId" && node?.GetAttribute("type") == "string" && node.InnerText.Length == 0) continue;
                if (node?.GetAttribute("type") != "string" || !Guid.TryParse(node.InnerText, out var id)
                    || (id == Guid.Empty && path != "/social/meetingId")) return path;
            }
            return TryGameMinutes(document, out _) ? null : "/gameTotalMinutes";
        }

        private static XmlDocument NormalizeRequest(XmlDocument document, DecisionTraceIdentifiers ids)
        {
            var copy = (XmlDocument)document.CloneNode(true);
            foreach (string path in new[] { "/root/worldRunId", "/root/decisionId", "/root/observation/worldRunId", "/root/social/meetingId" })
            {
                var node = copy.SelectSingleNode(path);
                if (node == null) continue;
                if (path == "/root/social/meetingId" && node.InnerText.Length == 0) continue;
                var id = Guid.Parse(node.InnerText);
                node.InnerText = id == Guid.Empty ? string.Empty : id.ToString("N");
            }
            copy.SelectSingleNode("/root/expression/mode").InnerText = "paired_expression_mode";
            var normalized = new XmlDocument();
            normalized.LoadXml(ids.Normalize(DecisionTraceIdentifiers.WriteJson(copy)));
            return normalized;
        }

        private static string FirstDifference(XmlElement first, XmlElement second, string path)
        {
            if (first == null || second == null || first.GetAttribute("type") != second.GetAttribute("type")) return path;
            string type = first.GetAttribute("type");
            if (type == "object")
            {
                var left = first.ChildNodes.OfType<XmlElement>().ToDictionary(node => node.Name, StringComparer.Ordinal);
                var right = second.ChildNodes.OfType<XmlElement>().ToDictionary(node => node.Name, StringComparer.Ordinal);
                foreach (string name in left.Keys.Union(right.Keys).OrderBy(name => name, StringComparer.Ordinal))
                {
                    left.TryGetValue(name, out var before);
                    right.TryGetValue(name, out var after);
                    string difference = FirstDifference(before, after, path + "/" + name.Replace("~", "~0").Replace("/", "~1"));
                    if (difference != null) return difference;
                }
                return null;
            }
            if (type == "array")
            {
                var left = first.ChildNodes.OfType<XmlElement>().ToArray();
                var right = second.ChildNodes.OfType<XmlElement>().ToArray();
                for (int index = 0; index < Math.Max(left.Length, right.Length); index++)
                {
                    string difference = FirstDifference(index < left.Length ? left[index] : null,
                        index < right.Length ? right[index] : null, path + "/" + index);
                    if (difference != null) return difference;
                }
                return null;
            }
            return first.InnerText == second.InnerText ? null : path;
        }

        private static AgentExperimentContextDivergence Difference(string path, string reason,
            DecisionTraceCall first = null, DecisionTraceCall second = null, XmlDocument firstRequest = null, XmlDocument secondRequest = null)
        {
            firstRequest ??= MetadataRequest(first);
            secondRequest ??= MetadataRequest(second);
            bool hasFreeGame = TryGameMinutes(firstRequest, out double freeGame);
            bool hasStructuredGame = TryGameMinutes(secondRequest, out double structuredGame);
            return new AgentExperimentContextDivergence { path = path, reason = reason,
                hasFreeTextCall = first != null, hasStructuredFactsCall = second != null,
                freeTextCallOrdinal = first?.callOrdinal ?? -1, structuredFactsCallOrdinal = second?.callOrdinal ?? -1,
                freeTextTick = first?.dispatchTick ?? -1, structuredFactsTick = second?.dispatchTick ?? -1,
                hasFreeTextGameMinutes = hasFreeGame, hasStructuredFactsGameMinutes = hasStructuredGame,
                freeTextGameMinutes = freeGame, structuredFactsGameMinutes = structuredGame,
                freeTextRealSeconds = first?.dispatchRealSeconds ?? 0, structuredFactsRealSeconds = second?.dispatchRealSeconds ?? 0 };
        }

        private static XmlDocument MetadataRequest(DecisionTraceCall call)
        {
            if (call == null) return null;
            try { return ReadRequest(call.requestJson); }
            catch (Exception exception) when (DataError(exception)) { return null; }
        }

        private static bool TryGameMinutes(XmlDocument document, out double minutes)
            => double.TryParse(document?.SelectSingleNode("/root/gameTotalMinutes")?.InnerText,
                NumberStyles.Float, CultureInfo.InvariantCulture, out minutes) && !double.IsNaN(minutes) && !double.IsInfinity(minutes) && minutes >= 0;

        private static bool DataError(Exception exception) => exception is ArgumentException || exception is FormatException
            || exception is InvalidOperationException || exception is XmlException || exception is SerializationException;
    }
}
