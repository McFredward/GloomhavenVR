using System;
using System.Linq;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class CardAppearanceGroupVectors
{
    internal static void Run(Harness t)
    {
        t.Case("69 supplemental native groups preserve legacy58 and complete dynamic hierarchies");
        var card = MakeCard(1, 1);
        var buffer = new byte[CardAppearanceCodec.MaxSize];
        int length = CardAppearanceCodec.Write(new CardAppearanceSnapshot(2, new[] { card }), buffer);
        int extension = FindRecord(buffer, length, 69);
        t.True(extension > 0, "a ninth native group has an additive page rather than overflowing legacy roles");
        byte[] page = buffer.Skip(extension).Take(length - extension).ToArray();
        t.Wire(Hex.Bytes("45 0C 00 01 00 01 01 00 00 07 00 00 00 3F"), page, page.Length,
            "supplemental group exact bytes: index, count, offset, binding, flags, alpha");
        t.True(CardAppearanceCodec.TryRead(buffer, length, out var read)
            && CardAppearanceState.Same(card, read!.States[0]), "nine native groups survive an atomic publication");
        var legacy = card.Copy(); legacy.ExtraGroups = Array.Empty<CardAppearanceNode>();
        byte[] legacyBytes = new byte[CardAppearanceCodec.MaxSize];
        int legacyLength = CardAppearanceCodec.Write(new CardAppearanceSnapshot(2, new[] { legacy }), legacyBytes);
        t.Wire(legacyBytes.Take(legacyLength).ToArray(), buffer, extension, "all record58 bytes remain unchanged");
        for (int n = extension + 1; n < length; n++)
            t.True(!CardAppearanceCodec.TryRead(buffer, n, out _), "an incomplete supplemental page never publishes");
        foreach (int offset in new[] { 2, 3, 4, 5, 9, 13 })
        {
            byte[] bad = buffer.Take(length).ToArray();
            bad[extension + offset] = offset == 3 ? (byte)0 : (byte)255;
            // Binding corruption by itself is valid; use zero across the whole key.
            if (offset == 5) Array.Clear(bad, extension + 5, 4);
            t.True(!CardAppearanceCodec.TryRead(bad, bad.Length, out _), "malformed supplemental field rejects: " + offset);
        }
        var duplicate = card.Copy(); duplicate.ExtraGroups[0].Binding = duplicate.Nodes[12].Binding;
        t.True(!duplicate.Validate(), "a group cannot occur in both legacy and supplemental pages");
        duplicate = card.Copy(); duplicate.ExtraGroups[0].Values[0] = float.NaN;
        t.True(!duplicate.Validate(), "nonfinite group alpha cannot enter a snapshot");
        var snapshot = new CardAppearanceSnapshot(3, new[] { card });
        card.ExtraGroups[0].Values[0] = .25f;
        t.Equal(.5f, snapshot.States[0].ExtraGroups[0].Values[0], "publication owns a deep copy of supplemental state");
        t.True(!CardAppearanceState.Same(card, snapshot.States[0]), "supplemental animation changes trigger publication");

        var maximum = new CardAppearanceSnapshot(4, Enumerable.Range(1, 32).Select(i => MakeCard(i, 56)).ToArray());
        length = CardAppearanceCodec.Write(maximum, buffer);
        t.Equal(57766, length, "all 32 cards, shader channels, provenance and 64 groups fit exact budget");
        t.True(length < CardAppearanceCodec.MaxSize && CardAppearanceCodec.MaxSize <= ushort.MaxValue,
            "the existing fragment length representation remains sufficient");
        t.True(CardAppearanceCodec.TryRead(buffer, length, out read), "maximum dynamic hierarchy parses completely");
        if (read != null) for (int i = 0; i < maximum.States.Length; i++)
            t.True(CardAppearanceState.Same(maximum.States[i], read.States[i]), "all supplemental group properties preserved");

        int firstPage = FindRecord(buffer, length, 69);
        int firstPageEnd = firstPage + 2 + buffer[firstPage + 1];
        t.True(!CardAppearanceCodec.TryRead(buffer, firstPageEnd, out _), "a complete first page cannot publish half a hierarchy");
        var repeated = buffer.Take(length).ToArray(); repeated[firstPageEnd + 4] = 0;
        t.True(!CardAppearanceCodec.TryRead(repeated, length, out _), "a repeated or overlapping supplemental page rejects");
        repeated = buffer.Take(length).ToArray(); repeated[firstPageEnd + 3]--;
        t.True(!CardAppearanceCodec.TryRead(repeated, length, out _), "supplemental total must agree across pages");

        foreach (bool compressed in new[] { false, true })
        {
            byte[][] pages = ExtrasFragments.Encode(buffer, length, 7, CardAppearanceCodec.Message,
                CardAppearanceCodec.FragmentMessage, CardAppearanceCodec.MaxSize, compress: compressed);
            var receiver = new ExtrasFragments(CardAppearanceCodec.Message, CardAppearanceCodec.FragmentMessage, CardAppearanceCodec.MaxSize);
            byte[]? restored = null;
            foreach (byte[] packet in pages.Reverse())
            {
                t.True(packet.Length <= ExtrasFragments.MaxDatagramBytes, "supplemental groups respect the physical event budget");
                restored = receiver.Accept(2, packet, packet.Length, 0) ?? restored;
            }
            t.True(restored != null && restored.SequenceEqual(buffer.Take(length)),
                "complete extended frame survives out-of-order transport, compression=" + compressed);
        }
    }

    private static CardAppearanceState MakeCard(int actor, int extra)
    {
        var nodes = new CardAppearanceNode[20];
        for (byte role = 0; role < nodes.Length; role++)
        {
            var node = new CardAppearanceNode { Role = role, Binding = role >= 12 ? (uint)(role + 1) : 0,
                Flags = (byte)(role == 11 ? 11 : 3), Mask = CardAppearanceNode.AllowedMask(role) };
            for (int v = 0; v < node.Values.Length; v++)
                if (role < 12 && CardAppearanceNode.Carries(node.Mask, v)) node.Values[v] = (v + 1) * .03125f;
            if (role >= 12) node.Values[0] = .5f;
            nodes[role] = node;
        }
        var groups = new CardAppearanceNode[extra];
        for (int i = 0; i < extra; i++)
        {
            groups[i] = new CardAppearanceNode { Role = 12, Binding = (uint)(257 + i), Flags = 7 };
            groups[i].Values[0] = .5f;
        }
        return new CardAppearanceState { ActorId = actor, SourceActorId = actor, PoolCount = 1,
            FaceCode = 0x20, ListCount = 1, Nodes = nodes, ExtraGroups = groups };
    }
    private static int FindRecord(byte[] buffer, int length, byte record)
    {
        for (int p = 6; p + 2 <= length; p += 2 + buffer[p + 1]) if (buffer[p] == record) return p;
        return -1;
    }
}
