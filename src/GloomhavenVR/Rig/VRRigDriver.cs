using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

/// <summary>
/// Persistent driver that owns the VR camera rig. Each frame it watches for the
/// game's scenario camera (<c>CameraController.s_CameraController.m_Camera</c>,
/// created per scenario scene) and builds/tears down the rig accordingly:
///
/// <code>
/// GloomhavenVR.VRRig (rig root: at orbit focus / menu vantage, yaw-aligned, scaled)
/// └── GloomhavenVR.HeadCamera (OUR camera + TrackedPoseDriver, the ONE stereo renderer)
/// </code>
///
/// OWNED HEAD CAMERA (hardware test #4 root cause, P2 freeze): the rig previously
/// head-tracked the GAME's camera directly (menu 'Main Camera' / scenario camera).
/// That hands the whole pose-application chain to objects the game owns — menu
/// camera animation writers, component toggles that don't trip isActiveAndEnabled,
/// VideoPlayer interactions — any of which can silently stop the HMD updating with
/// zero exceptions and zero health-check triggers (exactly the test-#4 freeze: HMD
/// image static, desktop fine, session FOCUSED, no teardown logged). The rig now
/// creates its OWN camera under its own DontDestroyOnLoad root; the game camera is
/// used ONLY as an anchor reference (vantage/yaw, culling-mask source, far plane)
/// and, like every other game camera, never renders stereo
/// (<see cref="VRCameraPolicy"/> — the tracked-head special-case is gone).
///
/// Diorama scale (ARCHITECTURE §3): the rig root is scaled by WorldScale (game units
/// per real meter) so head motion maps 1 m → WorldScale units and the board reads
/// as a table. Head pose via the game-shipped <see cref="TrackedPoseDriver"/> on OUR
/// camera; implicit XR camera tracking is disabled
/// (<see cref="XRDevice.DisableAutoXRCameraTracking"/>).
///
/// RIG LIFETIME: the health check in <see cref="Update"/> tears down and rebuilds
/// when the ANCHOR camera is destroyed OR disabled on any frame (re-anchors to the
/// next best camera; our own camera keeps rendering meanwhile), when our camera or
/// rig root is destroyed externally, and — on scene load — when a better menu
/// camera appeared. Every teardown/rebuild is logged with its trigger reason.
/// Hands re-home automatically: HandsDriver polls <see cref="RigRoot"/> every frame.
///
/// CAMERA OWNERSHIP: this driver is the pump for <see cref="VRCameraPolicy"/>
/// (game cameras never stereo — swept on scene load, rig rebuild and periodically)
/// and owns the head culling-mask policy: SCENARIO rig = anchor camera's mask OR'd
/// with <see cref="VRLayers.ModLayerMask"/>, never 0; MENU rig = the mod layer ONLY
/// (test #10 — Menu2D shows the world through the FlatScreen RT, never directly).
/// Re-asserted every frame. Nothing on the anchor camera needs restoring — it is never modified
/// (the scenario CameraController freeze flag is the one exception, restored on
/// teardown).
/// </summary>
internal sealed partial class VRRigDriver : MonoBehaviour
{
    /// <summary>
    /// Tracking-space root of the VR rig while it exists, else null. XR device poses
    /// (head, hands) are local to this transform; its lossyScale is the diorama scale.
    /// Phase-2 consumers (Hands) parent their tracked objects under this.
    /// </summary>
    internal static Transform? RigRoot { get; private set; }

    /// <summary>The rig's OWN head-tracked camera while the rig exists (never a game camera).</summary>
    internal static Camera? HeadCamera { get; private set; }

    /// <summary>
    /// Base (unmultiplied) diorama scale resolved at rig build, 0 while no rig. Phase-4
    /// comfort clamps pinch-scale relative to this ([Comfort] ScaleMin/ScaleMax).
    /// </summary>
    internal static float BaseWorldScale { get; private set; }

    /// <summary>The live driver instance (for <see cref="RequestRecenter"/>), if any.</summary>
    internal static VRRigDriver? Instance { get; private set; }

    /// <summary>
    /// Monotonic counter bumped whenever the rig is (re)built or deliberately
    /// recentered (P6). Consumers that cache rig-derived poses (PanelLayout's
    /// world-anchored panel yaw) re-derive on change. Snap turns and world-grab do
    /// NOT bump it — that is the point: panels must stay fixed in the world while
    /// the player merely turns.
    /// </summary>
    internal static int RigPoseVersion { get; private set; }

    /// <summary>Fallback diorama scale when auto-detection has no tile size yet.</summary>
    private const float FallbackWorldScale = 12f;

    // Scale-aware clip planes (test #17): WorldGrab rescales the rig root live
    // (0.1×–12× of base) while the hands — parented under the rig — scale and move
    // with it, so a near plane FIXED at build time in world units swallowed them at
    // max zoom-in (rig scale shrinks → the hands' world-unit distance from the eyes
    // shrinks below the frozen near plane and they clip invisible). TickClipPlanes
    // keeps near = BaseNearMeters × current rig scale (~5 real cm in front of the
    // eyes at ANY zoom), clamped to sane absolute world-unit bounds, and lets the
    // far plane grow with zoom-out so the diorama never pops out of the frustum;
    // the far/near ratio is capped for depth precision.

