using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.Events;

namespace GloomhavenVR.Cards.Patches;

// ---------------------------------------------------------------------------
// Damage-negation / burn flow hardening (tasks #11 + #10).
//
// The 2D game keeps its lose/discard commit safe by CONSTRUCTION: the
// "Karten verbrennen" DialogPopup only opens the moment
// selectedCardsUI.Count == maxCardsSelected, and while it is open every card is
// locked unselectable (CardsHandUI.OnCardSelected, CardsHandUI.cs:2029-2119) —
// so OnLoseCardClick may blindly index selectedCardsUI[0]/[1]
// (GameState.Lose2DiscardCardsToAvoidAttack(..., selectedCardsUI[0].AbilityCard,
// selectedCardsUI[1].AbilityCard), CardsHandUI.cs:2344). VR free card placement
// broke that invariant (cards plucked back while the popup showed); the commit
// then threw, the catch block routed into GlobalErrorMessage /
// ErrorHandlingUnloadSceneAndLoadMainMenu, and the scenario was torn down
// (EndScenarioSafely — the reproduced "crash to menu", Player.log 59952-60089).
// CardsDriver now cancels the popup through the game's own "choose another
// card" path the instant a pick card is grabbed; the prefix below is the
// DEFENSIVE last line: the commit REFUSES an incomplete selection instead of
// forwarding it into the game. Local UI only — no game state is faked, nothing
// is sent on the wire when the gate refuses.
// ---------------------------------------------------------------------------

/// <summary>
/// Task #11 (c): honest commit gate. Verified target: <c>private void
/// OnLoseCardClick()</c> (CardsHandUI.cs:2295) — the ONLY commit callback of the
/// lose/discard confirm popup (wired at CardsHandUI.cs:2085) and of the
/// ability-driven lose/discard flows. Mirrors the 2D invariant exactly: the
/// commit is legal only with <c>selectedCardsUI.Count == maxCardsSelected</c>.
/// On refusal the take-damage row is brought back so the player is never
/// stranded without an affordance.
/// </summary>
[HarmonyPatch(typeof(CardsHandUI), "OnLoseCardClick")]
internal static class CardsHandUI_OnLoseCardClick_Gate
{
    private static bool Prefix(CardsHandUI __instance)
    {
        int need = __instance.maxCardsSelected; // private, publicized
        int have = __instance.SelectedCards != null ? __instance.SelectedCards.Count : 0;
        if (need <= 0 || have >= need)
            return true; // complete selection — run the game's commit unchanged

        VRLog.Warn("Cards", $"Burn/lose commit REFUSED: {have}/{need} card(s) selected — the game " +
                            "would have indexed an incomplete selection (the crash-to-menu path). " +
                            "Selection stays open; place the required card(s) and confirm again.");
        try
        {
            // The popup already hid itself before this callback — restore the damage
            // row so the flow visibly continues (the cancel path does the same).
            Singleton<TakeDamagePanel>.Instance?.ToggleVisibility(visible: true);
        }
        catch (System.Exception ex)
        {
            VRLog.Debug("Cards", $"Commit-gate panel restore skipped: {ex.GetType().Name}.");
        }
        return false; // skip the game's commit — LOCAL refusal only
    }
}

// ---------------------------------------------------------------------------
// Task #10: hover-triggered burn theatrics.
//
// Two game paths animate big on a mere pointer HOVER, which the VR laser
// triggers constantly while aiming across the docked rows:
//
// 1. TakeDamagePanel wires its burn toggles' mouse enter/exit to
//    PreviewAvailableCards/PreviewDiscardedCards (TakeDamagePanel.cs:466-512)
//    — each hover SHOWS the whole card hand (CardsHandManager.Show), each
//    un-hover hides it: in VR the entire fan bursts open/closed while the beam
//    sweeps the row. The CLICK path (BurnAvailableCard/BurnDiscardedCards →
//    OnSelectedToggle → Preview*) is untouched — a deliberate toggle still
//    previews the cards.
//
// 2. The card-content DialogPopup options carry onMouseEnter/onMouseExit
//    actions that run the card's burn/ghost dissolve timeline both ways
//    (CardEffects.ToggleAdditiveEffect → BurnCard(burnAnim)/GhostOutOn —
//    BOTH directions start a full-card coroutine timeline, CardEffects.cs:404-446;
//    wired via DialogPopup.Show's onMouseEnter/onMouseExit listeners,
//    DialogPopup.cs:212-219). Every laser pass over "Karten verbrennen" churned
//    a theatrical dissolve on the dock card (the repeated "Burn/ghost effect
//    playing" spam, Player.log 59884-59907).
//
// Both hover paths are suppressed; the ACTUAL burn (commit → AnimateCardsLost /
// FinalizeShortRest / TryPlayBurnAnimation) still plays on the dock card, where
// BurnCardFx bounds the world-space smoke card-local (×0.35 size/speed) — the
// retained, card-sized burn effect.
// ---------------------------------------------------------------------------

/// <summary>Skip the burn toggles' hover-preview (enter) handlers — VR laser hover
/// must not open the full hand preview. Clicks still preview.</summary>
[HarmonyPatch]
internal static class TakeDamagePanel_BurnHover_Skip
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseEnterBurnOne))]
    private static bool SkipEnterOne() => false;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseEnterBurnTwo))]
    private static bool SkipEnterTwo() => false;

    // The exits are skipped symmetrically: with the enters gone, ResetPreviewing on
    // exit would still re-run BurnAvailableCard/BurnDiscardedCards for a toggled
    // option (TakeDamagePanel.ResetPreviewing:648-668) — a hidden hover side effect.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseExitBurnOne))]
    private static bool SkipExitOne() => false;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TakeDamagePanel), nameof(TakeDamagePanel.OnMouseExitBurnTwo))]
    private static bool SkipExitTwo() => false;
}

/// <summary>
/// Strip the hover (mouse enter/exit) listeners off the card-content dialog's
/// option buttons right after the game wires them — they only drive the
/// burn/ghost dissolve churn described above. Target: the List overload
/// <c>public void Show(List&lt;GameObject&gt; content, DialogOption[] options,
/// bool allowHide, int cancelOption, UnityAction cancelAction)</c>
/// (DialogPopup.cs:157) — the single-GameObject overload delegates to it, and
/// the string-content overloads (plain text dialogs) are left untouched.
/// Click/keyboard/controller activation of every option is unaffected.
/// </summary>
[HarmonyPatch(typeof(DialogPopup), nameof(DialogPopup.Show),
    typeof(List<GameObject>), typeof(DialogOption[]), typeof(bool), typeof(int), typeof(UnityAction))]
internal static class DialogPopup_Show_HoverStrip
{
    private static bool s_logged;

    private static void Postfix(DialogPopup __instance)
    {
        List<InputButton> buttons = __instance.optionButtons; // private, publicized
        if (buttons == null)
            return;
        for (int i = 0; i < buttons.Count; i++)
        {
            InputButton button = buttons[i];
            if (button == null || button.ExtendedButton == null)
                continue;
            button.ExtendedButton.onMouseEnter.RemoveAllListeners();
            button.ExtendedButton.onMouseExit.RemoveAllListeners();
        }
        if (!s_logged)
        {
            s_logged = true;
            VRLog.Info("Cards", "Dialog option hover FX stripped (task #10): laser hover over the " +
                                "docked burn/lose choice no longer runs the card dissolve timeline; " +
                                "the real burn still plays card-sized on the dock (BurnCardFx).");
        }
    }
}
