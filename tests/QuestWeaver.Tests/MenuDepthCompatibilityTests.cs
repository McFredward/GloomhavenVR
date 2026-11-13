using System.Security.Cryptography;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using QuestWeaver;

internal static class MenuDepthCompatibilityTests
{
    internal static void Run(string root, string modPath, string output, Action<bool, string> check)
    {
        const string menu = "GloomhavenVR.WorldUI.MenuDepth.";
        string managed = Path.Combine(root, "ressources/GH_Data/Managed");
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var immutable = new[] { modPath, Path.Combine(managed, "GH.Runtime.dll"), Path.Combine(managed, "UnityEngine.UI.dll") }
            .ToDictionary(path => path, Hash);
        using (Discovery model = Discovery.Load(modPath, managed))
        {
            // Capture the complete current menu-depth pipeline, including new
            // character creation framing and mesh-backed pointer presentation.
            // Only the four explicitly qualified factory/selector/path bodies
            // may change during conversion.
            var originalHeadMethods = Discovery.AllTypes(model.Mod.MainModule)
                .Where(t => t.FullName.StartsWith(menu, StringComparison.Ordinal)).SelectMany(t => t.Methods)
                .Where(m => m.HasBody && !(m.DeclaringType.Name == "MenuDepthCharacters" && m.Name == "CreateHiddenReader")
                    && !(m.DeclaringType.Name == "MenuDepthCharacterSource" && m.Name == "CreateOriginalReader")
                    && !(m.DeclaringType.Name == "MenuDepthCatalogue" && m.Name == "get_DirectoryPath")
                    && !(m.DeclaringType.Name == "MenuDepthLifecycleObserver" && m.Name == "Targets"))
                .ToDictionary(m => m.FullName, NativeMovieBoundary.Fingerprint);
            var originalClosures = new[] { "MenuDepthCharacters", "MenuDepthCharacterSource" }.ToDictionary(name => name,
                name => model.Mod.MainModule.GetType(menu + name).NestedTypes.Where(t => t.Name.Contains("DisplayClass", StringComparison.Ordinal))
                    .SelectMany(t => t.Methods).Where(m => m.Name.Contains("Create", StringComparison.Ordinal))
                    .ToDictionary(m => m.FullName, NativeMovieBoundary.Fingerprint));
            MethodDefinition factory = model.Mod.MainModule.GetType(menu + "MenuDepthCharacters").Methods.Single(m => m.Name == "CreateHiddenReader");
            int handlers = factory.Body.ExceptionHandlers.Count;
            AuditReport report = model.Audit();
            check(report.Issues.Count == 0 && report.Complete, "Actual menu-depth conversion blocked: " + string.Join("; ", report.Issues));
            string[] expected = {
                "StoryImageViewer::Awake", "UIEventPanel::Awake", "CompendiumWindow::Awake", "UICharacterStoryBox::Awake",
                "UIReward::Awake", "UICharacterCreatorClassRosterSlot::Awake", "UIPartyCharacterEquippementSlot::Awake",
                "UIEnhancementButtonHighlight::Awake", "UIPartyCharacterEnhancementAbilityCardsDisplay::Awake", "UnityEngine.UI.UITab::OnEnable",
                "FullAbilityCard::OnEnable", "MiniAbilityCard::Awake", "ItemCardUI::OnEnable", "MonsterRoundCardUI::Awake",
                "UIPerkAttackModifier::Awake", "UIScenarioAttackModifier::Awake", "AbilityCardUI::Awake", "AbilityCardUI::OnEnable",
                "UnityEngine.UI.Graphic::OnEnable", "UnityEngine.UI.Graphic::OnDestroy", "UnityEngine.UI.Graphic::OnTransformParentChanged",
                "UnityEngine.UI.Graphic::OnCanvasHierarchyChanged", "UnityEngine.UI.RawImage::set_texture", "UnityEngine.UI.UIWindow::OnEnable",
                "Character3DDisplayManager::Start" };
            var hooks = model.Hooks.Where(h => h.Patch.DeclaringType.FullName == menu + "MenuDepthLifecycleObserver").ToArray();
            check(hooks.Select(h => h.Target.DeclaringType.FullName + "::" + h.Target.Name).SequenceEqual(expected),
                "Metadata-only lifecycle enumeration differs from the exact independent original target roster.");
            check(hooks.Select(h => h.Target).Distinct().Count() == expected.Length && hooks.All(h => h.Kind == "postfix"),
                "Native lifecycle hooks were duplicated or changed phase.");
            MethodDefinition selector = model.Mod.MainModule.GetType(menu + "MenuDepthLifecycleObserver").Methods.Single(m => m.Name == "TargetMethods");
            check(selector.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyTargetMethods"),
                "Canonical selector lost its original explicit Harmony metadata.");
            foreach ((string owner, string name) in new[] { ("MenuDepthCharacters", "CreateHiddenReader"), ("MenuDepthCharacterSource", "CreateOriginalReader") })
            {
                TypeDefinition type = model.Mod.MainModule.GetType(menu + owner);
                MethodDefinition method = type.Methods.Single(m => m.Name == name);
                check(!method.Body.Instructions.Any(i => i.Operand is MethodReference call
                    && (call.DeclaringType.Namespace.StartsWith("System.Reflection.Emit", StringComparison.Ordinal)
                        || call.FullName == "UnityEngine.RuntimePlatform UnityEngine.Application::get_platform()")),
                    "Android accessor retained a desktop emitter or live platform ambiguity.");
                check(method.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == "GetField")
                    && method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldftn), "Original validated FieldInfo closure was replaced by a stub.");
                check(method.Body.Instructions.Last().OpCode == OpCodes.Ret, "Unreachable desktop removal left an invalid trailing fallthrough.");
                foreach (var closure in originalClosures[owner])
                    check(Discovery.AllTypes(model.Mod.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody)
                        .Any(m => m.FullName == closure.Key && NativeMovieBoundary.Fingerprint(m) == closure.Value),
                        "Original readonly field/type/error semantics changed in " + owner);
            }
            check(factory.Body.ExceptionHandlers.Count == handlers && handlers == 1,
                "Hidden-reader failure still needs its original catch-and-retain-native fallback.");
            MethodDefinition directory = model.Mod.MainModule.GetType(menu + "MenuDepthCatalogue").Methods.Single(m => m.Name == "get_DirectoryPath");
            check(directory.Body.Instructions.Select(i => i.OpCode.Code).SequenceEqual(new[] { Code.Call, Code.Ldstr, Code.Ldstr, Code.Call, Code.Ret })
                && directory.Body.Instructions[0].Operand is MethodReference rootGetter
                && rootGetter.FullName == "System.String GloomhavenVR.Core.QuestStandalonePlatform::get_EnvironmentBundleDirectory()"
                && directory.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand)
                    .SequenceEqual(new[] { "MenuDepth", "Story" }), "Loose story lookup lost its configured Quest root or exact content path.");
            check(report.ReflectionRoots.Any(r => r.Type == "Character3DDisplayManager"), "Private native field metadata may be stripped.");
            new Weaver(model).Write(output, report, false);
            check(report.Complete && report.ProtectedTypesVerified == 7 && report.WovenTargets > expected.Length,
                "Actual current-mod weave did not preserve the complete protected game contract.");
            using AssemblyDefinition woven = AssemblyDefinition.ReadAssembly(Path.Combine(output, "GloomhavenVR.dll"));
            check(woven.MainModule.GetType(menu + "MenuDepthLifecycleObserver").Methods.Any(m => m.Name == "TargetMethods"),
                "The actual written AOT mod lost its runtime selector.");
            var wovenHeadMethods = Discovery.AllTypes(woven.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody)
                .ToDictionary(m => m.FullName, NativeMovieBoundary.Fingerprint);
            check(originalHeadMethods.All(pair => wovenHeadMethods.TryGetValue(pair.Key, out string? hash) && hash == pair.Value),
                "AOT conversion changed a complete current menu-depth method body.");
            Console.WriteLine("Current menu-depth method bodies preserved: " + originalHeadMethods.Count);
            XElement links = XElement.Load(Path.Combine(output, "link.xml"));
            check(links.Elements("assembly").Any(a => (string?)a.Attribute("fullname") == "GH.Runtime"
                && a.Elements("type").Any(t => (string?)t.Attribute("fullname") == "Character3DDisplayManager"
                    && (string?)t.Attribute("preserve") == "all")),
                "Written linker metadata lost the exact native readonly-field owner.");
            check(links.Elements("assembly").Any(a => (string?)a.Attribute("fullname") == "GloomhavenVR"
                && (string?)a.Attribute("preserve") == "all"),
                "Written linker metadata lost the new head-raster/AOT closure.");
        }
        Negative("Android platform guard", a => {
            MethodDefinition method = a.MainModule.GetType(menu + "MenuDepthCharacters").Methods.Single(m => m.Name == "CreateHiddenReader");
            method.Body.Instructions.Single(i => i.Operand is MethodReference c && c.Name == "get_platform").Next.Operand = (sbyte)2;
        });
        Negative("native field name", a => {
            MethodDefinition method = a.MainModule.GetType(menu + "MenuDepthCharacterSource").Methods.Single(m => m.Name == "CreateOriginalReader");
            method.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "character3D").Operand = "otherCharacter";
        });
        Negative("original-reader Android platform guard", a => {
            MethodDefinition method = a.MainModule.GetType(menu + "MenuDepthCharacterSource").Methods.Single(m => m.Name == "CreateOriginalReader");
            method.Body.Instructions.Single(i => i.Operand is MethodReference c && c.Name == "get_platform").Next.Operand = (sbyte)2;
        });
        Negative("original-reader closure manager identity", a => {
            MethodDefinition method = a.MainModule.GetType(menu + "MenuDepthCharacterSource").NestedTypes.SelectMany(t => t.Methods)
                .Single(m => m.Name == "<CreateOriginalReader>b__0");
            method.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldarg_1).OpCode = OpCodes.Ldnull;
        });
        Negative("original-reader closure field authority", a => {
            TypeDefinition closure = a.MainModule.GetType(menu + "MenuDepthCharacterSource").NestedTypes
                .Single(t => t.Methods.Any(m => m.Name == "<CreateOriginalReader>b__0"));
            closure.Fields.Single(f => f.Name == "field").IsStatic = true;
        });
        Negative("stale Quest743 original-reader generated closure identity", a => {
            TypeDefinition closure = a.MainModule.GetType(menu + "MenuDepthCharacterSource").NestedTypes
                .Single(t => t.Methods.Any(m => m.Name == "<CreateOriginalReader>b__0"));
            closure.Name = "<>c__DisplayClass85_0";
        });
        Negative("readonly closure semantics", a => {
            MethodDefinition method = a.MainModule.GetType(menu + "MenuDepthCharacters").NestedTypes.SelectMany(t => t.Methods)
                .Single(m => m.Name == "<CreateHiddenReader>b__0");
            method.Body.Instructions.Single(i => i.OpCode == OpCodes.Unbox_Any).Operand = a.MainModule.TypeSystem.Int32;
        });
        Negative("lifecycle target selector", a => {
            MethodDefinition method = a.MainModule.GetType(menu + "MenuDepthLifecycleObserver").NestedTypes.Single().Methods.Single(m => m.Name == "MoveNext");
            method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "OnDestroy").Operand = "OnDisable";
        });
        Negative("native owner descriptor", a => {
            MethodDefinition method = a.MainModule.GetType(menu + "MenuDepthDriver").Methods.Single(m => m.Name == ".cctor");
            method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "UIReward").Operand = "UIOtherReward";
        });
        Negative("ambiguous lifecycle selector", a => {
            TypeDefinition observer = a.MainModule.GetType(menu + "MenuDepthLifecycleObserver");
            MethodDefinition selector = observer.Methods.Single(m => m.Name == "Targets");
            var added = new MethodDefinition("TargetMethods", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static, selector.ReturnType);
            added.Body.GetILProcessor().Emit(OpCodes.Ldnull); added.Body.GetILProcessor().Emit(OpCodes.Ret);
            observer.Methods.Add(added);
        });
        Negative("native card descriptor", a => {
            MethodDefinition method = a.MainModule.GetType(menu + "MenuDepthCards").Methods.Single(m => m.Name == ".cctor");
            method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "ItemCardUI").Operand = "UnknownCardUI";
        });
        Negative("story directory", a => {
            MethodDefinition method = a.MainModule.GetType(menu + "MenuDepthCatalogue").Methods.Single(m => m.Name == "get_DirectoryPath");
            method.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "Story").Operand = "Movies";
        });
        Negative("original bool field type", a => NativeManager(a).Fields.Single(f => f.Name == "isHidden").FieldType = a.MainModule.TypeSystem.String);
        Negative("original reference field authority", a => NativeManager(a).Fields.Single(f => f.Name == "character3D").IsStatic = true);
        foreach (var input in immutable) check(Hash(input.Key) == input.Value, "Metadata-only conversion wrote an immutable original input: " + input.Key);

        TypeDefinition NativeManager(AssemblyDefinition a) => a.MainModule.GetType(menu + "MenuDepthCharacters").Methods.Single(m => m.Name == "CreateHiddenReader")
            .Body.Instructions.Where(i => i.OpCode == OpCodes.Ldtoken).Select(i => i.Operand).OfType<TypeReference>()
            .First(t => t.FullName == "Character3DDisplayManager").Resolve();
        void Negative(string label, Action<AssemblyDefinition> mutate)
        {
            using Discovery model = Discovery.Load(modPath, managed);
            mutate(model.Mod);
            AuditReport report = model.Audit();
            check(report.Issues.Any(i => i.Code == "CURRENT_MOD_AOT_UNSUPPORTED") && !report.Complete,
                "Changed " + label + " did not fail closed.");
            Console.WriteLine("Rejected causal menu-depth control: " + label);
        }
    }
}
