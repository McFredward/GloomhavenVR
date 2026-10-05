using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.Cards;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.Hands;

namespace GloomhavenVR.WorldUI;

/// <summary>Publishes owner-authored original native service widgets after their final VR layout.
/// Gameplay controllers remain exclusively on their original local objects.</summary>
internal sealed partial class TownServiceSync
{
    private static readonly TownServiceSync Private = new(), Public = new();
    internal static void Prepare() => Private.PrepareCore();
    internal static void Tick(Transform sharedFrame, Transform? stationRoot) => Private.TickCore(sharedFrame, stationRoot);
    internal static bool LocalOwnsInteraction(byte service, uint sourceSession) =>
        Private._session == sourceSession && Private._service == service && Private._generation != 0
        && TownServiceMirror.LocalOwnsInteraction(service, Private._generation);
    internal static void Reset() => Private.ResetCore();
    internal static void ResetPublic() { ResetStock(); using (TownServiceMirror.UsePublicLane()) Public.ResetCore(); }
    internal static void TickPublic(Transform frame, Transform station, TownServiceCatalog catalog, uint session, float age)
    { using (TownServiceMirror.UsePublicLane()) Public.TickCatalog(frame, station, catalog, session, age); }
    private sealed class Published
    {
        internal ushort Id;
        internal string Address = string.Empty;
        internal string Identity = string.Empty;
        internal Transform Source = null!;
        internal Func<Transform, bool> Exclude = null!;
        internal bool Seen, CatalogResident;
    }
    private sealed class SourceEntry
    {
        internal string Key = string.Empty;
        internal Transform Root = null!;
        internal Transform? Parent;
        internal Transform? CatalogOwner;
        internal readonly List<Published> Parts = new();
        internal readonly List<RectMask2D> Masks = new();
        internal readonly List<CanvasGroup> Groups = new();
        internal bool Seen, CatalogResident;
        internal float OwnershipCheckAfter;
        internal bool Complete;
        internal TownRackState? RackClock;
    }
    private readonly Dictionary<Transform, SourceEntry> Sources = new();
    private readonly List<Transform> RemovedSources = new();
    private readonly List<TownRackMember> RackMembers = new();
    private readonly Vector3[] Corners = new Vector3[4];
    private float _prepareAfter;
    private readonly Dictionary<string, Published> Modules = new(StringComparer.Ordinal);
    private readonly List<string> Removed = new();
    private readonly HashSet<Transform> Visited = new();
    private readonly List<Transform> Dynamic = new();
    private readonly List<Transform> PriorityRoots = new();
    private readonly Dictionary<string, float> Failures = new(StringComparer.Ordinal);
    private float _reportWindow;
    private int _reportCount;
    private uint _session;
    // Wire lifetime is presentation-only. Never reuse an earlier generation after native
    // close/reopen, network reset or relocation; captured native session closures stay intact.
    private uint _generation;
    private ulong _relocationRevision;
    private bool _generationExhausted;
    private byte _service;
    private ushort _nextId;
    private Transform? _sharedFrame;
    private void PrepareCore()
    {
        if (!MapRoomDriver.Active || Time.unscaledTime < _prepareAfter) return;
        if (!WorldUIConfig.ImmersiveTownServices.Value && !TownServicePopulation.HasRemoteVisitors) return;
        try
        {
            NativeTemplates.Initialize(); TownServiceNativeAssets.Tick();
            NativeTemplates.PreparePhysicalPurses();
            // Resident item artwork and native pool layouts serve the same canonical
            // avatar fan even when the player has disabled the visual loading indicator.
            NetAvatarDriver.PrepareMerchantCardsForLoading();
        }
        catch (Exception e) { _prepareAfter = Time.unscaledTime + 2f; Report("prepare", e); }
    }
    private void TickCore(Transform sharedFrame, Transform? stationRoot)
    {
        _sharedFrame = sharedFrame;
        TownServiceMirror.SharedFrameForRemote = ResolveFrame;
        TickStock(sharedFrame);
        PrepareCore();
        IReadOnlyList<TownServiceEnhancementHandoff.ReturnPresentation> returns = TownServiceEnhancementHandoff.Returning;
        bool active = TownServicePresentation.Active && stationRoot != null;
        bool inspection = TownServiceMerchantHandoff.Active;
        if (!active && !inspection && returns.Count == 0) { ResetCore(); return; }
        if (!NativeTemplates.Ready) return;
        bool inspectionIdentity = inspection && (!active || TownServicePresentation.Service == 1);
        byte service = active ? TownServicePresentation.Service : inspection ? (byte)1 : (byte)3;
        uint session = inspectionIdentity ? TownServiceMerchantHandoff.Session
            : active ? TownServicePresentation.Session : returns[0].Session;
        ulong relocation = active ? TownServicePresentation.RelocationRevision : _relocationRevision;
        if (!active) stationRoot = inspection ? TownServiceMerchantHandoff.StationRoot ?? sharedFrame
            : returns[0].StationRoot != null ? returns[0].StationRoot : sharedFrame;
        if (_generationExhausted) return;
        if (_session != session || _service != service || _relocationRevision != relocation)
        {
            if (_generation == uint.MaxValue)
            {
                // Do not wrap into a still-known presentation. This process has exhausted its
                // finite wire namespace; native services continue without new mirror sessions.
                ResetCore(); _generationExhausted = true;
                Report("generation", new InvalidOperationException("Town presentation generation exhausted; restart the mod to resume publishing."));
                return;
            }
            ResetCore(); _session = session; _service = service; _relocationRevision = relocation; _nextId = 0;
            _generation++;
        }
        // A new existing wire session rebuilds observers at the actual new pose even when
        // transport coalescing drops every invisible relocation sample. Ordinary native fades
        // and hand movement retain this generation and therefore their normal interpolation.
        TownServiceMirror.BeginSession(service, _generation, sharedFrame, stationRoot!,
            inspectionIdentity ? TownServiceMerchantHandoff.SessionAge : active ? TownServicePresentation.SessionAge : returns[0].SessionAge);
        // Do not establish invisible module baselines at the relocation boundary: losing those
        // samples must not make the first visible state depend on a discarded zero-alpha packet.
        if (active && TownServicePresentation.RelocationVisibility <= 0f) return;
        foreach (Published module in Modules.Values) module.Seen = false;
        foreach (SourceEntry source in Sources.Values) source.Seen = false;
        Visited.Clear(); Dynamic.Clear(); PriorityRoots.Clear();
        foreach (TownServiceEnhancementHandoff.ReturnPresentation returning in returns)
        {
            Transform? face = returning.Face, body = returning.Body;
            if (face != null) TownServiceMirror.RegisterCardReturn(face, returning.Card.TryTownReturnMotion);
            if (body != null) TownServiceMirror.RegisterCardReturn(body, returning.Card.TryTownReturnMotion);
            if (face != null) PriorityRoots.Add(face);
            if (body != null) PriorityRoots.Add(body);
            // The window's pooled selected-card widget may already be recycled. The actual
            // flying face and captured identity are the only lifetime-safe provenance here.
            Publish("face." + returning.CardId.ToString(System.Globalization.CultureInfo.InvariantCulture), face, prewarm: true);
            Publish("map.cardbody", body);
        }
        if (inspection)
        {
            if (TownServiceMerchantHandoff.Zone != null) PriorityRoots.Add(TownServiceMerchantHandoff.Zone);
            // Maintainer ruling, Build614 hardware review: town pre-drop guides are
            // personal affordances. Publish the offered originals below, never the
            // visitor's local "place card here" canvas. Scenario board guides keep
            // their existing independent synchronization.
            foreach (ItemsPile.ItemChip chip in TownServiceMerchantHandoff.OwnedChips)
            {
                if (chip == null || chip.NativeItemCard == null || chip.Item == null
                    || ItemsPile.InspectionUsesAvatarTransport(chip)) continue;
                Transform mount = chip.InspectionMount;
                Transform face = chip.NativeItemCard.transform;
                Transform? body = chip.InspectionBody;
                // A newly revealed original item fan is as latency sensitive as
                // a held card; its complete immutable fronts cannot queue behind
                // the public cabinet's cold pages. Exact fan/holder provenance
                // also lets peers ride the already smoothed rig hand each frame.
                PriorityRoots.Add(mount); PriorityRoots.Add(face); if (body != null) PriorityRoots.Add(body);
                VRHand? hand = chip.Holder ?? (!chip.TownOffering && !chip.IsCollapsing
                    ? VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left : null);
                TownServiceMirror.RegisterMotionHand(mount, hand, followsRotation: chip.Holder != null);

                TownServiceMirror.RegisterCardReturn(mount, chip.TryTownReturnMotion, hand);
                TownServiceMirror.RegisterCardReturn(face, chip.TryTownReturnMotion, hand);
                if (body != null) TownServiceMirror.RegisterCardReturn(body, chip.TryTownReturnMotion, hand);
                TownServiceMirror.RegisterMotionOffering(mount, chip.TownOffering);
                TownServiceMirror.RegisterMotionOffering(face, chip.TownOffering);
                if (body != null) TownServiceMirror.RegisterMotionOffering(body, chip.TownOffering);
                Publish("item." + chip.Item.ID.ToString(System.Globalization.CultureInfo.InvariantCulture), face, prewarm: true);
                Publish(TownServiceInspectionBody.Key(chip), body, prewarm: true);
            }
        }
        if (active)
        {
            string prefix = service == 1 ? "merchant" : service == 2 ? "temple" : "enchant";
            TownServiceCatalog? catalog = TownServicePresentation.Catalog;
            if (service != 1 && catalog == null && TownServicePresentation.Ritual == null)
                Publish(prefix, TownServicePresentation.Window != null ? TownServicePresentation.Window.transform : null);
            foreach (TownServiceSurface surface in TownServicePresentation.LocalSurfaces)
                Publish(surface.Id == 40 ? "merchant.exit" : surface.Id == 10 ? prefix + ".inventory"
                    : surface.Id == 11 ? "enchant.holder" : "enchant.scroll", surface.Panel.Target);
            // The counter and every stock card are authored by TickPublic's one
            // elected public lane. Publishing the same catalog in each visitor's
            // private lane duplicates shelves and lets a visitor's locally
            // filtered page leak over the shared cabinet during handover. This
            // lane contains only that visitor's fan, held card and decision.
            TownServiceRitual? ritual = TownServicePresentation.Ritual;
            if (ritual != null)
            {
                // Maintainer ruling, 2026-10-04: all town pre-drop guides, including
                // the temple purse ghost, are visitor-local. Actual held/donated
                // originals and their effects remain shared through the piece modules.
                Publish(prefix + ".counter", TownServicePresentation.CounterFurniture);
                TownServiceEnhancementHandoff? handoff = ritual.Handoff;
                if (handoff != null)
                {
                    PriorityRoots.Add(handoff.Zone);
                    if (handoff.Card != null && handoff.OfferedCardId > 0 && handoff.Face != null)
                    {
                        Transform? body = handoff.Card.transform.Find("Visual/Backing");
                        PriorityRoots.Add(handoff.Face);
                        if (body != null) PriorityRoots.Add(body);
                        TownServiceMirror.RegisterMotionOffering(handoff.Face, true);
                        if (body != null) TownServiceMirror.RegisterMotionOffering(body, true);
                        Publish("face." + handoff.OfferedCardId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            handoff.Face, prewarm: true);
                        Publish("map.cardbody", body);
                    }
                }
                foreach (TownServiceCardSlots.Point point in ritual.CardSlots.Points)
                {
                    if (point.Content != null) PriorityRoots.Add(point.Content);
                    Publish("enchant.point", point.Content, point.Source, point.CloneOf, prewarm: true);
                }
                foreach (TownServiceRitual.Piece piece in ritual.Pieces)
                {
                    if (service == 2)
                    {
                        VRHand? previewHand = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
                        // TempleOffering authors this exact wrist preview frame. A grabbed
                        // body reparents to its actual GrabAnchor and a deposited body leaves
                        // that frame, so neither is guessed from observer proximity.
                        TownServicePursePresentation.RegisterMotion(piece.Token, piece.Root, piece.Body, previewHand);
                        PriorityRoots.Add(piece.Root); PriorityRoots.Add(piece.Body);
                        if (piece.Content != null) PriorityRoots.Add(piece.Content);
                    }
                    if (piece.Token.IsMoving)
                    {
                        PriorityRoots.Add(piece.Body);
                        if (piece.Content != null) PriorityRoots.Add(piece.Content);
                    }
                    // Wrist preview, physical hold, bowl deposit and return are the
                    // same original purse. Build622 changed its template address on
                    // pickup, retiring the ready wrist module and waiting for a new
                    // complete artwork snapshot under the held address. Keep that
                    // identity warm throughout; only its existing numeric hand,
                    // parent, pose and opacity change. The independent visitor
                    // address also retains a non-elected visitor's real purse.
                    Publish(service == 2 ? "ritual.purse.held" : piece.BodyKey,
                        piece.Body, prewarm: service == 2);
                    Publish(piece.Key, piece.Content, piece.Source.transform, piece.CloneOf);
                    // The priestess has one shared stand but each visitor can hold their
                    // own purse. A stable independent address lets observers keep that prop
                    // from a non-elected visitor without cloning the rest of the stand.

                    if (piece.DetailContent != null && piece.DetailSource != null)
                    {
                        UITooltip? detailTooltip = piece.DetailSource.GetComponent<UITooltip>();
                        Publish(service == 2 ? NativeTemplates.TempleTooltipKey(piece.DetailSource)
                            : detailTooltip != null ? NativeTemplates.TooltipKey(detailTooltip) : piece.DetailKey,
                            piece.DetailContent, piece.DetailSource, piece.DetailCloneOf);
                    }
                }
                // This is a registered native boundary, excluded from the folio inventory
                // clone. The original hover-price explanation needs its own module.
                if (service == 3)
                {
                    Transform? tooltipRoot = NativeTemplates.Original("enchant.tooltip");
                    UITooltip? nativeTooltip = tooltipRoot != null ? tooltipRoot.GetComponent<UITooltip>() : null;
                    if (tooltipRoot != null) PriorityRoots.Add(tooltipRoot);
                    Publish(nativeTooltip != null ? NativeTemplates.TooltipKey(nativeTooltip) : "enchant.tooltip", tooltipRoot);
                }
                foreach (TownServiceSurface surface in ritual.Surfaces)
                {
                    PriorityRoots.Add(surface.Panel.Target);
                    Publish(surface.Id switch { 10 => "enchant.inventory", 13 => "enchant.capacity",
                        14 => "enchant.information", 15 => "enchant.buy", 16 => "enchant.sell",
                        _ => "enchant.holder" }, surface.Panel.Target);
                }
                foreach (TownServiceRitual.Inscription inscription in ritual.Inscriptions)
                {
                    Publish(inscription.Key, inscription.Content, inscription.Source, inscription.CloneOf);
                }
            }

            if (TownServicePresentation.WorkspaceProps != null)
                foreach (TownServiceWorkspace.Prop prop in TownServicePresentation.WorkspaceProps) Publish(prop.Key, prop.Root);

            if (service != 1 && catalog == null && ritual == null)
            {
                Transform? tooltipRoot = NativeTemplates.Original(prefix + ".tooltip");
                UITooltip? nativeTooltip = tooltipRoot != null ? tooltipRoot.GetComponent<UITooltip>() : null;
                Publish(nativeTooltip != null ? NativeTemplates.TooltipKey(nativeTooltip) : prefix + ".tooltip", tooltipRoot);
            }
            else if (catalog != null && catalog.PreviewContent != null && catalog.PreviewSource != null)
            {
                PriorityRoots.Add(catalog.PreviewContent);
                Transform previewSource = catalog.PreviewSource;
                UITooltip? previewTooltip = previewSource.GetComponent<UITooltip>();
                Publish(previewTooltip != null ? NativeTemplates.TooltipKey(previewTooltip) : "merchant.tooltip",
                    catalog.PreviewContent, previewSource, catalog.PreviewCloneOf);
                PublishCopiedCards(previewSource, catalog.PreviewCloneOf);
            }
            Publish("item.confirm", NativeTemplates.Original("item.confirm"));
            Publish("enhance.confirm", NativeTemplates.Original("enhance.confirm"));
            if (TownServicePresentation.Tray != null) Publish("tray", TownServicePresentation.Tray.Root);
            UITooltip? tooltip = NativeTemplates.Tooltip;
            if (tooltip != null && catalog != null && catalog.HintContent != null && catalog.HintSource == tooltip.transform)
            {
                PriorityRoots.Add(catalog.HintContent);
                Publish(NativeTemplates.TooltipKey(tooltip), catalog.HintContent, catalog.HintSource, catalog.HintCloneOf);
                PublishCopiedCards(tooltip.transform, catalog.HintCloneOf);
            }
            else if (tooltip != null && tooltip.gameObject.activeInHierarchy && OwnsAnchor(tooltip.m_AnchorToTarget))
                Publish(NativeTemplates.TooltipKey(tooltip), tooltip.transform);
            // Original pooled branches are separate modules: adding/removing a row must never change
            // the native static template or replace the inventory container behind another visitor.
            for (int i = 0; i < Dynamic.Count; i++)
            {
                Transform source = Dynamic[i]; if (source == null) continue;
                string? key = DynamicKey(source);
                if (key != null)
                {
                    bool enhancementRow = key == "enchant.row";
                    if (enhancementRow) PriorityRoots.Add(source);
                    // A pool-row root can lie outside the viewport while its native
                    // child graphics are visible. Publish the complete original; its
                    // inherited RectMask2D, not this coarse root test, owns clipping.
                    Publish(key, source, prewarm: enhancementRow);
                }
            }
            foreach (TownServiceToken sample in TownServicePresentation.Samples)
            {
                if (sample.IsPhysical) continue; // The same native face and body already publish their held pose.
                Transform? held = sample.HeldContent;
                if (held == null) continue;
                PublishHeld(sample, sample.Source);
            }
        }
        // A palm decision belongs to the inspection session too; the native merchant
        // window can already be closed while its original confirmation remains visible.
        if (active || inspection)
        {
            foreach (TownServicePalmConfirmation.Entry confirmation in TownServicePalmConfirmation.Active)
                foreach (TownServiceSurface surface in confirmation.Surfaces)
                {
                    PriorityRoots.Add(surface.Panel.Target);
                    Publish((confirmation.Service == 1 ? "item.confirm.part." : "enhance.confirm.part.")
                        + (surface.Id - 60), surface.Panel.Target);
                }
        }
        Removed.Clear();
        foreach (var pair in Modules) if (!pair.Value.Seen) Removed.Add(pair.Key);
        foreach (string key in Removed) { TownServiceMirror.UnregisterModule(Modules[key].Id); Modules.Remove(key); }
        PruneSources();
    }
    private void PruneSources()
    {
        RemovedSources.Clear();
        float now = Time.unscaledTime;
        foreach (var pair in Sources)
        {
            if (pair.Key == null)
                RemovedSources.Add(pair.Key!);
            else if (!pair.Value.Seen)
            {
                // A complete dormant bank keeps its original owner across page epochs.
                // Recheck native parentage at a bounded census, not for every hidden
                // item each frame. Seen sources and destroyed objects never wait here.
                if (pair.Value.CatalogResident && now < pair.Value.OwnershipCheckAfter) continue;
                pair.Value.OwnershipCheckAfter = now + .5f;
                if (pair.Value.CatalogOwner == null
                    || !ReferenceEquals(TownServiceCatalog.PresentationOwner(pair.Key), pair.Value.CatalogOwner))
                    RemovedSources.Add(pair.Key);
            }
        }
        foreach (Transform source in RemovedSources)
        {
            // Retained dormant parts are still registered while hidden. End that
            // lifetime as soon as their real original owner is lost or destroyed.
            foreach (Published part in Sources[source].Parts)
                if (part.CatalogResident)
                { part.CatalogResident = false; TownServiceMirror.UnregisterModule(part.Id); Modules.Remove(part.Identity); }
            Sources.Remove(source);
        }
    }
    private void PublishCatalog(TownServiceCatalog catalog, Transform? furniture)
    {
        const string prefix = "merchant";
                if (furniture != null) PriorityRoots.Add(furniture);
                Publish(prefix + ".counter", furniture);
                foreach (TownServiceMerchantDrawer rack in catalog.Drawers)
                {
                    // Cranks and racks are owner-authored moving geometry. Cards are separate
                    // native modules; exclude the rack's Content root to avoid duplicate faces.
                    PriorityRoots.Add(rack.Root); PriorityRoots.Add(rack.HousingRoot);
                    Publish("merchant.crank", rack.Root);
                    Publish("merchant.rack", rack.HousingRoot);
                }
                foreach (TownServiceCatalogCategory category in catalog.Categories)
                { PriorityRoots.Add(category.Root); Publish(category.Key, category.Root); }
                foreach (TownServiceMerchantCounter extension in catalog.Extensions)
                    Publish("merchant.return", extension.Root);
                // Catalog drop guides are also visitor-local; cabinet mechanics and
                // every visible stock card remain globally authored by this lane.
                // Mirror the actual counter, not the suppressed flat inventory. These widgets
                // retain native template provenance but have the owner's physical layout.
                foreach (TownServiceCatalog.Control control in catalog.Controls)
                { PriorityRoots.Add(control.Surface.Panel.Target); Publish(control.Key, control.Surface.Panel.Target); }

                PublishOriginalCatalogBank(catalog);
                foreach (TownServiceCatalog.Entry entry in catalog.PublicationEntries)
                {
                    if (!entry.Current || !entry.Warm || entry.Sample.IsMoving) continue;
                    PriorityRoots.Add(entry.MountRoot); PriorityRoots.Add(entry.FaceRoot);
                    PriorityRoots.Add(entry.CardRoot);
                    if (entry.BodyRoot != null) PriorityRoots.Add(entry.BodyRoot);
                    if (entry.RowContent != null) PriorityRoots.Add(entry.RowContent);
                    Publish("merchant.cardmount", entry.MountRoot, prewarm: true);
                    // The native item root does not contain its original Face canvas or
                    // sold-out band. Capture that owner-authored shelf presentation too;
                    // visitor-held stock intentionally has no shelf-only annotation.
                    Publish("merchant.cardface", entry.FaceRoot, prewarm: true);
                    Publish("item." + entry.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.CardRoot, prewarm: true);
                    Publish("merchant.cardbody", entry.BodyRoot, prewarm: true);
                    if (entry.RowContent != null)
                        Publish("merchant.row", entry.RowContent, entry.RowSource.transform, entry.RowCloneOf, prewarm: true);
                }
                foreach (TownServiceMerchantDrawer rack in catalog.Drawers) PublishRackClock(catalog, rack);
    }
    private void TickCatalog(Transform frame, Transform station, TownServiceCatalog catalog, uint session, float age)
    {
        _sharedFrame = frame; _service = 1;
        PrepareCore();
        if (!NativeTemplates.Ready) return;
        if (_session != session) { ResetCore(); _session = session; _service = 1; _nextId = 0; }
        TownServiceMirror.BeginSession(1, session, frame, station, age);
        foreach (Published module in Modules.Values)
        {
            module.Seen = TownServiceMirror.IsPublicAuthor && module.CatalogResident;
            if (module.CatalogResident) TownServiceMirror.SetCatalogDormant(module.Id, true);
        }
        foreach (SourceEntry source in Sources.Values) source.Seen = false;
        Visited.Clear(); Dynamic.Clear(); PriorityRoots.Clear();
        if (TownServiceMirror.IsPublicAuthor) PublishCatalog(catalog, null);
        Removed.Clear();
        foreach (var pair in Modules) if (!pair.Value.Seen) Removed.Add(pair.Key);
        foreach (string key in Removed) { TownServiceMirror.UnregisterModule(Modules[key].Id); Modules.Remove(key); }
        PruneSources();
    }
    private bool OwnsAnchor(Transform? target)
    {
        if (target == null) return false;
        if (TownServicePresentation.Window != null && target.IsChildOf(TownServicePresentation.Window.transform)) return true;
        foreach (TownServiceSurface surface in TownServicePresentation.LocalSurfaces)
            if (surface.Panel.Target != null && target.IsChildOf(surface.Panel.Target)) return true;
        foreach (SourceEntry source in Sources.Values)
            if (source.Seen && source.Root != null && target.IsChildOf(source.Root)) return true;
        return false;
    }
    private Transform? ResolveFrame(int _) => _sharedFrame;
    private string? DynamicKey(Transform source)
    {
        if (source.GetComponent<UIShopItemSlot>() != null) return "merchant.row";
        if (source.GetComponent<UITempleShopSlot>() != null) return "temple.row";
        if (source.GetComponent<UINewEnhancementShopSlot>() != null) return "enchant.row";
        if (source.GetComponent<UIEnhanceCardSlot>() != null) return "enchant.cardrow";
        if (source.GetComponent<UIEnhanceCardPoint>() != null) return "enchant.point";
        if (source.GetComponent<UIEnhancementButtonHighlight>() != null) return "enchant.highlight";
        AbilityCardUI? card = source.GetComponent<AbilityCardUI>(); if (card != null) return NativeTemplates.CardKey(card);
        ItemCardUI? item = source.GetComponent<ItemCardUI>(); if (item != null) return "item." + item.CardID;
        return null;
    }
    private void PublishHeld(TownServiceToken sample, Transform original)
    {
        PublishCopiedCards(original, sample.HeldCloneOf);
    }
    private void PublishCopiedCards(Transform original, Func<Transform, Transform?> cloneOf)
    {
        Transform? clone = cloneOf(original);
        string? key = DynamicKey(original);
        if (clone != null && key != null) Publish(key, clone, original, cloneOf);
        // Native pooled card boundaries retain their original identities after local sample or
        // preview neutralization; publishing only the outer tooltip would omit its item card.
        for (int i = 0; i < original.childCount; i++) PublishCopiedCards(original.GetChild(i), cloneOf);
    }
    private void Publish(string key, Transform? source, Transform? provenance = null, Func<Transform, Transform?>? cloneOf = null, bool prewarm = false)
    {
        if (source == null || !Visited.Add(source)) return;
        try
        {
            Transform? catalogOwner = TownServiceCatalog.PresentationOwner(source);
            if (!Sources.TryGetValue(source, out SourceEntry? sourceEntry) || sourceEntry.Key != key
                || !ReferenceEquals(sourceEntry.CatalogOwner, catalogOwner))
            {
                if (sourceEntry != null)
                    foreach (Published previous in sourceEntry.Parts)
                        if (previous.CatalogResident)
                        {
                            previous.CatalogResident = false;
                            TownServiceMirror.UnregisterModule(previous.Id); Modules.Remove(previous.Identity);
                        }
                sourceEntry = new SourceEntry { Key = key, Root = source, CatalogOwner = catalogOwner }; Sources[source] = sourceEntry;
                TownServiceNativeAssets.PrepareRoot(provenance != null ? provenance : source);
            }
            sourceEntry.Seen = true;
            if (!prewarm && cloneOf == null && NativeTemplates.IsDynamic(source) && !Visible(sourceEntry)) return;
            if (cloneOf == null) CollectDynamic(source);
            if (sourceEntry.Complete)
            {
                foreach (Published existing in sourceEntry.Parts)
                {
                    existing.Seen = true;
                    if (!Modules.ContainsKey(existing.Identity) && Modules.Count >= TownServiceFrame.MaxModules)
                        throw new InvalidDataException("Town service exceeds the simultaneous module budget.");
                    Modules[existing.Identity] = existing;
                    TownServiceMirror.RegisterModule(existing.Id, 1, existing.Source, existing.Exclude, existing.Address);
                    if (existing.CatalogResident) TownServiceMirror.SetCatalogDormant(existing.Id, false);
                    TownServiceMirror.SetPriority(existing.Id, IsPriority(source));
                }
                return;
            }
            // A transient template/asset failure must retry the entire partition census.
            sourceEntry.Parts.Clear();
            IReadOnlyList<NativeTemplates.Part> parts = NativeTemplates.Parts(key);
            foreach (NativeTemplates.Part part in parts)
            {
                Transform? root = NativeTemplates.At(source, part.Path);
                if (root == null) throw new InvalidDataException("Original subtree is absent: " + key + "|" + part.Path);
                string address = key + "|" + part.Path, identity = address + "@" + source.GetInstanceID();
                if (catalogOwner != null) identity += "/catalog/" + catalogOwner.GetInstanceID();
                if (!Modules.TryGetValue(identity, out Published? module))
                {
                    if (Modules.Count >= TownServiceFrame.MaxModules || _nextId >= ushort.MaxValue - 1)
                        throw new InvalidDataException("Town service exceeds the simultaneous module budget.");
                    var excluded = new HashSet<Transform>();
                    foreach (NativeTemplates.Part other in parts)
                    {
                        if (other == part) continue;
                        Transform? child = NativeTemplates.At(source, other.Path);
                        if (child != null && child.IsChildOf(root)) excluded.Add(child);
                    }
                    if (provenance != null && cloneOf != null)
                        CollectHeldBoundaries(provenance, cloneOf, excluded);
                    module = new Published { Id = ++_nextId, Address = address, Identity = identity, Source = root,
                        Exclude = child => excluded.Contains(child) || NativeTemplates.IsBoundary(child) };
                    // The template address is independent of a peer's pool order and module IDs.
                    NativeTemplates.Resolve(_service, 1, address);
                    TownServiceMirror.RegisterModule(module.Id, 1, root, module.Exclude, address);
                    Modules.Add(identity, module);
                }
                TownServiceMirror.SetPriority(module.Id, IsPriority(source));
                module.Seen = true; sourceEntry.Parts.Add(module);
            }
            sourceEntry.Complete = true;
        }
        catch (Exception e) { Report(key, e); }
    }
    private void PublishRackClock(TownServiceCatalog catalog, TownServiceMerchantDrawer rack)
    {
        if (!Sources.TryGetValue(rack.HousingRoot, out SourceEntry? housing)
            || !Sources.TryGetValue(rack.Root, out SourceEntry? crank) || housing.Parts.Count != 1 || crank.Parts.Count != 1) return;
        RackMembers.Clear(); ushort rackId = housing.Parts[0].Id;
        foreach (TownServiceCatalog.Entry entry in catalog.PublicationEntries)
            if (entry.Warm && !entry.Sample.IsMoving)
            {
                AddRackMembers(entry.MountRoot, entry, rack, rackId);
                AddRackMembers(entry.FaceRoot, entry, rack, rackId);
                AddRackMembers(entry.CardRoot, entry, rack, rackId);
                AddRackMembers(entry.BodyRoot, entry, rack, rackId);
                AddRackMembers(entry.RowContent, entry, rack, rackId);
            }
        RackMembers.Sort(CompareRackMembers);
        TownServiceMirror.SetCatalogBankPrepared(rackId, catalog.OriginalBankPrepared);
        TownRackState? previous = housing.RackClock;
        TownCatalogSlot[] layout = catalog.StockLayout;
        bool sameMembers = previous != null && previous.Members.Length == RackMembers.Count;
        if (sameMembers) for (int i=0;i<RackMembers.Count;i++) if (!RackMembers[i].Same(previous!.Members[i])) {sameMembers=false;break;}
        if (sameMembers && previous!.Turn == rack.TurnEpoch && previous.Elapsed == rack.TurnElapsed
            && previous.ScrollDirection == rack.ScrollDirection && previous.PageCount == rack.PageCount
            && previous.LeadAngle == rack.LeadAngle && previous.Crank == crank.Parts[0].Id
            && previous.Page == rack.Page && previous.From == rack.FromPage && previous.To == rack.ToPage
            && TownCatalogLayout.Same(previous.Layout, layout)) return;
        var state = new TownRackState { Cassette = true, ScrollDirection = rack.ScrollDirection, PageCount = (ushort)rack.PageCount, Turn = rack.TurnEpoch, Elapsed = rack.TurnElapsed, LeadAngle = rack.LeadAngle,
            Crank = crank.Parts[0].Id, Page = (ushort)rack.Page, From = (ushort)rack.FromPage, To = (ushort)rack.ToPage,
            Members = sameMembers ? previous!.Members : RackMembers.ToArray(), Layout = layout };
        housing.RackClock = state; TownServiceMirror.SetRack(rackId, state);
    }
    private int CompareRackMembers(TownRackMember a,TownRackMember b)
    {
        return a.Id.CompareTo(b.Id);
    }
    private void AddRackMembers(Transform? root,TownServiceCatalog.Entry entry,TownServiceMerchantDrawer rack,ushort rackId)
    {
        if (root == null || !Sources.TryGetValue(root,out SourceEntry? source)) return;
        foreach (Published part in source.Parts) if (part.Seen)
        {
            RackMembers.Add(new TownRackMember(part.Id,(ushort)entry.Page,entry.Sample.IsMoving));
            TownServiceMirror.SetRackMember(part.Id,rackId,(ushort)entry.Page,rack.TurnEpoch,entry.Sample.IsMoving,entry.PageGate);
        }
    }
    private bool IsPriority(Transform source)
    {
        foreach (Transform root in PriorityRoots)
            if (root != null && (source == root || source.IsChildOf(root))) return true;
        return false;
    }
    private void CollectDynamic(Transform root)
    {
        // Native pool lists are the authoritative topology. Enumerating their references avoids
        // walking thousands of stable row descendants on every VR frame.
        UIShopItemInventory? merchant = root.GetComponent<UIShopItemInventory>();
        if (merchant != null) { foreach (UIShopItemSlot row in merchant.slotPool) if (row != null) Dynamic.Add(row.transform); return; }
        UITempleShopInventory? temple = root.GetComponent<UITempleShopInventory>();
        if (temple != null) { foreach (UITempleShopSlot row in temple.slots) if (row != null) Dynamic.Add(row.transform); return; }
        UINewEnhancementShopInventory? enchant = root.GetComponent<UINewEnhancementShopInventory>();
        if (enchant != null) { foreach (UINewEnhancementShopSlot row in enchant.slotsPool) if (row != null) Dynamic.Add(row.transform); return; }
        UIPartyCharacterEnhancementAbilityCardsDisplay? cards = root.GetComponent<UIPartyCharacterEnhancementAbilityCardsDisplay>();
        if (cards != null) { foreach (UIEnhanceCardSlot row in cards.slotsPool) if (row != null) Dynamic.Add(row.transform); return; }
        UIEnhanceCardSlot? cardRow = root.GetComponent<UIEnhanceCardSlot>();
        if (cardRow != null)
        {
            if (cardRow.AbilityCard != null) Dynamic.Add(cardRow.AbilityCard.transform);
            foreach (UIEnhanceCardPoint point in cardRow.enhancementPoints) if (point != null) Dynamic.Add(point.transform);
            return;
        }
        if (root.GetComponent<UIShopItemSlot>() != null || root.GetComponent<UITempleShopSlot>() != null
            || root.GetComponent<UINewEnhancementShopSlot>() != null || root.GetComponent<UIEnhanceCardPoint>() != null
            || root.GetComponent<UIEnhancementButtonHighlight>() != null || root.GetComponent<ItemCardUI>() != null) return;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (NativeTemplates.IsDynamic(child)) { Dynamic.Add(child); continue; }
            if (NativeTemplates.IsBoundary(child)) continue;
            CollectDynamic(child);
        }
    }
    private void CollectHeldBoundaries(Transform original, Func<Transform, Transform?> cloneOf, HashSet<Transform> excluded)
    {
        for (int i = 0; i < original.childCount; i++)
        {
            Transform child = original.GetChild(i);
            if (NativeTemplates.IsBoundary(child))
            { Transform? clone = cloneOf(child); if (clone != null) excluded.Add(clone); }
            else CollectHeldBoundaries(child, cloneOf, excluded);
        }
    }
    private bool Visible(SourceEntry entry)
    {
        Transform source = entry.Root;
        if (!source.gameObject.activeInHierarchy) return false;
        if (entry.Parent != source.parent)
        {
            entry.Parent = source.parent; entry.Groups.Clear(); entry.Masks.Clear();
            for (Transform? ancestor = source; ancestor != null; ancestor = ancestor.parent)
            {
                CanvasGroup? group = ancestor.GetComponent<CanvasGroup>(); if (group != null) entry.Groups.Add(group);
                RectMask2D? mask = ancestor.GetComponent<RectMask2D>(); if (mask != null) entry.Masks.Add(mask);
            }
        }
        foreach (CanvasGroup group in entry.Groups)
        {
            if (group == null || !group.enabled) continue;
            if (group.alpha <= 0f) return false;
            if (group.ignoreParentGroups) break;
        }
        if (source is not RectTransform rect) return true;
        rect.GetWorldCorners(Corners);
        foreach (RectMask2D mask in entry.Masks)
        {
            if (mask == null || !mask.isActiveAndEnabled) continue;
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity), max = new(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < 4; i++)
            { Vector3 point = mask.rectTransform.InverseTransformPoint(Corners[i]); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            Rect area = mask.rectTransform.rect; Vector4 padding = mask.padding;
            if (max.x <= area.xMin + padding.x || min.x >= area.xMax - padding.z
                || max.y <= area.yMin + padding.y || min.y >= area.yMax - padding.w) return false;
        }
        return true;
    }
    private void ResetCore()
    {
        if (_session != 0) TownServiceMirror.EndSession();
        ResetCatalogBank(); Modules.Clear(); Sources.Clear(); RemovedSources.Clear(); Visited.Clear(); Dynamic.Clear(); PriorityRoots.Clear(); Removed.Clear(); _session = 0; _service = 0;
    }
    internal static void ResetNetwork() { Private.ResetCore(); ResetPublic(); TownServiceMirror.ResetNetwork(); }
    internal static void Shutdown()
    { TownServiceMerchantHandoff.Reset(); TownServicePublicMerchant.Reset(); Private.ResetCore(); ResetPublic(); TownServiceMirror.Shutdown(); NativeTemplates.Shutdown(); TownServiceNativeAssets.Shutdown(); Private._sharedFrame = Public._sharedFrame = null; Private.ReportReset(); Public.ReportReset(); }
    private void ReportReset()
    {
        Failures.Clear(); _reportWindow = 0; _reportCount = 0;
    }
    private void Report(string scope, Exception e)
    {
        float now = Time.unscaledTime;
        string key = scope + ": " + e.Message;
        if (now - _reportWindow >= 30f) { _reportWindow = now; _reportCount = 0; }
        if (_reportCount >= 8)
        {
            if (_reportCount == 8) { _reportCount++; VRLog.Note("TownServices", "Further presentation errors are suppressed for this 30-second interval."); }
            return;
        }
        if (Failures.TryGetValue(key, out float then) && now - then < 30) return;
        if (Failures.Count > 32) Failures.Clear(); Failures[key] = now; _reportCount++;
        VRLog.Note("TownServices", "Original widget publisher unavailable: " + key);
    }
}
