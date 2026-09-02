using HarmonyLib;

namespace GloomhavenVR.WorldUI.Patches;

// ---------------------------------------------------------------------------
// THE MOUSEOVER ANIMATION OF A MAP SYMBOL — the seam, and only the seam.
//
// USER, 2026-09-03: "Bitte deaktiviere die animationen für das mouseover im Kartenraum wenn ich
// über ein Kartensymbol hovere - an der Stelle möchte ich es nicht."
//
// Everything about WHAT is suppressed, WHAT is deliberately left running and WHY an undo beats a
// skip is written once, in WorldUI/MapRoom/MapIconHoverAnimation. This file is the attachment
// point: one postfix on the single method that drives the whole hover reaction,
// MapLocation.Highlight (decompiled GH.Runtime/MapLocation.cs:525-555).
//
// A POSTFIX, NOT A PREFIX. Highlight is also what raises the quest-preview card (UpdateMarkers,
// :554) and what moves the party token and redraws the route lines
// (MapChoreographer.OnMapLocationHighlight, :553). Skipping the method would take all of that with
// it. Letting it run and putting back two of its writes leaves every other consequence of a hover
// happening in the game's own order.
//
// SCOPED TO THE 3D MAP ROOM, and the gate is one property read: MapIconHoverAnimation.Suppressing
// is false unless MapRoomDriver.Active. On the flat 2D campaign map ([Rig] Vanilla2DMap on) and in
// every scene that has no map room this postfix costs one boolean and returns, so the game animates
// exactly as it always has.
//
// HOVER ONLY: `active && !isSelected`. A selection's own 1.2x is a different state and he named the
// mouseover.
//
// REVERSIBLE BY CONSTRUCTION, like MapLocationSelectorGate beside it: no state is held here, the
// dial is read live, and the frame the room stands down the game's hover animates again with
// nothing to unwind — the writes this makes are values the game itself computed one line earlier.
// ---------------------------------------------------------------------------

/// <summary>
/// Puts back the two ANIMATED writes <c>MapLocation.Highlight</c> makes when a hover starts, while
/// the 3D map room stands and <c>[MapRoom] HoverAnimation</c> is off. Registered by
/// <c>WorldUIModule</c>. See <see cref="MapRoom.MapIconHoverAnimation"/> for the diagnosis.
/// </summary>
[HarmonyPatch(typeof(MapLocation), "Highlight")]
internal static class MapLocationHoverAnimationGate
{
    private static void Postfix(MapLocation __instance, bool active, bool isSelected)
    {
        if (!MapRoom.MapRoomDriver.Active)
        {
            // The room is gone (or was never up): re-arm the once-per-engagement verdict so the
            // next map room says which way it went, and touch nothing.
            MapRoom.MapIconHoverAnimation.Rearm();
            return;
        }
        if (!active || isSelected || __instance == null)
            return;
        if (!MapRoom.MapIconHoverAnimation.Suppressing)
        {
            MapRoom.MapIconHoverAnimation.ReportKept(__instance);
            return;
        }
        MapRoom.MapIconHoverAnimation.UndoHoverAnimation(__instance);
    }
}
