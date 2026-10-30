using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Capture the owner/context when native deferred work is constructed,
/// before navigation can reuse that component. Completion-time focus and a fresh
/// snapshot of the already changed owner cannot establish its original request.</summary>
[HarmonyPatch]
internal static class ConfirmationDeferredCapturePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var entry in new[] { ("UIRulesetsWindow", new[] { "CompileRuleset" }),
            ("UIListModsView", new[] { "ValidateMod", "OpenLevelEditor" }),
            ("UIRulesetView", new[] { "ValidateMod" }) })
            foreach (MethodBase method in ConfirmationNavigationCatalog.Methods(entry.Item1, entry.Item2)) yield return method;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Stfld && instruction.operand is FieldInfo field
                && field.Name == "<>4__this" && typeof(Component).IsAssignableFrom(field.FieldType))
            {
                LocalBuilder owner = generator.DeclareLocal(field.FieldType);
                LocalBuilder callback = generator.DeclareLocal(field.DeclaringType!);
                var first = new CodeInstruction(OpCodes.Stloc, owner);
                first.labels.AddRange(instruction.labels); instruction.labels.Clear();
                first.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                yield return first;
                yield return new CodeInstruction(OpCodes.Stloc, callback);
                yield return new CodeInstruction(OpCodes.Ldloc, callback);
                yield return new CodeInstruction(OpCodes.Ldloc, owner);
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Ldloc, callback);
                yield return new CodeInstruction(OpCodes.Ldloc, owner);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ConfirmationOnOwner), "CaptureDeferredOrigin"));
            }
            else yield return instruction;
        }
    }
}

[HarmonyPatch]
internal static class ConfirmationDeferredCoroutinePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => ConfirmationNavigationCatalog.Methods(
        "GLOOM.MainMenu.UICreateGameDLCStep", "SkipAFrameBeforeMessage");
    private static void Postfix(Component __instance, ref IEnumerator __result) =>
        __result = ConfirmationOnOwner.ScopeDeferredCoroutine(__instance, __result);
}
