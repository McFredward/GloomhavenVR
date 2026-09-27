using System;
using GloomhavenVR.WorldUI;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class InteractionProgram {
    private static int _checks;
    private static void Check(bool condition, string message) {
        _checks++;
        if (!condition) throw new Exception(message);
    }
    public static int Run() {
        _checks = 0;
        var root = new GameObject("Merchant transaction fixture");
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        var boxRoot = new GameObject("Native confirmation", typeof(UIWindow));
        boxRoot.transform.SetParent(root.transform, false);
        var box = new UIItemConfirmationBox { Window = boxRoot.GetComponent<UIWindow>() };
        box.confirmButton = new GameObject("Confirm", typeof(Button)).GetComponent<Button>();
        box.cancelButton = new GameObject("Cancel", typeof(Button)).GetComponent<Button>();
        Singleton<UIItemConfirmationBox>.Instance = box;
        var inventory = new UIShopItemInventory {
            itemsCanvasGroup = new GameObject("Inventory gate", typeof(CanvasGroup)).GetComponent<CanvasGroup>()
        };
        inventory.itemsCanvasGroup.interactable = true;
        var sale = new CItem { ID = 17 };
        var stock = new CItem { ID = 18 };
        inventory.service.Sell.Add(sale); inventory.service.Buy.Add(stock);
        var row = new GameObject("Native shop row", typeof(Button), typeof(UIShopItemSlot));
        row.transform.SetParent(root.transform, false);
        var slot = row.GetComponent<UIShopItemSlot>();
        slot.Selectable = row.GetComponent<Button>(); slot.Item = sale;
        slot.Selectable.onClick.AddListener(() => box.ShowConfirmation(slot.Item));
        inventory.slotPool.Add(slot);

        Check(TownServiceMerchantTransaction.Commit(inventory, sale, true, () => true)
            && box.IsActive && box.Window.IsOpen,
            "a real native sell row click opens its exact confirmation");
        Action? firstCallback = box._onConfirmedCallback;
        box.Window.Hide(); // UIWindowManager/Escape hides the window without updating IsActive.
        Check(box.IsActive && !box.Window.IsVisible,
            "fixture reproduces a closed window with a stale native active wrapper");
        Check(TownServiceMerchantTransaction.Commit(inventory, sale, true, () => true)
            && box.IsActive && box.Window.IsOpen
            && !ReferenceEquals(firstCallback, box._onConfirmedCallback),
            "a stale invisible native wrapper is reconciled and the next sell opens");

        Action? ownedCallback = box._onConfirmedCallback;
        Check(!TownServiceMerchantTransaction.Commit(inventory, stock, false, () => true)
            && ReferenceEquals(ownedCallback, box._onConfirmedCallback),
            "a genuinely visible prompt is never overwritten by another offer");
        box.Hide();
        box.Window.IsVisible = true; // Native fade-out has not completed.
        Check(!TownServiceMerchantTransaction.Commit(inventory, sale, true, () => true)
            && !box.IsActive && !box.Window.IsOpen,
            "an outgoing native fade is not interrupted by the next offer");
        box.Window.IsVisible = false;
        Check(TownServiceMerchantTransaction.Commit(inventory, sale, true, () => true),
            "the pending sell can open once the native fade completes");

        box.Hide(); box.Window.gameObject.SetActive(false);
        Check(!TownServiceMerchantTransaction.Commit(inventory, sale, true, () => true)
            && !box.IsActive,
            "an inactive native confirmation cannot capture an invisible sale");
        box.Window.gameObject.SetActive(true);

        bool refuseOnClick = true;
        slot.Selectable.onClick.RemoveAllListeners();
        slot.Selectable.onClick.AddListener(() => {
            if (refuseOnClick) box.Window.gameObject.SetActive(false);
            box.ShowConfirmation(slot.Item);
        });
        Check(!TownServiceMerchantTransaction.Commit(inventory, sale, true, () => true)
            && !box.IsActive && !box.Window.IsOpen,
            "a native Show refusal cannot leave IsActive latched with no buttons");
        box.Window.gameObject.SetActive(true); refuseOnClick = false;
        Check(TownServiceMerchantTransaction.Commit(inventory, sale, true, () => true),
            "the same item opens after a failed native Show attempt");

        box.Hide(); slot.Item = stock;
        Check(TownServiceMerchantTransaction.Commit(inventory, stock, false, () => true)
            && ReferenceEquals(box.Item, stock),
            "the recovered path also opens the exact stock buy decision");
        UnityEngine.Object.DestroyImmediate(root);
        return _checks;
    }
}
