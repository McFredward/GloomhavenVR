using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// ModalFallback part 14 (THE SHARED QUEST INFO WINDOW ON THE TABLE'S RIGHT CORNER WHILE THE PRIVATE
// QUESTS ARE BEING CHOSEN). A NEW part file, for the same reason part 13 is one: the shared-anchor
// arithmetic in ArcSeats.cs is edited by parallel lanes off the same base, so it receives only the
// four hooks named at the bottom of this comment and nothing else.
//
// ==========================================================================================
// WHY THIS EXISTS — USER REPORT 2026-09-07
// ==========================================================================================
//
// Verbatim: "1) Das Multiplayer-Questinfo Fenster soll an der rechten Ecke des Tisches Spawnen
// wenn man die privaten Quests auswählen muss. Aktuell spawnt es davor. Mach bei diesem Fenster
// explizit in der Situation eine Außnahme das es an der rechten Ecke spawnt."
//
// ------------------------------------------------------------------------------------------
// ROOT CAUSE: THE CORNER RULE SHIPPED ON THE LOCAL PATH AND THE MULTIPLAYER PATH NEVER ASKS IT.
// ------------------------------------------------------------------------------------------
//
// ModBuild 412's answer to the previous round of this same report is
// ModalFallback.13.QuestSelectionSeat.cs: `QuestPopupTakesRightCorner` makes the quest info popup
// the RIGHT corner window, and it is read by `ArcSeats.TryCornerWindowSide` — the LOCAL corner
// machinery, reached from `ComputeHmdPose`'s arc-seat path.
//
// A window that is SHARED never reaches that path. `TrySharedWindowAnchor` runs FIRST and returns
// a finished pose, and for this window it always answers: the quest popup carries
// `SharedWindowKind.QuestConfirm`, which owns HOME 1 of the map table by identity, on the ModBuild
// 250 half-ring about the table centre. So in single player the popup is on the right corner and in
// MULTIPLAYER — the only case the user's sentence names — it is on the ring. That is the whole
// defect, and it is why the report says "Multiplayer-Questinfo Fenster".
//
// THE PROOF, host Player.log of the tested ModBuild 476, at the exact moment he describes:
//
//   :28966  UIWindow SHOWN: 'UI Loadout Window'                     ← the loadout screen opens
//   :29959  UIWindow SHOWN: 'UI Quest Popup' (ID QuestPopup)
//   :29974  UIWindow SHOWN: 'UI Battle Goal Picker Window'          ← "die privaten Quests"
//   :30012  SHARED WINDOW ANCHOR APPLIED (SPAWN) — 'UI Quest Popup' … owns HOME 1 …
//               pos (0.6511,1.1543,0.4648) m, LATERAL STEP 0.329 m, SEATED AT DEPTH 0.729 m
//   :30057  MAP ROOM CORNER RE-SEATED: 'New Party display' (LEFT corner window)
//
// The table measures half-width 0.776 m and half-depth 1.150 m about the parchment centre (same
// line). The popup's slot is 0.329 m to one side and 0.729 m out — i.e. hanging over the middle of
// the table's far half, not on a corner: "aktuell spawnt es davor", exactly. And the lateral step is
// POSITIVE, which on this table is the reader's LEFT — the same side the character screen's corner
// is on (:30057), which is why the pair fight for the same space during the selection.
//
// The peer log has the identical shape (:15596 loadout, :16764 popup, :16779 picker), so this is not
// one machine's ordering.
//
// ------------------------------------------------------------------------------------------
// THE PREDICATE, AND WHY IT IS THIS ONE AND NOT THE NEARBY ONE THAT MERELY CORRELATES.
// ------------------------------------------------------------------------------------------
//
// THE SITUATION HE NAMES IS THE LOADOUT SCREEN. "Die privaten Quests" is this project's own
// established name for the BATTLE GOALS — see SubViewRevival.cs, which quotes his 2026-08-23 report
// ("Sobald die private Quest für die Charactere ausgewählt wird …") about the very window
// 'UI Battle Goal Picker Window'. The game opens that picker from exactly one place:
// `UILoadoutManager.EnableLoadoutInteraction` (decompiled UILoadoutManager.cs:332-341) calls
// `Singleton<UIQuestPopupManager>.Instance.ShowQuest(_quest)` and then
// `NewPartyDisplayUI.PartyDisplay.Show(this)` with `AutoselectCharacter` on OnShown, which opens the
// battle-goal panel. So the quest popup and the private-quest selection are raised by ONE call, and
// the predicate for "he is choosing his private quest" is "the loadout screen is open".
//
// SO THE TERM IS `StoryComposite.LoadoutScreenOpen` — `UILoadoutManager.IsOpen`, i.e. that screen's
// own `UIWindow.IsOpen`, which is the ModBuild 233 level this mod already relies on elsewhere.
//
// AND NOT `SubViewRevival.SelectionHoldsRelease`'s terms, which are the obvious neighbours and are
// WRONG HERE FOR A SHARED WINDOW. Those read `NewPartyDisplayUI.PartyDisplay.ActiveDisplay ==
// BATTLE_GOALS` and `BattleGoalWindow.IsOpen` — the party display's ACTIVE TAB and the picker's own
// open flag. Both are things ONE player changes for himself, at his own pace, and the tested log
// shows the gap they open: the picker was open from :29974 to :30834 while the loadout screen never
// closed, and the popup's THIRD spawn at :31286 falls after the picker closed and inside the same
// loadout. A shared window whose seat is a function of those terms is seated in one place on the
// machine of the player who is still picking and in another on the machine of the player who has
// finished — which is the 1:1 breach this project's standing ruling forbids, and it is invisible to
// the player who caused it. `UILoadoutManager.IsOpen` has no such per-player term: online the
// loadout is ENTERED together (`EnterLoadout` → `MPSyncState` locks every client behind
// GUI_WAIT_PLAYERS_CONFIRM_TIP and only the all-player ready-up calls `EnableLoadoutInteraction`,
// UILoadoutManager.cs:287-316) and LEFT together.
//
// IT IS ALSO NARROW, which is what he asked for. The tested log's FIRST quest popup (:23584, the
// map-location preview) stands 5382 lines before the loadout ever opens, so that appearance keeps
// today's ring seat untouched — the exception is only ever the loadout's popup.
//
// ------------------------------------------------------------------------------------------
// WHAT "THE RIGHT CORNER" IS, AS A POSE, AND WHY IT IS NOT THE LOCAL CORNER RULE'S ONE.
// ------------------------------------------------------------------------------------------
//
// `ArcSeats.TryTableFarCornerWorld` — the corner every LOCAL corner window is seated at — takes its
// direction from `MapRoomDriver.SeatFloor`, THIS CLIENT'S OWN SEAT. That is correct for a local
// window and fatal for a shared one: two players at different seats would compute two corners.
//
// So the corner here is built in the SHARED frame the ring is already built in, from the same two
// vectors and nothing else:
//
//   * `ahead`      — the host's one published gaze byte (record 20), or the fixed table axis when no
//                    decision has been received. It is not computed on this client.
//   * `right`      — `Vector3.Cross(Vector3.up, ahead)`, which is Unity's own right-hand of a
//                    forward vector and the same expression `TryTableFarCornerWorld` uses for
//                    "+ = the player's right". It is derived rather than taken as `-lateralDir`
//                    because the two are equal only on the shortAxisIsX branch.
//   * the four table corners `(±halfXm, ±halfZm)`, which come off the parchment bounds by the
//     surveyed slab ratios — the same numbers the ring's own line prints.
//
// THE CORNER IS `argmax over the four of dot(rel, ahead + right)`. That is the corner in the
// forward-and-right diagonal, and it reduces to the shipped answer exactly when the direction is a
// table axis: on the ModBuild 476 log the local rule's RIGHT far corner is (153.79,-0.02,-227.65) wu
// = frame-local (+0.776, −1.150) m, and so is this. It is written as a diagonal rather than as the
// local rule's `reach × 1000 + lateral` score because that score only separates the two far corners
// when their reach TIES, which an off-axis shared gaze breaks (at the log's own 78.75° the two far
// corners differ by 0.45 m of reach and the score would hand BOTH sides the same corner).
//
// THE WINDOW'S CENTRE GOES ON THE CORNER — the ModBuild 412 ruling for the RIGHT corner window ("the
// quest log its centre, the character screen its left edge"). The shared path has no drawn-content
// measurement at this point and does not need one here: the popup's own HIT RECT on the tested log
// is 553x667 px at (−20,4) in a 512x1021 px frame, i.e. its drawn centre is 20 px — 21 mm — off its
// host centre, which is below the resolution of anything this seat decides.
//
// NOTHING ELSE MOVES. The HEIGHT is still the room's one bar height (ModBuild 251, "die Höhe soll
// beim Spawn am Besten bei allen Fenster gleich sein gemessen am Greifbalken"). The FACING is still
// `LookRotation(ahead)` — the one yaw every shared slot takes, yaw-only, and NOT the local corner
// rule's "faces the spawn point", which is a per-client rotation and is refused for a shared window
// by the standing ruling ("Da es ein Fenster für alle ist, sollen diese Fenster nach dem Greifen
// auch nicht die Orientierung nach dem Spieler ändern"). The DRAG still wins: this is the initial
// spawn pose and `SharedAnchorSpent` retires it the moment anybody moves the window.
//
// THE DECISION IS LATCHED PER APPEARANCE, and that is not caution for its own sake. The anchor is
// computed TWICE — once at the SPAWN and once at the pre-reveal RE-PLACE on the fitted geometry
// (:30012 and :30073 in the log above). If the loadout closed between the two, the window would jump
// from the corner to the ring in front of the player's eyes, which is the "popping is unacceptable"
// ruling. So the first call decides for both.
//
// "PER APPEARANCE" IS THE CONVERTED PANEL AND EXPLICITLY NOT THE GAME WINDOW. The game reuses ONE
// 'UI Quest Popup' UIWindow for every open (:23584, :29959 and :31286 are the same object), so a
// latch keyed on its instance id would have carried the FIRST open's verdict — no loadout, ring seat
// — into the loadout's open, and this whole exception would have been dead code that logged a
// plausible refusal. The conversion builds a NEW panel per float (`Converted 'Modal_UI Quest Popup'`
// at :23615 AND again at :30003) and each panel takes exactly one SPAWN and one RE-PLACE, which is
// the scope wanted. The latch also dies with the room.
//
// THE ONE GREP THAT DECIDES THE NEXT ROUND: `PRIVATE QUEST CORNER` on the SHARED WINDOW ANCHOR
// APPLIED line. Its clause carries the predicate's verdict, the corner it chose in frame-local
// metres, and the ring seat it replaced — so "did the exception fire, and did both clients fire it"
// is one grep over two logs and not a comparison of poses.
//
// THE HOOKS IN ArcSeats.cs, and there are exactly four:
//   1. `ResetSharedQuestCornerSeats(reason)` at the top of `ResetSharedAnchors`, before its early
//      return (the roster clear beside it is there for the same reason).
//   2. `TrySharedAnchorOnTable` takes the arriving `ConvertedPanel?` as its first parameter — the
//      latch's key, and the one term this file could not reach from the window alone. Its single
//      caller `TrySharedWindowAnchor` already holds it.
//   3. `TryPrivateQuestCornerSeat(...)` in `TrySharedAnchorOnTable`, which chooses `localPos`.
//   4. `TakeQuestCornerClause()`, appended to that method's falsifier line.

