using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// Native startup and completion bodies are source-exact (runner validates them).
// Scheduling, artwork, overlay and event sinks are controlled substitutes. The
// first MoveNext is explicit, so no assumption about MEC eager scheduling hides
// pending input or permission state between sessions.
#nullable disable
public sealed partial class UIRewardsManager
{
    private bool hideBlackOverlayInstantly;
    private CoroutineHandle _currentProcessRewardCoroutine;
    public IEnumerator<float> CurrentNativeProcess => _currentProcessRewardCoroutine.Process;
	public void StartRewardsShowcase(List<RewardGroup> rewardGroups, bool hideBlackOverlayInstantly = false, bool networkProcessIfServer = false, Action onProcessEnded = null, Func<bool> interactionChecker = null)
	{
		if (rewardGroups.IsNullOrEmpty() || rewardGroups.FirstOrDefault().Rewards.IsNullOrEmpty())
		{
			Debug.Log("About to start the rewards showcase but there are no rewards to show.");
			onProcessEnded?.Invoke();
			return;
		}
		Debug.Log("Starting the rewards showcase");
		this.hideBlackOverlayInstantly = hideBlackOverlayInstantly;
		this.networkProcessIfServer = networkProcessIfServer;
		this.onProcessEnded = onProcessEnded;
		this.interactionChecker = interactionChecker;
		_currentProcessRewardCoroutine = Timing.RunCoroutine(ProcessRewards(rewardGroups));
		myWindow.Show();
		if (Singleton<UIBlackOverlay>.Instance == null)
		{
			Debug.LogError("UIBlackOverlay.Instance is null in StartRewardsShowcase");
		}
		else
		{
			Singleton<UIBlackOverlay>.Instance.ShowControlled(myWindow, 0.8f, 1f);
		}
	}

	private void EndProcess()
	{
		Debug.Log("Reward showcase has ended.");
		if (InputManager.GamePadInUse && Singleton<ESCMenu>.Instance != null)
		{
			Singleton<ESCMenu>.Instance.BeforeMainMenuLoadingStarted -= HandleOnBeforeMainMenuLoadingStarted;
		}
		Singleton<ESCMenu>.Instance.EscMenuStateChanged -= InstanceOnEscMenuStateChanged;
		Singleton<KeyActionHandlerController>.Instance.RemoveHandler(KeyAction.CONFIRM_ACTION_BUTTON, ConfirmPressed);
		processingRewards = false;
		InitializeBackground();
		myWindow.Hide(instant: true);
		if (hideBlackOverlayInstantly)
		{
			Singleton<UIBlackOverlay>.Instance.HideInstant();
		}
		else
		{
			Singleton<UIBlackOverlay>.Instance.DoTimedTransition(myWindow, UIWindow.VisualState.Hidden);
		}
		UIEventManager.LogUIEvent(new UIEvent(UIEvent.EUIEventType.RewardsWindowDismissed));
		onProcessEnded?.Invoke();
	}

}
#nullable enable
public readonly record struct CoroutineHandle(IEnumerator<float> Process);
public static class Timing
{
    public static CoroutineHandle RunCoroutine(IEnumerator<float> process) => new(process);
}
public static class NativeCollectionExtensions
{
    public static bool IsNullOrEmpty<T>(this ICollection<T>? values) => values == null || values.Count == 0;
}
public sealed class UIBlackOverlay : Singleton<UIBlackOverlay>
{
    public void ShowControlled(UIWindow window, float alpha, float duration) { }
    public void HideInstant() { }
    public void DoTimedTransition(UIWindow window, UIWindow.VisualState state) { }
}
public sealed class UIEvent
{
    public enum EUIEventType { RewardsWindowDismissed }
    public UIEvent(EUIEventType value) { }
}
public static class UIEventManager { public static void LogUIEvent(UIEvent value) { } }
