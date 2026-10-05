using System;
using UnityEngine;
using TMPro;
using System.Collections.Generic;
using GloomhavenVR.Hands;
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
        internal static void FixtureFollowPublicRack() => FollowPublicRack();
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
        internal VRHand? _hand;
        private readonly Func<bool> _mayClose = () => true;
        private TownServiceCabinetAudio? _followAudio;
        private TownServiceCabinetAudio _audio => _followAudio ??= new TownServiceCabinetAudio(HousingRoot);
        private float _clock, _leadAngle;
        private bool _turning, _swapped;
        private bool _disposed, _laser;
        private Vector3 _cursorStart;
        private float _pull, _lastVisibility = float.NaN;
        private int _availablePages = 2;
        private readonly Func<bool> _alive = () => true;
        private readonly Action<TownServiceMerchantDrawer> _opening = _ => { };
        private readonly List<Material> _visibilityMaterials = new();
        private BoxCollider _pick = null!;
        private TMP_Text _pageLabel = null!;
        private CanvasGroup _pageLabelGate = null!;
        private int _indicatorPage = -1, _indicatorCount;
        internal int Category => Page % 2048 / 256;
        internal bool Accessible => !_turning;
        internal float FixtureFollowClock => _clock;
        internal bool FixtureFollowingTurn => _turning;
        internal void FixturePrepareNavigation()
        {
            _pick = Root.gameObject.AddComponent<BoxCollider>();
            var label = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(CanvasGroup));
            label.transform.SetParent(HousingRoot, false);
            _pageLabel = label.GetComponent<TMP_Text>(); _pageLabelGate = label.GetComponent<CanvasGroup>();
        }
        internal void FixtureTick(float alpha = 1f)
        { Tick(alpha); TurnElapsed = Mathf.Clamp(_clock, 0f, TownRackState.TurnDuration); Moving = _turning || _hand != null; }
        internal void FixtureDisposeFollower() { _followAudio?.Dispose(); _followAudio = null; }
    }
    internal sealed partial class TownServiceCatalogCategory
    {
        private TownServiceMerchantDrawer _rack = null!;
        private Func<bool> _available = null!;
        private int _category;
        private float _lastPressed = float.NegativeInfinity, _pressed, _lastVisibility = float.NaN;
        private Vector3 _home;
        private BoxCollider _shape = null!;
        private readonly List<Material> _visibilityMaterials = new();
        internal void FixturePrepareNavigation(TownServiceMerchantDrawer rack, int category)
        {
            _rack = rack; _category = category; _home = Root.localPosition;
            _shape = Root.gameObject.AddComponent<BoxCollider>();
            _available = () => TownServicePublicMerchant.CanClaim;
        }
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{ internal static class MapRoomDriver { internal static bool Active = true; } }

// Native network identity/coordinator readiness are explicit ports. Public input,
// serialization, host deduplication and original drawer execution are bound from
// TownMerchantControlSync/Codec.cs, rather than a fixture model of shared control.
namespace FFSNet
{
    internal static class FFSNetwork { internal static bool IsOnline; }
    internal static class PlayerRegistry { internal static int HostPlayerID = 1; }
}
namespace GloomhavenVR.Net.TownServices
{ internal static class TownServiceGrantSync { internal static bool CoordinatorReady = true; } }
