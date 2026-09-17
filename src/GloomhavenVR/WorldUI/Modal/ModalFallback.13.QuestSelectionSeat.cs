using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// ModalFallback part 13 (THE QUEST INFO WINDOW DURING QUEST SELECTION — side by side with the
// character screen, the table's width apart). A NEW part file so the corner-seat functions in
// ArcSeats.cs, which a parallel lane is editing off the same base, receive only the one-line
// hooks named below and nothing else. No static field declared here carries an initializer.
//
// ==========================================================================================
// WHY THIS EXISTS — USER REPORT 2026-09-03 (second round, after ModBuild 412)
// ==========================================================================================
//
// Verbatim: "Die Questinfo und Character-UI für die Questauswahl ist auch noch nicht perfekt.
// Ich will, dass sie NEBENEINANDER spawnen mit einem ähnlichen Abstand zueinander wie die
// Tischkanten, so dass ich direkt auswählen kann, ohne die Überlappung erst manuell verschieben
// zu müssen."
//
// WHAT THE ModBuild 412 LOG SHOWS AT THAT MOMENT (.planning/debug/LogOutput.log):
//
//   :5794  the character screen is corner-seated LEFT (booked 46°, its frame — nothing
//          measurable yet).
//   :5925  WINDOW HANDOVER: 'New Party display' has taken the place of 'UI Loadout Window' —
//          the character screen is MOVED 1656 mm / 50° (:5934 POSE LOCK) onto the loadout's
//          seat at world yaw 106°, and its registry entry becomes 8°±31°, i.e. its whole frame
//          at the loadout's 1.68 m.
//   :5968  'UI Quest Popup' spawns: "THE FIELD OF VIEW IS FULL — ... standing set [8°±31°
//          'New Party display' CORNER/LEFT]; together they ask for 87° of the 80° the field of
//          view supplies" ⇒ spread to -27°, DEPTH LEVEL 1, "MEASURED: it overlaps 'New Party
//          display' by 6° of the 24° it spans".
//   :5969  MAP ROOM WINDOW BESIDE: gap 0° (0 mm), OVERLAP 6°, "no chooser with a reserved
//          sub-view slot stands" — the ModBuild 412 sub-view forecast (part 9f) did NOT fire:
//          it keys on the fit's captured size, and the character screen's FIXED FIT for this
//          open first APPLIED at :5983, fifteen lines AFTER the popup had already claimed.
//
// So the popup was seated by the least-collision rule, in front of the character screen's left
// edge, and the user had to drag it aside before he could pick. The forecast of the previous
// round could not have helped even if it had fired: the field of view was full.
//
// THE DESIGN HE ASKS FOR IS THE ONE THE ROOM ALREADY HAS FOR ITS TWO PERMANENT WINDOWS. The
// character screen owns the LEFT far corner and the quest log the RIGHT one — "nebeneinander,
// Abstand wie die Tischkanten" IS the two far corners of the map table. During quest selection
// the game HIDES the quest log the moment the quest popup shows (:5298 → :5307 "MAP ROOM WINDOW
// SLOT RELEASED: 'Quest Log Manager'", and at :5953/:5968 the standing set holds no quest log at
// all), so its corner seat is empty exactly when the popup needs one.
//
// THE RULE, in two clauses:
//   1. While the quest log holds NO standing reservation, the quest info popup ('UI Quest
//      Popup', ID QuestPopup) IS the RIGHT corner window: TryCornerWindowSide answers RIGHT for
//      it and the existing corner machinery does the rest — drawn centre over the right far
//      corner, facing the spawn point, no seat search, no depth step, corner re-asserted after
//      the clamps, registry refreshed at the pre-reveal re-place (TryRefreshCornerClaim), and
//      the MAP ROOM CORNER falsifier when it was NOT corner-placed.
//   2. While the quest log IS standing on that corner, prefer the free gaze centre. Only when
//      the centre is occupied, try immediately inside the log toward the centre, with the
//      existing chooser clearance. ModBuild 527: the old unconditional adjacent preference
//      placed the quest popup 6–7° to the side even with a free centre (build-526 BESIDE logs).
//      The user's 2026-09-17 correction explicitly keeps that newly opened window centred.
//      Private quest selection with the hidden quest log still uses clause 1 unchanged.
//
// WHAT THIS DOES NOT DECIDE. Where the CHARACTER SCREEN stands during selection is the map-room
// lane's: the handover at :5925 moves it off its corner onto the loadout's seat, and while that
// stands the popup on the right corner can still overlap it — the BESIDE line measures that
// overlap in degrees and it must read 0° for the pair to be side by side.
//
// THE ONE GREP THAT DECIDES THE NEXT ROUND: `MAP ROOM WINDOW BESIDE: 'GloomhavenVR.Panel_Modal_
// UI Quest Popup'` — its clause names the character screen, the gap in mm to its drawn edge,
// OVERLAP 0°, and the QUEST SELECTION note saying which of the two clauses seated the popup.

internal static partial class ModalFallback
{
    /// <summary>The window the seat search is currently running for — set by the two search
    /// callers (<c>TryClaimArcSeat</c>, <c>TryReseatArcClaimOnDrawnContent</c>) right before they
    /// search, read by <see cref="TryQuestSelectionPreferredSeat"/>. Never cleared: both callers
    /// write it, so a stale value cannot be read by a search that did not set it.</summary>
    private static ConvertedPanel? _arcArrivingPanel;

    /// <summary>Which clause of the rule seated the last quest info popup, for the BESIDE
    /// clause. Empty when the last placement was not the popup's.</summary>
    private static string? _questSelectionNote;

