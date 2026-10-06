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
    private static void OfferedReceive629(FastCapture capture, float sample)
    {
        foreach (byte[] bytes in capture.Motion)
        {
            Check(TownServiceMotionCodec.TryRead(bytes, bytes.Length, out TownServiceMotionPacket? packet), "offered sampler emits real bounded numeric entries");
            // Only the deterministic transport clock changes. Native geometry,
            // dirty-slot selection, packet packing and receiver are production.
            packet!.Sequence = ++_offeredSequence629; packet.SampleTime = sample;
            byte[] wire = TownServiceMotionCodec.Write(packet);
            Check(TownServiceMotionCodec.TryRead(wire, wire.Length, out packet), "offered transport clock survives the actual wire codec");
            Check(TownServiceMirror.ReceiveMotion(1, packet!), "offered receiver accepts genuine owner geometry");
        }
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
        TownServiceMirror.Shutdown(); _offeredSequence629 = 10000;
        Transform owner = Go("Owner offered frame").transform;
        Transform observer = Go("Observer offered frame").transform;
        observer.SetPositionAndRotation(new Vector3(6f, .13f, -.4f), Quaternion.Euler(0f, 83f, 0f));
        observer.localScale = Vector3.one * 1.4f;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        RectTransform root = (RectTransform)Go("CardHilight", owner).transform;
        root.sizeDelta = new Vector2(325f, 450f); root.localScale = Vector3.one * .00052f;
        root.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        root.gameObject.AddComponent<CanvasGroup>();
        var highlighter = root.gameObject.AddComponent<GloomhavenVR.WorldUI.UIEnhancementCardHighlighter>();
        var card = Go("Native pooled card", root).AddComponent<AbilityCardUI>();
        RectTransform print = (RectTransform)Go("FullAbilityCard", card.transform).transform;
        print.sizeDelta = new Vector2(294f, 450f); print.pivot = new Vector2(.31f, .68f);
        print.anchoredPosition = new Vector2(19f, -11f); print.localScale = Vector3.one * .84f;
        card.fullAbilityCard = print; highlighter.Card = card;
        Image area = Image("Selectable native printed row", print, new Vector2(47f, -62f), new Vector2(171f, 82f), Color.cyan);
        area.gameObject.AddComponent<UIEnhancementButtonHighlight>();
        RectTransform physical = (RectTransform)Go("Actual physical printed front", owner).transform;
        physical.sizeDelta = new Vector2(294f, 450f); physical.localScale = Vector3.one * .00049f;
        physical.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        physical.gameObject.AddComponent<CanvasGroup>();
        Image("Actual original artwork", physical, Vector2.zero, new Vector2(294f, 450f), Color.red);
        TownServiceEnhancementHandoff.PhysicalCardFace = physical;
        var mask = card.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>(); mask.Mask();
        mask.SendMessage("LateUpdate");
        TownServiceMirror.RegisterTemplate(3, 1, root, address: "enchant.holder|");
        TownServiceMirror.RegisterTemplate(3, 2, physical, address: "face.629|");
        TownServiceMirror.BeginSession(3, 629, owner, owner);
        TownServiceMirror.RegisterModule(10, 1, root, address: "enchant.holder|");
        TownServiceMirror.RegisterModule(11, 2, physical, address: "face.629|");
        TownServiceMirror.SetPriority(10, true); TownServiceMirror.SetPriority(11, true);
        FastCapture first = OfferedCapture629(); Receive(1, first.Artwork); DeliverMotion(1, first);
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding holder = Remote(1, 10)!, face = Remote(1, 11)!;
        Check(holder != null && face != null, "offered proof admits both independent physical artwork and original native overlays");
        RectTransform remotePrint = (RectTransform)holder.Root.Find("Native pooled card/FullAbilityCard");
        float clock = Time.unscaledTime + 1f;
        physical.rotation = Quaternion.Euler(17f, 91f, -9f); mask.SendMessage("LateUpdate");
        FastCapture turned = OfferedCapture629(); OfferedReceive629(turned, clock); OfferedRender629(clock);
        OfferedRender629(clock + .025f);
        Check(Quaternion.Angle(remotePrint.rotation, face.Root.rotation) < .02f,
            "independent offered print and native overlays share the same intermediate owner rotation");
        area.color = Color.green;
        FastCapture hovered = OfferedCapture629(); OfferedReceive629(hovered, clock + .03f); OfferedRender629(clock + .03f);
        OfferedRender629(clock + .055f);
        Check(Quaternion.Angle(remotePrint.rotation, face.Root.rotation) < .02f,
            "native hover packets cannot restart offered overlay root rotation independently of its physical print");
        mask.Restore(); TownServiceEnhancementHandoff.PhysicalCardFace = null;
        TownServiceMirror.Shutdown(); UnityEngine.Object.DestroyImmediate(owner.gameObject); UnityEngine.Object.DestroyImmediate(observer.gameObject);
    }
}
