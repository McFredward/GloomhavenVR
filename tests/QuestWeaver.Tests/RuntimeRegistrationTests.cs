using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

/// <summary>Exercise the emitted registry, rather than stopping at setter IL.
/// B710 removed the desktop movie transpilers successfully, but the unchanged
/// WorldUI initializer still registered both resulting empty classes. The real
/// registry rejected them before any intro/menu frame could start.</summary>
internal static class RuntimeRegistrationTests
{
    const string Hooks = "QuestWeaver.Hooks.v1", Static = "QuestWeaver.StaticPatches.v1";
    const string MovieClass = "Fixture.MoviePatch", UnknownClass = "Fixture.UnknownPatch";

    internal static void VerifyCurrentOutput(string output, string managed, Action<bool, string> check)
    {
        string modPath = Path.Combine(output, "GloomhavenVR.dll");
        using AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(modPath);
        string[][] hooks = Rows(mod, Hooks), statics = Rows(mod, Static);
        string[] movieClasses = { "GloomhavenVR.WorldUI.NativeVideoMoviePatch", "GloomhavenVR.WorldUI.NativeAmbientMoviePatch" };
        bool hasMovies = mod.MainModule.GetType("GloomhavenVR.WorldUI.NativeVideoMovies") != null;
        if (hasMovies)
        {
            // Run the exact classes from the written assembly through the same
            // registry API used by WorldUIModule.Initialize, before inspecting the
            // resource. On the actual B710 output this reproduces its startup
            // NotSupportedException instead of merely reporting missing metadata.
            using var runtime = new Runtime(modPath, output, managed);
            var owner = new Owner("current-movie-startup");
            foreach (string name in movieClasses)
            {
                Type patch = runtime.Mod.GetType(name, throwOnError: true)!;
                Exception? failure = Failure(() => runtime.PatchAll(owner, patch));
                check(failure == null, "Actual WorldUI startup registration failed for " + name + ": " + failure);
                runtime.PatchAll(owner, patch);
            }
            runtime.Unpatch(owner);
            foreach (string name in movieClasses) runtime.PatchAll(new Owner("replacement-movie-owner"), runtime.Mod.GetType(name)!);
            check(movieClasses.Order().SequenceEqual(statics.Where(row => movieClasses.Contains(row[0])).Select(row => row[0]).Order()),
                "The two qualified movie classes lack their exact paired static registration proof.");
            using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(Path.Combine(output, "GH.Runtime.dll"));
            foreach (string[] row in statics.Where(row => movieClasses.Contains(row[0])))
            {
                check(row.Length == 3 && row[2].StartsWith(mod.Name.Name + "/" + row[0] + "::Transpiler(", StringComparison.Ordinal),
                    "Static registration proof lost its exact removed desktop patch identity.");
                bool fullscreen = row[0] == movieClasses[0];
                MethodDefinition target = game.MainModule.GetType(fullscreen ? "VideoCamera" : "BackgroundView").Methods
                    .Single(method => method.Name == (fullscreen ? "PlayFullscreenVideo" : "Update"));
                check(row[1] == Discovery.MethodKey(target), "Static registration proof does not bind its exact original native movie method.");
                foreach (string kind in new[] { "prefix", "postfix", "finalizer" })
                    check(runtime.Ordered(row[1] + "|" + kind).Length == 0,
                        "A build-time movie substitution was exposed as an executable runtime hook.");
            }
        }

        var normalClasses = hooks.Select(row => row[0]).ToHashSet(StringComparer.Ordinal);
        var staticClasses = statics.Select(row => row[0]).ToHashSet(StringComparer.Ordinal);
        var callers = new List<(MethodDefinition Caller, TypeReference Patch)>();
        foreach (MethodDefinition caller in Discovery.AllTypes(mod.MainModule).SelectMany(type => type.Methods).Where(method => method.HasBody))
            foreach (Instruction call in caller.Body.Instructions.Where(instruction => instruction.Operand is MethodReference method
                && method.DeclaringType.FullName == "QuestWeaver.Runtime.Registry" && method.Name == "PatchAll"))
            {
                Instruction? type = Previous(call), token = type == null ? null : Previous(type);
                check(type?.Operand is MethodReference getType && getType.DeclaringType.FullName == "System.Type"
                    && getType.Name == "GetTypeFromHandle" && token?.OpCode == OpCodes.Ldtoken && token.Operand is TypeReference,
                    "Live PatchAll selector needs explicit closure qualification: " + caller.FullName);
                TypeReference patch = (TypeReference)token!.Operand;
                check(normalClasses.Contains(patch.FullName) || staticClasses.Contains(patch.FullName),
                    "Live PatchAll class has neither compiled hooks nor qualified static substitution: " + patch.FullName + " in " + caller.FullName);
                callers.Add((caller, patch));
            }
        check(callers.Count > 0, "The actual startup registration audit did not inspect any live callsite.");
        foreach (string name in movieClasses.Where(_ => hasMovies))
            check(callers.Any(site => site.Caller.DeclaringType.FullName == "GloomhavenVR.WorldUI.WorldUIModule" && site.Patch.FullName == name),
                "Movie test no longer covers the real WorldUI initializer: " + name);

        // All selector-free current classes can be activated on the host without
        // running Unity or gameplay controllers. Dynamic selectors are checked by
        // metadata closure above and the executable negative fixtures below.
        using (var runtime = new Runtime(modPath, output, managed))
        {
            var owner = new Owner("current-static-callsite-audit");
            int activated = 0;
            foreach (string name in callers.Select(site => site.Patch.FullName).Distinct())
            {
                TypeDefinition definition = mod.MainModule.GetType(name);
                if (definition.Methods.Any(method => method.Name is "Prepare" or "TargetMethod" or "TargetMethods")) continue;
                Exception? failure = Failure(() => runtime.PatchAll(owner, runtime.Mod.GetType(name.Replace('/', '+'), throwOnError: true)!));
                check(failure == null, "Current selector-free PatchAll registration failed for " + name + ": " + failure);
                activated++;
            }
            runtime.Unpatch(owner);
            check(activated > 0, "No current selector-free startup classes reached the real registry.");
            Console.WriteLine("Current startup registrations: " + callers.Count + " live callsites, "
                + callers.Select(site => site.Patch.FullName).Distinct().Count() + " classes, " + activated + " runtime activations, "
                + statics.Length + " qualified static substitutions.");
        }
    }

