using System;
using FFSNet;
using HarmonyLib;

namespace GloomhavenVR.WorldUI;

/// <summary>One explicit host-authorized test action inside the original ordered gameplay queue.</summary>
[HarmonyPatch(typeof(GameAction), nameof(GameAction.Execute))]
internal static class MultiplayerCheatActionPatch
{
    private static bool Prefix(GameAction __instance, ref bool ___isValid, ref bool __result)
    {
        if (__instance.ActionTypeID != MultiplayerCheats.NativeAction
            || Enum.IsDefined(typeof(GameActionType), MultiplayerCheats.NativeAction)) return true;
        __result = true; // Retire even a refused command; never hold the native queue.
        if (!MultiplayerCheats.Execute(__instance)) ___isValid = false;
        return false;
    }
}
