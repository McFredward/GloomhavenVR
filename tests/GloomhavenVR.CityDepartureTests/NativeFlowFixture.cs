#nullable disable warnings
using System;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using MapRuleLibrary.MapState;
using FFSNet;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.PhaseManager;
using UnityEngine;

// These native bodies are copied verbatim and verified against the read-only game.
// Platform HUD fields are boundaries; role selection and visibility requests are native.
internal sealed partial class UIMapMultiplayerController
{
    internal readonly ReadyTrackerProbe trackerBar=new();
    internal readonly NativeButton cancelQuestButton=new();
    private void ShowCharactersTrackers()=>trackerBar.Shows++;
    public bool IsShowingHostQuestToClient()=>false;
    public bool HasConfirmedQuest()=>false;
private bool InQuestSelectionPhase()
	{
		if (AdventureState.MapState.CurrentMapPhaseType != EMapPhaseType.InHQ)
		{
			return AdventureState.MapState.CurrentMapPhaseType == EMapPhaseType.AtLinkedScenario;
		}
		return true;
	}
public void ToggleReadyUpUI(bool show, EReadyUpToggleStates state)
	{
		m_State = state;
		Singleton<UIReadyToggle>.Instance.ToggleVisibility(show);
		if (show)
		{
			ShowCharactersTrackers();
			trackerBar.RefreshReady();
			Singleton<UIGuildmasterHUD>.Instance.EnableHeadquartersOptions(this, enableOptions: false);
			if (!InQuestSelectionPhase() || !FFSNetwork.IsClient)
			{
				return;
			}
			switch (state)
			{
			case EReadyUpToggleStates.Quests:
				if (Singleton<UIReadyToggle>.Instance.PlayersReady.Contains(PlayerRegistry.MyPlayer))
				{
					Singleton<HelpBox>.Instance.Show("GUI_WAIT_PLAYERS_CONFIRM_TIP", "GUI_WAIT_PLAYERS_CONFIRM_QUEST");
				}
				else
				{
					Singleton<HelpBox>.Instance.Show("GUI_ACCEPT_QUEST_TIP", "GUI_ACCEPT_QUEST");
				}
				break;
			case EReadyUpToggleStates.CityEvents:
				if (Singleton<UIReadyToggle>.Instance.PlayersReady.Contains(PlayerRegistry.MyPlayer))
				{
					Singleton<HelpBox>.Instance.Show("GUI_WAIT_PLAYERS_CONFIRM_TIP", "GUI_WAIT_PLAYERS_CONFIRM_CITY_EVENT");
				}
				else
				{
					Singleton<HelpBox>.Instance.Show("GUI_ACCEPT_CITY_EVENT_TIP", "GUI_ACCEPT_CITY_EVENT");
				}
				break;
			case EReadyUpToggleStates.TownRecords:
				if (Singleton<UIReadyToggle>.Instance.PlayersReady.Contains(PlayerRegistry.MyPlayer))
				{
					Singleton<HelpBox>.Instance.Show("GUI_WAIT_PLAYERS_CONFIRM_TIP", "GUI_WAIT_PLAYERS_CONFIRM_TOWN_RECORDS");
				}
				else
				{
					Singleton<HelpBox>.Instance.Show("GUI_ACCEPT_CITY_EVENT_TIP", "GUI_ACCEPT_TOWN_RECORDS");
				}
				break;
			}
		}
		else
		{
			Singleton<HelpBox>.Instance.Hide();
			trackerBar.HideTrackers();
			cancelQuestButton.gameObject.SetActive(value: false);
			if (Singleton<UIGuildmasterHUD>.Instance != null)
			{
				Singleton<UIGuildmasterHUD>.Instance.EnableHeadquartersOptions(this, enableOptions: true);
			}
		}
	}
}
internal sealed partial class AdventureMapUIManager
{
    private MapLocation? locationToTravel;
    private readonly NativeButton travelButton=new();
    internal readonly GameObject travelOptions=new();
    private readonly HotkeyProbe? hotkeySession=new();
    internal int NavigationRefreshes;
    private void TrySwitchToQuestState()=>NavigationRefreshes++;
    internal void SelectLocation()=>SetLocationToTravel(new MapLocation());
private void SetLocationToTravel(MapLocation location)
	{
		locationToTravel = location;
		NewPartyDisplayUI.PartyDisplay.OnOpenedPanel += OnPartyPanelToggled;
	}
private void ResetLocationToTravel()
	{
		locationToTravel = null;
		NewPartyDisplayUI.PartyDisplay.OnOpenedPanel -= OnPartyPanelToggled;
	}
private void OnPartyPanelToggled(bool visible, NewPartyDisplayUI.DisplayType display)
	{
		bool focused = !visible && NewPartyDisplayUI.PartyDisplay.ActiveDisplay == NewPartyDisplayUI.DisplayType.NONE;
		SetFocused(focused);
	}
private void SetFocused(bool focused)
	{
		EnableTravelOptions(focused);
		if (focused)
		{
			TrySwitchToQuestState();
		}
	}
public void EnableTravelOptions(bool show)
	{
		if (!InputManager.GamePadInUse)
		{
			travelButton.gameObject.SetActive(!FFSNetwork.IsOnline);
		}
		bool flag = show && (!FFSNetwork.IsOnline || (!Singleton<UIMapMultiplayerController>.Instance.IsShowingHostQuestToClient() && !Singleton<UIMapMultiplayerController>.Instance.HasConfirmedQuest()));
		travelOptions.SetActive(flag);
		hotkeySession?.SetHotkeyAdded("Back", flag);
		if (FFSNetwork.IsOnline && FFSNetwork.IsHost)
		{
			Singleton<UIMapMultiplayerController>.Instance.ToggleReadyUpUI(show, EReadyUpToggleStates.Quests);
		}
	}
}
internal sealed partial class MapLocation { }
internal sealed class NativeButton {internal readonly GameObject gameObject=new();}
internal sealed class ReadyTrackerProbe {internal int Shows,Refreshes,Hides;internal void RefreshReady()=>Refreshes++;internal void HideTrackers()=>Hides++;}
internal sealed class HotkeyProbe {internal void SetHotkeyAdded(string key,bool show) { }}
internal sealed partial class UIGuildmasterHUD {internal bool LastEnabled;internal void EnableHeadquartersOptions(object request,bool enableOptions)=>LastEnabled=enableOptions;}
internal sealed class HelpBox {internal string? Last;internal void Show(string key,string title)=>Last=key;internal void Hide()=>Last=null;}
internal static class NewPartyDisplayUI
{
    internal enum DisplayType {NONE,Character,Inventory}
    internal sealed class PartyDisplayProbe
    {
        internal DisplayType ActiveDisplay;
        internal bool IsOpen;
        internal void Hide(object owner,bool instant,Action done){IsOpen=false;done();}
        internal void Show(object owner)=>IsOpen=true;
        internal event System.Action<bool,DisplayType>? OnOpenedPanel;
        internal void HideRequiredCharactersWarning() { }
        internal void HideClassCharactersWarning() { }
        internal void Change(bool visible,DisplayType display){ActiveDisplay=visible?display:DisplayType.NONE;OnOpenedPanel?.Invoke(visible,display);}
    }
    internal static PartyDisplayProbe PartyDisplay=new();
}
internal static class CityFlowSetup
{
    internal static void Create()
    {Singleton<UIGuildmasterHUD>.Instance=new();Singleton<HelpBox>.Instance=new();Singleton<MapMarkersManager>.Instance=new();Singleton<AdventureMapUIManager>.Instance=new();NewPartyDisplayUI.PartyDisplay=new();}
}

