using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.WorldUI;

// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them to something
// prettier. This is the canonical statement of the rule; the other split families
// (CanvasConversion, ModalFallback, FlatScreenStereo, VROptionsTab) point here.
// The csproj uses the SDK's default `**/*.cs` glob, so compile order is the
// filename sort (MSBuild sorts glob results OrdinalIgnoreCase), and a partial
// class's members land in metadata in compile order — where FIELD INITIALIZERS
// run. `FlatScreen.Pointer.cs` would sort BEFORE `FlatScreen.cs` and silently
// reorder them. Numbered, the six parts concatenate back into the original member
// order — which is why splitting this file produced an EMPTY compiled-form diff
// rather than a 2 400-line permutation.

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
internal sealed partial class FlatScreen
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
    // _vmPressed is GONE with [WorldUI] ClickMode (user ruling 2026-08-13): nothing PRESSES the
    // virtual mouse any more — the device still carries the pointer POSITION (hover), while
    // clicks and drags go through uGUI ExecuteEvents / the drag handlers, unconditionally.
    private RenderTexture? _rt;
    private bool _visible;

    /// <summary>
    /// Static mirror of <see cref="_visible"/> for diagnostics that cannot reach the instance.
    ///
    /// <para>WHY (round 8): the flat screen owns the ONLY per-eye render targets in the mod (the
    /// stereo screen's left/right RTs), so "was the screen even up?" is the first question any
    /// one-eye artifact report has to answer. In a scenario it is HIDDEN — the game cameras are
    /// scrubbed to an offscreen sink instead — which makes the whole per-eye RT machinery inert
    /// there and rules it out of a scenario-side artifact in one log line. Written only by
    /// Show/Hide (rare), read only by log paths.</para>
    /// </summary>
    internal static bool ScreenVisible { get; private set; }
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
    /// <summary>True while the scrubbed cameras' draw-skip hooks are registered on Camera.onPreCull/onPostRender.</summary>
    private bool _scrubDrawSkipHooked;
    /// <summary>
    /// The one scrubbed camera whose culling mask is currently zeroed, and its real value.
    /// Single-slot by construction: Unity culls, renders and post-renders one camera at a time,
    /// so a save/restore pair can never nest (FlatScreen.3.Desktop SyncScrubDrawSkip).
    /// </summary>
    private Camera? _maskedCam;
    private int _maskedValue;

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
    /// <summary>Throttle stamp for the grip-chord "poke withheld" line (see <c>TryBeginPoke</c>).</summary>
    private float _nextPokeGripLogAt;

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

    // ==========================================================================================
    // PROGRAMMATIC RESCUE LATCH (ModBuild 373) — the same screen the chord raises, asked for by
    // code instead of by a press.
    //
    // WHY IT HAD TO BE BUILT, AND WHY IT IS STATIC. The 2026-09-03 travel-event deadlock leaves
    // the player on the CAMPAIGN MAP with `UIDistributeRewardManager.IsDistributing` true, the map
    // locked by `AdventureMapUIManager.LockOptionsInteraction`, and no panel anywhere. The mod's
    // standing universal escape is the A/X chord — and on the map that chord DOES NOT EXIST:
    // `TickManualChord` returns immediately (and clears `_manualShow`) unless
    // `VRModeStateMachine.ScenarioBoardExists`, and `WantVisible` refuses the screen outright
    // while `MapRoomDriver.Active`. So the one place this deadlock happens is the one place the
    // rescue was unreachable. ModBuild 370's lane declined to build this entry point; that call
    // was overruled on 2026-09-03 after the second identical report.
    //
    // The latch is SEPARATE from `_manualShow` on purpose: `_manualShow` is a chord state the
    // scenario-board guard is entitled to clear every tick, and a rescue that is wiped by that
    // guard is not a rescue. It is STATIC because the requester is a WorldUI surface with no
    // handle on the FlatScreen instance, exactly like `ManualScreenActive` above; the module owns
    // a single FlatScreen for the life of the session.
    //
    // IT IS NOT A SECOND POLICY. While it is set, `ManualScreenActive` is true — bit for bit the
    // state the chord produces — so ModalFallback's release-on-manual-screen path and every
    // surface's `!FlatScreen.ManualScreenActive` gate behave IDENTICALLY to a player press. There
    // is deliberately no second code path for anything to disagree about.
    // ==========================================================================================
    private static bool _rescueShow;
    private static string _rescueRequester = string.Empty;

    /// <summary>
    /// True while the full screen forces the 2D composite — the manual chord OR a programmatic
    /// rescue request (<see cref="RequestRescueScreen"/>). ModalFallback reads this to RELEASE
    /// its floating window conversions — a window re-parented onto a world-space host would be
    /// missing from the screen's RT composite, so the universal rescue must always put the
    /// windows back into the 2D UI first.
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

    private void OnSceneLoaded(Core.Events.SceneLoadedEvent e)
    {
        ReleaseStack();
        // A rescue is asked for by a watchdog watching ONE stuck flow in ONE scene. A scene load
        // ends that flow's world, so the latch is dropped here rather than trusted to a requester
        // that may itself have been destroyed with the scene. This can never strand the player:
        // every requester re-asks from its own per-tick watchdog, so a request that is still true
        // after the load is back within a frame, and one that is not was stale.
        ReleaseRescueScreen("<scene load>");
    }

    public void Tick()
    {
        NativeVideoWindow.Tick();
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
        UpdateScreenTakeover();

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

}
