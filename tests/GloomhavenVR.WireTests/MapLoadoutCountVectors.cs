using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class MapLoadoutCountVectors
{
    internal static void Run(Harness t)
    {
        t.Case("map loadout83: a resident-held card does not change the public source count");
        var bytes = new byte[PresenceSerializer.MaxSize];
        var state = new PresenceState { MapLoadoutCount = 10 };
        int length = PresenceSerializer.Write(in state, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 53 01 0A"), bytes, length,
            "independent golden83 states complete source without card identity");
        t.True(PresenceSerializer.TryRead(bytes, length, out var read) && read.MapLoadoutCount == 10,
            "reader preserves the source while fan and held counts change independently");
        for (int n = 1; n <= NetProtocol.MaxMapLoadoutCount; n++)
        {
            state.MapLoadoutCount = (byte)n;
            length = PresenceSerializer.Write(in state, bytes);
            t.True(PresenceSerializer.TryRead(bytes, length, out read) && read.MapLoadoutCount == n,
                "bounded complete source count " + n);
        }
        bytes[13] = 255;
        t.True(PresenceSerializer.TryRead(bytes, length, out read) && read.MapLoadoutCount == 0,
            "untrusted out-of-range count cannot override the ordinary resolver");
        state.MapLoadoutCount = 0;
        length = PresenceSerializer.Write(in state, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, length,
            "legacy idle presence remains byte-identical");
        t.True(PresenceSerializer.TryRead(bytes, length, out read) && read.MapLoadoutCount == 0,
            "absence clears the preceding source at scene or character teardown");
        state.MapLoadoutCount = 10;
        for (int size = 11; size < 14; size++)
        {
            var small = new byte[size];
            length = PresenceSerializer.Write(in state, small);
            t.True(length == 11 && small[10] == 0,
                "capacity limit omits the complete record without a partial write " + size);
        }
        var future = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 02 53 02 0A FF 53 00");
        t.True(PresenceSerializer.TryRead(future, future.Length, out read) && read.MapLoadoutCount == 10,
            "future suffix and empty record respect declared lengths");
    }
}
