using System;
using FFSNet;
using System.Reflection;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine.EventSystems;

namespace GloomhavenVR.WorldUI;

/// <summary>The physical release requests the original native confirmation. Rebind the
/// inventory before selecting; only the player's final confirmation authorizes the transaction.</summary>
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
        CItem selectedItem = selected.Item;
        Action? previous = confirmation._onConfirmedCallback;
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(selected.Selectable.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        // The exact native item is the confirmation scope. Revalidate after the callback:
        // refusal, ownership changes and another input can never confirm a different item.
        bool created = confirmation.IsActive && confirmation.IsConfirmingItem(selectedItem)
            && confirmation._onConfirmedCallback != null
            && !ReferenceEquals(previous, confirmation._onConfirmedCallback);

        if (!stillCurrent() || !Eligible(inventory, item, selling)
            || !created || !confirmation.confirmButton.IsActive() || !confirmation.confirmButton.IsInteractable())
        {
            // Use the native cancellation transition only for the prompt this selection
            // just installed. A rejected gesture must not leave a stale purchase available.
            if (created) confirmation.OnCancel();
            return false;
        }
        // The player makes the final purchase/sale decision in the original native prompt.
        // Offering a card must never silently confirm the transaction.
        return true;
    }
}
