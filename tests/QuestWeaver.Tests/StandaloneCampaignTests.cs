using System.Text.Json;
using Mono.Cecil;
using QuestWeaver;

internal static class StandaloneCampaignTests
{
    internal static void Run(string projectRoot, string root, Action<bool, string> check)
    {
        string managed = Path.Combine(projectRoot, "ressources/GH_Data/Managed");
        if (!File.Exists(Path.Combine(managed, "Apparance.Net.dll"))) return;
        Directory.CreateDirectory(root);
        string profile = Path.Combine(root, "profile.json");
        File.WriteAllText(profile, JsonSerializer.Serialize(new {
            schema = 1, provider = "steam", steamId = "0", accountId = 0,
            displayName = "Quest Campaign test (DUMMY)", isDummy = true
        }));
        string output = Path.Combine(root, "game");
        StandaloneReport report = Standalone.Write(managed, null, profile, output, target: "game");
        check(report.StartupAdapterComplete && report.Scope == "campaign-local-platform" && !report.FullGameReady,
            "Full target generates its real platform seam without claiming package or hardware readiness.");
        using var original = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Apparance.Net.dll"));
        using var candidate = AssemblyDefinition.ReadAssembly(Path.Combine(output, "Apparance.Net.dll"));
        var before = original.MainModule.GetType("Apparance.Net.Interop").Methods.Where(m => m.IsPInvokeImpl).ToArray();
        var after = candidate.MainModule.GetType("Apparance.Net.Interop").Methods.Where(m => m.IsPInvokeImpl).ToArray();
        check(before.Length == 12 && after.Length == 12, "All12 original native entry points are retained.");
        foreach (var native in before)
        {
            var rebound = after.Single(m => m.FullName == native.FullName);
            check(rebound.PInvokeInfo.Module.Name == "QuestApparance" && rebound.PInvokeInfo.EntryPoint == native.PInvokeInfo.EntryPoint
                && rebound.PInvokeInfo.Attributes == native.PInvokeInfo.Attributes && rebound.Attributes == native.Attributes,
                "Original native signature, entry point, calling convention and marshaling flags are retained: " + native.Name);
        }
        var originalEngine = original.MainModule.GetType("Apparance.Net.Engine");
        var emittedEngine = candidate.MainModule.GetType("Apparance.Net.Engine");
        foreach (var method in originalEngine.Methods.Where(m => m.HasBody))
            check(Body(method) == Body(emittedEngine.Methods.Single(m => m.FullName == method.FullName)),
                "Original managed procedural decoding/callback lifecycle remains intact: " + method.Name);
        var log = emittedEngine.Methods.Single(m => m.Name == "LogHandler");
        check(log.IsStatic && log.CustomAttributes.Any(a => a.AttributeType.Name == "MonoPInvokeCallback"),
            "Original reverse native callback retains its static AOT attribute and delegate lifetime.");
        using var unity = AssemblyDefinition.ReadAssembly(Path.Combine(output, "Apparance.Unity.dll"));
        var procedure = unity.MainModule.GetType("ApparanceEngine").Methods.Single(m => m.Name == "get_ProceduresDirectory");
        check(procedure.Body.Instructions.Any(i => i.Operand is MethodReference c
            && c.DeclaringType.FullName == "QuestGame.Compatibility.Paths" && c.Name == "get_streamingAssetsPath"),
            "Original procedural graph lookup uses the initialized owned content root.");
        foreach (string lifecycle in new[] { "ILifecycle.ApplicationStart", "StartEngine", "Update", "OnApplicationQuit" })
        {
            var method = unity.MainModule.GetType("ApparanceEngine").Methods.Single(m => m.Name == lifecycle);
            check(method.HasBody && method.Body.Instructions.Count > 1,
                "Full target retains actual native generation lifecycle: " + lifecycle);
        }
        check(report.UnchangedTypesVerified > 4000 && report.Modifications.Any(m => m.Contains("exactly12 original native exports")),
            "Full conversion verifies unrelated original types and documents only the native seam.");
    }
    static string Body(MethodDefinition method) => string.Join("\n", method.Body.Instructions.Select(i => i.OpCode + " " + i.Operand));
}
