using System;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Compat;

/// <summary>
/// Adapted from gummyboars' Gloomhaven Bug Fixes v5.0.0 (QuestItem.cs/Dopple.cs).
/// These patches repair the native item bar's local context, never the item, action,
/// protocol version, inventory contents or rule-library implementation. Native
/// replay and the existing VR adoption/observer paths still run unmodified.
/// </summary>
internal static class NativeItemUiRepair
{
    private static bool _reported;

    internal static void Report(Exception error)
    {
        if (_reported)
            return;
        _reported = true;
        try
        {
            VRLog.Warn("NativeItemUiRepair", $"Item UI context repair skipped: {error.GetType().Name}. Native handling retained.");
        }
        catch (Exception)
        {
            // Diagnostics must not introduce another exception into native replay.
        }
    }
}

/// <summary>
/// The shipped ShowUsableItems evaluates Any(IsItemInteractable) before assigning
/// actor. Campaign quest items read actor.IsUnderMyControl in that predicate:
/// first presentation crashes, and a previously viewed actor gives the wrong
/// ownership answer. Assign exactly the supplied valid owner before the predicate.
/// The original method already assigns this same field on both result branches.
/// </summary>
[HarmonyPatch(typeof(UIUseItemsBar), nameof(UIUseItemsBar.ShowUsableItems))]
internal static class NativeBugFixes_ShowUsableItemsActor
{
    private static void Prefix(CActor? inventoryOwner, ref CActor? ___actor)
    {
        if (!VRSession.IsRunning)
            return;
        try
        {
            if (inventoryOwner?.Inventory != null)
                ___actor = inventoryOwner;
        }
        catch (Exception error)
        {
            NativeItemUiRepair.Report(error);
        }
    }
}

/// <summary>
/// The hidden-bar replay uses its last actor without consulting action.ActorID.
/// Bind the existing native action to its exact native player only when that
/// player owns the token's item. This also covers a stale non-null bar, unlike
/// upstream's null-only repair. Visible original slots retain their own replay.
/// Do not substitute a summon with its summoner: that changes ToggleItem's
/// inventory owner and cannot be assumed equivalent for an unmodded recipient.
/// Missing/invalid input and failed resolution leave native handling untouched.
/// </summary>
[HarmonyPatch(typeof(UIUseItemsBar), nameof(UIUseItemsBar.ProxyUseItemBonus), new[] { typeof(GameAction) })]
internal static class NativeBugFixes_ItemReplayActor
{
    // Match the transport/quiet-controller read seam without adding a compile-time
    // Photon Bolt protocol dependency to this local UI repair.
    private static readonly PropertyInfo? SupplementaryToken = AccessTools.Property(typeof(GameAction), "SupplementaryDataToken");

    private static void Prefix(GameAction? action, bool ___isShown, ref CActor? ___actor)
    {
        if (!VRSession.IsRunning || ___isShown)
            return;
        try
        {
            if (action == null || SupplementaryToken?.GetValue(action) is not ItemToken token
                || token.ChosenElement == null || token.InfusionElements == null
                || action.ActionTypeID != (int)GameActionType.UseItem
                    && action.ActionTypeID != (int)GameActionType.ClickItemBonusSlot
                || Choreographer.s_Choreographer == null)
                return;

            CActor? owner = Choreographer.s_Choreographer.FindPlayerActor(action.ActorID);
            if (owner?.Inventory == null
                || !owner.Inventory.AllItems.Exists(item => item.NetworkID == token.ItemNetworkID))
                return;

            ___actor = owner;
        }
        catch (Exception error)
        {
            NativeItemUiRepair.Report(error);
        }
    }
}
