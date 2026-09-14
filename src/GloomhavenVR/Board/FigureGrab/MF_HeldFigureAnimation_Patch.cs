using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>Native clip dispatch is the final actor-specific seam for attack, hit, death, aura
/// and other non-movement animations. Only an actual available non-idle clip releases a hold;
/// a failed animation lookup or idle loop must leave unrelated inspection poses alone.</summary>
[HarmonyPatch(typeof(MF), nameof(MF.AnimatorPlay))]
internal static class MF_HeldFigureAnimation_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Animator animator, string state)
    {
        if ((HeldFigures.Count == 0 && NetHeldFigures.Count == 0) || animator == null
            || animator.runtimeAnimatorController == null || FigureBusy.IsIdleClip(state)
            || !animator.HasState(0, Animator.StringToHash(state)))
            return;
        ActorBehaviour? actor = FindHeldActor(animator.transform);
        if (actor != null)
            ActorBehaviour_HeldTransform_Patch.ReleaseForNativeAction(actor);
    }

    private static ActorBehaviour? FindHeldActor(Transform animated)
    {
        foreach (ActorBehaviour actor in HeldFigures.All)
            if (Contains(actor, animated)) return actor;
        foreach (ActorBehaviour actor in NetHeldFigures.All)
            if (Contains(actor, animated)) return actor;
        return null;
    }

    private static bool Contains(ActorBehaviour actor, Transform animated)
        => actor != null && actor.m_RootGameObject != null
           && animated.IsChildOf(actor.m_RootGameObject.transform);
}
