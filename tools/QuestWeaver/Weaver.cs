using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver.Runtime;

namespace QuestWeaver;

internal sealed class Weaver
{
    private readonly Discovery model;
    internal Weaver(Discovery model) { this.model = model; }
    public void Write(string output, AuditReport report, bool diagnostic)
    {
        string absolute = Path.GetFullPath(output);
        if (absolute == model.ManagedPath || absolute.StartsWith(model.ManagedPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || absolute == Path.GetDirectoryName(model.ModPath)) throw new ArgumentException("Output must not modify an input assembly directory.");
        if (Directory.Exists(absolute) && Directory.EnumerateFileSystemEntries(absolute).Any()) throw new ArgumentException("Output directory must be empty.");
        var allHooks = model.Hooks.ToArray();
        var protectedSnapshots = model.Loaded.Distinct().ToDictionary(a => a, ProtectedTypes.Snapshot);
        var slots = allHooks.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);
        foreach (TypeDefinition type in Discovery.AllTypes(model.Mod.MainModule))
        {
            if (type.Name == "<Module>") continue;
            if (type.IsNested) type.IsNestedPublic = true; else type.IsPublic = true;
        }
        foreach (HookDefinition hook in allHooks) hook.Patch.IsPublic = true;
        foreach (IGrouping<MethodDefinition, HookDefinition> group in allHooks.GroupBy(h => h.Target))
            Wrap(group.Key, group.ToArray(), slots);
        foreach (FieldHelper helper in model.Helpers) ReplaceHelper(helper);
        RewriteRegistrations();
        string manifest = string.Join("\n", allHooks.Select(h => h.Patch.DeclaringType.FullName + "|" + Discovery.MethodKey(h.Target)
            + "|" + Discovery.MethodKey(h.Patch) + "|" + h.Kind + "|" + h.Priority + "|" + slots[h])) + "\n";
        model.Mod.MainModule.Resources.Add(new EmbeddedResource("QuestWeaver.Hooks.v1", ManifestResourceAttributes.Private, Encoding.UTF8.GetBytes(manifest)));
        // Preserve reflection metadata and generic closure conservatively for the initial port.
        // This does not assert that every dynamic serializer has generated native code.
        string links = "<linker>\n" + string.Join("\n", model.Loaded.Distinct().Select(a => "  <assembly fullname=\"" + a.Name.Name + "\" preserve=\"all\" />")) + "\n  <assembly fullname=\"QuestWeaver.Runtime\" preserve=\"all\" />\n</linker>\n";
        string parent = Path.GetDirectoryName(absolute)!;
        Directory.CreateDirectory(parent);
        string scratch = Path.Combine(parent, ".quest-weaver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            foreach (AssemblyDefinition assembly in model.Loaded.Distinct())
            {
                string destination = Path.Combine(scratch, assembly.Name.Name + ".dll");
                // Only mutated input assemblies are emitted. Unity owns its CoreModule; that
                // module's scene methods are not current targets and cannot be replaced.
                assembly.Write(destination);
                using AssemblyDefinition reread = AssemblyDefinition.ReadAssembly(destination);
                if (reread.Name.Name != assembly.Name.Name) throw new InvalidDataException("Assembly identity changed during serialization.");
                report.ProtectedTypesVerified += ProtectedTypes.Verify(protectedSnapshots[assembly], reread);
            }
            File.Copy(typeof(Registry).Assembly.Location, Path.Combine(scratch, "QuestWeaver.Runtime.dll"));
            HarmonyFacade.Write(Path.Combine(scratch, "0Harmony.dll"), model.Mod.MainModule.AssemblyReferences.FirstOrDefault(a => a.Name == "0Harmony"));
            File.WriteAllText(Path.Combine(scratch, "link.xml"), links);
            if (diagnostic) File.WriteAllText(Path.Combine(scratch, "DIAGNOSTIC-INCOMPLETE.txt"), "This subset is not a complete Quest game/mod conversion. Inspect the audit report.\n");
            if (Directory.Exists(absolute)) Directory.Delete(absolute);
            Directory.Move(scratch, absolute);
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
        report.WovenTargets = allHooks.Select(h => h.Target).Distinct().Count();
        report.Complete = !diagnostic && report.Issues.Count == 0;
    }

    private void RewriteRegistrations()
    {
        foreach (MethodDefinition method in Discovery.AllTypes(model.Mod.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (Instruction i in method.Body.Instructions)
                if (i.Operand is MethodReference call && call.DeclaringType.FullName == "HarmonyLib.Harmony")
                {
                    string? replacement = call.Name switch
                    {
                        "PatchAll" when call.Parameters.Count == 1 && call.Parameters[0].ParameterType.FullName == "System.Type" => nameof(Registry.PatchAll),
                        "Patch" when call.Parameters.Count == 6 => nameof(Registry.Patch) ,
                        "UnpatchSelf" when call.Parameters.Count == 0 => nameof(Registry.UnpatchSelf),
                        _ => null
                    };
                    if (replacement == null) continue;
                    i.OpCode = OpCodes.Call;
                    i.Operand = ImportRegistry(method.Module, replacement);
                }
    }

    private static void ReplaceHelper(FieldHelper helper)
    {
        ModuleDefinition module = helper.Field.Module;
        string getterName = "__QuestFieldRef_" + helper.Field.Name;
        MethodDefinition? getter = helper.Field.DeclaringType.Methods.FirstOrDefault(m => m.Name == getterName && m.Parameters.Count == 1
            && m.Parameters[0].ParameterType.FullName == helper.Factory.GenericArguments[0].FullName);
        if (getter == null)
        {
            getter = new MethodDefinition(getterName, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                new ByReferenceType(module.ImportReference(helper.Factory.GenericArguments[1])));
            getter.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, module.ImportReference(helper.Factory.GenericArguments[0])));
            helper.Field.DeclaringType.Methods.Add(getter);
            ILProcessor gi = getter.Body.GetILProcessor();
            if (!helper.Field.IsStatic) gi.Emit(OpCodes.Ldarg_0);
            gi.Emit(helper.Field.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, helper.Field);
            gi.Emit(OpCodes.Ret);
        }
        ModuleDefinition callerModule = helper.Caller.Module;
        var delegateType = new GenericInstanceType(callerModule.ImportReference(((GenericInstanceType)helper.Factory.ReturnType).ElementType));
        foreach (TypeReference a in helper.Factory.GenericArguments) delegateType.GenericArguments.Add(callerModule.ImportReference(a));
        var ctor = new MethodReference(".ctor", callerModule.TypeSystem.Void, delegateType) { HasThis = true };
        ctor.Parameters.Add(new ParameterDefinition(callerModule.TypeSystem.Object));
        ctor.Parameters.Add(new ParameterDefinition(callerModule.TypeSystem.IntPtr));
        helper.Name.OpCode = OpCodes.Nop; helper.Name.Operand = null;
        helper.Call.OpCode = OpCodes.Ldnull; helper.Call.Operand = null;
        ILProcessor ci = helper.Caller.Body.GetILProcessor();
        Instruction function = Instruction.Create(OpCodes.Ldftn, callerModule.ImportReference(getter));
        ci.InsertAfter(helper.Call, function);
        ci.InsertAfter(function, Instruction.Create(OpCodes.Newobj, ctor));
    }

    private static void Wrap(MethodDefinition target, HookDefinition[] hooks, Dictionary<HookDefinition, int> slots)
    {
        ModuleDefinition module = target.Module;
        var clone = new MethodDefinition("__QuestOriginal_" + target.MetadataToken.ToInt32().ToString("x8"),
            MethodAttributes.Private | MethodAttributes.HideBySig | (target.IsStatic ? MethodAttributes.Static : 0), module.ImportReference(target.ReturnType));
        foreach (ParameterDefinition p in target.Parameters)
            clone.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, module.ImportReference(p.ParameterType)));
        target.DeclaringType.Methods.Add(clone);
        var oldBody = target.Body;
        clone.Body = new MethodBody(clone) { InitLocals = oldBody.InitLocals, MaxStackSize = oldBody.MaxStackSize };
        foreach (VariableDefinition v in oldBody.Variables) clone.Body.Variables.Add(v);
        foreach (Instruction i in oldBody.Instructions)
        {
            if (i.Operand is ParameterDefinition p) i.Operand = clone.Parameters[p.Index];
            clone.Body.Instructions.Add(i);
        }
        foreach (ExceptionHandler eh in oldBody.ExceptionHandlers) clone.Body.ExceptionHandlers.Add(eh);
        target.Body = new MethodBody(target) { InitLocals = true, MaxStackSize = Math.Max(16, target.Parameters.Count + 8) };
        ILProcessor il = target.Body.GetILProcessor();
        VariableDefinition? result = target.ReturnType.MetadataType == MetadataType.Void ? null : Local(target.ReturnType);
        VariableDefinition run = Local(module.TypeSystem.Boolean);
        VariableDefinition finalized = Local(module.TypeSystem.Boolean);
        TypeReference exceptionType = CoreType(module, "System", "Exception");
        VariableDefinition exception = Local(exceptionType);
        var states = new Dictionary<TypeDefinition, VariableDefinition>();
        foreach (HookDefinition hook in hooks)
            foreach (ParameterDefinition p in hook.Patch.Parameters.Where(p => p.Name == "__state"))
            {
                TypeReference type = p.ParameterType is ByReferenceType br ? br.ElementType : p.ParameterType;
                if (states.TryGetValue(hook.Patch.DeclaringType, out VariableDefinition? prior) && prior.VariableType.FullName != type.FullName)
                    throw new InvalidDataException("Patch class has inconsistent __state types: " + hook.Patch.DeclaringType.FullName);
                if (prior == null) states.Add(hook.Patch.DeclaringType, Local(module.ImportReference(type)));
            }
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, run);
        var ordered = new Dictionary<string, VariableDefinition>();
        foreach (string kind in new[] { "prefix", "postfix", "finalizer" })
        {
            VariableDefinition array = Local(new ArrayType(module.TypeSystem.Int32));
            ordered.Add(kind, array);
            il.Emit(OpCodes.Ldstr, Discovery.MethodKey(target) + "|" + kind);
            il.Emit(OpCodes.Call, ImportRegistry(module, nameof(Registry.Ordered))); il.Emit(OpCodes.Stloc, array);
        }
        Instruction tryStart = Instruction.Create(OpCodes.Nop), catchStart = Instruction.Create(OpCodes.Stloc, exception), afterCatch = Instruction.Create(OpCodes.Nop);
        il.Append(tryStart);
        EmitHooks("prefix");
        Instruction skipOriginal = Instruction.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldloc, run); il.Emit(OpCodes.Brfalse, skipOriginal);
        if (!target.IsStatic) il.Emit(OpCodes.Ldarg_0);
        foreach (ParameterDefinition p in target.Parameters) il.Emit(OpCodes.Ldarg, p);
        il.Emit(OpCodes.Call, clone);
        if (result != null) il.Emit(OpCodes.Stloc, result);
        il.Append(skipOriginal);
        EmitHooks("postfix");
        EmitHooks("finalizer");
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, finalized);
        Instruction clean = Instruction.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldloc, exception); il.Emit(OpCodes.Brfalse, clean); il.Emit(OpCodes.Ldloc, exception); il.Emit(OpCodes.Throw); il.Append(clean);
        il.Emit(OpCodes.Leave, afterCatch);
        il.Append(catchStart);
        Instruction completed = Instruction.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldloc, finalized); il.Emit(OpCodes.Brtrue, completed);
        EmitHooks("finalizer", true);
        il.Append(completed); il.Emit(OpCodes.Leave, afterCatch); il.Append(afterCatch);
        target.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch) { TryStart = tryStart, TryEnd = catchStart, HandlerStart = catchStart, HandlerEnd = afterCatch, CatchType = exceptionType });
        Instruction finish = Instruction.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldloc, exception); il.Emit(OpCodes.Brfalse, finish);
        il.Emit(OpCodes.Ldloc, exception);
        TypeReference dispatch = CoreType(module, "System.Runtime.ExceptionServices", "ExceptionDispatchInfo");
        var capture = new MethodReference("Capture", dispatch, dispatch);
        capture.Parameters.Add(new ParameterDefinition(exceptionType));
        il.Emit(OpCodes.Call, capture);
        il.Emit(OpCodes.Callvirt, new MethodReference("Throw", module.TypeSystem.Void, dispatch) { HasThis = true });
        il.Append(finish);
        if (result != null) il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);

        VariableDefinition Local(TypeReference type) { var v = new VariableDefinition(type); target.Body.Variables.Add(v); return v; }

        void EmitHooks(string kind, bool suppressExceptions = false)
        {
            HookDefinition[] selected = hooks.Where(h => h.Kind == kind).ToArray();
            if (selected.Length == 0) return;
            VariableDefinition index = Local(module.TypeSystem.Int32), slot = Local(module.TypeSystem.Int32);
            Instruction loop = Instruction.Create(OpCodes.Nop), check = Instruction.Create(OpCodes.Nop), next = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, index); il.Emit(OpCodes.Br, check); il.Append(loop);
            il.Emit(OpCodes.Ldloc, ordered[kind]); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldelem_I4); il.Emit(OpCodes.Stloc, slot);
            foreach (HookDefinition hook in selected)
            {
                Instruction noMatch = Instruction.Create(OpCodes.Nop);
                il.Emit(OpCodes.Ldloc, slot); il.Emit(OpCodes.Ldc_I4, slots[hook]); il.Emit(OpCodes.Bne_Un, noMatch);
                // HarmonyX 2.7 deliberately runs EVERY prefix, even after the original was
                // skipped. Returning true later cannot undo a prior false (the flags AND).
                Instruction? callStart = suppressExceptions ? Instruction.Create(OpCodes.Nop) : null;
                if (callStart != null) il.Append(callStart);
                foreach (ParameterDefinition p in hook.Patch.Parameters) EmitArgument(p, hook);
                il.Emit(OpCodes.Call, module.ImportReference(hook.Patch));
                if (kind == "prefix" && hook.Patch.ReturnType.MetadataType == MetadataType.Boolean)
                { il.Emit(OpCodes.Ldloc, run); il.Emit(OpCodes.And); il.Emit(OpCodes.Stloc, run); }
                if (kind == "finalizer" && hook.Patch.ReturnType.MetadataType != MetadataType.Void) il.Emit(OpCodes.Stloc, exception);
                if (suppressExceptions)
                {
                    VariableDefinition secondary = Local(exceptionType);
                    Instruction handler = Instruction.Create(OpCodes.Stloc, secondary), end = Instruction.Create(OpCodes.Nop);
                    il.Emit(OpCodes.Leave, end); il.Append(handler);
                    il.Emit(OpCodes.Ldloc, secondary); il.Emit(OpCodes.Ldstr, hook.Patch.FullName);
                    il.Emit(OpCodes.Call, ImportRegistry(module, nameof(Registry.ReportFinalizerFailure)));
                    il.Emit(OpCodes.Leave, end); il.Append(end);
                    target.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
                    { TryStart = callStart, TryEnd = handler, HandlerStart = handler, HandlerEnd = end, CatchType = exceptionType });
                }
                il.Emit(OpCodes.Br, next); il.Append(noMatch);
            }
            il.Append(next); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, index);
            il.Append(check); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldloc, ordered[kind]); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Blt, loop);
        }

        void EmitArgument(ParameterDefinition p, HookDefinition hook)
        {
            bool byref = p.ParameterType.IsByReference;
            string name = p.Name;
            if (name == "__state") { il.Emit(byref ? OpCodes.Ldloca : OpCodes.Ldloc, states[hook.Patch.DeclaringType]); return; }
            if (name == "__result") { il.Emit(byref ? OpCodes.Ldloca : OpCodes.Ldloc, result!); return; }
            if (name == "__exception") { il.Emit(byref ? OpCodes.Ldloca : OpCodes.Ldloc, exception); return; }
            if (name == "__runOriginal") { il.Emit(byref ? OpCodes.Ldloca : OpCodes.Ldloc, run); return; }
            if (name == "__instance") { il.Emit(OpCodes.Ldarg_0); return; }
            if (name == "__originalMethod")
            {
                TypeReference methodBase = CoreType(module, "System.Reflection", "MethodBase");
                var getMethod = new MethodReference("GetMethodFromHandle", methodBase, methodBase);
                getMethod.Parameters.Add(new ParameterDefinition(CoreType(module, "System", "RuntimeMethodHandle", true)));
                il.Emit(OpCodes.Ldtoken, target); il.Emit(OpCodes.Call, getMethod); return;
            }
            if (name.StartsWith("___", StringComparison.Ordinal))
            {
                FieldDefinition field = target.DeclaringType.Fields.First(f => f.Name == name[3..]);
                if (!field.IsStatic) il.Emit(OpCodes.Ldarg_0);
                il.Emit(field.IsStatic ? (byref ? OpCodes.Ldsflda : OpCodes.Ldsfld) : (byref ? OpCodes.Ldflda : OpCodes.Ldfld), field); return;
            }
            ParameterDefinition? arg = target.Parameters.FirstOrDefault(a => a.Name == name);
            if (arg == null) arg = target.Parameters[int.Parse(name[2..], System.Globalization.CultureInfo.InvariantCulture)];
            TypeReference at = arg.ParameterType is ByReferenceType abr ? abr.ElementType : arg.ParameterType;
            if (byref) il.Emit(arg.ParameterType.IsByReference ? OpCodes.Ldarg : OpCodes.Ldarga, arg);
            else
            {
                il.Emit(OpCodes.Ldarg, arg);
                if (arg.ParameterType.IsByReference) il.Emit(OpCodes.Ldobj, module.ImportReference(at));
                if (at.IsValueType && p.ParameterType.FullName == "System.Object") il.Emit(OpCodes.Box, module.ImportReference(at));
            }
        }
    }

    private static TypeReference CoreType(ModuleDefinition module, string ns, string name, bool value = false)
        => new(ns, name, module, module.TypeSystem.CoreLibrary, value);

    private static MethodReference ImportRegistry(ModuleDefinition module, string name)
    {
        // Import original netstandard metadata, never reflection objects from the build host's
        // .NET 8 core library. Unity's 2021 player does not contain System.Private.CoreLib.
        using AssemblyDefinition runtime = AssemblyDefinition.ReadAssembly(typeof(Registry).Assembly.Location);
        return module.ImportReference(runtime.MainModule.GetType("QuestWeaver.Runtime.Registry").Methods.Single(m => m.Name == name));
    }
}
