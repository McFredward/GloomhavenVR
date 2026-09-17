using System.Collections.Generic;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Cards;

internal sealed partial class CardsDriver
{
    private readonly List<CAbilityCard> _recoveredBurnClaims = new(8);

    // MB517: widgets are pooled presentation objects, not burn episodes. Rebuilding a lost-pile
    // widget or briefly exposing an empty widget list used to forget an already completed burn
    // and start another hold/flight. Native card membership is the recovery authority; no absent
    // UI sample or elapsed-time ceiling may reset an episode or shorten its native artwork.
    private bool IsKnownBurn(AbilityCardUI widget) =>
        widget.AbilityCard != null && _knownBurntCards.Contains(widget.AbilityCard);

    private void RememberBurn(AbilityCardUI widget)
    {
        if (widget.AbilityCard != null) _knownBurntCards.Add(widget.AbilityCard);
    }

    private bool HasBurnHold(AbilityCardUI widget)
    {
        if (_burnHoldSince.ContainsKey(widget)) return true;
        if (widget.AbilityCard == null) return false;
        foreach (AbilityCardUI held in _burnHoldSince.Keys)
            if (held != null && ReferenceEquals(held.PlayerActor, widget.PlayerActor)
                && ReferenceEquals(held.AbilityCard, widget.AbilityCard)) return true;
        return false;
    }

    private void SeedKnownBurns(CardsHandUI hand)
    {
        CCharacterClass? character = hand.PlayerActor?.CharacterClass;
        if (character == null) return;
        foreach (CAbilityCard card in character.LostAbilityCards) _knownBurntCards.Add(card);
        foreach (CAbilityCard card in character.PermanentlyLostAbilityCards) _knownBurntCards.Add(card);
    }

    private void PruneRecoveredBurns(CardsHandUI hand)
    {
        CCharacterClass? character = hand.PlayerActor?.CharacterClass;
        if (character == null) return;
        _recoveredBurnClaims.Clear();
        foreach (CAbilityCard card in _knownBurntCards)
            if (!character.LostAbilityCards.Contains(card)
                && !character.PermanentlyLostAbilityCards.Contains(card)) _recoveredBurnClaims.Add(card);
        foreach (CAbilityCard card in _recoveredBurnClaims) _knownBurntCards.Remove(card);
        _recoveredBurnClaims.Clear();
    }
}
