using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace GloomhavenVR.WorldUI;

// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and
// reasoning: FlatScreen.1.Core.cs). The four parts concatenate back into the
// original member order.
//
// THIS FILE IS THE MAP/COMPOSITOR SEPARATION AND NOTHING ELSE. The campaign-map
// renderer is part 3, whole; the stereo compositor is parts 2 and 4. BindConfig
// (part 2) binds stereo AND map keys in one method and stays whole — the
// config-binding seam is where a mistake costs a user their settings. The three
// camera hooks (part 4) contain both stereo and map logic in one body and stay
// whole for the same reason. Beyond this one cut the file is hardware-won
// plumbing and the charter's default answer applies: leave it.
//
// The three site-scoped `#pragma warning disable CS0162` blocks around the map
// bisection switches are all inside part 3 with the code they scope. A pragma is
// LINE-scoped: moving code across one either brings the warning back or lands the
// suppression on the wrong lines. Build is verified at 0 warnings.

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
/// THE FIX (as shipped — this paragraph was rewritten after the hunt closed; the earlier
/// version described the two designs that were DISPROVEN, see below): while the campaign
/// map is showing (detected FAST and positively at the map-open event — a MapChoreographer
/// whose worldMap/cityMap is active, see <c>TickFastMapEngage</c>; the black-probe below —
/// the game 'MapCamera' renders BLACK into any RenderTexture we own — and the non-black
/// MapChoreographer probe remain as fallbacks), a mod-owned FORWARD camera we fully
/// control renders the map mesh into its OWN PRIVATE RenderTexture
/// (<see cref="_mapRt"/>, <see cref="EnsureMapRt"/>), and the screen quad samples THAT in
/// map mode. The private RT is load-bearing: the game's deferred MapCamera is force-pinned
/// onto the SHARED base RT (<see cref="_leftRt"/>) by <c>FlatScreen.CaptureStack</c> and
/// its murk out-competed our forward render there — a magenta-clear hardware test proved
/// our output never survived <c>_leftRt</c>. FRAMING is ours, not the game's: the captured
/// game MapCamera pose GRAZES the y≈0 parchment plane (<c>WorldToViewportPoint(meshCenter).z
/// &lt; 0</c> — the mesh is literally behind it), so cloning its transform/projection is
/// impossible; instead <see cref="MapDiagTopDown"/> + <see cref="MapMatchGameFraming"/>
/// compute the pose from the live <c>CameraController</c> focal point/height and
/// <see cref="MapDriveGameCamera"/> DRIVES the game's own map camera to it, which makes the
/// game's marker projection and click raycasts (both of which go through that camera) line
/// up with our render for free. The parchment is drawn UNLIT via a temporary MATERIAL
/// OVERRIDE: one <c>GloomhavenVR/MapUnlit</c> material (bundle shader,
/// <see cref="MapUnlitShader"/>) per submesh whose <c>_MainTex</c> is the original
/// material's albedo (<c>_Alb</c> ?? <c>_MainTex</c> ?? <c>mainTexture</c>), sampled at the
/// mesh's own UV on the GPU (<see cref="MapUnlitUvChannel"/> = TexCoord0). The override is
/// swapped onto the map MeshRenderer in the mod camera's onPreRender and restored in its
/// onPostRender — scoped to exactly our render, so the game's own state is untouched
/// (rendering-only, MULTIPLAYER-SAFE). On top of the parchment the mod draws the location
/// icons itself (<see cref="DrawMapIcons"/> — the game's are deferred Decalicious decals
/// that never light into our RT) and dims, but keeps, the wind/cloud particles
/// (<see cref="TuneMapWindParticles"/>). Zoom is camera FOV, matched to flat's map configs;
/// pan is a trigger-drag on the focal point. Both the world and the city map are handled
/// (<see cref="DetectActiveMap"/>).
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
/// ALSO DISPROVEN AND REMOVED (code AND config keys — the 18 DEPRECATED map knobs were
/// deleted in the 2026-08 dead-settings sweep): rendering the map into the SHARED base RT
/// instead of the private one; cloning the game MapCamera's transform/projection; the
/// <c>Sprites/Default</c> + CPU uv0-REBUILD path (the old <c>MapUv*</c> keys — the mesh's
/// own TexCoord0 is correct and the shader samples it directly); the "keep the deferred
/// render, strip its image effects" strategy (<c>MapCaptureMode</c> 1 + the
/// <c>MapStrip*</c> keys); the never-implemented texture-blit strategy
/// (<c>MapCaptureMode</c> 2 + the <c>MapTex*</c> keys); and boosting ambient / adding a
/// light for the render (<c>MapAlbedoAmbient</c>, <c>MapAlbedoLight</c> — MapUnlit is
/// unlit, so lighting cannot affect it). Do not re-add any of it.
///
/// Detection is a throttled, 8x8-downsampled, ASYNC non-black probe of the base RT
/// (<see cref="AsyncGPUReadback"/> — no GPU stall) requiring several consecutive black
/// reads, so no currently-working scene (menu video, guildmaster town whose camera
/// renders fine) is ever switched. Engagement is sticky for the scene (the game base RT
/// stays black) and re-arms on the next captured-stack release.
/// Detection and re-render are UNCONDITIONAL (user ruling 2026-08-13: the two dials that could
/// switch either half off — [WorldUI] MapAlbedoRender and ScreenLeftMirrorFallback — both had a
/// black campaign map as their off-state, and are removed).
/// </summary>
internal sealed partial class FlatScreenStereo
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
    // s_leftMirrorFallback / s_mapAlbedoRender are GONE with their dials (user ruling
    // 2026-08-13): the black-map probe, the fast engage and the unlit albedo re-render all run
    // unconditionally, because every OFF state of those two left the campaign map black.
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
    /// <summary>Probe the base RT center EVERY frame for this many frames after the map engages (catch the detail→black transition), then throttle to <see cref="AlbedoProbeIntervalFrames"/>.</summary>
    private const int FastProbeFrames = 150;
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
    /// <summary>onPreCull hook: applies the captured game-map matrices to the mirror camera BEFORE its culling (culling with the stale grazing matrices was culling the map quads out → black).</summary>
    private readonly Camera.CameraCallback _preCullHook;

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
    /// <summary>
    /// PRIVATE map RenderTexture — the mod forward map camera (_mapAlbedoCam) renders EXCLUSIVELY into
    /// this, and the screen quad samples it in map mode. This is the fix for the compositing loss: the
    /// game's deferred MapCamera is force-pinned onto the SHARED base RT (_leftRt) by CaptureStack and
    /// its dark murk out-competed our forward render there (magenta-clear test proved our output never
    /// survived _leftRt). A dedicated RT nothing else writes makes our forward render the sole writer —
    /// mirroring the working character-portrait path (a forward camera that exclusively owns its RT).
    /// </summary>
    private RenderTexture? _mapRt;
    /// <summary>One-shot guard for the MAP RENDER camera-target log (per engagement).</summary>
    private bool _mapTargetLogged;
    /// <summary>Small RT the base RT is downsampled into for the async non-black probe.</summary>
    private RenderTexture? _probeRt;
    /// <summary>True once the base RT was found black — the mod albedo camera renders the map parchment into the base RT.</summary>
    private bool _mapBaseCapture;
    /// <summary>One-shot guard for the "campaign map MONO (glass kept)" log (ISSUE 1).</summary>
    private bool _mapMonoLogged;
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
    /// <summary>The active campaign map parchment MeshRenderer (MapChoreographer.worldMap OR cityMap — whichever is active).</summary>
    private MeshRenderer? _worldMapRenderer;
    /// <summary>The active map GameObject the renderer/textures were gathered from (world OR city). Invalidates on a world↔city switch (ISSUE 3).</summary>
    private GameObject? _activeMapGo;
    /// <summary>True when the active map is the city map (ISSUE 3) — for logging.</summary>
    private bool _activeMapIsCity;
    /// <summary>Unlit <c>GloomhavenVR/MapUnlit</c> override materials (one per submesh, _MainTex =
    /// the submesh's albedo, UV sampled on the GPU from the mesh's own TexCoord0). NOT
    /// Sprites/Default — that path needed CPU-rebuilt uv0 and was removed; see
    /// <see cref="BuildOverrideMaterials"/>.</summary>
    private Material[]? _worldMapOverrideMats;
    /// <summary>The renderer the current override materials were built for (rebuild on a world↔city switch).</summary>
    private Renderer? _overrideMatsRenderer;
    /// <summary>The originals swapped OUT for the current override (re-captured live each apply; restored in onPostRender).</summary>
    private Material[]? _worldMapOriginalMats;
    /// <summary>True while the override is currently on the worldMap renderer (between our onPreRender and onPostRender).</summary>
    private bool _overrideApplied;
    /// <summary>One-shot log guard: the discovered material/albedo facts (per engagement).</summary>
    private bool _albedoMaterialsLogged;
    /// <summary>One-shot WARN guard: worldMap not found / no usable albedo (per engagement).</summary>
    private bool _albedoWarned;
    /// <summary>Base-RT center probe (MAP ALBEDO probe line) — once-per-second success/failure readout.</summary>
    private RenderTexture? _albedoProbeRt;
    private bool _albedoProbePending;
    private int _albedoProbeReqGen;
    private int _albedoProbeFrame = int.MinValue;
    /// <summary>Frame the map capture engaged (both modes) — the fast per-frame base-RT probe window starts here.</summary>
    private int _mapEngageFrame = int.MinValue;

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

}
