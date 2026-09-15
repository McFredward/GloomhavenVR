using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Reflow the original HelpText and its original plate without shrinking glyphs. Build 507
/// resized the LabelArea parent, but the native plate is its BG child, beside HelpText (507
/// hardware log: BG 820x43, HelpText 800x23). The untouched BG and desktop text offsets made
/// a wide, disjoint union, so the composite still shrank everything (ausserhalb_der_box.jpg).
/// Own both sibling rects while parked and restore every changed property on handback. This
/// helper is only called for HelpText, never illustrated/page-based message boxes.
/// </summary>
internal sealed class HintTextReflow
{
    private TMP_Text? _text;
    private RectTransform? _box, _border;
    private Vector2 _boxSize;
    private RectState _textState, _borderState;
    private bool _wrap, _auto;
    private float _fontSize, _horizontalPadding, _verticalPadding, _authoredWidth;
    private ContentSizeFitter? _boxFitter, _textFitter, _borderFitter;
    private bool _boxFitterEnabled, _textFitterEnabled, _borderFitterEnabled;
    private LayoutGroup? _layout;
    private bool _layoutEnabled;
    private string? _lastText;
    private float _lastWidth;

    private struct RectState
    {
        private Vector2 _size, _min, _max, _pivot;
        private Vector3 _position;
        internal RectState(RectTransform rect)
        {
            _size = rect.sizeDelta;
            _min = rect.anchorMin;
            _max = rect.anchorMax;
            _pivot = rect.pivot;
            _position = rect.anchoredPosition3D;
        }
        internal void Restore(RectTransform rect)
        {
            rect.anchorMin = _min;
            rect.anchorMax = _max;
            rect.pivot = _pivot;
            rect.sizeDelta = _size;
            rect.anchoredPosition3D = _position;
        }
    }

    internal void Apply(TMP_Text text, float availableWidth)
    {
        if (text == null || text.rectTransform.parent is not RectTransform box || availableWidth < 8f)
            return;
        if (!ReferenceEquals(_text, text))
        {
            Restore();
            // Resolve the observed native sibling by exact path and component, not an arbitrary
            // image somewhere in the message (which could be a dimmer or an illustrated page).
            RectTransform? border = box.Find("BG") as RectTransform;
            if (border == null || border.GetComponent<Graphic>() == null)
                return;
            _text = text;
            _box = box;
            _border = border;
            _boxSize = box.sizeDelta;
            _authoredWidth = border.rect.width;
            _textState = new RectState(text.rectTransform);
            _borderState = new RectState(border);
            _wrap = text.enableWordWrapping;
            _auto = text.enableAutoSizing;
            _fontSize = text.fontSize;
            _horizontalPadding = Mathf.Max(0f, border.rect.width - text.rectTransform.rect.width);
            _verticalPadding = Mathf.Max(0f, border.rect.height - text.rectTransform.rect.height);
            _boxFitter = box.GetComponent<ContentSizeFitter>();
            _textFitter = text.GetComponent<ContentSizeFitter>();
            _borderFitter = border.GetComponent<ContentSizeFitter>();
            _layout = box.GetComponent<LayoutGroup>();
            _boxFitterEnabled = _boxFitter != null && _boxFitter.enabled;
            _textFitterEnabled = _textFitter != null && _textFitter.enabled;
            _borderFitterEnabled = _borderFitter != null && _borderFitter.enabled;
            _layoutEnabled = _layout != null && _layout.enabled;
        }
        availableWidth = Mathf.Min(availableWidth, _authoredWidth);
        if (_lastText == text.text && Mathf.Abs(_lastWidth - availableWidth) < 0.5f)
            return;
        _lastText = text.text;
        _lastWidth = availableWidth;
        if (_boxFitter != null) _boxFitter.enabled = false;
        if (_textFitter != null) _textFitter.enabled = false;
        if (_borderFitter != null) _borderFitter.enabled = false;
        if (_layout != null) _layout.enabled = false;
        text.enableWordWrapping = true;
        text.enableAutoSizing = false;
        text.fontSize = _fontSize;
        // TMP supplies the native localized/rich-text metrics. The border follows the resulting
        // height, and centered anchors remove the desktop offsets left by the disabled layout.
        float textWidth = Mathf.Max(8f, availableWidth - _horizontalPadding);
        float height = text.GetPreferredValues(text.text, textWidth, float.PositiveInfinity).y;
        box.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, availableWidth);
        box.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height + _verticalPadding);
        Seat(_border!, availableWidth, height + _verticalPadding);
        Seat(text.rectTransform, textWidth, height);
        text.ForceMeshUpdate();
    }

    private static void Seat(RectTransform rect, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        Vector3 position = rect.anchoredPosition3D;
        rect.anchoredPosition3D = new Vector3(0f, 0f, position.z);
        rect.sizeDelta = new Vector2(width, height);
    }

    internal void Restore()
    {
        if (_text != null)
        {
            _text.enableWordWrapping = _wrap;
            _text.enableAutoSizing = _auto;
            _text.fontSize = _fontSize;
            _textState.Restore(_text.rectTransform);
        }
        if (_border != null) _borderState.Restore(_border);
        if (_box != null) _box.sizeDelta = _boxSize;
        if (_boxFitter != null) _boxFitter.enabled = _boxFitterEnabled;
        if (_textFitter != null) _textFitter.enabled = _textFitterEnabled;
        if (_borderFitter != null) _borderFitter.enabled = _borderFitterEnabled;
        if (_layout != null) _layout.enabled = _layoutEnabled;
        _text = null;
        _box = _border = null;
        _boxFitter = _textFitter = _borderFitter = null;
        _layout = null;
        _lastText = null;
        _lastWidth = 0f;
    }
}
