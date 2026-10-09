using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

/// <summary>The Player owns Unity's native engine modules, including CoreModule.
/// Current desktop builds detour after the entire native pre-cull delegate list.
/// A recovered DLL export cannot replace that Player module. Its established
/// callers already fail open: restore native material/renderer leases and retain
/// the public paired camera callbacks for capture. Do not approximate this final
/// seam with an early delegate or omit the hook while claiming installation.
/// This adaptation touches only the locally generated Quest mod, never PC source.
/// </summary>
internal static class NativeCameraBoundary
{
    internal static void Apply(Discovery model, AuditReport report)
    {
        ModuleDefinition module = model.Mod.MainModule;
        TypeDefinition? boundary = module.GetType("GloomhavenVR.Core.ScenarioCameraCullBoundary");
        TypeDefinition? patch = module.GetType("GloomhavenVR.Core.Camera_FinalPreCull_BudgetPatch");
        if (boundary == null && patch == null) return;
        if (boundary == null || patch == null)
            throw new InvalidDataException("Native final pre-cull adapter requires its complete known boundary and patch.");
        CustomAttribute[] attributes = patch.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToArray();
        if (attributes.Length != 1 || attributes[0].ConstructorArguments.Count != 2
            || attributes[0].ConstructorArguments[0].Value is not TypeReference camera || camera.FullName != "UnityEngine.Camera"
            || attributes[0].ConstructorArguments[1].Value is not string name || name != "FireOnPreCull")
            throw new InvalidDataException("Native final pre-cull hook changed its engine target.");
        MethodDefinition postfix = patch.Methods.Single(m => m.Name == "Postfix");
        Instruction[] body = postfix.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToArray();
        if (!postfix.IsStatic || postfix.ReturnType.FullName != "System.Void" || postfix.Parameters.Count != 1
            || postfix.Parameters[0].ParameterType.FullName != "UnityEngine.Camera" || body.Length != 3
            || body[0].OpCode != OpCodes.Ldarg_0 || body[1].OpCode != OpCodes.Call || body[2].OpCode != OpCodes.Ret
            || body[1].Operand is not MethodReference after || after.DeclaringType.FullName != boundary.FullName
            || after.Name != "AfterNativePreCull" || after.ReturnType.FullName != "System.Void"
            || after.Parameters.Count != 1 || after.Parameters[0].ParameterType.FullName != "UnityEngine.Camera")
            throw new InvalidDataException("Native final pre-cull hook has unknown additional behavior.");
        MethodDefinition install = boundary.Methods.Single(m => m.Name == "Install");
        Instruction[] installation = install.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToArray();
        Code[] expected = { Code.Call, Code.Dup, Code.Brtrue_S, Code.Pop, Code.Ldstr, Code.Newobj, Code.Throw,
            Code.Stloc_0, Code.Ldsfld, Code.Ldloc_0, Code.Bne_Un_S, Code.Ret, Code.Ldloc_0, Code.Ldtoken,
            Code.Call, Code.Callvirt, Code.Ldloc_0, Code.Stsfld, Code.Ret };
        // Release C# compiles ReferenceEquals to a branch, rather than a method
        // call. Qualify the whole actual body, including same-owner/no-owner
        // guards, so an extra field write or instruction cannot be discarded.
        if (!install.IsStatic || install.ReturnType.FullName != "System.Void" || install.Parameters.Count != 0
            || install.Body.ExceptionHandlers.Count != 0 || install.Body.Variables.Count != 1
            || install.Body.Variables[0].VariableType.FullName != "HarmonyLib.Harmony"
            || !installation.Select(i => i.OpCode.Code).SequenceEqual(expected)
            || installation[0].Operand is not MethodReference owner || owner.FullName != "HarmonyLib.Harmony GloomhavenVR.Core.VRSession::get_Harmony()"
            || installation[2].Operand != installation[7] || installation[10].Operand != installation[12]
            || installation[4].Operand is not string message || message != "Native camera final pre-cull boundary requires the session Harmony owner."
            || installation[5].Operand is not MethodReference failure || failure.FullName != "System.Void System.InvalidOperationException::.ctor(System.String)"
            || installation[8].Operand is not FieldReference field || field.DeclaringType.FullName != boundary.FullName
            || field.Name != "_installedOwner" || field.FieldType.FullName != "HarmonyLib.Harmony"
            || installation[17].Operand is not FieldReference stored || stored.FullName != field.FullName
            || installation[13].Operand is not TypeReference target || target.FullName != patch.FullName
            || installation[14].Operand is not MethodReference type || type.FullName != "System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)"
            || installation[15].Operand is not MethodReference registration || registration.FullName != "System.Void HarmonyLib.Harmony::PatchAll(System.Type)")
            throw new InvalidDataException("Native final pre-cull installation is outside its reviewed fail-open contract.");

        ValidateConsumers(module, boundary);

        // Throw rather than return: EnvironmentBudget, WorldMaterialBudget and
        // FlatScreen must execute their existing restoration/fallback handlers.
        install.Body = new MethodBody(install);
        TypeReference exception = new("System", "NotSupportedException", module, module.TypeSystem.CoreLibrary);
        MethodReference constructor = new(".ctor", module.TypeSystem.Void, exception) { HasThis = true };
        constructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        ILProcessor il = install.Body.GetILProcessor();
        il.Emit(OpCodes.Ldstr, "Quest Player retains native rendering: the final Unity engine pre-cull detour is unavailable.");
        il.Emit(OpCodes.Newobj, constructor); il.Emit(OpCodes.Throw);
        patch.CustomAttributes.Remove(attributes[0]);
        report.StaticSubstitutions.Add("Native final pre-cull engine detour: existing native renderer/material/capture fail-open paths retained; Player CoreModule is not replaced.");
    }

