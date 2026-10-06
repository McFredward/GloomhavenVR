using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Keep the game's own enhancement-area buttons and their animations in
/// front of the physical card, without drawing a second pooled ability-card print.
/// Native input and selection still belong to UIEnhancementButtonHighlight.</summary>
internal sealed class TownServiceNativeEnhancementCardMask : MonoBehaviour
{
    private static TownServiceNativeEnhancementCardMask? _active;
    // Read the game's flat world-Z step before moving its parent into the
    // actual card's plane. This retains its phase even during a tilted card's
    // release-to-palm settle; RefreshCurrent then fits the final physical size.
    internal static void PrepareCurrentPlacement()
    {
        TownServiceNativeEnhancementCardMask? mask = _active;
        if (mask != null && mask._masked) mask.CaptureNativeAuraRotation();
    }
    internal static void RefreshCurrent()
    {
        TownServiceNativeEnhancementCardMask? mask = _active;
        if (mask != null && mask._masked) mask.AlignNativeEffects();
    }
    private readonly List<Graphic> _art = new();
    private readonly List<bool> _wasEnabled = new();
    private readonly List<Graphic> _frameGraphics = new();
    private readonly List<bool> _frameRaycast = new();
    private readonly List<Graphic> _effectGraphics = new();
    private readonly List<bool> _effectRaycast = new();
    private RectTransform? _nativeFrame;
    private RectTransform? _aura;
    private RectTransform? _auraBuy, _auraSell;
    private Vector3 _auraOriginalScale, _auraOriginalPosition;
    private Quaternion _auraOriginalRotation;
    private Quaternion _lastCorrectedAuraRotation;
    private Quaternion _nativeAuraRotation;
    private Quaternion _capturedAuraRotation;
    private bool _phaseCaptured;
    private Vector3 _lastCorrectedAuraScale;
    private bool _hasCorrectedAura;
    private bool _mappedPhysicalCard;
    private float _lastPhysicalCardHeight, _lastRootHeight;
    private readonly Vector3[] _auraCorners = new Vector3[4];
    private readonly Vector3[] _cardCorners = new Vector3[4];
    private readonly List<Graphic> _auraGraphics = new();
    private readonly Dictionary<RectTransform, Vector3> _inkOriginalScales = new();
    private readonly Dictionary<RectTransform, Quaternion> _inkOriginalRotations = new();
    private readonly Dictionary<RectTransform, Quaternion> _inkNativeRotations = new();
    private readonly Dictionary<RectTransform, Quaternion> _inkCorrectedRotations = new();
    private readonly List<UIEnhancementButtonHighlight> _areas = new();
    private RectTransform? _highlighterRect;
    private RectTransform? _nativePrint;
    private Vector3 _printRootPosition, _printRootScale;
    private Quaternion _printRootRotation;
    private readonly Vector3[] _physicalCorners = new Vector3[4];
    private Vector3 _frameScale;
    private Quaternion _frameRotation;
    private Vector2 _frameSize;
    private Vector3 _framePosition;
    private bool _masked;
    private Rect _captureBounds;
    private bool _hasCaptureBounds;

