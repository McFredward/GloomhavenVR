using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace QuestWeaver;

/// <summary>Compile the current confirmation transpilers into their exact original
/// native sites. Qualification covers complete selectors, iterators, predicates,
/// locals, exception regions and the original method roster before any mutation.
/// Native permission branches and writes keep their original order and authority.</summary>
internal static class NativeConfirmationBoundary
{
    const string Prefix = "GloomhavenVR.WorldUI.";
    const string Identity = "ConfirmationIdentityWritePatch", Deferred = "ConfirmationDeferredCapturePatch";
    const string IdentityContract = "59a9c08a0467959b52a685616020ef919320b51cbbd165a05efbd25a547cbcb8";
    const string DeferredContract = "9767ca17693918764494633e765609774918ab2cdcdde89c70ae82a7ad0b23af";
    const string CatalogueContract = "829f664325d81b5f9bb284cc9af900fbf7510f6a93e61f8c534356c6c54094bb";
    const string NativeContract = "c186da03aa0f88ea4c22926223e2985b42ea853f08b1c53e3cace68be4b40560";

    static readonly (string Type, string[] Names)[] IdentityCandidates = {
        ("NewPartyDisplayUI", new[] { "SelectCurrentCharacter", "OnLevelUpSelected" }),
        ("NewPartyCharacterUI", new[] { "Init", "InitAvailable" }),
        ("UIPerksInventory", new[] { "Display" }), ("UIPartyItemInventoryDisplay", new[] { "Display" }),
        ("UIPartyCharacterEquipmentDisplay", new[] { "OnItemSelect" }),
        ("UIShopItemInventory", new[] { "Init", "RefreshView" }), ("UITempleWindow", new[] { "EnterTemple", "OnSelectedCharacter" }),
        ("UINewEnhancementWindow", new[] { "OnSelectedCharacter", "OnSelectedCardToEnhance", "ShowBuyOptions", "ShowSellOptions" }),
        ("UIResetLevelUpWindow", new[] { "Show" }), ("UILevelUpWindow", new[] { "Show" }),
        ("UIAdventurePartyAssemblyWindow", new[] { "Show", "set_SelectedCharacter", "SelectCharacter", "RemoveCharacterFromParty" }),
        ("UIMultiplayerMerchantSlot", new[] { "AssignTo" }),
        ("UIMultiplayerSelectPlayerScreen", new[] { "Show", "OnSelectedPlayer", "SetSelected" }) };
    static readonly (string Type, string[] Names)[] DeferredCandidates = {
        ("UIRulesetsWindow", new[] { "CompileRuleset" }), ("UIListModsView", new[] { "ValidateMod", "OpenLevelEditor" }),
        ("UIRulesetView", new[] { "ValidateMod" }) };

