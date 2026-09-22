using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Player;
using CozyTown.Unity.Time;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CozyTown.Unity.Experiments
{
    public sealed class AgentExperimentSession
    {
        public GameSaveSnapshot InitialSnapshot { get; private set; }
        public CozyTownTownLifeController Controller { get; private set; }
        public CozyTownServices Services { get; private set; }
        public string ScenarioId { get; private set; }
        public double RealSeconds { get; private set; }
        private PlayerModalInputGate2D _inputGate;
        private NpcWorldResident2D[] _residents;
        private readonly List<NpcDecisionOutcome> _outcomes = new List<NpcDecisionOutcome>();
        private int _tick;
        public IReadOnlyList<NpcDecisionOutcome> Outcomes => _outcomes.AsReadOnly();
        public DecisionTraceClient TraceClient { get; private set; }
        public bool Started => TraceClient != null;
        public string RunMode { get; private set; }
        public NpcSpeechMode SpeechMode { get; private set; }
        public string ExperimentId { get; } = Guid.NewGuid().ToString("N");
        public int Tick => _tick;
        public bool Completed { get; private set; }
        private string _sourceRevision = "unknown", _pairId, _evaluationPlanReference = "none";
        private string[] _armOrder = Array.Empty<string>();
        private readonly List<AgentExperimentInput> _inputs = new List<AgentExperimentInput>();
        private GameSaveSnapshot _finalSnapshot;
        private Func<double> _completionClock;
        private AgentExperimentPackage _replayPackage;
        private int _replayInput;
        private bool _executingReplayInput;
        private string _failure;
        private readonly Dictionary<string, string> _worldMapping = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Divergence => _failure ?? TraceClient?.Divergence;

        private DecisionTrace _measuredTrace;
        private bool _capturingMeasurements;
        public string MeasurementsJson { get; private set; } = "{}";

        public DecisionTrace CaptureTraceReport()
        {
            if (!Started) throw new InvalidOperationException("Start the experiment before reading its trace.");
            var trace = TraceClient.Trace;
            if (_measuredTrace == null) return trace;
            foreach (var measured in _measuredTrace.calls)
            {
                if (measured.callOrdinal >= trace.calls.Count) continue;
                var call = trace.calls[measured.callOrdinal];
                if (call.requestJson != measured.requestJson) continue;
                call.rawResponseJson = measured.rawResponseJson;
                call.requestedModel = measured.requestedModel;
                call.returnedModel = measured.returnedModel;
                call.usageJson = measured.usageJson;
            }
            return trace;
        }

        public async Task<string> RefreshMeasurementsAsync(string endpoint, HttpClient http = null, CancellationToken cancellationToken = default)
        {
            if (!Started || RunMode != "live") throw new InvalidOperationException("Proxy measurements require a live experiment.");
            if (_capturingMeasurements) throw new InvalidOperationException("A measurement refresh is already running.");
            _capturingMeasurements = true;
            try
            {
                var trace = TraceClient.Trace;
                string report = await ProxyExperimentTelemetry.CaptureAsync(endpoint, trace, http, cancellationToken);
                _measuredTrace = trace;
                MeasurementsJson = report;
                return report;
            }
            finally { _capturingMeasurements = false; }
        }

        public void Complete()
        {
            RequireRunning();
            SampleRealTime();
            if (Controller.ActiveDecisionRequests != 0)
                throw new InvalidOperationException("Wait for pending replies to be delivered before completing the experiment.");
            _finalSnapshot = Capture();
            TraceClient.Complete();
            if (_replayPackage != null)
            {
                string difference = ExperimentReplayComparison.Snapshots(_replayPackage.Final, _finalSnapshot)
                    ?? ExperimentReplayComparison.Results(_replayPackage.Manifest.results, Results());
                if (difference != null) throw Stop(difference);
            }
            _inputs.Add(Input("complete"));
            Completed = true;
        }

        public void Export(string directory)
        {
            if (!Started) throw new InvalidOperationException("Start the experiment before exporting.");
            if (!Completed) SampleRealTime();
            var manifest = new AgentExperimentManifest {
                experimentId = ExperimentId, pairId = _pairId, armOrder = _armOrder.ToArray(),
                scenarioId = ScenarioId, speechMode = SpeechMode == NpcSpeechMode.FreeText ? "F" : "S",
                evaluationPlanReference = _evaluationPlanReference,
                runMode = RunMode, configuration = TraceClient.Trace.configuration, sourceRevision = _sourceRevision,
                derivedFromExperimentId = _replayPackage?.Manifest.experimentId,
                completed = Completed, trace = CaptureTraceReport(), inputs = new List<AgentExperimentInput>(_inputs),
                results = Results(), measurementsJson = MeasurementsJson
            };
            var exported = AgentExperimentPackage.Export(directory, manifest, InitialSnapshot, _finalSnapshot ?? Capture(), _checkpoints);
            if (!exported.IsSuccess) throw new InvalidOperationException(exported.ErrorCode);
        }

        private List<AgentExperimentResult> Results()
        {
            var codec = new ProxyNpcDecisionJsonCodec();
            return _outcomes.Select(result => new AgentExperimentResult {
                    npcId = result.NpcId, worldRunId = result.WorldRunId.ToString("N"), decisionId = result.DecisionId.ToString("N"),
                    code = result.Code, calls = result.Calls, startedRealSeconds = result.StartedAtSeconds,
                    finishedRealSeconds = result.FinishedAtSeconds, requestJson = codec.SerializeRequest(result.Context),
                    replyJson = result.Reply == null ? null : DecisionTraceResponse.Serialize(result.Reply, result.Context),
                    executionObservationJson = result.ExecutionObservation == null ? null : codec.SerializeObservation(result.ExecutionObservation),
                    candidateErrors = result.CandidateErrorCodes.ToArray()
                }).ToList();
        }

        public void StartReplay(string directory)
        {
            if (Started) throw new InvalidOperationException("Create a fresh experiment before starting replay.");
            var loaded = AgentExperimentPackage.Read(directory);
            if (!loaded.IsSuccess) throw new ArgumentException(loaded.ErrorCode, nameof(directory));
            var package = loaded.Value;
            if (package.Manifest.scenarioId != ScenarioId) throw new ArgumentException("Replay requires the recorded initial resource scenario.");
            SpeechMode = package.Manifest.speechMode == "F" ? NpcSpeechMode.FreeText : NpcSpeechMode.StructuredFacts;
            var trace = DecisionTraceClient.Replay(package.Manifest.trace, Configuration());
            Controller.ConfigureDecisions(trace, AgentExperimentContent.CreateConfiguration(ScenarioId).Npcs,
                new NpcDecisionSettings(maxRequestsPerMinute: 16), AgentExperimentContent.CreateMeetingPlans(), SpeechMode);
            InitialSnapshot = Capture();
            string difference = ExperimentReplayComparison.Snapshots(package.Initial, InitialSnapshot);
            if (difference != null) throw Stop("Replay initial state differs: " + difference);
            TraceClient = trace;
            _replayPackage = package;
            RunMode = "replay";
            MeasurementsJson = package.Manifest.measurementsJson;
            _evaluationPlanReference = package.Manifest.evaluationPlanReference;
            _pairId = package.Manifest.pairId;
            _armOrder = package.Manifest.armOrder.ToArray();
        }

        public double NextReplayDelaySeconds
        {
            get
            {
                if (_replayPackage == null) throw new InvalidOperationException("Start a recorded experiment replay first.");
                if (Completed) return 0;
                if (_replayInput >= _replayPackage.Manifest.inputs.Count) throw Stop("Replay has no completion input.");
                var input = _replayPackage.Manifest.inputs[_replayInput];
                double delay = input.kind == "advance" ? input.elapsedGameSeconds : 0;
                if (double.IsNaN(delay) || double.IsInfinity(delay) || delay < 0)
                    throw Stop("Replay input duration must be finite and non-negative.");
                return delay;
            }
        }

        public bool ReplayNext()
        {
            if (_replayPackage == null) throw new InvalidOperationException("Start a recorded experiment replay first.");
            if (Completed) return false;
            if (_replayInput >= _replayPackage.Manifest.inputs.Count) throw Stop("Replay has no completion input.");
            var input = _replayPackage.Manifest.inputs[_replayInput++];
            _executingReplayInput = true;
            try
            {
                if (input.tick != _tick || double.IsNaN(input.realSeconds) || double.IsInfinity(input.realSeconds)
                    || input.realSeconds < RealSeconds) throw Stop("Replay input tick or time differs.");
                CheckWorld(input.worldBefore);
                RealSeconds = input.realSeconds;
                switch (input.kind)
                {
                    case "advance": Step(input.elapsedGameSeconds, input.realSeconds); break;
                    case "save": Save(input.label); break;
                    case "load": Load(input.label); break;
                    case "sleep": Sleep(input.sleepMinutes); break;
                    case "complete":
                        if (_replayInput != _replayPackage.Manifest.inputs.Count) throw Stop("Replay has inputs after completion.");
                        Complete(); break;
                    default: throw Stop("Replay input kind is unsupported.");
                }
                CheckWorld(input.worldAfter);
                return true;
            }
            catch (Exception exception)
            {
                _failure ??= exception.Message;
                throw;
            }
            finally { _executingReplayInput = false; }
        }

        private void CheckWorld(string recorded)
        {
            if (string.IsNullOrEmpty(recorded)) throw Stop("Replay input has no world identity.");
            if (_worldMapping.TryGetValue(recorded, out var actual))
            {
                if (actual != WorldId) throw Stop("Replay world transition differs.");
            }
            else
            {
                if (_worldMapping.Values.Contains(WorldId)) throw Stop("Replay did not create the recorded world generation.");
                _worldMapping.Add(recorded, WorldId);
            }
        }

        private InvalidOperationException Stop(string reason)
        {
            _failure = reason;
            return new InvalidOperationException(reason);
        }
        public void Sleep(int minutes)
        {
            RequireRunning();
            SampleRealTime();
            var input = Input("sleep");
            input.sleepMinutes = minutes;
            var result = Services.Sleep.SleepForMinutes(minutes);
            if (!result.IsSuccess) throw new ArgumentException(result.ErrorCode, nameof(minutes));
            input.worldAfter = WorldId;
            _inputs.Add(input);
        }
        private readonly Dictionary<string, GameSaveSnapshot> _checkpoints = new Dictionary<string, GameSaveSnapshot>(StringComparer.Ordinal);

        public void Save(string label = "checkpoint")
        {
            RequireRunning();
            SampleRealTime();
            if (string.IsNullOrEmpty(label) || label.Length > 64
                || label.Any(character => character > 127 || !(char.IsLetterOrDigit(character) || character == '_' || character == '-')))
                throw new ArgumentException("Use a checkpoint label containing 1 to 64 ASCII letters, digits, hyphens or underscores.", nameof(label));
            if (_checkpoints.ContainsKey(label)) throw new ArgumentException("Choose a new checkpoint label to preserve the earlier snapshot.", nameof(label));
            var snapshot = Capture();
            if (_replayPackage != null && (!_replayPackage.Checkpoints.TryGetValue(label, out var expected)
                || ExperimentReplayComparison.Snapshots(expected, snapshot) != null))
                throw Stop("Replay checkpoint differs from its recorded save input: " + label);
            _checkpoints.Add(label, snapshot);
            _inputs.Add(Input("save", label));
        }

        public void Load(string label = "checkpoint")
        {
            RequireRunning();
            SampleRealTime();
            if (!_checkpoints.TryGetValue(label, out var saved)) throw new ArgumentException("Save this checkpoint before loading it.", nameof(label));
            var input = Input("load", label);
            var stored = Services.SaveStorage.Save("main", saved);
            if (!stored.IsSuccess) throw new InvalidOperationException(stored.ErrorCode);
            var loaded = Services.GameSave.Load();
            if (!loaded.IsSuccess) throw new InvalidOperationException(loaded.ErrorCode);
            if (!_inputGate.TryAcquire(this)) throw new InvalidOperationException("The experiment could not reacquire player input after loading.");
            input.worldAfter = WorldId;
            _inputs.Add(input);
        }

        private string WorldId => Controller.GetAgentState(_residents[0].NpcId).WorldRunId.ToString("N");
        private void SampleRealTime()
        {
            if (_completionClock == null) return;
            double now = _completionClock();
            if (double.IsNaN(now) || double.IsInfinity(now) || now < RealSeconds)
                throw new InvalidOperationException("The experiment clock must remain finite and monotonic.");
            RealSeconds = now;
        }
        private AgentExperimentInput Input(string kind, string label = null) => new AgentExperimentInput {
            tick = _tick, kind = kind, label = label, realSeconds = RealSeconds, worldBefore = WorldId, worldAfter = WorldId };

        private void RequireRunning()
        {
            if (!Started || Completed || Divergence != null)
                throw new InvalidOperationException("An unfinished, running experiment is required.");
            if (RunMode == "replay" && !_executingReplayInput)
                throw new InvalidOperationException("Advance replay through its recorded inputs.");
        }

        private GameSaveSnapshot Capture()
        {
            var saved = Services.GameSave.Save();
            if (!saved.IsSuccess) throw new InvalidOperationException(saved.ErrorCode);
            var loaded = Services.SaveStorage.Load("main");
            if (!loaded.IsSuccess) throw new InvalidOperationException(loaded.ErrorCode);
            return loaded.Value;
        }

        public void ConfigureIdentity(string sourceRevision = "unknown", string pairId = null, string[] armOrder = null,
            string evaluationPlanReference = "none")
        {
            if (Started) throw new InvalidOperationException("Configure experiment identity before starting.");
            if (string.IsNullOrWhiteSpace(evaluationPlanReference) || evaluationPlanReference.Length > 4096)
                throw new ArgumentException("Use a registered evaluation plan reference of at most 4096 characters, or none.", nameof(evaluationPlanReference));
            _sourceRevision = string.IsNullOrWhiteSpace(sourceRevision) ? "unknown" : sourceRevision;
            _pairId = pairId;
            _armOrder = armOrder?.ToArray() ?? Array.Empty<string>();
            _evaluationPlanReference = evaluationPlanReference;
        }

        public void Start(NpcSpeechMode? speechMode, string runMode, INpcDecisionClient client, Func<double> completionClock = null)
        {
            if (Started || Divergence != null) throw new InvalidOperationException("Create a fresh experiment before choosing another mode.");
            if (!speechMode.HasValue || !Enum.IsDefined(typeof(NpcSpeechMode), speechMode.Value))
                throw new ArgumentException("Choose F or S explicitly before starting.", nameof(speechMode));
            if (runMode != "fixed" && runMode != "live") throw new ArgumentException("Choose a supported experiment client.", nameof(runMode));
            if (client == null) throw new ArgumentNullException(nameof(client));
            SpeechMode = speechMode.Value;
            RunMode = runMode;
            TraceClient = DecisionTraceClient.Record(client, Configuration(), completionClock);
            _completionClock = completionClock;
            try
            {
                Controller.ConfigureDecisions(TraceClient, AgentExperimentContent.CreateConfiguration(ScenarioId).Npcs,
                    new NpcDecisionSettings(maxRequestsPerMinute: 16), AgentExperimentContent.CreateMeetingPlans(), SpeechMode);
                InitialSnapshot = Capture();
            }
            catch (Exception exception) { throw Stop("Experiment initialization failed: " + exception.Message); }
        }

        private string Configuration() => InitialSnapshot.CompleteWorld.ContentConfiguration + "\n"
            + InitialSnapshot.CompleteWorld.BodyConfiguration + "\nexperiment-v1:" + ScenarioId + ":" + SpeechMode + ":16:2";

        public void Step(double elapsedGameSeconds, double realSeconds)
        {
            RequireRunning();
            if (double.IsNaN(realSeconds) || double.IsInfinity(realSeconds) || realSeconds < RealSeconds)
                throw new ArgumentOutOfRangeException(nameof(realSeconds));
            if (double.IsNaN(elapsedGameSeconds) || double.IsInfinity(elapsedGameSeconds) || elapsedGameSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedGameSeconds));
            RealSeconds = realSeconds;
            var input = Input("advance");
            input.elapsedGameSeconds = elapsedGameSeconds;
            var advanced = Services.DaytimeClock.AdvanceElapsed(elapsedGameSeconds);
            if (!advanced.IsSuccess) throw new InvalidOperationException(advanced.ErrorCode);
            _inputs.Add(input);
            TraceClient.Pump(_tick++, realSeconds, Controller.GameTotalMinutes);
            _outcomes.AddRange(Controller.TickDecisions(realSeconds));
            if (TraceClient.IsStopped) throw new InvalidOperationException(TraceClient.Divergence);
        }

        public static AgentExperimentSession Attach(Scene scene, CozyTownServices services, string scenarioId)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("Load the experiment scene first.", nameof(scene));
            if (services == null) throw new ArgumentNullException(nameof(services));
            var components = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
            var controller = components.OfType<CozyTownTownLifeController>().Single();
            var driver = components.OfType<DaytimeClockDriver>().Single();
            var player = components.OfType<PlayerMovement2D>().Single();
            if (controller.DecisionsEnabled) throw new InvalidOperationException("Use a fresh scene with autonomous decisions disabled.");
            var session = new AgentExperimentSession {
                Services = services, Controller = controller, ScenarioId = scenarioId,
                _inputGate = player.GetComponent<PlayerModalInputGate2D>(),
                _residents = components.OfType<NpcWorldResident2D>().OrderBy(actor => actor.NpcId, StringComparer.Ordinal).ToArray()
            };
            if (session._residents.Length != 4 || session._residents.Select(actor => actor.NpcId).Distinct().Count() != 4)
                throw new InvalidOperationException("An experiment requires all four distinct residents.");
            controller.enabled = false;
            driver.enabled = false;
            driver.Bind(services.DaytimeClock);
            controller.Bind(services.WorldTimeFlow, services.ResourceTrading);
            controller.ConfigureSnapshots(services.WorldSnapshots, player, session._inputGate, driver, () => session.RealSeconds);
            services.WorldSnapshots.Require();
            if (!session._inputGate.TryAcquire(session)) throw new InvalidOperationException("Close other modal interfaces before starting an experiment.");
            // Physics contacts must not move the player between recorded experiment inputs.
            var playerBody = player.GetComponent<Rigidbody2D>();
            playerBody.constraints = RigidbodyConstraints2D.FreezeAll;
            playerBody.linearVelocity = Vector2.zero;
            playerBody.angularVelocity = 0;
            var saved = services.GameSave.Save();
            if (!saved.IsSuccess) throw new InvalidOperationException(saved.ErrorCode);
            session.InitialSnapshot = services.SaveStorage.Load("main").Value;
            return session;
        }
    }
}
