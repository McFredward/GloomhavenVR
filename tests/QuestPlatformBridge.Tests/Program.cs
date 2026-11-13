using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class Program
{
    private const string Manager = "Platforms.Generic.UserManagementGeneric";
    private static int assertions;
    private static readonly List<string> checks = new();
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        assertions++; checks.Add(name);
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static MethodDefinition Remove(AssemblyDefinition assembly) => assembly.MainModule.GetType(Manager).Methods.Single(m => m.Name == "RemovePlatformUser");
    private static AssemblyDefinition Read(string path, string managed)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managed);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        return AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
    }
    private static void Reject(string original, string managed, string name, Action<MethodDefinition> mutation)
    {
        using var input = Read(original, managed);
        MethodDefinition method = Remove(input);
        mutation(method);
        try { Standalone.GuardMissingPlatformUser(method); }
        catch (InvalidDataException) { Check(true, name); return; }
        throw new InvalidOperationException(name);
    }
    private static void TestMetadata(string managed, string output, StandaloneReport report)
    {
        string original = Path.Combine(managed, "SM.Consoles.dll");
        using var source = Read(original, managed);
        using var adapted = Read(Path.Combine(output, "SM.Consoles.dll"), managed);
        MethodDefinition method = Remove(adapted);
        Check(method.Body.Instructions[0].OpCode == OpCodes.Ldarg_1
            && method.Body.Instructions[1].OpCode == OpCodes.Brtrue
            && method.Body.Instructions[1].Operand == method.Body.Instructions[3]
            && method.Body.Instructions[2].OpCode == OpCodes.Ret, "null-only-prefix-shape");
        Check(method.Body.MaxStackSize == Remove(source).Body.MaxStackSize, "original-stack-bound-preserved");
        for (int i = 0; i < 3; i++) method.Body.Instructions.RemoveAt(0);
        Check(ProtectedTypes.Fingerprint(method.DeclaringType) == ProtectedTypes.Fingerprint(Remove(source).DeclaringType), "non-null-entire-type-body-unchanged");
        // Compare the real dispatcher separately: the patch must not remove default
        // users, input-device change routing, events, or the original virtual mouse.
        TypeDefinition input = source.MainModule.GetType("Platforms.Generic.PlatformInputGeneric");
        TypeDefinition actual = adapted.MainModule.GetType(input.FullName);
        // BuildUnityUsers is the pre-existing standalone identity seam. Every other
        // method, field, local and handler of this input type must remain original.
        input.Methods.Remove(input.Methods.Single(m => m.Name == "BuildUnityUsers"));
        actual.Methods.Remove(actual.Methods.Single(m => m.Name == "BuildUnityUsers"));
        Check(ProtectedTypes.Fingerprint(input) == ProtectedTypes.Fingerprint(actual), "original-device-dispatch-preserved");
        Check(report.Modifications.Any(m => m.Contains(Manager + "::RemovePlatformUser", StringComparison.Ordinal)), "guard-reported");
        Check(report.ProtectedTypesVerified > 0 && report.UnchangedTypesVerified > 4000, "production-protected-and-unrelated-verification");
        foreach (string filename in new[] { "GH.Runtime.dll", "SM.Consoles.dll", "Apparance.Unity.dll" })
        {
            using var before = Read(Path.Combine(managed, filename), managed);
            using var after = Read(Path.Combine(output, filename), managed);
            Check(ProtectedTypes.Verify(ProtectedTypes.Snapshot(before), after) >= 0, "protected-fingerprints-" + filename);
        }
        Reject(original, managed, "reject-static-abi", m => { m.IsStatic = true; m.HasThis = false; });
        Reject(original, managed, "reject-explicit-this-abi", m => m.ExplicitThis = true);
        Reject(original, managed, "reject-generic-abi", m => m.GenericParameters.Add(new GenericParameter("T", m)));
        Reject(original, managed, "reject-return-abi", m => m.ReturnType = m.Module.TypeSystem.Boolean);
        Reject(original, managed, "reject-parameter-abi", m => m.Parameters[0].ParameterType = m.Module.TypeSystem.Object);
        Reject(original, managed, "reject-parameter-count-abi", m => m.Parameters.Clear());
        Reject(original, managed, "reject-non-interface-abi", m => m.Parameters[0].ParameterType.Resolve().IsInterface = false);
        Reject(original, managed, "reject-empty-body", m => m.Body.Instructions.Clear());
        Reject(original, managed, "reject-abstract-body", m => { m.IsAbstract = true; m.Body = null; });
        Reject(original, managed, "reject-user-list-seam", m => m.DeclaringType.Fields.Single(f => f.Name == "_users").Name = "changedUsers");
        Reject(original, managed, "reject-input-user-call-seam", m =>
        {
            foreach (Instruction instruction in m.Body.Instructions)
                if (instruction.Operand is MethodReference call && call.DeclaringType.FullName == "Platforms.IPlatformUserData" && call.Name == "GetUnityInputUser")
                    call.Name = "ChangedInputUser";
        });
    }
    private static void TestRefusedOutput(string managed, string proof, string profile)
    {
        string overrides = Path.Combine(proof, "changed-original-seam");
        Directory.CreateDirectory(overrides);
        using (var altered = Read(Path.Combine(managed, "SM.Consoles.dll"), managed))
        {
            Remove(altered).DeclaringType.Fields.Single(f => f.Name == "_users").Name = "ChangedUsers";
            altered.Write(Path.Combine(overrides, "SM.Consoles.dll"));
        }
        string output = Path.Combine(proof, "refused-adapted-output");
        try { Standalone.Write(managed, overrides, profile, output); }
        catch (InvalidDataException)
        {
            Check(!Directory.Exists(output), "changed-seam-never-publishes-adapted-output");
            return;
        }
        throw new InvalidOperationException("changed-seam-never-publishes-adapted-output");
    }
    private static Exception? Call(MethodInfo method, object target, object? user)
    {
        try { method.Invoke(target, new[] { user }); return null; }
        catch (TargetInvocationException e) { return e.InnerException; }
    }
    private sealed class PlatformContext : AssemblyLoadContext
    {
        private readonly string platformDirectory, managed;
        public PlatformContext(string platformDirectory, string managed) : base(isCollectible: true)
        { this.platformDirectory = platformDirectory; this.managed = managed; }
        protected override Assembly? Load(AssemblyName name)
        {
            // Resolve only requested assembly names. Do not enumerate game types or
            // execute Unity/InputSystem initialization; InputUser equality is pure IL.
            if (name.Name == null || name.Name.StartsWith("System", StringComparison.Ordinal) || name.Name is "mscorlib" or "netstandard") return null;
            string path = Path.Combine(platformDirectory, name.Name + ".dll");
            if (!File.Exists(path)) path = Path.Combine(managed, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(Path.GetFullPath(path)) : null;
        }
    }
    private sealed record Outcome(int Members, int Events, bool SameMember, string? Failure);
    private static Outcome Exercise(string platformDirectory, string managed, bool absent, bool sameUser, bool uninitialized)
    {
        var context = new PlatformContext(platformDirectory, managed);
        try
        {
            Assembly platform = context.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(platformDirectory, "SM.Consoles.dll")));
            Type manager = platform.GetType(Manager, throwOnError: true)!;
            Type userType = platform.GetType("Platforms.Generic.UserDataGeneric", throwOnError: true)!;
            MethodInfo remove = manager.GetMethod("RemovePlatformUser", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object instance = uninitialized ? RuntimeHelpers.GetUninitializedObject(manager) : Activator.CreateInstance(manager)!;
            // This constructor stores fields only. Default InputUser (id 0) equality
            // compares two integers and never registers devices or invokes engine APIs.
            ConstructorInfo constructor = userType.GetConstructors().Single();
            Type inputUser = constructor.GetParameters()[3].ParameterType;
            object User(string id) => constructor.Invoke(new[] { "DUMMY fixture", id, (object)0, Activator.CreateInstance(inputUser)! });
            object member = User("fixture-member");
            object? argument = absent ? null : sameUser ? member : User("fixture-other");
            if (uninitialized)
                return new Outcome(-1, 0, false, Call(remove, instance, argument)?.GetType().FullName);
            IList members = (IList)manager.GetMethod("GetCurrentUsers")!.Invoke(instance, null)!;
            members.Add(member);
            int events = 0;
            manager.GetEvent("PlatformUserUpdatedEvent")!.AddEventHandler(instance, (Action)(() => events++));
            Exception? failure = Call(remove, instance, argument);
            return new Outcome(members.Count, events, members.Count > 0 && ReferenceEquals(members[0], member), failure?.GetType().FullName);
        }
        finally { context.Unload(); }
    }
    private static void TestRuntime(string managed, string output)
    {
        Outcome originalNull = Exercise(managed, managed, true, true, false);
        Check(originalNull.Failure == typeof(NullReferenceException).FullName && originalNull.Members == 1 && originalNull.Events == 0, "original-null-reproduced");
        Outcome adaptedNull = Exercise(output, managed, true, true, false);
        Check(adaptedNull == new Outcome(1, 0, true, null), "adapted-null-safe-without-list-or-event-change");
        Outcome nullWithoutFields = Exercise(output, managed, true, true, true);
        Check(nullWithoutFields.Failure == null, "adapted-null-returns-before-any-field-access");
        Outcome nonNullWithoutFields = Exercise(output, managed, false, true, true);
        Check(nonNullWithoutFields.Failure == typeof(NullReferenceException).FullName, "non-null-original-failure-preserved");
        foreach (bool same in new[] { true, false })
        {
            Outcome before = Exercise(managed, managed, false, same, false);
            Outcome after = Exercise(output, managed, false, same, false);
            Check(after == before && after.Failure == null, "non-null-original-runtime-parity-" + same);
            // The original also signals when an equal InputUser belongs to a different
            // user-data object; preserve that behavior rather than improving it here.
            Check(after == new Outcome(same ? 0 : 1, 1, !same, null), "original-removal-and-event-semantics-" + same);
        }
    }
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Provide original managed directory and private proof output.");
            string managed = Path.GetFullPath(args[0]), proof = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(proof);
            string profile = Path.Combine(proof, "dummy-profile.json"), output = Path.Combine(proof, "adapted");
            File.WriteAllText(profile, "{\"schema\":1,\"provider\":\"steam\",\"displayName\":\"DUMMY platform fixture\",\"steamId\":\"0\",\"accountId\":0,\"isDummy\":true}");
            string[] names = { "GH.Runtime.dll", "SM.Consoles.dll", "Apparance.Unity.dll", "ScenarioRuleLibrary.dll", "Unity.InputSystem.dll" };
            var originalHashes = names.ToDictionary(n => n, n => Hash(Path.Combine(managed, n)));
            StandaloneReport report = Standalone.Write(managed, null, profile, output);
            TestRuntime(managed, output);
            TestMetadata(managed, output, report);
            TestRefusedOutput(managed, proof, profile);
            foreach (var pair in originalHashes) Check(Hash(Path.Combine(managed, pair.Key)) == pair.Value, "readonly-original-" + pair.Key);
            File.WriteAllText(Path.Combine(proof, "proof.json"), JsonSerializer.Serialize(new
            {
                assertions, checks, originalHashes, report.ProtectedTypesVerified, report.UnchangedTypesVerified,
                outputAssemblies = report.OutputAssemblies,
                boundary = "Actual original/adapted CIL and targeted CLR invocation; no Unity or Quest hardware claim."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Quest platform bridge: {assertions} assertions passed.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
