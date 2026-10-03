using System;
using System.Collections;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static Transform CardWithLocalPads(Transform parent, bool pads, bool visiblePad = false)
    {
        Transform card = Rect("Original ability face", parent, Vector2.zero, new Vector2(294f, 450f));
        card.gameObject.AddComponent<Image>().color = new Color(.25f, .6f, .8f, 1f);
        for (int n = 0; n < 2; n++)
        {
            Transform action = Rect(n == 0 ? "Original top action" : "Original bottom action", card,
                new Vector2(0f, n == 0 ? 120f : -120f), new Vector2(250f, 170f));
            action.gameObject.AddComponent<Image>().color = new Color(.6f, .4f, .2f, 1f);
            if (!pads) continue;
            Transform pad = Rect("GloomhavenVR.PokePad", action, Vector2.zero, new Vector2(280f, 200f));
            pad.gameObject.AddComponent<GloomhavenVR.Hands.Interact.PokeOnlyTarget>();
            pad.gameObject.AddComponent<Image>().color = new Color(1f, 0f, 0f, visiblePad ? .5f : 0f);
        }
        return card;
    }

    private static IEnumerator PooledOriginalCardTopology()
    {
        for (int direction = 0; direction < 2; direction++)
        {
            TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 10;
            Transform author = Go("card pooling author").transform;
            Transform viewer = Go("card pooling observer").transform;
            Transform template = CardWithLocalPads(Go("borrowed native source").transform, direction == 1);
            Transform source = CardWithLocalPads(author, direction == 0);
            using (var sender = new TownServiceBinding(source))
            using (var provenance = new TownServiceBinding(template))
                Check(sender.Structure == provenance.Structure && sender.Nodes.Length == 3 && provenance.Nodes.Length == 3,
                    "pooled original card structure ignores only invisible local fingertip pads in either direction");
            TownServiceMirror.RegisterTemplate(3, 1, template, address: "card.611|");
            TownServiceMirror.BeginSession(3, (uint)(620 + direction), author, author);
            TownServiceMirror.RegisterModule(1, 1, source, address: "card.611|");
            List<byte[]> packets = Capture(); TownServiceMirror.EndSession();
            foreach (byte[] packet in packets) TownServiceMirror.Receive(2, packet, packet.Length);
            for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
            TownServiceMirror.TickRemote(_ => viewer);
            TownServiceBinding? observed = Remote(2, 1);
            Check(observed != null && observed.Root.gameObject.activeInHierarchy && observed.Nodes.Length == 3,
                "actual pooled original card capture and playback builds the complete remote front in either pad direction");
            Check(observed!.Root.GetComponent<Image>().color == source.GetComponent<Image>().color
                && observed.Root.Find("Original top action/GloomhavenVR.PokePad") == null
                && observed.Root.Find("Original bottom action/GloomhavenVR.PokePad") == null,
                "observer retains every original visible action without cloning local input-only pads");
            // Native code may add its fingertip geometry after publication. The
            // same visible tree stays addressable; this is not a new card epoch.
            if (direction == 1)
            {
                Transform pad = Rect("GloomhavenVR.PokePad", source.GetChild(0), Vector2.zero, new Vector2(280f, 200f));
                pad.gameObject.AddComponent<GloomhavenVR.Hands.Interact.PokeOnlyTarget>();
                pad.gameObject.AddComponent<Image>().color = Color.clear;
                using var late = new TownServiceBinding(source);
                Check(late.Structure == observed.Structure && late.Nodes.Length == observed.Nodes.Length,
                    "late native fingertip-pad insertion cannot change the remote original card topology");
            }
        }
        TownServiceMirror.Shutdown(); Baselines.Clear();
        Transform visible = CardWithLocalPads(Go("visible marker source").transform, true, true);
        using var retained = new TownServiceBinding(visible);
        Check(retained.Nodes.Length == 5,
            "a visible marked original child is presentation and is never pruned as an invisible input pad");
        NetPlayerActors.Peer = 1;
    }

    private static IEnumerator SecondaryVisitorOriginalPartitions()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 10;
        Transform author = Go("partitioned item author").transform;
        Transform viewer = Go("partitioned item viewer").transform;
        Transform item = Rect("Original partitioned item", author, Vector2.zero, new Vector2(294f, 450f));
        item.gameObject.AddComponent<Image>().color = Color.blue;
        Transform description = Rect("Original item description", item, Vector2.zero, new Vector2(250f, 300f));
        description.gameObject.AddComponent<Image>().color = Color.green;
        TownServiceMirror.RegisterTemplate(1, 1, item, t => t == description, "item.611|");
        TownServiceMirror.RegisterTemplate(1, 1, description, address: "item.611|native-original-part");
        TownServiceMirror.BeginSession(1, 631, author, author);
        TownServiceMirror.RegisterModule(1, 1, item, t => t == description, "item.611|");
        TownServiceMirror.RegisterModule(2, 1, description, address: "item.611|native-original-part");
        List<byte[]> frames = Capture(); TownServiceMirror.EndSession();
        foreach (byte[] frame in frames)
        { TownServiceMirror.Receive(2, frame, frame.Length); TownServiceMirror.Receive(3, frame, frame.Length); }
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => viewer);
        Check(TownServiceMirror.InteractionOwner(1) == 2
            && Remote(2, 1) != null && Remote(2, 2) != null && Remote(3, 1) != null && Remote(3, 2) != null,
            "both visitors retain every exact original item partition while one visitor authors the merchant controls");
        Check(Remote(3, 2)!.Root.GetComponent<Image>().color == Color.green
            && Remote(3, 2)!.Root.IsChildOf(Remote(3, 1)!.Root),
            "a secondary visitor's native item partition retains its real original hierarchy and appearance");
        TownServiceMirror.Shutdown(); Baselines.Clear();
        Transform face = Rect("Released original ability face", author, Vector2.zero, new Vector2(294f, 450f));
        face.gameObject.AddComponent<Image>().color = Color.cyan;
        Transform body = Go("Released original ability backing").transform; body.SetParent(author, false);
        TownServiceMirror.RegisterTemplate(3, 1, face, address: "face.611|");
        TownServiceMirror.RegisterTemplate(3, 1, body, address: "map.cardbody|");
        TownServiceMirror.BeginSession(3, 632, author, author);
        TownServiceMirror.RegisterModule(1, 1, face, address: "face.611|");
        TownServiceMirror.RegisterModule(2, 1, body, address: "map.cardbody|");
        frames = Capture(); TownServiceMirror.EndSession();
        foreach (byte[] frame in frames)
        { TownServiceMirror.Receive(2, frame, frame.Length); TownServiceMirror.Receive(3, frame, frame.Length); }
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => viewer);
        Check(TownServiceMirror.InteractionOwner(3) == 2
            && Remote(2, 1) != null && Remote(2, 2) != null && Remote(3, 1) != null && Remote(3, 2) != null,
            "released original mage face and backing remain visible for every visitor outside the exclusive parked offer");
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
    }

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
