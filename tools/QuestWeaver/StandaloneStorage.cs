using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

/// <summary>Adapts only the staged file-write seam; native serializers/queues remain original.</summary>
internal static class StandaloneStorage
{
    internal static void Bind(AssemblyDefinition game, ModuleDefinition compatibility,
        ISet<string> changedTypes, List<string> modifications)
    {
        TypeDefinition owner = game.MainModule.GetType("PlatformFileSystem")
            ?? throw new InvalidDataException("Original PlatformFileSystem storage ABI is missing.");
        MethodDefinition[] synchronous = owner.Methods.Where(m => m.Name == "WriteFile" && m.HasBody
            && m.ReturnType.FullName == "System.Void"
            && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.Byte[]", "System.String" })).ToArray();
        if (synchronous.Length != 1 || !owner.Methods.Any(m => m.Name == "WriteFileAsync" && m.HasBody
                && m.Parameters.Count == 3 && m.Parameters[0].ParameterType.FullName == "System.Byte[]"
                && m.Parameters[1].ParameterType.FullName == "System.String"))
            throw new InvalidDataException("Original synchronous/asynchronous save-file writer ABI changed.");
        TypeDefinition[] owners = Discovery.AllTypes(game.MainModule).Where(type => IsOwner(type, owner)).ToArray();
        var calls = owners.SelectMany(type => type.Methods).Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions.Where(i => i.Operand is MethodReference call
                && call.DeclaringType.FullName == "System.IO.File" && call.Name == "WriteAllBytes")
                .Select(instruction => (Method: method, Instruction: instruction))).ToArray();
        if (calls.Length != 2 || calls.Count(row => row.Method == synchronous[0]) != 1
            || calls.Count(row => row.Method != synchronous[0] && row.Method.DeclaringType != owner) != 1)
            throw new InvalidDataException("Original save writer no longer has one sync call and one native async worker call.");
        foreach (var row in calls)
        {
            var call = (MethodReference)row.Instruction.Operand;
            if (row.Instruction.OpCode != OpCodes.Call || call.HasThis || call.ReturnType.FullName != "System.Void"
                || !call.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.String", "System.Byte[]" })
                || Discovery.Protected(row.Method.DeclaringType))
                throw new InvalidDataException("Original native save File.WriteAllBytes call ABI changed: " + row.Method.FullName);
        }
        MethodDefinition writer = EmitWriter(compatibility);
        foreach (var row in calls)
        {
            row.Instruction.Operand = game.MainModule.ImportReference(writer);
            changedTypes.Add(row.Method.DeclaringType.FullName);
            modifications.Add("atomic native file bytes, original serializer/queue/callback retained: " + row.Method.FullName);
        }
    }

    private static bool IsOwner(TypeDefinition type, TypeDefinition owner)
    {
        for (TypeDefinition? current = type; current != null; current = current.DeclaringType)
            if (current == owner) return true;
        return false;
    }

    internal static MethodDefinition EmitWriter(ModuleDefinition module)
    {
        if (module.GetType("QuestGame.Compatibility.SaveFiles") != null)
            throw new InvalidDataException("Atomic native storage adapter was already emitted.");
        var type = new TypeDefinition("QuestGame.Compatibility", "SaveFiles",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(type);
        TypeReference Core(string ns, string name, bool valueType = false) => new(ns, name, module, module.TypeSystem.CoreLibrary, valueType);
        MethodReference Method(TypeReference owner, string name, TypeReference result, bool instance, params TypeReference[] arguments)
        {
            var method = new MethodReference(name, result, owner) { HasThis = instance };
            foreach (TypeReference argument in arguments) method.Parameters.Add(new ParameterDefinition(argument));
            return method;
        }
        TypeReference file = Core("System.IO", "File"), stream = Core("System.IO", "FileStream"), guid = Core("System", "Guid", true);
        var bytes = new ArrayType(module.TypeSystem.Byte);
        MethodReference exists = Method(file, "Exists", module.TypeSystem.Boolean, false, module.TypeSystem.String);
        MethodReference delete = Method(file, "Delete", module.TypeSystem.Void, false, module.TypeSystem.String);
        MethodReference nativeWrite = Method(file, "WriteAllBytes", module.TypeSystem.Void, false, module.TypeSystem.String, bytes);
        MethodReference replace = Method(file, "Replace", module.TypeSystem.Void, false, module.TypeSystem.String, module.TypeSystem.String, module.TypeSystem.String);
        MethodReference move = Method(file, "Move", module.TypeSystem.Void, false, module.TypeSystem.String, module.TypeSystem.String);
        MethodReference concat2 = Method(module.TypeSystem.String, "Concat", module.TypeSystem.String, false, module.TypeSystem.String, module.TypeSystem.String);
        MethodReference concat4 = Method(module.TypeSystem.String, "Concat", module.TypeSystem.String, false, module.TypeSystem.String, module.TypeSystem.String, module.TypeSystem.String, module.TypeSystem.String);
        MethodReference newGuid = Method(guid, "NewGuid", guid, false);
        MethodReference guidString = Method(guid, "ToString", module.TypeSystem.String, true, module.TypeSystem.String);
        MethodReference open = Method(stream, ".ctor", module.TypeSystem.Void, true, module.TypeSystem.String,
            Core("System.IO", "FileMode", true), Core("System.IO", "FileAccess", true), Core("System.IO", "FileShare", true));
        MethodReference flush = Method(stream, "Flush", module.TypeSystem.Void, true, module.TypeSystem.Boolean);
        MethodReference dispose = Method(Core("System.IO", "Stream"), "Dispose", module.TypeSystem.Void, true);
        // Cleanup must never replace the original IO exception. A leftover unique
        // temp is harmless; startup recovery reports it without claiming success.
        var cleanup = new MethodDefinition("TryDeleteTemporary", MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Void);
        cleanup.Parameters.Add(new ParameterDefinition("path", ParameterAttributes.None, module.TypeSystem.String));
        type.Methods.Add(cleanup);ILProcessor ci = cleanup.Body.GetILProcessor();
        Instruction cleanupStart = Instruction.Create(OpCodes.Ldarg_0), cleanupCatch = Instruction.Create(OpCodes.Pop), cleanupDone = Instruction.Create(OpCodes.Ret);
        Instruction cleanupLeave=Instruction.Create(OpCodes.Leave,cleanupDone);
        ci.Append(cleanupStart);ci.Emit(OpCodes.Call, exists);ci.Emit(OpCodes.Brfalse, cleanupLeave);
        ci.Emit(OpCodes.Ldarg_0);ci.Emit(OpCodes.Call, delete);ci.Append(cleanupLeave);
        ci.Append(cleanupCatch);ci.Emit(OpCodes.Leave, cleanupDone);ci.Append(cleanupDone);
        cleanup.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch) {
            TryStart=cleanupStart,TryEnd=cleanupCatch,HandlerStart=cleanupCatch,HandlerEnd=cleanupDone,CatchType=Core("System", "Exception") });

