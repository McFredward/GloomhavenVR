using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
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
    private static Material? _matMaterial;
    private static Vector3 _origin;
    private static Quaternion _yaw;
    private static float _scale, _opened, _nextCensus;
    private static uint _session;
    internal static byte Service { get; private set; }
    internal static uint Session => _session;
    internal static IReadOnlyList<TownServiceSurface> LocalSurfaces => Surfaces;
    internal static Transform? ContextRoot => _context?.Target;
    internal static Transform? StationRoot => _station?.Root;
    internal static bool Active => _window != null && _window.IsOpen && _station != null;

    internal static void Tick()
    {
        try { TickCore(); }
        catch (Exception e)
        {
            _failedWindow = _window;
            Reset();
            VRLog.Warn("WorldUI", "TOWN SERVICE FALLBACK: original window restored after " + e);
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
                VRLog.Warn("WorldUI", "TOWN SERVICE ASSET MISSING: original service window remains available; install the matching asset bundle.");
                return;
            }
            Service = service; _session++; if (_session == 0) _session++;
            _context = context; _scale = scale; _origin = context.HostGo.transform.position;
            _yaw = Quaternion.Euler(0f, context.HostGo.transform.eulerAngles.y, 0f);
            _opened = Time.unscaledTime;
            BuildMat();
            BuildSections(window, service);
            HidePortrait(window);
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
        _mat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _mat.name = "GloomhavenVR.TownService.WorkMat";
        UnityEngine.Object.Destroy(_mat.GetComponent<Collider>());
        _mat.transform.position = _origin + _yaw * new Vector3(.12f, -.40f, -.20f) * _scale;
        _mat.transform.rotation = _yaw;
        _mat.transform.localScale = Vector3.one * _scale;
        // The visual is a child so the mat's coordinate frame remains in metres for drop tests.
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "OfferingMat";
        visual.transform.SetParent(_mat.transform, false);
        visual.transform.localScale = new Vector3(.44f, .016f, .32f);
        UnityEngine.Object.Destroy(visual.GetComponent<Collider>());
        UnityEngine.Object.Destroy(_mat.GetComponent<MeshRenderer>());
        UnityEngine.Object.Destroy(_mat.GetComponent<MeshFilter>());
        _matMaterial = WorldUIAssets.CreateFlatMaterial(new Color(.16f, .11f, .07f));
        visual.GetComponent<MeshRenderer>().sharedMaterial = _matMaterial;
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
                if (slot != null) Token(slot, slot.Selectable, () => slot.Item);
        }
        else if (Service == 2)
        {
            foreach (UITempleShopSlot slot in _window.GetComponent<UITempleWindow>().Shop.slots)
                if (slot != null) Token(slot, slot.button, () => slot.Blessing);
        }
        else
        {
            UINewEnhancementWindow shop = _window.GetComponent<UINewEnhancementWindow>();
            foreach (UINewEnhancementShopSlot slot in shop.enhancementShop.slotsPool)
                if (slot != null) Token(slot, slot.button, () => slot.enhancement);
            foreach (UIEnhanceCardSlot slot in shop.CardsDisplay.slotsPool)
                if (slot != null) Token(slot, slot.button, () => slot.abilityCard);
        }
    }

    private static void Token(Component source, Selectable? button, Func<object?> identity)
    {
        if (Tokens.ContainsKey(source) || button == null || source.transform is not RectTransform rect || _mat == null) return;
        uint session = _session;
        Tokens.Add(source, new TownServiceToken(rect, button, identity, () => Active && _session == session, _mat.transform));
    }

    internal static void LateTick()
    {
        foreach (TownServiceSurface surface in Surfaces) surface.LateTick();
    }

    internal static void Reset()
    {
        foreach (TownServiceToken token in Tokens.Values) token.Dispose();
        Tokens.Clear();
        for (int i = Surfaces.Count - 1; i >= 0; i--) Surfaces[i].Dispose();
        Surfaces.Clear();
        foreach (Graphic portrait in Portraits) if (portrait != null) portrait.enabled = true;
        Portraits.Clear();
        if (_mat != null) UnityEngine.Object.Destroy(_mat);
        if (_matMaterial != null) UnityEngine.Object.Destroy(_matMaterial);
        _mat = null; _matMaterial = null;
        _station?.Dispose(); _station = null;
        _window = null; _context = null; Service = 0;
    }
}
