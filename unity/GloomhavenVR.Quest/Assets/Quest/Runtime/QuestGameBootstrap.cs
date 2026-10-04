#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Quest
{
    /// <summary>Starts the original scene only after owned content and compatibility validation.</summary>
    public sealed class QuestGameBootstrap : MonoBehaviour
    {
        [Serializable] sealed class BuildStamp { public int schema = 0, modBuild = 0; public string inputKey = null; }
        [Serializable] sealed class StartupState
        {
            public int schema = 1, modBuild, originalErrors, loadedScenes, completedModules;
            public string inputKey, state, failureDetail, modStage, modFailure, inviteKeyboardFailure, lastScene, scope = "original-startup-real-vr-mod";
            public string utc, contentPhase, contentFile;
            public long contentProcessedBytes, contentTotalBytes, contentOverallProcessedBytes, contentOverallTotalBytes;
            public int contentFileIndex, contentFileCount, preparationCompletedSteps, preparationTotalSteps;
            public double elapsedSeconds, lastContentProgressAgeSeconds;
            public int mainThreadFrames, startupOverallPercent;
            public bool loadingViewAvailable, contentLogWriteFailed;
            public bool contentReady, modContentReady, addressablesReady, originalBootstrapStarted, modInitializationComplete, rigReady, modLifecycleAvailable, fullGameReady, eosAuthorised, proceduralRuntimeAvailable, voiceNativeAvailable, passthroughActive, inviteKeyboardBound, inviteKeyboardVisible, realKeyboardVisible, focused, paused;
        }
        public string originalScene = "Bootstrap";
        public bool ContentReady { get; private set; }
        public bool ModContentReady { get; private set; }
        public bool OriginalBootstrapStarted { get; private set; }
        public bool ModLifecycleAvailable { get { return modLifecycle != null && modLifecycle.Available; } }
        public string State { get; private set; } = "pending";
        public string FailureDetail { get; private set; }
        string logPath;
        QuestGameStartupLog log;
        QuestGameAddressables addressables;
        QuestGameVideos videos;
        readonly QuestGamePresentationEvidence presentation = new QuestGamePresentationEvidence();
        QuestGameModLifecycle modLifecycle;
        BuildStamp build;
        string lastScene;
        int loadedScenes;
        float nextState;
        bool focused = true, paused, stateWriteWarning;
        readonly Stopwatch startupClock = Stopwatch.StartNew();
        readonly object contentSync = new object();
        QuestGameContentProgress contentProgress;
        double lastContentProgress;
        int contentLogRecords, mainThreadFrames;
        string lastContentLogPhase;
        string currentContentScope, currentContentSource;
        volatile bool destroyed;
        volatile bool contentLogWriteFailed;
        float nextLoadingView;
        // This is an explicit count of completed preparation gates, not a time
        // estimate or a claim that campaign/menu initialization has completed.
        const int PreparationTotalSteps = 5;
        int preparationCompletedSteps;
        decimal modContentWeight, gameContentWeight;
        volatile bool contentWorkRequested;
        bool deliveryViewStarted;
        int startupOverallPercent;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            // The original YML loader runs on a worker. B614 failed because its
            // redirected StreamingAssets getter queried a main-thread-only Unity
            // property. Publish immutable managed paths before any original/mod
            // initializer can access them; getters never call Unity afterwards.
            logPath = Path.Combine(Application.persistentDataPath, "quest-startup.log");
            try { log = new QuestGameStartupLog(logPath, "Unity=" + Application.unityVersion + " platform=" + Application.platform + " scope=original-startup-real-vr-mod fullGameReady=false"); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[Quest startup] diagnostic log initialization failed: " + e.Message); }
            // Original rule loaders can log from workers or block the Unity main
            // thread. Persist immediately in the thread-safe managed-only sink.
            Application.logMessageReceivedThreaded += CaptureLog;
            SceneManager.sceneLoaded += SceneLoaded;
            try { QuestGame.Compatibility.Paths.Initialize(Application.persistentDataPath); }
            catch (Exception error) { Fail("original-paths", error); }
            modLifecycle = GetComponent<QuestGameModLifecycle>() ?? gameObject.AddComponent<QuestGameModLifecycle>();
            // B613 had no rendering camera until after the 68 MB bank was copied
            // and checked. Its capture stops inside that opaque delivery gate;
            // it does not prove a native crash or a particular worker deadlock.
            // Submit neutral XR frames before any worker. Artwork is requested
            // only by actual installation work; warm launches create no canvas.
            try { modLifecycle.PrepareStartupView(); }
            catch (Exception error) { Fail("startup-view", error); }
            if (GetComponent<QuestGameScope>() == null) gameObject.AddComponent<QuestGameScope>();
            UnityEngine.Debug.Log("[Quest startup] scope=original-startup-real-vr-mod Unity=" + Application.unityVersion + " platform=" + Application.platform + " fullGameReady=false");
        }

        IEnumerator Start()
        {
            var stampAsset = Resources.Load<TextAsset>("quest-build");
            var contentAsset = Resources.Load<TextAsset>("quest-startup-content");
            var modContentAsset = Resources.Load<TextAsset>("quest-mod-content");
            BuildStamp stamp = null; QuestGameContentManifest manifest = null, modManifest = null;
            try
            {
                if (stampAsset == null || contentAsset == null || modContentAsset == null) throw new InvalidDataException("Startup build/game/mod content manifests are missing.");
                stamp = JsonUtility.FromJson<BuildStamp>(stampAsset.text);
                build = stamp;
                manifest = JsonUtility.FromJson<QuestGameContentManifest>(contentAsset.text);
                modManifest = JsonUtility.FromJson<QuestGameContentManifest>(modContentAsset.text);
                if (stamp == null || stamp.schema != 1) throw new InvalidDataException("Startup build stamp is invalid.");
                QuestGameContent.Validate(manifest, stamp.inputKey);
                QuestGameContent.Validate(modManifest, stamp.inputKey, "quest-mod-content.zip");
                foreach (var file in modManifest.files) modContentWeight += file.size;
                foreach (var file in manifest.files) gameContentWeight += file.size;
                if (modManifest.files.Length != 1 || modManifest.files[0].path != "StreamingAssets/gloomhavenvr.bundle")
                    throw new InvalidDataException("Real mod content must contain exactly the manifested Android gloomhavenvr.bundle.");
                // Configure and validate the optimized native hash ABI before
                // workers start; a missing Android plugin must fail explicitly.
                QuestGameContent.ConfigureNativeHash();
                UnityEngine.Debug.Log("[Quest startup] ModBuild=" + stamp.modBuild + " input=" + stamp.inputKey + " contentFiles=" + manifest.files.Length);
            }
            catch (Exception e) { Fail("manifest", e); }
            if (State == "failed") yield break;
            // Start the REAL mod before original bootstrap assets, so its
            // existing rig and input own the subsequent native loading view.
            string modRoot = Path.Combine(Application.persistentDataPath, "quest-mod-resources");
            yield return EnsureContent(modManifest, modRoot, "quest-mod-content.zip", "mod-content");
            if (State == "failed") yield break;
            // Install accepted a private installation receipt or verified new
            // bytes on its worker. A warm launch does not repeat content hashes.
            ModContentReady = true;
            preparationCompletedSteps = 1;
            State = "starting-real-mod";
            // B615's native abort occurred in a Debug performance type lookup
            // after plugin creation. A periodic snapshot still said "checking
            // mod content" because synchronous module installation delayed
            // Update. Persist the boundary before entering the original plugin.
            SaveState();
            yield return modLifecycle.Activate(modRoot);
            if (!modLifecycle.Available) { Fail("real-mod-lifecycle", new InvalidOperationException(modLifecycle.Failure ?? "Original plugin did not reach its observed running rig.")); yield break; }
            preparationCompletedSteps = 2;
            string root = Path.Combine(Application.persistentDataPath, "quest-owned-game");
            yield return EnsureContent(manifest, root, "quest-startup-content.zip", "content");
            if (State == "failed") yield break;
            ContentReady = true;
            preparationCompletedSteps = 3;
            UnityEngine.Debug.Log("[Quest startup] Owned file-backed content available at " + root);
            try
            {
                var moviesAsset = Resources.Load<TextAsset>("quest-startup-movies");
                if (moviesAsset == null) throw new InvalidDataException("Original startup movie manifest is missing.");
                videos = new QuestGameVideos();
                videos.Install(JsonUtility.FromJson<QuestGameMovieManifest>(moviesAsset.text), manifest, root, stamp.inputKey);
            }
            catch (Exception error) { Fail("original-videos", error); }
            if (State == "failed") yield break;
            var addressablesAsset = Resources.Load<TextAsset>("quest-startup-addressables");
            if (addressablesAsset == null) { Fail("native-addressables", new InvalidDataException("Native startup Addressables manifest is missing.")); yield break; }
            QuestGameAddressablesManifest addressablesManifest = null;
            try { addressablesManifest = JsonUtility.FromJson<QuestGameAddressablesManifest>(addressablesAsset.text); }
            catch (Exception e) { Fail("native-addressables-manifest", e); }
            if (State == "failed") yield break;
            State = "initializing-native-addressables";
            SaveState();
            addressables = new QuestGameAddressables();
            yield return addressables.Install(addressablesManifest, manifest, root, stamp.inputKey);
            if (!addressables.Ready) { Fail("native-addressables", addressables.Failure ?? new InvalidDataException("Native Addressables startup did not complete.")); yield break; }
            preparationCompletedSteps = 4;
            UnityEngine.Debug.LogWarning("[Quest startup] real-mod lifecycle observed=" + ModLifecycleAvailable + "; fullGameReady=false. EOSAuthorised=false; crossplay unverified. proceduralRuntimeAvailable=false; native Apparance lifecycle disabled for menu-only target, campaign generation remains gated. voiceNativeAvailable=false; Android Opus remains gated.");
            if (!Application.CanStreamedLevelBeLoaded(originalScene)) { Fail("original-scene", new InvalidDataException("Required original scene is absent: " + originalScene)); yield break; }
            State = "loading-original-bootstrap";
            SaveState();
            AsyncOperation loading = SceneManager.LoadSceneAsync(originalScene, LoadSceneMode.Single);
            if (loading == null) { Fail("original-scene", new InvalidOperationException("Unity refused original scene load.")); yield break; }
            yield return loading;
        }

        IEnumerator EnsureContent(QuestGameContentManifest manifest, string root, string expectedArchive, string phase)
        {
            State = "checking-" + phase;
            lock (contentSync) contentProgress = null;
            UnityEngine.Debug.Log("[Quest startup] content check started phase=" + phase);
            SaveState();
            string archive = Path.Combine(Application.persistentDataPath, expectedArchive + ".download");
            // Capture Unity paths on the main thread. Android's monolithic APK
            // is a local ZIP: worker-owned streaming avoids the jar/UWR completion
            // boundary and provides byte progress even before the first camera.
            bool sourceIsApk = Application.platform == RuntimePlatform.Android && !Application.isEditor;
            string source = sourceIsApk ? Application.dataPath : Path.Combine(Application.streamingAssetsPath, manifest.archive);
            currentContentScope = phase;
            currentContentSource = sourceIsApk ? "installed-apk" : "local-streaming-assets";
            // One worker reuses a valid installation receipt without content
            // reads or progress. Legacy adoption is quiet; only actual new bytes
            // request a shared loading view through the managed progress sink.
            Task<QuestGameContentDeliveryResult> delivery = Task.Run(() => QuestGameContent.Install(manifest, root, source, sourceIsApk, archive, expectedArchive, ReportContentProgress));
            while (!delivery.IsCompleted)
            {
                QuestGameContentProgress progress; lock (contentSync) progress = contentProgress;
                string workerPhase = progress != null ? progress.Phase : null;
                State = workerPhase == "copying-archive" ? "copying-" + phase
                    : workerPhase == "extracting-file" || workerPhase == "verifying-file" || workerPhase == "verifying-archive" ? "extracting-" + phase
                    : "checking-" + phase;
                UpdateStartupPresentation();
                yield return null;
            }
            UpdateStartupPresentation();
            if (delivery.IsFaulted) { Fail(phase + "-delivery", delivery.Exception.GetBaseException()); yield break; }
            try { if (File.Exists(archive)) File.Delete(archive); }
            catch (Exception e) { Fail(phase + "-archive-cleanup", e); }
            if (State == "failed") yield break;
            var result = delivery.Result;
            UnityEngine.Debug.Log("[Quest startup] content delivery completed phase=" + phase
                + " reusedContent=" + result.ReusedContent + " copiedArchive=" + result.CopiedArchive + " reusedArchive=" + result.ReusedArchive
                + " verifiedFiles=" + result.VerifiedFiles + " extractedFiles=" + result.ExtractedFiles
                + " verifiedBytes=" + result.VerifiedBytes + " copiedBytes=" + result.CopiedBytes + " extractedBytes=" + result.ExtractedBytes
                + " installation=" + result.InstallationState + " receiptReused=" + result.InstallationReceiptReused
                + " metadataCheckedFiles=" + result.MetadataCheckedFiles);
            SaveState();
        }

        void ReportContentProgress(QuestGameContentProgress progress)
        {
            if (destroyed) throw new OperationCanceledException("Startup owner was destroyed.");
            if (progress.Phase == "copying-archive" || progress.Phase == "extracting-file") contentWorkRequested = true;
            bool record = false;
            lock (contentSync)
            {
                contentProgress = progress;
                lastContentProgress = startupClock.Elapsed.TotalSeconds;
                // File-level detail stays in the overwritten state snapshot.
                // Bound normal lifecycle records independently of file count.
                if (lastContentLogPhase != progress.Phase && contentLogRecords < 32)
                {
                    lastContentLogPhase = progress.Phase; contentLogRecords++; record = true;
                }
            }
            // Managed-only sink: a worker never invokes Unity logging or APIs.
            if (record && log != null)
            {
                try
                {
                    log.Append("[Quest startup] content worker phase=" + progress.Phase
                        + " bytes=" + progress.ProcessedBytes + "/" + progress.TotalBytes, null);
                    if (progress.Phase == "copying-archive")
                        log.Append("[Quest startup] archive copy started phase=" + currentContentScope + " source=" + currentContentSource, null);
                }
                catch (IOException) { contentLogWriteFailed = true; }
                catch (UnauthorizedAccessException) { contentLogWriteFailed = true; }
            }
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (videos != null)
                try { videos.BindScene(scene); }
                catch (Exception error) { Fail("original-video-bindings", error); }
            if (scene.name == originalScene)
            {
                OriginalBootstrapStarted = true;
                if (State != "failed")
                {
                    State = "original-bootstrap-loaded";
                    preparationCompletedSteps = PreparationTotalSteps;
                    // Keep the preparation view during Unity's asynchronous
                    // scene load. Retire it only at the observed native handover,
                    // rather than creating another unexplained blank interval.
                    UpdateStartupPresentation();
                    if (modLifecycle != null) modLifecycle.EndDeliveryView();
                }
            }
            lastScene = scene.name; loadedScenes++; SaveState();
            UnityEngine.Debug.Log("[Quest startup] original scene loaded=" + scene.name + " mode=" + mode + " state=" + State + " fullGameReady=false");
        }
        void Fail(string gate, Exception e)
        {
            State = "failed";
            FailureDetail = gate + ": " + e.GetType().Name + ": " + e.Message;
            if (FailureDetail.Length > 500) FailureDetail = FailureDetail.Substring(0, 500) + "…";
            UnityEngine.Debug.LogError("[Quest startup] blocked gate=" + gate + " originalBootstrapStarted=" + OriginalBootstrapStarted + " exception=" + e);
            UpdateStartupPresentation();
            SaveState();
        }
        void UpdateStartupPresentation()
        {
            if (modLifecycle == null) return;
            if (contentWorkRequested && !deliveryViewStarted && !OriginalBootstrapStarted)
            {
                modLifecycle.BeginDeliveryView();
                deliveryViewStarted = true;
            }
            QuestGameContentProgress progress; lock (contentSync) progress = contentProgress;
            decimal fraction = progress != null && progress.OverallTotalBytes > 0
                ? Math.Max(0m, Math.Min(1m, (decimal)progress.OverallProcessedBytes / progress.OverallTotalBytes)) : 0m;
            decimal mod = ModContentReady ? 1m : currentContentScope == "mod-content" ? fraction : 0m;
            decimal game = ContentReady ? 1m : currentContentScope == "content" ? fraction : 0m;
            decimal weight = modContentWeight + gameContentWeight;
            // Required bank bytes weight the measured preparation work. Reserve
            // five percent for observed plugin/catalog/native handover gates;
            // unmeasured native work is never described as a time estimate.
            int percent = weight > 0 ? (int)decimal.Floor(95m * (mod * modContentWeight + game * gameContentWeight) / weight) : 0;
            if (preparationCompletedSteps >= 2) ++percent;
            if (preparationCompletedSteps >= 4) percent += 2;
            bool handedOver = State == "original-bootstrap-loaded";
            startupOverallPercent = handedOver ? 100 : Math.Max(startupOverallPercent, Math.Min(99, percent));
            modLifecycle.UpdateStartupView(State, startupOverallPercent);
        }
        void Update()
        {
            mainThreadFrames++;
            if (Time.unscaledTime >= nextLoadingView)
            {
                nextLoadingView = Time.unscaledTime + .25f;
                UpdateStartupPresentation();
            }
            if (Time.unscaledTime >= nextState)
            {
                nextState = Time.unscaledTime + 5;
                if (modLifecycle != null) modLifecycle.Observe();
                if (videos != null) videos.Observe();
                if (OriginalBootstrapStarted) presentation.Observe(lastScene);
                SaveState();
            }
        }
        void OnApplicationPause(bool value) { paused = value; SaveState(); }
        void OnApplicationFocus(bool value) { focused = value; SaveState(); }
        internal void SaveState()
        {
            if (logPath == null) return;
            QuestGameContentProgress progress; double progressTime;
            lock (contentSync) { progress = contentProgress; progressTime = lastContentProgress; }
            var state = new StartupState { modBuild = build != null ? build.modBuild : 0, inputKey = build != null ? build.inputKey : null, state = State, failureDetail = FailureDetail,
                utc = DateTime.UtcNow.ToString("O"), elapsedSeconds = startupClock.Elapsed.TotalSeconds, mainThreadFrames = mainThreadFrames,
                contentPhase = progress != null ? progress.Phase : null, contentFile = progress != null ? progress.File : null,
                contentProcessedBytes = progress != null ? progress.ProcessedBytes : 0, contentTotalBytes = progress != null ? progress.TotalBytes : 0,
                contentOverallProcessedBytes = progress != null ? progress.OverallProcessedBytes : 0,
                contentOverallTotalBytes = progress != null ? progress.OverallTotalBytes : 0,
                contentFileIndex = progress != null ? progress.FileIndex : 0, contentFileCount = progress != null ? progress.FileCount : 0,
                preparationCompletedSteps = preparationCompletedSteps, preparationTotalSteps = PreparationTotalSteps,
                lastContentProgressAgeSeconds = progress != null ? Math.Max(0, startupClock.Elapsed.TotalSeconds - progressTime) : 0,
                loadingViewAvailable = modLifecycle != null && modLifecycle.StartupViewAvailable,
                startupOverallPercent = startupOverallPercent,
                contentLogWriteFailed = contentLogWriteFailed,
                contentReady = ContentReady, modContentReady = ModContentReady, addressablesReady = addressables != null && addressables.Ready, originalBootstrapStarted = OriginalBootstrapStarted,
                modLifecycleAvailable = ModLifecycleAvailable, fullGameReady = false, eosAuthorised = false, originalErrors = log != null ? log.OriginalErrors : 0,
                modInitializationComplete = modLifecycle != null && modLifecycle.InitializationComplete, completedModules = modLifecycle != null ? modLifecycle.CompletedModules : 0,
                rigReady = modLifecycle != null && modLifecycle.RigReady, modStage = modLifecycle != null ? modLifecycle.Stage : null, modFailure = modLifecycle != null ? modLifecycle.Failure : null,
                passthroughActive = QuestPassthroughFeature.Active,
                // The existing keyboard serves any original TMP field. Module
                // completion does not prove an invite field or invite binding.
                realKeyboardVisible = modLifecycle != null && modLifecycle.InviteKeyboardVisible,
                inviteKeyboardBound = false, inviteKeyboardVisible = false, inviteKeyboardFailure = null, proceduralRuntimeAvailable = false, voiceNativeAvailable = false,
                loadedScenes = loadedScenes, lastScene = lastScene, focused = focused, paused = paused };
            string path = Path.Combine(Application.persistentDataPath, "quest-startup-state.json"), temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonUtility.ToJson(state, true));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch (Exception e)
            {
                if (!stateWriteWarning) { stateWriteWarning = true; UnityEngine.Debug.LogWarning("[Quest startup] state persistence failed: " + e.Message); }
            }
        }
        void CaptureLog(string message, string stack, LogType type)
        {
            try
            {
                if (type == LogType.Exception || type == LogType.Error)
                {
                    if (log != null) log.AppendOriginalError("original " + type + " " + message, stack);
                }
                else if (message != null && (message.StartsWith("[Quest startup]", StringComparison.Ordinal) || message.StartsWith("[GloomhavenVR]", StringComparison.Ordinal)))
                {
                    if (log != null) log.Append(message, stack);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        void OnDestroy() { destroyed = true; Application.logMessageReceivedThreaded -= CaptureLog; SceneManager.sceneLoaded -= SceneLoaded; if (videos != null) videos.Dispose(); if (addressables != null) addressables.Dispose(); }
    }
}
#endif
