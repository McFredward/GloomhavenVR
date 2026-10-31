using System.Text.Json;
using Mono.Cecil;
using QuestWeaver;

internal static class TargetMetadataTests
{
    internal static void Run(string projectRoot, string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        string targetPath = Path.Combine(root, "Target.dll");
        using (var target = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Target", new Version(1, 0)), "Target.dll", ModuleKind.Dll))
        {
            var local = new TypeDefinition("Owned", "Local", TypeAttributes.Public, target.MainModule.TypeSystem.Object);
            target.MainModule.Types.Add(local);
            target.Write(targetPath);
        }
        using (var baseline = AssemblyDefinition.ReadAssembly(targetPath))
        using (var hook = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("BaselineHook", new Version(1, 0)), "BaselineHook.dll", ModuleKind.Dll))
        {
            var local = new TypeReference("Owned", "Local", hook.MainModule, AssemblyNameReference.Parse(baseline.Name.FullName));
            baseline.MainModule.ImportReference(local);
            check(baseline.MainModule.AssemblyReferences.Any(r => r.FullName == baseline.Name.FullName),
                "Negative control must reproduce Cecil's Unity-rejected self AssemblyRef.");
            baseline.MainModule.ImportReference(typeof(QuestWeaver.Runtime.OfflineProfile).GetMethod("DisplayName")!);
            check(baseline.MainModule.AssemblyReferences.Any(r => r.Name == "System.Private.CoreLib"),
                "Negative control must reproduce reflection leaking the build host core library.");
        }
        using (var target = AssemblyDefinition.ReadAssembly(targetPath, new ReaderParameters { MetadataImporterProvider = TargetMetadata.Importer }))
        using (var hook = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Hook", new Version(1, 0)), "Hook.dll", ModuleKind.Dll))
        {
            var scope = AssemblyNameReference.Parse(target.Name.FullName);
            var local = new TypeReference("Owned", "Local", hook.MainModule, scope);
            var hookType = new TypeDefinition("Fixture", "Hook", TypeAttributes.Public, hook.MainModule.TypeSystem.Object);
            hook.MainModule.Types.Add(hookType);
            var method = new MethodDefinition("Postfix", MethodAttributes.Public | MethodAttributes.Static, local);
            method.Parameters.Add(new ParameterDefinition(new ByReferenceType(new ArrayType(local))));
            hookType.Methods.Add(method);
            var imported = target.MainModule.ImportReference(method);
            check(imported.ReturnType.Scope == target.MainModule && imported.Parameters[0].ParameterType.GetElementType().Scope == target.MainModule,
                "Imported hook return and nested parameter type scopes must bind to their own module.");
            check(!target.MainModule.AssemblyReferences.Any(r => r.FullName == target.Name.FullName),
                "Importing a mod signature must not introduce the Unity-rejected self AssemblyRef.");
            var foreign = new TypeReference("Owned", "Local", hook.MainModule, new AssemblyNameReference("Target", new Version(2, 0)));
            var external = target.MainModule.ImportReference(foreign);
            check(external.Scope is AssemblyNameReference reference && reference.Version == new Version(2, 0, 0, 0),
                "Same-named foreign assembly versions must not be guessed into the local module: " + external.Scope + " vs " + target.Name.FullName);
            var runtime = TargetMetadata.RuntimeMethod(target.MainModule, "QuestWeaver.Runtime.OfflineProfile", "DisplayName");
            check(runtime.ReturnType.Scope.Name != "System.Private.CoreLib" && runtime.Parameters.All(p => p.ParameterType.Scope.Name != "System.Private.CoreLib")
                && !target.MainModule.AssemblyReferences.Any(r => r.Name == "System.Private.CoreLib"),
                "Offline profile imports must retain target netstandard metadata, never the build host core library.");
        }
        string managed = Path.Combine(projectRoot, "ressources/GH_Data/Managed");
        if (!File.Exists(Path.Combine(managed, "GH.Runtime.dll")))
        {
            Console.WriteLine("SKIP actual target CLR plugin audit: private owned-game assemblies are unavailable; metadata importer negative controls remain active.");
            return;
        }
        string profile = Path.Combine(root, "profile.json"), output = Path.Combine(root, "standalone");
        File.WriteAllText(profile, JsonSerializer.Serialize(new { schema = 1, provider = "steam", steamId = "0", accountId = 0,
            displayName = "Target import fixture (DUMMY)", isDummy = true }));
        var report = Standalone.Write(managed, null, profile, output, target: "game");
        check(report.StartupAdapterComplete && report.UnchangedTypesVerified > 4000, "Target import repair must retain original standalone invariance gates.");
        foreach (string path in Directory.EnumerateFiles(output, "*.dll"))
        {
            using var result = AssemblyDefinition.ReadAssembly(path);
            check(!result.MainModule.AssemblyReferences.Any(r => r.Name == "System.Private.CoreLib" || r.FullName == result.Name.FullName),
                "Actual standalone plugin contains a host-core or self dependency: " + Path.GetFileName(path));
        }
    }
}
