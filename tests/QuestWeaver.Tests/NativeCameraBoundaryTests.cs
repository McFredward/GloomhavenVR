using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class NativeCameraBoundaryTests
{
    const string BoundaryName = "GloomhavenVR.Core.ScenarioCameraCullBoundary";
    const string PatchName = "GloomhavenVR.Core.Camera_FinalPreCull_BudgetPatch";

    internal static void Run(string root, string modPath, string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        string managed = Path.Combine(root, "ressources/GH_Data/Managed");
        byte[] inputHash = SHA256.HashData(File.ReadAllBytes(modPath));
        using (Discovery model = Discovery.Load(modPath, managed))
        {
            ModuleDefinition module = model.Mod.MainModule;
            MethodDefinition install = Method(module, BoundaryName, "Install");
            Dictionary<string, MethodDefinition> originalMethods = Discovery.AllTypes(module).SelectMany(t => t.Methods)
                .Where(m => m.HasBody && m != install).ToDictionary(m => m.FullName);
            Dictionary<string, string[]> untouched = originalMethods.ToDictionary(pair => pair.Key, pair => Body(pair.Value));
            TypeDefinition patch = module.GetType(PatchName);
            check(patch.CustomAttributes.Count(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch") == 1,
                "The actual input does not contain the reviewed engine detour.");
            var report = new AuditReport();
            NativeCameraBoundary.Apply(model, report);
            check(report.StaticSubstitutions.Count == 1, "Known engine boundary adaptation was not reported.");
            check(!patch.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"),
                "The Player-owned CoreModule patch remains discoverable.");
            check(untouched.All(pair => pair.Value.SequenceEqual(Body(originalMethods[pair.Key]))),
                "Engine fallback changed another original mod method.");
            check(install.Body.Instructions.Select(i => i.OpCode).SequenceEqual(new[] { OpCodes.Ldstr, OpCodes.Newobj, OpCodes.Throw }),
                "Unavailable engine installation returned success or retained a desktop detour.");
            check(install.Body.Instructions[1].Operand is MethodReference ctor
                && ctor.DeclaringType.FullName == "System.NotSupportedException"
                && ctor.DeclaringType.Scope.Name == module.TypeSystem.CoreLibrary.Name,
                "Unavailable engine installation imported a host framework exception.");
            Exception? unavailable = ExecuteInstall(install, Path.Combine(output, "unavailable.dll"));
            RequireUnavailable(unavailable);
            check(unavailable is NotSupportedException && unavailable.Message.Contains("native rendering", StringComparison.Ordinal),
                "Executing the actual adapted body did not enter consumers' failure handlers.");

            // A no-op would claim a working final seam and let renderer/material
            // leases run without their required callback. This causal negative
            // distinguishes explicit unavailability from silently removing a hook.
            install.Body = new Mono.Cecil.Cil.MethodBody(install);
            install.Body.GetILProcessor().Emit(OpCodes.Ret);
            try
            {
                RequireUnavailable(ExecuteInstall(install, Path.Combine(output, "silent-success.dll")));
                check(false, "A silent-success engine installation passed the executable failure contract.");
            }
            catch (InvalidDataException)
            { check(true, "A silent-success installation fails the executable failure contract."); }
        }

        using (Discovery model = Discovery.Load(modPath, managed))
        {
            CheckConsumerRestoration(model.Mod.MainModule, check);
            AuditReport report = model.Audit();
            check(report.Issues.Count == 0, "Actual Release mod was blocked: " + string.Join("; ", report.Issues));
            check(!report.Hooks.Any(h => h.Target?.Contains("UnityEngine.Camera::FireOnPreCull", StringComparison.Ordinal) == true),
                "Engine pre-cull hook survived AOT discovery.");
            check(!model.Hooks.Any(h => EngineModule(h.Target.Module.Assembly.Name.Name)),
                "Another engine-owned target survived AOT discovery.");
            check(!model.Loaded.Any(a => EngineModule(a.Name.Name)),
                "An engine-owned module would be emitted as a recovered assembly.");
            check(model.Hooks.Select(h => h.Target.FullName).Distinct().Count() >= 290,
                "Removing one engine seam discarded unrelated current mod targets.");
            check(report.Hooks.Any(h => h.Patch.Contains("ProceduralBase_Placed_EnvironmentBudgetPatch", StringComparison.Ordinal))
                && report.Hooks.Any(h => h.Patch.Contains("MaterialLoaderData_Ready_EnvironmentBudgetPatch", StringComparison.Ordinal)),
                "Original game callbacks needed by the retained native fallback were dropped.");
        }
        check(inputHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(modPath))), "The input PC mod was modified.");

        Reject("changed engine target", module => {
            CustomAttribute attribute = module.GetType(PatchName).CustomAttributes.Single(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
            attribute.ConstructorArguments[1] = new CustomAttributeArgument(module.TypeSystem.String, "FireOnPostRender");
        });
        Reject("changed postfix signature", module => Method(module, PatchName, "Postfix").Parameters[0].ParameterType = module.TypeSystem.Object);
        Reject("additional postfix side effect", module => {
            MethodDefinition method = Method(module, PatchName, "Postfix");
            method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[0], Instruction.Create(OpCodes.Ldc_I4_1));
            method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[1], Instruction.Create(OpCodes.Pop));
        });
        Reject("additional install side effect", module => {
            MethodDefinition method = Method(module, BoundaryName, "Install");
            var unknown = new MethodReference("UnknownSideEffect", module.TypeSystem.Void, method.DeclaringType);
            method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[0], Instruction.Create(OpCodes.Call, unknown));
        });
        Reject("removed same-owner guard", module => {
            MethodDefinition method = Method(module, BoundaryName, "Install");
            method.Body.Instructions.Single(i => i.OpCode.Code is Code.Bne_Un or Code.Bne_Un_S).OpCode = OpCodes.Br;
        });
        foreach (string consumer in new[] { "GloomhavenVR.Core.ScenarioEnvironmentBudget", "GloomhavenVR.Core.WorldMaterialBudget", "GloomhavenVR.WorldUI.FlatScreen" })
            Reject("missing failure guard in " + consumer, module => Method(module, consumer,
                consumer.EndsWith("FlatScreen", StringComparison.Ordinal) ? "SetCaptureRenderGuard" : "Install").Body.ExceptionHandlers.Clear());
        Reject("unknown engine-boundary consumer", module => {
            TypeDefinition boundary = module.GetType(BoundaryName);
            var method = new MethodDefinition("UnreviewedInstallConsumer", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
            boundary.Methods.Add(method);
            method.Body.GetILProcessor().Emit(OpCodes.Call, Method(module, BoundaryName, "Install"));
            method.Body.GetILProcessor().Emit(OpCodes.Ret);
        });
        Reject("removed environment restoration", module => {
            MethodDefinition stop = Method(module, "GloomhavenVR.Core.ScenarioEnvironmentBudget", "StopAfterFailure");
            Instruction restore = stop.Body.Instructions.Single(i => i.Operand is MethodReference call && call.Name == "RestoreAll");
            var original = (MethodReference)restore.Operand;
            restore.Operand = new MethodReference("UnreviewedCleanup", original.ReturnType, original.DeclaringType) { HasThis = original.HasThis };
        });
        Reject("environment driver left enabled", module => {
            MethodDefinition stop = Method(module, "GloomhavenVR.Core.ScenarioEnvironmentBudget", "StopAfterFailure");
            stop.Body.Instructions.Single(i => i.Operand is MethodReference call && call.Name == "set_enabled").Previous.OpCode = OpCodes.Ldc_I4_1;
        });
        Reject("world restoration retaining optional variants", module => {
            MethodDefinition fail = Method(module, "GloomhavenVR.Core.WorldMaterialBudget/Driver", "Fail");
            fail.Body.Instructions.Single(i => i.Operand is MethodReference call && call.Name == "RestoreAll").Previous.OpCode = OpCodes.Ldc_I4_0;
        });
        Reject("incomplete boundary pair", module => module.Types.Remove(module.GetType(PatchName)));
        UnknownEngineHook();

        void Reject(string label, Action<ModuleDefinition> alter)
        {
            using Discovery model = Discovery.Load(modPath, managed);
            alter(model.Mod.MainModule);
            try { NativeCameraBoundary.Apply(model, new AuditReport()); check(false, "Unknown " + label + " was silently accepted."); }
            catch (InvalidDataException) { check(true, "Unknown " + label + " was rejected."); }
        }

        void UnknownEngineHook()
        {
            using Discovery model = Discovery.Load(modPath, managed);
            ModuleDefinition module = model.Mod.MainModule;
            var unknown = new TypeDefinition("Fixture", "UnknownEnginePatch", Mono.Cecil.TypeAttributes.Public
                | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
            module.Types.Add(unknown);
            CustomAttribute original = module.GetType(PatchName).CustomAttributes.Single(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
            var attribute = new CustomAttribute(original.Constructor);
            foreach (CustomAttributeArgument argument in original.ConstructorArguments) attribute.ConstructorArguments.Add(argument);
            unknown.CustomAttributes.Add(attribute);
            var postfix = new MethodDefinition("Postfix", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
            postfix.Parameters.Add(new ParameterDefinition("cam", Mono.Cecil.ParameterAttributes.None,
                Method(module, PatchName, "Postfix").Parameters[0].ParameterType));
            postfix.Body.GetILProcessor().Emit(OpCodes.Ret); unknown.Methods.Add(postfix);
            AuditReport report = model.Audit();
            check(report.Issues.Any(i => i.Code == "HOOK_UNSUPPORTED" && i.Subject.Contains("UnknownEnginePatch", StringComparison.Ordinal)
                && i.Detail.Contains("Player owns", StringComparison.Ordinal)),
                "A new unknown engine-owned hook would be silently woven into CoreModule.");
            check(!report.Complete, "Unknown engine-owned hook did not close the build gate.");
        }
    }

    static void CheckConsumerRestoration(ModuleDefinition module, Action<bool, string> check)
    {
        MethodDefinition environment = Method(module, "GloomhavenVR.Core.ScenarioEnvironmentBudget", "Install");
        MethodDefinition stop = Method(module, "GloomhavenVR.Core.ScenarioEnvironmentBudget", "StopAfterFailure");
        check(GuardedFailure(environment, "StopAfterFailure"), "Environment initialization no longer catches unavailable final culling.");
        check(Calls(stop, "RestoreAll") && stop.Body.Instructions.Any(i => i.OpCode == OpCodes.Stsfld
            && i.Operand is FieldReference f && f.Name == "_failed") && Calls(stop, "set_enabled"),
            "Environment failure no longer stops optional work and restores native surfaces.");
        MethodDefinition world = Method(module, "GloomhavenVR.Core.WorldMaterialBudget", "Install");
        MethodDefinition fail = Method(module, "GloomhavenVR.Core.WorldMaterialBudget/Driver", "Fail");
        check(GuardedFailure(world, "Fail"), "World material initialization no longer catches unavailable final culling.");
        check(Calls(fail, "RestoreAll") && fail.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld
            && i.Operand is FieldReference f && f.Name == "_failed"),
            "World material failure no longer restores native materials and stops optional work.");
        MethodDefinition capture = Method(module, "GloomhavenVR.WorldUI.FlatScreen", "SetCaptureRenderGuard");
        check(GuardedFailure(capture, "OnCapturePreCull") && WritesCallback(capture, "onPreCull"), "Capture unavailable seam no longer retains the ordinary camera callback.");
        check(WritesCallback(capture, "onPostRender") && Calls(capture, "add_beginCameraRendering") && Calls(capture, "add_endCameraRendering"),
            "Capture fallback no longer retains paired restoration callbacks.");
        MethodDefinition terrain = Method(module, "GloomhavenVR.Core.ScenarioTerrainBudget", "Install");
        MethodDefinition terrainCallbacks = Method(module, "GloomhavenVR.Core.ScenarioTerrainBudget/Driver", "Awake");
        check(!Calls(terrain, "Install", BoundaryName) && WritesCallback(terrainCallbacks, "onPreCull") && WritesCallback(terrainCallbacks, "onPostRender"),
            "Independent terrain rendering became dependent on the unavailable engine detour.");
    }

    static bool GuardedFailure(MethodDefinition method, string failureCall)
    {
        Instruction? install = method.Body.Instructions.FirstOrDefault(i => i.Operand is MethodReference call
            && call.DeclaringType.FullName == BoundaryName && call.Name == "Install");
        return install != null && method.Body.ExceptionHandlers.Any(handler => handler.HandlerType == ExceptionHandlerType.Catch
            && handler.CatchType?.FullName == "System.Exception" && InRange(method, install, handler.TryStart, handler.TryEnd)
            && method.Body.Instructions.Any(i => InRange(method, i, handler.HandlerStart, handler.HandlerEnd)
                && i.Operand is MethodReference call && call.Name == failureCall));
    }

    static bool InRange(MethodDefinition method, Instruction instruction, Instruction start, Instruction? end)
    {
        var instructions = method.Body.Instructions;
        int at = instructions.IndexOf(instruction);
        return at >= instructions.IndexOf(start) && (end == null || at < instructions.IndexOf(end));
    }

    static Exception? ExecuteInstall(MethodDefinition adapted, string path)
    {
        using AssemblyDefinition fixture = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("NativeBoundaryProof" + Guid.NewGuid().ToString("N"), new Version(1, 0)), "proof", ModuleKind.Dll);
        ModuleDefinition module = fixture.MainModule;
        var type = new TypeDefinition("Fixture", "Boundary", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract
            | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(type);
        var install = new MethodDefinition("Install", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
        type.Methods.Add(install);
        ILProcessor il = install.Body.GetILProcessor();
        foreach (Instruction instruction in adapted.Body.Instructions)
        {
            if (instruction.OpCode == OpCodes.Ldstr) il.Emit(OpCodes.Ldstr, (string)instruction.Operand);
            else if (instruction.OpCode == OpCodes.Newobj && instruction.Operand is MethodReference ctor
                && ctor.DeclaringType.FullName == "System.NotSupportedException")
                il.Emit(OpCodes.Newobj, module.ImportReference(typeof(NotSupportedException).GetConstructor(new[] { typeof(string) })!));
            else if (instruction.OpCode == OpCodes.Throw || instruction.OpCode == OpCodes.Ret) il.Emit(instruction.OpCode);
            else throw new InvalidDataException("Executable adaptation proof encountered an unreviewed operation: " + instruction);
        }
        fixture.Write(path);
        var context = new AssemblyLoadContext("native-boundary-proof", isCollectible: true);
        try
        {
            try { context.LoadFromAssemblyPath(path).GetType("Fixture.Boundary")!.GetMethod("Install")!.Invoke(null, null); return null; }
            catch (TargetInvocationException error) { return error.InnerException; }
        }
        finally { context.Unload(); File.Delete(path); }
    }

    static bool WritesCallback(MethodDefinition method, string name) => method.Body.Instructions.Any(i => i.OpCode == OpCodes.Stsfld
        && i.Operand is FieldReference field && field.DeclaringType.FullName == "UnityEngine.Camera" && field.Name == name);

    static void RequireUnavailable(Exception? result)
    {
        if (result is not NotSupportedException)
            throw new InvalidDataException("Engine installation must explicitly enter the native-rendering failure handlers.");
    }

    static bool Calls(MethodDefinition method, string name, string? type = null) => method.Body.Instructions.Any(i => i.Operand is MethodReference call
        && call.Name == name && (type == null || call.DeclaringType.FullName == type));
    static MethodDefinition Method(ModuleDefinition module, string type, string method) => Discovery.AllTypes(module).Single(t => t.FullName == type).Methods.Single(m => m.Name == method);
    static string[] Body(MethodDefinition method) => method.Body.Instructions.Select(i => i.ToString()).ToArray();
    static bool EngineModule(string name) => name.StartsWith("UnityEngine.", StringComparison.Ordinal) && name.EndsWith("Module", StringComparison.Ordinal);
}
