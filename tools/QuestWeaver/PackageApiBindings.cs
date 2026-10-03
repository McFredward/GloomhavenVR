using System.Security.Cryptography;
using Mono.Cecil;

namespace QuestWeaver;

public sealed class PackageApiReport
{
    public int Schema { get; set; } = 1;
    public bool Complete { get; set; }
    public int CheckedTypeReferences { get; set; }
    public int CheckedMemberReferences { get; set; }
    public int ProtectedTypesVerified { get; set; }
    public int UnchangedTypesVerified { get; set; }
    public Dictionary<string, string> InputAssemblies { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> SdkAssemblies { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> OutputAssemblies { get; set; } = new(StringComparer.Ordinal);
    public List<PackageApiBinding> Rebindings { get; set; } = new();
    public string[] Modifications => Rebindings.Select(b => b.Assembly + ": " + b.OriginalMember + " -> " + b.BoundMember).ToArray();
    public List<string> Issues { get; set; } = new();
}

public sealed record PackageApiBinding(string Assembly, string OriginalMember, string BoundMember, string[] Callers);

/// <summary>Validate package ABI before IL2CPP; repair only proven covariant getter signatures.</summary>
internal static class PackageApiBindings
{
    // These original DLLs are disabled in the generated player in favour of actual
    // imported Unity packages. Script GUID rebinding cannot repair CLR member refs.
    internal static readonly string[] ReplacedAssemblies =
    {
        "Unity.InputSystem", "UnityEngine.UI", "Unity.Addressables",
        "Unity.ResourceManager", "Unity.ScriptableBuildPipeline",
        "Unity.XR.Management", "Unity.XR.OpenXR", "Unity.XR.CoreUtils"
    };

    internal static PackageApiReport Write(string managed, string sdk, string output, string? referenceManaged = null)
    {
        managed = Path.GetFullPath(managed); sdk = Path.GetFullPath(sdk); output = Path.GetFullPath(output);
        if (!Directory.Exists(managed) || !Directory.Exists(sdk))
            throw new ArgumentException("Package API input and imported SDK directories must exist.");
        if (output == managed || output.StartsWith(managed + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || output == sdk || output.StartsWith(sdk + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || managed.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || sdk.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new ArgumentException("Package API output must be empty and separate from immutable inputs.");
        var report = new PackageApiReport();
        using var resolver = new DefaultAssemblyResolver();
        // Target package assemblies must win over the original disabled packages.
        resolver.AddSearchDirectory(sdk);
        resolver.AddSearchDirectory(managed);
        if (referenceManaged != null)
        {
            referenceManaged = Path.GetFullPath(referenceManaged);
            if (!Directory.Exists(referenceManaged)) throw new ArgumentException("Reference managed directory must exist.");
            resolver.AddSearchDirectory(referenceManaged);
        }
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        foreach (string package in ReplacedAssemblies)
        {
            string path = Path.Combine(sdk, package + ".dll");
            if (File.Exists(path))
            {
                using AssemblyDefinition imported = AssemblyDefinition.ReadAssembly(path);
                if (imported.Name.Name != package) throw new InvalidDataException("Imported SDK assembly identity differs from its expected filename: " + package);
                report.SdkAssemblies[package + ".dll"] = Hash(path);
            }
        }
        var inputs = new List<(string Path, AssemblyDefinition Assembly, bool Changed)>();
        try
        {
            foreach (string path in Directory.EnumerateFiles(managed, "*.dll").Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(path);
                if (ReplacedAssemblies.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal)) continue;
                var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
                inputs.Add((path, assembly, false));
                report.InputAssemblies[name] = Hash(path);
                ModuleDefinition module = assembly.MainModule;
                Dictionary<string, string> protectedSnapshot = ProtectedTypes.Snapshot(assembly);
                Dictionary<string, string> typeSnapshots = Discovery.AllTypes(module).ToDictionary(t => t.FullName, ProtectedTypes.Fingerprint);
                var changedTypes = new HashSet<string>(StringComparer.Ordinal);
                foreach (TypeReference type in module.GetTypeReferences().Where(t => IsPackage(t)))
                {
                    report.CheckedTypeReferences++;
                    try
                    {
                        if (!report.SdkAssemblies.ContainsKey(Scope(type) + ".dll") || type.Resolve() == null)
                            report.Issues.Add(name + ": unavailable imported package type " + type.FullName);
                    }
                    catch (AssemblyResolutionException)
                    { report.Issues.Add(name + ": unresolved imported package type " + type.FullName); }
                }
                bool changed = false;
                foreach (MemberReference member in module.GetMemberReferences().Where(m => IsPackage(m.DeclaringType)))
                {
                    report.CheckedMemberReferences++;
                    bool valid;
                    try { valid = Valid(member, report); }
                    catch (AssemblyResolutionException) { valid = false; }
                    if (valid) continue;
                    if (member is MethodReference method && TryCovariantGetter(method, report, out MethodDefinition? replacement))
                    {
                        string before = method.FullName;
                        string[] callers = Discovery.AllTypes(module).SelectMany(t => t.Methods).Where(m => m.HasBody
                            && m.Body.Instructions.Any(i => ReferenceEquals(i.Operand, method))).Select(m => m.FullName).Order(StringComparer.Ordinal).ToArray();
                        foreach (TypeDefinition type in Discovery.AllTypes(module).Where(t => t.Methods.Any(m => m.HasBody
                            && m.Body.Instructions.Any(i => ReferenceEquals(i.Operand, method))))) changedTypes.Add(type.FullName);
                        // B612's real headset exception identifies Pointer.delta's
                        // Vector2Control -> DeltaControl return change in InputSystem1.7.
                        // DeltaControl derives from Vector2Control, so this changes only
                        // the binary getter signature. Existing input callbacks, opcodes,
                        // parameters, and all reads of the original Vector2 remain intact.
                        method.ReturnType = module.ImportReference(replacement!.ReturnType);
                        if (!Valid(method, report)) throw new InvalidDataException("Rebound package getter did not resolve: " + method.FullName);
                        report.Rebindings.Add(new PackageApiBinding(name, before, method.FullName, callers));
                        changed = true;
                    }
                    else report.Issues.Add(name + ": incompatible imported package member " + member.FullName);
                }
                report.ProtectedTypesVerified += ProtectedTypes.Verify(protectedSnapshot, assembly);
                foreach (TypeDefinition type in Discovery.AllTypes(module))
                {
                    if (changedTypes.Contains(type.FullName)) continue;
                    if (typeSnapshots[type.FullName] != ProtectedTypes.Fingerprint(type))
                        throw new InvalidDataException("Unrelated type changed during package API binding: " + type.FullName);
                    report.UnchangedTypesVerified++;
                }
                inputs[^1] = (path, assembly, changed);
            }
            if (inputs.Count == 0) throw new InvalidDataException("Package API preflight has no active managed assemblies.");
            if (report.Issues.Count != 0) return report; // No partial adapted payload can escape.
            foreach (var pair in report.SdkAssemblies)
                if (Hash(Path.Combine(sdk, pair.Key)) != pair.Value)
                    throw new InvalidDataException("Imported SDK changed during package API preflight: " + pair.Key);
            Directory.CreateDirectory(output);
            foreach (var input in inputs)
            {
                string name = Path.GetFileName(input.Path), destination = Path.Combine(output, name);
                if (Hash(input.Path) != report.InputAssemblies[name]) throw new InvalidDataException("Package API input changed during preflight: " + name);
                if (input.Changed) input.Assembly.Write(destination);
                else File.Copy(input.Path, destination);
                report.OutputAssemblies[name] = Hash(destination);
            }
            report.Complete = true;
            return report;
        }
        finally { foreach (var input in inputs) input.Assembly.Dispose(); }
    }

    static bool Valid(MemberReference member, PackageApiReport report)
    {
        if (!report.SdkAssemblies.ContainsKey(Scope(member.DeclaringType) + ".dll")) return false;
        if (member is MethodReference method)
        {
            MethodDefinition? resolved = method.Resolve();
            return resolved != null && resolved.IsStatic == !method.HasThis
                && resolved.GenericParameters.Count == method.GenericParameters.Count;
        }
        return member is FieldReference field && field.Resolve() != null;
    }

    static bool TryCovariantGetter(MethodReference method, PackageApiReport report, out MethodDefinition? replacement)
    {
        replacement = null;
        if (Scope(method.DeclaringType) != "Unity.InputSystem" || !report.SdkAssemblies.ContainsKey("Unity.InputSystem.dll")
            || !method.HasThis || method.Parameters.Count != 0 || method.GenericParameters.Count != 0
            || method.ReturnType.FullName != "UnityEngine.InputSystem.Controls.Vector2Control"
            || Scope(method.ReturnType) != "Unity.InputSystem"
            || !(method.DeclaringType.FullName == "UnityEngine.InputSystem.Pointer" && method.Name == "get_delta"
                || method.DeclaringType.FullName == "UnityEngine.InputSystem.Mouse" && method.Name == "get_scroll")) return false;
        TypeDefinition? owner = method.DeclaringType.Resolve();
        MethodDefinition[] candidates = owner?.Methods.Where(m => m.Name == method.Name && m.IsGetter && m.IsPublic
            && !m.IsStatic && m.Parameters.Count == 0 && m.GenericParameters.Count == 0
            && m.ReturnType.FullName == "UnityEngine.InputSystem.Controls.DeltaControl").ToArray() ?? Array.Empty<MethodDefinition>();
        if (candidates.Length != 1) return false;
        TypeDefinition? returnType = candidates[0].ReturnType.Resolve();
        if (returnType?.BaseType?.FullName != method.ReturnType.FullName
            || Scope(returnType.BaseType) != "Unity.InputSystem") return false;
        replacement = candidates[0]; return true;
    }

    static bool IsPackage(TypeReference type) => ReplacedAssemblies.Contains(Scope(type), StringComparer.Ordinal);
    static string Scope(TypeReference type)
    {
        while (type is TypeSpecification specification) type = specification.ElementType;
        while (type.DeclaringType != null) type = type.DeclaringType;
        return type.Scope.Name.EndsWith(".dll", StringComparison.Ordinal) ? type.Scope.Name[..^4] : type.Scope.Name;
    }
    static string Hash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
}
