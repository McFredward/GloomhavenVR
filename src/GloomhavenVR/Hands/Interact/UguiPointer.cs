using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Synthesizes uGUI pointer events (enter/exit/down/up/click) at a screen position,
/// the way the game itself clicks buttons programmatically
/// (<c>BaseButtons.clickButton</c> fires <c>ExecuteEvents.pointerClickHandler</c>,
/// UI-ARCH §4.4 — verified in the real GH.Runtime.dll: <c>private void
/// clickButton(GameObject Objbutton)</c>, IL 36 B).
///
/// MODALITY: this class never bypasses the game's UI lock. Hits are produced
/// exclusively by <see cref="GraphicRaycaster.Raycast"/> on ENABLED raycasters —
/// <c>UIManager.ToggleLockUI</c> disables every raycaster to lock the UI
/// (verified: <c>public void ToggleLockUI(bool active) { graphicRaycaster.enabled =
/// !active; ... }</c>), so a locked UI yields no hits and therefore no events.
///
/// One instance per hand; PointerEventData and hit lists are reused (no per-frame
/// allocations from our side; GraphicRaycaster's internal sort may allocate — it only
/// runs while a fingertip is actually near a registered canvas).
/// </summary>
internal sealed class UguiPointer
{
    /// <summary>Custom pointer IDs, clear of mouse (-1..-3) and touch (0+) ranges.</summary>
    private const int LeftHandPointerId = -101;
    private const int RightHandPointerId = -102;

    // P6: the far-ray pointer (RayUguiDriver) coexists with the fingertip pointer on
    // the same hand — distinct IDs so uGUI never sees one pointer teleporting.
    private const int LeftHandRayPointerId = -111;
    private const int RightHandRayPointerId = -112;

    private readonly int _pointerId;
    private readonly string _sourceTag; // click-log provenance ("laser-L", "poke-R")
    private readonly List<RaycastResult> _hits = new(16);

    private PointerEventData? _pointerData;
    private GameObject? _hovered;
    private GameObject? _pressed;
    private GameObject? _pressedClickHandler;

    internal UguiPointer(HandSide side, bool farRay = false)
    {
        _pointerId = farRay
            ? (side == HandSide.Left ? LeftHandRayPointerId : RightHandRayPointerId)
            : (side == HandSide.Left ? LeftHandPointerId : RightHandPointerId);
        _sourceTag = (farRay ? "laser" : "poke") + (side == HandSide.Left ? "-L" : "-R");
    }

    /// <summary>Currently hovered uGUI object (topmost raycast hit), if any.</summary>
    internal GameObject? Hovered => _hovered;

    /// <summary>
    /// Raycast a specific canvas at a screen point through its own GraphicRaycaster,
    /// merged with the raycasters of any NESTED canvases registered for it in
    /// <see cref="UguiPokeSurfaces"/> (test #20: Graphics under an ENABLED nested
    /// canvas register with THAT canvas, not the host — the host raycaster alone
    /// would raycast a hollow panel). Returns false (and clears hover) when the
    /// HOST raycaster is missing or DISABLED — i.e. when the game locked its UI;
    /// nested raycasters are only ever reachable through an enabled host, so the
    /// modality gate stays intact.
    /// </summary>
    internal bool TryRaycast(Canvas canvas, Vector2 screenPos, out RaycastResult topHit)
    {
        topHit = default;

        GraphicRaycaster? raycaster = canvas != null ? canvas.GetComponent<GraphicRaycaster>() : null;
        if (raycaster == null || !raycaster.enabled || !raycaster.isActiveAndEnabled)
            return false;

        PointerEventData data = GetData();
        data.position = screenPos;

        bool any = TryRaycastTop(raycaster, data, out topHit);

        List<Canvas>? nested = UguiPokeSurfaces.NestedOf(canvas);
        if (nested != null)
        {
            for (int i = 0; i < nested.Count; i++)
            {
                Canvas sub = nested[i];
                // A disabled nested canvas is the game hiding that subtree — its
                // Graphics must be neither visible nor hittable.
                if (sub == null || !sub.isActiveAndEnabled)
                    continue;
                GraphicRaycaster? subRaycaster = sub.GetComponent<GraphicRaycaster>();
                if (subRaycaster == null || !subRaycaster.isActiveAndEnabled)
                    continue;
                if (TryRaycastTop(subRaycaster, data, out RaycastResult subTop)
                    && (!any || Beats(subTop, topHit)))
                {
                    topHit = subTop;
                    any = true;
                }
            }
        }

        if (!any)
            return false;
        data.pointerCurrentRaycast = topHit;
        return true;
    }

