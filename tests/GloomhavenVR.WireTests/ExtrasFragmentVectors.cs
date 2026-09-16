using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class ExtrasFragmentVectors
{
    internal static void Run(Harness t)
    {
        AnimationStreams(t);
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
        foreach (int size in new[] { 8, 199, 200, 201, 800, 801, 1801, 3449, 4096, 4133, 4177, 4352 })
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
        t.Case("extras envelopes: reward handshake maximum across four peers");
        var rewardReceiver = new ExtrasFragments();
        byte[] rewardMaximum = Snapshot(4177);
        byte[][] rewardPages = ExtrasFragments.Encode(rewardMaximum, rewardMaximum.Length, 42);
        t.Equal(6, rewardPages.Length, "all worst-case records including74 fit six existing envelopes");
        for (int page = rewardPages.Length - 1; page >= 0; page--)
            for (int peer = 1; peer <= 4; peer++)
            {
                byte[]? result = rewardReceiver.Accept(peer, rewardPages[page], rewardPages[page].Length, 0);
                if (page != 0) t.True(result == null, "interleaved peers never commit partial reward handshake");
                else t.Wire(rewardMaximum, result!, result!.Length, "independent complete reward handshake snapshot");
            }
        bool oversizedRejected = false;
        try { ExtrasFragments.Encode(Snapshot(4353), 4353, 99); }
        catch (ArgumentException) { oversizedRejected = true; }
        t.True(oversizedRejected, "one byte above new reassembly bound refuses sender allocation");
        var beyondBound = (byte[])rewardPages[0].Clone();
        beyondBound[16] = 1; beyondBound[17] = 17; //4353 little endian
        t.True(rewardReceiver.Accept(5, beyondBound, beyondBound.Length, 0) == null,
            "one byte above new reassembly bound refuses receiver allocation");
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

    private static byte[] Motion(int size)
    {
        byte[] bytes = Snapshot(size);
        bytes[5] = NetProtocol.MsgUseBarAnimation;
        return bytes;
    }

    private static void AnimationStreams(Harness t)
    {
        const byte motionType = NetProtocol.MsgUseBarAnimation;
        const byte envelopeType = NetProtocol.MsgUseBarAnimationFragments;
        t.Case("native animation transport: independent framing and watermarks");
        byte[][] golden = ExtrasFragments.Encode(Hex.Bytes("31 52 56 47 03 03 00 00"), 8,
            0x0102030405060708UL, motionType, envelopeType);
        t.Wire(Hex.Bytes("31 52 56 47 03 04 30 14 08 07 06 05 04 03 02 01 08 00 00 00 31 52 56 47 03 03 00 00"),
            golden[0], golden[0].Length, "native envelope4 preserves independent message3");
        var presence = new ExtrasFragments();
        var motion = new ExtrasFragments(motionType, envelopeType);
        byte[][] basePages = ExtrasFragments.Encode(Snapshot(1801), 1801, 90);
        byte[][] motionPages = ExtrasFragments.Encode(Motion(3072), 3072, 1, motionType, envelopeType);
        t.True(presence.Accept(2, basePages[0], basePages[0].Length, 0) == null, "presence assembly starts");
        t.True(motion.Accept(2, basePages[0], basePages[0].Length, 0) == null, "presence envelope cannot advance motion watermark");
        byte[]? complete = null;
        for (int i = motionPages.Length - 1; i >= 0; i--)
        {
            t.True(presence.Accept(2, motionPages[i], motionPages[i].Length, 0) == null, "motion envelope cannot mutate presence");
            byte[]? value = motion.Accept(2, motionPages[i], motionPages[i].Length, 0);
            if (i > 0) t.True(value == null, "partial motion remains private");
            complete = value ?? complete;
        }
        t.Wire(Motion(3072), complete!, complete!.Length, "motion commits despite larger presence sequence");
        complete = null;
        for (int i = 1; i < basePages.Length; i++) complete = presence.Accept(2, basePages[i], basePages[i].Length, 0) ?? complete;
        t.Wire(Snapshot(1801), complete!, complete!.Length, "interleaved motion never interrupted presence");
        foreach (byte[] page in motionPages)
            t.True(motion.Accept(2, page, page.Length, 0) == null, "native replay inert");
        motion.Forget(2);
        complete = null;
        foreach (byte[] page in motionPages) complete = motion.Accept(2, page, page.Length, 0) ?? complete;
        t.True(complete != null, "native rejoin resets its watermark");
        motion.Clear();
        byte[] forged = (byte[])golden[0].Clone(); forged[25] = NetProtocol.MsgExtras;
        t.True(motion.Accept(2, forged, forged.Length, 0) == null, "native envelope cannot deliver presence body");

        t.Case("native animation transport: first waiting state and latest survive bounded replacement");
        var queue = new ExtrasSendQueue(0, motionType, envelopeType, preserveFirst: true);
        var receiver = new ExtrasFragments(motionType, envelopeType);
        queue.Enqueue(Motion(1801), 1801);
        byte[] first = queue.Next(0)!;
        t.True(receiver.Accept(1, first, first.Length, 0) == null, "long native snapshot starts atomically");
        queue.Enqueue(Motion(8), 8);
        for (int size = 9; size <= 1010; size++) queue.Enqueue(Motion(size), size);
        int[] expected = { 1801, 8, 1010 };
        int completed = 0;
        for (int tick = 1; tick < 12; tick++)
        {
            byte[]? page = queue.Next(tick);
            if (page == null) continue;
            byte[]? value = receiver.Accept(1, page, page.Length, tick * 0.01);
            if (value != null)
            {
                t.True(completed < expected.Length, "obsolete intermediate samples were replaced");
                if (completed < expected.Length) t.Equal(expected[completed], value.Length, "current, first waiting, then latest");
                completed++;
            }
        }
        t.Equal(3, completed, "1002 queued changes retain only bounded first/latest waiting states");
        queue.Enqueue(Motion(8), 8); queue.Enqueue(Motion(9), 9); queue.Enqueue(Motion(10), 10);
        first = queue.Next(20) ?? Array.Empty<byte>();
        t.Equal(8, receiver.Accept(1, first, first.Length, 1)?.Length ?? -1, "short animation initial state survives before first send");
        first = queue.Next(21) ?? Array.Empty<byte>();
        t.Equal(10, receiver.Accept(1, first, first.Length, 1)?.Length ?? -1, "short animation latest state follows");
        byte[] owned = Motion(8); queue.Enqueue(owned, owned.Length); owned[6] ^= 255;
        first = queue.Next(22)!;
        complete = receiver.Accept(1, first, first.Length, 1);
        t.Wire(Motion(8), complete!, complete!.Length, "queue owns a copy of reusable sender buffer");

        t.Case("presentation scheduler: native priority, presence fairness, one combined event budget");
        var scheduler = new ExtrasSendScheduler(0, motionType, envelopeType);
        for (int turn = 0; turn < 12; turn++)
        {
            scheduler.Enqueue(Motion(8), 8); scheduler.Enqueue(Snapshot(8), 8);
            double now = turn * 0.1;
            byte[] page = scheduler.Next(now)!;
            t.Equal(turn % 3 == 2 ? (int)NetProtocol.MsgExtrasFragments : envelopeType,
                NetPacket.PeekType(page, page.Length), "two native turns then waiting presence");
            t.True(scheduler.Next(now + 0.049) == null, "streams cannot exceed combined event cadence");
        }
        byte[] announcement = ExtrasVersionAnnouncement.Write(486, "v486");
        first = scheduler.Next(50, announcement)!;
        t.True(ReferenceEquals(first, announcement), "legacy handshake participates in event budget");
        t.True(scheduler.Next(50) == null, "handshake never shares tick with a fragment");
        t.True(scheduler.Next(100) != null && scheduler.Next(100) == null, "slow frame produces no catch-up burst");
        scheduler.Enqueue(Motion(3072), 3072); scheduler.Enqueue(Snapshot(1801), 1801);
        scheduler.Next(101); scheduler.Clear();
        t.True(scheduler.Next(102) == null, "session reset discards both streams and partial send pages");
        scheduler.Enqueue(Snapshot(8), 8);
        first = scheduler.Next(103)!;
        t.Equal((int)NetProtocol.MsgExtrasFragments, NetPacket.PeekType(first, first.Length), "idle animation never delays presence");
    }

    private static byte[] Snapshot(int size)
    {
        var bytes = new byte[size];
        byte[] header = Hex.Bytes("31 52 56 47 03 01");
        Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
        // Keep this paging fixture incompressible so the page-count assertions test pacing.
        var entropy = new byte[size - header.Length];
        new Random(489 + size).NextBytes(entropy);
        Buffer.BlockCopy(entropy, 0, bytes, header.Length, entropy.Length);
        return bytes;
    }
}
