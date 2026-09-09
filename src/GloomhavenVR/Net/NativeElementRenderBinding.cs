using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Bounded readback of an original element's complete presentation hierarchy. This works
/// for legacy AnimationGUIAnimator clips because it reads their OUTPUT, including stopped endpoints;
/// it never plays, samples, edits or enumerates clip curves and never invokes a game controller.</summary>
internal sealed class NativeElementRenderBinding
{
    internal readonly Node[] Nodes;
    internal readonly NativeElementRenderState Scratch;
    private readonly List<Sprite> _sprites = new();

    internal sealed class Node
    {
        internal Transform Source = null!;
        internal RectTransform? Rect;
        internal Graphic? Graphic;
        internal CanvasRenderer? Renderer;
        internal CanvasGroup? Group;
        internal Image? Image;
        internal RawImage? Raw;
        internal TMP_Text? Text;
        internal int ChildCount;
        internal ushort Components;
        internal byte Parent;
        internal uint Binding;
    }

    internal NativeElementRenderBinding(InfusionElementUI source)
    {
        var nodes = new List<Node>();
        Add(source.transform, 255, 2166136261u, nodes);
        Nodes = nodes.ToArray();
        Scratch = new NativeElementRenderState { Nodes = new NativeElementRenderNode[Nodes.Length] };
        for (int i = 0; i < Nodes.Length; i++)
        {
            Node node = Nodes[i];
            Scratch.Nodes[i] = new NativeElementRenderNode { Parent = node.Parent, Binding = node.Binding,
                Geometry = new float[node.Rect != null ? 18 : 10], Color = new float[node.Graphic != null ? 4 : 0],
                RendererColor = new float[node.Renderer != null ? 4 : 0], Uv = new float[node.Raw != null ? 4 : 0] };
        }
        // Stable asset bank: original serialized prefab images, then this element's original
        // configuration. Current live image order/state is deliberately not a bank input.
        InfusionBoardUI board = InfusionBoardUI.Instance;
        if (board == null || board.elementPrefab == null) throw new InvalidOperationException("original element prefab is unavailable");
        foreach (Image image in board.elementPrefab.GetComponentsInChildren<Image>(true))
        { AddSprite(image.sprite); AddSprite(image.overrideSprite); }
        foreach (var config in board.elementConfigs)
            if (config.element == source.elementType)
            { AddSprite(config.creationIcon); AddSprite(config.strongIcon); AddSprite(config.waningIcon); AddSprite(config.textHighlightBackground); }
    }

    private static void Add(Transform source, byte parent, uint parentHash, List<Node> nodes)
    {
        if (nodes.Count >= NativeElementRenderState.NodesMax) throw new InvalidOperationException("original element hierarchy exceeds32 nodes");
        uint hash = parentHash;
        foreach (char c in source.name) hash = unchecked((hash ^ c) * 16777619u);
        if (parent != 255) hash = unchecked((hash ^ (uint)source.GetSiblingIndex()) * 16777619u);
        if (hash == 0) hash = 1;
        var node = new Node { Source = source, Parent = parent, Binding = hash, ChildCount = source.childCount,
            Rect = source as RectTransform, Graphic = source.GetComponent<Graphic>(), Renderer = source.GetComponent<CanvasRenderer>(),
            Group = source.GetComponent<CanvasGroup>(), Image = source.GetComponent<Image>(), Raw = source.GetComponent<RawImage>(),
            Text = source.GetComponent<TMP_Text>() };
        node.Components = ComponentFlags(node);
        byte index = checked((byte)nodes.Count); nodes.Add(node);
        for (int i = 0; i < source.childCount; i++) Add(source.GetChild(i), index, hash, nodes);
    }
    internal static ushort ComponentFlags(Node node) => (ushort)((node.Rect != null ? NativeElementRenderNode.Rect : 0)
        | (node.Graphic != null ? NativeElementRenderNode.Graphic : 0) | (node.Renderer != null ? NativeElementRenderNode.Renderer : 0)
        | (node.Group != null ? NativeElementRenderNode.Group : 0) | (node.Image != null ? NativeElementRenderNode.Image : 0)
        | (node.Raw != null ? NativeElementRenderNode.Raw : 0) | (node.Text != null ? NativeElementRenderNode.Text : 0));

