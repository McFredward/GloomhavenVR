using System;
using GloomhavenVR.WorldUI;
using UnityEngine.UI;
static class Program {
 static int n; static void Check(bool value,string why){n++;if(!value)throw new Exception(why);}
 static ModalFallback.WindowPanel Float(UIWindowID id,bool sticky=true){var p=new ModalFallback.WindowPanel{Window=new UIWindow{ID=id},Sticky=sticky};ModalFallback.Converted.Add(p);return p;}
 static void Main(){
  var esc=Float(UIWindowID.ESCMenu);var multiplayer=Float(UIWindowID.ViceOptionsSubmenu);var chooser=Float(UIWindowID.MutiplayerPlayerPicker);
  var story=Float(UIWindowID.None);var required=Float(UIWindowID.MutiplayerHeroAssignPanel);required.Window.Mandatory=true;
  int nativeCleanups=0;multiplayer.Window.OnHidden=()=>{nativeCleanups++;chooser.Window.Hide();};
  // Exact native ownership seam: UIMultiplayerEscSubmenu.OnHidden invokes
  // selectPlayerPopup.Hide, which hides the chooser and clears native selection.
  // The game-controller/bootstrap itself is a declared boundary, not a fake vote.
  ModalFallback.CloseFloatedWindow(multiplayer.Window);
  Check(nativeCleanups==1&&!chooser.Window.IsOpen&&chooser.Window.Hides==1,"native owner closes its chooser once");
  ModalFallback.CloseStickyFloatsExceptEscMenu();ModalFallback.CloseFloatedWindow(esc.Window);
  Check(chooser.UserClosing&&chooser.WindowCanvasGroup.alpha==0&&!chooser.WindowCanvasGroup.blocksRaycasts,"controller close releases the already native-hidden chooser float");
  Check(!required.UserClosing&&required.Window.IsOpen&&!story.UserClosing&&story.Window.IsOpen,"controller close leaves mandatory join roster and story untouched");
  Check(chooser.Window.Hides==1&&chooser.Window.Escapes==0,"native-hidden chooser cleanup is never replayed");
  Check(!MenuWindowFamily.IsGameOwnedMenu(chooser.Window),"chooser never acquires global sticky menu ownership");
  Check(MenuWindowFamily.IsOptionsKeyDomain(chooser.Window)&&!MenuWindowFamily.IsOptionsKeyDomain(required.Window),"options-key domain follows the actual ESC child only");
  // A fresh native opening is independent of its previous float's retirement.
  ModalFallback.Converted.Clear();esc=Float(UIWindowID.ESCMenu);chooser=Float(UIWindowID.MutiplayerPlayerPicker,false);
  ModalFallback.CloseFloatedWindow(chooser.Window);
  Check(chooser.UserClosing&&!chooser.Window.IsOpen&&chooser.Window.Hides==1,"corner X uses original native close for an open chooser");
  var reopened=Float(UIWindowID.MutiplayerPlayerPicker,false);
  Check(reopened.Window.IsOpen&&!reopened.UserClosing,"a new original chooser opening retains no stale close latch");
  reopened.Window.Hide();Check(!reopened.Window.IsOpen&&!reopened.Sticky,"ordinary native hide has no introduced force-visible lease");
  var mandatory=Float(UIWindowID.MutiplayerPlayerPicker);mandatory.Window.Mandatory=true;
  ModalFallback.CloseFloatedWindow(mandatory.Window);
  Check(mandatory.Window.IsOpen&&!mandatory.UserClosing&&ModalFallback.Rescues==1,"a newly mandatory chooser keeps native continuation instead of being dismissed");
  mandatory.Window.Mandatory=false;ModalFallback.CloseFloatedWindow(mandatory.Window);
  Check(!mandatory.Window.IsOpen&&mandatory.UserClosing,"native cancellation restores the ordinary explicit-close route");
  Console.WriteLine($"PASS {n} source-bound ESC chooser close assertions; native controller/bootstrap is an explicit ownership/callback port");
 }
}
