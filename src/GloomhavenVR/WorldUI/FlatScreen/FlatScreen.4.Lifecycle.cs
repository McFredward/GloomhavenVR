using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private bool _cardLossFallbackNoted;

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
        // The rescue latch is static and therefore outlives this instance; a module teardown must
        // not leave the next FlatScreen booting with somebody's stale emergency still asserted.
        _rescueShow = false;
        _rescueRequester = string.Empty;
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
        // The [WorldUI] FlatScreen kill switch is gone (always on — user ruling 2026-08-11:
        // essential; the floating screen is the ONLY VR surface for the game's 2D flows).
        if (!WorldUIConfig.ConversionActive)
            return false;

        // LOADING GATE ("only the spinner on black"): while the VR loading indicator is
        // active the HMD deliberately shows just spinner + hands on the void — the 2D
        // composite would show the flat loading screen's hints/progress (or a half-torn-
        // down menu with no captured UI camera). The gate is a per-tick read of live
        // state, so the screen returns through this very policy the frame loading ends.
        if (LoadingIndicator.FlatScreenSuppressed)
            return false;

        // PROGRAMMATIC RESCUE (ModBuild 373) — ABOVE the map-room gate, and that placement IS the
        // fix. The rescue is only ever asserted by a watchdog that has established the game is
        // waiting on a decision the player has no way to make: on 2026-09-03 that was
        // UIDistributeRewardManager.IsDistributing standing true on the campaign map with the map
        // locked and no panel floated anywhere. In that state the map room is not a thing worth
        // protecting — it is the wall the player is stuck behind, and the two reasons the gate
        // below exists (the screen is "a lit quad hanging in front of the map"; the stereo
        // compositor fights the map room for the parchment renderer) are both cosmetic against a
        // campaign that cannot continue. Note that the manual A/X chord CANNOT reach past this
        // gate — TickManualChord clears _manualShow whenever no scenario board exists — so on the
        // map this latch is not a shortcut to the standing escape, it is the ONLY escape.
        //
        // The loading gate above deliberately stays higher: raising a composite of a half-torn-down
        // scene is not a rescue, and the requester's watchdog is still ticking when loading ends.
        if (_rescueShow)
            return true;

        // 3D MAP ROOM GATE ([Rig] Vanilla2DMap off, which is the default from ModBuild 230):
        // while the player is STANDING IN the campaign
        // map, the flat screen is not a second view of it — it is a lit quad hanging in front of
        // the map, and the stereo compositor behind it would fight the map room for the parchment
        // renderer. Same shape as the loading gate above: a per-tick read of live state, so the
        // screen returns through this very policy the frame the map room stands down. False in
        // every other situation, including the main menu, because MapRoomDriver.Active is only
        // ever true while the MAP rig is actually built.
        if (MapRoom.MapRoomDriver.Active)
            return false;

        VRMode mode = VRModeStateMachine.CurrentMode;
        if (mode != VRMode.ModalUI)
            _cardLossFallbackNoted = false;
        if (mode == VRMode.Menu2D)
            return true; // always auto-show in Menu2D — user ruling 2026-08-11: the manual chord
                         // needs a scenario board, so an OFF here left the main menu unreachable.

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
            // Native foreign-hand loss locks start before remote burn discovery and outlive
            // its material animation. Only the actual lock owners establish this empty modal;
            // owner effects, flight timing and the game's input lock remain unchanged.
            if (CardLossModalGuard.OwnsAllLocks(UIManager.Instance, Cards.Patches.HandSuppression.Active))
            {
                if (!_cardLossFallbackNoted)
                {
                    _cardLossFallbackNoted = true;
                    VRLog.Info("WorldUI", "CARD LOSS MODAL: empty FlatScreen fallback suppressed; " +
                        "all native UI locks belong to animating card hands.");
                }
                return false;
            }
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
    /// A/X is still held (the OPTIONS-window tap fires on release BELOW it — the
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
        // Chord always armed — user ruling 2026-08-11: the universal rescue must not be
        // switchable off (only its hold duration is tunable).
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
        NonDominantHold.Consumed = true; // the release must not also toggle the OPTIONS window
        // THE PLAYER'S PRESS OUTRANKS A WATCHDOG'S REQUEST. If a rescue raised the screen and he
        // now chords it away, leaving the latch set would re-raise it on the very next tick and
        // the toggle would read as broken hardware. Handing control back is safe: the requester's
        // watchdog is still running, so a flow that is genuinely still stuck will re-request —
        // it just has to earn the screen again through its own dwell, which is exactly right for
        // a player who has just said "not this".
        if (_rescueShow)
        {
            VRLog.Note("WorldUI", "RESCUE SCREEN RELEASED (manual chord): the player took the "
                                  + $"screen back by hand; the request from '{_rescueRequester}' "
                                  + "is dropped. Its watchdog may re-raise the screen if the flow "
                                  + "it is guarding is still stuck.");
            _rescueShow = false;
            _rescueRequester = string.Empty;
        }
        _manualShow = !_manualShow;
        NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
        VRLog.Info("WorldUI", $"MANUAL SCREEN CHORD: flat screen toggled {(_manualShow ? "ON" : "OFF")} " +
                              $"(non-dominant A/X held {threshold:F1}s in scenario).");
    }

    // ---- programmatic rescue (ModBuild 373) --------------------------------------------------

    /// <summary>True while a programmatic rescue holds the 2D composite up.</summary>
    internal static bool RescueScreenActive => _rescueShow;

    /// <summary>
    /// RAISE THE 2D COMPOSITE BECAUSE THE PLAYER HAS NO OTHER WAY FORWARD.
    ///
    /// <para>This is the guaranteed-escape entry point demanded by the standing ruling that a
    /// player must always end in one of two observable states — a usable panel on screen, or the
    /// action having happened, never "nothing". It goes through exactly the same latch the A/X
    /// chord drives (<see cref="ManualScreenActive"/>), so ModalFallback and every WorldUI surface
    /// release their floats and restore their windows to the 2D UI first, and the composite the
    /// player then sees contains the game's own screen-space widgets, clickable with the laser.
    /// Nothing here writes game state: no Show, no Hide, no SetActive, no synthesised click. The
    /// mod raises its OWN presentation surface and lets the player press the game's own button.</para>
    ///
    /// <para>IDEMPOTENT. Calling this every tick is the expected usage — a watchdog cannot know
    /// whether it already asked. Only the rising edge logs and only the rising edge changes state,
    /// so a per-frame caller costs one boolean compare. A second requester while one is standing is
    /// recorded in the name but does not re-log.</para>
    ///
    /// <para>THE CALLER OWNS THE RELEASE. There is deliberately no timeout here: a screen that
    /// takes itself away on a timer would put the player back in front of nothing, which is the
    /// exact failure this exists to prevent. It ends when the requester says the flow is unstuck
    /// (<see cref="ReleaseRescueScreen"/>), when the player chords it away by hand, on the next
    /// scene load, or on module shutdown.</para>
    /// </summary>
    /// <param name="requester">Short name of the watchdog, for the log and for the release line.</param>
    /// <param name="why">One sentence naming what the player is stuck on. Logged once, at Alert.</param>
    internal static void RequestRescueScreen(string requester, string why)
    {
        if (_rescueShow)
            return;
        _rescueShow = true;
        _rescueRequester = requester;
        // HW-VERIFY: the ONLY line that says the mod put a last-resort surface in front of the
        // player. If a deadlock report ever arrives WITH this line in the log, the escape fired and
        // the 2D composite was up — look at what he could not do on it. If a deadlock report
        // arrives WITHOUT it, the escape did not fire and its trigger is the thing to read next.
        VRLog.Alert("WorldUI", $"RESCUE SCREEN RAISED by '{requester}': {why} The mod has forced "
                              + "the full 2D composite up through the same latch the manual A/X "
                              + "chord uses, so every floated window has been restored to the 2D "
                              + "UI and the game's own widgets are on the screen quad, clickable "
                              + "with the laser. NOTHING WAS WRITTEN TO THE GAME — no Show, no "
                              + "Hide, no SetActive, no synthesised click; the player presses the "
                              + "game's own button. This screen does NOT time out: it stands until "
                              + "the requester reports the flow unstuck, the player chords it away, "
                              + "the scene changes, or the module shuts down.");
    }

    /// <summary>
    /// End a rescue. Idempotent and safe to call from a per-tick watchdog; only the falling edge
    /// logs. A caller that is not the current requester still releases — the latch is a single
    /// emergency, not a refcount, and two watchdogs both wanting the screen up at once has never
    /// happened; if it ever does, the second one's next tick re-raises it within a frame.
    /// </summary>
    internal static void ReleaseRescueScreen(string why)
    {
        if (!_rescueShow)
            return;
        string requester = _rescueRequester;
        _rescueShow = false;
        _rescueRequester = string.Empty;
        // HW-VERIFY: pairs with RESCUE SCREEN RAISED. Its presence proves the escape was BOUNDED —
        // that the mod let go of the player's view again — which is the one way a rescue could
        // itself become the next deadlock report.
        VRLog.Note("WorldUI", $"RESCUE SCREEN RELEASED: the request from '{requester}' ended "
                              + $"({why}). ManualScreenActive returns to the chord's own state and "
                              + "the floated surfaces resume normally.");
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
        bool want = preMenu && VRSession.IsRunning; // shell unconditional (user ruling 2026-08-11)
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
            MrBacking.Label(text); // free-floating over the room when MR is already on at start
            VRLayers.Apply(_indicator); // head camera masks include the mod layer (CAMERA-POLICY §2)
            VRLog.Info("WorldUI", "Starting indicator shown (pre-menu scene, FlatScreen gated).");
            _indicatorFacingLogged = false;
        }

        Transform h = head.transform;
        // YAW ONLY (user ruling 2026-09-04: "Das soll generell bei keinem Fenster der Fall sein.
        // Ausschließlich yaw-achse."). TMP fronts render along −Z, so the yaw-only rotation already
        // faces the head — this line used to inherit the head's pitch as well, which stood the label
        // at a slope whenever the player was not looking level. See HeadFacing.
        HeadFacing.Facing facing = HeadFacing.YawOnly(h);
        // POSITION KEPT ON THE RAW GAZE, AND THIS ONE IS NOT A JUDGEMENT CALL: the indicator's whole
        // job is to be visible in a void while the intro plays flat, and it is re-derived every frame
        // for exactly that reason. On the flattened forward it would slide out of view the moment the
        // player looked up or down — the one failure this label exists to prevent.
        Vector3 pos = h.position + h.forward * 1.5f;
        _indicator.transform.SetPositionAndRotation(pos, facing.Rotation);
        // ONCE PER INDICATOR, NOT PER FRAME. This method is the per-frame tick; the placement it
        // performs is continuous, so the proof line is gated to the first frame after the indicator
        // is built. That is enough to answer "was this label upright?" and cannot flood.
        if (!_indicatorFacingLogged)
        {
            _indicatorFacingLogged = true;
            HeadFacing.LogPlaced("StartingIndicator", facing, pos,
                "along the RAW gaze at 1.5 m — a pre-menu label that must stay in view at any head "
                + "pitch, so it deliberately keeps following the full gaze");
        }
    }

    /// <summary>One-shot gate for the starting indicator's yaw-only proof line — see
    /// <see cref="TickStartingIndicator"/>. Cleared when a new indicator is built.</summary>
    private bool _indicatorFacingLogged;

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
        ScreenVisible = true;
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
        ScreenVisible = false;

        // ROUND 8 (one-frame backbuffer gap, same artifact family as the pre-convert 2D blackout).
        // ReleaseStack() above puts every captured game camera back on the BACKBUFFER — and under
        // active XR the backbuffer IS the HMD eye textures, so a desktop-only (stereoTargetEye =
        // None) camera composites into an eye for that frame. TickDesktopCameraScrub, which is
        // what normally keeps those cameras off the backbuffer, already ran at the TOP of this
        // same Tick (it saw _visible == true and stood down), so without this call the game's 2D
        // UI and 3D composite reach the headset for exactly one frame on every screen hide.
        // Re-running it here — after _visible went false, so it now WANTS the scrub — closes the
        // hand-off inside the frame instead of one frame later. It is the identical work the next
        // tick would do, just not one render too late; ReleaseDesktopScrub cannot re-enter Hide().
        TickDesktopCameraScrub();
        _mirrorLogged = false;
        _handsDiagLogged = false;
        _placedHead = null;
        _offGazeSince = -1f;
        _gliding = false;
    }

}
