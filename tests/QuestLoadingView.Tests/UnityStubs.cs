#nullable disable
using System.Runtime.CompilerServices;

// Only Unity object/UI ownership seams are modeled. Actual production view,
// percentage/filename logic and standalone policy execute unchanged. Native
// layout, import/shader quality and headset appearance need separate evidence.
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    public class Object
    {
        public bool Alive = true;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b)
            || ((ReferenceEquals(a, null) || !a.Alive) && (ReferenceEquals(b, null) || !b.Alive));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value) { if (!ReferenceEquals(value, null)) value.Alive = false; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public bool CompareTag(string tag) => gameObject.tag == tag;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public static readonly List<GameObject> All = new();
        readonly Dictionary<Type, Component> components = new();
        public string name, tag;
        public UnityEngine.SceneManagement.Scene scene = new();
        public int layer;
        public bool activeInHierarchy = true;
        public Transform transform;
        public GameObject(string name, params Type[] types)
        {
            this.name = name; All.Add(this);
            transform = (Transform)Add(types.Contains(typeof(RectTransform)) ? typeof(RectTransform) : typeof(Transform));
            foreach (Type type in types) if (type != transform.GetType()) Add(type);
        }
        Component Add(Type type)
        {
            var value = (Component)Activator.CreateInstance(type); value.gameObject = this;
            components[type] = value; return value;
        }
        public T AddComponent<T>() where T : Component => (T)Add(typeof(T));
        public T GetComponent<T>() where T : Component => components.Values.OfType<T>().FirstOrDefault();
        public void SetActive(bool value) { activeInHierarchy = value; }
    }
    public class Transform : Component
    {
        public readonly List<Transform> children = new();
        public Transform parent;
        public Vector3 localPosition, localScale, position;
        public int childCount => children.Count;
        public Transform GetChild(int index) => children[index];
        public void SetParent(Transform owner, bool stays) { parent?.children.Remove(this); parent = owner; owner.children.Add(this); }
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, offsetMin, offsetMax, sizeDelta, anchoredPosition;
    }
    public enum CameraClearFlags { SolidColor }
    public enum StereoTargetEyeMask { None, Both }
    public class Camera : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => Alive && enabled && gameObject.activeInHierarchy;
        public string tag { get => gameObject.tag; set => gameObject.tag = value; }
        public int cullingMask;
        public CameraClearFlags clearFlags;
        public Color backgroundColor;
        public StereoTargetEyeMask stereoTargetEye;
        public float depth;
        public Texture targetTexture;
    }
    public class Texture : Object { }
    public class Texture2D : Texture { public int width = 1024, height = 179; }
    public class Font : Object { }
    public class Canvas : Component { public Camera worldCamera; public RenderMode renderMode; }
    public enum RenderMode { WorldSpace }
    public enum TextAnchor { MiddleCenter }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 operator *(Vector3 v, float f) => new(v.x * f, v.y * f, v.z * f);
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color clear => new(0, 0, 0, 0);
    }
    public static class Resources
    {
        public static readonly Texture2D Logo = new();
        public static readonly Font Font = new();
        public static T Load<T>(string name) where T : Object => Logo as T;
        public static T GetBuiltinResource<T>(string name) where T : Object => Font as T;
        public static T[] FindObjectsOfTypeAll<T>() where T : Component => GameObject.All.Select(g => g.GetComponent<T>()).Where(c => c != null).ToArray();
    }
    public enum RuntimePlatform { Android, WindowsPlayer }
    public enum SystemLanguage { English, German }
    public static class Application
    {
        public static RuntimePlatform platform = RuntimePlatform.WindowsPlayer;
        public static SystemLanguage systemLanguage;
        public static string persistentDataPath;
    }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Debug
    {
        public static readonly List<string> Logs = new();
        public static void Log(object value) => Logs.Add(value.ToString());
        public static void LogError(object value) => Logs.Add(value.ToString());
    }
}
namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Component { public bool raycastTarget = true; public UnityEngine.Color color; }
    public class Image : Graphic { }
    public class RawImage : Graphic { public UnityEngine.Texture texture; }
    public class Text : Graphic
    {
        public UnityEngine.Font font;
        public int fontSize;
        public UnityEngine.TextAnchor alignment;
        public bool supportRichText = true;
        public string text;
    }
}
namespace GloomhavenVR.Rig
{
    public static class VRRigDriver { public static UnityEngine.Camera HeadCamera; }
}
namespace GloomhavenVR.Core
{
    public enum VRLogLevel { Debug }
    public static class VRLog
    {
        public static bool Wants(VRLogLevel value) => false;
        public static void Warn(string owner, string text) { }
        public static void Error(string owner, string text) { }
    }
    public static class VRSession { public static bool IsRunning; }
    public static class VRLayers { public static int ModLayer = 27; }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public bool IsValid() => true; public bool isLoaded => true; }
}
namespace UnityEngine.XR
{
    public class XRDisplaySubsystem { public bool running = true; }
    public class XRInputSubsystem { public bool running = true; }
}
namespace UnityEngine.XR.Management
{
    public class XRLoader
    {
        public T GetLoadedSubsystem<T>() where T : new() => new T();
    }
    public class XRManagerSettings { public XRLoader activeLoader = new(); }
    public class XRGeneralSettings { public static XRGeneralSettings Instance = new(); public XRManagerSettings Manager = new(); }
}
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class BepInPlugin : Attribute { public string GUID => "fixture.plugin"; }
}
namespace GloomhavenVR
{
    [BepInEx.BepInPlugin] public class Plugin : UnityEngine.Component
    {
        public Plugin() { Core.VRSession.IsRunning = true; }
    }
}
namespace GloomhavenVR.Core
{
    public static class QuestStandaloneModuleHealth
    {
        public static bool InitializationComplete = true, InviteKeyboardVisible;
        public static int CompletedModules = 1;
        public static string Failure;
        public static void InstallUnityLogListener() { }
    }
}
namespace GloomhavenVR.Quest
{
    public class QuestGameBootstrap : UnityEngine.Component { internal void SaveState() { } }
    public static class QuestPassthroughFeature
    {
        public static bool Active;
        public static long SessionGeneration;
        public static bool SetEnabled(bool requested) => Active = requested;
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class FlatScreen
    {
        internal static bool OwnsVideoCapture(UnityEngine.Camera camera) => false;
    }
}
