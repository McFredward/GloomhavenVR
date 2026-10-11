// Explicit process/Unity/network boundaries. Native visibility, initialization and ReadyUp
// method bodies are extracted unchanged from the read-only game source by the runner.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using FFSNet;
using MapRuleLibrary.MapState;

namespace UnityEngine
{
    internal class GameObject
    {
        public string name = "fixture"; public Transform transform = new RectTransform(); public bool activeSelf = true, Destroyed;
        public void SetActive(bool value) => activeSelf = value;
        public static bool operator ==(GameObject? a,GameObject? b)
        {
            bool aNull=ReferenceEquals(a,null) || a.Destroyed, bNull=ReferenceEquals(b,null) || b.Destroyed;
            return aNull || bNull ? aNull==bNull : ReferenceEquals(a,b);
        }
        public static bool operator !=(GameObject? a,GameObject? b) => !(a==b);
        public override bool Equals(object? obj) => ReferenceEquals(this,obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    internal class Transform { }
    internal readonly struct Position { public override string ToString() => "(0,0)"; }
    internal sealed class RectTransform : Transform { public Position anchoredPosition; }
    internal static class Time { public static float unscaledTime; }
    internal readonly struct Color { }
    internal sealed class Sprite { }
    internal static class Debug { public static void LogGUI(string text) { } }
}
namespace UnityEngine.Events { internal delegate void UnityAction(); internal delegate void UnityAction<T>(T arg); internal delegate void UnityAction<T,U>(T arg,T arg2); }
namespace UnityEngine.UI
{
    internal sealed class UIWindow
    {
        public readonly GameObject gameObject = new(); public Transform transform => gameObject.transform;
        public string name => gameObject.name; public bool IsOpen; public int Shows, Hides;
        public void Show() { IsOpen = true; Shows++; } public void Hide() { IsOpen = false; Hides++; }
    }
    internal sealed class Toggle { public bool interactable; }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple=true)]
    internal sealed class HarmonyPatch : Attribute { public HarmonyPatch() { } public HarmonyPatch(Type type,string method) { } }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPostfix : Attribute { }
    internal static class AccessTools
    {
        public static Type? TypeByName(string name) => typeof(AccessTools).Assembly.GetType(name);
        public static System.Reflection.PropertyInfo? Property(Type type,string name) => type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
        public static System.Reflection.FieldInfo? Field(Type type,string name) => type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
    }
    internal sealed class Harmony { public void PatchAll(Type type) { } }
}
namespace GloomhavenVR.Core
{
    internal static class VRSession { public static HarmonyLib.Harmony? Harmony = new(); }
    internal static class VRLog
    {
        public static readonly List<string> Lines = new();
        public static void Info(string scope,string text) => Lines.Add(text);
        public static void Note(string scope,string text) => Lines.Add(text);
        public static void Warn(string scope,string text) => Lines.Add(text);
    }
}
namespace MapRuleLibrary.MapState { internal sealed class CQuestState { public string ID = "quest"; } }
namespace FFSNet
{
    internal static class FFSNetwork { public static bool IsOnline = true; public static bool IsClient = true; public static bool IsHost => IsOnline && !IsClient; }
    internal sealed class NetworkPlayer { public int PlayerID { get; set; } public bool IsParticipant { get; set; } = true; }
    internal delegate void PlayersChangedEvent(NetworkPlayer player);
    internal static class PlayerRegistry
    {
        public static NetworkPlayer MyPlayer { get; set; } = new() { PlayerID = 1 };
        public static List<NetworkPlayer> AllPlayers = new();
        public static List<NetworkPlayer> Participants => AllPlayers.Where(x => x.IsParticipant).ToList();
        public static PlayersChangedEvent? OnPlayerLeft;
    }
    internal static class Console { public static void LogInfo(string text) { } }
}
internal static class LogUtils { public static void Log(string text) { } }
internal sealed class TextGraphic { public void CrossFadeColor(Color color,float time,bool ignoreTimeScale,bool useAlpha) { } }
internal static class InputManager { public static bool GamePadInUse => false; }
internal sealed class UIInfoTools { public static UIInfoTools Instance = new(); public Color White, greyedOutTextColor; public Sprite GetQuestMarkerHighlightSprite(CQuestState quest) => new(); }
internal static class UIUtility { public static void BringToFront(GameObject go) { } }
internal enum EReadyUpToggleStates { NotSet, Quests, CityEvents, Reward }
internal static class Singleton<T> where T : class { public static T Instance = null!; public static bool IsInitialized => Instance != null; }
internal abstract class UIGuildmasterConfirmActionPresenter
{
    public abstract void ShowQuestSelectedAction(CQuestState quest,Action callback);
    protected Sprite GetQuestSelectedIcon(CQuestState quest) => new();
    protected void PlayShowAudio() { }
}
internal sealed partial class UIGuildmasterConfirmActionButton
{
    private Action? _onConfirmCallback;
    public void Show(Sprite icon,Sprite highlight,Action onClickedCallback,Action? onHoverCallback,Action? onUnhoverCallback) => _onConfirmCallback=onClickedCallback;
}
internal sealed partial class UIGuildmasterConfirmActionButtonPresenter : UIGuildmasterConfirmActionPresenter
{
    private readonly UIGuildmasterConfirmActionButton _button = new();
    public UIGuildmasterConfirmActionButton Button => _button;
    private void ShowMultiplayerQuestPreview(CQuestState quest) => Singleton<UIQuestPopupManager>.Instance.PreviewVisible=true;
    private void HideMultiplayerQuestPreview() => Singleton<UIQuestPopupManager>.Instance.HideMultiplayerPreview();
}
internal sealed class UIGuildmasterConfirmActionPopupPresenter { public void ShowQuestSelectedAction(CQuestState quest,Action callback) { } }
internal sealed partial class UIGuildmasterConfirmActionPopup
{
    private Action? _onConfirmCallback;
    public void SetCallback(Action callback) => _onConfirmCallback=callback;
}
internal sealed class UIQuestPopupManager
{
    public bool PreviewVisible; public int PreviewHides;
    public void HideMultiplayerPreview() { PreviewVisible=false; PreviewHides++; }
}
internal sealed partial class UIReadyToggle
{
    public enum EReadyUpType { Participant, Player }
    public EReadyUpToggleStates readyUpToggleState;
    private EReadyUpType readyUpType;
    private bool _requestVisible, _interactable, _allPlayersReady, _isOn;
    private readonly HashSet<object> _visibilityRequests = new();
    public readonly UIWindow window = new(); public readonly Toggle toggle = new();
    private readonly TextGraphic _textGraphic = new();
    private readonly Progress _progressBar = new();
    private UnityAction? onReady, onUnready, onAllPlayersReady;
    private UnityAction<NetworkPlayer,bool>? onReadiedPlayersChanged;
    private Action? onShortPressed;
    private Func<bool>? canReadyUp;
    private string? readyTextLoc, unreadyTextLoc, readyAudioItem, controllerAreaAllowed;
    private UnityAction<bool>? onStartProgress, onEndProgress;
    private bool validateReadyUpOnPlayerLeft;
    public bool IsVisible => window.IsOpen; public bool ToggledOn => _isOn;
    public List<NetworkPlayer> PlayersReady { get; } = new();
    public int ReadyActions, UnreadyActions; public bool LastAutoValidate;
    public readonly List<bool> Requests = new();
    private sealed class Progress { public readonly GameObject gameObject = new(); }
    private void SetIsOnWithoutNotify(bool isOn) => _isOn = isOn;
    private void OnPlayerLeft(NetworkPlayer player) => PlayersReady.Remove(player);
    public void Block(object key) { _visibilityRequests.Add(key); UpdateVisiblity(); }
    public void Unblock(object key) { _visibilityRequests.Remove(key); UpdateVisiblity(); }
    public void MarkAllReady(bool ready) { _allPlayersReady = ready; UpdateVisiblity(); }
    // Explicit gameplay transport boundary: record the native request, never auto-ready in
    // presentation. Host validation/controllable-state ACK/ReadyProceed are outside this fixture.
    private void ReadyUpPlayer(NetworkPlayer player,EReadyUpToggleStates state) { ReadyActions++; Requests.Add(true); }
    private void UnreadyPlayer(NetworkPlayer player,bool isValidatedAction,bool autoValidateAction) { UnreadyActions++; LastAutoValidate = autoValidateAction; Requests.Add(false); }
    public void CancelProgress() { }
}
internal sealed class UIMapMultiplayerController
{
    public CQuestState? HostSelectedQuest; public int Previews, Reveals;
    // Native callback boundary: the native PreviewQuest calls select/highlight, visibility and
    // interactability. Scene widgets are represented by Previews; no game action is performed.
    public Action Callback() => () => { Previews++; ToggleReadyUpUI(true,EReadyUpToggleStates.Quests); Singleton<MapChoreographer>.Instance.DeterminePlayerToggleInteractability(); };
    public void ToggleReadyUpUI(bool show,EReadyUpToggleStates state) { Reveals++; Singleton<UIReadyToggle>.Instance.ToggleVisibility(show); }
}
internal sealed class MapChoreographer
{
    public bool IsChoosingLinkedQuestOption() => false;
    public void DeterminePlayerToggleInteractability() => Singleton<UIReadyToggle>.Instance.SetInteractable(PlayerRegistry.MyPlayer.IsParticipant);
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { public static bool Active; }
    internal static class StoryComposite { public static bool PointOfNoReturn; }
    internal static class ChromeParkTuning { public const float ClaimGraceSeconds = .5f; }
    internal static class MapTravelConfirm { }
    internal static class MapLocationInteractor
    {
        public static string? Decision; public static float SelectedAt;
        public static bool TryGetPublishedDecision(out string? quest,out float selectedAt) { quest=Decision; selectedAt=SelectedAt; return quest != null; }
    }
}
