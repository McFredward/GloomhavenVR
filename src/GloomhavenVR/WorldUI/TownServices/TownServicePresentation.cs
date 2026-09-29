using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
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
    private static TownServiceWindowMask? _contextMask, _enhancementListMask;
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
    internal static IReadOnlyList<TownServiceWorkspace.Prop>? WorkspaceProps => _workspace?.Props;
    internal static TownServiceRitual? Ritual => _ritual;
    internal static float SessionAge => Mathf.Max(0f, Time.unscaledTime - _opened);
    internal static IReadOnlyList<TownServiceSurface> LocalSurfaces => Surfaces;
    internal static Transform? ContextRoot => _context?.Target;
    internal static Transform? StationRoot => _station?.Root;
    internal static bool UsesImmersiveEnhancement => MapRoomDriver.Active && Service == 3 && Active
        && _enhancementListMask != null
        && GuildmasterDestinations.CurrentDestinationMode() == EGuildmasterMode.Enchantress;
    internal static bool Active => WorldUIConfig.ImmersiveTownServices.Value
        && TownServiceGrantSync.CanUseImmersive
        && TownServiceAvailability.NativeUnlocked(Service)
        && ((Service != 1 && Service != 3) || TownServiceEnhancementHandoff.Enabled)
        && _window != null && _window.IsOpen && _station != null;
    internal static bool OwnsInteraction => Active
        && TownServiceSync.LocalOwnsInteraction(Service, _session);
    internal static bool NativeFallbackFor(byte service)
    {
        if (_failedWindow == null || !_failedWindow.IsOpen || service < 1 || service > 3)
            return false;
        EGuildmasterMode mode = GuildmasterDestinations.CurrentDestinationMode();
        return TownServiceVisitTarget.ServiceOf(mode) == service
            && ReferenceEquals(_failedWindow, GuildmasterDestinations.ModeWindow(mode));
    }
    internal static bool OwnsGrab(GrabbableModal holder)
    {
        foreach (TownServiceSurface surface in Surfaces)
            if (surface.OwnsGrab(holder)) return true;
        return false;
    }

    // Ownership lasts until rollback, even if the option changed earlier in this frame.
    // ModalFallback runs before our Tick and must not adopt a half-restored controller.
    internal static bool OwnsWindow(UIWindow window) => TownServicePalmConfirmation.Owns(window)
        || TownServiceConfirmationMask.Owns(window)
        || TownServiceWindowMask.OwnsRetiring(window)
        || WantsNativeController(window)
        || (_catalog != null || _ritual != null || _contextMask != null) && _window != null
        && (window == _window || window.transform.IsChildOf(_window.transform));

    /// <summary>Claim an immersive service controller before the generic modal pass converts it.
    /// WorldUI runs the modal pass before this presentation tick, so waiting for <see cref="_window"/>
    /// made every approach build and tear down a complete converted window before the physical
    /// station could mask it. The claim is derived only from the game's current destination and
    /// exact controller instance; flat mode and the explicit hand fallback remain unchanged.</summary>
    private static bool WantsNativeController(UIWindow window)
    {
        if (window == null || !MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value)
            return false;
        if (!TownServiceGrantSync.CanUseImmersive) return false;
        EGuildmasterMode mode = GuildmasterDestinations.CurrentDestinationMode();
        if (mode != EGuildmasterMode.Merchant && mode != EGuildmasterMode.Temple
            && mode != EGuildmasterMode.Enchantress)
            return false;
        if (!TownServiceAvailability.NativeUnlocked(TownServiceVisitTarget.ServiceOf(mode)))
            return false;
        if ((mode == EGuildmasterMode.Merchant || mode == EGuildmasterMode.Enchantress)
            && !TownServiceEnhancementHandoff.Enabled)
            return false;
        UIWindow? controller = GuildmasterDestinations.ModeWindow(mode);
        if (controller == null) return false;
        // The same native controller is the explicit escape hatch for a failed town
        // presentation. It must be eligible for the ordinary VR-window converter again.
        if (ReferenceEquals(controller, _failedWindow)) return false;
        // The shop Scroll View is itself a UIWindow. The generic modal pass sees it before this
        // presentation tick and used to detach it as a second flat panel behind the merchant.
        // It is presentation owned by the same native controller, not another dialog.
        return ReferenceEquals(controller, window)
            || mode == EGuildmasterMode.Merchant
            && window.transform.IsChildOf(controller.transform);
    }

    internal static void Tick()
    {
        try
        {
            TownServiceNativeAudioSilence.EnsureInstalled();
            TownServiceWindowMask.TickRetirements();
            TownServiceConfirmationMask.Tick();
            using (PerfMonitor.Scope("TownServicePresentation.Visit")) TickCore();
            using (PerfMonitor.Scope("TownServicePresentation.PublicStock")) TownServicePublicMerchant.Tick();
        }
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
        if (!WorldUIConfig.ImmersiveTownServices.Value || !TownServiceGrantSync.CanUseImmersive)
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
        TownServiceEnhancementHandoff.TickApproach();
        // The temple needs its own approach tick to open the native offering and purse.
        TownServiceTempleOffering.TickApproach();
        EGuildmasterMode mode = GuildmasterDestinations.CurrentDestinationMode();
        byte service = mode == EGuildmasterMode.Merchant ? (byte)1 : mode == EGuildmasterMode.Temple ? (byte)2
            : mode == EGuildmasterMode.Enchantress ? (byte)3 : (byte)0;
        UIWindow? window = service != 0 ? GuildmasterDestinations.ModeWindow(mode) : null;
        if (service != 0 && !TownServiceAvailability.NativeUnlocked(service))
        {
            // An old modal may still be retiring when a save or FTUE step changes. Do not
            // present an inaccessible resident or capture a controller whose native mode is
            // locked. The original window remains the fallback if it is already open.
            UIWindow? restore = _window;
            Reset(); _failedWindow = null;
            if (restore != null && restore.IsOpen) ModalFallback.RestoreClassicTownService(restore);
            return;
        }
        if ((service == 1 || service == 3) && !TownServiceEnhancementHandoff.Enabled)
        {
            // Respect the explicit map-hand preference. Without real hand cards the native
            // merchant/enhancement window is the complete interaction path; residents stay visible.
            UIWindow? restore = _window;
            Reset(); _failedWindow = null;
            if (restore != null && restore.IsOpen) ModalFallback.RestoreClassicTownService(restore);
            return;
        }
        if (!MapRoomDriver.Active || window == null || !window.IsOpen)
        {
            Reset();
            if (_failedWindow != null) _failedWindow.onHidden.RemoveListener(OnFallbackHidden);
            _failedWindow = null; return;
        }
        if (_window != null && !ReferenceEquals(_window, window)) Reset();
        if (_failedWindow == window) return;
        if (_window == null)
        {
            // Preclaim handles the normal ordering. Also retire a descendant which an earlier
            // modal pass already detached before the destination/controller identity settled.
            // This only releases presentation ownership; native shop callbacks keep running.
            if (service == 1 && !ModalFallback.ReleaseTownServiceAuxiliaries(window)) return;
            ConvertedPanel? context = FindContext(window);
            if (context != null && !context.IsAlive) context = null;
            if (!MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale)) return;
            _window = window;
            _station = TownServicePopulation.Acquire(service);
            if (_station == null)
            {
                _window = null;
                // A user can reach the map while Unity is still preloading the
                // optional town bundle. Keep the native service usable during that
                // interval, but do not latch this window as permanently failed:
                // the same open window must convert once the preload completes.
                if (!TownServiceAssets.IsLoading)
                {
                    _failedWindow = window;
                    VRLog.Note("WorldUI", "TOWN SERVICE ASSET MISSING: original service window remains available; install the matching asset bundle.");
                }
                return;
            }
            // Service entry swaps the ordinary map hand for a physical offering. Suppress
            // exactly that automatic edge once, after the station is actually ready:
            // retries during async art loading must not continually extend the mute and
            // swallow a real manual fan gesture.
            CardsDriver.SuppressNextOffScenarioFanEdgeSound(open: true, seconds: .35f);
            CardsDriver.SuppressNextOffScenarioFanEdgeSound(open: false, seconds: .35f);
            Service = service; _session++; if (_session == 0) _session++;
            _context = context; _scale = scale;
            _origin = context != null ? context.HostGo.transform.position : center;
            _yaw = Quaternion.Euler(0f,
                context != null ? context.HostGo.transform.eulerAngles.y : _station.Root.eulerAngles.y, 0f);
            _opened = Time.unscaledTime;
            window.onHidden.AddListener(OnNativeHidden);
            if (context != null && !ModalFallback.ReleaseForTownService(window, context))
                throw new InvalidOperationException("Previous service conversion has not restored its native hierarchy");
            // One permanent station belongs to each NPC, regardless of how many players
            // browse it. The former visitor workspace cloned the complete Shrine or
            // Workbench for every non-primary roster slot and published that furniture
            // to everyone: two temple visitors therefore saw a fourth table. The
            // transaction coordinator still admits independent visitors and serializes
            // only an actual card/purse offer; it never needs a second stand.
            _workspace = null;
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
                    _ritual = new TownServiceRitual(window, service, _station.Root,
                        () => Active && _session == session, SelectionContext);
                    if (service == 3)
                    {
                        // The flat card chooser is a sibling of the character column. Keep its
                        // native pool/controllers alive for selection, but remove its presentation
                        // from that panel. Capacity has already moved to the station folio.
                        EnchantressComposite.Reset();
                        _enhancementListMask = new TownServiceWindowMask(
                            (RectTransform)window.GetComponent<UINewEnhancementWindow>().CardsDisplay.transform);
                        _enhancementListMask.DetachFromPanel(_station.Root);
                    }
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
        // A visitor does not reserve a resident by walking up to the stand or
        // opening its original window. Publish the physical handoff before the
        // local input gate samples ownership, so separate NPCs remain independently
        // usable and a second visitor is blocked only during a parked transaction.
        bool parkedOffer = Service switch
        {
            1 => TownServiceMerchantHandoff.HasParkedOffer,
            2 => _ritual?.HasParkedTempleOffer == true,
            3 => _ritual?.Handoff?.Card != null,
            _ => false
        };
        TownServiceMirror.SetLocalTransactionActive(Service, parkedOffer);
        bool ownsInteraction = TownServiceSync.LocalOwnsInteraction(Service, _session);
        float localVisibility = ownsInteraction ? visibility : 0f;
        // Once carried, the tray keeps the player's chosen placement. A participant joining
        // or leaving may rearrange counter workspaces, but must not pull a held tray away.
        if (_workspace != null)
        {
            if (_tray != null && _tray.IsGrabbed && _tray.Root.parent == _workspace.Root)
                _tray.Root.SetParent(null, true);
            _workspace.Tick(_catalog?.CanRelocate != false && _ritual?.CanRelocate != false);
            _workspace.SetVisibility(localVisibility);
        }
        float relocation = _workspace?.RelocationVisibility ?? 1f;
        bool allowInput = ownsInteraction && (_workspace?.InputAvailable ?? true);
        _catalog?.SetVisibility(localVisibility, relocation, allowInput);
        _ritual?.SetVisibility(localVisibility * relocation, allowInput);
        foreach (TownServiceSurface surface in Surfaces)
        {
            if (_catalog != null) surface.SetVisibility(localVisibility * relocation, allowInput);
            surface.Tick(_origin, _yaw, _scale);
        }
        _catalog?.Tick(_scale);
        _ritual?.Tick(_scale);
        bool enhancementStalled = _ritual?.Handoff?.NativeOfferStalled == true;
        bool templeStalled = _ritual?.TempleGrantStalled == true;
        bool grantUnavailable = TownServiceMirror.LocalTransactionUnavailable(Service);
        if (grantUnavailable && Service == 1)
        {
            // A lost coordinator reply used to restore the original shop window after
            // three seconds. The flat shop appeared behind the resident and retiring
            // the physical offer removed the card from his palm. Cancel that one
            // uncommitted offer instead; the masked native shop remains available to
            // initialize the next attempt when the coordinator responds again.
            TownServiceMerchantHandoff.AbortUnavailable();
            TownServiceMirror.SetLocalTransactionActive(Service, false);
            return;
        }
        if (enhancementStalled || templeStalled || grantUnavailable)
        {
            // Preserve the original window and its native callbacks. A permanently blocked
            // immersive offer becomes an ordinary usable VR window for this opening only;
            // closing/reopening it clears the failure latch and permits a fresh attempt.
            RestoreNativeForOpening(enhancementStalled ? "Enhancement offer unavailable"
                : templeStalled ? "Temple donation unavailable"
                : "Town transaction coordinator unavailable");
            return;
        }
        if (_ritual?.TempleDonationAvailabilityKnown == true)
            TownServiceMirror.SetLocalTempleDonationAvailable(_ritual.TempleDonationAvailable);
        if (Time.unscaledTime >= _nextCensus)
        {
            _nextCensus = Time.unscaledTime + .25f;
            if (Service != 1 && _catalog == null && _ritual == null) RefreshTokens();
        }
        _tray?.Tick();
        _tray?.SetVisibility(localVisibility);
        foreach (TownServiceToken token in Tokens.Values) token.Tick(_scale);
    }

    private static void RestoreNativeForOpening(string reason)
    {
        UIWindow? original = _window;
        if (original == null || !original.IsOpen) return;
        Vector3 originalPosition = _origin;
        Quaternion originalYaw = _yaw;
        byte service = Service;
        if (_failedWindow != null) _failedWindow.onHidden.RemoveListener(OnFallbackHidden);
        _failedWindow = original;
        // The independent merchant hand must release its parked item before the
        // native shop becomes a conventional VR window again. Otherwise its hidden
        // card would remain in front of the restored original confirmation.
        if (service == 1) TownServiceMerchantHandoff.Reset();
        Reset();
        original.onHidden.AddListener(OnFallbackHidden);
        ConvertedPanel? restored = ModalFallback.RestoreTownServiceContext(original,
            originalPosition, originalYaw);
        if (restored != null)
            VRLog.Note("TownServices", reason + "; restored the original VR window for this visit.");
        else
            VRLog.Warn("TownServices", reason
                + "; original window returned to the ordinary VR conversion path but conversion is still pending.");
    }

    private static ConvertedPanel? FindContext(UIWindow window)
    {
        foreach (ConvertedPanel panel in CanvasConversion.ActivePanels)
            if (panel.Target != null && (panel.Target == window.transform || window.transform.IsChildOf(panel.Target))) return panel;
        return null;
    }

    private static void BuildMerchant(UIShopItemWindow shop)
    {
        // Public stock has its own lifetime and publication lane. Opening the native shop
        // only establishes permission/confirmation context; it never builds a second cabinet.
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
        _mat.transform.SetParent(_station.Root, false);
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

    private static void OnFallbackHidden()
    {
        if (_failedWindow != null) _failedWindow.onHidden.RemoveListener(OnFallbackHidden);
        _failedWindow = null;
    }

    internal static void LateTick()
    {
        TownServiceMerchantHandoff.LateTick(); // Final palm pose before either publication lane.
        _ritual?.Handoff?.LateTick();
        TownServicePalmConfirmation.Tick();
        TownServicePalmConfirmation.LateTick();
        TownServicePublicMerchant.LateTick();
        foreach (TownServiceSurface surface in Surfaces) surface.LateTick();
        _catalog?.LateTick();
        _tray?.LateTick();
        Transform? frame = TownServicePopulation.Frame;
        if (frame != null) TownServiceSync.Tick(frame, StationRoot);
    }

    internal static void Reset()
    {
        if (_window == null && Surfaces.Count == 0 && Tokens.Count == 0 && _mat == null) return;
        CardsDriver.SuppressNextOffScenarioFanEdgeSound(open: true);
        CardsDriver.SuppressNextOffScenarioFanEdgeSound(open: false);
        TownServicePalmConfirmation.Clear();
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
        _enhancementListMask?.Dispose(); _enhancementListMask = null;
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
