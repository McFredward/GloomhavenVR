using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class NativeRaycastBoundaryTests
{
    const string Prefix = "GloomhavenVR.Hands.Interact.";

    internal static void RunFixture(string fixtureDir, string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        foreach (string variant in new[] { "positive", "missing-display-control", "wrong-viewport-control", "missing-mouse-control", "missing-availability-control" })
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(Path.Combine(fixtureDir, "FixtureGame.dll"));
            ModuleDefinition module = assembly.MainModule;
            TypeDefinition callbacks = module.GetType("RaycastWitness");
            MethodDefinition Callback(string name) => callbacks.Methods.Single(m => m.Name == name);
            MethodDefinition graphic = module.GetType("UnityEngine.UI.GraphicRaycaster").Methods.Single(m => m.Name == "Raycast");
            MethodDefinition mouse = module.GetType("InControl.PointerInputModuleExtended").Methods.Single(m => m.Name == "GetMousePointerEventData");
            if (variant != "missing-display-control")
                NativeRaycastBoundary.Rewrite(graphic, true, Callback("Display"), Callback(variant == "wrong-viewport-control" ? "WrongViewport" : "Viewport"), Callback("Mouse"),
                    Callback(variant == "missing-availability-control" ? "AlwaysAvailable" : "IsAvailable"));
            if (variant != "missing-mouse-control") NativeRaycastBoundary.Rewrite(mouse, false, Callback("Display"), Callback("Viewport"), Callback("Mouse"), Callback("IsAvailable"));
            string path = Path.Combine(output, variant + ".dll"); assembly.Write(path);
            var context = new AssemblyLoadContext("raycast-" + variant, isCollectible: true);
            try
            {
                Assembly runtime = context.LoadFromAssemblyPath(path);
                Type witness = runtime.GetType("RaycastWitness")!, raycaster = runtime.GetType("UnityEngine.UI.GraphicRaycaster")!;
                object owner = Activator.CreateInstance(raycaster)!, data = Activator.CreateInstance(runtime.GetType("UnityEngine.EventSystems.PointerEventData")!)!;
                object results = Activator.CreateInstance(typeof(List<>).MakeGenericType(runtime.GetType("UnityEngine.EventSystems.RaycastResult")!))!;
                var trace = (List<string>)witness.GetField("Trace")!.GetValue(null)!;
                witness.GetField("Expected")!.SetValue(null, owner); witness.GetField("Data")!.SetValue(null, data);
                data.GetType().GetField("Allowed")!.SetValue(data, false);
                raycaster.GetMethod("Raycast")!.Invoke(owner, new[] { data, results });
                check(trace.SequenceEqual(new[] { "rejected" }), "Native rejection acquired a coordinate callback.");
                data.GetType().GetField("Allowed")!.SetValue(data, true); trace.Clear();
                Exception? failure = null;
                try { raycaster.GetMethod("Raycast")!.Invoke(owner, new[] { data, results }); }
                catch (TargetInvocationException e) { failure = e.InnerException; }
                if (variant == "wrong-viewport-control")
                { check(failure is InvalidOperationException { Message: "viewport-control" } && trace.Last() == "finally", "Viewport causal control missed the executed stack/exception oracle."); Console.WriteLine("Executed coordinate boundary: " + variant); continue; }
                check(failure == null, "Coordinate rewrite changed stack or receiver identity: " + failure);
                bool original = trace.SequenceEqual(new[] { "display", "native-display", "native-viewport", "viewport", "finally" });
                if (variant == "missing-display-control")
                { check(!original, "Missing display substitution escaped its executed oracle."); Console.WriteLine("Executed coordinate boundary: " + variant); continue; }
                check(original, "Unscoped fallback or original call/finally order changed.");
                trace.Clear(); witness.GetField("Scoped")!.SetValue(null, true);
                raycaster.GetMethod("Raycast")!.Invoke(owner, new[] { data, results });
                check(trace.SequenceEqual(new[] { "display", "native-viewport", "viewport", "finally" }), "Scoped display/viewport route differs from the original transpiler.");
                trace.Clear(); witness.GetField("Available")!.SetValue(null, false);
                raycaster.GetMethod("Raycast")!.Invoke(owner, new[] { data, results });
                bool unavailableOriginal = trace.SequenceEqual(new[] { "native-display", "native-viewport", "finally" });
                check(variant == "missing-availability-control" ? !unavailableOriginal : unavailableOriginal,
                    "Unavailable original delegate must preserve both untouched native graphic seams, including scoped pointers.");
                witness.GetField("Available")!.SetValue(null, true);
                trace.Clear(); object input = Activator.CreateInstance(runtime.GetType("InControl.PointerInputModuleExtended")!)!;
                check((int)input.GetType().GetMethod("GetMousePointerEventData")!.Invoke(input, new object[] { 3 })! == 1,
                    "Mouse substitution changed native result production.");
                bool nativeMouse = trace.SequenceEqual(new[] { "mouse", "native-all" });
                check(variant == "missing-mouse-control" ? !nativeMouse : nativeMouse, "Mouse owner substitution escaped its executed oracle.");
            }
            finally { context.Unload(); }
            Console.WriteLine("Executed coordinate boundary: " + variant);
        }
    }

    internal static void Verify(Discovery model, string managed, Action<bool, string> check)
    {
        StaticPatchDefinition[] proofs = model.StaticPatches.Where(p => p.PatchClass.FullName.StartsWith(Prefix, StringComparison.Ordinal)).ToArray();
        check(proofs.Length == 2, "Both coordinate registrations require qualified static proof.");
        foreach (StaticPatchDefinition proof in proofs)
        {
            using AssemblyDefinition source = AssemblyDefinition.ReadAssembly(Path.Combine(managed, proof.Target.Module.Assembly.Name.Name + ".dll"));
            MethodDefinition before = source.MainModule.GetType(proof.Target.DeclaringType.FullName).Methods.Single(m => m.FullName == proof.Target.FullName);
            bool GeneratedAvailabilityBranch(Instruction instruction) => instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch
                && instruction.Previous?.Operand is MethodReference previous && previous.DeclaringType.FullName == Prefix + "UguiRaycastCoordinates"
                && previous.Name is "get_Available" or "RelativeMouseAtScaled" or "Viewport";
            string[] Instructions(MethodDefinition method) => method.Body.Instructions.Where(i => !GeneratedAvailabilityBranch(i))
                .Where(i => i.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch || i.OpCode.Code is Code.Stfld or Code.Stsfld)
                .Select(i => i.OpCode.Name.Replace(".s", "", StringComparison.Ordinal) + ":" + (i.Operand is FieldReference f ? f.FullName : "")).ToArray();
            check(Instructions(before).SequenceEqual(Instructions(proof.Target)), "Coordinate conversion changed native permission branches or stores.");
            string[] Calls(MethodDefinition method) => method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>()
                .Where(m => !m.DeclaringType.FullName.StartsWith(Prefix, StringComparison.Ordinal)
                    && !(m.DeclaringType.FullName == "UnityEngine.UI.MultipleDisplayUtilities" && m.Name == "RelativeMouseAtScaled")
                    && !(m.DeclaringType.FullName == "UnityEngine.Camera" && m.Name == "ScreenToViewportPoint")
                    && !(m.DeclaringType.FullName == "UnityEngine.EventSystems.EventSystem" && m.Name == "RaycastAll")).Select(m => m.FullName).ToArray();
            check(Calls(before).SequenceEqual(Calls(proof.Target)), "Coordinate conversion changed native input/viewport/graphic callback order.");
            check(before.Body.ExceptionHandlers.Count == proof.Target.Body.ExceptionHandlers.Count, "Coordinate conversion changed native exception regions.");
            if (proof.PatchClass.Name == "GraphicRaycaster_SyntheticCoordinates")
            {
                check(proof.Target.Body.Instructions.Count(GeneratedAvailabilityBranch) == 4,
                    "The exact two availability guards and their two merge branches must be retained.");
                check(proof.Target.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.UI.MultipleDisplayUtilities"
                    && m.Name == "RelativeMouseAtScaled") == 1
                    && proof.Target.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.Camera"
                        && m.Name == "ScreenToViewportPoint") == 2, "The original unavailable display/viewport calls were lost.");
            }
            check(proof.PatchClass.Methods.Count == 0 && proof.PatchClass.NestedTypes.Count == 0, "A coordinate runtime emitter survived conversion.");
        }
        check(proofs.Sum(p => p.ReplacementCalls + p.AdditionalSites.Sum(s => s.Calls)) == 3, "Coordinate substitution site census changed.");
        Console.WriteLine("Native coordinates: 2 exact original methods, 3 sites; native stores, permission branches and remaining call order retained.");
    }

    internal static void Controls(string modPath, string managed, string output, Action<bool, string> check)
    {
        foreach (string label in new[] { "graphic transpiler", "mouse transpiler", "registration", "callback ABI", "availability getter", "availability mutation", "native permission", "native fallback", "independent caller", "late proof body", "late proof callback" })
        {
            using Discovery model = Discovery.Load(modPath, managed);
            TypeDefinition Graphic() => model.Mod.MainModule.GetType(Prefix + "GraphicRaycaster_SyntheticCoordinates");
            if (label.StartsWith("late", StringComparison.Ordinal))
            {
                AuditReport positive = model.Audit(); check(positive.Complete, "Writer control requires the actual qualified candidate.");
                StaticPatchDefinition proof = model.StaticPatches.Single(p => p.PatchClass.FullName == Prefix + "GraphicRaycaster_SyntheticCoordinates");
                if (label == "late proof body") proof.Target.Body.GetILProcessor().InsertBefore(proof.Target.Body.Instructions[0], Instruction.Create(OpCodes.Nop));
                else proof.Target.Body.Instructions.Single(i => i.Operand is MethodReference c && c.FullName == proof.AdditionalSites.Single().Replacement.FullName)
                    .Operand = new MethodReference("OtherViewport", model.Mod.MainModule.TypeSystem.Void, proof.Replacement.DeclaringType);
                Exception? failure = null;
                try { new Weaver(model).Write(Path.Combine(output, label.Replace(' ', '-')), positive, false); }
                catch (InvalidDataException e) { failure = e; }
                check(failure != null, "Writer accepted a changed coordinate static proof: " + label);
            }
            else
            {
                if (label.EndsWith("transpiler", StringComparison.Ordinal))
                {
                    TypeDefinition type = label.StartsWith("graphic", StringComparison.Ordinal) ? Graphic() : model.Mod.MainModule.GetType(Prefix + "PointerInputModuleExtended_CapturedCoordinates");
                    MethodDefinition method = type.Methods.Single(); method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[0], Instruction.Create(OpCodes.Nop));
                }
                if (label == "registration") Graphic().CustomAttributes.Single(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ConstructorArguments[1]
                    = new CustomAttributeArgument(model.Mod.MainModule.TypeSystem.String, "OtherRaycast");
                if (label == "callback ABI") model.Mod.MainModule.GetType(Prefix + "UguiRaycastCoordinates").Methods.Single(m => m.Name == "Viewport").Parameters.RemoveAt(4);
                if (label == "availability getter")
                {
                    MethodDefinition method = model.Mod.MainModule.GetType(Prefix + "UguiRaycastCoordinates").Methods.Single(m => m.Name == "get_Available");
                    method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[0], Instruction.Create(OpCodes.Nop));
                }
                if (label == "availability mutation") model.Mod.MainModule.GetType(Prefix + "UguiRaycastCoordinates").Fields.Single(f => f.Name == "Native").IsInitOnly = false;
                if (label is "native permission" or "native fallback")
                {
                    MethodDefinition callback = model.Mod.MainModule.GetType(Prefix + "UguiRaycastCoordinates").Methods.Single(m => m.Name == "RelativeMouseAtScaled");
                    ModuleDefinition native = callback.Parameters[1].ParameterType.Resolve().Module;
                    MethodDefinition method = label == "native permission" ? native.GetType("UnityEngine.UI.GraphicRaycaster").Methods.Single(m => m.Name == "Raycast" && m.Parameters.Count == 2)
                        : native.GetType("UnityEngine.UI.MultipleDisplayUtilities").Methods.Single(m => m.Name == "RelativeMouseAtScaled");
                    method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[0], Instruction.Create(OpCodes.Nop));
                }
                if (label == "independent caller")
                {
                    MethodDefinition callback = model.Mod.MainModule.GetType(Prefix + "UguiRaycastCoordinates").Methods.Single(m => m.Name == "Viewport");
                    callback.Body.GetILProcessor().InsertBefore(callback.Body.Instructions[0], Instruction.Create(OpCodes.Call, Graphic().Methods.Single()));
                }
                AuditReport report = model.Audit();
                check(!report.Complete && report.Issues.Any(i => i.Code == "CURRENT_MOD_AOT_UNSUPPORTED"), "Changed coordinate contract did not fail closed: " + label);
            }
            Console.WriteLine("Rejected causal coordinate control: " + label);
        }
    }
}
