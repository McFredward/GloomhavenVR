using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Dedicated native animation frames. Record 49 is additive; the six-byte v3 header,
/// presence serializer and record 47 are unchanged. A frame publishes only after every slot's
/// canonical chunks have arrived and agree. Transport fragmentation is a separate outer layer.</summary>
internal static class UseBarAnimationCodec
{
    internal const int MaxSize = 3072;
    internal const int MaxEncodedBytes = 2582; // 6 + 8 slots * 2 chunks * (2 + 15 + 8 * 18)
    internal const int EntriesPerChunk = 8;
    private const int MetadataBytes = 15;
    private const byte EmptySlot = 255;

    internal static int Write(UseBarAnimationSnapshot snapshot, byte[] buffer)
    {
        if (snapshot == null || buffer == null || buffer.Length < MaxSize)
            throw new ArgumentException("Invalid native animation output buffer or snapshot.");
        // Publication arrays are immutable by contract; validate again at the wire boundary so
        // a broken producer can never silently truncate a legitimate slot or write a partial one.
        if (!UseBarAnimationValue.Finite(snapshot.SampleTime) || snapshot.SampleTime < 0
            || snapshot.States == null || snapshot.States.Length > NetProtocol.UseBarsMaxSlots)
            throw new ArgumentException("Invalid native animation snapshot.");
        int occupied = 0;
        foreach (UseBarAnimationState state in snapshot.States)
        {
            if (state == null || !state.Validate() || (occupied & (1 << state.Slot)) != 0)
                throw new ArgumentException("Invalid native animation slot.");
            occupied |= 1 << state.Slot;
        }
        int at = 0;
        AvatarSerializer.WriteU32(buffer, ref at, NetProtocol.Magic);
        buffer[at++] = NetProtocol.Version;
        buffer[at++] = NetProtocol.MsgUseBarAnimation;
        if (snapshot.States.Length == 0)
        {
            buffer[at++] = NetProtocol.ExtIdUseBarAnimation;
            buffer[at++] = MetadataBytes;
            WriteMetadata(buffer, ref at, EmptySlot, 0, 0, snapshot.SampleTime, 0, 0, 0, 0);
            return at;
        }
        foreach (UseBarAnimationState state in snapshot.States)
        {
            int total = state.Entries.Length;
            for (int start = 0; start < Math.Max(1, total); start += EntriesPerChunk)
            {
                int count = Math.Min(EntriesPerChunk, total - start);
                int bytes = MetadataBytes;
                for (int n = 0; n < count; n++) bytes += 2 + 4 * state.Entries[start + n].Values.Length;
                buffer[at++] = NetProtocol.ExtIdUseBarAnimation;
                buffer[at++] = (byte)bytes;
                WriteMetadata(buffer, ref at, state.Slot, state.ActorId, state.SlotIdentity,
                    snapshot.SampleTime, snapshot.States.Length, total, start, count);
                for (int n = 0; n < count; n++)
                {
                    UseBarAnimationValue value = state.Entries[start + n];
                    buffer[at++] = value.SettingIndex;
                    buffer[at++] = (byte)value.Kind;
                    foreach (float component in value.Values) AvatarSerializer.WriteF32(buffer, ref at, component);
                }
            }
        }
        return at;
    }

    private static void WriteMetadata(byte[] buffer, ref int at, byte slot, int actor, ushort identity,
                                      float time, int slots, int total, int start, int count)
    {
        buffer[at++] = slot;
        AvatarSerializer.WriteI32(buffer, ref at, actor);
        buffer[at++] = (byte)identity;
        buffer[at++] = (byte)(identity >> 8);
        AvatarSerializer.WriteF32(buffer, ref at, time);
        buffer[at++] = (byte)slots;
        buffer[at++] = (byte)total;
        buffer[at++] = (byte)start;
        buffer[at++] = (byte)count;
    }

    private sealed class SlotParts
    {
        internal readonly UseBarAnimationState State;
        internal readonly byte[]?[] Chunks = new byte[2][];
        internal int Seen;
        internal SlotParts(byte slot, int actor, ushort identity, int count) => State = new()
        {
            Slot = slot, ActorId = actor, SlotIdentity = identity, Entries = new UseBarAnimationValue[count],
        };
    }

