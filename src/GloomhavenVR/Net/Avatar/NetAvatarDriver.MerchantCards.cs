using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    private readonly HashSet<int> _readyMerchantItems = new();
    private readonly List<int> _merchantPreparation = new();
    private int _merchantPreparationSeat;
    private float _nextMerchantPreparation;
    private static readonly List<int> LoadingMerchantItems = new();
    private static float _nextLoadingMerchantCensus;
    private static ObjectPool? _loadingMerchantPool;
    private static bool _loadingMerchantReady;

    // Call from native map/town preparation before the loading screen releases. This does
    // not require MapRoomDriver.Active. False retains the prerequisite until original art
    // and the ordinary inactive native item pool/prefab are ready.
    internal static bool PrepareMerchantCardsForLoading()
    {
        if (UIInfoTools.Instance == null || ObjectPool.instance == null) return false;
        // PrepareCore also runs during ordinary play. Do not revisit every item/pool
        // on every sync tick after readiness. The bounded census still detects a
        // Clear on the same pool singleton; visible original borrows validate directly.
        if (_loadingMerchantPool == ObjectPool.instance && Time.unscaledTime < _nextLoadingMerchantCensus)
            return _loadingMerchantReady;
        _loadingMerchantPool = ObjectPool.instance;
        _nextLoadingMerchantCensus = Time.unscaledTime + .25f;
        WorldUI.MapRoom.MapRoomHand.CollectMerchantPreparationItems(LoadingMerchantItems);
        bool ready = true;
        foreach (int id in LoadingMerchantItems) ready &= RemoteItemCardSource.PrepareMapItemForLoading(id);
        return _loadingMerchantReady = ready;
    }
    private void PrepareMerchantCards()
    {
        bool remoteSource = false;
        foreach (RemoteAvatar avatar in _avatars.Values)
            if (avatar.TimeSinceUpdate <= NetProtocol.StaleTimeoutSeconds
                && (avatar.ItemCardCount > 0 || avatar.HeldTownItemSource(1).HasValue || avatar.HeldTownItemSource(2).HasValue)) { remoteSource = true; break; }
        if (!WorldUI.MapRoom.MapRoomDriver.Active || !WorldUI.WorldUIConfig.ImmersiveTownServices.Value && !remoteSource && !WorldUI.TownServicePopulation.HasRemoteVisitors)
        { _readyMerchantItems.Clear(); _merchantPreparation.Clear(); _merchantPreparationSeat = 0; return; }
        // Visible immutable metadata always starts its artwork before background party pins.
        for (int slot = 1; slot <= 2; slot++)
        {
            TownItemHeldSource? local = LocalRigSampler.SampleHeldTownItem(slot);
            if (local.HasValue) RemoteItemCardSource.PrepareMapItem(local.Value.ItemId);
            foreach (RemoteAvatar avatar in _avatars.Values)
            {
                TownItemHeldSource? remote = avatar.HeldTownItemSource(slot);
                if (remote.HasValue && avatar.TimeSinceUpdate <= NetProtocol.StaleTimeoutSeconds)
                    RemoteItemCardSource.PrepareMapItem(remote.Value.ItemId);
            }
        }
        if (_merchantPreparationSeat >= _merchantPreparation.Count && Time.unscaledTime >= _nextMerchantPreparation)
        {
            _nextMerchantPreparation = Time.unscaledTime + 1f;
            WorldUI.MapRoom.MapRoomHand.CollectMerchantPreparationItems(_merchantPreparation);
            _merchantPreparationSeat = 0;
        }
        for (int n = 0; n < 4 && _merchantPreparationSeat < _merchantPreparation.Count; n++)
        {
            int id = _merchantPreparation[_merchantPreparationSeat++];
            if (_readyMerchantItems.Contains(id)) continue;
            if (RemoteItemCardSource.PrepareMapItem(id)) _readyMerchantItems.Add(id);
        }
    }
}
