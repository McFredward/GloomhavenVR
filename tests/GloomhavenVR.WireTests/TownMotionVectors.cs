using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownMotionVectors
{
    internal static TownServiceFrame? CapturedFront;
    internal static void Run(Harness t)
    {
        Codec(t); Packed(t); MotionBudget(t); VisibleFanBudget(t); Saturation(t);
    }
    private static void Packed(Harness t)
    {
        t.Case("Lossless record98 retains the original numeric fields inside one 864-byte event");
        var packet = Packet();
        for (ushort i = 1; i <= 70; i++) packet.Entries.Add(new TownServiceMotionEntry
            { Kind = 2, Service = 3, Session = 7, Structure = 4, Module = i,
                Binding = 5, Property = TownServiceProperty.Transform,
                Numbers = new[] { .25f, .5f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } });
        byte[]? packed = TownServiceMotionCodec.TryWritePacked(packet);
        t.True(packed != null && packed.Length <= 864 && packed[21] == 98,
            "seventy exact native properties share one bounded additive packed event");
        t.True(TownServiceMotionCodec.TryRead(packed!, packed!.Length, out var read)
            && read!.Entries.Count == 70 && read.Sequence == 1 && read.SampleTime == 2f,
            "packed header and complete property census retain owner affinity");
        for (int i = 0; i < 70; i++)
        {
            var entry = read!.Entries[i];
            t.True(entry.Module == i + 1 && entry.Structure == 4 && entry.Binding == 5
                && entry.Session == 7 && entry.Service == 3, "packing cannot change a native binding");
            for (int n = 0; n < 10; n++) t.Equal(packet.Entries[i].Numbers[n], entry.Numbers[n],
                "lossless original native number survives the packed event");
        }
        for (int length = 0; length < packed!.Length; length++)
            t.True(!TownServiceMotionCodec.TryRead(packed, length, out _), "truncated packed event cannot partially render");
        foreach (int at in new[] { 8, 17, 23, 24, 26, 28, packed.Length - 1 })
        {
            byte[] corrupt = (byte[])packed.Clone(); corrupt[at] ^= 1;
            t.True(!TownServiceMotionCodec.TryRead(corrupt, corrupt.Length, out _),
                "clock, extent, offset and checksum corruption fails atomically");
        }
        var live = new List<TownServiceMotionPending>(); var fan = new List<TownServiceMotionPending>();
        var cold = new List<TownServiceMotionPending>();
        for (ushort id = 1; id <= 160; id++)
        {
            var slot = new TownServiceMotionPending { Entry = new TownServiceMotionEntry
                { Kind = 2, Service = 1, Session = 7, Structure = 2, Module = id,
                    Binding = 2, Property = TownServiceProperty.Transform,
                    Numbers = new[] { .2f, .5f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } } };
            (id <= 80 ? live : id <= 120 ? fan : cold).Add(slot);
        }
        int a = 0, b = 0, c = 0; var latest = new Dictionary<ushort, float>(); float gap = 0f;
        for (int tick = 0; tick < 75; tick++)
        {
            float now = tick / 15f;
            foreach (var group in new[] { live, fan, cold }) foreach (var slot in group) slot.Dirty = true;
            var sample = Packet(); sample.SampleTime = now;
            byte[] encoded = TownServiceMotionBudget.FillPacked(sample, live, fan, cold, ref a, ref b, ref c, now);
            t.True(encoded.Length <= 864 && TownServiceMotionCodec.TryRead(encoded, encoded.Length, out _),
                "every saturated packed turn remains one original cadence event");
            foreach (var group in new[] { live, fan, cold }) foreach (var slot in group)
            {
                bool sent = sample.Entries.Contains(slot.Entry);
                t.True(sent ? !slot.Dirty && slot.SentAt == now : slot.Dirty && slot.SentAt != now,
                    "compression backpressure never marks an unsent slot delivered");
                if (!sent) continue;
                if (latest.TryGetValue(slot.Entry.Module, out float prior)) gap = Math.Max(gap, now - prior);
                latest[slot.Entry.Module] = now;
            }
        }
        t.Equal(160, latest.Count, "packed fair turns reach every continuously dirty native binding");
        t.True(gap <= .4f, "all current bindings remain within four tenths under dense representative native contention");
        Console.WriteLine($"NPC packed measured budget: bindings=160 continuouslyDirty=true maxGap={gap:F3}s maxEvent=864B");
    }
    private static float[] Pose(float x = 0f) => new[] { x, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f };
    private static TownServiceMotionPacket Packet(params TownServiceMotionEntry[] entries)
    { var packet = new TownServiceMotionPacket { Sequence = 1, SampleTime = 2f }; packet.Entries.AddRange(entries); return packet; }
    private static void Codec(Harness t)
    {
        t.Case("Independent fast NPC record97 has exact bounded bytes and strict affinity");
        var commit = new TownServiceMotionEntry { Kind = 3, Service = 2, Session = 7, Revision = 5, CommitAge = .5f };
        byte[] bytes = TownServiceMotionCodec.Write(Packet(commit));
        t.Wire(Hex.Bytes("31 52 56 47 03 1A 61 0D 00 01 00 00 00 00 00 00 00 00 00 00 40 61 0F 03 00 02 07 00 00 00 05 00 00 00 00 00 00 3F"), bytes, bytes.Length,
            "donation event preserves independent original little-endian timing vector");
        t.True(TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var read) && read!.Entries[0].CommitAge == .5f,
            "explicit committed native donation survives the independent relay");
        var cue = new TownServiceMotionEntry { Kind = 5, Service = 3, Session = 7, CueReady = true, CueStrength = .5f,
            HasSharedCue = true, SharedCueReady = true, SharedCueStrength = .25f, SharedGuideOwner = 12 };
        bytes = TownServiceMotionCodec.Write(Packet(cue));
        t.Wire(Hex.Bytes("31 52 56 47 03 1A 61 0D 00 01 00 00 00 00 00 00 00 00 00 00 40 61 16 05 00 03 07 00 00 00 01 00 00 00 3F 01 01 00 00 80 3E 0C 00 00 00"), bytes, bytes.Length,
            "canonical ready mage guide owner remains explicit alongside its owner strength");
        var root = new TownServiceMotionEntry { Kind = 1, Service = 2, Session = 7, Structure = 4, Module = 0,
            ParentModule = TownServiceFrame.ManifestModule, Pose = Pose(.25f), Visible = true };
        var mixed = Packet(commit, root);
        t.True(TownServiceMotionCodec.TryRead(TownServiceMotionCodec.Write(mixed), TownServiceMotionCodec.Write(mixed).Length, out _),
            "donation key cannot alias legal original root module zero");
        var malformed = new List<byte[]>();
        for (int length = 0; length < bytes.Length; length++)
        { var cut = new byte[length]; Array.Copy(bytes, cut, length); malformed.Add(cut); }
        var nonfinite = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, nonfinite, 17, 4); malformed.Add(nonfinite);
        var wrongOwner = (byte[])bytes.Clone(); Array.Clear(wrongOwner, wrongOwner.Length - 4, 4); malformed.Add(wrongOwner);
        var invalidBool = (byte[])bytes.Clone(); invalidBool[30] = 2; malformed.Add(invalidBool);
        foreach (byte[] bad in malformed) t.True(!TownServiceMotionCodec.TryRead(bad, bad.Length, out _), "malformed fast event rejected atomically");
        foreach (byte kind in new byte[] { 1, 2, 3, 4, 5 })
        {
            TownServiceMotionEntry entry = kind == 1 ? root : kind == 3 ? commit : kind == 5 ? cue
                : new TownServiceMotionEntry { Kind = kind, Service = 1, Session = 7, Structure = 4, Module = 3, Binding = 2,
                    Property = kind == 2 ? TownServiceProperty.Graphic : TownServiceProperty.Material,
                    Offset = 5, Numbers = kind == 2 ? new[] { 1f, .2f, .3f, .4f, .5f } : new[] { .4f } };
            foreach (bool canvas in new[] { false, true })
            { root.HasCanvasUpdate = canvas; root.HasCanvasFrame = canvas; root.CanvasOnHand = canvas; root.Hand = canvas ? (byte)1 : (byte)0;
              root.CanvasPose = Pose(); root.CanvasRect = new[] { 100f, 50f, .5f, .5f }; root.CanvasSettings = new[] { 100f, 0f, 0f, 1f, 0f };
              byte[] encoded = TownServiceMotionCodec.Write(Packet(entry));
              t.Equal(encoded.Length, TownServiceMotionCodec.EntryBytes(entry) + 21, "allocation-free size equals exact encoded grammar");
              t.True(TownServiceMotionCodec.TryRead(encoded, encoded.Length, out _), "every actual numeric entry validates exact shaped values"); }
        }
        foreach (ushort readonlyOffset in new ushort[] { 0, 1, 2, 3, 4, 9, 14, 19 })
        {
            var property = new TownServiceMotionEntry { Kind = 4, Service = 1, Session = 7, Structure = 2,
                Module = 2, Property = TownServiceProperty.Material, Offset = readonlyOffset, Numbers = new[] { .5f } };
            bool rejected = false; try { TownServiceMotionCodec.Write(Packet(property)); } catch (InvalidDataException) { rejected = true; }
            t.True(rejected, "fast material updates never mutate original shader metadata or property types");
        }
        var busy = new TownServiceMotionPacket { Sequence = 9, SampleTime = 3f };
        for (ushort module = 1; module < 10; module++) busy.Entries.Add(new TownServiceMotionEntry
            { Kind = 1, Service = 1, Session = 7, Structure = 2, Module = module, Pose = Pose(), Visible = true });
        byte[] bounded = TownServiceMotionCodec.Write(busy);
        t.True(bounded.Length <= 864, "nine compact held roots fit one rig-cadence bounded event");
        bool overflow = false;
        for (ushort module = 10; module < 20; module++) busy.Entries.Add(new TownServiceMotionEntry
            { Kind = 1, Service = 1, Session = 7, Structure = 2, Module = module, Pose = Pose(), Visible = true });
        try { TownServiceMotionCodec.Write(busy); } catch (InvalidDataException) { overflow = true; }
        t.True(overflow, "oversized combined numeric event cannot enter transport");
    }

    private static void MotionBudget(Harness t)
    {
        t.Case("Four sender numeric budgets keep hot controls live beside one hundred prewarmed fan roots");
        float maxHotGap = 0f, maxFanGap = 0f; int sent = 0, bytes = 0;
        for (int peer = 0; peer < 4; peer++)
        {
            var live = new List<TownServiceMotionPending>(); var fan = new List<TownServiceMotionPending>();
            for (ushort id = 1; id <= 3; id++) live.Add(new TownServiceMotionPending { Entry = new TownServiceMotionEntry
                { Kind = 1, Module = id, Service = 1, Session = 7, Structure = 1, Hand = 1, Pose = Pose(), Visible = true } });
            for (ushort id = 4; id < 104; id++) fan.Add(new TownServiceMotionPending { Entry = new TownServiceMotionEntry
                { Kind = 1, Module = id, Service = 1, Session = 7, Structure = 1, Hand = 3, Pose = Pose(), Visible = true,
                  HasCanvasUpdate = true, HasCanvasFrame = true, CanvasOnHand = true, CanvasPose = Pose(),
                  CanvasRect = new[] { 100f, 50f, .5f, .5f }, CanvasSettings = new[] { 100f, 0f, 0f, 1f, 0f } } });
            int lc = 0, fc = 0; var last = new Dictionary<ushort, float>();
            for (int tick = 0; tick < 150; tick++)
            {
                float now = tick / 15f; var packet = new TownServiceMotionPacket { Sequence = (ulong)tick + 1, SampleTime = now };
                // Worst case: every fan root is dirty continuously, rather than
                // spending just one cheap heartbeat per second after it settles.
                foreach (TownServiceMotionPending entry in live) entry.Dirty = true;
                foreach (TownServiceMotionPending entry in fan) entry.Dirty = true;
                TownServiceMotionBudget.Fill(packet, live, fan, ref lc, ref fc, now);
                byte[] raw = TownServiceMotionCodec.Write(packet); bytes += raw.Length; sent++;
                t.True(raw.Length <= 864, "live and one hundred fan affinities remain one bounded event per rig tick");
                foreach (TownServiceMotionEntry entry in packet.Entries)
                {
                    float gap = now - (last.TryGetValue(entry.Module, out float previous) ? previous : 0f);
                    if (entry.Module <= 3) maxHotGap = Math.Max(maxHotGap, gap); else maxFanGap = Math.Max(maxFanGap, gap);
                    last[entry.Module] = now;
                }
            }
            t.Equal(103, last.Count, "numeric capacity cannot starve any continually dirty fan affinity");
        }
        t.True(maxHotGap <= 1f / 15f + .00001f, "live held/press/scroll targets retain rig cadence under dense cold-fan contention");
        t.True(maxFanGap < 3f, "each dense fan affinity renews inside its existing stale lifetime");
        Console.WriteLine($"NPC numeric measured budget: senders=4 hotTargets=3 fanRoots=100 continuouslyDirty=true maxHotGap={maxHotGap:F3}s maxFanGap={maxFanGap:F3}s events={sent} bytes={bytes} maxEvent=864B");
    }

    private static void VisibleFanBudget(Harness t)
    {
        t.Case("Four sender live native targets retain turns through visible fan motion and closed prewarm heartbeats");
        float maxHot = 0f, maxVisible = 0f, maxCold = 0f;
        for (int peer = 0; peer < 4; peer++)
        {
            var hot = new List<TownServiceMotionPending>(); var visible = new List<TownServiceMotionPending>();
            var cold = new List<TownServiceMotionPending>(); var last = new Dictionary<ushort, float>();
            for (ushort id = 1; id <= 3; id++) hot.Add(new TownServiceMotionPending { Entry = new TownServiceMotionEntry
                { Kind = 1, Module = id, Service = 1, Session = 7, Structure = 1, Hand = 0, Pose = Pose(), Visible = true } });
            for (ushort id = 4; id < 104; id++) visible.Add(new TownServiceMotionPending { Entry = new TownServiceMotionEntry
                { Kind = 1, Module = id, Service = 1, Session = 7, Structure = 1, Hand = 3, Pose = Pose(), Visible = true } });
            for (ushort id = 104; id < 204; id++) cold.Add(new TownServiceMotionPending { Entry = new TownServiceMotionEntry
                { Kind = 1, Module = id, Service = 1, Session = 7, Structure = 1, Hand = 3, Pose = Pose(), Visible = true,
                  ParentAlpha = 0f, HasCanvasUpdate = true, HasCanvasFrame = true, CanvasOnHand = true, CanvasPose = Pose(),
                  CanvasRect = new[] { 100f, 50f, .5f, .5f }, CanvasSettings = new[] { 100f, 0f, 0f, 1f, 0f } } });
            int hc = 0, vc = 0, cc = 0;
            for (int tick = 0; tick < 240; tick++)
            {
                float now = tick / 15f; var packet = new TownServiceMotionPacket { Sequence = (ulong)tick + 1, SampleTime = now };
                foreach (var slot in hot) slot.Dirty = true;
                foreach (var slot in visible) slot.Dirty = true;
                // Intentionally harsher than settled closed fans: every cold
                // full-canvas heartbeat is waiting continuously too.
                foreach (var slot in cold) slot.Dirty = true;
                TownServiceMotionBudget.Fill(packet, hot, visible, cold, ref hc, ref vc, ref cc, now);
                t.True(TownServiceMotionCodec.Write(packet).Length <= 864, "three role budgets preserve the same one-event capacity");
                foreach (TownServiceMotionEntry entry in packet.Entries)
                {
                    float gap = now - (last.TryGetValue(entry.Module, out float before) ? before : 0f);
                    if (entry.Module <= 3) maxHot = Math.Max(maxHot, gap);
                    else if (entry.Module < 104) maxVisible = Math.Max(maxVisible, gap);
                    else maxCold = Math.Max(maxCold, gap);
                    last[entry.Module] = now;
                }
            }
            t.Equal(203, last.Count, "current fan roots and cold baseline recovery both retain bounded turns");
        }
        t.True(maxHot <= 1f / 15f + .0001f, "parked own front, held own front and stock face retain a turn every numeric tick");
        t.True(maxVisible < 1.2f, "one hundred visible upright root affinities no longer wait behind cold fan heartbeats");
        t.True(maxCold < 7f, "cold full-canvas recovery cannot be starved by constantly changing active fan roots");
        Console.WriteLine($"NPC three-role measured budget: senders=4 hotTargets=3 visibleRoots=100 coldRoots=100 allContinuouslyDirty=true maxHotGap={maxHot:F3}s maxVisibleGap={maxVisible:F3}s maxColdGap={maxCold:F3}s maxEvent=864B");
    }

    private static TownServiceFrame Front(ushort module, bool publicLane = false)
    {
        var frame = CapturedFront != null ? TownServiceDelta.Copy(CapturedFront) : new TownServiceFrame
        { Template = 1, Structure = 1, Nodes = new[] { new TownServiceNode { Binding = 1 } }, Pose = Pose() };
        frame.Module = module; frame.Session = 7; frame.Sequence = 1; frame.BaseSequence = 0; frame.Service = 1;
        frame.PublicCatalog = publicLane; frame.PublicClaim = publicLane ? 11u : 0u; frame.VisitorStock = false;
        frame.ParentModule = TownServiceFrame.ManifestModule; frame.ParentBinding = 0;
        frame.HighPriority = !publicLane; frame.Visible = true; frame.SampleTime = 0;
        if (CapturedFront == null)
            frame.Nodes[0].Values[TownServiceProperty.TmpText] = new TownServiceValue
                { Numbers = new float[19], Text = new[] { "Representative native item title and description", "font/original", "style" } };
        return frame;
    }
    private static void Saturation(Harness t)
    {
        t.Case("Four active peers preserve ordinary streams while urgent native fan fronts assemble");
        const int peers = 4, coldRows = 320, fanFaces = 8;
        var senders = new ExtrasSendScheduler[peers]; var receivers = new TownServiceFragments[peers];
        var arrived = new HashSet<ushort>[peers]; var ordinary = new HashSet<byte>[peers];
        var fanAt = new double[peers]; var ordinaryAt = new double[peers]; var pagesSeen = new int[peers];
        var pageStreams = new HashSet<int>(); var backgroundKinds = new[] { NetProtocol.MsgExtras, NetProtocol.MsgUseBarAnimation,
            NetProtocol.MsgNativeBoard, NetProtocol.MsgCardAppearance, NetProtocol.MsgItemAppearance, NetProtocol.MsgNativeDecisionPrompt };
        var backgrounds = new List<byte[]>();
        foreach (byte kind in backgroundKinds)
        { var raw = new byte[2048]; new Random(kind).NextBytes(raw);
          raw[0] = 0x31; raw[1] = 0x52; raw[2] = 0x56; raw[3] = 0x47; raw[4] = 3; raw[5] = kind; backgrounds.Add(raw); }
        int rawBytes = 0;
        for (int peer = 0; peer < peers; peer++)
        {
            senders[peer] = new ExtrasSendScheduler((ulong)(65536 * (peer + 1)), NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
            receivers[peer] = new TownServiceFragments(); arrived[peer] = new(); ordinary[peer] = new(); fanAt[peer] = ordinaryAt[peer] = -1;
            var publicIds = new ushort[coldRows];
            for (ushort id = 1; id <= coldRows; id++)
            { publicIds[id - 1] = id; TownServiceFrame row = Front(id, true);
              byte[] raw = TownServiceCodec.Write(row); senders[peer].Enqueue(raw, raw.Length, identity: row); }
            var publicManifest = new TownServiceFrame { Service = 1, Session = 7, Sequence = 1, PublicCatalog = true,
                PublicClaim = 11, Module = TownServiceFrame.ManifestModule, Visible = true, Modules = publicIds, Pose = Pose() };
            byte[] manifest = TownServiceCodec.Write(publicManifest); senders[peer].Enqueue(manifest, manifest.Length, identity: publicManifest);
            var fanIds = new ushort[fanFaces];
            for (ushort id = 1; id <= fanFaces; id++)
            { fanIds[id - 1] = id; TownServiceFrame row = Front(id); byte[] raw = TownServiceCodec.Write(row);
              if (peer == 0) rawBytes += raw.Length; senders[peer].Enqueue(raw, raw.Length, identity: row); }
            var census = new TownServiceFrame { Service = 1, Session = 7, Sequence = 1,
                Module = TownServiceFrame.ManifestModule, Visible = true, Modules = fanIds, Pose = Pose() };
            manifest = TownServiceCodec.Write(census); senders[peer].Enqueue(manifest, manifest.Length, identity: census);
        }
        int fastEvents = 0, fastBytes = 0; double nextFast = 0;
        for (int step = 0; step < 4000; step++)
        {
            double now = step * .051;
            for (int peer = 0; peer < peers; peer++)
            {
                foreach (byte[] raw in backgrounds) senders[peer].Enqueue(raw, raw.Length);
                byte[]? packet = senders[peer].NextBatch(now); if (packet == null) continue;
                t.True(packet.Length <= 864, "ordinary saturated scheduler keeps its original bounded event");
                byte[][] pages = PresentationBatch.TryRead(packet, packet.Length, out var batch) ? batch! : new[] { packet };
                foreach (byte[] page in pages)
                {
                    int stream = TownServiceFragments.Stream(page, page.Length);
                    if (stream < 0) { ordinary[peer].Add(page[5]); if (ordinary[peer].Count >= backgroundKinds.Length && ordinaryAt[peer] < 0) ordinaryAt[peer] = now; continue; }
                    pageStreams.Add(stream); pagesSeen[peer]++;
                    byte[]? full = receivers[peer].Accept(peer + 2, page, page.Length, now); if (full == null) continue;
                    byte[][] frames = TownServiceCodec.TryReadBundle(full, full.Length, out var bundle) ? bundle! : new[] { full };
                    foreach (byte[] bytes in frames)
                    { t.True(TownServiceCodec.TryRead(bytes, bytes.Length, out var frame), "cold native frames retain full original grammar");
                      if (frame != null && !frame.PublicCatalog && frame.Module != TownServiceFrame.ManifestModule) arrived[peer].Add(frame.Module); }
                    if (arrived[peer].Count == fanFaces && fanAt[peer] < 0) fanAt[peer] = now;
                }
            }
            if (now >= nextFast)
            {
                nextFast = now + TownServiceMotionCodec.SendInterval;
                for (int peer = 0; peer < peers; peer++)
                { byte[] bytes = TownServiceMotionCodec.Write(Packet(new TownServiceMotionEntry
                    { Kind = 1, Service = 1, Session = 7, Structure = 1, Module = 3, Pose = Pose((float)now), Visible = true }));
                  t.True(bytes.Length <= 864, "independent current numeric poses remain bounded despite full art queues");
                  fastEvents++; fastBytes += bytes.Length; }
            }
            bool complete = true; for (int peer = 0; peer < peers; peer++) complete &= fanAt[peer] >= 0 && ordinaryAt[peer] >= 0;
            if (complete) break;
        }
        for (int peer = 0; peer < peers; peer++)
        {
            t.True(fanAt[peer] >= 0 && fanAt[peer] < TownServiceFragments.AssemblyLifetime, "every cold urgent original fan completes while catalog and ordinary streams stay saturated");
            t.True(ordinaryAt[peer] >= 0 && ordinaryAt[peer] < 3, "original presence/board/native appearance streams retain their finite turn");
        }
        Console.WriteLine($"NPC queue measured fixture: frontSource={(CapturedFront != null ? "production Unity native capture" : "wire representative")}, nodes={Front(1).Nodes.Length}, fanRawBytes={rawBytes}, coldCatalogRows={coldRows}, peers={peers}, fanComplete={fanAt[0]:F3}s, ordinaryStreamsSeen={ordinaryAt[0]:F3}s, townPages={pagesSeen[0]}, independentFastEvents={fastEvents}, independentFastBytes={fastBytes}");
        t.True(pageStreams.Contains(TownServiceFrame.UrgentBundleStream), "cold priority fronts share the dedicated loss-safe urgent bundle stream");
    }
}

internal static class TownMotionProgram
{
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        { byte[] bytes = File.ReadAllBytes(args[0]);
          if (!TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame)) throw new InvalidDataException("Native fixture front failed codec validation.");
          TownMotionVectors.CapturedFront = frame; }
        var t = new Harness(); TownMotionVectors.Run(t); return t.Report();
    }
}
