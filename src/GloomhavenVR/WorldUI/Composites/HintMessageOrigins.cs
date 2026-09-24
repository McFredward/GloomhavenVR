using System;
using System.Runtime.CompilerServices;
using GLOO.Introduction;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Associates the actual native queued message with its producer. isShown is a process flag, not
/// the current message identity: several producers can queue messages while one window stays open.
/// Highlight processes enqueue later steps from promise callbacks, so they also restore their own
/// producer scope. Weak keys retain neither dismissed messages nor destroyed process objects.
/// All patches observe calls; native queues, promises, dismissal and gameplay remain untouched.
/// </summary>
internal static class HintMessageOrigins
{
    internal sealed class Origin
    {
        internal readonly Transform? Anchor;
        internal readonly string Description;
        internal Origin(Transform? anchor, string description)
        {
            Anchor = anchor;
            Description = description;
        }
    }

    private static ConditionalWeakTable<object, Origin> _messages = new();
    private static ConditionalWeakTable<UIIntroduceProcess, Origin> _processes = new();
    internal static Origin? CurrentScope;

    internal static Origin? Begin(UIIntroduceBase introducer)
    {
        Origin? previous = CurrentScope;
        try
        {
            Transform anchor = ResolveAnchor(introducer);
            CurrentScope = new Origin(anchor, $"native message producer '{introducer.name}', owner reference '{anchor.name}'");
            if (introducer.process != null)
            {
                _processes.Remove(introducer.process);
                _processes.Add(introducer.process, CurrentScope);
            }
        }
        catch (Exception e)
        {
            CurrentScope = new Origin(introducer.transform, $"native producer '{introducer.name}' (owner lookup failed)");
            VRLog.Warn("WorldUI", $"HINT MESSAGE OWNER: resolving producer failed ({e.GetType().Name}: {e.Message})");
        }
        return previous;
    }

    internal static Origin? Begin(UIIntroduceProcess process)
    {
        Origin? previous = CurrentScope;
        if (_processes.TryGetValue(process, out Origin? origin))
            CurrentScope = origin;
        else if (CurrentScope == null)
            CurrentScope = new Origin(process.transform, $"native highlight process '{process.name}'");
        return previous;
    }

    internal static Origin? Begin(UIIntroductionRewardsProcess rewards)
    {
        Origin? previous = CurrentScope;
        Transform? owner = null;
        bool ambiguous = false;
        foreach (CampaignRewardsManager manager in UnityEngine.Object.FindObjectsOfType<CampaignRewardsManager>(includeInactive: true))
        {
            if (manager != null && manager.introductionProcess == rewards && manager.rewardsWindow != null)
                Match(manager.rewardsWindow.transform, ref owner, ref ambiguous);
        }
        if (Singleton<UIAdventureRewardsManager>.IsInitialized
            && Singleton<UIAdventureRewardsManager>.Instance is UIGuildmasterAdventureRewardsManager guild
            && guild.rewardIntroduction == rewards && guild.window != null)
            Match(guild.window.transform, ref owner, ref ambiguous);
        CurrentScope = new Origin(!ambiguous && owner != null ? owner : rewards.transform,
            $"native reward introduction '{rewards.name}'");
        if (rewards.process != null)
        {
            _processes.Remove(rewards.process);
            _processes.Add(rewards.process, CurrentScope);
        }
        return previous;
    }

    internal static void Record(object message)
    {
        _messages.Remove(message);
        _messages.Add(message, CurrentScope ?? new Origin(null, "direct native introduction with no presenting UI owner"));
    }

    internal static Origin? For(object? message) =>
        message != null && _messages.TryGetValue(message, out Origin? origin) ? origin : null;

    internal static void Reset()
    {
        CurrentScope = null;
        _messages = new ConditionalWeakTable<object, Origin>();
        _processes = new ConditionalWeakTable<UIIntroduceProcess, Origin>();
    }

