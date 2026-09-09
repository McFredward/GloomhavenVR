using GloomhavenVR.Cards;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Reads only the original mandatory-damage decoration after native layout/animation.
/// Stable output keeps its source timestamp and immutable reference, including redundant sends.</summary>
internal static class NativeDecisionHighlightSampler
{
    private static readonly NativeDecisionHighlightState Scratch = new();
    private static NativeDecisionHighlightState? _last;
    private static bool _refused;
    internal static void Reset() { _last = null; _refused = false; }

    internal static NativeDecisionHighlightState? Sample()
    {
        if (!DecisionDockSurface.DockingTakeDamage || !Singleton<TakeDamagePanel>.IsInitialized)
        { Reset(); return null; }
        var root = Singleton<TakeDamagePanel>.Instance.mandatoryTakeDamageHighlight;
        Image? image = root != null ? root.GetComponent<Image>() : null;
        if (image == null) { Reset(); return null; }
        var rect = image.rectTransform;
        float[] r = Scratch.Rect;
        r[0] = rect.anchorMin.x; r[1] = rect.anchorMin.y;
        r[2] = rect.anchorMax.x; r[3] = rect.anchorMax.y;
        r[4] = rect.pivot.x; r[5] = rect.pivot.y;
        r[6] = rect.sizeDelta.x; r[7] = rect.sizeDelta.y;
        r[8] = rect.anchoredPosition3D.x; r[9] = rect.anchoredPosition3D.y; r[10] = rect.anchoredPosition3D.z;
        r[11] = rect.localScale.x; r[12] = rect.localScale.y; r[13] = rect.localScale.z;
        r[14] = rect.localRotation.x; r[15] = rect.localRotation.y; r[16] = rect.localRotation.z; r[17] = rect.localRotation.w;
        Color color = image.color, renderer = image.canvasRenderer.GetColor();
        float[] c = Scratch.Colors;
        c[0] = color.r; c[1] = color.g; c[2] = color.b; c[3] = color.a;
        c[4] = renderer.r; c[5] = renderer.g; c[6] = renderer.b; c[7] = renderer.a;
        Scratch.Flags = (byte)((image.gameObject.activeSelf ? 1 : 0) | (image.enabled ? 2 : 0)
            | (image.preserveAspect ? 4 : 0) | (image.fillCenter ? 8 : 0)
            | (image.fillClockwise ? 16 : 0) | (image.useSpriteMesh ? 32 : 0));
        Scratch.ImageType = (byte)image.type; Scratch.FillMethod = (byte)image.fillMethod;
        Scratch.FillOrigin = (byte)image.fillOrigin; Scratch.FillAmount = image.fillAmount;
        Scratch.PixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
        Canvas? canvas = image.canvas;
        if (canvas == null) { Reset(); return null; }
        Scratch.ReferencePixelsPerUnit = canvas.referencePixelsPerUnit;
        Sprite? sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
        if (sprite != null) sprite = CardFaceMipBake.OriginalFor(sprite);
        Scratch.SpriteName = sprite != null ? sprite.name : string.Empty;
        Scratch.TextureName = sprite != null && sprite.texture != null ? sprite.texture.name : string.Empty;
        if (!Scratch.Validate())
        {
            _last = null;
            if (!_refused)
            {
                _refused = true;
                GloomhavenVR.Core.VRLog.Warn("Net", "NATIVE DECISION HIGHLIGHT SAMPLE: original Image output refused "
                    + "(invalid rect/quaternion, Image settings, or public sprite/texture UTF8 name exceeds 64 bytes).");
            }
            return null;
        }
        _refused = false;
        if (NativeDecisionHighlightState.SamePicture(Scratch, _last)) return _last;
        Scratch.SampleTime = Time.unscaledTime;
        return _last = Scratch.Snapshot();
    }
}
