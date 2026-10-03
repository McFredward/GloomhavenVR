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
/// At least one third of bytes remains available to the latter under full contention.</summary>
internal static class TownServiceMotionBudget
{
    internal static void Fill(TownServiceMotionPacket packet, List<TownServiceMotionPending> live,
        List<TownServiceMotionPending> ordinary, ref int liveCursor, ref int ordinaryCursor, float now)
    {
        int size = 21;
        foreach (TownServiceMotionEntry entry in packet.Entries) size += TownServiceMotionCodec.EntryBytes(entry);
        int reserved = live.Count != 0 && ordinary.Count != 0 ? size + (TownServiceMotionCodec.MaxBytes - size) * 2 / 3 : TownServiceMotionCodec.MaxBytes;
        Append(packet, live, ref liveCursor, ref size, reserved, now);
        Append(packet, ordinary, ref ordinaryCursor, ref size, TownServiceMotionCodec.MaxBytes, now);
        Append(packet, live, ref liveCursor, ref size, TownServiceMotionCodec.MaxBytes, now);
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
