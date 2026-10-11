using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

internal static class ProtectedTypes
{
    internal static Dictionary<string, string> Snapshot(AssemblyDefinition assembly) => Discovery.AllTypes(assembly.MainModule)
        .Where(Discovery.Protected).ToDictionary(t => t.FullName, Fingerprint, StringComparer.Ordinal);

    private static string Fingerprint(TypeDefinition type)
    {
        var text = new StringBuilder(type.FullName).Append('|').Append(type.Attributes).Append('|').Append(type.BaseType?.FullName);
        foreach (FieldDefinition field in type.Fields) text.Append("\nf:").Append(field.FullName).Append('|').Append(field.Attributes);
        foreach (MethodDefinition method in type.Methods)
        {
            text.Append("\nm:").Append(method.FullName).Append('|').Append(method.Attributes).Append('|').Append(method.ImplAttributes);
            if (!method.HasBody) continue;
            text.Append("\nlocals:").Append(method.Body.InitLocals).Append('|').Append(string.Join(",", method.Body.Variables.Select(v => v.VariableType.FullName)));
            Instruction[] instructions = method.Body.Instructions.ToArray();
            foreach (Instruction instruction in instructions)
            {
                string operand = instruction.Operand switch
                {
                    Instruction branch => "instruction:" + Array.IndexOf(instructions, branch),
                    Instruction[] branches => "switch:" + string.Join(",", branches.Select(b => Array.IndexOf(instructions, b))),
                    MemberReference member => member.FullName,
                    ParameterDefinition parameter => "parameter:" + parameter.Index,
                    VariableDefinition variable => "variable:" + variable.Index,
                    _ => instruction.Operand?.ToString() ?? ""
                };
                text.Append('\n').Append(instruction.OpCode.Code).Append('|').Append(operand);
            }
            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
                text.Append("\neh:").Append(handler.HandlerType).Append('|').Append(handler.CatchType?.FullName)
                    .Append('|').Append(Array.IndexOf(instructions, handler.TryStart)).Append('|').Append(Array.IndexOf(instructions, handler.TryEnd))
                    .Append('|').Append(Array.IndexOf(instructions, handler.HandlerStart)).Append('|').Append(Array.IndexOf(instructions, handler.HandlerEnd));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    internal static int Verify(Dictionary<string, string> original, AssemblyDefinition output)
    {
        Dictionary<string, string> actual = Snapshot(output);
        if (original.Count != actual.Count || original.Any(pair => !actual.TryGetValue(pair.Key, out string? value) || value != pair.Value))
            throw new InvalidDataException("A protected game type changed during integration: " + output.Name.Name);
        return original.Count;
    }
}
