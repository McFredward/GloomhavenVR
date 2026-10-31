using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QuestWeaver;

/// <summary>Qualify the exact desktop menu-depth contracts before Android AOT conversion.
/// Every input is read through Cecil. No mod initializer, selector or Unity API runs
/// on the host, and original game assemblies remain read-only.</summary>
internal static class MenuDepthCompatibility
{
    const string Prefix = "GloomhavenVR.WorldUI.MenuDepth.";
    const string Observer = Prefix + "MenuDepthLifecycleObserver";
    // Complete optimized IL pins bind the selector's iterator/control flow and
    // the source-owned descriptor tables. Unknown registrations fail closed.
    static readonly Dictionary<string, string> Methods = new(StringComparer.Ordinal) {
        ["MenuDepthLifecycleObserver::Targets"] = "7e44a05bbdc05dc79639d8239a6e0d9b0aa9ae3be76df0e6f738a7d1d846d872",
        ["MenuDepthDriver::NativeOwnerLifecycles"] = "9c02974913afc6ce01bdf5f2d5df0520dcc6a2b51a2fe92ac2a673ff6df90e1d",
        ["MenuDepthDriver::.cctor"] = "b035ba687ac6152a671b7a930529b6222cb392731703a07e66b6a1e0a3667fe8",
        ["MenuDepthCards::NativeTypes"] = "c1da040f4fd514b3adcccfad07e9aeea2704cf7da702c03c7f325d96d7af5e75",
        ["MenuDepthCards::.cctor"] = "9cdd9cd93f5a05d4fa250f084c987c7025a6e47adae34b6f892598ed4f9cddbe" };
    static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal) {
        ["MenuDepthLifecycleObserver/<Targets>d__1"] = "b0e23458bf3addbd4614c97778da2a66fb918843011f46b2b8c7360e3faa4963",
        ["MenuDepthDriver/<NativeOwnerLifecycles>d__49"] = "72041c4d0d31d5a82044731ff8def0a53d5aca7a8d78336f2e3d5298a36bb439",
        ["MenuDepthDriver/Spec"] = "2f65c9450084ba7d47eb18c70164a9f0e03f3a8dc292d0eaa72b6dafa054235c",
        ["MenuDepthCards/<NativeTypes>d__6"] = "679aedea1b15737575257ade0d3f0e098a11464ebfcd1c24d0e6322c511d2599",
        ["MenuDepthCards/Kind"] = "4ffc579e808cfaa079dd88958557865d946aa710adec19af13c97298bf4ec52e",
        ["MenuDepthCharacters/<>c__DisplayClass32_0"] = "ebf1a06fd6602d707052d6381981c338ff5664a40a7884c0102139544194b52d",
        // Build742's additional sampling members move this generated closure
        // from 83 to 85. Complete factory/closure comparison with the qualified
        // Quest741 input proves only that exact generated-name change.
        ["MenuDepthCharacterSource/<>c__DisplayClass85_0"] = "b36e8be14e5d1c3964c140e62fd7958790907123c1c1a5c036b1048068843ea0" };
    // These literal names come from the completely pinned descriptor tables.
    // Resolve exact owner assembly and inherited no-argument members, then apply
    // the original native-assembly filter and method-handle deduplication.
    static readonly string[] Owners = { "StoryImageViewer", "UIEventPanel", "CompendiumWindow", "UICharacterStoryBox",
        "UIReward", "UICampaignReward", "UIQuestDescription", "UICharacterClassInformation",
        "UICharacterCreatorClassInformation", "UICharacterCreatorClassRosterSlot", "UIPartyCharacterEquippementSlot",
        "UIEnhancementButtonHighlight", "UIPartyCharacterEnhancementAbilityCardsDisplay", "UITab" };
    static readonly string[] Cards = { "FullAbilityCard", "MiniAbilityCard", "ItemCardUI", "MonsterRoundCardUI",
        "MonsterBaseUI", "UIPerkAttackModifier", "UIScenarioAttackModifier", "AbilityCardUI" };

    internal static void Apply(Discovery model, AuditReport report, Func<string, TypeDefinition?> find)
    {
        ModuleDefinition module = model.Mod.MainModule;
        TypeDefinition? observer = module.GetType(Observer);
        if (observer == null) return;
        QualifyLifecycle(module);
        MethodDefinition[] targets = LifecycleTargets(observer, find);
        var readers = new[] {
            PrepareReader(module, find, "MenuDepthCharacters", "CreateHiddenReader", "isHidden", "System.Boolean", "b5aa95b6621865760a421305cd960f8293b0ce75934cee88b40ba6084bf42b7b"),
            PrepareReader(module, find, "MenuDepthCharacterSource", "CreateOriginalReader", "character3D",
                "Character3DDisplayManager/Character3D", "0b6263d56b290df0ba7acef4408948d808ccd795fa680ec58f23da7212a544bb") };
        MethodDefinition directory = module.GetType(Prefix + "MenuDepthCatalogue").Methods.Single(m => m.Name == "get_DirectoryPath");
        RequireHash(directory, "91dd0e6a45b336572ce7054c7b3da6ef5192da7ef77e96ad7f3c570721e428fb");
        MethodDefinition resourceRoot = module.GetType("GloomhavenVR.Core.QuestStandalonePlatform").Methods.Single(m =>
            m.Name == "get_EnvironmentBundleDirectory" && m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.String");
        MethodReference combine = directory.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>()
            .Single(m => m.FullName == "System.String System.IO.Path::Combine(System.String,System.String,System.String)");

        // All contracts are qualified before mutating the private converted mod.
        foreach (var reader in readers)
        {
            foreach (Instruction instruction in reader.Guard) Nop(instruction);
            HashSet<Instruction> reachable = Reachable(reader.Method);
            var boundaries = reader.Method.Body.ExceptionHandlers.SelectMany(h => new[] {
                h.TryStart, h.TryEnd, h.HandlerStart, h.HandlerEnd, h.FilterStart }).Where(i => i != null).ToHashSet();
            foreach (Instruction instruction in reader.Method.Body.Instructions.Where(i => !reachable.Contains(i)).ToArray())
            {
                if (boundaries.Contains(instruction)) Nop(instruction);
                else reader.Method.Body.GetILProcessor().Remove(instruction);
            }
            if (reader.Method.Body.Instructions.Any(i => i.Operand is MemberReference { DeclaringType: not null } member
                && member.DeclaringType.Namespace.StartsWith("System.Reflection.Emit", StringComparison.Ordinal)))
                throw new InvalidDataException("Menu-depth Android accessor retained a reachable dynamic emitter.");
            report.ReflectionRoots.Add(new ReflectionRoot(reader.Field.DeclaringType.Module.Assembly.Name.Name, reader.Field.DeclaringType.FullName));
        }
        // The exact attributed selector is private and has no direct callers.
        // Canonical naming lets the existing AOT runtime registry execute its
        // unchanged enumeration/side effects and enforce the compiled closure.
        observer.Methods.Single(m => m.Name == "Targets").Name = "TargetMethods";
        directory.Body = new MethodBody(directory);
        ILProcessor il = directory.Body.GetILProcessor();
        il.Emit(OpCodes.Call, resourceRoot); il.Emit(OpCodes.Ldstr, "MenuDepth"); il.Emit(OpCodes.Ldstr, "Story");
        il.Emit(OpCodes.Call, combine); il.Emit(OpCodes.Ret);
        foreach (MethodDefinition target in targets)
            report.ReflectionRoots.Add(new ReflectionRoot(target.Module.Assembly.Name.Name, target.DeclaringType.FullName));
        report.StaticSubstitutions.Add("Menu depth: exact native lifecycle selector retained under its canonical AOT name; only statically unreachable desktop emitter instructions removed after proving Android field-reader branches; loose story fields resolve below the configured Quest resource root.");
    }

    internal static MethodDefinition[] LifecycleTargets(TypeDefinition observer, Func<string, TypeDefinition?> find)
    {
        if (observer.FullName != Observer) throw new InvalidDataException("Unknown menu-depth lifecycle selector owner.");
        TypeDefinition story = find("StoryImageViewer") ?? throw new InvalidDataException("Original story owner is absent.");
        TypeDefinition tab = find("UnityEngine.UI.UITab") ?? throw new InvalidDataException("Original tab owner is absent.");
        var result = new List<MethodDefinition>();
        void Add(MethodDefinition? method, bool originalOnly = false)
        {
            if (method == null || originalOnly && method.DeclaringType.Module.Assembly != story.Module.Assembly) return;
            if (!result.Contains(method)) result.Add(method);
        }
        foreach (string name in Owners.Concat(Cards))
        {
            TypeDefinition? owner = story.Module.GetType(name);
            if (owner == null && Owners.Contains(name, StringComparer.Ordinal)) owner = tab.Module.GetType("UnityEngine.UI." + name);
            if (owner == null) continue; // Same optional original-type behavior as the verified native iterator.
            Add(FindMethod(owner, "Awake"), true); Add(FindMethod(owner, "OnEnable"), true);
        }
        TypeDefinition graphic = find("UnityEngine.UI.Graphic") ?? throw new InvalidDataException("Original Graphic is absent.");
        foreach (string name in new[] { "OnEnable", "OnDestroy", "OnTransformParentChanged", "OnCanvasHierarchyChanged" })
            Add(FindMethod(graphic, name));
        TypeDefinition raw = find("UnityEngine.UI.RawImage") ?? throw new InvalidDataException("Original RawImage is absent.");
        for (TypeDefinition? type = raw; type != null; type = type.BaseType?.Resolve())
        {
            PropertyDefinition? texture = type.Properties.SingleOrDefault(p => p.Name == "texture");
            if (texture != null) { Add(texture.SetMethod); break; }
        }
        Add(FindMethod(find("UnityEngine.UI.UIWindow") ?? throw new InvalidDataException("Original UIWindow is absent."), "OnEnable"));
        Add(FindMethod(find("Character3DDisplayManager") ?? throw new InvalidDataException("Original character display is absent."), "Start"));
        return result.ToArray();
    }

    static MethodDefinition? FindMethod(TypeDefinition? type, string name)
    {
        for (; type != null; type = type.BaseType?.Resolve())
        {
            MethodDefinition? method = type.Methods.SingleOrDefault(m => m.Name == name && m.Parameters.Count == 0);
            if (method != null) return method;
        }
        return null;
    }

    static void QualifyLifecycle(ModuleDefinition module)
    {
        foreach (var pin in Methods)
        {
            string[] key = pin.Key.Split("::");
            MethodDefinition method = module.GetType(Prefix + key[0]).Methods.Single(m => m.Name == key[1]);
            RequireHash(method, pin.Value);
        }
        foreach (var pin in Types)
        {
            TypeDefinition type = Discovery.AllTypes(module).Single(t => t.FullName == Prefix + pin.Key);
            string text = string.Join("\n", type.Fields.Select(f => f.FullName + ":" + f.Attributes)) + "\n"
                + string.Join("\n", type.Methods.Select(m => m.HasBody ? NativeMovieBoundary.Fingerprint(m) : m.FullName));
            string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            if (actual != pin.Value) throw new InvalidDataException("Menu-depth selector metadata changed: " + type.FullName + "; sha256=" + actual);
        }
        MethodDefinition selector = module.GetType(Observer).Methods.Single(m => m.Name == "Targets");
        TypeDefinition owner = selector.DeclaringType;
        MethodDefinition[] selectors = owner.Methods.Where(m => m.Name is "TargetMethod" or "TargetMethods"
            || m.CustomAttributes.Any(a => a.AttributeType.FullName is "HarmonyLib.HarmonyTargetMethod" or "HarmonyLib.HarmonyTargetMethods")).ToArray();
        CustomAttribute[] patches = owner.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToArray();
        if (selectors.Length != 1 || selectors[0] != selector || patches.Length != 1 || patches[0].ConstructorArguments.Count != 0
            || !selector.IsPrivate || !selector.IsStatic || selector.Parameters.Count != 0
            || selector.CustomAttributes.Count(a => a.AttributeType.FullName == "HarmonyLib.HarmonyTargetMethods") != 1
            || Discovery.AllTypes(module).SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference call && call.FullName == selector.FullName))
            throw new InvalidDataException("Menu-depth private lifecycle selector has an unfamiliar registration or direct caller.");
    }

    static (MethodDefinition Method, Instruction[] Guard, FieldDefinition Field) PrepareReader(ModuleDefinition module,
        Func<string, TypeDefinition?> find, string owner, string factory, string fieldName, string fieldType, string hash)
    {
        MethodDefinition method = module.GetType(Prefix + owner).Methods.Single(m => m.Name == factory);
        RequireHash(method, hash);
        if (!method.IsPrivate || !method.IsStatic || method.Parameters.Count != 0)
            throw new InvalidDataException("Menu-depth readonly accessor factory signature changed.");
        FieldDefinition field = (find("Character3DDisplayManager") ?? throw new InvalidDataException("Original character display is absent."))
            .Fields.Single(f => f.Name == fieldName);
        if (field.IsStatic || field.FieldType.FullName != fieldType)
            throw new InvalidDataException("Menu-depth original readonly field type changed: " + field.FullName);
        Instruction platform = method.Body.Instructions.Single(i => i.Operand is MethodReference call
            && call.FullName == "UnityEngine.RuntimePlatform UnityEngine.Application::get_platform()");
        Instruction literal = platform.Next, branch = literal.Next;
        if (literal.OpCode.Code is not (Code.Ldc_I4 or Code.Ldc_I4_S) || Convert.ToInt32(literal.Operand) != 11
            || branch.OpCode.Code is not (Code.Bne_Un or Code.Bne_Un_S) || branch.Operand is not Instruction desktop
            || !method.Body.Instructions.SkipWhile(i => i != desktop).Any(i => i.Operand is MethodReference call
                && call.DeclaringType.FullName == "System.Reflection.Emit.DynamicMethod"))
            throw new InvalidDataException("Menu-depth factory no longer branches from Android to its exact desktop emitter.");
        return (method, new[] { platform, literal, branch }, field);
    }

    static HashSet<Instruction> Reachable(MethodDefinition method)
    {
        var seen = new HashSet<Instruction>(); var queue = new Queue<Instruction>();
        queue.Enqueue(method.Body.Instructions[0]);
        while (queue.Count > 0)
        {
            Instruction i = queue.Dequeue(); if (!seen.Add(i)) continue;
            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
                if (method.Body.Instructions.IndexOf(i) >= method.Body.Instructions.IndexOf(handler.TryStart)
                    && (handler.TryEnd == null || method.Body.Instructions.IndexOf(i) < method.Body.Instructions.IndexOf(handler.TryEnd)))
                { queue.Enqueue(handler.HandlerStart); if (handler.FilterStart != null) queue.Enqueue(handler.FilterStart); }
            if (i.Operand is Instruction target) queue.Enqueue(target);
            if (i.Operand is Instruction[] targets) foreach (Instruction branchTarget in targets) queue.Enqueue(branchTarget);
            if (i.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw) && i.Next != null) queue.Enqueue(i.Next);
        }
        return seen;
    }

    static void Nop(Instruction instruction) { instruction.OpCode = OpCodes.Nop; instruction.Operand = null; }
    static void RequireHash(MethodDefinition method, string expected)
    {
        string actual = NativeMovieBoundary.Fingerprint(method);
        if (actual != expected) throw new InvalidDataException("Menu-depth method is outside its complete optimized IL contract: " + method.FullName + "; sha256=" + actual);
    }
}