    /// <summary>Near clip in REAL meters in front of the eyes (× live rig scale).</summary>
    private const float BaseNearMeters = 0.05f;

    /// <summary>Absolute near-plane bounds, world units.</summary>
    private const float MinNearClip = 0.01f;
    private const float MaxNearClip = 0.5f;

    /// <summary>Depth-precision guard: the far plane never exceeds near × this.</summary>
    private const float MaxFarNearRatio = 50000f;

    /// <summary>Real-world size a hex tile should read as on the "table" (meters).</summary>
    private const float TargetHexSizeMeters = 0.15f;

    /// <summary>Stereo-policy sweep cadence (frames) between the event-driven sweeps.</summary>
    private const int SweepIntervalFrames = 30;

    /// <summary>
    /// Spawn-circle re-seat poll cadence (frames). The FFSNet participant registry may not
    /// be populated at the first-pose recenter (LocalStableIndex → (0,1) = solo seat), so we
    /// re-sample on this cadence and re-run Recenter only when the deterministic (idx,total)
    /// actually changes (players finished joining, or someone left). Stable session ⇒ one
    /// change then silent.
    /// </summary>
    private const int CircleReseatIntervalFrames = 30;

    /// <summary>What the current rig is built around (P5: menu rig added, MISSION A.7).</summary>
    private enum RigKind
    {
        None,
        Scenario,
        Menu
    }

    private GameObject? _rigRoot;
    private RigKind _kind;

    /// <summary>The GAME camera the rig is anchored to — reference only, never modified.</summary>
    private Camera? _anchor;

    /// <summary>OUR head camera (child of the rig root).</summary>
    private Camera? _camera;
    private GameObject? _cameraGo;

    private TrackedPoseDriver? _poseDriver;
    private float _buildScale = 1f;   // rig scale the head camera was created at
    private float _baseFarClip = 100f; // anchor-derived far plane at build scale
    private bool _pendingRecenter;
    private int _sweepCountdown;
    private bool _sceneRecheck;
    private string _sceneRecheckName = "";
    private string _rebuildTrigger = "initial";
    private bool _frozeGameCameraControl;

    /// <summary>
    /// One-shot teardown/rebuild request from outside the health check (currently only the
    /// [RenderQuality] RebuildRigOnMsaaChange diagnostic test path). Consumed by the next
    /// <see cref="Update"/>; the standard rebuild flow then re-anchors exactly as after any
    /// other teardown, so the request is safe at any rig state.
    /// </summary>
    private string? _pendingRebuildRequest;

    /// <summary>Request a full rig teardown+rebuild next frame, attributed to <paramref name="reason"/>.</summary>
    internal static void RequestRebuild(string reason)
    {
        if (Instance != null && Instance._pendingRebuildRequest == null)
            Instance._pendingRebuildRequest = reason;
    }

    // Spawn circle (FEATURE D). The scenario rig's FLAT board yaw, frozen at BuildRig — the
    // circle azimuth is applied on top of THIS every recenter so repeated recenters are
    // idempotent (never accumulate). The last (idx,total) a recenter applied, and the poll
    // countdown that re-runs the seat when that pair changes after the registry populates.
    private Quaternion _scenarioBaseYaw = Quaternion.identity;
    private int _lastCircleIdx = -1;
    private int _lastCircleTotal = -1;
    private int _circleReseatCountdown;

    // Per-frame maintenance ticks, each routed through the shared Core.TickGuard so a
    // throw in one (most plausibly MixedReality.Tick) is isolated + attributed instead of
    // aborting the rest and flooding an anonymous per-frame NullReferenceException. The
    // delegates are cached here ONCE (built in Awake) so the guarded loop allocates
    // nothing per frame; the CameraPolicy step reads its bool arg from a field.
    private (string name, System.Action fn)[] _tailSteps = System.Array.Empty<(string, System.Action)>();
    private bool _tickSceneLoaded;

    // Menu rig anchor: where the menu camera stood when we took its vantage — recenter
    // puts the player's head back there (real 1:1 scale, no table math).
    private Vector3 _menuAnchorPos;
    private Quaternion _menuAnchorYaw;

    // Demeo-style WORLD TILT ([Rig] WorldTiltDegrees, scenario rig only): the whole diorama
    // APPEARS tilted toward the player by pitching the TRACKING SPACE (this rig root) around
    // the board center — the player's viewpoint orbits up and over the board; no game-world
    // object ever moves. Maintained by TickWorldTilt (LateUpdate — after every Update-phase
    // rig writer, before rendering); _tiltActive gates the exact-no-op fast path at 0°, and
    // _lastTiltTarget is the magnitude edge detector (-1 = fresh-rig sentinel: the first
    // tick after a rig build adopts the configured tilt instantly instead of tweening).
    private bool _tiltActive;
    private float _lastTiltTarget = -1f;

