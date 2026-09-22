using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Unity.Core;
using CozyTown.Unity.Experiments;
using CozyTown.Unity.Npc;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CozyTown.Unity.Editor
{
    public sealed class AgentExperimentWindow : EditorWindow
    {
        private const string DevelopmentScene = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private static readonly string[] Scenarios = { "available", "seller_empty", "buyer_poor", "need_satisfied" };
        private static readonly string[] ScenarioLabels = { "资源可交易", "卖方无鱼", "买方金币不足", "买方已有鱼" };
        private static readonly string[] ResidentIds = { DefaultMvpIds.Npcs.Shopkeeper, DefaultMvpIds.Npcs.Farmer,
            DefaultMvpIds.Npcs.Fisher, DefaultMvpIds.Npcs.Cook };
        private static readonly string[] ResidentNames = { "Mina", "Eli", "Ren", "Sora" };
        [SerializeField] private AgentExperimentLaunchOptions _options = new AgentExperimentLaunchOptions();
        [SerializeField] private AgentExperimentLaunchOptions _frozenOptions;
        [SerializeField] private int _armIndex, _selectedResident;
        [SerializeField] private bool _completedExported, _nextArmRequested;
        [SerializeField] private string _checkpoint = "checkpoint", _exportParent = "Logs/agent-platform/exports", _lastExport;
        [SerializeField] private int _sleepMinutes = 60;
        private Vector2 _scroll;
        private string _message, _measurementStatus;
        private bool _showRequest, _showFacts = true, _showAudit, _measurementBusy, _exporting, _measurementFailed;
        internal Rect LastRepaintScreenBounds { get; private set; }
        internal Rect LastRepaintWindowPosition { get; private set; }
        internal double LastRepaintTime { get; private set; }
        internal float LastRepaintPixelScale { get; private set; }

        [MenuItem("CozyTown/Agent Experiment")]
        public static void Open() => GetWindow<AgentExperimentWindow>("四人实验").Show();

        private void OnEnable()
        {
            minSize = new Vector2(580, 620);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        private void OnInspectorUpdate() => Repaint();

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !_nextArmRequested) return;
            _nextArmRequested = false;
            EditorApplication.delayCall += () => { if (this != null) Try(() => Launch(_frozenOptions, 1)); };
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint)
            {
                LastRepaintPixelScale = EditorGUIUtility.pixelsPerPoint;
                LastRepaintWindowPosition = position;
                LastRepaintScreenBounds = GUIUtility.GUIToScreenRect(new Rect(Vector2.zero, position.size));
                LastRepaintTime = EditorApplication.timeSinceStartup;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("四人表达实验", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("F：自由文本；S：结构化事实。四人共享每分钟 16 次请求、并发 2。每次启动创建独立世界。", MessageType.Info);
            if (!EditorApplication.isPlaying)
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) DrawSetup();
            }
            else
            {
                var launchers = UnityEngine.Object.FindObjectsByType<AgentExperimentLauncher>(FindObjectsSortMode.None);
                if (launchers.Length == 1) DrawRun(launchers[0]);
                else EditorGUILayout.HelpBox("停止 Play 后，从此窗口启动四人实验。", MessageType.Info);
            }
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
            if (!string.IsNullOrEmpty(_lastExport)) EditorGUILayout.SelectableLabel("已导出：" + _lastExport, EditorStyles.wordWrappedLabel, GUILayout.Height(40));
            EditorGUILayout.EndScrollView();
        }

        private void DrawSetup()
        {
            _options.runMode = (AgentExperimentRunMode)EditorGUILayout.EnumPopup("运行模式（必选）", _options.runMode);
            if (_options.runMode == AgentExperimentRunMode.Replay)
            {
                _options.replayDirectory = EditorGUILayout.TextField("已完成实验包", _options.replayDirectory);
                if (GUILayout.Button("选择回放目录"))
                {
                    string directory = EditorUtility.OpenFolderPanel("选择包含 manifest.json 的实验包", _options.replayDirectory, string.Empty);
                    if (!string.IsNullOrEmpty(directory)) _options.replayDirectory = directory;
                }
                EditorGUILayout.HelpBox("回放从包中读取 F/S 与资源场景，逐项复现已记录输入；发现分歧即停止。", MessageType.Info);
            }
            else
            {
                _options.arm = (AgentExperimentArm)EditorGUILayout.EnumPopup("表达组（必选）", _options.arm);
                int scenario = Mathf.Max(0, Array.IndexOf(Scenarios, _options.scenarioId));
                _options.scenarioId = Scenarios[EditorGUILayout.Popup("资源初态", scenario, ScenarioLabels)];
                _options.sourceRevision = EditorGUILayout.TextField("构建版本（含 dirty 状态）", _options.sourceRevision);
                _options.evaluationPlanReference = EditorGUILayout.TextField("预登记计划引用（无则 none）", _options.evaluationPlanReference);
                if (_options.runMode == AgentExperimentRunMode.Live)
                {
                    _options.proxyEndpoint = EditorGUILayout.TextField("本机代理 URL", _options.proxyEndpoint);
                    EditorGUILayout.LabelField("模型生成配置 JSON（不要填写凭据）");
                    _options.clientConfigurationJson = EditorGUILayout.TextArea(_options.clientConfigurationJson, GUILayout.MinHeight(76));
                    EditorGUILayout.HelpBox("Live 会调用所选本机代理。请先在代理配置总调用上限；暂停会停止新 Tick，已发送请求仍可完成。", MessageType.Info);
                }
            }
            if (GUILayout.Button("创建实验并进入 Play（初始暂停）", GUILayout.Height(30))) Try(() => StartExperiment(_options));
        }

        public void StartExperiment(AgentExperimentLaunchOptions options)
        {
            if (_measurementBusy) throw new InvalidOperationException("Wait for the current measurement or export operation before starting another experiment.");
            if (options == null) throw new ArgumentNullException(nameof(options));
            _frozenOptions = options.Freeze();
            if (_frozenOptions.arm == AgentExperimentArm.Paired) _frozenOptions.pairId = Guid.NewGuid().ToString("N");
            Launch(_frozenOptions, 0);
        }

        private void Launch(AgentExperimentLaunchOptions options, int armIndex)
        {
            options.Validate();
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before creating another experiment.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AgentExperimentSceneRestore.Remember();
            try
            {
                var scene = EditorSceneManager.OpenScene(DevelopmentScene, OpenSceneMode.Single);
                Install(scene, options, armIndex);
                _armIndex = armIndex;
                _completedExported = false;
                _message = null;
                _measurementStatus = null;
                _measurementFailed = false;
                EditorApplication.isPlaying = true;
            }
            catch
            {
                AgentExperimentSceneRestore.Restore();
                throw;
            }
        }

        public static AgentExperimentLauncher Install(Scene scene, AgentExperimentLaunchOptions options, int armIndex = 0)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var frozen = options.Freeze();
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("Load the development scene before installing an experiment.", nameof(scene));
            if (armIndex < 0 || armIndex >= frozen.CreateArms().Length) throw new ArgumentOutOfRangeException(nameof(armIndex));
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Install the experiment before entering Play.");
            var bootstraps = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CozyTownBootstrap>(true)).ToArray();
            if (bootstraps.Length != 1) throw new InvalidOperationException("The experiment scene must contain exactly one Bootstrap.");
            var bootstrap = bootstraps[0];
            if (bootstrap.GetComponent<AgentExperimentLauncher>() != null)
                throw new InvalidOperationException("An experiment launcher is already installed in this scene.");
            var launcher = bootstrap.gameObject.AddComponent<AgentExperimentLauncher>();
            launcher.Configure(frozen, armIndex);
            return launcher;
        }

        private void DrawRun(AgentExperimentLauncher launcher)
        {
            if (launcher.Error != null) EditorGUILayout.HelpBox(launcher.Error, MessageType.Error);
            var session = launcher.Session;
            if (session == null || !session.Started)
            {
                EditorGUILayout.LabelField("正在装配实验世界…");
                return;
            }
            if (session.Divergence != null && session.Divergence != launcher.Error)
                EditorGUILayout.HelpBox("实验已停止：" + session.Divergence, MessageType.Error);
            var controller = session.Controller;
            var time = session.Services.Time.Current;
            EditorGUILayout.LabelField($"{session.RunMode} · {session.SpeechMode} · {session.ScenarioId} · Tick {session.Tick}");
            EditorGUILayout.SelectableLabel(session.ExperimentId, GUILayout.Height(18));
            EditorGUILayout.LabelField($"第 {time.Day} 天 {time.MinuteOfDay / 60:00}:{time.MinuteOfDay % 60:00} · 实时 {launcher.RealSeconds:F1}s · {(session.Completed ? "已完成" : launcher.IsRunning ? "运行中" : "暂停")}");
            EditorGUILayout.LabelField($"请求 {controller.DecisionRequestsStarted} · 近一分钟 {controller.DecisionRequestsInLastMinute}/16 · 处理中 {controller.ActiveDecisionRequests}/2 · 等待 {controller.WaitingDecisionResidents}");
            using (new EditorGUI.DisabledScope(session.Completed || launcher.Error != null || session.Divergence != null || _exporting))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(launcher.IsRunning ? "暂停" : "运行")) Try(() => launcher.SetRunning(!launcher.IsRunning));
                if (GUILayout.Button(launcher.IsReplay ? "下一条记录" : "推进 1 游戏分钟")) Try(launcher.AdvanceOneMinute);
                if (!launcher.IsReplay && GUILayout.Button("处理返回（不推进游戏时间）")) Try(launcher.PumpResponses);
                EditorGUILayout.EndHorizontal();
                using (new EditorGUI.DisabledScope(launcher.IsReplay))
                {
                    _checkpoint = EditorGUILayout.TextField("检查点标签", _checkpoint);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("保存检查点")) Command(launcher, () => session.Save(_checkpoint));
                    if (GUILayout.Button("读取检查点")) Command(launcher, () => session.Load(_checkpoint));
                    EditorGUILayout.EndHorizontal();
                    _sleepMinutes = EditorGUILayout.IntField("睡眠游戏分钟", _sleepMinutes);
                    if (GUILayout.Button("睡眠 / 跨日")) Command(launcher, () => session.Sleep(_sleepMinutes));
                    if (GUILayout.Button("完成实验")) Command(launcher, session.Complete);
                }
            }
            if (session.RunMode == "live")
            {
                using (new EditorGUI.DisabledScope(_measurementBusy))
                    if (GUILayout.Button("刷新代理指标（不调用模型）")) MeasureOrExport(launcher, false);
                bool hasMeasurements = !string.IsNullOrWhiteSpace(session.MeasurementsJson) && session.MeasurementsJson != "{}";
                EditorGUILayout.HelpBox(_measurementStatus ?? (hasMeasurements
                        ? "已有代理指标报告；刷新时间未确定，可重新刷新。未报告的字段保持未知。"
                        : "尚未刷新代理指标；未取得的型号、用量和费用保持未知。"),
                    _measurementFailed ? MessageType.Warning : MessageType.Info);
            }
            using (new EditorGUI.DisabledScope(_measurementBusy))
            {
                _exportParent = EditorGUILayout.TextField("导出父目录", _exportParent);
                if (GUILayout.Button(session.Completed ? "导出已完成实验包" : "暂停并导出当前审计记录（未完成）")) MeasureOrExport(launcher, true);
            }
            if (_measurementBusy) EditorGUILayout.LabelField(_exporting ? "正在获取指标并导出…" : "正在读取代理指标…");
            if (_frozenOptions?.arm == AgentExperimentArm.Paired && _armIndex == 0)
            {
                using (new EditorGUI.DisabledScope(!session.Completed || !_completedExported || _measurementBusy || session.Divergence != null))
                    if (GUILayout.Button("结束 F 并启动独立 S 组"))
                    {
                        _nextArmRequested = true;
                        EditorApplication.isPlaying = false;
                    }
            }
            EditorGUILayout.Space();
            DrawResidents(session);
        }

        private void Command(AgentExperimentLauncher launcher, Action command) => Try(() => {
            if (launcher.Session.Divergence != null) throw new InvalidOperationException(launcher.Session.Divergence);
            launcher.SetRunning(false);
            command();
        });

        private async void MeasureOrExport(AgentExperimentLauncher launcher, bool export)
        {
            if (_measurementBusy) return;
            _measurementBusy = true;
            _exporting = export;
            _message = null;
            try
            {
                var session = launcher.Session;
                if (export)
                {
                    if (string.IsNullOrWhiteSpace(_exportParent)) throw new ArgumentException("Choose an export parent directory.");
                    launcher.SetRunning(false);
                }
                if (session.RunMode == "live")
                {
                    try
                    {
                        await session.RefreshMeasurementsAsync(launcher.ProxyEndpoint);
                        _measurementStatus = "代理指标已刷新；未报告的字段仍为未知。";
                        _measurementFailed = false;
                    }
                    catch (Exception exception)
                    {
                        bool previous = !string.IsNullOrWhiteSpace(session.MeasurementsJson) && session.MeasurementsJson != "{}";
                        _measurementStatus = "本次代理指标未获取：" + exception.Message
                            + (previous ? "。导出保留此前指标；缺失字段保持未知。" : "。导出仍可继续；型号、用量和费用保持未知。");
                        _measurementFailed = true;
                    }
                }
                if (this == null) return;
                if (!EditorApplication.isPlaying || launcher == null || launcher.Session != session)
                    throw new InvalidOperationException("The experiment was closed while reading measurements; no package was exported.");
                if (export) Export(session);
            }
            catch (Exception exception) { _message = exception.Message; }
            finally
            {
                _measurementBusy = false;
                _exporting = false;
                if (this != null) Repaint();
            }
        }

        private void Export(AgentExperimentSession session)
        {
            if (string.IsNullOrWhiteSpace(_exportParent)) throw new ArgumentException("Choose an export parent directory.");
            string directory = Path.GetFullPath(Path.Combine(_exportParent, session.ExperimentId + "-"
                + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)));
            session.Export(directory);
            _lastExport = directory;
            if (session.Completed) _completedExported = true;
        }

        private void DrawResidents(AgentExperimentSession session)
        {
            var controller = session.Controller;
            EditorGUILayout.LabelField("四人当前状态", EditorStyles.boldLabel);
            var actors = controller.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NpcWorldResident2D>(true)).ToArray();
            for (int index = 0; index < ResidentIds.Length; index++)
            {
                string id = ResidentIds[index];
                var state = controller.GetAgentState(id);
                var actor = actors.SingleOrDefault(value => value.NpcId == id);
                var meeting = controller.GetMeeting(id);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Button(ResidentNames[index] + " · " + (meeting?.State.ToString() ?? "无会面"),
                        index == _selectedResident ? EditorStyles.boldLabel : EditorStyles.label)) _selectedResident = index;
                    EditorGUILayout.LabelField($"{state.Target.ExpectedActivity} → {state.Target.TargetLocationId} · {actor?.Status.ToString() ?? "无身体"} · {actor?.Position.ToString() ?? "未知位置"}");
                    if (session.Services.EconomyState.TryGetCharacter(id, out var character))
                        EditorGUILayout.LabelField($"本人金币 {character.Wallet.Balance} · 背包 " + string.Join(", ", character.Backpack.Items.Select(item => item.ItemId + " × " + item.Quantity)));
                }
            }
            string selected = ResidentIds[_selectedResident];
            var selectedState = controller.GetAgentState(selected);
            EditorGUILayout.LabelField(ResidentNames[_selectedResident] + " 的观察与经历", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel("世界代次 " + selectedState.WorldRunId.ToString("N") + " · 修订 " + selectedState.Revision, GUILayout.Height(18));
            var selectedMeeting = controller.GetMeeting(selected);
            if (selectedMeeting != null)
            {
                EditorGUILayout.LabelField($"会面 {selectedMeeting.Id:N} · {selectedMeeting.State} · 当前发言者 {selectedMeeting.SpeakerId}");
                var terms = selectedMeeting.ResourceTerms;
                if (terms != null) EditorGUILayout.LabelField($"承诺：{terms.SellerId} → {terms.BuyerId} · {terms.ItemId} × {terms.Quantity} / {terms.TotalPrice} 金币");
                EditorGUILayout.LabelField("交付结果：" + (selectedMeeting.DeliveryResultCode ?? "尚无"));
                foreach (var line in selectedMeeting.Transcript)
                    EditorGUILayout.LabelField($"[{line.TotalMinutes:F1}] {line.SpeakerId}: {line.Text}", EditorStyles.wordWrappedLabel);
            }
            foreach (var memory in controller.GetMeetingMemories(selected))
                EditorGUILayout.LabelField($"经历 [{memory.TotalMinutes:F1}] {memory.Kind} · {memory.PartnerId} · {memory.SpeakerId} {memory.Text}", EditorStyles.wordWrappedLabel);
            _showFacts = EditorGUILayout.Foldout(_showFacts, "当前局部事实（来源、时间与可表达性）", true);
            if (_showFacts)
            {
                var observation = controller.GetObservation(selected);
                EditorGUILayout.LabelField($"观察时间 {observation.ObservedAtTotalMinutes:F1} · 空间 {observation.SpaceId} · 区域 {observation.RegionId} · 范围 {observation.Radius}");
                foreach (var fact in observation.Facts)
                    EditorGUILayout.LabelField($"{fact.EntityId}.{fact.Predicate} = {fact.Value}\n{fact.Knowledge} / {fact.Source} · {fact.ObservedAtTotalMinutes:F1} · 可表达 {fact.CanExpress} · 说话者 {fact.SpeakerId}", EditorStyles.wordWrappedLabel);
            }
            _showAudit = EditorGUILayout.Foldout(_showAudit, "实验审计（包含读档前记录，不作为角色记忆）", true);
            if (_showAudit) DrawAudit(session, selected);
        }

        private void DrawAudit(AgentExperimentSession session, string npcId)
        {
            var outcome = session.Outcomes.LastOrDefault(value => value.NpcId == npcId);
            if (outcome != null)
            {
                EditorGUILayout.LabelField($"最近结果 {outcome.Code} · calls {outcome.Calls} · {(outcome.FinishedAtSeconds - outcome.StartedAtSeconds):F3}s");
                EditorGUILayout.LabelField("结果世界代次 " + outcome.WorldRunId.ToString("N"));
                EditorGUILayout.LabelField("候选拒绝：" + string.Join(", ", outcome.CandidateErrorCodes));
                EditorGUILayout.LabelField("响应自由文本：" + (outcome.Reply?.Text ?? "无"), EditorStyles.wordWrappedLabel);
                var frame = outcome.Reply?.SpeechFrame;
                if (frame != null) EditorGUILayout.LabelField($"响应事实框架：{frame.Intent} · {frame.FactId} · {frame.Tone}", EditorStyles.wordWrappedLabel);
                if (outcome.ExecutionObservation != null)
                    EditorGUILayout.LabelField($"执行观察时间 {outcome.ExecutionObservation.ObservedAtTotalMinutes:F1} · 区域 {outcome.ExecutionObservation.RegionId} · 事实 {outcome.ExecutionObservation.Facts.Count} 条");
            }
            var call = session.CaptureTraceReport().calls.LastOrDefault(value => {
                string request = string.IsNullOrEmpty(value.replayRequestJson) ? value.requestJson : value.replayRequestJson;
                return !string.IsNullOrEmpty(request) && JsonUtility.FromJson<RequestOwner>(request)?.npcId == npcId;
            });
            if (call == null) return;
            EditorGUILayout.LabelField($"请求 #{call.callOrdinal} · 发送 Tick {call.dispatchTick} · 交付 Tick {call.deliveryTick} · {call.completionKind}");
            EditorGUILayout.LabelField("请求模型：" + (call.requestedModel ?? "未提供") + " · 返回模型：" + (call.returnedModel ?? "未提供"));
            EditorGUILayout.LabelField("用量：" + (call.usageJson ?? "未提供"), EditorStyles.wordWrappedLabel);
            _showRequest = EditorGUILayout.Foldout(_showRequest, "请求与回放来源", true);
            if (_showRequest)
            {
                if (!string.IsNullOrEmpty(call.replayRequestJson))
                {
                    EditorGUILayout.LabelField("本轮回放请求");
                    Selectable(call.replayRequestJson);
                    EditorGUILayout.LabelField("来源包请求");
                }
                Selectable(call.requestJson);
            }
            if (!string.IsNullOrEmpty(call.replayResponseJson))
            {
                EditorGUILayout.LabelField("本轮回放响应");
                Selectable(call.replayResponseJson);
            }
            EditorGUILayout.LabelField(string.IsNullOrEmpty(call.replayResponseJson) ? "已记录候选响应" : "来源包候选响应");
            Selectable(call.rawResponseJson ?? call.responseJson ?? "尚未记录响应");
        }

        [Serializable] private sealed class RequestOwner { public string npcId; }
        private static void Selectable(string value) => EditorGUILayout.SelectableLabel(value, EditorStyles.textArea, GUILayout.Height(110));

        private void Try(Action action)
        {
            try { action(); _message = null; }
            catch (Exception exception) { _message = exception.Message; }
        }
    }

    [InitializeOnLoad]
    internal static class AgentExperimentSceneRestore
    {
        private const string Key = "CozyTown.AgentExperiment.SceneSetup";
        [Serializable] private sealed class SavedSetup { public SavedScene[] scenes; }
        [Serializable] private sealed class SavedScene { public string path; public bool loaded, active; }

        static AgentExperimentSceneRestore()
        {
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredEditMode) Restore();
            };
        }

        internal static void Remember()
        {
            var setup = new SavedSetup { scenes = EditorSceneManager.GetSceneManagerSetup().Select(scene =>
                new SavedScene { path = scene.path, loaded = scene.isLoaded, active = scene.isActive }).ToArray() };
            SessionState.SetString(Key, JsonUtility.ToJson(setup));
        }

        internal static void Restore()
        {
            string json = SessionState.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            SessionState.EraseString(Key);
            var setup = JsonUtility.FromJson<SavedSetup>(json);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var saved = setup.scenes.Where(scene => !string.IsNullOrEmpty(scene.path)).Select(scene => new SceneSetup {
                path = scene.path, isLoaded = scene.loaded, isActive = scene.active }).ToArray();
            if (saved.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(saved);
        }
    }
}
