using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.Save;
using UnityEngine;

namespace CozyTown.Unity.Experiments
{
    [Serializable]
    public sealed class AgentExperimentManifest
    {
        public int schemaVersion = 1;
        public string experimentId, pairId, scenarioId, speechMode, runMode, configuration, sourceRevision;
        public string derivedFromExperimentId;
        public string expressionContractVersion = "1.0";
        public string evaluationPlanReference = "none";
        public string[] armOrder = Array.Empty<string>();
        public bool completed, hasFinalSnapshot;
        public DecisionTrace trace;
        public List<AgentExperimentInput> inputs = new List<AgentExperimentInput>();
        public List<AgentExperimentResult> results = new List<AgentExperimentResult>();
        public string measurementsJson = "{}";
        public List<string> checkpointLabels = new List<string>();
    }

    [Serializable]
    public sealed class AgentExperimentInput
    {
        public int tick, sleepMinutes;
        public string kind, label, worldBefore, worldAfter;
        public double elapsedGameSeconds, realSeconds;
    }

    [Serializable]
    public sealed class AgentExperimentResult
    {
        public string npcId, worldRunId, decisionId, code;
        public int calls;
        public double startedRealSeconds, finishedRealSeconds;
        public string requestJson, replyJson, executionObservationJson;
        public string[] candidateErrors = Array.Empty<string>();
    }

    public sealed class AgentExperimentPackage
    {
        public AgentExperimentManifest Manifest { get; private set; }
        public GameSaveSnapshot Initial { get; private set; }
        public GameSaveSnapshot Final { get; private set; }
        public IReadOnlyDictionary<string, GameSaveSnapshot> Checkpoints { get; private set; }

        public static OperationResult Export(string directory, AgentExperimentManifest manifest,
            GameSaveSnapshot initial, GameSaveSnapshot final,
            IReadOnlyDictionary<string, GameSaveSnapshot> checkpoints = null)
        {
            if (string.IsNullOrWhiteSpace(directory) || manifest == null || initial == null)
                return OperationResult.Failure("experiment.package_invalid");
            checkpoints ??= new Dictionary<string, GameSaveSnapshot>();
            if (checkpoints.Any(item => !SafeLabel(item.Key) || item.Value == null))
                return OperationResult.Failure("experiment.checkpoint_invalid");
            try
            {
                if (Directory.Exists(directory) || File.Exists(directory))
                    return OperationResult.Failure("experiment.directory_exists");
                string invalid = InvalidMetadata(manifest);
                if (invalid != null) return OperationResult.Failure(invalid);
                if (!RecordsAreValid(manifest)) return OperationResult.Failure("experiment.manifest_invalid");
                if (manifest.completed && (final == null || !manifest.trace.completed))
                    return OperationResult.Failure("experiment.incomplete");
                var savedManifest = JsonUtility.FromJson<AgentExperimentManifest>(JsonUtility.ToJson(manifest));
                savedManifest.hasFinalSnapshot = final != null;
                savedManifest.checkpointLabels = checkpoints.Keys.OrderBy(label => label, StringComparer.Ordinal).ToList();
                var frames = new Dictionary<string, GameSaveSnapshot> { { "initial", initial } };
                if (final != null) frames.Add("final", final);
                foreach (var checkpoint in checkpoints) frames.Add("checkpoint-" + checkpoint.Key, checkpoint.Value);
                var validation = new InMemorySaveStorage();
                foreach (var frame in frames.Values)
                {
                    if (frame.SchemaVersion != GameSaveSnapshot.CurrentSchemaVersion)
                        return OperationResult.Failure("experiment.snapshot_schema_unsupported");
                    var validated = validation.Save(JsonFileSaveStorage.MainSlotId, frame);
                    if (!validated.IsSuccess) return validated;
                }
                if (!ConfigurationMatches(savedManifest, initial, final, checkpoints.Values))
                    return OperationResult.Failure("experiment.configuration_mismatch");
                foreach (var frame in frames)
                {
                    var saved = new JsonFileSaveStorage(Path.Combine(directory, frame.Key + ".json"))
                        .Save(JsonFileSaveStorage.MainSlotId, frame.Value);
                    if (!saved.IsSuccess) return saved;
                }
                File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonUtility.ToJson(savedManifest, true));
                return OperationResult.Success();
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                return OperationResult.Failure("experiment.write_failed");
            }
        }

