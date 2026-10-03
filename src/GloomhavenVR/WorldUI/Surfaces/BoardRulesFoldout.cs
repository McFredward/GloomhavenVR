using System;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>Presentation-only foldout around the original rules container. Layout never gates a
/// native continuation. The owner animates actual viewport geometry; peers apply those frames.</summary>
internal sealed class BoardRulesFoldout : IDisposable
{
    internal const float Density = 1440f, HeaderMeters = .026f, ExpandedMeters = .48f, GapMeters = .014f, ViewHeightMeters = .28f;
    private readonly RectTransform _host, _content, _viewport, _header;
    private readonly GameObject _wrapper;
    private readonly ScrollRect _scroll;
    private readonly TMP_Text _label;
    private readonly Image _face;
    private readonly CanvasGroup _viewportGroup;
    private readonly Quaternion _originalRotation;
    private readonly Vector3 _originalScale;
    private readonly Vector2 _originalHostPivot, _originalHostSize;
    private readonly Vector3 _originalHostPosition, _originalHostScale;
    private readonly bool _owner;
    private readonly RectTransform _originalParent;
    private readonly Vector2 _originalAnchorMin, _originalAnchorMax, _originalPivot, _originalSize;
    private readonly Vector3 _originalPosition;
    private float _compactWidth, _height, _width, _shownWidth;
    private float _recheckAt;
    private ulong _contentStamp;
    private int _tickedFrame = -1;
    private TMP_Text[] _texts = Array.Empty<TMP_Text>();
    private bool _overflow, _expanded, _hover;
    internal float OccupiedMeters => _overflow ? HeaderMeters : _height / Density;
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
        _viewport = NewRect("Viewport", frame); _viewport.gameObject.AddComponent<RectMask2D>();
        _viewportGroup = _viewport.gameObject.AddComponent<CanvasGroup>();
        var hit = _viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = owner;
        _scroll = frame.gameObject.AddComponent<ScrollRect>(); _scroll.viewport = _viewport; _scroll.content = content;
        _scroll.horizontal = false; _scroll.vertical = true; _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.inertia = false; _scroll.scrollSensitivity = 35f; _scroll.enabled = owner;
        content.SetParent(_viewport, false); content.anchorMin = content.anchorMax = content.pivot = Vector2.one;
        content.localRotation = Quaternion.identity; content.localScale = Vector3.one;
        _header = NewRect("SpecialRules", frame); _face = _header.gameObject.AddComponent<Image>();
        _face.sprite = NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Idle); _face.type = Image.Type.Sliced;
        _face.color = NativeButtonSkin.ColorFor(NativeButtonSkin.FaceState.Idle);
        var label = NewRect("Caption", _header); _label = label.gameObject.AddComponent<TextMeshProUGUI>();
        NativeButtonSkin.ApplyFont(_label); NativeButtonSkin.MakeLabelDepthHonest(_label); _label.fontSize = 20f; _label.alignment = TextAlignmentOptions.Center;
        _label.raycastTarget = false; label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one; label.sizeDelta = Vector2.zero;
        var button = _header.gameObject.AddComponent<Button>(); button.targetGraphic = _face; button.transition = Selectable.Transition.None; button.interactable = owner;
        if (owner)
        {
            button.onClick.AddListener(Toggle);
            var hover = _header.gameObject.AddComponent<BoardRulesHover>(); hover.Changed = SetHover;
        }
        else _face.raycastTarget = false;
        foreach (Transform child in _wrapper.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = host.gameObject.layer;
    }
    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); return rect;
    }
    internal void Toggle() { if (_owner && _overflow) { _expanded = !_expanded; _label.text = Loc.BoardRulesCaption + (_expanded ? "  ›" : "  ‹"); } }
    internal void SetHover(bool hover) { if (_owner) { _hover = hover; _face.color = NativeButtonSkin.ColorFor(hover ? NativeButtonSkin.FaceState.Accent : NativeButtonSkin.FaceState.Idle); } }
    internal void Tick(float compactWidthMeters, float delta)
    {
        if (!_owner || _tickedFrame == Time.frameCount) return;
        _tickedFrame = Time.frameCount;
        float compact = Mathf.Max(.12f, compactWidthMeters) * Density;
        if (Time.unscaledTime >= _recheckAt || Mathf.Abs(compact - _compactWidth) > .5f)
        {
            _recheckAt = Time.unscaledTime + .25f;
            TMP_Text[] text = _content.GetComponentsInChildren<TMP_Text>(true);
            ulong stamp = 1469598103934665603UL;
            foreach (TMP_Text t in text)
            { stamp = (stamp ^ (uint)t.GetInstanceID()) * 1099511628211UL; foreach (char c in t.text) stamp = (stamp ^ c) * 1099511628211UL;
              stamp = (stamp ^ (uint)(t.fontSize * 100f)) * 1099511628211UL; stamp = (stamp ^ (t.gameObject.activeSelf ? 1UL : 0UL)) * 1099511628211UL; }
            if (stamp != _contentStamp || Mathf.Abs(compact - _compactWidth) > .5f)
            {
                _contentStamp = stamp; _texts = text; _compactWidth = compact;
                float shortHeight = Measure(compact);
                bool wasOverflow = _overflow; _overflow = shortHeight > ScenarioRulesSurface.RulesBudgetMeters * Density;
                _width = _overflow ? Mathf.Max(ExpandedMeters * Density, compact) : compact;
                _height = _overflow ? Measure(_width) : shortHeight;
                if (!_overflow || !wasOverflow) { _expanded = false; _scroll.verticalNormalizedPosition = 1f; }
            }
        }
        float target = _overflow ? (_expanded ? _width : 0f) : _width;
        _shownWidth = Mathf.MoveTowards(_shownWidth, target, Mathf.Max(_width, 1f) * delta / .16f);
        if (!_overflow) _shownWidth = _width;
        Present(_compactWidth, _shownWidth, _height, _overflow, _hover, Loc.BoardRulesCaption);
    }
    private float Measure(float width)
    {
        _content.sizeDelta = new Vector2(width, _content.sizeDelta.y);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        LayoutContentHeight.Apply(_content, out _, out float height);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        if (height < 1f) height = _content.rect.height;
        return Mathf.Max(1f, height);
    }
    private void Present(float compact, float shown, float height, bool overflow, bool hover, string caption)
    {
        _header.gameObject.SetActive(overflow); _header.sizeDelta = new Vector2(compact, HeaderMeters * Density);
        _header.anchoredPosition = new Vector2(-compact * .5f, -HeaderMeters * Density * .5f);
        _label.text = caption + (_expanded ? "  ›" : "  ‹");
        _face.color = NativeButtonSkin.ColorFor(hover ? NativeButtonSkin.FaceState.Accent : NativeButtonSkin.FaceState.Idle);
        _viewport.anchorMin = _viewport.anchorMax = _viewport.pivot = new Vector2(1f, 1f);
        _viewport.anchoredPosition = new Vector2(overflow ? -compact - GapMeters * Density : 0f, 0f);
        _viewport.sizeDelta = new Vector2(shown, Mathf.Min(height, ViewHeightMeters * Density));
        // Keep original widgets active: clipping/alpha is presentation, never a native lifecycle write.
        _viewportGroup.alpha = shown > .5f ? 1f : 0f;
        _viewportGroup.blocksRaycasts = _owner && shown > .5f;
        _viewportGroup.interactable = _owner && shown > .5f;
        _content.sizeDelta = new Vector2(_width, height);
        Vector3 p = _content.anchoredPosition3D;
        _content.anchoredPosition3D = new Vector3(0f, Mathf.Clamp(p.y, 0f, Mathf.Max(0f, height - _viewport.rect.height)), 0f);
        _host.sizeDelta = new Vector2(overflow && shown > .5f ? compact + GapMeters * Density + shown : compact,
            overflow && shown > .5f ? Mathf.Max(HeaderMeters * Density, _viewport.rect.height) : overflow ? HeaderMeters * Density : height);
    }
    internal Vector3 HostPositionIn(Transform mount) => mount.InverseTransformPoint(_host.position);
    internal void CaptureFrame(NativeBoardRulesState state)
    {
        state.Visible = _host.gameObject.activeInHierarchy; state.Overflow = _overflow; state.Expanded = _expanded; state.Hover = _hover;
        state.Caption = Loc.BoardRulesCaption;
        Color face = _face.color, caption = _label.color; float[] header = state.Header;
        header[0] = face.r; header[1] = face.g; header[2] = face.b; header[3] = face.a;
        header[4] = caption.r; header[5] = caption.g; header[6] = caption.b; header[7] = caption.a; header[8] = _label.fontSize;
        float[] f = state.Frame; Vector3 p = _host.localPosition;
        f[0] = p.x; f[1] = p.y; f[2] = p.z; f[3] = _shownWidth; f[4] = _viewport.rect.height;
        f[5] = _width; f[6] = _height; f[7] = _content.anchoredPosition.y;
        f[8] = _compactWidth; f[9] = HeaderMeters * Density; f[10] = 1f / Density; f[12] = OccupiedMeters;
    }
    internal void Apply(NativeBoardRulesState state)
    {
        _overflow = state.Overflow; _expanded = state.Expanded; _hover = state.Hover; float[] f = state.Frame;
        _compactWidth = f[8]; _width = f[5]; _height = f[6]; _shownWidth = f[3];
        Present(_compactWidth, _shownWidth, _height, _overflow, _hover, state.Caption);
        float[] header = state.Header;
        _face.color = new Color(header[0], header[1], header[2], header[3]);
        _label.color = new Color(header[4], header[5], header[6], header[7]); _label.fontSize = header[8];
        _viewport.sizeDelta = new Vector2(f[3], f[4]);
        _content.anchoredPosition3D = new Vector3(0f, f[7], 0f);
        _host.localPosition = new Vector3(f[0], f[1], f[2]); _host.localScale = Vector3.one * f[10];
        _host.gameObject.SetActive(state.Visible);
    }
    public void Dispose()
    {
        if (_content != null && _originalParent != null)
        {
            _content.SetParent(_originalParent, false); _content.anchorMin = _originalAnchorMin; _content.anchorMax = _originalAnchorMax;
            _content.pivot = _originalPivot; _content.sizeDelta = _originalSize; _content.anchoredPosition3D = _originalPosition;
            _content.localRotation = _originalRotation; _content.localScale = _originalScale;
        }
        if (_host != null) { _host.pivot = _originalHostPivot; _host.sizeDelta = _originalHostSize; _host.localPosition = _originalHostPosition; _host.localScale = _originalHostScale; }
        // Destroy is deferred until frame end. Retired owned chrome must stop painting immediately
        // when a native row change binds a replacement in this same rendered frame.
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
