using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Path-addressed original item graphics and groups; no slot-count truncation or shader reconstruction.</summary>
internal sealed class ItemAppearanceBindings
{
    private static readonly FieldInfo? Overlay = typeof(ItemCardEffects).GetField("fgFx", BindingFlags.Instance | BindingFlags.NonPublic);
    internal readonly Transform Root;
    internal readonly Dictionary<uint, Graphic> Graphics = new();
    internal readonly Dictionary<uint, CanvasGroup> Groups = new();
    internal readonly ItemCardEffects Effects;
    private readonly List<Graphic> _graphics = new();
    private readonly List<CanvasGroup> _groups = new();
    private readonly Dictionary<Graphic, NativePlaybackProperties> _support = new();
    private readonly Dictionary<Graphic, Material> _materials = new();
    private readonly List<ItemAppearanceNode> _scratch = new();
    private ItemAppearanceNode[] _published = Array.Empty<ItemAppearanceNode>();
    internal ItemAppearanceBindings(ItemCardEffects effects)
    {
        Effects = effects; Root = effects.transform;
        Root.GetComponentsInChildren(true, _graphics); Root.GetComponentsInChildren(true, _groups);
        foreach (Graphic graphic in _graphics) Graphics.Add(Key(graphic.transform, Root), graphic);
        foreach (CanvasGroup group in _groups) Groups.Add(Key(group.transform, Root), group);
    }
    private static uint Key(Transform node, Transform root)
    {
        unchecked
        {
            uint key = 2166136261;
            for (Transform? t = node; t != null && !ReferenceEquals(t, root); t = t.parent)
            {
                key = (key ^ (uint)(t.GetSiblingIndex() + 1)) * 16777619;
                foreach (char c in t.name) key = (key ^ c) * 16777619;
            }
            return key == 0 ? 1u : key;
        }
    }
    private static bool Visible(Transform target, Transform root)
    {
        for (Transform? t = target; t != null; t = t.parent)
        {
            if (!t.gameObject.activeSelf) return false;
            if (ReferenceEquals(t, root)) return true;
        }
        return false;
    }
    internal ItemAppearanceNode[] Capture()
    {
        int index = 0; Graphic? overlay = Overlay?.GetValue(Effects) as Graphic;
        foreach (var pair in Graphics)
        {
            Graphic graphic = pair.Value; var entry = Scratch(index++); var node = entry.Value;
            entry.Binding = pair.Key; node.Role = (byte)(ReferenceEquals(graphic, overlay) ? 11 : graphic is TextMeshProUGUI ? 7 : 0);
            node.Flags = (byte)((Visible(graphic.transform, Root) ? 1 : 0) | (graphic.enabled ? 2 : 0)
                | (graphic is TextMeshProUGUI text && text.enableVertexGradient ? 4 : 0));
            CardAppearanceBindings.Put(node.Values, 0, graphic.color);
            CardAppearanceBindings.Put(node.Values, 4, graphic.canvasRenderer.GetColor());
            Material material = graphic.material;
            uint support = Support(graphic, material); node.Mask = support & CardAppearanceNode.AllowedMask(node.Role);
            for (int f = 0; f < CardAppearanceBindings.FloatIds.Length; f++) if ((node.Mask & 1u << f) != 0)
                node.Values[8 + f] = material.GetFloat(CardAppearanceBindings.FloatIds[f]);
            if ((node.Mask & 1u << 15) != 0) CardAppearanceBindings.Put(node.Values, 23, material.GetColor(CardAppearanceBindings.BurnTint));
            if ((node.Mask & 1u << 16) != 0) CardAppearanceBindings.Put(node.Values, 27, material.GetColor(CardAppearanceBindings.FlameTint));
            if ((node.Mask & 1u << 17) != 0) { Vector2 scale = material.GetTextureScale(CardAppearanceBindings.Noise); node.Values[31] = scale.x; node.Values[32] = scale.y; }
            if (node.Role == 11 && (support & NativePlaybackProperties.ParticleMask) != 0
                && ReferenceEquals(material.GetTexture(CardAppearanceBindings.Particle), Effects.overlayFrameGhost)) node.Flags |= 8;
        }
        foreach (var pair in Groups)
        {
            CanvasGroup group = pair.Value; var entry = Scratch(index++); var node = entry.Value;
            entry.Binding = node.Binding = pair.Key; node.Role = 12;
            node.Flags = (byte)((group.gameObject.activeSelf ? 1 : 0) | (group.enabled ? 2 : 0) | (group.ignoreParentGroups ? 4 : 0));
            node.Values[0] = group.alpha;
        }
        bool same = _published.Length == index;
        for (int i = 0; same && i < index; i++) same = Same(_scratch[i], _published[i]);
        if (same) return _published;
        var published = new ItemAppearanceNode[index];
        for (int i = 0; i < index; i++) published[i] = _scratch[i].Copy();
        return _published = published;
    }
    private ItemAppearanceNode Scratch(int index)
    {
        if (index == _scratch.Count) _scratch.Add(new ItemAppearanceNode());
        var entry = _scratch[index]; entry.Value.Flags = 0; entry.Value.Mask = entry.Value.Binding = 0;
        Array.Clear(entry.Value.Values, 0, entry.Value.Values.Length); return entry;
    }
    private static bool Same(ItemAppearanceNode a, ItemAppearanceNode b)
    {
        var x = a.Value; var y = b.Value;
        if (a.Binding != b.Binding || x.Binding != y.Binding || x.Role != y.Role || x.Flags != y.Flags || x.Mask != y.Mask) return false;
        for (int i = 0; i < x.Values.Length; i++) if (x.Values[i] != y.Values[i]) return false;
        return true;
    }
    private uint Support(Graphic graphic, Material material)
    {
        if (!_support.TryGetValue(graphic, out var support)) _support[graphic] = support = new NativePlaybackProperties();
        return support.Support(material);
    }
    internal bool Apply(ItemAppearanceState from, ItemAppearanceState to, float progress, Vector4 bounds, Action<Image, Material>? flameQueue = null)
    {
        foreach (var entry in to.Nodes)
            if (entry.Value.Role >= 12 ? !Groups.ContainsKey(entry.Binding) : !Graphics.ContainsKey(entry.Binding)) return false;
        foreach (var entry in to.Nodes)
        {
            var node = entry.Value; var old = node;
            foreach (var previous in from.Nodes) if (previous.Binding == entry.Binding && previous.Value.Role == node.Role && previous.Value.Mask == node.Mask) { old = previous.Value; break; }
            float k = node.Flags == old.Flags ? progress : 1f;
            float V(int index) => Mathf.LerpUnclamped(old.Values[index], node.Values[index], k);
            Color C(int index) => new(V(index), V(index+1), V(index+2), V(index+3));
            if (node.Role >= 12) { NativePlaybackWrites.Group(Groups[entry.Binding], V(0), node.Flags); continue; }
            Graphic graphic = Graphics[entry.Binding]; NativePlaybackWrites.Graphic(graphic, C(0), C(4), node.Flags);
            if (graphic is TextMeshProUGUI text && text.enableVertexGradient != ((node.Flags & 4) != 0)) text.enableVertexGradient = (node.Flags & 4) != 0;
            if (node.Mask == 0 && node.Role >= 7) continue;
            if (!_materials.TryGetValue(graphic, out Material material) || material == null)
                _materials[graphic] = material = new Material(graphic.material) { name = graphic.material.name + " (VR native item)" };
            NativePlaybackWrites.Material(graphic, material); uint support = Support(graphic, material);
            if (node.Role < 7 && (support & NativePlaybackProperties.BoundsMask) != 0) NativePlaybackWrites.Vector(material, Shader.PropertyToID("_PosAndBounds"), bounds);
            for (int f = 0; f < CardAppearanceBindings.FloatIds.Length; f++) if ((node.Mask & support & 1u << f) != 0) NativePlaybackWrites.Float(material, CardAppearanceBindings.FloatIds[f], V(8+f));
            if ((node.Mask & support & 1u << 15) != 0) NativePlaybackWrites.Color(material, CardAppearanceBindings.BurnTint, C(23));
            if ((node.Mask & support & 1u << 16) != 0) NativePlaybackWrites.Color(material, CardAppearanceBindings.FlameTint, C(27));
            if ((node.Mask & support & 1u << 17) != 0) NativePlaybackWrites.TextureScale(material, CardAppearanceBindings.Noise, new Vector2(V(31), V(32)));
            if (node.Role == 11)
            {
                if (graphic is Image flame) flameQueue?.Invoke(flame, material);
                if ((support & NativePlaybackProperties.ParticleMask) != 0)
                    NativePlaybackWrites.Texture(material, CardAppearanceBindings.Particle, (node.Flags & 8) != 0 ? Effects.overlayFrameGhost : Effects.overlayFrameBurn);
            }
        }
        return true;
    }
    internal void Destroy()
    {
        foreach (Material material in _materials.Values) if (material != null) UnityEngine.Object.Destroy(material);
        _materials.Clear();
    }
}
