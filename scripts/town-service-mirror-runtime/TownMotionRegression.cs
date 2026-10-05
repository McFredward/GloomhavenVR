using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private sealed class FastCapture
    {
        internal readonly List<byte[]> Artwork = new(), Motion = new();
    }
    private static FastCapture CaptureFast()
    {
        var captured = new FastCapture();
        TownServiceMirror.Capture((bytes, length, identity) =>
        {
            Check(length == bytes.Length, "fast capture publishes complete immutable events");
            if (bytes[5] == TownServiceMotionCodec.MessageType)
            {
                Check(length <= 864 && TownServiceMotionCodec.TryRead(bytes, length, out _),
                    "numeric capture validates within one bounded event");
                captured.Motion.Add(bytes);
            }
            else
            {
                Check(TownServiceCodec.TryRead(bytes, length, out _), "original capture still validates its full native artwork");
                captured.Artwork.Add(bytes);
            }
        });
        return captured;
    }
    private static void DeliverMotion(int peer, FastCapture capture)
    {
        foreach (byte[] bytes in capture.Motion)
        {
            Check(TownServiceMotionCodec.TryRead(bytes, bytes.Length, out TownServiceMotionPacket? packet), "received independent numeric packet validates");
            Check(TownServiceMirror.ReceiveMotion(peer, packet!), "numeric receiver accepts a validated peer clock");
        }
    }
    private static IEnumerator FastSettle(Transform observer, float duration = .11f)
    {
        for (float until = Time.unscaledTime + duration; Time.unscaledTime < until;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        TownServiceMirror.TickRemote(_ => observer);
    }
    private static IEnumerator FastNpcIntents()
    {
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
        Transform frame = Go("Independent NPC intent source frame").transform;
        TownServiceMirror.BeginSession(2, 991, frame, frame);
        TownServiceMirror.MarkLocalTempleDonationCommitted();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.WantsOffering = true;
        GloomhavenVR.WorldUI.TownServiceSharedCue.LocalReady = true;
        FastCapture first = CaptureFast();
        Check(first.Motion.Count == 1, "cold NPC readiness and committed donation share an immediate independent numeric event");
        DeliverMotion(2, first);
        Check(TownServiceMirror.RemoteMerchantOffering,
            "shared merchant offered-hand intent arrives before any guide or native artwork");
        Check(GloomhavenVR.WorldUI.TownServiceSharedCue.MageReady[2]
            && GloomhavenVR.WorldUI.TownServiceSharedCue.MageSessions[2] != 991,
            "actual fast mage intent uses its motion lifetime instead of a private service session");
        var donations = new List<TownTempleDonationState>(); TownServiceMirror.CollectTempleDonationStates(donations);
        Check(donations.Exists(state => state.Peer == 2 && state.Session == 991 && state.Revision == 1 && state.HasCommitAge),
            "the actual captured committed donation reaches observers without a service artwork manifest");
        for (float until = Time.unscaledTime + .09f; Time.unscaledTime < until;) yield return null;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.WantsOffering = false;
        GloomhavenVR.WorldUI.TownServiceSharedCue.LocalReady = false;
        FastCapture withdrawn = CaptureFast(); DeliverMotion(2, withdrawn); DeliverMotion(2, first);
        Check(!TownServiceMirror.RemoteMerchantOffering && !GloomhavenVR.WorldUI.TownServiceSharedCue.MageReady[2],
            "late reordered readiness cannot revive withdrawn merchant or mage palm intent");
        TownServiceMirror.Shutdown();
    }
    private static IEnumerator MotionFast()
    {
        IEnumerator purses = NativePurseMotion(); while (purses.MoveNext()) yield return purses.Current;
        IEnumerator intents = FastNpcIntents(); while (intents.MoveNext()) yield return intents.Current;
        TownServiceMirror.Shutdown();
        Transform shared = Go("Fast author frame").transform;
        Transform observer = Go("Fast observer frame").transform; observer.position = Vector3.right * 12f;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        Transform source = Source(shared);
        yield return null; Canvas.ForceUpdateCanvases();
        TownServiceMirror.RegisterTemplate(1, 1, source, address: "item.611|");
        TownServiceMirror.BeginSession(1, 611, shared, source);
        TownServiceMirror.RegisterModule(10, 1, source, address: "item.611|");
        FastCapture initial = CaptureFast();
        Check(initial.Artwork.Count >= 2, "cold original front and membership publish before fast motion");
        foreach (byte[] bytes in initial.Artwork)
            if (TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame) && frame!.Module == 10)
                File.WriteAllBytes(Path.Combine(_output, "captured-original-front.gvr"), bytes);
        Receive(1, initial.Artwork); TownServiceMirror.InteractionOwner(1);
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding copy = Remote(1)!; Check(copy != null, "initial full native front creates an inert clone");
        Sprite original = copy.Root.Find("Filled").GetComponent<Image>().sprite;
        Vector3 originalRoot = copy.Root.position;
        // The editor's render/clone settling can consume the real owner's .75 s
        // artwork heartbeat. Refresh that clock before the synchronous numeric
        // mutation, so this assertion isolates artwork changes rather than a
        // scheduled heartbeat. Recovery remains exercised below.
        // The ordinary artwork subscriber leaves the independent motion cadence
        // untouched, so the following change remains eligible for one fast event.
        Receive(1, Capture());
        int awakes = GameplayFixture.Awakes, enables = GameplayFixture.Enables;
        source.localPosition += new Vector3(.7f, .2f, 0f);
        _group.alpha = .52f; _fill.fillAmount = .26f; _fill.color = new Color(.8f, .3f, .5f, .9f);
        var child = (RectTransform)source.Find("Name"); child.anchoredPosition += new Vector2(33, -12);
        _clip.padding = new Vector4(17, 6, 11, 4);
        FastCapture numbers = CaptureFast();
        Check(numbers.Motion.Count == 1, "hover scroll alpha and root share one independent numeric event");
        Check(!numbers.Artwork.Exists(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame)
            && frame!.Module == 10), "numeric state changes do not requeue immutable original art");
        DeliverMotion(1, numbers);
        settle = FastSettle(observer); while (settle.MoveNext()) yield return settle.Current;
        Check(Vector3.Distance(copy.Root.position, observer.TransformPoint(shared.InverseTransformPoint(source.position))) < .0001f,
            "root motion converges while every original-art transport page is blocked");
        Check(Vector3.Distance(copy.Root.position, originalRoot) > .4f, "fast root has actually changed observer geometry");
        Check(Vector2.Distance(((RectTransform)copy.Root.Find("Name")).anchoredPosition, child.anchoredPosition) < .001f
            && Mathf.Abs(copy.Root.Find("Filled").GetComponent<Image>().fillAmount - _fill.fillAmount) < .0001f
            && Mathf.Abs(copy.Root.GetComponent<CanvasGroup>().alpha - _group.alpha) < .0001f,
            "composing fast root scroll and hover preserves every simultaneous original property");
        Check(copy.Root.Find("Viewport").GetComponent<RectMask2D>().padding == _clip.padding,
            "fast numeric native masks preserve the complete original owner padding");
        Check(ReferenceEquals(original, copy.Root.Find("Filled").GetComponent<Image>().sprite),
            "numeric updates never replace a validated original front with grey or fallback art");
        Check(GameplayFixture.Awakes == awakes && GameplayFixture.Enables == enables,
            "fast playback never enables original gameplay controllers or callbacks");
        ComparePixels(source, copy.Root, "fast-numeric-endpoint");

        // Lost event: the next changed property need not resend it immediately, but
        // the one-second numeric heartbeat recovers the full cumulative latest state.
        _fill.fillAmount = .71f;
        settle = FastSettle(observer, .08f); while (settle.MoveNext()) yield return settle.Current;
        FastCapture dropped = CaptureFast(); Check(dropped.Motion.Count == 1, "loss probe captures a real independent event");
        source.localPosition += new Vector3(.05f, 0f, 0f);
        settle = FastSettle(observer, .08f); while (settle.MoveNext()) yield return settle.Current;
        FastCapture later = CaptureFast(); DeliverMotion(1, later); DeliverMotion(1, numbers);
        settle = FastSettle(observer, 1.02f); while (settle.MoveNext()) yield return settle.Current;
        DeliverMotion(1, CaptureFast()); settle = FastSettle(observer); while (settle.MoveNext()) yield return settle.Current;
        Check(Mathf.Abs(copy.Root.Find("Filled").GetComponent<Image>().fillAmount - .71f) < .0001f,
            "bounded numeric heartbeat recovers a dropped property without an artwork replay");
        Check(Vector3.Distance(copy.Root.position, observer.TransformPoint(shared.InverseTransformPoint(source.position))) < .0001f,
            "reordered old numeric packets cannot rewind a newer root target");

        TownServiceMotionCodec.TryRead(numbers.Motion[0], numbers.Motion[0].Length, out TownServiceMotionPacket? alien);
        alien!.Sequence += 10000;
        foreach (TownServiceMotionEntry entry in alien.Entries) if (entry.Kind is 1 or 2 or 4) entry.Session += 1;
        TownServiceMirror.ReceiveMotion(1, alien); settle = FastSettle(observer); while (settle.MoveNext()) yield return settle.Current;
        Check(copy.Root.gameObject.activeInHierarchy && ReferenceEquals(original, copy.Root.Find("Filled").GetComponent<Image>().sprite),
            "a foreign numeric lifetime cannot conceal or rebuild last validated original artwork");
        TownServiceMirror.ForgetRemoteMotion(1);

        // The original canvas and native mask must move with the same rendered hand
        // every frame; merely moving its child produces missing/clipped card fronts.
        TownServiceMirror.Shutdown();
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        var handRoot = Go("Owner tracked hand").transform; handRoot.position = new Vector3(.2f, .4f, .6f);
        var grabAnchor = Go("Exact grab anchor", handRoot).transform;
        var left = new VRHand { Side = HandSide.Left, HasPose = true };
        left.Rig.Root = handRoot; left.Rig.GrabAnchor = grabAnchor; VRHands.Left = left;
        var canvasFrame = (RectTransform)Go("Original external card canvas", grabAnchor).transform;
        canvasFrame.sizeDelta = new Vector2(800, 500); canvasFrame.localScale = Vector3.one * .01f;
        canvasFrame.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        Object.DestroyImmediate(source.GetComponent<GraphicRaycaster>());
        Object.DestroyImmediate(source.GetComponent<Canvas>());
        source.SetParent(canvasFrame, false); source.localPosition = new Vector3(20, -12, 1); source.localScale = Vector3.one;
        TownServiceMirror.RegisterTemplate(1, 2, source, address: "item.612|");
        TownServiceMirror.BeginSession(1, 612, shared, shared);
        TownServiceMirror.RegisterModule(11, 2, source, address: "item.612|");
        Transform receiverHand = Go("Rendered approved left holder").transform;
        receiverHand.position = observer.position + handRoot.position; receiverHand.rotation = handRoot.rotation;
        NetAvatarDriver.MotionHandFrames[1] = new[] { receiverHand, Go("Unused right holder").transform };
        initial = CaptureFast(); Receive(1, initial.Artwork); DeliverMotion(1, initial);
        settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        copy = Remote(1, 11)!;
        Check(copy != null && copy.Root.Find("Viewport").GetComponent<RectMask2D>() != null,
            "held native card retains its actual nested masks and original artwork");
        TownServiceMotionCodec.TryRead(initial.Motion[0], initial.Motion[0].Length, out TownServiceMotionPacket? handPacket);
        TownServiceMotionEntry handEntry = handPacket!.Entries.Find(e => e.Kind == 1)!;
        Check(handEntry.Hand == 1 && handEntry.HasCanvasUpdate && handEntry.HasCanvasFrame && handEntry.CanvasOnHand,
            "physical GrabAnchor provenance includes the original enclosing hand canvas");
        for (int step = 0; step < 25; step++)
        {
            receiverHand.position += new Vector3(.011f, -.004f, .002f);
            receiverHand.rotation = Quaternion.Euler(step * 3f, step * 7f, step);
            TownServiceMirror.TickRemote(_ => observer);
            Check(Vector3.Distance(copy.Root.position, receiverHand.TransformPoint(new Vector3(handEntry.Pose[0], handEntry.Pose[1], handEntry.Pose[2]))) < .0001f,
                "held original root follows the approved smoothed rig between network events");
            Transform clonedCanvas = copy.Root.parent.GetComponent<Canvas>().transform;
            Check(Vector3.Distance(clonedCanvas.position, receiverHand.TransformPoint(new Vector3(handEntry.CanvasPose[0], handEntry.CanvasPose[1], handEntry.CanvasPose[2]))) < .0001f,
                "enclosing original canvas follows the same approved smoothed rig between events");
            Check(Quaternion.Angle(copy.Root.rotation, receiverHand.rotation * new Quaternion(handEntry.Pose[3], handEntry.Pose[4], handEntry.Pose[5], handEntry.Pose[6])) < .01f,
                "held original card rotates with the actual rig holder without old-world trailing");
            yield return null;
        }
        TownServiceMirror.RegisterMotionHand(grabAnchor, left, followsRotation: false);
        for (float until = Time.unscaledTime + .08f; Time.unscaledTime < until;) yield return null;
        DeliverMotion(1, CaptureFast());
        settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        Vector3 startOffset = receiverHand.InverseTransformPoint(copy.Root.position);
        Quaternion startRotation = copy.Root.rotation;
        settle = FastSettle(observer, .21f); while (settle.MoveNext()) yield return settle.Current;
        source.localPosition += new Vector3(40, 0, 0); source.rotation = Quaternion.AngleAxis(55f, Vector3.up) * startRotation;
        FastCapture spacedFan = CaptureFast(); DeliverMotion(1, spacedFan); TownServiceMirror.TickRemote(_ => observer);
        // Evaluate the actual production tween at a deterministic render instant:
        // software-rendered editor frames can themselves take longer than 250 ms.
        var remotePeers = (System.Collections.IDictionary)typeof(TownServiceMirror)
            .GetField("Remote", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null);
        var remoteModule = ((System.Collections.IDictionary)remotePeers[1]!)[(ushort)11]!;
        var nativeMotion = (TownServiceMotion)remoteModule.GetType()
            .GetField("Motion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(remoteModule)!;
        float instant = Time.unscaledTime + .11f;
        nativeMotion.Tick(instant); TownServiceMirror.ApplyRemoteMotion(instant);
        Vector3 targetOffset = handRoot.InverseTransformPoint(source.position);
        Vector3 displayedOffset = receiverHand.InverseTransformPoint(copy.Root.position);
        Check(Vector3.Distance(displayedOffset, startOffset) > .005f && Vector3.Distance(displayedOffset, targetOffset) > .005f,
            "native upright fan offsets remain between original endpoints at an intermediate frame after a spaced owner sample");
        Check(Quaternion.Angle(copy.Root.rotation, startRotation) > 1f && Quaternion.Angle(copy.Root.rotation, source.rotation) > 1f,
            "native upright fan rotation covers the measured owner interval instead of finishing in one fixed rig tick");
        // A following owner sample arrives while the previous relative tween
        // and the approved rig are both moving. It must start at what is currently
        // rendered, not at the old endpoint restored by native binding.
        float interruptedAt = instant + .025f;
        nativeMotion.Tick(interruptedAt); TownServiceMirror.ApplyRemoteMotion(interruptedAt);
        Vector3 beforeReplacement = receiverHand.InverseTransformPoint(copy.Root.position);
        receiverHand.position += new Vector3(.17f, .03f, -.04f);
        TownServiceMirror.ApplyRemoteMotion(interruptedAt);
        Check(Vector3.Distance(beforeReplacement, receiverHand.InverseTransformPoint(copy.Root.position)) < .001f,
            "an in-progress upright native fan tween rides current rig movement without changing its authored relative offset");
        TownServiceMotionCodec.TryRead(spacedFan.Motion[0], spacedFan.Motion[0].Length, out TownServiceMotionPacket? replacement);
        replacement!.Sequence++; replacement.SampleTime += .1f;
        foreach (TownServiceMotionEntry entry in replacement.Entries)
            if (entry.Kind == 1) { entry.Pose = (float[])entry.Pose.Clone(); entry.Pose[0] += .2f; targetOffset.x += .2f; }
        Check(TownServiceMotionCodec.TryRead(TownServiceMotionCodec.Write(replacement), TownServiceMotionCodec.Write(replacement).Length, out replacement),
            "following sparse fan owner sample still obeys the production strict numeric grammar");
        TownServiceMirror.ReceiveMotion(1, replacement!); TownServiceMirror.ApplyRemoteMotion(interruptedAt);
        Check(Vector3.Distance(beforeReplacement, receiverHand.InverseTransformPoint(copy.Root.position)) < .001f,
            "replacing an in-progress native relative fan tween preserves its current rendered pose instead of jumping to a previous target");
        nativeMotion.Tick(instant + 1.55f); TownServiceMirror.ApplyRemoteMotion(instant + 1.55f);
        Check(Vector3.Distance(receiverHand.InverseTransformPoint(copy.Root.position), targetOffset) < .001f,
            "native upright fan offsets converge to the exact original after measured-interval interpolation");
        TownServiceMirror.Shutdown(); VRHands.Left = VRHands.Right = null; NetAvatarDriver.MotionHandFrames.Clear();
        IEnumerator warmed = HiddenOwnedFanWarmup(); while (warmed.MoveNext()) yield return warmed.Current;
    }

    private static IEnumerator NativePurseMotion()
    {
        foreach (float worldScale in new[] { .1f, 1f, 198.12f })
        foreach (float styleScale in new[] { .62f, 1.12f, 1.7f })
        {
            TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
            Transform shared = Go("Native purse author frame").transform;
            Transform observer = Go("Native purse observer frame").transform;
            observer.position = new Vector3(21f, 3f, -8f);
            TownServiceMirror.SharedFrameForRemote = _ => observer;
            Transform tracking = Go("Owner tracked hand").transform;
            tracking.position = new Vector3(.25f, .7f, -.3f) * worldScale;
            tracking.rotation = Quaternion.Euler(29f, 44f, -17f);
            tracking.localScale = Vector3.one * worldScale;
            Transform handRoot = Go("Owner style visual", tracking).transform;
            handRoot.localScale = Vector3.one * styleScale;
            Transform palm = Go("Compensated owner palm", handRoot).transform;
            palm.localScale = Vector3.one / styleScale;
            var hand = new VRHand { Side = HandSide.Left, HasPose = true, WorldScale = worldScale };
            hand.Rig.Root = handRoot; hand.Rig.GrabAnchor = palm; VRHands.Left = hand;
            Transform receiver = Go("Approved interpolated remote holder").transform;
            receiver.SetPositionAndRotation(observer.position + handRoot.position, handRoot.rotation);
            receiver.localScale = Vector3.one * worldScale;
            NetAvatarDriver.MotionHandFrames[1] = new[] { receiver, Go("Unused right hand").transform };
            Transform preview = Go("Owner labelled purse seat", shared).transform;
            preview.position = palm.TransformPoint(new Vector3(0f, .12f, .03f));
            preview.rotation = Quaternion.Euler(0f, 23f, 0f);
            preview.localScale = Vector3.one * worldScale;
            CanvasGroup gate = preview.gameObject.AddComponent<CanvasGroup>(); gate.alpha = 0f;
            Transform source = NativePurse.Create(preview);
            TownServiceMirror.RegisterTemplate(2, 7, source, address: "ritual.purse|");
            TownServiceMirror.BeginSession(2, 772, shared, shared);
            TownServiceMirror.RegisterModule(17, 7, source, address: "ritual.purse|");
            TownServiceMirror.RegisterMotionHand(preview, hand, followsRotation: false);
            FastCapture baseline = CaptureFast();
            // Full-suite catalog setup may spend seconds in this sender frame.
            // Model actual arrival on a fresh receiver clock before its stale and
            // interaction-claim windows begin; keep the original cold snapshot.
            yield return null;
            Receive(1, baseline.Artwork); DeliverMotion(1, baseline);
            // The real send queue completes fully delivered original snapshots.
            // Its production callback starts their heartbeat at actual completion,
            // rather than leaving this warm body indefinitely queued at cold time.
            foreach(byte[] bytes in baseline.Artwork)
            {
                TownServiceCodec.TryRead(bytes,bytes.Length,out TownServiceFrame? delivered);
                TownServiceDelivery.Completed?.Invoke(delivered!);
            }
            for(float until=Time.unscaledTime+.5f;
                TownServiceMirror.InteractionOwner(2)!=1&&Time.unscaledTime<until;)
            {TownServiceMirror.TickRemote(_=>observer);yield return null;}
            TownServiceMirror.TickRemote(_=>observer);
            IEnumerator settle;
            TownServiceBinding copy = Remote(1, 17)!;
            Check(copy != null && copy.Root.parent.GetComponent<CanvasGroup>().alpha == 0f,
                "closed original purse is prewarmed without exposing a hidden wrist");
            gate.alpha = 1f;
            for (float until = Time.unscaledTime + .08f; Time.unscaledTime < until;) yield return null;
            FastCapture reveal = CaptureFast(); DeliverMotion(1, reveal);
            Check(!reveal.Artwork.Exists(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? f)
                && f!.Module == 17), "warm wrist purse reveal does not wait for immutable artwork");
            settle = FastSettle(observer, .13f); while (settle.MoveNext()) yield return settle.Current;
            Check(copy.Root.parent.GetComponent<CanvasGroup>().alpha > .99f,
                "warm original wrist purse appears on its first independent numeric event");
            CheckPurseGeometry(source, copy.Root, handRoot, receiver, worldScale);
            // Grab/release preserve the same complete normalized native geometry, then
            // every render frame follows the already approved smoothed remote holder.
            source.SetParent(palm, true);
            TownServiceMirror.RegisterMotionHand(source, hand, followsRotation: false);
            for (float until = Time.unscaledTime + .08f; Time.unscaledTime < until;) yield return null;
            FastCapture held = CaptureFast(); Receive(1, held.Artwork); DeliverMotion(1, held);
            settle = FastSettle(observer, .15f); while (settle.MoveNext()) yield return settle.Current;
            CheckPurseGeometry(source, copy.Root, handRoot, receiver, worldScale);
            Vector3 relative = Quaternion.Inverse(handRoot.rotation) * (source.position - handRoot.position) / worldScale;
            for (int step = 0; step < 4; step++)
            {
                receiver.position += new Vector3(.03f, -.01f, .02f) * worldScale;
                TownServiceMirror.TickRemote(_ => observer);
                Check(Vector3.Distance(copy.Root.position, receiver.TransformPoint(relative)) < .0002f * worldScale,
                    "held original purse follows the approved smoothed rig between network events");
            }
            // The exact token-to-endpoint calculation is bound separately to the
            // complete production Token and original game purse. This adapter now
            // exercises its output through actual capture, TLV106, receiver and
            // existing inert same-module geometry without constructing new art.
            source.SetParent(preview, true); TownServiceMirror.RegisterMotionHand(source, hand, followsRotation:false);
            Vector3 begin = Quaternion.Inverse(handRoot.rotation) * (source.position-handRoot.position) / worldScale;
            Vector3 end = Quaternion.Inverse(handRoot.rotation)
                * (preview.TransformPoint(new Vector3(0f,-.065f,0f))-handRoot.position) / worldScale;
            Quaternion returnRotation=Quaternion.Inverse(shared.rotation)*source.rotation;
            Vector3 size=source.lossyScale/worldScale;
            var returnToken=new GloomhavenVR.WorldUI.TownServiceToken {ReturnStarted=Time.unscaledTime,
                ReturnNumbers=new[]{0f,.35f,begin.x,begin.y,begin.z,returnRotation.x,returnRotation.y,returnRotation.z,returnRotation.w,
                    size.x,size.y,size.z,end.x,end.y,end.z,returnRotation.x,returnRotation.y,returnRotation.z,returnRotation.w,size.x,size.y,size.z}};
            TownServiceMirror.RegisterMotionReturn(source,returnToken);
            for(float until=Time.unscaledTime+.08f;Time.unscaledTime<until;)yield return null;
            FastCapture flight=CaptureFast(); Receive(1,flight.Artwork); DeliverMotion(1,flight);
            float receivedAt=Time.unscaledTime;
            TownServiceMotionEntry? flightEntry=null;
            foreach(byte[] bytes in flight.Motion)
                if(TownServiceMotionCodec.TryRead(bytes,bytes.Length,out TownServiceMotionPacket? parsed))
                    foreach(TownServiceMotionEntry entry in parsed!.Entries)if(entry.Kind==7)flightEntry=entry;
            Check(flightEntry!=null && flightEntry.Module==17 && flightEntry.Revision==5,
                "ordinary purse return publishes its same-module complete additive timeline immediately");
            for(int step=0;step<5;step++)
            {
                TownServiceMirror.TickRemote(_=>observer);
                float age=flightEntry!.Numbers[0]+Time.unscaledTime-receivedAt;
                float t=Mathf.Clamp01(age/.35f);float ease=t*t*(3f-2f*t);
                Vector3 expected=receiver.TransformPoint(Vector3.Lerp(begin,end,ease));
                Check(Vector3.Distance(copy.Root.position,expected)<.0003f*worldScale,
                    "received ordinary purse return follows its exact owner flight between sparse packets");
                yield return null;
            }
            returnToken.ReturnNumbers=null;
            source.SetParent(preview, true); TownServiceMirror.RegisterMotionHand(source, null);
            source.localPosition = new Vector3(0f, -.065f, 0f);
            for (float until = Time.unscaledTime + .08f; Time.unscaledTime < until;) yield return null;
            FastCapture returned = CaptureFast(); Receive(1, returned.Artwork); DeliverMotion(1, returned);
            settle = FastSettle(observer, .15f); while (settle.MoveNext()) yield return settle.Current;
            CheckPurseGeometry(source, copy.Root, handRoot, receiver, worldScale);
            TownServiceMirror.Shutdown(); VRHands.Left = null; NetAvatarDriver.MotionHandFrames.Clear();
        }
    }
    private static void CheckPurseGeometry(Transform source, Transform copy, Transform ownerHand, Transform receiver, float scale)
    {
        Vector3 relative = Quaternion.Inverse(ownerHand.rotation) * (source.position - ownerHand.position) / scale;
        Check(Vector3.Distance(copy.position, receiver.TransformPoint(relative)) < .0002f * scale
            && Vector3.Distance(copy.lossyScale, source.lossyScale) < .0002f * scale,
            "original purse world size and wrist offset survive the owner hand style");
        MeshRenderer original = source.GetComponentInChildren<MeshRenderer>();
        MeshRenderer rendered = copy.GetComponentInChildren<MeshRenderer>();
        Check(original.sharedMaterials.Length == rendered.sharedMaterials.Length
            && Vector3.Distance(original.bounds.size, rendered.bounds.size) < .0002f * scale,
            "exact original PCG purse renderer retains its visible body size after native playback");
    }

    private static IEnumerator HiddenOwnedFanWarmup()
    {
        const int count = 24;
        GloomhavenVR.WorldUI.TownServiceSync.ResetNetwork();
        Transform shared = Go("Hidden native item fan owner").transform;
        Transform observer = Go("Hidden native item fan observer").transform; observer.position = Vector3.right * 15f;
        Transform handRoot = Go("Exact owner offhand", shared).transform;
        var hand = new VRHand { Side = HandSide.Left, HasPose = true };
        hand.Rig.Root = handRoot; hand.Rig.GrabAnchor = Go("Fan grab anchor", handRoot).transform; VRHands.Left = hand;
        Transform receiverHand = Go("Exact rendered offhand", observer).transform;
        NetAvatarDriver.MotionHandFrames[2] = new[] { receiverHand, Go("Other rendered hand", observer).transform };
        var chips = new List<GloomhavenVR.Cards.ItemsPile.ItemChip>();
        var faces = new List<Transform>(); var opacities = new List<CanvasGroup>();
        GloomhavenVR.WorldUI.TownServicePresentation.Active = false;
        GloomhavenVR.WorldUI.TownServicePresentation.Ritual = null;
        GloomhavenVR.WorldUI.TownServicePresentation.Catalog = null;
        GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.Returning.Clear();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Active = true;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Session = 700;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.StationRoot = shared;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Clear();
        for (int i = 0; i < count; i++)
        {
            var chip = Go("Closed native chip " + i, handRoot).AddComponent<GloomhavenVR.Cards.ItemsPile.ItemChip>();
            CanvasGroup closed = chip.gameObject.AddComponent<CanvasGroup>(); closed.alpha = 0f; opacities.Add(closed);
            Transform face = Source(chip.transform); faces.Add(face); chips.Add(chip);
            chip.NativeItemCard = face.gameObject.AddComponent<GloomhavenVR.WorldUI.ItemCardUI>();
            chip.NativeItemCard.CardID = 700 + i; chip.Item = new GloomhavenVR.Cards.ItemsPile.Item { ID = 700 + i };
            chip.InspectionBody = Go("Exact owned card body " + i, chip.transform).transform;
            GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Add(chip);
        }
        yield return null; Canvas.ForceUpdateCanvases();
        GloomhavenVR.WorldUI.TownServiceSync.UseProductionPublish = true;
        TownServiceMirror.ResolveTemplate = (service, template, address) =>
        { GloomhavenVR.WorldUI.NativeTemplates.Resolve(service, template, address); return true; };
        GloomhavenVR.WorldUI.TownServiceSync.Tick(shared, null);
        Check(GloomhavenVR.WorldUI.TownServiceSync.ModuleCount >= count * 2,
            "closed owner item fan publishes complete original fronts with genuine hidden visibility before reveal");
        var modules = new List<ushort>();
        foreach (Transform face in faces) modules.Add(GloomhavenVR.WorldUI.TownServiceSync.ModuleId(face));
        FastCapture hidden = CaptureFast();
        foreach (ushort module in modules)
            Check(hidden.Artwork.Exists(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame)
                && frame!.Module == module && frame.ParentAlpha == 0f),
                "closed owner item fan publishes complete original fronts with genuine hidden visibility before reveal");
        Receive(2, hidden.Artwork);
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        var copies = new List<TownServiceBinding>(); var fronts = new List<Sprite>();
        foreach (ushort module in modules)
        {
            TownServiceBinding copy = Remote(2, module)!; copies.Add(copy);
            Check(copy != null && copy.Root.parent.GetComponent<CanvasGroup>().alpha == 0f,
                "prewarmed original fan stays hidden and never leaks a closed hand");
            fronts.Add(copy.Root.Find("Filled").GetComponent<Image>().sprite);
        }
        foreach (CanvasGroup opacity in opacities) opacity.alpha = 1f;
        float began = Time.unscaledTime; int events = 0; bool complete = false;
        while (Time.unscaledTime - began < 1f && !complete)
        {
            GloomhavenVR.WorldUI.TownServiceSync.Tick(shared, null); TownServiceMirror.SharedFrameForRemote = _ => observer;
            FastCapture reveal = CaptureFast(); DeliverMotion(2, reveal); events += reveal.Motion.Count;
            Check(!reveal.Artwork.Exists(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame)
                && modules.Contains(frame!.Module) && frame.BaseSequence == 0),
                "warm reveal does not wait for another full native front baseline");
            TownServiceMirror.TickRemote(_ => observer);
            complete = copies.TrueForAll(copy => copy.Root.parent.GetComponent<CanvasGroup>().alpha > .99f);
            if (!complete) yield return null;
        }
        Check(complete, "all twenty-four complete warm native fan fronts reveal within one second of wrist opening");
        File.WriteAllText(Path.Combine(_output, "warm-fan-cadence.txt"),
            $"nativeFronts={count} nativeRootModules={count * 2} completeReveal={Time.unscaledTime - began:F3}s events={events} maxEvent=864B\n");
        for (int i = 0; i < copies.Count; i++)
            Check(ReferenceEquals(fronts[i], copies[i].Root.Find("Filled").GetComponent<Image>().sprite),
                "opening a prewarmed owner item fan reveals its existing complete front through the independent numeric lane");
        // Real publisher provenance must elevate the owned parked face and its
        // physical body, while closed unheld prewarms remain background work.
        chips[0].TownOffering = true;
        GloomhavenVR.WorldUI.TownServiceSync.Tick(shared, null);
        object sources = typeof(TownServiceMirror).GetField("MotionSources", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null);
        for (float until = Time.unscaledTime + .08f; Time.unscaledTime < until;) yield return null;
        CaptureFast();
        int liveOriginals = 0;
        foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)sources)
        {
            var type = entry.Value!.GetType();
            if ((bool)type.GetField("Live", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(entry.Value)!) liveOriginals++;
        }
        Check(liveOriginals >= 2, "owned merchant parked front and physical body retain hot motion through exact publisher provenance");
        GloomhavenVR.WorldUI.TownServiceSync.UseProductionPublish = false;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Clear();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Active = false;
        GloomhavenVR.WorldUI.TownServiceSync.ResetNetwork(); TownServiceMirror.Shutdown();
        VRHands.Left = VRHands.Right = null; NetAvatarDriver.MotionHandFrames.Clear();
    }
}
