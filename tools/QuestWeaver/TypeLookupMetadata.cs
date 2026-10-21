using System.Text;
using Mono.Cecil;

namespace QuestWeaver;

// Short-name lookup is computed on the PC from current input metadata, without
// loading any input assembly or executing a game/mod initializer. Only string
// names are shipped; this resource cannot root/initialize an unrelated IL2CPP type.
internal static class TypeLookupMetadata
{
    internal const string ResourceName = "QuestWeaver.TypeAliases.v1";

    internal static void Attach(AssemblyDefinition facade, IEnumerable<AssemblyDefinition> currentAssemblies, string managedPath)
    {
        AssemblyDefinition[] current = currentAssemblies.Distinct().ToArray();
        if (current.Select(a => a.Name.Name).Distinct(StringComparer.Ordinal).Count() != current.Length)
            throw new InvalidDataException("Duplicate current assembly identities make Quest type aliases ambiguous.");
        var sources = current.ToDictionary(a => a.Name.Name, StringComparer.Ordinal);
        var owned = new List<AssemblyDefinition>();
        try
        {
            foreach (string path in Directory.EnumerateFiles(managedPath, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
            {
                AssemblyDefinition assembly;
                try { assembly = AssemblyDefinition.ReadAssembly(path); }
                catch (BadImageFormatException) { continue; } // Native inputs have no managed type aliases.
                owned.Add(assembly);
                // Current compiled/weaved dependencies replace matching game input
                // identities; game metadata supplies names of other optional types.
                if (!sources.ContainsKey(assembly.Name.Name)) sources.Add(assembly.Name.Name, assembly);
            }
            sources[facade.Name.Name] = facade;
            byte[] bytes = Encode(sources.Values);
            if (facade.MainModule.Resources.Any(r => r.Name == ResourceName)) throw new InvalidDataException("Type-alias metadata was already generated.");
            facade.MainModule.Resources.Add(new EmbeddedResource(ResourceName, ManifestResourceAttributes.Private, bytes));
        }
        finally { foreach (AssemblyDefinition assembly in owned) assembly.Dispose(); }
    }

    internal static byte[] Encode(IEnumerable<AssemblyDefinition> sources)
    {
        AssemblyDefinition[] assemblies = sources.OrderBy(a => a.Name.Name, StringComparer.Ordinal).ToArray();
        if (assemblies.Select(a => a.Name.Name).Distinct(StringComparer.Ordinal).Count() != assemblies.Length)
            throw new InvalidDataException("Duplicate assembly identities make Quest type aliases ambiguous.");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(assemblies.Length);
            foreach (AssemblyDefinition assembly in assemblies)
            {
                writer.Write(assembly.Name.Name);
                // ECMA TypeDef row order matches Assembly.GetTypes ordering. Retain it
                // within each simple-name group; do not sort ambiguous candidates.
                IGrouping<string, TypeDefinition>[] aliases = Discovery.AllTypes(assembly.MainModule)
                    .Where(t => t.Name != "<Module>" && ReflectionName(t) != t.Name)
                    .OrderBy(t => t.MetadataToken.RID == 0 ? int.MaxValue : t.MetadataToken.ToInt32())
                    .GroupBy(t => t.Name, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
                writer.Write(aliases.Length);
                foreach (IGrouping<string, TypeDefinition> alias in aliases)
                {
                    writer.Write(alias.Key);
                    string[] names = alias.Select(ReflectionName).ToArray();
                    writer.Write(names.Length);
                    foreach (string name in names) writer.Write(name);
                }
            }
        }
        return stream.ToArray();
    }

    private static string ReflectionName(TypeDefinition type) => type.DeclaringType != null
        ? ReflectionName(type.DeclaringType) + "+" + Escape(type.Name)
        : (type.Namespace.Length == 0 ? "" : Escape(type.Namespace) + ".") + Escape(type.Name);

    private static string Escape(string name)
    {
        var result = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if ("\\,+&*[]".Contains(c, StringComparison.Ordinal)) result.Append('\\');
            result.Append(c);
        }
        return result.ToString();
    }
}
