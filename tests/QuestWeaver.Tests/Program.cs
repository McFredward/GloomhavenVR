using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using QuestWeaver;

if (args.Length == 2 && args[0] == "--verify-next")
{
    string output = Path.GetFullPath(args[1]);
    var child = new AssemblyLoadContext("next-fixture", isCollectible: true);
    child.Resolving += (_, name) => name.Name == "QuestWeaver.Runtime" ? typeof(QuestWeaver.Runtime.Registry).Assembly
        : File.Exists(Path.Combine(output, name.Name + ".dll")) ? child.LoadFromAssemblyPath(Path.Combine(output, name.Name + ".dll")) : null;
    Assembly nextGame = child.LoadFromAssemblyPath(Path.Combine(output, "FixtureGame.dll"));
    Assembly nextMod = child.LoadFromAssemblyPath(Path.Combine(output, "FixtureMod.dll"));
    Type nextEntry = nextMod.GetType("FixtureMod.Entry")!;
    nextEntry.GetMethod("InstallDynamic")!.Invoke(null, null);
    nextEntry.GetMethod("InstallNext")!.Invoke(null, null);
    Type nextTarget = nextGame.GetType("FixtureGame.Target")!;
    int result = (int)nextTarget.GetMethod("Dynamic")!.Invoke(Activator.CreateInstance(nextTarget), new object[] { 2 })!;
    if (result != 28) throw new InvalidOperationException("N+1 changed behavior/new hook were not integrated: " + result);
    Console.WriteLine("N+1 executable fixture: changed behavior + new patch integrated without builder edits.");
    return;
}

