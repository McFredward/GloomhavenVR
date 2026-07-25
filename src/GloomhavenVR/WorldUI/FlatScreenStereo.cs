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
/// MAP ALBEDO RENDER (the campaign world map read BLACK on the flat screen — the
/// 9-times-failed bug). WHAT ACTUALLY DRAWS THE MAP (decompiled, confirmed): the
/// campaign map parchment is ORDINARY MESH GEOMETRY — <c>MapChoreographer.worldMap</c>,
/// a GameObject with a <c>MeshRenderer</c> whose submesh materials are named
/// <c>GH_WorldMap_01..04</c> (+ <c>_New</c> variants). Each material stores the
/// parchment ALBEDO texture directly — in property <c>_Alb</c> for the <c>_New</c>
/// materials, <c>_MainTex</c> otherwise — so the map does NOT need scene lighting to
/// look right: a top-down painted map is essentially correct rendered UNLIT.
///
/// THE FIX: while the campaign map is showing (detected by the reliable black-probe
/// below — the game 'MapCamera' renders BLACK into any RenderTexture we own), a
/// mod-owned FORWARD camera we fully control renders the worldMap mesh straight into
/// the flat-screen base RT (<see cref="_leftRt"/>, = FlatScreen's <c>_rt</c>). It is
/// cloned from the game MapCamera's transform + projection + mask (plus the worldMap
/// layer), given a solid dark clear, and a depth just ABOVE the game MapCamera so it
/// OWNS the base RT's final content each frame (the game camera's dark render is
/// overwritten). The parchment is drawn UNLIT via a temporary MATERIAL OVERRIDE: one
/// <c>Sprites/Default</c> material per submesh whose <c>_MainTex</c> is the original
/// material's albedo (<c>_Alb</c> ?? <c>_MainTex</c> ?? <c>mainTexture</c>). The
/// override is swapped onto the worldMap MeshRenderer in the mod camera's onPreRender
/// and restored in its onPostRender — scoped to exactly our render, so the game's own
/// state is untouched (rendering-only, MULTIPLAYER-SAFE). The base RT then reads
/// bright, and the mono suspension drives BOTH eyes from it.
///
/// EVERY RT-CAPTURE PATH IS PROVEN DEAD (do not re-attempt): redirecting the game
/// deferred MapCamera's targetTexture onto our RT gives flat unlit murk (a deferred
/// surface is never G-buffer-lit into a redirected off-screen RT); forcing the
/// camera to Forward draws nothing (the map's Amplify <c>Amp_Basic_N_MRAO</c> shader
/// has no usable forward pass); stripping image effects and sRGB/colorspace fixes
/// changed nothing (the rig renders in Gamma colorspace); and a CameraTarget
/// backbuffer grab reads pure black under active MULTIPASS XR. The albedo render
/// sidesteps all of that by never relying on the map's own lighting.
///
/// Detection is a throttled, 8x8-downsampled, ASYNC non-black probe of the base RT
/// (<see cref="AsyncGPUReadback"/> — no GPU stall) requiring several consecutive black
/// reads, so no currently-working scene (menu video, guildmaster town whose camera
/// renders fine) is ever switched. Engagement is sticky for the scene (the game base RT
/// stays black) and re-arms on the next captured-stack release. [WorldUI]
/// ScreenLeftMirrorFallback off = legacy (black map if the game camera renders black);
/// [WorldUI] MapAlbedoRender off = detect but do not render (base RT left as-is).
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
    /// <summary>MAP ALBEDO RENDER (default ON): render the campaign map parchment unlit via a mod forward camera (see MapAlbedoRender config, class doc MAP ALBEDO RENDER).</summary>
    private static ConfigEntry<bool>? s_mapAlbedoRender;

    // ---- map base capture (class doc MAP ALBEDO RENDER) ------------------------------------
    /// <summary>Downsample resolution of the base-RT non-black probe (NxN texels, max-reduced).</summary>
    private const int BlackProbeSize = 8;
    /// <summary>Frames between async non-black probes of the base RT (while not yet engaged).</summary>
    private const int BlackProbeIntervalFrames = 30;
    /// <summary>Consecutive all-black probe results before map base capture engages (transient guard).</summary>
    private const int BlackConsecutiveToEngage = 3;
    /// <summary>Max 0..255 channel value still counted as "black" (guards a near-black graded frame).</summary>
    private const int BlackChannelThreshold = 6;
    /// <summary>Frames between the once-per-second base-RT center probe (MAP ALBEDO probe line).</summary>
    private const int AlbedoProbeIntervalFrames = 60;
    /// <summary>Central fraction of the base RT the map-area probe samples (away from UI/corners).</summary>
    private const float AlbedoProbeRegion = 0.5f;

    /// <summary>One mod-owned mirror camera shadowing a captured game camera into the right RT.</summary>
    private sealed class MirrorEntry
    {
        public Camera Source = null!;
        public Camera Mirror = null!;
        public Transform MirrorTransform = null!;
        public GameObject Go = null!;
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
    /// <summary>onPostRender hook: restores the worldMap material override after the mod albedo camera renders.</summary>
    private readonly Camera.CameraCallback _postRenderHook;

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

    // ---- map base capture (class doc MAP ALBEDO RENDER) ------------------------------------
    /// <summary>Small RT the base RT is downsampled into for the async non-black probe.</summary>
    private RenderTexture? _probeRt;
    /// <summary>True once the base RT was found black — the mod albedo camera renders the map parchment into the base RT.</summary>
    private bool _mapBaseCapture;
    private int _blackProbeFrame = int.MinValue;
    private int _blackConsecutive;
    /// <summary>True while an async base-RT probe is in flight (one at a time).</summary>
    private bool _probePending;
    /// <summary>Bumped on teardown/scene release so a late async probe callback ignores stale results.</summary>
    private int _probeGen;
    private int _probeReqGen;

    // ---- map albedo render (class doc MAP ALBEDO RENDER) ------------------------------------
    /// <summary>Mod-owned forward camera that renders the worldMap parchment (unlit, albedo) into the base RT.</summary>
    private Camera? _mapAlbedoCam;
    private GameObject? _mapAlbedoGo;
    private Transform? _mapAlbedoTransform;
    /// <summary>The campaign map parchment MeshRenderer (MapChoreographer.worldMap, GH_WorldMap materials).</summary>
    private MeshRenderer? _worldMapRenderer;
    /// <summary>worldMap layer bit index — force-included in the albedo camera's culling mask.</summary>
    private int _worldMapLayer = -1;
    /// <summary>Unlit Sprites/Default override materials (one per submesh, _MainTex = the submesh's albedo).</summary>
    private Material[]? _worldMapOverrideMats;
    /// <summary>The originals swapped OUT for the current override (re-captured live each apply; restored in onPostRender).</summary>
    private Material[]? _worldMapOriginalMats;
    /// <summary>True while the override is currently on the worldMap renderer (between our onPreRender and onPostRender).</summary>
    private bool _overrideApplied;
    /// <summary>The worldMap mesh whose vertex colours we whitened (Sprites/Default multiplies texture × vertex colour; a dark/AO-baked vertex colour would dim the parchment — we neutralise it to white so the albedo shows at full brightness).</summary>
    private Mesh? _worldMapMesh;
    /// <summary>Original worldMap vertex colours, cached before whitening (restored on release). Null = the mesh had no colour channel (nothing to restore).</summary>
    private Color[]? _worldMapOrigColors;
    /// <summary>True once the worldMap mesh vertex colours have been forced white for the albedo render.</summary>
    private bool _worldMapWhitened;
    /// <summary>One-shot log guard: the discovered vertex-colour facts (per engagement).</summary>
    private bool _worldMapColorsLogged;
    /// <summary>One-shot log guard: the discovered material/albedo facts (per engagement).</summary>
    private bool _albedoMaterialsLogged;
    /// <summary>One-shot log guard: the MAP ALBEDO RENDER ENGAGED line (per engagement).</summary>
    private bool _albedoEngagedLogged;
    /// <summary>One-shot WARN guard: worldMap not found / no usable albedo (per engagement).</summary>
    private bool _albedoWarned;
    /// <summary>Base-RT center probe (MAP ALBEDO probe line) — once-per-second success/failure readout.</summary>
    private RenderTexture? _albedoProbeRt;
    private bool _albedoProbePending;
    private int _albedoProbeReqGen;
    private int _albedoProbeFrame = int.MinValue;

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
    /// True while both eye passes show the LEFT RT (video fallback / intro guard /
    /// campaign-map albedo render). <see cref="FlatScreen"/> reads this after
    /// <see cref="EndStackSync"/> to fold the UI back into the left RT while the single
    /// suspended image must carry everything (its SCREEN LAYER SPLIT class doc). The
    /// video depth SHIFT is NOT a suspension: the split keeps routing and the glass UI
    /// stays in front.
    /// </summary>
    internal bool Suspended => _videoSuspended;

    public FlatScreenStereo()
    {
        _preRenderHook = OnPreRenderCamera; // cached delegate — one allocation, ever
        _postRenderHook = OnPostRenderCamera; // worldMap material-override restore
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
            "Rescue the campaign map when it renders BLACK into the screen's render texture " +
            "(the game MapCamera renders all-black once its target is redirected onto our RT — " +
            "the map's deferred Amplify parchment shader is never lit into an off-screen " +
            "RenderTexture). When the base RT is detected black, the mod renders the map " +
            "parchment itself (see MapAlbedoRender) instead of showing a black void. Off = " +
            "legacy (black map if the game camera renders black).");
        s_mapAlbedoRender = file.Bind("WorldUI", "MapAlbedoRender", true,
            "THE MAP FIX (default ON): render the campaign world map's parchment UNLIT via a " +
            "mod-owned FORWARD camera we fully control, sampling the parchment's own albedo " +
            "texture. The map is ordinary mesh geometry (MapChoreographer.worldMap, GH_WorldMap " +
            "materials whose albedo lives in _Alb / _MainTex), but its deferred Amplify shader is " +
            "never lit into any RenderTexture we own and its backbuffer is unreadable under XR — " +
            "so instead of capturing the game's render, the mod draws the worldMap mesh straight " +
            "into the flat-screen base RT with a temporary Sprites/Default material per submesh " +
            "(albedo → _MainTex), swapped on only for our render and restored the same frame " +
            "(rendering-only, multiplayer-safe). A top-down painted map reads correct unlit. Off " +
            "= detect the black map but leave the base RT as-is (black).");
    }

    /// <summary>[WorldUI] MapAlbedoRender — render the map parchment unlit via a mod forward camera (class doc MAP ALBEDO RENDER).</summary>
    internal static bool MapAlbedoRenderOn => s_mapAlbedoRender?.Value ?? true;

    private static float DepthStrength => Mathf.Clamp(s_depthStrength?.Value ?? 1f, 0f, 3f);

    private static float ParallaxScale => Mathf.Clamp(s_parallaxScale?.Value ?? 6f, 1f, 60f);

    /// <summary>
    /// Colour RenderTexture factory for the flat-screen map/eye path — plain
    /// <c>RenderTextureReadWrite.Default</c>. (The sRGB/colorspace fix was proven a
    /// no-op: the rig renders in Gamma colorspace, so sRGB read/write yields sRGB=False
    /// regardless.)
    /// </summary>
    internal static RenderTexture CreateColorRt(int width, int height, int depth, string name)
    {
        return new RenderTexture(width, height, depth, RenderTextureFormat.Default, RenderTextureReadWrite.Default)
        {
            name = name,
            antiAliasing = 1,
        };
    }

    /// <summary>Human-readable facts of an RT (format, graphicsFormat, sRGB flag, depth) for the setup logs.</summary>
    internal static string DescribeRt(RenderTexture? rt)
    {
        if (rt == null)
            return "null";
        bool srgb = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(rt.graphicsFormat);
        return $"'{rt.name}' {rt.width}x{rt.height} fmt {rt.format}/{rt.graphicsFormat} sRGB={srgb} depth={rt.depth} aa={rt.antiAliasing}";
    }

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
            _rtRight = CreateColorRt(leftRt!.width, leftRt.height, 24, "GloomhavenVR.FlatScreenRT.Right");
            _rtRight.Create();
        }

        if (!_active)
        {
            _active = true;
            if (!_hooked)
            {
                Camera.onPreRender += _preRenderHook;
                Camera.onPostRender += _postRenderHook; // worldMap override restore
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
        ReleaseAlbedo();
        ReleaseMirrors();
        if (_hooked)
        {
            Camera.onPreRender -= _preRenderHook;
            Camera.onPostRender -= _postRenderHook;
            _hooked = false;
        }
        if (_quadMaterial != null && _leftRt != null && _quadMaterial.mainTexture != _leftRt)
            _quadMaterial.mainTexture = _leftRt;
        ReleaseRightRt();
        ReleaseShiftRt();
        ReleaseProbeRt();
        ReleaseAlbedoProbeRt();
        _probeGen++;                 // invalidate any in-flight probe callback
        _probePending = false;
        _albedoProbePending = false;
        _mapBaseCapture = false;
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

    private void ReleaseProbeRt()
    {
        if (_probeRt == null)
            return;
        _probeRt.Release();
        Object.Destroy(_probeRt);
        _probeRt = null;
    }

    private void ReleaseAlbedoProbeRt()
    {
        if (_albedoProbeRt == null)
            return;
        _albedoProbeRt.Release();
        Object.Destroy(_albedoProbeRt);
        _albedoProbeRt = null;
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
        Camera? mapSource = null; // lowest-depth live 3D source (= the game MapCamera) for the albedo clone
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
            if (entry.SourceOn && (mapSource == null || entry.Source.depth < mapSource.depth))
                mapSource = entry.Source;
        }

        // Decision (class doc): the intro guard forces the suspension outright — zero
        // shift, the intro must remain verified-identical. The campaign-map albedo
        // render (class doc MAP ALBEDO RENDER) also drives BOTH eyes MONO from the base
        // RT — the mod albedo camera fills it bright, no per-eye parallax. Otherwise an
        // active camera-plane video engages the uniform depth shift; if the shift path
        // is unavailable (kill switch / zero depth / no shifted RT) the suspension
        // fallback takes over — never one-eyed, never black.
        bool depthLayer = s_videoDepthLayer?.Value ?? true;
        bool shift = false;
        bool suspend = false;
        string? suspendWhy = null;
        if (_introGuard)
        {
            suspend = true;
        }
        else if (_mapBaseCapture)
        {
            suspend = true;
            suspendWhy = "campaign map — parchment albedo rendered into the base RT (mono)";
        }
        else if (anyVideo)
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
                ? $"Depth shift ENGAGED for '{videoSource}': the vanilla camera-plane video keeps " +
                  "rendering into the left RT untouched; mirrors off, both eyes show shifted copies " +
                  $"of it (±{_videoShiftUv:F4} UV, {VideoOverscan:F2}x overscan) — the background " +
                  "reads behind the glass UI."
                : "Depth shift RELEASED — no camera-plane video active; per-eye mirror parallax re-engages.");
        }

        if (suspend != _videoSuspended)
        {
            _videoSuspended = suspend;
            VRLog.Info("WorldUI", suspend
                ? (_introGuard
                    ? "Stereo screen SUSPENDED — intro guard: pre-menu scenes force identical " +
                      "eyes (both eyes = left RT, zero shift; the intro must remain " +
                      "verified-identical)."
                    : _mapBaseCapture
                        ? "Stereo screen SUSPENDED — campaign map albedo render: both eyes show the " +
                          "base RT (the mod forward camera draws the parchment unlit into it)."
                        : "Stereo screen SUSPENDED — the depth shift is unavailable " +
                          $"({suspendWhy}); both eyes show the left RT until it resumes.")
                : "Stereo screen RESUMED — per-eye rendering re-engaged.");
        }

        // Campaign-map albedo render (class doc MAP ALBEDO RENDER): configure + enable the mod
        // forward camera so it OWNS the base RT's final content this frame; disable it otherwise.
        ReconcileAlbedoCamera(mapSource);

        bool anyMirrorRendering = false;
        for (int i = 0; i < _mirrors.Count; i++)
        {
            MirrorEntry entry = _mirrors[i];
            bool want = entry.SourceOn && !suspend && !shift;
            if (entry.Mirror.enabled != want)
                entry.Mirror.enabled = want;
            if (want)
                anyMirrorRendering = true;
        }

        // Map base-capture probe (class doc MAP ALBEDO RENDER): a 3D background camera is
        // actively rendering (mirrors on) but the game's own render may be black — check
        // the base RT and engage the albedo render if so.
        TickBlackProbe(anyMirrorRendering);

        // Once-per-second base-RT center probe — the MAP ALBEDO success/failure readout.
        TickAlbedoProbe();
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
            var rt = CreateColorRt(_leftRt.width, _leftRt.height, 0, "GloomhavenVR.FlatScreenRT.LeftShifted");
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

    // ---- map base capture: probe + engage (class doc MAP ALBEDO RENDER) ---------------------

    /// <summary>
    /// Throttled ASYNC non-black probe of the base RT. Runs only while a 3D background
    /// camera is actively rendering (so the base RT SHOULD hold scene content) and the
    /// albedo render is not yet engaged; a stall-free downsample-then-readback that,
    /// after several consecutive all-black results, engages the mod albedo render of
    /// the campaign map.
    /// </summary>
    private void TickBlackProbe(bool anyMirrorRendering)
    {
        if (_mapBaseCapture || !(s_leftMirrorFallback?.Value ?? true))
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
                EngageMapAlbedo(maxChannel);
        }
        else
        {
            _blackConsecutive = 0;
        }
    }

    /// <summary>
    /// The base RT reads black while a 3D background camera renders — engage MAP ALBEDO
    /// RENDER: the mod's own forward camera renders the worldMap parchment (unlit, from
    /// its albedo texture) into the base RT, and both eyes are driven from it (mono).
    /// Sticky for the scene; re-arms on the next stack release.
    /// </summary>
    private void EngageMapAlbedo(int maxChannel)
    {
        if (_mapBaseCapture)
            return;
        _mapBaseCapture = true;
        _albedoEngagedLogged = false;
        _albedoMaterialsLogged = false;
        _albedoWarned = false;
        VRLog.Info("WorldUI", $"MAP ALBEDO detection: the screen's base RenderTexture reads BLACK " +
                              $"(max channel {maxChannel}/255 over {BlackProbeSize}x{BlackProbeSize}) while a 3D " +
                              "background camera renders — the campaign map's deferred parchment shader will not " +
                              "light into any RenderTexture we own. Engaging the mod forward albedo render of " +
                              "MapChoreographer.worldMap into the base RT. Re-arms on the next scene change.");
    }

    // ---- map albedo render: camera + material override (class doc MAP ALBEDO RENDER) --------

    /// <summary>
    /// Configure + enable the mod forward albedo camera so it renders the worldMap parchment into
    /// the base RT this frame; disable it whenever the map capture is not engaged / not ready.
    /// Cloned from <paramref name="mapSource"/> (the game MapCamera) with a depth just above it, so
    /// Unity renders it LAST among base-RT cameras and it OWNS the base RT's final content.
    /// </summary>
    private void ReconcileAlbedoCamera(Camera? mapSource)
    {
        if (!_mapBaseCapture || mapSource == null || !EnsureAlbedoReady())
        {
            if (_mapAlbedoCam != null && _mapAlbedoCam.enabled)
                _mapAlbedoCam.enabled = false;
            return;
        }

        Camera cam = _mapAlbedoCam!;
        _mapAlbedoTransform!.SetPositionAndRotation(mapSource.transform.position, mapSource.transform.rotation);
        cam.clearFlags = CameraClearFlags.SolidColor;
        // Neutral dark clear (sample the game camera's background) — the parchment fills the frame.
        cam.backgroundColor = mapSource.backgroundColor;
        // Render whatever the game camera sees PLUS the parchment's own layer (its material override
        // guarantees the mesh draws), never the mod layer (our quad/hands — feedback).
        cam.cullingMask = (mapSource.cullingMask | (1 << _worldMapLayer)) & ~VRLayers.ModLayerMask;
        // Just above the game MapCamera so Unity composites us LAST into the base RT (we overwrite
        // its dark render); still below the head camera, so the quad samples this frame's result.
        cam.depth = mapSource.depth + 0.1f;
        cam.rect = mapSource.rect;
        cam.nearClipPlane = mapSource.nearClipPlane;
        cam.farClipPlane = mapSource.farClipPlane;
        cam.orthographic = mapSource.orthographic;
        cam.orthographicSize = mapSource.orthographicSize;
        cam.fieldOfView = mapSource.fieldOfView;
        cam.allowHDR = mapSource.allowHDR;
        cam.allowMSAA = mapSource.allowMSAA;
        cam.useOcclusionCulling = mapSource.useOcclusionCulling;
        // Forward — the material override is an unlit forward shader (Sprites/Default); no deferred
        // G-buffer resolve is needed or wanted (that is the whole point — the deferred map never lit).
        cam.renderingPath = RenderingPath.Forward;
        cam.projectionMatrix = mapSource.projectionMatrix;
        if (cam.targetTexture != _leftRt)
            cam.targetTexture = _leftRt;
        if (!cam.enabled)
            cam.enabled = true;

        if (!_albedoEngagedLogged)
        {
            _albedoEngagedLogged = true;
            int submeshes = _worldMapOverrideMats != null ? _worldMapOverrideMats.Length : 0;
            VRLog.Info("WorldUI", $"MAP ALBEDO RENDER ENGAGED: worldMap MeshRenderer " +
                                  $"'{(_worldMapRenderer != null ? _worldMapRenderer.name : "?")}' found with " +
                                  $"{submeshes} submeshes; forward mod camera (depth {cam.depth:F1}, cloned from " +
                                  $"'{mapSource.name}') → base RT, parchment drawn unlit from its albedo texture.");
        }
    }

    /// <summary>
    /// Ensure the worldMap renderer, override materials and mod camera all exist for the albedo
    /// render (built lazily once the map capture is engaged; retried each tick until the scene's
    /// worldMap is available). Returns false (with a one-shot WARN) if the map is not renderable.
    /// </summary>
    private bool EnsureAlbedoReady()
    {
        if (!MapAlbedoRenderOn || _root == null || _leftRt == null)
            return false;

        if (_worldMapRenderer == null && !FindWorldMapRenderer())
        {
            if (!_albedoWarned)
            {
                _albedoWarned = true;
                VRLog.Warn("WorldUI", "MAP ALBEDO RENDER: no MapChoreographer.worldMap MeshRenderer with a " +
                                      "GH_WorldMap material found in the scene — the base RT is left as-is " +
                                      "(the map stays black). Retrying each tick while the map is showing.");
            }
            return false;
        }

        if (_worldMapOverrideMats == null && !BuildOverrideMaterials())
            return false;

        WhitenWorldMapColors();
        EnsureAlbedoCamera();
        return _mapAlbedoCam != null;
    }

    /// <summary>Reflect the decompiled campaign-map parchment renderer (MapChoreographer.worldMap, publicized).</summary>
    private bool FindWorldMapRenderer()
    {
        global::MapChoreographer choreo = Object.FindObjectOfType<global::MapChoreographer>();
        if (choreo == null)
            return false;
        GameObject worldMap = choreo.worldMap; // publicized private serialized field
        if (worldMap == null)
            return false;

        MeshRenderer[] rends = worldMap.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
        MeshRenderer? found = null;
        for (int i = 0; i < rends.Length && found == null; i++)
        {
            Material[] mats = rends[i].sharedMaterials;
            for (int j = 0; j < mats.Length; j++)
            {
                if (mats[j] != null && mats[j].name.IndexOf("GH_WorldMap", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = rends[i];
                    break;
                }
            }
        }
        // Fallback: the parchment renderer is directly on the worldMap GO (decompiled
        // MapChoreographer uses worldMap.GetComponent<MeshRenderer>()).
        if (found == null)
            found = worldMap.GetComponent<MeshRenderer>();
        if (found == null)
            return false;

        _worldMapRenderer = found;
        _worldMapLayer = found.gameObject.layer;
        return true;
    }

    /// <summary>
    /// Force the worldMap mesh's vertex colours to white for the albedo render. Sprites/Default
    /// (our unlit override) multiplies texture × vertex colour, so a dark or AO-baked vertex colour
    /// channel dims the parchment (the observed mean ~43 vs a bright 4096² albedo). Whitening lets
    /// the albedo render at full brightness. Idempotent; the originals are cached and restored on
    /// release. The game only renders this mesh into the base RT we overwrite, so this is invisible
    /// to gameplay and multiplayer (rendering-only).
    /// </summary>
    private void WhitenWorldMapColors()
    {
        if (_worldMapWhitened || _worldMapRenderer == null)
            return;
        MeshFilter? mf = _worldMapRenderer.GetComponent<MeshFilter>();
        Mesh? mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null)
            return;

        Color[] colors = mesh.colors; // empty array when the mesh has no colour channel
        if (!_worldMapColorsLogged)
        {
            _worldMapColorsLogged = true;
            string sample = colors.Length > 0
                ? $"{colors.Length} verts, colour[0] = (r{colors[0].r:F2} g{colors[0].g:F2} b{colors[0].b:F2} a{colors[0].a:F2})"
                : "NONE (no vertex-colour channel — default white; a dim result then means the albedo texture itself is dark, not vertex-colour)";
            VRLog.Info("WorldUI", $"MAP ALBEDO vertex colours: {sample}. Forcing white so Sprites/Default shows the parchment albedo at full brightness (MAP ALBEDO probe mean should rise from ~43 if vertex colour was the darkener).");
            LogWorldMapUvs(mesh);
        }

        if (colors.Length == 0)
        {
            // No channel to darken — nothing to gain from whitening; leave the mesh untouched.
            _worldMapWhitened = true;
            _worldMapMesh = mesh;
            _worldMapOrigColors = null;
            return;
        }

        _worldMapMesh = mesh;
        _worldMapOrigColors = colors;
        var white = new Color[colors.Length];
        for (int i = 0; i < white.Length; i++)
            white[i] = Color.white;
        var whitened = new Color[colors.Length];
        System.Array.Copy(white, whitened, white.Length);
        mesh.colors = whitened;
        _worldMapWhitened = true;
    }

    /// <summary>
    /// Log the worldMap mesh's UV channels (once). A flat, detail-less albedo render means
    /// Sprites/Default (uv0) is sampling degenerately — either uv0 is missing/degenerate (the
    /// parchment is on uv1/uv2, which Sprites/Default cannot reach → needs a custom-channel shader),
    /// or the ST is off. This prints each channel's vert count and 0..1 bounds to disambiguate.
    /// </summary>
    private void LogWorldMapUvs(Mesh mesh)
    {
        var uv0 = new System.Collections.Generic.List<Vector2>();
        var uv1 = new System.Collections.Generic.List<Vector2>();
        var uv2 = new System.Collections.Generic.List<Vector2>();
        mesh.GetUVs(0, uv0);
        mesh.GetUVs(1, uv1);
        mesh.GetUVs(2, uv2);
        VRLog.Info("WorldUI", $"MAP ALBEDO UVs: verts {mesh.vertexCount}, submeshes {mesh.subMeshCount}; " +
                              $"uv0 {UvBounds(uv0)}; uv1 {UvBounds(uv1)}; uv2 {UvBounds(uv2)}. " +
                              "Sprites/Default samples uv0 — if uv0 is degenerate/tiny but uv1 spans ~0..1, the parchment is on uv1 and needs a custom shader.");
    }

    private static string UvBounds(System.Collections.Generic.List<Vector2> uv)
    {
        if (uv == null || uv.Count == 0)
            return "NONE";
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < uv.Count; i++)
        {
            Vector2 v = uv[i];
            if (v.x < minX) minX = v.x; if (v.x > maxX) maxX = v.x;
            if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y;
        }
        return $"{uv.Count} [x {minX:F3}..{maxX:F3}, y {minY:F3}..{maxY:F3}]";
    }

    /// <summary>Restore the worldMap mesh's original vertex colours (game state untouched on release).</summary>
    private void RestoreWorldMapColors()
    {
        if (_worldMapMesh != null && _worldMapOrigColors != null)
            _worldMapMesh.colors = _worldMapOrigColors;
        _worldMapMesh = null;
        _worldMapOrigColors = null;
        _worldMapWhitened = false;
        _worldMapColorsLogged = false;
    }

    /// <summary>
    /// Build one <c>Sprites/Default</c> override material per worldMap submesh with its parchment
    /// albedo (<c>_Alb</c> ?? <c>_MainTex</c> ?? <c>mainTexture</c>) as <c>_MainTex</c>. Logs the
    /// discovered material facts once (name + which property held the albedo + texture + size).
    /// Returns false (one-shot WARN) if no submesh yielded a usable albedo texture.
    /// </summary>
    private bool BuildOverrideMaterials()
    {
        if (_worldMapRenderer == null)
            return false;
        Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (sh == null)
        {
            if (!_albedoWarned)
            {
                _albedoWarned = true;
                VRLog.Warn("WorldUI", "MAP ALBEDO RENDER: neither Sprites/Default nor UI/Default is present — " +
                                      "cannot build an unlit override; the base RT is left as-is (map black).");
            }
            return false;
        }

        Material[] orig = _worldMapRenderer.sharedMaterials;
        var overrides = new Material[orig.Length];
        int withTex = 0;
        string facts = "";
        for (int i = 0; i < orig.Length; i++)
        {
            Material o = orig[i];
            Texture? tex = null;
            string prop = "NONE";
            if (o != null)
            {
                if (o.HasProperty("_Alb") && (tex = o.GetTexture("_Alb")) != null)
                    prop = "_Alb";
                else if (o.HasProperty("_MainTex") && (tex = o.GetTexture("_MainTex")) != null)
                    prop = "_MainTex";
                else if ((tex = o.mainTexture) != null)
                    prop = "mainTexture";
            }
            var m = new Material(sh) { name = "GloomhavenVR.MapAlbedo." + i };
            Vector2 scale = Vector2.one, offset = Vector2.zero;
            if (tex != null)
            {
                m.mainTexture = tex; // Sprites/Default samples _MainTex
                // Copy the source's tiling/offset — a flat, detail-less result means Sprites/Default
                // (default ST 1,1,0,0) is sampling the wrong region; the Amplify material may carry a
                // non-default _MainTex_ST. Read from whichever property held the albedo.
                string stProp = prop == "_Alb" ? "_Alb_ST" : "_MainTex_ST";
                if (o != null && o.HasProperty(stProp))
                {
                    Vector4 st = o.GetVector(stProp);
                    scale = new Vector2(st.x, st.y);
                    offset = new Vector2(st.z, st.w);
                }
                else if (o != null)
                {
                    scale = o.mainTextureScale;
                    offset = o.mainTextureOffset;
                }
                m.mainTextureScale = scale;
                m.mainTextureOffset = offset;
                withTex++;
            }
            overrides[i] = m;
            if (!_albedoMaterialsLogged)
                facts += $"\n  material[{i}] '{(o != null ? o.name : "<null>")}': albedo from {prop}"
                         + (tex != null ? $" = '{tex.name}' {tex.width}x{tex.height} ST scale({scale.x:F3},{scale.y:F3}) offset({offset.x:F3},{offset.y:F3})" : " (none)");
        }
        _worldMapOverrideMats = overrides;
        _worldMapOriginalMats = orig;

        if (!_albedoMaterialsLogged)
        {
            _albedoMaterialsLogged = true;
            VRLog.Info("WorldUI", $"MAP ALBEDO materials ({orig.Length} submesh(es), {withTex} with a texture):{facts}");
        }

        if (withTex == 0)
        {
            if (!_albedoWarned)
            {
                _albedoWarned = true;
                VRLog.Warn("WorldUI", "MAP ALBEDO RENDER: no worldMap submesh material exposed an albedo texture " +
                                      "(_Alb / _MainTex / mainTexture all null) — the base RT is left as-is (map black).");
            }
            // Drop the useless overrides so a later rebuild (materials populated) can retry.
            DestroyOverrideMaterials();
            return false;
        }
        return true;
    }

    /// <summary>Create the mod-owned albedo camera (bare, disabled; flipped on by ReconcileAlbedoCamera).</summary>
    private void EnsureAlbedoCamera()
    {
        if (_mapAlbedoCam != null || _root == null)
            return;
        var go = new GameObject("GloomhavenVR.MapAlbedoCamera");
        go.transform.SetParent(_root.transform, worldPositionStays: false);
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;                            // ReconcileAlbedoCamera flips it on
        cam.stereoTargetEye = StereoTargetEyeMask.None; // VRCameraPolicy-invisible by construction
        cam.targetTexture = _leftRt;
        XRDevice.DisableAutoXRCameraTracking(cam, true);
        _mapAlbedoCam = cam;
        _mapAlbedoGo = go;
        _mapAlbedoTransform = go.transform;
    }

    /// <summary>Swap the unlit override onto the worldMap renderer just before our albedo camera renders it.</summary>
    private void ApplyWorldMapOverride()
    {
        if (_overrideApplied || _worldMapRenderer == null || _worldMapOverrideMats == null)
            return;
        // Re-capture the live originals each time so the restore always puts back exactly what the
        // game currently has (a submesh count change invalidates our override — skip + rebuild).
        Material[] current = _worldMapRenderer.sharedMaterials;
        if (current.Length != _worldMapOverrideMats.Length)
        {
            DestroyOverrideMaterials(); // stale — EnsureAlbedoReady rebuilds next tick
            return;
        }
        _worldMapOriginalMats = current;
        _worldMapRenderer.sharedMaterials = _worldMapOverrideMats;
        _overrideApplied = true;
    }

    /// <summary>Restore the worldMap renderer's original materials right after our albedo camera renders (game state untouched).</summary>
    private void RestoreWorldMapOverride()
    {
        if (!_overrideApplied)
            return;
        _overrideApplied = false;
        if (_worldMapRenderer != null && _worldMapOriginalMats != null)
            _worldMapRenderer.sharedMaterials = _worldMapOriginalMats;
    }

    private void DestroyOverrideMaterials()
    {
        if (_worldMapOverrideMats != null)
        {
            for (int i = 0; i < _worldMapOverrideMats.Length; i++)
                if (_worldMapOverrideMats[i] != null)
                    Object.Destroy(_worldMapOverrideMats[i]);
            _worldMapOverrideMats = null;
        }
        _worldMapOriginalMats = null;
    }

    /// <summary>Tear down the whole albedo render (restore game materials, destroy overrides + mod camera).</summary>
    private void ReleaseAlbedo()
    {
        RestoreWorldMapOverride(); // never leave the override on the game renderer
        RestoreWorldMapColors();   // put the mesh's original vertex colours back
        DestroyOverrideMaterials();
        _worldMapRenderer = null;
        _worldMapLayer = -1;
        _albedoMaterialsLogged = false;
        _albedoEngagedLogged = false;
        _albedoWarned = false;
        _overrideApplied = false;
        if (_mapAlbedoGo != null)
        {
            Object.Destroy(_mapAlbedoGo);
            _mapAlbedoGo = null;
            _mapAlbedoCam = null;
            _mapAlbedoTransform = null;
        }
    }

    // ---- map albedo render: once-per-second base-RT probe (MAP ALBEDO probe line) ------------

    /// <summary>
    /// Throttled async probe of the CENTER region of the base RT while the albedo render is engaged —
    /// a single decisive line reporting whether the parchment landed bright (mean &gt; 50) or the base
    /// RT is still failing (~0 black / ~12 dark murk).
    /// </summary>
    private void TickAlbedoProbe()
    {
        if (!_mapBaseCapture || !_active || _leftRt == null || _albedoProbePending)
            return;
        if (_albedoProbeFrame != int.MinValue
            && Time.frameCount - _albedoProbeFrame < AlbedoProbeIntervalFrames)
            return;
        _albedoProbeFrame = Time.frameCount;

        if (_albedoProbeRt == null)
        {
            _albedoProbeRt = new RenderTexture(BlackProbeSize, BlackProbeSize, 0)
            {
                name = "GloomhavenVR.FlatScreenRT.AlbedoProbe",
                antiAliasing = 1,
            };
            if (!_albedoProbeRt.Create())
            {
                ReleaseAlbedoProbeRt();
                return;
            }
        }

        float scale = AlbedoProbeRegion;
        float offset = (1f - AlbedoProbeRegion) * 0.5f;
        Graphics.Blit(_leftRt, _albedoProbeRt, new Vector2(scale, scale), new Vector2(offset, offset));
        _albedoProbePending = true;
        _albedoProbeReqGen = _probeGen;
        AsyncGPUReadback.Request(_albedoProbeRt, 0, TextureFormat.RGBA32, OnAlbedoProbe);
    }

    private void OnAlbedoProbe(AsyncGPUReadbackRequest req)
    {
        _albedoProbePending = false;
        if (!_active || !_mapBaseCapture || _albedoProbeReqGen != _probeGen || req.hasError)
            return;

        var data = req.GetData<Color32>();
        int n = Mathf.Max(1, data.Length);
        long sum = 0, rSum = 0, gSum = 0, bSum = 0;
        int max = 0;
        for (int i = 0; i < data.Length; i++)
        {
            Color32 c = data[i];
            int m = c.r;
            if (c.g > m) m = c.g;
            if (c.b > m) m = c.b;
            if (m > max) max = m;
            sum += m; rSum += c.r; gSum += c.g; bSum += c.b;
        }
        int mean = (int)(sum / n);
        int rMean = (int)(rSum / n), gMean = (int)(gSum / n), bMean = (int)(bSum / n);
        VRLog.Info("WorldUI",
            $"MAP ALBEDO probe: base RT center mean {mean}/255 (r{rMean} g{gMean} b{bMean}), max {max} — " +
            ">50 ⇒ parchment visible; ~0/~12 ⇒ still failing.");
    }

    /// <summary>Destroy all mirrors (captured stack released — scene change / hide). Cheap to rebuild.</summary>
    internal void ReleaseMirrors()
    {
        for (int i = _mirrors.Count - 1; i >= 0; i--)
            DestroyMirrorAt(i);
        _mirrors.Clear();
        _bySource.Clear();

        // Re-arm map albedo render for the next scene (class doc MAP ALBEDO RENDER): the
        // evidence (a black base RT) belongs to the scene we just left, and the worldMap
        // renderer/overrides reference textures from it. A late probe callback is
        // invalidated by the generation bump.
        if (_mapBaseCapture || _blackConsecutive != 0)
        {
            _mapBaseCapture = false;
            _blackConsecutive = 0;
            _probeGen++;
            _probePending = false;
            _albedoProbePending = false;
            ReleaseAlbedo();
        }
    }

    private void DestroyMirrorAt(int index)
    {
        MirrorEntry entry = _mirrors[index];
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
    /// ALSO: for the mod albedo camera (campaign map), this is where the unlit worldMap
    /// material override is swapped ON (restored in <see cref="OnPostRenderCamera"/>).
    /// </summary>
    private void OnPreRenderCamera(Camera cam)
    {
        if (!_active)
            return;
        if (_mapAlbedoCam != null && cam == _mapAlbedoCam)
        {
            ApplyWorldMapOverride();
            return;
        }
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null || cam != head)
            return;
        Material? mat = _quadMaterial;
        if (mat == null)
            return;

        if (_videoShift && _shiftBlitFrame != Time.frameCount
            && _leftRt != null && _rtRight != null && _rtLeftShifted != null)
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
            Graphics.Blit(_leftRt, _rtLeftShifted, scale, new Vector2(margin + shift, margin));
            Graphics.Blit(_leftRt, _rtRight, scale, new Vector2(margin - shift, margin));
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

        // Target per eye: suspension (intro guard / video-unavailable / campaign-map albedo)
        // → the left RT for BOTH eyes (for the map it now holds the mod's bright parchment
        // render); depth shift → the per-eye shifted copies; otherwise the mirror-rendered
        // right RT and, for the left eye, the left RT.
        RenderTexture? target;
        if (_videoSuspended)
            target = _leftRt;
        else if (_videoShift && _rtLeftShifted != null && _rtRight != null)
            target = right ? _rtRight : _rtLeftShifted;
        else if (right)
            target = _rtRight != null ? _rtRight : _leftRt;
        else
            target = _leftRt;
        if (target != null && !ReferenceEquals(mat.mainTexture, target))
            mat.mainTexture = target;
    }

    /// <summary>Head-camera post-render hook: restore the worldMap materials after the mod albedo camera rendered.</summary>
    private void OnPostRenderCamera(Camera cam)
    {
        if (!_active)
            return;
        if (_mapAlbedoCam != null && cam == _mapAlbedoCam)
            RestoreWorldMapOverride();
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
