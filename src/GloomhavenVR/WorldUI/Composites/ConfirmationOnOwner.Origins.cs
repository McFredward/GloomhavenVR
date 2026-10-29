using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SpriteMemoryManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ConfirmationOnOwner
{
    internal sealed class Request
    {
        internal readonly Component Source;
        internal readonly UIWindow? Window;
        internal bool ReportedFailure;
        internal Request(Component source)
        {
            Source = source;
            Window = source.GetComponentInParent<UIWindow>();
        }
    }

    private static ConditionalWeakTable<Component, Request> Requests = new();
    internal static Component? CurrentScope;

    internal static Component? Begin(Component source)
    {
        Component? previous = CurrentScope;
        CurrentScope = source;
        return previous;
    }

    internal static void Record(UIConfirmationBoxManager manager)
    {
        ConfirmationBox? box = manager.CurrentBox;
        if (box != null) Record(box);
    }

    internal static void Record(Component box)
    {
        Requests.Remove(box);
        if (CurrentScope != null) Requests.Add(box, new Request(CurrentScope));
    }
}

/// <summary>These exact original methods synchronously raise generic confirmations.
/// A prefix/finalizer scope survives exceptions and nested producers; no late focus scan
/// or event subscriber ordering decides which native screen owns the pooled box.</summary>
[HarmonyPatch]
internal static class ConfirmationProducerScopePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(typeof(UILevelUpWindow), "ConfirmSelectCard");
        yield return AccessTools.DeclaredMethod(typeof(UIResetLevelUpWindow), "ResetAbilities");
        yield return AccessTools.DeclaredMethod(typeof(UIPerksInventory), "OnSelectedPerk");
        yield return AccessTools.DeclaredMethod(typeof(UIPartyItemInventoryDisplay), "OnItemSelected");
        yield return AccessTools.DeclaredMethod(typeof(UIAdventurePartyAssemblyRoster), "OnSelectedCharacter");
        yield return AccessTools.DeclaredMethod(typeof(UIOptionsWindow), "AskLogOut");
        yield return AccessTools.DeclaredMethod(typeof(ESCMenu), "LoadMainMenu");
        yield return AccessTools.DeclaredMethod(typeof(ESCMenu), "ExitGame");
        yield return AccessTools.DeclaredMethod(typeof(UIMapEscMenu), "TurnOffTutorial");
        yield return AccessTools.DeclaredMethod(typeof(UIMultiplayerEscSubmenu), "ShowConfirmationBoxToEndSession");
        yield return AccessTools.DeclaredMethod(typeof(UIScenarioEscMenu), "QuitDungeon");
        yield return AccessTools.DeclaredMethod(typeof(UIScenarioEscMenu), "RestartScenario");
        yield return AccessTools.DeclaredMethod(typeof(UIScenarioEscMenu), "QueueRestartRound");
        yield return AccessTools.DeclaredMethod(typeof(UIScenarioEscMenu), "RestartTurn");
        yield return AccessTools.DeclaredMethod(typeof(UIScenarioEscMenu), "SkipMission");
        yield return AccessTools.DeclaredMethod(typeof(UIScenarioEscMenu), "SkipTutorial");
        yield return AccessTools.DeclaredMethod(typeof(UIShopItemInventory), "OnSelectedItemSell");
        yield return AccessTools.DeclaredMethod(typeof(UIShopItemInventory), "OnSelectedItemBuy");
        yield return AccessTools.DeclaredMethod(typeof(LevelEditorMainMenu), "OnPlaySelectedFilePressed");
        yield return AccessTools.DeclaredMethod(typeof(UICreateModView), "Confirm");
        yield return AccessTools.DeclaredMethod(typeof(UIExportModdingManager), "OnExportFailed");
        yield return AccessTools.DeclaredMethod(typeof(UIRulesetsWindow), "ChooseRulesetTypeToCreate");
        yield return AccessTools.DeclaredMethod(typeof(UIRulesetsWindow), "OnCloseButton");
        yield return AccessTools.DeclaredMethod(typeof(UIRulesetsWindow), "OnConfirmedRulesetChanges");
        yield return AccessTools.DeclaredMethod(typeof(UIRulesetsWindow), "OnValidationFailed");
        yield return AccessTools.DeclaredMethod(typeof(UIRulesetsWindow), "AskDeleteRuleset");
        foreach (MethodInfo method in typeof(PersistentAbilitiesUI).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (method.Name == "OnCancelActiveAbility") yield return method;
    }

    private static void Prefix(Component __instance, out Component? __state) =>
        __state = ConfirmationOnOwner.Begin(__instance);

    private static Exception? Finalizer(Exception? __exception, Component? __state)
    {
        ConfirmationOnOwner.CurrentScope = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class ConfirmationRequestOriginPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(typeof(UIConfirmationBoxManager), "ShowGenericConfirmation");
        yield return AccessTools.DeclaredMethod(typeof(UIConfirmationBoxManager), "ShowGenericWarningConfirmation");
        yield return AccessTools.DeclaredMethod(typeof(UIConfirmationBoxManager), "ShowGenericCancelConfirmation");
        yield return AccessTools.DeclaredMethod(typeof(UIConfirmationBoxManager), "ShowGenericSpendConfirmation");
        yield return AccessTools.DeclaredMethod(typeof(UIConfirmationBoxManager), "ShowCancelActiveAbility");
    }

    private static void Prefix(UIConfirmationBoxManager __instance) => ConfirmationOnOwner.Record(__instance);
}

