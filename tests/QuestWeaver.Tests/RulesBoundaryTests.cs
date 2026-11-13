using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class RulesBoundaryTests
{
    internal static void Run(string managed, string adapted, string profile, string temp, Action<bool, string> check)
    {
        string rulesPath = Path.Combine(managed, "ScenarioRuleLibrary.dll");
        byte[] before = SHA256.HashData(File.ReadAllBytes(rulesPath));
        using var original = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll"));
        using var emitted = AssemblyDefinition.ReadAssembly(Path.Combine(adapted, "GH.Runtime.dll"));
        using var compatibility = AssemblyDefinition.ReadAssembly(Path.Combine(adapted, "QuestGame.Compatibility.dll"));
        MethodDefinition Boundary(AssemblyDefinition assembly) => Discovery.AllTypes(assembly.MainModule)
            .Single(t => t.DeclaringType?.FullName == "SceneController" && t.Name.StartsWith("<InitialiseGloomhavenCoroutine>", StringComparison.Ordinal))
            .Methods.Single(m => m.Name == "MoveNext");
        MethodDefinition boundary = Boundary(emitted);
        Instruction[] hooks = boundary.Body.Instructions.Where(i => i.Operand is MethodReference call
            && call.DeclaringType.FullName == "QuestGame.Compatibility.Paths" && call.Name == "InitializeRulebasePath").ToArray();
        check(hooks.Length == 1 && hooks[0].OpCode == OpCodes.Call && hooks[0].Next?.Operand is MethodReference originalInit
            && originalInit.DeclaringType.FullName == "ScenarioRuleLibrary.ScenarioRuleClient" && originalInit.Name == "Initialise",
            "File-backed lazy rule root was not initialized immediately before the original rule constructors.");
        boundary.Body.GetILProcessor().Remove(hooks.Single());
        check(ProtectedTypes.Fingerprint(boundary.DeclaringType) == ProtectedTypes.Fingerprint(Boundary(original).DeclaringType),
            "Original rule initialization coroutine changed beyond the scoped IO-setting call.");
        MethodDefinition pathInit = compatibility.MainModule.GetType("QuestGame.Compatibility.Paths").Methods.Single(m => m.Name == "InitializeRulebasePath");
        Instruction[] body = pathInit.Body.Instructions.ToArray();
        check(body.Length == 5 && body[0].OpCode == OpCodes.Call && body[0].Operand is MethodReference ownedPath && ownedPath.Name == "get_streamingAssetsPath"
            && body[1].OpCode == OpCodes.Ldstr && (string)body[1].Operand == "Rulebase"
            && body[2].OpCode == OpCodes.Call && body[2].Operand is MethodReference combine && combine.DeclaringType.FullName == "System.IO.Path" && combine.Name == "Combine"
            && body[3].OpCode == OpCodes.Stsfld && body[3].Operand is FieldReference setting && setting.DeclaringType.FullName == "ScenarioRuleLibrary.LazyLoadingConstants"
            && setting.Name == "RulesetPath" && body[4].OpCode == OpCodes.Ret,
            "Lazy rule bridge changed parser behavior or did not use the original public IO setting.");
        check(!File.Exists(Path.Combine(adapted, "ScenarioRuleLibrary.dll")) && before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(rulesPath))),
            "Protected rule DLL was rewritten or emitted by the standalone adapter.");

        // Fail closed if a future game removes/privatizes the public setting, or
        // moves its native constructor boundary. Do not silently leave jar IO.
        string overrides = Path.Combine(temp, "rules-boundary-overrides"); Directory.CreateDirectory(overrides);
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(managed);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        using (var rules = AssemblyDefinition.ReadAssembly(rulesPath, new ReaderParameters { AssemblyResolver = resolver }))
        {
            rules.MainModule.GetType("ScenarioRuleLibrary.LazyLoadingConstants").Fields.Single(f => f.Name == "RulesetPath").IsPublic = false;
            rules.Write(Path.Combine(overrides, "ScenarioRuleLibrary.dll"));
        }
        Reject("private-rule-path");
        File.Delete(Path.Combine(overrides, "ScenarioRuleLibrary.dll"));
        using (var changed = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll"), new ReaderParameters { AssemblyResolver = resolver }))
        {
            Instruction initCall = Boundary(changed).Body.Instructions.Single(i => i.Operand is MethodReference call
                && call.DeclaringType.FullName == "ScenarioRuleLibrary.ScenarioRuleClient" && call.Name == "Initialise");
            var originalCall = (MethodReference)initCall.Operand;
            var futureCall = new MethodReference("UnknownFutureRuleInitialization", originalCall.ReturnType, originalCall.DeclaringType);
            foreach (ParameterDefinition argument in originalCall.Parameters) futureCall.Parameters.Add(new ParameterDefinition(argument.ParameterType));
            initCall.Operand = futureCall; changed.Write(Path.Combine(overrides, "GH.Runtime.dll"));
        }
        Reject("missing-rule-boundary");
        check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(rulesPath))), "Rule-boundary negative controls changed read-only owned input.");

        void Reject(string label)
        {
            string output = Path.Combine(temp, label);
            try { Standalone.Write(managed, overrides, profile, output); check(false, "Changed original rule boundary was accepted: " + label); }
            catch (InvalidDataException) { check(!Directory.Exists(output), "Rejected rule boundary leaked an adapted output: " + label); }
        }
    }
}
