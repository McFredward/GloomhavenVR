using System;
using System.Collections.Generic;
using System.Linq;
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
    internal sealed class Row
    {
        internal readonly UIShopItemSlot Source;
        internal readonly bool Selling;
        internal CItem Item => Source.Item;
        internal Row(UIShopItemSlot source, bool selling) { Source = source; Selling = selling; }
    }
    private readonly UIShopItemInventory _inventory;
    private readonly GameObject _root;
    internal readonly List<Row> Rows = new();
    internal TownServiceMerchantRows(UIShopItemInventory inventory)
    {
        _inventory = inventory;
        _root = new GameObject("GloomhavenVR.Merchant.PresentationRows", typeof(RectTransform), typeof(CanvasGroup));
        _root.transform.SetParent(inventory.transform, false);
        CanvasGroup gate = _root.GetComponent<CanvasGroup>(); gate.alpha = 0f; gate.blocksRaycasts = false;
    }
    internal bool Refresh()
    {
        var service = _inventory.service;
        var character = _inventory.character;
        bool empty = character == null && AdventureState.MapState.GoldMode == EGoldMode.CharacterGold;
        var buy = empty ? new List<CItem>() : service.GetItemsToBuy(character);
        var sell = empty ? new List<CItem>() : service.GetItemsToSell(character);
        var all = empty ? buy : buy.Concat(service.GetItemsToSell()).ToList();
        if (character != null) all = all.FindAll(item => item.CanEquipItem(character.CharacterID));
        var groups = all.GroupBy(item => item.ID).OrderBy(g => SlotOrder(g.First()))
            .ThenBy(g => LocalizationManager.GetTranslation(g.First().Name)).ToList();
        var owned = sell.OrderBy(SlotOrder).ThenBy(item => LocalizationManager.GetTranslation(item.Name))
            .ThenBy(item => item.NetworkID).ToList();
        var bounds = service.GetBoundsItems(character);
        bool changed = Rows.Count != groups.Count + owned.Count;
        for (int i = 0; !changed && i < groups.Count; i++)
            changed = Rows[i].Selling || !ReferenceEquals(Rows[i].Item, groups[i].First());
        for (int i = 0; !changed && i < owned.Count; i++)
            changed = !Rows[groups.Count + i].Selling || !ReferenceEquals(Rows[groups.Count + i].Item, owned[i]);
        if (changed)
        {
            Clear();
            foreach (var group in groups) Rows.Add(Create(false));
            foreach (CItem item in owned) Rows.Add(Create(true));
        }
        int index = 0;
        foreach (var group in groups)
        {
            CItem item = group.First();
            int amount = buy.Count(candidate => candidate.ID == item.ID);
            Rows[index++].Source.Initialize(item, service.DiscountedCost(item), IgnoreSelect, IgnoreHover, null,
                amount, group.Count(), service.IsAffordable(item, character), false, false,
                service.GetBuyDiscount(), character);
        }
        foreach (CItem item in owned)
        {
            bool bound = bounds.TryGetValue(item, out var binding);
            Rows[index++].Source.Initialize(item, item.SellPrice, IgnoreSelect, IgnoreHover, null,
                bound ? binding!.Item1 : null, bound && binding!.Item2, character);
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
        foreach (Row row in Rows) { row.Source.gameObject.SetActive(false); UnityEngine.Object.Destroy(row.Source.gameObject); }
        Rows.Clear();
    }
    public void Dispose() { Clear(); UnityEngine.Object.Destroy(_root); }
}
