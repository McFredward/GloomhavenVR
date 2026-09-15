using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> Instances = new();
        public static T[] FindObjectsOfType<T>() => Instances.OfType<T>().ToArray();
        public static void Destroy(Object o) { }
        public static void DontDestroyOnLoad(Object o) { }
    }
    public class MonoBehaviour : Component { }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeSelf;
    }
    public class Transform : Component
    {
        public Vector3 position;
        public Vector3 localScale;
        public Transform(GameObject go) { gameObject = go; }
        public void SetParent(Transform parent, bool preserve) { }
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; }
    }
    public sealed class RectTransform : Transform
    {
        public RectTransform(GameObject go) : base(go) { }
        public Vector2 sizeDelta;
    }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public readonly Transform transform;
        public bool activeSelf = true;
        public Scene scene => new();
        public GameObject(string name, params Type[] types)
        {
            transform = Array.IndexOf(types, typeof(RectTransform)) >= 0 ? new RectTransform(this) : new Transform(this);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var result = new T { gameObject = this };
            Components.Add(result);
            Object.Instances.Add(result);
            return result;
        }
        public void SetActive(bool active) { activeSelf = active; }
    }
    public struct Scene { public bool isLoaded => true; }
    public sealed class Camera : Component { }
    public sealed class Canvas : Component { public RenderMode renderMode; public Camera? worldCamera; }
    public sealed class CanvasGroup : Component { }
    public enum RenderMode { WorldSpace }
    public enum RuntimePlatform { WindowsPlayer, OSXPlayer, Switch, GameCoreXboxOne }
    public static class Application { public static RuntimePlatform platform; public static string dataPath = "/game"; }
    public sealed class Texture { public int width = 1920; public int height = 1080; }
    public readonly record struct Vector2(float x, float y);
    public struct Vector3
    {
        public float x, y, z;
        public static Vector3 one => new() { x = 1, y = 1, z = 1 };
        public static Vector3 operator *(Vector3 a, float b) => new() { x = a.x * b, y = a.y * b, z = a.z * b };
    }
    public struct Quaternion { }
    public struct Color { public static Color white => new(); }
    public static class Mathf { public static int Max(int a, int b) => Math.Max(a, b); }
}
namespace UnityEngine.UI
{
    public sealed class UIWindow : UnityEngine.Component { }
    public sealed class GraphicRaycaster : UnityEngine.Component { }
    public sealed class RawImage : UnityEngine.Component
    {
        public UnityEngine.Texture texture = null!;
        public UnityEngine.Color color;
        public bool raycastTarget;
    }
}
namespace UnityEngine.Video
{
    public sealed class VideoPlayer : UnityEngine.Component
    {
        public event Action<VideoPlayer>? started;
        public void StartPlayback() => started?.Invoke(this);
        public bool isPlaying, isPaused, isPrepared;
        public long frame = -1;
        public UnityEngine.Texture texture = null!;
        public string url = string.Empty;
    }
}
namespace UnityEngine.EventSystems
{
    public sealed class PointerEventData { }
    public interface IPointerClickHandler { void OnPointerClick(PointerEventData data); }
}
public sealed class UIMapFTUEInitialStep : UnityEngine.Component
{
    private string introVideoPath = "CP_Intro/GH_CP_Intro";
    private ClickTrackerExtended clickTrackerExtended = null!;
    public void SetTracker(ClickTrackerExtended tracker, string path) { clickTrackerExtended = tracker; introVideoPath = path; }
    public string Diagnostic => introVideoPath + clickTrackerExtended.enabled;
    public int Escapes;
    public bool Escape() { Escapes++; return true; }
}
public sealed class ClickTrackerExtended : UnityEngine.Component { }
public sealed class VideoCamera : UnityEngine.Component
{
    public static VideoCamera? s_This;
    public UnityEngine.Camera m_Camera = null!;
    public UnityEngine.Video.VideoPlayer m_VideoPlayer = null!;
}
namespace GloomhavenVR.Core
{
    internal static class VRLayers { internal static void Apply(UnityEngine.GameObject go) { } }
    internal static class VRLog { internal static void Note(string a, string b) { } }
}
namespace GloomhavenVR.Hands.Interact
{
    internal interface IPanelGrabOwner { UnityEngine.Transform? GrabRoot { get; } }
    internal static class UguiPokeSurfaces
    {
        internal static readonly HashSet<UnityEngine.Canvas> Canvases = new();
        internal static void Register(UnityEngine.Canvas c) => Canvases.Add(c);
        internal static void Unregister(UnityEngine.Canvas c) => Canvases.Remove(c);
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class ConfigValue { internal float Value = 1; }
    internal static class WorldUIConfig { internal static bool ConversionActive = true; internal static ConfigValue CanvasScaleMm = new(); }
    internal static class CanvasConversion { internal static UnityEngine.Camera? WorldCamera; }
    internal static class PanelLayout { internal static float WorldScale => 1; }
    internal static class PanelPlacement
    {
        internal static void Spawn(UnityEngine.Camera head, float scale, out UnityEngine.Vector3 p, out UnityEngine.Quaternion q) { p = new(); q = new(); }
    }
    internal sealed class ConvertedPanel
    {
        public UnityEngine.RectTransform Target = null!, HostRect = null!;
        public UnityEngine.GameObject HostGo = null!;
        public UnityEngine.Canvas HostCanvas = null!;
        public UnityEngine.UI.GraphicRaycaster HostRaycaster = null!;
    }
    internal sealed class GrabbableModal : GloomhavenVR.Hands.Interact.IPanelGrabOwner
    {
        public UnityEngine.Transform? GrabRoot { get; } = new UnityEngine.GameObject("Frame").transform;
        internal static int LiveCount;
        internal void Build(ConvertedPanel p, float s, string n) { LiveCount++; }
        internal void Destroy() { LiveCount--; }
        internal void Tick() { }
        internal void SetExtraScale(float s) { }
        internal void SyncSharedState(UnityEngine.UI.UIWindow? w) { }
        internal void SnapFrameTo(UnityEngine.Vector3 p, UnityEngine.Quaternion q) { GrabRoot?.SetPositionAndRotation(p, q); }
    }
    internal static class SharedWindowSizeLaw
    {
        internal static float SharedGrabFactor(float factor) => factor;
        internal static float ExtraScale(UnityEngine.Vector2 size, float mm) => 1 / mm;
    }
}
