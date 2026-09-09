using GloomhavenVR.Net;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>Read-only distinction between an unresolved damage preview and committed actor HP.</summary>
internal static class DamageDecisionPreview
{
    internal static DamageDecisionPreviewState? Local { get; private set; }

    internal static bool TryGetCommittedHealth(CActor actor, out int health)
    {
        health = 0;
        if (actor == null || !(GameState.WaitingForPlayerToSelectDamageResponse
            || GameState.WaitingForPlayerActorToAvoidDamageResponse)) return false;
        GameState.DamageData? pending = GameState.CurrentDamageData;
        if (pending == null || pending.ActorDamaged == null || pending.ActorDamaged.ID != actor.ID) return false;
        // GameState stores this BEFORE CActor.Damaged subtracts tentative damage. Do not derive
        // it from the first sampled frame: a shield may already have restored projected HP.
        health = pending.PreDamageHealth;
        return health >= 0;
    }

    internal static DamageDecisionPreviewState? SampleLocal()
    {
        TakeDamagePanel? panel = Singleton<TakeDamagePanel>.IsInitialized ? Singleton<TakeDamagePanel>.Instance : null;
        CActor? actor = panel?.actorBeingAttacked;
        if (panel == null || actor == null || !panel.ThisPlayerHasTakeDamageControl
            || !TryGetCommittedHealth(actor, out int committed)) { Local = null; return null; }
        // CalculateCurrentHealth is the game's projected result after selected mitigation. The
        // old bridge combined it with a separately stale base-damage label, extending both the
        // green and orange regions. The total bar must always end at the original HP instead.
        int damage = DamageDecisionPreviewMath.Damage(committed, panel.CalculateCurrentHealth());
        var sample = new DamageDecisionPreviewState { ActorId = NetFigures.StableActorId(actor), CommittedHealth = committed,
            Damage = damage, BaseDamage = panel.damageToTake, OriginalMaxHealth = actor.OriginalMaxHealth };
        if (!sample.Validate()) { Local = null; return null; }
        if (!DamageDecisionPreviewState.SamePicture(Local, sample)) Local = sample;
        return Local;
    }

    internal static void Apply(WorldspacePanelUIController controller, DamageDecisionPreviewState state)
    {
        controller.m_HealthBar.PreviewAttack(state.CommittedHealth, state.Damage, state.BaseDamage, state.OriginalMaxHealth);
        controller.m_InfoBar.PreviewAttack(state.CommittedHealth, state.Damage, state.BaseDamage,
            isEnemyAttacking: false, justDamage: true);
    }
}
