using System;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Hands;

/// <summary>Coarse hand pose derived from controller input. FROZEN Phase-2 API.</summary>
internal enum HandPose
{
    /// <summary>Nothing special held/pressed.</summary>
    Idle,

    /// <summary>All curls low — flat open hand.</summary>
    OpenPalm,

    /// <summary>Grip held, trigger released — index extended (UI/board pointing).</summary>
    Point,

    /// <summary>Grip + trigger held — closed fist (world grab in Phase 4).</summary>
    Fist
}

/// <summary>
/// One tracked hand (FROZEN Phase-2 API): device pose + input, the
/// <see cref="HandRig"/> transform contract, finger articulation, haptics and the
/// four interaction primitives (<see cref="Poke"/>, <see cref="Ray"/>,
/// <see cref="Grabber"/>, <see cref="PalmGate"/>).
///
/// Pose source: the game-shipped <c>UnityEngine.XR.InputDevices</c> device API
/// (XRNode.LeftHand/RightHand + CommonUsages) — zero dependency on the game's
/// InputSystem version. Verified against the REAL UnityEngine.XRModule.dll
/// (2021.3.5f1) with ilspycmd (2026-07-15):
/// <code>
///   public static InputDevice InputDevices.GetDeviceAtXRNode(XRNode node)
///   public bool InputDevice.TryGetFeatureValue(InputFeatureUsage&lt;T&gt; usage, out T value)
///   // CommonUsages (all present in the shipped 2021.3 XRModule):
///   isTracked (bool), devicePosition (Vector3), deviceRotation (Quaternion),
///   trigger (float), grip (float), triggerButton/gripButton (bool),
///   primaryButton/secondaryButton (bool), primaryTouch/secondaryTouch (bool),
///   primary2DAxis (Vector2), primary2DAxisTouch/primary2DAxisClick (bool)
/// </code>
/// Alternative (not used): InputSystem actions (&lt;XRController&gt;{LeftHand}/…) — the
/// game ships InputSystem 1.3.0, which would work, but couples us to its action/state
/// pipeline; the device API reads the same OpenXR data directly.
///
/// The GameObject lives under the Phase-1 rig root, so localPosition/localRotation are
/// tracking-space and the diorama scale applies automatically. Update order within a
/// frame: device read → derived input → finger curler → interactors.
/// </summary>
internal sealed class VRHand : MonoBehaviour
{
    private const float PressThreshold = 0.75f;
    private const float ReleaseThreshold = 0.55f;
    private const float HoverHapticMinInterval = 0.05f;

    /// <summary>
    /// Static visual/rig offset between the OpenXR device (grip) pose and the hand
    /// frame (wrist, +Z fingers, +Y back of hand). Controller grip poses point the
    /// device's -Z roughly along the thumb; tune on hardware (docs/TESTING-P2.md).
    /// </summary>
    private static readonly Vector3 VisualOffsetPosition = new(0f, -0.02f, -0.06f);
    private static readonly Quaternion VisualOffsetRotation = Quaternion.Euler(-40f, 0f, 0f);

    private InputDevice _device;
    private FingerCurler _curler = null!;
    private Transform _handRoot = null!;

    // Velocity ring buffer (palm position, world) — fixed size, no allocations.
    private const int VelocitySamples = 8;
    private readonly Vector3[] _velPositions = new Vector3[VelocitySamples];
    private readonly float[] _velTimes = new float[VelocitySamples];
    private int _velHead;
    private int _velCount;

    private float _lastHoverHapticTime;

    // Simulated input (dev harness).
    private bool _simulated;
    private float _simTrigger;
    private float _simGrip;

    // ---- frozen public surface -----------------------------------------------------------

    public HandSide Side { get; private set; }

    /// <summary>Transform contract (wrist, palm, fingertip, per-finger joints…). Never null after init.</summary>
    public HandRig Rig { get; private set; } = null!;

    /// <summary>True while the device reports tracking (or the hand is simulated).</summary>
    public bool IsTracked { get; private set; }

    /// <summary>True when driven by the dev harness instead of a real device.</summary>
    public bool IsSimulated => _simulated;

    /// <summary>True when the hand has a usable pose this frame (tracked or simulated).</summary>
    public bool HasPose => IsTracked;

    /// <summary>Analog trigger 0..1 (index finger).</summary>
    public float TriggerValue { get; private set; }

    /// <summary>Analog grip 0..1 (middle/ring/pinky).</summary>
    public float GripValue { get; private set; }

    /// <summary>Digital trigger with 0.75/0.55 hysteresis.</summary>
    public bool TriggerPressed { get; private set; }

    /// <summary>Digital grip with 0.75/0.55 hysteresis.</summary>
    public bool GripPressed { get; private set; }

    /// <summary>Trigger crossed into pressed this frame.</summary>
    public bool TriggerDown { get; private set; }

    /// <summary>Trigger crossed into released this frame.</summary>
    public bool TriggerUp { get; private set; }

    /// <summary>Grip crossed into pressed this frame.</summary>
    public bool GripDown { get; private set; }

