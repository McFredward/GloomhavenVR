#nullable disable
#pragma warning disable CS0649 // Original SerializeField fields are initialized by the scene fixture.
using Code.State;
using FFSNet;
using GLOOM;
using MapRuleLibrary.MapState;
using MapRuleLibrary.Party;
using SM.Gamepad;
using Script.GUI.Popups;
using Script.GUI.SMNavigation.HotkeysBehaviour;
using Script.GUI.SMNavigation.States.CampaignMapStates;
using Script.GUI.SMNavigation.States.MainMenuStates;
using Script.GUI.SMNavigation.States.PopupStates;
using Script.GUI.SMNavigation;
using System.Linq;
using System;
using TMPro;
using UnityEngine.UI;
using UnityEngine;


public class UIGuildmasterConfirmActionButton : MonoBehaviour
{
	[SerializeField]
	private Image _icon;

	[SerializeField]
	private Image _highlight;

	[SerializeField]
	private ExtendedButton _button;

	[SerializeField]
	private UITextTooltipTarget _tooltip;

	[SerializeField]
	private GUIAnimator _showAnimation;

	[SerializeField]
	private GUIAnimator _highlightAnimation;

	protected Action _onConfirmCallback;

	protected Action _onHoverCallback;

	protected Action _onUnhoverCallback;

	private void OnEnable()
	{
		_button.onMouseEnter.AddListener(OnHovered);
		_button.onMouseExit.AddListener(OnUnhovered);
		_button.onClick.AddListener(OnClicked);
		if (!_showAnimation.IsPlaying)
		{
			_highlightAnimation.Play(fromStart: true);
		}
	}

	private void OnDisable()
	{
		_button.onClick.RemoveListener(OnClicked);
		_button.onMouseEnter.RemoveListener(OnHovered);
		_button.onMouseExit.RemoveListener(OnUnhovered);
		_showAnimation?.Stop();
		_highlightAnimation.Stop();
	}

	public void Show(Sprite icon, Sprite highlight, Action onClickedCallback, Action onHoverCallback = null, Action onUnhoverCallback = null, bool showAnimation = false, string tooltip = null)
	{
		_onConfirmCallback = onClickedCallback;
		_onHoverCallback = onHoverCallback;
		_onUnhoverCallback = onUnhoverCallback;
		if (showAnimation)
		{
			_showAnimation.Play(fromStart: true);
		}
		base.gameObject.SetActive(value: true);
		_icon.sprite = icon;
		_highlight.sprite = highlight;
		if (!tooltip.IsNullOrEmpty())
		{
			_tooltip.SetText(tooltip);
			_tooltip.enabled = true;
		}
		else
		{
			_tooltip.enabled = false;
		}
	}

	public void Hide()
	{
		if (base.gameObject.activeSelf)
		{
			base.gameObject.SetActive(value: false);
		}
	}

	private void OnHovered()
	{
		_onHoverCallback?.Invoke();
	}

	private void OnUnhovered()
	{
		_onUnhoverCallback?.Invoke();
	}

	private void OnClicked()
	{
		_onConfirmCallback?.Invoke();
	}
}

public class UIGuildmasterConfirmActionPopup : MonoBehaviour
{
	[SerializeField]
	private KeyAction _keyAction;

	[SerializeField]
	private Hotkey _hotkey;

	[SerializeField]
	private LongPressHandler _longPressHandler;

	[SerializeField]
	private TMP_Text _header;

	[SerializeField]
	private TMP_Text _message;

	[SerializeField]
	private Image _icon;

	private SimpleKeyActionHandlerBlocker _partyPanelBlocker = new SimpleKeyActionHandlerBlocker();

	protected Action _onConfirmCallback;

	private void OnEnable()
	{
		_hotkey.TryEnableHotkey();
		Singleton<KeyActionHandlerController>.Instance.AddHandler(new KeyActionHandler(_keyAction, OnConfirmPressed).AddBlocker(_partyPanelBlocker));
		Singleton<UINavigation>.Instance.StateMachine.EventStateChanged += StateMachineOnEventStateChanged;
		StateMachineOnEventStateChanged(Singleton<UINavigation>.Instance.StateMachine.CurrentState);
	}

	private void OnDisable()
	{
		_hotkey.DisableHotkey();
		Singleton<KeyActionHandlerController>.Instance.RemoveHandler(_keyAction, OnConfirmPressed);
		Singleton<UINavigation>.Instance.StateMachine.EventStateChanged -= StateMachineOnEventStateChanged;
	}

	private void StateMachineOnEventStateChanged(IState state)
	{
		CampaignMapStateTag[] source = new CampaignMapStateTag[9]
		{
			CampaignMapStateTag.WorldMap,
			CampaignMapStateTag.Temple,
			CampaignMapStateTag.Merchant,
			CampaignMapStateTag.ShopItemTooltip,
			CampaignMapStateTag.GuildmasterTrainer,
			CampaignMapStateTag.LocationHover,
			CampaignMapStateTag.QuestLog,
			CampaignMapStateTag.MapEvent,
			CampaignMapStateTag.TravelQuestState
		};
		bool block = ((!(state is CampaignMapState campaignMapState)) ? (state is PopupState || state is MainMenuState) : (!source.Contains(campaignMapState.StateTag)));
		_partyPanelBlocker.SetBlock(block);
	}

	public void Show(string header, string message, Sprite icon, Action onConfirmCallback)
	{
		_onConfirmCallback = onConfirmCallback;
		base.gameObject.SetActive(value: true);
		_header.text = header;
		_message.text = message;
		_icon.sprite = icon;
	}

