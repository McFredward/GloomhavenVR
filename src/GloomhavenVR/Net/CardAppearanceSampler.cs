using ScenarioRuleLibrary;
using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static class CardAppearanceSampler
{
    private static readonly List<VRCard> Cards = new();
    private static readonly Dictionary<FullAbilityCard, CardAppearanceBindings> Bindings = new();
    private static readonly HashSet<FullAbilityCard> Seen = new();
    private static readonly List<FullAbilityCard> Removed = new();
    private static CardAppearanceState[] _previous = Array.Empty<CardAppearanceState>();
    internal static CardAppearanceState[] Sample()
    {
        CardsDriver.CopyVisibleCards(Cards); Seen.Clear();
        var states = new List<CardAppearanceState>();
        foreach (VRCard card in Cards)
        {
            FullAbilityCard? full = card.FullCard;
            CPlayerActor? actor = card.GameCard?.PlayerActor;
            if (full == null || full.cardEffects == null || actor == null) continue;
            bool local = CardsGameApi.LocalControlsActor(actor, out bool known);
            if (!local && (known || !actor.IsUnderMyControl)) continue;
            LocalRigSampler.NameCard(actor, card, out byte code, out byte count);
            if (code == 0 && card.GameCard?.AbilityCard != null)
            {
                var round = actor.CharacterClass?.RoundAbilityCards;
                int seat = round != null ? round.IndexOf(card.GameCard.AbilityCard) : -1;
                if (seat >= 0 && seat < NetProtocol.HeldFaceIndexUnknown && round!.Count <= 255)
                { code = (byte)((CardPlumeState.RoundList << NetProtocol.HeldFaceListShift) | seat); count = (byte)round.Count; }
            }
            if (code == 0) continue;
            Seen.Add(full);
            if (!Bindings.TryGetValue(full, out var binding)) Bindings[full] = binding = new CardAppearanceBindings(full.cardEffects);
            var state = new CardAppearanceState { ActorId = NetFigures.StableActorId(actor), FaceCode = code, ListCount = count, Nodes = binding.Capture() };
            if (!state.Validate()) throw new InvalidOperationException("Native card appearance is outside the bounded wire domain.");
            states.Add(state);
        }
        Removed.Clear(); foreach (var full in Bindings.Keys) if (!Seen.Contains(full)) Removed.Add(full);
        foreach (var full in Removed) Bindings.Remove(full);
        if (states.Count > CardAppearanceState.CountMax) throw new InvalidOperationException("Native card appearance population exceeds wire bound.");
        states.Sort((a, b) => a.ActorId != b.ActorId ? a.ActorId.CompareTo(b.ActorId) : a.FaceCode.CompareTo(b.FaceCode));
        bool same = states.Count == _previous.Length;
        for (int i = 0; same && i < states.Count; i++) same = CardAppearanceState.Same(states[i], _previous[i]);
        if (!same) _previous = states.ToArray();
        return _previous;
    }
    internal static void Reset() { CardAppearanceBindings.ResetAssets(); Cards.Clear(); Bindings.Clear(); Seen.Clear(); Removed.Clear(); _previous = Array.Empty<CardAppearanceState>(); }
}
