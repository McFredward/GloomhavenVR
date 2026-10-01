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
public enum EGuildmasterMode { None, Merchant, Temple, Enchantress }
public enum PartySlotState { Assigned }
public class NewPartyCharacterUI { public PartySlotState State = PartySlotState.Assigned; public void OnClick() { } }
public class NewPartyDisplayUI {
 public static NewPartyDisplayUI? PartyDisplay;
 public NewPartyCharacterUI? SelectedUISlot;
}
public class UIWindow : MonoBehaviour { public bool IsOpen, IsVisible; public void Hide() { IsOpen=false; IsVisible=false; } }
public class ItemCardUI : MonoBehaviour { public UnityEngine.UI.Image cardBackground=null!; }
public class ObjectPool : MonoBehaviour {
 public enum ECardType { Item }
 public static ObjectPool? instance;
 private readonly Dictionary<int,List<GameObject>> _cards = new();
 public static bool LastResetScale,LastResetMiddle,LastResetRotation;
 public static GameObject SpawnCard(int id,ECardType type,Transform parent,bool resetLocalScale=false,bool resetToMiddle=false,bool resetLocalRotation=false,bool activate=true) {
  LastResetScale=resetLocalScale;LastResetMiddle=resetToMiddle;LastResetRotation=resetLocalRotation;
  GameObject card;
  if(instance!=null&&instance._cards.TryGetValue(id,out var cards)&&cards.Count>0) {card=cards[^1];cards.RemoveAt(cards.Count-1);}
  else card=new GameObject("NativeArt",typeof(RectTransform),typeof(CanvasRenderer),typeof(UnityEngine.UI.Image),typeof(ItemCardUI));
  card.transform.SetParent(parent,false);
  var rect=(RectTransform)card.transform;
  if(resetLocalScale)rect.localScale=Vector3.one;
  if(resetToMiddle){rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;rect.anchoredPosition=Vector2.zero;}
  if(resetLocalRotation)rect.localRotation=Quaternion.identity;
  rect.localPosition=new Vector3(rect.localPosition.x,rect.localPosition.y,0f);card.SetActive(activate);return card;
 }
 public static void RecycleCard(int id,ECardType type,GameObject card) {
  if(instance==null)return;
  if(!instance._cards.TryGetValue(id,out var cards))instance._cards[id]=cards=new List<GameObject>();
  card.transform.localRotation=Quaternion.identity;
  var rect=(RectTransform)card.transform;rect.anchoredPosition=Vector2.zero;rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;
  card.SetActive(false);cards.Add(card);
 }
 public static bool Contains(int id,GameObject card)=>instance!=null&&instance._cards.TryGetValue(id,out var cards)&&cards.Contains(card);
}
public class UIShopItemInventory { public ShopService? service; public MapRuleLibrary.Party.CMapCharacter? character; }
public class UIShopItemWindow : MonoBehaviour { public UIShopItemInventory ItemInventory = new(); }
public class UIGuildmasterHUD { public UIShopItemWindow shopWindow = null!; }
public class UIItemConfirmationBox {
 public bool IsActive; public UnityEngine.UI.Button confirmButton=null!,cancelButton=null!; public Action? _onConfirmedCallback, _onCancelCallback; public Action? OnCancelled; public int Cancels; public bool DeferCancelCallback;private Action? _deferredCancel;
 public readonly UIWindow Window = new GameObject("Native Item Confirmation",typeof(UIWindow)).GetComponent<UIWindow>();
 public T GetComponent<T>() where T:class => (T)(object)Window;
 public void NativeHide() { IsActive = false; Window.Hide(); }
 public void Hide() { NativeHide(); }
 public void OnCancel() { Cancels++; NativeHide();if(DeferCancelCallback)_deferredCancel=_onCancelCallback;else _onCancelCallback?.Invoke(); OnCancelled?.Invoke(); _onConfirmedCallback = null; }
 public void CompleteCancel(){_deferredCancel?.Invoke();_deferredCancel=null;}
}
namespace GloomhavenVR.Core {
 public static class Loc { public static string Mod(string s) => s; }
 public static class VRLayers { public const int ModLayer=27; public static void Apply(GameObject o) { } }
 public static class VRLog { public static bool WantsDebug; public static void Debug(string scope,string message) { } public static void Warn(string scope,string message) { } public static void Info(string scope,string message) { } }
}
namespace GloomhavenVR.Core.Events {
 public enum VRMode { TableIdle, ModalUI } public static class VRModeStateMachine { public static VRMode CurrentMode; }
}
namespace GloomhavenVR.Hands {
 public enum HandSide { Left, Right }
 public enum HapticPreset { ClickPulse, HoverTick }
 public class VRHand { public bool HasPose = true; public float WorldScale = 1; public int Haptics; public Holder Grabber = new(); public HandRig Rig = new(); public Gate PalmGate = new(); public HandSide Side; public void SendHaptic(HapticPreset preset) { if(preset==HapticPreset.ClickPulse)Haptics++; } }
 public class Holder { public object? Held; public void CancelAll() { if (Held is Cards.ItemsPile.ItemChip chip) chip.Holder = null; Held = null; } }
 public class HandRig { public Transform PalmCenter = null!; public Transform GrabAnchor = null!; }
 public class Gate { public bool IsOpen = true, Enabled, IgnoreWhenHandBusy; public float EnterDegrees, ExitDegrees; }
 public static class VRHands { public static VRHand? Left, Right, Primary; }
}
namespace GloomhavenVR.Rig { public static class VRRigDriver { public static Transform? RigRoot; public static Camera? HeadCamera; } }
namespace GloomhavenVR.Cards {
 public readonly struct HeldPose { public readonly Vector3 LocalPosition; public readonly Quaternion LocalRotation; public readonly float? LocalScale; public HeldPose(Vector3 p,Quaternion r,float? s=null){LocalPosition=p;LocalRotation=r;LocalScale=s;} }
 internal abstract class GrabbableBehaviour : MonoBehaviour {
  private Transform? _originalParent; private Vector3 _originalLocalPos,_originalLocalScale; private Quaternion _originalLocalRot; private bool _attached;
  public Hands.VRHand? Holder { get; set; }
  protected virtual HeldPose GetHeldPose(Hands.VRHand hand)=>new(Vector3.zero,Quaternion.identity,1f);
  public virtual void OnGrab(Hands.VRHand hand) { Holder=hand; _originalParent=transform.parent;_originalLocalPos=transform.localPosition;_originalLocalRot=transform.localRotation;_originalLocalScale=transform.localScale;var pose=GetHeldPose(hand);transform.SetParent(hand.Rig.GrabAnchor,false);transform.localPosition=pose.LocalPosition;transform.localRotation=pose.LocalRotation;if(pose.LocalScale.HasValue)transform.localScale=Vector3.one*pose.LocalScale.Value;_attached=true; }
  public virtual void OnRelease(Hands.VRHand hand,Vector3 velocity) { if(_attached){Transform? parent=_originalParent!=null&&_originalParent.gameObject.activeInHierarchy?_originalParent:null;transform.SetParent(parent,false);transform.localPosition=_originalLocalPos;transform.localRotation=_originalLocalRot;transform.localScale=_originalLocalScale;_attached=false;}Holder=null; }
 }
 public sealed class Dial<T> { public T Value; public Dial(T v) { Value = v; } }
 public static class CardsConfig {
  public static bool RevealAlways;
  public static readonly Dial<float> ItemFanSeedScale = new(.12f), ItemFanSettleOvershoot = new(1.7f), ItemFanOpenSpinDegrees = new(20f), ItemFanOpenDuration = new(.25f), ItemFanCloseDuration = new(.2f), ItemFanOpenArc = new(.015f);
  public static readonly Dial<float> FanRadius = new(.3f), FanSplitMultiplier = new(1f), FanSplitFalloff = new(1f), FanHoverSplitScale = new(1f);
  public static readonly Dial<float> InspectScale = new(1.6f), CardLerpSpeed = new(14f); public static readonly Dial<string> CardGrabSound = new(""); public const float CardHeight=.14f,CardWidth=.1f;
  private static readonly Dial<float> RadiusFactor = new(1f), Step = new(10f);
  public static Dial<float> FanRadiusFactor(PileKind kind)=>RadiusFactor;
  public static Dial<float> FanStepDegrees(PileKind kind)=>Step;
  public static readonly Dial<float> FanPalmOffset = new(.09f), FanFollowSmoothing = new(16f), FanFollowDeadzone = new(.001f), RevealEnterDegrees = new(60f), RevealExitDegrees = new(45f);
  public static readonly Dial<bool> RevealIgnoreWhenGrabbing = new(true);
 }
 public enum PileKind { Items }
 public class VRCard { }
 public static class CardsDriver {
  internal static bool OffScenarioFanIsOpen;
  internal static int SuppressedOpenEdges, SuppressedCloseEdges;
  internal static void StandDownForItemFanContact(Hands.VRHand? hand, IReadOnlyList<ItemsPile.ItemChip> chips) { }
  internal static void PlayCardSound(string sound,Transform at) { }
  internal static void SuppressNextOffScenarioFanEdgeSound(bool open, float seconds = 2f) {
   if(open) SuppressedOpenEdges++; else SuppressedCloseEdges++;
  }
 }
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
  private ItemsPile(Action<ItemChip,Vector3,Hands.VRHand> release) { _inspectionRelease = release; }
  private void EnsureRoot() { _root = new GameObject("Fan").transform; }
  private void ClearHandSweep() { }
  private bool PlacedCardIsLocked(ItemChip chip)=>false;
  private void UpdateHandSweep() { }
  internal void UnclipChip(ItemChip chip) { }
  internal bool IsTransferring(ItemChip chip)=>false;
  internal bool CompleteChipTransfer(ItemChip chip,Hands.VRHand from)=>false;
  internal void RefreshFanLayout() { if(IsOpen) Relayout(); }
  internal void OnChipReleased(ItemChip chip,Vector3 point,Hands.VRHand hand) { if(_inspectionRelease!=null){_inspectionCensusDirty=true;_inspectionRelease(chip,point,hand);if(!IsOpen&&!chip.TownOffering)chip.BeginCollapse(_root!=null?_root.position:point);} }
  private void Relayout() { LayoutCalls++; ProductionRelayout(); }
  private void ClearChips() { foreach(var c in _chips) UnityEngine.Object.DestroyImmediate(c.gameObject); _chips.Clear(); }
  internal partial class ItemChip : GrabbableBehaviour {
   public enum Visual { Normal, Spent } public Visual State;
   public bool PendingUse, TownOffering; public Action? TownOfferingReclaimed;
   public void CancelReleaseGlide() { _releaseGlide=0f; }
   public void ResumeInspectionGlide() { _releaseGlide=.3f; }
   private Vector3 _homePos,_emergeFrom,_collapseWorld,_collapseFrom;
   private Quaternion _homeRot=Quaternion.identity,_emergeSpin,_collapseFromRot,_collapseSpin;
   private float _homeScale=1f,_releaseGlide,_emergeTime,_emergeDelay,_collapseFromScale,_collapseTime,_collapseDelay,_heldScale=1f,_pop;
   private Vector3 _heldPos;
   private const float ReleaseGlideSeconds=.35f;
   private bool _emerging,_collapsing,_fingerPopped,_laserPopped,_recessPopped;
   private BoxCollider? _box;
   private ItemCardUI? _cardUI;
   private GameObject? _cardGo;
   private Canvas? _faceCanvas;
   private const float TightArtPollSeconds=2f;
   private bool _inspectionArtPending;
   private Vector3 _inspectionArtConverge;
   private float _inspectionArtSpinSign,_inspectionArtDeadline;
   public bool InspectionArtPending=>_inspectionArtPending;
   public float FaceWidth=>.14f;
   public Vector3 Home=>_homePos; public Quaternion HomeRotation=>_homeRot;
   public float CollapseTime=>_collapseTime;
   private static float SeedScale()=>Mathf.Clamp(CardsConfig.ItemFanSeedScale.Value,.02f,1f);
   private static float Overshoot()=>Mathf.Clamp(CardsConfig.ItemFanSettleOvershoot.Value,0f,3f);
   public void SetGrabStrip(float width) { }
   public void ClearHandSuppressed() { }
   public void AdvanceEmerge(float dt) { TickEmerge(dt,_homePos,_homeScale); }

