using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

/// <summary>The excluded mode is rejected before native privilege/save work. No game protocol changes.</summary>
internal static class StandaloneGuildmaster
{
    internal static IEnumerable<string> Apply(AssemblyDefinition game, AssemblyNameReference runtimeAssembly, StandaloneReport report)
    {
        ModuleDefinition module = game.MainModule;
        TypeDefinition client = RequireType(module, "GHClientCallbacks"), save = RequireType(module, "SaveData");
        MethodDefinition connected = RequireMethod(client, "Connected", "System.Void", "Photon.Bolt.BoltConnection");
        MethodDefinition load = RequireMethod(save, "LoadGuildmasterMode", "System.Void", "PartyAdventureData", "System.Boolean",
            "System.Boolean", "System.Action", "System.Action", "System.Boolean");
        if (connected.Body.ExceptionHandlers.Count != 0 || load.Body.ExceptionHandlers.Count != 0
            || connected.Body.Instructions.Concat(load.Body.Instructions).Any(i => i.Operand is MethodReference m
                && m.DeclaringType.FullName == "GloomhavenVR.Quest.QuestGameScope"))
            throw new InvalidDataException("Original Guildmaster entry already adapted or changed its exception boundary.");

        int guildmaster = EnumValue(module, "EGameMode", "Guildmaster"), invalidSession = EnumValue(module, "ConnectionErrorCode", "InvalidSessionData");
        Instruction[] instructions = connected.Body.Instructions.ToArray();
        Instruction modeRead = Unique(instructions, i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference m
            && m.DeclaringType.FullName == "GameToken" && m.Name == "get_GameModeID" && m.Parameters.Count == 0
            && m.ReturnType.FullName == "System.Int32", "original client GameModeID read");
        FieldDefinition token = client.Fields.SingleOrDefault(f => f.Name == "gameData" && f.FieldType.FullName == "GameToken" && !f.IsStatic)
            ?? throw new InvalidDataException("Original client GameToken field changed.");
        Instruction start = modeRead.Previous?.Previous?.Previous ?? throw new InvalidDataException("Original client mode entry is absent.");
        // The original valid-token branch loads its closure before reading the
        // mode. Insert ahead of that load, where the evaluation stack is empty.
        if (start.OpCode != OpCodes.Ldloc_0 || start.Next.OpCode != OpCodes.Ldarg_0
            || start.Next.Next.OpCode != OpCodes.Ldfld || ((FieldReference)start.Next.Next.Operand).FullName != token.FullName
            || modeRead.Next.OpCode != OpCodes.Stfld || modeRead.Next.Operand is not FieldReference capturedMode
            || capturedMode.FieldType.FullName != "EGameMode" || capturedMode.DeclaringType.DeclaringType != client)
            throw new InvalidDataException("Original client mode capture shape changed.");
        Instruction admission = Unique(instructions, i => i.Operand == start, "original valid-token branch");
        if (admission.OpCode != OpCodes.Brtrue_S || admission.Previous.OpCode != OpCodes.Ldfld
            || admission.Previous.Operand is not FieldReference field || field.FullName != token.FullName
            || admission.Previous.Previous.OpCode != OpCodes.Ldarg_0
            || start.Previous.OpCode != OpCodes.Ret)
            throw new InvalidDataException("Original client missing-token/admission boundary changed.");
        Instruction[] rejection = instructions.Skip(Array.IndexOf(instructions, admission) + 1)
            .Take(Array.IndexOf(instructions, start) - Array.IndexOf(instructions, admission) - 1).ToArray();
        MethodReference manager = Call(rejection, "FFSNetwork", "get_Manager"), failed = Call(rejection, "FFSNet.NetworkManager", "get_OnConnectionFailed"),
            invoke = Call(rejection, "UnityEngine.Events.UnityAction`1<ConnectionErrorCode>", "Invoke"), shutdown = Call(rejection, "FFSNetwork", "Shutdown");
        Instruction error = Unique(rejection, i => i.Next?.Operand == invoke, "original InvalidSessionData callback argument");
        if (!IsInt(error, invalidSession) || manager.HasThis || manager.Parameters.Count != 0
            || !failed.HasThis || failed.Parameters.Count != 0 || invoke.Parameters.Count != 1 || shutdown.HasThis
            || shutdown.Parameters.Count != 2 || shutdown.ReturnType.FullName != "System.Void"
            || shutdown.Parameters[0].ParameterType.FullName != "Photon.Bolt.IProtocolToken"
            || shutdown.Parameters[1].ParameterType.FullName != "UnityEngine.Events.UnityAction"
            || start.Previous.Previous.Operand != shutdown)
            throw new InvalidDataException("Original invalid-session failure/shutdown ABI changed.");
        MethodReference dlc = Call(load.Body.Instructions, "PlatformLayer", "get_DLC"),
            owns = Call(load.Body.Instructions, "PlatformDLC", "CanPlayPartyData"),
            resume = Call(load.Body.Instructions, "SceneController", "GuildmasterResume");
        if (dlc.HasThis || dlc.Parameters.Count != 0 || dlc.ReturnType.FullName != "PlatformDLC"
            || !owns.HasThis || owns.ReturnType.FullName != "System.Boolean" || owns.Parameters.Count != 1
            || owns.Parameters[0].ParameterType.FullName != "PartyAdventureData" || !resume.HasThis)
            throw new InvalidDataException("Original Guildmaster load entry changed.");

        if (!module.AssemblyReferences.Contains(runtimeAssembly)) module.AssemblyReferences.Add(runtimeAssembly);
        var scope = new TypeReference("GloomhavenVR.Quest", "QuestGameScope", module, runtimeAssembly);
        var notify = new MethodReference("NotifyGuildmasterUnavailable", module.TypeSystem.Void, scope);
        notify.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
        notify.Parameters.Add(new ParameterDefinition(load.Parameters[4].ParameterType));

        ILProcessor il = connected.Body.GetILProcessor();
        Instruction invokeFailure = il.Create(OpCodes.Ldc_I4, invalidSession), stop = il.Create(OpCodes.Ldnull);
        Instruction[] guard = {
            il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Ldfld, token), il.Create(OpCodes.Callvirt, (MethodReference)modeRead.Operand),
            il.Create(OpCodes.Ldc_I4, guildmaster), il.Create(OpCodes.Bne_Un, start),
            il.Create(OpCodes.Call, manager), il.Create(OpCodes.Callvirt, failed), il.Create(OpCodes.Dup),
            il.Create(OpCodes.Brtrue, invokeFailure), il.Create(OpCodes.Pop), il.Create(OpCodes.Br, stop),
            invokeFailure, il.Create(OpCodes.Callvirt, invoke), stop, il.Create(OpCodes.Ldnull), il.Create(OpCodes.Call, shutdown),
            il.Create(OpCodes.Ldc_I4_0), il.Create(OpCodes.Ldnull), il.Create(OpCodes.Call, notify), il.Create(OpCodes.Ret)
        };
        foreach (Instruction instruction in guard) il.InsertBefore(start, instruction);
        admission.Operand = guard[0];
        connected.Body.MaxStackSize = Math.Max(connected.Body.MaxStackSize, 4);

