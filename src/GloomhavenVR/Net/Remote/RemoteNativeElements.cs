using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net;

/// <summary>Owner-rendered original element output, applied after mirror/layout writes. The clone
/// retains original art and target bindings; no gameplay animator or reconstructed recipe runs.</summary>
internal sealed class RemoteNativeElements
{
    private NativeBoardState? _latest;
    private List<NativeBoardState>? _history;
    private readonly UseBarAnimationPlaybackClock _clock = new();
    private uint _generation;
    private int _stamp = -1;
    private Element[] _elements = Array.Empty<Element>();
    private RectTransform? _root;
    private string? _refusal;
    private Vector2 _renderSize, _renderParent;
    private bool _hasRenderFrame;

    internal void SetState(NativeBoardState? state, List<NativeBoardState> history)
    { _latest = state; _history = history; }

    internal void Configure(RemoteWidgetMirror mirror)
    {
        if (_hasRenderFrame && _latest?.Generation == _generation)
        { mirror.SetOwnerFrame(_renderSize, _renderParent); return; }
        float[]? frame = _latest?.Frame;
        mirror.SetOwnerFrame(frame != null ? new Vector2(frame[0], frame[1]) : Vector2.zero,
            frame != null ? new Vector2(frame[2], frame[3]) : Vector2.zero);
    }

    internal bool Apply(RemoteWidgetMirror mirror, InfusionBoardUI source)
    {
        NativeBoardState? latest = _latest;
        if (latest == null || latest.Elements.Length != 6) { _generation = 0; return false; }
        try
        {
            if (_stamp != mirror.RebuildStamp || _elements.Length != 6)
            {
                DestroyBindings();
                _root = mirror.CloneOf(source.transform) as RectTransform
                    ?? throw new InvalidOperationException("original element board has no clone mapping");
                var mapped = new Element[6];
                try
                {
                    for (int i = 0; i < mapped.Length; i++)
                        mapped[i] = new Element(new NativeElementBindings(source.elementsUI[(ElementInfusionBoardManager.EElement)i]), mirror);
                }
                catch { foreach (Element? element in mapped) element?.Destroy(); throw; }
                _elements = mapped; _stamp = mirror.RebuildStamp;
            }
            List<NativeBoardState>? history = _history;
            int first = history?.Count ?? 0;
            if (history != null)
                for (int i = history.Count - 1; i >= 0; i--)
                { if (history[i].Generation != latest.Generation) break; first = i; }
            if (_generation != latest.Generation)
            {
                _generation = latest.Generation;
                _clock.Reset(history != null && first < history.Count ? history[first].SampleTime : latest.SampleTime, Time.unscaledTime);
            }
            float cursor = _clock.Advance(Time.unscaledTime, latest.SampleTime);
            NativeBoardState from = latest, to = latest;
            if (history != null)
                for (int i = first; i < history.Count; i++)
                {
                    NativeBoardState frame = history[i];
                    if (i == first || frame.SampleTime <= cursor) from = to = frame;
                    if (frame.SampleTime > cursor) { to = frame; break; }
                }
            float progress = _clock.Progress(from.SampleTime, to.SampleTime);
            // Validate the COMPLETE frame before any clone field is touched.
            for (int i = 0; i < _elements.Length; i++)
            {
                _elements[i].Validate(from.Elements[i]); _elements[i].Validate(to.Elements[i]);
                _elements[i].Rendered.Validate(from.RenderElements?[i]); _elements[i].Rendered.Validate(to.RenderElements?[i]);
            }
            // Fit the same source-time frame being rendered, not a future newest envelope.
            _renderSize = new Vector2(Mathf.LerpUnclamped(from.Frame[0], to.Frame[0], progress),
                Mathf.LerpUnclamped(from.Frame[1], to.Frame[1], progress));
            _renderParent = new Vector2(Mathf.LerpUnclamped(from.Frame[2], to.Frame[2], progress),
                Mathf.LerpUnclamped(from.Frame[3], to.Frame[3], progress));
            _hasRenderFrame = true;
            mirror.SetOwnerFrame(_renderSize, _renderParent);
            NativeBoardState discrete = progress < 1f ? from : to;
            Rect(_root!, from.Frame, to.Frame, 4, progress);
            for (int order = 0; order < 6; order++)
                for (int i = 0; i < 6; i++)
                    if (discrete.Elements[i].Sibling == order) _elements[i].Root.SetSiblingIndex(order);
            for (int i = 0; i < _elements.Length; i++)
            {
                _elements[i].Apply(from.Elements[i], to.Elements[i], progress);
                _elements[i].Rendered.Apply(from.RenderElements?[i], to.RenderElements?[i], progress);
            }
            _refusal = null; return true;
        }
        catch (Exception e)
        {
            string refusal = e.GetType().Name + ": " + e.Message;
            if (_refusal != refusal)
            { _refusal = refusal; VRLog.Warn("Net", "NATIVE ELEMENT PLAYBACK: original output unavailable: " + refusal); }
            return false;
        }
    }

    private static void Rect(RectTransform target, float[] from, float[] to, int at, float t)
    {
        float L(int i) => Mathf.LerpUnclamped(from[at + i], to[at + i], t);
        target.anchoredPosition3D = new Vector3(L(0), L(1), L(2));
        target.localScale = new Vector3(L(3), L(4), L(5));
        target.sizeDelta = new Vector2(L(6), L(7));
    }
    private void DestroyBindings()
    { foreach (Element element in _elements) element.Destroy(); _elements = Array.Empty<Element>(); _root = null; _stamp = -1; }
    internal void Destroy() { DestroyBindings(); _latest = null; _history = null; _generation = 0; _hasRenderFrame = false; }

