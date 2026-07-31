using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.Surfaces;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// TAKE-DAMAGE PANEL SAFETY (test #23, item 6 — the burn-two DEADLOCK + game exception).
///
/// ROOT CAUSE (Player.log, corrected from the field report's hypothesis): the panel's
/// state was NOT clobbered by our dock suppression. The panel closed through the game's
/// OWN normal path — the player clicked the docked "Receive Damage" button, the game
/// applied the hit ("BruteID takes 5 damage" → <c>GameState.PlayerNotAvoidingDamage</c>)
/// and ran <c>TakeDamagePanel.ResetAndHide</c>, which nulls <c>actorBeingAttacked</c>
/// (TakeDamagePanel.cs:1058) after hiding the window. AFTER that, stale input kept
/// reaching the docked widgets and re-entered the panel's handlers on the now-dead
/// panel:
///   - the reliable-click queue (commit 2f43a79) can deliver a SECOND queued press to
///     the same button a frame late → a second <c>TakeDamage()</c> → NRE at
///     <c>actorBeingAttacked.Inventory</c> (line 776);
///   - the VR laser jittering across the docked toggle row fires the game's
///     <c>OnMouseEnter*/OnMouseExit*</c> hover handlers dozens of times a second →
///     <c>Preview*/ResetPreviewing/ClearSelectedToggle</c> → <c>ShowDamageTooltip</c> →
///     <c>get_IsLethalDamage</c> derefs the null actor (line 171) → NRE.
/// Each NRE is caught by the panel's own try/catch, which pops the game error dialog
/// ("Leider ist ein Fehler aufgetreten") and bails to the main-menu error handler —
/// the deadlock the user saw. The SAME hover thrash, while the panel is still live,
/// flip-flops the LoseCard preview fan (hand↔discard) so the burn pick never settles.
///
/// FIX (option (c) — a SAFE presentation that never NREs; TakeDamagePanel is GH.Runtime
/// UI, NOT the rules engine, so a Harmony guard is in-bounds). Two airtight nets, both
/// gated on VR conversion being active so vanilla behaviour is byte-identical when the
/// mod is off:
///   1. LIVENESS GUARD — every widget-reachable entry point is prefixed to run only
///      while the panel is LOGICALLY LIVE (<c>actorBeingAttacked != null</c>, exactly
///      the window between <c>Show</c> assigning it and <c>ResetAndHide</c> nulling it).
///      A stale/duplicated/hover event on a dead panel is swallowed (change-deduped
///      log) instead of NREing. The panel's own state is never touched — this keeps it
///      LIVE through the whole burn flow and only ignores events that arrive after it
///      already closed.
///   2. HOVER STAND-DOWN — while the row is docked on the board
///      (<see cref="DecisionDockSurface.DockingTakeDamage"/>) the mouse-hover preview
///      handlers are skipped entirely: VR drives the preview through the deliberate
///      toggle CLICK (<c>BurnDiscardedCards(true) → PreviewDiscardedCards</c>), so the
///      jitter-driven hover previews are pure noise. This stops the fan thrash and
///      makes the two-discard pick usable.
///   3. LETHALITY BACKSTOP — <c>get_IsLethalDamage</c> returns false (never throws)
///      when the actor is null, so even an unforeseen caller on a dead panel is inert.
///
/// All members are reachable directly (GH.Runtime is publicized — see the .csproj);
/// <c>actorBeingAttacked</c> is the private field the getter derefs.
/// </summary>
[HarmonyPatch]
internal static class TakeDamagePanelSafety
{
    /// <summary>The panel is logically live iff it still holds the actor under attack.</summary>
    private static bool Live(TakeDamagePanel p) => p != null && p.actorBeingAttacked != null;

    /// <summary>Guards only bite while VR canvas conversion is active — vanilla otherwise.</summary>
    private static bool GuardsActive => WorldUIConfig.ConversionActive;

    // Change-dedup for the swallow log: (reason) — one line per distinct reason burst.
    private static string? _lastSwallow;

    /// <summary>
    /// Shared prefix decision: return true to RUN the original, false to SWALLOW the
    /// stale event. When guards are inactive (VR off) the original always runs.
    /// </summary>
    private static bool Allow(TakeDamagePanel p, string method)
    {
        if (!GuardsActive || Live(p))
        {
            _lastSwallow = null;
            return true;
        }
        if (_lastSwallow != method)
        {
            _lastSwallow = method;
            VRLog.Info("WorldUI", $"TAKE-DAMAGE SAFETY: swallowed stale '{method}' on a closed panel " +
                                  "(actorBeingAttacked already cleared by ResetAndHide) — no NRE, no error dialog.");
        }
        return false;
    }

