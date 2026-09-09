// Minimal tree/material API for executing the production binding discovery and capture code.
// These fakes do not emulate Unity lifecycle, drawing, shaders, Addressables or network codecs.
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
    }
    public sealed class GameObject
    {
        public readonly Transform transform;
        public readonly List<Component> Components = new();
        public bool activeSelf = true;
        public GameObject(string name) { transform = new Transform(this, name); }
        public T Add<T>() where T : Component, new() { var c = new T { gameObject = this }; Components.Add(c); return c; }
        public T? GetComponentInChildren<T>(bool includeInactive) where T : Component
            => transform.GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
    }
    public sealed class Transform
    {
        public readonly GameObject gameObject;
        public readonly string name;
        public Transform? parent;
        public readonly List<Transform> Children = new();
        public Transform(GameObject owner, string name) { gameObject = owner; this.name = name; }
        public void Add(Transform child) { child.parent = this; Children.Add(child); }
        public int GetSiblingIndex() => parent?.Children.IndexOf(this) ?? 0;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            if (includeInactive || gameObject.activeSelf)
            {
                result.AddRange(gameObject.Components.OfType<T>());
                foreach (var child in Children) result.AddRange(child.GetComponentsInChildren<T>(includeInactive));
            }
            return result.ToArray();
        }
    }
    public sealed class CanvasGroup : Component
    {
        public float alpha = 1f;
        public bool enabled = true, ignoreParentGroups;
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float value) { r = g = b = a = value; }
    }
    public struct Vector2 { public float x, y; }
    public sealed class Texture { }
    public sealed class Shader
    {
        public static int PropertyToID(string name) => name.GetHashCode();
    }
    public sealed class Material
    {
        public readonly Shader shader = new();
        public bool HasProperty(int property) => false;
        public float GetFloat(int property) => 0;
        public Color GetColor(int property) => default;
        public Vector2 GetTextureScale(int property) => default;
        public Texture? GetTexture(int property) => null;
    }
    public sealed class CanvasRenderer
    {
        public Color GetColor() => new(1f);
    }
}
namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Component
    {
        public bool enabled = true;
        public UnityEngine.Color color = new(1f);
        public UnityEngine.CanvasRenderer canvasRenderer = new();
        public UnityEngine.Material material = new();
    }
}
namespace TMPro
{
    public sealed class TextMeshProUGUI : UnityEngine.UI.Graphic { public bool enableVertexGradient; }
}
public sealed class CardEffects : UnityEngine.Component
{
    public UnityEngine.Material? _lowMaterial = null;
    public UnityEngine.Texture? overlayFrameBurn = null, overlayFrameGhost = null;
    public UnityEngine.UI.Graphic? _headerImage = null, _topButton = null, _bottomAction = null,
        _topDefAction = null, _botDefAction = null, _topDefActionIcon = null, _botDefActionIcon = null,
        _header = null, _topDefActionTxt = null, _bottomDefActionTxt = null, _initiativeText = null, _uiFxOverlay = null;
}
public sealed class AssetBundleManager
{
    public static AssetBundleManager? Instance => null;
    public T? LoadAssetFromBundle<T>(string bundle, string asset, string category) where T : class => null;
}
namespace GloomhavenVR.Net
{
    // Capture output only; actual DTO/codec validation is exercised by GloomhavenVR.WireTests.
    internal sealed class CardAppearanceNode
    {
        internal byte Role, Flags;
        internal uint Binding, Mask;
        internal float[] Values = new float[33];
        internal static uint AllowedMask(byte role) => 0;
    }
}