    // DEMEO-MODEL TILT AXIS (hardware round 5 — replicated from Demeo's decompiled
    // shipping code, decompiled-demeo/Assembly-CSharp/Boardgame/AvatarController.cs
    // StartTilt + Boardgame.CameraControls/*). Rounds 2-4 all aimed the tilt axis at
    // some function of the HEAD (head→pivot line, then view yaw with deadband + ease);
    // the round-4 hardware log proved any head-derived axis nauseating: axisYaw chased
    // the view (72.9°→16.8°→90.5°→…) and every re-aim rotated the whole world about the
    // focus pivot on pure head movement. Demeo NEVER does that. In Demeo:
    //   - the tilt is a rotation about a FIXED LOCAL X AXIS of a dedicated child
    //     transform in the avatar-root hierarchy (AvatarController.StartTilt:
    //     tiltHolder.localRotation = Euler(tilt, 0, 0)). No code ever aims that axis —
    //     not at the head, not at the view, not at anything;
    //   - when the player yaws/moves the world (two-hand grab rotate, recenter), the
    //     tilt axis co-rotates automatically BY PARENTING (tiltHolder is a child of the
    //     avatar root), continuously during the drag — no event plumbing at all;
    //   - head pose is read NOWHERE in the world-pose path
    //     (CameraMoveAndScaleControl.Tick gates every continuous pose change on grip
    //     buttons; TiltMovement.CheckThumbTilt is a discrete stick flick).
    // Our per-frame reconstruction reproduces that hierarchy exactly by deriving the
    // axis from the RIG's OWN yaw: axis = yawOnly(rig.rotation) * Vector3.right, so
    //   desired = AngleAxis(tilt, axis) ∘ yawOnly  ==  yawOnly ∘ AngleAxis(tilt, +X)
    // — algebraically identical to Demeo's yaw-parent/tilt-child chain. Consequences:
    //   - head movement CANNOT move the world: the axis is a pure function of the rig
    //     pose, so under head-only motion desired == current and the transform write is
    //     skipped (bit-frozen world);
    //   - snap/smooth turn and world-grab rotate the rig yaw → the axis co-rotates the
    //     SAME frame, continuously while dragging (the round-4 request), for free;
    //   - the tilt always tips the board toward the rig's forward — the seat direction
    //     the player last chose via recenter/turn/grab, exactly like Demeo, where you
    //     re-aim the world with your HANDS, never with your head.
    //
    // ROUND 6 — VIEW-AIMED TILT WITHOUT PERCEPTIBLE MOTION (hardware round 6 feedback:
    // the tilt must ALWAYS tip toward the current view direction, yet the world must
    // FEEL bolted down — no gliding, no snapping, no visible catch-up, ever).
    // Demeo re-read: Demeo has no continuous head-follow either (no head/CenterEye read
    // exists anywhere in Boardgame.CameraControls/*), but its recenters ABSORB the
    // player's physical yaw into the avatar root behind a fade —
    // CameraMoveAndScaleControl.RecenterDefault (:417) calls InputTracking.Recenter()
    // (tracking origin re-zeroed to the CURRENT head yaw; auto-fired by the headset's
    // system recenter via trackingOriginUpdated, :126), and on VisionOS — whose runtime
    // does not re-zero yaw — AvatarController.ResetAvatarPositionRotationScaleTilt
    // (:684-694) does it explicitly: root.localRotation *= Inverse(headYaw). So in
    // Demeo the tilt "always faces you" because every (masked) recenter silently
    // re-anchors the root yaw to the head yaw. We replicate that and close the
    // remaining gap (physical turning between events) with two channels, both writing
    // ONLY the head-local aim yaw _tiltAimYawDeg that the axis is composed with:
    //   1. MASKED EVENTS (the Demeo-faithful part): recenter, stick turn, world grab
    //      (continuously while held, and on release) instantly set aim = head yaw —
    //      the world is already jumping/dragging, so the re-aim is invisible;
    //   2. MASKED ROTATION (redirected-rotation technique, subthreshold gain): while
    //      the head yaws faster than [Rig] MaskedReaimHeadRate (default 30°/s), the
    //      aim rotates toward the view at MaskedReaimGain (default 15%) of the head's
    //      own angular speed — far below the ~20% rotation-gain detection threshold,
    //      and the induced world motion is further scaled by sin(tilt) (< 8% of head
    //      speed at 30° tilt). Corrections run ONLY during the rotation — the instant
    //      the head slows below threshold the writes stop THAT frame (no catch-up
    //      glide); leftover error waits, bit-frozen, for the next fast rotation or
    //      masked event. Errors below MaskedReaimDeadband (default 5°) are ignored
    //      outright, so ordinary looking-around triggers nothing and the world stays
    //      bit-identical under head-only motion exactly as in round 5.
    //
    // TILT MAGNITUDE (Demeo AvatarController.Tilt): Demeo changes tilt ONLY on a
    // discrete thumbstick flick, in 15° steps (tiltValue 0..12), animated by a 0.2 s
    // LINEAR LeanTween. Ours changes only on the [Rig] WorldTiltDegrees ±5° settings
    // click (its sole writer) and animates between values with the same 0.2 s linear
    // ramp (TiltTweenSeconds). Zoom/scale never touch the magnitude — in Demeo scale
    // only CLAMPS harder while tilted (minScaleTilted), it never drives tilt.

