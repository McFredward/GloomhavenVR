using System;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;

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
