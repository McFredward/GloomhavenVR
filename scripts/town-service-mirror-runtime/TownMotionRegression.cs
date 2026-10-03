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
    private static IEnumerator MotionFast()
    {
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
        TownServiceMirror.Shutdown(); VRHands.Left = VRHands.Right = null; NetAvatarDriver.MotionHandFrames.Clear();
        IEnumerator warmed = HiddenOwnedFanWarmup(); while (warmed.MoveNext()) yield return warmed.Current;
    }

    private static IEnumerator HiddenOwnedFanWarmup()
    {
        GloomhavenVR.WorldUI.TownServiceSync.ResetNetwork();
        Transform shared = Go("Hidden native item fan owner").transform;
        Transform observer = Go("Hidden native item fan observer").transform; observer.position = Vector3.right * 15f;
        var chip = Go("Closed native chip", shared).AddComponent<GloomhavenVR.Cards.ItemsPile.ItemChip>();
        CanvasGroup closed = chip.gameObject.AddComponent<CanvasGroup>(); closed.alpha = 0f;
        Transform face = Source(chip.transform);
        chip.NativeItemCard = face.gameObject.AddComponent<GloomhavenVR.WorldUI.ItemCardUI>();
        chip.NativeItemCard.CardID = 700; chip.Item = new GloomhavenVR.Cards.ItemsPile.Item { ID = 700 };
        chip.InspectionBody = Go("Exact owned card body", chip.transform).transform;
        GloomhavenVR.WorldUI.TownServicePresentation.Active = false;
        GloomhavenVR.WorldUI.TownServicePresentation.Ritual = null;
        GloomhavenVR.WorldUI.TownServicePresentation.Catalog = null;
        GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.Returning.Clear();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Active = true;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Session = 700;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.StationRoot = shared;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Clear();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Add(chip);
        yield return null; Canvas.ForceUpdateCanvases();
        GloomhavenVR.WorldUI.TownServiceSync.UseProductionPublish = true;
        TownServiceMirror.ResolveTemplate = (service, template, address) =>
        { GloomhavenVR.WorldUI.NativeTemplates.Resolve(service, template, address); return true; };
        GloomhavenVR.WorldUI.TownServiceSync.Tick(shared, null);
        Check(GloomhavenVR.WorldUI.TownServiceSync.ModuleCount >= 2,
            "closed owner item fan publishes complete original fronts with genuine hidden visibility before reveal");
        ushort module = GloomhavenVR.WorldUI.TownServiceSync.ModuleId(face);
        FastCapture hidden = CaptureFast();
        Check(hidden.Artwork.Exists(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame)
            && frame!.Module == module && frame.ParentAlpha == 0f),
            "closed owner item fan publishes complete original fronts with genuine hidden visibility before reveal");
        Receive(2, hidden.Artwork);
        // Production ResolveFrame owns the publisher mount. Supply the remote rig room
        // adapter separately, exactly as NetAvatarDriver does in the running game.
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding copy = Remote(2, module)!;
        Check(copy != null && copy.Root.parent.GetComponent<CanvasGroup>().alpha == 0f,
            "prewarmed original fan stays hidden and never leaks a closed hand");
        Sprite front = copy.Root.Find("Filled").GetComponent<Image>().sprite;
        closed.alpha = 1f;
        GloomhavenVR.WorldUI.TownServiceSync.Tick(shared, null); TownServiceMirror.SharedFrameForRemote = _ => observer;
        FastCapture reveal = CaptureFast(); DeliverMotion(2, reveal);
        Check(!reveal.Artwork.Exists(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame) && frame!.Module == module && frame.BaseSequence == 0),
            "warm reveal does not wait for another full native front baseline");
        settle = FastSettle(observer); while (settle.MoveNext()) yield return settle.Current;
        Check(copy.Root.parent.GetComponent<CanvasGroup>().alpha > .99f && ReferenceEquals(front, copy.Root.Find("Filled").GetComponent<Image>().sprite),
            "opening a prewarmed owner item fan reveals its existing complete front through the independent numeric lane");
        GloomhavenVR.WorldUI.TownServiceSync.UseProductionPublish = false;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Clear();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Active = false;
        GloomhavenVR.WorldUI.TownServiceSync.ResetNetwork(); TownServiceMirror.Shutdown();
    }
}
