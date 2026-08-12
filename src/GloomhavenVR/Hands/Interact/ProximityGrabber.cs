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
    /// </summary>
    private const float ReachMeters = 0.13f;

    /// <summary>
    /// Candidate stickiness (P6, hardware test #8; widened P7, test #10): a rival must
    /// be closer than the current highlight by this margin (meters, scale 1) to steal
    /// it — overlapping fan cards used to flap the highlight every frame, buzzing the
    /// controller. 2.5 cm means a neighboring card can never oscillate with the
    /// current one: the pop animation moves a card by ~3.5 cm, less than the margin
    /// plus the card strip spacing, so animation alone cannot flip the winner.
    /// </summary>
    private const float SwitchMarginMeters = 0.025f;

    private readonly VRHand _hand;
    private bool _enabled = true;
    private bool _releaseOnTriggerUp;

    // Which button + source grabbed the current hold, for the release log (G3).
    private string _grabLabel = "";
    private string _lastGrabLog = "";

    // Refusal diagnostic throttle (user bug A: "won't grab" gave a silent log — every
    // refused grab attempt now NAMES its gate, at most one line per second per hand).
    private float _nextRefusalLogAt;

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
    /// read "Figuren sollen — wie die Karten — nur mit dem Trigger aufgenommen werden können".
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

    private void UpdateHighlight()
    {
        var entries = VRInteractables.Grabbables;
        Vector3 palm = _hand.Rig.PalmCenter.position;
        float reach = ReachMeters * _hand.WorldScale;

        IGrabbable? nearest = null;
        float nearestDist = float.MaxValue;
        float currentDist = float.MaxValue; // distance to the CURRENT highlight, if still valid
        bool sawDead = false;

        for (int i = 0; i < entries.Count; i++)
        {
            Collider collider = entries[i].Collider;
            if (collider == null)
            {
                sawDead = true;
                continue;
            }
            if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;

            IGrabbable target = entries[i].Target;
            if (!target.CanGrab)
                continue;
            // Per-hand gate (P7): e.g. fan cards reject the fan-owning hand entirely.
            if (target is IGrabbableHandFilter filter && !filter.AllowsHand(_hand))
                continue;

            float dist = Vector3.Distance(palm, collider.ClosestPoint(palm));
            if (ReferenceEquals(target, Highlighted))
                currentDist = dist;
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
            && currentDist <= reach && nearestDist > currentDist - SwitchMarginMeters * _hand.WorldScale)
        {
            return;
        }

        if (!ReferenceEquals(nearest, Highlighted))
        {
            SetHighlighted(nearest);
            if (nearest != null)
                _hand.SendHaptic(HapticPreset.HoverTick);
        }
    }

    private void SetHighlighted(IGrabbable? target)
    {
        if (ReferenceEquals(target, Highlighted))
            return;

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