    internal static void RunFixture(string output, Action<bool, string> check, string fixtureDir)
    {
        Directory.CreateDirectory(output);
        Fixture("valid-proof", (assembly, proof) => proof, (runtime, target) => {
            var owner = new Owner("static-owner"); Type patch = runtime.Mod.GetType(MovieClass)!;
            runtime.PatchAll(owner, patch); runtime.PatchAll(owner, patch);
            check(runtime.Ordered(target + "|prefix").Length == 0, "Static proof created an executable prefix slot.");
            check(Failure(() => runtime.PatchAll(new Owner("conflicting-owner"), patch)) is InvalidOperationException,
                "Static registration lost ordinary Harmony owner-conflict semantics.");
            runtime.Unpatch(owner); runtime.PatchAll(new Owner("next-owner"), patch);
            check(runtime.Ordered(target + "|postfix").Length == 0, "Unpatch/re-registration changed native static substitution semantics.");
            Exception? unknown = Failure(() => runtime.PatchAll(owner, runtime.Mod.GetType(UnknownClass)!));
            check(unknown is NotSupportedException && unknown.Message.Contains("No compiled Quest hooks", StringComparison.Ordinal),
                "An unknown empty class received a silent-success runtime registration.");
        });
        Reject("missing-proof", (_, _) => null, error => error is NotSupportedException);
        Reject("truncated-proof", (_, proof) => proof[..proof.LastIndexOf('|')], error => error is InvalidDataException);
        Reject("duplicate-proof", (_, proof) => proof + "\n" + proof, error => error is InvalidDataException);
        Reject("wrong-removed-owner", (_, proof) => proof.Replace("/" + MovieClass + "::Transpiler(", "/" + UnknownClass + "::Transpiler(", StringComparison.Ordinal), error => error is InvalidDataException);
        Reject("missing-proof-class", (_, proof) => proof.Replace(MovieClass, "Fixture.MissingPatch", StringComparison.Ordinal), error => error is InvalidDataException);
        Reject("invalid-target", (_, proof) => proof.Replace("::Play()", "Play", StringComparison.Ordinal), error => error is InvalidDataException);
        Reject("nonempty-proof-class", (assembly, proof) => {
            var method = new MethodDefinition("Unexpected", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, assembly.MainModule.TypeSystem.Void);
            assembly.MainModule.GetType(MovieClass).Methods.Add(method); method.Body.GetILProcessor().Emit(OpCodes.Ret); return proof;
        }, error => error is InvalidDataException);
        Reject("ordinary-static-conflict", (assembly, proof) => {
            Resource(assembly, Hooks, MovieClass + "|" + Target(assembly) + "|" + assembly.Name.Name + "/" + MovieClass + "::Prefix()|prefix|0|0\n"); return proof;
        }, error => error is InvalidDataException);
        Reject("ordinary-static-kind", (assembly, proof) => {
            Resource(assembly, Hooks, MovieClass + "|" + Target(assembly) + "|" + assembly.Name.Name + "/" + MovieClass + "::Transpiler()|static|0|-1\n"); return null;
        }, error => error is InvalidDataException);
        Reject("proof-class-initializer", (assembly, proof) => {
            var method = new MethodDefinition(".cctor", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static
                | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, assembly.MainModule.TypeSystem.Void);
            assembly.MainModule.GetType(MovieClass).Methods.Add(method); method.Body.GetILProcessor().Emit(OpCodes.Ret); return proof;
        }, error => error is InvalidDataException);
        Fixture("atomic-proof-failure", (assembly, proof) => {
            ModuleDefinition module = assembly.MainModule; TypeDefinition normal = Empty(module, "NormalPatch");
            var prefix = new MethodDefinition("Prefix", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
            normal.Methods.Add(prefix); prefix.Body.GetILProcessor().Emit(OpCodes.Ret);
            Resource(assembly, Hooks, normal.FullName + "|" + Target(assembly) + "|" + Discovery.MethodKey(prefix) + "|prefix|0|0\n");
            return proof + "\ninvalid";
        }, (runtime, target) => {
            var owner = new Owner("atomic-proof-owner");
            foreach (string patch in new[] { MovieClass, "Fixture.NormalPatch" })
            {
                Exception? error = Failure(() => runtime.PatchAll(owner, runtime.Mod.GetType(patch)!));
                check(error is InvalidDataException, "Invalid proof was partially committed, allowing another class to activate: " + patch + "; error=" + error);
                check(runtime.Ordered(target + "|prefix").Length == 0, "An ordinary hook survived atomic static-proof rejection.");
            }
        });
        foreach (bool outside in new[] { false, true })
            Fixture(outside ? "selector-outside" : "selector-inside", (assembly, proof) => {
                ModuleDefinition module = assembly.MainModule; TypeDefinition patch = Empty(module, "DynamicPatch");
                var prefix = new MethodDefinition("Prefix", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
                patch.Methods.Add(prefix); prefix.Body.GetILProcessor().Emit(OpCodes.Ret);
                var selector = new MethodDefinition("TargetMethod", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.ImportReference(typeof(MethodInfo)));
                patch.Methods.Add(selector); ILProcessor il = selector.Body.GetILProcessor();
                il.Emit(OpCodes.Ldtoken, module.GetType("Fixture.Original"));
                il.Emit(OpCodes.Call, module.ImportReference(typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!));
                il.Emit(OpCodes.Ldstr, outside ? "Other" : "Play");
                il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(Type).GetMethod(nameof(Type.GetMethod), new[] { typeof(string) })!)); il.Emit(OpCodes.Ret);
                Resource(assembly, Hooks, patch.FullName + "|" + Target(assembly) + "|" + Discovery.MethodKey(prefix) + "|prefix|0|0\n");
                return proof;
            }, (runtime, target) => {
                var owner = new Owner("selector-owner"); Type patch = runtime.Mod.GetType("Fixture.DynamicPatch")!;
                Exception? error = Failure(() => runtime.PatchAll(owner, patch));
                check(outside ? error is NotSupportedException && error.Message.Contains("outside the compiled Quest closure", StringComparison.Ordinal) : error == null,
                    "Runtime selector closure changed: outside=" + outside + "; error=" + error);
                check(runtime.Ordered(target + "|prefix").Length == (outside ? 0 : 1), "Runtime selector activated an unselected or refused hook.");
                runtime.Unpatch(owner); check(runtime.Ordered(target + "|prefix").Length == 0, "UnpatchSelf left a dynamic hook active.");
            });
        foreach (bool declined in new[] { false, true })
        {
            string source = Path.Combine(output, declined ? "source-declined" : "source-unbound"); Directory.CreateDirectory(source);
            File.Copy(Path.Combine(fixtureDir, "FixtureGame.dll"), Path.Combine(source, "FixtureGame.dll"));
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(Path.Combine(fixtureDir, "FixtureMod.dll")))
            {
                TypeDefinition empty = Empty(assembly.MainModule, "UnboundPatch");
                MethodDefinition install = assembly.MainModule.GetType("FixtureMod.Entry").Methods.Single(method => method.Name == "InstallDynamic");
                install.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Ldtoken).Operand = empty;
                if (declined)
                {
                    var prepare = new MethodDefinition("Prepare", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static, assembly.MainModule.TypeSystem.Boolean);
                    empty.Methods.Add(prepare); prepare.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_0); prepare.Body.GetILProcessor().Emit(OpCodes.Ret);
                }
                assembly.Write(Path.Combine(source, "FixtureMod.dll"));
            }
            using Discovery model = Discovery.Load(Path.Combine(source, "FixtureMod.dll"), source);
            AuditReport report = model.Audit();
            check(report.Issues.Any(issue => issue.Code == "PATCH_CLASS_REGISTRATION_UNRESOLVED") == !declined,
                "Build-time live PatchAll closure gate lost its unknown-class/explicit-Prepare-false distinction: declined=" + declined
                + "; issues=" + string.Join("; ", report.Issues));
            if (declined)
            {
                string written = Path.Combine(output, "declined-output"); new Weaver(model).Write(written, report, false);
                using var runtime = new Runtime(Path.Combine(written, "FixtureMod.dll"), written, source);
                check(Failure(() => runtime.PatchAll(new Owner("declined-owner"), runtime.Mod.GetType("Fixture.UnboundPatch")!)) == null,
                    "A source-qualified Prepare-false class was forced to own an executable runtime hook.");
            }
        }

        void Reject(string label, Func<AssemblyDefinition, string, string?> alter, Func<Exception?, bool> expected) =>
            Fixture(label, alter, (runtime, _) => {
                Exception? error = Failure(() => runtime.PatchAll(new Owner(label), runtime.Mod.GetType(MovieClass)!));
                check(expected(error), "Invalid static proof did not fail closed (" + label + "): " + error);
            });

        void Fixture(string label, Func<AssemblyDefinition, string, string?> alter, Action<Runtime, string> verify)
        {
            string path = Path.Combine(output, label + ".dll");
            string target;
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("StaticRegistration_" + Guid.NewGuid().ToString("N"), new Version(1, 0)), label, ModuleKind.Dll))
            {
                ModuleDefinition module = assembly.MainModule; Empty(module, "MoviePatch"); Empty(module, "UnknownPatch");
                TypeDefinition original = Empty(module, "Original");
                foreach (string name in new[] { "Play", "Other" })
                { var method = new MethodDefinition(name, Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void); original.Methods.Add(method); method.Body.GetILProcessor().Emit(OpCodes.Ret); }
                target = Target(assembly); Resource(assembly, Hooks, "");
                string proof = MovieClass + "|" + target + "|" + assembly.Name.Name + "/" + MovieClass + "::Transpiler()";
                string? changed = alter(assembly, proof); if (changed != null) Resource(assembly, Static, changed + "\n");
                assembly.Write(path);
            }
            using (var runtime = new Runtime(path, output, "")) verify(runtime, target);
        }
    }

    internal static void VerifyWriteGate(string modPath, string managed, string output, Action<bool, string> check)
    {
        foreach (string mutation in new[] { "missing-proof", "damaged-substitution", "different-native-target" })
        {
            using Discovery model = Discovery.Load(modPath, managed); AuditReport report = model.Audit();
            if (model.StaticPatches.Count == 0) return;
            StaticPatchDefinition proof = model.StaticPatches[0];
            if (mutation == "missing-proof") model.StaticPatches.Clear();
            else if (mutation == "different-native-target")
                model.StaticPatches[0] = proof with { Target = proof.Target.DeclaringType.Methods.First(method => method.HasBody && method != proof.Target) };
            else
            {
                Instruction setter = proof.Target.Body.Instructions.Single(instruction => instruction.Operand is MethodReference call
                    && call.DeclaringType.FullName == "GloomhavenVR.WorldUI.NativeVideoMovies" && call.Name == "SetNativeUrl");
                // A valid call shape to an unrelated method cannot certify the
                // reviewed URL substitution simply because the original target
                // assembly is still present in the model's Loaded collection.
                MethodReference previous = (MethodReference)setter.Operand;
                setter.Operand = new MethodReference("UnreviewedMovieSetter", previous.ReturnType, previous.DeclaringType);
            }
            string path = Path.Combine(output, mutation);
            Exception? error = Failure(() => new Weaver(model).Write(path, report, false));
            check(error is InvalidDataException, "Writer certified a damaged or absent static substitution proof: " + mutation + "; error=" + error);
        }
    }

    static TypeDefinition Empty(ModuleDefinition module, string name)
    {
        var type = new TypeDefinition("Fixture", name, Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(type); return type;
    }
    static string Target(AssemblyDefinition assembly) => assembly.Name.Name + "/Fixture.Original::Play()";
    static void Resource(AssemblyDefinition assembly, string name, string value)
    {
        foreach (Resource previous in assembly.MainModule.Resources.Where(resource => resource.Name == name).ToArray()) assembly.MainModule.Resources.Remove(previous);
        assembly.MainModule.Resources.Add(new EmbeddedResource(name, Mono.Cecil.ManifestResourceAttributes.Private, Encoding.UTF8.GetBytes(value)));
    }
    static string[][] Rows(AssemblyDefinition assembly, string name)
    {
        EmbeddedResource? resource = assembly.MainModule.Resources.OfType<EmbeddedResource>().SingleOrDefault(item => item.Name == name);
        return resource == null ? Array.Empty<string[]>() : Encoding.UTF8.GetString(resource.GetResourceData()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r').Split('|')).ToArray();
    }
    static Instruction? Previous(Instruction instruction)
    { Instruction? previous = instruction.Previous; while (previous?.OpCode == OpCodes.Nop) previous = previous.Previous; return previous; }
    static Exception? Failure(Action action) { try { action(); return null; } catch (Exception error) { return error; } }
    sealed class Owner { public string Id { get; } public Owner(string id) { Id = id; } }
    sealed class Runtime : IDisposable
    {
        readonly AssemblyLoadContext context = new("runtime-registration-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        readonly Type registry;
        internal Assembly Mod { get; }
        internal Runtime(string modPath, string output, string managed)
        {
            string runtimePath = Path.Combine(output, "QuestWeaver.Runtime.dll");
            if (!File.Exists(runtimePath)) runtimePath = typeof(QuestWeaver.Runtime.Registry).Assembly.Location;
            context.Resolving += (_, name) => {
                string path = name.Name == "QuestWeaver.Runtime" ? runtimePath : Path.Combine(output, name.Name + ".dll");
                if (!File.Exists(path)) path = Path.Combine(managed, name.Name + ".dll");
                return File.Exists(path) ? context.LoadFromAssemblyPath(Path.GetFullPath(path)) : null;
            };
            registry = context.LoadFromAssemblyPath(Path.GetFullPath(runtimePath)).GetType("QuestWeaver.Runtime.Registry")!;
            Mod = context.LoadFromAssemblyPath(Path.GetFullPath(modPath));
        }
        internal void PatchAll(object owner, Type patch) => Invoke("PatchAll", owner, patch);
        internal void Unpatch(object owner) => Invoke("UnpatchSelf", owner);
        internal int[] Ordered(string key) => (int[])Invoke("Ordered", key)!;
        object? Invoke(string name, params object[] arguments)
        {
            try { return registry.GetMethod(name)!.Invoke(null, arguments); }
            catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        public void Dispose() => context.Unload();
    }
}
