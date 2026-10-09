using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

internal static class GeometryClock661
{
    internal static bool Controlled;
    internal static float Now, Delta = .011f;
    internal static float Read => Controlled ? Now : Time.unscaledTime;
}

public static partial class MirrorProgram
{
    private const string Geometry661Invariant = "offered body and printed front preserve exact original geometry on every render";
    private static int _geometry661Frames;
    private static void RegisterPhysical661(Transform body, Transform print)
    {
        // The old-code causal control has no registration API. Keep its actual
        // production path intact and let the same rendered invariant reject it.
        typeof(TownServiceMirror).GetMethod("RegisterOfferedPhysical", PrivateStatic)?.Invoke(null, new object[] { body, print });
    }

    private static void RenderGeometry661(float now)
    {
        // Deterministic subframes use the same opening restore seam that the
        // production TickRemote calls before its ordinary interpolation pass.
        typeof(TownServiceMirror).GetMethod("RestoreOfferedPhysicalMounts", PrivateStatic)?.Invoke(null, null);
        OfferedRender629(now);
    }

    private static IEnumerator Geometry661()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); _offeredSequence629 = 661000; _geometry661Frames = 0;
        GeometryClock661.Controlled = false;
        Transform owner = Go("661 owner shared world").transform;
        Transform observer = Go("661 observer shared world").transform;
        owner.localScale = Vector3.one * .8f;
        owner.rotation = Quaternion.Euler(0f, 23f, 0f);
        observer.localScale = Vector3.one * 1.2f;
        observer.SetPositionAndRotation(new Vector3(7f, .2f, -.3f), Quaternion.Euler(0f, 79f, 0f));
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        Transform card = Go("661 real source card frame", owner).transform;
        card.localPosition = new Vector3(.2f, .4f, -.1f);
        VRCard ownerCard = card.gameObject.AddComponent<VRCard>();
        ownerCard.FixtureBacking(new Vector2(.14406f, .2205f));
        Transform body = card.Find("Visual/Backing");
        Check(body.GetComponent<MeshFilter>() != null && body.GetComponent<MeshRenderer>() != null,
            "geometry proof uses the actual procedural VRCard factory and complete CardMesh engine");
        RectTransform canvas = (RectTransform)Go("FaceCanvas", card).transform;
        canvas.localScale = Vector3.one * .00049f;
        canvas.localPosition = new Vector3(0f, 0f, -.0012f);
        canvas.sizeDelta = new Vector2(294f, 450f);
        canvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform physical = (RectTransform)Go("FullAbilityCard", canvas).transform;
        physical.sizeDelta = canvas.sizeDelta;
        physical.gameObject.AddComponent<CanvasGroup>();
        Image frontInk = Image("Actual front artwork", physical, Vector2.zero, physical.sizeDelta, new Color(.8f, .04f, .03f));
        Image("Actual front identity mark", physical, new Vector2(30f, 85f), new Vector2(57f, 44f), Color.white);

        RectTransform nativeCanvas = (RectTransform)Go("Actual converted native canvas", owner).transform;
        nativeCanvas.localScale = new Vector3(.00073f, .00116f, .001f);
        nativeCanvas.localRotation = Quaternion.Euler(0f, 27f, 11f);
        nativeCanvas.sizeDelta = new Vector2(680f, 660f);
        nativeCanvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform holder = (RectTransform)Go("CardHilight", nativeCanvas).transform;
        holder.sizeDelta = new Vector2(325f, 450f); holder.localScale = Vector3.one * .52f;
        holder.gameObject.AddComponent<CanvasGroup>();
        var highlighter = holder.gameObject.AddComponent<UIEnhancementCardHighlighter>();
        var nativeCard = Go("Native pooled card", holder).AddComponent<AbilityCardUI>();
        RectTransform nativePrint = (RectTransform)Go("FullAbilityCard", nativeCard.transform).transform;
        nativePrint.sizeDelta = physical.sizeDelta; nativePrint.localScale = Vector3.one * .84f;
        nativePrint.pivot = new Vector2(.31f, .68f); nativePrint.anchoredPosition = new Vector2(19f, -11f);
        nativeCard.fullAbilityCard = nativePrint; highlighter.Card = nativeCard;
        Image row = Image("Original selected row", nativePrint, new Vector2(47f, -62f), new Vector2(171f, 82f), Color.cyan);
        row.gameObject.AddComponent<UIEnhancementButtonHighlight>();
        RectTransform aura = (RectTransform)Go("Aura", holder).transform; aura.sizeDelta = new Vector2(600f, 600f);
        Image ring = Image("Original aura ink", aura, Vector2.zero, new Vector2(520f, 520f), new Color(.1f, .7f, 1f, .35f));
        TownServiceEnhancementHandoff.PhysicalCardFace = physical;
        var mask = nativeCard.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>(); mask.Mask(); mask.SendMessage("LateUpdate");
        RegisterPhysical661(body, physical);
        TownServiceMirror.RegisterMotionOffering(body, true); TownServiceMirror.RegisterMotionOffering(physical, true);
        TownServiceMirror.RegisterTemplate(3, 1, holder, address: "enchant.holder|");
        TownServiceMirror.RegisterTemplate(3, 2, physical, address: "face.661|");
        string bodyAddress = TownServiceAbilityBody.Key(ownerCard) + "|";
        TownServiceMirror.RegisterTemplate(3, 3, body, address: bodyAddress);
        TownServiceMirror.BeginSession(3, 661, owner, owner);
        TownServiceMirror.RegisterModule(10, 1, holder, address: "enchant.holder|");
        TownServiceMirror.RegisterModule(11, 2, physical, address: "face.661|");
        TownServiceMirror.RegisterModule(12, 3, body, address: bodyAddress);
        foreach (ushort module in new ushort[] { 10, 11, 12 }) TownServiceMirror.SetPriority(module, true);
        FastCapture initial = OfferedCapture629(); Receive(1, initial.Artwork); DeliverMotion(1, initial);
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding frontCopy = Remote(1, 11)!, bodyCopy = Remote(1, 12)!, holderCopy = Remote(1, 10)!;
        Check(frontCopy != null && bodyCopy != null && holderCopy != null,
            "actual original body, printed front and overlay are independently admitted before movement");
        Check(ReferenceEquals(body.GetComponent<MeshFilter>().sharedMesh, bodyCopy.Root.GetComponent<MeshFilter>().sharedMesh),
            "observer retains actual original body vertices and back-facing triangle partitions");
        RectTransform remoteRow = (RectTransform)holderCopy.Root.Find("Native pooled card/FullAbilityCard/Original selected row");
        RectTransform remoteRing = (RectTransform)holderCopy.Root.Find("Aura/Original aura ink");
        float clock = Time.unscaledTime + 1f;
        var delayed = new List<TownServiceMotionPacket>();
        for (int sample = 0; sample < 32; sample++)
        {
            yield return null;
            clock += .08f;
            // The first24 samples retain the actual scalar room-scale contract
            // and nonuniform native converted canvas. Eight further body-only
            // samples challenge complete affine/reflected print ancestry; they
            // do not certify the unrelated native overlay world-TRS protocol.
            owner.localScale = sample < 24 ? Vector3.one * .8f : new Vector3(.8f, .69f, .92f);
            observer.localScale = sample < 24 ? Vector3.one * 1.2f
                : sample < 28 ? new Vector3(1.2f, .93f, 1.1f) : new Vector3(-1.2f, .93f, 1.1f);
            card.localPosition = new Vector3(.2f, .4f + .006f * Mathf.Sin(sample * .45f), -.1f);
            card.localRotation = Quaternion.Euler(4f * Mathf.Sin(sample * .3f), 55f + sample * 21f, 2f);
            card.localScale = Vector3.one * (sample % 3 == 0 ? .85f : sample % 3 == 1 ? 1f : 1.15f);
            row.color = sample % 2 == 0 ? Color.cyan : Color.green;
            frontInk.color = sample % 2 == 0 ? new Color(.8f, .04f, .03f) : new Color(.7f, .1f, .02f);
            mask.SendMessage("LateUpdate");
            FastCapture capture = OfferedCapture629();
            // Keep real source records and the production codec. Only actual
            // transport loss/order/jitter is controlled; no geometry is injected.
            foreach (TownServiceMotionPacket prior in delayed)
            {
                prior.Sequence = ++_offeredSequence629; prior.SampleTime = clock - .08f;
                ReceiveGeometry661(prior, clock);
            }
            delayed.Clear();
            foreach (byte[] bytes in capture.Motion)
            {
                Check(TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var packet), "real offered geometry decodes from bounded numeric packet");
                var late = new TownServiceMotionPacket { SampleTime = clock };
                for (int entry = packet!.Entries.Count - 1; entry >= 0; entry--)
                {
                    TownServiceMotionEntry value = packet.Entries[entry];
                    if (value.Module == 12 && value.Kind == 1 && sample % 3 != 0)
                    { late.Entries.Add(value); packet.Entries.RemoveAt(entry); }
                    else if (value.Module == 12 && value.Kind == 9 && sample % 4 != 0)
                        packet.Entries.RemoveAt(entry); // exact relative geometry remains unchanged
                }
                if (late.Entries.Count != 0) delayed.Add(late);
                packet.Sequence = ++_offeredSequence629; packet.SampleTime = clock;
                ReceiveGeometry661(packet, clock);
            }
            for (int render = 0; render < 7; render++)
            {
                float now = clock + render * .011f;
                RenderGeometry661(now); Canvas.ForceUpdateCanvases(); _geometry661Frames++;
                float error = GeometryError661(body, physical, bodyCopy.Root, frontCopy.Root);
                if (error >= .00005f)
                {
                    File.AppendAllText(Path.Combine(_output, "geometry661-failure.txt"),
                        "sample=" + sample + " render=" + render + " maxVertexDistance=" + error.ToString("F8")
                        + " bodyRotation=" + bodyCopy.Root.rotation.eulerAngles + " frontRotation=" + frontCopy.Root.rotation.eulerAngles + "\n");
                    RenderPhysical661(body, physical, 8, false, "failure-owner");
                    RenderPhysical661(bodyCopy.Root, frontCopy.Root, 9, false, "failure-observer");
                }
                Check(error < .00005f, Geometry661Invariant);
                if (sample < 24)
                {
                    CheckOfferedCorners629(physical, row.rectTransform, frontCopy.Root, remoteRow,
                        "native enhancement overlay stays registered to coherent body and print during owner rotation");
                    CheckOfferedPlane629(physical, ring.rectTransform, frontCopy.Root, remoteRing,
                        "stable offered ring retains exact printed plane throughout body and front rotation");
                }
                foreach (bool back in new[] { false, true })
                {
                    string label = "frame-" + _geometry661Frames + (back ? "-back" : "-front");
                    Color32[] reference = RenderPhysical661(body, physical, 8, back, sample % 8 == 0 && render == 3 ? label + "-owner" : null);
                    Color32[] observed = RenderPhysical661(bodyCopy.Root, frontCopy.Root, 9, back, sample % 8 == 0 && render == 3 ? label + "-observer" : null);
                    int changed = 0, ink = 0;
                    for (int i = 0; i < reference.Length; i++)
                    {
                        if (reference[i].r > 40 || reference[i].g > 40 || reference[i].b > 40) ink++;
                        bool originalInk = reference[i].r > 40 || reference[i].g > 40 || reference[i].b > 40;
                        bool receivedInk = observed[i].r > 40 || observed[i].g > 40 || observed[i].b > 40;
                        if (originalInk != receivedInk) changed++;
                    }
                    Check(ink > 3000, "actual original card submits visible ink from its front and opposing native back");
                    // Front content colors have their own allowed interpolation.
                    // Compare silhouette/occlusion pixels, preserving full native materials.
                    if (changed >= 80)
                    {
                        File.AppendAllText(Path.Combine(_output, "geometry661-raster-failure.txt"), label + " changed=" + changed + " ink=" + ink + "\n");
                        RenderPhysical661(body, physical, 8, back, "raster-failure-owner");
                        RenderPhysical661(bodyCopy.Root, frontCopy.Root, 9, back, "raster-failure-observer");
                    }
                    Check(changed < 80, "coherent physical card retains native front/back silhouette on every camera render");
                }
            }
        }
        IEnumerator lifecycle = PhysicalLifecycle661(owner, observer, card, ownerCard, body, canvas, physical,
            holder, mask, frontCopy, bodyCopy, clock);
        while (lifecycle.MoveNext()) yield return lifecycle.Current;
        File.WriteAllText(Path.Combine(_output, "geometry661-receipt.txt"), "renders=" + _geometry661Frames + "\nassertions=" + _assertions + "\n");
        TownServiceEnhancementHandoff.PhysicalCardFace = null; TownServiceMirror.Shutdown();
        GeometryClock661.Controlled = false; VRHands.Left = VRHands.Right = null; NetAvatarDriver.MotionHandFrames.Clear();
    }

    private static int _geometry661Returns, _geometry661Hands, _geometry661Reoffers;
    private static TownServiceMotionEntry? _geometry661OldAffinity;

    private static List<TownServiceMotionPacket> CaptureNative661()
    {
        typeof(TownServiceMirror).GetField("_nextMotionSend", PrivateStatic)!.SetValue(null, 0f);
        var packets = new List<TownServiceMotionPacket>();
        TownServiceMirror.CaptureMotion((bytes, length, _) =>
        {
            Check(TownServiceMotionCodec.TryRead(bytes, length, out var packet), "actual native lifecycle capture retains bounded wire records");
            packets.Add(packet!);
        });
        return packets;
    }

    private static void DeliverNative661(List<TownServiceMotionPacket> packets, bool loseWithdrawal)
    {
        foreach (TownServiceMotionPacket packet in packets)
        {
            for (int i = packet.Entries.Count - 1; i >= 0; i--)
            {
                TownServiceMotionEntry entry = packet.Entries[i];
                if (entry.Kind == 9 && entry.Module == 12 && entry.Visible) _geometry661OldAffinity = entry;
                if (loseWithdrawal && entry.Kind == 9 && (entry.Module == 12 || entry.Module == 16) && !entry.Visible) packet.Entries.RemoveAt(i);
            }
            packet.Sequence = ++_offeredSequence629;
            ReceiveGeometry661(packet, GeometryClock661.Now);
        }
    }

    private static void LifecyclePicture661(Transform sourceBody, Transform sourcePrint, Transform observerBody,
        Transform observerPrint, string invariant, string phase, int frame)
    {
        float error = GeometryError661(sourceBody, sourcePrint, observerBody, observerPrint);
        if (error >= .00005f)
        {
            File.AppendAllText(Path.Combine(_output, "geometry661-lifecycle-failure.txt"), phase + "," + frame + "," + error + "\n");
            File.AppendAllText(Path.Combine(_output, "geometry661-lifecycle-failure.txt"),
                "source body/print " + sourceBody.position.ToString("F7") + " / " + sourcePrint.position.ToString("F7")
                + " scales " + sourceBody.lossyScale.ToString("F7") + " / " + sourcePrint.lossyScale.ToString("F7")
                + " remote body/print " + observerBody.position.ToString("F7") + " / " + observerPrint.position.ToString("F7")
                + " scales " + observerBody.lossyScale.ToString("F7") + " / " + observerPrint.lossyScale.ToString("F7")
                + " local body/print " + observerBody.localPosition.ToString("F7") + " / " + observerPrint.localPosition.ToString("F7")
                + " parents " + observerBody.parent.name + " / " + observerPrint.parent.name + "\n");
            RenderPhysical661(sourceBody, sourcePrint, 8, false, "lifecycle-failure-owner");
            RenderPhysical661(observerBody, observerPrint, 9, false, "lifecycle-failure-observer");
        }
        Check(error < .00005f, invariant);
        Check(observerBody.gameObject.activeInHierarchy && observerPrint.gameObject.activeInHierarchy,
            "actual offered, returned and held originals never blink during native ownership transitions");
        foreach (bool back in new[] { false, true })
        {
            Color32[] reference = RenderPhysical661(sourceBody, sourcePrint, 8, back, null);
            Color32[] observed = RenderPhysical661(observerBody, observerPrint, 9, back,
                frame % 20 == 0 ? phase + "-" + frame + (back ? "-back" : "-front") : null);
            int union = 0, changed = 0;
            for (int i = 0; i < reference.Length; i++)
            {
                bool a = reference[i].r > 40 || reference[i].g > 40 || reference[i].b > 40;
                bool b = observed[i].r > 40 || observed[i].g > 40 || observed[i].b > 40;
                if (a || b) union++;
                if (a != b) changed++;
            }
            Check(union > 3000 && changed < 80, "native lifecycle preserves actual opposing body/front silhouettes on every render");
        }
    }

    private static IEnumerator PhysicalLifecycle661(Transform owner, Transform observer, Transform card, VRCard native,
        Transform body, RectTransform canvas, RectTransform print, RectTransform holder,
        TownServiceNativeEnhancementCardMask mask, TownServiceBinding frontCopy, TownServiceBinding bodyCopy, float previousClock)
    {
        owner.localScale = Vector3.one * .8f; observer.localScale = Vector3.one * 1.2f;
        GeometryClock661.Controlled = true; GeometryClock661.Now = previousClock + .25f;
        for (int budget = 0; budget < 8; budget++) DeliverNative661(CaptureNative661(), false);
        RenderGeometry661(GeometryClock661.Now);
        // The preceding synthetic sheared-room extension is a body-affinity
        // challenge only. Reestablish the actual scalar room Canvas through its
        // ordinary production interpolation before testing native return TRS.
        GeometryClock661.Now += .4f; RenderGeometry661(GeometryClock661.Now);
        Check(GeometryError661(body, print, bodyCopy.Root, frontCopy.Root) < .00005f,
            "actual scalar room offered picture is coherent before native ownership transfer");
        Check(_geometry661OldAffinity != null, "lifecycle starts from a genuine still-visible source body affinity");
        // Stop the real source mask. Drop its actual withdrawal on the transport
        // only: the observer still holds the older visible109 during the return.
        mask.Restore();
        canvas.localScale *= .83f; canvas.localPosition += new Vector3(-.004f, .003f, 0f);
        native.BeginNative661(owner.TransformPoint(new Vector3(-.3f, .25f, .22f)), .35f);
        TownServiceMirror.RegisterCardReturn(card, native.TryTownReturnMotion);
        bool began = false, ended = false;
        for (int frame = 0; frame < 72; frame++)
        {
            GeometryClock661.Now += GeometryClock661.Delta;
            if (native.NativeFlying661) native.StepNative661();
            if (frame % 6 == 0 || frame == 32)
                for (int budget = 0; budget < 4; budget++) DeliverNative661(CaptureNative661(), true);
            RenderGeometry661(GeometryClock661.Now); Canvas.ForceUpdateCanvases();
            began |= TownServiceMirror.NativeReturnActive661(1, 12);
            if (began)
            {
                LifecyclePicture661(body, print, bodyCopy.Root, frontCopy.Root,
                    "native return supersedes lost old offered-body affinity on every render", "return", frame);
                CheckNativeWorld661(owner, observer, body, print, bodyCopy.Root, frontCopy.Root);
                _geometry661Returns++;
                ended |= !TownServiceMirror.NativeReturnActive661(1, 12);
            }
            yield return null;
        }
        Check(began && ended && _geometry661Returns >= 60, "real native cohort starts, completes and hands its original roots back to ordinary pose ownership");
        Check(TownServiceMirror.PhysicalMounts661 == 0, "terminal native return cannot inherit the old offered mount graph");

        var hand = new VRHand { Side = HandSide.Left, HasPose = true, WorldScale = .8f };
        hand.Rig.Root = Go("Actual left reclaim hand", owner).transform;
        hand.Rig.Root.localPosition = new Vector3(-.12f, .6f, -.2f);
        hand.Rig.GrabAnchor = Go("Actual left grab anchor", hand.Rig.Root).transform;
        VRHands.Left = hand; VRHands.Right = null;
        Transform remoteHand = Go("Observed left reclaim hand", observer).transform;
        remoteHand.localPosition = hand.Rig.Root.localPosition;
        NetAvatarDriver.MotionHandFrames[1] = new[] { remoteHand, remoteHand };
        native.ReclaimNative661(hand); card.SetParent(hand.Rig.GrabAnchor, true);
        TownServiceMirror.RegisterMotionHand(card, hand);
        for (int frame = 0; frame < 28; frame++)
        {
            GeometryClock661.Now += GeometryClock661.Delta;
            hand.Rig.Root.localPosition += new Vector3(.001f, .0006f, .0003f);
            hand.Rig.Root.localRotation = Quaternion.Euler(0f, frame * 3f, frame * .7f);
            remoteHand.localPosition = hand.Rig.Root.localPosition; remoteHand.localRotation = hand.Rig.Root.localRotation;
            if (frame % 3 == 0) for (int budget = 0; budget < 4; budget++) DeliverNative661(CaptureNative661(), true);
            RenderGeometry661(GeometryClock661.Now); Canvas.ForceUpdateCanvases();
            LifecyclePicture661(body, print, bodyCopy.Root, frontCopy.Root,
                "reclaimed hand motion supersedes the obsolete offered body relation on every render", "reclaim", frame);
            CheckNativeWorld661(owner, observer, body, print, bodyCopy.Root, frontCopy.Root);
            Check(TownServiceMirror.PhysicalMounts661 == 0, "actual held root owns its original body without an obsolete offer mount");
            _geometry661Hands++; yield return null;
        }

        // A different native card reuses the same backing recipe/address, never
        // the old print identity. Census retirement must happen before its first
        // mount and cannot destroy a backing still parented beneath an old face.
        TownServiceMirror.UnregisterModule(11); TownServiceMirror.UnregisterModule(12);
        native.Holder = null; card.gameObject.SetActive(false);
        Transform replacement = Go("Second actual source card", owner).transform;
        replacement.localPosition = new Vector3(.15f, .36f, -.08f);
        var nextCard = replacement.gameObject.AddComponent<VRCard>(); nextCard.FixtureBacking(new Vector2(.14406f, .2205f));
        Transform nextBody = replacement.Find("Visual/Backing");
        RectTransform nextCanvas = (RectTransform)Go("FaceCanvas", replacement).transform;
        nextCanvas.sizeDelta = new Vector2(294f, 450f); nextCanvas.localScale = Vector3.one * .00049f;
        nextCanvas.localPosition = new Vector3(0f, 0f, -.0012f);
        nextCanvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform nextPrint = (RectTransform)Go("FullAbilityCard", nextCanvas).transform;
        nextPrint.sizeDelta = nextCanvas.sizeDelta; nextPrint.gameObject.AddComponent<CanvasGroup>();
        Image("Second actual blue front", nextPrint, Vector2.zero, nextPrint.sizeDelta, Color.blue);
        TownServiceEnhancementHandoff.PhysicalCardFace = nextPrint; mask.Mask(); mask.SendMessage("LateUpdate");
        RegisterPhysical661(nextBody, nextPrint); TownServiceMirror.RegisterMotionOffering(nextBody, true);
        TownServiceMirror.RegisterMotionOffering(nextPrint, true);
        TownServiceMirror.RegisterTemplate(3, 4, nextPrint, address: "face.662|");
        TownServiceMirror.RegisterModule(15, 4, nextPrint, address: "face.662|");
        TownServiceMirror.RegisterModule(16, 3, nextBody, address: TownServiceAbilityBody.Key(nextCard) + "|");
        TownServiceMirror.SetPriority(15, true); TownServiceMirror.SetPriority(16, true);
        FastCapture reoffer = OfferedCapture629(); Receive(1, reoffer.Artwork); DeliverNative661(DecodeNative661(reoffer.Motion), false);
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(1, 11) == null && Remote(1, 12) == null, "new exact source census retires the first body and print before reoffer");
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding newFront = Remote(1, 15)!, newBody = Remote(1, 16)!;
        Check(newFront != null && newBody != null, "new native card prepares its own original print and exact backing recipe");
        var obsolete = new TownServiceMotionPacket { SampleTime = previousClock, Sequence = ++_offeredSequence629 };
        obsolete.Entries.Add(_geometry661OldAffinity!); ReceiveGeometry661(obsolete, GeometryClock661.Now);
        for (int frame = 0; frame < 21; frame++)
        {
            GeometryClock661.Now += GeometryClock661.Delta;
            replacement.localRotation = Quaternion.Euler(5f, frame * 17f, 3f);
            replacement.localPosition += new Vector3(.0002f, .0004f * Mathf.Sin(frame), 0f);
            mask.SendMessage("LateUpdate");
            if (frame % 3 == 0) for (int budget = 0; budget < 4; budget++) DeliverNative661(CaptureNative661(), false);
            RenderGeometry661(GeometryClock661.Now); Canvas.ForceUpdateCanvases();
            LifecyclePicture661(nextBody, nextPrint, newBody.Root, newFront.Root,
                "new actual source identity alone owns the backing despite an obsolete visible109", "reoffer", frame);
            Check(Remote(1, 11) == null && Remote(1, 12) == null, "late old-body affinity cannot revive either retired original");
            _geometry661Reoffers++; yield return null;
        }
        Check(TownServiceMirror.PhysicalMounts661 == 1, "retirement challenge starts with a real backing mounted below its current original print");
        Transform retainedBody = newBody.Root;
        TownServiceMirror.UnregisterModule(15);
        TownServiceMirror.RegisterModule(17, 4, nextPrint, address: "face.662|"); TownServiceMirror.SetPriority(17, true);
        FastCapture remap = OfferedCapture629(); Receive(1, remap.Artwork);
        Check(retainedBody != null && retainedBody.gameObject.activeInHierarchy && ReferenceEquals(Remote(1, 16)!.Root, retainedBody),
            "native census preserves the visible mounted backing before retiring its old printed parent");
        DeliverNative661(DecodeNative661(remap.Motion), false); TownServiceMirror.TickRemote(_ => observer);
        newFront = Remote(1, 17)!;
        Check(Remote(1, 15) == null && newFront != null && ReferenceEquals(Remote(1, 16)!.Root, retainedBody),
            "original print identity remap retains the exact existing body and creates only the declared new print");
        for (int budget = 0; budget < 4; budget++) DeliverNative661(CaptureNative661(), false);
        GeometryClock661.Now += .2f; RenderGeometry661(GeometryClock661.Now); Canvas.ForceUpdateCanvases();
        LifecyclePicture661(nextBody, nextPrint, retainedBody, newFront.Root,
            "retained physical backing immediately follows only its replacement native print", "census", 0);
        mask.Restore();
        Check(TownServiceMirror.PhysicalSources661 == 0, "actual source withdrawal releases all pooled physical offer references");
        nextCanvas.localPosition += new Vector3(.003f, -.002f, 0f);
        nextCard.ReclaimNative661(hand); replacement.SetParent(hand.Rig.GrabAnchor, true);
        TownServiceMirror.RegisterMotionHand(replacement, hand);
        for (int frame = 0; frame < 18; frame++)
        {
            GeometryClock661.Now += GeometryClock661.Delta;
            hand.Rig.Root.localPosition += Vector3.right * .001f;
            remoteHand.localPosition = hand.Rig.Root.localPosition;
            if (frame % 3 == 0) for (int budget = 0; budget < 4; budget++) DeliverNative661(CaptureNative661(), true);
            RenderGeometry661(GeometryClock661.Now); Canvas.ForceUpdateCanvases();
            LifecyclePicture661(nextBody, nextPrint, newBody.Root, newFront.Root,
                "direct hand reclaim supersedes lost old offered-body affinity on every render", "direct-reclaim", frame);
            CheckNativeWorld661(owner, observer, nextBody, nextPrint, newBody.Root, newFront.Root);
            Check(TownServiceMirror.PhysicalMounts661 == 0, "a hand without any previous return clock supersedes the still-visible old offer");
            _geometry661Hands++; yield return null;
        }
        GeometryClock661.Now += TownServiceMotionCodec.Heartbeat + .01f;
        bool withdrawn = false;
        for (int budget = 0; budget < 8; budget++)
        {
            var packets = CaptureNative661();
            foreach (var packet in packets) foreach (var entry in packet.Entries)
                if (entry.Module == 16 && entry.Kind == 9 && !entry.Visible) withdrawn = true;
            DeliverNative661(packets, false);
        }
        RenderGeometry661(GeometryClock661.Now);
        Check(withdrawn, "ending the exact native offer withdraws physical body affinity within eight actual bounded budget turns");
        Check(TownServiceMirror.PhysicalMounts661 == 0, "withdrawal releases the final physical mount without keeping stale buttons or artwork");
        File.WriteAllText(Path.Combine(_output, "geometry661-lifecycle-receipt.txt"),
            "native-return-renders=" + _geometry661Returns + "\nheld-renders=" + _geometry661Hands + "\nnew-card-renders=" + _geometry661Reoffers + "\n");
    }

    private static List<TownServiceMotionPacket> DecodeNative661(List<byte[]> bytes)
    {
        var packets = new List<TownServiceMotionPacket>();
        foreach (byte[] wire in bytes) { TownServiceMotionCodec.TryRead(wire, wire.Length, out var packet); packets.Add(packet!); }
        return packets;
    }

    private static void CheckNativeWorld661(Transform owner, Transform observer, Transform body, RectTransform print,
        Transform remoteBody, Transform remotePrint)
    {
        float error = 0f;
        foreach (Vector3 vertex in body.GetComponent<MeshFilter>().sharedMesh.vertices)
            error = Mathf.Max(error, Vector3.Distance(observer.TransformPoint(owner.InverseTransformPoint(body.TransformPoint(vertex))),
                remoteBody.TransformPoint(vertex)));
        var corners = new Vector3[4]; var remote = new Vector3[4];
        print.GetWorldCorners(corners); ((RectTransform)remotePrint).GetWorldCorners(remote);
        for (int i = 0; i < 4; i++) error = Mathf.Max(error, Vector3.Distance(observer.TransformPoint(owner.InverseTransformPoint(corners[i])), remote[i]));
        Check(error < .00005f, "every returned and held native world vertex follows the actual source curve and owner hand, error=" + error);
    }

    private static void ReceiveGeometry661(TownServiceMotionPacket packet, float receipt)
    {
        byte[] wire = TownServiceMotionCodec.TryWritePacked(packet)!;
        Check(wire != null && TownServiceMotionCodec.TryRead(wire, wire.Length, out var decoded), "jittered actual offered records retain exact wire geometry");
        TownServiceMotionCodec.TryRead(wire, wire.Length, out var received);
        Check(TownServiceMirror.ReceiveMotion(1, received!), "actual offered receiver accepts reordered source records");
        float authored = received!.SampleTime; received.SampleTime = receipt; OfferedReceiptClock629(received); received.SampleTime = authored;
    }

    private static float GeometryError661(Transform body, Transform print, Transform remoteBody, Transform remotePrint)
    {
        float error = 0f;
        foreach (Vector3 vertex in body.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            Vector3 expected = remotePrint.TransformPoint(print.InverseTransformPoint(body.TransformPoint(vertex)));
            error = Mathf.Max(error, Vector3.Distance(expected, remoteBody.TransformPoint(vertex)));
        }
        return error;
    }

    private static Color32[] RenderPhysical661(Transform body, Transform print, int layer, bool back, string? name)
    {
        foreach (GameObject fixture in Objects) if (fixture != null) Layer(fixture.transform, 30);
        Layer(body, layer); Layer(print, layer);
        foreach (Canvas canvas in print.GetComponentsInParent<Canvas>(true)) canvas.gameObject.layer = layer;
        _camera.cullingMask = 1 << layer;
        Vector3 center = print.TransformPoint(((RectTransform)print).rect.center);
        Quaternion view = back ? print.rotation * Quaternion.Euler(0f, 180f, 0f) : print.rotation;
        _camera.transform.SetPositionAndRotation(center - view * Vector3.forward * 2f, view);
        Vector3[] corners = new Vector3[4]; ((RectTransform)print).GetWorldCorners(corners);
        _camera.orthographicSize = ((RectTransform)print).rect.height * .65f;
        // Normalize only the camera to the actual complete print matrix. World
        // vertices are independently checked before this readback. This exposes
        // body/print misregistration even under nonuniform/reflected ancestors,
        // without mistaking differing shared-room scale for different source ink.
        Matrix4x4 localView = Matrix4x4.TRS(new Vector3(0f, 0f, back ? 10f : -10f),
            back ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity, Vector3.one);
        _camera.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * localView.inverse * print.worldToLocalMatrix;
        Canvas.ForceUpdateCanvases();
        var target = new RenderTexture(256, 192, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var image = new Texture2D(256, 192, TextureFormat.RGBA32, false);
        bool previousCulling = GL.invertCulling;
        try
        {
            // A reflected custom view matrix is a mirror camera. Its winding
            // correction belongs to this normalization camera, never the native
            // backing material or production world transform.
            GL.invertCulling = previousCulling ^ (print.localToWorldMatrix.determinant < 0f);
            _camera.targetTexture = target; _camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 256, 192), 0, 0); image.Apply();
            if (name != null) File.WriteAllBytes(Path.Combine(_output, name + ".png"), image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            RenderTexture.active = null; _camera.targetTexture = null;
            GL.invertCulling = previousCulling;
            _camera.ResetWorldToCameraMatrix();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
        }
    }
}

namespace GloomhavenVR.Net.TownServices
{
    internal static partial class TownServiceMirror
    {
        internal static bool NativeReturnActive661(int peer, ushort id) => Remote.TryGetValue(peer, out var modules)
            && modules.TryGetValue(id, out var module) && MotionRemoteFrames.TryGetValue(module, out var motion) && motion.HadCardReturn;
        internal static int PhysicalMounts661 => (typeof(TownServiceMirror).GetField("OfferedPhysicalMounts", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as IDictionary)?.Count ?? 0;
        internal static int PhysicalSources661 => (typeof(TownServiceMirror).GetField("OfferedPhysicalFrames", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as IDictionary)?.Count ?? 0;
    }
}
