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
        // Build656 paired root/clock admission counted detached original modules,
        // not physical cards. With body/print registration separated, two bodies
        // could begin while both native fronts stayed parked: the gray rectangle
        // in the supplied return video. The first 28 clock numbers are the owner's
        // exact physical-root receipt; only the final ten describe each child.
        // Admit every such sibling with its own root in one indivisible event.
        // At most two native returns take priority; ordinary finite turns and the
        // 864-byte encoded budget remain unchanged. No observer ACK gates flight.
        int bundles = 0;
        for (int i = 0; i < live.Count && bundles < 2; i++)
        {
            int index = (liveCursor + i) % live.Count;
            TownServiceMotionPending clock = live[index];
            if (seen.Contains(clock) || clock.Entry.Kind != 8 || clock.SentAt == now || !clock.Dirty
                || clock.AdmittedReturnRevision == clock.Entry.Revision) continue;
            var group = new List<Selected>();
            int bytes = 0;
            bool complete = true;
            for (int part = 0; part < live.Count; part++)
            {
                TownServiceMotionPending sibling = live[part];
                if (!SameReturnRoot(clock.Entry, sibling.Entry)) continue;
                int rootIndex = ReturnRoot(live, sibling.Entry);
                if (rootIndex < 0 || seen.Contains(sibling) || seen.Contains(live[rootIndex]))
                { complete = false; break; }
                TownServiceMotionPending anchor = live[rootIndex];
                bytes += TownServiceMotionCodec.EntryBytes(anchor.Entry) + TownServiceMotionCodec.EntryBytes(sibling.Entry);
                group.Add(new Selected(anchor, 0, rootIndex + 1, bundles + 1));
                group.Add(new Selected(sibling, 0, part + 1, bundles + 1));
            }
            if (!complete || group.Count == 0 || size + bytes > TownServiceMotionCodec.MaxExpandedBytes
                || packet.Entries.Count + group.Count > TownServiceMotionCodec.MaxExpandedEntries) continue;
            bundles++;
            foreach (Selected part in group)
            { packet.Entries.Add(part.Slot.Entry); selected.Add(part); seen.Add(part.Slot); }
            size += bytes;
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
            if (slot.Entry.Kind == 8 && slot.AdmittedReturnRevision != slot.Entry.Revision
                || slot.Entry.Kind == 1 && HasUnadmittedReturn(live, slot.Entry)) continue;
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
            while (keep > 0 && keep < selected.Count && selected[keep - 1].Bundle != 0
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
    private static int ReturnRoot(List<TownServiceMotionPending> live, TownServiceMotionEntry flight)
    {
        for (int n = 0; n < live.Count; n++)
        {
            TownServiceMotionEntry root = live[n].Entry;
            if (root.Kind == 1 && root.Lane == flight.Lane && root.Service == flight.Service
                && root.Session == flight.Session && root.PublicClaim == flight.PublicClaim
                && root.Module == flight.Module && root.Structure == flight.Structure && root.Hand == flight.Hand) return n;
        }
        return -1;
    }
    private static bool HasUnadmittedReturn(List<TownServiceMotionPending> live, TownServiceMotionEntry root)
    {
        foreach (TownServiceMotionPending clock in live)
            if (clock.Entry.Kind == 8 && clock.AdmittedReturnRevision != clock.Entry.Revision
                && clock.Entry.Lane == root.Lane && clock.Entry.Service == root.Service
                && clock.Entry.Session == root.Session && clock.Entry.PublicClaim == root.PublicClaim
                && clock.Entry.Module == root.Module && clock.Entry.Structure == root.Structure) return true;
        return false;
    }
    private static bool SameReturnRoot(TownServiceMotionEntry first, TownServiceMotionEntry second)
    {
        if (second.Kind != 8 || first.Lane != second.Lane || first.Service != second.Service
            || first.Session != second.Session || first.PublicClaim != second.PublicClaim
            || first.Hand != second.Hand || first.Revision != second.Revision
            || first.Numbers.Length != 38 || second.Numbers.Length != 38) return false;
        for (int n = 0; n < 28; n++) if (first.Numbers[n] != second.Numbers[n]) return false;
        return true;
    }

}
