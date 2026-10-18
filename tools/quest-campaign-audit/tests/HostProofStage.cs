using QuestWeaver;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class HostProofStage
{
    internal static void Stage(string source, string destination)
    {
        string managed = Path.GetFullPath(source), output = Path.GetFullPath(destination);
        if (output.StartsWith(managed + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Host proof output must be outside read-only original assemblies.");
        Directory.CreateDirectory(output);
        foreach (string path in Directory.EnumerateFiles(managed, "*.dll"))
            if (Path.GetFileName(path) is not ("OdinSerializer.dll" or "UnityEngine.CoreModule.dll"))
                File.Copy(path, Path.Combine(output, Path.GetFileName(path)), true);
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(managed);
        ReaderParameters read = new() { InMemory = true, AssemblyResolver = resolver };
        using (AssemblyDefinition core = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "UnityEngine.CoreModule.dll"), read))
        {
            // Native Unity getters need an explicit host-only substitute outside Unity.
            // Original game/serializer constructors, fields and callbacks remain intact.
            void Stub(string type, string name, object? value)
            {
                MethodDefinition method = core.MainModule.GetType(type).Methods.Single(method => method.Name == name);
                method.ImplAttributes = Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed;
                method.Attributes &= ~Mono.Cecil.MethodAttributes.PInvokeImpl;
                method.Body = new MethodBody(method);
                ILProcessor il = method.Body.GetILProcessor();
                if (value is int number) il.Emit(OpCodes.Ldc_I4, number);
                else if (value is string text) il.Emit(OpCodes.Ldstr, text);
                else if (type == "UnityEngine.DebugLogHandler")
                {
                    int argument = name == "Internal_Log" ? 2 : 0;
                    il.Emit(OpCodes.Ldarg, argument);
                    var console = new TypeReference("System", "Console", core.MainModule, core.MainModule.TypeSystem.CoreLibrary);
                    var write = new MethodReference("WriteLine", core.MainModule.TypeSystem.Void, console);
                    write.Parameters.Add(new ParameterDefinition(core.MainModule.TypeSystem.Object));
                    il.Emit(OpCodes.Call, write);
                }
                else if (method.ReturnType.FullName != "System.Void") throw new InvalidDataException("Unexpected host native stub signature.");
                il.Emit(OpCodes.Ret);
            }
            Stub("UnityEngine.Application", "get_platform", 11); // RuntimePlatform.Android
            Stub("UnityEngine.Application", "get_unityVersion", "2021.3.5f1");
            Stub("UnityEngine.Application", "get_version", "Host original serializer fixture (DUMMY)");
            Stub("UnityEngine.Application", "get_isEditor", 0);
            Stub("UnityEngine.DebugLogHandler", "Internal_Log", null);
            Stub("UnityEngine.DebugLogHandler", "Internal_LogException", null);
            core.Write(Path.Combine(output, "UnityEngine.CoreModule.dll"));
        }
        using (AssemblyDefinition original = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "OdinSerializer.dll"), read))
            original.Write(Path.Combine(output, "OdinSerializer.pc.dll"));
        using (AssemblyDefinition adapted = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "OdinSerializer.dll"), read))
        {
            StandaloneOdin.Apply(adapted, new StandaloneReport());
            adapted.Write(Path.Combine(output, "OdinSerializer.quest.dll"));
            // A negative capability oracle: poison every original runtime emit API
            // call, while preserving game/formatter bodies otherwise. Successful
            // roundtrips now establish these exact fixtures never reach codegen.
            IEnumerable<TypeDefinition> All(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(All));
            int poisoned = 0;
            var exception = new TypeReference("System", "NotSupportedException", adapted.MainModule, adapted.MainModule.TypeSystem.CoreLibrary);
            var constructor = new MethodReference(".ctor", adapted.MainModule.TypeSystem.Void, exception) { HasThis = true };
            constructor.Parameters.Add(new ParameterDefinition(adapted.MainModule.TypeSystem.String));
            var forbidden = new TypeDefinition("QuestHostOracle", "EmissionForbidden", Mono.Cecil.TypeAttributes.Class,
                adapted.MainModule.TypeSystem.Object);
            var fail = new MethodDefinition("Fail", Mono.Cecil.MethodAttributes.Assembly | Mono.Cecil.MethodAttributes.Static,
                adapted.MainModule.TypeSystem.Void);
            fail.Parameters.Add(new ParameterDefinition(adapted.MainModule.TypeSystem.String));
            forbidden.Methods.Add(fail);
            fail.Body.GetILProcessor().Emit(OpCodes.Ldarg_0);
            fail.Body.GetILProcessor().Emit(OpCodes.Newobj, constructor);
            fail.Body.GetILProcessor().Emit(OpCodes.Throw);
            adapted.MainModule.Types.Add(forbidden);
            foreach (MethodDefinition method in adapted.MainModule.Types.SelectMany(All).SelectMany(type => type.Methods).Where(method => method.HasBody))
            {
                // Inserted calls can exceed desktop short-branch reach. Expand the
                // existing branch opcodes without changing their target or behavior.
                foreach (Instruction instruction in method.Body.Instructions)
                    if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                        instruction.OpCode = (OpCode)typeof(OpCodes).GetField(instruction.OpCode.Code.ToString()[..^2])!.GetValue(null)!;
                foreach (Instruction instruction in method.Body.Instructions.ToArray())
                    if (instruction.Operand is MethodReference target
                        && (target.DeclaringType.FullName.StartsWith("System.Reflection.Emit.", StringComparison.Ordinal)
                            || target.DeclaringType.FullName == "System.AppDomain" && target.Name.Contains("DynamicAssembly", StringComparison.Ordinal)))
                    {
                        ILProcessor il = method.Body.GetILProcessor();
                        il.InsertBefore(instruction, Instruction.Create(OpCodes.Ldstr, "Host AOT oracle forbids runtime emit: " + target.FullName));
                        il.InsertBefore(instruction, Instruction.Create(OpCodes.Call, fail));
                        poisoned++;
                    }
            }
            if (poisoned < 300) throw new InvalidDataException("Original Odin emit-call inventory changed unexpectedly.");
            adapted.Write(Path.Combine(output, "OdinSerializer.quest-noemit.dll"));
            adapted.MainModule.GetType("OdinSerializer.Utilities.EmitUtilities").Methods.Single(method => method.Name == "get_CanEmit")
                .Body.Instructions[0].OpCode = OpCodes.Ldc_I4_1;
            adapted.Write(Path.Combine(output, "OdinSerializer.pc-noemit.dll"));
            Console.WriteLine("Host poison oracle blocks " + poisoned + " original Odin runtime emit API sites.");
        }
        Console.WriteLine("Staged original game DLLs plus explicitly host-only Unity native getter/logger substitutes; original and Quest Odin variants preserved separately.");
    }
}
