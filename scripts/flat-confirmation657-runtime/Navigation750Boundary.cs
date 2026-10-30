using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Only surrounding session/artwork/navigation services are ports. The installed
// game's UIWindow, generic/item Show/No/Yes, Perks producer and party selection/
// page methods are extracted verbatim and pinned separately by the runner.
public class Singleton<T> where T:class { public static T? Instance;public static bool IsInitialized=>Instance!=null; }
public static class InputManager { public static bool GamePadInUse;public static void SkipNextSubmitAction(){} }
public static class AudioControllerUtils { public static void PlaySound(string? _){} }
public static class FFSNetwork {public static bool IsOnline=false,IsHost=false,IsClient=false;}
public enum ActionPhaseType {None,MapHQ}
public static class ActionProcessor {public static ActionPhaseType CurrentPhase=ActionPhaseType.None;}
public static class PlayerRegistry {public static readonly List<object> JoiningPlayers=new();public static bool OtherClientsAreJoining=false;}
public sealed class UIInfoTools {public static readonly UIInfoTools Instance=new();public string InvalidOptionAudioItem="invalid";public AudioProfile750 generalAudioButtonProfile=new();}
public sealed class AudioProfile750 {public string mouseDownAudioItem="native-click";}
public enum EscapeKeyAction { None,Toggle }
public static class UIWindowManager { public static void RegisterEscapable(UIWindow _){} public static void UnregisterEscapable(UIWindow _){} }
public static class Probe750
{
 public static bool Enabled=true,Foreign,Reject;
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
public sealed class ControllerInputAreaLocal {public bool IsFocused=true;public void Enable(){}public void Destroy(){} }
public sealed class GUIAnimator {public void Play(){} }
public sealed class PanelHotkeyContainer {public void SetActiveHotkey(string _,bool value){} }
public sealed class VerticalPointerUI {public void PointAt(RectTransform _){} }
public sealed class Navigation750State {public void SwitchToState(object? _){}public void RemoveLast(object? _){} }
public sealed class UINavigation {public readonly Navigation750State StateMachine=new();public object? Input;public Navigation750Manager NavigationManager=new();}
public sealed class Navigation750Manager {public void TrySelect(object? _){Probe750.Cancelled++;} }
public interface INavigationOperation {void ExecuteForDefaultMachine();}
public static class CoroutineHelper {public static void RunNextFrame(Action action)=>action();}
namespace GLOOM {public static class LocalizationManager {public static string GetTranslation(string text)=>text;public static bool TryGetTranslation(string key,out string value){value=key;return true;}}}
public static class StringExtensions {public static bool IsNOTNullOrEmpty(this string? value)=>!string.IsNullOrEmpty(value);}
public sealed class CharacterPerk {public string PerkID="one";public bool IsActive;public PerkDescription Perk=new();}
public sealed class PerkDescription {public string Name="native-perk";}
public sealed class UIPerkInventorySlot {public CharacterPerk Perk=new();public int ActiveCounters,TotalCounters=1;public object? Selectable;public void ActiveBackground(bool _){} }
public interface ICharacterService {string CharacterID{get;}string CharacterName{get;}int PerkPoints{get;}List<CharacterPerk> Perks{get;}}
public class NativeCharacter750:ICharacterService
{
 public string CharacterID {get;set;}="class";public string CharacterName {get;set;}="A";
 public int PerkPoints{get;set;}=2;public List<CharacterPerk> Perks{get;}=new(){new()};
}
public sealed class CMapCharacter:NativeCharacter750 { }
public sealed class NewPartyCharacterUI:MonoBehaviour
{
 public NativeCharacter750 Data {get;set;}=new();public NativeCharacter750 Service=>Data;
 public RectTransform ItemPointReference= null!;public void AddEquippedItemModifiers(object _){}public void RefreshEquippedItemModifiers(){}public void CloseItemsWindow(){}
}
public sealed class NativeTab750 {public void RemoveCurrent(bool deselectFirst){} }
public sealed class Equipment750
{
 public void Display(object? service,NativeCharacter750 data,Action<object> modifiers,Action refreshed,RectTransform point,Action<object> items,bool enabled,Action close)
 {Probe750.Events.Add("inventory:"+data.CharacterName);}
}
public sealed class PerksWindow750 {public UIPerksInventory inventory=null!;}
public partial class NewPartyDisplayUI:MonoBehaviour
{
 public enum DisplayType {NONE,INVENTORY,CARDS,PERKS,CHARACTER_SELECTOR,LEVELUP,BATTLE_GOALS}
 public static NewPartyDisplayUI? PartyDisplay;public NewPartyCharacterUI? selectedCharacter;public DisplayType displayActive=DisplayType.PERKS;
 public NewPartyCharacterUI? SelectedUISlot=>selectedCharacter;public DisplayType ActiveDisplay=>displayActive;
 private NativeTab750 _tab=new();public Equipment750 ItemInventoryDisplay=new();public PerksWindow750 perkManager=new();
 private object? itemService;private readonly List<object> disableStatsRequests=new();public Action<bool,DisplayType>? OnOpenedPanel;
 private void CloseQuickAccessAssignRole(){}private void OnNewItemsDisplayed(object _){}
 private void TryHideCurrentDisplay(DisplayType ignore=DisplayType.NONE){if(displayActive!=ignore)Probe750.Events.Add("hide:"+displayActive);}
 [MethodImpl(MethodImplOptions.NoInlining)]public void Choose(NewPartyCharacterUI next)=>SelectCurrentCharacter(next);
 [MethodImpl(MethodImplOptions.NoInlining)]public void Items(NewPartyCharacterUI next)=>OnItemsSelected(next,true);
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
 private ControllerInputAreaLocal controllerArea=new();private object? _uiState;private bool usingAdventureStartConfirmation,_resetAfterAction,isPCVersion=true;
 private INavigationOperation? _onHideNavigation;private Hotkey congirmHotkey=new(),cancelHotkey=new(),_reportHotkey=new();
 private readonly List<Action> _onConfirmActions=new(),_onCancelActions=new(),_onReportActions=new();private event Action? onHidden;
 private Color HeaderTextColor=>Color.white;private Color MessageTextColor=>Color.white;
 public bool IsRequested{get;private set;}public bool IsOpen=>confirmationBox.IsOpen;
 public UIButtonExtended Confirm=>confirmButton;public UIButtonExtended Cancel=>cancelButton;
 private void InitGamepadInput(){}private void DisableGamepadInput(){}private void OnShown(){}
 public void Bind(UIButtonExtended yes,UIButtonExtended no,UIButtonExtended report)
 {confirmationBox=GetComponent<UIWindow>();confirmButton=yes;cancelButton=no;reportButton=report;primaryContent=new GameObject("Primary");secondaryGeneralContent=new GameObject("SecondaryGeneral");secondaryContent=new GameObject("Secondary");reportContent=new GameObject("Report");header=new GameObject("Header");}
}
public enum BoxConfirmationType {General,Buy,Sell}
namespace ScenarioRuleLibrary {public sealed class CItem{public string ID="fixture-item";} }
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
internal sealed class UIResetLevelUpWindow:MonoBehaviour {private void ResetAbilities(){}public void Hide(){} }
internal sealed class UIPartyItemInventoryDisplay:MonoBehaviour {private void OnItemSelected(){} }
internal sealed class UIAdventurePartyAssemblyRoster:MonoBehaviour {private void OnSelectedCharacter(){} }
public partial class UIOptionsWindow:MonoBehaviour
{
 private ToggleGroup m_ToggleGroup=null!;private void AskLogOut(){}public void Hide()=>GetComponent<UIWindow>().Hide();private void CloseWindows(){}
 public void Bind(){m_ToggleGroup=gameObject.AddComponent<ToggleGroup>();}
}
public partial class UIMenuOption:MonoBehaviour
{
 protected bool isSelected;private GUIAnimator selectAnimation=new();private Frame750? _frame;private Action? onSelected;
 public bool IsSelected=>isSelected;private void SetSelected(bool isSelected)=>this.isSelected=isSelected;public void Bind(Action selected)=>onSelected=selected;
}
public sealed class Frame750 {public void Activate(){}public void Deactivate(){} }
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
 private bool CanDeleteCharacter()=>true;
 private void DeleteCharacter(CMapCharacter value)=>Probe750.Events.Add("deleted:"+value.CharacterName);
 [MethodImpl(MethodImplOptions.NoInlining)]public void RaiseDelete()=>ConfirmDeleteCharacter();
}
internal sealed class ESCMenu:MonoBehaviour {private void LoadMainMenu(){}private void ExitGame(){}public void Hide(){} }
internal sealed class UIMapEscMenu:MonoBehaviour {private void TurnOffTutorial(){} }
internal sealed class UIMultiplayerEscSubmenu:MonoBehaviour {private void ShowConfirmationBoxToEndSession(){} }
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
namespace GloomhavenVR.Core {internal static class VRLog {internal static void Alert(string _,string text)=>Probe750.Events.Add(text);} }
namespace GloomhavenVR.WorldUI
{
 internal static partial class ConfirmationOnOwner
 {
  private static bool Enabled=>Probe750.Enabled;private static bool ForeignOwner(Component box)=>Probe750.Foreign;
  private static void Release(Component box){Probe750.Releases++;}
 }
}
namespace GloomhavenVR.WorldUI.Patches
{
 internal static class ConfirmationRescue
 {
  internal static bool IsCurrentNavigationOpening(object opening)=>false;
  internal static bool DeclineForNavigation(Component? box,object opening)=>false;
 }
}
