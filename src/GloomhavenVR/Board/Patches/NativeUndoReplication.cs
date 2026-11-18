using System;
using System.Collections;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// The native item element picker arms a local cancel override and
/// SkipNextNetworkAction together. In mouse mode the gamepad state's Exit does
/// not run. After the item resolves, hiding Undo clears its override stack but
/// leaves the skip flag set. The next real Undo during target selection can then
/// rewind only the acting client. Issue #24 has two identical ability choices
/// with no intervening Undo on the host, after a native potion confirmation.
/// </summary>
[HarmonyPatch(typeof(UndoButton), nameof(UndoButton.OnClickInternal))]
internal static class NativeUndoReplication
{
    private static readonly FieldInfo? OverridesField = AccessTools.Field(typeof(UndoButton), "m_OnClickOverrides");
    private static bool _reportedRepair;
    private static bool _reportedFailure;

    private static void Prefix(UndoButton __instance, bool networkActionIfOnline, GameAction? action)
    {
        if (!VRSession.IsRunning || !networkActionIfOnline || action != null || !__instance.SkipNextNetworkAction)
            return;

        try
        {
            if (!FFSNetwork.IsOnline || ActionProcessor.CurrentPhase != ActionPhaseType.TargetSelection)
                return;
            Choreographer? choreographer = Choreographer.s_Choreographer;
            if (choreographer == null || choreographer.m_UndoButton != __instance
                || choreographer.CurrentActor == null || !choreographer.ThisPlayerHasTurnControl)
                return;
            if (OverridesField?.GetValue(__instance) is not ICollection { Count: 0 })
                return;

            // The native callback still decides between Undo and ClearTargets,
            // forwards the native action and performs local undo. Never clear
            // suppression while any local-only override can consume this click.
            __instance.SkipNextNetworkAction = false;
            if (!_reportedRepair)
            {
                _reportedRepair = true;
                VRLog.Note("Board", "UNDO REPLICATION: retired the item picker's orphaned local cancel flag before native targeting undo.");
            }
        }
        catch (Exception error)
        {
            if (_reportedFailure)
                return;
            _reportedFailure = true;
            try { VRLog.Warn("Board", "Undo replication guard could not read the native button context: " + error.GetType().Name); }
            catch (Exception) { }
        }
    }
}
