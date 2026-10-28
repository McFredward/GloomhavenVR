using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class StandaloneOdinTests
{
    internal static void Run(string projectRoot, Action<bool, string> check)
    {
        string path = Path.Combine(projectRoot, "ressources/GH_Data/Managed/OdinSerializer.dll");
        if (!File.Exists(path)) return;
        using AssemblyDefinition original = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true });
        Guid mvid = original.MainModule.Mvid;
        var before = Discovery.AllTypes(original.MainModule).ToDictionary(type => type.FullName, ProtectedTypes.Fingerprint);
        TypeDefinition emit = original.MainModule.GetType("OdinSerializer.Utilities.EmitUtilities");
        string MethodBody(MethodDefinition method) => string.Join("\n", method.Body.Instructions.Select(instruction => instruction.OpCode + " " + instruction.Operand));
        var otherMethods = emit.Methods.Where(method => method.HasBody && method.Name is not ("get_CanEmit" or "EmitIsIllegalForMember")).ToDictionary(method => method.FullName, MethodBody);
        MethodDefinition memberGuard = emit.Methods.Single(method => method.Name == "EmitIsIllegalForMember");
        string originalGuard = MethodBody(memberGuard);
        var report = new StandaloneReport();
        string[] changed = StandaloneOdin.Apply(original, report).ToArray();
        check(changed.SequenceEqual(new[] { emit.FullName }), "Only the original Odin platform capability type is marked changed.");
        check(original.MainModule.Mvid == mvid, "Original serializer assembly identity is preserved.");
        check(emit.Methods.Single(method => method.Name == "get_CanEmit").Body.Instructions[0].OpCode == OpCodes.Ldc_I4_0,
            "The staged serializer selects its existing non-emitting AOT branch.");
        foreach (TypeDefinition type in Discovery.AllTypes(original.MainModule))
            if (type != emit) check(ProtectedTypes.Fingerprint(type) == before[type.FullName], "Unrelated serializer type changed: " + type.FullName);
        foreach (MethodDefinition method in emit.Methods.Where(method => method.HasBody && method.Name is not ("get_CanEmit" or "EmitIsIllegalForMember")))
            check(otherMethods[method.FullName] == MethodBody(method), "An emit utility body was replaced instead of selecting its existing fallback.");
        check(memberGuard.Body.Instructions[0].OpCode == OpCodes.Call
            && memberGuard.Body.Instructions[0].Operand is MethodReference selector && selector.Name == "get_CanEmit"
            && memberGuard.Body.Instructions[1].OpCode == OpCodes.Brtrue
            && memberGuard.Body.Instructions[1].Operand == memberGuard.Body.Instructions[4]
            && memberGuard.Body.Instructions[2].OpCode == OpCodes.Ldc_I4_1 && memberGuard.Body.Instructions[3].OpCode == OpCodes.Ret,
            "AOT callback/member helpers select their original reflection delegates before any DynamicMethod is constructed.");
        check(string.Join("\n", memberGuard.Body.Instructions.Skip(4).Select(instruction => instruction.OpCode + " " + instruction.Operand)) == originalGuard,
            "The original member emit guard remains intact behind its capability check.");
        check(report.Modifications.Count == 2, "Both exact platform selectors are reported.");
        try { StandaloneOdin.Apply(original, report); check(false, "An already adapted serializer was accepted."); }
        catch (InvalidDataException) { check(true, "Repeated or drifted selector is rejected."); }
        using AssemblyDefinition drifted = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true });
        drifted.MainModule.GetType("OdinSerializer.Utilities.EmitUtilities").Methods.Single(method => method.Name == "get_CanEmit")
            .Body.GetILProcessor().InsertBefore(drifted.MainModule.GetType("OdinSerializer.Utilities.EmitUtilities").Methods.Single(method => method.Name == "get_CanEmit").Body.Instructions[0], Instruction.Create(OpCodes.Nop));
        try { StandaloneOdin.Apply(drifted, new StandaloneReport()); check(false, "A changed desktop selector shape was silently accepted."); }
        catch (InvalidDataException) { check(true, "Changed native serializer selector requires review."); }
        using AssemblyDefinition fallback = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true });
        TypeDefinition weak = fallback.MainModule.GetType("OdinSerializer.WeakReflectionFormatter");
        weak.Name = "MissingWeakFallback";
        try { StandaloneOdin.Apply(fallback, new StandaloneReport()); check(false, "A missing original AOT fallback was accepted."); }
        catch (InvalidDataException) { check(true, "Missing existing reflection path fails closed."); }
        using AssemblyDefinition guardDrift = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true });
        guardDrift.MainModule.GetType("OdinSerializer.Utilities.EmitUtilities").Methods.Single(method => method.Name == "EmitIsIllegalForMember").Name = "ChangedGuard";
        try { StandaloneOdin.Apply(guardDrift, new StandaloneReport()); check(false, "A missing callback member guard was accepted."); }
        catch (InvalidDataException) { check(true, "Missing original member helper reflection selector fails closed."); }
    }
}
