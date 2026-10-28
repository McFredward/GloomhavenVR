using System;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Keep the native level-up hover's pooled card in its owning window from its first draw.</summary>
[HarmonyPatch(typeof(UILevelUpCardHolder), nameof(UILevelUpCardHolder.PlaceNewCard))]
internal static class MenuDepthLevelUpHoverObserver
{
    private static int _failures;

    [HarmonyPostfix]
    private static void AfterPlace(UILevelUpCardHolder __instance, bool immediately)
    {
        if (!immediately) return; // Keep the original won-card reveal/fade path intact.
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
            // intent synchronously after the original show, before any Canvas/head
            // batch. Keep the original pool, fade, callback, XY and scale untouched.
            card.localRotation = Quaternion.identity;
            return;
        }
    }
}
