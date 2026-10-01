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
    private static bool _failed;
    internal static TownServiceCatalog? Catalog => _catalog;
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
        TownRackState? state = TownServiceMirror.PublicRack;
        if (!TownServiceMirror.IsPublicAuthor && state != null) _catalog.Drawers[0].Follow(state);
        TownServiceMirror.ClaimPublicCatalog(); _catalog.SetObserver(false);
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
            }
            if (_station == null || _station.Root == null) { Reset(); return; }
            if (!TownServiceMirror.IsPublicAuthor && TownServiceMirror.PublicRack is TownRackState remote)
                _catalog.Drawers[0].Follow(remote);
            using (PerfMonitor.Scope("TownPublicStock.Catalog"))
            {
                _catalog.SetVisibility(Mathf.Clamp01((Time.unscaledTime - _opened) / .22f),
                    // Cabinet browsing stays available while another player owns a
                    // buy/sell decision. TownServiceMerchantHandoff.CanOffer keeps
                    // the separate physical transaction lease on card placement.
                    allowInput: !StoryComposite.PointOfNoReturn);
                _catalog.Tick(_station.Root.lossyScale.x);
            }
            bool observer = !TownServiceMirror.IsPublicAuthor;
            using (PerfMonitor.Scope("TownPublicStock.Observer"))
            {
                if (observer) foreach (TownServiceToken sample in _catalog.Samples) if (sample.IsMoving) sample.CancelInspection();
                _catalog.SetObserver(observer);
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
        TownServiceSync.ResetPublic(); _catalog?.Dispose(); _catalog = null; _station = null;
        if (_mount != null) UnityEngine.Object.Destroy(_mount);
        if (_mat != null) UnityEngine.Object.Destroy(_mat);
        _mount = _mat = null; _character = _context = null;
    }
}
