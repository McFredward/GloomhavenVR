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
    private Vector3 _lastCorrectedAuraScale;
    private bool _hasCorrectedAura;
    private bool _mappedPhysicalCard;
    private float _lastPhysicalCardHeight;
    private readonly Vector3[] _auraCorners = new Vector3[4];
    private readonly Vector3[] _cardCorners = new Vector3[4];
    private readonly List<Graphic> _auraGraphics = new();
    private RectTransform? _highlighterRect;
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
        _highlighterRect = highlighter != null ? highlighter.transform as RectTransform : null;
        _aura = highlighter != null ? highlighter.transform.Find("Aura") as RectTransform : null;
        _auraBuy = _aura?.Find("Types/Buy") as RectTransform;
        _auraSell = _aura?.Find("Types/Sell") as RectTransform;
        if (_aura != null)
        {
            _auraOriginalScale = _aura.localScale;
            _auraGraphics.AddRange(_aura.GetComponentsInChildren<Graphic>(true));
        }
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
        _aura = _auraBuy = _auraSell = _highlighterRect = null;
        _hasCorrectedAura = false;
        _mappedPhysicalCard = false;
        _lastPhysicalCardHeight = 0f;
        _auraGraphics.Clear();
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

        // The build-575 headset shows a tall narrow ring even after the old
        // correction. Buy/Sell are grouping RectTransforms, not necessarily the
        // rendered image: measuring a zero-size/stretch group can report a square
        // while its descendant graphic is the 325x706 strip in the capture log.
        // Measure the actual active graphic after native animation, then fit its
        // two submitted edges to a circle slightly larger than the physical card.
        // The original sprite, alpha, rotation and animation remain native.
        if (_aura == null || _highlighterRect == null) return;
        RectTransform? ink = ActiveAuraInk();
        if (ink == null) return;
        ink.GetWorldCorners(_auraCorners);
        float width = Vector3.Distance(_auraCorners[0], _auraCorners[3]);
        float height = Vector3.Distance(_auraCorners[0], _auraCorners[1]);
        if (width < .00001f || height < .00001f) return;
        // The CardHilight root is 325x450 px, while its drawn native effect spans
        // 578..770 px in the build-575 log. Preserve that native pulse envelope:
        // the geometric mean of the ink's two edges is its pre-correction size.
        // Map that size from the highlighter's world height to the actual VRCard
        // world height, rather than freezing every animation frame at one diameter.
        _highlighterRect.GetWorldCorners(_cardCorners);
        float rootHeight = Vector3.Distance(_cardCorners[0], _cardCorners[1]);
        if (rootHeight < .00001f) return;
        bool physicalNow = TownServiceEnhancementHandoff.TryPhysicalCardHeight(out float cardHeight);
        if (!physicalNow)
            cardHeight = rootHeight;
        Vector3 scale = _aura.localScale;
        bool nativeRootWrite = !_hasCorrectedAura
            || (scale - _lastCorrectedAuraScale).sqrMagnitude > .00000001f;
        // If no native animation rewrote the parent, the measured ink already
        // contains last frame's physical-size mapping. Applying it a second time
        // would exponentially inflate the ring on every canvas render callback.
        float correction = nativeRootWrite ? cardHeight / rootHeight * 1.08f : 1f;
        // The native mask is first created before its offered VRCard is seated.
        // Once the physical card arrives, change only the mapping factor; do not
        // reapply the earlier 8% margin or flatten the native pulse.
        if (!nativeRootWrite && physicalNow && _mappedPhysicalCard)
            correction = cardHeight / _lastPhysicalCardHeight;
        else if (!nativeRootWrite && physicalNow && !_mappedPhysicalCard)
            correction = cardHeight / rootHeight;
        float diameter = Mathf.Sqrt(width * height) * correction;
        scale.x *= Mathf.Clamp(diameter / width, .025f, 40f);
        scale.y *= Mathf.Clamp(diameter / height, .025f, 40f);
        _aura.localScale = scale;
        _lastCorrectedAuraScale = scale;
        _hasCorrectedAura = true;
        _mappedPhysicalCard = physicalNow;
        _lastPhysicalCardHeight = cardHeight;
    }

    private RectTransform? ActiveAuraInk()
    {
        RectTransform? branch = _auraBuy != null && _auraBuy.gameObject.activeInHierarchy ? _auraBuy
            : _auraSell != null && _auraSell.gameObject.activeInHierarchy ? _auraSell : null;
        if (branch == null) branch = _aura;
        if (branch == null) return null;
        // Native effect groups can have a zero rect. The largest live Graphic is
        // the visible aura quad; a group-only fixture missed this in build 575.
        // Cache the fixed prefab's candidate Graphics when masking, not on every
        // Canvas.willRenderCanvases callback (which may run more than once/frame).
        RectTransform? ink = null;
        float area = 0f;
        foreach (Graphic graphic in _auraGraphics)
        {
            if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy
                || graphic.rectTransform == null || !graphic.transform.IsChildOf(branch)) continue;
            RectTransform rect = graphic.rectTransform;
            rect.GetWorldCorners(_auraCorners);
            float candidate = Vector3.Distance(_auraCorners[0], _auraCorners[3])
                * Vector3.Distance(_auraCorners[0], _auraCorners[1]);
            if (candidate <= area) continue;
            area = candidate; ink = rect;
        }
        return ink;
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
