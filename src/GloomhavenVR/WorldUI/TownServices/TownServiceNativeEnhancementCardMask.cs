using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Keep the game's own enhancement-area buttons and their animations in
/// front of the physical card, without drawing a second pooled ability-card print.
/// Native input and selection still belong to UIEnhancementButtonHighlight.</summary>
internal sealed class TownServiceNativeEnhancementCardMask : MonoBehaviour
{
    private readonly List<Graphic> _art = new();
    private readonly List<bool> _wasEnabled = new();
    private readonly List<Graphic> _frameGraphics = new();
    private readonly List<bool> _frameRaycast = new();
    private readonly List<Graphic> _effectGraphics = new();
    private readonly List<bool> _effectRaycast = new();
    private RectTransform? _nativeFrame;
    private RectTransform? _aura;
    private RectTransform? _auraBuy, _auraSell;
    private Vector3 _auraOriginalScale;
    private readonly Vector3[] _auraCorners = new Vector3[4];
    private Vector3 _frameScale;
    private Quaternion _frameRotation;
    private Vector2 _frameSize;
    private Vector2 _framePosition;
    private bool _masked;

    internal void Mask()
    {
        if (_masked)
        {
            // Native card refreshes may enable artwork again while the same physical
            // card remains offered; keep the printed copy hidden for that frame.
            foreach (Graphic graphic in _art) if (graphic != null) graphic.enabled = false;
            AlignNativeEffects();
            return;
        }
        // The original full-card effect is a sibling of the pooled printed card. Its
        // flat-screen GUI animation can leave X almost collapsed when the holder is
        // converted into the resident's world-space palm canvas. Keep that original
        // effect on the physical card's complete rect; the separate native ability
        // highlights below retain their own sizes and animation.
        UIEnhancementCardHighlighter highlighter = GetComponentInParent<UIEnhancementCardHighlighter>();
        _aura = highlighter != null ? highlighter.transform.Find("Aura") as RectTransform : null;
        _auraBuy = _aura?.Find("Types/Buy") as RectTransform;
        _auraSell = _aura?.Find("Types/Sell") as RectTransform;
        if (_aura != null) _auraOriginalScale = _aura.localScale;
        if (highlighter != null)
        {
            // The original Aura and full-card flourish overlap the area buttons.
            // They are visual effects, never enhancement choices. A fingertip
            // pressing with grip must hit the game's real ability button rather
            // than a translucent pixel of its surrounding effect.
            foreach (Graphic graphic in highlighter.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.GetComponentInParent<UIEnhancementButtonHighlight>() != null)
                    continue;
                _effectGraphics.Add(graphic);
                _effectRaycast.Add(graphic.raycastTarget);
                graphic.raycastTarget = false;
            }
        }
        _nativeFrame = highlighter != null
            ? highlighter.transform.Find("GUI_LevelUp_Frame") as RectTransform : null;
        if (_nativeFrame != null)
        {
            _frameScale = _nativeFrame.localScale;
            _frameRotation = _nativeFrame.localRotation;
            _frameSize = _nativeFrame.sizeDelta;
            _framePosition = _nativeFrame.anchoredPosition;
            foreach (Graphic graphic in _nativeFrame.GetComponentsInChildren<Graphic>(true))
            {
                _frameGraphics.Add(graphic);
                _frameRaycast.Add(graphic.raycastTarget);
                graphic.raycastTarget = false;
            }
            AlignNativeEffects();
        }
        var graphics = new List<Graphic>();
        GetComponentsInChildren(true, graphics);
        foreach (Graphic graphic in graphics)
        {
            if (graphic == null || graphic.GetComponentInParent<UIEnhancementButtonHighlight>() != null)
                continue;
            _art.Add(graphic);
            _wasEnabled.Add(graphic.enabled);
            graphic.enabled = false;
        }
        _masked = true;
        Canvas.willRenderCanvases += OnBeforeCanvasRender;
    }

    internal void Restore()
    {
        if (!_masked) return;
        if (_nativeFrame != null)
        {
            _nativeFrame.localScale = _frameScale;
            _nativeFrame.localRotation = _frameRotation;
            _nativeFrame.sizeDelta = _frameSize;
            _nativeFrame.anchoredPosition = _framePosition;
        }
        if (_aura != null) _aura.localScale = _auraOriginalScale;
        for (int i = 0; i < _frameGraphics.Count; i++)
            if (_frameGraphics[i] != null) _frameGraphics[i].raycastTarget = _frameRaycast[i];
        _frameGraphics.Clear(); _frameRaycast.Clear();
        Canvas.willRenderCanvases -= OnBeforeCanvasRender;
        for (int i = 0; i < _effectGraphics.Count; i++)
            if (_effectGraphics[i] != null) _effectGraphics[i].raycastTarget = _effectRaycast[i];
        _effectGraphics.Clear(); _effectRaycast.Clear();
        _nativeFrame = null;
        _aura = _auraBuy = _auraSell = null;
        for (int i = 0; i < _art.Count; i++)
            if (_art[i] != null) _art[i].enabled = _wasEnabled[i];
        _art.Clear(); _wasEnabled.Clear(); _masked = false;
    }

    private void AlignNativeEffects()
    {
        if (_nativeFrame != null)
        {
            _nativeFrame.localScale = Vector3.one;
            _nativeFrame.localRotation = Quaternion.identity;
            _nativeFrame.sizeDelta = Vector2.zero;
            _nativeFrame.anchoredPosition = Vector2.zero;
        }

        // Build 574 corrected GUI_LevelUp_Frame, but the headset's narrow cyan
        // ring is the *other* original branch: CardHilight/Aura/Types/Buy (or Sell).
        // The actual game asset authors Aura as a 500x500 square; its animated
        // graphic occupied 253x707 px after the world conversion in the build-574
        // Player.log. Normalize the submitted graphic's two world edge lengths,
        // keeping their geometric mean so the native radial pulse retains its area
        // and its own alpha/rotation clock. Measuring corners after animation is
        // essential: a local-scale-only fixture passed while this rendered stretch
        // remained visible in the headset. Never touch the ability-area buttons.
        if (_aura == null) return;
        RectTransform? ink = ActiveAuraInk();
        if (ink == null) return;
        ink.GetWorldCorners(_auraCorners);
        float width = Vector3.Distance(_auraCorners[0], _auraCorners[3]);
        float height = Vector3.Distance(_auraCorners[0], _auraCorners[1]);
        if (width < .00001f || height < .00001f) return;
        float ratio = Mathf.Clamp(height / width, .1f, 10f);
        if (Mathf.Abs(1f - ratio) < .005f) return;
        float compensation = Mathf.Sqrt(ratio);
        Vector3 scale = _aura.localScale;
        scale.x *= compensation;
        scale.y /= compensation;
        _aura.localScale = scale;
    }

    private RectTransform? ActiveAuraInk()
    {
        if (_auraBuy != null && _auraBuy.gameObject.activeInHierarchy) return _auraBuy;
        if (_auraSell != null && _auraSell.gameObject.activeInHierarchy) return _auraSell;
        return null;
    }

    // Native UI effects can write after Ritual.Tick. The canvas callback is the
    // latest point before submission, including builds where their LateUpdate
    // order comes after ours; this LateUpdate also covers editor and mirror paths.
    private void LateUpdate()
    {
        if (_masked) AlignNativeEffects();
    }

    private void OnBeforeCanvasRender()
    {
        if (_masked) AlignNativeEffects();
    }

    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();
}
