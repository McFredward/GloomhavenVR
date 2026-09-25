using System;
using System.Runtime.CompilerServices;
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

/// <summary>Suppress the hidden flat town UI's show/hide sounds while its physical replacement
/// is active. The native controllers and their callbacks still run; only the exact serialized
/// audio field read by that call is blank for the call's duration. Flat mode, confirmation and
/// purchase feedback, resident speech and physical cabinet/card sounds remain untouched.</summary>
internal static class TownServiceNativeAudioSilence
{
    private sealed class ImmersiveOpenMarker { }

    private static bool _installed;
    private static readonly ConditionalWeakTable<UIWindow, ImmersiveOpenMarker> ImmersiveOpenWindows = new();
    internal static void EnsureInstalled()
    {
        if (_installed || VRSession.Harmony == null) return;
        var target = AccessTools.Method(typeof(UIPartyCharacterEnhancementAbilityCardsDisplay),
            nameof(UIPartyCharacterEnhancementAbilityCardsDisplay.Display));
        var show = AccessTools.Method(typeof(UIWindow), nameof(UIWindow.Show), new[] { typeof(bool) });
        var hide = AccessTools.Method(typeof(UIWindow), nameof(UIWindow.Hide), new[] { typeof(bool) });
        if (target == null || show == null || hide == null) return;
        VRSession.Harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(BeforeDisplay)),
            finalizer: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(AfterDisplay)));
        VRSession.Harmony.Patch(show,
            prefix: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(BeforeWindowShow)),
            finalizer: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(AfterWindowShow)));
        VRSession.Harmony.Patch(hide,
            prefix: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(BeforeWindowHide)),
            finalizer: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(AfterWindowHide)));
        _installed = true;
    }

    /// <summary>The immersive hand path is the switch that decides whether these native windows
    /// are hidden controllers or the complete flat presentation. Check the exact three controller
    /// types rather than names or UIWindow ids (Temple and several unrelated windows use id None).</summary>
    internal static bool ShouldSilence(UIWindow window)
    {
        if (window == null || !MapRoom.MapRoomDriver.Active
            || !WorldUIConfig.ImmersiveTownServices.Value || !TownServiceEnhancementHandoff.Enabled)
            return false;
        return window.GetComponent<UIShopItemWindow>() != null
            || window.GetComponent<UITempleWindow>() != null
            || window.GetComponent<UINewEnhancementWindow>() != null;
    }

    internal static void BeforeWindowShow(UIWindow __instance, ref string? __state)
    {
        __state = null;
        if (!ShouldSilence(__instance)) return;
        ImmersiveOpenWindows.Remove(__instance);
        ImmersiveOpenWindows.Add(__instance, new ImmersiveOpenMarker());
        __state = __instance.AudioItemShow;
        __instance.AudioItemShow = string.Empty;
        VRLog.Debug("TownServices", "Immersive town native window show sound suppressed for " + __instance.name + ".");
    }

    internal static Exception? AfterWindowShow(UIWindow __instance, string? __state, Exception? __exception)
    {
        if (__state != null) __instance.AudioItemShow = __state;
        return __exception;
    }

    internal static void BeforeWindowHide(UIWindow __instance, ref string? __state)
    {
        __state = null;
        bool wasImmersive = ImmersiveOpenWindows.TryGetValue(__instance, out _);
        ImmersiveOpenWindows.Remove(__instance);
        // Map teardown can clear MapRoomDriver.Active before UIWindow.Hide runs. Remember only
        // windows that actually opened through the immersive path so their matching close cue
        // stays silent. Switching the feature or hand fallback off deliberately restores the
        // complete flat 1.0.6 sound path, even for a window that was already open.
        bool retainImmersiveClose = wasImmersive && WorldUIConfig.ImmersiveTownServices.Value
            && TownServiceEnhancementHandoff.Enabled;
        if (!ShouldSilence(__instance) && !retainImmersiveClose) return;
        __state = __instance.AudioItemHide;
        __instance.AudioItemHide = string.Empty;
        VRLog.Debug("TownServices", "Immersive town native window hide sound suppressed for " + __instance.name + ".");
    }

    internal static Exception? AfterWindowHide(UIWindow __instance, string? __state, Exception? __exception)
    {
        if (__state != null) __instance.AudioItemHide = __state;
        return __exception;
    }

    internal static void BeforeDisplay(UIPartyCharacterEnhancementAbilityCardsDisplay __instance,
        ref string? __state)
    {
        __state = null;
        UIWindow? window = __instance.GetComponentInParent<UIWindow>();
        if (window == null || !ShouldSilence(window)) return;
        __state = __instance.audioItemShow;
        __instance.audioItemShow = string.Empty;
    }

    internal static Exception? AfterDisplay(UIPartyCharacterEnhancementAbilityCardsDisplay __instance,
        string? __state, Exception? __exception)
    {
        if (__state != null) __instance.audioItemShow = __state;
        return __exception;
    }
}
