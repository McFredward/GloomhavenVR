using System;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Proximity grab (FROZEN Phase-2 API) — Demeo pattern: no physics joints, no
/// collider-on-hand. The nearest registered <see cref="IGrabbable"/> with
/// <c>CanGrab</c> within palm reach becomes the highlighted candidate
/// (<see cref="IGrabHighlight"/> hook + <see cref="HighlightChanged"/> event for
/// emissive pulses); pressing the grab button grabs it, releasing that button
/// releases it with the measured palm velocity — deterministic and MP-safe.
/// WHICH button: CARDS and board figures are TRIGGER-ONLY (see
/// <see cref="IsTriggerOnly"/> — user 2026-08-11: "Die Karten sollen nur mit dem
/// trigger nehmbar sein"), panels/tray bars are grip-only
/// (<see cref="IGrabbable.GrabWithGrip"/>), and a future grabbable in neither class
/// grabs with the trigger too. The two buttons operate their two object classes
/// INDEPENDENTLY: a grip press with a trigger-only highlight falls through to the
/// nearest grip-grabbable in reach (<see cref="TryGripFallThrough"/> — a highlighted
/// card must not eat the grip meant for the tray bar, hardware test 2026-08-11).
/// The Trigger path defers to any ray/UI click
/// via <see cref="RayInteractor.HasFreshUiHit"/> (see Tick), and shares the
/// trigger-up release edge with the laser pluck (<see cref="ForceGrab"/>). While a
/// trigger-taken card is held, the grip is ignored for it entirely — the hold loop
/// reads only the button that grabbed.
/// </summary>
internal sealed class ProximityGrabber
{
    /// <summary>Palm reach in meters (scale 1). MIRRORED — a second copy of this value exists
    /// in Board (a private const of the same name there). Deliberately NOT merged: a shared
    /// constant would need this interactor detail promoted onto the frozen P2 surface, or a
    /// Core constants file neither owner reads when tuning (REVIEW-Hands-Board-Core §P3). The
    /// price of that decision is that the copies must be tuned TOGETHER, which is what
    /// scripts/check-mirrors.sh enforces — it names the other site when they disagree. Hands
    /// deliberately does not name Board here; the lint carries the coupling, not the layering.
    ///
    /// <para>INTERNAL since 2026-08-23 (ModBuild 231) because it is now also the LASER REEL'S NEAR
    /// BOUND: <see cref="WorldUI.PanelGrabHandle"/>'s stick-driven carry distance stops exactly
    /// where a grip press would already take the window, so "you can pull it close enough to then
    /// grab it with your hand" is true by construction instead of by a tuned constant. The
    /// alternative — a fourth copy of 0.13 in WorldUI — is a value that drifts the first time this
    /// one is tuned, and the mirror lint below would then be policing three of four sites.
    ///
    /// NOTHING WAS COPIED AND THE LINT GROUP IS UNCHANGED (still ProximityGrabber /
    /// FigureGrabDriver / FanSweep): the reel READS this constant. The same widening, for the same
    /// reason, was done to <c>RayGrabDriver.MaxDistanceMeters</c> one build earlier, which is the
    /// reel's far bound. Behaviour here is untouched — visibility only.</para>
    /// </summary>
    internal const float ReachMeters = 0.13f;

    /// <summary>
    /// Candidate stickiness (P6, hardware test #8; widened P7, test #10): a rival must
    /// be closer than the current highlight by this margin (meters, scale 1) to steal
    /// it — overlapping fan cards used to flap the highlight every frame, buzzing the
    /// controller. 2.5 cm means a neighboring card can never oscillate with the
    /// current one: the pop animation moves a card by ~3.5 cm, less than the margin
    /// plus the card strip spacing, so animation alone cannot flip the winner.
    /// </summary>
    private const float SwitchMarginMeters = 0.025f;

    /// <summary>
    /// HOVER SCHMITT TRIGGER, the release ring: a target that ALREADY holds the highlight keeps it
    /// out to <c>ReachMeters * ExitReachFactor</c>, while a new one still has to come inside
    /// <see cref="ReachMeters"/> to take it. A factor rather than a second dial for the same reason
    /// <c>FigureGrabDriver.PickExitFactor</c> is one: a player tunes "how close must I get", never
    /// "how much slack does letting go get", and two independent radii that can be set to cross
    /// each other would need a third rule to sort them out.
    ///
    /// <para>1.35 x 130 mm = 176 mm, a 46 mm dead band. That is far wider than the hand tremor the
    /// ModBuild 335 hardware log shows around the boundary and far narrower than the gap between
    /// two board figures on neighbouring hexes, so it cannot make a NEIGHBOUR sticky. Matches the
    /// on/off-fraction idiom of <c>PeerBoardFade</c> / <c>WallSegmentFade</c>.</para>
    /// </summary>
    private const float ExitReachFactor = 1.35f;

