using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Reversible presentation around the three original town controllers. Failures restore
/// the existing window; neither a station nor an animation can delay a native continuation.</summary>
internal static class TownServicePresentation
{
    private static readonly List<TownServiceSurface> Surfaces = new();
    private static readonly Dictionary<Component, TownServiceToken> Tokens = new();
    private static readonly List<Graphic> Portraits = new();
    private static readonly List<Component> DeadTokens = new();
    private static TownServiceStation? _station;
    private static UIWindow? _window, _failedWindow;
    private static ConvertedPanel? _context;
    private static GameObject? _mat;
    private static TownServiceTray? _tray;
    private static TownServiceCatalog? _catalog;
    private static TownServiceRitual? _ritual;
    private static TownServiceWorkspace? _workspace;
    private static Transform? _counter;
    private static TownServiceWindowMask? _contextMask;
    private static Vector3 _origin;
    private static Quaternion _yaw;
    private static float _scale, _opened, _nextCensus;
    private static uint _session;
    private static object? _selectionOwner, _selectionCard, _selectionKey;
    private static int _selectionMode;
    internal static byte Service { get; private set; }
    internal static uint Session => _session;
    internal static ulong RelocationRevision => _workspace?.RelocationRevision ?? 0;
    internal static float RelocationVisibility => _workspace?.RelocationVisibility ?? 1f;
    internal static UIWindow? Window => _window;
    internal static Transform? WorkMat => _mat != null ? _mat.transform : null;
    internal static TownServiceTray? Tray => _tray;
    internal static Transform? CounterFurniture => _workspace?.FurnitureRoot;
    internal static IReadOnlyCollection<TownServiceToken> Samples => _catalog != null ? _catalog.Samples
        : _ritual != null ? _ritual.Samples : Tokens.Values;
    internal static TownServiceCatalog? Catalog => _catalog;
    internal static TownServiceRitual? Ritual => _ritual;
    internal static float SessionAge => Mathf.Max(0f, Time.unscaledTime - _opened);
    internal static IReadOnlyList<TownServiceSurface> LocalSurfaces => Surfaces;
    internal static Transform? ContextRoot => _context?.Target;
    internal static Transform? StationRoot => _station?.Root;
    internal static bool Active => WorldUIConfig.ImmersiveTownServices.Value
        && _window != null && _window.IsOpen && _station != null;

    internal static bool OwnsGrab(GrabbableModal holder)
    {
        foreach (TownServiceSurface surface in Surfaces)
            if (surface.OwnsGrab(holder)) return true;
        return false;
    }

    // Ownership lasts until rollback, even if the option changed earlier in this frame.
    // ModalFallback runs before our Tick and must not adopt a half-restored controller.
    internal static bool OwnsWindow(UIWindow window) => (_catalog != null || _ritual != null) && _window != null
        && (window == _window || window.transform.IsChildOf(_window.transform));

    internal static void Tick()
    {
        try { TickCore(); }
        catch (Exception e)
        {
            UIWindow? restore = _window;
            if (_failedWindow == null) _failedWindow = _window;
            Reset();
            if (restore != null && restore.IsOpen)
                ModalFallback.RestoreTownServiceContext(restore, _origin, _yaw);
            VRLog.Note("WorldUI", "TOWN SERVICE FALLBACK: original window restored after " + e);
        }
    }

