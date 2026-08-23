using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE ONE TABLE OF "THIS SUBTREE IS A MOUSEOVER", identified by the game's own component
/// types.</b>
///
/// <para><b>WHY IT IS ITS OWN CLASS (ModBuild 241).</b> The table, the six probes and the family
/// names were written in ModBuild 201 inside <c>CanvasConversion.3.Fit.cs</c>, for — quoting its own
/// comment — "the whole of report (5), 'nothing may shift because of mouseovers'". ModBuild 241 needs
/// the identical answer in a second place (<see cref="PanelInkBounds"/>, so the brass grab bar stops
/// resizing under a hover), and this repo has already paid for the alternative: <c>PanelInkBounds</c>
/// restates <c>FixedFitPlateWidthFraction</c>, <c>…HeightFraction</c> and <c>FitMinAlpha</c> BY VALUE
/// and says out loud in its own class comment that each is a standing drift risk. A third
/// hand-copied list — this one six <c>TryGetComponent</c> calls long, and the one that decides
/// whether a user-visible handle moves — would be the same mistake at three times the size. So the
/// probe moved here and BOTH call sites read it; <c>CanvasConversion.3.Fit.cs</c> keeps its private
/// wrappers so not one of its own call sites changed.</para>
///
/// <para><b>THE RULE.</b> A transform belongs to a transient family when itself or an ancestor, up to
/// but excluding the caller's root, carries one of the six types below. Matched BY COMPONENT TYPE,
/// never by name, never by "it is not one of the known sub-views", and never by the nested
/// <c>Canvas</c> the game adds to a full ability card on show — ModBuild 194 proved that
/// <c>Canvas</c> outlives its own hide, because the mod's own <c>GraphicRaycaster</c> adoption keeps
/// it alive, so it would report "hovering" forever. The component that MEANS "this is a full card"
/// never lies.</para>
///
/// <para><b>IT IS AN IDENTITY, NOT A HEURISTIC, AND THAT IS THE WHOLE POINT.</b> The user's ruling of
/// 2026-08-24 is explicit that the exemption must be narrow: <i>"WICHTIG: Das soll nicht für andere
/// Elemente gelten wie zB die Auswahl der persönlichen Quest wo das resizing das von dir eingebaute
/// wurde das Problem der Verdeckung behoben hat."</i> Nothing here tests size, position, lifetime,
/// transparency or "does it stick out of the frame" — every one of those would also describe the
/// battle-goal picker's reward rows, which is the content ModBuild 236 moved the bar for and which he
/// is happy with. A row of the personal-quest picker carries none of these six components and no
/// ancestor of it does either: it lives under the picker's own sub-view root, and the six types are
/// tooltip/preview widgets the game instantiates for a hover and nothing else. See
/// <see cref="PanelInkBounds"/> for the same argument stated against the bar's numbers.</para>
///
/// <para><b>CONTAINMENT IS THE RIGHT TEST HERE</b>, which is worth writing down because this repo has
/// twice shipped the opposite mistake. <c>[[containment-is-not-identity]]</c> is a rule about
/// DECIDING WHAT AN OBJECT IS — <c>GetComponentInParent&lt;UIWindow&gt;</c> must never answer "is this
/// a window root". Here the question genuinely is about the ancestor: the marker component sits on
/// the tooltip's own root and the graphic being judged is a descendant of it, so "does this graphic
/// belong to a tooltip subtree" IS an ancestor question. Both walks stop at the caller's root, so
/// nothing outside the window can ever answer it.</para>
///
/// <para><b>THIS CLASS NEVER WRITES GAME STATE.</b> It reads components and returns an int.</para>
/// </summary>
internal static class TransientFamilies
{
    /// <summary>Human name per family index of <see cref="Self"/>; index 0 is "not transient" and is
    /// never printed. Kept as one array so a family added here is named in every log line in the mod
    /// that prints a family, without a second table to remember.</summary>
    internal static readonly string[] Names =
    {
        string.Empty,
        "FullAbilityCard (the ability-card hover preview)",
        "UIPartyItemInventoryTooltip (the item-card hint)",
        "UILocalTooltip and its subclasses (UIItemLocalTooltip, UIQuestEnemyStatsPopup)",
        "UIItemModifiersTooltip (the modifier flyout on an item card)",
        "UITempleSlotTooltip (the blessing hint)",
        "TooltipUI (the ExtendedButton hint)",
    };

    /// <summary>
    /// Does THIS transform carry a transient family marker? The six type probes, in the order the
    /// ModBuild 200 hardware log's own LOCAL TOOLTIP lines make likely, so the common case exits
    /// first. <c>TryGetComponent</c> matches subclasses, which is why <c>UILocalTooltip</c> covers
    /// three families in one probe and does not allocate.
    ///
    /// <para>The list is the one <c>TooltipOnWindow</c>'s family table already argues for
    /// (TooltipOnWindow.cs:523-543), with <c>UILocalTooltip</c> listed as the BASE so its subclasses
    /// are one entry and a fourth subclass would need no change, plus <c>UIItemModifiersTooltip</c>,
    /// which is the one family that table is missing.</para>
    /// </summary>
    internal static int Self(Transform t)
    {
        if (t.TryGetComponent<FullAbilityCard>(out _))
            return 1;
        if (t.TryGetComponent<UIPartyItemInventoryTooltip>(out _))
            return 2;
        if (t.TryGetComponent<UILocalTooltip>(out _))
            return 3;
        if (t.TryGetComponent<UIItemModifiersTooltip>(out _))
            return 4;
        if (t.TryGetComponent<UITempleSlotTooltip>(out _))
            return 5;
        if (t.TryGetComponent<TooltipUI>(out _))
            return 6;
        return 0;
    }

    /// <summary>
    /// WHICH TRANSIENT FAMILY THIS NODE BELONGS TO, or 0 for real window content — the BOTTOM-UP
    /// form, for a caller that holds a graphic and has to walk to the root to find out.
    ///
    /// <para>Answers with the INNERMOST marker on the chain (the node itself first, then upward),
    /// which only differs from the top-down form when one tooltip is nested inside another; both
    /// forms are non-zero for exactly the same set of nodes, which is the only thing an exclusion
    /// reads.</para>
    ///
    /// <para><paramref name="memo"/> is the CALLER'S, and its lifetime is the caller's problem: this
    /// method never clears it and never keeps a reference to it. Siblings share their whole ancestor
    /// chain, so a ~250-transform window costs about one six-way probe per transform. The root is
    /// deliberately never memoised — it short-circuits before the lookup — so a memo can be reused
    /// across windows without a cached answer that was computed against a different root.</para>
    /// </summary>
    internal static int Of(Transform? node, Transform root, Dictionary<Transform, int> memo)
    {
        if (node == null || ReferenceEquals(node, root))
            return 0;
        if (memo.TryGetValue(node, out int cached))
            return cached;
        int family = Self(node);
        if (family == 0)
            family = Of(node.parent, root, memo);
        memo[node] = family;
        return family;
    }

    /// <summary>The families in <paramref name="mask"/>, spelled out for the log — a bitmask in a
    /// hardware log is a number somebody has to decode against a source file that may have moved on
    /// by then.</summary>
    internal static string Describe(int mask)
    {
        var sb = new System.Text.StringBuilder(96);
        for (int i = 1; i < Names.Length; i++)
        {
            if ((mask & (1 << i)) == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(Names[i]);
        }
        return sb.Length > 0 ? sb.ToString() : "no family";
    }
}
