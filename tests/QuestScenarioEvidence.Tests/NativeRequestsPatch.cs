using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    static string Body(MethodDefinition method) => string.Join("\n", method.Body.Instructions.Select(i => i.OpCode.Code + ":" + i.Operand));
    static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    static int Main(string[] args)
    {
        using var original = AssemblyDefinition.ReadAssembly(args[0]);
        using var boundary = AssemblyDefinition.ReadAssembly(args[1]);
        var module = original.MainModule;
        var interop = module.GetType("Apparance.Net.Interop");
        var entity = module.GetType("Apparance.Net.Entity");
        var retained = entity.Methods.Where(m => m.Name is "UpdateAll" or "Update" or "HandleTask" or "CheckTasks" or "Refresh").ToDictionary(m => m.FullName, m => Sha(Body(m)));
        int count = 0;
        foreach (var method in interop.Methods.Where(m => m.IsPInvokeImpl))
        {
            method.PInvokeInfo = null; method.Attributes &= ~MethodAttributes.PInvokeImpl;
            method.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
            method.Body = new MethodBody(method); var il = method.Body.GetILProcessor();
            string target = method.Name switch {
                "ApparanceCreateEntity" => "Create", "ApparanceEntityBuild" => "Build",
                "ApparancePopEntityTask" => "Pop", "ApparancePopEngineTask" => "NoTask",
                "ApparanceGetNextAssetRequest" => "NoAsset", "ApparanceUpdate" => "Tick", _ => "" };
            if (target != "")
            {
                var called = boundary.MainModule.GetType("NativeRequestsBoundary").Methods.Single(m => m.Name == target);
                foreach (var p in called.Parameters) il.Emit(OpCodes.Ldarg, method.Parameters[p.Index]);
                il.Emit(OpCodes.Call, module.ImportReference(called));
            }
            else if (method.ReturnType.MetadataType == MetadataType.Int32) il.Emit(OpCodes.Ldc_I4_1);
            else if (method.ReturnType.MetadataType != MetadataType.Void) throw new InvalidDataException(method.FullName);
            il.Emit(OpCodes.Ret); count++;
        }
        if (count != 12 || retained.Any(row => Sha(Body(entity.Methods.Single(m => m.FullName == row.Key))) != row.Value))
            throw new InvalidDataException("Original scheduling/decoding changed outside twelve controlled native imports.");
        original.Write(args[2]);
        File.WriteAllText(args[3], JsonSerializer.Serialize(new { nativeImports = count, originalBodyHashes = retained, originalManagedSchedulingAndDecodingPreserved = true }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
