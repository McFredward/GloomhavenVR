using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver.Runtime;

namespace QuestWeaver;

internal static class RegistryVersionSpecializer
{
    internal static TypeDefinition Apply(ModuleDefinition mod, MethodDefinition resolver)
    {
        var factoryCalls = resolver.Body.Instructions.Where(i => i.Operand is MethodReference c && c.DeclaringType.FullName == "HarmonyLib.AccessTools" && c.Name == "FieldRefAccess").ToArray();
        string[] names = resolver.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
        if (factoryCalls.Length != 1 || !names.SequenceEqual(new[] { "_version", "m_version", "version" })
            || factoryCalls[0].Operand is not GenericInstanceMethod factory || factory.GenericArguments.Count != 2
            || factory.Parameters.Count != 1 || factory.Parameters[0].ParameterType.FullName != "System.String"
            || factory.GenericArguments[0].FullName != "System.Collections.Generic.HashSet`1<UnityEngine.UI.UIWindow>"
            || factory.GenericArguments[1].FullName != "System.Int32")
            throw new InvalidDataException("Native registry field factory changed its known readonly version contract.");
        TypeDefinition target = factory.GenericArguments[0].Resolve();
        if (!target.Fields.Any(f => names.Contains(f.Name, StringComparer.Ordinal) && !f.IsStatic && f.FieldType.FullName == "System.Int32"))
            throw new InvalidDataException("Native registry framework has no qualified integer version field.");
        if (resolver.ReturnType is not GenericInstanceType result || result.ElementType.FullName != "HarmonyLib.AccessTools/FieldRef`2"
            || !result.GenericArguments.Select(t => t.FullName).SequenceEqual(factory.GenericArguments.Select(t => t.FullName)))
            throw new InvalidDataException("Native registry resolver delegate identity changed.");
        string original = resolver.ReturnType.FullName;
        MethodDefinition[] methods = Discovery.AllTypes(mod).SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
        var relatedFactories = methods.SelectMany(m => m.Body.Instructions).Where(i => i.Operand is GenericInstanceMethod c
            && c.DeclaringType.FullName == "HarmonyLib.AccessTools" && c.Name == "FieldRefAccess"
            && c.GenericArguments.Select(t => t.FullName).SequenceEqual(factory.GenericArguments.Select(t => t.FullName))).ToArray();
        var relatedFields = Discovery.AllTypes(mod).SelectMany(t => t.Fields).Where(f => f.FieldType.FullName == original).ToArray();
        if (relatedFactories.Length != 1 || relatedFields.Length != 1 || relatedFields[0].Name != "NativeWindowRegistryVersion"
            || !relatedFields[0].IsStatic || !relatedFields[0].IsInitOnly
            || methods.Any(m => m != resolver && m.ReturnType.FullName == original)
            || methods.SelectMany(m => m.Body.Instructions).Any(i => (i.OpCode == OpCodes.Ldftn || i.OpCode == OpCodes.Ldvirtftn)
                && i.Operand is MethodReference call && call.FullName == resolver.FullName))
            throw new InvalidDataException("Native registry version delegate has another factory, storage or escaping function use.");
        var invokes = methods.SelectMany(m => m.Body.Instructions).Where(i => i.Operand is MethodReference c && c.DeclaringType.FullName == original).ToArray();
        if (invokes.Length < 4 || invokes.Any(i => i.Operand is not MethodReference c || c.Name != "Invoke" || i.OpCode != OpCodes.Callvirt
                || c.ReturnType is not ByReferenceType || i.Next?.OpCode != OpCodes.Ldind_I4))
            throw new InvalidDataException("Native registry version reference has an unknown call or escaping/write use.");
        using AssemblyDefinition runtime = AssemblyDefinition.ReadAssembly(typeof(FieldReaders).Assembly.Location);
        MethodReference imported = mod.ImportReference(runtime.MainModule.GetType("QuestWeaver.Runtime.FieldReaders").Methods.Single(m => m.Name == nameof(FieldReaders.Version)));
        var replacementFactory = new GenericInstanceMethod(imported); replacementFactory.GenericArguments.Add(mod.ImportReference(factory.GenericArguments[0]));
        var func = new GenericInstanceType(mod.ImportReference(imported.ReturnType is GenericInstanceType g ? g.ElementType : throw new InvalidDataException("Version reader delegate identity changed.")));
        foreach (TypeReference argument in factory.GenericArguments) func.GenericArguments.Add(mod.ImportReference(argument));
        // Replace only this closed readonly delegate specialization. Other FieldRefs
        // retain their existing static reference accessors and mutation semantics.
        foreach (TypeDefinition type in Discovery.AllTypes(mod))
        {
            foreach (FieldDefinition field in type.Fields) if (field.FieldType.FullName == original) field.FieldType = func;
            foreach (MethodDefinition method in type.Methods)
            {
                if (method.ReturnType.FullName == original) method.ReturnType = func;
                foreach (ParameterDefinition parameter in method.Parameters) if (parameter.ParameterType.FullName == original)
                    throw new InvalidDataException("Native registry reference escaped through a parameter.");
                if (!method.HasBody) continue;
                foreach (VariableDefinition local in method.Body.Variables) if (local.VariableType.FullName == original) local.VariableType = func;
                foreach (Instruction i in method.Body.Instructions)
                    if (i.Operand is FieldReference field && field.FieldType.FullName == original) field.FieldType = func;
            }
        }
        foreach (Instruction invoke in invokes)
        {
            var call = (MethodReference)invoke.Operand;
            var getter = new MethodReference("Invoke", func.ElementType.GenericParameters[1], func) { HasThis = true };
            // Func<T,int>'s metadata parameter is !0, not the closed collection type.
            getter.Parameters.Add(new ParameterDefinition(func.ElementType.GenericParameters[0]));
            invoke.Operand = getter; invoke.Next!.OpCode = OpCodes.Nop; invoke.Next.Operand = null;
        }
        factoryCalls[0].Operand = replacementFactory;
        return target;
    }
}
