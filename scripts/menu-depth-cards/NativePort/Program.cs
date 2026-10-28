using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Editor uGUI lacks the game's LayoutRebuilder.Enable property. Port only its
// bool setter calls in the original pool, never any pose/parent/spawn operation.
// Editor layout stays enabled: this does not prove the original layout-gate timing.
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
using var source = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver });
string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> roots)
{
    foreach (var type in roots)
    {
        yield return type;
        foreach (var nested in Types(type.NestedTypes)) yield return nested;
    }
}
string Semantic(MethodDefinition method)
{
    if (!method.HasBody) return "<no-body>";
    var code = method.Body.Instructions;
    string Operand(object? value) => value switch
    {
        null => "",
        Instruction target => "@" + code.IndexOf(target),
        Instruction[] targets => string.Join(",", targets.Select(t => "@" + code.IndexOf(t))),
        MemberReference member => member.FullName,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };
    return string.Join("\n", code.Select(i => i.OpCode.Name + " " + Operand(i.Operand)))
        + "\nEX:" + string.Join(";", method.Body.ExceptionHandlers.Select(h => h.HandlerType + ":"
            + (h.TryStart == null ? -1 : code.IndexOf(h.TryStart)) + ":" + (h.TryEnd == null ? -1 : code.IndexOf(h.TryEnd))
            + ":" + (h.HandlerStart == null ? -1 : code.IndexOf(h.HandlerStart)) + ":" + (h.HandlerEnd == null ? -1 : code.IndexOf(h.HandlerEnd))));
}
var methods = Types(source.MainModule.Types).SelectMany(t => t.Methods)
    .Select((m, index) => new { Key = index + ":" + m.FullName, Method = m })
    .ToDictionary(p => p.Key, p => p.Method);
var before = methods.ToDictionary(p => p.Key, p => Semantic(p.Value));
var changes = new List<object>(); int calls = 0;
foreach (var method in methods.Values.Where(m => m.DeclaringType.FullName == "ObjectPool"
    && (m.Name == "SpawnCard" || m.Name == "RecycleCard")))
{
    foreach (var instruction in method.Body.Instructions)
    {
        if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference call
            || call.DeclaringType.FullName != "UnityEngine.UI.LayoutRebuilder" || call.Name != "set_Enable") continue;
        if (call.Parameters.Count != 1 || call.Parameters[0].ParameterType.FullName != "System.Boolean"
            || call.ReturnType.FullName != "System.Void") throw new InvalidOperationException("Unexpected native layout API.");
        changes.Add(new { method = method.FullName, index = method.Body.Instructions.IndexOf(instruction), before = call.FullName, after = "pop" });
        instruction.OpCode = OpCodes.Pop; instruction.Operand = null; calls++;
    }
}
if (calls != 4) throw new InvalidOperationException("Expected exactly four original pool layout setters, got " + calls);
var expected = methods.ToDictionary(p => p.Key, p => Semantic(p.Value));
Directory.CreateDirectory(Path.GetDirectoryName(args[1])!); source.Write(args[1]);
using var verified = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { AssemblyResolver = resolver });
var actual = Types(verified.MainModule.Types).SelectMany(t => t.Methods)
    .Select((m, index) => new { Key = index + ":" + m.FullName, Method = m })
    .ToDictionary(p => p.Key, p => Semantic(p.Method));
if (expected.Count != actual.Count || expected.Any(p => !actual.TryGetValue(p.Key, out string? value) || value != p.Value))
    throw new InvalidOperationException("Private native copy differs beyond the four declared setter ports.");
var changed = before.Keys.Where(k => before[k] != actual[k]).OrderBy(k => k).ToArray();
if (changed.Length != 2 || changed.Any(k => !k.Contains("ObjectPool::SpawnCard(") && !k.Contains("ObjectPool::RecycleCard(")))
    throw new InvalidOperationException("Unexpected modified native method.");
File.WriteAllText(args[2], JsonSerializer.Serialize(new {
    source = args[0], source_sha256 = Digest(args[0]), output = args[1], output_sha256 = Digest(args[1]),
    methods_verified = actual.Count, modified_methods = changed, setter_ports = changes,
    boundary = "Exactly four game-only LayoutRebuilder.Enable(bool) calls consume their bool via Pop. Editor layout stays enabled; native pool pose/parent/rotation/activation and all remaining methods are unchanged. No original game asset is written."
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Verified native pool API port: 4 setters, 2 methods; all " + actual.Count + " method instruction streams checked.");
