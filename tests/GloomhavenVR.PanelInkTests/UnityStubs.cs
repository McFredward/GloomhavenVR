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
        public Matrix4x4 worldToLocalMatrix => new(null,this);
        public Matrix4x4 localToWorldMatrix => new(this,null);
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
        public Vector2 pivot = new(.5f,.5f);
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
    // Model precisely the production local-to-world -> world-to-host matrix composition.
    // Existing transform chains carry rotations/scales; this value wrapper allocates nothing.
    public readonly struct Matrix4x4
    {
        private readonly Transform? _from, _to;
        public Matrix4x4(Transform? from,Transform? to) { _from=from;_to=to; }
        public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b) => new(b._from,a._to);
        public Vector3 MultiplyPoint3x4(Vector3 value)
        {
            Vector3 world=_from==null ? value : _from.TransformPoint(value);
            return _to==null ? world : _to.InverseTransformPoint(world);
        }
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
        public float width { get=>xMax-xMin; set=>xMax=xMin+value; }
        public float height { get=>yMax-yMin; set=>yMax=yMin+value; }
        public float x { get=>xMin; set { float w=width;xMin=value;width=w; } }
        public float y { get=>yMin; set { float h=height;yMin=value;height=h; } }
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
    public struct Vector4 { public float x,y,z,w; public Vector4(float a,float b,float c,float d) { x=a;y=b;z=c;w=d; } }
    public class Sprite { public string name="OriginalSprite";public Rect rect; public Vector4 Padding; }
    public struct UIVertex { public Vector3 position;public Color32 color; }
    public class TextGenerator { public readonly List<UIVertex> verts=new(); }
    public struct Color { public float a; }
    public struct Color32 { public byte a; }
    public enum HideFlags { HideAndDontSave }
    public sealed class Mesh
    {
        public string name = "";
        public HideFlags hideFlags;
        public int Reads;
        public readonly List<Vector3> Points = new();
        public readonly List<Color32> Colors = new();
        public void Clear() { Points.Clear(); Colors.Clear(); }
        public void GetVertices(List<Vector3> output) { Reads++; output.Clear(); output.AddRange(Points); }
        public void GetColors(List<Color32> output) { output.Clear(); output.AddRange(Colors); }
    }
    public class Shader { public string name="NativeShader"; }
    public class Material { public string name="NativeMaterial";public Shader? shader=new(); public bool HasProperty(string _) => true;public Color GetColor(string _) => new Color{a=.75f}; }
    public class CanvasGroup : Component { public float alpha=1;public bool ignoreParentGroups; }
    public class Canvas : Component { public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public class CanvasRenderer : Component
    {
        public bool cull;
        public float Alpha = 1;
        public float OwnAlpha = 1;
        public float GetAlpha() => OwnAlpha;
        public float GetInheritedAlpha() => Alpha;
    }
    public static class Mathf
    {
        public static float Abs(float a)=>Math.Abs(a);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int RoundToInt(float value) => (int)Math.Round(value);
        public static float Clamp01(float value) => Math.Clamp(value,0,1);
    }
    public static class Time { public static int frameCount => 1; }
}
namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Component
    {
        public bool ThrowOnMaterial;
        public UnityEngine.Material? material
        { get { if(ThrowOnMaterial) throw new InvalidOperationException("destroyed diagnostic getter");return new UnityEngine.Material(); } }
        public UnityEngine.RectTransform rectTransform => gameObject.transform;
        public UnityEngine.Rect GetPixelAdjustedRect() => rectTransform.rect;
        public UnityEngine.Vector2 PixelAdjustPoint(UnityEngine.Vector2 point) => point;
        public UnityEngine.Canvas? canvas;
        public UnityEngine.Color color = new() { a = 1 };
        public UnityEngine.CanvasRenderer canvasRenderer = new();
    }
    public class RawImage : Graphic { }
    public class Image : Graphic
    {
        public enum Type { Simple,Sliced,Tiled,Filled }
        public enum FillMethod { Horizontal,Vertical,Radial90,Radial180,Radial360 }
        public Type type;public FillMethod fillMethod;public int fillOrigin;
        public float fillAmount=1;public bool preserveAspect;public UnityEngine.Sprite? overrideSprite;
    }
    public class Text : Graphic { public string text = string.Empty; public float pixelsPerUnit=1; public UnityEngine.TextGenerator cachedTextGenerator=new(); }
    public class RectMask2D : UnityEngine.Component { }
    public class Mask : UnityEngine.Component { public bool showMaskGraphic=true; }
}
namespace UnityEngine.Sprites { public static class DataUtility { public static UnityEngine.Vector4 GetPadding(UnityEngine.Sprite sprite)=>sprite.Padding; } }
namespace TMPro
{
    public class TMP_Text : UnityEngine.UI.Graphic { public string text=string.Empty;public UnityEngine.Bounds textBounds;public UnityEngine.Mesh? mesh; }
    public class TMP_SubMeshUI : UnityEngine.UI.Graphic { public UnityEngine.Mesh? mesh; }
}
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
        internal UnityEngine.Transform? FitContentRoot => null;
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
        internal static UnityEngine.Transform? Hover;
        internal static int Self(UnityEngine.Transform value) => ReferenceEquals(value,Hover) ? 2 : 0;
        internal static bool IsDeclaredEffectQuad(UnityEngine.Transform value, UnityEngine.Transform root) => false;
    }
}

public class Singleton<T> where T : class
{
    public static T? Instance;
    public static bool IsInitialized => Instance != null;
}
public sealed class UIRewardsManager { public TMPro.TMP_Text? rewardAnnouncementText; }

namespace GloomhavenVR.Core
{
    internal enum VRLogLevel { Info }
    internal static class VRLog
    {
        internal static bool Enabled, ThrowOnNote;
        internal static readonly List<string> Lines=new();
        internal static bool Wants(VRLogLevel _) => Enabled;
        internal static void Note(string scope,string message)
        { if(ThrowOnNote) throw new InvalidOperationException("log unavailable");Lines.Add(message); }
    }
}
