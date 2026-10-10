using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
internal static class HoverClock666
{
    internal static bool Controlled;
    internal static float Now;
    internal const float Delta = 1f / 90f;
    internal static float Read => Controlled ? Now : Time.unscaledTime;
}
public static partial class MirrorProgram
{
    private static object? HoverRecipe666(TownServiceFrame frame) => typeof(TownServiceFrame)
        .GetField("OfferedHover", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(frame);
    private static T HoverValue666<T>(object recipe, string field) => (T)recipe.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(recipe)!;
    private static TownServiceFrame HoverHeader666(FastCapture capture, ushort module)
    {
        foreach (byte[] bytes in capture.Artwork)
            if (TownServiceCodec.TryRead(bytes, bytes.Length, out var frame) && frame!.Module == module) return frame;
        throw new InvalidOperationException("Actual original header absent for module " + module);
    }
    private static uint CheckHoverEpoch666(FastCapture capture, params ushort[] modules)
    {
        // Legacy665 has no117; its untouched receive/render path reaches the
        // quantitative first-cold wave assertion rather than a DTO compile error.
        if (typeof(TownServiceFrame).GetField("OfferedHover", BindingFlags.Instance | BindingFlags.NonPublic) == null) return 0;
        uint epoch = 0;
        foreach (ushort id in modules)
        {
            object? recipe = HoverRecipe666(HoverHeader666(capture, id));
            Check(recipe != null, "actual native original publishes its intrinsic recipe module=" + id);
            uint current = HoverValue666<uint>(recipe!, "Epoch");
            Check(current != 0 && (epoch == 0 || current == epoch),
                "front body and linked canvas use one native accepted epoch despite mixed sampler registration");
            epoch = current;
        }
        return epoch;
    }
    private static bool? OfferedGuard666(int peer, ushort id)
    {
        var pictures = typeof(TownServiceMirror).GetField("HoverPictures", BindingFlags.Static | BindingFlags.NonPublic);
        if (pictures == null) return null;
        var remote = (IDictionary)typeof(TownServiceMirror).GetField("Remote", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object module = ((IDictionary)remote[peer]!)[id]!;
        var peers = (IDictionary)typeof(TownServiceMirror).GetField("MotionPeers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var slots = (IDictionary)peers[peer]!.GetType().GetField("Slots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peers[peer])!;
        foreach (object slot in slots.Values)
        {
            var entry = (TownServiceMotionEntry)slot.GetType().GetField("Entry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(slot)!;
            if (entry.Kind == 1 && entry.Lane == 0 && entry.Module == id)
                return (bool)typeof(TownServiceMirror).GetMethod("ContinuousOfferedRoot", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new[] {(object)peer, module, slot, HoverClock666.Now})!;
        }
        throw new InvalidOperationException("Actual received offered Kind1 absent for floor proof");
    }
    private static void HoverCanvas666()
    {
        if (typeof(TownServiceFrame).GetField("OfferedHover", BindingFlags.Instance | BindingFlags.NonPublic) == null) return;
        foreach (bool externalStatic in new[] {false, true})
        foreach (bool nearestDisabled in new[] {false, true})
        {
            TownServiceMirror.Shutdown(); Baselines.Clear(); HoverClock666.Controlled = true; HoverClock666.Now += 1f;
            Transform shared = Go("Actual canvas hover source").transform;
            Transform palm = Go("Actual canvas hover palm", shared).transform;
            RectTransform staticRoot = (RectTransform)Go("Static root canvas", shared).transform;
            staticRoot.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            staticRoot.localPosition = new Vector3(.3f, .2f, -.4f);
            Transform seat = Go("Actual canvas hover seat", externalStatic ? staticRoot : shared).transform;
            Transform card = Go("Actual canvas hover accepted card", seat).transform;
            RectTransform movingCanvas = (RectTransform)Go("Native card canvas", card).transform;
            Canvas moving = movingCanvas.gameObject.AddComponent<Canvas>(); moving.renderMode = RenderMode.WorldSpace;
            movingCanvas.localScale = Vector3.one * .001f;
            RectTransform source = (RectTransform)Go("Native nested source canvas", movingCanvas).transform;
            Canvas nested = source.gameObject.AddComponent<Canvas>(); nested.renderMode = RenderMode.WorldSpace;
            nested.enabled = !nearestDisabled;
            source.sizeDelta = new Vector2(294, 450); source.gameObject.AddComponent<CanvasGroup>();
            Image("Exact original nested content", source, Vector2.zero, source.sizeDelta, Color.red);
            const float age = .31f;
            TownServiceOfferingPose.Place(seat, palm, shared, age);
            new TownServiceOfferingCard(card, seat, 1f).Tick();
            TownServiceMirror.RegisterMotionOffering(source, true);
            TownServiceMirror.RegisterTemplate(1, 1, source, address: "item.667|");
            TownServiceMirror.BeginSession(1, 667, shared, shared); TownServiceMirror.SetLocalTransactionActive(1, true);
            TownServiceMirror.RegisterModule(11, 1, source, address: "item.667|"); TownServiceMirror.SetPriority(11, true);
            FastCapture capture = OfferedCapture629(); TownServiceFrame frame = HoverHeader666(capture, 11);
            object recipe = HoverRecipe666(frame)!;
            Check(recipe != null && frame.HasCanvasFrame, "actual nested source captures its original active root canvas and hover metadata");
            Check(HoverValue666<bool>(recipe, "CanvasFollowsHover") == !externalStatic,
                "hover recipe identifies the exact transmitted active root canvas instead of its nearest nested or disabled canvas");
            Vector3 offset = Vector3.up * (.006f * Mathf.Sin(age * 1.8f));
            Vector3 canonicalRoot = shared.InverseTransformPoint(source.position - offset);
            Vector3 sentRoot = new Vector3(frame.Pose[0], frame.Pose[1], frame.Pose[2]);
            Check(Vector3.Distance(canonicalRoot, sentRoot) < .000002f,
                "full original nested physical pose strips exactly native Place displacement");
            Transform actualCanvas = externalStatic ? staticRoot : movingCanvas;
            Vector3 expectedCanvas = shared.InverseTransformPoint(actualCanvas.position - (externalStatic ? Vector3.zero : offset));
            Check(Vector3.Distance(expectedCanvas, new Vector3(frame.CanvasPose[0], frame.CanvasPose[1], frame.CanvasPose[2])) < .000002f,
                "static outside root canvas stays authored while the actual moving root canvas is canonicalized once");
            foreach (byte[] bytes in capture.Motion)
            {
                TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var packet);
                foreach (var entry in packet!.Entries) if (entry.Kind == 1 && entry.Module == 11)
                {
                    Check(Vector3.Distance(canonicalRoot, new Vector3(entry.Pose[0], entry.Pose[1], entry.Pose[2])) < .000002f,
                        "real97 numeric root uses the same exact canonical native source pose");
                    Check(Vector3.Distance(expectedCanvas, new Vector3(entry.CanvasPose[0], entry.CanvasPose[1], entry.CanvasPose[2])) < .000002f,
                        "real98 numeric canvas uses the exact full-header author and canonical base");
                }
            }
        }
    }
    private static IEnumerator Hover666()
    {
        foreach (byte service in new byte[] {3, 1})
        foreach (float stationScale in new[] {.75f, 1.4f})
        foreach (bool irregular in new[] {false, true})
        {
            TownServiceMirror.Shutdown(); Baselines.Clear(); HoverClock666.Controlled = false;
            Transform owner = Go("666 owner shared world").transform;
            Transform observer = Go("666 observer shared world").transform;
            observer.SetPositionAndRotation(new Vector3(7f, .2f, -.3f), Quaternion.Euler(0f, 79f, 0f));
            observer.localScale = Vector3.one * 1.2f;
            TownServiceMirror.SharedFrameForRemote = _ => observer;
            owner.localScale = Vector3.one * stationScale;
            owner.rotation = Quaternion.Euler(7f, 31f, 0f);
            Transform palm = Go("666 actual activity palm", owner).transform;
            palm.localPosition = new Vector3(.4f, .5f, -.2f);
            Transform seat = Go("666 actual offering seat", owner).transform;
            Transform card = Go("666 source card", seat).transform;
            VRCard actual = card.gameObject.AddComponent<VRCard>();
            actual.PrimeInactive666(7);
            actual.FixtureBacking(service == 3 ? new Vector2(.14406f, .2205f) : new Vector2(.14406f, .14406f));
            Transform body = card.Find("Visual/Backing");
            RectTransform canvas = (RectTransform)Go("FaceCanvas", card).transform;
            canvas.localScale = Vector3.one * .00049f;
            canvas.localPosition = new Vector3(0f, 0f, -.0012f);
            canvas.sizeDelta = service == 3 ? new Vector2(294f, 450f) : new Vector2(294f, 294f);
            canvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            RectTransform face = (RectTransform)Go("Actual native printed face", canvas).transform;
            face.sizeDelta = canvas.sizeDelta; face.gameObject.AddComponent<CanvasGroup>();
            Image ink = Image("Original printed artwork", face, Vector2.zero, face.sizeDelta, Color.red);
            Texture2D texture = new Texture2D(2, 2); texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); texture.Apply();
            Sprite first = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
            first.name = "666 first original";
            Sprite second = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
            second.name = "666 second original";
            Assets.Add(texture); Assets.Add(first); Assets.Add(second); ink.sprite = first;
            TownServiceNativeEnhancementCardMask? mask = null;
            RectTransform? holder = null, sourceRow = null, sourceRing = null;
            if (service == 3)
            {
                RectTransform nativeCanvas = (RectTransform)Go("Original converted enhancement canvas", owner).transform;
                nativeCanvas.localScale = new Vector3(.00073f, .00116f, .001f);
                nativeCanvas.localRotation = Quaternion.Euler(0f, 27f, 11f);
                nativeCanvas.sizeDelta = new Vector2(680f, 660f);
                nativeCanvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                holder = (RectTransform)Go("CardHilight", nativeCanvas).transform;
                holder.sizeDelta = new Vector2(325f, 450f); holder.localScale = Vector3.one * .52f;
                holder.gameObject.AddComponent<CanvasGroup>();
                var highlighter = holder.gameObject.AddComponent<UIEnhancementCardHighlighter>();
                var nativeCard = Go("Native pooled card", holder).AddComponent<AbilityCardUI>();
                RectTransform nativePrint = (RectTransform)Go("FullAbilityCard", nativeCard.transform).transform;
                nativePrint.sizeDelta = face.sizeDelta; nativePrint.localScale = Vector3.one * .84f;
                nativePrint.pivot = new Vector2(.31f, .68f); nativePrint.anchoredPosition = new Vector2(19f, -11f);
                nativeCard.fullAbilityCard = nativePrint; highlighter.Card = nativeCard;
                Image row = Image("Original selected row", nativePrint, new Vector2(47f, -62f), new Vector2(171f, 82f), Color.cyan);
                row.gameObject.AddComponent<UIEnhancementButtonHighlight>(); sourceRow = row.rectTransform;
                RectTransform aura = (RectTransform)Go("Aura", holder).transform;
                aura.sizeDelta = new Vector2(600f, 600f);
                sourceRing = Image("Original aura ink", aura, Vector2.zero, new Vector2(520f, 520f), new Color(.1f, .7f, 1f, .35f)).rectTransform;
                TownServiceEnhancementHandoff.PhysicalCardFace = face;
                mask = nativeCard.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>(); mask.Mask();
            }
            var head = VRRigDriver.HeadCamera!.transform;
            head.position = palm.position - Vector3.forward;
            TownServiceOfferingPose.Place(seat, palm, owner, 0f);
            mask?.SendMessage("LateUpdate");
            string faceAddress = service == 3 ? "face.666|" : "item.666|";
            string bodyAddress = service == 3 ? TownServiceAbilityBody.Key(actual) + "|" : "inspectionbody.3e138dbe.3e138dbe.p|";
            TownServiceMirror.RegisterMotionOffering(face, true); TownServiceMirror.RegisterMotionOffering(body, true);
            // Only the print has an actual native inactive sampler initially;
            // body and linked overlay must still identify the same native seat.
            TownServiceMirror.RegisterCardReturn(face, actual.TryTownReturnMotion);
            if (service == 3) TownServiceMirror.RegisterOfferedPhysical(body, face);
            TownServiceMirror.RegisterTemplate(service, 1, face, address: faceAddress);
            TownServiceMirror.RegisterTemplate(service, 2, body, address: bodyAddress);
            if (holder != null) TownServiceMirror.RegisterTemplate(service, 3, holder, address: "enchant.holder|");
            TownServiceMirror.BeginSession(service, 666, owner, owner);
            TownServiceMirror.SetLocalTransactionActive(service, true);
            TownServiceMirror.RegisterModule(11, 1, face, address: faceAddress);
            TownServiceMirror.RegisterModule(12, 2, body, address: bodyAddress);
            if (holder != null) { TownServiceMirror.RegisterModule(10, 3, holder, address: "enchant.holder|"); TownServiceMirror.SetPriority(10, true); }
            TownServiceMirror.SetPriority(11, true); TownServiceMirror.SetPriority(12, true);
            // Place and capture use the same actual native frame instant. The
            // observer is deliberately rotated/scaled; phase is its own lease.
            float began = Time.unscaledTime + 1f;
            HoverClock666.Controlled = true; HoverClock666.Now = began;
            TownServiceOfferingPose.Place(seat, palm, owner, 0f); mask?.SendMessage("LateUpdate");
            FastCapture initial = OfferedCapture629();
            uint initialEpoch = service == 3 ? CheckHoverEpoch666(initial, 11, 12, 10) : CheckHoverEpoch666(initial, 11, 12);
            Receive(1, initial.Artwork);
            File.WriteAllText(Path.Combine(_output,"initial-frames.txt"),string.Join("\n",initial.Artwork.ConvertAll(b=>{TownServiceCodec.TryRead(b,b.Length,out var f);return "module="+f!.Module+" visible="+f.Visible+" nodes="+f.Nodes.Length+" nativeBasis="+f.NativeTemplateBasisKey+" census="+string.Join(",",f.Modules)+" required="+string.Join(",",f.RequiredVisibleModules??Array.Empty<ushort>());})));
            float admissionStarted = Time.realtimeSinceStartup;
            int admissionFrames = 0;
            do
            {
                TownServiceMirror.TickRemote(_ => observer);
                if (Remote(1, 11) != null && Remote(1, 12) != null && TownServiceMirror.InteractionOwner(service) == 1) break;
                // The existing transaction election advances on native time,
                // not editor wall time. This cold test keeps numeric packets
                // delayed while its real .12-second lease matures normally.
                admissionFrames++; HoverClock666.Now += 1f / 90f;
                TownServiceOfferingPose.Place(seat, palm, owner, 0f);
                yield return null;
            } while (Time.realtimeSinceStartup - admissionStarted < 2f);
            TownServiceBinding copy = Remote(1, 11)!;
            TownServiceBinding bodyCopy = Remote(1, 12)!;
            File.WriteAllText(Path.Combine(_output, "first-cold-receipt.txt"), "service="+service+" admissionFrames="+admissionFrames+" admissionSeconds="+(Time.realtimeSinceStartup-admissionStarted)+" face="+(copy!=null)+" body="+(bodyCopy!=null)+"\n"+string.Join("\n",GloomhavenVR.Core.VRLog.Messages));
            Check(copy != null && bodyCopy != null, "the first displayed cold native picture admits both exact original physical parts without a numeric root");
            began = HoverClock666.Now;
            Vector3 baseCard = owner.InverseTransformPoint(card.position);
            Vector3 printOffset = Quaternion.Inverse(face.rotation) * (face.position - card.position) / stationScale;
            Vector3 nativeAxis = owner.InverseTransformVector(Vector3.up * (.006f * stationScale));
            Check(Vector3.Distance(observer.InverseTransformPoint(copy.Root.position), owner.InverseTransformPoint(face.position)) < .000002f,
                "first cold native paint keeps the actual canonical source base");
            // Existing109 supplies exact affine overlay/physical ancestry. The
            // cold card assertion above intentionally precedes every numeric
            // event; composed geometry starts once its real relation arrives.
            Vector3 ColdExpected(float age) => observer.TransformPoint(baseCard
                + Quaternion.Inverse(observer.rotation) * copy.Root.rotation * printOffset
                + nativeAxis * Mathf.Sin(age * 1.8f));
            for (int coldFrame = 1; coldFrame <= 8; coldFrame++)
            {
                HoverClock666.Now = began + coldFrame / 90f;
                TownServiceMirror.TickRemote(_ => observer);
                Check(Vector3.Distance(copy.Root.position, ColdExpected(coldFrame / 90f)) < .000004f,
                    "cold native original evaluates one intrinsic wave without any numeric root or accumulated displacement");
                CheckOfferedBody665(face, body, copy.Root, bodyCopy.Root);
            }
            DeliverMotion(1, initial); TownServiceMirror.TickRemote(_ => observer);
            Vector3[]? sampledRow = sourceRow == null ? null : NativeRowCorners665(face, sourceRow);
            ulong sampledSequence = 0;
            foreach (var bytes in initial.Motion)
            {
                TownServiceMotionCodec.TryRead(bytes, bytes.Length, out var packet);
                foreach (var entry in packet!.Entries)
                    if (entry.Kind == 9 && entry.Module == 10 && entry.Visible) sampledSequence = packet.Sequence;
            }
            var queue = new List<(int Due, byte[] Bytes, Vector3[]? Row)>();
            Vector3 Expected(float age) => observer.TransformPoint(baseCard
                + Quaternion.Inverse(observer.rotation) * copy.Root.rotation * printOffset
                + nativeAxis * Mathf.Sin(age * 1.8f));
            string csv = Path.Combine(_output, "hover666-service" + service + "-scale" + stationScale + "-irregular" + irregular + ".csv");
            File.WriteAllText(csv, "frame,time,sourceHover,remoteHover,expectedHover,error,motionEvents\n");
            int packets = 0, dropped = 0, delayed = 0;
            float worst = 0f, maxStepError = 0f;
            Vector3 previous = copy.Root.position, previousExpected = Expected(8f / 90f);
            for (int frame = 9; frame <= 368; frame++)
            {
                float age = frame / 90f; HoverClock666.Now = began + age;
                TownServiceOfferingPose.Place(seat, palm, owner, age);
                mask?.SendMessage("LateUpdate");
                if (frame % 24 == 3) ink.sprite = ink.sprite == first ? second : first;
                FastCapture capture = CaptureFast(); Receive(1, capture.Artwork);
                foreach (byte[] bytes in capture.Motion)
                {
                    int index = packets++;
                    if (irregular && index == 4) { dropped++; continue; }
                    int delay = irregular ? index == 7 ? 10 : index % 4 == 1 ? 2 : index % 4 == 3 ? 3 : 0 : 0;
                    if (delay > 0) delayed++;
                    queue.Add((frame + delay, bytes, sourceRow == null ? null : NativeRowCorners665(face, sourceRow)));
                }
                for (int i = 0; i < queue.Count;)
                    if (queue[i].Due <= frame)
                    {
                        var item = queue[i];
                        TownServiceMotionCodec.TryRead(item.Bytes, item.Bytes.Length, out var packet);
                        foreach (var entry in packet!.Entries)
                            if (entry.Kind == 9 && entry.Module == 10 && entry.Visible && packet.Sequence > sampledSequence)
                            { sampledRow = item.Row; sampledSequence = packet.Sequence; }
                        var delivered = new FastCapture(); delivered.Motion.Add(item.Bytes); queue.RemoveAt(i); DeliverMotion(1, delivered);
                    }
                    else i++;
                TownServiceMirror.TickRemote(_ => observer);
                Vector3 expected = Expected(age);
                float error = Vector3.Distance(expected, copy.Root.position); worst = Mathf.Max(worst, error);
                Vector3 expectedBefore = previousExpected; previousExpected = expected;
                float stepError = Vector3.Distance(copy.Root.position - previous, expected - expectedBefore);
                maxStepError = Mathf.Max(maxStepError, stepError); previous = copy.Root.position;
                File.AppendAllText(csv, frame + "," + HoverClock666.Now + "," + (.006f * stationScale * Mathf.Sin(age * 1.8f))
                    + "," + Vector3.Dot(copy.Root.position - Expected(age) - observer.TransformVector(nativeAxis)*Mathf.Sin(age*1.8f), observer.TransformVector(nativeAxis).normalized)
                    + "," + Vector3.Distance(expected, Expected(age) - observer.TransformVector(nativeAxis)*Mathf.Sin(age*1.8f)) + "," + error + "," + capture.Motion.Count + "\n");
                Check(error < .000004f, "native intrinsic sine is evaluated per render from its complete canonical base service=" + service
                    + " scale=" + stationScale + " irregular=" + irregular + " frame=" + frame + " error=" + error);
                Check(stepError < .000004f, "native hover derivative has no packet-chord stalls or arrival steps on a 90Hz render");
                CheckOfferedBody665(face, body, copy.Root, bodyCopy.Root);
                if (sourceRow != null)
                {
                    RectTransform copied = (RectTransform)Remote(1, 10)!.Root.Find("Native pooled card/FullAbilityCard/Original selected row");
                    Vector3[] reference = sampledRow!, corners = new Vector3[4]; copied.GetWorldCorners(corners);
                    for (int corner = 0; corner < 4; corner++)
                        Check(Vector3.Distance(copy.Root.TransformPoint(reference[corner]), corners[corner]) < .00004f,
                            "actual native enhancement area follows final hovered physical print on every render frame="+frame+" corner="+corner+" error="+Vector3.Distance(copy.Root.TransformPoint(reference[corner]),corners[corner])+" source="+reference[corner]+" target="+copy.Root.InverseTransformPoint(corners[corner]));
                }
            }
            File.AppendAllText(csv, "worst=" + worst + ",maxStepError=" + maxStepError + ",dropped=" + dropped + ",delayed=" + delayed + "\n");
            Check(packets > 20 && packets < 90, "source retains bounded 15Hz numeric cadence while observers draw360 native wave frames");
            Check(!irregular || dropped == 1 && delayed > 0, "irregular probe loses exactly one real event and delays actual numeric events");
            // Native release reparents the original away from its Place seat.
            // Keep the old MotionOffering registration: ancestry, not a stale
            // boolean, must author the actual off state.
            // Save an authentic newer-than-received active root, then deliver
            // it only AFTER the reliable OFF header. Its sequence remains valid
            // but its canonical pose no longer owns this physical presentation.
            HoverClock666.Now += .08f; TownServiceOfferingPose.Place(seat, palm, owner, 4.2f);
            mask?.SendMessage("LateUpdate"); FastCapture lateActive = OfferedCapture629();
            card.SetParent(owner, true); HoverClock666.Now += .1f;
            FastCapture released = OfferedCapture629(); Receive(1, released.Artwork);
            DeliverMotion(1, lateActive);
            TownServiceMirror.TickRemote(_ => observer);
            Check(OfferedGuard666(1, 11) != true,
                "fresh native OFF header rejects the actual delayed active canonical root even with a stale offering registration");
            DeliverMotion(1, released);
            TownServiceMirror.TickRemote(_ => observer);
            var hoverClocks = typeof(TownServiceMirror).GetField("HoverClocks", BindingFlags.Static | BindingFlags.NonPublic);
            Check(hoverClocks == null || ((IDictionary)hoverClocks.GetValue(null)!).Count == 0,
                "native release immediately revokes the intrinsic hover lease");
            Vector3 stopped = copy.Root.position;
            for (int frame = 0; frame < 30; frame++)
            {
                HoverClock666.Now += 1f / 90f; TownServiceMirror.TickRemote(_ => observer);
                if (frame == 20) stopped = copy.Root.position;
            }
            Check(Vector3.Distance(stopped, copy.Root.position) < .000003f,
                "ordinary release pose finishes its existing native tween without a residual hover");
            HoverClock666.Now += .1f;
            // Acceptance reparents before the next Place update. No Place saw
            // the detached interval, so only the real ctor activation can
            // distinguish this lifetime from its previous same-seat offer.
            var nativeSettle = new TownServiceOfferingCard(card, seat, 1f);
            TownServiceOfferingPose.Place(seat, palm, owner, 7f);
            float reofferedAt = HoverClock666.Now;
            mask?.SendMessage("LateUpdate");
            FastCapture again = OfferedCapture629();
            uint againEpoch = service == 3 ? CheckHoverEpoch666(again, 11, 12, 10) : CheckHoverEpoch666(again, 11, 12);
            Check(initialEpoch == 0 || againEpoch != initialEpoch,
                "actual native acceptance creates a new epoch after a no-Place no-flight detached interval");
            Receive(1, again.Artwork); DeliverMotion(1, again);
            TownServiceMirror.TickRemote(_ => observer);
            Vector3 settleFrom = card.localPosition;
            for (int frame = 1; frame <= 60; frame++)
            {
                float elapsed = frame / 90f; HoverClock666.Now = reofferedAt + elapsed;
                TownServiceOfferingPose.Place(seat, palm, owner, 7f + elapsed);
                nativeSettle.Tick(); mask?.SendMessage("LateUpdate");
                float progress = Mathf.Clamp01(elapsed / .25f); progress = progress * progress * (3f - 2f * progress);
                Check(Vector3.Distance(card.localPosition, Vector3.Lerp(settleFrom, Vector3.zero, progress)) < .000002f,
                    "real native offered card retains its exact finite .25-second smoothstep settle");
                FastCapture settling = CaptureFast(); Receive(1, settling.Artwork); DeliverMotion(1, settling);
                TownServiceMirror.TickRemote(_ => observer);
                if (frame >= 45)
                {
                    Vector3 currentBase = owner.InverseTransformPoint(seat.position - Vector3.up * (.006f * stationScale * Mathf.Sin((7f + elapsed) * 1.8f)));
                    Vector3 expected = observer.TransformPoint(currentBase
                        + Quaternion.Inverse(observer.rotation) * copy.Root.rotation * printOffset
                        + nativeAxis * Mathf.Sin(elapsed * 1.8f));
                    Check(Vector3.Distance(copy.Root.position, expected) < .000005f,
                        "native reoffer gets one common current epoch without replaying the old offered root frame="+frame+" error="+Vector3.Distance(copy.Root.position,expected));
                }
                CheckOfferedBody665(face, body, copy.Root, bodyCopy.Root);
            }
            TownServiceMirror.RegisterCardReturn(body, actual.TryTownReturnMotion);
            // Both physical parts now use the exact actual native return sampler.
            // Its first active113 owns the original; intrinsic hover cannot add
            // another displacement while the native curve is rendering.
            actual.BeginNative666(card.position + new Vector3(.18f, -.07f, .12f), .65f);
            FastCapture firstFlight = OfferedCapture629(); Receive(1, firstFlight.Artwork); DeliverMotion(1, firstFlight);
            TownServiceMirror.TickRemote(_ => observer);
            Check(hoverClocks == null || ((IDictionary)hoverClocks.GetValue(null)!).Count == 0,
                "actual first native return sample revokes all offered intrinsic clocks");
            for (int flightFrame = 1; flightFrame <= 80; flightFrame++)
            {
                HoverClock666.Now += HoverClock666.Delta; actual.StepNative666();
                FastCapture flying = CaptureFast(); Receive(1, flying.Artwork); DeliverMotion(1, flying);
                TownServiceMirror.TickRemote(_ => observer);
                Check(hoverClocks == null || ((IDictionary)hoverClocks.GetValue(null)!).Count == 0,
                    "native active terminal and ordinary return frames never retain an intrinsic offered wave");
                CheckOfferedBody665(face, body, copy.Root, bodyCopy.Root);
            }
            card.gameObject.SetActive(false); if (holder != null) holder.gameObject.SetActive(false);
            HoverClock666.Now += .1f; FastCapture hidden = OfferedCapture629(); Receive(1, hidden.Artwork); DeliverMotion(1, hidden);
            TownServiceMirror.TickRemote(_ => observer);
            Check(!copy.Root.gameObject.activeInHierarchy && !bodyCopy.Root.gameObject.activeInHierarchy,
                "true native withdrawal revokes the whole hovered original immediately");
            HoverClock666.Controlled = false; yield return null;
        }
        HoverCanvas666(); HoverClock666.Controlled = false;
    }
}
