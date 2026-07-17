using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.Board.Patches;

// ---------------------------------------------------------------------------
// TEMPORARY diagnostics for test #14 item 5 (hero-placement second click):
// Info-level evidence at the two stages the vanilla placement flow gates on.
// Event-driven and change-deduped — no per-frame log spam, no allocations
// between transitions. Remove after the placement flow is confirmed on HMD.
// ---------------------------------------------------------------------------

/// <summary>
/// HOVER stage: <c>WorldspaceStarHexDisplay.HighlightSelectedPlacementHex</c> is the
/// only writer of <c>Waypoint.s_PlacementTile</c> (decompiled WorldspaceStarHexDisplay
/// .cs:551 clears / :627 sets; called from Update on pointed-at-interactable change,
/// :436). The postfix logs every RESULT change: which tile became the armed placement
/// destination — or that hover produced none (the pre-fix VR symptom: IsPointerOverUI
/// stuck true → Interactable() null → s_PlacementTile permanently null).
/// </summary>
[HarmonyPatch(typeof(WorldspaceStarHexDisplay), nameof(WorldspaceStarHexDisplay.HighlightSelectedPlacementHex))]
internal static class Placement_Hover_Diagnostics
{
    private static CClientTile? _lastLogged;
    private static bool _loggedOnce;

    private static void Postfix()
    {
        CClientTile? tile = Waypoint.s_PlacementTile;
        if (_loggedOnce && ReferenceEquals(tile, _lastLogged))
            return;
        _loggedOnce = true;
        _lastLogged = tile;
        VRLog.Info("Board", "[Placement] hover refresh → s_PlacementTile=" +
                            $"{TileName(tile)}, overUI={UIManager.IsPointerOverUI}, " +
                            $"pick={BoardPick.Source}, mode={Core.Events.VRModeStateMachine.CurrentMode}.");
    }

    internal static string TileName(CClientTile? tile) =>
        tile != null && tile.m_Tile != null
            ? $"({tile.m_Tile.m_ArrayIndex.X},{tile.m_Tile.m_ArrayIndex.Y})"
            : "null";
}

/// <summary>
/// GATE stage: the fresh hardware log showed TileHandler clicks with armed=null while
/// the hover diagnostic above stayed silent ALL session — so the block sits in
/// <c>WorldspaceStarHexDisplay.Update</c>'s gate chain BEFORE
/// HighlightSelectedPlacementHex (the found root cause: TimeManager left paused by a
/// camera-follow transition, see <see cref="CameraArrivalGuard"/>). This prefix logs a
/// change-deduped snapshot of EVERY gate on the way to the hover-arming call
/// (WorldspaceStarHexDisplay.cs:408-436):
///
///   :410 <c>m_HexDisplayToggledOff</c> → toggledOff
///   :425 <c>CurrentGameState == Scenario</c> → gameState, <c>!TimeManager.IsPaused</c>
///        → paused (+ Main.s_NumberOfPausesRegistered → pause3d, to distinguish a
///        CameraTargetFocalFollowController pause, which does NOT touch the Main
///        refcount, from a leaked Pause3DWorld)
///   :427 <c>PointingAtANewTile()</c> → hover (the same <c>Interactable()</c> the game
///        will compare: null names the :3815 UI gate via overUI, a tile names success)
///   :428 <c>!m_AllHexesHighlighted && !LockView</c> → allHexes / lockView
///   :430 <c>m_RefreshedCurrentState</c> → refreshed
///   :433 <c>m_currentDisplayState == CharacterPlacement</c> → display
///
/// Ladder semantics on hardware: NO "Update gates:" lines while "[Placement]
/// TileHandler click:" lines appear ⇒ WSHD.Update itself is not running (:410 fails
/// or the component is inactive). Otherwise the first snapshot with a blocking value
/// names the gate. Silent outside WaitingForCardSelection; the <c>Interactable()</c>
/// probe (one raycast) runs only while display==CharacterPlacement. Remove together
/// with the other two diagnostics after HMD confirmation.
/// </summary>
[HarmonyPatch(typeof(WorldspaceStarHexDisplay), nameof(WorldspaceStarHexDisplay.Update))]
internal static class Placement_UpdateGate_Diagnostics
{
    private static (WorldspaceStarHexDisplay.WorldSpaceStarDisplayState display, bool toggledOff,
        EGameState gameState, bool paused, int pause3d, bool allHexes, bool lockView,
        bool refreshed, CInteractable? hover, bool overUI, BoardPick.PickSource source)? _last;

