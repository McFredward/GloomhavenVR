using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestWeaver.Runtime;

namespace HarmonyLib
{
    // The original HarmonyX initializer installs a runtime stack-trace detour. This facade
    // retains only metadata, ordinary reflection and precompiled activation. It contains no
    // MonoMod, Cecil, Reflection.Emit, stack-trace patcher or native detour dependency.
    public sealed class Harmony
    {
        public string Id { get; }
        public Harmony(string id) { if (string.IsNullOrEmpty(id)) throw new ArgumentException("Harmony owner ID is required.", nameof(id)); Id = id; }
        public void PatchAll(Type type) => Registry.PatchAll(this, type);
        public void UnpatchSelf() => Registry.UnpatchSelf(this);
        public MethodInfo? Patch(MethodBase original, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null,
            HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null, HarmonyMethod? ilManipulator = null)
            => Registry.Patch(this, original, prefix, postfix, transpiler, finalizer, ilManipulator);
    }

    public sealed class HarmonyMethod
    {
        public MethodInfo method;
        public HarmonyMethod(MethodInfo method) { this.method = method ?? throw new ArgumentNullException(nameof(method)); }
        public HarmonyMethod(Type type, string name, Type[]? argumentTypes = null)
        { method = AccessTools.Method(type, name, argumentTypes) ?? throw new MissingMethodException(type.FullName, name); }
    }

