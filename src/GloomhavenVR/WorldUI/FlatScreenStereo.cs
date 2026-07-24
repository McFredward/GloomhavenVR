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
/// PER-EYE RENDERING (MultiPass): the background cameras keep rendering the LEFT RT
/// exactly as before (this is also what the desktop mirror blits — the monitor stays
/// monoscopic). For every captured 3D camera ONE mod-owned MIRROR camera renders the
/// RIGHT RT: offset by the eye separation along the source camera's +right, with an
/// off-axis projection shift (lens shift, no toe-in — toe-in causes vertical
/// parallax) that converges both eyes at the screen's own distance. Scene content AT
/// the convergence distance sits exactly on the quad; farther content recedes BEHIND
/// it (window/portal look); nearer content pops slightly out.
///
/// ONLY 3D CAMERAS ARRIVE HERE (hardware test #18): Screen-Space-Camera canvases
/// render exclusively through their assigned camera — a mirror can NEVER reproduce
/// them, so the pre-#18 zero-offset "MONO mirror" path for UI/orthographic cameras
/// produced an EMPTY right-eye UI (menu left-eye-only once the video depth layer
/// replaced the suspension that had masked it). <see cref="FlatScreen"/>'s SCREEN
/// LAYER SPLIT now retargets UI cameras onto its transparent glass RT (identical in
/// both eyes at the screen plane) and syncs ONLY the 3D background cameras into this
/// class; while the split is not active, FlatScreen keeps the whole stereo feature
/// off (single mono RT — degraded but never one-eyed).
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
/// PER-EYE QUAD TEXTURE (MultiPass): the head camera renders the background quad
/// once per eye pass; a <see cref="Camera.onPreRender"/> hook swaps that quad
/// material's mainTexture per pass from <c>camera.stereoActiveEye</c> (Left → RT-L,
/// Right → RT-R). Should a runtime ever report Mono there, a per-frame pass-parity
/// fallback (first pass = Left) takes over automatically; the observed pattern is
/// logged once.
///
/// VIDEO DEPTH SHIFT (hardware tests #17-#21): VideoPlayers in CameraNearPlane/
/// FarPlane mode blit decoded frames into their host camera's render target only — a
/// mirror camera can NEVER reproduce them (the player is bound to one camera), so
/// the right eye would miss the video entirely. Re-routing the PLAYER instead failed
/// on hardware twice: a mid-play renderMode/targetTexture switch never re-opens its
/// internal render path (test #18: mod RT black in both eyes), and the APIOnly
/// re-kick left the player prepared-but-never-playing (test #21: isPlaying=False,
/// frame -1 — and with the vanilla far-plane path replaced, the video reached NO eye:
/// black menu). The player is therefore left in its VANILLA mode, untouched, forever
/// — the video provably reaches the left RT through the normal camera-plane render.
///
/// Since the screen layer split, the background RT contains ONLY 3D cameras +
/// camera-plane videos (the UI lives on the glass RT), so while a camera-plane video
/// is ACTIVE on a captured background camera the whole background is treated as one
/// flat plane: the mirrors stop rendering and both eyes show SHIFTED COPIES of the
/// left RT — one full-RT blit per eye whose sampling window is displaced by the
/// ±<see cref="_videoShiftUv"/> disparity (behind-the-screen/uncrossed: each eye's
/// IMAGE moves toward that eye; a blit offset moves the SAMPLING window, i.e. the
/// negative of the image shift — left eye samples at +shift, right at −shift). The
/// entire background (during menu videos essentially just the video) reads
/// [WorldUI] VideoDepth meters BEHIND the glass UI in both eyes; the fixed ~3 %
/// overscan zoom keeps the shifted window inside the RT so edges never show void.
/// A scene with BOTH an active camera-plane video AND other 3D cameras gets the
/// uniform shift too (their mirrors disabled for the period): a mirror cannot
/// reproduce the video, so mirror parallax and a video base layer can never compose
/// into one consistent right RT — uniform recession of the whole composite is
/// correct-enough and artifact-free. 3D cameras WITHOUT any active video keep the
/// true per-eye mirror parallax path (guildmaster town etc.).
///
/// LEFT EYE VIA INTERMEDIATE BLIT (not a quad-material UV offset): the screen
/// quad's shader comes from a runtime fallback chain (Hidden/BlitCopy →
/// Sprites/Default → UI/Default; see FlatScreen.Show) and Sprites/Default ignores
/// _MainTex_ST — a mainTextureOffset shift would silently no-op there, leaving the
/// left eye unshifted (halved, asymmetric depth). The intermediate blit is
/// shader-independent, keeps the onPreRender hook to pure texture swaps (no mutable
/// material state to restore on disengage), and costs one extra full-RT blit per
/// frame — strictly cheaper than the per-camera mirror renders the shift disables
/// while active. Both per-eye blits run once per frame in the head camera's
/// pre-render hook, so they copy the freshest available left RT.
///
/// Disparity math (real meters, screen-plane geometry): eyes converged on the
/// screen at distance D see a plane at D+V with on-screen disparity
/// p = IPD·V/(D+V) — right eye's image displaced toward the right, left toward the
/// left (each eye toward its own side = behind the screen). p is strictly below
/// the IPD (~6.3 cm divergence limit) for any finite V; the explicit 55 mm clamp
/// only engages once ScreenDepthStrength ≳ 1.5. Test #19 ("depth too subtle to
/// notice"): the default V is now 2.2 m — at the default D = 1.6 m screen the
/// video reads at 3.8 m ≈ 2.4× the screen distance, p ≈ 36 mm (was V = 0.8 m,
/// 2.4 m ≈ 1.5×, ~21 mm). UV shift per eye = (p/2)/ScreenWidth.
///
/// FALLBACK SUSPENSION: if the shift path is unavailable (no left RT, shifted-RT
/// creation failure, VideoDepth 0 → zero shift) — or [WorldUI] VideoDepthLayer is
/// off — stereo is SUSPENDED while the video runs: both eye passes show the left RT
/// and the mirrors stop rendering. The vanilla player keeps drawing into the left RT
/// no matter what, so the result is never one-eyed and never black. Logged on every
/// flip.
///
/// INTRO GUARD (hardware test #17: one-eyed intro): the intro's render path is NOT
/// mirror-reproducible-by-construction and NOT observable — decompiled
/// GH.Runtime/IntroPlayer.cs only ever calls <c>_player.Play()</c>; the
/// VideoPlayer's renderMode/camera binding and the logo canvas mode are
/// scene-serialized in Intro.unity (no code to verify against), and the test-#17
/// log shows the player never surfaces as camera-plane (no discovery/suspension
/// line during the whole intro) while the right eye still misses the video — the
/// frames reach the left RT through some camera-bound path the 'Main Camera'
/// mirror cannot replay. While the screen shows a PRE-MENU scene, stereo is
/// therefore force-suspended: both eyes = the left RT, ZERO shift — the intro is
/// flat 2D content and must remain verified-identical in both eyes.
///
/// VIDEO DISCOVERY (hardware test #16, one-eyed intro): a one-shot GetComponent at
/// mirror creation is NOT enough — the intro's player binds to its camera via
/// <c>VideoPlayer.targetCamera</c> from a DIFFERENT GameObject (the intro 'Camera'
/// mirrors as 3D with no player, no suspension → right eye black). Two throttled
/// recovery paths keep the shift/suspension correct without per-frame cost: entries
/// whose cached player is null re-run GetComponent every ~15 frames (players added
/// to the camera GO after capture), and a global FindObjectsOfType sweep every ~30
/// frames matches camera-plane players to captured cameras by targetCamera OR host
/// GO. Late discoveries are logged once per player. The activity check itself also
/// verifies the binding still points at the source, so a player re-targeted to an
/// uncaptured camera stops shifting stereo.
///
/// LIFECYCLE: everything is mod-owned under one hidden DontDestroyOnLoad root.
/// Mirrors die with the captured stack (<see cref="ReleaseMirrors"/> on scene
/// change) and are rebuilt on the next capture sweep — late arrivals get a mirror
/// the tick they are captured. <see cref="Deactivate"/> (screen hide, VR off, config
/// off, hot reload via FlatScreen.Shutdown→Hide) destroys mirrors + both mod RTs,
/// unhooks onPreRender and restores the quad texture to the left RT. With
/// <c>[WorldUI] StereoScreen=false</c> (or strength 0) nothing is ever created —
/// the mono path is untouched.
///
/// PERF: while active, every captured camera renders twice (once per RT). That
/// doubles MENU-scene rendering only (the screen is a menu/modal surface), and both
/// the shift and the suspension path disable the mirrors exactly when videos
/// already dominate — the shift's two full-RT blits per frame are far cheaper than
/// the camera renders they replace. No per-frame allocations: mirror sync is field
/// copies; records allocate only when a NEW camera is first mirrored.
///
/// MAP BASE CAPTURE (hardware log builds e6e6bffa1 → 892455e8a: the CampaignMap read
/// BLACK where the map should be). WHAT ACTUALLY DRAWS THE MAP (decompiled, confirmed):
/// the campaign map parchment is ORDINARY MESH GEOMETRY — MapChoreographer.worldMap, a
/// MeshRenderer with GH_WorldMap_01..04 materials — NOT an image effect, command buffer,
/// render-texture, or screen-space canvas. So the earlier "the map is a post-processing
/// effect a mirror can't reproduce" theory was WRONG.
///
/// Why every earlier attempt still went black — TWO independent causes, both now covered:
///   (1) The game 'MapCamera' (perspective, mask 0xF00FFE37, depth −1) renders ALL-BLACK
///       into the base RT once its <c>targetTexture</c> is redirected onto our RT (log
///       max channel 1/255). So the LEFT eye / mono / desktop, which read the base RT,
///       are black.
///   (2) MapCamera's mask 0xF00FFE37 EXCLUDES the parchment's layer (excluded layers:
///       3,6,7,8,20-27). The first attempt's rescue mirror copied that same mask, so it
///       too skipped the parchment — the RIGHT eye showed the greyish NON-parchment
///       background but a BLACK map region, and driving the left eye from an identical
///       mirror simply made BOTH eyes black over the map.
///
/// FIX: when the base RT is detected black while a 3D background camera renders, the mod
/// renders the map scene ITSELF — a zero-offset bare clone of the source camera with a
/// WIDENED culling mask (all layers minus the mod layer, so the parchment layer is always
/// included) and no image effect — into <see cref="_rtLeft"/>. That mod render is
/// guaranteed non-black. Both eyes are then driven from it through the VIDEO DEPTH SHIFT
/// path (both eyes = ±UV-shifted copies of _rtLeft, converged at the screen), giving the
/// map the same 3D-window parallax the menu video gets — the KNOWN-WORKING stereo route,
/// not a geometry mirror pair. If the shift path is unavailable (VideoDepthLayer off /
/// VideoDepth 0 / shifted-RT failure) both eyes fall back to _rtLeft MONOSCOPICALLY —
/// non-black either way (correctness first, 3D second). Cost: the scene's own colour
/// grading (already lost — it was black) plus one extra scene render while engaged.
///
/// Detection is a throttled, 8x8-downsampled, ASYNC non-black probe of the base RT
/// (<see cref="AsyncGPUReadback"/> — no GPU stall) requiring several consecutive black
/// reads, so no currently-working scene (menu video, guildmaster town whose camera
/// renders fine) is ever switched. Engagement is sticky for the scene (the game base RT
/// stays black) and re-arms on the next captured-stack release. [WorldUI]
/// ScreenLeftMirrorFallback off = legacy (black map if the game camera renders black).
/// NOTE: this covers the STEREO path; with StereoScreen off the mono single-RT path
/// still shows the game camera's (black) render — the default is stereo on.
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
    /// UV zoom on the video depth shift blits (class doc VIDEO DEPTH SHIFT): each
    /// per-eye copy samples a window this factor smaller than the full left RT, so
    /// the disparity shift never drags the sampling window off the RT (void at the
    /// edges). Margin per side = (1 − 1/1.03)/2 ≈ 0.0146 UV. Re-derived for the
    /// test-#19 defaults (V = 2.2 m, D = 1.6 m, W = 2.2 m): default shift =
    /// (p/2)/W ≈ 0.036/2/2.2 ≈ 0.0083 (1.75× headroom); the worst CLAMPED shift
    /// (55 mm → 0.0125) still fits. Narrower configured ScreenWidths hit the
    /// margin cap in <see cref="ComputeVideoShiftUv"/> first — depth silently
    /// saturates there, void is impossible by construction.
    /// </summary>
    private const float VideoOverscan = 1.03f;
    /// <summary>
    /// Ceiling on the total video disparity in real meters — below the ~6.3 cm
    /// divergence limit. Geometry alone (p = IPD·V/(D+V)) can never reach the IPD;
    /// this guards ScreenDepthStrength values ≳ 1.5 scaling p past it (at the
    /// default V/D the geometric p is already ~36 mm).
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
    private static ConfigEntry<bool>? s_leftMirrorFallback;

    // ---- map base capture (class doc MAP BASE CAPTURE) -------------------------------------
    /// <summary>Downsample resolution of the base-RT non-black probe (NxN texels, max-reduced).</summary>
    private const int BlackProbeSize = 8;
    /// <summary>Frames between async non-black probes of the base RT (while not yet engaged).</summary>
    private const int BlackProbeIntervalFrames = 30;
    /// <summary>Consecutive all-black probe results before map base capture engages (transient guard).</summary>
    private const int BlackConsecutiveToEngage = 3;
    /// <summary>Max 0..255 channel value still counted as "black" (guards a near-black graded frame).</summary>
    private const int BlackChannelThreshold = 6;

    /// <summary>One mod-owned mirror camera shadowing a captured game camera into the right RT.</summary>
    private sealed class MirrorEntry
    {
        public Camera Source = null!;
        public Camera Mirror = null!;
        public Transform MirrorTransform = null!;
        public GameObject Go = null!;
        // Map base-capture camera (class doc MAP BASE CAPTURE) — created lazily only once the
        // base RT is found to render black; a zero-offset bare clone of the source.
        public Camera? MirrorLeft;
        public Transform? MirrorLeftTransform;
        public GameObject? GoLeft;
        /// <summary>
        /// Camera-plane VideoPlayer bound to the source (video depth shift trigger) —
        /// hosted on its GO or targeting it via targetCamera; discovered at mirror
        /// creation or later by the throttled recheck/sweep (class doc VIDEO DISCOVERY).
        /// </summary>
        public VideoPlayer? Video;
        // Per-tick mark-and-sweep + video inputs (written by SyncCamera, read by EndStackSync).
        public bool Synced;
        public bool SourceOn;
        public bool VideoActive;
    }

    private readonly List<MirrorEntry> _mirrors = new(8);
    private readonly Dictionary<Camera, MirrorEntry> _bySource = new();
    private readonly Camera.CameraCallback _preRenderHook;

    private GameObject? _root;
    private RenderTexture? _rtRight;
    /// <summary>
    /// Left eye's target while the video depth shift is engaged: a shifted copy of
    /// the left RT (class doc LEFT EYE VIA INTERMEDIATE BLIT). The left RT itself is
    /// never written — it stays the desktop mirror's and the suspension's pristine
    /// source.
    /// </summary>
    private RenderTexture? _rtLeftShifted;
    private RenderTexture? _leftRt;
    private Material? _quadMaterial;

    // ---- map base capture (class doc MAP BASE CAPTURE) -------------------------------------
    /// <summary>
    /// The mod's own render of the map scene while <see cref="_mapBaseCapture"/> — a bare
    /// clone of the base 3D camera rendered with a WIDENED culling mask (all layers minus
    /// the mod layer) so it captures the map parchment even though the game MapCamera's own
    /// mask (0xF00FFE37) excludes that layer. Fed into the video depth-shift path so both
    /// eyes show this non-black map (class doc MAP BASE CAPTURE).
    /// </summary>
    private RenderTexture? _rtLeft;
    /// <summary>Small RT the base RT is downsampled into for the async non-black probe.</summary>
    private RenderTexture? _probeRt;
    /// <summary>True once the base RT was found black — both eyes are driven by the mod's own widened-mask map render, not the game render.</summary>
    private bool _mapBaseCapture;
    /// <summary>Map-base-capture RT creation failed this activation → stay on the game render (avoid retry spam).</summary>
    private bool _mapBaseCaptureFailed;
    private int _blackProbeFrame = int.MinValue;
    private int _blackConsecutive;
    /// <summary>True while an async base-RT probe is in flight (one at a time).</summary>
    private bool _probePending;
    /// <summary>Bumped on teardown/scene release so a late async probe callback ignores stale results.</summary>
    private int _probeGen;
    private int _probeReqGen;

    private bool _active;
    private bool _hooked;
    private bool _videoSuspended;
    /// <summary>True while both eyes show shifted copies of the left RT (class doc VIDEO DEPTH SHIFT).</summary>
    private bool _videoShift;
    /// <summary>Shifted-RT creation failed this activation — plain suspension, no retry spam.</summary>
    private bool _shiftRtFailed;
    /// <summary>Frame the per-eye shift blits last ran (once per frame, first head pass).</summary>
    private int _shiftBlitFrame = -1;

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
    /// <summary>
    /// One-line-per-gate guard for the sweep's early-outs (test #20 diagnostic: every
    /// silent skip names itself ONCE). Keyed per player instance ID plus a small
    /// per-gate discriminant (e.g. the renderMode), so a player that CHANGES mode
    /// re-logs with the new state.
    /// </summary>
    private readonly HashSet<long> _videoGateLogged = new();

    /// <summary>Eye separation / convergence distance in CAPTURED-SCENE units (recomputed per tick).</summary>
    private float _sepScene;
    private float _convScene;

    /// <summary>Per-eye UV shift of the video depth shift (recomputed per tick; 0 = plane depth).</summary>
    private float _videoShiftUv;

    /// <summary>True while the screen shows a pre-menu scene (class doc INTRO GUARD).</summary>
    private bool _introGuard;

    // onPreRender eye-pass bookkeeping (all fixed fields — the hook must not allocate).
    private int _parityFrame = -1;
    private int _parityIndex;
    private int _eyeObsCount;
    private Camera.MonoOrStereoscopicEye _eyeObs0;
    private Camera.MonoOrStereoscopicEye _eyeObs1;

    /// <summary>True while per-eye rendering is engaged (right RT + hook exist).</summary>
    internal bool Active => _active;

    /// <summary>
    /// True while both eye passes show the LEFT RT (video fallback / intro guard).
    /// <see cref="FlatScreen"/> reads this after <see cref="EndStackSync"/> to fold
    /// the UI back into the left RT while the single suspended image must carry
    /// everything (its SCREEN LAYER SPLIT class doc). The video depth SHIFT is NOT a
    /// suspension: the split keeps routing and the glass UI stays in front.
    /// </summary>
    internal bool Suspended => _videoSuspended;

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
            "ambient movie, story videos): the VideoPlayer stays untouched in its vanilla " +
            "camera-plane mode and BOTH eyes show slightly shifted copies of the captured " +
            "background, so the whole background (essentially just the video then) reads " +
            "VideoDepth meters BEHIND the glass UI — background recedes, menu floats in " +
            "front, no artificial geometry. Off = stereo is fully suspended (mono) while " +
            "any camera-plane video plays.");
        s_videoDepth = file.Bind("WorldUI", "VideoDepth", 2.2f,
            "How far BEHIND the screen plane the background reads while a fullscreen 2D " +
            "video plays, in real meters (VideoDepthLayer). Disparity p = IPD*V/(D+V) with " +
            "D = ScreenDistance: at the 1.6 m default screen and 2.2 m depth the video reads " +
            "at 3.8 m (~2.4x the screen distance, ~36 mm disparity — below the ~63 mm " +
            "divergence limit; clamped to 55 mm regardless). Raised from 0.8 after test #19 " +
            "(the recession read too subtle). 0 = video on the screen plane (no video depth).");
        s_parallaxScale = file.Bind("WorldUI", "ScreenParallaxScale", 6.0f,
            "Amplifies the stereo screen's scene-INTERNAL depth (test #16: far menu scenery " +
            "read flat at geometric settings). Separation AND convergence are multiplied by " +
            "the same factor, so the at-infinity disparity (their ratio) stays constant and " +
            "comfortable while depth differences inside the captured scene grow this many " +
            "times stronger — diorama-behind-glass instead of flat photo. 1 = strict window " +
            "geometry; clamped to 1-60.");
        s_leftMirrorFallback = file.Bind("WorldUI", "ScreenLeftMirrorFallback", true,
            "Rescue the map when a captured 3D scene renders BLACK into the screen's render " +
            "texture (the campaign map: the game MapCamera renders all-black once its target " +
            "is redirected onto our RT, AND its culling mask excludes the map parchment's " +
            "layer). When the base RT is detected black, the mod renders the map scene itself " +
            "with a WIDENED culling mask into its own texture and drives BOTH eyes from it " +
            "through the depth-shift path — a non-black 3D-window map (mono non-black if the " +
            "shift is unavailable) instead of a black void. Costs that scene's own colour " +
            "grading (already lost — it was black) plus one extra render while engaged. " +
            "Off = legacy (black map if the game camera renders black).");
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
    /// <paramref name="introActive"/> = the screen currently shows a pre-menu scene
    /// (class doc INTRO GUARD — forces the suspension).
    /// </summary>
    internal void Tick(RenderTexture? leftRt, Renderer? quadRenderer, bool introActive)
    {
        _leftRt = leftRt;
        _quadMaterial = quadRenderer != null ? quadRenderer.sharedMaterial : null;
        _introGuard = introActive;

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

        // Map base-capture RT tracks the base RT's dimensions (class doc MAP BASE
        // CAPTURE); only kept alive while engaged.
        if (_rtLeft != null && (_rtLeft.width != leftRt!.width || _rtLeft.height != leftRt.height))
            ReleaseLeftRt();
        if (_mapBaseCapture && _rtLeft == null)
            EnsureLeftRt();

        if (!_active)
        {
            _active = true;
            if (!_hooked)
            {
                Camera.onPreRender += _preRenderHook;
                _hooked = true;
            }
            _eyeObsCount = 0; // re-log the observed eye-pass pattern per activation
            _shiftRtFailed = false; // a failed shifted RT gets a fresh chance per activation
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
    /// Per-eye UV shift for the video depth shift (class doc VIDEO DEPTH SHIFT —
    /// disparity math). Pure screen-plane geometry in REAL meters, so no WorldScale
    /// or parallax factor applies: p = IPD·strength·V/(D+V), clamped below the
    /// divergence limit AND below the overscan margin (a larger shift would sample
    /// past the RT edge — depth silently caps instead of showing void).
    /// </summary>
    private float ComputeVideoShiftUv()
    {
        float depth = Mathf.Clamp(s_videoDepth?.Value ?? 2.2f, 0f, 5f);
        if (depth <= 0f)
            return 0f;
        float distance = Mathf.Max(0.1f, WorldUIConfig.ScreenDistance.Value);
        float disparity = Mathf.Min(
            _ipdMeters * DepthStrength * depth / (distance + depth), MaxVideoDisparityMeters);
        float halfUv = 0.5f * disparity / Mathf.Max(0.5f, WorldUIConfig.ScreenWidth.Value);
        return Mathf.Min(halfUv, (1f - 1f / VideoOverscan) * 0.5f);
    }

    /// <summary>Full teardown: mirrors, mod RTs, render hook; quad texture back to the left RT.</summary>
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
        ReleaseShiftRt();
        ReleaseLeftRt();
        ReleaseProbeRt();
        _probeGen++;                 // invalidate any in-flight probe callback
        _probePending = false;
        _mapBaseCapture = false;
        _mapBaseCaptureFailed = false;
        _blackConsecutive = 0;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        if (_active)
        {
            _active = false;
            _videoSuspended = false;
            _videoShift = false;
            _shiftRtFailed = false;
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

    private void ReleaseShiftRt()
    {
        if (_rtLeftShifted == null)
            return;
        _rtLeftShifted.Release();
        Object.Destroy(_rtLeftShifted);
        _rtLeftShifted = null;
    }

    // ---- map base capture: RT (class doc MAP BASE CAPTURE) ---------------------------------

    /// <summary>Create the map base-capture RT matching the base RT (returns false on failure).</summary>
    private bool EnsureLeftRt()
    {
        if (_leftRt == null)
            return false;
        if (_rtLeft != null
            && (_rtLeft.width != _leftRt.width || _rtLeft.height != _leftRt.height))
            ReleaseLeftRt();
        if (_rtLeft == null)
        {
            var rt = new RenderTexture(_leftRt.width, _leftRt.height, 24)
            {
                name = "GloomhavenVR.FlatScreenRT.Left",
                antiAliasing = 1,
            };
            if (!rt.Create())
            {
                Object.Destroy(rt);
                return false;
            }
            _rtLeft = rt;
            ClearOpaqueBlack(_rtLeft); // fresh RT color is undefined — no garbage before the first render
        }
        return true;
    }

    private void ReleaseLeftRt()
    {
        if (_rtLeft == null)
            return;
        _rtLeft.Release();
        Object.Destroy(_rtLeft);
        _rtLeft = null;
    }

    private void ReleaseProbeRt()
    {
        if (_probeRt == null)
            return;
        _probeRt.Release();
        Object.Destroy(_probeRt);
        _probeRt = null;
    }

    private static void ClearOpaqueBlack(RenderTexture rt)
    {
        RenderTexture? previous = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(clearDepth: true, clearColor: true, backgroundColor: new Color(0f, 0f, 0f, 1f));
        RenderTexture.active = previous;
    }

    // ---- per-tick stack sync ---------------------------------------------------------------

    /// <summary>Mark-and-sweep begin: every entry must be re-claimed by <see cref="SyncCamera"/>.</summary>
    internal void BeginStackSync()
    {
        for (int i = 0; i < _mirrors.Count; i++)
            _mirrors[i].Synced = false;

        // Late-video throttles (class doc VIDEO DISCOVERY). The global sweep runs
        // FIRST so the SyncCamera calls of this very tick already see a discovered
        // player and can shift/suspend immediately.
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

            if (player.renderMode != VideoRenderMode.CameraNearPlane
                && player.renderMode != VideoRenderMode.CameraFarPlane)
            {
                // Gate diagnostic (test #20): a player bound to a MIRRORED camera in
                // a non-camera-plane mode needs no depth shift BY DESIGN (its frames
                // reach both eyes through the normal camera render) — but if a video
                // ever reads flat/one-eyed on hardware, this line rules the mode in
                // or out. Keyed player+mode so a mode CHANGE re-logs.
                if (entry != null
                    && _videoGateLogged.Add(player.GetInstanceID() * 31L + (int)player.renderMode))
                    VRLog.Info("WorldUI", $"Stereo screen sweep: VideoPlayer on " +
                                          $"'{player.gameObject.name}' is bound to captured " +
                                          $"'{entry.Source.name}' but renderMode {player.renderMode} " +
                                          "is not camera-plane — no depth shift needed/possible.");
                continue;
            }
            if (entry == null)
            {
                // Camera-plane player whose camera we did NOT capture: the depth
                // shift cannot see it and no suspension applies — name it once.
                if (_videoGateLogged.Add(player.GetInstanceID()))
                    VRLog.Info("WorldUI", $"Stereo screen sweep: camera-plane VideoPlayer on " +
                                          $"'{player.gameObject.name}' (targetCamera " +
                                          $"'{(bound != null ? bound.name : "<null>")}') matches no " +
                                          "captured camera — outside the depth shift's reach.");
                continue;
            }
            // Only fill EMPTY slots (Unity fake-null included — a destroyed player is
            // replaced, a live cached one is never thrashed by a second candidate).
            if (entry.Video != null)
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
                              $"'{source.name}' ({how}) — the video depth shift/suspension now " +
                              "applies (without it this video would render in one eye only).");
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
        entry.VideoActive = entry.SourceOn && IsNearPlaneVideoActive(entry.Video, source);
        if (!entry.SourceOn)
            return; // mirror gets disabled in EndStackSync; nothing to copy

        Camera mirror = entry.Mirror;
        Transform st = source.transform;

        // Eye offset: right eye = source pose shifted along the source's +right
        // (only 3D cameras arrive here — the UI lives on FlatScreen's glass layer).
        if (_sepScene > 0f)
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

        // Projection: copy the source matrix plus the off-axis convergence shift
        // (see class doc — lens shift, not toe-in).
        // Derivation: the right camera sits +s along +right; a point straight ahead
        // at the convergence distance D lands at NDC x = -m00*s/D in it, so
        // m02 -= m00*s/D translates the image so that point matches the left eye
        // (zero disparity at D; uncrossed/behind-screen beyond it).
        Matrix4x4 proj = source.projectionMatrix;
        if (_sepScene > 0f && _convScene > 1e-4f)
            proj.m02 -= proj.m00 * (_sepScene / _convScene);
        mirror.projectionMatrix = proj;

        // MAP BASE CAPTURE (class doc): a zero-offset bare clone of the source rendered
        // with a WIDENED culling mask into the mod's own RT (_rtLeft), which the video
        // depth-shift path then samples for BOTH eyes. The map parchment is ordinary mesh
        // geometry (decompiled MapChoreographer.worldMap, GH_WorldMap materials) sitting on
        // a layer the game MapCamera's mask (0xF00FFE37) EXCLUDES — so copying the source
        // mask (the first attempt) reproduced a BLACK map region. Rendering all layers
        // except the mod layer captures the parchment regardless of which layer it is on;
        // the source's UNMODIFIED projection keeps the map framed exactly as the game did.
        if (_mapBaseCapture && _rtLeft != null)
        {
            EnsureLeftMirror(entry);
            Camera lm = entry.MirrorLeft!;
            entry.MirrorLeftTransform!.SetPositionAndRotation(st.position, st.rotation);
            lm.clearFlags = source.clearFlags;
            lm.backgroundColor = source.backgroundColor;
            // WIDENED mask (all layers minus the mod layer) — the core fix: the game
            // MapCamera excludes the parchment's layer, so a source-mask clone renders it
            // black. Our own camera has no image effect either, so the redirect breakage
            // that blacks the game render into the base RT cannot affect it.
            lm.cullingMask = ~VRLayers.ModLayerMask;
            lm.depth = source.depth;
            lm.rect = source.rect;
            lm.nearClipPlane = source.nearClipPlane;
            lm.farClipPlane = source.farClipPlane;
            lm.orthographic = source.orthographic;
            lm.orthographicSize = source.orthographicSize;
            lm.fieldOfView = source.fieldOfView;
            lm.allowHDR = source.allowHDR;
            lm.allowMSAA = source.allowMSAA;
            lm.useOcclusionCulling = source.useOcclusionCulling;
            lm.projectionMatrix = source.projectionMatrix;
            if (lm.targetTexture != _rtLeft)
                lm.targetTexture = _rtLeft;
        }
    }

    /// <summary>
    /// Mark-and-sweep end: drop mirrors whose source died/left the stack, engage the
    /// video depth shift while any camera-plane video is active (class doc VIDEO
    /// DEPTH SHIFT), decide the fallback suspension for THIS frame, and apply the
    /// enabled state (mirrors render only while neither shift nor suspension runs).
    /// </summary>
    internal void EndStackSync()
    {
        if (!_active)
            return;

        bool anyVideo = false;
        string? videoSource = null;
        for (int i = _mirrors.Count - 1; i >= 0; i--)
        {
            MirrorEntry entry = _mirrors[i];
            if (!entry.Synced || entry.Source == null)
            {
                DestroyMirrorAt(i);
                continue;
            }
            if (entry.VideoActive && !anyVideo)
            {
                anyVideo = true;
                videoSource = entry.Source.name;
            }
        }

        // Decision (class doc): the intro guard forces the suspension outright —
        // zero shift, the intro must remain verified-identical. Otherwise an active
        // camera-plane video OR the map base capture engages the uniform shift; if the
        // shift path is unavailable (kill switch / zero depth / no shifted RT) the
        // suspension fallback takes over — never one-eyed, never black (the mod's own
        // map render / the vanilla player keeps feeding the shift source either way).
        // MAP BASE CAPTURE (class doc): the map's effect-less mesh is rendered by our own
        // widened-mask camera into _rtLeft; routing it through the SAME shift path the
        // menu video uses gives both eyes a non-black 3D-window map (task step 3).
        bool depthLayer = s_videoDepthLayer?.Value ?? true;
        string? shiftSource = videoSource ?? (_mapBaseCapture ? "map base capture" : null);
        bool shift = false;
        bool suspend = false;
        string? suspendWhy = null;
        if (_introGuard)
        {
            suspend = true;
        }
        else if (anyVideo || _mapBaseCapture)
        {
            if (!depthLayer)
                suspendWhy = "VideoDepthLayer disabled";
            else if (_videoShiftUv <= 0f)
                suspendWhy = "VideoDepth is 0 (content on the screen plane = plain mono)";
            else if (!EnsureShiftRt())
                suspendWhy = "shifted-RT unavailable";
            else
                shift = true;
            suspend = !shift;
        }

        if (shift != _videoShift)
        {
            _videoShift = shift;
            VRLog.Info("WorldUI", shift
                ? $"Depth shift ENGAGED for '{shiftSource}': shift source untouched (mod map " +
                  "render into _rtLeft for the map base capture; vanilla camera-plane video into " +
                  "the left RT otherwise); mirrors off, both eyes show shifted copies of it " +
                  $"(±{_videoShiftUv:F4} UV, {VideoOverscan:F2}x overscan) — the background/map " +
                  "reads behind the glass UI."
                : "Depth shift RELEASED — no camera-plane video / map base capture active; " +
                  "per-eye mirror parallax re-engages.");
        }

        if (suspend != _videoSuspended)
        {
            _videoSuspended = suspend;
            VRLog.Info("WorldUI", suspend
                ? (_introGuard
                    ? "Stereo screen SUSPENDED — intro guard: pre-menu scenes force identical " +
                      "eyes (both eyes = left RT, zero shift; the intro must remain " +
                      "verified-identical)."
                    : "Stereo screen SUSPENDED — the depth shift is unavailable " +
                      $"({suspendWhy}); both eyes show the " +
                      (_mapBaseCapture ? "mod map render (_rtLeft, mono non-black)" : "left RT") +
                      " until it resumes.")
                : "Stereo screen RESUMED — per-eye rendering re-engaged.");
        }

        bool anyMirrorRendering = false;
        for (int i = 0; i < _mirrors.Count; i++)
        {
            MirrorEntry entry = _mirrors[i];
            bool want = entry.SourceOn && !suspend && !shift;
            if (entry.Mirror.enabled != want)
                entry.Mirror.enabled = want;
            if (want)
                anyMirrorRendering = true;

            // The map base-capture camera renders whenever the fallback is engaged and its
            // source is on — INDEPENDENT of the parallax-mirror gate (which is off while the
            // shift runs): it fills _rtLeft, the very source the shift then samples for both
            // eyes. Without this it would go dark exactly when the shift needs it.
            if (entry.MirrorLeft != null)
            {
                bool wantLeft = entry.SourceOn && _mapBaseCapture;
                if (entry.MirrorLeft.enabled != wantLeft)
                    entry.MirrorLeft.enabled = wantLeft;
            }
        }

        // Map base-capture probe (class doc MAP BASE CAPTURE): a 3D background camera is
        // actively rendering (mirrors on) but the game's own render may be black —
        // check the base RT and engage map base capture if so.
        TickBlackProbe(anyMirrorRendering);
    }

    /// <summary>
    /// Ensure the left eye's shifted intermediate RT exists and matches the left
    /// RT's dimensions (the right RT is kept in step by <see cref="Tick"/>). A
    /// creation failure latches <see cref="_shiftRtFailed"/> for this activation —
    /// the caller falls back to the plain suspension.
    /// </summary>
    private bool EnsureShiftRt()
    {
        if (_leftRt == null || _rtRight == null || _shiftRtFailed)
            return false;
        if (_rtLeftShifted != null
            && (_rtLeftShifted.width != _leftRt.width || _rtLeftShifted.height != _leftRt.height))
        {
            ReleaseShiftRt();
        }
        if (_rtLeftShifted == null)
        {
            var rt = new RenderTexture(_leftRt.width, _leftRt.height, 0)
            {
                name = "GloomhavenVR.FlatScreenRT.LeftShifted",
            };
            if (!rt.Create())
            {
                Object.Destroy(rt);
                _shiftRtFailed = true;
                VRLog.Warn("WorldUI", "Video depth shift: left-shifted RT creation failed — " +
                                      "plain suspension fallback (both eyes = left RT; never " +
                                      "one-eyed, never black).");
                return false;
            }
            _rtLeftShifted = rt;
        }
        return true;
    }

    // ---- map base capture: probe + engage (class doc MAP BASE CAPTURE) ----------------------

    /// <summary>
    /// Throttled ASYNC non-black probe of the base RT. Runs only while a 3D background
    /// camera is actively rendering (so the base RT SHOULD hold scene content) and the
    /// fallback is not yet engaged/failed; a stall-free downsample-then-readback that,
    /// after several consecutive all-black results, drives the left eye from mod
    /// mirrors instead of the game camera's (black) render.
    /// </summary>
    private void TickBlackProbe(bool anyMirrorRendering)
    {
        if (_mapBaseCapture || _mapBaseCaptureFailed || !(s_leftMirrorFallback?.Value ?? true))
            return;
        if (!anyMirrorRendering || _leftRt == null || _probePending)
            return;
        if (_blackProbeFrame != int.MinValue
            && Time.frameCount - _blackProbeFrame < BlackProbeIntervalFrames)
            return;
        _blackProbeFrame = Time.frameCount;

        if (_probeRt == null)
        {
            _probeRt = new RenderTexture(BlackProbeSize, BlackProbeSize, 0)
            {
                name = "GloomhavenVR.FlatScreenRT.BlackProbe",
                antiAliasing = 1,
            };
            if (!_probeRt.Create())
            {
                ReleaseProbeRt();
                return;
            }
        }

        // Bilinear downsample to NxN, then read those few texels back off-thread.
        Graphics.Blit(_leftRt, _probeRt);
        _probePending = true;
        _probeReqGen = _probeGen;
        AsyncGPUReadback.Request(_probeRt, 0, TextureFormat.RGBA32, OnBlackProbe);
    }

    private void OnBlackProbe(AsyncGPUReadbackRequest req)
    {
        _probePending = false;
        // Ignore results from a torn-down / superseded activation.
        if (!_active || _mapBaseCapture || _probeReqGen != _probeGen || req.hasError)
            return;

        var data = req.GetData<Color32>();
        int maxChannel = 0;
        for (int i = 0; i < data.Length; i++)
        {
            Color32 c = data[i];
            int m = c.r;
            if (c.g > m) m = c.g;
            if (c.b > m) m = c.b;
            if (m > maxChannel)
                maxChannel = m;
        }

        if (maxChannel <= BlackChannelThreshold)
        {
            _blackConsecutive++;
            if (_blackConsecutive >= BlackConsecutiveToEngage)
                EngageLeftMirror(maxChannel);
        }
        else
        {
            _blackConsecutive = 0;
        }
    }

    /// <summary>
    /// The base RT reads black while a 3D background camera renders — engage MAP BASE
    /// CAPTURE: the mod renders the map scene itself with a WIDENED culling mask into
    /// _rtLeft (created + rendered on the next sweep) and routes it through the depth-shift
    /// path for both eyes. Sticky for the scene (the game render stays black); re-arms on
    /// the next stack release.
    /// </summary>
    private void EngageLeftMirror(int maxChannel)
    {
        if (_mapBaseCapture)
            return;
        if (!EnsureLeftRt())
        {
            _mapBaseCaptureFailed = true;
            VRLog.Warn("WorldUI", "Map base capture: capture RT creation failed — " +
                                  "both eyes stay on the game render (may be black). Retries next activation.");
            return;
        }
        _mapBaseCapture = true;
        VRLog.Info("WorldUI", $"MAP BASE CAPTURE ENGAGED: the screen's base RenderTexture reads " +
                              $"BLACK (max channel {maxChannel}/255 over {BlackProbeSize}x{BlackProbeSize}) while a " +
                              "3D background camera renders. Decompiled evidence: the campaign map parchment is " +
                              "ordinary mesh geometry (MapChoreographer.worldMap, GH_WorldMap materials) on a layer " +
                              "the game MapCamera's mask (0xF00FFE37) EXCLUDES, and MapCamera's own render into the " +
                              "redirected base RT comes out all-black. The mod now renders the map itself with a " +
                              "WIDENED culling mask (all layers minus the mod layer) into _rtLeft and drives BOTH " +
                              "eyes from it through the depth-shift path (3D window; mono non-black if the shift is " +
                              "unavailable). The scene's own colour grading is lost — it was black. Re-arms on the " +
                              "next scene change.");
    }

    /// <summary>Create the lazily-allocated map base-capture camera for this entry (bare, disabled).</summary>
    private void EnsureLeftMirror(MirrorEntry entry)
    {
        if (entry.MirrorLeft != null || _root == null || _rtLeft == null)
            return;
        var go = new GameObject("GloomhavenVR.MapBaseCapture." + entry.Source.name);
        go.transform.SetParent(_root.transform, worldPositionStays: false);
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;                            // EndStackSync flips it on
        cam.stereoTargetEye = StereoTargetEyeMask.None; // VRCameraPolicy-invisible by construction
        cam.targetTexture = _rtLeft;
        XRDevice.DisableAutoXRCameraTracking(cam, true);
        entry.MirrorLeft = cam;
        entry.MirrorLeftTransform = go.transform;
        entry.GoLeft = go;
        VRLog.Info("WorldUI", $"Map base capture: capture camera created for '{entry.Source.name}' " +
                              "(zero offset, unmodified projection, WIDENED culling mask — renders the map " +
                              "into _rtLeft; the depth-shift path drives both eyes from it).");
    }

    /// <summary>Destroy all mirrors (captured stack released — scene change / hide). Cheap to rebuild.</summary>
    internal void ReleaseMirrors()
    {
        for (int i = _mirrors.Count - 1; i >= 0; i--)
            DestroyMirrorAt(i);
        _mirrors.Clear();
        _bySource.Clear();

        // Re-arm map base capture for the next scene (class doc MAP BASE
        // CAPTURE): the evidence (a black base RT) belongs to the scene we just left.
        // The RT is kept (dimensions re-checked in Tick); a late probe callback is
        // invalidated by the generation bump.
        if (_mapBaseCapture || _blackConsecutive != 0)
        {
            _mapBaseCapture = false;
            _blackConsecutive = 0;
            _probeGen++;
            _probePending = false;
            ReleaseLeftRt();
        }
    }

    private void DestroyMirrorAt(int index)
    {
        MirrorEntry entry = _mirrors[index];
        // Unity fake-null: the managed key survives Destroy — Remove still works.
        _bySource.Remove(entry.Source);
        if (entry.Go != null)
            Object.Destroy(entry.Go);
        if (entry.GoLeft != null)
            Object.Destroy(entry.GoLeft);
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
            // 'Video Camera' fullscreen videos) — first shift probe; players added
            // later or bound from another GO via targetCamera (the intro) are
            // caught by the throttled recheck/sweep (class doc VIDEO DISCOVERY).
            Video = source.GetComponent<VideoPlayer>(),
        };
        _mirrors.Add(entry);
        _bySource.Add(source, entry);

        VRLog.Info("WorldUI", $"Stereo mirror created for '{source.name}': eye offset " +
                              $"{_sepScene:F4} scene units, converge {_convScene:F2}" +
                              $"{(entry.Video != null ? ", hosts a VideoPlayer (video depth shift applies)" : "")}.");
        return entry;
    }

    /// <summary>
    /// True while this player blits camera-plane frames into the SOURCE camera's
    /// output. The binding check (targetCamera or same GO) lets a player that gets
    /// re-targeted elsewhere stop shifting stereo without waiting for a sweep.
    /// </summary>
    private static bool IsNearPlaneVideoActive(VideoPlayer? video, Camera source) =>
        video != null && video.enabled
        && (video.renderMode == VideoRenderMode.CameraNearPlane
            || video.renderMode == VideoRenderMode.CameraFarPlane)
        && (video.targetCamera == source || video.gameObject == source.gameObject);

    // ---- per-eye quad texture (MultiPass) ----------------------------------------------------

    /// <summary>
    /// Head-camera pre-render hook: swap the quad material's texture per eye pass.
    /// MultiPass renders the head camera twice per frame with
    /// <c>stereoActiveEye</c> = Left/Right; if a runtime ever reports Mono instead,
    /// a per-frame pass-parity fallback (first pass = Left) takes over seamlessly.
    /// While the video depth shift is engaged, the first pass of each frame also
    /// refreshes BOTH per-eye shifted copies of the left RT (class doc VIDEO DEPTH
    /// SHIFT — the freshest possible copy, one pair of blits per frame).
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

        // Shift source: the mod's own map render (_rtLeft) while the map base capture is
        // engaged — the game's base RT is black there; the camera-plane video's left RT
        // otherwise (class doc MAP BASE CAPTURE / VIDEO DEPTH SHIFT).
        RenderTexture? shiftSrc = _mapBaseCapture && _rtLeft != null ? _rtLeft : _leftRt;
        if (_videoShift && _shiftBlitFrame != Time.frameCount
            && shiftSrc != null && _rtRight != null && _rtLeftShifted != null)
        {
            _shiftBlitFrame = Time.frameCount;
            // Behind-the-screen (uncrossed) disparity displaces each eye's IMAGE
            // toward that eye — left −x, right +x — and a blit offset moves the
            // SAMPLING window, i.e. the negative of the image shift, on top of the
            // centered overscan margin.
            float zoom = 1f / VideoOverscan;
            float margin = (1f - zoom) * 0.5f;
            float shift = _videoShiftUv;
            var scale = new Vector2(zoom, zoom);
            RenderTexture? previous = RenderTexture.active;
            Graphics.Blit(shiftSrc, _rtLeftShifted, scale, new Vector2(margin + shift, margin));
            Graphics.Blit(shiftSrc, _rtRight, scale, new Vector2(margin - shift, margin));
            RenderTexture.active = previous;
        }

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

        // Target per eye: suspension → the shift source for both (the mod map render
        // _rtLeft while the map base capture is engaged so the mono fallback is non-black,
        // else the left RT); depth shift → the per-eye shifted copies; otherwise the
        // mirror-rendered right RT and, for the left eye, the base RT (class doc MAP BASE
        // CAPTURE — the game render is black there, which is why base capture routes
        // through the shift/mono path instead of the base RT).
        RenderTexture? target;
        if (_videoSuspended)
            target = _mapBaseCapture && _rtLeft != null ? _rtLeft : _leftRt;
        else if (_videoShift && _rtLeftShifted != null && _rtRight != null)
            target = right ? _rtRight : _rtLeftShifted;
        else if (right)
            target = _rtRight != null ? _rtRight : _leftRt;
        else
            target = _mapBaseCapture && _rtLeft != null ? _rtLeft : _leftRt;
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