internal sealed partial class UIMapMultiplayerController
{
public void ConfirmCityEvent()
	{
		if (FFSNetwork.IsOnline && FFSNetwork.IsHost && InQuestSelectionPhase())
		{
			OnReady(ready: true);
			Synchronizer.SendGameAction(GameActionType.SelectCityEvent, ActionPhaseType.MapHQ);
		}
	}
public void OnSelectedCityEvent()
	{
		if (FFSNetwork.IsOnline && InQuestSelectionPhase())
		{
			MapChoreographer.InitReadyToggle(EReadyUpToggleStates.CityEvents);
			ToggleReadyUpUI(show: true, EReadyUpToggleStates.CityEvents);
			Singleton<UIReadyToggle>.Instance.ReadyUp(toggledOn: true);
		}
	}
}

internal sealed partial class UIGuildmasterHUD
{
private void RefreshUnlockedOptions() { }
public void OpenCityEncounter()
	{
		Singleton<MapChoreographer>.Instance.OpenCityEvent();
		RefreshUnlockedOptions();
	}
}

internal sealed partial class MapChoreographer
{
    private void DetermineHostToggleInteractability()=>Singleton<UIReadyToggle>.Instance.SetInteractable(true);
    private void DetermineClientToggleInteractability()=>Singleton<UIReadyToggle>.Instance.SetInteractable(PlayerRegistry.MyPlayer.IsParticipant);
    private void OpenCityEventInternal() { }
    // Callback factory is the native platform boundary; original Initialize/ReadyUp and
    // the explicit game-authored OnSelectedCityEvent/OpenCityEvent methods are unchanged.
    public static void InitReadyToggle(EReadyUpToggleStates state)
    {
        Singleton<UIReadyToggle>.Instance.Initialize(show:false,
            onReady:()=>Singleton<UIMapMultiplayerController>.Instance.ConfirmCityEvent(),
            onUnready:()=>Singleton<UIMapMultiplayerController>.Instance.OnReady(false),
            onAllPlayersReady:()=>Singleton<UIReadyToggle>.Instance.Reset(),
            readyTextLoc:"GUI_DRAW_CITY_EVENT",unreadyTextLoc:"GUI_CITY_EVENT_CANCEL",
            readyAudioItem:"PlaySound_UIMultiQuestSelected",validateReadyUpOnPlayerLeft:false,
            readyUpType:UIReadyToggle.EReadyUpType.Participant,readyUpToggleState:state);
    }
public void OpenCityEvent()
	{
		if (FFSNetwork.IsOnline)
		{
			Singleton<UIMapMultiplayerController>.Instance.OnSelectedCityEvent();
			if (FFSNetwork.IsHost)
			{
				DetermineHostToggleInteractability();
			}
			else
			{
				DetermineClientToggleInteractability();
			}
		}
		else
		{
			OpenCityEventInternal();
		}
	}
}

