using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI
{
    internal static partial class LazyTemplateProbe
    {
        internal static Transform FreezeOriginal646(Transform source, string key)
        {
            var entry = new Entry { Original = source };
            Freeze(key, entry); Entries.Add(key, entry); WarmEnhancementBasis(key);
            if (!Resolve(3, 1, key + "|") || entry.Parts.Count != 1)
                throw new InvalidOperationException("Actual native Freeze/Warm/Resolve must prepare exactly one bounded source partition");
            return entry.Parts[0].Original;
        }
    }
}

public static partial class MirrorProgram
{
    private static void RevisionDependencies646(TownServiceFrame sample)
    {
        TownServiceFrame older = TownServiceDelta.Copy(sample); older.Sequence = 100; older.BaseSequence = 0;
        TownServiceFrame changed = TownServiceDelta.Copy(older); changed.Sequence = 101; changed.Pose[0] += .01f;
        TownServiceFrame oldDelta = TownServiceDelta.Create(older, changed);
        TownServiceFrame replacement = TownServiceDelta.Copy(changed); replacement.Sequence = 102;
        TownServiceFrame future = TownServiceDelta.Copy(changed); future.Sequence = 103; future.Pose[0] += .01f;
        TownServiceFrame futureDelta = TownServiceDelta.Create(older, future);
        var queue = new ExtrasSendQueue(9, TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
            preserveFirst: true, snapshotLimit: TownServiceFrame.MaxBytes);
        void Enqueue(TownServiceFrame frame) { byte[] bytes = TownServiceCodec.Write(frame); queue.Enqueue(bytes, bytes.Length, frame); }
        Enqueue(older); Enqueue(oldDelta); Enqueue(futureDelta); queue.SupersedeTownOriginal(replacement); Enqueue(replacement);
        Check(queue.HasTownDelta(older.Sequence), "genuinely newer named-baseline delta survives an older complete replacement");
        var originals = new List<TownServiceFrame>();
        while (queue.TryTakePending(out byte[]? bytes, out object? identity)) originals.Add((TownServiceFrame)identity!);
        Check(originals.Any(x => x.Sequence == 100 && x.BaseSequence == 0), "exact older full dependency remains for the genuinely newer delta");
        Check(originals.Any(x => x.Sequence == 103 && x.BaseSequence == 100), "genuinely newer delta bytes and baseline affinity are retained");
        Check(!originals.Any(x => x.Sequence == 101), "strictly older cumulative revision alone is superseded by complete current state");
        Enqueue(older); Enqueue(oldDelta); Enqueue(futureDelta);
        TownServiceFrame latest = TownServiceDelta.Copy(replacement); latest.Sequence = 104;
        queue.SupersedeTownOriginal(latest); Enqueue(latest);
        Check(!queue.HasTownDelta(older.Sequence), "all strictly older cumulative revisions leave once actually newer complete state replaces them");
        Check(queue.TryTakePending(out _, out object? remaining) && ((TownServiceFrame)remaining!).Sequence == 104,
            "latest complete current original is first deliverable after supersession");
    }

