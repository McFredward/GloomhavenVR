using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

/// <summary>Selects the original serializer's existing AOT branch in staged Quest output.</summary>
internal static class StandaloneOdin
{
    internal static IEnumerable<string> Apply(AssemblyDefinition odin, StandaloneReport report)
    {
        if (odin.Name.Name != "OdinSerializer") throw new InvalidDataException("The original Odin serializer assembly identity changed.");
        TypeDefinition emit = odin.MainModule.GetType("OdinSerializer.Utilities.EmitUtilities")
            ?? throw new InvalidDataException("Original Odin emit capability type is absent.");
        MethodDefinition[] getters = emit.Methods.Where(method => method.Name == "get_CanEmit" && method.IsStatic
            && method.ReturnType.FullName == "System.Boolean" && method.Parameters.Count == 0 && method.HasBody).ToArray();
        if (getters.Length != 1 || getters[0].Body.Instructions.Count != 2
            || getters[0].Body.Instructions[0].OpCode != OpCodes.Ldc_I4_1 || getters[0].Body.Instructions[1].OpCode != OpCodes.Ret)
            throw new InvalidDataException("Original Odin desktop CanEmit constant changed; review its AOT selector.");
        TypeDefinition locator = odin.MainModule.GetType("OdinSerializer.FormatterLocator")
            ?? throw new InvalidDataException("Original Odin formatter locator is absent.");
        bool callsSelector(MethodDefinition method) => method.HasBody && method.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference call && call.FullName == getters[0].FullName);
        if (!locator.Methods.Any(method => method.Name == "CreateFormatter" && callsSelector(method)
            && method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj
                && instruction.Operand is MethodReference ctor && ctor.DeclaringType.FullName == "OdinSerializer.WeakReflectionFormatter"))
            || odin.MainModule.GetType("OdinSerializer.ReflectionFormatter`1") == null
            || odin.MainModule.GetType("OdinSerializer.WeakReflectionFormatter") == null
            || odin.MainModule.GetType("OdinSerializer.SerializationUtility") is not TypeDefinition utility
            || !utility.Methods.Any(method => method.Name == "SerializeValue" && callsSelector(method))
            || !utility.Methods.Any(method => method.Name == "DeserializeValue" && callsSelector(method)))
            throw new InvalidDataException("Original Odin reflection serialization/deserialization fallback changed.");
        getters[0].Body.Instructions[0].OpCode = OpCodes.Ldc_I4_0;
        report.Modifications.Add("Quest IL2CPP selects original Odin AOT/reflection formatter path; serializer, callbacks and binary format retained: " + getters[0].FullName);
        return new[] { emit.FullName };
    }
}
