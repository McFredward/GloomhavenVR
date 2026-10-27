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
internal sealed class AdventureMapUIManager
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
internal sealed class MapLocation { }
internal sealed class NativeButton {internal readonly GameObject gameObject=new();}
internal sealed class ReadyTrackerProbe {internal int Shows,Refreshes,Hides;internal void RefreshReady()=>Refreshes++;internal void HideTrackers()=>Hides++;}
internal sealed class HotkeyProbe {internal void SetHotkeyAdded(string key,bool show) { }}
internal sealed class UIGuildmasterHUD {internal bool LastEnabled;internal void EnableHeadquartersOptions(object request,bool enableOptions)=>LastEnabled=enableOptions;}
internal sealed class HelpBox {internal string? Last;internal void Show(string key,string title)=>Last=key;internal void Hide()=>Last=null;}
internal static class NewPartyDisplayUI
{
    internal enum DisplayType {NONE,Character,Inventory}
    internal sealed class PartyDisplayProbe
    {
        internal DisplayType ActiveDisplay;
        internal event System.Action<bool,DisplayType>? OnOpenedPanel;
        internal void Change(bool visible,DisplayType display){ActiveDisplay=visible?display:DisplayType.NONE;OnOpenedPanel?.Invoke(visible,display);}
    }
    internal static PartyDisplayProbe PartyDisplay=new();
}
internal static class CityFlowSetup
{
    internal static void Create()
    {Singleton<UIGuildmasterHUD>.Instance=new();Singleton<HelpBox>.Instance=new();NewPartyDisplayUI.PartyDisplay=new();}
}
