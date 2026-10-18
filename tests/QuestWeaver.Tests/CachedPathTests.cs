using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

internal static class CachedPathTests
{
    private static readonly string[] Getters = { "get_dataPath", "get_streamingAssetsPath", "get_persistentDataPath" };

    internal static void Run(string temp, Action<bool, string> check)
    {
        string directory = Path.Combine(temp, "cached-paths"); Directory.CreateDirectory(directory);
        string library = Path.Combine(directory, "QuestGame.Compatibility.dll");
        using (ModuleDefinition game = ModuleDefinition.CreateModule("PathFixture", ModuleKind.Dll))
        using (AssemblyDefinition generated = PathsCompatibility.Create(game))
        {
            TypeDefinition paths = generated.MainModule.GetType("QuestGame.Compatibility.Paths");
            check(!generated.MainModule.AssemblyReferences.Any(a => a.Name.StartsWith("Unity", StringComparison.Ordinal))
                && !paths.Methods.Any(m => m.IsConstructor), "Pure managed paths retained Unity dependencies or a hidden initializer.");
            check(paths.Fields.Count == 1 && paths.Fields[0].IsPrivate && paths.Fields[0].IsStatic,
                "Path cache has publicly writable or independently published roots.");
            check(Getters.All(name => paths.Methods.Single(m => m.Name == name).Body.Instructions.All(i => i.Operand is not MethodReference call
                || call.DeclaringType.FullName != "UnityEngine.Application")), "A cached path getter retained a Unity fallback.");
            check(paths.Methods.Single(m => m.Name == "Initialize").Body.Instructions.Count(i => i.Operand is MethodReference call
                && call.DeclaringType.FullName == "System.Threading.Interlocked" && call.Name == "CompareExchange") == 1,
                "Path snapshot was not published atomically exactly once.");
            generated.Write(library);
            MetadataControls(game, paths, check);
        }
        int mainThread = Environment.CurrentManagedThreadId;
        using (var fixture = new RuntimeFixture(library))
        {
            foreach (string getter in Getters)
            {
                Exception error = Task.Run(() => Capture(() => fixture.Call(getter))).GetAwaiter().GetResult();
                check(error is InvalidOperationException && error.Message.Contains("Paths.Initialize", StringComparison.Ordinal),
                    "Uninitialized worker getter queried Unity or silently selected a root: " + getter);
            }
            foreach (string? invalid in new string?[] { null, "", "  ", "relative", "jar:file:///application.apk!/assets", "file:///tmp/save", "/tmp/content://foreign", "/tmp/invalid\0path" })
            {
                Exception rejected = Capture(() => fixture.Initialize(invalid));
                check(rejected is ArgumentException,
                    "Non-local, relative, or invalid path initialized the cache: " + invalid + " -> " + rejected);
                check(Capture(() => fixture.Call(Getters[0])) is InvalidOperationException,
                    "Rejected initialization published a partial snapshot.");
            }
            string persistent = Path.Combine(directory, "fresh Ü owner");
            check(!Directory.Exists(persistent), "Fresh-path fixture existed before initialization.");
            fixture.Initialize(persistent);
            string[] expected = { Path.Combine(persistent, "quest-owned-game"), Path.Combine(persistent, "quest-owned-game", "StreamingAssets"), persistent };
            check(Read(fixture).SequenceEqual(expected), "Cached aliases changed the original owned-content/save root layout.");
            check(!Directory.Exists(persistent), "Pure path initialization performed filesystem mutations or required existing files.");
            fixture.Initialize(Path.Combine(persistent, "."));
            check(Read(fixture).SequenceEqual(expected), "Equivalent normalized initialization changed the snapshot.");
            string rulebase = Path.Combine(expected[1], "Rulebase"); Directory.CreateDirectory(rulebase);
            File.WriteAllText(Path.Combine(rulebase, "rules.dat"), "original verified rule payload");
            Task<(int Thread, bool Complete, string Payload)>[] reads = Enumerable.Range(0, 48).Select(_ => Task.Run(() =>
            {
                bool complete = true;
                for (int i = 0; i < 32; i++) complete &= Read(fixture).SequenceEqual(expected);
                return (Environment.CurrentManagedThreadId, complete, File.ReadAllText(Path.Combine(fixture.Call(Getters[1]), "Rulebase", "rules.dat")));
            })).ToArray();
            Task.WaitAll(reads);
            foreach (Task<(int Thread, bool Complete, string Payload)> read in reads)
                check(read.Result.Thread != mainThread && read.Result.Complete && read.Result.Payload == "original verified rule payload",
                    "Background path/owned-rule IO failed or observed an inconsistent snapshot.");
            check(Capture(() => fixture.Initialize(Path.Combine(directory, "other owner"))) is InvalidOperationException,
                "Different save/content roots were accepted after initialization.");
            check(Read(fixture).SequenceEqual(expected), "Rejected root replacement changed initialized paths.");
            string absoluteScreenshot = Path.Combine(persistent, "ErrorSceenCap.png");
            string filename = fixture.Call("GetMobileScreenshotFilename", absoluteScreenshot);
            check(filename == "ErrorSceenCap.png" && Path.Combine(persistent, filename) == absoluteScreenshot,
                "Mobile screenshot argument still duplicates the persistent root or changes its original filename.");
            foreach (string? invalid in new[] { null, "ErrorSceenCap.png", Path.Combine(directory, "other owner", "ErrorSceenCap.png"), Path.Combine(persistent, "other.png") })
                check(Capture(() => fixture.Call("GetMobileScreenshotFilename", invalid)) is ArgumentException,
                    "Unknown screenshot destination was silently reinterpreted: " + invalid);
        }
        using (var race = new RuntimeFixture(library))
        {
            var start = new ManualResetEventSlim(false);
            Task<(string Root, Exception? Error)>[] attempts = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
            {
                string root = Path.Combine(directory, "raced-owner-" + i); start.Wait();
                try { race.Initialize(root); return (root, (Exception?)null); }
                catch (Exception error) { return (root, error); }
            })).ToArray();
            start.Set(); Task.WaitAll(attempts); start.Dispose();
            var winners = attempts.Where(a => a.Result.Error == null).Select(a => a.Result.Root).ToArray();
            check(winners.Length == 1 && attempts.Where(a => a.Result.Error != null).All(a => a.Result.Error is InvalidOperationException),
                "Concurrent initialization published multiple roots or failed ambiguously.");
            string winner = winners.Single();
            check(Read(race).SequenceEqual(new[] { Path.Combine(winner, "quest-owned-game"), Path.Combine(winner, "quest-owned-game", "StreamingAssets"), winner }),
                "Racing initialization produced mixed root aliases.");
            race.Initialize(winner);
        }
        LegacyWorkerControl(directory, mainThread, check);
        Console.WriteLine("Cached-path fixture: 48 real worker readers / 1,536 snapshot rounds, explicit pre-init failures, immutable publication, mobile screenshot argument and ABI negative controls passed.");
    }

    private static void LegacyWorkerControl(string directory, int mainThread, Action<bool, string> check)
    {
        string library = Path.Combine(directory, "legacy-main-thread-control.dll");
        string persistent = Path.Combine(directory, "legacy owner");
        using ModuleDefinition game = ModuleDefinition.CreateModule("LegacyFixture", ModuleKind.Dll);
        using AssemblyDefinition generated = PathsCompatibility.Create(game);
        ModuleDefinition module = generated.MainModule;
        TypeDefinition paths = module.GetType("QuestGame.Compatibility.Paths");
        var application = new TypeDefinition("UnityEngine", "Application", TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(application);
        var native = new MethodDefinition("get_persistentDataPath", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.String);
        application.Methods.Add(native);
        ILProcessor guard = native.Body.GetILProcessor();
        Instruction ready = Instruction.Create(OpCodes.Ldstr, persistent);
        guard.Emit(OpCodes.Call, module.ImportReference(typeof(Environment).GetProperty(nameof(Environment.CurrentManagedThreadId))!.GetMethod!));
        guard.Emit(OpCodes.Ldc_I4, mainThread); guard.Emit(OpCodes.Beq, ready);
        guard.Emit(OpCodes.Ldstr, "get_persistentDataPath can only be called from the main thread.");
        guard.Emit(OpCodes.Newobj, module.ImportReference(typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })!));
        guard.Emit(OpCodes.Throw); guard.Append(ready); guard.Emit(OpCodes.Ret);
        MethodDefinition getter = paths.Methods.Single(m => m.Name == "get_streamingAssetsPath");
        MethodReference combine = paths.Methods.Single(m => m.Name == "Initialize").Body.Instructions.Select(i => i.Operand)
            .OfType<MethodReference>().First(m => m.DeclaringType.FullName == "System.IO.Path" && m.Name == "Combine");
        // Exact B614 getter sequence, with a fixture native boundary enforcing the
        // same main-thread rule reported by the headset. This is a negative control
        // for the worker test, not a claim to execute Unity's native getter on .NET.
        getter.Body = new Mono.Cecil.Cil.MethodBody(getter);
        ILProcessor old = getter.Body.GetILProcessor();
        old.Emit(OpCodes.Call, native); old.Emit(OpCodes.Ldstr, "quest-owned-game"); old.Emit(OpCodes.Call, combine);
        old.Emit(OpCodes.Ldstr, "StreamingAssets"); old.Emit(OpCodes.Call, combine); old.Emit(OpCodes.Ret);
        generated.Write(library);
        using var legacy = new RuntimeFixture(library);
        legacy.Initialize(persistent);
        check(legacy.Call("get_streamingAssetsPath") == Path.Combine(persistent, "quest-owned-game", "StreamingAssets"),
            "Legacy negative control failed even on its permitted main thread.");
        Exception error = Task.Run(() => Capture(() => legacy.Call("get_streamingAssetsPath"))).GetAwaiter().GetResult();
        check(error is InvalidOperationException && error.Message == "get_persistentDataPath can only be called from the main thread.",
            "Background fixture failed to reject the exact legacy Unity getter path.");
    }

    private static void MetadataControls(ModuleDefinition game, TypeDefinition paths, Action<bool, string> check)
    {
        var owner = new TypeDefinition("", "Caller", TypeAttributes.Public, game.TypeSystem.Object); game.Types.Add(owner);
        MethodDefinition Getter(string name, string typeName = "UnityEngine.Application")
        {
            var method = new MethodDefinition(name + "Caller", MethodAttributes.Public | MethodAttributes.Static, game.TypeSystem.String);
            owner.Methods.Add(method);
            var called = new MethodReference(name, game.TypeSystem.String, new TypeReference("UnityEngine", typeName.Split('.').Last(), game, game.TypeSystem.CoreLibrary));
            method.Body.GetILProcessor().Emit(OpCodes.Call, called); method.Body.GetILProcessor().Emit(OpCodes.Ret);
            return method;
        }
        MethodDefinition[] getters = Getters.Select(name => Getter(name)).ToArray();
        MethodDefinition unrelated = Getter("get_productName");
        var protectedType = new TypeDefinition("FFSNet", "NetworkManager", TypeAttributes.Public, game.TypeSystem.Object); game.Types.Add(protectedType);
        MethodDefinition protectedCall = Getter("get_streamingAssetsPath"); owner.Methods.Remove(protectedCall); protectedType.Methods.Add(protectedCall);
        string protectedBefore = ProtectedTypes.Fingerprint(protectedType);
        var changed = new HashSet<string>(); var modifications = new List<string>();
        PathsCompatibility.RebindCalls(game, paths, changed, modifications);
        check(getters.All(m => ((MethodReference)m.Body.Instructions[0].Operand).DeclaringType.FullName == paths.FullName)
            && modifications.Count == 3 && changed.SetEquals(new[] { "Caller" }), "Supported path ABI was not rebound exactly at its original callsites.");
        check(((MethodReference)unrelated.Body.Instructions[0].Operand).DeclaringType.FullName == "UnityEngine.Application"
            && ProtectedTypes.Fingerprint(protectedType) == protectedBefore, "Path rebind changed unrelated Unity API or protected network code.");
        foreach (Action<MethodDefinition, MethodReference> mutate in new Action<MethodDefinition, MethodReference>[]
        {
            (_, call) => call.ReturnType = game.TypeSystem.Int32,
            (_, call) => call.Parameters.Add(new ParameterDefinition(game.TypeSystem.String)),
            (_, call) => call.HasThis = true,
            (method, _) => method.Body.Instructions[0].OpCode = OpCodes.Callvirt,
            (_, call) => call.GenericParameters.Add(new GenericParameter("T", call))
        })
        {
            MethodDefinition unknown = Getter("get_dataPath"); mutate(unknown, (MethodReference)unknown.Body.Instructions[0].Operand);
            check(Capture(() => PathsCompatibility.RebindCalls(game, paths, changed, modifications)) is InvalidDataException,
                "Changed filesystem getter ABI was accepted.");
            owner.Methods.Remove(unknown);
        }
    }

    internal static void RunOriginal(string managed, string adapted, string temp, Action<bool, string> check)
    {
        using var original = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll"));
        using var emitted = AssemblyDefinition.ReadAssembly(Path.Combine(adapted, "GH.Runtime.dll"));
        using var compatibility = AssemblyDefinition.ReadAssembly(Path.Combine(adapted, "QuestGame.Compatibility.dll"));
        TypeDefinition paths = compatibility.MainModule.GetType("QuestGame.Compatibility.Paths");
        MethodDefinition Screenshot(AssemblyDefinition assembly) => Discovery.AllTypes(assembly.MainModule)
            .Single(t => t.DeclaringType?.FullName == "GloomUtility" && t.Name.StartsWith("<TakeErrorScreenshot>", StringComparison.Ordinal))
            .Methods.Single(m => m.Name == "MoveNext");
        MethodDefinition screenshot = Screenshot(emitted);
        Instruction[] hooks = screenshot.Body.Instructions.Where(i => i.Operand is MethodReference call
            && call.DeclaringType.FullName == paths.FullName && call.Name == "GetMobileScreenshotFilename").ToArray();
        check(hooks.Length == 1 && hooks[0].Previous?.Operand is MethodReference originalPath && originalPath.DeclaringType.FullName == "RootSaveData"
            && originalPath.Name == "get_ScreenCaptureImagePath" && hooks[0].Next?.Operand is MethodReference capture
            && capture.DeclaringType.FullName == "UnityEngine.ScreenCapture" && capture.Name == "CaptureScreenshot",
            "Screenshot bridge was not inserted at the single original absolute-path/capture boundary.");
        screenshot.Body.GetILProcessor().Remove(hooks.Single());
        check(ProtectedTypes.Fingerprint(screenshot.DeclaringType) == ProtectedTypes.Fingerprint(Screenshot(original).DeclaringType),
            "Screenshot coroutine changed beyond its mobile filename argument.");
        MethodDefinition Alias(AssemblyDefinition assembly) => assembly.MainModule.GetType("RootSaveData").Methods.Single(m => m.Name == "get_ScreenCaptureImagePath");
        check(Alias(original).Body.Instructions.Select(InstructionText).SequenceEqual(Alias(emitted).Body.Instructions.Select(InstructionText)),
            "Original absolute screenshot/save alias changed.");
        int mapped = Discovery.AllTypes(emitted.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Count(i => i.Operand is MethodReference call && call.DeclaringType.FullName == paths.FullName && Getters.Contains(call.Name));
        int originals = Discovery.AllTypes(original.MainModule).Where(t => !Discovery.Protected(t)).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Count(i => i.Operand is MethodReference call && call.DeclaringType.FullName == "UnityEngine.Application" && Getters.Contains(call.Name));
        check(mapped == originals && !Discovery.AllTypes(emitted.MainModule).Where(t => !Discovery.Protected(t)).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference call && call.DeclaringType.FullName == "UnityEngine.Application" && Getters.Contains(call.Name)),
            "Original storage/content Unity getters were left behind or new path callsites were introduced.");
        int initializerCalls = Discovery.AllTypes(original.MainModule).SelectMany(t => t.Methods).Where(m => m.IsConstructor && m.IsStatic && m.HasBody)
            .SelectMany(m => m.Body.Instructions).Count(i => i.Operand is MethodReference call && call.DeclaringType.FullName == "UnityEngine.Application" && Getters.Contains(call.Name));
        ExecuteOriginalRulePath(emitted, adapted, temp, check);
        foreach (MethodDefinition other in Discovery.AllTypes(original.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody && m.DeclaringType.FullName != Screenshot(original).DeclaringType.FullName
            && m.Body.Instructions.Any(i => i.Operand is MethodReference call && call.DeclaringType.FullName == "UnityEngine.ScreenCapture")))
        {
            MethodDefinition after = emitted.MainModule.GetType(other.DeclaringType.FullName).Methods.Single(m => m.FullName == other.FullName);
            check(other.Body.Instructions.Select(InstructionText).SequenceEqual(after.Body.Instructions.Select(InstructionText)),
                "Unrelated screenshot API changed: " + other.FullName);
        }
        foreach (Action<MethodDefinition> mutate in new Action<MethodDefinition>[]
        {
            method => method.Body.Instructions.Single(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "UnityEngine.ScreenCapture").OpCode = OpCodes.Callvirt,
            method => ((MethodReference)method.Body.Instructions.Single(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "UnityEngine.ScreenCapture").Operand).Parameters.Add(new ParameterDefinition(original.MainModule.TypeSystem.Int32)),
            method => ((MethodReference)method.Body.Instructions.Single(i => i.Operand is MethodReference c && c.Name == "get_ScreenCaptureImagePath").Operand).Name = "get_UnknownScreenshotPath",
            method => method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[0], Instruction.Create(OpCodes.Br,
                method.Body.Instructions.Single(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "UnityEngine.ScreenCapture"))),
            method => method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = method.Body.Instructions.Single(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "UnityEngine.ScreenCapture")
            })
        })
        {
            using var negative = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll"));
            mutate(Screenshot(negative));
            check(Capture(() => PathsCompatibility.BindErrorScreenshot(negative.MainModule, paths, new HashSet<string>(), new List<string>())) is InvalidDataException,
                "Changed original screenshot boundary was silently rewritten.");
        }
        Console.WriteLine("Original path metadata: " + originals + " preserved callsites mapped (" + initializerCalls + " direct static-initializer calls), screenshot coroutine unchanged after removing its single bridge; original absolute alias retained.");
    }

    private static void ExecuteOriginalRulePath(AssemblyDefinition emitted, string adapted, string temp, Action<bool, string> check)
    {
        MethodDefinition source = emitted.MainModule.GetType("RootSaveData").Methods.Single(m => m.Name == "get_CoreRulebasePath");
        string fixturePath = Path.Combine(temp, "original-root-path.dll");
        using (var fixture = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("OriginalRootPathFixture", new Version(1, 0)), "OriginalRootPathFixture", ModuleKind.Dll))
        {
            fixture.MainModule.Runtime = emitted.MainModule.Runtime;
            var owner = new TypeDefinition("", "OriginalRootPath", TypeAttributes.Public, fixture.MainModule.TypeSystem.Object); fixture.MainModule.Types.Add(owner);
            var method = new MethodDefinition(source.Name, source.Attributes, fixture.MainModule.TypeSystem.String); owner.Methods.Add(method);
            ILProcessor il = method.Body.GetILProcessor();
            foreach (Instruction instruction in source.Body.Instructions)
                il.Append(instruction.Operand switch
                {
                    null => Instruction.Create(instruction.OpCode),
                    string text => Instruction.Create(instruction.OpCode, text),
                    MethodReference call => Instruction.Create(instruction.OpCode, fixture.MainModule.ImportReference(call)),
                    _ => throw new InvalidDataException("Unexpected original CoreRulebasePath instruction: " + instruction)
                });
            check(source.Body.Instructions.Select(InstructionText).SequenceEqual(method.Body.Instructions.Select(InstructionText)),
                "Copied original rule path fixture changed its actual compiled IL.");
            fixture.Write(fixturePath);
        }
        var context = new AssemblyLoadContext("original-rule-path-worker", isCollectible: true);
        context.Resolving += (_, requested) => throw new InvalidOperationException("Unexpected original path dependency: " + requested.Name);
        try
        {
            Type paths = context.LoadFromAssemblyPath(Path.Combine(adapted, "QuestGame.Compatibility.dll")).GetType("QuestGame.Compatibility.Paths", true)!;
            string persistent = Path.Combine(temp, "actual-original-worker");
            paths.GetMethod("Initialize")!.Invoke(null, new object[] { persistent });
            string expected = Path.Combine(persistent, "quest-owned-game", "StreamingAssets", "Rulebase");
            Directory.CreateDirectory(expected); File.WriteAllText(Path.Combine(expected, "fixture.yml"), "unchanged original rule bytes");
            MethodInfo actualGetter = context.LoadFromAssemblyPath(fixturePath).GetType("OriginalRootPath", true)!.GetMethod(source.Name)!;
            (string Path, string Bytes) result = Task.Run(() =>
            {
                string path = (string)actualGetter.Invoke(null, null)!;
                return (path, File.ReadAllText(Path.Combine(path, "fixture.yml")));
            }).GetAwaiter().GetResult();
            check(result.Path == expected && result.Bytes == "unchanged original rule bytes",
                "Actual original CoreRulebasePath getter could not read its file-backed rule root on a worker.");
        }
        finally { context.Unload(); }
    }

    private static string InstructionText(Instruction instruction) => instruction.OpCode.Code + "|" + (instruction.Operand is MemberReference member ? member.FullName : instruction.Operand?.ToString() ?? "");
    private static string[] Read(RuntimeFixture fixture) => Getters.Select(name => fixture.Call(name)).ToArray();
    private static Exception Capture(Action action)
    {
        try { action(); return new Exception("Fixture unexpectedly succeeded."); }
        catch (Exception error) { return error; }
    }

    private sealed class RuntimeFixture : IDisposable
    {
        private readonly AssemblyLoadContext context = new("pure-managed-paths", isCollectible: true);
        private readonly Type paths;
        internal RuntimeFixture(string library)
        {
            context.Resolving += (_, assembly) => throw new InvalidOperationException("Unexpected runtime dependency: " + assembly.Name);
            paths = context.LoadFromAssemblyPath(library).GetType("QuestGame.Compatibility.Paths", true)!;
        }
        internal void Initialize(string? persistent) => Invoke("Initialize", new object?[] { persistent });
        internal string Call(string name, params object?[] arguments) => (string)Invoke(name, arguments)!;
        private object? Invoke(string name, object?[] arguments)
        {
            try { return paths.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, arguments); }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }
        public void Dispose() => context.Unload();
    }
}
