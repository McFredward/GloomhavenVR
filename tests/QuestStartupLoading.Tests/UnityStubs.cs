#nullable disable
using System.Collections;
using System.Collections.Concurrent;
using System.Text.Json;

// Only Unity and downstream-component seams are modeled. The actual Bootstrap,
// archive delivery and content verification execute unchanged. The logger is
// a minimal seam that lets its progress callback hold a real Task worker; the
// separate startup-log fixture covers the actual durable sink and its bounds.
// This establishes coroutine/file ordering; Unity XR/native rendering and
// Android IL2CPP throughput still require a real player and headset evidence.
internal static class Fixture
{
    internal static int MainThread = Environment.CurrentManagedThreadId;
    internal static readonly ConcurrentQueue<string> Events = new();
    internal static Action<string> OnLog;
    internal static Action<string> OnDurableRecord;
    internal static string Root;
    internal static int MainUpdates, Activations, Addressables, Loads, WorkerApiCalls, ViewCreations, ViewRetargets, LastStartupPercent;
    internal static bool NativeHandover;
    internal static readonly List<int> Percentages = new();
    internal static bool ViewPrepared, ViewVisible, ModFailure, AddressablesFailure, SceneAvailable = true, HoldOriginalScene;
    internal static void RequireMain(string name)
    {
        if (Environment.CurrentManagedThreadId != MainThread)
        {
            Interlocked.Increment(ref WorkerApiCalls);
            throw new InvalidOperationException("worker-api: Unity " + name + " was called by a content worker");
        }
    }
    internal static void Event(string name) { Events.Enqueue(name); }
    internal static void Reset()
    {
        MainThread = Environment.CurrentManagedThreadId;
        while (Events.TryDequeue(out _)) { }
        OnLog = OnDurableRecord = null; MainUpdates = Activations = Addressables = Loads = WorkerApiCalls = 0;
        ViewPrepared = ViewVisible = ModFailure = AddressablesFailure = NativeHandover = false;
        ViewCreations = ViewRetargets = LastStartupPercent = 0; Percentages.Clear();
        SceneAvailable = true;
        HoldOriginalScene = false;
    }
}
namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
    public enum RuntimePlatform { Android, LinuxPlayer }
    public class MonoBehaviour
    {
        public GameObject gameObject = new();
        protected static void DontDestroyOnLoad(object value) { Fixture.RequireMain("DontDestroyOnLoad"); }
        protected T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
    }
    public class GameObject
    {
        readonly Dictionary<Type, object> components = new();
        public T AddComponent<T>() where T : new()
        {
            Fixture.RequireMain("AddComponent");
            T value = new T(); components[typeof(T)] = value; return value;
        }
        public T GetComponent<T>() where T : class
        {
            Fixture.RequireMain("GetComponent"); return components.TryGetValue(typeof(T), out object value) ? value as T : null;
        }
    }
    public class AsyncOperation { public bool isDone = true; }
    public static class Time
    {
        internal static float Clock;
        internal static int Frame;
        public static float unscaledTime { get { Fixture.RequireMain("Time.unscaledTime"); return Clock; } }
        public static float unscaledDeltaTime { get { Fixture.RequireMain("Time.unscaledDeltaTime"); return .016f; } }
        public static float realtimeSinceStartup { get { Fixture.RequireMain("Time.realtimeSinceStartup"); return Clock; } }
        public static int frameCount { get { Fixture.RequireMain("Time.frameCount"); return Frame; } }
    }
    public static class Application
    {
        internal static string Persistent, Streaming, Data;
        internal static RuntimePlatform Platform;
        internal static bool Editor;
        public static string persistentDataPath { get { Fixture.RequireMain("Application.persistentDataPath"); return Persistent; } }
        public static string streamingAssetsPath { get { Fixture.RequireMain("Application.streamingAssetsPath"); return Streaming; } }
        public static string dataPath { get { Fixture.RequireMain("Application.dataPath"); return Data; } }
        public static RuntimePlatform platform { get { Fixture.RequireMain("Application.platform"); return Platform; } }
        public static bool isEditor { get { Fixture.RequireMain("Application.isEditor"); return Editor; } }
        public static string unityVersion { get { Fixture.RequireMain("Application.unityVersion"); return "fixture"; } }
        public static event Action<string, string, LogType> logMessageReceivedThreaded;
        public static bool CanStreamedLevelBeLoaded(string name) { Fixture.RequireMain("CanStreamedLevelBeLoaded"); return Fixture.SceneAvailable; }
        internal static void Emit(string message, LogType type)
        {
            logMessageReceivedThreaded?.Invoke(message, "", type);
            Fixture.OnLog?.Invoke(message);
        }
    }
    public static class Debug
    {
        public static void Log(object value) { Fixture.RequireMain("Debug.Log"); Application.Emit(value?.ToString() ?? "", LogType.Log); }
        public static void LogWarning(object value) { Fixture.RequireMain("Debug.LogWarning"); Application.Emit(value?.ToString() ?? "", LogType.Warning); }
        public static void LogError(object value) { Fixture.RequireMain("Debug.LogError"); Application.Emit(value?.ToString() ?? "", LogType.Error); }
    }
    public class TextAsset { public string text; public TextAsset(string value) { text = value; } }
    public static class Resources
    {
        internal static readonly Dictionary<string, TextAsset> Assets = new();
        public static T Load<T>(string name) where T : class
        {
            Fixture.RequireMain("Resources.Load"); return Assets.TryGetValue(name, out TextAsset asset) ? asset as T : null;
        }
    }
    public static class JsonUtility
    {
        static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static T FromJson<T>(string text) { Fixture.RequireMain("JsonUtility.FromJson"); return JsonSerializer.Deserialize<T>(text, Options); }
        public static string ToJson(object value, bool pretty)
        {
            Fixture.RequireMain("JsonUtility.ToJson");
            return JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true, WriteIndented = pretty });
        }
    }
}
namespace UnityEngine.Scripting
{
    public static class GarbageCollector { public static bool isIncremental = true; }
}
namespace GloomhavenVR.Core
{
    public static class QuestStandalonePlatform { public static bool DebugLogging; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public enum LoadSceneMode { Single, Additive }
    public static class SceneManager
    {
        public static event Action<Scene, LoadSceneMode> sceneLoaded;
        static string pendingName;
        static LoadSceneMode pendingMode;
        static UnityEngine.AsyncOperation pendingOperation;
        public static UnityEngine.AsyncOperation LoadSceneAsync(string name, LoadSceneMode mode)
        {
            Fixture.RequireMain("LoadSceneAsync"); Fixture.Loads++; Fixture.Event("original-scene");
            if (Fixture.HoldOriginalScene)
            {
                pendingName = name; pendingMode = mode;
                return pendingOperation = new UnityEngine.AsyncOperation { isDone = false };
            }
            Fixture.NativeHandover = true;
            sceneLoaded?.Invoke(new Scene { name = name }, mode);
            return new UnityEngine.AsyncOperation();
        }
        public static void CompleteOriginalScene()
        {
            Fixture.RequireMain("CompleteOriginalScene");
            Fixture.NativeHandover = true;
            sceneLoaded?.Invoke(new Scene { name = pendingName }, pendingMode);
            pendingOperation.isDone = true;
        }
    }
}
namespace QuestGame.Compatibility
{
    public static class Paths
    {
        public static void Initialize(string root)
        {
            Fixture.RequireMain("OriginalPaths.Initialize");
            if (!Path.IsPathRooted(root)) throw new InvalidDataException("Original path root is not absolute.");
            Fixture.Event("original-paths");
        }
    }
}
namespace GloomhavenVR.Quest
{
    public sealed class QuestGameStartupLog
    {
        readonly string path;
        readonly object sync = new();
        public int OriginalErrors { get; private set; }
        public QuestGameStartupLog(string target, string banner) { path = target; Append("run " + banner, null); }
        public void Append(string message, string stack)
        {
            lock (sync) File.AppendAllText(path, message + "\n" + stack);
            Fixture.OnDurableRecord?.Invoke(message);
        }
        public void AppendOriginalError(string message, string stack) { OriginalErrors++; Append(message, stack); }
    }
    public class QuestGameScope { public static int DiscoveryScans, SceneComponentCount; public static double DiscoveryLastMs, DiscoveryWorstMs; }
    public class QuestGameModLifecycle
    {
        public bool Available, InitializationComplete, RigReady, InviteKeyboardVisible;
        public int CompletedModules;
        public string Stage = "pending", Failure;
        public bool StartupViewAvailable { get { Fixture.RequireMain("StartupViewAvailable"); return Fixture.ViewVisible; } }
        public void PrepareStartupView()
        {
            Fixture.RequireMain("PrepareStartupView"); Fixture.ViewPrepared = true; Fixture.Event("early-anchor");
        }
        public void Observe() { Fixture.RequireMain("ModLifecycle.Observe"); }
        public void BeginDeliveryView()
        {
            Fixture.RequireMain("BeginDeliveryView");
            if (!Fixture.ViewVisible) { Fixture.ViewCreations++; Fixture.ViewVisible = true; Fixture.Event("delivery-view"); }
        }
        public void EndDeliveryView() { Fixture.RequireMain("EndDeliveryView"); Fixture.ViewVisible = false; Fixture.Event("end-delivery-view"); }
        public void UpdateStartupView(string state, int percent)
        {
            Fixture.RequireMain("UpdateStartupView"); Fixture.MainUpdates++;
            if (percent < Fixture.LastStartupPercent || percent < 0 || percent > 100)
                throw new InvalidOperationException("single-progress: total must remain bounded and monotonic");
            if (percent == 100 && !Fixture.NativeHandover)
                throw new InvalidOperationException("handover-percent: only the observed native scene may complete startup");
            Fixture.LastStartupPercent = percent; Fixture.Percentages.Add(percent);
            if (state == "failed") BeginDeliveryView();
        }
        public IEnumerator Activate(string root)
        {
            Fixture.RequireMain("ModLifecycle.Activate"); Fixture.Activations++; Fixture.Event("real-mod");
            // Observe the durable boundary before the synchronous plugin seam,
            // without relying on a later periodic Unity Update to write it.
            using (var checkpoint = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture.Root, "quest-startup-state.json"))))
                if (checkpoint.RootElement.GetProperty("state").GetString() != "starting-real-mod"
                    || !checkpoint.RootElement.GetProperty("modContentReady").GetBoolean())
                    throw new InvalidOperationException("mod-checkpoint: durable state must identify verified content and imminent plugin activation");
            if (!File.Exists(Path.Combine(root, "StreamingAssets/gloomhavenvr.bundle")))
                throw new InvalidOperationException("mod-before-content: activation preceded verified bundle installation");
            if (File.Exists(Path.Combine(Fixture.Root, "quest-owned-game/StreamingAssets/original.rules")))
                Fixture.Event("original-already-warm");
            if (Fixture.ModFailure) { Failure = "fixture module failure"; Stage = "failed"; yield break; }
            yield return null;
            Available = InitializationComplete = RigReady = true; CompletedModules = 1; Stage = "real-mod-running";
            if (Fixture.ViewVisible) { Fixture.ViewRetargets++; Fixture.Event("retarget-same-view"); }
        }
    }
    public static class QuestPassthroughFeature { public static bool Active; }
    public class QuestGamePresentationEvidence { public void Observe(string scene) { } }
    public class QuestGameAudioEvidence { public void Observe(string scene) { } }
    public class QuestGameMovieManifest { }
    public class QuestGameVideos : IDisposable
    {
        public void Install(QuestGameMovieManifest movies, QuestGameContentManifest content, string root, string key) { }
        public void BindScene(UnityEngine.SceneManagement.Scene scene) { }
        public void Observe() { }
        public void Dispose() { }
    }
    public class QuestGameAddressablesManifest { public int schema; }
    public class QuestGameAddressables : IDisposable
    {
        public bool Ready; public Exception Failure;
        public IEnumerator Install(QuestGameAddressablesManifest value, QuestGameContentManifest manifest, string root, string key)
        {
            Fixture.RequireMain("Addressables.Install"); Fixture.Addressables++; Fixture.Event("addressables");
            if (!File.Exists(Path.Combine(root, "StreamingAssets/original.rules")))
                throw new InvalidOperationException("original-before-content: Addressables preceded content installation");
            yield return null;
            if (Fixture.AddressablesFailure) Failure = new InvalidDataException("fixture addressables failure");
            else Ready = true;
        }
        public void Dispose() { Fixture.RequireMain("Addressables.Dispose"); }
    }
}