    // ---- liveness guards on the widget-reachable entry points -----------------------------

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.TakeDamage))]
    private static bool GuardTakeDamage(TakeDamagePanel __instance)
    {
        if (!Allow(__instance, "TakeDamage"))
            return false;
        if (GuardsActive)
            AutoUseMandatoryActiveBonuses(__instance);
        return true;
    }

    // ---- mandatory active-bonus auto-use (MP test bug #10a — the "Receive Damage does
    //      nothing" deadlock) ------------------------------------------------------------------

    /// <summary>
    /// DEADLOCK (large MP test, LogOutput.log:12166+): every docked "Receive Damage" click
    /// reached the game (15+ delivered uGUI clicks) but <c>TakeDamagePanel.TakeDamage()</c>
    /// refused each one at <c>CanTakeDamage()</c> (TakeDamagePanel.cs:764/847): the actor had
    /// a MANDATORY active bonus showing (<c>ToggleIsOptional == false</c> — the dock
    /// diagnostic recorded <c>Mandatory Highlight [active=True]</c> on the widget), and the
    /// game refuses the confirm until every mandatory bonus is toggled in the 2D
    /// <c>UIActiveBonusBar</c>. That bar is part of the flat HUD the VR conversion hides —
    /// nothing converts it — so the player had NO way to toggle the bonus: a hard deadlock
    /// (the session log shows the panel still open at mod shutdown). NOT an authority
    /// problem: the panel opened via <c>Show()</c> (not <c>ShowOtherPlayer</c>), which the
    /// game only does for the controlling client, and <c>takeDamageButton.interactable</c>
    /// tracks <c>ThisPlayerHasTakeDamageControl</c>.
    ///
    /// FIX: when the player confirms, toggle the still-pending mandatory bonuses FOR them
    /// through the game's own click path — <c>UIUseSlot.Toggle()</c> on the bar slot, exactly
    /// what a flat-screen click on the bar does: it runs
    /// <c>ActiveBonus.ToggleActiveBonus(…, fromClick: true)</c> (the MP-synced
    /// <c>ScenarioRuleClient.ToggleActiveBonus</c> call) and the panel's own
    /// <c>ToggleActiveBonus</c> callback (updates <c>toggledActiveBonuses</c>/shield/damage
    /// preview). "Mandatory" means the flat game FORCES these clicks before accepting the
    /// confirm, so no player choice is removed. MP-safe by construction: it only runs on the
    /// client with take-damage control (proxy clients replay the toggles from the
    /// <c>ActiveBonusesToken</c> the original then sends), and the pending set mirrors
    /// <c>CanTakeDamage()</c> exactly (non-optional bonuses, minus prevent-only-if-lethal
    /// ones while the hit is non-lethal), recomputed after every toggle because a toggled
    /// shield can flip the lethality pruning and a prevent-damage bonus short-circuits via
    /// <c>preventAllDamage</c>. A bonus whose slot needs a manual option/element pick (its
    /// <c>Select()</c> won't complete programmatically) is logged and left alone — the
    /// original then refuses exactly like vanilla flat would, with the warning tooltip
    /// surfaced by <see cref="DamageTooltipSurface"/>.
    /// </summary>
    private static void AutoUseMandatoryActiveBonuses(TakeDamagePanel p)
    {
        try
        {
            if (p == null || p.actorBeingAttacked == null || !p.ThisPlayerHasTakeDamageControl)
                return; // proxies (ProxyTakeDamage) replay token toggles themselves
            UIActiveBonusBar? bar =
                Singleton<UIActiveBonusBar>.IsInitialized ? Singleton<UIActiveBonusBar>.Instance : null;
            if (bar == null)
                return;

            // Bounded: each pass toggles at most one bonus, then recomputes the pending set
            // from live panel state (addedShield / preventAllDamage move under our feet).
            for (int pass = 0; pass < 8; pass++)
            {
                if (p.preventAllDamage || p.CalculateCurrentDamage() == 0)
                    return; // CanTakeDamage() already passes — nothing to force
                bool nonLethal = p.actorBeingAttacked.Health + p.addedShield > 0;
                bool toggledOne = false;
                List<CActiveBonus> showing = bar.ShowingActiveBonuses;
                for (int i = 0; i < showing.Count; i++)
                {
                    CActiveBonus b = showing[i];
                    if (b == null || b.Ability == null || b.Ability.ActiveBonusData == null
                        || b.Ability.ActiveBonusData.ToggleIsOptional)
                        continue; // optional — the player may skip it, CanTakeDamage ignores it
                    if (nonLethal && b is CPreventDamageActiveBonus pd && pd.PreventOnlyIfLethal)
                        continue; // pruned by CanTakeDamage for non-lethal hits
                    UIUseActiveBonus? slot = bar.GetSlotForActiveBonus(b);
                    if (slot == null || slot.IsSelected())
                        continue; // already toggled (counts toward CanTakeDamage) or gone
                    string name = BonusName(b);
                    slot.Toggle(); // the game's own click path (MP-synced, updates the panel)
                    if (!slot.IsSelected())
                    {
                        VRLog.Warn("WorldUI",
                            $"TAKE-DAMAGE SAFETY: mandatory active bonus '{name}' needs a manual " +
                            "option/element pick its slot cannot make programmatically — leaving it; " +
                            "the game will refuse the confirm and show its mandatory-use warning.");
                        return;
                    }
                    VRLog.Info("WorldUI",
                        $"TAKE-DAMAGE SAFETY: auto-used MANDATORY active bonus '{name}' on the " +
                        "'Receive Damage' confirm (the 2D UIActiveBonusBar is unreachable in VR; " +
                        "the flat game forces this exact click before CanTakeDamage() accepts).");
                    toggledOne = true;
                    break; // recompute lethality/prevent state before the next pick
                }
                if (!toggledOne)
                    return; // no mandatory bonus pending — the confirm goes through
            }
        }
        catch (Exception e)
        {
            // Never let the helper break the confirm itself — worst case the original
            // refuses exactly as it did before this fix.
            VRLog.Error($"[WorldUI] TAKE-DAMAGE SAFETY: mandatory-bonus auto-use threw: {e}");
        }
    }

    /// <summary>Readable bonus name for the log (card name, then ability name, then type).</summary>
    private static string BonusName(CActiveBonus b)
    {
        try
        {
            if (b.BaseCard != null && !string.IsNullOrEmpty(b.BaseCard.Name))
                return b.BaseCard.Name;
            if (b.Ability != null && !string.IsNullOrEmpty(b.Ability.Name))
                return b.Ability.Name;
        }
        catch (Exception)
        {
            // fall through to the type name
        }
        return b.GetType().Name;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.BurnAvailableCard), new[] { typeof(bool) })]
    private static bool GuardBurnAvailable(TakeDamagePanel __instance) => Allow(__instance, "BurnAvailableCard");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.BurnDiscardedCards), new[] { typeof(bool) })]
    private static bool GuardBurnDiscarded(TakeDamagePanel __instance) => Allow(__instance, "BurnDiscardedCards");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.PreviewDamage))]
    private static bool GuardPreviewDamage(TakeDamagePanel __instance) => Allow(__instance, "PreviewDamage");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.PreviewAvailableCards))]
    private static bool GuardPreviewAvailable(TakeDamagePanel __instance) => Allow(__instance, "PreviewAvailableCards");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.PreviewDiscardedCards))]
    private static bool GuardPreviewDiscarded(TakeDamagePanel __instance) => Allow(__instance, "PreviewDiscardedCards");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.ResetPreviewing))]
    private static bool GuardResetPreviewing(TakeDamagePanel __instance) => Allow(__instance, "ResetPreviewing");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.ClearSelectedToggle))]
    private static bool GuardClearSelectedToggle(TakeDamagePanel __instance) => Allow(__instance, "ClearSelectedToggle");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), "ShowDamageTooltip")]
    private static bool GuardShowDamageTooltip(TakeDamagePanel __instance) => Allow(__instance, "ShowDamageTooltip");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), "UpdateTakeDamageOptionVisuals")]
    private static bool GuardUpdateTakeDamageOptionVisuals(TakeDamagePanel __instance) =>
        Allow(__instance, "UpdateTakeDamageOptionVisuals");

    // ---- hover stand-down while docked (also liveness-guarded) -----------------------------

    /// <summary>
    /// The six mouse-hover preview handlers: swallowed on a dead panel (liveness) AND
    /// while the row is docked on the board (VR jitter noise — the click drives the
    /// preview). Returns true to run the original only when both conditions clear.
    /// </summary>
    private static bool AllowHover(TakeDamagePanel p, string method)
    {
        if (GuardsActive && DecisionDockSurface.DockingTakeDamage)
            return false; // docked: hover previews are laser-jitter noise (task A)
        return Allow(p, method);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseEnterTakeDamage))]
    private static bool GuardEnterTakeDamage(TakeDamagePanel __instance) => AllowHover(__instance, "OnMouseEnterTakeDamage");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseExitTakeDamage))]
    private static bool GuardExitTakeDamage(TakeDamagePanel __instance) => AllowHover(__instance, "OnMouseExitTakeDamage");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseEnterBurnOne))]
    private static bool GuardEnterBurnOne(TakeDamagePanel __instance) => AllowHover(__instance, "OnMouseEnterBurnOne");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseExitBurnOne))]
    private static bool GuardExitBurnOne(TakeDamagePanel __instance) => AllowHover(__instance, "OnMouseExitBurnOne");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseEnterBurnTwo))]
    private static bool GuardEnterBurnTwo(TakeDamagePanel __instance) => AllowHover(__instance, "OnMouseEnterBurnTwo");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseExitBurnTwo))]
    private static bool GuardExitBurnTwo(TakeDamagePanel __instance) => AllowHover(__instance, "OnMouseExitBurnTwo");

    // ---- lethality backstop ---------------------------------------------------------------

    /// <summary>
    /// Last-resort guard on the property every stale path funnels through: when the
    /// actor is gone the panel is dead and the answer is meaningless — return false
    /// rather than dereference null. Only intercepts the null case; live evaluation is
    /// the game's own.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), "IsLethalDamage", MethodType.Getter)]
    private static bool GuardIsLethalDamage(TakeDamagePanel __instance, ref bool __result)
    {
        if (GuardsActive && __instance != null && __instance.actorBeingAttacked == null)
        {
            __result = false;
            return false;
        }
        return true;
    }
}