    internal static void Apply(Discovery model, AuditReport report, Func<string, TypeDefinition?> find,
        Action<AssemblyDefinition> track)
    {
        ModuleDefinition module = model.Mod.MainModule;
        TypeDefinition? identity = module.GetType(Prefix + Identity), deferred = module.GetType(Prefix + Deferred);
        if (identity == null && deferred == null) return;
        if (identity == null || deferred == null) throw new InvalidDataException("Both native confirmation transpilers are required.");
        foreach (var (patch, expected) in new[] { (identity, IdentityContract), (deferred, DeferredContract) })
        {
            RequireContract(patch, expected);
            CustomAttribute[] attributes = patch.CustomAttributes.Where(a => a.AttributeType.Namespace == "HarmonyLib").ToArray();
            if (!patch.IsAbstract || !patch.IsSealed || patch.Fields.Count != 0 || attributes.Length != 1
                || attributes[0].AttributeType.FullName != "HarmonyLib.HarmonyPatch" || attributes[0].ConstructorArguments.Count != 0)
                throw new InvalidDataException("Native confirmation patch registration changed: " + patch.FullName);
            HashSet<TypeDefinition> removed = Flat(patch).ToHashSet();
            if (Discovery.AllTypes(module).Where(t => !removed.Contains(t)).SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MemberReference member
                    && removed.Any(t => t.FullName == member.DeclaringType?.FullName)))
                throw new InvalidDataException("Native confirmation transpiler acquired an independent caller.");
        }
        RequireContract(module.GetType(Prefix + "ConfirmationNavigationCatalog"), CatalogueContract);
        MethodDefinition[] Resolve((string Type, string[] Names)[] specs) => specs.SelectMany(spec =>
            (find(spec.Type) ?? throw new InvalidDataException("Native confirmation owner absent: " + spec.Type)).Methods
                .Where(m => !m.IsStatic && spec.Names.Contains(m.Name, StringComparer.Ordinal))).ToArray();
        MethodDefinition[] identityCandidates = Resolve(IdentityCandidates), deferredCandidates = Resolve(DeferredCandidates);
        MethodDefinition[] candidates = identityCandidates.Concat(deferredCandidates).ToArray();
        if (candidates.Any(m => !m.HasBody || m.HasGenericParameters || Discovery.Protected(m.DeclaringType)
            || m.Module.Assembly.Name.Name != "GH.Runtime")) throw new InvalidDataException("Native confirmation candidate contract changed.");
        string actual = Hash(string.Join("\n", candidates.Select(NativeMovieBoundary.Fingerprint)));
        if (actual != NativeContract) throw new InvalidDataException("Native confirmation original method roster/bodies changed; sha256=" + actual);