	public void Hide()
	{
		if (base.gameObject.activeSelf)
		{
			base.gameObject.SetActive(value: false);
		}
	}

	private void Confirm()
	{
		_onConfirmCallback?.Invoke();
	}

	private void OnConfirmPressed()
	{
		_longPressHandler.Pressed(Confirm);
	}
}

public class UIGuildmasterConfirmActionButtonPresenter : UIGuildmasterConfirmActionPresenter
{
	[SerializeField]
	private UIGuildmasterConfirmActionButton _button;

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

	public override void HideQuestSelectedAction()
	{
		HideMultiplayerQuestPreview();
		_button.Hide();
	}

	public override void ShowCharacterRetiredAction(CMapCharacter character, NetworkPlayer player, Action onConfirmCallback)
	{
		Sprite characterRetiredIcon = GetCharacterRetiredIcon(character);
		Sprite highlightQuestMarker = UIInfoTools.Instance.highlightQuestMarker;
		string characterRetiredTooltipText = GetCharacterRetiredTooltipText();
		_button.Show(characterRetiredIcon, highlightQuestMarker, onConfirmCallback, null, null, showAnimation: true, characterRetiredTooltipText);
		PlayShowAudio();
	}

	public override void HideCharacterRetiredAction()
	{
		_button.Hide();
	}

	public override void ShowCityEncounterAction(Action onConfirmCallback)
	{
	}

	public override void HideCityEncounterAction()
	{
	}

	public override void ClearAll()
	{
		HideQuestSelectedAction();
		HideCharacterRetiredAction();
	}

	private void ShowMultiplayerQuestPreview(CQuestState quest)
	{
		Singleton<UIQuestPopupManager>.Instance.ShowMultiplayerPreview(quest);
	}

	private void HideMultiplayerQuestPreview()
	{
		Singleton<UIQuestPopupManager>.Instance.HideMultiplayerPreview();
	}

	public override void ToggleOn()
	{
	}

	public override void ToggleOff()
	{
	}
}

public class UIGuildmasterConfirmActionPopupPresenter : UIGuildmasterConfirmActionPresenter
{
	private const string characterRetiredHeader = "CONSOLES/GUI_MULTIPLAYER_POPUP_CHARACTER_RETIRED_HEADER";

	private const string characterRetiredMessage = "CONSOLES/GUI_MULTIPLAYER_POPUP_CHARACTER_RETIRED_MESSAGE";

	private const string questHeader = "CONSOLES/GUI_MULTIPLAYER_POPUP_QUEST_HEADER";

	private const string questMessage = "CONSOLES/GUI_MULTIPLAYER_POPUP_QUEST_MESSAGE";

	private const string cityEncounterHeader = "CONSOLES/GUI_MULTIPLAYER_POPUP_CITY_ENCOUNTER_HEADER";

	private const string cityEncounterMessage = "CONSOLES/GUI_MULTIPLAYER_POPUP_CITY_ENCOUNTER_MESSAGE";

	[SerializeField]
	private UIGuildmasterConfirmActionPopup _popup;

	public override void ShowQuestSelectedAction(CQuestState quest, Action onConfirmCallback)
	{
		Sprite questSelectedIcon = GetQuestSelectedIcon(quest);
		string translation = LocalizationManager.GetTranslation("CONSOLES/GUI_MULTIPLAYER_POPUP_QUEST_HEADER");
		string translation2 = LocalizationManager.GetTranslation("CONSOLES/GUI_MULTIPLAYER_POPUP_QUEST_MESSAGE");
		_popup.Show(translation, translation2, questSelectedIcon, onConfirmCallback);
	}

	public override void HideQuestSelectedAction()
	{
		_popup.Hide();
	}

	public override void ShowCharacterRetiredAction(CMapCharacter character, NetworkPlayer player, Action onConfirmCallback)
	{
		Sprite characterRetiredIcon = GetCharacterRetiredIcon(character);
		string arg = (character.CharacterName.IsNOTNullOrEmpty() ? character.CharacterName : LocalizationManager.GetTranslation(character.CharacterYMLData.LocKey));
		string arg2 = player.UserNameWithPlatformIcon();
		string header = string.Format(LocalizationManager.GetTranslation("CONSOLES/GUI_MULTIPLAYER_POPUP_CHARACTER_RETIRED_HEADER"), arg);
		string message = string.Format(LocalizationManager.GetTranslation("CONSOLES/GUI_MULTIPLAYER_POPUP_CHARACTER_RETIRED_MESSAGE"), arg2);
		_popup.Show(header, message, characterRetiredIcon, onConfirmCallback);
	}

	public override void HideCharacterRetiredAction()
	{
		_popup.Hide();
	}

	public override void ShowCityEncounterAction(Action onConfirmCallback)
	{
		Sprite cityEncounterIcon = GetCityEncounterIcon();
		string translation = LocalizationManager.GetTranslation("CONSOLES/GUI_MULTIPLAYER_POPUP_CITY_ENCOUNTER_HEADER");
		string translation2 = LocalizationManager.GetTranslation("CONSOLES/GUI_MULTIPLAYER_POPUP_CITY_ENCOUNTER_MESSAGE");
		_popup.Show(translation, translation2, cityEncounterIcon, onConfirmCallback);
	}

	public override void HideCityEncounterAction()
	{
		_popup.Hide();
	}

	public override void ClearAll()
	{
		_popup.Hide();
	}

	public override void ToggleOn()
	{
		base.gameObject.SetActive(value: true);
	}

	public override void ToggleOff()
	{
		base.gameObject.SetActive(value: false);
	}
}