internal sealed partial class AdventureMapUIManager
{
    private readonly Dictionary<object,bool> lockInteractionRequests=new();
    private readonly GameObject warningTravel=new();
    private Action<MapLocation>? onConfirmTravelCallback;
    internal int CancelAttempts;
    internal bool SelectedPopupVisible;
    public MapLocation LocationToTravel => locationToTravel;
    public bool IsLocked => !lockInteractionRequests.IsNullOrEmpty();
    internal void SetLock(bool value){if(value)lockInteractionRequests[this]=false;else lockInteractionRequests.Clear();}
    internal void Browse(MapLocation location){SetLocationToTravel(location);location.IsSelected=true;SelectedPopupVisible=true;}
    private void StopHideWarningAnimation() { }
public void HideTravelOption()
	{
		UIWindowManager.UnregisterEscapable(this);
		ResetLocationToTravel();
		onConfirmTravelCallback = null;
		EnableTravelOptions(show: false);
		HideTravelWarning();
	}
public void CancelTravel()
	{
		MapLocation mapLocation = ((locationToTravel != null && locationToTravel.IsSelected) ? locationToTravel : null);
		if (!(mapLocation != null) || Singleton<UIMapMultiplayerController>.Instance.CanCancelSelectedLocation(mapLocation))
		{
			HideTravelOption();
			locationToTravel = mapLocation;
			locationToTravel?.ForceHighlight(force: false);
			locationToTravel?.Deselect();
			ResetLocationToTravel();
			UIWindowManager.UnregisterEscapable(this);
		}
	}
public void HideTravelWarning()
	{
		ClearTravelWarning();
	}
private void ClearTravelWarning()
	{
		StopHideWarningAnimation();
		if (Singleton<HelpBox>.Instance != null)
		{
			Singleton<HelpBox>.Instance.Hide();
		}
		warningTravel.SetActive(value: false);
		NewPartyDisplayUI.PartyDisplay.HideRequiredCharactersWarning();
		NewPartyDisplayUI.PartyDisplay.HideClassCharactersWarning();
	}
public void OnDeselectedMapLocation(MapLocation mapLocation)
	{
		if (mapLocation == locationToTravel)
		{
			Debug.LogGUI("OnDeselectedMapLocation");
			HideTravelOption();
			Singleton<MapMarkersManager>.Instance.ShowMarkers();
			Singleton<UIMapMultiplayerController>.Instance.OnDeselectedQuest();
		}
		Singleton<UIGuildmasterHUD>.Instance.EnableHeadquartersOptions(this, enableOptions: true);
	}
}

