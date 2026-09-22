using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CozyTown.Unity.CameraView;
using CozyTown.Unity.Experiments;
using CozyTown.Unity.Npc;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CozyTown.Unity.Editor
{
    [InitializeOnLoad]
    public static class AgentExperimentEntryProbe
    {
        private const string StateKey = "CozyTown.AgentExperiment.EntryProbe";
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private static ProbeResult _state;
        private static Camera _camera;
        private static RenderTexture _target, _previousTarget;
        private static bool _rendered;

        [Serializable]
        private sealed class SceneIdentity { public string path; public bool loaded, active; }
        [Serializable]
        private sealed class ResidentEvidence
        {
            public string npcId, route, target, sprite;
            public float x, y;
            public int facts;
            public double observationMinutes;
        }
        [Serializable]
        private sealed class ProbeResult
        {
            public string outputDirectory, stage, failure, editorCaptureFailure, gameCaptureFailure, sceneHashBefore, sceneHashAfter;
            public string unityVersion, windowPng, scenePng;
            public double startedAt, stageStartedAt, realBeforePause, realAfterPause, minutesBefore, minutesAfter;
            public long initialCalls, callsAfterStep;
            public bool menuOpened, initialPaused, wallClockContinues, gamePaused, steppedOneMinute,
                windowWithinDesktop, editorImageHasContent, editorPixelsCaptured,
                sceneRendered, sceneRestored, sceneAssetUnchanged, passed;
            public Rect windowRect, windowContentRect, desktopBounds;
            public float pixelsPerPoint, editorNearBlackFraction;
            public int editorDistinctColorsCappedAt33;
            public SceneIdentity[] originalScenes;
            public ResidentEvidence[] residents;
        }

        static AgentExperimentEntryProbe()
        {
            string saved = SessionState.GetString(StateKey, string.Empty);
            if (!string.IsNullOrEmpty(saved)) _state = JsonUtility.FromJson<ProbeResult>(saved);
            EditorApplication.update += Update;
        }

        public static void Run()
        {
            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-platform", "editor-entry-" + Guid.NewGuid().ToString("N")));
            string[] args = Environment.GetCommandLineArgs();
            for (int index = 0; index < args.Length - 1; index++)
                if (args[index] == "-experimentProbeOutput") directory = Path.GetFullPath(args[index + 1]);
            if (Directory.Exists(directory)) throw new InvalidOperationException("Choose a new directory for the editor entry probe.");
            Directory.CreateDirectory(directory);
            _state = new ProbeResult { outputDirectory = directory, stage = "starting",
                startedAt = EditorApplication.timeSinceStartup, unityVersion = Application.unityVersion,
                originalScenes = CurrentScenes(), sceneHashBefore = HashScene() };
            Persist();
            try
            {
                if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("The editor entry probe requires a non-batch Unity editor with a graphics device.");
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Start the probe from Edit mode.");
                if (Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                    .Any(index => UnityEngine.SceneManagement.SceneManager.GetSceneAt(index).isDirty))
                    throw new InvalidOperationException("Save the isolated task scenes before running the probe.");
                _state.menuOpened = EditorApplication.ExecuteMenuItem("CozyTown/Agent Experiment");
                if (!_state.menuOpened) throw new InvalidOperationException("The experiment menu command was not available.");
                var window = PrepareWindow();
                Stage("awaiting-play");
                window.StartExperiment(new AgentExperimentLaunchOptions { arm = AgentExperimentArm.F,
                    runMode = AgentExperimentRunMode.Fixed, scenarioId = "available", sourceRevision = "unknown" });
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void Update()
        {
            if (_state == null) return;
            try
            {
                if (_state.stage != "restoring-scene" && EditorApplication.timeSinceStartup - _state.startedAt > 120)
                    throw new TimeoutException("The editor entry probe exceeded 120 seconds.");
                EditorApplication.QueuePlayerLoopUpdate();
                switch (_state.stage)
                {
                    case "awaiting-play":
                        if (!EditorApplication.isPlaying) return;
                        var starting = Launcher();
                        if (starting.Error != null) throw new InvalidOperationException(starting.Error);
                        if (starting.Session == null || !starting.Session.Started) return;
                        _state.initialCalls = starting.Session.Controller.DecisionRequestsStarted;
                        _state.initialPaused = !starting.IsRunning;
                        if (!_state.initialPaused || _state.initialCalls != 0)
                            throw new InvalidOperationException("The editor entry dispatched before the user advanced the experiment.");
                        _state.realBeforePause = starting.RealSeconds;
                        _state.minutesBefore = starting.Session.Controller.GameTotalMinutes;
                        Stage("checking-pause");
                        break;
                    case "checking-pause":
                        if (Elapsed < 0.5) return;
                        var paused = Launcher();
                        _state.realAfterPause = paused.RealSeconds;
                        _state.wallClockContinues = _state.realAfterPause > _state.realBeforePause;
                        _state.gamePaused = paused.Session.Controller.GameTotalMinutes == _state.minutesBefore
                            && paused.Session.Controller.DecisionRequestsStarted == 0 && paused.Session.Tick == 0;
                        if (!_state.wallClockContinues || !_state.gamePaused)
                            throw new InvalidOperationException("Pausing must preserve game time while the real clock continues.");
                        paused.AdvanceOneMinute();
                        _state.minutesAfter = paused.Session.Controller.GameTotalMinutes;
                        _state.callsAfterStep = paused.Session.Controller.DecisionRequestsStarted;
                        _state.steppedOneMinute = _state.minutesAfter == _state.minutesBefore + 1 && paused.Session.Tick == 1;
                        if (!_state.steppedOneMinute || _state.callsAfterStep == 0)
                            throw new InvalidOperationException("A single editor step must advance one game minute and enable fixed decisions.");
                        RecordResidents(paused);
                        PrepareWindow();
                        Stage("capturing-editor");
                        break;
                    case "capturing-editor":
                        if (Elapsed < 1) return;
                        var window = EditorWindow.GetWindow<AgentExperimentWindow>();
                        if (InternalEditorUtility.isApplicationActive && window.hasFocus && !window.docked)
                        {
                            try { CaptureEditor(window); }
                            catch (Exception exception) { _state.editorCaptureFailure = exception.Message; }
                        }
                        else
                        {
                            if (Elapsed < 5) { window.Focus(); window.Repaint(); return; }
                            _state.editorCaptureFailure = "Unity was not visibly active with a focused floating experiment window; no desktop pixels were read.";
                        }
                        BeginSceneCapture();
                        Stage("capturing-scene");
                        break;
                    case "capturing-scene":
                        if (!_rendered && Elapsed < 10) return;
                        try
                        {
                            if (!_rendered) throw new TimeoutException("The scene camera did not render its target within ten seconds.");
                            CaptureScene();
                        }
                        catch (Exception exception) { _state.gameCaptureFailure = exception.Message; }
                        finally { ReleaseTarget(); }
                        Stage("restoring-scene");
                        EditorApplication.isPlaying = false;
                        break;
                    case "restoring-scene":
                        if (Elapsed > 15) throw new TimeoutException("The editor did not leave Play and restore its scene within fifteen seconds.");
                        if (EditorApplication.isPlayingOrWillChangePlaymode || Elapsed < 0.5) return;
                        _state.sceneRestored = SameScenes(_state.originalScenes, CurrentScenes())
                            && UnityEngine.Object.FindObjectsByType<AgentExperimentLauncher>(FindObjectsSortMode.None).Length == 0;
                        _state.sceneHashAfter = HashScene();
                        _state.sceneAssetUnchanged = _state.sceneHashAfter == _state.sceneHashBefore;
                        if (!_state.sceneRestored || !_state.sceneAssetUnchanged)
                            throw new InvalidOperationException("Leaving Play did not restore the original scene setup without changing the development scene asset.");
                        Finish();
                        break;
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static double Elapsed => EditorApplication.timeSinceStartup - _state.stageStartedAt;
        private static AgentExperimentLauncher Launcher()
            => UnityEngine.Object.FindObjectsByType<AgentExperimentLauncher>(FindObjectsSortMode.None).Single();

        private static AgentExperimentWindow PrepareWindow()
        {
            var window = EditorWindow.GetWindow<AgentExperimentWindow>("四人实验");
            window.ShowUtility();
            Rect display = InternalEditorUtility.GetBoundsOfDesktopAtPoint(window.position.center);
            window.position = new Rect(display.x + 24, display.y + 48,
                Mathf.Min(960, display.width - 48), Mathf.Min(940, display.height - 96));
            window.Focus();
            window.Repaint();
            return window;
        }

        private static void CaptureEditor(AgentExperimentWindow window)
        {
            if (!InternalEditorUtility.isApplicationActive || !window.hasFocus || window.docked)
                throw new InvalidOperationException("The experiment window lost foreground visibility before capture.");
            _state.windowRect = window.position;
            _state.pixelsPerPoint = window.LastRepaintPixelScale;
            if (window.LastRepaintWindowPosition != window.position || window.LastRepaintTime < _state.stageStartedAt)
                throw new InvalidOperationException("The experiment window has not repainted at its current position.");
            Rect screen = window.LastRepaintScreenBounds;
            _state.windowContentRect = Rect.MinMaxRect(Mathf.Ceil(screen.xMin), Mathf.Ceil(screen.yMin),
                Mathf.Floor(screen.xMax), Mathf.Floor(screen.yMax));
            _state.desktopBounds = InternalEditorUtility.GetBoundsOfDesktopAtPoint(screen.center);
            Rect pixels = _state.windowContentRect;
            int width = Mathf.FloorToInt(pixels.width), height = Mathf.FloorToInt(pixels.height);
            if (width < 100 || height < 100) throw new InvalidOperationException("The experiment window has no capturable content rectangle.");
            _state.windowWithinDesktop = pixels.xMin >= _state.desktopBounds.xMin && pixels.yMin >= _state.desktopBounds.yMin
                && pixels.xMax <= _state.desktopBounds.xMax && pixels.yMax <= _state.desktopBounds.yMax;
            if (!_state.windowWithinDesktop)
                throw new InvalidOperationException("The complete experiment content rectangle is outside the desktop bounds; no desktop pixels were read.");
            var colors = InternalEditorUtility.ReadScreenPixel(pixels.position, width, height);
            if (!InternalEditorUtility.isApplicationActive || !window.hasFocus || window.position != _state.windowRect)
                throw new InvalidOperationException("The experiment window lost focus or moved during capture; the image was discarded.");
            if (colors == null || colors.Length != width * height)
                throw new InvalidOperationException("Unity did not return the complete experiment content rectangle.");
            _state.editorNearBlackFraction = colors.Count(color => color.r < 0.01f && color.g < 0.01f && color.b < 0.01f) / (float)colors.Length;
            _state.editorDistinctColorsCappedAt33 = colors.Select(color => (Color32)color).Distinct().Take(33).Count();
            _state.editorImageHasContent = _state.editorNearBlackFraction < 0.1f && _state.editorDistinctColorsCappedAt33 == 33;
            if (!_state.editorImageHasContent)
                throw new InvalidOperationException("The experiment image contains excessive near-black pixels or fewer than 33 distinct colors; visual evidence was rejected.");
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.SetPixels(colors);
                texture.Apply();
                _state.windowPng = Path.Combine(_state.outputDirectory, "experiment-window.png");
                File.WriteAllBytes(_state.windowPng, texture.EncodeToPNG());
                _state.editorPixelsCaptured = true;
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void RecordResidents(AgentExperimentLauncher launcher)
        {
            var residents = launcher.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NpcWorldResident2D>(true)).OrderBy(actor => actor.NpcId).ToArray();
            if (residents.Length != 4 || residents.Select(actor => actor.NpcId).Distinct().Count() != 4)
                throw new InvalidOperationException("The editor entry did not instantiate four distinct residents.");
            _state.residents = residents.Select(actor => {
                var observation = launcher.Session.Controller.GetObservation(actor.NpcId);
                var sprite = actor.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(renderer => renderer.enabled
                    && renderer.gameObject.activeInHierarchy && renderer.sprite != null && renderer.sprite.name.StartsWith("npc_", StringComparison.Ordinal));
                if (sprite == null) throw new InvalidOperationException("A resident has no rendered sprite: " + actor.NpcId);
                return new ResidentEvidence { npcId = actor.NpcId, x = actor.Position.x, y = actor.Position.y,
                    route = actor.Status.ToString(), target = actor.TargetLocationId, sprite = sprite.sprite.name,
                    facts = observation.Facts.Count, observationMinutes = observation.ObservedAtTotalMinutes };
            }).ToArray();
        }

        private static void BeginSceneCapture()
        {
            var launcher = Launcher();
            _camera = launcher.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Single(camera => camera.CompareTag("MainCamera"));
            var follow = _camera.GetComponent<CozyTownFollowCamera2D>();
            if (follow != null) follow.enabled = false;
            var pixelPerfect = _camera.GetComponent<PixelPerfectCamera>();
            if (pixelPerfect != null) pixelPerfect.enabled = false;
            var bounds = new Bounds(new Vector3(_state.residents[0].x, _state.residents[0].y, 0), Vector3.zero);
            foreach (var resident in _state.residents) bounds.Encapsulate(new Vector3(resident.x, resident.y + 1, 0));
            _camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, _camera.transform.position.z);
            _camera.orthographicSize = Mathf.Max(bounds.extents.y + 2, (bounds.extents.x + 2) * 540f / 960f);
            _previousTarget = _camera.targetTexture;
            _target = new RenderTexture(960, 540, 24);
            _camera.targetTexture = _target;
            _rendered = false;
            RenderPipelineManager.endCameraRendering += OnCameraRendered;
        }

        private static void OnCameraRendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera) _rendered = true;
        }

        private static void CaptureScene()
        {
            var previous = RenderTexture.active;
            var texture = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = _target;
                texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                texture.Apply();
                foreach (var resident in _state.residents)
                {
                    var viewport = _camera.WorldToViewportPoint(new Vector3(resident.x, resident.y + 1, 0));
                    if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1)
                        throw new InvalidOperationException("A resident was outside the inspection camera: " + resident.npcId);
                }
                if (texture.GetPixels32().Distinct().Take(17).Count() < 17)
                    throw new InvalidOperationException("The scene render target contained fewer than 17 distinct colors.");
                _state.scenePng = Path.Combine(_state.outputDirectory, "four-resident-scene.png");
                File.WriteAllBytes(_state.scenePng, texture.EncodeToPNG());
                _state.sceneRendered = true;
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void ReleaseTarget()
        {
            RenderPipelineManager.endCameraRendering -= OnCameraRendered;
            if (_camera != null) _camera.targetTexture = _previousTarget;
            if (_target != null) { _target.Release(); UnityEngine.Object.DestroyImmediate(_target); }
            _target = null;
        }

        private static SceneIdentity[] CurrentScenes() => EditorSceneManager.GetSceneManagerSetup().Select(scene =>
            new SceneIdentity { path = scene.path, loaded = scene.isLoaded, active = scene.isActive }).ToArray();
        private static bool SameScenes(SceneIdentity[] left, SceneIdentity[] right) => left.Length == right.Length
            && left.Zip(right, (a, b) => a.path == b.path && a.loaded == b.loaded && a.active == b.active).All(value => value);
        private static string HashScene()
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(ScenePath))).Replace("-", string.Empty);
        }
        private static void Stage(string stage)
        {
            _state.stage = stage;
            _state.stageStartedAt = EditorApplication.timeSinceStartup;
            Persist();
        }
        private static void Persist()
        {
            string json = JsonUtility.ToJson(_state, true);
            SessionState.SetString(StateKey, json);
            File.WriteAllText(Path.Combine(_state.outputDirectory, "probe-result.json"), json);
        }
        private static void Fail(Exception exception)
        {
            _state.failure = exception.Message;
            ReleaseTarget();
            if (_state.stage == "restoring-scene") { Finish(); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Stage("restoring-scene");
                EditorApplication.isPlaying = false;
            }
            else Finish();
        }
        private static void Finish()
        {
            _state.stage = "finished";
            _state.passed = _state.failure == null && _state.windowWithinDesktop && _state.editorImageHasContent
                && _state.editorPixelsCaptured && _state.sceneRendered
                && _state.sceneRestored && _state.sceneAssetUnchanged;
            Persist();
            bool passed = _state.passed;
            Debug.Log("Agent experiment editor probe: " + Path.Combine(_state.outputDirectory, "probe-result.json"));
            SessionState.EraseString(StateKey);
            _state = null;
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
