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

    private readonly VRHand _hand;
    private bool _enabled = true;

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
            // Grip released (or the object vanished) → release with palm velocity.
            if (!_hand.GripPressed)
            {
                IGrabbable released = Held;
                Held = null;
                released.OnRelease(_hand, _hand.PalmVelocity);
            }
            return;
        }

        UpdateHighlight();

        if (_hand.GripDown && Highlighted != null)
        {
            Held = Highlighted;
            SetHighlighted(null);
            Held.OnGrab(_hand);
            _hand.SendHaptic(HapticPreset.GrabPulse);
        }
    }

    internal void CancelAll()
    {
        if (Held != null)
        {
            IGrabbable released = Held;
            Held = null;
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

            float dist = Vector3.Distance(palm, collider.ClosestPoint(palm));
            if (dist <= reach && dist < nearestDist)
            {
                nearestDist = dist;
                nearest = target;
            }
        }

        if (sawDead)
            VRInteractables.Prune();

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