    static void ValidateConsumers(ModuleDefinition module, TypeDefinition boundary)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["System.Void GloomhavenVR.Core.ScenarioEnvironmentBudget::Install(UnityEngine.GameObject)"] = "GloomhavenVR.Core.ScenarioEnvironmentBudget::StopAfterFailure",
            ["System.Void GloomhavenVR.Core.WorldMaterialBudget::Install(UnityEngine.GameObject)"] = "GloomhavenVR.Core.WorldMaterialBudget/Driver::Fail",
            ["System.Void GloomhavenVR.WorldUI.FlatScreen::SetCaptureRenderGuard(System.Boolean)"] = "GloomhavenVR.WorldUI.FlatScreen::OnCapturePreCull" };
        var calls = Discovery.AllTypes(module).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions.Where(i => i.Operand is MethodReference call
                && call.DeclaringType.FullName == boundary.FullName && call.Name == "Install").Select(i => (Method: m, Call: i))).ToArray();
        if (calls.Length != expected.Count || calls.Select(c => c.Method.FullName).Distinct().Count() != expected.Count)
            throw new InvalidDataException("Native final pre-cull consumers changed their reviewed restoration set.");
        foreach (var entry in calls)
        {
            if (!expected.TryGetValue(entry.Method.FullName, out string? restore))
                throw new InvalidDataException("Native final pre-cull has an unknown consumer: " + entry.Method.FullName);
            var instructions = entry.Method.Body.Instructions;
            ExceptionHandler? handler = entry.Method.Body.ExceptionHandlers.FirstOrDefault(h => h.HandlerType == ExceptionHandlerType.Catch
                && h.CatchType?.FullName == "System.Exception" && instructions.IndexOf(h.TryStart) <= instructions.IndexOf(entry.Call)
                && (h.TryEnd == null || instructions.IndexOf(entry.Call) < instructions.IndexOf(h.TryEnd)));
            Instruction[] fallback = handler == null ? Array.Empty<Instruction>() : instructions
                .Skip(instructions.IndexOf(handler.HandlerStart)).Take((handler.HandlerEnd == null ? instructions.Count
                    : instructions.IndexOf(handler.HandlerEnd)) - instructions.IndexOf(handler.HandlerStart)).ToArray();
            if (!fallback.Any(i => i.Operand is MethodReference call && call.DeclaringType.FullName + "::" + call.Name == restore)
                || fallback.Any(i => i.OpCode.Code is Code.Throw or Code.Rethrow))
                throw new InvalidDataException("Native final pre-cull consumer lost its caught restoration: " + entry.Method.FullName);
            if (restore.EndsWith("::OnCapturePreCull", StringComparison.Ordinal)
                && (!WritesCallback(fallback, "onPreCull") || !WritesCallback(instructions, "onPostRender")))
                throw new InvalidDataException("Native final pre-cull capture lost its paired public camera fallback.");
        }
        foreach (var specification in new[] {
            (Type: "GloomhavenVR.Core.ScenarioEnvironmentBudget", Method: "StopAfterFailure", Store: OpCodes.Stsfld),
            (Type: "GloomhavenVR.Core.WorldMaterialBudget/Driver", Method: "Fail", Store: OpCodes.Stfld) })
        {
            MethodDefinition failure = Discovery.AllTypes(module).Single(t => t.FullName == specification.Type).Methods.Single(m => m.Name == specification.Method);
            if (!failure.Body.Instructions.Any(i => i.Operand is MethodReference call
                    && call.Name == "RestoreAll" && call.DeclaringType.FullName == "GloomhavenVR.Core."
                        + (specification.Method == "Fail" ? "WorldMaterialBudget/Driver" : "ScenarioEnvironmentBudget/Driver")
                    && (specification.Method != "Fail" || i.Previous?.OpCode == OpCodes.Ldc_I4_1))
                || !failure.Body.Instructions.Any(i => i.OpCode == specification.Store && i.Previous?.OpCode == OpCodes.Ldc_I4_1
                    && i.Operand is FieldReference field && field.DeclaringType.FullName == specification.Type && field.Name == "_failed")
                || specification.Method != "Fail" && !failure.Body.Instructions.Any(i => i.Previous?.OpCode == OpCodes.Ldc_I4_0
                    && i.Operand is MethodReference call && call.FullName == "System.Void UnityEngine.Behaviour::set_enabled(System.Boolean)"))
                throw new InvalidDataException("Native final pre-cull restoration no longer retires and restores its failed budget: " + specification.Type);
        }
    }

    static bool WritesCallback(IEnumerable<Instruction> instructions, string name)
        => instructions.Any(i => i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference field
            && field.DeclaringType.FullName == "UnityEngine.Camera" && field.Name == name)
           && instructions.Any(i => i.Operand is MethodReference call && call.DeclaringType.FullName == "System.Delegate" && call.Name == "Combine");
}
