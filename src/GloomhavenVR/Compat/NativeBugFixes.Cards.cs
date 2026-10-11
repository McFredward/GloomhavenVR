using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using System;
using System.Reflection;

namespace GloomhavenVR.Compat;

/// <summary>
/// Repair only the native card-selection UI after an interrupted extra turn.
/// Bug Fixes 5.0.0 by gummyboars identifies the controller's stale extra-turn
/// restriction in RestorePhase. The original restores card references, but not
/// extraTurnType, before SetPhase(Select2ndCard) disables the remaining legal half.
///
/// This adapts that UI repair without its separate ScenarioRuleLibrary stack
/// rewrite: the existing native actor, turn stacks, played-side flags, actions,
/// network version and replay remain unchanged. SetPhase still owns every final
/// restriction, including restoring the remaining card's halves itself. The
/// upstream broad SetInteractable reset is unnecessary in choice phases and can
/// promote an already-used card in target phases, so it is deliberately omitted.
/// GameState already restored the previous actor when this native message arrives;
/// CurrentHand can still show a borrowed extra turn, so all owner identities must
/// agree before refreshing this UI field. Inert observer clones, absent managers
/// and stale pairs must never enter this repair.
/// </summary>
[HarmonyPatch(typeof(CardsActionControlller), nameof(CardsActionControlller.RestorePhase))]
internal static class NativeCardsRestorePhaseFix
{
    private static readonly FieldInfo? CardOwnerField = AccessTools.Field(typeof(FullAbilityCard), "playerActor");
    private static bool _failureReported;

    [HarmonyPrefix]
    private static void Prefix(CardsActionControlller __instance,
        CardsActionControlller.Phase ___CachedPhase,
        FullAbilityCard? ___cachedTopCard, FullAbilityCard? ___cachedBottomCard,
        ref CAbilityExtraTurn.EExtraTurnType ___extraTurnType)
    {
        try
        {
            if (CardOwnerField == null || !VRSession.IsRunning || __instance != CardsActionControlller.s_Instance
                || ___CachedPhase != CardsActionControlller.Phase.Select1stCard
                    && ___CachedPhase != CardsActionControlller.Phase.Select2ndCard
                || GameState.InternalCurrentActor is not CPlayerActor actor)
                return;

            if (CardsHandManager.Instance == null || CardsHandManager.Instance.CurrentHand == null
                || !ReferenceEquals(CardsHandManager.Instance.CurrentHand.PlayerActor, actor))
                return;

            CPlayerActor? topOwner = ___cachedTopCard != null ? CardOwnerField.GetValue(___cachedTopCard) as CPlayerActor : null;
            CPlayerActor? bottomOwner = ___cachedBottomCard != null ? CardOwnerField.GetValue(___cachedBottomCard) as CPlayerActor : null;
            if (topOwner == null && bottomOwner == null
                || ___cachedTopCard != null && !ReferenceEquals(topOwner, actor)
                || ___cachedBottomCard != null && !ReferenceEquals(bottomOwner, actor))
                return;

            ___extraTurnType = actor.TakingExtraTurnOfType;
        }
        catch (Exception error)
        {
            // An optional UI repair cannot stop native continuation, even if
            // the diagnostic sink also fails. Report at most once per session.
            if (_failureReported) return;
            _failureReported = true;
            try { VRLog.Warn("Compat", "Native card phase UI repair failed; original restore continues: " + error); }
            catch (Exception) { }
        }
    }
}
