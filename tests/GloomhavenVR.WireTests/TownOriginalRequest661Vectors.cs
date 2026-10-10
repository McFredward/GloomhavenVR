using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownOriginalRequest661Vectors
{
    internal static void Run(Harness t)
    {
        var entries = new[] { new TownServiceOriginalReceiptEntry(0x1234, 0x0102030405060708UL) };
        byte[] packet = TownServiceOriginalRequestCodec.Write(0x01020304, 3, 0x0a0b0c0d, 0, entries);
        // Independently specified bytes: no generated round-trip expected value.
        t.Wire(Hex.Bytes("31 52 56 47 03 1C 72 16 01 04 03 02 01 03 0D 0C 0B 0A 00 01 34 12 08 07 06 05 04 03 02 01"),
            packet, packet.Length, "114 exact original request immutable golden");
        t.True(TownServiceOriginalRequestCodec.TryRead(packet, packet.Length, out var decoded)
            && decoded!.OriginPeer == 0x01020304 && decoded.Service == 3 && decoded.Lane == 0
            && decoded.Session == 0x0a0b0c0d && decoded.Entries.Length == 1
            && decoded.Entries[0].Module == 0x1234 && decoded.Entries[0].Sequence == 0x0102030405060708UL,
            "114 retains exact rejected baseline identity");
        t.True(!TownServiceOriginalReceiptCodec.TryRead(packet, packet.Length, out _),
            "114 rejection never acknowledges the rejected original through112");
        byte[] receipt = TownServiceOriginalReceiptCodec.Write(2, 3, 1, 0, entries);
        t.True(!TownServiceOriginalRequestCodec.TryRead(receipt, receipt.Length, out _),
            "112 acknowledgement is never a114 repair request");
        for (int cut = 0; cut < packet.Length; cut++)
            t.True(!TownServiceOriginalRequestCodec.TryRead(packet, cut, out _), "114 rejects truncated request " + cut);
        foreach (int at in new[] { 0, 4, 5, 6, 7, 8, 13, 18, 19 })
        {
            byte[] malformed = (byte[])packet.Clone(); malformed[at] = 255;
            t.True(!TownServiceOriginalRequestCodec.TryRead(malformed, malformed.Length, out _), "114 rejects invalid header at " + at);
        }
        foreach (int service in new[] { 0, 2, 4, 255 })
        {
            byte[] malformed = (byte[])packet.Clone(); malformed[13] = (byte)service;
            t.True(!TownServiceOriginalRequestCodec.TryRead(malformed, malformed.Length, out _), "114 restricts native service " + service);
        }
        var extended = new byte[packet.Length + 4]; Array.Copy(packet, extended, packet.Length);
        extended[packet.Length] = 238; extended[packet.Length + 1] = 2;
        extended[packet.Length + 2] = 17; extended[packet.Length + 3] = 42;
        t.True(TownServiceOriginalRequestCodec.TryRead(extended, extended.Length, out _), "114 skips additive unknown records");
        var duplicate = new byte[packet.Length * 2 - 6]; Array.Copy(packet, duplicate, packet.Length);
        Array.Copy(packet, 6, duplicate, packet.Length, packet.Length - 6);
        t.True(!TownServiceOriginalRequestCodec.TryRead(duplicate, duplicate.Length, out _), "114 rejects duplicate known record");
        var ambiguous = new byte[packet.Length + receipt.Length - 6]; Array.Copy(packet, ambiguous, packet.Length);
        Array.Copy(receipt, 6, ambiguous, packet.Length, receipt.Length - 6);
        t.True(!TownServiceOriginalRequestCodec.TryRead(ambiguous, ambiguous.Length, out _), "114 rejects mixed receipt/request authority");
        var maximum = new TownServiceOriginalReceiptEntry[20];
        for (ushort i = 0; i < maximum.Length; i++) maximum[i] = new TownServiceOriginalReceiptEntry(i, 100UL + i);
        byte[] max = TownServiceOriginalRequestCodec.Write(2, 1, 1, 0, maximum);
        t.True(max.Length == 220 && TownServiceOriginalRequestCodec.TryRead(max, max.Length, out var full)
            && full!.Entries.Length == 20 && full.Service == 1, "114 exact bounded merchant batch");
        foreach (var bad in new[]
        {
            new[] { new TownServiceOriginalReceiptEntry(1, 0) },
            new[] { new TownServiceOriginalReceiptEntry(TownServiceFrame.ManifestModule, 1) },
            new[] { new TownServiceOriginalReceiptEntry(TownServiceFrame.UrgentBundleStream, 1) },
            new[] { new TownServiceOriginalReceiptEntry(1, 1), new TownServiceOriginalReceiptEntry(1, 2) },
            Array.Empty<TownServiceOriginalReceiptEntry>(),
            new TownServiceOriginalReceiptEntry[21]
        })
        {
            bool rejected = false;
            try { TownServiceOriginalRequestCodec.Write(2, 3, 1, 0, bad); }
            catch (ArgumentException) { rejected = true; }
            t.True(rejected, "114 writer rejects invalid identity batch");
        }
        // ReliableOrdered selection already treats message28 as original metadata.
        // The sibling record uses the same clock, bounded queue and native event.
        t.True(PresentationBatch.HasTownOriginalPage(packet, packet.Length), "114 repair request uses existing reliable original transport");
        byte[] mixed = PresentationBatch.Write(new List<byte[]> { Hex.Bytes("31 52 56 47 03 01"), packet });
        t.True(PresentationBatch.TryRead(mixed, mixed.Length, out var pages) && pages!.Length == 2
            && PresentationBatch.HasTownOriginalPage(mixed, mixed.Length), "114 shares the existing native event cap");
        var scheduler = new ExtrasSendScheduler(0, 3, 4);
        scheduler.Enqueue(packet, packet.Length);
        byte[]? sent = scheduler.NextBatch(0);
        t.True(sent != null && TownServiceOriginalRequestCodec.TryRead(sent, sent.Length, out _), "114 drains through actual bounded scheduler");
        scheduler.Enqueue(packet, packet.Length);
        t.True(scheduler.NextBatch(.001) == null, "114 cannot bypass fifty-millisecond send clock");
        scheduler.Clear();
        t.True(scheduler.NextBatch(1) == null, "114 reset discards old requests");
        KnownOriginalQueue(t, 1);
        KnownOriginalQueue(t, 3);
        KnownOriginalInFlight(t);
        OpeningReservationEnds(t);
    }

    private static void KnownOriginalQueue(Harness t, byte service)
    {
        t.Case("An accepted rejected original borrows the existing urgent turn without ordinary priority service=" + service);
        var original = new TownServiceFrame { Service = service, Session = 661, Sequence = 1,
            Module = 42, Template = 1, TemplateAddress = service == 1 ? "card.7|row" : "face.333|row", Structure = 7,
            Visible = true, Nodes = new[] { new TownServiceNode { Binding = 1 } },
            Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } };
        byte[] bytes = TownServiceCodec.Write(original);
        var ordinary = new TownServiceLaneSendQueue(0);
        ordinary.Enqueue(bytes, bytes.Length, original);
        t.True(ordinary.NextUrgent(0) == null, "ordinary non-priority original earns no rejected-original turn");
        ordinary.Clear();
        Func<TownServiceFrame, bool>? previous = TownRequestedOriginalRepair.Source;
        try
        {
            TownRequestedOriginalRepair.Source = frame => ReferenceEquals(frame, original);
            var queue = new TownServiceLaneSendQueue(0);
            queue.Enqueue(bytes, bytes.Length, original);
            byte[]? page = queue.NextUrgent(0);
            t.True(page != null, "known refused ordinary original is eligible without another high-priority widget");
            if (page != null)
            {
                t.True(page.Length <= PresentationBatch.MaxSize, "known repair retains the existing event bound");
                var receiver = new TownServiceFragments();
                byte[]? complete = receiver.Accept(2, page, page.Length, 0);
                t.True(complete != null, "small known original completes its actual module stream");
                if (complete != null) t.Wire(bytes, complete, complete.Length, "known repair is the exact immutable source bytes");
            }
            t.True(queue.NextUrgent(.051) == null && queue.Next(1) == null,
                "completed requested original leaves no borrowed work or repeated bytes");
            queue.Enqueue(bytes, bytes.Length, original);
            queue.Clear();
            t.True(queue.NextUrgent(2) == null && queue.Next(2) == null,
                "reset retires a queued accepted request");
            queue.Enqueue(bytes, bytes.Length, original);
            var withdrawn = new TownServiceFrame { Service = service, Session = 661, Sequence = 2,
                Module = TownServiceFrame.ManifestModule, Visible = true,
                Modules = Array.Empty<ushort>(), Pose = original.Pose };
            byte[] census = TownServiceCodec.Write(withdrawn);
            queue.Enqueue(census, census.Length, withdrawn);
            t.True(queue.NextUrgent(3) == null, "withdrawn source retires the requested original before paint");
            queue.Clear();
        }
        finally { TownRequestedOriginalRepair.Source = previous; }
    }

    private static void KnownOriginalInFlight(Harness t)
    {
        t.Case("An old compact source identity finishes before the pinned full repair and its dependent delta");
        var random = new Random(661);
        var characters = new char[6000];
        for (int i = 0; i < characters.Length; i++) characters[i] = (char)('a' + random.Next(26));
        var node = new TownServiceNode { Binding = 1 };
        node.Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Numbers = new float[40],
            Text = new[] { new string(characters), "originalFont", "originalSprites" } });
        var source = new TownServiceFrame { Service = 3, Session = 661, Sequence = 1,
            Module = 42, Template = 1, TemplateAddress = "face.333|row", Structure = 7,
            Visible = true, Nodes = new[] { node },
            Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } };
        var compact = TownServiceDelta.Retain(source); compact.NativeTemplateBasisKey = 123;
        byte[] compactBytes = TownServiceCodec.Write(compact), fullBytes = TownServiceCodec.Write(source);
        var current = TownServiceDelta.Retain(source); current.Sequence = 2; current.Pose[0] = .4f;
        var delta = TownServiceDelta.Create(source, current); byte[] deltaBytes = TownServiceCodec.Write(delta);
        Func<TownServiceFrame, bool>? previous = TownRequestedOriginalRepair.Source;
        bool accepted = false;
        try
        {
            TownRequestedOriginalRepair.Source = frame => accepted && ReferenceEquals(frame, source);
            var queue = new TownServiceLaneSendQueue(0);
            queue.Enqueue(compactBytes, compactBytes.Length, source);
            // Port only the pre-existing direct-module in-flight state. Actual
            // per-module fragmentation, lane arbitration and identities run unchanged.
            var modules = (Dictionary<ushort, ExtrasSendQueue>)typeof(TownServiceLaneSendQueue)
                .GetField("_queues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(queue)!;
            var requested = (Dictionary<ushort, TownServiceFrame>)typeof(TownServiceLaneSendQueue)
                .GetField("_requestedOriginals", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(queue)!;
            byte[] first = modules[42].Next(0)!;
            var receiver = new TownServiceFragments();
            t.True(receiver.Accept(2, first, first.Length, 0) == null && modules[42].HasInFlight,
                "actual compact module has unfinished pages before the request arrives");
            accepted = true;
            queue.Enqueue(fullBytes, fullBytes.Length, source);
            queue.Enqueue(deltaBytes, deltaBytes.Length, delta);
            bool compactArrived = false, fullArrived = false, deltaArrived = false;
            for (int i = 1; i < 100; i++)
            {
                double now = i * .051;
                byte[]? page = queue.NextUrgent(now) ?? queue.Next(now);
                if (page == null) continue;
                t.True(page.Length <= PresentationBatch.MaxSize, "in-flight repair keeps the existing event cap");
                byte[]? result = receiver.Accept(2, page, page.Length, now); if (result == null) continue;
                byte[][] children = TownServiceCodec.TryReadBundle(result, result.Length, out var bundle) ? bundle! : new[] { result };
                foreach (byte[] child in children)
                {
                    t.True(TownServiceCodec.TryRead(child, child.Length, out var frame), "every retained source page decodes");
                    if (frame!.NativeTemplateBasisKey != 0)
                    {
                        t.Wire(compactBytes, child, child.Length, "started compact remains byte-exact");
                        t.True(requested.ContainsKey(source.Module), "old compact completion preserves urgency for the still-pinned full source");
                        compactArrived = true;
                    }
                    else if (frame.BaseSequence == 0)
                    { t.True(compactArrived, "old compact completes before its requested full source"); t.Wire(fullBytes, child, child.Length, "requested full source is retained exactly"); fullArrived = true; }
                    else
                    { t.True(fullArrived, "new dependent revision follows its usable full baseline"); t.Wire(deltaBytes, child, child.Length, "dependent delta survives requested repair"); deltaArrived = true; }
                }
            }
            t.True(compactArrived && fullArrived && deltaArrived, "all source dependencies make finite progress");
            t.True(requested.Count == 0, "completed ordinary repair retires its exact urgency marker");
            t.True(queue.NextUrgent(10) == null && queue.Next(10) == null,
                "ordinary repair and later delta leave no stale urgency or duplicate source");
            queue.Clear();
        }
        finally { TownRequestedOriginalRepair.Source = previous; }
    }

    private static void OpeningReservationEnds(Harness t)
    {
        t.Case("Finite opening debt retires with no ordinary non-town traffic");
        var source = new TownServiceFrame { Service = 3, Session = 661, Sequence = 1,
            Module = 42, Template = 1, TemplateAddress = "face.333|row", Structure = 7,
            Visible = true, HighPriority = true,
            Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } };
        var census = new TownServiceFrame { Service = 3, Session = 661, Sequence = 2,
            Module = TownServiceFrame.ManifestModule, Visible = true, TransactionActive = true,
            Modules = new ushort[] { 42 }, RequiredVisibleModules = new ushort[] { 42 }, Pose = source.Pose };
        byte[] manifest = TownServiceCodec.Write(census);
        bool eightPages = false; byte[] original = Array.Empty<byte>();
        for (int length = 8000; length <= 14000 && !eightPages; length += 100)
        {
            var random = new Random(661); var characters = new char[length];
            for (int i = 0; i < length; i++) characters[i] = (char)('a' + random.Next(26));
            var node = new TownServiceNode { Binding = 1 };
            node.Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Numbers = new float[40],
                Text = new[] { new string(characters), "originalFont", "originalSprites" } });
            source.Nodes = new[] { node }; original = TownServiceCodec.Write(source);
            var pool = new TownServiceCodec.OriginalValuePoolBuilder();
            t.True(pool.TryAdd(manifest) && pool.TryAdd(original), "exact opening originals fit their existing atomic pool");
            byte[] bytes = pool.Write();
            eightPages = ExtrasFragments.Encode(bytes, bytes.Length, TownServiceFrame.UrgentBundleStream,
                TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: true).Length == 8;
        }
        t.True(eightPages, "actual opening pool fills two four-page reservations");
        if (!eightPages) return;
        var sender = new ExtrasSendScheduler(0, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        sender.Enqueue(original, original.Length, identity: source); sender.Enqueue(manifest, manifest.Length, identity: census);
        var receiver = new TownServiceFragments(); bool full = false, advanced = false;
        var current = TownServiceDelta.Retain(source); current.Sequence = 3; current.Pose[0] = .4f;
        var delta = TownServiceDelta.Create(source, current); delta.HighPriority = true;
        byte[] revision = TownServiceCodec.Write(delta);
        for (int tick = 0; tick < 100; tick++)
        {
            double now = tick * .051; byte[]? packet = sender.NextBatch(now); if (packet == null) continue;
            t.True(packet.Length <= PresentationBatch.MaxSize && sender.NextBatch(now) == null,
                "reservation completion preserves the shared event bound and send clock");
            byte[][] pages = PresentationBatch.TryRead(packet, packet.Length, out var batch) ? batch! : new[] { packet };
            foreach (byte[] page in pages)
            {
                byte[]? result = receiver.Accept(2, page, page.Length, now); if (result == null) continue;
                byte[][] children = TownServiceCodec.TryReadBundle(result, result.Length, out var bundle) ? bundle! : new[] { result };
                foreach (byte[] child in children)
                {
                    if (!TownServiceCodec.TryRead(child, child.Length, out var frame) || frame!.Module != 42) continue;
                    if (frame.BaseSequence == 0 && !full)
                    { t.Wire(original, child, child.Length, "opening delivers the complete exact original"); full = true; sender.Enqueue(revision, revision.Length, identity: delta); }
                    else if (frame.BaseSequence == 1)
                    { t.Wire(revision, child, child.Length, "ordinary revision survives the completed opening reservation"); advanced = true; }
                }
            }
        }
        t.True(full && advanced, "town revisions progress after opening even when all other streams are empty");
        sender.Clear();
    }
}
