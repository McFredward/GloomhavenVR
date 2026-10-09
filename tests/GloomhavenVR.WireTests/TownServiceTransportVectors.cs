using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownServiceTransportVectors
{
    internal static void Run(Harness t)
    {
        ExactOriginalPool(t);
        RackClocks(t);
        PublicCatalogLanes(t);
        PrivateWorkspaceCloth(t);
        SharedTempleAvailability(t);
        PrivateTransactionReservations(t);
        AdditiveReviewRecords(t);
        NativeTemplateMetadata(t);
        CompleteOpeningBypassesOldBundle(t);
        OpeningBudget(t);
        RepeatedDistinctOpenings(t);
        ReplacingOfferedSourcesInFlight(t);
        HoverCensusDoesNotRestartOriginals(t);
        ExactColdCompletion(t);
        OriginalSupersessionKeepsDependencies(t);
        t.Case("Town service original widgets use the real wire header and loss-safe module lanes");
        var frame = Frame(62000, 1);
        byte[] raw = TownServiceCodec.Write(frame);
        t.Wire(Hex.Bytes("31 52 56 47 03 13"), raw, 6, "independent existing GVR1 little-endian header");
        t.Equal(19, NetPacket.PeekType(raw, raw.Length), "production router recognizes town snapshots");
        foreach (bool compressed in new[] { false, true })
        {
            byte[][] pages = ExtrasFragments.Encode(raw, raw.Length, 65536UL + frame.Module,
                TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: compressed);
            var receiver = new TownServiceFragments();
            byte[]? result = null;
            for (int i = pages.Length - 1; i >= 0; i--)
            {
                t.True(pages[i].Length <= ExtrasFragments.MaxDatagramBytes, "page stays inside global datagram cap");
                t.Equal(62000, TownServiceFragments.Stream(pages[i], pages[i].Length), "every page retains high pool module ID");
                byte[]? next = receiver.Accept(2, pages[i], pages[i].Length, .1);
                if (i > 0) t.True(next == null, "reordered incomplete module stays atomic");
                result = next ?? result;
            }
            t.True(result != null, "actual transport publishes complete module");
            if (result != null) t.Wire(raw, result, result.Length, "all original widget outputs preserved by fragmentation");
            foreach (byte[] page in pages) t.True(receiver.Accept(2, page, page.Length, .2) == null, "completed module cannot replay");
        }
        var queue = new TownServiceSendQueue(65536);
        var frames = new[] { Frame(62000, 1), Frame(4, 1), Frame(65000, 1) };
        foreach (TownServiceFrame row in frames) { byte[] bytes = TownServiceCodec.Write(row); queue.Enqueue(bytes, bytes.Length, row); }
        byte[] smoke=TownServiceCodec.WriteBundle(new[]{TownServiceCodec.Write(frames[0]),TownServiceCodec.Write(frames[1])});
        t.True(TownServiceCodec.TryReadBundle(smoke,smoke.Length,out byte[][]? smokeDecoded),"bundle raw roundtrip");
        var multiplexed = new TownServiceFragments();
        var arrived = new HashSet<ushort>();
        for (int i = 0; i < 100; i++)
        {
            byte[]? page = queue.Next(i * .05); if (page == null) continue;
            byte[]? complete = multiplexed.Accept(2, page, page.Length, i * .05);
            if(complete==null)continue;
            byte[][] messages=TownServiceCodec.TryReadBundle(complete,complete.Length,out byte[][]? expanded)?expanded!:new[]{complete};
            foreach(byte[] message in messages)
                if(TownServiceCodec.TryRead(message,message.Length,out TownServiceFrame? decoded))arrived.Add(decoded!.Module);
        }
        t.Equal(3, arrived.Count, "interleaved pool modules each complete without coalescing different rows");
        queue.Clear();
        frame.Sequence = 5; raw = TownServiceCodec.Write(frame); queue.Enqueue(raw, raw.Length, frame);
        bool reopened = false;
        for (int i = 100; i < 140; i++)
        {
            byte[]? page = queue.Next(i * .05); if (page == null) continue;
            byte[]? complete = multiplexed.Accept(2, page, page.Length, i * .05);
            reopened |= complete != null;
        }
        t.True(reopened, "closing and reopening preserves fragment sequence monotonicity");
        ColdService(t, 8, false); ColdService(t, 24, false); ColdService(t, 24, true); ColdService(t, 64, true);
        LateGameCatalog(t, false, 191 * 3 + 64); LateGameCatalog(t, false); LateGameCatalog(t, true);
        BundleBounds(t); UrgentBundleDependency(t); DelayedFragmentCensus(t);
    }
    private static void AdditiveReviewRecords(Harness t)
    {
        t.Case("NPC review extensions retain independent timing, complete layout and held-stock lanes");
        var donation = Frame(TownServiceFrame.ManifestModule, 9, 0);
        donation.Service = 2; donation.Template = 0; donation.Structure = 0;
        donation.TempleDonationKnown = true; donation.TempleDonationRevision = 1;
        byte[] legacy = TownServiceCodec.Write(donation);
        donation.HasTempleDonationCommitAge = true; donation.TempleDonationCommitAge = .5f;
        byte[] timed = TownServiceCodec.Write(donation);
        t.Wire(legacy, timed, legacy.Length, "commit timing leaves the prior manifest prefix unchanged");
        t.Wire(Hex.Bytes("5D 05 01 00 00 00 3F"), Slice(timed, legacy.Length, 7), 7,
            "independent93 commit-age little-endian vector");
        t.True(TownServiceCodec.TryRead(timed, timed.Length, out var committed)
            && committed!.TempleDonationCommitAge == .5f, "receiver retains prior donation age rather than restarting a blessing");
        var rack = Frame(4, 2, 1); rack.PublicCatalog = true; rack.TemplateAddress = "merchant.rack|native";
        rack.Rack = new TownRackState { Cassette = true, Crank = 5 };
        legacy = TownServiceCodec.Write(rack);
        rack.Rack.Layout = new[] { new TownCatalogSlot(7, 12), new TownCatalogSlot(19, 3073) };
        byte[] mapped = TownServiceCodec.Write(rack);
        t.Wire(legacy, mapped, legacy.Length, "full placement is additive after the prior rack and roller bytes");
        t.Wire(Hex.Bytes("5E 0F 01 02 00 07 00 00 00 0C 00 13 00 00 00 01 0C"),
            Slice(mapped, legacy.Length, 17), 17, "independent94 full placement vector includes cold pages");
        t.True(TownServiceCodec.TryRead(mapped, mapped.Length, out var layout)
            && TownCatalogLayout.Same(rack.Rack.Layout, layout!.Rack!.Layout), "native ordering differences cannot relocate the public stock");
        var stock = Frame(4, 2, 1); legacy = TownServiceCodec.Write(stock); stock.VisitorStock = true;
        byte[] held = TownServiceCodec.Write(stock);
        t.Wire(legacy, held, legacy.Length, "stock lane retains all original private widget bytes");
        t.Wire(Hex.Bytes("5F 01 01"), Slice(held, legacy.Length, 3), 3, "independent95 third cosmetic-lane vector");
        t.True(TownServiceCodec.TryRead(held, held.Length, out var observed) && observed!.VisitorStock,
            "held sample remains distinguishable from public pages and an NPC interaction");
        foreach (byte[] badTail in new[] { Hex.Bytes("5F 01 00"), Hex.Bytes("5F 02 01 00"), Hex.Bytes("5F 00") })
        {
            var bad = new byte[legacy.Length + badTail.Length]; Array.Copy(legacy, bad, legacy.Length);
            Array.Copy(badTail, 0, bad, legacy.Length, badTail.Length);
            t.True(!TownServiceCodec.TryRead(bad, bad.Length, out _), "invalid third lane marker is inert");
        }
    }
    private static void PrivateTransactionReservations(Harness t)
    {
        t.Case("TLV92 reserves only the offered NPC while private browsing remains unclaimed");
        for (byte service = 1; service <= 3; service++)
        {
            var manifest = Frame(TownServiceFrame.ManifestModule, 17, 0);
            manifest.Service = service; manifest.Template = 0; manifest.Structure = 0;
            byte[] browsing = TownServiceCodec.Write(manifest);
            t.True(TownServiceCodec.TryRead(browsing, browsing.Length, out TownServiceFrame? visitor)
                && !visitor!.TransactionActive, "an approached service remains available before an offer");
            manifest.TransactionActive = true;
            byte[] reserved = TownServiceCodec.Write(manifest);
            t.Wire(browsing, reserved, browsing.Length, "a reservation leaves the original private manifest prefix intact");
            t.Wire(Hex.Bytes("5C 01 01"), Slice(reserved, reserved.Length - 3, 3), 3,
                "only this service session carries its explicit reservation");
            t.True(TownServiceCodec.TryRead(reserved, reserved.Length, out TownServiceFrame? claimed)
                && claimed!.Service == service && claimed.TransactionActive,
                "each service can reserve independently without a global NPC lock");
            var released = TownServiceDelta.Copy(manifest); released.TransactionActive = false;
            t.True(TownServiceCodec.TryRead(TownServiceCodec.Write(released),
                    TownServiceCodec.Write(released).Length, out TownServiceFrame? open)
                && !open!.TransactionActive, "reclaiming the offer releases this service");
            byte[] duplicate = new byte[reserved.Length + 3];
            Array.Copy(reserved, duplicate, reserved.Length);
            Array.Copy(reserved, reserved.Length - 3, duplicate, reserved.Length, 3);
            t.True(!TownServiceCodec.TryRead(duplicate, duplicate.Length, out _),
                "duplicate reservation records are rejected");
            byte[] malformed = (byte[])reserved.Clone(); malformed[^1] = 2;
            t.True(!TownServiceCodec.TryRead(malformed, malformed.Length, out _),
                "invalid reservation grammar is rejected");
        }
        var foreign = Frame(4, 17, 1); foreign.TransactionActive = true;
        bool rejected = false;
        try { TownServiceCodec.Write(foreign); } catch (System.IO.InvalidDataException) { rejected = true; }
        t.True(rejected, "module data cannot forge a transaction reservation");
    }
    private static void PrivateWorkspaceCloth(Harness t)
    {
        t.Case("Private town furniture carries bounded physical cloth controls in its own module lane");
        var temple = Frame(7, 1, 1);
        temple.Service = 2; temple.TemplateAddress = "temple.counter|main";
        temple.WorkspaceCloth = new byte[] { 12, 244, 3, 253, 7, 249, 2, 254,
            9, 247, 4, 252, 6, 250, 1, 255 };
        byte[] raw = TownServiceCodec.Write(temple);
        t.Wire(Hex.Bytes("5A 12 01 02 0C F4 03 FD 07 F9 02 FE 09 F7 04 FC 06 FA 01 FF"),
            Slice(raw, raw.Length - 20, 20), 20, "private temple cloth extension has a stable two-runner grammar");
        t.True(TownServiceCodec.TryRead(raw, raw.Length, out TownServiceFrame? received)
            && received!.WorkspaceCloth != null, "private temple cloth survives module decoding");
        if (received?.WorkspaceCloth != null)
            t.Wire(temple.WorkspaceCloth, received.WorkspaceCloth, 16, "private controls preserve each signed edge sample");
        var changed = TownServiceDelta.Copy(temple);
        changed.Sequence = 2; changed.WorkspaceCloth![0] = 25;
        var delta = TownServiceDelta.Create(temple, changed);
        byte[] deltaBytes = TownServiceCodec.Write(delta);
        t.True(TownServiceCodec.TryRead(deltaBytes, deltaBytes.Length, out TownServiceFrame? deltaRead),
            "private cloth-only delta decodes");
        var expanded = TownServiceDelta.Expand(temple, deltaRead!);
        t.True(expanded?.WorkspaceCloth?[0] == 25 && temple.WorkspaceCloth[0] == 12,
            "new cloth sample overlays an immutable baseline after dropped intermediate packets");
        byte[] duplicate = new byte[raw.Length + 20];
        Array.Copy(raw, duplicate, raw.Length); Array.Copy(raw, raw.Length - 20, duplicate, raw.Length, 20);
        t.True(!TownServiceCodec.TryRead(duplicate, duplicate.Length, out _), "duplicate private cloth record rejected");
        byte[] foreign = (byte[])raw.Clone(); foreign[^18] = 2;
        t.True(!TownServiceCodec.TryRead(foreign, foreign.Length, out _), "unknown cloth grammar rejected");
        byte[] excessive = (byte[])raw.Clone(); excessive[^16] = 127;
        t.True(!TownServiceCodec.TryRead(excessive, excessive.Length, out _), "cloth deformation beyond physical bounds rejected");
        var wrongAddress = TownServiceDelta.Copy(temple); wrongAddress.TemplateAddress = "merchant.counter|main";
        bool rejected = false; try { TownServiceCodec.Write(wrongAddress); } catch (System.IO.InvalidDataException) { rejected = true; }
        t.True(rejected, "private cloth cannot attach to another station type");
        var enchant = Frame(8, 1, 1);
        enchant.Service = 3; enchant.TemplateAddress = "enchant.counter|main";
        enchant.WorkspaceCloth = new byte[] { 1, 255, 2, 254, 3, 253, 4, 252 };
        byte[] enchantBytes = TownServiceCodec.Write(enchant);
        t.True(TownServiceCodec.TryRead(enchantBytes, enchantBytes.Length, out TownServiceFrame? enchantRead)
            && enchantRead!.WorkspaceCloth?.Length == 8, "enchantress furniture carries one runner");
        var merchant = Frame(9, 1, 1);
        merchant.Service = 1; merchant.TemplateAddress = "merchant.counter|main";
        merchant.WorkspaceCloth = new byte[] { 8, 250, 3, 255, 4, 253, 2, 254 };
        byte[] merchantBytes = TownServiceCodec.Write(merchant);
        t.True(TownServiceCodec.TryRead(merchantBytes, merchantBytes.Length, out TownServiceFrame? merchantRead)
            && merchantRead!.WorkspaceCloth?.Length == 8
            && merchantRead.WorkspaceCloth[0] == merchant.WorkspaceCloth[0],
            "merchant visitor furniture carries its moving side hanging");
    }
    private static void SharedTempleAvailability(Harness t)
    {
        t.Case("TLV91 temple availability extends the owner manifest without changing its historical prefix");
        var manifest = Frame(TownServiceFrame.ManifestModule, 7, 0);
        manifest.Service = 2; manifest.Template = 0; manifest.Structure = 0;
        byte[] historical = TownServiceCodec.Write(manifest);
        manifest.TempleDonationKnown = true;
        manifest.TempleDonationRevision = 7;
        byte[] unavailable = TownServiceCodec.Write(manifest);
        t.Wire(historical, unavailable, historical.Length, "interaction state leaves complete historical manifest prefix byte-identical");
        t.Wire(Hex.Bytes("5B 06 01 00 07 00 00 00"), Slice(unavailable, unavailable.Length - 8, 8), 8,
            "additive interaction record carries unavailable state and blessing revision");
        t.True(TownServiceCodec.TryRead(unavailable, unavailable.Length, out TownServiceFrame? blocked)
            && blocked!.TempleDonationKnown && !blocked.TempleDonationAvailable
            && blocked.TempleDonationRevision == 7,
            "observer decodes explicit unavailable state without inventing gameplay state");
        manifest.TempleDonationAvailable = true;
        byte[] available = TownServiceCodec.Write(manifest);
        t.Wire(Hex.Bytes("5B 06 01 01 07 00 00 00"), Slice(available, available.Length - 8, 8), 8,
            "interaction record carries the owner's available donation affordance");
        t.True(TownServiceCodec.TryRead(available, available.Length, out TownServiceFrame? open)
            && open!.TempleDonationKnown && open.TempleDonationAvailable,
            "observer preserves explicit available state");
        byte[] duplicate = new byte[available.Length + 8];
        available.CopyTo(duplicate, 0); Array.Copy(available, available.Length - 8, duplicate, available.Length, 8);
        t.True(!TownServiceCodec.TryRead(duplicate, duplicate.Length, out _), "duplicate interaction state rejected");
        byte[] invalidFlag = (byte[])available.Clone(); invalidFlag[^5] = 2;
        t.True(!TownServiceCodec.TryRead(invalidFlag, invalidFlag.Length, out _), "invalid availability flag rejected");
        var wrongService = Frame(TownServiceFrame.ManifestModule, 8, 0);
        wrongService.Template = 0; wrongService.Structure = 0; wrongService.TempleDonationKnown = true;
        bool rejected = false;
        try { TownServiceCodec.Write(wrongService); } catch (System.IO.InvalidDataException) { rejected = true; }
        t.True(rejected, "temple availability cannot attach to another resident manifest");
        manifest.Visible = false;
        rejected = false;
        try { TownServiceCodec.Write(manifest); } catch (System.IO.InvalidDataException) { rejected = true; }
        t.True(rejected, "closed session cannot retain a stale availability lock");
    }
    private static void LateGameCatalog(Harness t, bool contention, int count = 673 * 3 + 64)
    {
        t.Case("Native stock/owned card modules="+count+" contention=" + contention);
        t.True(TownServiceFrame.MaxModules >= count, "late catalog includes body, original row, face and overhead without truncation");
        var sender = new ExtrasSendScheduler(65536, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        var receiver = new TownServiceFragments();
        var missing = new HashSet<ushort>();
        var ids = new ushort[count];
        int initialPages = 0, initialWireBytes = 0;
        var budgetQueue = new TownServiceSendQueue(65536);
        for (ushort id = 1; id <= count; id++)
        {
            ids[id - 1] = id; missing.Add(id);
            var sample = Frame(id, 1, id <= 8 ? 128 : 16);
            byte[] bytes = TownServiceCodec.Write(sample); sender.Enqueue(bytes, bytes.Length, identity: sample);
            budgetQueue.Enqueue(bytes, bytes.Length, sample);
            byte[][] encoded=ExtrasFragments.Encode(bytes,bytes.Length,65536UL+id,TownServiceCodec.MessageType,
                TownServiceCodec.FragmentType,TownServiceFrame.MaxBytes,compress:true);
            initialPages+=encoded.Length;foreach(byte[] page in encoded)initialWireBytes+=page.Length;
        }
        // Count the actual production bundles/fragments for this immutable census.
        // A saturated global round retains three town turns per21; this lane gives
        // one of every three non-census turns to cold originals. Census costs its
        // actual compressed pages at the unchanged five-second renewal cadence.
        int coldBundlePages = 0;
        while (budgetQueue.Next(coldBundlePages * .051) != null) coldBundlePages++;
        var budgetManifest = Frame(TownServiceFrame.ManifestModule, 1, 0);
        budgetManifest.Modules = ids; budgetManifest.Template = 0; budgetManifest.Structure = 0;
        byte[] budgetCensus = TownServiceCodec.Write(budgetManifest);
        int censusPages = ExtrasFragments.Encode(budgetCensus, budgetCensus.Length,
            65536UL + TownServiceFrame.ManifestModule, TownServiceCodec.MessageType,
            TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: true).Length;
        const double slowSenderFrames = 18, townTurns = 3, globalTurns = 21;
        double availableTownPagesPerSecond = slowSenderFrames * townTurns / globalTurns - censusPages / 5d;
        t.True(coldBundlePages > 0 && availableTownPagesPerSecond > 0,
            "cold catalog has a finite production-encoded fair budget");
        // Six outstanding borrowed turns, the initial census and one arbitration
        // round cover start/end phase. This derives a deadline without changing
        // transport bandwidth or borrowing extra turns from the original streams.
        double fairColdSeconds = (coldBundlePages * 3d + 6 + censusPages + townTurns)
            / availableTownPagesPerSecond;
        var backgrounds = new List<byte[]>();
        if(contention)foreach (byte kind in new[] { NetProtocol.MsgExtras, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgNativeBoard,
            NetProtocol.MsgCardAppearance, NetProtocol.MsgItemAppearance, NetProtocol.MsgNativeDecisionPrompt })
        {
            var bytes = new byte[4096]; new Random(kind).NextBytes(bytes);
            bytes[0]=0x31;bytes[1]=0x52;bytes[2]=0x56;bytes[3]=0x47;bytes[4]=3;bytes[5]=kind;backgrounds.Add(bytes);
        }
        double firstHeld = -1, firstOrdinary = -1, completeAt = -1, last = -1;
        TownServiceFrame heldBaseline=Frame((ushort)count,1,16);
        ulong sequence = 2;
        int heldCompletions = 0, warmHeld=0; double warmMaxDelay=0; bool receivedManifest=false; int deliveredBytes=0;
        // One guaranteed background town turn per21 globally saturated turns, including
        // a slow18fps sender and census overhead. Budget derives from actual encoded
        // pages, not a fast-LAN promise the864B/50ms wire cannot physically provide.
        double limitSeconds=initialPages*2.0+30;
        for (int frame = 0; frame < 18 * limitSeconds; frame++)
        {
            double now = frame / 18d;
            if (frame % 2 == 0) foreach (byte[] bytes in backgrounds) sender.Enqueue(bytes, bytes.Length);
            if (frame % 9 == 0)
            {
                var manifest = Frame(TownServiceFrame.ManifestModule, sequence++, 0);manifest.Modules=ids;manifest.Template=0;manifest.Structure=0;
                byte[] bytes=TownServiceCodec.Write(manifest);sender.Enqueue(bytes,bytes.Length,identity:manifest);
            }
            if (frame >= 18 && frame % 3 == 0)
            {
                var pose = Frame((ushort)count, sequence++, 16);pose.Pose[0]=(float)Math.Sin(now);pose.SampleTime=(float)now;
                var held=TownServiceDelta.Create(heldBaseline,pose);held.HighPriority=true;
                byte[] bytes=TownServiceCodec.Write(held);sender.Enqueue(bytes,bytes.Length,identity:held);
            }
            byte[]? packet=sender.NextBatch(now);if(packet==null)continue;
            t.True(last<0||now-last>=.05-1e-6,"late catalog retains global no-burst cadence");last=now;
            t.True(packet.Length<=ExtrasFragments.MaxDatagramBytes,"late catalog retains global datagram cap");
            byte[][] pages=PresentationBatch.TryRead(packet,packet.Length,out byte[][]? batch)?batch!:new[]{packet};
            foreach(byte[] page in pages)
            {
                if(TownServiceFragments.Stream(page,page.Length)<0)continue;
                byte[]? bytes=receiver.Accept(2,page,page.Length,now);
                deliveredBytes+=page.Length;
                if(bytes==null)continue;
                byte[][] messages=TownServiceCodec.TryReadBundle(bytes,bytes.Length,out byte[][]? expanded)?expanded!:new[]{bytes};
                foreach(byte[] message in messages)
                {
                    t.True(TownServiceCodec.TryRead(message,message.Length,out TownServiceFrame? arrived),"all bundled modules decode");
                    if(arrived!.Module==TownServiceFrame.ManifestModule)
                    {receivedManifest=true;t.Equal(count,arrived.Modules.Length,"complete large manifest survives wire");continue;}
                    missing.Remove(arrived.Module);
                    if(arrived.BaseSequence==0)t.Equal(arrived.Module<=8?128:16,arrived.Nodes.Length,"all original node content retained");
                    if(arrived.Module==count)
                    {
                        if(arrived.BaseSequence!=0){if(firstHeld<0)firstHeld=now;heldCompletions++;}
                        if(completeAt>=0&&arrived.BaseSequence!=0)
                        {
                            warmHeld++;warmMaxDelay=Math.Max(warmMaxDelay,now-arrived.SampleTime);
                            TownServiceFrame? expandedHeld=TownServiceDelta.Expand(heldBaseline,arrived);
                            t.True(expandedHeld!=null&&expandedHeld.Nodes.Length==16,"warm moving original retains full baseline contents");
                        }
                    }
                    else if(firstOrdinary<0)firstOrdinary=now;
                }
            }
            if(missing.Count==0&&receivedManifest&&completeAt<0)completeAt=now;
            if(completeAt>=0&&now>=completeAt+8)break;
        }
        t.True(firstHeld>=1&&firstHeld<5,"held native card bypasses initial full-catalog backlog");
        t.True(firstOrdinary>=0&&firstOrdinary<15,"manifest heartbeats cannot starve native catalog modules");
        t.True(heldCompletions>10,"continuous held motion remains live while cold catalog fills");
        t.True(receivedManifest,"large manifest completes");
        t.True(completeAt>=0&&completeAt<(contention?fairColdSeconds:count<1000?10:30),"cold catalog remains within measured compression budget");
        t.True(warmHeld>5&&warmMaxDelay<(contention?5:.5),"held motion stays live after cold catalog completes");
        t.Equal(0,missing.Count,"every late-game original face/body/quantity module completes");
        Console.WriteLine("TOWN_LATE modules="+count+" contention="+contention+" initialPages="+initialPages+" individualBytes="+initialWireBytes+" deliveredBytes="+deliveredBytes+" firstHeld="+firstHeld.ToString("F3")+" firstStock="+firstOrdinary.ToString("F3")+" complete="+completeAt.ToString("F3")+" warmHeldMaxDelay="+warmMaxDelay.ToString("F3")+" coldBundlePages="+coldBundlePages+" censusPages="+censusPages+" fairColdSeconds="+fairColdSeconds.ToString("F3"));
    }
    private static void DelayedFragmentCensus(Harness t)
    {
        t.Case("Delayed census cannot destroy a newer partial module");
        var receiver=new TownServiceFragments();TownServiceFrame module=Frame(3,101,128);
        byte[] raw=TownServiceCodec.Write(module);
        byte[][] pages=ExtrasFragments.Encode(raw,raw.Length,65536+3,TownServiceCodec.MessageType,TownServiceCodec.FragmentType,TownServiceFrame.MaxBytes);
        t.True(pages.Length>2,"adversarial module needs multiple datagrams");
        t.True(receiver.Accept(2,pages[0],pages[0].Length,.1)==null,"new module starts before census completes");
        TownServiceFrame census=Frame(TownServiceFrame.ManifestModule,100,0);census.Template=0;census.Structure=0;census.Modules=new ushort[]{1,2};
        byte[] manifest=TownServiceCodec.Write(census);
        foreach(byte[] page in ExtrasFragments.Encode(manifest,manifest.Length,65536+TownServiceFrame.ManifestModule,TownServiceCodec.MessageType,TownServiceCodec.FragmentType,TownServiceFrame.MaxBytes))
            receiver.Accept(2,page,page.Length,.2);
        byte[]? result=null;
        for(int i=1;i<pages.Length;i++)result=receiver.Accept(2,pages[i],pages[i].Length,.3+i*.01)??result;
        t.True(result!=null,"old census leaves newer assembly intact");
        if(result!=null)t.Wire(raw,result,raw.Length,"new complete baseline survives old census byte for byte");
        foreach(byte[] page in ExtrasFragments.Encode(manifest,manifest.Length,131072+TownServiceFrame.ManifestModule,TownServiceCodec.MessageType,TownServiceCodec.FragmentType,TownServiceFrame.MaxBytes))
            receiver.Accept(2,page,page.Length,TownServiceFragments.AssemblyLifetime+2);
        var streams=(System.Collections.IDictionary)typeof(TownServiceFragments).GetField("_streams",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(receiver)!;
        t.Equal(1,streams.Count,"expired historical lanes reclaimed within fixed pool bound");
    }
    private static void UrgentBundleDependency(Harness t)
    {
        t.Case("Grabbing a card promotes its in-flight bundle baseline");
        var queue=new TownServiceSendQueue(65536);var receiver=new TownServiceFragments();
        for(ushort id=1;id<=20;id++){var baseline=PromotionFrame(id,1);byte[] raw=TownServiceCodec.Write(baseline);queue.Enqueue(raw,raw.Length,baseline);}
        byte[] first=queue.Next(0)!;
        t.Equal((int)TownServiceFrame.BundleStream,TownServiceFragments.Stream(first,first.Length),"initial catalog uses the background bundle stream");
        t.True(receiver.Accept(2,first,first.Length,0)==null,"cold bundle is still in flight");
        TownServiceFrame original=PromotionFrame(2,1), current=PromotionFrame(2,2);current.Pose[0]=.4f;
        TownServiceFrame moving=TownServiceDelta.Create(original,current);moving.HighPriority=true;
        byte[] bytes=TownServiceCodec.Write(moving);queue.Enqueue(bytes,bytes.Length,moving);
        bool baselineArrived=false,poseArrived=false,bundleArrived=false;
        TownServiceFrame? receivedBaseline=null,rendered=null;
        double baselineAt=-1,poseAt=-1,coldAt=-1;
        int nextTick=1;
        for(int i=1;i<20&&!poseArrived;i++)
        {
            nextTick=i+1;
            byte[]? page=queue.Next(i*.051);if(page==null)continue;
            byte[]? result=receiver.Accept(2,page,page.Length,i*.051);if(result==null)continue;
            int stream=TownServiceFragments.Stream(page,page.Length);
            // Promoted original widgets now use their own compressed bundle stream.
            // A completed urgent container is not completion of the in-flight cold
            // catalog: apply its children just as the production receiver does.
            byte[][] children=TownServiceCodec.TryReadBundle(result,result.Length,out byte[][]? members)?members!:new[]{result};
            if(stream==TownServiceFrame.BundleStream){bundleArrived=true;coldAt=i*.051;}
            foreach(byte[] child in children)
            {
                if(!TownServiceCodec.TryRead(child,child.Length,out TownServiceFrame? frame)||frame!.Module!=2)continue;
                t.Equal((int)TownServiceFrame.UrgentBundleStream,stream,"promoted dependency and pose use the independent urgent stream");
                if(frame.BaseSequence==0)
                {
                    byte[] expected=TownServiceCodec.Write(original);
                    t.Wire(expected,child,child.Length,"promoted dependency is the exact original complete baseline");
                    receivedBaseline=frame;baselineArrived=true;baselineAt=i*.051;
                }
                else
                {
                    t.True(baselineArrived,"promoted pose never arrives before its exact original dependency");
                    rendered=TownServiceDelta.Expand(receivedBaseline,frame);
                    t.True(rendered!=null,"urgent pose reconstructs against its delivered original baseline");
                    if(rendered!=null)
                    {
                        byte[] expected=TownServiceCodec.Write(current),actual=TownServiceCodec.Write(rendered);
                        t.Wire(expected,actual,actual.Length,"urgent native card has every original property and the current pose");
                        poseArrived=true;poseAt=i*.051;
                    }
                }
            }
        }
        t.True(baselineArrived&&poseArrived&&!bundleArrived,"urgent card renders with exact baseline before cold bundle completes");
        var backgroundModules=new HashSet<ushort>();
        for(int i=nextTick;i<100&&backgroundModules.Count<20;i++)
        {
            byte[]? page=queue.Next(i*.051);if(page==null)continue;
            byte[]? result=receiver.Accept(2,page,page.Length,i*.051);if(result==null)continue;
            if(TownServiceFragments.Stream(page,page.Length)!=TownServiceFrame.BundleStream)continue;
            t.True(TownServiceCodec.TryReadBundle(result,result.Length,out byte[][]? children),"original cold bundle remains valid after urgent promotion");
            if(children==null)continue;
            foreach(byte[] child in children)
            {
                t.True(TownServiceCodec.TryRead(child,child.Length,out TownServiceFrame? frame),"every original background dependency still decodes");
                if(frame==null)continue;
                t.True(frame.Module>=1&&frame.Module<=20,"background bundle retains the original catalog membership");
                byte[] expected=TownServiceCodec.Write(PromotionFrame(frame.Module,1));
                t.Wire(expected,child,child.Length,"urgent promotion leaves each background original byte unchanged");
                backgroundModules.Add(frame.Module);
            }
            if(!bundleArrived)coldAt=i*.051;
            bundleArrived=true;
        }
        t.True(bundleArrived&&backgroundModules.Count==20,"cold catalog still completes without dropping promoted or neighboring originals");
        Console.WriteLine("TOWN_PROMOTION baseline="+baselineAt.ToString("F3")+" pose="+poseAt.ToString("F3")+" cold="+coldAt.ToString("F3"));
    }
    private static void CompleteOpeningBypassesOldBundle(Harness t)
    {
        t.Case("A complete offered original does not replay an obsolete cold bundle dependency");
        var queue = new TownServiceSendQueue(65536);
        for (ushort id = 1; id <= 20; id++)
        {
            var original = PromotionFrame(id, 1); byte[] bytes = TownServiceCodec.Write(original);
            queue.Enqueue(bytes, bytes.Length, original);
        }
        byte[] initial = queue.Next(0)!;
        t.Equal((int)TownServiceFrame.BundleStream, TownServiceFragments.Stream(initial, initial.Length), "old background original remains in flight");
        var replacement = PromotionFrame(2, 2); replacement.HighPriority = true; replacement.Pose[0] = .4f;
        byte[] current = TownServiceCodec.Write(replacement); queue.Enqueue(current, current.Length, replacement);
        var receiver = new TownServiceFragments(); bool arrived = false;
        for (int i = 1; i < 30 && !arrived; i++)
        {
            byte[]? page = queue.NextUrgent(i * .051); if (page == null) continue;
            byte[]? packet = receiver.Accept(2, page, page.Length, i * .051); if (packet == null) continue;
            if (TownServiceFragments.Stream(page, page.Length) != TownServiceFrame.UrgentBundleStream) continue;
            t.True(TownServiceCodec.TryReadBundle(packet, packet.Length, out var members), "current urgent original decodes independently");
            foreach (byte[] member in members!)
            {
                TownServiceCodec.TryRead(member, member.Length, out var frame);
                t.True(frame!.Sequence == 2 && frame.BaseSequence == 0, "first urgent picture is the replacement, never obsolete sequence1");
                t.Wire(current, member, current.Length, "complete replacement retains all source-owned properties");
                arrived = true;
            }
        }
        t.True(arrived, "complete new card arrives before the unrelated old catalog finishes");
    }

    private static void OpeningBudget(Harness t)
    {
        t.Case("A finite offered picture borrows existing page turns under saturated presentation traffic");
        var sender = new ExtrasSendScheduler(65536, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        var census = Frame(TownServiceFrame.ManifestModule, 100, 0); census.Service = 3;
        census.Template = 0; census.Structure = 0; census.TransactionActive = true;
        census.Modules = new ushort[33]; census.RequiredVisibleModules = census.Modules;
        for (ushort id = 1; id <= 33; id++)
        {
            census.Modules[id - 1] = id;
            var frame = Frame(id, 1, 64); frame.Service = 3; frame.HighPriority = true;
            byte[] bytes = TownServiceCodec.Write(frame); sender.Enqueue(bytes, bytes.Length, identity: frame);
        }
        byte[] manifest = TownServiceCodec.Write(census); sender.Enqueue(manifest, manifest.Length, identity: census);
        var backgrounds = new List<byte[]>();
        foreach (byte kind in new[] { NetProtocol.MsgExtras, NetProtocol.MsgUseBarAnimation,
            NetProtocol.MsgNativeBoard, NetProtocol.MsgCardAppearance, NetProtocol.MsgItemAppearance, NetProtocol.MsgNativeDecisionPrompt })
        {
            var bytes = new byte[4096]; new Random(kind).NextBytes(bytes);
            bytes[0] = 0x31; bytes[1] = 0x52; bytes[2] = 0x56; bytes[3] = 0x47; bytes[4] = 3; bytes[5] = kind;
            backgrounds.Add(bytes); sender.Enqueue(bytes, bytes.Length);
        }
        var receiver = new TownServiceFragments(); var arrived = new HashSet<ushort>();
        double ready = -1; int otherPages = 0;
        for (int tick = 0; tick < 640; tick++)
        {
            double now = tick * .051; byte[]? packet = sender.NextBatch(now); if (packet == null) continue;
            t.True(packet.Length <= PresentationBatch.MaxSize, "first-picture reservation retains the global864-byte event limit");
            t.True(sender.NextBatch(now) == null, "first-picture reservation never adds a same-frame catch-up event");
            byte[][] parts = NetPacket.PeekType(packet, packet.Length) == NetProtocol.MsgPresentationBatch
                && PresentationBatch.TryRead(packet, packet.Length, out var batch) ? batch! : new[] { packet };
            foreach (byte[] page in parts)
            {
                if (TownServiceFragments.Stream(page, page.Length) < 0) { otherPages++; continue; }
                byte[]? full = receiver.Accept(2, page, page.Length, now); if (full == null) continue;
                byte[][] modules = TownServiceCodec.TryReadBundle(full, full.Length, out var packed) ? packed! : new[] { full };
                foreach (byte[] module in modules)
                    if (TownServiceCodec.TryRead(module, module.Length, out var frame)) arrived.Add(frame!.Module);
            }
            if (ready < 0 && arrived.Count == 34) ready = now;
        }
        t.True(ready >= 0 && ready < 1, "all33 current originals and their exact census arrive below1s with every main presentation stream busy");
        t.True(otherPages >= 36, "background streams continue and repay the finite opening reservation");
        Console.WriteLine("TOWN_OPENING ready=" + ready.ToString("F3") + "s otherPages=" + otherPages);
    }

    private static void RepeatedDistinctOpenings(Harness t)
    {
        foreach (int count in new[] { 22, 33 })
        foreach (bool priorInFlight in new[] { false, true })
        foreach (bool withdraw in new[] { false, true })
        {
            t.Case("Distinct complete opening repeats without inheriting old urgent debt: " + count + "/" + priorInFlight + "/withdraw=" + withdraw);
            var sender = new ExtrasSendScheduler(65536, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
            var receiver = new TownServiceFragments(); var arrived = new HashSet<ushort>();
            var expected = new Dictionary<ushort, byte[]>(); int otherPages = 0;
            var progressed = new HashSet<int>();
            var backgrounds = new List<byte[]>();
            foreach (byte kind in new[] { NetProtocol.MsgExtras, NetProtocol.MsgUseBarAnimation,
                NetProtocol.MsgNativeBoard, NetProtocol.MsgCardAppearance, NetProtocol.MsgItemAppearance, NetProtocol.MsgNativeDecisionPrompt })
            {
                var bytes = new byte[4096]; new Random(kind).NextBytes(bytes);
                bytes[0] = 0x31; bytes[1] = 0x52; bytes[2] = 0x56; bytes[3] = 0x47; bytes[4] = 3; bytes[5] = kind;
                backgrounds.Add(bytes);
            }
            void Busy() { foreach (byte[] bytes in backgrounds) sender.Enqueue(bytes, bytes.Length); }
            void Offer(ulong sequence, bool active)
            {
                expected.Clear();
                var ids = new ushort[count];
                for (ushort id = 1; id <= count; id++)
                {
                    ids[id - 1] = id;
                    if (!active) continue;
                    var frame = PromotionFrame(id, sequence); frame.Service = 3; frame.HighPriority = true;
                    frame.Pose[0] = sequence * .01f;
                    foreach (TownServiceNode node in frame.Nodes)
                        if (node.Values.TryGetValue(TownServiceProperty.TmpText, out var text)) text.Text[0] += "/offer" + sequence;
                    byte[] bytes = TownServiceCodec.Write(frame); expected.Add(id, bytes);
                    sender.Enqueue(bytes, bytes.Length, identity: frame);
                }
                var manifest = Frame(TownServiceFrame.ManifestModule, sequence, 0); manifest.Service = 3;
                manifest.Template = 0; manifest.Structure = 0; manifest.TransactionActive = active;
                manifest.Modules = manifest.RequiredVisibleModules = ids;
                byte[] census = TownServiceCodec.Write(manifest); expected.Add(manifest.Module, census);
                sender.Enqueue(census, census.Length, identity: manifest);
                if (active)
                {
                    var pool = new TownServiceCodec.OriginalValuePoolBuilder(); int members = 0;
                    foreach (byte[] original in expected.Values) if (pool.TryAdd(original)) members++;
                    byte[] bundle = pool.Write();
                    byte[][] pages = ExtrasFragments.Encode(bundle, bundle.Length, 65536 + TownServiceFrame.UrgentBundleStream,
                        TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: true);
                    Console.WriteLine("TOWN_REPEATED_WIRE modules=" + count + " pooled=" + members + " raw=" + bundle.Length
                        + " pages=" + pages.Length + " wire=" + System.Linq.Enumerable.Sum(pages, page => page.Length));
                }
            }
            void Accept(byte[]? packet, double now, ulong current)
            {
                if (packet == null) return;
                t.True(packet.Length <= PresentationBatch.MaxSize, "repeated offers keep the exact864-byte shared cap");
                byte[][] pages = PresentationBatch.TryRead(packet, packet.Length, out var batch) ? batch! : new[] { packet };
                foreach (byte[] page in pages)
                {
                    if (TownServiceFragments.Stream(page, page.Length) < 0)
                    { otherPages++; progressed.Add(NetPacket.PeekType(page, page.Length)); continue; }
                    byte[]? full = receiver.Accept(2, page, page.Length, now); if (full == null) continue;
                    byte[][] modules = TownServiceCodec.TryReadBundle(full, full.Length, out var packed) ? packed! : new[] { full };
                    foreach (byte[] module in modules)
                    {
                        if (!TownServiceCodec.TryRead(module, module.Length, out var frame) || frame!.Sequence != current) continue;
                        t.True(expected.TryGetValue(frame.Module, out var source), "only exact current source modules count toward readiness");
                        t.Wire(source!, module, source!.Length, "complete source-owned offer metadata remains byte exact");
                        arrived.Add(frame.Module);
                    }
                }
            }
            double now = 0;
            if (priorInFlight)
            {
                Offer(1, true); Offer(2, false); bool began = false;
                for (int tick = 0; tick < 100 && !began; tick++)
                {
                    Busy(); byte[]? packet = sender.NextBatch(now); Accept(packet, now, 2);
                    if (packet != null)
                    {
                        byte[][] pages = PresentationBatch.TryRead(packet, packet.Length, out var batch) ? batch! : new[] { packet };
                        foreach (byte[] page in pages)
                            began |= TownServiceFragments.Stream(page, page.Length) == TownServiceFrame.UrgentBundleStream;
                    }
                    now += .051;
                }
                t.True(began, "the prior urgent full bundle has actually emitted its first fragment, beyond the inactive manifest");
            }
            double[] timings = new double[2];
            for (int offer = 0; offer < 2; offer++)
            {
                ulong current = (ulong)(3 + offer * 2);
                if (offer > 0 && withdraw) Offer(current - 1, false);
                Offer(current, true); arrived.Clear(); double started = now;
                timings[offer] = -1;
                for (int tick = 0; tick < 200; tick++)
                {
                    Busy(); Accept(sender.NextBatch(now), now, current);
                    t.True(sender.NextBatch(now) == null, "opening never emits a second same-frame event");
                    if (arrived.Count == count + 1) { timings[offer] = now - started; now += .051; break; }
                    now += .051;
                }
                t.True(timings[offer] >= 0 && timings[offer] <= (count == 22 ? 1 : 2),
                    "repeated distinct originals respect their measured bounded bandwidth:22 below1s,33 high-entropy below2s");
            }
            // A sustained busy visit still reserves real progress for every
            // established background source, rather than a five-second debt wait.
            int before = otherPages;
            for (int tick = 0; tick < 120; tick++) { Busy(); Accept(sender.NextBatch(now), now, 5); now += .051; }
            t.True(otherPages - before >= 120, "six saturated original streams continue after finite first-picture bursts");
            t.Equal(6, progressed.Count, "every one of the six saturated original streams makes progress");
            Console.WriteLine("TOWN_REPEATED count=" + count + " prior=" + priorInFlight + " withdraw=" + withdraw
                + " first=" + timings[0].ToString("F3") + " second=" + timings[1].ToString("F3") + " other=" + otherPages);
        }
    }

    private static void ReplacingOfferedSourcesInFlight(Harness t)
    {
        foreach (bool retired in new[] { false, true })
        {
            t.Case("A changed offered template or retired physical module does not block unchanged required originals: " + retired);
            var sender = new ExtrasSendScheduler(65536, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
            var receiver = new TownServiceFragments(); var expected = new Dictionary<ushort, byte[]>();
            var ids = new ushort[22];
            for (ushort id = 1; id <= 22; id++)
            {
                ids[id - 1] = id;
                var original = PromotionFrame(id, 1); original.Service = 3; original.HighPriority = true;
                byte[] bytes = TownServiceCodec.Write(original); expected[id] = bytes;
                sender.Enqueue(bytes, bytes.Length, identity: original);
            }
            var census = Frame(TownServiceFrame.ManifestModule, 1, 0); census.Service = 3;
            census.Template = 0; census.Structure = 0; census.TransactionActive = true;
            census.Modules = census.RequiredVisibleModules = ids;
            byte[] manifest = TownServiceCodec.Write(census); sender.Enqueue(manifest, manifest.Length, identity: census);
            var backgrounds = new List<byte[]>();
            foreach (byte kind in new[] { NetProtocol.MsgExtras, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgNativeBoard,
                NetProtocol.MsgCardAppearance, NetProtocol.MsgItemAppearance, NetProtocol.MsgNativeDecisionPrompt })
            {
                var bytes = new byte[4096]; new Random(kind).NextBytes(bytes);
                bytes[0] = 0x31; bytes[1] = 0x52; bytes[2] = 0x56; bytes[3] = 0x47; bytes[4] = 3; bytes[5] = kind;
                backgrounds.Add(bytes); sender.Enqueue(bytes, bytes.Length);
            }
            byte[] first = sender.NextBatch(0)!;
            byte[][] firstPages = PresentationBatch.TryRead(first, first.Length, out var initial) ? initial! : new[] { first };
            bool began = false;
            foreach (byte[] page in firstPages)
                if (TownServiceFragments.Stream(page, page.Length) == TownServiceFrame.UrgentBundleStream)
                {
                    began = true;
                    t.True(receiver.Accept(2, page, page.Length, 0) == null, "prior original bundle is genuinely incomplete");
                }
            t.True(began, "the old physical offer has emitted an actual urgent fragment");
            ushort card = retired ? (ushort)100 : (ushort)1;
            if (retired) TownServiceDelivery.Retire(false, false, 3, 99, 1);
            var replacement = PromotionFrame(card, 3); replacement.Service = 3; replacement.HighPriority = true;
            replacement.Template = 4; replacement.TemplateAddress = "offered-card/new"; replacement.Structure++;
            byte[] current = TownServiceCodec.Write(replacement); expected.Remove(1); expected[card] = current;
            sender.Enqueue(current, current.Length, identity: replacement);
            var currentIds = new List<ushort>(expected.Keys); currentIds.Sort();
            census = Frame(TownServiceFrame.ManifestModule, 3, 0); census.Service = 3;
            census.Template = 0; census.Structure = 0; census.TransactionActive = true;
            census.Modules = census.RequiredVisibleModules = currentIds.ToArray();
            manifest = TownServiceCodec.Write(census); expected[census.Module] = manifest;
            sender.Enqueue(manifest, manifest.Length, identity: census);
            var arrived = new HashSet<ushort>(); double ready = -1;
            for (int tick = 1; tick < 200; tick++)
            {
                double now = tick * .051; foreach (byte[] bytes in backgrounds) sender.Enqueue(bytes, bytes.Length);
                byte[]? packet = sender.NextBatch(now); if (packet == null) continue;
                t.True(packet.Length <= PresentationBatch.MaxSize, "source migration preserves the exact shared event cap");
                byte[][] pages = PresentationBatch.TryRead(packet, packet.Length, out var batch) ? batch! : new[] { packet };
                foreach (byte[] page in pages)
                {
                    if (TownServiceFragments.Stream(page, page.Length) < 0) continue;
                    byte[]? full = receiver.Accept(2, page, page.Length, now); if (full == null) continue;
                    byte[][] modules = TownServiceCodec.TryReadBundle(full, full.Length, out var packed) ? packed! : new[] { full };
                    foreach (byte[] module in modules)
                    {
                        if (!TownServiceCodec.TryRead(module, module.Length, out var frame)) continue;
                        // The newer census has its independent immediate lane.
                        // A useful old immutable bundle may finish with its older
                        // census/withdrawn card; actual ReceiveParsed rejects that
                        // census and current membership cannot render that card.
                        // Neither byte group counts toward the current picture.
                        if (retired && frame!.Module == TownServiceFrame.ManifestModule && frame.Sequence < 3) continue;
                        if (retired && frame!.Module == 1)
                        {
                            t.Equal(1UL, frame.Sequence, "only the exact already-started retired original can finish");
                            t.True(!currentIds.Contains(frame.Module), "current census excludes the withdrawn physical original");
                            continue;
                        }
                        t.True(expected.TryGetValue(frame!.Module, out var source), "retired old offered-card metadata is not replayed into the new picture");
                        if (!expected.TryGetValue(frame.Module, out source)) continue;
                        t.Equal(source.Length, module.Length, "obsolete differently shaped original metadata is not replayed into the new card");
                        if (module.Length == source.Length)
                            t.Wire(source, module, source.Length, "unchanged native originals migrate byte exactly; only the changed owner source is replaced");
                        arrived.Add(frame.Module);
                    }
                }
                if (arrived.Count == expected.Count) { ready = now - .051; break; }
            }
            t.True(ready >= 0 && ready < 1, "new offered template/retired source and all unchanged required originals assemble below1s");
            Console.WriteLine("TOWN_REPLACED_SOURCE retired=" + retired + " ready=" + ready.ToString("F3"));
        }
    }

    private static void ExactColdCompletion(Harness t)
    {
        t.Case("Completing an old background original cannot clear the newest cold offer marker");
        var queue = new TownServiceSendQueue(65536);
        for (ushort id = 1; id <= 2; id++)
        {
            var frame = PromotionFrame(id, 1); byte[] bytes = TownServiceCodec.Write(frame);
            queue.Enqueue(bytes, bytes.Length, frame);
        }
        t.True(queue.Next(0) != null, "the older original bundle has begun fragmentation");
        for (ushort id = 1; id <= 2; id++)
        {
            var frame = PromotionFrame(id, 3); frame.HighPriority = true;
            var random = new Random(id * 887);
            foreach (TownServiceNode node in frame.Nodes)
                if (node.Values.TryGetValue(TownServiceProperty.TmpText, out var text))
                {
                    var extra = new char[3000];
                    for (int i = 0; i < extra.Length; i++) extra[i] = (char)('a' + random.Next(26));
                    text.Text[0] += new string(extra);
                }
            byte[] bytes = TownServiceCodec.Write(frame); queue.Enqueue(bytes, bytes.Length, frame);
        }
        var manifest = Frame(TownServiceFrame.ManifestModule, 3, 0);
        manifest.Template = 0; manifest.Structure = 0; manifest.TransactionActive = true;
        manifest.Modules = new ushort[] { 1, 2 };
        byte[] census = TownServiceCodec.Write(manifest); queue.Enqueue(census, census.Length, manifest);
        // Public Next retains the ordinary2-urgent/1-background ordering, so the
        // old bundle's final page lands during the newer urgent assembly.
        for (int tick = 1; tick <= 3; tick++) queue.Next(tick * .051);
        t.True(queue.NextOpening(.204) != null, "a completion for sequence1 does not erase the pending exact sequence3 marker");
    }

    private static void HoverCensusDoesNotRestartOriginals(Harness t)
    {
        foreach (bool withdrawPrepared in new[] { false, true })
        {
            t.Case("Live hover census preserves useful fragmented originals: prepared withdrawal=" + withdrawPrepared);
            var lane = new TownServiceLaneSendQueue(65536);
            var receiver = new TownServiceFragments();
            var originals = new Dictionary<ushort, TownServiceFrame>();
            for (ushort id = 1; id <= 34; id++)
            {
                var original = PromotionFrame(id, 1); original.Service = 3; original.HighPriority = true;
                originals.Add(id, original);
                byte[] bytes = TownServiceCodec.Write(original); lane.Enqueue(bytes, bytes.Length, original);
            }
            void Census(ulong sequence, bool hover)
            {
                var manifest = Frame(TownServiceFrame.ManifestModule, sequence, 0); manifest.Service = 3;
                manifest.Template = 0; manifest.Structure = 0; manifest.TransactionActive = true;
                var members = new List<ushort>();
                for (ushort id = 1; id <= 34; id++) if (id != 34 || !withdrawPrepared || hover) members.Add(id);
                manifest.Modules = members.ToArray();
                manifest.RequiredVisibleModules = hover ? members.ToArray() : members.GetRange(0, 33).ToArray();
                byte[] bytes = TownServiceCodec.Write(manifest); lane.Enqueue(bytes, bytes.Length, manifest);
            }
            Census(100, true);
            byte[] first = lane.Next(0)!;
            t.Equal((int)TownServiceFrame.UrgentBundleStream, TownServiceFragments.Stream(first, first.Length),
                "native originals really began on the fragmented urgent stream");
            t.True(receiver.Accept(2, first, first.Length, 0) == null, "first page has not completed the native picture");
            ulong sequence = BitConverter.ToUInt64(first, 8);
            bool complete = false;
            for (int tick = 1; tick <= 48; tick++)
            {
                // Twelve real enter/exit revisions, including prepared membership
                // withdrawal, occur before this high-entropy original completes.
                if (tick <= 12)
                {
                    bool hover = tick % 2 == 0;
                    if (withdrawPrepared && hover)
                    {
                        TownServiceFrame restored = originals[34];
                        byte[] bytes = TownServiceCodec.Write(restored); lane.Enqueue(bytes, bytes.Length, restored);
                    }
                    Census((ulong)(100 + tick), hover);
                }
                byte[]? page = lane.Next(tick * .051); if (page == null) continue;
                t.True(page.Length <= ExtrasFragments.MaxDatagramBytes, "hover leaves the native datagram cap unchanged");
                if (TownServiceFragments.Stream(page, page.Length) != TownServiceFrame.UrgentBundleStream) continue;
                t.Equal(sequence, BitConverter.ToUInt64(page, 8),
                    "required/prepared hover changes cannot reset useful original fragment progress");
                byte[]? full = receiver.Accept(2, page, page.Length, tick * .051); if (full == null) continue;
                t.True(TownServiceCodec.TryReadBundle(full, full.Length, out var bundled), "original atomic assembly stays valid across hover edits");
                foreach (byte[] bytes in bundled!)
                {
                    t.True(TownServiceCodec.TryRead(bytes, bytes.Length, out var original), "retained exact original still decodes");
                    if (original!.Module == TownServiceFrame.ManifestModule) continue;
                    byte[] expected = TownServiceCodec.Write(originals[original.Module]);
                    t.Wire(expected, bytes, expected.Length, "useful originals keep every owner-authored byte");
                }
                complete = true; break;
            }
            t.True(complete, "useful fragmented native picture completes despite repeated hover changes");
        }
    }

    private static void OriginalSupersessionKeepsDependencies(Harness t)
    {
        t.Case("Complete-original supersession preserves named delta baselines and other source boundaries");
        var original = PromotionFrame(1, 1);
        var queue = new ExtrasSendQueue(65537, TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
            preserveFirst: true, snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
            counterMask: TownServiceFragments.StockLaneMarker - 1);
        byte[] bytes = TownServiceCodec.Write(original); queue.Enqueue(bytes, bytes.Length, original);
        var delta = TownServiceDelta.Copy(original); delta.Sequence = 2; delta.BaseSequence = 1; delta.Pose[0] = .3f;
        bytes = TownServiceCodec.Write(delta); queue.Enqueue(bytes, bytes.Length, delta);
        var replacement = PromotionFrame(1, 3);
        var future = TownServiceDelta.Copy(delta); future.Sequence = 4; future.Pose[0] = .5f;
        bytes = TownServiceCodec.Write(future); queue.Enqueue(bytes, bytes.Length, future);
        t.True(queue.HasTownDelta(1, replacement.Sequence), "a genuinely newer cumulative delta still names the old original");
        queue.SupersedeTownOriginal(replacement);
        t.True(queue.TryTakePending(out _, out var first) && ReferenceEquals(first, original), "named original remains before its meaningful cumulative delta");
        t.True(queue.TryTakePending(out _, out var second) && ReferenceEquals(second, future), "newer native numeric delta remains intact while obsolete delta retires");
        t.True(!queue.HasPending, "the strictly older delta cannot leave fragment debt after complete replacement");
        bytes = TownServiceCodec.Write(original); queue.Enqueue(bytes, bytes.Length, original);
        bytes = TownServiceCodec.Write(delta); queue.Enqueue(bytes, bytes.Length, delta);
        queue.SupersedeTownOriginal(replacement);
        t.True(!queue.HasPending && !queue.HasTownDelta(1), "complete current original supersedes both old original and strictly older delta without a future dependency");
        bytes = TownServiceCodec.Write(original); queue.Enqueue(bytes, bytes.Length, original);
        var different = TownServiceDelta.Copy(replacement); different.Session++;
        queue.SupersedeTownOriginal(different);
        t.True(queue.TryTakePending(out _, out first) && ReferenceEquals(first, original), "another session never supersedes an original");
        bytes = TownServiceCodec.Write(original); queue.Enqueue(bytes, bytes.Length, original);
        different = TownServiceDelta.Copy(replacement); different.Module++;
        queue.SupersedeTownOriginal(different);
        t.True(queue.TryTakePending(out _, out first) && ReferenceEquals(first, original), "another source module never supersedes an original");
    }

    private static TownServiceFrame PromotionFrame(ushort id, ulong sequence)
    {
        var frame = Frame(id, sequence, 16); var random = new Random(id * 991);
        // Exact cross-module pooling can now finish twenty identical originals in
        // one page. This proof needs a genuine in-flight cold bundle; distinct
        // owner text keeps that precondition without weakening any assertion.
        foreach (var node in frame.Nodes)
            if (node.Values.TryGetValue(TownServiceProperty.TmpText, out var text))
            {
                var characters = new char[128];
                for (int i = 0; i < characters.Length; i++) characters[i] = (char)('a' + random.Next(26));
                text.Text[0] = "Original owner " + id + "/" + node.Binding + ": " + new string(characters);
            }
        return frame;
    }
    private static void BundleBounds(Harness t)
    {
        t.Case("Lossless town bundle bounds and CPU measurement");
        byte[] legacy=Hex.Bytes("31 52 56 47 03 13 4e 61 01 01 63 00 00 00 07 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 04 00 03 00 00 00 7b 00 00 00 00 ff ff 00 00 00 00 00 00 80 3f 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3f 00 00 80 3f 00 00 80 3f 00 00 80 3f 00 00 00 00 00");
        t.True(TownServiceCodec.TryRead(legacy,legacy.Length,out TownServiceFrame? legacyFrame)&&legacyFrame!.Module==4&&legacyFrame.Sequence==7,"independent legacy record78 v1 golden still decodes");
        var frames=new List<byte[]>();
        for(ushort id=1;id<=16;id++)frames.Add(TownServiceCodec.Write(Frame(id,1,16)));
        byte[] packet=TownServiceCodec.WriteBundle(frames);
        t.Equal(NetProtocol.ExtIdTownServiceBundle, TownServiceCodec.BundleRecordId,
            "town bundle codec and presence protocol share the assigned extension id");
        t.True(TownServiceCodec.TryReadBundle(packet,packet.Length,out byte[][]? children),"bounded bundle parses");
        for(int at=6;at<packet.Length;){t.Equal(89,(int)packet[at++],"independent additive town bundle record89");int length=packet[at++];t.True(length>0&&at+length<=packet.Length,"ordinary bounded TLV framing");at+=length;}
        for(int i=0;i<frames.Count;i++)t.Wire(frames[i],children![i],frames[i].Length,"every original node/property/string survives byte for byte");
        for(int n=0;n<packet.Length;n+=97)t.True(!TownServiceCodec.TryReadBundle(packet,n,out _),"truncated bundle rejected");
        var tail=new byte[packet.Length+1];Buffer.BlockCopy(packet,0,tail,0,packet.Length);
        t.True(!TownServiceCodec.TryReadBundle(tail,tail.Length,out _),"trailing bundle bytes rejected");
        byte old=packet[9];packet[9]=255;t.True(!TownServiceCodec.TryReadBundle(packet,packet.Length,out _),"unbounded child count rejected");packet[9]=old;
        foreach(bool optimal in new[]{false,true})
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();int length=0;
            for(int n=0;n<100;n++)length=PresentationCompression.TryCompress(packet,packet.Length,optimal)!.Length;
            watch.Stop();
            byte[] packed=PresentationCompression.TryCompress(packet,packet.Length,optimal)!;
            uint crc=0xffffffff;
            foreach(byte value in packet){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u);}
            t.Equal(~crc,BitConverter.ToUInt32(packed,packed.Length-4),"table CRC equals independent bitwise wire checksum");
            Console.WriteLine("TOWN_COMPRESS optimal="+optimal+" input="+packet.Length+" compressed="+length+" meanDesktopMs="+(watch.Elapsed.TotalMilliseconds/100).ToString("F3"));
        }
    }
    private static void ColdService(Harness t, int rows, bool contention)
    {
        t.Case("town cold original widget catalog rows=" + rows + " saturatedOtherLanes=" + contention);
        var sender = new ExtrasSendScheduler(65536, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        var receiver = new TownServiceFragments();
        var expected = new HashSet<ushort>(); int rawTotal = 0, wireTotal = 0, deliveredBytes = 0;
        for (ushort module = 1; module <= rows + 3; module++)
        {
            TownServiceFrame sample = Frame(module, 1, module <= 3 ? 128 : 16);
            byte[] bytes = TownServiceCodec.Write(sample); rawTotal += bytes.Length;
            foreach (byte[] page in ExtrasFragments.Encode(bytes, bytes.Length, 65536UL + module,
                TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: true)) wireTotal += page.Length;
            expected.Add(module); sender.Enqueue(bytes, bytes.Length, identity: sample);
        }
        var backgrounds = new List<byte[]>();
        if (contention)
            foreach (byte kind in new[] { NetProtocol.MsgExtras, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgNativeBoard,
                NetProtocol.MsgCardAppearance, NetProtocol.MsgItemAppearance, NetProtocol.MsgNativeDecisionPrompt })
            {
                var bytes = new byte[4096]; new Random(kind).NextBytes(bytes);
                bytes[0] = 0x31; bytes[1] = 0x52; bytes[2] = 0x56; bytes[3] = 0x47; bytes[4] = 3; bytes[5] = kind;
                backgrounds.Add(bytes);
            }
        double last = -1, finished = -1; int initialCount = expected.Count;
        for (int frame = 0; frame < 90 * 40; frame++)
        {
            double now = frame / 90d;
            if (frame % 9 == 0) foreach (byte[] bytes in backgrounds) sender.Enqueue(bytes, bytes.Length);
            byte[]? packet = sender.NextBatch(now); if (packet == null) continue;
            t.True(last < 0 || now - last >= .05 - 1e-6, "cold catalog preserves global no-burst cadence"); last = now;
            t.True(packet.Length <= ExtrasFragments.MaxDatagramBytes, "cold catalog preserves shared datagram budget");
            byte[][] pages = PresentationBatch.TryRead(packet, packet.Length, out byte[][]? batch) ? batch! : new[] { packet };
            foreach (byte[] page in pages)
            {
                if (TownServiceFragments.Stream(page, page.Length) < 0) continue;
                deliveredBytes += page.Length;
                byte[]? complete = receiver.Accept(2, page, page.Length, now);
                if(complete==null)continue;
                byte[][] messages=TownServiceCodec.TryReadBundle(complete,complete.Length,out byte[][]? expanded)?expanded!:new[]{complete};
                foreach(byte[] message in messages)
                    if(TownServiceCodec.TryRead(message,message.Length,out TownServiceFrame? sample))expected.Remove(sample!.Module);
            }
            if (expected.Count == 0) { finished = now; break; }
        }
        t.Equal(0, expected.Count, "all synthetic native-like modules finish atomically before assembler expiry");
        Console.WriteLine("TOWN_COLD rows=" + rows + " modules=" + initialCount + " contention=" + contention
            + " raw=" + rawTotal + " compressedPages=" + wireTotal + " delivered=" + deliveredBytes + " completeSeconds=" + finished.ToString("F3"));
    }
    private static void RackClocks(Harness t)
    {
        t.Case("Cabinet clocks are additive85 with bounded explicit phase and causal stamps");
        var frame=Frame(10,1,1);frame.TemplateAddress="merchant.rack|";
        byte[] legacy=TownServiceCodec.Write(frame);
        frame.Rack=new TownRackState {Turn=0x01020304,Elapsed=.425f,LeadAngle=17.5f,Crank=11,Page=1,From=0,To=1,
            Members=new[]{new TownRackMember(12,0,false),new TownRackMember(13,1,true)}};
        byte[] bytes=TownServiceCodec.Write(frame);var extension=new List<byte>();var old=new List<byte>();old.AddRange(new ArraySegment<byte>(bytes,0,6));
        for(int at=6;at<bytes.Length;)
        {
            int start=at,id=bytes[at++],length=bytes[at++];
            if(id==85)extension.AddRange(new ArraySegment<byte>(bytes,at,length));
            else old.AddRange(new ArraySegment<byte>(bytes,start,length+2));at+=length;
        }
        byte[] golden=Hex.Bytes("01 04 03 02 01 9a 99 d9 3e 00 00 8c 41 0b 00 01 00 00 00 01 00 02 00 0c 00 00 00 00 0d 00 01 00 01");
        t.Wire(golden,extension.ToArray(),extension.Count,"independently specified85 clock layout");
        t.Wire(legacy,old.ToArray(),old.Count,"every legacy78 byte remains unchanged");
        t.True(TownServiceCodec.TryRead(bytes,bytes.Length,out var decoded)&&decoded!.Rack!.Same(frame.Rack),"explicit clock survives production decoder");
        t.True(TownServiceCodec.TryRead(old.ToArray(),old.Count,out var oldDecoded)&&oldDecoded!.Rack==null,"legacy readers can skip85 by its TLV length");
        var next=TownServiceDelta.Copy(frame);next.Sequence=2;next.Rack!.Elapsed=.8f;
        var delta=TownServiceDelta.Create(frame,next);var expanded=TownServiceDelta.Expand(frame,delta);
        t.True(expanded?.Rack?.Elapsed==.8f&&frame.Rack.Elapsed==.425f,"cumulative delta retains immutable explicit phase");
        for(int cut=legacy.Length+1;cut<bytes.Length;cut++)t.True(!TownServiceCodec.TryRead(bytes,cut,out _),"partial85 payload never publishes a clock");
        byte[] malformed=(byte[])bytes.Clone();malformed[legacy.Length+2]=99;
        t.True(!TownServiceCodec.TryRead(malformed,malformed.Length,out _),"unknown85 discriminator rejected without native fallback reinterpretation");
        foreach(var broken in new[]{float.NaN,float.PositiveInfinity,-.01f,1f})
        {
            var bad=TownServiceDelta.Copy(frame);bad.Rack!.Elapsed=broken;bool rejected=false;
            try{TownServiceCodec.Write(bad);}catch(System.IO.InvalidDataException){rejected=true;}
            t.True(rejected,"unbounded or nonfinite revolution clock rejected");
        }
        var member=Frame(12,5,1);member.RackMember=new TownRackStamp{Rack=10,Page=1,Turn=0x01020304,Detached=true};
        byte[] memberGolden=Hex.Bytes("02 0a 00 01 00 04 03 02 01 01 00 00 80 3f");
        byte[] memberRaw=member.RackMember.Write(member.Module);t.Wire(memberGolden,memberRaw,memberRaw.Length,"independent85 member epoch and original-alpha layout");
        byte[] stamped=TownServiceCodec.Write(member);t.True(TownServiceCodec.TryRead(stamped,stamped.Length,out var stamp)&&stamp!.RackMember!.Same(member.RackMember),"held epoch is carried with its own module lane");
        var assembler=new TownServiceFragments();byte[]? complete=null;
        byte[][] fragments=ExtrasFragments.Encode(bytes,bytes.Length,65536+10,TownServiceCodec.MessageType,TownServiceCodec.FragmentType,TownServiceFrame.MaxBytes,compress:false);
        for(int i=fragments.Length-1;i>=0;i--)complete=assembler.Accept(2,fragments[i],fragments[i].Length,.1)??complete;
        t.True(complete!=null&&TownServiceCodec.TryRead(complete,complete.Length,out var reassembled)&&reassembled!.Rack!=null,"existing unchanged fragment lane carries85 atomically");
        t.True(TownRackState.Progress(0)==0&&Math.Abs(TownRackState.Progress(.425f)-.5f)<.00001f&&TownRackState.Progress(.85f)==1,"unwrapped owner curve retains an entire revolution");
    }
    private static void PublicCatalogLanes(Harness t)
    {
        t.Case("Indexed cabinet86 and independent public/private fragmented presentations");
        var catalog = Frame(10, 1, 1); catalog.TemplateAddress = "merchant.rack|";
        catalog.Rack = new TownRackState { Page = 0, From = 0, To = 1, Turn = 1, Elapsed = .425f };
        byte[] legacy = TownServiceCodec.Write(catalog);
        catalog.PublicCatalog = true; catalog.PublicClaim = 0x01020304; catalog.Rack.Cassette = true;
        byte[] current = TownServiceCodec.Write(catalog);
        var old = new List<byte>(); old.AddRange(new ArraySegment<byte>(current, 0, 6));
        var additive = new List<byte>(); var roller = new List<byte>();
        for (int at = 6; at < current.Length;)
        {
            int start = at, id = current[at++], count = current[at++];
            (id == 87 ? roller : id == 86 ? additive : old).AddRange(new ArraySegment<byte>(current, start, count + 2));
            at += count;
        }
        t.Wire(legacy, old.ToArray(), old.Count, "86 leaves complete historical78/85 payload unchanged");
        t.Wire(Hex.Bytes("56 06 01 03 04 03 02 01"), additive.ToArray(), additive.Count, "independent86 little endian golden vector");
        t.True(TownServiceCodec.TryRead(current, current.Length, out var decoded)
            && decoded!.PublicCatalog && decoded.PublicClaim == 0x01020304 && decoded.Rack!.Cassette,
            "public lane, authority claim and cassette mechanism survive decoder");
        t.Wire(Hex.Bytes("57 04 01 00 01 00"), roller.ToArray(), roller.Count, "independent87 idle roller golden vector");
        foreach (sbyte direction in new sbyte[] { -1, 1 })
        {
            catalog.Rack.ScrollDirection = direction; catalog.Rack.PageCount = 256;
            byte[] turn = TownServiceCodec.Write(catalog);
            t.Wire(Hex.Bytes(direction < 0 ? "57 04 01 FF 00 01" : "57 04 01 01 00 01"),
                Slice(turn, turn.Length - 6, 6), 6, "87 preserves signed scrolling direction and 256-page bound");
            t.True(TownServiceCodec.TryRead(turn, turn.Length, out var rolled) && rolled!.Rack!.ScrollDirection == direction
                && rolled.Rack.PageCount == 256, "reverse and forward wraparound preserve exact owner direction");
            var repeated = new byte[turn.Length + 6]; turn.CopyTo(repeated,0); Array.Copy(turn,turn.Length-6,repeated,turn.Length,6);
            t.True(!TownServiceCodec.TryRead(repeated,repeated.Length,out _),"duplicate roller clock rejected");
            foreach(byte bad in new byte[] {2,127,128,254})
            {byte[] corrupt=(byte[])turn.Clone();corrupt[corrupt.Length-3]=bad;
             t.True(!TownServiceCodec.TryRead(corrupt,corrupt.Length,out _),"invalid signed roller direction rejected");}
            byte[] zero=(byte[])turn.Clone();zero[zero.Length-2]=zero[zero.Length-1]=0;
            t.True(!TownServiceCodec.TryRead(zero,zero.Length,out _),"empty roller inventory count rejected");
        }
        catalog.Rack.ScrollDirection = 0; catalog.Rack.PageCount = 1;
        var duplicate = new byte[current.Length + additive.Count];
        current.CopyTo(duplicate, 0); additive.ToArray().CopyTo(duplicate, current.Length);
        t.True(!TownServiceCodec.TryRead(duplicate, duplicate.Length, out _), "duplicate lane metadata rejected");
        var privateFrame = Frame(10, 2, 64); var publicFrame = Frame(10, 2, 64);
        publicFrame.PublicCatalog = true; publicFrame.PublicClaim = 3;
        publicFrame.Pose[0] = 9f;
        byte[] privateRaw = TownServiceCodec.Write(privateFrame), publicRaw = TownServiceCodec.Write(publicFrame);
        foreach (bool compressed in new[] { false, true })
        {
            var receiver = new TownServiceFragments(); var arrived = new HashSet<bool>();
            byte[][] a = ExtrasFragments.Encode(privateRaw, privateRaw.Length, 131072 + 10,
                TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: compressed);
            byte[][] b = ExtrasFragments.Encode(publicRaw, publicRaw.Length, 196608 + 10,
                TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: compressed);
            for (int i = Math.Max(a.Length, b.Length) - 1; i >= 0; i--)
                foreach (byte[][] lane in new[] { a, b }) if (i < lane.Length)
                {
                    byte[]? complete = receiver.Accept(2, lane[i], lane[i].Length, .1);
                    if (complete != null && TownServiceCodec.TryRead(complete, complete.Length, out var frame)) arrived.Add(frame!.PublicCatalog);
                }
            t.Equal(2, arrived.Count, "same peer/module public and private fragments coexist under reverse interleaving");
        }
        var queue = new TownServiceSendQueue(65536); var assembly = new TownServiceFragments();
        queue.Enqueue(privateRaw, privateRaw.Length, privateFrame); queue.Enqueue(publicRaw, publicRaw.Length, publicFrame);
        var received = new HashSet<bool>();
        for (int i = 0; i < 300; i++)
        {
            byte[]? page = queue.Next(i * .05); if (page == null) continue;
            byte[]? complete = assembly.Accept(2, page, page.Length, i * .05); if (complete == null) continue;
            byte[][] frames = TownServiceCodec.TryReadBundle(complete, complete.Length, out var bundle) ? bundle! : new[] { complete };
            foreach (byte[] raw in frames) if (TownServiceCodec.TryRead(raw, raw.Length, out var frame)) received.Add(frame!.PublicCatalog);
        }
        t.Equal(2, received.Count, "shared global scheduler budget delivers both presentation lanes");
        foreach (float phase in new[] { .45f, .49f, .5f, .51f, .55f })
        {
            TownCassetteMotion.Sample(phase, out float depth, out float openness);
            t.True(depth >= .319f && openness <= .001f, "card identity changes only fully withdrawn behind closed opaque shutter");
        }
        TownCassetteMotion.Sample(0, out float firstDepth, out float firstOpen);
        TownCassetteMotion.Sample(1, out float lastDepth, out float lastOpen);
        t.True(firstDepth == 0 && lastDepth == 0 && firstOpen == 1 && lastOpen == 1,
            "owner and observer cassette clocks share both visible endpoint poses");
    }
    private static void NativeTemplateMetadata(Harness t)
    {
        t.Case("Original native metadata survives ordinary bundles and production fragments");
        TownServiceFrame frame = Frame(41, 7, 8); frame.Service = 3;
        frame.NativeTemplateBasisKey = 0x8877665544332211UL;
        byte[] sparse = TownServiceCodec.Write(frame);
        t.Wire(Hex.Bytes("69 09 01 11 22 33 44 55 66 77 88"),
            Slice(sparse, sparse.Length - 11, 11), 11, "additive TLV105 has its independent version and immutable basis identity");
        t.True(TownServiceCodec.TryRead(sparse, sparse.Length, out var decoded)
            && decoded!.NativeTemplateBasisKey == frame.NativeTemplateBasisKey && decoded.BaseSequence == 0,
            "native metadata is not a delta against a previous network image");
        TownServiceFrame retained = TownServiceDelta.Retain(decoded!);
        t.True(retained.NativeTemplateBasisKey == frame.NativeTemplateBasisKey,
            "queue tagging preserves the native basis record");
        var duplicate = new byte[sparse.Length + 11]; sparse.CopyTo(duplicate, 0);
        Array.Copy(sparse, sparse.Length - 11, duplicate, sparse.Length, 11);
        t.True(!TownServiceCodec.TryRead(duplicate, duplicate.Length, out _), "duplicate native metadata record is rejected");
        byte[] malformed = (byte[])sparse.Clone(); malformed[malformed.Length - 9] = 2;
        t.True(!TownServiceCodec.TryRead(malformed, malformed.Length, out _), "unknown native metadata version is rejected");
        malformed = (byte[])sparse.Clone(); Array.Clear(malformed, malformed.Length - 8, 8);
        t.True(!TownServiceCodec.TryRead(malformed, malformed.Length, out _), "zero native basis identity is rejected");
        TownServiceFrame second = TownServiceDelta.Retain(frame); second.Module = 42;
        byte[] bundle = TownServiceCodec.WriteBundle(new[] { sparse, TownServiceCodec.Write(second) });
        foreach (bool compressed in new[] { false, true })
        {
            byte[][] pages = ExtrasFragments.Encode(bundle, bundle.Length,
                65536UL + TownServiceFrame.BundleStream, TownServiceCodec.MessageType,
                TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: compressed);
            var receiver = new TownServiceFragments(); byte[]? complete = null;
            for (int i = pages.Length - 1; i >= 0; i--)
            {
                byte[]? received = receiver.Accept(2, pages[i], pages[i].Length, .1);
                if (i != 0) t.True(received == null, "fragmented native bundle remains atomic before its final page");
                complete = received ?? complete;
            }
            t.True(complete != null && TownServiceCodec.TryReadBundle(complete, complete.Length, out _),
                "actual fragmented transport reconstructs the complete original metadata bundle");
            TownServiceCodec.TryReadBundle(complete!, complete!.Length, out var parts);
            t.Equal(2, parts!.Length, "both independent native modules survive the bundle");
            foreach (byte[] part in parts)
                t.True(TownServiceCodec.TryRead(part, part.Length, out var member)
                    && member!.NativeTemplateBasisKey == frame.NativeTemplateBasisKey,
                    "fragmented bundled modules preserve original immutable prefab metadata");
        }
        var queue = new TownServiceSendQueue(65536);
        queue.Enqueue(sparse, sparse.Length, decoded!);
        TownServiceFrame revision = TownServiceDelta.Retain(frame); revision.Sequence++;
        byte[] latest = TownServiceCodec.Write(revision); queue.Enqueue(latest, latest.Length, revision);
        var assembler = new TownServiceFragments(); ulong arrived = 0;
        for (int i = 0; i < 150; i++)
        {
            byte[]? page = queue.Next(i * .05); if (page == null) continue;
            byte[]? received = assembler.Accept(2, page, page.Length, i * .05); if (received == null) continue;
            byte[][] parts = TownServiceCodec.TryReadBundle(received, received.Length, out var packed) ? packed! : new[] { received };
            foreach (byte[] part in parts) if (TownServiceCodec.TryRead(part, part.Length, out var member))
            {
                t.True(member!.NativeTemplateBasisKey == frame.NativeTemplateBasisKey && member.BaseSequence == 0,
                    "actual coalescing queue retains complete native metadata rather than an unusable network delta");
                arrived = Math.Max(arrived, member.Sequence);
            }
        }
        t.Equal(revision.Sequence, arrived, "coalescing delivers the newest complete native revision");
    }

    private static void ExactOriginalPool(Harness t)
    {
        t.Case("Exact native original pools have independent bounded additive110 vectors");
        var frame = Frame(1, 1, 1); var builder = new TownServiceCodec.OriginalValuePoolBuilder();
        byte[] original = TownServiceCodec.Write(frame);
        t.True(builder.TryAdd(original), "complete native module admitted");
        byte[] packet = builder.Write();
        // Independently specified grammar1: frame/value/scalar/string tables,
        // exact node map, ordinary original header and native node indices.
        byte[] expected = Hex.Bytes("31 52 56 47 03 13 6E AB 01 01 00 02 00 02 00 00 00 00 00 00 00 80 3F 00 00 0A 00 00 00 00 00 00 00 00 00 00 00 00 00 01 00 01 00 01 00 01 00 00 01 00 01 00 00 01 00 01 00 00 00 02 01 00 00 02 01 00 6A 00 31 52 56 47 03 13 4E 62 02 01 63 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 00 03 00 00 00 7B 00 00 00 00 FF FF 00 00 00 00 00 00 80 3F 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 00 00 00 01 01 00 00 00");
        t.Wire(expected, packet, expected.Length, "record110 retains stable scalar/string/value/node/header ordering");
        t.True(TownServiceCodec.TryReadBundle(expected, expected.Length, out var decoded), "independently specified original-pool vector decodes");
        t.Wire(original, decoded![0], original.Length, "original identity and every float survive the independent pool vector");
        for (int length = 0; length < expected.Length; length++)
            t.True(!TownServiceCodec.TryReadBundle(expected, length, out _), "every truncated additive110 vector remains inert");
        byte[][] fragments = ExtrasFragments.Encode(packet, packet.Length, 65536UL + TownServiceFrame.BundleStream,
            TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: true);
        var receiver = new TownServiceFragments(); byte[]? complete = null;
        for (int i = fragments.Length - 1; i >= 0; i--) complete = receiver.Accept(2, fragments[i], fragments[i].Length, .1) ?? complete;
        t.True(complete != null && TownServiceCodec.TryReadBundle(complete, complete.Length, out decoded), "real compressed/reordered fragment transport preserves the exact original pool");

        var first = new TownServiceCodec.OriginalValuePoolBuilder();
        for (ushort id = 1; id <= 24; id++) t.True(first.TryAdd(TownServiceCodec.Write(PromotionFrame(id, 1))), "all distinct exact originals fit the loss-test bounded pool");
        byte[] cold = first.Write();
        byte[][] lost = ExtrasFragments.Encode(cold, cold.Length, 65536UL + TownServiceFrame.UrgentBundleStream,
            TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: true);
        t.True(lost.Length > 2, "loss proof requires a genuine multi-datagram exact native picture");
        receiver = new TownServiceFragments();
        for (int i = 1; i < lost.Length; i++)
            t.True(receiver.Accept(2, lost[i], lost[i].Length, .2) == null, "a missing first page exposes no partial native originals or census");
        // A cumulative replacement/session change supersedes the incomplete old
        // picture. No original depends on a dictionary from that missing packet.
        var replacement = new TownServiceCodec.OriginalValuePoolBuilder(); var exact = new List<byte[]>();
        for (ushort id = 1; id <= 24; id++)
        {
            var next = PromotionFrame(id, 2); next.Session = 100;
            byte[] native = TownServiceCodec.Write(next); exact.Add(native);
            t.True(replacement.TryAdd(native), "new owner session remains a self-contained exact picture");
        }
        byte[] newer = replacement.Write();
        fragments = ExtrasFragments.Encode(newer, newer.Length, 196608UL + TownServiceFrame.UrgentBundleStream,
            TownServiceCodec.MessageType, TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: true);
        complete = null;
        for (int i = fragments.Length - 1; i >= 0; i--)
        {
            byte[]? arrived = receiver.Accept(2, fragments[i], fragments[i].Length, .3);
            if (i > 0) t.True(arrived == null, "reordered replacement remains atomic until all exact metadata arrives");
            complete = arrived ?? complete;
        }
        t.True(complete != null && TownServiceCodec.TryReadBundle(complete, complete.Length, out decoded), "a replacement picture recovers from packet loss without the old dictionary/session");
        if (complete != null && decoded != null)
            for (int i = 0; i < exact.Count; i++) t.Wire(exact[i], decoded[i], exact[i].Length, "replacement retains every original byte of the new owner session");
        t.True(receiver.Accept(2, lost[0], lost[0].Length, .4) == null, "a late old-session page cannot overwrite the complete replacement picture");

        var releaseQueue = new TownServiceSendQueue(0); receiver = new TownServiceFragments();
        var census = Frame(TownServiceFrame.ManifestModule, 1, 0); census.Template = 0; census.Structure = 0;
        census.Modules = new ushort[] { 1 }; census.TransactionActive = true;
        byte[] held = TownServiceCodec.Write(census); releaseQueue.Enqueue(held, held.Length, census);
        byte[] page = releaseQueue.Next(0)!;
        t.True(receiver.Accept(2, page, page.Length, 0) != null, "the transaction census is initially delivered");
        var released = TownServiceDelta.Retain(census); released.TransactionActive = false; released.Sequence = 2;
        byte[] cancelled = TownServiceCodec.Write(released); releaseQueue.Enqueue(cancelled, cancelled.Length, released);
        page = releaseQueue.Next(.051)!;
        t.True(page != null, "withdrawal with unchanged prepared modules does not wait five seconds for the heartbeat");
        if (page != null)
        {
            byte[]? release = receiver.Accept(2, page, page.Length, .051);
            t.True(release != null && TownServiceCodec.TryRead(release, release.Length, out var open) && !open!.TransactionActive,
                "a cancelled offered picture immediately releases only its exact NPC transaction");
        }
    }

    private static byte[] Slice(byte[] bytes,int at,int count)
    {var result=new byte[count];Array.Copy(bytes,at,result,0,count);return result;}
    private static TownServiceFrame Frame(ushort module, ulong sequence, int count = 64)
    {
        var frame = new TownServiceFrame { Service = 1, Session = 99, Module = module, Template = 3,
            Sequence = sequence, Structure = 123, Visible = true, Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f }, Nodes = new TownServiceNode[count] };
        for (int i = 0; i < frame.Nodes.Length; i++)
        {
            var node = new TownServiceNode { Binding = (uint)(i + 1) };
            node.Values.Add(TownServiceProperty.Transform, new TownServiceValue { Numbers = new[] { i * .13f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } });
            node.Values.Add(TownServiceProperty.Active, new TownServiceValue { Numbers = new[] { 1f } });
            if (i % 4 == 3)
            {
                node.Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Numbers = new float[40],
                    Text = new[] { "Original native item " + i + " — Äöü shield", "originalFont", "originalSprites" } });
                var material = new TownServiceValue { Numbers = new float[154], Text = new string[62] };
                material.Text[0] = "shader|native"; material.Text[1] = "GLOW";
                for (int p = 0; p < 30; p++) { material.Text[2 + p * 2] = "_Property" + p; material.Text[3 + p * 2] = string.Empty; }
                node.Values.Add(TownServiceProperty.TextMaterial, material);
            }
            frame.Nodes[i] = node;
        }
        return frame;
    }
}
