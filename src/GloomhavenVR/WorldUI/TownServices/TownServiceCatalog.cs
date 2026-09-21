using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>A bounded rack of original item cards, backed by the native merchant's filtered
/// rows. The original controller retains prices, permissions, selection and confirmation.
/// An owned wrapper suppresses the old list; original controls move to reversible counter
/// surfaces. No native controller or payment state is replaced.</summary>
internal sealed class TownServiceCatalog : IDisposable
{
    internal const int PageSize = 6;
    private readonly UIShopItemInventory _inventory;
    private readonly Transform _anchor, _mat;
    private readonly Func<object?> _contextIdentity;
    private readonly Func<bool> _alive;
    private readonly ScrollRect _scroll;
    private readonly CanvasGroup _listGate;
    private readonly Transform _listHome;
    private readonly int _listSibling;
    private readonly GameObject _listWrapper;
    private readonly GameObject _root;
    private readonly CanvasGroup _opening;
    private readonly Canvas _navigation = null!;
    private readonly TMP_Text _pageLabel;
    private readonly Button _previous, _next;
    private readonly List<UIShopItemSlot> _rows = new();
    private readonly List<Entry> _entries = new();
    private readonly List<TownServiceToken> _samples = new();
    private readonly List<Control> _controls = new();
    internal IReadOnlyList<Control> Controls => _controls;
    internal readonly struct Control
    {
        internal readonly string Key;
        internal readonly TownServiceSurface Surface;
        internal Control(string key, TownServiceSurface surface) { Key = key; Surface = surface; }
    }
    private int _page;
    private object? _context;
    private float _nextCensus, _nextScrollPage, _lastScroll = 1f;
    private bool _disposed;
    internal IReadOnlyList<Entry> Entries => _entries;
    internal IReadOnlyList<TownServiceToken> Samples => _samples;
    internal Transform NavigationRoot => _navigation.transform;
    internal Transform Root => _root.transform;
    internal int Page => _page;
    internal int PageCount => Mathf.Max(1, (_rows.Count + PageSize - 1) / PageSize);

    internal TownServiceCatalog(UIShopItemInventory inventory, Transform anchor,
        Func<object?> contextIdentity, Func<bool> alive, Transform mat)
    {
        _inventory = inventory; _anchor = anchor; _mat = mat;
        _contextIdentity = contextIdentity; _alive = alive;
        _scroll = inventory.scroll;
        if (_scroll == null || _scroll.viewport == null)
            throw new InvalidOperationException("The original merchant scroll viewport is missing");
        // Unity permits only one CanvasGroup per object. Put our gate on an owned wrapper,
        // never overwrite a native group's live fade/permission fields. The wrapper has exactly
        // its parent's rect, so the unchanged viewport anchors still resolve to the same frame.
        _listHome = _scroll.viewport.parent; _listSibling = _scroll.viewport.GetSiblingIndex();
        _listWrapper = new GameObject("GloomhavenVR.Catalog.HiddenList", typeof(RectTransform));
        var wrapper = (RectTransform)_listWrapper.transform;
        wrapper.SetParent(_listHome, false); wrapper.SetSiblingIndex(_listSibling);
        wrapper.anchorMin = Vector2.zero; wrapper.anchorMax = Vector2.one;
        wrapper.offsetMin = wrapper.offsetMax = Vector2.zero;
        if (_listHome is RectTransform homeRect) wrapper.pivot = homeRect.pivot;
        _scroll.viewport.SetParent(wrapper, false);
        _listGate = _listWrapper.AddComponent<CanvasGroup>();
        _listGate.alpha = 0f; _listGate.blocksRaycasts = false;
        _root = new GameObject("GloomhavenVR.TownService.Catalog");
        Root.SetParent(anchor, false);
        _opening = _root.AddComponent<CanvasGroup>(); _opening.alpha = 0f;
        try
        {
            TMP_Text? font = inventory.GetComponentInChildren<TMP_Text>(true);
            GameObject navigation = CreateNavigationTemplate(font);
            navigation.transform.SetParent(Root, false);
            navigation.SetActive(true);
            _navigation = navigation.GetComponent<Canvas>();
            _pageLabel = navigation.transform.Find("Page").GetComponent<TMP_Text>();
            _previous = navigation.transform.Find("Previous").GetComponent<Button>();
            _next = navigation.transform.Find("Next").GetComponent<Button>();
            _previous.onClick.AddListener(() => TurnPage(-1));
            _next.onClick.AddListener(() => TurnPage(1));
            UguiPokeSurfaces.Register(_navigation);
            AddControl("merchant.buy", inventory.buyTab.transform, -.08f, .33f, .14f);
            AddControl("merchant.sell", inventory.sellTab.transform, .08f, .33f, .14f);
            AddControl("merchant.filter.all", inventory.allFilter.transform, -.24f, .265f, .055f);
            AddControl("merchant.filter.owned", inventory._ownedFilter.transform, -.16f, .265f, .055f);
            AddControl("merchant.filter.head", inventory.headFilter.transform, -.08f, .265f, .055f);
            AddControl("merchant.filter.body", inventory.bodyFilter.transform, 0f, .265f, .055f);
            AddControl("merchant.filter.hands", inventory.handsFilter.transform, .08f, .265f, .055f);
            AddControl("merchant.filter.legs", inventory.legsFilter.transform, .16f, .265f, .055f);
            AddControl("merchant.filter.small", inventory.smallItemsFilter.transform, .24f, .265f, .055f);
        }
        catch { Dispose(); throw; }
    }

