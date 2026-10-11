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
        Transform housing = Go("original cabinet mechanism", owner).transform;
        Transform warmOne = Go("original warm page one mount", owner).transform;
        Transform warmOneFace = StockFace(warmOne, "original warm page one front", Color.cyan);
        Transform warmTwo = Go("original warm page two mount", owner).transform;
        Transform warmTwoFace = StockFace(warmTwo, "original warm page two front", Color.magenta);
        var rackState = new TownRackState { Cassette = true, PageCount = 3, Crank = 29,
            Page = 0, From = 0, To = 0, Elapsed = TownRackState.TurnDuration,
            Members = new[] { new TownRackMember(7, 0, false), new TownRackMember(8, 0, false),
                new TownRackMember(21, 1, false), new TownRackMember(22, 1, false),
                new TownRackMember(23, 2, false), new TownRackMember(24, 2, false) } };
        Func<Transform, bool> excludeChildren = node => node.parent == held || node.parent == rack
            || node.parent == warmOne || node.parent == warmTwo;
        TownServiceMirror.RegisterTemplate(2, 1, npc, address: "temple.counter|");
        TownServiceMirror.RegisterTemplate(1, 1, empty, address: "merchant.cardmount|");
        TownServiceMirror.RegisterTemplate(1, 1, housing, address: "merchant.rack|");
        TownServiceMirror.RegisterTemplate(1, 1, warmOneFace, address: "item.78|");
        TownServiceMirror.RegisterTemplate(1, 1, warmTwoFace, address: "item.79|");
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
            TownServiceMirror.RegisterModule(21, 1, warmOne, excludeChildren, "merchant.cardmount|");
            TownServiceMirror.RegisterModule(22, 1, warmOneFace, address: "item.78|");
            TownServiceMirror.RegisterModule(23, 1, warmTwo, excludeChildren, "merchant.cardmount|");
            TownServiceMirror.RegisterModule(24, 1, warmTwoFace, address: "item.79|");
            TownServiceMirror.RegisterModule(30, 1, housing, address: "merchant.rack|");
            TownServiceMirror.SetRack(30, rackState);
            foreach (ushort id in new ushort[] { 7, 8, 21, 22, 23, 24 })
                TownServiceMirror.SetRackMember(id, new TownRackStamp { Rack = 30, Page = id < 20 ? (ushort)0 : id < 23 ? (ushort)1 : (ushort)2 });
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
        NetPlayerActors.Peer = 3;
        var driver = new NetAvatarDriver();
        foreach (byte[] packet in packets) Check(driver.QueueFixture(2, packet), "actual avatar accepts complete immutable town packet");
        Check(driver.QueuedFixture(2) == packets.Count, "actual avatar queue retains all three original presentation lanes");
        driver.ApplyFixture(); TownServiceMirror.InteractionOwner(2);
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
        Check(Remote(-2, 21)!.Root.parent.GetComponent<CanvasGroup>().alpha == 0f
            && Remote(-2, 23)!.Root.parent.GetComponent<CanvasGroup>().alpha == 0f,
            "two prewarmed pages remain invisible under the current rack gate");
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
        using (TownServiceMirror.UsePublicLane())
        {
            rackState.Page = rackState.From = rackState.To = 1;
            TownServiceMirror.SetRack(30, rackState); TownServiceMirror.ClaimPublicCatalog();
        }
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
        TownServiceMirror.ResetNetwork();
        Check(privateParents.Contains(npc) && stockParents.Contains(heldFace),
            "network reconnect retains ancestry for unchanged original local bindings");
        NetPlayerActors.Peer = 6;
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
        Check(Remote(-4, 7) != null && !Remote(-4, 7)!.Root.parent.gameObject.activeSelf
            && Remote(-4, 7)!.Root.parent.GetComponent<CanvasGroup>().alpha == 0f
            && Remote(-4, 21)!.Root.parent.GetComponent<CanvasGroup>().alpha > .99f
            && Remote(-4, 23)!.Root.parent.GetComponent<CanvasGroup>().alpha == 0f,
            "retired stock return preserves the selected rack page and hidden warm originals");
        TownServiceMirror.RemovePeer(2); TownServiceMirror.TickRemote(_ => observer);
        Check(!TownServiceMirror.RemoteSessions.ContainsKey(2), "disconnect removes the visitor without touching the other public author");
        TownServiceMirror.ResetNetwork();
        Check(TownServiceMirror.RemoteSessions.Count == 0 && !TownServiceMirror.StockItemHeldByOther(77),
            "network reset releases all independent stock and visitor membership");
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
        IEnumerator inscriptions = SecondaryPurseInscriptions();
        while (inscriptions.MoveNext()) yield return inscriptions.Current;
        IEnumerator voices = IndependentStockVoice();
        while (voices.MoveNext()) yield return voices.Current;
    }

    private static IEnumerator SecondaryPurseInscriptions()
    {
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 10;
        Transform owner = Go("simultaneous purse owner frame").transform;
        Transform observer = Go("simultaneous purse observer frame").transform;
        Transform purse = StockFace(owner, "original held purse", Color.yellow);
        Transform counter = StockFace(owner, "original shared temple counter", Color.blue);
        Transform row = StockFace(owner, "original purse inscriptions", Color.white);
        TownServiceMirror.RegisterTemplate(2, 1, purse, address: "ritual.purse.held|");
        TownServiceMirror.RegisterTemplate(2, 1, counter, address: "temple.counter|");
        TownServiceMirror.RegisterTemplate(2, 1, row, address: "temple.row|");
        TownServiceMirror.BeginSession(2, 700, owner, owner);
        TownServiceMirror.RegisterModule(7, 1, purse, address: "ritual.purse.held|");
        TownServiceMirror.RegisterModule(8, 1, counter, address: "temple.counter|");
        TownServiceMirror.RegisterModule(9, 1, row, address: "temple.row|");
        List<byte[]> packets = Capture(); TownServiceMirror.EndSession();
        foreach (int peer in new[] { 2, 3 })
            foreach (byte[] bytes in packets)
            {
                Check(TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame),
                    "simultaneous original purse content decodes");
                frame!.Session = (uint)(700 + peer); frame.Sequence += 100;
                byte[] packet = TownServiceCodec.Write(frame);
                Check(TownServiceMirror.Receive(peer, packet, packet.Length), "simultaneous visitor purse and inscription packet is retained");
            }
        TownServiceMirror.InteractionOwner(2);
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(2, 8) != null && Remote(3, 8) == null && Remote(2, 7) != null && Remote(3, 7) != null,
            "secondary held purse never duplicates the original shared temple counter");
        Check(Remote(3, 9) != null && Remote(3, 9)!.Root.gameObject.activeInHierarchy
            && Remote(3, 9)!.Root.GetComponent<Image>().color == Color.white,
            "secondary visitor retains the original held-purse inscriptions");
        InteractionManifest(3, 2, 703, 200, modules: new ushort[] { 8, 9 });
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(3, 7) == null && !Remote(3, 9)!.Root.parent.gameObject.activeSelf
            && Remote(2, 9)!.Root.gameObject.activeInHierarchy,
            "returning one purse hides only that visitor's inscriptions");
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
    }
    // The recording scheduler boundary only observes the accepted cosmetic cue.
    // Publisher, three-lane capture, actual avatar queue, membership validation and
    // reordered network receive compile directly from production implementations.
    private static IEnumerator IndependentStockVoice()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 2;
        TownServiceVoice.Accepted.Clear(); TownServiceVoice.StockAccepted.Clear();
        Transform owner = Go("independent stock voice visitor").transform;
        Transform empty = Go("original stock voice mount").transform;
        Transform mount = Go("actual lifted stock voice sample", owner).transform;
        Transform item = StockFace(mount, "original stock voice front", Color.green);
        TownServiceMirror.RegisterTemplate(1, 1, empty, address: "merchant.heldstock|");
        TownServiceMirror.RegisterTemplate(1, 1, item, address: "item.77|");
        TownServiceMirror.BeginSession(3, 300, owner, owner);
        TownServiceMirror.InteractionOwner(3);
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceVoice.RelayRequest!(3, TownVoiceReaction.EnchantressEnhance);
        using (TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1, 400, owner, owner);
            TownServiceMirror.RegisterModule(7, 1, mount, node => node.parent == mount, "merchant.heldstock|");
            TownServiceMirror.RegisterModule(8, 1, item, address: "item.77|");
        }
        Check(TownServiceVoice.StockRelayRequest!(TownVoiceReaction.MerchantOffer)
            && !TownServiceVoice.StockRelayRequest(TownVoiceReaction.MerchantBuy),
            "independent stock pickup cue is cosmetic and needs no merchant transaction");
        List<byte[]> packets = Capture();
        byte[] Find(bool stock, ushort module) => packets.Find(bytes =>
            TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame)
            && frame!.VisitorStock == stock && frame.Module == module)!;
        byte[] stockVoice = Find(true, TownServiceFrame.VoiceModule);
        byte[] privateVoice = Find(false, TownServiceFrame.VoiceModule);
        Check(stockVoice != null && privateVoice != null,
            "starting a stock hold preserves the other NPC's queued private cue");
        Check(TownServiceCodec.TryRead(stockVoice, stockVoice.Length, out TownServiceFrame? stockCue)
            && stockCue!.Session == 400 && stockCue.Sequence == 1 && stockCue.VisitorStock
            && TownServiceCodec.TryRead(privateVoice, privateVoice.Length, out TownServiceFrame? privateCue)
            && privateCue!.Session == 300 && privateCue.Sequence == 1 && privateCue.Service == 3,
            "stock and mage cues keep coincident ordinals in distinct visitor envelopes");
        TownServiceVoice.RelayRequest!(3, TownVoiceReaction.EnchantressInspect);
        using (TownServiceMirror.UseStockLane()) TownServiceMirror.EndSession();
        List<byte[]> returned = Capture();
        Check(returned.Exists(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame)
            && !frame!.VisitorStock && frame.Module == TownServiceFrame.VoiceModule),
            "returning stock leaves the other NPC's queued private cue intact");
        TownServiceMirror.EndSession(); TownServiceMirror.ResetNetwork(); NetPlayerActors.Peer = 3;
        var driver = new NetAvatarDriver();
        Check(driver.QueueFixture(2, stockVoice) && driver.QueueFixture(2, privateVoice)
            && driver.QueuedVoiceFixture(2) == 2,
            "actual avatar retains coincident private and stock voice ordinals");
        driver.ApplyFixture();
        Check(TownServiceVoice.StockAccepted.Count == 0,
            "stock voice cannot precede its original lifted source manifest");
        byte[] census = Find(true, TownServiceFrame.ManifestModule);
        Check(TownServiceMirror.Receive(2, census, census.Length), "stock voice original census is received");
        Check(TownServiceVoice.StockAccepted.Count == 0,
            "a stock census alone does not authorize a pickup cue");
        byte[] front = Find(true, 8);
        Check(TownServiceMirror.Receive(2, front, front.Length), "stock voice original item is received before its mount");
        Check(TownServiceVoice.StockAccepted.Count == 0,
            "an original item without its typed lifted mount does not authorize a pickup cue");
        byte[] root = Find(true, 7);
        Check(TownServiceMirror.Receive(2, root, root.Length), "stock voice original mount is received");
        Check(TownServiceVoice.StockAccepted.Count == 1
            && TownServiceVoice.StockAccepted[0].Peer == 2 && TownServiceVoice.StockAccepted[0].Session == 400
            && TownServiceVoice.StockAccepted[0].Reaction == TownVoiceReaction.MerchantOffer,
            "matching original lifted membership releases the deferred stock cue using the real sender");
        Check(TownServiceMirror.InteractionOwner(1) == 0,
            "a stock pickup cue creates no merchant interaction claim");
        TownServiceFrame unrelated = TownServiceVoiceRelayCodec.Create(1, 401, 2,
            TownVoiceReaction.MerchantSoldOut, Time.unscaledTime, 0f, visitorStock: true);
        byte[] unrelatedBytes = TownServiceCodec.Write(unrelated);
        TownServiceMirror.Receive(2, unrelatedBytes, unrelatedBytes.Length);
        Check(TownServiceVoice.StockAccepted.Count == 1,
            "an unmatched stock session cannot impersonate a live sample");
        TownServiceMirror.RemovePeer(2);
        var pending = (IDictionary)typeof(TownServiceMirror).GetField("StockVoicePending", PrivateStatic)!.GetValue(null)!;
        Check(!pending.Contains(2), "disconnect removes deferred stock voice ownership");
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
    }

}
