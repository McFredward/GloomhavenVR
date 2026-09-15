using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Reflows the game's single-line HelpText plate at its authored font size before the composite
/// fits it. Build 506 squeezed a desktop-wide sentence into a 304px party column, leaving 34%
/// glyphs (kleiner_hinweis.jpg). Keep the original TMP, border, text and callbacks; only their
/// layout changes. Never apply this to illustrated/page-based message boxes.
/// </summary>
internal sealed class HintTextReflow
{
    private TMP_Text? _text;
    private RectTransform? _box;
    private Vector2 _boxSize, _textSize;
    private bool _wrap, _auto;
    private float _fontSize, _horizontalPadding, _verticalPadding, _authoredWidth;
    private ContentSizeFitter? _boxFitter, _textFitter;
    private bool _boxFitterEnabled, _textFitterEnabled;
    private LayoutGroup? _layout;
    private bool _layoutEnabled;
    private string? _lastText;
    private float _lastWidth;

    internal void Apply(TMP_Text text, float availableWidth)
    {
        if (text == null || text.rectTransform.parent is not RectTransform box || availableWidth < 8f)
            return;
        if (!ReferenceEquals(_text, text))
        {
            Restore();
            _text = text;
            _box = box;
            _boxSize = box.sizeDelta;
            _authoredWidth = box.rect.width;
            _textSize = text.rectTransform.sizeDelta;
            _wrap = text.enableWordWrapping;
            _auto = text.enableAutoSizing;
            _fontSize = text.fontSize;
            _horizontalPadding = Mathf.Max(0f, box.rect.width - text.rectTransform.rect.width);
            _verticalPadding = Mathf.Max(0f, box.rect.height - text.rectTransform.rect.height);
            _boxFitter = box.GetComponent<ContentSizeFitter>();
            _textFitter = text.GetComponent<ContentSizeFitter>();
            _layout = box.GetComponent<LayoutGroup>();
            _boxFitterEnabled = _boxFitter != null && _boxFitter.enabled;
            _textFitterEnabled = _textFitter != null && _textFitter.enabled;
            _layoutEnabled = _layout != null && _layout.enabled;
        }
        availableWidth = Mathf.Min(availableWidth, _authoredWidth);
        if (_lastText == text.text && Mathf.Abs(_lastWidth - availableWidth) < 0.5f)
            return;
        _lastText = text.text;
        _lastWidth = availableWidth;
        if (_boxFitter != null) _boxFitter.enabled = false;
        if (_textFitter != null) _textFitter.enabled = false;
        if (_layout != null) _layout.enabled = false;
        text.enableWordWrapping = true;
        text.enableAutoSizing = false;
        text.fontSize = _fontSize;
        // Preserve native padding. The content height comes from TMP's own rich-text/font metrics,
        // so German sentences, inline glyphs and line breaks retain their native rendering.
        float textWidth = Mathf.Max(8f, availableWidth - _horizontalPadding);
        float height = text.GetPreferredValues(text.text, textWidth, float.PositiveInfinity).y;
        box.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, availableWidth);
        box.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height + _verticalPadding);
        text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        text.ForceMeshUpdate();
    }

    internal void Restore()
    {
        if (_text != null)
        {
            _text.enableWordWrapping = _wrap;
            _text.enableAutoSizing = _auto;
            _text.fontSize = _fontSize;
            _text.rectTransform.sizeDelta = _textSize;
        }
        if (_box != null) _box.sizeDelta = _boxSize;
        if (_boxFitter != null) _boxFitter.enabled = _boxFitterEnabled;
        if (_textFitter != null) _textFitter.enabled = _textFitterEnabled;
        if (_layout != null) _layout.enabled = _layoutEnabled;
        _text = null;
        _box = null;
        _boxFitter = _textFitter = null;
        _layout = null;
        _lastText = null;
        _lastWidth = 0f;
    }
}
