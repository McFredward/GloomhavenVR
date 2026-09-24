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
public class ItemCardUI : MonoBehaviour { }
public class UIShopItemInventory { public ShopService? service; public MapRuleLibrary.Party.CMapCharacter? character; }
public class UIShopItemWindow : MonoBehaviour { public UIShopItemInventory ItemInventory = new(); }
public class UIGuildmasterHUD { public UIShopItemWindow shopWindow = null!; }
public class UIItemConfirmationBox {
 public bool IsActive; public Action? _onConfirmedCallback; public int Cancels;
 public void OnCancel() { IsActive = false; Cancels++; _onConfirmedCallback = null; }
}
namespace GloomhavenVR.Core {
 public static class Loc { public static string Mod(string s) => s; }
 public static class VRLayers { public const int ModLayer=27; public static void Apply(GameObject o) { } }
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
  public static readonly Dial<float> FanPalmOffset = new(.09f), FanFollowSmoothing = new(16f), FanFollowDeadzone = new(.001f), RevealEnterDegrees = new(60f), RevealExitDegrees = new(45f);
  public static readonly Dial<bool> RevealIgnoreWhenGrabbing = new(true);
 }
 public class VRCard { }
 public static class PileFanShape { public static void FaceHead(Transform root) { } }
 internal sealed partial class ItemsPile {
  private Transform? _root; private TMPro.TMP_Text? _title;
  private readonly List<ItemChip> _chips = new();
  private int HighlightedIndex => -1;
  public bool IsOpen;
  private ItemsPile(Action<ItemChip,Vector3> release) { _inspectionRelease = release; }
  private void EnsureRoot() { _root = new GameObject("Fan").transform; }
  private void ClearHandSweep() { }
  private void UpdateHandSweep() { }
  private void Relayout() { }
  private void ClearChips() { foreach(var c in _chips) UnityEngine.Object.DestroyImmediate(c.gameObject); _chips.Clear(); }
  internal class ItemChip : MonoBehaviour {
   public ScenarioRuleLibrary.CItem? Item; public ItemsPile? Owner; public Hands.VRHand? Holder; public bool IsCollapsing;
   public ItemCardUI? NativeItemCard; public Transform InspectionMount => transform; public Transform? InspectionBody;
   public static ItemChip Create(ItemsPile owner, Transform parent, ScenarioRuleLibrary.CItem item) {
    var c = new GameObject("ActualInspectionCard",typeof(ItemChip)).GetComponent<ItemChip>(); c.transform.SetParent(parent,false); c.Owner = owner; c.Item = item; return c;
   }
   public void BeginEmerge(Vector3 p,float delay,float sign) { }
   public void BeginCollapse(Vector3 p) { IsCollapsing = true; }
   public void Release(Vector3 p) { Holder = null; Owner!._inspectionRelease!(this,p); }
  }
 }
}
namespace GloomhavenVR.WorldUI.MapRoom {
 public static class MapRoomDriver { public static bool Active = true, CanVisit = true; public static int Visits;
  public static bool CanVisitTownService(EGuildmasterMode mode) => CanVisit;
  public static bool PressGuildmasterMode(EGuildmasterMode mode,string reason) { Visits++; GuildmasterDestinations.Mode = mode; return true; }
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
 public static class WorldUIConfig { public static readonly Cards.Dial<bool> ImmersiveTownServices = new(true); }
 public static class TownServiceEnhancementHandoff { public static bool Enabled = true; }
 public static class StoryComposite { public static bool PointOfNoReturn; }
 internal sealed class TownServiceStation { public Transform Root = null!; public bool Near = true; public bool IsLocalVisitorNear(bool previous)=>Near; }
 internal static class TownServicePopulation { public static TownServiceStation? Station; public static bool Available(byte s)=>Station!=null; public static TownServiceStation? Acquire(byte s)=>Station; }
 internal static class TownServiceCatalog { public static Func<ScenarioRuleLibrary.CItem,bool,bool>? CanOffer; public static Func<ScenarioRuleLibrary.CItem,bool,Vector3,bool>? Offer; public static Func<Vector3,bool>? InOfferingZone; public static bool HeldOfferAvailable; }
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
