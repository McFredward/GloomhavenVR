using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class MapContinuationVectors
{
    internal static void Run(Harness t)
    {
        t.Case("native map continuation83/84: independent golden records and presence integration");
        foreach (bool reward in new[] { false, true })
        {
            var entries = new[] { new MapStoryOpening {
                Epoch = 1, Token = 2, PreviousToken = 1, SemanticKey = 0x11223344, ContentKey = 0x55667788,
                Page = 0, PageCount = 1, Finished = true, TotalParticipants = 1, Participants = new[] { 9 } } };
            var state = new PresenceState {
                HasMapStoryLifecycle = !reward, MapStoryLifecycleEntries = reward ? null : entries,
                HasRewardContinuation = reward, RewardContinuationEntries = reward ? entries : null };
            byte id = reward ? (byte)84 : (byte)83;
            byte[] golden = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 53 23 01 "
                + "01 00 00 00 02 00 00 00 01 00 00 00 44 33 22 11 88 77 66 55 00 00 00 00 00 01 01 01 01 00 09 00 00 00");
            golden[11] = id;
            var bytes = new byte[PresenceSerializer.MaxSize];
            int length = PresenceSerializer.Write(in state, bytes);
            t.Wire(golden, bytes, length, "opening provenance golden " + id);
            t.True(PresenceSerializer.TryRead(golden, golden.Length, out var decoded), "independent golden decodes " + id);
            var read = reward ? decoded.RewardContinuationEntries : decoded.MapStoryLifecycleEntries;
            t.True((reward ? decoded.HasRewardContinuation : decoded.HasMapStoryLifecycle)
                && read?.Length == 1 && read[0].Epoch == 1 && read[0].Token == 2 && read[0].PreviousToken == 1
                && read[0].SemanticKey == 0x11223344 && read[0].ContentKey == 0x55667788
                && read[0].Finished && read[0].Participants.Length == 1 && read[0].Participants[0] == 9,
                "reader preserves scoped completion and recipients " + id);
            for (int cut = 0; cut < golden.Length; cut++)
            {
                bool ok = PresenceSerializer.TryRead(golden, cut, out decoded);
                t.True(!ok || !(reward ? decoded.HasRewardContinuation : decoded.HasMapStoryLifecycle),
                    "truncated opening cannot complete native UI " + id + "/" + cut);
            }
            // Invalid record must not consume the following version record or manufacture completion.
            foreach (int corrupt in new[] { 13, 38, 39, 40, 41, 42 })
            {
                var bad = new byte[golden.Length + 4];
                Array.Copy(golden, bad, golden.Length);
                bad[10] = 2;
                bad[corrupt] = (corrupt == 39 || corrupt == 42) ? (byte)0 : (byte)0xFE;
                bad[golden.Length] = 3; bad[golden.Length + 1] = 2;
                bad[golden.Length + 2] = 43; bad[golden.Length + 3] = 2;
                t.True(PresenceSerializer.TryRead(bad, bad.Length, out decoded)
                    && !(reward ? decoded.HasRewardContinuation : decoded.HasMapStoryLifecycle)
                    && decoded.HasModVersion && decoded.ModBuild == 555,
                    "malformed lifecycle skipped without losing following TLV " + id + "/" + corrupt);
            }
            for (int size = 11; size < golden.Length; size++)
            {
                var limited = new byte[size];
                Array.Fill(limited, (byte)0xCD);
                int written = PresenceSerializer.Write(in state, limited);
                t.True(written == 11 && limited[10] == 0, "insufficient capacity omits complete record " + id + "/" + size);
                bool untouched = true;
                for (int i = 11; i < size; i++) untouched &= limited[i] == 0xCD;
                t.True(untouched, "no partial lifecycle write " + id + "/" + size);
            }
        }
        t.True(NetProtocol.Version == 3 && NetProtocol.ExtIdMapStoryLifecycle == 83
            && NetProtocol.ExtIdRewardContinuation == 84, "additive map lifecycle retains v3 registry");
        var maximum = new MapStoryOpening[6];
        for (int i = 0; i < maximum.Length; i++)
            maximum[i] = new MapStoryOpening { Epoch = 7, Token = (uint)(i + 1), SemanticKey = 11,
                ContentKey = 22, Page = 0, PageCount = 1, TotalParticipants = 3, Participants = new[] { 1, 2, 3 } };
        var both = new PresenceState { HasMapStoryLifecycle = true, MapStoryLifecycleEntries = maximum,
            HasRewardContinuation = true, RewardContinuationEntries = maximum };
        var full = new byte[PresenceSerializer.MaxSize];
        int fullLength = PresenceSerializer.Write(in both, full);
        t.True(fullLength == 521 && full[10] == 2 && full[11] == 83 && full[12] == 253
            && full[266] == 84 && full[267] == 253, "both maximum histories fit one-byte TLV lengths without wrapping");
        t.True(PresenceSerializer.TryRead(full, fullLength, out var maxRead)
            && maxRead.HasMapStoryLifecycle && maxRead.MapStoryLifecycleEntries?.Length == 6
            && maxRead.HasRewardContinuation && maxRead.RewardContinuationEntries?.Length == 6,
            "simultaneous maximum story and reward histories remain independently readable");
    }
}
