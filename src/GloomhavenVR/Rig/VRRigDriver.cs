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
internal sealed class VRRigDriver : MonoBehaviour
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

        // Build the guarded tick list once — order matches the original Update() tail
        // exactly (HeadCullingMask → HeadClearColor → ClipPlanes → CameraPolicy →
        // MixedReality). Cached delegates → zero per-frame allocation in the loop.
        _tailSteps = new (string, System.Action)[]
        {
            ("Rig.HeadCullingMask", TickHeadCullingMask),
            ("Rig.HeadClearColor", TickHeadClearColor),
            ("Rig.ClipPlanes", TickClipPlanes),
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

    // ---- Demeo-style world tilt ([Rig] WorldTiltDegrees) -----------------------------------

    /// <summary>Configured tilt target, clamped to the supported 0-60° range (0 while unbound).</summary>
    private static float TargetTiltDegrees =>
        Plugin.WorldTiltDegrees != null ? Mathf.Clamp(Plugin.WorldTiltDegrees.Value, 0f, 60f) : 0f;

    /// <summary>
    /// The yaw-only (horizon-aligned) part of a rig rotation, via swing–twist decomposition
    /// about world up: for a unit quaternion q, the twist around Y is
    /// <c>normalize(0, q.y, 0, q.w)</c>. This is EXACT for every pose our writers produce —
    /// algebraically, twist(T ∘ Y) = Y for ANY tilt T about a HORIZONTAL axis composed onto a
    /// yaw Y (the horizontal tilt vector is orthogonal to the yaw vector, so the y/w
    /// components of the product are just cos(t/2)·(sin, cos of the half-yaw)), and likewise
    /// twist(Y₂ ∘ T ∘ Y) = Y₂·Y for snap-turn's world-up compositions. The former
    /// forward-projection version was only exact while the tilt axis was the yaw's own right
    /// axis; the player-relative tilt axis (TickWorldTilt) broke that assumption — projection
    /// would have bled a per-frame yaw drift into the healing loop.
    /// </summary>
    private static Quaternion YawOnly(Quaternion rotation)
    {
        float y = rotation.y;
        float w = rotation.w;
        float mag = Mathf.Sqrt(y * y + w * w);
        if (mag < 1e-6f)
            return Quaternion.identity; // pure 180° flip about a horizontal axis; unreachable
        return new Quaternion(0f, y / mag, 0f, w / mag);
    }

    /// <summary>
    /// Assert the world tilt on the scenario rig (LOCAL-ONLY, rig-side — Demeo model):
    /// reconstruct the desired pose as <c>tilt(target°, about the RIG-YAW-RELATIVE horizontal
    /// axis) ∘ yawOnly(current)</c> and rotate the rig into it around the BOARD CENTER
    /// (<c>CameraController.FocusPoint</c> — the same orbit focus the rig was built at).
    /// Because the rotation happens about the pivot, the player's virtual head orbits up and
    /// over the board while the board itself appears to tilt toward them; world coordinates
    /// of every game object are untouched, so nothing changes for multiplayer peers except
    /// our own (honestly moved) avatar pose.
    ///
    /// TILT AXIS (rounds 5+6 — the DEMEO MODEL plus view aim; provenance + algebra on
    /// the axis-field comment block). The axis is the rig's yaw-frame right composed
    /// with the head-local aim yaw, <c>(yawOnly(rig) ∘ R_up(aim)) * Vector3.right</c> —
    /// the rig-yaw factor is Demeo's yaw-parent/tilt-child chain (co-rotates exactly
    /// with stick turns/world-grab, zero writes), and the aim factor keeps the tilt
    /// tipping toward the VIEW direction. The aim is updated ONLY under perceptual
    /// masking: instantly at masked events (recenter/stick turn/world grab — Demeo's
    /// InputTracking.Recenter analog) and gradually at a subthreshold gain while the
    /// head itself rotates fast (redirected rotation). Under head-only motion below
    /// the masking threshold every input to the desired pose is constant, so
    /// desired == current and no transform write happens — the world is bit-frozen.
    ///
    /// Per-frame reconstruction (not an incremental delta) is what makes every composition
    /// free: recenter and rig rebuilds re-run their yaw-only math and the tilt re-applies
    /// the same frame; snap turn (RotateAround world-up) preserves the pitch and lands
    /// within epsilon; WorldGrab's two-hand yaw-flatten is healed before render. YawOnly's
    /// swing–twist decomposition keeps the yaw extraction exact under the head-relative
    /// (non-yaw-aligned) tilt axis. At the default 0° with no tilt ever applied the method
    /// returns before touching the transform — bit-identical to the pre-feature rig.
    /// </summary>
    private void TickWorldTilt()
    {
        if (_kind != RigKind.Scenario || _rigRoot == null)
            return;

        float target = TargetTiltDegrees;
        if (target <= 0f && !_tiltActive)
        {
            // Fast path: feature off and nothing to undo — zero transform writes, 0°
            // bit-identical to the pre-feature rig. Keep the magnitude edge armed so a
            // later enable tweens up from 0° attributed as the user's config-change.
            _lastTiltTarget = 0f;
            _tiltApplied = 0f;
            _tiltTweenFrom = 0f;
            _prevHeadYawValid = false; // no head-rate tracking while off → no stale spike on enable
            return;
        }

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
            return; // anchor died mid-frame; the Update health check tears down next tick

        Transform rig = _rigRoot.transform;
        Quaternion current = rig.rotation;
        Quaternion yawOnly = YawOnly(current);
        Vector3 pivot = controller.FocusPoint;

        // TILT MAGNITUDE (Demeo AvatarController.Tilt/StartTilt): changes only on the
        // discrete [Rig] WorldTiltDegrees ±5° settings click (its sole writer — the
        // Demeo analog of the thumb-flick 15° step; zoom/scale never touch it) and
        // animates with Demeo's 0.2 s LINEAR ramp. A fresh rig (teardown sentinel -1)
        // adopts the configured tilt instantly — the whole world just (re)appeared,
        // there is no motion the tween would mask.
        string? changeTrigger = null;
        bool seedAimFromHead = false;
        if (!Mathf.Approximately(target, _lastTiltTarget))
        {
            bool freshRig = _lastTiltTarget < 0f;
            _tiltTweenFrom = freshRig ? target : _tiltApplied;
            _tiltTweenStartTime = Time.unscaledTime;
            _lastTiltTarget = target;
            changeTrigger = freshRig ? "rig-build" : "config-change";
            // Fresh rig, or a tween up from FLAT: the axis direction is currently
            // invisible (0° applied), so adopting the player's view yaw as the aim is
            // free — the tilt grows toward wherever they are actually looking.
            seedAimFromHead = freshRig || _tiltTweenFrom <= 0f;
            _nextTiltLogTime = 0f; // edge-trigger the periodic diagnostic line below
        }
        float tweenT = Mathf.Clamp01((Time.unscaledTime - _tiltTweenStartTime) / TiltTweenSeconds);
        _tiltApplied = Mathf.Lerp(_tiltTweenFrom, target, tweenT);

        // VIEW-AIM MAINTENANCE (round 6; mechanism + provenance on the axis comment
        // block). The head-local yaw is the PURE DEVICE pose (TrackedPoseDriver writes
        // rig-local), so every quantity here is invariant under rig writes — snap/stick
        // turns and world-grab can never fake a head rotation, and under head-only
        // motion nothing below writes any state that feeds the desired pose unless a
        // masking condition holds.
        bool grabActive = WorldGrab.Instance != null && WorldGrab.Instance.IsGrabbing;
        bool maskedStep = false;
        float aimError = 0f;
        if (_camera != null)
        {
            float headYawDeg = YawOnly(_camera.transform.localRotation).eulerAngles.y;
            float dt = Time.unscaledDeltaTime;
            float headRate = _prevHeadYawValid && dt > 1e-5f
                ? Mathf.DeltaAngle(_prevHeadYawDeg, headYawDeg) / dt
                : 0f;
            _prevHeadYawDeg = headYawDeg;
            _prevHeadYawValid = true;

            aimError = Mathf.DeltaAngle(_tiltAimYawDeg, headYawDeg);
            if (_axisSnapReason != null || grabActive || seedAimFromHead)
            {
                // MASKED EVENT: the world is already jumping (recenter, stick turn,
                // grab release) or being dragged (active grab — continuous re-aim) or
                // the tilt is still flat — consume the whole error at once, invisibly.
                // This is exactly Demeo's recenter mechanism (InputTracking.Recenter
                // absorbs the head yaw into the root behind a fade).
                _tiltAimYawDeg = headYawDeg;
                aimError = 0f;
            }
            else if (Mathf.Abs(headRate) >= MaskedReaimHeadRateDps
                     && Mathf.Abs(aimError) > MaskedReaimDeadbandDeg)
            {
                // MASKED ROTATION (redirected-rotation, subthreshold gain): correct
                // toward the view ONLY while the head itself rotates fast enough to
                // mask it. The gate re-evaluates every frame from the CURRENT head
                // rate, so the instant the head slows the writes stop — residual
                // error waits, bit-frozen, for the next fast rotation or event.
                float step = Mathf.Sign(aimError)
                             * Mathf.Min(Mathf.Abs(aimError),
                                         MaskedReaimGainFrac * Mathf.Abs(headRate) * dt);
                _tiltAimYawDeg = Mathf.DeltaAngle(0f, _tiltAimYawDeg + step);
                aimError -= step;
                maskedStep = true;
                if (!_burstActive)
                {
                    _burstActive = true;
                    _burstStartTime = Time.unscaledTime;
                    _burstDegrees = 0f;
                    _burstPeakHeadRate = 0f;
                }
                _burstDegrees += Mathf.Abs(step);
                _burstPeakHeadRate = Mathf.Max(_burstPeakHeadRate, Mathf.Abs(headRate));
                _burstLastStepTime = Time.unscaledTime;
            }
        }
        // ONE summary line per masked-correction burst, at its END — never per-frame.
        if (_burstActive && !maskedStep && Time.unscaledTime - _burstLastStepTime > BurstEndGraceSeconds)
        {
            _burstActive = false;
            VRLog.Info("Rig", $"WorldTilt masked re-aim burst: consumed {_burstDegrees:F1}° over " +
                              $"{Mathf.Max(0f, _burstLastStepTime - _burstStartTime):F2}s " +
                              $"(peak head rate {_burstPeakHeadRate:F0}°/s, residual view error {aimError:F1}°).");
        }

        // Tilt axis (round-5 Demeo parenting model + round-6 view aim): the rig's own
        // yaw frame composed with the head-local aim yaw —
        //   desired = AngleAxis(tilt, axis) ∘ yawOnly,  axis = (yawOnly ∘ R_up(aim)) · right
        // The rig-yaw factor makes a pure world-up rig yaw (snap/stick turn, grab
        // rotate) co-rotate the axis exactly — R∘(T∘Y) is bit-reconstructed for the
        // new yaw R·Y, zero correction write — while the aim factor tips the tilt
        // toward the view direction the masked channels last captured. Both factors
        // are constant under head-only motion → desired == current → no writes.
        Quaternion aimYaw = yawOnly * Quaternion.AngleAxis(_tiltAimYawDeg, Vector3.up);
        Vector3 axis = aimYaw * Vector3.right;

        Quaternion desired = _tiltApplied > 0f
            ? Quaternion.AngleAxis(_tiltApplied, axis) * yawOnly
            : yawOnly;

        // Periodic diagnostic while active (hardware-log contract): rigYaw/aim/axis must
        // read IDENTICAL across consecutive lines unless a 'WorldTilt change [trigger]'
        // or a 'masked re-aim burst' line sits between them — every change is either a
        // locomotion/config event or a masked-rotation burst, never bare head movement.
        // viewErr is the head-vs-aim yaw error currently waiting (frozen) for masking.
        if (target > 0f && Time.unscaledTime >= _nextTiltLogTime)
        {
            _nextTiltLogTime = Time.unscaledTime + TiltLogIntervalSeconds;
            Vector3 headPos = _camera != null ? _camera.transform.position : Vector3.zero;
            VRLog.Info("Rig", $"WorldTilt {_tiltApplied:F1}°/{target:0}°: pivot {pivot}, head {headPos}, " +
                              $"rigYaw {yawOnly.eulerAngles.y:F1}°, aim {_tiltAimYawDeg:F1}° (head-local), " +
                              $"viewErr {aimError:F1}°, axis {axis} (last change [{_lastChangeTrigger}]).");
        }

        float error = Quaternion.Angle(current, desired);
        if (error > 0.01f)
        {
            // CHANGE-ATTRIBUTED write (hardware-log contract): every world motion the
            // tilt system causes is logged with its trigger — a locomotion event
            // (NotifyTiltAxisSnap reason, e.g. recenter's yaw-flatten + instant re-aim
            // healed here), an active world grab (its per-frame two-hand yaw-flatten +
            // continuous re-aim healed here), a masked-rotation step, or the user's
            // tilt config click. 'rig-pose-heal' would mean an unattributed external
            // writer flattened the rig — investigate if it ever appears. 'masked-reaim'
            // frames stay QUIET here — their one-line-per-burst summary above is the
            // log (never per-frame).
            string trigger = _axisSnapReason
                             ?? changeTrigger
                             ?? (grabActive ? "world-grab"
                                 : maskedStep ? "masked-reaim"
                                 : "rig-pose-heal");
            bool repeatTrigger = trigger == _lastChangeTrigger;
            _lastChangeTrigger = trigger;
            if (trigger != "masked-reaim" && (!repeatTrigger || Time.unscaledTime >= _nextChangeLogTime))
            {
                _nextChangeLogTime = Time.unscaledTime + ChangeLogThrottleSeconds;
                VRLog.Info("Rig", $"WorldTilt change [{trigger}]: tilt {_tiltApplied:F1}°/{target:0}°, " +
                                  $"rigYaw {yawOnly.eulerAngles.y:F1}°, healed {error:F2}°.");
            }

            // Rotate the rig into the desired pose AROUND the board center so the pose
            // change reads as the viewpoint orbiting the board, not the world snapping.
            Quaternion delta = desired * Quaternion.Inverse(current);
            rig.position = pivot + delta * (rig.position - pivot);
            rig.rotation = desired;
        }
        _axisSnapReason = null; // attribution is per-frame; a no-write frame consumes it too

        _tiltActive = _tiltApplied > 0f;
    }

    private void OnDestroy()
    {
        VREvents.SceneLoaded -= OnSceneLoaded;
        TearDownRig("rig driver destroyed (shutdown/hot reload)");
        MixedReality.RestoreAll(); // put every keyed camera + the skybox back before the policy release
        VRCameraPolicy.RestoreAll();
        if (Instance == this)
            Instance = null;
    }

    // ---- camera ownership policies (docs/CAMERA-POLICY.md) --------------------------------

    /// <summary>
    /// Head culling mask policy (docs/CAMERA-POLICY.md §2):
    ///
    /// - SCENARIO rig: anchor game camera's mask OR the mod layer bit, never 0 — the
    ///   head camera renders the diorama world plus mod visuals.
    /// - MENU rig (hardware test #10 fix): the MOD LAYER ONLY — nothing else, ever.
    ///   Menu2D shows the world exclusively THROUGH the FlatScreen RT composite; the
    ///   anchor mask on the campaign map (0xF00FFE37, the whole 3D world) rendered the
    ///   giant map 1:1 below the player while the quad showed on top of it. The HMD in
    ///   Menu2D must contain exactly: void + screen quad + hands + indicator.
    ///
    /// Cheap per-frame re-assert — the game may rewrite the anchor's mask (and
    /// CanvasConversion may OR UI bits onto our camera in scenario); the policy must
    /// survive every foreign write.
    /// </summary>
    private static int ComposeHeadMask(int sourceMask) =>
        (sourceMask == 0 ? 1 : sourceMask) | VRLayers.ModLayerMask;

    private void TickHeadCullingMask()
    {
        if (_kind == RigKind.None || _camera == null)
            return;
        int wanted;
        if (_kind == RigKind.Menu)
        {
            // Mod layer only — never follow the anchor in Menu2D (test #10).
            wanted = VRLayers.ModLayerMask;
        }
        else
        {
            // Follow the live anchor mask while the anchor exists (the game may toggle
            // layers scene-side); once the anchor died, keep re-asserting our own.
            int source = _anchor != null ? _anchor.cullingMask : _camera.cullingMask;
            wanted = ComposeHeadMask(source);
        }
        if (_camera.cullingMask != wanted)
            _camera.cullingMask = wanted;
    }

    /// <summary>
    /// Keep the owned head camera's SolidColor clear on <c>[Rig] VoidColor</c> —
    /// live-tunable (per-frame color compare only; Skybox-clear anchors keep their sky).
    /// </summary>
    private void TickHeadClearColor()
    {
        if (_camera == null || _camera.clearFlags != CameraClearFlags.SolidColor)
            return;
        Color wanted = Plugin.VoidColor.Value;
        if (_camera.backgroundColor != wanted)
            _camera.backgroundColor = wanted;
    }

    /// <summary>
    /// Keep the owned head camera's clip planes scale-aware (test #17: hands
    /// vanished at max zoom-in — WorldGrab shrinks the rig scale, the hands' world-
    /// unit distance from the eyes shrinks with it, and the build-time near plane
    /// clipped them). near = <see cref="BaseNearMeters"/> × live rig scale, clamped
    /// to absolute world-unit bounds; far grows with zoom-out (the eyes recede from
    /// the fixed-size world) but never drops below the anchor-derived build value,
    /// with the far/near ratio capped for depth precision. Menu rig: scale stays 1,
    /// so this degenerates to the build values. Two float compares per frame.
    /// </summary>
    private void TickClipPlanes()
    {
        if (_camera == null || _rigRoot == null)
            return;
        float scale = _rigRoot.transform.localScale.x;
        float near = Mathf.Clamp(BaseNearMeters * scale, MinNearClip, MaxNearClip);
        float far = Mathf.Min(
            Mathf.Max(_baseFarClip, _baseFarClip * (scale / _buildScale)),
            near * MaxFarNearRatio);
        if (!Mathf.Approximately(_camera.nearClipPlane, near))
            _camera.nearClipPlane = near;
        if (!Mathf.Approximately(_camera.farClipPlane, far))
            _camera.farClipPlane = far;
    }

    /// <summary>
    /// Stereo-exclusion pump: sweep immediately on scene loads (new foreign cameras,
    /// e.g. MainMenu's stereo=Both 'Main Camera'), otherwise on a frame cadence that
    /// also catches cameras created mid-scene. Rig rebuilds sweep inside Build*.
    /// </summary>
    private void TickCameraPolicy(bool sceneLoaded)
    {
        if (!VRSession.IsRunning)
            return;
        if (sceneLoaded)
        {
            VRCameraPolicy.PruneDead();
            MixedReality.PruneDead(); // drop MR bookkeeping for cameras the unload destroyed
            VRCameraPolicy.Sweep("scene load");
            _sweepCountdown = SweepIntervalFrames;
            return;
        }
        if (--_sweepCountdown > 0)
            return;
        _sweepCountdown = SweepIntervalFrames;
        VRCameraPolicy.Sweep("periodic");
    }

    // ---- owned head camera -----------------------------------------------------------------

    /// <summary>
    /// Create OUR head camera under the rig root, seeded from the anchor game camera:
    /// depth = anchor + 1, far plane from the anchor. Clip planes are seeded for
    /// <paramref name="rigScale"/> and kept scale-aware per frame by
    /// <see cref="TickClipPlanes"/> (test #17). Mask policy (CAMERA-POLICY §2):
    /// scenario = anchor mask | mod layer (never 0); menu (<paramref name="modLayerOnly"/>,
    /// test #10) = the mod layer ONLY, with a forced SolidColor [Rig] VoidColor clear —
    /// Menu2D shows the world exclusively through the FlatScreen RT, so the HMD renders
    /// void + quad + hands and nothing of the 3D scene. Scenario keeps the anchor's
    /// Skybox clear when it has one (that IS visible content). The game camera itself is
    /// never modified; stereo on it (and every other game camera) is owned by
    /// <see cref="VRCameraPolicy"/>.
    /// </summary>
    private void CreateHeadCamera(Camera anchor, float rigScale, bool modLayerOnly = false)
    {
        _cameraGo = new GameObject("GloomhavenVR.HeadCamera");
        _cameraGo.transform.SetParent(_rigRoot!.transform, worldPositionStays: false);
        _cameraGo.transform.localPosition = Vector3.zero;
        _cameraGo.transform.localRotation = Quaternion.identity;

        _buildScale = rigScale;
        _baseFarClip = Mathf.Max(anchor.farClipPlane, 100f);

        _camera = _cameraGo.AddComponent<Camera>();
        _camera.cullingMask = modLayerOnly ? VRLayers.ModLayerMask : ComposeHeadMask(anchor.cullingMask);
        _camera.depth = anchor.depth + 1f;
        _camera.nearClipPlane = Mathf.Clamp(BaseNearMeters * rigScale, MinNearClip, MaxNearClip);
        _camera.farClipPlane = _baseFarClip;
        _camera.allowHDR = anchor.allowHDR;
        // ALWAYS allow MSAA on the head camera (aliasing fix #5b/#6): the game's cameras may
        // ship allowMSAA=false and copying that would silently veto the [RenderQuality]
        // MsaaLevel eye-texture MSAA. allowMSAA is only a permission — actual sampling is
        // QualitySettings.antiAliasing (RenderQuality.Tick) and only on the forward path;
        // on deferred it is ignored, so forcing it on is always safe.
        _camera.allowMSAA = true;
        _camera.useOcclusionCulling = anchor.useOcclusionCulling;
        if (!modLayerOnly && anchor.clearFlags == CameraClearFlags.Skybox)
        {
            _camera.clearFlags = CameraClearFlags.Skybox;
        }
        else
        {
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Plugin.VoidColor.Value; // [Rig] VoidColor, default black
        }
        // FOV is owned by the XR display (per-eye projection) — no need to copy.
        _camera.stereoTargetEye = StereoTargetEyeMask.Both;

        // OCCLUSION ROOT CAUSE (transparent effects through walls — flames, hex ring, health bars):
        // the SkyBackdrop DepthResetRenderer (Overlay shader, ZTest Always, queue 1999) resets depth
        // to far so the near sky sphere doesn't occlude the floated board/menus. The Overlay shader
        // has NO deferred pass, so on a DEFERRED camera it renders in the forward-opaque FALLBACK —
        // AFTER the deferred G-buffer walls — and its ZTest-Always wipes the wall depth for the whole
        // transparent pass, so every transparent effect (queue 3000-4000, even ZTest LEqual like the
        // patched hex ring) draws over walls. Opaque figures are unaffected (occluded in the G-buffer
        // BEFORE the wipe) — which is exactly the observed split. FORWARD rendering restores strict
        // per-queue order: the reset (1999) runs BEFORE the walls (2000), the walls overwrite it, the
        // depth buffer keeps the walls, and transparents occlude correctly (the reset's original
        // design assumption). Config-gated so forward's per-object light limit can be reverted if the
        // dungeon lighting regresses.
        if (Plugin.ForwardRendering.Value)
            _camera.renderingPath = RenderingPath.Forward;

        // OCCLUSION (the fire/glow-through-walls saga, final root cause): the game's VFX shaders
        // (torch/candle flames+glow, DFade clouds, distortion) SOFT-FADE against
        // _CameraDepthTexture — big glow billboards physically poke through thin walls, and the
        // depth-fade term is what hides those poked-through fragments in the flat game (its camera
        // gets the depth texture via the game's own stack, incl. the PostProcessLayer the mod
        // kill-switches). Our mod-created head camera shipped with DepthTextureMode.None, so the
        // fade sampled nothing and FAILED OPEN → glow rendered fully through walls. All serialized
        // shader pass states were proven clean (ZTest LEqual, walls ZWrite On) — the ONLY missing
        // piece was this depth texture. One extra depth prepass per eye is the cost; the visual
        // result is the game's ORIGINAL intended soft-particle look.
        _camera.depthTextureMode = DepthTextureMode.Depth;

        // We drive the pose via TrackedPoseDriver — switch off the implicit XR camera
        // tracking the display subsystem would otherwise apply on top.
        XRDevice.DisableAutoXRCameraTracking(_camera, true);

        _poseDriver = _cameraGo.AddComponent<TrackedPoseDriver>();
        _poseDriver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        _poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        _poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        VRCameraPolicy.AllowedHead = _camera;
    }

    /// <summary>Rig root: DontDestroyOnLoad (scene swaps must not kill our camera) + hidden.</summary>
    private GameObject CreateRigRoot()
    {
        var root = new GameObject("GloomhavenVR.VRRig");
        Object.DontDestroyOnLoad(root);
        root.hideFlags = HideFlags.HideAndDontSave;
        return root;
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

    /// <summary>Recenter the live rig, if any (Phase-4 comfort entry point — chord/panel/dev key).</summary>
    internal static void RequestRecenter() => Instance?.Recenter();

    /// <summary>
    /// Reposition the rig so the player's CURRENT head pose ends up at the configured
    /// table-edge spot: eyes <see cref="ComfortSettings.EffectiveEyeHeightMeters"/> (real)
    /// above the orbit focus plane and <see cref="ComfortSettings.EffectiveEyeBackMeters"/>
    /// back (standing/seated presets + [Comfort] TableHeightOffset). Called automatically
    /// on the first tracked pose; Phase 4 binds it to the B+Y hold chord (see
    /// <see cref="Comfort"/>).
    /// </summary>
    internal void Recenter()
    {
        if (_rigRoot == null || _camera == null)
            return;

        if (_kind == RigKind.Menu)
        {
            RecenterMenu();
            return;
        }

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
            return;

        // World tilt composition: the seat math below is authored for a yaw-only rig
        // (seatYaw reads the current rotation; offsets assume a level horizon). Flatten the
        // tilt out first — TickWorldTilt re-applies the configured tilt on top of the fresh
        // seat in LateUpdate this same frame, so a recenter lands at the standard table-edge
        // seat viewed through the tilt, with no untilted frame ever rendered.
        if (_tiltActive)
            _rigRoot.transform.rotation = YawOnly(_rigRoot.transform.rotation);
        // Attribute this frame's tilt re-apply (the LateUpdate heal after the flatten
        // above) to the recenter in the WorldTilt change log — and let it act as a
        // MASKED RE-AIM event: TickWorldTilt instantly re-aims the tilt at the current
        // view yaw, exactly what Demeo's recenter does by absorbing the head yaw into
        // the avatar root (InputTracking.Recenter / AvatarController.cs:684-694).
        _axisSnapReason = "recenter";

        float scale = _rigRoot.transform.localScale.x;

        // Spawn circle (FEATURE D): give each player a distinct azimuth around the focus
        // point so N VR players sit evenly around the board — each FACING the center —
        // instead of stacking at one shared seat. Default seatYaw is the CURRENT rig
        // rotation, so single-player / offline (total <= 1) and the SpawnInCircle-off case
        // keep the EXACT prior behavior: rotation untouched, seat direction = current yaw.
        //
        // For 2+ players we rotate the whole rig about the focus point's up (world up) axis
        // by 360*idx/total, pivoting on the frozen flat base yaw — this rotates BOTH the
        // seat offset direction AND the facing, so the head lands on the circle and the
        // board still reads centered ahead. Rotating from the frozen base (not the live
        // rotation) makes repeated recenters idempotent. The re-seated head world pose is
        // broadcast as-is (foundation avatar sync is world-frame) → remote avatars separate
        // for free. NetPlayerActors is deterministic (participants sorted by PlayerID) and
        // returns (0,1) when the registry isn't ready yet → solo seat this pass; the Update
        // poll re-runs Recenter once (idx,total) changes.
        int idx = 0, total = 1;
        Quaternion seatYaw = _rigRoot.transform.rotation;
        if (Plugin.SpawnInCircle.Value)
        {
            idx = NetPlayerActors.LocalStableIndex(out total);
            if (total > 1)
            {
                seatYaw = Quaternion.AngleAxis(360f * idx / total, Vector3.up) * _scenarioBaseYaw;
                _rigRoot.transform.rotation = seatYaw;
            }
        }
        _lastCircleIdx = idx;
        _lastCircleTotal = total;

        Vector3 desiredHeadWorld = controller.FocusPoint
                                   + seatYaw * (Vector3.back * (ComfortSettings.EffectiveEyeBackMeters * scale))
                                   + Vector3.up * (ComfortSettings.EffectiveEyeHeightMeters * scale);
        Vector3 headOffsetWorld = seatYaw * (_camera.transform.localPosition * scale);
        _rigRoot.transform.position = desiredHeadWorld - headOffsetWorld;
        RigClamp.Apply(_rigRoot.transform);
        RigPoseVersion++; // P6: world-anchored panels re-derive their seat yaw on recenter

        VRLog.Info("Rig", $"Recentered — head at {desiredHeadWorld}, rig root at {_rigRoot.transform.position} " +
                          $"(circle seat {idx + 1}/{total}).");
    }

    /// <summary>
    /// Menu recenter: put the head back at the menu camera's authored vantage (1:1).
    /// Sign convention (verified against hardware test #3 logs): rig = anchor − yaw·headLocal
    /// puts head world = rig + yaw·headLocal = anchor exactly. With floor-origin
    /// tracking headLocal.y ≈ eye height, so the rig root legitimately sits ~1.1–1.7 m
    /// BELOW the anchor.
    /// </summary>
    private void RecenterMenu()
    {
        if (_rigRoot == null || _camera == null)
            return;
        _rigRoot.transform.rotation = _menuAnchorYaw;
        // Offset with the NEW yaw applied (rig scale is 1 in the menu).
        Vector3 headOffsetWorld = _menuAnchorYaw * _camera.transform.localPosition;
        _rigRoot.transform.position = _menuAnchorPos - headOffsetWorld;
        RigPoseVersion++;
        VRLog.Info("Rig", $"Menu rig recentered at the menu camera vantage (anchor {_menuAnchorPos}, " +
                          $"head local {_camera.transform.localPosition}, rig root {_rigRoot.transform.position}).");
    }

    /// <summary>
    /// WorldScale config wins when &gt; 0; otherwise derive from the runtime hex tile
    /// size (<c>UnityGameEditorRuntime.s_TileSize</c>, BOARD-INPUT §2: x = hex width in
    /// world units) so one hex reads as ~15 cm on the table. Falls back to 12× when the
    /// tile size isn't initialized yet (outside a scenario).
    /// </summary>
    private static float ResolveWorldScale()
    {
        float configured = Plugin.WorldScale.Value;
        if (configured > 0f)
            return Mathf.Clamp(configured, 1f, 100f);

        float tileSize = UnityGameEditorRuntime.s_TileSize.x;
        if (tileSize <= 0.001f)
            return FallbackWorldScale;

        return Mathf.Clamp(tileSize / TargetHexSizeMeters, 1f, 100f);
    }

    private void TearDownRig(string reason)
    {
        bool hadRig = _kind != RigKind.None;
        bool wasMenu = _kind == RigKind.Menu;
        _kind = RigKind.None;
        _tiltActive = false; // the tilted transform dies with the rig; a new rig re-tilts fresh
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