    /// <summary>Grip crossed into released this frame.</summary>
    public bool GripUp { get; private set; }

    /// <summary>A/X button.</summary>
    public bool PrimaryButton { get; private set; }

    /// <summary>B/Y button.</summary>
    public bool SecondaryButton { get; private set; }

    /// <summary>PrimaryButton went down this frame.</summary>
    public bool PrimaryDown { get; private set; }

    /// <summary>SecondaryButton went down this frame.</summary>
    public bool SecondaryDown { get; private set; }

    /// <summary>Capacitive thumb rest detection (any of primary/secondary/stick touch).</summary>
    public bool ThumbTouch { get; private set; }

    /// <summary>Thumbstick axis (Phase-3a uses left/right for AoE rotation).</summary>
    public Vector2 Thumbstick { get; private set; }

    /// <summary>Coarse pose classification (point / open palm / fist).</summary>
    public HandPose Pose { get; private set; }

    /// <summary>Palm velocity, world units/s (already diorama-scaled). For throw/release.</summary>
    public Vector3 PalmVelocity { get; private set; }

    /// <summary>Diorama scale at this hand (lossyScale of the rig). Multiply "real meters" by this.</summary>
    public float WorldScale { get; private set; } = 1f;

    /// <summary>Fingertip press interactor.</summary>
    public PokeInteractor Poke { get; private set; } = null!;

    /// <summary>Far-interaction ray (implements IPickProvider).</summary>
    public RayInteractor Ray { get; private set; } = null!;

    /// <summary>Proximity grab interactor.</summary>
    public ProximityGrabber Grabber { get; private set; } = null!;

    /// <summary>Palm-toward-face gate (card fan trigger).</summary>
    public PalmGate PalmGate { get; private set; } = null!;

    /// <summary>Tracking gained/lost (also fired when simulation toggles).</summary>
    public event Action<VRHand, bool>? TrackedChanged;

    /// <summary>Current curl 0..1 of a finger (smoothed).</summary>
    public float GetCurl(Finger finger) => _curler.GetCurl(finger);

    /// <summary>Fire a haptic preset on this hand's controller (rate-limited for HoverTick).</summary>
    public void SendHaptic(HapticPreset preset)
    {
        if (preset == HapticPreset.HoverTick)
        {
            if (Time.unscaledTime - _lastHoverHapticTime < HoverHapticMinInterval)
                return;
            _lastHoverHapticTime = Time.unscaledTime;
        }
        VRHaptics.Play(_device, preset);
    }

    // ---- lifecycle (HandsDriver only) ------------------------------------------------------

    internal void Initialize(HandSide side)
    {
        Side = side;

        // Device pose lands on THIS transform; the hand frame hangs below with a
        // static offset so art/rig tuning never touches tracking code.
        _handRoot = new GameObject("HandRoot").transform;
        _handRoot.SetParent(transform, worldPositionStays: false);
        _handRoot.localPosition = VisualOffsetPosition;
        _handRoot.localRotation = VisualOffsetRotation;

        Rig = HandVisuals.Build(_handRoot, side);
        _curler = new FingerCurler(Rig);

        Poke = new PokeInteractor(this);
        Ray = new RayInteractor(this);
        Grabber = new ProximityGrabber(this);
        PalmGate = new PalmGate(this);

        VRLog.Info("Hands", $"{side} hand initialized (rig complete: {Rig.IsComplete}).");
    }

    /// <summary>Apply the mode policy: which interactors are live.</summary>
    internal void SetInteractorMask(Core.Events.Interactors mask)
    {
        Poke.Enabled = (mask & Core.Events.Interactors.Poke) != 0;
        Ray.Enabled = (mask & Core.Events.Interactors.Ray) != 0;
        Grabber.Enabled = (mask & Core.Events.Interactors.Grab) != 0;
        PalmGate.Enabled = (mask & Core.Events.Interactors.PalmGate) != 0;
    }

    internal void SetSimulated(bool simulated)
    {
        if (_simulated == simulated)
            return;
        _simulated = simulated;
        if (!simulated)
            SetTracked(false);
    }

    /// <summary>Dev harness: feed a fake local pose + analog values.</summary>
    internal void SetSimulatedInput(Vector3 localPosition, Quaternion localRotation, float trigger, float grip)
    {
        transform.localPosition = localPosition;
        transform.localRotation = localRotation;
        _simTrigger = trigger;
        _simGrip = grip;
    }

    private void OnDestroy()
    {
        Ray?.DestroyVisuals();
        Poke?.CancelAll();
        Grabber?.CancelAll();
    }

    // ---- per-frame -------------------------------------------------------------------------

    private void Update()
    {
        WorldScale = transform.lossyScale.x;

        if (_simulated)
            ReadSimulated();
        else
            ReadDevice();

        UpdateVelocity();
        UpdatePoseClassification();
        UpdateCurlTargets();
        _curler.Tick(Time.deltaTime);

        // Interactors see the fresh pose; deterministic order.
        Poke.Tick();
        Ray.Tick();
        Grabber.Tick();
        PalmGate.Tick();
    }

