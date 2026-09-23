using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class SharedWindowMotionVectors
{
    internal static void Run(Harness t)
    {
        t.Case("shared window motion77: exact grip, automatic movement and explicit release");
        var bytes = new byte[PresenceSerializer.MaxSize];
        var state = new PresenceState
        {
            HasSharedWindowMotion = true,
            SharedWindowHeldMask = NetProtocol.SharedWindowMotionMapStoryBit | NetProtocol.SharedWindowMotionEncounterBit,
            SharedWindowReflowMask = NetProtocol.SharedWindowMotionQuestConfirmBit,
        };
        int length = PresenceSerializer.Write(in state, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 4D 02 05 02"), bytes, length,
            "golden77 keeps held story/encounter separate from automatically moving quest");
        t.True(PresenceSerializer.TryRead(bytes, length, out var decoded) && decoded.HasSharedWindowMotion
            && decoded.SharedWindowHeldMask == 5 && decoded.SharedWindowReflowMask == 2,
            "production reader decodes independent motion masks");
        state.SharedWindowHeldMask = 0;
        state.SharedWindowReflowMask = 0;
        length = PresenceSerializer.Write(in state, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 4D 02 00 00"), bytes, length,
            "all-zero release remains an explicit record");
        t.True(PresenceSerializer.TryRead(bytes, length, out decoded) && decoded.HasSharedWindowMotion
            && decoded.SharedWindowHeldMask == 0 && decoded.SharedWindowReflowMask == 0,
            "release cannot be mistaken for a peer that supplied no ownership metadata");
        state.HasSharedWindowMotion = false;
        state.SharedWindowHeldMask = 7;
        state.SharedWindowReflowMask = 7;
        length = PresenceSerializer.Write(in state, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, length,
            "absence keeps the legacy idle extras byte-identical");
        t.True(PresenceSerializer.TryRead(bytes, length, out decoded) && !decoded.HasSharedWindowMotion
            && decoded.SharedWindowHeldMask == 0 && decoded.SharedWindowReflowMask == 0,
            "a fresh decode cannot retain a preceding grip or manufacture confirmed release");

        t.Case("shared window motion77: masks sanitize on both independent codec boundaries");
        state.HasSharedWindowMotion = true;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            state.SharedWindowHeldMask = (byte)value;
            state.SharedWindowReflowMask = (byte)(255 - value);
            length = PresenceSerializer.Write(in state, bytes);
            t.True(length == 15 && bytes[11] == 77 && bytes[12] == 2
                && bytes[13] == (value & 7) && bytes[14] == ((255 - value) & 7),
                "writer strips reserved bits for both masks " + value);
            // Deliberately bypass the writer's sanitization: a matching round trip cannot
            // prove that a reader independently ignores a future sender's unknown bits.
            bytes[13] = (byte)value;
            bytes[14] = (byte)(255 - value);
            t.True(PresenceSerializer.TryRead(bytes, length, out decoded) && decoded.HasSharedWindowMotion
                && decoded.SharedWindowHeldMask == (value & 7)
                && decoded.SharedWindowReflowMask == ((255 - value) & 7),
                "reader independently strips reserved bits " + value);
        }

        t.Case("shared window motion77: short, truncated and future records remain bounded");
        foreach (string shortRecord in new[]
        {
            "31 52 56 47 03 01 80 00 80 00 02 4D 00 03 02 06 02",
            "31 52 56 47 03 01 80 00 80 00 02 4D 01 07 03 02 06 02",
        })
        {
            byte[] packet = Hex.Bytes(shortRecord);
            t.True(PresenceSerializer.TryRead(packet, packet.Length, out decoded)
                && !decoded.HasSharedWindowMotion && decoded.HasModVersion && decoded.ModBuild == 518,
                "short77 makes no ownership statement and preserves the following native version record");
        }
        byte[] complete = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 4D 02 07 01");
        for (int cut = 0; cut < complete.Length; cut++)
        {
            bool read = PresenceSerializer.TryRead(complete, cut, out decoded);
            t.True(!read || !decoded.HasSharedWindowMotion,
                "truncation cannot manufacture complete ownership metadata at " + cut);
        }
        byte[] future = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 03 FE 03 4D 02 FF 4D 04 03 04 AA BB 03 02 06 02");
        t.True(PresenceSerializer.TryRead(future, future.Length, out decoded)
            && decoded.HasSharedWindowMotion && decoded.SharedWindowHeldMask == 3
            && decoded.SharedWindowReflowMask == 4 && decoded.HasModVersion && decoded.ModBuild == 518,
            "unknown preceding TLV and future77 suffix are skipped by declared length");
        state.SharedWindowHeldMask = 5;
        state.SharedWindowReflowMask = 2;
        for (int size = 11; size < 15; size++)
        {
            var limited = new byte[size];
            Array.Fill(limited, (byte)0xCD);
            length = PresenceSerializer.Write(in state, limited);
            t.True(length == 11 && limited[10] == 0,
                "insufficient capacity omits the entire77 record at " + size);
            bool untouched = true;
            for (int i = 11; i < size; i++) untouched &= limited[i] == 0xCD;
            t.True(untouched, "insufficient capacity writes no partial77 bytes at " + size);
        }
        t.True(NetProtocol.Version == 3 && NetProtocol.ExtIdSharedWindowMotion == 77
            && NetProtocol.SharedWindowMotionRecordBytes == 2 && NetProtocol.SharedWindowMotionDefinedMask == 7,
            "additive ownership leaves v3 and all earlier record identities unchanged");
        t.True(6869 <= ExtrasFragments.MaxSnapshotBytes && PresenceSerializer.MaxSize == 7393
            && PresenceSerializer.MaxSize - 6869 >= 257,
            "record77 margin remains intact after resident79 adds117 and face80 adds96 allocation bytes");
    }
}
