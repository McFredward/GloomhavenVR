using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI.MapRoom;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Physical offerings and rune samples around the original service controller.
/// Inspection is free. A deliberate matching drop dispatches the original guarded selection
/// and its own confirmation; neither a clone nor an observer runs a gameplay callback.</summary>
internal sealed class TownServiceRitual : IDisposable
{
    internal sealed class Inscription : IDisposable
    {
        internal readonly string Key;
        internal readonly Transform Source;
        private readonly Transform _root;
        private readonly RemoteWidgetMirror _mirror;
        internal Transform? Content => _mirror.CloneOf(Source);
        internal Transform? CloneOf(Transform source) => _mirror.CloneOf(source);
        internal Inscription(string key, Component source, Transform parent, Vector3 position, float width, float height)
        {
            Key = key; Source = source.transform;
            _root = new GameObject("Town ledger inscription").transform;
            _root.SetParent(parent, false); _root.localPosition = position;
            _root.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _mirror = new RemoteWidgetMirror("TownLedger", _root, width, height, Vector2.zero, mrBacking: false);
            _mirror.Refresh(Source);
        }
        internal void Tick() => _mirror.TickLive();
        public void Dispose() { _mirror.Destroy(); UnityEngine.Object.Destroy(_root.gameObject); }
    }

    internal sealed class Piece : IDisposable
    {
        private readonly TownServiceRitual _owner;
        internal readonly Component Source;
        internal readonly string Key;
        internal readonly Transform Root;
        internal readonly TownServiceToken Token;
        internal readonly Transform Body;
        internal readonly string BodyKey;
        private readonly RemoteWidgetMirror _mirror;
        private RemoteWidgetMirror? _details;
        private Transform? _detailMount;
        private bool _hovering;
        private readonly Selectable _button;
        private float _detailRefresh;
        private readonly Func<object?> _identity;
        private readonly object? _createdIdentity;
        private readonly List<Graphic> _inscriptions;
        private readonly RectTransform _reach;
        private readonly bool _card;
        private Transform? _graphicRoot;
        private readonly List<Graphic> _graphics = new();
        private readonly List<Material> _coinMaterials = new();
        private float _refreshAt;
        internal Transform? Content => _mirror.CloneOf(Source.transform);
        internal Transform? CloneOf(Transform original) => _mirror.CloneOf(original);
        internal Transform? DetailSource => Source is UITempleShopSlot
            ? _owner._window.GetComponent<UITempleWindow>().Shop.tooltip.transform
            : Source is UINewEnhancementShopSlot ? _owner._window.GetComponent<UINewEnhancementWindow>().enhancementShop.tooltip.transform : null;
        internal string DetailKey => Source is UITempleShopSlot ? "temple.tooltip" : "enchant.tooltip";
        internal Transform? DetailContent => _hovering && DetailSource != null ? _details?.CloneOf(DetailSource) : null;
        internal Transform? DetailCloneOf(Transform source) => _hovering ? _details?.CloneOf(source) : null;
        internal bool Current => Source != null && Source.gameObject.activeInHierarchy
            && ReferenceEquals(_createdIdentity, _identity());

