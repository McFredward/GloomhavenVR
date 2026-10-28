namespace UnityEngine
{
    public class GameObject { public bool activeInHierarchy = true; }
    public struct Vector3 { public float x; }
    public class Transform { public Vector3 lossyScale = new() { x = 22 }; public Vector3 position; }
    public class Camera { public Transform transform = new(); }
    public class AudioClip
    {
        public string name = "SFX_BruteMeleeAttack";
        public int channels = 4, frequency = 48000, samples = 48000;
        public string loadType = "DecompressOnLoad", loadState = "Loaded";
        public int Id;
        public int GetInstanceID() => Id;
    }
    public class AudioSource
    {
        public AudioClip clip = new();
        public Transform transform = new(); public string velocityUpdateMode = "Dynamic";
        public float pitch = 1.125f, volume = .75f, spatialBlend = 1, dopplerLevel = 1;
        public bool isPlaying = true, isVirtual = false, loop;
        public int timeSamples = 1234;
        public Mixer? outputAudioMixerGroup = new();
    }
    public class Mixer { public string name = "SFX"; }
    public static class AudioSettings
    {
        public static int outputSampleRate = 48000, Reads;
        public static double dspTime = 20;
        public static void GetDSPBufferSize(out int length, out int buffers) { Reads++; length = 1024; buffers = 4; }
    }
    public static class Time { public static int frameCount = 42; public static float unscaledDeltaTime = .05f; }
}
public static class AudioController
{
    public static int Reads;
    public static ClockStone.AudioObject? Music, Ambient;
    public static ClockStone.AudioObject? GetCurrentMusic() { Reads++; return Music; }
    public static ClockStone.AudioObject? GetCurrentAmbienceSound() { Reads++; return Ambient; }
}
namespace ClockStone
{
    public class AudioCategory { public string Name = "SFX"; }
    public class AudioItem { public string Name = ""; }
    public class AudioObject
    {
        public string audioID = ""; public AudioCategory? category = new(); public AudioItem? audioItem;
        public UnityEngine.AudioSource? Source = new();
        public int Reads;
        public bool Throw;
        public float originalPitch = 1.125f;
        public UnityEngine.AudioSource primaryAudioSource { get { Reads++; if (Throw) throw new System.InvalidOperationException(); return Source!; } }
    }
}
namespace HarmonyLib { [System.AttributeUsage(System.AttributeTargets.Class)] public class HarmonyPatch : System.Attribute { public HarmonyPatch(System.Type type, string method) { } } }
namespace GloomhavenVR.Core
{
    public static class QuestStandalonePlatform { public static bool Enabled = true; }
    internal static class QuestCombatAudio { internal static bool HasPrepared(UnityEngine.AudioSource source) => false; }
    internal static class QuestCombatAudioTap { internal static int Calls; internal static void Observe(UnityEngine.AudioSource source, UnityEngine.AudioClip clip, System.Action<string> append) { Calls++; } }
    public static class VRSession { public static bool IsRunning = true; }
    public enum VRLogLevel { Debug }
    public static class VRLog { public static bool Debug = true; public static bool Wants(VRLogLevel level) => Debug; }
}
namespace GloomhavenVR.Rig { public static class VRRigDriver { public static UnityEngine.Camera? HeadCamera = new(); } }
public class SceneController { public static SceneController? Instance = new(); public bool IsLoading, ScenarioIsLoading; }
public class RoomVisibilityManager { public static RoomVisibilityManager? s_Instance = new(); public UnityEngine.GameObject? Maps = new(); }
