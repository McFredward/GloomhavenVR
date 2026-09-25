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
        private readonly RemoteWidgetMirror _mirror = null!;
        private readonly TownServiceBookInk _ink;
        internal Transform? Content => Source != null && Source.gameObject.activeInHierarchy ? _mirror.CloneOf(Source) : null;
        internal Transform? CloneOf(Transform source) => _mirror.CloneOf(source);
        internal Inscription(string key, Component source, Transform parent, Vector3 position, float width, float height)
        {
            Key = key; Source = source.transform;
            _ink = new TownServiceBookInk(key, parent);
            _root = new GameObject("Town ledger inscription").transform;
            _root.SetParent(parent, false); _root.localPosition = position;
            _root.localRotation = Quaternion.Euler(90f, 0f, 0f);
            try
            {
                _mirror = new RemoteWidgetMirror("TownLedger", _root, width, height, Vector2.zero, mrBacking: false);
                if (!_mirror.Refresh(Source)) throw new InvalidOperationException("Original ledger inscription is unavailable");
                Tick();
            }
            catch { _mirror?.Destroy(); UnityEngine.Object.Destroy(_root.gameObject); throw; }
        }
        internal void Tick()
        {
            _mirror.SetShown(Source != null && Source.gameObject.activeInHierarchy);
            _mirror.TickLive();
            _ink.Apply(Content);
        }
        public void Dispose()
        {
            _mirror?.Destroy();
            // Unity may already have destroyed the station during scene teardown.
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        }
    }

    internal sealed class Piece : IDisposable
    {
        private readonly TownServiceRitual _owner;
        internal readonly Component Source;
        internal readonly string Key;
        internal readonly Transform Root;
        internal readonly TownServiceToken Token = null!;
        internal readonly Transform Body = null!;
        internal readonly string BodyKey;
        private readonly RemoteWidgetMirror _mirror = null!;
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
        private readonly CanvasGroup? _purseGate;
        private float _requestedVisibility = 1f;
        private Transform? _graphicRoot;
        private readonly List<Graphic> _graphics = new();
        private readonly List<Material> _coinMaterials = new();
        private float _refreshAt;
        private TownServiceRitualLayout.Placement _placement;
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
            Func<object?> identity, Func<bool> eligible, Func<bool> drop, TownServiceRitualLayout.Placement placement,
            bool card, params Graphic[] inscriptions)
        {
            _owner = owner; Source = source; Key = key; _identity = identity; _button = button;
            _placement = placement;
            _createdIdentity = identity(); _inscriptions = new List<Graphic>(inscriptions); _card = card;
            var root = new GameObject("GloomhavenVR.TownService." + key, typeof(RectTransform));
            Root = root.transform;
            try
            {
                Root.SetParent(owner._templeOffering?.Root ?? owner.Root, false);
                Root.localPosition = placement.Position; Root.localRotation = placement.Rotation;
                _reach = (RectTransform)Root;
                bool offering = source is UITempleShopSlot;
                _reach.sizeDelta = placement.Size;
                if (offering)
                {
                    _purseGate = Root.gameObject.AddComponent<CanvasGroup>();
                    Transform template = TownServiceDecor.MoneyBagTemplate
                        ?? throw new InvalidOperationException("Original offering purse is still loading");
                    Body = UnityEngine.Object.Instantiate(template.gameObject, Root, false).transform;
                    Body.localPosition = new Vector3(0f, -.065f, 0f); Body.localRotation = Quaternion.identity;
                    Body.localScale = template.localScale; Body.gameObject.SetActive(true);
                    BodyKey = "ritual.purse";
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
                    () => owner._alive() && Current, owner._templeOffering?.DropFrame ?? owner.Root, Root, drop, eligible,
                    owner._service == 2 ? TownServiceTempleBowl.Center : Vector3.zero,
                    inspect: () => owner._templeOffering?.Available ?? true,
                    zoneHalfWidth: owner._service == 2 ? .095f : .20f,
                    dropLocation: owner._service == 2 ? world => owner._templeOffering?.InBowl(world) ?? false : null, reachDepth: offering ? .10f : .009f, uprightProp: offering,
                    handAllowed: hand => owner._templeOffering?.AllowsHand(hand) ?? true);
                VRLayers.Apply(root);
                ApplyInscriptions(); SetVisibility(owner._visibility);
            }
            catch { Dispose(); throw; }
        }

        internal void SetVisibility(float visibility)
        {
            _requestedVisibility = visibility;
            visibility *= Token?.PhysicalVisibility ?? 1f;
            if (_purseGate != null) _purseGate.alpha = visibility;
            if (BodyKey == "merchant.cardbody") TownServiceCardBody.SetVisibility(Body.gameObject, visibility);
            foreach (Material material in _coinMaterials) material.SetFloat("_TownVisibility", visibility);
        }

        internal void Tick(float scale)
        {
            if (!Current) return;
            if (!Token.IsMoving)
            {
                float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / .06f);
                Root.localPosition = Vector3.Lerp(Root.localPosition, _placement.Position, blend);
                Root.localRotation = Quaternion.Slerp(Root.localRotation, _placement.Rotation, blend);
            }
            if (Time.unscaledTime >= _refreshAt)
            { _refreshAt = Time.unscaledTime + .2f; _mirror.Refresh(Source.transform); }
            _mirror.TickLive(); ApplyInscriptions(); Token.Tick(scale);
            SetVisibility(_requestedVisibility);
            TickDetails();
        }

        internal void Arrange(TownServiceRitualLayout.Placement placement) => _placement = placement;

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
            Token?.Dispose();
            if (_hovering && Current && EventSystem.current != null)
                ExecuteEvents.Execute(_button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
            _details?.Destroy(); _mirror?.Destroy();
            foreach (Material material in _coinMaterials) UnityEngine.Object.Destroy(material); _coinMaterials.Clear();
            if (Body != null && BodyKey == "merchant.cardbody") TownServiceCardBody.Dispose(Body.gameObject);
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
        }
    }

    private readonly UIWindow _window;
    private readonly byte _service;
    private readonly Func<bool> _alive;
    private readonly Func<bool> _sessionAlive;
    private readonly Func<object?> _context;
    private readonly Dictionary<Component, Piece> _pieces = new();
    private readonly List<TownServiceToken> _samples = new();
    private readonly List<Component> _retired = new();
    private readonly List<TownServiceSurface> _surfaces = new();
    private readonly List<Inscription> _inscriptions = new();
    private readonly GameObject? _zone;
    private readonly CanvasGroup? _zoneGate;
    private readonly CanvasGroup _opening;
    private bool _allowInput = true;
    private float _visibility;
    private readonly TMP_Text? _zoneLabel;
    private readonly TownServiceTempleBowlMarker? _bowlMarker;
    private float _censusAt;
    private readonly float _started = Time.unscaledTime;
    private bool _disposed;
    private TownServiceTempleOffering? _templeOffering;
    private readonly HashSet<(string Character, object Blessing)> _submittedOfferings = new();
    internal TownServiceEnhancementHandoff? Handoff { get; private set; }
    internal TownServiceCardSlots CardSlots { get; } = new();
    internal Transform Root { get; }
    internal IEnumerable<Piece> Pieces => _pieces.Values;
    internal IReadOnlyCollection<TownServiceToken> Samples => _samples;
    internal IReadOnlyList<Inscription> Inscriptions => _inscriptions;
    internal Transform? Zone => _bowlMarker?.Visual ?? _zone?.transform;
    internal IReadOnlyList<TownServiceSurface> Surfaces => _surfaces;
    internal bool CanRelocate
    { get { if (Handoff?.Card != null) return false; foreach (Piece piece in _pieces.Values) if (piece.Token.IsMoving) return false; return true; } }

    internal TownServiceRitual(UIWindow window, byte service, Transform station,
        Func<bool> alive, Func<object?> context)
    {
        _window = window; _service = service; _sessionAlive = alive;
        _alive = () => alive() && _allowInput && _visibility >= .99f; _context = context;
        Root = new GameObject("GloomhavenVR.TownService.Ritual").transform;
        try
        {
            _opening = Root.gameObject.AddComponent<CanvasGroup>(); _opening.alpha = 0f;
            Root.SetParent(station, false); Root.localPosition = TownServiceRitualLayout.Origin;
            if (service == 2)
                _templeOffering = new TownServiceTempleOffering(this, window.GetComponent<UITempleWindow>(),
                    TownServicePresentation.StationRoot ?? station);
            if (service == 2)
            {
                _bowlMarker = new TownServiceTempleBowlMarker(_templeOffering!.DropFrame);
            }
            else
            {
                _zone = TownServiceMerchantZone.CreateTemplate(window.GetComponentInChildren<TMP_Text>(true));
                _zone.transform.SetParent(Root, false);
                _zone.transform.localPosition = new Vector3(0f, .014f, 0f);
                _zoneGate = _zone.GetComponent<CanvasGroup>(); _zoneGate.alpha = 0f;
                _zoneLabel = _zone.transform.Find("Caption").GetComponent<TMP_Text>();
                ((RectTransform)_zone.transform).sizeDelta = new Vector2(380f, 260f);
                ((RectTransform)_zone.transform.Find("Border")).sizeDelta = new Vector2(380f, 260f);
                _zoneLabel.rectTransform.sizeDelta = new Vector2(360f, 70f); _zoneLabel.fontSize = 36f;
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
                Handoff = new TownServiceEnhancementHandoff(shop,
                    TownServicePresentation.StationRoot ?? throw new InvalidOperationException("Resident is unavailable"),
                    alive, _alive);
                // Keep the original scroll viewport, rows, price breakdown and native
                // hover/selection controllers together. Build 550's manually stacked rune
                // cards let child anchors escape their faces and obscured later options.
                // This open folio is attached to the stand, not a separate movable window.
                AddFolio(10, shop.enhancementShop);
                AddFolio(11, shop.cardHolder);
                AddFolio(13, shop.CardsDisplay.enhancementPointsText.transform.parent);
                AddFolio(14, shop.cardInformationText);
                AddFolio(15, shop.buyButton);
                AddFolio(16, shop.sellButton);
            }
            RefreshPieces();
        }
        catch { Dispose(); throw; }
    }

    internal void SetVisibility(float visibility, bool allowInput)
    {
        _allowInput = allowInput;
        if (!allowInput || visibility < .99f)
        { if (_zoneGate != null) _zoneGate.alpha = 0f; _bowlMarker?.Tick(false); }
        _opening.interactable = allowInput; _opening.blocksRaycasts = allowInput;
        foreach (TownServiceSurface surface in _surfaces) surface.SetVisibility(visibility, allowInput);
        if (_visibility == visibility) return;
        _visibility = _opening.alpha = Mathf.Clamp01(visibility);
        foreach (Piece piece in _pieces.Values) piece.SetVisibility(_visibility);
    }

    internal void Tick(float scale)
    {
        Handoff?.Tick();
        if (Handoff != null) CardSlots.Tick(Handoff);
        _templeOffering?.Tick(!_disposed && _alive());
        if (_disposed || !_alive()) return;
        if (_service == 2 && TownServiceDecor.MoneyBagTemplate == null && Time.unscaledTime - _started > 15f)
            throw new InvalidOperationException("Original offering geometry did not load; restoring the native temple window.");
        if (Time.unscaledTime >= _censusAt) { _censusAt = Time.unscaledTime + .2f; RefreshPieces(); }
        foreach (Piece piece in _pieces.Values) piece.Tick(scale);
        if (_zoneGate != null) _zoneGate.alpha = 0f;
        bool offeringHeld = false;
        foreach (Piece piece in _pieces.Values)
        {
            if (!piece.Token.DropEligible) continue;
            offeringHeld = true;
            if (_zoneLabel != null && _zoneGate != null)
            {
                _zoneLabel.text = Loc.Mod(piece.Source is AbilityCardUI ? "town_enchant_card" : "town_inscribe");
                _zoneGate.alpha = 1f;
            }
            break;
        }
        // Show the noninteractive destination while the player can inspect a purse,
        // not only after the purse has already been picked up. Otherwise the cue is
        // missing when deciding where to carry it.
        _bowlMarker?.Tick(offeringHeld || _templeOffering?.Available == true);
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
            // A featureless primitive or a card-shaped replacement is not a cloth purse.
            if (TownServiceDecor.MoneyBagTemplate == null) return;
            UITempleWindow temple = _window.GetComponent<UITempleWindow>();
            int index = 0, count = 0;
            foreach (UITempleShopSlot slot in temple.Shop.slots)
                if (slot != null && slot.gameObject.activeInHierarchy && slot.Blessing != null) count++;
            foreach (UITempleShopSlot slot in temple.Shop.slots)
            {
                if (slot == null || !slot.gameObject.activeInHierarchy || slot.Blessing == null) continue;
                var placement = TownServiceRitualLayout.Offering(index++, count);
                if (ArrangeExisting(slot, placement)) continue;
                Add(slot, "temple.row", slot.button, () => slot.Blessing,
                        () => OfferingEligible(temple, slot),
                        () => Donate(temple, slot, accepted =>
                        { if (_pieces.TryGetValue(slot, out Piece? piece)) piece.Token.CompletePhysicalOffering(accepted); }),
                        placement, false,
                        slot.blessIcon, slot.blessName, slot.priceText);
            }
        }
        // Enchantment uses the original native inventory above, including its scroll
        // container. Do not create a second independently laid out or clickable stock.
    }

    private void AddFolio(ushort id, Component source)
    {
        TownServiceRitualLayout.Placement placement = TownServiceRitualLayout.Folio(id);
        _surfaces.Add(new TownServiceSurface(id, (RectTransform)source.transform, placement.Position,
            placement.Size.x, Root, placement.Rotation, placement.Size.y));
    }

    private void Add(Component source, string key, Selectable button, Func<object?> identity,
        Func<bool> eligible, Func<bool> drop, TownServiceRitualLayout.Placement placement, bool card, params Graphic[] inscriptions)
    {
        var piece = new Piece(this, source, key, button, identity, eligible, drop, placement, card, inscriptions);
        _pieces.Add(source, piece); _samples.Add(piece.Token);
    }

    private bool ArrangeExisting(Component source, TownServiceRitualLayout.Placement placement)
    {
        if (!_pieces.TryGetValue(source, out Piece? piece)) return false;
        piece.Arrange(placement);
        return true;
    }

    private bool OfferingEligible(UITempleWindow temple, UITempleShopSlot slot) => _templeOffering?.Available == true && temple.character != null && slot.Blessing != null
        && !_submittedOfferings.Contains((temple.character.CharacterID, slot.Blessing)) && TempleEligible(temple, slot);

    private bool Donate(UITempleWindow temple, UITempleShopSlot slot, Action<bool>? settled = null)
    {
        if (!OfferingEligible(temple, slot))
        {
            if (VRLog.WantsDebug) VRLog.Debug("TownServices", "Temple purse entered bowl but native blessing is unavailable; confirmation was not opened.");
            return false;
        }
        var offering = (temple.character.CharacterID, (object)slot.Blessing);
        _submittedOfferings.Add(offering);
        bool submitted = Confirm(slot.button, () => slot.Blessing, temple,
            () => _templeOffering?.Available == true && TempleEligible(temple, slot),
            () => _templeOffering?.VisitorPresent == true && TemplePendingEligible(temple, slot),
            committed =>
            {
                if (!committed) _submittedOfferings.Remove(offering);
                // This confirms execution of the original callback, not a later host
                // inventory/currency acknowledgement. Keep those distinct in bug logs.
                if (committed)
                {
                    VRLog.Note("TownServices", "Temple purse: original donation callback executed for the selected blessing.");
                    TownServiceVoice.RequestReaction(2, TownVoiceReaction.PriestessDonate);
                }
                else if (VRLog.WantsDebug) VRLog.Debug("TownServices", "Temple purse: native confirmation cancelled or stale before donation callback.");
                settled?.Invoke(committed);
            });
        // Online clients wait for the original host action before stock refreshes. A second
        // release during that interval must never send the same donation twice.
        if (!submitted)
        {
            _submittedOfferings.Remove(offering);
            if (VRLog.WantsDebug) VRLog.Debug("TownServices", "Temple purse: native confirmation could not be submitted.");
        }
        return submitted;
    }

    private static bool TempleEligible(UITempleWindow temple, UITempleShopSlot slot) => temple.character != null
        && MapRoomHand.OwnedMerchantCharacter()?.CharacterID == temple.character.CharacterID
        && temple.Shop.slotsCanvasGroup.interactable && slot.IsAvailable && slot.button.IsInteractable()
        // CanBuy opens native warnings on failure. Quiet live reads must precede its
        // multiplayer permission check so a held unaffordable offering cannot spam UI.
        && temple.service.IsAvailable(temple.character.CharacterID, slot.Blessing)
        && temple.service.CanAfford(temple.character.CharacterID, slot.Blessing)
        && temple.service.CanBuy(temple.character.CharacterID, slot.Blessing);

    // The original selection disables its row and shop canvas while its modal is open.
    // Those presentation/input flags are valid before the click, but cannot validate
    // the pending original callback: doing so cancelled every physical donation in 560.
    private static bool TemplePendingEligible(UITempleWindow temple, UITempleShopSlot slot) => temple.character != null
        && slot != null && slot.Blessing != null
        && MapRoomHand.OwnedMerchantCharacter()?.CharacterID == temple.character.CharacterID
        && temple.service.IsAvailable(temple.character.CharacterID, slot.Blessing)
        && temple.service.CanAfford(temple.character.CharacterID, slot.Blessing)
        && temple.service.CanBuy(temple.character.CharacterID, slot.Blessing);

    private bool Confirm(Selectable button, Func<object?> identity, Component controller,
        Func<bool> eligible, Func<bool> pendingEligible, Action<bool>? completed = null)
    {
        UIEnhancementConfirmationBox? box = Singleton<UIEnhancementConfirmationBox>.Instance;
        if (!_alive() || box == null || box.GetComponent<UIWindow>().IsOpen || !button.IsInteractable() || !eligible()) return false;
        object? context = _context(), selected = identity();
        Action? previous = box._onConfirmCallback;
        using var confirmation = TownServiceRitualConfirmationGuard.Begin(box,
            () => _sessionAlive() && pendingEligible() && button != null
                && ReferenceEquals(context, _context()) && ReferenceEquals(selected, identity()), completed);
        if (!Click(button)) return false;
        // The native selection synchronously installs its callback. Refusal or an unrelated
        // pre-existing prompt cannot become an implicit purchase. The callback still owns
        // server validation, currency, devotion, enhancement limits and transition completion.
        bool owns = controller is UITempleWindow temple ? temple._isConfirmationBoxOpened
            : ((UINewEnhancementWindow)controller)._isConfirmationBoxOpened;
        bool created = owns && box.GetComponent<UIWindow>().IsOpen && box._onConfirmCallback != null
            && !ReferenceEquals(previous, box._onConfirmCallback);
        if (created) TownServiceConfirmationMask.Begin(box.GetComponent<UIWindow>(), () => box._onConfirmCallback);
        Func<bool> stillValid = () => _sessionAlive() && pendingEligible()
            && ReferenceEquals(context, _context()) && ReferenceEquals(selected, identity());
        bool valid = stillValid();
        if (!valid || !created)
        {
            if (VRLog.WantsDebug) VRLog.Debug("TownServices", "Temple purse confirmation refused after native selection: "
                + "owned=" + owns + " open=" + box.GetComponent<UIWindow>().IsOpen
                + " callbackChanged=" + !ReferenceEquals(previous, box._onConfirmCallback)
                + " pendingValid=" + valid + " sourceInteractable=" + button.IsInteractable()
                + " confirmInteractable=" + (box.confirmButton != null && box.confirmButton.IsInteractable()));
            // Cancel only the prompt installed by this selection. Its original transition
            // clears the controller's pending flag; never leave a stale purchase clickable.
            if (created) box.Hide();
            return false;
        }
        // Gamepad mode intentionally does not wire confirmButton.OnClick in the flat game.
        // Waiting for that visual control therefore leaves every VR donation pending forever.
        // Enter through the confirmation box's own continuation instead: OnConfirm installs the
        // native hidden-transition callback and starts Hide, so controller flags, navigation and
        // delayed transaction validation still follow the exact flat-game lifecycle.
        try
        {
            box.OnConfirm();
            return true;
        }
        catch (Exception error)
        {
            VRLog.Error("TownServices", "Temple purse could not continue through the original confirmation lifecycle: " + error);
            if (box.GetComponent<UIWindow>().IsOpen) box.Hide();
            return false;
        }
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
        CardSlots.Dispose();
        Handoff?.Dispose(); Handoff = null;
        foreach (Piece piece in _pieces.Values) piece.Dispose(); _pieces.Clear(); _samples.Clear();
        _bowlMarker?.Dispose();
        _templeOffering?.Dispose(); _templeOffering = null;
        foreach (Inscription inscription in _inscriptions) inscription.Dispose(); _inscriptions.Clear();
        for (int i = _surfaces.Count - 1; i >= 0; i--) _surfaces[i].Dispose(); _surfaces.Clear();
        if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
    }
}
