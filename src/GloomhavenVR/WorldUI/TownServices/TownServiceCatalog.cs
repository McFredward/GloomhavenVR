using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Original native merchant stock in one indexed cabinet cassette.
/// Physical category buttons and a crank select bounded trays. Only an explicit eligible palm drop
/// enters the original native transaction; picking up a card is always inspection.</summary>
internal sealed class TownServiceCatalog : IDisposable
{
    private static readonly Dictionary<Transform, Entry> CardMounts = new();
    internal static bool IsCardMountChild(Transform source) => source.parent != null && CardMounts.ContainsKey(source.parent);
    // Ownership survives hidden trays but ends before pooled native cards are returned.
    // The mount also distinguishes a new catalog borrower reusing the same native transform.
    internal static Transform? PresentationOwner(Transform source)
    {
        for (Transform? node = source; node != null; node = node.parent)
            if (CardMounts.TryGetValue(node, out Entry? entry))
                return source == entry.MountRoot || source == entry.CardRoot || source == entry.BodyRoot
                    || source == entry.RowContent ? node : null;
        return null;
    }
    private readonly UIShopItemInventory _inventory;
    private readonly bool _persistent;
    private readonly Transform _anchor, _mat;
    private readonly Func<object?> _contextIdentity;
    private readonly Func<bool> _alive;
    private readonly GameObject _root;
    private readonly CanvasGroup _opening;
    private readonly TownServiceMerchantRows _backend;
    private readonly List<Entry> _entries = new();
    private readonly List<TownServiceToken> _samples = new();
    private readonly HashSet<UIShopItemSlot> _warmSources = new();
    private readonly List<TownServiceMerchantDrawer> _drawers = new();
    private readonly List<TownServiceCatalogCategory> _categories = new();
    internal IReadOnlyList<TownServiceCatalogCategory> Categories => _categories;
    private readonly List<TownServiceMerchantCounter> _extensions = new();
    private readonly List<TownServiceMerchantZone> _zones = new();
    internal IReadOnlyList<TownServiceMerchantDrawer> Drawers => _drawers;
    internal IReadOnlyList<TownServiceMerchantCounter> Extensions => _extensions;
    internal IReadOnlyList<TownServiceMerchantZone> Zones => _zones;
    private readonly List<Control> _controls = new();
    private readonly CanvasGroup _nativeGate;
    private readonly GameObject _nativeWrapper;
    private readonly Transform _nativeHome;
    private readonly int _nativeSibling;
    private float _nextCensus;
    private uint _lastWarmTurn;
    private int _lastWarmPage = -1, _lastWarmPageCount = -1;
    private bool _disposed, _allowInput, _observerDirty = true;
    private bool _observer;
    private readonly List<Canvas> _observerCanvases = new();
    private readonly List<Renderer> _observerRenderers = new();
    private readonly HashSet<Renderer> _bodyObserverRenderers = new();
    private object? _context;
    private TownServiceCatalogPreview? _preview;
    private Entry? _inspected;
    internal IReadOnlyList<Entry> Entries => _entries;
    internal IReadOnlyList<TownServiceToken> Samples => _samples;
    internal IReadOnlyList<Control> Controls => _controls;
    internal Transform Root => _root.transform;
    // Legacy transport call sites are retained until the integration switches to zone roots.
    internal Transform NavigationRoot => Root;
    internal Transform? PreviewSource => _preview?.Source;
    internal Transform? PreviewContent => _preview?.Content;
    internal Transform? PreviewCloneOf(Transform source) => _preview?.CloneOf(source);
    internal bool OwnsHintSource => _preview?.OwnsHintSource == true;
    internal Transform? HintSource => _preview?.HintSource;
    internal Transform? HintContent => _preview?.HintContent;
    internal Transform? HintCloneOf(Transform source) => _preview?.HintCloneOf(source);
    internal bool CanRelocate { get { foreach (var sample in _samples) if (sample.IsMoving) return false; return true; } }
    internal readonly struct Control
    {
        internal readonly string Key;
        internal readonly TownServiceSurface Surface;
        internal Control(string key, TownServiceSurface surface) { Key = key; Surface = surface; }
    }
    internal TownServiceCatalog(UIShopItemInventory inventory, Transform anchor,
        Func<object?> contextIdentity, Func<bool> alive, Transform mat, bool persistent = false)
    {
        _persistent=persistent; _inventory=inventory; _anchor=anchor; _mat=mat; _contextIdentity=contextIdentity; _alive=alive;
        _nativeHome=inventory.transform.parent; _nativeSibling=inventory.transform.GetSiblingIndex();
        try
        {
            _nativeWrapper=new GameObject("GloomhavenVR.Merchant.HiddenBackend",typeof(RectTransform),typeof(CanvasGroup));
            var rect=(RectTransform)_nativeWrapper.transform; rect.SetParent(_nativeHome,false);
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            if (!persistent) inventory.transform.SetParent(rect,false);
            _nativeGate=_nativeWrapper.GetComponent<CanvasGroup>();_nativeGate.alpha=0f;_nativeGate.blocksRaycasts=false;
            _root=new GameObject("GloomhavenVR.TownService.Catalog");Root.SetParent(anchor,false);
            _opening=_root.AddComponent<CanvasGroup>();_opening.alpha=0f;
            _backend=new TownServiceMerchantRows(inventory,persistent?Root:null);
            TMP_Text? font=inventory.GetComponentInChildren<TMP_Text>(true);

            _drawers.Add(new TownServiceMerchantDrawer(Root, 0, false, 0, "", font,
                () => !_disposed && _alive() && _allowInput && TownServicePublicMerchant.CanClaim,
                // A card parked in the merchant's palm is no longer on the rack. Keeping
                // that sample's movement in this interlock disabled every category button
                // and the crank throughout a buy decision, contrary to the physical scene.
                () => !_entries.Exists(entry => entry.Sample.IsMoving
                    && !TownServiceMerchantHandoff.IsParkedStock(entry.Sample)), drawer => ClearInspection()));
            for(int category=0;category<6;category++)
                _categories.Add(new TownServiceCatalogCategory(Root,category,_drawers[0],()=>_allowInput&&_alive()&&_drawers[0].Accessible&&TownServicePublicMerchant.CanClaim));
            if(!persistent&&inventory.itemTooltip!=null)_preview=new TownServiceCatalogPreview(inventory.itemTooltip,Root,()=>_inspected!=null&&_inspected.Current&&_inspected.Sample.IsHeld);
        }
        catch { Dispose(); throw; }
    }
    internal void SetVisibility(float value,float relocation=1f,bool allowInput=true)
    {
        _allowInput=allowInput&&relocation>=1f;
        float alpha = Mathf.Clamp01(value) * Mathf.Clamp01(relocation);
        if (_opening.alpha != alpha) _opening.alpha = alpha;
        if (_opening.interactable != _allowInput) _opening.interactable = _allowInput;
        if (_opening.blocksRaycasts) _opening.blocksRaycasts = false;
    }
    internal void LateTick()
    {
        Entry? held=null;
        foreach(var entry in _entries)if(entry.Current&&entry.Sample.IsHeld&&(held==null||entry.Sample.PickupSequence>held.Sample.PickupSequence))held=entry;
        if(held!=_inspected)
        {
            ClearInspection();_inspected=held;
            if(held!=null&&_preview!=null&&_inventory.itemTooltip!=null)
            {
                string? key=held.Selling?(!held.Item.Tradeable?"GUI_ITEM_CANNOT_BE_SOLD":null):(!held.RowSource.IsAvailable?"GUI_ITEM_SOLDOUT":null);
                string? information=key!=null?GLOOM.LocalizationManager.GetTranslation(key):null;
                _inventory.itemTooltip.Show(held.Item,(RectTransform)held.RowSource.transform,held.RowSource.Owner,
                    information,key!=null?CItem.EItemSlotState.Spent:(CItem.EItemSlotState?)null,held.Selling?null:_inventory.service);
                _preview.AttachTo(held.CardRoot.parent.parent);
            }
        }
        _preview?.Tick();
    }
    private void ClearInspection()
    {
        _preview?.Clear();
        if(_inspected!=null&&_inventory.itemTooltip!=null)_inventory.itemTooltip.Hide();
        _inspected=null;
    }
    internal void Tick(float scale)
    {
        if(_disposed)return;
        if(!_alive()||_inventory==null||_anchor==null){Dispose();return;}
        if(Time.unscaledTime>=_nextCensus)
        {
            _nextCensus=Time.unscaledTime+.5f;
            using (PerfMonitor.Scope("TownPublicStock.Catalog.Census")) RefreshRows();
        }
        using (PerfMonitor.Scope("TownPublicStock.Catalog.Navigation"))
        {
            foreach(var drawer in _drawers)
            {
                int maximum=-1; foreach(var entry in _entries) if(entry.Selling==drawer.Selling&&entry.Category==drawer.Category) maximum=Math.Max(maximum,entry.Ordinal);
                drawer.SetPageCount(maximum/TownServiceMerchantDrawer.Capacity+1); drawer.Tick(_opening.alpha);
            }
        }
        // The crank can admit a formerly cold page after the scheduled census,
        // including on the shutter's swap frame. Bring its native source current
        // before the card and row mirror sample that frame.
        TownServiceMerchantDrawer currentRack = _drawers[0];
        if (currentRack.TurnEpoch != _lastWarmTurn || currentRack.Page != _lastWarmPage
            || currentRack.PageCount != _lastWarmPageCount)
        {
            _lastWarmTurn = currentRack.TurnEpoch;
            _lastWarmPage = currentRack.Page;
            _lastWarmPageCount = currentRack.PageCount;
            if (_backend.HasPendingPresentation)
                foreach (Entry entry in _entries)
                    if (entry.Warm && _backend.NeedsPresentation(entry.RowSource))
                    { using (PerfMonitor.Scope("TownPublicStock.Catalog.Census")) RefreshRows(); break; }
        }
        using (PerfMonitor.Scope("TownPublicStock.Catalog.Cards"))
        {
            Camera? camera = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
            foreach(var entry in _entries)entry.Tick(scale, camera);
        }
        using (PerfMonitor.Scope("TownPublicStock.Catalog.Controls"))
        {
            foreach(var category in _categories)category.Tick(_opening.alpha);
            foreach(var zone in _zones)
            {
                bool shown=false;
                foreach(var entry in _entries)if(entry.Selling==zone.Selling&&entry.Sample.DropEligible){shown=true;break;}
                zone.SetShown(shown,_opening.alpha);
            }
        }
    }
    private void RefreshRows()
    {
        object? context=_contextIdentity();
        _warmSources.Clear();
        foreach (Entry entry in _entries) if (entry.Warm) _warmSources.Add(entry.RowSource);
        bool changed=_backend.Refresh(_warmSources);
        if(!ReferenceEquals(context,_context)){if(!_persistent)ClearEntries();_context=context;changed=true;}
        if(!changed)return;
        _observerDirty = true;
        var sources = new HashSet<UIShopItemSlot>();
        foreach (var row in _backend.Rows) sources.Add(row.Source);
        for(int i=_entries.Count-1;i>=0;i--)
        {
            Entry entry=_entries[i];
            if(!entry.Current||!sources.Contains(entry.RowSource))
            {if(_inspected==entry)ClearInspection();entry.Dispose();_samples.Remove(entry.Sample);_entries.RemoveAt(i);}
        }
        var indexed = new HashSet<UIShopItemSlot>();
        var occupied = new HashSet<(bool Selling, int Category, int Ordinal)>();
        foreach (Entry entry in _entries)
        { indexed.Add(entry.RowSource); occupied.Add((entry.Selling, entry.Category, entry.Ordinal)); }
        foreach(var row in _backend.Rows)
        {
            if(indexed.Contains(row.Source))continue;
            // Retain each surviving card's physical slot across native stock refreshes. An
            // unlock or another visitor's purchase must never rearrange the card in a hand.
            int category = CategoryOf(row.Item);
            int position=0;
            while(occupied.Contains((row.Selling,category,position)))position++;
            TownServiceMerchantDrawer rack = _drawers[0];
            Transform parent = rack.CardParent(position);
            Vector3 local = TownServiceMerchantLayout.StockPosition(position % TownServiceMerchantDrawer.Capacity);
            local.y = 0f; // The articulated holder row carries the original vertical slot.
            var added=new Entry(this,row.Source,position,row.Selling,parent,local);
            _entries.Add(added);_samples.Add(added.Sample);
            indexed.Add(row.Source); occupied.Add((row.Selling, category, position));
        }
        foreach (TownServiceMerchantDrawer rack in _drawers)
        {
            int maximum = -1;
            foreach (Entry entry in _entries) if (entry.Selling == rack.Selling && entry.Category == rack.Category) maximum = Math.Max(maximum, entry.Ordinal);
            rack.SetPageCount(maximum / TownServiceMerchantDrawer.Capacity + 1);
        }
    }
    internal static int CategoryOf(CItem item) => item.YMLData.Slot switch
    { CItem.EItemSlot.Head => 0, CItem.EItemSlot.Body => 1, CItem.EItemSlot.Legs => 2,
      CItem.EItemSlot.OneHand => 3, CItem.EItemSlot.TwoHand => 4, _ => 5 };
    internal static bool HeldOfferAvailable
    {
        get
        {
            foreach (Entry entry in CardMounts.Values)
                if (entry.Sample.IsHeld && entry.Current && (CanOffer?.Invoke(entry.Item, entry.Selling) ?? false)) return true;
            return false;
        }
    }
    internal static bool TryHeldOffer(Vector3 target, out Vector3 position, out VRHand? hand, out bool selling)
    {
        float best = float.PositiveInfinity;
        position = default; hand = null; selling = false;
        foreach (Entry entry in CardMounts.Values)
            if (entry.Sample.IsHeld && entry.Current && (CanOffer?.Invoke(entry.Item, entry.Selling) ?? false))
            {
                Vector3 point = entry.MountRoot.position;
                float distance = (point - target).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; position = point;
                hand = entry.Sample.HoldingHand; selling = entry.Selling;
            }
        return hand != null;
    }
    internal static Func<CItem, bool, bool>? CanOffer = null;
    internal static Func<CItem, bool, Vector3, bool>? Offer = null;
    internal static Func<Vector3, bool>? InOfferingZone = null;
    internal static Action<TownServiceToken>? RetainOffer = null;
    internal bool Eligible(Entry entry) => _allowInput && entry.Current
        && (CanOffer?.Invoke(entry.Item, entry.Selling) ?? false);
    internal bool Drop(Entry entry)
    {
        bool eligible = Eligible(entry);
        bool accepted = eligible && (Offer?.Invoke(entry.Item, entry.Selling, entry.MountRoot.position) ?? false);
        if (!accepted)
        {
            if (VRLog.WantsDebug)
                VRLog.Debug("TownServices", "Merchant cabinet card drop refused: eligible=" + eligible
                    + " input=" + _allowInput + " current=" + entry.Current
                    + " nativeAvailable=" + entry.RowSource.IsAvailable + " selling=" + entry.Selling);
            return false;
        }
        RetainOffer?.Invoke(entry.Sample);
        return true;
    }
    private void OnStockGrabbed(Entry entry)
    {
        // The native item card itself travels to the hand. Clear the cabinet-only
        // availability stamp synchronously with the grab, before its first held
        // frame; waiting for the next catalogue tick leaves a readable stamp in hand.
        entry.RefreshSoldOutMarker(false);
        TownServicePublicMerchant.Claim();
        if (!entry.Selling) TownServiceMerchantHandoff.StockInspected(entry.Item, entry.RowSource.IsAvailable);
    }
    internal void SetObserver(bool observer)
    {
        bool observerChanged = _observer != observer;
        bool observerCensus = _observerDirty;
        if (observerCensus)
        {
            Root.GetComponentsInChildren(true, _observerCanvases);
            Root.GetComponentsInChildren(true, _observerRenderers);
            _bodyObserverRenderers.Clear();
            foreach (Entry entry in _entries) entry.AddBodyRenderers(_bodyObserverRenderers);
            _observerDirty = false;
        }
        // Avoid repeating engine setter calls when the observer election and sampled
        // card output have not changed. Still inspect every member: another
        // presentation path may have changed it since the preceding tick.
        foreach (Canvas canvas in _observerCanvases)
            if (canvas != null && canvas.enabled == observer) canvas.enabled = !observer;
        foreach (Renderer renderer in _observerRenderers)
            if (renderer != null && !_bodyObserverRenderers.Contains(renderer)
                && renderer.forceRenderingOff != observer) renderer.forceRenderingOff = observer;
        // Entry.Tick already applies the page and current observer to every body
        // earlier in this frame. Repeat that pass only when the election changes
        // after Tick (including an immediate physical claim), or a row census added
        // bodies since the preceding observer pass. A stable cabinet formerly read
        // and compared every body renderer twice per frame for the same answer.
        if (observerChanged || observerCensus)
            foreach (Entry entry in _entries) entry.SetBodyRendererVisibility(observer || !entry.Exposed, true);
        _observer = observer;
        if (observer && !TownServicePublicMerchant.CanClaim)
            foreach (Entry entry in _entries) entry.Sample.PickCollider.enabled = false;
    }
    private void ClearEntries(){ClearInspection();foreach(var entry in _entries)entry.Dispose();_entries.Clear();_samples.Clear();foreach(var extension in _extensions)extension.Dispose();_extensions.Clear();}
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;foreach(var category in _categories)category.Dispose();_categories.Clear();ClearEntries();_preview?.Dispose();_preview=null;_backend?.Dispose();foreach(var drawer in _drawers)drawer.Dispose();_drawers.Clear();foreach(var zone in _zones)zone.Dispose();_zones.Clear();
        if(_inventory!=null&&_nativeWrapper!=null&&_inventory.transform.parent==_nativeWrapper.transform)
        {_inventory.transform.SetParent(_nativeHome,false);_inventory.transform.SetSiblingIndex(_nativeSibling);}
        if(_nativeWrapper!=null){_nativeWrapper.SetActive(false);UnityEngine.Object.Destroy(_nativeWrapper);}
        if(_root!=null){_root.SetActive(false);UnityEngine.Object.Destroy(_root);}
    }
    // The old template address may exist in a previous snapshot. New publishers omit it.
    internal static GameObject CreateNavigationTemplate(TMP_Text? font)=>new GameObject("RetiredCatalogNavigation");

    internal sealed class Entry : IDisposable
    {
        private readonly TownServiceCatalog _owner;
        private readonly GameObject _root;
        private readonly Canvas _canvas;
        private readonly CanvasGroup _pageGate;
        private readonly Transform _display;
        private readonly GameObject _soldOutBand;
        private readonly float _presentedAt;
        private Vector3 _displayHome;
        private Transform? _body;
        private Renderer[]? _bodyRenderers;
        private bool? _bodyHidden;
        internal Transform? BodyRoot => _body;
        internal void AddBodyRenderers(HashSet<Renderer> target)
        {
            if (_body == null) return;
            foreach (Renderer renderer in _bodyRenderers ??= _body.GetComponentsInChildren<Renderer>(true))
                if (renderer != null) target.Add(renderer);
        }
        internal void SetBodyRendererVisibility(bool hidden, bool recheck = false)
        {
            // This renderer belongs solely to the physical cabinet card. Only page
            // exposure and observer election write its forceRenderingOff state;
            // changing either invalidates this answer. Avoid querying every body
            // renderer on every frame of the large, fully occluded stock population.
            if (_body == null || (!recheck && _bodyHidden == hidden)) return;
            foreach (Renderer renderer in _bodyRenderers ??= _body.GetComponentsInChildren<Renderer>(true))
                if (renderer != null && renderer.forceRenderingOff != hidden) renderer.forceRenderingOff = hidden;
            _bodyHidden = hidden;
        }
        private readonly RemoteWidgetMirror _row;
        private readonly List<KeyValuePair<Graphic, bool>> _raycastTargets = new();
        private readonly List<KeyValuePair<GraphicRaycaster, bool>> _raycasters = new();
        private readonly List<Transform> _rowBackgrounds = new();
        private GameObject? _card;
        private float _nextRefresh;
        // The cabinet borrows the same native ItemCardUI as the scenario fan.
        // Use its exact zero-aliased-frame watcher as well: the native async
        // loader assigns a mipless sprite one frame before enabling the Image,
        // and a periodic bulk rescan alone necessarily exposes that frame.
        private readonly CardArtWatch _artWatch = new();
        private bool _disposed;
        internal readonly UIShopItemSlot RowSource;
        internal readonly CItem Item;
        internal readonly bool Selling;
        internal readonly int Ordinal;
        internal readonly int Category;
        private TownServiceMerchantDrawer Rack => _owner._drawers[0];
        internal bool Warm => Current && (Sample.IsMoving || Rack.RetainsPage(Page));
        internal readonly int Page;
        internal bool Exposed => Current && (Sample.IsMoving || Page == Rack.Page);
        internal readonly TownServiceToken Sample;
        internal ItemCardUI CardUI { get; private set; } = null!;
        internal Transform CardRoot => CardUI.transform;
        internal Transform MountRoot => _display;
        internal CanvasGroup PageGate => _pageGate;
        private readonly List<KeyValuePair<Canvas, bool>> _canvases = new();
        private bool _shown = true;
        internal Transform? RowContent => _row.CloneOf(RowSource.transform);
        internal Transform? RowCloneOf(Transform original) => _row.CloneOf(original);
        internal int ItemId => Item.ID;
        internal bool Current => !_disposed && _owner._alive() && RowSource != null
            && RowSource.gameObject.activeInHierarchy && ReferenceEquals(Item, RowSource.Item);

        internal Entry(TownServiceCatalog owner, UIShopItemSlot source, int position, bool selling, Transform parent, Vector3 local)
        {
            _owner = owner; RowSource = source; Item = source.Item; Selling = selling; Ordinal = position;
            Category = CategoryOf(Item);
            Page = (Selling ? 2048 : 0) + Category * 256 + Ordinal / TownServiceMerchantDrawer.Capacity;
            _root = new GameObject("CatalogItem");
            _pageGate = _root.AddComponent<CanvasGroup>(); _pageGate.blocksRaycasts = false;
            _root.transform.SetParent(parent, false);
            _root.transform.localPosition = local;
            _display = new GameObject("PhysicalCard").transform;
            _display.SetParent(_root.transform, false); CardMounts.Add(_display, this);
            _display.localRotation = Quaternion.Euler(TownServiceMerchantLayout.FacePitch, 0f, 0f);
            _presentedAt = Time.unscaledTime + Mathf.Min(position, 12) * .015f;
            var face = new GameObject("Face", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            face.transform.SetParent(_display, false);
            _canvas = face.GetComponent<Canvas>(); _canvas.renderMode = RenderMode.WorldSpace;
            VRLayers.Apply(face);
            var rowMount = new GameObject("Price"); rowMount.transform.SetParent(_display, false);
            rowMount.transform.localPosition = new Vector3(0f, -.053f, -.002f);
            rowMount.transform.localRotation = Quaternion.identity;
            _row = new RemoteWidgetMirror("CatalogPrice", rowMount.transform, .118f, .030f, Vector2.zero,
                externallyShownBranch: node => node.GetComponent<UIPartyItemInventoryTooltip>() != null, mrBacking: false);
            RectTransform rowRect = (RectTransform)source.transform;
            _row.SetOwnerFrame(rowRect.rect.size, rowRect.parent is RectTransform rowParent ? rowParent.rect.size : rowRect.rect.size);
            try
            {
                _card = ObjectPool.SpawnCard(Item.ID, ObjectPool.ECardType.Item, face.transform,
                    resetLocalScale: true, resetToMiddle: true, resetLocalRotation: true, activate: false);
                if (_card == null) throw new InvalidOperationException("Native merchant item card could not be borrowed");
                CardUI = _card.GetComponent<ItemCardUI>();
                if (CardUI == null) throw new InvalidOperationException("Native merchant item card component is missing");
                CardUI.item = Item;
                ItemBurnPlayback.ObserveInitialState(CardUI);
                CardUI.Show(highlightElement: false);
                CardFaceMipBake.Rescan(CardUI);
                _artWatch.Capture(CardUI);
                RectTransform rect = (RectTransform)_card.transform;
                Vector2 size = rect.rect.size;
                if (size.x < 1f || size.y < 1f) throw new InvalidOperationException("Native merchant item card has invalid dimensions");
                RectTransform host = (RectTransform)face.transform;
                host.sizeDelta = size;
                host.localScale = Vector3.one * Mathf.Min(TownServiceMerchantLayout.CardWidth / size.x, TownServiceMerchantLayout.CardHeight / size.y);
                host.localPosition = new Vector3(0f, 0f, -.0012f);
                _soldOutBand = new GameObject("OriginalStockSoldOut", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                RectTransform soldOutRect = (RectTransform)_soldOutBand.transform;
                soldOutRect.SetParent(face.transform, false);
                soldOutRect.sizeDelta = new Vector2(size.x * .92f, size.y * .18f);
                soldOutRect.localPosition = new Vector3(0f, 0f, -.003f);
                Image soldOutInk = _soldOutBand.GetComponent<Image>();
                soldOutInk.color = new Color(.19f, .035f, .055f, .94f);
                soldOutInk.raycastTarget = false;
                var soldOutLabel = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                soldOutLabel.transform.SetParent(soldOutRect, false);
                soldOutLabel.rectTransform.anchorMin = Vector2.zero;
                soldOutLabel.rectTransform.anchorMax = Vector2.one;
                soldOutLabel.rectTransform.offsetMin = soldOutLabel.rectTransform.offsetMax = Vector2.zero;
                TMP_Text? priceFont = source.GetComponentInChildren<TMP_Text>(true);
                if (priceFont != null) { soldOutLabel.font = priceFont.font; soldOutLabel.fontSharedMaterial = priceFont.fontSharedMaterial; }
                soldOutLabel.text = GLOOM.LocalizationManager.GetTranslation("GUI_ITEM_SOLDOUT");
                soldOutLabel.fontSize = 38f;
                soldOutLabel.enableAutoSizing = true;
                soldOutLabel.fontSizeMin = 22f;
                soldOutLabel.fontSizeMax = 38f;
                soldOutLabel.alignment = TextAlignmentOptions.Center;
                soldOutLabel.color = new Color(1f, .9f, .72f, 1f);
                soldOutLabel.raycastTarget = false;
                RefreshSoldOutMarker(true);
                Vector2 physicalSize = size * host.localScale.x;
                // With the cassette inside the carved cheeks, seat the native face
                // about 3 mm ahead of the imported leather backing. The former 15 mm
                // inset left the glass 18 mm in front of its own seat, so the whole
                // card plane still projected beyond the cabinet in a headset side view.
                _displayHome = new Vector3(0f, 0f, .030f);
                _display.localPosition = _displayHome + new Vector3(0f, 0f, .045f);
                _body = TownServiceCardBody.Create(_display).transform;
                _body.localScale = new Vector3(physicalSize.x, physicalSize.y, 1f);
                TownServiceCardBody.SetVisibility(_body.gameObject, owner._opening.alpha);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition3D = Vector3.zero; rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one;
                foreach (GraphicRaycaster raycaster in _card.GetComponentsInChildren<GraphicRaycaster>(true))
                { _raycasters.Add(new KeyValuePair<GraphicRaycaster, bool>(raycaster, raycaster.enabled)); raycaster.enabled = false; }
                // A card has one interaction owner: its physical collider. A transparent
                // clickable uGUI overlay used to veto near grabs and could never far-grab.
                foreach (Graphic graphic in _card.GetComponentsInChildren<Graphic>(true))
                { _raycastTargets.Add(new KeyValuePair<Graphic, bool>(graphic, graphic.raycastTarget)); graphic.raycastTarget = false; }
                face.GetComponent<GraphicRaycaster>().enabled=false;
                foreach (Canvas canvas in face.GetComponentsInChildren<Canvas>(true))
                    _canvases.Add(new KeyValuePair<Canvas, bool>(canvas, canvas.enabled));
                Sample = new TownServiceToken(rect, source.Selectable, () => source.Item,
                    owner._contextIdentity, () => Current, owner._mat, _display,
                    drop: () => owner.Drop(this), eligible: () => owner.Eligible(this),
                    zoneCenter: new Vector3(selling ? .078f : -.078f, .015f, -.20f),
                    inspect: () => owner._allowInput && Exposed && Rack.Accessible && TownServicePublicMerchant.CanClaim, zoneHalfWidth: .07f,
                    dropLocation: point => InOfferingZone?.Invoke(point) == true,
                    grabbing: () => owner.OnStockGrabbed(this));
                _row.Refresh(source.transform);
                foreach(RawImage background in source.GetComponentsInChildren<RawImage>(true))_rowBackgrounds.Add(background.transform);
                TownServiceNativeAssets.PrepareItem(CardUI);
            }
            catch { Dispose(); throw; }
        }

        internal void Tick(float scale, Camera? camera = null)
        {
            if (_disposed) return;
            if (!Current) { Sample.Dispose(); _root.SetActive(false); return; }
            // Current is a Unity hierarchy/item-identity query. The large public
            // cabinet used to repeat it in Exposed and Warm for every entry on
            // every frame, even for the fully occluded pages.
            bool moving = Sample.IsMoving;
            bool exposed = moving || Page == Rack.Page;
            bool warm = moving || Rack.RetainsPage(Page);
            float pageAlpha = exposed ? 1f : 0f;
            if (_pageGate.alpha != pageAlpha) _pageGate.alpha = pageAlpha;
            // Keep actual original content available for bounded hidden-page prewarming.
            // The page gate is explicit presentation state: it never suppresses native data,
            // changes a transaction, or uses a disabled ancestor Canvas invisible to capture.
            if (_shown != exposed) { _shown = exposed; _row.SetShown(true); }
            if (_body != null)
            {
                // The opaque cassette hides every other page. Its body has no
                // observable fade or texture while hidden; sample the current
                // source on the first exposed frame, before either the local render
                // or the shared presentation snapshot. This retains the opening
                // fade and late native artwork on every visible item without
                // polling two source materials on all hidden stock every frame.
                if (exposed) TownServiceCardBody.SetVisibility(_body.gameObject, _owner._opening.alpha);
                SetBodyRendererVisibility(!exposed || _owner._observer);
            }
            if (!warm) { Sample.PickCollider.enabled = false; return; }
            RefreshSoldOutMarker(!moving);
            if (!moving)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - _presentedAt) / .24f);
                float ease = t * t * (3f - 2f * t);
                Vector3 displayPosition = _displayHome + new Vector3(0f, 0f, .045f * (1f - ease));
                if (!_display.localPosition.Equals(displayPosition)) _display.localPosition = displayPosition;
            }
            camera ??= VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
            if (_canvas.worldCamera != camera) _canvas.worldCamera = camera;
            _artWatch.Poll("merchant cabinet item");
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + .25f;
                _row.Refresh(RowSource.transform);
                CardFaceMipBake.Rescan(CardUI);
                _artWatch.Capture(CardUI);
            }
            // Keep the native price/stock widget's intermediate animation on the
            // visible page. A prewarmed next page is fully obscured by its page
            // gate; its 27-node clone is refreshed on this same tick when it first
            // becomes exposed, so it never presents a stale frame to either peer.
            if (exposed) _row.TickLive();
            // Preserve original stock/price/name glyphs but remove the flat list's backing.
            foreach(Transform original in _rowBackgrounds)
            {Transform? clone=_row.CloneOf(original);if(clone!=null&&clone.gameObject.activeSelf)clone.gameObject.SetActive(false);}
            // The native detail widget follows the hovered row. It belongs to the full detail
            // placard, never inside this narrow price strip or its measured bounds.
            if (_owner._inventory.itemTooltip != null)
            {
                Transform? inline = _row.CloneOf(_owner._inventory.itemTooltip.transform);
                if (inline != null && inline.gameObject.activeSelf) inline.gameObject.SetActive(false);
            }
            if (exposed) Sample.Tick(scale); else Sample.PickCollider.enabled = false;
        }
        internal void RefreshSoldOutMarker(bool inCabinet)
        {
            bool shown = inCabinet && !Selling && !RowSource.IsAvailable;
            if (_soldOutBand.activeSelf != shown) _soldOutBand.SetActive(shown);
        }
        public void Dispose()
        {
            if (_disposed) return;
            // Cancel a gesture before recycling its source; no release callback is dispatched.
            Sample?.Dispose(); _disposed = true; CardMounts.Remove(_display);
            _row.Destroy(); UguiPokeSurfaces.Unregister(_canvas);
            if (_body != null) TownServiceCardBody.Dispose(_body.gameObject);
            // Pool borrowers after us must receive the same input flags we received. The
            // catalog's separate pointer surface is not a permanent edit to native card input.
            foreach (var canvas in _canvases) if (canvas.Key != null) canvas.Key.enabled = canvas.Value;
            foreach (var target in _raycastTargets) if (target.Key != null) target.Key.raycastTarget = target.Value;
            foreach (var raycaster in _raycasters) if (raycaster.Key != null) raycaster.Key.enabled = raycaster.Value;
            _artWatch.Clear();
            CardFaceMipBake.RestoreSprites(CardUI);
            if (_card != null) { RemoteItemCardSource.ReturnBorrowed(Item.ID, _card); _card = null; }
            UnityEngine.Object.Destroy(_root);
        }
    }
}
