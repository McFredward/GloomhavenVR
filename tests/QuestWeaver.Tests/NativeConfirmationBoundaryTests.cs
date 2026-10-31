using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class NativeConfirmationBoundaryTests
{
    const string Prefix = "GloomhavenVR.WorldUI.";

    internal static void RunFixture(string fixtureDir, string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        foreach (string variant in new[] { "positive", "missing-identity-control", "bypassed-entry-control", "early-capture-control" })
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(Path.Combine(fixtureDir, "FixtureGame.dll"));
            ModuleDefinition module = assembly.MainModule;
            TypeDefinition callbacks = module.GetType("ConfirmationWitness");
            var methods = new[] { "BeforeIdentityWrite", "BeforePartyPage", "BeforeInventoryClose", "CaptureDeferredOrigin" }
                .ToDictionary(n => n, n => callbacks.Methods.Single(m => m.Name == n));
            TypeDefinition deferred = module.GetType("DeferredWitness");
            deferred.NestedTypes.Single().Fields.Single(f => f.Name == "Owner").Name = "<>4__this";
            MethodDefinition selection = module.GetType("UIAdventurePartyAssemblyWindow").Methods.Single(m => m.Name == "Select");
            Instruction write = selection.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld);
            // Enter the store with its original object/value already on stack.
            // A branch directly to the old store must enter its callback too.
            selection.Body.GetILProcessor().InsertBefore(write, Instruction.Create(OpCodes.Ldc_I4_0));
            selection.Body.GetILProcessor().InsertBefore(write, Instruction.Create(OpCodes.Switch, new[] { write }));
            foreach (var spec in new[] { ("UIAdventurePartyAssemblyWindow", "Select"), ("UIPartyItemInventoryDisplay", "Display"),
                ("NewPartyDisplayUI", "Level"), ("NewPartyDisplayUI", "Close"), ("DeferredWitness", "Create") })
                NativeConfirmationBoundary.Rewrite(module.GetType(spec.Item1).Methods.Single(m => m.Name == spec.Item2), spec.Item1 == "DeferredWitness", methods);
            if (variant == "missing-identity-control")
                foreach (Instruction i in module.GetType("UIAdventurePartyAssemblyWindow").Methods.Single(m => m.Name == "Select").Body.Instructions
                    .Where(i => i.Operand is MethodReference c && c.Name == "BeforeIdentityWrite"))
                    i.Operand = callbacks.Methods.Single(m => m.Name == "NoopIdentity");
            if (variant == "bypassed-entry-control")
                selection.Body.Instructions.Single(i => i.OpCode == OpCodes.Switch).Operand = new[] { write };
            if (variant == "early-capture-control")
            {
                MethodDefinition create = deferred.Methods.Single(m => m.Name == "Create");
                Instruction capture = create.Body.Instructions.Single(i => i.Operand is MethodReference c && c.Name == "CaptureDeferredOrigin");
                Instruction store = create.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld);
                Instruction[] sequence = { capture.Previous.Previous, capture.Previous, capture };
                ILProcessor il = create.Body.GetILProcessor();
                foreach (Instruction i in sequence) il.Remove(i);
                foreach (Instruction i in sequence) il.InsertBefore(store, i);
            }
            string path = Path.Combine(output, variant + ".dll"); assembly.Write(path);
            var context = new AssemblyLoadContext("confirmation-" + variant, isCollectible: true);
            try
            {
                Assembly runtime = context.LoadFromAssemblyPath(path);
                var trace = (IEnumerable<string>)runtime.GetType("ConfirmationWitness")!.GetField("Trace")!.GetValue(null)!;
                object party = Activator.CreateInstance(runtime.GetType("UIAdventurePartyAssemblyWindow")!)!;
                MethodInfo select = party.GetType().GetMethod("Select")!;
                check(!(bool)select.Invoke(party, new object[] { "blocked", false })!, "Native rejection return was changed.");
                check(party.GetType().GetField("selectedCharacter")!.GetValue(party) is "old" && trace.SequenceEqual(new[] { "rejected" }),
                    "Rejected selection observed a write or acquired an identity callback.");
                check((bool)select.Invoke(party, new object[] { "next", true })!, "Accepted native selection return changed.");
                bool identity = trace.SequenceEqual(new[] { "rejected", "selectedCharacter:old>next", "stored:next", "finally" });
                if (variant is "missing-identity-control" or "bypassed-entry-control")
                { check(!identity, "Missing/bypassed identity callback control escaped the executed oracle."); Console.WriteLine("Rejected executed confirmation control: " + variant); continue; }
                check(identity, "Callback must observe the old identity before the original accepted write, retaining finally.");
                object inventory = Activator.CreateInstance(runtime.GetType("UIPartyItemInventoryDisplay")!)!;
                inventory.GetType().GetMethod("Display")!.Invoke(inventory, new object[] { 7 });
                check(trace.TakeLast(2).SequenceEqual(new[] { "slotNum:3>7", "slot:7" }), "Value-type store lost its boxing, old value or exact new value.");
                object owner = Activator.CreateInstance(runtime.GetType("NewPartyDisplayUI")!)!;
                owner.GetType().GetMethod("Level")!.Invoke(owner, new[] { Activator.CreateInstance(runtime.GetType("UILevelUpWindow")!) });
                owner.GetType().GetMethod("Close")!.Invoke(owner, new[] { inventory });
                check(trace.TakeLast(4).SequenceEqual(new[] { "LEVELUP:True:NewPartyDisplayUI", "show", "before-hide:NewPartyDisplayUI", "hide" }),
                    "Native Show/Hide receiver, argument stack or notification order changed.");
                object deferredOwner = Activator.CreateInstance(runtime.GetType("DeferredWitness")!)!;
                Exception? failure = null;
                try { deferredOwner.GetType().GetMethod("Create")!.Invoke(deferredOwner, null); } catch (TargetInvocationException e) { failure = e.InnerException; }
                if (variant == "early-capture-control")
                    check(failure is InvalidOperationException { Message: "capture-before-store" }, "Early deferred capture control did not fail its actual owner-store oracle.");
                else check(failure == null && trace.Last() == "captured", "Deferred callback did not observe its exact original owner after storage.");
            }
            finally { context.Unload(); }
            Console.WriteLine("Executed confirmation boundary: " + variant);
        }
    }

    internal static void Verify(Discovery model, string managed, Action<bool, string> check)
    {
        if (model.Mod.MainModule.GetType(Prefix + "ConfirmationIdentityWritePatch") == null) return;
        using AssemblyDefinition original = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "GH.Runtime.dll"));
        StaticPatchDefinition[] proofs = model.StaticPatches.Where(p => p.PatchClass.Name.StartsWith("Confirmation", StringComparison.Ordinal)).ToArray();
        check(proofs.Length == 2, "Both confirmation registrations require their exact static proof.");
        var sites = proofs.SelectMany(p => new[] { new StaticPatchSite(p.Target, p.Replacement, p.ReplacementCalls) }.Concat(p.AdditionalSites)).ToArray();
        foreach (var group in sites.GroupBy(s => s.Target))
        {
            MethodDefinition after = group.Key;
            MethodDefinition before = original.MainModule.GetType(after.DeclaringType.FullName).Methods.Single(m => m.FullName == after.FullName);
            string[] Stores(MethodDefinition method) => method.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld).Select(i => ((FieldReference)i.Operand).FullName).ToArray();
            string[] Calls(MethodDefinition method) => method.Body.Instructions.Where(i => i.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
                .Select(i => (MethodReference)i.Operand).Where(c => c.DeclaringType.FullName != Prefix + "ConfirmationOnOwner").Select(c => c.FullName).ToArray();
            check(Stores(before).SequenceEqual(Stores(after)), "Confirmation conversion changed an original field store or native write order: " + before.FullName);
            check(Calls(before).SequenceEqual(Calls(after)), "Confirmation conversion changed an original call/constructor or native continuation order: " + before.FullName);
            check(before.Body.ExceptionHandlers.Count == after.Body.ExceptionHandlers.Count, "Confirmation conversion changed native exception coverage.");
            var branchesBefore = before.Body.Instructions.Where(i => i.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch).ToArray();
            var branchesAfter = after.Body.Instructions.Where(i => i.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch).ToArray();
            check(branchesBefore.Length == branchesAfter.Length && branchesBefore.Zip(branchesAfter).All(pair =>
                pair.First.OpCode.Name.Replace(".s", "", StringComparison.Ordinal) == pair.Second.OpCode.Name.Replace(".s", "", StringComparison.Ordinal)),
                "Confirmation conversion changed native permission branch conditions.");
        }
        check(sites.Select(s => s.Target).Distinct().Count() == 29 && sites.Sum(s => s.Calls) == 35,
            "Confirmation target/site census differs from the independently reviewed native IL.");
        check(proofs.All(p => p.PatchClass.Methods.Count == 0 && p.PatchClass.NestedTypes.Count == 0), "Removed confirmation emitters/iterators remain in the AOT closure.");
        Console.WriteLine("Native confirmation boundary: " + sites.Select(s => s.Target).Distinct().Count() + " methods, " + sites.Sum(s => s.Calls) + " callback sites; all original stores/calls/permission branch conditions retained.");
    }

    internal static void Controls(string modPath, string managed, string output, Action<bool, string> check)
    {
        foreach (string label in new[] { "identity predicate", "deferred predicate", "selector catalogue", "independent caller", "callback ABI",
            "native permission branch", "native overload", "native field authority", "additional native proof body", "additional native proof callback" })
        {
            using Discovery model = Discovery.Load(modPath, managed);
            TypeDefinition Patch(string name) => model.Mod.MainModule.GetType(Prefix + name);
            ModuleDefinition Native() => Patch("ConfirmationNavigationCatalog").Methods.Single(m => m.Name == "NativeType")
                .Body.Instructions.Select(i => i.Operand).OfType<TypeReference>().Where(t => t.FullName == "UILevelUpWindow")
                .DistinctBy(t => t.FullName).Single().Resolve().Module;
            bool writer = label.StartsWith("additional", StringComparison.Ordinal);
            if (writer)
            {
                AuditReport positive = model.Audit();
                check(positive.Complete, "Confirmation writer control requires a qualified actual candidate.");
                StaticPatchSite site = model.StaticPatches.Single(p => p.PatchClass.Name == "ConfirmationIdentityWritePatch").AdditionalSites[0];
                if (label.EndsWith("body", StringComparison.Ordinal))
                    site.Target.Body.GetILProcessor().InsertBefore(site.Target.Body.Instructions[0], Instruction.Create(OpCodes.Nop));
                else
                    site.Target.Body.Instructions.First(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference c && c.FullName == site.Replacement.FullName)
                        .Operand = new MethodReference("OtherCallback", site.Replacement.ReturnType, site.Replacement.DeclaringType);
                Exception? failure = null;
                try { new Weaver(model).Write(Path.Combine(output, label.Replace(' ', '-')), positive, false); } catch (InvalidDataException e) { failure = e; }
                check(failure != null, "Writer accepted changed additional confirmation substitution: " + label);
            }
            else
            {
                if (label == "identity predicate")
                {
                    MethodDefinition predicate = Patch("ConfirmationIdentityWritePatch").Methods.Single(m => m.Name == "IdentityWrite");
                    predicate.Body.Instructions.First(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "selectedCharacter").Operand = "otherIdentity";
                }
                if (label == "deferred predicate")
                    Patch("ConfirmationDeferredCapturePatch").NestedTypes.Single(t => t.Name.StartsWith("<Transpiler>", StringComparison.Ordinal)).Methods.Single(m => m.Name == "MoveNext")
                        .Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "<>4__this").Operand = "otherOwner";
                if (label == "selector catalogue")
                {
                    MethodDefinition catalogue = Patch("ConfirmationNavigationCatalog").Methods.Single(m => m.Name == "NativeType");
                    catalogue.Body.GetILProcessor().InsertBefore(catalogue.Body.Instructions[0], Instruction.Create(OpCodes.Nop));
                }
                if (label == "independent caller")
                {
                    MethodDefinition callback = Patch("ConfirmationOnOwner").Methods.Single(m => m.Name == "BeforeInventoryClose");
                    callback.Body.GetILProcessor().InsertBefore(callback.Body.Instructions[0], Instruction.Create(OpCodes.Call,
                        Patch("ConfirmationIdentityWritePatch").Methods.Single(m => m.Name == "TargetMethods")));
                }
                if (label == "callback ABI") Patch("ConfirmationOnOwner").Methods.Single(m => m.Name == "CaptureDeferredOrigin").Parameters.RemoveAt(1);
                if (label == "native permission branch")
                {
                    MethodDefinition native = Native().GetType("UIAdventurePartyAssemblyWindow").Methods.Single(m => m.Name == "SelectCharacter");
                    native.Body.Instructions.First(i => i.OpCode == OpCodes.Brfalse_S).OpCode = OpCodes.Brtrue_S;
                }
                if (label == "native overload")
                {
                    ModuleDefinition native = Native();
                    var method = new MethodDefinition("Show", Mono.Cecil.MethodAttributes.Private, native.TypeSystem.Void);
                    method.Body.GetILProcessor().Emit(OpCodes.Ret); native.GetType("UILevelUpWindow").Methods.Add(method);
                }
                if (label == "native field authority") Native().GetType("UIAdventurePartyAssemblyWindow").Fields.Single(f => f.Name == "selectedCharacter").IsStatic = true;
                AuditReport report = model.Audit();
                check(!report.Complete && report.Issues.Any(i => i.Code == "CURRENT_MOD_AOT_UNSUPPORTED"), "Changed confirmation contract did not fail closed: " + label);
            }
            Console.WriteLine("Rejected causal confirmation control: " + label);
        }
    }
}