    public enum MethodType { Normal, Getter, Setter, Constructor, StaticConstructor, Enumerator, Async }
    public enum ArgumentType { Normal, Ref, Out, Pointer }
    public static class Priority { public const int Last = 0, VeryLow = 100, Low = 200, LowerThanNormal = 300, Normal = 400, HigherThanNormal = 500, High = 600, VeryHigh = 700, First = 800; }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type declaringType) { }
        public HarmonyPatch(string methodName) { }
        public HarmonyPatch(Type declaringType, string methodName) { }
        public HarmonyPatch(Type declaringType, string methodName, MethodType methodType) { }
        public HarmonyPatch(Type declaringType, string methodName, Type[] argumentTypes) { }
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPriority : Attribute { public HarmonyPriority(int priority) { } }

    public static class AccessTools
    {
        public delegate ref F FieldRef<in T, F>(T instance);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string fieldName)
            => throw new NotSupportedException("FieldRefAccess must be replaced by a compiled Quest accessor: " + typeof(T).FullName + "." + fieldName);
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Declared = All | BindingFlags.DeclaredOnly;

        public static Type? TypeByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Type? direct = Type.GetType(name, false);
            if (direct != null) return direct;
            return ResolveFromAssemblies(name, AppDomain.CurrentDomain.GetAssemblies());
        }

        // B615's verified native abort occurred while the old all-assembly scan read an
        // unrelated RuntimeType.FullName (IL2CPP GetBitmapNoInit/Class::Init). Catching a
        // managed exception cannot recover SIGABRT. Resolve only the requested type;
        // never enumerate or inspect unrelated runtime types, even for missing targets.
        // Harmony's full-name priority and first-loaded-assembly short-name fallback
        // remain intact. The weaver supplies short-name aliases from Cecil metadata,
        // so namespace/nested aliases do not require Assembly.GetTypes at runtime.
        private static Type? ResolveFromAssemblies(string name, Assembly[] assemblies)
        {
            foreach (Assembly assembly in assemblies)
            {
                if (assembly.FullName.StartsWith("Microsoft.VisualStudio", StringComparison.Ordinal)) continue;
                Type? exact = assembly.GetType(name, false);
                if (exact != null) return exact;
            }
            foreach (Assembly assembly in assemblies)
            {
                if (assembly.FullName.StartsWith("Microsoft.VisualStudio", StringComparison.Ordinal)) continue;
                string? assemblyName = assembly.GetName().Name;
                if (assemblyName == null || !Aliases.Value.TryGetValue(assemblyName, out Dictionary<string, string[]> byName)
                    || !byName.TryGetValue(name, out string[] candidates)) continue;
                foreach (string candidate in candidates)
                {
                    Type? alias = assembly.GetType(candidate, false);
                    if (alias != null) return alias;
                }
            }
            return null;
        }

        private const string AliasResource = "QuestWeaver.TypeAliases.v1";
        private static readonly Lazy<Dictionary<string, Dictionary<string, string[]>>> Aliases =
            new Lazy<Dictionary<string, Dictionary<string, string[]>>>(ReadAliases);

        private static Dictionary<string, Dictionary<string, string[]>> ReadAliases()
        {
            using (Stream stream = typeof(AccessTools).Assembly.GetManifestResourceStream(AliasResource)
                ?? throw new InvalidDataException("The Quest facade is missing its generated type-alias metadata."))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported Quest type-alias metadata version.");
                int assemblies = ReadCount(reader);
                var result = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.Ordinal);
                for (int a = 0; a < assemblies; a++)
                {
                    string assembly = reader.ReadString();
                    int names = ReadCount(reader);
                    var aliases = new Dictionary<string, string[]>(StringComparer.Ordinal);
                    for (int n = 0; n < names; n++)
                    {
                        string name = reader.ReadString();
                        int count = ReadCount(reader);
                        var candidates = new string[count];
                        for (int c = 0; c < count; c++) candidates[c] = reader.ReadString();
                        aliases.Add(name, candidates);
                    }
                    result.Add(assembly, aliases);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected Quest type-alias metadata tail.");
                return result;
            }
        }

        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 1000000) throw new InvalidDataException("Invalid Quest type-alias metadata count.");
            return count;
        }

        public static FieldInfo? Field(Type? type, string name)
        {
            for (Type? t = type; t != null; t = t.BaseType)
            { FieldInfo? field = t.GetField(name, All); if (field != null) return field; }
            return null;
        }
        public static PropertyInfo? Property(Type? type, string name)
        {
            for (Type? t = type; t != null; t = t.BaseType)
            { PropertyInfo? property = t.GetProperty(name, All); if (property != null) return property; }
            return null;
        }
        public static MethodInfo? PropertySetter(Type? type, string name) => Property(type, name)?.GetSetMethod(true);
        public static List<MethodInfo> GetDeclaredMethods(Type type) => type.GetMethods(Declared).ToList();
        public static MethodInfo? DeclaredMethod(Type? type, string name, Type[]? parameters = null, Type[]? generics = null)
        {
            if (type == null || name == null) return null;
            MethodInfo? method = parameters == null ? type.GetMethod(name, Declared) : type.GetMethod(name, Declared, null, parameters, null);
            return method != null && generics != null ? method.MakeGenericMethod(generics) : method;
        }
        public static MethodInfo? Method(Type? type, string name, Type[]? parameters = null, Type[]? generics = null)
        {
            if (type == null || name == null) return null;
            MethodInfo? method;
            try { method = Find(type, name, parameters); }
            catch (AmbiguousMatchException)
            {
                // Match HarmonyX's zero-argument fallback for overloads when no parameter
                // vector was supplied. Never choose an arbitrary overload.
                if (parameters != null) throw;
                method = Find(type, name, Type.EmptyTypes);
                if (method == null) throw;
            }
            return method != null && generics != null ? method.MakeGenericMethod(generics) : method;
        }
        private static MethodInfo? Find(Type type, string name, Type[]? parameters)
        {
            for (Type? t = type; t != null; t = t.BaseType)
            {
                MethodInfo? method = parameters == null ? t.GetMethod(name, All) : t.GetMethod(name, All, null, parameters, null);
                if (method != null) return method;
            }
            return null;
        }
        public static MethodInfo? Method(string typeColonMethodname, Type[]? parameters = null, Type[]? generics = null)
        {
            int colon = typeColonMethodname.LastIndexOf(':');
            if (colon <= 0 || colon == typeColonMethodname.Length - 1) throw new ArgumentException("Expected Type:Method.", nameof(typeColonMethodname));
            return Method(TypeByName(typeColonMethodname.Substring(0, colon)), typeColonMethodname.Substring(colon + 1), parameters, generics);
        }
    }
}