    /// <summary>Demeo's tilt-step animation time (AvatarController.tiltTime = 0.2 s, linear).</summary>
    private const float TiltTweenSeconds = 0.2f;

    private float _tiltApplied;        // degrees actually applied this frame (tween output)
    private float _tiltTweenFrom;      // tween start value (deg)
    private float _tiltTweenStartTime; // Time.unscaledTime at tween start

    /// <summary>
    /// The world-space rotation the tilt currently applies to the rig — the <c>AngleAxis(tilt,
    /// axis)</c> factor of the rig pose, identity while the tilt is off or no scenario rig
    /// exists. This IS the player's perceived-level frame: physical-up maps to
    /// <c>this * Vector3.up</c>, so anything that must READ AS LEVEL to the local player
    /// composes its world pose as <c>WorldTiltRotation * levelPose</c>. CURRENTLY UNCONSUMED:
    /// the control board (item 11) was its only reader until the user decoupled the board from
    /// the tilt entirely (decision 2026-08 — the board stays world-static and is laid out
    /// manually); the frame stays published because the rig is its single honest source and a
    /// future consumer must not have to re-derive it. Written once per
    /// <see cref="TickWorldTilt"/>; declared HERE with the rest of the tilt state so
    /// <c>TearDownRig</c> stays the single reset point (it must never outlive the rig it
    /// described). LOCAL-ONLY, like the tilt itself — never sent over the wire.
    /// </summary>
    internal static Quaternion WorldTiltRotation { get; private set; } = Quaternion.identity;

    // View-aimed tilt state (round 6, provenance above): the HEAD-LOCAL yaw (degrees,
    // rig/tracking space — pure device pose, unaffected by any rig write) the tilt tips
    // toward. 0 = rig forward (the round-5 behavior). Written ONLY under a masked event
    // or a masked-rotation step; under head-only motion it is constant, so the desired
    // pose is constant and the world stays bit-frozen.
    private float _tiltAimYawDeg;
    private float _prevHeadYawDeg;     // last frame's head-local yaw (deg) for the rate estimate
    private bool _prevHeadYawValid;    // false after rig build / fast-path frames → no stale-rate spike

    /// <summary>A masked-correction burst ends after this long without a step (one summary log line).</summary>
    private const float BurstEndGraceSeconds = 0.3f;
    private bool _burstActive;
    private float _burstDegrees;       // total aim degrees consumed this burst
    private float _burstStartTime;
    private float _burstLastStepTime;
    private float _burstPeakHeadRate;  // deg/s

    /// <summary>[Rig] MaskedReaimHeadRate with unbound-safe default and sane floor.</summary>
    private static float MaskedReaimHeadRateDps =>
        Plugin.MaskedReaimHeadRate != null ? Mathf.Max(5f, Plugin.MaskedReaimHeadRate.Value) : 30f;

    /// <summary>[Rig] MaskedReaimGain with unbound-safe default, clamped to the plausible-masking range.</summary>
    private static float MaskedReaimGainFrac =>
        Plugin.MaskedReaimGain != null ? Mathf.Clamp(Plugin.MaskedReaimGain.Value, 0f, 0.5f) : 0.15f;

    /// <summary>[Rig] MaskedReaimDeadband with unbound-safe default.</summary>
    private static float MaskedReaimDeadbandDeg =>
        Plugin.MaskedReaimDeadband != null ? Mathf.Clamp(Plugin.MaskedReaimDeadband.Value, 0f, 45f) : 5f;

    /// <summary>Pending locomotion-event reason for LOG ATTRIBUTION, null when none. Set by
    /// <see cref="NotifyTiltAxisSnap"/>; consumed by the next TickWorldTilt that actually
    /// writes the rig pose. Last-wins when several events land in one frame.</summary>
    private string? _axisSnapReason;

    /// <summary>
    /// Locomotion-event notification. The rig-yaw part of the axis co-rotates with stick
    /// turns / world-grab automatically (round-5 Demeo parenting model — nothing to snap),
    /// but since round 6 these events additionally serve as MASKED RE-AIM opportunities:
    /// the next TickWorldTilt instantly re-aims the tilt at the current view yaw (aim =
    /// head-local yaw), which is invisible because the event is already jumping/dragging
    /// the world — the exact Demeo recenter mechanism (InputTracking.Recenter absorbs the
    /// head yaw behind a fade; provenance on the axis comment block). The reason string
    /// also ATTRIBUTES the resulting pose write in the WorldTilt change log.
    /// </summary>
    internal static void NotifyTiltAxisSnap(string reason)
    {
        if (Instance != null)
            Instance._axisSnapReason = reason;
    }