    /// <summary>
    /// HOVER SCHMITT TRIGGER, the exit dwell: how long the glow is held after the election stops
    /// naming the current target, while that target is still inside the exit ring.
    ///
    /// <para>WHY A DWELL AND NOT JUST A RADIUS (ModBuild 335 hardware log). The highlight did fire
    /// on every figure the user hovered — SpittingDrake, ElderDrake, RendingDrakeElite, Mindthief —
    /// and was torn down again within one to three log lines, in a log printing ~20 lines a frame.
    /// A glow that lives a frame or two is invisible, which is the whole of "kein highlighting wenn
    /// ich mit der Hand ueber die Figur fahre". The distance was NOT the term that dropped it: the
    /// palm readings at engagement were 25-77 mm against this class's 130 mm reach. What dropped it
    /// is the per-hand veto (<see cref="IGrabbableHandFilter"/>) going false for a frame or more,
    /// because <c>FigureGrabDriver</c>'s election found no winner that frame — and its own
    /// re-entry costs a further six frames of dwell before the figure is elected again. A radius
    /// alone cannot see any of that; only time can.</para>
    ///
    /// <para>0.20 s at 90 Hz is 18 frames — it absorbs a full drop plus the driver's six-frame
    /// re-entry dwell with margin to spare, and is well under the ~250 ms at which a standing
    /// affordance starts to read as stuck. It is the same 0.20 s <c>PeerBoardFade</c> uses for its
    /// enter dwell. Nothing about GRABBING is made stickier: see <see cref="Tick"/>'s hover-tail
    /// branch, which refuses a grab during the tail with exactly the gates that governed before
    /// this hysteresis existed.</para>
    /// </summary>
    private const float ExitDwellSeconds = 0.20f;

    private readonly VRHand _hand;
    private bool _enabled = true;
    private bool _releaseOnTriggerUp;

    // Which button + source grabbed the current hold, for the release log (G3).
    private string _grabLabel = "";
    private string _lastGrabLog = "";

    // Refusal diagnostic throttle (user bug A: "won't grab" gave a silent log — every
    // refused grab attempt now NAMES its gate, at most one line per second per hand).
    private float _nextRefusalLogAt;

    // HOVER HYSTERESIS state (see ExitReachFactor / ExitDwellSeconds).
    //   _highlightDropAt   unscaled time the current highlight FIRST stopped passing the election
    //                      this frame; negative while it is passing.
    //   _highlightGrabbable  whether the current highlight satisfies the pre-hysteresis grab
    //                      condition RIGHT NOW (elected nearest, in reach, CanGrab, hand allowed).
    //                      False during the tail, and the only thing the grab edges consult.
    //   _highlightBlocker  which gate ended it, in words — the field that turns "CLEARED" from a
    //                      bare event into an answer (see LogHighlightDrop).
    private float _highlightDropAt = -1f;
    private bool _highlightGrabbable;
    private string _highlightBlocker = "";
    private float _nextDropLogAt;
    private int _dropsSinceLastLog;

    internal ProximityGrabber(VRHand hand) => _hand = hand;

    /// <summary>The current grab candidate (highlighted), if any.</summary>
    public IGrabbable? Highlighted { get; private set; }

    /// <summary>The object currently held by this hand, if any.</summary>
    public IGrabbable? Held { get; private set; }

    /// <summary>Fired when the highlight candidate changes (null = none).</summary>
    public event Action<VRHand, IGrabbable?>? HighlightChanged;

    /// <summary>Enable/disable (mode policy). Disabling releases any held object.</summary>
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
        if (!_enabled)
        {
            // User bug A ("bars vibrate but won't grab", hardware log 4125-4193): the
            // interactor being POLICY-OFF was completely silent while hover systems that
            // do not consult it (RayGrabDriver tint/haptic) kept promising a grab. Name
            // the refusal on every grab-intent edge so the next hardware log points at
            // the mode mask instead of a ghost.
            if (_hand.HasPose && (_hand.GripDown || _hand.TriggerDown))
                LogRefusal($"interactor disabled by mode policy (mode={Core.Events.VRModeStateMachine.CurrentMode})");
            return;
        }
        if (!_hand.HasPose)
            return;

        if (Held != null && HealDeadHeld())
            return; // healed a stuck hold this frame; resume normal grabbing next Tick

        if (Held != null)
        {
            // Hold button released (grip — or trigger for trigger-grabbed / laser-
            // plucked objects, see ForceGrab) → release with palm velocity.
            // Trigger-grab and laser-pluck SHARE the trigger-up release edge, so a card
            // grabbed either way can never get stuck held.
            bool stillHeld = _releaseOnTriggerUp ? _hand.TriggerPressed : _hand.GripPressed;
            if (!stillHeld)
            {
                IGrabbable released = Held;
                Held = null;
                _releaseOnTriggerUp = false;
                released.OnRelease(_hand, _hand.PalmVelocity);
                LogGrab($"{_hand.Side} release — {_grabLabel}.");
            }
            return;
        }

