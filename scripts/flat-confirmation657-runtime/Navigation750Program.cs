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
 private static void PressYes(ConfirmationBox box)
 {if(InputManager.GamePadInUse)AccessTools.DeclaredMethod(typeof(ConfirmationBox),"OnSubmit").Invoke(box,null);else box.Confirm.onClick.Invoke();}
 private static void PressEnhancementYes(UIEnhancementConfirmationBox box)=>AccessTools.DeclaredMethod(typeof(UIEnhancementConfirmationBox),"OnConfirm").Invoke(box,null);
 public static int RunNavigation(string variant)
 {
  checks=0;InputManager.GamePadInUse=variant.Contains("gamepad");Probe750.Confirmed=Probe750.Cancelled=0;Probe750.Events.Clear();Probe750.Enabled=true;Probe750.Foreign=Probe750.Reject=Probe750.ManualScreen=false;
  Singleton<UINavigation>.Instance=new();Singleton<MapChoreographer>.Instance=new();Singleton<UILoadoutManager>.Instance=new();Singleton<UIResetLevelUpWindow>.Instance=Window("reset-owner").gameObject.AddComponent<UIResetLevelUpWindow>();
  var harmony=new Harmony("navigation750."+variant);
  foreach(Type patch in new[]{typeof(ConfirmationProducerScopePatch),typeof(ConfirmationRequestOriginPatch),typeof(ItemConfirmationRequestOriginPatch)})harmony.PatchAll(patch);
  if(variant!="navigation-published749")foreach(Type patch in new[]{typeof(ConfirmationAdditionalOriginPatch),typeof(ConfirmationNativeOutcomePatch),typeof(ConfirmationNativeTweenOutcomePatch),typeof(ConfirmationIdentityWritePatch),typeof(ConfirmationPartyNavigationPatch),typeof(ConfirmationProducerDeparturePatch),typeof(ConfirmationMenuNavigationPatch),typeof(ConfirmationDeferredCoroutinePatch),typeof(ConfirmationInventoryRefreshPatch)})harmony.PatchAll(patch);
  Type? reentry=typeof(ConfirmationOnOwner).Assembly.GetType("GloomhavenVR.WorldUI.ConfirmationReentrantHidePatch");if(reentry!=null&&variant!="navigation-reentrant749")harmony.PatchAll(reentry);
  try
  {
   var partyWindow=Window("New Party display");partyWindow.Show(true);var party=partyWindow.gameObject.AddComponent<NewPartyDisplayUI>();NewPartyDisplayUI.PartyDisplay=party;
   var a=Window("character A").gameObject.AddComponent<NewPartyCharacterUI>();a.Data.CharacterName="A";
   var b=Window("character B").gameObject.AddComponent<NewPartyCharacterUI>();b.Data.CharacterName="B";
   a.ItemPointReference=a.CardPointReference=a.PerksPointReference=(RectTransform)a.transform;b.ItemPointReference=b.CardPointReference=b.PerksPointReference=(RectTransform)b.transform;party.selectedCharacter=a;
   var perks=Window("Inventory Perks").gameObject.AddComponent<UIPerksInventory>();perks.transform.SetParent(party.transform,false);perks.Bind();perks.characterData=a.Data;party.perkManager.inventory=perks;
   var dialog=Window("Confirmation Box_pc");var box=dialog.gameObject.AddComponent<ConfirmationBox>();box.Bind(Button<UIButtonExtended>("Yes",dialog.transform),Button<UIButtonExtended>("No",dialog.transform),Button<UIButtonExtended>("Report",dialog.transform));
   var manager=new UIConfirmationBoxManager{CurrentBox=box};Singleton<UIConfirmationBoxManager>.Instance=manager;
   // A real Perks request, then the native Class Toggle mutation, matching749.
   Raise(perks,manager);dialog.OnHide+=()=>{Probe750.Events.Add("cancel:"+perks.characterData.CharacterName);Probe750.Events.Add("selected-at-cancel:"+party.SelectedUISlot!.Data.CharacterName);};
   party.Choose(b);perks.Display(b.Data,(RectTransform)b.transform,true);
   Check(Probe750.Cancelled==1&&!box.IsOpen&&Probe750.Events.Contains("selected-at-cancel:A"),"native Class Toggle declines the old Perks request before character mutation");
   if(box.IsOpen)PressYes(box);
   Check(Probe750.Confirmed==0&&Probe750.Events.Contains("cancel:A"),"stale Perks Yes cannot enable a perk on the newly selected character");
   // Real positive page selection must also invalidate the same-character request.
   party.displayActive=NewPartyDisplayUI.DisplayType.PERKS;Raise(perks,manager);int before=Probe750.Cancelled;
   party.Items(b);
   Check(Probe750.Cancelled==before+1&&!box.IsOpen&&party.ActiveDisplay==NewPartyDisplayUI.DisplayType.INVENTORY,
    "native tab selection declines old Perks before the new inventory page opens");
   Check(Probe750.Events.LastIndexOf("cancel:B")<Probe750.Events.LastIndexOf("inventory:B"),"native decline completes before native page continuation");
   party.displayActive=NewPartyDisplayUI.DisplayType.PERKS;Raise(perks,manager);before=Probe750.Cancelled;
   party.ClosePerks(b);party.Cards(b);
   Check(Probe750.Cancelled==before+1&&!box.IsOpen&&party.ActiveDisplay==NewPartyDisplayUI.DisplayType.CARDS&&ReferenceEquals(party.SelectedUISlot,b),
    "same-character native Perks to Cards deselect-select declines before the new page opens");
   Check(Probe750.Events.LastIndexOf("cancel:B")<Probe750.Events.LastIndexOf("cards:B"),"same-character Perks No completes before native Cards page continuation");
   party.displayActive=NewPartyDisplayUI.DisplayType.PERKS;Raise(perks,manager);before=Probe750.Cancelled;party.ClosePerks(b);
   Check(Probe750.Cancelled==before+1&&!box.IsOpen&&party.ActiveDisplay==NewPartyDisplayUI.DisplayType.NONE,"closing only the active native Perks tab declines its pending original decision");
   party.displayActive=NewPartyDisplayUI.DisplayType.PERKS;Probe750.ManualScreen=true;Raise(perks,manager);before=Probe750.Cancelled;party.Cards(b);
   Check(Probe750.Cancelled==before+1&&!box.IsOpen&&party.ActiveDisplay==NewPartyDisplayUI.DisplayType.CARDS,
    "manual flat-screen same-character Perks to Cards retains native navigation No semantics");Probe750.ManualScreen=false;
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
   select.Invoke(roster,new object[]{oldCharacter});roster.RaiseDelete();deleteWindow.FinishTransition();deleteBox.Confirm();deleteWindow.FinishTransition();
   int deletions=Probe750.Events.FindAll(value=>value=="deleted:old").Count;select.Invoke(roster,new object[]{nextCharacter});
   Check(Probe750.Events.FindAll(value=>value=="deleted:old").Count==deletions,"naturally completed native confirmation is never replayed by later navigation");
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
   box.Hide();
   // The original deferred DLC coroutine retains its creation page, even when
   // a later page has become active before the delayed native Show arrives.
   var dlc=Window("DLC creation step").gameObject.AddComponent<GLOOM.MainMenu.UICreateGameDLCStep>();dlc.transform.SetParent(options.transform,false);
   int deferredYes=0;var pending=dlc.Warning(()=>deferredYes++);Check(pending.MoveNext(),"original native DLC confirmation yields before opening");
   var nextOption=Window("another options page").gameObject.AddComponent<UIMenuOption>();nextOption.transform.SetParent(options.transform,false);nextOption.Bind(()=>{});nextOption.Select();
   Check(!pending.MoveNext()&&!box.IsOpen&&deferredYes==0,"deferred native request from a departed page declines on arrival instead of confirming the new context");
   var closeStepWindow=Window("standalone deferred source before explicit close");closeStepWindow.Show(true);
   var closeStep=closeStepWindow.gameObject.AddComponent<GLOOM.MainMenu.UICreateGameDLCStep>();var beforeCloseWork=closeStep.Warning(()=>deferredYes++);
   Check(beforeCloseWork.MoveNext()&&!box.IsOpen,"native delayed work starts before any confirmation request exists");
   ConfirmationOnOwner.BeforeUserWindowClose(closeStepWindow);closeStepWindow.Hide(true);
   Check(!beforeCloseWork.MoveNext()&&!box.IsOpen&&deferredYes==0,"explicit source-window close invalidates a native delayed request even before its original Show");
   // The actual original item No closure reads the old selected item to restore
   // focus. It must execute before same-character inventory subpage reuse.
   var inventoryWindow=Window("native equipment inventory");var inventory=inventoryWindow.gameObject.AddComponent<UIPartyItemInventoryDisplay>();inventory.Bind();
   var service=new ItemService750();var target=new CMapCharacter{CharacterName="item-owner"};
   inventory.Display(target,service,ScenarioRuleLibrary.CItem.EItemSlot.Head,0,(RectTransform)inventory.transform);
   var itemSlot=Window("original item slot").gameObject.AddComponent<UIPartyItemSlot>();var originalSelectable=itemSlot.gameObject.AddComponent<UINavigationSelectable>();
   Singleton<UIItemConfirmationBox>.Instance=item;
   inventory.RaiseItem(itemSlot);itemWindow.FinishTransition();before=Probe750.Cancelled;
   inventory.Display(target,service,ScenarioRuleLibrary.CItem.EItemSlot.Head,0,(RectTransform)inventory.transform,resetOpen:false);
   Check(item.IsActive&&inventory.HasPendingItem&&Probe750.Cancelled==before,"same inventory slot refresh preserves its original pending item decision and focus target");
   inventory.Display(target,service,ScenarioRuleLibrary.CItem.EItemSlot.Body,1,(RectTransform)inventory.transform);
   Check(!item.IsActive&&!itemWindow.IsVisible&&Probe750.Cancelled==before+1&&!inventory.HasPendingItem
    &&ReferenceEquals(Singleton<UINavigation>.Instance!.NavigationManager.LastSelected,originalSelectable),
    "native focused item No selects the original item before same-character slot subpage fields reset");
   itemWindow.FinishTransition();inventory.Display(target,service,ScenarioRuleLibrary.CItem.EItemSlot.Hand,2,(RectTransform)inventory.transform);
   Check(Probe750.Cancelled==before+1,"natural item No completion cannot replay after its selected-item field is cleared");
   party.displayActive=NewPartyDisplayUI.DisplayType.PERKS;Raise(perks,manager);before=Probe750.Cancelled;
   ConfirmationOnOwner.BeforeUserWindowClose(Window("unrelated native window"));Check(box.IsOpen&&Probe750.Cancelled==before,"explicit closure of another window cannot decline the current source request");
   ConfirmationOnOwner.BeforeUserWindowClose(partyWindow);partyWindow.Hide(true);
   Check(!box.IsOpen&&Probe750.Cancelled==before+1,"explicit source-window close completes original No before native parent Hide");
   var temple=Window("flat native temple").gameObject.AddComponent<UITempleWindow>();
   var enhancementWindow=Window("native enhancement confirmation");var enhancement=enhancementWindow.gameObject.AddComponent<UIEnhancementConfirmationBox>();
   enhancement.Bind(enhancementWindow.gameObject.AddComponent<Image>(),Button<ExtendedButton>("Yes",enhancement.transform),Button<ExtendedButton>("No",enhancement.transform));Singleton<UIEnhancementConfirmationBox>.Instance=enhancement;
   temple.Choose(a);
   int enhancementYes=0,enhancementNo=0;string? enhancementFor=null;
   Action raiseEnhancement=()=>{var scope=ConfirmationOnOwner.Begin(temple);enhancement.ShowConfirmation("temple","native",null!,"effect",()=>{enhancementYes++;enhancementFor=((ICharacterService)AccessTools.Field(typeof(UITempleWindow),"character").GetValue(temple)).CharacterName;},onCancelled:()=>enhancementNo++);ConfirmationOnOwner.CurrentScope=scope;enhancementWindow.FinishTransition();};
   raiseEnhancement();enhancement.Cancel.onClick.Invoke();Check(enhancementNo==0,"native enhancement No waits for original Hidden callback");
   temple.Choose(b);Check(enhancementNo==1&&enhancementYes==0&&!enhancementWindow.IsVisible,"chosen enhancement No completes before same-root temple character mutation");
   temple.Choose(a);Check(enhancementNo==1,"completed enhancement No cannot replay on another character mutation");
   raiseEnhancement();PressEnhancementYes(enhancement);enhancementWindow.FinishTransition();temple.Choose(b);
   Check(enhancementYes==1&&enhancementNo==1,"naturally completed enhancement purchase is never invoked again by later navigation");
   temple.Choose(a);raiseEnhancement();PressEnhancementYes(enhancement);temple.Choose(b);
   Check(enhancementYes==2&&enhancementFor=="A"&&enhancementNo==1,"pending native enhancement Yes completes for its original character before mutation");
   int baselineYes=0,baselineNo=0;previous=ConfirmationOnOwner.Begin(options);
   manager.ShowGenericConfirmation("native baseline","native",()=>baselineYes++,()=>baselineNo++);ConfirmationOnOwner.CurrentScope=previous;dialog.FinishTransition();PressYes(box);
   Check(baselineYes==1&&baselineNo==1,"unchanged native generic Yes baseline runs onHidden No before its original Yes callback");
   int oldNo=0,oldYes=0,newNo=0,newYes=0,shows=0,hides=0;
   var area=(ControllerInputAreaLocal)AccessTools.Field(typeof(ConfirmationBox),"controllerArea").GetValue(box);
   var stateMachine=Singleton<UINavigation>.Instance!.StateMachine;var originalState=stateMachine.CurrentState;int originalHistory=stateMachine.HistoryCount;
   var requestedState=(Code.State.IState)AccessTools.Field(typeof(ConfirmationBox),"_uiState").GetValue(box);
   int enables=area.Enables,destroys=area.Destroys,switches=Singleton<UINavigation>.Instance!.StateMachine.Switches,removes=Singleton<UINavigation>.Instance.StateMachine.Removes;
   dialog.OnShow+=()=>shows++;dialog.OnHide+=()=>hides++;
   previous=ConfirmationOnOwner.Begin(perks);
   manager.ShowGenericConfirmation("old request","native",()=>oldYes++,()=>
   {
    oldNo++;var scope=ConfirmationOnOwner.Begin(options);
    manager.ShowGenericConfirmation("new request","native",()=>newYes++,()=>newNo++);ConfirmationOnOwner.CurrentScope=scope;
   });ConfirmationOnOwner.CurrentScope=previous;dialog.FinishTransition();
   perks.Display(b.Data,(RectTransform)b.transform,true);
   Check(oldNo==1&&oldYes==0&&newNo==0&&newYes==0&&box.IsRequested&&box.IsOpen&&dialog.IsVisible,
    "reentrant native replacement remains open after the exact old Hide tail finishes");
   Check(area.Active&&(!InputManager.GamePadInUse||ReferenceEquals(stateMachine.CurrentState,requestedState)&&stateMachine.HistoryCount==originalHistory+1),
    "reentrant native gamepad opening restores its exact original state history and enabled input");
   Check(shows==2&&hides==1&&area.Enables==enables+2&&area.Destroys==destroys+1&&Singleton<UINavigation>.Instance.StateMachine.Switches==switches+2+(InputManager.GamePadInUse?1:0),
    "reentrant replacement emits one original Show and controller-enable per actual opening without duplicate native navigation");
   PressYes(box);
   Check(newYes==1&&newNo==baselineNo&&oldNo==1&&oldYes==0&&hides==2&&area.Destroys==destroys+2&&Singleton<UINavigation>.Instance.StateMachine.Removes==removes+2,
    "reentrant replacement preserves original callback baseline and hides each native opening once");
   Check(!area.Active&&ReferenceEquals(stateMachine.CurrentState,originalState)&&stateMachine.HistoryCount==originalHistory,
    "new native gamepad Yes removes only its exact restored history entry and disables the retired controller");
   var mpWindow=Window("native multiplayer confirmation");var mp=mpWindow.gameObject.AddComponent<UIMultiplayerConfirmationBox>();
   mp.Bind(Button<ExtendedButton>("Kick",mp.transform),Button<ExtendedButton>("Report",mp.transform),Button<ExtendedButton>("No",mp.transform));
   var assignWindow=Window("native player selection");var assign=assignWindow.gameObject.AddComponent<UIMultiplayerSelectPlayerScreen>();
   var playerA=new NetworkPlayer{PlayerID=1};var playerB=new NetworkPlayer{PlayerID=2};assign.Bind(mp,playerA,playerB);
   assign.Show("party","class","same character",0,playerA,(RectTransform)assign.transform);assign.AskRemove(playerA);mpWindow.FinishTransition();
   assign.AskRemove(playerB);playerA.IsClient=false;assign.AskRemove(playerA);playerA.IsClient=true;
   Check(mpWindow.IsOpen&&!Probe750.Events.Contains("removed-player:1"),"rejected original multiplayer removal permissions leave the existing request unchanged");
   assign.Show("party","class","same character",0,playerA,(RectTransform)assign.transform);assign.SetSelected(playerA);
   Check(mpWindow.IsOpen,"same native multiplayer character-name-slot and player refresh retains its original request");
   assign.SetSelected(playerB);Check(!mpWindow.IsOpen&&!Probe750.Events.Contains("removed-player:1"),"new native multiplayer player selection declines instead of invoking the old kick callback");
   assign.SetSelected(playerA);int mpNo=0,mpYes=0;previous=ConfirmationOnOwner.Begin(assign);mp.ShowConfirmation(playerA,onCanceled:()=>mpNo++);ConfirmationOnOwner.CurrentScope=previous;mpWindow.FinishTransition();mp.Confirm();
   Check(mpWindow.IsOpen&&mpNo==0,"native multiplayer Confirm without a permitted original callback neither retires nor accepts the decision");
   assign.SetSelected(playerB);Check(mpNo==1&&!mpWindow.IsOpen,"subsequent actual navigation still declines the previously rejected multiplayer decision");
   assign.SetSelected(playerA);previous=ConfirmationOnOwner.Begin(assign);mp.ShowConfirmation(playerA,()=>mpYes++,onCanceled:()=>mpNo++);ConfirmationOnOwner.CurrentScope=previous;mpWindow.FinishTransition();mp.Confirm();assign.SetSelected(playerB);
   Check(mpYes==1&&mpNo==1&&!mpWindow.IsVisible,"chosen native multiplayer Yes finishes once before current-player mutation");
   assign.SetSelected(playerA);previous=ConfirmationOnOwner.Begin(assign);mp.ShowConfirmation(playerA,()=>mpYes++,onCanceled:()=>mpNo++);ConfirmationOnOwner.CurrentScope=previous;mpWindow.FinishTransition();mp.Confirm();mpWindow.FinishTransition();assign.SetSelected(playerB);
   Check(mpYes==2&&mpNo==1,"naturally completed multiplayer Yes is never replayed by a later selection");
   assign.SetSelected(playerA);assign.AskRemove(playerA);mpWindow.FinishTransition();mp.Confirm();assign.SetSelected(playerB);assign.SetSelected(playerA);
   Check(Probe750.Events.FindAll(value=>value=="removed-player:1").Count==1&&!Probe750.Events.Contains("removed-player:2"),"original native removal callback keeps its permitted target and runs exactly once");
   // Keep the original Options and its opening button in separate roots, as
   // independent conversion does. The authored callback, not focus, links them.
   var outerWindow=Window("actual ESC menu owner");var outer=outerWindow.gameObject.AddComponent<ESCMenu>();outer.Bind();outerWindow.Show(true);
   var openingButton=Window("original ESC Options entry").gameObject.AddComponent<UIMenuOption>();openingButton.transform.SetParent(outer.transform,false);openingButton.Bind(()=>{});openingButton.Select();
   var externalOptionsWindow=Window("separately converted native Options");var externalOptions=externalOptionsWindow.gameObject.AddComponent<UIOptionsWindow>();externalOptions.Bind();
   externalOptions.Show((RectTransform)openingButton.transform,openingButton.Deselect);externalOptionsWindow.FinishTransition();externalOptions.AskLogout();dialog.FinishTransition();
   var otherOuterButton=Window("actual other ESC page").gameObject.AddComponent<UIMenuOption>();otherOuterButton.transform.SetParent(outer.transform,false);otherOuterButton.Bind(()=>{});otherOuterButton.Select();
   Check(!box.IsOpen&&externalOptions.LogOutNos==1&&externalOptions.LogOuts==0,
    "native Options logout declines through the authored outer ESC opening button before another outer page opens");
   var mpEscWindow=Window("separately converted multiplayer ESC submenu");var mpEsc=mpEscWindow.gameObject.AddComponent<UIMultiplayerEscSubmenu>();mpEsc.Bind();
   mpEsc.OnHide.AddListener(outer.HideOptionListener());mpEsc.Show();mpEscWindow.FinishTransition();
   var wrongEsc=Window("unrelated currently registered ESC").gameObject.AddComponent<ESCMenu>();wrongEsc.Bind();Singleton<ESCMenu>.Instance=wrongEsc;
   var wrongOption=Window("unrelated ESC page").gameObject.AddComponent<UIMenuOption>();wrongOption.transform.SetParent(wrongEsc.transform,false);wrongOption.Bind(()=>{});
   UIConfirmationBoxManager.MainMenuInstance=manager;FFSNetwork.IsOnline=true;int entered=stateMachine.Enters;mpEsc.AskEndSession();dialog.FinishTransition();wrongOption.Select();
   Check(box.IsOpen&&mpEsc.EndSessions==0&&stateMachine.Enters==entered,
    "unrelated static ESC and its selection never replace the exact original multiplayer menu event owner");
   var actualOther=Window("new page in original ESC owner").gameObject.AddComponent<UIMenuOption>();actualOther.transform.SetParent(outer.transform,false);actualOther.Bind(()=>{});actualOther.Select();
   Check(!box.IsOpen&&mpEsc.EndSessions==0&&stateMachine.Enters==entered+1,
    "native multiplayer ESC confirmation declines through its actual UnityEvent owner without ending the session");FFSNetwork.IsOnline=false;
   // Execute the existing production rescue as well: destroyed/native-null boxes
   // must not leave a separate stale Yes closure after their page has departed.
   GloomhavenVR.WorldUI.CanvasConversion.WorldCamera=Window("fallback camera").gameObject.AddComponent<Camera>();
   harmony.PatchAll(typeof(GloomhavenVR.WorldUI.Patches.ConfirmationBox_ShowGenericConfirmation_Pair_Rescue_Patch));
   perks.Display(a.Data,(RectTransform)a.transform,true);UnityEngine.Object.DestroyImmediate((GameObject)AccessTools.Field(typeof(ConfirmationBox),"primaryContent").GetValue(box));
   int rescueYes=0,rescueNo=0;previous=ConfirmationOnOwner.Begin(perks);manager.ShowGenericConfirmation("original fallback","native",()=>rescueYes++,()=>rescueNo++);ConfirmationOnOwner.CurrentScope=previous;
   var rescue=GloomhavenVR.Net.VersionDialog.Last!;var staleYes=rescue.Yes!;Check(rescue.IsShowing,"damaged native original uses the actual production fallback");
   perks.Display(b.Data,(RectTransform)b.transform,true);staleYes();
   Check(!rescue.IsShowing&&rescueNo==1&&rescueYes==0,"navigation declines the exact fallback and invalidates its saved stale Yes closure");
   UnityEngine.Object.DestroyImmediate(box.gameObject);perks.Display(a.Data,(RectTransform)a.transform,true);previous=ConfirmationOnOwner.Begin(perks);ConfirmationOnOwner.Record(box);
   GloomhavenVR.WorldUI.Patches.ConfirmationRescue.PrefixDecide(box,"dead original","native",()=>rescueYes++,()=>rescueNo++);ConfirmationOnOwner.CurrentScope=previous;staleYes=rescue.Yes!;
   perks.Display(b.Data,(RectTransform)b.transform,true);staleYes();
   Check(!rescue.IsShowing&&rescueNo==2&&rescueYes==0,"destroyed native component retains only its exact fallback opening and original No");
   perks.Display(a.Data,(RectTransform)a.transform,true);previous=ConfirmationOnOwner.Begin(perks);
   GloomhavenVR.WorldUI.Patches.ConfirmationRescue.PrefixDecide(null!,"missing original","native",()=>rescueYes++,()=>rescueNo++);ConfirmationOnOwner.CurrentScope=previous;staleYes=rescue.Yes!;
   perks.Display(b.Data,(RectTransform)b.transform,true);staleYes();
   Check(!rescue.IsShowing&&rescueNo==3&&rescueYes==0,"native-null fallback origin declines once without requiring a live native window");
   return checks;
  }
  finally {harmony.UnpatchSelf();}
 }
}
