using System;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Unity.Core;
using CozyTown.Unity.Npc;
using UnityEngine;

namespace CozyTown.Unity.Experiments
{
    public enum AgentExperimentArm { Unselected, F, S, Paired }
    public enum AgentExperimentRunMode { Unselected, Fixed, Live, Replay }

    [Serializable]
    public sealed class AgentExperimentLaunchOptions
    {
        public AgentExperimentArm arm;
        public AgentExperimentRunMode runMode;
        public string scenarioId = "available";
        public string proxyEndpoint = "http://127.0.0.1:8765/decide";
        public string clientConfigurationJson = string.Empty;
        public string replayDirectory = string.Empty;
        public string sourceRevision = "unknown";
        public string pairId = string.Empty;
        public string evaluationPlanReference = "none";

        public void Validate()
        {
            if (runMode == AgentExperimentRunMode.Unselected || !Enum.IsDefined(typeof(AgentExperimentRunMode), runMode))
                throw new ArgumentException("Choose fixed, live or replay before starting.");
            if (runMode == AgentExperimentRunMode.Replay)
            {
                ReadReplay();
                return;
            }
            if (arm == AgentExperimentArm.Unselected || !Enum.IsDefined(typeof(AgentExperimentArm), arm))
                throw new ArgumentException("Choose F, S or a paired F/S experiment before starting.");
            if (string.IsNullOrWhiteSpace(evaluationPlanReference) || evaluationPlanReference.Length > 4096)
                throw new ArgumentException("Declare an evaluation plan reference of at most 4096 characters, or use none.");
            AgentExperimentContent.CreateConfiguration(scenarioId);
            if (runMode == AgentExperimentRunMode.Live)
            {
                if (string.IsNullOrWhiteSpace(clientConfigurationJson))
                    throw new ArgumentException("Declare the model and non-secret generation settings before starting live.");
                if (!Uri.TryCreate(proxyEndpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback
                    || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new ArgumentException("Use a loopback HTTP proxy URL without credentials.");
                _ = new ProxyNpcDecisionClient(proxyEndpoint, snapshotConfiguration: clientConfigurationJson);
            }
        }

        public NpcSpeechMode[] CreateArms()
        {
            Validate();
            if (runMode == AgentExperimentRunMode.Replay)
                return new[] { ReadReplay().Manifest.speechMode == "F" ? NpcSpeechMode.FreeText : NpcSpeechMode.StructuredFacts };
            return arm == AgentExperimentArm.Paired
                ? new[] { NpcSpeechMode.FreeText, NpcSpeechMode.StructuredFacts }
                : new[] { arm == AgentExperimentArm.F ? NpcSpeechMode.FreeText : NpcSpeechMode.StructuredFacts };
        }

        public AgentExperimentLaunchOptions Freeze()
        {
            Validate();
            var frozen = JsonUtility.FromJson<AgentExperimentLaunchOptions>(JsonUtility.ToJson(this));
            if (runMode == AgentExperimentRunMode.Live)
                frozen.clientConfigurationJson = new ProxyNpcDecisionClient(proxyEndpoint,
                    snapshotConfiguration: clientConfigurationJson).SnapshotConfiguration;
            if (runMode == AgentExperimentRunMode.Replay)
            {
                var manifest = ReadReplay().Manifest;
                frozen.scenarioId = manifest.scenarioId;
                frozen.arm = manifest.speechMode == "F" ? AgentExperimentArm.F : AgentExperimentArm.S;
                frozen.evaluationPlanReference = manifest.evaluationPlanReference;
            }
            return frozen;
        }

        private AgentExperimentPackage ReadReplay()
        {
            if (string.IsNullOrWhiteSpace(replayDirectory)) throw new ArgumentException("Choose a completed experiment package to replay.");
            var package = AgentExperimentPackage.Read(replayDirectory);
            if (!package.IsSuccess) throw new ArgumentException(package.ErrorCode);
            return package.Value;
        }
    }

    [DefaultExecutionOrder(-2000)]
    [DisallowMultipleComponent]
    public sealed class AgentExperimentLauncher : MonoBehaviour, ICozyTownServicesFactory
    {
        [SerializeField] private AgentExperimentLaunchOptions _options;
        [SerializeField] private int _armIndex;
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
        private double _lastUpdate, _accumulator;
        public const double GameStepSeconds = 0.5;
        public CozyTownServices Services { get; private set; }
        public AgentExperimentSession Session { get; private set; }
        public bool IsRunning { get; private set; }
        public string Error { get; private set; }
        public bool IsReplay => _options?.runMode == AgentExperimentRunMode.Replay;
        public string ProxyEndpoint => _options?.runMode == AgentExperimentRunMode.Live ? _options.proxyEndpoint : null;
        public double RealSeconds => _clock.Elapsed.TotalSeconds;
        public void Configure(AgentExperimentLaunchOptions options, int armIndex = 0)
        {
            if (Services != null) throw new InvalidOperationException("Create a new launcher to change an experiment.");
            if (options == null) throw new ArgumentNullException(nameof(options));
            var frozen = options.Freeze();
            if (armIndex < 0 || armIndex >= frozen.CreateArms().Length) throw new ArgumentOutOfRangeException(nameof(armIndex));
            _options = frozen;
            _armIndex = armIndex;
        }

        public CozyTownServices Create()
        {
            if (Services != null) return Services;
            if (_options == null) throw new InvalidOperationException("Configure the experiment before creating its world.");
            _options.Validate();
            Services = CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration(_options.scenarioId));
            Services.WorldSnapshots.Require();
            return Services;
        }

        private void Awake()
        {
            var bootstrap = GetComponent<CozyTownBootstrap>();
            if (bootstrap == null)
            {
                Error = "Place the experiment launcher on the Bootstrap object.";
                enabled = false;
                return;
            }
            try { bootstrap.SetFactory(this); }
            catch (Exception exception) { Fail(exception); }
        }

        private void Start()
        {
            if (Error != null) return;
            try
            {
                if (Services == null || !GetComponent<CozyTownBootstrap>().IsInitialized)
                    throw new InvalidOperationException("The experiment world did not initialize.");
                Session = AgentExperimentSession.Attach(gameObject.scene, Services, _options.scenarioId);
                Session.ConfigureIdentity(_options.sourceRevision, _options.pairId,
                    _options.arm == AgentExperimentArm.Paired ? new[] { "F", "S" } : Array.Empty<string>(),
                    evaluationPlanReference: _options.evaluationPlanReference);
                _clock.Start();
                if (IsReplay) Session.StartReplay(_options.replayDirectory);
                else
                {
                    INpcDecisionClient client = _options.runMode == AgentExperimentRunMode.Fixed
                        ? new FixedExperimentDecisionClient()
                        : new ProxyNpcDecisionClient(_options.proxyEndpoint, snapshotConfiguration: _options.clientConfigurationJson);
                    Session.Start(_options.CreateArms()[_armIndex],
                        _options.runMode == AgentExperimentRunMode.Fixed ? "fixed" : "live", client,
                        () => _clock.Elapsed.TotalSeconds);
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        public void SetRunning(bool running)
        {
            if (running) RequireReady();
            IsRunning = running;
            _lastUpdate = RealSeconds;
            _accumulator = 0;
        }

        public void AdvanceOneMinute()
        {
            RequireReady();
            SetRunning(false);
            Advance();
        }

        public void PumpResponses()
        {
            RequireReady();
            if (IsReplay) throw new InvalidOperationException("Use the recorded inputs when replaying.");
            SetRunning(false);
            Session.Step(0, RealSeconds);
        }

        private void Update()
        {
            if (!IsRunning) return;
            try
            {
                double now = RealSeconds;
                _accumulator += now - _lastUpdate;
                _lastUpdate = now;
                int steps = 0;
                while (_accumulator >= GameStepSeconds && IsRunning && steps++ < 8)
                {
                    _accumulator -= GameStepSeconds;
                    Advance();
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void Advance()
        {
            RequireReady();
            if (IsReplay)
            {
                if (!Session.ReplayNext() || Session.Completed) SetRunning(false);
            }
            else Session.Step(GameStepSeconds, RealSeconds);
        }

        private void RequireReady()
        {
            string failure = Error ?? Session?.Divergence;
            if (failure != null || Session == null || !Session.Started || Session.Completed)
            {
                IsRunning = false;
                throw new InvalidOperationException(failure ?? "An initialized, unfinished experiment is required.");
            }
        }

        private void Fail(Exception exception)
        {
            IsRunning = false;
            Error = exception.Message;
        }

        private void OnDestroy() => _clock.Stop();
    }
}
