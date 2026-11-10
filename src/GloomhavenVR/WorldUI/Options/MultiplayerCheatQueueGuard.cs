using System;
using FFSNet;
using HarmonyLib;

namespace GloomhavenVR.WorldUI;

/// <summary>Admit exact queued test commands without spending a native player-choice permit.</summary>
[HarmonyPatch(typeof(ActionProcessor), "TryProcessNextAction")]
internal static class MultiplayerCheatQueueGuard
{
    private static bool _applyingReceiver;
    private static bool Prefix(bool ___lockProcessingAction, bool ___readyToProcessNextAction, ref bool __result)
    {
        if (_applyingReceiver) { __result = false; return false; }
        GameAction? head;
        try
        {
            if (___lockProcessingAction || !___readyToProcessNextAction || ActionProcessor.IsProcessing
                || ActionProcessor.ActionQueue.Count == 0) return true;
            head = ActionProcessor.ActionQueue.Peek();
            if (!MultiplayerCheats.IsCanonicalHostAction(head)) return true;
            if (MultiplayerCheats.IsOwnedPendingHostAction(head)
                && ActionProcessor.CurrentState.StateType != ActionProcessorStateType.ProcessFreely)
            {
                // Match native stale-action withdrawal, before ProcessAction can
                // change ProcessOneAndHalt or spend ProcessOneAndSwitchBack.
                ActionProcessor.ActionQueue.Dequeue();
                MultiplayerCheats.RetireUnforwarded(head);
                return true;
            }
            if (MultiplayerCheats.WaitForReveal(head)) { __result = false; return false; }
            if (FFSNetwork.IsHost) return true;
        }
        catch { return true; } // A probe failure yields to original native exception handling.

        // The host has already admitted and forwarded this command in the original
        // reliable order. Receiver FinishProcessingAction adds no ID/forwarding but
        // WOULD consume its private one-action permit. Execute only this exact head,
        // retaining lock/ready/phase state and leaving the next real choice untouched.
        __result = true;
        _applyingReceiver = true;
        try
        {
            // Match native ProcessAction's retirement-before-execution: a failed
            // model callback must never leave its head available for another apply.
            ActionProcessor.ActionQueue.Dequeue();
            head.Execute();
        }
        catch (Exception ex) { __result = false; FFSNetwork.HandleDesync(ex); }
        finally { _applyingReceiver = false; }
        return false;
    }
}
