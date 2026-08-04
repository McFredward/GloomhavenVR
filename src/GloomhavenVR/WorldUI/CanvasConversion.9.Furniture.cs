using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 9 (NON-CANVAS transparent furniture on the distance ladder). NEW
// members only - appended after part 8 in the filename sort, so the tracked member /
// static-initializer order of parts 1-8 (part 1's header explains the rules) is untouched.

/// <summary>
/// Owner of a cluster of NON-CANVAS transparent renderers that must ride the converted-panel
/// distance ladder as ONE group (see <see cref="CanvasConversion.RegisterFurniture"/>): the
/// control board is the one implementer today. The anchor supplies the group's liveness and
/// its eye-distance measure - the same "nearest point of my finite rect" question
/// <c>CanvasConversion.PanelEyeDistance</c> answers for a panel, so group and panels are
/// ranked by directly comparable numbers.
/// </summary>
internal interface IFurnitureOrderAnchor
{
    /// <summary>Log name for the group ("control board").</summary>
    string FurnitureOrderName { get; }

    /// <summary>False once the owner is torn down - the group (and its entries) are dropped.</summary>
    bool FurnitureOrderAlive { get; }

    /// <summary>Eye distance of the furniture cluster - nearest point of the owner's finite
    /// rect to <paramref name="eye"/>, matching <c>PanelEyeDistance</c>'s measure (stable
    /// under head ROTATION by construction: it reads only the eye position).</summary>
    float FurnitureEyeDistance(Vector3 eye);
}

internal static partial class CanvasConversion
{
    // ---- board furniture rides the distance ladder too ---------------------------------------
    //
    // ROOT CAUSE (user report 2026-08-04: "Die 'Statustafel' am Controllboard wird wieder mit dem
    // Optionsmenu DAHINTER ueberdeckt"). The control board carries transparent, NON-canvas
    // furniture: the pick-status placard (a Sprites/Default parchment quad + a plain TextMeshPro,
    // PlayTray.5.Status), the round readout label, the engraved keycap labels (sortingOrder 3),
    // the native keycap face sprites (1), the slot/item-use glow quads and the pile count/caption
    // labels. All of it draws in the transparent queue with ZWrite OFF at sortingOrder 0..3 -
    // and Unity resolves transparent renderers by sortingLayer -> sortingOrder FIRST, distance
    // last. Every converted panel now sits on the distance ladder at order >= PanelOrderBase
    // (100), so a panel BEHIND the board still painted over the board's transparent furniture:
    // the menu draws later (order 100+ vs 0..3) and the furniture wrote no depth for the menu's
    // ZTest to fail against. The board SLAB is opaque (queue <= 2500, depth-writing) and was
    // always correct - exactly the split the user saw: board wins, placard loses.
    //
    // THE FIX is the ladder's own medicine, not a special case: the furniture registers as a
    // GROUP that is ranked against the ladder by the SAME eye-distance measure the panels use.
    // Each frame the group counts the listed panels measurably FARTHER than the board
    // (> OrderSwapMarginMeters beyond the group distance - a tie keeps the panel in front,
    // which preserves the shipped "keycap furniture draws under the board's own docked panels"
    // contract) and parks its renderers in the BAND just below the nearest-in-front panel's
    // slot: bandBase = PanelOrderBase + rank*PanelOrderStep - FurnitureBandWidth. The band sits
    // ABOVE every panel behind the board (their own followers reach at most +10 of a step of
    // 16) and BELOW every panel in front of it, so BOTH directions hold: placard in front of
    // the menu -> placard visible; menu in front of the placard -> menu wins. With no panel
    // behind the board the band is 95..99 - below the whole ladder, the shipped behaviour.
    //
    // Each renderer keeps its CREATION-time sortingOrder (0..3: plate/labels 0, keycap faces 1,
    // dust/embers 2, engraved labels 3) as its offset INSIDE the band, so the board's internal
    // draw ladder is preserved verbatim - this pass only moves the whole cluster.
    //
    // Flicker: the rank is applied through the ladder's own two gates (OrderSwapMarginMeters +
    // OrderSwapStableFrames) - head micro-motion clears neither, a genuine board/window move
    // re-ranks within ~0.1 s. Known residue, accepted: TMP fallback-font submeshes created
    // AFTER registration (e.g. the checkmark glyph of "✓ READY") copy the parent's order
    // at spawn and only re-seat on the next rank change.
    //
    // ROUND 2 (user report 2026-08-04 #2: "Die Initiativbilder vermischen sich mit dem Text der
    // Statustafel. Auch nicht immer - je nach Winkel ploppt es manchmal auf und manchmal nicht.").
    // ROOT CAUSE: the rank above counted EVERY listed panel measurably farther than the board -
    // including the board's OWN docked panels. The initiative track is docked at the board's top
    // edge and grows UP (PlayTray.InitiativeMountY = BoardH/2 - 0.06, max height 0.14), so its
    // rect is a thin strip around the top edge, exactly where the placard hovers (PickBannerBase
    // y = BoardH/2 + 0.10). The furniture group's distance is measured to the board's LARGE
    // furnished face rect, the track's to its own SMALL strip - two nearest-point-of-rect
    // measures that move DIFFERENTLY as the head orbits: with the eye near board-top level both
    // clamp to the top edge (tie, track in front, placard under the portraits - correct), while
    // from lower/lateral angles the eye's foot lies inside the big rect but several centimetres
    // below/away from the strip, the track measures > OrderSwapMarginMeters farther than the
    // board, got COUNTED as "behind" it, and the band jumped ABOVE the track's ladder slot -
    // placard plate and text painted over the portraits. The 2 cm margin is the flip line the
    // user saw popping.
    //
    // THE FIX is structural, not a bigger margin: distance never arbitrates INSIDE the board's
    // own plane. Every dock placement tags its panel with the board it is docked on
    // (ConvertedPanel.OrderCluster == this group's anchor), and the rank pass (a) never counts a
    // same-board panel as "behind" the board, whatever it measures, and (b) caps the rank at the
    // lowest ladder index any same-board panel occupies, so the band always sits BELOW every
    // docked panel's slot. Board furniture < board-docked panels is now a fixed sub-ladder that
    // holds at every angle; the measured distance still ranks the whole board cluster against
    // everything else (floated menus, bars, other boards), so a window between the eye and the
    // board still beats both, and the furniture still beats panels genuinely behind the board.
    private const int FurnitureBandWidth = 5;

