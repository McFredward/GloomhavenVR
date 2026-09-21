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

/// <summary>Complete native merchant stock and owned inventory in physical filing racks.
/// No page/filter controls and no hidden count cap. Only an explicit eligible zone drop
/// enters the original native transaction; picking up a card is always inspection.</summary>
internal sealed class TownServiceCatalog : IDisposable
{
    private readonly UIShopItemInventory _inventory;
    private readonly Transform _anchor, _mat;
    private readonly Func<object?> _contextIdentity;
    private readonly Func<bool> _alive;
    private readonly GameObject _root;
    private readonly CanvasGroup _opening;
    private readonly TownServiceMerchantRows _backend;
    private readonly List<Entry> _entries = new();
    private readonly List<TownServiceToken> _samples = new();
    private readonly List<Control> _controls = new();
    private readonly CanvasGroup _nativeGate;
    private readonly GameObject _nativeWrapper;
    private readonly Transform _nativeHome;
    private readonly int _nativeSibling;
    private float _nextCensus;
    private bool _disposed, _allowInput;
    private object? _context;
    internal IReadOnlyList<Entry> Entries => _entries;
    internal IReadOnlyList<TownServiceToken> Samples => _samples;
    internal IReadOnlyList<Control> Controls => _controls;
    internal Transform Root => _root.transform;
    // Legacy transport call sites are retained until the integration switches to zone roots.
    internal Transform NavigationRoot => Root;
    internal Transform? PreviewSource => null;
    internal Transform? PreviewContent => null;
    internal Transform? PreviewCloneOf(Transform source) => null;
    internal bool OwnsHintSource => false;
    internal Transform? HintSource => null;
    internal Transform? HintContent => null;
    internal Transform? HintCloneOf(Transform source) => null;
    internal bool CanRelocate { get { foreach (var sample in _samples) if (sample.IsMoving) return false; return true; } }
    internal readonly struct Control
    {
        internal readonly string Key;
        internal readonly TownServiceSurface Surface;
        internal Control(string key, TownServiceSurface surface) { Key = key; Surface = surface; }
    }
    internal TownServiceCatalog(UIShopItemInventory inventory, Transform anchor,
        Func<object?> contextIdentity, Func<bool> alive, Transform mat)
    {
        _inventory=inventory; _anchor=anchor; _mat=mat; _contextIdentity=contextIdentity; _alive=alive;
        _nativeHome=inventory.transform.parent; _nativeSibling=inventory.transform.GetSiblingIndex();
        _nativeWrapper=new GameObject("GloomhavenVR.Merchant.HiddenBackend",typeof(RectTransform),typeof(CanvasGroup));
        var rect=(RectTransform)_nativeWrapper.transform; rect.SetParent(_nativeHome,false);
        rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        inventory.transform.SetParent(rect,false);
        _nativeGate=_nativeWrapper.GetComponent<CanvasGroup>();_nativeGate.alpha=0f;_nativeGate.blocksRaycasts=false;
        _root=new GameObject("GloomhavenVR.TownService.Catalog");Root.SetParent(anchor,false);
        _opening=_root.AddComponent<CanvasGroup>();_opening.alpha=0f;
        _backend=new TownServiceMerchantRows(inventory);
    }
    internal void SetVisibility(float value,float relocation=1f,bool allowInput=true)
    {
        _allowInput=allowInput&&relocation>=1f;
        _opening.alpha=Mathf.Clamp01(value)*Mathf.Clamp01(relocation);
        _opening.interactable=_allowInput;_opening.blocksRaycasts=false;
    }
    internal void LateTick() { }
    internal void Tick(float scale)
    {
        if(_disposed)return;
        if(!_alive()||_inventory==null||_anchor==null){Dispose();return;}
        if(Time.unscaledTime>=_nextCensus){_nextCensus=Time.unscaledTime+.5f;RefreshRows();}
        foreach(var entry in _entries)entry.Tick(scale);
    }
    private void RefreshRows()
    {
        object? context=_contextIdentity();
        bool changed=_backend.Refresh();
        if(changed||!ReferenceEquals(context,_context))
        {
            ClearEntries();_context=context;
            int buy=0,sell=0;
            foreach(var row in _backend.Rows)
            {
                var entry=new Entry(this,row.Source,row.Selling?sell++:buy++,row.Selling);
                _entries.Add(entry);_samples.Add(entry.Sample);
            }
        }
    }
    internal static Vector3 RackPosition(int ordinal,bool selling)
    {
        // Twelve exposed card edges per slanted lane. New lanes extend outwards rather
        // than replacing existing entries or stranding stock behind a page control.
        int lane=ordinal/12,depth=ordinal%12;
        return new Vector3((selling?1f:-1f)*(.30f+lane*.205f),depth*.022f,.10f+depth*.045f);
    }
    internal bool Eligible(Entry entry)=>_allowInput&&entry.Current
        &&TownServiceMerchantTransaction.Eligible(_inventory,entry.Item,entry.Selling);
    internal bool Drop(Entry entry)
    {
        if(!Eligible(entry))return false;
        object? context=_contextIdentity();
        return TownServiceMerchantTransaction.Commit(_inventory,entry.Item,entry.Selling,
            ()=>entry.Current&&_allowInput&&ReferenceEquals(context,_contextIdentity()));
    }
    private void ClearEntries(){foreach(var entry in _entries)entry.Dispose();_entries.Clear();_samples.Clear();}
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;ClearEntries();_backend.Dispose();
        if(_inventory!=null&&_inventory.transform.parent==_nativeWrapper.transform)
        {_inventory.transform.SetParent(_nativeHome,false);_inventory.transform.SetSiblingIndex(_nativeSibling);}
        _nativeWrapper.SetActive(false);UnityEngine.Object.Destroy(_nativeWrapper);UnityEngine.Object.Destroy(_root);
    }
    // The old template address may exist in a previous snapshot. New publishers omit it.
    internal static GameObject CreateNavigationTemplate(TMP_Text? font)=>new GameObject("RetiredCatalogNavigation");

    internal sealed class Entry : IDisposable
    {
        private readonly TownServiceCatalog _owner;
        private readonly GameObject _root;
        private readonly Canvas _canvas;
        private readonly Transform _display;
        private readonly float _presentedAt;
        private Vector3 _displayHome;
        private Transform? _body;
        internal Transform? BodyRoot => _body;
        private readonly RemoteWidgetMirror _row;
        private readonly List<KeyValuePair<Graphic, bool>> _raycastTargets = new();
        private readonly List<KeyValuePair<GraphicRaycaster, bool>> _raycasters = new();
        private GameObject? _card;
        private float _nextRefresh;
        private bool _disposed;
        internal readonly UIShopItemSlot RowSource;
        internal readonly CItem Item;
        internal readonly bool Selling;
        internal readonly int Ordinal;
        internal readonly TownServiceToken Sample;
        internal ItemCardUI CardUI { get; private set; } = null!;
        internal Transform CardRoot => CardUI.transform;
        internal Transform? RowContent => _row.CloneOf(RowSource.transform);
        internal Transform? RowCloneOf(Transform original) => _row.CloneOf(original);
        internal int ItemId => Item.ID;
        internal bool Current => !_disposed && _owner._alive() && RowSource != null
            && RowSource.gameObject.activeInHierarchy && ReferenceEquals(Item, RowSource.Item);

        internal Entry(TownServiceCatalog owner, UIShopItemSlot source, int position, bool selling)
        {
            _owner = owner; RowSource = source; Item = source.Item; Selling = selling; Ordinal = position;
            _root = new GameObject("CatalogItem");
            _root.transform.SetParent(owner.Root, false);
            _root.transform.localPosition = RackPosition(position, selling);
            _display = new GameObject("PhysicalCard").transform;
            _display.SetParent(_root.transform, false);
            _display.localRotation = Quaternion.Euler(65f, 0f, 0f);
            _presentedAt = Time.unscaledTime + Mathf.Min(position, 12) * .015f;
            var face = new GameObject("Face", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            face.transform.SetParent(_display, false);
            _canvas = face.GetComponent<Canvas>(); _canvas.renderMode = RenderMode.WorldSpace;
            VRLayers.Apply(face);
            var rowMount = new GameObject("Price"); rowMount.transform.SetParent(_root.transform, false);
            rowMount.transform.localPosition = new Vector3(0f, .005f, -.077f);
            rowMount.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _row = new RemoteWidgetMirror("CatalogPrice", rowMount.transform, .19f, .042f, Vector2.zero,
                externallyShownBranch: node => node.GetComponent<UIPartyItemInventoryTooltip>() != null);
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
                host.localPosition = new Vector3(0f, 0f, -.0012f);
                Vector2 physicalSize = size * host.localScale.x;
                _displayHome = new Vector3(0f, physicalSize.y * .5f * Mathf.Cos(65f * Mathf.Deg2Rad) + .006f, 0f);
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
                Sample = new TownServiceToken(rect, source.Selectable, () => source.Item,
                    owner._contextIdentity, () => Current, owner._mat, _display,
                    drop: () => owner.Drop(this), eligible: () => owner.Eligible(this),
                    zoneCenter: new Vector3(selling ? .27f : -.27f, .015f, -.20f));
                _row.Refresh(source.transform);
                TownServiceNativeAssets.PrepareItem(CardUI);
            }
            catch { Dispose(); throw; }
        }

        internal void Tick(float scale)
        {
            if (_disposed) return;
            if (!Current) { Sample.Dispose(); _root.SetActive(false); return; }
            if (!Sample.IsMoving)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - _presentedAt) / .24f);
                float ease = t * t * (3f - 2f * t);
                _display.localPosition = _displayHome + new Vector3(0f, 0f, .045f * (1f - ease));
            }
            _canvas.worldCamera = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + .25f;
                _row.Refresh(RowSource.transform);
                CardFaceMipBake.Rescan(CardUI);
            }
            if (_body != null) TownServiceCardBody.SetVisibility(_body.gameObject, _owner._opening.alpha);
            _row.TickLive();
            // The native detail widget follows the hovered row. It belongs to the full detail
            // placard, never inside this narrow price strip or its measured bounds.
            if (_owner._inventory.itemTooltip != null)
            {
                Transform? inline = _row.CloneOf(_owner._inventory.itemTooltip.transform);
                if (inline != null) inline.gameObject.SetActive(false);
            }
            Sample.Tick(scale);
        }
        public void Dispose()
        {
            if (_disposed) return;
            // Cancel a gesture before recycling its source; no release callback is dispatched.
            Sample?.Dispose(); _disposed = true;
            _row.Destroy(); UguiPokeSurfaces.Unregister(_canvas);
            if (_body != null) TownServiceCardBody.Dispose(_body.gameObject);
            // Pool borrowers after us must receive the same input flags we received. The
            // catalog's separate pointer surface is not a permanent edit to native card input.
            foreach (var target in _raycastTargets) if (target.Key != null) target.Key.raycastTarget = target.Value;
            foreach (var raycaster in _raycasters) if (raycaster.Key != null) raycaster.Key.enabled = raycaster.Value;
            if (_card != null) { RemoteItemCardSource.ReturnBorrowed(Item.ID, _card); _card = null; }
            UnityEngine.Object.Destroy(_root);
        }
    }
}
