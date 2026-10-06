using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static IEnumerator NativeDelivery629()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear();
        NetPlayerActors.Peer = 10;
        Transform owner = Go("Native delivery owner").transform;
        Transform observer = Go("Native delivery observer").transform;
        Transform original = Source(owner);
        var sources = new List<Transform>();
        for (ushort i = 0; i < 34; i++)
        {
            Transform source = i == 0 ? original : Object.Instantiate(original.gameObject, owner, false).transform;
            source.Find("Name").GetComponent<TextMeshProUGUI>().text = "Owner original branch " + i + " <b>upgrade details</b>";
            source.Find("Price").GetComponent<Text>().text = (5 + i) + " gold";
            source.localPosition += Vector3.right * (i * .02f);
            sources.Add(source);
            string address = i == 0 ? "face.62901|" : i == 1 ? "enchant.highlight|" : "enchant.row|" + i;
            TownServiceMirror.RegisterTemplate(3, (ushort)(i + 1), source, address: address);
        }
        TownServiceMirror.BeginSession(3, 629, owner, owner);
        for (ushort i = 0; i < 34; i++)
        {
            string address = i == 0 ? "face.62901|" : i == 1 ? "enchant.highlight|" : "enchant.row|" + i;
            TownServiceMirror.RegisterModule((ushort)(i + 1), (ushort)(i + 1), sources[i], address: address);
            TownServiceMirror.SetPriority((ushort)(i + 1), true);
            sources[i].gameObject.SetActive(false);
        }
        TownServiceMirror.SetLocalTransactionActive(3, false);
        var scheduler = new ExtrasSendScheduler(0, 8, 9);
        var fragments = new TownServiceFragments();
        // Populate the existing independent stream queues with maximum, incompressible
        // cosmetic snapshots. Their contents are not played as town UI: the native town
        // route below still captures and decodes every real owner property normally.
        var random = new System.Random(629);
        foreach (string field in new[] { "_presence", "_animation", "_plumes", "_board", "_appearance", "_prompt", "_itemAppearance", "_mapTooltip" })
        {
            var queue = (ExtrasSendQueue)typeof(ExtrasSendScheduler).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scheduler)!;
            int limit = (int)typeof(ExtrasSendQueue).GetField("_snapshotLimit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
            byte type = (byte)typeof(ExtrasSendQueue).GetField("_payloadType", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
            byte[] bytes = new byte[limit]; random.NextBytes(bytes);
            bytes[0]=0x31;bytes[1]=0x52;bytes[2]=0x56;bytes[3]=0x47;bytes[4]=3;bytes[5]=type;
            queue.Enqueue(bytes, bytes.Length);
        }
        var samples = new List<TownServiceFrame>();
        var sizes = new List<int>();
        Action<byte[], int, object?> publish = (bytes, length, metadata) =>
        { samples.Add((TownServiceFrame)metadata!); sizes.Add(length); scheduler.Enqueue(bytes, length, identity: metadata); };
        Action capture = () => NativeSenderCapture629(publish);
        capture();
        Check(samples.Count==1&&samples[0].Module==TownServiceFrame.ManifestModule,
            "hidden never-presented originals do not consume first-visible capture or wire work");
        double clock=0;
        var visit=NativeVisitorReady629(scheduler,fragments,observer,3,value=>clock=value);
        while(visit.MoveNext())yield return visit.Current;
        samples.Clear();sizes.Clear();
        for(int i=0;i<6;i++)sources[i].gameObject.SetActive(true);
        SetNativeSenderActive629(true);TownServiceMirror.SetLocalTransactionActive(3,true);
        capture();
        double coldBegan=clock;
        TownServiceFrame census = samples.Find(x => x.Module == TownServiceFrame.ManifestModule)!;
        Check(census.Modules.Length == 34 && census.RequiredVisibleModules != null && census.RequiredVisibleModules.Length == 6,
            "actual 34-module capture keeps preparation membership but requires only current visible originals");
        Check(samples.Count == 7, "hidden never-presented originals do not consume first-visible capture or wire work");
        Check(TownServiceCodec.TryRead(TownServiceCodec.Write(census), TownServiceCodec.Write(census).Length, out TownServiceFrame? decodedCensus)
            && decodedCensus!.RequiredVisibleModules!.Length == 6,
            "visible original census survives the production codec");
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        double ready = -1; int events = 0, townPages = 0, totalBytes = 0;
        for (int turn = 0; turn < 40 && ready < 0; turn++)
        {
            clock += .050001;
            byte[]? batch = scheduler.NextBatch(clock);
            if (batch == null) continue;
            events++; totalBytes += batch.Length;
            Check(batch.Length <= PresentationBatch.MaxSize, "first-visible delivery keeps the existing 864-byte event cap");
            Check(scheduler.NextBatch(clock) == null, "first-visible delivery never catches up with extra events");
            byte[][] pages = PresentationBatch.TryRead(batch, batch.Length, out byte[][]? children) ? children! : new[] { batch };
            foreach (byte[] page in pages)
            {
                if (TownServiceFragments.Stream(page, page.Length) < 0) continue;
                townPages++;
                byte[]? packet = fragments.Accept(2, page, page.Length, clock); if (packet == null) continue;
                byte[][] members = TownServiceCodec.TryReadBundle(packet, packet.Length, out byte[][]? bundle) ? bundle! : new[] { packet };
                Receive(2, members);
            }
            TownServiceMirror.TickRemote(_ => observer);
            bool complete = true; for (ushort id = 1; id <= 6; id++) complete &= Remote(2, id) != null;
            if (complete) ready = clock-coldBegan;
            yield return null;
        }
        File.WriteAllText(Path.Combine(_output, "native-delivery629-cost.txt"),
            "Actual CaptureCore -> ExtrasSendScheduler -> bounded batch -> TownServiceFragments -> Receive -> TickRemote\n"
            + "34 declared original modules; 6 visible representative original uGUI/TMP branches (not frozen game prefabs).\n"
            + "Original per-module bytes=" + string.Join(",", sizes) + "; first-ready simulated seconds=" + ready
            + "; events=" + events + "; town pages=" + townPages + "; total wire bytes=" + totalBytes + "\n"
            + "Clock is lossless transport scheduling; engine playback/yield time is separate from headset/network latency.\n");
        Check(ready >= 0, "complete visible native picture arrives without a hidden-module repair wait");
        Check(ready <= .8001, "actual contended visible original picture has a bounded first-delivery cost");
        for (ushort id = 1; id <= 6; id++)
            Check(Remote(2, id)!.Root.Find("Name").GetComponent<TextMeshProUGUI>().text
                == sources[id - 1].Find("Name").GetComponent<TextMeshProUGUI>().text,
                "all visible native branch contents match the current owner at first readiness");
        for (int revision = 1; revision <= 3; revision++)
        {
            foreach (Transform source in sources)
                if (source.gameObject.activeInHierarchy)
                    source.Find("Name").GetComponent<TextMeshProUGUI>().text = "Current owner option revision " + revision;
            int beganSample = samples.Count; double began = clock; double warmReady = -1;
            capture();
            Check(samples.Count > beganSample && samples[beganSample].BaseSequence != 0,
                "a warmed original offer reuses the transmitted exact owner baseline");
            for (int turn = 0; turn < 120 && warmReady < 0; turn++)
            {
                clock += .050001;
                byte[]? batch = scheduler.NextBatch(clock); if (batch == null) { yield return null; continue; }
                Check(batch.Length <= PresentationBatch.MaxSize, "warmed original controls preserve the event cap");
                byte[][] pages = PresentationBatch.TryRead(batch, batch.Length, out byte[][]? children) ? children! : new[] { batch };
                foreach (byte[] page in pages)
                {
                    if (TownServiceFragments.Stream(page, page.Length) < 0) continue;
                    byte[]? packet = fragments.Accept(2, page, page.Length, clock); if (packet == null) continue;
                    byte[][] members = TownServiceCodec.TryReadBundle(packet, packet.Length, out byte[][]? bundle) ? bundle! : new[] { packet };
                    Receive(2, members);
                }
                TownServiceMirror.TickRemote(_ => observer);
                bool complete = true;
                for (ushort id = 1; id <= 6; id++)
                    complete &= Remote(2, id)!.Root.Find("Name").GetComponent<TextMeshProUGUI>().text
                        == sources[id - 1].Find("Name").GetComponent<TextMeshProUGUI>().text;
                if (complete) warmReady = clock - began;
                yield return null;
            }
            File.AppendAllText(Path.Combine(_output, "native-delivery629-cost.txt"), "Warm owner revision " + revision + "=" + warmReady
                + "s; delta bytes=" + sizes[beganSample] + "\n");
            Check(warmReady >= 0 && warmReady <= .8001, "warmed current original controls arrive without a periodic repair");
        }
        var merchant = NativeMerchantDelivery629(owner, observer);
        while (merchant.MoveNext()) yield return merchant.Current;
        NativeWarmPriorityFairness629();
        NativePublicationRecovery629(owner);
        NativeEncodingRecovery629(owner);
        NativeVisibleMounts629(owner);
        NativeManifestValidation629(census);
        NativePartitionShape629(owner);
        NativeCensusLifetime629(owner);

    }

    private static void SetNativeSenderActive629(bool active)
    {
        object lane=typeof(TownServiceMirror).GetField("PrivateLane",PrivateStatic)!.GetValue(null)!;
        lane.GetType().GetField("Active",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(lane,active);
    }
    private static void NativeSenderCapture629(Action<byte[],int,object?> publish)
    {
        // This one Unity process models two machines. The observer has no private
        // visit competing against the sender's lease; only the publisher pass does.
        SetNativeSenderActive629(true);
        try { typeof(TownServiceMirror).GetMethod("CaptureCore",PrivateStatic)!.Invoke(null,new object[]{publish,false}); }
        finally { SetNativeSenderActive629(false); }
    }
    private static IEnumerator NativeVisitorReady629(ExtrasSendScheduler scheduler,TownServiceFragments fragments,
        Transform observer,byte service,Action<double> remember)
    {
        double clock=0;
        for(int turn=0;turn<80&&!TownServiceMirror.RemoteSessions.ContainsKey(2);turn++)
        {
            clock+=.050001;byte[]? batch=scheduler.NextBatch(clock);
            if(batch!=null)
            {
                byte[][] pages=PresentationBatch.TryRead(batch,batch.Length,out byte[][]? children)?children!:new[]{batch};
                foreach(byte[] page in pages)
                {
                    if(TownServiceFragments.Stream(page,page.Length)<0)continue;
                    byte[]? packet=fragments.Accept(2,page,page.Length,clock);if(packet==null)continue;
                    Receive(2,TownServiceCodec.TryReadBundle(packet,packet.Length,out byte[][]? members)?members!:new[]{packet});
                }
            }
            yield return null;
        }
        Check(TownServiceMirror.RemoteSessions.ContainsKey(2),"the actual pre-offer visit manifest reaches the observer through bounded transport");
        float deadline=Time.unscaledTime+1f;
        while(TownServiceMirror.InteractionOwner(service)!=2&&Time.unscaledTime<deadline)yield return null;
        Check(TownServiceMirror.InteractionOwner(service)==2,
            "the actual existing visitor lease is ready before measuring an offered original picture");
        TownServiceMirror.TickRemote(_=>observer);remember(clock);
    }

    private static IEnumerator NativeMerchantDelivery629(Transform owner, Transform observer)
    {
        TownServiceMirror.Shutdown(); Baselines.Clear();
        Transform face = Source(owner), confirmation = Source(owner), hidden = Source(owner);
        face.Find("Name").GetComponent<TextMeshProUGUI>().text = "Exact merchant offered item";
        confirmation.Find("Name").GetComponent<TextMeshProUGUI>().text = "Sell this item for 10 gold?";
        confirmation.Find("Price").GetComponent<Text>().text = "Confirm / Cancel";
        TownServiceMirror.RegisterTemplate(1, 1, face, address: "item.62903|");
        TownServiceMirror.RegisterTemplate(1, 2, confirmation, address: "item.confirm.part.0|");
        TownServiceMirror.RegisterTemplate(1, 3, hidden, address: "merchant.tooltip|");
        TownServiceMirror.BeginSession(1, 639, owner, owner);
        TownServiceMirror.RegisterModule(1, 1, face, address: "item.62903|");
        TownServiceMirror.RegisterModule(2, 2, confirmation, address: "item.confirm.part.0|");
        TownServiceMirror.RegisterModule(3, 3, hidden, address: "merchant.tooltip|");
        for (ushort id = 1; id <= 3; id++) TownServiceMirror.SetPriority(id, true);
        face.gameObject.SetActive(false); confirmation.gameObject.SetActive(false); hidden.gameObject.SetActive(false);
        TownServiceMirror.SetLocalTransactionActive(1, false);
        var scheduler = new ExtrasSendScheduler(0, 8, 9);
        var fragments = new TownServiceFragments();
        var sent = new List<TownServiceFrame>();
        Action<byte[], int, object?> publish = (bytes, length, identity) =>
        { sent.Add((TownServiceFrame)identity!); scheduler.Enqueue(bytes, length, identity: identity); };
        Action capture = () => NativeSenderCapture629(publish);
        capture();
        double clock=0;
        var visit=NativeVisitorReady629(scheduler,fragments,observer,1,value=>clock=value);
        while(visit.MoveNext())yield return visit.Current;
        sent.Clear();face.gameObject.SetActive(true);confirmation.gameObject.SetActive(true);
        SetNativeSenderActive629(true);TownServiceMirror.SetLocalTransactionActive(1,true);
        capture();
        Check(sent.Count == 3 && !sent.Exists(x => x.Module == 3),
            "merchant first-visible publication excludes its inactive original tooltip");
        Check(sent.Find(x => x.Module == 2)!.BaseSequence == 0
            && sent.Find(x => x.Module == 2)!.NativeTemplateBasisKey == 0,
            "merchant first confirmation carries the complete current owner original");
        double ready = -1; int pages = 0;
        for (int revision = 0; revision < 3; revision++)
        {
            double began = clock; ready = -1;
            string expected = revision == 0 ? "Sell this item for 10 gold?" : "Buy replacement " + revision + " for 20 gold?";
            if (revision != 0)
            {
                confirmation.Find("Name").GetComponent<TextMeshProUGUI>().text = expected;
                face.Find("Name").GetComponent<TextMeshProUGUI>().text = "Replacement item " + revision;
                capture();
            }
            for (int turn = 0; turn < 40 && ready < 0; turn++)
            {
                clock += .050001;
                byte[]? batch = scheduler.NextBatch(clock);
                if (batch == null) { yield return null; continue; }
                Check(batch.Length <= PresentationBatch.MaxSize, "merchant confirmation delivery retains the event byte cap");
                byte[][] children = PresentationBatch.TryRead(batch, batch.Length, out byte[][]? members) ? members! : new[] { batch };
                foreach (byte[] page in children)
                {
                    if (TownServiceFragments.Stream(page, page.Length) < 0) continue;
                    pages++; byte[]? packet = fragments.Accept(2, page, page.Length, clock);
                    if (packet == null) continue;
                    Receive(2, TownServiceCodec.TryReadBundle(packet, packet.Length, out byte[][]? bundle) ? bundle! : new[] { packet });
                }
                TownServiceMirror.TickRemote(_ => observer);
                if (Remote(2, 1) != null && Remote(2, 2) != null
                    && Remote(2, 2)!.Root.Find("Name").GetComponent<TextMeshProUGUI>().text == expected) ready = clock - began;
                yield return null;
            }
            File.AppendAllText(Path.Combine(_output, "native-delivery629-cost.txt"),
                "Merchant representative original revision " + revision + "=" + ready + "s; cumulative pages=" + pages + "; face="+(Remote(2,1)!=null)+"; confirmation="+(Remote(2,2)!=null)+"; owner="+TownServiceMirror.InteractionOwner(1)+"\n");
            Check(ready >= 0 && ready <= .4001, "merchant original card and confirmation appear without a sparse-template repair wait");
        }
    }
    private static void NativeWarmPriorityFairness629()
    {
        var queue = new TownServiceLaneSendQueue(0);
        var warm = new TownServiceFrame { Service=3, Session=1, Sequence=2, BaseSequence=1, Module=7,
            Template=1, TemplateAddress="enchant.row|warm", Visible=true, HighPriority=true,
            Pose=new[]{0f,0f,0f,0f,0f,0f,1f,1f,1f,1f} };
        byte[] bytes = TownServiceCodec.Write(warm); queue.Enqueue(bytes, bytes.Length, warm);
        Check(queue.NextUrgent(0) != null,
            "a warm current original control can borrow an existing bounded urgent town turn");
        // Sustained warm requests still repay the lane's background share. Retain a
        // real native original in the background and decode its complete fragments.
        var background = new TownServiceFrame { Service=3, Session=1, Sequence=1, Module=8,
            Template=1, TemplateAddress="enchant.row|background", Visible=false,
            Pose=new[]{0f,0f,0f,0f,0f,0f,1f,1f,1f,1f} };
        bytes = TownServiceCodec.Write(background); queue.Enqueue(bytes, bytes.Length, background);
        var fragments = new TownServiceFragments(); bool repaid = false;
        for (int turn=1; turn<=30; turn++)
        {
            warm.Sequence++; bytes=TownServiceCodec.Write(warm); queue.Enqueue(bytes, bytes.Length, warm);
            byte[]? page=queue.NextUrgent(turn*.050001); if(page==null) continue;
            byte[]? packet=fragments.Accept(2,page,page.Length,turn*.050001); if(packet==null) continue;
            byte[][] members=TownServiceCodec.TryReadBundle(packet,packet.Length,out byte[][]? bundle)?bundle!:new[]{packet};
            foreach(byte[] member in members)
                if(TownServiceCodec.TryRead(member,member.Length,out TownServiceFrame? frame)&&frame!.Module==8) repaid=true;
        }
        Check(repaid, "sustained warm controls retain bounded background original recovery");
    }
    private static void NativeCensusLifetime629(Transform owner)
    {
        TownServiceMirror.Shutdown();
        Transform source=Source(owner);
        TownServiceMirror.RegisterTemplate(3,1,source,address:"enchant.row|lifecycle");
        TownServiceMirror.BeginSession(3,640,owner,owner);
        TownServiceMirror.RegisterModule(1,1,source,address:"enchant.row|lifecycle");
        var frames=new List<TownServiceFrame>();
        TownServiceMirror.Capture((bytes,length,identity)=>{if(identity is TownServiceFrame frame)frames.Add(frame);});
        var visible=frames.Find(x=>x.Module==TownServiceFrame.ManifestModule)!;
        TownServiceFrame retained=TownServiceDelta.Retain(visible);
        Check(retained.RequiredVisibleModules!=null&&retained.RequiredVisibleModules.Length==1
            &&!ReferenceEquals(retained.RequiredVisibleModules,visible.RequiredVisibleModules),
            "retained manifest owns its exact visible dependency census");
        TownServiceMirror.EndSession();frames.Clear();
        TownServiceMirror.Capture((bytes,length,identity)=>{if(identity is TownServiceFrame frame)frames.Add(frame);});
        var closed=frames.Find(x=>x.Module==TownServiceFrame.ManifestModule);
        Check(closed!=null&&!closed.Visible&&closed.Modules.Length==0&&closed.RequiredVisibleModules!.Length==0,
            "closed original session publishes an empty valid visible census immediately");
        TownServiceMirror.BeginSession(1,641,owner,owner);frames.Clear();
        TownServiceMirror.RegisterTemplate(1,1,source,address:"item.62903|");
        TownServiceMirror.RegisterModule(1,1,source,address:"item.62903|");
        TownServiceMirror.Capture((bytes,length,identity)=>{if(identity is TownServiceFrame frame)frames.Add(frame);});
        Check(frames.Find(x=>x.Module==TownServiceFrame.ManifestModule)!.RequiredVisibleModules==null,
            "changing native services cannot retain a preceding mage admission census");
    }

    private static void NativePublicationRecovery629(Transform owner)
    {
        TownServiceMirror.Shutdown();
        Transform source = Source(owner);
        TownServiceMirror.RegisterTemplate(3, 1, source, address: "enchant.row|recovery");
        TownServiceMirror.BeginSession(3, 630, owner, owner);
        TownServiceMirror.RegisterModule(41, 1, source, address: "enchant.row|recovery");
        Action<byte[], int, object?> rejected = (bytes, length, metadata) =>
        { if (((TownServiceFrame)metadata!).Module == 41) throw new IOException("Deliberate queue rejection"); };
        typeof(TownServiceMirror).GetMethod("CaptureCore", PrivateStatic)!.Invoke(null, new object[] { rejected, false });
        var modules = (IDictionary)typeof(TownServiceMirror).GetProperty("Local", PrivateStatic)!.GetValue(null)!;
        object module = modules[(ushort)41]!;
        FieldInfo baseline = module.GetType().GetField("Baseline", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(baseline.GetValue(module) == null,
            "rejected publication cannot invent an undelivered original baseline");
        module.GetType().GetField("RetryAfter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, 0f);
        TownServiceFrame? accepted = null;
        Action<byte[], int, object?> accept = (bytes, length, metadata) =>
        { if (((TownServiceFrame)metadata!).Module == 41) accepted = (TownServiceFrame)metadata!; };
        typeof(TownServiceMirror).GetMethod("CaptureCore", PrivateStatic)!.Invoke(null, new object[] { accept, false });
        Check(accepted != null && accepted.BaseSequence == 0 && accepted.NativeTemplateBasisKey == 0,
            "publication recovery sends a complete current original rather than a stranded delta");
    }
    private static void NativeEncodingRecovery629(Transform owner)
    {
        TownServiceMirror.Shutdown();
        Transform source=Source(owner);
        var labels=new List<Text>();
        for(int i=0;i<30;i++)
        {
            Text text=Rect("Live native long caption "+i,source,Vector2.zero,new Vector2(80,20)).gameObject.AddComponent<Text>();
            text.font=Resources.GetBuiltinResource<Font>("Arial.ttf");text.text="Original caption "+i+new string('x',2200);labels.Add(text);
        }
        TownServiceMirror.RegisterTemplate(3,1,source,address:"enchant.inventory|encoding-recovery");
        TownServiceMirror.BeginSession(3,642,owner,owner);
        TownServiceMirror.RegisterModule(42,1,source,address:"enchant.inventory|encoding-recovery");
        int sends=0;
        Action<byte[],int,object?> send=(bytes,length,identity)=>{if(identity is TownServiceFrame frame&&frame.Module==42)sends++;};
        typeof(TownServiceMirror).GetMethod("CaptureCore",PrivateStatic)!.Invoke(null,new object[]{send,false});
        var modules=(IDictionary)typeof(TownServiceMirror).GetProperty("Local",PrivateStatic)!.GetValue(null)!;
        object module=modules[(ushort)42]!;
        Check(sends==0&&module.GetType().GetField("Baseline",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(module)==null,
            "oversized original encoding leaves no unpublished delta dependency");
        foreach(Text text in labels)text.text="Current original short text";
        module.GetType().GetField("RetryAfter",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(module,0f);
        TownServiceFrame? accepted=null;
        send=(bytes,length,identity)=>{if(identity is TownServiceFrame frame&&frame.Module==42)accepted=frame;};
        typeof(TownServiceMirror).GetMethod("CaptureCore",PrivateStatic)!.Invoke(null,new object[]{send,false});
        Check(accepted!=null&&accepted.BaseSequence==0&&accepted.NativeTemplateBasisKey==0,
            "a valid original following encoding refusal recovers with a full exact keyframe");
    }

    private static void NativeVisibleMounts629(Transform owner)
    {
        TownServiceMirror.Shutdown();
        Transform parent = Source(owner); parent.GetComponent<CanvasGroup>().alpha = 0f;
        Transform child = Rect("Visible original ignoring parent alpha", parent, Vector2.zero, new Vector2(80, 40));
        child.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
        Image("Visible child ink", child, Vector2.zero, new Vector2(70, 30), Color.cyan);
        TownServiceMirror.RegisterTemplate(3, 1, parent, node => node == child, "enchant.row|parent");
        TownServiceMirror.RegisterTemplate(3, 2, child, address: "enchant.row|child");
        TownServiceMirror.BeginSession(3, 631, owner, owner);
        TownServiceMirror.RegisterModule(1, 1, parent, node => node == child, "enchant.row|parent");
        TownServiceMirror.RegisterModule(2, 2, child, address: "enchant.row|child");
        var frames = new List<TownServiceFrame>();
        TownServiceMirror.Capture((bytes, length, identity) => { if (identity is TownServiceFrame frame) frames.Add(frame); });
        TownServiceFrame manifest = frames.Find(frame => frame.Module == TownServiceFrame.ManifestModule)!;
        Check(manifest.RequiredVisibleModules!.Length == 2 && frames.Exists(frame => frame.Module == 1 && frame.HighPriority),
            "a visible ignore-parent-alpha original requires and prioritizes its exact hidden native mount");
        Check(frames.Find(frame => frame.Module == 2)!.ParentModule == 1,
            "required child original preserves the genuine published native parent");
        TownServiceMirror.Shutdown();
        Transform guide = Rect("Visitor local guide", owner, Vector2.zero, new Vector2(80, 40));
        Transform offered = Source(guide);
        TownServiceMirror.RegisterTemplate(3, 1, guide, node => node == offered, "merchant.zone|");
        TownServiceMirror.RegisterTemplate(3, 2, offered, address: "face.62902|");
        TownServiceMirror.BeginSession(3, 632, owner, owner);
        TownServiceMirror.RegisterModule(1, 1, guide, node => node == offered, "merchant.zone|");
        TownServiceMirror.RegisterModule(2, 2, offered, address: "face.62902|");
        frames.Clear(); TownServiceMirror.Capture((bytes, length, identity) => { if (identity is TownServiceFrame frame) frames.Add(frame); });
        manifest = frames.Find(frame => frame.Module == TownServiceFrame.ManifestModule)!;
        TownServiceFrame face = frames.Find(frame => frame.Module == 2)!;
        Check(manifest.RequiredVisibleModules!.Length == 1 && manifest.RequiredVisibleModules[0] == 2
            && face.ParentModule == TownServiceFrame.ManifestModule,
            "visitor-local guides neither gate nor mount the shared original offered face");
        Check(Vector3.Distance(owner.TransformPoint(new Vector3(face.Pose[0],face.Pose[1],face.Pose[2])), offered.position) < .00001f,
            "skipping the local-only guide mount preserves the exact owner world pose");
    }
    private static void NativeManifestValidation629(TownServiceFrame original)
    {
        TownServiceFrame census = TownServiceDelta.Retain(original);
        census.RequiredVisibleModules = new ushort[] { 4000 };
        bool invalid = false; try { TownServiceCodec.Write(census); } catch (InvalidDataException) { invalid = true; }
        Check(invalid, "a visible original census cannot name absent preparation modules");
        census.RequiredVisibleModules = new ushort[] { 1, 1 };
        invalid = false; try { TownServiceCodec.Write(census); } catch (InvalidDataException) { invalid = true; }
        Check(invalid, "a visible original census rejects duplicate dependency IDs");
        census.RequiredVisibleModules = Array.Empty<ushort>();
        byte[] empty = TownServiceCodec.Write(census);
        Check(TownServiceCodec.TryRead(empty, empty.Length, out TownServiceFrame? decoded) && decoded!.RequiredVisibleModules != null
            && decoded.RequiredVisibleModules.Length == 0, "an explicit empty visible census is not historical absence");
    }
    private static void NativePartitionShape629(Transform owner)
    {
        TownServiceMirror.Shutdown();
        Transform original = Source(owner);
        for (int i = 0; i < 60; i++)
        {
            Text caption = Rect("Native caption " + i, original, Vector2.zero, new Vector2(80, 20)).gameObject.AddComponent<Text>();
            caption.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); caption.text = "Original caption " + i + new string('x', 1300);
        }
        int originalNodes = 1 + original.GetComponentsInChildren<Transform>(true).Length - 1;
        Check(originalNodes < 128, "native partition counterexample lies below the historical node-only limit");
        var parts = new List<GloomhavenVR.WorldUI.LazyTemplateProbe.Part>();
        MethodInfo partition = typeof(GloomhavenVR.WorldUI.LazyTemplateProbe).GetMethod("Partition", PrivateStatic)!;
        partition.Invoke(null, new object[] { original, string.Empty, parts });
        Check(parts.Count > 1, "real original text/material partitions split before bounded native capture can fail");
        var paths = new List<string>(); var structures = new List<uint>(); var bindings = new List<uint[]>(); int largest = 0;
        foreach (var part in parts)
        {
            paths.Add(part.Path);
            using var binding = new TownServiceBinding(part.Original, part.Excluded.Contains);
            structures.Add(binding.Structure);bindings.Add((uint[])binding.Bindings.Clone());
            TownServiceFrame frame = NativeFrame623(part.Original, 1, 1, "enchant.row|");
            frame.Structure = binding.Structure; frame.Nodes = binding.Read(TownServiceMirror.Assets, includeInactiveGraphics: true);
            int bytes = TownServiceCodec.Write(frame).Length; largest = Math.Max(largest, bytes);
            Check(bytes <= 48000, "each genuine original partition retains complete metadata within the native wire bound");
        }
        foreach (Text caption in original.GetComponentsInChildren<Text>(true))
            caption.text = "Lokalisierte Beschreibung " + caption.name + new string('ä', 300);
        original.Find("Name").GetComponent<TextMeshProUGUI>().ForceMeshUpdate();
        var translated = new List<GloomhavenVR.WorldUI.LazyTemplateProbe.Part>();
        partition.Invoke(null, new object[] { original, string.Empty, translated });
        Check(translated.Count == parts.Count, "localized native captions and warmed fonts retain identical partition membership");
        for (int i = 0; i < paths.Count; i++)
        {
            Check(paths[i] == translated[i].Path,
                "localized native captions retain identical original partition addresses");
            using var binding=new TownServiceBinding(translated[i].Original,translated[i].Excluded.Contains);
            Check(binding.Structure==structures[i]&&binding.Bindings.Length==bindings[i].Length,
                "localized native captions and font warm-up retain exact original partition topology");
            for(int j=0;j<bindings[i].Length;j++)Check(binding.Bindings[j]==bindings[i][j],
                "localized native captions preserve every original stable binding identity");
        }
        File.WriteAllText(Path.Combine(_output, "native-partition629-cost.txt"), "Original nodes=" + originalNodes
            + "; parts=" + parts.Count + "; largest complete original=" + largest + " bytes\n");
    }

}
