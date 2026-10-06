// Native game model/callback boundaries. The entire production quiet controller,
// reflection dispatch, source mask, RectTransform topology and rollback run unchanged
// against real Unity. Counters stand in for native payments/save/UI controllers;
// this fixture deliberately never claims to prove those boundary implementations.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using MapRuleLibrary.Party;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) {} }
    public static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public static FieldInfo? Field(Type type, string name) => type.GetField(name, Flags);
        public static MethodInfo? Method(Type type, string name) => type.GetMethod(name, Flags);
        public static PropertyInfo? Property(Type type, string name) => type.GetProperty(name, Flags);
    }
}
namespace FFSNet
{
    public delegate void ControllerChangedEvent();
    public static class ControllableRegistry
    {
        private static ControllerChangedEvent? _changed;
        public static int Listeners;
        public static event ControllerChangedEvent OnControllerChanged { add { _changed += value; Listeners++; } remove { _changed -= value; Listeners--; } }
        public static void Emit() => _changed?.Invoke();
    }
}
namespace GloomhavenVR.Core { public static class VRLog { public static bool WantsDebug;public static int Warnings; public static void Info(string scope,string message) {} public static void Warn(string scope,string message){Warnings++;} } }
namespace ScenarioRuleLibrary
{
    public sealed class CAbilityCard { public int ID; public CAbilityCard(int id) { ID=id; } }
    public sealed class EnhancementToken { public int CardID; }
    public class GameAction { public object? SupplementaryDataToken {get;set;} }
}
namespace MapRuleLibrary.Party
{
    public sealed class CMapCharacter
    {
        public string CharacterID="owned";public bool IsUnderMyControl=true;
        public int FreePoints=3;
        public List<CAbilityCard> Cards=new();
        public int GetFreeEnhancementSlots()=>FreePoints;
        public List<CAbilityCard> GetOwnedAbilityCards()=>Cards;
    }
}
namespace MapRuleLibrary.Adventure
{
    public sealed class MapParty { public bool SellAvailable=true; }
    public sealed class Headquarters { public int EnhancementSlots=5; }
    public sealed class MapState { public MapParty MapParty=new(); public Headquarters HeadquartersState=new(); }
    public static class AdventureState { public static MapState MapState=new(); }
    public sealed class ShopService
    {
        public readonly MapParty Party; public readonly Action<object> Notify;
        public Action? Removed; public int RegisterRemoved;
        public ShopService(MapParty party, Action<object> notify) { Party=party;Notify=notify; }
        public void RegisterOnRemovedNewItemFlag(Action removed) { Removed=removed; RegisterRemoved++; }
    }
    public sealed class MapPartyEnhancementShopService
    {
        private readonly MapParty party=AdventureState.MapState.MapParty;
        public MapParty Party=>party; public int Enters;
        public bool IsSellAvailable=>party.SellAvailable;
        public void OnEnterShop(){Enters++;}
    }
}
public enum EGuildmasterMode { None,Merchant,Enchantress,Temple,Other }
public enum PartySlotState { Empty,Assigned }
public class UIWindow : MonoBehaviour { public bool IsOpen,IsVisible; public int Shows; public void Show(){IsOpen=IsVisible=true;Shows++;} }
public class UIItemConfirmationBox : MonoBehaviour {}
public class UIEnhancementConfirmationBox : MonoBehaviour {}
public sealed class NewPartyCharacterUI { public PartySlotState State=PartySlotState.Assigned; public CMapCharacter? Service; public int Clicks; public void OnClick(){Clicks++;} }
public sealed class NewPartyDisplayUI { public static NewPartyDisplayUI? PartyDisplay=new(); public NewPartyCharacterUI? SelectedUISlot; }
public class UIShopItemInventory : MonoBehaviour
{
    public int Inits,NewFlagRefresh; public ShopService? Service; public CMapCharacter? Character;
    public bool ThrowInit;
    public void Init(ShopService service,CMapCharacter character){Inits++;if(ThrowInit)throw new Exception("native fixture initialization failed");Service=service;Character=character;}
    public void RefreshSellNewItemNotification(){NewFlagRefresh++;}
    public void MPBuyItem(){} public void MPSellItem(){}
}
public sealed class NativeBus
{
    private Action? bound,gold,equipped,unbound,unequipped;
    public int Registrations,Removals;
    public int Count=>(bound?.GetInvocationList().Length??0)+(gold?.GetInvocationList().Length??0)+(equipped?.GetInvocationList().Length??0)+(unbound?.GetInvocationList().Length??0)+(unequipped?.GetInvocationList().Length??0);
    public void RegisterToOnCharacterItemBound(Action callback){bound+=callback;Registrations++;}
    public void RegisterToOnGoldChanged(Action callback){gold+=callback;Registrations++;}
    public void RegisterToOnCharacterItemEquipped(Action callback){equipped+=callback;Registrations++;}
    public void RegisterToOnCharacterItemUnbound(Action callback){unbound+=callback;Registrations++;}
    public void RegisterToOnCharacterItemUnequipped(Action callback){unequipped+=callback;Registrations++;}
    public void Remove(Action a,Action b,Action c,Action d,Action e){bound-=a;gold-=b;equipped-=c;unbound-=d;unequipped-=e;Removals++;}
    public void Emit(){bound?.Invoke();gold?.Invoke();equipped?.Invoke();unbound?.Invoke();unequipped?.Invoke();}
}
public sealed class MapChoreographer { public NativeBus EventBuss=new(); }
public static class Singleton<T> where T:new() { public static T Instance=new(); }
public class UIShopItemWindow : MonoBehaviour
{
    private ShopService? service;
    public ShopService? Service=>service;
    public UIShopItemInventory ItemInventory=null!;
    public Action<object>? OnUpdateNewPartyItemNotification;
    public int Clears,Notifications,Bound,Gold,Equipped,Unbound,Unequipped;public bool ThrowClear;
    private void ClearEvents(){Clears++;Singleton<MapChoreographer>.Instance.EventBuss.Remove(OnItemBound,OnGoldUpdate,OnItemEquipped,OnItemUnbound,OnItemUnequipped);if(ThrowClear)throw new Exception("native fixture teardown failed");}
    private void OnItemBound(){Bound++;} private void OnGoldUpdate(){Gold++;}
    private void OnItemEquipped(){Equipped++;} private void OnItemUnbound(){Unbound++;} private void OnItemUnequipped(){Unequipped++;}
}
public class AbilityCardUI : MonoBehaviour { public CAbilityCard AbilityCard=null!; }
public class UIEnhanceCardSlot : MonoBehaviour
{
    public CAbilityCard? Card; public CMapCharacter? Character; public bool LastCanSelect; public int Inits;
    public Action<AbilityCardUI>? Selected;
    public void Init(CAbilityCard card,CMapCharacter character,Action<AbilityCardUI> callback,object? unused,bool canSelect){Inits++;Card=card;Character=character;Selected=callback;LastCanSelect=canSelect;}
}
public class NativeCardsPanel { public RectTransform content=null!; }
public class UIPartyCharacterEnhancementAbilityCardsDisplay : MonoBehaviour
{
    private CMapCharacter? characterData;
    private Action<AbilityCardUI>? onAbilityCardSelected;
    private readonly Dictionary<CAbilityCard,UIEnhanceCardSlot> assignedSlots=new();
    private UIEnhanceCardSlot slotPrefab=null!;
    private AbilityCardUI? selectedCard;
    public List<UIEnhanceCardSlot> slotsPool=new();
    public NativeCardsPanel abilityCardsPanel=new();
    public int PointsUpdates,Deselects,Adds,Removes,Callbacks,WarningsUpdates;public bool ThrowRefresh,WarningsVisible=true;
    public bool LastSelected;
    public CMapCharacter? Character=>characterData;
    public Dictionary<CAbilityCard,UIEnhanceCardSlot> Assigned=>assignedSlots;
    public void SetPrefab(UIEnhanceCardSlot prefab){slotPrefab=prefab;}
    private void OnSelectedCard(AbilityCardUI card){Callbacks++;selectedCard=card;onAbilityCardSelected?.Invoke(card);}
    public void UpdateEnhancementPoints(){PointsUpdates++;}
    public void ShowWarningPoints(bool visible){WarningsUpdates++;WarningsVisible=visible;}
    public void Deselect(){Deselects++;selectedCard=null;}
    public void OnAddedEnhancement(CAbilityCard card,bool selected){if(ThrowRefresh)throw new Exception("native fixture presentation refresh failed");Adds++;LastSelected=selected;}
    public void OnRemovedEnhancement(CAbilityCard card,bool selected){Removes++;LastSelected=selected;}
}
public sealed class NativeEnhancementShop { public int Clears; public void Clear(){Clears++;} }
public sealed class NativeBuyTab { public GameObject gameObject=null!;public bool interactable,isOn;public int Activations;public Action? Active; public void Activate(){Activations++;if(!isOn){isOn=true;Active?.Invoke();}} }
public class UINewEnhancementWindow : MonoBehaviour
{
    private MapPartyEnhancementShopService? shopService;
    private AbilityCardUI? previousSelectedCard;
    private AbilityCardUI? lastShowedCard;
    private string audioItemBuyEnhancement="native-buy-enhancement",audioItemSellEnhancement="native-sell-enhancement";
    public MapPartyEnhancementShopService? Service=>shopService;
    public CMapCharacter character=null!;
    public UIPartyCharacterEnhancementAbilityCardsDisplay CardsDisplay=null!;
    public NativeEnhancementShop enhancementShop=new();
    public NativeBuyTab buyButton=new();
    public NativeBuyTab sellButton=new();
    public AbilityCardUI? selectedCard;
    public int Selections,OwnershipChanges,Refreshes,Payments;public bool BuyMode;
    public void SetOldShown(AbilityCardUI card){lastShowedCard=previousSelectedCard=selectedCard=card;}
    public void ShowBuyOptions(){BuyMode=true;if(lastShowedCard!=null)OnSelectedCardToEnhance(lastShowedCard);}
    public void OnSelectedCardToEnhance(AbilityCardUI? card){Selections++;selectedCard=card;previousSelectedCard=card;}
    private void OnControllableOwnershipChanged(){OwnershipChanges++;}
    private void RefreshSelectionAfterEnhance(AbilityCardUI card){Refreshes++;}
    public void ProxyBuyEnhancement(){} public void ProxySellEnhancement(){}
}
public static class HelperTools
{
    public static int Normalizations;
    public static void NormalizePool(ref List<UIEnhanceCardSlot> pool,GameObject prefab,Transform parent,int count)
    {
        Normalizations++;
        while(pool.Count<count){var go=UnityEngine.Object.Instantiate(prefab,parent);pool.Add(go.GetComponent<UIEnhanceCardSlot>());}
        for(int i=0;i<pool.Count;i++)pool[i].gameObject.SetActive(i<count);
    }
}
public static class AudioControllerUtils { public static readonly List<string> Played=new();public static void PlaySound(string name){Played.Add(name);} }
namespace GloomhavenVR.Net.TownServices { public static class TownServiceGrantSync { public static bool CanUseImmersive=true; } }
namespace GloomhavenVR.WorldUI.MapRoom
{
    public static class MapRoomDriver { public static bool Active=true; public static int Presses; public static bool PressGuildmasterMode(EGuildmasterMode mode,string reason){Presses++;GuildmasterDestinations.Mode=mode;return true;} }
    public static class MapRoomHand { public static CMapCharacter? Owned;public static CMapCharacter? OwnedMerchantCharacter()=>Owned; }
    public static class GuildmasterDestinations { public static EGuildmasterMode Mode;public static EGuildmasterMode CurrentDestinationMode()=>Mode; }
}
namespace GloomhavenVR.WorldUI
{
    public static class WorldUIConfig { public static Toggle ImmersiveTownServices=new();public class Toggle { public bool Value=true; } }
    public static class StoryComposite { public static bool PointOfNoReturn; }
    public static class TownServiceEnhancementHandoff { public static bool Enabled=true,HasCurrentOffering,KeepsQuietVisit; }
    public static class TownServiceMerchantHandoff { public static bool Active=true,HasParkedOffer; }
    public sealed class TownServiceRitual { public bool HasTemplePurseInHand,HasParkedTempleOffer; }
    public static class TownServicePresentation
    {
        public static UIWindow? Window;public static byte Service;
        public static TownServiceRitual? Ritual;
        public static bool IsQuietController(UIWindow? window,byte service)=>ReferenceEquals(window,Window)&&service==Service;
        public static bool IsQuietTemple(UIWindow window)=>IsQuietController(window,2);
    }
    public sealed class TownServiceStation { public bool Near=true;public bool IsLocalVisitorNear(bool previous)=>Near; }
    public static class TownServicePopulation { public static TownServiceStation Station=new();public static bool Available(byte service)=>true;public static TownServiceStation? Acquire(byte service)=>Station; }
}