        public static OperationResult<AgentExperimentPackage> Read(string directory, bool forReplay = true)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return OperationResult<AgentExperimentPackage>.Failure("experiment.package_invalid");
            try
            {
                string json = File.ReadAllText(Path.Combine(directory, "manifest.json"));
                var document = ReadJson(json);
                if (!VersionIsSupported(document, "/root/schemaVersion") ||
                    !VersionIsSupported(document, "/root/trace/schemaVersion"))
                    return OperationResult<AgentExperimentPackage>.Failure("experiment.schema_unsupported");
                if (!HasType(document, "/root/expressionContractVersion", "string") ||
                    !HasType(document, "/root/evaluationPlanReference", "string") ||
                    !HasType(document, "/root/trace", "object") ||
                    new[] { "/root/trace/ticks", "/root/trace/calls", "/root/inputs", "/root/results" }
                        .Any(path => !HasType(document, path, "array") ||
                            document.SelectNodes(path + "/item").Cast<XmlElement>().Any(item => item.GetAttribute("type") != "object")))
                    return OperationResult<AgentExperimentPackage>.Failure("experiment.manifest_invalid");
                var manifest = JsonUtility.FromJson<AgentExperimentManifest>(json);
                string invalid = InvalidMetadata(manifest);
                if (invalid != null) return OperationResult<AgentExperimentPackage>.Failure(invalid);
                if (!RecordsAreValid(manifest)) return OperationResult<AgentExperimentPackage>.Failure("experiment.manifest_invalid");
                if (manifest == null || manifest.checkpointLabels == null ||
                    manifest.checkpointLabels.Any(label => !SafeLabel(label)) ||
                    manifest.checkpointLabels.Distinct(StringComparer.Ordinal).Count() != manifest.checkpointLabels.Count)
                    return OperationResult<AgentExperimentPackage>.Failure("experiment.checkpoint_invalid");
                if (forReplay && (!manifest.completed || !manifest.hasFinalSnapshot || manifest.trace == null || !manifest.trace.completed))
                    return OperationResult<AgentExperimentPackage>.Failure("experiment.incomplete");
                var initial = ReadFrame(directory, "initial");
                if (!initial.IsSuccess) return OperationResult<AgentExperimentPackage>.Failure(initial.ErrorCode);
                GameSaveSnapshot final = null;
                if (manifest.hasFinalSnapshot)
                {
                    var loaded = ReadFrame(directory, "final");
                    if (!loaded.IsSuccess) return OperationResult<AgentExperimentPackage>.Failure(loaded.ErrorCode);
                    final = loaded.Value;
                }
                var checkpoints = new Dictionary<string, GameSaveSnapshot>(StringComparer.Ordinal);
                foreach (var label in manifest.checkpointLabels)
                {
                    var loaded = ReadFrame(directory, "checkpoint-" + label);
                    if (!loaded.IsSuccess) return OperationResult<AgentExperimentPackage>.Failure(loaded.ErrorCode);
                    checkpoints.Add(label, loaded.Value);
                }
                if (!ConfigurationMatches(manifest, initial.Value, final, checkpoints.Values))
                    return OperationResult<AgentExperimentPackage>.Failure("experiment.configuration_mismatch");
                return OperationResult<AgentExperimentPackage>.Success(new AgentExperimentPackage {
                    Manifest = manifest, Initial = initial.Value, Final = final,
                    Checkpoints = new ReadOnlyDictionary<string, GameSaveSnapshot>(checkpoints) });
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                return OperationResult<AgentExperimentPackage>.Failure("experiment.read_failed");
            }
        }

        private static OperationResult<GameSaveSnapshot> ReadFrame(string directory, string name)
        {
            var loaded = new JsonFileSaveStorage(Path.Combine(directory, name + ".json")).Load(JsonFileSaveStorage.MainSlotId);
            return loaded.IsSuccess && loaded.Value.SchemaVersion != GameSaveSnapshot.CurrentSchemaVersion
                ? OperationResult<GameSaveSnapshot>.Failure("experiment.snapshot_schema_unsupported") : loaded;
        }

        private static string InvalidMetadata(AgentExperimentManifest manifest)
        {
            if (manifest == null || manifest.trace == null) return "experiment.manifest_invalid";
            if (manifest.schemaVersion != 1 || manifest.trace.schemaVersion != 1) return "experiment.schema_unsupported";
            if (manifest.expressionContractVersion != "1.0" || string.IsNullOrWhiteSpace(manifest.evaluationPlanReference) ||
                manifest.evaluationPlanReference.Length > 4096 ||
                string.IsNullOrWhiteSpace(manifest.experimentId) || string.IsNullOrWhiteSpace(manifest.sourceRevision) ||
                string.IsNullOrWhiteSpace(manifest.configuration) || string.IsNullOrWhiteSpace(manifest.trace.clientConfiguration) ||
                (manifest.scenarioId != "available" && manifest.scenarioId != "seller_empty" &&
                    manifest.scenarioId != "buyer_poor" && manifest.scenarioId != "need_satisfied") ||
                (manifest.speechMode != "F" && manifest.speechMode != "S") ||
                (manifest.runMode != "fixed" && manifest.runMode != "live" && manifest.runMode != "replay") ||
                (manifest.runMode == "replay" && string.IsNullOrWhiteSpace(manifest.derivedFromExperimentId)))
                return "experiment.manifest_invalid";
            return null;
        }

        private static bool ConfigurationMatches(AgentExperimentManifest manifest, GameSaveSnapshot initial,
            GameSaveSnapshot final, IEnumerable<GameSaveSnapshot> checkpoints)
        {
            if (initial?.CompleteWorld?.Decisions == null || manifest.trace == null ||
                manifest.configuration != manifest.trace.configuration ||
                initial.CompleteWorld.Decisions.ClientConfiguration != manifest.trace.clientConfiguration ||
                (manifest.speechMode != "F" && manifest.speechMode != "S") ||
                initial.CompleteWorld.Decisions.SpeechMode != (manifest.speechMode == "F" ? NpcSpeechMode.FreeText : NpcSpeechMode.StructuredFacts))
                return false;
            string expected = ConfigurationSignature(initial);
            return (final == null || ConfigurationSignature(final) == expected) &&
                checkpoints.All(snapshot => ConfigurationSignature(snapshot) == expected);
        }

        private static bool RecordsAreValid(AgentExperimentManifest manifest)
            => manifest.inputs != null && manifest.results != null && manifest.trace.ticks != null && manifest.trace.calls != null &&
                manifest.inputs.All(input => input != null && (input.kind == "advance" || input.kind == "save" ||
                    input.kind == "load" || input.kind == "sleep" || input.kind == "complete")) && manifest.results.All(result => result != null) &&
                manifest.trace.ticks.All(tick => tick != null) && manifest.trace.calls.All(call => call != null);

        private static string ConfigurationSignature(GameSaveSnapshot snapshot)
        {
            var document = ReadJson(JsonFileSaveStorage.SerializeSnapshot(snapshot));
            var signature = new StringBuilder();
            foreach (string path in new[] {
                "/root/worldSeed", "/root/completeWorld/contentConfiguration", "/root/completeWorld/bodyConfiguration",
                "/root/completeWorld/world/schedules", "/root/completeWorld/meetingsEnabled", "/root/completeWorld/meetings/plans",
                "/root/completeWorld/decisionsEnabled", "/root/completeWorld/decisions/settings",
                "/root/completeWorld/decisions/speechMode", "/root/completeWorld/decisions/clientConfiguration" })
                signature.Append(document.SelectSingleNode(path)?.OuterXml).Append('\n');
            foreach (XmlNode resident in document.SelectNodes("/root/completeWorld/decisions/residents/item"))
                foreach (string field in new[] { "npcId", "displayName", "persona", "fallbackDialogue" })
                    signature.Append(resident.SelectSingleNode(field)?.OuterXml).Append('\n');
            return signature.ToString();
        }

        private static bool SafeLabel(string value)
            => value != null && Regex.IsMatch(value, "\\A[A-Za-z0-9_-]{1,64}\\z");

        private static XmlDocument ReadJson(string json)
        {
            using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json), XmlDictionaryReaderQuotas.Max);
            var document = new XmlDocument();
            document.Load(reader);
            return document;
        }

        private static bool HasType(XmlDocument document, string path, string type)
        {
            var nodes = document.SelectNodes(path);
            return nodes.Count == 1 && nodes[0] is XmlElement element && element.GetAttribute("type") == type;
        }

        private static bool VersionIsSupported(XmlDocument document, string path)
            => HasType(document, path, "number") && document.SelectSingleNode(path).InnerText == "1";

        private static bool IsFileFailure(Exception exception)
            => exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException ||
                exception is NotSupportedException || exception is SerializationException || exception is XmlException;
    }
}
