using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

/// <summary>Guildmaster uses original admission, save loading and validation.
/// Procedural execution/performance on the headset still requires hardware.</summary>
internal static class StandaloneGuildmasterTests
{
    internal static void Run(string projectRoot, Action<bool, string> check, bool requireOriginal = false)
    {
        string managed = Path.Combine(projectRoot, "ressources/GH_Data/Managed");
        string gamePath = Path.Combine(managed, "GH.Runtime.dll");
        if (!File.Exists(gamePath))
        {
            if (requireOriginal) throw new InvalidDataException("Original game input is required for the full-mode invariance proof.");
            Console.WriteLine("Original Guildmaster invariance proof skipped: owned game input is absent.");
            return;
        }
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(gamePath));
        string root = Path.Combine(Path.GetTempPath(), "quest-full-mode-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string profile = Path.Combine(root, "profile.json"), output = Path.Combine(root, "game");
            File.WriteAllText(profile, JsonSerializer.Serialize(new { schema = 1, provider = "steam", steamId = "0", accountId = 0,
                displayName = "Quest complete game test (DUMMY)", isDummy = true }));
            StandaloneReport report = Standalone.Write(managed, null, profile, output, target: "game");
            using var source = AssemblyDefinition.ReadAssembly(gamePath);
            using var actual = AssemblyDefinition.ReadAssembly(Path.Combine(output, "GH.Runtime.dll"));
            check(source.MainModule.Mvid == actual.MainModule.Mvid && report.StartupAdapterComplete
                && !report.FullGameReady, "Full original identity is retained without asserting headset engine/playability.");
            check(report.Modifications.All(m => !m.Contains("Guildmaster", StringComparison.Ordinal)),
                "Full-game conversion retains no excluded Guildmaster adapter.");
            check(ProtectedTypes.Verify(ProtectedTypes.Snapshot(source), actual) > 0,
                "Original transport/rules protected types remain unchanged.");
            check(report.UnchangedTypesVerified > 4000, "Full conversion verifies all unrelated original types.");
            CheckBodies(source, actual, check);
            // This runs the complete conversion above, not the former isolated
            // adapter. Intentional edits to each emitted admission/iterator seam
            // must fail the same original-source fingerprint comparison.
            foreach ((string type, string method) in new[] { ("GHClientCallbacks", "Connected"), ("SaveData", "LoadGuildmasterMode"),
                ("GlobalData", "ValidateSaves"), ("SaveData", "LoadRulebase") })
            {
                MethodDefinition target = Method(actual, type, method);
                Instruction mutation = Instruction.Create(OpCodes.Nop);
                target.Body.GetILProcessor().InsertBefore(target.Body.Instructions[0], mutation);
                try
                {
                    CheckBodies(source, actual, (ok, message) => { if (!ok) throw new InvalidOperationException(message); });
                    check(false, "Altered original mode/lifecycle seam escaped: " + type + "." + method);
                }
                catch (InvalidOperationException) { check(true, "Altered original mode/lifecycle seam rejected: " + type + "." + method); }
                finally { target.Body.Instructions.Remove(mutation); }
            }
            check(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(gamePath))), "Read-only original full-game input remains unchanged.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static MethodDefinition Method(AssemblyDefinition assembly, string type, string name) =>
        assembly.MainModule.GetType(type).Methods.Single(m => m.Name == name);

    static void CheckBodies(AssemblyDefinition source, AssemblyDefinition actual, Action<bool, string> check)
    {
        foreach (string type in new[] { "GHClientCallbacks", "GlobalData" })
            check(ProtectedTypes.Fingerprint(source.MainModule.GetType(type)) == ProtectedTypes.Fingerprint(actual.MainModule.GetType(type)),
                "Original complete-mode type changed: " + type);
        // Existing path adaptation replaces exactly one Unity dataPath read in
        // YMLLoading. Normalize only that already-proven IO call; all original
        // parser, mode selection, validation and unload code must remain exact.
        TypeDefinition originalYml = source.MainModule.GetType("YMLLoading"), emittedYml = actual.MainModule.GetType("YMLLoading");
        Instruction originalPath = originalYml.Methods.SelectMany(m => m.HasBody ? m.Body.Instructions : Enumerable.Empty<Instruction>())
            .Single(i => i.Operand is MethodReference call && call.DeclaringType.FullName == "UnityEngine.Application" && call.Name == "get_dataPath");
        Instruction emittedPath = emittedYml.Methods.SelectMany(m => m.HasBody ? m.Body.Instructions : Enumerable.Empty<Instruction>())
            .Single(i => i.Operand is MethodReference call && call.DeclaringType.FullName == "QuestGame.Compatibility.Paths" && call.Name == "get_dataPath");
        object adaptedPath = emittedPath.Operand;
        check(originalPath.OpCode == emittedPath.OpCode, "Only the existing native path operand is normalized.");
        emittedPath.Operand = originalPath.Operand;
        try { check(ProtectedTypes.Fingerprint(originalYml) == ProtectedTypes.Fingerprint(emittedYml), "Original parser/rule-mode type changed beyond its existing owned-path seam."); }
        finally { emittedPath.Operand = adaptedPath; }
        foreach (string name in new[] { "LoadGuildmasterMode", "LoadCampaignMode", "LoadRulebase" })
            check(Body(Method(source, "SaveData", name)) == Body(Method(actual, "SaveData", name)),
                "Original included-mode save/lifecycle body changed: SaveData." + name);
        foreach (string owner in new[] { "GHClientCallbacks", "GlobalData", "YMLLoading", "SaveData" })
            foreach (TypeDefinition nested in source.MainModule.GetType(owner).NestedTypes)
                check(ProtectedTypes.Fingerprint(nested) == ProtectedTypes.Fingerprint(actual.MainModule.GetType(owner).NestedTypes.Single(t => t.FullName == nested.FullName)),
                    "Original included-mode coroutine/closure changed: " + nested.FullName);
        var emittedCalls = Method(actual, "GHClientCallbacks", "Connected").Body.Instructions
            .Select(i => i.Operand).OfType<MethodReference>();
        check(!emittedCalls.Any(m => m.DeclaringType.FullName == "GloomhavenVR.Quest.QuestGameScope"),
            "Original mode admission retains no Guildmaster-unavailable callback.");
    }

    static string Body(MethodDefinition method)
    {
        if (!method.HasBody) return "no body";
        Instruction[] instructions = method.Body.Instructions.ToArray();
        string Operand(object? value) => value switch
        {
            Instruction i => "branch:" + Array.IndexOf(instructions, i),
            Instruction[] branches => "switch:" + string.Join(",", branches.Select(i => Array.IndexOf(instructions, i))),
            ParameterDefinition p => "param:" + p.Index,
            VariableDefinition v => "local:" + v.Index,
            MemberReference m => m.FullName,
            _ => value?.ToString() ?? ""
        };
        return string.Join("\n", instructions.Select(i => i.OpCode + " " + Operand(i.Operand)));
    }
}
