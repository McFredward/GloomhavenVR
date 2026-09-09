using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Exact comparisons against actual clone output. A repeated snapshot is not proof
/// that the target is unchanged: layout, legacy effects and moving burn bounds also write it.</summary>
internal static class NativePlaybackWrites
{
    internal static void Active(GameObject target, bool value)
    { if (target.activeSelf != value) target.SetActive(value); }
    internal static void Group(CanvasGroup target, float alpha, byte flags)
    {
        if (!target.alpha.Equals(alpha)) target.alpha = alpha;
        if (target.enabled != ((flags & 2) != 0)) target.enabled = (flags & 2) != 0;
        if (target.ignoreParentGroups != ((flags & 4) != 0)) target.ignoreParentGroups = (flags & 4) != 0;
        Active(target.gameObject, (flags & 1) != 0);
    }
    internal static void Graphic(Graphic target, Color color, Color rendered, byte flags)
    {
        if (!target.color.Equals(color)) target.color = color;
        if (!target.canvasRenderer.GetColor().Equals(rendered)) target.canvasRenderer.SetColor(rendered);
        if (target.enabled != ((flags & 2) != 0)) target.enabled = (flags & 2) != 0;
        Active(target.gameObject, (flags & 1) != 0);
    }
    internal static void Material(Graphic target, Material material)
    { if (!ReferenceEquals(target.material, material)) target.material = material; }
    internal static void Float(Material target, int property, float value)
    { if (!target.GetFloat(property).Equals(value)) target.SetFloat(property, value); }
    internal static void Vector(Material target, int property, Vector4 value)
    { if (!target.GetVector(property).Equals(value)) target.SetVector(property, value); }
    internal static void Color(Material target, int property, Color value)
    { if (!target.GetColor(property).Equals(value)) target.SetColor(property, value); }
    internal static void TextureScale(Material target, int property, Vector2 value)
    { if (!target.GetTextureScale(property).Equals(value)) target.SetTextureScale(property, value); }
    internal static void Texture(Material target, int property, Texture? value)
    { if (!ReferenceEquals(target.GetTexture(property), value)) target.SetTexture(property, value); }
}

/// <summary>One bounded cache per native card role; only shader property existence is cached.
/// Shader swaps invalidate it immediately, including swaps on the same material instance.</summary>
internal sealed class NativePlaybackProperties
{
    internal const uint BoundsMask = 1u << 18;
    internal const uint ParticleMask = 1u << 19;
    private static readonly int Bounds = Shader.PropertyToID("_PosAndBounds");
    private Shader? _shader;
    private uint _support;
    internal uint Support(Material material)
    {
        Shader shader = material.shader;
        if (shader != null && ReferenceEquals(_shader, shader)) return _support;
        uint support = 0;
        for (int f = 0; f < CardAppearanceBindings.FloatIds.Length; f++)
            if (material.HasProperty(CardAppearanceBindings.FloatIds[f])) support |= 1u << f;
        if (material.HasProperty(CardAppearanceBindings.BurnTint)) support |= 1u << 15;
        if (material.HasProperty(CardAppearanceBindings.FlameTint)) support |= 1u << 16;
        if (material.HasProperty(CardAppearanceBindings.Noise)) support |= 1u << 17;
        if (material.HasProperty(Bounds)) support |= BoundsMask;
        if (material.HasProperty(CardAppearanceBindings.Particle)) support |= ParticleMask;
        _shader = shader; _support = support;
        return support;
    }
}

/// <summary>Field-only native material ownership. The normal mirror still copies every other
/// widget property. The exact acquiring material is the release token, so stale teardown cannot
/// revoke a replacement generation's ownership.</summary>
internal struct NativePlaybackMaterialOwner
{
    private Material? _owned;
    internal void Own(Material material) => _owned = material;
    internal void Release(Graphic source, Graphic target, Material material)
    {
        if (!ReferenceEquals(_owned, material)) return;
        _owned = null;
        Copy(source, target);
    }
    internal void Copy(Graphic source, Graphic target)
    {
        if (_owned == null) NativePlaybackWrites.Material(target, source.material);
    }
}

/// <summary>Pattern-based two-array enumeration without iterator objects or interface boxing.</summary>
internal readonly struct NativePlaybackRange<T>
{
    private readonly T[] _nodes;
    private readonly T[]? _extra;
    internal NativePlaybackRange(T[] nodes, T[]? extra) { _nodes = nodes; _extra = extra; }
    public Enumerator GetEnumerator() => new(_nodes, _extra);
    internal struct Enumerator
    {
        private readonly T[] _nodes;
        private readonly T[]? _extra;
        private int _index;
        internal Enumerator(T[] nodes, T[]? extra) { _nodes = nodes; _extra = extra; _index = -1; }
        public bool MoveNext() => ++_index < _nodes.Length + (_extra?.Length ?? 0);
        public T Current => _index < _nodes.Length ? _nodes[_index] : _extra![_index - _nodes.Length];
    }
}
