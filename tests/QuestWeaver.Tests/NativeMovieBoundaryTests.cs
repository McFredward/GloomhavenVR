using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class NativeMovieBoundaryTests
{
    internal static void Verify(Discovery model, string managed, Action<bool, string> check)
    {
        if (model.Mod.MainModule.GetType("GloomhavenVR.WorldUI.NativeVideoMovies") == null) return;
        using AssemblyDefinition original = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll"));
        AssemblyDefinition written = model.Loaded.Single(a => a.Name.Name == "GH.Runtime");
        foreach (var spec in new[] {
            (Type: "VideoCamera", Method: "PlayFullscreenVideo", Replacement: "SetNativeUrl", Name: true),
            (Type: "BackgroundView", Method: "Update", Replacement: "SetAmbientUrl", Name: false) })
        {
            MethodDefinition before = original.MainModule.GetType(spec.Type).Methods.Single(m => m.Name == spec.Method);
            MethodDefinition after = written.MainModule.GetType(spec.Type).Methods.Single(m => m.Name == spec.Method);
            check(before.ReturnType.FullName == after.ReturnType.FullName
                && before.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(after.Parameters.Select(p => p.ParameterType.FullName)),
                "Movie setter specialization changed the native admission signature.");
            check(before.Body.ExceptionHandlers.Count == after.Body.ExceptionHandlers.Count,
                "Movie setter specialization changed original exception regions.");
            MethodReference[] beforeCalls = Calls(before), afterCalls = Calls(after);
            int setter = Array.FindIndex(beforeCalls, c => c.DeclaringType.FullName == "UnityEngine.Video.VideoPlayer" && c.Name == "set_url");
            check(setter >= 0 && beforeCalls.Length == afterCalls.Length,
                "Movie setter specialization duplicated a native decoder/Play/callback call.");
            for (int i = 0; i < beforeCalls.Length; i++)
                check(i == setter ? afterCalls[i].DeclaringType.FullName == "GloomhavenVR.WorldUI.NativeVideoMovies"
                    && afterCalls[i].Name == spec.Replacement : afterCalls[i].FullName == beforeCalls[i].FullName,
                    "Movie setter specialization changed native call order or continuation.");
            Instruction site = after.Body.Instructions.Single(i => i.Operand is MethodReference c
                && c.DeclaringType.FullName == "GloomhavenVR.WorldUI.NativeVideoMovies" && c.Name == spec.Replacement);
            check(site.OpCode == OpCodes.Call && (!spec.Name || site.Previous?.OpCode == OpCodes.Ldarg_1),
                "Movie replacement lost its original requested name or static call ABI.");
            check(!after.Body.Instructions.Any(i => i.Operand is MethodReference c
                && c.DeclaringType.Namespace.StartsWith("System.Reflection.Emit", StringComparison.Ordinal)),
                "Movie replacement imported runtime code generation into the game.");
        }
        foreach (string name in new[] { "NativeVideoMoviePatch", "NativeAmbientMoviePatch" })
        {
            TypeDefinition patch = model.Mod.MainModule.GetType("GloomhavenVR.WorldUI." + name);
            check(patch.Methods.Count == 0 && patch.CustomAttributes.All(a => a.AttributeType.FullName != "HarmonyLib.HarmonyPatch"),
                "Qualified desktop movie transpiler survived into the AOT mod.");
        }
    }

    static MethodReference[] Calls(MethodDefinition method) => method.Body.Instructions
        .Where(i => i.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
        .Select(i => i.Operand).OfType<MethodReference>().ToArray();
}