    private sealed class Element
    {
        internal readonly RectTransform Root;
        internal readonly RemoteElementRenderedHierarchy Rendered;
        private readonly NativeElementBindings _source;
        private readonly Graphic[] _graphics, _effects;
        private readonly RemoteUseBarAnimation[] _animations;
        private readonly Dictionary<Graphic, Material> _materials = new();
        private readonly Dictionary<Graphic, Material> _materialSources = new();
        internal Element(NativeElementBindings source, RemoteWidgetMirror mirror)
        {
            _source = source;
            Rendered = new RemoteElementRenderedHierarchy(source, mirror);
            Root = mirror.CloneOf(source.Source.transform) as RectTransform
                ?? throw new InvalidOperationException("original element has no clone mapping");
            _graphics = Map(source.Graphics, mirror); _effects = Map(source.Effects, mirror);
            _animations = new RemoteUseBarAnimation[source.Animations.Length];
            try
            {
                for (int a = 0; a < _animations.Length; a++)
                    _animations[a] = RemoteUseBarAnimation.FromBindings(source.Animations[a]).Map(mirror, _materials);
            }
            catch { Destroy(); throw; } // constructor failure never reaches the caller's element array

        }
        private static Graphic[] Map(Graphic[] originals, RemoteWidgetMirror mirror)
        {
            var result = new Graphic[originals.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = mirror.CloneOf(originals[i].transform)?.GetComponent<Graphic>()
                    ?? throw new InvalidOperationException("original element graphic has no clone mapping");
            return result;
        }
        internal void Validate(NativeElementState state)
        {
            if (state.Graphics.Length != _graphics.Length || state.Effects.Length != _effects.Length
                || state.Animations.Length != _animations.Length)
                throw new InvalidOperationException("owner element fields differ from original widget");
            for (int i = 0; i < _graphics.Length; i++) ValidateMaterial(_source.Graphics[i], state.Graphics[i]);
            for (int i = 0; i < _effects.Length; i++) ValidateMaterial(_source.Effects[i], state.Effects[i]);
            for (int a = 0; a < _animations.Length; a++)
            {
                NativeUseBarAnimationBinding[] bindings = _source.Animations[a];
                UseBarAnimationValue[] values = state.Animations[a];
                if (bindings.Length != values.Length) throw new InvalidOperationException("owner element animation count differs from original");
                for (int i = 0; i < bindings.Length; i++)
                    if (bindings[i].SettingIndex != values[i].SettingIndex || bindings[i].Kind != values[i].Kind)
                        throw new InvalidOperationException("owner element animation target differs from original");
            }
        }
        private void ValidateMaterial(Graphic original, NativeElementGraphic state)
        {
            // Pair.Apply skips inactive viewer branches. Their clone can still carry a default
            // or destroyed old material when the owner's frame activates the original effect.
            // Bind the exact original graphic directly; clone state is never an asset source.
            if ((state.Flags & 4) != 0) EffectMaterial(original);
        }
        private Material EffectMaterial(Graphic original)
        {
            Material material = original.material;
            if (material == null || !material.HasProperty("_FXAnim"))
                throw new InvalidOperationException("owner element effect has no original material: element="
                    + _source.Source.elementType + ", graphic=" + original.name + ", shader="
                    + (material != null && material.shader != null ? material.shader.name : "missing"));
            return material;
        }
        internal void Apply(NativeElementState from, NativeElementState to, float t)
        {
            NativeElementState discrete = t < 1f ? from : to;
            Root.gameObject.SetActive((discrete.Flags & 1) != 0);
            Rect(Root, from.Rect, to.Rect, 0, t);
            int state = (discrete.Flags >> 4) & 3;
            if (_graphics[0] is Image image)
            {
                if (state == 2) image.sprite = _source.Source.completeElement;
                else if (state == 3) image.sprite = _source.Source.waningElement;
            }
            for (int i = 0; i < _graphics.Length; i++) Graphic(_graphics[i], _source.Graphics[i], from.Graphics[i], to.Graphics[i], t);
            for (int i = 0; i < _effects.Length; i++) Graphic(_effects[i], _source.Effects[i], from.Effects[i], to.Effects[i], t);
            for (int a = 0; a < _animations.Length; a++)
                _animations[a].ApplyValues(from.Animations[a], to.Animations[a], t);
        }
        private void Graphic(Graphic target, Graphic source, NativeElementGraphic from, NativeElementGraphic to, float t)
        {
            NativeElementGraphic discrete = t < 1f ? from : to;
            target.gameObject.SetActive((discrete.Flags & 1) != 0); target.enabled = (discrete.Flags & 2) != 0;
            target.color = Color.LerpUnclamped(new Color(from.R, from.G, from.B, from.A), new Color(to.R, to.G, to.B, to.A), t);
            if ((discrete.Flags & 4) == 0) return;
            Material original = EffectMaterial(source);
            if (!_materials.TryGetValue(target, out Material material) || material == null
                || !_materialSources.TryGetValue(target, out Material previous) || !ReferenceEquals(previous, original))
            {
                Material replacement = new Material(original);
                if (material != null) Object.Destroy(material);
                material = replacement; _materials[target] = material; _materialSources[target] = original;
            }
            // Pair.Apply restores the viewer source material each tick. Only this clone-owned
            // instance receives owner animation output; the game's original is never written.
            target.material = material;
            material.SetFloat("_FXAnim", Mathf.LerpUnclamped(from.Fx, to.Fx, t));
        }
        internal void Destroy()
        {
            foreach (RemoteUseBarAnimation animation in _animations) animation?.Destroy();
            foreach (Material material in _materials.Values) if (material != null) Object.Destroy(material);
            _materials.Clear(); _materialSources.Clear();
        }
    }
}
