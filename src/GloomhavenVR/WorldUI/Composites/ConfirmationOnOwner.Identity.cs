using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Observe accepted native identity writes, not selection attempts. In particular,
/// AdventurePartyAssembly.SelectCharacter has native authority/rejection returns
/// before selectedCharacter changes. A prefix on that attempt would decline a
/// still-valid prompt even though no character switch was allowed.
/// </summary>
[HarmonyPatch]
internal static class ConfirmationIdentityWritePatch
{
    private static bool LevelUpEntry(CodeInstruction instruction) => instruction.opcode == OpCodes.Callvirt
        && instruction.operand is MethodInfo method && method.DeclaringType?.Name == "UILevelUpWindow" && method.Name == "Show";
    private static bool InventoryClose(CodeInstruction instruction) => instruction.opcode == OpCodes.Callvirt
        && instruction.operand is MethodInfo method && method.DeclaringType?.Name == "UIPartyItemInventoryDisplay" && method.Name == "Hide";
    private static bool IdentityWrite(CodeInstruction instruction)
    {
        if (instruction.opcode != OpCodes.Stfld || instruction.operand is not FieldInfo field) return false;
        string type = field.DeclaringType!.Name;
        return (type == "NewPartyDisplayUI" || type == "UIAdventurePartyAssemblyWindow") && field.Name == "selectedCharacter"
            || type == "NewPartyCharacterUI" && field.Name == "characterData"
            || (type == "UIPerksInventory" || type == "UIResetLevelUpWindow") && field.Name == "characterData"
            || (type == "UILevelUpWindow" || type == "UIShopItemInventory" || type == "UITempleWindow") && field.Name == "character"
            || type == "UIPartyItemInventoryDisplay" && (field.Name == "character" || field.Name == "slotType" || field.Name == "slotNum")
            || type == "UINewEnhancementWindow" && (field.Name == "character" || field.Name == "selectedCard" || field.Name == "mode")
            || type == "UIMultiplayerMerchantSlot" && field.Name == "player"
            || type == "UIMultiplayerSelectPlayerScreen" && (field.Name == "currentSelection" || field.Name == "characterID" || field.Name == "characterName" || field.Name == "m_CurrentSlotIndex");
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var entry in new[]
        {
            ("NewPartyDisplayUI", new[] { "SelectCurrentCharacter" }),
            ("NewPartyDisplayUI", new[] { "OnLevelUpSelected" }),
            ("NewPartyCharacterUI", new[] { "Init", "InitAvailable" }),
            ("UIPerksInventory", new[] { "Display" }),
            ("UIPartyItemInventoryDisplay", new[] { "Display" }),
            ("UIPartyCharacterEquipmentDisplay", new[] { "OnItemSelect" }),
            ("UIShopItemInventory", new[] { "Init", "RefreshView" }),
            ("UITempleWindow", new[] { "EnterTemple", "OnSelectedCharacter" }),
            ("UINewEnhancementWindow", new[] { "OnSelectedCharacter", "OnSelectedCardToEnhance", "ShowBuyOptions", "ShowSellOptions" }),
            ("UIResetLevelUpWindow", new[] { "Show" }),
            ("UILevelUpWindow", new[] { "Show" }),
            ("UIAdventurePartyAssemblyWindow", new[] { "Show", "set_SelectedCharacter", "SelectCharacter", "RemoveCharacterFromParty" }),
            ("UIMultiplayerMerchantSlot", new[] { "AssignTo" }),
            ("UIMultiplayerSelectPlayerScreen", new[] { "Show", "OnSelectedPlayer", "SetSelected" })
        })
            foreach (MethodBase method in ConfirmationNavigationCatalog.Methods(entry.Item1, entry.Item2))
                foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
                    if (IdentityWrite(instruction) || LevelUpEntry(instruction) || InventoryClose(instruction)) { yield return method; break; }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        MethodInfo notify = AccessTools.DeclaredMethod(typeof(ConfirmationOnOwner), "BeforeIdentityWrite");
        foreach (CodeInstruction instruction in instructions)
        {
            if (InventoryClose(instruction))
            {
                var first = new CodeInstruction(OpCodes.Ldarg_0);
                first.labels.AddRange(instruction.labels); instruction.labels.Clear();
                first.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                yield return first;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ConfirmationOnOwner), "BeforeInventoryClose"));
            }
            if (LevelUpEntry(instruction))
            {
                var first = new CodeInstruction(OpCodes.Ldarg_0);
                first.labels.AddRange(instruction.labels); instruction.labels.Clear();
                first.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                yield return first;
                yield return new CodeInstruction(OpCodes.Ldstr, "LEVELUP");
                yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ConfirmationOnOwner), "BeforePartyPage"));
            }
            if (IdentityWrite(instruction))
            {
                var field = (FieldInfo)instruction.operand;
                LocalBuilder value = generator.DeclareLocal(field.FieldType);
                LocalBuilder owner = generator.DeclareLocal(field.DeclaringType!);
                var first = new CodeInstruction(OpCodes.Stloc, value);
                first.labels.AddRange(instruction.labels); instruction.labels.Clear();
                first.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                yield return first;
                yield return new CodeInstruction(OpCodes.Stloc, owner);
                yield return new CodeInstruction(OpCodes.Ldloc, owner);
                yield return new CodeInstruction(OpCodes.Ldloc, value);
                if (field.FieldType.IsValueType) yield return new CodeInstruction(OpCodes.Box, field.FieldType);
                yield return new CodeInstruction(OpCodes.Ldstr, field.Name);
                yield return new CodeInstruction(OpCodes.Call, notify);
                yield return new CodeInstruction(OpCodes.Ldloc, owner);
                yield return new CodeInstruction(OpCodes.Ldloc, value);
            }
            yield return instruction;
        }
    }
}
