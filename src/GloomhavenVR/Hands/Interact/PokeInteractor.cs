using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Fingertip (index tip) press interaction (FROZEN Phase-2 API). Two target kinds:
///
/// 1. Registered <see cref="IPokeable"/> colliders (<see cref="VRInteractables"/>):
///    nearest collider within hover range gets OnPokeEnter/Exit; pressing into the
///    ~8 mm fingertip contact radius fires OnPoke once (re-arms after retracting).
///
/// 2. Registered uGUI canvases (<see cref="UguiPokeSurfaces"/>): when the fingertip
///    crosses a world-space canvas plane, real pointer events are synthesized via
///    ExecuteEvents (<see cref="UguiPointer"/>) — hover from the front side; plane
///    contact ARMS the press (pointerDown → the button shows its pressed visual, light
///    haptic tick) and the CLICK fires once the fingertip pushed
///    <see cref="WorldUI.WorldUIConfig.PokePressDepthMm"/> (default 12 mm) THROUGH the
///    plane — the flat-button analog of the 3D keycaps' travel-fire (user #12:
///    instant-on-contact fired on accidental brushes). Retracting before reaching the
///    depth cancels without a click; retracting past ReleaseDepth re-arms the next
///    press; depth 0 restores the old instant click on contact. Game modality is
///    respected because hits come from the canvas's own (enabled) GraphicRaycaster only.
///
///    PER-CANVAS PRESS MODE (user #13b): a canvas tagged in
///    <see cref="DeliberatePokeSurfaces"/> (the decision dock's high-stakes buttons)
///    takes the DELIBERATE v1 semantics instead — contact arms exactly the same, but
///    the click fires only on the conscious WITHDRAWAL back past ReleaseDepth in FRONT
///    of the plane; sweeping through past PressThrough or leaving sideways cancels
///    silently (no click, no cooldown charge). "You have to push in AND consciously
///    pull back" — a sweep of the hand across the dock can never trigger a decision.
///    Gated live by [WorldUI] DecisionPokeDeliberate (default true).
///
/// THE GRIP CHORD (user 2026-08, both paths): a press — the OnPoke of path 1, and the ARMING,
/// the instant click and the deliberate withdrawal-click of path 2 — commits only while that
/// hand holds the grip and carries nothing. HOVER is not gated. The gate, its two halves, why
/// hover stays live and what is deliberately untouched (laser, proximity grab) are documented
/// once at <see cref="PressAllowed"/>; every gate site refers to it.
///
/// Plain class ticked by <see cref="VRHand"/> every frame after pose update.
/// No per-frame allocations: for-loops over registries, reused event data.
/// All distances are meters at scale 1 and multiplied by the hand's world scale.
/// </summary>
internal sealed class PokeInteractor
{
    // Distances in meters (scale 1). Canvas distances are per-canvas since P5
    // (UguiPokeSurfaces.Register(canvas, PokeSurfaceTuning) — MISSION A.10).
    //
    // MIRRORED (FingertipRadius, ReleaseRange): the near board click and three other
    // fingertip-contact sites carry their own copies of these two values so their press and
    // re-arm edges land on the SAME depths as the poke — a mismatch means the surfaces feel
    // different under one finger. Deliberately not merged into a shared constant (it would
    // either leak an interactor private onto the frozen P2 surface or hide the number in a
    // Core file nobody opens when tuning — REVIEW-Hands-Board-Core §P3). The copies are held
    // together by scripts/check-mirrors.sh, which fails the guard and NAMES every site when
    // one is tuned alone. Tune them together or document why they may now diverge.
    private const float FingertipRadius = 0.008f;
    private const float HoverRange = 0.035f;
    private const float ReleaseRange = 0.02f;

    /// <summary>
    /// Anti-double-fire (user #3/#7b): minimum time between two uGUI poke clicks of the
    /// same hand. The press is edge-triggered (armed only after the fingertip retracted
    /// past ReleaseDepth), so this only swallows re-entry jitter right at the release
    /// boundary — it is far below anything a deliberate second press can hit, so it
    /// never reads as a dwell.
    /// </summary>
    private const float ClickCooldownSeconds = 0.25f;

