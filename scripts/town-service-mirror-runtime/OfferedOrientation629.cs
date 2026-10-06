using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static FastCapture OfferedCapture629()
    {
        // Drive the real owner sampler deterministically without waiting for a
        // software editor frame longer than the actual 15 Hz transport turn.
        typeof(TownServiceMirror).GetField("_nextMotionSend", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0f);
        var local = (IDictionary)typeof(TownServiceMirror).GetProperty("Local", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (object module in local.Values)
            module.GetType().GetField("NextRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, 0f);
        return CaptureFast();
    }

    private static ulong _offeredSequence629;
    private static string _offeredContext629 = "initial";
    private static TownServiceMotionEntry? _authenticOfferedFrame629, _authenticPhysicalRoot629;

    private static void RememberOffered629(TownServiceMotionPacket packet)
    {
        foreach (TownServiceMotionEntry entry in packet.Entries)
        {
            if (entry.Kind == 9 && entry.Visible) _authenticOfferedFrame629 = entry;
            if (entry.Kind == 1 && entry.Module == 11) _authenticPhysicalRoot629 = entry;
        }
    }
    private static void OfferedReceive629(FastCapture capture, float sample)
    {
        foreach (byte[] bytes in capture.Motion)
        {
            Check(TownServiceMotionCodec.TryRead(bytes, bytes.Length, out TownServiceMotionPacket? packet), "offered sampler emits real bounded numeric entries");
            // Only the deterministic transport clock changes. Native geometry,
            // dirty-slot selection, packet packing and receiver are production.
            RememberOffered629(packet!);
            packet!.Sequence = ++_offeredSequence629; packet.SampleTime = sample;
            byte[] wire = TownServiceMotionCodec.TryWritePacked(packet)!;
            Check(TownServiceMotionCodec.TryRead(wire, wire.Length, out packet), "offered transport clock survives the actual wire codec");
            Check(TownServiceMirror.ReceiveMotion(1, packet!), "offered receiver accepts genuine owner geometry");
            OfferedReceiptClock629(packet!);
        }
    }

    private static void OfferedReceiptClock629(TownServiceMotionPacket packet)
    {
        // The deterministic render clock must also be the simulated receipt
        // clock; otherwise a 3-second liveness timeout expires real editor
        // packets while evaluating future subframes in less than one second.
        var peers = (IDictionary)typeof(TownServiceMirror).GetField("MotionPeers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object peer = peers[1]!;
        var slots = (IDictionary)peer.GetType().GetField("Slots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer)!;
        foreach (object slot in slots.Values)
            if ((ulong)slot.GetType().GetField("ReceivedSequence", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(slot)! == packet.Sequence)
                slot.GetType().GetField("ReceivedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(slot, packet.SampleTime);
    }

    private static void OfferedRender629(float now)
    {
        var peers = (IDictionary)typeof(TownServiceMirror).GetField("Remote", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (IDictionary modules in peers.Values)
            foreach (object module in modules.Values)
                ((TownServiceMotion)module.GetType().GetField("Motion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!).Tick(now);
        TownServiceMirror.ApplyRemoteMotion(now);
    }

    private static IEnumerator OfferedOrientation629()
    {
        TownServiceMirror.Shutdown(); _offeredSequence629 = 10000; _authenticOfferedFrame629 = _authenticPhysicalRoot629 = null;
        Transform owner = Go("Owner offered frame").transform;
        Transform observer = Go("Observer offered frame").transform;
        observer.SetPositionAndRotation(new Vector3(6f, .13f, -.4f), Quaternion.Euler(0f, 83f, 0f));
        observer.localScale = Vector3.one * 1.4f;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        RectTransform canvasFrame = (RectTransform)Go("Original converted holder canvas", owner).transform;
        canvasFrame.sizeDelta = new Vector2(680f, 660f); canvasFrame.localScale = new Vector3(.001f * .73f, .001f * 1.16f, .001f);
        canvasFrame.localRotation = Quaternion.Euler(0f, 27f, 11f);
        canvasFrame.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform root = (RectTransform)Go("CardHilight", canvasFrame).transform;
        root.sizeDelta = new Vector2(325f, 450f); root.localScale = Vector3.one * .52f;
        root.gameObject.AddComponent<CanvasGroup>();
        var highlighter = root.gameObject.AddComponent<GloomhavenVR.WorldUI.UIEnhancementCardHighlighter>();
        var card = Go("Native pooled card", root).AddComponent<AbilityCardUI>();
        RectTransform print = (RectTransform)Go("FullAbilityCard", card.transform).transform;
        print.sizeDelta = new Vector2(294f, 450f); print.pivot = new Vector2(.31f, .68f);
        print.anchoredPosition = new Vector2(19f, -11f); print.localScale = Vector3.one * .84f;
        card.fullAbilityCard = print; highlighter.Card = card;
        Image area = Image("Selectable native printed row", print, new Vector2(47f, -62f), new Vector2(171f, 82f), Color.cyan);
        area.gameObject.AddComponent<UIEnhancementButtonHighlight>();
        RectTransform aura = (RectTransform)Go("Aura", root).transform; aura.sizeDelta = new Vector2(600f, 600f);
        Image ring = Image("Native aura ink", aura, Vector2.zero, new Vector2(520f, 520f), new Color(.1f, .7f, 1f, .35f));
        RectTransform physical = (RectTransform)Go("Actual physical printed front", owner).transform;
        physical.sizeDelta = new Vector2(294f, 450f); physical.localScale = Vector3.one * .00049f;
        physical.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        physical.gameObject.AddComponent<CanvasGroup>();
        Image original = Image("Actual original artwork", physical, Vector2.zero, new Vector2(294f, 450f), Color.red);
        RectTransform control = (RectTransform)Go("Unhovered physical print control", owner).transform;
        control.sizeDelta = physical.sizeDelta; control.localScale = physical.localScale;
        control.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        control.gameObject.AddComponent<CanvasGroup>();
        Image("Unhovered original artwork", control, Vector2.zero, physical.sizeDelta, Color.red);
        TownServiceEnhancementHandoff.PhysicalCardFace = physical;
        var mask = card.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>(); mask.Mask();
        mask.SendMessage("LateUpdate");
        TownServiceMirror.RegisterTemplate(3, 1, root, address: "enchant.holder|");
        TownServiceMirror.RegisterTemplate(3, 2, physical, address: "face.629|");
        TownServiceMirror.RegisterTemplate(3, 3, control, address: "face.630|");
        TownServiceMirror.BeginSession(3, 629, owner, owner);
        TownServiceMirror.RegisterModule(10, 1, root, address: "enchant.holder|");
        TownServiceMirror.RegisterModule(11, 2, physical, address: "face.629|");
        TownServiceMirror.RegisterModule(12, 3, control, address: "face.630|");
        TownServiceMirror.SetPriority(10, true); TownServiceMirror.SetPriority(11, true); TownServiceMirror.SetPriority(12, true);
        FastCapture first = OfferedCapture629(); Receive(1, first.Artwork); DeliverMotion(1, first);
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding holder = Remote(1, 10)!, face = Remote(1, 11)!;
        Check(holder != null && face != null, "offered proof admits both independent physical artwork and original native overlays");
        RectTransform remotePrint = (RectTransform)holder.Root.Find("Native pooled card/FullAbilityCard");
        TownServiceBinding controlCopy = Remote(1, 12)!;
        RectTransform remoteArea = (RectTransform)holder.Root.Find("Native pooled card/FullAbilityCard/Selectable native printed row");
        RectTransform remoteRing = (RectTransform)holder.Root.Find("Aura/Native aura ink");
        float clock = Time.unscaledTime + 1f;
        physical.rotation = control.rotation = Quaternion.Euler(17f, 91f, -9f); mask.SendMessage("LateUpdate");
        FastCapture turned = OfferedCapture629(); OfferedReceive629(turned, clock); OfferedRender629(clock);
        OfferedRender629(clock + .025f);
        Check(Quaternion.Angle(remotePrint.rotation, face.Root.rotation) < .02f,
            "independent offered print and native overlays share the same intermediate owner rotation");
        yield return null;
        area.color = Color.green; original.color = Color.yellow;
        FastCapture hovered = OfferedCapture629(); OfferedReceive629(hovered, clock + .03f); OfferedRender629(clock + .03f);
        OfferedRender629(clock + .055f);
        Check(Quaternion.Angle(remotePrint.rotation, face.Root.rotation) < .02f,
            "native hover packets cannot restart offered overlay root rotation independently of its physical print");
        Check(Quaternion.Angle(face.Root.rotation, controlCopy.Root.rotation) < .02f,
            "physical offered print facing remains invariant under unrelated native artwork hover");
        CheckOfferedCorners629(physical, area.rectTransform, face.Root, remoteArea,
            "native area stays in the same owner-authored print frame during a hover turn");

        // Exact source output, not a nominal card aspect ratio or root quaternion.
        // Source canvas stretch and native print pivots are intentionally retained.
        for (int step = 0; step < 18; step++)
        {
            yield return null;
            clock += .08f;
            physical.SetPositionAndRotation(new Vector3(.17f, .81f + Mathf.Sin(step * .2f) * .013f, -.2f),
                Quaternion.Euler(12f + step * .3f, step * 19f, -9f));
            control.SetPositionAndRotation(physical.position, physical.rotation);
            float scale = step % 3 == 0 ? .6f : step % 3 == 1 ? 1f : 1.7f;
            physical.localScale = control.localScale = new Vector3(.00049f * scale, .00049f * scale, .00049f * scale);
            canvasFrame.localScale = new Vector3(.001f * .73f, .001f * 1.16f, .001f);
            aura.rotation = Quaternion.Euler(0f, 0f, step * 23f);
            mask.SendMessage("LateUpdate");
            FastCapture motion = OfferedCapture629();
            // A legitimate delayed numeric holder root cannot pull its original
            // ring/areas away from the already admitted owner's physical print.
            foreach (byte[] bytes in motion.Motion)
            {
                TownServiceMotionCodec.TryRead(bytes, bytes.Length, out TownServiceMotionPacket? packet);
                RememberOffered629(packet!);
                packet!.Entries.RemoveAll(entry => entry.Module == 10 && entry.Kind == 1);
                packet.Sequence = ++_offeredSequence629; packet.SampleTime = clock;
                byte[] wire = TownServiceMotionCodec.TryWritePacked(packet)!;
                Check(TownServiceMotionCodec.TryRead(wire, wire.Length, out packet), "partial numeric delivery preserves validated remaining original records");
                TownServiceMirror.ReceiveMotion(1, packet!); OfferedReceiptClock629(packet!);
            }
            OfferedRender629(clock);
            foreach (float offset in new[] { .011f, .035f, .065f })
            {
                _offeredContext629 = "step=" + step + " offset=" + offset;
                _camera.transform.SetPositionAndRotation(new Vector3(step * .5f, 3f, -4f), Quaternion.Euler(step * 11f, step * 37f, step * 7f));
                OfferedRender629(clock + offset);
                CheckOfferedCorners629(physical, area.rectTransform, face.Root, remoteArea,
                    "delayed native holder root cannot separate original overlays from admitted owner print");
                CheckOfferedPlane629(physical, ring.rectTransform, face.Root, remoteRing,
                    "original aura remains in the exact owner print plane throughout continuous gaze turns");
            }
        }
        OfferedRender629(clock + .5f);
        CheckOfferedCorners629(physical, ring.rectTransform, face.Root, remoteRing,
            "settled native aura retains exact owner offsets and native phase under canvas stretch");
        // Layout replacement is discrete native metadata; verify the settled
        // geometry separately from continuously animated owner gaze.
        for (int layout = 0; layout < 3; layout++)
        {
            yield return new WaitForSecondsRealtime(.02f);
            print.sizeDelta = new Vector2(294f + layout * 41f, 450f + layout * 19f);
            print.pivot = new Vector2(.19f + layout * .21f, .78f - layout * .13f);
            print.anchoredPosition = new Vector2(17f + layout * 7f, -11f - layout * 5f);
            mask.SendMessage("LateUpdate"); clock += .13f;
            FastCapture replacement = OfferedCapture629();
            OfferedReceive629(replacement, clock); OfferedRender629(clock); OfferedRender629(clock + .12f);
            System.IO.File.AppendAllText(System.IO.Path.Combine(_output, "layout-clock.txt"),
                "layout=" + layout + " sourceNow=" + Time.unscaledTime + " numericPackets=" + replacement.Motion.Count + "\n");
            _offeredContext629 = "layout=" + layout + " sourcePivot=" + print.pivot + " copiedPivot=" + remotePrint.pivot + " sourceSize=" + print.sizeDelta + " copiedSize=" + remotePrint.sizeDelta;
            CheckOfferedCorners629(physical, area.rectTransform, face.Root, remoteArea,
                "native print layout replacement preserves exact source pivots corners and offsets");
        }
        LogOfferedRingBoundary636("before final settle", physical, ring.rectTransform, face.Root, remoteRing);
        // Layout replacement also changes the native ink's compensated phase.
        // Its independent pure-spin clock lasts 130ms * 1.1 = 143ms; the earlier
        // +120ms area check is an intermediate picture, not its exact endpoint.
        // Keep those intermediate checks and compare the final static pixels
        // only after the already-received drawing target finishes interpolating.
        clock += .2f; // Finish the independently sampled native drawing target.
        OfferedRender629(clock);
        LogOfferedRingBoundary636("after final settle", physical, ring.rectTransform, face.Root, remoteRing);
        CheckOfferedCorners629(physical, ring.rectTransform, face.Root, remoteRing,
            "final native drawing target settles exactly before owner and observer pixel equality");
        Color32[] ownerInk = RenderOffered629(canvasFrame, print, ring.rectTransform, area.rectTransform, 8, "offered-orientation-629-owner");
        Color32[] observerInk = RenderOffered629(holder.Root.parent, remotePrint, remoteRing, remoteArea, 9, "offered-orientation-629-observer");
        long error = 0; int painted = 0;
        for (int pixel = 0; pixel < ownerInk.Length; pixel++)
        {
            if (!ownerInk[pixel].Equals(ownerInk[0])) painted++;
            error += Math.Abs(ownerInk[pixel].r - observerInk[pixel].r)
                + Math.Abs(ownerInk[pixel].g - observerInk[pixel].g) + Math.Abs(ownerInk[pixel].b - observerInk[pixel].b);
        }
        Check(painted > 500 && error <= ownerInk.Length / 10,
            "continuous offered geometry produces the same visible native ring and selectable ink in owner and observer renders");
        // Reject stale/different original identities before any transform write.
        Check(_authenticOfferedFrame629 != null && _authenticOfferedFrame629.HasCanvasFrame
            && _authenticOfferedFrame629.OfferedLocalScale && _authenticOfferedFrame629.OfferedModule == 11,
            "real owner declares its exact original canvas and native print bindings");
        OfferedRender629(clock + .2f);
        Vector3 validPosition = holder.Root.position; Quaternion validRotation = holder.Root.rotation;
        foreach (int affinity in new[] {0, 1, 2})
        {
            var real = new TownServiceMotionPacket { Sequence = ++_offeredSequence629, SampleTime = clock + .21f };
            real.Entries.Add(_authenticOfferedFrame629!);
            byte[] faithful = TownServiceMotionCodec.Write(real);
            TownServiceMotionCodec.TryRead(faithful, faithful.Length, out var damaged);
            TownServiceMotionEntry entry = damaged!.Entries[0]; entry.Numbers[0] += 1000f;
            if (affinity == 0) entry.OfferedBinding = uint.MaxValue;
            else if (affinity == 1) entry.OfferedStructure++;
            else entry.Session++;
            byte[] malformedAffinity = TownServiceMotionCodec.Write(damaged);
            TownServiceMotionCodec.TryRead(malformedAffinity, malformedAffinity.Length, out damaged);
            TownServiceMirror.ReceiveMotion(1, damaged!); OfferedReceiptClock629(damaged!); OfferedRender629(clock + .22f);
            Check(Vector3.Distance(holder.Root.position, validPosition) < .00003f
                && Quaternion.Angle(holder.Root.rotation, validRotation) < .02f,
                "missing different or stale original print affinity cannot move native overlays");
        }
        var restored = new TownServiceMotionPacket { Sequence = ++_offeredSequence629, SampleTime = clock + .23f };
        restored.Entries.Add(_authenticOfferedFrame629!);
        TownServiceMirror.ReceiveMotion(1, restored); OfferedReceiptClock629(restored); OfferedRender629(clock + .24f);
        CheckOfferedCorners629(physical, area.rectTransform, face.Root, remoteArea,
            "fresh exact original affinity resumes the owner print without approximating its geometry");
        observer.localScale = Vector3.zero;
        Check(_authenticPhysicalRoot629 != null, "continuous fixture captures the actual owner physical root records");
        var singular = new TownServiceMotionPacket { Sequence = ++_offeredSequence629, SampleTime = clock + .25f };
        singular.Entries.Add(_authenticPhysicalRoot629!);
        TownServiceMirror.ReceiveMotion(1, singular); OfferedReceiptClock629(singular);
        OfferedRender629(clock + .25f);
        Check(IsFiniteOffered629(holder.Root.localScale) && IsFiniteOffered629(face.Root.localScale),
            "a temporarily singular shared parent never produces infinite native scale");
        observer.localScale = Vector3.one * 1.4f; OfferedRender629(clock + .26f);
        CheckOfferedCorners629(physical, area.rectTransform, face.Root, remoteArea,
            "a restored finite shared parent resumes exact original geometry");
        mask.Restore(); TownServiceEnhancementHandoff.PhysicalCardFace = null;
        yield return new WaitForSecondsRealtime(.02f);
        FastCapture released = OfferedCapture629(); bool withdrew = false;
        foreach (byte[] raw in released.Motion)
        {
            TownServiceMotionCodec.TryRead(raw, raw.Length, out var packet);
            foreach (TownServiceMotionEntry entry in packet!.Entries)
                if (entry.Kind == 9 && entry.Module == 10 && !entry.Visible)
                    withdrew = entry.OfferedModule == 0 && entry.OfferedStructure == 0 && entry.OfferedBinding == 0;
        }
        Check(withdrew, "restoring a pooled native holder explicitly withdraws its original offered print relation");
        var registry = (IDictionary)typeof(TownServiceMirror).GetField("OfferedFrames", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            TownServiceEnhancementHandoff.PhysicalCardFace = physical;
            mask.Mask(); mask.SendMessage("LateUpdate");
            Check(registry.Count == 1, "reused native card records only its current exact physical print");
            mask.Restore();
            Check(registry.Count == 0, "repeated native holder restoration releases its source transform references");
        }
        mask.Mask(); mask.SendMessage("LateUpdate");
        UnityEngine.Object.DestroyImmediate(print.gameObject); mask.Restore();
        Check(registry.Count == 0, "disposing native artwork before its holder still unregisters the offered relation");
        Transform destroyedHolder = Go("Disposed pooled original", owner).transform;
        TownServiceMirror.RegisterOfferedFrame(destroyedHolder, physical);
        UnityEngine.Object.DestroyImmediate(destroyedHolder.gameObject);
        OfferedCapture629();
        Check(registry.Count == 0, "a disposed pooled holder cannot retain an unbounded strong transform registry entry");
        TownServiceEnhancementHandoff.PhysicalCardFace = null;
        TownServiceMirror.Shutdown(); UnityEngine.Object.DestroyImmediate(owner.gameObject); UnityEngine.Object.DestroyImmediate(observer.gameObject);
    }

    private static void LogOfferedRingBoundary636(string stage, RectTransform physical, RectTransform ring,
        Transform remotePhysical, RectTransform remoteRing)
    {
        var source = new Vector3[4]; var observed = new Vector3[4];
        ring.GetWorldCorners(source); remoteRing.GetWorldCorners(observed);
        string message = stage + " sourcePhase=" + (Quaternion.Inverse(physical.rotation) * ring.rotation).eulerAngles
            + " remotePhase=" + (Quaternion.Inverse(remotePhysical.rotation) * remoteRing.rotation).eulerAngles
            + " sourceLocalScale=" + ring.localScale.ToString("F8") + " remoteLocalScale=" + remoteRing.localScale.ToString("F8")
            + " sourceLocalRotation=" + ring.localRotation.ToString("F8") + " remoteLocalRotation=" + remoteRing.localRotation.ToString("F8") + "\n";
        var peers = (IDictionary)typeof(TownServiceMirror).GetField("Remote", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object module = ((IDictionary)peers[1]!)[(ushort)10]!;
        var motion = (TownServiceMotion)module.GetType().GetField("Motion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
        var nodes = (Array)typeof(TownServiceMotion).GetField("_nodes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(motion)!;
        var targets = (Array)typeof(TownServiceMotion).GetField("_to", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(motion)!;
        var starts = (float[])typeof(TownServiceMotion).GetField("_nodeStarted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(motion)!;
        var durations = (float[])typeof(TownServiceMotion).GetField("_nodeDuration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(motion)!;
        var samples = (float[])typeof(TownServiceMotion).GetField("_nodeSampleTime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(motion)!;
        for (int node = 0; node < nodes.Length; node++)
        {
            object identity = nodes.GetValue(node)!;
            if (!ReferenceEquals(identity.GetType().GetField("Transform", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(identity), remoteRing)) continue;
            object target = targets.GetValue(node)!;
            message += "native drawing node=" + node + " started=" + starts[node] + " duration=" + durations[node]
                + " sample=" + samples[node] + " targetScale=" + target.GetType().GetField("Scale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)
                + " targetRotation=" + target.GetType().GetField("Rotation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target) + "\n";
        }
        for (int corner = 0; corner < 4; corner++)
            message += "corner=" + corner + " errorM=" + Vector3.Distance(
                remotePhysical.TransformPoint(physical.InverseTransformPoint(source[corner])), observed[corner]) + "\n";
        System.IO.File.AppendAllText(System.IO.Path.Combine(_output, "offered-final-clock636.log"), message);
    }
    private static Color32[] RenderOffered629(Transform canvas, RectTransform print, RectTransform ring,
        RectTransform area, int layer, string name)
    {
        // Include the enclosing converted canvas and the entire native aura.
        // The ordinary printed-row fixture has no external canvas or large ring.
        foreach (GameObject fixture in Objects) if (fixture != null) Layer(fixture.transform, 30);
        Layer(canvas, layer);
        foreach (Canvas child in canvas.GetComponentsInChildren<Canvas>(true)) child.worldCamera = _camera;
        _camera.cullingMask = 1 << layer;
        _camera.transform.SetPositionAndRotation(print.TransformPoint(print.rect.center) - print.forward * 10f, print.rotation);
        float vertical = 0f, horizontal = 0f; var corners = new Vector3[4];
        foreach (RectTransform ink in new[] {print, ring, area})
        {
            ink.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            { Vector3 local = _camera.transform.InverseTransformPoint(corner);
              vertical = Mathf.Max(vertical, Mathf.Abs(local.y)); horizontal = Mathf.Max(horizontal, Mathf.Abs(local.x)); }
        }
        _camera.orthographicSize = Mathf.Max(vertical, horizontal * 384f / 512f) * 1.15f;
        Canvas.ForceUpdateCanvases();
        var target = new RenderTexture(512, 384, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var image = new Texture2D(512, 384, TextureFormat.RGBA32, false);
        try
        {
            _camera.targetTexture = target; _camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 512, 384), 0, 0); image.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(_output, name + ".png"), image.EncodeToPNG());
            Color32[] pixels = image.GetPixels32(); bool clear = true;
            for (int x = 0; x < 512; x++) clear &= pixels[x].Equals(pixels[0]) && pixels[383 * 512 + x].Equals(pixels[0]);
            for (int y = 0; y < 384; y++) clear &= pixels[y * 512].Equals(pixels[0]) && pixels[y * 512 + 511].Equals(pixels[0]);
            Check(clear, "entire original aura and printed areas are fully framed in owner and observer renders");
            return pixels;
        }
        finally
        {
            RenderTexture.active = null; _camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
        }
    }

    private static bool IsFiniteOffered629(Vector3 scale) => !float.IsNaN(scale.x) && !float.IsInfinity(scale.x)
        && !float.IsNaN(scale.y) && !float.IsInfinity(scale.y) && !float.IsNaN(scale.z) && !float.IsInfinity(scale.z);

    private static void CheckOfferedCorners629(RectTransform originalPrint, RectTransform originalInk,
        Transform admittedPrint, RectTransform admittedInk, string message)
    {
        var from = new Vector3[4]; var to = new Vector3[4];
        originalInk.GetWorldCorners(from); admittedInk.GetWorldCorners(to);
        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 expected = admittedPrint.TransformPoint(originalPrint.InverseTransformPoint(from[corner]));
            float distance = Vector3.Distance(to[corner], expected);
            if (distance >= .00003f)
                System.IO.File.AppendAllText(System.IO.Path.Combine(_output, "geometry-failure.txt"),
                    _offeredContext629 + " corner=" + corner + " distance=" + distance + " expected=" + expected.ToString("F6") + " observed=" + to[corner].ToString("F6")
                    + " sourceInk=" + originalInk.localToWorldMatrix.ToString("F7") + " copiedInk=" + admittedInk.localToWorldMatrix.ToString("F7")
                    + " copiedPrint=" + admittedPrint.localToWorldMatrix.ToString("F7") + " sourcePrint=" + originalPrint.localToWorldMatrix.ToString("F7") + "\n");
            Check(distance < .00003f, message);
        }
    }

    private static void CheckOfferedPlane629(RectTransform originalPrint, RectTransform originalInk,
        Transform admittedPrint, RectTransform admittedInk, string message)
    {
        var from = new Vector3[4]; var to = new Vector3[4];
        originalInk.GetWorldCorners(from); admittedInk.GetWorldCorners(to);
        Vector3 expectedRight = admittedPrint.TransformVector(originalPrint.InverseTransformVector(from[3] - from[0]));
        Vector3 expectedUp = admittedPrint.TransformVector(originalPrint.InverseTransformVector(from[1] - from[0]));
        Vector3 expectedNormal = Vector3.Cross(expectedRight, expectedUp).normalized;
        Vector3 observedNormal = Vector3.Cross(to[3] - to[0], to[1] - to[0]).normalized;
        float error = Vector3.Distance(expectedNormal, observedNormal);
        if (error >= .00001f)
            System.IO.File.AppendAllText(System.IO.Path.Combine(_output, "geometry-failure.txt"),
                _offeredContext629 + " normal-error=" + error + " expected=" + expectedNormal.ToString("F7")
                + " observed=" + observedNormal.ToString("F7") + " sourceRingRotation=" + originalInk.localRotation.ToString("F7")
                + " copiedRingRotation=" + admittedInk.localRotation.ToString("F7") + "\n");
        Check(error < .00001f, message);
    }

}
