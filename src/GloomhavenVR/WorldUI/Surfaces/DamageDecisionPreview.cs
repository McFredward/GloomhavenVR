using GloomhavenVR.Net;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using GloomhavenVR.Cards;
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
        if (pending == null || pending.ActorDamaged == null || NetFigures.StableActorId(pending.ActorDamaged) != NetFigures.StableActorId(actor)) return false;
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

    private sealed class PeerPreview
    {
        internal WorldspacePanelUIController? Controller;
    }
    private static readonly ConditionalWeakTable<RemoteAvatar, PeerPreview> Peers = new();
    private static readonly List<int> Claimants = new();

    internal static void TickRemote(RemoteAvatar owner, DamageDecisionPreviewState? state)
    {
        PeerPreview active = Peers.GetOrCreateValue(owner);
        WorldspacePanelUIController? controller = null;
        if (state != null && WorldUIConfig.ConversionActive && !FlatScreen.ManualScreenActive
            && WorldspaceUITools.Instance != null)
        {
            foreach (WorldspacePanelUIController candidate in WorldspaceUITools.Instance._panelUIControllers)
            {
                CActor? actor = candidate != null ? candidate.InfoUI?.m_ActorBehavior?.Actor : null;
                if (actor != null && NetFigures.StableActorId(actor) == state.ActorId && IsOwner(owner, actor))
                { controller = candidate; break; }
            }
        }
        if (active.Controller != null && active.Controller != controller) ResetRemote(owner);
        if (controller == null || state == null) return;
        controller.Focus(true, "VR_PEER_DAMAGE_PREVIEW");
        Apply(controller, state);
        active.Controller = controller;
    }

    private static bool IsOwner(RemoteAvatar owner, CActor actor)
    {
        CPlayerActor? chooser = actor as CPlayerActor ?? (actor as CHeroSummonActor)?.Summoner;
        if (chooser == null && Singleton<TakeDamagePanel>.IsInitialized)
        {
            TakeDamagePanel panel = Singleton<TakeDamagePanel>.Instance;
            if (NetFigures.StableActorId(panel.actorBeingAttacked) == NetFigures.StableActorId(actor))
                chooser = panel.actorToShowCardsFor;
        }
        if (chooser == null) return false;
        Claimants.Clear();
        CardsGameApi.ClaimantPlayerIds(chooser, Claimants);
        return Claimants.Contains(owner.PlayerId);
    }

    internal static void ResetRemote(RemoteAvatar owner)
    {
        if (!Peers.TryGetValue(owner, out PeerPreview active)) return;
        if (active.Controller != null)
        {
            active.Controller.ResetDamagePreview(0);
            active.Controller.Focus(false, "VR_PEER_DAMAGE_PREVIEW");
        }
        active.Controller = null;
    }

    internal static void Apply(WorldspacePanelUIController controller, DamageDecisionPreviewState state)
    {
        controller.m_HealthBar.PreviewAttack(state.CommittedHealth, state.Damage, state.BaseDamage, state.OriginalMaxHealth);
        controller.m_InfoBar.PreviewAttack(state.CommittedHealth, state.Damage, state.BaseDamage,
            isEnemyAttacking: false, justDamage: true);
    }
}