    /// <summary>
    /// User #12 fallback when the WorldUI config is not yet bound (module init order):
    /// the [WorldUI] PokePressDepthMm default.
    /// </summary>
    private const float DefaultPressDepthMm = 12f;

    /// <summary>
    /// The push-in fire depth must stay clearly SHORT of the per-canvas PressThrough
    /// canvas-drop guard (default 50 mm, SmallDialog 30 mm) — the click always fires
    /// before the canvas is dropped, so the guard can never cancel a press between the
    /// configured depth and PressThrough.
    /// </summary>
    private const float DepthPressThroughHeadroom = 0.8f;

    /// <summary>
    /// While a push-in press is PENDING on a canvas, its PressThrough drop guard is
    /// stretched by this factor so a fast deliberate stab that overshoots PressThrough
    /// between two frames still fires (the fire depth is evaluated on the same tick)
    /// instead of silently dropping the canvas and cancelling the press.
    /// </summary>
    private const float PendingPressThroughGrace = 1.5f;

    private readonly VRHand _hand;
    private readonly UguiPointer _pointer;

    private bool _enabled = true;
    private IPokeable? _hovered;
    private bool _armed = true;

    private Canvas? _activeCanvas;
    private bool _canvasPressed;
    private bool _pressPending;    // pointerDown sent, click awaiting fire depth / withdrawal
    private bool _pressDeliberate; // user #13b: pending press is on a deliberate (v1) canvas
    private float _lastCanvasClick = -1f;

    /// <summary>Throttle stamp for the grip-chord "press withheld" line (see <see cref="LogGripGate"/>).</summary>
    private float _nextGripGateLogAt;

    /// <summary>One-shot init log for the poke click mode (two hands share one line).</summary>
    private static bool s_modeLogged;

    internal PokeInteractor(VRHand hand)
    {
        _hand = hand;
        _pointer = new UguiPointer(hand.Side);
    }

    /// <summary>Configured push-in depth in meters at scale 1 (0 = instant mode). Read live.</summary>
    private static float ConfiguredPressDepthMeters()
    {
        float mm = WorldUI.WorldUIConfig.PokePressDepthMm != null
            ? WorldUI.WorldUIConfig.PokePressDepthMm.Value
            : DefaultPressDepthMm;
        return Mathf.Clamp(mm, 0f, 30f) * 0.001f;
    }

    /// <summary>
    /// One-shot mode log, emitted lazily so it reports the actually-bound config value
    /// (the Hands module may construct before the WorldUI config binds).
    /// </summary>
    private static void LogModeOnce()
    {
        if (s_modeLogged || WorldUI.WorldUIConfig.PokePressDepthMm == null)
            return;
        s_modeLogged = true;
        float mm = WorldUI.WorldUIConfig.PokePressDepthMm.Value;
        Core.VRLog.Info("Interact", mm > 0f
            ? $"PokeInteractor: PUSH-IN poke clicks — plane contact arms the press (pointerDown, pressed " +
              $"visual, light haptic tick); the CLICK fires at {mm:F0} mm push-through ([WorldUI] " +
              "PokePressDepthMm); retracting before that depth cancels without a click " +
              $"(re-arm on retract + {ClickCooldownSeconds:F2}s cooldown against double-fire)."
            : "PokeInteractor: INSTANT poke clicks ([WorldUI] PokePressDepthMm = 0) — a uGUI poke fires " +
              "the full down+up+click on plane contact " +
              $"(re-arm on retract + {ClickCooldownSeconds:F2}s cooldown against double-fire).");
    }

    /// <summary>Currently hovered pokeable, if any.</summary>
    public IPokeable? Hovered => _hovered;

    /// <summary>Currently hovered uGUI object (registered canvases only), if any.</summary>
    public GameObject? HoveredUi => _pointer.Hovered;