    private static void TickCore()
    {
        if (!WorldUIConfig.ImmersiveTownServices.Value)
        {
            // Cancel samples before restoring their source widgets. Never close/reopen the
            // native controller: its character, selection and pending confirmation stay intact.
            UIWindow? restore = _window;
            Reset();
            _failedWindow = null;
            if (restore != null && restore.IsOpen)
                ModalFallback.RestoreClassicTownService(restore);
            return;
        }
        EGuildmasterMode mode = GuildmasterDestinations.CurrentDestinationMode();
        byte service = mode == EGuildmasterMode.Merchant ? (byte)1 : mode == EGuildmasterMode.Temple ? (byte)2
            : mode == EGuildmasterMode.Enchantress ? (byte)3 : (byte)0;
        UIWindow? window = service != 0 ? GuildmasterDestinations.ModeWindow(mode) : null;
        if (!MapRoomDriver.Active || window == null || !window.IsOpen)
        { Reset(); _failedWindow = null; return; }
        if (_window != null && !ReferenceEquals(_window, window)) Reset();
        if (_failedWindow == window) return;
        if (_window == null)
        {
            ConvertedPanel? context = FindContext(window);
            if (context == null || !context.IsAlive || !MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale)) return;
            _window = window;
            _station = TownServicePopulation.Acquire(service);
            if (_station == null)
            {
                _failedWindow = window; _window = null;
                VRLog.Note("WorldUI", "TOWN SERVICE ASSET MISSING: original service window remains available; install the matching asset bundle.");
                return;
            }
            Service = service; _session++; if (_session == 0) _session++;
            _context = context; _scale = scale; _origin = context.HostGo.transform.position;
            _yaw = Quaternion.Euler(0f, context.HostGo.transform.eulerAngles.y, 0f);
            _opened = Time.unscaledTime;
            window.onHidden.AddListener(OnNativeHidden);
            if (!ModalFallback.ReleaseForTownService(window, context))
                throw new InvalidOperationException("Previous service conversion has not restored its native hierarchy");
            _workspace = new TownServiceWorkspace(_station.Root);
            BuildMat();
            try
            {
                if (service == 1)
                {
                    BuildMerchant(window.GetComponent<UIShopItemWindow>());
                    // Suppress obsolete illustration/list only after every usable control has
                    // its own counter owner. Native focus and permissions remain untouched.
                    _contextMask = new TownServiceWindowMask((RectTransform)window.transform);
                    _context = null;
                }
                else
                {
                    uint session = _session;
                    _ritual = new TownServiceRitual(window, service, _workspace.Root,
                        () => Active && _session == session, SelectionContext);
                    _contextMask = new TownServiceWindowMask((RectTransform)window.transform);
                    _context = null;
                }
            }
            catch
            {
                Reset();
                ModalFallback.RestoreTownServiceContext(window, _origin, _yaw);
                _failedWindow = window;
                throw;
            }
            VRLog.Note("WorldUI", "TOWN SERVICE OPEN: service=" + service + " session=" + _session + " native sections=" + Surfaces.Count);
        }
        float visibility = Mathf.Clamp01(SessionAge / .22f);
        // Once carried, the tray keeps the player's chosen placement. A participant joining
        // or leaving may rearrange counter workspaces, but must not pull a held tray away.
        if (_workspace != null)
        {
            if (_tray != null && _tray.IsGrabbed && _tray.Root.parent == _workspace.Root)
                _tray.Root.SetParent(null, true);
            _workspace.Tick(_catalog?.CanRelocate != false && _ritual?.CanRelocate != false);
            _workspace.SetVisibility(visibility);
        }
        float relocation = _workspace?.RelocationVisibility ?? 1f;
        bool allowInput = _workspace?.InputAvailable ?? true;
        _catalog?.SetVisibility(visibility, relocation, allowInput);
        foreach (TownServiceSurface surface in Surfaces)
        {
            if (_catalog != null) surface.SetVisibility(visibility * relocation, allowInput);
            surface.Tick(_origin, _yaw, _scale);
        }
        _catalog?.Tick(_scale);
        _ritual?.Tick(_scale);
        if (Time.unscaledTime >= _nextCensus)
        {
            _nextCensus = Time.unscaledTime + .25f;
            if (_catalog == null && _ritual == null) RefreshTokens();
        }
        _tray?.Tick();
        _tray?.SetVisibility(visibility);
        foreach (TownServiceToken token in Tokens.Values) token.Tick(_scale);
    }

    private static ConvertedPanel? FindContext(UIWindow window)
    {
        foreach (ConvertedPanel panel in CanvasConversion.ActivePanels)
            if (panel.Target != null && (panel.Target == window.transform || window.transform.IsChildOf(panel.Target))) return panel;
        return null;
    }

    private static void BuildMerchant(UIShopItemWindow shop)
    {
        if (_station == null || _mat == null) throw new InvalidOperationException("Merchant counter is unavailable");
        var anchor = new GameObject("GloomhavenVR.TownService.CounterCards");
        _counter = anchor.transform;
        _counter.SetParent(_workspace != null ? _workspace.Root : _station.Root, false);
        // Authored planks end at 0.955 m. Original filters, page arrows and exit sit above
        // that same worktop, around the bounded six-card rack.
        _counter.localPosition = new Vector3(0f, .970f, 0f);
        uint session = _session;
        _catalog = new TownServiceCatalog(shop.ItemInventory, _counter, SelectionContext,
            () => Active && _session == session, _mat.transform);

    }

    private static void BuildSections(UIWindow window, byte service)
    {
        if (service == 1)
        {
            UIShopItemWindow shop = window.GetComponent<UIShopItemWindow>();
            Add(10, shop.ItemInventory.transform, new Vector3(-.32f, -.04f, -.08f), .50f);
        }
        else if (service == 2)
        {
            UITempleWindow temple = window.GetComponent<UITempleWindow>();
            Add(10, temple.Shop.transform, new Vector3(-.30f, -.04f, -.08f), .50f);
        }
        else
        {
            UINewEnhancementWindow shop = window.GetComponent<UINewEnhancementWindow>();
            Add(10, shop.enhancementShop.transform, new Vector3(.42f, -.06f, -.08f), .34f);
            Add(11, shop.cardHolder.transform, new Vector3(0f, -.02f, -.10f), .34f);
            // EnchantressComposite retains ownership of CardsDisplay itself. Only its original
            // scroll section moves, avoiding two writers of the composite's parent/pose.
            Add(12, shop.CardsDisplay.abilityCardsPanel.transform, new Vector3(-.42f, -.06f, -.08f), .34f);
        }
    }

    private static void Add(ushort id, Transform source, Vector3 offset, float width)
    {
        if (source is not RectTransform rect) throw new InvalidOperationException("Native section is not a RectTransform");
        Surfaces.Add(new TownServiceSurface(id, rect, offset, width));
    }

    private static void BuildMat()
    {
        if (_station == null) throw new InvalidOperationException("Town station is unavailable");
        _mat = new GameObject("GloomhavenVR.TownService.InspectionFrame");
        _mat.transform.SetParent(_workspace != null ? _workspace.Root : _station.Root, false);
        _mat.transform.localPosition = new Vector3(0f, .970f, 0f);
    }

    private static void HidePortrait(UIWindow window)
    {
        foreach (Graphic graphic in window.GetComponentsInChildren<Graphic>(true))
        {
            Texture? texture = graphic.mainTexture;
            string name = texture != null ? texture.name : string.Empty;
            if (name != "GuildBackground_Merchant" && name != "Guild_Background_Temple" && name != "Guild_Background_Enchantress") continue;
            if (!graphic.enabled) continue;
            Portraits.Add(graphic); graphic.enabled = false;
        }
    }

    private static void RefreshTokens()
    {
        if (_window == null || _mat == null) return;
        DeadTokens.Clear();
        foreach (Component key in Tokens.Keys) if (key == null) DeadTokens.Add(key!);
        foreach (Component key in DeadTokens) { Tokens[key].Dispose(); Tokens.Remove(key); }
        if (Service == 1)
        {
            foreach (UIShopItemSlot slot in _window.GetComponent<UIShopItemWindow>().ItemInventory.slotPool)
                if (slot != null && !Tokens.ContainsKey(slot)) Token(slot, slot.Selectable, () => slot.Item);
        }
        else if (Service == 2)
        {
            foreach (UITempleShopSlot slot in _window.GetComponent<UITempleWindow>().Shop.slots)
                if (slot != null && !Tokens.ContainsKey(slot)) Token(slot, slot.button, () => slot.Blessing);
        }
        else
        {
            UINewEnhancementWindow shop = _window.GetComponent<UINewEnhancementWindow>();
            foreach (UINewEnhancementShopSlot slot in shop.enhancementShop.slotsPool)
                if (slot != null && !Tokens.ContainsKey(slot)) Token(slot, slot.button, () => slot.enhancement);
            foreach (UIEnhanceCardSlot slot in shop.CardsDisplay.slotsPool)
                if (slot != null && !Tokens.ContainsKey(slot)) Token(slot, slot.Selectable,
                    () => slot.AbilityCard != null ? slot.AbilityCard.AbilityCard : null);
        }
    }

    private static void Token(Component source, Selectable? button, Func<object?> identity)
    {
        if (Tokens.ContainsKey(source) || button == null || source.transform is not RectTransform rect || _mat == null) return;
        uint session = _session;
        Tokens.Add(source, new TownServiceToken(rect, button, identity, SelectionContext,
            () => Active && _session == session, _mat.transform));
    }

    private static object? SelectionContext()
    {
        if (_window == null) return null;
        object? owner, card = null;
        int mode = 0;
        if (Service == 1)
        {
            UIShopItemInventory shop = _window.GetComponent<UIShopItemWindow>().ItemInventory;
            // Buy and sell samples coexist. The hidden native tab changes during a guarded
            // drop transaction and is not an owner/selection change of this physical catalog.
            owner = shop.character;
        }
        else if (Service == 2) owner = _window.GetComponent<UITempleWindow>().character;
        else
        {
            UINewEnhancementWindow shop = _window.GetComponent<UINewEnhancementWindow>();
            owner = shop.character; mode = (int)shop.mode;
            card = shop.selectedCard != null ? shop.selectedCard.AbilityCard : null;
        }
        if (_selectionKey == null || !ReferenceEquals(owner, _selectionOwner)
            || !ReferenceEquals(card, _selectionCard) || mode != _selectionMode)
        {
            _selectionOwner = owner; _selectionCard = card; _selectionMode = mode;
            _selectionKey = new object();
        }
        return _selectionKey;
    }

    private static void OnNativeHidden()
    {
        // A close/reopen within one frame must still retire the old gesture/session.
        Reset();
        _failedWindow = null;
    }

    internal static void LateTick()
    {
        foreach (TownServiceSurface surface in Surfaces) surface.LateTick();
        _catalog?.LateTick();
        _tray?.LateTick();
        Transform? frame = TownServicePopulation.Frame;
        if (frame != null) TownServiceSync.Tick(frame, StationRoot);
    }

    internal static void Reset()
    {
        if (_window == null && Surfaces.Count == 0 && Tokens.Count == 0 && _mat == null) return;
        TownServiceSync.Reset();
        if (_window != null) _window.onHidden.RemoveListener(OnNativeHidden);
        foreach (TownServiceToken token in Tokens.Values) token.Dispose();
        Tokens.Clear();
        // Reverse the ownership handoff too: the context must not re-adopt restored descendants
        // halfway through their teardown or record another conversion's camera/layer as native.
        if (_window != null && _context != null && _context.IsAlive)
            ModalFallback.ReleaseForComposite(_window);
        _catalog?.Dispose(); _catalog = null;
        _ritual?.Dispose(); _ritual = null;
        for (int i = Surfaces.Count - 1; i >= 0; i--) Surfaces[i].Dispose();
        Surfaces.Clear();
        _contextMask?.Dispose(); _contextMask = null;
        if (_counter != null) UnityEngine.Object.Destroy(_counter.gameObject);
        _counter = null;
        foreach (Graphic portrait in Portraits) if (portrait != null) portrait.enabled = true;
        Portraits.Clear();
        if (_tray == null && _mat != null) UnityEngine.Object.Destroy(_mat);
        _tray?.Dispose(); _tray = null; _mat = null;
        _workspace?.Dispose(); _workspace = null;
        _station = null; // Population retains a station while another visitor still uses it.
        _window = null; _context = null; Service = 0;
        _selectionOwner = null; _selectionCard = null; _selectionKey = null;
    }
}
