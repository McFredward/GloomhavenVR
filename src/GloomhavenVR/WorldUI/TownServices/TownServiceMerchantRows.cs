using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GloomhavenVR.Core;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.Party;
using MapRuleLibrary.State;
using GLOOM;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Original row prefabs are used only as native price/stock/owner presenters. The
/// actual inventory remains the transaction backend, and is resolved afresh at the drop.</summary>
internal sealed class TownServiceMerchantRows : IDisposable
{
    private sealed class ItemReferenceComparer : IEqualityComparer<CItem>
    {
        internal static readonly ItemReferenceComparer Instance = new();
        public bool Equals(CItem? left, CItem? right) => ReferenceEquals(left, right);
        public int GetHashCode(CItem item) => RuntimeHelpers.GetHashCode(item);
    }
    internal sealed class Row
    {
        internal readonly UIShopItemSlot Source;
        internal readonly bool Selling;
        internal CItem Item => Source.Item;
        internal Row(UIShopItemSlot source, bool selling) { Source = source; Selling = selling; }
    }
    private readonly UIShopItemInventory _inventory;
    private IShopItemService? _publicService;
    private object? _publicParty;
    private readonly GameObject _root;
    internal readonly List<Row> Rows = new();
    // Public rows retain a complete original presentation bank. Initialize runs only
    // when an actual native input changes; unchanged dormant pages do no widget work.
    // Private transaction rows keep their existing current-page refresh policy.
    private readonly HashSet<UIShopItemSlot> _pendingPresentation = new();
    private readonly Dictionary<UIShopItemSlot, (CItem Item, int Cost, int Amount, int Total, bool Affordable, int Discount, CMapCharacter? Character)> _publicInputs = new();
    internal uint PresentationRevision { get; private set; }
    internal bool HasPendingPresentation => _pendingPresentation.Count != 0;
    internal bool NeedsPresentation(UIShopItemSlot source) => _pendingPresentation.Contains(source);
    internal TownServiceMerchantRows(UIShopItemInventory inventory, Transform? publicParent = null)
    {
        _inventory = inventory;
        if (publicParent != null)
        {
            _publicParty = AdventureState.MapState.MapParty;
            _publicService = new ShopService(AdventureState.MapState.MapParty, _ => { });
        }
        _root = new GameObject("GloomhavenVR.Merchant.PresentationRows", typeof(RectTransform), typeof(CanvasGroup));
        _root.transform.SetParent(publicParent != null ? publicParent : inventory.transform, false);
        CanvasGroup gate = _root.GetComponent<CanvasGroup>(); gate.alpha = 0f; gate.blocksRaycasts = false;
    }
    internal bool Refresh(HashSet<UIShopItemSlot> warmSources)
    {
        // Native ShopService retains its constructor party for owned items, while
        // its public stock comes from the current adventure. A network/map update
        // can replace that party without rebuilding our persistent presentation.
        // Rebind only that native backend; preserve every unchanged original row.
        if (_publicService != null && !ReferenceEquals(_publicParty, AdventureState.MapState.MapParty))
        {
            _publicParty = AdventureState.MapState.MapParty;
            _publicService = new ShopService(AdventureState.MapState.MapParty, _ => { });
        }
        var service = _publicService ?? _inventory.service;
        var character = _publicService != null ? NewPartyDisplayUI.PartyDisplay?.SelectedUISlot?.Data : _inventory.character;
        bool empty = _publicService == null && character == null && AdventureState.MapState.GoldMode == EGoldMode.CharacterGold;
        var buy = empty ? new List<CItem>() : service.GetItemsToBuy(_publicService != null ? null : character);
        var sell = empty || _publicService != null ? new List<CItem>() : service.GetItemsToSell(character);
        var all = empty ? buy : buy.Concat(service.GetItemsToSell()).ToList();
        if (_publicService == null && character != null) all = all.FindAll(item => item.CanEquipItem(character.CharacterID));
        var groups = all.GroupBy(item => item.ID).OrderBy(g => SlotOrder(g.First()))
            .ThenBy(g => _publicService != null ? g.Key : 0)
            .ThenBy(g => LocalizationManager.GetTranslation(g.First().Name)).ToList();
        var owned = sell.OrderBy(SlotOrder).ThenBy(item => LocalizationManager.GetTranslation(item.Name))
            .ThenBy(item => item.NetworkID).ToList();
        var bounds = service.GetBoundsItems(character);
        bool changed = Rows.Count != groups.Count + owned.Count;
        for (int i = 0; !changed && i < groups.Count; i++)
            changed = Rows[i].Selling || !ReferenceEquals(Rows[i].Item, groups[i].First());
        for (int i = 0; !changed && i < owned.Count; i++)
            changed = !Rows[groups.Count + i].Selling || !ReferenceEquals(Rows[groups.Count + i].Item, owned[i]);
        // The public cabinet is present even when the player is visiting another resident.
        // The Frame trace showed 15-33 ms catalog ticks during priestess/enchantress play.
        // Matching each old row with List.Find and counting the whole buy list for every
        // displayed item made this census quadratic. Preserve the exact first matching row
        // and order while looking each identity up once; native Initialize remains the sole
        // source of stock, affordability and price presentation.
        var previousBuy = new Dictionary<int, Queue<Row>>(Rows.Count);
        var previousOwned = new Dictionary<CItem, Queue<Row>>(ItemReferenceComparer.Instance);
        foreach (Row row in Rows)
        {
            if (row.Selling)
            {
                if (!previousOwned.TryGetValue(row.Item, out Queue<Row>? queue))
                    previousOwned[row.Item] = queue = new Queue<Row>();
                queue.Enqueue(row);
            }
            else
            {
                if (!previousBuy.TryGetValue(row.Item.ID, out Queue<Row>? queue))
                    previousBuy[row.Item.ID] = queue = new Queue<Row>();
                queue.Enqueue(row);
            }
        }
        var reused = new HashSet<Row>();
        var next = new List<Row>(groups.Count + owned.Count);
        foreach(var group in groups)
        {
            Row? row = previousBuy.TryGetValue(group.Key, out Queue<Row>? queue) && queue.Count > 0
                ? queue.Dequeue() : null;
            if (row == null) row = Create(false); else reused.Add(row);
            next.Add(row);
        }
        foreach(CItem item in owned)
        {
            Row? row = previousOwned.TryGetValue(item, out Queue<Row>? queue) && queue.Count > 0
                ? queue.Dequeue() : null;
            if (row == null) row = Create(true); else reused.Add(row);
            next.Add(row);
        }
        foreach (Row row in Rows) if (!reused.Contains(row))
        { _publicInputs.Remove(row.Source); row.Source.gameObject.SetActive(false); UnityEngine.Object.Destroy(row.Source.gameObject); }
        Rows.Clear();Rows.AddRange(next);
        _pendingPresentation.Clear();
        var buyAmounts = new Dictionary<int, int>(buy.Count);
        foreach (CItem item in buy)
        { buyAmounts.TryGetValue(item.ID, out int amount); buyAmounts[item.ID] = amount + 1; }
        using (PerfMonitor.Scope("TownPublicStock.Rows.Initialize"))
        {
            int index = 0;
            foreach (var group in groups)
            {
                CItem item = group.First();
                buyAmounts.TryGetValue(item.ID, out int amount);
                UIShopItemSlot source = Rows[index++].Source;
                int cost = service.DiscountedCost(item), discount = service.GetBuyDiscount();
                bool affordable = service.IsAffordable(item, character);
                var inputs = (item, cost, amount, group.Count(), affordable, discount, character);
                bool originalChanged = _publicService != null
                    && (!_publicInputs.TryGetValue(source, out var previous) || !previous.Equals(inputs));
                if (originalChanged) { _publicInputs[source] = inputs; PresentationRevision++; }
                // Always initialize a new or repurposed row: Entry.Current uses this
                // exact item reference to retire old physical cards after stock changes.
                // Warm rows keep the original half-second price, quantity and permission
                // cadence. Hidden rows are brought current before page exposure.
                if (!ReferenceEquals(source.Item, item) || originalChanged || _publicService == null && warmSources.Contains(source))
                    source.Initialize(item, cost, IgnoreSelect, IgnoreHover, null,
                        amount, group.Count(), affordable, false, false, discount, character);
                else if (_publicService == null) _pendingPresentation.Add(source);
            }
            foreach (CItem item in owned)
            {
                bool bound = bounds.TryGetValue(item, out var binding);
                UIShopItemSlot source = Rows[index++].Source;
                if (!ReferenceEquals(source.Item, item) || warmSources.Contains(source))
                    source.Initialize(item, item.SellPrice, IgnoreSelect, IgnoreHover, null,
                        bound ? binding!.Item1 : null, bound && binding!.Item2, character);
                else _pendingPresentation.Add(source);
            }
        }
        return changed;
    }
    private Row Create(bool selling)
    {
        UIShopItemSlot row = UnityEngine.Object.Instantiate(_inventory.slotPrefab, _root.transform);
        row.gameObject.SetActive(true);
        return new Row(row, selling);
    }
    private static int SlotOrder(CItem item) => item.YMLData.Slot switch
    { CItem.EItemSlot.Head => 0, CItem.EItemSlot.Body => 1, CItem.EItemSlot.OneHand => 2,
      CItem.EItemSlot.TwoHand => 2, CItem.EItemSlot.Legs => 3, _ => 4 };
    private static void IgnoreSelect(UIShopItemSlot row) { }
    private static void IgnoreHover(UIShopItemSlot row, bool hovered) { }
    private void Clear()
    {
        foreach (Row row in Rows)
            if (row.Source != null) { row.Source.gameObject.SetActive(false); UnityEngine.Object.Destroy(row.Source.gameObject); }
        Rows.Clear(); _pendingPresentation.Clear(); _publicInputs.Clear();
    }
    public void Dispose() { Clear(); UnityEngine.Object.Destroy(_root); }
}