internal static partial class ModalFallback
{
    /// <summary>The converted panel the corner clause has already decided for, and what it decided.
    ///
    /// <para><b>THE KEY IS THE PANEL AND NOT THE GAME WINDOW, and that is the whole correctness of
    /// the latch.</b> The game reuses ONE <c>UI Quest Popup</c> <c>UIWindow</c> for every open — on
    /// the ModBuild 476 host log the same window is shown at :23584, :29959 and :31286 — so a latch
    /// keyed on its instance id would have carried the FIRST open's verdict (no loadout, ring seat)
    /// into the loadout's open and the exception would never have fired. The conversion, by
    /// contrast, builds a NEW panel per float (<c>Converted 'Modal_UI Quest Popup'</c> at :23615 AND
    /// again at :30003), and each panel receives exactly one SPAWN and one RE-PLACE (:23624/:23692,
    /// :30012/:30073). So the panel is precisely the scope "this one appearance of the window".</para>
    ///
    /// <para>One field rather than a set: at most one QuestConfirm window stands in the room at a
    /// time (the home is reserved by identity), so a second entry could never be read.</para></summary>
    private static ConvertedPanel? _questCornerPanel;

    /// <summary>What <see cref="_questCornerPanel"/> was decided to be.</summary>
    private static bool _questCornerTaken;

