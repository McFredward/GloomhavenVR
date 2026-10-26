using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
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

    private static ConditionalWeakTable<ConfirmationBox, Request> Requests = new();
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
        if (box == null) return;
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
