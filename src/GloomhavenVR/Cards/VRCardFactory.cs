using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Creates and tracks <see cref="VRCard"/> objects; maps game widgets
/// (<c>AbilityCardUI</c>) 1:1 to VR cards. Loads the <c>CardBacking</c> mesh from
/// <c>gloomhavenvr.bundle</c> (asset contract:
/// unity/GloomhavenVR.Assets/Assets/Bundle/Table/README.md) with a procedural
/// fallback. All faces are restored to the game on <see cref="Clear"/> — wired to
/// scenario teardown, mode exits, and module shutdown (hot-reload clean).
/// </summary>
internal sealed class VRCardFactory
{
    private const string BundleFileName = "gloomhavenvr.bundle";
    private static readonly string[] BackingAssetPaths =
    {
        "Assets/Bundle/Table/CardBacking.prefab",
    };
    // Enum → bundle prefab path map for the switchable control board. Oak is the
    // original bundled board (default); Steel/Bronze are the two new boards added to
    // the bundle in parallel. GetTrayPrefab picks per CardsConfig.Board and falls back
    // to Oak (then the procedural board) when a selected prefab isn't in the bundle yet.
    private const string OakTrayPath = "Assets/Bundle/Table/PlayTray.prefab";
    private const string SteelTrayPath = "Assets/Bundle/Table/PlayTray_9capjqp6.prefab";
    private const string BronzeTrayPath = "Assets/Bundle/Table/PlayTray_16vm268h.prefab";

    private readonly Dictionary<AbilityCardUI, VRCard> _byWidget = new(16);
    private readonly List<VRCard> _all = new(16);

    private AssetBundle? _bundle;
    private bool _bundleOwned;
    private bool _bundleProbed;
    private GameObject? _backingPrefab;

    private Transform? _poolRoot;

    internal IReadOnlyList<VRCard> All => _all;

    internal Transform PoolRoot
    {
        get
        {
            if (_poolRoot == null)
            {
                var go = new GameObject("GloomhavenVR.Cards.Pool");
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.SetActive(false); // parked cards are invisible/inactive
                _poolRoot = go.transform;
            }
            return _poolRoot;
        }
    }

    // ------------------------------------------------------------------ cards --

    /// <summary>Get (or create) the VR card mirroring a game widget.</summary>
    internal VRCard GetOrCreate(AbilityCardUI widget)
    {
        if (_byWidget.TryGetValue(widget, out VRCard existing) && existing != null)
            return existing;

        VRCard card = CreateBlank();
        if (!card.AttachGameCard(widget))
            VRLog.Warn("Cards", $"Face adoption failed for {CardsGameApi.CardName(widget)} — backing only.");
        _byWidget[widget] = card;
        return card;
    }

    internal VRCard? Find(AbilityCardUI widget) =>
        _byWidget.TryGetValue(widget, out VRCard card) && card != null ? card : null;

    /// <summary>Create an unbound card (dev fake hand).</summary>
    internal VRCard CreateBlank()
    {
        var go = new GameObject("VRCard");
        go.transform.SetParent(PoolRoot, worldPositionStays: false);
        var card = go.AddComponent<VRCard>();
        card.Build(GetBackingPrefab());
        _all.Add(card);
        return card;
    }

    /// <summary>Restore + destroy the VR card for one widget (pool recycle guard).</summary>
    internal void ReleaseWidget(AbilityCardUI widget)
    {
        if (!_byWidget.TryGetValue(widget, out VRCard card))
            return;
        _byWidget.Remove(widget);
        if (card == null)
            return;
        card.DetachGameCard();
        _all.Remove(card);
        Object.Destroy(card.gameObject);
    }

