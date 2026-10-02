using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI
{
    internal static partial class NativeTemplates
    { internal static Transform BadgeTemplate = null!; }
    internal sealed partial class TownServiceSync
    {
        internal static void BadgeStockRouting(TownServiceCatalog catalog)
        {
            Calls.Clear();
            Stock.PublishStockEntries(catalog);
        }
    }
}

public static partial class MirrorProgram
{
    private static IEnumerator OriginalMerchantBadge()
    {
        TownServiceMirror.Shutdown(); TownServiceSync.ResetNetwork(); Baselines.Clear();
        TownServiceSync.UseProductionPublish = true; NetPlayerActors.Peer = 2;
        Transform owner = Go("original stock owner frame").transform;
        Transform viewer = Go("original stock observer frame").transform;
        viewer.position = new Vector3(3f, 0f, 0f);
        Transform housing = Go("original cabinet", owner).transform;
        Transform crank = Go("original crank", owner).transform;
        Transform mount = Go("original physical card mount", owner).transform;
        Transform face = TownServiceCardFace.CreateCanvas().transform;
        face.SetParent(mount, false);
        ((RectTransform)face).sizeDelta = new Vector2(260f, 260f);
        face.localScale = Vector3.one * .01f;
        Transform card = Rect("original pooled native item", face, Vector2.zero, new Vector2(260f, 260f));
        card.gameObject.AddComponent<ItemCardUI>().CardID = 77;
        card.gameObject.AddComponent<Image>().color = new Color(.08f, .38f, .62f, 1f);
        var font = Go("original native row font").AddComponent<TextMeshProUGUI>();
        font.font = TMP_Settings.defaultFontAsset;
        GameObject band = TownServiceCardFace.AddStockBand(face, new Vector2(260f, 260f), font, "Original sold out");
        NativeTemplates.BadgeTemplate = TownServiceCardFace.CreateTemplate(font, "template default caption").transform;
        Objects.Add(NativeTemplates.BadgeTemplate.gameObject);
        var catalog = new TownServiceCatalog();
        var entry = new TownServiceCatalog.Entry
        { ItemId = 77, CardRoot = card, FaceRoot = face, PhysicalMount = mount };
        catalog.Entries.Add(entry); TownServiceCatalog.CardMounts.Add(mount, entry);
        var drawer = new TownServiceMerchantDrawer
        { Root = crank, HousingRoot = housing, Page = 0, FromPage = 0, ToPage = 0,
          PageCount = 2, TurnElapsed = TownRackState.TurnDuration };
        catalog.Drawers.Add(drawer);
        Canvas.ForceUpdateCanvases();
        TownServiceSync.TickPublic(owner, owner, catalog, 905, 0f);
        ushort mountId = TownServiceSync.PublicModuleId(mount), cardId = TownServiceSync.PublicModuleId(card);
        List<byte[]> packets = Capture();
        var frames = packets.Select(bytes => { TownServiceCodec.TryRead(bytes, bytes.Length, out var frame); return frame!; }).ToList();
        TownServiceFrame? faceFrame = frames.Find(frame => frame.TemplateAddress == "merchant.cardface|");
        Check(faceFrame != null && faceFrame.Nodes.Length == 3,
            "original shelf face and band are captured alongside the native item");
        ushort faceId = faceFrame!.Module;
        Check(faceFrame.ParentModule == mountId && frames.Find(frame => frame.Module == cardId)!.ParentModule == faceId,
            "original item, face canvas and stock band keep their captured physical ancestry");
        TownServiceFrame rack = frames.Find(frame => frame.Rack != null)!;
        Check(rack.Rack!.Members.Any(member => member.Id == faceId && member.Page == 0),
            "original stock band belongs to the same causal cassette page as its card");
        using (var source = new TownServiceBinding(face, NativeTemplates.IsBoundary))
        using (var template = new TownServiceBinding(NativeTemplates.BadgeTemplate))
            Check(source.Structure == template.Structure && source.Nodes.Length == template.Nodes.Length,
                "original local face and canonical native observer bank share exact presentation topology");

        NetPlayerActors.Peer = 3; Receive(2, packets); TownServiceMirror.TickRemote(_ => viewer);
        TownServiceBinding copiedFace = Remote(-2, faceId)!;
        Check(copiedFace != null && Remote(-2, cardId) != null,
            "late public observer receives the complete original stock canvas and card");
        Transform copiedBand = copiedFace.Root.Find("OriginalStockSoldOut");
        Check(copiedBand != null && copiedBand.gameObject.activeInHierarchy
            && copiedBand.Find("Caption").GetComponent<TMP_Text>().text == "Original sold out",
            "observer clones the original owner caption instead of inventing its stock state");
        if (copiedBand == null) throw new InvalidOperationException("Original stock band is missing after its presence check.");
        Check(((RectTransform)copiedBand).sizeDelta == ((RectTransform)band.transform).sizeDelta
            && copiedBand.GetComponent<Image>().color == band.GetComponent<Image>().color,
            "original stock band preserves dimensions and ink through capture and wire");
        Check(copiedBand.GetComponent<Image>().raycastTarget == false
            && copiedFace.Root.GetComponent<GraphicRaycaster>() == null,
            "original shelf canvas remains inert on observers");
        Check(Vector3.Distance(copiedFace.Root.position, viewer.TransformPoint(owner.InverseTransformPoint(face.position))) < .00002f,
            "translated observer retains the original face world pose before raster isolation");
        // Raster both text meshes at the same world coordinates after checking the
        // translated observer pose. Floating-point camera recentering at x=3 moves
        // a few anti-aliased glyph edge samples even when their geometry is identical.
        viewer.position = Vector3.zero;
        Canvas.ForceUpdateCanvases();
        yield return null;
        ComparePixels(face, copiedFace.Root, "original-sold-out-shelf");

        // A warm neighbouring page uses this same original annotation but must not
        // leak it before its actual cassette turn. The rack owns all member visibility.
        NetPlayerActors.Peer = 2; entry.Page = 1;
        TownServiceSync.TickPublic(owner, owner, catalog, 905, .1f);
        TownServiceMirror.RequestFullRefresh(); packets = Capture();
        NetPlayerActors.Peer = 3; Receive(2, packets); TownServiceMirror.TickRemote(_ => viewer);
        Check(Remote(-2, faceId)!.Root.parent.GetComponent<CanvasGroup>().alpha == 0f,
            "prewarmed original stock band cannot appear on an inactive page");
        NetPlayerActors.Peer = 2; entry.Page = 0;
        TownServiceSync.TickPublic(owner, owner, catalog, 905, .2f);
        TownServiceMirror.RequestFullRefresh(); packets = Capture();
        NetPlayerActors.Peer = 3; Receive(2, packets); TownServiceMirror.TickRemote(_ => viewer);
        Check(Remote(-2, faceId)!.Root.gameObject.activeInHierarchy
            && Remote(-2, faceId)!.Root.parent.GetComponent<CanvasGroup>().alpha > .99f,
            "original stock band reappears on its active page without a fabricated observer badge");

        // Another visitor's independent original sample masks the ENTIRE public
        // mount. Its annotation is neither published in the stock lane nor left
        // floating at the public slot after the physical original has departed.
        entry.Sample.IsMoving = true;
        TownServiceSync.UseProductionPublish = false;
        TownServiceSync.BadgeStockRouting(catalog);
        Check(TownServiceSync.Calls.All(call => call.Key != "merchant.cardface"),
            "held-stock publication never copies the shelf-only sold-out annotation");
        TownServiceSync.UseProductionPublish = true; entry.Sample.IsMoving = false;
        Transform held = Go("original visitor sample mount", owner).transform;
        Transform heldFace = Rect("original held item", held, Vector2.zero, new Vector2(260f, 260f));
        heldFace.gameObject.AddComponent<Image>().color = card.GetComponent<Image>().color;
        heldFace.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        TownServiceMirror.RegisterTemplate(1, 1, Go("canonical empty held mount").transform, address: "merchant.heldstock|");
        TownServiceMirror.RegisterTemplate(1, 1, heldFace, address: "item.77|");
        NetPlayerActors.Peer = 2;
        using (TownServiceMirror.UseStockLane())
        {
            TownServiceMirror.BeginSession(1, 906, owner, owner);
            TownServiceMirror.RegisterModule(21, 1, held, child => child.parent == held, "merchant.heldstock|");
            TownServiceMirror.RegisterModule(22, 1, heldFace, address: "item.77|");
        }
        TownServiceMirror.RequestFullRefresh(); packets = Capture();
        NetPlayerActors.Peer = 3; Receive(2, packets); TownServiceMirror.TickRemote(_ => viewer);
        Check(TownServiceMirror.StockItemHeldByOther(77)
            && !Remote(-2, mountId)!.Root.parent.gameObject.activeSelf
            && !Remote(-2, faceId)!.Root.Find("OriginalStockSoldOut").gameObject.activeInHierarchy,
            "independent visitor stock masks the whole original public face and shelf-only band");
        NetPlayerActors.Peer = 2;
        using (TownServiceMirror.UseStockLane()) TownServiceMirror.EndSession();
        packets = Capture(); NetPlayerActors.Peer = 3; Receive(2, packets); TownServiceMirror.TickRemote(_ => viewer);
        Check(!TownServiceMirror.StockItemHeldByOther(77)
            && Remote(-2, faceId)!.Root.Find("OriginalStockSoldOut").gameObject.activeInHierarchy,
            "retiring the visitor sample restores the original shelf band on the same public page");
        TownServiceSync.UseProductionPublish = false; TownServiceCatalog.CardMounts.Clear();
        TownServiceSync.ResetNetwork(); TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
        yield break;
    }
}
