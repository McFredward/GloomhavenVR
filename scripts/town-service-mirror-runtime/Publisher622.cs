#if PUBLISHER622
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static bool Publisher622Priority(ushort id)
    {
        var modules = (IDictionary)typeof(TownServiceMirror).GetProperty("Local", PrivateStatic)!.GetValue(null)!;
        object module = modules[id]!;
        return (bool)module.GetType().GetField("HighPriority", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
    }

    private static void Publisher622Reset(byte service)
    {
        TownServiceSync.Reset(); TownServiceMirror.Shutdown(); Baselines.Clear();
        NativeTemplates.Originals.Clear(); NativeTemplates.BoundaryRoots.Clear();
        TownServicePresentation.Active = true; TownServicePresentation.Service = service;
        TownServicePresentation.Session++; TownServicePresentation.SessionAge = 0;
        TownServicePresentation.RelocationRevision = 0; TownServicePresentation.RelocationVisibility = 1;
        TownServicePresentation.Window = null; TownServicePresentation.Catalog = null;
        TownServicePresentation.Ritual = null; TownServicePresentation.CounterFurniture = null;
        TownServicePresentation.LocalSurfaces.Clear(); TownServicePresentation.Samples.Clear();
        TownServicePresentation.WorkspaceProps = null; TownServicePresentation.Tray = null;
        TownServiceEnhancementHandoff.Returning.Clear(); TownServiceMerchantHandoff.Active = false;
        TownServicePalmConfirmation.Active.Clear();
        TownServiceMerchantHandoff.OwnedChips.Clear(); NativeTemplates.Tooltip = null;
        TownServiceSync.Calls.Clear(); TownServiceSync.UseProductionPublish = true;
        // The local fixture sender must not claim the replayed remote visitor's
        // lease; both endpoints share this process, unlike the real players.
        NetPlayerActors.Peer = 10;
    }

    private static IEnumerator NativeEnhancementPublisher622()
    {
        Publisher622Reset(3);
        Transform author = Go("native enhancement publisher author").transform;
        Transform observer = Go("native enhancement publisher observer").transform;
        observer.SetPositionAndRotation(new Vector3(4, 1, 2), Quaternion.Euler(0, -31, 0));
        var ritual = new TownServiceRitual(); TownServicePresentation.Ritual = ritual;
        Transform inventory = Rect("Original upgrade inventory", author, Vector2.zero, new Vector2(350, 400));
        inventory.localScale = Vector3.one * .001f;
        inventory.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var pool = inventory.gameObject.AddComponent<UINewEnhancementShopInventory>();
        Transform viewport = Rect("Native viewport", inventory, Vector2.zero, new Vector2(330, 120));
        viewport.gameObject.AddComponent<RectMask2D>().padding = new Vector4(3, 5, 7, 11);
        Transform content = Rect("Native scroll content", viewport, Vector2.zero, new Vector2(330, 900));
        var rows = new List<Transform>();
        for (int i = 0; i < 4; i++)
        {
            // The coarse row rect is outside the mask for rows 1..3; their real
            // graphics reach into it. The original mask owns visible pixels.
            Transform row = Rect("Original upgrade row", content, new Vector2(0, 200 * i), new Vector2(320, 40));
            pool.slotsPool.Add(row.gameObject.AddComponent<UINewEnhancementShopSlot>());
            row.gameObject.AddComponent<GameplayFixture>();
            TextMeshProUGUI label = Rect("Original option", row, new Vector2(0, -200 * i + i * 20 - 30), new Vector2(250, 32))
                .gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset; label.text = "Original option " + i;
            label.fontSize = 19 + i; label.color = new Color(.15f * i, .7f, .5f, 1);
            Image("Native highlight", row, new Vector2(0, -200 * i + i * 20 - 30), new Vector2(270, 36), Color.cyan);
            rows.Add(row);
        }
        ritual.Surfaces.Add(new TownServiceSurface { Id = 10, Panel = new PanelFixture { Target = inventory } });
        Transform holder = Rect("Original card holder", author, Vector2.zero, new Vector2(350, 500));
        ritual.Surfaces.Add(new TownServiceSurface { Id = 11, Panel = new PanelFixture { Target = holder } });
        foreach (ushort id in new ushort[] { 13, 14, 15, 16 })
            ritual.Surfaces.Add(new TownServiceSurface { Id = id, Panel = new PanelFixture { Target = Go("Native decision " + id, author).transform } });
        Transform tooltip = Go("Original option explanation", author).transform;
        NativeTemplates.Originals["enchant.tooltip"] = tooltip;
        Transform confirmation = Rect("Original enhancement decision", author, Vector2.zero, new Vector2(330, 90));
        confirmation.localScale = Vector3.one * .001f;
        confirmation.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        CanvasGroup decisionGate = confirmation.gameObject.AddComponent<CanvasGroup>();
        confirmation.gameObject.AddComponent<Button>(); confirmation.gameObject.AddComponent<GameplayFixture>();
        TextMeshProUGUI decisionText = Rect("Original decision caption", confirmation, Vector2.zero, new Vector2(310, 60))
            .gameObject.AddComponent<TextMeshProUGUI>();
        decisionText.font = TMP_Settings.defaultFontAsset; decisionText.text = "Confirm original enhancement"; decisionText.fontSize = 22;
        var palm = new TownServicePalmConfirmation.Entry { Service = 3 };
        palm.Surfaces.Add(new TownServiceSurface { Id = 60, Panel = new PanelFixture { Target = confirmation } });
        TownServicePalmConfirmation.Active.Add(palm);

        Transform card = Go("Actual offered physical card", author).transform;
        card.SetPositionAndRotation(new Vector3(.4f, .7f, -.2f), Quaternion.Euler(17, 53, 9));
        Transform visual = Go("Visual", card).transform;
        Transform body = Image("Backing", visual, Vector2.zero, new Vector2(.15f, .23f), Color.black).transform;
        Transform face = Image("Actual offered front", visual, Vector2.zero, new Vector2(.14f, .22f), Color.yellow).transform;
        var handoff = new TownServiceEnhancementHandoff { OfferedCardId = 62201, Card = card, Face = face,
            NativeSource = null, Zone = Go("Visitor local pre-drop guide", author).transform };
        ritual.Handoff = handoff;
        // Resident readiness is authored by the original handoff/presentation
        // lifecycle, whose geometry has a separate production-bound fixture.
        TownServiceSharedCue.LocalReady = true; TownServiceSharedCue.LocalStrength = 1f;
        TownServiceSync.Tick(author, author);
        Check(TownServiceSync.HasPublishedSource(face)
            && TownServiceSync.Calls.Exists(call => call.Key == "face.62201" && call.Source == face
            && call.Provenance == null && call.CloneOf == null && call.Prewarm),
            "offered physical face publishes without a recycled native source widget");
        ushort faceId = TownServiceSync.ModuleId(face), bodyId = TownServiceSync.ModuleId(body);
        Check(Publisher622Priority(faceId) && Publisher622Priority(bodyId),
            "actual offered face and backing receive original artwork priority");
        var offerings = (HashSet<Transform>)typeof(TownServiceMirror).GetField("MotionOfferings", PrivateStatic)!.GetValue(null)!;
        Check(offerings.Contains(face) && offerings.Contains(body),
            "actual offered face and backing use the independent live motion lane");
        foreach (TownServiceSurface surface in ritual.Surfaces)
            Check(Publisher622Priority(TownServiceSync.ModuleId(surface.Panel.Target)),
                "every actual native enhancement inventory holder and decision is prioritized");
        Check(Publisher622Priority(TownServiceSync.ModuleId(tooltip)), "actual original enhancement tooltip is prioritized");
        ushort decisionId = TownServiceSync.ModuleId(confirmation);
        Check(Publisher622Priority(decisionId), "actual original palm confirmation is prioritized");
        var ids = new List<ushort>();
        foreach (Transform row in rows)
        {
            Check(TownServiceSync.HasPublishedSource(row),
                "every pooled original enhancement row bypasses the coarse viewport census");
            ushort id = TownServiceSync.ModuleId(row); ids.Add(id);
            Check(Publisher622Priority(id), "all original upgrade rows receive artwork priority");
        }
        Check(!TownServiceSync.Calls.Exists(call => call.Source == handoff.Zone),
            "local card pre-drop guide is absent from the actual published modules");
        int awakes = GameplayFixture.Awakes, enables = GameplayFixture.Enables;
        FastCapture first = CaptureFast();
        Check(first.Artwork.Count > rows.Count && first.Motion.Count > 0,
            "native publisher produces actual original artwork and independent motion packets");
        // Suspend only the local fixture endpoint before replay. Otherwise this
        // single process can elect its own older visitor lease over the remote
        // endpoint during a slow first TMP capture. The real peers are separate.
        object endpoint = typeof(TownServiceMirror).GetField("PrivateLane", PrivateStatic)!.GetValue(null)!;
        endpoint.GetType().GetField("Active", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(endpoint, false);
        NetPlayerActors.Peer = 10;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        DeliverMotion(2, first);
        Check(GloomhavenVR.WorldUI.TownServiceSharedCue.MageReady.TryGetValue(2, out bool ready) && ready,
            "offered exact face opens the shared resident palm before original artwork arrives");
        var lateFace = new List<byte[]>();
        // Deliberately reverse content order and delay only the real offered face.
        for (int i = first.Artwork.Count - 1; i >= 0; i--)
        {
            byte[] bytes = first.Artwork[i]; TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? frame);
            if (frame!.Module == faceId) lateFace.Add(bytes); else Receive(2, new[] { bytes });
        }
        IEnumerator settle = FastSettle(observer, .22f); while (settle.MoveNext()) yield return settle.Current;
        Check(Remote(2, faceId) == null, "delayed original offered artwork remains a pending exact face");
        foreach (ushort id in ids)
            Check(Remote(2, id) == null,
                "actual original upgrade rows wait for the complete offered-card picture");
        card.position += new Vector3(.15f, -.07f, .1f); card.rotation = Quaternion.Euler(23, 81, 14);
        ((RectTransform)content).anchoredPosition += new Vector2(0, 27);
        rows[2].Find("Native highlight").GetComponent<Image>().color = Color.magenta;
        decisionGate.alpha = .45f;
        settle = FastSettle(observer, .09f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceSync.Tick(author, author); FastCapture moving = CaptureFast();
        NetPlayerActors.Peer = 10; TownServiceMirror.SharedFrameForRemote = _ => observer; DeliverMotion(2, moving);
        Receive(2, lateFace);
        settle = FastSettle(observer, .17f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding? observedFace = Remote(2, faceId);
        Check(observedFace != null && observedFace.Root.gameObject.activeInHierarchy,
            "late exact offered face joins after earlier original motion and independent row artwork");
        for (int i = 0; i < rows.Count; i++)
        {
            TownServiceBinding? remote = Remote(2, ids[i]);
            Check(remote != null && remote.Root.gameObject.activeInHierarchy,
                "actual original row playback has no missing upgrade-option branches");
            TextMeshProUGUI original = rows[i].Find("Original option").GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI copy = remote!.Root.Find("Original option").GetComponent<TextMeshProUGUI>();
            Check(copy.text == original.text && copy.fontSize == original.fontSize && copy.color == original.color,
                "original option text style and order survive real reverse message arrival");
            Check(Vector2.Distance(((RectTransform)copy.transform).anchoredPosition, ((RectTransform)original.transform).anchoredPosition) < .001f,
                "original option geometry survives actual capture codec and playback");
        }
        Vector3 expected = observer.TransformPoint(author.InverseTransformPoint(face.position));
        Quaternion rotation = observer.rotation * Quaternion.Inverse(author.rotation) * face.rotation;
        var geometry = new System.Text.StringBuilder();
        geometry.AppendLine("owner=" + face.position.ToString("F6") + "; expected=" + expected.ToString("F6")
            + "; actual=" + observedFace!.Root.position.ToString("F6") + "; angle=" + Quaternion.Angle(observedFace.Root.rotation, rotation));
        foreach (byte[] bytes in moving.Motion)
        {
            TownServiceMotionCodec.TryRead(bytes, bytes.Length, out TownServiceMotionPacket? packet);
            foreach (TownServiceMotionEntry entry in packet!.Entries)
                if (entry.Module == faceId) geometry.AppendLine("motion kind=" + entry.Kind + "; pose=" + string.Join(",", entry.Pose));
        }
        File.WriteAllText(Path.Combine(_output, "late-face-geometry.txt"), geometry.ToString());
        Check(Vector3.Distance(observedFace!.Root.position, expected) < .0002f
            && Quaternion.Angle(observedFace.Root.rotation, rotation) < .02f,
            "late original offered face adopts the latest exact owner card position and orientation");
        Check(Remote(2, ids[2])!.Root.Find("Native highlight").GetComponent<Image>().color == Color.magenta,
            "native upgrade hover highlight advances while the offered face artwork is delayed");
        Transform copiedInventory = Remote(2, TownServiceSync.ModuleId(inventory))!.Root;
        Check(Vector2.Distance(((RectTransform)copiedInventory.Find("Native viewport/Native scroll content")).anchoredPosition,
                ((RectTransform)content).anchoredPosition) < .001f
            && copiedInventory.Find("Native viewport").GetComponent<RectMask2D>().padding == viewport.GetComponent<RectMask2D>().padding,
            "native scroll movement and exact clipping advance independently of late offered artwork");
        Transform copiedDecision = Remote(2, decisionId)!.Root;
        // Original decision transitions span the measured owner sample interval
        // (bounded at 250 ms), rather than the mechanical-control 100 ms cap.
        Check(copiedDecision.GetComponent<CanvasGroup>().alpha >= .45f
            && copiedDecision.GetComponent<CanvasGroup>().alpha < 1f,
            "original confirmation transition preserves an intermediate visible owner state");
        settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        Check(Mathf.Abs(copiedDecision.GetComponent<CanvasGroup>().alpha - .45f) < .0001f
            && copiedDecision.Find("Original decision caption").GetComponent<TextMeshProUGUI>().text == decisionText.text
            && copiedDecision.GetComponent<Button>() == null,
            "original confirmation content and transition advance without remote gameplay callbacks");
        Check(GameplayFixture.Awakes == awakes && GameplayFixture.Enables == enables,
            "actual row and face clones never invoke original gameplay callbacks");
        File.WriteAllText(Path.Combine(_output, "publisher622-native-census.txt"),
            "Original pool rows=" + rows.Count + "; captured modules=" + TownServiceSync.ModuleCount
            + "; offered NativeSource=null; reverse artwork arrival; latest motion before face\n");
    }

    private static void NativeCatalogPublisher622()
    {
        Publisher622Reset(1);
        Transform author = Go("actual visible cabinet publisher").transform;
        var catalog = new TownServiceCatalog();
        var rack = new TownServiceMerchantDrawer { Root = Go("Original crank", author).transform,
            HousingRoot = Go("Original cabinet", author).transform, Page = 2, FromPage = 1, ToPage = 3, PageCount = 5 };
        rack.Follow(new TownRackState { Page = 2, From = 1, To = 3, PageCount = 5, Turn = 1, Elapsed = .1f });
        catalog.Drawers.Add(rack);
        for (int page = 0; page < 5; page++)
        {
            Transform mount = Go("Original page " + page, author).transform;
            Transform card = Image("Original item", mount, Vector2.zero, new Vector2(150, 220), Color.blue).transform;
            Transform row = Go("Original cost", author).transform;
            catalog.Entries.Add(new TownServiceCatalog.Entry { NavigationRack = rack, Page = page,
                ItemId = 62210 + page, CardRoot = card, PhysicalMount = mount,
                FaceRoot = Go("Original sold-out annotation", mount).transform,
                BodyRoot = Go("Original backing", mount).transform, RowContent = row, RowSource = row });
        }
        TownServicePublicMerchant.Catalog = catalog; TownServicePublicMerchant.StationRoot = author;
        TownServicePublicMerchant.Session++; TownServiceSync.TickPublic(author, author, catalog, TownServicePublicMerchant.Session, 0);
        using (TownServiceMirror.UsePublicLane())
            foreach (TownServiceCatalog.Entry entry in catalog.Entries)
            {
                if (entry.Page == 0 || entry.Page == 4)
                { Check(!TownServiceSync.Calls.Exists(call => call.Source == entry.CardRoot), "cold nonvisible cabinet pages remain outside the actual publication census"); continue; }
                foreach (Transform source in new[] { entry.MountRoot, entry.FaceRoot!, entry.CardRoot, entry.BodyRoot!, entry.RowContent! })
                    Check(Publisher622Priority(TownServiceSync.PublicModuleId(source)),
                        "every actual current from and to cabinet page receives original artwork priority");
            }
        TownServicePublicMerchant.Catalog = null; TownServiceSync.ResetPublic();
    }

    private static void TempleBodyOrdering622()
    {
        Publisher622Reset(2);
        Transform author = Go("first temple body publisher").transform;
        var ritual = new TownServiceRitual(); TownServicePresentation.Ritual = ritual;
        Transform source = Go("original temple price").transform;
        var piece = new TownServiceRitual.Piece { Key = "temple.row", BodyKey = "ritual.purse",
            Source = source, Content = Go("original temple inscription", author).transform,
            Body = Go("actual first purse body", author).transform, Root = Go("actual wrist preview", author).transform };
        ritual.Pieces.Add(piece); TownServiceSync.Tick(author, author);
        Check(TownServiceSync.Calls.FindIndex(call => call.Source == piece.Body)
            < TownServiceSync.Calls.FindIndex(call => call.Source == piece.Content),
            "first temple publication registers actual body before original inscription artwork");
        Check(Publisher622Priority(TownServiceSync.ModuleId(piece.Body)) && Publisher622Priority(TownServiceSync.ModuleId(piece.Content)),
            "first temple body and its original inscriptions both receive actual artwork priority");
        ushort bodyId = TownServiceSync.ModuleId(piece.Body), inscriptionId = TownServiceSync.ModuleId(piece.Content);
        int allocated = TownServiceSync.AllocatedIds;
        // This is the actual TickCore -> PublishNative -> RegisterModule path. The
        // older motion proof registered one fixed ID by hand and consequently never
        // exercised the Build622 address switch on pickup.
        foreach (bool held in new[] { true, false, true, false })
        {
            piece.Token.IsHeld = held;
            piece.Token.IsMoving = held;
            piece.Token.PhysicalAtHome = !held;
            TownServiceSync.Calls.Clear(); TownServiceSync.Tick(author, author);
            Check(TownServiceSync.ModuleId(piece.Body) == bodyId
                && TownServiceSync.ModuleId(piece.Content) == inscriptionId
                && TownServiceSync.AllocatedIds == allocated,
                "actual purse pickup keeps its prepared original module and template identity");
            Check(TownServiceSync.Calls.Exists(call => call.Source == piece.Body
                && call.Key == "ritual.purse.held" && call.Prewarm),
                "actual purse wrist hold and return all retain prepared original visitor artwork");
        }
    }

    public static IEnumerator RunPublisher622(string output, string variant, string suite)
    {
        _output = Path.Combine(output, variant + "-evidence"); Directory.CreateDirectory(_output); _assertions = 0;
        try
        {
            IEnumerator cold = ColdAssets622(); while (cold.MoveNext()) yield return cold.Current;
            ColdPhysicalPurse622();
            var cameraGo = Go("Publisher622 camera"); _camera = cameraGo.AddComponent<Camera>();
            _camera.enabled = false; GloomhavenVR.Rig.VRRigDriver.HeadCamera = _camera;
            IEnumerator enchant = NativeEnhancementPublisher622(); while (enchant.MoveNext()) yield return enchant.Current;
            NativeCatalogPublisher622(); TempleBodyOrdering622();
            // Keep the ordinary routing fixture's expectations synchronized with
            // the actual face lifetime and the explicit local-guide exception.
            Publisher622Reset(1); TownServiceSync.UseProductionPublish = false; PublisherRouting();
            File.WriteAllText(Path.Combine(_output, "assertions.txt"), _assertions + " assertions\n");
        }
        finally
        {
            TownServiceSync.Reset(); TownServiceSync.ResetPublic(); TownServiceMirror.Shutdown(); TownServiceSync.UseProductionPublish = false;
            foreach (var go in Objects) if (go != null) Object.DestroyImmediate(go);
            foreach (var asset in Assets) if (asset != null) Object.DestroyImmediate(asset);
            Objects.Clear(); Assets.Clear(); Baselines.Clear(); GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
            NativeTemplates.Originals.Clear(); NativeTemplates.BoundaryRoots.Clear();
        }
    }
}
#endif
