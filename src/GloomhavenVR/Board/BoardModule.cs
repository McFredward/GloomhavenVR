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
/// HERO PLACEMENT (verified for test #13, decompiled GH.Runtime): scenario-start
/// placement runs while the mode is CardSelection — the Choreographer wait-state is
/// WaitingForCardSelection (NOT a targeting state), and the P5 matrix gives the
/// dominant hand Ray there, so the whole pick+click pipeline is live. Hover:
/// WorldspaceStarHexDisplay.HighlightSelectedPlacementHex → InteractableUnderMouse →
/// our patched MF.FindInteractableAtMousePosition (m_HexSelectionRaycastLayer, the
/// same tile layer) sets Waypoint.s_PlacementTile + the glowing star
/// (WorldspaceStarHexDisplay.cs:436/549/627/3822). Click: our CommonLoop postfix →
/// LateUpdate → TileBehaviour.s_Callback → Choreographer.TileHandler placement
/// branch (WaitingForCardSelection + clientTile == Waypoint.s_PlacementTile →
/// PlaceActorAtRoundStart, Choreographer.cs:1841-1866) — re-picking another glowing
/// hex is the same flow again. Nothing board-side needs a CardSelection extension.
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

        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — board targeting not installed.");
            return;
        }

        _active = true;

        VRSession.Harmony?.PatchAll(typeof(Patches.MF_FindInteractableAtMousePosition_Patch));
        VRSession.Harmony?.PatchAll(typeof(Patches.InputManager_CursorPosition_Patch));
        VRSession.Harmony?.PatchAll(typeof(Controller_CommonLoop_Patch));

        _driverGo = new GameObject("GloomhavenVR.Board");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<BoardDriver>();

        VRLog.Info(Name, "Board targeting installed (pick + cursor + click patches, AoE stick control).");
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
        }
        // Harmony patches are removed collectively by Plugin.OnDestroy (UnpatchSelf).
    }
}
