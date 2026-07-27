using System;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Proximity grab (FROZEN Phase-2 API) — Demeo pattern: no physics joints, no
/// collider-on-hand. The nearest registered <see cref="IGrabbable"/> with
/// <c>CanGrab</c> within palm reach becomes the highlighted candidate
/// (<see cref="IGrabHighlight"/> hook + <see cref="HighlightChanged"/> event for
/// emissive pulses); pressing the grab button ([Cards] GrabButton — Trigger like
/// Demeo by default, or Grip) grabs it, releasing that button releases it with the
/// measured palm velocity — deterministic and MP-safe. The Trigger path defers to
/// any ray/UI click via <see cref="RayInteractor.HasFreshUiHit"/> (see Tick), and
/// shares the trigger-up release edge with the laser pluck (<see cref="ForceGrab"/>).
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
            // plucked objects, see ForceGrab / GrabButton) → release with palm velocity.
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
        // button regardless of [Cards] GrabButton — the tester found grip more
        // intuitive for moving boards, while cards keep the Demeo trigger grab.
        if (Highlighted.GrabWithGrip)
        {
            if (_hand.GripDown)
                BeginGrab(Highlighted, releaseOnTriggerUp: false, "grip", "proximity");
            return;
        }

        // G3 (test-#22 Demeo parity): the grab edge is the button selected by
        // [Cards] GrabButton — Trigger (Demeo default) or Grip (legacy). CardsConfig
        // may be unbound before the Cards module inits, but Highlighted is non-null
        // only when a grabbable is registered (today: cards), so it is bound here;
        // fall back to the Trigger default defensively.
        bool useTrigger = CardsConfig.GrabButton == null
            || CardsConfig.GrabButton.Value == CardGrabButton.Trigger;

        if (useTrigger)
        {
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
            if (_hand.TriggerDown && !_hand.Ray.HasFreshUiHit)
                BeginGrab(Highlighted, releaseOnTriggerUp: true, "trigger", "proximity");
        }
        else if (_hand.GripDown)
        {
            BeginGrab(Highlighted, releaseOnTriggerUp: false, "grip", "proximity");
        }
    }

    /// <summary>Shared grab entry (proximity Tick paths); ForceGrab is the laser variant.</summary>
    private void BeginGrab(IGrabbable target, bool releaseOnTriggerUp, string button, string source)
    {
        Held = target;
        _releaseOnTriggerUp = releaseOnTriggerUp;
        _grabLabel = $"{button}/{source}";
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
