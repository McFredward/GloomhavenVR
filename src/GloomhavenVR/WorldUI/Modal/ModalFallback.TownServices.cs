using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    /// <summary>Retire only auxiliary UIWindow floats which originated below one immersive town
    /// controller. Build 568 hardware evidence showed the merchant's nested Scroll View detached
    /// behind the NPC after an item handoff. Its native controller must remain alive, but that
    /// duplicate presentation must return home before the controller is masked.</summary>
    internal static bool ReleaseTownServiceAuxiliaries(UIWindow controller)
    {
        if (controller == null || controller.transform == null) return false;
        // ReleaseForComposite mutates both registries. Re-scan after each exact release instead
        // of sweeping scene windows or keeping stale panel indices.
        for (int pass = 0; pass < 32; pass++)
        {
            UIWindow? auxiliary = null;
            for (int i = 0; i < Converted.Count; i++)
            {
                WindowPanel wp = Converted[i];
                if (IsTownServiceAuxiliary(controller, wp.Window, wp.Panel))
                {
                    auxiliary = wp.Window;
                    break;
                }
            }
            if (auxiliary == null)
            {
                for (int i = 0; i < CanvasConversion.ActivePanels.Count; i++)
                {
                    ConvertedPanel panel = CanvasConversion.ActivePanels[i];
                    UIWindow? candidate = panel.Target != null
                        ? panel.Target.GetComponent<UIWindow>() : null;
                    if (IsTownServiceAuxiliary(controller, candidate, panel))
                    {
                        auxiliary = candidate;
                        break;
                    }
                }
            }
            if (auxiliary == null) return true;
            if (!ReleaseForComposite(auxiliary)) return false;
        }
        // A controller cannot reasonably contain this many detached child windows. Refuse to
        // mask it if native code is continuously producing more during the handoff.
        return false;
    }

    private static bool IsTownServiceAuxiliary(UIWindow controller, UIWindow? candidate,
        ConvertedPanel panel)
    {
        if (candidate == null || ReferenceEquals(candidate, controller)) return false;
        Transform child = candidate.transform;
        Transform? parent = panel.OriginalParent;
        return child != null && child.IsChildOf(controller.transform)
            || parent != null && (ReferenceEquals(parent, controller.transform)
                || parent.IsChildOf(controller.transform));
    }

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
/// is active. The native controllers and their callbacks still run; exact serialized fields are
/// blank only for their calls and direct UI audio is skipped only inside an automatic service
/// transition. Flat mode, confirmation and purchase feedback, resident speech, physical map
/// presses and cabinet/card sounds remain untouched.</summary>
internal static class TownServiceNativeAudioSilence
{
    private sealed class ImmersiveOpenMarker { }

