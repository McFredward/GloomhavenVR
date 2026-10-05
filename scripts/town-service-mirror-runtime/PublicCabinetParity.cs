using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static IEnumerator PopulatedPublicCabinetHandover()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 2;
        Transform owner = Go("populated cabinet native author").transform;
        Transform viewer = Go("populated cabinet observer").transform;
        Transform rack = Go("Original cabinet housing", owner).transform;
        Transform category = Rect("Original public category key", rack, Vector2.zero, new Vector2(40, 30));
        category.gameObject.AddComponent<Image>().color = Color.cyan;
        Button key = category.gameObject.AddComponent<Button>();
        key.onClick.AddListener(TownServicePublicMerchant.Claim);
        Transform crank = Go("Original crank", rack).transform;
        var mesh = new Mesh { name = "npc611 original public native geometry" };
        mesh.vertices = new[] { new Vector3(-.05f, -.06f, 0), new Vector3(-.05f, .06f, 0),
            new Vector3(.05f, .06f, 0), new Vector3(.05f, -.06f, 0) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateBounds(); Assets.Add(mesh);
        var material = new Material(Shader.Find("Unlit/Color")) { name = "npc611 public holder material", color = Color.gray };
        Assets.Add(material);
        foreach (Transform part in new[] { rack, crank })
        { part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material; }
        Transform mount = Go("Original native card holder", rack).transform;
        mount.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        mount.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        Transform face = Rect("Original item artwork", mount, Vector2.zero, new Vector2(80, 120));
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "npc611 populated stock artwork" };
        texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.yellow }); texture.Apply(); Assets.Add(texture);
        Sprite artwork = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        artwork.name = "npc611 populated original item sprite"; Assets.Add(artwork);
        face.gameObject.AddComponent<Image>().sprite = artwork;
        Transform body = Go("Original native item backing", mount).transform;
        body.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        body.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        Transform price = Rect("Original native stock price", mount, Vector2.down * 65f, new Vector2(80, 20));
        price.gameObject.AddComponent<Image>().color = Color.green;
        Transform neighbour = Rect("Original cold neighbour stock", rack, Vector2.zero, new Vector2(80, 120));
        neighbour.gameObject.AddComponent<Image>().sprite = artwork;
        Transform extraKey = Rect("Original newly unlocked category key", rack, Vector2.zero, new Vector2(40, 30));
        extraKey.gameObject.AddComponent<Image>().color = Color.yellow; extraKey.gameObject.SetActive(false);
        Func<Transform, bool> rackParts = t => t == category || t == crank || t == mount || t == neighbour || t == extraKey;
        Func<Transform, bool> mountParts = t => t == face || t == body || t == price;
        TownServiceMirror.RegisterTemplate(1, 1, rack, rackParts, "merchant.rack|");
        TownServiceMirror.RegisterTemplate(1, 1, category, address: "merchant.category.weapon|");
        TownServiceMirror.RegisterTemplate(1, 1, crank, address: "merchant.crank|");
        TownServiceMirror.RegisterTemplate(1, 1, mount, mountParts, "merchant.cardholder|");
        TownServiceMirror.RegisterTemplate(1, 1, face, address: "item.611|");
        TownServiceMirror.RegisterTemplate(1, 1, body, address: "merchant.cardbody|");
        TownServiceMirror.RegisterTemplate(1, 1, price, address: "merchant.row|");
        TownServiceMirror.RegisterTemplate(1, 1, neighbour, address: "item.612|");
        TownServiceMirror.RegisterTemplate(1, 1, extraKey, address: "merchant.category.unlocked|");
        var catalog = new TownServiceCatalog { ObserverRoot = rack };
        catalog.Drawers.Add(new TownServiceMerchantDrawer { Root = rack, HousingRoot = rack });
        TownServicePublicMerchant.Catalog = catalog;
        TownServicePublicMerchant.FixtureResetVisibility();
        TownServiceMirror.CommitPublicVisibility = TownServicePublicMerchant.FixtureCommitVisibility;
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceMirror.BeginSession(1, 771, owner, rack);
            TownServiceMirror.RegisterModule(10, 1, rack, rackParts, "merchant.rack|");
            TownServiceMirror.RegisterModule(11, 1, category, address: "merchant.category.weapon|");
            TownServiceMirror.RegisterModule(12, 1, crank, address: "merchant.crank|");
            TownServiceMirror.RegisterModule(20, 1, mount, mountParts, "merchant.cardholder|");
            TownServiceMirror.RegisterModule(21, 1, face, address: "item.611|");
            TownServiceMirror.RegisterModule(22, 1, body, address: "merchant.cardbody|");
            TownServiceMirror.RegisterModule(23, 1, price, address: "merchant.row|");
            TownServiceMirror.RegisterModule(31, 1, neighbour, address: "item.612|");
            SetPopulatedRack(0, 0);
        }
        List<byte[]> initial = Capture().Where(bytes => ReadModule(bytes) != 31).ToList(); NetPlayerActors.Peer = 10;
        Receive(2, initial); TownServiceMirror.TickRemote(_ => viewer);
        Check(Remote(-2, 10) != null, "populated public original root builds: "
            + string.Join(" | ", GloomhavenVR.Core.VRLog.Messages.TakeLast(8)));
        Transform first = Remote(-2, 10)!.Root;
        Check(TownServiceMirror.HasReadyPublicPresentation && first.gameObject.activeInHierarchy,
            "current public stock commits a populated original cabinet with its category key and crank");
        Check(Remote(-2, 31) == null && TownServiceMirror.HasReadyPublicPresentation,
            "known cold off-page preload originals do not gate the complete current public page");
        Check(Remote(-2, 20)!.Root.gameObject.activeInHierarchy
            && Remote(-2, 21)!.Root.GetComponent<Image>().sprite == artwork
            && Remote(-2, 22)!.Root.GetComponent<MeshFilter>().sharedMesh == mesh
            && Remote(-2, 23)!.Root.GetComponent<Image>().color == Color.green,
            "committed public page contains its original holder, front, physical body and price");
        Check(TownServicePublicMerchant.FixtureObserving && rack.GetComponent<Renderer>().forceRenderingOff,
            "exact production visibility commit hides the preceding local native cabinet on the reveal frame");
        Check(Remote(-2, 11)!.Root.GetComponent<Button>() == null || !Remote(-2, 11)!.Root.GetComponent<Button>().interactable,
            "public observer originals never acquire the author's native category callback");

        NetPlayerActors.Peer = 3;
        key.onClick.Invoke(); // Inspection alone does not replace the prepared native bank.
        Check(TownServiceMirror.PublicAuthor == 2 && !TownServiceMirror.IsPublicAuthor
            && TownServicePublicMerchant.FixtureObserving && rack.GetComponent<Renderer>().forceRenderingOff,
            "joining peer inspection retains the complete original bank author and visible observer copy");
        // Explicit bank-owner replacement is a separate mirror lifetime boundary,
        // not the normal category/page input path exercised by Navigation.cs.
        TownServiceMirror.ClaimPublicCatalog();
        using (TownServiceMirror.UsePublicLane()) SetPopulatedRack(1, 256);
        List<byte[]> changed = Capture().Where(bytes => ReadModule(bytes) != 31).ToList(); NetPlayerActors.Peer = 10;
        List<byte[]> missing = changed.Where(bytes => ReadModule(bytes) != 23).ToList();
        Receive(3, missing); TownServiceMirror.TickRemote(_ => viewer);
        Check(TownServiceMirror.PublicAuthor == 3 && !TownServiceMirror.HasReadyPublicPresentation
            && first.gameObject.activeInHierarchy && !Remote(-3, 10)!.Root.gameObject.activeInHierarchy,
            "new remote author retains the previous complete cabinet while one real original price is missing");
        TownServicePublicMerchant.FixtureFollowPublicRack();
        Check(catalog.Drawers[0].Page == 256 && !catalog.Drawers[0].FixtureFollowingTurn
            && !TownServiceMirror.HasReadyPublicPresentation,
            "public category proxy follows the validated owner clock while original price artwork is still missing");
        Receive(3, changed.Where(bytes => ReadModule(bytes) == 23));
        TownServiceMirror.TickRemote(_ => viewer);
        Transform second = Remote(-3, 10)!.Root;
        Check(TownServiceMirror.HasReadyPublicPresentation && second.gameObject.activeInHierarchy
            && !first.gameObject.activeInHierarchy
            && Remote(-3, 21)!.Root.GetComponent<Image>().sprite == artwork,
            "the new author's complete original page replaces the old cabinet atomically without grey faces or overlap");
        Check(TownServicePublicMerchant.FixtureObserving && catalog.Drawers[0].Page == 256,
            "exact production visibility commit adopts the new layout and mechanism clock before reveal");

        // The next press belongs to the same author. Its new member epoch can
        // overtake the rack root without revealing an empty or stale page.
        NetPlayerActors.Peer = 3; key.onClick.Invoke();
        using (TownServiceMirror.UsePublicLane())
        {
            extraKey.gameObject.SetActive(true);
            TownServiceMirror.RegisterModule(13, 1, extraKey, address: "merchant.category.unlocked|");
            SetPopulatedRack(2, 512);
        }
        List<byte[]> next = Capture().Where(bytes => ReadModule(bytes) != 31).ToList(); NetPlayerActors.Peer = 10;
        byte[] firstMember = next.Single(bytes => ReadModule(bytes) == 21);
        Receive(3, new[] { firstMember }); TownServiceMirror.TickRemote(_ => viewer);
        Check(!TownServiceMirror.HasReadyPublicPresentation && second.gameObject.activeInHierarchy,
            "a reordered native member starts the same-author replacement without destroying the previous complete page");
        Receive(3, next.Where(bytes => ReadModule(bytes) != 13 && ReadModule(bytes) != 21));
        TownServiceMirror.TickRemote(_ => viewer);
        Check(!TownServiceMirror.HasReadyPublicPresentation && second.gameObject.activeInHierarchy
            && ReferenceEquals(second, Remote(-3, 10)!.Root),
            "missing new mechanical original retains the same author's usable native cabinet");
        Receive(3, next.Where(bytes => ReadModule(bytes) == 13)); TownServiceMirror.TickRemote(_ => viewer);
        Transform third = Remote(-3, 10)!.Root;
        Check(TownServiceMirror.HasReadyPublicPresentation && third.gameObject.activeInHierarchy
            && ReferenceEquals(second, third) && Remote(-3, 12)!.Root.gameObject.activeInHierarchy && Remote(-3, 13)!.Root.gameObject.activeInHierarchy,
            "the same author's next complete category reuses the populated original native group");
        NetPlayerActors.Peer = 2; key.onClick.Invoke();
        Check(TownServiceMirror.PublicAuthor == 3 && !TownServiceMirror.IsPublicAuthor && key.interactable
            && TownServicePublicMerchant.FixtureObserving,
            "later visitor inspection does not replace the prepared bank after validated owner replacements");
        NetPlayerActors.Peer = 10;
        TownServiceMirror.CommitPublicVisibility = null;
        catalog.Drawers[0].FixtureDisposeFollower(); TownServicePublicMerchant.Catalog = null;
        TownServicePublicMerchant.FixtureResetVisibility(); TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
        yield return null;
    }

    private static IEnumerator PreparedCatalogBankParity()
    {
        TownServiceSync.ResetNetwork(); TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 1;
        Transform owner = Go("prepared original bank author").transform, viewer = Go("prepared bank observer").transform;
        Transform housing = Go("original prepared cassette", owner).transform; Go("Cassette", housing);
        var rack = new TownServiceMerchantDrawer { HousingRoot = housing, Root = Go("original prepared crank", owner).transform };
        rack.FixturePrepareNavigation();
        var catalog = new TownServiceCatalog { ObserverRoot = housing, OriginalBankPrepared = true, OriginalBankRevision = 1 };
        catalog.Drawers.Add(rack); var hand = new GloomhavenVR.Hands.VRHand();
        var bodyMesh = new Mesh { name = "original prepared item stock body" };
        bodyMesh.vertices = new[] { Vector3.zero, Vector3.right * .02f, Vector3.up * .02f };
        bodyMesh.triangles = new[] { 0, 1, 2 }; Assets.Add(bodyMesh);
        var bodyMaterial = new Material(Shader.Find("Unlit/Color")) { name = "original prepared item body material", color = Color.grey }; Assets.Add(bodyMaterial);
        foreach (int category in new[] { 0, 3, 5 })
        {
            Transform button = Rect("original bank category " + category, owner, Vector2.zero, new Vector2(40, 40));
            button.gameObject.AddComponent<Image>().color = Color.green;
            var key = new TownServiceCatalogCategory { Key = "merchant.category.bank." + category, Root = button };
            key.FixturePrepareNavigation(rack, category); catalog.Categories.Add(key);
            Transform mount = Go("original bank mount " + category, housing.Find("Cassette")).transform;
            Transform face = Rect("original bank face " + category, mount, Vector2.zero, new Vector2(80, 120));
            Transform card = Rect("original bank item " + category, face, Vector2.zero, new Vector2(80, 120));
            card.gameObject.AddComponent<ItemCardUI>().CardID = 970 + category;
            card.gameObject.AddComponent<Image>().color = category == 5 ? Color.yellow : Color.blue;
            Transform price = Rect("original bank price " + category, mount, Vector2.down * 65, new Vector2(80, 20));
            price.gameObject.AddComponent<Image>().color = category == 5 ? Color.magenta : Color.cyan;
            Transform body = Go("original bank body " + category, mount).transform;
            body.gameObject.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
            body.gameObject.AddComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
            Transform nativePrice = Rect("original source price " + category, owner, Vector2.zero, new Vector2(80, 20));
            nativePrice.gameObject.AddComponent<Image>().color = price.GetComponent<Image>().color;
            var entry = new TownServiceCatalog.Entry { ItemId = 970 + category, Page = category * 256,
                NavigationRack = rack, CardRoot = card, FaceRoot = face, BodyRoot = body, PhysicalMount = mount, RowContent = price, RowSource = nativePrice };
            catalog.Entries.Add(entry); catalog.PreparedOriginalEntries.Add(entry); TownServiceCatalog.CardMounts.Add(mount, entry);
            foreach (Transform part in new[] { mount, face, card, body, price }) NativeTemplates.BoundaryRoots.Add(part);
        }
        catalog.StockLayout = catalog.Entries.Select(e => new TownCatalogSlot(e.ItemId, (ushort)(e.Page / 256 * TownCatalogLayout.SlotsPerCategory))).ToArray();
        TownServicePublicMerchant.Catalog = catalog; TownServicePublicMerchant.FixtureResetVisibility();
        TownServiceMirror.CommitPublicVisibility = TownServicePublicMerchant.FixtureCommitVisibility;
        TownServiceSync.UseProductionPublish = true; TownServiceSync.TickPublic(owner, owner, catalog, 997, 1f);
        var transport = new PreparedCabinetWire625();
        List<byte[]> initial = transport.Capture();
        ushort farPrice = TownServiceSync.PublicModuleId(catalog.Entries[2].RowContent!);
        bool FarOriginalReady() => initial.Any(p => { var frame = Decode(p); return frame.Module == farPrice && frame.BaseSequence == 0
            && frame.PublicCatalog && frame.RackMember != null && !frame.RackMember.Detached && frame.Nodes.Length > 0; });
        // Dormant loss-repair originals are deliberately bounded loading slices.
        // Drive the actual publisher/capture over native frames instead of requiring
        // the removed all-stock serialization spike in the first capture call.
        float preparationDeadline = Time.unscaledTime + 2f;
        while (!FarOriginalReady() && Time.unscaledTime < preparationDeadline)
        {
            yield return null;
            TownServiceSync.TickPublic(owner, owner, catalog, 997, 1f);
            initial.AddRange(transport.Capture());
        }
        Check(FarOriginalReady(), "prepared dormant original sends a genuine complete loading prewarm snapshot"
            + ": farPrice=" + farPrice + " prepared=" + TownServiceSync.HasPreparedPublicCatalog
            + " packets=" + string.Join(",", initial.Select(p => { var f=Decode(p); return f.Module + ":" + f.BaseSequence + ":" + (f.RackMember != null) + ":" + f.Nodes.Length; }))
            + " logs=" + string.Join(" | ", GloomhavenVR.Core.VRLog.Messages.TakeLast(12)));
        Check(TownServiceSync.HasPreparedPublicCatalog, "loading preparation retains a complete source bank before first far category input");
        Check(transport.CompletedRoots > 0 && transport.LastCompletedSequence >= transport.LastSourceBank!.Sequence,
            "queued canonical clock completion releases the actual publisher heartbeat suppression");
        ulong firstBankSequence = transport.LastSourceBank!.Sequence;
        // Expire only this real module's ordinary refresh deadline; no asset, bank,
        // baseline or completion state is injected. Avoid a five-second harness wait
        // while exercising the actual LastSent gate and publisher heartbeat path.
        object lane = typeof(TownServiceMirror).GetField("PublicLane", PrivateStatic)!.GetValue(null)!;
        var moduleFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var modules = (IDictionary)lane.GetType().GetField("Modules", moduleFlags)!.GetValue(lane)!;
        object sourceRack = modules[transport.LastSourceBank.Module]!;
        sourceRack.GetType().GetField("NextRefresh", moduleFlags)!.SetValue(sourceRack, Time.unscaledTime - .01f);
        TownServiceSync.TickPublic(owner, owner, catalog, 997, 1f);
        initial.AddRange(transport.Capture());
        Check(transport.LastSourceBank!.Sequence > firstBankSequence && transport.CompletedRoots >= 2,
            "actual completed canonical clock allows a later unchanged-owner loss-repair heartbeat");
        catalog.OriginalBankPrepared = false; TownServiceSync.TickPublic(owner, owner, catalog, 997, 1f);
        List<byte[]> partial = transport.Capture();
        Check(partial.Any(p => Decode(p).CatalogBank?.Prepared == false),
            "partial dormant preparation never claims a complete original bank");
        catalog.OriginalBankPrepared = true; TownServiceSync.TickPublic(owner, owner, catalog, 997, 1f);
        initial.AddRange(transport.Capture());
        var identities = new PublicNavigationIdentities(); identities.Switch(2);
        byte[] coldClock = initial.First(p => Decode(p).CatalogBank?.Prepared == true);
        Check(!TownServiceMirror.Receive(1, coldClock, coldClock.Length),
            "actual queued prepared clock cannot expose an original bank before exact cold dependency delivery");
        Receive(1, initial.Where(p => Decode(p).CatalogBank == null));
        Receive(1, initial.Where(p => Decode(p).CatalogBank != null));
        TownServiceMirror.TickRemote(_ => viewer);
        Check(TownServiceMirror.HasReadyPublicPresentation, "prepared initial original bank mounts dependencies in parent order on one receiver tick");
        identities.Switch(1); catalog.Categories[2].OnPoke(hand);
        int previousFrame = -1; bool checkedBoundary = false;
        while (rack.FixtureFollowClock < TownRackState.TurnDuration + .02f)
        {
            if (previousFrame == Time.frameCount) { yield return null; continue; } previousFrame = Time.frameCount;
            identities.Switch(1); TownServicePublicMerchant.FixtureCommitVisibility(); rack.FixtureTick(); TownServiceSync.TickPublic(owner, owner, catalog, 997, 1f);
            List<byte[]> packets = transport.Capture();
            identities.Switch(2);
            // Every ordinary stock member refresh is deliberately withheld. The actual
            // queue sends only canonical header/patch clocks, controls and census here.
            Receive(1, packets.Where(p => { var f = Decode(p); return f.RackMember == null; }));
            TownServiceMirror.TickRemote(_ => viewer);
            if (!checkedBoundary && rack.FixtureFollowClock > .47f)
            {
                Check(TownServiceMirror.HasReadyPublicPresentation && TownServiceMirror.PublicRack!.To == 1280
                    && Remote(-1, farPrice)!.Root.GetComponent<Image>().color == Color.magenta
                    && Remote(-1, farPrice)!.Root.GetComponentInParent<CanvasGroup>().alpha > .99f,
                    "prepared cold far category stays fully populated at owner boundary with every separate stock refresh withheld: "
                    + NavigationReadiness(TownServiceMirror.PublicRack!) + " priceExists=" + (Remote(-1, farPrice) != null)
                    + " logs=" + string.Join(" | ", GloomhavenVR.Core.VRLog.Messages.TakeLast(6)));
                Transform remoteRack = Remote(-1, TownServiceSync.PublicModuleId(housing))!.Root;
                Check(Vector3.Distance(housing.Find("Cassette").localPosition, remoteRack.Find("Cassette").localPosition) < .004f,
                    "atomic original bank follows exact owner intermediate cassette pose without freezing for member freshness");
                checkedBoundary = true;
            }
            if (rack.FixtureFollowClock >= TownRackState.TurnDuration) break;
            yield return null;
        }
        Check(checkedBoundary, "cold bank proof samples actual owner replacement boundary");
        identities.Switch(1); TownServicePublicMerchant.FixtureCommitVisibility(); catalog.Entries[2].RowContent!.GetComponent<Image>().color = Color.red;
        TownServiceSync.TickPublic(owner, owner, catalog, 997, 1f); List<byte[]> changed = transport.Capture(); identities.Switch(2);
        Receive(1, changed.Where(p => Decode(p).RackMember == null)); TownServiceMirror.TickRemote(_ => viewer);
        var owners = (IDictionary)typeof(TownServiceMirror).GetField("Remote", PrivateStatic)!.GetValue(null)!;
        object observedPrice = ((IDictionary)owners[-1]!)[farPrice]!;
        var appliedPrice = (TownServiceFrame)observedPrice.GetType().GetField("LastFrame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(observedPrice)!;
        float[] graphic = appliedPrice.Nodes[0].Values[TownServiceProperty.Graphic].Numbers;
        Check(graphic[1] == 1f && graphic[2] == 0f && graphic[3] == 0f && TownServiceMirror.HasReadyPublicPresentation,
            "genuinely changed original price is installed atomically with same-turn canonical bank");
        // This runner advances nested iterators explicitly; its ordinary yield does not
        // execute Unity's WaitForSeconds objects. Sample the real existing 100ms widget
        // interpolation without assuming an instantaneous Graphic.color target.
        float settle = Time.unscaledTime + .12f;
        while (Time.unscaledTime < settle) { yield return null; TownServiceMirror.TickRemote(_ => viewer); }
        Check(Remote(-1, farPrice)!.Root.GetComponent<Image>().color == Color.red && TownServiceMirror.HasReadyPublicPresentation,
            "genuinely changed original price is installed atomically with same-turn canonical bank: source="
            + catalog.Entries[2].RowContent!.GetComponent<Image>().color + " remote=" + Remote(-1, farPrice)!.Root.GetComponent<Image>().color
            + " packets=" + string.Join(",", changed.Select(p => { var f=Decode(p); var price=f.CatalogBank?.Updates.FirstOrDefault(u=>u.Module==farPrice); return f.Module + ":" + f.Sequence + ":bank=" + (f.CatalogBank?.Updates.Length.ToString()??"-")+":price="+(price==null?"-":string.Join("/",price.Nodes[0].Values[TownServiceProperty.Graphic].Numbers)); }))
            + " ready=" + NavigationReadiness(TownServiceMirror.PublicRack!) + " logs=" + string.Join(" | ", GloomhavenVR.Core.VRLog.Messages.TakeLast(8)));
        TownServiceFrame bankRoot = transport.LastSourceBank!;
        Check(changed.Select(Decode).Single(f => f.CatalogBank != null).CatalogBank!.Updates.Length == 0
            && bankRoot.CatalogBank!.Updates.Length == bankRoot.CatalogBank.Members.Length,
            "actual prepared publisher retains complete source metadata separately from sparse transmitted clock");
        Check(bankRoot.BaseSequence == 0, "atomic bank contains its complete original rack root without an older baseline dependency");
        CatalogBankAdmissionProbes(bankRoot);
        var channel = new PublicNavigationChannel(identities);
        TownServicePublicMerchant.Session = 997; channel.Install(2);
        catalog.Categories[1].OnPoke(hand); channel.DeliverRequests(2);
        channel.Install(1);
        TownServiceSync.TickPublic(owner, owner, catalog, 997, 1f); List<byte[]> takeover = transport.Capture(); identities.Switch(3);
        Check(takeover.Select(Decode).Single(f => f.CatalogBank != null).Rack!.Elapsed < .05f,
            "delayed bank probe actually withholds the first owner turn frame rather than an already completed turn");
        Receive(1, takeover.Where(p => Decode(p).RackMember == null)); TownServiceMirror.TickRemote(_ => viewer);
        Check(TownServiceMirror.HasReadyPublicPresentation && TownServiceMirror.PublicRack!.From == 1280 && TownServiceMirror.PublicRack.To == 768,
            "unassigned peer input retains the same author and both original pages without ordinary member packets");
        // Keep the exact newly captured atomic turn in flight. The independent native
        // owner manifest advances while its header clock is delayed after bounded repairs.
        channel.Install(1); float finishedAt = Time.unscaledTime + TownRackState.TurnDuration + .12f;
        while (Time.unscaledTime < finishedAt) { rack.FixtureTick(); yield return null; }
        TownServicePublicMerchant.FixtureCommitVisibility(); rack.FixtureTick();
        TownServiceSync.TickPublic(owner, owner, catalog, 997, 1f); TownServiceMirror.RequestFullRefresh();
        var lateJoinTransport = new PreparedCabinetWire625();
        List<byte[]> latest = lateJoinTransport.Capture(); identities.Switch(4);
        TownServiceMirror.RemovePeer(1); // A genuinely new observer has no earlier rendered turn to coast forward.
        // A new observer receives the real unpaged category/crank originals as
        // well as the latest census and independent exact stock repairs. Stable author controls are not resent by
        // every ordinary page request; only the old turn clock remains delayed.
        // Its outgoing category is absent from the new idle bank. Deliver the genuine
        // queued dormant prewarm originals as loss repair, including the exact old
        // price basis needed by the real changed-property patch in that old clock.
        Receive(1, initial.Where(p => { var f = Decode(p); return f.RackMember != null && f.BaseSequence == 0; }));
        Receive(1, latest.Where(p => { var f=Decode(p); return f.CatalogBank == null; }));
        foreach (byte[] delayed in takeover.Where(p => { var f = Decode(p); return f.RackMember == null && f.Module != TownServiceFrame.ManifestModule; }))
            Check(TownServiceMirror.Receive(1, delayed, delayed.Length),
                "actual late-join originals admit the deliberately delayed queued clock: " + CatalogKeys625(Decode(delayed)));

        TownServiceMirror.TickRemote(_ => viewer);
        var clocks = (IDictionary)typeof(TownServiceMirror).GetField("RemoteRacks", PrivateStatic)!.GetValue(null)!;
        ushort rackId = TownServiceSync.PublicModuleId(housing); object clock = ((IDictionary)clocks[-1]!)[rackId]!;
        var clockFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        Check(TownServiceMirror.HasReadyPublicPresentation && (ushort)clock.GetType().GetField("DisplayPage", clockFlags)!.GetValue(clock)! == 768
            && !(bool)clock.GetType().GetField("Turning", clockFlags)!.GetValue(clock)!,
            "delayed atomic bank seeks newer same-owner manifest age without replaying an obsolete rack turn"
            + ": ready=" + TownServiceMirror.HasReadyPublicPresentation
            + " page=" + clock.GetType().GetField("DisplayPage", clockFlags)!.GetValue(clock)
            + " turning=" + clock.GetType().GetField("Turning", clockFlags)!.GetValue(clock)
            + " elapsed=" + clock.GetType().GetField("Elapsed", clockFlags)!.GetValue(clock)
            + " sent=" + takeover.Select(Decode).Single(f => f.CatalogBank != null).SessionAge
            + " latest=" + string.Join(",", latest.Select(Decode).Where(f => f.Module == TownServiceFrame.ManifestModule).Select(f=>f.SessionAge))
            + " ownerPage=" + rack.Page + " ownerClock=" + rack.FixtureFollowClock

            + " logs=" + string.Join(" | ", GloomhavenVR.Core.VRLog.Messages.TakeLast(8)));
        channel.Dispose(); identities.Switch(1); TownServiceMirror.CommitPublicVisibility = null; TownServiceSync.UseProductionPublish = false;
        TownServiceSync.ResetNetwork(); TownServicePublicMerchant.Catalog = null; TownServicePublicMerchant.FixtureResetVisibility();
        rack.FixtureDisposeFollower(); NativeTemplates.BoundaryRoots.Clear(); TownServiceCatalog.CardMounts.Clear(); TownServiceMirror.Shutdown(); Baselines.Clear();
        yield return null;
    }
    // This fixture exercises the same immutable metadata contract used by the actual
    // sender. A prepared source keeps originals for dependency repair; its emitted
    // byte packet contains only existing TLV103 canonical headers. Never turn those
    // bytes back into an invented full bank to make a receiver test pass.
    private sealed class PreparedCabinetWire625
    {
        private readonly TownServiceLaneSendQueue _queue = new TownServiceLaneSendQueue(65536);
        private readonly TownServiceFragments _fragments = new TownServiceFragments();
        private double _now;
        internal TownServiceFrame? LastSourceBank;
        internal int Datagrams, CompletePackets, CompletedRoots;
        internal ulong LastCompletedSequence;
        internal List<byte[]> Capture()
        {
            Action<byte[], int, object?> send = (bytes, length, metadata) =>
            {
                Check(length == bytes.Length && metadata is TownServiceFrame,
                    "actual prepared capture carries its full native metadata to the real queue");
                var source = (TownServiceFrame)metadata!;
                TownServiceFrame emitted = Decode(bytes);
                if (source.CatalogBank != null)
                {
                    LastSourceBank = source;
                    if (source.CatalogBank.Prepared)
                        Check(emitted.CatalogBank!.Updates.Length == 0
                            && emitted.CatalogBank.Headers.Length == source.CatalogBank.Members.Length,
                            "actual prepared source emits only canonical headers while retaining exact original dependencies");
                }
                _queue.Enqueue(bytes, length, source);
            };
            typeof(TownServiceMirror).GetMethod("CaptureCore", PrivateStatic)!
                .Invoke(null, new object[] { send, false });
            var packets = new List<byte[]>(); int idle = 0;
            Action<TownServiceFrame>? completed = TownServiceDelivery.Completed;
            TownServiceDelivery.Completed = frame =>
            {
                if (frame.CatalogBank != null)
                { CompletedRoots++; LastCompletedSequence = Math.Max(LastCompletedSequence, frame.Sequence); }
                completed?.Invoke(frame);
            };
            try
            {
                for (int turn = 0; turn < 4096 && idle < 4; turn++)
                {
                    _now += .050001;
                    byte[]? page = _queue.Next(_now);
                    if (page == null) { idle++; continue; }
                    idle = 0; Datagrams++;
                    Check(page.Length <= ExtrasFragments.MaxDatagramBytes,
                        "actual prepared cabinet delivery retains the existing datagram cap");
                    byte[]? complete = _fragments.Accept(1, page, page.Length, _now);
                    if (complete == null) continue;
                    byte[][] members = TownServiceCodec.TryReadBundle(complete, complete.Length, out byte[][]? bundle)
                        ? bundle! : new[] { complete };
                    foreach (byte[] member in members)
                    {
                        Decode(member); packets.Add(member); CompletePackets++;
                    }
                }
            }
            finally { TownServiceDelivery.Completed = completed; }
            Check(idle == 4, "bounded actual cabinet queue and fragment assemblies finish without an invented full-bank fallback");
            return packets;
        }
    }

    private static string CatalogKeys625(TownServiceFrame frame)
    {
        if (frame.CatalogBank == null) return "module=" + frame.Module + " ordinary";
        var banks = (IDictionary)typeof(TownServiceMirror).GetField("CatalogOriginalBanks", PrivateStatic)!.GetValue(null)!;
        if (!banks.Contains(-1)) return "no exact source cache";
        object bank = banks[-1]!;
        var entries = (IDictionary)bank.GetType().GetField("Originals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(bank)!;
        return string.Join(",", frame.CatalogBank.Members.Select(member =>
        {
            if (!entries.Contains(member.Id)) return member.Id + " missing";
            var keys = ((IList)entries[member.Id]!).Cast<object>().Select(original =>
                (ulong)original.GetType().GetField("Key", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(original)!);
            return member.Id + ":target=" + member.ContentKey + ":cache=" + string.Join("/", keys);
        }));
    }

    private static TownServiceFrame Decode(byte[] bytes)
    { if (!TownServiceCodec.TryRead(bytes, bytes.Length, out var frame)) throw new InvalidOperationException("bank packet decode failed"); return frame!; }

    private static void CatalogBankAdmissionProbes(TownServiceFrame source)
    {
        var pending = (IDictionary)typeof(TownServiceMirror).GetField("Pending", PrivateStatic)!.GetValue(null)!;
        var baselines = (IDictionary)typeof(TownServiceMirror).GetField("ReceivedBaselines", PrivateStatic)!.GetValue(null)!;
        object beforePending = ((IDictionary)pending[-1]!)[source.Module]!, beforeBaseline = ((IDictionary)baselines[-1]!)[source.Module]!;
        bool Unchanged() => ReferenceEquals(beforePending, ((IDictionary)pending[-1]!)[source.Module])
            && ReferenceEquals(beforeBaseline, ((IDictionary)baselines[-1]!)[source.Module]);
        TownServiceFrame bad = TownServiceDelta.Copy(source); bad.Sequence += 100;
        TownServiceFrame child = bad.CatalogBank!.Updates.First(u => u.ParentModule != TownServiceFrame.ManifestModule);
        child.ParentBinding = uint.MaxValue;
        bad.CatalogBank.Members = bad.CatalogBank.Members.Select(m => m.Id == child.Module
            ? new TownCatalogBankMember(m.Id, TownCatalogBank.ContentKey(child)) : m).ToArray();
        byte[] invalid = TownServiceCodec.Write(bad);
        Check(!TownServiceMirror.Receive(1, invalid, invalid.Length) && Unchanged(),
            "missing original cabinet parent binding is rejected before any atomic pending or baseline mutation");
        bad = TownServiceDelta.Copy(source); bad.Sequence += 101; bad.Structure ^= 1;
        invalid = TownServiceCodec.Write(bad);
        Check(!TownServiceMirror.Receive(1, invalid, invalid.Length) && Unchanged(),
            "missing original cabinet root topology is rejected before any atomic pending or baseline mutation");
        const int departed = 123;
        typeof(TownServiceMirror).GetMethod("IncomingCatalogContentKey", PrivateStatic)!.Invoke(null, new object[] { -departed, child });
        var keys = (IDictionary)typeof(TownServiceMirror).GetField("IncomingCatalogKeys", PrivateStatic)!.GetValue(null)!;
        long key = ((long)-departed << 16) | child.Module;
        Check(keys.Contains(key), "peer recycle probe first retains a real immutable original content key");
        TownServiceMirror.RemovePeer(departed);
        Check(!keys.Contains(key), "departed public peer clears original content keys before peer identity can be recycled");
    }

    private static ushort ReadModule(byte[] bytes)
    { Check(TownServiceCodec.TryRead(bytes, bytes.Length, out var frame), "populated cabinet original packet decodes"); return frame!.Module; }

    private static void SetPopulatedRack(uint turn, ushort page)
    {
        TownServiceMirror.SetRack(10, new TownRackState
        {
            Cassette = true, Turn = turn, Page = page, From = page, To = page, Elapsed = TownRackState.TurnDuration,
            Layout = new[] { new TownCatalogSlot(611, (ushort)(page / 256 * TownCatalogLayout.SlotsPerCategory)),
                new TownCatalogSlot(612, (ushort)(3 * TownCatalogLayout.SlotsPerCategory)) },
            Members = new[] { new TownRackMember(20, page, false), new TownRackMember(21, page, false),
                new TownRackMember(22, page, false), new TownRackMember(23, page, false),
                new TownRackMember(31, 768, false) }
        });
        foreach (ushort member in new ushort[] { 20, 21, 22, 23 })
            TownServiceMirror.SetRackMember(member, new TownRackStamp { Rack = 10, Page = page, Turn = turn });
        TownServiceMirror.SetRackMember(31, new TownRackStamp { Rack = 10, Page = 768, Turn = turn });
        TownServiceMirror.RequestFullRefresh();
    }
}