    /// <summary>Cadence of the tilt-axis diagnostic line while the tilt is active (seconds).</summary>
    private const float TiltLogIntervalSeconds = 5f;
    private float _nextTiltLogTime;

    // Change-attribution diagnostics (round 5): every axis/magnitude change logs its
    // TRIGGER so the hardware log can prove no change ever fires without a locomotion
    // event. Per-frame triggers (smooth turn, active grab) re-log at most once per
    // ChangeLogThrottleSeconds; a NEW trigger always logs immediately.
    private const float ChangeLogThrottleSeconds = 1f;
    private string _lastChangeTrigger = "none";
    private float _nextChangeLogTime;

    // Head camera clear color: [Rig] VoidColor (default pure black since test #6 —
    // the diagnostic-grey era is over; the config description documents that a dark
    // grey helps debugging "renders but empty" vs "camera dead"). Live-tunable via
    // TickHeadClearColor.

    private void Awake()
    {
        Instance = this;
        VREvents.SceneLoaded += OnSceneLoaded;

        // Build the guarded tick list once. The array IS the order; it is no longer "the original
        // Update() tail exactly", as this comment used to claim — Rig.RenderQuality was added
        // later and was never part of that inline tail, which is the same edit that left the step
        // list below wrong (see the note under the marker).
        // Cached delegates → zero per-frame allocation in the loop.
        //
        // FRAME-ORDER VRRigDriver._tailSteps [Rig.HeadCullingMask, Rig.HeadClearColor, Rig.ClipPlanes, Rig.DepthPrepass, Rig.RenderQuality, Rig.CameraPolicy, Rig.MixedReality]
        //   MixedReality is LAST on purpose: it reads the camera state every earlier step wrote
        //   (clear colour, clip planes, per-camera policy) and decides see-through from it.
        //   Promoting it — or inserting a step after it — silently changes what it sees.
        //   Cross-boundary invariant: the array lives in Rig/, the constraint belongs to
        //   Core.MixedReality; the marker is the only thing holding the two together.
        //
        //   The prose that used to sit here listed FIVE steps for this SIX-step array —
        //   Rig.RenderQuality was inserted and the comment was left alone, so the only guard on
        //   the mod's most order-sensitive array had been wrong ever since. Naming the steps
        //   twice (here and in FRAME-ORDER.lock) and checking both against the source is exactly
        //   what stops that from recurring.
        _tailSteps = new (string, System.Action)[]
        {
            ("Rig.HeadCullingMask", TickHeadCullingMask),
            ("Rig.HeadClearColor", TickHeadClearColor),
            ("Rig.ClipPlanes", TickClipPlanes),
            // Sits with the other head-camera render-state re-asserts and BEFORE MixedReality,
            // which reads the camera state the earlier steps wrote. It writes only
            // depthTextureMode, which no later step reads.
            ("Rig.DepthPrepass", TickDepthTextureMode),
            ("Rig.RenderQuality", RenderQuality.Tick),
            ("Rig.CameraPolicy", () => TickCameraPolicy(_tickSceneLoaded)),
            ("Rig.MixedReality", MixedReality.Tick),
        };
    }

    private void OnSceneLoaded(SceneLoadedEvent e)
    {
        _sceneRecheck = true;
        _sceneRecheckName = e.Scene.name;
    }

    /// <summary>
    /// Perf attribution wrapper (2026-07 perf pass): the rig's Update body is the mod's single
    /// largest unattributed per-frame block (rig-kind resolution, teardown/build, recenter, spawn
    /// reseat, then the guarded tail steps), so it gets its own measured scope. The tail's
    /// TickGuard steps are NESTED inside it — they are still ranked individually in the [Perf]
    /// STEPS line, and the nesting depth counter makes sure they are counted only ONCE in the
    /// mod-total, so "Rig.Update" reads as the inclusive total for the whole rig frame.
    /// </summary>
    private void Update()
    {
        using (Core.PerfMonitor.Scope("Rig.Update"))
            UpdateBody();
    }

