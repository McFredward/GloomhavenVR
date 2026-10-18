using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

/// <summary>Specializes verified runtime-code-generation and native engine paths.
/// Inputs stay read-only; all substitutions are proven against their actual metadata before
/// the normal hook discovery runs. Unknown variants remain build errors.</summary>
internal static class CurrentModCompatibility
{
    internal static void Apply(Discovery model, AuditReport report, Func<string, TypeDefinition?> find, Action<AssemblyDefinition> track)
    {
        try
        {
            CameraBridge(model, report, find, track);
            WindowRegistry(model, report);
            NativeCameraBoundary.Apply(model, report);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or AssemblyResolutionException)
        { report.Issues.Add(new IntegrationIssue("CURRENT_MOD_AOT_UNSUPPORTED", model.Mod.Name.Name, error.Message)); }
    }

    static void CameraBridge(Discovery model, AuditReport report, Func<string, TypeDefinition?> find, Action<AssemblyDefinition> track)
    {
        TypeDefinition? type = model.Mod.MainModule.GetType("GloomhavenVR.Core.NativeCameraRenderBudget");
        if (type == null) return;
        MethodDefinition bridge = Method(type, "EnsureBridge", "System.Boolean", 0);
        MethodDefinition main = Method(type, "get_Main", "UnityEngine.Camera", 0);
        MethodDefinition enumeration = Method(type, "get_ProjectionCameras", "UnityEngine.Camera[]", 0);
        ValidateBridge(bridge);
        var arrays = StringArrays(bridge).Take(3).ToArray();
        if (arrays.Length != 3 || !arrays[0].SequenceEqual(new[] { "GH.Runtime", "GH.Runtime.FirstPass", "ThirdParty" })
            || !arrays[2].SequenceEqual(new[] { "CanvasManager", "InputManager" }) || arrays[1].Length == 0
            || arrays[1].Distinct(StringComparer.Ordinal).Count() != arrays[1].Length)
            throw new InvalidDataException("Native camera bridge target/assembly lists are outside its static contract.");
        var generated = new List<TypeDefinition>();
        foreach ((string source, string getter, string replacement) in new[] {
            ("ReplaceMain", "main", "Main"), ("ReplaceProjectionEnumeration", "allCameras", "ProjectionCameras") })
        {
            MethodDefinition iterator = type.Methods.Single(m => m.Name == source);
            TypeDefinition state = type.NestedTypes.Single(t => t.Name.StartsWith("<" + source + ">d__", StringComparison.Ordinal));
            MethodDefinition move = state.Methods.Single(m => m.Name == "MoveNext");
            string[] names = move.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
            string[] lookupTypes = move.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldtoken).Select(i => ((TypeReference)i.Operand).FullName).ToArray();
            if (!names.SequenceEqual(new[] { getter, replacement })
                || !lookupTypes.SequenceEqual(new[] { "UnityEngine.Camera", type.FullName })
                || move.Body.Instructions.Count(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "HarmonyLib.AccessTools" && c.Name == "PropertyGetter") != 2
                || move.Body.Instructions.Count(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "HarmonyLib.CodeInstructionExtensions" && c.Name == "Calls") != 1
                || move.Body.Instructions.Count(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.DeclaringType.FullName == "HarmonyLib.CodeInstruction" && f.Name is "opcode" or "operand") != 2)
                throw new InvalidDataException("Native camera transpiler changed its exact getter substitution: " + source);
            Instruction match = move.Body.Instructions.Single(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "HarmonyLib.CodeInstructionExtensions" && c.Name == "Calls");
            Instruction[] mutation = new Instruction[9]; Instruction? next = match.Next;
            for (int n = 0; n < mutation.Length; n++) { mutation[n] = next ?? throw new InvalidDataException("Truncated native camera substitution."); next = next.Next; }
            if (mutation[0].OpCode.Code is not (Code.Brfalse or Code.Brfalse_S)
                || mutation[2].OpCode != OpCodes.Ldsfld || mutation[2].Operand is not FieldReference opcode || opcode.FullName != "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Call"
                || mutation[3].OpCode != OpCodes.Stfld || mutation[3].Operand is not FieldReference instructionCode || instructionCode.Name != "opcode"
                || mutation[6].OpCode != OpCodes.Ldfld || mutation[6].Operand is not FieldReference operand || operand.DeclaringType != state || operand.Name != "<replacement>5__3"
                || mutation[7].OpCode != OpCodes.Stfld || mutation[7].Operand is not FieldReference instructionOperand || instructionOperand.Name != "operand"
                || mutation[0].Operand != mutation[8])
                throw new InvalidDataException("Native camera substitution no longer writes its matched replacement only.");
            // No other call or field may import behavior into the eliminated iterator.
            foreach (Instruction i in move.Body.Instructions)
            {
                if (i.Operand is MethodReference c && !(c.Name is "GetTypeFromHandle" or "PropertyGetter" or "Calls" or "GetEnumerator" or "MoveNext" or "get_Current" or "<>m__Finally1" or "System.IDisposable.Dispose"))
                    throw new InvalidDataException("Native camera transpiler has an unknown call: " + c.FullName);
                if (i.Operand is FieldReference f && f.DeclaringType == state) continue;
                if (i.Operand is FieldReference field && !(field.DeclaringType.FullName == "HarmonyLib.CodeInstruction" && field.Name is "opcode" or "operand")
                    && !(field.DeclaringType.FullName == "System.Reflection.Emit.OpCodes" && field.Name == "Call"))
                    throw new InvalidDataException("Native camera transpiler has an unknown field: " + field.FullName);
            }
            if (Discovery.AllTypes(model.Mod.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody && m != bridge && m != iterator && m.DeclaringType != state)
                .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference c && c.FullName == iterator.FullName))
                throw new InvalidDataException("Native camera transpiler has a caller outside its verified bridge.");
            generated.Add(state);
        }
        var replacements = new List<(Instruction Call, MethodDefinition Getter, MethodDefinition Caller)>();
        Gather(arrays[1], "get_main", "UnityEngine.Camera", main);
        int mainReaders = replacements.Select(r => r.Caller).Distinct().Count();
        int prior = replacements.Count;
        Gather(arrays[2], "get_allCameras", "UnityEngine.Camera[]", enumeration);
        int enumerators = replacements.Skip(prior).Select(r => r.Caller).Distinct().Count();
        if (mainReaders == 0 || enumerators < 2) throw new InvalidDataException("Native camera static projection closure is incomplete.");
        FieldDefinition attempted = Field(type, "_bridgeAttempted", "System.Boolean"), ready = Field(type, "_bridgeReady", "System.Boolean");
        MethodReference harmony = bridge.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().DistinctBy(c => c.FullName).Single(c => c.DeclaringType.FullName == "GloomhavenVR.Core.VRSession" && c.Name == "get_Harmony");
        MethodReference note = bridge.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Single(c => c.DeclaringType.FullName == "GloomhavenVR.Core.VRLog" && c.Name == "Note");
        if (note.Parameters.Count != 2 || note.Parameters.Any(p => p.ParameterType.FullName != "System.String")) throw new InvalidDataException("Native camera projection log signature changed.");
        // Every target and replacement is closed before modifying any original method.
        foreach (var replacement in replacements)
        { replacement.Getter.IsPublic = true; replacement.Call.OpCode = OpCodes.Call; replacement.Call.Operand = replacement.Caller.Module.ImportReference(replacement.Getter); track(replacement.Caller.Module.Assembly); }
        foreach (string name in new[] { "ReplaceMain", "ReplaceProjectionEnumeration" }) type.Methods.Remove(type.Methods.Single(m => m.Name == name));
        foreach (TypeDefinition state in generated) type.NestedTypes.Remove(state);
        bridge.Body = new MethodBody(bridge); ILProcessor il = bridge.Body.GetILProcessor();
        Instruction first = Instruction.Create(OpCodes.Call, harmony), activate = Instruction.Create(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldsfld, attempted); il.Emit(OpCodes.Brfalse, first); il.Emit(OpCodes.Ldsfld, ready); il.Emit(OpCodes.Ret);
        il.Append(first); il.Emit(OpCodes.Brtrue, activate); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        il.Append(activate); il.Emit(OpCodes.Stsfld, attempted); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stsfld, ready);
        il.Emit(OpCodes.Ldstr, "WorldUI"); il.Emit(OpCodes.Ldstr, "NATIVE CAMERA PROJECTION: " + mainReaders + " original managed readers retain native Camera identity while discarded draws are suspended.");
        il.Emit(OpCodes.Call, note); il.Emit(OpCodes.Ldsfld, ready); il.Emit(OpCodes.Ret);
        report.StaticSubstitutions.Add("Native camera projection: " + mainReaders + " main-reader methods, " + enumerators + " enumeration methods, " + replacements.Count + " original getter call sites.");

        void Gather(IEnumerable<string> names, string getter, string returns, MethodDefinition replacement)
        {
            foreach (string name in names)
            {
                TypeDefinition target = find(name) ?? throw new InvalidDataException("Native camera original type is missing: " + name);
                if (Discovery.Protected(target)) throw new InvalidDataException("Native camera target is protected: " + name);
                foreach (MethodDefinition method in target.Methods.Where(m => m.HasBody && !m.IsAbstract && !m.HasGenericParameters && !m.DeclaringType.HasGenericParameters))
                    foreach (Instruction i in method.Body.Instructions)
                        if (i.Operand is MethodReference call && call.DeclaringType.FullName == "UnityEngine.Camera" && call.Name == getter)
                        {
                            if (i.OpCode != OpCodes.Call || call.HasThis || call.Parameters.Count != 0 || call.ReturnType.FullName != returns)
                                throw new InvalidDataException("Native camera getter signature/opcode changed: " + method.FullName);
                            replacements.Add((i, replacement, method));
                        }
            }
        }
    }

    static void ValidateBridge(MethodDefinition bridge)
    {
        Instruction[] prefix = bridge.Body.Instructions.Take(12).ToArray();
        if (prefix.Length != 12 || prefix[0].OpCode != OpCodes.Ldsfld || ((FieldReference)prefix[0].Operand).Name != "_bridgeAttempted"
            || prefix[1].OpCode.Code is not (Code.Brfalse or Code.Brfalse_S) || prefix[1].Operand != prefix[4]
            || prefix[2].OpCode != OpCodes.Ldsfld || ((FieldReference)prefix[2].Operand).Name != "_bridgeReady" || prefix[3].OpCode != OpCodes.Ret
            || prefix[4].OpCode != OpCodes.Call || prefix[4].Operand is not MethodReference owner || owner.DeclaringType.FullName != "GloomhavenVR.Core.VRSession" || owner.Name != "get_Harmony"
            || prefix[5].OpCode.Code is not (Code.Brtrue or Code.Brtrue_S) || prefix[5].Operand != prefix[8]
            || prefix[6].OpCode != OpCodes.Ldc_I4_0 || prefix[7].OpCode != OpCodes.Ret || prefix[8].OpCode != OpCodes.Ldc_I4_1
            || prefix[9].OpCode != OpCodes.Stsfld || ((FieldReference)prefix[9].Operand).Name != "_bridgeAttempted")
            throw new InvalidDataException("Native camera bridge owner/attempt activation guards changed.");
        var allowed = new HashSet<string>(StringComparer.Ordinal) {
            "GloomhavenVR.Core.VRSession::get_Harmony", "System.Reflection.Assembly::Load", "System.Type::GetTypeFromHandle",
            "HarmonyLib.AccessTools::PropertyGetter", "System.Reflection.MethodInfo::op_Equality", "System.MissingMethodException::.ctor",
            "HarmonyLib.AccessTools::TypeByName", "System.TypeLoadException::.ctor", "System.Type::GetMethods",
            "System.Reflection.MethodBase::get_IsAbstract", "System.Reflection.MethodBase::get_ContainsGenericParameters", "System.Reflection.MethodBase::GetMethodBody",
            "HarmonyLib.PatchProcessor::GetOriginalInstructions", "System.Collections.Generic.List`1<HarmonyLib.CodeInstruction>::GetEnumerator",
            "System.Collections.Generic.List`1/Enumerator<HarmonyLib.CodeInstruction>::get_Current", "HarmonyLib.CodeInstructionExtensions::Calls",
            "System.Collections.Generic.List`1/Enumerator<HarmonyLib.CodeInstruction>::MoveNext", "System.IDisposable::Dispose",
            "HarmonyLib.AccessTools::Method", "HarmonyLib.HarmonyMethod::.ctor", "HarmonyLib.Harmony::Patch", "System.Int32::ToString",
            "System.String::Concat", "GloomhavenVR.Core.VRLog::Note", "System.Exception::GetType", "System.Reflection.MemberInfo::get_Name",
            "System.Exception::get_Message", "GloomhavenVR.Core.VRLog::Warn" };
        foreach (Instruction i in bridge.Body.Instructions)
        {
            if (i.Operand is MethodReference call && !allowed.Contains(call.DeclaringType.FullName + "::" + call.Name))
                throw new InvalidDataException("Native camera bridge has an unknown runtime effect: " + call.FullName);
            if (i.Operand is FieldReference field && (field.DeclaringType != bridge.DeclaringType || field.Name is not ("_bridgeAttempted" or "_bridgeReady" or "_faultNoted")))
                throw new InvalidDataException("Native camera bridge has an unknown field effect: " + field.FullName);
            if (i.OpCode == OpCodes.Stsfld && i.Previous?.OpCode != OpCodes.Ldc_I4_1)
                throw new InvalidDataException("Native camera bridge readiness/fault policy changed.");
            if (i.Operand is MethodReference member && member.Name == "GetMethods" && Int(i.Previous) != 62)
                throw new InvalidDataException("Native camera bridge declared-method search policy changed.");
            if (i.Operand is MethodReference patch && patch.DeclaringType.FullName == "HarmonyLib.Harmony" && patch.Name == "Patch")
            {
                Instruction? constructor = i.Previous?.Previous?.Previous;
                if (i.Previous?.OpCode != OpCodes.Ldnull || i.Previous.Previous?.OpCode != OpCodes.Ldnull || constructor?.OpCode != OpCodes.Newobj
                    || constructor.Operand is not MethodReference harmony || harmony.DeclaringType.FullName != "HarmonyLib.HarmonyMethod"
                    || constructor.Previous?.Operand is not MethodReference method || method.DeclaringType.FullName != "HarmonyLib.AccessTools" || method.Name != "Method"
                    || constructor.Previous.Previous?.OpCode != OpCodes.Ldnull || constructor.Previous.Previous.Previous?.OpCode != OpCodes.Ldnull
                    || constructor.Previous.Previous.Previous.Previous?.OpCode != OpCodes.Ldstr
                    || constructor.Previous.Previous.Previous.Previous.Operand is not string name || name is not ("ReplaceMain" or "ReplaceProjectionEnumeration"))
                    throw new InvalidDataException("Native camera bridge transpiler registration vector changed.");
            }
        }
        if (bridge.Body.Instructions.Count(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "HarmonyLib.Harmony" && c.Name == "Patch") != 2
            || bridge.Body.Instructions.Count(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "HarmonyLib.PatchProcessor" && c.Name == "GetOriginalInstructions") != 2)
            throw new InvalidDataException("Native camera bridge registration/inspection count changed.");
    }

    static IEnumerable<string[]> StringArrays(MethodDefinition method)
    {
        foreach (Instruction allocation in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Newarr && i.Operand is TypeReference t && t.FullName == "System.String"))
        {
            int count = Int(allocation.Previous); var names = new List<string>(); Instruction? current = allocation.Next;
            for (int n = 0; n < count; n++)
            {
                if (current?.OpCode != OpCodes.Dup || Int(current.Next) != n || current.Next?.Next?.OpCode != OpCodes.Ldstr || current.Next.Next.Next?.OpCode != OpCodes.Stelem_Ref)
                    throw new InvalidDataException("Native camera target list is no longer a literal string array.");
                names.Add((string)current.Next.Next.Operand); current = current.Next.Next.Next.Next;
            }
            yield return names.ToArray();
        }
    }
    static int Int(Instruction? i) => i?.OpCode.Code switch {
        Code.Ldc_I4_0 => 0, Code.Ldc_I4_1 => 1, Code.Ldc_I4_2 => 2, Code.Ldc_I4_3 => 3, Code.Ldc_I4_4 => 4,
        Code.Ldc_I4_5 => 5, Code.Ldc_I4_6 => 6, Code.Ldc_I4_7 => 7, Code.Ldc_I4_8 => 8,
        Code.Ldc_I4 or Code.Ldc_I4_S => Convert.ToInt32(i.Operand), _ => throw new InvalidDataException("Expected a literal array count/index.") };
    static MethodDefinition Method(TypeDefinition t, string name, string returns, int count) => t.Methods.Single(m => m.Name == name && m.IsStatic && m.ReturnType.FullName == returns && m.Parameters.Count == count);
    static FieldDefinition Field(TypeDefinition t, string name, string type) => t.Fields.Single(f => f.Name == name && f.IsStatic && f.FieldType.FullName == type);

    static void WindowRegistry(Discovery model, AuditReport report)
    {
        TypeDefinition? type = model.Mod.MainModule.GetType("GloomhavenVR.WorldUI.CanvasConversion");
        MethodDefinition? resolver = type?.Methods.FirstOrDefault(m => m.Name == "ResolveNativeWindowRegistryVersion");
        if (resolver == null) return;
        TypeDefinition collection = RegistryVersionSpecializer.Apply(model.Mod.MainModule, resolver);
        report.ReflectionRoots.Add(new ReflectionRoot(collection.Module.Assembly.Name.Name, collection.FullName));
        report.StaticSubstitutions.Add("Native window registry: readonly private HashSet version delegate; original add/remove qualification retained, no BCL mutation or dynamic code.");
    }
}