    private static IEnumerator AdmissionCensus646()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 10;
        Transform owner = Go("646 actual source").transform, observer = Go("646 actual observer").transform;
        owner.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        GameObject bank = Go("646 frozen template bank"); bank.SetActive(false);
        GloomhavenVR.WorldUI.LazyTemplateProbe.Close(); GloomhavenVR.WorldUI.LazyTemplateProbe.Open(bank);
        Transform holder = NativeRow632(owner, "", "highlight"); holder.localScale = Vector3.one * .001f;
        holder.Find("GUI_LevelUp_Frame").gameObject.SetActive(false);
        holder.Find("Enhancement Ability Highlight Variant").gameObject.SetActive(false);
        Transform aura = holder.Find("Aura"); aura.Find("Highlight").GetComponent<CanvasGroup>().alpha = 1f;
        aura.Find("Types").GetComponent<CanvasGroup>().alpha = 1f;
        aura.Find("Types/Buy").gameObject.SetActive(true); aura.Find("Types/Sell").gameObject.SetActive(false);
        Transform print = Rect("646 physical offered print", owner, new Vector2(-.4f, 0), new Vector2(294, 450));
        print.localScale = Vector3.one * .0006f;
        Image("646 physical ink", print, Vector2.zero, new Vector2(294, 450), Color.red);
        var copy = Object.Instantiate(holder.gameObject, owner, false).transform;
        Object.DestroyImmediate(copy.Find("Aura").gameObject);
        TownServiceMirror.RegisterTemplate(3, 1, GloomhavenVR.WorldUI.LazyTemplateProbe.FreezeOriginal646(copy, "census646.holder"), address: "enchant.holder|");
        TownServiceMirror.RegisterTemplate(3, 2, GloomhavenVR.WorldUI.LazyTemplateProbe.FreezeOriginal646(aura, "census646.aura"), address: "enchant.holder|Aura#0");
        TownServiceMirror.RegisterTemplate(3, 3, GloomhavenVR.WorldUI.LazyTemplateProbe.FreezeOriginal646(print, "census646.print"), address: "face.646|");
        // A separate native UI root has its own source Canvas and no offered kind9
        // relation. Its original root/Graphic retain genuine native tweens.
        Transform genericHolder = Go("646 detached pooled generic holder").transform;
        genericHolder.position = new Vector3(.3f, .3f, 0); genericHolder.localScale = Vector3.one * .001f;
        Transform generic = Rect("646 retained ordinary native UI", genericHolder, Vector2.zero, new Vector2(120, 80));
        generic.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        Image genericInk = Image("646 ordinary original ink", generic, Vector2.zero, new Vector2(120, 80), Color.green);
        var genericCopy = Object.Instantiate(genericHolder.gameObject).transform;
        Object.DestroyImmediate(genericCopy.Find(generic.name).gameObject);
        TownServiceMirror.RegisterTemplate(3, 5, GloomhavenVR.WorldUI.LazyTemplateProbe.FreezeOriginal646(genericCopy, "census646.genericHolder"), address: "generic.mount|");
        TownServiceMirror.RegisterTemplate(3, 6, GloomhavenVR.WorldUI.LazyTemplateProbe.FreezeOriginal646(generic, "census646.generic"), address: "generic.original|");
        TownServiceMirror.BeginSession(3, 646, owner, owner);
        TownServiceMirror.RegisterModule(1, 1, holder, node => node == aura, "enchant.holder|");
        TownServiceMirror.RegisterModule(2, 2, aura, address: "enchant.holder|Aura#0");
        TownServiceMirror.RegisterModule(3, 3, print, address: "face.646|");
        TownServiceMirror.RegisterModule(5, 5, genericHolder, node => node == generic, "generic.mount|");
        TownServiceMirror.RegisterModule(6, 6, generic, address: "generic.original|");
        foreach (ushort id in new ushort[] { 1, 2, 3, 5, 6 }) TownServiceMirror.SetPriority(id, true);
        TownServiceMirror.RegisterOfferedFrame(holder, print);
        TownServiceMirror.SetLocalTransactionActive(3, true);
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        var receiver = new NetAvatarDriver(); var captures = new List<TownServiceFrame>(); var motions = new List<byte[]>();
        void Capture()
        {
            NetPlayerActors.Peer = 2; SetNativeSenderActive629(true);
            try { TownServiceMirror.Capture((bytes, length, identity) => {
                if (identity is TownServiceFrame frame) captures.Add(frame); else motions.Add(bytes);
            }); }
            finally { SetNativeSenderActive629(false); NetPlayerActors.Peer = 10; }
        }
        void Deliver(TownServiceFrame frame) => Check(receiver.FixtureQueue638(2, TownServiceCodec.Write(frame)), "actual original/census enters actual avatar queue");
        Capture(); RevisionDependencies646(captures.First(x => x.Module == 3)); foreach (var frame in captures) Deliver(frame);
        TownServiceFrame initialManifest655 = captures.Single(x => x.Module == TownServiceFrame.ManifestModule);
        TownServiceFrame initialHolder655 = captures.Single(x => x.Module == 1);
        foreach (var bytes in motions) Check(receiver.FixtureQueueMotion638(2, bytes), "actual kind9 offered relation enters receiver");
        receiver.FixtureApply638(); yield return null; receiver.FixtureApply638();
        Check(Remote(2, 2) != null && Remote(2, 2)!.Root.gameObject.activeInHierarchy, "initial original aura is mounted and visible");
        Transform remoteAura = Remote(2, 2)!.Root;
        Graphic[] ink = remoteAura.GetComponentsInChildren<Graphic>(true);
        // Drive a real new owner root/header and child-color revision through
        // capture, original codecs and the actual avatar receiver.
        captures.Clear(); motions.Clear();
        generic.localPosition += new Vector3(100, 20, 0); genericInk.color = new Color(0, .5f, 0, .8f);
        yield return null;
        SetNativeSenderActive629(true); TownServiceMirror.SetLocalTransactionActive(3, false);
        TownServiceMirror.SetLocalTransactionActive(3, true); SetNativeSenderActive629(false); Capture();
        File.WriteAllText(Path.Combine(_output, "generic-capture.txt"), "modules=" + string.Join(",", captures.Select(x => x.Module)) + "\n" + string.Join("\n", GloomhavenVR.Core.VRLog.Messages));
        TownServiceFrame genericRevision = captures.Single(x => x.Module == 6);
        genericRevision.SampleTime += .1f; // deterministic nonzero native interpolation interval
        foreach (var frame in captures.Where(x => x.Module != 6)) Deliver(frame);
        Deliver(genericRevision); receiver.FixtureApply638();
        var remoteOwners = (IDictionary)typeof(TownServiceMirror).GetField("Remote", PrivateStatic)!.GetValue(null)!;
        object genericModule = ((IDictionary)remoteOwners[2]!)[(ushort)6]!;
        var genericMotion = (TownServiceMotion)genericModule.GetType().GetField("Motion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(genericModule)!;
        Array from = (Array)typeof(TownServiceMotion).GetField("_from", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(genericMotion)!;
        Array to = (Array)typeof(TownServiceMotion).GetField("_to", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(genericMotion)!;
        Type motionState = from.GetValue(0)!.GetType();
        Vector3 oldFrom = (Vector3)motionState.GetField("Position", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(from.GetValue(1))!;
        Vector3 oldTo = (Vector3)motionState.GetField("Position", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(to.GetValue(1))!;
        Check(oldFrom != oldTo, "real generic original has an unfinished native local hover interpolation");
        Transform genericHost = ((GameObject)genericModule.GetType().GetField("Host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(genericModule)!).transform;
        Matrix4x4 nativeBasis = genericHost.localToWorldMatrix;
        float[] durations = (float[])typeof(TownServiceMotion).GetField("_nodeDuration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(genericMotion)!;
        Color childFrom = (Color)motionState.GetField("Color", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(from.GetValue(2))!;
        Color childTo = (Color)motionState.GetField("Color", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(to.GetValue(2))!;
        Check(childFrom != childTo, "real original Graphic alpha/color has an independent unfinished native tween");
        float[] clocks = (float[])typeof(TownServiceMotion).GetField("_nodeStarted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(genericMotion)!;
        float childClock = clocks[clocks.Length - 1];

        Vector3 before = remoteAura.position;
        void PaintCurrent()
        {
            Transform currentAura = Remote(2, 2)!.Root, currentPrint = Remote(2, 3)!.Root;
            Check(currentPrint != null && currentPrint.gameObject.activeInHierarchy,
                "physical card remains mounted and visible on every census render");
            Check(currentPrint.GetComponentInChildren<Image>(true).color == Color.red,
                "physical original card ink remains authored, never a stale/blank substitute");
            foreach (Graphic original in aura.GetComponentsInChildren<Graphic>(true))
            {
                string path = Relative639(aura, original.transform);
                Transform same = path.Length == 0 ? currentAura : currentAura.Find(path);
                Graphic? painted = same.GetComponent(original.GetType()) as Graphic;
                Check(painted != null && painted.enabled == original.enabled && painted.color == original.color,
                    "each retained original Graphic keeps its authored alpha/color on every rendered frame");
            }
            Check(Remote(2, 6) != null && Remote(2, 6)!.Root.gameObject.activeInHierarchy,
                "ordinary retained original remains visible without offered-frame masking");
            float rootPhase = Mathf.Clamp01((Time.unscaledTime - clocks[1]) / durations[1]);
            Vector3 expected = nativeBasis.MultiplyPoint3x4(Vector3.LerpUnclamped(oldFrom, oldTo, rootPhase));
            Check(Vector3.Distance(Remote(2, 6)!.Root.position, expected) < .00001f,
                "real retained original local hover keeps its exact authored world path without kind9 masking");
            float inkPhase = Mathf.Clamp01((Time.unscaledTime - clocks[2]) / durations[2]);
            Color expectedInk = Color.LerpUnclamped(childFrom, childTo, inkPhase);
            Check(Remote(2, 6)!.Root.GetComponentInChildren<Image>().color == expectedInk,
                "real retained original alpha/color tween keeps its exact independent output");
            Check(clocks[clocks.Length - 1] == childClock,
                "independent authored original child animation clock survives root reparenting");
            Color32[] output = Render639(observer);
            Check(output.Count(pixel => pixel.r > 180 && pixel.g < 60 && pixel.b < 60) > 100,
                "actual camera/readback paints the retained physical original every census render");
        }
        PaintCurrent();
        // The owner genuinely keeps this original child alive while its pooled
        // parent is retired. Its updated parent/pose metadata arrives later.
        aura.SetParent(owner, true); generic.SetParent(owner, true);
        TownServiceMirror.UnregisterModule(1); TownServiceMirror.UnregisterModule(5);
        captures.Clear(); motions.Clear(); Capture();
        TownServiceFrame manifest = captures.Single(x => x.Module == TownServiceFrame.ManifestModule);
        Check(manifest.Modules.Contains((ushort)2) && !manifest.Modules.Contains((ushort)1), "actual shrinking census retains child and withdraws old mount");
        Deliver(manifest); receiver.FixtureApply638();
        // A sender may finish the old immutable bundle after the newer census.
        // Both old metadata groups use the actual queue/ReceiveParsed path, not
        // a fixture-side filter. They cannot reintroduce a retired holder.
        Deliver(initialManifest655); Deliver(initialHolder655); receiver.FixtureApply638();
        for (int frame = 0; frame < 45; frame++)
        {
            receiver.FixtureApply638(); Canvas.ForceUpdateCanvases();
            Check(Remote(2, 1) == null, "late old census/original cannot remount or render the withdrawn native holder");
            Check(remoteAura != null && remoteAura.gameObject.activeInHierarchy, "retained native child survives retired mount on every render");
            Check(ink[0] != null && ink[0].gameObject.activeInHierarchy && ink[0].color.a > .99f, "retained original Graphic alpha is never blanked by census shrink");
            Check(Vector3.Distance(remoteAura.position, before) < .00001f, "unchanged current physical offered plane survives while its new parent packet is pending");
            PaintCurrent(); yield return null;
        }
        foreach (var frame in captures.Where(x => x.Module != TownServiceFrame.ManifestModule && x.Module != 6)) Deliver(frame);
        foreach (var bytes in motions) Check(receiver.FixtureQueueMotion638(2, bytes), "current exact geometric relation is retained");
        receiver.FixtureApply638();
        remoteAura = Remote(2, 2)!.Root;
        Check(remoteAura.gameObject.activeInHierarchy && remoteAura.parent != null,
            "current exact original remains visible across legitimate detached-canvas remount");
        Check(Vector3.Distance(remoteAura.position, before) < .00001f,
            "current exact native physical plane is unchanged after detached-canvas remount");
        // Growth returns the same original to a newly registered native holder.
        // Its census/holder metadata can precede the updated child header too.
        aura.SetParent(holder, true); SetNativeSenderActive629(true);
        try { TownServiceMirror.RegisterModule(4, 1, holder, node => node == aura, "enchant.holder|"); }
        finally { SetNativeSenderActive629(false); }
        TownServiceMirror.SetPriority(4, true); captures.Clear(); motions.Clear(); Capture();
        TownServiceFrame growth = captures.Single(x => x.Module == TownServiceFrame.ManifestModule);
        Check(growth.Modules.Contains((ushort)4), "native current census grows with a replacement holder");
        Deliver(growth); Deliver(captures.Single(x => x.Module == 4)); receiver.FixtureApply638();
        for (int frame = 0; frame < 20; frame++)
        {
            receiver.FixtureApply638(); Canvas.ForceUpdateCanvases();
            Check(Remote(2, 2) != null && Remote(2, 2)!.Root.gameObject.activeInHierarchy,
                "retained native ink survives growing census while child metadata is delayed");
            Check(Vector3.Distance(Remote(2, 2)!.Root.position, before) < .00001f,
                "growing census never displaces actual physical ring plane");
            PaintCurrent(); yield return null;
        }
        foreach (var frame in captures.Where(x => x.Module != TownServiceFrame.ManifestModule && x.Module != 4)) Deliver(frame);
        foreach (var bytes in motions) Check(receiver.FixtureQueueMotion638(2, bytes), "grown native kind9 relation enters unchanged receiver");
        receiver.FixtureApply638();
        File.WriteAllText(Path.Combine(_output, "census-final.txt"), "captured=" + string.Join(",", captures.Select(x => x.Module + ":" + x.Sequence + ":" + x.ParentModule + ":" + x.Visible)) + "\n" + string.Join("\n", GloomhavenVR.Core.VRLog.Messages));
        Check(Remote(2, 2)!.Root.gameObject.activeInHierarchy && Remote(2, 4) != null,
            "grown picture atomically remounts its native child after current headers");
        // Current genuine owner withdrawal must hide immediately, never use
        // the previous visible picture as a timeout-based fallback.
        captures.Clear(); motions.Clear(); TownServiceMirror.EndSession(); Capture();
        Deliver(captures.Single(x => x.Module == TownServiceFrame.ManifestModule)); receiver.FixtureApply638();
        Check(Remote(2, 2) == null || !Remote(2, 2)!.Root.gameObject.activeInHierarchy, "genuine owner close removes offered originals immediately");
        TownServiceMirror.Shutdown(); GloomhavenVR.WorldUI.LazyTemplateProbe.Close();
    }
}