    private void UpdateBody()
    {
        CameraController controller = CameraController.s_CameraController;
        bool scenarioCameraAlive = controller != null && controller.m_Camera != null;

        // P6 (test #8 giant-map fix): the ORBIT CAMERA ALONE IS NOT A SCENARIO.
        // CameraController.s_CameraController also exists on the campaign/world map
        // (verified: decompiled GH.Runtime/ClickTrackerMap.cs:78 raycasts MapLocations
        // through it on the map scenes), so anchoring the diorama rig to it put the
        // guildmaster map HUGE below the player while the flat window lost the map.
        // The scenario diorama additionally requires an actual scenario board —
        // Choreographer alive, the same canonical signal the mode machine uses
        // (VRModeStateMachine.ScenarioBoardExists; decompiled Choreographer.cs:659,715).
        // Everything pre-scenario (campaign map, guildmaster, merchant, level-up)
        // stays on the MENU rig: head-tracked void + the WorldUI flat screen showing
        // the full backbuffer composite (the map camera is a normal capture there).
        // A head-tracked 3D map diorama is a deliberate FUTURE feature — the
        // [Rig] Experimental3DMap placeholder is bound but UNIMPLEMENTED (it must
        // never silently re-enable the broken orbit-camera anchoring).
        bool scenarioBoardExists = VRModeStateMachine.ScenarioBoardExists;

        // P5 (MISSION A.7): outside a scenario the rig falls back to the menu camera
        // so the HMD view is head-tracked in the main menu / guildmaster map and the
        // WorldUI flat screen + hands have a tracked anchor.
        RigKind desired =
            !VRSession.IsRunning ? RigKind.None :
            scenarioCameraAlive && scenarioBoardExists ? RigKind.Scenario :
            Plugin.MenuRig.Value ? RigKind.Menu :
            RigKind.None;

        bool sceneRecheck = _sceneRecheck;
        _sceneRecheck = false;

        // Health check — tear down (and rebuild below) the frame anything breaks.
        // Order matters: kind change > our camera/root destroyed > anchor destroyed >
        // anchor disabled > a better camera appeared with a scene load.
        string? teardownReason = null;
        string? rebuildRequest = _pendingRebuildRequest;
        _pendingRebuildRequest = null; // one-shot, consumed (or dropped while no rig exists)
        if (_kind != RigKind.None)
        {
            if (rebuildRequest != null)
                teardownReason = rebuildRequest;
            else if (desired != _kind)
                teardownReason = $"rig kind change {_kind} → {desired}";
            else if (_camera == null)
                teardownReason = "owned head camera destroyed externally";
            else if (_rigRoot == null)
                teardownReason = "rig root destroyed externally";
            else if (_anchor == null)
                teardownReason = "anchor camera destroyed";
            else if (!_anchor.isActiveAndEnabled && _kind == RigKind.Menu)
                teardownReason = $"anchor camera '{_anchor.name}' disabled/deactivated";
            else if (sceneRecheck && _kind == RigKind.Menu)
            {
                Camera? best = ResolveMenuCamera();
                if (best != null && best != _anchor)
                    teardownReason = $"scene '{_sceneRecheckName}' brought a better menu camera '{best.name}'";
            }
        }

        if (teardownReason != null)
        {
            TearDownRig(teardownReason);
            _rebuildTrigger = teardownReason;
        }

        if (_kind == RigKind.None)
        {
            if (desired == RigKind.Scenario)
                BuildRig(controller!);
            else if (desired == RigKind.Menu)
                BuildMenuRig();
        }

        // Recenter once tracking delivers the first real pose (localPosition leaves zero).
        if (_pendingRecenter && _camera != null && _camera.transform.localPosition.sqrMagnitude > 1e-6f)
        {
            Recenter();
            _pendingRecenter = false;
        }

        // Spawn-circle re-seat (FEATURE D, B3 robustness): the FFSNet participant registry may
        // not be populated at the first-pose recenter, so LocalStableIndex returns (0,1) and the
        // player gets the solo seat. Poll on a cheap cadence while the scenario rig lives; when
        // the deterministic (idx,total) actually changes — players finished joining, or someone
        // left — re-run Recenter to (re)apply the azimuth. In a stable session this fires once
        // (when the registry populates) then stays silent, so it never fights world-grab/snap-turn.
        if (_kind == RigKind.Scenario && !_pendingRecenter && _camera != null
            && Plugin.SpawnInCircle.Value && --_circleReseatCountdown <= 0)
        {
            _circleReseatCountdown = CircleReseatIntervalFrames;
            int idx = NetPlayerActors.LocalStableIndex(out int total);
            if (idx != _lastCircleIdx || total != _lastCircleTotal)
                Recenter();
        }

        // Per-frame maintenance ticks, each ISOLATED + attributed via the shared
        // Core.TickGuard (throw in one can't abort the rest; the log names the thrower).
        // MR runs LAST so its key-color clear wins the frame over TickHeadClearColor's
        // VoidColor (docs: MixedReality precedence) — no-op unless MR mode is on.
        _tickSceneLoaded = sceneRecheck;
        var tail = _tailSteps;
        for (int i = 0; i < tail.Length; i++)
            TickGuard.Run(tail[i].name, tail[i].fn);
    }

    /// <summary>
    /// LateUpdate runs AFTER every Update-phase rig writer (WorldGrab, SnapTurn, Comfort,
    /// Recenter) and BEFORE rendering — the world tilt is (re)asserted here so any writer
    /// that flattened the rig back to yaw-only this frame (WorldGrab's two-hand solve,
    /// Recenter) is healed before the player ever sees an untilted frame.
    /// </summary>
    private void LateUpdate() =>
        // [Optimize] CacheTickDelegates: the expression body used to allocate a fresh Action from
        // this instance method group every frame — the sibling _tailSteps array had already cached
        // its delegates for exactly this reason; this one had been missed.
        TickGuard.Run("Rig.WorldTilt", PerfConfig.CacheDelegates ? _tickWorldTilt ??= TickWorldTilt : TickWorldTilt);

