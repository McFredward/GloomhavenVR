using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.CustomLevels;

namespace GloomhavenVR.Compat;

/// <summary>
/// CARD / ITEM NAMES for the VR tutorial hints — resolved from the GAME's own data at
/// DISPLAY time, never hardcoded.
///
/// WHY: the flat tutorial can get away with "play the requested card" because the flat UI
/// highlights that card for you. In VR the cards live in a hand fan the player opens
/// themselves, so a hint that does not NAME the card forces trial and error (user ruling
/// 2026-08: "'Die verlangte Karte' hinlegen ist zu unspezifisch"). Every VR hint that
/// demands a SPECIFIC card therefore names it.
///
/// SOURCE OF TRUTH (proven from the decompiled sources — the name is never invented here):
/// - A tutorial step that waits for a card carries the card in its own trigger:
///   <c>CLevelTrigger.EventTriggerContextId</c>. <c>LevelEventsController</c> matches that
///   string against the UIEvent's <c>ContextID</c> (LevelEventsController.cs:698-711), and
///   the producers put the CARD there — <c>CardsHandUI.cs:2136</c> and
///   <c>FullAbilityCard.cs:679</c> both log
///   <c>new UIEvent(&lt;type&gt;, actor, abilityCard.Name)</c>.
/// - <c>CBaseCard.Name</c> (CBaseCard.cs:52-82) is the YML <c>Name</c> field of the ability
///   OR item card — and that string is a LOCALIZATION TERM, not display text:
///   <c>FullAbilityCard.SetCardName</c> (FullAbilityCard.cs:847-850) renders the card's own
///   title as <c>LocalizationManager.GetTranslation(cardName)</c>. Feeding the same term
///   through <see cref="Loc.Game"/> (→ <c>GLOOM.LocalizationManager.TryGetTranslation</c>,
///   which retries with spaces/apostrophes stripped exactly like the trigger matcher) can
///   therefore only ever produce the SAME string that is printed on the card the player is
///   looking at — in the player's language, for every language the game ships.
/// - The BURNT card of the short-rest step is not in any trigger (the
///   <c>ShortRestChoseToBurn</c> UIEvent carries no context, CardsHandUI.cs:974), so it is
///   read from the authoritative pile instead: <c>CCharacterClass.LostAbilityCards</c>
///   (CCharacterClass.cs:104) — the very list <see cref="Cards.CardsGameApi.BurntCount"/>
///   counts for the VR burnt stack. Ambiguity (more than one lost card) is broken with the
///   card the tutorial FORCED to burn, recorded from its own <c>SetShortRestCard</c> level
///   event (<see cref="NoteLevelEvent"/>; LevelEventsController.cs:1069 stores that
///   <c>EventResource</c>, CardsHandUI.cs:757 picks that card out of the discard pile).
///
/// FALLBACK: every entry point returns false rather than a guess. Callers then keep the
/// generic wording and log a Warn naming the step, so a hardware log shows exactly which
/// hint went nameless. Read-only throughout; no game state is touched.
/// </summary>
internal static class TutorialCardNames
{
    /// <summary>UIEvent types whose <c>ContextID</c> IS a card (see the class header). Ints,
    /// because <c>CLevelTrigger</c> stores the event type as a raw int.</summary>
    private static readonly int[] CardContextEvents =
    {
        (int)UIEvent.EUIEventType.AbilityCardSelected,
        (int)UIEvent.EUIEventType.CardTopHalfSelected,
        (int)UIEvent.EUIEventType.CardBottomHalfSelected,
    };

    /// <summary>Card term the running tutorial forces the short rest to burn, from its own
    /// <c>SetShortRestCard</c> level event; empty when the scenario has no such event.</summary>
    private static string _shortRestCard = string.Empty;

    /// <summary>Scenario boundary — drop the recorded short-rest card (called from the flow dump).</summary>
    internal static void Reset() => _shortRestCard = string.Empty;

    /// <summary>
    /// Record a scripted level event's card reference while the flow dump walks the queue.
    /// Only <c>SetShortRestCard</c> is interesting: its <c>EventResource</c> is the card
    /// term the tutorial will burn (LevelEventsController.cs:1069-1070). The game CONSUMES
    /// and clears its own copy the moment the short rest resolves
    /// (<c>RunActionIfShortRestDataPending</c>, LevelEventsController.cs:1466-1472), which is
    /// long before the "your card is burnt" box appears — hence the mod's own copy.
    /// </summary>
    internal static void NoteLevelEvent(CLevelEvent? levelEvent)
    {
        if (levelEvent == null
            || levelEvent.EventType != CLevelEvent.ELevelEventType.SetShortRestCard
            || string.IsNullOrEmpty(levelEvent.EventResource))
            return;
        _shortRestCard = levelEvent.EventResource;
        VRLog.Info("Tutorial", $"This tutorial forces the short rest to burn '{_shortRestCard}' "
            + "(SetShortRestCard level event) — the VR burnt-card hint can name it.");
    }

    /// <summary>
    /// The localized card/item name a hint's own trigger refers to (see the class header).
    /// False — with no name — for triggers that carry no card, so the caller can keep its
    /// generic wording instead of printing a guess.
    /// </summary>
    internal static bool TryFromTrigger(CLevelTrigger? trigger, out string name)
    {
        name = string.Empty;
        try
        {
            if (trigger == null || trigger.IsTriggeredByDismiss || !trigger.IsUIEventTypeTrigger)
                return false;
            bool carriesCard = false;
            foreach (int type in CardContextEvents)
                if (trigger.EventTriggerTypeInt == type)
                {
                    carriesCard = true;
                    break;
                }
            if (!carriesCard || string.IsNullOrEmpty(trigger.EventTriggerContextId))
                return false;
            return TryLocalize(trigger.EventTriggerContextId, out name);
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", $"card-name lookup from trigger failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The localized name of the card that is lying on the BURNT pile right now. Unique by
    /// construction at the tutorial's burn step (the hardware log shows discard=0, burnt=1
    /// when that box opens); when several cards are lost the tutorial's own forced
    /// short-rest card breaks the tie. False when neither rule identifies exactly one card.
    /// </summary>
    internal static bool TryBurntCardName(out string name)
    {
        name = string.Empty;
        try
        {
            List<CPlayerActor>? players = ScenarioManager.Scenario?.PlayerActors;
            if (players == null)
                return false;

            string? only = null;
            string? forced = null;
            int lost = 0;
            for (int i = 0; i < players.Count; i++)
            {
                List<CAbilityCard>? cards = players[i]?.CharacterClass?.LostAbilityCards;
                if (cards == null)
                    continue;
                for (int c = 0; c < cards.Count; c++)
                {
                    string term = cards[c]?.Name ?? string.Empty;
                    if (string.IsNullOrEmpty(term))
                        continue;
                    lost++;
                    only = term;
                    if (!string.IsNullOrEmpty(_shortRestCard)
                        && string.Equals(term, _shortRestCard, StringComparison.OrdinalIgnoreCase))
                        forced = term;
                }
            }

            string? pick = lost == 1 ? only : forced;
            return !string.IsNullOrEmpty(pick) && TryLocalize(pick!, out name);
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", $"burnt-card name lookup failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Term → the card title in the player's language, or false when the game has no
    /// translation for it (never print the raw term — that would be an English-only name).</summary>
    private static bool TryLocalize(string term, out string name)
    {
        name = Loc.Game(term, string.Empty);
        return !string.IsNullOrEmpty(name);
    }
}
