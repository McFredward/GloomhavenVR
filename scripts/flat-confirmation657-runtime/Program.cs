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
  var retiring=new Scene();retiring.Raise(EGuildmasterMode.Temple);TownServicePresentation.Owned=true;retiring.Temple.Hide();MapDialogSeat.Tick();
  Check(retiring.Dialog.IsOpen && retiring.Pending,"immersive retiring or palm mask is not cancelled by a former flat host");
  Check(retiring.Dialog.transform.parent==retiring.Mage.transform,"immersive ownership hands back flat presentation without hiding native continuation");
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
