using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>One latest numeric entry, with the same no-catch-up budget as the rig lane.</summary>
internal class TownServiceMotionPending
{
    internal TownServiceMotionEntry Entry = null!;
    internal bool Dirty = true;
    internal float SentAt = float.NegativeInfinity;
}

/// <summary>Live controls and held targets get a finite turn before cold fan heartbeats.
/// A complete cold record retains a turn under full contention.</summary>
internal static class TownServiceMotionBudget
{
    private static readonly List<TownServiceMotionPending> NoVisibleFan = new();
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
