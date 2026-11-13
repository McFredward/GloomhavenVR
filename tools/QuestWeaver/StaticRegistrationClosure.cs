using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

/// <summary>Do not certify a complete conversion which will reject an exact live
/// PatchAll(typeof(...)) registration during original module initialization.
/// Other Type producers retain the existing runtime closure guard; their value
/// must never be guessed by executing the mod on the build host.</summary>
internal static class StaticRegistrationClosure
{
    internal static void Audit(Discovery model, AuditReport report)
    {
        foreach (MethodDefinition caller in Discovery.AllTypes(model.Mod.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody))
        foreach (Instruction site in caller.Body.Instructions)
        {
            if (site.Operand is not MethodReference call || call.DeclaringType.FullName != "HarmonyLib.Harmony"
                || call.Name != "PatchAll" || call.Parameters.Count != 1 || call.Parameters[0].ParameterType.FullName != "System.Type") continue;
            Instruction? conversion = Previous(site), token = conversion == null ? null : Previous(conversion);
            if (conversion?.OpCode != OpCodes.Call || conversion.Operand is not MethodReference fromHandle
                || fromHandle.FullName != "System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)"
                || token?.OpCode != OpCodes.Ldtoken || token.Operand is not TypeReference reference) continue;
            TypeDefinition? patch = model.Mod.MainModule.GetType(reference.FullName);
            if (patch != null && (model.Hooks.Any(h => h.Patch.DeclaringType == patch)
                || model.StaticPatches.Any(p => p.PatchClass == patch) || AlwaysDeclines(patch))) continue;
            report.Issues.Add(new IntegrationIssue("PATCH_CLASS_REGISTRATION_UNRESOLVED", caller.FullName,
                "Live PatchAll class has no compiled hook or qualified static substitution: " + reference.FullName));
        }
    }

    private static Instruction? Previous(Instruction instruction)
    {
        Instruction? previous = instruction.Previous;
        while (previous?.OpCode == OpCodes.Nop) previous = previous.Previous;
        return previous;
    }

    private static bool AlwaysDeclines(TypeDefinition patch)
    {
        MethodDefinition? prepare = patch.Methods.SingleOrDefault(m => m.Name == "Prepare" && m.IsStatic
            && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Boolean");
        if (prepare == null || !prepare.HasBody || prepare.Body.ExceptionHandlers.Count != 0) return false;
        Instruction[] body = prepare.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToArray();
        return body.Length == 2 && body[1].OpCode == OpCodes.Ret
            && (body[0].OpCode == OpCodes.Ldc_I4_0 || body[0].OpCode.Code is Code.Ldc_I4 or Code.Ldc_I4_S && Convert.ToInt32(body[0].Operand) == 0);
    }
}
