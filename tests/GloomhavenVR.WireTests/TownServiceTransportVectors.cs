using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownServiceTransportVectors
{
    internal static void Run(Harness t)
    {
        RackClocks(t);
        PublicCatalogLanes(t);
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
    private static void LateGameCatalog(Harness t, bool contention, int count = 673 * 3 + 64)
    {
        t.Case("Native stock/owned card modules="+count+" contention=" + contention);
        t.True(TownServiceFrame.MaxModules >= count, "late catalog includes body, original row, face and overhead without truncation");
        var sender = new ExtrasSendScheduler(65536, NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
        var receiver = new TownServiceFragments();
        var missing = new HashSet<ushort>();
        var ids = new ushort[count];
        int initialPages = 0, initialWireBytes = 0;
        for (ushort id = 1; id <= count; id++)
        {
            ids[id - 1] = id; missing.Add(id);
            var sample = Frame(id, 1, id <= 8 ? 128 : 16);
            byte[] bytes = TownServiceCodec.Write(sample); sender.Enqueue(bytes, bytes.Length, identity: sample);
            byte[][] encoded=ExtrasFragments.Encode(bytes,bytes.Length,65536UL+id,TownServiceCodec.MessageType,
                TownServiceCodec.FragmentType,TownServiceFrame.MaxBytes,compress:true);
            initialPages+=encoded.Length;foreach(byte[] page in encoded)initialWireBytes+=page.Length;
        }
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
        t.True(completeAt>=0&&completeAt<(contention?400:count<1000?10:30),"cold catalog remains within measured compression budget");
        t.True(warmHeld>5&&warmMaxDelay<(contention?5:.5),"held motion stays live after cold catalog completes");
        t.Equal(0,missing.Count,"every late-game original face/body/quantity module completes");
        Console.WriteLine("TOWN_LATE modules="+count+" contention="+contention+" initialPages="+initialPages+" individualBytes="+initialWireBytes+" deliveredBytes="+deliveredBytes+" firstHeld="+firstHeld.ToString("F3")+" firstStock="+firstOrdinary.ToString("F3")+" complete="+completeAt.ToString("F3")+" warmHeldMaxDelay="+warmMaxDelay.ToString("F3"));
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
        for(ushort id=1;id<=20;id++){var baseline=Frame(id,1,16);byte[] raw=TownServiceCodec.Write(baseline);queue.Enqueue(raw,raw.Length,baseline);}
        byte[] first=queue.Next(0)!;t.True(receiver.Accept(2,first,first.Length,0)==null,"cold bundle is still in flight");
        TownServiceFrame original=Frame(2,1,16), current=Frame(2,2,16);current.Pose[0]=.4f;
        TownServiceFrame moving=TownServiceDelta.Create(original,current);moving.HighPriority=true;
        byte[] bytes=TownServiceCodec.Write(moving);queue.Enqueue(bytes,bytes.Length,moving);
        bool baselineArrived=false,poseArrived=false,bundleArrived=false;
        for(int i=1;i<20&&!poseArrived;i++)
        {
            byte[]? page=queue.Next(i*.051);if(page==null)continue;
            byte[]? result=receiver.Accept(2,page,page.Length,i*.051);if(result==null)continue;
            if(TownServiceCodec.TryReadBundle(result,result.Length,out _)){bundleArrived=true;continue;}
            if(TownServiceCodec.TryRead(result,result.Length,out TownServiceFrame? frame)&&frame!.Module==2)
            {if(frame.BaseSequence==0)baselineArrived=true;else poseArrived=true;}
        }
        t.True(baselineArrived&&poseArrived&&!bundleArrived,"urgent card renders with exact baseline before cold bundle completes");
    }
    private static void BundleBounds(Harness t)
    {
        t.Case("Lossless town bundle bounds and CPU measurement");
        byte[] legacy=Hex.Bytes("31 52 56 47 03 13 4e 61 01 01 63 00 00 00 07 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 04 00 03 00 00 00 7b 00 00 00 00 ff ff 00 00 00 00 00 00 80 3f 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3f 00 00 80 3f 00 00 80 3f 00 00 80 3f 00 00 00 00 00");
        t.True(TownServiceCodec.TryRead(legacy,legacy.Length,out TownServiceFrame? legacyFrame)&&legacyFrame!.Module==4&&legacyFrame.Sequence==7,"independent legacy record78 v1 golden still decodes");
        var frames=new List<byte[]>();
        for(ushort id=1;id<=16;id++)frames.Add(TownServiceCodec.Write(Frame(id,1,16)));
        byte[] packet=TownServiceCodec.WriteBundle(frames);
        t.True(TownServiceCodec.TryReadBundle(packet,packet.Length,out byte[][]? children),"bounded bundle parses");
        for(int at=6;at<packet.Length;){t.Equal(84,(int)packet[at++],"additive bundle record84");int length=packet[at++];t.True(length>0&&at+length<=packet.Length,"ordinary bounded TLV framing");at+=length;}
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
