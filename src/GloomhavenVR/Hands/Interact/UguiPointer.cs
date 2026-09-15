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

    /// <summary>
    /// Every mod hand pointer id is &lt;= this; the game's MOUSE pointer (-1..-3) and any
    /// touch pointer (&gt;= 0) are above it. THE single definition — <see cref="Cards.CardFaceRaycaster"/>
    /// and <c>WorldUI.Patches.TooltipRaiseGuard</c> both read it here rather than mirroring the
    /// number, because "is this event ours" is the question both of them decide on.
    /// </summary>
    internal const int ModPointerIdCeiling = -100;

    /// <summary>
    /// Was this uGUI event synthesized by one of the MOD's hand pointers (poke or laser) —
    /// as opposed to the game's own mouse/touch pointer?
    ///
    /// <para>The distinction is load-bearing in VR and not a detail: the game's EventSystem
    /// mouse pointer runs EVERY frame at the PARKED desktop mouse pixel
    /// (<c>InControlInputModule.ProcessMouseEvent</c> → <c>GetMousePointerEventData</c> →
    /// <c>InputSystemUtilities.GetMousePosition</c>) and its <c>RaycastAll</c> reaches every
    /// enabled <see cref="GraphicRaycaster"/>, including the mod's WORLD-SPACE surfaces whose
    /// <c>worldCamera</c> is the HEAD camera. A fixed pixel through a moving head is a world ray
    /// that sweeps the room on its own, with no user input at all — which is exactly the defect
    /// <see cref="Cards.CardFaceRaycaster"/> was written for on card faces.</para>
    /// </summary>
    internal static bool IsModPointerId(int pointerId) => pointerId <= ModPointerIdCeiling;

    /// <summary>
    /// Is <paramref name="pointerId"/> one of the mod's FAR-RAY (laser) pointers?
    /// Consumed by <c>Cards.Patches</c> (laser-half-hover suppression): the docked
    /// half-selection cards take their half highlight from the beam's GEOMETRY, so the
    /// game's per-graphic <c>FullCardEventPusher</c> must be able to tell a laser event
    /// from a poke/mouse event. Kept here, next to the ID block, so the two can never
    /// drift apart.
    /// </summary>
    internal static bool IsLaserPointerId(int pointerId) =>
        pointerId == LeftHandRayPointerId || pointerId == RightHandRayPointerId;

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

    // ---- EXIT HYSTERESIS (ModBuild 203) ---------------------------------------------------
    //
    // THE DEFECT, MEASURED. In the ModBuild 202 hardware log the beam parked on the map room's
    // mercenary roster produced 77 hover transitions in 2.16 s (~35.6/s at 90 Hz) and the
    // character display was rebuilt 28 times in 1.01 s. The log names both halves of the
    // oscillation directly:
    //
    //     uGUI hover EXIT:  'UI Campaign PartyRoster Slot' (laser-R) (+11 throttled).
    //     uGUI hover ENTER: 'UI Party Roster' (laser-R) [already entered ... — not re-sent]
    //     uGUI hover ENTER: 'UI Campaign PartyRoster Slot' (laser-R) (+15 throttled).
    //
    // 'UI Party Roster' is the slot's own ANCESTOR — that is exactly what the "already entered
    // (shared ancestor)" note means. So the beam is not moving between two widgets: it falls off
    // the slot onto the list background the slot sits in, and back on again.
    //
    // WHY IT IS A CLOSED LOOP, AND WHY ONLY IN VR. The game's own
    // UIAdventurePartyAssemblyRosterSlot.OnPointerEnter (:266-284) GROWS the slot
    // (animationRect.sizeDelta += hoverFactor), re-pivots its portrait, and then calls
    // _scrollRect.ScrollToFit(...) so the now-taller slot fits — i.e. the ENTER moves the slot
    // out from under a stationary ray. OnPointerExit → Unhighlight() (:302-313) shrinks it back,
    // the layout reflows, and the slot returns under the ray. On a desktop the cursor moves with
    // the layout or the hand is never that still; a VR laser is a ray from a hand held roughly
    // still in world space, so enter→exit→enter closes. Each lap costs two full
    // UIAdventurePartyAssemblyCharacterDisplay.Display() rebuilds (six TMP strings each, several
    // with <sprite> tags, plus a live 3D model swap).
    //
    // THE RULE. A hover change that ENTERS NO NEW WIDGET is a pure LOSS of hover: the new target
    // is either nothing at all, or an object this pointer ALREADY holds entered (an ancestor in
    // _hoverChain — the enter walk stops at the common root and would dispatch nothing). Those,
    // and only those, are held back for ExitHysteresisFrames consecutive frames before the exit
    // is dispatched. A target in a DIFFERENT subtree — a real second widget — switches on the
    // same frame it is seen, with no delay whatsoever, so pointing from one button to the next
    // never feels laggy.
    //
    // WHAT THE THRESHOLD CAN AND CANNOT SWALLOW. The measured storm flips state every 2-3 frames
    // at 90 Hz (35.6 transitions/s ≈ 17.8 laps/s ≈ 5 frames/lap). Six frames ≈ 67 ms at 90 Hz,
    // so this swallows any loss phase of 5 frames or fewer — every oscillation faster than about
    // 9 laps/s, which covers the measured one with better than 2x margin. It does NOT swallow a
    // loop whose loss phase lasts 6 frames or longer (slower than ~7.5 laps/s at 90 Hz, ~3.7 at
    // 45 Hz): such a loop still produces one exit per lap. It also does not, and must not, damp
    // an A↔B alternation between two genuinely different widgets — for the party roster that
    // second belt is WorldUI.Patches.PartyPreviewStorm, which suppresses the redundant rebuild
    // itself. Note the hold is self-reinforcing in the right direction: while the exit is held
    // the game keeps the slot grown, so ScrollToFit does not run again and the layout settles.
    //
    // BALANCE, TEARDOWN AND CLICKS. Holding an exit changes only WHEN the exit is dispatched,
    // never WHETHER: the chain stays recorded in _hoverChain, its UguiHoverTracker counts stay
    // taken, and every teardown path funnels through Cancel(), which forces the exit out. A
    // DESTROYED hover is never held (the Unity-null test on _hovered below falls straight
    // through to the dispatch), so a closed panel still balances its counts on the frame it
    // dies. Press() and Release() flush the pending exit BEFORE they do anything, so a press or
    // a click inside the hysteresis window resolves to exactly the object today's code would
    // have used — clicks are neither delayed nor invented.
    //
    // LOCAL ONLY. This is input dispatch on this client. Nothing here reads or writes game
    // state, so there is nothing for a peer to observe and nothing goes on the wire.

    /// <summary>
    /// Consecutive frames a pure hover LOSS must persist before the exit is dispatched. Six
    /// frames ≈ 67 ms at 90 Hz — see the block comment above for what that swallows.
    /// </summary>
    internal const int ExitHysteresisFrames = 6;

    /// <summary>How many consecutive <see cref="SetHovered"/> calls have asked for a pure loss.</summary>
    private int _lossFrames;

    /// <summary>The target the most recent held-back <see cref="SetHovered"/> asked for, so a
    /// press/release can flush straight to it.</summary>
    private GameObject? _pendingTarget;

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
        // ModBuild 403: the LASER skips poke-only pads (Cards.PokePads) and takes the next hit —
        // a pad enlarged for the fingertip must not enlarge what the beam can click.
        if (_farRay)
        {
            for (int i = 0; i < _hits.Count; i++)
            {
                GameObject? go = _hits[i].gameObject;
                if (go != null && go.GetComponent<PokeOnlyTarget>() != null)
                    continue;
                top = _hits[i];
                return true;
            }
            top = default;
            return false;
        }
        // ModBuild 405: the FINGER prefers a poke-only pad wherever it ranks. The pad is a CHILD of
        // the small default-action button, so in the depth sort it sits right behind that button
        // and under every later sibling that overlaps the plate (the half's big action button, the
        // face-wide header graphic); taking _hits[0] there hands the finger to the big area at any
        // pixel inside the plate. A pad exists only where the design wants the finger to win, so
        // its mere presence in the list decides. The laser branch above is untouched.
        _lastPokeHitCount = _hits.Count;
        _lastPokePadRank = -1;
        for (int i = 0; i < _hits.Count; i++)
        {
            GameObject? go = _hits[i].gameObject;
            if (go != null && go.GetComponent<PokeOnlyTarget>() != null)
            {
                _lastPokePadRank = i;
                break;
            }
        }
        top = _lastPokePadRank >= 0 ? _hits[_lastPokePadRank] : _hits[0];
        // The runner-up for the POKE PICK line: what would have won without the pad rule, or the
        // next hit down when no pad was in the list.
        _lastPokeRunnerUp = _lastPokePadRank > 0 ? _hits[0].gameObject
            : _hits.Count > 1 ? _hits[1].gameObject : null;
        return true;
    }

    // ---- POKE PICK bookkeeping (ModBuild 405) — written by TryRaycastTop, read by the log --
    private int _lastPokeHitCount;
    private int _lastPokePadRank = -1;
    private GameObject? _lastPokeRunnerUp;
    private static int s_pokePickLogsLeft = PokePickLogBudget;
    private static string? s_lastPokePickKey;
    private const int PokePickLogBudget = 10;

    /// <summary>
    /// The POKE PICK line: on a fingertip click, the winner, the hit that lost to it and whether the
    /// finger lay inside a default action's padded rect — which is exactly "a poke-only pad was in
    /// the hit list", since the pad IS that rect. Change-gated on (handler, pad present, runner-up)
    /// and capped per session. Event-driven: one candidate line per synthesized click.
    /// </summary>
    private void LogPokePick(GameObject handler, GameObject hit)
    {
        if (s_pokePickLogsLeft <= 0)
            return;
        bool padInList = _lastPokePadRank >= 0;
        string runner = _lastPokeRunnerUp != null ? _lastPokeRunnerUp.name : "<none>";
        // THE SEPARATOR IS SPELLED '\u0000' AND MUST STAY SPELLED THAT WAY. Written as a raw
        // '\0' it puts an actual NUL BYTE in the source file, which makes the whole file `data`
        // rather than `text`: `file` says so, and GNU grep then treats it as binary and prints
        // NOTHING without -a. Every grep-based lint, census and instrument under scripts/ was
        // therefore silently skipping this file — not failing on it, skipping it, which is the
        // worse of the two. Found by the 2026-09-05 redundancy survey, whose author noticed the
        // file never appeared in any result. The escape compiles to the same char; only the
        // bytes on disk differ.
        string key = handler.name + '\u0000' + padInList + '\u0000' + runner;
        if (key == s_lastPokePickKey)
            return;
        s_lastPokePickKey = key;
        s_pokePickLogsLeft--;
        GameObject? runnerHandler = _lastPokeRunnerUp != null
            ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(_lastPokeRunnerUp)
            : null;
        string displaced = _lastPokePadRank > 0
            ? $"the pad ranked #{_lastPokePadRank} (0 = topmost) in the GraphicRaycaster's depth sort and was "
              + $"PROMOTED over '{runner}' (which would have clicked "
              + $"'{(runnerHandler != null ? runnerHandler.name : "<no click handler>")}')"
            : padInList
                ? "the pad was already the topmost hit; runner-up '" + runner + "'"
                : $"no pad in the list; runner-up '{runner}'";
        // HW-VERIFY: the pick behind every fingertip click. 'inside a default action's padded rect:
        // YES' with a click on anything but that default action would be the pick losing again;
        // 'no' on a poke the player aimed at the plate means the finger was outside the padded
        // rect, so the pad size is the question, not the pick. Zero hits cannot reach this line.
        Core.VRLog.Note("Interact",
            $"POKE PICK ({_sourceTag}): click delivered to '{handler.name}' through hit '{hit.name}' — "
            + $"{_lastPokeHitCount} raycast hit(s) under the fingertip, {displaced}; fingertip inside a "
            + $"default action's padded rect: {(padInList ? "YES" : "no")} ({s_pokePickLogsLeft} more of "
            + "these lines).");
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
        return StableOrder(challenger) >= StableOrder(incumbent);
    }

    /// <summary>
    /// The sortingOrder this comparison must use, which since the transparency round is NOT always
    /// the live one. ROOT CAUSE: converted panel HOSTS no longer hold a fixed order - WorldUI's
    /// CanvasConversion.8.Order.cs rewrites every host canvas's sortingOrder each frame from its eye
    /// distance, so that panels occlude each other by perspective without any of them writing depth.
    /// Every threshold <see cref="Beats"/> encodes was calibrated against the CONVERSION-TIME order
    /// instead (the initiative track's inner canvas at 40 beating an order-0 host; the element
    /// board's -1 underlay staying behind host content; the modal X's hit plane at 1100 beating the
    /// adopted window content at 1000), and a live ladder value in the hundreds would silently
    /// invert all three. Asking WorldUI for the host's conversion tier keeps every raycast decision
    /// bit-identical to the shipped builds while the draw order moves freely. Nested and non-panel
    /// canvases are not converted hosts, so they keep reporting their own order - which is exactly
    /// what the comparison expects of them.
    /// </summary>
    private static int StableOrder(in RaycastResult hit)
    {
        BaseRaycaster? module = hit.module;
        if (module != null
            && WorldUI.CanvasConversion.BaseSortingOrderOf(module.gameObject, out int baseOrder))
            return baseOrder;
        return hit.sortingOrder;
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
    ///
    /// EXIT HYSTERESIS (ModBuild 203): a hover change that would enter NO new widget — a null
    /// target, or an ancestor this pointer already holds entered — is held back for
    /// <see cref="ExitHysteresisFrames"/> consecutive frames before the exit is dispatched, so a
    /// hand held still cannot be made to chatter by a hover target that MOVES ITSELF (the party
    /// roster's <c>ScrollToFit</c>). A target in a DIFFERENT subtree switches on the same frame,
    /// and <see cref="Press"/>/<see cref="Release"/> flush a pending exit before they act, so no
    /// input is ever delayed. The measured rate, the balance argument and what the threshold can
    /// and cannot swallow are in the block comment on <see cref="ExitHysteresisFrames"/>.
    /// </summary>
    internal void SetHovered(GameObject? target) => SetHovered(target, force: false);

    /// <summary>
    /// The real hover update — see the public overload above for the dispatch contract and the
    /// <c>ExitHysteresisFrames</c> block comment for the hold rule.
    /// </summary>
    /// <param name="target">The object under the pointer this frame, or null for nothing.</param>
    /// <param name="force">Skip the exit hysteresis and dispatch on this frame. Set by every
    /// teardown path (<see cref="Cancel"/>) and by the press/release flush, so a held exit can
    /// never outlive the pointer, the panel or the click that needs it resolved.</param>
    private void SetHovered(GameObject? target, bool force)
    {
        if (ReferenceEquals(target, _hovered))
        {
            // Back on the widget we still hold: the loss never completed, so forget it happened.
            _lossFrames = 0;
            _pendingTarget = null;
            return;
        }

        // EXIT HYSTERESIS (see the block comment on ExitHysteresisFrames). The Unity-null test on
        // _hovered is load-bearing: a DESTROYED hover must never be held, or its refcount would
        // not be handed back on the frame the panel dies.
        if (!force && _hovered != null && IsPureHoverLoss(target))
        {
            _pendingTarget = target;
            if (++_lossFrames < ExitHysteresisFrames)
            {
                WorldUI.Patches.PartyPreviewStorm.NoteHoverHeld();
                return;
            }
        }
        _lossFrames = 0;
        _pendingTarget = null;
        WorldUI.Patches.PartyPreviewStorm.NoteHoverDispatched();

        // THE EAR, BEFORE THE EVENT. ExtendedButton.OnPointerEnter/OnPointerExit play their authored
        // hover items SYNCHRONOUSLY inside the dispatch below, through AudioController.Play(id) —
        // which spawns a 3D source one world unit in front of the game's CACHED AudioListener. That
        // cache still points at the listener EnvSound disabled, so the sound is placed hundreds of
        // world units from the ear that hears (user report 2026-08-21: "Die Knöpfe machen keine
        // Geräusche"). See WorldUI.UiSoundEar for the whole reading; it dispatches nothing and plays
        // nothing, so it cannot change what this method sends or how often.
        WorldUI.UiSoundEar.BeforeUiEvent();

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

        // One-shot diagnostic only (see WorldUI.UiSoundEar.NoticeHoveredWidget): the first time the
        // beam or a fingertip lands on one of the game's audio-carrying button classes, the log
        // states which items that button is authored with, whether the game can play them, whether
        // the pooled source is 3D, and how far the game is placing them from the VR ear. It reads
        // serialized fields; it plays nothing and dispatches nothing.
        if (enteredWidget != null)
            WorldUI.UiSoundEar.NoticeHoveredWidget(enteredWidget);
    }

    /// <summary>
    /// Would moving the hover to <paramref name="target"/> enter NOTHING new — i.e. is this a
    /// pure LOSS of the hover this pointer currently holds?
    ///
    /// <para>True for a null target (the ray reached no graphic at all) and for any object
    /// already recorded in <see cref="_hoverChain"/>. The second case is the one the party-roster
    /// storm actually produces: the beam falls off a list entry onto the LIST ITSELF, which is an
    /// ancestor this pointer already holds entered, so <see cref="SetHovered"/>'s enter walk
    /// stops immediately at the common root and dispatches nothing. Either way the user gains no
    /// new widget, which is why holding the exit for a few frames cannot hide anything from them.
    /// A target in a different subtree returns false and switches on the same frame.</para>
    /// </summary>
    private bool IsPureHoverLoss(GameObject? target)
    {
        if (target == null)
            return true;
        // Identity, not Unity equality: two destroyed objects compare EQUAL under the overloaded
        // operator, which would misread an unrelated dead target as "an ancestor we hold".
        for (int i = 0; i < _hoverChain.Count; i++)
        {
            if (ReferenceEquals(_hoverChain[i], target))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolve any held-back exit right now, to the target the last frame actually asked for.
    /// Called first thing in <see cref="Press"/> and <see cref="Release"/> so a press, a drag and
    /// a click all see the object today's code would have given them: the hysteresis delays an
    /// EXIT, never an input.
    /// </summary>
    private void FlushPendingHoverExit()
    {
        if (_lossFrames == 0)
            return;
        GameObject? pending = _pendingTarget;
        _lossFrames = 0;
        _pendingTarget = null;
        SetHovered(pending, force: true);
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
        string key = phase + '\u0000' + widget.name;
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

    /// <summary>
    /// THE ONLY WRITER of <see cref="_pressed"/> — so the cross-pointer table in
    /// <see cref="UguiPressTracker"/> cannot desync from it.
    ///
    /// <para>The press is claimed and handed back here rather than at the four sites that used to
    /// assign the field (press, the swallowed menu-tab click, the end of a release, and the
    /// teardown in <see cref="Cancel"/>). Four writers and one table is how a refcount ends up
    /// stuck: one path that forgets to hand a claim back leaves a widget pressed forever on every
    /// mirrored board that reads the table. One writer makes that unreachable.</para>
    ///
    /// <para><c>is not null</c> and not <c>!= null</c>, exactly as <see cref="UguiHoverTracker"/>
    /// does it: a DESTROYED press target must still hand its claim back, and Unity fake-null
    /// equality would skip it.</para>
    /// </summary>
    private GameObject? Pressing
    {
        get => _pressed;
        set
        {
            if (ReferenceEquals(_pressed, value))
                return;
            if (_pressed is not null)
                UguiPressTracker.Release(_pressed);
            _pressed = value;
            if (_pressed is not null)
                UguiPressTracker.Acquire(_pressed);
        }
    }

    /// <summary>Pointer-down on the hovered object (mirrors StandaloneInputModule press handling).</summary>
    internal void Press(Vector2 screenPos)
    {
        // Never press through a held-back exit: settle the hover to what the ray really reports
        // first, so the pressed object is bit-for-bit the one the pre-hysteresis code would have
        // pressed. A press is never delayed by this — the flush is synchronous.
        FlushPendingHoverExit();

        if (_hovered == null || _pressed != null)
            return;

        // The press sound: UIButtonExtended.OnPointerDown (UIButtonExtended.cs:64-75) and
        // ExtendedButton.OnPointerDown (:200-222) play their mouseDown item inside the dispatch
        // below. Same repair, same reason as in SetHovered.
        WorldUI.UiSoundEar.BeforeUiEvent();

        PointerEventData data = GetData();
        data.position = screenPos;
        data.pressPosition = screenPos;
        data.pointerPressRaycast = data.pointerCurrentRaycast;
        data.eligibleForClick = true;
        data.button = PointerEventData.InputButton.Left;

        GameObject pressTarget = ExecuteEvents.ExecuteHierarchy(_hovered, data, ExecuteEvents.pointerDownHandler)
                                 ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hovered);
        Pressing = pressTarget != null ? pressTarget : _hovered;
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
        // Same reason as in Press: the click test below compares the click handler under
        // _hovered against the one the press captured, so a held-back exit must be settled first
        // or the hysteresis could KEEP a click the ray had already left (or, on the way back,
        // invent one). Flushing here makes release behaviour identical to the pre-hysteresis code.
        FlushPendingHoverExit();

        if (_pressed == null)
            return;

        // The release and click sounds: ExtendedButton.OnPointerUp (:224-236) plays the mouseUp item
        // and OnPointerClick (:168-184) plays the mouseClick item, both inside the dispatches below.
        WorldUI.UiSoundEar.BeforeUiEvent();

        PointerEventData data = GetData();
        data.position = screenPos;

        ExecuteEvents.Execute(_pressed, data, ExecuteEvents.pointerUpHandler);

        GameObject? hoveredClickHandler = _hovered != null
            ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hovered)
            : null;
        if (_pressedClickHandler != null && ReferenceEquals(hoveredClickHandler, _pressedClickHandler))
        {
            // OPEN-ONLY pause-menu tabs (user ruling 2026-08-04): a re-click on an ESC-menu tab
            // whose window is ALREADY open is swallowed HERE - the one choke point both mod
            // click paths (poke and laser) deliver pointerClick through - so the tab's Toggle
            // never flips and the game's Show/Hide (which resets the still-floating window's
            // content) never runs. ModalFallback resolves the tab's target window and logs the
            // decision; anything that is not an already-open tab passes through unchanged.
            // pointerUp above already ran, which is correct - a real input module also releases
            // the press; only the click itself is withheld.
            if (WorldUI.ModalFallback.ShouldSwallowMenuTabClick(_pressedClickHandler))
            {
                data.pointerPress = null;
                data.eligibleForClick = false;
                Pressing = null;
                _pressedClickHandler = null;
                _dragging = false;
                data.dragging = false;
                data.pointerDrag = null;
                _dragTarget = null;
                return;
            }

            // PHANTOM CLICK ON AN UN-STARTED WINDOW (ModBuild 389 follow-up). Same seam, same
            // shape and the same reason as the branch above: a pooled UIWindow subtree renders
            // for one frame at its prefab state, still hit-testable because only Start() drives
            // its CanvasGroup to alpha 0 / blocksRaycasts false. Such a click cannot delete
            // anything itself, but UIAdventureCharacterConfirmationBox.OnConfirmClick subscribes
            // to onTransitionComplete BEFORE the Hide() that no-ops, leaving a permanent listener
            // that turns the player's next ABBRECHEN into a character deletion. Withheld here, at
            // the mod's own click delivery; nothing game-side is written. See the method for the
            // term and for why both halves of it are needed.
            if (ShouldWithholdUnstartedWindowClick(_pressedClickHandler, _sourceTag))
            {
                data.pointerPress = null;
                data.eligibleForClick = false;
                Pressing = null;
                _pressedClickHandler = null;
                _dragging = false;
                data.dragging = false;
                data.pointerDrag = null;
                _dragTarget = null;
                return;
            }

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
            // ModBuild 405: the fingertip's pick, beside the click it produced (poke only — the
            // laser's resolution did not change and gets no new line).
            if (!_farRay && _hovered != null)
                LogPokePick(_pressedClickHandler, _hovered);

            // CLICK ACKNOWLEDGEMENT (user report 2026-08-09: "das Ablehnen-Geräusch … immer
            // dann … wenn man in der Initiativreihenfolge ein Bild von einem nicht-spielbaren
            // Character anklickt", re-keyed on the click's OUTCOME after the follow-up report "das
            // 'Abgelehnt-Geräusch' kommt wenn ich auf ein Character den ich selber nicht besitze —
            // obwohl es ja gar nicht (mehr) abgelehnt wird": a refused click gets the game's
            // refusal item, a successful one — including the read-only view of a character this
            // client does not control — gets the game's character-change item. The outcome is read
            // from the ledger the click seam itself wrote DURING the dispatch above, which is why
            // this call must stay on the line after it, in the same frame. It sits HERE, not in
            // RayUguiDriver or PokeInteractor, because
            // this is the one line both VR click paths pass through — the laser and the fingertip
            // poke — and the player will try both. A poke that PokeInteractor.PressAllowed
            // withheld never pressed and therefore never reaches this line, so a touch the grip
            // chord refused stays silent, which is correct: it was not a click. Called AFTER the
            // dispatch above so vanilla has already had its full turn at the event; the method
            // returns in two reference tests for every click that is not an initiative portrait,
            // and it cannot influence what the click did.
            WorldUI.Surfaces.InitiativePortraitClickSound.NoticeClick(_pressedClickHandler);
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
        Pressing = null;
        _pressedClickHandler = null;
    }


    // ---- PHANTOM CLICK ON A WINDOW WHOSE Start() HAS NOT RUN (ModBuild 389 follow-up) -------

    /// <summary>The (widget, window) pair the withheld-click line last named — the change gate.</summary>
    private static string? s_lastUnstartedKey;

    /// <summary>How many clicks this guard has withheld this session, INCLUDING the ones the
    /// change gate above did not print. Reported on every line it does print.</summary>
    private static int s_unstartedWithheld;

    /// <summary>
    /// Should this click be withheld because the window it lands in has never been through
    /// <c>UIWindow.Start()</c>? Called by <see cref="Release"/> immediately before the click
    /// would be dispatched, exactly like <c>ModalFallback.ShouldSwallowMenuTabClick</c> two
    /// lines above it — <c>pointerUp</c> has already gone out (a real input module also
    /// releases the press); only the click itself is withheld.
    ///
    /// <para>WHAT THIS EXISTS TO STOP (ModBuild 389 diagnosis, re-confirmed here against the
    /// decompiled GH.Runtime). A pooled <c>UIWindow</c> subtree renders for exactly one frame
    /// at its PREFAB state: <c>m_CurrentVisualState</c> is a field initialiser and only
    /// <c>Start()</c> — which Unity runs at the top of the NEXT frame — drives the
    /// <c>CanvasGroup</c> alpha to 0 and <c>blocksRaycasts</c> to false (UIWindow.cs:363-375,
    /// via <c>EvaluateAndTransitionToVisualState</c>). During that frame the plate is still at
    /// its authored, hit-testable values, so the mod's own <c>GraphicRaycaster</c> pass can
    /// return it. The raycast half is not provably safe and is not assumed to be.</para>
    ///
    /// <para>THE CLICK ITSELF CANNOT DELETE ANYTHING - THE LISTENER IT LEAVES BEHIND CAN.
    /// <c>UIAdventureCharacterConfirmationBox.OnConfirmClick</c> (:70-74) does two things and
    /// only the second no-ops on an unshown window:
    /// <c>window.onTransitionComplete.AddListener(OnTransitionComplete)</c> succeeds
    /// unconditionally, then <c>window.Hide()</c> returns having done nothing because
    /// <c>UIWindow.Hide(bool)</c> is guarded on <c>m_CurrentVisualState != Hidden</c>
    /// (:524-537). <c>UnityEvent.AddListener</c> does not dedupe and only
    /// <c>OnTransitionComplete</c> removes itself (:62-69), so the subscription is now
    /// PERMANENT. On the player's next legitimate use the box opens with <c>confirmAction</c>
    /// really assigned, he presses CANCEL, <c>OnBackClick</c>'s <c>Hide()</c> succeeds this
    /// time, the transition completes, and the stale listener invokes <c>confirmAction</c> -
    /// the cancel deletes the character. <c>UIRetireCharacterConfirmationBox</c> (:40) has the
    /// same shape with an anonymous delegate, which cannot be removed at all.</para>
    ///
    /// <para>THE TERM IS <c>!HasGoneToStartingState</c> AND <c>!IsOpen</c>, AND BOTH HALVES ARE
    /// LOAD-BEARING.
    /// <list type="bullet">
    /// <item><b><c>!HasGoneToStartingState</c></b> is the precise reading of "Unity has never
    /// run <c>Start()</c> on this window". The flag is a public getter with a PRIVATE setter,
    /// written in exactly one place — <c>UIWindow.Start()</c> (:370) — and never cleared: not
    /// on hide, not on pooling, not on re-activation. That is what makes it immune to the
    /// ambiguity <c>IsOpen</c> alone has, and it is the discriminator ModBuild 389 wanted but
    /// did not have. <c>IsOpen</c> goes false at the START of a hide transition
    /// (<c>EvaluateAndTransitionToVisualState</c> assigns <c>m_CurrentVisualState</c> before
    /// the alpha tween is even started, :573-580), so <c>!IsOpen</c> is equally true for a
    /// window the player is legitimately watching fade out - and a click during a fade-out is
    /// a click the player meant. A fading window has had <c>Start()</c> run long ago, so this
    /// first term is false for it and the click is delivered.</item>
    /// <item><b><c>!IsOpen</c></b> is what keeps the standing ruling that it must ALWAYS be
    /// possible to open the options menu. The mod's own standalone VR options pane is a clone
    /// that <c>VROptionsTab.CloneWindow</c> deactivates inside the same synchronous block that
    /// instantiates it, so Unity has never dispatched <c>Start()</c> on it and
    /// <c>HasGoneToStartingState</c> is false - and <c>VROptionsTab.ShowStandalone</c> opens it
    /// anyway by calling <c>UIWindow.Show()</c>, the one path that does not consult the flag
    /// (that is the whole ModBuild 350 fix). For the rest of that frame the pane is genuinely
    /// open with the flag still false. Requiring <c>!IsOpen</c> as well means that pane is
    /// never a candidate here: it is open, so its clicks are delivered, and the ruling is kept
    /// by construction rather than by a one-frame race. In the frame this guard is FOR,
    /// <c>IsOpen</c> is false because <c>m_CurrentVisualState</c> still holds its field
    /// initialiser <c>Hidden</c> - which is exactly what ModBuild 389 measured.</item>
    /// </list></para>
    ///
    /// <para>IT FAILS TOWARD DELIVERING. No ancestor <c>UIWindow</c> means DELIVER, which is
    /// what keeps the mod's own chrome working: the corner close X, the grab bars, the world
    /// keycaps and the tray controls are mod objects with no game window over them. A throw
    /// while resolving the ancestor also delivers. The only click withheld is one landing
    /// inside a window that is neither started nor open - a window no player can have aimed
    /// at, because until <c>Start()</c> runs it has never been drawn at a state anyone could
    /// read (its labels still hold the TMP default, which is what proved the diagnosis).</para>
    ///
    /// <para>This withholds an input the MOD synthesises and writes nothing game-side. It does
    /// not <c>Hide</c>, <c>SetActive</c> or <c>RemoveListener</c> anything, and it deliberately
    /// does not repair the stale subscription - that is the game's bug; this only stops the mod
    /// from being the thing that arms it. Local input handling: nothing goes on the wire, so
    /// there is nothing here for a remote board to mirror.</para>
    /// </summary>
    internal static bool ShouldWithholdUnstartedWindowClick(GameObject? clickHandler, string sourceTag)
    {
        if (clickHandler == null)
            return false;

        UIWindow? win;
        try
        {
            // The nearest ancestor window, the clicked object itself included. CONTAINMENT is the
            // right question here - "which window does this click land in" - not identity.
            win = clickHandler.GetComponentInParent<UIWindow>(true);
        }
        catch (System.Exception)
        {
            return false; // fail toward delivering
        }

        if (win == null)
            return false; // mod chrome and anything else outside a game window: always delivered

        // ModBuild 506 hardware: Movie clicks reached this guard but its deliberately
        // disabled identity-only UIWindow never runs Start or Show. Preserve the protection
        // for all native pooled windows; only the live movie's exact surface is mod chrome.
        if (ReferenceEquals(win, WorldUI.NativeVideoWindow.Window)
            && WorldUI.NativeVideoWindow.OwnsClick(clickHandler))
            return false;

        if (win.HasGoneToStartingState || win.IsOpen)
            return false;

        s_unstartedWithheld++;

        string key = clickHandler.name + " in " + win.name;
        if (key == s_lastUnstartedKey)
            return true; // same widget as the last printed line: counted, not re-printed
        s_lastUnstartedKey = key;

        // HW-VERIFY: the falsifier for this guard, and the only evidence it ever fires at all.
        Core.VRLog.Note("Interact",
            $"CLICK WITHHELD: '{clickHandler.name}' ({sourceTag}) inside window '{win.name}' - "
            + "UIWindow.HasGoneToStartingState is false AND UIWindow.IsOpen is false, i.e. Unity has "
            + "not run UIWindow.Start() on this window and nothing has opened it by hand either, so "
            + "what the pointer hit is a pooled subtree still standing at its prefab state. pointerUp "
            + $"was delivered as normal; only the click was withheld. That is {s_unstartedWithheld} "
            + "withheld click(s) this session - this line is change-gated on the widget/window pair, "
            + "so a jump in that count is repeats of one widget rather than a missing line. THIS LINE "
            + "NAMING A CONTROL THE PLAYER MEANT TO PRESS IS THE FALSIFIER: it would mean a window the "
            + "player can read and aim at is reaching this seam un-started and un-opened, that the "
            + "guard is over-firing, and that a real press was eaten. Read the widget name first; if "
            + "it is a button a player would recognise, this term is wrong and must be narrowed, not "
            + "tuned.");
        return true;
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
            Pressing = null;
            _pressedClickHandler = null;
        }
        // Releases the whole recorded ancestor chain (pointerExit up the hierarchy, counts
        // handed back to UguiHoverTracker) — the single teardown every caller funnels
        // through, so no widget can stay stuck highlighted when the beam moves away, the
        // hand switches, the panel closes or the surface is released. FORCED past the exit
        // hysteresis: a teardown is exactly the case where a pending exit must not survive.
        SetHovered(null, force: true);
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

    /// <summary>
    /// Is any mod pointer hovering <paramref name="go"/> RIGHT NOW — the table read as a question
    /// instead of a claim.
    ///
    /// <para>Added for the multiplayer 1:1 rule (ModBuild 300): the owner-side sampler in
    /// <c>WorldUI.Surfaces.DecisionDockSurface</c> asks this per docked option so a peer can be
    /// told which option the OWNER is pointing at. Reading the table rather than a
    /// <c>Selectable</c> internal is the point — the same refcount the enter/exit dispatch is built
    /// on IS the hover, so the wire bit and the highlight the owner sees cannot disagree.</para>
    ///
    /// <para>Note the SUBTREE semantics, which are the useful ones here and are not an accident:
    /// <see cref="UguiPointer.SetHovered"/> claims the whole ancestor chain of the object under the
    /// pointer, so a button whose LABEL was hit answers true — which is exactly what the button
    /// itself does (its own highlight is driven by that same chain dispatch).</para>
    /// </summary>
    internal static bool IsHovered(GameObject? go) =>
        go is not null && Counts.ContainsKey(go.GetInstanceID());
}

/// <summary>
/// CROSS-POINTER PRESS TABLE — the press twin of <see cref="UguiHoverTracker"/>, and built for one
/// reason: the multiplayer 1:1 rule needs to answer "is the OWNER holding this widget down" for a
/// widget on the owner machine (user ruling, verbatim: "auch wie das Bild auf einem mouseover oder
/// klick reagiert soll den anderen Spielern genauso dargestellt werden").
///
/// <para>WHY A TABLE AND NOT A FIELD READ. The press lives in a <see cref="UguiPointer"/> instance,
/// and the mod runs up to four of them (a laser and a fingertip poke per hand). A caller asking
/// "is this widget pressed" would otherwise have to find and interrogate every pointer — and would
/// silently start lying the day a fifth appears. The refcount answers for all of them at once, and
/// its shape is deliberately identical to the hover table beside it so the two are read the same
/// way and go wrong the same way.</para>
///
/// <para>Keyed by instance ID for the same reason the hover table is: two DESTROYED
/// <c>UnityEngine.Object</c>s compare EQUAL under fake-null semantics, which would corrupt a
/// dictionary keyed by the object. Every claim is made and handed back by the single writer
/// <c>UguiPointer.Pressing</c>, so the table drains to empty whenever nothing is held.</para>
///
/// <para>THIS TABLE DISPATCHES NOTHING. Unlike the hover table it is not part of the event path:
/// uGUI press/release events are sent by <see cref="UguiPointer"/> exactly as before and neither
/// the count nor its return value gates them. It is a read seam and cannot change input
/// behaviour.</para>
/// </summary>
internal static class UguiPressTracker
{
    private static readonly Dictionary<int, int> Counts = new(4);

    /// <summary>Claim a press on <paramref name="go"/>.</summary>
    internal static void Acquire(GameObject go)
    {
        if (go is null)
            return;
        int id = go.GetInstanceID();
        Counts[id] = Counts.TryGetValue(id, out int n) ? n + 1 : 1;
    }

    /// <summary>Hand a press back. Unity-null tolerant: a destroyed target must still balance.</summary>
    internal static void Release(GameObject go)
    {
        if (go is null)
            return;
        int id = go.GetInstanceID();
        if (!Counts.TryGetValue(id, out int n))
            return;
        if (n <= 1)
            Counts.Remove(id);
        else
            Counts[id] = n - 1;
    }

    /// <summary>Is any mod pointer holding <paramref name="go"/> down right now?</summary>
    /// <remarks>EXACT, not subtree: unlike the hover chain, a press is claimed on the ONE object
    /// that handled <c>pointerDown</c> — for a button, the button. That is the object whose pressed
    /// tint is drawn, so asking about it is asking about the picture.</remarks>
    internal static bool IsPressed(GameObject? go) =>
        go is not null && Counts.ContainsKey(go.GetInstanceID());
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
