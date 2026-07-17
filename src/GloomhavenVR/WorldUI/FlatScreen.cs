using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
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
/// DESKTOP MIRROR (menu-blackscreen fix): while the RT redirect is active nothing
/// would reach the desktop backbuffer (the XR mirror shows an HMD eye, which shows
/// the quad at best). <see cref="OnEndOfFrame"/> — driven by the WorldUI driver's
/// WaitForEndOfFrame loop, i.e. after Unity's own XR mirror-view blit — blits the RT
/// to the backbuffer every frame, so the monitor always shows the full 2D UI and
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

    private GameObject? _quad;
    private Renderer? _quadRenderer;
    /// <summary>True while the virtual mouse left button is held by us (drag or virtualmouse mode).</summary>
    private bool _vmPressed;
    private RenderTexture? _rt;
    private bool _visible;
    private bool _pressing;
    private bool _mirrorLogged;

    // ---- click latch (test #6, requirement 1) --------------------------------------------
    /// <summary>True while the held press is frozen at its press pixel (click, not drag).</summary>
    private bool _latched;
    private Vector2 _latchedLocal;     // quad-local x/y of the press (reticle while latched)
    private Vector2 _latchedPixel;     // RT pixel of the press (re-warped while latched)
    private Vector3 _pressDirection;   // world ray direction at press time
    private float _dragOverSince = -1f;

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
    }

    // Captured camera stack (I1, hardware test #5). The list is reused across
    // frames; a CapturedCamera record only allocates when a NEW camera is first
    // captured (rare event — scene load / MainMenuVideo appearing).
    private readonly System.Collections.Generic.List<CapturedCamera> _captured = new(8);

    /// <summary>The stack's base (lowest-depth) camera — the only one whose clear we force.</summary>
    private CapturedCamera? _base;

    /// <summary>Cameras currently captured into the RT (camera-inventory diagnostics).</summary>
    private static readonly System.Collections.Generic.HashSet<Camera> CapturedSet = new();

    /// <summary>True while the FlatScreen has redirected this camera into its RT.</summary>
    internal static bool IsCaptured(Camera cam) => CapturedSet.Contains(cam);

    // Base-clear rationale (hardware test #4, P3b): the game's menu cameras ship
    // clearFlags=Depth — rendering that into a fresh RT leaves the COLOR buffer
    // (incl. alpha ≈ 0) undefined, so an alpha-blended quad shows nothing in the
    // HMD while the desktop Blit (which ignores alpha) looks perfect. The stack's
    // lowest-depth camera therefore gets an OPAQUE SolidColor clear forced (also
    // fixes RT garbage); every other camera keeps its own clear flags.
    private static readonly Color OpaqueBlack = new(0f, 0f, 0f, 1f);

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
    }

    /// <summary>
    /// Redirect every backbuffer game camera into our RT (I1, hardware test #5) and
    /// keep the stack's lowest-depth camera on an OPAQUE SolidColor clear (P3b — see
    /// field comment). Cheap per-tick re-assert: game code may rewrite targetTexture
    /// or clearFlags at any time, and new cameras (MainMenuVideo) appear mid-scene.
    /// Excluded: our rig head camera and cameras already targeting a different RT
    /// ('GUI 3D Camera' → character-assembly RT, per the test-#5 camera inventory).
    /// Originals are recorded on first capture and restored by <see cref="ReleaseStack"/>.
    /// No per-frame allocations: shared scan buffer + reused list; records allocate
    /// only when a new camera is first captured.
    /// </summary>
    private void CaptureStack()
    {
        if (_rt == null)
            return;

        Camera? head = Rig.VRRigDriver.HeadCamera;

        // 1. Capture new backbuffer cameras / re-assert the redirect on known ones.
        int count = Core.VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || (head != null && cam == head))
                continue;
            if (CapturedSet.Contains(cam))
            {
                if (cam.targetTexture != _rt) // game code rewrote it — re-assert
                    cam.targetTexture = _rt;
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
            };
            _captured.Add(record);
            CapturedSet.Add(cam);
            cam.targetTexture = _rt;
            // Full disposition line (test #10): rect + clear + depth + mask make the RT
            // composite reconstructable from the log alone.
            Rect r = cam.rect;
            VRLog.Info("WorldUI", $"FlatScreen stack capture: '{cam.name}' → RenderTexture " +
                                  $"(depth {cam.depth:F1}, clear {record.OriginalClearFlags} " +
                                  $"'{record.OriginalBackground}', rect ({r.x:F2},{r.y:F2},{r.width:F2},{r.height:F2}), " +
                                  $"mask 0x{cam.cullingMask:X8}).");
        }

        // 2. Compact dead entries (scene unloads destroy cameras behind our back).
        for (int i = _captured.Count - 1; i >= 0; i--)
        {
            if (_captured[i].Camera == null)
            {
                if (_captured[i] == _base)
                    _base = null;
                CapturedSet.Remove(_captured[i].Camera);
                _captured.RemoveAt(i);
            }
        }

        // 3. The stack's FIRST camera gets the forced opaque clear. Unity renders
        //    cameras targeting the same RT in ascending depth order, so pick the
        //    lowest depth. Depth ties exist (test-#5 inventory: 'Main Camera' and
        //    'UI Camera' both at depth 1.0) — the UICamera-tagged camera always
        //    composites LAST per game intent (CanvasManager binds all overlay
        //    canvases to it), so on a tie it must NOT be the base or its forced
        //    clear would erase the other cameras' output.
        CapturedCamera? newBase = null;
        for (int i = 0; i < _captured.Count; i++)
        {
            CapturedCamera c = _captured[i];
            if (newBase == null
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

        TickStackClears();
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
        for (int i = 0; i < _captured.Count; i++)
        {
            Camera cam = _captured[i].Camera;
            if (cam == null)
                continue;
            if (cam.targetTexture == _rt)
                cam.targetTexture = null;
            // Only the base (forced clear) and demoted overlays (SolidColor → Depth)
            // had their flags touched — leave everyone else alone (their flags may
            // have been legitimately changed by game code meanwhile).
            if (_captured[i] == _base || _captured[i].Demoted)
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
    }

    /// <summary>
    /// End-of-frame hook (WorldUI driver coroutine, after Unity's XR mirror blit):
    /// while the UICamera is redirected into our RT, copy the RT to the desktop
    /// backbuffer so the monitor never goes black and stays mouse-operable.
    /// </summary>
    public void OnEndOfFrame()
    {
        if (!_visible || _rt == null || !_rt.IsCreated())
            return;

        if (!_mirrorLogged)
        {
            _mirrorLogged = true;
            VRLog.Info("WorldUI", $"Desktop mirror active — FlatScreen RT ({_rt.width}x{_rt.height}) " +
                                  "blits to the backbuffer at end of frame.");
        }
        Graphics.Blit(_rt, (RenderTexture?)null);
    }

    public void Shutdown()
    {
        Core.Events.VREvents.SceneLoaded -= OnSceneLoaded;
        Hide();
        DestroyIndicator();
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
        // ModalFallback tracker asserted ModalUI and wants the full 2D composite.
        // Otherwise, in a plain UI-lock modal, the world-space confirmation surface
        // owns simple dialogs; everything else falls back 2D too.
        if (mode == VRMode.ModalUI)
            return ModalFallback.ScreenWanted
                   || !WorldUIConfig.Dialogs.Value
                   || !IsConfirmationBoxOpen();

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
            text.text = "GloomhavenVR\n<size=60%>starting… (intro plays on the desktop)</size>";
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
            _rt = new RenderTexture(Mathf.Max(Screen.width, 1280), Mathf.Max(Screen.height, 720), 24)
            {
                name = "GloomhavenVR.FlatScreenRT",
                antiAliasing = 1,
            };
            _rt.Create();
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
            _quadRenderer.sharedMaterial = new Material(shader) { mainTexture = _rt };

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
        }
        if (_pokePressing)
            EndPoke("screen hidden");
        ReleaseStack();

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
        }
        if (_visible)
            VRLog.Info("WorldUI", "FlatScreen hidden — captured cameras restored to the backbuffer.");
        _visible = false;
        _mirrorLogged = false;
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

        Vector3 target = h.position + fwd * (WorldUIConfig.ScreenDistance.Value * scale);
        // Quad primitive faces -Z (visible from -forward side): +Z away from viewer.
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        float width = WorldUIConfig.ScreenWidth.Value * scale;
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

        if (instant)
        {
            _placedHead = head;
            Transform? rig = Rig.VRRigDriver.RigRoot;
            _placedRigPos = rig != null ? rig.position : Vector3.zero;
            LogPlacement(head);
        }
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
            $"RT={( _rt != null ? $"{_rt.width}x{_rt.height}" : "NULL")} | " +
            $"head '{head.name}' pos={head.transform.position}, fwd={head.transform.forward}, " +
            $"mask=0x{head.cullingMask:X8}, clear={head.clearFlags}, stereo={head.stereoTargetEye}.");
    }

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
        // Live-tunable size/distance ([WorldUI] ScreenWidth/ScreenDistance): re-place
        // when the configured width no longer matches the quad (cheap float compare).
        float wantedWidth = WorldUIConfig.ScreenWidth.Value * PanelLayout.WorldScale;
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

        // Click latch (requirement 1, class doc): while pressed and latched the warp
        // position stays frozen at the press pixel; deliberate sustained ray movement
        // opens the latch into a real drag.
        if (_pressing && _latched)
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
                    // Drags always run through the virtual mouse; in execute mode the
                    // button wasn't pressed yet — press it now at the latched pixel so
                    // the drag starts where the press landed.
                    if (!_vmPressed)
                    {
                        VirtualMouse.WarpTo(_latchedPixel);
                        VirtualMouse.Press();
                        _vmPressed = true;
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

        // While frozen, keep re-warping to the SAME latched pixel: identical uGUI
        // position (no drag delta), but the per-tick write keeps pointer currency
        // reclaimed and the queued-event stream alive during a held press.
        bool frozen = _pressing && _latched;
        VirtualMouse.WarpTo(frozen ? _latchedPixel : pixel);

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
            VirtualMouse.Release();
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
                if (!_vmPressed) // execute mode: start the VM drag at the press pixel
                {
                    VirtualMouse.WarpTo(_pokePressPixel);
                    VirtualMouse.Press();
                    _vmPressed = true;
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
                VirtualMouse.WarpTo(new Vector2(px, py));
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
