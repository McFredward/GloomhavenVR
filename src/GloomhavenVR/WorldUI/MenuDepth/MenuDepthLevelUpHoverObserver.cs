using System;
using GloomhavenVR.Core;
using HarmonyLib;
using Script.GUI.SMNavigation;
using Script.GUI.SMNavigation.States.CampaignMapStates;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Keep native hover and won-card presentations in their owning window from the first draw.</summary>
[HarmonyPatch(typeof(UILevelUpCardHolder), nameof(UILevelUpCardHolder.PlaceNewCard))]
internal static class MenuDepthLevelUpHoverObserver
{
    private static int _failures;

    [HarmonyPostfix]
    private static void AfterPlace(UILevelUpCardHolder __instance, bool immediately)
    {
        // Build745: both native paths synchronously spawn and show the same
        // pooled card. "immediately" changes only its CanvasGroup alpha/fade;
        // the won-card introduction needs the same owner-local orientation
        // before the fade's first visible frame. Leave that original tween,
        // highlight effect and deferred continuation entirely native.
        try { AlignOriginal(__instance); }
        catch (Exception exception)
        {
            int count = ++_failures;
            if (count <= 3 || (count & (count - 1)) == 0)
                VRLog.Note("WorldUI", "LEVEL-UP HOVER orientation could not settle the original card: "
                    + exception.GetType().Name + "; occurrence=" + count + ".");
        }
    }

    internal static void AlignOriginal(UILevelUpCardHolder holder)
    {
        if (!VRSession.IsRunning || !WorldUIConfig.ConversionActive || FlatScreen.ManualScreenActive
            || holder == null || holder.Card == null) return;
        Transform card = holder.Card.transform;
        if (card.parent != holder.transform) return;
        for (int i = 0; i < CanvasConversion.ActivePanels.Count; i++)
        {
            ConvertedPanel panel = CanvasConversion.ActivePanels[i];
            if (!panel.IsAlive || panel.Target == null || !panel.Target.gameObject.activeInHierarchy
                || !holder.transform.IsChildOf(panel.Target)) continue;
            // Build738: HighlightCard uses PlaceNewCard(immediately:true). Its
            // original SpawnCard resets local position/scale but preserves world
            // rotation when reparenting out of the pool. A yawed VR host therefore
            // receives an inverse local yaw until the budgeted flatten walk finds
            // it. The native recycle path explicitly resets this same root to
            // identity, which is the flat UI's authored orientation. Restore that
            // intent for hover and won-card reveal after the original show, before any Canvas/head
            // batch. Keep the original pool, fade, callback, XY and scale untouched.
            card.localRotation = Quaternion.identity;
            return;
        }
    }
}

/// <summary>Keep the native locked/browse inventory preview outside its scrolling list's clipper.</summary>
[HarmonyPatch(typeof(UILevelUpCard), nameof(UILevelUpCard.MouseEnter))]
internal static class MenuDepthFurtherAbilityHoverObserver
{
    private static int _failures;

    [HarmonyPostfix]
    private static void AfterMouseEnter(UILevelUpCard __instance, AbilityCardUI? ___abilityCard,
        bool ___highlighted, Vector3 ____fullCardPrewiewOffset)
    {
        try { MenuDepthFurtherAbilityHover.SettleOriginal(__instance, ___abilityCard, ___highlighted, ____fullCardPrewiewOffset); }
        catch (Exception exception)
        {
            int count = ++_failures;
            if (count <= 3 || (count & (count - 1)) == 0)
                VRLog.Note("WorldUI", "FURTHER CARD HOVER could not settle the original preview: "
                    + exception.GetType().Name + "; occurrence=" + count + ".");
        }
    }

}

// Keep presentation helpers outside the Harmony container: its analyzer treats
// reads of Vector3 parameter members as writes to non-ref patch parameters.
internal static class MenuDepthFurtherAbilityHover
{
    internal static void SettleOriginal(UILevelUpCard entry, AbilityCardUI? card, bool highlighted,
        Vector3 nativeOffset)
    {
        if (FlatScreen.ManualScreenActive || !VRSession.IsRunning || !WorldUIConfig.ConversionActive
            || entry == null || !entry.gameObject.activeInHierarchy || !highlighted
            || card == null || entry.Selectable == null) return;
        // The original method decides whether this row requests a preview. In
        // particular, an ordinary selectable level-up row only highlights the
        // shared reveal holder; it must not acquire a second preview here.
        if (entry.Selectable.interactable && !InputManager.GamePadInUse) return;
        var navigation = Singleton<UINavigation>.Instance;
        if (navigation == null || navigation.StateMachine == null
            || navigation.StateMachine.CurrentState
                == navigation.StateMachine.GetState(CampaignMapStateTag.ShowingCardLevelUp)) return;
        FullAbilityCard full = card.fullAbilityCard;
        if (full == null
            || !full.gameObject.activeInHierarchy || card.MiniAbilityCard == null) return;
        ConvertedPanel? owner = ModalFallback.FindOwningWindow(card.transform);
        if (owner == null || !owner.IsAlive || owner.HostRect == null
            || !ReferenceEquals(ModalFallback.FindOwningWindow(full.transform), owner)) return;

        // Build747: this native path deliberately skips ChangeFullCardPosition,
        // then writes the Full position again AFTER ToggleFullCardPreview. Its
        // new Canvas initially escapes the inventory's RectMask2D, but adoption
        // clears that flag on the next sweep and cuts the card to the list.
        // Settle after the final native writer, using the same original Mini
        // anchor/offset in the window's pixel basis. Existing tooltip ownership
        // raises the original Full, preserves scale and ordering, and hands it
        // back to this pooled AbilityCardUI on the ordinary native hide edge.
        Vector3 miniOffset = owner.HostRect.InverseTransformVector(
            card.MiniAbilityCard.transform.position - card.transform.position);
        float offsetX = nativeOffset.x;
        float offsetY = nativeOffset.y;
        const string kind = "FullAbilityCard (Further Ability Cards hover preview)";
        // SpawnCard can preserve the pooled row's world rotation. Establish the
        // Full's existing owner plane before PlaceFullCardPreview drops local Z;
        // doing that while it still inherits the tilted row loses an in-plane
        // component. Only the original preview moves; the native row stays owned
        // by its inventory and keeps its original pool/input lifetime.
        TooltipOnWindow.Settle(full, kind, card);
        TooltipOnWindow.PlaceFullCardPreview(card, full,
            miniOffset.x + offsetX, miniOffset.y + offsetY,
            kind);
    }
}
