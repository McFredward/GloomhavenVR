using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Exact native components whose original implementation does not write the sampled
/// skeleton. CharacterManager is mandatory on every real bar root; refusing every MonoBehaviour
/// except ActorBehaviour therefore made Build612's optional bone cadence unreachable in game.
/// This is an audited allow list, not a name/disabled-component heuristic. Unknown subclasses,
/// IK, constraints, unknown detail providers and shader vertex dissolve retain immediate checks.
/// </summary>
internal static class NativeActorPoseAudit
{
    // Actual Choreographer.IdleStates vocabulary; authored CLIP names differ by creature.
    private static readonly int[] IdleHashes = { Animator.StringToHash("Idle-Run"), Animator.StringToHash("SleepIdle"),
        Animator.StringToHash("CheerAllyIdle"), Animator.StringToHash("CheerEnemyIdle") };
    internal static bool IsIdleState(AnimatorStateInfo state)
    {
        foreach (int hash in IdleHashes) if (state.shortNameHash == hash) return true;
        return false;
    }
    internal static bool AllowsStateBehaviour(StateMachineBehaviour behaviour)
    {
        Type type = behaviour.GetType();
        // These native callbacks change only the native Animator clock/phase or distinct FX;
        // they never assign an original bone. Continuation callbacks still run natively.
        return type == typeof(IdleSMB) || type == typeof(AnimationOffsetSMB)
            || type == typeof(ToggleAlternativeIdleFxSMB) || type == typeof(ProgressChoreographerSMB);
    }
    internal static bool Allows(Component component, Transform root)
    {
        if (!(component is MonoBehaviour) && !(component is UnityEngine.Animations.IConstraint))
            return true;
        Type type = component.GetType();
        if (type == typeof(ActorBehaviour)) return component.transform == root;
        if (type == typeof(CharacterManager) || type == typeof(ActorEvents)
            || type == typeof(UnityGameEditorObject) || type == typeof(VFXLookup)
            || type == typeof(FootstepSound) || type == typeof(AnimFXTrigger)
            || type == typeof(EPOOutline.Outlinable) || type == typeof(EPOOutline.TargetStateListener)
            || type == typeof(EnemyShadowsDisabler) || type == typeof(CharacterShadowsDisabler))
            return true;
        if (type == typeof(DeathDissolve)) return !((DeathDissolve)component).addVertexAnim
            && !DeathDissolve.s_DeathDissolvesInProgress.Contains((DeathDissolve)component);
        if (type == typeof(AutomaticLOD)) return ((AutomaticLOD)component).LODSwitchMode
            == AutomaticLOD.SwitchMode.UnityLODGroup;
        if (type == typeof(DetailsDisabler))
        {
            foreach (Component provider in component.GetComponents<Component>())
                if (provider is IDetailDisablerProvider && provider.GetType() != typeof(EnemyShadowsDisabler)
                    && provider.GetType() != typeof(CharacterShadowsDisabler)) return false;
            return true;
        }
        return false;
    }

    // These two native modes may change after preparation. Check their cached identities on
    // each shortcut attempt; no renderer/hierarchy/component inventory is required at runtime.
    internal static bool NeedsLiveCheck(Component component) => component.GetType() == typeof(DeathDissolve)
        || component.GetType() == typeof(AutomaticLOD);

    internal static bool StillSafe(List<Component> checks, Transform root)
    {
        foreach (Component component in checks)
            if (component == null || !component.transform.IsChildOf(root) || !Allows(component, root)) return false;
        return true;
    }
}
