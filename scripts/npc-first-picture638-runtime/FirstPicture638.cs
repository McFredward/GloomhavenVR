using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using TMPro;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static void FirstPictureLifecycle638()
    {
        TownServiceMirror.Shutdown(); GloomhavenVR.WorldUI.TownServiceSync.Reset();
        var owner = Go("638 actual publisher lifecycle owner").transform;
        var canvas = owner.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = _camera;
        var source = NativeRow632(owner, "Published original enhancement row");
        // Original prefab rendering is exported from the game. Only the native
        // game controller's pool-membership adapter is a fixture boundary here;
        // actual Publish/Visible/Tick retirement methods remain production.
        source.gameObject.AddComponent<GloomhavenVR.WorldUI.UINewEnhancementShopSlot>();
        var surface = new GloomhavenVR.WorldUI.TownServiceSurface { Id = 10 };
        surface.Panel.Target = source;
        GloomhavenVR.WorldUI.TownServiceSync.UseProductionPublish = true;
        GloomhavenVR.WorldUI.TownServicePresentation.Active = true;
        GloomhavenVR.WorldUI.TownServicePresentation.Service = 3;
        GloomhavenVR.WorldUI.TownServicePresentation.Session = 6381;
        GloomhavenVR.WorldUI.TownServicePresentation.Ritual = new GloomhavenVR.WorldUI.TownServiceRitual();
        GloomhavenVR.WorldUI.TownServicePresentation.LocalSurfaces.Clear();
        GloomhavenVR.WorldUI.TownServicePresentation.LocalSurfaces.Add(surface);
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner, owner);
        ushort id = GloomhavenVR.WorldUI.TownServiceSync.ModuleId(source);
        var first = Capture();
        var actual = first.Select(bytes => { TownServiceCodec.TryRead(bytes, bytes.Length, out var frame); return frame!; }).First(x => x.Module == id);
        Check(actual.BaseSequence == 0, "published visible dynamic native row owns an actual complete original baseline");
        Check(TownServiceMirror.TryExpandNativeTemplateState(actual,out TownServiceFrame original),
            "initial complete native original validates before becoming a reveal dependency");
        source.gameObject.SetActive(false);
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner, owner);
        var modules = (IDictionary)typeof(TownServiceMirror).GetProperty("Local", PrivateStatic)!.GetValue(null)!;
        Check(modules.Contains(id), "hidden published dynamic original retains its registered module and named baseline");
        Check(!TownServiceDelivery.HasRetired(false, false, actual.Service, actual.Session),
            "temporary native visibility cannot retire an immutable source used by in-flight originals");
        var hidden = Capture();
        var hiddenManifest = hidden.Select(bytes => { TownServiceCodec.TryRead(bytes, bytes.Length, out var frame); return frame!; })
            .FirstOrDefault(x => x.Module == TownServiceFrame.ManifestModule);
        if (hiddenManifest != null) Check(Array.BinarySearch(hiddenManifest.Modules, id) >= 0,
            "hidden original remains registered in the actual production census");
        source.gameObject.SetActive(true);
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner, owner);
        Check(GloomhavenVR.WorldUI.TownServiceSync.ModuleId(source) == id,
            "reveal reuses stable original identity without retiring and recreating its baseline");
        var visible = Capture();
        var restored = visible.Select(bytes => { TownServiceCodec.TryRead(bytes, bytes.Length, out var frame); return frame!; }).First(x => x.Module == id);
        Check(restored.Service==original.Service&&restored.Session==original.Session&&restored.Module==original.Module
            &&restored.Template==original.Template&&restored.TemplateAddress==original.TemplateAddress&&restored.Structure==original.Structure
            &&restored.Visible&&restored.Sequence>original.Sequence,
            "reveal retains the exact original identity and publishes a newer visible state");
        TownServiceFrame? complete;
        if(restored.BaseSequence==0)
        {
            Check(TownServiceMirror.TryExpandNativeTemplateState(restored,out TownServiceFrame full),
                "complete reveal validates its original native basis");complete=full;
        }
        else
        {
            Check(restored.BaseSequence==original.Sequence&&TownServiceDelta.Expand(null,restored)==null,
                "cumulative reveal requires the exact captured original dependency");
            complete=TownServiceDelta.Expand(original,restored);
        }
        Check(complete!=null,"reveal resolves to a complete current original through production expansion");
        object local=modules[id]!;
        var last=(TownServiceFrame)local.GetType().GetField("Last",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.GetValue(local)!;
        AssertNativeEqual623(last,complete!);
        Check(complete!.Visible==last.Visible&&complete.ParentModule==last.ParentModule&&complete.ParentBinding==last.ParentBinding
            &&complete.ParentAlpha==last.ParentAlpha&&complete.HasCanvasFrame==last.HasCanvasFrame
            &&complete.CanvasSortingOrder==last.CanvasSortingOrder&&complete.CanvasSortingLayer==last.CanvasSortingLayer
            &&complete.Pose.SequenceEqual(last.Pose)&&complete.CanvasPose.SequenceEqual(last.CanvasPose)
            &&complete.CanvasRect.SequenceEqual(last.CanvasRect)&&complete.CanvasSettings.SequenceEqual(last.CanvasSettings),
            "expanded reveal retains exact owner visibility, pose, parent and canvas geometry");
        Receive(2,first);Receive(2,hidden);Receive(2,visible);
        var received=(Dictionary<int,Dictionary<ushort,TownServiceFrame>>)typeof(TownServiceMirror).GetField("ReceivedBaselines",PrivateStatic)!.GetValue(null)!;
        var pending=(Dictionary<int,Dictionary<ushort,TownServiceFrame>>)typeof(TownServiceMirror).GetField("Pending",PrivateStatic)!.GetValue(null)!;
        Check(received.TryGetValue(2,out var bases)&&bases.TryGetValue(id,out var dependency)
            &&dependency.Sequence==(restored.BaseSequence==0?restored.Sequence:original.Sequence)
            &&pending.TryGetValue(2,out var current)&&current.ContainsKey(id),
            "actual ordered receiver retains the exact complete dependency and current reveal");
        TownServiceFrame? painted=TownServiceDelta.Expand(received[2][id],pending[2][id]);
        Check(painted!=null&&painted.Visible,"actual receiver reconstructs the visible reveal without another baseline");
        AssertNativeEqual623(last,painted!);
        File.WriteAllText(Path.Combine(_output,"first-picture638-lifecycle.txt"),
            "Actual production PublishNative -> TickCore removal/prune -> CaptureCore\n"
            + "native prefab nodes=26; stable original module=" + id + "; hidden retained=true; reveal baseline=" + restored.BaseSequence + "\n");
        GloomhavenVR.WorldUI.TownServiceSync.UseProductionPublish = false;
        GloomhavenVR.WorldUI.TownServicePresentation.LocalSurfaces.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.Reset(); TownServiceMirror.Shutdown();
    }
    private static IEnumerator FirstPicture638(bool delayedTemplate = false)
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 10;
        var owner = Go("638 current native owner").transform;
        var observer = Go("638 cold native observer").transform;
        var canvas = owner.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = _camera;
        var sources = new List<Transform>();
        for (ushort id = 1; id <= 44; id++)
        {
            Transform source = NativeRow632(owner, "Exact owner native enhancement " + id);
            if (id > 14)
            {
                var text = source.GetComponentsInChildren<TMP_Text>(true).First(x => x.name == "Name").transform;
                text.SetParent(owner, false); Object.DestroyImmediate(source.gameObject); source = text;
            }
            string address = id == 2 ? "enchant.inventory|" : "enchant.row|native638." + id;
            sources.Add(source);
            // Deliberately keep only the native template bank warm, exactly as632.
            // No ReceivedBaseline or final remote GameObject is constructed here.
            var original = Object.Instantiate(source.gameObject, observer, false).transform;
            foreach (var text in original.GetComponentsInChildren<TMP_Text>(true)) text.text = "different observer default";
            TownServiceMirror.RegisterTemplate(3, id, original, address: address);
        }
        TownServiceMirror.BeginSession(3, 638, owner, owner);
        for (ushort id = 1; id <= 44; id++)
        {
            string address = id == 2 ? "enchant.inventory|" : "enchant.row|native638." + id;
            TownServiceMirror.RegisterModule(id, id, sources[id - 1], address: address);
            TownServiceMirror.SetPriority(id, true);
        }
        var sender = new ExtrasSendScheduler(0, 3, 4); var fragments = new TownServiceFragments();
        var sent = new List<TownServiceFrame>(); var motion = new Queue<byte[]>();
        Action<byte[], int, object?> publish = (bytes, length, identity) => {
            if (identity is TownServiceFrame original) { sent.Add(original); sender.Enqueue(bytes, length, identity: identity); }
            else { motion.Enqueue(bytes); }
        };
        var templates = (Dictionary<string, GameObject>)typeof(TownServiceMirror).GetField("Templates", PrivateStatic)!.GetValue(null)!;
        string lateKey = (string)typeof(TownServiceMirror).GetMethod("TemplateKey", PrivateStatic)!
            .Invoke(null, new object[] { (byte)3, (ushort)2, "enchant.inventory|" })!;
        GameObject lateOriginal = templates[lateKey];
        bool observerPrepared = !delayedTemplate, withheld = false;
        Func<byte, ushort, string, bool>? previousResolver = TownServiceMirror.ResolveTemplate;
        // One Unity process models two machines. Swap only the template-bank
        // availability boundary: the owner already owns the native template;
        // the observer's genuine original becomes available asynchronously.
        TownServiceMirror.ResolveTemplate = (service, template, address) => {
            if (address != "enchant.inventory|") return previousResolver?.Invoke(service, template, address) ?? false;
            if (!observerPrepared) return false;
            templates[lateKey] = lateOriginal; return true;
        };
        void CaptureFast() {
            if (withheld) templates[lateKey] = lateOriginal;
            SetNativeSenderActive629(true);
            try { typeof(TownServiceMirror).GetMethod("CaptureCore", PrivateStatic)!.Invoke(null, new object[] {publish, true}); }
            finally {
                SetNativeSenderActive629(false);
                if (withheld && !observerPrepared) templates.Remove(lateKey);
            }
        }
        // The owner has already sampled and completed a real prior visit. A newly
        // joined/reconstructed observer has no old complete dependency. Sender
        // completion is not a receiver acknowledgement; packet loss has the same
        // dependency shape. The actual production clock and completion callback
        // establish NextBaseline, rather than assigning a ready test object.
        TownServiceMirror.SetLocalTransactionActive(3, false); CaptureFast();
        double clock = 0; int warmEvents = 0;
        for (int step = 0; step < 120; step++) {
            clock += .050001; var packet = sender.NextBatch(clock);
            if (packet != null) { Check(packet.Length <= PresentationBatch.MaxSize, "638 prior real delivery preserves datagram cap"); warmEvents++; }
        }
        Check(sent.Count >= 45 && sent.Any(x => x.Module == 2 && x.BaseSequence == 0), "638 warm native owner sampled the actual inventory complete baseline");
        var oldInventory = sent.Last(x => x.Module == 2 && x.BaseSequence == 0);
        sent.Clear(); motion.Clear();
        // Current visibility/content changes while the old owner's baseline is
        // still within its real repair interval. The cold observer sees its first
        // census, then current cumulative updates through the real avatar queue.
        sources[1].GetComponentsInChildren<TMP_Text>(true).First().text = "Current offered upgrade options";
        SetNativeSenderActive629(true); TownServiceMirror.SetLocalTransactionActive(3, true); CaptureFast();
        Check(sent.Any(x => x.Module == TownServiceFrame.ManifestModule && x.TransactionActive), "638 genuine transaction edge is captured in the production manifest");
        if (delayedTemplate) { withheld = true; templates.Remove(lateKey); }
        var receiver = new NetAvatarDriver(); TownServiceMirror.SharedFrameForRemote = _ => observer;
        float began = Time.unscaledTime, nextCapture = began, ready = -1;
        int events = 0, members = 0; float firstAssembled = -1; double beganSend = clock, readySend = -1;
        float blockedAt = -1, preparedAt = -1; ulong heldOriginalSequence = 0;
        var deferredOriginals = (IDictionary)typeof(TownServiceMirror).GetField("UnpreparedNativeTemplates", PrivateStatic)!.GetValue(null)!;
        for (int step = 0; step < 240 && ready < 0; step++)
        {
            float now = Time.unscaledTime;
            if (now >= nextCapture) { nextCapture = now + 1f / 15f; CaptureFast(); }
            clock += .050001; FillOtherQueues632(sender, false);
            byte[]? batch = sender.NextBatch(clock);
            if (batch != null) {
                events++; Check(batch.Length <= PresentationBatch.MaxSize, "638 cold observer keeps864-byte event cap");
                foreach (var page in PresentationBatch.TryRead(batch, batch.Length, out var pages) ? pages! : new[] {batch}) {
                    if (TownServiceFragments.Stream(page, page.Length) < 0) continue;
                    var packet = fragments.Accept(2, page, page.Length, clock); if (packet == null) continue;
                    if (firstAssembled < 0) firstAssembled = now - began;
                    foreach (var child in TownServiceCodec.TryReadBundle(packet, packet.Length, out var children) ? children! : new[] {packet}) {
                        Check(receiver.FixtureQueue638(2, child), "638 actual avatar receive queue accepts original member"); members++;
                    }
                }
            }
            while (motion.Count != 0) Check(receiver.FixtureQueueMotion638(2, motion.Dequeue()), "638 independent numeric packets use actual motion queue");
            receiver.FixtureApply638();
            if (delayedTemplate && blockedAt < 0 && (deferredOriginals.Count > 0
                || GloomhavenVR.Core.VRLog.Messages.Any(x => x.Contains("blocker=native-template module=2")))) {
                blockedAt = now;
                heldOriginalSequence = sent.Last(x => x.Module == 2 && x.BaseSequence == 0).Sequence;
                Check(Remote(2, 2)?.Root.gameObject.activeInHierarchy != true,
                    "cold genuine native template holds the assembled current original without a substitute");
            }
            if (delayedTemplate && blockedAt >= 0 && !observerPrepared && now - blockedAt >= .15f) {
                preparedAt = now; observerPrepared = true;
                // ResolveTemplate registers the original on its next genuine
                // production retry; the fixture never Receive()s a new repair.
            }
            bool complete = true;
            for (ushort id = 1; id <= 44; id++) complete &= Remote(2, id)?.Root.gameObject.activeInHierarchy == true;
            if (complete) { ready = now - began; readySend = clock - beganSend; }
            yield return new WaitForSecondsRealtime(.05f);
        }
        var pending = (Dictionary<int, Dictionary<ushort, TownServiceFrame>>)typeof(TownServiceMirror).GetField("Pending", PrivateStatic)!.GetValue(null)!;
        var baselines = (Dictionary<int, Dictionary<ushort, TownServiceFrame>>)typeof(TownServiceMirror).GetField("ReceivedBaselines", PrivateStatic)!.GetValue(null)!;
        string inventory = pending.TryGetValue(2, out var part) && part.TryGetValue(2, out var frame)
            ? "seq=" + frame.Sequence + ",base=" + frame.BaseSequence : "absent";
        string baseline = baselines.TryGetValue(2, out var bases) && bases.TryGetValue(2, out var full) ? full.Sequence.ToString() : "absent";
        TownServiceMirror.ResolveTemplate = previousResolver; templates[lateKey] = lateOriginal;
        File.WriteAllText(Path.Combine(_output, delayedTemplate ? "first-picture638-template.txt" : "first-picture638-dependency.txt"),
            "Source route: CaptureCore(fast:true) -> actual ExtrasSendScheduler -> fragments -> original pool -> actual QueueTownService -> actual ApplyTownServices -> TickRemote\n"
            + "Warm events=" + warmEvents + "; old owner inventory baseline=" + oldInventory.Sequence
            + "; first assembled=" + firstAssembled + "s; first complete active44-original picture=" + ready + "s; simulatedsend=" + readySend + "s; events=" + events + "; members=" + members
            + "; current inventory=" + inventory + "; observer baseline=" + baseline
            + "; delayedTemplate=" + delayedTemplate + "; blockedAt=" + (blockedAt - began)
            + "; preparedAt=" + (preparedAt - began) + "; admissionAfterPreparation=" + (preparedAt < 0 ? -1 : began + ready - preparedAt)
            + "; heldOriginalSequence=" + heldOriginalSequence + "\n"
            + string.Join("\n", GloomhavenVR.Core.VRLog.Messages) + "\n");
        Check(ready >= 0 && readySend <= 1.0001, "cold observer gets all named original dependencies within1s");
        if (delayedTemplate) {
            Check(blockedAt >= 0 && preparedAt >= blockedAt && began + ready - preparedAt <= .3f,
                "retained current picture admits within300ms of genuine observer-template readiness");
            Check(baselines.TryGetValue(2, out var retainedBases) && retainedBases.TryGetValue(2, out var sameOriginal)
                && sameOriginal.Sequence == heldOriginalSequence,
                "delayed original preparation replays the same complete source without a periodic network repair");
        } else Check(GloomhavenVR.Core.VRLog.Messages.Any(x => x.Contains("Native enhancement picture admitted:") && x.Contains("age=0.000s")),
            "assembled complete current native picture incurs no named-baseline repair wait");
    }
}
