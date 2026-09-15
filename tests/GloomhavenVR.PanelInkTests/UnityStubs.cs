using System;
using System.Collections.Generic;

// A bounded uGUI hierarchy model for the real production ink walk. Geometry is axis-aligned;
// these tests claim content classification, alpha, clipping and visibility, not Unity rendering.
namespace UnityEngine
{
    public class Object { public int GetInstanceID() => GetHashCode(); }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public bool enabled = true;
        public string name => gameObject.name;
        public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class Transform : Component
    {
        public Transform? parent;
        public readonly List<Transform> Children = new();
        public int childCount => Children.Count;
        public Transform GetChild(int index) => Children[index];
        public void SetParent(Transform value) { parent = value; value.Children.Add(this); }
    }
    public sealed class RectTransform : Transform
    {
        public Rect rect = Rect.MinMaxRect(-640, -360, 640, 360);
        public void GetWorldCorners(Vector3[] corners)
        {
            corners[0] = new Vector3(rect.xMin, rect.yMin, 0);
            corners[1] = new Vector3(rect.xMin, rect.yMax, 0);
            corners[2] = new Vector3(rect.xMax, rect.yMax, 0);
            corners[3] = new Vector3(rect.xMax, rect.yMin, 0);
        }
        public Vector3 InverseTransformPoint(Vector3 point) => point;
    }
    public class GameObject : Object
    {
        public readonly string name;
        public readonly RectTransform transform;
        private readonly List<Component> _components = new();
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public GameObject(string value)
        { name = value; transform = new RectTransform { gameObject = this }; _components.Add(transform); }
        public T AddComponent<T>() where T : Component, new()
        { var component = new T { gameObject = this }; _components.Add(component); return component; }
        public T? GetComponent<T>() where T : Component
        { foreach (var item in _components) if (item is T result) return result; return null; }
    }
    public class Renderer : Component { }
    public class Camera : Component { }
    public readonly struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
    }
    public struct Rect
    {
        public float xMin, yMin, xMax, yMax;
        public float width => xMax - xMin;
        public float height => yMax - yMin;
        public Rect(float x, float y, float w, float h) { xMin = x; yMin = y; xMax = x + w; yMax = y + h; }
        public static Rect MinMaxRect(float x, float y, float right, float top)
            => new Rect(x, y, right - x, top - y);
    }
    public struct Color { public float a; }
    public class CanvasRenderer : Component
    {
        public bool cull;
        public float Alpha = 1;
        public float GetInheritedAlpha() => Alpha;
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
    }
    public static class Time { public static int frameCount => 1; }
}
namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Component
    {
        public UnityEngine.Color color = new() { a = 1 };
        public UnityEngine.CanvasRenderer canvasRenderer = new();
    }
    public class RawImage : Graphic { }
    public class Image : Graphic { }
    public class Text : Graphic { public string text = string.Empty; }
    public class RectMask2D : UnityEngine.Component { }
    public class Mask : UnityEngine.Component { }
}
namespace TMPro { public class TMP_Text : UnityEngine.UI.Graphic { public string text = string.Empty; } }
public sealed class NewPartyDisplayUI
{
    public static NewPartyDisplayUI? PartyDisplay => null;
        public int ActiveDisplay => 0;
        public UnityEngine.Component? CharacterSelector => null;
        public UnityEngine.Component? PerkManager => null;
        public UnityEngine.Component? AbilityCardsDisplay => null;
        public UnityEngine.Component? EnhancementCardsDisplay => null;
        public UnityEngine.Component? ItemInventoryDisplay => null;
        public UnityEngine.Component? BattleGoalWindow => null;
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class ConvertedPanel
    {
        public UnityEngine.RectTransform HostRect = null!;
        public UnityEngine.RectTransform Target = null!;
        public UnityEngine.UI.Graphic? ContentGraphic;
    }
    internal static class CanvasConversion
    {
        internal const float FixedFitPlateWidthFraction = .80f;
        internal const float FixedFitPlateHeightFraction = .95f;
        internal const float FitMinAlpha = .05f;
    }
    internal static class TransientFamilies
    {
        internal const int EffectQuadFamily = 7;
        internal static int Self(UnityEngine.Transform value) => 0;
        internal static bool IsDeclaredEffectQuad(UnityEngine.Transform value, UnityEngine.Transform root) => false;
    }
}
