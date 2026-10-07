#nullable disable
using System;
using System.Collections.Generic;
using Assets.Script.GUI.Quest;
using MapRuleLibrary.MapState;
using UnityEngine;
using UnityEngine.UI;

// Audiovisual and transport boundaries only. Selection fields and the native
// mutation/callback bodies being reviewed live in NativeFixture.cs.
namespace UnityEngine
{
    internal class GameObject { public bool activeInHierarchy; public void SetActive(bool value) => activeInHierarchy = value; }
    internal class Component
    {
        public string name;
        public GameObject gameObject = new();
        public UIWindow Window;
        public T GetComponent<T>() where T : class => Window as T;
    }
    internal class MonoBehaviour : Component { }
    internal static class Time { public static float unscaledTime; public static int frameCount; }
    internal class Object
    {
        // Old type-wide popup lookup deliberately sees an inactive OTHER popup first.
        public static UIQuestPopup FirstPopup;
        public static T FindObjectOfType<T>() where T : class => FirstPopup as T;
    }
    internal static class Debug { public static void LogGUI(string message) { } }
}
namespace UnityEngine.UI
{
    internal class UIWindow : Component { public int ID = 37; }
    internal class Button : Component { }
}
internal class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance;
    public static bool IsInitialized => Instance != null;
}
internal static class FFSNetwork { public static bool IsOnline, IsClient; public static bool IsHost => IsOnline && !IsClient; }
namespace MapRuleLibrary.MapState
{
    internal class CLocationState { public string ID; public CLocationState(string id) { ID = id; } }
    internal class CQuestState { public string ID; public CQuest Quest = new(); public CQuestState(string id) { ID = id; } }
    internal class CQuest { public EQuestType Type = EQuestType.City; }
}
namespace Assets.Script.GUI.Quest
{
    internal interface IQuest { }
    internal class Quest : IQuest
    {
        private CQuestState questState;
        public Quest(CQuestState state) { questState = state; }
        public override bool Equals(object other) => other is Quest quest && ReferenceEquals(questState, quest.questState);
        public override int GetHashCode() => questState.GetHashCode();
    }
}
internal sealed partial class UIQuestPopupManager : Singleton<UIQuestPopupManager>
{
    private UIQuestPopup selectedQuestPopup = new(), multiplayerQuestPopup = new();
    private IQuest selectedQuest;
    private CQuestState clientSelectedQuest;
    public UIQuestPopup Selected => selectedQuestPopup;
    public UIQuestPopup Multiplayer => multiplayerQuestPopup;
    private void HidePreview(bool instant = false) { }
    private void HidePreview(IQuest quest) { }
}
internal class UIQuestPopup : MonoBehaviour
{
    public UIQuestPopup() { Window = new UIWindow(); Window.gameObject = gameObject; }
    public void ShowQuest(IQuest quest, bool autoFocus = false) => gameObject.SetActive(true);
    public void Hide(bool instant = false) => gameObject.SetActive(false);
}
internal sealed partial class MapLocation : MonoBehaviour
{
    private bool m_IsSelected;
    private delegate bool MapClick(MapLocation map, bool active);
    private MapClick m_OnClickAction;
    private Label nameText = new();
    private object nodeTitleGlowMat = new(), m_NodeTitleRegularMat = new();
    public CLocationState Location;
    public CQuestState LocationQuest;
    public bool IsSelected => m_IsSelected;
    public bool Selectable = true;
    public int DeselectCalls;
    public MapLocation(string location, string quest)
    {
        name = location; Location = new CLocationState(location); LocationQuest = new CQuestState(quest);
        m_OnClickAction = (map, active) =>
        {
            if (active)
            {
                Singleton<AdventureMapUIManager>.Instance.SelectNative(map);
                Singleton<UIQuestPopupManager>.Instance.ShowQuest(LocationQuest);
            }
            else
            {
                DeselectCalls++;
                Singleton<UIQuestPopupManager>.Instance.Hide(new Quest(LocationQuest));
            }
            return true;
        };
    }
    private bool IsSelectable() => Selectable;
    public void ForceHighlight(bool force) { }
    private void Highlight(bool isHighlighted, bool isSelected = false) { }
    private sealed class Label { public object fontMaterial; }
}
internal sealed class AdventureMapUIManager : Singleton<AdventureMapUIManager>
{
    public MapLocation LocationToTravel;
    public bool IsLocked;
    public void SelectNative(MapLocation location)
    {
        if (!ReferenceEquals(LocationToTravel, location)) LocationToTravel?.Deselect();
        LocationToTravel = location;
    }
    public void DeselectCurrentMapLocation() => LocationToTravel?.Deselect();
    public void OnCancelTravelButtonClick() => DeselectCurrentMapLocation();
}
internal enum EReadyUpToggleStates { Quests }
internal enum EGuildmasterMode { WorldMap, City }
internal enum EQuestType { City }
internal sealed partial class UIMapMultiplayerController : Singleton<UIMapMultiplayerController>
{
    private MapLocation hostSelectedLocation;
    private Button cancelQuestButton = new();
    public bool ReadyVisible;
    public Action Pending => GuildmasterConfirmAction.Callback;
    private Presenter GuildmasterConfirmAction => Singleton<UIGuildmasterHUD>.Instance.ConfirmActionPresenter;
    private void ToggleReadyUpUI(bool show, EReadyUpToggleStates state)
    {
        ReadyVisible = show;
        Singleton<UIReadyToggle>.Instance.IsVisible = show;
    }
    public void ConfirmNativeProposal() => Pending();
    public void CancelNativeProposal() => ClearHostSelectedQuest(force: true);
    private bool InQuestSelectionPhase() => true;
    private void OnReady(bool ready) => Singleton<AdventureMapUIManager>.Instance.IsLocked = ready;
}
internal enum GameActionType { SelectQuest }
internal enum ActionPhaseType { MapHQ }
internal interface IProtocolToken { }
internal sealed class LocationToken : IProtocolToken
{ public string ID; public LocationToken(string id) { ID = id; } }
internal static class Synchronizer
{
    public static LocationToken LastToken;
    // Photon transport boundary retains the exact token emitted by native host code.
    public static void SendGameAction(GameActionType action, ActionPhaseType phase,
        bool validateOnServerBeforeExecuting, bool disableAutoReplication, int a, int b, int c, int d,
        bool supplementaryDataBoolean, Guid guid, IProtocolToken token) => LastToken = (LocationToken)token;
}
internal sealed class UIReadyToggle : Singleton<UIReadyToggle>
{
    public bool IsVisible, ToggledOn;
    public void ReadyUp(bool toggledOn, bool autoValidateUnreadying = false) => ToggledOn = toggledOn;
}
internal sealed class UIGuildmasterHUD : Singleton<UIGuildmasterHUD>
{
    public EGuildmasterMode CurrentMode = EGuildmasterMode.WorldMap;
    public Presenter ConfirmActionPresenter = new();
    public void UpdateCurrentMode(EGuildmasterMode mode) => CurrentMode = mode;
}
internal class Presenter
{
    public Action Callback;
    public void ShowQuestSelectedAction(CQuestState quest, Action callback) => Callback = callback;
    public void HideQuestSelectedAction() => Singleton<UIQuestPopupManager>.Instance.HideMultiplayerPreview();
}
internal sealed class MapChoreographer : Singleton<MapChoreographer>
{
    public bool Linked;
    public bool IsChoosingLinkedQuestOption() => Linked;
    public void DeterminePlayerToggleInteractability() { }
}
internal static class UIMultiplayerNotifications { public static void ShowSelectedQuest() { } }
namespace MapRuleLibrary.Adventure
{
    internal static class AdventureState { public static MapState MapState = new(); }
    internal class MapState { public bool IsCampaign = false; }
}
internal static class AdventureState { public static MapRuleLibrary.Adventure.MapState MapState => MapRuleLibrary.Adventure.AdventureState.MapState; }
namespace FFSNet { internal static class Console { public static void LogInfo(string message) { } } }
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        public static void Warn(string scope, string message) { }
        public static void Info(string scope, string message) { }
        public static void Note(string scope, string message) { }
    }
}
namespace GloomhavenVR.Hands { internal class VRHand { public string Side = "Left"; } }
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { public static bool Active = true; }
    internal static class MapQuestReadyUp
    {
        public static readonly List<(string, string)> Edges = new();
        public static void OnQuestDecisionChanged(string previous, string current) => Edges.Add((previous, current));
    }
    internal sealed partial class MapLocationInteractor
    {
        private MapLocation _hover = null;
        public GloomhavenVR.Hands.VRHand Press;
        private GloomhavenVR.Hands.VRHand TriggerEdgeHand() => Press;
        private bool RayOnMapOrTable(GloomhavenVR.Hands.VRHand hand, out string what, out float distance)
        { what = "map"; distance = 1; return true; }
        private void NoteDeselectRefused(GloomhavenVR.Hands.VRHand hand, string what) { }
        public void Sample() => TickQuestDecision();
        public string SettledId => _publishedDecisionId;
        public void DeselectTick() => TickDeselect();
        public bool PopupOpen() => QuestPopupOpen();
        public void Arm(MapLocation location)
        { _selected = location; _selectedDecisionId = DecisionIdOf(location); _selectedAt = Time.unscaledTime; _dispatchedHere++; }
        // Downstream pointer dispatch terminates at the verbatim native Select in this
        // fixture; the production AdoptSelection admission and Deselect run unchanged.
        private void Dispatch(MapLocation location, string source)
        { location.Select(); if (location.IsSelected) Arm(location); }
        public static void ClearStatic()
        { _publishedDecisionEver = false; _publishedDecisionId = null; _publishedBySettle = false; _publishedAt = 0; }
    }
}
