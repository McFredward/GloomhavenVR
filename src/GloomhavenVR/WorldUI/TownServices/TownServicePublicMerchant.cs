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
    // Loading is a display preparation owner. Browsing/input remains solely native-unlock
    // controlled; never turn an art dependency or another visitor into a gameplay lease.
    internal static bool PrepareCatalogForLoading()
    {
        // Local loading ends when the genuine local rows, sprites and observer
        // templates are ready. The independent shared bank still repairs itself
        // through its original network queues. Waiting here for authority election
        // or a remote loss-repair acknowledgement left the map spinner running over
        // an already playable local scene (Build624 paired hardware report).
        return _catalog != null && _catalog.PrepareOriginalBankForLoading();
    }
    internal static bool CanClaim
    {
        get
        {
            // Browsing is a public operation, independent of character assignment
            // and of another visitor's detached/parked card. The previous receiver
            // gate disabled all six physical buttons whenever any owner rack member
            // was detached, even though its slot had already been vacated. The local
            // native presentation rows contain the complete public stock without a
            // selected character; a press operates the host's existing original bank.
            return TownServiceAvailability.NativeUnlocked(1);
        }
    }
    // A visitor only operates the common mechanism. It must not change native
    // original-bank authorship merely by grabbing the crank or inspecting a card.
    internal static void Claim() { }
    internal static bool TrySelectCategory(TownServiceMerchantDrawer rack, int category)
    {
        if (_catalog == null || !ReferenceEquals(_catalog.Drawers[0], rack) || !CanClaim
            || !MapRoomDriver.Active || StoryComposite.PointOfNoReturn) return false;
        return TownMerchantControlSync.RequestCategory(category);
    }
    internal static bool TryTurnPage(TownServiceMerchantDrawer rack, int direction)
    {
        if (_catalog == null || !ReferenceEquals(_catalog.Drawers[0], rack) || !CanClaim
            || !MapRoomDriver.Active || StoryComposite.PointOfNoReturn) return false;
        return TownMerchantControlSync.RequestPage(direction);
    }
    internal static bool TryBeginCrank(TownServiceMerchantDrawer rack)
    { return _catalog != null && ReferenceEquals(_catalog.Drawers[0], rack) && CanClaim
        && MapRoomDriver.Active && !StoryComposite.PointOfNoReturn && TownMerchantControlSync.RequestCrankGrab(); }
    internal static bool CanGrabCrank { get { return TownMerchantControlSync.CanGrabCrank; } }
    internal static bool IsLocalCrankOwner(int owner) { return TownMerchantControlSync.IsLocalCrankOwner(owner); }
    internal static void RequestCrankDrag(float leadAngle) { TownMerchantControlSync.RequestCrankDrag(leadAngle); }
    internal static bool RequestCrankRelease(float leadAngle) { return TownMerchantControlSync.RequestCrankRelease(leadAngle); }
    internal static void RequestCrankCancel() { TownMerchantControlSync.RequestCrankCancel(); }
    internal static bool CanBeginOriginalCrank
    { get { return _catalog != null && CanClaim && MapRoomDriver.Active
        && !StoryComposite.PointOfNoReturn && _catalog.Drawers[0].CanGrab; } }
    internal static bool ApplyOriginalCrankRelease(float leadAngle)
    { return _catalog != null && CanClaim && MapRoomDriver.Active && !StoryComposite.PointOfNoReturn
        && _catalog.Drawers[0].RequestTurn(1, leadAngle); }
    internal static void ApplySharedCrank(int owner, float leadAngle) { _catalog?.Drawers[0].FollowCrank(owner, leadAngle); }
    internal static bool ApplyOriginalControl(TownMerchantControlOperation operation, int value)
    {
        if (_catalog == null || !CanClaim || !MapRoomDriver.Active || StoryComposite.PointOfNoReturn) return false;
        TownServiceMerchantDrawer rack = _catalog.Drawers[0];
        return operation == TownMerchantControlOperation.Category ? rack.Select(value, false)
            : operation == TownMerchantControlOperation.Page && rack.RequestTurn(value);
    }
    internal static TownRackState? ControlClock
    {
        get
        {
            if (_catalog == null || !MapRoomDriver.Active || StoryComposite.PointOfNoReturn) return null;
            TownServiceMerchantDrawer rack = _catalog.Drawers[0];
            return new TownRackState { Cassette = true, Turn = rack.TurnEpoch,
                Elapsed = rack.TurnElapsed, LeadAngle = rack.LeadAngle,
                Page = (ushort)rack.Page, From = (ushort)rack.FromPage, To = (ushort)rack.ToPage,
                ScrollDirection = rack.ScrollDirection, PageCount = (ushort)rack.PageCount };
        }
    }
    internal static void ApplySharedControlClock(TownRackState clock)
    {
        if (_catalog != null && !TownServiceMirror.IsPublicAuthor) _catalog.Drawers[0].Follow(clock);
    }
    // Artwork completeness gates the atomic picture replacement, not the public
    // mechanism or its local input proxy. One still-cold original image must not
    // strand the other player's category/page or make its next press start from
    // an obsolete local page.
    private static void FollowPublicRack()
    {
        if (_catalog == null || TownServiceMirror.PublicRack is not TownRackState state) return;
        if (state.Layout != null) _catalog.AdoptStockLayout(state.Layout);
        _catalog.Drawers[0].Follow(TownMerchantControlSync.SharedClock ?? state);
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
                _catalog.PrepareOriginalBankForLoading();
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
