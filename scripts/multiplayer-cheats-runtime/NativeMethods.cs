// Exact original native method bodies, GH.Runtime.dll. Execute method name only adapts the fixture prefix invocation.
#nullable disable
using System;
using System.Linq;
using System.Text;
using ScenarioRuleLibrary;
namespace FFSNet
{
internal sealed partial class GameAction
{
public bool NativeExecute()
	{
		if (actions.TryGetValue((GameActionType)ActionTypeID, out var value))
		{
			bool executionFinished = true;
			value(this, ref isValid, ref executionFinished);
			return executionFinished;
		}
		throw new Exception("Error executing action. Matching delegate method not found.");
	}
}
internal static partial class ActionProcessor
{
public struct ProcessorState
	{
		public ActionProcessorStateType StateType { get; private set; }

		public ActionPhaseType PhaseType { get; private set; }

		public ProcessorState(ActionProcessorStateType stateType, ActionPhaseType phaseType)
		{
			StateType = stateType;
			PhaseType = phaseType;
		}
	}
public static void SetState(ActionProcessorStateType stateType, ActionPhaseType phaseType = ActionPhaseType.NONE, bool savePreviousState = false, bool processQueueImmediately = false)
	{
		if (stateType == currentState.StateType && phaseType == currentState.PhaseType)
		{
			return;
		}
		previousState = currentState;
		currentState = ((stateType == ActionProcessorStateType.SwitchBackToSavedState) ? savedState : new ProcessorState(stateType, phaseType));
		if (stateType == ActionProcessorStateType.SwitchBackToSavedState)
		{
			savedState = default(ProcessorState);
		}
		else if (savePreviousState && stateType != ActionProcessorStateType.SwitchBackToSavedState)
		{
			savedState = previousState;
		}
		Console.LogCoreInfo("STATE: " + currentState.StateType.ToString() + " @ " + currentState.PhaseType, customFlag: true);
		if (currentState.StateType.In(ActionProcessorStateType.ProcessFreely, ActionProcessorStateType.ProcessOneAndHalt, ActionProcessorStateType.ProcessOneAndSwitchBack))
		{
			readyToProcessNextAction = true;
			if (FFSNetwork.IsOnline)
			{
				StartUpActionProcessing(processQueueImmediately);
			}
		}
		else if (currentState.StateType == ActionProcessorStateType.Halted)
		{
			readyToProcessNextAction = false;
		}
		else
		{
			Console.LogError("ERROR_MULTIPLAYER_00014", "Cannot process state: " + currentState.StateType);
		}
	}
public static void QueueUpAction(GameAction action, bool processQueueImmediately = false)
	{
		if (QueueDelay > 0f)
		{
			ActionProcessorCoroutineHelper.RunCoroutine(QueueUpActionAfterDelay(action, processQueueImmediately));
			return;
		}
		actionQueue.Enqueue(action);
		Console.LogInfo("New action queued. Queue size: " + actionQueue.Count);
		StartUpActionProcessing(processQueueImmediately);
	}
private static bool TryProcessNextAction()
	{
		try
		{
			if (!lockProcessingAction && readyToProcessNextAction && actionQueue.Count > 0 && !processingAction)
			{
				GameAction action = actionQueue.Peek();
				if (action == null)
				{
					throw new Exception("Error processing action. Action returns null.");
				}
				if (PlayerRegistry.GetPlayer(action.PlayerID) == null)
				{
					Console.LogCoreInfo("Attempting to process message from NULL player (PlayerID " + action.PlayerID + ").  Skipping this action", customFlag: true);
					actionQueue.Dequeue();
					return true;
				}
				if (action.ActionTypeID == 98 && PlayerRegistry.IsSwitchingCharacter && PlayerRegistry.SwitchingPlayerID != action.DataInt)
				{
					Debug.Log("ToggleIsSwitchingCharacter action discarded as another player is already switching.  PlayerRegistry.SwitchingPlayerID: " + PlayerRegistry.SwitchingPlayerID + "  action.DataInt: " + action.DataInt);
					actionQueue.Dequeue();
					return true;
				}
				if (FFSNetwork.IsHost)
				{
					if (action.ActionTypeID == 3 && PlayerRegistry.IsSwitchingCharacter && (action.SupplementaryDataIDMax != PlayerRegistry.SwitchingPlayerID || action.SupplementaryDataIDMed != PlayerRegistry.SwitchingCharacterSlot))
					{
						Debug.Log(((GameActionType)action.ActionTypeID).ToString() + " action discarded as another player is already switching.  PlayerRegistry.SwitchingPlayerID: " + PlayerRegistry.SwitchingPlayerID + "  action.SupplementaryDataIDMax: " + action.SupplementaryDataIDMax + " PlayerRegistry.SwitchingCharacterSlot: " + PlayerRegistry.SwitchingCharacterSlot + " action.SupplementaryDataIDMed: " + action.SupplementaryDataIDMed);
						actionQueue.Dequeue();
						return true;
					}
					if (action.ActionTypeID == 4 && PlayerRegistry.IsSwitchingCharacter && action.SupplementaryDataIDMax != PlayerRegistry.SwitchingPlayerID)
					{
						Debug.Log(((GameActionType)action.ActionTypeID).ToString() + " action discarded as another player is already switching.  PlayerRegistry.SwitchingPlayerID: " + PlayerRegistry.SwitchingPlayerID + "  action.SupplementaryDataIDMax: " + action.SupplementaryDataIDMax);
						actionQueue.Dequeue();
						return true;
					}
				}
				if (action.TargetPhaseID == 27 && (currentState.PhaseType == ActionPhaseType.MapHQ || currentState.PhaseType == ActionPhaseType.MapAtLinkedScenario))
				{
					actionQueue.Dequeue();
					return true;
				}
				if ((action.ActionTypeID == 38 || action.ActionTypeID == 39) && action.TargetPhaseID == 8 && currentState.PhaseType == ActionPhaseType.TargetSelection)
				{
					action.TargetPhaseID = 16;
				}
				if (action.TargetPhaseID == 1 && currentState.PhaseType == ActionPhaseType.MapAtLinkedScenario)
				{
					action.TargetPhaseID = 30;
				}
				if (action.TargetPhaseID == 30 && currentState.PhaseType == ActionPhaseType.MapHQ)
				{
					action.TargetPhaseID = 1;
				}
				if (currentState.PhaseType == ActionPhaseType.ScenarioEnded && action.TargetPhaseID != 27)
				{
					actionQueue.Dequeue();
					return true;
				}
				if (currentState.PhaseType == ActionPhaseType.StartOfRound && action.TargetPhaseID == 3 && (action.ActionTypeID == 22 || action.ActionTypeID == 20 || action.ActionTypeID == 21))
				{
					actionQueue.Dequeue();
					return true;
				}
				if (currentState.PhaseType != ActionPhaseType.StartOfRound && action.TargetPhaseID == 4 && (action.ActionTypeID == 20 || action.ActionTypeID == 21 || action.ActionTypeID == 43))
				{
					actionQueue.Dequeue();
					return true;
				}
				if (action.ActionTypeID == 22 && action.TargetPhaseID != (int)CurrentPhase)
				{
					NetworkPlayer networkPlayer = PlayerRegistry.AllPlayers.SingleOrDefault((NetworkPlayer s) => s.PlayerID == action.TargetPlayerID);
					if (networkPlayer == null || !networkPlayer.IsActive)
					{
						actionQueue.Dequeue();
						return true;
					}
				}
				if (currentState.PhaseType == ActionPhaseType.MapLoadoutScreen && action.TargetPhaseID == 1 && (action.ActionTypeID == 22 || action.ActionTypeID == 20 || action.ActionTypeID == 21))
				{
					actionQueue.Dequeue();
					return true;
				}
				if ((action.ActionTypeID == 20 || action.ActionTypeID == 21) && FFSNetwork.IsClient && action.TargetPhaseID != (int)currentState.PhaseType && action.TargetPhaseID != 0)
				{
					actionQueue.Dequeue();
					return true;
				}
				if (action.ActionTypeID == 45 && ((ScenarioRuleClient.IsProcessingOrMessagesQueued && !GameState.WaitingForPlayerToSelectDamageResponse && !GameState.WaitingForPlayerActorToAvoidDamageResponse) || Choreographer.s_Choreographer.m_MessageQueue.Count > 0))
				{
					Console.LogWarning("Unable to process ConfirmAction until SRL and choreographer are done processing their messages");
					return false;
				}
				if (action.TargetPhaseID != (int)currentState.PhaseType && action.TargetPhaseID != 0)
				{
					incorrectActionsDetectedCounter++;
					if (incorrectActionsDetectedCounter == 1)
					{
						Console.LogWarning("Incorrect action detected (Action: " + ((GameActionType)action.ActionTypeID).ToString() + " | TargetPhase: " + ((ActionPhaseType)action.TargetPhaseID).ToString() + "). CurrentPhase: " + currentState.PhaseType.ToString() + ". Desync countdown started.");
					}
					else
					{
						Console.LogWarning("Incorrect action detected. Counter: " + incorrectActionsDetectedCounter + "/" + MaxConsecutiveIncorrectActionsAllowed);
					}
					if (incorrectActionsDetectedCounter >= MaxConsecutiveIncorrectActionsAllowed)
					{
						NetworkPlayer player = PlayerRegistry.GetPlayer(action.PlayerID);
						NetworkPlayer myPlayer = PlayerRegistry.MyPlayer;
						string text = ((!FFSNetwork.Manager.DisplayControllableNameForIncorrectActionErrors) ? string.Empty : ControllableRegistry.GetControllable(action.ActorID)?.ControllableObject?.GetName());
						string text2 = string.Empty;
						if (action.BinaryDataIncludesLoggingDetails && action.CustomBinaryData != null)
						{
							text2 = Encoding.ASCII.GetString(action.CustomBinaryData);
						}
						throw new Exception("Error processing action. Timed out after " + FFSNetwork.Manager.IncorrectActionDetectedTimeOutDuration + " seconds waiting for a suitable phase to play the action in a " + PlayerRegistry.Participants.Count + "-player game. Acting Player: " + ((player != null) ? (player.Username + " (ID: " + player.PlayerID + ", " + (player.IsClient ? "CLIENT" : "HOST") + ((!text.IsNullOrEmpty()) ? (", " + text) : "") + ")") : "NULL") + ", Action: " + ((GameActionType)action.ActionTypeID).ToString() + " (Specifics: " + ((!text2.IsNullOrEmpty()) ? text2 : "-") + "), TargetPhase: " + ((ActionPhaseType)action.TargetPhaseID).ToString() + ". My Player: " + ((myPlayer != null) ? (myPlayer.Username + " (ID: " + myPlayer.PlayerID + ", " + (myPlayer.IsClient ? "CLIENT" : "HOST") + ")") : "NULL") + ", Phase: " + currentState.PhaseType.ToString() + ".");
					}
					return false;
				}
				incorrectActionsDetectedCounter = 0;
				ProcessAction(action);
				return true;
			}
			return false;
		}
		catch (Exception ex)
		{
			FFSNetwork.HandleDesync(ex);
			return false;
		}
	}
private static bool ProcessAction(GameAction action)
	{
		Console.LogInfo("Processing action STARTED.");
		processingAction = true;
		actionQueue.Dequeue();
		if (currentState.StateType == ActionProcessorStateType.ProcessOneAndHalt)
		{
			SetState(ActionProcessorStateType.Halted);
		}
		string text = string.Empty;
		try
		{
			if (FFSNetwork.IsHost)
			{
				switch ((GameActionType)action.ActionTypeID)
				{
				case GameActionType.BuyBlessing:
					if (action.SupplementaryDataToken is BlessingToken blessingToken)
					{
						text = text + " Blessed Character ID: " + blessingToken.CharacterID;
					}
					break;
				case GameActionType.CampaignAssignPersonalQuest:
					if (action.SupplementaryDataToken is CampaignPersonalQuestData campaignPersonalQuestData)
					{
						text = text + " Character ID: " + campaignPersonalQuestData.CharacterID;
						text = text + " Character Name: " + campaignPersonalQuestData.CharacterName;
						text = text + " Personal Quest ID: " + campaignPersonalQuestData.PersonalQuestID;
					}
					break;
				}
			}
		}
		catch
		{
		}
		Console.LogCoreInfo("[[Processing GameAction #" + action.ActionID + " (" + ((GameActionType)action.ActionTypeID).ToString() + " @ " + ((ActionPhaseType)action.TargetPhaseID).ToString() + ") initiated by " + PlayerRegistry.GetPlayer(action.PlayerID).Username + "(PlayerID: " + action.PlayerID + ")." + text + "]]", customFlag: true);
		if (action.Execute())
		{
			FinishProcessingAction(action);
			return true;
		}
		return false;
	}
public static void FinishProcessingAction(GameAction action)
	{
		if (!FFSNetwork.IsOnline)
		{
			return;
		}
		if (FFSNetwork.IsHost && action.IsValid)
		{
			action.ActionID = ++PlayerRegistry.MyPlayer.state.LatestProcessedActionID;
			LogAction(action);
			if (!action.DoNotForwardAction)
			{
				Synchronizer.ForwardGameActionToClients(action);
			}
		}
		processingAction = false;
		Console.LogInfo("Processing action FINISHED. Remaining in queue: " + actionQueue.Count);
		if (currentState.StateType == ActionProcessorStateType.ProcessOneAndSwitchBack)
		{
			Console.LogInfo("Switching back to previous state.");
			SetState(previousState.StateType, previousState.PhaseType);
		}
	}
}
internal static partial class Synchronizer
{
public static void AutoExecuteServerAuthGameAction(GameActionType actionType, ActionPhaseType targetPhaseType = ActionPhaseType.NONE, bool disableAutoReplication = false, int actorID = 0, int supplementaryDataIDMin = 0, int supplementaryDataIDMed = 0, int supplementaryDataIDMax = 0, bool supplementaryDataBoolean = false, Guid supplementaryDataGuid = default(Guid), object supplementaryDataToken = null, object supplementaryDataToken2 = null, object supplementaryDataToken3 = null, object supplementaryDataToken4 = null, byte[] customBinaryData = null, bool binaryDataIncludesLoggingDetails = false)
	{
		ReplicateAction(actionType, targetPhaseType, syncViaStateUpdate: false, executeLocallyFirst: true, validateOnServerBeforeExecuting: true, disableAutoReplication, actorID, supplementaryDataIDMin, supplementaryDataIDMed, supplementaryDataIDMax, supplementaryDataBoolean, supplementaryDataGuid, supplementaryDataToken, supplementaryDataToken2, supplementaryDataToken3, supplementaryDataToken4, customBinaryData, binaryDataIncludesLoggingDetails, null);
	}
public static void ForwardGameActionToClients(GameAction action)
	{
		if (FFSNetwork.HasDesynchronized)
		{
			return;
		}
		Console.LogInfo("Forwarding action #" + action.ActionID + " (" + ((GameActionType)action.ActionTypeID).ToString() + ") to " + (action.ValidateAction ? "all clients." : "remaining clients."));
		if (action.SyncViaStateUpdate)
		{
			ControllableRegistry.GetControllable(action.ActorID).UpdateState(action);
			return;
		}
		NetworkPlayer player = PlayerRegistry.GetPlayer(action.PlayerID);
		foreach (NetworkPlayer allPlayer in PlayerRegistry.AllPlayers)
		{
			if (allPlayer != PlayerRegistry.MyPlayer && (action.ValidateAction || allPlayer != player))
			{
				SendGameActionToPlayer(action, allPlayer);
			}
		}
	}
}
}
