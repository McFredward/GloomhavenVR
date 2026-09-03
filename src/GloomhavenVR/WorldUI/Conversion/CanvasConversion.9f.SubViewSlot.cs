using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 9f (THE SUB-VIEW SLOT FORECAST — what the character screen is ABOUT to
// draw beside its column). A NEW part file for the reason parts 6, 8, 9b and 9c give: the refactor
// guard tracks the partial class's member and static-initializer order, and the filename sort
// ('.9e.' < '.9f.') appends this part AFTER every existing one, so nothing existing moves. No
// static field declared here carries an initializer — the one scratch list is created on first
// use — so no static constructor entry is added and the initializer order the guard tracks is
// untouched.
//
// ==========================================================================================
// WHY THIS EXISTS — USER REPORT 2026-09-03, THE QUEST INFO WINDOW INSIDE THE BATTLE-GOAL PICKER
// ==========================================================================================
//
// Verbatim, translated: "When one has to choose the PERSONAL QUEST, the quest-info window always
// spawns too far to the LEFT, inside the window in which one selects the personal quests.
// Increase the spacing between the two windows at spawn."
//
// WHICH TWO WINDOWS, READ OFF THE ModBuild 411 LOG (.planning/debug/LogOutput.log) AND NOT
// GUESSED. "The window in which one selects the personal quests" is the character screen
// ('New Party display') with the game's battle-goal picker ('UI Battle Goal Picker Window')
// open inside it as a sub-view — the picker the user has called "die persoenliche Quest" since
// his 2026-08-24 ruling, and the one the fixed fit seats flush on the column seam:
//
//     FIXED FIT 'GloomhavenVR.Panel_Modal_New Party display' APPLIED: ... the COLUMN SEAM is
//     x=-654 px ... PER SUB-VIEW: [UI Battle Goal Picker Window: rect -654..-88 x -255..255 px
//     ... wrote sub-view 'UI Battle Goal Picker Window' ... shifted by 14,-202 px onto the
//     column seam x=-654                                              (LogOutput.log:2412)
//
// "The quest-info window" is 'UI Quest Popup' (ID QuestPopup), and the log has it seated
// EXACTLY where that picker is drawn a moment later:
//
//     MAP ROOM WINDOW SLOT: 'GloomhavenVR.Panel_Modal_UI Quest Popup' claimed reservation 1
//     at 1° from the spawn gaze ... the FREE INTERVAL NEAREST THE GAZE (23°-wide DRAWN
//     content, seated at 0°±12° ... with a 2° gap) ... The windows already standing
//     [-25°±7° (world 68°) 'GloomhavenVR.Panel_Modal_New Party display' frame ±36° centred
//     32° off PERMANENT/no-X] were not touched. DEPTH STEP — IT SPAWNS NEARER, IN FRONT
//                                                                    (LogOutput.log:2345)
//
// THE ARITHMETIC OF THE OVERLAP. The character screen books only what it DRAWS at that instant
// — the 328 px character column, ±7° — and hands the rest of its 73° frame back to the arc as
// "PHANTOM FRAME" (ModBuild 234/245, the right rule for an empty sheet). Its column ends at
// -25°+7° = -18°. The picker is 566 px = 24° wide at 1.05 mm/px and 1.4 m, seated flush on the
// column seam, so it draws over [-18°, +6°]. The popup was seated at [-12°, +12°]: 18° of its
// 23° lie over the picker, on the picker's RIGHT half — i.e. from the player's side the info
// window stands inside the chooser, and the chooser's cards are to its left. That is the
// report, sign and all.
//
// WHY THE PACKER COULD NOT SEE IT. The order is the game's: UILoadoutManager.
// EnableLoadoutInteraction shows the quest popup FIRST (UILoadoutManager.cs:321) and only then
// shows the party display with an onShown listener that opens the picker
// (AutoselectCharacter → OpenBattleGoalPanel, :330/:371). At the moment the popup looks for a
// seat the picker is not open, so no visibility-tested measurement can find it; and once it IS
// open the popup may not be moved (user ruling: after the one pre-reveal re-place a window is
// never posed again while it floats). The only place the fix can live is the popup's SPAWN,
// and the only way it can live there is a FORECAST: the character screen's reservation must
// include the sub-view slot the game is about to fill.
//
// WHAT THIS PART DOES. <see cref="TryForecastSubViewSlot"/> answers, for a fixed-fit host, the
// host-local x-interval its NEXT (or current) sub-view will occupy: from the captured column
// seam, as wide as that sub-view NEEDS. The need is taken, in this order of trust,
//   1. from the fit's own solved measurement of the open sub-view group (ViewNeed), when a
//      sub-view is open and has been measured;
//   2. from the authored ink of the sub-view root the GAME says is open (ActiveDisplay) or is
//      about to open (the loadout is up ⇒ the battle-goal picker, by the call chain above),
//      measured over its graphics INCLUDING INACTIVE ones — UIWindow.Hide SetActive(false)s a
//      hidden window (UIWindow.cs:746), so a visibility-tested walk finds nothing;
//   3. from <see cref="ForecastFallbackNeedPx"/>, the ModBuild 411 log's own number for the
//      picker, when the authored ink cannot be read — and the line says so, so a forecast that
//      ran on the fallback cannot look like one that measured.
// The arc (ModalFallback.MeasureArcDrawnGeometry) unions that interval into the window's drawn
// extent, so the popup's search sees the picker's slot as taken before the picker exists, and
// keeps the chooser gap beside it. Outside the loadout, with nothing open and nothing forecast,
// this returns false and the reservation is exactly what it was — the phantom-frame hand-back
// the user's earlier rounds paid for is untouched.
//
// THIS PART NEVER WRITES GAME STATE. It reads the fit's own table, the game's singletons and
// transforms, and returns numbers.

internal static partial class CanvasConversion
{
    /// <summary>
    /// The battle-goal picker's measured need in authored px, from the ModBuild 411 hardware log
    /// (LogOutput.log:2412, "the open sub-view group needs 566x509 px at scale 1"). USED ONLY when
    /// the authored ink of the forecast sub-view cannot be measured, and named on the line whenever
    /// it is — a frozen literal is a consumer, so the forecast says which of its three sources
    /// answered.
    /// </summary>
    private const float ForecastFallbackNeedPx = 566f;

    /// <summary>Scratch for the authored-ink walk. Created on first use (no static initializer —
    /// see the part header).</summary>
    private static List<Graphic>? s_slotScratch;

    /// <summary>
    /// THE SUB-VIEW SLOT A FIXED-FIT HOST IS ABOUT TO FILL, as a host-local x-interval in authored
    /// px. True only for a fixed-fit window whose column seam has been captured AND for which a
    /// sub-view is open or forecast; false for every other window in the game, at the cost of one
    /// dictionary lookup.
    /// </summary>
    /// <param name="leftPx">The slot's left edge — the column seam.</param>
    /// <param name="rightPx">The slot's right edge — seam + the sub-view's need, never past the
    /// host's own right edge (content past the frame is cropped by the band limit anyway).</param>
    /// <param name="how">Which source answered, in the placement line's words.</param>
    internal static bool TryForecastSubViewSlot(ConvertedPanel? panel, out float leftPx,
        out float rightPx, out string how)
    {
        leftPx = 0f;
        rightPx = 0f;
        how = string.Empty;
        try
        {
            if (panel == null || panel.HostRect == null || panel.Target == null)
                return false;
            if (!TryGetLiveFixedFit(panel, out FixedFitState fx) || !fx.SeamCaptured)
                return false;

            float seam = fx.ColumnSeamX;
            float frameRight = fx.SizeCaptured ? fx.Size.x * 0.5f : panel.HostRect.rect.xMax;

            // ---- source 1: the fit's own solved need for the OPEN group -----------------------
            bool anyOpen = false;
            string openNames = string.Empty;
            for (int i = 0; i < fx.Views.Count; i++)
            {
                SubViewFit v = fx.Views[i];
                if (!v.Visible || v.View == null)
                    continue;
                anyOpen = true;
                openNames = openNames.Length == 0 ? v.View.name : openNames + " + " + v.View.name;
            }
            if (anyOpen && fx.ViewNeed.x >= 1f)
            {
                leftPx = seam;
                rightPx = Mathf.Min(seam + fx.ViewNeed.x, frameRight);
                how = $"the fit's own measured need of the OPEN sub-view group ({openNames}: "
                      + $"{fx.ViewNeed.x:F0} px at scale 1)";
                return rightPx - leftPx >= 1f;
            }

            // ---- source 2: the sub-view root the game names, measured over its authored ink ---
            NewPartyDisplayUI? display;
            try
            {
                display = NewPartyDisplayUI.PartyDisplay;
            }
            catch (System.Exception)
            {
                display = null;
            }
            if (display == null)
                return false;

            Component? root = null;
            string why = string.Empty;
            switch (display.ActiveDisplay)
            {
                case NewPartyDisplayUI.DisplayType.BATTLE_GOALS:
                    root = display.BattleGoalWindow;
                    why = "the game reports ActiveDisplay=BATTLE_GOALS";
                    break;
                case NewPartyDisplayUI.DisplayType.INVENTORY:
                    root = display.ItemInventoryDisplay;
                    why = "the game reports ActiveDisplay=INVENTORY";
                    break;
                case NewPartyDisplayUI.DisplayType.CARDS:
                    root = display.AbilityCardsDisplay;
                    why = "the game reports ActiveDisplay=CARDS";
                    break;
                case NewPartyDisplayUI.DisplayType.PERKS:
                    root = display.PerkManager;
                    why = "the game reports ActiveDisplay=PERKS";
                    break;
                case NewPartyDisplayUI.DisplayType.CHARACTER_SELECTOR:
                    root = display.CharacterSelector;
                    why = "the game reports ActiveDisplay=CHARACTER_SELECTOR";
                    break;
            }
            if (root == null && LoadoutIsOpen())
            {
                // THE FORECAST PROPER. The loadout shows the quest popup first and opens the
                // battle-goal picker on the character screen's onShown (UILoadoutManager.cs:321,
                // :330, :371), so while the loadout stands the picker's slot is spoken for even
                // on the tick nothing is open yet.
                root = display.BattleGoalWindow;
                why = "the LOADOUT is open, and UILoadoutManager.AutoselectCharacter opens the "
                      + "battle-goal picker on the character screen's onShown "
                      + "(UILoadoutManager.cs:330/371) — forecast before the picker exists";
            }
            if (root == null)
                return false;

            float need = MeasureAuthoredInkWidthPx(panel, root.transform, out int graphics);
            bool rootInactive = !root.gameObject.activeInHierarchy;
            string source;
            if (need >= 1f && !(rootInactive && need < ForecastFallbackNeedPx))
            {
                source = $"the authored ink of '{root.name}' ({need:F0} px over {graphics} "
                         + "graphic(s), inactive ones included)";
            }
            else if (need >= 1f)
            {
                // A HIDDEN root's layout-driven children can still be unlaid-out (0 px rects are
                // skipped above), so a cold read can come back narrower than the picker really is.
                // A too-narrow forecast is the defect itself, half-fixed; the wider of the two
                // numbers costs at most a few degrees of empty arc beside the column.
                source = $"the authored ink of '{root.name}' read {need:F0} px over {graphics} "
                         + "graphic(s) while HIDDEN, narrower than the picker is known to be, so the "
                         + $"FALLBACK width {ForecastFallbackNeedPx:F0} px (the ModBuild 411 log's "
                         + "measured need for the picker) is booked instead";
                need = ForecastFallbackNeedPx;
            }
            else
            {
                need = ForecastFallbackNeedPx;
                source = $"the FALLBACK width {ForecastFallbackNeedPx:F0} px (the ModBuild 411 "
                         + $"log's measured need for the picker) because '{root.name}' has no "
                         + "measurable authored ink yet";
            }
            leftPx = seam;
            rightPx = Mathf.Min(seam + need, frameRight);
            how = $"{why}; width from {source}";
            return rightPx - leftPx >= 1f;
        }
        catch (System.Exception)
        {
            leftPx = 0f;
            rightPx = 0f;
            how = "the forecast threw and answered nothing";
            return false;
        }
    }

    /// <summary>The pre-scenario loadout is on screen — <c>UILoadoutManager.IsOpen</c>, read the
    /// way <c>LoadoutConfirmPark</c> reads the same singleton.</summary>
    private static bool LoadoutIsOpen()
    {
        try
        {
            if (!Singleton<UILoadoutManager>.IsInitialized)
                return false;
            UILoadoutManager lm = Singleton<UILoadoutManager>.Instance;
            return lm != null && lm.IsOpen;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The x-extent of a sub-view root's AUTHORED ink in host-local px — every <c>Graphic</c> under
    /// it, inactive ones included, minus the mod's own objects and minus full-frame backdrop plates
    /// (the same <see cref="FixedFitPlateWidthFraction"/> test the fit applies). Transforms of an
    /// inactive subtree are still the prefab's, so the width is the one the fit will measure once
    /// the game shows it. 0 when nothing could be read.
    /// </summary>
    private static float MeasureAuthoredInkWidthPx(ConvertedPanel panel, Transform root,
        out int graphics)
    {
        graphics = 0;
        if (panel.HostRect == null || panel.Target == null)
            return 0f;
        List<Graphic> scratch = s_slotScratch ??= new List<Graphic>(256);
        scratch.Clear();
        root.GetComponentsInChildren(includeInactive: true, scratch);
        float plateW = panel.Target.rect.width * FixedFitPlateWidthFraction;
        float minX = float.MaxValue, maxX = float.MinValue;
        Vector3[] corners = CornerScratch;
        for (int i = 0; i < scratch.Count; i++)
        {
            Graphic g = scratch[i];
            if (g == null)
                continue;
            if (g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                continue;
            if (g.transform is not RectTransform rt)
                continue;
            Rect r = rt.rect;
            if (r.width < 0.5f || r.height < 0.5f)
                continue;
            rt.GetWorldCorners(corners);
            float gMin = float.MaxValue, gMax = float.MinValue;
            for (int c = 0; c < 4; c++)
            {
                float x = panel.HostRect.InverseTransformPoint(corners[c]).x;
                if (x < gMin) gMin = x;
                if (x > gMax) gMax = x;
            }
            if (gMax - gMin >= plateW)
                continue; // a backdrop plate, not content — the fit's own rule
            if (gMin < minX) minX = gMin;
            if (gMax > maxX) maxX = gMax;
            graphics++;
        }
        scratch.Clear();
        return graphics == 0 || maxX <= minX ? 0f : maxX - minX;
    }
}
