using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

public sealed class AuditReport
{
    public int Version { get; set; } = 1;
    public bool Complete { get; set; }
    public string ModSha256 { get; set; } = "";
    public int PatchClasses { get; set; }
    public int AttributedHookMethods { get; set; }
    public int WovenTargets { get; set; }
    public int ProtectedTypesVerified { get; set; }
    public List<HookEvidence> Hooks { get; set; } = new();
    public List<CallEvidence> Registrations { get; set; } = new();
    public List<CallEvidence> FieldHelpers { get; set; } = new();
    public List<IntegrationIssue> Issues { get; set; } = new();
    public List<string> HarmonyApi { get; set; } = new();
    public List<IntegrationIssue> AotRisks { get; set; } = new();
    public Dictionary<string, string> InputAssemblies { get; set; } = new();
}

public sealed record HookEvidence(string Patch, string Kind, string? Target, int Priority);
public sealed record CallEvidence(string Caller, int IlOffset, string Api, string[] Strings);
public sealed record IntegrationIssue(string Code, string Subject, string Detail);
internal sealed record HookDefinition(MethodDefinition Patch, MethodDefinition Target, string Kind, int Priority);
internal sealed record FieldHelper(MethodDefinition Caller, Instruction Call, Instruction Name, FieldDefinition Field, GenericInstanceMethod Factory);

internal sealed class Discovery : IDisposable
{
    private readonly DefaultAssemblyResolver resolver;
    public AssemblyDefinition Mod { get; }
    public string ModPath { get; }
    public string ManagedPath { get; }
    public List<HookDefinition> Hooks { get; } = new();
    public List<FieldHelper> Helpers { get; } = new();
    public List<AssemblyDefinition> Loaded { get; } = new();
    private AuditReport? cached;
    private readonly List<AssemblyDefinition> typeSearch = new();

    private Discovery(string modPath, string managedPath, DefaultAssemblyResolver ar, AssemblyDefinition mod)
    { ModPath = modPath; ManagedPath = managedPath; resolver = ar; Mod = mod; Loaded.Add(mod); }

    public static Discovery Load(string modPath, string managedPath)
    {
        if (!File.Exists(modPath)) throw new FileNotFoundException("Mod assembly is missing.", modPath);
        if (!Directory.Exists(managedPath)) throw new DirectoryNotFoundException(managedPath);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedPath);
        resolver.AddSearchDirectory(Path.GetDirectoryName(modPath)!);
        // A compiler's reference facade is useful only for metadata resolution; target bodies
        // still come exclusively from the player's input, and are verified before weaving.
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string harmony = Path.Combine(packages, "harmonyx", "2.7.0", "lib", "netstandard2.0");
        if (Directory.Exists(harmony)) resolver.AddSearchDirectory(harmony);
        var mod = AssemblyDefinition.ReadAssembly(modPath, new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
        return new Discovery(modPath, managedPath, resolver, mod);
    }

