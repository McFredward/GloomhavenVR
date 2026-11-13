using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace QuestWeaver;

/// <summary>Compile the two current desktop coordinate transpilers into their
/// exact managed UI/input owners. Original input processing, native raycasters,
/// permission branches, callbacks and exception regions remain authoritative.
/// Unknown source or native IL closes the build gate before either mutation.</summary>
internal static class NativeRaycastBoundary
{
    const string Prefix = "GloomhavenVR.Hands.Interact.";

    internal static void Apply(Discovery model, AuditReport report, Func<string, TypeDefinition?> find,
        Action<AssemblyDefinition> track)
    {
        var specs = new[] {
            (Patch: "GraphicRaycaster_SyntheticCoordinates", Native: "UnityEngine.UI.GraphicRaycaster", Assembly: "UnityEngine.UI",
                Method: "Raycast", Parameters: new[] { "UnityEngine.EventSystems.PointerEventData", "System.Collections.Generic.List`1<UnityEngine.EventSystems.RaycastResult>" },
                Transpiler: "d4171f8559cfde91361d5396c4fa0c051e19f6db12121a3cfeb4c2e8ed6f17ae",
                Body: "e5f2f4725a19d288aa9ca8240f1be493a08f4bc83fc72c2e7779fb8c96be081f"),
            (Patch: "PointerInputModuleExtended_CapturedCoordinates", Native: "InControl.PointerInputModuleExtended", Assembly: "InControl",
                Method: "GetMousePointerEventData", Parameters: new[] { "System.Int32" },
                Transpiler: "b19eb6e723ccd54f24b7ec143303b048dc2d51b697f49730127ccd3579f6aba0",
                Body: "9e2c6fafdb2334c3316aa41275815688889dd7f6f3af1f41318517588638fcb8") };
        if (specs.All(s => model.Mod.MainModule.GetType(Prefix + s.Patch) == null)) return;
        var prepared = new List<(TypeDefinition Patch, MethodDefinition Transpiler, MethodDefinition Target)>();
        foreach (var spec in specs)
        {
            TypeDefinition patch = model.Mod.MainModule.GetType(Prefix + spec.Patch)
                ?? throw new InvalidDataException("Both current coordinate transpilers are required.");
            MethodDefinition transpiler = patch.Methods.Single(m => m.Name == "Transpiler");
            CustomAttribute[] attributes = patch.CustomAttributes.Where(a => a.AttributeType.Namespace == "HarmonyLib").ToArray();
            if (!patch.IsAbstract || !patch.IsSealed || patch.Fields.Count != 0 || patch.NestedTypes.Count != 0
                || patch.Methods.Count != 1 || !transpiler.IsStatic
                || NativeMovieBoundary.Fingerprint(transpiler) != spec.Transpiler
                || transpiler.CustomAttributes.Count(a => a.AttributeType.Namespace == "HarmonyLib") != 1
                || !transpiler.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyTranspiler")
                || attributes.Length != 1 || attributes[0].AttributeType.FullName != "HarmonyLib.HarmonyPatch"
                || attributes[0].ConstructorArguments.Count != 3
                || attributes[0].ConstructorArguments[0].Value is not TypeReference owner || owner.FullName != spec.Native
                || attributes[0].ConstructorArguments[1].Value is not string name || name != spec.Method
                || attributes[0].ConstructorArguments[2].Value is not CustomAttributeArgument[] parameters
                || !parameters.Select(p => ((TypeReference)p.Value).FullName).SequenceEqual(spec.Parameters))
                throw new InvalidDataException("Native coordinate transpiler changed its complete source/registration contract: " + patch.FullName);
            if (Discovery.AllTypes(model.Mod.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody && m != transpiler)
                .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MemberReference member && member.DeclaringType?.FullName == patch.FullName))
                throw new InvalidDataException("Native coordinate transpiler acquired an independent caller: " + patch.FullName);
            TypeDefinition native = find(spec.Native) ?? throw new InvalidDataException("Native coordinate owner absent: " + spec.Native);
            MethodDefinition target = native.Methods.Single(m => m.Name == spec.Method
                && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(spec.Parameters));
            if (native.Module.Assembly.Name.Name != spec.Assembly || Discovery.Protected(native)
                || target.IsStatic || target.HasGenericParameters || !target.HasBody
                || NativeMovieBoundary.Fingerprint(target) != spec.Body)
                throw new InvalidDataException("Native coordinate original method/permission contract changed: " + target.FullName);
            prepared.Add((patch, transpiler, target));
        }
        MethodDefinition Callback(string owner, string name, string returns, params string[] arguments)
        {
            TypeDefinition type = model.Mod.MainModule.GetType(Prefix + owner)
                ?? throw new InvalidDataException("Native coordinate callback owner absent: " + owner);
            MethodDefinition method = type.Methods.Single(m => m.Name == name);
            if (!method.IsStatic || method.ReturnType.FullName != returns
                || !method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(arguments))
                throw new InvalidDataException("Native coordinate callback ABI changed: " + method.FullName);
            return method;
        }
        MethodDefinition display = Callback("UguiRaycastCoordinates", "RelativeMouseAtScaled", "UnityEngine.Vector3",
            "UnityEngine.Vector2", "UnityEngine.UI.GraphicRaycaster", "UnityEngine.EventSystems.PointerEventData");
        MethodDefinition viewport = Callback("UguiRaycastCoordinates", "Viewport", "UnityEngine.Vector3",
            "UnityEngine.Vector3", "UnityEngine.Vector3", "UnityEngine.Camera", "UnityEngine.UI.GraphicRaycaster", "UnityEngine.EventSystems.PointerEventData");
        MethodDefinition mouse = Callback("NativeMouseRaycastCoordinates", "Raycast", "System.Void", "UnityEngine.EventSystems.EventSystem",
            "UnityEngine.EventSystems.PointerEventData", "System.Collections.Generic.List`1<UnityEngine.EventSystems.RaycastResult>", "InControl.PointerInputModuleExtended");
        MethodDefinition available = Callback("UguiRaycastCoordinates", "get_Available", "System.Boolean");
        if (NativeMovieBoundary.Fingerprint(available) != "d010602b55be1cc12e6532e6b2cea513b801c9e93a1ee38556c1b23fa4a89910"
            || available.DeclaringType.Fields.Where(f => f.Name is "Original" or "Native").Count(f => f.IsStatic && f.IsInitOnly) != 2)
            throw new InvalidDataException("Native coordinate immutable availability contract changed.");
        MethodDefinition original = find("UnityEngine.UI.MultipleDisplayUtilities")?.Methods.Single(m => m.Name == "RelativeMouseAtScaled")
            ?? throw new InvalidDataException("Native original display utility absent.");
        if (NativeMovieBoundary.Fingerprint(original) != "cf291a416644691ac45ee2c674f53ffedd91e99d685c8a7554094bfdc9ec18b8")
            throw new InvalidDataException("Native display utility fallback contract changed.");
        report.ReflectionRoots.Add(new ReflectionRoot(original.Module.Assembly.Name.Name, original.DeclaringType.FullName));

