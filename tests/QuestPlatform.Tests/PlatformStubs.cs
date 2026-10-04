#nullable disable
global using Object = UnityEngine.Object;
using System.Collections;

// XR and platform seams only. Production CoreModule and all three bridge/bootstrap
// classes are compiled unchanged; this does not simulate rendering/native OpenXR.
namespace UnityEngine
{
    public enum RuntimePlatform { WindowsPlayer, Android }
    public enum HideFlags { HideAndDontSave }
    public static class Application
    {
        public static RuntimePlatform platform;
        public static string unityVersion = "fixture", dataPath = "fixture-data";
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color clear => new Color(0, 0, 0, 0);
    }
    public class Object
    {
        public static List<object> Destroyed = new();
        public static void DontDestroyOnLoad(object value) { }
        public static void Destroy(object value) { Destroyed.Add(value); }
    }
    public class ScriptableObject : Object
    {
        public static int Creations;
        public static T CreateInstance<T>() where T : new()
        {
            Creations++; T value = new T();
            if (value is XR.Management.XRGeneralSettings settings) XR.Management.XRGeneralSettings.Instance = settings;
            return value;
        }
    }
    public class GameObject : Object
    {
        public string name;
        public HideFlags hideFlags;
        public bool activeInHierarchy = true;
        public static List<Type> AddedComponents = new();
        public GameObject(string name) { this.name = name; }
        public T AddComponent<T>() where T : new() { AddedComponents.Add(typeof(T)); return new T(); }
    }
    public class Camera : Object
    {
        public bool enabled = true;
        public GameObject gameObject = new GameObject("fixture camera");
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public class MonoBehaviour : Object { public void StartCoroutine(IEnumerator value) { } }
    public interface ISubsystemDescriptor { string id { get; } }
    public static class SubsystemManager
    {
        public static List<XR.XRDisplaySubsystem> Displays = new();
        public static List<XR.XRInputSubsystem> Inputs = new();
        public static List<ISubsystemDescriptor> Descriptors = new();
        public static int DescriptorReads;
        public static void GetInstances<T>(List<T> values)
        {
            values.Clear();
            if (typeof(T) == typeof(XR.XRDisplaySubsystem)) values.AddRange(Displays.Cast<T>());
            else if (typeof(T) == typeof(XR.XRInputSubsystem)) values.AddRange(Inputs.Cast<T>());
        }
        public static void GetAllSubsystemDescriptors(List<ISubsystemDescriptor> values) { DescriptorReads++; values.AddRange(Descriptors); }
    }
    public static class SystemInfo
    {
        public static Rendering.GraphicsDeviceType graphicsDeviceType;
        public static string graphicsDeviceName = "fixture";
    }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeLoadType loadType;
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { loadType = type; }
    }
}
namespace UnityEngine.Rendering { public enum GraphicsDeviceType { Direct3D11, OpenGLES3 } }
namespace UnityEngine.XR
{
    public class XRDisplaySubsystem { public bool running; }
    public class XRInputSubsystem { public bool running; }
}
namespace UnityEngine.XR.Management
{
    public class XRLoader : UnityEngine.ScriptableObject { }
    public class XRManagerSettings : UnityEngine.ScriptableObject
    {
        public List<XRLoader> m_Loaders = new();
        public HashSet<XRLoader> registeredLoaders = new();
        public XRLoader activeLoader;
        public int Stops, Deinitializations, Starts;
        public void StopSubsystems() { Stops++; }
        public void DeinitializeLoader() { Deinitializations++; activeLoader = null; }
    }
    public class XRGeneralSettings : UnityEngine.ScriptableObject
    {
        public static XRGeneralSettings Instance;
        public XRManagerSettings Manager;
        public static int Initializations;
        public void InitXRSDK()
        {
            Initializations++; Manager.activeLoader = Manager.m_Loaders[0];
            UnityEngine.SubsystemManager.Displays.Add(new XR.XRDisplaySubsystem { running = false });
        }
        public void Start() { Manager.Starts++; }
    }
}
namespace UnityEngine.XR.OpenXR
{
    public class OpenXRLoader : Management.XRLoader { }
    public static class OpenXRRuntime
    {
        public static string name = "fixture runtime", version = "1", pluginVersion = "fixture";
    }
    public class OpenXRSettings
    {
        public enum RenderMode { MultiPass, SinglePassInstanced }
        public enum DepthSubmissionMode { None }
        public static OpenXRSettings Instance = new();
        public Features.OpenXRFeature[] features = Array.Empty<Features.OpenXRFeature>();
        public RenderMode renderMode;
        public DepthSubmissionMode depthSubmissionMode;
    }
}
namespace UnityEngine.XR.OpenXR.Features { public class OpenXRFeature : UnityEngine.ScriptableObject { public bool enabled; } }
namespace UnityEngine.XR.OpenXR.Features.Interactions
{
    public class OculusTouchControllerProfile : UnityEngine.XR.OpenXR.Features.OpenXRFeature { }
    public class ValveIndexControllerProfile : UnityEngine.XR.OpenXR.Features.OpenXRFeature { }
    public class KHRSimpleControllerProfile : UnityEngine.XR.OpenXR.Features.OpenXRFeature { }
}
namespace GloomhavenVR.Rig { public static class VRRigDriver { public static UnityEngine.Camera HeadCamera; } }
namespace GloomhavenVR.Core
{
    internal enum VRLogLevel { Debug }
    internal static class VRLayers { internal static int ModLayer => 27; }
    internal static class VRSession
    {
        internal static bool IsRunning;
        internal static string RuntimeName;
        internal static UnityEngine.MonoBehaviour CoroutineHost;
    }
    public class Entry<T> { public T Value; public Entry(T value) { Value = value; } }
    internal static class Plugin
    {
        internal static void Awake() { }
        internal static void OnDestroy() { }
        internal static Entry<string> RuntimeOverride = new("must-not-be-used-on-android"), RuntimePriority = new("fixture");
        internal static Entry<bool> SkipRuntimeCandidates = new(false);
    }
    internal static class VRLog
    {
        internal static bool WantsDebug;
        internal static bool Wants(VRLogLevel level) => WantsDebug;
        internal static List<string> Lines = new();
        internal static void Info(string area, string value) { Lines.Add("Info " + area + " " + value); }
        internal static void Warn(string area, string value) { Lines.Add("Warn " + area + " " + value); }
        internal static void Error(string area, string value) { Lines.Add("Error " + area + " " + value); }
        internal static void Debug(string area, string value) { Lines.Add("Debug " + area + " " + value); }
        internal static void Note(string area, string value) { Lines.Add("Note " + area + " " + value); }
    }
    internal static class OpenXRRuntimeRegistry
    {
        internal sealed class RuntimeEntry { internal string Name = "fixture", JsonPath; public override string ToString() => Name; }
        internal static int CandidatesRead;
        internal static RuntimeEntry SystemDefaultOnly() { CandidatesRead++; return new(); }
        internal static List<RuntimeEntry> GetCandidates(string path, string priority) { CandidatesRead++; return new() { new RuntimeEntry() }; }
    }
    internal static class OpenXRDiagnostics
    {
        internal static string ReportFilePath = "fixture.log";
        internal static List<string> Reports = new();
        internal static void AppendReportToFile(string line) { Reports.Add(line); }
        internal static bool TryGetActiveRuntimeName(out string name) { name = ""; return false; }
        internal static bool TryGetActiveRuntimeVersion(out ushort maj, out ushort min, out ushort pat) { maj = min = pat = 0; return false; }
    }
    internal static class StereoModeConfig { internal enum Mode { MultiPass, SinglePassInstanced } internal static Mode Current; }
    internal static class Loc { internal static int Starts, Stops; internal static void Init() { Starts++; } internal static void Dispose() { Stops++; } }
    internal static class ExceptionTraces { internal static int Starts, Stops; internal static void Install() { Starts++; } internal static void Shutdown() { Stops++; } }
    internal static class PerfMonitor { internal static int Starts, Stops; internal static void Install(UnityEngine.GameObject host) { Starts++; } internal static void Shutdown() { Stops++; } }
    internal static class AutoLod { internal static void Install(UnityEngine.GameObject host) { } internal static void Shutdown() { } }
    internal static class ScenarioSceneryBudget { internal static void Install(UnityEngine.GameObject host) { } internal static void Shutdown() { } }
    internal static class ScenarioGenerationDetail { internal static void Install() { } internal static void Shutdown() { } }
    internal static class ScenarioFigureDetailBudget { internal static void Install(UnityEngine.GameObject host) { } internal static void Shutdown() { } }
    internal static class ScenarioEnvironmentBudget { internal static void Install(UnityEngine.GameObject host) { } internal static void Shutdown() { } }
    public class VRHeartbeat { }
    public class VRPresenceWatch { }
}

namespace GloomhavenVR.WorldUI
{
    internal static class FlatScreen
    {
        internal static UnityEngine.Camera OwnedCamera;
        internal static bool OwnsVideoCapture(UnityEngine.Camera camera) => camera != null && camera == OwnedCamera;
    }
}
