using System;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using HarmonyLib;
using ScenarioRuleLibrary;

public static class ConfirmationProgram
{
 private static int checks;
 private static void Check(bool value,string message) { checks++;if(!value) throw new Exception(message); }
 private static UIWindow Window(string name, Transform? parent=null)
 {
  var go=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup),typeof(UIWindow));
  if(parent!=null) go.transform.SetParent(parent,false);
  var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(1920,1080);rect.localScale=Vector3.one;
  var window=go.GetComponent<UIWindow>();window.Bind();return window;
 }
 private static ExtendedButton Button(string name,Transform parent)
 {
  var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(ExtendedButton));
  go.transform.SetParent(parent,false);return go.GetComponent<ExtendedButton>();
 }
 private sealed class ItemScene
 {
  internal readonly UIWindow Merchant=Window("Merchant"),Party=Window("New Party Display");
  internal readonly UIWindow Dialog;
  internal readonly UIItemConfirmationBox Box;
  internal readonly CItem Item=new();
  internal readonly Action ConfirmAction,CancelAction;
  internal int Confirmed,Cancelled;
  internal ItemScene(bool reset=true,bool warm=false,bool flat=true,bool quiet=false)
  {
   if(reset)MapDialogSeat.Reset();
   Singleton<UIEnhancementConfirmationBox>.Instance=null;
   GuildmasterDestinations.Windows.Clear();ModalFallback.Panels.Clear();TownServicePresentation.Quiet=quiet;TownServicePresentation.Owned=false;
   GuildmasterDestinations.Mode=EGuildmasterMode.Merchant;
   GuildmasterDestinations.Windows.Add(EGuildmasterMode.Merchant,Merchant);
   foreach(var window in new[]{Merchant,Party}) {window.Show();window.FinishTransition();if(flat)ModalFallback.Panels.Add(window,new ConvertedPanel(window));}
   Dialog=Window("UI Item Confirmation Box",Party.transform);
   var rect=(RectTransform)Dialog.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.sizeDelta=Vector2.zero;
   Box=Dialog.gameObject.AddComponent<UIItemConfirmationBox>();
   var holder=new GameObject("Original picture holder",typeof(RectTransform));holder.transform.SetParent(Dialog.transform,false);
   Box.Bind((RectTransform)holder.transform,Button("Confirm",Dialog.transform),Button("Cancel",Dialog.transform));
   Singleton<UIItemConfirmationBox>.Instance=Box;
   ConfirmAction=()=>Confirmed++;CancelAction=()=>Cancelled++;
   if(warm)MapDialogSeat.Tick();
  }
  internal void Raise(BoxConfirmationType kind,bool tick=true)
  {
   Box.ShowConfirmation(kind,"native item title","native item information",Item,ConfirmAction,"BUY","CANCEL","native item cue",CancelAction);
   Dialog.FinishTransition();if(tick)MapDialogSeat.Tick();
  }
  internal void Finish() { Dialog.FinishTransition();MapDialogSeat.Tick(); }
 }
 private static void CheckItemOwned()
 {
  var scene=new ItemScene();scene.Raise(BoxConfirmationType.Buy);
  var rect=(RectTransform)scene.Dialog.transform;int edges=CanvasConversion.Transfers;
  var mask=new TownServiceWindowMask(rect);var wrapper=rect.parent;
  TownServicePresentation.Owned=true;
  rect.anchoredPosition3D=new Vector3(11,-7,.04f);rect.localRotation=Quaternion.Euler(17,12,3);rect.gameObject.layer=7;
  var position=rect.anchoredPosition3D;var rotation=rect.localRotation;
  Check(wrapper!=scene.Merchant.transform && wrapper.GetComponent<CanvasGroup>().alpha==0f,"item native presentation mask owns actual zero-alpha wrapper");
  MapDialogSeat.Tick();
  Check(rect.parent==wrapper,"item-owned tick cannot pull native confirmation out of its actual zero-alpha mask");
  Check(rect.anchoredPosition3D==position && Quaternion.Angle(rect.localRotation,rotation)<.01f && rect.gameObject.layer==7,
    "item ownership leaves native presentation pose and layer untouched");
  scene.Merchant.Hide();MapDialogSeat.Tick();
  Check(rect.parent==wrapper && scene.Dialog.IsOpen && scene.Confirmed==0 && scene.Cancelled==0,
    "owned merchant hide neither restores original root nor invokes a native decision");
  Check(ReferenceEquals(scene.Box.ConfirmCallback,scene.ConfirmAction) && ReferenceEquals(scene.Box.CancelCallback,scene.CancelAction) && CanvasConversion.Transfers==edges,
    "item ownership leaves both native callbacks and conversion snapshots untouched");
  scene.Box.Cancel.onClick.Invoke();scene.Dialog.FinishTransition();
  Check(scene.Cancelled==1,"original item cancellation still completes while masked");
  mask.Dispose();TownServicePresentation.Owned=false;
  for(int i=0;i<3;i++) {MapDialogSeat.Tick();Check(rect.parent==wrapper,"item retirement preserves original root in mask");TownServiceWindowMask.TickRetirements();}
  MapDialogSeat.Tick();
  Check(rect.parent==scene.Party.transform && CanvasConversion.Transfers==edges+1,"retired item mask restores exact native party home once");
 }
 public static int RunItems(string variant)
 {
  checks=0;CanvasConversion.Transfers=0;
  var harmony=new Harmony("ghvr.item-confirmation."+typeof(ConfirmationProgram).Assembly.GetName().Name);
  var patch=typeof(MapDialogSeat).Assembly.GetType("GloomhavenVR.WorldUI.MapItemConfirmationRequestObserver");
  if(patch!=null)harmony.PatchAll(patch);
  try
  {
   if(patch!=null)Check(Harmony.GetPatchInfo(typeof(UIItemConfirmationBox).GetMethod("ShowConfirmation")).Prefixes.Count==1,
     "actual observer prefix binds installed native ShowConfirmation signature");
   // Historical once-only subscription is warmed, then falsified by its actual
   // replacement. The missing-request-edge mutation instead fails first discovery.
   var first=new ItemScene(warm:variant is "item-published669" or "item-subscribe-once");
   first.Raise(BoxConfirmationType.Buy);
   Check(first.Dialog.transform.parent==first.Merchant.transform,"first native request before any modal tick seats on merchant");
   Check(first.Box.RequestObservers==1,"one native request observer on initial instance");
   Check(((RectTransform)first.Dialog.transform).rect.size==new Vector2(1920,1080),"item seating preserves measured original stretch size");
   Check(first.Box.Title=="native item title" && first.Box.Information=="native item information" && first.Box.Card!=null && ReferenceEquals(first.Box.Card.item,first.Item),
     "native title information and original item construction survive seating");
   Check(ReferenceEquals(first.Box.ConfirmCallback,first.ConfirmAction) && ReferenceEquals(first.Box.CancelCallback,first.CancelAction),"native item callbacks are not replaced");
   for(int i=0;i<5;i++)MapDialogSeat.Tick();
   Check(first.Box.RequestObservers==1,"stable ticks never duplicate native event subscriptions");
   first.Box.Confirm.onClick.Invoke();
   Check(first.Confirmed==1 && first.Cancelled==0 && !first.Dialog.IsOpen && first.Dialog.IsVisible,"native item confirm commits once before its original fade finishes");
   MapDialogSeat.Tick();Check(first.Dialog.transform.parent==first.Merchant.transform,"confirmed item remains seated throughout visible fade");
   first.Finish();
   Check(first.Dialog.transform.parent==first.Party.transform && first.Box.Card==null && first.Box.Area.Destroys==1,"native confirm cleanup finishes before exact item home restoration");
   first.Raise(BoxConfirmationType.Sell);first.Box.Cancel.onClick.Invoke();
   Check(first.Cancelled==0 && first.Dialog.IsVisible,"native item cancel waits for original hidden completion");
   MapDialogSeat.Tick();Check(first.Dialog.transform.parent==first.Merchant.transform,"cancelled item remains seated throughout visible fade");
   first.Finish();Check(first.Cancelled==1 && first.Confirmed==1,"native item cancellation runs once after original fade");
   first.Raise(BoxConfirmationType.Buy);
   // Returning to the menu resets the module but the old implementation kept its
   // once-only subscription flag. The second save creates a fresh native box.
   MapDialogSeat.Reset();
   var replacement=new ItemScene(reset:false);
   replacement.Raise(BoxConfirmationType.Sell);
   Check(replacement.Dialog.transform.parent==replacement.Merchant.transform,"replacement merchant request before first tick seats on exact new host");
   Check(first.Dialog.transform.parent==first.Party.transform,"instance replacement restores exact old native home without touching gameplay");
   Check(first.Confirmed==1 && first.Cancelled==1,"instance replacement does not invoke either old native transaction");
   Check(first.Box.RequestObservers==0,"replacement detaches the exact old native event target");
   Check(replacement.Box.RequestObservers==1,"replacement binds exactly one new native event target");
   replacement.Box.Cancel.onClick.Invoke();replacement.Finish();
   Check(replacement.Cancelled==1 && replacement.Confirmed==0,"replacement cancellation still uses original native callback");
   replacement.Raise(BoxConfirmationType.General);
   Check(replacement.Dialog.transform.parent==replacement.Party.transform,"General request on current instance stays in original party display");
   replacement.Box.Confirm.onClick.Invoke();replacement.Finish();
   Check(replacement.Confirmed==1 && replacement.Cancelled==1,"General continuation still uses original native callback");
   replacement.Raise(BoxConfirmationType.Buy);MapDialogSeat.Reset();
   Check(replacement.Dialog.transform.parent==replacement.Party.transform && replacement.Dialog.IsOpen,"reset restores original home without closing native item choice");
   Check(replacement.Box.RequestObservers==0,"reset detaches exact live native event target");
   replacement.Box.Cancel.onClick.Invoke();replacement.Dialog.FinishTransition();
   replacement.Raise(BoxConfirmationType.General);
   Check(replacement.Dialog.transform.parent==replacement.Party.transform && replacement.Box.RequestObservers==1,"request immediately after reset rebinds General provenance before tick");
   replacement.Box.Cancel.onClick.Invoke();replacement.Finish();
   replacement.Raise(BoxConfirmationType.Buy);
   var unknown=new ItemScene(reset:false);
   unknown.Dialog.Show();unknown.Dialog.FinishTransition();MapDialogSeat.Tick();
   Check(unknown.Dialog.transform.parent==unknown.Party.transform,"unknown request on replacement cannot inherit old merchant provenance");
   Check(replacement.Box.RequestObservers==0,"tick replacement also detaches exact old event target");
   unknown.Box.Hide();unknown.Finish();
   unknown.Raise(BoxConfirmationType.Buy);unknown.Merchant.Hide();MapDialogSeat.Tick();
   Check(unknown.Dialog.transform.parent==unknown.Party.transform && unknown.Dialog.IsOpen && unknown.Confirmed==0 && unknown.Cancelled==0,
     "merchant host closure restores item home and preserves native pending decision");
   unknown.Box.Cancel.onClick.Invoke();unknown.Finish();
   Check(unknown.Cancelled==1,"native cancellation after host closure retains original callback");
   var newGeneral=new ItemScene(reset:false);newGeneral.Raise(BoxConfirmationType.General);
   Check(newGeneral.Dialog.transform.parent==newGeneral.Party.transform,"General request on new instance cannot inherit old trade provenance");
   newGeneral.Box.Cancel.onClick.Invoke();newGeneral.Finish();
   var quiet=new ItemScene(flat:false,quiet:true);quiet.Raise(BoxConfirmationType.Buy);quiet.Merchant.Hide();MapDialogSeat.Tick();
   Check(quiet.Dialog.transform.parent==quiet.Party.transform && quiet.Dialog.IsOpen,"quiet immersive merchant controller has no flat seat or cancellation owner");
   quiet.Box.Cancel.onClick.Invoke();quiet.Finish();
   CheckItemOwned();
   MapDialogSeat.Reset();Singleton<UIItemConfirmationBox>.Instance=null;MapDialogSeat.Tick();
   Check(!Singleton<UIItemConfirmationBox>.IsInitialized,"missing item singleton remains a safe teardown observation");
   return checks;
  }
  finally {harmony.UnpatchSelf();MapDialogSeat.Reset();}
 }
 public static int RunPrefixFault(string variant)
 {
  checks=0;Probe.ObservationFaults=0;GloomhavenVR.Core.VRLog.Alerts.Clear();
  var harmony=new Harmony("ghvr.item-confirmation-fault."+typeof(ConfirmationProgram).Assembly.GetName().Name);
  harmony.PatchAll(typeof(MapDialogSeat).Assembly.GetType("GloomhavenVR.WorldUI.MapItemConfirmationRequestObserver"));
  try
  {
   var scene=new ItemScene();int nativeRequests=0;BoxConfirmationType? nativeKind=null;
   scene.Box.ConfirmationBoxRequested += kind=>{nativeRequests++;nativeKind=kind;};
   bool continued=true;
   try {scene.Raise(BoxConfirmationType.Buy);}catch(InvalidOperationException){continued=false;}
   Check(continued && scene.Dialog.IsOpen && scene.Box.IsActive,"observation failure leaves native Show continuation intact");
   Check(nativeRequests==1 && nativeKind==BoxConfirmationType.Buy,"native Buy announcement still reaches original independent observer after presentation failure");
   Check(scene.Box.Card!=null && ReferenceEquals(scene.Box.Card.item,scene.Item) && scene.Box.Title=="native item title","native Show constructs original item content after presentation failure");
   Check(ReferenceEquals(scene.Box.ConfirmCallback,scene.ConfirmAction) && ReferenceEquals(scene.Box.CancelCallback,scene.CancelAction),"presentation failure preserves both original native callback delegates");
   Check(scene.Dialog.transform.parent==scene.Party.transform,"failed presentation observation leaves original native home intact");
   scene.Box.Confirm.onClick.Invoke();scene.Finish();
   Check(scene.Confirmed==1 && scene.Cancelled==0 && scene.Box.Area.Destroys==1,"native confirmation and hidden cleanup complete exactly once after observation failure");
   scene.Raise(BoxConfirmationType.Sell);
   Check(nativeRequests==2 && nativeKind==BoxConfirmationType.Sell && scene.Dialog.IsOpen,"second failed observation still permits native Sell announcement and Show");
   scene.Box.Cancel.onClick.Invoke();Check(scene.Cancelled==0,"failed observation preserves original cancellation fade ordering");
   scene.Finish();Check(scene.Cancelled==1 && scene.Confirmed==1,"native cancellation completes once after repeated observation failure");
   Check(Probe.ObservationFaults==2,"failure-path proof exercises actual prefix twice");
   Check(GloomhavenVR.Core.VRLog.Alerts.Count==1 && GloomhavenVR.Core.VRLog.Alerts[0].Contains("observing native item confirmation failed"),"repeated observation failures emit one bounded useful diagnostic");
   return checks;
  }
  finally {harmony.UnpatchSelf();MapDialogSeat.Reset();}
 }
 private sealed class Scene
 {
  internal readonly UIWindow Temple=Window("Temple"), Mage=Window("Enchantress");
  internal readonly UIWindow Dialog;
  internal readonly UIEnhancementConfirmationBox Box;
  internal int Confirmed, Cancelled;
  internal bool Pending;
  internal Scene(bool flat=true,bool quiet=false)
  {
   MapDialogSeat.Reset();Singleton<UIItemConfirmationBox>.Instance=null;
   Singleton<UINavigation>.Instance=new UINavigation();
   GuildmasterDestinations.Windows.Clear();ModalFallback.Panels.Clear();Probe.Events.Clear();TownServicePresentation.Quiet=quiet;TownServicePresentation.Owned=false;
   GuildmasterDestinations.Windows.Add(EGuildmasterMode.Temple,Temple);GuildmasterDestinations.Windows.Add(EGuildmasterMode.Enchantress,Mage);
   foreach(var window in new[]{Temple,Mage}) {window.Show();window.FinishTransition();if(flat)ModalFallback.Panels.Add(window,new ConvertedPanel(window));}
   Dialog=Window("UI Enhancement Confirmation Box",Mage.transform);
   var rect=(RectTransform)Dialog.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.sizeDelta=Vector2.zero;
   // Stretch geometry is now 1920x1080; production measures before collapsing it.
   Box=Dialog.gameObject.AddComponent<UIEnhancementConfirmationBox>();
   Box.Bind(Dialog.gameObject.AddComponent<Image>(),Button("Confirm",Dialog.transform),Button("Cancel",Dialog.transform));
   Singleton<UIEnhancementConfirmationBox>.Instance=Box;
   MapDialogSeat.Tick();
  }
  internal void Raise(EGuildmasterMode mode)
  {
   GuildmasterDestinations.Mode=mode;Pending=true;
   Box.ShowConfirmation("title","information",null!,"blessing",()=>{Pending=false;Confirmed++;},null!,null!,()=>{Pending=false;Cancelled++;});
   Dialog.FinishTransition();MapDialogSeat.Tick();
  }
  internal void Switch(EGuildmasterMode next)
  {
   Probe.Events.Clear();var before=GuildmasterDestinations.Mode;
   Probe.Events.Add("exit:"+before);GuildmasterDestinations.ModeWindow(before)!.Hide();
   Check(!Dialog.IsOpen,"native prompt cancels before next destination enters");
   GuildmasterDestinations.Mode=next;Probe.Events.Add("enter:"+next);
   MapDialogSeat.Tick();
  }
  internal void Finish() { Dialog.FinishTransition();MapDialogSeat.Tick(); }
 }
 private static void PendingOutcomeOnHostClose(bool confirm)
 {
  var scene=new Scene();scene.Raise(EGuildmasterMode.Temple);
  if(confirm)scene.Box.Confirm.onClick.Invoke();else scene.Box.Cancel.onClick.Invoke();
  Check(!scene.Dialog.IsOpen && scene.Dialog.IsVisible,"chosen outcome has an actual pending native fade");
  scene.Switch(EGuildmasterMode.Enchantress);
  scene.Mage.Show();scene.Mage.FinishTransition();scene.Raise(EGuildmasterMode.Enchantress);
  Check(confirm ? scene.Confirmed==1 && scene.Cancelled==0 : scene.Cancelled==1 && scene.Confirmed==0,
    confirm ? "pending confirmed fade completes before singleton reuse" : "pending cancelled fade completes before singleton reuse");
  scene.Dialog.FinishTransition();
  Check(scene.Dialog.IsOpen,"old chosen-outcome completion cannot hide a new request");
  Check(Probe.Events.FindAll(item=>item.StartsWith("previous:")).Count==0,"finishing an already hidden outcome emits no additional navigation hide");
  scene.Box.Cancel.onClick.Invoke();scene.Finish();
  Check(confirm ? scene.Confirmed==1 && scene.Cancelled==1 : scene.Confirmed==0 && scene.Cancelled==2,
    "old chosen outcome and new cancellation each complete exactly once");
 }
 private static void CheckOwnedWrapper()
 {
  var scene=new Scene();scene.Raise(EGuildmasterMode.Temple);
  var rect=(RectTransform)scene.Dialog.transform;
  var callback=scene.Box.ConfirmCallback;
  int edges=CanvasConversion.Transfers;
  rect.GetComponent<CanvasGroup>().ignoreParentGroups=true;
  var mask=new TownServiceWindowMask(rect);
  var wrapper=rect.parent;
  Check(wrapper!=scene.Temple.transform && wrapper.GetComponent<CanvasGroup>().alpha==0f,
    "actual native presentation mask owns a zero-alpha wrapper");
  TownServicePresentation.Owned=true;
  var ownedPosition=new Vector3(25f,-17f,.04f);var ownedRotation=Quaternion.Euler(17f,12f,3f);
  rect.anchoredPosition3D=ownedPosition;rect.localRotation=ownedRotation;rect.gameObject.layer=7;
  MapDialogSeat.Tick();
  Check(rect.parent==wrapper,"owned tick cannot pull the native root out of its zero-alpha mask");
  Check(rect.anchoredPosition3D==ownedPosition && Quaternion.Angle(rect.localRotation,ownedRotation)<.01f && rect.gameObject.layer==7,
    "owned tick leaves the presentation owner's pose and layer untouched");
  scene.Temple.Hide();MapDialogSeat.Tick();
  Check(rect.parent==wrapper && scene.Dialog.IsOpen && scene.Pending && scene.Confirmed==0 && scene.Cancelled==0,
    "owned host closing preserves the masked native root and pending callback");
  Check(ReferenceEquals(callback,scene.Box.ConfirmCallback) && CanvasConversion.Transfers==edges,
    "owned ticks neither replace callback nor transfer presentation snapshots");
  scene.Box.Cancel.onClick.Invoke();scene.Dialog.FinishTransition();
  Check(scene.Cancelled==1 && !scene.Pending,"original native cancellation still completes while presentation is masked");
  mask.Dispose();TownServicePresentation.Owned=false;
  MapDialogSeat.Tick();
  Check(rect.parent==wrapper && TownServiceWindowMask.OwnsRetiring(scene.Dialog),
    "closed native confirmation remains inside its actual retiring mask");
  for(int i=0;i<2;i++) {TownServiceWindowMask.TickRetirements();MapDialogSeat.Tick();}
  Check(rect.parent==wrapper && CanvasConversion.Transfers==edges,
    "all owned retirement ticks preserve wrapper and original flat snapshot");
  TownServiceWindowMask.TickRetirements();MapDialogSeat.Tick();
  Check(rect.parent==scene.Mage.transform && !TownServiceWindowMask.OwnsRetiring(scene.Dialog),
    "only actual mask disposal permits original native home restoration");
  Check(rect.anchorMin==Vector2.zero && rect.anchorMax==Vector2.one && rect.sizeDelta==Vector2.zero,
    "released ownership restores original authored stretch geometry");
  Check(rect.GetComponent<CanvasGroup>().ignoreParentGroups && CanvasConversion.Transfers==edges+1,
    "mask and flat ownership each restore once without an extra transfer");
 }
 public static int RunOwned(string variant) { checks=0;CheckOwnedWrapper();MapDialogSeat.Reset();return checks; }
 public static int Run(string variant)
 {
  checks=0;CanvasConversion.Transfers=0;
  var scene=new Scene();scene.Raise(EGuildmasterMode.Temple);
  Check(scene.Dialog.transform.parent==scene.Temple.transform,"temple request seats on actual raising host");
  Check(((RectTransform)scene.Dialog.transform).rect.size==new Vector2(1920,1080),"stretch dialog preserves original measured size");
  GuildmasterDestinations.Mode=EGuildmasterMode.Enchantress;MapDialogSeat.Tick();
  Check(scene.Dialog.transform.parent==scene.Temple.transform,"destination change does not reidentify an existing temple request");
  GuildmasterDestinations.Mode=EGuildmasterMode.Temple;
  scene.Box.Cancel.onClick.Invoke();Check(!scene.Dialog.IsOpen && scene.Pending,"cancel uses native hidden completion rather than early callback");
  scene.Finish();Check(scene.Cancelled==1 && !scene.Pending,"cancel native completion clears temple pending flag exactly once");
  Check(scene.Dialog.transform.parent==scene.Mage.transform,"completed cancellation restores original native home");
  Check(((RectTransform)scene.Dialog.transform).anchorMin==Vector2.zero && ((RectTransform)scene.Dialog.transform).anchorMax==Vector2.one,"native stretch anchors restored");
  scene.Raise(EGuildmasterMode.Temple);scene.Box.Confirm.onClick.Invoke();
  Check(scene.Confirmed==0,"confirmation transaction waits for native fade completion");
  scene.Finish();Check(scene.Confirmed==1 && scene.Box.Area.Destroys==2,"original confirm commits once after cancellation and reopen");
  scene.Raise(EGuildmasterMode.Temple);scene.Switch(EGuildmasterMode.Enchantress);
  Check(!scene.Dialog.IsVisible && !scene.Pending,"departing host completes native cancellation before another request");
  Check(Probe.Events.IndexOf("previous:Temple") < Probe.Events.IndexOf("enter:Enchantress"),"native previous-state restore precedes entering new destination");
  scene.Finish();Check(scene.Cancelled==2 && !scene.Pending,"destination switch executes native cancellation once");
  scene.Mage.Show();scene.Mage.FinishTransition();scene.Raise(EGuildmasterMode.Enchantress);scene.Switch(EGuildmasterMode.Temple);
  Check(!scene.Dialog.IsVisible && !scene.Pending,"reverse switch closes native prompt before entering temple");
  scene.Finish();Check(scene.Cancelled==3,"reverse switch cancels through same native completion");
  scene.Temple.Show();scene.Temple.FinishTransition();scene.Raise(EGuildmasterMode.Temple);
  int immediateCancels=scene.Cancelled;scene.Switch(EGuildmasterMode.Enchantress);
  scene.Mage.Show();scene.Mage.FinishTransition();scene.Raise(EGuildmasterMode.Enchantress);
  Check(scene.Cancelled==immediateCancels+1 && scene.Dialog.IsOpen,"immediate new request cannot reset away the outgoing temple cancellation");
  scene.Dialog.FinishTransition();Check(scene.Dialog.IsOpen,"old completion cannot hide the new enchantress request");
  scene.Box.Cancel.onClick.Invoke();scene.Finish();
  scene.Temple.Show();scene.Temple.FinishTransition();scene.Raise(EGuildmasterMode.Temple);
  scene.Box.Confirm.onClick.Invoke();scene.Temple.Hide();scene.Finish();
  Check(scene.Confirmed==2 && scene.Cancelled==5,"host hiding after confirmation cannot turn committed continuation into cancellation");
  scene.Temple.Show();scene.Temple.FinishTransition();scene.Raise(EGuildmasterMode.Temple);
  MapDialogSeat.Reset();scene.Temple.Hide();
  Check(scene.Dialog.IsOpen,"reset detaches host cancellation without writing a native transaction");
  scene.Box.Cancel.onClick.Invoke();scene.Finish();
  Check(CanvasConversion.Transfers>=12,"seating and home restoration call explicit conversion handoff edges");
  var quiet=new Scene(false,true);quiet.Raise(EGuildmasterMode.Temple);quiet.Temple.Hide();
  Check(quiet.Dialog.IsOpen && quiet.Dialog.transform.parent==quiet.Mage.transform,"unconverted immersive controller has no flat cancellation or seating owner");
  CheckOwnedWrapper();
  var unconverted=new Scene(false);unconverted.Raise(EGuildmasterMode.Temple);unconverted.Switch(EGuildmasterMode.Enchantress);unconverted.Finish();
  Check(unconverted.Cancelled==1 && !unconverted.Pending,"flat host closing before conversion still cancels native pending choice");
  var late=new Scene();MapDialogSeat.Reset();late.Raise(EGuildmasterMode.Temple);
  Check(late.Dialog.transform.parent==late.Temple.transform,"already-open discovery resolves actual mode once");
  int callbacks=late.Cancelled;late.Switch(EGuildmasterMode.Enchantress);late.Finish();
  Check(late.Cancelled==callbacks+1,"already-open discovery still cancels on original host edge");
  PendingOutcomeOnHostClose(variant=="lost-confirm-fade");
  PendingOutcomeOnHostClose(variant!="lost-confirm-fade");
  MapDialogSeat.Reset();
  return checks;
 }
}