    internal bool Matches()
    {
        foreach (Node node in Nodes)
            if (node.Source == null || node.Source.childCount != node.ChildCount
                || (node.Parent != 255 && node.Source.parent != Nodes[node.Parent].Source)) return false;
        return true;
    }
    private void AddSprite(Sprite? sprite)
    {
        if (sprite == null) return;
        sprite = CardFaceMipBake.OriginalFor(sprite);
        if (!_sprites.Contains(sprite)) _sprites.Add(sprite);
    }
    private ushort SpriteIndex(Sprite? sprite)
    {
        if (sprite == null) return 0;
        sprite = CardFaceMipBake.OriginalFor(sprite);
        int index = _sprites.IndexOf(sprite);
        if (index < 0) throw new InvalidOperationException("rendered element sprite is outside its original prefab/configuration bank: " + sprite.name);
        return checked((ushort)(index + 1));
    }
    internal bool HasSprite(ushort index) => index <= _sprites.Count;
    internal Sprite? SpriteAt(ushort index)
    {
        if (index == 0) return null;
        Sprite source = _sprites[index - 1];
        return CardFaceMipBake.ReplacementFor(source) ?? source;
    }

    internal void Read()
    {
        for (int i = 0; i < Nodes.Length; i++)
        {
            Node source = Nodes[i]; NativeElementRenderNode value = Scratch.Nodes[i];
            value.Flags = (ushort)(source.Components | (source.Source.gameObject.activeSelf ? NativeElementRenderNode.Active : 0));
            value.Sibling = checked((byte)source.Source.GetSiblingIndex());
            Vector3 p = source.Rect != null ? source.Rect.anchoredPosition3D : source.Source.localPosition;
            Vector3 scale = source.Source.localScale; Quaternion q = source.Source.localRotation;
            float[] g = value.Geometry;
            g[0] = p.x; g[1] = p.y; g[2] = p.z; g[3] = scale.x; g[4] = scale.y; g[5] = scale.z;
            g[6] = q.x; g[7] = q.y; g[8] = q.z; g[9] = q.w;
            if (source.Rect != null)
            {
                g[10] = source.Rect.anchorMin.x; g[11] = source.Rect.anchorMin.y;
                g[12] = source.Rect.anchorMax.x; g[13] = source.Rect.anchorMax.y;
                g[14] = source.Rect.pivot.x; g[15] = source.Rect.pivot.y;
                g[16] = source.Rect.sizeDelta.x; g[17] = source.Rect.sizeDelta.y;
            }
            if (source.Graphic != null)
            { Put(value.Color, source.Graphic.color); if (source.Graphic.enabled) value.Flags |= NativeElementRenderNode.GraphicEnabled; }
            if (source.Renderer != null) Put(value.RendererColor, source.Renderer.GetColor());
            if (source.Group != null)
            { value.GroupAlpha = source.Group.alpha; if (source.Group.ignoreParentGroups) value.Flags |= NativeElementRenderNode.IgnoreParentGroups;
                if (source.Group.enabled) value.Flags |= NativeElementRenderNode.GroupEnabled; }
            if (source.Image != null) { value.Fill = source.Image.fillAmount; value.Sprite = SpriteIndex(source.Image.sprite); value.OverrideSprite = SpriteIndex(source.Image.overrideSprite); }
            if (source.Raw != null)
            { Rect uv = source.Raw.uvRect; value.Uv[0] = uv.x; value.Uv[1] = uv.y; value.Uv[2] = uv.width; value.Uv[3] = uv.height; }
            if (source.Text != null) value.FontSize = source.Text.fontSize;
        }
    }
    private static void Put(float[] values, Color color)
    { values[0] = color.r; values[1] = color.g; values[2] = color.b; values[3] = color.a; }
}