        // Complete pair/source/native/ABI qualification precedes the first write.
        foreach (MethodDefinition callback in new[] { display, viewport, mouse, available }) callback.IsPublic = true;
        foreach (var entry in prepared)
        {
            bool graphic = entry.Patch.Name == specs[0].Patch;
            Rewrite(entry.Target, graphic, display, viewport, mouse, available);
            var sites = (graphic ? new[] { display, viewport } : new[] { mouse })
                .Select(m => new StaticPatchSite(entry.Target, m, 1)).ToArray();
            string removed = Discovery.MethodKey(entry.Transpiler);
            entry.Patch.Methods.Clear();
            foreach (CustomAttribute attribute in entry.Patch.CustomAttributes.Where(a => a.AttributeType.Namespace == "HarmonyLib").ToArray())
                entry.Patch.CustomAttributes.Remove(attribute);
            model.StaticPatches.Add(new StaticPatchDefinition(entry.Patch, entry.Target, removed,
                sites[0].Replacement, 1, sites.Skip(1)));
            track(entry.Target.Module.Assembly);
        }
        report.StaticSubstitutions.Add("Native coordinates: exact GraphicRaycaster display/viewport and InControl mouse RaycastAll seams compiled from both reviewed desktop transpilers; original availability gate, native input/permission branches, callbacks and unscoped fallbacks retained with closed registration proof.");
    }

    internal static void Rewrite(MethodDefinition target, bool graphic, MethodDefinition display,
        MethodDefinition viewport, MethodDefinition mouse, MethodDefinition available)
    {
        ILProcessor il = target.Body.GetILProcessor();
        foreach (Instruction site in target.Body.Instructions.ToArray())
        {
            if (site.Operand is not MethodReference call) continue;
            var after = new List<Instruction>();
            if (graphic && call.DeclaringType.FullName == "UnityEngine.UI.MultipleDisplayUtilities" && call.Name == "RelativeMouseAtScaled")
            {
                // Desktop installs neither graphic substitution when the original
                // immutable display delegate cannot bind. Keep the original call
                // reachable in that case, even inside an admitted pointer scope.
                Instruction next = site.Next ?? throw new InvalidDataException("Display seam has no continuation.");
                Instruction fallback = Instruction.Create(OpCodes.Call, call);
                site.OpCode = OpCodes.Call; site.Operand = target.Module.ImportReference(available);
                after.AddRange(new[] { Instruction.Create(OpCodes.Brfalse, fallback), Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, target.Module.ImportReference(display)),
                    Instruction.Create(OpCodes.Br, next), fallback });
            }
            else if (graphic && call.DeclaringType.FullName == "UnityEngine.Camera" && call.Name == "ScreenToViewportPoint")
            {
                var screen = new VariableDefinition(target.Module.ImportReference(call.Parameters.Single().ParameterType));
                var camera = new VariableDefinition(target.Module.ImportReference(call.DeclaringType));
                target.Body.Variables.Add(screen); target.Body.Variables.Add(camera);
                OpCode opcode = site.OpCode; site.OpCode = OpCodes.Stloc; site.Operand = screen;
                Instruction next = site.Next ?? throw new InvalidDataException("Viewport seam has no continuation.");
                Instruction fallback = Instruction.Create(OpCodes.Ldloc, screen);
                after.AddRange(new[] { Instruction.Create(OpCodes.Dup), Instruction.Create(OpCodes.Stloc, camera),
                    Instruction.Create(OpCodes.Call, target.Module.ImportReference(available)), Instruction.Create(OpCodes.Brfalse, fallback),
                    Instruction.Create(OpCodes.Ldloc, screen), Instruction.Create(opcode, call), Instruction.Create(OpCodes.Ldloc, screen),
                    Instruction.Create(OpCodes.Ldloc, camera), Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1),
                    Instruction.Create(OpCodes.Call, target.Module.ImportReference(viewport)), Instruction.Create(OpCodes.Br, next),
                    fallback, Instruction.Create(opcode, call) });
            }
            else if (!graphic && call.DeclaringType.FullName == "UnityEngine.EventSystems.EventSystem" && call.Name == "RaycastAll")
            {
                site.OpCode = OpCodes.Ldarg_0; site.Operand = null;
                after.Add(Instruction.Create(OpCodes.Call, target.Module.ImportReference(mouse)));
            }
            Instruction previous = site;
            foreach (Instruction instruction in after) { il.InsertAfter(previous, instruction); previous = instruction; }
        }
        target.Body.MaxStackSize += 5;
        target.Body.SimplifyMacros(); target.Body.OptimizeMacros();
    }
}
