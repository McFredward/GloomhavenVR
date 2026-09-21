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
            _reach.sizeDelta = card ? new Vector2(.145f, .22f) : new Vector2(.17f, .17f);
            Body = TownServiceCardBody.Create(Root).transform;
            Body.localScale = new Vector3(_reach.sizeDelta.x, _reach.sizeDelta.y, 1f);
            var mount = new GameObject("Original inscriptions").transform;
            mount.SetParent(Root, false); mount.localPosition = new Vector3(0f, 0f, -.002f);
            _mirror = new RemoteWidgetMirror("TownRitual", mount, _reach.sizeDelta.x,
                _reach.sizeDelta.y, Vector2.zero, mrBacking: false);
            var rect = (RectTransform)source.transform;
            Vector2 frame = card ? rect.rect.size : Vector2.one * Mathf.Max(1f, rect.rect.width);
            _mirror.SetOwnerFrame(frame, rect.parent is RectTransform parent ? parent.rect.size : rect.rect.size);
            if (!_mirror.Refresh(source.transform)) throw new InvalidOperationException("Original ritual artwork is unavailable");
            Token = new TownServiceToken(_reach, button, identity, owner._context,
                () => owner._alive() && Current, owner.Root, Root, drop, eligible);
            VRLayers.Apply(root);
            ApplyInscriptions();
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
                rect.sizeDelta = new Vector2(i == 0 ? height * .35f : width * .9f, height * .23f);
                if (copy is TMP_Text text)
                { text.enableAutoSizing = true; text.fontSizeMin = 8f; text.fontSizeMax = height * .18f; text.alignment = TextAlignmentOptions.Center; }
            }
        }

        public void Dispose()
        {
            Token.Dispose();
            if (_hovering && Current && EventSystem.current != null)
                ExecuteEvents.Execute(_button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
            _details?.Destroy(); _mirror.Destroy(); TownServiceCardBody.Dispose(Body.gameObject);
            UnityEngine.Object.Destroy(Root.gameObject);
        }
    }

    private readonly UIWindow _window;
    private readonly byte _service;
    private readonly Func<bool> _alive;
    private readonly Func<object?> _context;
    private readonly Dictionary<Component, Piece> _pieces = new();
    private readonly List<Component> _retired = new();
    private readonly List<TownServiceSurface> _surfaces = new();
    private readonly List<Inscription> _inscriptions = new();
    private float _censusAt;
    private bool _disposed;
    internal Transform Root { get; }
    internal IEnumerable<Piece> Pieces => _pieces.Values;
    internal IReadOnlyList<Inscription> Inscriptions => _inscriptions;
    internal IReadOnlyList<TownServiceSurface> Surfaces => _surfaces;
    internal bool CanRelocate
    { get { foreach (Piece piece in _pieces.Values) if (piece.Token.IsMoving) return false; return true; } }

    internal TownServiceRitual(UIWindow window, byte service, Transform station,
        Func<bool> alive, Func<object?> context)
    {
        _window = window; _service = service; _alive = alive; _context = context;
        Root = new GameObject("GloomhavenVR.TownService.Ritual").transform;
        Root.SetParent(station, false); Root.localPosition = new Vector3(0f, .978f, -.08f);
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
        Census();
    }

    internal void Tick(float scale)
    {
        if (_disposed || !_alive()) return;
        if (Time.unscaledTime >= _censusAt) { _censusAt = Time.unscaledTime + .2f; Census(); }
        foreach (Piece piece in _pieces.Values) piece.Tick(scale);
        foreach (Inscription inscription in _inscriptions) inscription.Tick();
        foreach (TownServiceSurface surface in _surfaces) surface.Tick(Vector3.zero, Quaternion.identity, scale);
    }

    private void Census()
    {
        _retired.Clear();
        foreach (var pair in _pieces) if (!pair.Value.Current) _retired.Add(pair.Key);
        foreach (Component source in _retired) { _pieces[source].Dispose(); _pieces.Remove(source); }
        if (_service == 2)
        {
            UITempleWindow temple = _window.GetComponent<UITempleWindow>();
            int index = 0;
            foreach (UITempleShopSlot slot in temple.Shop.slots)
            {
                if (slot == null || !slot.gameObject.activeInHierarchy || slot.Blessing == null) continue;
                if (!_pieces.ContainsKey(slot))
                    Add(slot, "temple.row", slot.button, () => slot.Blessing,
                        () => temple.Shop.slotsCanvasGroup.interactable && slot.button.IsInteractable() && slot.IsAvailable,
                        () => Confirm(slot.button, () => slot.Blessing, temple),
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
                    Add(card, NativeTemplates.CardKey(card), slot.Selectable, () => slot.AbilityCard,
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
                        () => slot.button.IsInteractable(),
                        () => Confirm(slot.button, () => slot.enhancement, shop),
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
    { _pieces.Add(source, new Piece(this, source, key, button, identity, eligible, drop, position, card, inscriptions)); }

    private bool Confirm(Selectable button, Func<object?> identity, Component controller)
    {
        UIEnhancementConfirmationBox? box = Singleton<UIEnhancementConfirmationBox>.Instance;
        if (!_alive() || box == null || box.GetComponent<UIWindow>().IsOpen || !button.IsInteractable()) return false;
        object? context = _context(), selected = identity();
        Action? previous = box._onConfirmCallback;
        if (!Click(button)) return false;
        // The native selection synchronously installs its callback. Refusal or an unrelated
        // pre-existing prompt cannot become an implicit purchase. The callback still owns
        // server validation, currency, devotion, enhancement limits and transition completion.
        if (!_alive() || !ReferenceEquals(context, _context()) || !ReferenceEquals(selected, identity())
            || !box.GetComponent<UIWindow>().IsOpen || box._onConfirmCallback == null
            || ReferenceEquals(previous, box._onConfirmCallback)) return false;
        bool owns = controller is UITempleWindow temple ? temple._isConfirmationBoxOpened
            : ((UINewEnhancementWindow)controller)._isConfirmationBoxOpened;
        return owns && Click(box.confirmButton);
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
        foreach (Piece piece in _pieces.Values) piece.Dispose(); _pieces.Clear();
        foreach (Inscription inscription in _inscriptions) inscription.Dispose(); _inscriptions.Clear();
        for (int i = _surfaces.Count - 1; i >= 0; i--) _surfaces[i].Dispose(); _surfaces.Clear();
        UnityEngine.Object.Destroy(Root.gameObject);
    }
}
