using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.MapState;
using MapRuleLibrary.Party;
using MapRuleLibrary.State;
using MapRuleLibrary.YML.Achievements;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Set(object target, string name, object value)
    {
        Type type = target.GetType();
        PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null) { property.DeclaringType!.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value); return; }
        (type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException("Missing native field: " + type.FullName + "." + name)).SetValue(target, value);
    }
    private static CMapState State()
    {
        // Fixture boundary: prepare native party containers directly rather than start a campaign,
        // load its complete rules registry, platform services and reward UI. Tested methods are
        // the unchanged original DLL, not copied or reimplemented native method bodies.
        var party = (CMapParty)FormatterServices.GetUninitializedObject(typeof(CMapParty));
        Set(party, "LastScenarioStats", new List<CPlayerStatsScenario>());
        Set(party, "CurrentScenarioStats", new List<CPlayerStatsScenario>());
        Set(party, "LastScenarioHeroSummon", new List<CPlayerStatsScenario>());
        Set(party, "LastScenarioMonster", new List<CPlayerStatsScenario>());
        Set(party, "Achievements", new List<CPartyAchievement>());
        var character = (CMapCharacter)FormatterServices.GetUninitializedObject(typeof(CMapCharacter));
        Set(character, "CharacterID", "DemolitionistID");
        Set(party, "Characters", new List<CMapCharacter> { character });
        var state = new CMapState();
        Set(state, "MapParty", party);
        AdventureState.UpdateMapState(state);
        return state;
    }
    private static CPartyAchievement Achievement(CMapState state)
    {
        // Exact C-C-C-Combo condition in shipped DLC_JoTL_Guildmaster.ruleset.
        // Unlock condition is already satisfied at this external seam; its map/chapter lookup
        // is not the defect being audited. Native achievement and target methods execute intact.
        var target = new CUnlockConditionTarget(EUnlockConditionTargetFilter.DealDamage,
            EUnlockConditionTargetSubFilter.RoundNoReset, new List<string> { "DemolitionistID" },
            null, 0, abilityTypes: new List<CAbility.EAbilityType> { CAbility.EAbilityType.Attack },
            amount: 15, sameTarget: true, times: 3);
        var data = new AchievementYMLData("fixture") {
            ID = "Achievement_Demolitionist_2", AchievementType = EAchievementType.Mercenaries,
            UnlockCondition = new CUnlockCondition(targets: new List<CUnlockConditionTarget>()),
            AchievementCondition = new CUnlockCondition(targets: new List<CUnlockConditionTarget> { target }),
            TreasureTables = new List<string>()
        };
        var achievement = new CPartyAchievement(data);
        Set(achievement, "State", EAchievementState.Unlocked);
        Set(achievement, "m_CachedAchievementData", data);
        Set(achievement, "RolledRewards", true);
        state.MapParty.Achievements.Add(achievement);
        return achievement;
    }
    private static void Attacks(CMapState state, int count, bool sameEnemy = true)
    {
        var stats = new CPlayerStatsScenario("DemolitionistID") { RoundsPlayed = 1 };
        for (int i = 0; i < count; i++)
        {
            var damage = new CPlayerStatsDamage();
            Set(damage, "ActingClassID", "DemolitionistID");
            Set(damage, "ActedOnGUID", sameEnemy ? "enemy-a" : "enemy-" + i);
            Set(damage, "Round", 1);
            Set(damage, "AbilityType", CAbility.EAbilityType.Attack);
            // Original StatsDataStorage derives type labels from the damage recipient
            // and damage source in that order, despite the GUID/class property names.
            damage.ActingType = "Enemy";
            damage.ActedOnType = "Player";
            stats.DamageDealt.Add(damage);
        }
        state.MapParty.LastScenarioStats.Add(stats);
    }
    private static int Progress(CPartyAchievement achievement) => achievement.AchievementConditionState.CurrentProgress;
    public static void Main()
    {
        var state = State(); var achievement = Achievement(state); Attacks(state, 3);
        // Existing result UI clears this exact original list before the later Guildmaster map
        // achievement check; no fabricated condition evaluation is involved.
        state.MapParty.LastScenarioStats.Clear();
        state.CheckNonTrophyAchievements();
        Check(Progress(achievement) == 0, "Current original result-clear ordering loses valid combo progress");

        state = State(); achievement = Achievement(state); Attacks(state, 3);
        state.CheckNonTrophyAchievements();
        Check(Progress(achievement) == 1, "Upstream early native achievement call counts valid combo");
        state.MapParty.LastScenarioStats.Clear(); state.CheckNonTrophyAchievements();
        Check(Progress(achievement) == 1, "Later map call retains already counted combo");

        state = State(); achievement = Achievement(state); Attacks(state, 2);
        state.CheckNonTrophyAchievements();
        Check(Progress(achievement) == 0, "Two same-enemy attacks must not count combo");
        state = State(); achievement = Achievement(state); Attacks(state, 3, false);
        state.CheckNonTrophyAchievements();
        Check(Progress(achievement) == 0, "Three different-enemy attacks must not count combo");

        state = State(); achievement = Achievement(state); Attacks(state, 3);
        state.CheckNonTrophyAchievements(); state.CheckNonTrophyAchievements();
        Check(Progress(achievement) == 2, "Unscoped upstream result prefix can count identical native input twice");

        // Simulate only the difference in local end-result callbacks between a modded peer and
        // an unmodified peer. This demonstrates divergent persisted native state; it does not
        // claim to run Bolt or a complete multiplayer session.
        var modified = State(); var modifiedAchievement = Achievement(modified); Attacks(modified, 3);
        modified.CheckNonTrophyAchievements();
        var vanilla = State(); var vanillaAchievement = Achievement(vanilla); Attacks(vanilla, 3);
        vanilla.MapParty.LastScenarioStats.Clear(); vanilla.CheckNonTrophyAchievements();
        Check(Progress(modifiedAchievement) == 1 && Progress(vanillaAchievement) == 0,
            "A UI-only prefix produces different achievement progress on modified and vanilla peers");
        var modifiedInfo = new SerializationInfo(typeof(CUnlockConditionState), new FormatterConverter());
        modifiedAchievement.AchievementConditionState.GetObjectData(modifiedInfo, default);
        var vanillaInfo = new SerializationInfo(typeof(CUnlockConditionState), new FormatterConverter());
        vanillaAchievement.AchievementConditionState.GetObjectData(vanillaInfo, default);
        var modifiedTargets = (List<CUnlockConditionState.CUnlockConditionTargetState>)modifiedInfo.GetValue("UnlockConditionTargetStates", typeof(List<CUnlockConditionState.CUnlockConditionTargetState>))!;
        var vanillaTargets = (List<CUnlockConditionState.CUnlockConditionTargetState>)vanillaInfo.GetValue("UnlockConditionTargetStates", typeof(List<CUnlockConditionState.CUnlockConditionTargetState>))!;
        Check(modifiedTargets[0].CompletedValue == 1 && vanillaTargets[0].CompletedValue == 0,
            "Divergent combo target counters are written by original native serialization");
        Console.WriteLine("PASS: " + assertions + " unchanged native achievement assertions; original lost-count, early-call repair, wrong-target negatives, repeat hazard and persisted mixed-client divergence.");
        Console.WriteLine("Limits: native methods execute unchanged managed DLLs; party preparation, satisfied unlock and battle stats are explicit seams. No Unity scene, Bolt session, host snapshot reconciliation, rewards or hardware claim.");
    }
}