    /// <summary>Top hit of a single raycaster, if any.</summary>
    private bool TryRaycastTop(GraphicRaycaster raycaster, PointerEventData data, out RaycastResult top)
    {
        _hits.Clear();
        raycaster.Raycast(data, _hits);
        if (_hits.Count == 0)
        {
            top = default;
            return false;
        }
        // GraphicRaycaster appends results sorted by depth (closest/topmost first).
        top = _hits[0];
        return true;
    }

    /// <summary>
    /// Cross-raycaster ordering — the subset of EventSystem's RaycastComparer that
    /// matters for coplanar, same-camera canvases: sorting layer value, then canvas
    /// sortingOrder. A nested canvas reports its own serialized order even with
    /// overrideSorting off (verified test #19 logs: 35/-1 while the host is 0), so
    /// the initiative track's content (40) beats the host background and the element
    /// board's 'BackgroundMask' underlay (-1) stays behind host content. Ties go to
    /// the challenger: nested content draws inside/above the host content it overlaps.
    /// </summary>
    private static bool Beats(in RaycastResult challenger, in RaycastResult incumbent)
    {
        int challengerLayer = SortingLayer.GetLayerValueFromID(challenger.sortingLayer);
        int incumbentLayer = SortingLayer.GetLayerValueFromID(incumbent.sortingLayer);
        if (challengerLayer != incumbentLayer)
            return challengerLayer > incumbentLayer;
        return challenger.sortingOrder >= incumbent.sortingOrder;
    }

    /// <summary>Update hover state to <paramref name="target"/> (null = nothing hovered).</summary>
    internal void SetHovered(GameObject? target)
    {
        if (ReferenceEquals(target, _hovered))
            return;

        PointerEventData data = GetData();
        if (_hovered != null)
            ExecuteEvents.Execute(_hovered, data, ExecuteEvents.pointerExitHandler);
        _hovered = target;
        if (_hovered != null)
            ExecuteEvents.Execute(_hovered, data, ExecuteEvents.pointerEnterHandler);
    }

    /// <summary>True if the pointer currently has a pressed target.</summary>
    internal bool IsPressed => _pressed != null;

    /// <summary>Pointer-down on the hovered object (mirrors StandaloneInputModule press handling).</summary>
    internal void Press(Vector2 screenPos)
    {
        if (_hovered == null || _pressed != null)
            return;

        PointerEventData data = GetData();
        data.position = screenPos;
        data.pressPosition = screenPos;
        data.pointerPressRaycast = data.pointerCurrentRaycast;
        data.eligibleForClick = true;
        data.button = PointerEventData.InputButton.Left;

        GameObject pressTarget = ExecuteEvents.ExecuteHierarchy(_hovered, data, ExecuteEvents.pointerDownHandler)
                                 ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hovered);
        _pressed = pressTarget != null ? pressTarget : _hovered;
        _pressedClickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hovered);
        data.pointerPress = _pressed;
    }

    /// <summary>Pointer-up (+ click when released over the same handler).</summary>
    internal void Release(Vector2 screenPos)
    {
        if (_pressed == null)
            return;

        PointerEventData data = GetData();
        data.position = screenPos;

        ExecuteEvents.Execute(_pressed, data, ExecuteEvents.pointerUpHandler);

        GameObject? hoveredClickHandler = _hovered != null
            ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hovered)
            : null;
        if (_pressedClickHandler != null && ReferenceEquals(hoveredClickHandler, _pressedClickHandler))
        {
            ExecuteEvents.Execute(_pressedClickHandler, data, ExecuteEvents.pointerClickHandler);
            // Test #18 verification: every synthesized uGUI click carries its
            // provenance in the log — "did the initiative portrait click reach the
            // game's handler" is answerable from the log alone (the P7 idiom:
            // every interaction is logged).
            Core.VRLog.Info("Interact", $"uGUI click: '{_pressedClickHandler.name}' ({_sourceTag}).");
        }

        data.pointerPress = null;
        data.eligibleForClick = false;
        _pressed = null;
        _pressedClickHandler = null;
    }

    /// <summary>Abort any in-flight hover/press (interactor disabled, canvas gone, …).</summary>
    internal void Cancel()
    {
        if (_pressed != null)
        {
            PointerEventData data = GetData();
            ExecuteEvents.Execute(_pressed, data, ExecuteEvents.pointerUpHandler);
            _pressed = null;
            _pressedClickHandler = null;
        }
        SetHovered(null);
    }

    private PointerEventData GetData()
    {
        // Created lazily: EventSystem.current may not exist during early boot.
        if (_pointerData == null)
            _pointerData = new PointerEventData(EventSystem.current) { pointerId = _pointerId };
        return _pointerData;
    }
}
