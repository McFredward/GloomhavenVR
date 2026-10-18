namespace UnityEngine
{
    public class Transform { public Vector3 lossyScale = new() { x = 12.94f }; }
    public struct Vector3 { public float x; }
    public class Camera { public Transform transform = new(); }
    public class AudioClip { public string name = "ScoundrelStandardRangedAttack_Onhit1"; public int channels = 4, frequency = 48000; }
    public class Mixer { public string name = "3DSoundsReverb"; }
    public class AudioSource
    {
        public AudioClip? clip = new(); public Mixer? outputAudioMixerGroup = new();
        public float pitch = 1.125f, volume = .49f, spatialBlend = 1, dopplerLevel = 1, minDistance = 2, maxDistance = 80;
        public bool loop, muted; public int Starts; public float AtStartDoppler;
        public void PlayDelayed(float delay) { Starts++; AtStartDoppler = dopplerLevel; }
    }
}
namespace ClockStone { public class AudioObject { public UnityEngine.AudioSource? primaryAudioSource = new(), secondaryAudioSource; } }
namespace HarmonyLib
{
    [System.AttributeUsage(System.AttributeTargets.Class)] public class HarmonyPatch : System.Attribute { public HarmonyPatch(System.Type type, string method) { } }
}
namespace GloomhavenVR.Core
{
    internal static class QuestCombatAudioTap { internal static void PlaybackStarting(ClockStone.AudioObject owner) { } }
    internal static class QuestCombatAudioBank { internal static void Prepare(UnityEngine.AudioSource? source) { } internal static void Restore(UnityEngine.AudioSource? source) { } internal static void Inherit(UnityEngine.AudioSource? primary,UnityEngine.AudioSource? secondary) { } }
    public static class QuestStandalonePlatform { public static bool Enabled = true; }
    public static class VRSession { public static bool IsRunning = true; }
}
namespace GloomhavenVR.Rig { public static class VRRigDriver { public static UnityEngine.Camera? HeadCamera = new(); } }