    /// <summary>Restore faces of every card belonging to a dying hand.</summary>
    internal void ReleaseHand(CardsHandUI hand)
    {
        if (hand == null)
            return;
        List<AbilityCardUI> cards = hand.cardsUI;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                ReleaseWidget(cards[i]);
        }
    }

    /// <summary>Park a card invisibly without destroying it (mode exits).</summary>
    internal void Park(VRCard card)
    {
        card.Grabbable = false;
        card.SetHome(PoolRoot, Vector3.zero, Quaternion.identity, 1f, instant: true);
    }

    /// <summary>Restore all faces and destroy all VR cards (scenario end / shutdown).</summary>
    internal void Clear()
    {
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            VRCard card = _all[i];
            if (card != null)
            {
                card.DetachGameCard();
                Object.Destroy(card.gameObject);
            }
        }
        _all.Clear();
        _byWidget.Clear();
    }

    // ------------------------------------------------------------------ assets --

    internal GameObject? GetBackingPrefab()
    {
        if (_backingPrefab == null)
            _backingPrefab = LoadPrefab(BackingAssetPaths);
        return _backingPrefab;
    }

    /// <summary>
    /// Load the control-board prefab for the selected <see cref="ControlBoard"/>. Tries the
    /// selected board's bundle path first; if it isn't in the bundle yet, falls back to the
    /// Oak (original) path; if that also fails, returns null so <c>PlayTray.EnsureBuilt</c>'s
    /// procedural fallback board kicks in. NOTE (per-board tuning seam): SlotCardInset,
    /// SlotCardFill and RoundButtonDiameter (CardsConfig) are documented as per-board — for
    /// now they stay single global values; a future per-board descriptor keyed by
    /// <see cref="ControlBoard"/> may override them once the new boards' recess dimensions land.
    /// </summary>
    internal GameObject? GetTrayPrefab()
    {
        ControlBoard board = CardsConfig.Board.Value;
        string selectedPath = board switch
        {
            ControlBoard.Steel => SteelTrayPath,
            ControlBoard.Bronze => BronzeTrayPath,
            _ => OakTrayPath,
        };

        GameObject? prefab = LoadPrefab(new[] { selectedPath });
        if (prefab != null)
        {
            VRLog.Info("Cards", $"Control board '{board}' → '{selectedPath}' loaded from bundle.");
            return prefab;
        }

        if (selectedPath != OakTrayPath)
        {
            prefab = LoadPrefab(new[] { OakTrayPath });
            if (prefab != null)
            {
                VRLog.Info("Cards", $"Control board '{board}' ('{selectedPath}') not in bundle — " +
                                    $"fell back to Oak ('{OakTrayPath}').");
                return prefab;
            }
        }

        VRLog.Info("Cards", $"Control board '{board}' prefab unavailable in bundle — " +
                            "using the procedural fallback board.");
        return null;
    }

    private GameObject? LoadPrefab(string[] candidates)
    {
        AssetBundle? bundle = GetBundle();
        if (bundle == null)
            return null;
        foreach (string path in candidates)
        {
            var prefab = bundle.LoadAsset<GameObject>(path);
            if (prefab != null)
                return prefab;
        }
        return null;
    }

    /// <summary>
    /// The bundle may already be loaded by the Hands module (same file; Unity forbids
    /// loading a bundle twice) — reuse a loaded instance first, own it otherwise.
    /// </summary>
    private AssetBundle? GetBundle()
    {
        if (_bundleProbed)
            return _bundle;
        _bundleProbed = true;

        foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (loaded != null && loaded.name.Contains("gloomhavenvr"))
            {
                _bundle = loaded;
                _bundleOwned = false;
                return _bundle;
            }
        }

        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string bundlePath = Path.Combine(pluginDir, BundleFileName);
        if (!File.Exists(bundlePath))
        {
            VRLog.Info("Cards", $"Asset bundle not found ({bundlePath}) — procedural card/tray visuals active.");
            return null;
        }

        _bundle = AssetBundle.LoadFromFile(bundlePath);
        _bundleOwned = _bundle != null;
        if (_bundle == null)
            VRLog.Warn("Cards", $"AssetBundle.LoadFromFile failed for {bundlePath} — procedural visuals active.");
        return _bundle;
    }

    /// <summary>Shutdown: destroy pool root, release the bundle if we own it.</summary>
    internal void Dispose()
    {
        Clear();
        if (_poolRoot != null)
        {
            Object.Destroy(_poolRoot.gameObject);
            _poolRoot = null;
        }
        if (_bundle != null && _bundleOwned)
            _bundle.Unload(unloadAllLoadedObjects: false);
        _bundle = null;
        _backingPrefab = null;
        _bundleProbed = false;
        _bundleOwned = false;
    }
}
