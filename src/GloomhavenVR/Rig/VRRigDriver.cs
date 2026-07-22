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
    // _lastTiltTarget dedupes the comfort vignette pulse to actual angle changes.
    private bool _tiltActive;
    private float _lastTiltTarget;

    /// <summary>Cadence of the tilt-axis diagnostic line while the tilt is active (seconds).</summary>
    private const float TiltLogIntervalSeconds = 5f;
    private float _nextTiltLogTime;

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

    private void Update()
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
        if (_kind != RigKind.None)
        {
            if (desired != _kind)
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
    private void LateUpdate() => TickGuard.Run("Rig.WorldTilt", TickWorldTilt);

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
    /// reconstruct the desired pose as <c>tilt(target°, about the PLAYER-RELATIVE horizontal
    /// axis) ∘ yawOnly(current)</c> and rotate the rig into it around the BOARD CENTER
    /// (<c>CameraController.FocusPoint</c> — the same orbit focus the rig was built at).
    /// Because the rotation happens about the pivot, the player's virtual head orbits up and
    /// over the board while the board itself appears to tilt toward them; world coordinates
    /// of every game object are untouched, so nothing changes for multiplayer peers except
    /// our own (honestly moved) avatar pose.
    ///
    /// TILT AXIS (hardware round 2 fix — the world STILL tipped partly to the RIGHT): the
    /// axis is the horizontal perpendicular <c>up × d</c> of the flattened head→pivot
    /// direction <c>d</c> — the player's right when facing the board — so tilting about it
    /// reads as pure pitch (board tips toward you) with zero roll. Round 1 derived <c>d</c>
    /// from the CURRENT (already tilted) head position. That feedback is what leaned the
    /// world sideways: tilting orbits the virtual head up toward the pivot's vertical, so
    /// the flattened baseline collapses from the full seat length (EyeBack ≈ 0.55–0.7 m ×
    /// scale) to |head−pivot|·sin(atan(EyeBack/EyeHeight) − tilt) — near zero as tilt
    /// approaches ~45° (standing seat 0.7/0.7) and NEGATIVE (axis flips, 2-frame flip-flop)
    /// beyond it. On a near-degenerate baseline every real-world lateral head offset of a
    /// few cm swings the axis by tens of degrees, and any axis yaw error δ shows up as
    /// tilt·sin(δ) of ROLL in the player's view — the observed rightward lean. Fix: derive
    /// <c>d</c> in the UNTILTED reference frame. The live rig pose is R = T ∘ Y (tilt about
    /// a horizontal axis composed onto yaw), so T = R·Y⁻¹; un-rotating the head about the
    /// pivot by T⁻¹ recovers the head position the yaw-only writers produced before any
    /// tilt — its flattened baseline keeps the full seat length at EVERY tilt angle. The
    /// mapping is idempotent (the axis no longer depends on the tilt it produces), the
    /// tilt plane truly contains the player, and walking around the board still re-aims
    /// the tilt because the untilted head follows the real head. Degenerate case (head
    /// directly above the pivot): fall back to the rig-yaw right axis.
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
            return; // fast path: feature off and nothing to undo — zero writes, 0° bit-identical

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
            return; // anchor died mid-frame; the Update health check tears down next tick

        Transform rig = _rigRoot.transform;
        Quaternion current = rig.rotation;
        Quaternion yawOnly = YawOnly(current);
        Vector3 pivot = controller.FocusPoint;

        // Player-relative tilt axis, derived in the UNTILTED reference frame (round-2 fix,
        // see header): strip the tilt component of the live pose (R = T ∘ Y ⇒ T = R·Y⁻¹)
        // from the head position by un-rotating it about the pivot, flatten THAT head→pivot
        // line, then take the horizontal perpendicular. The baseline keeps its full seat
        // length at every tilt angle, so the axis is insensitive to lateral head noise and
        // independent of the tilt it produces (idempotent — no feedback drift).
        Vector3 headWorld = Vector3.zero;
        Vector3 headUntilted = Vector3.zero;
        Vector3 headToPivot = Vector3.zero;
        if (_camera != null)
        {
            headWorld = _camera.transform.position;
            Quaternion currentTilt = current * Quaternion.Inverse(yawOnly);
            headUntilted = pivot + Quaternion.Inverse(currentTilt) * (headWorld - pivot);
            headToPivot = pivot - headUntilted;
            headToPivot.y = 0f;
        }
        Vector3 axis = headToPivot.sqrMagnitude > 1e-6f
            ? Vector3.Cross(Vector3.up, headToPivot.normalized)
            : yawOnly * Vector3.right; // head above pivot / camera gone — rig-yaw fallback

        Quaternion desired = target > 0f
            ? Quaternion.AngleAxis(target, axis) * yawOnly
            : yawOnly;

        // Comfort: a vignette pulse on actual ANGLE CHANGES (stepper presses / config edits)
        // masks the instant horizon reorientation; per-frame healing never pulses.
        if (!Mathf.Approximately(target, _lastTiltTarget))
        {
            _lastTiltTarget = target;
            _nextTiltLogTime = 0f; // edge-trigger the diagnostic line below
            ComfortVignette.Pulse();
        }

        // Diagnostic (hardware log proof for the sideways-lean fix): on every tilt change
        // and every few seconds while active, log pivot/head/axis and the angle between the
        // axis and the player's flattened head-right. ≈0° (or ≈180° after a snap turn past
        // the board) ⇒ the axis is perpendicular to the view line and the tilt is pure
        // pitch in the player's view; a persistent large mid value ⇒ the pivot
        // (CameraController.FocusPoint) is off to the side of what the player faces.
        if (target > 0f && _camera != null && Time.unscaledTime >= _nextTiltLogTime)
        {
            _nextTiltLogTime = Time.unscaledTime + TiltLogIntervalSeconds;
            Vector3 headRight = _camera.transform.right;
            headRight.y = 0f;
            float axisVsHeadRight = headRight.sqrMagnitude > 1e-6f
                ? Vector3.Angle(axis, headRight.normalized)
                : -1f;
            VRLog.Info("Rig", $"WorldTilt {target:0}°: pivot {pivot}, head {headWorld}, " +
                              $"untilted head {headUntilted} (flat baseline {headToPivot.magnitude:F2}u), " +
                              $"axis {axis} — axis↔head-right {axisVsHeadRight:F1}° " +
                              "(≈0/180 ⇒ pure toward-player pitch, no sideways lean).");
        }

        float error = Quaternion.Angle(current, desired);
        if (error > 0.01f)
        {
            // Rotate the rig into the desired pose AROUND the board center so the pose
            // change reads as the viewpoint orbiting the board, not the world snapping.
            Quaternion delta = desired * Quaternion.Inverse(current);
            rig.position = pivot + delta * (rig.position - pivot);
            rig.rotation = desired;
        }

        _tiltActive = target > 0f;
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
                          $"(seated {(ComfortSettings.IsBound && ComfortSettings.SeatedMode.Value ? "yes" : "no")}" +
                          $", circle seat {idx + 1}/{total}).");
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
