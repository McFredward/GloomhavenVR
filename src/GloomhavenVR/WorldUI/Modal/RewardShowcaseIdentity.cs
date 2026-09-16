using ScenarioRuleLibrary;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Addresses the exact chest whose native continuation currently blocks the message pump.</summary>
internal static class RewardShowcaseIdentity
{
    internal static uint ContentKey(UIWindow? window)
    {
        if (window == null || (!window.IsOpen && !window.IsVisible)) return 0;
        Choreographer choreographer = Choreographer.s_Choreographer;
        // ProcessMessage records this exact message before entering WaitingForRewardsProcess.
        // The queue stays blocked until the reward continuation runs. Reading a latest event-log
        // entry instead could address a later simulated chest while its animation is still queued.
        if (choreographer == null || !choreographer.m_BlockClientMessageProcessing
            || choreographer.LastMessage is not CActivateProp_MessageData message
            || message.m_Prop == null
            || (message.m_Prop.ObjectType != ScenarioManager.ObjectImportType.Chest
                && message.m_Prop.ObjectType != ScenarioManager.ObjectImportType.GoalChest)) return 0;

        return HashChestGuid(message.m_Prop.PropGuid);
    }

    internal static uint HashChestGuid(string? guid)
    {
        if (string.IsNullOrEmpty(guid)) return 0;
        // The same public prop GUID is already used to address shared prop poses. Only its
        // stable hash travels: no reward text, private cards, local sequence or Unity instance ID.
        uint hash = 2166136261u;
        unchecked
        {
            for (int i = 0; i < guid!.Length; i++)
                hash = (hash ^ guid[i]) * 16777619u;
        }
        return hash == 0 ? 1u : hash;
    }
}
