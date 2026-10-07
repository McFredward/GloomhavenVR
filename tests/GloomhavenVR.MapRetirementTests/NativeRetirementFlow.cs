#nullable disable
using System;
using Assets.Script.Misc;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
// Methods below are verbatim game source. The fixtures replace scene/action transport,
// never the native pending-promise branch or retirement ready barrier.
public sealed class UIMapMultiplayerController
{
    private EReadyUpToggleStates m_State;
    public UIGuildmasterConfirmActionPresenter GuildmasterConfirmAction;
    public void Cancel() { }
    public void HideRetirementMultiplayer() { }
    public void ShowRetirementMultiplayer() { }
    public void UpdateReadyTracker(NetworkPlayer player, bool ready) { }
	public ICallbackPromise ConfirmRetirement(CMapCharacter character, NetworkPlayer player, bool isOptional = true)
	{
		Debug.Log("ConfirmRetirement " + character.CharacterID + " " + m_State);
		if (m_State != 0 && m_State != EReadyUpToggleStates.Retirement)
		{
			Singleton<UIConfirmationBoxManager>.Instance.ShowGenericCancelConfirmation(GLOOM.LocalizationManager.GetTranslation("GUI_RETIREMENT"), GLOOM.LocalizationManager.GetTranslation("GUI_RETIREMENT_CANCEL_SELECTED_QUEST_CONFIRMATION"), "GUI_CLOSE");
			Singleton<UIReadyToggle>.Instance.ReadyUp(toggledOn: false, autoValidateUnreadying: true);
			Cancel();
		}
		m_State = EReadyUpToggleStates.Retirement;
		CallbackPromise callbackPromise;
		if (isOptional && !character.IsUnderMyControl)
		{
			Singleton<UIPersonalQuestResultManager>.Instance.ShowOtherPlayerCompletedQuestNotification(character);
			callbackPromise = new CallbackPromise();
			GuildmasterConfirmAction.ShowCharacterRetiredAction(character, player, callbackPromise.Resolve);
		}
		else
		{
			callbackPromise = CallbackPromise.Resolved();
		}
		return callbackPromise.Then(delegate
		{
			GuildmasterConfirmAction.HideCharacterRetiredAction();
			if (Singleton<UIGuildmasterHUD>.Instance.CurrentMode != EGuildmasterMode.WorldMap && Singleton<UIGuildmasterHUD>.Instance.CurrentMode != EGuildmasterMode.City)
			{
				Singleton<UIGuildmasterHUD>.Instance.UpdateCurrentMode(EGuildmasterMode.WorldMap);
			}
			return Singleton<UIPersonalQuestResultManager>.Instance.PlayerConfirmRetirement(character, new PersonalQuestDTO(character.PersonalQuest));
		});
	}
}
public sealed class UIRetirementManager
{
    public int Commits;
    private ICallbackPromise CommitRetirement(CMapCharacter character, PersonalQuestDTO dto)
    { Commits++; return CallbackPromise.Resolved(); }
	public ICallbackPromise MPConfirmRetire(CMapCharacter character, PersonalQuestDTO completedStepState)
	{
		CallbackPromise promise = new CallbackPromise();
		Singleton<UIReadyToggle>.Instance.Initialize(show: false, null, null, delegate
		{
			Singleton<UIMapMultiplayerController>.Instance.HideRetirementMultiplayer();
			Singleton<UIReadyToggle>.Instance.Reset();
			CommitRetirement(character, completedStepState).Done(promise.Resolve);
		}, delegate(NetworkPlayer player, bool isReady)
		{
			Singleton<UIMapMultiplayerController>.Instance.UpdateReadyTracker(player, isReady);
		}, null, null, "GUI_READY", "GUI_UNREADY", "PlaySound_UIMultiPlayerReady", bringToFront: false, null, null, validateReadyUpOnPlayerLeft: true, UIReadyToggle.EReadyUpType.Player, EReadyUpToggleStates.Retirement);
		Singleton<UIMapMultiplayerController>.Instance.ShowRetirementMultiplayer();
		Singleton<UIReadyToggle>.Instance.ReadyUp(toggledOn: true);
		return promise;
	}
}
