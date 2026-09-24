using System;
using System.Linq;
using GloomhavenVR.Net;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string why)
    {
        ++_assertions;
        if (!value) throw new InvalidOperationException(why);
    }
    private static void Main()
    {
        Chain(); Repeated(); LateJoin(); Churn(); PartialRecipients(); Epoch(); Bidirectional(); FailedDispatch(); Codec();
        Console.WriteLine($"Map story lifecycle: {_assertions} assertions passed.");
    }

    private static void Chain()
    {
        var sender = new MapStoryOpeningLedger();
        var receiver = new MapStoryOpeningLedger();
        var own = new object[30];
        // Sender-only previous dialogs prove token counters need not agree.
        for (int i = 0; i < 9; ++i) sender.Open(new object(), (uint)(900 + i), 900, 1, Array.Empty<int>());
        for (int i = 0; i < own.Length; ++i)
        {
            var remote = new object(); own[i] = new object();
            sender.Open(remote, (uint)(100 + i), 77, 3, new[] { 2 });
            sender.Update(remote, 2, new[] { 2 });
            sender.Finish(remote); // Native queue advances synchronously before ANY packet.
            receiver.Open(own[i], (uint)(100 + i), 77, 3, new[] { 1 });
        }
        for (int packet = 0; packet < 30; ++packet)
        {
            MapStoryOpening[] state = sender.Sample(null);
            Check(state.Length <= MapStoryLifecycleCodec.MaxEntries, "history snapshot bounded without dropping history");
            if (packet % 3 != 0) receiver.Observe(1, state); // Drop a third of whole snapshots.
        }
        foreach (object subject in own)
        {
            Check(receiver.Resolve(subject, 2, -1, true) == -1, "opening animation must paint first page before remote drive");
            Check(receiver.Resolve(subject, 2, 0, false) == -1, "native already closed window cannot replay callbacks");
            Check(receiver.Resolve(subject, 2, 0, true) == 3,
                "every synchronous queued completion survives loss and bounded rotation");
            Check(receiver.Resolve(subject, 2, 2, true) == -1, "terminal callback applies once before native finish returns");
            receiver.Finish(subject);
        }
        Check(receiver.Resolve(new object(), 2, 0, true) == -1, "unopened native content is never skipped");
    }

    private static void Repeated()
    {
        var sender = new MapStoryOpeningLedger(); var receiver = new MapStoryOpeningLedger();
        var a = new object(); var b = new object(); var remoteA = new object(); var remoteB = new object();
        sender.Open(remoteA, 11, 22, 2, new[] { 2 });
        receiver.Open(a, 11, 22, 2, new[] { 1 });
        sender.Update(remoteA, 1, new[] { 2 });
        receiver.Observe(1, sender.Sample(remoteA));
        Check(receiver.Resolve(a, 2, 0, true) == 1, "any participant advances the same native page");
        sender.Finish(remoteA);
        MapStoryOpening[] completed = sender.Sample(null);
        receiver.Observe(1, completed);
        Check(receiver.Resolve(a, 2, 1, true) == 2, "first occurrence completes normally");
        receiver.Finish(a);
        receiver.Open(b, 11, 22, 2, new[] { 1 });
        receiver.Observe(1, completed);
        Check(receiver.Resolve(b, 2, 0, true) == -1, "old same-content completion cannot bind a reopened native occurrence");
        sender.Open(remoteB, 11, 22, 2, new[] { 2 });
        sender.Update(remoteB, 0, new[] { 2 });
        receiver.Observe(1, sender.Sample(remoteB));
        Check(receiver.Resolve(b, 2, 0, true) == -1, "fresh same-content occurrence does not inherit predecessor finish");
        sender.Finish(remoteB);
        receiver.Observe(1, sender.Sample(null));
        Check(receiver.Resolve(b, 2, 0, true) == 2, "new occurrence has independent completion");

        // A receiver that closed A locally before ever receiving a peer packet must
        // bind old A to its retained old object, not mistake it for current B.
        var delayed = new MapStoryOpeningLedger();
        var old = new object(); var current = new object();
        delayed.Open(old, 11, 22, 2, new[] { 1 }); delayed.Finish(old);
        delayed.Open(current, 11, 22, 2, new[] { 1 });
        MapStoryOpening[] full = sender.Sample(null);
        MapStoryOpening later = full.Single(x => x.PreviousToken != 0);
        later.Finished = false;
        delayed.Observe(1, new[] { later });
        Check(delayed.Resolve(current, 2, 0, true) == -1, "reordered successor waits for semantic predecessor provenance");
        delayed.Observe(1, completed);
        Check(delayed.Resolve(current, 2, 0, true) == -1,
            "reordered history must not swap identical completed and live occurrences");
        later.Finished = true;
        delayed.Observe(1, new[] { later });
        Check(delayed.Resolve(current, 2, 0, true) == 2, "delayed history binds identical occurrences FIFO to retained native objects");

        var independentRun = new MapStoryOpeningLedger(); var nextRun = new object();
        independentRun.Open(nextRun, 12, 22, 2, new[] { 1 });
        independentRun.Observe(1, full);
        Check(independentRun.Resolve(nextRun, 2, 0, true) == -1, "prior public run cannot advance a new run with identical text");
    }

    private static void Epoch()
    {
        var oldPeer = new MapStoryOpeningLedger(10);
        var newPeer = new MapStoryOpeningLedger(20);
        var receiver = new MapStoryOpeningLedger(30);
        var old = new object(); var current = new object(); var oldRemote = new object(); var newRemote = new object();
        oldPeer.Open(oldRemote, 11, 22, 2, new[] { 2 }); oldPeer.Finish(oldRemote);
        MapStoryOpening[] stale = oldPeer.Sample(null);
        receiver.Open(old, 11, 22, 2, new[] { 1 }); receiver.Finish(old);
        receiver.Observe(1, stale);
        receiver.Open(current, 11, 22, 2, new[] { 1 });
        Check(receiver.Resolve(current, 2, 0, true) == -1, "old epoch belongs to the earlier local opening");
        newPeer.Open(newRemote, 11, 22, 2, new[] { 2 }); newPeer.Update(newRemote, 1, new[] { 2 });
        receiver.Observe(1, newPeer.Sample(newRemote));
        Check(receiver.Resolve(current, 2, 0, true) == 1, "restarted player may reuse local token under a new epoch");
        receiver.Observe(1, stale);
        Check(receiver.Resolve(current, 2, 1, true) == -1, "retired epoch replay cannot complete a new opening");
        newPeer.Finish(newRemote); receiver.Observe(1, newPeer.Sample(null));
        Check(receiver.Resolve(current, 2, 1, true) == 2, "new epoch completion remains live");
    }

    private static void Churn()
    {
        var sender = new MapStoryOpeningLedger(); var subject = new object();
        sender.Open(subject, 11, 22, 2, new[] { 2, 3, 4 });
        sender.Update(subject, 1, new[] { 3, 4, 5 });
        sender.Update(subject, 1, new[] { 4, 5, 6 });
        sender.Finish(subject);
        for (int player = 2; player <= 6; ++player)
        {
            var receiver = new MapStoryOpeningLedger(); var local = new object();
            receiver.Open(local, 11, 22, 2, new[] { 1 });
            for (int packet = 0; packet < 6; ++packet)
            {
                MapStoryOpening[] snapshot = sender.Sample(null);
                var bytes = new byte[MapStoryLifecycleCodec.MaxPayload + 2]; int offset = 0;
                Check(MapStoryLifecycleCodec.Write(bytes, ref offset, snapshot),
                    "recipient churn never exceeds bounded wire grammar");
                receiver.Observe(1, snapshot);
            }
            Check(receiver.Resolve(local, player, 0, true) == 2,
                "recipient slices preserve all participants of a completed opening");
        }
    }

    private static void PartialRecipients()
    {
        var receiver = new MapStoryOpeningLedger(); var old = new object(); var current = new object();
        receiver.Open(old, 11, 22, 2, new[] { 1 }); receiver.Finish(old);
        receiver.Open(current, 11, 22, 2, new[] { 1 });
        var partial = new MapStoryOpening { Epoch = 1, Token = 1, SemanticKey = 11, ContentKey = 22,
            PageCount = 2, Finished = true, TotalParticipants = 4, Participants = new[] { 3, 4, 5 } };
        var successor = new MapStoryOpening { Epoch = 1, Token = 2, PreviousToken = 1, SemanticKey = 11,
            ContentKey = 22, PageCount = 2, TotalParticipants = 1, Participants = new[] { 2 } };
        receiver.Observe(1, new[] { partial, successor });
        Check(receiver.Resolve(current, 2, 0, true) == -1,
            "partial recipient slice cannot prove predecessor excluded this player");
        partial.Participants = new[] { 2 };
        receiver.Observe(1, new[] { partial });
        Check(receiver.Resolve(current, 2, 0, true) == -1,
            "partial recipient history cannot close the live successor");
        successor.Finished = true;
        receiver.Observe(1, new[] { successor });
        Check(receiver.Resolve(current, 2, 0, true) == 2,
            "complete recipient union binds old and new openings to the right objects");

        var late = new MapStoryOpeningLedger(); var fresh = new object();
        late.Open(fresh, 11, 22, 2, new[] { 1 });
        partial.Finished = false; partial.TotalParticipants = 3; partial.Participants = new[] { 3, 4, 5 };
        late.Observe(1, new[] { partial, successor });
        Check(late.Resolve(fresh, 2, 0, true) == -1,
            "live predecessor cannot prove final absence before future recipient enrollment");
        partial.Finished = true;
        late.Observe(1, new[] { partial });
        Check(late.Resolve(fresh, 2, 0, true) == 2,
            "completed full recipient list permits a genuine late joiner to bypass old occurrence");
    }

    private static void Bidirectional()
    {
        var a = new MapStoryOpeningLedger(10); var b = new MapStoryOpeningLedger(20);
        var subjectA = new object(); var subjectB = new object();
        a.Open(subjectA, 11, 22, 4, new[] { 2 }, bidirectional: true);
        b.Open(subjectB, 11, 22, 4, new[] { 1 }, bidirectional: true);
        a.Update(subjectA, 2, new[] { 2 }); b.Update(subjectB, 2, new[] { 1 });
        a.Update(subjectA, 1, new[] { 2 });
        MapStoryOpening[] previous = a.Sample(subjectA);
        b.Observe(1, previous);
        Check(b.Resolve(subjectB, 2, 2, true) == 1, "native previous-page action synchronizes backwards");
        b.Update(subjectB, 1, new[] { 1 });
        Check(b.Sample(subjectB)[0].PageRevision == previous[0].PageRevision,
            "remote page adoption does not manufacture a relay revision");
        b.Update(subjectB, 0, new[] { 1 });
        a.Observe(2, b.Sample(subjectB));
        Check(a.Resolve(subjectA, 1, 1, true) == 0, "either participant can turn an introduction backwards");
        b.Observe(1, previous);
        Check(b.Resolve(subjectB, 2, 0, true) == -1, "older backwards-page packet cannot undo a later local action");
        a.Update(subjectA, 1, new[] { 2 }); b.Update(subjectB, 2, new[] { 1 });
        MapStoryOpening[] aa = a.Sample(subjectA), bb = b.Sample(subjectB);
        a.Observe(2, bb); b.Observe(1, aa);
        Check(a.Resolve(subjectA, 1, 1, true) == 2 && b.Resolve(subjectB, 2, 2, true) == -1,
            "simultaneous opposite page actions have a deterministic tie resolution");
    }

    private static void FailedDispatch()
    {
        var sender = new MapStoryOpeningLedger(); var receiver = new MapStoryOpeningLedger();
        var remote = new object(); var local = new object();
        sender.Open(remote, 11, 22, 2, new[] { 2 }); sender.Finish(remote);
        receiver.Open(local, 11, 22, 2, new[] { 1 }); receiver.Observe(1, sender.Sample(null));
        Check(receiver.Resolve(local, 2, 0, true) == 2, "terminal dispatch reserves before native callback");
        Check(receiver.Resolve(local, 2, 0, true) == -1, "reentrant native callback cannot dispatch terminal twice");
        receiver.RetryTerminal(local);
        Check(receiver.Resolve(local, 2, 0, true) == 2, "failed unchanged native opening can retry terminal reservation");
        receiver.Finish(local); receiver.RetryTerminal(local);
        Check(receiver.Resolve(local, 2, 0, true) == -1, "successful native finish cannot be reopened by failure rollback");
    }

    private static void LateJoin()
    {
        var sender = new MapStoryOpeningLedger(); var receiver = new MapStoryOpeningLedger();
        var old = new object(); var live = new object(); var late = new object();
        sender.Open(old, 11, 22, 2, new[] { 2 }); sender.Finish(old);
        receiver.Open(late, 11, 22, 2, new[] { 1 });
        receiver.Observe(1, sender.Sample(null));
        Check(receiver.Resolve(late, 3, 0, true) == -1, "late joiner cannot claim completed history from before participation");
        sender.Open(live, 11, 22, 2, new[] { 2 });
        sender.Update(live, 1, new[] { 2, 3 });
        receiver.Observe(1, sender.Sample(live));
        Check(receiver.Resolve(late, 3, 0, true) == 1, "late joiner adopts live opening after absent predecessor is proven");
        sender.Finish(live); sender.Update(live, 1, new[] { 2, 3, 4 });
        MapStoryOpening finished = sender.Sample(null).Single(x => x.PreviousToken != 0);
        Check(!finished.Participants.Contains(4), "finished opening never acquires new late recipients");
    }

    private static void Codec()
    {
        var entry = new MapStoryOpening { Epoch = 0xaabbccdd, Token = 0x01020304, PreviousToken = 1, SemanticKey = 0x11223344,
            ContentKey = 0x55667788, Page = 1, PageCount = 2, Finished = true, TotalParticipants = 1, Participants = new[] { 7 } };
        byte[] golden = { 83, 35, 1, 0xdd, 0xcc, 0xbb, 0xaa, 4, 3, 2, 1, 1, 0, 0, 0, 0x44, 0x33, 0x22, 0x11,
            0x88, 0x77, 0x66, 0x55, 0, 0, 0, 0, 1, 2, 1, 1, 1, 0, 7, 0, 0, 0 };
        var buffer = new byte[MapStoryLifecycleCodec.MaxPayload + 2]; int offset = 0;
        Check(MapStoryLifecycleCodec.Write(buffer, ref offset, new[] { entry }), "golden writer accepts native opening");
        Check(offset == golden.Length && buffer.Take(offset).SequenceEqual(golden), "lifecycle golden bytes are append-only grammar proof");
        Check(MapStoryLifecycleCodec.TryRead(golden, 2, 35, out MapStoryOpening[] decoded)
            && decoded[0].Epoch == 0xaabbccdd && decoded[0].PreviousToken == 1 && decoded[0].Finished && decoded[0].Participants[0] == 7,
            "golden parser reads opening and participation provenance");
        for (int length = 0; length < 35; ++length)
            Check(!MapStoryLifecycleCodec.TryRead(golden, 2, length, out _), "truncated payload rejected");
        byte[] malformed = (byte[])golden.Clone(); malformed[29] = 4;
        Check(!MapStoryLifecycleCodec.TryRead(malformed, 2, 35, out _), "unknown completion flags rejected");
        malformed = (byte[])golden.Clone(); malformed[27] = 2;
        Check(!MapStoryLifecycleCodec.TryRead(malformed, 2, 35, out _), "out of range page rejected");
        offset = 0;
        Check(!MapStoryLifecycleCodec.Write(buffer, ref offset, new[] { entry, entry }) && offset == 0,
            "duplicate opening token rejected symmetrically before writing");
        var foreignEpoch = new MapStoryOpening { Epoch = 99, Token = 2, SemanticKey = 11, ContentKey = 22, PageCount = 2 };
        offset = 0;
        Check(!MapStoryLifecycleCodec.Write(buffer, ref offset, new[] { entry, foreignEpoch }) && offset == 0,
            "mixed epochs cannot share a snapshot");
        entry.Participants = new[] { 7, 7 }; offset = 0;
        Check(!MapStoryLifecycleCodec.Write(buffer, ref offset, new[] { entry }) && offset == 0,
            "duplicate participants rejected before writing");
    }
}