        ILProcessor local = load.Body.GetILProcessor();
        Instruction original = load.Body.Instructions[0];
        foreach (Instruction instruction in new[] { local.Create(OpCodes.Ldarg, load.Parameters[2]),
            local.Create(OpCodes.Ldarg, load.Parameters[4]), local.Create(OpCodes.Call, notify), local.Create(OpCodes.Ret) })
            local.InsertBefore(original, instruction);
        load.Body.MaxStackSize = Math.Max(load.Body.MaxStackSize, 2);
        report.Modifications.Add("Quest-only Guildmaster host admission rejected before privileges/save transfer: " + connected.FullName);
        report.Modifications.Add("Quest-only Guildmaster load denied before save mutation; imported bytes retained: " + load.FullName);
        string validationType = SkipExcludedSaveValidation(module, guildmaster, report);
        return new[] { client.FullName, save.FullName, validationType };
    }

    static string SkipExcludedSaveValidation(ModuleDefinition module, int guildmaster, StandaloneReport report)
    {
        TypeDefinition global = RequireType(module, "GlobalData");
        MethodDefinition validate = RequireMethod(global, "ValidateSaves", "System.Collections.IEnumerator", "EGameMode",
            "System.Collections.Generic.List`1<System.String>");
        Instruction[] native = validate.Body.Instructions.ToArray();
        MethodReference construct = (MethodReference)Unique(native, i => i.OpCode == OpCodes.Newobj
            && i.Operand is MethodReference method && method.Name == ".ctor"
            && method.DeclaringType.DeclaringType?.FullName == global.FullName,
            "original save-validation coroutine allocation").Operand;
        FieldReference[] captures = native.Where(i => i.OpCode == OpCodes.Stfld).Select(i => (FieldReference)i.Operand).ToArray();
        if (validate.Body.ExceptionHandlers.Count != 0 || native.Any(i => i.OpCode.FlowControl == FlowControl.Cond_Branch)
            || construct.Parameters.Count != 1 || construct.Parameters[0].ParameterType.FullName != "System.Int32"
            || captures.Length != 3 || captures.Any(f => f.DeclaringType.FullName != construct.DeclaringType.FullName)
            || captures.Count(f => f.FieldType.FullName == global.FullName) != 1
            || captures.Count(f => f.FieldType.FullName == validate.Parameters[0].ParameterType.FullName) != 1
            || captures.Count(f => f.FieldType.FullName == validate.Parameters[1].ParameterType.FullName) != 1
            || native.Last().OpCode != OpCodes.Ret)
            throw new InvalidDataException("Original save-validation coroutine captures or entry changed.");
        MethodDefinition enumerator = module.TypeSystem.Object.Resolve().Module.GetType("System.Array").Methods.SingleOrDefault(m =>
            m.Name == "GetEnumerator" && !m.IsStatic && m.Parameters.Count == 0
            && m.ReturnType.FullName == validate.ReturnType.FullName)
            ?? throw new InvalidDataException("Original core-library empty enumerator ABI is absent.");
        ILProcessor il = validate.Body.GetILProcessor();
        Instruction original = native[0];
        // B624 spent about twenty seconds parsing the excluded Guildmaster and
        // its Shared rules solely because startup validates that mode BEFORE
        // checking for saves. Keep every save byte and Campaign validation;
        // skip only this unavailable mode before its coroutine can load rules.
        // This seam exists solely in the Quest-woven GH.Runtime assembly.
        foreach (Instruction instruction in new[] {
            il.Create(OpCodes.Ldarg_1), il.Create(OpCodes.Ldc_I4, guildmaster), il.Create(OpCodes.Bne_Un, original),
            il.Create(OpCodes.Ldc_I4_0), il.Create(OpCodes.Newarr, module.TypeSystem.Object),
            il.Create(OpCodes.Callvirt, module.ImportReference(enumerator)), il.Create(OpCodes.Ret) })
            il.InsertBefore(original, instruction);
        validate.Body.MaxStackSize = Math.Max(validate.Body.MaxStackSize, 2);
        report.Modifications.Add("Quest-only excluded Guildmaster startup/save validation yields no work; Campaign coroutine and all save bytes retained: " + validate.FullName);
        return global.FullName;
    }

    static TypeDefinition RequireType(ModuleDefinition module, string name) => module.GetType(name)
        ?? throw new InvalidDataException("Original excluded-mode type is absent: " + name);

    static MethodDefinition RequireMethod(TypeDefinition type, string name, string result, params string[] args)
    {
        MethodDefinition[] candidates = type.Methods.Where(m => m.Name == name && m.HasBody && !m.IsStatic && m.ReturnType.FullName == result
            && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args)).ToArray();
        return candidates.Length == 1 ? candidates[0] : throw new InvalidDataException("Original excluded-mode entry changed: " + type.FullName + "." + name);
    }

    static int EnumValue(ModuleDefinition module, string type, string name)
    {
        FieldDefinition[] fields = RequireType(module, type).Fields.Where(f => f.Name == name && f.IsLiteral && f.HasConstant && f.FieldType.FullName == type).ToArray();
        return fields.Length == 1 ? Convert.ToInt32(fields[0].Constant) : throw new InvalidDataException("Original mode/error enum member changed: " + type + "." + name);
    }

    static Instruction Unique(IEnumerable<Instruction> instructions, Func<Instruction, bool> predicate, string boundary)
    {
        Instruction[] matches = instructions.Where(predicate).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Expected one " + boundary + ".");
    }

    static MethodReference Call(IEnumerable<Instruction> instructions, string type, string name) =>
        (MethodReference)Unique(instructions, i => i.OpCode is var code && (code == OpCodes.Call || code == OpCodes.Callvirt)
            && i.Operand is MethodReference m && m.DeclaringType.FullName == type && m.Name == name, type + "." + name).Operand;

    static bool IsInt(Instruction instruction, int value) => instruction.OpCode == OpCodes.Ldc_I4 && Equals(instruction.Operand, value)
        || instruction.OpCode == OpCodes.Ldc_I4_S && Convert.ToInt32(instruction.Operand) == value;
}
