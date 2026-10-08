using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;
using QuestWeaver.Runtime;
using System.Reflection;
using System.Runtime.Loader;

internal static class CurrentModCompatibilityTests
{
    sealed class Revision { private int _version; public void Advance() { _version++; } public int Current => _version; }
    sealed class WrongRevision { private string _version = "wrong"; public string Current => _version; }

    internal static void Run(string root, string modPath, string output, Action<bool, string> check)
    {
        string managed = Path.Combine(root, "ressources/GH_Data/Managed");
        var revision = new Revision(); var read = FieldReaders.Version<Revision>("_version");
        check(read(revision) == 0, "Readonly AOT reader changed the initial revision."); revision.Advance();
        check(read(revision) == 1 && revision.Current == 1, "Readonly AOT reader missed the original mutation.");
        check(Failure(() => FieldReaders.Version<Revision>("unknown")) is NotSupportedException, "Unknown runtime field name was accepted.");
        check(Failure(() => FieldReaders.Version<WrongRevision>("_version")) is MissingFieldException, "Wrong revision field type was accepted.");
        using (Discovery model = Discovery.Load(modPath, managed))
        {
            TypeDefinition camera = model.Mod.MainModule.GetType("GloomhavenVR.Core.NativeCameraRenderBudget");
            string[] mainBody = Body(camera.Methods.Single(m => m.Name == "get_Main"));
            string[] enumBody = Body(camera.Methods.Single(m => m.Name == "get_ProjectionCameras"));
            string[] applyBody = Body(camera.Methods.Single(m => m.Name == "Apply"));
            AuditReport report = model.Audit();
            check(report.Issues.Count == 0, "Actual current mod conversion blocked: " + string.Join("; ", report.Issues));
            check(report.StaticSubstitutions.Count == 2, "Current mod did not qualify both AOT substitutions.");
            check(mainBody.SequenceEqual(Body(camera.Methods.Single(m => m.Name == "get_Main")))
                && enumBody.SequenceEqual(Body(camera.Methods.Single(m => m.Name == "get_ProjectionCameras")))
                && applyBody.SequenceEqual(Body(camera.Methods.Single(m => m.Name == "Apply"))), "Camera resolver/suspension semantics changed.");
            check(!Discovery.AllTypes(model.Mod.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                .Any(i => i.Operand is MethodReference c && (c.DeclaringType.FullName == "HarmonyLib.PatchProcessor" || c.DeclaringType.Namespace.StartsWith("System.Reflection.Emit", StringComparison.Ordinal))), "Runtime camera IL inspection survived qualification.");
            TypeDefinition canvas = model.Mod.MainModule.GetType("GloomhavenVR.WorldUI.CanvasConversion");
            var resolver = canvas.Methods.Single(m => m.Name == "ResolveNativeWindowRegistryVersion");
            check(resolver.ReturnType.FullName == "System.Func`2<System.Collections.Generic.HashSet`1<UnityEngine.UI.UIWindow>,System.Int32>", "Readonly registry delegate was not specialized.");
            check(resolver.Body.Instructions.Count(i => i.Operand is MethodReference c && c.Name is "Add" or "Remove") == 2,
                "Original private-set add/remove proof was removed.");
            check(!model.Loaded.Any(a => a.Name.Name is "System.Core" or "mscorlib"), "Readonly registry accessor mutated a BCL assembly.");
            new Weaver(model).Write(output, report, false);
            check(report.Complete && report.ProtectedTypesVerified > 0, "Actual output failed protected original game boundaries.");
        }
        using (AssemblyDefinition woven = AssemblyDefinition.ReadAssembly(Path.Combine(output, "GloomhavenVR.dll")))
        {
            check(!woven.MainModule.AssemblyReferences.Any(a => a.Name == "System.Private.CoreLib"), "Host framework identity escaped into Unity output.");
            MethodDefinition factory = woven.MainModule.GetType("GloomhavenVR.WorldUI.CanvasConversion").Methods.Single(m => m.Name == "ResolveNativeWindowRegistryVersion");
            MethodReference invoke = factory.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().First(c => c.DeclaringType.FullName.StartsWith("System.Func`2<", StringComparison.Ordinal) && c.Name == "Invoke");
            check(invoke.ReturnType.FullName == "!1" && invoke.Parameters.Single().ParameterType.FullName == "!0", "Func Invoke lost its original generic metadata positions.");
            using AssemblyDefinition fixture = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("VersionInvoke", new Version(1, 0)), "VersionInvoke", ModuleKind.Dll);
            ModuleDefinition module = fixture.MainModule;
            var target = new TypeDefinition("Fixture", "Version", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
            module.Types.Add(target);
            var readVersion = new MethodDefinition("Read", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Int32);
            TypeReference set = module.ImportReference(typeof(HashSet<object>)); readVersion.Parameters.Add(new ParameterDefinition(set)); target.Methods.Add(readVersion);
            using AssemblyDefinition runtime = AssemblyDefinition.ReadAssembly(typeof(FieldReaders).Assembly.Location);
            var create = new GenericInstanceMethod(module.ImportReference(runtime.MainModule.GetType("QuestWeaver.Runtime.FieldReaders").Methods.Single(m => m.Name == "Version"))); create.GenericArguments.Add(set);
            var func = new GenericInstanceType(module.ImportReference(((GenericInstanceType)create.ReturnType).ElementType)); func.GenericArguments.Add(set); func.GenericArguments.Add(module.TypeSystem.Int32);
            var compiledInvoke = new MethodReference("Invoke", func.ElementType.GenericParameters[1], func) { HasThis = true }; compiledInvoke.Parameters.Add(new ParameterDefinition(func.ElementType.GenericParameters[0]));
            ILProcessor il = readVersion.Body.GetILProcessor(); il.Emit(OpCodes.Ldstr, "_version"); il.Emit(OpCodes.Call, create); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, compiledInvoke); il.Emit(OpCodes.Ret);
            string path = output + ".invoke.dll"; fixture.Write(path);
            var context = new AssemblyLoadContext("version-invoke", isCollectible: true); context.Resolving += (_, name) => name.Name == "QuestWeaver.Runtime" ? typeof(FieldReaders).Assembly : null;
            try {
                MethodInfo generatedRead = context.LoadFromAssemblyPath(path).GetType("Fixture.Version")!.GetMethod("Read")!;
                var sample = new HashSet<object>(); int initial = (int)generatedRead.Invoke(null, new object[] { sample })!; sample.Add(new object());
                check((int)generatedRead.Invoke(null, new object[] { sample })! != initial, "Generated AOT delegate factory/Invoke binding missed a real HashSet revision.");
            } finally { context.Unload(); File.Delete(path); }
        }
        string links = File.ReadAllText(Path.Combine(output, "link.xml"));
        check(links.Contains("HashSet`1\" preserve=\"all\"", StringComparison.Ordinal), "Private collection revision metadata can be stripped.");
        Negative("unknown camera target", a => {
            MethodDefinition bridge = a.MainModule.GetType("GloomhavenVR.Core.NativeCameraRenderBudget").Methods.Single(m => m.Name == "EnsureBridge");
            bridge.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "CanvasManager").Operand = "FFSNet.NetworkManager";
        });
        Negative("changed camera opcode", a => {
            TypeDefinition state = a.MainModule.GetType("GloomhavenVR.Core.NativeCameraRenderBudget").NestedTypes.Single(t => t.Name.StartsWith("<ReplaceMain>d__", StringComparison.Ordinal));
            FieldReference call = state.Methods.Single(m => m.Name == "MoveNext").Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Single(f => f.DeclaringType.FullName == "System.Reflection.Emit.OpCodes");
            call.Name = "Callvirt";
        });
        Negative("unfamiliar revision field", a => {
            MethodDefinition resolver = a.MainModule.GetType("GloomhavenVR.WorldUI.CanvasConversion").Methods.Single(m => m.Name == "ResolveNativeWindowRegistryVersion");
            resolver.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "_version").Operand = "otherRevision";
        });
        Negative("escaping revision reference", a => {
            MethodDefinition resolver = a.MainModule.GetType("GloomhavenVR.WorldUI.CanvasConversion").Methods.Single(m => m.Name == "ResolveNativeWindowRegistryVersion");
            resolver.Body.Instructions.First(i => i.OpCode == OpCodes.Ldind_I4).OpCode = OpCodes.Stind_I4;
        });
        Negative("additional bridge side effect", a => {
            MethodDefinition bridge = a.MainModule.GetType("GloomhavenVR.Core.NativeCameraRenderBudget").Methods.Single(m => m.Name == "EnsureBridge");
            var unknown = new MethodReference("UnknownRuntimeEffect", a.MainModule.TypeSystem.Void, bridge.DeclaringType);
            bridge.Body.GetILProcessor().InsertBefore(bridge.Body.Instructions[0], Instruction.Create(OpCodes.Call, unknown));
        });
        Negative("changed camera readiness guard", a => {
            MethodDefinition bridge = a.MainModule.GetType("GloomhavenVR.Core.NativeCameraRenderBudget").Methods.Single(m => m.Name == "EnsureBridge");
            bridge.Body.Instructions.Single(i => i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference f && f.Name == "_bridgeReady").Previous.OpCode = OpCodes.Ldc_I4_0;
        });
        Negative("second dynamic revision factory", a => {
            TypeDefinition canvas = a.MainModule.GetType("GloomhavenVR.WorldUI.CanvasConversion");
            MethodDefinition resolver = canvas.Methods.Single(m => m.Name == "ResolveNativeWindowRegistryVersion");
            var second = new MethodDefinition("OtherVersionFactory", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static, resolver.ReturnType);
            canvas.Methods.Add(second); ILProcessor il = second.Body.GetILProcessor();
            il.Emit(OpCodes.Ldstr, "_version"); il.Emit(OpCodes.Call, resolver.Body.Instructions.Select(i => i.Operand).OfType<GenericInstanceMethod>().Single(c => c.Name == "FieldRefAccess")); il.Emit(OpCodes.Ret);
        });

        void Negative(string label, Action<AssemblyDefinition> alter)
        {
            string path = output + ".negative.dll";
            using (Discovery source = Discovery.Load(modPath, managed)) { alter(source.Mod); source.Mod.Write(path); }
            try { using Discovery model = Discovery.Load(path, managed); check(model.Audit().Issues.Any(i => i.Code == "CURRENT_MOD_AOT_UNSUPPORTED"), "Unknown " + label + " did not close the build gate."); }
            finally { File.Delete(path); }
        }
    }
    static string[] Body(MethodDefinition method) => method.Body.Instructions.Select(i => i.ToString()).ToArray();
    static Exception? Failure(Action action) { try { action(); return null; } catch (Exception e) { return e; } }
}
