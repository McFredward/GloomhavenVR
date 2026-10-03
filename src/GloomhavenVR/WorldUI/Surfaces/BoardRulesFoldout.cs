using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>Original rules always occupy their original column. Long prose has a native ellipsis
/// preview; hovering expands temporarily and clicking pins it. The owner alone authors the
/// animated occupied height and native row geometry, including every intermediate peer frame.</summary>
internal sealed class BoardRulesFoldout : IDisposable
{
    internal const float Density = 1440f;
    private const float TransitionSeconds = .16f;
    private readonly RectTransform _host, _content, _viewport;
    private readonly GameObject _wrapper;
    private readonly Image _hit;
    private readonly Button _button;
    private readonly CanvasGroup _viewportGroup;
    private readonly Quaternion _originalRotation;
    private readonly Vector3 _originalScale;
    private readonly Vector2 _originalHostPivot, _originalHostSize;
    private readonly Vector3 _originalHostPosition, _originalHostScale;
    private readonly bool _owner;
    private readonly RectTransform _originalParent;
    private readonly Vector2 _originalAnchorMin, _originalAnchorMax, _originalPivot, _originalSize;
    private readonly Vector3 _originalPosition;
    private readonly List<TextExtent> _extents = new();
    private readonly Dictionary<TMP_Text, TextOverflowModes> _originalOverflow = new();
    private readonly Dictionary<TMP_Text, string> _originalText = new(), _paintedText = new();
    private readonly Dictionary<Behaviour, bool> _layouts = new();
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly List<TMP_Text> _retiredTexts = new();
    private readonly List<Behaviour> _retiredLayouts = new();
    private float _compactWidth, _height, _shownHeight;
    private float _transitionFrom, _transitionTarget = -1f, _transitionProgress;
    private float _recheckAt;
    private ulong _contentStamp;
    private int _tickedFrame = -1;
    private bool _overflow, _expanded, _hover, _pinned;
    private sealed class TextExtent
    {
        internal TMP_Text Text = null!;
        internal Vector3 Position;
        internal float Top, Height, FirstLineBottom;
    }
    internal float OccupiedMeters => _shownHeight / Density;
    internal RectTransform Content => _content;
    internal RectTransform VisualRoot => (RectTransform)_wrapper.transform;
    internal bool Overflow => _overflow;
    internal bool Expanded => _expanded;

