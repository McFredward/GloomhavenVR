using System;
using System.Collections;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static IEnumerator FastOfferingLifetime()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 2;
        Transform owner = Go("fast offering native author").transform;
        Transform viewer = Go("fast offering native observer").transform;
        Transform inventory = Rect("Unrelated current inventory", owner, Vector2.zero, new Vector2(100, 100));
        inventory.gameObject.AddComponent<Image>().color = Color.green;
        Transform offering = Rect("Exact native merchant offering request", owner, Vector2.zero, new Vector2(80, 120));
        offering.gameObject.AddComponent<Image>().color = Color.cyan;
        offering.gameObject.AddComponent<CanvasGroup>().alpha = 0f; // Parked card hides the ghost, not the request.
        TownServiceMirror.RegisterTemplate(1, 1, inventory, address: "merchant.inventory|");
        TownServiceMirror.RegisterTemplate(1, 1, offering, address: TownServiceMirror.MerchantOfferingAddress);
        TownServiceMirror.BeginSession(1, 881, owner, owner);
        TownServiceMirror.RegisterModule(4, 1, inventory, address: "merchant.inventory|");
        TownServiceMirror.RegisterModule(9, 1, offering, address: TownServiceMirror.MerchantOfferingAddress);
        TownServiceMirror.SetPriority(9, true);
        CaptureLiveOffering(2, false);
        NetPlayerActors.Peer = 10;
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => viewer);
        Check(TownServiceMirror.RemoteMerchantOffering,
            "parked native merchant offer remains requested even when its original ghost alpha is zero");
        float began = Time.unscaledTime;
        int sampled = 0;
        // Drop every subsequent immutable offering-art packet while continuing
        // native capture and the direct validated numeric lane. Unrelated traffic
        // alone cannot refresh this exact request; its own root heartbeat must.
        while (Time.unscaledTime - began < 6.2f)
        {
            NetPlayerActors.Peer = 2; CaptureLiveOffering(2, true);
            NetPlayerActors.Peer = 10; TownServiceMirror.TickRemote(_ => viewer);
            if (Time.unscaledTime - began > 3.1f)
            { Check(TownServiceMirror.RemoteMerchantOffering,
                "exact fast root heartbeats keep a live parked offer beyond six seconds without immutable artwork refresh"); sampled++; }
            yield return null;
        }
        Check(sampled > 4, "offering lifetime proof spans multiple independent direct-heartbeat intervals");
        offering.gameObject.SetActive(false);
        for (float until = Time.unscaledTime + TownServiceMotionCodec.SendInterval + .02f; Time.unscaledTime < until;) yield return null;
        NetPlayerActors.Peer = 2; CaptureLiveOffering(2, true);
        NetPlayerActors.Peer = 10; TownServiceMirror.TickRemote(_ => viewer);
        Check(!TownServiceMirror.RemoteMerchantOffering,
            "explicit fast native withdrawal closes the palm immediately before any immutable artwork packet");
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
    }

    private static void CaptureLiveOffering(int peer, bool dropOfferingArt)
    {
        // Actual production overload: immutable content and direct numeric packets
        // are demultiplexed exactly as the real driver does, then decoded/apply.
        TownServiceMirror.Capture((bytes, length, sent) =>
        {
            if (sent is TownServiceMotionPacket)
            {
                Check(TownServiceMotionCodec.TryRead(bytes, length, out var motion), "real fast offering packet decodes");
                Check(TownServiceMirror.ReceiveMotion(peer, motion!), "real fast offering packet enters its affinitized receiver");
                return;
            }
            Check(TownServiceCodec.TryRead(bytes, length, out var frame), "real immutable offering packet decodes");
            TownServiceDelivery.Completed?.Invoke(frame!);
            if (!dropOfferingArt || frame!.Module != 9) TownServiceMirror.Receive(peer, bytes, length);
        });
    }
}
