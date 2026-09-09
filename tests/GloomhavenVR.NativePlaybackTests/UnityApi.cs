// Observable Unity API seam: execute the production write/ownership helpers and count writes.
// This does not emulate rendering or establish hardware timing or visual parity.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public record struct Color(float r, float g, float b, float a);
    public record struct Vector4(float x, float y, float z, float w);
    public record struct Vector2(float x, float y);
    public class Texture { }
    public sealed class Shader
    {
        private static readonly Dictionary<string, int> Ids = new();
        public readonly HashSet<int> Properties = new();
        public static int PropertyToID(string name)
        {
            if (!Ids.TryGetValue(name, out int id)) Ids[name] = id = Ids.Count + 1;
            return id;
        }
    }
    public sealed class Material
    {
        public Shader shader = new();
        public int Writes, Probes;
        private readonly Dictionary<int, float> _float = new();
        private readonly Dictionary<int, Vector4> _vector = new();
        private readonly Dictionary<int, Color> _color = new();
        private readonly Dictionary<int, Vector2> _scale = new();
        private readonly Dictionary<int, Texture?> _texture = new();
        public bool HasProperty(int property) { Probes++; return shader.Properties.Contains(property); }
        public float GetFloat(int property) => _float.GetValueOrDefault(property);
        public void SetFloat(int property, float value) { Writes++; _float[property] = value; }
        public Vector4 GetVector(int property) => _vector.GetValueOrDefault(property);
        public void SetVector(int property, Vector4 value) { Writes++; _vector[property] = value; }
        public Color GetColor(int property) => _color.GetValueOrDefault(property);
        public void SetColor(int property, Color value) { Writes++; _color[property] = value; }
        public Vector2 GetTextureScale(int property) => _scale.GetValueOrDefault(property);
        public void SetTextureScale(int property, Vector2 value) { Writes++; _scale[property] = value; }
        public Texture? GetTexture(int property) => _texture.GetValueOrDefault(property);
        public void SetTexture(int property, Texture? value) { Writes++; _texture[property] = value; }
    }
    public sealed class GameObject
    {
        public int Writes;
        public bool activeSelf;
        public void SetActive(bool value) { Writes++; activeSelf = value; }
    }
    public sealed class CanvasRenderer
    {
        public int Writes;
        private Color _color;
        public Color GetColor() => _color;
        public void SetColor(Color value) { Writes++; _color = value; }
    }
    public sealed class CanvasGroup
    {
        public readonly GameObject gameObject = new();
        public int Writes;
        private float _alpha;
        private bool _enabled, _ignore;
        public float alpha { get => _alpha; set { Writes++; _alpha = value; } }
        public bool enabled { get => _enabled; set { Writes++; _enabled = value; } }
        public bool ignoreParentGroups { get => _ignore; set { Writes++; _ignore = value; } }
    }
}
namespace UnityEngine.UI
{
    public sealed class Graphic
    {
        public readonly UnityEngine.GameObject gameObject = new();
        public readonly UnityEngine.CanvasRenderer canvasRenderer = new();
        public int Writes;
        private UnityEngine.Color _color;
        private bool _enabled;
        private UnityEngine.Material _material = new();
        public UnityEngine.Color color { get => _color; set { Writes++; _color = value; } }
        public bool enabled { get => _enabled; set { Writes++; _enabled = value; } }
        public UnityEngine.Material material { get => _material; set { Writes++; _material = value; } }
    }
}
namespace GloomhavenVR.Net
{
    internal static class CardAppearanceBindings
    {
        internal static readonly int[] FloatIds = { UnityEngine.Shader.PropertyToID("_GreyOut"), UnityEngine.Shader.PropertyToID("_Burn") };
        internal static readonly int BurnTint = UnityEngine.Shader.PropertyToID("_BurnTint");
        internal static readonly int FlameTint = UnityEngine.Shader.PropertyToID("_FlameTint");
        internal static readonly int Noise = UnityEngine.Shader.PropertyToID("_Noise");
        internal static readonly int Particle = UnityEngine.Shader.PropertyToID("_Particle");
    }
}