    private void ReadDevice()
    {
        if (!_device.isValid)
        {
            _device = InputDevices.GetDeviceAtXRNode(Side == HandSide.Left ? XRNode.LeftHand : XRNode.RightHand);
            if (!_device.isValid)
            {
                SetTracked(false);
                ClearInput();
                return;
            }
        }

        bool tracked = _device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked;
        SetTracked(tracked);

        if (_device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
            transform.localPosition = position;
        if (_device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
            transform.localRotation = rotation;

        _device.TryGetFeatureValue(CommonUsages.trigger, out float trigger);
        _device.TryGetFeatureValue(CommonUsages.grip, out float grip);
        ApplyAnalog(trigger, grip);

        bool prevPrimary = PrimaryButton;
        bool prevSecondary = SecondaryButton;
        _device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary);
        _device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondary);
        PrimaryButton = primary;
        SecondaryButton = secondary;
        PrimaryDown = primary && !prevPrimary;
        SecondaryDown = secondary && !prevSecondary;

        _device.TryGetFeatureValue(CommonUsages.primaryTouch, out bool primaryTouch);
        _device.TryGetFeatureValue(CommonUsages.secondaryTouch, out bool secondaryTouch);
        _device.TryGetFeatureValue(CommonUsages.primary2DAxisTouch, out bool stickTouch);
        ThumbTouch = primaryTouch || secondaryTouch || stickTouch;

        _device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
        Thumbstick = stick;
    }

    private void ReadSimulated()
    {
        SetTracked(true);
        ApplyAnalog(_simTrigger, _simGrip);
        ThumbTouch = _simGrip > 0.5f;
        Thumbstick = Vector2.zero;
        PrimaryDown = SecondaryDown = false;
    }

    private void ApplyAnalog(float trigger, float grip)
    {
        TriggerValue = trigger;
        GripValue = grip;

        bool wasTrigger = TriggerPressed;
        TriggerPressed = wasTrigger ? trigger > ReleaseThreshold : trigger > PressThreshold;
        TriggerDown = TriggerPressed && !wasTrigger;
        TriggerUp = !TriggerPressed && wasTrigger;

        bool wasGrip = GripPressed;
        GripPressed = wasGrip ? grip > ReleaseThreshold : grip > PressThreshold;
        GripDown = GripPressed && !wasGrip;
        GripUp = !GripPressed && wasGrip;
    }

    private void ClearInput()
    {
        TriggerValue = GripValue = 0f;
        TriggerPressed = GripPressed = false;
        TriggerDown = TriggerUp = GripDown = GripUp = false;
        PrimaryButton = SecondaryButton = PrimaryDown = SecondaryDown = false;
        ThumbTouch = false;
        Thumbstick = Vector2.zero;
    }

    private void SetTracked(bool tracked)
    {
        if (IsTracked == tracked)
            return;
        IsTracked = tracked;
        try
        {
            TrackedChanged?.Invoke(this, tracked);
        }
        catch (Exception ex)
        {
            VRLog.Error("Hands", $"TrackedChanged subscriber threw: {ex}");
        }
        if (!tracked)
        {
            Poke.CancelAll();
            Grabber.CancelAll();
        }
    }

    private void UpdateVelocity()
    {
        Vector3 palm = Rig.PalmCenter.position;
        float now = Time.unscaledTime;

        _velPositions[_velHead] = palm;
        _velTimes[_velHead] = now;
        _velHead = (_velHead + 1) % VelocitySamples;
        if (_velCount < VelocitySamples)
            _velCount++;

        if (_velCount >= 2)
        {
            int oldest = (_velHead - _velCount + VelocitySamples) % VelocitySamples;
            float dt = now - _velTimes[oldest];
            PalmVelocity = dt > 1e-4f ? (palm - _velPositions[oldest]) / dt : Vector3.zero;
        }
    }

    /// <summary>
    /// Poses (LCVR pattern, ARCHITECTURE §4): point = grip held + trigger released;
    /// open palm = nothing held; fist = both held.
    /// </summary>
    private void UpdatePoseClassification()
    {
        bool gripHeld = GripValue > 0.5f;
        bool triggerHeld = TriggerValue > 0.5f;
        if (gripHeld && TriggerValue < 0.2f)
            Pose = HandPose.Point;
        else if (gripHeld && triggerHeld)
            Pose = HandPose.Fist;
        else if (GripValue < 0.15f && TriggerValue < 0.15f)
            Pose = HandPose.OpenPalm;
        else
            Pose = HandPose.Idle;
    }

    private void UpdateCurlTargets()
    {
        // Point pose keeps the index rigid regardless of trigger noise.
        float indexCurl = Pose == HandPose.Point ? 0f : TriggerValue;
        _curler.SetTarget(Finger.Index, indexCurl);
        _curler.SetTarget(Finger.Middle, GripValue);
        _curler.SetTarget(Finger.Ring, GripValue);
        _curler.SetTarget(Finger.Pinky, GripValue);
        _curler.SetTarget(Finger.Thumb, ThumbTouch ? 0.65f : 0.15f);
    }
}
