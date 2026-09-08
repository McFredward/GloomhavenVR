using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class ShortRestStateVectors
{
    public static void Run(Harness t)
    {
        t.Case("46: explicit short-rest state is independent of a sacrifice seat");
        var buffer = new byte[PresenceSerializer.MaxSize];
        int count = PresenceSerializer.Write(new PresenceState { ShortRestInProgress = true }, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 2E 01 01"), buffer, count,
               "record 46 appends one flags byte and leaves version 3 unchanged");
        t.True(PresenceSerializer.TryRead(buffer, count, out PresenceState read), "short rest parses");
        t.True(read.ShortRestInProgress && !read.HasSacrificeSeat,
               "choice remains secret when there is no resolvable sacrifice seat");
        count = PresenceSerializer.Write(new PresenceState(), buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), buffer, count, "idle packet is unchanged");
        t.True(PresenceSerializer.TryRead(buffer, count, out read) && !read.ShortRestInProgress,
               "omission clears the previous choice state");

        for (int flags = 0; flags <= 255; flags++)
        {
            byte[] packet = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 02 2E 02 00 AA 2B 01 02");
            packet[13] = (byte)flags;
            t.True(PresenceSerializer.TryRead(packet, packet.Length, out read), "future flags parse");
            t.True(read.ShortRestInProgress == ((flags & 1) != 0), "only bit 0 decides short rest");
            t.True(read.HasFanSource && read.FanSourceList == 2, "following TLV survives extra byte");
        }
        byte[] empty = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 02 2E 00 2B 01 02");
        t.True(PresenceSerializer.TryRead(empty, empty.Length, out read)
               && !read.ShortRestInProgress && read.HasFanSource,
               "empty malformed short-rest record cannot borrow the following record's byte");

        t.Case("card FX: repeated and reordered datagrams never replay an old flight");
        for (int accepted = 0; accepted <= 255; accepted++)
        {
            t.True(!NetProtocol.IsNewCardFxSequence((byte)accepted, (byte)accepted), "redundant event");
            t.True(NetProtocol.IsNewCardFxSequence((byte)(accepted + 1), (byte)accepted), "next, wrapping");
            t.True(!NetProtocol.IsNewCardFxSequence((byte)(accepted - 1), (byte)accepted), "reordered older");
            t.True(!NetProtocol.IsNewCardFxSequence((byte)(accepted + 128), (byte)accepted), "ambiguous half-cycle");
            t.True(NetProtocol.IsNewCardFxSequence((byte)(accepted + 32), (byte)accepted), "forward packet loss");
        }
        t.Case("burn release: an invisible local coroutine never launches an occupied owner recess");
        t.True(!BurnReleasePolicy.MayFallback(true, 0, 1, 0.51f, 3f), "MB482 early phantom is refused");
        t.True(!BurnReleasePolicy.MayFallback(true, 0, 1, 60f, 3f), "still seated through the turn");
        t.True(!BurnReleasePolicy.MayFallback(true, 0, 0, 1f, 3f), "empty seat waits for owner event");
        t.True(BurnReleasePolicy.MayFallback(true, 0, 0, 3f, 3f), "lost event has bounded fallback");
        t.True(BurnReleasePolicy.MayFallback(true, 1, 1, 3f, 3f), "other recess does not block release");
        t.True(BurnReleasePolicy.MayFallback(false, -1, 0, 3f, 3f), "unseatable legacy burn remains bounded");

        t.Case("use slot: native mandatory highlight has a distinct additive state bit");
        t.Equal(32, (int)NetProtocol.UseSlotMandatoryBit, "mandatory is bit 5");
        t.Equal(63, (int)NetProtocol.UseSlotDefinedMask, "all six states survive sanitization");
    }
}