    /// <summary>One registered renderer of a furniture group: the renderer plus its
    /// creation-time sortingOrder, kept as the in-band offset (0..<see cref="FurnitureBandWidth"/>-1).</summary>
    private struct FurnitureEntry
    {
        public Renderer? Renderer;
        public int Offset;
    }

    /// <summary>A furniture cluster riding the ladder as one unit (see the header above).</summary>
    private sealed class FurnitureGroup
    {
        public FurnitureGroup(IFurnitureOrderAnchor anchor) => Anchor = anchor;

        public readonly IFurnitureOrderAnchor Anchor;
        public readonly List<FurnitureEntry> Entries = new(48);

        /// <summary>Ladder rank currently applied (-1 = never seated; the first measure applies
        /// immediately, like a newcomer panel's distance-correct insert).</summary>
        public int AppliedRank = -1;

        public int PendingRank = -1;
        public int PendingStreak;
    }

    private static readonly List<FurnitureGroup> FurnitureGroups = new(2);

    private static float s_nextFurnitureLogAt;

    /// <summary>
    /// Register a mod-owned transparent <paramref name="renderer"/> into
    /// <paramref name="anchor"/>'s furniture group. Idempotent per renderer; the renderer's
    /// CURRENT sortingOrder is captured as its in-band offset, so callers must have finished
    /// their own relative-order writes (keycap label 3, face sprite 1, ...) before adopting.
    /// Destroyed renderers are pruned on the next order apply; a dead anchor drops its whole
    /// group.
    /// </summary>
    internal static void RegisterFurniture(IFurnitureOrderAnchor anchor, Renderer renderer)
    {
        if (anchor == null || renderer == null)
            return;
        FurnitureGroup? group = null;
        for (int i = 0; i < FurnitureGroups.Count; i++)
        {
            if (ReferenceEquals(FurnitureGroups[i].Anchor, anchor))
            {
                group = FurnitureGroups[i];
                break;
            }
        }
        if (group == null)
        {
            group = new FurnitureGroup(anchor);
            FurnitureGroups.Add(group);
        }
        for (int i = 0; i < group.Entries.Count; i++)
        {
            if (ReferenceEquals(group.Entries[i].Renderer, renderer))
                return;
        }
        int offset = Mathf.Clamp(renderer.sortingOrder, 0, FurnitureBandWidth - 1);
        group.Entries.Add(new FurnitureEntry { Renderer = renderer, Offset = offset });
        // Seat it immediately at the group's current band (no one-frame gap at order 0..3,
        // which is exactly the defect band). A never-ranked group seats on its first tick.
        if (group.AppliedRank >= 0)
        {
            int want = FurnitureBandBase(group.AppliedRank) + offset;
            if (renderer.sortingOrder != want)
                renderer.sortingOrder = want;
        }
    }

    /// <summary>Lowest order of the band for a group at <paramref name="rank"/> (the count of
    /// ladder panels measurably farther than the group).</summary>
    private static int FurnitureBandBase(int rank) =>
        PanelOrderBase + rank * PanelOrderStep - FurnitureBandWidth;

