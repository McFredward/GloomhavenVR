#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Script.Misc;
using UnityEngine;
using UnityEngine.Events;

// These substitutes provide native scene dependencies. Native prompt, promise and
// retirement methods themselves are retained in the source-bound fixtures.
namespace FFSNet { }
namespace MapRuleLibrary.MapState { }
namespace MapRuleLibrary.Party { }
namespace Code.State { public interface IState { } }
namespace SM.Gamepad { }
namespace Script.GUI.Popups { }
namespace Script.GUI.SMNavigation
{
    public sealed class UINavigation { public readonly NavigationStateMachine StateMachine = new(); }
    public sealed class NavigationStateMachine
    {
        public event Action<Code.State.IState> EventStateChanged;
        public Code.State.IState CurrentState = new States.CampaignMapStates.CampaignMapState();
        public void Change(Code.State.IState state) { CurrentState = state; EventStateChanged?.Invoke(state); }
    }
}
namespace Script.GUI.SMNavigation.HotkeysBehaviour { public sealed class Hotkey { public int Enabled; public void TryEnableHotkey() => Enabled++; public void DisableHotkey() => Enabled--; } }
namespace Script.GUI.SMNavigation.States.CampaignMapStates
{
    public enum CampaignMapStateTag { WorldMap, Temple, Merchant, ShopItemTooltip, GuildmasterTrainer, LocationHover, QuestLog, MapEvent, TravelQuestState, PartyPanel }
    public sealed class CampaignMapState : Code.State.IState { public CampaignMapStateTag StateTag; }
}
namespace Script.GUI.SMNavigation.States.MainMenuStates { public sealed class MainMenuState : Code.State.IState { } }
namespace Script.GUI.SMNavigation.States.PopupStates { public sealed class PopupState : Code.State.IState { } }
namespace GLOOM { public static class LocalizationManager { public static string GetTranslation(string key) => key + " {0}"; } }
public class Singleton<T> where T : new() { public static T Instance = new(); public static bool IsInitialized => Instance != null; }
public sealed class ESCMenu { public bool IsOpen; }
public sealed class ExtendedButton : MonoBehaviour
{
    public readonly UnityEvent onMouseEnter = new(), onMouseExit = new(), onClick = new();
}
public sealed class UITextTooltipTarget : MonoBehaviour { public string Text; public void SetText(string text) => Text = text; }
public sealed class GUIAnimator : MonoBehaviour
{
    public bool IsPlaying;
    public int Plays, Stops;
    public void Play(bool fromStart) { IsPlaying = true; Plays++; }
    public void Stop() { IsPlaying = false; Stops++; }
}
public static class StringExtensions { public static bool IsNullOrEmpty(this string value) => string.IsNullOrEmpty(value); public static bool IsNOTNullOrEmpty(this string value) => !string.IsNullOrEmpty(value); }
public enum KeyAction { Confirm }
public sealed class LongPressHandler { public void Pressed(Action callback) => callback(); }
public sealed class KeyActionHandler
{
    public readonly Action Callback;
    public SimpleKeyActionHandlerBlocker Blocker;
    public KeyActionHandler(KeyAction key, Action callback) => Callback = callback;
    public KeyActionHandler AddBlocker(SimpleKeyActionHandlerBlocker value) { Blocker = value; return this; }
}
public sealed class KeyActionHandlerController
{
    public readonly List<KeyActionHandler> Handlers = new();
    public void AddHandler(KeyActionHandler handler) => Handlers.Add(handler);
    public void RemoveHandler(KeyAction key, Action callback) => Handlers.RemoveAll(x => x.Callback == callback);
}
public sealed class SimpleKeyActionHandlerBlocker
{
    public bool IsBlock { get; private set; }
    public void SetBlock(bool value) => IsBlock = value;
}
public sealed class CMapCharacter
{
    public string CharacterID = "Character", CharacterName = "Native name";
    public bool IsUnderMyControl;
    public readonly object PersonalQuest = new();
    public readonly NativeYml CharacterYMLData = new();
}
public sealed class NativeYml { public string LocKey = "Native loc"; }
public sealed class NetworkPlayer { public string UserNameWithPlatformIcon() => "Player"; }
public sealed class CQuestState { }
public sealed class PersonalQuestDTO { public PersonalQuestDTO(object quest) { } }
public abstract class UIGuildmasterConfirmActionPresenter : MonoBehaviour
{
    public abstract void ShowQuestSelectedAction(CQuestState quest, Action onConfirmCallback);
    public abstract void HideQuestSelectedAction();
    public abstract void ShowCharacterRetiredAction(CMapCharacter character, NetworkPlayer player, Action onConfirmCallback);
    public abstract void HideCharacterRetiredAction();
    public abstract void ShowCityEncounterAction(Action onConfirmCallback);
    public abstract void HideCityEncounterAction();
    public abstract void ClearAll();
    public abstract void ToggleOn();
    public abstract void ToggleOff();
    protected Sprite GetQuestSelectedIcon(CQuestState quest) => UIInfoTools.Instance.NativeIcon;
    protected Sprite GetCharacterRetiredIcon(CMapCharacter character) => UIInfoTools.Instance.NativeIcon;
    protected Sprite GetCityEncounterIcon() => UIInfoTools.Instance.NativeIcon;
    protected string GetCharacterRetiredTooltipText() => "Original native retirement tooltip";
    protected void PlayShowAudio() { }
}
public sealed class UIInfoTools
{
    public static readonly UIInfoTools Instance = new();
    public readonly Sprite NativeIcon = new(), highlightQuestMarker = new();
    public Sprite GetQuestMarkerHighlightSprite(CQuestState quest) => highlightQuestMarker;
}
public sealed class UIQuestPopupManager { public void ShowMultiplayerPreview(CQuestState quest) { } public void HideMultiplayerPreview() { } }
public enum EReadyUpToggleStates { NotSet, Quests, Retirement }
public enum EGuildmasterMode { WorldMap, City, Other }
public sealed class UIGuildmasterHUD { public EGuildmasterMode CurrentMode; public void UpdateCurrentMode(EGuildmasterMode mode) => CurrentMode = mode; }
public sealed class UIConfirmationBoxManager { public void ShowGenericCancelConfirmation(string title, string message, string close) { } }
public sealed class UIPersonalQuestResultManager
{
    public int Notifications, Confirmations;
    public void ShowOtherPlayerCompletedQuestNotification(CMapCharacter character) => Notifications++;
    public ICallbackPromise PlayerConfirmRetirement(CMapCharacter character, PersonalQuestDTO quest)
    { Confirmations++; return Singleton<UIRetirementManager>.Instance.MPConfirmRetire(character, quest); }
}
public sealed class UIReadyToggle
{
    public enum EReadyUpType { Participant, Player }
    private UnityAction _allReady;
    public int OwnReadyPresses, OtherReadyPresses, ExpectedPlayers = 4, Resets;
    public EReadyUpType Type;
    public bool ValidateDeparture;
    public void Initialize(bool show = true, UnityAction onReady = null, UnityAction onUnready = null,
        UnityAction onAllPlayersReady = null, UnityAction<NetworkPlayer, bool> onReadiedPlayersChanged = null,
        Action onShortPressed = null, Func<bool> canReadyUp = null, string readyTextLoc = "", string unreadyTextLoc = "",
        string readyAudioItem = "", bool bringToFront = false, UnityAction<bool> onStartProgress = null,
        UnityAction<bool> onEndProgress = null, bool validateReadyUpOnPlayerLeft = false,
        EReadyUpType readyUpType = EReadyUpType.Participant, EReadyUpToggleStates readyUpToggleState = EReadyUpToggleStates.NotSet)
    { _allReady = onAllPlayersReady; Type = readyUpType; ValidateDeparture = validateReadyUpOnPlayerLeft; }
    public void ReadyUp(bool toggledOn, bool autoValidateUnreadying = false) { if (toggledOn) OwnReadyPresses++; TryProceed(); }
    public void OtherNativePlayerReady() { OtherReadyPresses++; TryProceed(); }
    public void Reset() => Resets++;
    private void TryProceed() { if (OwnReadyPresses + OtherReadyPresses >= ExpectedPlayers && _allReady != null) { UnityAction action = _allReady; _allReady = null; action(); } }
}
