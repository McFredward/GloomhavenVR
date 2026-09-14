using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// Board touch/ray targeting (Phase 3a, R5 "touch the board"):
///
/// - <b>Picking</b>: prefixes on <c>MF.FindInteractableAtMousePosition</c> and
///   <c>InputManager.get_CursorPosition</c> substitute the VR pick (near fingertip
///   touch / far hand ray, <see cref="BoardPick"/>). <c>HoverRegisterer</c> and the
///   pooled <c>HexSelect_Control</c> highlight pipeline run unchanged on top.
/// - <b>Click commit</b>: <see cref="BoardClickDriver"/> + a postfix on
///   <c>Controller.CommonLoop</c> drive the game's own click path
///   (LateUpdate → ShowNormalInterface → TileBehaviour.s_Callback) — never
///   ScenarioRuleClient directly, so undo/MP/second-click-confirm stay intact.
/// - <b>AoE</b>: <see cref="AoeControl"/> — thumbstick flick →
///   <c>WorldspaceStarHexDisplay.RotateAOEClockwise</c> + star redraw + haptic.
/// - <b>UX</b>: <see cref="TargetingUx"/> — valid-target hover haptics; hex-center
///   cursor snap via config; enemy stat popup rides the game's hover flow.
///
/// Mode integration: the VRModeStateMachine defaults already cover Phase 3a —
/// BoardTargeting = Ray|Poke and all five targeting wait-states are mapped
/// (VRModeStateMachine.cs), so no MapMessage/SetTargetingState/SetInteractorPolicy
/// extension calls are needed here. Near-touch additionally works in every mode
/// whose policy includes Poke (e.g. character placement during CardSelection).
///
/// HERO PLACEMENT (re-verified for test #14, decompiled GH.Runtime): scenario-start
/// placement runs while the mode is CardSelection — the Choreographer wait-state is
/// WaitingForCardSelection (NOT a targeting state), and the P5 matrix gives the
/// dominant hand Ray there, so the whole pick+click pipeline is live. Hover:
/// WorldspaceStarHexDisplay.Update → PointingAtANewTile → Interactable() — which
/// FIRST bails when UIManager.IsPointerOverUI (WorldspaceStarHexDisplay.cs:3813-3819,
/// the gate the test-#13 analysis missed; see UIManager_IsPointerOverUI_Patch) —
/// then InteractableUnderMouse → our patched MF.FindInteractableAtMousePosition
/// (m_HexSelectionRaycastLayer, the same tile layer) →
/// HighlightSelectedPlacementHex sets Waypoint.s_PlacementTile + the glowing star
/// (WorldspaceStarHexDisplay.cs:436/549/627/3822). Click: our CommonLoop postfix →
/// LateUpdate → TileBehaviour.s_Callback → Choreographer.TileHandler placement
/// branch (WaitingForCardSelection + clientTile == Waypoint.s_PlacementTile →
/// PlaceActorAtRoundStart, Choreographer.cs:1841-1866) — re-picking another glowing
/// hex is the same flow again. The destination must be HOVERED before the click so
/// s_PlacementTile is armed; with the pointer-over-UI truth patched for VR (and the
/// giant unfitted host planes gone, test #14 item 1) the VR hover arms it exactly
/// like the mouse hover does.
///
/// SECOND hardware-found gate on the same chain: WSHD.Update:425 also requires
/// !TimeManager.IsPaused, and in VR the pause taken by camera-follow transitions
/// (SmartFocus(..., pauseDuringTransition: true)) was never released because the
/// only OnArrivedToPoint call site is the prefix-skipped CameraController.LateUpdate
/// — see <see cref="CameraArrivalGuard"/> (the fix) and
/// Patches.Placement_UpdateGate_Diagnostics (the gate-chain evidence ladder).
///
/// Active when VR runs, and in Dev mode ([Dev] Enabled) so the whole pick/click
/// pipeline is exercisable flat via [Dev] SimulateHands.
/// </summary>
internal sealed class BoardModule : IVRModule
{
    public string Name => "Board";

    private GameObject? _driverGo;
    private bool _active;

    public void Init()
    {
        // P5 (MISSION A.9): module-owned config file (dev.gloomhavenvr.board.cfg).
        // Bound even when the module stays dormant, so the section always exists.
        BoardConfig.Bind();
        // P8: figure-grab config (dev.gloomhavenvr.figuregrab.cfg) — bound up-front so
        // FigureGrabbable.CanGrab can read the toggle from frame one.
        FigureGrab.FigureGrabConfig.Bind();
        // Issue #6: hex-highlight swim mitigation config (dev.gloomhavenvr.hexhighlight.cfg).
        HexHighlightFix.BindConfig();
        // Selection-phase pending cue toggle (dev.gloomhavenvr.selectionready.cfg) — bound up-front
        // so the section always exists even when the driver stays dormant.
        SelectionReadyHighlighter.Bind();

        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — board targeting not installed.");
            return;
        }

        _active = true;

