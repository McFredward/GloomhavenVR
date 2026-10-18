namespace UnityEngine
{
    public class GameObject { public bool activeInHierarchy = true; }
    public struct Vector3 { public float x; }
    public class Transform { public Vector3 lossyScale = new() { x = 22 }; }
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
        public float pitch = 1.125f, volume = .75f, spatialBlend = 1, dopplerLevel = 1;
        public bool isPlaying = true, isVirtual = false, loop;
        public Mixer? outputAudioMixerGroup = new();
    }
    public class Mixer { public string name = "SFX"; }
    public static class AudioSettings
    {
        public static int outputSampleRate = 48000, Reads;
        public static void GetDSPBufferSize(out int length, out int buffers) { Reads++; length = 1024; buffers = 4; }
    }
    public static class Time { public static int frameCount = 42; public static float unscaledDeltaTime = .05f; }
}
namespace ClockStone
{
    public class AudioObject
    {
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
    public static class VRSession { public static bool IsRunning = true; }
    public enum VRLogLevel { Debug }
    public static class VRLog { public static bool Debug = true; public static bool Wants(VRLogLevel level) => Debug; }
}
namespace GloomhavenVR.Rig { public static class VRRigDriver { public static UnityEngine.Camera? HeadCamera = new(); } }
public class SceneController { public static SceneController? Instance = new(); public bool IsLoading, ScenarioIsLoading; }
public class RoomVisibilityManager { public static RoomVisibilityManager? s_Instance = new(); public UnityEngine.GameObject? Maps = new(); }