    private void AddControl(string key, Transform source, float x, float z, float width)
    {
        if (source is not RectTransform rect) throw new InvalidOperationException("Native merchant control is not a RectTransform: " + key);
        _controls.Add(new Control(key, new TownServiceSurface((ushort)(20 + _controls.Count), rect,
            new Vector3(x, .008f, z), width, _anchor)));
    }

    internal void SetVisibility(float value)
    {
        _opening.alpha = Mathf.Clamp01(value);
        foreach (Control control in _controls) control.Surface.SetVisibility(value);
    }

    internal void LateTick()
    {
        foreach (Control control in _controls) control.Surface.Tick(Vector3.zero, Quaternion.identity, 1f);
    }

    /// <summary>Identical original visual source for observers, without service controllers or
    /// callbacks. Symbols and a numeric fraction need no language-dependent replacement text.</summary>
    internal static GameObject CreateNavigationTemplate(TMP_Text? font)
    {
        var go = new GameObject("CatalogNavigation", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        go.SetActive(false);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(600f, 56f);
        rect.localScale = Vector3.one * .001f;
        rect.localPosition = new Vector3(0f, .004f, -.235f);
        rect.localRotation = Quaternion.Euler(90f, 0f, 0f);
        AddLabel(rect, "Previous", "‹", -235f, true, font);
        AddLabel(rect, "Page", "1 / 1", 0f, false, font);
        AddLabel(rect, "Next", "›", 235f, true, font);
        VRLayers.Apply(go);
        return go;
    }

    private static void AddLabel(RectTransform parent, string name, string text, float x, bool button, TMP_Text? native)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(button ? 110f : 240f, 56f);
        rect.anchoredPosition = new Vector2(x, 0f);
        var label = go.AddComponent<TextMeshProUGUI>();
        if (native != null) { label.font = native.font; label.fontSharedMaterial = native.fontSharedMaterial; }
        label.text = text; label.fontSize = button ? 48f : 28f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(.95f, .89f, .69f, 1f);
        label.raycastTarget = button;
        if (button)
        {
            Button control = go.AddComponent<Button>(); control.targetGraphic = label;
            ColorBlock colors = control.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, .82f, .25f);
            colors.pressedColor = new Color(.8f, .6f, .12f); colors.disabledColor = new Color(.4f, .4f, .4f);
            control.colors = colors;
        }
    }

    internal void Tick(float scale)
    {
        if (_disposed) return;
        if (!_alive() || _inventory == null || _anchor == null) { Dispose(); return; }
        _navigation.worldCamera = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (Time.unscaledTime >= _nextCensus)
        {
            _nextCensus = Time.unscaledTime + .25f;
            RefreshRows();
        }
        float scroll = _scroll.verticalNormalizedPosition;
        if (!float.IsNaN(scroll) && Mathf.Abs(scroll - _lastScroll) > .001f)
        {
            _lastScroll = scroll;
            SetPage(Mathf.RoundToInt((1f - Mathf.Clamp01(scroll)) * (PageCount - 1)), false);
        }
        foreach (Entry entry in _entries) entry.Tick(scale);
    }

    private void RefreshRows()
    {
        var rows = new List<UIShopItemSlot>();
        foreach (UIShopItemSlot row in _inventory.slotPool)
            if (row != null && row.Item != null && row.Item.ID != 0 && row.gameObject.activeInHierarchy) rows.Add(row);
        rows.Sort((a, b) => CompareOrder(a.transform, b.transform));
        object? context = _contextIdentity();
        bool changed = !ReferenceEquals(context, _context) || rows.Count != _rows.Count;
        for (int i = 0; !changed && i < rows.Count; i++) changed = !ReferenceEquals(rows[i], _rows[i]);
        _rows.Clear(); _rows.AddRange(rows); _context = context;
        if (changed) SetPage(0, true, true);
        else if (_entries.Count == 0 && _rows.Count != 0) Rebuild();
        else
        {
            // Native pooled rows may be rebound without changing their GameObject or order.
            foreach (Entry entry in _entries)
                if (!ReferenceEquals(entry.Item, entry.RowSource.Item)) { Rebuild(); break; }
        }
    }

    private static int CompareOrder(Transform a, Transform b)
    {
        if (a == b) return 0;
        int ad = 0, bd = 0;
        for (Transform? t = a; t != null; t = t.parent) ad++;
        for (Transform? t = b; t != null; t = t.parent) bd++;
        while (ad > bd && a.parent != null) { a = a.parent; ad--; }
        while (bd > ad && b.parent != null) { b = b.parent; bd--; }
        while (a.parent != b.parent && a.parent != null && b.parent != null) { a = a.parent; b = b.parent; }
        return a.GetSiblingIndex().CompareTo(b.GetSiblingIndex());
    }

    private void ScrollPage(int direction)
    {
        // Thumbstick scroll is emitted each frame. Keep a single owner-level repeat timer;
        // rebuilding a page must not reset it and race through every card borrow in one gesture.
        if (Time.unscaledTime < _nextScrollPage) return;
        _nextScrollPage = Time.unscaledTime + .20f;
        TurnPage(direction);
    }

    internal void TurnPage(int direction)
    {
        if (!_disposed && _alive()) SetPage(_page + direction, true);
    }

    private void SetPage(int page, bool setScroll, bool force = false)
    {
        page = Mathf.Clamp(page, 0, PageCount - 1);
        bool changed = page != _page; _page = page;
        if (setScroll)
        {
            _scroll.StopMovement();
            _lastScroll = PageCount > 1 ? 1f - (float)_page / (PageCount - 1) : 1f;
            _scroll.verticalNormalizedPosition = _lastScroll;
        }
        if (changed || force) Rebuild();
    }

    private void Rebuild()
    {
        ClearEntries();
        try
        {
            for (int i = _page * PageSize; i < Mathf.Min(_rows.Count, (_page + 1) * PageSize); i++)
            {
                var entry = new Entry(this, _rows[i], i % PageSize);
                _entries.Add(entry); _samples.Add(entry.Sample);
            }
            _pageLabel.text = (_page + 1) + " / " + PageCount;
            _previous.interactable = _page > 0; _next.interactable = _page + 1 < PageCount;
        }
        catch { ClearEntries(); throw; }
    }

    private void ClearEntries()
    {
        foreach (Entry entry in _entries) entry.Dispose();
        _entries.Clear(); _samples.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearEntries();
        for (int i = _controls.Count - 1; i >= 0; i--) _controls[i].Surface.Dispose();
        _controls.Clear();
        if (_navigation != null) UguiPokeSurfaces.Unregister(_navigation);
        if (_scroll != null && _scroll.viewport != null && _listWrapper != null && _scroll.viewport.parent == _listWrapper.transform)
        {
            _scroll.viewport.SetParent(_listHome, false);
            _scroll.viewport.SetSiblingIndex(_listSibling);
        }
        // Detach before deferred destruction: same-frame reopening never finds an old gate.
        if (_listWrapper != null)
        { _listWrapper.SetActive(false); UnityEngine.Object.Destroy(_listWrapper); }
        UnityEngine.Object.Destroy(_root);
    }

    internal sealed class Entry : IDisposable
    {
        private readonly TownServiceCatalog _owner;
        private readonly GameObject _root;
        private readonly Canvas _canvas;
        private readonly RemoteWidgetMirror _row;
        private readonly List<KeyValuePair<Graphic, bool>> _raycastTargets = new();
        private readonly List<KeyValuePair<GraphicRaycaster, bool>> _raycasters = new();
        private GameObject? _card;
        private float _nextRefresh;
        private bool _disposed;
        internal readonly UIShopItemSlot RowSource;
        internal readonly CItem Item;
        internal readonly TownServiceToken Sample;
        internal ItemCardUI CardUI { get; private set; } = null!;
        internal Transform CardRoot => CardUI.transform;
        internal Transform? RowContent => _row.CloneOf(RowSource.transform);
        internal Transform? RowCloneOf(Transform original) => _row.CloneOf(original);
        internal int ItemId => Item.ID;
        internal bool Current => !_disposed && _owner._alive() && RowSource != null
            && RowSource.gameObject.activeInHierarchy && ReferenceEquals(Item, RowSource.Item);

        internal Entry(TownServiceCatalog owner, UIShopItemSlot source, int position)
        {
            _owner = owner; RowSource = source; Item = source.Item;
            _root = new GameObject("CatalogItem");
            _root.transform.SetParent(owner.Root, false);
            _root.transform.localPosition = new Vector3((position % 3 - 1) * .205f, .007f, position < 3 ? .105f : -.105f);
            _root.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var face = new GameObject("Face", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            face.transform.SetParent(_root.transform, false);
            _canvas = face.GetComponent<Canvas>(); _canvas.renderMode = RenderMode.WorldSpace;
            VRLayers.Apply(face);
            var rowMount = new GameObject("Price"); rowMount.transform.SetParent(_root.transform, false);
            rowMount.transform.localPosition = new Vector3(0f, -.081f, 0f);
            _row = new RemoteWidgetMirror("CatalogPrice", rowMount.transform, .19f, .042f, Vector2.zero);
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
                RectTransform rect = (RectTransform)_card.transform;
                Vector2 size = rect.rect.size;
                if (size.x < 1f || size.y < 1f) throw new InvalidOperationException("Native merchant item card has invalid dimensions");
                RectTransform host = (RectTransform)face.transform;
                host.sizeDelta = size;
                host.localScale = Vector3.one * Mathf.Min(.18f / size.x, .145f / size.y);
                host.localPosition = new Vector3(0f, .025f, -.001f);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition3D = Vector3.zero; rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one;
                foreach (GraphicRaycaster raycaster in _card.GetComponentsInChildren<GraphicRaycaster>(true))
                { _raycasters.Add(new KeyValuePair<GraphicRaycaster, bool>(raycaster, raycaster.enabled)); raycaster.enabled = false; }
                // A separate original-card face forwards input to the native row. Its own
                // presentation controller never gains service selection/payment callbacks.
                var pointer = face.AddComponent<TownServiceCatalogPointer>();
                pointer.Click = Click; pointer.Hover = Hover; pointer.Scroll = owner.ScrollPage;
                foreach (Graphic graphic in _card.GetComponentsInChildren<Graphic>(true))
                { _raycastTargets.Add(new KeyValuePair<Graphic, bool>(graphic, graphic.raycastTarget)); graphic.raycastTarget = false; }
                var hit = new GameObject("Input", typeof(RectTransform), typeof(Image));
                var hitRect = (RectTransform)hit.transform; hitRect.SetParent(face.transform, false);
                hitRect.sizeDelta = size; hitRect.localPosition = new Vector3(0f, 0f, -.001f);
                hit.GetComponent<Image>().color = Color.clear;
                UguiPokeSurfaces.Register(_canvas);
                Sample = new TownServiceToken(rect, source.Selectable, () => source.Item,
                    owner._contextIdentity, () => Current, owner._mat);
                _row.Refresh(source.transform);
                TownServiceNativeAssets.PrepareItem(CardUI);
            }
            catch { Dispose(); throw; }
        }

        private void Click()
        {
            if (!Current || !RowSource.Selectable.IsActive() || !RowSource.Selectable.IsInteractable() || EventSystem.current == null) return;
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(RowSource.Selectable.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        private void Hover(bool enter)
        {
            if (!Current || EventSystem.current == null) return;
            var pointer = new PointerEventData(EventSystem.current);
            if (enter) ExecuteEvents.Execute(RowSource.Selectable.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            else ExecuteEvents.Execute(RowSource.Selectable.gameObject, pointer, ExecuteEvents.pointerExitHandler);
        }
        internal void Tick(float scale)
        {
            if (_disposed) return;
            if (!Current) { Sample.Dispose(); _root.SetActive(false); return; }
            _canvas.worldCamera = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + .25f;
                _row.Refresh(RowSource.transform);
                CardFaceMipBake.Rescan(CardUI);
            }
            _row.TickLive(); Sample.Tick(scale);
        }
        public void Dispose()
        {
            if (_disposed) return;
            // Cancel a gesture before recycling its source; no release callback is dispatched.
            Sample?.Dispose(); Hover(false); _disposed = true;
            _row.Destroy(); UguiPokeSurfaces.Unregister(_canvas);
            // Pool borrowers after us must receive the same input flags we received. The
            // catalog's separate pointer surface is not a permanent edit to native card input.
            foreach (var target in _raycastTargets) if (target.Key != null) target.Key.raycastTarget = target.Value;
            foreach (var raycaster in _raycasters) if (raycaster.Key != null) raycaster.Key.enabled = raycaster.Value;
            if (_card != null) { RemoteItemCardSource.ReturnBorrowed(Item.ID, _card); _card = null; }
            UnityEngine.Object.Destroy(_root);
        }
    }
}
