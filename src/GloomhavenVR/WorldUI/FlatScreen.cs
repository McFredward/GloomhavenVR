using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Floating 2D screen (ROADMAP P3c #6): a world-space quad showing the flat game's
/// full desktop composite for everything that is not physicalized — main menu,
/// guildmaster map, merchant, level-up, and any unconverted window.
///
/// Rendering (STACK CAPTURE, hardware test #5): while the screen is visible EVERY
/// game camera that renders to the backbuffer is retargeted onto one shared
/// RenderTexture shown on the quad (UUVR screen-mirror pattern; UI-ARCH §2.1
/// consequence (a)). Test #5 proved a UICamera-only redirect loses the menu's
/// ambient slideshow/video: 'Main Camera' (clear=Depth, mask 0x20) and the
/// late-created 'MainMenuVideo' camera kept rendering to the backbuffer, which our
/// end-of-frame Blit overwrites — their content reached neither the quad nor the
/// desktop (black background). Unity orders cameras by depth per render target, so
/// the captured cameras compose inside the RT exactly like they did on the
/// backbuffer; only the FIRST camera of the stack gets an opaque SolidColor clear
/// forced (fresh RTs have undefined color; see field comment), all others keep
/// their own clear flags (Depth etc.) so compositing matches the game's intent.
/// The per-tick capture sweep also catches cameras created later (MainMenuVideo
/// appears seconds after the menu scene). Cameras already targeting another RT
/// ('GUI 3D Camera' → character assembly RT) and our own rig head camera are left
/// alone. Everything (targetTexture, clear flags) is restored on hide, scene
/// change, VR off and hot reload. Screen-Space-Camera canvases follow their
/// worldCamera into the RT automatically — verified via ilspycmd (GH.Runtime.dll,
/// CanvasManager): <c>private void OnSceneLoaded(Scene scene, LoadSceneMode mode)</c>
/// re-binds <c>persistentUICanvas.worldCamera</c> / <c>tooltipCanvas.worldCamera</c>
/// to the first camera tagged "UICamera" (PATCH-TARGETS §1.7 ✅).
///
/// Coordination with <see cref="Core.VRCameraPolicy"/>: complementary, no fight —
/// the policy owns exactly <c>stereoTargetEye</c>/XR tracking, this class owns
/// exactly <c>targetTexture</c>/clear flags of captured cameras. A camera can be
/// stereo-None AND render into our RT.
///
/// SCREEN LAYER SPLIT (hardware test #18): Screen-Space-Camera canvases render ONLY
/// through their assigned camera — no other camera (in particular no stereo mirror)
/// can ever reproduce them, so a right-eye RT composed by mirrors alone loses the
/// entire menu UI (test #18: left-eye-only menu, once the video depth layer replaced
/// the stereo suspension that had masked it). While [WorldUI] ScreenLayerSplit is on,
/// the captured stack is therefore split in two:
///
/// - UI GLASS LAYER: UI-classified cameras (UICamera-tagged / orthographic — the
///   canvases' worldCamera targets) are retargeted onto a mod-owned UI RT cleared to
///   TRANSPARENT; their canvases follow automatically. The screen quad shows this RT
///   alpha-blended ("glass"), identical in both eyes, exactly at the pointer plane.
/// - BACKGROUND LAYER: 3D perspective cameras keep compositing the left RT — with
///   <see cref="FlatScreenStereo"/> mirrors producing the right RT from WORLD-space
///   content (which mirrors CAN render), or, while a camera-plane video plays, both
///   eyes showing shifted copies of the left RT (its VIDEO DEPTH SHIFT) — shown on
///   an opaque background quad a few cm BEHIND the glass, so the UI always visibly
///   floats in front even when parallax is subtle.
///
/// The pointer target stays the screen quad at the screen plane (the single
/// authoritative quad — TickPointer/TickPoke and the RT pixel mapping are
/// untouched; both RTs share one resolution). FALLBACK (config off, UI RT creation
/// failure, no UI camera captured for a sustained period): back to the proven
/// single-RT path — all cameras composite the left RT, the screen quad goes opaque
/// and stereo stays OFF (mirrors cannot carry the UI) — degraded but never
/// one-eyed, never black. The no-UI fallback is SCENE-SCOPED (hardware test #20:
/// the UI-less loading scene between intro and menu latched it for the whole Show,
/// which kept the split — and with it stereo AND the video depth layer — off for
/// the entire main menu): its latch re-arms on the next scene change or the moment
/// a UI-classified camera IS captured, so a scene that does have a UI always gets
/// the split back. While stereo SUSPENDS (intro guard / video with the shift path
/// unavailable) the UI cameras temporarily rejoin the left RT and the glass clears transparent,
/// so the single suspended image carries everything, exactly like the pre-split
/// path; the pre-menu intro guard keeps working unchanged.
///
/// STEREO SCREEN (hardware test #15, point 7): while shown in VR AND split, the
/// background renders WITH stereo depth — the 3D cameras keep composing the (left)
/// RT and <see cref="FlatScreenStereo"/> shadows each with an eye-offset,
/// convergence-shifted mirror into a right-eye RT; a Camera.onPreRender hook swaps
/// the BACKGROUND quad texture per MultiPass eye pass. Pointer mapping, virtual
/// mouse and the desktop mirror below are unaffected. [WorldUI] StereoScreen=false
/// (or ScreenDepthStrength=0) never engages any of it: the split still works, the
/// background is simply mono.
///
/// DESKTOP MIRROR (menu-blackscreen fix): while the RT redirect is active nothing
/// would reach the desktop backbuffer (the XR mirror shows an HMD eye, which shows
/// the quad at best). <see cref="OnEndOfFrame"/> — driven by the WorldUI driver's
/// WaitForEndOfFrame loop, i.e. after Unity's own XR mirror-view blit — blits the
/// left RT to the backbuffer every frame and alpha-composites the UI RT over it
/// while the split routes, so the monitor always shows the complete 2D UI and
/// stays mouse-operable as a fallback.
///
/// PRE-MENU GATE (menu-blackscreen fix): the game boots scene 0 (Bootstrap) → scene
/// "Intro" (IntroPlayer: VideoPlayer + logos) → "Gloomhaven_unified" (main menu);
/// all Single-mode loads (verified via decompiled GH.Runtime Bootstrap.ShowSplash).
/// The screen stays fully hands-off in scene 0 / "Intro" — the intro renders
/// vanilla — and engages on the first real menu scene. While gated, the HMD shows
/// the menu rig's void ([Rig] VoidColor) plus a small "starting…" indicator.
///
/// XR EXCLUSION: owned centrally by <see cref="Core.VRCameraPolicy"/> (pumped by the
/// rig driver) — in VR only the rig head camera renders stereo; the UICamera and any
/// other camera are forced to StereoTargetEyeMask.None there, and restored on VR-off.
/// The per-UICamera guard this class used to run was removed (one owner, one policy;
/// docs/CAMERA-POLICY.md §1).
///
/// Pointer: the DOMINANT hand ray (Phase-2 <see cref="RayInteractor"/> pick pose) is
/// intersected with the screen plane in code (no physics collider — keeps clear of
/// Phase-3a's selection ray masks), hit UV × RT resolution → the P2
/// <see cref="VirtualMouse"/> bridge (<c>WarpTo</c>; trigger press/release →
/// <c>Press</c>/<c>Release</c>). Everything downstream (InControl module,
/// ClickTracker, IsPointerOverUI, tooltips) works untouched.
///
/// CLICK LATCH (hardware test #6, requirement 1): at 1.6 m quad distance on a
/// 2560×1440 RT, sub-degree hand tremor moves the projected pixel by dozens of px
/// between press and release. The game's uGUI release path only clicks when the
/// release raycast still hits the SAME IPointerClickHandler as the press
/// (decompiled/InControl/InControl/InControlInputModule.cs:506-512), and any
/// IDragHandler ancestor (scroll lists) starts a drag past
/// EventSystem.pixelDragThreshold, which cancels the pending click
/// (decompiled/InControl/InControl/PointerInputModuleExtended.cs:336-347:
/// ProcessDrag → pointerUp + eligibleForClick = false). So while the trigger is
/// held the warp position is FROZEN at the press pixel; deliberate ray movement
/// (>[WorldUI] DragUnlockDegrees for DragUnlockSeconds) opens the latch into a
/// real drag. Press/release/click-vs-drag decisions are logged at Info.
///
/// POKE CLICK (requirement 2): the index fingertip crossing the quad plane clicks
/// at the poked RT position — quad-local hit → RT pixel → latched warp+press,
/// release on withdraw. The quad is an RT surface, not a canvas, so the mapping
/// lives here (PokeInteractor only serves colliders + registered canvases).
///
/// HANDEDNESS (requirement 4): only the dominant hand ([Hands] PrimaryHand) has a
/// beam and clicks (Menu2D per-hand policy, HandsModule). In Menu2D the
/// NON-dominant trigger switches dominance to that hand (persisted config, haptic
/// confirm) — cards/fan/wrist HUD follow the same setting automatically.
///
/// Show policy (P6): auto-appears in <see cref="VRMode.Menu2D"/> (config) — which
/// since test #8 covers EVERYTHING pre-scenario including the campaign/world map —
/// hides in scenario modes; in <see cref="VRMode.ModalUI"/> it appears when a
/// <see cref="ModalFallback"/> window is open or no converted dialog owns the modal.
/// MANUAL CHORD (test #8 self-rescue): holding the non-dominant A/X for
/// [WorldUI] ManualScreenChordSeconds during a scenario toggles the screen in ANY
/// scenario mode, overriding the policy — the player can always reach the 2D UI.
/// </summary>
internal sealed class FlatScreen
{
    private const float ScreenAspect = 16f / 9f;

    /// <summary>Intro scene name (decompiled GH.Runtime Bootstrap.ShowSplash: "Intro").</summary>
    private const string IntroSceneName = "Intro";

    /// <summary>
    /// Real-meter gap between the UI glass (screen plane) and the background quad
    /// behind it. Widened 3 → 6 cm after test #19: the PHYSICAL glass-vs-background
    /// separation is the strongest depth cue of the whole split and costs nothing —
    /// the background quad is grown by 1 + gap/distance so it subtends the same
    /// angle from the head (no edge inset), and the pointer plane stays the glass.
    /// </summary>
    private const float BackplaneGapMeters = 0.06f;

    /// <summary>Sustained routing time without any captured UI camera before the split falls back.</summary>
    private const float NoUiFallbackSeconds = 3f;

    private GameObject? _quad;
    private Renderer? _quadRenderer;
    /// <summary>Opaque single-RT material (screen quad in fallback; background quad in split mode).</summary>
    private Material? _screenMaterial;

    // ---- screen layer split (test #18; class doc SCREEN LAYER SPLIT) -----------------------
    /// <summary>Background quad a few cm behind the glass (split mode only).</summary>
    private GameObject? _backQuad;
    private Renderer? _backRenderer;
    /// <summary>Transparent UI RT the glass shows (null = split not engaged).</summary>
    private RenderTexture? _uiRt;
    /// <summary>Alpha-blended glass material (screen quad in split mode; doubles as the desktop UI-composite blit).</summary>
    private Material? _glassMaterial;
    /// <summary>True while UI cameras actually render the UI RT (false during stereo suspension / pre-menu).</summary>
    private bool _splitRouting;
    /// <summary>The split failed this Show (UI RT/shader creation loss) — single-RT fallback until the next Show.</summary>
    private bool _splitFailed;
    /// <summary>
    /// No-UI watchdog latch (<see cref="TickNoUiWatchdog"/>): the CURRENT scene
    /// demonstrably has no retargetable UI camera — single-RT fallback. Unlike
    /// <see cref="_splitFailed"/> the evidence dies with the scene (hardware test
    /// #20: the UI-less loading scene latched the fallback for the whole Show and
    /// the main menu never got its split/stereo/video-depth back), so it re-arms on
    /// stack release (scene change) or when a UI-classified camera IS captured.
    /// </summary>
    private bool _splitNoUi;
    /// <summary>Since when the routing stack has contained no UI camera (fallback watchdog).</summary>
    private float _noUiSince = -1f;
    /// <summary>Last logged stereo-gate reason (change-deduped diagnostic, test #20).</summary>
    private string? _stereoGateReason;
    /// <summary>True while the virtual mouse left button is held by us (drag or virtualmouse mode).</summary>
    private bool _vmPressed;
    private RenderTexture? _rt;
    private bool _visible;
    private bool _pressing;
    private bool _mirrorLogged;

    // ---- ITEM 9: desktop (flat monitor) = clean LEFT-EYE mirror, nothing else --------------
    /// <summary>
    /// [WorldUI] DesktopMirrorLeftEye — local default (the WorldUIConfig entry is not
    /// owned by this branch; see the returned snippet to wire the live toggle). While
    /// true the flat monitor mirrors ONLY the HMD's LEFT eye: the game-view mirror mode
    /// is pinned to <c>GameViewRenderMode.LeftEye</c> AND the end-of-frame desktop
    /// composite blit (the old 2D-menu overlay / blackscreen fallback) is skipped, so no
    /// menu composite, both-eyes/side-by-side, or UI overlay reaches the desktop.
    /// </summary>
    private static bool DesktopMirrorLeftEye => WorldUIConfig.DesktopMirrorLeftEye.Value;
    /// <summary>True while we currently hold <see cref="UnityEngine.XR.XRSettings.gameViewRenderMode"/> at LeftEye.</summary>
    private bool _mirrorModeApplied;
    /// <summary>Original mirror mode captured once so VR-off / hot-reload restores it.</summary>
    private bool _mirrorModeCaptured;
    private UnityEngine.XR.GameViewRenderMode _originalMirrorMode;
    /// <summary>One-time log of the chosen mirror mode (confirms on the next hardware log).</summary>
    private bool _mirrorModeLogged;

    // ---- ITEM 9 (SCENARIO STATE): scrub game cameras off the desktop backbuffer -------------
    /// <summary>
    /// Offscreen sink RT for scrubbed backbuffer cameras (Screen-sized so their pixel
    /// dimensions — Camera.main.ScreenToWorldPoint etc. — are unchanged; content discarded).
    /// </summary>
    private RenderTexture? _scrubRt;
    /// <summary>
    /// Game cameras we retargeted OFF the backbuffer (their original targetTexture was
    /// null → restore to null). Distinct from <see cref="CapturedSet"/>: this list runs
    /// only while the flat screen is HIDDEN (scenario), where CaptureStack does not.
    /// </summary>
    private readonly System.Collections.Generic.List<Camera> _scrubbed = new(8);
    /// <summary>True while the scenario desktop-scrub holds cameras off the backbuffer.</summary>
    private bool _scrubActive;
    /// <summary>Scene name whose backbuffer inventory we already logged (re-log once per scene/state).</summary>
    private string? _scrubInventoryScene;

    // ---- ITEM 1: hands in front of the menu/intro screen -----------------------------------
    /// <summary>Shader-default render queue of <see cref="_screenMaterial"/> (captured on create).</summary>
    private int _screenMaterialQueueDefault = -1;
    /// <summary>One-shot ground-truth log of the hands' render state while the menu screen is shown.</summary>
    private bool _handsDiagLogged;

    // ---- click latch (test #6, requirement 1) --------------------------------------------
    /// <summary>True while the held press is frozen at its press pixel (click, not drag).</summary>
    private bool _latched;
    private Vector2 _latchedLocal;     // quad-local x/y of the press (reticle while latched)
    private Vector2 _latchedPixel;     // RT pixel of the press (re-warped while latched)
    private Vector3 _pressDirection;   // world ray direction at press time
    private float _dragOverSince = -1f;

    // ---- campaign-map pan (laser + trigger grab-drag) --------------------------------------
    /// <summary>True while a held trigger over the campaign map is panning the parchment (via
    /// <see cref="FlatScreenStereo.UpdateMapPan"/>) instead of dragging a uGUI widget. A still
    /// trigger keeps the latch closed → the normal click path selects the location under it.</summary>
    private bool _mapPanGesture;
    /// <summary>Pointer travel (RT px) from the press pixel that flips a held trigger over the map from
    /// a click into a grab-pan. Small so the map feels grabbed immediately, large enough to protect taps.</summary>
    private const float MapPanStartPixels = 22f;

    // ---- poke click (requirement 2) --------------------------------------------------------
    // Meters at scale 1 (multiplied by the poking hand's WorldScale). Contact/release
    // hysteresis mirrors PokeInteractor's canvas path (press at plane contact, release
    // ~2 cm in front, drop when far behind).
    private const float PokeContactMeters = 0.01f;
    private const float PokeReleaseMeters = 0.03f;
    private const float PokeThroughMeters = 0.08f;
    private const float PokeDragUnlockMeters = 0.015f;

    private bool _pokePressing;
    private VRHand? _pokeHand;
    private bool _pokeLatched;
    private Vector3 _pokePressPoint; // world, on the screen plane
    private Vector2 _pokePressPixel; // RT pixel of the poke press (re-warped while latched)

    // ---- execute-mode drag session (flat-menu sliders / scrollbars / scroll-rects) ----------
    /// <summary>Distinct pointer id for the flat-menu drag (clear of mouse -1..-3 and the poke/laser ids).</summary>
    private const int ScreenDragPointerId = -120;
    /// <summary>Minimum spacing of the flat-menu drag begin/end log lines.</summary>
    private const float ScreenDragLogSeconds = 0.5f;

    private UnityEngine.EventSystems.PointerEventData? _screenDragData;
    private GameObject? _screenDragPress;   // object that took pointerDown (pointerUp target)
    private GameObject? _screenDragTarget;  // IDragHandler under the press (Slider/Scrollbar/ScrollRect)
    private bool _screenDragActive;         // a down+drag session is live
    private bool _screenDragBegun;          // beginDrag already fired
    private Vector2 _screenDragLastPos;     // last dispatched pointer pixel (delta + pointerUp position)
    private float _lastScreenDragLog = float.NegativeInfinity;

    /// <summary>One captured backbuffer camera + everything needed to restore it.</summary>
    private sealed class CapturedCamera
    {
        public Camera Camera = null!;
        public CameraClearFlags OriginalClearFlags;
        public Color OriginalBackground;
        /// <summary>Last observed enabled state (transition diagnostics, test #10).</summary>
        public bool WasEnabled;
        /// <summary>True while we demote this camera's fullscreen SolidColor clear to Depth (see <see cref="TickStackClears"/>).</summary>
        public bool Demoted;
        /// <summary>UI-stack classification at capture time (class doc SCREEN LAYER SPLIT).</summary>
        public bool IsUi;
    }

    // Captured camera stack (I1, hardware test #5). The list is reused across
    // frames; a CapturedCamera record only allocates when a NEW camera is first
    // captured (rare event — scene load / MainMenuVideo appearing).
    private readonly System.Collections.Generic.List<CapturedCamera> _captured = new(8);

    /// <summary>The background stack's base (lowest-depth) camera — forced to an OPAQUE SolidColor clear.</summary>
    private CapturedCamera? _base;

    /// <summary>The UI stack's base camera — forced to a TRANSPARENT SolidColor clear while routing.</summary>
    private CapturedCamera? _uiBase;

    /// <summary>Per-eye rendering for the screen (test #15 #7) — inert unless [WorldUI] StereoScreen.</summary>
    private readonly FlatScreenStereo _stereo = new();

    /// <summary>Cameras currently captured, with their records (per-camera target re-assert + diagnostics).</summary>
    private static readonly System.Collections.Generic.Dictionary<Camera, CapturedCamera> CapturedSet = new();

    /// <summary>True while the FlatScreen has redirected this camera into one of its RTs.</summary>
    internal static bool IsCaptured(Camera cam) => CapturedSet.ContainsKey(cam);

    // Base-clear rationale (hardware test #4, P3b): the game's menu cameras ship
    // clearFlags=Depth — rendering that into a fresh RT leaves the COLOR buffer
    // (incl. alpha ≈ 0) undefined, so an alpha-blended quad shows nothing in the
    // HMD while the desktop Blit (which ignores alpha) looks perfect. The stack's
    // lowest-depth camera therefore gets an OPAQUE SolidColor clear forced (also
    // fixes RT garbage); every other camera keeps its own clear flags.
    private static readonly Color OpaqueBlack = new(0f, 0f, 0f, 1f);

    /// <summary>UI-stack base clear (split): the glass shows exactly what the UI cameras drew.</summary>
    private static readonly Color TransparentBlack = new(0f, 0f, 0f, 0f);

    // Placement anchors: re-place instantly when the head camera or the rig moves
    // (menu rig rebuild / recenter), instead of waiting for the lazy 45° follow.
    private Camera? _placedHead;
    private Vector3 _placedRigPos;

    // Lazy follow (P3a): only glide the screen back in front after the gaze has
    // been >45° off it for >1 s (prevents chasing quick glances).
    private const float FollowAngleDegrees = 45f;
    private const float FollowDwellSeconds = 1f;
    private const float FollowSettledDegrees = 5f;
    private float _offGazeSince = -1f;
    private bool _gliding;

    // Pre-menu "starting…" indicator (HMD-side sign of life while the intro plays flat).
    private GameObject? _indicator;

    // Manual screen chord (P6 self-rescue): forced-visible latch + per-press fire guard.
    private bool _manualShow;
    private bool _chordFired;
    private bool _chordArmingLogged;

    /// <summary>
    /// True while the manual chord forces the full screen (P8): ModalFallback reads
    /// this to RELEASE its floating window conversions — a window re-parented onto a
    /// world-space host would be missing from the screen's RT composite, so the
    /// universal rescue must always put the windows back into the 2D UI first.
    /// </summary>
    internal static bool ManualScreenActive { get; private set; }

    public FlatScreen()
    {
        // P5 (MISSION A.2): scene loads re-wire the game's UI cameras (CanvasManager.
        // OnSceneLoaded re-binds worldCamera) — release the whole captured stack on
        // the bus event and re-capture next Tick instead of waiting for old cameras
        // to die.
        Core.Events.VREvents.SceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Core.Events.SceneLoadedEvent e) => ReleaseStack();

    public void Tick()
    {
        // ITEM 9: keep the flat monitor a clean left-eye mirror while VR runs (must run
        // regardless of the flat-screen's own visibility — it is a global desktop concern).
        TickDesktopMirrorMode();
        // ITEM 9 (scenario): while the flat screen is HIDDEN, route the game's own
        // backbuffer cameras (which the stereo policy forced desktop-only) off the
        // monitor so nothing but the HMD left-eye mirror reaches it.
        TickDesktopCameraScrub();

        // Test #11: the intro CAN show on the screen now — the pre-menu gate existed
        // because the early quad died with the Single-mode scene loads, which is long
        // fixed (DontDestroyOnLoad + external-destroy rebuild). [WorldUI] ShowIntro
        // (default on) shows logos/intro video in VR; off = old desktop-only intro
        // with the void indicator.
        bool preMenu = IsPreMenuScene() && !WorldUIConfig.ShowIntro.Value;

        TickStartingIndicator(preMenu);

        // The quad can be destroyed behind our back (a Single-mode scene load before
        // it was made persistent, an explorer tool, …) — treat it as hidden and let
        // Show() rebuild everything instead of ticking a dead screen forever. This
        // was the P3c-era root cause of the black menu: the quad died with the
        // "Intro" scene unload while the UICamera stayed redirected into the RT.
        if (_visible && _quad == null)
        {
            VRLog.Warn("WorldUI", "FlatScreen quad was destroyed externally — rebuilding.");
            _visible = false;
        }

        TickManualChord();
        ManualScreenActive = _manualShow;

        bool want = !preMenu && WantVisible();
        if (want && !_visible)
            Show();
        else if (!want && _visible)
            Hide();

        if (!_visible)
            return;

        CaptureStack();

        FollowHead();
        TickPointer();
        TickBackdropDepth();
    }

    // ---- ITEM 1: hands visible in front of the menu / intro screen --------------------------

    /// <summary>
    /// ITEM 1 FIX + DIAGNOSTIC. In MENU/INTRO the rig head camera renders the mod layer
    /// ONLY, so the only things it draws are the flat-screen quads and the mod's own
    /// hands. The opaque backdrop (single-RT screen quad in fallback; background quad in
    /// split) uses <c>Hidden/BlitCopy</c> — ZTest Always / ZWrite Off — which IGNORES
    /// depth and, rendering LATER than the (physically closer) hands inside the Geometry
    /// queue, overwrote them: the screen appeared to cover hands held in front of it.
    ///
    /// Fix: in menu-like modes push the backdrop material into the <c>Background</c>
    /// queue so it renders BEFORE the hands — the opaque glove then depth/paints over
    /// it and the transparent glass (Sprites/Default, ZTest LEqual) already respects the
    /// closer hand's depth — so the hands regain their place in front. Outside menu
    /// modes the summoned screen must stay ON TOP of the 3D world, so the shader default
    /// queue is restored (scenario behaviour unchanged).
    ///
    /// The glass quad needs no change: it is genuinely transparent (ZTest LEqual), so an
    /// opaque hand that wrote depth already occludes it, and a transparent (procedural)
    /// hand sorts in front of it by distance.
    /// </summary>
    private void TickBackdropDepth()
    {
        if (_screenMaterial == null)
            return;

        bool menuLike = VRModeStateMachine.CurrentMode == VRMode.Menu2D || IsPreMenuScene();
        int wantQueue = menuLike
            ? (int)UnityEngine.Rendering.RenderQueue.Background
            : _screenMaterialQueueDefault >= 0
                ? _screenMaterialQueueDefault
                : (int)UnityEngine.Rendering.RenderQueue.Geometry;
        if (_screenMaterial.renderQueue != wantQueue)
        {
            _screenMaterial.renderQueue = wantQueue;
            VRLog.Info("WorldUI", $"ITEM1: flat-screen backdrop render queue → {wantQueue} " +
                                  (menuLike
                                      ? "(menu/intro — renders before the mod hands so hands held in front show)."
                                      : "(scenario — shader default; summoned screen stays over the 3D world)."));
        }

        if (menuLike && !_handsDiagLogged && (VRHands.Left != null || VRHands.Right != null))
        {
            _handsDiagLogged = true;
            LogHandsVisibilityDiagnostic();
        }
    }

    /// <summary>
    /// ITEM 1 ground truth (menu screen shown): logs the flat-screen material state
    /// (render queue + shader, from which its ZTest is known) and each hand glove
    /// renderer's enabled/isVisible/layer/bounds — so an in-game log reveals whether the
    /// hands were rendered-but-occluded (queue), culled (layer/enabled) or off-screen
    /// (bounds), even without an HMD capture.
    /// </summary>
    private void LogHandsVisibilityDiagnostic()
    {
        Shader? backShader = _screenMaterial != null ? _screenMaterial.shader : null;
        Shader? glassShader = _glassMaterial != null ? _glassMaterial.shader : null;
        VRLog.Info("WorldUI", "ITEM1 diag — flat-screen materials: backdrop shader " +
            $"'{(backShader != null ? backShader.name : "null")}' queue " +
            $"{(_screenMaterial != null ? _screenMaterial.renderQueue : -1)} " +
            "(Hidden/BlitCopy = ZTest Always / ZWrite Off), glass shader " +
            $"'{(glassShader != null ? glassShader.name : "null")}' queue " +
            $"{(_glassMaterial != null ? _glassMaterial.renderQueue : -1)} " +
            "(Sprites/Default = ZTest LEqual / ZWrite Off), split " +
            $"{(SplitActive ? (_splitRouting ? "routing" : "engaged") : "off")}, mod layer {Core.VRLayers.ModLayer}.");
        LogHandRenderers(VRHands.Left, "Left");
        LogHandRenderers(VRHands.Right, "Right");
    }

    private static void LogHandRenderers(VRHand? hand, string label)
    {
        if (hand == null || hand.Rig == null || hand.Rig.Root == null)
        {
            VRLog.Info("Hands", $"ITEM1 diag — {label} glove: no hand/rig present.");
            return;
        }
        Renderer[] renderers = hand.Rig.Root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            VRLog.Info("Hands", $"ITEM1 diag — {label} glove: rig root '{hand.Rig.Root.name}' has NO renderers.");
            return;
        }
        Renderer r = renderers[0];
        Material? m = r.sharedMaterial;
        VRLog.Info("Hands", $"ITEM1 diag — {label} glove ({renderers.Length} renderer(s)); first '{r.name}': " +
            $"enabled={r.enabled}, isVisible={r.isVisible}, layer={r.gameObject.layer} " +
            $"(mod layer {Core.VRLayers.ModLayer}), activeInHierarchy={r.gameObject.activeInHierarchy}, " +
            $"bounds c={r.bounds.center} e={r.bounds.extents}, " +
            $"shader='{(m != null && m.shader != null ? m.shader.name : "null")}', " +
            $"queue={(m != null ? m.renderQueue : -1)}.");
    }

    /// <summary>
    /// Redirect every backbuffer game camera into our RTs (I1, hardware test #5) —
    /// UI-classified cameras onto the glass RT while the split routes, everything
    /// else onto the background RT — and keep each stack's lowest-depth camera on
    /// its forced SolidColor clear (opaque for the background, transparent for the
    /// UI; P3b — see field comments). Cheap per-tick re-assert: game code may
    /// rewrite targetTexture or clearFlags at any time, and new cameras
    /// (MainMenuVideo) appear mid-scene. Excluded: our rig head camera and cameras
    /// already targeting a different RT ('GUI 3D Camera' → character-assembly RT,
    /// per the test-#5 camera inventory). Originals are recorded on first capture
    /// and restored by <see cref="ReleaseStack"/>. No per-frame allocations: shared
    /// scan buffer + reused list; records allocate only when a new camera is first
    /// captured.
    /// </summary>
    private void CaptureStack()
    {
        if (_rt == null)
            return;

        TickSplitLifecycle();

        Camera? head = Rig.VRRigDriver.HeadCamera;

        // 1. Capture new backbuffer cameras / re-assert the redirect on known ones.
        int count = Core.VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || (head != null && cam == head))
                continue;
            if (CapturedSet.TryGetValue(cam, out CapturedCamera known))
            {
                RenderTexture? want = TargetFor(known);
                if (want != null && cam.targetTexture != want) // game code rewrote it — re-assert
                    cam.targetTexture = want;
                continue;
            }
            if (cam.targetTexture != null)
                continue; // renders to its own RT (e.g. 'GUI 3D Camera') — not ours

            var record = new CapturedCamera
            {
                Camera = cam,
                OriginalClearFlags = cam.clearFlags,
                OriginalBackground = cam.backgroundColor,
                WasEnabled = true, // Camera.allCameras only lists enabled cameras
                IsUi = IsUiCamera(cam),
            };
            _captured.Add(record);
            CapturedSet.Add(cam, record);
            cam.targetTexture = TargetFor(record);
            // A UI camera exists after all — the no-UI watchdog's evidence is void
            // (test #20): re-arm so TickSplitLifecycle re-engages on the next sweep.
            if (record.IsUi && _splitNoUi)
            {
                _splitNoUi = false;
                VRLog.Info("WorldUI", $"Screen layer split no-UI fallback re-armed — UI camera " +
                                      $"'{cam.name}' captured; the split re-engages on the next sweep.");
            }
            // Full disposition line (test #10): rect + clear + depth + mask make the RT
            // composite reconstructable from the log alone.
            Rect r = cam.rect;
            VRLog.Info("WorldUI", $"FlatScreen stack capture: '{cam.name}' → RenderTexture " +
                                  $"(depth {cam.depth:F1}, clear {record.OriginalClearFlags} " +
                                  $"'{record.OriginalBackground}', rect ({r.x:F2},{r.y:F2},{r.width:F2},{r.height:F2}), " +
                                  $"mask 0x{cam.cullingMask:X8}, {(record.IsUi ? "UI" : "background")} layer).");
        }

        // 2. Compact dead entries (scene unloads destroy cameras behind our back).
        for (int i = _captured.Count - 1; i >= 0; i--)
        {
            if (_captured[i].Camera == null)
            {
                if (_captured[i] == _base)
                    _base = null;
                if (_captured[i] == _uiBase)
                    _uiBase = null;
                CapturedSet.Remove(_captured[i].Camera);
                _captured.RemoveAt(i);
            }
        }

        // 3. Per-stack base selection + clear policy.
        SelectBases();
        TickStackClears();

        // 4. Stereo screen (test #15 #7): mirror the captured 3D cameras into the
        //    right-eye RT — ONLY while the layer split carries the UI on the glass
        //    (mirrors can never render Screen-Space-Camera canvases; test #18:
        //    left-eye-only menu). Runs AFTER the clear policy so mirrors copy the
        //    EFFECTIVE clear flags. Pre-menu scenes engage the stereo intro guard
        //    (test #17 one-eyed intro: the Intro scene's render path is
        //    scene-serialized and unverifiable — FlatScreenStereo forces identical
        //    eyes there, zero shift: the intro must remain verified-identical).
        bool preMenu = IsPreMenuScene();
        if (SplitActive)
        {
            _stereoGateReason = null; // gate open — the diagnostic re-logs on the next block
            _stereo.Tick(_rt, _backRenderer, preMenu);
            if (_stereo.Active)
            {
                _stereo.BeginStackSync();
                for (int i = 0; i < _captured.Count; i++)
                {
                    CapturedCamera c = _captured[i];
                    if (c.Camera != null && !c.IsUi) // UI never mirrors — the glass shows it in both eyes
                        _stereo.SyncCamera(c.Camera);
                }
                _stereo.EndStackSync();
            }

            // Routing follows the suspension decided THIS tick (EndStackSync):
            // suspended (intro guard / unavailable shift path) → the UI rejoins the
            // left RT so the single suspended image carries everything. Applying the flip
            // here is still same-frame — cameras render after Update — so no frame
            // is ever one-eyed or UI-less; bases/clears are recomputed immediately.
            bool routing = !preMenu && !_stereo.Suspended;
            if (routing != _splitRouting)
            {
                SetSplitRouting(routing, routing
                    ? "no suspension in effect"
                    : preMenu ? "pre-menu scene" : "stereo suspended");
                SelectBases();
                TickStackClears();
            }
            TickNoUiWatchdog();
        }
        else
        {
            if (_stereo.Active)
                _stereo.Deactivate("screen layer split inactive");
            // Gate diagnostic (test #20: the flat menu was exactly this state and the
            // log never named it): with the split down, the WHOLE stereo/video-depth
            // chain is structurally off — one change-deduped line names the reason so
            // a flat screen in a hardware log is attributable at a glance.
            string gate = !WorldUIConfig.ScreenLayerSplit.Value
                ? "ScreenLayerSplit off (config)"
                : _splitFailed ? "split creation failed this Show"
                : _splitNoUi ? "no-UI watchdog latched (re-arms on scene change / UI capture)"
                : "split not engaged (quad/RT not ready)";
            if (gate != _stereoGateReason)
            {
                _stereoGateReason = gate;
                VRLog.Info("WorldUI", $"Stereo screen + video depth layer unavailable — {gate}; " +
                                      "the screen renders the single mono RT.");
            }
        }
    }

    /// <summary>
    /// UI-stack classification (class doc SCREEN LAYER SPLIT): the cameras whose
    /// Screen-Space-Camera canvases hold the 2D UI. UICamera-tagged (CanvasManager
    /// binds every overlay canvas to the first such camera — PATCH-TARGETS §1.7) or
    /// orthographic cameras are UI; everything else is 3D background. Identical to
    /// the pre-split stereo-mirror MONO classification, so the split only MOVES
    /// layers, it never reclassifies a camera.
    /// </summary>
    private static bool IsUiCamera(Camera cam) => cam.CompareTag("UICamera") || cam.orthographic;

    /// <summary>The RT this captured camera should render into under the current routing.</summary>
    private RenderTexture? TargetFor(CapturedCamera c) =>
        c.IsUi && _splitRouting && _uiRt != null ? _uiRt : _rt;

    /// <summary>
    /// Each stack's FIRST camera gets a forced SolidColor clear (fresh RTs have
    /// undefined color — see field comments): opaque black for the background RT,
    /// transparent for the glass RT. Unity renders cameras targeting the same RT in
    /// ascending depth order, so pick the lowest depth per stack. Depth ties exist
    /// (test-#5 inventory: 'Main Camera' and 'UI Camera' both at depth 1.0) — with
    /// routing OFF the UICamera-tagged camera always composites LAST per game intent
    /// (CanvasManager binds all overlay canvases to it), so on a tie it must NOT be
    /// the background base or its forced clear would erase the other cameras' output.
    /// </summary>
    private void SelectBases()
    {
        CapturedCamera? newBase = null;
        CapturedCamera? newUiBase = null;
        for (int i = 0; i < _captured.Count; i++)
        {
            CapturedCamera c = _captured[i];
            if (c.IsUi && _splitRouting)
            {
                if (newUiBase == null || c.Camera.depth < newUiBase.Camera.depth)
                    newUiBase = c;
            }
            else if (newBase == null
                || c.Camera.depth < newBase.Camera.depth
                || (c.Camera.depth == newBase.Camera.depth && newBase.Camera.CompareTag("UICamera")))
            {
                newBase = c;
            }
        }

        if (newBase != _base)
        {
            // The previous base (still captured) returns to its own clear flags.
            if (_base != null && _base.Camera != null)
            {
                _base.Camera.clearFlags = _base.OriginalClearFlags;
                _base.Camera.backgroundColor = _base.OriginalBackground;
            }
            _base = newBase;
            if (_base != null)
                VRLog.Info("WorldUI", $"FlatScreen stack base: '{_base.Camera.name}' (depth {_base.Camera.depth:F1}) " +
                                      $"clears the RT (clear {_base.OriginalClearFlags} → SolidColor opaque black).");
        }

        if (newUiBase != _uiBase)
        {
            if (_uiBase != null && _uiBase.Camera != null)
            {
                _uiBase.Camera.clearFlags = _uiBase.OriginalClearFlags;
                _uiBase.Camera.backgroundColor = _uiBase.OriginalBackground;
            }
            _uiBase = newUiBase;
            if (_uiBase != null)
                VRLog.Info("WorldUI", $"FlatScreen UI-stack base: '{_uiBase.Camera.name}' " +
                                      $"(depth {_uiBase.Camera.depth:F1}) clears the glass RT " +
                                      $"(clear {_uiBase.OriginalClearFlags} → SolidColor transparent).");
        }
    }

    // ---- screen layer split (class doc SCREEN LAYER SPLIT) ---------------------------------

    /// <summary>True while the split is engaged (glass RT exists; routing may still be off).</summary>
    private bool SplitActive => _uiRt != null;

    /// <summary>
    /// Engage/disengage the split per config + failure state (called at the top of
    /// every capture sweep, so a live [WorldUI] ScreenLayerSplit flip takes effect on
    /// the next tick). Engaging creates the transparent glass RT, the alpha material
    /// and the background quad; any creation failure latches <see cref="_splitFailed"/>
    /// and the single-RT fallback takes over for the rest of this Show.
    /// </summary>
    private void TickSplitLifecycle()
    {
        bool want = WorldUIConfig.ScreenLayerSplit.Value && !_splitFailed && !_splitNoUi
                    && _rt != null && _quad != null && _quadRenderer != null;
        if (want && SplitActive)
        {
            EnsureBackQuad(); // rebuilt if an external tool destroyed it (same policy as the screen quad)
            return;
        }
        if (!want)
        {
            TeardownSplit("ScreenLayerSplit disabled");
            return;
        }

        // Glass RT: same dimensions as the background RT — the pointer pixel mapping
        // and the quad UVs are shared between the two layers by construction.
        var uiRt = new RenderTexture(_rt!.width, _rt.height, 24)
        {
            name = "GloomhavenVR.FlatScreenRT.UI",
            antiAliasing = 1,
        };
        // Alpha-blended glass shader (both verified shipped — see the Show() shader
        // comment); the RT alpha is exactly what the UI cameras leave behind, which
        // is the point: everything they did not draw stays see-through.
        Shader? glassShader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (glassShader == null || !uiRt.Create())
        {
            uiRt.Release();
            Object.Destroy(uiRt);
            _splitFailed = true;
            VRLog.Warn("WorldUI", "Screen layer split FAILED to engage (" +
                                  (glassShader == null ? "no alpha-blended shader found" : "glass RT creation failed") +
                                  ") — single-RT fallback (mono screen, video suspension; never one-eyed).");
            return;
        }
        _uiRt = uiRt;
        ClearUiRt(); // fresh RT color is undefined — the glass must start fully transparent

        _glassMaterial = new Material(glassShader) { mainTexture = _uiRt };
        EnsureBackQuad();
        _quadRenderer!.sharedMaterial = _glassMaterial;

        _noUiSince = -1f;
        VRLog.Info("WorldUI", $"Screen layer split ENGAGED: UI cameras → transparent glass RT " +
                              $"({_uiRt.width}x{_uiRt.height}) on the screen quad (both eyes identical, " +
                              $"pointer plane); 3D cameras stay on the background RT shown " +
                              $"{BackplaneGapMeters * 100f:F0} cm behind it. glass RT {FlatScreenStereo.DescribeRt(_uiRt)} " +
                              "(default read/write — not sRGB-corrected, so the UI is not overbrightened).");
    }

    /// <summary>Create the background quad — or re-create it after an external destroy.</summary>
    private void EnsureBackQuad()
    {
        if (_backQuad != null)
            return;
        _backQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _backQuad.name = "GloomhavenVR.FlatScreen.Background";
        Object.DontDestroyOnLoad(_backQuad); // survives Single-mode scene loads like the screen quad
        Object.Destroy(_backQuad.GetComponent<Collider>());
        _backRenderer = _backQuad.GetComponent<Renderer>();
        _backRenderer.sharedMaterial = _screenMaterial;
        VRLayers.Apply(_backQuad);
        PlaceScreen(instant: true); // the fresh quad needs its pose now, not on the next head move
    }

    /// <summary>
    /// Fallback path (class doc): give every UI camera the background RT back
    /// (single-RT composite, exactly the pre-split behavior), drop the glass state
    /// and the background quad, and put the opaque single-RT material back on the
    /// screen quad. FlatScreenStereo is deactivated by the next capture sweep
    /// (split inactive → mono — mirrors cannot carry the UI).
    /// </summary>
    private void TeardownSplit(string reason)
    {
        if (!SplitActive)
            return;
        SetSplitRouting(false, reason);
        if (_uiBase != null && _uiBase.Camera != null)
        {
            _uiBase.Camera.clearFlags = _uiBase.OriginalClearFlags;
            _uiBase.Camera.backgroundColor = _uiBase.OriginalBackground;
        }
        _uiBase = null;
        if (_backQuad != null)
        {
            Object.Destroy(_backQuad);
            _backQuad = null;
            _backRenderer = null;
        }
        if (_glassMaterial != null)
        {
            Object.Destroy(_glassMaterial);
            _glassMaterial = null;
        }
        if (_uiRt != null)
        {
            _uiRt.Release();
            Object.Destroy(_uiRt);
            _uiRt = null;
        }
        if (_quadRenderer != null && _screenMaterial != null)
            _quadRenderer.sharedMaterial = _screenMaterial;
        _noUiSince = -1f;
        VRLog.Info("WorldUI", $"Screen layer split RELEASED ({reason}) — single-RT path " +
                              "(all cameras composite the background RT; screen quad opaque).");
    }

    /// <summary>
    /// Flip which RT the UI cameras render (class doc): ON = glass RT (UI floats in
    /// front, both eyes); OFF (stereo suspension / pre-menu) = they rejoin the left
    /// RT so the single suspended image carries everything — the glass is cleared to
    /// fully transparent so no stale UI lingers in front of it.
    /// </summary>
    private void SetSplitRouting(bool routing, string reason)
    {
        if (_splitRouting == routing)
            return;
        _splitRouting = routing;

        for (int i = 0; i < _captured.Count; i++)
        {
            CapturedCamera c = _captured[i];
            if (!c.IsUi || c.Camera == null)
                continue;
            RenderTexture? want = TargetFor(c);
            if (want != null && c.Camera.targetTexture != want)
                c.Camera.targetTexture = want;
        }
        if (!routing)
            ClearUiRt();

        VRLog.Info("WorldUI", routing
            ? $"Screen layer split routing ON ({reason}) — UI cameras render the glass RT."
            : $"Screen layer split routing OFF ({reason}) — UI cameras rejoin the background RT " +
              "(glass cleared transparent).");
    }

    /// <summary>Clear the glass RT to fully transparent (engage + every routing-off flip).</summary>
    private void ClearUiRt()
    {
        if (_uiRt == null)
            return;
        RenderTexture? previous = RenderTexture.active;
        RenderTexture.active = _uiRt;
        GL.Clear(clearDepth: true, clearColor: true, backgroundColor: TransparentBlack);
        RenderTexture.active = previous;
    }

    /// <summary>
    /// Split fallback trigger "no UI cameras" (class doc): if the routing stack holds
    /// captured cameras but none classified UI for a sustained period, the canvases'
    /// worldCamera is evidently not one we can retarget — release the split so the
    /// proven single-RT path carries the UI instead of risking a one-eyed screen.
    /// </summary>
    private void TickNoUiWatchdog()
    {
        if (!_splitRouting || _captured.Count == 0)
        {
            _noUiSince = -1f;
            return;
        }
        for (int i = 0; i < _captured.Count; i++)
        {
            if (_captured[i].IsUi)
            {
                _noUiSince = -1f;
                return;
            }
        }
        if (_noUiSince < 0f)
        {
            _noUiSince = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime - _noUiSince < NoUiFallbackSeconds)
            return;

        _splitNoUi = true;
        VRLog.Warn("WorldUI", $"Screen layer split: no UI camera captured for {NoUiFallbackSeconds:F0}s " +
                              "while routing — falling back to the single-RT path (the UI would " +
                              "otherwise be invisible to the stereo mirrors). Re-arms on the next " +
                              "scene change or UI-camera capture (test #20: never latch across scenes).");
        TeardownSplit("no UI cameras found");
    }

    /// <summary>
    /// Per-tick clear-flag policy + enabled-transition diagnostics for the captured
    /// stack (test #10).
    ///
    /// BASE: the stack's lowest-depth camera keeps its forced OPAQUE SolidColor clear
    /// (fresh RTs have undefined color — see field comment).
    ///
    /// NON-BASE FULLSCREEN SolidColor DEMOTION ([WorldUI] DemoteOverlaySolidClears):
    /// the campaign map's 'Video Camera' (decompiled GH.Runtime/VideoCamera.cs) is a
    /// depth-5 fullscreen-video surface: enabled ONLY between PlayFullscreenVideo
    /// (VideoCamera.cs:96) and EndReached/Stop/error (VideoCamera.cs:146,164,190,
    /// re-enable path :84/:127), renderMode CameraNearPlane (VideoCamera.cs:97), and
    /// its SolidColor clear exists purely as the black BACKDROP behind/between
    /// fullscreen videos. Rendering last into our RT, that clear wipes the whole
    /// composite (map + UI) whenever the near-plane video blit does not land in the
    /// redirected RT — hardware test #10 saw exactly that (black map, black encounter
    /// backgrounds, 'Video Camera' captured at depth 5.0 clear SolidColor). Demoting
    /// the clear to Depth for NON-BASE fullscreen SolidColor cameras diverges from
    /// vanilla only in the backdrop (letterbox bars would show the scene instead of
    /// black while a video plays) and never hides content: the video frame itself, when
    /// it renders, is drawn on top either way. Sub-rect SolidColor cameras keep their
    /// clear — a viewport-limited background IS legitimate visible content
    /// (camera.rect respected). Original flags are restored on release.
    ///
    /// ENABLED TRANSITIONS: Camera.allCameras only lists enabled cameras, so captured
    /// cameras that get disabled (video ended) vanish silently — log every flip so the
    /// next hardware report shows exactly when 'Video Camera' rendered into the RT.
    /// </summary>
    private void TickStackClears()
    {
        bool demote = WorldUIConfig.DemoteOverlaySolidClears.Value;
        for (int i = 0; i < _captured.Count; i++)
        {
            CapturedCamera c = _captured[i];
            Camera cam = c.Camera;
            if (cam == null)
                continue;

            // Enabled-state transition diagnostics.
            bool enabled = cam.isActiveAndEnabled;
            if (enabled != c.WasEnabled)
            {
                c.WasEnabled = enabled;
                Rect r = cam.rect;
                VRLog.Info("WorldUI", $"FlatScreen stack member '{cam.name}' {(enabled ? "ENABLED" : "DISABLED")} " +
                                      $"(depth {cam.depth:F1}, clear {cam.clearFlags}, " +
                                      $"rect ({r.x:F2},{r.y:F2},{r.width:F2},{r.height:F2})) — " +
                                      $"{(enabled ? "now renders into" : "no longer renders into")} the RT.");
            }

            if (c == _base)
            {
                // Re-assert the base clear every tick (game code may rewrite it).
                if (cam.clearFlags != CameraClearFlags.SolidColor)
                    cam.clearFlags = CameraClearFlags.SolidColor;
                if (cam.backgroundColor != OpaqueBlack)
                    cam.backgroundColor = OpaqueBlack;
                continue;
            }

            if (c == _uiBase)
            {
                // UI-stack base clears TRANSPARENT — the glass composites over the
                // background layer, so undrawn pixels must stay see-through.
                if (cam.clearFlags != CameraClearFlags.SolidColor)
                    cam.clearFlags = CameraClearFlags.SolidColor;
                if (cam.backgroundColor != TransparentBlack)
                    cam.backgroundColor = TransparentBlack;
                continue;
            }

            if (c.IsUi && _splitRouting)
                continue; // non-base UI cameras keep their own flags (overlay demotion is a BACKGROUND policy)

            bool fullscreen = cam.rect.width >= 0.99f && cam.rect.height >= 0.99f;
            bool wantDemote = demote && fullscreen && c.OriginalClearFlags == CameraClearFlags.SolidColor;
            if (wantDemote)
            {
                if (!c.Demoted)
                {
                    c.Demoted = true;
                    VRLog.Info("WorldUI", $"FlatScreen stack: non-base '{cam.name}' (depth {cam.depth:F1}) " +
                                          "fullscreen SolidColor clear DEMOTED to Depth — its clear is a video " +
                                          "backdrop (VideoCamera.cs:96-97) and must not wipe the RT composite.");
                }
                if (cam.clearFlags == CameraClearFlags.SolidColor)
                    cam.clearFlags = CameraClearFlags.Depth;
            }
            else if (c.Demoted)
            {
                c.Demoted = false;
                if (cam.clearFlags == CameraClearFlags.Depth)
                    cam.clearFlags = c.OriginalClearFlags;
                cam.backgroundColor = c.OriginalBackground;
                VRLog.Info("WorldUI", $"FlatScreen stack: '{cam.name}' clear demotion lifted " +
                                      $"(restored {c.OriginalClearFlags}).");
            }
        }
    }

    /// <summary>Undo everything <see cref="CaptureStack"/> did and drop all references.</summary>
    private void ReleaseStack()
    {
        // Mirrors shadow captured cameras — they die with the stack and are rebuilt
        // by the next capture sweep (the stereo RT/hook lifecycle stays with Hide).
        _stereo.ReleaseMirrors();
        for (int i = 0; i < _captured.Count; i++)
        {
            Camera cam = _captured[i].Camera;
            if (cam == null)
                continue;
            if (cam.targetTexture == _rt || (_uiRt != null && cam.targetTexture == _uiRt))
                cam.targetTexture = null;
            // Only the stack bases (forced clears) and demoted overlays (SolidColor
            // → Depth) had their flags touched — leave everyone else alone (their
            // flags may have been legitimately changed by game code meanwhile).
            if (_captured[i] == _base || _captured[i] == _uiBase || _captured[i].Demoted)
            {
                cam.clearFlags = _captured[i].OriginalClearFlags;
                cam.backgroundColor = _captured[i].OriginalBackground;
            }
        }
        if (_captured.Count > 0)
            VRLog.Info("WorldUI", $"FlatScreen stack released — {_captured.Count} camera(s) restored to the backbuffer.");
        _captured.Clear();
        CapturedSet.Clear();
        _base = null;
        _uiBase = null;
        // The no-UI watchdog's evidence dies with the stack (scene change / hide):
        // the NEXT scene may well have a UI camera — give the split a fresh chance
        // (test #20: the latch carried from the UI-less loading scene into the main
        // menu and kept split/stereo/video-depth off for the whole session).
        if (_splitNoUi)
        {
            _splitNoUi = false;
            VRLog.Info("WorldUI", "Screen layer split no-UI fallback re-armed — captured stack " +
                                  "released (scene change / hide); the split re-engages on the next sweep.");
        }
    }

    // ---- ITEM 9: desktop (flat monitor) = clean LEFT-EYE mirror ---------------------------

    /// <summary>
    /// ITEM 9 — force the flat monitor to mirror ONLY the HMD's LEFT eye. Unity/OpenXR's
    /// default game-view mirror mode is uncontrolled (which eye — or both, side by side —
    /// is build/driver dependent), so pin
    /// <see cref="UnityEngine.XR.XRSettings.gameViewRenderMode"/> to
    /// <c>GameViewRenderMode.LeftEye</c>. This is a real RUNTIME property (not an
    /// editor-only field) — valid on a built OpenXR player — and controls the desktop
    /// mirror-view blit Unity performs after every frame. It is re-asserted every tick
    /// because a scene load or game code can rewrite it, and the original is captured
    /// once so VR-off / hot-reload / the toggle restores it (<see cref="RestoreDesktopMirrorMode"/>).
    /// Combined with the <see cref="OnEndOfFrame"/> composite gate, the desktop shows the
    /// rig head camera's LEFT eye and nothing else — the left-eye mirror already carries
    /// the in-VR flat-screen quad (which shows the 2D menu), so no separate 2D composite
    /// is needed and the monitor never goes black.
    /// </summary>
    private void TickDesktopMirrorMode()
    {
        if (!DesktopMirrorLeftEye || !VRSession.IsRunning)
        {
            RestoreDesktopMirrorMode();
            return;
        }
        if (!_mirrorModeCaptured)
        {
            _originalMirrorMode = UnityEngine.XR.XRSettings.gameViewRenderMode;
            _mirrorModeCaptured = true;
        }
        if (UnityEngine.XR.XRSettings.gameViewRenderMode != UnityEngine.XR.GameViewRenderMode.LeftEye)
            UnityEngine.XR.XRSettings.gameViewRenderMode = UnityEngine.XR.GameViewRenderMode.LeftEye;
        _mirrorModeApplied = true;
        if (!_mirrorModeLogged)
        {
            _mirrorModeLogged = true;
            // Read the value BACK: on some OpenXR runtimes gameViewRenderMode is a no-op
            // (the compositor owns the mirror), so this line tells the next hardware log
            // whether the managed set is honored — and, if not, that the desktop-camera
            // scrub below is the only lever that keeps the monitor clean.
            var readback = UnityEngine.XR.XRSettings.gameViewRenderMode;
            bool honored = readback == UnityEngine.XR.GameViewRenderMode.LeftEye;
            VRLog.Info("WorldUI", "ITEM9 desktop mirror mode = LEFT EYE: XRSettings.gameViewRenderMode " +
                                  $"set to LeftEye (was {_originalMirrorMode}); readback={readback} " +
                                  (honored
                                      ? "(runtime HONORED — the mirror carries the left eye only). "
                                      : "(runtime IGNORED — the OpenXR compositor's mirror is not controllable " +
                                        "from managed code here; the desktop-camera scrub keeps the monitor clean instead). ") +
                                  "The end-of-frame 2D-menu composite blit is skipped.");
        }
    }

    /// <summary>Restore the original game-view mirror mode (VR off / toggle off / hot reload).</summary>
    private void RestoreDesktopMirrorMode()
    {
        if (!_mirrorModeApplied)
            return;
        if (_mirrorModeCaptured)
            UnityEngine.XR.XRSettings.gameViewRenderMode = _originalMirrorMode;
        _mirrorModeApplied = false;
        _mirrorModeLogged = false;
        VRLog.Info("WorldUI", $"ITEM9 desktop mirror mode restored to {_originalMirrorMode} (VR off / toggle off / hot reload).");
    }

    /// <summary>
    /// ITEM 9 (SCENARIO STATE) — keep the game's OWN cameras off the desktop backbuffer
    /// while the flat screen is HIDDEN.
    ///
    /// WHY THE DESKTOP STILL SHOWED EXTRA UI: <see cref="Core.VRCameraPolicy"/> forces
    /// every game camera to <c>StereoTargetEyeMask.None</c> while VR runs (only the rig
    /// head camera renders the HMD). A None camera renders to the DESKTOP backbuffer.
    /// In Menu2D <see cref="CaptureStack"/> redirects all of them into the flat-screen RT,
    /// so nothing but the XR left-eye mirror reaches the monitor — but in a SCENARIO the
    /// flat screen is hidden, CaptureStack does not run, and the game's flat 2D UI (hand,
    /// bars, buttons) and 3D board composite straight onto the monitor IN PARALLEL with
    /// the mirror. That is the "extra UI on the desktop" this item reports; it is NOT the
    /// XR runtime's mirror (which only ever carries the HMD eye) — it is the game's own
    /// desktop cameras drawing next to it.
    ///
    /// FIX (mirrors CaptureStack): retarget EVERY non-head backbuffer camera onto one
    /// throwaway offscreen sink RT (Screen-sized, so Camera pixel dimensions and
    /// screen-space raycasts are unchanged) so ONLY the XR mirror composites onto the
    /// monitor. The rig <see cref="Rig.VRRigDriver.HeadCamera"/> (the sole stereo renderer
    /// → HMD) is excluded, so the in-VR view is untouched; the game UI still RENDERS (into
    /// the sink), so the WorldUI canvas conversion that consumes it in VR is unaffected —
    /// only its DESKTOP output is suppressed. Fully reversible (targetTexture → null) on
    /// VR stop / toggle off / hot reload / when the flat screen shows (CaptureStack owns
    /// the cameras then). Gated by [WorldUI] DesktopMirrorLeftEye (off = legacy).
    ///
    /// Instrumented: the full backbuffer inventory is logged once per scene so the next
    /// hardware log names exactly which cameras reached the desktop and what was scrubbed.
    /// </summary>
    private void TickDesktopCameraScrub()
    {
        // Only while the flat screen is HIDDEN: Menu2D already routes every backbuffer
        // camera into its own RT via CaptureStack (double-owning them would fight).
        bool want = DesktopMirrorLeftEye && VRSession.IsRunning && !_visible;
        if (!want)
        {
            ReleaseDesktopScrub(_visible ? "flat screen captures the cameras" : "toggle off / VR stopped");
            return;
        }

        if (_scrubRt == null)
        {
            _scrubRt = new RenderTexture(Mathf.Max(Screen.width, 1280), Mathf.Max(Screen.height, 720), 24)
            {
                name = "GloomhavenVR.DesktopScrubSink",
                antiAliasing = 1,
            };
            _scrubRt.Create();
        }

        Camera? head = Rig.VRRigDriver.HeadCamera;
        int count = Core.VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);

        string scene = SceneManager.GetActiveScene().name;
        bool logInventory = _scrubInventoryScene != scene;
        if (logInventory)
        {
            _scrubInventoryScene = scene;
            VRLog.Info("WorldUI", $"ITEM9 desktop backbuffer inventory (scene '{scene}', flat screen HIDDEN, " +
                                  $"{count} active camera(s)) — each targetTexture==null camera below composites " +
                                  "onto the monitor; all except the VR head are retargeted to an offscreen sink:");
        }

        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null)
                continue;
            bool isHead = head != null && cam == head;

            if (logInventory)
            {
                Rect r = cam.rect;
                string dest = cam.targetTexture != null ? cam.targetTexture.name : "backbuffer";
                VRLog.Info("WorldUI", $"  '{cam.name}' tag={cam.tag} enabled={cam.enabled} depth={cam.depth:F1} " +
                                      $"clear={cam.clearFlags} rect=({r.x:F2},{r.y:F2},{r.width:F2},{r.height:F2}) " +
                                      $"mask=0x{cam.cullingMask:X8} stereo={cam.stereoTargetEye} target={dest}" +
                                      (isHead ? " [VR head — KEPT: its XR mirror IS the desktop]"
                                       : cam.targetTexture != null ? " [own RT — left alone]"
                                       : " [SCRUBBED → offscreen sink]"));
            }

            if (isHead || FlatScreen.IsCaptured(cam))
                continue; // head renders the HMD; captured cameras belong to CaptureStack
            if (cam.targetTexture == _scrubRt)
                continue; // already scrubbed
            if (cam.targetTexture != null)
                continue; // renders to its own RT (e.g. 'GUI 3D Camera') — not the desktop

            cam.targetTexture = _scrubRt;
            if (!_scrubbed.Contains(cam))
                _scrubbed.Add(cam);
            if (!logInventory)
                VRLog.Info("WorldUI", $"ITEM9 desktop scrub: '{cam.name}' (depth {cam.depth:F1}, " +
                                      $"mask 0x{cam.cullingMask:X8}, clear {cam.clearFlags}) retargeted off the " +
                                      "backbuffer to the offscreen sink (kept off the monitor; still renders for " +
                                      "the in-VR canvas conversion).");
        }

        // Re-assert (game code may reset targetTexture to null) + compact dead / released.
        for (int i = _scrubbed.Count - 1; i >= 0; i--)
        {
            Camera cam = _scrubbed[i];
            if (cam == null)
            {
                _scrubbed.RemoveAt(i);
                continue;
            }
            if ((head != null && cam == head) || FlatScreen.IsCaptured(cam))
            {
                // Ownership moved (rig rebuilt onto it / flat screen captured it) — drop our claim.
                if (cam.targetTexture == _scrubRt)
                    cam.targetTexture = null;
                _scrubbed.RemoveAt(i);
                continue;
            }
            if (cam.targetTexture == null)
                cam.targetTexture = _scrubRt;          // re-assert our redirect
            else if (cam.targetTexture != _scrubRt)
                _scrubbed.RemoveAt(i);                  // game gave it its own RT — no longer our concern
        }

        _scrubActive = true;
    }

    /// <summary>
    /// Restore every scrubbed camera to the backbuffer and drop the sink RT
    /// (VR off / toggle off / hot reload / the flat screen taking the cameras over).
    /// </summary>
    private void ReleaseDesktopScrub(string reason)
    {
        if (!_scrubActive && _scrubbed.Count == 0 && _scrubRt == null)
            return;
        int restored = 0;
        for (int i = 0; i < _scrubbed.Count; i++)
        {
            Camera cam = _scrubbed[i];
            if (cam == null)
                continue;
            if (cam.targetTexture == _scrubRt)
            {
                cam.targetTexture = null;
                restored++;
            }
        }
        _scrubbed.Clear();
        if (_scrubRt != null)
        {
            _scrubRt.Release();
            Object.Destroy(_scrubRt);
            _scrubRt = null;
        }
        if (_scrubActive)
            VRLog.Info("WorldUI", $"ITEM9 desktop scrub released ({reason}) — {restored} camera(s) restored to the backbuffer.");
        _scrubActive = false;
        _scrubInventoryScene = null;
    }

    /// <summary>
    /// End-of-frame hook (WorldUI driver coroutine, after Unity's XR mirror blit).
    ///
    /// ITEM 9: with <see cref="DesktopMirrorLeftEye"/> on, the desktop is a clean
    /// LEFT-EYE mirror (<see cref="TickDesktopMirrorMode"/>) — the XR mirror of the rig
    /// head camera's left eye already fills the monitor and shows the in-VR flat-screen
    /// quad (the 2D menu). We therefore do NOT overwrite the backbuffer with the 2D-menu
    /// composite here; blitting the RT would replace the eye image with the flat menu.
    ///
    /// Legacy path (toggle off): while the UICamera is redirected into our RT, copy the
    /// RT to the desktop backbuffer so the monitor never goes black and stays
    /// mouse-operable (the old "menu blackscreen" fallback + UI composite overlay).
    /// </summary>
    public void OnEndOfFrame()
    {
        if (!_visible || _rt == null || !_rt.IsCreated())
            return;

        // ITEM 9: left-eye mirror is the desktop — leave the eye image untouched.
        if (DesktopMirrorLeftEye)
        {
            if (!_mirrorLogged)
            {
                _mirrorLogged = true;
                VRLog.Info("WorldUI", "ITEM9 desktop mirror: end-of-frame 2D composite blit SKIPPED — " +
                                      "the monitor shows the HMD LEFT-eye mirror only (no menu composite / overlay).");
            }
            return;
        }

        if (!_mirrorLogged)
        {
            _mirrorLogged = true;
            VRLog.Info("WorldUI", $"Desktop mirror active — FlatScreen RT ({_rt.width}x{_rt.height}) " +
                                  "blits to the backbuffer at end of frame" +
                                  (SplitActive ? " (glass RT alpha-composited on top while routing)." : "."));
        }
        Graphics.Blit(_rt, (RenderTexture?)null);
        // Split (class doc D): the UI lives on its own RT now — alpha-composite it
        // over the background so the monitor still shows the complete menu.
        if (_splitRouting && _uiRt != null && _uiRt.IsCreated() && _glassMaterial != null)
            Graphics.Blit(_uiRt, (RenderTexture?)null, _glassMaterial);
    }

    public void Shutdown()
    {
        Core.Events.VREvents.SceneLoaded -= OnSceneLoaded;
        ManualScreenActive = false;
        Hide();
        DestroyIndicator();
        RestoreDesktopMirrorMode();      // ITEM 9: reversible on VR stop / hot reload
        ReleaseDesktopScrub("shutdown"); // ITEM 9 (scenario): restore scrubbed cameras to the backbuffer
    }

    // ---- policy ------------------------------------------------------------------------

    /// <summary>
    /// True while the game is still in its boot flow: scene 0 (Bootstrap) or the
    /// "Intro" scene (video + logos). Verified against decompiled GH.Runtime
    /// Bootstrap.ShowSplash: Intro and the menu scene both load Single, so nothing
    /// non-persistent survives into the menu. The FlatScreen must not touch the
    /// UICamera's targetTexture here — the intro renders vanilla on the desktop.
    /// </summary>
    private static bool IsPreMenuScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.buildIndex == 0 || scene.name == IntroSceneName;
    }

    private bool WantVisible()
    {
        if (!WorldUIConfig.FlatScreen.Value || !WorldUIConfig.ConversionActive)
            return false;

        VRMode mode = VRModeStateMachine.CurrentMode;
        if (mode == VRMode.Menu2D)
            return WorldUIConfig.FlatScreenAutoShow.Value;

        // P6 self-rescue: the manual chord forces the screen in ANY scenario mode.
        if (_manualShow)
            return true;

        // Catch-all fallback for unconverted windows that expect interaction during
        // a scenario (events, tutorials, take-damage, ESC menu, rewards, ...): the
        // ModalFallback tracker asserted ModalUI and wants the full 2D composite
        // ([WorldUI] ModalStyle = screen, or a window that failed to convert).
        // P8: while ModalFallback floats the window itself as a world-space panel
        // (WindowModalActive), the modal is owned by that panel — no screen.
        // Otherwise, in a plain UI-lock modal, the world-space confirmation surface
        // owns simple dialogs; everything else falls back 2D too.
        if (mode == VRMode.ModalUI)
        {
            // A genuinely floated modal always shows (its own window/composite is wanted).
            if (ModalFallback.ScreenWanted)
                return true;
            // Item 5b (test #23): burning a card UI-locks the game (→ ModalUI), but the burn
            // plays on the WORLD card (BurnCardFx) and the burn-confirm is a DialogPopup, not
            // a UIConfirmationBox — so IsConfirmationBoxOpen() is false and the catch-all below
            // would raise the full desktop-mirror quad over an EMPTY UI-lock composite (the
            // ~1 s flat-screen flash). HandSuppression.BurnActive is ref-counted across the
            // whole burn/lost/discard window; suppress ONLY the empty catch-all while it holds.
            // A truly floated modal (ScreenWanted above / WindowModalActive below) is untouched.
            if (Cards.Patches.HandSuppression.BurnActive)
                return false;
            return !ModalFallback.WindowModalActive
                   && (!WorldUIConfig.Dialogs.Value || !IsConfirmationBoxOpen());
        }

        return false;
    }

    /// <summary>
    /// Manual screen chord (P6): fires AT the hold threshold while the non-dominant
    /// A/X is still held (the settings-panel chord fires on release BELOW it — the
    /// shared <see cref="NonDominantHold"/> tracker arbitrates via Consumed). Active
    /// only while an actual scenario board exists: pre-scenario Menu2D auto-shows
    /// the screen anyway, and the latch resets on scenario exit.
    /// </summary>
    private void TickManualChord()
    {
        if (!Core.Events.VRModeStateMachine.ScenarioBoardExists)
        {
            _manualShow = false;
            _chordFired = false;
            return;
        }
        if (!WorldUIConfig.ManualScreenChord.Value)
            return;

        if (NonDominantHold.HeldSeconds <= 0f)
        {
            _chordFired = false;
            _chordArmingLogged = false;
            return;
        }
        // Test #17: while a floating modal is open the SAME hold is the modal escape
        // chord (ModalFallback.TickEscapeChord, runs earlier in the driver order and
        // consumes the press) — one press, one action; the screen toggle needs a
        // fresh press once no modal floats.
        if (NonDominantHold.Consumed)
            return;
        float threshold = Mathf.Max(0.5f, WorldUIConfig.ManualScreenChordSeconds.Value);
        // Arming diagnostic (test #10): proves in the log that the hardware press
        // reaches the chord tracker even when the player releases before the threshold.
        if (!_chordArmingLogged && !_chordFired && NonDominantHold.HeldSeconds >= threshold * 0.5f)
        {
            _chordArmingLogged = true;
            VRLog.Info("WorldUI", $"Manual screen chord ARMING: non-dominant A/X held " +
                                  $"{NonDominantHold.HeldSeconds:F1}s — keep holding to " +
                                  $"{threshold:F1}s to toggle the 2D screen.");
        }
        if (_chordFired || NonDominantHold.HeldSeconds < threshold)
            return;

        _chordFired = true;
        NonDominantHold.Consumed = true; // the release must not also toggle the settings panel
        _manualShow = !_manualShow;
        NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
        VRLog.Info("WorldUI", $"MANUAL SCREEN CHORD: flat screen toggled {(_manualShow ? "ON" : "OFF")} " +
                              $"(non-dominant A/X held {threshold:F1}s in scenario).");
    }

    private static bool IsConfirmationBoxOpen() =>
        Singleton<UIConfirmationBoxManager>.IsInitialized
        && Singleton<UIConfirmationBoxManager>.Instance.IsOpen;

    // ---- pre-menu indicator -----------------------------------------------------------------

    /// <summary>
    /// While the intro plays flat (pre-menu gate) the HMD would show only the menu
    /// rig's empty void — float a small "starting…" label in front of the head so the
    /// player knows the mod is alive and the menu is coming.
    /// </summary>
    private void TickStartingIndicator(bool preMenu)
    {
        bool want = preMenu && VRSession.IsRunning
                    && WorldUIConfig.FlatScreen.Value && WorldUIConfig.Master.Value;
        if (!want)
        {
            if (_indicator != null)
            {
                DestroyIndicator();
                VRLog.Info("WorldUI", "Starting indicator removed (menu scene reached).");
            }
            return;
        }

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        if (_indicator == null)
        {
            _indicator = new GameObject("GloomhavenVR.StartingIndicator");
            Object.DontDestroyOnLoad(_indicator);
            var text = _indicator.AddComponent<TextMeshPro>();
            // Brand stays literal; the status line follows the game language (mod string).
            text.text = "GloomhavenVR\n<size=60%>" + Loc.Mod("starting_desktop") + "</size>";
            text.fontSize = 1f; // 3D TMP: ~0.1 m line height, comfortable at 1.5 m
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.75f, 0.75f, 0.78f, 1f);
            var rect = (RectTransform)_indicator.transform;
            rect.sizeDelta = new Vector2(3f, 1f);
            VRLayers.Apply(_indicator); // head camera masks include the mod layer (CAMERA-POLICY §2)
            VRLog.Info("WorldUI", "Starting indicator shown (pre-menu scene, FlatScreen gated).");
        }

        Transform h = head.transform;
        Vector3 fwd = h.forward;
        _indicator.transform.SetPositionAndRotation(
            h.position + fwd * 1.5f,
            Quaternion.LookRotation(fwd, Vector3.up)); // TMP front faces -Z → toward the head
    }

    private void DestroyIndicator()
    {
        if (_indicator != null)
        {
            Object.Destroy(_indicator);
            _indicator = null;
        }
    }

    // ---- lifecycle ----------------------------------------------------------------------

    private void Show()
    {
        if (_rt == null)
        {
            // Plain colour RT (RenderTextureReadWrite.Default). The campaign map's darkness is NOT a
            // colorspace issue (the rig renders in Gamma, so sRGB read/write is a no-op) — the map is
            // rendered bright by the mod's own forward albedo camera (FlatScreenStereo MAP ALBEDO RENDER).
            _rt = FlatScreenStereo.CreateColorRt(
                Mathf.Max(Screen.width, 1280), Mathf.Max(Screen.height, 720), 24, "GloomhavenVR.FlatScreenRT");
            _rt.Create();
            VRLog.Info("WorldUI", $"FlatScreen base RT created: {FlatScreenStereo.DescribeRt(_rt)}.");
        }

        if (_quad == null)
        {
            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "GloomhavenVR.FlatScreen";
            // Menu scenes load Single — the quad must survive them (root cause of the
            // black HMD menu: quad died with the scene, UICamera stayed on the RT).
            Object.DontDestroyOnLoad(_quad);
            Object.Destroy(_quad.GetComponent<Collider>());
            _quadRenderer = _quad.GetComponent<Renderer>();
            // Shader choice (P3b, hardware test #4): the quad must render its texture
            // ALPHA-IGNORING — the RT's alpha channel is whatever the UI left behind.
            //
            // 1. "Hidden/BlitCopy": Unity's always-included internal blit shader —
            //    plain opaque copy of _MainTex (Blend Off, ignores alpha). Verified
            //    shipped: the GAME itself calls Shader.Find("Hidden/BlitCopy")
            //    (decompiled GH.Runtime.FirstPass, RenderHeads.Media.AVProMovieCapture/
            //    CaptureFromCamera360.cs:446), and Graphics.Blit depends on it, so it
            //    cannot be stripped. Its pass states ZTest Always / ZWrite Off — fine
            //    for a menu screen that must never be occluded.
            // 2. Fallback "Sprites/Default" (verified shipped: decompiled ThirdParty
            //    GraphProgress/VertexView calls Shader.Find on it): alpha-blended, but
            //    with the RT now cleared to OPAQUE black (RetargetUiCamera) dst alpha
            //    ≈ 1, so it renders near-opaque instead of invisible.
            // (An opaque keyword/MaterialPropertyBlock variant of Sprites/Default does
            // not exist — the shader has no such keyword — hence the BlitCopy pick.)
            Shader? shader = Shader.Find("Hidden/BlitCopy")
                             ?? Shader.Find("Sprites/Default")
                             ?? Shader.Find("UI/Default");
            _screenMaterial = new Material(shader) { mainTexture = _rt };
            // ITEM 1: remember the shader's own queue so the menu/scenario depth policy
            // (TickBackdropDepth) can restore it outside menu modes.
            _screenMaterialQueueDefault = _screenMaterial.renderQueue;
            _quadRenderer.sharedMaterial = _screenMaterial;

            // No FlatScreen-owned reticle: the RayInteractor's beam + dot clamp to
            // the screen hit via UiHitOverride (test #7 — one convergent visual).

            // The screen lives on the dedicated mod layer — only the rig head camera
            // renders it (its mask ORs the mod bit, never 0; CAMERA-POLICY §2).
            VRLayers.Apply(_quad);
        }

        _quad.SetActive(true);
        CaptureStack();

        // P5 (MISSION A.5): the ModalUI-constrained laser may point at the screen.
        RayInteractor.RegisterUiTarget(_quad.transform);

        PlaceScreen(instant: true);
        _visible = true;
        VRLog.Info("WorldUI", "FlatScreen shown (backbuffer camera stack → RenderTexture; " +
                              "desktop mirror engages at end of frame).");
    }

    private void Hide()
    {
        if (_pressing)
        {
            VirtualMouse.Release();
            _pressing = false;
            _latched = false;
            EndScreenDrag();
        }
        if (_pokePressing)
            EndPoke("screen hidden");
        ReleaseStack();
        _stereo.Deactivate("screen hidden");
        TeardownSplit("screen hidden");
        _splitFailed = false; // a failed split gets a fresh chance on the next Show
        _stereoGateReason = null;

        if (_quad != null)
        {
            RayInteractor.UnregisterUiTarget(_quad.transform);
            _quad.SetActive(false);
        }
        if (_rt != null)
        {
            _rt.Release();
            Object.Destroy(_rt);
            _rt = null;
            if (_quad != null)
            {
                Object.Destroy(_quad);
                _quad = null;
                _quadRenderer = null;
            }
            if (_screenMaterial != null)
            {
                Object.Destroy(_screenMaterial);
                _screenMaterial = null;
            }
        }
        if (_visible)
            VRLog.Info("WorldUI", "FlatScreen hidden — captured cameras restored to the backbuffer.");
        _visible = false;
        _mirrorLogged = false;
        _handsDiagLogged = false;
        _placedHead = null;
        _offGazeSince = -1f;
        _gliding = false;
    }

    // ---- placement -----------------------------------------------------------------------

    private void PlaceScreen(bool instant)
    {
        if (_quad == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f)
            fwd = Vector3.forward;
        fwd.Normalize();

        float distance = Mathf.Max(0.1f, WorldUIConfig.ScreenDistance.Value);
        Vector3 target = h.position + fwd * (distance * scale);
        // Quad primitive faces -Z (visible from -forward side): +Z away from viewer.
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        float width = WantedQuadWidth(scale);
        Vector3 size = new(width, width / ScreenAspect, 1f);

        Transform t = _quad.transform;
        if (instant)
        {
            t.SetPositionAndRotation(target, rot);
        }
        else
        {
            t.position = Vector3.Lerp(t.position, target, Time.deltaTime * 3f);
            t.rotation = Quaternion.Slerp(t.rotation, rot, Time.deltaTime * 3f);
        }
        t.localScale = size;

        PlaceBackQuad(scale, distance);

        if (instant)
        {
            _placedHead = head;
            Transform? rig = Rig.VRRigDriver.RigRoot;
            _placedRigPos = rig != null ? rig.position : Vector3.zero;
            LogPlacement(head);
        }
    }

    /// <summary>
    /// Background quad (split): derived from the screen quad's CURRENT pose — so it
    /// stays coherent during the lazy-follow glide — the layer gap behind it along
    /// +forward (away from the viewer), scaled up so it subtends the same angle from
    /// the head (no visible edge inset where the glass ends).
    /// </summary>
    private void PlaceBackQuad(float scale, float distance)
    {
        if (_backQuad == null || _quad == null)
            return;
        Transform t = _quad.transform;
        float grow = 1f + BackplaneGapMeters / Mathf.Max(0.1f, distance);
        Transform b = _backQuad.transform;
        b.SetPositionAndRotation(t.position + t.forward * (BackplaneGapMeters * scale), t.rotation);
        Vector3 size = t.localScale;
        b.localScale = new Vector3(size.x * grow, size.y * grow, 1f);
    }

    /// <summary>
    /// One diagnostic line per (re)placement: quad pose vs head pose, culling mask,
    /// layer and shader — makes "quad exists but camera can't see it" visible in the
    /// BepInEx log without an HMD report.
    /// </summary>
    private void LogPlacement(Camera head)
    {
        if (_quad == null || _quadRenderer == null)
            return;
        Shader? shader = _quadRenderer.sharedMaterial != null ? _quadRenderer.sharedMaterial.shader : null;
        VRLog.Info("WorldUI",
            $"FlatScreen quad placed: pos={_quad.transform.position}, size={_quad.transform.localScale}, " +
            $"layer={_quad.layer}, shader='{(shader != null ? shader.name : "NULL")}', " +
            $"RT={( _rt != null ? $"{_rt.width}x{_rt.height}" : "NULL")}, " +
            $"split={(SplitActive ? (_splitRouting ? "routing" : "engaged") : "off")} | " +
            $"head '{head.name}' pos={head.transform.position}, fwd={head.transform.forward}, " +
            $"mask=0x{head.cullingMask:X8}, clear={head.clearFlags}, stereo={head.stereoTargetEye}.");
    }

    /// <summary>
    /// Quad width in world units for the configured screen size. Single source of
    /// truth for <see cref="PlaceScreen"/> AND <see cref="FollowHead"/>'s re-place
    /// trigger — a divergence between the two would re-place the screen every frame.
    /// </summary>
    private static float WantedQuadWidth(float scale) =>
        WorldUIConfig.ScreenWidth.Value * scale;

    private void FollowHead()
    {
        if (_quad == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        // Rig rebuild / recenter / head-camera swap: snap the screen back in front of
        // the (new) vantage instead of lazily drifting after it. Placement always uses
        // the TRACKED head's actual world forward (PlaceScreen) — never an assumed
        // axis: hardware test #4 placed the quad "forward" of a vantage the player
        // wasn't facing.
        Transform? rig = Rig.VRRigDriver.RigRoot;
        Vector3 rigPos = rig != null ? rig.position : Vector3.zero;
        // Live-tunable size/distance ([WorldUI] ScreenWidth/ScreenDistance):
        // re-place when the effective width no longer matches the quad (cheap
        // float compare).
        float wantedWidth = WantedQuadWidth(PanelLayout.WorldScale);
        if (head != _placedHead || (rigPos - _placedRigPos).sqrMagnitude > 1e-4f
            || Mathf.Abs(_quad.transform.localScale.x - wantedWidth) > 0.001f)
        {
            PlaceScreen(instant: true);
            _offGazeSince = -1f;
            _gliding = false;
            return;
        }

        // Lazy follow (P3a): after the gaze has been >45° off the screen for >1 s,
        // glide it back in front of the current gaze until it settles (<5°).
        Vector3 toScreen = _quad.transform.position - head.transform.position;
        float angle = Vector3.Angle(head.transform.forward, toScreen);
        if (angle > FollowAngleDegrees)
        {
            if (_offGazeSince < 0f)
                _offGazeSince = Time.unscaledTime;
            if (!_gliding && Time.unscaledTime - _offGazeSince >= FollowDwellSeconds)
            {
                _gliding = true;
                VRLog.Info("WorldUI", $"FlatScreen lazy follow: gaze {angle:F0}° off for " +
                                      $">{FollowDwellSeconds:F0}s — gliding back in front (head fwd {head.transform.forward}).");
            }
        }
        else
        {
            _offGazeSince = -1f;
        }

        if (_gliding)
        {
            PlaceScreen(instant: false);
            if (angle < FollowSettledDegrees)
            {
                _gliding = false;
                _offGazeSince = -1f;
            }
        }
    }

    // ---- pointer ---------------------------------------------------------------------------

    private void TickPointer()
    {
        if (_quad == null || _rt == null)
            return;

        // Handedness switch first: it may change which hand is "primary" below.
        if (TickHandednessSwitch())
        {
            HideReticle();
            return; // masks re-apply this frame; pointer resumes next frame
        }

        TickPoke();

        VRHand? hand = VRHands.Primary;
        IPickProvider? pick = VRHands.PrimaryPick;
        if (pick == null || hand == null || !pick.TryGetPick(out PickPose pose))
        {
            HideReticle();
            return;
        }

        if (_pokePressing)
        {
            // The fingertip owns the virtual mouse; the ray resumes after withdraw.
            HideReticle();
            return;
        }

        // Ray ∩ screen plane (quad faces -Z; plane normal = -forward toward viewer).
        Transform t = _quad.transform;
        Vector3 normal = -t.forward;
        float denom = Vector3.Dot(pose.Direction, normal);
        if (Mathf.Abs(denom) < 1e-4f)
        {
            HideReticle();
            return;
        }
        float dist = Vector3.Dot(t.position - pose.Origin, normal) / denom;
        if (dist < 0f)
        {
            HideReticle();
            return;
        }

        Vector3 hit = pose.Origin + pose.Direction * dist;
        Vector3 local = t.InverseTransformPoint(hit); // quad local: x/y in [-0.5, 0.5]
        bool onQuad = Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f;
        if (!onQuad && !_pressing)
        {
            HideReticle();
            return;
        }
        // While pressed, edge tremor must not cancel the press — clamp instead of drop.
        local.x = Mathf.Clamp(local.x, -0.5f, 0.5f);
        local.y = Mathf.Clamp(local.y, -0.5f, 0.5f);

        // UV → virtual mouse pixels.
        var pixel = new Vector2((local.x + 0.5f) * _rt.width, (local.y + 0.5f) * _rt.height);

        // On the campaign map, a moving held trigger PANS the map (grab-drag) instead of
        // dragging a uGUI widget — see the map-pan block below. Suppress the generic
        // latch→uGUI-drag here so the two gestures never fight.
        bool mapActive = _stereo.MapActive;

        // Click latch (requirement 1, class doc): while pressed and latched the warp
        // position stays frozen at the press pixel; deliberate sustained ray movement
        // opens the latch into a real drag.
        if (_pressing && _latched && !mapActive)
        {
            float angle = Vector3.Angle(_pressDirection, pose.Direction);
            if (angle > WorldUIConfig.DragUnlockDegrees.Value)
            {
                if (_dragOverSince < 0f)
                {
                    _dragOverSince = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _dragOverSince >= WorldUIConfig.DragUnlockSeconds.Value)
                {
                    _latched = false;
                    // Deliberate drag. virtualmouse/both modes press-and-follow the
                    // virtual-mouse device; execute mode (the default) drives uGUI drag
                    // events directly. The VM path never moved a menu slider on hardware:
                    // the virtual-mouse BUTTON edges do not survive the input module
                    // (test #7 — the very reason clicks default to ExecuteEvents), so a
                    // held VM "drag" carried no pressed state, uGUI started no drag, and
                    // the slider handle never followed — only the DirectClick on release
                    // ever set a value (user report: sliders in the MAIN MENU can only be
                    // clicked, never dragged, so 0 is unreachable). The ExecuteEvents drag
                    // below moves Sliders/Scrollbars/ScrollRects the same way the in-game
                    // world-space menus do (Hands.Interact.UguiPointer).
                    if (WorldUIConfig.VirtualMouseButtons)
                    {
                        if (!_vmPressed)
                        {
                            VirtualMouse.WarpTo(_latchedPixel);
                            VirtualMouse.Press();
                            _vmPressed = true;
                        }
                    }
                    else
                    {
                        BeginScreenDrag(_latchedPixel);
                        UpdateScreenDrag(pixel); // catch the drag up to the current ray at once
                    }
                    VRLog.Info("WorldUI", $"FlatScreen pointer: click latch OPENED → drag " +
                                          $"(ray {angle:F1}° off the press direction for " +
                                          $">{WorldUIConfig.DragUnlockSeconds.Value:F2}s).");
                }
            }
            else
            {
                _dragOverSince = -1f;
            }
        }

        // MAP PAN (grab-drag): on the campaign map a held trigger that travels past a small
        // pixel threshold grabs the parchment and drags it — the map follows the laser. This
        // consumes the press latch so release does NOT select a location; a still trigger keeps
        // the latch closed and clicks through to the location, exactly as before.
        if (mapActive && _pressing)
        {
            if (!_mapPanGesture &&
                (pixel - _latchedPixel).sqrMagnitude > MapPanStartPixels * MapPanStartPixels)
            {
                _mapPanGesture = true;
                _latched = false;             // consume the click — release must not DirectClick
                if (_vmPressed)
                {
                    VirtualMouse.Release();    // drop any held virtual-mouse button so no hover/drag leaks
                    _vmPressed = false;
                }
                EndScreenDrag();
                _stereo.BeginMapPan(_latchedPixel);
                VRLog.Info("WorldUI", "FlatScreen pointer: map PAN started (trigger-drag grabbed the map).");
            }
            if (_mapPanGesture)
                _stereo.UpdateMapPan(pixel);
        }

        // While frozen, keep re-warping to the SAME latched pixel: identical uGUI
        // position (no drag delta), but the per-tick write keeps pointer currency
        // reclaimed and the queued-event stream alive during a held press. During a map
        // pan the virtual mouse is FROZEN at the press pixel so panning never drags the
        // cursor across (and accidentally hovers) location markers.
        bool frozen = _pressing && _latched;
        if (!_mapPanGesture)
            VirtualMouse.WarpTo(frozen ? _latchedPixel : pixel);

        // Execute-mode drag (default ClickMode): once the latch has opened, drive the
        // uGUI IDragHandler under the press so sliders/scrollbars/scroll-rects follow
        // the ray continuously — down/up/click alone never move a Slider handle.
        if (_screenDragActive)
            UpdateScreenDrag(pixel);

        // Single convergent visual (test #7): the beam is CLAMPED to this exact world
        // point and the RayInteractor's reticle shows there — no separate FlatScreen
        // dot, no beam passing through the screen, no beam/dot parallax. While
        // latched, the point is the frozen click position, so the beam visibly
        // sticks to where the click will land.
        Vector3 uiWorldPoint = frozen
            ? t.TransformPoint(new Vector3(_latchedLocal.x, _latchedLocal.y, 0f))
            : hit;
        hand.Ray.UiHitOverride = uiWorldPoint;

        // Trigger = left mouse button (press/release so drags work). The release
        // condition is the trigger STATE, not the TriggerUp edge: a hands rebuild
        // mid-press (HandsDriver re-creates VRHand instances on rig changes) would
        // swallow the edge forever and leave uGUI in drag state — hover would die
        // globally (I3 hardening; VirtualMouse has a second, time-based watchdog).
        if (hand.TriggerDown && !_pressing)
        {
            _pressing = true;
            _latched = WorldUIConfig.ClickLatch.Value;
            _latchedLocal = new Vector2(local.x, local.y);
            _latchedPixel = pixel;
            _pressDirection = pose.Direction;
            _dragOverSince = -1f;
            VirtualMouse.WarpTo(pixel); // press lands exactly on the frozen pixel
            if (WorldUIConfig.VirtualMouseButtons)
            {
                VirtualMouse.Press();
                _vmPressed = true;
            }
            LogUnderPointer(pixel); // diagnostic: what the click will actually hit
            VRLog.Info("WorldUI", $"FlatScreen pointer: trigger PRESS at RT pixel " +
                                  $"({pixel.x:F0},{pixel.y:F0}), latch={_latched}, " +
                                  $"mode={WorldUIConfig.ClickMode.Value}.");
        }
        else if (_pressing && !hand.TriggerPressed)
        {
            _pressing = false;
            if (!hand.TriggerUp)
                VRLog.Warn("WorldUI", "FlatScreen pointer: trigger release edge was missed " +
                                      "(hands rebuilt mid-press?) — forced release.");
            if (_vmPressed)
            {
                VirtualMouse.Release();
                _vmPressed = false;
            }
            // A map pan consumed the press (latch already false) — end it, no click fires.
            if (_mapPanGesture)
            {
                _stereo.EndMapPan();
                _mapPanGesture = false;
            }
            // End any execute-mode drag first (endDrag + pointerUp). A drag opened the
            // latch, so _latched is false here and DirectClick does not double-fire —
            // the two paths are mutually exclusive (tap → DirectClick; drag → EndScreenDrag).
            EndScreenDrag();
            if (_latched && WorldUIConfig.ExecuteClicks)
                DirectClick(_latchedPixel);
            VRLog.Info("WorldUI", $"FlatScreen pointer: trigger RELEASE at RT pixel " +
                                  $"({pixel.x:F0},{pixel.y:F0}) — " +
                                  $"{(_latched ? "CLICK (latched)" : "drag end")}.");
            _latched = false;
        }
    }

    /// <summary>
    /// Requirement 4: in Menu2D the NON-dominant trigger switches dominance to that
    /// hand — only the dominant hand has a beam and clicks (per-hand Menu2D policy,
    /// HandsModule). Writes <c>[Hands] PrimaryHand</c> (BepInEx persists on set;
    /// HandsDriver reapplies the interactor masks via SettingChanged), so the card
    /// fan / wrist HUD side stays consistent. Returns true when a switch happened —
    /// the caller skips this frame so the freshly dominant hand's TriggerDown edge
    /// cannot fire an immediate accidental click.
    /// </summary>
    private bool TickHandednessSwitch()
    {
        if (VRModeStateMachine.CurrentMode != VRMode.Menu2D)
            return false;

        VRHand? primary = VRHands.Primary;
        VRHand? other = primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        if (primary == null || other == null || !other.HasPose || !other.TriggerDown)
            return false;
        // Dev harness drives BOTH triggers from one key — a switch would flip-flop.
        if (other.IsSimulated)
            return false;

        if (_pressing)
        {
            _pressing = false;
            _latched = false;
            if (_mapPanGesture)
            {
                _stereo.EndMapPan();
                _mapPanGesture = false;
            }
            VirtualMouse.Release();
            EndScreenDrag();
        }

        string side = other.Side == HandSide.Left ? "Left" : "Right";
        Plugin.PrimaryHand.Value = side; // persisted (SaveOnConfigSet default true)
        other.SendHaptic(HapticPreset.ClickPulse);
        VRLog.Info("WorldUI", $"Handedness switch: {side} trigger pressed in Menu2D — dominant " +
                              $"hand is now {side} (laser + click move; fan/HUD follow on the other hand).");
        return true;
    }

    // ---- poke click (requirement 2) --------------------------------------------------------

    /// <summary>
    /// Fingertip poke on the flat screen = click at the poked RT position: quad-local
    /// hit → RT pixel → latched VirtualMouse warp+press on plane contact, release on
    /// withdraw. Both hands may poke (Menu2D grants Poke to both); the trigger-ray
    /// press and the poke press are mutually exclusive.
    /// </summary>
    private void TickPoke()
    {
        if (_quad == null || _rt == null)
            return;

        if (!WorldUIConfig.PokeClick.Value)
        {
            if (_pokePressing)
                EndPoke("poke click disabled");
            return;
        }

        Transform t = _quad.transform;

        if (_pokePressing)
        {
            VRHand? hand = _pokeHand;
            if (hand == null || !hand.HasPose)
            {
                EndPoke("hand lost");
                return;
            }

            float scale = hand.WorldScale;
            Vector3 tip = hand.Rig.IndexTip.position;
            float signed = Vector3.Dot(tip - t.position, t.forward); // viewer side < 0
            Vector3 local = t.InverseTransformPoint(tip);
            bool inRect = Mathf.Abs(local.x) <= 0.55f && Mathf.Abs(local.y) <= 0.55f;

            if (signed < -PokeReleaseMeters * scale || signed > PokeThroughMeters * scale || !inRect)
            {
                EndPoke(null); // normal withdraw (or slid off) → release = click/drag end
                return;
            }

            // Latch (same rationale as the trigger path): frozen at the press pixel
            // until the fingertip deliberately slides sideways.
            Vector3 onPlane = tip - t.forward * signed;
            if (_pokeLatched
                && (onPlane - _pokePressPoint).sqrMagnitude
                   > PokeDragUnlockMeters * PokeDragUnlockMeters * scale * scale)
            {
                _pokeLatched = false;
                // Same drag split as the trigger-ray path: execute mode (default) drives
                // uGUI drag events (sliders/scrollbars follow); virtualmouse/both press
                // the VM device. The VM path never moved a slider (test #7 button edges).
                if (WorldUIConfig.VirtualMouseButtons)
                {
                    if (!_vmPressed)
                    {
                        VirtualMouse.WarpTo(_pokePressPixel);
                        VirtualMouse.Press();
                        _vmPressed = true;
                    }
                }
                else
                {
                    BeginScreenDrag(_pokePressPixel);
                }
                VRLog.Info("WorldUI", "FlatScreen poke: latch OPENED → drag (fingertip slid " +
                                      $">{PokeDragUnlockMeters * 1000f:F0} mm laterally).");
            }
            if (_pokeLatched)
            {
                // Same-pixel re-warp (see the trigger path): keeps currency reclaimed.
                VirtualMouse.WarpTo(_pokePressPixel);
            }
            else
            {
                float px = (Mathf.Clamp(local.x, -0.5f, 0.5f) + 0.5f) * _rt.width;
                float py = (Mathf.Clamp(local.y, -0.5f, 0.5f) + 0.5f) * _rt.height;
                var p = new Vector2(px, py);
                VirtualMouse.WarpTo(p);
                if (_screenDragActive)
                    UpdateScreenDrag(p);
            }
            return;
        }

        if (_pressing)
            return; // trigger-ray press owns the pointer

        TryBeginPoke(VRHands.Left, t);
        if (!_pokePressing)
            TryBeginPoke(VRHands.Right, t);
    }

    private void TryBeginPoke(VRHand? hand, Transform t)
    {
        // Respect the per-mode interactor matrix: only hands whose Poke interactor
        // is enabled may poke the screen.
        if (hand == null || !hand.HasPose || !hand.Poke.Enabled)
            return;

        float scale = hand.WorldScale;
        Vector3 tip = hand.Rig.IndexTip.position;
        float signed = Vector3.Dot(tip - t.position, t.forward); // viewer side < 0
        if (signed < -PokeContactMeters * scale || signed > PokeThroughMeters * scale)
            return; // not touching the plane / far behind it

        Vector3 local = t.InverseTransformPoint(tip);
        if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f)
            return;

        var pixel = new Vector2((local.x + 0.5f) * _rt!.width, (local.y + 0.5f) * _rt.height);
        _pokePressing = true;
        _pokeHand = hand;
        _pokeLatched = WorldUIConfig.ClickLatch.Value;
        _pokePressPoint = tip - t.forward * signed;
        _pokePressPixel = pixel;
        VirtualMouse.WarpTo(pixel);
        if (WorldUIConfig.VirtualMouseButtons)
        {
            VirtualMouse.Press();
            _vmPressed = true;
        }
        hand.SendHaptic(HapticPreset.ClickPulse);
        LogUnderPointer(pixel);
        VRLog.Info("WorldUI", $"FlatScreen poke: {hand.Side} fingertip PRESS at RT pixel " +
                              $"({pixel.x:F0},{pixel.y:F0}), latch={_pokeLatched}.");
    }

    private void EndPoke(string? reason)
    {
        _pokePressing = false;
        _pokeHand = null;
        if (_vmPressed)
        {
            VirtualMouse.Release();
            _vmPressed = false;
        }
        // End any execute-mode drag (endDrag + pointerUp); no-op for a plain latched tap.
        EndScreenDrag();
        // reason == null is the normal withdraw → deliver the click; any named reason
        // (hand lost, poke disabled) is an abort.
        if (_pokeLatched && reason == null && WorldUIConfig.ExecuteClicks)
            DirectClick(_pokePressPixel);
        VRLog.Info("WorldUI", $"FlatScreen poke: fingertip RELEASE — " +
                              $"{reason ?? (_pokeLatched ? "CLICK (latched)" : "drag end")}.");
        _pokeLatched = false;
    }

    private void HideReticle()
    {
        if (_pressing)
        {
            _pressing = false;
            _latched = false;
            if (_vmPressed)
            {
                VirtualMouse.Release();
                _vmPressed = false;
            }
            EndScreenDrag();
            VRLog.Info("WorldUI", "FlatScreen pointer: press released (ray left the screen / pose lost).");
        }
    }

    // ---- direct click delivery (test #7) ----------------------------------------------------

    private static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>
        s_raycastResults = new(16);

    /// <summary>
    /// Delivers a latched click directly through uGUI ExecuteEvents at the given RT
    /// pixel — the exact mechanism the game itself uses for programmatic clicks
    /// (BaseButtons.clickButton, UI-ARCH §5). Bypasses the input module entirely, so
    /// no frame-edge/pointer-currency quirk can swallow it. Modality is respected by
    /// construction: EventSystem.RaycastAll only returns hits from ENABLED
    /// GraphicRaycasters (UIManager.ToggleLockUI disables them to lock the UI).
    /// </summary>
    private void DirectClick(Vector2 pixel)
    {
        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
        {
            VRLog.Warn("WorldUI", "DirectClick: no EventSystem — click dropped.");
            return;
        }

        var data = new UnityEngine.EventSystems.PointerEventData(es)
        {
            position = pixel,
            button = UnityEngine.EventSystems.PointerEventData.InputButton.Left,
            clickCount = 1,
            clickTime = Time.unscaledTime,
            eligibleForClick = true,
        };
        s_raycastResults.Clear();
        es.RaycastAll(data, s_raycastResults);
        if (s_raycastResults.Count == 0)
        {
            VRLog.Info("WorldUI", $"DirectClick at ({pixel.x:F0},{pixel.y:F0}): nothing under the pointer.");
            return;
        }

        UnityEngine.EventSystems.RaycastResult top = s_raycastResults[0];
        data.pointerCurrentRaycast = data.pointerPressRaycast = top;

        GameObject? pressTarget = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
            top.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        GameObject? clickTarget = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler
            <UnityEngine.EventSystems.IPointerClickHandler>(top.gameObject);
        data.pointerPress = pressTarget ?? clickTarget;

        if (data.pointerPress != null)
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                data.pointerPress, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);

        if (clickTarget != null)
        {
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                clickTarget, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            VRLog.Info("WorldUI", $"DirectClick at ({pixel.x:F0},{pixel.y:F0}) → clicked '{clickTarget.name}'.");
        }
        else
        {
            VRLog.Info("WorldUI", $"DirectClick at ({pixel.x:F0},{pixel.y:F0}): top hit " +
                                  $"'{top.gameObject.name}' has no IPointerClickHandler.");
        }
    }

    // ---- execute-mode drag delivery (flat-menu sliders / scrollbars / scroll-rects) --------

    /// <summary>
    /// Begin an ExecuteEvents drag session at the press pixel — the click latch has just
    /// opened into a deliberate drag. A uGUI <see cref="UnityEngine.UI.Slider"/> handle,
    /// scrollbar or scroll-rect moves ONLY through <c>IDragHandler.OnDrag</c>; the
    /// <see cref="DirectClick"/> down/up/click sets a value at the press pixel but can
    /// never fine-adjust it or reach an extreme the press pixel is not on (drag a volume
    /// slider to 0). Mirrors <see cref="Hands.Interact.UguiPointer"/> for the whole-screen
    /// composite: hits come from <c>EventSystem.RaycastAll</c> at the RT pixel (= the
    /// game's real screen pixel), exactly like <see cref="DirectClick"/>, so
    /// <c>pointerPressRaycast.module.eventCamera</c> (the UICamera) resolves the slider's
    /// local point correctly. pointerDown + <c>initializePotentialDrag</c> here;
    /// <see cref="UpdateScreenDrag"/> fires beginDrag/dragHandler as the pointer follows;
    /// <see cref="EndScreenDrag"/> fires endDrag + pointerUp on release.
    /// </summary>
    private void BeginScreenDrag(Vector2 pixel)
    {
        if (_screenDragActive)
            return;
        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
        {
            VRLog.Warn("WorldUI", "Flat-menu drag: no EventSystem — drag dropped (slider cannot follow).");
            return;
        }

        var data = new UnityEngine.EventSystems.PointerEventData(es)
        {
            pointerId = ScreenDragPointerId,
            position = pixel,
            pressPosition = pixel,
            button = UnityEngine.EventSystems.PointerEventData.InputButton.Left,
            eligibleForClick = false,  // a deliberate drag is not a click
            useDragThreshold = false,  // VR: begin dragging on the first move, no pixel threshold
        };
        s_raycastResults.Clear();
        es.RaycastAll(data, s_raycastResults);
        if (s_raycastResults.Count == 0)
        {
            VRLog.Info("WorldUI", $"Flat-menu drag: nothing under the pointer at ({pixel.x:F0},{pixel.y:F0}) — no drag.");
            return;
        }

        UnityEngine.EventSystems.RaycastResult top = s_raycastResults[0];
        data.pointerCurrentRaycast = data.pointerPressRaycast = top;

        GameObject? pressTarget = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
            top.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        _screenDragPress = pressTarget != null ? pressTarget : top.gameObject;
        data.pointerPress = _screenDragPress;

        _screenDragTarget = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler
            <UnityEngine.EventSystems.IDragHandler>(top.gameObject);
        data.pointerDrag = _screenDragTarget;
        if (_screenDragTarget != null)
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
                top.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.initializePotentialDrag);

        _screenDragData = data;
        _screenDragActive = true;
        _screenDragBegun = false;
        _screenDragLastPos = pixel;
    }

    /// <summary>
    /// Drive the active flat-menu drag as the pointer follows: update position + delta,
    /// fire beginDrag on the first move (ScrollRect wants it; Slider/Scrollbar have no
    /// IBeginDragHandler and simply ignore it) and dragHandler every tick thereafter.
    /// No-op when nothing draggable sits under the press.
    /// </summary>
    private void UpdateScreenDrag(Vector2 pixel)
    {
        if (!_screenDragActive || _screenDragData == null || _screenDragTarget == null)
        {
            _screenDragLastPos = pixel;
            return;
        }
        UnityEngine.EventSystems.PointerEventData data = _screenDragData;
        data.delta = pixel - _screenDragLastPos;
        data.position = pixel;
        _screenDragLastPos = pixel;

        if (!_screenDragBegun)
        {
            _screenDragBegun = true;
            data.dragging = true;
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                _screenDragTarget, data, UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);
            if (Time.unscaledTime - _lastScreenDragLog >= ScreenDragLogSeconds)
            {
                _lastScreenDragLog = Time.unscaledTime;
                VRLog.Info("WorldUI", $"Flat-menu drag BEGIN on '{_screenDragTarget.name}' at " +
                                      $"({pixel.x:F0},{pixel.y:F0}) — sliders/scrollbars now follow the " +
                                      "pointer via uGUI OnDrag (not just the click on the press pixel).");
            }
        }
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            _screenDragTarget, data, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
    }

    /// <summary>
    /// End the active flat-menu drag (endDrag + pointerUp at the last dispatched pixel);
    /// a safe no-op when none is active. A drag is never a click (it opened the latch), so
    /// no pointerClick is fired — matching the old virtual-mouse drag path (release only).
    /// </summary>
    private void EndScreenDrag()
    {
        if (!_screenDragActive)
            return;
        UnityEngine.EventSystems.PointerEventData? data = _screenDragData;
        if (data != null)
        {
            data.position = _screenDragLastPos;
            if (_screenDragPress != null)
                UnityEngine.EventSystems.ExecuteEvents.Execute(
                    _screenDragPress, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            if (_screenDragBegun && _screenDragTarget != null)
                UnityEngine.EventSystems.ExecuteEvents.Execute(
                    _screenDragTarget, data, UnityEngine.EventSystems.ExecuteEvents.endDragHandler);
            data.dragging = false;
            data.pointerDrag = null;
            data.pointerPress = null;
        }
        if (_screenDragBegun && Time.unscaledTime - _lastScreenDragLog >= ScreenDragLogSeconds)
        {
            _lastScreenDragLog = Time.unscaledTime;
            VRLog.Info("WorldUI", $"Flat-menu drag END at ({_screenDragLastPos.x:F0},{_screenDragLastPos.y:F0}).");
        }
        _screenDragActive = false;
        _screenDragBegun = false;
        _screenDragPress = null;
        _screenDragTarget = null;
        _screenDragData = null;
    }

    /// <summary>
    /// Press-time diagnostic (all click modes): logs the top uGUI raycast hits under
    /// the press pixel, so a click that lands on the wrong element (or on nothing) is
    /// attributable from the log alone.
    /// </summary>
    private static void LogUnderPointer(Vector2 pixel)
    {
        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
            return;
        var data = new UnityEngine.EventSystems.PointerEventData(es) { position = pixel };
        s_raycastResults.Clear();
        es.RaycastAll(data, s_raycastResults);
        if (s_raycastResults.Count == 0)
        {
            VRLog.Info("WorldUI", $"Under pointer ({pixel.x:F0},{pixel.y:F0}): nothing.");
            return;
        }
        int n = Mathf.Min(3, s_raycastResults.Count);
        var sb = new System.Text.StringBuilder(128);
        sb.Append($"Under pointer ({pixel.x:F0},{pixel.y:F0}): ");
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(" | ");
            GameObject go = s_raycastResults[i].gameObject;
            Canvas? root = go.GetComponentInParent<Canvas>();
            sb.Append($"'{go.name}'");
            if (root != null)
                sb.Append($" (canvas '{root.rootCanvas.name}')");
        }
        VRLog.Info("WorldUI", sb.ToString());
    }
}
