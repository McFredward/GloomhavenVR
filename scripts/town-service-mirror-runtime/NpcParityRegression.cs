using System;
using System.Collections;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static IEnumerator RetainValidatedOriginalOnMissingAsset()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 10;
        Transform author = Go("validated item author").transform;
        Transform viewer = Go("validated item observer").transform;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "npc611-original-regression-art" };
        texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.yellow }); texture.Apply(); Assets.Add(texture);
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.one * .5f);
        sprite.name = "npc611-original-front"; Assets.Add(sprite);
        Transform original = Rect("Original item face", author, Vector2.zero, new Vector2(150f, 150f));
        original.gameObject.AddComponent<Image>().sprite = sprite;
        TownServiceMirror.RegisterTemplate(1, 1, original, address: "item.611|");
        TownServiceMirror.BeginSession(1, 611, author, author);
        TownServiceMirror.RegisterModule(1, 1, original, address: "item.611|");
        List<byte[]> packets = Capture(); TownServiceMirror.EndSession();
        TownServiceFrame? captured = null;
        foreach (byte[] packet in packets)
        {
            TownServiceCodec.TryRead(packet, packet.Length, out TownServiceFrame? frame);
            if (frame!.Module == 1) captured = frame;
            TownServiceMirror.Receive(2, packet, packet.Length);
        }
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => viewer);
        Transform copy = Remote(2, 1)!.Root;
        Check(copy != null && copy.gameObject.activeInHierarchy && copy.GetComponent<Image>().sprite == sprite,
            "complete original item artwork enters the real remote capture/playback path");
        TownServiceFrame bad = TownServiceDelta.Copy(captured!);
        bad.Sequence += 10;
        var image = bad.Nodes[0].Values[TownServiceProperty.Image];
        bad.Nodes[0].Values[TownServiceProperty.Image] = new TownServiceValue
        { Numbers = image.Numbers, Text = new[] { "sprite|npc611-unloaded-original", image.Text[1] } };
        byte[] absent = TownServiceCodec.Write(bad);
        TownServiceMirror.Receive(2, absent, absent.Length); TownServiceMirror.TickRemote(_ => viewer);
        Check(copy.gameObject.activeInHierarchy && copy.GetComponent<Image>().sprite == sprite,
            "a missing next dependency retains the last validated original front instead of blinking grey");
        // A later complete owner picture still advances after the bounded retry;
        // holding the old picture is recovery, never swallowing the next update.
        TownServiceFrame recovered = TownServiceDelta.Copy(captured!);
        recovered.Sequence = bad.Sequence + 1; recovered.SampleTime += .2f;
        recovered.Pose[0] += .15f;
        byte[] ready = TownServiceCodec.Write(recovered);
        TownServiceMirror.Receive(2, ready, ready.Length);
        for (float until = Time.unscaledTime + .26f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => viewer);
        for (float until = Time.unscaledTime + .24f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => viewer);
        Check(copy.gameObject.activeInHierarchy && Mathf.Abs(viewer.InverseTransformPoint(copy.position).x - .15f) < .0001f,
            "dependency recovery applies the latest actual owner pose after retaining original art");
        TownServiceFrame different = TownServiceDelta.Copy(recovered);
        different.Sequence++; different.TemplateAddress = "item.612|";
        byte[] replacement = TownServiceCodec.Write(different);
        TownServiceMirror.Receive(2, replacement, replacement.Length); TownServiceMirror.TickRemote(_ => viewer);
        Check(!copy.gameObject.activeInHierarchy,
            "a different unavailable original may not borrow the preceding item's validated artwork");
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
    }
}