    /// <summary>The native cyan aura animates beyond CardHilight's 325 px host width.
    /// The native effect can leave the generic drawable-ink census during a
    /// capture-frame measurement while its ring is otherwise visible. Keep the largest
    /// measured active-offer footprint until Restore, so supersampling never
    /// reallocates to the narrow host and cuts off the ring's left/right arcs.</summary>
    internal static bool TryAuraCaptureBounds(RectTransform host, out Rect bounds)
    {
        bounds = default;
        TownServiceNativeEnhancementCardMask? mask = _active;
        if (mask == null || !mask._masked || mask._aura == null
            || !mask.transform.IsChildOf(host)) return false;
        mask.AlignNativeEffects();
        RectTransform? ink = mask.ActiveAuraInk();
        if (ink != null)
        {
            ink.GetWorldCorners(mask._auraCorners);
            Vector3 first = host.InverseTransformPoint(mask._auraCorners[0]);
            float minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
            for (int i = 1; i < 4; i++)
            {
                Vector3 point = host.InverseTransformPoint(mask._auraCorners[i]);
                minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y); maxY = Mathf.Max(maxY, point.y);
            }
            Rect current = Rect.MinMaxRect(minX, minY, maxX, maxY);
            if (current.width > .01f && current.height > .01f)
            {
                mask._captureBounds = mask._hasCaptureBounds
                    ? Rect.MinMaxRect(Mathf.Min(mask._captureBounds.xMin, current.xMin),
                        Mathf.Min(mask._captureBounds.yMin, current.yMin),
                        Mathf.Max(mask._captureBounds.xMax, current.xMax),
                        Mathf.Max(mask._captureBounds.yMax, current.yMax))
                    : current;
                mask._hasCaptureBounds = true;
            }
        }
        bounds = mask._captureBounds;
        return mask._hasCaptureBounds;
    }

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
        AbilityCardUI? nativeCard = GetComponent<AbilityCardUI>();
        _nativePrint = nativeCard != null && nativeCard.fullAbilityCard != null
            ? nativeCard.fullAbilityCard.transform as RectTransform : null;
        if (_highlighterRect != null)
        {
            _printRootPosition = _highlighterRect.localPosition;
            _printRootRotation = _highlighterRect.localRotation;
            _printRootScale = _highlighterRect.localScale;
        }
        _aura = highlighter != null ? highlighter.transform.Find("Aura") as RectTransform : null;
        _auraBuy = _aura?.Find("Types/Buy") as RectTransform;
        _auraSell = _aura?.Find("Types/Sell") as RectTransform;
        if (_aura != null)
        {
            _auraOriginalScale = _aura.localScale;
            _auraOriginalPosition = _aura.localPosition;
            _auraOriginalRotation = _nativeAuraRotation = _aura.localRotation;
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
            _framePosition = _nativeFrame.anchoredPosition3D;
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
        _active = this;
        Canvas.willRenderCanvases += OnBeforeCanvasRender;
    }

    internal void Restore()
    {
        if (!_masked) return;
        if (!ReferenceEquals(_highlighterRect, null))
            Net.TownServices.TownServiceMirror.RegisterOfferedFrame(_highlighterRect, null);
        if (_highlighterRect != null && _nativePrint != null)
        {
            _highlighterRect.localPosition = _printRootPosition;
            _highlighterRect.localRotation = _printRootRotation;
            _highlighterRect.localScale = _printRootScale;
        }
        if (_nativeFrame != null)
        {
            _nativeFrame.localScale = _frameScale;
            _nativeFrame.localRotation = _frameRotation;
            _nativeFrame.sizeDelta = _frameSize;
            _nativeFrame.anchoredPosition3D = _framePosition;
        }
        if (_aura != null)
        { _aura.localScale = _auraOriginalScale; _aura.localRotation = _auraOriginalRotation; _aura.localPosition = _auraOriginalPosition; }
        foreach (KeyValuePair<RectTransform, Quaternion> ink in _inkOriginalRotations)
            if (ink.Key != null) ink.Key.localRotation = ink.Value;
        _inkOriginalRotations.Clear(); _inkNativeRotations.Clear(); _inkCorrectedRotations.Clear();
        _areas.Clear();
        foreach (KeyValuePair<RectTransform, Vector3> ink in _inkOriginalScales)
            if (ink.Key != null) ink.Key.localScale = ink.Value;
        _inkOriginalScales.Clear();
        for (int i = 0; i < _frameGraphics.Count; i++)
            if (_frameGraphics[i] != null) _frameGraphics[i].raycastTarget = _frameRaycast[i];
        _frameGraphics.Clear(); _frameRaycast.Clear();
        Canvas.willRenderCanvases -= OnBeforeCanvasRender;
        for (int i = 0; i < _effectGraphics.Count; i++)
            if (_effectGraphics[i] != null) _effectGraphics[i].raycastTarget = _effectRaycast[i];
        _effectGraphics.Clear(); _effectRaycast.Clear();
        _nativeFrame = null;
        _aura = _auraBuy = _auraSell = _highlighterRect = _nativePrint = null;
        _hasCorrectedAura = false;
        _phaseCaptured = false;
        _mappedPhysicalCard = false;
        _lastPhysicalCardHeight = _lastRootHeight = 0f;
        _auraGraphics.Clear();
        for (int i = 0; i < _art.Count; i++)
            if (_art[i] != null) _art[i].enabled = _wasEnabled[i];
        _art.Clear(); _wasEnabled.Clear(); _masked = false;
        _hasCaptureBounds = false;
        if (ReferenceEquals(_active, this)) _active = null;
    }

    private void AlignNativeEffects()
    {
        // Read the original world-Z callback before fitting the print can move its
        // sibling effect's parent. Handoff may already have saved this sample
        // before surface placement; do not recapture that same local value after
        // its parent's basis changes. This also covers late native tween writes.
        CaptureNativeAuraRotation();
        AlignNativePrint();
        AlignNativeAreas();
        if (_nativeFrame != null)
        {
            _nativeFrame.localScale = Vector3.one;
            _nativeFrame.localRotation = PhysicalPlane(_nativeFrame.parent);
            _nativeFrame.sizeDelta = Vector2.zero;
            _nativeFrame.anchoredPosition = Vector2.zero;
            PlaceOnPhysicalCenter(_nativeFrame);
        }

        // Build 576's video exposes the transform-space error in the older fit:
        // it measured a ROTATED descendant's world corners, then changed X/Y scale
        // on the Aura ANCESTOR. Those parent axes no longer coincide with the ink's
        // axes, so rotating the descendant (or the card) squeezes the apparent ring
        // differently. Preserve the native geometric-mean pulse on an isotropic
        // world-space Aura basis; square the original active ink at its own
        // RectTransform. Because the correction follows the ink's local axes,
        // a native Z rotation cannot turn the world-space circle into an ellipse.
        if (_aura == null || _highlighterRect == null) return;
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
            // AlignNativePrint already scales the holder when the physical card
            // changes size. Applying the raw physical ratio here scaled the aura
            // twice (e.g. a .6 card size gave a .36 ring). Only the change of the
            // physical-to-holder ratio remains, including legacy holders without
            // an adopted print whose basis genuinely does not follow the card.
            correction = cardHeight / rootHeight * _lastRootHeight / _lastPhysicalCardHeight;
        else if (!nativeRootWrite && physicalNow && !_mappedPhysicalCard)
            correction = cardHeight / rootHeight;
        float pulse = Mathf.Sqrt(Mathf.Abs(scale.x * scale.y));
        float uniform = pulse * correction;
        // The converted card-holder is normally uniformly scaled, but a native
        // intermediate parent can stretch X and Y independently. Cancel that
        // stretch BEFORE the ink's own rotation. A nonuniform ancestor followed
        // by another rotation also creates SHEAR, so dividing by two edge lengths
        // alone is insufficient: diagonalise the parent's 2D Gram matrix first,
        // then invert its principal stretches. The ink's own rotation/animation
        // is left intact on the now-isotropic world-space basis.
        Transform parent = _aura.parent;
        if (!_hasCorrectedAura
            || Quaternion.Angle(_aura.localRotation, _lastCorrectedAuraRotation) > .05f)
        {
            // UIEnchantressEffect.Rotate writes WORLD eulerAngles=(0,0,phase)
            // on every LeanTween step. Its flat-screen basis is not the palm
            // card's basis. Reading local Euler Z from inverse card yaw mixed
            // those spaces and left the published ring sideways after native
            // Update. Keep the native world-Z clock as a local planar rotation;
            // RefreshCurrent also runs after final card placement, before sync.
            if (!_phaseCaptured || Quaternion.Angle(_aura.localRotation, _capturedAuraRotation) > .05f)
                CaptureNativeAuraRotation();
        }
        // ObjectPool.SpawnCard resets the pooled card's scale and centre but its
        // resetLocalRotation parameter remains false. AlignNativePrint therefore
        // may turn the holder by inverse pooled-print yaw/tilt to fit the paper.
        // Aura is a SIBLING of that print: using the holder's bare XY plane leaves
        // it edge-on across an otherwise correctly facing card. Resolve the actual
        // physical print's plane and centre before applying its native Z clock.
        Quaternion plane = PhysicalPlane(parent);
        Vector3 basisRight = parent.TransformVector(plane * Vector3.right);
        Vector3 basisUp = parent.TransformVector(plane * Vector3.up);
        float gxx = Vector3.Dot(basisRight, basisRight);
        float gxy = Vector3.Dot(basisRight, basisUp);
        float gyy = Vector3.Dot(basisUp, basisUp);
        float anisotropy = Mathf.Sqrt((gxx - gyy) * (gxx - gyy) + 4f * gxy * gxy);
        float nativeAngle = _nativeAuraRotation.eulerAngles.z;
        float angle = nativeAngle;
        if (anisotropy > (gxx + gyy) * .0001f)
        {
            float principal = .5f * Mathf.Atan2(2f * gxy, gxx - gyy) * Mathf.Rad2Deg;
            // Either principal axis can be X. Select the one nearest the native
            // phase so even a textured native effect avoids a gratuitous 90° flip.
            angle = Mathf.Abs(Mathf.DeltaAngle(nativeAngle, principal))
                <= Mathf.Abs(Mathf.DeltaAngle(nativeAngle, principal + 90f))
                ? principal : principal + 90f;
        }
        Quaternion correctedRotation = plane * Quaternion.Euler(0f, 0f, angle);
        Vector3 parentRight = parent.TransformVector(correctedRotation * Vector3.right);
        Vector3 parentUp = parent.TransformVector(correctedRotation * Vector3.up);
        float parentX = Mathf.Max(.00001f, parentRight.magnitude);
        float parentY = Mathf.Max(.00001f, parentUp.magnitude);
        float parentMean = Mathf.Sqrt(parentX * parentY);
        _aura.localRotation = correctedRotation;
        PlaceOnPhysicalCenter(_aura);
        _aura.localScale = new Vector3(Mathf.Sign(scale.x) * uniform * parentMean / parentX,
            Mathf.Sign(scale.y) * uniform * parentMean / parentY, scale.z);
        _lastCorrectedAuraScale = _aura.localScale;
        _lastCorrectedAuraRotation = correctedRotation;
        _hasCorrectedAura = true;
        _phaseCaptured = false;
        _mappedPhysicalCard = physicalNow;
        _lastPhysicalCardHeight = cardHeight;
        _lastRootHeight = rootHeight;

        // A principal-axis correction must not discard the native rotation clock.
        // The old fit chose a fixed principal angle, so the circle stayed round but
        // its actual textured ink stopped turning. Carry the remaining phase into
        // every original drawing transform after the now-isotropic Aura basis.
        foreach (Graphic graphic in _auraGraphics)
        {
            if (graphic == null || graphic.rectTransform == null) continue;
            RectTransform drawing = graphic.rectTransform;
            Quaternion observed = drawing.localRotation;
            if (!_inkOriginalRotations.ContainsKey(drawing))
            {
                _inkOriginalRotations.Add(drawing, observed);
                _inkNativeRotations.Add(drawing, observed);
            }
            else if (!_inkCorrectedRotations.TryGetValue(drawing, out Quaternion prior)
                || Quaternion.Angle(observed, prior) > .05f)
                _inkNativeRotations[drawing] = observed;
            Quaternion compensated = Quaternion.Euler(0f, 0f, nativeAngle - angle)
                * _inkNativeRotations[drawing];
            drawing.localRotation = compensated;
            _inkCorrectedRotations[drawing] = compensated;
        }
        RectTransform? ink = ActiveAuraInk();
        if (ink == null) return;
        ink.GetWorldCorners(_auraCorners);
        float width = Vector3.Distance(_auraCorners[0], _auraCorners[3]);
        float height = Vector3.Distance(_auraCorners[0], _auraCorners[1]);
        if (width < .00001f || height < .00001f) return;
        if (!_inkOriginalScales.ContainsKey(ink)) _inkOriginalScales.Add(ink, ink.localScale);
        float diameter = Mathf.Sqrt(width * height);
        Vector3 inkScale = ink.localScale;
        inkScale.x *= Mathf.Clamp(diameter / width, .025f, 40f);
        inkScale.y *= Mathf.Clamp(diameter / height, .025f, 40f);
        ink.localScale = inkScale;
    }

    private void AlignNativePrint()
    {
        RectTransform? physical = TownServiceEnhancementHandoff.PhysicalCardFace;
        if (_highlighterRect == null || _nativePrint == null || physical == null
            || physical == _nativePrint) return;
        // Build625 mapped the 325x450 CardHilight host to a nominal 63.5:88
        // slab. Its printed FullAbilityCard is instead 294x450, and CardFace
        // fits and insets the adopted original by 6%. A matched card ROOT is
        // therefore not a matched print: native selectable areas drift by
        // millimetres. Map the actual hidden original print to the actual
        // adopted print, including each native pivot/offset and the card's
        // intermediate settling pose. The original area children retain their
        // native local geometry, state and animation; remote capture includes
        // this same corrected ancestor rather than fitting the observer's card.
        _nativePrint.GetWorldCorners(_cardCorners);
        physical.GetWorldCorners(_physicalCorners);
        float nativeHeight = Vector3.Distance(_cardCorners[0], _cardCorners[1]);
        float physicalHeight = Vector3.Distance(_physicalCorners[0], _physicalCorners[1]);
        float nativeWidth = Vector3.Distance(_cardCorners[0], _cardCorners[3]);
        float physicalWidth = Vector3.Distance(_physicalCorners[0], _physicalCorners[3]);
        if (nativeHeight < .000001f || physicalHeight < .000001f
            || nativeWidth < .000001f || physicalWidth < .000001f) return;
        Quaternion correction = physical.rotation * Quaternion.Inverse(_nativePrint.rotation);
        if (Quaternion.Angle(correction, Quaternion.identity) > .001f)
            _highlighterRect.rotation = correction * _highlighterRect.rotation;
        Vector3 relativeRight = _highlighterRect.InverseTransformVector(_nativePrint.TransformVector(Vector3.right));
        Vector3 relativeUp = _highlighterRect.InverseTransformVector(_nativePrint.TransformVector(Vector3.up));
        float targetX = physicalWidth / Mathf.Max(.000001f, _nativePrint.rect.width);
        float targetY = physicalHeight / Mathf.Max(.000001f, _nativePrint.rect.height);
        if (Mathf.Abs(targetX - targetY) < Mathf.Max(targetX, targetY) * .00001f
            && Mathf.Abs(relativeRight.magnitude - relativeUp.magnitude) < relativeUp.magnitude * .00001f
            && Mathf.Abs(Vector3.Dot(relativeRight.normalized, relativeUp.normalized)) < .00001f)
        {
            // Native pooled print geometry is a uniform basis (including a
            // rotated print). Cancel any old holder stretch on the holder's
            // own axes before its child rotation, rather than sizing a rotated
            // child's edge length on two different parent axes.
            Transform parent = _highlighterRect.parent;
            float parentX = parent.TransformVector(_highlighterRect.localRotation * Vector3.right).magnitude;
            float parentY = parent.TransformVector(_highlighterRect.localRotation * Vector3.up).magnitude;
            float fit = targetY / Mathf.Max(.000001f, relativeUp.magnitude);
            Vector3 scale = _highlighterRect.localScale;
            Vector3 fitted = new(fit / Mathf.Max(.000001f, parentX), fit / Mathf.Max(.000001f, parentY),
                fit / Mathf.Max(.000001f, parent.TransformVector(_highlighterRect.localRotation * Vector3.forward).magnitude));
            if ((scale - fitted).sqrMagnitude > fitted.sqrMagnitude * .000000000001f)
                _highlighterRect.localScale = fitted;
        }
        else
        {
            // World/inspect scale can independently stretch a still-planar
            // print. Measure again after orienting it, then retain both axes.
            _nativePrint.GetWorldCorners(_cardCorners);
            float fitX = physicalWidth / Mathf.Max(.000001f, Vector3.Distance(_cardCorners[0], _cardCorners[3]));
            float fitY = physicalHeight / Mathf.Max(.000001f, Vector3.Distance(_cardCorners[0], _cardCorners[1]));
            if (Mathf.Abs(fitX - 1f) > .000001f || Mathf.Abs(fitY - 1f) > .000001f)
            {
                Vector3 scale = _highlighterRect.localScale;
                _highlighterRect.localScale = new Vector3(scale.x * fitX, scale.y * fitY, scale.z * fitY);
            }
        }
        _nativePrint.GetWorldCorners(_cardCorners);
        Vector3 nativeCenter = (_cardCorners[0] + _cardCorners[2]) * .5f;
        Vector3 physicalCenter = (_physicalCorners[0] + _physicalCorners[2]) * .5f;
        // One tiny depth bias prevents coplanar flicker; it does not change any
        // printed area's X/Y alignment. Scale with the actual print height so
        // world/inspect scaling retains the same relative separation.
        Vector3 translation = physicalCenter - nativeCenter
            - physical.forward * (physicalHeight * .0003f);
        if (translation.sqrMagnitude > physicalHeight * physicalHeight * .000000000001f)
            _highlighterRect.position += translation;
        Net.TownServices.TownServiceMirror.RegisterOfferedFrame(_highlighterRect, physical);
    }

    private Quaternion PhysicalPlane(Transform parent)
    {
        RectTransform? physical = TownServiceEnhancementHandoff.PhysicalCardFace;
        return physical != null ? Quaternion.Inverse(parent.rotation) * physical.rotation : Quaternion.identity;
    }

    private void PlaceOnPhysicalCenter(RectTransform drawing)
    {
        RectTransform? physical = TownServiceEnhancementHandoff.PhysicalCardFace;
        if (physical == null) return;
        physical.GetWorldCorners(_physicalCorners);
        float height = Vector3.Distance(_physicalCorners[0], _physicalCorners[1]);
        drawing.position = (_physicalCorners[0] + _physicalCorners[2]) * .5f
            - physical.forward * (height * .0003f);
    }

    private void AlignNativeAreas()
    {
        if (_nativePrint == null) return;
        _areas.Clear();
        _nativePrint.GetComponentsInChildren(true, _areas);
        foreach (UIEnhancementButtonHighlight area in _areas)
        {
            if (area == null || !area.gameObject.activeSelf) continue;
            // The native HighlightButtons pool moves the original button with
            // SetParent(target) (worldPositionStays=true). Highlight updates only
            // pivot/size/position. In flat UI both parents share a basis; in a
            // converted palm that preserves the previous flat world rotation and
            // scale, stretching the button or turning it away from the print.
            // The serialized original template has identity local rotation/scale.
            // Restore that template basis on the actual original target, retaining
            // its native dimensions, pivot, state, material and callbacks. Do not
            // restore the stale world-basis artifact when the offer ends.
            if (area.transform.localRotation != Quaternion.identity)
                area.transform.localRotation = Quaternion.identity;
            if (area.transform.localScale != Vector3.one)
                area.transform.localScale = Vector3.one;
        }
    }

    private void CaptureNativeAuraRotation()
    {
        if (_aura == null || _phaseCaptured
            && Quaternion.Angle(_aura.localRotation, _capturedAuraRotation) <= .05f
            || _hasCorrectedAura
            && Quaternion.Angle(_aura.localRotation, _lastCorrectedAuraRotation) <= .05f) return;
        _nativeAuraRotation = Quaternion.Euler(0f, 0f, _aura.rotation.eulerAngles.z);
        _capturedAuraRotation = _aura.localRotation;
        _phaseCaptured = true;
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
