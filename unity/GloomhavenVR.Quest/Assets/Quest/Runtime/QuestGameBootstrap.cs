#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
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
        QuestGameModLifecycle modLifecycle;
        BuildStamp build;
        string lastScene;
        int loadedScenes;
        float nextState;
        bool focused = true, paused, stateWriteWarning;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            logPath = Path.Combine(Application.persistentDataPath, "quest-startup.log");
            try { log = new QuestGameStartupLog(logPath, "Unity=" + Application.unityVersion + " platform=" + Application.platform + " scope=original-startup-real-vr-mod fullGameReady=false"); }
            catch (Exception e) { Debug.LogWarning("[Quest startup] diagnostic log initialization failed: " + e.Message); }
            // Original rule loaders can log from workers or block the Unity main
            // thread. Persist immediately in the thread-safe managed-only sink.
            Application.logMessageReceivedThreaded += CaptureLog;
            SceneManager.sceneLoaded += SceneLoaded;
            modLifecycle = GetComponent<QuestGameModLifecycle>() ?? gameObject.AddComponent<QuestGameModLifecycle>();
            if (GetComponent<QuestGameScope>() == null) gameObject.AddComponent<QuestGameScope>();
            Debug.Log("[Quest startup] scope=original-startup-real-vr-mod Unity=" + Application.unityVersion + " platform=" + Application.platform + " fullGameReady=false");
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
                if (modManifest.files.Length != 1 || modManifest.files[0].path != "StreamingAssets/gloomhavenvr.bundle")
                    throw new InvalidDataException("Real mod content must contain exactly the manifested Android gloomhavenvr.bundle.");
                Debug.Log("[Quest startup] ModBuild=" + stamp.modBuild + " input=" + stamp.inputKey + " contentFiles=" + manifest.files.Length);
            }
            catch (Exception e) { Fail("manifest", e); }
            if (State == "failed") yield break;
            // The selected Android mod bundle is much smaller than original
            // content. Start the REAL mod first, so its existing rig and input
            // own the view while large native archives are being verified.
            string modRoot = Path.Combine(Application.persistentDataPath, "quest-mod-resources");
            yield return EnsureContent(modManifest, modRoot, "quest-mod-content.zip", "mod-content");
            if (State == "failed") yield break;
            try { QuestGameContent.ResolveVerifiedPath(modManifest, modRoot, "StreamingAssets/gloomhavenvr.bundle"); }
            catch (Exception e) { Fail("mod-bundle-verification", e); }
            if (State == "failed") yield break;
            ModContentReady = true;
            State = "starting-real-mod";
            yield return modLifecycle.Activate(modRoot);
            if (!modLifecycle.Available) { Fail("real-mod-lifecycle", new InvalidOperationException(modLifecycle.Failure ?? "Original plugin did not reach its observed running rig.")); yield break; }
            string root = Path.Combine(Application.persistentDataPath, "quest-owned-game");
            yield return EnsureContent(manifest, root, "quest-startup-content.zip", "content");
            if (State == "failed") yield break;
            ContentReady = true;
            Debug.Log("[Quest startup] Owned file-backed content verified at " + root);
            var addressablesAsset = Resources.Load<TextAsset>("quest-startup-addressables");
            if (addressablesAsset == null) { Fail("native-addressables", new InvalidDataException("Native startup Addressables manifest is missing.")); yield break; }
            QuestGameAddressablesManifest addressablesManifest = null;
            try { addressablesManifest = JsonUtility.FromJson<QuestGameAddressablesManifest>(addressablesAsset.text); }
            catch (Exception e) { Fail("native-addressables-manifest", e); }
            if (State == "failed") yield break;
            State = "initializing-native-addressables";
            addressables = new QuestGameAddressables();
            yield return addressables.Install(addressablesManifest, manifest, root, stamp.inputKey);
            if (!addressables.Ready) { Fail("native-addressables", addressables.Failure ?? new InvalidDataException("Native Addressables startup did not complete.")); yield break; }
            Debug.LogWarning("[Quest startup] real-mod lifecycle observed=" + ModLifecycleAvailable + "; fullGameReady=false. EOSAuthorised=false; crossplay unverified. proceduralRuntimeAvailable=false; native Apparance lifecycle disabled for menu-only target, campaign generation remains gated. voiceNativeAvailable=false; Android Opus remains gated.");
            if (!Application.CanStreamedLevelBeLoaded(originalScene)) { Fail("original-scene", new InvalidDataException("Required original scene is absent: " + originalScene)); yield break; }
            State = "loading-original-bootstrap";
            AsyncOperation loading = SceneManager.LoadSceneAsync(originalScene, LoadSceneMode.Single);
            if (loading == null) { Fail("original-scene", new InvalidOperationException("Unity refused original scene load.")); yield break; }
            yield return loading;
        }

        IEnumerator EnsureContent(QuestGameContentManifest manifest, string root, string expectedArchive, string phase)
        {
            State = "checking-" + phase;
            SaveState();
            Task<bool> ready = Task.Run(() => QuestGameContent.IsReady(manifest, root));
            while (!ready.IsCompleted) yield return null;
            if (ready.IsFaulted) { Fail(phase + "-check", ready.Exception.GetBaseException()); yield break; }
            if (ready.Result) yield break;
            State = "extracting-" + phase;
            SaveState();
            string archive = Path.Combine(Application.persistentDataPath, expectedArchive + ".download");
            string uri = Application.streamingAssetsPath.TrimEnd('/') + "/" + manifest.archive;
            using (UnityWebRequest request = UnityWebRequest.Get(uri))
            {
                request.downloadHandler = new DownloadHandlerFile(archive) { removeFileOnAbort = true };
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { Fail(phase + "-archive-download", new IOException(request.error)); yield break; }
            }
            Task extraction = Task.Run(() => QuestGameContent.Extract(manifest, archive, root, expectedArchive));
            while (!extraction.IsCompleted) yield return null;
            try { if (File.Exists(archive)) File.Delete(archive); }
            catch (Exception e) { Fail(phase + "-archive-cleanup", e); }
            if (State == "failed") yield break;
            if (extraction.IsFaulted) { Fail(phase + "-extract", extraction.Exception.GetBaseException()); yield break; }
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == originalScene) { OriginalBootstrapStarted = true; State = "original-bootstrap-loaded"; }
            lastScene = scene.name; loadedScenes++; SaveState();
            Debug.Log("[Quest startup] original scene loaded=" + scene.name + " mode=" + mode + " state=" + State + " fullGameReady=false");
        }
        void Fail(string gate, Exception e)
        {
            State = "failed";
            FailureDetail = gate + ": " + e.GetType().Name + ": " + e.Message;
            if (FailureDetail.Length > 500) FailureDetail = FailureDetail.Substring(0, 500) + "…";
            Debug.LogError("[Quest startup] blocked gate=" + gate + " originalBootstrapStarted=" + OriginalBootstrapStarted + " exception=" + e);
            SaveState();
        }
        void Update() { if (Time.unscaledTime >= nextState) { nextState = Time.unscaledTime + 5; if (modLifecycle != null) modLifecycle.Observe(); SaveState(); } }
        void OnApplicationPause(bool value) { paused = value; SaveState(); }
        void OnApplicationFocus(bool value) { focused = value; SaveState(); }
        void SaveState()
        {
            if (logPath == null) return;
            var state = new StartupState { modBuild = build != null ? build.modBuild : 0, inputKey = build != null ? build.inputKey : null, state = State, failureDetail = FailureDetail,
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
                if (!stateWriteWarning) { stateWriteWarning = true; Debug.LogWarning("[Quest startup] state persistence failed: " + e.Message); }
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
        void OnDestroy() { Application.logMessageReceivedThreaded -= CaptureLog; SceneManager.sceneLoaded -= SceneLoaded; if (addressables != null) addressables.Dispose(); }
    }
}
#endif
