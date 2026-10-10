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
    internal static float Read => Controlled ? Now : Time.unscaledTime;
}
public static partial class MirrorProgram
{
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
            FastCapture initial = OfferedCapture629(); Receive(1, initial.Artwork);
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
            Vector3 previous = copy.Root.position, previousExpected = Expected(0f);
            for (int frame = 1; frame <= 360; frame++)
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
            card.SetParent(owner, true); HoverClock666.Now += .1f;
            FastCapture released = OfferedCapture629(); Receive(1, released.Artwork); DeliverMotion(1, released);
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
            TownServiceOfferingPose.Place(seat, palm, owner, 7f);
            var nativeSettle = new TownServiceOfferingCard(card, seat, 1f);
            float reofferedAt = HoverClock666.Now;
            mask?.SendMessage("LateUpdate");
            FastCapture again = OfferedCapture629(); Receive(1, again.Artwork); DeliverMotion(1, again);
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
            card.gameObject.SetActive(false); if (holder != null) holder.gameObject.SetActive(false);
            HoverClock666.Now += .1f; FastCapture hidden = OfferedCapture629(); Receive(1, hidden.Artwork); DeliverMotion(1, hidden);
            TownServiceMirror.TickRemote(_ => observer);
            Check(!copy.Root.gameObject.activeInHierarchy && !bodyCopy.Root.gameObject.activeInHierarchy,
                "true native withdrawal revokes the whole hovered original immediately");
            HoverClock666.Controlled = false; yield return null;
        }
    }
}
