using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GloomhavenVR.Core;
using HarmonyLib;
using InControl;
using UnityEngine.EventSystems;

namespace GloomhavenVR.Hands.Interact;

/// <summary>Label only the original current mouse module's actual event data.
/// It still creates/resets that data, reads processed input, calculates deltas,
/// runs every raycaster and chooses its original result itself.</summary>
internal static class NativeMouseRaycastCoordinates
{
    internal static void Raycast(EventSystem system, PointerEventData data, List<RaycastResult> results,
        PointerInputModuleExtended module)
    {
        if (VRSession.IsRunning && system != null && module != null && module.isActiveAndEnabled
            && ReferenceEquals(EventSystem.current, system) && ReferenceEquals(system.currentInputModule, module)
            && ReferenceEquals(module.GetComponent<EventSystem>(), system)
            && WorldUI.VirtualMouse.OwnsProcessedPosition(data.position))
        {
            using (UguiRaycastCoordinates.EnterCaptured(data))
                system.RaycastAll(data, results);
            return;
        }
        system!.RaycastAll(data, results);
    }
}

[HarmonyPatch(typeof(PointerInputModuleExtended), "GetMousePointerEventData", new[] { typeof(int) })]
internal static class PointerInputModuleExtended_CapturedCoordinates
{
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        MethodInfo original = AccessTools.Method(typeof(EventSystem), nameof(EventSystem.RaycastAll), new[] { typeof(PointerEventData), typeof(List<RaycastResult>) });
        int count = 0; bool unsupportedBoundary = false;
        for (int i = 0; i < code.Count; i++)
            if (code[i].Calls(original))
            {
                count++;
                unsupportedBoundary |= code[i].blocks.Count != 0 || i > 0 && code[i - 1].opcode.OpCodeType == OpCodeType.Prefix;
            }
        if (count != 1 || unsupportedBoundary)
        {
            VRLog.Warn("Interact", "CAPTURED MOUSE COORDINATES: original mouse raycast seam changed; keeping original input.");
            return code;
        }
        MethodInfo replacement = AccessTools.Method(typeof(NativeMouseRaycastCoordinates), nameof(NativeMouseRaycastCoordinates.Raycast));
        var result = new List<CodeInstruction>(code.Count + 1);
        foreach (CodeInstruction instruction in code)
        {
            if (!instruction.Calls(original)) { result.Add(instruction); continue; }
            var owner = new CodeInstruction(OpCodes.Ldarg_0); owner.labels.AddRange(instruction.labels);
            result.Add(owner); result.Add(new CodeInstruction(OpCodes.Call, replacement));
        }
        return result;
    }
}
