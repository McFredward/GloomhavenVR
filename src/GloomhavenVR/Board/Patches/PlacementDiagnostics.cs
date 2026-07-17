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
