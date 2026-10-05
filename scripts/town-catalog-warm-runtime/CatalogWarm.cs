using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using TownServiceAssets = GloomhavenVR.WorldUI.TownServiceAssets;

public static partial class MirrorProgram
{
    private static IEnumerator CatalogWarmClock()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 1;
        Transform owner = Go("actual housing source frame").transform, viewer = Go("warm original observer").transform;
        TownServiceAssets.Furniture = Resources.Load<GameObject>("TownServices/Prefabs/TownMerchant");
        Check(TownServiceAssets.Furniture != null, "actual authored merchant prefab is loaded from current source assets");
        Transform housing = TownServiceMerchantDrawer.CreateHousingTemplate().transform;
        housing.SetParent(owner, false); Objects.Add(housing.gameObject);
        Transform crank = TownServiceMerchantDrawer.CreateTemplate(null).transform; crank.SetParent(owner, false); Objects.Add(crank.gameObject);
        var excluded = new HashSet<Transform>();
        var canvases = new List<Canvas>(); var bodies = new List<MeshRenderer>();
        var sources = new Dictionary<ushort, Transform>();
        for (int page = 0; page < 2; page++)
        {
            Transform mount = Go("owner original mount " + page, housing.Find("Cassette/Row0")).transform; excluded.Add(mount);
            Transform face = Rect("owner original face " + page, mount, Vector2.zero, new Vector2(80, 120));
            Canvas canvas = face.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            face.gameObject.AddComponent<Image>().color = page == 0 ? Color.blue : Color.yellow;
            canvases.Add(canvas); canvas.enabled = page == 0;
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "owner original body " + page;
            body.transform.SetParent(mount, false); body.transform.localScale = new Vector3(.02f, .001f, .03f);
            bodies.Add(body.GetComponent<MeshRenderer>()); bodies[page].forceRenderingOff = page != 0;
            ushort id = (ushort)(20 + page * 10);
            sources[id] = mount; sources[(ushort)(id + 1)] = face; sources[(ushort)(id + 2)] = body.transform;
            TownServiceMirror.RegisterTemplate(1, 1, mount, t => t == face || t == body.transform, "merchant.cardmount|" + page);
            TownServiceMirror.RegisterTemplate(1, 1, face, address: "item." + (62200 + page) + "|");
            TownServiceMirror.RegisterTemplate(1, 1, body.transform, address: "merchant.cardbody|" + page);
        }
        TownServiceMirror.RegisterTemplate(1, 1, housing, t => excluded.Contains(t), "merchant.rack|");
        TownServiceMirror.RegisterTemplate(1, 1, crank, address: "merchant.crank|");
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceMirror.BeginSession(1, 622, owner, housing);
            TownServiceMirror.RegisterModule(10, 1, housing, t => excluded.Contains(t), "merchant.rack|");
            TownServiceMirror.RegisterModule(11, 1, crank, address: "merchant.crank|");
            foreach (var source in sources)
            {
                string address = source.Key % 10 == 0 ? "merchant.cardmount|" + (source.Key / 10 - 2)
                    : source.Key % 10 == 1 ? "item." + (62200 + source.Key / 10 - 2) + "|" : "merchant.cardbody|" + (source.Key / 10 - 2);
                Func<Transform, bool>? skip = source.Key % 10 == 0 ? t => t == sources[(ushort)(source.Key + 1)] || t == sources[(ushort)(source.Key + 2)] : null;
                TownServiceMirror.RegisterModule(source.Key, 1, source.Value, skip, address);
                TownServiceMirror.SetCatalogSource(source.Key, 1, source.Key >= 30);
                TownServiceMirror.SetRackMember(source.Key, 10, (ushort)(source.Key >= 30 ? 1280 : 0), 0, false, null);
            }
            TownServiceMirror.SetCatalogBankPrepared(10, true);
            TownServiceMirror.SetRack(10, new TownRackState { Cassette = true, Crank = 11,
                Members = sources.Keys.Where(id => id < 30).Select(id => new TownRackMember(id, 0, false)).ToArray() });
        }
        List<TownServiceFrame> captured = Capture().Select(Decode).ToList();
        var originals = captured.Where(f => f.RackMember != null && f.BaseSequence == 0).GroupBy(f => f.Module).ToDictionary(g => g.Key, g => g.Last());
        Check(originals.Count == 6, "production loading capture genuinely sends dormant original modules before exposure");
        TownServiceFrame initial = captured.Single(f => f.Module == 10);
        Check(initial.Nodes.Length > 1 && initial.Nodes.Any(n => n.Values.ContainsKey(TownServiceProperty.Mesh)),
            "timing root is the production factory housing with actual authored geometry");
        TownServiceFrame measured = TownServiceDelta.Copy(initial); measured.CatalogBank = null; measured.Rack!.Members = Array.Empty<TownRackMember>();
        File.WriteAllBytes(Path.Combine(_output, "native-housing.packet"), TownServiceCodec.Write(measured));
        File.WriteAllText(Path.Combine(_output, "housing.txt"), "actual housing nodes=" + measured.Nodes.Length + " bytes=" + TownServiceCodec.Write(measured).Length + "\n");
        NetPlayerActors.Peer = 2;
        var queue = new NetAvatarDriver();
        foreach (var frame in captured) queue.QueueFixture(1, TownServiceCodec.Write(frame));
        queue.ApplyFixture(); TownServiceMirror.TickRemote(_ => viewer);
        Check(Remote(-1, 30) == null && Remote(-1, 31) == null && Remote(-1, 32) == null,
            "dormant prewarm retains exact frames without constructing visible off-page clones");
        NetPlayerActors.Peer = 1;
        canvases[1].enabled = true; bodies[1].forceRenderingOff = false;
        using (TownServiceMirror.UsePublicLane())
        {
            foreach (var source in sources)
            { TownServiceMirror.SetCatalogDormant(source.Key, false); TownServiceMirror.SetRackMember(source.Key, 10, (ushort)(source.Key >= 30 ? 1280 : 0), 1, false, null); }
            TownServiceMirror.SetRack(10, new TownRackState { Cassette = true, Crank = 11, Turn = 1, From = 0, To = 1280, Page = 1280, Elapsed = .46f,
                Members = sources.Keys.Select(id => new TownRackMember(id, (ushort)(id >= 30 ? 1280 : 0), false)).ToArray() });
        }
        TownServiceFrame next = Capture().Select(Decode).Last(f => f.Module == 10);
        TownServiceFrame warm = TownCatalogClock.Create(next, originals);
        Check(warm.CatalogBank!.HeaderBaseKeys.Count(key => key != 0) >= 2
            && warm.CatalogBank.Headers.Count(h => h.Nodes.Any(n => n.Values.ContainsKey(TownServiceProperty.Canvas) || n.Values.ContainsKey(TownServiceProperty.Mesh))) >= 2,
            "actual captured Canvas and mesh exposure changes produce genuine original property patches");
        NetPlayerActors.Peer = 2;
        Check(TownServiceMirror.Receive(1, TownServiceCodec.Write(warm), TownServiceCodec.Write(warm).Length),
            "prepared far page accepts actual captured native toggle deltas before the full bank arrives");
        Check(PendingFrame(-1, 31).Nodes.Any(n => n.Values.TryGetValue(TownServiceProperty.Canvas, out var value) && value.Numbers[0] == 1f)
            && PendingFrame(-1, 32).Nodes.Any(n => n.Values.TryGetValue(TownServiceProperty.Mesh, out var value) && value.Numbers[1] == 0f),
            "warm reconstruction preserves actual new original Canvas enabled and body forceRenderingOff values");
        TownServiceFrame claim = TownServiceDelta.Copy(next); claim.PublicClaim++;
        foreach (TownServiceFrame original in claim.CatalogBank!.Updates) { original.PublicClaim = claim.PublicClaim; original.Sequence += 100; }
        claim.Sequence += 200; claim.Rack!.Turn++;
        foreach (TownServiceFrame original in claim.CatalogBank.Updates) original.RackMember!.Turn = claim.Rack.Turn;
        var reauthorized = TownCatalogClock.Create(claim, originals);
        Check(ReceiveWarm(1, reauthorized), "same actual sender/session explicitly reauthorizes exact native content under a higher claim");
        Check(PendingFrame(-1, 31).PublicClaim == claim.PublicClaim && PendingFrame(-1, 31).RackMember!.Turn == claim.Rack.Turn,
            "higher claim installs current owner header metadata instead of stale off-page metadata");
        Check(!ReceiveWarm(1, warm), "stale old claim cannot overwrite a prepared new claim");
        Check(!ReceiveWarm(3, reauthorized), "different actual sender cannot borrow another peer's originals");
        var foreign = TownServiceDelta.Copy(claim); foreign.Session++;
        foreach (var update in foreign.CatalogBank!.Updates) update.Session = foreign.Session;
        Check(!ReceiveWarm(1, TownCatalogClock.Create(foreign, originals)), "different session requires complete genuine originals");
        var changed = TownServiceDelta.Copy(claim); changed.Sequence += 300;
        changed.CatalogBank!.Updates[1].Nodes[0].Values[TownServiceProperty.Graphic].Numbers[1] = .123f;
        changed.CatalogBank.Members[1] = new(changed.CatalogBank.Updates[1].Module, TownCatalogBank.ContentKey(changed.CatalogBank.Updates[1]));
        var changedRefs = TownCatalogClock.Create(changed, new Dictionary<ushort, TownServiceFrame>());
        Check(!ReceiveWarm(1, changedRefs), "changed native content without the exact patch base stays incomplete");
        Check(ReceiveWarm(1, changed), "complete same-sequence fallback repairs a rejected reference clock");
        TownServiceMirror.RemovePeer(1);
        Check(!ReceiveWarm(1, reauthorized), "departed public sender clears all dormant native revisions");
        var coldQueue = new NetAvatarDriver(); coldQueue.QueueFixture(4, TownServiceCodec.Write(reauthorized)); coldQueue.QueueFixture(4, TownServiceCodec.Write(claim));
        Check(coldQueue.QueuedFixture(4) == 2, "main-thread coalescing retains rejected references and complete repair with the same source sequence");
        coldQueue.ApplyFixture(); Check(PendingFrame(-4, 31).PublicClaim == claim.PublicClaim, "same-sequence delayed full bank is actually admitted after a cold reference rejection");
        TownServiceMirror.ResetNetwork();
        Check(!ReceiveWarm(4, reauthorized), "network reset also clears dormant original banks without any living remote host");
        yield return null;
    }
    private static bool ReceiveWarm(int peer, TownServiceFrame frame)
    { byte[] packet = TownServiceCodec.Write(frame); return TownServiceMirror.Receive(peer, packet, packet.Length); }
    private static TownServiceFrame PendingFrame(int peer, ushort id) => (TownServiceFrame)((IDictionary)((IDictionary)typeof(TownServiceMirror).GetField("Pending", PrivateStatic)!.GetValue(null)!)[peer]!)[id]!;
}
