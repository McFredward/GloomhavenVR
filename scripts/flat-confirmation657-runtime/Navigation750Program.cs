using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;

public static class ConfirmationProgram
{
 private static int checks;
 private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 private static UIWindow Window(string name)
 {var go=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup),typeof(UIWindow));var w=go.GetComponent<UIWindow>();w.Bind();return w;}
 private static T Button<T>(string name,Transform parent)where T:Button
 {var go=new GameObject(name,typeof(RectTransform),typeof(T));go.transform.SetParent(parent,false);return go.GetComponent<T>();}
 private static void Raise(UIPerksInventory perks,UIConfirmationBoxManager manager)
 {perks.Raise();manager.CurrentBox.GetComponent<UIWindow>().FinishTransition();}
 public static int RunNavigation(string variant)
 {
  checks=0;Probe750.Confirmed=Probe750.Cancelled=0;Probe750.Events.Clear();Probe750.Enabled=true;Probe750.Foreign=Probe750.Reject=false;
  Singleton<UINavigation>.Instance=new();Singleton<UIResetLevelUpWindow>.Instance=Window("reset-owner").gameObject.AddComponent<UIResetLevelUpWindow>();
  var harmony=new Harmony("navigation750."+variant);
  foreach(Type patch in new[]{typeof(ConfirmationProducerScopePatch),typeof(ConfirmationRequestOriginPatch),typeof(ItemConfirmationRequestOriginPatch)})harmony.PatchAll(patch);
  if(variant!="navigation-published749")foreach(Type patch in new[]{typeof(ConfirmationAdditionalOriginPatch),typeof(ConfirmationNativeOutcomePatch),typeof(ConfirmationIdentityWritePatch),typeof(ConfirmationPartyNavigationPatch),typeof(ConfirmationProducerDeparturePatch),typeof(ConfirmationMenuNavigationPatch)})harmony.PatchAll(patch);
  try
  {
   var partyWindow=Window("New Party display");partyWindow.Show(true);var party=partyWindow.gameObject.AddComponent<NewPartyDisplayUI>();NewPartyDisplayUI.PartyDisplay=party;
   var a=Window("character A").gameObject.AddComponent<NewPartyCharacterUI>();a.Data.CharacterName="A";
   var b=Window("character B").gameObject.AddComponent<NewPartyCharacterUI>();b.Data.CharacterName="B";
   a.ItemPointReference=(RectTransform)a.transform;b.ItemPointReference=(RectTransform)b.transform;party.selectedCharacter=a;
   var perks=Window("Inventory Perks").gameObject.AddComponent<UIPerksInventory>();perks.transform.SetParent(party.transform,false);perks.Bind();perks.characterData=a.Data;party.perkManager.inventory=perks;
   var dialog=Window("Confirmation Box_pc");var box=dialog.gameObject.AddComponent<ConfirmationBox>();box.Bind(Button<UIButtonExtended>("Yes",dialog.transform),Button<UIButtonExtended>("No",dialog.transform),Button<UIButtonExtended>("Report",dialog.transform));
   var manager=new UIConfirmationBoxManager{CurrentBox=box};Singleton<UIConfirmationBoxManager>.Instance=manager;
   // A real Perks request, then the native Class Toggle mutation, matching749.
   Raise(perks,manager);dialog.OnHide+=()=>Probe750.Events.Add("cancel:"+perks.characterData.CharacterName);
   party.Choose(b);perks.Display(b.Data,(RectTransform)b.transform,true);
   Check(Probe750.Cancelled==1&&!box.IsOpen,"native Class Toggle declines the old Perks request before character mutation");
   if(box.IsOpen)box.Confirm.onClick.Invoke();
   Check(Probe750.Confirmed==0&&Probe750.Events.Contains("cancel:A"),"stale Perks Yes cannot enable a perk on the newly selected character");
   // Real positive page selection must also invalidate the same-character request.
   party.displayActive=NewPartyDisplayUI.DisplayType.PERKS;Raise(perks,manager);int before=Probe750.Cancelled;
   party.Items(b);
   Check(Probe750.Cancelled==before+1&&!box.IsOpen&&party.ActiveDisplay==NewPartyDisplayUI.DisplayType.INVENTORY,
    "native tab selection declines old Perks before the new inventory page opens");
   Check(Probe750.Events.LastIndexOf("cancel:B")<Probe750.Events.LastIndexOf("inventory:B"),"native decline completes before native page continuation");
   // No-change refresh, another window, modal dimming and presentation teardown are controls.
   party.displayActive=NewPartyDisplayUI.DisplayType.PERKS;Raise(perks,manager);before=Probe750.Cancelled;
   party.Choose(b);perks.Display(b.Data,(RectTransform)b.transform,true);
   Check(box.IsOpen&&Probe750.Cancelled==before,"same-character native refresh retains the original decision");
   Probe750.Reject=true;perks.Raise();Probe750.Reject=false;
   Check(box.IsOpen&&Probe750.Cancelled==before,"rejected native Perks action cannot cancel the accepted original request");
   partyWindow.Hide(true);Check(box.IsOpen&&Probe750.Cancelled==before,"native parent dimming is not navigation");partyWindow.Show(true);
   Probe750.Foreign=true;party.Choose(a);Check(box.IsOpen&&Probe750.Cancelled==before,"foreign town-service ownership retains its native decision authority");Probe750.Foreign=false;
   Probe750.Enabled=false;party.Choose(b);Check(box.IsOpen&&Probe750.Cancelled==before,"VR teardown cannot invoke native decline");Probe750.Enabled=true;
   box.Hide();
   // Item No owns a deferred Hidden callback; navigation must finish it before
   // the original inventory context is reused, without invoking native Yes.
   var itemWindow=Window("UI Item Confirmation Box");var item=itemWindow.gameObject.AddComponent<UIItemConfirmationBox>();
   item.Bind((RectTransform)item.transform,Button<ExtendedButton>("Yes",item.transform),Button<ExtendedButton>("No",item.transform));
   int itemNo=0,itemYes=0;string? cancelledFor=null;var previous=ConfirmationOnOwner.Begin(perks);
   item.ShowConfirmation(BoxConfirmationType.General,"bind","item",new ScenarioRuleLibrary.CItem(),()=>itemYes++,onActionCancelled:()=>{itemNo++;cancelledFor=perks.characterData.CharacterName;});ConfirmationOnOwner.CurrentScope=previous;itemWindow.FinishTransition();
   perks.Display(a.Data,(RectTransform)a.transform,true);
   Check(itemNo==1&&itemYes==0&&cancelledFor=="B"&&!item.IsActive&&!itemWindow.IsVisible,"native item No completes once before the inventory changes character");
   for(int tick=0;tick<5;tick++)itemWindow.FinishTransition();
   Check(itemNo==1&&itemYes==0,"repeated completion cannot decline or confirm the retired item request twice");
   // Another native decision family captures mutable selectedCharacter too.
   var roster=Window("campaign character selection").gameObject.AddComponent<UICampaignAdventurePartyAssemblyWindow>();
   var deleteWindow=Window("native character delete confirmation");var deleteBox=deleteWindow.gameObject.AddComponent<UIAdventureCharacterConfirmationBox>();deleteBox.Bind();roster.characterConfirmationBox=deleteBox;
   var oldCharacter=new CMapCharacter{CharacterName="old"};var nextCharacter=new CMapCharacter{CharacterName="next"};
   var select=AccessTools.PropertySetter(typeof(UIAdventurePartyAssemblyWindow),"SelectedCharacter");
   select.Invoke(roster,new object[]{oldCharacter});roster.RaiseDelete();deleteWindow.FinishTransition();
   select.Invoke(roster,new object[]{nextCharacter});
   Check(!deleteWindow.IsOpen&&!Probe750.Events.Contains("deleted:next"),"native character deletion declines before a different roster character is selected");
   select.Invoke(roster,new object[]{oldCharacter});roster.RaiseDelete();deleteWindow.FinishTransition();deleteBox.Confirm();
   Check(!deleteWindow.IsOpen&&deleteWindow.IsVisible&&!Probe750.Events.Contains("deleted:old"),"chosen native character deletion waits for its original fade");
   select.Invoke(roster,new object[]{nextCharacter});
   Check(Probe750.Events.Contains("deleted:old")&&!Probe750.Events.Contains("deleted:next")&&!deleteWindow.IsVisible,
    "already confirmed native fade completes for the old character before selection changes");
   // Actual UIMenuOption.Select runs only after native toggle/check permission.
   var optionsWindow=Window("native options owner");var options=optionsWindow.gameObject.AddComponent<UIOptionsWindow>();options.Bind();optionsWindow.Show(true);
   var option=Window("other native options page").gameObject.AddComponent<UIMenuOption>();option.transform.SetParent(options.transform,false);
   int menuNo=0,menuYes=0;previous=ConfirmationOnOwner.Begin(options);
   manager.ShowGenericConfirmation("logout","native options",()=>menuYes++,()=>menuNo++);ConfirmationOnOwner.CurrentScope=previous;dialog.FinishTransition();
   options.Hide();Check(box.IsOpen&&menuNo==0,"typed native options Hide alone is not intentional navigation");
   option.Bind(()=>Probe750.Events.Add("options-new-page"));option.Select();
   Check(menuNo==1&&menuYes==0&&!box.IsOpen&&option.IsSelected,"accepted native menu selection declines the old original decision once");
   previous=ConfirmationOnOwner.Begin(options);manager.ShowGenericConfirmation("logout2","native options",()=>menuYes++,()=>menuNo++);ConfirmationOnOwner.CurrentScope=previous;dialog.FinishTransition();
   option.Select();Check(menuNo==1&&box.IsOpen,"native same-page menu reselect preserves the pending decision");
   return checks;
  }
  finally {harmony.UnpatchSelf();}
 }
}
