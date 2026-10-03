using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

// Read metadata without loading or executing any game assembly on the builder.
if (args.Length != 2)
{
    Console.Error.WriteLine("usage: ManagedInventory <managed-directory> <output.json>");
    return 2;
}
var assemblies = new SortedDictionary<string, object>(StringComparer.Ordinal);
foreach (var path in Directory.EnumerateFiles(args[0], "*.dll").Order(StringComparer.Ordinal))
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata) continue;
    var reader = pe.GetMetadataReader();
    var definition = reader.GetAssemblyDefinition();
    var types = new List<object>();
    foreach (var handle in reader.TypeDefinitions)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        if (name == "<Module>") continue;
        var ns = reader.GetString(type.Namespace);
        var declaring = type.GetDeclaringType();
        // Nested types are not independent Unity MonoScript components. Keep the
        // declared metadata names nevertheless; never invent a top-level type.
        types.Add(new { @namespace = ns, name, nested = !declaring.IsNil });
    }
    var references = reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)).Order(StringComparer.Ordinal).ToArray();
    var nativeImports = new List<object>();
    foreach (var handle in reader.MethodDefinitions)
    {
        var method = reader.GetMethodDefinition(handle);
        if ((method.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) == 0) continue;
        var import = method.GetImport();
        var owner = reader.GetTypeDefinition(method.GetDeclaringType());
        nativeImports.Add(new
        {
            type = reader.GetString(owner.Namespace) + "." + reader.GetString(owner.Name),
            method = reader.GetString(method.Name),
            library = reader.GetString(reader.GetModuleReference(import.Module).Name),
            entryPoint = reader.GetString(import.Name), flags = import.Attributes.ToString()
        });
    }
    assemblies[Path.GetFileName(path)] = new
    {
        name = reader.GetString(definition.Name), assemblyVersion = definition.Version.ToString(),
        types, references, nativeImports
    };
}
File.WriteAllText(args[1], JsonSerializer.Serialize(assemblies, new JsonSerializerOptions { WriteIndented = true }) + "\n");
return 0;
