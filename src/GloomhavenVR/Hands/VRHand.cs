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

    /// <summary>All curls low — flat open hand.
    ///
    /// <para>KEEP — assigned by <c>UpdatePoseClassification</c> but never compared against
    /// (refactor Batch D, verified at HEAD; <c>Point</c> and <c>Fist</c> ARE read). The
    /// ASSIGNMENT is what makes the classification TOTAL: remove it and the classifier
    /// silently reports <c>Idle</c> for a flat open hand, which is a behaviour change wearing
    /// a cleanup costume. The member itself is frozen Phase-2 API.</para></summary>
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

    /// <summary>
    /// TRIGGER DROPOUT fallbacks (hardware evidence, LogOutput "FIST Right ... raw
    /// trig=0.00 grip=1.00" + "peak raw grip=1.00 trigger=0.00"): Quest 3 via Virtual
    /// Desktop's VDXR runtime can deliver a permanently-zero ANALOG trigger
    /// (CommonUsages.trigger) while the digital states still fire. When the analog
    /// reads 0 we probe, in order: the alternate "TriggerValue" float some runtimes
    /// tag instead of "Trigger", the digital CommonUsages.triggerButton, and the
    /// OpenXR "Select" usage (the runtime's authored click) — first non-zero wins
    /// (booleans map to 1.0). Logged once per device acquisition so hardware logs
    /// show WHICH source actually feeds the index finger.
    /// </summary>
    private static readonly InputFeatureUsage<float> TriggerValueAltUsage = new("TriggerValue");
    private static readonly InputFeatureUsage<bool> SelectUsage = new("Select");

    /// <summary>
    /// INDEX CAPACITIVE TOUCH (pointing fix): the grip-fist override closes ALL fingers
    /// at (near-)full grip, which killed index pointing on runtimes whose analog trigger
    /// is dead (VDXR: raw trig=0.00 through a full squeeze) — gripping the controller
    /// always curled the index. The physically honest signal for "is the index ON the
    /// trigger" is the trigger's capacitive touch pad. Sources probed per frame:
    ///  - "TriggerTouch": the usage the OpenXR Oculus Touch interaction profile tags on
    ///    '/input/trigger/touch' (verified in RuntimeDeps Unity.XR.OpenXR 1.10.0,
    ///    OculusTouchControllerProfile.RegisterActionMapsWithRuntime, ActionConfig
    ///    "triggerTouched" usages = { "TriggerTouch" }; Quest Pro profile tags the same);
    ///  - "IndexTouch": the legacy Oculus XR plugin name for the same pad.
    /// Support is LATCHED the first time either usage is delivered
    /// (TryGetFeatureValue returns true) and the winning source is logged once —
    /// hardware logs show "index touch source: …". A live analog trigger (&gt;5 %) also
    /// counts as touched (you cannot pull an untouched trigger), which doubles as the
    /// documented fallback when NO touch usage exists: the index then follows the
    /// trigger fallback chain ONLY and the grip never closes it (pointing preserved).
    /// </summary>
    private static readonly InputFeatureUsage<bool> TriggerTouchUsage = new("TriggerTouch");
    private static readonly InputFeatureUsage<bool> IndexTouchUsage = new("IndexTouch");

    private InputDevice _device;
    private FingerCurler _curler = null!;
    private Transform _handRoot = null!;
    private float _appliedGripPitch = float.NaN;

    /// <summary>Roll already on the hand root, ALREADY MIRRORED for this side (NaN = never applied).</summary>
    private float _appliedGripRoll = float.NaN;

    /// <summary>Yaw already on the hand root, ALREADY MIRRORED for this side (NaN = never applied).</summary>
    private float _appliedGripYaw = float.NaN;

    /// <summary>Spread already folded into the hand root's X, ALREADY MIRRORED (NaN = never applied).</summary>
    private float _appliedSpread = float.NaN;
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

    /// <summary>
    /// How long a hand keeps its last pose after the device stops reporting one, before it is
    /// declared untracked.
    ///
    /// <para>WHY THERE IS A GRACE AT ALL (hardware evidence, multiplayer test 2026-08-02). The log
    /// carries 163 <c>"Right ray OFF - no pose (tracking lost)"</c> events against 4 on the left: the
    /// dominant controller drops its pose for a frame or two over and over, and every single drop
    /// takes the laser away and — through <see cref="SetTracked"/> — CANCELS the poke, the UI ray,
    /// the ray-grab and every active grab. That is what "the VR features suddenly disconnect" looks
    /// like from the inside: not a session ending, but the working hand being reset a hundred and
    /// sixty times.</para>
    ///
    /// <para>0.35 s is chosen to be longer than any plausible single-frame or re-enumeration gap
    /// (4 frames at 90 Hz is 44 ms) and far shorter than a real disconnect. The controller dropouts
    /// the same log shows at the DEVICE level last tens of seconds — those still end in
    /// <c>IsTracked = false</c> a third of a second in, exactly as before. Nothing is extrapolated
    /// while the grace runs: the hand simply keeps the pose it last had, which is where the player
    /// last saw it.</para>
    /// </summary>
    private const float PoseGraceSeconds = 0.35f;

    /// <summary>Unscaled time the device pose went away, or -1 while it is being delivered.</summary>
    private float _poseLostAt = -1f;

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

    /// <summary>
    /// Capacitive INDEX finger detection: trigger touch pad (see <see cref="TriggerTouchUsage"/>)
    /// or a live analog trigger. False ⇒ the index is off the trigger ⇒ pointing.
    /// </summary>
    public bool IndexTouch { get; private set; }

    /// <summary>True once this device delivered a capacitive trigger-touch usage.</summary>
    public bool IndexTouchSupported => _indexTouchSupported;

    private bool _indexTouchSupported;
    private bool _indexTouchSourceLogged;
    private bool _indexTouchSupportLogged;

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
        _curler = new FingerCurler(Rig, side);

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

    /// <summary>
    /// Hand teardown, ONE ISOLATED STEP PER INTERACTOR. This runs inside Unity's end-of-frame
    /// destroy wave whenever the rig goes down — a scenario restart, a hand-style switch, a scene
    /// swap — and every cancel below reaches into objects (hover targets, held grabbables, laser
    /// visuals) that the SAME wave may already have destroyed. Unguarded, one throw skipped the
    /// remaining cancels and printed a single anonymous "NullReferenceException" with no owner:
    /// the game turns stack traces off process-wide (see Core/ExceptionTraces), so a bare NRE in
    /// Player.log names nothing. Under TickGuard each step is isolated and the throw is logged
    /// WITH its stack under its own name. Order is unchanged.
    /// </summary>
    private void OnDestroy()
    {
        Core.TickGuard.Run("Hands.Teardown.RayVisuals", () => Ray?.DestroyVisuals(), "Hands");
        Core.TickGuard.Run("Hands.Teardown.RayUgui", () => RayUgui?.Cancel(), "Hands");
        Core.TickGuard.Run("Hands.Teardown.RayGrab", () => RayGrab?.Cancel(), "Hands");
        Core.TickGuard.Run("Hands.Teardown.Poke", () => Poke?.CancelAll(), "Hands");
        Core.TickGuard.Run("Hands.Teardown.Grabber", () => Grabber?.CancelAll(), "Hands");
    }

    // ---- per-frame -------------------------------------------------------------------------

    /// <summary>
    /// Apply the [Hands] seat controls between the tracked (grip) pose and the HandRig
    /// root: pitch, roll and yaw plus lateral (X), vertical (Y), forward (Z) and spread —
    /// PER-STYLE absolute values since the per-style rework
    /// (<see cref="HandsConfig.StyleSeatPitch"/> etc.; the shipped defaults are the
    /// hardware-measured tables in Defaults.Hands.cs) — all device-space, applied to
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
    /// Quaternion.Euler(80, 0, 0) — ~80° down from the tracked pose. Every value (plus the
    /// style scale) is re-checked per frame, float compare only, so all of them live-tune.
    /// </summary>
    private void SyncVisualOffset()
    {
        // Per-STYLE tunables (scale + the ABSOLUTE per-style seat controls, which replaced
        // the old shared globals + additive trims), keyed by
        // the style the visuals were ACTUALLY built with (Rig.VisualStyle — an old
        // bundle may have degraded Plate/Arcane to Glove). Before the rig exists (first
        // call from Initialize) fall back to the configured style; Build applies the
        // initial scale itself either way. Re-read every frame, so value edits AND
        // style switches both live-apply here.
        int style = (int)(Rig != null ? Rig.VisualStyle : HandVisuals.LocalStyle());
        float pitch = HandsConfig.SeatPitchSafe(style);
        float lateral = HandsConfig.SeatLateralSafe(style);
        float vertical = HandsConfig.SeatVerticalSafe(style);
        float forward = HandsConfig.SeatForwardSafe(style);

        // ROLL IS MIRRORED, PITCH IS NOT — and that asymmetry is the reason it is its own control.
        // Both controllers report local axes of the same handedness, so a single value applied
        // unchanged would twist the two hands the SAME way in world terms: one palm rolling inward
        // while the other rolls outward. Negating it for the left hand makes a positive number turn
        // both palms the same way relative to their own side of the body, which is what "roll the
        // hands" means to the person wearing them. Pitch needs no such flip: fingers-down is
        // fingers-down on both sides.
        float mirror = Side == HandSide.Left ? -1f : 1f;
        float roll = HandsConfig.SeatRollSafe(style) * mirror;
        float yaw = HandsConfig.SeatYawSafe(style) * mirror;

        // SPREAD is a MIRRORED lateral offset and therefore a second control, not a replacement:
        // `lateral` shifts both hands the same way in device space (the pair moves together, right
        // when the whole pair sits off-centre on the controllers), and no value of it can move the
        // hands APART. Spread adds the mirrored term, so positive takes the left hand left and the
        // right hand right.
        float spread = HandsConfig.SeatSpreadSafe(style) * mirror;

        float scale = HandVisuals.StyleScale((HandStyle)style);
        if (pitch == _appliedGripPitch
            && lateral == _appliedLateralOffset
            && vertical == _appliedVerticalOffset
            && forward == _appliedForwardOffset
            && roll == _appliedGripRoll
            && yaw == _appliedGripYaw
            && spread == _appliedSpread
            && scale == _appliedStyleScale)
            return;
        _appliedGripPitch = pitch;
        _appliedLateralOffset = lateral;
        _appliedVerticalOffset = vertical;
        _appliedForwardOffset = forward;
        _appliedGripRoll = roll;
        _appliedGripYaw = yaw;
        _appliedSpread = spread;

        // EVERYTHING IN THE HAND COMES ALONG, by construction rather than by anyone remembering to
        // update it: the rig's anchors and sockets are descendants of _handRoot and a grabbed
        // figure or held card is PARENTED to one of them, so held-object offsets stay relative to
        // the hand. The same parenting is why other players see all of it — the pose on the wire
        // is sampled from Rig.Root, which IS this transform.
        _handRoot.localPosition = new Vector3(lateral + spread, vertical, forward);
        _handRoot.localRotation = Quaternion.Euler(-pitch, yaw, roll);
        if (Rig != null)
        {
            // Live scale re-apply (stepper/config edit): scales the hand subtree and
            // re-normalizes the attachment sockets so held objects/fan/HUD keep size.
            HandVisuals.ApplyStyleScale(_handRoot, Rig, scale);
            _appliedStyleScale = scale;
        }
    }

    /// <summary>
    /// Perf attribution (2026-07 perf pass): this runs twice per frame (one instance per hand)
    /// directly on the tracking path, so if a head/hand-motion spike is ours it is one of the
    /// two places it can live.
    /// </summary>
    private void Update()
    {
        using (Core.PerfMonitor.Scope("Hands.VRHand"))
            UpdateBody();
    }

    private void UpdateBody()
    {
        // Falsifier sample, deliberately the FIRST thing in the frame: what the beam still owned
        // when this frame started. Read before anything ticks, so the grip-suppression line below
        // can name what was actually dropped instead of what it expected to drop. Four field
        // reads; not part of any locked ordering (it drives a log line, nothing consumes it).
        SampleLaserFlight();

        // FRAME-ORDER VRHand.UpdateBody.pose [ReadSimulated, ReadDevice, UpdateVelocity, UpdatePoseClassification, UpdateCurlTargets, _curler.Tick, Poke.Tick]
        //   Every later step consumes the step before it: velocity is differentiated from the
        //   pose read THIS frame, the pose classifier reads that velocity, the curl targets read
        //   the classification, and the interactors must see all of it settled. Machine-checked
        //   against .planning/refactor/FRAME-ORDER.lock — reordering is Tier 3.
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

        // FRAME-ORDER VRHand.UpdateBody.interactors [Poke, Ray, RayUgui, RayGrab, Grabber, PalmGate]
        // Interactors see the fresh pose; deterministic order (RayUgui consumes the
        // ray's pick of THIS frame, so it ticks right after the ray). RayGrab ticks AFTER
        // RayUgui so a UI click on a window wins over dragging its bar, and BEFORE the
        // Grabber so a laser-carry it starts (Held set via ForceGrab) suppresses any
        // proximity trigger-grab that frame (Grabber early-outs on Held != null).
        //   Every adjacency above is an arbitration decision — INVARIANTS §6. Reordering is
        //   Tier 3, and is invisible to refactor-guard.sh, which is why it is locked.
        Poke.Tick();
        Ray.Tick();
        RayUgui.Tick();
        RayGrab.Tick();
        Grabber.Tick();
        PalmGate.Tick();

        // AFTER every interactor has run, so the line reports the state the frame ENDED in —
        // beam drawn or not, press cancelled or not. Placed here on purpose: run before the
        // interactors and it could only report an intention.
        TickGripLaserFalsifier();
    }

    // ---- grip-held laser suppression: falsifier -----------------------------------------

    /// <summary>
    /// One Info line per transition, per hand, when this hand's laser stands down because the
    /// GRIP is held (RayInteractor.GripSuppressed — the physical fingertip-press posture) and
    /// one more when it comes back.
    ///
    /// <para>It is written to report the OUTCOME. Every value in it is read back AFTER the
    /// interactors ran (<c>Ray.Active</c>, <c>Ray.BeamDrawn</c>, the drivers' live in-flight
    /// fields), and the "cancelled" list is the difference between the state sampled at the
    /// TOP of this frame (<see cref="SampleLaserFlight"/>) and the state now — so a line that
    /// says a press was cancelled is a line that watched the field go false. If the beam
    /// somehow stays drawn while the grip is held, this line says <c>beam drawn=True</c> and
    /// the fix is falsified on the spot.</para>
    ///
    /// <para>Rate limit: at most one Info line per hand per
    /// <see cref="GripFalsifierInfoIntervalSeconds"/>. Transitions inside that window are still
    /// printed (at Debug, so nothing is lost from a Trace-level hardware log) and counted, and
    /// the count is carried on the next Info line — a throttle that silently ate transitions
    /// would make this instrument lie about how often the beam moved.</para>
    /// </summary>
    private const float GripFalsifierInfoIntervalSeconds = 0.25f;

    private bool _gripLaserSuppressed;
    private float _gripLaserSuppressedAt;
    private float _nextGripFalsifierInfoAt;
    private int _gripFalsifierCollapsed;

    // In-flight snapshot taken at the top of the frame (see SampleLaserFlight).
    private bool _preUguiPress;
    private bool _preUguiHover;
    private bool _preBarHover;
    private bool _preFreshUiHit;

    private void SampleLaserFlight()
    {
        // Defensive: Update can run on the frame the hand is built, before Initialize has
        // created the interactors (they are declared `null!`). An unguarded read here would
        // throw every frame and starve input for the session.
        _preUguiPress = RayUgui != null && RayUgui.IsPressing;
        _preUguiHover = RayUgui != null && RayUgui.IsHovering;
        _preBarHover = RayGrab != null && RayGrab.IsHovering;
        _preFreshUiHit = Ray != null && Ray.HasFreshUiHit;
    }

    private void TickGripLaserFalsifier()
    {
        try
        {
            bool suppressed = Ray != null && Ray.GripSuppressed;
            if (suppressed == _gripLaserSuppressed)
                return;
            _gripLaserSuppressed = suppressed;

            string message;
            if (suppressed)
            {
                _gripLaserSuppressedAt = Time.unscaledTime;
                bool uguiPress = RayUgui.IsPressing;
                bool uguiHover = RayUgui.IsHovering;
                bool barHover = RayGrab.IsHovering;
                bool freshUiHit = Ray!.HasFreshUiHit;

                string cancelled = DescribeCancelled(
                    _preUguiPress && !uguiPress,
                    _preUguiHover && !uguiHover,
                    _preBarHover && !barHover,
                    _preFreshUiHit && !freshUiHit);

                message =
                    $"LASER SUPPRESSED ({Side}) — grip HELD (grip={GripValue:0.00}, pressed={GripPressed}); " +
                    $"read back after the interactors ran: ray Active={Ray.Active}, beam drawn={Ray.BeamDrawn} " +
                    $"[{Ray.StateReason}]; in flight at the top of this frame: uGUI press={_preUguiPress}, " +
                    $"uGUI hover={_preUguiHover}, panel-bar hover={_preBarHover}, fresh-UI-hit={_preFreshUiHit}; " +
                    $"now: uGUI press={uguiPress}, uGUI hover={uguiHover}, panel-bar hover={barHover}, " +
                    $"fresh-UI-hit={freshUiHit} => cancelled {cancelled}; carried object: " +
                    $"{DescribeHeld()} (NOT dropped — the Grabber owns it and its hold button is unchanged).";
            }
            else
            {
                message =
                    $"LASER RESTORED ({Side}) — grip RELEASED (grip={GripValue:0.00}, pressed={GripPressed}) " +
                    $"after {Time.unscaledTime - _gripLaserSuppressedAt:0.00}s; read back after the interactors " +
                    $"ran: ray Active={Ray!.Active}, beam drawn={Ray.BeamDrawn} [{Ray.StateReason}]; " +
                    $"carried object: {DescribeHeld()}.";
            }

            float now = Time.unscaledTime;
            if (now < _nextGripFalsifierInfoAt)
            {
                _gripFalsifierCollapsed++;
                VRLog.Debug("Hands", message + $" [throttled: {_gripFalsifierCollapsed} transition(s) " +
                                               $"within {GripFalsifierInfoIntervalSeconds:0.00}s]");
                return;
            }
            _nextGripFalsifierInfoAt = now + GripFalsifierInfoIntervalSeconds;
            if (_gripFalsifierCollapsed > 0)
                message += $" [+{_gripFalsifierCollapsed} earlier transition(s) were logged at Debug by the throttle]";
            _gripFalsifierCollapsed = 0;
            VRLog.Info("Hands", message);
        }
        catch (Exception ex)
        {
            // A log line may never cost the session its input (the unguarded-Update failure mode).
            VRLog.Error("Hands", $"grip laser falsifier threw: {ex}");
        }
    }

    private static string DescribeCancelled(bool uguiPress, bool uguiHover, bool barHover, bool freshUiHit)
    {
        if (!uguiPress && !uguiHover && !barHover && !freshUiHit)
            return "nothing (the beam was idle)";
        string list = "";
        if (uguiPress) list += "uGUI press/drag";
        if (uguiHover) list += (list.Length > 0 ? ", " : "") + "uGUI hover";
        if (barHover) list += (list.Length > 0 ? ", " : "") + "panel-bar hover";
        if (freshUiHit) list += (list.Length > 0 ? ", " : "") + "beam UI clamp";
        return "[" + list + "]";
    }

    private string DescribeHeld()
    {
        IGrabbable? held = Grabber != null ? Grabber.Held : null;
        return held switch
        {
            null => "none",
            UnityEngine.Object o => o != null ? $"'{o.name}'" : "'<destroyed>'",
            _ => held.GetType().Name,
        };
    }

    private void ReadDevice()
    {
        // RE-ACQUISITION IS UNCONDITIONAL AND IMMEDIATE. The handle is re-fetched from the XR node
        // the very frame it goes invalid, so the mod never sits on a stale device across a runtime
        // re-enumeration — the hardware log's device-level dropouts (devices=1, L/R invalid, for
        // tens of seconds at a time while the input subsystem stays "running") are the runtime
        // losing the controllers, not us failing to ask again.
        if (!_device.isValid)
        {
            _device = InputDevices.GetDeviceAtXRNode(Side == HandSide.Left ? XRNode.LeftHand : XRNode.RightHand);
            if (!_device.isValid)
            {
                if (HoldPoseThroughGap("the device handle went invalid"))
                    return;
                SetTracked(false);
                ClearInput();
                return;
            }
        }

        if (!HasUsablePose())
        {
            if (HoldPoseThroughGap("the device stopped reporting a pose"))
                return; // keep the last pose and the last input for the rest of the grace
            SetTracked(false);
            ClearInput();
            return;
        }

        _poseLostAt = -1f;
        SetTracked(true);

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
        if (trigger <= 0.001f)
            trigger = ReadTriggerFallbacks();
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

        // Index capacitive touch (see TriggerTouchUsage doc). A live analog trigger
        // always implies touch; the capacitive pad is what distinguishes "finger off
        // the trigger" (pointing) from "finger resting on a dead-analog trigger".
        bool indexTouch = TriggerValue > 0.05f;
        string touchSource = "analog trigger only (no capacitive usage delivered)";
        if (_device.TryGetFeatureValue(TriggerTouchUsage, out bool trigTouch))
        {
            indexTouch |= trigTouch;
            _indexTouchSupported = true;
            touchSource = "TriggerTouch (OpenXR '/input/trigger/touch')";
        }
        else if (_device.TryGetFeatureValue(IndexTouchUsage, out bool idxTouch))
        {
            indexTouch |= idxTouch;
            _indexTouchSupported = true;
            touchSource = "IndexTouch (legacy Oculus usage)";
        }
        IndexTouch = indexTouch;
        // Log the source once; if a capacitive usage only starts arriving later
        // (runtimes can withhold it until the controller wakes), log the upgrade once.
        if (_indexTouchSupported && !_indexTouchSupportLogged)
        {
            _indexTouchSupportLogged = _indexTouchSourceLogged = true;
            VRLog.Info("Hands", $"{Side} index touch source: {touchSource} — trigger untouched keeps " +
                                "the index straight (pointing) even at full grip; touched ⇒ index joins the fist.");
        }
        else if (!_indexTouchSourceLogged)
        {
            _indexTouchSourceLogged = true;
            VRLog.Info("Hands", $"{Side} index touch source: {touchSource} — index follows the trigger " +
                                "fallback chain only; grip never closes the index (pointing preserved).");
        }

        _device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
        Thumbstick = stick;

        // Stick-push button (primary2DAxisClick) — edge-tracked like the face buttons.
        bool prevStickClick = ThumbstickClick;
        _device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool stickClick);
        ThumbstickClick = stickClick;
        ThumbstickClickDown = stickClick && !prevStickClick;
        ThumbstickClickUp = !stickClick && prevStickClick;
    }

    // Which trigger fallback source is currently feeding TriggerValue (log dedup).
    private enum TriggerSource { Analog, AltValue, Button, Select }
    private TriggerSource _triggerSource = TriggerSource.Analog;

    /// <summary>See <see cref="TriggerValueAltUsage"/>: probe alternate trigger sources when the
    /// analog CommonUsages.trigger reads 0 (VDXR delivers 0.00 through a full squeeze).</summary>
    private float ReadTriggerFallbacks()
    {
        TriggerSource source = TriggerSource.Analog;
        float value = 0f;
        if (_device.TryGetFeatureValue(TriggerValueAltUsage, out float alt) && alt > 0.001f)
        {
            value = alt;
            source = TriggerSource.AltValue;
        }
        else if (_device.TryGetFeatureValue(CommonUsages.triggerButton, out bool button) && button)
        {
            value = 1f;
            source = TriggerSource.Button;
        }
        else if (_device.TryGetFeatureValue(SelectUsage, out bool select) && select)
        {
            value = 1f;
            source = TriggerSource.Select;
        }
        if (source != TriggerSource.Analog && source != _triggerSource)
        {
            _triggerSource = source;
            VRLog.Info("Hands", $"{Side} analog trigger reads 0 while '{source}' is active — " +
                                $"index finger now driven from the {source} fallback (VDXR trigger dropout).");
        }
        return value;
    }

    private void ReadSimulated()
    {
        _poseLostAt = -1f; // the dev harness always has a pose; never let a stale gap linger
        SetTracked(true);
        HasPointerPose = false; // sim rays use the hand frame
        ApplyAnalog(_simTrigger, _simGrip);
        ThumbTouch = _simGrip > 0.5f;
        IndexTouch = _simTrigger > 0.05f;   // sim: finger on trigger ⇔ any pull
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
        IndexTouch = false;
        Thumbstick = Vector2.zero;
        ThumbstickClick = false;
        ThumbstickClickDown = ThumbstickClickUp = false;
        HasPointerPose = false;
    }

    /// <summary>
    /// Does the device deliver a pose we can drive the hand from this frame?
    ///
    /// <para>NOT <c>isTracked</c> ALONE, and that is the point. <c>CommonUsages.isTracked</c> is the
    /// runtime's summary judgement and OpenXR runtimes drop it eagerly — a Quest controller lowered
    /// out of the headset's camera view reports it false while still delivering a perfectly good
    /// pose. <c>trackingState</c> is the per-component truth, so when it says BOTH position and
    /// rotation are valid the pose is usable no matter what the summary flag claims. The old
    /// <c>isTracked</c> test stays as the fallback for a runtime that does not fill trackingState,
    /// so this can only ever ADD frames in which the hand keeps working, never remove one.</para>
    /// </summary>
    private bool HasUsablePose()
    {
        const InputTrackingState posed = InputTrackingState.Position | InputTrackingState.Rotation;
        if (_device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state)
            && (state & posed) == posed)
            return true;
        return _device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked;
    }

    /// <summary>
    /// Ride out a momentary pose gap: true while <see cref="PoseGraceSeconds"/> has not elapsed, in
    /// which case the caller leaves the transform and the input values exactly where they are.
    /// Returns false once the gap has lasted long enough to be a real loss.
    /// </summary>
    private bool HoldPoseThroughGap(string reason)
    {
        float now = Time.unscaledTime;
        if (_poseLostAt < 0f)
        {
            _poseLostAt = now;
            VRLog.Debug("Hands", $"{Side} pose gap ({reason}) — holding the last pose for up to " +
                                 $"{PoseGraceSeconds:0.00}s before anything is cancelled.");
        }
        if (now - _poseLostAt < PoseGraceSeconds)
            return true;

        // Only announce the real loss, and only once per gap: IsTracked is about to go false, which
        // is what cancels the grabs and takes the laser away.
        if (IsTracked)
            VRLog.Info("Hands", $"{Side} tracking lost for more than {PoseGraceSeconds:0.00}s " +
                                $"({reason}) — the hand is released; it returns the frame the " +
                                "device delivers a pose again.");
        return false;
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
    /// open palm = nothing held; fist = both held. When the runtime delivers the
    /// trigger's capacitive touch (see <see cref="TriggerTouchUsage"/>), "trigger
    /// released" means "index OFF the trigger" — the analog value alone cannot tell
    /// pointing from a fist on a dead-analog runtime (VDXR trig=0.00 at full squeeze).
    /// </summary>
    private void UpdatePoseClassification()
    {
        bool gripHeld = GripValue > 0.5f;
        bool pointing = _indexTouchSupported ? !IndexTouch : TriggerValue < 0.2f;
        bool triggerHeld = TriggerValue > 0.5f
                           || (_indexTouchSupported && IndexTouch && GripValue > 0.9f);
        if (gripHeld && pointing)
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
            float gripCurl = RemapCurlInput(GripValue);
            float triggerCurl = RemapCurlInput(TriggerValue);
            // INDEX MAPPING: the index is gated by the trigger CAPACITIVE touch, NOT by
            // the grip (root cause of that rule: the TriggerTouchUsage field doc — the
            // superseded grip-fist override closed every finger at gripCurl>=0.9 and so
            // made pointing impossible on a dead-analog-trigger runtime). The rules:
            //   - touch source available:  trigger NOT touched ⇒ index straight
            //     (pointing) even at full grip; touched ⇒ the index joins the fist
            //     (max of the analog trigger and, at near-full grip, the grip curl —
            //     so a full squeeze with the finger resting on a dead trigger still
            //     closes completely). A light 0.45 rest curl while merely touching
            //     keeps the finger visually ON the trigger.
            //   - no touch source on this runtime: the index follows the TRIGGER
            //     fallback chain only (analog → TriggerValue alt → triggerButton →
            //     Select; see ReadTriggerFallbacks) and the grip NEVER closes it —
            //     pointing beats fist-completeness when the runtime cannot tell.
            // The startup "index touch source:" log line shows which mode is live.
            bool gripFist = gripCurl >= 0.9f;
            float indexCurl;
            if (_indexTouchSupported)
            {
                indexCurl = !IndexTouch
                    ? 0f
                    : Mathf.Max(triggerCurl, gripFist ? gripCurl : 0.45f);
            }
            else
            {
                indexCurl = Pose == HandPose.Point ? 0f : triggerCurl;
            }
            _curler.SetTarget(Finger.Index, indexCurl);
            _curler.SetTarget(Finger.Middle, gripCurl);
            _curler.SetTarget(Finger.Ring, gripCurl);
            _curler.SetTarget(Finger.Pinky, gripCurl);
            // Thumb: capacitive touch alone can only reach 0.65 (resting on the stick
            // is not a fist) — but a FIST must close the thumb fully. Fist intent =
            // both pressed, or a near-full grip whose index is also closing (touch or
            // trigger); while POINTING at full grip the thumb stays at the touch cap.
            bool fistIntent = (GripPressed && TriggerPressed)
                              || (gripFist && (_indexTouchSupported ? IndexTouch : triggerCurl > 0.5f));
            float thumbCurl = fistIntent ? 1f : (ThumbTouch ? 0.65f : 0.15f);
            _curler.SetTarget(Finger.Thumb, thumbCurl);

            // THE MODELLED CARD GRIP overrides all five, and ONLY while this hand really holds a
            // card in the in-hand mode ([Cards] InHandHold; Cards.HeldCardGrip owns that answer,
            // and it is false whenever no card is held). Last, so it wins the whole block.
            //
            // WHY AN OVERRIDE IS NOT OPTIONAL HERE. The gesture that selects the mode is the GRIP
            // BUTTON HELD DOWN, and the grip is exactly what drives middle/ring/pinky toward a
            // fist three lines up — so the un-overridden hand would be a closed fist with a card
            // standing out of it. It is also the one mode in which the hand is NOT ghosted (the
            // user's ruling: the ghost belongs to the reading pose), so it is fully opaque and
            // fully in the picture, on this player's screen, in their mirror, and on every peer's.
            //
            // NOTHING NEW GOES ON THE WIRE FOR THIS: curls already ride every rig packet, so a
            // peer's hand closes into the same modelled grip from the numbers written here.
            if (Cards.HeldCardGrip.InHand(Side))
            {
                // The THUMB curl is per hand style — the three assets' thumbs start at different
                // distances from the card and the same fraction of a fixed full-curl angle either
                // leaves the glove's thumb off the card or bends the gauntlets' into a hook. See
                // CardGripPose.ThumbCurlByStyle for the render family that forced that.
                int style = (int)Rig.VisualStyle;
                for (int f = 0; f < 5; f++)
                    _curler.SetTarget((Finger)f, Cards.CardGripPose.CurlFor(f, style));
            }
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
                            $"raw trig={TriggerValue:0.00} grip={GripValue:0.00} thumbTouch={ThumbTouch} " +
                            $"indexTouch={IndexTouch}({(_indexTouchSupported ? "capacitive" : "no-cap-source")}) pose={Pose} | " +
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
