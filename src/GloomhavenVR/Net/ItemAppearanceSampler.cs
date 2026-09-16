using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

internal static class ItemAppearanceSampler
{
    private sealed class Entry
    {
        internal CItem Item = null!;
        internal int Actor;
        internal uint Generation;
        internal ItemCardUI? Source;
        internal ItemAppearanceBindings? Bindings;
        internal ItemAppearanceState? Published;
        internal bool Seen;
        internal string? Refusal;
        internal bool TerminalPinned;
        internal CItem.EItemSlotState TerminalState;
        internal readonly ItemAppearanceState Candidate = new();
    }
    private static readonly List<ItemsPile.ItemChip> Chips = new();
    private static readonly List<Entry> Entries = new();
    private static readonly List<ItemAppearanceState> Output = new();
    private static ItemAppearanceState[] _previous = Array.Empty<ItemAppearanceState>();
    private static uint _generation;
    internal static void Reset() { Chips.Clear(); Entries.Clear(); Output.Clear(); _previous = Array.Empty<ItemAppearanceState>(); _generation = 0; }
    internal static void RetainCompletion(ItemsPile.ItemChip chip)
    {
        // Offline sessions do not run the sampler's pruning pass. Their local native effect
        // needs no network snapshot and must not retain item graphs across successive scenarios.
        if (!NetAvatarDriver.CanPublishNativePresentation) return;
        Entry? entry = Capture(chip, completed: true);
        if (entry != null) entry.Seen = true;
    }
    internal static ItemAppearanceState[] Sample()
    {
        using var scope = PerfMonitor.Scope("Net.ItemAppearance.Sample");
        foreach (Entry entry in Entries) entry.Seen = false;
        ItemsPile.ItemChip.CopySmokeChips(Chips);
        foreach (var chip in Chips) Capture(chip, completed: false);
        Output.Clear();
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            Entry entry = Entries[i];
            CPlayerActor? actor = RemoteBoardFocus.ActorById(entry.Actor);
            if (actor == null || entry.TerminalPinned && entry.Item.SlotState != entry.TerminalState)
            {
                Entries.RemoveAt(i); continue;
            }
            // Retain terminal output after source recycling and inventory removal, so a late
            // peer can finish an already-bound burn. Bounded by one latest entry per item.
            if (!entry.Seen && (entry.Published == null || (entry.Published.Flags & 2) == 0)) { Entries.RemoveAt(i); continue; }
        }
        while (Entries.Count > ItemAppearanceSnapshot.MaxCards)
        {
            int old = Entries.FindIndex(e => !e.Seen);
            if (old < 0) throw new InvalidOperationException("Native item presentation exceeds atomic frame capacity.");
            Entries.RemoveAt(old);
        }
        foreach (Entry entry in Entries) if (entry.Published != null) Output.Add(entry.Published);
        bool same = _previous.Length == Output.Count;
        for (int i = 0; same && i < Output.Count; i++) same = ReferenceEquals(_previous[i], Output[i]);
        return same ? _previous : _previous = Output.ToArray();
    }
    private static Entry? Capture(ItemsPile.ItemChip chip, bool completed)
    {
        if (chip == null || chip.Item == null || chip.Owner?.OwnerActor == null || chip.NativeItemCard?.cardEffects == null) return null;
        int actor = NetFigures.StableActorId(chip.Owner.OwnerActor);
        Entry? entry = null;
        foreach (Entry candidate in Entries)
            if (candidate.Actor == actor && ReferenceEquals(candidate.Item, chip.Item)) { entry = candidate; break; }
        if (entry == null)
        {
            if (++_generation == 0) _generation++;
            entry = new Entry { Actor = actor, Item = chip.Item, Generation = _generation }; Entries.Add(entry);
        }
        entry.Seen = true;
        try
        {
            ItemCardUI source = chip.NativeItemCard;
            // Capture the terminal native graph once, before the collapse changes its visibility.
            // Recovery is a new incarnation: late terminal packets cannot bind to the recovered card.
            if (entry.TerminalPinned)
            {
                if (ReferenceEquals(entry.Source, source)
                    && (chip.BurnPresentationPending || chip.Item.SlotState == entry.TerminalState)) return entry;
                entry.TerminalPinned = false;
                if (++_generation == 0) _generation++;
                entry.Generation = _generation; entry.Published = null;
            }
            if (!ReferenceEquals(entry.Source, source)) { entry.Source = source; entry.Bindings = new ItemAppearanceBindings(source.cardEffects); }
            byte seat = entry.Published?.Seat ?? 0, count = entry.Published?.Count ?? 0, population = entry.Published?.Population ?? 0;
            // During a forfeit the model may already have removed the item. Keep its established
            // opening address instead of assigning another card's shifted position.
            if (entry.Published == null || (entry.Published.Flags & 3) == 0)
                chip.Owner.ItemSourceAddress(chip, out seat, out count, out population);
            if (count == 0) return entry;
            bool running = chip.BurnPresentationPending || ItemBurnPlayback.Playing(source.cardEffects);
            var state = entry.Candidate;
            state.ActorId = actor; state.Seat = seat; state.Count = count; state.Population = population;
            state.Generation = entry.Generation; state.Flags = (byte)((completed ? 2 : running ? 1 : 0) | (chip.PendingUse ? 4 : 0));
            state.Nodes = entry.Bindings!.Capture();
            if (entry.Published == null || !ItemAppearanceState.Same(entry.Published, state))
            {
                if (!state.Validate()) throw new InvalidOperationException("Original item hierarchy exceeds native frame domain.");
                entry.Published = new ItemAppearanceState { ActorId = state.ActorId, Seat = state.Seat, Count = state.Count,
                    Population = state.Population, Generation = state.Generation, Flags = state.Flags, Nodes = state.Nodes };
            }
            if (completed) { entry.TerminalPinned = true; entry.TerminalState = chip.Item.SlotState; }
            entry.Refusal = null;
        }
        catch (Exception e)
        {
            if (entry.Refusal != e.Message) { entry.Refusal = e.Message; VRLog.Warn("Net", "Native item appearance unavailable: " + e.Message); }
        }
        return entry;
    }
}
