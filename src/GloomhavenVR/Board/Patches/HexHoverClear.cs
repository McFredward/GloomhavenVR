using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

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
/// whenever the mod is in a live scenario and the VR laser is NOT genuinely resolving
/// to a hex. "Resolving to a hex" is judged the same way the game does
/// (<c>Interactable()?.GetComponent&lt;TileBehaviour&gt;()?.m_ClientTile != null</c>,
/// WSHD.cs:3212): a real hex hover means the VR pick both hit a collider AND that
/// collider carries a <c>TileBehaviour</c> with a live <c>m_ClientTile</c>. Anything
/// else — laser off the board (no hit), laser over a non-hex gap, laser on a FIGURE /
/// prop (a <c>CInteractableActor</c>, no TileBehaviour), OR no VR pick produced at all
/// (<c>source==None</c>, ray untracked / mode policy) — is "not on a hex", so any lit
/// cursor star is stale and gets killed.
///
/// Why this is stronger than the old <c>Active &amp;&amp; !HasHit</c> gate:
/// - <c>source==None</c> gap: with no VR pick, <see cref="BoardPick.Active"/> was false
///   and the postfix NO-OPPED, so the game's fallback (stale) mouse position could keep
///   a star lit / Update's tail could re-activate a star parented to a stale tile. We
///   now gate on <see cref="BoardPick.InScenario"/> instead, which is true even when no
///   hand produced a pick, so the stale star is cleared in that case too.
/// - figure hover: a laser on a mini hits a collider (HasHit) but resolves to NO
///   TileBehaviour, so the game's own null branch already cleared the tile ref but left
///   the previous hex's star lit; we now kill it.
///
/// SCOPE / SAFETY:
/// - <c>s_CursorHighlightedStar</c> is EXCLUSIVELY the cursor-hover indicator
///   (HexMode.Cursor). Placement / movement / attack / ability TargetSelection highlights
///   live in SEPARATE star dictionaries (s_PlacementStars, s_AttackStars, s_AbilityStars,
///   s_PossibleMoveStars, … — verified WSHD.cs:113-141), so real multi-frame targeting
///   highlighting is untouched — only the stale single hover star is killed.
/// - Only runs while <see cref="BoardPick.InScenario"/> (never in Menu2D/ModalUI, never
///   outside a scenario Controller), so vanilla behaviour outside a live scenario is intact.
/// - When the VR pick IS on a real hex we early-return, leaving the game's fresh star up;
///   re-hover therefore restores the star normally.
///
/// STALE HOVER HINT (second stale-hover bug, same family — verified in the decompiled
/// WSHD 2026-07-21): the info hint shown when hovering a loot tile ("2 Gold"), a closed
/// door, etc. is <c>UITextInfoPanel</c> (plus <c>UIPropInfoPanel</c> for carryable quest
/// items), shown by <c>ShowTooltipForTile</c> (WSHD.cs:3370) — which only runs when a NEW
/// valid tile is hovered (<c>s_CursorHighlightedTile != clientTile</c>, WSHD.cs:3251). The
/// hide lives INSIDE <c>ShowTooltipForTile</c> (WSHD.cs:3574-3575); the no-tile branch of
/// <c>DisplayCursorHoverStar</c> (WSHD.cs:3212-3218) clears the stat panel, the cursor tile
/// and the outlines but NEVER hides these two panels. So once shown, the hint can only be
/// replaced by hovering another tile — with a mouse that's near-immediate, but in VR the
/// laser can simply cease to exist (grabbing a figure turns the ray off: "ray OFF — hand is
/// holding a grabbable"), leaving the hint stuck on screen indefinitely. FIX: in the same
/// not-on-a-hex branch that kills the stale star, hide the two panels exactly the way
/// <c>ShowTooltipForTile</c> does (<c>UITextInfoPanel.Hide()</c> +
/// <c>UIPropInfoPanel.Hide(EPropType.QuestItem)</c>), guarded so it is a no-op when nothing
/// is showing:
/// - the text panel is only Hide()-den while its <c>UIWindow.IsVisible</c> (so a panel the
///   game temp-hid — card viewer open, gamepad tooltip toggle — is left untouched and we
///   never spam Hide every frame);
/// - the prop panel uses the game's own typed <c>Hide(QuestItem)</c>, which self-no-ops
///   unless a quest-item hint is the current content — trap / hazardous / difficult-terrain
///   tooltips keep their own <c>IHoverable OnCursorEnter/Exit</c> lifecycle (HoverRegisterer
///   fires OnCursorExit itself on a missed raycast, so those cannot go stale this way).
/// Re-hovering a tile re-shows the hint normally: the game's null branch already cleared
/// <c>s_CursorHighlightedTile</c>, so the next real hex hover re-runs ShowTooltipForTile.
/// UITextInfoPanel content is ONLY ever populated from ShowTooltipForTile (verified: every
/// other caller in GH.Runtime just hides / temp-hides it), so hiding it here can never
/// fight another feature's tooltip.
/// </summary>
[HarmonyPatch(typeof(WorldspaceStarHexDisplay), nameof(WorldspaceStarHexDisplay.Update))]
internal static class HexHoverClear
{
    private static readonly AccessTools.FieldRef<WorldspaceStarHexDisplay, HexSelect_Control?> CursorStarRef =
        AccessTools.FieldRefAccess<WorldspaceStarHexDisplay, HexSelect_Control?>("s_CursorHighlightedStar");

