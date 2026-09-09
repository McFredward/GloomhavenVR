using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Final readback writer for original element nodes, after every mirror/layout/recipe
/// write. A legacy Animation component or clip is never installed or sampled on the clone.</summary>
internal sealed class RemoteElementRenderedHierarchy
{
    private readonly NativeElementRenderBinding _source;
    private readonly NativeElementRenderBinding.Node[] _targets;
    private readonly bool _required;

    internal RemoteElementRenderedHierarchy(NativeElementBindings bindings, RemoteWidgetMirror mirror)
    {
        _source = bindings.Render; _required = bindings.RequiresRenderedHierarchy;
        _targets = new NativeElementRenderBinding.Node[_source.Nodes.Length];
        for (int i = 0; i < _targets.Length; i++)
        {
            Transform target = mirror.CloneOf(_source.Nodes[i].Source)
                ?? throw new InvalidOperationException("original rendered element node has no clone mapping");
            _targets[i] = new NativeElementRenderBinding.Node { Source = target, Rect = target as RectTransform,
                Graphic = target.GetComponent<Graphic>(), Renderer = target.GetComponent<CanvasRenderer>(),
                Group = target.GetComponent<CanvasGroup>(), Image = target.GetComponent<Image>(),
                Raw = target.GetComponent<RawImage>(), Text = target.GetComponent<TMP_Text>() };
            _targets[i].Components = NativeElementRenderBinding.ComponentFlags(_targets[i]);
        }
    }

    internal void Validate(NativeElementRenderState? state)
    {
        if (state == null)
        {
            if (_required) throw new InvalidOperationException("legacy element animation requires rendered hierarchy record53");
            return; // old52 frame remains meaningful for supported original LeanTween recipes
        }
        if (state.Nodes.Length != _targets.Length) throw new InvalidOperationException("owner element hierarchy node count differs from original");
        for (int i = 0; i < state.Nodes.Length; i++)
        {
            NativeElementRenderNode node = state.Nodes[i];
            if (node.Parent != _source.Nodes[i].Parent || node.Binding != _source.Nodes[i].Binding
                || (node.Flags & NativeElementRenderNode.Components) != _targets[i].Components
                || !_source.HasSprite(node.Sprite) || !_source.HasSprite(node.OverrideSprite))
                throw new InvalidOperationException("owner element hierarchy target/component/art binding differs from original");
        }
    }

    internal void Apply(NativeElementRenderState? from, NativeElementRenderState? to, float t)
    {
        if (from == null && to == null) return;
        from ??= to; to ??= from;
        NativeElementRenderState discrete = t < 1f ? from! : to!;
        // Set sibling order in ascending rank; moving an earlier sibling changes later indices.
        for (int order = 0; order < NativeElementRenderState.NodesMax; order++)
            for (int i = 0; i < _targets.Length; i++)
                if (discrete.Nodes[i].Sibling == order) _targets[i].Source.SetSiblingIndex(order);
        for (int i = 0; i < _targets.Length; i++)
        {
            NativeElementRenderBinding.Node target = _targets[i];
            NativeElementRenderNode a = from!.Nodes[i], b = to!.Nodes[i], d = discrete.Nodes[i];
            bool active = (d.Flags & NativeElementRenderNode.Active) != 0;
            if (target.Source.gameObject.activeSelf != active) target.Source.gameObject.SetActive(active);
            float[] x = a.Geometry, y = b.Geometry;
            float L(int n) => Mathf.LerpUnclamped(x[n], y[n], t);
            if (target.Rect != null)
            {
                target.Rect.anchorMin = new Vector2(L(10), L(11)); target.Rect.anchorMax = new Vector2(L(12), L(13));
                target.Rect.pivot = new Vector2(L(14), L(15)); target.Rect.sizeDelta = new Vector2(L(16), L(17));
                target.Rect.anchoredPosition3D = new Vector3(L(0), L(1), L(2));
            }
            else target.Source.localPosition = new Vector3(L(0), L(1), L(2));
            target.Source.localScale = new Vector3(L(3), L(4), L(5));
            var qa = new Quaternion(x[6], x[7], x[8], x[9]); var qb = new Quaternion(y[6], y[7], y[8], y[9]);
            target.Source.localRotation = t <= 0f ? qa : t >= 1f ? qb : Quaternion.SlerpUnclamped(qa, qb, t);
            if (target.Group != null)
            { target.Group.enabled = (d.Flags & NativeElementRenderNode.GroupEnabled) != 0;
                target.Group.alpha = Mathf.LerpUnclamped(a.GroupAlpha, b.GroupAlpha, t); target.Group.ignoreParentGroups = (d.Flags & NativeElementRenderNode.IgnoreParentGroups) != 0; }
            if (target.Graphic != null)
            {
                target.Graphic.enabled = (d.Flags & NativeElementRenderNode.GraphicEnabled) != 0;
                Color color = Color.LerpUnclamped(ColorOf(a.Color), ColorOf(b.Color), t);
                if (target.Graphic.color != color) target.Graphic.color = color;
            }
            if (target.Image != null)
            {
                target.Image.fillAmount = Mathf.LerpUnclamped(a.Fill, b.Fill, t);
                Sprite? sprite = _source.SpriteAt(d.Sprite);
                if (target.Image.sprite != sprite) target.Image.sprite = sprite;
                // Preserve both native public properties: the base sprite can determine native
                // image sizing while a different override supplies the actually drawn artwork.
                target.Image.overrideSprite = _source.SpriteAt(d.OverrideSprite);
            }
            if (target.Raw != null)
                target.Raw.uvRect = new Rect(Mathf.LerpUnclamped(a.Uv[0], b.Uv[0], t), Mathf.LerpUnclamped(a.Uv[1], b.Uv[1], t),
                    Mathf.LerpUnclamped(a.Uv[2], b.Uv[2], t), Mathf.LerpUnclamped(a.Uv[3], b.Uv[3], t));
            if (target.Text != null)
            { target.Text.enableAutoSizing = false; target.Text.fontSize = Mathf.LerpUnclamped(a.FontSize, b.FontSize, t); }
            // Graphic.color and CanvasRenderer color are separate native multipliers. CrossFade
            // writes the latter; copying the Graphic alone loses a fade despite matching RGBA.
            if (target.Renderer != null) target.Renderer.SetColor(Color.LerpUnclamped(ColorOf(a.RendererColor), ColorOf(b.RendererColor), t));
        }
    }
    private static Color ColorOf(float[] value) => new(value[0], value[1], value[2], value[3]);
}
