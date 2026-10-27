extern alias native;
global using ExtendedButton = native::ExtendedButton;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// Session, native owner fields and conversion services are explicit surrounding inputs.
// The new production compositor, original Unity transforms/scenes, actual game ExtendedButton,
// uGUI/TMP pixels, event dispatch and source/root lifetime execute in the real editor.
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
    private void ResetAbilities() { }
}
internal sealed class NewPartyDisplayUI:MonoBehaviour
{
    internal static NewPartyDisplayUI? PartyDisplay;
    internal UIWindow window=null!;internal NativeCardsDisplay? AbilityCardsDisplay;
}
internal sealed class UICampaignRewardWindow:MonoBehaviour { internal ExtendedButton continueButton=null!; }
internal sealed class CampaignRewardsManager { internal UICampaignRewardWindow rewardsWindow=null!; }
internal sealed class UIConfirmationBoxManager
{
    internal static UIConfirmationBoxManager? MainMenuInstance;internal ConfirmationBox? CurrentBox;
    private void ShowGenericConfirmation() { } private void ShowGenericWarningConfirmation() { }
    private void ShowGenericCancelConfirmation() { } private void ShowGenericSpendConfirmation() { }
    private void ShowCancelActiveAbility() { }
}
internal sealed class ConfirmationBox:MonoBehaviour { internal bool IsOpen=true; }
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
    internal enum UIWindowID { None,PartyPanel,HeroLevelUpPanel }
    internal sealed class UIWindow:MonoBehaviour { internal bool IsOpen=true,IsVisible=true;internal UIWindowID ID; }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog {internal static void Info(string area,string message){}internal static void Warn(string area,string message){}internal static void Alert(string area,string message){} }
}
namespace GloomhavenVR.Core.Events {internal static class VRModeStateMachine {internal static bool TableInFrontOfPlayer=true;} }
namespace GloomhavenVR.Hands.Interact {internal sealed class PokeSurfaceTuning {internal static readonly PokeSurfaceTuning SmallDialog=new();} }
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig {internal static bool ConversionActive=true;}
    internal static class FlatScreen {internal static bool ManualScreenActive;}
    internal static class ChromeParkTuning {internal const float ChromeGapPx=12f,OffsetEpsilonPx=.25f;}
    internal sealed class ConvertedPanel
    {
        internal RectTransform Target=null!,HostRect=null!;internal GameObject HostGo=null!;internal Canvas HostCanvas=null!;
        internal GraphicRaycaster HostRaycaster=null!;internal bool Alive=true;internal bool IsAlive=>Alive&&Target!=null;
        internal Transform? Home;internal int Index;internal Vector2 Min,Max,Pivot,Size,Position;internal Vector3 Scale;internal Quaternion Rotation;
    }
    internal static class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> ActivePanels=new();internal static Camera? WorldCamera;
        internal static int Converts=0,PoseAuthors=0;
        internal static ConvertedPanel? Convert(RectTransform? target,string name,bool pokeable=true,
            Hands.Interact.PokeSurfaceTuning? pokeTuning=null,bool? fitContent=null,bool flattenWindow=false)
        {
            if(target==null)return null;
            var panel=new ConvertedPanel {Target=target,Home=target.parent,Index=target.GetSiblingIndex(),Min=target.anchorMin,Max=target.anchorMax,
                Pivot=target.pivot,Size=target.sizeDelta,Position=target.anchoredPosition,Scale=target.localScale,Rotation=target.localRotation};
            panel.HostGo=new GameObject("capture-only:"+name,typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            panel.HostRect=(RectTransform)panel.HostGo.transform;panel.HostRect.sizeDelta=target.rect.size;
            panel.HostCanvas=panel.HostGo.GetComponent<Canvas>();panel.HostRaycaster=panel.HostGo.GetComponent<GraphicRaycaster>();
            // Existing converter contract: retain original root scene before reparent.
            // This service port is not a reimplementation of the new owner selection/pose logic.
            if(target.gameObject.scene.name=="DontDestroyOnLoad")UnityEngine.Object.DontDestroyOnLoad(panel.HostGo);
            else SceneManager.MoveGameObjectToScene(panel.HostGo,target.gameObject.scene);
            target.SetParent(panel.HostRect,false);target.anchorMin=target.anchorMax=target.pivot=new Vector2(.5f,.5f);target.anchoredPosition=Vector2.zero;
            panel.HostCanvas.renderMode=RenderMode.WorldSpace;panel.HostCanvas.worldCamera=WorldCamera;
            panel.HostCanvas.sortingOrder=10;panel.HostRect.localScale=Vector3.one*.001f;panel.HostGo.layer=target.gameObject.layer;
            ActivePanels.Add(panel);Converts++;return panel;
        }
        internal static void Release(ConvertedPanel? panel)
        {
            if(panel==null||!panel.IsAlive)return;
            panel.Target.SetParent(panel.Home,false);panel.Target.anchorMin=panel.Min;panel.Target.anchorMax=panel.Max;panel.Target.pivot=panel.Pivot;
            panel.Target.sizeDelta=panel.Size;panel.Target.anchoredPosition=panel.Position;panel.Target.localScale=panel.Scale;panel.Target.localRotation=panel.Rotation;
            panel.Alive=false;ActivePanels.Remove(panel);UnityEngine.Object.Destroy(panel.HostGo);
        }
        internal static void TransferSeatedSubtree(RectTransform rect,ConvertedPanel? destination) { }
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
        internal static bool ReleaseForComposite(UIWindow window)
        {
            for(int i=CanvasConversion.ActivePanels.Count-1;i>=0;i--)if(ReferenceEquals(CanvasConversion.ActivePanels[i].Target,window.transform))CanvasConversion.Release(CanvasConversion.ActivePanels[i]);
            return true;
        }
    }
}
