using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static class ItemAppearanceMirror
{
    private sealed class Entry
    {
        internal CItem? Item;
        internal bool Rejected;
        internal ItemAppearanceState Current = null!, Previous = null!;
        internal float PreviousTime, CurrentTime, TerminalTime = -1, Presented = -1;
    }
    private sealed class Frame
    {
        internal float Time = -1;
        internal readonly Dictionary<ulong, Entry> Entries = new();
        internal readonly UseBarAnimationPlaybackClock Clock = new();
    }
    private static readonly Dictionary<int, Frame> Frames = new();
    internal static void Remove(int playerId) => Frames.Remove(playerId);
    internal static void Reset() => Frames.Clear();
    private static ulong Key(ItemAppearanceState state) => (ulong)(uint)state.ActorId << 32 | state.Generation;
    internal static void Set(int playerId, ItemAppearanceSnapshot snapshot)
    {
        if (!Frames.TryGetValue(playerId, out Frame? frame)) Frames[playerId] = frame = new Frame();
        if (snapshot.SampleTime <= frame.Time) return;
        bool continuous = frame.Time >= 0 && snapshot.SampleTime - frame.Time <= UseBarAnimationPlaybackClock.MaximumContinuousGap;
        if (!continuous) frame.Clock.Reset(snapshot.SampleTime, Time.unscaledTime);
        var present = new HashSet<ulong>();
        foreach (ItemAppearanceState state in snapshot.States)
        {
            ulong key = Key(state); present.Add(key);
            if (!frame.Entries.TryGetValue(key, out Entry? entry))
            {
                CPlayerActor? actor = RemoteBoardFocus.ActorById(state.ActorId);
                entry = new Entry { Item = Resolve(actor, state), Current = state, Previous = state,
                    CurrentTime = snapshot.SampleTime, PreviousTime = snapshot.SampleTime };
                frame.Entries[key] = entry;
            }
            else
            {
                if (entry.Item == null) entry.Item = Resolve(RemoteBoardFocus.ActorById(state.ActorId), state);
                entry.Previous = continuous ? entry.Current : state;
                entry.PreviousTime = continuous ? entry.CurrentTime : snapshot.SampleTime;
                entry.Current = state; entry.CurrentTime = snapshot.SampleTime;
            }
            if ((state.Flags & 2) != 0) { if (entry.TerminalTime < 0) entry.TerminalTime = snapshot.SampleTime; }
            else entry.TerminalTime = -1;
        }
        // The sender retains completed sources while needed. An explicit absent generation is
        // retirement, not permission to bind an old sample to a different item at the same seat.
        var removed = new List<ulong>();
        foreach (ulong key in frame.Entries.Keys) if (!present.Contains(key)) removed.Add(key);
        foreach (ulong key in removed) frame.Entries.Remove(key);
        frame.Time = snapshot.SampleTime;
    }
    private static CItem? Resolve(CPlayerActor? actor, ItemAppearanceState state)
    {
        List<CItem>? items = state.Population == 1 ? CardsGameApi.LoseRewardItems() : actor?.Inventory?.AllItems;
        if (items == null) return null;
        int count = 0; CItem? found = null;
        foreach (CItem item in items) if (item != null) { if (count == state.Seat) found = item; count++; }
        if (count != state.Count) return null;
        // A terminal-only arrival cannot identify a removed reward by a now-reused seat.
        // Inventory burns retain their item object and can safely resolve once the consumed model arrives.
        if ((state.Flags & 2) != 0 && (state.Population != 0 || found?.SlotState != CItem.EItemSlotState.Consumed)) return null;
        return found;
    }
    internal static bool TryGet(int playerId, CPlayerActor? actor, CItem? item,
        out ItemAppearanceState? previous, out ItemAppearanceState? current, out float progress)
    {
        previous = current = null; progress = 1;
        if (actor == null || item == null || !Frames.TryGetValue(playerId, out Frame? frame)) return false;
        int actorId = NetFigures.StableActorId(actor); Entry? best = null;
        foreach (Entry entry in frame.Entries.Values)
            if (entry.Current.ActorId == actorId && ReferenceEquals(entry.Item, item)
                && (best == null || entry.Current.Generation > best.Current.Generation)) best = entry;
        if (best == null) return false;
        frame.Clock.Advance(Time.unscaledTime, frame.Time);
        previous = best.Previous; current = best.Current;
        progress = frame.Clock.Progress(best.PreviousTime, best.CurrentTime);
        return true;
    }
    internal static CItem? ItemAt(int playerId, int actorId, int seat, int count)
    {
        if (!Frames.TryGetValue(playerId, out Frame? frame)) return null;
        Entry? best = null;
        foreach (Entry entry in frame.Entries.Values)
            if (entry.Current.ActorId == actorId && entry.Current.Seat == seat && entry.Current.Count == count
                && (best == null || entry.Current.Generation > best.Current.Generation)) best = entry;
        return best?.Item;
    }
    internal static void MarkPresented(int playerId, ItemAppearanceState state, float progress)
    {
        if (!Frames.TryGetValue(playerId, out Frame? frame) || !frame.Entries.TryGetValue(Key(state), out Entry? entry)) return;
        entry.Rejected = false;
        float presented = entry.PreviousTime + (entry.CurrentTime - entry.PreviousTime) * progress;
        entry.Presented = Math.Max(entry.Presented, presented);
    }
    internal static void RejectPresentation(int playerId, ItemAppearanceState state)
    {
        if (Frames.TryGetValue(playerId, out Frame? frame) && frame.Entries.TryGetValue(Key(state), out Entry? entry)) entry.Rejected = true;
    }
    internal static bool HoldsAnyClip(int playerId, CPlayerActor? actor)
    {
        if (actor == null || !Frames.TryGetValue(playerId, out Frame? frame)) return false;
        int actorId = NetFigures.StableActorId(actor);
        foreach (Entry entry in frame.Entries.Values)
            if (entry.Item != null && !entry.Rejected && (entry.Current.Flags & 4) != 0 && entry.Current.ActorId == actorId
                && ((entry.Current.Flags & 1) != 0 || entry.TerminalTime >= 0 && entry.Presented < entry.TerminalTime)) return true;
        return false;
    }
    internal static bool HoldsClip(int playerId, int actorId, int seat, int count)
    {
        if (!Frames.TryGetValue(playerId, out Frame? frame)) return false;
        foreach (Entry entry in frame.Entries.Values)
            if (entry.Item != null && !entry.Rejected && (entry.Current.Flags & 4) != 0 && entry.Current.ActorId == actorId && entry.Current.Seat == seat && entry.Current.Count == count
                && ((entry.Current.Flags & 1) != 0 || entry.TerminalTime >= 0 && entry.Presented < entry.TerminalTime)) return true;
        return false;
    }
}
