using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

/// <summary>Activates the isolated build-time exporter before the original Windows startup.</summary>
internal static class StandaloneExport
{
    internal static void BindNativeCapture(AssemblyDefinition nativeApi, string helperPath, StandaloneReport report)
    {
        using var helper = AssemblyDefinition.ReadAssembly(helperPath, new ReaderParameters { InMemory = true });
        TypeDefinition capture = helper.MainModule.GetType("GloomhavenVR.Procedural.NativeCapture")
            ?? throw new InvalidDataException("Original native capture helper is missing.");
        TypeDefinition interop = nativeApi.MainModule.GetType("Apparance.Net.Interop")
            ?? throw new InvalidDataException("Original native ABI type is missing.");
        var bindings = new Dictionary<string, string> {
            ["ApparanceCreateEntity"] = "CreateEntity", ["ApparanceDestroyEntity"] = "DestroyEntity", ["ApparanceEntityBuild"] = "Build",
            ["ApparancePopEntityTask"] = "PopEntityTask", ["ApparancePopEngineTask"] = "PopEngineTask",
            ["ApparanceUpdateAsset"] = "UpdateAsset", ["ApparanceGetNextAssetRequest"] = "GetNextAssetRequest", ["ApparanceUpdate"] = "Update" };
        foreach (var pair in bindings)
        {
            MethodDefinition original = interop.Methods.Single(method => method.Name == pair.Key && method.IsStatic && method.IsPInvokeImpl);
            MethodDefinition proxy = capture.Methods.Single(method => method.Name == pair.Value && method.IsStatic && method.IsPublic);
            if (original.ReturnType.FullName != proxy.ReturnType.FullName || original.PInvokeInfo.Module.Name != "ApparanceEngine"
                || original.PInvokeInfo.EntryPoint != pair.Key || !original.Parameters.Select(p => p.ParameterType.FullName)
                    .SequenceEqual(proxy.Parameters.Select(p => p.ParameterType.FullName)))
                throw new InvalidDataException("Native request capture ABI changed: " + pair.Key);
            original.PInvokeInfo = null;
            // Cecil's PInvokeInfo setter enables PInvokeImpl even for null.
            // Clear the flag afterwards or the writer drops the managed body.
            original.IsPInvokeImpl = false;
            original.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
            original.Body = new MethodBody(original) { MaxStackSize = Math.Max(1, original.Parameters.Count) };
            ILProcessor il = original.Body.GetILProcessor();
            foreach (ParameterDefinition parameter in original.Parameters) il.Emit(OpCodes.Ldarg, parameter);
            il.Emit(OpCodes.Call, nativeApi.MainModule.ImportReference(proxy)); il.Emit(OpCodes.Ret);
            if (!original.HasBody || original.IsPInvokeImpl) throw new InvalidDataException("Native capture proxy is not a managed method: " + pair.Key);
            report.Modifications.Add("private build-time copied native ABI journal: " + original.FullName);
        }
    }
    internal static IEnumerable<string> Bind(AssemblyDefinition game, string helperPath, StandaloneReport report)
    {
        using var helper = AssemblyDefinition.ReadAssembly(helperPath, new ReaderParameters { InMemory = true });
        if (helper.Name.Name != "QuestProceduralExport") throw new InvalidDataException("Native export helper assembly identity changed.");
        TypeDefinition controller = helper.MainModule.GetType("GloomhavenVR.Procedural.ExportController")
            ?? throw new InvalidDataException("Native export controller is missing.");
        MethodDefinition Require(TypeDefinition type, string name, string result)
        {
            return type.Methods.SingleOrDefault(method => method.Name == name && method.ReturnType.FullName == result && method.Parameters.Count == 0)
                ?? throw new InvalidDataException("Native export helper or startup ABI changed: " + type.FullName + "::" + name);
        }
        MethodDefinition initialize = Require(controller, "Initialize", "System.Void");
        MethodDefinition startup = Require(controller, "LoadNativeStartup", "System.Collections.IEnumerator");
        if (!initialize.IsPublic || !initialize.IsStatic || !startup.IsPublic || !startup.IsStatic)
            throw new InvalidDataException("Native export helper methods must be public static entry points.");
        TypeDefinition bootstrap = game.MainModule.GetType("Bootstrap")
            ?? throw new InvalidDataException("Original native Bootstrap identity changed.");
        MethodDefinition start = Require(bootstrap, "Start", "System.Void");
        if (!start.HasBody || Discovery.Protected(bootstrap)) throw new InvalidDataException("Original export startup cannot be patched.");
        start.Body.GetILProcessor().InsertBefore(start.Body.Instructions[0],
            Instruction.Create(OpCodes.Call, game.MainModule.ImportReference(initialize)));
        MethodDefinition splash = Require(bootstrap, "ShowSplash", "System.Collections.IEnumerator");
        splash.Body = new MethodBody(splash);
        ILProcessor body = splash.Body.GetILProcessor();
        body.Emit(OpCodes.Call, game.MainModule.ImportReference(startup));
        body.Emit(OpCodes.Ret);
        report.InputAssemblies["QuestProceduralExport.dll"] = Standalone.Hash(helperPath);
        report.Modifications.Add("build-time native exporter before original startup: " + start.FullName);
        report.Modifications.Add("build-time movie bypass retains original content initialization: " + splash.FullName);
        TypeDefinition intro = game.MainModule.GetType("IntroPlayer") ?? throw new InvalidDataException("Original export IntroPlayer is missing.");
        MethodDefinition introStart = Require(intro, "Start", "System.Void");
        // Presentation alone is skipped in this headless conversion process.
        // All authored scene objects, singleton Awakes and input setup remain.
        introStart.Body = new MethodBody(introStart);
        introStart.Body.GetILProcessor().Emit(OpCodes.Ret);
        report.Modifications.Add("build-time movie-only bypass retains original Intro input singleton scene: " + introStart.FullName);
        return new[] { bootstrap.FullName, intro.FullName };
    }
}
