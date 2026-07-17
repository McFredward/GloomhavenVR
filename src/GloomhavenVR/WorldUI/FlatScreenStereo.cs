using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Stereo depth for the <see cref="FlatScreen"/> (hardware test #15, point 7): the
/// flat screen becomes a "3D movie screen" — the captured menu scene (main-menu
/// slideshow, campaign map, guildmaster town) renders once per eye with a horizontal
/// eye offset, so the screen reads like a window into the scene while it is still
/// operated as a flat screen (laser + virtual mouse + poke unchanged).
///
/// PER-EYE RENDERING (MultiPass): the game cameras keep rendering the LEFT RT
/// exactly as before (this is also what the desktop mirror blits — the monitor stays
/// monoscopic and byte-identical). For every captured camera ONE mod-owned MIRROR
/// camera renders the RIGHT RT:
///
/// - 3D cameras (perspective, not tagged UICamera): mirror offset by the eye
///   separation along the source camera's +right, with an off-axis projection shift
///   (lens shift, no toe-in — toe-in causes vertical parallax) that converges both
///   eyes at the screen's own distance. Scene content AT the convergence distance
///   sits exactly on the quad; farther content recedes BEHIND it (window/portal
///   look); nearer content pops slightly out.
/// - UI / orthographic cameras: mirror at ZERO offset — the identical image in both
///   eyes puts all flat UI exactly on the screen plane, which is where the pointer
///   pixel mapping says it is. Rendering the UI per-RT (instead of compositing it
///   once) is REQUIRED for correctness, not just simpler: camera depth order
///   interleaves UI and 3D surfaces (menu 'UI Camera' depth 1 UNDER the map's
///   'Video Camera' depth 5), so each RT must replay the full stack in depth order —
///   a post-hoc UI blit over the right RT would reorder the composite.
///
/// Mirrors are bare cameras: settings are field-copied from the live source every
/// tick (transform, projection, mask minus the mod layer, EFFECTIVE clear flags —
/// i.e. after FlatScreen's base-clear/demotion policy — rect, depth, planes), so the
/// right RT composes exactly like the left one. stereoTargetEye is None and they own
/// a targetTexture, so <see cref="VRCameraPolicy"/> ignores them by construction and
/// <see cref="FlatScreen.CaptureStack"/> skips them (targetTexture != null).
///
/// SEPARATION MATH: the HMD IPD (sampled from the XR head device, fallback 63 mm) is
/// a REAL-meter quantity; the captured cameras live at GAME scale. The mod's one
/// canonical real↔game relation is the rig scale (<see cref="PanelLayout.WorldScale"/>:
/// head motion maps 1 real meter → WorldScale game units; 1 in the menu rig, the
/// diorama scale in scenarios), so:
///
///   separation = IPD × ScreenDepthStrength × WorldScale × ScreenParallaxScale
///   convergence = ScreenDistance × WorldScale × ScreenParallaxScale
///
/// Converging at the screen's own distance makes the geometry self-consistent: the
/// quad physically sits ScreenDistance meters
/// away, so scene content at the equivalent scene distance shows zero disparity
/// (on the quad), and content at scene-infinity shows an uncrossed disparity of
/// IPD·f/D_screen·(W/2) ≈ 4 cm real on the default 2.2 m screen — comfortably
/// BELOW the ~6.3 cm divergence limit. Because the captured camera's FOV is
/// typically NARROWER than the angle the quad subtends, perceived depth is
/// slightly understated — intentional (comfort).
/// Strength scales all disparities linearly; 0 = mono = exactly today's behavior.
///
/// PARALLAX SCALE (hardware test #16: the menu pan read FLAT): with geometric
/// separation (6.3 cm) and convergence at 1.6 scene units, relative parallax
/// (∝ separation/distance) of FAR menu scenery is below the perceivable threshold.
/// [WorldUI] ScreenParallaxScale multiplies BOTH separation AND convergence by the
/// same factor — KEY INVARIANT: the at-infinity disparity depends only on the
/// sep/conv RATIO, which stays constant (still ~4 cm real, below the divergence
/// limit), while every scene-INTERNAL depth difference is amplified by the factor.
/// The captured world reads like a diorama behind glass instead of a flat photo;
/// comfort at infinity is untouched by construction. Near content pops out more
/// aggressively at high factors — the clamp (1..60) and the default (6) keep it
/// in the range validated for the menu scenes.
///
/// PER-EYE QUAD TEXTURE (MultiPass): the head camera renders the quad once per eye
/// pass; a <see cref="Camera.onPreRender"/> hook swaps the quad material's
/// mainTexture per pass from <c>camera.stereoActiveEye</c> (Left → RT-L, Right →
/// RT-R). Should a runtime ever report Mono there, a per-frame pass-parity fallback
/// (first pass = Left) takes over automatically; the observed pattern is logged once.
///
/// VIDEO DEPTH LAYER (hardware test #17): VideoPlayers in CameraNearPlane/FarPlane
/// mode blit decoded frames into their host camera's render target only — a mirror
/// camera can NEVER reproduce them (the player is bound to one camera), so the right
/// eye would miss the video entirely. Suspending stereo (both eyes = left RT) fixed
/// the rivalry but flattened the WHOLE screen whenever the menu's ambient movie ran —
/// the main menu never had depth. Now each discovered camera-plane player is
/// RE-ROUTED instead: renderMode flips to RenderTexture into a mod-owned video RT,
/// and one CommandBuffer per eye composites that RT as the BASE layer into the
/// left RT (on the source camera) and the right RT (on its mirror) with a small
/// opposite horizontal shift per eye. The symmetric shift is UNCROSSED disparity —
/// the flat video plane reads [WorldUI] VideoDepth meters BEHIND the screen plane,
/// while zero-offset UI mirrors keep the clickable menu exactly ON the plane: the
/// UI floats in front of a receded background with no artificial geometry at all.
/// The 3D mirrors keep rendering, so real scene content (menu pan scenery) still
/// gets true per-eye parallax on top of the video base layer.
///
/// Disparity math (real meters, screen-plane geometry): eyes converged on the
/// screen at distance D see a plane at D+V with on-screen disparity
/// p = IPD·V/(D+V) — right eye's image displaced toward the right, left toward the
/// left (each eye toward its own side = behind the screen). p is strictly below
/// the IPD (~6.3 cm divergence limit) for any finite V; the explicit clamp only
/// guards ScreenDepthStrength > 1. UV shift per eye = (p/2)/ScreenWidth; a fixed
/// ~3 % overscan zoom keeps the shifted sampling window inside the video, so the
/// shift never exposes void at the edges. The original renderMode/targetCamera is
/// restored when the video/camera ends, the screen hides, the mirror dies or the
/// feature is disabled (hot-reload safe via Deactivate → ReleaseMirrors).
///
/// FALLBACK SUSPENSION: if re-routing fails (unexpected render mode, RT creation
/// failure) — or [WorldUI] VideoDepthLayer is off — stereo is SUSPENDED while the
/// video runs: both eye passes show the left RT and the mirrors stop rendering.
/// The result is never a one-eyed image. Logged on every flip.
///
/// VIDEO DISCOVERY (hardware test #16, one-eyed intro): a one-shot GetComponent at
/// mirror creation is NOT enough — the intro's player binds to its camera via
/// <c>VideoPlayer.targetCamera</c> from a DIFFERENT GameObject (the intro 'Camera'
/// mirrors as 3D with no player, no suspension → right eye black). Two throttled
/// recovery paths keep the suspension correct without per-frame cost: entries whose
/// cached player is null re-run GetComponent every ~15 frames (players added to the
/// camera GO after capture), and a global FindObjectsOfType sweep every ~30 frames
/// matches camera-plane players to captured cameras by targetCamera OR host GO.
/// Late discoveries are logged once per player. The suspension check itself also
/// verifies the binding still points at the source, so a player re-targeted to an
/// uncaptured camera stops suspending stereo.
///
/// LIFECYCLE: everything is mod-owned under one hidden DontDestroyOnLoad root.
/// Mirrors die with the captured stack (<see cref="ReleaseMirrors"/> on scene
/// change) and are rebuilt on the next capture sweep — late arrivals get a mirror
/// the tick they are captured. <see cref="Deactivate"/> (screen hide, VR off, config
/// off, hot reload via FlatScreen.Shutdown→Hide) destroys mirrors + right RT,
/// unhooks onPreRender and restores the quad texture to the left RT. With
/// <c>[WorldUI] StereoScreen=false</c> (or strength 0) nothing is ever created —
/// the mono path is untouched.
///
/// PERF: while active, every captured camera renders twice (once per RT). That
/// doubles MENU-scene rendering only (the screen is a menu/modal surface), and the
/// suspension path removes the extra cost exactly when videos already dominate.
/// No per-frame allocations: mirror sync is field copies; records allocate only when
/// a NEW camera is first mirrored.
/// </summary>
internal sealed class FlatScreenStereo
{
    private const float DefaultIpdMeters = 0.063f;
    private const int IpdSampleIntervalFrames = 90;
    /// <summary>Convergence floor (real meters) — guards a mis-configured ScreenDistance.</summary>
    private const float MinConvergenceMeters = 0.25f;
    /// <summary>Frames between GetComponent re-checks on entries with no cached VideoPlayer.</summary>
    private const int VideoRecheckIntervalFrames = 15;
    /// <summary>Frames between global sweeps for camera-plane VideoPlayers on OTHER GameObjects.</summary>
    private const int VideoSweepIntervalFrames = 30;
    /// <summary>
    /// UV zoom on the re-routed video (class doc VIDEO DEPTH LAYER): the per-eye blit
    /// samples a window this factor smaller than the full frame, so the disparity
    /// shift never drags the sampling window off the video (void at the edges). The
    /// 3 % margin per axis (~1.46 % per side) covers the worst-case clamped shift.
    /// </summary>
    private const float VideoOverscan = 1.03f;
    /// <summary>
    /// Ceiling on the total video disparity in real meters — below the ~6.3 cm
    /// divergence limit. Geometry alone (p = IPD·V/(D+V)) can never reach the IPD;
    /// this guards ScreenDepthStrength values above 1 scaling p past it.
    /// </summary>
    private const float MaxVideoDisparityMeters = 0.055f;

    // Config lives in the SAME dev.gloomhavenvr.worldui.cfg as the rest of the
    // FlatScreen ([WorldUI] section) — bound through an existing entry's ConfigFile
    // (WorldUIConfig itself is owned by another feature set; no second ConfigFile
    // instance may be opened on the same path).
    private static ConfigEntry<bool>? s_stereoScreen;
    private static ConfigEntry<float>? s_depthStrength;
    private static ConfigEntry<float>? s_parallaxScale;
    private static ConfigEntry<bool>? s_videoDepthLayer;
    private static ConfigEntry<float>? s_videoDepth;

    /// <summary>One mod-owned mirror camera shadowing a captured game camera into the right RT.</summary>
    private sealed class MirrorEntry
    {
        public Camera Source = null!;
        public Camera Mirror = null!;
        public Transform MirrorTransform = null!;
        public GameObject Go = null!;
        /// <summary>
        /// Camera-plane VideoPlayer bound to the source (near-plane video suspension) —
        /// hosted on its GO or targeting it via targetCamera; discovered at mirror
        /// creation or later by the throttled recheck/sweep (class doc VIDEO DISCOVERY).
        /// </summary>
        public VideoPlayer? Video;
        /// <summary>True = eye-offset stereo camera; false = zero-offset mono (UI / orthographic).</summary>
        public bool Stereo3D;
        // Per-tick mark-and-sweep + video inputs (written by SyncCamera, read by EndStackSync).
        public bool Synced;
        public bool SourceOn;
        public bool VideoActive;

        // ---- video depth layer routing (class doc VIDEO DEPTH LAYER) ----------------
        /// <summary>True while the player is re-routed into <see cref="VideoRt"/> and both blits run.</summary>
        public bool Routed;
        /// <summary>Routing failed once for this entry — stay on the suspension fallback (no retry spam).</summary>
        public bool RouteFailed;
        /// <summary>Player state to restore when the route is torn down.</summary>
        public VideoRenderMode RoutedOriginalMode;
        public Camera? RoutedOriginalCamera;
        /// <summary>Mod-owned RT the re-routed player decodes into (composited into BOTH eye RTs).</summary>
        public RenderTexture? VideoRt;
        public CommandBuffer? SourceCb;
        public CommandBuffer? MirrorCb;
        /// <summary>Camera event both blits hook (near-plane → after geometry, far-plane → before).</summary>
        public CameraEvent CbEvent;
        /// <summary>Per-eye UV shift the blits were last built with (rebuild only on change).</summary>
        public float BuiltShiftUv = float.MinValue;
    }

    private readonly List<MirrorEntry> _mirrors = new(8);
    private readonly Dictionary<Camera, MirrorEntry> _bySource = new();
    private readonly Camera.CameraCallback _preRenderHook;

    private GameObject? _root;
    private RenderTexture? _rtRight;
    private RenderTexture? _leftRt;
    private Material? _quadMaterial;

    private bool _active;
    private bool _hooked;
    private bool _videoSuspended;

    private float _ipdMeters = DefaultIpdMeters;
    private int _ipdFrame = int.MinValue;
    private bool _ipdLogged;

    // Late video discovery (class doc VIDEO DISCOVERY) — throttle gates + one-line-
    // per-player log guard (instance IDs; players die with their scene, the set stays
    // small for the process lifetime).
    private int _videoRecheckFrame = int.MinValue;
    private bool _videoRecheckDue;
    private int _videoSweepFrame = int.MinValue;
    private readonly HashSet<int> _videoLogged = new();

    /// <summary>Eye separation / convergence distance in CAPTURED-SCENE units (recomputed per tick).</summary>
    private float _sepScene;
    private float _convScene;

    /// <summary>Per-eye UV shift of the video depth layer (recomputed per tick; 0 = plane depth).</summary>
    private float _videoShiftUv;

    // onPreRender eye-pass bookkeeping (all fixed fields — the hook must not allocate).
    private int _parityFrame = -1;
    private int _parityIndex;
    private int _eyeObsCount;
    private Camera.MonoOrStereoscopicEye _eyeObs0;
    private Camera.MonoOrStereoscopicEye _eyeObs1;

    /// <summary>True while per-eye rendering is engaged (right RT + hook exist).</summary>
    internal bool Active => _active;

    public FlatScreenStereo()
    {
        _preRenderHook = OnPreRenderCamera; // cached delegate — one allocation, ever
        BindConfig();
    }

    private static void BindConfig()
    {
        if (s_stereoScreen != null)
            return;
        ConfigFile file = WorldUIConfig.Master.ConfigFile;
        s_stereoScreen = file.Bind("WorldUI", "StereoScreen", true,
            "Render the floating 2D screen WITH stereo depth (3D-movie/window effect): the " +
            "captured 3D menu content (campaign map, town, slideshow scene) is rendered once " +
            "per eye via mod-owned mirror cameras while flat UI stays exactly on the screen " +
            "plane. Interaction (laser, poke, virtual mouse) and the desktop mirror are " +
            "unaffected. Costs one extra render of the menu scene per frame while the screen " +
            "is visible. Off = single mono RenderTexture, exactly the pre-stereo behavior.");
        s_depthStrength = file.Bind("WorldUI", "ScreenDepthStrength", 1.0f,
            "Strength of the flat screen's stereo depth (scales the per-eye separation " +
            "linearly). 1 = geometrically derived from your HMD IPD (window-accurate, " +
            "slightly understated by design); smaller = flatter/more comfortable; " +
            "0 = mono (same as StereoScreen=false).");
        s_videoDepthLayer = file.Bind("WorldUI", "VideoDepthLayer", true,
            "Keep stereo depth while a fullscreen 2D video plays on the screen (main-menu " +
            "ambient movie, story videos): the video is re-routed into a mod RenderTexture " +
            "and composited into BOTH eyes with a small opposite per-eye shift, so the flat " +
            "video reads VideoDepth meters BEHIND the screen plane while the clickable UI " +
            "stays exactly ON it — background recedes, menu floats in front, no artificial " +
            "geometry. Off = old behavior: stereo is fully suspended (mono) while any " +
            "camera-plane video plays.");
        s_videoDepth = file.Bind("WorldUI", "VideoDepth", 0.8f,
            "How far BEHIND the screen plane a re-routed 2D video appears, in real meters " +
            "(VideoDepthLayer). Disparity p = IPD*V/(D+V) with D = ScreenDistance: at the " +
            "1.6 m default screen and 0.8 m depth the video reads at 2.4 m (1.5x the screen " +
            "distance, ~21 mm disparity — far below the ~63 mm divergence limit; clamped " +
            "regardless). 0 = video on the screen plane (no video depth).");
        s_parallaxScale = file.Bind("WorldUI", "ScreenParallaxScale", 6.0f,
            "Amplifies the stereo screen's scene-INTERNAL depth (test #16: far menu scenery " +
            "read flat at geometric settings). Separation AND convergence are multiplied by " +
            "the same factor, so the at-infinity disparity (their ratio) stays constant and " +
            "comfortable while depth differences inside the captured scene grow this many " +
            "times stronger — diorama-behind-glass instead of flat photo. 1 = strict window " +
            "geometry; clamped to 1-60.");
    }

    private static float DepthStrength => Mathf.Clamp(s_depthStrength?.Value ?? 1f, 0f, 3f);

    private static float ParallaxScale => Mathf.Clamp(s_parallaxScale?.Value ?? 6f, 1f, 60f);

    private static bool WantActive(RenderTexture? leftRt) =>
        leftRt != null
        && VRSession.IsRunning
        && Rig.VRRigDriver.HeadCamera != null
        && (s_stereoScreen?.Value ?? false)
        && DepthStrength > 0f;

    // ---- lifecycle -------------------------------------------------------------------------

    /// <summary>
    /// Per-tick lifecycle (called from FlatScreen.CaptureStack while the screen is
    /// visible): engage/disengage per config + session state, keep the right RT in
    /// step with the left one, refresh IPD and the derived scene-unit geometry.
    /// </summary>
    internal void Tick(RenderTexture? leftRt, Renderer? quadRenderer)
    {
        _leftRt = leftRt;
        _quadMaterial = quadRenderer != null ? quadRenderer.sharedMaterial : null;

        if (!WantActive(leftRt))
        {
            if (_active)
                Deactivate("disabled (config/session/rig state)");
            return;
        }

        // Right RT mirrors the left one's dimensions (they must match — the pointer
        // pixel mapping and the quad UVs are shared).
        if (_rtRight != null && (_rtRight.width != leftRt!.width || _rtRight.height != leftRt.height))
        {
            ReleaseRightRt();
        }
        if (_rtRight == null)
        {
            _rtRight = new RenderTexture(leftRt!.width, leftRt.height, 24)
            {
                name = "GloomhavenVR.FlatScreenRT.Right",
                antiAliasing = 1,
            };
            _rtRight.Create();
        }

        if (!_active)
        {
            _active = true;
            if (!_hooked)
            {
                Camera.onPreRender += _preRenderHook;
                _hooked = true;
            }
            _eyeObsCount = 0; // re-log the observed eye-pass pattern per activation
            VRLog.Info("WorldUI", "STEREO SCREEN ACTIVE — flat screen renders per eye " +
                                  "(left RT = game cameras, right RT = mod mirror cameras; " +
                                  "menu-scene rendering doubles while the screen is visible). " +
                                  $"IPD {_ipdMeters * 1000f:F1} mm, strength {DepthStrength:F2}.");
        }

        SampleIpd();

        // Real meters → captured-scene units via the rig's real↔game scale relation
        // (1 in the menu rig; diorama scale in scenarios — see class doc). Both terms
        // carry the SAME parallax factor (class doc PARALLAX SCALE: sep/conv ratio —
        // and with it the at-infinity comfort — is invariant; only scene-internal
        // depth is amplified), and convergence targets the quad's actual distance
        // (ScreenDistance), so content at screen distance lands exactly on the image.
        float scale = PanelLayout.WorldScale;
        float parallax = ParallaxScale;
        _sepScene = _ipdMeters * DepthStrength * scale * parallax;
        _convScene = Mathf.Max(MinConvergenceMeters,
            WorldUIConfig.ScreenDistance.Value) * scale * parallax;
        _videoShiftUv = ComputeVideoShiftUv();
    }

    /// <summary>
    /// Per-eye UV shift for the video depth layer (class doc VIDEO DEPTH LAYER —
    /// disparity math). Pure screen-plane geometry in REAL meters, so no WorldScale
    /// or parallax factor applies: p = IPD·strength·V/(D+V), clamped below the
    /// divergence limit AND below the overscan margin (a larger shift would sample
    /// past the video edge — depth silently caps instead of showing void).
    /// </summary>
    private float ComputeVideoShiftUv()
    {
        float depth = Mathf.Clamp(s_videoDepth?.Value ?? 0.8f, 0f, 5f);
        if (depth <= 0f)
            return 0f;
        float distance = Mathf.Max(0.1f, WorldUIConfig.ScreenDistance.Value);
        float disparity = Mathf.Min(
            _ipdMeters * DepthStrength * depth / (distance + depth), MaxVideoDisparityMeters);
        float halfUv = 0.5f * disparity / Mathf.Max(0.5f, WorldUIConfig.ScreenWidth.Value);
        return Mathf.Min(halfUv, (1f - 1f / VideoOverscan) * 0.5f);
    }

    /// <summary>Full teardown: mirrors, right RT, render hook; quad texture back to the left RT.</summary>
    internal void Deactivate(string reason)
    {
        ReleaseMirrors();
        if (_hooked)
        {
            Camera.onPreRender -= _preRenderHook;
            _hooked = false;
        }
        if (_quadMaterial != null && _leftRt != null && _quadMaterial.mainTexture != _leftRt)
            _quadMaterial.mainTexture = _leftRt;
        ReleaseRightRt();
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        if (_active)
        {
            _active = false;
            _videoSuspended = false;
            VRLog.Info("WorldUI", $"Stereo screen deactivated ({reason}) — mirrors destroyed, " +
                                  "right RT released, quad back on the mono RT.");
        }
    }

    private void ReleaseRightRt()
    {
        if (_rtRight == null)
            return;
        _rtRight.Release();
        Object.Destroy(_rtRight);
        _rtRight = null;
    }

    // ---- per-tick stack sync ---------------------------------------------------------------

    /// <summary>Mark-and-sweep begin: every entry must be re-claimed by <see cref="SyncCamera"/>.</summary>
    internal void BeginStackSync()
    {
        for (int i = 0; i < _mirrors.Count; i++)
            _mirrors[i].Synced = false;

        // Late-video throttles (class doc VIDEO DISCOVERY). The global sweep runs
        // FIRST so the SyncCamera calls of this very tick already see a discovered
        // player and can suspend immediately.
        _videoRecheckDue = Time.frameCount - _videoRecheckFrame >= VideoRecheckIntervalFrames;
        if (_videoRecheckDue)
            _videoRecheckFrame = Time.frameCount;
        if (_active && _mirrors.Count > 0
            && Time.frameCount - _videoSweepFrame >= VideoSweepIntervalFrames)
        {
            _videoSweepFrame = Time.frameCount;
            SweepForCameraPlaneVideos();
        }
    }

    /// <summary>
    /// Global sweep for camera-plane VideoPlayers living on GameObjects OTHER than
    /// their camera (the intro binds via targetCamera — test #16 one-eyed intro).
    /// Throttled to every <see cref="VideoSweepIntervalFrames"/> frames while stereo
    /// is active and mirrors exist; the FindObjectsOfType allocation is accepted at
    /// that rate — a missed player costs a whole eye, not a frame-time spike.
    /// </summary>
    private void SweepForCameraPlaneVideos()
    {
        VideoPlayer[] players = Object.FindObjectsOfType<VideoPlayer>();
        for (int i = 0; i < players.Length; i++)
        {
            VideoPlayer player = players[i];
            if (player.renderMode != VideoRenderMode.CameraNearPlane
                && player.renderMode != VideoRenderMode.CameraFarPlane)
                continue;

            MirrorEntry? entry = null;
            Camera? bound = player.targetCamera;
            if (bound != null && _bySource.TryGetValue(bound, out MirrorEntry byTarget))
                entry = byTarget;
            else
            {
                Camera host = player.GetComponent<Camera>();
                if (host != null && _bySource.TryGetValue(host, out MirrorEntry byHost))
                    entry = byHost;
            }
            // Only fill EMPTY slots (Unity fake-null included — a destroyed player is
            // replaced, a live cached one is never thrashed by a second candidate).
            if (entry == null || entry.Video != null)
                continue;
            entry.Video = player;
            LogLateVideo(player, entry.Source, "global sweep, bound via targetCamera from GO '"
                                               + player.gameObject.name + "'");
        }
    }

    private void LogLateVideo(VideoPlayer player, Camera source, string how)
    {
        if (!_videoLogged.Add(player.GetInstanceID()))
            return;
        VRLog.Info("WorldUI", $"Stereo screen: camera-plane VideoPlayer discovered LATE for " +
                              $"'{source.name}' ({how}) — near-plane suspension now applies " +
                              "(without it this video would render in one eye only).");
    }

    /// <summary>
    /// Ensure a mirror exists for this captured camera and copy the source's live
    /// state onto it (field copies only — no allocations after first capture).
    /// </summary>
    internal void SyncCamera(Camera source)
    {
        if (!_active || _rtRight == null)
            return;

        if (!_bySource.TryGetValue(source, out MirrorEntry entry))
            entry = CreateMirror(source);

        entry.Synced = true;
        entry.SourceOn = source.isActiveAndEnabled;
        // Players can be ADDED to the camera GO after mirror creation — re-check
        // empty slots on the throttled gate (destroyed players are fake-null and
        // re-checked too; class doc VIDEO DISCOVERY).
        if (entry.Video == null && _videoRecheckDue)
        {
            entry.Video = source.GetComponent<VideoPlayer>();
            if (entry.Video != null)
                LogLateVideo(entry.Video, source, "component appeared on the camera's GameObject");
        }
        if (entry.Routed)
        {
            // While re-routed the player is OURS: re-assert the RenderTexture binding
            // every tick (game code may rewrite renderMode/targetTexture at any time —
            // same philosophy as FlatScreen's targetTexture re-assert) and count the
            // video as active while the player lives and its host camera renders.
            // EndStackSync tears the route down once either goes away.
            VideoPlayer? player = entry.Video;
            bool alive = player != null && player.enabled;
            if (alive && entry.VideoRt != null)
            {
                if (player!.renderMode != VideoRenderMode.RenderTexture)
                    player.renderMode = VideoRenderMode.RenderTexture;
                if (player.targetTexture != entry.VideoRt)
                    player.targetTexture = entry.VideoRt;
            }
            entry.VideoActive = entry.SourceOn && alive;
        }
        else
        {
            entry.VideoActive = entry.SourceOn && IsNearPlaneVideoActive(entry.Video, source);
        }
        if (!entry.SourceOn)
            return; // mirror gets disabled in EndStackSync; nothing to copy

        Camera mirror = entry.Mirror;
        Transform st = source.transform;

        // Eye offset: right eye = source pose shifted along the source's +right.
        // UI/ortho mirrors stay at zero offset (identical image = screen-plane depth).
        if (entry.Stereo3D && _sepScene > 0f)
            entry.MirrorTransform.SetPositionAndRotation(st.position + st.right * _sepScene, st.rotation);
        else
            entry.MirrorTransform.SetPositionAndRotation(st.position, st.rotation);

        // Live field copies: the source's CURRENT clear flags are the EFFECTIVE ones
        // (FlatScreen's base-clear force + overlay demotion already applied), so the
        // right RT composes under the exact same clear policy as the left.
        mirror.clearFlags = source.clearFlags;
        mirror.backgroundColor = source.backgroundColor;
        mirror.cullingMask = source.cullingMask & ~VRLayers.ModLayerMask; // never our quad/hands (feedback!)
        mirror.depth = source.depth;                                     // same per-RT compositing order
        mirror.rect = source.rect;
        mirror.nearClipPlane = source.nearClipPlane;
        mirror.farClipPlane = source.farClipPlane;
        mirror.orthographic = source.orthographic;
        mirror.orthographicSize = source.orthographicSize;
        mirror.fieldOfView = source.fieldOfView;
        mirror.allowHDR = source.allowHDR;
        mirror.allowMSAA = source.allowMSAA;
        mirror.useOcclusionCulling = source.useOcclusionCulling;
        if (mirror.targetTexture != _rtRight)
            mirror.targetTexture = _rtRight;

        // Projection: copy the source matrix; stereo mirrors additionally get the
        // off-axis convergence shift (see class doc — lens shift, not toe-in).
        // Derivation: the right camera sits +s along +right; a point straight ahead
        // at the convergence distance D lands at NDC x = -m00*s/D in it, so
        // m02 -= m00*s/D translates the image so that point matches the left eye
        // (zero disparity at D; uncrossed/behind-screen beyond it).
        Matrix4x4 proj = source.projectionMatrix;
        if (entry.Stereo3D && _sepScene > 0f && _convScene > 1e-4f)
            proj.m02 -= proj.m00 * (_sepScene / _convScene);
        mirror.projectionMatrix = proj;
    }

    /// <summary>
    /// Mark-and-sweep end: drop mirrors whose source died/left the stack, engage the
    /// video depth layer for camera-plane videos (class doc VIDEO DEPTH LAYER),
    /// decide the fallback suspension for THIS frame, and apply the enabled state.
    /// </summary>
    internal void EndStackSync()
    {
        if (!_active)
            return;

        bool depthLayer = s_videoDepthLayer?.Value ?? true;
        bool suspend = false;
        for (int i = _mirrors.Count - 1; i >= 0; i--)
        {
            MirrorEntry entry = _mirrors[i];
            if (!entry.Synced || entry.Source == null)
            {
                DestroyMirrorAt(i);
                continue;
            }

            if (entry.Routed && (!depthLayer || !entry.VideoActive))
            {
                // Kill switch flipped, or the player/camera went away — give the
                // player its native binding back, then fall through: a STILL-running
                // camera-plane video (kill-switch case) re-enters the suspension
                // check below so no frame is ever one-eyed.
                RestoreVideoRoute(entry, depthLayer ? "video/camera ended" : "VideoDepthLayer disabled");
                entry.VideoActive = entry.SourceOn
                                    && IsNearPlaneVideoActive(entry.Video, entry.Source);
            }

            if (!entry.VideoActive)
                continue;

            if (entry.Routed || (depthLayer && !entry.RouteFailed && TryRouteVideo(entry)))
            {
                UpdateVideoBlits(entry);
                continue; // both RTs get the video — full stereo stays engaged
            }
            suspend = true; // fallback: both eyes show the left RT (never one-eyed)
        }

        if (suspend != _videoSuspended)
        {
            _videoSuspended = suspend;
            VRLog.Info("WorldUI", suspend
                ? "Stereo screen SUSPENDED — a captured camera plays a near-plane video " +
                  "the depth layer could not take over (disabled or re-route failed); both " +
                  "eyes show the left RT until it ends."
                : "Stereo screen RESUMED — near-plane video ended; per-eye rendering re-engaged.");
        }

        for (int i = 0; i < _mirrors.Count; i++)
        {
            MirrorEntry entry = _mirrors[i];
            bool want = entry.SourceOn && !suspend;
            if (entry.Mirror.enabled != want)
                entry.Mirror.enabled = want;
        }
    }

    /// <summary>Destroy all mirrors (captured stack released — scene change / hide). Cheap to rebuild.</summary>
    internal void ReleaseMirrors()
    {
        for (int i = _mirrors.Count - 1; i >= 0; i--)
            DestroyMirrorAt(i);
        _mirrors.Clear();
        _bySource.Clear();
    }

    private void DestroyMirrorAt(int index)
    {
        MirrorEntry entry = _mirrors[index];
        // A routed player must never outlive its route: without the source CB the
        // video would stop reaching the left RT entirely (worse than mono).
        RestoreVideoRoute(entry, "mirror destroyed");
        // Unity fake-null: the managed key survives Destroy — Remove still works.
        _bySource.Remove(entry.Source);
        if (entry.Go != null)
            Object.Destroy(entry.Go);
        _mirrors.RemoveAt(index);
    }

    private MirrorEntry CreateMirror(Camera source)
    {
        if (_root == null)
        {
            _root = new GameObject("GloomhavenVR.StereoScreenMirrors");
            Object.DontDestroyOnLoad(_root);
            _root.hideFlags = HideFlags.HideAndDontSave;
        }

        var go = new GameObject("GloomhavenVR.StereoMirror." + source.name);
        go.transform.SetParent(_root.transform, worldPositionStays: false);

        var cam = go.AddComponent<Camera>();
        cam.enabled = false;                              // EndStackSync flips it on
        cam.stereoTargetEye = StereoTargetEyeMask.None;   // VRCameraPolicy-invisible by construction
        cam.targetTexture = _rtRight;
        XRDevice.DisableAutoXRCameraTracking(cam, true);

        var entry = new MirrorEntry
        {
            Source = source,
            Mirror = cam,
            MirrorTransform = go.transform,
            Go = go,
            // Camera-hosted VideoPlayer (MainMenuVideo ambient movies, campaign
            // 'Video Camera' fullscreen videos) — first suspension probe; players
            // added later or bound from another GO via targetCamera (the intro) are
            // caught by the throttled recheck/sweep (class doc VIDEO DISCOVERY).
            Video = source.GetComponent<VideoPlayer>(),
            // UI-tagged and orthographic cameras composite flat AT the screen plane;
            // real 3D perspective cameras get the eye offset.
            Stereo3D = !source.CompareTag("UICamera") && !source.orthographic,
        };
        _mirrors.Add(entry);
        _bySource.Add(source, entry);

        VRLog.Info("WorldUI", $"Stereo mirror created for '{source.name}': " +
                              $"{(entry.Stereo3D ? $"3D (eye offset {_sepScene:F4} scene units, converge {_convScene:F2})" : "MONO (UI/ortho — screen-plane depth)")}" +
                              $"{(entry.Video != null ? ", hosts a VideoPlayer (near-plane suspension applies)" : "")}.");
        return entry;
    }

    /// <summary>
    /// True while this player blits camera-plane frames into the SOURCE camera's
    /// output. The binding check (targetCamera or same GO) lets a player that gets
    /// re-targeted elsewhere stop suspending stereo without waiting for a sweep.
    /// </summary>
    private static bool IsNearPlaneVideoActive(VideoPlayer? video, Camera source) =>
        video != null && video.enabled
        && (video.renderMode == VideoRenderMode.CameraNearPlane
            || video.renderMode == VideoRenderMode.CameraFarPlane)
        && (video.targetCamera == source || video.gameObject == source.gameObject);

    // ---- video depth layer (class doc VIDEO DEPTH LAYER) -------------------------------------

    /// <summary>
    /// Re-route the entry's camera-plane VideoPlayer into a mod-owned RT and attach
    /// one compositing CommandBuffer per eye. Ordered so the PLAYER is touched last:
    /// any failure before that leaves the game's binding untouched, and the catch
    /// path (mode/RT/AddCommandBuffer surprises) unwinds whatever was created and
    /// flags the entry for the suspension fallback. False = caller must suspend.
    /// </summary>
    private bool TryRouteVideo(MirrorEntry entry)
    {
        VideoPlayer? player = entry.Video;
        RenderTexture? leftRt = _leftRt;
        if (player == null || leftRt == null)
            return false;

        try
        {
            // RT sized to the decoded video (1:1 texels); before the player reports
            // its dimensions (not yet prepared) fall back to the screen RT size —
            // both end up stretched to the full screen by the blit either way.
            int width = player.width > 0 ? (int)player.width : leftRt.width;
            int height = player.height > 0 ? (int)player.height : leftRt.height;
            var rt = new RenderTexture(width, height, 0)
            {
                name = "GloomhavenVR.FlatScreenVideoRT",
            };
            if (!rt.Create())
            {
                Object.Destroy(rt);
                entry.RouteFailed = true;
                VRLog.Warn("WorldUI", $"Video depth layer: RT creation failed for '{entry.Source.name}' " +
                                      "— staying on the mono suspension fallback.");
                return false;
            }

            // Near-plane video draws in FRONT of its host camera's own geometry,
            // far-plane BEHIND it — mirror that ordering with the hook point so the
            // per-RT composite matches what the backbuffer showed.
            entry.CbEvent = player.renderMode == VideoRenderMode.CameraFarPlane
                ? CameraEvent.BeforeForwardOpaque
                : CameraEvent.AfterForwardAlpha;
            entry.RoutedOriginalMode = player.renderMode;
            entry.RoutedOriginalCamera = player.targetCamera;

            entry.SourceCb = new CommandBuffer { name = "GloomhavenVR.VideoDepth.Left" };
            entry.MirrorCb = new CommandBuffer { name = "GloomhavenVR.VideoDepth.Right" };
            entry.Source.AddCommandBuffer(entry.CbEvent, entry.SourceCb);
            entry.Mirror.AddCommandBuffer(entry.CbEvent, entry.MirrorCb);
            entry.VideoRt = rt;

            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = rt;
            entry.Routed = true;
            entry.BuiltShiftUv = float.MinValue; // force the first blit build

            VRLog.Info("WorldUI", $"Video depth layer ENGAGED for '{entry.Source.name}': player " +
                                  $"re-routed {entry.RoutedOriginalMode} → RenderTexture ({width}x{height}); " +
                                  $"both eyes composite it with ±{_videoShiftUv:F4} UV shift — video reads " +
                                  "behind the screen plane, UI stays on it.");
            return true;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("WorldUI", $"Video depth layer: re-routing '{entry.Source.name}' failed ({ex.Message}) " +
                                  "— restoring the player, mono suspension fallback takes over.");
            entry.RouteFailed = true;
            RestoreVideoRoute(entry, "route failed"); // unwinds partial state too
            return false;
        }
    }

    /// <summary>
    /// (Re)build both per-eye blits when the derived shift changed (config edits,
    /// IPD refresh). Behind-the-screen (uncrossed) disparity displaces each eye's
    /// IMAGE toward that eye — left RT −x, right RT +x — and a blit offset moves
    /// the SAMPLING window, i.e. the negative of the image shift, on top of the
    /// centered overscan margin.
    /// </summary>
    private void UpdateVideoBlits(MirrorEntry entry)
    {
        if (entry.SourceCb == null || entry.MirrorCb == null || entry.VideoRt == null)
            return;
        float shift = _videoShiftUv;
        if (Mathf.Abs(shift - entry.BuiltShiftUv) < 1e-5f)
            return;
        entry.BuiltShiftUv = shift;

        float zoom = 1f / VideoOverscan;
        var scale = new Vector2(zoom, zoom);
        float margin = (1f - zoom) * 0.5f;
        entry.SourceCb.Clear();
        entry.SourceCb.Blit(entry.VideoRt, BuiltinRenderTextureType.CameraTarget,
            scale, new Vector2(margin + shift, margin));
        entry.MirrorCb.Clear();
        entry.MirrorCb.Blit(entry.VideoRt, BuiltinRenderTextureType.CameraTarget,
            scale, new Vector2(margin - shift, margin));
    }

    /// <summary>
    /// Tear the route down and give the player its native camera-plane binding back.
    /// Idempotent and partial-state safe (also the TryRouteVideo catch path): every
    /// piece is released independently, Unity fake-nulls guard destroyed cameras and
    /// players, and only a fully-routed entry restores the player fields.
    /// </summary>
    private static void RestoreVideoRoute(MirrorEntry entry, string reason)
    {
        if (entry.SourceCb != null)
        {
            if (entry.Source != null)
                entry.Source.RemoveCommandBuffer(entry.CbEvent, entry.SourceCb);
            entry.SourceCb.Release();
            entry.SourceCb = null;
        }
        if (entry.MirrorCb != null)
        {
            if (entry.Mirror != null)
                entry.Mirror.RemoveCommandBuffer(entry.CbEvent, entry.MirrorCb);
            entry.MirrorCb.Release();
            entry.MirrorCb = null;
        }
        if (entry.VideoRt != null)
        {
            entry.VideoRt.Release();
            Object.Destroy(entry.VideoRt);
            entry.VideoRt = null;
        }
        if (!entry.Routed)
            return;
        entry.Routed = false;

        VideoPlayer? player = entry.Video;
        if (player != null)
        {
            player.targetTexture = null;
            player.renderMode = entry.RoutedOriginalMode;
            player.targetCamera = entry.RoutedOriginalCamera;
        }
        VRLog.Info("WorldUI", $"Video depth layer RELEASED for " +
                              $"'{(entry.Source != null ? entry.Source.name : "<destroyed>")}' ({reason}) — " +
                              $"player restored to {entry.RoutedOriginalMode}.");
    }

    // ---- per-eye quad texture (MultiPass) ----------------------------------------------------

    /// <summary>
    /// Head-camera pre-render hook: swap the quad material's texture per eye pass.
    /// MultiPass renders the head camera twice per frame with
    /// <c>stereoActiveEye</c> = Left/Right; if a runtime ever reports Mono instead,
    /// a per-frame pass-parity fallback (first pass = Left) takes over seamlessly.
    /// Nothing is restored afterwards — the desktop mirror blits the left RT
    /// directly and every pass re-asserts its own texture.
    /// </summary>
    private void OnPreRenderCamera(Camera cam)
    {
        if (!_active)
            return;
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null || cam != head)
            return;
        Material? mat = _quadMaterial;
        if (mat == null)
            return;

        Camera.MonoOrStereoscopicEye eye = cam.stereoActiveEye;
        bool right;
        if (eye == Camera.MonoOrStereoscopicEye.Right)
        {
            right = true;
        }
        else if (eye == Camera.MonoOrStereoscopicEye.Left)
        {
            right = false;
        }
        else
        {
            // Mono reported (unexpected for the stereo head) — pass-parity fallback.
            if (_parityFrame != Time.frameCount)
            {
                _parityFrame = Time.frameCount;
                _parityIndex = 0;
            }
            right = (_parityIndex & 1) == 1;
            _parityIndex++;
        }

        // One-time diagnostic (per activation): the observed eye-pass pattern proves
        // in the log whether stereoActiveEye is trustworthy on this runtime.
        if (_eyeObsCount < 2)
        {
            if (_eyeObsCount == 0) _eyeObs0 = eye; else _eyeObs1 = eye;
            _eyeObsCount++;
            if (_eyeObsCount == 2)
                VRLog.Info("WorldUI", $"Stereo screen eye passes observed: {_eyeObs0}, {_eyeObs1} — " +
                                      (_eyeObs0 == Camera.MonoOrStereoscopicEye.Left
                                       && _eyeObs1 == Camera.MonoOrStereoscopicEye.Right
                                          ? "stereoActiveEye drives the per-eye texture swap."
                                          : "using pass-parity fallback where Mono is reported."));
        }

        RenderTexture? target = right && !_videoSuspended && _rtRight != null ? _rtRight : _leftRt;
        if (target != null && !ReferenceEquals(mat.mainTexture, target))
            mat.mainTexture = target;
    }

    // ---- IPD -------------------------------------------------------------------------------

    /// <summary>
    /// Sample the real eye separation from the XR head device every ~90 frames
    /// (allocation-free feature reads). Plausibility-clamped; falls back to 63 mm
    /// when the runtime does not expose per-eye positions.
    /// </summary>
    private void SampleIpd()
    {
        if (Time.frameCount - _ipdFrame < IpdSampleIntervalFrames && _ipdFrame != int.MinValue)
            return;
        _ipdFrame = Time.frameCount;

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!device.isValid)
            device = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (device.isValid
            && device.TryGetFeatureValue(CommonUsages.leftEyePosition, out Vector3 left)
            && device.TryGetFeatureValue(CommonUsages.rightEyePosition, out Vector3 right))
        {
            float ipd = Vector3.Distance(left, right);
            if (ipd > 0.045f && ipd < 0.08f)
            {
                if (!_ipdLogged)
                {
                    _ipdLogged = true;
                    VRLog.Info("WorldUI", $"Stereo screen IPD from HMD: {ipd * 1000f:F1} mm.");
                }
                _ipdMeters = ipd;
                return;
            }
        }
        if (!_ipdLogged)
        {
            _ipdLogged = true;
            VRLog.Info("WorldUI", "Stereo screen IPD not exposed by the runtime — using the " +
                                  $"{DefaultIpdMeters * 1000f:F0} mm default.");
        }
    }
}
