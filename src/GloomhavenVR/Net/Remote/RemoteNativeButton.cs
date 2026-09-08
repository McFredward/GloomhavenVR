using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Original ExtendedButton visual recipe without events, audio or selection. Native
/// pointer-down/up scale is immediate; enter/exit uses its authored easeOutExpo duration.</summary>
internal sealed class RemoteNativeButton
{
    private Transform? _scale;
    private RectTransform? _move;
    private Graphic? _graphic;
    private Image? _image;
    private Selectable.Transition _transition;
    private SpriteState _sprites;
    private ColorBlock _colors;
    private Vector2 _movement, _rest;
    private Vector3 _from, _current, _target;
    private float _factor, _seconds, _at, _colorAt;
    private Color _colorFrom, _colorCurrent, _colorTarget;
    private bool _hovered, _pressed, _scaleOwned, _colorSet, _instantScale;

    internal static RemoteNativeButton? Capture(ExtendedButton? source)
    {
        if (source == null) return null;
        Transform scale = source.overridedTargetRectScale != null ? source.overridedTargetRectScale
            : source.targetRect != null ? source.targetRect : source.transform;
        RectTransform move = source.targetRect != null ? source.targetRect : (RectTransform)source.transform;
        return new RemoteNativeButton { _scale = scale, _move = move, _graphic = source.targetGraphic,
            _image = source.targetGraphic as Image, _transition = source.transition, _sprites = source.spriteState,
            _colors = source.colors, _movement = source.hoverMovement, _rest = source.isMoved ? source.hoverStartPosition : move.anchoredPosition,
            _factor = source.highlightScaleFactor, _seconds = source.animateScaling ? source.animationDuration : 0f,
            _from = scale.localScale, _current = scale.localScale, _target = scale.localScale };
    }
    internal RemoteNativeButton Map(RemoteWidgetMirror mirror)
    {
        Transform? scale = mirror.CloneOf(_scale);
        Vector3 resting = scale != null ? scale.localScale : _current;
        return new RemoteNativeButton {
            _scale = scale, _move = mirror.CloneOf(_move) as RectTransform,
            _graphic = _graphic != null ? mirror.CloneOf(_graphic.transform)?.GetComponent<Graphic>() : null,
            _image = _image != null ? mirror.CloneOf(_image.transform)?.GetComponent<Image>() : null,
            _transition = _transition, _sprites = _sprites, _colors = _colors, _movement = _movement, _rest = _rest,
            _factor = _factor, _seconds = _seconds, _from = resting, _current = resting, _target = resting,
        };
    }
    internal void Paint(bool interactable, bool hovered, bool pressed, bool geometry = true)
    {
        float now = Time.unscaledTime;
        if (geometry && (_hovered != hovered || _pressed != pressed))
        {
            bool pressEdge = hovered && (_pressed != pressed);
            _instantScale = pressEdge || _seconds <= 0f;
            _hovered = hovered; _pressed = pressed; _scaleOwned = true;
            _from = _current; _target = Vector3.one * (hovered ? pressed ? (_factor + 1f) * 0.5f : _factor : 1f);
            _target.z = 1f; _at = now;
            if (_move != null && _movement != Vector2.zero)
                _move.anchoredPosition = hovered ? _rest + _movement : _rest;
        }
        if (geometry && _scaleOwned && _scale != null)
        {
            float t = _instantScale ? 1f : Mathf.Clamp01((now - _at) / _seconds);
            _current = Vector3.LerpUnclamped(_from, _target, t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t));
            if (_scale.localScale != _current) _scale.localScale = _current;
            if (_move != null && _movement != Vector2.zero)
                _move.anchoredPosition = _hovered ? _rest + _movement : _rest;
        }
        if (_transition == Selectable.Transition.SpriteSwap && _image != null)
        {
            Sprite? sprite = !interactable ? _sprites.disabledSprite : pressed ? _sprites.pressedSprite
                : hovered ? _sprites.highlightedSprite : null;
            if (ReferenceEquals(sprite, _image.sprite)) sprite = null;
            Sprite? old = _image.overrideSprite != _image.sprite ? _image.overrideSprite : null;
            if (!ReferenceEquals(old, sprite)) _image.overrideSprite = sprite;
        }
        else if (_transition == Selectable.Transition.ColorTint && _graphic != null)
        {
            Color target = (!interactable ? _colors.disabledColor : pressed ? _colors.pressedColor
                : hovered ? _colors.highlightedColor : _colors.normalColor) * _colors.colorMultiplier;
            if (!_colorSet || target != _colorTarget)
            {
                _colorFrom = _colorSet ? _colorCurrent : _graphic.canvasRenderer.GetColor();
                _colorTarget = target; _colorAt = now; _colorSet = true;
            }
            float t = _colors.fadeDuration > 0f ? Mathf.Clamp01((now - _colorAt) / _colors.fadeDuration) : 1f;
            _colorCurrent = Color.Lerp(_colorFrom, _colorTarget, t);
            if (_graphic.canvasRenderer.GetColor() != _colorCurrent) _graphic.canvasRenderer.SetColor(_colorCurrent);
        }
    }
}
