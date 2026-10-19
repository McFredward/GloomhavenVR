using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace QuestWeaver;

/// <summary>Apply the current mod's two reviewed movie setter substitutions before
/// AOT conversion. Native decoders, branches, exception regions, Play calls and
/// completion callbacks retain their original owners. No runtime IL emitter is
/// shipped and unknown transpilers never silently receive this qualification.</summary>
internal static class NativeMovieBoundary
{
    private const string Owner = "GloomhavenVR.WorldUI.NativeVideoMovies";
    private static readonly string[] TranspilerHashes = { "51a2bcc2653842ebd28de2f1ef70cfacf7a7e37c67b0f30c7f5473ad8f8c0472", "26ee991766cc43cedfb4debcb1ae8dd9e107ea57687ed809318c6b64aa5bcc6c" };

    internal static void Apply(Discovery model, AuditReport report,
        Func<string, TypeDefinition?> find, Action<AssemblyDefinition> track)
    {
        var specifications = new[] {
            (Patch: "NativeVideoMoviePatch", Target: "VideoCamera", Method: "PlayFullscreenVideo", Replacement: "SetNativeUrl", NameArgument: true),
            (Patch: "NativeAmbientMoviePatch", Target: "BackgroundView", Method: "Update", Replacement: "SetAmbientUrl", NameArgument: false) };
        TypeDefinition? owner = model.Mod.MainModule.GetType(Owner);
        TypeDefinition?[] patches = specifications.Select(s => model.Mod.MainModule.GetType("GloomhavenVR.WorldUI." + s.Patch)).ToArray();
        if (owner == null && patches.All(p => p == null)) return;
        if (owner == null || patches.Any(p => p == null))
            throw new InvalidDataException("Native movie AOT substitution requires both complete current patch owners.");
        var prepared = new List<(TypeDefinition Patch, MethodDefinition Transpiler,
            MethodDefinition Target, Instruction Setter, MethodDefinition Replacement, bool NameArgument)>();
        for (int index = 0; index < specifications.Length; index++)
        {
            var spec = specifications[index]; TypeDefinition patch = patches[index]!;
            MethodDefinition transpiler = patch.Methods.Single(m => m.Name == "Transpiler");
            string hash = Fingerprint(transpiler);
            if (hash != TranspilerHashes[index])
                throw new InvalidDataException("Native movie transpiler is outside its reviewed complete IL contract: " + spec.Patch + "; sha256=" + hash);
            CustomAttribute[] attributes = patch.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToArray();
            if (attributes.Length != 1 || attributes[0].ConstructorArguments.Count != 2
                || attributes[0].ConstructorArguments[0].Value is not TypeReference original || original.FullName != spec.Target
                || attributes[0].ConstructorArguments[1].Value is not string name || name != spec.Method
                || patch.Methods.Count != 1 || !transpiler.IsStatic
                || transpiler.CustomAttributes.Count(a => a.AttributeType.Namespace == "HarmonyLib") != 1
                || !transpiler.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyTranspiler"))
                throw new InvalidDataException("Native movie patch changed its exact target or acquired additional behavior: " + spec.Patch);
            TypeDefinition targetType = find(spec.Target) ?? throw new InvalidDataException("Native movie original type is missing: " + spec.Target);
            MethodDefinition target = targetType.Methods.Single(m => m.Name == spec.Method);
            if (!target.HasBody || target.IsStatic || target.ReturnType.FullName != (spec.NameArgument ? "System.Boolean" : "System.Void")
                || Discovery.Protected(targetType) || target.HasGenericParameters
                || (spec.NameArgument && (target.Parameters.Count == 0 || target.Parameters[0].ParameterType.FullName != "System.String")))
                throw new InvalidDataException("Native movie original method no longer accepts its reviewed setter substitution: " + target.FullName + "; static=" + target.IsStatic + "; protected=" + Discovery.Protected(targetType));
            Instruction[] setters = target.Body.Instructions.Where(i => i.Operand is MethodReference call
                && call.DeclaringType.FullName == "UnityEngine.Video.VideoPlayer" && call.Name == "set_url").ToArray();
            if (setters.Length != 1 || setters[0].OpCode.Code is not (Code.Call or Code.Callvirt)
                || setters[0].Operand is not MethodReference setter || !setter.HasThis
                || setter.Parameters.Count != 1 || setter.Parameters[0].ParameterType.FullName != "System.String"
                || setter.ReturnType.FullName != "System.Void")
                throw new InvalidDataException("Native movie URL assignment changed; targeted AOT repair cannot guess its location: " + target.FullName);
            MethodDefinition replacement = owner.Methods.Single(m => m.Name == spec.Replacement);
            if (!replacement.IsStatic || replacement.ReturnType.FullName != "System.Void"
                || !replacement.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(spec.NameArgument
                    ? new[] { "UnityEngine.Video.VideoPlayer", "System.String", "System.String" }
                    : new[] { "UnityEngine.Video.VideoPlayer", "System.String" }))
                throw new InvalidDataException("Native movie replacement setter signature changed: " + spec.Replacement);
            // A removed build-time implementation must have no live callers.
            if (Discovery.AllTypes(model.Mod.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody && m != transpiler)
                .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference call && call.FullName == transpiler.FullName))
                throw new InvalidDataException("Native movie transpiler acquired an independent runtime caller.");
            prepared.Add((patch, transpiler, target, setters[0], replacement, spec.NameArgument));
        }
        // Qualify the entire pair before touching either original method.
        foreach (var entry in prepared)
        {
            entry.Replacement.IsPublic = true;
            MethodReference replacement = entry.Target.Module.ImportReference(entry.Replacement);
            if (entry.NameArgument)
            {
                // Keep the existing instruction as the argument load: branch and
                // exception-start markers still enter before the replacement, and
                // an exclusive exception-end boundary stays outside both.
                entry.Setter.OpCode = OpCodes.Ldarg_1; entry.Setter.Operand = null;
                entry.Target.Body.GetILProcessor().InsertAfter(entry.Setter, Instruction.Create(OpCodes.Call, replacement));
                entry.Target.Body.MaxStackSize = Math.Max(3, entry.Target.Body.MaxStackSize + 1);
                // Insertion can move a branch beyond its signed-byte range.
                // Recompute encodings without changing its target or condition.
                entry.Target.Body.SimplifyMacros();
                entry.Target.Body.OptimizeMacros();
            }
            else { entry.Setter.OpCode = OpCodes.Call; entry.Setter.Operand = replacement; }
            string removedPatch = Discovery.MethodKey(entry.Transpiler);
            entry.Patch.Methods.Remove(entry.Transpiler);
            foreach (CustomAttribute attribute in entry.Patch.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToArray())
                entry.Patch.CustomAttributes.Remove(attribute);
            // WorldUIModule still installs these exact desktop patch classes. A
            // static substitution has no runtime hook body, but is not an unknown
            // empty class: retain its closed registration proof for the AOT
            // registry. Only the completely qualified pair may receive it.
            model.StaticPatches.Add(new StaticPatchDefinition(entry.Patch, entry.Target, removedPatch));
            track(entry.Target.Module.Assembly);
        }
        report.StaticSubstitutions.Add("Native movies: two verified URL setter sites retain native decoder, Play order, callbacks and exception boundaries; reviewed desktop transpilers are removed before Quest AOT conversion and their exact static registration proofs remain available to the Quest registry.");
    }

    internal static string Fingerprint(MethodDefinition method)
    {
        var instructions = method.Body.Instructions;
        string Operand(object? value) => value switch {
            null => "", Instruction target => "@" + instructions.IndexOf(target),
            Instruction[] targets => "@" + string.Join(",", targets.Select(t => instructions.IndexOf(t))),
            MemberReference member => member.FullName, VariableDefinition variable => "local:" + variable.Index,
            ParameterDefinition parameter => "arg:" + parameter.Index,
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" };
        string text = method.FullName + "\n" + string.Join("\n", method.Body.Variables.Select(v => v.VariableType.FullName))
            + "\n" + string.Join("\n", instructions.Select(i => i.OpCode.Name + ":" + Operand(i.Operand)))
            + "\n" + string.Join("\n", method.Body.ExceptionHandlers.Select(h => h.HandlerType + ":"
                + Operand(h.TryStart) + ":" + Operand(h.TryEnd) + ":" + Operand(h.HandlerStart) + ":"
                + Operand(h.HandlerEnd) + ":" + Operand(h.FilterStart) + ":" + h.CatchType?.FullName));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
