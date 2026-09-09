using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Final owner paint of the original mandatory Image, after native mirror/layout writes.</summary>
internal sealed class RemoteNativeDecisionHighlight
{
    private readonly UseBarAnimationPlaybackClock _clock = new();
    private NativeDecisionHighlightState? _previous, _current;
    private Sprite? _sprite;
    private string? _spriteName, _textureName;
    private float _retryAt;
    private bool _refused;
    private Canvas? _canvas;
    private float _originalReferencePixelsPerUnit;

    internal void Reset()
    {
        if (_canvas != null) _canvas.referencePixelsPerUnit = _originalReferencePixelsPerUnit;
        _canvas = null;
        _previous = _current = null; _sprite = null; _spriteName = _textureName = null;
        _retryAt = 0; _refused = false;
    }

    internal void Apply(GameObject? target, Canvas? canvas, NativeDecisionHighlightState? state)
    {
        if (target == null || canvas == null) return;
        if (state == null) { Reset(); return; }
        if (_canvas != canvas)
        {
            if (_canvas != null) _canvas.referencePixelsPerUnit = _originalReferencePixelsPerUnit;
            _canvas = canvas; _originalReferencePixelsPerUnit = canvas.referencePixelsPerUnit;
        }
        Image? image = target.GetComponent<Image>();
        if (image == null) return;
        float now = Time.unscaledTime;
        if (_current == null || state.SampleTime > _current.SampleTime)
        {
            bool continuous = _current != null && state.SpriteName == _current.SpriteName
                && state.TextureName == _current.TextureName
                && state.SampleTime - _current.SampleTime <= UseBarAnimationPlaybackClock.MaximumContinuousGap;
            _previous = continuous ? _current : state;
            _current = state;
            if (!continuous) _clock.Reset(state.SampleTime, now);
        }
        // Presence duplicates are decoded into fresh DTOs. Their equal timestamp does not restart
        // interpolation; neither does an older redundant packet replace a newer owner picture.
        NativeDecisionHighlightState to = _current!, from = _previous!;
        _clock.Advance(now, to.SampleTime);
        float progress = _clock.Progress(from.SampleTime, to.SampleTime);
        if (!ResolveSprite(image, to, now)) { target.SetActive(false); return; }
        NativeDecisionHighlightState discrete = progress >= 1f ? to : from;
        var rect = image.rectTransform;
        float R(int i) => Mathf.LerpUnclamped(from.Rect[i], to.Rect[i], progress);
        float C(int i) => Mathf.LerpUnclamped(from.Colors[i], to.Colors[i], progress);
        canvas.referencePixelsPerUnit = Mathf.LerpUnclamped(from.ReferencePixelsPerUnit, to.ReferencePixelsPerUnit, progress);
        rect.anchorMin = new Vector2(R(0), R(1)); rect.anchorMax = new Vector2(R(2), R(3));
        rect.pivot = new Vector2(R(4), R(5)); rect.sizeDelta = new Vector2(R(6), R(7));
        rect.anchoredPosition3D = new Vector3(R(8), R(9), R(10));
        rect.localScale = new Vector3(R(11), R(12), R(13));
        rect.localRotation = Quaternion.SlerpUnclamped(new Quaternion(from.Rect[14], from.Rect[15], from.Rect[16], from.Rect[17]),
            new Quaternion(to.Rect[14], to.Rect[15], to.Rect[16], to.Rect[17]), progress);
        image.overrideSprite = _sprite;
        if (_sprite == null) image.sprite = null;
        image.type = (Image.Type)discrete.ImageType;
        image.preserveAspect = (discrete.Flags & 4) != 0; image.fillCenter = (discrete.Flags & 8) != 0;
        image.fillClockwise = (discrete.Flags & 16) != 0; image.useSpriteMesh = (discrete.Flags & 32) != 0;
        image.fillMethod = (Image.FillMethod)discrete.FillMethod; image.fillOrigin = discrete.FillOrigin;
        image.fillAmount = Mathf.LerpUnclamped(from.FillAmount, to.FillAmount, progress);
        image.pixelsPerUnitMultiplier = Mathf.LerpUnclamped(from.PixelsPerUnitMultiplier, to.PixelsPerUnitMultiplier, progress);
        image.color = new Color(C(0), C(1), C(2), C(3));
        image.canvasRenderer.SetColor(new Color(C(4), C(5), C(6), C(7)));
        image.enabled = (discrete.Flags & 2) != 0;
        bool active = (discrete.Flags & 1) != 0;
        if (target.activeSelf != active) target.SetActive(active);
    }

    private bool ResolveSprite(Image image, NativeDecisionHighlightState state, float now)
    {
        if (_spriteName != state.SpriteName || _textureName != state.TextureName)
        { _spriteName = state.SpriteName; _textureName = state.TextureName; _sprite = null; _retryAt = 0; _refused = false; }
        if (state.SpriteName.Length == 0) return true;
        if (_sprite != null) return true;
        Sprite? current = image.overrideSprite != null ? image.overrideSprite : image.sprite;
        if (Matches(current, state)) { _sprite = current; return true; }
        if (now < _retryAt) return false;
        _retryAt = now + 1f;
        foreach (Sprite candidate in Resources.FindObjectsOfTypeAll<Sprite>())
            if (Matches(candidate, state)) { _sprite = candidate; return true; }
        if (!_refused)
        {
            _refused = true;
            VRLog.Warn("Net", $"NATIVE DECISION HIGHLIGHT: original sprite '{state.SpriteName}' " +
                $"in texture '{state.TextureName}' is not available; refusing a substituted border.");
        }
        return false;
    }

    private static bool Matches(Sprite? sprite, NativeDecisionHighlightState state)
    {
        if (sprite == null) return false;
        Sprite original = CardFaceMipBake.OriginalFor(sprite);
        return original != null && original.name == state.SpriteName && original.texture != null
            && original.texture.name == state.TextureName;
    }
}