    /// <summary>Cached LateUpdate tick delegate ([Optimize] CacheTickDelegates).</summary>
    private System.Action? _tickWorldTilt;

    private void OnDestroy()
    {
        VREvents.SceneLoaded -= OnSceneLoaded;
        TearDownRig("rig driver destroyed (shutdown/hot reload)");
        // RESTORE ORDER IS LOAD-BEARING: MixedReality FIRST, then VRCameraPolicy. MR is the
        // narrower mutation and it lives ON cameras the policy owns — it resolves the head camera
        // through VRCameraPolicy.AllowedHead, and every camera it keyed was swept while the policy
        // was in force. VRCameraPolicy.RestoreAll releases that ownership and nulls AllowedHead,
        // so it must be the LAST step of camera teardown: release first and the keyed cameras are
        // left green with the state that recorded them already gone.
        // Do not alphabetise or "group the restores"; this pair is an ordering, not a list.
        // (INVARIANTS-Net-Rig.md "MixedReality.RestoreAll runs BEFORE VRCameraPolicy.RestoreAll",
        //  established by 5c881e7.)
        MixedReality.RestoreAll();
        VRCameraPolicy.RestoreAll();
        if (Instance == this)
            Instance = null;
    }

    // ---- build ---------------------------------------------------------------------------

    private void BuildRig(CameraController controller)
    {
        Camera anchor = controller.m_Camera;
        _anchor = anchor;

        // Freeze the game's orbit-camera writers so the FocusPoint anchor (rig/panel/
        // recenter reference) stays parked while VR owns the view. This flag is the
        // ONLY game-side state the rig touches (restored on teardown); the Harmony
        // prefix-skips in CameraControllerPatches are the durable half.
        controller.m_IsCameraCodeControlDisabled = true;
        _frozeGameCameraControl = true;

        float baseScale = ResolveWorldScale();
        // Re-apply the pinch-scale the player last reached ([Comfort] SavedScaleMultiplier).
        float scale = baseScale * ComfortSettings.ClampedSavedMultiplier;

        _rigRoot = CreateRigRoot();
        // Rig at the orbit focus, yaw taken from the current camera so the board is
        // oriented the way the player last saw it flat. Frozen as the spawn-circle base yaw:
        // Recenter rotates the seat by the per-player azimuth about THIS (idempotent).
        _scenarioBaseYaw = Quaternion.Euler(0f, anchor.transform.eulerAngles.y, 0f);
        _rigRoot.transform.position = controller.FocusPoint;
        _rigRoot.transform.rotation = _scenarioBaseYaw;
        _rigRoot.transform.localScale = Vector3.one * scale;

        // Force the first-pose recenter to (re)evaluate the circle seat from scratch.
        _lastCircleIdx = -1;
        _lastCircleTotal = -1;
        _circleReseatCountdown = CircleReseatIntervalFrames;

        // Clip planes seeded for this scale (~5 real cm near plane) and kept
        // scale-aware while WorldGrab zooms the rig (TickClipPlanes, test #17).
        CreateHeadCamera(anchor, scale);

        RigRoot = _rigRoot.transform;
        HeadCamera = _camera;
        BaseWorldScale = baseScale;
        _kind = RigKind.Scenario;
        RigPoseVersion++;

        _pendingRecenter = true;

        VRLog.Info("Rig", $"VR rig built at focus {controller.FocusPoint}, world scale {scale:F1} " +
                          $"(base {baseScale:F1}, config {Plugin.WorldScale.Value:F1}, " +
                          $"tile size {UnityGameEditorRuntime.s_TileSize.x:F2}); owned head camera " +
                          $"'GloomhavenVR.HeadCamera' (anchor '{anchor.name}' mask 0x{anchor.cullingMask:X8} → " +
                          $"head 0x{_camera!.cullingMask:X8}, renderingPath={_camera.renderingPath}/actual={_camera.actualRenderingPath}) " +
                          $"— trigger: {_rebuildTrigger}.");
        VRCameraPolicy.Sweep("scenario rig built");
        // MSAA truth check: read the XR eye-target desc back once the build settled — proves
        // whether the [RenderQuality] MSAA level actually reached the swapchain (class doc).
        RenderQuality.RequestEyeTargetDiagnostics("scenario rig built");
    }

