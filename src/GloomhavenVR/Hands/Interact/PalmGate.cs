using System;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// "Palm turned toward the face" detector with hysteresis (FROZEN Phase-2 API;
/// P6 additive: tunable thresholds + device-pose normal).
/// Phase-3b shows/hides the card fan on this gate (ARCHITECTURE §4).
///
/// Opens when dot(palmNormal, toHMD) &gt; <see cref="EnterThreshold"/>, closes when it
/// drops below <see cref="ExitThreshold"/> — the dead band prevents flicker at the
/// boundary. Ticked every frame by <see cref="VRHand"/>; state is queryable
/// (<see cref="IsOpen"/>) and edge-observable (<see cref="Changed"/>, main thread).
///
/// P6 (hardware test #8): the gate used the VISUAL rig's palm normal, which includes
/// the [Hands] GripPitchOffsetDegrees rotation (default -60°) between the tracked
/// grip pose and the hand model — so opening the fan demanded ~60° of wrist
/// supination BEYOND "palm faces me". <see cref="UseDevicePalmNormal"/> (default on)
/// evaluates the raw device pose instead (-Y of the grip pose = out of the physical
/// palm), matching what the wrist actually does. Thresholds are set per frame by the
/// Cards driver from [Cards] TiltThreshold (Demeo-style forgiving cone).
/// </summary>
internal sealed class PalmGate
{
    private const float DefaultEnterDot = 0.6f;
    private const float DefaultExitDot = 0.35f;

    private readonly VRHand _hand;
    private bool _enabled = true;

    internal PalmGate(VRHand hand) => _hand = hand;

    /// <summary>Dot(palmNormal, toHMD) above which the gate opens. Default = P2 constant 0.6.</summary>
    public float EnterThreshold = DefaultEnterDot;

    /// <summary>Dot(palmNormal, toHMD) below which the gate closes. Default = P2 constant 0.35.</summary>
    public float ExitThreshold = DefaultExitDot;

    /// <summary>
    /// Evaluate the palm normal in the DEVICE (grip-pose) frame instead of the visual
    /// hand rig — removes the [Hands] GripPitchOffsetDegrees penalty (see class doc).
    /// </summary>
    public bool UseDevicePalmNormal = true;

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

        // Palm normal: -Y of the grip pose (physical palm; simulated hands have no
        // grip-pitch offset applied to the device transform either) or the visual rig.
        Vector3 normal = UseDevicePalmNormal
            ? _hand.transform.rotation * Vector3.down
            : _hand.Rig.PalmNormal;
        CurrentDot = Vector3.Dot(normal, toHead.normalized);

        if (!IsOpen && CurrentDot > EnterThreshold)
            SetOpen(true);
        else if (IsOpen && CurrentDot < ExitThreshold)
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
