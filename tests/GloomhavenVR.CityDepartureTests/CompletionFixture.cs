#nullable disable warnings
using System;using System.Collections.Generic;using System.Linq;using FFSNet;
using MapRuleLibrary.Adventure;

internal sealed partial class UIEventPanel
{
    private readonly List<RewardGroup> appliedRewards=new();
    private Action? onEventCompleted;
    private readonly string finsihAudioItem="finish";
    internal int HiddenCalls;
    private void OnHidden()=>HiddenCalls++;
    internal void ConfigureCompletion(Action done){onEventCompleted=done;appliedRewards.Clear();appliedRewards.Add(new RewardGroup{Rewards=new(){new Reward{Type=ETreasureType.Gold}}});}
    internal void NativeHideAndFinish(){myWindow.Hide();OnFinishedEvent();}
private void OnFinishedEvent()
	{
		if (eventData == null)
		{
			return;
		}
		OnHidden();
		List<Reward> rewards = appliedRewards.SelectMany((RewardGroup it) => it.Rewards).ToList();
		if (AdventureState.MapState.IsCampaign)
		{
			if (eventData.EventType == "RoadEvent")
			{
				Singleton<CampaignRewardsManager>.Instance.ShowAppliedEffects(rewards, delegate
				{
					DistributeRewards(rewards);
				});
				return;
			}
			AdventureState.MapState.ApplyRewards(rewards.FindAll((Reward it) => it.Type == ETreasureType.Prosperity), "party");
			Singleton<CampaignRewardsManager>.Instance.ShowAppliedEffects(rewards, delegate
			{
				DistributeRewards(rewards.FindAll((Reward it) => it.Type != ETreasureType.Prosperity));
			});
		}
		else
		{
			Singleton<UIRewardsManager>.Instance.StartRewardsShowcase(appliedRewards, hideBlackOverlayInstantly: false, networkProcessIfServer: true, delegate
			{
				DistributeRewards(rewards);
			});
		}
	}
private void DistributeRewards(List<Reward> rewards)
	{
		bool isRoadEvent = eventData.EventType == "RoadEvent";
		bool readyUp = isRoadEvent && rewards.Count > 0;
		if (readyUp)
		{
			ActionProcessor.SetState(ActionProcessorStateType.ProcessFreely, ActionPhaseType.MapEvent);
		}
		Singleton<UIDistributeRewardManager>.Instance.Process(rewards, delegate
		{
			if (readyUp)
			{
				ActionProcessor.SetState(ActionProcessorStateType.Halted, ActionPhaseType.MapEvent);
			}
			if (isRoadEvent && NewPartyDisplayUI.PartyDisplay.IsOpen)
			{
				NewPartyDisplayUI.PartyDisplay.Hide(this, instant: false, OnDistributedRewards);
			}
			else
			{
				OnDistributedRewards();
			}
		}, delegate
		{
			NewPartyDisplayUI.PartyDisplay.Show(this);
		}, readyUp);
	}
private void OnDistributedRewards()
	{
		eventData = null;
		AudioControllerUtils.PlaySound(finsihAudioItem);
		onEventCompleted?.Invoke();
	}
}

internal sealed class EventRecord {internal string EventType="CityEvent";}
internal enum ETreasureType {Prosperity,Gold}
internal sealed class Reward {internal ETreasureType Type;}
internal sealed class RewardGroup {internal List<Reward> Rewards=new();}
internal sealed class CampaignRewardsManager
{
    internal Action? Pending;
    internal void ShowAppliedEffects(List<Reward> rewards,Action done)=>Pending=done;
    internal void Release(){var done=Pending;Pending=null;done?.Invoke();}
}
internal sealed class UIDistributeRewardManager
{
    internal Action? Pending;
    internal void Process(List<Reward> rewards,Action done,Action show,bool readyUp)=>Pending=done;
    internal void Release(){var done=Pending;Pending=null;done?.Invoke();}
}
internal sealed class UIRewardsManager
{
    internal Action? Pending;
    internal void StartRewardsShowcase(List<RewardGroup> groups,bool hideBlackOverlayInstantly,bool networkProcessIfServer,Action done)=>Pending=done;
    internal void Release(){var done=Pending;Pending=null;done?.Invoke();}
}
namespace MapRuleLibrary.Adventure
{
    internal sealed partial class CityMapState
    {internal bool IsCampaign=true;internal int AppliedRewards;internal void ApplyRewards(List<Reward> rewards,string target)=>AppliedRewards+=rewards.Count;}
}
namespace FFSNet
{
    internal enum ActionProcessorStateType {ProcessFreely,Halted}
    internal static partial class ActionProcessor
    {internal static void SetState(ActionProcessorStateType state,ActionPhaseType phase)=>CurrentPhase=phase;}
}
