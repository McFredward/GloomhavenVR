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
        MethodDefinition[] memberGuards = emit.Methods.Where(method => method.Name == "EmitIsIllegalForMember"
            && method.IsStatic && method.HasBody && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName == "System.Reflection.MemberInfo"
            && method.ReturnType.FullName == "System.Boolean").ToArray();
        if (memberGuards.Length != 1 || memberGuards[0].Body.Instructions.Any(instruction => instruction.Operand is MethodReference call
                && call.FullName == getters[0].FullName)
            || !memberGuards[0].Body.Instructions.Any(instruction => instruction.Operand is FieldReference field
                && field.DeclaringType == emit && field.Name == "EngineAssembly"))
            throw new InvalidDataException("Original Odin member emit guard changed; review its existing callback/member reflection branches.");
        MethodDefinition[] emittedHelpers = emit.Methods.Where(method => method.HasBody && method.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Newobj && instruction.Operand is MethodReference ctor
            && ctor.DeclaringType.FullName == "System.Reflection.Emit.DynamicMethod")).ToArray();
        if (emittedHelpers.Length != 23 || emittedHelpers.Any(method => !method.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference call && call.FullName == memberGuards[0].FullName)))
            throw new InvalidDataException("Original Odin emitted callback/member helper coverage changed.");
        getters[0].Body.Instructions[0].OpCode = OpCodes.Ldc_I4_0;
        // Desktop Odin's helper guard originally checks Unity-native members only.
        // Its existing reflection delegates also support managed callbacks and boxed
        // structs. Select those delegates whenever the staged AOT capability is false.
        ILProcessor il = memberGuards[0].Body.GetILProcessor();
        Instruction originalEntry = memberGuards[0].Body.Instructions[0];
        il.InsertBefore(originalEntry, Instruction.Create(OpCodes.Call, getters[0]));
        il.InsertBefore(originalEntry, Instruction.Create(OpCodes.Brtrue, originalEntry));
        il.InsertBefore(originalEntry, Instruction.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(originalEntry, Instruction.Create(OpCodes.Ret));
        report.Modifications.Add("Quest IL2CPP selects original Odin AOT/reflection formatter path; serializer, callbacks and binary format retained: " + getters[0].FullName);
        report.Modifications.Add("Quest IL2CPP selects all 23 original Odin callback/member reflection delegates through its platform member guard: " + memberGuards[0].FullName);
        return new[] { emit.FullName };
    }
}