    /// <summary>
    /// P5 (MISSION A.7): anchor the rig at the MENU camera's vantage at real 1:1 scale
    /// so Menu2D is not a frozen viewpoint — the WorldUI flat screen (and the hands
    /// driving its pointer) anchor to a tracked head in the main menu / guildmaster
    /// screens. Torn down as soon as a scenario camera appears.
    /// </summary>
    private void BuildMenuRig()
    {
        Camera? anchor = ResolveMenuCamera();
        if (anchor == null)
            return;
        _anchor = anchor;

        // Anchor: the camera's authored vantage — recenter puts the head back here.
        _menuAnchorPos = anchor.transform.position;
        _menuAnchorYaw = Quaternion.Euler(0f, anchor.transform.eulerAngles.y, 0f);

        _rigRoot = CreateRigRoot();
        _rigRoot.transform.position = _menuAnchorPos;
        _rigRoot.transform.rotation = _menuAnchorYaw;
        _rigRoot.transform.localScale = Vector3.one;

        CreateHeadCamera(anchor, 1f, modLayerOnly: true);

        RigRoot = _rigRoot.transform;
        HeadCamera = _camera;
        BaseWorldScale = 1f;
        _kind = RigKind.Menu;
        RigPoseVersion++;

        _pendingRecenter = true;

        VRLog.Info("Rig", $"Menu rig built at vantage of camera '{anchor.name}' (1:1 scale, owned head camera " +
                          $"'GloomhavenVR.HeadCamera': clear {_camera!.clearFlags} '{_camera.backgroundColor}', " +
                          $"mask MOD-ONLY 0x{_camera.cullingMask:X8} (anchor 0x{anchor.cullingMask:X8} NOT copied — " +
                          $"test #10), depth {_camera.depth:F1}, stereo Both; anchor stays desktop-only) " +
                          $"— trigger: {_rebuildTrigger}.");
        VRCameraPolicy.Sweep("menu rig built");
        RenderQuality.RequestEyeTargetDiagnostics("menu rig built");
    }

    /// <summary>
    /// The camera to anchor to outside scenarios, best first: Camera.main (tag
    /// MainCamera) → highest-depth enabled backbuffer camera that isn't the UICamera.
    /// Null when the menu scene has no world camera (the rig then waits; the flat
    /// screen is hidden anyway because it needs a world camera too).
    /// </summary>
    private static Camera? ResolveMenuCamera()
    {
        Camera? cam = Camera.main;
        if (cam != null)
            return cam;

        // Cold path only (no-rig frames / scene-load recheck) — shared non-alloc buffer.
        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] all);
        Camera? best = null;
        for (int i = 0; i < count; i++)
        {
            Camera candidate = all[i];
            if (candidate == null || !candidate.enabled || candidate.targetTexture != null
                || candidate.CompareTag("UICamera") || candidate == HeadCamera)
                continue;
            if (best == null || candidate.depth > best.depth)
                best = candidate;
        }
        return best;
    }

    private void TearDownRig(string reason)
    {
        bool hadRig = _kind != RigKind.None;
        bool wasMenu = _kind == RigKind.Menu;
        _kind = RigKind.None;
        // TILT STATE MACHINE RESET — the tilt CODE lives in VRRigDriver.WorldTilt.cs, but every
        // field it owns is declared in THIS file so that this block stays their single reset
        // point. Adding a tilt field there and not resetting it here is the failure mode the
        // partial split makes easy (INVARIANTS-Net-Rig.md "TearDownRig resets the whole tilt
        // state machine"): a fresh rig would inherit the dead rig's aim/tween/burst state.
        _tiltActive = false; // the tilted transform dies with the rig; a new rig re-tilts fresh
        WorldTiltRotation = Quaternion.identity; // perceived-level frame dies with the rig too
        _axisSnapReason = null;
        _lastChangeTrigger = "none";
        _lastTiltTarget = -1f; // fresh-rig sentinel: next rig adopts the configured tilt instantly
        _tiltApplied = 0f;
        _tiltTweenFrom = 0f;
        _tiltAimYawDeg = 0f;   // fresh rig re-seeds the aim from the head (seedAimFromHead)
        _prevHeadYawValid = false;
        _burstActive = false;
        RigRoot = null;
        HeadCamera = null;
        BaseWorldScale = 0f;
        VRCameraPolicy.AllowedHead = null;

        // Everything we destroy here is OURS — the anchor game camera was never
        // reparented or modified, so there is nothing to restore on it.
        if (_poseDriver != null)
        {
            Destroy(_poseDriver);
            _poseDriver = null;
        }
        if (_cameraGo != null)
        {
            Destroy(_cameraGo);
        }
        _cameraGo = null;
        _camera = null;

        if (_rigRoot != null)
        {
            Destroy(_rigRoot);
        }
        _rigRoot = null;
        _anchor = null;

        // Scenario only: un-freeze the game's orbit camera control.
        if (_frozeGameCameraControl)
        {
            CameraController controller = CameraController.s_CameraController;
            if (controller != null)
                controller.m_IsCameraCodeControlDisabled = false;
            _frozeGameCameraControl = false;
        }

        if (hadRig)
        {
            VRLog.Info("Rig", wasMenu
                ? $"Menu rig torn down ({reason}) — owned head camera destroyed, anchor untouched."
                : $"VR rig torn down ({reason}) — owned head camera destroyed, game camera control restored.");
        }

        _pendingRecenter = false;
    }
}
