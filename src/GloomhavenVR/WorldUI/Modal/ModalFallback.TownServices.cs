using System;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    /// <summary>Return the intact native controller to the ordinary window path. Unlike a
    /// section handoff, this retains the normal fitting, placement and opening lifecycle.</summary>
    internal static void RestoreClassicTownService(UIWindow window)
    {
        if (window != null && window.IsOpen) TryConvertWindow(window);
    }

    /// <summary>End the previous conversion before any descendant acquires a new owner.
    /// A deferred rollback must finish before a section can record its native home.</summary>
    internal static bool ReleaseForTownService(UIWindow window, ConvertedPanel previous)
    {
        if (!ReleaseForComposite(window)) return false;
        if (previous.Target == null) return false;
        if (previous.HostGo != null && previous.Target.IsChildOf(previous.HostGo.transform)) return false;
        if (previous.Target.parent != previous.OriginalParent) return false;
        foreach (ConvertedPanel panel in CanvasConversion.ActivePanels)
            if (panel.Target == previous.Target) return false;
        return true;
    }

    /// <summary>Re-enroll the original context after its independent sections have moved. Its
    /// previous pose remains authoritative; this is a composition handoff, not another opening.</summary>
    internal static ConvertedPanel? RestoreTownServiceContext(UIWindow window, Vector3 position, Quaternion rotation)
    {
        if (window == null || !window.IsOpen) return null;
        if (!TryConvertWindow(window)) return null;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!ReferenceEquals(wp.Window, window)) continue;
            wp.Grab?.SnapFrameTo(position, rotation);
            wp.SpawnAnchor = default;
            wp.PoseRePlaceDone = true;
            wp.PoseRePlacedAtFit = wp.Panel.FitAppliedGeneration;
            wp.ReflowCancelled = true;
            return wp.Panel;
        }
        return null;
    }
}

/// <summary>Suppress the hidden flat enchantment list's own tab sound while its physical
/// replacement is active. This patch changes one serialized string only for the duration of
/// the original Display call; flat mode, purchase feedback and NPC foley retain their audio.</summary>
internal static class TownServiceNativeAudioSilence
{
    private static bool _installed;
    internal static void EnsureInstalled()
    {
        if (_installed || VRSession.Harmony == null) return;
        var target = AccessTools.Method(typeof(UIPartyCharacterEnhancementAbilityCardsDisplay),
            nameof(UIPartyCharacterEnhancementAbilityCardsDisplay.Display));
        if (target == null) return;
        VRSession.Harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(BeforeDisplay)),
            finalizer: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(AfterDisplay)));
        _installed = true;
    }

    private static void BeforeDisplay(UIPartyCharacterEnhancementAbilityCardsDisplay __instance,
        ref string? __state)
    {
        __state = null;
        if (!MapRoom.MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value) return;
        __state = __instance.audioItemShow;
        __instance.audioItemShow = string.Empty;
    }

    private static Exception? AfterDisplay(UIPartyCharacterEnhancementAbilityCardsDisplay __instance,
        string? __state, Exception? __exception)
    {
        if (__state != null) __instance.audioItemShow = __state;
        return __exception;
    }
}