    internal BoardRulesFoldout(RectTransform host, RectTransform content, bool owner)
    {
        _host = host; _content = content; _owner = owner;
        _originalHostPivot = host.pivot; _originalHostSize = host.sizeDelta; _originalHostPosition = host.localPosition; _originalHostScale = host.localScale;
        _host.pivot = Vector2.one;
        _originalParent = (RectTransform)content.parent;
        _originalAnchorMin = content.anchorMin; _originalAnchorMax = content.anchorMax;
        _originalPivot = content.pivot; _originalSize = content.sizeDelta; _originalPosition = content.anchoredPosition3D;
        _originalRotation = content.localRotation; _originalScale = content.localScale;
        _wrapper = new GameObject("GloomhavenVR.RulesFoldout", typeof(RectTransform));
        var frame = (RectTransform)_wrapper.transform; frame.SetParent(host, false);
        frame.anchorMin = frame.anchorMax = frame.pivot = Vector2.one; frame.anchoredPosition = Vector2.zero; frame.sizeDelta = Vector2.zero;
        _viewport = (RectTransform)new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(CanvasGroup)).transform;
        _viewport.SetParent(frame, false); _viewport.anchorMin = _viewport.anchorMax = _viewport.pivot = Vector2.one;
        _viewport.anchoredPosition = Vector2.zero;
        _viewportGroup = _viewport.GetComponent<CanvasGroup>();
        _hit = _viewport.gameObject.AddComponent<Image>(); _hit.color = Color.clear; _hit.raycastTarget = owner;
        _button = _viewport.gameObject.AddComponent<Button>(); _button.targetGraphic = _hit;
        _button.transition = Selectable.Transition.None; _button.interactable = owner;
        content.SetParent(_viewport, false); content.anchorMin = content.anchorMax = content.pivot = Vector2.one;
        content.localRotation = Quaternion.identity; content.localScale = Vector3.one;
        if (owner)
        {
            _button.onClick.AddListener(Toggle);
            var hover = _viewport.gameObject.AddComponent<BoardRulesHover>(); hover.Changed = SetHover;
        }
        foreach (Transform child in _wrapper.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = host.gameObject.layer;
    }
    internal void Toggle()
    {
        if (!_owner || !_overflow) return;
        _pinned = !_pinned; _expanded = _pinned || _hover;
    }
    internal void SetHover(bool hover)
    {
        if (!_owner) return;
        _hover = hover; _expanded = _overflow && (_pinned || hover);
    }
    internal void Tick(float compactWidthMeters, float delta)
    {
        if (!_owner || _tickedFrame == Time.frameCount) return;
        _tickedFrame = Time.frameCount;
        float compact = Mathf.Max(.12f, compactWidthMeters) * Density;
        if (Time.unscaledTime >= _recheckAt || Mathf.Abs(compact - _compactWidth) > .5f)
        {
            _recheckAt = Time.unscaledTime + .25f;
            TMP_Text[] texts = _content.GetComponentsInChildren<TMP_Text>(true);
            ulong stamp = 1469598103934665603UL;
            foreach (TMP_Text t in texts)
            {
                stamp = (stamp ^ (uint)t.GetInstanceID()) * 1099511628211UL;
                foreach (char c in OriginalText(t)) stamp = (stamp ^ c) * 1099511628211UL;
                stamp = (stamp ^ (uint)(t.fontSize * 100f)) * 1099511628211UL;
                stamp = (stamp ^ (t.gameObject.activeSelf ? 1UL : 0UL)) * 1099511628211UL;
            }
            if (stamp != _contentStamp || Mathf.Abs(compact - _compactWidth) > .5f)
            {
                _contentStamp = stamp; _compactWidth = compact;
                RestoreTextExtents();
                _retiredTexts.Clear();
                foreach (var text in _originalOverflow) if (text.Key == null) _retiredTexts.Add(text.Key!);
                foreach (TMP_Text text in _retiredTexts)
                { _originalOverflow.Remove(text); _originalText.Remove(text); _paintedText.Remove(text); }
                foreach (TMP_Text text in texts)
                {
                    if (!_originalOverflow.ContainsKey(text)) _originalOverflow.Add(text, text.overflowMode);
                    _originalText[text] = text.text;
                }
                CollectLayouts();
                foreach (var layout in _layouts) if (layout.Key != null) layout.Key.enabled = layout.Value;
                _content.sizeDelta = new Vector2(compact, _content.sizeDelta.y);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                LayoutContentHeight.Apply(_content, out _, out float height);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                bool hasText = false;
                foreach (TMP_Text text in texts) hasText |= text.gameObject.activeSelf && !string.IsNullOrWhiteSpace(text.text);
                _height = hasText ? Mathf.Max(1f, height) : 0f;
                if (!hasText) _shownHeight = 0f;
                _extents.Clear();
                foreach (TMP_Text text in texts)
                {
                    text.rectTransform.GetWorldCorners(_corners);
                    float top = -_content.InverseTransformPoint(_corners[1]).y;
                    text.ForceMeshUpdate(true);
                    float firstLineBottom = text.textInfo.lineCount > 0
                        ? text.rectTransform.rect.yMax - text.textInfo.lineInfo[0].descender : text.fontSize;
                    _extents.Add(new TextExtent { Text = text, Position = text.rectTransform.anchoredPosition3D,
                        Top = top, Height = text.rectTransform.rect.height, FirstLineBottom = Mathf.Max(1f, firstLineBottom) });
                }
                // A later Canvas rebuild must not put a truncated row back to full preferred
                // height. Native layout runs again only when the actual row content changes.
                foreach (var layout in _layouts) if (layout.Key != null) layout.Key.enabled = false;
                bool wasOverflow = _overflow;
                _overflow = _height > ScenarioRulesSurface.RulesBudgetMeters * Density;
                if (!_overflow || !wasOverflow) _pinned = false;
                _expanded = _overflow && (_pinned || _hover);
                if (_shownHeight <= 0f) _shownHeight = Mathf.Min(_height, ScenarioRulesSurface.RulesBudgetMeters * Density);
            }
        }
        float target = _expanded ? _height : Mathf.Min(_height, ScenarioRulesSurface.RulesBudgetMeters * Density);
        if (Mathf.Abs(target - _transitionTarget) > .1f)
        { _transitionFrom = _shownHeight; _transitionTarget = target; _transitionProgress = 0f; }
        _transitionProgress = Mathf.Min(1f, _transitionProgress + Mathf.Max(0f, delta) / TransitionSeconds);
        _shownHeight = Mathf.LerpUnclamped(_transitionFrom, target, Mathf.SmoothStep(0f, 1f, _transitionProgress));
        Present(_compactWidth, _shownHeight, _height);
        ApplyTextExtents(_shownHeight);
    }
    private void CollectLayouts()
    {
        _retiredLayouts.Clear();
        foreach (var layout in _layouts) if (layout.Key == null) _retiredLayouts.Add(layout.Key!);
        foreach (Behaviour layout in _retiredLayouts) _layouts.Remove(layout);
        foreach (LayoutGroup layout in _content.GetComponentsInChildren<LayoutGroup>(true))
            if (!_layouts.ContainsKey(layout)) _layouts.Add(layout, layout.enabled);
        foreach (ContentSizeFitter layout in _content.GetComponentsInChildren<ContentSizeFitter>(true))
            if (!_layouts.ContainsKey(layout)) _layouts.Add(layout, layout.enabled);
    }
    private string OriginalText(TMP_Text text) => _paintedText.TryGetValue(text, out string painted)
        && text.text == painted && _originalText.TryGetValue(text, out string original) ? original : text.text;
    private void ApplyTextExtents(float shown)
    {
        TextExtent? last = null;
        if (_overflow && shown + .5f < _height)
            foreach (TextExtent frame in _extents)
                if (frame.Text != null && frame.Text.gameObject.activeSelf && shown - frame.Top >= frame.FirstLineBottom)
                    if (last == null || frame.Top > last.Top) last = frame;
        foreach (TextExtent frame in _extents)
        {
            if (frame.Text == null) continue;
            RectTransform rect = frame.Text.rectTransform;
            float visible = Mathf.Clamp(shown - frame.Top, 0f, frame.Height);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, visible);
            rect.anchoredPosition3D = frame.Position + Vector3.up * ((frame.Height - visible) * (1f - rect.pivot.y));
            // If a preview ends between paragraphs, TMP would otherwise show one complete row
            // with no indication that later rows were clipped. The original last visible widget
            // receives only the requested ellipsis suffix; its canonical prose is retained and
            // restored before native layout, expansion, source changes or release.
            string prose = _originalText[frame.Text] + (ReferenceEquals(frame, last) ? "..." : string.Empty);
            if (frame.Text.text != prose) frame.Text.text = prose;
            _paintedText[frame.Text] = prose;
            frame.Text.overflowMode = visible + .5f < frame.Height || ReferenceEquals(frame, last)
                ? TextOverflowModes.Ellipsis : _originalOverflow[frame.Text];
        }
    }
    private void RestoreTextExtents()
    {
        foreach (TextExtent frame in _extents)
        {
            if (frame.Text == null) continue;
            frame.Text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, frame.Height);
            frame.Text.rectTransform.anchoredPosition3D = frame.Position;
        }
        foreach (var text in _originalOverflow)
        {
            if (text.Key == null) continue;
            text.Key.overflowMode = text.Value;
            if (_paintedText.TryGetValue(text.Key, out string painted) && text.Key.text == painted
                && _originalText.TryGetValue(text.Key, out string original)) text.Key.text = original;
        }
    }
    private void Present(float width, float shown, float height)
    {
        _viewport.sizeDelta = new Vector2(width, shown);
        _viewportGroup.alpha = shown > .5f ? 1f : 0f;
        _viewportGroup.blocksRaycasts = _owner && _overflow;
        _viewportGroup.interactable = _owner && _overflow;
        _button.interactable = _owner && _overflow; _hit.raycastTarget = _owner && _overflow;
        _content.sizeDelta = new Vector2(width, height); _content.anchoredPosition3D = Vector3.zero;
        _host.sizeDelta = new Vector2(width, shown);
    }
    internal Vector3 HostPositionIn(Transform mount) => mount.InverseTransformPoint(_host.position);
    internal void CaptureFrame(NativeBoardRulesState state)
    {
        state.Visible = _host.gameObject.activeInHierarchy && _height > .5f; state.Overflow = _overflow; state.Expanded = _expanded; state.Hover = _hover;
        state.Caption = Loc.BoardRulesCaption;
        Array.Clear(state.Header, 0, state.Header.Length);
        float[] f = state.Frame; Vector3 p = _host.localPosition;
        f[0] = p.x; f[1] = p.y; f[2] = p.z; f[3] = _compactWidth; f[4] = _shownHeight;
        f[5] = _compactWidth; f[6] = _height; f[7] = 0f;
        f[8] = _compactWidth; f[9] = Mathf.Min(_height, ScenarioRulesSurface.RulesBudgetMeters * Density);
        f[10] = 1f / Density; f[12] = OccupiedMeters;
    }
    internal void Apply(NativeBoardRulesState state)
    {
        _overflow = state.Overflow; _expanded = state.Expanded; _hover = state.Hover;
        float[] f = state.Frame; _compactWidth = f[8]; _height = f[6]; _shownHeight = f[4];
        Present(f[3], f[4], f[6]);
        _host.localPosition = new Vector3(f[0], f[1], f[2]); _host.localScale = Vector3.one * f[10];
        _host.gameObject.SetActive(state.Visible);
    }
    public void Dispose()
    {
        RestoreTextExtents();
        foreach (var layout in _layouts) if (layout.Key != null) layout.Key.enabled = layout.Value;
        if (_content != null && _originalParent != null)
        {
            _content.SetParent(_originalParent, false); _content.anchorMin = _originalAnchorMin; _content.anchorMax = _originalAnchorMax;
            _content.pivot = _originalPivot; _content.sizeDelta = _originalSize; _content.anchoredPosition3D = _originalPosition;
            _content.localRotation = _originalRotation; _content.localScale = _originalScale;
        }
        if (_host != null) { _host.pivot = _originalHostPivot; _host.sizeDelta = _originalHostSize; _host.localPosition = _originalHostPosition; _host.localScale = _originalHostScale; }
        if (_wrapper != null) { _wrapper.SetActive(false); Object.Destroy(_wrapper); }
    }
}
internal sealed class BoardRulesHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    internal Action<bool>? Changed;
    public void OnPointerEnter(PointerEventData data) => Changed?.Invoke(true);
    public void OnPointerExit(PointerEventData data) => Changed?.Invoke(false);
    private void OnDisable() => Changed?.Invoke(false);
}