   private Hands.VRHand? _suppressedForHand=null,_suppressedForHand2=null;
   private ItemsPile? _owner; public ScenarioRuleLibrary.CItem? Item; public ItemsPile? Owner { get=>_owner; set=>_owner=value; } public bool IsCollapsing=>_collapsing;
   public static bool NewArtReady=true;
   public ItemCardUI? NativeItemCard; public Transform InspectionMount => transform; public Transform? InspectionBody;
   public static ItemChip Create(ItemsPile owner, Transform parent, ScenarioRuleLibrary.CItem item) {
    var go=new GameObject("ActualInspectionCard",typeof(BoxCollider),typeof(ItemChip));
    var c=go.GetComponent<ItemChip>(); c.transform.SetParent(parent,false); c.Owner=owner;c.Item=item;c._box=go.GetComponent<BoxCollider>();
    var canvasGo=new GameObject("FaceCanvas",typeof(RectTransform),typeof(Canvas));canvasGo.transform.SetParent(go.transform,false);c._faceCanvas=canvasGo.GetComponent<Canvas>();
    var art=SpawnHostedItemCard(item.ID,canvasGo.transform);
    c._cardGo=art;c._cardUI=art.GetComponent<ItemCardUI>();c.NativeItemCard=c._cardUI;c._cardUI.cardBackground=art.GetComponent<UnityEngine.UI.Image>();
    CanonicalizeHostedFace(art.transform,(RectTransform)canvasGo.transform);c.SetArtReady(NewArtReady);return c;
   }
   private void OnDisable(){if(_cardGo!=null&&_cardUI!=null)ReturnHostedCardToPool(Item!.ID,_cardGo);_cardGo=null;_cardUI=null;NativeItemCard=null;}
   public void SetArtReady(bool ready) { _cardUI!.cardBackground.sprite=ready?Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),Vector2.one*.5f):null; }
   public void Release(Vector3 p) { Holder = null; Owner!.OnChipReleased(this,p,Hands.VRHands.Right!); }
   protected override HeldPose GetHeldPose(Hands.VRHand hand)=>new(new Vector3(.02f,.03f,.06f),Quaternion.Euler(5f,12f,3f),CardsConfig.InspectScale.Value);
   public float HomeScale=>_homeScale;
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
 internal static class CanvasConversion { internal static int Releases; internal static int ReleaseHiddenWindowVeilOwnership(Transform root) { Releases++; return 1; } }
 internal static class TownServiceMerchantLayout { internal const float CardWidth=.14f; }
 internal enum TownVoiceReaction : byte { MerchantOffer, MerchantBuy, MerchantSell, MerchantUnaffordable, MerchantSoldOut }
 internal static class TownServiceVoice { internal static int Offers, Buys, Sells, Unaffordable, SoldOut, StockRequests; internal static bool StockReady=true; internal static bool RequestStockReaction(TownVoiceReaction reaction) { if(!StockReady)return false;StockRequests++;RequestReaction(1,reaction);return true; } internal static void RequestReaction(byte service, TownVoiceReaction reaction) { if(service!=1) return; if(reaction==TownVoiceReaction.MerchantOffer) Offers++; else if(reaction==TownVoiceReaction.MerchantBuy) Buys++; else if(reaction==TownVoiceReaction.MerchantSell) Sells++; else if(reaction==TownVoiceReaction.MerchantUnaffordable) Unaffordable++; else if(reaction==TownVoiceReaction.MerchantSoldOut) SoldOut++; } }
 public static class WorldUIConfig { public static readonly Cards.Dial<bool> ImmersiveTownServices = new(true); }
 public static class TownServiceEnhancementHandoff { public static bool Enabled = true, WantsAbilityFan; }
 public static class TownServiceTempleOffering { public static bool WantsPurseFocus; }
 public sealed class TownServiceRitual {
  public bool HasParkedTempleOffer;
  public TownServiceEnhancementOffer? Handoff;
 }
 public sealed class TownServiceEnhancementOffer { public Cards.VRCard? Card; }
 public static class TownServicePresentation {
  public static TownServiceRitual? Ritual;
  public static bool NativeFallbackFor(byte service) => false;
 }
 public static class StoryComposite { public static bool PointOfNoReturn; }
 internal sealed class TownServiceStation { public Transform Root = null!; public bool Near = true; public bool IsLocalVisitorNear(bool previous)=>Near; }
 internal static class TownServicePopulation { public static TownServiceStation? Station; public static bool Available(byte s)=>Station!=null; public static TownServiceStation? Acquire(byte s)=>Station; }
 internal static class TownServiceCatalog { public static Func<ScenarioRuleLibrary.CItem,bool,bool>? CanOffer; public static Func<ScenarioRuleLibrary.CItem,bool,Vector3,bool>? Offer; public static Func<Vector3,bool>? InOfferingZone; public static bool HeldOfferAvailable; public static Action<TownServiceToken>? RetainOffer; public static bool TryHeldOffer(Vector3 target, out Vector3 position, out Hands.VRHand? hand, out bool selling) { position=default; hand=null; selling=false; return HeldOfferAvailable; } }
 internal sealed class TownServiceToken { public int Parks,Returns; public void ParkOffering(Transform seat,Action reclaim) { Parks++; } public void ReturnOffering() { Returns++; } }
 internal static class TownServicePalmConfirmation {
  internal static int Bindings;
  internal static bool PhysicalControlsVisible;
  internal static void Begin(UIItemConfirmationBox box, Transform seat) {
   Bindings++; PhysicalControlsVisible=box.IsActive && box.Window.IsOpen;
  }
 }
 internal static class TownServiceMerchantTransaction {
  public static int Requests, CommitFailures, NativeCommits; public static ScenarioRuleLibrary.CItem? LastItem; public static bool LastSelling;
  public static bool Commit(UIShopItemInventory inventory, ScenarioRuleLibrary.CItem item, bool selling, Func<bool> current) {
   if(!current()) return false;
   if(CommitFailures>0) { CommitFailures--; return false; }
   Requests++; LastItem=item; LastSelling=selling;
   var box=Singleton<UIItemConfirmationBox>.Instance!;
   box.IsActive=true; box.Window.IsOpen=box.Window.IsVisible=true; box._onConfirmedCallback=()=>NativeCommits++; box._onCancelCallback=()=>{};
   box.confirmButton.onClick.RemoveAllListeners();
   box.confirmButton.onClick.AddListener(()=>{ box._onConfirmedCallback?.Invoke(); box.NativeHide(); });
   return true;
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
namespace GloomhavenVR.Net.TownServices {
 internal static class TownServiceGrantSync { internal static bool CanUseImmersive = true; }
 internal static class TownServiceMirror {
  internal static bool GrantSettled = true;
  internal static bool CanLocalBeginTransaction(byte service) => true;
  internal static bool LocalTransactionDenied(byte service) => false;
  internal static bool LocalTransactionSettled(byte service) => GrantSettled;
 }
}