    /// <summary>Enable/disable (mode policy). Disabling cancels in-flight hover/press.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            if (!value)
                CancelAll();
        }
    }

    internal void Tick()
    {
        if (!_enabled || !_hand.HasPose)
        {
            return;
        }

        float scale = _hand.WorldScale;
        Vector3 tip = _hand.Rig.IndexTip.position;

        TickPokeables(tip, scale);
        TickCanvases(tip, scale);
    }

    internal void CancelAll()
    {
        if (_hovered != null)
        {
            SafeExit(_hovered);
            _hovered = null;
        }
        _armed = true;
        _pointer.Cancel();
        _activeCanvas = null;
        _canvasPressed = false;
        _pressPending = false;
        _pressDeliberate = false;
    }

    /// <summary>
    /// USER #13b: does THIS canvas take the deliberate v1 press (contact arms, click on
    /// withdrawal past ReleaseDepth, sweep-through cancels)? Registered per docked
    /// canvas by the decision dock; [WorldUI] DecisionPokeDeliberate (default true) is
    /// the live escape hatch — off restores the shared push-in behaviour for decision
    /// buttons too. Null-tolerant on config for module init order.
    /// </summary>
    private static bool IsDeliberate(Canvas canvas)
    {
        if (!DeliberatePokeSurfaces.Contains(canvas))
            return false;
        var entry = WorldUI.WorldUIConfig.DecisionPokeDeliberate;
        return entry == null || entry.Value;
    }

    // ---- the grip chord -------------------------------------------------------------------

    /// <summary>
    /// THE GRIP CHORD: a PHYSICAL fingertip press commits only while that hand HOLDS THE GRIP
    /// and carries nothing.
    ///
    /// <para>USER REPORT (2026-08, verbatim): "Auch die Entscheidungsbuttons (so wie alle buttons)
    /// sollen nur auf pyhsisches Drücken reagieren wenn die Greiftaste gedrückt ist (und damit der
    /// Finger gespreitzt). Aktuell reagieren die Entscheidungsbuttons auch bereits so auf physische
    /// Berührung." The chord already guarded the 3D keycaps (<c>Cards.PlayTray.BoardButton</c>,
    /// <c>WorldUI.ButtonCluster.PhysicalButton</c> — both check it at their depth-fire) and the
    /// fingertip-on-hex ping (<c>Board.BoardPick.TryNearPick</c> +
    /// <c>Board.BoardClickDriver.TickNear</c>). The two paths driven from HERE did not: registered
    /// <see cref="IPokeable"/> colliders, and registered uGUI canvases — which is exactly what the
    /// decision dock's buttons ride. So the dock fired on plain contact while every keycap next to
    /// it required the chord, which is the inconsistency the report names.</para>
    ///
    /// <para>THE CONDITION IS DELIBERATELY THE ONE <c>BoardPick.TryNearPick</c> USES, character for
    /// character, so there is ONE rule for every physical fingertip commit in the mod instead of a
    /// per-surface dialect. Its two halves, from that doc comment:</para>
    /// <list type="bullet">
    ///   <item><description>GRIP HELD — "Faust mit ausgestrecktem Zeigefinger". Grip held with the
    ///   trigger released is exactly <see cref="HandPose.Point"/>, which the FingerCurler renders as
    ///   a fist with the index finger extended: the player's hand SHOWS the mode it is in, and a
    ///   hand drifting past a panel with the grip open is inert.</description></item>
    ///   <item><description>HAND EMPTY — the grip is also what GRABS (<c>IGrabbable.GrabWithGrip</c>),
    ///   so a held object means the grip was pressed to CARRY something, not to press a button.
    ///   Without this half, walking a card across the board would poke everything it passed over.
    ///   Note the frame order (<c>VRHand.UpdateBody.interactors</c>: Poke, …, Grabber): on the
    ///   GripDown frame the grabber has not run yet, so a press can still arm on the frame a grab
    ///   starts — which is why the gate is RE-CHECKED every frame a press is pending below, not
    ///   only at the arming edge.</description></item>
    /// </list>
    ///
    /// <para>HOVER IS DELIBERATELY NOT GATED — the choice this fix had to make explicitly. The
    /// keycaps set the precedent: "the cap still follows the finger; it just cannot FIRE". A button
    /// that lights up under an open hand and refuses to commit TEACHES the chord (the player sees
    /// the button is live and reaches for the grip); a button that is completely dead reads as a
    /// broken button and produces exactly the "reagiert nicht" reports this mod keeps its logs for.
    /// So OnPokeEnter/Exit, the uGUI hover highlight, the hover haptic and the card pop are
    /// untouched — only the COMMIT is gated.</para>
    ///
    /// <para>NOT AFFECTED: the LASER. Laser clicks never come through this interactor
    /// (<c>RayUguiDriver</c> drives its own pointer; <c>CardsDriver</c> calls <c>OnPoke</c> on board
    /// targets directly with the fingertip nowhere near them), so pointing and clicking from a
    /// distance stays grip-free. Neither is the PROXIMITY GRAB touched: that is
    /// <see cref="ProximityGrabber"/>, a different interactor, and it keeps taking cards on
    /// GripDown/TriggerDown exactly as before.</para>
    /// </summary>
    private bool PressAllowed => _hand.GripPressed && _hand.Grabber.Held == null;

    /// <summary>
    /// Throttled (1 s per hand) "the press was withheld" line. A "der Button reagiert nicht" report
    /// has to be answerable from the log alone — the same reason and the same throttle the two
    /// keycap sites carry. The message is only BUILT once the throttle lets it through, so a
    /// fingertip resting on a panel with the grip open costs one comparison per frame.
    /// </summary>
    private void LogGripGate(string what, object? target)
    {
        if (Time.unscaledTime < _nextGripGateLogAt)
            return;
        _nextGripGateLogAt = Time.unscaledTime + 1f;
        // Name the thing that refused: a Unity Object by its object name (null-checked through
        // Unity's operator — a destroyed target would throw on .name), anything else (the cluster's
        // PhysicalButton is a plain class, not a Component) by its type.
        string label = target switch
        {
            UnityEngine.Object o => o != null ? o.name : "<destroyed>",
            null => "?",
            _ => target.GetType().Name,
        };
        Core.VRLog.Info("Interact",
            $"Poke WITHHELD ({_hand.Side}) on {what} '{label}' — a " +
            "PHYSICAL fingertip press commits only while the SAME hand holds the GRIP and carries " +
            $"nothing (grip {(_hand.GripPressed ? "held" : "open")}, hand " +
            $"{(_hand.Grabber.Held != null ? "holding something" : "empty")}). Hover, the pressed " +
            "visual and every laser click are unaffected.");
    }

    // ---- collider pokeables -------------------------------------------------------------

    private void TickPokeables(Vector3 tip, float scale)
    {
        var entries = VRInteractables.Pokeables;
        IPokeable? nearest = null;
        float nearestDist = float.MaxValue;
        bool sawDeadCollider = false;

        for (int i = 0; i < entries.Count; i++)
        {
            Collider collider = entries[i].Collider;
            if (collider == null)
            {
                sawDeadCollider = true;
                continue;
            }
            if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;

            float dist = Vector3.Distance(tip, collider.ClosestPoint(tip));
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = entries[i].Target;
            }
        }

        if (sawDeadCollider)
            VRInteractables.Prune();

        // Hover transition.
        IPokeable? newHover = nearest != null && nearestDist <= HoverRange * scale ? nearest : null;
        if (!ReferenceEquals(newHover, _hovered))
        {
            if (_hovered != null)
                SafeExit(_hovered);
            _hovered = newHover;
            _armed = true;
            if (_hovered != null)
            {
                _hovered.OnPokeEnter(_hand);
                _hand.SendHaptic(HapticPreset.HoverTick);
            }
        }

        // Contact (press) with re-arm hysteresis.
        if (_hovered != null)
        {
            float contact = FingertipRadius * scale;
            if (_armed && nearestDist <= contact)
            {
                // GRIP CHORD (user 2026-08, see PressAllowed): contact alone is not a press.
                // The interactor stays ARMED while the chord is missing — the gate is checked at
                // COMMIT time, exactly like the two keycap depth-fires, so closing the grip with
                // the fingertip already on the target presses instead of demanding a full retract
                // first (nothing here is a half-committed state that could leak: OnPoke is a
                // single instantaneous call, so there is no in-flight press to cancel on this
                // path — the uGUI path below is the one that needs the mid-press unwind).
                if (!PressAllowed)
                {
                    LogGripGate("pokeable", _hovered);
                }
                else
                {
                    _armed = false;
                    // ONE PRESS, ONE PULSE — AND THE RECEIVER OWNS IT (R22, 2026-09-05).
                    //
                    // This line used to send HapticPreset.ClickPulse unconditionally, right here,
                    // the instant the fingertip came within FingertipRadius. It was wrong in both
                    // directions at once:
                    //
                    //   DOUBLE. Every receiver that actually commits something already pulses at
                    //   its OWN commit point, after its own gates — VRCard.OnPoke, PileStack.OnPoke,
                    //   HalfZone.OnPoke, BoardButton.Press, and now MapButtonRail.Press and
                    //   MapLocationInteractor.Dispatch. A fingertip poke on the discard pile
                    //   therefore fired TWO overlapping impulses while a laser click on the same
                    //   stack (PileStack.LaserToggle) fired one.
                    //
                    //   PHANTOM. The receivers that deliberately do NOT commit on contact got a
                    //   full click anyway. BoardButton.OnPoke does nothing for an ENABLED cap on
                    //   purpose — its press is the depth-fire at ~90% of travel — so the buzz fired
                    //   on exactly the 8 mm brush that user requirement #6 exists to make inert, and
                    //   then fired AGAIN when the key actually bottomed out. BoardSurfaceTarget's
                    //   OnPoke is empty by construction. A receiver that refuses (VRCard with poke
                    //   select off) buzzed for a press that never happened.
                    //
                    // The decision is made HERE, at the sender, and it is to send nothing: only the
                    // receiver knows whether the gesture became a press, and it is the receiver that
                    // has the gates. This interactor's other three ClickPulse sends are untouched
                    // and are NOT the same case — they are the uGUI canvas path, where the interactor
                    // IS the committer (it drives the pointer down/up itself) and there is no mod
                    // receiver to own anything.
                    _hovered.OnPoke(_hand);
                }
            }
            else if (!_armed && nearestDist > ReleaseRange * scale)
            {
                _armed = true;
            }
        }
    }

    private void SafeExit(IPokeable target)
    {
        try
        {
            target.OnPokeExit(_hand);
        }
        catch (System.Exception ex)
        {
            Core.VRLog.Error("Interact", $"OnPokeExit threw: {ex}");
        }
    }

    // ---- uGUI canvases --------------------------------------------------------------------

    private void TickCanvases(Vector3 tip, float scale)
    {
        LogModeOnce();
        var surfaces = UguiPokeSurfaces.Surfaces;

        // Find the canvas whose plane the fingertip is closest to (front side only).
        Canvas? best = null;
        float bestAbs = float.MaxValue;
        float bestSigned = 0f;
        bool sawDead = false;

        for (int i = 0; i < surfaces.Count; i++)
        {
            Canvas canvas = surfaces[i];
            if (canvas == null)
            {
                sawDead = true;
                continue;
            }
            if (!canvas.isActiveAndEnabled)
                continue;
            // The same term the far-ray loop carries (RayUguiDriver.Tick, 2026-09-03): a pane
            // whose elements are missing — dissolving, or not yet materialised — is not a surface
            // the fingertip can hover or press. See WindowMaterialise.IsPointerBlind.
            if (WorldUI.WindowMaterialise.IsPointerBlind(canvas))
                continue;

            Transform t = canvas.transform;
            // uGUI renders facing -forward: the viewer/front side is where
            // dot(tip - canvas, forward) < 0. Fingers physically sink through the
            // plane on a press, so tolerate some press-through before dropping it.
            float signed = Vector3.Dot(tip - t.position, t.forward);
            float maxThrough = UguiPokeSurfaces.TuningFor(canvas).PressThrough * scale;
            // Depth-fire mode only: don't drop a pending press mid-stab. A DELIBERATE
            // (v1) pending press keeps the plain guard — sweeping through past
            // PressThrough must DROP the canvas and cancel silently, never click.
            if (_pressPending && !_pressDeliberate && ReferenceEquals(canvas, _activeCanvas))
                maxThrough *= PendingPressThroughGrace;
            if (signed > maxThrough)
                continue; // far behind the canvas — ignore

            // Inside the canvas rect?
            //
            // THE HOST RECT, NOT THE HIT RECT, AND THE DIFFERENCE IS DELIBERATE (ModBuild 440).
            // The far ray asks a different question here: RayUguiDriver.HitRectOf goes through
            // CanvasConversion.TryGetHitRect, which follows content drawn OUTSIDE the frame and,
            // since ModBuild 242, may also be narrowed INSIDE it. This test is the raw frame.
            //
            // WHY THAT IS NOT THIS ROUND'S DEFECT, stated as arithmetic rather than as a hope. The
            // ModBuild 439 report ("wenn der Kampflog wieder kleiner geworden ist geht der Laser
            // durch das X hindurch") is the NARROWING reaching over the mod's close X, and the
            // plate cannot be outside THIS rectangle: ModalCloseButton.PlaceAgainstInk clamps the
            // plate's pivot corner to (hostRect.max - InsetPx) and the plate pivots at (1,1), so it
            // extends inward from that corner and is inside the host rect by construction, on both
            // axes, on every window. The fingertip could always reach the X; the beam could not.
            //
            // WHAT THE DIVERGENCE DOES STILL COST, so the next round does not have to re-derive it:
            // a fingertip cannot reach content the window draws OUTSIDE its own frame, which the
            // laser can (his ModBuild 439 log has 'GloomhavenVR.Panel_InitiativeTrack' drawing
            // 931x263 px past a 621x175 frame). Nobody has reported it — every such widget has been
            // within the frame or out of arm's reach — and closing it means growing what a
            // fingertip claims, which is a change that needs its own report to justify. Whoever
            // takes it should take BOTH tests through one accessor rather than adding a third rect.
            var rect = (RectTransform)t;
            Vector3 local = t.InverseTransformPoint(tip);
            if (!rect.rect.Contains(new Vector2(local.x, local.y)))
                continue;

            float abs = Mathf.Abs(signed);
            if (abs < bestAbs)
            {
                bestAbs = abs;
                bestSigned = signed;
                best = canvas;
            }
        }

        if (sawDead)
            UguiPokeSurfaces.Prune();

        // Canvas switch / loss cancels in-flight pointer state.
        if (!ReferenceEquals(best, _activeCanvas))
        {
            // Canvas lost mid-press = the v1 cancel paths for a deliberate press:
            // swept through past PressThrough or left the rect sideways → pointerUp
            // without a click, silent, no cooldown charge.
            _pointer.Cancel();
            _canvasPressed = false;
            _pressPending = false;
            _pressDeliberate = false;
            _activeCanvas = best;
        }

        if (_activeCanvas == null)
            return;

        PokeSurfaceTuning tuning = UguiPokeSurfaces.TuningFor(_activeCanvas);

        if (bestAbs > tuning.HoverRange * scale && !_canvasPressed)
        {
            _pointer.SetHovered(null);
            return;
        }

        // Project the fingertip onto the canvas plane → screen point of the canvas camera.
        Transform ct = _activeCanvas.transform;
        Vector3 onPlane = tip - ct.forward * bestSigned;
        Camera? cam = _activeCanvas.worldCamera != null ? _activeCanvas.worldCamera : Camera.main;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, onPlane);

        bool hit = _pointer.TryRaycast(_activeCanvas, screenPos, out RaycastResult top);
        GameObject? previousHover = _pointer.Hovered;
        _pointer.SetHovered(hit ? top.gameObject : null);
        if (hit && previousHover == null && _pointer.Hovered != null)
            _hand.SendHaptic(HapticPreset.HoverTick);

        // PUSH-IN press (user #12; supersedes the user #3/#7b instant click, which
        // remains as the PokePressDepthMm = 0 mode). Plane contact only ARMS the press:
        // pointerDown is sent so the button shows its native pressed visual (plus a
        // light haptic tick), and the CLICK (pointerUp over the same handler) fires
        // once the fingertip pushed PokePressDepthMm THROUGH the plane — the flat-button
        // analog of the 3D keycaps' travel-fire, so brushing a docked panel can no
        // longer trigger a decision by accident. Retracting BEFORE the fire depth
        // cancels via _pointer.Cancel() (pointerUp without click — the uGUI
        // release-outside idiom), costs nothing (no cooldown charge) and re-arms once
        // the fingertip is back out past ReleaseDepth. The fire depth is clamped safely
        // below the per-canvas PressThrough canvas-drop guard, so a press can never be
        // eaten between fire depth and PressThrough — the click always lands first.
        // Anti-double-fire stays: _canvasPressed is edge-latched until the fingertip
        // retracts past ReleaseDepth (hysteresis re-arm) and the 0.25 s cooldown
        // swallows jitter across that boundary. (Poke never drove uGUI drags — Drag()
        // is laser-only; the laser path in RayUguiDriver is unchanged.)
        float fireDepth = ConfiguredPressDepthMeters();
        if (fireDepth > 0f)
            fireDepth = Mathf.Min(fireDepth, tuning.PressThrough * DepthPressThroughHeadroom);

        if (!_canvasPressed)
        {
            if (hit && bestSigned >= -FingertipRadius * scale)
            {
                // GRIP CHORD (user 2026-08, see PressAllowed) — the gate sits BEFORE the arming,
                // not only before the click, and that placement is the whole fix for the decision
                // dock. Arming is not a neutral visual: it sends pointerDown, and on a DELIBERATE
                // (v1) canvas it is the FIRST HALF of a click that the withdrawal completes. An
                // ungated arm would therefore let a grip-less touch commit a decision the moment
                // the hand is pulled back — precisely the "reagiert bereits auf physische
                // Berührung" the report names. Nothing is latched when the chord is missing
                // (_canvasPressed stays false), so closing the grip with the fingertip already on
                // the plane arms normally — same commit-time semantics as the keycaps, and the
                // hover/highlight set above stays live the whole time.
                if (!PressAllowed)
                {
                    LogGripGate("uGUI canvas", _activeCanvas);
                    return;
                }

                _canvasPressed = true;
                if (Time.unscaledTime - _lastCanvasClick >= ClickCooldownSeconds)
                {
                    // USER #13b: a deliberate (v1) canvas always ARMS on contact — even
                    // with PokePressDepthMm = 0 — because its click is defined by the
                    // conscious withdrawal, not by contact or push depth.
                    bool deliberate = IsDeliberate(_activeCanvas);
                    if (deliberate || fireDepth > 0f)
                    {
                        // Arm only — pressed visual now; click at depth (v3) or on
                        // withdrawal (deliberate v1).
                        _pressPending = true;
                        _pressDeliberate = deliberate;
                        _pointer.Press(screenPos);
                        _hand.SendHaptic(HapticPreset.HoverTick); // light arming tick
                    }
                    else
                    {
                        // Instant mode (PokePressDepthMm = 0): full click on contact.
                        _lastCanvasClick = Time.unscaledTime;
                        _pointer.Press(screenPos);
                        _pointer.Release(screenPos);
                        _hand.SendHaptic(HapticPreset.ClickPulse);
                    }
                }
            }
        }
        else if (_pressPending)
        {
            // GRIP RELEASED MID-PRESS (user 2026-08, the half of the chord that is easy to get
            // wrong): the gate is re-checked on EVERY pending frame, and losing it cancels the
            // press on the spot — pointerUp WITHOUT a click (the uGUI release-outside idiom that
            // the retract-before-depth path already uses), so the button's pressed visual returns
            // to normal, nothing is left pressed in uGUI, and no cooldown is charged.
            //
            // This is what makes the DELIBERATE (v1) dock safe. Its click is defined by the
            // WITHDRAWAL, so a press armed under a held grip and pulled back after the grip opened
            // would otherwise still fire on the way out — the exact phantom click the fix must not
            // ship. It cannot: _pressPending is already false by the time that withdrawal is seen,
            // and the withdrawal then only falls through the plain re-arm branch below.
            //
            // It also covers the frame order (Poke ticks BEFORE Grabber, see PressAllowed): a
            // press that armed on the frame a proximity grab started is cancelled one frame later,
            // when the hand reports what it is now carrying.
            //
            // _canvasPressed is cleared with it — the same re-arm the depth-cancel path performs,
            // so the next contact with the chord held presses again with no leftover state.
            if (!PressAllowed)
            {
                _pressPending = false;
                _pressDeliberate = false;
                _canvasPressed = false;
                _pointer.Cancel();
                // Not throttled: this is an edge, at most one per press, and it is the line that
                // answers "the button flashed pressed and then did nothing".
                Core.VRLog.Info("Interact",
                    $"Poke press CANCELLED ({_hand.Side}) on '{_activeCanvas.name}' — the grip was " +
                    "released (or the hand grabbed something) while the press was pending, so the " +
                    "pointer was released WITHOUT a click, the pressed visual is cleared and no " +
                    "click can fire on the withdrawal. Push in again with the grip held.");
            }
            else if (_pressDeliberate)
            {
                // DELIBERATE v1 (user #13b, decision buttons): the click fires on the
                // conscious WITHDRAWAL — the fingertip retracting back past ReleaseDepth
                // in FRONT of the plane. Push depth never fires; sweeping through past
                // PressThrough (or leaving the rect sideways) drops the canvas in the
                // scan above → Cancel, silent. Firing at the withdrawal edge means the
                // fingertip is already past the re-arm hysteresis, so the press fully
                // re-arms here (next contact presses again, cooldown still applies).
                if (bestSigned < -tuning.ReleaseDepth * scale)
                {
                    _pressPending = false;
                    _pressDeliberate = false;
                    _canvasPressed = false;
                    _lastCanvasClick = Time.unscaledTime;
                    _pointer.Release(screenPos); // up + click (released over the pressed handler)
                    _hand.SendHaptic(HapticPreset.ClickPulse); // fire pulse on the pull-out
                }
            }
            else if (bestSigned >= fireDepth * scale)
            {
                _pressPending = false;
                _lastCanvasClick = Time.unscaledTime;
                _pointer.Release(screenPos); // up + click (released over the pressed handler)
                _hand.SendHaptic(HapticPreset.ClickPulse); // stronger fire pulse
            }
            else if (bestSigned < -tuning.ReleaseDepth * scale)
            {
                // Retracted before reaching the fire depth: cancel — pointerUp without
                // a click, no cooldown charge, immediately re-armed for the next press.
                _pressPending = false;
                _canvasPressed = false;
                _pointer.Cancel();
            }
        }
        else if (bestSigned < -tuning.ReleaseDepth * scale)
        {
            _canvasPressed = false; // re-armed: the next plane contact presses again
        }
    }
}

