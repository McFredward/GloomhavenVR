using System;
using UnityEngine;
using GloomhavenVR.Net.TownServices;

// The populated mirror test uses production Claim/CommitPublicVisibility/Follow.
// Native catalogue construction, game unlocks and current map/story state below
// are boundaries. ObserverRoots are the actual original page transforms also
// captured and received by the production mirror; visibility is observable on
// their real Unity renderers/canvases, rather than a mocked ready flag.
namespace GloomhavenVR.WorldUI
{
    internal static partial class TownServicePublicMerchant
    {
        private static TownServiceCatalog? _catalog => Catalog;
        private static bool _observingPublic;
        internal static bool FixtureObserving => _observingPublic;
        internal static void FixtureCommitVisibility() => CommitPublicVisibility();
        internal static void FixtureResetVisibility() => _observingPublic = false;
    }
    internal static class StoryComposite { internal static bool PointOfNoReturn; }
    internal static class TownServiceAvailability
    { internal static bool Unlocked = true; internal static bool NativeUnlocked(byte service) => Unlocked; }
    internal sealed partial class TownServiceCatalog
    {
        internal Transform? ObserverRoot;
        internal bool Observing;
        internal int ObserverChanges;
        internal void AdoptStockLayout(TownCatalogSlot[] layout) => StockLayout = (TownCatalogSlot[])layout.Clone();
        internal void SetObserver(bool value)
        {
            if (Observing != value) ObserverChanges++;
            Observing = value;
            if (ObserverRoot == null) return;
            foreach (Canvas canvas in ObserverRoot.GetComponentsInChildren<Canvas>(true)) canvas.enabled = !value;
            foreach (Renderer renderer in ObserverRoot.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = value;
        }
    }
    internal sealed partial class TownServiceMerchantDrawer
    {
        internal object? _hand;
        private readonly Func<bool> _mayClose = () => true;
        private TownServiceCabinetAudio? _followAudio;
        private TownServiceCabinetAudio _audio => _followAudio ??= new TownServiceCabinetAudio(HousingRoot);
        private float _clock, _leadAngle;
        private bool _turning, _swapped;
        internal float FixtureFollowClock => _clock;
        internal bool FixtureFollowingTurn => _turning;
        internal void FixtureDisposeFollower() { _followAudio?.Dispose(); _followAudio = null; }
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{ internal static class MapRoomDriver { internal static bool Active = true; } }