    /// <summary>
    /// Per-frame service, run from <see cref="TickPanelOrder"/> AFTER the panel distances are
    /// measured and the ladder orders are assigned: rank every furniture group against the
    /// listed panels and re-seat its renderers when the rank settles on a new value (same
    /// margin + streak gates as an adjacent panel swap). A steady scene writes nothing.
    /// </summary>
    private static void TickFurnitureOrder(Vector3 eye)
    {
        for (int g = FurnitureGroups.Count - 1; g >= 0; g--)
        {
            FurnitureGroup group = FurnitureGroups[g];
            if (!group.Anchor.FurnitureOrderAlive)
            {
                FurnitureGroups.RemoveAt(g); // owner torn down; renderers died with it
                continue;
            }

            float dist = group.Anchor.FurnitureEyeDistance(eye);
            // A panel counts as "behind the board" only when it is farther by MORE than the
            // swap margin - a tie keeps the panel in front. A panel DOCKED ON THIS BOARD
            // (OrderCluster == this anchor) never counts, whatever it measures: its rect and
            // the board's furnished rect are coplanar strips whose nearest-point measures
            // drift apart by several centimetres as the head orbits (see the ROUND 2 header),
            // and letting that drift cross the margin was the angle-dependent placard-over-
            // portraits pop. The cap below additionally pins the band under the LOWEST ladder
            // slot any same-board panel holds, so "furniture below the board's own panels"
            // holds even when the ladder ranks a docked panel below a non-board panel that
            // out-measured the board anchor.
            int desired = 0;
            int clusterFloor = int.MaxValue;
            for (int i = 0; i < OrderedPanels.Count; i++)
            {
                ConvertedPanel p = OrderedPanels[i];
                if (ReferenceEquals(p.OrderCluster, group.Anchor))
                {
                    if (i < clusterFloor)
                        clusterFloor = i; // rigid sub-ladder: band must stay below this slot
                    continue;
                }
                if (p.OrderDistance > dist + OrderSwapMarginMeters)
                    desired++;
            }
            if (desired > clusterFloor)
                desired = clusterFloor;
            if (desired > PanelOrderMaxRank)
                desired = PanelOrderMaxRank;

            if (desired == group.AppliedRank)
            {
                group.PendingRank = -1;
                group.PendingStreak = 0;
                continue;
            }
            if (group.AppliedRank >= 0) // first-ever measure seats immediately (newcomer rule)
            {
                if (desired != group.PendingRank)
                {
                    group.PendingRank = desired;
                    group.PendingStreak = 1;
                    continue;
                }
                if (++group.PendingStreak < OrderSwapStableFrames)
                    continue;
            }

            int previous = group.AppliedRank;
            group.AppliedRank = desired;
            group.PendingRank = -1;
            group.PendingStreak = 0;
            ApplyFurnitureOrder(group);
            LogFurnitureOrder(group, previous, dist);
        }
    }

    /// <summary>Write the group's band onto every registered renderer (change-gated); prune
    /// entries whose renderer was destroyed (keycap rebuilds, transient FX).</summary>
    private static void ApplyFurnitureOrder(FurnitureGroup group)
    {
        int bandBase = FurnitureBandBase(group.AppliedRank);
        for (int i = group.Entries.Count - 1; i >= 0; i--)
        {
            FurnitureEntry entry = group.Entries[i];
            if (entry.Renderer == null)
            {
                group.Entries.RemoveAt(i);
                continue;
            }
            int want = bandBase + entry.Offset;
            if (entry.Renderer.sortingOrder != want)
                entry.Renderer.sortingOrder = want;
        }
    }

    /// <summary>Attribution line for the next hardware log (rank changes are rare and
    /// deliberate, but a "placard still hidden" report must be decidable from the log):
    /// names the group, its measured distance, how many panels rank behind it and the
    /// resulting band. Throttled like the ladder's own diagnostics.</summary>
    private static void LogFurnitureOrder(FurnitureGroup group, int previousRank, float dist)
    {
        float now = Time.unscaledTime;
        if (now < s_nextFurnitureLogAt)
            return;
        s_nextFurnitureLogAt = now + OrderDiagMinIntervalSeconds;
        int bandBase = FurnitureBandBase(group.AppliedRank);
        VRLog.Info("WorldUI", $"FURNITURE ORDER: '{group.Anchor.FurnitureOrderName}' " +
                              $"({group.Entries.Count} renderer(s), d={dist:F2}m) ranks ABOVE " +
                              $"{group.AppliedRank} farther panel(s) (was {previousRank}) - band " +
                              $"{bandBase}..{bandBase + FurnitureBandWidth - 1}. Its transparent " +
                              "furniture now paints over panels behind it and under panels in front.");
    }
}
