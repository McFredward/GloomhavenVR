extern alias native;
global using ExtendedButton = native::ExtendedButton;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// Session, native owner fields and conversion services are explicit surrounding inputs.
// The new production compositor, original Unity transforms/scenes, actual game ExtendedButton,
// uGUI/TMP pixels, event dispatch and source/root lifetime execute in the real editor.
internal enum BoxConfirmationType { General,Buy,Sell }
internal enum EGuildmasterMode { None,Merchant,Temple,Enchantress }
internal static class Singleton<T> where T:class { internal static T? Instance; internal static bool IsInitialized=>Instance!=null; }
internal static class FFSNetwork { internal static bool IsOnline; }
internal sealed class CharacterPermission { internal bool IsUnderMyControl=true; }
internal sealed class NativeTracker:MonoBehaviour
{
    internal Action? OnClick;internal int Clicks; internal bool SkipNextClick=false;
    internal void ProcessClick() { if(SkipNextClick){SkipNextClick=false;return;} Clicks++;OnClick?.Invoke(); }
}
internal sealed class UILevelUpWindow:MonoBehaviour
{
    internal UIWindow myWindow=null!;internal UILevelUpCardInventory inventory=null!;internal UILevelUpCardHighlighter cardHolder=null!;
    internal bool IsShowing=true,isPlayingOpenAnimation=false,isOpenConfirmationBox=false,enableTracker=true;
    internal NativeTracker nextCardTracker=null!;internal CharacterPermission character=new();
    private void ConfirmSelectCard() { }
}
internal sealed class UILevelUpCardInventory:MonoBehaviour { }
internal sealed class UILevelUpCardHighlighter:MonoBehaviour { internal UILevelUpCardHolder cardHolder=null!; }
internal sealed class UILevelUpCardHolder:MonoBehaviour { internal NativeAbilityCard? Card; }
internal sealed class NativeAbilityCard:MonoBehaviour { internal NativeFullCard fullAbilityCard=null!; }
internal sealed class NativeFullCard:MonoBehaviour { }
internal sealed class NativeCardsDisplay:MonoBehaviour { }
internal sealed class UIResetLevelUpWindow:MonoBehaviour
{
    internal UILevelUpCardInventory inventory=null!;internal Toggle displayToggle=null!;
    internal readonly HashSet<object> requestDisableShow=new();
    private void ResetAbilities() { }
}
internal sealed class NewPartyDisplayUI:MonoBehaviour
{
    internal enum DisplayType { NONE, CARDS }
    internal static NewPartyDisplayUI? PartyDisplay;
    internal UIWindow window=null!;internal NativeCardsDisplay? AbilityCardsDisplay;
    internal DisplayType ActiveDisplay=DisplayType.NONE;
    internal bool IsDisableFurtherAbilityPanel=false;
}
internal sealed class UICampaignRewardWindow:MonoBehaviour { internal ExtendedButton continueButton=null!; }
internal sealed class CampaignRewardsManager { internal UICampaignRewardWindow rewardsWindow=null!; }
internal sealed class UIConfirmationBoxManager
{
    internal static UIConfirmationBoxManager? MainMenuInstance;internal ConfirmationBox CurrentBox=null!;
    internal Action? BoxWindowShown=null,BoxWindowHidden=null;
    [MethodImpl(MethodImplOptions.NoInlining)] internal void ShowGenericConfirmation() { CurrentBox.IsOpen=true;CurrentBox.GetComponent<UIWindow>().IsVisible=true;BoxWindowShown?.Invoke(); } private void ShowGenericWarningConfirmation() { }
    private void ShowGenericCancelConfirmation() { } private void ShowGenericSpendConfirmation() { }
    private void ShowCancelActiveAbility() { }
}
internal sealed class ConfirmationBox:MonoBehaviour { internal bool IsOpen=true; }
internal sealed class UIItemConfirmationBox : MonoBehaviour
{
    private bool _active=false;internal bool IsActive {get=>_active;set{_active=value;var w=GetComponent<UIWindow>();if(w!=null)w.IsOpen=value;}}
    internal event Action<BoxConfirmationType>? ConfirmationBoxRequested;
    [MethodImpl(MethodImplOptions.NoInlining)] internal void ShowConfirmation(BoxConfirmationType kind=BoxConfirmationType.General) { ConfirmationBoxRequested?.Invoke(kind);IsActive=true;GetComponent<UIWindow>().IsVisible=true; }
}
internal sealed class UIEnhancementConfirmationBox : MonoBehaviour { }
internal sealed class UIShopItemInventory : MonoBehaviour { internal UIItemConfirmationBox? Item=null; private void OnSelectedItemSell() { Item?.ShowConfirmation(BoxConfirmationType.Sell); } private void OnSelectedItemBuy() { Item?.ShowConfirmation(BoxConfirmationType.Buy); } }
internal sealed class LevelEditorMainMenu : MonoBehaviour { private void OnPlaySelectedFilePressed() { } }
internal sealed class UICreateModView : MonoBehaviour { private void Confirm() { } }
internal sealed class UIExportModdingManager : MonoBehaviour { private void OnExportFailed() { } }
internal sealed class UIRulesetsWindow : MonoBehaviour
{
    internal Action? Completion; internal int Completed; internal UIConfirmationBoxManager? Manager=null;
    private void ChooseRulesetTypeToCreate() { } private void OnCloseButton() { }
    private void OnConfirmedRulesetChanges() { } private void OnValidationFailed() { } private void AskDeleteRuleset() { }
    internal void CompileRuleset() { int request=7; Completion=()=>{Completed+=request;Manager?.ShowGenericConfirmation();}; }
}
internal sealed class UIListModsView : MonoBehaviour
{
    internal Action? Completion; internal int Completed; internal UIConfirmationBoxManager? Manager=null;
    internal void ValidateMod() { int request=11; Completion=()=>{Completed+=request;Manager?.ShowGenericConfirmation();}; }
    internal void OpenLevelEditor() { int request=13; Completion=()=>{Completed+=request;Manager?.ShowGenericConfirmation();}; }
}
internal sealed class UIRulesetView : MonoBehaviour
{
    internal Action? Completion; internal int Completed; internal UIConfirmationBoxManager? Manager=null;
    internal void ValidateMod() { int request=17; Completion=()=>{Completed+=request;Manager?.ShowGenericConfirmation();}; }
}
internal sealed class PersistentAbilitiesUI : MonoBehaviour
{
    private void OnCancelActiveAbility(int value) { } private void OnCancelActiveAbility(string value) { }
    internal List<Tuple<SpriteMemoryManagement.ReferenceToSprite,string,string,Action,bool>> Effects=new();
    [MethodImpl(MethodImplOptions.NoInlining)] internal List<Tuple<SpriteMemoryManagement.ReferenceToSprite,string,string,Action,bool>> GetCustomEffects() => Effects;
}
namespace SpriteMemoryManagement { internal sealed class ReferenceToSprite { } }
internal sealed class UIPerksInventory:MonoBehaviour { private void OnSelectedPerk() { } }
internal sealed class UIPartyItemInventoryDisplay:MonoBehaviour { private void OnItemSelected() { } }
internal sealed class UIAdventurePartyAssemblyRoster:MonoBehaviour { private void OnSelectedCharacter() { } }
internal sealed class UIOptionsWindow:MonoBehaviour { private void AskLogOut() { } }
internal sealed class ESCMenu:MonoBehaviour { private void LoadMainMenu() { }private void ExitGame() { } }
internal sealed class UIMapEscMenu:MonoBehaviour { private void TurnOffTutorial() { } }
internal sealed class UIMultiplayerEscSubmenu:MonoBehaviour { private void ShowConfirmationBoxToEndSession() { } }
internal sealed class UIScenarioEscMenu:MonoBehaviour
{private void QuitDungeon() { }private void RestartScenario() { }private void QueueRestartRound() { }private void RestartTurn() { }private void SkipMission() { }private void SkipTutorial() { }}
namespace UnityEngine.UI
{
    internal enum UIWindowID { None,PartyPanel,HeroLevelUpPanel,Shop,EnhancementShop }
    internal sealed class UIWindow:MonoBehaviour
    {
        internal bool IsOpen=true,IsVisible=true;internal UIWindowID ID;
        internal Action? OnShow,OnHide;
        internal void Show(){IsOpen=IsVisible=true;OnShow?.Invoke();}
        internal void Hide(bool instant=false){OnHide?.Invoke();IsOpen=IsVisible=false;}
        internal void StartAlphaTween(float alpha,float seconds,bool ignoreTimeScale){IsVisible=alpha>0;}
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRSession {internal static bool IsRunning=true;}
    internal static partial class Loc
    {
        internal static string CurrentLanguage="English";
        internal static event Action? OnChanged;
        internal static void SetLanguage(string language){CurrentLanguage=language;OnChanged?.Invoke();}
    }
    internal static class PerfMonitor {internal readonly struct Noop:IDisposable{public void Dispose(){}}internal static Noop Scope(string name)=>default;internal static void Count(string name,int count){}}
    internal static class UnseenTileOrder {internal static void Tick(Vector3 eye){}}
    internal static class VRLog {internal static void Info(string area,string message){}internal static void Warn(string area,string message){}internal static void Alert(string area,string message){}internal static void Note(string area,string message){} }
}
namespace GloomhavenVR.Core.Events {
    internal static class MenuWorldContext { internal static bool IsReady=false; }
internal static class VRModeStateMachine {internal static bool TableInFrontOfPlayer=true;} }
namespace GloomhavenVR.Hands.Interact {internal sealed class PokeSurfaceTuning {internal static readonly PokeSurfaceTuning SmallDialog=new();} }
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class GuildmasterDestinations
    {
        internal static EGuildmasterMode Mode=EGuildmasterMode.Merchant;
        internal static readonly Dictionary<EGuildmasterMode,UIWindow> Windows=new();
        internal static EGuildmasterMode? CurrentDestinationMode()=>Mode;
        internal static UIWindow? ModeWindow(EGuildmasterMode mode)=>Windows.TryGetValue(mode,out var window)?window:null;
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class FixtureSetting<T> {internal T Value;internal FixtureSetting(T value){Value=value;}}
    internal static class WorldUIConfig
    {
        internal static bool ConversionActive=true;
        internal static FixtureSetting<bool> Dialogs=new(true),NeutraliseGrabPassBlur=new(true);
        internal static FixtureSetting<float> PanelMipLodOffset=new(0f);
    }
    internal static class WindowMaterialise {internal static void DropPreRoll(ConvertedPanel panel,string why){} internal static void Cancel(ConvertedPanel panel,string why){}}
    internal static class TownServicePresentation
    {
        internal static UIWindow? Owned=null;
        internal static bool OwnsWindow(UIWindow window)=>ReferenceEquals(window,Owned);
        internal static bool IsQuietController(UIWindow window,byte service)=>false;
    }
    internal static class Defaults {internal const float PanelMipLodOffset=0f;}
    internal static class PanelLayout {internal static float WorldScale=1f;}
    internal static class HeadFacing
    {
        internal readonly struct Facing {internal readonly Quaternion Rotation;internal Facing(Quaternion rotation){Rotation=rotation;}}
        internal static Facing YawOnly(Transform head)=>new(Quaternion.Euler(0f,head.eulerAngles.y,0f));
        internal static void LogPlaced(string name,Facing facing,Vector3 position,string reason) { }
    }
    internal static class VRLayers {internal static void Apply(GameObject go)=>go.layer=26;}
    internal static class FlatScreen
    {
        internal static bool ManualScreenActive;
        internal static ConvertedPanel? MenuOwner=null;
        internal static Transform? MenuSource=null;
        internal static bool TryGetMenuOwner(Transform source,out ConvertedPanel? panel)
        { panel=MenuOwner;return Core.Events.MenuWorldContext.IsReady&&MenuSource!=null&&source.IsChildOf(MenuSource)&&panel!=null; }
        internal static bool IsMenuOwner(ConvertedPanel panel)=>Core.Events.MenuWorldContext.IsReady&&ReferenceEquals(panel,MenuOwner);
    }
    internal static class ChromeParkTuning {internal const float ChromeGapPx=12f,OffsetEpsilonPx=.25f;}
    internal sealed class ConvertedPanel
    {
        internal RectTransform Target=null!,HostRect=null!;internal GameObject HostGo=null!;internal Canvas HostCanvas=null!;
        internal GraphicRaycaster HostRaycaster=null!;internal bool IsAlive=>Target!=null;
        internal bool ModLayerEnabled=false,RevealPending=false,RenderHidden=false,OwnerRenderHidden=false,HideBackground=false;
        internal int BackgroundSweepNextFrame=0,FitOneShotStableCount=0,FitSettleStillCount=0,FitOneShotStableGraphics=0;
        internal readonly List<Graphic> HiddenBackgrounds=new();
        internal int DrawSortingOrder=0,BaseSortingOrder=0,OrderSwapStreak=0,LastShownFrame=0;internal float OrderDistance=0;internal bool OrderListed=false;internal ConvertedPanel? OrderSwapPeer=null;
        internal Vector3 LastShownPosition=default,LastShownLossyScale=Vector3.one;internal Quaternion LastShownRotation=Quaternion.identity;
        internal readonly List<OrderFollower> OrderFollowers=new();internal Transform HostTransform=>HostRect;
        internal Transform? Home;internal int Index;internal Vector2 Min,Max,Pivot,Size,Position;internal Vector3 Scale;internal Quaternion Rotation;
    }
    internal struct OrderFollower {internal Canvas? Canvas;internal Renderer? Renderer;internal int Offset;internal OrderFollower(Renderer? renderer){Canvas=null;Renderer=renderer;Offset=0;}}
    internal static class MrBacking {internal static void SyncPlateOrders(){}}
    internal static partial class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> ActivePanels=new();internal static Camera? WorldCamera;
        internal static int Converts=0,PoseAuthors=0,Releases=0;
        internal static ConvertedPanel? Convert(RectTransform? target,string name,bool pokeable=true,
            Hands.Interact.PokeSurfaceTuning? pokeTuning=null,bool? fitContent=null,bool flattenWindow=false,bool useModLayer=false,bool transparentBackground=false,int sortingOrder=0)
        {
            if(target==null)return null;
            var panel=new ConvertedPanel {Target=target,Home=target.parent,Index=target.GetSiblingIndex(),Min=target.anchorMin,Max=target.anchorMax,
                Pivot=target.pivot,Size=target.sizeDelta,Position=target.anchoredPosition,Scale=target.localScale,Rotation=target.localRotation,ModLayerEnabled=useModLayer,BaseSortingOrder=sortingOrder,DrawSortingOrder=sortingOrder};
            panel.HostGo=new GameObject("capture-only:"+name,typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            panel.HostRect=(RectTransform)panel.HostGo.transform;panel.HostRect.sizeDelta=target.rect.size;
            panel.HostCanvas=panel.HostGo.GetComponent<Canvas>();panel.HostRaycaster=panel.HostGo.GetComponent<GraphicRaycaster>();
            // Existing converter contract: retain original root scene before reparent.
            // This service port is not a reimplementation of the new owner selection/pose logic.
            if(target.gameObject.scene.name=="DontDestroyOnLoad")UnityEngine.Object.DontDestroyOnLoad(panel.HostGo);
            else SceneManager.MoveGameObjectToScene(panel.HostGo,target.gameObject.scene);
            NormalizeTarget739(target,panel.HostRect,panel.HostRect.rect.size);
            panel.HostCanvas.renderMode=RenderMode.WorldSpace;panel.HostCanvas.worldCamera=WorldCamera;
            panel.HostCanvas.sortingOrder=10;panel.HostRect.localScale=Vector3.one*.001f;panel.HostGo.layer=target.gameObject.layer;
            if(transparentBackground){panel.HideBackground=true;HideFullScreenBackground(panel,true);}
            ActivePanels.Add(panel);Converts++;return panel;
        }
        internal static void Release(ConvertedPanel? panel)
        {
            if(panel==null)return;Releases++;
            RestoreBackgrounds739(panel);
            panel.Target.SetParent(panel.Home,false);panel.Target.anchorMin=panel.Min;panel.Target.anchorMax=panel.Max;panel.Target.pivot=panel.Pivot;
            panel.Target.sizeDelta=panel.Size;panel.Target.anchoredPosition=panel.Position;panel.Target.localScale=panel.Scale;panel.Target.localRotation=panel.Rotation;
            panel.OrderListed=false;ActivePanels.Remove(panel);if(!panel.Target.IsChildOf(panel.HostRect))UnityEngine.Object.Destroy(panel.HostGo);
        }
        internal static void TransferSeatedSubtree(RectTransform rect,ConvertedPanel? destination) { }
        internal static void PlaceHost(ConvertedPanel panel,Vector3 position,Quaternion rotation,float scale)
        {panel.HostRect.SetPositionAndRotation(position,rotation);panel.HostRect.localScale=Vector3.one*scale;PoseAuthors++;}

    }
    internal static partial class ModalFallback
    {
        // Surrounding modal/session inputs; actual production admission methods are
        // linked separately. The post-admission conversion body is an explicit port.
        private static bool ConvertBaseActive=true;
        private static readonly List<UIWindow> StrandedFloat=new();
        private static bool ContainsWindow(List<UIWindow> values,UIWindow window)=>values.Contains(window);
        internal static int AdmissionAttempts=0;
        internal static bool WantsFloat(UIWindow window)=>FloatWantedFor(window,true);
        internal static bool DirectAdmission(UIWindow window)=>TryConvertWindow(window);
        internal static ConvertedPanel? PanelFor(UIWindow window)=>CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,window.transform));
        internal static bool FloatIsLive(UIWindow window)=>window.IsOpen&&PanelFor(window)!=null;
        private sealed class GrabPort {internal void Destroy() { }}
        private sealed class WindowPanel {internal UIWindow Window=null!;internal ConvertedPanel Panel=null!;internal bool UserClosing=false;internal GrabPort? Grab=null;}
        private static readonly List<WindowPanel> Converted=new();
        internal static int CompositeTransfers=0;
        private static void ReleasePreConvertHide(UIWindow window,string why) { CompositeTransfers++; }
        private static void ReleaseScreenBind(UIWindow window,string why) { }

    }
}