    // The game serializes some introducers on Campaign Canvas, outside the screen they explain.
    // Reverse those exact serialized references instead of treating transform ancestry as ownership.
    // This census runs once when a producer starts, never during Tick or each queued message.
    private static Transform ResolveAnchor(UIIntroduceBase introducer)
    {
        Transform? owner = null;
        bool ambiguous = false;
        Find<UIPartyCharacterAbilityCardsDisplay>(x => x.introduction == introducer, ref owner, ref ambiguous);
        Find<UIPartyCharacterEquipmentDisplay>(x => x.introduction == introducer, ref owner, ref ambiguous);
        Find<UIPerksWindow>(x => x.introduction == introducer, ref owner, ref ambiguous);
        Find<UILevelUpWindow>(x => x.introduction == introducer, ref owner, ref ambiguous);
        Find<UIShopItemWindow>(x => x.introduction == introducer || x.ftueStep == introducer, ref owner, ref ambiguous);
        Find<UINewEnhancementWindow>(x => x.introduction == introducer, ref owner, ref ambiguous);
        Find<UIBattleGoalPickerWindow>(x => x.introduction == introducer, ref owner, ref ambiguous);
        Find<UITempleWindow>(x => x.introduction == introducer, ref owner, ref ambiguous);
        Find<UICharacterCreatorPersonalQuestStep>(x => x.introduction == introducer || x.ftuePQ == introducer, ref owner, ref ambiguous);
        Find<UICharacterCreatorClassStep>(x => x.ftueClass == introducer, ref owner, ref ambiguous);
        Find<UICampaignAdventurePartyAssemblyWindow>(x => x.ftueStep == introducer, ref owner, ref ambiguous);
        if (Singleton<QuestManager>.IsInitialized)
        {
            QuestManager manager = Singleton<QuestManager>.Instance;
            if (manager != null && manager.questIntroduction == introducer && manager.questLog != null)
                Match(manager.questLog.transform, ref owner, ref ambiguous);
        }
        return !ambiguous && owner != null ? owner : introducer.transform;
    }

    private static void Find<T>(Func<T, bool> refersToProducer, ref Transform? owner, ref bool ambiguous) where T : Component
    {
        T[] all = UnityEngine.Object.FindObjectsOfType<T>(includeInactive: true);
        foreach (T candidate in all)
        {
            if (candidate == null || !refersToProducer(candidate))
                continue;
            Match(candidate.transform, ref owner, ref ambiguous);
        }
    }

    private static void Match(Transform candidate, ref Transform? owner, ref bool ambiguous)
    {
        // Shared serialized producers are not evidence for choosing one of two UI owners.
        if (owner != null && !ReferenceEquals(owner, candidate))
            ambiguous = true;
        owner = candidate;
    }
}

[HarmonyPatch(typeof(UIIntroduceBase), "Show", new[] { typeof(IntroductionConfigUI), typeof(Action) })]
internal static class HintProducerScopePatch
{
    private static void Prefix(UIIntroduceBase __instance, out HintMessageOrigins.Origin? __state) =>
        __state = HintMessageOrigins.Begin(__instance);

    private static Exception? Finalizer(Exception? __exception, HintMessageOrigins.Origin? __state)
    {
        HintMessageOrigins.CurrentScope = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(UIIntroduceProcessHighlight), "Process", new[] { typeof(IntroductionStepUI), typeof(string) })]
internal static class HintHighlightScopePatch
{
    private static void Prefix(UIIntroduceProcessHighlight __instance, out HintMessageOrigins.Origin? __state) =>
        __state = HintMessageOrigins.Begin(__instance);

    private static Exception? Finalizer(Exception? __exception, HintMessageOrigins.Origin? __state)
    {
        HintMessageOrigins.CurrentScope = __state;
        return __exception;
    }
}

// Reward introductions bypass UIIntroduceBase entirely, including promise-driven subsequent
// reward concepts. Their serialized CampaignRewardsManager reference names the native window.
[HarmonyPatch(typeof(UIIntroductionRewardsProcess), "Process", new[] { typeof(EIntroductionConcept) })]
internal static class HintRewardScopePatch
{
    private static void Prefix(UIIntroductionRewardsProcess __instance, out HintMessageOrigins.Origin? __state) =>
        __state = HintMessageOrigins.Begin(__instance);

    private static Exception? Finalizer(Exception? __exception, HintMessageOrigins.Origin? __state)
    {
        HintMessageOrigins.CurrentScope = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(UIIntroductionManager), "AddMessage")]
internal static class HintMessageOriginPatch
{
    private static void Prefix(object __0) => HintMessageOrigins.Record(__0);
}

// Concept calls from MapChoreographer/UIUnlockLocationFlowManager have no UI introducer. A direct
// call can occur inside another producer's synchronous completion callback; do not inherit it.
[HarmonyPatch(typeof(UIIntroductionManager), "Show", new[] { typeof(EIntroductionConcept), typeof(Action) })]
internal static class HintDirectConceptScopePatch
{
    private static void Prefix(out HintMessageOrigins.Origin? __state)
    {
        __state = HintMessageOrigins.CurrentScope;
        HintMessageOrigins.CurrentScope = null;
    }

    private static Exception? Finalizer(Exception? __exception, HintMessageOrigins.Origin? __state)
    {
        HintMessageOrigins.CurrentScope = __state;
        return __exception;
    }
}
