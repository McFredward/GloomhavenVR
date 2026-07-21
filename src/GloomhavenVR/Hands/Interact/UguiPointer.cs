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
    private readonly HandSide _side;    // this pointer's hand — source of the depth aim ray
    private readonly bool _farRay;      // laser (true) vs fingertip poke (false)
    private readonly List<RaycastResult> _hits = new(16);

    private PointerEventData? _pointerData;
    private GameObject? _hovered;
    private GameObject? _pressed;
    private GameObject? _pressedClickHandler;

    // Drag state (P?: sliders/scrollbars/scroll-rects only move via IDragHandler —
    // Down/Up/Click alone never budge a Slider handle). Cached on Press, driven by
    // Drag() every held frame, torn down on Release/Cancel — mirrors
    // PointerInputModule's initializePotentialDrag → beginDrag → drag → endDrag flow.
    private GameObject? _dragTarget;
    private bool _dragging;
    private Vector2 _lastDragPos;

    internal UguiPointer(HandSide side, bool farRay = false)
    {
        _side = side;
        _farRay = farRay;
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

        // Depth-aware entry pick (user #3 follow-up). For a host whose entries carry
        // REAL 3D depth — the initiative track's portraits, each stepped in z — the flat
        // host-plane screen point projects to a DIFFERENT screen position than a
        // depth-displaced portrait under perspective, so the GraphicRaycaster below
        // resolves a NEIGHBOUR. When such a host registered a per-portrait picker
        // (<see cref="DepthPortraitPicks"/>), resolve the exact entry by intersecting
        // the TRUE world aim ray with each portrait's world rect at its real depth
        // instead of the flat screen point. Scoped tightly: only the FAR ray (a poke's
        // fingertip is a world point, not this aim ray) and only registered hosts —
        // every other converted panel, and the poke on this one, keep the flat pick
        // below unchanged. The modality gate above still holds: a locked UI disabled
        // this raycaster and already returned, so no depth hit escapes it either.
        if (_farRay
            && DepthPortraitPicks.TryGet(canvas!, out IDepthPortraitPicker picker)
            && TryAimRay(out Vector3 rayOrigin, out Vector3 rayDir)
            && picker.TryPickPortrait(rayOrigin, rayDir, out GameObject portrait, out Vector3 portraitHit))
        {
            // Land the visible beam on the portrait's real position/depth (UiHitOverride
            // clamps beam LENGTH only; pick data untouched) so the hit corresponds to
            // the portrait the player points at, not the flat host plane. RayUguiDriver
            // set the flat point just before calling us; this same-frame override wins.
            RayInteractor? ray = VRHands.Get(_side)?.Ray;
            if (ray != null)
                ray.UiHitOverride = portraitHit;

            topHit = default;
            topHit.gameObject = portrait;
            topHit.worldPosition = portraitHit;
            GetData().pointerCurrentRaycast = topHit;
            return true;
        }

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

        // Prime a potential drag (StandaloneInputModule parity): notify the drag handler
        // it MIGHT start dragging, and cache it so Drag() can drive it each held frame.
        // Many controls have no IDragHandler — then _dragTarget stays null and Drag() no-ops.
        _dragging = false;
        _lastDragPos = screenPos;
        _dragTarget = ExecuteEvents.GetEventHandler<IDragHandler>(_hovered);
        data.pointerDrag = _dragTarget;
        data.useDragThreshold = false; // VR laser: begin dragging on the first move, no pixel threshold
        if (_dragTarget != null)
            ExecuteEvents.ExecuteHierarchy(_hovered, data, ExecuteEvents.initializePotentialDrag);
    }

    /// <summary>
    /// Drive a drag while the trigger is held (called every frame by RayUguiDriver with
    /// the same clamped-into-rect screen point used for the raycast, so deltas are
    /// consistent). Updates position + delta, fires beginDrag on the first movement and
    /// dragHandler every frame thereafter. A Slider handle / scrollbar / scroll-rect
    /// moves here — it responds to OnDrag, never to Down/Up/Click. No-op when nothing
    /// draggable sits under the press.
    /// </summary>
    internal void Drag(Vector2 screenPos)
    {
        if (_pressed == null || _dragTarget == null)
        {
            _lastDragPos = screenPos;
            return;
        }

        PointerEventData data = GetData();
        data.delta = screenPos - _lastDragPos;
        data.position = screenPos;
        _lastDragPos = screenPos;

        if (!_dragging)
        {
            _dragging = true;
            data.dragging = true;
            ExecuteEvents.Execute(_dragTarget, data, ExecuteEvents.beginDragHandler);
            Core.VRLog.Info("Interact", $"uGUI drag begin: '{_dragTarget.name}' ({_sourceTag}).");
        }
        ExecuteEvents.Execute(_dragTarget, data, ExecuteEvents.dragHandler);
    }

    /// <summary>
    /// Task #8 (thumbstick scrolling): synthesize a mouse-wheel scroll on the hovered
    /// object — <c>scrollDelta</c> in wheel notches, dispatched up the hierarchy to the
    /// nearest <see cref="IScrollHandler"/> (a <see cref="ScrollRect"/> multiplies it by
    /// its own <c>scrollSensitivity</c> px/notch), exactly how StandaloneInputModule
    /// forwards <c>Input.mouseScrollDelta</c>. Mirrors the established ExecuteEvents
    /// click/drag pattern above; no-op when nothing is hovered. The delta is cleared
    /// afterwards so the shared PointerEventData never leaks a stale scroll into the
    /// next press/drag event.
    /// </summary>
    internal void Scroll(Vector2 scrollDelta)
    {
        if (_hovered == null)
            return;
        PointerEventData data = GetData();
        data.scrollDelta = scrollDelta;
        ExecuteEvents.ExecuteHierarchy(_hovered, data, ExecuteEvents.scrollHandler);
        data.scrollDelta = Vector2.zero;
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

        // End any active drag (StandaloneInputModule fires endDrag after up+click) and
        // settle the drag fields so the control comes to rest.
        if (_dragging && _dragTarget != null)
            ExecuteEvents.Execute(_dragTarget, data, ExecuteEvents.endDragHandler);
        _dragging = false;
        data.dragging = false;
        data.pointerDrag = null;
        _dragTarget = null;

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
            if (_dragging && _dragTarget != null)
                ExecuteEvents.Execute(_dragTarget, data, ExecuteEvents.endDragHandler);
            data.dragging = false;
            data.pointerDrag = null;
            data.pointerPress = null;
            data.eligibleForClick = false;
            _dragging = false;
            _dragTarget = null;
            _pressed = null;
            _pressedClickHandler = null;
        }
        SetHovered(null);
    }

    /// <summary>
    /// This pointer's hand aim ray (world origin/direction), or false when the hand is
    /// untracked / its ray inactive. Only the FAR-ray depth pick calls this; the poke
    /// path never does. RayUguiDriver only ticks the primary hand's far ray on
    /// registered surfaces, so this reads the very ray that produced the pending pick.
    /// </summary>
    private bool TryAimRay(out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;
        RayInteractor? ray = VRHands.Get(_side)?.Ray;
        if (ray == null || !ray.TryGetPick(out PickPose pick))
            return false;
        origin = pick.Origin;
        direction = pick.Direction;
        return true;
    }

    private PointerEventData GetData()
    {
        // Created lazily: EventSystem.current may not exist during early boot.
        if (_pointerData == null)
            _pointerData = new PointerEventData(EventSystem.current) { pointerId = _pointerId };
        return _pointerData;
    }
}

