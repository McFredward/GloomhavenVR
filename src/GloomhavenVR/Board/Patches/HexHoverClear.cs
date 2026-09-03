using System;
using GloomhavenVR.Core;
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
/// FIX: a Postfix on <c>WorldspaceStarHexDisplay.Update()</c> — so it runs AFTER the tail
/// re-activation at :487 and gets the last word — deactivates a stale cursor star while the mod
/// is in a live scenario. WHETHER the pointer is on a hex at all is judged exactly the way the
/// game judges it (<see cref="ResolvePointedHex"/>, mirroring WSHD.cs:3212); WHEN a lit star is
/// legitimate is THE ONE RULE below.
///
/// TRAP — do NOT gate this on <c>Active &amp;&amp; !HasHit</c> again (the superseded gate). With no
/// VR pick at all (<c>source==None</c>) <see cref="BoardPick.Active"/> is false, so the postfix
/// NO-OPPED and Update's tail kept re-lighting a star parented to a stale tile; the gate is
/// <see cref="BoardPick.InScenario"/>, which holds even when no hand produced a pick. It also
/// missed the figure hover: a laser on a mini HAS a hit but resolves to no TileBehaviour, so the
/// game's null branch cleared its tile ref and left the previous hex's star lit.
///
/// UNDISCOVERED-ROOM LEAK (user hardware report, ModBuild 90 — "pointing at a tile in an
/// undiscovered room lights up the tile I last hovered in the discovered room"). The gate
/// above was still too permissive, because an UNDISCOVERED hex is a perfectly ordinary
/// <c>TileBehaviour</c> with a live <c>m_ClientTile</c> — the pick "is on a hex", so this
/// postfix early-returned and the game got the last word. And the game's last word is a
/// stale star:
/// <code>
///   // WSHD.cs:3220 — pointed tile is in an unrevealed map…
///   if (tile.m_Tile.m_HexMap != null &amp;&amp; !tile.m_Tile.m_HexMap.Revealed
///       &amp;&amp; (tile.m_Tile.m_Hex2Map == null || !tile.m_Tile.m_Hex2Map.Revealed))
///   {
///       if (!CanCursorHighlightUnrevealedTile(clientTile))   // :3330 — only dungeon EXIT tiles
///       {
///           s_CursorHighlightedTile = null;
///           s_CursorHighlightedStar.gameObject.SetActive(false);   // hidden, NOT moved
///       }
///       else if (…) { ShowTooltipForTile(clientTile); }            // exit tile: star not moved either
///       return;                                                    // ← never places a star
///   }
/// </code>
/// So an undiscovered hex NEVER gets a star; the pooled star stays parked where the last
/// <c>SetStarPos</c> (WSHD.cs:3000) put it — the last hex hovered in the DISCOVERED room — and
/// Update's unconditional tail (:487-489) re-activates it the same frame. Exactly the report:
/// point at the dark, the discovered room's last-hovered tile lights up.
///
/// THE ONE RULE (this class, since the ModBuild 90 fix): the cursor star may stay lit only
/// while the game has actually placed it on the hex the VR pointer is resolving to RIGHT
/// NOW; every other outcome CLEARS it — a miss never falls back to a remembered hex. That is
/// three conditions, checked in order by <see cref="Run"/> (there is no separate predicate
/// method — each condition owns its own ClearStar reason string):
/// 1. the pointer resolves to a live hex at all (collider → <c>TileBehaviour.m_ClientTile</c>),
/// 2. that hex is REVEALED (the game's own :3220 test, inverted) — an undiscovered hex is a
///    hex the game refuses to star, so any lit star is by definition somewhere else,
/// 3. <c>s_CursorHighlightedTile</c> — the game's own record of which hex the star was moved
///    to (set in lockstep with <c>SetStarPos</c> at :3253-3258, so it IS where the star sits)
///    — equals that hex. This catches every remaining "the game did not (re)place the star
///    this frame" case in one predicate: <c>Interactable()</c> gated off by
///    <c>UIManager.IsPointerOverUI</c>, <c>DisplayCursorHoverStar</c> skipped entirely
///    (<c>m_AllHexesHighlighted</c> / <c>LockView</c> / paused / WaitingForTileSelected), or
///    the dungeon-EXIT unrevealed branch that sets the tile ref without moving the star.
/// Both pointers are covered by construction: <see cref="BoardPick"/> is the single VR pick
/// and it arbitrates fingertip (Near) over laser (Far), so whichever pointer owns the frame
/// is the one this rule is applied to; a fingertip that leaves <c>[Board] TouchRange</c>
/// while the laser has no hit collapses to <c>HasHit == false</c> → cleared.
///
/// TOOLTIP HALF (unchanged on purpose): <see cref="HideStaleTooltips"/> still runs ONLY when
/// the pointer resolves to no hex at all. When the pointer IS on a hex the GAME owns the
/// tooltip for it — it hides it itself for an undiscovered hex (:3227) and deliberately SHOWS
/// it for an undiscovered dungeon-exit tile (:3236). Hiding it there would fight the game.
///
/// SCOPE / SAFETY:
/// - <c>s_CursorHighlightedStar</c> is EXCLUSIVELY the cursor-hover indicator
///   (HexMode.Cursor). Placement / movement / attack / ability TargetSelection highlights
///   live in SEPARATE star dictionaries (s_PlacementStars, s_AttackStars, s_AbilityStars,
///   s_PossibleMoveStars, … — verified WSHD.cs:113-141), so real multi-frame targeting
///   highlighting is untouched — only the stale single hover star is killed.
/// - Only runs while <see cref="BoardPick.InScenario"/> (never in Menu2D, never outside a
///   scenario Controller), so vanilla behaviour outside a live scenario is intact.
/// - We only ever DEACTIVATE the cursor star; nothing here activates one, and when it IS on the
///   pointed hex we early-return and leave the game's fresh star alone. Re-hovering a valid hex
///   restores it through the game's own <c>DisplayCursorHoverStar</c> + Update tail.
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
    private const string Scope = "HexHover";

    private static readonly AccessTools.FieldRef<WorldspaceStarHexDisplay, HexSelect_Control?> CursorStarRef =
        AccessTools.FieldRefAccess<WorldspaceStarHexDisplay, HexSelect_Control?>("s_CursorHighlightedStar");

    /// <summary>
    /// The game's own record of which hex the cursor star was last MOVED to — written in
    /// lockstep with <c>SetStarPos</c> (WSHD.cs:3253-3258), so it is where the star physically
    /// sits. Comparing it with the hex under the VR pointer is condition 3 of the one rule.
    /// </summary>
    private static readonly AccessTools.FieldRef<WorldspaceStarHexDisplay, CClientTile?> CursorTileRef =
        AccessTools.FieldRefAccess<WorldspaceStarHexDisplay, CClientTile?>("s_CursorHighlightedTile");

    // ---- change-gated diagnostics (never per-frame) ------------------------------------------

    /// <summary>Hex the highlight was last logged as SHOWING (null = last logged state was "cleared").</summary>
    private static CClientTile? _loggedShown;

    /// <summary>Reason last logged for a clear; null while the last logged state was "showing".</summary>
    private static string? _loggedMiss;

    private static int _errorLogs;

    private static void Postfix(WorldspaceStarHexDisplay __instance)
    {
        // WorldUI lesson: an unguarded NRE in a per-frame game path starves everything
        // downstream of it. The whole body is guarded; errors are logged a few times, then quiet.
        try
        {
            Run(__instance);
        }
        catch (Exception e)
        {
            if (_errorLogs < 3)
            {
                _errorLogs++;
                VRLog.Error(Scope, $"postfix failed: {e}");
            }
        }
    }

    private static void Run(WorldspaceStarHexDisplay display)
    {
        // Outside a live scenario (Menu2D/ModalUI, no Controller) leave vanilla behaviour alone.
        if (!BoardPick.InScenario)
        {
            _loggedShown = null;
            _loggedMiss = null;
            return;
        }

        CClientTile? pointed = ResolvePointedHex(out string miss);

        // No hex under the pointer at all: clear the star AND the hover hints — with nothing
        // hovered, a visible "2 Gold" / door / quest-item hint is stale too (see class doc).
        if (pointed == null)
        {
            ClearStar(display, miss);
            HideStaleTooltips();
            return;
        }

        // The pointer IS on a live hex, so the GAME owns the tooltip for it from here on
        // (it hides it for an undiscovered hex, shows it for an undiscovered exit tile).

        // Undiscovered room: the game refuses to place a star on that hex (WSHD.cs:3220-3238)
        // and Update's tail (:487) re-lights the one still parked on the last valid hex.
        if (!IsRevealed(pointed))
        {
            ClearStar(display, "the hex under the pointer is in an UNDISCOVERED room — the game " +
                               "never stars it (WSHD.DisplayCursorHoverStar:3220), so any lit star " +
                               "is the last hex hovered in a discovered room");
            return;
        }

        // The star must actually sit on THAT hex. Anything else (game skipped
        // DisplayCursorHoverStar this frame, Interactable() gated off, exit-tile branch) means
        // the lit star is a memory, not a hover.
        if (!ReferenceEquals(CursorTileRef(display), pointed))
        {
            ClearStar(display, "the game's cursor star is parked on a different hex than the " +
                               "pointer (s_CursorHighlightedTile != the picked hex — the game did " +
                               "not place a star for this hex this frame)");
            return;
        }

        LogShown(pointed);
    }

    /// <summary>Deactivate the pooled cursor star (never activates anything) and log the change once.</summary>
    private static void ClearStar(WorldspaceStarHexDisplay display, string reason)
    {
        HexSelect_Control? star = CursorStarRef(display);
        if (star != null && star.gameObject.activeSelf)
            star.gameObject.SetActive(false);
        LogCleared(reason);
    }

    private static void LogShown(CClientTile hex)
    {
        if (ReferenceEquals(_loggedShown, hex))
            return;
        _loggedShown = hex;
        _loggedMiss = null;
        VRLog.Info(Scope, $"highlight SHOWS hex {Describe(hex)} (pointer source={BoardPick.Source}).");
    }

    private static void LogCleared(string reason)
    {
        // Change-gated on BOTH the previous state and the reason: sweeping the laser across a
        // dark room logs once, not once per frame and not once per hex.
        if (_loggedShown == null && _loggedMiss == reason)
            return;
        _loggedShown = null;
        _loggedMiss = reason;
        VRLog.Info(Scope, $"highlight CLEARED — {reason}.");
    }

    private static string Describe(CClientTile hex)
    {
        try
        {
            if (hex.m_Tile != null)
                return $"[{hex.m_Tile.m_ArrayIndex.X},{hex.m_Tile.m_ArrayIndex.Y}]";
            return hex.m_GameObject != null ? $"'{hex.m_GameObject.name}'" : "(unnamed)";
        }
        catch
        {
            return "(unnamed)";
        }
    }

    /// <summary>
    /// Hide the tile-hover info panels when the VR pick is not on a hex — the exact pair
    /// <c>ShowTooltipForTile</c> hides before showing fresh content (WSHD.cs:3574-3575).
    /// Guarded to a strict no-op when nothing is visible/current, so calling it every
    /// not-on-a-hex frame is cheap and can never fight a live hover.
    /// </summary>
    private static void HideStaleTooltips()
    {
        // A HELD PROP'S INFO CARD IS NOT A STALE HOVER (ModBuild 340). User, 2026-09-02, on the
        // props: "Die Info die da sein sollte (wie bei den Figuren auch) ist nicht sichtbar."
        // UITextInfoPanel is the window the GAME itself uses for a prop (WSHD.cs:3607), so it is
        // the window GrabbableProp raises while a prop is in the hand — and holding a grabbable
        // turns that hand's ray OFF, which puts the pick permanently not-on-a-hex and made this
        // method fire every frame of the hold. Both writers were correct about their own premise;
        // only one of them knows a prop is being held, so the test belongs here.
        //
        // RE-VERIFIED FOR ModBuild 366, WHICH CHANGED WHICH WINDOW THE HELD CARD IS IN. A prop with
        // a rich card — a trap, hazardous terrain, difficult terrain, a carryable quest item — now
        // raises UIPropInfoPanel instead of UITextInfoPanel so it keeps its effect rows
        // ("Ich will, dass wenn man etwas in die Hand nimmt immer die detaillierteste Info
        // angezeigt wird inkl. aller effekte"). This early return already covers that: it sits
        // ABOVE both blocks and returns before either panel is touched, so the guard is on the
        // METHOD and not on one of the two windows. Nothing to change here — but it had to be
        // checked, because a guard that had been written per-panel would have silently kept hiding
        // the better card four times a second.
        if (Board.FigureGrab.HeldProps.Count > 0)
            return;

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

    /// <summary>
    /// The hex the VR pointer (fingertip OR laser — <see cref="BoardPick"/> arbitrates) resolves
    /// to this frame, or null with a human reason. Mirrors the game's own hex-validity test
    /// (<c>TileBehaviour</c> with a live <c>m_ClientTile</c>, WSHD.cs:3212) so we can never
    /// disagree with it about WHETHER a hex is under the pointer — only about whether the star
    /// belongs on it.
    /// </summary>
    private static CClientTile? ResolvePointedHex(out string miss)
    {
        if (!BoardPick.Active)
        {
            miss = "no VR pick this frame (ray untracked / mode policy — source=None)";
            return null;
        }
        if (!BoardPick.HasHit)
        {
            miss = "the pointer hits nothing (laser off the board, or the fingertip left " +
                   "[Board] TouchRange)";
            return null;
        }
        Collider? collider = BoardPick.HitCollider;
        if (collider == null)
        {
            miss = "the picked collider vanished this frame";
            return null;
        }
        TileBehaviour? tile = collider.GetComponentInParent<TileBehaviour>();
        if (tile == null)
        {
            miss = "the pointer is on a non-hex object (figure, prop or scenery — no TileBehaviour)";
            return null;
        }
        if (tile.m_ClientTile == null)
        {
            miss = "the pointed TileBehaviour carries no live m_ClientTile";
            return null;
        }
        miss = string.Empty;
        return tile.m_ClientTile;
    }

    /// <summary>
    /// The game's revealed test for a hex, inverted from <c>DisplayCursorHoverStar</c>
    /// (WSHD.cs:3220): a tile counts as UNREVEALED only when it has a hex map that is not
    /// revealed AND no revealed second map. Tiles without a map (level-editor / loose geometry)
    /// count as revealed, exactly as the game's condition does.
    /// </summary>
    private static bool IsRevealed(CClientTile hex)
    {
        var tile = hex.m_Tile;
        if (tile == null || tile.m_HexMap == null)
            return true;
        if (tile.m_HexMap.Revealed)
            return true;
        return tile.m_Hex2Map != null && tile.m_Hex2Map.Revealed;
    }
}
