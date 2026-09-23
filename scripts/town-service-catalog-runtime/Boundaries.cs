// Real Unity transforms, colliders, canvases and button dispatch. Native inventory/service
// data and confirmation callbacks are explicit boundaries: tests verify routing, never
// pretend to execute original gameplay transactions without a running game.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using ScenarioRuleLibrary;
using MapRuleLibrary.Party;
public enum ItemListingType { None,AllGear,Head,Body,Hands,Legs,SmallItems,Owned }
namespace ScenarioRuleLibrary
{
    public class CItem
    {
        public enum EItemSlotState { Spent }
        public enum EItemSlot { Head,Body,OneHand,TwoHand,Legs,SmallItem,QuestItem }
        public int ID;public uint NetworkID;public string Name="Item";public bool Tradeable=true;public int SellPrice=5;
        public Data YMLData=new();public CItem(int id){ID=id;NetworkID=(uint)id;Name="Item"+id;}
        public bool CanEquipItem(int id)=>true;
        public class Data{public EItemSlot Slot;}
    }
}
namespace MapRuleLibrary.Party { public class CMapCharacter{public int CharacterID;} }
namespace MapRuleLibrary.State { public enum EGoldMode{PartyGold,CharacterGold} }
namespace MapRuleLibrary.Adventure
{ public static class AdventureState{public static State MapState=new();public class State{public MapRuleLibrary.State.EGoldMode GoldMode;}} }
namespace GLOOM { public static class LocalizationManager{public static string GetTranslation(string key)=>key;} }
namespace FFSNet
{
    public static class FFSNetwork { public static bool IsOnline; }
    public static class PlayerRegistry{public static NetworkPlayer? MyPlayer;}
    public class NetworkPlayer{public bool IsParticipant{get;set;}=true;}
}
namespace HarmonyLib { public static class AccessTools { public static System.Reflection.PropertyInfo? Property(Type? type,string name)=>type?.GetProperty(name); } }
public class Singleton<T> where T:class { public static T Instance=null!; }
public sealed class Service
{
    public readonly List<CItem> Buy=new(),Sell=new();public bool Affordable=true;public int Commits;
    public List<CItem> GetItemsToBuy(CMapCharacter? c=null)=>new(Buy);
    public List<CItem> GetItemsToSell(CMapCharacter? c=null)=>new(Sell);
    public Dictionary<CItem,Tuple<CMapCharacter,bool>> GetBoundsItems(CMapCharacter? c)=>new();
    public bool IsAffordable(CItem item,CMapCharacter? c)=>Affordable;
    public int DiscountedCost(CItem item)=>10;
    public int GetBuyDiscount()=>0;
}
public class Tab:MonoBehaviour{public Action? Changed;private bool _on;public bool isOn{get=>_on;set{_on=value;if(value)Changed?.Invoke();}}}
public class UIShopItemInventory:MonoBehaviour
{
    public Service service=new();public CMapCharacter? character;public CanvasGroup itemsCanvasGroup=null!;
    public UIShopItemSlot slotPrefab=null!;public List<UIShopItemSlot> slotPool=new();public Tab buyTab=null!,sellTab=null!;
    public UIPartyItemInventoryTooltip? itemTooltip;public bool Selling;public int Refreshes;
    public void RefreshView()
    {
        Refreshes++;foreach(var old in slotPool)UnityEngine.Object.DestroyImmediate(old.gameObject);slotPool.Clear();
        var items=Selling?service.Sell:service.Buy.GroupBy(x=>x.ID).Select(x=>x.First()).ToList();
        foreach(var item in items)
        {
            var row=UnityEngine.Object.Instantiate(slotPrefab,transform);row.gameObject.SetActive(true);row.Item=item;
            row.Selectable.onClick.RemoveAllListeners();row.Selectable.onClick.AddListener(()=>
            {if(!service.Affordable||!itemsCanvasGroup.interactable)return;Singleton<UIItemConfirmationBox>.Instance.Show(item,()=>service.Commits++);});
            slotPool.Add(row);
        }
    }
    public void FilterShownItems(ItemListingType type){}
    public string GetLocalizationSlot(ItemListingType type)=>type.ToString();
}
public class UIShopItemSlot:MonoBehaviour
{
    public CItem Item=null!;public CMapCharacter? Owner;public Button Selectable=null!;public bool IsAvailable=true;
    public void Initialize(CItem item,int price,Action<UIShopItemSlot> selected,Action<UIShopItemSlot,bool> hovered,object? moved,
        int amount,int total,bool affordable,bool n=false,bool n2=false,int discount=0,CMapCharacter? c=null){Item=item;IsAvailable=amount>0;}
    public void Initialize(CItem item,int price,Action<UIShopItemSlot> selected,Action<UIShopItemSlot,bool> hovered,object? moved,
        CMapCharacter? owner,bool equipped=false,CMapCharacter? c=null){Item=item;IsAvailable=true;}
}
public class UIItemConfirmationBox:MonoBehaviour
{
    public bool IsActive;public Button confirmButton=null!;public CItem? Item;public Action? BeforeShow;public Action? _onConfirmedCallback;public int Cancels;
    public void Show(CItem item,Action confirm){BeforeShow?.Invoke();IsActive=true;Item=item;_onConfirmedCallback=confirm;confirmButton.onClick.RemoveAllListeners();confirmButton.onClick.AddListener(()=>{confirm();IsActive=false;});}
    public bool IsConfirmingItem(CItem item)=>ReferenceEquals(Item,item);
    public void OnCancel(){IsActive=false;Cancels++;}
}
public class UIPartyItemInventoryTooltip:MonoBehaviour
{
    public bool IsShown;public ItemCardUI m_ItemCardUI=null!;public int Shows;public CMapCharacter? BoundTo;public Service? Service;
    public void Show(CItem item,RectTransform target,CMapCharacter? owner,string? info,CItem.EItemSlotState? state,Service? service)
    {
        Shows++;IsShown=true;BoundTo=owner;Service=service;transform.SetParent(target,false);
        if(m_ItemCardUI==null){var card=new GameObject("DetailCard",typeof(RectTransform));card.transform.SetParent(transform,false);m_ItemCardUI=card.AddComponent<ItemCardUI>();}
        m_ItemCardUI.item=item;gameObject.SetActive(true);
    }
    public void Hide(){IsShown=false;}
}
public class UITextTooltipTarget:MonoBehaviour
{
    public bool TooltipShown;
    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData data){TooltipShown=true;}
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData data){TooltipShown=false;}
}
namespace UnityEngine.UI{public class UITooltip:MonoBehaviour{public RectTransform? m_AnchorToTarget;}}
public class ItemCardUI:MonoBehaviour{public UITextTooltipTarget? AllHintsCardTooltip;public CItem item=null!;public void Show(bool highlightElement){gameObject.SetActive(true);}}
public static class ObjectPool
{
    public enum ECardType{Item}public static int Alive;
    public static GameObject SpawnCard(int id,ECardType type,Transform parent,bool resetLocalScale,bool resetToMiddle,bool resetLocalRotation,bool activate)
    {
        var go=new GameObject("OriginalItem",typeof(RectTransform),typeof(Image));go.SetActive(false);go.transform.SetParent(parent,false);
        ((RectTransform)go.transform).sizeDelta=new Vector2(180,145);go.AddComponent<ItemCardUI>();go.AddComponent<GraphicRaycaster>();Alive++;return go;
    }
}
namespace TMPro
{
    public enum TextAlignmentOptions{Center}public class TMP_FontAsset:ScriptableObject{}
    public class TMP_Text:Graphic
    {public TMP_FontAsset? font;public Material? fontSharedMaterial;public TextAlignmentOptions alignment;public float fontSize;public string text="";public bool enableWordWrapping;}
    public class TextMeshProUGUI:TMP_Text{}public class TextMeshPro:TMP_Text{}
}
namespace GloomhavenVR.Core
{
    internal static class VRLayers{internal static void Apply(GameObject go){}}
    internal static class Loc{internal static event Action? OnChanged;internal static int Subscribers=>OnChanged?.GetInvocationList().Length??0;internal static void Change()=>OnChanged?.Invoke();internal static string Mod(string key)=>key;}
}
namespace GloomhavenVR.Cards
{
    internal static class ItemBurnPlayback{internal static void ObserveInitialState(ItemCardUI item){}}
    internal static class CardFaceMipBake{internal static void Rescan(ItemCardUI item){}}
    internal sealed class ConfigFloat { internal float Value; internal ConfigFloat(float value){Value=value;} }
    internal static class CardsConfig
    { internal static ConfigFloat HeldOffPalm=new(.01f),HeldForward=new(.025f),HeldFaceBias=new(65f); }
}
namespace GloomhavenVR.Board.FigureGrab
{ internal static class HeldPoseMirror { internal static float OffsetSign(bool left)=>left?-1f:1f; } }
namespace GloomhavenVR.Rig{internal static class VRRigDriver{internal static Camera? HeadCamera;}}
namespace GloomhavenVR.Hands
{
    public enum HandSide{Left,Right}
    public enum Finger{Thumb,Index}
    public struct FingerJoints{public bool IsValid;public Transform Tip;}
    public class VRHand
    {
        public HandRig Rig=new();public float WorldScale=1;public bool HasPose=true,TriggerUp;public HandSide Side;
        internal GrabberFixture Grabber=new();
        public void GetAimRay(out Vector3 o,out Vector3 d){o=Rig.GrabAnchor.position;d=Rig.GrabAnchor.forward;}
    }
    internal class GrabberFixture{internal GloomhavenVR.Hands.Interact.IGrabbable? Held;}
    public class HandRig{public Transform GrabAnchor=new GameObject("Hand").transform;public FingerJoints GetFinger(Finger f)=>default;}
}
namespace GloomhavenVR.Hands.Interact
{
    internal interface IGrabbable{bool CanGrab{get;}bool GrabWithGrip{get;}void OnGrab(GloomhavenVR.Hands.VRHand h);void OnRelease(GloomhavenVR.Hands.VRHand h,Vector3 v);}
    internal interface IGrabbableHandFilter{bool AllowsHand(GloomhavenVR.Hands.VRHand h);}
    internal interface IGrabCancellation{void OnGrabCancelled(GloomhavenVR.Hands.VRHand h);}
    internal interface ITriggerOnlyGrabbable{}
    internal interface IGrabHighlight{void OnGrabHighlight(GloomhavenVR.Hands.VRHand h,bool value);}
    internal static class VRInteractables{internal static readonly List<IGrabbable> Registered=new();internal static void RegisterGrabbable(IGrabbable g,Collider c)=>Registered.Add(g);internal static void UnregisterGrabbable(IGrabbable g)=>Registered.Remove(g);}
    internal static class UguiPokeSurfaces{internal static void Unregister(Canvas c){}}
}
namespace GloomhavenVR.Net
{
    internal sealed class RemoteWidgetMirror
    {
        internal static int ThrowConstruction;
        private readonly Transform _mount;private readonly Dictionary<Transform,Transform> _clones=new();
        internal RemoteWidgetMirror(string name,Transform mount,float width,float height,Vector2 offset,Func<Transform,bool>? externallyShownBranch=null,bool mrBacking=true){if(ThrowConstruction>0&&--ThrowConstruction==0)throw new InvalidOperationException("injected mirror allocation failure");_mount=mount;}
        internal void SetOwnerFrame(Vector2 a,Vector2 b){}
        internal bool Refresh(Transform source){if(!_clones.ContainsKey(source)){var clone=new GameObject("Clone",typeof(RectTransform));clone.transform.SetParent(_mount,false);_clones.Add(source,clone.transform);}return true;}
        internal Transform? CloneOf(Transform source)=>_clones.TryGetValue(source,out var clone)?clone:null;
        internal void SetShown(bool shown){}
        internal void TickLive(){}internal void Destroy(){foreach(var clone in _clones.Values)if(clone!=null)UnityEngine.Object.DestroyImmediate(clone.gameObject);}
    }
    internal static class RemoteItemCardSource
    {internal static void ReturnBorrowed(int id,GameObject go){if(!go.GetComponent<Image>().raycastTarget||!go.GetComponent<GraphicRaycaster>().enabled||!go.GetComponent<Canvas>().enabled)throw new Exception("pool input restore");ObjectPool.Alive--;UnityEngine.Object.DestroyImmediate(go);}}
}
namespace GloomhavenVR.WorldUI
{
    internal static class TownServiceCardBody{internal static GameObject Create(Transform p){var g=new GameObject("Body");g.transform.SetParent(p,false);return g;}internal static void SetVisibility(GameObject g,float v){}internal static void Dispose(GameObject g){}}
    internal static class NativeTemplates{internal static UnityEngine.UI.UITooltip? Tooltip;}
    internal static class TownServiceNativeAssets{internal static void PrepareItem(ItemCardUI i){}}
    internal class TownServiceSurface{}
    internal static class TownServicePhysicalRay{internal static void Claim(GloomhavenVR.Hands.VRHand hand){}}
    internal static class TownServiceAssets{internal static GameObject? Merchant;internal static GameObject? Prefab(string n)=>Merchant;}
    internal static class PanelLayout{internal static float WorldScale=>1f;}
}

// Confirmation presentation is tested with the actual mask in the interaction suite.
namespace UnityEngine.UI { public class UIWindow : MonoBehaviour { } }
namespace GloomhavenVR.WorldUI { internal static class TownServiceConfirmationMask
{ internal static void Begin(UnityEngine.UI.UIWindow? window, Func<object?> identity) { } } }

// Catalogue tests exercise native stock and input. The workspace suite binds real terrain
// sampling, support transforms and remote parity against the actual furniture bundle.
namespace GloomhavenVR.Core { internal static class SkyAlternative { internal static UnityEngine.Transform? PlacedRoomRoot = null; } }
namespace GloomhavenVR.WorldUI { internal sealed class TownServiceGrounding : System.IDisposable {
 internal TownServiceGrounding(UnityEngine.Transform root, UnityEngine.Transform? furniture = null) { }
 internal void Resolve(out float actor, out float bottom) { actor = bottom = 0f; }
 internal void Apply(float actor, float bottom) { }
 public void Dispose() { }
} }
