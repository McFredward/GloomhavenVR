using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class RewardPoseHandshakeVectors
{
    internal static void Run(Harness t)
    {
        t.Case("reward pose handshake: additive74 golden request and explicit decline");
        var state = new RewardPoseHandshakeState { Key = 0x12345678, Generation = 2, Count = 1,
            First = new RewardPoseDecline { Requester = 3, Key = 0xAABBCCDD, Generation = 4 } };
        var presence = new PresenceState { HasRewardPoseHandshake = true, RewardPoseHandshake = state };
        var bytes = new byte[PresenceSerializer.MaxSize];
        int length = PresenceSerializer.Write(in presence, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 4A 15 78 56 34 12 02 00 00 00 01 03 00 00 00 DD CC BB AA 04 00 00 00"), bytes, length,
            "request/reply carries no reward content or continuation action");
        t.True(PresenceSerializer.TryRead(bytes, length, out var decoded) && decoded.HasRewardPoseHandshake
            && decoded.RewardPoseHandshake.First.Key == 0xAABBCCDD && !decoded.HasRewardWindow,
            "handshake absence of native presentation remains explicit");
        presence.HasRewardWindow = true;
        presence.RewardWindow = new RewardWindowState { Window = new SharedWindowEntry { ContentKey = 0x12345678, Flags = NetProtocol.SharedOpenBit } };
        length = PresenceSerializer.Write(in presence, bytes);
        t.True(PresenceSerializer.TryRead(bytes, length, out decoded) && decoded.HasRewardPoseHandshake && decoded.HasRewardWindow,
            "both records coexist without replacing reward73");
        presence = default;
        length = PresenceSerializer.Write(in presence, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, length, "no active handshake preserves legacy omission bytes");
        t.True(PresenceSerializer.TryRead(bytes, length, out decoded) && !decoded.HasRewardPoseHandshake,
            "omission clears a previous handshake snapshot");

        t.Case("reward pose handshake: malformed and maximum bounded records");
        state.Count = 4;
        for (int i = 0; i < 4; i++) state.Set(i, new RewardPoseDecline { Requester = i + 1, Key = (uint)(10 + i), Generation = (uint)(20 + i) });
        int cursor = 0;
        t.True(RewardPoseHandshakeCodec.Write(bytes, ref cursor, in state), "four declines fit");
        t.Equal(59, cursor, "record74 maximum including TLV");
        for (int n = 0; n < 57; n++)
            t.True(!RewardPoseHandshakeCodec.TryRead(bytes, 2, n, out _), "truncated four-decline payload rejected " + n);
        foreach (int offset in new[] { -1, bytes.Length, int.MaxValue })
            t.True(!RewardPoseHandshakeCodec.TryRead(bytes, offset, 57, out _), "invalid offset rejected " + offset);
        var bad = (byte[])bytes.Clone(); bad[10] = 5;
        t.True(!RewardPoseHandshakeCodec.TryRead(bad, 2, 57, out _), "excess count rejected");
        bad = (byte[])bytes.Clone(); Array.Clear(bad, 11, 4);
        t.True(!RewardPoseHandshakeCodec.TryRead(bad, 2, 57, out _), "zero requester rejected");
        bad = (byte[])bytes.Clone(); Array.Clear(bad, 19, 4);
        t.True(!RewardPoseHandshakeCodec.TryRead(bad, 2, 57, out _), "zero requester generation rejected");
        bad = (byte[])bytes.Clone(); Array.Copy(bad, 11, bad, 23, 4);
        t.True(!RewardPoseHandshakeCodec.TryRead(bad, 2, 57, out _), "duplicate requester rejected");
        cursor = 1;
        t.True(!RewardPoseHandshakeCodec.Write(new byte[59], ref cursor, in state) && cursor == 1, "too-small buffer remains unchanged");
        t.True(4074 + 59 <= ExtrasFragments.MaxSnapshotBytes && PresenceSerializer.MaxSize - (4074 + 59) >= 257,
            "new full record retains bounded transport and one-record spare sender margin");
    }
}
