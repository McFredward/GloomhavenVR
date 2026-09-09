using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class CardFxVisibilityVectors
{
    internal static void Run(Harness t)
    {
        t.Case("54: covered burn provenance follows its semantic sequence after context closes");
        var buffer = new byte[PresenceSerializer.MaxSize];
        var sent = new PresenceState { HasCardFx = true, FxSeq = 42, FxEndpoints = 0x41,
            HasCardFxVisibility = true, FxVisibilitySeq = 42, FxVisibilityFlags = 1 };
        int length = PresenceSerializer.Write(sent, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 A0 00 2A 41 80 00 01 36 02 2A 01"), buffer, length,
            "additive54 changes no existing card-event byte and carries no card identity");
        t.True(PresenceSerializer.TryRead(buffer, length, out PresenceState read)
            && read.HasCardFxVisibility && read.FxVisibilitySeq == 42 && read.FxVisibilityFlags == 1
            && !read.ShortRestInProgress, "covered provenance survives the closing short-rest snapshot");
        sent.HasCardFxVisibility = false;
        length = PresenceSerializer.Write(sent, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 20 00 2A 41"), buffer, length,
            "old event shape remains identical without54");
        sent.HasCardFxVisibility = true;
        for (int seq = 0; seq <= 255; seq++)
        {
            sent.FxSeq = sent.FxVisibilitySeq = (byte)seq;
            length = PresenceSerializer.Write(sent, buffer);
            t.True(PresenceSerializer.TryRead(buffer, length, out read)
                && read.HasCardFxVisibility && read.FxVisibilitySeq == seq && read.FxVisibilityFlags == 1,
                "all wrapping sequence values retain their own flag");
        }
        t.Case("54: unrelated and malformed modifiers cannot bind to an event");
        byte[] packet = Hex.Bytes("31 52 56 47 03 01 A0 00 2A 41 80 00 02 36 02 2B 01 2E 01 01");
        t.True(PresenceSerializer.TryRead(packet, packet.Length, out read)
            && !read.HasCardFxVisibility && read.ShortRestInProgress,
            "wrong sequence is ignored without swallowing the following record");
        packet[15] = 42;
        for (int flags = 2; flags <= 255; flags++)
        {
            packet[16] = (byte)flags;
            t.True(PresenceSerializer.TryRead(packet, packet.Length, out read)
                && !read.HasCardFxVisibility && read.ShortRestInProgress, "unknown flags cannot invent permission");
        }
        packet = Hex.Bytes("31 52 56 47 03 01 A0 00 2A 41 80 00 02 36 01 2A 2E 01 01");
        t.True(PresenceSerializer.TryRead(packet, packet.Length, out read)
            && !read.HasCardFxVisibility && read.ShortRestInProgress,
            "short54 cannot borrow its flag from the next TLV");
        sent.HasCardFx = false;
        length = PresenceSerializer.Write(sent, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), buffer, length, "orphan provenance is omitted");
    }
}
