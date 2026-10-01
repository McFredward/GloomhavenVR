using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static Transform StockFace(Transform mount, string name, Color color)
    {
        Transform root = Rect(name, mount, Vector2.zero, new Vector2(90, 130));
        root.localScale = Vector3.one * .002f;
        root.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        root.gameObject.AddComponent<Image>().color = color;
        root.gameObject.AddComponent<GameplayFixture>();
        return root;
    }
    private static int StockPeerKey(int peer) => ((Dictionary<int, int>)typeof(TownServiceMirror)
        .GetField("StockKeys", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null))[peer];
    private static IEnumerator VisitorStockLanes()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 2;
        Transform owner = Go("stock visitor owner frame").transform;
        Transform observer = Go("stock visitor observer frame").transform;
        Transform npc = StockFace(owner, "original temple book", Color.blue);
        Transform empty = Go("original empty physical card mount").transform;
        Transform rack = Go("shared original stock slot", owner).transform;
        Transform rackFace = StockFace(rack, "original item front", Color.green);
        Transform held = Go("visitor held original stock", owner).transform;
        held.localPosition = new Vector3(.6f, .4f, -.2f);
        Transform heldFace = StockFace(held, "original item front", Color.green);
        Transform heldBody = StockFace(held, "original stock backing", Color.gray);
        Transform heldPrice = StockFace(held, "original stock price", Color.yellow);
        Func<Transform, bool> excludeChildren = node => node.parent == held || node.parent == rack;
        TownServiceMirror.RegisterTemplate(2, 1, npc, address: "temple.counter|");
        TownServiceMirror.RegisterTemplate(1, 1, empty, address: "merchant.cardmount|");
        TownServiceMirror.RegisterTemplate(1, 1, empty, address: "merchant.heldstock|");
        TownServiceMirror.RegisterTemplate(1, 1, heldFace, address: "item.77|");
        TownServiceMirror.RegisterTemplate(1, 1, heldBody, address: "merchant.heldstock.body|");
        TownServiceMirror.RegisterTemplate(1, 1, heldPrice, address: "merchant.heldstock.row|");
        TownServiceMirror.BeginSession(2, 1, owner, owner);
        TownServiceMirror.RegisterModule(7, 1, npc, address: "temple.counter|");
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceMirror.BeginSession(1, 1, owner, owner);
            TownServiceMirror.RegisterModule(7, 1, rack, excludeChildren, "merchant.cardmount|");
            TownServiceMirror.RegisterModule(8, 1, rackFace, address: "item.77|");
        }
        using (TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1, 1, owner, owner);
            TownServiceMirror.RegisterModule(7, 1, held, excludeChildren, "merchant.heldstock|");
            TownServiceMirror.RegisterModule(8, 1, heldFace, address: "item.77|");
            TownServiceMirror.RegisterModule(9, 1, heldBody, address: "merchant.heldstock.body|");
            TownServiceMirror.RegisterModule(10, 1, heldPrice, address: "merchant.heldstock.row|");
        }
        List<byte[]> packets = Capture();
        object Lane(string name) => typeof(TownServiceMirror).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        IDictionary Parents(object lane) => (IDictionary)lane.GetType().GetField("Parents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(lane);
        IDictionary privateParents = Parents(Lane("PrivateLane")), publicParents = Parents(Lane("PublicLane")), stockParents = Parents(Lane("StockLane"));
        Check(!ReferenceEquals(privateParents, publicParents) && !ReferenceEquals(publicParents, stockParents)
            && privateParents.Contains(npc) && !privateParents.Contains(heldFace)
            && publicParents.Contains(rackFace) && !publicParents.Contains(heldFace)
            && stockParents.Contains(heldFace) && !stockParents.Contains(rackFace),
            "original module ancestry is scoped to each presentation lane");

        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceMirror.RegisterModule(11, 1, held, excludeChildren, "merchant.cardmount|");
            TownServiceMirror.RegisterModule(12, 1, heldFace, address: "item.77|");
            TownServiceMirror.UnregisterModule(11); TownServiceMirror.UnregisterModule(12);
        }
        TownServiceMirror.RequestFullRefresh();
        List<byte[]> afterRetirement = Capture();
        bool linked = false;
        foreach (byte[] bytes in afterRetirement)
            if (TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame)
                && frame!.VisitorStock && frame.Module == 8) linked = frame.ParentModule == 7;
        Check(linked && stockParents.Contains(heldFace),
            "retiring the former public borrower cannot erase the surviving stock original parent");
        packets = afterRetirement;
        int sameId = 0;
        foreach (byte[] bytes in packets)
            if (TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame) && frame!.Module == 7) sameId++;
        Check(sameId == 3, "three original lanes retain identical session and module IDs independently");
        NetPlayerActors.Peer = 3; Receive(2, packets); TownServiceMirror.InteractionOwner(2);
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        var supplied = new List<int>();
        TownServiceMirror.TickRemote(peer => { supplied.Add(peer); return observer; });
        int stockKey = StockPeerKey(2);
        Check(supplied.TrueForAll(peer => peer == 2), "all mirror lanes resolve the real sender frame rather than virtual namespace IDs");
        Check(TownServiceMirror.PublicAuthor == 2 && TownServiceMirror.InteractionOwner(2) == 2
            && TownServiceMirror.InteractionOwner(1) == 0,
            "independent held stock neither elects an NPC visitor nor replaces the public cabinet author");
        Check(Remote(2, 7) != null && Remote(stockKey, 7) != null && Remote(stockKey, 8) != null
            && Remote(stockKey, 9) != null && Remote(stockKey, 10) != null,
            "observer retains a private NPC interaction and the other hand's full original stock sample");
        Check(TownServiceMirror.StockItemHeldByOther(77) && !TownServiceMirror.StockItemHeldByOther(78),
            "only the live typed stock mount vacates its exact public item slot");
        Check(!Remote(-2, 7)!.Root.parent.gameObject.activeSelf && !Remote(-2, 8)!.Root.gameObject.activeInHierarchy,
            "observer suppresses the whole duplicate public shelf sample while it is held");
        Check(Remote(stockKey, 8)!.Root.GetComponent<Image>().color == Color.green
            && Remote(stockKey, 9)!.Root.GetComponent<Image>().color == Color.gray
            && Remote(stockKey, 10)!.Root.GetComponent<Image>().color == Color.yellow,
            "original stock front, backing and price all retain their owner presentation");
        Check(Remote(stockKey, 8)!.Root.GetComponentsInChildren<GameplayFixture>(true).Length == 0,
            "stock observer front contains no gameplay controller or callback");
        Vector3 shown = Remote(stockKey, 7)!.Root.position;
        NetPlayerActors.Peer = 2; held.localPosition += new Vector3(.2f, -.1f, .05f);
        TownServiceMirror.RequestFullRefresh(); var moved = Capture();
        NetPlayerActors.Peer = 3; Receive(2, moved); TownServiceMirror.TickRemote(_ => observer);
        Check(Vector3.Distance(Remote(stockKey, 7)!.Root.position, shown) < .0001f,
            "held stock movement starts at its displayed original pose");
        for (float until = Time.unscaledTime + .12f; Time.unscaledTime < until;)
        { yield return null; TownServiceMirror.TickRemote(_ => observer); }
        Check(Vector3.Distance(Remote(stockKey, 7)!.Root.position, shown) > .001f,
            "stock root moves smoothly under its independent owner sample clock");

        // A new public author can replace/repage the cabinet without retiring this
        // visitor's original hold or the unrelated NPC interaction in their other hand.
        NetPlayerActors.Peer = 4;
        using (TownServiceMirror.UsePublicLane()) TownServiceMirror.ClaimPublicCatalog();
        var newAuthor = Capture();
        newAuthor.RemoveAll(bytes => !TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame) || !frame!.PublicCatalog);
        using (TownServiceMirror.UsePublicLane()) TownServiceMirror.EndSession();
        NetPlayerActors.Peer = 3; Receive(4, newAuthor);
        TownServiceMirror.TickRemote(_ => observer);
        Check(TownServiceMirror.PublicAuthor == 4 && Remote(stockKey, 8) != null,
            "public category authorship handover cannot dislodge another visitor's stock hold");
        Check(TownServiceMirror.StockItemHeldByOther(77), "new author still sees the visitor-owned slot reservation");

        // A newly joined observer receives complete originals for each independent
        // lane, even after a different player became the current cabinet author.
        TownServiceMirror.ResetNetwork(); NetPlayerActors.Peer = 6;
        Receive(2, afterRetirement); Receive(4, newAuthor); TownServiceMirror.InteractionOwner(2);
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => observer); stockKey = StockPeerKey(2);
        Check(TownServiceMirror.PublicAuthor == 4 && Remote(stockKey, 8) != null
            && Remote(stockKey, 8)!.Root.GetComponent<Image>().color == Color.green
            && !Remote(-4, 7)!.Root.parent.gameObject.activeSelf,
            "late observer receives the surviving held original and current public shelf ownership");

        NetPlayerActors.Peer = 2;
        using (TownServiceMirror.UseStockLane()) TownServiceMirror.EndSession();
        var returned = Capture(); NetPlayerActors.Peer = 3; Receive(2, returned); TownServiceMirror.TickRemote(_ => observer);
        Check(!TownServiceMirror.StockItemHeldByOther(77) && Remote(stockKey, 7) == null,
            "finished return/decision retires stock membership immediately");
        Check(Remote(-4, 7) != null && Remote(-4, 7)!.Root.parent.gameObject.activeSelf,
            "retired held membership restores the selected public shelf original");
        TownServiceMirror.RemovePeer(2); TownServiceMirror.TickRemote(_ => observer);
        Check(!TownServiceMirror.RemoteSessions.ContainsKey(2), "disconnect removes the visitor without touching the other public author");
        TownServiceMirror.ResetNetwork();
        Check(TownServiceMirror.RemoteSessions.Count == 0 && !TownServiceMirror.StockItemHeldByOther(77),
            "network reset releases all independent stock and visitor membership");
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
    }
}
