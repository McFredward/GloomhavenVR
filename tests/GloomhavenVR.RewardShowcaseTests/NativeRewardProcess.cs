using System;
using System.Collections.Generic;

// The native ProcessRewards iterator is retained verbatim below so tests execute the
// real reward/group/authority control flow. Its Unity presentation and action-bus calls
// are substitutes; they cannot prove rendered pixels. The runner compares this method
// with the read-only decompilation when that reference is available locally.
public sealed partial class UIRewardsManager
{
    private bool nextRewardOverride;
    private readonly NativeLongConfirm _longConfirmHandler = new();
    private SimpleKeyActionHandlerBlocker? blocker;
    private void HandleOnBeforeMainMenuLoadingStarted() { }
    private void InstanceOnEscMenuStateChanged(bool value) { }
    private void InitializeBackground() { }
    public readonly List<int> ShownRewards = new();
    private void ProcessReward(int reward) => ShownRewards.Add(reward);
    public IEnumerator<float> BeginNativeRewards(params int[][] groups)
    {
        var list = new List<RewardGroup>();
        foreach (int[] rewards in groups) list.Add(new RewardGroup(rewards));
        return ProcessRewards(list);
    }
    public bool StepNativeRewards(IEnumerator<float> process)
    {
        _nativeProcessStep = true;
        try { return process.MoveNext(); }
        finally { _nativeProcessStep = false; }
    }
    public void ReceiveNativeProcessNextReward() => nextRewardOverride = true;

	private IEnumerator<float> ProcessRewards(List<RewardGroup> rewardGroups)
	{
		if (InputManager.GamePadInUse)
		{
			Singleton<ESCMenu>.Instance.BeforeMainMenuLoadingStarted += HandleOnBeforeMainMenuLoadingStarted;
			_longConfirmHandler.SetActiveLongConfirmButton(!IsGuildmasterMode);
			blocker = new SimpleKeyActionHandlerBlocker(Singleton<ESCMenu>.Instance.IsOpen);
			Singleton<ESCMenu>.Instance.EscMenuStateChanged += InstanceOnEscMenuStateChanged;
			Singleton<KeyActionHandlerController>.Instance.AddHandler(new KeyActionHandler(KeyAction.CONFIRM_ACTION_BUTTON, ConfirmPressed).AddBlocker(blocker));
		}
		processingRewards = true;
		int rewardGroupIndex = 0;
		int rewardIndex = 0;
		bool movingToNextGroup = true;
		if ((interactionChecker == null) ? FFSNetwork.IsClient : (!interactionChecker()))
		{
			ActionProcessor.SetState(ActionProcessorStateType.ProcessOneAndHalt);
		}
		while (processingRewards)
		{
			bool flag = (!((interactionChecker == null) ? FFSNetwork.IsClient : (!interactionChecker())) || !networkProcessIfServer) && (Singleton<InputManager>.Instance.PlayerControl.MouseClickLeft.WasPressed || isConfirmPressed);
			if (flag && FFSNetwork.IsOnline && ((interactionChecker == null) ? FFSNetwork.IsHost : interactionChecker()) && networkProcessIfServer)
			{
				Synchronizer.SendGameAction(GameActionType.ProcessNextReward);
			}
			if (flag || movingToNextGroup || nextRewardOverride)
			{
				nextRewardOverride = false;
				if (flag && AutoTestController.s_ShouldRecordUIActionsForAutoTest)
				{
					AutoTestController.s_Instance.LogNextRewardClicked();
				}
				if (rewardGroupIndex < rewardGroups.Count)
				{
					if (rewardIndex < rewardGroups[rewardGroupIndex].Rewards.Count)
					{
						InitializeBackground();
						ProcessReward(rewardGroups[rewardGroupIndex].Rewards[rewardIndex]);
						rewardIndex++;
						movingToNextGroup = false;
						if (FFSNetwork.IsOnline && ((interactionChecker == null) ? FFSNetwork.IsClient : (!interactionChecker())))
						{
							ActionProcessor.SetState(ActionProcessorStateType.ProcessOneAndHalt);
						}
						isConfirmPressed = false;
					}
					else
					{
						rewardGroupIndex++;
						rewardIndex = 0;
						movingToNextGroup = true;
					}
				}
				else
				{
					EndProcess();
				}
			}
			yield return 0f;
		}
	}

}
public sealed class RewardGroup
{
    public readonly List<int> Rewards;
    public RewardGroup(int[] rewards) => Rewards = new List<int>(rewards);
}
public sealed class NativeLongConfirm { public void SetActiveLongConfirmButton(bool active) { } }
public sealed class SimpleKeyActionHandlerBlocker { public SimpleKeyActionHandlerBlocker(bool active) { } }
public enum KeyAction { CONFIRM_ACTION_BUTTON }
public sealed class KeyActionHandler
{
    public KeyActionHandler(KeyAction action, Action callback) { }
    public KeyActionHandler AddBlocker(SimpleKeyActionHandlerBlocker blocker) => this;
}
public sealed class KeyActionHandlerController : Singleton<KeyActionHandlerController>
{
    public void AddHandler(KeyActionHandler handler) { }
}
public sealed class InputManager : Singleton<InputManager>
{
    public static bool GamePadInUse;
    public readonly NativePlayerControl PlayerControl = new();
}
public sealed class NativePlayerControl { public readonly NativeMouseClick MouseClickLeft = new(); }
public sealed class NativeMouseClick { public bool WasPressed; }
public enum ActionProcessorStateType { ProcessOneAndHalt }
public static class ActionProcessor
{
    public static int HaltRequests;
    public static void SetState(ActionProcessorStateType state) => HaltRequests++;
}
public enum GameActionType { ProcessNextReward }
public static class Synchronizer
{
    public static int Sent;
    public static void SendGameAction(GameActionType action) => Sent++;
}
public sealed class AutoTestController
{
    public static bool s_ShouldRecordUIActionsForAutoTest;
    public static readonly AutoTestController s_Instance = new();
    public void LogNextRewardClicked() { }
}