        UpdateHighlight();

        if (Highlighted == null)
        {
            // Grab-intent edge with NO candidate: if a registered grabbable is within
            // reach but was SKIPPED, name why (CanGrab gate / per-hand filter) —
            // otherwise the refusal reads as "no grabbable in reach" in the log.
            if (_hand.GripDown || _hand.TriggerDown)
                LogNoCandidateRefusal();
            return;
        }

        // ---- HOVER TAIL: A GLOW THAT IS NOT AN OFFER -------------------------------------------
        //
        // The highlight is being held past the election by the Schmitt trigger in
        // UpdateHighlight (see ExitReachFactor / ExitDwellSeconds), so a boundary flicker or a
        // one-frame veto cannot make the hover invisible. THE GRAB IS NOT MADE STICKIER BY IT:
        // grabbing still requires exactly what it required before the hysteresis existed — being
        // the elected nearest candidate, in reach, CanGrab, hand allowed — which is precisely what
        // _highlightGrabbable carries. A GRIP press still falls through to a grip-grabbable
        // (that path never takes Highlighted, so the tray bar is unaffected); a grab aimed AT the
        // tail is refused BY NAME and drops the glow on the spot, so no promise is ever left
        // standing where a grab was just refused.
        if (!_highlightGrabbable)
        {
            if (_hand.GripDown)
            {
                IGrabbable? gripTarget = FindNearestGripGrabbable();
                if (gripTarget != null)
                {
                    BeginGrab(gripTarget, releaseOnTriggerUp: false, "grip", "proximity fall-through",
                        clearHighlight: false);
                    return;
                }
            }
            if (_hand.TriggerDown || _hand.GripDown)
            {
                LogRefusal($"'{DescribeGrabbable(Highlighted)}' is no longer the elected candidate "
                           + $"({(_highlightBlocker.Length > 0 ? _highlightBlocker : "election lost")}) "
                           + $"— its glow is inside the {ExitDwellSeconds:0.00} s hover tail, which "
                           + "shows where the hand is and offers nothing; the glow is dropped now");
                SetHighlighted(null);
            }
            return;
        }

        // Test #27: grip-only grabbables (world panels/boards) always take the GRIP
        // button — the tester found grip more intuitive for moving boards, while cards
        // keep the Demeo trigger grab.
        if (Highlighted.GrabWithGrip)
        {
            if (_hand.GripDown)
                BeginGrab(Highlighted, releaseOnTriggerUp: false, "grip", "proximity");
            return;
        }

        // TRIGGER-ONLY targets: board figures (ITriggerOnlyGrabbable, hardware MP test
        // 2026-08 requirement (b)) and — since the 2026-08-11 hardware report — CARDS
        // (see IsTriggerOnly). Their one and only entry is the trigger edge below, with
        // its UI/laser arbitration; a grip squeeze near them never grabs THEM. The grip
        // edge instead FALLS THROUGH to the nearest grip-grabbable in reach (tray bar/
        // panel — see TryGripFallThrough, hardware test 2026-08-11 item 2), and only
        // when none exists is the refusal NAMED (throttled) so the next hardware log
        // explains "I squeezed and nothing happened" instead of reading as a dead
        // controller.
        //
        // ARBITRATION (blueprint critical guard): the trigger is ALSO the uGUI/
        // laser/board "click". Only claim it for a proximity grab when the ray is
        // NOT clamped to a UI/interactive surface this frame. Ray.HasFreshUiHit is
        // the single unified signal every trigger-click path already raises — game
        // UI panels (RayUguiDriver), fan/board cards (CardsDriver fan/board laser),
        // and the flat screen (FlatScreen) all set Ray.UiHitOverride, and the board
        // far-click uses the same flag to skip the trigger. So we DEFER to the laser
        // rather than fight it: a laser-pluck / UI click always wins the trigger, and
        // a trigger pull with an empty hand (Highlighted == null) or near no card
        // falls straight through to the UI/board click exactly as before. When the
        // laser-pluck and a proximity highlight coincide, whichever grabs first sets
        // Held and the other early-outs on Held != null — no double grab.
        if (IsTriggerOnly(Highlighted))
        {
            if (_hand.TriggerDown && !_hand.Ray.HasFreshUiHit)
            {
                BeginGrab(Highlighted, releaseOnTriggerUp: true, "trigger", "proximity");
            }
            else if (_hand.GripDown)
            {
                TryGripFallThrough();
            }
            return;
        }

