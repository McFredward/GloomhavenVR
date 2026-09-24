using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
  Exception? benchmarkFailure=null;
  foreach(int size in new[]{31,512}) try { Benchmark(size); } catch(Exception error) { benchmarkFailure ??= error; }
  if(benchmarkFailure!=null) throw benchmarkFailure;
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
  camera.transform.position = Vector3.forward * (1.2f * scale);
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
  Transform zone = TownServiceMerchantHandoff.Zone!;
  Check(Vector3.Dot(zone.up, Vector3.up) > .999f, "offering overlay is upright over the palm");
  Check(Mathf.Abs((zone.position.y - palm.position.y) / scale - .17f) < .007f, "whole offering portrait clears the palm");
  Check(((RectTransform)zone).sizeDelta.y > ((RectTransform)zone).sizeDelta.x, "offering overlay is portrait shaped");
  var animated = new GameObject("PoseProbe").transform; animated.SetParent(root.transform, false);
  palm.localRotation = Quaternion.Euler(70f, 31f, 43f);
  TownServiceOfferingPose.Place(animated, palm, root.transform, 0f); Vector3 start = animated.position;
  TownServiceOfferingPose.Place(animated, palm, root.transform, 1f);
  Check((animated.position - start).magnitude / scale > .002f, "offering suspension has visible gentle continuous motion");
  Check(Vector3.Dot(animated.up, Vector3.up) > .999f, "wrist roll cannot flatten the offering portrait");
  palm.localRotation = Quaternion.identity;

  Check(TownServiceMerchantHandoff.OwnedChips.Count==31,"all equipped and bound copies become actual inspection cards");
  Check(TownServiceMerchantHandoff.Active,"local owned fan opens near resident");
  Check(!TownServiceMerchantHandoff.WantsOffering,"nearby empty hands never request merchant palm");
  Check(!TownServiceMerchantHandoff.CanOffer(character.AllCharacterItems[4],true),"nontradeable items remain readable but cannot be sold");
  var first=TownServiceMerchantHandoff.OwnedChips[0];
  Check(Mathf.Abs(first.transform.parent.lossyScale.x-scale)<.01f,"glove armature scale cannot enlarge item fan");
  Check(!TownServiceMerchantHandoff.Offer(first.Item!,true,palm.position+palm.right*scale),"release outside palm cannot open merchant");
  Check(MapRoomDriver.Visits==0,"outside release leaves map state unchanged");
  // The same card object remains in hand through a wrist-close; its return is network-visible.
  first.Holder=VRHands.Right; VRHands.Right.Grabber.Held=first;
  Check(TownServiceMerchantHandoff.WantsOffering,"eligible held owned item requests merchant palm");
  VRHands.Left.PalmGate.IsOpen=false; TownServiceMerchantHandoff.Tick();
  Check(TownServiceMerchantHandoff.OwnedChips.Count==31,"closing animation remains published until completion");
  Check(first.Holder==VRHands.Right,"closing fan never tears a held card away");
  VRHands.Right.Grabber.Held=null; first.Holder=null;
  // Native window opening may complete on a subsequent frame; no hidden purchase is allowed.
  first.Release(palm.position);
  Check(first.TownOffering && MapRoomDriver.Visits==1,"actual owned release parks the original card and requests merchant");
  Check(first.transform.parent == zone.parent,"actual owned item shares the floating offering frame");
  Check(TownServiceMerchantTransaction.Requests==0,"release waits for native inventory readiness");
  window.GetComponent<UIWindow>().IsOpen=true;
  window.ItemInventory.character=new CMapCharacter(); TownServiceMerchantHandoff.Tick();
  Check(TownServiceMerchantTransaction.Requests==0,"native inventory for another character cannot receive offer");
  Check(first.TownOffering && !first.IsCollapsing,"closed wrist fan retains actual pending offering");
  window.ItemInventory.character=character; TownServiceMerchantHandoff.Tick();
  Check(TownServiceMerchantTransaction.Requests==1 && TownServiceMerchantTransaction.LastSelling,"owned release dispatches exact sell confirmation");
  Check(ReferenceEquals(TownServiceMerchantTransaction.LastItem,first.Item),"sale preserves item copy identity");
  Check(Singleton<UIItemConfirmationBox>.Instance!.IsActive,"final native confirmation remains visibly pending");
  Check(TownServiceMerchantHandoff.WantsOffering && TownServiceMerchantHandoff.CanReclaim(first),"exact pending card keeps palm and can be reclaimed");
  Check(first.AllowsHand(VRHands.Left)&&first.AllowsHand(VRHands.Right),"parked owned item bypasses wrist fan election for both hands");
  first.Holder=VRHands.Left; first.TownOffering=false;
  Check(first.AllowsHand(VRHands.Left),"reclaimed item remains valid for its fan-owning holder");
  first.Holder=null; first.TownOffering=true;
  Check(!TownServiceMerchantHandoff.CanReclaim(TownServiceMerchantHandoff.OwnedChips[1]),"unrelated item never receives pending decision exception");
  Check(!TownServiceMerchantHandoff.Offer(duplicate,true,palm.position),"pending native confirmation excludes another offer");
  character.AllCharacterItems.Remove(first.Item!);
  typeof(TownServiceMerchantHandoff).GetField("_nextItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.SetValue(null,0f);
  TownServiceMerchantHandoff.Tick();
  Check(first.TownOffering && !first.IsCollapsing,"native inventory mutation cannot destroy a pending offered card");
  // Leaving cancels only our own confirmation and restores the original hand.
  int restore=MapRoomHand.NormalRebuilds;
  Singleton<UIItemConfirmationBox>.Instance.OnCancelled=()=> {
   Check(!TownServiceMerchantHandoff.Active,"reentrant native cancellation observes withdrawn handoff");
   TownServiceMerchantHandoff.Tick(); TownServiceMerchantHandoff.Reset();
  }; TownServicePopulation.Station.Near=false; TownServiceMerchantHandoff.Tick();
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
 private sealed class InventoryProbe : IReadOnlyList<CItem> {
  public readonly List<CItem> Values = new(); public int Reads;
  public int Count { get { Reads++; return Values.Count; } }
  public CItem this[int i] { get { Reads++; return Values[i]; } }
  public IEnumerator<CItem> GetEnumerator() { Reads++; return Values.GetEnumerator(); }
  IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
 }
 private static void Benchmark(int count) {
  TownServiceMerchantHandoff.Reset();
  var root=new GameObject("Benchmark"); VRRigDriver.RigRoot=root.transform;
  VRHands.Left=new VRHand(); VRHands.Right=new VRHand(); VRHands.Primary=VRHands.Right;
  VRHands.Left.Rig.PalmCenter=root.transform; VRHands.Right.Rig.PalmCenter=root.transform;
  var items=new InventoryProbe(); for(int i=0;i<count;i++) items.Values.Add(new CItem{ID=i});
  var fan=ItemsPile.CreateInspection((c,p)=>{});
  var watch=Stopwatch.StartNew(); fan.TickInspection(items,1); watch.Stop();
  double creation=watch.Elapsed.TotalMilliseconds;
  int firstLayouts=fan.LayoutCalls;
  foreach(var chip in fan.InspectionChips) {
   Vector3 seed=chip.transform.localPosition;
   chip.AdvanceEmerge(.125f);
   Check((chip.transform.localPosition-seed).sqrMagnitude>0f,"original item emergence advances from its seed");
   chip.AdvanceEmerge(.25f);
   Check((chip.transform.localPosition-chip.Home).sqrMagnitude<.000001f,"original item emergence lands on final batch home");
  }
  for(int i=0;i<20;i++) fan.TickInspection(items,1);
  int reads=items.Reads, layouts=fan.LayoutCalls;
  long allocated=GC.GetAllocatedBytesForCurrentThread();
  watch.Restart(); for(int i=0;i<1000;i++) fan.TickInspection(items,1); watch.Stop();
  allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
  UnityEngine.Debug.Log($"MERCHANT_HOST count={count} creation_ms={creation:F4} steady_ms={watch.Elapsed.TotalMilliseconds/1000:F6} bytes_1000={allocated} first_layouts={firstLayouts} steady_layouts={fan.LayoutCalls-layouts} inventory_reads={items.Reads-reads}");
  Check(items.Reads==reads,"stable membership never rereads the inventory census");
  Check(firstLayouts==1 && fan.LayoutCalls==layouts,"one initial layout and no unchanged-frame relayouts");
  Check(allocated==0,"steady inspection host performs no managed allocations");
  float previousStep=CardsConfig.FanStepDegrees(PileKind.Items).Value;
  CardsConfig.FanStepDegrees(PileKind.Items).Value=previousStep+1f; fan.TickInspection(items,1);
  Check(fan.LayoutCalls==layouts+1,"live layout setting changes update exactly once without a model revision");
  CardsConfig.FanStepDegrees(PileKind.Items).Value=previousStep;
  items.Values.RemoveAt(0); fan.TickInspection(items,2);
  Check(fan.InspectionChips.Count==count,"removed item retains its closing surface");
  fan.DestroyInspection(); UnityEngine.Object.DestroyImmediate(root);
 }

}