/// <summary>
/// USER #13b: per-canvas poke PRESS-MODE registry. Canvases tagged here take the
/// DELIBERATE v1 press semantics in <see cref="PokeInteractor"/> — contact only arms
/// (pointerDown + light haptic), the CLICK fires on the conscious withdrawal back past
/// ReleaseDepth, and sweep-through/leave-sideways cancels silently — instead of the
/// shared v3 push-in depth-fire. Registered per docked canvas by
/// <c>WorldUI.Surfaces.DecisionDockSurface</c> (the take-damage burn choice, the
/// burn-confirm dialog, the short-rest Ja/Nein: accidental instant triggers on these
/// are costly and irreversible); every unregistered canvas keeps the v3 behaviour.
/// The [WorldUI] DecisionPokeDeliberate config is checked LIVE at press time by the
/// interactor, so this registry only says WHICH canvases are decision surfaces.
/// Mirrors the <see cref="UguiPokeSurfaces"/> conventions: reference-compared list,
/// allocation-free lookup, Unity-null-tolerant unregister.
/// </summary>
internal static class DeliberatePokeSurfaces
{
    private static readonly List<Canvas> Canvases = new(2);

    /// <summary>Tag a poke canvas deliberate-press (one log line per registration).</summary>
    public static void Register(Canvas canvas, string owner)
    {
        if (canvas == null)
        {
            Core.VRLog.Warn("Interact", "DeliberatePokeSurfaces.Register called with null canvas — ignored.");
            return;
        }
        if (Contains(canvas))
            return;
        Canvases.Add(canvas);
        Core.VRLog.Info("Interact", $"Poke press-mode: canvas '{canvas.name}' registered DELIBERATE for " +
                                    $"{owner} — contact arms (pressed visual + light haptic), the CLICK " +
                                    "fires on the conscious withdrawal past ReleaseDepth, sweep-through/" +
                                    "leave-sideways cancels silently ([WorldUI] DecisionPokeDeliberate).");
    }

    /// <summary>Reference-based removal (a released host canvas may already be Unity-destroyed).</summary>
    public static void Unregister(Canvas canvas)
    {
        if (canvas is not null)
            Canvases.Remove(canvas);
    }

    /// <summary>True when this canvas was tagged deliberate (reference compare, allocation-free).</summary>
    internal static bool Contains(Canvas? canvas)
    {
        if (canvas is null)
            return false;
        for (int i = 0; i < Canvases.Count; i++)
        {
            if (ReferenceEquals(Canvases[i], canvas))
                return true;
        }
        return false;
    }
}
