using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Read public effect/action models behind original visible widgets. Card IDs, card
/// names and art are never selectors. Resolution searches replicated public action/bonus data.</summary>
internal static class NativeUseBarModels
{
    internal static readonly object Dummy = new();
    private static readonly Dictionary<Type, Dictionary<string, FieldInfo?>> Fields = new();
    internal static T? Field<T>(object source, string name) where T : class
    {
        Type type = source.GetType();
        if (!Fields.TryGetValue(type, out Dictionary<string, FieldInfo?> fields))
        { fields = new Dictionary<string, FieldInfo?>(); Fields.Add(type, fields); }
        if (!fields.TryGetValue(name, out FieldInfo? field))
        { field = AccessTools.Field(type, name); fields.Add(name, field); }
        return field?.GetValue(source) as T;
    }

    internal static object Describe(Component source, NativeUseBarState state)
    {
        CActor actor = Field<CActor>(source, "actor") ?? throw new InvalidOperationException("native slot has no actor");
        state.ActorId = NetFigures.StableActorId(actor);
        object model = Field<object>(source, "element") ?? throw new InvalidOperationException("native slot has no model");
        switch (model)
        {
            case UIUseAbilitiesBar.DummyAbilityInfusion:
                state.ModelKind = NativeUseBarModelKind.DummyInfusion; break;
            case UIUseAbilitiesBar.InfuseAction action:
                state.ModelKind = NativeUseBarModelKind.InfuseAction;
                state.ModelId = Field<CAction>(action, "action")!.ID; break;
            case ChooseAbility choose:
                state.ModelKind = NativeUseBarModelKind.ChooseAbility; state.ModelId = choose.Abilities[0].ID; break;
            case UIUseAbilitiesBar.Ability ability:
                state.ModelKind = NativeUseBarModelKind.Ability; state.ModelId = ability.Abilities[0].ID; break;
            case CItem item:
                state.ModelKind = NativeUseBarModelKind.Item; state.SlotIdentity = UseBarSlotSymbol.ItemId(item); break;
            case ConsumeButtonAugmentation augment:
                DescribeAugment(state, augment.Augmentation, false); break;
            case ConsumeButtonGroupAugmentation group:
                CAction? current = FindGroupAction(actor, group.ID);
                CActionAugmentation? first = current?.Augmentations.Find(a => a.ConsumeGroup == group.ID);
                if (first == null) throw new InvalidOperationException("native augmentation group has no replicated action");
                DescribeAugment(state, first, true); break;
            default: throw new InvalidOperationException("unknown native use-slot model");
        }
        if (state.SlotIdentity == 0)
        {
            // Only a local animation generation guard. Full Guid/kind/index is matched too;
            // unlike record45 this folded value never resolves content by itself.
            byte[] bytes = state.ModelId.ToByteArray(); uint hash = 2166136261;
            foreach (byte value in bytes) hash = (hash ^ value) * 16777619;
            hash = (hash ^ (byte)state.ModelKind) * 16777619;
            hash = (hash ^ state.ModelIndex) * 16777619;
            state.SlotIdentity = (ushort)(hash % 65535 + 1);
        }
        return model;
    }
    private static void DescribeAugment(NativeUseBarState state, CActionAugmentation augment, bool group)
    {
        state.ModelKind = group ? NativeUseBarModelKind.AugmentGroup : NativeUseBarModelKind.Augment;
        state.ModelId = augment.ActionID;
        CAction? action = FindAction(state.ModelId, null);
        if (action == null) throw new InvalidOperationException("native augmentation action is unavailable");
        int index = action.Augmentations.IndexOf(augment);
        if (index < 0 || action.Augmentations.Count > ushort.MaxValue)
            throw new InvalidOperationException("native augmentation is absent from its replicated action");
        state.ModelIndex = checked((ushort)index); state.ModelCount = checked((ushort)action.Augmentations.Count);
    }

