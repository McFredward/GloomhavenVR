using System;
using FFSNet;
using System.Reflection;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine.EventSystems;

namespace GloomhavenVR.WorldUI;

/// <summary>The physical release replaces the native confirmation gesture, never its
/// transaction. Rebind the original inventory immediately before selecting and confirming.</summary>
internal static class TownServiceMerchantTransaction
{
    private static readonly PropertyInfo? Participant = AccessTools.Property(typeof(PlayerRegistry).Assembly.GetType("FFSNet.NetworkPlayer"), "IsParticipant");
    private static bool Participates()
    {
        if (!FFSNetwork.IsOnline) return true;
        object? player = PlayerRegistry.MyPlayer;
        return player != null && Participant?.GetValue(player, null) is true;
    }
    internal static bool Eligible(UIShopItemInventory inventory, CItem item, bool selling)
    {
        if (inventory == null || inventory.service == null || !inventory.itemsCanvasGroup.interactable
            || !Participates()) return false;
        if (!item.Tradeable) return false;
        if (selling) return inventory.service.GetItemsToSell(inventory.character).Contains(item);
        return inventory.service.IsAffordable(item, inventory.character)
            && inventory.service.GetItemsToBuy(inventory.character).Exists(candidate => candidate.ID == item.ID);
    }
    internal static bool Commit(UIShopItemInventory inventory, CItem item, bool selling, Func<bool> stillCurrent)
    {
        if (!stillCurrent() || !Eligible(inventory, item, selling) || EventSystem.current == null) return false;
        UIItemConfirmationBox confirmation = Singleton<UIItemConfirmationBox>.Instance;
        // Do not replace a confirmation that another input or player action already owns.
        if (confirmation == null || confirmation.IsActive) return false;
        if (selling) inventory.sellTab.isOn = true; else inventory.buyTab.isOn = true;
        inventory.RefreshView();
        inventory.FilterShownItems(ItemListingType.AllGear);
        if (!stillCurrent() || !Eligible(inventory, item, selling)) return false;
        UIShopItemSlot? selected = null;
        foreach (UIShopItemSlot row in inventory.slotPool)
        {
            if (row == null || !row.gameObject.activeInHierarchy || row.Item == null) continue;
            bool same = selling ? ReferenceEquals(row.Item, item) : row.Item.ID == item.ID;
            if (same && row.Selectable.IsActive() && row.Selectable.IsInteractable() && row.IsAvailable)
            { selected = row; break; }
        }
        if (selected == null) return false;
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(selected.Selectable.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        // The exact native item is the confirmation scope. Revalidate after the callback:
        // refusal, ownership changes and another input can never confirm a different item.
        if (!stillCurrent() || !Eligible(inventory, item, selling)
            || !confirmation.IsActive || !confirmation.IsConfirmingItem(selected.Item)
            || !confirmation.confirmButton.IsActive() || !confirmation.confirmButton.IsInteractable()) return false;
        ExecuteEvents.Execute(confirmation.confirmButton.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        return true;
    }
}
