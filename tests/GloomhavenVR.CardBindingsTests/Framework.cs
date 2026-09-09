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
        public string name;
        public Transform? parent;
        public readonly List<Transform> Children = new();
        public Transform(GameObject owner, string name) { gameObject = owner; this.name = name; }
        public void Add(Transform child) { child.parent = this; Children.Add(child); }
        public int GetSiblingIndex() => parent?.Children.IndexOf(this) ?? 0;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>(); GetComponentsInChildren(includeInactive, result); return result.ToArray();
        }
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> result) where T : Component
        { result.Clear(); AppendComponents(includeInactive, result); }
        private void AppendComponents<T>(bool includeInactive, List<T> result) where T : Component
        {
            if (!includeInactive && !gameObject.activeSelf) return;
            foreach (Component component in gameObject.Components) if (component is T match) result.Add(match);
            foreach (Transform child in Children) child.AppendComponents(includeInactive, result);
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
        public readonly HashSet<int> Properties = new();
        public static int PropertyToID(string name) => name.GetHashCode();
    }
    public sealed class Material
    {
        public Shader shader = new();
        public readonly Dictionary<int, float> Floats = new();
        public readonly Dictionary<int, Color> Colors = new();
        public readonly Dictionary<int, Vector2> Scales = new();
        public readonly Dictionary<int, Texture?> Textures = new();
        public bool HasProperty(int property) => shader.Properties.Contains(property);
        public float GetFloat(int property) => Floats.TryGetValue(property, out float value) ? value : 0f;
        public Color GetColor(int property) => Colors.TryGetValue(property, out Color value) ? value : default;
        public Vector2 GetTextureScale(int property) => Scales.TryGetValue(property, out Vector2 value) ? value : default;
        public Texture? GetTexture(int property) => Textures.TryGetValue(property, out Texture? value) ? value : null;
    }
    public sealed class CanvasRenderer
    {
        public Color Color = new(1f);
        public Color GetColor() => Color;
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
    // Only the positional address constants are stubbed; node/state validation and snapshot
    // retention below execute production code. The complete wire grammar has its own suite.
    internal static class NetProtocol
    {
        internal const byte HeldFaceIndexUnknown = 31;
        internal static byte HeldFaceIndex(byte code) => (byte)(code & 31);
        internal static byte HeldFaceList(byte code) => (byte)(code >> 5);
        internal static bool HeldFaceNamesCard(byte code) => HeldFaceList(code) >= 1
            && HeldFaceList(code) <= 6 && HeldFaceIndex(code) != HeldFaceIndexUnknown;
    }
    internal static class CardPlumeState { internal const byte RoundList = 7; }
}