        var callbacks = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);
        TypeDefinition owner = module.GetType(Prefix + "ConfirmationOnOwner");
        foreach (var spec in new[] {
            ("BeforeIdentityWrite", new[] { "UnityEngine.Component", "System.Object", "System.String" }),
            ("BeforeInventoryClose", new[] { "UnityEngine.Component" }),
            ("BeforePartyPage", new[] { "UnityEngine.Component", "System.String", "System.Boolean" }),
            ("CaptureDeferredOrigin", new[] { "System.Object", "UnityEngine.Component" }) })
        {
            MethodDefinition callback = owner.Methods.Single(m => m.Name == spec.Item1);
            if (!callback.IsStatic || callback.ReturnType.FullName != "System.Void"
                || !callback.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(spec.Item2))
                throw new InvalidDataException("Native confirmation callback ABI changed: " + spec.Item1);
            callbacks.Add(spec.Item1, callback);
        }
        var prepared = new[] { (Patch: identity, Targets: identityCandidates.Where(m => m.Body.Instructions.Any(IsIdentitySite)).ToArray(), Deferred: false),
            (Patch: deferred, Targets: deferredCandidates, Deferred: true) };
        foreach (var entry in prepared)
        {
            if (entry.Targets.Length == 0) throw new InvalidDataException("Native confirmation substitution has no qualified target.");
            foreach (MethodDefinition target in entry.Targets)
            {
                if (entry.Deferred && !target.Body.Instructions.Any(IsDeferredSite))
                    throw new InvalidDataException("Native deferred origin no longer captures an original component: " + target.FullName);
                foreach (Instruction instruction in target.Body.Instructions.Where(i => entry.Deferred ? IsDeferredSite(i) : IsIdentitySite(i)))
                {
                    if (instruction.Operand is FieldReference field && (field.Resolve().IsStatic || !IsComponent(field.DeclaringType)))
                    {
                        // Deferred stores belong to compiler display classes; their
                        // value, rather than their container, is the Component.
                        if (!entry.Deferred || field.Resolve().IsStatic || !IsComponent(field.FieldType))
                            throw new InvalidDataException("Native confirmation field authority changed: " + field.FullName);
                    }
                    if (instruction.Operand is MethodReference call && call.Resolve().IsStatic)
                        throw new InvalidDataException("Native confirmation receiver changed: " + call.FullName);
                }
            }
        }
        // Only this fully qualified pair can produce multi-target static proof.
        foreach (MethodDefinition callback in callbacks.Values) callback.IsPublic = true;
        foreach (var entry in prepared)
        {
            string removed = Discovery.MethodKey(entry.Patch.Methods.Single(m => m.Name == "Transpiler"));
            var sites = new List<StaticPatchSite>();
            foreach (MethodDefinition target in entry.Targets)
            {
                Rewrite(target, entry.Deferred, callbacks);
                foreach (MethodDefinition callback in callbacks.Values)
                {
                    int calls = target.Body.Instructions.Count(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference c
                        && c.FullName == callback.FullName);
                    if (calls > 0) sites.Add(new StaticPatchSite(target, callback, calls));
                }
                track(target.Module.Assembly);
            }
            entry.Patch.Methods.Clear(); entry.Patch.NestedTypes.Clear();
            foreach (CustomAttribute attribute in entry.Patch.CustomAttributes.Where(a => a.AttributeType.Namespace == "HarmonyLib").ToArray())
                entry.Patch.CustomAttributes.Remove(attribute);
            StaticPatchSite first = sites[0];
            model.StaticPatches.Add(new StaticPatchDefinition(entry.Patch, first.Target, removed, first.Replacement,
                first.Calls, sites.Skip(1)));
        }
        report.StaticSubstitutions.Add("Native confirmations: exact accepted identity writes and level-up/inventory transitions retain native branches; deferred owner capture follows each original closure store. Complete desktop selectors/transpilers are removed with bound multi-target registration proof; no runtime emitter remains.");
    }

    internal static void Rewrite(MethodDefinition target, bool deferred, IReadOnlyDictionary<string, MethodDefinition> callbacks)
    {
        ILProcessor il = target.Body.GetILProcessor();
        foreach (Instruction site in target.Body.Instructions.ToArray())
        {
            var before = new List<Instruction>(); var after = new List<Instruction>();
            Instruction Call(string name) => Instruction.Create(OpCodes.Call, target.Module.ImportReference(callbacks[name]));
            if (deferred && IsDeferredSite(site))
            {
                var field = (FieldReference)site.Operand;
                VariableDefinition owner = Local(field.FieldType), callback = Local(field.DeclaringType);
                before.AddRange(new[] { Instruction.Create(OpCodes.Stloc, owner), Instruction.Create(OpCodes.Stloc, callback),
                    Instruction.Create(OpCodes.Ldloc, callback), Instruction.Create(OpCodes.Ldloc, owner) });
                after.AddRange(new[] { Instruction.Create(OpCodes.Ldloc, callback), Instruction.Create(OpCodes.Ldloc, owner), Call("CaptureDeferredOrigin") });
            }
            else if (!deferred && site.OpCode == OpCodes.Callvirt && site.Operand is MethodReference call)
            {
                if (call.DeclaringType.Name == "UIPartyItemInventoryDisplay" && call.Name == "Hide")
                    before.AddRange(new[] { Instruction.Create(OpCodes.Ldarg_0), Call("BeforeInventoryClose") });
                if (call.DeclaringType.Name == "UILevelUpWindow" && call.Name == "Show")
                    before.AddRange(new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldstr, "LEVELUP"),
                        Instruction.Create(OpCodes.Ldc_I4_1), Call("BeforePartyPage") });
            }
            else if (!deferred && IsIdentityWrite(site))
            {
                var field = (FieldReference)site.Operand;
                VariableDefinition value = Local(field.FieldType), owner = Local(field.DeclaringType);
                before.AddRange(new[] { Instruction.Create(OpCodes.Stloc, value), Instruction.Create(OpCodes.Stloc, owner),
                    Instruction.Create(OpCodes.Ldloc, owner), Instruction.Create(OpCodes.Ldloc, value) });
                if (field.FieldType.IsValueType) before.Add(Instruction.Create(OpCodes.Box, target.Module.ImportReference(field.FieldType)));
                before.AddRange(new[] { Instruction.Create(OpCodes.Ldstr, field.Name), Call("BeforeIdentityWrite"),
                    Instruction.Create(OpCodes.Ldloc, owner), Instruction.Create(OpCodes.Ldloc, value) });
            }
            if (before.Count == 0) continue;
            foreach (Instruction instruction in before) il.InsertBefore(site, instruction);
            Redirect(target, site, before[0]);
            Instruction previous = site;
            foreach (Instruction instruction in after) { il.InsertAfter(previous, instruction); previous = instruction; }
        }
        target.Body.MaxStackSize += 3;
        target.Body.SimplifyMacros(); target.Body.OptimizeMacros();
        VariableDefinition Local(TypeReference type) { var variable = new VariableDefinition(target.Module.ImportReference(type)); target.Body.Variables.Add(variable); return variable; }
    }

    static bool IsIdentitySite(Instruction i) => IsIdentityWrite(i) || i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference c
        && (c.DeclaringType.Name == "UILevelUpWindow" && c.Name == "Show" || c.DeclaringType.Name == "UIPartyItemInventoryDisplay" && c.Name == "Hide");
    static bool IsIdentityWrite(Instruction i)
    {
        if (i.OpCode != OpCodes.Stfld || i.Operand is not FieldReference f) return false;
        return f.DeclaringType.Name switch {
            "NewPartyDisplayUI" or "UIAdventurePartyAssemblyWindow" => f.Name == "selectedCharacter",
            "NewPartyCharacterUI" or "UIPerksInventory" or "UIResetLevelUpWindow" => f.Name == "characterData",
            "UILevelUpWindow" or "UIShopItemInventory" or "UITempleWindow" => f.Name == "character",
            "UIPartyItemInventoryDisplay" => f.Name is "character" or "slotType" or "slotNum",
            "UINewEnhancementWindow" => f.Name is "character" or "selectedCard" or "mode",
            "UIMultiplayerMerchantSlot" => f.Name == "player",
            "UIMultiplayerSelectPlayerScreen" => f.Name is "currentSelection" or "characterID" or "characterName" or "m_CurrentSlotIndex",
            _ => false };
    }
    static bool IsDeferredSite(Instruction i) => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f
        && f.Name == "<>4__this" && IsComponent(f.FieldType);
    static bool IsComponent(TypeReference reference)
    {
        for (TypeDefinition? type = reference.Resolve(); type != null; type = type.BaseType?.Resolve())
            if (type.FullName == "UnityEngine.Component") return true;
        return false;
    }
    static void Redirect(MethodDefinition method, Instruction old, Instruction first)
    {
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (instruction.Operand == old) instruction.Operand = first;
            if (instruction.Operand is Instruction[] branches)
                for (int i = 0; i < branches.Length; i++) if (branches[i] == old) branches[i] = first;
        }
        foreach (ExceptionHandler h in method.Body.ExceptionHandlers)
        {
            if (h.TryStart == old) h.TryStart = first; if (h.TryEnd == old) h.TryEnd = first;
            if (h.HandlerStart == old) h.HandlerStart = first; if (h.HandlerEnd == old) h.HandlerEnd = first;
            if (h.FilterStart == old) h.FilterStart = first;
        }
    }
    static IEnumerable<TypeDefinition> Flat(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Flat));
    static string Contract(TypeDefinition type) => string.Join("\n", Flat(type).SelectMany(t => new[] { t.FullName + ":" + t.Attributes }
        .Concat(t.Fields.Select(f => f.FullName + ":" + f.Attributes)).Concat(t.Methods.Select(m => m.FullName + ":" + m.Attributes
            + ":" + string.Join(",", m.CustomAttributes.Select(a => a.AttributeType.FullName)) + ":" + (m.HasBody ? NativeMovieBoundary.Fingerprint(m) : "")))));
    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    static void RequireContract(TypeDefinition type, string expected)
    {
        string actual = Hash(Contract(type));
        if (actual != expected) throw new InvalidDataException("Native confirmation selector/transpiler changed: " + type.FullName + "; sha256=" + actual);
    }
}