        internal Piece(TownServiceRitual owner, Component source, string key, Selectable button,
            Func<object?> identity, Func<bool> eligible, Func<bool> drop, Vector3 position,
            bool card, params Graphic[] inscriptions)
        {
            _owner = owner; Source = source; Key = key; _identity = identity; _button = button;
            _createdIdentity = identity(); _inscriptions = new List<Graphic>(inscriptions); _card = card;
            var root = new GameObject("GloomhavenVR.TownService." + key, typeof(RectTransform));
            Root = root.transform; Root.SetParent(owner.Root, false);
            Root.localPosition = position; Root.localRotation = Quaternion.Euler(65f, 0f, 0f);
            _reach = (RectTransform)Root;
            bool offering = source is UITempleShopSlot;
            _reach.sizeDelta = card ? new Vector2(.145f, .22f) : offering ? new Vector2(.075f, .075f) : new Vector2(.17f, .17f);
            if (offering)
            {
                Transform template = TownServiceDecor.CoinTemplate
                    ?? throw new InvalidOperationException("Original offering coin is still loading");
                Body = UnityEngine.Object.Instantiate(template.gameObject, Root, false).transform;
                Body.localPosition = Vector3.zero; Body.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                Body.localScale = template.localScale * 1.4f; Body.gameObject.SetActive(true);
                BodyKey = "ritual.coin";
                foreach (MeshRenderer renderer in Body.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    { materials[i] = new Material(materials[i]); _coinMaterials.Add(materials[i]); }
                    renderer.sharedMaterials = materials;
                }
            }
            else
            {
                Body = TownServiceCardBody.Create(Root).transform;
                Body.localScale = new Vector3(_reach.sizeDelta.x, _reach.sizeDelta.y, 1f);
                BodyKey = "merchant.cardbody";
            }
            var mount = new GameObject("Original inscriptions").transform;
            mount.SetParent(Root, false); mount.localPosition = new Vector3(0f, 0f, -.002f);
            _mirror = new RemoteWidgetMirror("TownRitual", mount, _reach.sizeDelta.x,
                _reach.sizeDelta.y, Vector2.zero, mrBacking: false);
            var rect = (RectTransform)source.transform;
            Vector2 frame = card ? rect.rect.size : Vector2.one * Mathf.Max(1f, rect.rect.width);
            _mirror.SetOwnerFrame(frame, rect.parent is RectTransform parent ? parent.rect.size : rect.rect.size);
            if (!_mirror.Refresh(source.transform)) throw new InvalidOperationException("Original ritual artwork is unavailable");
            Token = new TownServiceToken(_reach, button, identity, owner._context,
                () => owner._alive() && Current, owner.Root, Root, drop, eligible,
                owner._service == 2 ? new Vector3(0f, .08f, .26f) : Vector3.zero);
            VRLayers.Apply(root);
            ApplyInscriptions(); SetVisibility(owner._visibility);
        }

        internal void SetVisibility(float visibility)
        {
            if (BodyKey == "merchant.cardbody") TownServiceCardBody.SetVisibility(Body.gameObject, visibility);
            foreach (Material material in _coinMaterials) material.SetFloat("_TownVisibility", visibility);
        }

        internal void Tick(float scale)
        {
            if (!Current) return;
            if (Time.unscaledTime >= _refreshAt)
            { _refreshAt = Time.unscaledTime + .2f; _mirror.Refresh(Source.transform); }
            _mirror.TickLive(); ApplyInscriptions(); Token.Tick(scale);
            TickDetails();
        }