    internal static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module) => module.Types.SelectMany(Descendants);
    private static IEnumerable<TypeDefinition> Descendants(TypeDefinition t)
    { yield return t; foreach (TypeDefinition n in t.NestedTypes) foreach (TypeDefinition d in Descendants(n)) yield return d; }
    internal static string MethodKey(MethodReference m) => m.DeclaringType.Scope.Name.Replace(".dll", "", StringComparison.Ordinal)
        + "/" + m.DeclaringType.FullName + "::" + m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")";

    public AuditReport Audit()
    {
        if (cached != null) return cached;
        var report = new AuditReport { ModSha256 = Hash(ModPath) };
        if (Mod.MainModule.Resources.Any(r => r.Name == "QuestWeaver.Hooks.v1")) report.Issues.Add(new IntegrationIssue("ALREADY_WOVEN", Mod.Name.Name, "The mod input already contains generated Quest integration. Build from a fresh current mod output and original game assemblies."));
        report.Issues.AddRange(HarmonyFacade.Validate(Mod.MainModule));
        report.HarmonyApi = Mod.MainModule.GetMemberReferences().Where(m => m.DeclaringType.FullName.StartsWith("HarmonyLib.", StringComparison.Ordinal)).Select(m => m.FullName).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        foreach (TypeDefinition type in AllTypes(Mod.MainModule))
            if (type.BaseType?.FullName == "BepInEx.BaseUnityPlugin") report.AotRisks.Add(new IntegrationIssue("BEPINEX_MONO_BOOTSTRAP", type.FullName,
                "Original BaseUnityPlugin constructor requires initialized Chainloader, Paths, logger/config and assembly location; Quest needs a verified standalone lifecycle adapter."));
        cached = report;
        var direct = new HashSet<MethodDefinition>();
        foreach (TypeDefinition type in AllTypes(Mod.MainModule))
        {
            bool patchClass = type.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch")
                || type.Methods.Any(m => m.CustomAttributes.Any(a => a.AttributeType.FullName.StartsWith("HarmonyLib.Harmony", StringComparison.Ordinal)));
            if (patchClass)
            {
                report.PatchClasses++;
                bool resolverMethod = type.Methods.Any(m => m.Name is "TargetMethod" or "TargetMethods"
                    || m.CustomAttributes.Any(a => a.AttributeType.Name is "HarmonyTargetMethod" or "HarmonyTargetMethods"));
                foreach (MethodDefinition patch in type.Methods)
                {
                    string? kind = Kind(patch);
                    if (kind == null) continue;
                    report.AttributedHookMethods++;
                    int priority = Priority(patch, type);
                    MethodDefinition? target = null;
                    string? failure = null;
                    try
                    {
                        target = ResolveTarget(type, patch);
                        if (target == null && resolverMethod) failure = "Target selector must be resolved from metadata-compatible IL; executing the mod and Unity startup on the build host is prohibited.";
                        else if (target == null) failure = "No complete target type/name metadata was found.";
                    }
                    catch (Exception e) when (e is AssemblyResolutionException or InvalidOperationException or ArgumentException)
                    { failure = e.Message; }
                    if (target == null && resolverMethod)
                    {
                        MethodDefinition[] candidates = SelectorCandidates(type).Where(t => ValidateHook(patch, t, kind) == null).ToArray();
                        if (candidates.Length > 0)
                        {
                            foreach (MethodDefinition candidate in candidates) Add(patch, candidate, kind, priority);
                            continue;
                        }
                    }
                    report.Hooks.Add(new HookEvidence(patch.FullName, kind, target?.FullName, priority));
                    if (failure != null) report.Issues.Add(new IntegrationIssue("TARGET_UNRESOLVED", patch.FullName, failure));
                    if (target == null) continue;
                    Track(target.Module.Assembly);
                    string? unsupported = ValidateHook(patch, target, kind);
                    if (unsupported != null) report.Issues.Add(new IntegrationIssue("HOOK_UNSUPPORTED", patch.FullName, unsupported));
                    else Hooks.Add(new HookDefinition(patch, target, kind, priority));
                }
            }
            foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
            {
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MethodReference call) continue;
                    if (call.DeclaringType.FullName == "System.Reflection.Assembly" && call.Name.StartsWith("Load", StringComparison.Ordinal))
                        report.AotRisks.Add(new IntegrationIssue("DYNAMIC_ASSEMBLY_LOADING", method.FullName, call.FullName));
                    if (call.DeclaringType.FullName == "System.Reflection.Assembly" && call.Name == "get_Location")
                        report.AotRisks.Add(new IntegrationIssue("ASSEMBLY_LOCATION_PATH", method.FullName, "Use a verified Quest persistent/content root; an IL2CPP assembly does not provide a desktop DLL directory."));
                    string[] strings = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).Distinct().ToArray();
                    if (call.DeclaringType.FullName == "HarmonyLib.Harmony" && call.Name is "Patch" or "PatchAll" or "UnpatchSelf" or "Unpatch" or "UnpatchAll")
                    {
                        report.Registrations.Add(new CallEvidence(method.FullName, instruction.Offset, call.FullName, strings));
                        if (call.Name == "Patch") direct.Add(method);
                        if (call.Name is "Unpatch" or "UnpatchAll") report.Issues.Add(new IntegrationIssue("UNPATCH_UNSUPPORTED", method.FullName, call.FullName));
                    }
                    if (call.DeclaringType.FullName == "HarmonyLib.AccessTools" && call.Name.Contains("FieldRefAccess", StringComparison.Ordinal))
                    {
                        report.FieldHelpers.Add(new CallEvidence(method.FullName, instruction.Offset, call.FullName, strings));
                        ResolveHelper(method, instruction, report);
                    }
                    if (call.DeclaringType.Namespace.StartsWith("System.Reflection.Emit", StringComparison.Ordinal))
                        report.Issues.Add(new IntegrationIssue("DYNAMIC_CODE", method.FullName, call.FullName));
                }
            }
            foreach (MethodDefinition native in type.Methods.Where(m => m.IsPInvokeImpl))
                report.AotRisks.Add(new IntegrationIssue("ANDROID_NATIVE_ENTRYPOINT", native.FullName, native.PInvokeInfo.Module.Name + ":" + native.PInvokeInfo.EntryPoint));
        }
        foreach (MethodDefinition method in direct)
        {
            MethodDefinition[] closure = MetadataClosure(new[] { method });
            MethodDefinition[] targets = CandidateTargets(closure);
            int added = 0;
            try
            {
                foreach (DirectHook registration in DirectRegistrations.Read(method, FindType))
                {
                    MethodDefinition[] candidates = (registration.Targets ?? targets).Where(t => ValidateHook(registration.Patch, t, registration.Kind) == null).ToArray();
                    if (candidates.Length == 0) throw new InvalidOperationException("No compatible target for " + registration.Kind + " " + registration.Patch.FullName);
                    foreach (MethodDefinition candidate in candidates)
                    { Add(registration.Patch, candidate, registration.Kind, Priority(registration.Patch, registration.Patch.DeclaringType)); added++; }
                }
            }
            catch (Exception e) when (e is InvalidOperationException or AssemblyResolutionException)
            { report.Issues.Add(new IntegrationIssue("DIRECT_REGISTRATION_UNRESOLVED", method.FullName, e.Message)); }
            if (added == 0) report.Issues.Add(new IntegrationIssue("DIRECT_REGISTRATION_UNRESOLVED", method.FullName,
                "The direct registration's candidate target/patch closure is not derivable from current static metadata."));
        }
        foreach (AssemblyDefinition assembly in Loaded.Distinct())
        {
            string file = assembly.MainModule.FileName;
            if (File.Exists(file)) report.InputAssemblies[assembly.Name.Name] = Hash(file);
        }
        report.Complete = report.Issues.Count == 0;
        return report;

        void Add(MethodDefinition patch, MethodDefinition target, string kind, int priority)
        {
            if (Hooks.Any(h => h.Patch == patch && h.Target == target && h.Kind == kind)) return;
            Track(target.Module.Assembly);
            Hooks.Add(new HookDefinition(patch, target, kind, priority));
            report.Hooks.Add(new HookEvidence(patch.FullName, kind, target.FullName, priority));
        }
    }

    private MethodDefinition[] SelectorCandidates(TypeDefinition type)
    {
        MethodDefinition[] selectors = type.Methods.Where(m => m.Name is "TargetMethod" or "TargetMethods").ToArray();
        return CandidateTargets(MetadataClosure(selectors));
    }

    private MethodDefinition[] MetadataClosure(IEnumerable<MethodDefinition> roots)
    {
        var seen = new HashSet<MethodDefinition>();
        var queue = new Queue<MethodDefinition>(roots);
        while (queue.Count > 0)
        {
            MethodDefinition method = queue.Dequeue();
            if (!method.HasBody || !seen.Add(method)) continue;
            if (seen.Count > 256) throw new InvalidOperationException("Metadata resolver closure exceeds the bounded 256-method budget: " + method.FullName);
            foreach (Instruction i in method.Body.Instructions)
            {
                if (i.Operand is FieldReference field && field.DeclaringType.Scope == Mod.MainModule)
                    foreach (MethodDefinition cctor in field.DeclaringType.Resolve().Methods.Where(m => m.IsConstructor && m.IsStatic)) queue.Enqueue(cctor);
                if (i.Operand is not MethodReference call || call.DeclaringType.Scope != Mod.MainModule) continue;
                MethodDefinition? callee = call.Resolve();
                if (callee == null) continue;
                if (callee.IsConstructor && callee.DeclaringType.IsNested && callee.DeclaringType.Name.StartsWith("<", StringComparison.Ordinal))
                    foreach (MethodDefinition generated in callee.DeclaringType.Methods) queue.Enqueue(generated);
                else if (call.ReturnType.FullName.Contains("System.Reflection.Method", StringComparison.Ordinal)
                    || call.ReturnType.FullName == "System.Boolean" && callee.Name.StartsWith("Is", StringComparison.Ordinal)) queue.Enqueue(callee);
            }
        }
        return seen.ToArray();
    }

    private MethodDefinition[] CandidateTargets(MethodDefinition[] closure)
    {
        var types = new HashSet<TypeDefinition>();
        var strings = closure.SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToHashSet(StringComparer.Ordinal);
        foreach (Instruction i in closure.SelectMany(m => m.Body.Instructions))
            if (i.OpCode == OpCodes.Ldtoken && i.Operand is TypeReference tr)
            {
                try { TypeDefinition? td = tr.Resolve(); if (td != null && td.Module != Mod.MainModule) types.Add(td); }
                catch (AssemblyResolutionException) { }
            }
        foreach (string literal in strings.ToArray())
            if (literal.Contains(':', StringComparison.Ordinal))
            { string[] pair = literal.Split(':'); if (pair.Length == 2) { strings.Add(pair[0]); strings.Add(pair[1]); } }
        EnsureTypeSearch();
        TypeDefinition[] available = typeSearch.SelectMany(a => AllTypes(a.MainModule)).ToArray();
        foreach (string name in strings)
        {
            TypeDefinition[] exact = available.Where(t => t.FullName == name).ToArray();
            // Harmony prefers a full name (including a global type's simple full name) over
            // simple-name fallback. Do not drag SonyNP.Main into a literal global Main hook.
            foreach (TypeDefinition type in exact.Length > 0 ? exact : available.Where(t => t.Name == name)) types.Add(type);
        }
        return types.SelectMany(t => t.Methods).Where(m => strings.Contains(m.Name)
            || strings.Any(s => s.StartsWith(".", StringComparison.Ordinal) && m.Name.EndsWith(s, StringComparison.Ordinal))).Distinct().ToArray();
    }

    private void EnsureTypeSearch()
    {
        if (typeSearch.Count != 0) return;
        foreach (string file in Directory.EnumerateFiles(ManagedPath, "*.dll"))
        {
            try
            {
                using AssemblyDefinition header = AssemblyDefinition.ReadAssembly(file);
                AssemblyDefinition assembly = resolver.Resolve(AssemblyNameReference.Parse(header.Name.FullName));
                if (!typeSearch.Contains(assembly)) typeSearch.Add(assembly);
            }
            catch (BadImageFormatException) { }
            catch (AssemblyResolutionException) { }
        }
    }

    private TypeDefinition? FindType(string name)
    {
        EnsureTypeSearch();
        return typeSearch.SelectMany(a => AllTypes(a.MainModule)).FirstOrDefault(t => t.FullName == name)
            ?? typeSearch.SelectMany(a => AllTypes(a.MainModule)).FirstOrDefault(t => t.Name == name);
    }

    private void ResolveHelper(MethodDefinition method, Instruction instruction, AuditReport report)
    {
        string? issue = null;
        if (instruction.Operand is not GenericInstanceMethod factory || factory.GenericArguments.Count != 2)
            issue = "Only the actual reference-returning FieldRefAccess<T,F>(string) form is supported.";
        else if (instruction.Previous?.OpCode != OpCodes.Ldstr || factory.Parameters.Count != 1)
            issue = "Field name is not a literal directly preceding the factory call.";
        else
        {
            try
            {
                TypeDefinition? type = factory.GenericArguments[0].Resolve();
                string name = (string)instruction.Previous.Operand;
                FieldDefinition? field = null;
                for (TypeDefinition? t = type; t != null && field == null; t = t.BaseType?.Resolve())
                    field = t.Fields.FirstOrDefault(f => f.Name == name);
                if (field == null) issue = "The referenced private field does not exist: " + name;
                else if (field.FieldType.FullName != factory.GenericArguments[1].FullName)
                    issue = "Field reference type mismatch: " + field.FieldType.FullName + " vs " + factory.GenericArguments[1].FullName;
                else if (Protected(field.DeclaringType)) issue = "Reference accessor would modify a protected game type.";
                else { Track(field.Module.Assembly); Helpers.Add(new FieldHelper(method, instruction, instruction.Previous, field, factory)); }
            }
            catch (Exception e) when (e is AssemblyResolutionException or InvalidOperationException)
            { issue = e.Message; }
        }
        if (issue != null) report.Issues.Add(new IntegrationIssue("FIELD_HELPER_UNSUPPORTED", method.FullName, issue));
    }

    private void Track(AssemblyDefinition assembly)
    { if (!Loaded.Contains(assembly)) Loaded.Add(assembly); }

    private MethodDefinition? ResolveTarget(TypeDefinition type, MethodDefinition method)
    {
        TypeReference? targetType = null;
        string? name = null;
        TypeReference[]? args = null;
        int methodType = 0;
        foreach (CustomAttribute attribute in type.CustomAttributes.Concat(method.CustomAttributes).Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
        {
            foreach (CustomAttributeArgument arg in attribute.ConstructorArguments)
            {
                if (arg.Type.FullName == "System.Type") targetType = arg.Value as TypeReference;
                else if (arg.Type.FullName == "System.String") name = arg.Value as string;
                else if (arg.Type.FullName == "System.Type[]") args = ((CustomAttributeArgument[])arg.Value).Select(a => (TypeReference)a.Value).ToArray();
                else if (arg.Type.FullName == "HarmonyLib.MethodType") methodType = Convert.ToInt32(arg.Value);
                else if (arg.Type.FullName == "HarmonyLib.ArgumentType[]") throw new InvalidOperationException("Harmony argument-variation metadata needs explicit by-ref conversion.");
            }
        }
        if (targetType == null || (name == null && methodType is not (3 or 4))) return null;
        string targetName = methodType switch { 1 => "get_" + name, 2 => "set_" + name, 3 => ".ctor", 4 => ".cctor", 0 => name!, _ => throw new InvalidOperationException("Unsupported Harmony MethodType " + methodType) };
        TypeDefinition definition = targetType.Resolve() ?? throw new InvalidOperationException("Target type cannot be resolved: " + targetType.FullName);
        MethodDefinition[] candidates = definition.Methods.Where(m => m.Name == targetName && (args == null ||
            m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args.Select(a => a.FullName)))).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("Target overload is ambiguous or missing: " + definition.FullName + "." + targetName + " matches=" + candidates.Length);
        return candidates[0];
    }

    internal static bool Protected(TypeDefinition t) => t.Module.Assembly.Name.Name == "ScenarioRuleLibrary"
        || t.Module.Assembly.Name.Name.StartsWith("Bolt", StringComparison.Ordinal) || t.FullName == "FFSNet.NetworkManager"
        || t.FullName.StartsWith("FFSNet.NetworkManager/", StringComparison.Ordinal);

    private static string? ValidateHook(MethodDefinition patch, MethodDefinition target, string kind)
    {
        if (Protected(target.DeclaringType)) return "The target is protected by project contracts and must remain unmodified.";
        if (kind is not ("prefix" or "postfix" or "finalizer")) return "Only Prefix, Postfix and Finalizer are supported, not " + kind;
        if (!target.HasBody || target.IsConstructor || target.HasGenericParameters || target.DeclaringType.HasGenericParameters)
            return "Target has no managed body, is a constructor, or requires generic wrapper specialization.";
        if (target.DeclaringType.IsValueType || target.ReturnType.IsByReference) return "Value-type instance and reference-returning original wrappers require additional closure.";
        if (!patch.IsStatic || patch.HasGenericParameters) return "Patch must be a non-generic static method.";
        if (kind == "prefix" && patch.ReturnType.MetadataType is not (MetadataType.Void or MetadataType.Boolean)) return "Prefix return must be void or bool.";
        if (kind == "postfix" && patch.ReturnType.MetadataType != MetadataType.Void) return "Pass-through postfix results require explicit ordering support.";
        if (kind == "finalizer" && patch.ReturnType.MetadataType != MetadataType.Void && patch.ReturnType.FullName != "System.Exception") return "Finalizer return must be void or Exception.";
        foreach (CustomAttribute a in patch.CustomAttributes.Concat(patch.DeclaringType.CustomAttributes))
            if (a.AttributeType.Name is "HarmonyBefore" or "HarmonyAfter" or "HarmonyArgument") return "Patch ordering/argument remapping attribute requires explicit generated support: " + a.AttributeType.Name;
        foreach (ParameterDefinition p in patch.Parameters)
        {
            string name = p.Name;
            TypeReference pt = p.ParameterType is ByReferenceType br ? br.ElementType : p.ParameterType;
            if (name == "__state") continue;
            if (name == "__instance") { if (target.IsStatic || p.ParameterType.IsByReference || !Assignable(target.DeclaringType, pt)) return "Original cannot supply this __instance type."; continue; }
            if (name == "__result") { if (target.ReturnType.MetadataType == MetadataType.Void || pt.FullName != target.ReturnType.FullName) return "Result type does not match original."; continue; }
            if (name == "__exception") { if (pt.FullName != "System.Exception") return "Exception parameter type mismatch."; continue; }
            if (name == "__originalMethod") { if (p.ParameterType.IsByReference) return "By-ref original method unsupported."; continue; }
            if (name == "__runOriginal") { if (pt.MetadataType != MetadataType.Boolean) return "Run-original flag must be bool."; continue; }
            if (name.StartsWith("___", StringComparison.Ordinal))
            {
                FieldDefinition? f = target.DeclaringType.Fields.FirstOrDefault(f => f.Name == name[3..]);
                if (f == null || f.FieldType.FullName != pt.FullName) return "Injected field missing or mismatched: " + name;
                continue;
            }
            ParameterDefinition? arg = target.Parameters.FirstOrDefault(a => a.Name == name);
            if (arg == null && name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(name[2..], out int index)) arg = target.Parameters.ElementAtOrDefault(index);
            if (arg == null) return "Unknown injected argument: " + name;
            TypeReference at = arg.ParameterType is ByReferenceType ab ? ab.ElementType : arg.ParameterType;
            if (!Assignable(at, pt)) return "Argument type mismatch for " + name + ": " + at.FullName + " vs " + pt.FullName;
            if (p.ParameterType.IsByReference && at.FullName != pt.FullName) return "By-ref argument type must match exactly: " + name;
        }
        return null;
    }

    private static bool Assignable(TypeReference actual, TypeReference wanted)
    {
        if (actual.FullName == wanted.FullName || wanted.FullName == "System.Object") return true;
        try
        {
            for (TypeDefinition? t = actual.Resolve(); t != null; t = t.BaseType?.Resolve())
                if (t.FullName == wanted.FullName || t.Interfaces.Any(i => i.InterfaceType.FullName == wanted.FullName)) return true;
        }
        catch (AssemblyResolutionException) { }
        return false;
    }

    private static string? Kind(MethodDefinition method)
    {
        foreach (string kind in new[] { "Prefix", "Postfix", "Finalizer", "Transpiler" })
            if (method.Name == kind || method.CustomAttributes.Any(a => a.AttributeType.Name == "Harmony" + kind)) return kind.ToLowerInvariant();
        return null;
    }
    private static int Priority(MethodDefinition method, TypeDefinition type) => method.CustomAttributes.Concat(type.CustomAttributes)
        .Where(a => a.AttributeType.Name == "HarmonyPriority").Select(a => Convert.ToInt32(a.ConstructorArguments[0].Value)).DefaultIfEmpty(400).First();
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public void Dispose() { foreach (AssemblyDefinition a in Loaded.Distinct()) a.Dispose(); resolver.Dispose(); }
}
