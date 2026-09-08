using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class ExtrasFragmentVectors
{
    internal static void Run(Harness t)
    {
        t.Case("extras envelopes: independent golden bytes and pacing");
        byte[][] golden = ExtrasFragments.Encode(Hex.Bytes("31 52 56 47 03 01 00 00"), 8, 0x0102030405060708UL);
        t.Wire(Hex.Bytes("31 52 56 47 03 02 30 14 08 07 06 05 04 03 02 01 08 00 00 00 31 52 56 47 03 01 00 00"),
            golden[0], golden[0].Length, "version3 envelope48, LE sequence/length/offset");
        var queue = new ExtrasSendQueue(10);
        var paced = new ExtrasFragments();
        queue.Enqueue(Snapshot(1801), 1801);
        byte[] page0 = queue.Next(0)!;
        t.True(paced.Accept(2, page0, page0.Length, 0) == null, "first paced page is partial");
        queue.Enqueue(Snapshot(800), 800);
        queue.Enqueue(Snapshot(8), 8);
        t.True(queue.Next(0.049) == null, "packing rate bounded");
        byte[] page1 = queue.Next(1)!;
        t.True(paced.Accept(2, page1, page1.Length, 1) == null, "waiting snapshot cannot interrupt current");
        t.True(queue.Next(1) == null, "slow frame does not burst");
        byte[] page2 = queue.Next(2)!;
        t.Equal(1801, paced.Accept(2, page2, page2.Length, 2)!.Length, "in-flight snapshot finishes");
        byte[] latest = queue.Next(3)!;
        t.Equal(8, paced.Accept(2, latest, latest.Length, 3)!.Length, "only latest waiting snapshot follows");
        t.True(queue.Next(4) == null, "queue drains without obsolete middle snapshot");
        queue.Enqueue(Snapshot(800), 800); queue.Clear();
        t.True(queue.Next(5) == null, "session reset drops pending presentation");

        t.Case("legacy version handshake never becomes an empty board on a new reader");
        byte[] announcement = ExtrasVersionAnnouncement.Write(484, "v484");
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 03 06 E4 01 76 34 38 34"),
            announcement, announcement.Length, "legacy readable version-only shape");
        t.True(ExtrasVersionAnnouncement.TryRead(announcement, announcement.Length, out PresenceState version)
            && version.ModBuild == 484 && version.ModVersionText == "v484", "new reader classifies announcement");
        var regular = new byte[PresenceSerializer.MaxSize];
        int regularLength = PresenceSerializer.Write(new PresenceState
            { HasModVersion = true, ModBuild = 484, ShortRestInProgress = true }, regular);
        t.True(!ExtrasVersionAnnouncement.TryRead(regular, regularLength, out _), "extra record remains a real snapshot");
        for (int n = 0; n < announcement.Length; n++)
            t.True(!ExtrasVersionAnnouncement.TryRead(announcement, n, out _), "truncated handshake refused");
        byte[] extended = new byte[announcement.Length + 1];
        Array.Copy(announcement, extended, announcement.Length);
        t.True(!ExtrasVersionAnnouncement.TryRead(extended, extended.Length, out _), "trailing bytes not a handshake");

        t.Case("extras envelopes: bounded events and atomic reordered snapshots");
        foreach (int size in new[] { 8, 199, 200, 201, 800, 801, 1801, 3441, 4096 })
        {
            byte[] original = Snapshot(size);
            byte[][] pages = ExtrasFragments.Encode(original, size, 42);
            var receiver = new ExtrasFragments();
            for (int i = pages.Length - 1; i >= 0; i--)
            {
                t.True(pages[i].Length <= ExtrasFragments.MaxDatagramBytes, "side action fits budget");
                byte[]? result = receiver.Accept(2, pages[i], pages[i].Length, 0);
                if (i > 0) t.True(result == null, "incomplete snapshot cannot clear UI");
                else t.Wire(original, result!, result!.Length, "reordered complete snapshot exact");
                t.True(receiver.Accept(2, pages[i], pages[i].Length, 0) == null, "duplicate inert");
            }
        }
        byte[][] old = ExtrasFragments.Encode(Snapshot(1801), 1801, 100);
        byte[][] fresh = ExtrasFragments.Encode(Snapshot(8), 8, 101);
        var state = new ExtrasFragments();
        t.True(state.Accept(1, old[0], old[0].Length, 0) == null, "old partial retained");
        t.True(state.Accept(1, fresh[0], fresh[0].Length, 0) != null, "small newer snapshot commits");
        foreach (byte[] page in old)
            t.True(state.Accept(1, page, page.Length, 0) == null, "old large snapshot cannot roll UI back");
        state.Clear();
        t.True(state.Accept(1, old[0], old[0].Length, 0) == null, "start missing-page assembly");
        for (int i = 1; i < old.Length; i++)
            t.True(state.Accept(1, old[i], old[i].Length, 6) == null, "expired generation cannot commit");
        t.True(state.Accept(1, fresh[0], fresh[0].Length, 6) != null, "next generation recovers loss");
        state.Forget(1);
        t.True(state.Accept(1, fresh[0], fresh[0].Length, 7) != null, "rejoin resets watermark");
        state.Clear();
        for (int peer = 0; peer < 8; peer++)
            t.True(state.Accept(peer, fresh[0], fresh[0].Length, 0) != null, "independent sender");
        t.True(state.Accept(8, fresh[0], fresh[0].Length, 0) == null, "bounded peer capacity");

        t.Case("extras envelopes: malformed chunks and conflicting retransmissions stay inert");
        for (int length = 0; length < old[0].Length; length++)
        {
            state.Clear();
            t.True(state.Accept(1, old[0], length, 0) == null, "truncated datagram cannot complete");
        }
        state.Clear();
        byte[] malformed = new byte[old[0].Length + 1];
        Array.Copy(old[0], malformed, old[0].Length);
        malformed[20] ^= 1;
        t.True(state.Accept(1, malformed, malformed.Length, 0) == null, "bad trailing TLV refuses whole datagram");
        byte[]? assembled = null;
        foreach (byte[] page in old) assembled = state.Accept(1, page, page.Length, 0) ?? assembled;
        t.True(assembled != null, "invalid datagram did not mutate assembly with its conflicting data");
        byte[] wrong = (byte[])old[0].Clone();
        wrong[18] = 1; // First offset is no longer a canonical chunk boundary.
        state.Clear();
        t.True(state.Accept(1, wrong, wrong.Length, 0) == null, "unaligned chunk refused");
        wrong = (byte[])old[0].Clone();
        wrong[16] = 255; wrong[17] = 255;
        t.True(state.Accept(1, wrong, wrong.Length, 0) == null, "oversized allocation refused");
        state.Accept(1, old[0], old[0].Length, 0);
        wrong = (byte[])old[0].Clone(); wrong[20] ^= 1;
        t.True(state.Accept(1, wrong, wrong.Length, 0) == null, "conflicting duplicate poisons generation");
        foreach (byte[] page in old)
            t.True(state.Accept(1, page, page.Length, 0) == null, "poisoned generation never commits");
        t.True(state.Accept(1, fresh[0], fresh[0].Length, 1) != null, "new generation recovers corruption");
        state.Clear();
        wrong = (byte[])fresh[0].Clone(); wrong[25] = NetProtocol.MsgRig;
        t.True(state.Accept(1, wrong, wrong.Length, 0) == null, "envelope cannot inject rig packet");
        t.True(state.Accept(1, fresh[0], fresh[0].Length + 1, 0) == null, "length beyond array refused");
    }

    private static byte[] Snapshot(int size)
    {
        var bytes = new byte[size];
        byte[] header = Hex.Bytes("31 52 56 47 03 01");
        Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
        for (int i = header.Length; i < size; i++) bytes[i] = (byte)(i * 17);
        return bytes;
    }
}
