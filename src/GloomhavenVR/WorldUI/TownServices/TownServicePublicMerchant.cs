using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.Adventure;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Persistent read-only stock presentation. It never enters the native shop merely
/// to make cards visible; native gameplay begins only through a deliberate hand offering.</summary>
internal static class TownServicePublicMerchant
{
    private static TownServiceCatalog? _catalog;
    private static TownServiceStation? _station;
    private static GameObject? _mount, _mat;
    private static uint _session;
    private static float _opened, _retryAt;
    private static object? _character, _context;
    private static readonly HashSet<string> Failures = new(StringComparer.Ordinal);
    private static bool _failed, _observingPublic;
    internal static TownServiceCatalog? Catalog => _catalog;
    internal static Transform? StationRoot => _station?.Root;
    internal static uint Session => _session;
    internal static float SessionAge => Mathf.Max(0f, Time.unscaledTime - _opened);
    internal static bool CanClaim
    {
        get
        {
            // Browsing is a public operation, independent of character assignment
            // and of another visitor's detached/parked card. The previous receiver
            // gate disabled all six physical buttons whenever any owner rack member
            // was detached, even though its slot had already been vacated. The local
            // native presentation rows contain the complete public stock without a
            // selected character; a press elects this visitor as its next author.
            return TownServiceAvailability.NativeUnlocked(1);
        }
    }
    internal static void Claim()
    {
        if (_catalog == null || !CanClaim || !MapRoomDriver.Active || StoryComposite.PointOfNoReturn) return;
        if (!TownServiceMirror.IsPublicAuthor) FollowPublicRack();
        TownServiceMirror.ClaimPublicCatalog(); _observingPublic = false; _catalog.SetObserver(false);
    }
    // Artwork completeness gates the atomic picture replacement, not the public
    // mechanism or its local input proxy. One still-cold original image must not
    // strand the other player's category/page or make its next press start from
    // an obsolete local page.
    private static void FollowPublicRack()
    {
        if (_catalog == null || TownServiceMirror.PublicRack is not TownRackState state) return;
        if (state.Layout != null) _catalog.AdoptStockLayout(state.Layout);
        _catalog.Drawers[0].Follow(state);
    }
    private static object? Context()
    {
        object? character = NewPartyDisplayUI.PartyDisplay?.SelectedUISlot?.Data;
        if (_context == null || !ReferenceEquals(character, _character))
        { _character = character; _context = new object(); }
        return _context;
    }
    internal static void Tick()
    {
        if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value
            // MapRoomDriver tears down after the native adventure. ShopService reads
            // MapState again on every stock/discount lookup; do not run its census
            // during that intervening exit frame (Build 601 remote Player.log 75793).
            || AdventureState.MapState?.MapParty == null || AdventureState.MapState.HeadquartersState == null
            || !TownServiceGrantSync.CanUseImmersive
            || !TownServiceAvailability.NativeUnlocked(1)
            || TownServicePresentation.NativeFallbackFor(1))
        { Reset(); Failures.Clear(); _failed = false; _retryAt = 0; return; }
        if (Time.unscaledTime < _retryAt) return;
        try
        {
            TownServiceSync.Prepare();
            if (!NativeTemplates.Ready) return;
            if (_catalog == null)
            {
                _station = TownServicePopulation.Acquire(1);
                if (_station == null || !_station.IsReady) return;
                UIShopItemInventory? inventory = NativeTemplates.Original("merchant.inventory")?.GetComponent<UIShopItemInventory>();
                if (inventory == null) return;
                _mount = new GameObject("GloomhavenVR.Merchant.PublicStock");
                _mount.transform.SetParent(_station.Root, false); _mount.transform.localPosition = Vector3.up * .970f;
                _mat = new GameObject("GloomhavenVR.Merchant.OfferingContext"); _mat.transform.SetParent(_station.Root, false);
                uint session = ++_session; if (session == 0) session = ++_session;
                _opened = Time.unscaledTime;
                _catalog = new TownServiceCatalog(inventory, _mount.transform, Context,
                    () => MapRoomDriver.Active && WorldUIConfig.ImmersiveTownServices.Value && _session == session,
                    _mat.transform, persistent: true);
                UiScrollFocus.PhysicalHoverProbe = ProbeScrollHover;
                TownServiceMirror.CommitPublicVisibility = CommitPublicVisibility;
            }
            if (_station == null || _station.Root == null) { Reset(); return; }
            bool publicAuthor = TownServiceMirror.IsPublicAuthor;
            bool remoteReady = !publicAuthor && TownServiceMirror.HasReadyPublicPresentation;
            if (!publicAuthor) FollowPublicRack();
            // A manifest elects an author before that author's original page, holders,
            // faces and card bodies have arrived. Keep the last verified picture until
            // the receiver can replace it as one complete group. If we were already an
            // observer, the mirror retains that previous remote group; if we were the
            // author, keep this local cabinet. Neither case authorizes a stale publish.
            if (publicAuthor) _observingPublic = false;
            else if (remoteReady) _observingPublic = true;
            using (PerfMonitor.Scope("TownPublicStock.Catalog"))
            {
                _catalog.SetVisibility(Mathf.Clamp01((Time.unscaledTime - _opened) / .22f),
                    // Cabinet browsing stays available while another player owns a
                    // buy/sell decision. TownServiceMerchantHandoff.CanOffer keeps
                    // the separate physical transaction lease on card placement.
                    allowInput: !StoryComposite.PointOfNoReturn);
                _catalog.Tick(_station.Root.lossyScale.x);
            }
            using (PerfMonitor.Scope("TownPublicStock.Observer"))
            {
                _catalog.SetObserver(_observingPublic);
            }
            TownServiceCatalogCategory.TickLaser();
            _catalog.Drawers[0].TickStickScroll();
            if (_failed) { VRLog.Note("TownServices", "Persistent merchant stock presentation recovered."); _failed = false; }
        }
        catch (Exception error)
        {
            Reset(); _retryAt = Time.unscaledTime + 5f;
            _failed = true;
            string failure = error.GetType().FullName + ": " + error.Message;
            if (Failures.Count < 8 && Failures.Add(failure))
            {
                VRLog.Note("TownServices", "Persistent merchant stock unavailable; native merchant remains usable: " + failure);
                if (VRLog.WantsDebug) VRLog.Debug("TownServices", "Persistent merchant stock failure detail: " + error);
            }
        }
    }
    private static void ProbeScrollHover(VRHand hand)
    {
        // A consumer may run before the presentation tick observes a mode/config edge.
        if (MapRoomDriver.Active && WorldUIConfig.ImmersiveTownServices.Value
            && TownServiceAvailability.NativeUnlocked(1) && !StoryComposite.PointOfNoReturn)
            _catalog?.Drawers[0].NoteScrollHover(hand);
    }

    // Called by the mirror after it validates the candidate's complete original
    // cabinet and before it reveals that group. Hide the retained local picture
    // on this same render frame, regardless of presentation/receiver tick order.
    private static void CommitPublicVisibility()
    {
        if (_catalog == null) return;
        if (TownServiceMirror.IsPublicAuthor) _observingPublic = false;
        else if (TownServiceMirror.HasReadyPublicPresentation)
        {
            FollowPublicRack();
            _observingPublic = true;
        }
        _catalog.SetObserver(_observingPublic);
    }

    internal static void LateTick()
    {
        if (_catalog == null || _station == null || TownServicePopulation.Frame == null) return;
        _catalog.LateTick();
        TownServiceSync.TickPublic(TownServicePopulation.Frame, _station.Root, _catalog, _session,
            Mathf.Max(0f, Time.unscaledTime - _opened));
    }
    internal static void Reset()
    {
        if (UiScrollFocus.PhysicalHoverProbe == ProbeScrollHover) UiScrollFocus.PhysicalHoverProbe = null;
        if (TownServiceMirror.CommitPublicVisibility == CommitPublicVisibility)
            TownServiceMirror.CommitPublicVisibility = null;
        TownServiceSync.ResetPublic(); _catalog?.Dispose(); _catalog = null; _station = null;
        if (_mount != null) UnityEngine.Object.Destroy(_mount);
        if (_mat != null) UnityEngine.Object.Destroy(_mat);
        _mount = _mat = null; _character = _context = null;
        _observingPublic = false;
    }
}