/// <summary>
/// Per-portrait depth-aware pick geometry for converted panels whose entries carry
/// REAL 3D depth (user #3 follow-up). The initiative track authors each portrait with
/// a stepped local z; on the world-space host that becomes literal geometry, and the
/// flat host-plane laser pick (RayUguiDriver's screen point → GraphicRaycaster) then
/// resolves the wrong portrait under perspective. A surface implements
/// <see cref="IDepthPortraitPicker"/> and registers it against its HOST canvas here;
/// the far-ray <see cref="UguiPointer"/> intersects the true aim ray with each entry's
/// world rect at its real depth so the exact portrait pointed at is the one selected.
/// Only hosts with an entry take the depth path — every other panel keeps the flat pick.
/// </summary>
internal interface IDepthPortraitPicker
{
    /// <summary>
    /// Intersect the world aim ray with the per-portrait world geometry (each at its
    /// real depth). On the NEAREST hit output the clickable portrait GameObject and the
    /// world hit point; return false when the ray meets no portrait (the far-ray caller
    /// then falls back to the ordinary flat GraphicRaycaster pick).
    /// </summary>
    bool TryPickPortrait(Vector3 rayOrigin, Vector3 rayDirection, out GameObject target, out Vector3 worldHit);
}

/// <summary>
/// Registry of depth-aware portrait pickers keyed by their HOST canvas — the same
/// canvas RayUguiDriver intersects and hands to <see cref="UguiPointer.TryRaycast"/>.
/// A surface registers on conversion and unregisters on release, so an entry exists
/// only while that panel is live and depth-picking stays scoped to it.
/// </summary>
internal static class DepthPortraitPicks
{
    private static readonly Dictionary<Canvas, IDepthPortraitPicker> Pickers = new(2);

    public static void Register(Canvas host, IDepthPortraitPicker picker)
    {
        if (host != null && picker != null)
            Pickers[host] = picker;
    }

    public static void Unregister(Canvas host)
    {
        // Reference-based remove: a released host canvas is already Unity-destroyed, but
        // its reference still hashes, so the key is cleared instead of leaking.
        if (host is not null)
            Pickers.Remove(host);
    }

    internal static bool TryGet(Canvas host, out IDepthPortraitPicker picker)
    {
        picker = null!;
        return host != null && Pickers.TryGetValue(host, out picker!) && picker != null;
    }
}