    private static bool _installed;
    [ThreadStatic] private static int _automaticTransitionDepth;
    [ThreadStatic] private static string? _automaticTransitionContext;
    private static int _traceLines;
    private const int TraceLineBudget = 32;
    private static readonly ConditionalWeakTable<UIWindow, ImmersiveOpenMarker> ImmersiveOpenWindows = new();
    internal static void EnsureInstalled()
    {
        if (_installed || VRSession.Harmony == null) return;
        var target = AccessTools.Method(typeof(UIPartyCharacterEnhancementAbilityCardsDisplay),
            nameof(UIPartyCharacterEnhancementAbilityCardsDisplay.Display));
        var show = AccessTools.Method(typeof(UIWindow), nameof(UIWindow.Show), new[] { typeof(bool) });
        var hide = AccessTools.Method(typeof(UIWindow), nameof(UIWindow.Hide), new[] { typeof(bool) });
        var nativePlay = AccessTools.Method(typeof(AudioControllerUtils), nameof(AudioControllerUtils.PlaySound),
            new[] { typeof(string), typeof(bool) });
        if (target == null || show == null || hide == null || nativePlay == null) return;
        VRSession.Harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(BeforeDisplay)),
            finalizer: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(AfterDisplay)));
        VRSession.Harmony.Patch(show,
            prefix: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(BeforeWindowShow)),
            finalizer: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(AfterWindowShow)));
        VRSession.Harmony.Patch(hide,
            prefix: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(BeforeWindowHide)),
            finalizer: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(AfterWindowHide)));
        VRSession.Harmony.Patch(nativePlay,
            prefix: new HarmonyMethod(typeof(TownServiceNativeAudioSilence), nameof(BeforeNativePlay)));
        _installed = true;
    }

    /// <summary>Mark one automatic physical-resident mode transition. The native mode still runs
    /// every listener and callback; only audio requested synchronously by that automatic switch is
    /// skipped. Physical map-button presses never enter this scope, so their hover/down feedback
    /// remains the game's own.</summary>
    internal static bool BeginAutomaticTransition(EGuildmasterMode mode, string source)
    {
        if (!MapRoom.MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value
            || !TownServiceEnhancementHandoff.Enabled)
            return false;
        if (_automaticTransitionDepth++ == 0)
            _automaticTransitionContext = source + " -> " + mode;
        return true;
    }

    internal static void EndAutomaticTransition(bool armed)
    {
        if (!armed || _automaticTransitionDepth <= 0) return;
        if (--_automaticTransitionDepth == 0) _automaticTransitionContext = null;
    }

    /// <summary>Final defense for direct native UI sounds which do not read a UIWindow field.
    /// This is deliberately stack-bounded by <see cref="BeginAutomaticTransition"/> rather than
    /// item-name based: the same UI item is legitimate when the player presses a physical map cap.</summary>
    internal static bool BeforeNativePlay(string audioItem)
    {
        if (_automaticTransitionDepth <= 0) return true;
        Trace("AudioControllerUtils.PlaySound", audioItem, Caller());
        return false;
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
        Trace("UIWindow.Show", __state, __instance.name);
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
        Trace("UIWindow.Hide", __state, __instance.name);
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
        Trace("UIPartyCharacterEnhancementAbilityCardsDisplay.Display", __state,
            window.name);
    }

    internal static Exception? AfterDisplay(UIPartyCharacterEnhancementAbilityCardsDisplay __instance,
        string? __state, Exception? __exception)
    {
        if (__state != null) __instance.audioItemShow = __state;
        return __exception;
    }

    private static void Trace(string edge, string? item, string owner)
    {
        // The maintainer tests at Debug. VRLog.Debug maps to BepInEx LogDebug, which the supplied
        // build-566 capture proved can be filtered independently (zero Debug lines despite abundant
        // project Debug-tier output). Use the project's Debug-tier Info emitter and cap the whole
        // session so ordinary player logs remain unchanged and repeated visits cannot grow a file.
        if (!VRLog.WantsDebug || _traceLines >= TraceLineBudget) return;
        _traceLines++;
        VRLog.Info("TownServices", "TOWN NATIVE AUDIO suppressed " + edge + " item='"
            + (string.IsNullOrEmpty(item) ? "<empty>" : item) + "' owner='" + owner
            + "' automatic='" + (_automaticTransitionContext ?? "none") + "' ("
            + _traceLines + "/" + TraceLineBudget + ").");
    }

    private static string Caller()
    {
        // Called only inside a rare automatic transition, only when Debug is requested and only
        // for the bounded lines above. This is causal evidence, not a per-frame sampling stream.
        // Trace() has already enforced the session budget before asking for a caller.
        // Keep this helper read-only with respect to that diagnostic counter so instrumentation
        // cannot become a false mechanism dependency in the refactor guard.
        if (!VRLog.WantsDebug) return "not sampled";
        var trace = new StackTrace(2, false);
        for (int i = 0; i < trace.FrameCount; i++)
        {
            var method = trace.GetFrame(i)?.GetMethod();
            Type? type = method?.DeclaringType;
            if (method == null || type == null || type == typeof(TownServiceNativeAudioSilence)
                || type == typeof(AudioControllerUtils) || type.Namespace?.StartsWith("HarmonyLib") == true)
                continue;
            return type.FullName + "." + method.Name;
        }
        return "unknown managed caller";
    }
}
