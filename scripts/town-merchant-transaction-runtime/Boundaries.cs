using System;
using System.Collections.Generic;
using System.Reflection;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace HarmonyLib {
    public static class AccessTools {
        public static PropertyInfo? Property(Type? type, string name) => type?.GetProperty(name);
    }
}
namespace FFSNet {
    public static class FFSNetwork { public static bool IsOnline; }
    public sealed class NetworkPlayer { public bool IsParticipant { get; set; } = true; }
    public static class PlayerRegistry { public static NetworkPlayer? MyPlayer = new(); }
}
namespace ScenarioRuleLibrary {
    public sealed class CItem { public int ID; public bool Tradeable = true; }
}
namespace GloomhavenVR.Core {
    internal static class VRLog {
        internal static bool WantsDebug = false;
        internal static void Debug(string scope, string message) { }
    }
}
public sealed class Singleton<T> { public static T? Instance; }
public enum ItemListingType { AllGear }
public sealed class ShopService {
    public readonly List<CItem> Buy = new();
    public readonly List<CItem> Sell = new();
    public bool IsAffordable(CItem item, object? character) => true;
    public List<CItem> GetItemsToBuy(object? character) => Buy;
    public List<CItem> GetItemsToSell(object? character) => Sell;
}
public sealed class Tab { public bool isOn; }
public sealed class UIShopItemInventory {
    public ShopService service = new();
    public object character = new();
    public CanvasGroup itemsCanvasGroup = null!;
    public Tab sellTab = new(), buyTab = new();
    public readonly List<UIShopItemSlot> slotPool = new();
    public void RefreshView() { }
    public void FilterShownItems(ItemListingType type) { }
}
public sealed class UIShopItemSlot : MonoBehaviour {
    public CItem Item = null!;
    public Button Selectable = null!;
    public bool IsAvailable = true;
}
public sealed class UIWindow : MonoBehaviour {
    public bool IsOpen, IsVisible;
    public void Show() { if (gameObject.activeInHierarchy) IsOpen = IsVisible = true; }
    public void Hide() { IsOpen = IsVisible = false; }
}
public sealed class UIItemConfirmationBox {
    public bool IsActive;
    public Action? _onConfirmedCallback;
    public Button confirmButton = null!, cancelButton = null!;
    public UIWindow Window = null!;
    public CItem? Item;
    public T GetComponent<T>() where T : class => (T)(object)Window;
    public bool IsConfirmingItem(CItem item) => Window.IsOpen && ReferenceEquals(Item, item);
    public void ShowConfirmation(CItem item) {
        IsActive = true; Item = item; _onConfirmedCallback = () => { _ = item.ID; };
        Window.Show();
    }
    public void Hide() { Window.Hide(); IsActive = false; }
    public void OnCancel() { Hide(); }
}