    /// <summary>The last corner clause built, so the anchor line can carry it without this method
    /// having to be called twice. Consumed by the caller on the same tick it is written.</summary>
    private static string? _questCornerClause;

    /// <summary>Forget every latched corner decision. Called from
    /// <c>ResetSharedAnchors</c> on the same teardown edges that release the floats.</summary>
    internal static void ResetSharedQuestCornerSeats(string reason)
    {
        if (_questCornerPanel == null)
            return;
        // HW-VERIFY
        VRLog.Note("WorldUI",
            $"PRIVATE QUEST CORNER RESET ({reason}) — the last quest info window in this room was "
            + $"latched {(_questCornerTaken ? "ON THE RIGHT CORNER" : "on its ordinary ring seat")}. "
            + "The latch exists so the SPAWN and the pre-reveal RE-PLACE of ONE appearance cannot be "
            + "decided by two different answers and make the window jump; it must not outlive the "
            + "room, or the next entry's popup would inherit a verdict about a loadout nobody in "
            + "that session opened.");
        _questCornerPanel = null;
        _questCornerTaken = false;
    }

    /// <summary>
    /// IS THE PLAYER BEING ASKED TO CHOOSE HIS PRIVATE QUEST RIGHT NOW? One term, and it is the
    /// game's own: the LOADOUT SCREEN is open (<c>UILoadoutManager.IsOpen</c>, via
    /// <see cref="StoryComposite.LoadoutScreenOpen"/>).
    ///
    /// <para>See the file header for why this and not the party display's active tab or the battle
    /// goal picker's own open flag: those are per-player and would seat a SHARED window in two
    /// places at once. This one is entered and left by the game's all-player ready-up.</para>
    /// </summary>
    private static bool PrivateQuestSelectionLive(out string why)
    {
        try
        {
            if (StoryComposite.LoadoutScreenOpen)
            {
                why = "the LOADOUT SCREEN is open (UILoadoutManager.IsOpen), which is the phase in "
                      + "which the game raises this popup and the battle-goal picker together from "
                      + "one call (UILoadoutManager.EnableLoadoutInteraction)";
                return true;
            }
            why = "the loadout screen is NOT open, so this popup is the map-location preview and "
                  + "keeps its ordinary ring seat";
            return false;
        }
        catch (System.Exception ex)
        {
            // A game-side accessor threw. Do NOT claim the corner: an instrument that cannot answer
            // must not move a shared window on a guess, and the ring seat is the shipped default.
            why = $"the loadout probe threw ({ex.GetType().Name}: {ex.Message}), so the ordinary "
                  + "ring seat is used — a shared pose is never moved on an unanswered question";
            return false;
        }
    }