    private static void Postfix(WorldspaceStarHexDisplay __instance)
    {
        // Outside a live scenario (Menu2D/ModalUI, no Controller) leave vanilla behaviour alone.
        if (!BoardPick.InScenario)
            return;

        // Keep the game's fresh star ONLY while the VR pick is genuinely on a hex tile — mirror the
        // game's own hex-validity test (TileBehaviour with a live m_ClientTile). Every other case
        // (off-board, non-hex, on a figure, or no pick at all) means a lit cursor star is stale.
        if (PickIsOnHex())
            return;

        HexSelect_Control? star = CursorStarRef(__instance);
        if (star != null && star.gameObject.activeSelf)
            star.gameObject.SetActive(false);

        // Stale hover hint (see class doc): nothing is hovered, so any visible tile info hint
        // ("2 Gold" loot, closed door, quest item) is stale — hide it the way the game does.
        HideStaleTooltips();
    }

    /// <summary>
    /// Hide the tile-hover info panels when the VR pick is not on a hex — the exact pair
    /// <c>ShowTooltipForTile</c> hides before showing fresh content (WSHD.cs:3574-3575).
    /// Guarded to a strict no-op when nothing is visible/current, so calling it every
    /// not-on-a-hex frame is cheap and can never fight a live hover.
    /// </summary>
    private static void HideStaleTooltips()
    {
        if (Singleton<UITextInfoPanel>.IsInitialized)
        {
            UITextInfoPanel? text = Singleton<UITextInfoPanel>.Instance;
            if (text != null)
            {
                // Only while actually visible: skips panels the game temp-hid (card viewer /
                // tooltip toggle) and avoids per-frame Hide spam once cleared.
                UIWindow? window = text.GetComponent<UIWindow>();
                if (window != null && window.IsVisible)
                    text.Hide();
            }
        }

        if (Singleton<UIPropInfoPanel>.IsInitialized)
        {
            UIPropInfoPanel? prop = Singleton<UIPropInfoPanel>.Instance;
            // Typed hide self-no-ops unless the CURRENT content is the quest-item hint —
            // trap/terrain tooltips (own IHoverable exit lifecycle) are never touched.
            if (prop != null)
                prop.Hide(UIPropInfoPanel.EPropType.QuestItem);
        }
    }

    private static bool PickIsOnHex()
    {
        if (!BoardPick.Active || !BoardPick.HasHit)
            return false;
        Collider? collider = BoardPick.HitCollider;
        if (collider == null)
            return false;
        TileBehaviour? tile = collider.GetComponentInParent<TileBehaviour>();
        return tile != null && tile.m_ClientTile != null;
    }
}
