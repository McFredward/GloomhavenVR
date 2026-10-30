using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Only surrounding session/artwork/navigation services are ports. The installed
// game's UIWindow, generic/item Show/No/Yes, Perks producer and party selection/
// page methods are extracted verbatim and pinned separately by the runner.
public class Singleton<T> where T:class { public static T Instance=null!;public static bool IsInitialized=>Instance!=null; }
public static class InputManager { public static bool GamePadInUse;public static void SkipNextSubmitAction(){} }
public static class AudioControllerUtils { public static void PlaySound(string? _){} }
public static class FFSNetwork {public static bool IsOnline=false,IsHost=false,IsClient=false,IsStartingUp=false;}
public enum ActionPhaseType {None,MapHQ}
public static class ActionProcessor {public static ActionPhaseType CurrentPhase=ActionPhaseType.None;}
public static class PlayerRegistry {public static readonly List<object> JoiningPlayers=new();public static bool OtherClientsAreJoining=false;public static PlayersChangedEvent? OnPlayerLeft;}
public sealed class UIInfoTools {public static readonly UIInfoTools Instance=new();public string InvalidOptionAudioItem="invalid";public AudioProfile750 generalAudioButtonProfile=new();public Sprite? GetItemSlotIcon(string name)=>null;public static Color GetPlaceholderPlayerColor(NetworkPlayer player)=>Color.white;}
public sealed class AudioProfile750 {public string mouseDownAudioItem="native-click";}
public enum EscapeKeyAction { None,Toggle }
public static class UIWindowManager { public static void RegisterEscapable(UIWindow _){} public static void UnregisterEscapable(UIWindow _){} }
public static class Probe750
{
 public static bool Enabled=true,Foreign,Reject,ManualScreen;
 public static int Confirmed,Cancelled,Releases;
 public static readonly List<string> Events=new();
}
namespace UnityEngine.UI
{
 public partial class UIWindow:MonoBehaviour
 {
  public enum VisualState { Shown,Hidden } public enum Transition { Instant,Fade }
  private VisualState m_CurrentVisualState=VisualState.Hidden;private Transition m_Transition=Transition.Fade;
  private CanvasGroup m_CanvasGroup=null!;private string m_AudioItemShow="",m_AudioItemHide="";private EscapeKeyAction escapeKeyAction;
  public readonly UnityEvent onShown=new(),onHidden=new();public readonly UnityEvent<UIWindow,VisualState,bool> onTransitionBegin=new();
  public readonly UnityEvent<UIWindow,VisualState> onTransitionComplete=new();
  public Action? OnShow,OnHide;public Action<bool>? OnActivityChanged;
  public bool IsOpen=>m_CurrentVisualState==VisualState.Shown;public bool IsVisible=>m_CanvasGroup.alpha>0;
  public void Bind()=>m_CanvasGroup=GetComponent<CanvasGroup>();private bool IsActive()=>gameObject.activeInHierarchy;private void Focus(){}
  private void OnTransitionStarted(VisualState _,bool instant){}private void OnTransitionCompleted(){}
  private float m_TransitionDuration=.2f;private bool pending;
  private void SetCanvasAlpha(float alpha){m_CanvasGroup.alpha=alpha;}
  public void StartAlphaTween(float target,float duration,bool ignoreTimeScale){pending=duration>0;if(!pending){SetCanvasAlpha(target);OnTweenFinished();}}
  public void FinishTransition(){if(!pending)return;pending=false;SetCanvasAlpha(IsOpen?1:0);OnTweenFinished();}
  public void Show()=>Show(false);public void Hide()=>Hide(false);
 }
}
public class ExtendedButton:Button { public string TextLanguageKey="";public string? mouseDownAudioItem; }
public class UIButtonExtended:ExtendedButton { }
public sealed class Label {public string text="";public Color color;}
public sealed class TextLocalizedListener {public void SetTextKey(string _){} }
public sealed class Hotkey {public void Initialize(object? _){}public void Deinitialize(){} }
public sealed class ControllerInputAreaLocal {public bool IsFocused=true,Active;public int Enables,Destroys;public void Enable(){Enables++;Active=true;}public void Destroy(){Destroys++;Active=false;} }
public sealed class GUIAnimator {public void Play(){} }
public sealed class PanelHotkeyContainer {public void SetActiveHotkey(string _,bool value){} }
public sealed class VerticalPointerUI {public void PointAt(RectTransform _){} }
public partial class Navigation750State:Code.State.IStateProvider
{
 public int Switches,Removes,Enters;private readonly List<Code.State.IState> _stateHistory=new(){new NativeState750("root")};
 private Code.State.IStateFilter? _stateFilter;public event Action<Code.State.IState>? EventStateChanged;
 public int HistoryCount=>_stateHistory.Count;
 public void SwitchToState(Code.State.IState? state){Switches++;NativeSwitchToState(state!);}
 public void RemoveLast(Code.State.IState? state){Removes++;NativeRemoveLast(state!);}
 public void Enter(object? _){Enters++;}public void ToNonMenuPreviousState(){}public void SetFilter(object? filter){_stateFilter=filter as Code.State.IStateFilter;}public void ToPreviousState(){}
}
namespace Code.State
{
 public interface IStateProvider { }
 public interface IState {void Enter<TPayload>(IStateProvider provider,TPayload? payload=default)where TPayload:class;void Exit<TPayload>(IStateProvider provider,TPayload? payload=default)where TPayload:class;}
 public interface IStateFilter {bool IsValid(IState state);}
}
public sealed class NativeState750:Code.State.IState
{
 private readonly string _name;public int Enters,Exits;public NativeState750(string name){_name=name;}
 public void Enter<TPayload>(Code.State.IStateProvider provider,TPayload? payload=default)where TPayload:class{Enters++;}
 public void Exit<TPayload>(Code.State.IStateProvider provider,TPayload? payload=default)where TPayload:class{Exits++;}
 public override string ToString()=>_name;
}
public static class LogUtils {public static void Log(string text){} }
namespace Script.GUI.SMNavigation.States.CampaignMapStates {public enum CampaignMapStateTag {EnhancmentConfirmation}}
public sealed class SkipFrameKeyActionHandlerBlocker {public void Run(){} }
public enum SpecialStateTag {Lock}
public sealed class UINavigation {public readonly Navigation750State StateMachine=new();public object? Input;public Navigation750Manager NavigationManager=new();}
public sealed class Navigation750Manager {public object? LastSelected;public void TrySelect(object? value){LastSelected=value;Probe750.Cancelled++;} }
public interface INavigationOperation {void ExecuteForDefaultMachine();}
public static class CoroutineHelper {public static void RunNextFrame(Action action)=>action();}
namespace GLOOM {public static class LocalizationManager {public static string GetTranslation(string text)=>text;public static bool TryGetTranslation(string key,out string value){value=key;return true;}}}
public static class StringExtensions {public static bool IsNOTNullOrEmpty(this string? value)=>!string.IsNullOrEmpty(value);}
public sealed class CharacterPerk {public string PerkID="one";public bool IsActive;public PerkDescription Perk=new();}
public sealed class PerkDescription {public string Name="native-perk";}
public sealed class UIPerkInventorySlot {public CharacterPerk Perk=new();public int ActiveCounters,TotalCounters=1;public object? Selectable;public void ActiveBackground(bool _){} }
public interface ICharacterService {string CharacterID{get;}string CharacterName{get;}int PerkPoints{get;}List<CharacterPerk> Perks{get;}bool CanLevelup();}
public class NativeCharacter750:ICharacterService
{
 public string CharacterID {get;set;}="class";public string CharacterName {get;set;}="A";
 public int PerkPoints{get;set;}=2;public List<CharacterPerk> Perks{get;}=new(){new()};
 public bool CanLevelup()=>false;
}
public sealed class CMapCharacter:NativeCharacter750 { }
public sealed class NewPartyCharacterUI:MonoBehaviour
{
 public NativeCharacter750 Data {get;set;}=new();public NativeCharacter750 Service=>Data;
 public RectTransform ItemPointReference= null!,CardPointReference=null!,PerksPointReference=null!;public void AddEquippedItemModifiers(object _){}public void RefreshEquippedItemModifiers(){}public void CloseItemsWindow(){}public void CloseCardsWindow(){}public void ClosePerksWindow(){}
}
public sealed class NativeTab750 {public void RemoveCurrent(bool deselectFirst){} }
public sealed class Equipment750
{
 public void Display(object? service,NativeCharacter750 data,Action<object> modifiers,Action refreshed,RectTransform point,Action<object> items,bool enabled,Action close)
 {Probe750.Events.Add("inventory:"+data.CharacterName);}
}
public sealed class PerksWindow750 {public UIPerksInventory inventory=null!;public void Show(ICharacterService data,RectTransform point,bool intro,Action<object> hover,bool enabled,Action close){} }
public sealed class CardsDisplay750
{
 public void Hide(){}public void Display(NativeCharacter750 data,Action<bool> selected,RectTransform point,bool edit,bool animate,Action levelup,Action close,bool enableLevelUp,bool isLevelUpValid)
 {Probe750.Events.Add("cards:"+data.CharacterName);}
}
public static class AdventureState {public static readonly MapState750 MapState=new();}
public sealed class MapState750 {public bool IsCampaign=true;}
public sealed class MapChoreographer {public bool IsChoosingLinkedQuestOption()=>false;}
public sealed class UILoadoutManager {public bool InLoadout()=>false;}
public sealed class MapParty750 {public bool HasIntroduced(string concept)=>true;public void MarkIntroDone(string _){} }
public enum EIntroductionConcept {PerksPanel}
public sealed class SaveData {public static readonly SaveData Instance=new();public GlobalSave750 Global=new();public void SaveCurrentAdventureData(){} }
public sealed class GlobalSave750 {public bool EpicLogin=false;}
public partial class NewPartyDisplayUI:MonoBehaviour
{
 public enum DisplayType {NONE,INVENTORY,CARDS,PERKS,CHARACTER_SELECTOR,LEVELUP,BATTLE_GOALS}
 public static NewPartyDisplayUI? PartyDisplay;public NewPartyCharacterUI? selectedCharacter;public DisplayType displayActive=DisplayType.PERKS;
 public NewPartyCharacterUI? SelectedUISlot=>selectedCharacter;public DisplayType ActiveDisplay=>displayActive;
 private NativeTab750 _tab=new();public Equipment750 ItemInventoryDisplay=new();public PerksWindow750 perkManager=new();
 public PerksWindow750 PerkManager=>perkManager;public CardsDisplay750 AbilityCardsDisplay=new();
 [Flags]private enum FlagsInteraction {None,DISABLE_RESET_LEVELUP}
 private FlagsInteraction flagsInteraction=FlagsInteraction.None;private readonly HashSet<DisplayType> _ignoreDisplayTypes=new();private MapParty750 mapParty=new();
 private object? itemService;private readonly List<object> disableStatsRequests=new();public Action<bool,DisplayType>? OnOpenedPanel;
 private void CloseQuickAccessAssignRole(){}private void OnNewItemsDisplayed(object _){}
 private void TryHideCurrentDisplay(DisplayType ignore=DisplayType.NONE){if(displayActive!=ignore){Probe750.Events.Add("hide:"+displayActive);displayActive=DisplayType.NONE;}}
 private void TryHideCurrentDisplay(HashSet<DisplayType> ignore){if(!ignore.Contains(displayActive))TryHideCurrentDisplay();}
 private void OnAbilityCardSelected(bool _){}private void OnLevelUpSelected(NewPartyCharacterUI _){}private void OnHoveredPerk(object _){}
 [MethodImpl(MethodImplOptions.NoInlining)]public void Choose(NewPartyCharacterUI next)=>SelectCurrentCharacter(next);
 [MethodImpl(MethodImplOptions.NoInlining)]public void Items(NewPartyCharacterUI next)=>OnItemsSelected(next,true);
 [MethodImpl(MethodImplOptions.NoInlining)]public void Cards(NewPartyCharacterUI next)=>OnCardsSelected(next,true,false,false);
 [MethodImpl(MethodImplOptions.NoInlining)]public void ClosePerks(NewPartyCharacterUI next)=>OnPerksSelected(next,false);
}
public partial class UIPerksInventory:MonoBehaviour
{
 public ICharacterService characterData=new NativeCharacter750();private ScrollRect scroll=null!;private VerticalPointerUI verticalPointer=new();
 private GUIAnimator? showAnimation;private PanelHotkeyContainer? panelHotkeyContainer;private ControllerInputAreaLocal controllerArea=new();
 private INavigationOperation? _toPreviousNavigationOperation;
 private void ReturnSlotsToObjectPool(){}private void UpdateDisplay(bool _){}private void CancelAnimations(){}
 private bool ValidatePreconditions(CharacterPerk _)=>!Probe750.Reject;
 private void EnabledPerk(CharacterPerk perk,UIPerkInventorySlot slot){Probe750.Confirmed++;Probe750.Events.Add("enabled:"+characterData.CharacterName);}
 public void Bind(){scroll=gameObject.AddComponent<ScrollRect>();scroll.content=(RectTransform)transform;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void Raise()=>OnSelectedPerk(new UIPerkInventorySlot());
}
public partial class UIConfirmationBoxManager
{
 public static UIConfirmationBoxManager? MainMenuInstance;public ConfirmationBox CurrentBox=null!;
 private Color WarningTextColor=>Color.white;
}
public partial class ConfirmationBox:MonoBehaviour
{
 private UIWindow confirmationBox=null!;private UIButtonExtended confirmButton=null!,cancelButton=null!,reportButton=null!;
 private TextLocalizedListener cancelButtonText=new(),confirmButtonText=new(),headerText=new();
 private GameObject primaryContent=null!,secondaryGeneralContent=null!,secondaryContent=null!,reportContent=null!,header=null!;
 private Label primaryContentText=new(),secondaryGeneralContentText=new(),secondaryContentFirstText=new(),secondaryContentSecondText=new();
 private ControllerInputAreaLocal controllerArea=new();private Code.State.IState _uiState=new NativeState750("confirmation");private bool usingAdventureStartConfirmation,_resetAfterAction,isPCVersion=true;
 private INavigationOperation? _onHideNavigation;private Hotkey congirmHotkey=new(),cancelHotkey=new(),_reportHotkey=new();
 private readonly List<Action> _onConfirmActions=new(),_onCancelActions=new(),_onReportActions=new();private event Action? onHidden;
 private Color HeaderTextColor=>Color.white;private Color MessageTextColor=>Color.white;
 public bool IsRequested{get;private set;}public bool IsOpen=>confirmationBox.IsOpen;
 public UIButtonExtended Confirm=>confirmButton;public UIButtonExtended Cancel=>cancelButton;
 private void InitGamepadInput(){}private void DisableGamepadInput(){}
 public void Bind(UIButtonExtended yes,UIButtonExtended no,UIButtonExtended report)
 {confirmationBox=GetComponent<UIWindow>();confirmButton=yes;cancelButton=no;reportButton=report;primaryContent=new GameObject("Primary");secondaryGeneralContent=new GameObject("SecondaryGeneral");secondaryContent=new GameObject("Secondary");reportContent=new GameObject("Report");header=new GameObject("Header");}
}
public enum BoxConfirmationType {General,Buy,Sell}
namespace ScenarioRuleLibrary {public sealed class CItem{public enum EItemSlot{Head,Body,Hand}public string ID="fixture-item";public bool Tradeable=true;public int SellPrice=1;} }
public sealed class ItemCardUI:MonoBehaviour {public string CardID="fixture-item";public ScenarioRuleLibrary.CItem item=null!;public void ReloadBackground(){} }
public static class ObjectPool
{
 public enum ECardType {Item}
 public static GameObject SpawnCard(string id,ECardType _,Transform parent,bool resetLocalScale,bool resetToMiddle){var go=new GameObject(id,typeof(RectTransform),typeof(ItemCardUI));go.transform.SetParent(parent,false);return go;}
 public static void RecycleCard(string id,ECardType _,GameObject go)=>UnityEngine.Object.DestroyImmediate(go);
}
public partial class UIItemConfirmationBox:MonoBehaviour
{
 private RectTransform pictureHolder=null!;private ExtendedButton confirmButton=null!,cancelButton=null!;private Label titleText=new(),informationText=new();
 private ControllerInputAreaLocal controllerArea=new();private UIWindow confirmationBox=null!;private ItemCardUI? itemCardUI;
 private Action? _onConfirmedCallback,_onCancelCallback;public bool IsActive{get;private set;}public event Action<BoxConfirmationType>? ConfirmationBoxRequested;
 public ExtendedButton Confirm=>confirmButton;public ExtendedButton Cancel=>cancelButton;
 public void Bind(RectTransform holder,ExtendedButton yes,ExtendedButton no){pictureHolder=holder;confirmButton=yes;cancelButton=no;confirmationBox=GetComponent<UIWindow>();}
}
internal sealed class UILevelUpWindow:MonoBehaviour {private void ConfirmSelectCard(){} }
internal sealed class UILevelUpCardInventory:MonoBehaviour { }
internal sealed class UIResetLevelUpWindow:MonoBehaviour {private void ResetAbilities(){}public void Hide(){}public void Show(ICharacterService _){} }
public sealed class UINavigationSelectable:MonoBehaviour { }
public sealed class UIPartyItemSlot:MonoBehaviour {public ScenarioRuleLibrary.CItem Item=new();}
public delegate void ControllerChangedEvent(object controllable,object oldController,object newController);
public static class ControllableRegistry {public static ControllerChangedEvent? OnControllerChanged;}
public interface IPartyItemEquipmentService
{
 void RegisterOnAddedItemParty(Action<ScenarioRuleLibrary.CItem> handler);void RegisterOnRemovedItemParty(Action<ScenarioRuleLibrary.CItem,int> handler);
 void RegisterOnRemovedNewItemFlag(Action<ScenarioRuleLibrary.CItem> handler);void RegisterOnBindedItemToCharacter(Action<ScenarioRuleLibrary.CItem,CMapCharacter> handler);
}
public sealed class ItemService750:IPartyItemEquipmentService
{
 public void RegisterOnAddedItemParty(Action<ScenarioRuleLibrary.CItem> h){}public void RegisterOnRemovedItemParty(Action<ScenarioRuleLibrary.CItem,int> h){}
 public void RegisterOnRemovedNewItemFlag(Action<ScenarioRuleLibrary.CItem> h){}public void RegisterOnBindedItemToCharacter(Action<ScenarioRuleLibrary.CItem,CMapCharacter> h){}
}
public partial class UIPartyItemInventoryDisplay:MonoBehaviour
{
 private CMapCharacter? character;private IPartyItemEquipmentService? service;private int slotNum;private ScenarioRuleLibrary.CItem.EItemSlot slotType;
 private Action<ScenarioRuleLibrary.CItem>? onItemEquipped;private Action<ScenarioRuleLibrary.CItem,int>? onItemRemoved;private Action<ScenarioRuleLibrary.CItem,bool>? onItemHovered;
 private UIPartyItemSlot? selectedItemToCofirmSwitch;private Label infoText=new();private Image iconTypeInventory=null!;private VerticalPointerUI verticalPointer=new();
 private UIWindow window=null!;private GUIAnimator showAnimation=new();private string audioItemDisplay="native";private ControllerInputAreaLocal controllerArea=new();
 public void Bind(){window=GetComponent<UIWindow>();iconTypeInventory=gameObject.AddComponent<Image>();}
 private void ClearAll(){onItemEquipped=null;onItemRemoved=null;onItemHovered=null;}private void InitializeHotkeys(){}
 private void CreateItems(ScenarioRuleLibrary.CItem.EItemSlot type,int number,ScenarioRuleLibrary.CItem item,bool edit){}
 private void OnNewItemAdded(ScenarioRuleLibrary.CItem i){}private void OnItemRemoved(ScenarioRuleLibrary.CItem i,int n){}
 private void RefreshNewItemNotification(ScenarioRuleLibrary.CItem i){}private void OnItemBinded(ScenarioRuleLibrary.CItem i,CMapCharacter c){}
 private void OnControllableOwnershipChanged(object c,object old,object next){}
 private void BindAndEquipItem(UIPartyItemSlot slot){Probe750.Confirmed++;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void RaiseItem(UIPartyItemSlot slot)=>OnItemSelected(slot);
 public bool HasPendingItem=>selectedItemToCofirmSwitch!=null;
 public bool IsVisible=>window.IsVisible;
}
namespace GLOOM.MainMenu
{
 public partial class UICreateGameDLCStep:MonoBehaviour
 {
  private INavigationOperation? _navigationOperation;
  public System.Collections.IEnumerator Warning(UnityAction confirmed)=>SkipAFrameBeforeMessage(confirmed);
 }
}
internal sealed class UIAdventurePartyAssemblyRoster:MonoBehaviour {private void OnSelectedCharacter(){} }
public partial class UIOptionsWindow:MonoBehaviour
{
 private ToggleGroup m_ToggleGroup=null!;private UIWindow m_Window=null!;private ControllerInputAreaLocal m_ControllerArea=new();private VerticalPointerUI m_VerticalPointer=new();private Action? m_OnHidden;
 private UIMenuOption m_EpicStoreOption=null!;private EpicLogin750 epicLoginService=new();public int LogOuts,LogOutNos;
 public void Hide()=>GetComponent<UIWindow>().Hide();private void CloseWindows(){}
 private void LogOut(){LogOuts++;}
 public void Bind(){m_ToggleGroup=gameObject.AddComponent<ToggleGroup>();m_Window=GetComponent<UIWindow>();m_Window.onHidden.AddListener(OnHidden);var go=new GameObject("native logout option",typeof(RectTransform),typeof(UIMenuOption));go.transform.SetParent(transform,false);m_EpicStoreOption=go.GetComponent<UIMenuOption>();m_EpicStoreOption.Init(AskLogOut,()=>LogOutNos++);}
 public void AskLogout()=>m_EpicStoreOption.Select();
}
public sealed class EpicLogin750 {public bool IsSigningOut()=>false;}
public partial class UIMenuOption:MonoBehaviour
{
 protected bool isSelected;private GUIAnimator selectAnimation=new();private Frame750? _frame;private Action? onSelected,onDeselected;
 public bool IsSelected=>isSelected;private void SetSelected(bool isSelected)=>this.isSelected=isSelected;public void Bind(Action selected)=>onSelected=selected;
 public void Init(Action selected,Action? deselected=null){onSelected=selected;onDeselected=deselected;}
 private void CancelSelectedAnimation(){}
}
public sealed class Frame750 {public void Activate(){}public void Deactivate(){} }
public partial class UITempleWindow:MonoBehaviour
{
 private ICharacterService? character;public TempleShop750 Shop=new();private void OnSelectedSlot(){}
 [MethodImpl(MethodImplOptions.NoInlining)]public void Choose(NewPartyCharacterUI next)=>OnSelectedCharacter(next);
}
public sealed class TempleShop750 {public void Refresh(ICharacterService _){} }
public sealed class NetworkPlayer {public int PlayerID;public bool IsClient=true;public string Username="native-player",PlatformName="fixture";public Sprite? Avatar;}
public delegate void PlayersChangedEvent(NetworkPlayer player);
public sealed class KeyActionHandler {public bool HasBlockers=true;}
public sealed class PlatformLayer {public static readonly PlatformLayer Instance=new();public PlatformImages750 PlayerPlatformImageController=new();public bool IsConsole=false;}
public sealed class PlatformImages750 {public Sprite? GetPlayerPlatformImage(string name)=>null;}
public partial class UIMultiplayerConfirmationBox:MonoBehaviour
{
 private UIWindow confirmationBox=null!;private ControllerInputAreaLocal _controllerArea=new();
 private Image icon=null!,_playerPlatformImage=null!;private Label informationText=new();
 private ExtendedButton confirmButton=null!,alternativeConfirmButton=null!,cancelButton=null!,_pcConfirmButton=null!,_pcAlternativeConfirmButton=null!,_pcCancelButton=null!;
 private GameObject _pcButtons=null!,_gamepadButtons=null!;private NetworkPlayer? _networkPlayer;
 private Action? _onConfirmClickedCallback,_onAlternativeConfirmClickedCallback,_onCancelClickedCallback;private KeyActionHandler? _kickKeyActionHandler=new();
 private void InitGamepadInput(){}private void OnPlayerLeft(NetworkPlayer player){}
 public void Bind(ExtendedButton yes,ExtendedButton alternative,ExtendedButton no)
 {confirmationBox=GetComponent<UIWindow>();confirmButton=_pcConfirmButton=yes;alternativeConfirmButton=_pcAlternativeConfirmButton=alternative;cancelButton=_pcCancelButton=no;icon=gameObject.AddComponent<Image>();var platform=new GameObject("native player platform",typeof(RectTransform),typeof(Image));platform.transform.SetParent(transform,false);_playerPlatformImage=platform.GetComponent<Image>();_pcButtons=new GameObject("native PC buttons");_gamepadButtons=new GameObject("native gamepad buttons");}
 public void Confirm()=>OnConfirm();
}
public interface INetworkHeroAssignService {PlayersChangedEvent? OnPlayerLeft{get;set;}void RemoveClient(NetworkPlayer player,bool sendKickMessage);}
public sealed class NativeAssignService750:INetworkHeroAssignService {public PlayersChangedEvent? OnPlayerLeft{get;set;}public void RemoveClient(NetworkPlayer player,bool sendKickMessage){Probe750.Events.Add("removed-player:"+player.PlayerID);}}
public sealed class UIMultiplayerPlayerOption {public void SetSelected(bool selected){} }
public partial class UIMultiplayerSelectPlayerScreen:MonoBehaviour
{
 private NetworkPlayer? currentSelection;private readonly Dictionary<NetworkPlayer,UIMultiplayerPlayerOption> assignedSlots=new();
 private UIWindow window=null!;private UIMultiplayerConfirmationBox confirmationBox=null!;private INetworkHeroAssignService service=new NativeAssignService750();
 private string? characterID,characterName;private int m_CurrentSlotIndex;private Action? onHiddenCallback;private Action<NetworkPlayer>? onSelectedPlayer;
 private VerticalPointerUI pointer=new();private float defaultPositionX=0;private ControllerInputAreaLocal controllerArea=new();public string Id{get;private set;}="";
 private void RefreshPlayers(){}private void RemoveEvents(){}private void RemovePlayer(NetworkPlayer player){}private void OnControllableOwnersChanged(object c,object old,object next){}
 public void Bind(UIMultiplayerConfirmationBox box,NetworkPlayer a,NetworkPlayer b){window=GetComponent<UIWindow>();confirmationBox=box;assignedSlots[a]=new();assignedSlots[b]=new();}
 [MethodImpl(MethodImplOptions.NoInlining)]public void AskRemove(NetworkPlayer player)=>OnSelectedRemovePlayer(player);
}
public sealed class UICharacterInformation {public void Display(CMapCharacter _){} }
public partial class UIAdventureCharacterConfirmationBox:MonoBehaviour
{
 private UIWindow window=null!;private Action? confirmAction;private UICharacterInformation information=new();
 public void Bind()=>window=GetComponent<UIWindow>();public void Confirm()=>OnConfirmClick();
}
public partial class UIAdventurePartyAssemblyWindow:MonoBehaviour {protected CMapCharacter? selectedCharacter;public CMapCharacter? Current=>selectedCharacter;}
public partial class UICampaignAdventurePartyAssemblyWindow:UIAdventurePartyAssemblyWindow
{
 public UIAdventureCharacterConfirmationBox characterConfirmationBox=null!;
 private bool CanDeleteCharacter=>true;
 private void DeleteCharacter(CMapCharacter value)=>Probe750.Events.Add("deleted:"+value.CharacterName);
 [MethodImpl(MethodImplOptions.NoInlining)]public void RaiseDelete()=>ConfirmDeleteCharacter();
}
public partial class ESCMenu:MonoBehaviour
{
 private ToggleGroup toggleGroup=null!;private void LoadMainMenu(){}private void ExitGame(){}public void Hide(){}
 public void Bind(){toggleGroup=gameObject.AddComponent<ToggleGroup>();}
 public UnityAction HideOptionListener()=>OnHideOptionWindow;
}
internal sealed class UIMapEscMenu:MonoBehaviour {private void TurnOffTutorial(){} }
public partial class UIMultiplayerEscSubmenu:MonoBehaviour
{
 private UIWindow window=null!;private GameObject epicPanel=null!;private ControllerInputAreaLocal controllerArea=new();public UIWindow Window=>window;public UnityEvent OnHide=>Window.onHidden;
 public int EndSessions;public void EndSession(){EndSessions++;}public void Bind(){window=GetComponent<UIWindow>();epicPanel=new GameObject("native epic panel");}
 public void AskEndSession()=>ShowConfirmationBoxToEndSession();
}
public enum MainStateTag {MultiplayerOnlineContainer}
internal sealed class UIScenarioEscMenu:MonoBehaviour {private void QuitDungeon(){}private void RestartScenario(){}private void QueueRestartRound(){}private void RestartTurn(){}private void SkipMission(){}private void SkipTutorial(){} }
internal sealed class UIShopItemInventory:MonoBehaviour {private void OnSelectedItemSell(){}private void OnSelectedItemBuy(){} }
internal sealed class LevelEditorMainMenu:MonoBehaviour {private void OnPlaySelectedFilePressed(){} }
internal sealed class UICreateModView:MonoBehaviour {private void Confirm(){} }
internal sealed class UIExportModdingManager:MonoBehaviour {private void OnExportFailed(){} }
internal sealed class UIRulesetsWindow:MonoBehaviour {private void ChooseRulesetTypeToCreate(){}private void OnCloseButton(){}private void OnConfirmedRulesetChanges(){}private void OnValidationFailed(){}private void AskDeleteRuleset(){} }
internal sealed class UIListModsView:MonoBehaviour { }
internal sealed class UIRulesetView:MonoBehaviour { }
internal sealed class PersistentAbilitiesUI:MonoBehaviour {private void OnCancelActiveAbility(int _){}private void OnCancelActiveAbility(string _){}private List<Tuple<SpriteMemoryManagement.ReferenceToSprite,string,string,Action,bool>> GetCustomEffects()=>new();}
namespace SpriteMemoryManagement {internal sealed class ReferenceToSprite{} }
namespace GloomhavenVR.Core {internal static class VRLog {internal static void Alert(string _,string text)=>Probe750.Events.Add(text);internal static void Note(string _,string text){}internal static void Error(string _,string text)=>Probe750.Events.Add(text);} }
namespace GloomhavenVR.WorldUI
{
 internal static partial class ConfirmationOnOwner
 {
  private static bool Enabled=>Probe750.Enabled&&!Probe750.ManualScreen;private static bool ForeignOwner(Component box)=>Probe750.Foreign;
  private static void Release(Component box){Probe750.Releases++;}
 }
 internal static class CanvasConversion {internal static Camera? WorldCamera;}
 internal static class WorldUIConfig {internal static bool ConversionActive=>Probe750.Enabled;}
}
namespace GloomhavenVR.Net
{
 internal sealed class VersionDialog
 {
  internal static VersionDialog? Last;internal Action? Yes,No;internal bool IsShowing;
  internal void Show(string title,string body,string yes,string no,Action confirm,Action cancel){Last=this;Yes=confirm;No=cancel;IsShowing=true;}
  internal void Tick(){}internal void Close(){IsShowing=false;Yes=No=null;}
 }
}
