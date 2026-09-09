using ScenarioRuleLibrary;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static class CardAppearanceMirror
{
    private sealed class Frame
    {
        internal CardAppearanceSnapshot? Previous, Current;
        internal readonly UseBarAnimationPlaybackClock Clock = new();
    }
    private static readonly Dictionary<int, Frame> Frames = new();
    private static readonly List<AbilityCardUI> Pile = new();
    internal static void Set(int playerId, CardAppearanceSnapshot snapshot)
    {
        if (!Frames.TryGetValue(playerId, out var frame)) Frames[playerId] = frame = new Frame();
        if (frame.Current != null && snapshot.SampleTime <= frame.Current.SampleTime) return;
        bool continuous = frame.Current != null && snapshot.SampleTime - frame.Current.SampleTime <= UseBarAnimationPlaybackClock.MaximumContinuousGap;
        frame.Previous = continuous ? frame.Current : snapshot; frame.Current = snapshot;
        if (!continuous) frame.Clock.Reset(snapshot.SampleTime, Time.unscaledTime);
    }
    internal static void Remove(int playerId) => Frames.Remove(playerId);
    internal static void Reset() { Frames.Clear(); Pile.Clear(); }
    internal static bool TryGet(int playerId, CPlayerActor? actor, CAbilityCard? card,
        out CardAppearanceState? previous, out CardAppearanceState? current, out float progress)
    {
        previous = current = null; progress = 1f;
        if (actor == null || card == null || !Frames.TryGetValue(playerId, out var frame) || frame.Current == null) return false;
        int actorId = NetFigures.StableActorId(actor);
        foreach (var state in frame.Current.States)
            if (state.ActorId == actorId && ReferenceEquals(Resolve(actor, state.FaceCode, state.ListCount), card)) { current = state; break; }
        if (current == null) return false;
        if (frame.Previous != null)
            foreach (var state in frame.Previous.States)
                if (state.ActorId == current.ActorId && state.FaceCode == current.FaceCode && state.ListCount == current.ListCount)
                { previous = state; break; }
        previous ??= current;
        frame.Clock.Advance(Time.unscaledTime, frame.Current.SampleTime);
        progress = frame.Clock.Progress(frame.Previous!.SampleTime, frame.Current.SampleTime);
        return true;
    }
    internal static CAbilityCard? Resolve(CPlayerActor actor, byte code, byte count)
    {
        int seat = NetProtocol.HeldFaceIndex(code); byte list = NetProtocol.HeldFaceList(code);
        if (seat >= count) return null;
        if (list == NetProtocol.HeldFaceListActive) return RemoteHeldCardFace.TryPeekActiveSeat(actor, code, count);
        if (list == CardPlumeState.RoundList)
        {
            var cards = actor.CharacterClass?.RoundAbilityCards;
            return cards != null && cards.Count == count ? cards[seat] : null;
        }
        CardsHandUI? hand = CardsHandManager.Instance != null ? CardsHandManager.Instance.GetHand(actor) : null;
        if (hand == null) return null;
        if (list == NetProtocol.HeldFaceListBurnt || list == NetProtocol.HeldFaceListDiscard)
        {
            CardsGameApi.GetPileArcWidgets(hand, list == NetProtocol.HeldFaceListBurnt, Pile);
            return Pile.Count == count ? Pile[seat]?.AbilityCard : null;
        }
        if (list != NetProtocol.HeldFaceListHand || hand.cardsUI == null) return null;
        int n = 0; CAbilityCard? found = null;
        foreach (var widget in hand.cardsUI)
            if (CardsGameApi.HandFanMember(widget, actor)) { if (n == seat) found = widget.AbilityCard; n++; }
        return n == count ? found : null;
    }
}