// Item binding/equipment and shop confirmations use a different original native
// component, not the generic ConfirmationBox pool. Record its exact Show call too.
[HarmonyPatch(typeof(UIItemConfirmationBox), "ShowConfirmation")]
internal static class ItemConfirmationRequestOriginPatch
{
    private static void Prefix(UIItemConfirmationBox __instance) => ConfirmationOnOwner.Record(__instance);
}

/// <summary>
/// Original mod validation finishes after its initiating method has returned.
/// Scope those exact compiler-generated native callbacks at execution, using only
/// their captured original owner. Never carry an ambient scope across frames or
/// infer a source from the window which happens to be focused when work finishes.
/// </summary>
[HarmonyPatch]
internal static class DeferredConfirmationProducerScopePatch
{
    private static readonly Dictionary<Type, FieldInfo> Owners = new();

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (MethodBase method in Callbacks(typeof(UIRulesetsWindow), "CompileRuleset")) yield return method;
        foreach (MethodBase method in Callbacks(typeof(UIListModsView), "ValidateMod", "OpenLevelEditor")) yield return method;
        foreach (MethodBase method in Callbacks(typeof(UIRulesetView), "ValidateMod")) yield return method;
    }

    private static IEnumerable<MethodBase> Callbacks(Type owner, params string[] entries)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (Type closure in owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            FieldInfo? field = closure.GetField("<>4__this", flags);
            if (field == null || field.FieldType != owner) continue;
            foreach (MethodInfo method in closure.GetMethods(flags))
                for (int i = 0; i < entries.Length; i++)
                    if (method.Name.StartsWith("<" + entries[i] + ">b__", StringComparison.Ordinal))
                    {
                        Owners[closure] = field;
                        yield return method;
                        break;
                    }
        }
    }

    private static void Prefix(object __instance, out Component? __state)
    {
        __state = ConfirmationOnOwner.CurrentScope;
        ConfirmationOnOwner.CurrentScope = Owners.TryGetValue(__instance.GetType(), out FieldInfo? field)
            ? field.GetValue(__instance) as Component : null;
    }

    private static Exception? Finalizer(Exception? __exception, Component? __state)
    {
        ConfirmationOnOwner.CurrentScope = __state;
        return __exception;
    }
}

// The original improved-short-rest tooltip action captures actors, but not its UI
// component. Record the explicit originating UI when that native action is created;
// callbacks and intrinsic game permissions still execute only on the original click.
[HarmonyPatch(typeof(PersistentAbilitiesUI), "GetCustomEffects")]
internal static class PersistentConfirmationActionScopePatch
{
    private static void Postfix(PersistentAbilitiesUI __instance,
        List<Tuple<ReferenceToSprite, string, string, Action, bool>> __result)
    {
        for (int i = 0; i < __result.Count; i++)
        {
            var effect = __result[i];
            if (effect.Item4 is not Action original) continue;
            __result[i] = new Tuple<ReferenceToSprite, string, string, Action, bool>(
                effect.Item1, effect.Item2, effect.Item3, () =>
                {
                    Component? previous = ConfirmationOnOwner.Begin(__instance);
                    try { original(); }
                    finally { ConfirmationOnOwner.CurrentScope = previous; }
                }, effect.Item5);
        }
    }
}
