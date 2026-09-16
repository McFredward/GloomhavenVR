using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static partial class CardAppearanceSampler
{
    private sealed class BurnFinal
    {
        internal CPlayerActor Actor = null!;
        internal CAbilityCard Card = null!;
        internal CardAppearanceState State = null!;
    }
    private static readonly List<BurnFinal> BurnFinals = new(CardAppearanceState.CountMax);
    private static bool _finalCapacityLogged;

    /// <summary>Keep the actual completed output publishable after its VR wrapper leaves.
    /// The release event names this same owner clock; a receiver must render through it first.</summary>
    internal static float RetainBurnCompletion(AbilityCardUI widget, FullAbilityCard? full)
    {
        full ??= widget != null ? widget.fullAbilityCard : null;
        CPlayerActor? actor = widget != null ? widget.PlayerActor : null;
        CAbilityCard? card = widget != null ? widget.AbilityCard : null;
        if (full == null || full.cardEffects == null || actor == null || card == null) return -1f;
        try
        {
            var capture = new CardAppearanceCapture(full.cardEffects);
            CardAppearanceState state = capture.Candidate;
            state.ActorId = NetFigures.StableActorId(actor);
            state.Nodes = capture.Bindings.Capture(); state.ExtraGroups = capture.Bindings.CaptureExtraGroups();
            if (!CardAppearanceProvenance.Capture(state, actor, card) || !TryBurnAddress(actor, card, out byte code, out byte count)) return -1f;
            state.FaceCode = code; state.ListCount = count;
            if (!state.Validate()) return -1f;
            state = state.Copy();
            for (int i = BurnFinals.Count - 1; i >= 0; i--)
                if (ReferenceEquals(BurnFinals[i].Card, card)) BurnFinals.RemoveAt(i);
            if (BurnFinals.Count == CardAppearanceState.CountMax) BurnFinals.RemoveAt(0);
            BurnFinals.Add(new BurnFinal { Actor = actor, Card = card, State = state });
            return Time.unscaledTime;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Net", $"Native burn completion capture unavailable: {ex.Message}; no completion timestamp invented.");
            return -1f;
        }
    }

    private static bool TryBurnAddress(CPlayerActor actor, CAbilityCard card, out byte code, out byte count)
    {
        code = count = 0;
        var cards = actor != null ? actor.CharacterClass : null;
        if (cards == null || (!cards.LostAbilityCards.Contains(card) && !cards.PermanentlyLostAbilityCards.Contains(card))
            || cards.HandAbilityCards.Contains(card) || cards.RoundAbilityCards.Contains(card)) return false;
        CardsHandUI? hand = CardsHandManager.Instance != null ? CardsHandManager.Instance.GetHand(actor) : null;
        if (hand == null) return false;
        CardsGameApi.GetPileArcWidgets(hand, burnt: true, Pile);
        return CardAppearanceAddress.TryPile(Pile, card, widget => widget?.AbilityCard, burnt: true, out code, out count);
    }

    private static void AppendBurnFinals(List<CardAppearanceState> states)
    {
        bool full = false;
        for (int i = BurnFinals.Count - 1; i >= 0; i--)
        {
            BurnFinal final = BurnFinals[i];
            if (final.Actor == null || !ReferenceEquals(CardAppearanceProvenance.Resolve(final.State), final.Card)
                || !TryBurnAddress(final.Actor, final.Card, out byte code, out byte count))
            { BurnFinals.RemoveAt(i); continue; }
            if (final.State.FaceCode != code || final.State.ListCount != count)
            {
                var relocated = final.State.Copy();
                relocated.FaceCode = code; relocated.ListCount = count; final.State = relocated;
            }
            bool present = false;
            foreach (var live in states)
                if (live.ActorId == final.State.ActorId && live.SourceActorId == final.State.SourceActorId
                    && live.PoolSeat == final.State.PoolSeat && live.PoolCount == final.State.PoolCount)
                { present = true; break; }
            if (present) continue; // The still-visible original always supplies its live pixels.
            if (states.Count >= CardAppearanceState.CountMax) { full = true; continue; }
            states.Add(final.State);
        }
        if (full && !_finalCapacityLogged)
            VRLog.Warn("Net", "Native burn completion capture pending: live card snapshot occupies every protocol seat; retained completion retries without dropping live output.");
        _finalCapacityLogged = full;
    }
}