    internal static object? Resolve(CPlayerActor? actor, NativeUseBarState state)
    {
        if (actor == null || NetFigures.StableActorId(actor) != state.ActorId) return null;
        if (state.ModelKind == NativeUseBarModelKind.DummyInfusion) return Dummy;
        if (state.ModelKind == NativeUseBarModelKind.Item)
            return UseBarSlotSymbol.ResolveItemModel(actor, state.SlotIdentity, out _);
        if (state.ModelKind == NativeUseBarModelKind.InfuseAction)
            return FindAction(state.ModelId, actor);
        if (state.Bar == 2)
        {
            CAction? action = FindAction(state.ModelId, actor);
            return action != null && action.Augmentations.Count == state.ModelCount && state.ModelIndex < action.Augmentations.Count
                ? action.Augmentations[state.ModelIndex] : null;
        }
        foreach (CAbility ability in Abilities(actor))
            if (ability.ID == state.ModelId)
                return state.ModelKind != NativeUseBarModelKind.ChooseAbility || ability is CAbilityChooseAbility ? ability : null;
        return null;
    }
    internal static List<IOption> Options(object? model, NativeUseBarState state, CPlayerActor? actor)
    {
        var options = new List<IOption>();
        if (model is CAbilityChooseAbility choose)
            foreach (CAbility ability in choose.ApplicableAbilities) options.Add(new AbilityOption(ability));
        else if (model is ChooseAbility native)
            foreach (CAbility ability in ((CAbilityChooseAbility)native.Abilities[0]).ApplicableAbilities) options.Add(new AbilityOption(ability));
        else if (state.ModelKind == NativeUseBarModelKind.AugmentGroup)
        {
            CAction? action = FindAction(state.ModelId, actor);
            if (action != null && state.ModelIndex < action.Augmentations.Count)
            {
                string group = action.Augmentations[state.ModelIndex].ConsumeGroup;
                foreach (CActionAugmentation augment in action.Augmentations)
                    if (augment.ConsumeGroup == group) options.Add(new AugmentationOption(augment));
            }
        }
        if (options.Count > 32) throw new InvalidOperationException("native option count exceeds bounded descriptor");
        return options;
    }
    internal static string SelectedText(IOption option, object? model, int index)
    {
        CAbilityChooseAbility? choose = model as CAbilityChooseAbility;
        if (model is ChooseAbility native) choose = native.Abilities[0] as CAbilityChooseAbility;
        return choose != null ? PreviewEffectGenerator.GenerateDescription(choose.ApplicableAbilities[index]) : option.GetSelectedText();
    }
    private static CAction? FindGroupAction(CActor actor, string group)
    {
        foreach (CAction action in Actions(actor as CPlayerActor))
            if (action.Augmentations.Exists(a => a.ConsumeGroup == group)) return action;
        return null;
    }
    private static CAction? FindAction(Guid id, CPlayerActor? actor)
    { foreach (CAction action in Actions(actor)) if (action.ID == id) return action; return null; }
    private static IEnumerable<CAction> Actions(CPlayerActor? actor)
    {
        if (GameState.CurrentAction?.Action != null) yield return GameState.CurrentAction.Action;
        actor ??= GameState.InternalCurrentActor as CPlayerActor;
        if (actor?.CharacterClass?.RoundAbilityCards != null)
            foreach (CAbilityCard card in actor.CharacterClass.RoundAbilityCards)
            { if (card.TopAction != null) yield return card.TopAction; if (card.BottomAction != null) yield return card.BottomAction; }
    }
    private static IEnumerable<CAbility> Abilities(CPlayerActor actor)
    {
        var stack = new Stack<CAbility>(); var seen = new HashSet<CAbility>();
        foreach (CAction action in Actions(actor)) foreach (CAbility ability in action.Abilities) stack.Push(ability);
        if (PhaseManager.CurrentPhase is CPhaseAction phase && phase.CurrentPhaseAbilities != null)
            foreach (var step in phase.CurrentPhaseAbilities) if (step.m_Ability != null) stack.Push(step.m_Ability);
        List<CActiveBonus>? bonuses = CharacterClassManager.FindAllActiveBonuses(actor);
        if (bonuses != null) foreach (CActiveBonus bonus in bonuses)
        {
            if (bonus.Ability != null) stack.Push(bonus.Ability);
            if (bonus is CStartTurnAbilityActiveBonus start && start.AddAbility != null) stack.Push(start.AddAbility);
        }
        if (actor.Inventory?.AllItems != null) foreach (CItem item in actor.Inventory.AllItems)
            if (item.YMLData?.Data?.Abilities != null) foreach (CAbility ability in item.YMLData.Data.Abilities) stack.Push(ability);
        while (stack.Count > 0)
        {
            CAbility ability = stack.Pop(); if (ability == null || !seen.Add(ability)) continue;
            yield return ability;
            if (ability.SubAbilities != null) foreach (CAbility child in ability.SubAbilities) stack.Push(child);
            if (ability is CAbilityChooseAbility choose) foreach (CAbility child in choose.ApplicableAbilities) stack.Push(child);
        }
    }
}
