using HarmonyLib;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// Kills the STALE cursor-hover hex highlight when the VR laser is not on any hex.
///
/// ROOT CAUSE (decompiled GH.Runtime, WorldspaceStarHexDisplay, verified 2026-07-20):
/// the per-frame hover indicator is a single pooled star, <c>s_CursorHighlightedStar</c>,
/// managed by <c>DisplayCursorHoverStar()</c> (WSHD.cs:3200). When the pick resolves to
/// NO tile (<c>Interactable()</c> → our patched <c>MF.FindInteractableAtMousePosition</c>
/// returns null), its null branch (WSHD.cs:3212-3218) clears <c>s_CursorHighlightedTile</c>,
/// the stat panel and the hovered outlines — but NEVER deactivates the star itself.
/// Worse, the very tail of <c>Update()</c> (WSHD.cs:487-490) unconditionally
/// re-activates the star every frame:
/// <code>
///   if ((bool)s_CursorHighlightedStar)
///       s_CursorHighlightedStar?.gameObject.SetActive(_doShow);   // _doShow == true
/// </code>
/// So once any hex has been hovered, the star stays lit on that LAST hex forever until
/// a different hex is hovered — the star is parented to the tile and simply re-shown.
/// With a mouse this is masked (the pointer is almost always over some tile); in VR the
/// laser routinely leaves the board (or crosses a non-hex gap), leaving the highlight
/// visibly stuck on the last hex. This matches the reported bug exactly.
///
/// FIX: a Postfix on <c>WorldspaceStarHexDisplay.Update()</c> (so it runs AFTER the
/// tail re-activation at :487 and gets the last word) deactivates the cursor star
/// whenever the VR pick owns the pointer but hits no hex
/// (<see cref="BoardPick.Active"/> &amp;&amp; !<see cref="BoardPick.HasHit"/>). This covers
/// BOTH "laser off the board" and "over the board but off a hex" — the game's own
/// <c>DisplayCursorHoverStar()</c> null branch has already cleared everything else
/// (tile ref, stat panel, outlines), so only the star's SetActive(false) was missing.
///
/// SCOPE / SAFETY:
/// - <c>s_CursorHighlightedStar</c> is EXCLUSIVELY the cursor-hover indicator
///   (HexMode.Cursor). Placement / movement / TargetSelection highlights live in
///   separate star dictionaries, so real multi-frame target-selection highlighting is
///   untouched — only the stale hover star is killed.
/// - Only runs while <see cref="BoardPick.Active"/> (never in Menu2D/ModalUI, never
///   when VR isn't picking), so vanilla mouse behaviour outside a live VR pick is intact.
/// - Re-hover restores the star: the game's own path re-shows it on the next hex (the
///   null branch nulled <c>s_CursorHighlightedTile</c>, and Update's tail re-activates
///   it once a tile resolves again).
/// </summary>
[HarmonyPatch(typeof(WorldspaceStarHexDisplay), nameof(WorldspaceStarHexDisplay.Update))]
internal static class HexHoverClear
{
    private static readonly AccessTools.FieldRef<WorldspaceStarHexDisplay, HexSelect_Control?> CursorStarRef =
        AccessTools.FieldRefAccess<WorldspaceStarHexDisplay, HexSelect_Control?>("s_CursorHighlightedStar");

    private static void Postfix(WorldspaceStarHexDisplay __instance)
    {
        // Only intervene while the VR laser owns the pick and is on NO hex.
        // (Active is false in Menu2D/ModalUI or when VR isn't picking → vanilla.)
        if (!BoardPick.Active || BoardPick.HasHit)
            return;

        HexSelect_Control? star = CursorStarRef(__instance);
        if (star != null && star.gameObject.activeSelf)
            star.gameObject.SetActive(false);
    }
}
