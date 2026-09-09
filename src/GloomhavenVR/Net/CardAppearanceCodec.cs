using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Additive58 node pages in message12. Every page belongs to one atomic snapshot.</summary>
internal static class CardAppearanceCodec
{
    internal const int MaxSize = 45056;
    internal const byte Message = 12, FragmentMessage = 13, Record = 58;
    internal static int Write(CardAppearanceSnapshot snapshot, byte[] buffer)
    {
        if (buffer == null || buffer.Length < MaxSize) throw new ArgumentException("Invalid appearance output buffer.");
        snapshot = new CardAppearanceSnapshot(snapshot.SampleTime, snapshot.States);
        int at = 0;
        AvatarSerializer.WriteU32(buffer, ref at, NetProtocol.Magic);
        buffer[at++] = NetProtocol.Version; buffer[at++] = Message;
        if (snapshot.States.Length == 0)
        {
            buffer[at++] = Record; buffer[at++] = 6; buffer[at++] = 255; buffer[at++] = 0;
            AvatarSerializer.WriteF32(buffer, ref at, snapshot.SampleTime);
        }
        for (int i = 0; i < snapshot.States.Length; i++)
        {
            var state = snapshot.States[i];
            foreach (var node in state.Nodes)
            {
                buffer[at++] = Record; int sizeAt = at++;
                buffer[at++] = (byte)i; buffer[at++] = (byte)snapshot.States.Length;
                AvatarSerializer.WriteF32(buffer, ref at, snapshot.SampleTime);
                AvatarSerializer.WriteI32(buffer, ref at, state.ActorId);
                buffer[at++] = state.FaceCode; buffer[at++] = state.ListCount;
                buffer[at++] = (byte)state.Nodes.Length; buffer[at++] = node.Role; buffer[at++] = node.Flags;
                AvatarSerializer.WriteU32(buffer, ref at, node.Mask);
                if (node.Role >= 12) AvatarSerializer.WriteU32(buffer, ref at, node.Binding);
                for (int v = 0; v < (node.Role >= 12 ? 1 : CardAppearanceNode.ValueCount); v++)
                    if (CardAppearanceNode.Carries(node.Mask, v)) AvatarSerializer.WriteF32(buffer, ref at, node.Values[v]);
                buffer[sizeAt] = (byte)(at - sizeAt - 1);
            }
        }
        return at;
    }
    internal static bool TryRead(byte[] buffer, int length, out CardAppearanceSnapshot? snapshot)
    {
        snapshot = null;
        if (buffer == null || length < 14 || length > buffer.Length || length > MaxSize) return false;
        int at = 0;
        if (AvatarSerializer.ReadU32(buffer, ref at) != NetProtocol.Magic || buffer[at++] != NetProtocol.Version || buffer[at++] != Message) return false;
        var cards = new List<CardAppearanceState>(); var nodes = new List<CardAppearanceNode>();
        int count = -1, expectedNodes = 0; float time = -1; bool empty = false;
        while (at < length)
        {
            if (at + 2 > length) return false;
            byte record = buffer[at++]; int end = at + 1 + buffer[at++];
            if (end > length) return false;
            if (record != Record) { at = end; continue; }
            if (empty || end - at < 6) return false;
            int index = buffer[at++], total = buffer[at++]; float sample = AvatarSerializer.ReadF32(buffer, ref at);
            if (float.IsNaN(sample) || float.IsInfinity(sample) || sample < 0 || total > CardAppearanceState.CountMax) return false;
            if (count < 0) { count = total; time = sample; }
            if (total != count || sample != time) return false;
            if (total == 0) { if (index != 255 || at != end || cards.Count != 0) return false; empty = true; continue; }
            if (end - at < 13 || index > cards.Count || index >= count) return false;
            int actor = AvatarSerializer.ReadI32(buffer, ref at); byte code = buffer[at++], listCount = buffer[at++];
            int nodeCount = buffer[at++];
            if (nodeCount == 0 || nodeCount > CardAppearanceNode.RoleCount) return false;
            if (index == cards.Count)
            {
                if (cards.Count > 0)
                {
                    if (nodes.Count != expectedNodes) return false;
                    cards[cards.Count - 1].Nodes = nodes.ToArray(); nodes.Clear();
                }
                cards.Add(new CardAppearanceState { ActorId = actor, FaceCode = code, ListCount = listCount });
                expectedNodes = nodeCount;
            }
            else if (index != cards.Count - 1) return false;
            var card = cards[index];
            if (card.ActorId != actor || card.FaceCode != code || card.ListCount != listCount || nodeCount != expectedNodes || nodes.Count >= nodeCount) return false;
            var node = new CardAppearanceNode { Role = buffer[at++], Flags = buffer[at++], Mask = AvatarSerializer.ReadU32(buffer, ref at) };
            if (node.Role >= CardAppearanceNode.RoleCount || (node.Mask & ~CardAppearanceNode.AllowedMask(node.Role)) != 0) return false;
            if (node.Role >= 12) { if (at + 4 > end) return false; node.Binding = AvatarSerializer.ReadU32(buffer, ref at); }
            for (int v = 0; v < (node.Role >= 12 ? 1 : CardAppearanceNode.ValueCount); v++) if (CardAppearanceNode.Carries(node.Mask, v))
            {
                if (at + 4 > end) return false;
                node.Values[v] = AvatarSerializer.ReadF32(buffer, ref at);
            }
            if (at != end || !node.Validate()) return false;
            nodes.Add(node);
        }
        if (count < 0 || cards.Count != count || count > 0 && nodes.Count != expectedNodes) return false;
        if (count > 0) cards[cards.Count - 1].Nodes = nodes.ToArray();
        try { snapshot = new CardAppearanceSnapshot(time, cards.ToArray()); return true; }
        catch (ArgumentException) { return false; }
    }
}
