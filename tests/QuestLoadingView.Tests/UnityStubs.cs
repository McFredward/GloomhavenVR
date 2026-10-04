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
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        readonly Dictionary<Type, Component> components = new();
        public string name;
        public int layer;
        public bool activeInHierarchy = true;
        public Transform transform;
        public GameObject(string name, params Type[] types)
        {
            this.name = name;
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
        public Vector3 localPosition, localScale;
        public void SetParent(Transform owner, bool stays) { parent = owner; owner.children.Add(this); }
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, offsetMin, offsetMax, sizeDelta, anchoredPosition;
    }
    public class Camera : Component { public bool isActiveAndEnabled = true; }
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
    }
    public enum RuntimePlatform { Android, WindowsPlayer }
    public static class Application { public static RuntimePlatform platform = RuntimePlatform.WindowsPlayer; }
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