    /// <summary>Is this window the quest info popup — the game's own ID, never a name.</summary>
    private static bool IsQuestPopupWindow(UIWindow? window) =>
        window != null && window.ID == UIWindowID.QuestPopup;

    /// <summary>The <c>UIWindow</c> a converted panel stands for, read the way
    /// <c>TryCornerWindowSide</c> reads it (the target's own component first, the registry second
    /// — the ModBuild 411 lesson that the registry is empty at the instant of the claim).</summary>
    private static UIWindow? WindowOfPanel(ConvertedPanel? panel)
    {
        if (panel == null)
            return null;
        UIWindow? window = panel.Target != null ? panel.Target.GetComponent<UIWindow>() : null;
        return window != null ? window : WindowForPanel(panel);
    }

    /// <summary>The registry slot of the QUEST LOG's standing reservation, or -1 when the quest
    /// log holds none (hidden by the game, or never floated). One pass over eight entries.</summary>
    private static int StandingQuestLogSlot()
    {
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            ConvertedPanel? p = _arcClaims[i].Panel;
            if (p == null || !p.IsAlive)
                continue;
            if (IsQuestLogWindow(WindowOfPanel(p)))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// CLAUSE 1: the quest info popup takes the RIGHT far corner while the quest log holds no
    /// standing reservation. Read by <c>TryCornerWindowSide</c> — the corner machinery's identity
    /// test — so every corner behaviour (pose re-assert, facing, corner refresh, the MAP ROOM
    /// CORNER falsifier, the one-shot re-seat) applies to the popup unchanged.
    /// </summary>
    private static bool QuestPopupTakesRightCorner(ConvertedPanel? panel)
    {
        try
        {
            if (!IsQuestPopupWindow(WindowOfPanel(panel)))
                return false;
            bool corner = StandingQuestLogSlot() < 0;
            _questSelectionNote = corner
                ? " QUEST SELECTION: the quest info popup IS the RIGHT corner window while the quest "
                  + "log holds no reservation (the game hides the quest log when this popup shows), "
                  + "so it stands on the right far corner, side by side with the character screen's "
                  + "left corner, the table's width apart (user request 2026-09-03: 'NEBENEINANDER "
                  + "... Abstand wie die Tischkanten')"
                : " QUEST SELECTION: the quest log is standing on the right corner, so the popup is "
                  + "offered the free gaze centre first, with the seat immediately inside the log "
                  + "used only when the centre is occupied";
            return corner;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// CLAUSE 2: prefer the gaze centre for a quest popup beside a standing quest log. If occupied,
    /// try inside the log at the chooser clearance. Both choices must be free of standing seats
    /// and fit inside the field of view (<paramref name="centreLimit"/>). A failed choice hands
    /// the search back unchanged; this method does not move an existing window.
    /// </summary>
    private static bool TryQuestSelectionPreferredSeat(float gazeYawDeg, float centreLimit,
        float halfAngle, out float offsetDeg)
    {
        offsetDeg = 0f;
        try
        {
            if (!IsQuestPopupWindow(WindowOfPanel(_arcArrivingPanel)))
                return false;
            int log = StandingQuestLogSlot();
            if (log < 0)
                return false;
            // A standing quest log does not justify moving a newly opened popup away from an
            // unobstructed centre. This check precedes the historical adjacent-seat preference;
            // returning false here would let the general map-channel pass choose another side.
            if (centreLimit >= -1e-3f && ArcSeatIsFree(gazeYawDeg, halfAngle))
            {
                _questSelectionNote = " QUEST SELECTION: the quest log is standing on the right "
                                      + "corner, and the popup keeps the free spawn gaze centre";
                return true;
            }
            float logOffset = Mathf.DeltaAngle(gazeYawDeg, _arcSeatWorldYaw[log]);
            float clearance = _arcClaims[log].HalfWidthDeg + halfAngle + NeighbourGapDegrees
                              + ChooserSlotExtraGapDegrees;
            // Inside = toward the gaze centre from the quest log's side; a quest log dead ahead
            // (offset 0) has no inside and the ordinary search decides.
            if (Mathf.Abs(logOffset) < 0.5f)
                return false;
            float candidate = logOffset > 0f ? logOffset - clearance : logOffset + clearance;
            if (Mathf.Abs(candidate) > centreLimit + 1e-3f)
                return false;
            if (!ArcSeatIsFree(gazeYawDeg + candidate, halfAngle))
                return false;
            offsetDeg = candidate;
            _questSelectionNote = " QUEST SELECTION: the quest log is standing on the right corner, "
                                  + $"so the popup took the seat IMMEDIATELY INSIDE it at "
                                  + $"{candidate:F0}° off the spawn gaze — the quest log's inner edge "
                                  + $"less this window's half-width and the "
                                  + $"{NeighbourGapDegrees + ChooserSlotExtraGapDegrees:F0}° chooser "
                                  + "clearance — free of every standing seat and inside the field of "
                                  + "view";
            return true;
        }
        catch (System.Exception)
        {
            offsetDeg = 0f;
            return false;
        }
    }

    /// <summary>The note the BESIDE clause carries for the popup's placement, and nothing for any
    /// other window. Consumed once: cleared on read so a later window cannot inherit it.</summary>
    private static string TakeQuestSelectionNote(ConvertedPanel? arriving)
    {
        string? note = _questSelectionNote;
        _questSelectionNote = null;
        if (string.IsNullOrEmpty(note) || !IsQuestPopupWindow(WindowOfPanel(arriving)))
            return string.Empty;
        return note!;
    }
}
