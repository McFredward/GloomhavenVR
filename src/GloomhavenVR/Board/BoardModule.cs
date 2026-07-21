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
/// pipeline is exercisable flat via [Dev] SimulateHands (+ [Board] ForceFarMode).
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

        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — board targeting not installed.");
            return;
        }

        _active = true;

        VRSession.Harmony?.PatchAll(typeof(Patches.MF_FindInteractableAtMousePosition_Patch));
        VRSession.Harmony?.PatchAll(typeof(Patches.InputManager_CursorPosition_Patch));
        VRSession.Harmony?.PatchAll(typeof(Patches.UIManager_IsPointerOverUI_Patch));
        // Feature #3 fix: clear the stale cursor-hover hex highlight when the VR laser
        // is on no hex (WorldspaceStarHexDisplay never deactivates s_CursorHighlightedStar
        // on a null pick; see HexHoverClear).
        VRSession.Harmony?.PatchAll(typeof(Patches.HexHoverClear));
        // Issue #6: the hex-selection highlight's shader (OmniDecal_Shd) is a screen-space
        // depth-reconstruction projector whose layers swim with head pose in VR stereo —
        // zero the offending layers after every game material write (see HexHighlightFix).
        VRSession.Harmony?.PatchAll(typeof(HexHighlightFix.HexSelect_ProjectorMaterialAdjustment_Patch));
        VRSession.Harmony?.PatchAll(typeof(Controller_CommonLoop_Patch));
        // P8: suppress the game's per-frame figure-transform writes for HELD actors only,
        // so a grabbed mini can ride the hand (gated by HeldFigures.Owns).
        VRSession.Harmony?.PatchAll(typeof(FigureGrab.ActorBehaviour_HeldTransform_Patch));
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

        VRLog.Info(Name, "Board targeting installed (pick + cursor + click patches, AoE stick control, figure grab).");
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
        }
        // Harmony patches are removed collectively by Plugin.OnDestroy (UnpatchSelf).
    }
}