        VRSession.Harmony?.PatchAll(typeof(Patches.MF_FindInteractableAtMousePosition_Patch));
        VRSession.Harmony?.PatchAll(typeof(Patches.InputManager_CursorPosition_Patch));
        VRSession.Harmony?.PatchAll(typeof(Patches.UIManager_IsPointerOverUI_Patch));
        // USER-BUG (ModBuild 158, "Weiterhin wenn ich mit dem laser drauf hovere kommt kein
        // Hinweis." — said of water, true of far more): HoverRegisterer is the ONLY caller of
        // IHoverable.OnCursorEnter/Exit in the whole game, and it un-projects the cursor through
        // the cached, PARKED Camera.main — not the head camera the patched cursor was measured
        // in. So the hint cards that have no second producer (difficult terrain incl. WATER,
        // traps, hazardous terrain, spawners) were silent for the entire life of the rig. Give
        // it the VR pick ray directly, on its own targetLayer mask. See HoverPickPatch.
        VRSession.Harmony?.PatchAll(typeof(Patches.HoverPickPatch));
        // Feature #3 fix: clear the stale cursor-hover hex highlight when the VR laser
        // is on no hex (WorldspaceStarHexDisplay never deactivates s_CursorHighlightedStar
        // on a null pick; see HexHoverClear).
        VRSession.Harmony?.PatchAll(typeof(Patches.HexHoverClear));
        // Issue #6: the hex-selection highlight's shader (OmniDecal_Shd) is a screen-space
        // depth-reconstruction projector whose layers swim with head pose in VR stereo —
        // swap the material onto the bundled stereo-stable GloomhavenVR/HexDecalStable
        // after every game material write (fallback: zero the offending layers when the
        // bundle lacks the shader; see HexHighlightFix).
        VRSession.Harmony?.PatchAll(typeof(HexHighlightFix.HexSelect_ProjectorMaterialAdjustment_Patch));
        // ModBuild 132 finding 5: with a 3D environment room now spawned around the play space
        // (Core/SkyAlternative), any LIVE UnityEngine.Projector in the game paints its decal onto
        // the room's floor a metre under the board — projectors have no distance limit beyond
        // their far clip and ignore nothing by default. Guard them additively (mod layer ORed
        // into ignoreLayers, restored on shutdown); this postfix is the game's own decal-projector
        // creation site, so pooled/re-created decals are covered at birth. See HexHighlightFix,
        // ENVIRONMENT-ROOM BLEED.
        VRSession.Harmony?.PatchAll(typeof(HexHighlightFix.ProjectorModifier_Awake_Patch));
        VRSession.Harmony?.PatchAll(typeof(Controller_CommonLoop_Patch));
        // P8: suppress the game's per-frame figure-transform writes for HELD actors only,
        // so a grabbed mini can ride the hand (gated by HeldFigures.Owns).
        VRSession.Harmony?.PatchAll(typeof(FigureGrab.ActorBehaviour_HeldTransform_Patch));
        // Return held figures before native action setup samples their transform or plays a clip.
        VRSession.Harmony?.PatchAll(typeof(FigureGrab.Choreographer_HeldFigureAction_Patch));
        VRSession.Harmony?.PatchAll(typeof(FigureGrab.MF_HeldFigureAnimation_Patch));
        // USER-BUG: during the action phase, laser-clicking a NON-current player's initiative
        // avatar re-docked the wrong actor's cards and deadlocked the action board. Reject that
        // human click with the game's own invalid-click SFX, keeping the current actor selected.
        VRSession.Harmony?.PatchAll(typeof(Patches.InitiativeTrackPlayerAvatar_OnClick_Guard));
        // FREE CHARACTER FOCUS (user ruling 2026-08-08, "das Wechseln des Characters darf nie
        // blockiert sein"): the game's interaction-isolation interceptor
        // (InteractabilityManager.ShouldAllowClickForExtendedButton, consulted by
        // ExtendedButton.OnPointerClick) swallows the portrait click whenever a level message /
        // interaction profile is loaded — i.e. exactly while the player owes a decision. Allow
        // it through for initiative PORTRAITS only, and only while the focus gate is open (where
        // the click is a pure read-only view change). Every other isolated control is untouched.
        VRSession.Harmony?.PatchAll(typeof(Patches.InteractabilityManager_PortraitFocusBypass));
        // MP test item #8a: online with >1 participant, refuse selecting a character that is
        // assigned to ANOTHER player (portrait seam is inside the OnClick guard above; this
        // closes the board-miniature seam in Choreographer.TileHandler) with the game's own
        // denied SFX. Offline/solo: both guards bail before touching anything.
        VRSession.Harmony?.PatchAll(typeof(Patches.Choreographer_TileHandler_OwnershipGuard));
        // USER-BUG (MP hardware 2026-08-07) — THE action deadlock. The OTHER half of the vanilla
        // portrait click (InitiativeTrackPlayerAvatar.cs:40) opens the screen-space All-Cards
        // viewer, which VR can neither show nor close, and its IsFullCardPreviewShowing latch
        // then silently swallows EVERY card-action click (FullAbilityCard.cs:613) until a turn
        // hand-off that can no longer happen. Refuse to open it while conversion is active.
        VRSession.Harmony?.PatchAll(typeof(Patches.AllCardsViewerBlock));
        // MP test item #8b: when the host reassigns the locally SELECTED character to another
        // player, fall back to a still-owned character (or the game's own no-selection state).
        // The patch only arms SelectionOwnershipFallback; BoardDriver ticks it.
        VRSession.Harmony?.PatchAll(typeof(Patches.CharacterManager_OnControlReleased_Fallback));
        // MP bug #7: world-space name tag over every hex ping (own + received, flat or VR) —
        // the game's own screen-space ping tooltip is unreadable from a VR head pose.
        VRSession.Harmony?.PatchAll(typeof(Patches.PingNameTag_Patch));
        // USER REPORT (3-player hardware): "Wenn es keine Gegnerinfos gibt weil aktuell sichtbar
        // gar keine Gegner existieren, soll die Phase übersprungen werden in dem der host nochmal
        // mit 'Fortfahren' bestätigen muss." The game HAS that skip (Choreographer.cs:3705) but
        // guards it on ClientMonsterObjects, which concatenates m_ClientObjects — chests and props
        // make the count non-zero while the screen stays blank. Count the rows the reveal will
        // actually draw and press the host's own ReadyButton for them. Host-only, no new wire.
        VRSession.Harmony?.PatchAll(typeof(Patches.InitiativeTrack_ShowMonsterClasses_ArmSkip));
        VRSession.Harmony?.PatchAll(typeof(Patches.InitiativeTrack_Update_TickSkip));
        // TEMPORARY test-#14 item-5 evidence (hero placement) — remove once confirmed.
        VRSession.Harmony?.PatchAll(typeof(Patches.Placement_Hover_Diagnostics));
        VRSession.Harmony?.PatchAll(typeof(Patches.Placement_UpdateGate_Diagnostics));
        VRSession.Harmony?.PatchAll(typeof(Patches.Placement_Click_Diagnostics));

