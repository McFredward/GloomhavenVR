using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class PackageApiTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        string original = Path.Combine(root, "package-original"), target = Path.Combine(root, "package-target"), input = Path.Combine(root, "package-input");
        Directory.CreateDirectory(original); Directory.CreateDirectory(target); Directory.CreateDirectory(input);
        MakeSdk(Path.Combine(original, "Unity.InputSystem.dll"), false);
        MakeSdk(Path.Combine(target, "Unity.InputSystem.dll"), true);
        MakeConsumer(Path.Combine(input, "PackageConsumer.dll"), Path.Combine(original, "Unity.InputSystem.dll"));
        byte[] immutable = File.ReadAllBytes(Path.Combine(input, "PackageConsumer.dll"));
        try { Execute(input, target); check(false, "Unadapted old package getter unexpectedly executed against covariant target."); }
        catch (TargetInvocationException e)
        { check(e.InnerException is MissingMethodException, "ABI reproduction did not fail with missing method."); }
        string output = Path.Combine(root, "package-output");
        PackageApiReport result = PackageApiBindings.Write(input, target, output);
        check(result.Complete && result.Issues.Count == 0 && result.Rebindings.Count == 2, "Proven covariant getters were not rebound completely.");
        check(result.Rebindings.All(r => r.OriginalMember.Contains("Controls.Vector2Control", StringComparison.Ordinal)
            && r.BoundMember.Contains("Controls.DeltaControl", StringComparison.Ordinal) && r.Callers.Length == 1), "Getter source/target/caller evidence is incomplete.");
        check(result.InputAssemblies.Keys.SequenceEqual(result.OutputAssemblies.Keys) && result.SdkAssemblies.Count == 1, "Adaptation hashes omit an input/output or actual SDK.");
        check(File.ReadAllBytes(Path.Combine(input, "PackageConsumer.dll")).SequenceEqual(immutable), "Read-only original assembly was mutated.");
        check(Execute(output, target) == 84, "Rebound callbacks do not read original inherited Vector2 values.");
        string second = Path.Combine(root, "package-second");
        PackageApiReport idempotent = PackageApiBindings.Write(output, target, second);
        check(idempotent.Complete && idempotent.Rebindings.Count == 0
            && File.ReadAllBytes(Path.Combine(second, "PackageConsumer.dll")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "PackageConsumer.dll"))), "Validated exact package APIs must be byte-preserving.");
        check(idempotent.CheckedTypeReferences > 0 && idempotent.CheckedMemberReferences >= 6 && idempotent.UnchangedTypesVerified > 0,
            "Preflight did not audit the entire referenced type/member surface, including inherited calls and fields.");

        foreach (string mutation in new[] { "missing-field", "return", "parameter", "instance", "unknown-getter", "no-subtype", "type", "generic-arity", "generic-method" })
        {
            string badSdk = Path.Combine(root, "package-bad-" + mutation); Directory.CreateDirectory(badSdk);
            using (AssemblyDefinition sdk = AssemblyDefinition.ReadAssembly(Path.Combine(target, "Unity.InputSystem.dll")))
            {
                TypeDefinition pointer = sdk.MainModule.GetType("UnityEngine.InputSystem.Pointer");
                MethodDefinition getter = pointer.Methods.Single(m => m.Name == "get_delta");
                if (mutation == "missing-field") pointer.Fields.Single(f => f.Name == "Tag").Name = "Renamed";
                if (mutation == "return") getter.ReturnType = sdk.MainModule.TypeSystem.Object;
                if (mutation == "parameter") getter.Parameters.Add(new ParameterDefinition(sdk.MainModule.TypeSystem.Int32));
                if (mutation == "instance") getter.IsStatic = true;
                if (mutation == "unknown-getter") getter.Name = "get_different";
                if (mutation == "no-subtype") sdk.MainModule.GetType("UnityEngine.InputSystem.Controls.DeltaControl").BaseType = sdk.MainModule.TypeSystem.Object;
                if (mutation == "type") pointer.Name = "RenamedPointer";
                if (mutation == "generic-arity") getter.GenericParameters.Add(new GenericParameter("Unexpected", getter));
                if (mutation == "generic-method") pointer.Methods.Single(m => m.Name == "Identity").Name = "ChangedIdentity";
                sdk.Write(Path.Combine(badSdk, "Unity.InputSystem.dll"));
            }
            string blocked = Path.Combine(root, "package-blocked-" + mutation);
            PackageApiReport failure = PackageApiBindings.Write(input, badSdk, blocked);
            check(!failure.Complete && failure.Issues.Count > 0 && !Directory.Exists(blocked)
                && failure.OutputAssemblies.Count == 0, mutation + " produced a partial or falsely successful adaptation.");
        }
        string absentSdk = Path.Combine(root, "package-absent-sdk"); Directory.CreateDirectory(absentSdk);
        PackageApiReport absent = PackageApiBindings.Write(input, absentSdk, Path.Combine(root, "package-absent-output"));
        check(!absent.Complete && absent.Issues.Count > 0, "Disabled original SDK silently replaced missing imported SDK.");
        try { PackageApiBindings.Write(input, target, input); check(false, "Overlapping package output overwrote immutable input."); }
        catch (ArgumentException) { check(true, "Overlapping package output rejected."); }
        string protectedInput = Path.Combine(root, "package-protected-input"); Directory.CreateDirectory(protectedInput);
        using (AssemblyDefinition protectedAssembly = AssemblyDefinition.ReadAssembly(Path.Combine(input, "PackageConsumer.dll")))
        {
            TypeDefinition type = protectedAssembly.MainModule.GetType("PackageConsumer.Entry"); type.Namespace = "FFSNet"; type.Name = "NetworkManager";
            protectedAssembly.Write(Path.Combine(protectedInput, "PackageConsumer.dll"));
        }
        string protectedOutput = Path.Combine(root, "package-protected-output");
        try { PackageApiBindings.Write(protectedInput, target, protectedOutput); check(false, "Package getter binding silently modified protected gameplay/network class."); }
        catch (InvalidDataException e) { check(e.Message.Contains("protected game type", StringComparison.Ordinal) && !Directory.Exists(protectedOutput), "Protected API class rejection wrote partial output or lacked a cause."); }
    }

    static int Execute(string consumer, string sdk)
    {
        var context = new AssemblyLoadContext("package-api-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            context.Resolving += (_, name) => name.Name == "Unity.InputSystem" ? context.LoadFromAssemblyPath(Path.Combine(sdk, "Unity.InputSystem.dll")) : null;
            Assembly game = context.LoadFromAssemblyPath(Path.Combine(consumer, "PackageConsumer.dll"));
            return (int)game.GetType("PackageConsumer.Entry")!.GetMethod("Read")!.Invoke(null, null)!;
        }
        finally { context.Unload(); }
    }

    static void MakeSdk(string path, bool modern)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Unity.InputSystem", new Version(1, 0)), "Unity.InputSystem", ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        TypeDefinition Type(string ns, string name, TypeReference parent)
        { var type = new TypeDefinition(ns, name, Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, parent); module.Types.Add(type); return type; }
        MethodDefinition Constructor(TypeDefinition type)
        {
            var constructor = new MethodDefinition(".ctor", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            type.Methods.Add(constructor); ILProcessor il = constructor.Body.GetILProcessor(); il.Emit(OpCodes.Ldarg_0);
            MethodReference parent = type.BaseType.FullName == "System.Object" ? module.ImportReference(typeof(object).GetConstructor(System.Type.EmptyTypes)!)
                : type.BaseType.Resolve().Methods.Single(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
            il.Emit(OpCodes.Call, parent); il.Emit(OpCodes.Ret); return constructor;
        }
        TypeDefinition vector = Type("UnityEngine.InputSystem.Controls", "Vector2Control", module.TypeSystem.Object);
        Constructor(vector);
        var read = new MethodDefinition("ReadValue", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Int32);
        vector.Methods.Add(read); read.Body.GetILProcessor().Emit(OpCodes.Ldc_I4, 42); read.Body.GetILProcessor().Emit(OpCodes.Ret);
        TypeDefinition delta = Type("UnityEngine.InputSystem.Controls", "DeltaControl", vector); Constructor(delta);
        TypeDefinition pointer = Type("UnityEngine.InputSystem", "Pointer", module.TypeSystem.Object); Constructor(pointer);
        pointer.Fields.Add(new FieldDefinition("Tag", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.Int32));
        TypeDefinition mouse = Type("UnityEngine.InputSystem", "Mouse", pointer); Constructor(mouse);
        foreach (var pair in new[] { (pointer, "delta"), (mouse, "scroll") })
        {
            TypeDefinition value = modern ? delta : vector;
            var getter = new MethodDefinition("get_" + pair.Item2, Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.HideBySig, value);
            pair.Item1.Methods.Add(getter); getter.Body.GetILProcessor().Emit(OpCodes.Newobj, value.Methods.Single(m => m.IsConstructor)); getter.Body.GetILProcessor().Emit(OpCodes.Ret);
            pair.Item1.Properties.Add(new PropertyDefinition(pair.Item2, Mono.Cecil.PropertyAttributes.None, value) { GetMethod = getter });
        }
        var stable = new MethodDefinition("Stable", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Int32);
        pointer.Methods.Add(stable); stable.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_0); stable.Body.GetILProcessor().Emit(OpCodes.Ret);
        var generic = new MethodDefinition("Identity", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
        pointer.Methods.Add(generic); var valueParameter = new GenericParameter(modern ? "RenamedValue" : "OriginalValue", generic);
        generic.GenericParameters.Add(valueParameter); generic.ReturnType = valueParameter; generic.Parameters.Add(new ParameterDefinition(valueParameter));
        generic.Body.GetILProcessor().Emit(OpCodes.Ldarg_0); generic.Body.GetILProcessor().Emit(OpCodes.Ret);
        assembly.Write(path);
    }

    static void MakeConsumer(string path, string originalSdk)
    {
        using var sdk = AssemblyDefinition.ReadAssembly(originalSdk);
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("PackageConsumer", new Version(1, 0)), "PackageConsumer", ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        var entry = new TypeDefinition("PackageConsumer", "Entry", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object); module.Types.Add(entry);
        var method = new MethodDefinition("Read", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Int32); entry.Methods.Add(method);
        TypeDefinition pointer = sdk.MainModule.GetType("UnityEngine.InputSystem.Pointer"), mouse = sdk.MainModule.GetType("UnityEngine.InputSystem.Mouse");
        MethodReference read = module.ImportReference(sdk.MainModule.GetType("UnityEngine.InputSystem.Controls.Vector2Control").Methods.Single(m => m.Name == "ReadValue"));
        ILProcessor il = method.Body.GetILProcessor();
        foreach (var pair in new[] { (pointer, "get_delta"), (mouse, "get_scroll") })
        {
            il.Emit(OpCodes.Newobj, module.ImportReference(pair.Item1.Methods.Single(m => m.IsConstructor)));
            il.Emit(OpCodes.Callvirt, module.ImportReference(pair.Item1.Methods.Single(m => m.Name == pair.Item2)));
            il.Emit(OpCodes.Callvirt, read);
        }
        il.Emit(OpCodes.Add);
        // The owner is a derived type and the member lives on its base type.
        var inherited = new MethodReference("Stable", module.TypeSystem.Int32, module.ImportReference(mouse)) { HasThis = true };
        il.Emit(OpCodes.Newobj, module.ImportReference(mouse.Methods.Single(m => m.IsConstructor))); il.Emit(OpCodes.Callvirt, inherited); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newobj, module.ImportReference(pointer.Methods.Single(m => m.IsConstructor))); il.Emit(OpCodes.Ldfld, module.ImportReference(pointer.Fields.Single())); il.Emit(OpCodes.Add);
        var generic = new GenericInstanceMethod(module.ImportReference(pointer.Methods.Single(m => m.Name == "Identity"))); generic.GenericArguments.Add(module.TypeSystem.Int32);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Call, generic); il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret); assembly.Write(path);
    }
}
