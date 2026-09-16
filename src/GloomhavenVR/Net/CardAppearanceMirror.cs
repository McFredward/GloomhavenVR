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
        internal CardAppearanceBinding<CAbilityCard>[] PreviousCards = System.Array.Empty<CardAppearanceBinding<CAbilityCard>>(),
            CurrentCards = System.Array.Empty<CardAppearanceBinding<CAbilityCard>>();
        internal readonly UseBarAnimationPlaybackClock Clock = new();
        internal readonly SpentAppearanceHistory<CAbilityCard, CardAppearanceState> Spent = new();
        internal readonly Dictionary<CAbilityCard, (int Actor, float Time, CardAppearanceState Source)> Presented = new(CardReferenceComparer.Instance);
        internal readonly List<CAbilityCard> Retired = new();
    }
    private sealed class CardReferenceComparer : IEqualityComparer<CAbilityCard>
    {
        internal static readonly CardReferenceComparer Instance = new();
        public bool Equals(CAbilityCard? x, CAbilityCard? y) => ReferenceEquals(x, y);
        public int GetHashCode(CAbilityCard value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
    private static readonly Dictionary<int, Frame> Frames = new();
    private static readonly List<AbilityCardUI> Pile = new();
    internal static void Set(int playerId, CardAppearanceSnapshot snapshot)
    {
        if (!Frames.TryGetValue(playerId, out var frame)) Frames[playerId] = frame = new Frame();
        if (frame.Current != null && snapshot.SampleTime <= frame.Current.SampleTime) return;
        bool continuous = frame.Current != null && snapshot.SampleTime - frame.Current.SampleTime <= UseBarAnimationPlaybackClock.MaximumContinuousGap;
        frame.Spent.RemoveRecovered((actorId, card) => Recovered(RemoteBoardFocus.ActorById(actorId), card));
        frame.Retired.Clear();
        foreach (var presented in frame.Presented)
            if (Recovered(RemoteBoardFocus.ActorById(presented.Value.Actor), presented.Key)
                || !ReferenceEquals(CardAppearanceProvenance.Resolve(presented.Value.Source), presented.Key))
                frame.Retired.Add(presented.Key);
        foreach (var card in frame.Retired) frame.Presented.Remove(card);
        var bindings = new CardAppearanceBinding<CAbilityCard>[snapshot.States.Length];
        for (int i = 0; i < bindings.Length; i++)
        {
            var state = snapshot.States[i];
            CPlayerActor? actor = RemoteBoardFocus.ActorById(state.ActorId);
            CAbilityCard? card = actor != null ? Resolve(actor, state.FaceCode, state.ListCount) : null;
            // Legacy samples have no provenance. Production frames carry an immutable roster
            // address, so even an already-delayed same-count replacement cannot adopt old paint.
            if (state.SourceActorId != 0 && !ReferenceEquals(card, CardAppearanceProvenance.Resolve(state))) card = null;
            bindings[i] = new CardAppearanceBinding<CAbilityCard>(card);
            byte list = NetProtocol.HeldFaceList(state.FaceCode);
            if (card != null && state.SourceActorId != 0 && Recovered(actor, card)
                && (list == NetProtocol.HeldFaceListHand || list == CardPlumeState.RoundList || list == NetProtocol.HeldFaceListActive))
                CardFlightVisibility.CancelOwnerReleaseBefore(card, snapshot.SampleTime);
            // This history is independent of mutable seats, but requires original immutable
            // provenance and a real owner observation. Legacy/unresolved samples cannot seed it.
            if (card != null && state.SourceActorId != 0 && !Recovered(actor, card)
                && (list == NetProtocol.HeldFaceListDiscard || list == NetProtocol.HeldFaceListActive))
                frame.Spent.Remember(state.ActorId, card, state);
        }
        frame.PreviousCards = continuous ? frame.CurrentCards : bindings;
        frame.CurrentCards = bindings;
        frame.Previous = continuous ? frame.Current : snapshot; frame.Current = snapshot;
        if (!continuous) frame.Clock.Reset(snapshot.SampleTime, Time.unscaledTime);
    }
    internal static bool OwnerBurnInProgress(CPlayerActor? actor)
        => actor != null && NetAvatarDriver.TryGetCharacterDecisionOwner(actor, out RemoteAvatar? owner)
            && owner != null && owner.HasBurnInProgress(NetFigures.StableActorId(actor));

    internal static void Remove(int playerId) => Frames.Remove(playerId);
    internal static void Reset() { Frames.Clear(); Pile.Clear(); }
    internal static bool TryGet(int playerId, CPlayerActor? actor, CAbilityCard? card,
        out CardAppearanceState? previous, out CardAppearanceState? current, out float progress)
    {
        previous = current = null; progress = 1f;
        if (actor == null || card == null || !Frames.TryGetValue(playerId, out var frame) || frame.Current == null) return false;
        int actorId = NetFigures.StableActorId(actor);
        // A seat is a receive-time address, not a permanent name. Once its population changes,
        // this sample is invalid forever, even if that old card later returns to the same seat.
        for (int i = 0; i < frame.Current.States.Length; i++)
        {
            var state = frame.Current.States[i];
            if (state.ActorId != actorId || !frame.CurrentCards[i].Matches(card,
                Resolve(actor, state.FaceCode, state.ListCount))) continue;
            current = state;
            break;
        }
        if (current == null) return false;
        if (frame.Previous != null)
            for (int i = 0; i < frame.Previous.States.Length; i++)
            {
                var state = frame.Previous.States[i];
                if (state.ActorId == current.ActorId && state.FaceCode == current.FaceCode && state.ListCount == current.ListCount
                    && frame.PreviousCards[i].Matches(card, Resolve(actor, state.FaceCode, state.ListCount)))
                { previous = state; break; }
            }
        previous ??= current;
        frame.Clock.Advance(Time.unscaledTime, frame.Current.SampleTime);
        progress = frame.Clock.Progress(frame.Previous!.SampleTime, frame.Current.SampleTime);
        // A completed picture stays acknowledged when retained final pages rotate or a later
        // unrelated card changes the packet identity. Recovery/provenance invalidation clears it.
        CardAppearanceState presented = ReferenceEquals(previous, current) || progress >= 1f ? current : previous;
        float presentedTime = ReferenceEquals(presented, current) ? frame.Current.SampleTime : frame.Previous.SampleTime;
        if (presented.SourceActorId != 0 && ReferenceEquals(CardAppearanceProvenance.Resolve(presented), card)
            && (!frame.Presented.TryGetValue(card, out var known) || known.Time < presentedTime)
            && (frame.Presented.ContainsKey(card) || frame.Presented.Count < CardBurnCompletionHistory.CountMax))
            frame.Presented[card] = (actorId, presentedTime, presented);
        return true;
    }
    // Cancellation evidence only. A recovered card no longer has a valid Lost-list address,
    // but a post-release owner sample in a recovered population proves the old burn ended. Never
    // use this weaker test to authorize a flight or to paint a recovered card's old materials.
    internal static bool HasRecoveredSourceAfter(int playerId, CPlayerActor? actor, CAbilityCard? card, float completionTime)
    {
        if (completionTime < 0f || actor == null || card == null || !Frames.TryGetValue(playerId, out var frame)
            || frame.Current == null || frame.Current.SampleTime < completionTime) return false;
        int actorId = NetFigures.StableActorId(actor);
        foreach (var state in frame.Current.States)
            if (state.ActorId == actorId && state.SourceActorId != 0
                && (NetProtocol.HeldFaceList(state.FaceCode) == NetProtocol.HeldFaceListHand
                    || NetProtocol.HeldFaceList(state.FaceCode) == CardPlumeState.RoundList
                    || NetProtocol.HeldFaceList(state.FaceCode) == NetProtocol.HeldFaceListActive)
                && ReferenceEquals(card, CardAppearanceProvenance.Resolve(state))) return true;
        return false;
    }

    internal static bool HasPresentedThrough(int playerId, CPlayerActor? actor, CAbilityCard? card, float completionTime)
    {
        if (completionTime < 0f) return true; // old senders have no cross-stream watermark
        if (actor != null && card != null && Frames.TryGetValue(playerId, out var acknowledged)
            && acknowledged.Presented.TryGetValue(card, out var shown) && shown.Actor == NetFigures.StableActorId(actor)
            && shown.Time >= completionTime && !Recovered(actor, card)
            && ReferenceEquals(CardAppearanceProvenance.Resolve(shown.Source), card)) return true;
        if (actor == null || card == null || !Frames.TryGetValue(playerId, out var frame)
            || frame.Current == null || frame.Current.SampleTime < completionTime
            || !TryGet(playerId, actor, card, out var previous, out var current, out float progress)) return false;
        // TryGet already validates the receive-time binding against the live population. The
        // immutable roster additionally excludes same-seat reuse after rest or a pooled rebuild.
        if (current == null || current.SourceActorId == 0
            || !ReferenceEquals(card, CardAppearanceProvenance.Resolve(current))) return false;
        // If both sides are the same native output, it is already painted even at progress zero.
        // Otherwise the old side must itself be causally after the release, or its interpolation
        // must finish. Arrival order and the receiver's inactive native coroutine prove nothing.
        return ReferenceEquals(previous, current) || progress >= 1f
            || frame.Previous != null && frame.Previous.SampleTime >= completionTime;
    }

    private static bool Recovered(CPlayerActor? actor, CAbilityCard card)
    {
        var cards = actor?.CharacterClass;
        return cards == null || cards.HandAbilityCards.Contains(card) || cards.RoundAbilityCards.Contains(card);
    }
    internal static bool TryGetLastSpentFrame(int playerId, CPlayerActor? actor, CAbilityCard? card,
        out CardAppearanceState? state)
    {
        state = null;
        var cards = actor?.CharacterClass;
        if (cards == null || card == null || !Frames.TryGetValue(playerId, out var frame)) return false;
        bool lost = cards.LostAbilityCards.Contains(card) || cards.PermanentlyLostAbilityCards.Contains(card);
        bool recovered = Recovered(actor, card) || cards.ActivatedCards.Contains(card) && lost;
        if (!frame.Spent.TryGet(NetFigures.StableActorId(actor!), card, lost, recovered, out state)) return false;
        // The immutable source roster may itself have been rebuilt; never reuse its old picture.
        if (state == null || !ReferenceEquals(card, CardAppearanceProvenance.Resolve(state)))
        { state = null; return false; }
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
