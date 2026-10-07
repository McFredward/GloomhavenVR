#nullable disable
using System;
using Assets.Script.GUI.Quest;
using MapRuleLibrary.MapState;
using UnityEngine;
// Verbatim native selection methods. Boundary.cs supplies only surrounding audiovisual APIs.

internal sealed partial class UIQuestPopupManager
{

	public bool IsQuestShown => selectedQuest != null;

	public void ShowQuest(CQuestState questState, bool autoFocus = false)
	{
		ShowQuest(new Quest(questState), autoFocus);
	}

	public void ShowQuest(IQuest questState, bool autoFocus = false)
	{
		HidePreview();
		if (!object.Equals(questState, selectedQuest))
		{
			selectedQuest = questState;
			selectedQuestPopup.ShowQuest(questState, autoFocus);
		}
	}

	public void Hide(IQuest quest)
	{
		if (object.Equals(quest, selectedQuest))
		{
			selectedQuestPopup.Hide();
			selectedQuest = null;
		}
		HidePreview(quest);
	}

	public void HideAll(bool instant = false)
	{
		selectedQuestPopup.Hide(instant);
		selectedQuest = null;
		HidePreview(instant);
		HideMultiplayerPreview(instant);
	}

	public void HideMultiplayerPreview(bool instant = false)
	{
		if (clientSelectedQuest != null)
		{
			clientSelectedQuest = null;
			multiplayerQuestPopup.Hide(instant);
		}
	}

	public void ShowMultiplayerPreview(CQuestState quest)
	{
		if (clientSelectedQuest != quest)
		{
			clientSelectedQuest = quest;
			multiplayerQuestPopup.gameObject.SetActive(value: true);
			multiplayerQuestPopup.ShowQuest(new Quest(quest));
		}
	}

}

internal sealed partial class UIMapMultiplayerController
{

	public CQuestState HostSelectedQuest => hostSelectedLocation?.LocationQuest;

	public void ConfirmSelectedLocation()
	{
		if (FFSNetwork.IsOnline && FFSNetwork.IsHost && InQuestSelectionPhase())
		{
			OnReady(ready: true);
			IProtocolToken supplementaryDataToken = new LocationToken(Singleton<AdventureMapUIManager>.Instance.LocationToTravel.Location.ID);
			Synchronizer.SendGameAction(GameActionType.SelectQuest, ActionPhaseType.MapHQ, validateOnServerBeforeExecuting: false, disableAutoReplication: false, 0, 0, 0, 0, supplementaryDataBoolean: false, default(Guid), supplementaryDataToken);
		}
	}

	public void ProxyHostSelectedLocation(MapLocation location)
	{
		if (!FFSNetwork.IsClient)
		{
			return;
		}
		UIMultiplayerNotifications.ShowSelectedQuest();
		if (hostSelectedLocation != null)
		{
			ProxyHostCancelledSelectedLocation();
		}
		hostSelectedLocation = location;
		if (!Singleton<MapChoreographer>.Instance.IsChoosingLinkedQuestOption())
		{
			GuildmasterConfirmAction.ShowQuestSelectedAction(location.LocationQuest, delegate
			{
				PreviewQuest();
			});
		}
		else
		{
			GuildmasterConfirmAction.HideQuestSelectedAction();
			PreviewQuest(isCancellable: false);
		}
	}

	private void ProxyHostCancelledSelectedLocation()
	{
		if (FFSNetwork.IsClient)
		{
			FFSNet.Console.LogInfo("Host cancelled selected quest");
			if (Singleton<UIReadyToggle>.Instance.ToggledOn)
			{
				Singleton<UIReadyToggle>.Instance.ReadyUp(toggledOn: false, autoValidateUnreadying: true);
			}
			ClearHostSelectedQuest();
		}
	}

	public void ClearHostSelectedQuest(bool force = false)
	{
		if (FFSNetwork.IsClient)
		{
			GuildmasterConfirmAction.HideQuestSelectedAction();
			CancelPreviewedQuest(force);
			Singleton<AdventureMapUIManager>.Instance.OnCancelTravelButtonClick();
			hostSelectedLocation = null;
		}
	}

	private void CancelPreviewedQuest(bool force = false)
	{
		if (FFSNetwork.IsHost)
		{
			Singleton<AdventureMapUIManager>.Instance.DeselectCurrentMapLocation();
			return;
		}
		if (hostSelectedLocation != null && (force || IsShowingHostQuestToClient()))
		{
			hostSelectedLocation.ForceHighlight(force: false);
			hostSelectedLocation.Deselect();
		}
		ToggleReadyUpUI(show: false, EReadyUpToggleStates.Quests);
	}

	private void PreviewQuest(bool isCancellable = true)
	{
		if (Singleton<UIGuildmasterHUD>.Instance.CurrentMode != EGuildmasterMode.WorldMap && Singleton<UIGuildmasterHUD>.Instance.CurrentMode != EGuildmasterMode.City)
		{
			if (AdventureState.MapState.IsCampaign && hostSelectedLocation.LocationQuest.Quest.Type == EQuestType.City)
			{
				Singleton<UIGuildmasterHUD>.Instance.UpdateCurrentMode(EGuildmasterMode.City);
			}
			else
			{
				Singleton<UIGuildmasterHUD>.Instance.UpdateCurrentMode(EGuildmasterMode.WorldMap);
			}
		}
		hostSelectedLocation.ForceHighlight(force: true);
		hostSelectedLocation.Select(isHighlighted: false);
		ToggleReadyUpUI(show: true, EReadyUpToggleStates.Quests);
		Singleton<MapChoreographer>.Instance.DeterminePlayerToggleInteractability();
		cancelQuestButton.gameObject.SetActive(isCancellable);
	}

	public bool IsShowingHostQuestToClient()
	{
		if (FFSNetwork.IsOnline && FFSNetwork.IsClient)
		{
			return Singleton<UIReadyToggle>.Instance.IsVisible;
		}
		return false;
	}

}

internal sealed partial class MapLocation
{

	public void Select(bool isHighlighted = true)
	{
		if (IsSelectable() && m_OnClickAction(this, active: true))
		{
			m_IsSelected = true;
			Highlight(isHighlighted, isSelected: true);
			nameText.fontMaterial = nodeTitleGlowMat;
		}
	}

	public void Deselect(bool keepHover = false)
	{
		if (IsSelectable() && m_OnClickAction(this, active: false))
		{
			m_IsSelected = false;
			Highlight(keepHover);
			nameText.fontMaterial = m_NodeTitleRegularMat;
		}
	}

}
