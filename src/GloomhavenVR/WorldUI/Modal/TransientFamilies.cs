using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE ONE TABLE OF "THIS SUBTREE IS DRAWN BUT MUST NOT BE MEASURED", identified by the game's
/// own component types.</b>
///
/// <para><b>ModBuild 448 WIDENED WHAT THE TABLE IS ABOUT, AND THE OLD NAME WAS WRONG BY ONE WORD.</b>
/// Families 1-6 are mouseovers; family 7 is a decorative UI EFFECT container. Both answer the same
/// question and are read by the same call sites, because the contract was never "is this a hover" -
/// it is "may this graphic decide where the window's edges, its handle and its parked confirm
/// button go". A hover may not, because it comes and goes with the pointer; an animated FX quad may
/// not, because it comes and goes with its own animation clock. See the ModBuild 448 block on
/// <see cref="Names"/> for the hardware that added the second one.</para>
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
/// but excluding the caller's root, carries one of the types below. Matched BY COMPONENT TYPE,
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
/// is happy with. A row of the personal-quest picker carries none of these components and no
/// ancestor of it does either: it lives under the picker's own sub-view root, and the types are
/// tooltip/preview widgets the game instantiates for a hover, plus the effect container the game
/// itself declares its animated quads in. See <see cref="PanelInkBounds"/> for the same argument
/// stated against the bar's numbers.</para>
///
/// <para><b>THE GEOMETRIC TEST IS STILL REFUSED, AND FAMILY 7 IS WHY IT HAD TO BE.</b> The obvious
/// rule for the ModBuild 448 defect - "a graphic that paints outside the frame may not size the
/// window" - is exactly the rule the paragraph above rules out: the battle-goal picker draws 373 px
/// below its frame and the user accepted the bar following it (<c>GRAB BAR CLEARS THE INK</c>'s own
/// two-reading clause says so in as many words). So the wave is excluded because of WHAT IT IS, and
/// never because of where it happens to be this frame.</para>
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
        // ---- ModBuild 448 - WHY FAMILY 7 EXISTS. ------------------------------------------------
        // USER REPORT (2026-09-05, verbatim): "Das Questfenster im Map-Raum verletzte im Test die
        // 1:1 Regel! Die Groesse ist unterschiedlich dargestellt bei den Spielern und daher auch wo
        // der Greifbalken genau ist divergiert im Test. Weiterhin ist der Abstand bei mir immer
        // noch zu gross angezeigt worden" (abstand2.jpg).
        //
        // ONE OBJECT ANSWERED BOTH HALVES. The map room's 'UI Quest Popup' carries a
        // UIFX_MaterialFX_Control ButtonFX container whose animated quad 'Button_FX/UIFX_Wave (1)'
        // paints 245-299 px BELOW a 512x1021 px frame, and its extent changes with its own
        // animation and not with the quest. On the ModBuild 447 hardware pair that single graphic:
        //
        //   * held the host-rect fit at the full frame instead of letting it shrink to the drawn
        //     content, so the HOST settled 'UI Quest Popup' at 512x1021 px (own height 1.072 m) at
        //     the same open the CO-PLAYER settled it at 512x779 px (0.817 m) - nine different ink
        //     rectangles for one window across the session, spanning 763..1289 px of height. The
        //     world size, the grab bar and the shared seat are all pure functions of that rect, so
        //     the three diverged TOGETHER: that IS the 1:1 breach, and it needs no wire field to
        //     fix, only an input that does not depend on WHEN it was sampled;
        //   * dragged the quest information's measured bottom edge to the card's own bottom edge -
        //     MAP TRAVEL CONFIRM's own line reads "RAW union (x:-296.00, y:-590.50 ...) CLAMPED ...
        //     window-local y = -510.5, i.e. 0 mm above the card's own bottom edge", while the drawn
        //     rewards panel ends at y = -244 - so the confirm button and the ready shields hung
        //     266 px = 279 mm below the content. That is the empty band in abstand2.jpg, ~26 % of
        //     the card's height, and it is NOT a game-authored plate: nothing is drawn in it and
        //     the forest behind the window shows through;
        //   * put the grab bar 261 px = 274 mm below the window's own bottom edge, which is the
        //     14 'GRAB BAR CLEARS THE INK: NOT ACHIEVED' lines on the host, every one of them
        //     naming 'UIFX_Wave (1)' at y=-756 px as the graphic holding it down.
        //
        // That last instrument had already asked the question it could not answer itself: "TWO
        // READINGS SETTLE WHETHER IT IS A FAULT. If that graphic really is painted there ... the
        // bar is CORRECT ... If it is not on the screen, the ink union is measuring a ghost." The
        // screenshot is the second reading.
        //
        // ---- THE ONE OTHER WINDOW THIS FAMILY REACHES, NAMED BEFORE IT IS TESTED. --------------
        // This table is read by the host-rect fit and by PanelInkBounds, i.e. by EVERY window, so
        // "which other window carries a UIFX container" is the question that decides whether 448
        // re-broke something while fixing the quest card. The ModBuild 447 pair answers it: exactly
        // one other panel's fit line names a UIFX quad, 'GloomhavenVR.Panel_ElementBoard' —
        //
        //   Host rect fit 'GloomhavenVR.Panel_ElementBoard': 60x120 -> 60x120 px ... union
        //   (-242,-53)..(52,48) px [frame-clamped]; top (rendered rects): 'FX/UIFX_Wave (1)'
        //   106x101px at (-53,-53), 'FX/UIFX_Sparks (1)' 106x101px at (-53,-53),
        //   'Creating icon/CreatingText' 199x50px at (-242,-25)
        //
        // Its two FX quads are the TALLEST things in that union (101 px against the text's 50), so
        // if family 7 reaches them the board's measured content halves and ElementBoardSurface,
        // which lays itself out from Panel.HostRect.rect, would draw a shorter board. THE SHIPPED
        // READING TO CHECK, ON THE FIRST HARDWARE LOG AFTER 448: that same line must still write
        // '60x120 -> 60x120 px ... rigid re-centre only (host size unchanged)'. If it writes any
        // other host size, family 7 reached a window it should not have, and the fix is to narrow
        // IsPureEffectContainer (not to name a panel here — a name test is what this class exists
        // to refuse). The merchant ('UI Shop Item Window') and the temple ('UI Temple Window'), the
        // two windows the user accepted in the round just gone, name no UIFX quad on either
        // machine and are untouched by construction.
        "FullAbilityCard (the ability-card hover preview)",
        "UIPartyItemInventoryTooltip (the item-card hint)",
        "UILocalTooltip and its subclasses (UIItemLocalTooltip, UIQuestEnemyStatsPopup)",
        "UIItemModifiersTooltip (the modifier flyout on an item card)",
        "UITempleSlotTooltip (the blessing hint)",
        "TooltipUI (the ExtendedButton hint)",
        "UIFX_MaterialFX_Control / UIFX_MaterialFX_AttackModifier (the game's own decorative UI "
            + "effect container — the animated 'UIFX_Wave'/'UIFX_Sparks' quads under 'Button_FX', "
            + "'FX' or 'Initiative_Selection_FX')",
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
        // FAMILY 7 LAST, deliberately: the six above are one TryGetComponent each and answer the
        // common case, while this one may have to ask a controller where its own icon lives. A
        // window with no UIFX controller in it pays two more failed TryGetComponent calls per node
        // and nothing else.
        if (t.TryGetComponent<UIFX_MaterialFX_Control>(out UIFX_MaterialFX_Control control))
            return IsPureEffectContainer(t, control.MainIcon, control.MainIcon2) ? 7 : 0;
        if (t.TryGetComponent<UIFX_MaterialFX_AttackModifier>(out UIFX_MaterialFX_AttackModifier mod))
            return IsPureEffectContainer(t, mod.MainIcon, null) ? 7 : 0;
        return 0;
    }

    /// <summary>
    /// <b>THE GUARD THAT MAKES FAMILY 7 SAFE BY CONSTRUCTION.</b> A <c>UIFX_MaterialFX_Control</c>
    /// declares its effect quads (<c>MainIconFX</c>, <c>MainIcon2FX</c>, <c>TextAndSubIconFX</c>,
    /// <c>ActivateFX</c>) SEPARATELY from the icon they decorate (<c>MainIcon</c>,
    /// <c>MainIcon2</c>). Where the controller sits on a pure FX container — the shipped authoring,
    /// and the one the hardware log's own paths show ('Button_FX/UIFX_Wave (1)',
    /// 'FX/UIFX_Wave (1)', 'Initiative_Selection_FX/UIFX_Wave') — its whole subtree is decoration
    /// and excluding it removes nothing the player reads.
    ///
    /// <para>Where it instead sits on the WIDGET (a button that owns both its label and its FX),
    /// excluding the subtree would delete that label from every union the fit and the bar are
    /// placed by, and the handle would cut across the button — <c>quest_überlap.jpg</c>, a defect
    /// this repo has already paid for. So the family is REFUSED whenever the controller's own icon,
    /// or a Graphic on the controller node itself, lies inside the subtree the exclusion would
    /// take. In that authoring this family is inert and the window measures exactly as it did
    /// before, which is the outcome that cannot be wrong on the user's screen.</para>
    ///
    /// <para>Cost: two reference compares and at most two <c>IsChildOf</c> walks, and ONLY on a node
    /// that actually carries a controller — a handful per window, never per graphic.</para>
    /// </summary>
    private static bool IsPureEffectContainer(Transform t, Graphic? icon, Graphic? icon2)
    {
        if (t.TryGetComponent<Graphic>(out _))
            return false;   // the controller node paints something itself — not a pure container
        if (icon != null && icon.transform.IsChildOf(t))
            return false;
        if (icon2 != null && icon2.transform.IsChildOf(t))
            return false;
        return true;
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