internal sealed partial class UIMapMultiplayerController
{
internal MapLocation? hostSelectedLocation;
public bool CanCancelSelectedLocation(MapLocation locationToDeselect)
	{
		if (!FFSNetwork.IsOnline || FFSNetwork.IsHost)
		{
			return true;
		}
		if (Singleton<MapChoreographer>.Instance.IsChoosingLinkedQuestOption() && hostSelectedLocation == locationToDeselect)
		{
			return false;
		}
		return true;
	}
public void OnDeselectedQuest()
	{
		if (FFSNetwork.IsOnline)
		{
			cancelQuestButton.gameObject.SetActive(value: false);
			ToggleReadyUpUI(show: false, EReadyUpToggleStates.Quests);
		}
	}
}

internal sealed partial class MapLocation
{
    internal int Selects,Deselects;
    public bool IsSelected;
    private readonly AudioProfile m_AudioProfile=new();
    internal void Select(){Selects++;Singleton<AdventureMapUIManager>.Instance.Browse(this);}
    internal void Deselect(){Deselects++;Singleton<AdventureMapUIManager>.Instance.OnDeselectedMapLocation(this);IsSelected=false;Singleton<AdventureMapUIManager>.Instance.SelectedPopupVisible=false;}
    internal void ForceHighlight(bool force) { }
    private bool IsStartingVillageUnlocked()=>true;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
public void OnPointerClick(PointerEventData eventData)
	{
		if (InputManager.GamePadInUse)
		{
			AudioControllerUtils.PlaySound(m_AudioProfile.mouseDownAudioItem);
			Select();
		}
		else if (eventData.button == PointerEventData.InputButton.Left)
		{
			AudioControllerUtils.PlaySound(m_AudioProfile.mouseDownAudioItem);
			FFSNet.Console.LogInfo("ON MOUSE UP. IsStartingVillageUnlocked: " + IsStartingVillageUnlocked());
			Select();
		}
	}
}

internal sealed class QuestManager
{
    private readonly Dictionary<CQuestState,MapLocation> locations=new();
    internal void Bind(CQuestState quest,MapLocation location)=>locations[quest]=location;
    internal void UserPick(CQuestState quest)=>OnSelectedLogQuest(quest);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
private void OnSelectedLogQuest(CQuestState quest)
	{
		Singleton<MapChoreographer>.Instance.SelectLocation(locations[quest]);
	}
}

internal sealed partial class MapChoreographer
{
    internal bool LinkedChoice;
    public bool IsChoosingLinkedQuestOption()=>LinkedChoice;
    public void SelectLocation(MapLocation location)=>location.Select();
}
internal sealed class MapMarkersManager {internal void ShowMarkers() { }}
internal static class UIWindowManager {internal static int Unregisters;internal static void UnregisterEscapable(object target){NativeProbeFaults.Read("cancel-travel");Unregisters++;}}
internal sealed class AudioProfile {internal string mouseDownAudioItem="click";}
internal static class AudioControllerUtils {internal static void PlaySound(string name) { }}
namespace UnityEngine.EventSystems {internal sealed class PointerEventData {internal enum InputButton{Left,Right} internal InputButton button;}}
namespace MapRuleLibrary.MapState {internal sealed class CQuestState { }}
namespace UnityEngine {internal static class Debug {internal static void LogGUI(string message) { }}}