        // Anything left grabs with the TRIGGER (same UI/laser arbitration as above). The
        // [Cards] GrabButton dial that used to switch this branch is retired: no registered
        // grabbable ever reached it (cards + figures take the trigger-only branch, panels/
        // tray bars the GrabWithGrip branch, pile stacks refuse CanGrab), so the dial
        // switched nothing. Trigger is the Demeo default a future grabbable in neither
        // class inherits.
        if (_hand.TriggerDown && !_hand.Ray.HasFreshUiHit)
        {
            BeginGrab(Highlighted, releaseOnTriggerUp: true, "trigger", "proximity");
            return;
        }
        // Grip independence holds for this class too: a default-trigger highlight must not
        // eat a grip meant for a grip-grabbable behind it (same rule as the trigger-only
        // branch; currently no registered grabbable lives in this class, see above).
        if (_hand.GripDown)
            TryGripFallThrough();
    }

    /// <summary>
    /// GRIP FALL-THROUGH (hardware test 2026-08-11, verbatim: "Beim greifbalken am
    /// Controllboard kommt es öfters vor, dass ich ihn (mit der Greiftaste) nicht greife, da
    /// meine Hand so nah an einer Karte ist die auf dem Controllboard abliegt, dass sie
    /// gehighlighted wird. Ein highlighting der Karte sollte nicht den Greifbalken
    /// deaktivieren - damit es da nicht zu verschwechslung kommt sind es zwei verschiedene
    /// Tasten mit denen man es bedient (greiftaste für den Balken und trigger für die
    /// Karte).").
    ///
    /// Since the trigger-only round, a GRIP press with a trigger-only Highlighted (a card
    /// lying on the tray) hit that branch's refusal-and-return — the grip never reached
    /// grip-grabbables, and the nearest-candidate election had already given the highlight
    /// to the card, so the tray handle bar right under it was unreachable. The two buttons
    /// operate two DIFFERENT object classes independently now: on GRIP-down with a non-grip
    /// highlight, the candidate search is re-run restricted to <see cref="IGrabbable.GrabWithGrip"/>
    /// targets within reach and the nearest one is grabbed.
    ///
    /// The card highlight is left UNTOUCHED (<c>clearHighlight: false</c>): the bar was not
    /// the elected highlight, so no bar hover affordance existed to hand over — exactly what
    /// the bar shows today — and the card rides the tray the bar drags, so the hand STAYS
    /// near it; clearing would flash the card highlight off and back on around every bar
    /// drag (the "grab flashes" class of defect). While the bar is held the hold loop reads
    /// only the grip, so the frozen highlight cannot promise a trigger grab it would then
    /// refuse — the trigger stays the card's button the moment the bar is released.
    ///
    /// The named refusal remains ONLY for the case where the grip finds no grip-grabbable
    /// in reach.
    /// </summary>
    private void TryGripFallThrough()
    {
        IGrabbable? target = FindNearestGripGrabbable();
        if (target != null)
        {
            BeginGrab(target, releaseOnTriggerUp: false, "grip", "proximity fall-through",
                clearHighlight: false);
            return;
        }
        LogRefusal($"'{DescribeGrabbable(Highlighted!)}' is trigger-only (cards and figures " +
                   "grab with the TRIGGER; grip operates tray bars/panels — user 2026-08-11) " +
                   "and no grip-grabbable is in reach — grip ignored");
    }

    /// <summary>
    /// Nearest registered grabbable that takes the GRIP (<see cref="IGrabbable.GrabWithGrip"/> —
    /// tray handle bars, world panels) within palm reach, passing the same CanGrab/per-hand
    /// gates as <see cref="UpdateHighlight"/>. Runs only on a GripDown edge — never
    /// per-frame work.
    /// </summary>
    private IGrabbable? FindNearestGripGrabbable()
    {
        var entries = VRInteractables.Grabbables;
        Vector3 palm = _hand.Rig.PalmCenter.position;
        float reach = ReachMeters * _hand.WorldScale;

        IGrabbable? nearest = null;
        float nearestDist = float.MaxValue;
        for (int i = 0; i < entries.Count; i++)
        {
            Collider collider = entries[i].Collider;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;
            IGrabbable target = entries[i].Target;
            if (!target.GrabWithGrip || !target.CanGrab)
                continue;
            if (target is IGrabbableHandFilter filter && !filter.AllowsHand(_hand))
                continue;
            float dist = Vector3.Distance(palm, collider.ClosestPoint(palm));
            if (dist <= reach && dist < nearestDist)
            {
                nearestDist = dist;
                nearest = target;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Is this target picked up by the TRIGGER edge only? True for the
    /// <see cref="ITriggerOnlyGrabbable"/> marker (board figures) and for CARDS — the two card
    /// grabbables named by type, the same explicit-inventory pattern as
    /// <see cref="HandGhosts"/>.IsHeldCard: exactly two card grabbables exist
    /// (<see cref="Cards.VRCard"/> = ability card, <see cref="Cards.ItemsPile.ItemChip"/> = item
    /// card), each is listed on purpose, and a NEW grabbable can never become trigger-only by
    /// accident (it opts in via the marker instead).
    ///
    /// WHY cards are trigger-only (hardware test 2026-08-11, verbatim): "Die Karten sollen nur
    /// mit dem trigger nehmbar sein, aktuell ist es greiftaste UND trigger". Until then a
    /// closing fist grip-grabbed a highlighted card too — a fallback added for the un-grabbable
    /// placed pick card of 2026-08-04, whose guard message promised "grip it to swap" while no
    /// grip route existed. That promise is GONE: the pick flow was fixed STRUCTURALLY
    /// (CardsDriver.3.Laser "TAKE-BACK RESTORED" — the dock no longer offers the cancel option,
    /// and the lift-priority accept/fallback trigger-grabs the lifted pick card via ForceGrab,
    /// which does not run this gate), so removing the grip edge cannot resurrect that bug. The
    /// fist near a card is now what it is near a figure: the canonical ACCIDENTAL gesture (and
    /// the grip half of the fingertip-ping chord).
    ///
    /// Scope: ACQUISITION only. Grip keeps every other role (GrabWithGrip panels/tray bars,
    /// world drag, gestures), and holding is untouched: a card taken by trigger is held while
    /// the TRIGGER stays pressed (releaseOnTriggerUp) — pressing or releasing the GRIP while a
    /// card is held does nothing to the card (the hold loop in <see cref="Tick"/> only reads
    /// the button that grabbed). The retired [Cards] GrabButton dial never affected cards —
    /// the user's request carried no config qualifier, and their figure requirement already
    /// read "Figuren sollen — wie die Karten — nur mit dem Trigger aufgenommen werden können"
    /// ("figures should — like the cards — only be pickable with the trigger").
    /// </summary>
    private static bool IsTriggerOnly(IGrabbable target) =>
        target is ITriggerOnlyGrabbable or Cards.VRCard or Cards.ItemsPile.ItemChip;

    /// <summary>
    /// Shared grab entry (proximity Tick paths); ForceGrab is the laser variant.
    /// <paramref name="clearHighlight"/> is false ONLY for the grip fall-through
    /// (<see cref="TryGripFallThrough"/>), where the grabbed target is NOT the highlighted
    /// object and the highlight must not flash off around the bar drag.
    /// </summary>
    private void BeginGrab(IGrabbable target, bool releaseOnTriggerUp, string button, string source,
        bool clearHighlight = true)
    {
        Held = target;
        _releaseOnTriggerUp = releaseOnTriggerUp;
        _grabLabel = $"{button}/{source}";
        if (clearHighlight)
            SetHighlighted(null);
        Held.OnGrab(_hand);
        _hand.SendHaptic(HapticPreset.GrabPulse);
        // Controls lesson: "reach out and take hold of something". Any near grab counts —
        // a figure, a prop or a card are all the same gesture, which is the thing being taught.
        Compat.ControlsProgress.Notify(Compat.ControlAction.ProximityGrab);
        LogGrab($"{_hand.Side} grab — {_grabLabel}.");
    }

    /// <summary>Change-deduped grab/release trace (button + source) — edge events only.</summary>
    private void LogGrab(string message)
    {
        if (message == _lastGrabLog)
            return;
        _lastGrabLog = message;
        Core.VRLog.Debug("Interact", message);
    }

    /// <summary>
    /// P6: programmatic grab for the Demeo laser-pluck — the Cards driver pulls a
    /// laser-pointed fan card into this hand on TriggerDown. With
    /// <paramref name="releaseOnTriggerUp"/> the hold button becomes the trigger
    /// instead of the grip (so the pluck gesture is press-point-release).
    /// Returns false when this hand already holds something or the target refuses.
    /// </summary>
    public bool ForceGrab(IGrabbable target, bool releaseOnTriggerUp = false)
    {
        // Refusal diagnostics (user bug A): the laser drivers (RayGrabDriver, fan pluck,
        // figure pluck) silently swallowed a false return — the hardware log showed
        // "LASER-CARRY armed" eleven times with no engage and no reason. Name the gate.
        if (!_enabled)
        {
            LogRefusal($"ForceGrab refused — interactor disabled by mode policy (mode={Core.Events.VRModeStateMachine.CurrentMode})");
            return false;
        }
        if (!_hand.HasPose)
            return false;
        if (Held != null)
        {
            LogRefusal($"ForceGrab refused — already holding '{DescribeGrabbable(Held)}' ({_grabLabel})");
            return false;
        }
        if (target == null)
            return false;
        if (!target.CanGrab)
        {
            LogRefusal($"ForceGrab refused — target '{DescribeGrabbable(target)}' CanGrab=false");
            return false;
        }
        if (target is IGrabbableHandFilter filter && !filter.AllowsHand(_hand))
        {
            LogRefusal($"ForceGrab refused — target '{DescribeGrabbable(target)}' AllowsHand({_hand.Side})=false");
            return false;
        }
        BeginGrab(target, releaseOnTriggerUp, releaseOnTriggerUp ? "trigger" : "grip", "laser");
        return true;
    }

    /// <summary>
    /// GRAB STATE self-heal (user bug A, structural): every path that ends a grab is
    /// SUPPOSED to release through <see cref="Tick"/>/<see cref="CancelAll"/>, but a held
    /// object can also die underneath the hold — destroyed with its window, re-parked to
    /// the pool (SetActive(false) → OnDisable), or its per-hand filter can start refusing
    /// this hand (arbitration/dominance change). A stale <see cref="Held"/> would then
    /// refuse EVERY subsequent grab (and keep <c>RayInteractor.Active</c> false — laser
    /// gone) while hover haptics elsewhere still fire. Re-derive validity every Tick and
    /// force-release with a Warn instead of latching.
    /// </summary>
    private bool HealDeadHeld()
    {
        IGrabbable held = Held!;
        bool destroyed = held is UnityEngine.Object obj && obj == null;
        string? why = null;
        if (destroyed)
            why = "held object was destroyed";
        else if (held is MonoBehaviour mb && !mb.isActiveAndEnabled)
            why = $"held object '{mb.name}' was disabled/re-parked while held";
        else if (held is IGrabbableHandFilter filter && !filter.AllowsHand(_hand))
            why = $"held object '{DescribeGrabbable(held)}' no longer allows this hand";
        if (why == null)
            return false;

        Held = null;
        _releaseOnTriggerUp = false;
        if (!destroyed)
        {
            try
            {
                held.OnRelease(_hand, Vector3.zero);
            }
            catch (Exception ex)
            {
                Core.VRLog.Warn("Interact", $"GRAB STATE heal: OnRelease threw during heal ({ex.GetType().Name}: {ex.Message}) — state cleared anyway.");
            }
        }
        Core.VRLog.Warn("Interact", $"GRAB STATE heal: {_hand.Side} force-released ({_grabLabel}) — {why}. Grabs re-enabled.");
        return true;
    }

    /// <summary>Stable display name for a grabbable (MonoBehaviour name when alive).</summary>
    private static string DescribeGrabbable(IGrabbable target)
    {
        if (target is UnityEngine.Object obj)
            return obj == null ? $"{target.GetType().Name} (destroyed)" : obj.name;
        return target.GetType().Name;
    }

    /// <summary>Throttled (1/s per hand) refusal diagnostic — Info level so hardware logs carry it.</summary>
    private void LogRefusal(string reason)
    {
        if (Time.unscaledTime < _nextRefusalLogAt)
            return;
        _nextRefusalLogAt = Time.unscaledTime + 1f;
        Core.VRLog.Info("Interact", $"{_hand.Side} grab refused — {reason}.");
    }

    /// <summary>
    /// Grab-intent edge with no highlight: scan for the nearest IN-REACH registered
    /// grabbable that was skipped by a gate and log which gate ate it (throttled).
    /// Runs only on GripDown/TriggerDown frames — never per-frame work.
    /// </summary>
    private void LogNoCandidateRefusal()
    {
        if (Time.unscaledTime < _nextRefusalLogAt)
            return; // pre-check so the scan below is skipped while throttled

        var entries = VRInteractables.Grabbables;
        Vector3 palm = _hand.Rig.PalmCenter.position;
        float reach = ReachMeters * _hand.WorldScale;
        string? reason = null;
        float nearest = float.MaxValue;
        for (int i = 0; i < entries.Count; i++)
        {
            Collider collider = entries[i].Collider;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;
            float dist = Vector3.Distance(palm, collider.ClosestPoint(palm));
            if (dist > reach || dist >= nearest)
                continue;
            IGrabbable target = entries[i].Target;
            if (!target.CanGrab)
            {
                nearest = dist;
                reason = $"nearest in-reach grabbable '{DescribeGrabbable(target)}' has CanGrab=false";
            }
            else if (target is IGrabbableHandFilter filter && !filter.AllowsHand(_hand))
            {
                nearest = dist;
                reason = $"nearest in-reach grabbable '{DescribeGrabbable(target)}' refuses this hand (AllowsHand)";
            }
            // A target passing both gates would have been highlighted — not reachable here.
        }
        if (reason != null)
            LogRefusal(reason);
    }

    internal void CancelAll()
    {
        if (Held != null)
        {
            IGrabbable released = Held;
            Held = null;
            _releaseOnTriggerUp = false;
            released.OnRelease(_hand, Vector3.zero);
            LogGrab($"{_hand.Side} cancel — {_grabLabel}.");
        }
        SetHighlighted(null);
    }

    /// <summary>
    /// Elect this hand's hover candidate: the nearest registered grabbable inside the palm reach
    /// that still passes every gate. Two kinds of stickiness sit on top of that raw election, and
    /// they answer two different reports:
    ///
    /// <para>RIVAL SWITCH (<see cref="SwitchMarginMeters"/>, hardware test #8) — a second target
    /// has to be decisively closer to steal a live highlight, so two overlapping fan cards cannot
    /// trade it every frame and buzz the controller. Untouched by this round.</para>
    ///
    /// <para>HOVER SCHMITT TRIGGER (<see cref="ExitReachFactor"/> + <see cref="ExitDwellSeconds"/>,
    /// user 2026-09-02: "Kein highlighting wenn ich mit der hand ueber die Figur fahre") — until
    /// ModBuild 336 there was hysteresis for SWITCHING and none at all for LOSING: the sticky
    /// branch required a non-null rival, so the very frame the election named nobody the highlight
    /// was torn down. The ModBuild 335 hardware log shows exactly that, and shows it for every
    /// figure rather than only the boss the user reported: ENGAGED and CLEARED one to three lines
    /// apart in a log printing ~20 lines a frame, over and over, on SpittingDrake, ElderDrake,
    /// RendingDrakeElite and Mindthief alike. A glow that lives one or two frames is invisible.
    /// The current highlight now keeps the hover out to the wider EXIT ring, and survives a gap in
    /// the election for the exit dwell — long enough to cover FigureGrabDriver's own six-frame
    /// re-entry dwell — while <see cref="_highlightGrabbable"/> stays false throughout so the GRAB
    /// keeps exactly the gates it had before.</para>
    ///
    /// <para>Every exit also records WHICH gate ended it (<see cref="_highlightBlocker"/>). Three
    /// rounds were spent on this defect against a log that said only "CLEARED"; a bare event cannot
    /// tell "the hand left" from "the driver elected nobody this frame", and those want opposite
    /// fixes.</para>
    /// </summary>
    private void UpdateHighlight()
    {
        var entries = VRInteractables.Grabbables;
        Vector3 palm = _hand.Rig.PalmCenter.position;
        float reach = ReachMeters * _hand.WorldScale;
        float exitReach = reach * ExitReachFactor;
        float scale = Mathf.Max(_hand.WorldScale, 1e-4f);

        IGrabbable? nearest = null;
        float nearestDist = float.MaxValue;
        float currentDist = float.MaxValue; // distance to the CURRENT highlight while it is measurable
        bool currentMeasured = false;       // its collider is alive, so currentDist means something
        bool currentEligible = false;       // it passed every gate this frame (the pre-336 condition)
        string currentBlocker = "it is no longer a registered grabbable";
        bool sawDead = false;

        for (int i = 0; i < entries.Count; i++)
        {
            IGrabbable target = entries[i].Target;
            bool isCurrent = Highlighted != null && ReferenceEquals(target, Highlighted);

            Collider collider = entries[i].Collider;
            if (collider == null)
            {
                sawDead = true;
                if (isCurrent)
                    currentBlocker = "its collider was destroyed";
                continue;
            }
            if (!collider.enabled || !collider.gameObject.activeInHierarchy)
            {
                if (isCurrent)
                    currentBlocker = collider.enabled
                        ? "its object was deactivated (re-parked to a pool, or hidden)"
                        : "its collider was switched off";
                continue;
            }

            // Measured BEFORE the policy gates so a vetoed current highlight still reports where
            // the hand actually is — the exit ring is a distance test and has to run on the frames
            // the gates fail, which are the only frames it matters on.
            float dist = Vector3.Distance(palm, collider.ClosestPoint(palm));
            if (isCurrent)
            {
                currentDist = dist;
                currentMeasured = true;
            }

            if (!target.CanGrab)
            {
                if (isCurrent)
                    currentBlocker = "CanGrab went false (a busy figure the turn machine is waiting "
                                     + "on, a figure a remote player took, a dead actor, or the "
                                     + "[FigureGrab] GrabFigures dial)";
                continue;
            }
            // Per-hand gate (P7): e.g. fan cards reject the fan-owning hand entirely.
            if (target is IGrabbableHandFilter filter && !filter.AllowsHand(_hand))
            {
                if (isCurrent)
                    currentBlocker = "the per-hand veto turned it off for this hand "
                                     + "(IGrabbableHandFilter.AllowsHand=false — for a board figure "
                                     + "that is FigureGrabDriver's election naming another figure, "
                                     + "or none at all, this frame)";
                continue;
            }

            if (isCurrent)
            {
                currentEligible = true;
                if (dist > reach)
                    currentBlocker = $"the hand moved out to {dist / scale * 1000f:F0} mm real, past "
                                     + $"the {ReachMeters * 1000f:F0} mm palm reach";
            }
            if (dist <= reach && dist < nearestDist)
            {
                nearestDist = dist;
                nearest = target;
            }
        }

        if (sawDead)
            VRInteractables.Prune();

        // Sticky candidate: keep the current highlight while it is still in reach and
        // the rival is not decisively closer (haptic-buzz fix, see SwitchMarginMeters).
        if (nearest != null && Highlighted != null && !ReferenceEquals(nearest, Highlighted)
            && currentEligible && currentDist <= reach
            && nearestDist > currentDist - SwitchMarginMeters * _hand.WorldScale)
        {
            _highlightDropAt = -1f;
            _highlightGrabbable = true;
            return;
        }

        if (ReferenceEquals(nearest, Highlighted))
        {
            // The election still names what is already lit (or both are null) — the normal case.
            _highlightDropAt = -1f;
            _highlightGrabbable = Highlighted != null;
            return;
        }

        if (Highlighted != null && nearest == null)
        {
            // THE EXIT. Nothing is elected; hold the glow while the target is still inside the exit
            // ring and the dwell has not run out, but never offer the grab while doing so.
            _highlightGrabbable = false;
            _highlightBlocker = currentBlocker;
            if (currentMeasured && currentDist <= exitReach)
            {
                if (_highlightDropAt < 0f)
                    _highlightDropAt = Time.unscaledTime;
                if (Time.unscaledTime - _highlightDropAt < ExitDwellSeconds)
                    return;
                LogHighlightDrop(DescribeGrabbable(Highlighted),
                    $"{currentBlocker}, and it stayed that way for the whole "
                    + $"{ExitDwellSeconds:0.00} s hover tail (it is {currentDist / scale * 1000f:F0} mm "
                    + $"real from the palm, inside the {ReachMeters * ExitReachFactor * 1000f:F0} mm "
                    + "exit ring, so DISTANCE is not what ended this hover)");
            }
            else
            {
                LogHighlightDrop(DescribeGrabbable(Highlighted),
                    currentMeasured
                        ? $"{currentBlocker}; at {currentDist / scale * 1000f:F0} mm real it is also "
                          + $"outside the {ReachMeters * ExitReachFactor * 1000f:F0} mm exit ring, so "
                          + "the hover ends immediately"
                        : $"{currentBlocker} — nothing left to measure, so the hover ends immediately");
            }
            SetHighlighted(null);
            return;
        }

        // A different target takes the highlight outright (or the first one arrives).
        SetHighlighted(nearest);
        if (nearest != null)
            _hand.SendHaptic(HapticPreset.HoverTick);
    }

    /// <summary>
    /// NAME THE BLOCKER, not the number: one line per hover that actually ENDED, saying which gate
    /// ended it. Throttled to one per second per hand and carrying the count it swallowed, so a
    /// hover that is still flapping reads as a flap instead of as a single tidy event.
    ///
    /// <para>Three hardware rounds were spent on an invisible pre-grab glow against a log whose
    /// only exit evidence was the words "pre-grab highlight CLEARED (SpittingDrakeID)". That line
    /// cannot separate "the hand left the figure" from "the election named nobody for two frames",
    /// and the two want opposite fixes. This one names it.</para>
    /// </summary>
    private void LogHighlightDrop(string label, string reason)
    {
        _dropsSinceLastLog++;
        if (Time.unscaledTime < _nextDropLogAt)
            return;
        _nextDropLogAt = Time.unscaledTime + 1f;
        int swallowed = _dropsSinceLastLog - 1;
        _dropsSinceLastLog = 0;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        Core.VRLog.Note("Interact",
            $"{_hand.Side} hover ENDED on '{label}' — {reason}. "
            + $"Hover hysteresis: enter {ReachMeters * 1000f:F0} mm, exit "
            + $"{ReachMeters * ExitReachFactor * 1000f:F0} mm, tail {ExitDwellSeconds:0.00} s"
            + (swallowed > 0
                ? $". {swallowed} further hover end(s) in the last second are not printed — a count "
                  + "above zero here means the hover is still flapping and the tail is too short."
                : "."));
    }

    private void SetHighlighted(IGrabbable? target)
    {
        if (ReferenceEquals(target, Highlighted))
            return;

        // Any real change of candidate restarts the hysteresis: a fresh highlight is grabbable by
        // definition (it was just elected), and a cleared one has no tail to serve.
        _highlightDropAt = -1f;
        _highlightGrabbable = target != null;

        if (Highlighted is IGrabHighlight oldHighlight)
            oldHighlight.OnGrabHighlight(_hand, false);
        Highlighted = target;
        if (Highlighted is IGrabHighlight newHighlight)
            newHighlight.OnGrabHighlight(_hand, true);

        try
        {
            HighlightChanged?.Invoke(_hand, Highlighted);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Interact", $"HighlightChanged subscriber threw: {ex}");
        }
    }
}
