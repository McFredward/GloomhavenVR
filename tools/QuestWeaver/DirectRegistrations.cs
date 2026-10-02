using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

internal sealed record DirectHook(MethodDefinition Patch, string Kind, MethodDefinition[]? Targets);

/// <summary>
/// Reads the actual Harmony.Patch argument positions. Hook names are deliberately irrelevant:
/// a prefix named BeforeDisplay and one named ArbitraryName must have identical semantics.
/// Unknown factory/control-flow values stop conversion rather than being guessed.
/// </summary>
internal static class DirectRegistrations
{
    private sealed record Methods(MethodDefinition[] Definitions);
    private sealed record HarmonyValue(MethodDefinition[] Definitions);
    private static readonly object Unknown = new();

    public static IReadOnlyList<DirectHook> Read(MethodDefinition method, Func<string, TypeDefinition?> findType)
    {
        var hooks = new List<DirectHook>();
        var stack = new List<object?>();
        var locals = new Dictionary<int, object?>();
        object? Pop() { if (stack.Count == 0) return Unknown; object? value = stack[^1]; stack.RemoveAt(stack.Count - 1); return value; }
        object?[] Arguments(MethodReference call)
        { var args = new object?[call.Parameters.Count]; for (int j = args.Length - 1; j >= 0; j--) args[j] = Pop(); return args; }
        foreach (Instruction instruction in method.Body.Instructions)
        {
            Code code = instruction.OpCode.Code;
            switch (code)
            {
                case Code.Ldnull: stack.Add(null); break;
                case Code.Ldstr: stack.Add((string)instruction.Operand); break;
                case Code.Ldtoken: stack.Add(instruction.Operand); break;
                case Code.Dup: stack.Add(stack.Count == 0 ? Unknown : stack[^1]); break;
                case Code.Pop: Pop(); break;
                case Code.Ldloc_0: case Code.Ldloc_1: case Code.Ldloc_2: case Code.Ldloc_3:
                { int n = (int)code - (int)Code.Ldloc_0; stack.Add(locals.GetValueOrDefault(n, Unknown)); break; }
                case Code.Ldloc: case Code.Ldloc_S:
                    stack.Add(locals.GetValueOrDefault(((VariableDefinition)instruction.Operand).Index, Unknown)); break;
                case Code.Stloc_0: case Code.Stloc_1: case Code.Stloc_2: case Code.Stloc_3:
                    locals[(int)code - (int)Code.Stloc_0] = Pop(); break;
                case Code.Stloc: case Code.Stloc_S: locals[((VariableDefinition)instruction.Operand).Index] = Pop(); break;
                case Code.Call: case Code.Callvirt: case Code.Newobj:
                {
                    var call = (MethodReference)instruction.Operand;
                    object?[] args = Arguments(call);
                    object? receiver = call.HasThis && code != Code.Newobj ? Pop() : null;
                    object? result = Unknown;
                    if (call.DeclaringType.FullName == "HarmonyLib.Harmony" && call.Name == "Patch")
                    {
                        if (args.Length != 6) throw new InvalidOperationException("Unsupported Harmony.Patch overload.");
                        if (args[3] != null || args[5] != null) throw new InvalidOperationException("Runtime transpiler/IL manipulator is outside the AOT contract.");
                        foreach ((int index, string kind) in new[] { (1, "prefix"), (2, "postfix"), (4, "finalizer") })
                        {
                            if (args[index] == null) continue;
                            if (args[index] is not HarmonyValue value) throw new InvalidOperationException("Cannot resolve " + kind + " HarmonyMethod at IL_" + instruction.Offset.ToString("x4"));
                            foreach (MethodDefinition patch in value.Definitions) hooks.Add(new DirectHook(patch, kind, (args[0] as Methods)?.Definitions));
                        }
                    }
                    else if (call.DeclaringType.FullName == "HarmonyLib.HarmonyMethod" && code == Code.Newobj)
                    {
                        if (args.Length == 1 && args[0] is Methods methods) result = new HarmonyValue(methods.Definitions);
                        else if (args.Length >= 2 && args[0] is TypeReference type && args[1] is string name)
                            result = new HarmonyValue(type.Resolve().Methods.Where(m => m.Name == name).ToArray());
                    }
                    else if (call.Name == "GetTypeFromHandle" && args.Length == 1) result = args[0];
                    else if (call.Name == "GetMethodFromHandle" && args.Length == 1 && args[0] is MethodReference mr) result = new Methods(new[] { mr.Resolve() });
                    else if (call.Name == "GetMethod" && receiver is TypeReference type && args.FirstOrDefault() is string name)
                        result = new Methods(type.Resolve().Methods.Where(m => m.Name == name).ToArray());
                    else if (call.DeclaringType.FullName == "HarmonyLib.AccessTools" && call.Name == "TypeByName" && args.FirstOrDefault() is string typeName)
                        result = findType(typeName) ?? (object)Unknown;
                    else if (call.DeclaringType.FullName == "HarmonyLib.AccessTools" && call.Name is "Method" or "DeclaredMethod")
                    {
                        TypeDefinition? accessType = args.FirstOrDefault() is TypeReference tr ? tr.Resolve() : null;
                        string? accessName = args.ElementAtOrDefault(1) as string;
                        if (args.FirstOrDefault() is string qualified && qualified.Contains(':', StringComparison.Ordinal))
                        { string[] split = qualified.Split(':'); accessType = findType(split[0]); accessName = split[1]; }
                        if (accessType != null && accessName != null) result = new Methods(accessType.Methods.Where(m => m.Name == accessName).ToArray());
                    }
                    if (code == Code.Newobj || call.ReturnType.MetadataType != MetadataType.Void) stack.Add(result);
                    break;
                }
                case Code.Leave: case Code.Leave_S: case Code.Ret: case Code.Throw: case Code.Rethrow: stack.Clear(); break;
                default:
                    int pop = instruction.OpCode.StackBehaviourPop switch
                    {
                        StackBehaviour.Pop0 => 0,
                        StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
                        StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                            or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
                        StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
                            or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref => 3,
                        _ => 0
                    };
                    for (int n = 0; n < pop; n++) Pop();
                    int push = instruction.OpCode.StackBehaviourPush switch
                    { StackBehaviour.Push0 => 0, StackBehaviour.Push1_push1 => 2, _ => 1 };
                    for (int n = 0; n < push; n++) stack.Add(Unknown);
                    break;
            }
        }
        return hooks;
    }
}
