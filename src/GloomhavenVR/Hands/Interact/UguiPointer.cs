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

    /// <summary>
    /// USER "Hover-Animation der Entscheidungsknöpfe" — the ANCESTOR CHAIN this pointer
    /// currently holds entered, leaf-FIRST (index 0 = the raycast target, last = the
    /// outermost ancestor). See <see cref="SetHovered"/> for the root cause; the list
    /// exists because the exit walk must be able to run even when the entered objects
    /// were meanwhile Unity-destroyed (a released/closed panel), where walking
    /// <c>_hovered.transform</c> would throw.
    /// </summary>
    private readonly List<GameObject> _hoverChain = new(8);

    // Throttled hover log (see LogHover): last emitted key + time, and how many lines the
    // rate cap swallowed since (reported on the next emitted line, so the log never lies
    // about the true event rate).
    private string? _lastHoverLogKey;
    private float _lastHoverLogTime = -99f;
    private int _hoverLogsSuppressed;

    /// <summary>Dedupe window: the SAME enter/exit on the SAME widget is quiet for this long.</summary>
    private const float HoverLogDedupeSeconds = 1f;

    /// <summary>Hard rate cap per pointer, so a jittering beam sweeping a row cannot flood the log.</summary>
    private const float HoverLogMinIntervalSeconds = 0.2f;

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

    /// <summary>
    /// Update hover state to <paramref name="target"/> (null = nothing hovered), firing
    /// the game's own pointerEnter/pointerExit along the ANCESTOR CHAIN — the exact
    /// dispatch <see cref="UnityEngine.EventSystems.BaseInputModule"/>.HandlePointerExitAndEnter
    /// performs for a real mouse.
    ///
    /// ROOT CAUSE (user: "wenn der Laser drauf ist, sollte die Hover-Animation genau so
    /// triggern wie bei physischem Kontakt"). This method used to dispatch with
    /// <c>ExecuteEvents.Execute(_hovered, …)</c> — which delivers to THAT ONE GameObject
    /// and to nothing else. The game's hover feedback, however, does NOT live on the
    /// object a GraphicRaycaster returns. Verified in the decompiled GH.Runtime:
    ///   • <c>ExtendedButton : Button</c> — <c>OnPointerEnter → OnHighlight() →
    ///     onMouseEnter.Invoke() + PlaySound(mouseEnterAudio) + ToggleHighlight(true)</c>,
    ///     a LeanTween scale to <c>highlightScaleFactor</c>. The handler sits on the
    ///     BUTTON; the raycast hit is whichever child Graphic is topmost under the point
    ///     (the TextMeshProUGUI label — <c>raycastTarget</c> is on by default — or the
    ///     plate/background Image, or, for a Toggle, its Background child).
    ///   • <c>HoverEffect</c>, <c>MouseOverUIElement</c>, <c>UIHighlightTransition</c>,
    ///     <c>UITooltipTarget</c> — standalone <c>IPointerEnterHandler</c>/
    ///     <c>IPointerExitHandler</c> MonoBehaviours that the game routinely puts on a
    ///     WRAPPER above the graphic they animate.
    /// So the leaf-only Execute landed on an object with no handler and the animation
    /// never ran. Nothing about the laser path was broken per se — the identical defect
    /// was in the poke path.
    ///
    /// WHY PHYSICAL CONTACT NEVERTHELESS "WORKED": a poke does not stop at hover. Plane
    /// contact ARMS the press (<see cref="PokeInteractor"/>), and <see cref="Press"/>
    /// dispatches with <c>ExecuteEvents.ExecuteHierarchy</c>, which DOES walk up to the
    /// real widget: the game's <c>Selectable</c> gets <c>OnPointerDown</c> → pressed
    /// state transition, and <c>Selectable.OnPointerDown</c> also calls
    /// <c>EventSystem.SetSelectedGameObject</c> → <c>OnSelect</c>, which
    /// <c>HoverEffect</c>/<c>MouseOverUIElement</c>/<c>UIHighlightTransition</c> answer
    /// with the very same <c>Animate(effects)</c> the hover uses. Touching therefore
    /// produced the animation as a side effect of pressing, while the laser — which only
    /// hovers until the trigger is pulled — produced nothing.
    ///
    /// FIX: mirror uGUI. Exit walks from the previously entered leaf up to the common
    /// root with the new target; enter walks from the new target up to (excluding) that
    /// common root. Consequences that matter here:
    ///   • the real widget (button/toggle/wrapper) receives enter/exit, so the game's own
    ///     animation, hover sound and tooltip run — nothing is reimplemented or faked;
    ///   • <c>PointerEventData.hovered</c>/<c>pointerEnter</c> are maintained exactly as
    ///     the input module does, so any game code reading them sees a coherent pointer;
    ///   • the common-root stop means beam jitter INSIDE one widget (label ↔ plate) fires
    ///     no handler events at all — strictly less thrash than the old leaf dispatch.
    ///
    /// DOUBLE-FIRE: enter/exit are arbitrated across ALL mod pointers by
    /// <see cref="UguiHoverTracker"/> — the first pointer to reach an object dispatches
    /// enter, the last to leave dispatches exit. Poking a widget the laser already hovers
    /// (or both hands on it) therefore highlights it once and un-highlights it once.
    ///
    /// LEAK-FREE: everything entered is recorded in <see cref="_hoverChain"/> and released
    /// through this same method — <c>SetHovered(null)</c> from <see cref="Cancel"/> is on
    /// every teardown path (hand switch, ray inactive, canvas lost/closed, surface
    /// released, interactor disabled), and the exit walk uses the recorded chain rather
    /// than live transforms so a destroyed panel still balances its counts.
    /// </summary>
    internal void SetHovered(GameObject? target)
    {
        if (ReferenceEquals(target, _hovered))
            return;

        PointerEventData data = GetData();

        // Common root of old and new hover. Unity-null-safe: a DESTROYED previous hover
        // has no usable transform, so treat it as "no common root" and fully exit the
        // recorded chain.
        GameObject? commonRoot = _hovered != null ? FindCommonRoot(_hovered, target) : null;

        // ---- exit: recorded chain, leaf-first, up to (excluding) the common root -------
        GameObject? exitedWidget = null;
        bool exitDispatched = false;
        while (_hoverChain.Count > 0)
        {
            GameObject go = _hoverChain[0];
            if (commonRoot != null && ReferenceEquals(go, commonRoot))
                break; // this and everything above it stay entered (shared with the new target)
            _hoverChain.RemoveAt(0);
            RemoveByRef(data.hovered, go);
            if (!UguiHoverTracker.Release(go) || go == null)
                continue; // another mod pointer still hovers it, or it is gone — no event
            ExecuteEvents.Execute(go, data, ExecuteEvents.pointerExitHandler);
            exitDispatched = true;
            if (exitedWidget == null && IsHoverHandler(go))
                exitedWidget = go;
        }
        if (_hovered != null && exitedWidget == null && _hoverChain.Count == 0)
            exitedWidget = _hovered; // nothing carried a handler — name the leaf we left

        // ---- enter: new target up to (excluding) the common root -----------------------
        _hovered = target;
        data.pointerEnter = target;
        GameObject? enteredWidget = null;
        bool enterDispatched = false;
        if (target != null)
        {
            int insert = 0;
            Transform? t = target.transform;
            while (t != null && !ReferenceEquals(t.gameObject, commonRoot))
            {
                GameObject go = t.gameObject;
                _hoverChain.Insert(insert++, go);
                data.hovered.Add(go);
                if (UguiHoverTracker.Acquire(go))
                {
                    ExecuteEvents.Execute(go, data, ExecuteEvents.pointerEnterHandler);
                    enterDispatched = true;
                    if (enteredWidget == null && IsHoverHandler(go))
                        enteredWidget = go;
                }
                t = t.parent;
            }
            if (enteredWidget == null)
                enteredWidget = target; // no handler in the fresh part of the chain
        }

        if (exitedWidget != null)
            LogHover("EXIT", exitedWidget, exitDispatched);
        if (enteredWidget != null)
            LogHover("ENTER", enteredWidget, enterDispatched);
    }

    /// <summary>
    /// Reference-based removal from <c>PointerEventData.hovered</c>. <c>List.Remove</c>
    /// would use <c>UnityEngine.Object.Equals</c>, under which two DESTROYED objects
    /// compare equal (both wrap a null native pointer) — that could drop the wrong entry
    /// when a panel is torn down while hovered. Identity comparison cannot.
    /// </summary>
    private static void RemoveByRef(List<GameObject> list, GameObject go)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], go))
            {
                list.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>Does this GameObject itself answer pointer enter/exit (i.e. is it "the widget")?</summary>
    private static bool IsHoverHandler(GameObject go) =>
        go != null && go.GetComponent<IPointerEnterHandler>() != null;

    /// <summary>
    /// Nearest shared ancestor GameObject of two hover targets, or null when they share
    /// none — our own copy of <c>BaseInputModule.FindCommonRoot</c> (protected there, so
    /// not reachable from a plain class). Unity-null tolerant on both sides.
    /// </summary>
    private static GameObject? FindCommonRoot(GameObject? a, GameObject? b)
    {
        if (a == null || b == null)
            return null;
        Transform? ta = a.transform;
        while (ta != null)
        {
            Transform? tb = b.transform;
            while (tb != null)
            {
                if (ReferenceEquals(ta, tb))
                    return ta.gameObject;
                tb = tb.parent;
            }
            ta = ta.parent;
        }
        return null;
    }

    /// <summary>
    /// Hardware-log verification for the hover fix: names the WIDGET that took the
    /// enter/exit and the path it came from ("laser-R" / "poke-L"), so the next log
    /// answers "did pointing actually reach the game's hover handler" without guesswork.
    /// Throttled twice over — the same widget+phase is quiet for
    /// <see cref="HoverLogDedupeSeconds"/>, and a hard
    /// <see cref="HoverLogMinIntervalSeconds"/> cap bounds a jittering beam sweeping a
    /// widget row; every swallowed line is counted and reported on the next emitted one,
    /// so the log never understates the real event rate.
    /// </summary>
    private void LogHover(string phase, GameObject widget, bool dispatched)
    {
        float now = Time.unscaledTime;
        string key = phase + ' ' + widget.name;
        if (key == _lastHoverLogKey && now - _lastHoverLogTime < HoverLogDedupeSeconds)
        {
            _hoverLogsSuppressed++;
            return;
        }
        if (now - _lastHoverLogTime < HoverLogMinIntervalSeconds)
        {
            _hoverLogsSuppressed++;
            return;
        }
        int swallowed = _hoverLogsSuppressed;
        _hoverLogsSuppressed = 0;
        _lastHoverLogKey = key;
        _lastHoverLogTime = now;
        Core.VRLog.Info("Interact",
            $"uGUI hover {phase}: '{widget.name}' ({_sourceTag})" +
            (dispatched ? string.Empty : " [already entered (shared ancestor or another pointer) — not re-sent]") +
            (swallowed > 0 ? $" (+{swallowed} throttled)" : string.Empty) + ".");
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

        // A laser is not a finger on glass: it never holds perfectly still, and with
        // useDragThreshold = false EVERY press moves a pixel or two. On a list that means each
        // attempt to click an option also pans the list under the cursor, which is what makes
        // the options menu hard to hit. So a press whose only drag handler is the SCROLL VIEW
        // itself does not take the drag — the list then scrolls with the stick, which is
        // precise, and the press stays a clean click.
        //
        // GetEventHandler returns the NEAREST ancestor that handles IDragHandler, so a slider,
        // scrollbar or dropdown inside a scroll view still resolves to itself and keeps
        // dragging normally. Only the scroll view loses it.
        if (Plugin.ScrollWithStickOnly.Value && _dragTarget != null &&
            _dragTarget.GetComponent<ScrollRect>() != null)
        {
            _dragTarget = null;
        }

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
            // The on-screen keyboard opens and closes on clicks. This is the world-space half of
            // that (converted panels, fingertip poke); FlatScreen.DirectClick is the flat-screen
            // half. Told before the click is delivered, so the keyboard is already up when the field
            // processes it. _hovered rather than the click handler: the keyboard needs the exact
            // object hit, not the ancestor that happens to handle clicks.
            WorldUI.VRKeyboard.NoticeClick(_hovered);

            ExecuteEvents.Execute(_pressedClickHandler, data, ExecuteEvents.pointerClickHandler);
            // Test #18 verification: every synthesized uGUI click carries its
            // provenance in the log — "did the initiative portrait click reach the
            // game's handler" is answerable from the log alone (the P7 idiom:
            // every interaction is logged).
            // [Optimize] QuietDiagnostics: one line per synthesized click. Event-driven (not
            // per-frame) and the single most-repeated mod line in the hardware log, so it is the
            // obvious thing to silence for a clean performance capture; on by default because it is
            // the verification trace for the whole far-click path (test #18).
            if (!Core.PerfConfig.Quiet)
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
        // Releases the whole recorded ancestor chain (pointerExit up the hierarchy, counts
        // handed back to UguiHoverTracker) — the single teardown every caller funnels
        // through, so no widget can stay stuck highlighted when the beam moves away, the
        // hand switches, the panel closes or the surface is released.
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
/// CROSS-POINTER HOVER ARBITRATION (user "Hover-Animation der Entscheidungsknöpfe").
/// The mod runs up to four uGUI pointers at once — a fingertip poke and a far laser per
/// hand, each with its own pointer ID and its own <see cref="PointerEventData"/>. Without
/// arbitration, poking a widget the laser already points at would send the game a SECOND
/// <c>pointerEnter</c> (a second highlight tween + a second hover sound), and pulling one
/// of the two away would send a <c>pointerExit</c> that un-highlights a widget the other
/// pointer is still on.
///
/// So enter/exit are reference-counted per GameObject: the FIRST pointer to enter an
/// object dispatches <c>pointerEnter</c>, the LAST to leave dispatches <c>pointerExit</c>,
/// and everything in between is silent. This is the whole "do not double-fire" rule; it
/// costs one dictionary probe per chain element per hover change.
///
/// Keyed by instance ID, never by the GameObject: <c>UnityEngine.Object</c> overrides
/// equality with fake-null semantics under which two DESTROYED objects compare EQUAL,
/// which would corrupt a dictionary keyed by the object itself. Instance IDs stay stable
/// and unique across destruction, and every count is handed back through
/// <see cref="UguiPointer.SetHovered"/>/<see cref="UguiPointer.Cancel"/>, so the table
/// drains to empty whenever nothing is hovered.
/// </summary>
internal static class UguiHoverTracker
{
    private static readonly Dictionary<int, int> Counts = new(32);

    /// <summary>Claim a hover on <paramref name="go"/>. True when this is the FIRST claim — the caller then sends pointerEnter.</summary>
    internal static bool Acquire(GameObject go)
    {
        if (go is null)
            return false;
        int id = go.GetInstanceID();
        if (Counts.TryGetValue(id, out int n))
        {
            Counts[id] = n + 1;
            return false;
        }
        Counts[id] = 1;
        return true;
    }

    /// <summary>Hand a hover back. True when this was the LAST claim — the caller then sends pointerExit.</summary>
    internal static bool Release(GameObject go)
    {
        // Unity-null tolerant on purpose: a destroyed object must still balance its count,
        // and GetInstanceID() keeps working on the destroyed managed wrapper.
        if (go is null)
            return false;
        int id = go.GetInstanceID();
        if (!Counts.TryGetValue(id, out int n))
            return false;
        if (n <= 1)
        {
            Counts.Remove(id);
            return true;
        }
        Counts[id] = n - 1;
        return false;
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
