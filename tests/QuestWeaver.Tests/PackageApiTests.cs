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

        string? previousProgress = Environment.GetEnvironmentVariable("GHVRQ_WIZARD_PROGRESS");
        TextWriter previousOutput = Console.Out;
        using var observed = new StringWriter();
        string progressOutput = Path.Combine(root, "package-progress");
        try
        {
            Environment.SetEnvironmentVariable("GHVRQ_WIZARD_PROGRESS", "1");
            Console.SetOut(observed);
            PackageApiReport observedReport = PackageApiBindings.Write(output, target, progressOutput);
            check(observedReport.Complete && observedReport.OutputAssemblies.Count == idempotent.OutputAssemblies.Count,
                "Progress observation changed the package ABI result.");
        }
        finally
        {
            Console.SetOut(previousOutput);
            Environment.SetEnvironmentVariable("GHVRQ_WIZARD_PROGRESS", previousProgress);
        }
        var counters = observed.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => System.Text.Json.JsonDocument.Parse(line[15..])).ToArray();
        try
        {
            check(counters.Length == 4 && counters.All(item => item.RootElement.GetProperty("operation").GetString() == "package-api"),
                "CLI progress lost actual binding/publication owners or emitted unrelated diagnostics.");
            check(counters.Select(item => item.RootElement.GetProperty("done").GetInt32()).SequenceEqual(new[] { 0, 1, 0, 1 }),
                "Package progress did not record actual completed assembly boundaries.");
            check(File.ReadAllBytes(Path.Combine(progressOutput, "PackageConsumer.dll")).SequenceEqual(File.ReadAllBytes(Path.Combine(second, "PackageConsumer.dll"))),
                "Progress observation changed emitted assembly bytes.");
        }
        finally { foreach (var item in counters) item.Dispose(); }

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
        AndroidDesktopCast(root, check);
    }

    static void AndroidDesktopCast(string root, Action<bool, string> check)
    {
        string original = Path.Combine(root, "style-desktop-sdk"), target = Path.Combine(root, "style-android-sdk");
        string input = Path.Combine(root, "style-input"), output = Path.Combine(root, "style-output");
        Directory.CreateDirectory(original); Directory.CreateDirectory(target); Directory.CreateDirectory(input);
        MakeStyleSdk(Path.Combine(original, "Unity.InputSystem.dll"), true);
        MakeStyleSdk(Path.Combine(target, "Unity.InputSystem.dll"), false);
        MakeStyleConsumer(Path.Combine(input, "InControl.dll"), Path.Combine(original, "Unity.InputSystem.dll"));
        byte[] immutable = File.ReadAllBytes(Path.Combine(input, "InControl.dll"));
        check(ExecuteStyle(input, original, null) == 0 && ExecuteStyle(input, original, "XInput.XInputController") == 2
            && ExecuteStyle(input, original, "DualShock.DualShockGamepad") == 6
            && ExecuteStyle(input, original, "Switch.SwitchProControllerHID") == 21, "Original device-style branch outcomes are not reproduced.");
        try { ExecuteStyle(input, target, null); check(false, "Original desktop cast unexpectedly executed with absent Android HID type."); }
        catch (TargetInvocationException e) { check(e.InnerException is TypeLoadException, "Unavailable desktop HID reproduction did not fail with missing type."); }
        string unspecified = Path.Combine(root, "style-unspecified");
        PackageApiReport fail = PackageApiBindings.Write(input, target, unspecified);
        check(!fail.Complete && fail.Issues.Any(i => i.Contains("SwitchProControllerHID", StringComparison.Ordinal))
            && !Directory.Exists(unspecified), "Absent desktop HID was silently accepted without explicit Android target.");
        PackageApiReport result = PackageApiBindings.Write(input, target, output, sdkTarget: "Android");
        check(result.Complete && result.SdkTarget == "Android" && result.Rebindings.Count == 1
            && result.Rebindings[0].Callers.Single().Contains("DetectDeviceStyle", StringComparison.Ordinal), "Android cast adaptation omitted target/caller proof.");
        check(ExecuteStyle(output, target, null) == 0 && ExecuteStyle(output, target, "InputDevice") == 0
            && ExecuteStyle(output, target, "XInput.XInputController") == 2 && ExecuteStyle(output, target, "DualShock.DualShockGamepad") == 6,
            "Android adaptation changed existing Unknown, Xbox or DualShock style selection.");
        check(File.ReadAllBytes(Path.Combine(input, "InControl.dll")).SequenceEqual(immutable)
            && result.ProtectedTypesVerified == 1 && result.UnchangedTypesVerified >= 2, "Android adaptation mutated original, protected or unrelated types.");
        using (AssemblyDefinition before = AssemblyDefinition.ReadAssembly(Path.Combine(input, "InControl.dll")))
        using (AssemblyDefinition after = AssemblyDefinition.ReadAssembly(Path.Combine(output, "InControl.dll")))
        {
            Instruction[] oldIl = before.MainModule.GetType("InControl.NewUnityInputDevice").Methods.Single(m => m.Name == "DetectDeviceStyle").Body.Instructions.ToArray();
            Instruction[] newIl = after.MainModule.GetType("InControl.NewUnityInputDevice").Methods.Single(m => m.Name == "DetectDeviceStyle").Body.Instructions.ToArray();
            check(newIl.Length == oldIl.Length && newIl[6].OpCode == OpCodes.Nop && newIl[7].OpCode == OpCodes.Ldnull
                && Enumerable.Range(0, oldIl.Length).Where(i => i is not (6 or 7)).All(i => oldIl[i].OpCode == newIl[i].OpCode
                    && (oldIl[i].Operand is Instruction branch ? Array.IndexOf(oldIl, branch) == Array.IndexOf(newIl, (Instruction)newIl[i].Operand)
                        : oldIl[i].Operand?.ToString() == newIl[i].Operand?.ToString())), "Cast adaptation changed surrounding opcodes, values or branch targets.");
            check(!after.MainModule.GetTypeReferences().Any(t => t.FullName.EndsWith("SwitchProControllerHID", StringComparison.Ordinal)), "Emitted Android metadata retains unavailable HID type.");
            foreach (string type in new[] { "FFSNet.NetworkManager", "InControl.UnrelatedCallback" })
                check(ProtectedTypes.Fingerprint(before.MainModule.GetType(type)) == ProtectedTypes.Fingerprint(after.MainModule.GetType(type)), "Protected/unrelated executable callback changed: " + type);
            string CallbackBody(AssemblyDefinition assembly) => string.Join("|", assembly.MainModule.GetType("InControl.NewUnityInputDevice").Methods.Single(m => m.Name == "OriginalButtonCallback").Body.Instructions.Select(i => i.ToString()));
            check(CallbackBody(before) == CallbackBody(after), "Original device button callback changed during impossible cast removal.");
        }
        string repeat = Path.Combine(root, "style-repeat");
        PackageApiReport idempotent = PackageApiBindings.Write(output, target, repeat, sdkTarget: "Android");
        check(idempotent.Complete && idempotent.Rebindings.Count == 0
            && File.ReadAllBytes(Path.Combine(repeat, "InControl.dll")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "InControl.dll"))), "Android cast adaptation is not byte-preserving when repeated.");
        string desktopOutput = Path.Combine(root, "style-desktop-output");
        PackageApiReport desktop = PackageApiBindings.Write(input, original, desktopOutput, sdkTarget: "Android");
        check(desktop.Complete && desktop.Rebindings.Count == 0 && ExecuteStyle(desktopOutput, original, "Switch.SwitchProControllerHID") == 21,
            "An available desktop HID class was suppressed merely by a target flag.");
        foreach (string mutation in new[] { "assembly", "type", "method", "visibility", "signature", "opcode", "branch", "enum", "second-cast", "field-use" })
        {
            string bad = Path.Combine(root, "style-bad-" + mutation); Directory.CreateDirectory(bad);
            using (AssemblyDefinition a = AssemblyDefinition.ReadAssembly(Path.Combine(input, "InControl.dll")))
            {
                TypeDefinition owner = a.MainModule.GetType("InControl.NewUnityInputDevice"); MethodDefinition m = owner.Methods.Single(m => m.Name == "DetectDeviceStyle");
                if (mutation == "assembly") a.Name.Name = "DifferentInput";
                if (mutation == "type") owner.Name = "DifferentInputDevice";
                if (mutation == "method") m.Name = "DifferentStyleDetector";
                if (mutation == "visibility") m.IsPublic = true;
                if (mutation == "signature") m.Parameters.Add(new ParameterDefinition(a.MainModule.TypeSystem.Int32));
                if (mutation == "opcode") m.Body.Instructions[6].OpCode = OpCodes.Ldnull;
                if (mutation == "branch") m.Body.Instructions[8].Operand = m.Body.Instructions[10];
                if (mutation == "enum") a.MainModule.GetType("InControl.InputDeviceStyle").Fields.Single(f => f.Name == "Unknown").Constant = 7;
                if (mutation == "second-cast")
                {
                    var extra = new MethodDefinition("OtherCast", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static, a.MainModule.TypeSystem.Object);
                    owner.Methods.Add(extra); extra.Body.GetILProcessor().Emit(OpCodes.Ldnull);
                    extra.Body.GetILProcessor().Emit(OpCodes.Isinst, (TypeReference)m.Body.Instructions[7].Operand); extra.Body.GetILProcessor().Emit(OpCodes.Ret);
                }
                if (mutation == "field-use") owner.Fields.Add(new FieldDefinition("UnsupportedDesktopField", Mono.Cecil.FieldAttributes.Public, (TypeReference)m.Body.Instructions[7].Operand));
                a.Write(Path.Combine(bad, "InControl.dll"));
            }
            string blocked = Path.Combine(root, "style-blocked-" + mutation);
            PackageApiReport rejected = PackageApiBindings.Write(bad, target, blocked, sdkTarget: "Android");
            check(!rejected.Complete && rejected.Issues.Count > 0 && !Directory.Exists(blocked)
                && rejected.OutputAssemblies.Count == 0, "Unknown " + mutation + " produced an adapted Android payload.");
        }
        try { PackageApiBindings.Write(input, target, Path.Combine(root, "style-wrong-target"), sdkTarget: "Editor"); check(false, "Unsupported SDK target was accepted."); }
        catch (ArgumentException) { check(true, "Unsupported SDK target rejected."); }
    }

    static int ExecuteStyle(string consumer, string sdk, string? kind)
    {
        var context = new AssemblyLoadContext("device-style-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            Assembly package = context.LoadFromAssemblyPath(Path.Combine(sdk, "Unity.InputSystem.dll"));
            object? device = kind == null ? null : Activator.CreateInstance(package.GetType("UnityEngine.InputSystem." + kind)!);
            Assembly game = context.LoadFromAssemblyPath(Path.Combine(consumer, "InControl.dll"));
            return Convert.ToInt32(game.GetType("InControl.NewUnityInputDevice")!.GetMethod("DetectDeviceStyle", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { device }));
        }
        finally { context.Unload(); }
    }

    static void MakeStyleSdk(string path, bool desktop)
    {
        using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Unity.InputSystem", new Version(1, 0)), "Unity.InputSystem", ModuleKind.Dll);
        ModuleDefinition module = a.MainModule;
        var input = new TypeDefinition("UnityEngine.InputSystem", "InputDevice", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(input);
        foreach (string name in desktop ? new[] { "InputDevice", "XInput.XInputController", "DualShock.DualShockGamepad", "Switch.SwitchProControllerHID" }
            : new[] { "InputDevice", "XInput.XInputController", "DualShock.DualShockGamepad" })
        {
            int split = name.LastIndexOf('.');
            TypeDefinition type = name == "InputDevice" ? input : new TypeDefinition("UnityEngine.InputSystem." + name[..split], name[(split + 1)..], Mono.Cecil.TypeAttributes.Public, input);
            if (type != input) module.Types.Add(type);
            var ctor = new MethodDefinition(".ctor", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            type.Methods.Add(ctor); ctor.Body.GetILProcessor().Emit(OpCodes.Ldarg_0);
            ctor.Body.GetILProcessor().Emit(OpCodes.Call, type == input ? module.ImportReference(typeof(object).GetConstructor(System.Type.EmptyTypes)!) : input.Methods.Single());
            ctor.Body.GetILProcessor().Emit(OpCodes.Ret);
        }
        a.Write(path);
    }

    static void MakeStyleConsumer(string path, string sdkPath)
    {
        using var sdk = AssemblyDefinition.ReadAssembly(sdkPath);
        using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("InControl", new Version(1, 0)), "InControl", ModuleKind.Dll);
        ModuleDefinition module = a.MainModule;
        var style = new TypeDefinition("InControl", "InputDeviceStyle", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Sealed, module.ImportReference(typeof(Enum))); module.Types.Add(style);
        style.Fields.Add(new FieldDefinition("value__", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.SpecialName | Mono.Cecil.FieldAttributes.RTSpecialName, module.TypeSystem.Int32));
        foreach (var pair in new[] { ("Unknown", 0), ("XboxOne", 2), ("PlayStation4", 6), ("NintendoSwitch", 21) })
            style.Fields.Add(new FieldDefinition(pair.Item1, Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Literal | Mono.Cecil.FieldAttributes.HasDefault, style) { Constant = pair.Item2 });
        var owner = new TypeDefinition("InControl", "NewUnityInputDevice", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object); module.Types.Add(owner);
        var m = new MethodDefinition("DetectDeviceStyle", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static, style); owner.Methods.Add(m);
        m.Parameters.Add(new ParameterDefinition(module.ImportReference(sdk.MainModule.GetType("UnityEngine.InputSystem.InputDevice"))));
        ILProcessor il = m.Body.GetILProcessor(); Instruction xbox = Instruction.Create(OpCodes.Ldc_I4_2), dual = Instruction.Create(OpCodes.Ldc_I4_6);
        Instruction nintendo = Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)21), unknown = Instruction.Create(OpCodes.Ldc_I4_0);
        foreach (var pair in new[] { ("XInput.XInputController", xbox), ("DualShock.DualShockGamepad", dual), ("Switch.SwitchProControllerHID", nintendo) })
        { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, module.ImportReference(sdk.MainModule.GetType("UnityEngine.InputSystem." + pair.Item1))); il.Emit(OpCodes.Brtrue_S, pair.Item2); }
        il.Emit(OpCodes.Br_S, unknown);
        foreach (Instruction instruction in new[] { xbox, dual, nintendo, unknown }) { il.Append(instruction); il.Emit(OpCodes.Ret); }
        void Callback(TypeDefinition type, string name)
        {
            var callback = new MethodDefinition(name, Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Int32);
            type.Methods.Add(callback); callback.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            callback.Body.GetILProcessor().Emit(OpCodes.Ldarg_0); callback.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_3);
            callback.Body.GetILProcessor().Emit(OpCodes.Add); callback.Body.GetILProcessor().Emit(OpCodes.Ret);
        }
        Callback(owner, "OriginalButtonCallback");
        var protectedType = new TypeDefinition("FFSNet", "NetworkManager", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object); module.Types.Add(protectedType); Callback(protectedType, "OriginalGameplay");
        var unrelated = new TypeDefinition("InControl", "UnrelatedCallback", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object); module.Types.Add(unrelated); Callback(unrelated, "OriginalInputEvent");
        a.Write(path);
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
