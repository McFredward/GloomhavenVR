using System;
using System.Threading;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Native presentation reads figure transforms before starting animations: attacks aim from the
/// attacker to target positions, and movement samples its origin. End only the affected cosmetic
/// holds before those reads. The original message body, callbacks, queue and rule state are untouched.
/// Human decisions and unrelated idle actors are deliberately absent from this action list.
/// </summary>
[HarmonyPatch(typeof(Choreographer), "ProcessMessage")]
internal static class Choreographer_HeldFigureAction_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Choreographer __instance, CMessageData message)
    {
        // ProcessMessage itself requeues foreign-thread calls. Touch Unity presentation only on
        // the game's main thread, when the original body will actually consume the message.
        if (ScenarioRuleClient.s_MainThread != Thread.CurrentThread || message == null
            || SceneController.Instance.GlobalErrorMessage.ShowingMessage || PhaseManager.CurrentPhase == null)
            return;
        try
        {
            ReleaseParticipants(__instance, message);
        }
        catch (Exception error)
        {
            // This prefix is beneath native network-action dispatch. Cosmetic failure must not
            // escape into the game's desync handler or prevent its authoritative message body.
            VRLog.Error("FigureGrab", $"Native action figure handover failed: {error}");
        }
    }

    internal static void ReleaseParticipants(Choreographer choreographer, CMessageData message)
    {
        if (HeldFigures.Count == 0 && NetHeldFigures.Count == 0)
            return;
        switch (message)
        {
            case CActorHasMoved_MessageData moved:
                Release(choreographer, moved.m_MovingActor);
                if (moved.m_ActorsToCarry != null)
                    foreach (CActor carried in moved.m_ActorsToCarry) Release(choreographer, carried);
                return;
            case CActorHasTeleported_MessageData teleported:
                Release(choreographer, teleported.m_ActorTeleported);
                return;
            case CActorsAreSwapping_MessageData swapped:
                Release(choreographer, swapped.m_FirstTarget);
                Release(choreographer, swapped.m_SecondTarget);
                return;
            case CActorIsAttacking_MessageData attacking:
                Release(choreographer, attacking.m_AttackingActor);
                if (attacking.m_ActorsAttacking != null)
                    foreach (CActor target in attacking.m_ActorsAttacking) Release(choreographer, target);
                return;
            case CActorBeenAttacked_MessageData hit:
                Release(choreographer, hit.m_ActorBeingAttacked);
                return;
            case CActorBeenDamaged_MessageData damaged:
                Release(choreographer, damaged.m_ActorBeingDamaged);
                return;
            case CSummon_MessageData summon:
                Release(choreographer, summon.m_ActorSummoning);
                return;
            case CRevive_MessageData revive:
                Release(choreographer, revive.m_ActorReviving);
                return;
            case CActorDead_MessageData dead:
                Release(choreographer, dead.m_Actor);
                return;
        }
        // These native branches directly LookAt a target before animation dispatch. Even when
        // an ability skips its clip, the native facing operation must start from the board pose.
        // Other clips (including aura originators) release at MF.AnimatorPlay, where the exact
        // animated object and an actually available non-idle state are known.
        switch (message.m_Type)
        {
            case CMessageData.MessageType.ActorIsKilling:
            case CMessageData.MessageType.ActorIsHealing:
            case CMessageData.MessageType.ActorIsSelectingItemCards:
            case CMessageData.MessageType.ActorHasDamaged:
            case CMessageData.MessageType.PlacingTrap:
            case CMessageData.MessageType.DisarmTrap:
            case CMessageData.MessageType.ActivateOrDeactivateSpawner:
            case CMessageData.MessageType.DestroyObstacle:
            case CMessageData.MessageType.ActorIsApplyingConditionActiveBonus:
            case CMessageData.MessageType.RecoverLostCards:
            case CMessageData.MessageType.RecoverDiscardedCards:
            case CMessageData.MessageType.SelectRecoverCards:
            case CMessageData.MessageType.SelectLoseCards:
            case CMessageData.MessageType.SelectIncreasedCardLimit:
            case CMessageData.MessageType.SelectExtraTurnCards:
            case CMessageData.MessageType.ActorIsPulling:
            case CMessageData.MessageType.ActorIsPushing:
                Release(choreographer, message.m_ActorSpawningMessage);
                break;
        }
    }

    private static void Release(Choreographer choreographer, CActor? actor)
    {
        if (actor == null)
            return;
        GameObject root = choreographer.FindClientActorGameObject(actor);
        if (root == null)
            return;
        ActorBehaviour native = ActorBehaviour.GetActorBehaviour(root);
        if (native != null)
            ActorBehaviour_HeldTransform_Patch.ReleaseForNativeAction(native);
    }
}
