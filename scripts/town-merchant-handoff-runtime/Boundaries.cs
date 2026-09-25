// Production Handoff, MapRoomHand ownership/suspension and ItemsPile inspection lifecycle run
// against real Unity transforms. Native game inventory/confirmation and the existing ItemChip
// renderer/grabber are explicit test boundaries; this does not claim headset rendering parity.
using System;
using System.Collections.Generic;
using UnityEngine;
namespace TMPro { public class TMP_Text : MonoBehaviour { public string text = ""; public RectTransform rectTransform => (RectTransform)transform; } }
namespace FFSNet { public static class FFSNetwork { public static bool IsOnline; } }
namespace ScenarioRuleLibrary { public class CItem { public int ID; public bool Tradeable = true; } }
namespace MapRuleLibrary.Party {
 public class CMapCharacter { public bool IsUnderMyControl = true; public readonly List<ScenarioRuleLibrary.CItem> AllCharacterItems = new(); }
 public class CMapParty { public readonly List<ScenarioRuleLibrary.CItem> Stock = new(); }
}
namespace MapRuleLibrary.Adventure {
 public static class AdventureState { public static State MapState = new(); public class State { public MapRuleLibrary.Party.CMapParty MapParty = new(); } }
}
public class ShopService {
 private readonly MapRuleLibrary.Party.CMapParty _party;
 public ShopService(MapRuleLibrary.Party.CMapParty party, Action<ScenarioRuleLibrary.CItem> updated) { _party = party; }
 public bool IsAffordable(ScenarioRuleLibrary.CItem item, MapRuleLibrary.Party.CMapCharacter? c) => Affordable;
 public static bool Affordable = true;
 public List<ScenarioRuleLibrary.CItem> GetItemsToBuy(MapRuleLibrary.Party.CMapCharacter? c) => _party.Stock;
 public List<ScenarioRuleLibrary.CItem> GetItemsToSell(MapRuleLibrary.Party.CMapCharacter? c) => c!.AllCharacterItems;
}
public class Singleton<T> { public static T? Instance; }
public enum EGuildmasterMode { None, Merchant, Enchantress }
public class UIWindow : MonoBehaviour { public bool IsOpen; }
public class ItemCardUI : MonoBehaviour { public UnityEngine.UI.Image cardBackground=null!; }
public class UIShopItemInventory { public ShopService? service; public MapRuleLibrary.Party.CMapCharacter? character; }
public class UIShopItemWindow : MonoBehaviour { public UIShopItemInventory ItemInventory = new(); }
public class UIGuildmasterHUD { public UIShopItemWindow shopWindow = null!; }
public class UIItemConfirmationBox {
 public bool IsActive; public UnityEngine.UI.Button confirmButton=null!; public Action? _onConfirmedCallback; public Action? OnCancelled; public int Cancels;
 public void OnCancel() { Cancels++; OnCancelled?.Invoke(); IsActive = false; _onConfirmedCallback = null; }
}
namespace GloomhavenVR.Core {
 public static class Loc { public static string Mod(string s) => s; }
 public static class VRLayers { public const int ModLayer=27; public static void Apply(GameObject o) { } }
 public static class VRLog { public static void Warn(string scope,string message) { } }
}
namespace GloomhavenVR.Core.Events {
 public enum VRMode { TableIdle, ModalUI } public static class VRModeStateMachine { public static VRMode CurrentMode; }
}
namespace GloomhavenVR.Hands {
 public class VRHand { public bool HasPose = true; public float WorldScale = 1; public Holder Grabber = new(); public HandRig Rig = new(); public Gate PalmGate = new(); }
 public class Holder { public object? Held; public void CancelAll() { if (Held is Cards.ItemsPile.ItemChip chip) chip.Holder = null; Held = null; } }
 public class HandRig { public Transform PalmCenter = null!; }
 public class Gate { public bool IsOpen = true, Enabled, IgnoreWhenHandBusy; public float EnterDegrees, ExitDegrees; }
 public static class VRHands { public static VRHand? Left, Right, Primary; }
}
namespace GloomhavenVR.Rig { public static class VRRigDriver { public static Transform? RigRoot; public static Camera? HeadCamera; } }
namespace GloomhavenVR.Cards {
 public sealed class Dial<T> { public T Value; public Dial(T v) { Value = v; } }
 public static class CardsConfig {
  public static bool RevealAlways;
  public static readonly Dial<float> ItemFanSeedScale = new(.12f), ItemFanSettleOvershoot = new(1.7f), ItemFanOpenSpinDegrees = new(20f), ItemFanOpenDuration = new(.25f), ItemFanCloseDuration = new(.2f), ItemFanOpenArc = new(.015f);
  public static readonly Dial<float> FanRadius = new(.3f), FanSplitMultiplier = new(1f), FanSplitFalloff = new(1f), FanHoverSplitScale = new(1f);
  private static readonly Dial<float> RadiusFactor = new(1f), Step = new(10f);
  public static Dial<float> FanRadiusFactor(PileKind kind)=>RadiusFactor;
  public static Dial<float> FanStepDegrees(PileKind kind)=>Step;
  public static readonly Dial<float> FanPalmOffset = new(.09f), FanFollowSmoothing = new(16f), FanFollowDeadzone = new(.001f), RevealEnterDegrees = new(60f), RevealExitDegrees = new(45f);
  public static readonly Dial<bool> RevealIgnoreWhenGrabbing = new(true);
 }
 public enum PileKind { Items }
 public class VRCard { }
 public static class CardsDriver { internal static void StandDownForItemFanContact(Hands.VRHand? hand, IReadOnlyList<ItemsPile.ItemChip> chips) { } }
 public static class FanSweep { public static float ArcChord(float radius,float step)=>2f*radius*Mathf.Sin(step*Mathf.Deg2Rad*.5f); public static float SplitOffset(int i)=>i*.01f; }
 public static class PileFanShape { public const float ArchFactor=.55f,TiltFactor=.85f; public static void FaceHead(Transform root) { } }
 internal sealed partial class ItemsPile {
  private Transform? _root; private TMPro.TMP_Text? _title;
  private readonly List<ItemChip> _chips = new();
  private int HighlightedIndex => -1;
  private int SplitPivotIndex => -1;
  public int LayoutCalls;
  private const float MaxArcDegrees=110f,ChipScale=1.25f,ZStagger=.004f;
  private bool UseAnimationPending=>false; private int _splitPivotIndex;
  public bool IsOpen;
  private ItemsPile(Action<ItemChip,Vector3> release) { _inspectionRelease = release; }
  private void EnsureRoot() { _root = new GameObject("Fan").transform; }
  private void ClearHandSweep() { }
  private bool PlacedCardIsLocked(ItemChip chip)=>false;
  private void UpdateHandSweep() { }
  private void Relayout() { LayoutCalls++; ProductionRelayout(); }
  private void ClearChips() { foreach(var c in _chips) UnityEngine.Object.DestroyImmediate(c.gameObject); _chips.Clear(); }
  internal partial class ItemChip : MonoBehaviour {
   public enum Visual { Normal, Spent } public Visual State;
   public bool PendingUse, TownOffering; public Action? TownOfferingReclaimed;
   public void CancelReleaseGlide() { _releaseGlide=0f; }
   public void ResumeInspectionGlide() { _releaseGlide=.3f; }
   private Vector3 _homePos,_emergeFrom,_collapseWorld,_collapseFrom;
   private Quaternion _homeRot=Quaternion.identity,_emergeSpin,_collapseFromRot,_collapseSpin;
   private float _homeScale=1f,_releaseGlide,_emergeTime,_emergeDelay,_collapseFromScale,_collapseTime,_collapseDelay;
   private bool _emerging,_collapsing,_fingerPopped,_laserPopped,_recessPopped;
   private BoxCollider? _box;
   private ItemCardUI? _cardUI;
   private const float TightArtPollSeconds=2f;
   private bool _inspectionArtPending;
   private Vector3 _inspectionArtConverge;
   private float _inspectionArtSpinSign,_inspectionArtDeadline;
   public bool InspectionArtPending=>_inspectionArtPending;
   public Vector3 Home=>_homePos; public Quaternion HomeRotation=>_homeRot;
   public float CollapseTime=>_collapseTime;
   private static float SeedScale()=>Mathf.Clamp(CardsConfig.ItemFanSeedScale.Value,.02f,1f);
   private static float Overshoot()=>Mathf.Clamp(CardsConfig.ItemFanSettleOvershoot.Value,0f,3f);
   public void SetGrabStrip(float width) { }
   public void ClearHandSuppressed() { }
   public void AdvanceEmerge(float dt) { TickEmerge(dt,_homePos,_homeScale); }

