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
    private RectTransform? _nativeFrame;
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
            AlignNativeFrame();
            return;
        }
        // The original full-card effect is a sibling of the pooled printed card. Its
        // flat-screen GUI animation can leave X almost collapsed when the holder is
        // converted into the resident's world-space palm canvas. Keep that original
        // effect on the physical card's complete rect; the separate native ability
        // highlights below retain their own sizes and animation.
        UIEnhancementCardHighlighter highlighter = GetComponentInParent<UIEnhancementCardHighlighter>();
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
            AlignNativeFrame();
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
        for (int i = 0; i < _frameGraphics.Count; i++)
            if (_frameGraphics[i] != null) _frameGraphics[i].raycastTarget = _frameRaycast[i];
        _frameGraphics.Clear(); _frameRaycast.Clear();
        _nativeFrame = null;
        for (int i = 0; i < _art.Count; i++)
            if (_art[i] != null) _art[i].enabled = _wasEnabled[i];
        _art.Clear(); _wasEnabled.Clear(); _masked = false;
    }

    private void AlignNativeFrame()
    {
        if (_nativeFrame == null) return;
        _nativeFrame.localScale = Vector3.one;
        _nativeFrame.localRotation = Quaternion.identity;
        _nativeFrame.sizeDelta = Vector2.zero;
        _nativeFrame.anchoredPosition = Vector2.zero;
    }

    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();
}
