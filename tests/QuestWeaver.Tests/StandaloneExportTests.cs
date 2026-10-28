using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class StandaloneExportTests
{
    internal static void Run(string projectRoot, string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        string helperPath = Path.Combine(output, "QuestProceduralExport.dll");
        using var helper = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("QuestProceduralExport", new Version(1, 0)), "QuestProceduralExport", ModuleKind.Dll);
        var capture = new TypeDefinition("GloomhavenVR.Procedural", "NativeCapture", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, helper.MainModule.TypeSystem.Object);
        helper.MainModule.Types.Add(capture);
        var expected = new Dictionary<string, string> {
            ["ApparanceCreateEntity"] = "CreateEntity", ["ApparanceDestroyEntity"] = "DestroyEntity", ["ApparanceEntityBuild"] = "Build",
            ["ApparancePopEntityTask"] = "PopEntityTask", ["ApparancePopEngineTask"] = "PopEngineTask",
            ["ApparanceUpdateAsset"] = "UpdateAsset", ["ApparanceGetNextAssetRequest"] = "GetNextAssetRequest", ["ApparanceUpdate"] = "Update" };
        string managed = Path.Combine(projectRoot, "ressources/GH_Data/Managed");
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(managed);
        using var native = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Apparance.Net.dll"), new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
        var interop = native.MainModule.GetType("Apparance.Net.Interop");
        foreach (var pair in expected)
        {
            var original = interop.Methods.Single(method => method.Name == pair.Key);
            var method = new MethodDefinition(pair.Value, MethodAttributes.Public | MethodAttributes.Static, helper.MainModule.ImportReference(original.ReturnType));
            foreach (var parameter in original.Parameters) method.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, helper.MainModule.ImportReference(parameter.ParameterType)));
            capture.Methods.Add(method);
            var il = method.Body.GetILProcessor();
            if (method.ReturnType.FullName == "System.Int32") il.Emit(OpCodes.Ldc_I4, 42);
            else if (method.ReturnType.FullName == "System.String") il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
        helper.Write(helperPath);
        StandaloneExport.BindNativeCapture(native, helperPath, new StandaloneReport());
        string adapted = Path.Combine(output, "Apparance.Net.dll"); native.Write(adapted);
        using var reread = AssemblyDefinition.ReadAssembly(adapted);
        foreach (var pair in expected)
        {
            var method = reread.MainModule.GetType("Apparance.Net.Interop").Methods.Single(m => m.Name == pair.Key);
            check(method.HasBody && !method.IsPInvokeImpl && method.PInvokeInfo == null,
                "Native capture proxy lost its managed body during Cecil serialization: " + pair.Key);
            check(method.Body.Instructions.Count == method.Parameters.Count + 2 && method.Body.Instructions[^1].OpCode == OpCodes.Ret,
                "Native proxy did not forward exactly the original arguments/result: " + pair.Key);
            check(method.Body.Instructions[^2].Operand is MethodReference call && call.DeclaringType.FullName == capture.FullName && call.Name == pair.Value,
                "Native proxy no longer calls its explicit journal boundary: " + pair.Key);
        }
        check(reread.MainModule.GetType("Apparance.Net.Interop").Methods.Count(method => method.IsPInvokeImpl) == 4,
            "Native journal changed an original initialization/save/shutdown boundary.");
        capture.Methods.Single(method => method.Name == "CreateEntity").ReturnType = helper.MainModule.TypeSystem.Int64;
        helper.Write(helperPath);
        using var negative = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Apparance.Net.dll"));
        try { StandaloneExport.BindNativeCapture(negative, helperPath, new StandaloneReport()); check(false, "Native capture accepted a changed return ABI."); }
        catch (InvalidDataException error) { check(error.Message.Contains("ABI changed"), "Native ABI negative control failed for another reason."); }
    }
}
