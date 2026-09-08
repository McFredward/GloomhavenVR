using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net;

/// <summary>Applies owner-rendered values to original native clone targets after all mirror and
/// layout writes. Native gameplay behaviours stay stripped; this never plays a GUIAnimator.</summary>
internal sealed class RemoteUseBarAnimation
{
    private readonly NativeUseBarAnimationBinding[] _source;
    private Target[] _targets = Array.Empty<Target>();
    private readonly Dictionary<Graphic, Material> _materials = new();
    private bool _refused;
    private static readonly ConditionalWeakTable<RemoteAvatar, Playback[]> Playbacks = new();

    private sealed class Playback
    {
        internal int ActorId;
        internal ushort SlotIdentity;
        internal bool Initialized;
        internal readonly UseBarAnimationPlaybackClock Clock = new();
        internal float Boundary = float.NegativeInfinity;
    }

    private RemoteUseBarAnimation(NativeUseBarAnimationBinding[] source) => _source = source;

    internal static RemoteUseBarAnimation Capture(UIUseActiveBonus source) =>
        new(NativeUseBarAnimationBinding.Capture(source));

    internal RemoteUseBarAnimation Map(RemoteWidgetMirror mirror)
    {
        var result = new RemoteUseBarAnimation(_source) { _targets = new Target[_source.Length] };
        for (int i = 0; i < _source.Length; i++)
            result._targets[i] = new Target(_source[i], mirror);
        return result;
    }

