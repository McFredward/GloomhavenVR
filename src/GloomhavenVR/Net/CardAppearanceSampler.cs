using ScenarioRuleLibrary;
using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static partial class CardAppearanceSampler
{
    private static readonly List<VRCard> Cards = new();
    private static readonly List<AbilityCardUI> Pile = new();
    private static readonly List<CardAppearanceState> States = new();
    private static readonly Dictionary<FullAbilityCard, CardAppearanceCapture> Bindings = new();
    private static readonly HashSet<FullAbilityCard> Seen = new();
    private static readonly List<FullAbilityCard> Removed = new();
    private static readonly Dictionary<FullAbilityCard, string> Failures = new();
    private static CardAppearanceState[] _previous = Array.Empty<CardAppearanceState>();
    internal static CardAppearanceState[] Sample()
    {
        using var scope = PerfMonitor.Scope("Net.CardAppearance.Sample");
        CardsDriver.CopyVisibleCards(Cards); Seen.Clear();
        var states = States; states.Clear();
        foreach (VRCard card in Cards)
        {
            FullAbilityCard? full = card.FullCard;
            CPlayerActor? actor = card.GameCard?.PlayerActor;
            if (full == null || full.cardEffects == null || actor == null) continue;
            // CopyVisibleCards enumerates this board's real adopted VRCard widgets, never
            // received RemoteCardArt clones. A foreign character's visible card is still part
            // of this board owner's picture; gameplay control is not presentation authority.
            LocalRigSampler.NameCard(actor, card, out byte code, out byte count);
            if (code == 0 && card.GameCard?.AbilityCard != null)
            {
                var round = actor.CharacterClass?.RoundAbilityCards;
                int seat = round != null ? round.IndexOf(card.GameCard.AbilityCard) : -1;
                if (seat >= 0 && seat < NetProtocol.HeldFaceIndexUnknown && round!.Count <= 255)
                { code = (byte)((CardPlumeState.RoundList << NetProtocol.HeldFaceListShift) | seat); count = (byte)round.Count; }
            }
            if (code == 0 && card.GameCard?.AbilityCard != null)
            {
                CardsHandUI? hand = CardsHandManager.Instance?.GetHand(actor);
                if (hand != null)
                {
                    CardsGameApi.GetPileArcWidgets(hand, burnt: false, Pile);
                    if (!CardAppearanceAddress.TryPile(Pile, card.GameCard.AbilityCard,
                        widget => widget?.AbilityCard, burnt: false, out code, out count))
                    {
                        CardsGameApi.GetPileArcWidgets(hand, burnt: true, Pile);
                        CardAppearanceAddress.TryPile(Pile, card.GameCard.AbilityCard,
                            widget => widget?.AbilityCard, burnt: true, out code, out count);
                    }
                }
            }
            if (code == 0) continue;
            Seen.Add(full);
            try
            {
                card.PreserveSpentBurnAppearance();
                if (!Bindings.TryGetValue(full, out var capture)) Bindings[full] = capture = new CardAppearanceCapture(full.cardEffects);
                var state = capture.Candidate;
                state.ActorId = NetFigures.StableActorId(actor); state.FaceCode = code; state.ListCount = count;
                state.Nodes = capture.Bindings.Capture(); state.ExtraGroups = capture.Bindings.CaptureExtraGroups();
                // A late frame must not attach its material output to another card which has since
                // occupied this same list seat. The original class pool is stable across moves/rests.
                if (card.GameCard?.AbilityCard == null
                    || !CardAppearanceProvenance.Capture(state, actor, card.GameCard.AbilityCard)) continue;
                states.Add(capture.Publish());
                Failures.Remove(full);
            }
            catch (Exception ex)
            {
                // An unpublishable decoration must neither interrupt original card construction
                // nor amputate every other card's frame. Keep the exact failure visible, once per
                // changed condition, and retry the current graph next sample without truncating it.
                if (!Failures.TryGetValue(full, out string previousFailure) || previousFailure != ex.Message)
                {
                    Failures[full] = ex.Message;
                    VRLog.Warn("Net", $"Native card appearance unavailable for actor {NetFigures.StableActorId(actor)}, "
                        + $"seat {code}: {ex.Message}; original artwork remains independent.");
                }
            }
        }
        Removed.Clear(); foreach (var full in Bindings.Keys) if (!Seen.Contains(full)) Removed.Add(full);
        foreach (var full in Removed) { Bindings.Remove(full); Failures.Remove(full); }
        Removed.Clear(); foreach (var full in Failures.Keys) if (!Seen.Contains(full)) Removed.Add(full);
        foreach (var full in Removed) Failures.Remove(full);
        AppendBurnFinals(states);
        if (states.Count > CardAppearanceState.CountMax) throw new InvalidOperationException("Native card appearance population exceeds wire bound.");
        states.Sort((a, b) => a.ActorId != b.ActorId ? a.ActorId.CompareTo(b.ActorId) : a.FaceCode.CompareTo(b.FaceCode));
        bool same = states.Count == _previous.Length;
        for (int i = 0; same && i < states.Count; i++) same = ReferenceEquals(states[i], _previous[i]) || CardAppearanceState.Same(states[i], _previous[i]);
        if (!same) _previous = states.ToArray();
        return _previous;
    }
    internal static void Reset() { BurnFinals.Clear(); _finalCapacityLogged = false; _finalPageStart = 0; _nextFinalPageAt = 0f; CardAppearanceBindings.ResetAssets(); Cards.Clear(); Pile.Clear(); States.Clear(); Bindings.Clear(); Seen.Clear(); Removed.Clear(); Failures.Clear(); _previous = Array.Empty<CardAppearanceState>(); }
}
