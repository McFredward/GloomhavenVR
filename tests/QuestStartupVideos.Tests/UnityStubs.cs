#nullable disable
// Unity hierarchy/video seams only. The production movie router and production
// containment/manifest/file verification execute unchanged. This cannot establish
// Android codec, native callback ordering, stereo appearance or decoder throughput.
internal static class Fixture
{
    internal static readonly int MainThread = Environment.CurrentManagedThreadId;
    internal static readonly List<string> Logs = new();
    internal static int WorkerApiCalls;
    internal static void RequireMain(string api)
    {
        if (Environment.CurrentManagedThreadId == MainThread) return;
        Interlocked.Increment(ref WorkerApiCalls);
        throw new InvalidOperationException("worker-api: " + api);
    }
}
namespace UnityEngine
{
    public class Object { public string name = "Fixture"; }
    public class Texture : Object { public int width = 1920, height = 1080; }
    public class RenderTexture : Texture { public bool IsCreated() { Fixture.RequireMain("RenderTexture.IsCreated"); return true; } }
    public class Camera : Object
    {
        public bool isActiveAndEnabled = true;
        public int cullingMask = 32;
        public RenderTexture targetTexture = new() { name = "FixtureCameraTarget" };
    }
    public static class Time { public static float realtimeSinceStartup; }
    public class Transform
    {
        internal readonly GameObject Owner;
        internal Transform Parent;
        internal Transform(GameObject owner) { Owner = owner; }
        public GameObject gameObject { get { Fixture.RequireMain("Transform.gameObject"); return Owner; } }
        public Transform parent { get { Fixture.RequireMain("Transform.parent"); return Parent; } }
    }
    public class GameObject : Object
    {
        internal readonly List<GameObject> Children = new();
        internal Video.VideoPlayer Player;
        readonly string objectName;
        public bool activeSelf = true;
        public bool activeInHierarchy { get { Fixture.RequireMain("GameObject.activeInHierarchy"); return activeSelf && (objectTransform.Parent == null || objectTransform.Parent.Owner.activeInHierarchy); } }
        readonly Transform objectTransform;
        public GameObject(string name) { Fixture.RequireMain("GameObject constructor"); objectName = name; objectTransform = new(this); }
        public new string name { get { Fixture.RequireMain("GameObject.name"); return objectName; } }
        public Transform transform { get { Fixture.RequireMain("GameObject.transform"); return objectTransform; } }
        public GameObject Add(string name, bool active = true)
        {
            Fixture.RequireMain("GameObject.Add"); var child = new GameObject(name) { activeSelf = active };
            child.objectTransform.Parent = objectTransform; Children.Add(child); return child;
        }
        public Video.VideoPlayer AddPlayer() { Fixture.RequireMain("AddPlayer"); return Player = new(this); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class
        {
            Fixture.RequireMain("GetComponentsInChildren"); var values = new List<T>();
            void Visit(GameObject current)
            {
                if (!includeInactive && !current.activeSelf) return;
                if (current.Player is T value) values.Add(value);
                foreach (var child in current.Children) Visit(child);
            }
            Visit(this); return values.ToArray();
        }
    }
    public static class Debug
    {
        public static void Log(object message) { Fixture.RequireMain("Debug.Log"); Fixture.Logs.Add(message.ToString()); }
        public static void LogError(object message) { Fixture.RequireMain("Debug.LogError"); Fixture.Logs.Add("ERROR " + message); }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        readonly string sceneName;
        readonly UnityEngine.GameObject[] roots;
        public Scene(string name, params UnityEngine.GameObject[] roots) { sceneName = name; this.roots = roots; }
        public string name { get { Fixture.RequireMain("Scene.name"); return sceneName; } }
        public UnityEngine.GameObject[] GetRootGameObjects() { Fixture.RequireMain("Scene.GetRootGameObjects"); return roots; }
    }
}
namespace UnityEngine.Video
{
    public class VideoClip : UnityEngine.Object { }
    public enum VideoSource { VideoClip, Url }
    public sealed class VideoPlayer : UnityEngine.Object
    {
        readonly UnityEngine.GameObject owner;
        VideoClip originalClip = new();
        VideoSource originalSource = VideoSource.VideoClip;
        string originalUrl = "original://preserved-until-binding";
        public readonly List<string> Writes = new();
        public VideoPlayer(UnityEngine.GameObject owner) { this.owner = owner; }
        public UnityEngine.GameObject gameObject { get { Fixture.RequireMain("VideoPlayer.gameObject"); return owner; } }
        public UnityEngine.Transform transform { get { Fixture.RequireMain("VideoPlayer.transform"); return owner.transform; } }
        public VideoClip clip { get { Fixture.RequireMain("VideoPlayer.clip"); return originalClip; } set { Fixture.RequireMain("VideoPlayer.clip"); Writes.Add("clip"); originalClip = value; } }
        public VideoSource source { get { Fixture.RequireMain("VideoPlayer.source"); return originalSource; } set { Fixture.RequireMain("VideoPlayer.source"); Writes.Add("source"); originalSource = value; } }
        public string url { get { Fixture.RequireMain("VideoPlayer.url"); return originalUrl; } set { Fixture.RequireMain("VideoPlayer.url"); Writes.Add("url"); originalUrl = value; } }
        public bool playOnAwake = false, isLooping = true, waitForFirstFrame = false, skipOnDrop = false;
        public string renderMode = "CameraNearPlane", audioOutputMode = "AudioSource", timeReference = "ExternalTime";
        public readonly UnityEngine.RenderTexture targetTexture = new() { name = "FixtureVideoTarget" };
        public readonly UnityEngine.Camera targetCamera = new() { name = "FixtureVideoCamera" };
        public readonly object targetMaterialRenderer = new(), targetAudioSource = new();
        public bool isPrepared, isPlaying, isPaused;
        public bool isActiveAndEnabled => gameObject.activeInHierarchy;
        public float targetCameraAlpha = 1f;
        public string targetMaterialProperty = "_OriginalMovie", aspectRatio = "FitHorizontally";
        public double time = 12.75;
        public float playbackSpeed = 0.75f;
        public ushort controlledAudioTrackCount = 2;
        public uint width = 1920, height = 1080;
        public long frame = -1;
        public UnityEngine.Texture texture;
        public int PlayCalls, PrepareCalls, StopCalls;
        Action<VideoPlayer> prepared, began, loop;
        Action<VideoPlayer, string> errors;
        public event Action<VideoPlayer> prepareCompleted { add { Fixture.RequireMain("prepareCompleted.add"); prepared += value; } remove { Fixture.RequireMain("prepareCompleted.remove"); prepared -= value; } }
        public event Action<VideoPlayer> started { add { Fixture.RequireMain("started.add"); began += value; } remove { Fixture.RequireMain("started.remove"); began -= value; } }
        public event Action<VideoPlayer> loopPointReached { add { Fixture.RequireMain("loopPointReached.add"); loop += value; } remove { Fixture.RequireMain("loopPointReached.remove"); loop -= value; } }
        public event Action<VideoPlayer, string> errorReceived { add { Fixture.RequireMain("errorReceived.add"); errors += value; } remove { Fixture.RequireMain("errorReceived.remove"); errors -= value; } }
        public void Play() { Fixture.RequireMain("VideoPlayer.Play"); PlayCalls++; }
        public void Prepare() { Fixture.RequireMain("VideoPlayer.Prepare"); PrepareCalls++; }
        public void Stop() { Fixture.RequireMain("VideoPlayer.Stop"); StopCalls++; }
        public void EmitPrepared() => prepared?.Invoke(this);
        public void EmitStarted() => began?.Invoke(this);
        public void EmitError(string error) => errors?.Invoke(this, error);
        public void EmitCompleted() => loop?.Invoke(this);
        public int PreparedSubscribers => prepared?.GetInvocationList().Length ?? 0;
        public int StartedSubscribers => began?.GetInvocationList().Length ?? 0;
        public int ErrorSubscribers => errors?.GetInvocationList().Length ?? 0;
        public int CompletedSubscribers => loop?.GetInvocationList().Length ?? 0;
    }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool DebugLogging = true; }
}
