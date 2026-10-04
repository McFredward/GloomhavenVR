#nullable disable
using System.Collections;

// Only the event/filesystem integration seam is modeled here. This fixture
// does not establish Unity scene, XR, IL2CPP or headset rendering behavior.
namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
    public enum RuntimePlatform { Android, LinuxEditor }
    public class MonoBehaviour
    {
        protected static void DontDestroyOnLoad(object value) { }
        public GameObject gameObject = new GameObject();
        protected T GetComponent<T>() where T : class { return null; }
    }
    public class GameObject { public T AddComponent<T>() where T : new() { return new T(); } }
    public class AsyncOperation { }
    public static class Time { public static float unscaledTime; }
    public static class Application
    {
        public static string persistentDataPath, streamingAssetsPath, dataPath;
        public static string unityVersion = "fixture";
        public static RuntimePlatform platform = RuntimePlatform.LinuxEditor;
        public static bool isEditor = true;
        public static event Action<string, string, LogType> logMessageReceived;
        public static event Action<string, string, LogType> logMessageReceivedThreaded;
        public static bool CanStreamedLevelBeLoaded(string name) { return true; }
        public static void Emit(string message, string stack, LogType type, bool mainThread = true)
        {
            logMessageReceivedThreaded?.Invoke(message, stack, type);
            if (mainThread) logMessageReceived?.Invoke(message, stack, type);
        }
    }
    public static class Debug
    {
        public static void Log(string value) { Application.Emit(value, "", LogType.Log); }
        public static void LogWarning(string value) { Application.Emit(value, "", LogType.Warning); }
        public static void LogError(string value) { Application.Emit(value, "", LogType.Error); }
    }
    public class TextAsset { public string text; }
    public static class Resources { public static T Load<T>(string name) where T : class { return null; } }
    public static class JsonUtility
    {
        public static T FromJson<T>(string text) { return default(T); }
        public static string ToJson(object value, bool pretty)
        {
            return System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { IncludeFields = true, WriteIndented = pretty });
        }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public enum LoadSceneMode { Single, Additive }
    public static class SceneManager
    {
        public static event Action<Scene, LoadSceneMode> sceneLoaded;
        public static UnityEngine.AsyncOperation LoadSceneAsync(string name, LoadSceneMode mode) { return null; }
    }
}
namespace UnityEngine.Networking
{
    public class DownloadHandler { }
    public class DownloadHandlerFile : DownloadHandler
    {
        public bool removeFileOnAbort;
        public DownloadHandlerFile(string path) { }
    }
    public class UnityWebRequest : IDisposable
    {
        public enum Result { Success }
        public DownloadHandler downloadHandler;
        public string error;
        public Result result;
        public static UnityWebRequest Get(string uri) { return new UnityWebRequest(); }
        public object SendWebRequest() { return null; }
        public void Dispose() { }
    }
}
namespace GloomhavenVR.Quest
{
    public class QuestGameMenu { }
    public class QuestGameKeyboard { public bool Bound, Visible; public string Failure; }
    // These are lifecycle seams for the logging fixture, not a real mod execution proof.
    public class QuestGameScope { }
    public class QuestGameModLifecycle
    {
        public bool Available, InitializationComplete, RigReady, InviteKeyboardVisible;
        public int CompletedModules;
        public string Stage, Failure;
        public bool StartupViewAvailable;
        public void PrepareStartupView() { StartupViewAvailable = true; }
        public void UpdateStartupView(string state, QuestGameContentProgress progress) { }
        public void BeginDeliveryView() { }
        public void EndDeliveryView() { }
        public IEnumerator Activate(string root) { yield break; }
        public void Observe() { }
    }
    public static class QuestPassthroughFeature { public static bool Active; }
    public class QuestGameContentFile { public string path; }
    public class QuestGameContentManifest { public string archive; public QuestGameContentFile[] files; }
    public class QuestGameContentProgress { public string Phase, File; public long ProcessedBytes, TotalBytes; }
    public static class QuestGameArchiveDelivery
    {
        public static void Stage(QuestGameContentManifest manifest, string source, string destination, bool sourceIsApk, Action<QuestGameContentProgress> progress = null) { }
    }
    public static class QuestGameContent
    {
        public static void Validate(QuestGameContentManifest manifest, string key, string expectedArchive = "quest-startup-content.zip") { }
        public static bool IsReady(QuestGameContentManifest manifest, string root, Action<QuestGameContentProgress> progress = null) { return true; }
        public static void Extract(QuestGameContentManifest manifest, string archive, string root, string expectedArchive = "quest-startup-content.zip", Action<QuestGameContentProgress> progress = null) { }
        public static string ResolveVerifiedPath(QuestGameContentManifest manifest, string root, string relative) { return relative; }
    }
    public class QuestGamePresentationEvidence { public void Observe(string scene) { } }
    public class QuestGameMovieManifest { }
    public class QuestGameVideos : IDisposable
    {
        public void Install(QuestGameMovieManifest movies, QuestGameContentManifest content, string root, string key) { }
        public void BindScene(UnityEngine.SceneManagement.Scene scene) { }
        public void Observe() { }
        public void Dispose() { }
    }
    public class QuestGameAddressablesManifest { }
    public class QuestGameAddressables : IDisposable
    {
        public bool Ready; public Exception Failure;
        public IEnumerator Install(QuestGameAddressablesManifest value, QuestGameContentManifest manifest, string root, string key) { yield break; }
        public void Dispose() { }
    }
}

namespace QuestGame.Compatibility
{
    public static class Paths { public static void Initialize(string root) { } }
}