        _driverGo = new GameObject("GloomhavenVR.Board");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<BoardDriver>();
        // P8: figure grab (grip-grab a mini into the hand to inspect it).
        _driverGo.AddComponent<FigureGrab.FigureGrabDriver>();
        // Feature #3: laser + dominant-hand "A" (primaryButton) → game hex ping.
        _driverGo.AddComponent<BoardPing>();
        // Selection cue: pulse a soft highlight on the INITIATIVE ORDER BAR entry of every local
        // player figure that has not yet finished card selection (two cards / long rest).
        _driverGo.AddComponent<SelectionReadyHighlighter>();
        // FREE CHARACTER FOCUS: the initiative-track focus/turn rings and the frame around the
        // LOCAL control board. Peers' boards are drawn by Net/RemoteFocusOutline from the same
        // FocusCue palette; the focus itself is set by the portrait-click seam above.
        _driverGo.AddComponent<FocusDriver>();

        // One sweep now (and the single HEX PROJECTOR log line that proves the guard ran);
        // later projectors are caught by the creation-site postfix and by the scene-load arm.
        HexHighlightFix.InstallProjectorGuard();

        // The round-fifteen held-prop-flash A/B (PropOcclusionGate + its SettingChanged hook) was
        // armed here. It answered on ModBuild 467 — the gate was genuinely down for all 120
        // head-camera passes and the user saw no change — so the occlusion channel is excluded by
        // experiment and the whole rig is gone. Its successor needs nothing here: the round-
        // seventeen render-pass probe arms itself from the prop's own hold window
        // (PropAnimBelt.RenderPass.cs) and unhooks the moment that window closes.

        VRLog.Info(Name, "Board targeting installed (pick + cursor + click patches, AoE stick control, figure grab).");
        VRLog.Info(Name, BoardConfig.TouchTilesWithFingertip.Value
            ? "FINGERTIP TILE TOUCH armed: hold the GRIP and put an index fingertip on a hex to commit " +
              $"the same click the laser trigger commits (range {BoardConfig.TouchRange.Value:0.00} m real, " +
              "one commit per hex entry, laser cannot double-commit while the finger owns the pick)."
            : "FINGERTIP TILE TOUCH off ([Board] TouchTilesWithFingertip=false) — board commits come " +
              "from the laser trigger only.");
    }

    public void Shutdown()
    {
        if (_driverGo != null)
        {
            Object.Destroy(_driverGo);
            _driverGo = null;
        }

        if (_active)
        {
            _active = false;
            // Hot-reload hygiene: everything here is static.
            BoardPick.Reset();
            BoardClickDriver.Reset();
            AoeControl.Reset();
            TargetingUx.Reset();
            HexHighlightFix.Reset();
            SelectionOwnershipFallback.Reset();
            Patches.PingNameTag.Reset();
            Patches.EnemyInfoPhaseSkip.Reset();
            Patches.PickPhaseInitiativeTrack.Reset();
            Patches.HoverPickPatch.Reset();
        }
        // Harmony patches are removed collectively by Plugin.OnDestroy (UnpatchSelf).
    }
}
