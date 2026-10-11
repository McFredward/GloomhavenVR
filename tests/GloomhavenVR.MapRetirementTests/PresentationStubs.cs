using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public class Object { public static void Destroy(GameObject value) { value.SetActive(false); value.Destroyed = true; value.transform.SetParent(null, false); } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
    }
    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public class GameObject : Object
    {
        public readonly string name;
        public bool activeSelf = true, Destroyed;
        public bool activeInHierarchy => activeSelf && !Destroyed && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public int layer;
        public Transform transform;
        public readonly List<Component> Components = new();
        public GameObject(string name, params Type[] types)
        {
            this.name = name;
            transform = new RectTransform { gameObject = this };
            Components.Add(transform);
            foreach (Type type in types) if (type != typeof(RectTransform)) AddComponent(type);
        }
        private Component AddComponent(Type type)
        { var value = (Component)Activator.CreateInstance(type)!; value.gameObject = this; Components.Add(value); return value; }
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public T? GetComponent<T>() where T : Component => Components.Find(x => x is T) as T;
        public void SetActive(bool value)
        {
            if (activeSelf == value) return;
            activeSelf = value;
            foreach (Component component in Components)
                component.GetType().GetMethod(value ? "OnEnable" : "OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(component, null);
        }
    }
    public class Transform : Component
    {
        public Transform? parent;
        public Vector3 localScale = Vector3.one, localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public bool RefuseNextDetach;
        public readonly List<Transform> Children = new();
        public void SetParent(Transform? value, bool worldPositionStays) { if (RefuseNextDetach) { RefuseNextDetach = false; return; } parent?.Children.Remove(this); parent = value; parent?.Children.Add(this); }
        public bool IsChildOf(Transform other) { for (Transform? at = this; at != null; at = at.parent) if (ReferenceEquals(at, other)) return true; return false; }
        public int GetSiblingIndex() => parent?.Children.IndexOf(this) ?? 0;
        public void SetSiblingIndex(int index) { if (parent == null) return; parent.Children.Remove(this); parent.Children.Insert(Math.Min(index, parent.Children.Count), this); }
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { }
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, offsetMin, offsetMax, pivot = new(.5f, .5f), sizeDelta = new(300, 80);
        public Vector3 anchoredPosition3D { get => localPosition; set => localPosition = value; }
        public Rect rect => new(sizeDelta);
    }
    public readonly struct Rect { public readonly Vector2 size; public Rect(Vector2 size) => this.size = size; public float height => size.y; }
    public readonly struct Vector2
    {
        public readonly float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(); public static Vector2 one => new(1, 1);
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new(); public static Vector3 one => new(1, 1, 1);
        public static Vector3 operator *(Vector3 value, float factor) => new(value.x * factor, value.y * factor, value.z * factor);
    }
    public readonly struct Quaternion
    {
        private readonly float _angle;
        public Quaternion(float angle) => _angle = angle;
        public static Quaternion identity => new(); public Vector3 eulerAngles => new(0, 0, _angle);
        public static float Angle(Quaternion a, Quaternion b) => Math.Abs(a._angle - b._angle);
    }
    public static class Mathf { public static float Abs(float value) => Math.Abs(value); }
    public readonly struct Color { public static Color clear => new(); }
    public sealed class Camera : Object { }
    public sealed class Sprite : Object { }
    public static class Time { public static int frameCount; }
    public static class Debug { public static void Log(string text) { } }
}
namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T>(T value);
    public delegate void UnityAction<T, U>(T value, U other);
    public sealed class UnityEvent
    {
        private readonly List<UnityAction> _listeners = new();
        public int Count => _listeners.Count;
        public void AddListener(UnityAction callback) => _listeners.Add(callback);
        public void RemoveListener(UnityAction callback) => _listeners.RemoveAll(x => x == callback);
        public void Invoke() { foreach (UnityAction callback in _listeners.ToArray()) callback(); }
    }
}
namespace UnityEngine.UI
{
    public class Image : UnityEngine.MonoBehaviour { public UnityEngine.Sprite? sprite; public UnityEngine.Color color; public bool raycastTarget; }
}
namespace UnityEngine.EventSystems
{
    public sealed class PointerEventData { }
    public interface IPointerClickHandler { void OnPointerClick(PointerEventData eventData); }
}
namespace TMPro { public sealed class TMP_Text : UnityEngine.MonoBehaviour { public string text = ""; } }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { }
    public sealed class Harmony { public readonly List<Type> Installed = new(); public void PatchAll(Type value) => Installed.Add(value); }
    public static class AccessTools
    {
        public static FieldInfo? Field(Type type, string name) => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
    }
}
namespace GloomhavenVR.Core
{
    public static class VRSession { public static HarmonyLib.Harmony? Harmony = new(); }
    public static class VRLog
    {
        public static int Warnings, Notes, Alerts;
        public static bool DebugEnabled = true, ThrowWarnings, ThrowNotes, ThrowAlerts;
        public static void Warn(string area, string text) { if (!DebugEnabled) return; Warnings++; if (ThrowWarnings) throw new InvalidOperationException("Diagnostic sink failed"); }
        public static void Note(string area, string text) { Notes++; if (ThrowNotes) throw new InvalidOperationException("Diagnostic sink failed"); }
        public static void Alert(string area, string text) { Alerts++; if (ThrowAlerts) throw new InvalidOperationException("Diagnostic sink failed"); }
    }
}
namespace GloomhavenVR.Net.Desync
{
    public static class DispatchGuard { public static void Run(string context, Action callback) => callback(); }
}
namespace GloomhavenVR.WorldUI
{
    public static class WorldUIConfig { public static bool ConversionActive = true; public static readonly FloatValue CanvasScaleMm = new(); }
    public sealed class FloatValue { public float Value = 1; }
    public static class FlatScreen { public static bool ManualScreenActive; }
    public static class PanelLayout { public static float WorldScale = 1; }
    public static class SharedWindowSizeLaw { public static float ExtraScale(UnityEngine.Vector2 value, float scale) => scale; }
    public static class PanelPlacement
    { public static void Spawn(UnityEngine.Camera camera, float scale, out UnityEngine.Vector3 position, out UnityEngine.Quaternion rotation) { position = new(); rotation = new(); } }
    public sealed class ConvertedPanel
    {
        public UnityEngine.RectTransform Target = null!, HostRect = null!;
        public UnityEngine.Transform? OriginalParent;
        public int OriginalLayer;
        public readonly Dictionary<UnityEngine.Transform, int> OriginalLayers = new();
        public UnityEngine.GameObject HostGo = null!;
        public bool IsAlive => !HostGo.Destroyed;
        public bool FitHeightCapped, FrameDriftLogged;
        public string FitHeightCapName = "";
    }
    // Conversion is the established presentation dependency, not the defect being tested.
    // Its stub reparents the exact native object and restores it, without lifecycle calls.
    public static partial class CanvasConversion
    {
        public static UnityEngine.Camera? WorldCamera = new();
        public static int Converts, Releases;
        public static bool ThrowBeforeConvert;
        public static ConvertedPanel? Last;
        public static ConvertedPanel Convert(UnityEngine.RectTransform target, string name, bool fitContent, bool useModLayer)
        {
            if (ThrowBeforeConvert) throw new InvalidOperationException();
            Converts++;
            var host = new UnityEngine.GameObject(name, typeof(UnityEngine.RectTransform));
            var panel = new ConvertedPanel { Target = target, OriginalParent = target.parent, OriginalLayer = target.gameObject.layer, HostGo = host, HostRect = (UnityEngine.RectTransform)host.transform };
            target.SetParent(panel.HostRect, false);
            target.localScale = UnityEngine.Vector3.one; target.localRotation = UnityEngine.Quaternion.identity;
            if (useModLayer) ApplyLayer(panel, target);
            return Last = panel;
        }
        public static void Release(ConvertedPanel panel)
        { Releases++; panel.Target.SetParent(panel.OriginalParent, false); foreach (var layer in panel.OriginalLayers) layer.Key.gameObject.layer = layer.Value; UnityEngine.Object.Destroy(panel.HostGo); }
        private static void ApplyLayer(ConvertedPanel panel, UnityEngine.Transform target)
        { panel.OriginalLayers[target] = target.gameObject.layer; target.gameObject.layer = 27; foreach (var child in target.Children) ApplyLayer(panel, child); }
        private static float ResolveStableHeightCap(UnityEngine.RectTransform target, string name, out string source) { source = "fixture"; return 1080; }
    }
    public static class SharedWindowSize
    {
        public static bool IsArmed(ConvertedPanel panel) => false;
        public static bool Repin(ConvertedPanel panel, UnityEngine.RectTransform target, out string note) { note = ""; return false; }
    }
    public sealed class GrabbableModal
    {
        public static bool ThrowAfterConvert;
        public static GrabbableModal? Last;
        public bool Destroyed;
        public void Build(ConvertedPanel panel, float scale, string label) { Last = this; if (ThrowAfterConvert) throw new InvalidOperationException(); }
        public void Tick() { }
        public void LateSyncHost() { }
        public void Destroy() => Destroyed = true;
    }
}
namespace GloomhavenVR.WorldUI.MapRoom { public static class MapRoomDriver { public static bool Active = true; } }