string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
string fixtureDir = Path.Combine(projectRoot, "tests/QuestWeaver.Tests/FixtureMod/bin", configuration, "net8.0");
string temp = Path.Combine(Path.GetTempPath(), "quest-weaver-tests-" + Guid.NewGuid().ToString("N"));
int assertions = 0;
void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
try
{
    if (args.Length == 1 && args[0] == "--guildmaster-only")
    {
        StandaloneGuildmasterTests.Run(projectRoot, Check, requireOriginal: true);
        Console.WriteLine($"Quest Guildmaster admission: {assertions} assertions passed.");
        return;
    }
    if (args.Length == 1 && args[0] == "--type-lookup")
    {
        HarmonyLookupTests.Run(Path.Combine(temp, "type-lookup"), Check);
        Console.WriteLine($"Quest Harmony type lookup: {assertions} assertions passed.");
        return;
    }
    using (Discovery model = Discovery.Load(Path.Combine(fixtureDir, "FixtureMod.dll"), fixtureDir))
    {
        AuditReport report = model.Audit();
        Check(report.Issues.Count == 0, "Fixture unexpectedly blocked: " + string.Join(";", report.Issues));
        Check(report.Hooks.Count == 11 && report.FieldHelpers.Count == 1, "Fixture hook/helper coverage changed.");
        new Weaver(model).Write(temp, report, false);
        Check(report.Complete && report.WovenTargets == 3 && report.ProtectedTypesVerified == 1, "Fixture integration or protected-type invariance is incomplete.");
    }
    using (AssemblyDefinition facade = AssemblyDefinition.ReadAssembly(Path.Combine(temp, "0Harmony.dll")))
        Check(!facade.MainModule.AssemblyReferences.Any(a => a.Name.StartsWith("MonoMod", StringComparison.Ordinal) || a.Name.StartsWith("Mono.Cecil", StringComparison.Ordinal))
            && !facade.MainModule.GetTypeReferences().Any(t => t.Namespace.StartsWith("System.Reflection.Emit", StringComparison.Ordinal)), "Harmony facade retained an emit/detour dependency.");
    var context = new AssemblyLoadContext("woven-fixture", isCollectible: true);
    context.Resolving += (_, name) => name.Name == "QuestWeaver.Runtime" ? typeof(QuestWeaver.Runtime.Registry).Assembly
        : File.Exists(Path.Combine(temp, name.Name + ".dll")) ? context.LoadFromAssemblyPath(Path.Combine(temp, name.Name + ".dll")) : null;
    Assembly game = context.LoadFromAssemblyPath(Path.Combine(temp, "FixtureGame.dll"));
    Assembly mod = context.LoadFromAssemblyPath(Path.Combine(temp, "FixtureMod.dll"));
    Type targetType = game.GetType("FixtureGame.Target")!;
    object target = Activator.CreateInstance(targetType)!;
    MethodInfo calculate = targetType.GetMethod("Calculate")!;
    Type entry = mod.GetType("FixtureMod.Entry")!;
    var trace = (List<string>)targetType.GetField("Trace")!.GetValue(null)!;
    object?[] input = { 3 };
    Check((int)calculate.Invoke(target, input)! == 15 && (int)input[0]! == 4, "Inactive wrapper changed vanilla behavior.");
    entry.GetMethod("Install")!.Invoke(null, null); trace.Clear(); input[0] = 3;
    Check((int)calculate.Invoke(target, input)! == 29 && (int)input[0]! == 6, "State/result/ref argument behavior is wrong.");
    Check(string.Join(",", trace) == "prefix,simple-prefix,mutating-prefix,original,postfix:Calculate,finalizer,cleanup:ok", "Normal hook ordering is wrong.");
    trace.Clear(); input[0] = -1;
    Check((int)calculate.Invoke(target, input)! == 82 && (int)input[0]! == -2, "HarmonyX must run later mutating prefixes after a skipped original.");
    Check(string.Join(",", trace) == "prefix,simple-prefix,mutating-prefix,postfix:Calculate,finalizer,cleanup:ok", "Skipped original did not run all HarmonyX prefixes/postfix/finalizer.");
    trace.Clear(); input[0] = 901;
    Check((int)calculate.Invoke(target, input)! == 92, "Finalizer did not suppress original exception with preserved state.");
    Check(string.Join(",", trace) == "prefix,simple-prefix,mutating-prefix,original,finalizer,cleanup:ok", "Exceptional original should skip postfix.");
    input[0] = -201;
    try { calculate.Invoke(target, input); Check(false, "Original exception was lost."); }
    catch (TargetInvocationException e) { Check(e.InnerException is ArgumentException && e.InnerException.Message == "preserved-failure", "Wrong preserved exception."); }
    entry.GetField("FinalizerFailures")!.SetValue(null, 1); trace.Clear(); input[0] = 1;
    Check((int)calculate.Invoke(target, input)! == 94, "Finalizer failure did not enter the HarmonyX guarded recovery pass.");
    Check(string.Join(",", trace) == "prefix,simple-prefix,mutating-prefix,original,postfix:Calculate,finalizer,finalizer,cleanup:ok", "Finalizer recovery ordering or later cleanup is wrong.");
    entry.GetField("FinalizerFailures")!.SetValue(null, 1); trace.Clear(); input[0] = -201;
    try { calculate.Invoke(target, input); Check(false, "Guarded finalizer failure suppressed the original error."); }
    catch (TargetInvocationException e) { Check(e.InnerException is ArgumentException, "Secondary finalizer failure replaced the original error."); }
    Check(trace.Last() == "cleanup:ArgumentException", "Later cleanup did not run after a guarded finalizer failed.");
    entry.GetMethod("SetCounter")!.Invoke(null, new object[] { target, 40 });
    Check((int)targetType.GetProperty("Counter")!.GetValue(target)! == 40, "Generated FieldRef delegate did not mutate the private field.");
    Check(HarmonyLib.AccessTools.Field(targetType, "counter") != null && HarmonyLib.AccessTools.Method(targetType, "Calculate", new[] { typeof(int).MakeByRefType() }) != null, "Facade ordinary private-field/overload reflection differs.");
    entry.GetMethod("Remove")!.Invoke(null, null); trace.Clear(); input[0] = 1;
    Check((int)calculate.Invoke(target, input)! == 43, "UnpatchSelf did not restore vanilla behavior.");
    Check(string.Join(",", trace) == "original", "Inactive hooks remained active.");
    entry.GetMethod("InstallDynamic")!.Invoke(null, null);
    Check((int)targetType.GetMethod("Dynamic")!.Invoke(target, new object[] { 2 })! == 14, "Metadata-derived dynamic target did not run compiled hooks.");
    Check((int)entry.GetField("Preparations")!.GetValue(null)! == 1 && (int)entry.GetField("Selections")!.GetValue(null)! == 1, "Prepare/TargetMethod side effects were lost.");
    entry.GetMethod("InstallDirect")!.Invoke(null, null); input[0] = 2;
    Check((int)targetType.GetMethod("Direct")!.Invoke(target, input)! == 11 && (int)input[0]! == 9, "Direct custom-named prefix/postfix/finalizer registration failed.");
    entry.GetMethod("Remove")!.Invoke(null, null);
    Check((int)targetType.GetMethod("Dynamic")!.Invoke(target, new object[] { 2 })! == 4, "Dynamic target remained active after UnpatchSelf.");
    // A real negative control removes the referenced private field in a fresh input copy.
    string broken = temp + "-broken"; Directory.CreateDirectory(broken);
    File.Copy(Path.Combine(fixtureDir, "FixtureMod.dll"), Path.Combine(broken, "FixtureMod.dll"));
    using (AssemblyDefinition original = AssemblyDefinition.ReadAssembly(Path.Combine(fixtureDir, "FixtureGame.dll")))
    { var t = original.MainModule.GetType("FixtureGame.Target"); t.Fields.First(f => f.Name == "counter").Name = "renamedCounter"; original.Write(Path.Combine(broken, "FixtureGame.dll")); }
    using (Discovery bad = Discovery.Load(Path.Combine(broken, "FixtureMod.dll"), broken))
        Check(bad.Audit().Issues.Any(i => i.Code == "FIELD_HELPER_UNSUPPORTED"), "Missing-field negative control did not fail.");
    File.Copy(Path.Combine(fixtureDir, "FixtureGame.dll"), Path.Combine(broken, "FixtureGame.dll"), true);
    using (AssemblyDefinition altered = AssemblyDefinition.ReadAssembly(Path.Combine(fixtureDir, "FixtureMod.dll")))
    {
        MethodDefinition install = altered.MainModule.GetType("FixtureMod.Entry").Methods.First(m => m.Name == "InstallDirect");
        install.Body.Instructions.First(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && (string)i.Operand == "ArbitraryName").Operand = "MissingPatch";
        altered.Write(Path.Combine(broken, "FixtureMod.dll"));
    }
    using (Discovery bad = Discovery.Load(Path.Combine(broken, "FixtureMod.dll"), broken))
        Check(bad.Audit().Issues.Any(i => i.Code == "DIRECT_REGISTRATION_UNRESOLVED"), "Missing direct patch was silently skipped.");
    using (AssemblyDefinition altered = AssemblyDefinition.ReadAssembly(Path.Combine(fixtureDir, "FixtureMod.dll")))
    {
        altered.MainModule.GetType("FixtureMod.CalculatePatch").Methods.First(m => m.Name == "Prefix").Parameters[0].Name = "__unsupported";
        altered.Write(Path.Combine(broken, "FixtureMod.dll"));
    }
    using (Discovery bad = Discovery.Load(Path.Combine(broken, "FixtureMod.dll"), broken))
        Check(bad.Audit().Issues.Any(i => i.Code == "HOOK_UNSUPPORTED"), "Unknown injected argument was silently accepted.");
    using (AssemblyDefinition altered = AssemblyDefinition.ReadAssembly(Path.Combine(fixtureDir, "FixtureMod.dll")))
    {
        var access = altered.MainModule.GetTypeReferences().First(t => t.FullName == "HarmonyLib.AccessTools");
        var unknown = new MethodReference("UnportedApi", altered.MainModule.TypeSystem.Void, access);
        var function = new MethodDefinition("Unsupported", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, altered.MainModule.TypeSystem.Void);
        altered.MainModule.GetType("FixtureMod.Entry").Methods.Add(function);
        function.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Call, unknown); function.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
        altered.Write(Path.Combine(broken, "FixtureMod.dll"));
    }
    using (Discovery bad = Discovery.Load(Path.Combine(broken, "FixtureMod.dll"), broken))
        Check(bad.Audit().Issues.Any(i => i.Code == "HARMONY_FACADE_API"), "Unknown facade API was silently accepted.");
    using (Discovery twice = Discovery.Load(Path.Combine(temp, "FixtureMod.dll"), temp))
        Check(twice.Audit().Issues.Any(i => i.Code == "ALREADY_WOVEN"), "Already integrated mod input was silently accepted.");
    using (AssemblyDefinition protectedAssembly = AssemblyDefinition.ReadAssembly(Path.Combine(fixtureDir, "FixtureGame.dll")))
    {
        Dictionary<string, string> snapshot = ProtectedTypes.Snapshot(protectedAssembly);
        MethodDefinition immutable = protectedAssembly.MainModule.GetType("FFSNet.NetworkManager").Methods.First(m => m.Name == "Immutable");
        immutable.Body.Instructions.First(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_2).OpCode = Mono.Cecil.Cil.OpCodes.Ldc_I4_3;
        try { ProtectedTypes.Verify(snapshot, protectedAssembly); Check(false, "Protected-method mutation was accepted."); }
        catch (InvalidDataException) { Check(true, "Protected mutation rejected."); }
    }
    Directory.Delete(broken, true);
    string nextInput = temp + "-next-input", nextOutput = temp + "-next-output";
    try
    {
        var build = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) != "dotnet")
            build.FileName = Path.Combine(new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName, "dotnet");
        build.ArgumentList.Add("build"); build.ArgumentList.Add(Path.Combine(projectRoot, "tests/QuestWeaver.Tests/FixtureMod/FixtureMod.csproj"));
        build.ArgumentList.Add("--configuration"); build.ArgumentList.Add(configuration); build.ArgumentList.Add("--no-restore");
        build.ArgumentList.Add("-p:DefineConstants=QUEST_FIXTURE_NEXT"); build.ArgumentList.Add("-p:OutputPath=" + nextInput + Path.DirectorySeparatorChar);
        using (var process = System.Diagnostics.Process.Start(build)!)
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
            Check(process.ExitCode == 0, "N+1 fixture compilation failed: " + stdout + stderr);
        }
        using (Discovery next = Discovery.Load(Path.Combine(nextInput, "FixtureMod.dll"), nextInput))
        {
            AuditReport nextReport = next.Audit();
            Check(nextReport.Issues.Count == 0 && nextReport.Hooks.Count == 12, "N+1 discovery required a hand-edited patch list.");
            new Weaver(next).Write(nextOutput, nextReport, false);
        }
        var verify = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") verify.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        verify.ArgumentList.Add("--verify-next"); verify.ArgumentList.Add(nextOutput);
        using (var process = System.Diagnostics.Process.Start(verify)!)
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
            Check(process.ExitCode == 0 && stdout.Contains("N+1 executable fixture", StringComparison.Ordinal), "N+1 executable behavior failed: " + stdout + stderr);
        }
    }
    finally
    {
        if (Directory.Exists(nextInput)) Directory.Delete(nextInput, true);
        if (Directory.Exists(nextOutput)) Directory.Delete(nextOutput, true);
    }
    context.Unload();
    HarmonyLookupTests.Run(Path.Combine(temp, "type-lookup"), Check);
    StartupTests.Run(projectRoot, Check);
    DlcTests.Run(projectRoot, Check);
    StandaloneProfileTests.Run(projectRoot, Check);
    StandaloneCampaignTests.Run(projectRoot, Path.Combine(temp, "native-campaign"), Check);
    StandaloneOdinTests.Run(projectRoot, Check);
    StandaloneGuildmasterTests.Run(projectRoot, Check);
    StandaloneExportTests.Run(projectRoot, Path.Combine(temp, "native-export"), Check);
    PackageApiTests.Run(temp + "-package-api", Check);
    Console.WriteLine($"QuestWeaver executable fixture: {assertions} assertions passed.");
}
finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
