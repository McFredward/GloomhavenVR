using System;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Observe native requests without changing their model, animation or refcount.</summary>
[HarmonyPatch(typeof(Character3DDisplayManager), nameof(Character3DDisplayManager.Display),
    typeof(Component), typeof(ECharacter), typeof(string), typeof(string))]
internal static class MenuDepthCharacterDisplayObserver
{
    private static bool _reported;

    [HarmonyPostfix]
    private static void AfterDisplay(Character3DDisplayManager __instance, Component request)
    {
        try { MenuDepthCharacters.ObserveDisplay(__instance, request); }
        catch (Exception exception)
        {
            if (_reported) return;
            _reported = true;
            VRLog.Warn("WorldUI", "MENU DEPTH CHARACTER discovery failed after native display; bounded discovery will retry: "
                + exception.GetType().Name + ".");
        }
    }
}