        var writer = new MethodDefinition("WriteAllBytes", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        writer.Parameters.Add(new ParameterDefinition("path", ParameterAttributes.None, module.TypeSystem.String));
        writer.Parameters.Add(new ParameterDefinition("bytes", ParameterAttributes.None, bytes));type.Methods.Add(writer);
        writer.Body.InitLocals=true;writer.Body.MaxStackSize=16;
        var nonce=new VariableDefinition(guid);var temporary=new VariableDefinition(module.TypeSystem.String);var handle=new VariableDefinition(stream);
        writer.Body.Variables.Add(nonce);writer.Body.Variables.Add(temporary);writer.Body.Variables.Add(handle);
        ILProcessor il=writer.Body.GetILProcessor();
        foreach ((int index,string name) in new[] { (0,"path"),(1,"bytes") })
        {
            Instruction valid=Instruction.Create(OpCodes.Nop);
            il.Emit(index==0?OpCodes.Ldarg_0:OpCodes.Ldarg_1);il.Emit(OpCodes.Brtrue,valid);il.Emit(OpCodes.Ldstr,name);
            il.Emit(OpCodes.Newobj,Method(Core("System", "ArgumentNullException"),".ctor",module.TypeSystem.Void,true,module.TypeSystem.String));
            il.Emit(OpCodes.Throw);il.Append(valid);
        }
        Instruction nonempty=Instruction.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Callvirt,Method(module.TypeSystem.String,"get_Length",module.TypeSystem.Int32,true));
        il.Emit(OpCodes.Brtrue,nonempty);il.Emit(OpCodes.Ldstr,"Empty path is invalid.");il.Emit(OpCodes.Ldstr,"path");
        il.Emit(OpCodes.Newobj,Method(Core("System", "ArgumentException"),".ctor",module.TypeSystem.Void,true,module.TypeSystem.String,module.TypeSystem.String));
        il.Emit(OpCodes.Throw);il.Append(nonempty);
        il.Emit(OpCodes.Call,newGuid);il.Emit(OpCodes.Stloc,nonce);
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldstr,".ghvr-save-write-");il.Emit(OpCodes.Ldloca,nonce);il.Emit(OpCodes.Ldstr,"N");
        il.Emit(OpCodes.Call,guidString);il.Emit(OpCodes.Ldstr,".tmp");il.Emit(OpCodes.Call,concat4);il.Emit(OpCodes.Stloc,temporary);
        Instruction outerStart=Instruction.Create(OpCodes.Ldloc,temporary), innerStart=Instruction.Create(OpCodes.Ldloc,handle),
            innerFinally=Instruction.Create(OpCodes.Ldloc,handle), afterFlush=Instruction.Create(OpCodes.Ldarg_0),
            firstWrite=Instruction.Create(OpCodes.Ldloc,temporary), outerFinally=Instruction.Create(OpCodes.Ldloc,temporary), done=Instruction.Create(OpCodes.Ret);
        il.Append(outerStart);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Call,nativeWrite);
        il.Emit(OpCodes.Ldloc,temporary);il.Emit(OpCodes.Ldc_I4_3);il.Emit(OpCodes.Ldc_I4_3);il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj,open);il.Emit(OpCodes.Stloc,handle);
        il.Append(innerStart);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Callvirt,flush);il.Emit(OpCodes.Leave,afterFlush);
        il.Append(innerFinally);il.Emit(OpCodes.Callvirt,dispose);il.Emit(OpCodes.Endfinally);
        il.Append(afterFlush);il.Emit(OpCodes.Call,exists);il.Emit(OpCodes.Brfalse,firstWrite);
        // Same-directory replace keeps the old complete file as a recovery copy.
        // Never delete/truncate the live target before the replacement commits.
        il.Emit(OpCodes.Ldloc,temporary);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldstr,".ghvr-save-backup");
        il.Emit(OpCodes.Call,concat2);il.Emit(OpCodes.Call,replace);il.Emit(OpCodes.Leave,done);
        il.Append(firstWrite);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,move);il.Emit(OpCodes.Leave,done);
        il.Append(outerFinally);il.Emit(OpCodes.Call,cleanup);il.Emit(OpCodes.Endfinally);il.Append(done);
        writer.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
            TryStart=innerStart,TryEnd=innerFinally,HandlerStart=innerFinally,HandlerEnd=afterFlush });
        writer.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
            TryStart=outerStart,TryEnd=outerFinally,HandlerStart=outerFinally,HandlerEnd=done });
        return writer;
    }
}