        private void TickDetails()
        {
            bool held = Token.HeldRoot != null && Current && DetailSource != null;
            if (_hovering != held && EventSystem.current != null)
            {
                _hovering = held;
                var pointer = new PointerEventData(EventSystem.current);
                if (held) ExecuteEvents.Execute(_button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                else ExecuteEvents.Execute(_button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            }
            if (!held) { _details?.SetShown(false); return; }
            if (_details == null)
            {
                _detailMount = new GameObject("Original offering description").transform;
                _detailMount.SetParent(Root, false); _detailMount.localPosition = new Vector3(.21f, 0f, 0f);
                _details = new RemoteWidgetMirror("RitualDetails", _detailMount, .22f, .22f, Vector2.zero, mrBacking: false);
            }
            if (Time.unscaledTime >= _detailRefresh)
            { _detailRefresh = Time.unscaledTime + .15f; _details.Refresh(DetailSource!); }
            _details.TickLive(); _details.SetShown(true);
        }

        private void ApplyInscriptions()
        {
            Transform? content = Content;
            if (content == null || _card) return;
            // Preserve native text, icons, price and warning state. Only replace the flat row's
            // geometry with inscriptions on the physical sample; no new gameplay explanation.
            if (_graphicRoot != content)
            { _graphicRoot = content; _graphics.Clear(); content.GetComponentsInChildren(true, _graphics); }
            foreach (Graphic graphic in _graphics) if (graphic != null) graphic.enabled = false;
            var original = (RectTransform)Source.transform;
            float width = Mathf.Max(original.rect.width, 1f), height = width;
            for (int i = 0; i < _inscriptions.Count; i++)
            {
                Graphic source = _inscriptions[i];
                if (source == null) continue;
                Transform? target = _mirror.CloneOf(source.transform);
                if (target is not RectTransform rect) continue;
                Graphic copy = rect.GetComponent<Graphic>();
                copy.enabled = source.enabled; copy.raycastTarget = false;
                // The first inscription is the native symbol, then name, price and point cost.
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one;
                rect.position = content.TransformPoint(new Vector3(0f, height * (.30f - i * .23f), -.1f));
                rect.sizeDelta = new Vector2(i == 0 && copy is not TMP_Text ? height * .35f : width * .9f, height * .23f);
                if (copy is TMP_Text text)
                { text.enableAutoSizing = true; text.fontSizeMin = height * .045f; text.fontSizeMax = height * .14f; text.alignment = TextAlignmentOptions.Center; }
            }
        }

        public void Dispose()
        {
            Token.Dispose();
            if (_hovering && Current && EventSystem.current != null)
                ExecuteEvents.Execute(_button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
            _details?.Destroy(); _mirror.Destroy();
            foreach (Material material in _coinMaterials) UnityEngine.Object.Destroy(material); _coinMaterials.Clear();
            if (BodyKey == "merchant.cardbody") TownServiceCardBody.Dispose(Body.gameObject);
            UnityEngine.Object.Destroy(Root.gameObject);
        }
    }

    private readonly UIWindow _window;
    private readonly byte _service;
    private readonly Func<bool> _alive;
    private readonly Func<object?> _context;
    private readonly Dictionary<Component, Piece> _pieces = new();
    private readonly List<TownServiceToken> _samples = new();
    private readonly List<Component> _retired = new();
    private readonly List<TownServiceSurface> _surfaces = new();
    private readonly List<Inscription> _inscriptions = new();
    private readonly GameObject _zone;
    private readonly CanvasGroup _zoneGate, _opening;
    private bool _allowInput = true;
    private float _visibility;
    private readonly TMP_Text _zoneLabel;
    private float _censusAt;
    private readonly float _started = Time.unscaledTime;
    private bool _disposed;
    internal Transform Root { get; }
    internal IEnumerable<Piece> Pieces => _pieces.Values;
    internal IReadOnlyCollection<TownServiceToken> Samples => _samples;
    internal IReadOnlyList<Inscription> Inscriptions => _inscriptions;
    internal Transform Zone => _zone.transform;
    internal IReadOnlyList<TownServiceSurface> Surfaces => _surfaces;
    internal bool CanRelocate
    { get { foreach (Piece piece in _pieces.Values) if (piece.Token.IsMoving) return false; return true; } }

    internal TownServiceRitual(UIWindow window, byte service, Transform station,
        Func<bool> alive, Func<object?> context)
    {
        _window = window; _service = service; _alive = () => alive() && _allowInput && _visibility >= .99f; _context = context;
        Root = new GameObject("GloomhavenVR.TownService.Ritual").transform;
        _opening = Root.gameObject.AddComponent<CanvasGroup>(); _opening.alpha = 0f;
        Root.SetParent(station, false); Root.localPosition = new Vector3(0f, .978f, -.08f);
        _zone = TownServiceMerchantZone.CreateTemplate(window.GetComponentInChildren<TMP_Text>(true));
        _zone.transform.SetParent(Root, false);
        _zone.transform.localPosition = service == 2 ? new Vector3(0f, .16f, .26f) : new Vector3(0f, .014f, 0f);
        _zoneGate = _zone.GetComponent<CanvasGroup>(); _zoneGate.alpha = 0f;
        _zoneLabel = _zone.transform.Find("Caption").GetComponent<TMP_Text>();
        if (service == 2)
        {
            ((RectTransform)_zone.transform).sizeDelta = new Vector2(190f, 190f);
            ((RectTransform)_zone.transform.Find("Border")).sizeDelta = new Vector2(190f, 190f);
            _zoneLabel.rectTransform.sizeDelta = new Vector2(175f, 90f); _zoneLabel.fontSize = 24f;
        }
        if (service == 2)
        {
            UITempleWindow temple = window.GetComponent<UITempleWindow>();
            _inscriptions.Add(new Inscription("temple.level", temple.devotionLevel, Root,
                new Vector3(-.33f, .022f, .025f), .26f, .025f));
            _inscriptions.Add(new Inscription("temple.gold", temple.totalDonatedGold.text, Root,
                new Vector3(-.40f, .022f, -.015f), .10f, .025f));
            if (temple.devotionProgress.AmountTexts.Count > 0)
                _inscriptions.Add(new Inscription("temple.progress", temple.devotionProgress.AmountTexts[0], Root,
                    new Vector3(-.26f, .022f, -.015f), .12f, .025f));
            _inscriptions.Add(new Inscription("temple.description", temple.helpBox.tipText, Root,
                new Vector3(-.33f, .022f, -.09f), .26f, .12f));
        }
        if (service == 3)
        {
            // The selected original card retains its actual ability/enhancement hotspots.
            // This is the narrow native UI required to choose which printed ability changes.
            UINewEnhancementWindow shop = window.GetComponent<UINewEnhancementWindow>();
            _surfaces.Add(new TownServiceSurface(11, (RectTransform)shop.cardHolder.transform,
                new Vector3(0f, .008f, 0f), .25f, Root));
        }
        RefreshPieces();
    }

    internal void SetVisibility(float visibility, bool allowInput)
    {
        _allowInput = allowInput;
        _opening.interactable = allowInput; _opening.blocksRaycasts = allowInput;
        foreach (TownServiceSurface surface in _surfaces) surface.SetVisibility(visibility, allowInput);
        if (_visibility == visibility) return;
        _visibility = _opening.alpha = Mathf.Clamp01(visibility);
        foreach (Piece piece in _pieces.Values) piece.SetVisibility(_visibility);
    }

    internal void Tick(float scale)
    {
        if (_disposed || !_alive()) return;
        if (_service == 2 && TownServiceDecor.CoinTemplate == null && Time.unscaledTime - _started > 15f)
            throw new InvalidOperationException("Original offering geometry did not load; restoring the native temple window.");
        if (Time.unscaledTime >= _censusAt) { _censusAt = Time.unscaledTime + .2f; RefreshPieces(); }
        foreach (Piece piece in _pieces.Values) piece.Tick(scale);
        _zoneGate.alpha = 0f;
        foreach (Piece piece in _pieces.Values)
        {
            if (!piece.Token.DropEligible) continue;
            _zoneLabel.text = Loc.Mod(_service == 2 ? "town_offering"
                : piece.Source is AbilityCardUI ? "town_enchant_card" : "town_inscribe");
            _zoneGate.alpha = 1f; break;
        }
        foreach (Inscription inscription in _inscriptions) inscription.Tick();
        foreach (TownServiceSurface surface in _surfaces) surface.Tick(Vector3.zero, Quaternion.identity, scale);
    }

    private void RefreshPieces()
    {
        _retired.Clear();
        foreach (var pair in _pieces) if (!pair.Value.Current) _retired.Add(pair.Key);
        foreach (Component source in _retired)
        { _samples.Remove(_pieces[source].Token); _pieces[source].Dispose(); _pieces.Remove(source); }
        if (_service == 2)
        {
            // Keep the interaction pending during the original asynchronous prop load.
            // A featureless primitive or a card-shaped replacement is not a coin offering.
            if (TownServiceDecor.CoinTemplate == null) return;
            UITempleWindow temple = _window.GetComponent<UITempleWindow>();
            int index = 0;
            foreach (UITempleShopSlot slot in temple.Shop.slots)
            {
                if (slot == null || !slot.gameObject.activeInHierarchy || slot.Blessing == null) continue;
                if (!_pieces.ContainsKey(slot))
                    Add(slot, "temple.row", slot.button, () => slot.Blessing,
                        () => TempleEligible(temple, slot),
                        () => Confirm(slot.button, () => slot.Blessing, temple, () => TempleEligible(temple, slot)),
                        new Vector3(-.42f + (index % 3) * .20f, .025f, .16f + index / 3 * .19f), false,
                        slot.blessIcon, slot.blessName, slot.priceText);
                index++;
            }
        }
        else
        {
            UINewEnhancementWindow shop = _window.GetComponent<UINewEnhancementWindow>();
            if (shop.shopService.IsSellAvailable)
            {
                AddMode(shop.buyButton, "enchant.buy", new Vector3(-.16f, .025f, -.34f));
                AddMode(shop.sellButton, "enchant.sell", new Vector3(.16f, .025f, -.34f));
            }
            int index = 0;
            foreach (UIEnhanceCardSlot slot in shop.CardsDisplay.slotsPool)
            {
                if (slot == null || !slot.gameObject.activeInHierarchy || slot.AbilityCard == null) continue;
                AbilityCardUI card = slot.AbilityCard;
                if (!_pieces.ContainsKey(card))
                    Add(card, NativeTemplates.CardKey(card), slot.Selectable,
                        () => slot.AbilityCard != null ? slot.AbilityCard.AbilityCard : null,
                        () => slot.Selectable.IsInteractable(),
                        () => Click(slot.Selectable),
                        new Vector3(-.58f - (index / 10) * .16f, .025f + (index % 10) * .010f,
                            -.25f + (index % 10) * .035f), true);
                index++;
            }
            index = 0;
            foreach (UINewEnhancementShopSlot slot in shop.enhancementShop.slotsPool)
            {
                if (slot == null || !slot.gameObject.activeInHierarchy || slot.enhancement == null) continue;
                if (!_pieces.ContainsKey(slot))
                    Add(slot, "enchant.row", slot.button, () => slot.enhancement,
                        () => RuneEligible(shop, slot),
                        () => Confirm(slot.button, () => slot.enhancement, shop, () => RuneEligible(shop, slot)),
                        new Vector3(.34f + (index % 3) * .18f, .025f, -.20f + index / 3 * .18f), false,
                        slot.itemIcon, slot.itemName, slot.itemPrice, slot.enhancementPoints);
                index++;
            }
        }
    }

    private void AddMode(Selectable button, string key, Vector3 position)
    {
        if (_pieces.ContainsKey(button) || !button.gameObject.activeInHierarchy) return;
        TMP_Text? label = button.GetComponentInChildren<TMP_Text>(true);
        Add(button, key, button, () => button, () => button.IsInteractable(),
            () => Click(button), position, false, label!);
    }

    private void Add(Component source, string key, Selectable button, Func<object?> identity,
        Func<bool> eligible, Func<bool> drop, Vector3 position, bool card, params Graphic[] inscriptions)
    {
        var piece = new Piece(this, source, key, button, identity, eligible, drop, position, card, inscriptions);
        _pieces.Add(source, piece); _samples.Add(piece.Token);
    }

    private static bool TempleEligible(UITempleWindow temple, UITempleShopSlot slot) => temple.character != null
        && temple.Shop.slotsCanvasGroup.interactable && slot.IsAvailable && slot.button.IsInteractable()
        && temple.service.CanBuy(temple.character.CharacterID, slot.Blessing);

    private static bool RuneEligible(UINewEnhancementWindow shop, UINewEnhancementShopSlot slot)
    {
        if (shop.character == null || slot.enhancement == null || !slot.button.IsInteractable()
            || !shop.enhancementShop.enhancementsCanvasGroup.interactable) return false;
        EnhancementSlot enhancement = slot.enhancement;
        if (!enhancement.BuyMode) return shop.shopService.IsSellAvailable && enhancement.AvailableToSell;
        return enhancement.AvailableToBuy && enhancement.priceCalculator != null
            && shop.character.Gold >= enhancement.priceCalculator.CalculateTotalPrice(enhancement)
            && EnhancementBuyPriceCalculator.CanAffordPoints(enhancement, shop.character);
    }

    private bool Confirm(Selectable button, Func<object?> identity, Component controller, Func<bool> eligible)
    {
        UIEnhancementConfirmationBox? box = Singleton<UIEnhancementConfirmationBox>.Instance;
        if (!_alive() || box == null || box.GetComponent<UIWindow>().IsOpen || !button.IsInteractable() || !eligible()) return false;
        object? context = _context(), selected = identity();
        Action? previous = box._onConfirmCallback;
        if (!Click(button)) return false;
        // The native selection synchronously installs its callback. Refusal or an unrelated
        // pre-existing prompt cannot become an implicit purchase. The callback still owns
        // server validation, currency, devotion, enhancement limits and transition completion.
        bool owns = controller is UITempleWindow temple ? temple._isConfirmationBoxOpened
            : ((UINewEnhancementWindow)controller)._isConfirmationBoxOpened;
        bool created = owns && box.GetComponent<UIWindow>().IsOpen && box._onConfirmCallback != null
            && !ReferenceEquals(previous, box._onConfirmCallback);
        if (created) TownServiceConfirmationMask.Begin(box.GetComponent<UIWindow>(), () => box._onConfirmCallback);
        if (!_alive() || !eligible() || !button.IsActive() || !button.IsInteractable()
            || !ReferenceEquals(context, _context()) || !ReferenceEquals(selected, identity())
            || !created)
        {
            // Cancel only the prompt installed by this selection. Its original transition
            // clears the controller's pending flag; never leave a stale purchase clickable.
            if (created) box.Hide();
            return false;
        }
        if (Click(box.confirmButton)) return true;
        box.Hide(); return false;
    }

    private static bool Click(Selectable button)
    {
        if (button == null || !button.IsActive() || !button.IsInteractable() || EventSystem.current == null) return false;
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (Piece piece in _pieces.Values) piece.Dispose(); _pieces.Clear(); _samples.Clear();
        foreach (Inscription inscription in _inscriptions) inscription.Dispose(); _inscriptions.Clear();
        for (int i = _surfaces.Count - 1; i >= 0; i--) _surfaces[i].Dispose(); _surfaces.Clear();
        UnityEngine.Object.Destroy(Root.gameObject);
    }
}