    /// <summary>Read one whole animation message. Unknown TLVs skip by length; malformed known
    /// records, torn slots, duplicate ordinals and conflicting duplicates reject the whole frame.
    /// Identical repeated chunks are inert. Output slots are ordered by their original bar ordinal.</summary>
    internal static bool TryRead(byte[] buffer, int length, out UseBarAnimationSnapshot? snapshot)
    {
        snapshot = null;
        if (buffer == null || length < 6 || length > buffer.Length || length > MaxSize
            || NetPacket.PeekType(buffer, length) != NetProtocol.MsgUseBarAnimation) return false;
        var slots = new SlotParts?[NetProtocol.UseBarsMaxSlots];
        bool hasTime = false, empty = false;
        float sampleTime = 0;
        int slotCount = 0, expectedSlots = -1;
        for (int at = 6; at < length;)
        {
            if (at + 2 > length) return false;
            int type = buffer[at++], bytes = buffer[at++];
            int payloadAt = at, end = at + bytes;
            if (end > length) return false;
            if (type != NetProtocol.ExtIdUseBarAnimation) { at = end; continue; }
            if (bytes < MetadataBytes) return false;
            byte slot = buffer[at++];
            int actor = AvatarSerializer.ReadI32(buffer, ref at);
            ushort identity = (ushort)(buffer[at] | buffer[at + 1] << 8); at += 2;
            float time = AvatarSerializer.ReadF32(buffer, ref at);
            int statedSlots = buffer[at++], total = buffer[at++], start = buffer[at++], count = buffer[at++];
            if (statedSlots > slots.Length || (expectedSlots >= 0 && statedSlots != expectedSlots)) return false;
            expectedSlots = statedSlots;
            if (!UseBarAnimationValue.Finite(time) || time < 0 || (hasTime && time != sampleTime)) return false;
            hasTime = true; sampleTime = time;
            if (slot == EmptySlot)
            {
                if (statedSlots != 0 || slotCount != 0 || actor != 0 || identity != 0 || total != 0 || start != 0 || count != 0
                    || at != end) return false;
                empty = true;
                continue;
            }
            if (empty || statedSlots == 0 || slot >= slots.Length || actor == 0 || identity == UseBarSlotIdentity.NoIdentity
                || total > UseBarAnimationState.CountMax || start % EntriesPerChunk != 0
                || start >= Math.Max(1, total) || count != Math.Min(EntriesPerChunk, total - start)) return false;
            SlotParts? parts = slots[slot];
            if (parts == null)
            {
                parts = new SlotParts(slot, actor, identity, total);
                slots[slot] = parts;
                slotCount++;
            }
            else if (parts.State.ActorId != actor || parts.State.SlotIdentity != identity
                     || parts.State.Entries.Length != total) return false;
            int chunk = start / EntriesPerChunk;
            byte[]? previous = parts.Chunks[chunk];
            if (previous != null)
            {
                if (previous.Length != bytes) return false;
                for (int b = 0; b < bytes; b++)
                    if (previous[b] != buffer[payloadAt + b]) return false;
                at = end;
                continue;
            }
            for (int n = 0; n < count; n++)
            {
                if (at + 2 > end) return false;
                byte index = buffer[at++];
                var kind = (UseBarAnimationKind)buffer[at++];
                int components = UseBarAnimationValue.Components(kind);
                if (components == 0 || at + 4 * components > end) return false;
                var value = new UseBarAnimationValue { SettingIndex = index, Kind = kind, Values = new float[components] };
                for (int c = 0; c < components; c++)
                {
                    value.Values[c] = AvatarSerializer.ReadF32(buffer, ref at);
                    if (!UseBarAnimationValue.Finite(value.Values[c])) return false;
                }
                parts.State.Entries[start + n] = value;
            }
            if (at != end) return false;
            parts.Chunks[chunk] = new byte[bytes];
            Buffer.BlockCopy(buffer, payloadAt, parts.Chunks[chunk]!, 0, bytes);
            parts.Seen |= 1 << chunk;
        }
        if (!hasTime || slotCount != expectedSlots || (!empty && slotCount == 0)) return false;
        var states = new List<UseBarAnimationState>(slotCount);
        foreach (SlotParts? parts in slots)
        {
            if (parts == null) continue;
            int chunks = Math.Max(1, (parts.State.Entries.Length + EntriesPerChunk - 1) / EntriesPerChunk);
            if (parts.Seen != (1 << chunks) - 1 || !parts.State.Validate()) return false;
            states.Add(parts.State);
        }
        snapshot = new UseBarAnimationSnapshot(sampleTime, states.ToArray());
        return true;
    }
}
