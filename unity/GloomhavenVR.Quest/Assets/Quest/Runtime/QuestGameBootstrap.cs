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
        [Serializable] sealed class BuildStamp { public int schema, modBuild; public string inputKey; }
        [Serializable] sealed class StartupState
        {
            public int schema = 1, modBuild, originalErrors, loadedScenes;
            public string inputKey, state, failureDetail, lastScene, scope = "original-startup-diagnostic";
            public bool contentReady, addressablesReady, originalBootstrapStarted, modLifecycleAvailable, fullGameReady, eosAuthorised, proceduralRuntimeAvailable, voiceNativeAvailable, passthroughActive, focused, paused;
        }
        public string originalScene = "Bootstrap";
        public bool ContentReady { get; private set; }
        public bool OriginalBootstrapStarted { get; private set; }
        public bool ModLifecycleAvailable { get; private set; }
        public string State { get; private set; } = "pending";
        public string FailureDetail { get; private set; }
        string logPath;
        QuestGameStartupLog log;
        int failures;
        QuestGameAddressables addressables;
        BuildStamp build;
        string lastScene;
        int loadedScenes;
        float nextState;
        bool focused = true, paused, stateWriteWarning;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            logPath = Path.Combine(Application.persistentDataPath, "quest-startup.log");
            try { log = new QuestGameStartupLog(logPath, "Unity=" + Application.unityVersion + " platform=" + Application.platform + " scope=original-startup-diagnostic fullGameReady=false"); }
            catch (Exception e) { Debug.LogWarning("[Quest startup] diagnostic log initialization failed: " + e.Message); }
            Application.logMessageReceived += CaptureLog;
            SceneManager.sceneLoaded += SceneLoaded;
            // The first frame must have a tracked view and a visible gate/error status,
            // including manifest/extraction failures before original scenes can load.
            if (GetComponent<QuestGameMenu>() == null) gameObject.AddComponent<QuestGameMenu>();
            Debug.Log("[Quest startup] scope=original-startup-diagnostic Unity=" + Application.unityVersion + " platform=" + Application.platform + " fullGameReady=false");
        }

        IEnumerator Start()
        {
            var stampAsset = Resources.Load<TextAsset>("quest-build");
            var contentAsset = Resources.Load<TextAsset>("quest-startup-content");
            BuildStamp stamp = null; QuestGameContentManifest manifest = null;
            try
            {
                if (stampAsset == null || contentAsset == null) throw new InvalidDataException("Startup build/content manifests are missing.");
                stamp = JsonUtility.FromJson<BuildStamp>(stampAsset.text);
                build = stamp;
                manifest = JsonUtility.FromJson<QuestGameContentManifest>(contentAsset.text);
                if (stamp == null || stamp.schema != 1) throw new InvalidDataException("Startup build stamp is invalid.");
                QuestGameContent.Validate(manifest, stamp.inputKey);
                Debug.Log("[Quest startup] ModBuild=" + stamp.modBuild + " input=" + stamp.inputKey + " contentFiles=" + manifest.files.Length);
            }
            catch (Exception e) { Fail("manifest", e); }
            if (State == "failed") yield break;
            string root = Path.Combine(Application.persistentDataPath, "quest-owned-game");
            State = "checking-content";
            Task<bool> ready = Task.Run(() => QuestGameContent.IsReady(manifest, root));
            while (!ready.IsCompleted) yield return null;
            if (ready.IsFaulted) { Fail("content-check", ready.Exception.GetBaseException()); yield break; }
            if (!ready.Result)
            {
                State = "extracting-content";
                string archive = Path.Combine(Application.persistentDataPath, "quest-startup-content.download");
                string uri = Application.streamingAssetsPath.TrimEnd('/') + "/" + manifest.archive;
                using (UnityWebRequest request = UnityWebRequest.Get(uri))
                {
                    request.downloadHandler = new DownloadHandlerFile(archive) { removeFileOnAbort = true };
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success) { Fail("archive-download", new IOException(request.error)); yield break; }
                }
                Task extraction = Task.Run(() => QuestGameContent.Extract(manifest, archive, root));
                while (!extraction.IsCompleted) yield return null;
                if (File.Exists(archive)) File.Delete(archive);
                if (extraction.IsFaulted) { Fail("content-extract", extraction.Exception.GetBaseException()); yield break; }
            }
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
            // The original mod is not created until its separate lifecycle/XR/content gates
            // are verified. A recovered menu never establishes campaign or mod readiness.
            ModLifecycleAvailable = false;
            Debug.LogWarning("[Quest startup] modLifecycleAvailable=false; standalone BepInEx/XR integration remains gated. EOSAuthorised=false; crossplay unverified. proceduralRuntimeAvailable=false; native Apparance lifecycle disabled for menu-only target, campaign generation remains gated.");
            if (!Application.CanStreamedLevelBeLoaded(originalScene)) { Fail("original-scene", new InvalidDataException("Required original scene is absent: " + originalScene)); yield break; }
            State = "loading-original-bootstrap";
            AsyncOperation loading = SceneManager.LoadSceneAsync(originalScene, LoadSceneMode.Single);
            if (loading == null) { Fail("original-scene", new InvalidOperationException("Unity refused original scene load.")); yield break; }
            yield return loading;
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
        void Update() { if (Time.unscaledTime >= nextState) { nextState = Time.unscaledTime + 5; SaveState(); } }
        void OnApplicationPause(bool value) { paused = value; SaveState(); }
        void OnApplicationFocus(bool value) { focused = value; SaveState(); }
        void SaveState()
        {
            if (logPath == null) return;
            var state = new StartupState { modBuild = build != null ? build.modBuild : 0, inputKey = build != null ? build.inputKey : null, state = State, failureDetail = FailureDetail,
                contentReady = ContentReady, addressablesReady = addressables != null && addressables.Ready, originalBootstrapStarted = OriginalBootstrapStarted,
                modLifecycleAvailable = ModLifecycleAvailable, fullGameReady = false, eosAuthorised = false, originalErrors = Math.Min(failures, 24),
                passthroughActive = QuestPassthroughFeature.Active,
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
                if (message.StartsWith("[Quest startup]", StringComparison.Ordinal)) { if (log != null) log.Append(message); }
                else if (type == LogType.Exception || type == LogType.Error)
                {
                    if (failures < 24) { failures++; if (log != null) log.Append("original " + type + " " + message, stack); }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        void OnDestroy() { Application.logMessageReceived -= CaptureLog; SceneManager.sceneLoaded -= SceneLoaded; if (addressables != null) addressables.Dispose(); }
    }
}
#endif
