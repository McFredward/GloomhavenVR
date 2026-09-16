using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Additive record76 pages of one atomic native item frame. Existing wire layouts are untouched.</summary>
internal static class ItemAppearanceCodec
{
    internal const int MaxSize = 60000;
    internal const byte Message = NetProtocol.MsgItemAppearance, FragmentMessage = NetProtocol.MsgItemAppearanceFragments,
        Record = NetProtocol.ExtIdItemAppearance;
    internal static int Write(ItemAppearanceSnapshot snapshot, byte[] buffer)
    {
        if (buffer == null || buffer.Length < MaxSize) throw new ArgumentException("Invalid native item buffer.");
        snapshot = new ItemAppearanceSnapshot(snapshot.SampleTime, snapshot.States);
        int at = 0;
        AvatarSerializer.WriteU32(buffer, ref at, NetProtocol.Magic); buffer[at++] = NetProtocol.Version; buffer[at++] = Message;
        buffer[at++] = Record; buffer[at++] = 6; buffer[at++] = 0;
        AvatarSerializer.WriteF32(buffer, ref at, snapshot.SampleTime); buffer[at++] = (byte)snapshot.States.Length;
        for (int i = 0; i < snapshot.States.Length; i++)
        {
            var state = snapshot.States[i];
            buffer[at++] = Record; buffer[at++] = 16; buffer[at++] = 1; buffer[at++] = (byte)i;
            AvatarSerializer.WriteI32(buffer, ref at, state.ActorId); buffer[at++] = state.Seat; buffer[at++] = state.Count; buffer[at++] = state.Population;
            AvatarSerializer.WriteU32(buffer, ref at, state.Generation); buffer[at++] = state.Flags;
            buffer[at++] = (byte)state.Nodes.Length; buffer[at++] = (byte)(state.Nodes.Length >> 8);
            foreach (var entry in state.Nodes)
            {
                var node = entry.Value;
                buffer[at++] = Record; buffer[at++] = 144; buffer[at++] = 2; buffer[at++] = (byte)i;
                AvatarSerializer.WriteU32(buffer, ref at, entry.Binding); buffer[at++] = node.Role; buffer[at++] = node.Flags;
                AvatarSerializer.WriteU32(buffer, ref at, node.Mask);
                for (int v = 0; v < CardAppearanceNode.ValueCount; v++) AvatarSerializer.WriteF32(buffer, ref at, node.Values[v]);
            }
        }
        return at;
    }
    internal static bool TryRead(byte[] buffer, int length, out ItemAppearanceSnapshot? snapshot)
    {
        snapshot = null;
        if (buffer == null || length < 14 || length > buffer.Length || length > MaxSize) return false;
        int at = 0;
        if (AvatarSerializer.ReadU32(buffer, ref at) != NetProtocol.Magic || buffer[at++] != NetProtocol.Version || buffer[at++] != Message) return false;
        float time = -1; ItemAppearanceState[]? states = null;
        List<ItemAppearanceNode>[]? nodes = null; int[]? counts = null; int totalNodes = 0;
        while (at < length)
        {
            if (at + 2 > length) return false;
            byte record = buffer[at++]; int size = buffer[at++], end = at + size;
            if (end > length) return false;
            if (record != Record) { at = end; continue; }
            if (size == 0) return false;
            byte kind = buffer[at++];
            if (kind == 0)
            {
                if (size != 6 || states != null) return false;
                time = AvatarSerializer.ReadF32(buffer, ref at); int count = buffer[at++];
                if (float.IsNaN(time) || float.IsInfinity(time) || time < 0 || count > ItemAppearanceSnapshot.MaxCards) return false;
                states = new ItemAppearanceState[count]; nodes = new List<ItemAppearanceNode>[count]; counts = new int[count];
            }
            else if (kind == 1)
            {
                if (size != 16 || states == null) return false;
                int index = buffer[at++]; if (index >= states.Length || states[index] != null) return false;
                var state = new ItemAppearanceState { ActorId = AvatarSerializer.ReadI32(buffer, ref at), Seat = buffer[at++], Count = buffer[at++], Population = buffer[at++] };
                state.Generation = AvatarSerializer.ReadU32(buffer, ref at); state.Flags = buffer[at++];
                int count = buffer[at++] | buffer[at++] << 8;
                if (count == 0 || (totalNodes += count) > ItemAppearanceSnapshot.MaxNodes) return false;
                states[index] = state; nodes![index] = new List<ItemAppearanceNode>(count); counts![index] = count;
            }
            else if (kind == 2)
            {
                if (size != 144 || states == null) return false;
                int index = buffer[at++];
                if (index >= states.Length || states[index] == null || nodes![index].Count >= counts![index]) return false;
                var entry = new ItemAppearanceNode { Binding = AvatarSerializer.ReadU32(buffer, ref at) };
                var node = entry.Value; node.Role = buffer[at++]; node.Flags = buffer[at++]; node.Mask = AvatarSerializer.ReadU32(buffer, ref at);
                if (node.Role >= 12) node.Binding = entry.Binding;
                for (int v = 0; v < CardAppearanceNode.ValueCount; v++) node.Values[v] = AvatarSerializer.ReadF32(buffer, ref at);
                nodes![index].Add(entry);
            }
            else return false;
            if (at != end) return false;
        }
        if (states == null) return false;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i] == null || nodes![i].Count != counts![i]) return false;
            states[i].Nodes = nodes[i].ToArray();
        }
        try { snapshot = new ItemAppearanceSnapshot(time, states); return true; }
        catch (ArgumentException) { return false; }
    }
}
