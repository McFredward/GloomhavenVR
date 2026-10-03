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
        key.onClick.Invoke(); // Actual public category callback invokes production Claim.
        Check(TownServiceMirror.IsPublicAuthor && !TownServicePublicMerchant.FixtureObserving
            && !rack.GetComponent<Renderer>().forceRenderingOff,
            "joining peer's physical category press immediately claims and reveals its own complete native stock");
        using (TownServiceMirror.UsePublicLane()) SetPopulatedRack(1, 256);
        List<byte[]> changed = Capture().Where(bytes => ReadModule(bytes) != 31).ToList(); NetPlayerActors.Peer = 10;
        List<byte[]> missing = changed.Where(bytes => ReadModule(bytes) != 23).ToList();
        Receive(3, missing); TownServiceMirror.TickRemote(_ => viewer);
        Check(TownServiceMirror.PublicAuthor == 3 && !TownServiceMirror.HasReadyPublicPresentation
            && first.gameObject.activeInHierarchy && !Remote(-3, 10)!.Root.gameObject.activeInHierarchy,
            "new remote author retains the previous complete cabinet while one real original price is missing");
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
            && !Remote(-3, 10)!.Root.gameObject.activeInHierarchy,
            "missing current mechanical originals delay atomic reveal even when every card original is ready");
        Receive(3, next.Where(bytes => ReadModule(bytes) == 13)); TownServiceMirror.TickRemote(_ => viewer);
        Transform third = Remote(-3, 10)!.Root;
        Check(TownServiceMirror.HasReadyPublicPresentation && third.gameObject.activeInHierarchy
            && !second.gameObject.activeInHierarchy && Remote(-3, 12)!.Root.gameObject.activeInHierarchy && Remote(-3, 13)!.Root.gameObject.activeInHierarchy,
            "the same author's next complete category remains populated and retires the old native group once");
        NetPlayerActors.Peer = 2; key.onClick.Invoke();
        Check(TownServiceMirror.IsPublicAuthor && key.interactable && !TownServicePublicMerchant.FixtureObserving,
            "a later different visitor can press the native category again after both validated replacements");
        NetPlayerActors.Peer = 10;
        TownServiceMirror.CommitPublicVisibility = null;
        catalog.Drawers[0].FixtureDisposeFollower(); TownServicePublicMerchant.Catalog = null;
        TownServicePublicMerchant.FixtureResetVisibility(); TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
        yield return null;
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
