// Verbatim native methods; announcement-tests.sh verifies these against the game reference.
using System;
public partial class UILevelUpWindow
{
	protected void ShowCard()
	{
		if (cardsAddedInLevel.IsNullOrEmpty() || currentCard == cardsAddedInLevel.Count)
		{
			OnFinishedShowCards();
			return;
		}
		AudioControllerUtils.PlaySound(audioItemShowNewCard);
		nextCardTracker.enabled = false;
		enableTracker = false;
		nextCardControllerTip.SetActive(value: false);
		cardHolder.HighlightWonCard(cardsAddedInLevel[currentCard], delegate
		{
			enableTracker = true;
			nextCardTracker.enabled = true;
			if (controllerArea.IsFocused)
			{
				nextCardControllerTip.SetActive(value: true);
			}
		});
	}

	private void OnCardShown()
	{
		nextCardTracker.enabled = false;
		enableTracker = false;
		nextCardControllerTip.SetActive(value: false);
		cardHolder.UnhighlightWonCard(delegate
		{
			inventory.AddNewCard(cardsAddedInLevel[currentCard]);
			currentCard++;
			ShowCard();
		});
	}

	private void OnFinishedShowCards()
	{
		Singleton<UINavigation>.Instance.StateMachine.Enter(CampaignMapStateTag.LevelUp);
		nextCardTracker.enabled = false;
		enableTracker = false;
		inventory.EnableInteraction(enabled: true);
		cardsReceivedText.text = string.Format(LocalizationManager.GetTranslation("GUI_LEVELUP_RECEIVED_CARDS"), cardsAddedInLevel.Count);
		cardsReceivedText.gameObject.SetActive(value: true);
		nextCardControllerTip.SetActive(value: false);
		StartCoroutine(SkipAFrameAndSetIsShownToFalse());
		FinishedShowCards?.Invoke();
		controllerArea.Focus();
	}
}

public sealed partial class ClickTrackerExtended
{
	public void ProcessClick()
	{
		if (SkipNextClick)
		{
			SkipNextClick = false;
		}
		else if (onClick != null)
		{
			AudioControllerUtils.PlaySound(audioItemClick);
			onClick.Invoke();
		}
		else
		{
			Debug.LogError("ClickTrackerExtended: Trying to invoke but the event is null.");
		}
	}

}