    private static void Prefix(WorldspaceStarHexDisplay __instance)
    {
        Choreographer? choreo = Choreographer.s_Choreographer;
        if (choreo == null || choreo.m_WaitState == null
            || choreo.m_WaitState.m_State != Choreographer.ChoreographerStateType.WaitingForCardSelection)
            return;

        WorldspaceStarHexDisplay.WorldSpaceStarDisplayState display = __instance.CurrentDisplayState;
        CInteractable? hover = null;
        bool overUI = false;
        if (display == WorldspaceStarHexDisplay.WorldSpaceStarDisplayState.CharacterPlacement)
        {
            overUI = UIManager.IsPointerOverUI;
            hover = __instance.Interactable(); // frame-memoized VR pick → identical to the game's own :427 probe
        }

        var snapshot = (display, __instance.m_HexDisplayToggledOff,
            SaveData.Instance.Global.CurrentGameState, TimeManager.IsPaused,
            Main.s_NumberOfPausesRegistered, __instance.m_AllHexesHighlighted,
            __instance.LockView, __instance.m_RefreshedCurrentState, hover, overUI,
            BoardPick.Source);
        if (_last.HasValue && _last.Value == snapshot)
            return;
        _last = snapshot;

        VRLog.Info("Board", "[Placement] Update gates: " +
                            $"display={snapshot.display}, toggledOff={snapshot.Item2}, " +
                            $"gameState={snapshot.Item3}, paused={snapshot.Item4}, " +
                            $"pause3d={snapshot.Item5}, allHexes={snapshot.Item6}, " +
                            $"lockView={snapshot.Item7}, refreshed={snapshot.Item8}, " +
                            $"hover={HoverName(hover)}, overUI={snapshot.overUI}, pick={snapshot.Source}.");
    }

    private static string HoverName(CInteractable? hover)
    {
        if (hover == null)
            return "null";
        CClientTile? tile = hover.GetComponent<TileBehaviour>()?.m_ClientTile;
        return tile != null ? Placement_Hover_Diagnostics.TileName(tile) : hover.GetType().Name;
    }
}

/// <summary>
/// CLICK stage: <c>Choreographer.TileHandler</c>'s placement branch commits only when
/// <c>clientTile == Waypoint.s_PlacementTile</c> and an actor is selected on the
/// initiative track (decompiled Choreographer.cs:1841 → PlaceActorAtRoundStart
/// :1846-1866). The prefix logs every click that reaches TileHandler during
/// WaitingForCardSelection with all three operands, so a dead second click is
/// attributable from the log alone (clicked tile vs armed tile vs selection).
/// </summary>
[HarmonyPatch(typeof(Choreographer), nameof(Choreographer.TileHandler))]
internal static class Placement_Click_Diagnostics
{
    private static void Prefix(Choreographer __instance, CClientTile clientTile)
    {
        if (__instance.m_WaitState == null
            || __instance.m_WaitState.m_State != Choreographer.ChoreographerStateType.WaitingForCardSelection)
            return;
        bool actorSelected = InitiativeTrack.Instance != null
                             && InitiativeTrack.Instance.SelectedActor() != null;
        VRLog.Info("Board", "[Placement] TileHandler click: tile=" +
                            $"{Placement_Hover_Diagnostics.TileName(clientTile)}, armed=" +
                            $"{Placement_Hover_Diagnostics.TileName(Waypoint.s_PlacementTile)}, " +
                            $"actorSelected={actorSelected} → will " +
                            (actorSelected && clientTile != null && clientTile == Waypoint.s_PlacementTile
                                ? "PLACE." : "NOT place."));
    }
}