   private Hands.VRHand? _suppressedForHand=null,_suppressedForHand2=null;
   private ItemsPile? _owner; public ScenarioRuleLibrary.CItem? Item; public ItemsPile? Owner { get=>_owner; set=>_owner=value; } public Hands.VRHand? Holder; public bool IsCollapsing=>_collapsing;
   public static bool NewArtReady=true;
   public ItemCardUI? NativeItemCard; public Transform InspectionMount => transform; public Transform? InspectionBody;
   public static ItemChip Create(ItemsPile owner, Transform parent, ScenarioRuleLibrary.CItem item) {
    var go=new GameObject("ActualInspectionCard",typeof(BoxCollider),typeof(ItemChip));
    var c=go.GetComponent<ItemChip>(); c.transform.SetParent(parent,false); c.Owner=owner;c.Item=item;c._box=go.GetComponent<BoxCollider>();
    var art=new GameObject("NativeArt",typeof(RectTransform),typeof(CanvasRenderer),typeof(UnityEngine.UI.Image),typeof(ItemCardUI));art.transform.SetParent(go.transform,false);
    c._cardUI=art.GetComponent<ItemCardUI>();c.NativeItemCard=c._cardUI;c._cardUI.cardBackground=art.GetComponent<UnityEngine.UI.Image>();c.SetArtReady(NewArtReady);return c;
   }
   public void SetArtReady(bool ready) { _cardUI!.cardBackground.sprite=ready?Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),Vector2.one*.5f):null; }
   public void Release(Vector3 p) { Holder = null; Owner!._inspectionCensusDirty = true; Owner._inspectionRelease!(this,p); }
  }
 }
}
namespace GloomhavenVR.WorldUI.MapRoom {
 public static class MapRoomDriver { public static bool Active = true, CanVisit = true, LastSuppressed; public static int Visits;
  public static bool CanVisitTownService(EGuildmasterMode mode) => CanVisit;
  public static bool PressGuildmasterMode(EGuildmasterMode mode,string reason, bool suppressNativeSound = false) { Visits++; LastSuppressed=suppressNativeSound; GuildmasterDestinations.Mode = mode; return true; }
 }
 public static class GuildmasterDestinations { public static EGuildmasterMode Mode; public static EGuildmasterMode CurrentDestinationMode()=>Mode; }
 public static class MapCharacterSelection { public static MapRuleLibrary.Party.CMapCharacter? Selected; public static MapRuleLibrary.Party.CMapCharacter? Current(out string source) { source="fixture"; return Selected; } }
 internal sealed partial class MapRoomHand {
  private static MapRoomHand s_live = new(); private bool _engaged = true;
  public static int NormalRebuilds, NormalReleases;
  private void ReleaseFan(string why) { NormalReleases++; }
  private void RebuildFan() { NormalRebuilds++; }
 }
}
namespace GloomhavenVR.WorldUI {
 internal enum TownVoiceReaction : byte { MerchantOffer, MerchantBuy, MerchantSell }
 internal static class TownServiceVoice { internal static int Offers, Buys, Sells; internal static void RequestReaction(byte service, TownVoiceReaction reaction) { if(service!=1) return; if(reaction==TownVoiceReaction.MerchantOffer) Offers++; else if(reaction==TownVoiceReaction.MerchantBuy) Buys++; else if(reaction==TownVoiceReaction.MerchantSell) Sells++; } }
 public static class WorldUIConfig { public static readonly Cards.Dial<bool> ImmersiveTownServices = new(true); }
 public static class TownServiceEnhancementHandoff { public static bool Enabled = true; }
 public static class StoryComposite { public static bool PointOfNoReturn; }
 internal sealed class TownServiceStation { public Transform Root = null!; public bool Near = true; public bool IsLocalVisitorNear(bool previous)=>Near; }
 internal static class TownServicePopulation { public static TownServiceStation? Station; public static bool Available(byte s)=>Station!=null; public static TownServiceStation? Acquire(byte s)=>Station; }
 internal static class TownServiceCatalog { public static Func<ScenarioRuleLibrary.CItem,bool,bool>? CanOffer; public static Func<ScenarioRuleLibrary.CItem,bool,Vector3,bool>? Offer; public static Func<Vector3,bool>? InOfferingZone; public static bool HeldOfferAvailable; public static Action<TownServiceToken>? RetainOffer; }
 internal sealed class TownServiceToken { public void ParkOffering(Transform seat,Action reclaim) {} public void ReturnOffering() {} }
 internal static class TownServicePalmConfirmation { internal static void Begin(UIItemConfirmationBox box, Transform seat) {} }
 internal static class TownServiceMerchantTransaction {
  public static int Requests; public static ScenarioRuleLibrary.CItem? LastItem; public static bool LastSelling;
  public static bool Commit(UIShopItemInventory inventory, ScenarioRuleLibrary.CItem item, bool selling, Func<bool> current) {
   if(!current()) return false; Requests++; LastItem=item; LastSelling=selling;
   Singleton<UIItemConfirmationBox>.Instance!.IsActive = true;
   Singleton<UIItemConfirmationBox>.Instance!._onConfirmedCallback = ()=>{}; return true;
  }
 }
 public static class TownServiceMerchantZone {
  public static GameObject CreateTemplate(TMPro.TMP_Text? font) {
   var go = new GameObject("Zone",typeof(RectTransform),typeof(CanvasGroup)); go.transform.localScale=Vector3.one*.001f;
   new GameObject("Border",typeof(RectTransform)).transform.SetParent(go.transform,false);
   new GameObject("Caption",typeof(RectTransform),typeof(TMPro.TMP_Text)).transform.SetParent(go.transform,false); return go;
  }
 }
}
