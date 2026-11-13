using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Read-only inventory. Direct call edges do not prove scene/serialized liveness.
static class Program
{
    static IEnumerable<TypeDefinition> Types(ModuleDefinition module)
    {
        IEnumerable<TypeDefinition> Descendants(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Descendants));
        return module.Types.SelectMany(Descendants);
    }

    static string? Concern(MethodReference method)
    {
        string type = method.DeclaringType.FullName;
        if ((type == "UnityEngine.SceneManagement.SceneManager" && method.Name.Contains("Scene", StringComparison.Ordinal))
            || type == "UnityEngine.SceneManagement.SceneUtility" && method.Name.Contains("Scene", StringComparison.Ordinal)
            || type == "UnityEngine.Application" && method.Name.Contains("Level", StringComparison.Ordinal)
            || type == "UnityEngine.SceneManagement.Scene" && method.Name == "get_buildIndex") return "scene";
        if (type.StartsWith("System.Reflection.Emit.", StringComparison.Ordinal)
            || type == "System.Reflection.Assembly" && method.Name.StartsWith("Load", StringComparison.Ordinal)
            || type == "System.AppDomain" && method.Name.Contains("Dynamic", StringComparison.Ordinal)
            || type.StartsWith("System.Linq.Expressions.", StringComparison.Ordinal) && method.Name == "Compile") return "dynamic-code";
        if (method.Name is "MakeGenericType" or "MakeGenericMethod" or "CreateInstance" && type.StartsWith("System.", StringComparison.Ordinal)) return "reflection-construction";
        return null;
    }

    static object[] Window(MethodDefinition method, int index)
    {
        return method.Body.Instructions.Skip(Math.Max(0, index - 8)).Take(17)
            .Select(instruction => (object)new { offset = instruction.Offset, opcode = instruction.OpCode.Name,
                operand = instruction.Operand?.ToString() }).ToArray();
    }

    static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: QuestCampaignAudit MANAGED_DIRECTORY OUTPUT_JSON"); return 2; }
        string managed = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        if (output.StartsWith(managed + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Audit output must be outside the original managed directory.");
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(managed);
        var libraries = new List<AssemblyDefinition>();
        try
        {
            foreach (string path in Directory.EnumerateFiles(managed, "*.dll").Order())
                if (!Path.GetFileName(path).StartsWith("UnityEngine", StringComparison.Ordinal)
                    && !Path.GetFileName(path).StartsWith("System", StringComparison.Ordinal)
                    && Path.GetFileName(path) is not ("mscorlib.dll" or "netstandard.dll" or "Mono.Security.dll"))
                    libraries.Add(AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, AssemblyResolver = resolver }));
            var imports = new List<object>(); var concerns = new List<object>(); var calls = new List<object>();
            foreach (AssemblyDefinition library in libraries)
                foreach (MethodDefinition method in Types(library.MainModule).SelectMany(type => type.Methods))
                {
                    if (method.IsPInvokeImpl)
                        imports.Add(new { assembly = library.Name.Name, method = method.FullName,
                            module = method.PInvokeInfo.Module.Name, entryPoint = method.PInvokeInfo.EntryPoint,
                            attributes = method.PInvokeInfo.Attributes.ToString() });
                    if (!method.HasBody) continue;
                    for (int index = 0; index < method.Body.Instructions.Count; index++)
                    {
                        Instruction instruction = method.Body.Instructions[index];
                        if (instruction.Operand is not MethodReference target) continue;
                        string? concern = Concern(target);
                        if (concern != null) concerns.Add(new { kind = concern, assembly = library.Name.Name,
                            caller = method.FullName, target = target.FullName, offset = instruction.Offset, window = Window(method, index) });
                        // Preserve all direct call/function-pointer edges. Callvirt and
                        // ldftn remain distinct; reflection and serialized roots need review.
                        if (instruction.OpCode.FlowControl == FlowControl.Call || instruction.OpCode == OpCodes.Ldftn || instruction.OpCode == OpCodes.Ldvirtftn)
                            calls.Add(new { assembly = library.Name.Name, caller = method.FullName, target = target.FullName,
                                targetScope = target.DeclaringType.Scope?.Name,
                                opcode = instruction.OpCode.Name, offset = instruction.Offset });
                    }
                }
            var report = new { schema = 1, scope = "original-custom-managed-IL-portability-inventory",
                evidenceLimit = "Direct original IL edges; serialized scene/component liveness and Android execution are separate evidence.",
                assemblies = libraries.Select(library => new { name = library.Name.Name,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library.MainModule.FileName))).ToLowerInvariant() }),
                imports, concerns, calls };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine(JsonSerializer.Serialize(new { assemblies = libraries.Count, imports = imports.Count, concerns = concerns.Count, directEdges = calls.Count }));
            return 0;
        }
        finally { foreach (AssemblyDefinition library in libraries) library.Dispose(); }
    }
}
