using System;
using System.Collections;
using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static void IndependentNativeClock632()
    {
        RectTransform host = (RectTransform)Go("Separate native child clock").transform;
        host.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform aura = (RectTransform)Go("Native spinning Aura", host).transform;
        var motion = new TownServiceMotion(host, new Transform[] {host, aura}, "enchant.holder|");
        motion.BeforeApply(0f); motion.AfterApply(0f, .1f, sourceSampleTime: 0f);
        motion.BeforeApply(.1f); aura.localRotation = Quaternion.Euler(0f, 0f, 36f);
        motion.AfterApply(.1f, .1f, sourceSampleTime: .1f);
        motion.Tick(.12f);
        float previous = aura.localEulerAngles.z;
        motion.BeforeApply(.12f); host.localPosition = Vector3.right * .2f;
        motion.AfterApply(.12f, .02f, sourceSampleTime: .12f);
        for (int i = 1; i <= 7; i++)
        {
            float now = .12f + i * .01f; motion.Tick(now);
            float phase = aura.localEulerAngles.z;
            Quaternion expected = Quaternion.SlerpUnclamped(Quaternion.identity, Quaternion.Euler(0f, 0f, 36f), (now - .1f) / .11f);
            Check(Quaternion.Angle(aura.localRotation, expected) < .02f,
                "unrelated owner root headers preserve the native child's independent continuous spin clock");
            Check(phase > previous && phase - previous < 3.4f,
                "native aura advances smoothly every render instead of finishing early and freezing between its samples");
            previous = phase;
        }
        motion.Tick(.22f);
        Check(Quaternion.Angle(aura.localRotation, Quaternion.Euler(0f, 0f, 36f)) < .02f,
            "independent native child clock retains its exact original endpoint");
    }

    private static void CompleteOfferedArtwork632(List<byte[]> artwork)
    {
        foreach (byte[] bytes in artwork)
            if (TownServiceCodec.TryRead(bytes, bytes.Length, out var frame)) TownServiceDelivery.Completed?.Invoke(frame!);
    }

    private static IEnumerator PartitionedOfferedGeometry632()
    {
        IndependentNativeClock632();
        TownServiceMirror.Shutdown(); _offeredSequence629 = 50000;
        Transform owner = Go("Partitioned source shared frame").transform;
        Transform observer = Go("Partitioned observer shared frame").transform;
        observer.SetPositionAndRotation(new Vector3(5f, .31f, .6f), Quaternion.Euler(0f, 77f, 0f));
        observer.localScale = Vector3.one * 1.3f;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        RectTransform canvas = (RectTransform)Go("Original world holder canvas", owner).transform;
        canvas.localScale = new Vector3(.0011f, .0008f, .0012f);
        canvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform holder = (RectTransform)Go("CardHilight", canvas).transform;
        holder.sizeDelta = new Vector2(325f, 450f); holder.localScale = Vector3.one * .52f;
        holder.gameObject.AddComponent<CanvasGroup>();
        RectTransform nativePrint = (RectTransform)Go("FullAbilityCard", holder).transform;
        nativePrint.sizeDelta = new Vector2(294f, 450f);
        Image area = Image("Native selectable row", nativePrint, new Vector2(47f, -62f), new Vector2(171f, 82f), Color.cyan);
        RectTransform aura = (RectTransform)Go("Aura", holder).transform;
        aura.sizeDelta = new Vector2(520f, 520f);
        Image ring = Image("Ring ink", aura, Vector2.zero, aura.sizeDelta, new Color(.1f, .7f, 1f, .35f));
        RectTransform physical = (RectTransform)Go("Actual adopted FullAbilityCard", owner).transform;
        physical.sizeDelta = nativePrint.sizeDelta; physical.localScale = Vector3.one * .00052f;
        physical.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        physical.gameObject.AddComponent<CanvasGroup>();
        Image("Native adopted red print", physical, Vector2.zero, physical.sizeDelta, Color.red);
        // The native publisher cuts exact subtrees at both packet-size partitions
        // and dynamic ability-button boundaries. Preserve that actual topology:
        // each clone retains its exact native parent and independently sampled root header.
        Func<Transform, bool> exclude = node => node == aura || node == area.transform;
        TownServiceMirror.RegisterTemplate(3, 1, holder, exclude, "enchant.holder|");
        TownServiceMirror.RegisterTemplate(3, 2, aura, address: "enchant.holder|Aura#0");
        TownServiceMirror.RegisterTemplate(3, 3, area.transform, address: "enchant.highlight|");
        TownServiceMirror.RegisterTemplate(3, 4, physical, address: "face.632|");
        TownServiceMirror.BeginSession(3, 632, owner, owner);
        TownServiceMirror.RegisterModule(10, 1, holder, exclude, "enchant.holder|");
        TownServiceMirror.RegisterModule(11, 2, aura, address: "enchant.holder|Aura#0");
        TownServiceMirror.RegisterModule(12, 3, area.transform, address: "enchant.highlight|");
        TownServiceMirror.RegisterModule(13, 4, physical, address: "face.632|");
        foreach (ushort module in new ushort[] {10, 11, 12, 13}) TownServiceMirror.SetPriority(module, true);
        TownServiceMirror.RegisterOfferedFrame(holder, physical);
        FastCapture first = OfferedCapture629(); Receive(1, first.Artwork); CompleteOfferedArtwork632(first.Artwork); DeliverMotion(1, first);
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding copy = Remote(1, 10)!, copiedRing = Remote(1, 11)!, copiedArea = Remote(1, 12)!, face = Remote(1, 13)!;
        Check(copy != null && copiedRing != null && copiedArea != null && face != null,
            "real separately mounted native aura selectable area and physical print are all admitted");
        float clock = Time.unscaledTime + 1f;
        for (int step = 0; step < 5; step++)
        {
            yield return null; clock += .2f;
            physical.SetPositionAndRotation(new Vector3(.17f, .8f + step * .006f, -.2f), Quaternion.Euler(0f, 93f + step * 29f, 0f));
            canvas.SetPositionAndRotation(physical.position, physical.rotation);
            holder.SetPositionAndRotation(physical.position, physical.rotation);
            area.rectTransform.anchoredPosition = new Vector2(47f + step * 5f, -62f - step * 3f);
            FastCapture current = OfferedCapture629(); CompleteOfferedArtwork632(current.Artwork);
            // Model the separately-budgeted old holder header explicitly: fresh
            // face, ring, row and exact affinity samples arrive first.
            foreach (byte[] bytes in current.Motion)
            {
                TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var packet);
                packet!.Entries.RemoveAll(entry => entry.Module != 13 && entry.Kind == 1);
                packet.Sequence = ++_offeredSequence629; packet.SampleTime = clock;
                TownServiceMirror.ReceiveMotion(1, packet); OfferedReceiptClock629(packet);
            }
            OfferedRender629(clock); OfferedRender629(clock + .15f);
            CheckOfferedCorners629(physical, area.rectTransform, face.Root, (RectTransform)copiedArea.Root,
                "partitioned native area follows the physical print despite an independently delayed ninety-degree holder header");
            CheckOfferedPlane629(physical, ring.rectTransform, face.Root, (RectTransform)copiedRing.Root.Find("Ring ink"),
                "partitioned native aura follows the actual physical print plane instead of its old canvas yaw");
            if (step != 4) continue;
            // A fresh unchanged artwork header contains no TLV109. It cannot
            // invalidate an already verified current exact native print relation.
            var local = (System.Collections.IDictionary)typeof(TownServiceMirror).GetProperty("Local", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            foreach (object module in local.Values)
                module.GetType().GetField("NextBaseline", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(module, 0f);
            List<byte[]> artwork = OfferedCapture629().Artwork; CompleteOfferedArtwork632(artwork);
            int headers = 0;
            foreach (byte[] bytes in artwork)
            {
                TownServiceCodec.TryRead(bytes, bytes.Length, out var frame);
                if (frame!.Module == TownServiceFrame.ManifestModule) continue;
                headers++;
                frame.SampleTime = clock + .16f; frame.Sequence += 1000000;
                byte[] updated = TownServiceCodec.Write(frame);
                Check(TownServiceMirror.Receive(1, updated, updated.Length), "newer original same-structure header accepts its exact native content");
            }
            Check(headers == 4, "newer artwork heartbeat probe uses four genuine complete production originals: " + headers);
            TownServiceMirror.TickRemote(_ => observer); OfferedRender629(clock + .17f);
            var remotes = (IDictionary)typeof(TownServiceMirror).GetField("Remote", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            var updatedModules = (IDictionary)remotes[1]!;
            foreach (ushort moduleId in new ushort[] {10, 11, 12, 13})
            {
                object updatedModule = updatedModules[moduleId]!;
                var updatedFrame = (TownServiceFrame)updatedModule.GetType().GetField("LastFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(updatedModule)!;
                Check(Mathf.Abs(updatedFrame.SampleTime - (clock + .16f)) < .001f,
                    "genuine same-original artwork heartbeat is actually applied before the affinity probe: " + moduleId + "/" + updatedFrame.SampleTime + "/" + (clock + .16f));
            }
            yield return null;
            physical.rotation = Quaternion.Euler(0f, 181f + step * 29f, 0f);
            canvas.rotation = physical.rotation;
            holder.rotation = physical.rotation;
            int movedPrints = 0;
            for (int turn = 0; turn < 8 && movedPrints == 0; turn++)
            {
                FastCapture faceOnly = OfferedCapture629(); CompleteOfferedArtwork632(faceOnly.Artwork);
                foreach (byte[] bytes in faceOnly.Motion)
                {
                    TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var packet);
                    packet!.Entries.RemoveAll(entry => entry.Module != 13 || entry.Kind != 1);
                    if (packet.Entries.Count == 0) continue;
                    movedPrints += packet.Entries.Count;
                    packet.Sequence = ++_offeredSequence629; packet.SampleTime = clock + .22f;
                    TownServiceMirror.ReceiveMotion(1, packet); OfferedReceiptClock629(packet);
                }
                if (movedPrints == 0) yield return null;
            }
            Check(movedPrints == 1, "same-original affinity probe moves the actual physical print with a real fresh numeric sample: " + movedPrints);
            OfferedRender629(clock + .22f); OfferedRender629(clock + .49f);
            CheckOfferedPlane629(physical, ring.rectTransform, face.Root, (RectTransform)copiedRing.Root.Find("Ring ink"),
                "same-original artwork heartbeat cannot withdraw the current independent offered print affinity");
        }
        TownServiceMirror.RegisterOfferedFrame(holder, null); TownServiceMirror.Shutdown();
        UnityEngine.Object.DestroyImmediate(owner.gameObject); UnityEngine.Object.DestroyImmediate(observer.gameObject);
    }
}
