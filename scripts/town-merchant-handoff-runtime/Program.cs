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
using UnityEngine.UI;
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
  ItemsPile.ItemChip.NewArtReady=true;
 TownServiceVoice.Offers=TownServiceVoice.Buys=TownServiceVoice.Sells=0;
  CanvasConversion.Releases=0;
  TownServiceMerchantHandoff.Reset(); MapRoomDriver.Active=true; MapRoomDriver.CanVisit=true;
  CardsDriver.OffScenarioFanIsOpen=true;
  CardsDriver.SuppressedOpenEdges=CardsDriver.SuppressedCloseEdges=0;
  GuildmasterDestinations.Mode=EGuildmasterMode.None; MapRoomDriver.Visits=0;
  FFSNet.FFSNetwork.IsOnline=false; StoryComposite.PointOfNoReturn=false;
  TownServiceMerchantTransaction.Requests=0; ShopService.Affordable=true;
  var root=new GameObject("Fixture"); root.transform.localScale=Vector3.one*scale; VRRigDriver.RigRoot=root.transform;
  var camera=new GameObject("Head",typeof(Camera)).GetComponent<Camera>(); camera.transform.SetParent(root.transform,false); VRRigDriver.HeadCamera=camera;
  var attention=new ActualAttention{_root=root.transform};
  camera.transform.position=Vector3.forward*(2.35f*scale);
  Check(attention.IsLocalVisitorNear(false),"resident attention enter distance matches handoff");
  camera.transform.position=Vector3.forward*(2.6f*scale);
  Check(!attention.IsLocalVisitorNear(false) && !attention.IsLocalVisitorNear(true),
      "visitor range is fixed after a previous merchant interaction");
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
  Singleton<UIItemConfirmationBox>.Instance.confirmButton=new GameObject("Native Confirm",typeof(RectTransform),typeof(Button)).GetComponent<Button>();
  var character=new CMapCharacter(); var duplicate=new CItem{ID=1};
  for(int i=0;i<30;i++) character.AllCharacterItems.Add(new CItem{ID=i}); character.AllCharacterItems.Add(duplicate);
  character.AllCharacterItems[4].Tradeable=false; MapCharacterSelection.Selected=character;
  window.ItemInventory.character=character; window.ItemInventory.service=new ShopService(AdventureState.MapState.MapParty,_=>{});
  ProveInspectionReturnInvariant(root.transform, character.AllCharacterItems[0]);
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
  Check(CardsDriver.SuppressedCloseEdges==1 && CardsDriver.SuppressedOpenEdges==0,
      "merchant inspection silences exactly the automatic open fan close edge");
  Check(!TownServiceMerchantHandoff.WantsOffering && !zone.gameObject.activeSelf,"nearby empty hands never request merchant palm");
  Check(!TownServiceMerchantHandoff.CanOffer(character.AllCharacterItems[4],true),"nontradeable items remain readable but cannot be sold");
  var first=TownServiceMerchantHandoff.OwnedChips[0];
  Check(Mathf.Abs(first.transform.parent.lossyScale.x-scale)<.01f,"glove armature scale cannot enlarge item fan");
  Check(!TownServiceMerchantHandoff.Offer(first.Item!,true,palm.position+palm.right*scale),"release outside palm cannot open merchant");
  Check(MapRoomDriver.Visits==0,"outside release leaves map state unchanged");
  // The same card object remains in hand through a wrist-close; its return is network-visible.
  first.Holder=VRHands.Right; VRHands.Right.Grabber.Held=first;
  TownServiceMerchantHandoff.LateTick();
  Check(TownServiceMerchantHandoff.WantsOffering && zone.gameObject.activeSelf,"eligible held owned item requests merchant palm");
  VRHands.Left.PalmGate.IsOpen=false; TownServiceMerchantHandoff.Tick();
  Check(TownServiceMerchantHandoff.OwnedChips.Count==31,"closing animation remains published until completion");
  Check(first.Holder==VRHands.Right,"closing fan never tears a held card away");
  VRHands.Right.Grabber.Held=null; first.Holder=null;
  // Native window opening may complete on a subsequent frame; no hidden purchase is allowed.
  first.Release(palm.position);
  Check(first.TownOffering && MapRoomDriver.Visits==1,"actual owned release parks the original card and requests merchant");
  object offering=typeof(TownServiceMerchantHandoff).GetField("_offering",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)!;
  float offeredScale=(float)offering.GetType().GetField("_scale",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(offering)!;
  float offeredWorldWidth=first.FaceWidth*offeredScale*first.transform.parent.lossyScale.x;
  Check(Mathf.Abs(offeredWorldWidth-TownServiceMerchantLayout.CardWidth*scale*1.5f)<.0001f,
      "owned and cabinet cards have one merchant-palm size");
  Check(MapRoomDriver.LastSuppressed,"automatic merchant entry suppresses the flat button sound");
  Check(first.transform.parent == zone.parent,"actual owned item shares the floating offering frame");
  Check(TownServiceMerchantTransaction.Requests==0,"release waits for native inventory readiness");
  window.GetComponent<UIWindow>().IsOpen=true;
  window.ItemInventory.character=new CMapCharacter(); TownServiceMerchantHandoff.Tick();
  Check(TownServiceMerchantTransaction.Requests==0,"native inventory for another character cannot receive offer");
  Check(first.TownOffering && !first.IsCollapsing,"closed wrist fan retains actual pending offering");
  window.ItemInventory.character=character; TownServiceMerchantHandoff.Tick();
  Check(TownServiceMerchantTransaction.Requests==1 && TownServiceMerchantTransaction.LastSelling,"owned release dispatches exact sell confirmation");
  Check(TownServiceVoice.Sells==1&&TownServiceVoice.Buys==0&&TownServiceVoice.Offers==0,
      "merchant chooses the seller voice family when a sale confirmation opens");
  Check(ReferenceEquals(TownServiceMerchantTransaction.LastItem,first.Item),"sale preserves item copy identity");
  Check(Singleton<UIItemConfirmationBox>.Instance!.IsActive,"final native confirmation remains visibly pending");
  Check(TownServiceMerchantHandoff.WantsOffering && TownServiceMerchantHandoff.CanReclaim(first),"exact pending card keeps palm and can be reclaimed");
  Check(first.AllowsHand(VRHands.Left)&&first.AllowsHand(VRHands.Right),"parked owned item bypasses wrist fan election for both hands");
  first.Holder=VRHands.Left; first.TownOffering=false;
  Check(first.AllowsHand(VRHands.Left),"reclaimed item remains valid for its fan-owning holder");
  first.Holder=null; first.TownOffering=true;
  var second=TownServiceMerchantHandoff.OwnedChips[1];
  Check(!TownServiceMerchantHandoff.CanReclaim(second),"unrelated item never receives pending decision exception");
  Check(!TownServiceMerchantHandoff.Offer(first.Item!,true,palm.position),"same pending card cannot replace itself");
  VRHands.Left.PalmGate.IsOpen=true; TownServiceMerchantHandoff.Tick();
  int requestsBeforeSwap=TownServiceMerchantTransaction.Requests;
  int cancelsBeforeSwap=Singleton<UIItemConfirmationBox>.Instance.Cancels;
  second.Release(palm.position);
  Check(!first.TownOffering && second.TownOffering && first.transform.parent.name=="GloomhavenVR.MerchantOwnedItems",
      "second valid item atomically replaces merchant palm and returns old card to canonical fan");
  Check(Singleton<UIItemConfirmationBox>.Instance.Cancels==cancelsBeforeSwap+1,
      "merchant swap cancels exactly the displaced native decision");
  TownServiceMerchantHandoff.Tick();
  Check(TownServiceMerchantTransaction.Requests==requestsBeforeSwap+1
      && ReferenceEquals(TownServiceMerchantTransaction.LastItem,second.Item),
      "merchant swap opens one native decision for replacement only");
  first.Release(palm.position); TownServiceMerchantHandoff.Tick();
  Check(first.TownOffering && !second.TownOffering
      && ReferenceEquals(TownServiceMerchantTransaction.LastItem,first.Item),
      "merchant palm swaps back without leaving duplicate ownership");
  character.AllCharacterItems.Remove(first.Item!);
  typeof(TownServiceMerchantHandoff).GetField("_nextItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.SetValue(null,0f);
  TownServiceMerchantHandoff.Tick();
  Check(TownServiceVoice.Sells==3,"an inventory refresh without native confirmation cannot voice another sale");
  Check(first.TownOffering && !first.IsCollapsing,"native inventory mutation cannot destroy a pending offered card");
  // Leaving cancels only our own confirmation and restores the original hand.
  int restore=MapRoomHand.NormalRebuilds;
  int cancelsBeforeLeaving=Singleton<UIItemConfirmationBox>.Instance.Cancels;
  Singleton<UIItemConfirmationBox>.Instance.OnCancelled=()=> {
   Check(!TownServiceMerchantHandoff.Active,"reentrant native cancellation observes withdrawn handoff");
   TownServiceMerchantHandoff.Tick(); TownServiceMerchantHandoff.Reset();
  }; TownServicePopulation.Station.Near=false; TownServiceMerchantHandoff.Tick();
  Check(!TownServiceMerchantHandoff.Active && MapRoomHand.NormalRebuilds==restore+1,"leaving restores normal fan exactly once");
  Check(CardsDriver.SuppressedCloseEdges==1 && CardsDriver.SuppressedOpenEdges==1,
      "merchant departure silences exactly the matching automatic fan reopen edge");
  Check(Singleton<UIItemConfirmationBox>.Instance.Cancels==cancelsBeforeLeaving+1,"leaving cancels own unconfirmed sale");
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
  Check(TownServiceVoice.Offers==0 && TownServiceVoice.Buys==1,
      "merchant chooses the buyer voice family when a purchase confirmation opens");
  Singleton<UIItemConfirmationBox>.Instance.confirmButton.onClick.Invoke();
  ItemsPile.ItemChip.NewArtReady=false;
  replacement.AllCharacterItems.Add(stock);
  Singleton<UIItemConfirmationBox>.Instance.IsActive=false;
  typeof(TownServiceMerchantHandoff).GetField("_nextItems",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.SetValue(null,0f);
  TownServiceMerchantHandoff.Tick();
  Check(TownServiceVoice.Buys==1,"confirmed native inventory change does not repeat the purchase prompt voice");
  ItemsPile.ItemChip? purchased=null;
  foreach(var chip in TownServiceMerchantHandoff.OwnedChips)if(ReferenceEquals(chip.Item,stock)){purchased=chip;break;}
  Check(purchased!=null,"purchased item joins the owned inspection census");
  var bought=purchased!;
  Check(ReferenceEquals(bought.Item,stock)&&bought.InspectionArtPending
      &&bought.transform.localScale==Vector3.zero&&!bought.GetComponent<BoxCollider>().enabled,
      "purchased item never exposes a brown backing while original art is pending");
  Check(bought.TickInspectionArtArrival(),"pending original art keeps purchased item hidden");
  bought.SetArtReady(true);
  Check(!bought.TickInspectionArtArrival()&&!bought.InspectionArtPending
      &&bought.GetComponent<BoxCollider>().enabled,"original art arrival starts one normal grabbable emergence");
  bought.AdvanceEmerge(1f);
  Check((bought.transform.localPosition-bought.Home).sqrMagnitude<.000001f
      &&Quaternion.Angle(bought.transform.localRotation,bought.HomeRotation)<.1f,
      "purchased item lands on the exact ordinary fan home pose and face rotation");
  ItemsPile.ItemChip.NewArtReady=true;
  Action foreign=()=>{}; Singleton<UIItemConfirmationBox>.Instance._onConfirmedCallback=foreign;
  Singleton<UIItemConfirmationBox>.Instance.IsActive=true;
  TownServiceMerchantHandoff.Reset();
  Check(Singleton<UIItemConfirmationBox>.Instance.IsActive && ReferenceEquals(Singleton<UIItemConfirmationBox>.Instance._onConfirmedCallback,foreign),"reset cannot cancel somebody else's confirmation");
  Check(TownServiceCatalog.Offer==null && TownServiceCatalog.CanOffer==null && TownServiceCatalog.InOfferingZone==null,"reset clears callback lifetime");
  UnityEngine.Object.DestroyImmediate(root);
 }
 private static void ProveInspectionReturnInvariant(Transform rigRoot,CItem item) {
  var fan=ItemsPile.CreateInspection((chip,point)=>{});
  fan.TickInspection(new[]{item},1);
  var chip=fan.InspectionChips[0];
  Transform fanParent=chip.transform.parent;
  var merchantPalm=new GameObject("Merchant palm").transform;merchantPalm.SetParent(rigRoot,false);
  chip.transform.SetParent(merchantPalm,true);chip.TownOffering=true;
 fan.PrepareInspectionReclaim(chip);
  Check(chip.transform.parent!=merchantPalm&&chip.transform.parent.name=="GloomhavenVR.MerchantOwnedItems",
      "reclaimed merchant item records the item fan as its release parent");
  Check(CanvasConversion.Releases==1,
      "reclaimed merchant item explicitly releases its former flat-window renderer veil");
  chip.TownOffering=false;chip.transform.SetParent(merchantPalm,true);chip.SetArtReady(false);
  fan.ResumeInspection(chip);
  Check(chip.transform.parent!=merchantPalm&&chip.transform.parent.name=="GloomhavenVR.MerchantOwnedItems",
      "every free merchant return restores the item fan parent");
  Check(CanvasConversion.Releases==2,
      "every free merchant return releases its former flat-window renderer veil before rendering");
  Check(chip.InspectionArtPending&&chip.transform.localScale==Vector3.zero&&!chip.GetComponent<BoxCollider>().enabled,
      "returned item never exposes a brown backing while its original front is unavailable");
  chip.SetArtReady(true);Check(!chip.TickInspectionArtArrival(),"returned original front resumes the ordinary fan animation");
  chip.AdvanceEmerge(1f);
  Check(Quaternion.Angle(chip.transform.localRotation,chip.HomeRotation)<.1f,
      "returned item lands front-forward at the canonical fan rotation");
  // Exercise the real production ItemChip grab/release methods repeatedly. The earlier assertions
  // called ResumeInspection directly and therefore could not observe the base grabbable recording
  // a merchant-palm parent or a held scale/rotation leaking into the next wrist-fan opening.
  var anchor=new GameObject("Rotating grab anchor").transform;anchor.SetParent(rigRoot,false);
  var hand=VRHands.Right!;hand.Rig.GrabAnchor=anchor;hand.Side=HandSide.Right;
  int reclaimed=0;
  for(int round=0;round<4;round++) {
   chip.SetArtReady(true);
   merchantPalm.localPosition=new Vector3(.14f+.03f*round,1.04f,.19f);
   merchantPalm.localRotation=Quaternion.Euler(23f+11f*round,71f-9f*round,37f+7f*round);
   chip.transform.SetParent(merchantPalm,false);
   chip.transform.localPosition=new Vector3(.02f*round,.08f,-.03f);
   chip.transform.localRotation=Quaternion.Euler(65f-3f*round,19f+17f*round,42f);
   chip.transform.localScale=Vector3.one*(.42f+.07f*round);
   chip.TownOffering=true;chip.TownOfferingReclaimed=()=>reclaimed++;
   if(round==0) {
    // The complete production wrist-close census is exercised in RunScale before this helper;
    // carry that retained card through a close/reopen state into the real grab path here.
    fan.IsOpen=false;Check(chip.TownOffering&&!chip.IsCollapsing,
        "retained art-ready merchant card survives the closed-fan interval before reclaim");
    fan.IsOpen=true;
   }
   anchor.localRotation=Quaternion.Euler(31f+round*13f,117f-round*8f,22f+round*19f);
   chip.OnGrab(hand);
   Check(ReferenceEquals(chip.Holder,hand)&&ReferenceEquals(chip.transform.parent,anchor)
       &&reclaimed==round+1,"real merchant reclaim callback adopts the original card into the rotating hand once");
   anchor.localRotation=Quaternion.Euler(74f-round*6f,33f+round*21f,91f-round*12f);
   chip.OnRelease(hand,Vector3.zero);
   for(int frame=0;frame<12;frame++)chip.AdvanceInspectionReturn(.05f);
   Check(ReferenceEquals(chip.transform.parent,fanParent),
       "repeated art-ready reclaim restores the canonical fan parent");
   Check((chip.transform.localPosition-chip.Home).sqrMagnitude<.000001f
       &&Quaternion.Angle(chip.transform.localRotation,chip.HomeRotation)<.01f
       &&Mathf.Abs(chip.transform.localScale.x-chip.HomeScale)<.0001f,
       "repeated art-ready merchant reclaim settles at the canonical fan position rotation and scale");
  }
  // Replacement/cancel returns do not pass through a grab. They must converge on the same terminal
  // pose as the four reclaim/release rounds above while the original front is already available.
  chip.transform.SetParent(merchantPalm,false);chip.transform.localRotation=Quaternion.Euler(81f,27f,63f);
  chip.transform.localScale=Vector3.one*.57f;chip.TownOffering=false;fan.ResumeInspection(chip);
  for(int frame=0;frame<12;frame++)chip.AdvanceInspectionReturn(.05f);
  Check(Quaternion.Angle(chip.transform.localRotation,chip.HomeRotation)<.01f
      &&Mathf.Abs(chip.transform.localScale.x-chip.HomeScale)<.0001f,
      "art-ready replacement or cancellation return shares the canonical terminal pose");
  fan.DestroyInspection();UnityEngine.Object.DestroyImmediate(merchantPalm.gameObject);
  UnityEngine.Object.DestroyImmediate(anchor.gameObject);
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
  var inspectionCard=fan.InspectionChips[0];Quaternion upright=inspectionCard.transform.localRotation;
  inspectionCard.State=ItemsPile.ItemChip.Visual.Spent;
  CardsConfig.FanStepDegrees(PileKind.Items).Value=previousStep;
  fan.TickInspection(items,1);
  Check(Quaternion.Angle(inspectionCard.transform.localRotation,upright)<.1f,
   "merchant inspection keeps a native spent item upright on first reveal");
  items.Values.RemoveAt(0); fan.TickInspection(items,2);
  Check(fan.InspectionChips.Count==count,"removed item retains its closing surface");
  fan.DestroyInspection(); UnityEngine.Object.DestroyImmediate(root);
 }

}
