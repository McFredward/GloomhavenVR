using Mono.Cecil;

namespace QuestWeaver;

internal static class HarmonyFacade
{
    internal static string Path => typeof(HarmonyLib.Harmony).Assembly.Location;
    public static IEnumerable<IntegrationIssue> Validate(ModuleDefinition mod)
    {
        using AssemblyDefinition facade = AssemblyDefinition.ReadAssembly(Path);
        TypeDefinition[] types = Discovery.AllTypes(facade.MainModule).ToArray();
        foreach (MemberReference member in mod.GetMemberReferences().Where(m => m.DeclaringType.Scope.Name is "0Harmony" or "0Harmony.dll"))
        {
            string typeName = member.DeclaringType is GenericInstanceType generic ? generic.ElementType.FullName : member.DeclaringType.FullName;
            TypeDefinition? type = types.FirstOrDefault(t => t.FullName == typeName);
            bool supported = type != null && (member switch
            {
                MethodReference method => type.Methods.Any(candidate => candidate.Name == method.Name && candidate.HasThis == method.HasThis
                    && candidate.GenericParameters.Count == method.GenericParameters.Count
                    && TypeKey(candidate.ReturnType) == TypeKey(method.ReturnType)
                    && candidate.Parameters.Select(p => TypeKey(p.ParameterType)).SequenceEqual(method.Parameters.Select(p => TypeKey(p.ParameterType)))),
                FieldReference field => type.Fields.Any(candidate => candidate.Name == field.Name && candidate.FieldType.FullName == field.FieldType.FullName),
                _ => false
            });
            if (!supported) yield return new IntegrationIssue("HARMONY_FACADE_API", member.FullName, "The AOT reflection/attribute facade does not expose this exact current mod API. Add and validate its AOT semantics before conversion.");
        }
    }

    private static string TypeKey(TypeReference type) => type switch
    {
        GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position,
        GenericInstanceType generic => generic.ElementType.FullName + "<" + string.Join(",", generic.GenericArguments.Select(TypeKey)) + ">",
        ByReferenceType byref => TypeKey(byref.ElementType) + "&",
        ArrayType array => TypeKey(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]",
        _ => type.FullName
    };

    public static void Write(string destination, AssemblyNameReference? original, IEnumerable<AssemblyDefinition> currentAssemblies, string managedPath)
    {
        using AssemblyDefinition facade = AssemblyDefinition.ReadAssembly(Path);
        if (original != null)
        {
            if (original.HasPublicKey || original.PublicKeyToken.Length > 0) throw new NotSupportedException("A signed Harmony identity requires an explicitly compatible facade.");
            facade.Name.Version = original.Version;
        }
        TypeLookupMetadata.Attach(facade, currentAssemblies, managedPath);
        facade.Write(destination);
    }
}
