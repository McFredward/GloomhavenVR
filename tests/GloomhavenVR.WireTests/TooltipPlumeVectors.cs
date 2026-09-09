using System;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class TooltipPlumeVectors
{
    internal static void Run(Harness t)
    {
        t.Case("56: original item-tooltip smoke shares the bounded native particle stream");
        var tooltip = new CardPlumeState { ActorId = 42, Source = CardPlumeState.BonusTooltipSource,
            BonusSlot = 2, BonusIdentity = 0x1234, Flags = CardPlumeState.Playing,
            Episode = 7, RandomSeed = 9, Age = .5f, PlaybackRate = 1,
            StartSizeMultiplier = .35f, StartSpeedMultiplier = .35f,
            Color = new Color(.1f, .2f, .3f, .4f), LocalPosition = new Vector3(1, 2, 3),
            LocalRotation = Quaternion.identity, LocalScale = Vector3.one * .01f };
        var buffer = new byte[CardPlumeCodec.MaxSize];
        t.True(tooltip.MatchesTooltipOwner(42, 0x1234), "current board actor and45 identity resolve the original tooltip");
        t.True(!tooltip.MatchesTooltipOwner(43, 0x1234), "same bonus identity on another actor cannot retain old tooltip smoke");
        t.True(!tooltip.MatchesTooltipOwner(42, 0x1235), "new slot identity cannot inherit old tooltip smoke");
        var frame = new CardPlumeSnapshot(2, new[] { tooltip });
        int length = CardPlumeCodec.Write(frame, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 05 38 60 00 01 00 00 00 40 2a 00 00 00 01 02 34 12 01 00 07 00 00 00 09 00 00 00 00 00 00 3f 00 00 80 3f 33 33 b3 3e 33 33 b3 3e cd cc cc 3d cd cc 4c 3e 9a 99 99 3e cd cc cc 3e 00 00 80 3f 00 00 00 40 00 00 40 40 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3f 0a d7 23 3c 0a d7 23 3c 0a d7 23 3c"), buffer, length,
            "56 adds an explicit source/slot/45-identity address and preserves native particle output");
        t.True(CardPlumeCodec.TryRead(buffer, length, out CardPlumeSnapshot? parsed)
            && CardPlumeState.Same(tooltip, parsed!.States[0]), "56 golden decodes exactly");
        var golden = new byte[length]; Array.Copy(buffer, golden, length);
        var ordinary = tooltip.Snapshot(); ordinary.Source = 0; ordinary.BonusSlot = 0;
        ordinary.BonusIdentity = 0; ordinary.FaceCode = 34; ordinary.ListCount = 3;
        var mixed = new CardPlumeSnapshot(2, new[] { ordinary, tooltip });
        length = CardPlumeCodec.Write(mixed, buffer);
        t.True(CardPlumeCodec.TryRead(buffer, length, out parsed) && parsed!.States.Length == 2
            && parsed.States[0].Source == 0 && parsed.States[1].Source == 1, "ordinary and tooltip emitters coexist atomically");
        for (int n = 0; n < length; n++)
            t.True(!CardPlumeCodec.TryRead(buffer, n, out _), "a missing tooltip tail cannot publish the ordinary prefix");
        var bad = new byte[length]; Array.Copy(buffer, bad, length); bad[102] = 99;
        t.True(!CardPlumeCodec.TryRead(bad, length, out _), "unknown replacement for required56 cannot publish a partial frame");
        foreach (int offset in new[] { 18, 19, 20 })
        {
            bad = (byte[])golden.Clone();
            if (offset == 18) bad[offset] = 0; //56 cannot impersonate namespace51
            if (offset == 19) bad[offset] = 8;
            if (offset == 20) { bad[20] = 0; bad[21] = 0; }
            t.True(!CardPlumeCodec.TryRead(bad, bad.Length, out _), "invalid namespace/slot/identity poisons56");
        }
        bad = (byte[])golden.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, bad, 32, 4);
        t.True(!CardPlumeCodec.TryRead(bad, bad.Length, out _), "non-finite native56 age cannot reach Unity particle simulation");
        var duplicate = new byte[golden.Length * 2 - 6];
        Array.Copy(golden, duplicate, golden.Length); Array.Copy(golden, 6, duplicate, golden.Length, golden.Length - 6);
        t.True(CardPlumeCodec.TryRead(duplicate, duplicate.Length, out _), "identical56 retransmission is inert");
        duplicate[duplicate.Length - 1] ^= 1;
        t.True(!CardPlumeCodec.TryRead(duplicate, duplicate.Length, out _), "conflicting56 duplicate poisons entire snapshot");
        var other = tooltip.Snapshot(); other.BonusSlot++;
        t.True(!CardPlumeState.SameAddress(tooltip, other), "same item tooltip in different bonus slots is a different source");
        int twinsLength = CardPlumeCodec.Write(new CardPlumeSnapshot(2, new[] { tooltip, other }), buffer);
        t.True(CardPlumeCodec.TryRead(buffer, twinsLength, out parsed) && parsed!.States.Length == 2,
            "same45 identity in distinct original slots can have separate live emitter episodes");
        other = tooltip.Snapshot(); other.BonusIdentity++;
        t.True(!CardPlumeState.SameAddress(tooltip, other), "recycled slot with new45 identity is a different source");
        t.True(!CardPlumeState.SameAddress(tooltip, ordinary), "ordinary item and tooltip namespaces never alias");
        t.True(!PresentationPending.SamePlumeIdentity(frame, new CardPlumeSnapshot(3, new[] { other })),
            "pending queue retains slot-identity replacement boundary despite same native episode");
        t.True(!PresentationPending.SamePlumeIdentity(frame, new CardPlumeSnapshot(3, new[] { ordinary })),
            "pending queue retains namespace replacement boundary");
        var maximum = new CardPlumeState[64];
        for (int i = 0; i < maximum.Length; i++)
        {
            maximum[i] = tooltip.Snapshot(); maximum[i].BonusSlot = (byte)(i % 8); maximum[i].BonusIdentity = (ushort)(i + 1);
            maximum[i].Flags |= 96; maximum[i].EmitterLocalScale = Vector3.one;
            maximum[i].CustomSpacePresent = true; maximum[i].CustomRotation = Quaternion.identity;
            maximum[i].CustomScale = Vector3.one;
        }
        length = CardPlumeCodec.Write(new CardPlumeSnapshot(3, maximum), buffer);
        t.Equal(9670, length, "64 tooltip emitters with all conditional fields fit the fixed bound");
        t.Equal(CardPlumeCodec.MaxEncodedBytes, length, "documented combined worst case is exact");
        t.True(length < 12288 && CardPlumeCodec.TryRead(buffer, length, out parsed) && parsed!.States.Length == 64,
            "complete saturated snapshot fits unchanged transport budget");
        for (int n = 0; n < length; n++)
            t.True(!CardPlumeCodec.TryRead(buffer, n, out _), "saturated56 snapshot remains atomic at every truncation");
        bool refused = false;
        try { _ = new CardPlumeSnapshot(4, new CardPlumeState[65]); } catch (ArgumentException) { refused = true; }
        t.True(refused, "combined cap refuses overflow instead of truncating live original particles");
        refused = false;
        try { _ = new CardPlumeSnapshot(4, new[] { tooltip, tooltip }); } catch (ArgumentException) { refused = true; }
        t.True(refused, "duplicate namespace/slot/identity/emitter is invalid");
        other = tooltip.Snapshot(); other.FaceCode = ordinary.FaceCode;
        t.True(!other.Validate(), "tooltip address cannot carry a regular-card identity");
        length = CardPlumeCodec.Write(new CardPlumeSnapshot(5, Array.Empty<CardPlumeState>()), buffer);
        t.True(CardPlumeCodec.TryRead(buffer, length, out parsed) && parsed!.States.Length == 0,
            "existing clear frame withdraws both namespaces");
    }
}
