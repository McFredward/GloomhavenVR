using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class HarmonyLookupTests
{
    internal static void Run(string temp, Action<bool, string> check)
    {
        Directory.CreateDirectory(temp);
        using AssemblyDefinition first = Source("LookupOne");
        Add(first, "Earlier", "Short"); Add(first, "Later", "Short");
        Add(first, "AliasScope", "Collision");
        TypeDefinition outer = Add(first, "NestedScope", "Outer");
        outer.NestedTypes.Add(new TypeDefinition("", "Inner", Mono.Cecil.TypeAttributes.NestedPublic | Mono.Cecil.TypeAttributes.Class, first.MainModule.TypeSystem.Object));
        outer.NestedTypes.Add(new TypeDefinition("", "PrivateInner", Mono.Cecil.TypeAttributes.NestedPrivate | Mono.Cecil.TypeAttributes.Class, first.MainModule.TypeSystem.Object));
        Add(first, "GenericScope", "Marker`1");
        Add(first, "EscapedScope", "Plus+Name");
        using AssemblyDefinition second = Source("LookupTwo");
        Add(second, "", "Collision"); Add(second, "Another", "Short");
        first.Write(Path.Combine(temp, "LookupOne.dll")); second.Write(Path.Combine(temp, "LookupTwo.dll"));
        using AssemblyDefinition one = AssemblyDefinition.ReadAssembly(Path.Combine(temp, "LookupOne.dll"));
        using AssemblyDefinition two = AssemblyDefinition.ReadAssembly(Path.Combine(temp, "LookupTwo.dll"));
        byte[] encoded = TypeLookupMetadata.Encode(new[] { one, two });
        check(encoded.SequenceEqual(TypeLookupMetadata.Encode(new[] { two, one })), "Alias encoding depends on input assembly enumeration order.");
        using AssemblyDefinition facade = AssemblyDefinition.ReadAssembly(HarmonyFacade.Path);
        TypeLookupMetadata.Attach(facade, new[] { one }, temp);
        byte[] attached = ((EmbeddedResource)facade.MainModule.Resources.Single(r => r.Name == TypeLookupMetadata.ResourceName)).GetResourceData();
        using (AssemblyDefinition fresh = AssemblyDefinition.ReadAssembly(HarmonyFacade.Path))
            check(attached.SequenceEqual(TypeLookupMetadata.Encode(new[] { one, two, fresh })), "Current source/facade alias metadata was not generated deterministically.");
        Reject(() => TypeLookupMetadata.Attach(facade, new[] { one }, temp), "Already-generated aliases were silently appended.");
        using (AssemblyDefinition duplicate = Source("LookupOne"))
            Reject(() => TypeLookupMetadata.Encode(new[] { one, duplicate }), "Duplicate alias assembly identity was accepted.");
        // A changed current mod is read again, rather than consulting a hand-maintained
        // alias list or a previous build's resource.
        Add(one, "NewMod", "FutureType");
        check(!encoded.SequenceEqual(TypeLookupMetadata.Encode(new[] { one, two })), "Current mod metadata additions did not regenerate aliases.");

        string output = Path.Combine(temp, "0Harmony.dll"); facade.Write(output);
        var context = new AssemblyLoadContext("safe-quest-type-lookup", isCollectible: true);
        context.Resolving += (_, name) => name.Name == "QuestWeaver.Runtime" ? typeof(QuestWeaver.Runtime.Registry).Assembly : null;
        try
        {
            Assembly loaded = context.LoadFromAssemblyPath(output);
            Type access = loaded.GetType("HarmonyLib.AccessTools")!;
            MethodInfo resolve = access.GetMethod("ResolveFromAssemblies", BindingFlags.Static | BindingFlags.NonPublic)!;
            MethodInfo publicLookup = access.GetMethod("TypeByName", BindingFlags.Static | BindingFlags.Public)!;
            var shortFirst = new PoisonType(); var shortSecond = new PoisonType(); var shortOtherAssembly = new PoisonType();
            var global = new PoisonType(); var full = new PoisonType(); var nested = new PoisonType(); var privateNested = new PoisonType(); var generic = new PoisonType(); var escaped = new PoisonType();
            var a = new LookupAssembly("LookupOne", new Dictionary<string, Type>(StringComparer.Ordinal) {
                ["Earlier.Short"] = shortFirst, ["Later.Short"] = shortSecond, ["AliasScope.Collision"] = full,
                ["NestedScope.Outer+Inner"] = nested, ["NestedScope.Outer+PrivateInner"] = privateNested,
                ["GenericScope.Marker`1"] = generic, ["EscapedScope.Plus\\+Name"] = escaped });
            var b = new LookupAssembly("LookupTwo", new Dictionary<string, Type>(StringComparer.Ordinal) {
                ["Collision"] = global, ["Another.Short"] = shortOtherAssembly });
            Assembly[] assemblies = { a, b };
            Type? Lookup(string name, params Assembly[] inputs) => (Type?)resolve.Invoke(null, new object[] { name, inputs.Length == 0 ? assemblies : inputs });
            check(ReferenceEquals(Lookup("Collision"), global), "An earlier simple alias took priority over a later exact global name.");
            check(ReferenceEquals(Lookup("AliasScope.Collision"), full), "Namespaced exact lookup changed.");
            check(ReferenceEquals(Lookup("Short"), shortFirst), "Ambiguous simple names did not retain first assembly/TypeDef order.");
            check(ReferenceEquals(Lookup("Short", b, a), shortOtherAssembly), "Loaded assembly order was ignored for ambiguous aliases.");
            check(ReferenceEquals(Lookup("Inner"), nested) && ReferenceEquals(Lookup("NestedScope.Outer+Inner"), nested), "Nested simple/full names changed.");
            check(ReferenceEquals(Lookup("PrivateInner"), privateNested), "Nonpublic nested alias was excluded.");
            check(ReferenceEquals(Lookup("Marker`1"), generic), "Generic arity in a simple type name changed.");
            check(ReferenceEquals(Lookup("Plus+Name"), escaped), "Literal special characters were not escaped in the generated reflection name.");
            check(Lookup("short") == null && Lookup("earlier.Short") == null, "Lookup became case-insensitive.");
            int before = a.Requests.Count + b.Requests.Count;
            check(Lookup("Missing.Optional.Type") == null && a.Requests.Count + b.Requests.Count == before + 2,
                "Missing optional type inspected anything beyond one exact name per assembly.");
            var unknown = new LookupAssembly("UnknownCurrentAssembly", new Dictionary<string, Type>(StringComparer.Ordinal) { ["NewNamespace.Short"] = new PoisonType() });
            check(Lookup("Short", unknown) == null && unknown.Requests.SequenceEqual(new[] { "Short" }), "Unknown assembly guessed a simple alias or scanned its metadata.");
            check(ReferenceEquals(Lookup("NewNamespace.Short", unknown), unknown.Types["NewNamespace.Short"]), "Unknown assembly lost exact-name support.");
            // A catalog entry can disappear in an actual replaced package: it must not
            // force a type load or block a later still-valid candidate.
            a.Types.Remove("Earlier.Short");
            check(ReferenceEquals(Lookup("Short", a), shortSecond), "A removed first alias stopped lookup before a valid later candidate.");
            a.Types.Remove("Later.Short");
            check(Lookup("Short", a) == null, "Removed alias was guessed or treated as a loaded type.");
            var excluded = new LookupAssembly("Microsoft.VisualStudio.Probe", new Dictionary<string, Type>(StringComparer.Ordinal) { ["Collision"] = new PoisonType() });
            check(ReferenceEquals(Lookup("Collision", excluded, b), global) && excluded.Requests.Count == 0, "Existing VisualStudio assembly exclusion changed.");
            check(publicLookup.Invoke(null, new object?[] { null }) == null && publicLookup.Invoke(null, new object[] { "" }) == null, "Null/empty type name behavior changed.");
            check(ReferenceEquals(publicLookup.Invoke(null, new object[] { typeof(string).AssemblyQualifiedName! }), typeof(string)), "Assembly-qualified direct name resolution changed.");
            check(ReferenceEquals(publicLookup.Invoke(null, new object[] { "System.String" }), typeof(string)), "Core full-name direct resolution changed.");
            check(publicLookup.Invoke(null, new object[] { "QuestFixture.Absent, MissingQuestAssembly" }) == null, "Missing assembly-qualified optional name threw or resolved an unrelated type.");
            check(new[] { a, b, unknown, excluded }.All(item => item.EnumerationCalls == 0), "Safe lookup enumerated runtime types.");
            check(new[] { shortFirst, shortSecond, shortOtherAssembly, global, full, nested, privateNested, generic, escaped }.All(type => type.MetadataReads == 0),
                "Safe lookup inspected a resolved/unrelated Type.FullName or Type.Name.");
            // Negative controls execute the old failure-prone operations. These guards
            // must detect both the all-type enumeration and FullName metadata access.
            try { _ = a.GetTypes(); check(false, "Enumeration trap did not fail."); }
            catch (InvalidOperationException) { check(a.EnumerationCalls == 1, "Enumeration negative control was not reached."); }
            try { _ = global.FullName; check(false, "FullName trap did not fail."); }
            catch (InvalidOperationException) { check(global.MetadataReads == 1, "FullName negative control was not reached."); }
            // Also resolve actual Cecil-written assemblies, including inaccessible
            // nested types; the trapping doubles above cannot establish that ABI.
            Assembly actualOne = context.LoadFromAssemblyPath(Path.Combine(temp, "LookupOne.dll"));
            Assembly actualTwo = context.LoadFromAssemblyPath(Path.Combine(temp, "LookupTwo.dll"));
            check(ReferenceEquals(Lookup("Short", actualOne, actualTwo), actualOne.GetType("Earlier.Short", false)), "Actual generated assembly short-name lookup differs.");
            check(ReferenceEquals(Lookup("PrivateInner", actualOne), actualOne.GetType("NestedScope.Outer+PrivateInner", false)), "Actual nonpublic nested type could not be resolved.");
            check(ReferenceEquals(publicLookup.Invoke(null, new object[] { "Earlier.Short, LookupOne" }), actualOne.GetType("Earlier.Short", false)), "Actual non-core assembly-qualified type could not be resolved.");
        }
        finally { context.Unload(); }

        // Inspect compiled facade, not source text: the entire lookup implementation
        // must lack broad runtime enumeration and any Type name metadata getter.
        TypeDefinition accessMetadata = facade.MainModule.GetType("HarmonyLib.AccessTools");
        string[] methods = { "TypeByName", "ResolveFromAssemblies", "ReadAliases", "ReadCount" };
        check(accessMetadata.Methods.Where(m => methods.Contains(m.Name)).SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand).OfType<MethodReference>().All(m => !(m.DeclaringType.FullName == "System.Type" && m.Name is "get_FullName" or "get_Name")
                && !(m.DeclaringType.FullName == "System.Reflection.Assembly" && m.Name is "GetTypes" or "GetExportedTypes")), "Compiled lookup retains broad runtime type inspection.");

        // Malformed build metadata fails explicitly; it cannot activate a runtime
        // all-type fallback. Each case loads the actual compiled reader afresh.
        byte[] unknownVersion = (byte[])attached.Clone(); unknownVersion[0] = 2;
        byte[] negativeCount = (byte[])attached.Clone(); Array.Fill(negativeCount, (byte)255, 4, 4);
        BadResource(unknownVersion, "Unknown alias format was accepted.");
        BadResource(negativeCount, "Negative alias count was accepted.");
        BadResource(attached.Concat(new byte[] { 0 }).ToArray(), "Unexpected alias payload tail was accepted.");
        BadResource(null, "An ungenerated facade silently lost short-name semantics.");

        void BadResource(byte[]? bytes, string message)
        {
            using AssemblyDefinition altered = AssemblyDefinition.ReadAssembly(HarmonyFacade.Path);
            if (bytes != null) altered.MainModule.Resources.Add(new EmbeddedResource(TypeLookupMetadata.ResourceName, Mono.Cecil.ManifestResourceAttributes.Private, bytes));
            string directory = Path.Combine(temp, "bad-resource-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string dll = Path.Combine(directory, "0Harmony.dll"); altered.Write(dll);
            var bad = new AssemblyLoadContext("malformed-quest-alias", isCollectible: true);
            bad.Resolving += (_, name) => name.Name == "QuestWeaver.Runtime" ? typeof(QuestWeaver.Runtime.Registry).Assembly : null;
            try
            {
                MethodInfo read = bad.LoadFromAssemblyPath(dll).GetType("HarmonyLib.AccessTools")!.GetMethod("ReadAliases", BindingFlags.Static | BindingFlags.NonPublic)!;
                try { read.Invoke(null, null); check(false, message); }
                catch (TargetInvocationException e) { check(e.InnerException is InvalidDataException, message); }
            }
            finally { bad.Unload(); }
        }

        void Reject(Action action, string message)
        {
            try { action(); check(false, message); }
            catch (InvalidDataException) { check(true, message); }
        }
    }

    private static AssemblyDefinition Source(string name) => AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0, 0, 0)), name, ModuleKind.Dll);
    private static TypeDefinition Add(AssemblyDefinition source, string ns, string name)
    {
        var type = new TypeDefinition(ns, name, Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, source.MainModule.TypeSystem.Object);
        source.MainModule.Types.Add(type); return type;
    }

    private sealed class LookupAssembly : Assembly
    {
        private readonly string name;
        internal readonly Dictionary<string, Type> Types;
        internal readonly List<string> Requests = new();
        internal int EnumerationCalls;
        internal LookupAssembly(string name, Dictionary<string, Type> types) { this.name = name; Types = types; }
        public override string FullName => name + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
        public override AssemblyName GetName(bool copiedName) => new AssemblyName(FullName);
        public override Type? GetType(string name, bool throwOnError, bool ignoreCase)
        {
            if (throwOnError || ignoreCase) throw new InvalidOperationException("Lookup must be exact and optional.");
            Requests.Add(name); return Types.TryGetValue(name, out Type? found) ? found : null;
        }
        public override Type[] GetTypes() { EnumerationCalls++; throw new InvalidOperationException("Unrelated runtime type enumeration is forbidden."); }
    }

    private sealed class PoisonType : TypeDelegator
    {
        internal int MetadataReads;
        internal PoisonType() : base(typeof(HarmonyLookupTests)) { }
        public override string? FullName { get { MetadataReads++; throw new InvalidOperationException("Unrelated RuntimeType.FullName is forbidden."); } }
        public override string Name { get { MetadataReads++; throw new InvalidOperationException("Unrelated RuntimeType.Name is forbidden."); } }
    }
}
