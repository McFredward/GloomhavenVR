using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.Party;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary;
using UnityEngine;
public static class InteractionProgram
{
 private static int _count;
 private static void Check(bool condition,string reason) { _count++; if(!condition) throw new Exception(reason); }
 public static int Run() {
  _count=0;
  foreach(float scale in new[]{.05f,1f,198.12f}) RunScale(scale);
  return _count;
 }
 private static void RunScale(float scale) {
  TownServiceMerchantHandoff.Reset(); MapRoomDriver.Active=true; MapRoomDriver.CanVisit=true;
  GuildmasterDestinations.Mode=EGuildmasterMode.None; MapRoomDriver.Visits=0;
  FFSNet.FFSNetwork.IsOnline=false; StoryComposite.PointOfNoReturn=false;
  TownServiceMerchantTransaction.Requests=0; ShopService.Affordable=true;
  var root=new GameObject("Fixture"); root.transform.localScale=Vector3.one*scale; VRRigDriver.RigRoot=root.transform;
  var camera=new GameObject("Head",typeof(Camera)).GetComponent<Camera>(); camera.transform.SetParent(root.transform,false); VRRigDriver.HeadCamera=camera;
  var attention=new ActualAttention{_root=root.transform};
  camera.transform.position=Vector3.forward*(2.35f*scale);
  Check(attention.IsLocalVisitorNear(false),"resident attention enter distance matches handoff");
  camera.transform.position=Vector3.forward*(2.6f*scale);
  Check(!attention.IsLocalVisitorNear(false) && attention.IsLocalVisitorNear(true),"attention hysteresis prevents fan chatter");
  camera.transform.position=Vector3.back*scale;
  Check(!attention.IsLocalVisitorNear(true),"behind-resident player never opens merchant fan");
  camera.transform.position=Vector3.forward*(2.35f*scale);
  var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(root.transform,false); wall.transform.localPosition=Vector3.forward;
  Physics.SyncTransforms();
  Check(!attention.IsLocalVisitorNear(true),"occluded player never opens merchant fan");
  UnityEngine.Object.DestroyImmediate(wall);
  var station=new GameObject("Station").transform; station.SetParent(root.transform,false);
  var palm=new GameObject("ActivityOfferingPalm").transform; palm.SetParent(station,false);
  palm.localPosition=new Vector3(-.2f,1.18f,.2f); palm.localRotation=Quaternion.Euler(15,70,30);
  TownServicePopulation.Station=new TownServiceStation{Root=station};
  VRHands.Left=new VRHand{WorldScale=scale}; VRHands.Right=new VRHand{WorldScale=scale}; VRHands.Primary=VRHands.Right;
  var fanPalm=new GameObject("GlovePalmWithCentimetreBone").transform; fanPalm.SetParent(root.transform,false); fanPalm.localScale=Vector3.one*100f;
  VRHands.Left.Rig.PalmCenter=fanPalm; VRHands.Right.Rig.PalmCenter=fanPalm;
  var native=new GameObject("NativeMerchant",typeof(UIWindow),typeof(UIShopItemWindow)); native.transform.SetParent(root.transform,false);
  var window=native.GetComponent<UIShopItemWindow>();
  Singleton<UIGuildmasterHUD>.Instance=new UIGuildmasterHUD{shopWindow=window}; Singleton<UIItemConfirmationBox>.Instance=new();
  var character=new CMapCharacter(); var duplicate=new CItem{ID=1};
  for(int i=0;i<30;i++) character.AllCharacterItems.Add(new CItem{ID=i}); character.AllCharacterItems.Add(duplicate);
  character.AllCharacterItems[4].Tradeable=false; MapCharacterSelection.Selected=character;
  window.ItemInventory.character=character; window.ItemInventory.service=new ShopService(AdventureState.MapState.MapParty,_=>{});
  TownServiceMerchantHandoff.Tick(); TownServiceMerchantHandoff.LateTick();
  Check(MapRoomDriver.Visits==0,"approach never opens a native service");
  Check(TownServiceMerchantHandoff.OwnedChips.Count==31,"all equipped and bound copies become actual inspection cards");
  Check(TownServiceMerchantHandoff.Active,"local owned fan opens near resident");
  Check(!TownServiceMerchantHandoff.CanOffer(character.AllCharacterItems[4],true),"nontradeable items remain readable but cannot be sold");
  var first=TownServiceMerchantHandoff.OwnedChips[0];
  Check(Mathf.Abs(first.transform.parent.lossyScale.x-scale)<.01f,"glove armature scale cannot enlarge item fan");
  Check(!TownServiceMerchantHandoff.Offer(first.Item!,true,palm.position+palm.right*scale),"release outside palm cannot open merchant");
  Check(MapRoomDriver.Visits==0,"outside release leaves map state unchanged");
  // The same card object remains in hand through a wrist-close; its return is network-visible.
  first.Holder=VRHands.Right; VRHands.Right.Grabber.Held=first;
  VRHands.Left.PalmGate.IsOpen=false; TownServiceMerchantHandoff.Tick();
  Check(TownServiceMerchantHandoff.OwnedChips.Count==31,"closing animation remains published until completion");
  Check(first.Holder==VRHands.Right,"closing fan never tears a held card away");
  VRHands.Right.Grabber.Held=null; first.Holder=null;
  // Native window opening may complete on a subsequent frame; no hidden purchase is allowed.
  Check(TownServiceMerchantHandoff.Offer(first.Item!,true,palm.position),"actual owned release requests merchant");
  Check(TownServiceMerchantTransaction.Requests==0,"release waits for native inventory readiness");
  window.GetComponent<UIWindow>().IsOpen=true;
  window.ItemInventory.character=new CMapCharacter(); TownServiceMerchantHandoff.Tick();
  Check(TownServiceMerchantTransaction.Requests==0,"native inventory for another character cannot receive offer");
  window.ItemInventory.character=character; TownServiceMerchantHandoff.Tick();
  Check(TownServiceMerchantTransaction.Requests==1 && TownServiceMerchantTransaction.LastSelling,"owned release dispatches exact sell confirmation");
  Check(ReferenceEquals(TownServiceMerchantTransaction.LastItem,first.Item),"sale preserves item copy identity");
  Check(Singleton<UIItemConfirmationBox>.Instance!.IsActive,"final native confirmation remains visibly pending");
  Check(!TownServiceMerchantHandoff.Offer(duplicate,true,palm.position),"pending native confirmation excludes another offer");
  // Leaving cancels only our own confirmation and restores the original hand.
  int restore=MapRoomHand.NormalRebuilds; TownServicePopulation.Station.Near=false; TownServiceMerchantHandoff.Tick();
  Check(!TownServiceMerchantHandoff.Active && MapRoomHand.NormalRebuilds==restore+1,"leaving restores normal fan exactly once");
  Check(Singleton<UIItemConfirmationBox>.Instance.Cancels==1,"leaving cancels own unconfirmed sale");
  Check(ItemsPile.InspectionCurrent==null,"leaving clears laser owner");
  GuildmasterDestinations.Mode=EGuildmasterMode.None; TownServicePopulation.Station.Near=true;
  FFSNet.FFSNetwork.IsOnline=true; character.IsUnderMyControl=false; TownServiceMerchantHandoff.Tick();
  Check(!TownServiceMerchantHandoff.Active,"remote character cannot open an owned-item fan");
  character.IsUnderMyControl=true; VRHands.Left.PalmGate.IsOpen=true; TownServiceMerchantHandoff.Tick(); TownServiceMerchantHandoff.LateTick();
  uint session=TownServiceMerchantHandoff.Session;
  var replacement=new CMapCharacter(); replacement.AllCharacterItems.Add(new CItem{ID=90}); MapCharacterSelection.Selected=replacement;
  TownServiceMerchantHandoff.Tick(); TownServiceMerchantHandoff.LateTick();
  Check(TownServiceMerchantHandoff.Session!=session && TownServiceMerchantHandoff.OwnedChips.Count==1,"character switch replaces all fan contents and transaction scope");
  var stock=new CItem{ID=99}; AdventureState.MapState.MapParty.Stock.Add(stock); window.ItemInventory.character=replacement;
  Check(TownServiceMerchantHandoff.Offer(stock,false,palm.position),"cabinet release requests native buy");
  TownServiceMerchantHandoff.Tick(); Check(!TownServiceMerchantTransaction.LastSelling,"stock release dispatches buy confirmation");
  Action foreign=()=>{}; Singleton<UIItemConfirmationBox>.Instance._onConfirmedCallback=foreign;
  TownServiceMerchantHandoff.Reset();
  Check(Singleton<UIItemConfirmationBox>.Instance.IsActive && ReferenceEquals(Singleton<UIItemConfirmationBox>.Instance._onConfirmedCallback,foreign),"reset cannot cancel somebody else's confirmation");
  Check(TownServiceCatalog.Offer==null && TownServiceCatalog.CanOffer==null && TownServiceCatalog.InOfferingZone==null,"reset clears callback lifetime");
  UnityEngine.Object.DestroyImmediate(root);
 }
}
