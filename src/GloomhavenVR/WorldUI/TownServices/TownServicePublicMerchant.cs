using System;
using GloomhavenVR.Core;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI.MapRoom;
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
    internal static TownServiceCatalog? Catalog => _catalog;
    internal static bool CanClaim
    {
        get
        {
            if (TownServiceMirror.IsPublicAuthor) return true;
            TownRackState? state = TownServiceMirror.PublicRack;
            if (state != null) foreach (TownRackMember member in state.Members) if (member.Detached) return false;
            return true;
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
        if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value)
        { Reset(); return; }
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
            }
            if (_station == null || _station.Root == null) { Reset(); return; }
            if (!TownServiceMirror.IsPublicAuthor && TownServiceMirror.PublicRack is TownRackState remote)
                _catalog.Drawers[0].Follow(remote);
            _catalog.SetVisibility(Mathf.Clamp01((Time.unscaledTime - _opened) / .22f),
                allowInput: !StoryComposite.PointOfNoReturn);
            _catalog.Tick(_station.Root.lossyScale.x);
            bool observer = !TownServiceMirror.IsPublicAuthor;
            if (observer) foreach (TownServiceToken sample in _catalog.Samples) if (sample.IsHeld) sample.CancelInspection();
            _catalog.SetObserver(observer);
            TownServiceCatalogCategory.TickLaser();
        }
        catch (Exception error)
        {
            Reset(); _retryAt = Time.unscaledTime + 5f;
            VRLog.Note("TownServices", "Persistent merchant stock unavailable; native merchant remains usable: " + error.Message);
        }
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
        TownServiceSync.ResetPublic(); _catalog?.Dispose(); _catalog = null; _station = null;
        if (_mount != null) UnityEngine.Object.Destroy(_mount);
        if (_mat != null) UnityEngine.Object.Destroy(_mat);
        _mount = _mat = null; _character = _context = null;
    }
}
