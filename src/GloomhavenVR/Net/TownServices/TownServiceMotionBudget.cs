using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>One latest numeric entry, with the same no-catch-up budget as the rig lane.</summary>
internal class TownServiceMotionPending
{
    internal TownServiceMotionEntry Entry = null!;
    internal bool Dirty = true;
    internal float SentAt = float.NegativeInfinity;
    internal uint AdmittedReturnRevision;
}

/// <summary>Live controls and held targets get a finite turn before cold fan heartbeats.
/// A complete cold record retains a turn under full contention.</summary>
internal static class TownServiceMotionBudget
{
    private static readonly List<TownServiceMotionPending> NoVisibleFan = new();
    private readonly struct Selected
    {
        internal readonly TownServiceMotionPending Slot;
        internal readonly int Group, Next, Bundle;
        internal Selected(TownServiceMotionPending slot, int group, int next, int bundle = 0)
        { Slot = slot; Group = group; Next = next; Bundle = bundle; }
    }

    /// <summary>Pack current numeric samples losslessly into the unchanged event budget.
    /// Fair ordered turns survive compression backpressure: unsent slots retain both
    /// their dirty flag and their last-send clock. No dropped record is marked sent.</summary>
    internal static byte[] FillPacked(TownServiceMotionPacket packet, List<TownServiceMotionPending> live,
        List<TownServiceMotionPending> visibleFan, List<TownServiceMotionPending> ordinary,
        ref int liveCursor, ref int visibleCursor, ref int ordinaryCursor, float now)
    {
        var groups = new[] { live, visibleFan, ordinary };
        var cursors = new[] { liveCursor, visibleCursor, ordinaryCursor };
        var visited = new int[3];
        var selected = new List<Selected>(TownServiceMotionCodec.MaxExpandedEntries);
        var seen = new HashSet<TownServiceMotionPending>();
        int initial = packet.Entries.Count, size = 21;
        foreach (TownServiceMotionEntry entry in packet.Entries) size += TownServiceMotionCodec.EntryBytes(entry);
        // A new short original return must begin before routine material and hover numbers.
        // Admit at most two complete clock/root pairs, preserving ordinary finite turns
        // and the same event size. Compression trimming retains or removes each pair together.
        int pairs = 0;
        for (int i = 0; i < live.Count && pairs < 2; i++)
        {
            int index = (liveCursor + i) % live.Count;
            TownServiceMotionPending clock = live[index];
            if (clock.Entry.Kind != 8 || clock.SentAt == now || !clock.Dirty
                || clock.AdmittedReturnRevision == clock.Entry.Revision) continue;
            int rootIndex = -1;
            for (int n = 0; n < live.Count; n++)
            {
                TownServiceMotionEntry root = live[n].Entry, flight = clock.Entry;
                if (root.Kind == 1 && root.Lane == flight.Lane && root.Service == flight.Service
                    && root.Session == flight.Session && root.PublicClaim == flight.PublicClaim
                    && root.Module == flight.Module && root.Structure == flight.Structure && root.Hand == flight.Hand)
                { rootIndex = n; break; }
            }
            if (rootIndex < 0) continue;
            TownServiceMotionPending anchor = live[rootIndex];
            int pairBytes = TownServiceMotionCodec.EntryBytes(anchor.Entry) + TownServiceMotionCodec.EntryBytes(clock.Entry);
            if (size + pairBytes > TownServiceMotionCodec.MaxExpandedBytes
                || packet.Entries.Count + 2 > TownServiceMotionCodec.MaxExpandedEntries) break;
            int bundle = ++pairs;
            packet.Entries.Add(anchor.Entry); packet.Entries.Add(clock.Entry);
            selected.Add(new Selected(anchor, 0, rootIndex + 1, bundle));
            selected.Add(new Selected(clock, 0, index + 1, bundle));
            seen.Add(anchor); seen.Add(clock); size += pairBytes;
        }
        int turn = 0;
        while (packet.Entries.Count < TownServiceMotionCodec.MaxExpandedEntries
            && (visited[0] < live.Count || visited[1] < visibleFan.Count || visited[2] < ordinary.Count))
        {
            // Two live turns, one visible fan and one recovery turn. The order matters:
            // fitting a smaller incompressible prefix preserves all three participants.
            int group = turn++ % 4; group = group < 2 ? 0 : group - 1;
            List<TownServiceMotionPending> waiting = groups[group];
            if (visited[group] >= waiting.Count) continue;
            int index = (cursors[group] + visited[group]++) % waiting.Count;
            TownServiceMotionPending slot = waiting[index];
            if (slot.SentAt == now || !seen.Add(slot)) continue;
            // A first clock waits for its complete priority pair rather than taking a
            // later unpaired ordinary turn. Admitted clocks use normal bounded repair.
            if (slot.Entry.Kind == 8 && slot.AdmittedReturnRevision != slot.Entry.Revision) continue;
            int bytes = TownServiceMotionCodec.EntryBytes(slot.Entry);
            if (size + bytes > TownServiceMotionCodec.MaxExpandedBytes) break;
            packet.Entries.Add(slot.Entry); selected.Add(new Selected(slot, group, index + 1)); size += bytes;
        }
        byte[]? encoded = packet.Entries.Count == 0 ? null : TownServiceMotionCodec.TryWritePacked(packet);
        while (encoded == null && selected.Count > 0)
        {
            // At most logarithmically many compression probes; real random float
            // payloads must retain a legacy-sized finite turn too.
            int keep = Math.Max(0, selected.Count * 3 / 4);
            if (keep > 0 && keep < selected.Count && selected[keep - 1].Bundle != 0
                && selected[keep - 1].Bundle == selected[keep].Bundle) keep--;
            selected.RemoveRange(keep, selected.Count - keep);
            packet.Entries.RemoveRange(initial + keep, packet.Entries.Count - initial - keep);
            if (packet.Entries.Count > 0) encoded = TownServiceMotionCodec.TryWritePacked(packet);
        }
        if (encoded == null) return System.Array.Empty<byte>();
        foreach (Selected accepted in selected)
        {
            accepted.Slot.Dirty = false; accepted.Slot.SentAt = now;
            if (accepted.Slot.Entry.Kind == 8) accepted.Slot.AdmittedReturnRevision = accepted.Slot.Entry.Revision;
            cursors[accepted.Group] = accepted.Next;
        }
        liveCursor = cursors[0]; visibleCursor = cursors[1]; ordinaryCursor = cursors[2];
        return encoded;
    }
    internal static void Fill(TownServiceMotionPacket packet, List<TownServiceMotionPending> live,
        List<TownServiceMotionPending> ordinary, ref int liveCursor, ref int ordinaryCursor, float now)
    {
        int emptyCursor = 0;
        Fill(packet, live, NoVisibleFan, ordinary, ref liveCursor, ref emptyCursor, ref ordinaryCursor, now);
    }
    internal static void Fill(TownServiceMotionPacket packet, List<TownServiceMotionPending> live,
        List<TownServiceMotionPending> visibleFan, List<TownServiceMotionPending> ordinary,
        ref int liveCursor, ref int visibleCursor, ref int ordinaryCursor, float now)
    {
        int size = 21;
        foreach (TownServiceMotionEntry entry in packet.Entries) size += TownServiceMotionCodec.EntryBytes(entry);
        // Reserve a complete record, rather than a percentage too small to fit
        // its canvas. A visible wrist reveal has a turn before cold heartbeats;
        // it cannot consume the turn of a held/palm card, press or scroll.
        int cold = Reservation(ordinary), visible = Reservation(visibleFan);
        Append(packet, live, ref liveCursor, ref size, TownServiceMotionCodec.MaxBytes - cold - visible, now);
        Append(packet, visibleFan, ref visibleCursor, ref size, TownServiceMotionCodec.MaxBytes - cold, now);
        Append(packet, ordinary, ref ordinaryCursor, ref size, TownServiceMotionCodec.MaxBytes, now);
        Append(packet, live, ref liveCursor, ref size, TownServiceMotionCodec.MaxBytes, now);
        Append(packet, visibleFan, ref visibleCursor, ref size, TownServiceMotionCodec.MaxBytes, now);
    }
    private static int Reservation(List<TownServiceMotionPending> waiting)
    {
        int largest = 0;
        foreach (TownServiceMotionPending slot in waiting) largest = System.Math.Max(largest, TownServiceMotionCodec.EntryBytes(slot.Entry));
        return largest;
    }
    private static void Append(TownServiceMotionPacket packet, List<TownServiceMotionPending> waiting,
        ref int cursor, ref int size, int limit, float now)
    {
        if (waiting.Count == 0) { cursor = 0; return; }
        int start = cursor % waiting.Count;
        for (int i = 0; i < waiting.Count && packet.Entries.Count < TownServiceMotionCodec.MaxEntries; i++)
        {
            int index = (start + i) % waiting.Count;
            TownServiceMotionPending slot = waiting[index];
            if (slot.SentAt == now) continue;
            int bytes = TownServiceMotionCodec.EntryBytes(slot.Entry);
            if (size + bytes > limit) continue;
            packet.Entries.Add(slot.Entry); size += bytes; slot.Dirty = false; slot.SentAt = now;
            cursor = index + 1;
        }
    }
}
