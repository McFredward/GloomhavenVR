using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

/// <summary>Retains original config/logging implementations; removes desktop loader/codegen closure.</summary>
internal static class BepInExStandalone
{
    internal static void Write(string originalPath, string modPath, string destination, StandaloneReport report)
    {
        using var bep = AssemblyDefinition.ReadAssembly(originalPath, new ReaderParameters { InMemory = true });
        using var mod = AssemblyDefinition.ReadAssembly(modPath, new ReaderParameters { InMemory = true });
        if (bep.Name.HasPublicKey) throw new InvalidDataException("Strong-named BepInEx requires a separate verified signing strategy.");
        ModuleDefinition m = bep.MainModule;
        var types = Discovery.AllTypes(m).ToDictionary(t => t.FullName);
        TypeDefinition Type(string name) => types.TryGetValue(name, out var t) ? t : throw new InvalidDataException("Required BepInEx type changed: " + name);
        MethodDefinition Method(string type, string name, params string[] parameters) => Type(type).Methods.SingleOrDefault(x => x.Name == name && x.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters))
            ?? throw new InvalidDataException("Required BepInEx API changed: " + type + "::" + name);
        MethodDefinition ctor = Method("BepInEx.BaseUnityPlugin", ".ctor");
        MethodReference monoCtor = ctor.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().First(c => c.Name == ".ctor" && c.DeclaringType.FullName == "UnityEngine.MonoBehaviour");
        if (!ctor.Body.Instructions.Any(i => i.Operand is MemberReference r && r.DeclaringType.FullName == "BepInEx.Bootstrap.Chainloader"))
            throw new InvalidDataException("BepInEx input lacks expected original Chainloader constructor seam.");
        ctor.Body = new MethodBody(ctor) { InitLocals = true, MaxStackSize = 12 };
        var metadata = new VariableDefinition(Type("BepInEx.BepInPlugin")); ctor.Body.Variables.Add(metadata);
        ILProcessor il = ctor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, monoCtor);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, Method("BepInEx.MetadataHelper", "GetMetadata", "System.Object")); il.Emit(OpCodes.Stloc, metadata);
        // Original metadata, logger, config classes and setters, not a replacement config model.
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Newobj, Method("BepInEx.PluginInfo", ".ctor"));
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, metadata); il.Emit(OpCodes.Callvirt, Method("BepInEx.PluginInfo", "set_Metadata", "BepInEx.BepInPlugin"));
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, Method("BepInEx.PluginInfo", "set_Instance", "BepInEx.BaseUnityPlugin"));
        il.Emit(OpCodes.Stfld, Type("BepInEx.BaseUnityPlugin").Fields.Single(f => f.Name == "<Info>k__BackingField"));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, metadata); il.Emit(OpCodes.Callvirt, Method("BepInEx.BepInPlugin", "get_Name"));
        il.Emit(OpCodes.Call, Method("BepInEx.Logging.Logger", "CreateLogSource", "System.String"));
        il.Emit(OpCodes.Stfld, Type("BepInEx.BaseUnityPlugin").Fields.Single(f => f.Name == "<Logger>k__BackingField"));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, Method("BepInEx.Paths", "get_ConfigPath"));
        il.Emit(OpCodes.Ldloc, metadata); il.Emit(OpCodes.Callvirt, Method("BepInEx.BepInPlugin", "get_GUID"));
        il.Emit(OpCodes.Ldstr, ".cfg");
        var concat = new MethodReference("Concat", m.TypeSystem.String, m.TypeSystem.String); concat.Parameters.Add(new ParameterDefinition(m.TypeSystem.String)); concat.Parameters.Add(new ParameterDefinition(m.TypeSystem.String));
        il.Emit(OpCodes.Call, concat);
        var pathType = new TypeReference("System.IO", "Path", m, m.TypeSystem.CoreLibrary);
        var combine = new MethodReference("Combine", m.TypeSystem.String, pathType); combine.Parameters.Add(new ParameterDefinition(m.TypeSystem.String)); combine.Parameters.Add(new ParameterDefinition(m.TypeSystem.String));
        il.Emit(OpCodes.Call, combine); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, metadata);
        il.Emit(OpCodes.Newobj, Method("BepInEx.Configuration.ConfigFile", ".ctor", "System.String", "System.Boolean", "BepInEx.BepInPlugin"));
        il.Emit(OpCodes.Stfld, Type("BepInEx.BaseUnityPlugin").Fields.Single(f => f.Name == "<Config>k__BackingField")); il.Emit(OpCodes.Ret);

        var standalone = new TypeDefinition("BepInEx", "QuestStandalone", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, m.TypeSystem.Object); m.Types.Add(standalone); types.Add(standalone.FullName, standalone);
        var init = new MethodDefinition("Initialize", MethodAttributes.Public | MethodAttributes.Static, m.TypeSystem.Void);
        init.Name = "InitializeQuestStandalone"; init.Parameters.Add(new ParameterDefinition("root", ParameterAttributes.None, m.TypeSystem.String)); Type("BepInEx.Paths").Methods.Add(init);
        var entry = new MethodDefinition("Initialize", MethodAttributes.Public | MethodAttributes.Static, m.TypeSystem.Void);
        entry.Parameters.Add(new ParameterDefinition("root", ParameterAttributes.None, m.TypeSystem.String)); standalone.Methods.Add(entry);
        entry.Body.GetILProcessor().Emit(OpCodes.Ldarg_0); entry.Body.GetILProcessor().Emit(OpCodes.Call, init); entry.Body.GetILProcessor().Emit(OpCodes.Ret);
        ILProcessor pi = init.Body.GetILProcessor();
        var values = new Dictionary<string, string> {
            ["BepInExRootPath"] = "", ["GameRootPath"] = "", ["ConfigPath"] = "config", ["BepInExConfigPath"] = "config/BepInEx.cfg",
            ["PluginPath"] = "plugins", ["PatcherPluginPath"] = "patchers", ["CachePath"] = "cache", ["ManagedPath"] = "managed",
            ["BepInExAssemblyDirectory"] = "core", ["BepInExAssemblyPath"] = "core/BepInEx.dll", ["ExecutablePath"] = "QuestStandalone" };
        foreach (var pair in values)
        {
            pi.Emit(OpCodes.Ldarg_0);
            if (pair.Value.Length != 0) { pi.Emit(OpCodes.Ldstr, pair.Value); pi.Emit(OpCodes.Call, combine); }
            pi.Emit(OpCodes.Call, Method("BepInEx.Paths", "set_" + pair.Key, "System.String"));
        }
        pi.Emit(OpCodes.Ldstr, "QuestStandalone"); pi.Emit(OpCodes.Call, Method("BepInEx.Paths", "set_ProcessName", "System.String")); pi.Emit(OpCodes.Ret);

        // Keep an automatically traversed closure rooted in the chosen mod's actual BepInEx
        // API and all original configuration APIs (the options browser reflects over them).
        var keptMethods = new HashSet<MethodDefinition>(); var keptTypes = new HashSet<TypeDefinition>();
        var pending = new Queue<MethodDefinition>();
        void KeepMethod(MethodDefinition method) { if (keptMethods.Add(method)) pending.Enqueue(method); KeepType(method.DeclaringType); }
        void KeepType(TypeDefinition type)
        {
            if (!keptTypes.Add(type)) return;
            if (type.DeclaringType != null) KeepType(type.DeclaringType);
            foreach (MethodDefinition method in type.Methods.Where(x => x.IsConstructor && x.IsStatic || x.IsVirtual)) KeepMethod(method);
            if (type.BaseType != null) KeepReference(type.BaseType);
            foreach (InterfaceImplementation iface in type.Interfaces) KeepReference(iface.InterfaceType);
            foreach (FieldDefinition field in type.Fields) KeepReference(field.FieldType);
            foreach (CustomAttribute attr in type.CustomAttributes) KeepReference(attr.Constructor);
        }
        void KeepReference(MemberReference member)
        {
            if (member is GenericParameter) return;
            if (member is TypeSpecification specification) { KeepReference(specification.ElementType); if (member is GenericInstanceType generic) foreach (TypeReference a in generic.GenericArguments) KeepReference(a); return; }
            TypeReference declaring = member is TypeReference reference ? reference : member.DeclaringType;
            string name = declaring is GenericInstanceType constructed ? constructed.ElementType.FullName : declaring.FullName;
            if (!types.TryGetValue(name, out TypeDefinition? type))
            {
                if (declaring.Scope.Name is "BepInEx" or "BepInEx.dll") throw new InvalidDataException("BepInEx closure type unresolved: " + name);
                return;
            }
            KeepType(type);
            if (member is MethodReference mr)
            {
                if (mr is GenericInstanceMethod gm) { foreach (TypeReference a in gm.GenericArguments) KeepReference(a); mr = gm.ElementMethod; }
                MethodDefinition? found = member as MethodDefinition ?? type.Methods.FirstOrDefault(candidate => candidate.Name == mr.Name && candidate.GenericParameters.Count == mr.GenericParameters.Count
                    && Key(candidate.ReturnType) == Key(mr.ReturnType) && candidate.Parameters.Select(p => Key(p.ParameterType)).SequenceEqual(mr.Parameters.Select(p => Key(p.ParameterType))));
                if (found == null) throw new InvalidDataException("BepInEx closure method unresolved: " + mr.FullName);
                KeepMethod(found);
            }
            else if (member is FieldReference field)
            {
                if (!type.Fields.Any(f => f.Name == field.Name && Key(f.FieldType) == Key(field.FieldType))) throw new InvalidDataException("BepInEx closure field unresolved: " + field.FullName);
                KeepReference(field.FieldType);
            }
        }
        KeepType(Type("<Module>")); KeepMethod(entry); KeepMethod(init); KeepMethod(ctor);
        foreach (TypeDefinition type in types.Values.Where(t => t.Namespace == "BepInEx.Configuration")) foreach (MethodDefinition method in type.Methods) KeepMethod(method);
        foreach (MethodDefinition method in Type("BepInEx.Logging.ManualLogSource").Methods) KeepMethod(method);
        foreach (MemberReference member in mod.MainModule.GetMemberReferences().Where(r => r.DeclaringType.Scope.Name is "BepInEx" or "BepInEx.dll")) KeepReference(member);
        foreach (MethodDefinition accessor in Type("BepInEx.BaseUnityPlugin").Methods.Where(x => x.IsGetter)) KeepMethod(accessor);
        while (pending.TryDequeue(out MethodDefinition? method))
        {
            KeepReference(method.ReturnType); foreach (ParameterDefinition p in method.Parameters) KeepReference(p.ParameterType);
            foreach (CustomAttribute attr in method.CustomAttributes) KeepReference(attr.Constructor);
            if (!method.HasBody) continue;
            foreach (VariableDefinition v in method.Body.Variables) KeepReference(v.VariableType);
            foreach (ExceptionHandler h in method.Body.ExceptionHandlers) if (h.CatchType != null) KeepReference(h.CatchType);
            foreach (Instruction instruction in method.Body.Instructions) if (instruction.Operand is MemberReference member) KeepReference(member);
        }
        foreach (TypeDefinition type in types.Values)
        {
            foreach (MethodDefinition method in type.Methods.ToArray()) if (!keptMethods.Contains(method)) type.Methods.Remove(method);
            foreach (PropertyDefinition property in type.Properties.ToArray())
                if (property.GetMethod != null && !keptMethods.Contains(property.GetMethod) || property.SetMethod != null && !keptMethods.Contains(property.SetMethod)) type.Properties.Remove(property);
            foreach (EventDefinition evt in type.Events.ToArray())
                if (evt.AddMethod != null && !keptMethods.Contains(evt.AddMethod) || evt.RemoveMethod != null && !keptMethods.Contains(evt.RemoveMethod)) type.Events.Remove(evt);
            if (!keptTypes.Contains(type)) { if (type.DeclaringType != null) type.DeclaringType.NestedTypes.Remove(type); else m.Types.Remove(type); }
        }
        string[] forbidden = { "MonoMod", "Mono.Cecil", "HarmonyLib", "System.Reflection.Emit", "BepInEx.Bootstrap.Chainloader" };
        foreach (MethodDefinition method in keptMethods.Where(x => x.HasBody))
            foreach (Instruction instruction in method.Body.Instructions)
                if (instruction.Operand is MemberReference r && forbidden.Any(f => r.FullName.Contains(f, StringComparison.Ordinal)))
                    throw new InvalidDataException("Standalone BepInEx closure retains unsupported desktop code: " + method.FullName + " -> " + r.FullName);
        foreach (AssemblyNameReference reference in m.AssemblyReferences.Where(a => a.Name is "Mono.Cecil" or "MonoMod.Utils" or "MonoMod.RuntimeDetour" or "0Harmony").ToArray()) m.AssemblyReferences.Remove(reference);
        bep.Write(destination);
        using var reread = AssemblyDefinition.ReadAssembly(destination);
        if (reread.Name.FullName != bep.Name.FullName) throw new InvalidDataException("Standalone BepInEx assembly identity changed.");
        report.Modifications.Add("BepInEx original config/logging source closure: " + keptTypes.Count + " types / " + keptMethods.Count + " methods; standalone constructor and paths");
        report.RemainingGates.Add(new IntegrationIssue("BEPINEX_LIFECYCLE_EXECUTION", mod.Name.Name, "Generated lifecycle/config/logging closure does not certify original mod XR, content, AOT reflection or native callbacks. Initialize paths before any ConfigFile and instantiate only after those gates pass."));
    }

    static string Key(TypeReference type) => type switch {
        GenericParameter p => (p.Type == GenericParameterType.Method ? "!!" : "!") + p.Position,
        GenericInstanceType g => g.ElementType.FullName + "<" + string.Join(",", g.GenericArguments.Select(Key)) + ">",
        ByReferenceType b => Key(b.ElementType) + "&", ArrayType a => Key(a.ElementType) + "[" + a.Rank + "]", _ => type.FullName };
}
