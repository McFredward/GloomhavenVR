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
    /// OpenXR aim ("pointer") pose feature usages. Verified against the RuntimeDeps
    /// Unity.XR.OpenXR 1.10.0 source (tools/RuntimeDepsBuild/sources): every
    /// controller interaction profile registers a Pose-type ActionConfig named
    /// "pointer" bound to ".../input/aim/pose" with usages = { "Pointer" }
    /// (e.g. OculusTouchControllerProfile.cs "Pointer Pose" block), and
    /// OpenXRInteractionFeature.ActionConfig.usages documents that these strings
    /// "will be tagged onto UnityEngine.XR.InputDevice features" for
    /// TryGetFeatureValue. Pose usages expand to &lt;Usage&gt;Position/&lt;Usage&gt;Rotation —
    /// the same mapping that makes usage "Device" appear as CommonUsages.devicePosition
    /// ("DevicePosition"). Probed per device; grip pose is the fallback.
    /// </summary>
    private static readonly InputFeatureUsage<Vector3> PointerPositionUsage = new("PointerPosition");
    private static readonly InputFeatureUsage<Quaternion> PointerRotationUsage = new("PointerRotation");

    private InputDevice _device;
    private FingerCurler _curler = null!;
    private Transform _handRoot = null!;
    private float _appliedGripPitch = float.NaN;
    private float _appliedLateralOffset = float.NaN;
    private float _appliedVerticalOffset = float.NaN;
    private float _appliedForwardOffset = float.NaN;
    private float _appliedStyleScale = float.NaN;

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

    /// <summary>
    /// Thumbstick CLICK (push the stick straight down — <c>primary2DAxisClick</c>).
    /// Distinct from <see cref="Thumbstick"/> (the analog axis): SnapTurn and AoE read
    /// the axis, world-grab reads this click, so the two never collide. Sampled the
    /// same way as the face buttons (raw digital, no hysteresis — the click is already
    /// a debounced boolean from the runtime).
    /// </summary>
    public bool ThumbstickClick { get; private set; }

    /// <summary>ThumbstickClick went down this frame.</summary>
    public bool ThumbstickClickDown { get; private set; }

    /// <summary>ThumbstickClick went up this frame.</summary>
    public bool ThumbstickClickUp { get; private set; }

    /// <summary>Coarse pose classification (point / open palm / fist).</summary>
    public HandPose Pose { get; private set; }

    /// <summary>
    /// True while the device delivers the OpenXR aim ("pointer") pose. The laser ray
    /// should originate there (P1, hardware test #4) — the aim pose is the runtime's
    /// authored "where this controller points", independent of the grip-pose tilt.
    /// </summary>
    public bool HasPointerPose { get; private set; }

    /// <summary>World-space aim-pose origin (valid while <see cref="HasPointerPose"/>).</summary>
    public Vector3 PointerOrigin { get; private set; }

    /// <summary>World-space aim-pose forward (valid while <see cref="HasPointerPose"/>).</summary>
    public Vector3 PointerDirection { get; private set; }

    /// <summary>Palm velocity, world units/s (already diorama-scaled). For throw/release.</summary>
    public Vector3 PalmVelocity { get; private set; }

    /// <summary>Diorama scale at this hand (lossyScale of the rig). Multiply "real meters" by this.</summary>
    public float WorldScale { get; private set; } = 1f;

    /// <summary>Fingertip press interactor.</summary>
    public PokeInteractor Poke { get; private set; } = null!;

    /// <summary>Far-interaction ray (implements IPickProvider).</summary>
    public RayInteractor Ray { get; private set; } = null!;

    /// <summary>
    /// Far-ray uGUI pointing on registered world canvases (P6 additive; inert unless
    /// this is the dominant hand and <see cref="Ray"/> is enabled).
    /// </summary>
    public RayUguiDriver RayUgui { get; private set; } = null!;

    /// <summary>
    /// Far-ray grab-at-a-distance for world panels (feature #8; inert unless this is the
    /// dominant hand and <see cref="Ray"/> is active). Drags a window by its drag bar with
    /// the laser, alongside the near-hand grip grab.
    /// </summary>
    public RayGrabDriver RayGrab { get; private set; } = null!;

    /// <summary>Proximity grab interactor.</summary>
    public ProximityGrabber Grabber { get; private set; } = null!;

    /// <summary>Palm-toward-face gate (card fan trigger).</summary>
    public PalmGate PalmGate { get; private set; } = null!;

    /// <summary>Tracking gained/lost (also fired when simulation toggles).</summary>
    public event Action<VRHand, bool>? TrackedChanged;

    /// <summary>Current curl 0..1 of a finger (smoothed).</summary>
    public float GetCurl(Finger finger) => _curler.GetCurl(finger);

    /// <summary>
    /// The current aim ray in WORLD space — the OpenXR aim ("pointer") pose when the device
    /// delivers it, else the index-knuckle origin + hand-forward fallback. Mirrors exactly
    /// how <see cref="RayInteractor.Tick"/> seeds its pick, but is readable even while the
    /// hand HOLDS a grabbable (the ray's own pick is suppressed then): feature #8's
    /// laser-carry slides the window along THIS ray every frame during the drag.
    /// </summary>
    public void GetAimRay(out Vector3 origin, out Vector3 direction)
    {
        if (HasPointerPose)
        {
            origin = PointerOrigin;
            direction = PointerDirection;
        }
        else
        {
            origin = Rig.GetFinger(Finger.Index).Root.position;
            direction = Rig.Root.forward;
        }
    }

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
        // configurable offset so art/rig tuning never touches tracking code. The full
        // local pose (position + rotation) is set by SyncVisualOffset from config.
        _handRoot = new GameObject("HandRoot").transform;
        _handRoot.SetParent(transform, worldPositionStays: false);
        SyncVisualOffset();

        Rig = HandVisuals.Build(_handRoot, side);
        _curler = new FingerCurler(Rig);

        Poke = new PokeInteractor(this);
        Ray = new RayInteractor(this);
        RayUgui = new RayUguiDriver(this);
        RayGrab = new RayGrabDriver(this);
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
        RayUgui?.Cancel();
        RayGrab?.Cancel();
        Poke?.CancelAll();
        Grabber?.CancelAll();
    }

    // ---- per-frame -------------------------------------------------------------------------

    /// <summary>
    /// Apply the four [Hands] seat offsets between the tracked (grip) pose and the
    /// HandRig root: GripPitchOffsetDegrees (rotation) plus HandLateralOffset (X),
    /// HandVerticalOffset (Y) and HandForwardOffset (Z) — all device-space, applied to
    /// the HandRoot origin so wrist, palm, grab anchor and index-knuckle laser origin
    /// translate/rotate together (relative rig geometry, and the per-hand mirroring in
    /// <see cref="HandVisuals"/>, are unchanged). Pitch semantics: NEGATIVE = fingertips
    /// tilt DOWN from the grip-pose forward; Unity pitches down with POSITIVE X Euler,
    /// hence the sign flip. Position semantics: X = lateral, Y = up (POSITIVE raises the
    /// hand), Z = forward toward the fingertips (NEGATIVE sits the wrist behind the grip
    /// origin). Setting position 0/0/0 and pitch 0 places the hand EXACTLY at the tracked
    /// grip pose. The OpenXR grip pose points up along the controller handle, not where a
    /// relaxed hand points (hardware tests #4/#24/#27); reference: LCVR (DaXcess/LCVR,
    /// Source/Player/VRPlayer.cs) rotates its controller-relative interact/ray origins by
    /// Quaternion.Euler(80, 0, 0) — ~80° down from the tracked pose. All four re-checked
    /// per frame (float compare only) so the values are live-tunable.
    /// </summary>
    private void SyncVisualOffset()
    {
        // Per-STYLE tunables (scale + seat trims) ride on top of the four global seat
        // controls, keyed by the style the visuals were ACTUALLY built with
        // (Rig.VisualStyle — an old bundle may have degraded Plate/Arcane to Glove).
        // Before the rig exists (first call from Initialize) fall back to the
        // configured style; Build applies the initial scale itself either way.
        int style = (int)(Rig != null ? Rig.VisualStyle : HandVisuals.LocalStyle());
        float pitch = Plugin.GripPitchOffsetDegrees.Value + StyleTrim(Plugin.HandStylePitchTrim, style);
        float lateral = Plugin.HandLateralOffset.Value + StyleTrim(Plugin.HandStyleLateralTrim, style);
        float vertical = Plugin.HandVerticalOffset.Value + StyleTrim(Plugin.HandStyleVerticalTrim, style);
        float forward = Plugin.HandForwardOffset.Value + StyleTrim(Plugin.HandStyleForwardTrim, style);
        float scale = HandVisuals.StyleScale((HandStyle)style);
        if (pitch == _appliedGripPitch
            && lateral == _appliedLateralOffset
            && vertical == _appliedVerticalOffset
            && forward == _appliedForwardOffset
            && scale == _appliedStyleScale)
            return;
        _appliedGripPitch = pitch;
        _appliedLateralOffset = lateral;
        _appliedVerticalOffset = vertical;
        _appliedForwardOffset = forward;
        _handRoot.localPosition = new Vector3(lateral, vertical, forward);
        _handRoot.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
        if (Rig != null)
        {
            // Live scale re-apply (stepper/config edit): scales the hand subtree and
            // re-normalizes the attachment sockets so held objects/fan/HUD keep size.
            HandVisuals.ApplyStyleScale(_handRoot, Rig, scale);
            _appliedStyleScale = scale;
        }
    }

    /// <summary>Safe per-style trim read (0 until the config array is bound).</summary>
    private static float StyleTrim(BepInEx.Configuration.ConfigEntry<float>[]? entries, int style)
    {
        try
        {
            return entries != null ? entries[style].Value : 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private void Update()
    {
        WorldScale = transform.lossyScale.x;
        SyncVisualOffset();

        if (_simulated)
            ReadSimulated();
        else
            ReadDevice();

        UpdateVelocity();
        UpdatePoseClassification();
        UpdateCurlTargets();
        _curler.Tick(Time.deltaTime);

        // Interactors see the fresh pose; deterministic order (RayUgui consumes the
        // ray's pick of THIS frame, so it ticks right after the ray). RayGrab ticks AFTER
        // RayUgui so a UI click on a window wins over dragging its bar, and BEFORE the
        // Grabber so a laser-carry it starts (Held set via ForceGrab) suppresses any
        // proximity trigger-grab that frame (Grabber early-outs on Held != null).
        Poke.Tick();
        Ray.Tick();
        RayUgui.Tick();
        RayGrab.Tick();
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

        // OpenXR aim pose (see PointerPositionUsage doc). Delivered in the same
        // tracking space as the grip pose — convert through our parent (the hands
        // root under the rig) into world space for ray consumers.
        bool hadPointer = HasPointerPose;
        if (_device.TryGetFeatureValue(PointerPositionUsage, out Vector3 pointerPos)
            && _device.TryGetFeatureValue(PointerRotationUsage, out Quaternion pointerRot))
        {
            Transform space = transform.parent != null ? transform.parent : transform;
            PointerOrigin = space.TransformPoint(pointerPos);
            PointerDirection = (space.rotation * (pointerRot * Vector3.forward)).normalized;
            HasPointerPose = true;
        }
        else
        {
            HasPointerPose = false;
        }
        if (HasPointerPose != hadPointer)
            VRLog.Info("Hands", HasPointerPose
                ? $"{Side} controller delivers the OpenXR aim pose (PointerPosition/PointerRotation) — laser uses it."
                : $"{Side} controller lost the aim pose — laser falls back to the grip-pose hand frame.");

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

        // Stick-push button (primary2DAxisClick) — edge-tracked like the face buttons.
        bool prevStickClick = ThumbstickClick;
        _device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool stickClick);
        ThumbstickClick = stickClick;
        ThumbstickClickDown = stickClick && !prevStickClick;
        ThumbstickClickUp = !stickClick && prevStickClick;
    }

    private void ReadSimulated()
    {
        SetTracked(true);
        HasPointerPose = false; // sim rays use the hand frame
        ApplyAnalog(_simTrigger, _simGrip);
        ThumbTouch = _simGrip > 0.5f;
        Thumbstick = Vector2.zero;
        PrimaryDown = SecondaryDown = false;
        ThumbstickClick = false;
        ThumbstickClickDown = ThumbstickClickUp = false;
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
        ThumbstickClick = false;
        ThumbstickClickDown = ThumbstickClickUp = false;
        HasPointerPose = false;
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
            RayUgui.Cancel();
            RayGrab.Cancel();
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
        if (HandsConfig.TestFistActive)
        {
            // Debug override ([Hands] TestFist): the maximum fist the rig can produce,
            // independent of controller input — separates input loss from rig loss.
            for (int f = 0; f < 5; f++)
                _curler.SetTarget((Finger)f, 1f);
        }
        else
        {
            // Raw analog → curl REMAP ([Hands] CurlInputFullAt): Quest 3 via Virtual
            // Desktop plateaus the analog grip below 1.0 at a comfortable full squeeze,
            // so the unremapped value never reached full curl — "mostly no fist" on
            // every style. Only the curl targets are remapped; TriggerValue/GripValue
            // stay raw for the 0.75/0.55 press hysteresis and pose classification.
            float indexCurl = Pose == HandPose.Point ? 0f : RemapCurlInput(TriggerValue);
            float gripCurl = RemapCurlInput(GripValue);
            _curler.SetTarget(Finger.Index, indexCurl);
            _curler.SetTarget(Finger.Middle, gripCurl);
            _curler.SetTarget(Finger.Ring, gripCurl);
            _curler.SetTarget(Finger.Pinky, gripCurl);
            // Thumb: capacitive touch alone can only reach 0.65 (resting on the stick
            // is not a fist) — but a FIST must close the thumb fully; the old constant
            // cap left it a third open on every closed hand.
            float thumbCurl = GripPressed && TriggerPressed ? 1f : (ThumbTouch ? 0.65f : 0.15f);
            _curler.SetTarget(Finger.Thumb, thumbCurl);
        }

        TickFistDiagnostics();
    }

    /// <summary>Curl remap: raw analog ≥ [Hands] CurlInputFullAt counts as full curl.</summary>
    private static float RemapCurlInput(float raw) =>
        Mathf.Clamp01(raw / HandsConfig.CurlInputFullAtSafe());

    // ---- fist diagnostics ----------------------------------------------------------------

    /// <summary>Frames to wait after reaching a near-full curl target before sampling the
    /// applied joint angles (lets the exponential smoothing converge, ~0.3 s at 72 Hz).</summary>
    private const int FistLogSettleFrames = 24;

    private float _peakGrip;
    private float _peakTrigger;
    private bool _fistLogLatched;
    private int _fistLogCountdown = -1;

    /// <summary>
    /// Edge-triggered, compact fist evidence for hardware logs — proves WHERE curl
    /// range is lost without a debugger:
    ///  - at (near-)full grip (remapped curl ≥ 0.95, once per squeeze, sampled after
    ///    smoothing settles): raw inputs, per-finger curls, the ACTUALLY-applied
    ///    per-joint angles, and the external-overwrite drift detector;
    ///  - on grip release: the PEAK raw grip/trigger of the squeeze — shows whether
    ///    the controller ever delivers 1.0 (and what CurlInputFullAt should be).
    /// </summary>
    private void TickFistDiagnostics()
    {
        _peakGrip = Mathf.Max(_peakGrip, GripValue);
        _peakTrigger = Mathf.Max(_peakTrigger, TriggerValue);

        if (GripDown)
        {
            _peakGrip = GripValue;
            _peakTrigger = TriggerValue;
        }
        else if (GripUp)
        {
            VRLog.Info("Hands", $"{Side} squeeze released: peak raw grip={_peakGrip:0.00} " +
                                $"trigger={_peakTrigger:0.00} (remap full at {HandsConfig.CurlInputFullAtSafe():0.00} " +
                                $"→ peak curls {RemapCurlInput(_peakGrip):0.00}/{RemapCurlInput(_peakTrigger):0.00}).");
        }

        bool nearFull = HandsConfig.TestFistActive || RemapCurlInput(GripValue) >= 0.95f;
        if (!nearFull)
        {
            _fistLogCountdown = -1;
            if (RemapCurlInput(GripValue) < 0.5f)
                _fistLogLatched = false; // re-arm for the next squeeze
            return;
        }

        if (_fistLogLatched)
            return;
        if (_fistLogCountdown < 0)
            _fistLogCountdown = FistLogSettleFrames;
        if (--_fistLogCountdown > 0)
            return;

        _fistLogLatched = true;
        _fistLogCountdown = -1;
        float drift = _curler.ConsumeExternalDrift();
        VRLog.Info("Hands", $"FIST {Side} (style {Rig.VisualStyle}, testFist={HandsConfig.TestFistActive}): " +
                            $"raw trig={TriggerValue:0.00} grip={GripValue:0.00} thumbTouch={ThumbTouch} pose={Pose} | " +
                            $"curl T/I/M/R/P={GetCurl(Finger.Thumb):0.00}/{GetCurl(Finger.Index):0.00}/" +
                            $"{GetCurl(Finger.Middle):0.00}/{GetCurl(Finger.Ring):0.00}/{GetCurl(Finger.Pinky):0.00} | " +
                            $"applied° thumb={FormatAngles(Finger.Thumb)} index={FormatAngles(Finger.Index)} " +
                            $"middle={FormatAngles(Finger.Middle)} ring={FormatAngles(Finger.Ring)} " +
                            $"pinky={FormatAngles(Finger.Pinky)} | externalDrift={drift:0.0}° " +
                            $"{(drift > 1f ? "(!! another writer moves the finger bones after us)" : "(no other writer)")}");
    }

    /// <summary>"root/mid/tip" degrees the curler actually applied to a finger.</summary>
    private string FormatAngles(Finger finger)
    {
        Vector3 a = _curler.GetAppliedAngles(finger);
        return $"{a.x:0}/{a.y:0}/{a.z:0}";
    }
}