    internal void Apply(RemoteAvatar owner, int actorId, ushort slotIdentity, int slot)
    {
        if (!Playbacks.TryGetValue(owner, out Playback[] clocks))
        {
            clocks = new Playback[NetProtocol.UseBarsMaxSlots];
            for (int i = 0; i < clocks.Length; i++) clocks[i] = new Playback();
            Playbacks.Add(owner, clocks);
        }
        Playback clock = clocks[slot];
        UseBarAnimationState? latest = State(owner.AnimationStates, slot, actorId, slotIdentity);
        if (latest == null || actorId == 0 || slotIdentity == UseBarSlotIdentity.NoIdentity)
        {
            clock.Initialized = false;
            return;
        }
        // Rebuilds map new original targets but retain this bounded per-owner/slot clock. A
        // picker/layout rebuild must not restart an already consumed opening animation.
        List<UseBarAnimationSnapshot> history = owner.AnimationHistory;
        int first = history.Count;
        float boundary = float.NegativeInfinity;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (State(history[i].States, slot, actorId, slotIdentity) == null)
            { boundary = history[i].SampleTime; break; }
            first = i;
        }
        float now = Time.unscaledTime;
        if (!clock.Initialized || clock.ActorId != actorId || clock.SlotIdentity != slotIdentity
            || boundary > clock.Boundary)
        {
            clock.Initialized = true; clock.ActorId = actorId; clock.SlotIdentity = slotIdentity;
            clock.Clock.Reset(first < history.Count ? history[first].SampleTime : owner.AnimationSampleTime, now);
            clock.Boundary = boundary;
        }
        // Start at the first retained picture if the original widget only became available after
        // its opening had already been received. Play source-time intervals at 1x, and hold the
        // final received picture. No extrapolation, guessed easing, or duplicate-packet restart.
        float renderTime = clock.Clock.Advance(now, owner.AnimationSampleTime);
        UseBarAnimationState from = latest, to = latest;
        float fromTime = owner.AnimationSampleTime, toTime = fromTime;
        for (int i = first; i < history.Count; i++)
        {
            UseBarAnimationSnapshot frame = history[i];
            UseBarAnimationState? state = State(frame.States, slot, actorId, slotIdentity);
            if (state == null) continue;
            if (i == first || frame.SampleTime <= renderTime)
            { from = state; fromTime = frame.SampleTime; to = state; toTime = fromTime; }
            if (frame.SampleTime > renderTime)
            { to = state; toTime = frame.SampleTime; break; }
        }
        if (!Matches(from) || !Matches(to))
        {
            if (!_refused)
            {
                _refused = true;
                VRLog.Warn("Net", $"USE BAR ANIMATION: slot {slot} descriptor does not match original prefab settings; no foreign target receives a write.");
            }
            return;
        }
        _refused = false;
        float progress = clock.Clock.Progress(fromTime, toTime);
        foreach (Target target in _targets)
            target.Apply(progress, Find(from, target.Source.SettingIndex)!.Values,
                Find(to, target.Source.SettingIndex)!.Values, _materials);
    }

    private static UseBarAnimationState? State(UseBarAnimationState[]? states, int slot, int actorId, ushort identity)
    {
        if (states == null) return null;
        foreach (UseBarAnimationState state in states)
            if (state.Slot == slot && state.ActorId == actorId && state.SlotIdentity == identity) return state;
        return null;
    }

    private bool Matches(UseBarAnimationState state)
    {
        if (!state.Validate() || state.Entries.Length != _targets.Length) return false;
        foreach (Target target in _targets)
        {
            UseBarAnimationValue? value = Find(state, target.Source.SettingIndex);
            if (value == null || value.Kind != target.Source.Kind || value.Values.Length != target.Source.Components)
                return false;
        }
        return true;
    }

    private static UseBarAnimationValue? Find(UseBarAnimationState state, byte index)
    {
        foreach (UseBarAnimationValue value in state.Entries)
            if (value.SettingIndex == index) return value;
        return null;
    }

    internal void RestoreGeometry()
    {
        // Fitting uses the inactive native stage's resting geometry. Intermediate positions and
        // scales belong only to the final visible pose, never to the host's metres-per-pixel fit.
        foreach (Target target in _targets) target.RestoreGeometry();
    }

    internal void Destroy()
    {
        foreach (Material material in _materials.Values) if (material != null) Object.Destroy(material);
        _materials.Clear();
        foreach (Target target in _targets) target.Destroy();
    }

    private sealed class Target
    {
        internal readonly NativeUseBarAnimationBinding Source;
        private readonly RectTransform _rect;
        private readonly CanvasGroup? _group;
        private readonly Graphic? _graphic;
        private readonly TMP_Text? _text;
        private readonly RawImage? _raw;
        private readonly Image? _image;
        private readonly TMP_Text[] _texts;
        private Material? _textMaterial;
        private float _materialProgress = float.NaN;

        internal Target(NativeUseBarAnimationBinding source, RemoteWidgetMirror mirror)
        {
            Source = source;
            Transform target = mirror.CloneOf(source.Target)
                ?? throw new InvalidOperationException("original animation target has no clone mapping");
            _texts = new TMP_Text[source.TextTargets.Length];
            for (int i = 0; i < _texts.Length; i++)
                _texts[i] = mirror.CloneOf(source.TextTargets[i].transform)?.GetComponent<TMP_Text>()
                    ?? throw new InvalidOperationException("original animated text has no clone mapping");
            _rect = (RectTransform)target;
            _group = target.GetComponent<CanvasGroup>();
            _graphic = target.GetComponent<Graphic>();
            _text = target.GetComponent<TMP_Text>();
            _raw = target.GetComponent<RawImage>();
            _image = target.GetComponent<Image>();
        }

        internal void Apply(float t, float[] from, float[] to, Dictionary<Graphic, Material> materials)
        {
            float x = Mathf.LerpUnclamped(from[0], to[0], t);
            float y = to.Length > 1 ? Mathf.LerpUnclamped(from[1], to[1], t) : 0f;
            float z = to.Length > 2 ? Mathf.LerpUnclamped(from[2], to[2], t) : 0f;
            float w = to.Length > 3 ? Mathf.LerpUnclamped(from[3], to[3], t) : 0f;
            Vector3 vector = new(x, y, z);
            Color color = new(x, y, z, w);
            switch (Source.Kind)
            {
                case UseBarAnimationKind.Scale3:
                    if (_rect.localScale != vector) _rect.localScale = vector; break;
                case UseBarAnimationKind.AnchoredPosition3:
                    if (_rect.anchoredPosition3D != vector) _rect.anchoredPosition3D = vector; break;
                case UseBarAnimationKind.LocalPosition3:
                    if (_rect.localPosition != vector) _rect.localPosition = vector; break;
                case UseBarAnimationKind.SizeDelta2:
                case UseBarAnimationKind.CustomSizeDelta2:
                    if (_rect.sizeDelta != (Vector2)vector) _rect.sizeDelta = vector; break;
                case UseBarAnimationKind.UvPosition2:
                    Rect uv = _raw!.uvRect;
                    if (uv.position != (Vector2)vector) { uv.position = vector; _raw.uvRect = uv; } break;
                case UseBarAnimationKind.CanvasGroupAlpha1:
                    if (_group!.alpha != x) _group.alpha = x; break;
                case UseBarAnimationKind.GraphicAlpha1:
                    color = _graphic!.color;
                    if (color.a != x) { color.a = x; _graphic.color = color; } break;
                case UseBarAnimationKind.TextAlpha1:
                    if (_text!.alpha != x) _text.alpha = x; break;
                case UseBarAnimationKind.GraphicColor4:
                    if (_graphic!.color != color) _graphic.color = color; break;
                case UseBarAnimationKind.TextColor4:
                    if (_text!.color != color) _text.color = color; break;
                case UseBarAnimationKind.CustomFill1:
                    if (_image!.fillAmount != x) _image.fillAmount = x; break;
                case UseBarAnimationKind.CustomTextMaterial1:
                    _textMaterial ??= new Material(Source.OriginalMaterial!);
                    if (_materialProgress != x)
                    {
                        _textMaterial.Lerp(Source.OriginalMaterial!, Source.FinalMaterial!, x);
                        _materialProgress = x;
                    }
                    foreach (TMP_Text text in _texts)
                        if (!ReferenceEquals(text.fontSharedMaterial, _textMaterial)) text.fontSharedMaterial = _textMaterial;
                    break;
                case UseBarAnimationKind.MaterialFloat1:
                    // Every material we modify is unique to this mapped clone. Mirror sync may
                    // restore the source material first; rebind our owned copy after that reset.
                    if (!materials.TryGetValue(_graphic!, out Material owned))
                    {
                        owned = new Material(_graphic!.material);
                        materials.Add(_graphic, owned);
                    }
                    if (!ReferenceEquals(_graphic!.material, owned)) _graphic.material = owned;
                    Material material = Source.MaterialForRendering ? _graphic.materialForRendering : owned;
                    if (material != null && material.HasProperty(Source.MaterialProperty)
                        && material.GetFloat(Source.MaterialProperty) != x)
                        material.SetFloat(Source.MaterialProperty, x);
                    break;
            }
        }

        internal void RestoreGeometry()
        {
            if (_rect == null || Source.Rect == null) return;
            switch (Source.Kind)
            {
                case UseBarAnimationKind.Scale3: _rect.localScale = Source.Rect.localScale; break;
                case UseBarAnimationKind.AnchoredPosition3: _rect.anchoredPosition3D = Source.Rect.anchoredPosition3D; break;
                case UseBarAnimationKind.LocalPosition3: _rect.localPosition = Source.Rect.localPosition; break;
                case UseBarAnimationKind.SizeDelta2:
                case UseBarAnimationKind.CustomSizeDelta2: _rect.sizeDelta = Source.Rect.sizeDelta; break;
            }
        }

        internal void Destroy()
        {
            if (_textMaterial != null) Object.Destroy(_textMaterial);
            _textMaterial = null;
        }
    }
}
