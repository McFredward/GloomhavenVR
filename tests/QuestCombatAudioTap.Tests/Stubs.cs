using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object
    {
        public static int Destroyed;
        public static void Destroy(Object value) { Destroyed++; if (value is GameObject go) go.activeInHierarchy = false; }
        public static void DontDestroyOnLoad(Object value) { }
    }
    public class GameObject : Object
    {
        public static readonly List<MonoBehaviour> Components = new();
        public static int Created;
        public bool activeInHierarchy = true;
        public GameObject(string name = "") { Created++; }
        public T AddComponent<T>() where T : MonoBehaviour
        { var component = (T)Activator.CreateInstance(typeof(T), true)!; component.gameObject = this; Components.Add(component); return component; }
    }
    public class MonoBehaviour : Object { public GameObject gameObject = null!; }
    public enum AudioDataLoadState { Unloaded, Loading, Loaded, Failed }
    public enum AudioClipLoadType { DecompressOnLoad, CompressedInMemory, Streaming }
    public enum AudioSpeakerMode { Mono, Stereo, Quad }
    public class AudioClip : Object
    {
        public AudioDataLoadState loadState = AudioDataLoadState.Loaded;
        public AudioClipLoadType loadType = AudioClipLoadType.DecompressOnLoad;
        public int channels = 4, samples = 100000, Reads;
        public bool Throw;
        public bool GetData(float[] target, int offset)
        { Reads++; if (Throw) throw new InvalidOperationException(); for (int i = 0; i < target.Length; i++) target[i] = .25f; return true; }
    }
    public class AudioSource : Object
    {
        public AudioClip? clip;
        public GameObject gameObject = new("original");
        public int timeSamples, Reads;
        public bool isPlaying = true, isVirtual;
        public float pitch = 1.2f, volume = .4f, dopplerLevel = 0, spatialBlend = 1;
        public void GetOutputData(float[] target, int channel)
        { if (clip == null || channel >= clip.channels) throw new ArgumentOutOfRangeException(); Reads++; for (int i = 0; i < target.Length; i++) target[i] = .5f; }
    }
    public static class AudioListener
    {
        public static int Reads;
        public static void GetOutputData(float[] target, int channel) { Reads++; for (int i = 0; i < target.Length; i++) target[i] = .75f; }
    }
    public static class Time { public static float realtimeSinceStartup; public static int frameCount; }
    public static class AudioSettings
    {
        public static double dspTime;
        public static int outputSampleRate = 48000;
        public struct Configuration { public AudioSpeakerMode speakerMode; }
        public static AudioSpeakerMode speakerMode = AudioSpeakerMode.Stereo;
        public static Configuration GetConfiguration() => new() { speakerMode = speakerMode };
        public static void GetDSPBufferSize(out int samples, out int count) { samples = 1024; count = 4; }
    }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled = true; }
    internal static class VRSession { internal static bool IsRunning = true; internal static bool? InputFocus = true; }
    internal enum VRLogLevel { Debug }
    internal static class VRLog { internal static bool Debug = true; internal static bool Wants(VRLogLevel value) => Debug; }
}
public class SceneController { public static SceneController? Instance = new(); public bool IsLoading, ScenarioIsLoading; }

namespace ClockStone { public class AudioObject { public UnityEngine.AudioSource primaryAudioSource = null!; } }
