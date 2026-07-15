using System;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// "Palm turned toward the face" detector with hysteresis (FROZEN Phase-2 API).
/// Phase-3b shows/hides the card fan on this gate (ARCHITECTURE §4).
///
/// Opens when dot(palmNormal, toHMD) &gt; 0.6, closes when it drops below 0.35 —
/// the dead band prevents flicker at the boundary. Ticked every frame by
/// <see cref="VRHand"/>; state is queryable (<see cref="IsOpen"/>) and edge-observable
/// (<see cref="Changed"/>, main thread).
/// </summary>
internal sealed class PalmGate
{
    private const float EnterDot = 0.6f;
    private const float ExitDot = 0.35f;

    private readonly VRHand _hand;
    private bool _enabled = true;

    internal PalmGate(VRHand hand) => _hand = hand;

    /// <summary>True while the palm faces the headset.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Latest dot(palmNormal, toHMD) — for debug overlays and tuning.</summary>
    public float CurrentDot { get; private set; }

    /// <summary>Fired on open/close transitions.</summary>
    public event Action<VRHand, bool>? Changed;

    /// <summary>Enable/disable (mode policy). Disabling force-closes the gate.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            if (!value)
                SetOpen(false);
        }
    }

    internal void Tick()
    {
        if (!_enabled || !_hand.HasPose)
            return;

        // Head position: the rig's head camera in VR; Camera.main in the desktop sim.
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;

        Vector3 palm = _hand.Rig.PalmCenter.position;
        Vector3 toHead = head.transform.position - palm;
        if (toHead.sqrMagnitude < 1e-8f)
            return;

        CurrentDot = Vector3.Dot(_hand.Rig.PalmNormal, toHead.normalized);

        if (!IsOpen && CurrentDot > EnterDot)
            SetOpen(true);
        else if (IsOpen && CurrentDot < ExitDot)
            SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        if (IsOpen == open)
            return;
        IsOpen = open;
        try
        {
            Changed?.Invoke(_hand, open);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Interact", $"PalmGate.Changed subscriber threw: {ex}");
        }
    }
}
