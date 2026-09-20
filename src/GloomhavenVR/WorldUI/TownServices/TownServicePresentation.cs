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
    private static TMP_Text? _caption;
    private static Vector3 _origin;
    private static Quaternion _yaw;
    private static float _scale, _opened, _nextCensus;
    private static uint _session;
    private static object? _selectionOwner, _selectionCard, _selectionKey;
    private static int _selectionMode;
    internal static byte Service { get; private set; }
    internal static uint Session => _session;
    internal static UIWindow? Window => _window;
    internal static Transform? WorkMat => _mat != null ? _mat.transform : null;
    internal static IReadOnlyCollection<TownServiceToken> Samples => Tokens.Values;
    internal static float SessionAge => Mathf.Max(0f, Time.unscaledTime - _opened);
    internal static IReadOnlyList<TownServiceSurface> LocalSurfaces => Surfaces;
    internal static Transform? ContextRoot => _context?.Target;
    internal static Transform? StationRoot => _station?.Root;
    internal static bool Active => _window != null && _window.IsOpen && _station != null;

    internal static void Tick()
    {
        try { TickCore(); }
        catch (Exception e)
        {
            if (_failedWindow == null) _failedWindow = _window;
            Reset();
            VRLog.Note("WorldUI", "TOWN SERVICE FALLBACK: original window restored after " + e);
        }
    }

    private static void TickCore()
    {
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
            _station = TownServiceStation.Create(service, center, scale);
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
            BuildMat();
            try
            {
                BuildSections(window, service);
                HidePortrait(window);
                _context = ModalFallback.RestoreTownServiceContext(window, _origin, _yaw)
                    ?? throw new InvalidOperationException("Native context could not be restored after section handoff");
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
        foreach (TownServiceSurface surface in Surfaces) surface.Tick(_origin, _yaw, _scale);
        if (Time.unscaledTime >= _nextCensus)
        {
            _nextCensus = Time.unscaledTime + .25f;
            RefreshTokens();
        }
        foreach (TownServiceToken token in Tokens.Values) token.Tick(_scale);
        float elapsed = Time.unscaledTime - _opened;
        _station?.Sample(elapsed < 1.2f ? "Greeting" : "Idle", elapsed < 1.2f ? elapsed : elapsed - 1.2f);
    }

    private static ConvertedPanel? FindContext(UIWindow window)
    {
        foreach (ConvertedPanel panel in CanvasConversion.ActivePanels)
            if (panel.Target != null && (panel.Target == window.transform || window.transform.IsChildOf(panel.Target))) return panel;
        return null;
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
        GameObject? prefab = TownServiceAssets.Prefab("townworktray");
        if (prefab == null) throw new InvalidOperationException("Town work tray is missing from the asset bundle");
        _mat = UnityEngine.Object.Instantiate(prefab);
        _mat.name = "GloomhavenVR.TownService.WorkMat";
        _mat.transform.position = _origin + _yaw * new Vector3(.12f, -.40f, -.20f) * _scale;
        _mat.transform.rotation = _yaw;
        _mat.transform.localScale = Vector3.one * _scale;
        TMP_Text? nativeText = _window != null ? _window.GetComponentInChildren<TMP_Text>(true) : null;
        if (nativeText != null && nativeText.font != null)
        {
            var label = new GameObject("Instructions", typeof(RectTransform));
            label.transform.SetParent(_mat.transform, false);
            label.transform.localPosition = new Vector3(0f, .025f, -.085f);
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            label.transform.localScale = Vector3.one * .01f;
            _caption = label.AddComponent<TextMeshPro>();
            _caption.font = nativeText.font;
            _caption.fontSharedMaterial = nativeText.font.material;
            _caption.rectTransform.sizeDelta = new Vector2(40f, 9f);
            _caption.alignment = TextAlignmentOptions.Center;
            _caption.fontSize = 24f; _caption.enableAutoSizing = true;
            _caption.fontSizeMin = 16f; _caption.fontSizeMax = 24f;
            _caption.color = new Color(.95f, .86f, .68f);
            RefreshCaption();
            Loc.OnChanged += RefreshCaption;
        }
    }

    private static void RefreshCaption() { if (_caption != null) _caption.text = Loc.Mod("town_sample_hint"); }

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
            owner = shop.character; mode = (int)shop.mode;
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
    }

    internal static void Reset()
    {
        if (_window != null) _window.onHidden.RemoveListener(OnNativeHidden);
        foreach (TownServiceToken token in Tokens.Values) token.Dispose();
        Tokens.Clear();
        // Reverse the ownership handoff too: the context must not re-adopt restored descendants
        // halfway through their teardown or record another conversion's camera/layer as native.
        if (_window != null && _context != null && _context.IsAlive)
            ModalFallback.ReleaseForComposite(_window);
        for (int i = Surfaces.Count - 1; i >= 0; i--) Surfaces[i].Dispose();
        Surfaces.Clear();
        foreach (Graphic portrait in Portraits) if (portrait != null) portrait.enabled = true;
        Portraits.Clear();
        if (_mat != null) UnityEngine.Object.Destroy(_mat);
        _mat = null;
        Loc.OnChanged -= RefreshCaption; _caption = null;
        _station?.Dispose(); _station = null;
        _window = null; _context = null; Service = 0;
        _selectionOwner = null; _selectionCard = null; _selectionKey = null;
    }
}
