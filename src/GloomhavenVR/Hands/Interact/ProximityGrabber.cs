using System;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Proximity grab (FROZEN Phase-2 API) — Demeo pattern: no physics joints, no
/// collider-on-hand. The nearest registered <see cref="IGrabbable"/> with
/// <c>CanGrab</c> within palm reach becomes the highlighted candidate
/// (<see cref="IGrabHighlight"/> hook + <see cref="HighlightChanged"/> event for
/// emissive pulses); pressing grip grabs it, releasing grip releases it with the
/// measured palm velocity — deterministic and MP-safe.
/// </summary>
internal sealed class ProximityGrabber
{
    /// <summary>Palm reach in meters (scale 1).</summary>
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
        if (!_enabled || !_hand.HasPose)
            return;

        if (Held != null)
        {
            // Hold button released (grip — or trigger for laser-plucked objects,
            // see ForceGrab) → release with palm velocity.
            bool stillHeld = _releaseOnTriggerUp ? _hand.TriggerPressed : _hand.GripPressed;
            if (!stillHeld)
            {
                IGrabbable released = Held;
                Held = null;
                _releaseOnTriggerUp = false;
                released.OnRelease(_hand, _hand.PalmVelocity);
            }
            return;
        }

        UpdateHighlight();

        if (_hand.GripDown && Highlighted != null)
        {
            Held = Highlighted;
            _releaseOnTriggerUp = false;
            SetHighlighted(null);
            Held.OnGrab(_hand);
            _hand.SendHaptic(HapticPreset.GrabPulse);
        }
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
        if (!_enabled || !_hand.HasPose || Held != null || target == null || !target.CanGrab)
            return false;
        if (target is IGrabbableHandFilter filter && !filter.AllowsHand(_hand))
            return false;
        SetHighlighted(null);
        Held = target;
        _releaseOnTriggerUp = releaseOnTriggerUp;
        Held.OnGrab(_hand);
        _hand.SendHaptic(HapticPreset.GrabPulse);
        return true;
    }

    internal void CancelAll()
    {
        if (Held != null)
        {
            IGrabbable released = Held;
            Held = null;
            _releaseOnTriggerUp = false;
            released.OnRelease(_hand, Vector3.zero);
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
