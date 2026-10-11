using System;
using System.Linq;
using ScenarioRuleLibrary;
using SpriteMemoryManagement;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>Read the same portrait selectors as the original ActorStatPanel, without showing
/// an actor or changing its Image/loading request. Only existing scenario actors are visited;
/// borrowed sprites/references are queued in the normal cache and pin owner.</summary>
internal static class StatPortraitPreparation
{
    internal static void Collect(ActorBehaviour[] actors, Action<Sprite> sprite,
        Action<ReferenceToSprite> reference)
    {
        UIInfoTools? tools = UIInfoTools.Instance;
        if (tools == null) return;
        int failures = 0;
        foreach (ActorBehaviour behaviour in actors)
        {
            if (behaviour == null || behaviour.Actor == null) continue;
            try
            {
                CActor actor = behaviour.Actor;
                if (actor is CPlayerActor player)
                {
                    string custom = player.CharacterClass.CharacterYML.CustomCharacterConfig;
                    Sprite? portrait = tools.GetCharacterConfigUI(actor.Class.DefaultModel,
                        useDefault: true, custom).scenarioPreviewInfoPortrait;
                    portrait ??= tools.GetCharacterHeroPortrait(actor.Class.DefaultModel, custom);
                    if (portrait != null) sprite(portrait);
                }
                else if (actor is CEnemyActor enemy)
                {
                    string model = enemy.MonsterClass.DefaultModel;
                    if (enemy is CObjectActor obj && obj.IsAttachedToProp
                        && !string.IsNullOrEmpty(obj.AttachedProp.PropHealthDetails.ActorSpriteName))
                        model = obj.AttachedProp.PropHealthDetails.ActorSpriteName;
                    string? custom = ScenarioRuleClient.SRLYML.MonsterConfigs.SingleOrDefault(
                        s => enemy.MonsterClass.MonsterYML.CustomConfig == s.ID)?.Portrait;
                    ReferenceToSprite? portrait = tools.GetActorPortraitRef(model, custom);
                    if (portrait != null) reference(portrait);
                }
                else if (actor is CHeroSummonActor summon)
                {
                    string? custom = ScenarioRuleClient.SRLYML.MonsterConfigs.SingleOrDefault(
                        s => summon.HeroSummonClass.SummonYML.CustomConfig == s.ID)?.Portrait;
                    ReferenceToSprite? portrait = tools.GetActorPortraitRef(summon.GetPrefabName(), custom);
                    if (portrait != null) reference(portrait);
                }
            }
            catch (Exception error)
            {
                // Missing/modded configs cannot hold the loader or alter native fallback art.
                if (++failures <= 2)
                    Core.VRLog.Warn("WorldUI", $"Original stat portrait preparation skipped ({error.GetType().Name}); live native preview remains available (report {failures}/2).");
            }
        }
    }
}
