using System;
using System.Collections.Generic;
using System.Linq;

// Narrow API-boundary substitutes. Production files are compiled in full,
// unchanged. These do not model XR/GPU eye routing or native gameplay.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object? left, Object? right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null) || right.Destroyed;
            if (ReferenceEquals(right, null)) return left.Destroyed;
            return ReferenceEquals(left, right);
        }
        public static bool operator !=(Object? left, Object? right) => !(left == right);
        public override bool Equals(object? other) => other is Object value && this == value;
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value)
        {
            value.Destroyed = true;
            if (value is GameObject go)
            {
                foreach (var component in go.Components) component.Destroyed = true;
                foreach (var child in go.transform.Children.ToArray()) Destroy(child.gameObject);
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public static readonly List<GameObject> All = new();
        public readonly List<Component> Components = new();
        public string name;
        public Transform transform;
        public bool Active = true;
        public GameObject(string name, params Type[] types)
        {
            this.name = name;
            transform = types.Contains(typeof(RectTransform)) ? new RectTransform() : new Transform();
            transform.gameObject = this;
            Components.Add(transform);
            All.Add(this);
        }
        public void SetActive(bool active) => Active = active;
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            Components.Add(component);
            if (component is Video.VideoPlayer player) Video.VideoPlayer.All.Add(player);
            return component;
        }
    }
    public class Transform : Component
    {
        public readonly List<Transform> Children = new();
        public Vector3 position;
        public Vector3 forward = Vector3.forward;
        public Vector3 localScale = Vector3.one;
        public Quaternion rotation;
        public void SetParent(Transform parent, bool worldPositionStays) => parent.Children.Add(this);
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
        { this.position = position; this.rotation = rotation; }
    }
    public sealed class RectTransform : Transform
    { public Vector2 sizeDelta; public Vector2 anchoredPosition; }
    public struct Vector2
    { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 forward => new(0, 0, 1);
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 one => new(1, 1, 1);
        public float sqrMagnitude => x * x + y * y + z * z;
        public void Normalize() { float length = MathF.Sqrt(sqrMagnitude); x /= length; y /= length; z /= length; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
    }
    public struct Quaternion
    { public Vector3 Forward; public static Quaternion LookRotation(Vector3 forward, Vector3 up) => new() { Forward = forward }; }
    public struct Color { public static Color white => new(); }
    public class Texture : Object { public int width; public int height; }
    public class Camera : Component { }
    public static class Time { public static float unscaledTime; }
    public static class Mathf { public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f); }
    public class CanvasGroup : Component { public float alpha = 1f; }
    public enum RenderMode { WorldSpace }
    public class Canvas : Component
    { public RenderMode renderMode; public Camera? worldCamera; public int sortingOrder; }
}
namespace UnityEngine.UI
{
    public class GraphicRaycaster : UnityEngine.Component { }
    public class RawImage : UnityEngine.Component
    { public UnityEngine.Texture? texture; public UnityEngine.Color color; public bool raycastTarget; }
}
namespace UnityEngine.EventSystems
{
    public class PointerEventData { }
    public interface IPointerClickHandler { void OnPointerClick(PointerEventData data); }
}
namespace UnityEngine.Video
{
    public enum VideoRenderMode { APIOnly, CameraNearPlane }
    public enum VideoAudioOutputMode { None, Direct }
    public class VideoPlayer : UnityEngine.Component
    {
        public static readonly List<VideoPlayer> All = new();
        public bool playOnAwake = true, isLooping, isPrepared;
        public long frame = -1;
        public UnityEngine.Texture? texture;
        public string url = "";
        public VideoRenderMode renderMode = VideoRenderMode.CameraNearPlane;
        public VideoAudioOutputMode audioOutputMode = VideoAudioOutputMode.Direct;
        public int Prepares, Plays, Stops;
        public event Action<VideoPlayer>? prepareCompleted;
        public event Action<VideoPlayer, string>? errorReceived;
        public int PreparedSubscribers => prepareCompleted?.GetInvocationList().Length ?? 0;
        public int ErrorSubscribers => errorReceived?.GetInvocationList().Length ?? 0;
        public Action<VideoPlayer>? CapturePrepared() => prepareCompleted;
        public Action<VideoPlayer, string>? CaptureError() => errorReceived;
        public void EmitPrepared() => prepareCompleted?.Invoke(this);
        public void EmitError(string error) => errorReceived?.Invoke(this, error);
        public void Prepare() => Prepares++;
        public void Play() => Plays++;
        public void Stop() => Stops++;
    }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Center }
    public class TextMeshProUGUI : UnityEngine.Component
    { public float fontSize; public TextAlignmentOptions alignment; public bool raycastTarget; public string text = ""; }
}
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog
    {
        internal static readonly List<string> Lines = new();
        internal static void Alert(string area, string message) => Lines.Add(area + ":" + message);
        internal static void Note(string area, string message) => Lines.Add(area + ":" + message);
    }
    internal static class VRLayers { internal static int Applies; internal static void Apply(UnityEngine.GameObject go) => Applies++; }
    internal static partial class Loc { internal static string CurrentLanguage = "English"; }
}
public sealed class SceneController
{
    public static SceneController? Instance = new();
    public static int Reads;
    public string Scene = "MainMenu";
    public string GetCurrentSceneName { get { Reads++; return Scene; } }
}
public static class FFSNetwork { public static bool IsOnline; }
namespace GloomhavenVR.Hands.Interact
{
    internal static class UguiPokeSurfaces
    {
        internal static readonly HashSet<UnityEngine.Canvas> Canvases = new();
        internal static int Registrations, Removals;
        internal static void Register(UnityEngine.Canvas canvas) { Registrations++; Canvases.Add(canvas); }
        internal static void Unregister(UnityEngine.Canvas canvas) { Removals++; Canvases.Remove(canvas); }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class File
    {
        internal static int Calls;
        internal static bool Present = true;
        internal static string LastPath = "";
        internal static bool Exists(string path) { Calls++; LastPath = path; return Present; }
    }
    internal static class WorldUIConfig { internal static bool ConversionActive = true; }
    internal static class CanvasConversion { internal static UnityEngine.Camera? WorldCamera; }
    internal static class PanelLayout { internal static float WorldScale = 1f; }
    internal static class ModalFallback { internal const int ModalHostSortingOrder = 200; }
    internal sealed class StereoVideoSurface : IDisposable
    {
        internal static int AvailabilityChecks, Attachments, Disposals;
        internal static bool Available = true, AttachSucceeds = true;
        internal static bool IsAvailable(out string reason) { AvailabilityChecks++; reason = "fixture shader unavailable"; return Available; }
        internal static float PerEyeAspect(int width, int height) => width / (2f * height);
        internal static bool TryAttach(UnityEngine.UI.RawImage image, out StereoVideoSurface? surface, out string reason)
        {
            Attachments++; reason = "fixture attach failure";
            surface = AttachSucceeds ? new StereoVideoSurface() : null;
            return AttachSucceeds;
        }
        public void Dispose() => Disposals++;
    }
}