    /// <summary>
    /// THE EXCEPTION. True with <paramref name="cornerRel"/> set to the table's RIGHT far corner in
    /// FRAME-LOCAL METRES (the horizontal pair only — the caller supplies the height, which the
    /// bar-height rule owns and which this clause does not touch).
    ///
    /// <para>False leaves the caller on the ModBuild 250 half-ring, bit for bit.</para>
    /// </summary>
    /// <param name="panel">The converted panel this appearance of the window was built into — the
    /// latch's key; see <see cref="_questCornerPanel"/> for why it is not the game window.</param>
    /// <param name="ahead">The ring's dead-ahead direction: the host's published gaze, or the fixed
    /// table axis. Horizontal and unit-length by construction, so the corner is yaw-only too.</param>
    /// <param name="halfXm">The table's half-extent along world X, frame-local metres.</param>
    /// <param name="halfZm">The table's half-extent along world Z, frame-local metres.</param>
    private static bool TryPrivateQuestCornerSeat(ConvertedPanel? panel, SharedWindowKind kind,
        Vector3 ahead, float halfXm, float halfZm, out Vector3 cornerRel)
    {
        cornerRel = Vector3.zero;
        _questCornerClause = null;
        if (kind != SharedWindowKind.QuestConfirm || panel == null)
            return false;

        bool live;
        string why;
        if (ReferenceEquals(_questCornerPanel, panel))
        {
            live = _questCornerTaken;
            why = live
                ? "LATCHED from this appearance's SPAWN — the seat may not change between the spawn "
                  + "and the pre-reveal re-place, or the window jumps"
                : "LATCHED from this appearance's SPAWN, which found no loadout";
        }
        else
        {
            live = PrivateQuestSelectionLive(out why);
            _questCornerPanel = panel;
            _questCornerTaken = live;
        }
        if (!live)
        {
            _questCornerClause = "PRIVATE QUEST CORNER: NOT TAKEN — " + why + ". ";
            return false;
        }

        // + = the reader's right, exactly as TryTableFarCornerWorld defines it. Derived rather than
        // taken as -lateralDir: the two agree on the shortAxisIsX branch and not on the other.
        Vector3 right = Vector3.Cross(Vector3.up, ahead);
        Vector3 diagonal = ahead + right;
        float best = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector3 rel = new Vector3((i & 1) == 0 ? -halfXm : halfXm, 0f,
                                      (i & 2) == 0 ? -halfZm : halfZm);
            float score = Vector3.Dot(rel, diagonal);
            if (score > best)
            {
                best = score;
                cornerRel = rel;
            }
        }
        _questCornerClause =
            "PRIVATE QUEST CORNER: TAKEN — " + why + ", so this window is seated on the table's "
            + $"RIGHT FAR CORNER at frame-local ({cornerRel.x:F4},{cornerRel.z:F4}) m instead of its "
            + "ring home (user request 2026-09-07: \"Das Multiplayer-Questinfo Fenster soll an der "
            + "rechten Ecke des Tisches Spawnen wenn man die privaten Quests auswählen muss. Aktuell "
            + "spawnt es davor.\"). THE CORNER IS THE argmax OVER THE FOUR TABLE CORNERS OF "
            + $"dot(rel, ahead + right) with right = cross(up, ahead) = "
            + $"({right.x:F3},{right.z:F3}) — every term a function of the parchment bounds and the "
            + "HOST'S published direction, so both clients compute this same corner and nothing "
            + "client-local reached it. THE ARC TERMS BELOW DESCRIBE THE RING SEAT THIS REPLACED, "
            + "not where the window went. The height, the facing and the drag rule are untouched. ";
        return true;
    }

    /// <summary>The corner clause for the anchor line, consumed once so a later window cannot
    /// inherit it.</summary>
    private static string TakeQuestCornerClause()
    {
        string? clause = _questCornerClause;
        _questCornerClause = null;
        return clause ?? string.Empty;
    }
}
