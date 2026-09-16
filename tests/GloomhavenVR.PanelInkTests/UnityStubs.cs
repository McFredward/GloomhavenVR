using System;
using System.Collections.Generic;

// A bounded uGUI hierarchy model for the real production ink walk, with 2D transform chains;
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
        public Vector3 localPosition;
        public Vector3 localScale = new(1, 1, 1);
        public float angleDegrees;
        public Vector3 TransformPoint(Vector3 point)
        {
            double angle = angleDegrees * Math.PI / 180;
            float x = point.x * localScale.x, y = point.y * localScale.y;
            var own = new Vector3((float)(x * Math.Cos(angle) - y * Math.Sin(angle)) + localPosition.x,
                (float)(x * Math.Sin(angle) + y * Math.Cos(angle)) + localPosition.y, point.z + localPosition.z);
            return parent == null ? own : parent.TransformPoint(own);
        }
        public Vector3 InverseTransformPoint(Vector3 point)
        {
            Vector3 own = parent == null ? point : parent.InverseTransformPoint(point);
            double angle = -angleDegrees * Math.PI / 180;
            float x = own.x - localPosition.x, y = own.y - localPosition.y;
            return new Vector3((float)(x * Math.Cos(angle) - y * Math.Sin(angle)) / localScale.x,
                (float)(x * Math.Sin(angle) + y * Math.Cos(angle)) / localScale.y, own.z - localPosition.z);
        }
        public readonly List<Transform> Children = new();
        public int childCount => Children.Count;
        public Transform GetChild(int index) => Children[index];
        public bool IsChildOf(Transform ancestor)
        { for (Transform? p = parent; p != null; p = p.parent) if (ReferenceEquals(p, ancestor)) return true; return false; }
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> result) where T : Component
        {
            if (!includeInactive && !gameObject.activeInHierarchy) return;
            T? own = GetComponent<T>(); if (own != null) result.Add(own);
            foreach (Transform child in Children) child.GetComponentsInChildren(includeInactive, result);
        }
        public void SetParent(Transform value) { parent = value; value.Children.Add(this); }
    }
    public sealed class RectTransform : Transform
    {
        public Rect rect = Rect.MinMaxRect(-640, -360, 640, 360);
        public void GetWorldCorners(Vector3[] corners)
        {
            corners[0] = TransformPoint(new Vector3(rect.xMin, rect.yMin, 0));
            corners[1] = TransformPoint(new Vector3(rect.xMin, rect.yMax, 0));
            corners[2] = TransformPoint(new Vector3(rect.xMax, rect.yMax, 0));
            corners[3] = TransformPoint(new Vector3(rect.xMax, rect.yMin, 0));
        }
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
    public readonly struct Bounds
    {
        public readonly Vector3 min, max;
        public Bounds(Vector3 center, Vector3 size)
        {
            min = new(center.x - size.x / 2, center.y - size.y / 2, center.z - size.z / 2);
            max = new(center.x + size.x / 2, center.y + size.y / 2, center.z + size.z / 2);
        }
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
    public struct Vector2
    {
        public float x, y;
        public Vector2(float a, float b) { x = a; y = b; }
        public static Vector2 Min(Vector2 a, Vector2 b) => new(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        public static Vector2 Max(Vector2 a, Vector2 b) => new(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
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
namespace TMPro { public class TMP_Text : UnityEngine.UI.Graphic { public string text = string.Empty; public UnityEngine.Bounds textBounds; } }
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
    internal static partial class CanvasConversion
    {
        internal const float FixedFitPlateWidthFraction = .80f;
        internal const float FixedFitPlateHeightFraction = .95f;
        internal const float FitMinAlpha = .05f;
    }
    internal static class HintOnOwnerComposite
    {
        internal static UnityEngine.Transform? Parked;
        internal static bool IsParkedContent(UnityEngine.Transform? value) => Parked != null && value != null
            && (ReferenceEquals(value, Parked) || value.IsChildOf(Parked));
    }
    internal static class TransientFamilies
    {
        internal const int EffectQuadFamily = 7;
        internal static int Self(UnityEngine.Transform value) => 0;
        internal static bool IsDeclaredEffectQuad(UnityEngine.Transform value, UnityEngine.Transform root) => false;
    }
}

public class Singleton<T> where T : class
{
    public static T? Instance;
    public static bool IsInitialized => Instance != null;
}
public sealed class UIRewardsManager { public TMPro.TMP_Text? rewardAnnouncementText; }
