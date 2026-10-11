// Verbatim native method bodies; pinned by the runner against read-only game source when available.
#nullable disable
using System;using System.Linq;using FFSNet;using UnityEngine;using UnityEngine.UI;using UnityEngine.Events;using MapRuleLibrary.MapState;
internal sealed partial class UIReadyToggle {
public bool ShouldBeVisible
	{
		get
		{
			if (_requestVisible && _interactable && !_allPlayersReady)
			{
				return _visibilityRequests.Count == 0;
			}
			return false;
		}
	}
public void Initialize(bool show = true, UnityAction onReady = null, UnityAction onUnready = null, UnityAction onAllPlayersReady = null, UnityAction<NetworkPlayer, bool> onReadiedPlayersChanged = null, Action onShortPressed = null, Func<bool> canReadyUp = null, string readyTextLoc = "GUI_READY", string unreadyTextLoc = "GUI_UNREADY", string readyAudioItem = "PlaySound_UIMultiPlayerReady", bool bringToFront = false, UnityAction<bool> onStartProgress = null, UnityAction<bool> onEndProgress = null, bool validateReadyUpOnPlayerLeft = false, EReadyUpType readyUpType = EReadyUpType.Participant, EReadyUpToggleStates readyUpToggleState = EReadyUpToggleStates.NotSet, string controllerAreaAllowed = null)
	{
		this.onReady = onReady;
		this.onUnready = onUnready;
		this.onAllPlayersReady = onAllPlayersReady;
		this.onReadiedPlayersChanged = onReadiedPlayersChanged;
		this.onShortPressed = onShortPressed;
		this.canReadyUp = canReadyUp;
		this.readyTextLoc = readyTextLoc;
		this.unreadyTextLoc = unreadyTextLoc;
		this.readyAudioItem = readyAudioItem;
		this.onStartProgress = onStartProgress;
		this.onEndProgress = onEndProgress;
		this.validateReadyUpOnPlayerLeft = validateReadyUpOnPlayerLeft;
		this.readyUpType = readyUpType;
		this.readyUpToggleState = readyUpToggleState;
		this.controllerAreaAllowed = controllerAreaAllowed;
		_allPlayersReady = false;
		_progressBar.gameObject.SetActive(value: false);
		SetInteractable(interactable: false);
		SetIsOnWithoutNotify(isOn: false);
		PlayerRegistry.OnPlayerLeft = (PlayersChangedEvent)Delegate.Remove(PlayerRegistry.OnPlayerLeft, new PlayersChangedEvent(OnPlayerLeft));
		PlayerRegistry.OnPlayerLeft = (PlayersChangedEvent)Delegate.Combine(PlayerRegistry.OnPlayerLeft, new PlayersChangedEvent(OnPlayerLeft));
		foreach (NetworkPlayer item in PlayersReady.Except(PlayerRegistry.AllPlayers).ToList())
		{
			OnPlayerLeft(item);
		}
		if (show)
		{
			ToggleVisibility(visible: true, bringToFront);
		}
	}
public void ToggleVisibility(bool visible, bool bringToFront = false)
	{
		_requestVisible = visible;
		if (visible && bringToFront)
		{
			UIUtility.BringToFront(window.gameObject);
		}
		UpdateVisiblity();
	}
public void SetInteractable(bool interactable)
	{
		if (_interactable != interactable)
		{
			_interactable = interactable;
			toggle.interactable = interactable;
			if (!InputManager.GamePadInUse)
			{
				_textGraphic.CrossFadeColor(interactable ? UIInfoTools.Instance.White : UIInfoTools.Instance.greyedOutTextColor, 0f, ignoreTimeScale: true, useAlpha: true);
			}
			UpdateVisiblity();
			FFSNet.Console.LogInfo("MP Ready Toggle set to " + (interactable ? " INTERACTABLE." : " UNINTERACTABLE."));
		}
	}
private void UpdateVisiblity()
	{
		bool shouldBeVisible = ShouldBeVisible;
		if (shouldBeVisible != IsVisible)
		{
			if (shouldBeVisible)
			{
				window.Show();
			}
			else
			{
				CancelProgress();
				window.Hide();
			}
			FFSNet.Console.LogInfo("MP Ready Toggle set to " + (ShouldBeVisible ? " VISIBLE." : "INVISIBLE."));
		}
	}
private bool IsReadyUpForbidden()
	{
		if (canReadyUp != null && !canReadyUp())
		{
			SetIsOnWithoutNotify(isOn: false);
			return true;
		}
		return false;
	}
public void ReadyUp(bool toggledOn, bool autoValidateUnreadying)
	{
		_progressBar.gameObject.SetActive(value: false);
		if (!FFSNetwork.IsOnline || (PlayersReady.Count >= ((readyUpType == EReadyUpType.Participant) ? PlayerRegistry.Participants : PlayerRegistry.AllPlayers).Count && !(!toggledOn && autoValidateUnreadying)))
		{
			return;
		}
		if (toggledOn)
		{
			if (!IsReadyUpForbidden())
			{
				ReadyUpPlayer(PlayerRegistry.MyPlayer, readyUpToggleState);
			}
		}
		else
		{
			UnreadyPlayer(PlayerRegistry.MyPlayer, isValidatedAction: false, autoValidateUnreadying);
		}
	}
}
internal sealed partial class UIGuildmasterConfirmActionButtonPresenter {
public override void ShowQuestSelectedAction(CQuestState quest, Action onConfirmCallback)
	{
		Sprite questSelectedIcon = GetQuestSelectedIcon(quest);
		Sprite questMarkerHighlightSprite = UIInfoTools.Instance.GetQuestMarkerHighlightSprite(quest);
		Action onClickedCallback = delegate
		{
			HideMultiplayerQuestPreview();
			onConfirmCallback?.Invoke();
		};
		Action onHoverCallback = delegate
		{
			ShowMultiplayerQuestPreview(quest);
		};
		Action onUnhoverCallback = delegate
		{
			HideMultiplayerQuestPreview();
		};
		_button.Show(questSelectedIcon, questMarkerHighlightSprite, onClickedCallback, onHoverCallback, onUnhoverCallback);
		PlayShowAudio();
	}
}
internal sealed partial class UIGuildmasterConfirmActionButton {
private void OnClicked()
	{
		_onConfirmCallback?.Invoke();
	}
}
internal sealed partial class UIGuildmasterConfirmActionPopup {
private void Confirm()
	{
		_onConfirmCallback?.Invoke();
	}
}
