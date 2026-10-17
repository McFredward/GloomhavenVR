using System;
using System.Collections.Generic;
using System.Linq;

// UI hierarchy/art helpers and runtime are explicit boundaries. The complete new
// partial plus actual Advanced index and BuildLinkRow/Button listener are executed.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object? a, Object? b)
        {
            if (ReferenceEquals(a, null)) return ReferenceEquals(b, null) || b.Destroyed;
            if (ReferenceEquals(b, null)) return a.Destroyed;
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? other) => other is Object o && this == o;
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object o)
        {
            o.Destroyed = true;
            if (o is GameObject go)
            {
                foreach (var component in go.Components) component.Destroyed = true;
                foreach (var child in go.transform.Children) Destroy(child.gameObject);
            }
        }
        public static GameObject Instantiate(GameObject original, Transform parent)
        {
            var row = new GameObject("Category clone"); row.transform.SetParent(parent);
            row.AddComponent<TMPro.TMP_Text>(); row.AddComponent<UI.Toggle>();
            return row;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component
    {
        public readonly List<Transform> Children = new();
        public void SetParent(Transform parent) => parent.Children.Add(this);
    }
    public class RectTransform : Transform { public Rect rect = new() { width = 600 }; }
    public struct Rect { public float width; }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Color { }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public readonly Transform transform;
        public string name;
        public bool activeSelf = true;
        public GameObject(string name = "fixture") { this.name = name; transform = new RectTransform { gameObject = this }; }
        public void SetActive(bool active) => activeSelf = active;
        public T AddComponent<T>() where T : Component, new()
        { var value = new T { gameObject = this }; Components.Add(value); return value; }
        public T? GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault(c => !c.Destroyed);
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component =>
            Components.OfType<T>().Concat(transform.Children.SelectMany(c => c.gameObject.GetComponentsInChildren<T>(inactive)))
                .Where(c => !c.Destroyed && (inactive || c.gameObject.activeSelf)).ToArray();
        public T? GetComponentInChildren<T>(bool inactive) where T : Component => GetComponentsInChildren<T>(inactive).FirstOrDefault();
    }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
}
namespace UnityEngine.UI
{
    public class Image : UnityEngine.Component { }
    public class LayoutElement : UnityEngine.Component
    { public float minHeight = 40, preferredHeight = 40, flexibleHeight; }
    public struct ColorBlock
    {
        public UnityEngine.Color normalColor, highlightedColor, pressedColor, selectedColor;
        public float fadeDuration;
    }
    public class Button : UnityEngine.Component
    {
        public sealed class Event
        {
            private readonly List<Action> _callbacks = new();
            public void AddListener(Action callback) => _callbacks.Add(callback);
            public void Invoke() { foreach (var callback in _callbacks.ToArray()) callback(); }
        }
        public Image? targetGraphic;
        public ColorBlock colors;
        public readonly Event onClick = new();
    }
    public class Toggle : UnityEngine.Component
    {
        public sealed class Event
        {
            private readonly List<Action<bool>> _callbacks = new();
            public void RemoveAllListeners() => _callbacks.Clear();
            public void AddListener(Action<bool> callback) => _callbacks.Add(callback);
        }
        public object? group;
        public Image? targetGraphic, graphic;
        public readonly Event onValueChanged = new();
        public void SetIsOnWithoutNotify(bool on) { }
    }
}
namespace TMPro
{
    public class TMP_Text : UnityEngine.Component
    {
        private string _text = "";
        public int Writes;
        public string text { get => _text; set { _text = value; Writes++; } }
        public UnityEngine.RectTransform rectTransform => (UnityEngine.RectTransform)transform;
        public UnityEngine.Vector2 GetPreferredValues(string value, float width, float height) =>
            new(width, (float)Math.Ceiling(value.Length * 20f / Math.Max(width, 1f)) * 30f);
    }
}
namespace GloomhavenVR.Core
{
    internal static class Loc
    {
        internal static string Language = "English";
        internal static string StereoVideoCalibrationStart => Language + ":Start stereo";
        internal static string StereoVideoCalibrationMenuHint => Language + ":Offline main menu; 9s loop; click picture to close";
        internal static string Mod(string key) => key;
    }
    internal static class TickGuard { internal static void Run(string tag, Action callback, string area) => callback(); }
    internal static class VRLog { internal static void Warn(string area, string message) => throw new Exception(message); }
}
namespace GloomhavenVR.WorldUI
{
    internal static class StereoVideoCalibration
    {
        internal static int Requests, Decoders, StatusReads;
        internal static bool Active, Allowed = true, Missing;
        internal static string State = "ready";
        internal static string Status { get { StatusReads++; return Core.Loc.Language + ":" + State; } }
        internal static void RequestStart()
        {
            Requests++;
            if (Active) return;
            if (!Allowed) { State = "requires offline main menu"; return; }
            if (Missing) { State = "movie files missing"; return; }
            Decoders++; Active = true; State = "preparing";
        }
    }
    internal static class ConfigCatalog
    {
        internal enum ConfigTopic { First, Second, Empty }
        internal const int TopicCount = 3;
        internal static List<int> Groups(ConfigTopic topic) => topic == ConfigTopic.Empty ? new() : new() { 1 };
        internal static string TopicLabel(ConfigTopic topic) => topic.ToString();
    }
    internal static partial class VROptionsTab
    {
        private enum View { AdvancedTopic, AdvancedTriggers }
        private static View _view;
        private static ConfigCatalog.ConfigTopic _category;
        private static UnityEngine.Transform? ContentRoot;
        private static readonly List<UnityEngine.GameObject> Rows = new();
        private static UnityEngine.GameObject? _categoryTemplate = new("Category template");
        private static UnityEngine.GameObject? _toggleTemplate = new("Toggle template");
        private const float HoverTintFadeSeconds = .1f;
        internal static int Rebuilds;
        private static void Rebuild() { _ = _view; _ = _category; Rebuilds++; }
        private static void BuildHeader(UnityEngine.Transform parent, string text) { }
        private static int BuildCheatsIndexLink() => 0;
        private static void StripForReuse(UnityEngine.GameObject row) { }
        private static void RestVisuals(UnityEngine.GameObject row) { }
        private static void FitTabCaption(TMPro.TMP_Text title) { }
        private static void ApplyOptionCaption(TMPro.TMP_Text title) { }
        private static void EnsureRowHitArea(UnityEngine.GameObject row) { }
        private static UnityEngine.Color ActionPlateRest() => new();
        private static UnityEngine.Color ActionPlateHot() => new();
        private static UnityEngine.Color ActionPlateDown() => new();
        private static void SafeDestroy(UnityEngine.Object target) => UnityEngine.Object.Destroy(target);
        private static UnityEngine.UI.Image? BuildActionPlate(UnityEngine.GameObject row, UnityEngine.Transform? option) => row.AddComponent<UnityEngine.UI.Image>();
        private static UnityEngine.GameObject StampRow(UnityEngine.GameObject? template, UnityEngine.Transform parent,
            out TMPro.TMP_Text? title, out UnityEngine.Transform? option)
        {
            var row = new UnityEngine.GameObject("Setting row"); row.transform.SetParent(parent);
            title = row.AddComponent<TMPro.TMP_Text>(); row.AddComponent<UnityEngine.UI.LayoutElement>();
            var holder = new UnityEngine.GameObject("Option"); holder.transform.SetParent(row.transform);
            holder.AddComponent<UnityEngine.UI.Toggle>(); option = holder.transform;
            Rows.Add(row); return row;
        }
        private static void BuildNote(UnityEngine.Transform parent, string text, bool multiline = false)
        {
            var row = StampRow(_toggleTemplate, parent, out var title, out var option);
            title!.text = text; option!.gameObject.SetActive(false);
        }
        internal static UnityEngine.GameObject[] FixtureRows => Rows.ToArray();
        internal static int BuildFixture(bool present = true)
        {
            ContentRoot = present ? new UnityEngine.GameObject("Content").transform : null;
            return BuildAdvancedIndex();
        }
        internal static void DestroyFixture()
        { foreach (var row in Rows) UnityEngine.Object.Destroy(row); Rows.Clear(); }
        internal static void Frame()
        {
            foreach (var row in Rows.Where(r => !r.Destroyed && r.activeSelf))
            foreach (var component in row.Components.OfType<UnityEngine.MonoBehaviour>().Where(c => !c.Destroyed))
                component.GetType().GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(component, null);
        }
    }
}
