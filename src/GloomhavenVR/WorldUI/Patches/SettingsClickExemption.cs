using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// SETTINGS NEVER INPUT-BLOCKED, round 3 (user ruling 2026-08-02, escalated after two failed
/// rounds): while a scripted tutorial instruction is open, clicking the floated options window's
/// tabs did nothing — "ein Klick reagiert einfach nicht".
///
/// ROOT CAUSE (proven from decompiled source + the ModBuild-15 hardware log): the click is NOT
/// lost mod-side. The laser delivered pointer-down/click to the tab widget every time (the log's
/// "uGUI click: 'GloomhavenVR.OptionsTab'/'Cat.N'" lines ARE the ExecuteEvents dispatch), and the
/// round-2 nearest-canvas fall-through correctly never engaged (zero SETTINGS EXEMPT lines — the
/// beam was on the widget, not on an apron). The click died GAME-side: every options tab is an
/// <c>ExtendedToggle</c> (via <c>UIMainMenuOption</c>; the mod's VR tab and its Cat.N sub-tabs
/// are CLONES of those donors, so they carry the same component), and
/// <c>ExtendedToggle.OnPointerClick/OnPointerDown/OnSubmit</c> begin with
/// <code>
///   if (!InteractabilityManager.ShouldAllowClickForExtendedToggle(this)) return;
/// </code>
/// (decompiled ExtendedToggle.cs:103-142; ExtendedButton/TrackedButton/TrackedToggle/UITab carry
/// the same pattern with their own gate method). During a scripted tutorial
/// <c>InteractabilityManager.ShouldTryPreventControl()</c> is TRUE for essentially the whole
/// level: <c>LevelEventsController.s_EventsControllerActive</c> holds, and a non-null interaction
/// profile stays loaded between messages too (MessageWasDismissed →
/// <c>LoadDefaultMessagelessProfile()</c> when the level sets ShouldPreventUnspecifiedInteraction
/// — LevelEventsController.cs:986). The allow lists only ever contain the EscapeMenu /
/// PersistentUI / LevelMessageWindow families (InteractabilityManager.cs:24-45), which is why the
/// ESC menu still opened the options window while every widget INSIDE it was vetoed. The game's
/// own veto Debug.Log ("intercepted and prevented by InteractabilityManager") never reaches the
/// mod log because Unity-log capture is off — hence two blind rounds.
///
/// THE FIX: postfix all five static gate methods
/// (<c>ShouldAllowClickForExtendedButton/-ExtendedToggle/-TrackedButton/-TrackedToggle/-Tab</c>)
/// and flip a FALSE verdict to TRUE exactly when the vetoed widget lives under the mod-floated
/// options window (<see cref="ModalFallback.IsUnderFloatedSettingsWindow"/> — Options /
/// OptionsSubmenu floats only). Nothing else changes:
/// <list type="bullet">
/// <item>Blocking semantics for every OTHER surface are byte-identical — the postfix only ever
/// widens a verdict for descendants of the floated settings window; tutorial isolation of cards,
/// board tiles, bottom bar, dialogs etc. is untouched.</item>
/// <item>No state is mutated (no allow-list edits, no CanvasGroup writes), so the "restore on
/// close" path is intrinsic: the predicate goes false the moment the options float releases
/// (window closed / UserClosing / conversion dropped) and every verdict is vanilla again.</item>
/// <item>Local-only UI: the options window exists only on this client; nothing here is
/// networked, ModBuild untouched.</item>
/// <item>Desktop play is 100% vanilla: the postfix early-outs unless VR (or dev conversion)
/// runs, and without a VR float the predicate is false anyway.</item>
/// </list>
///
/// Fully reflection-guarded per repo convention (<see cref="WallFadeDisable"/> pattern): each of
/// the five targets resolves via <see cref="AccessTools"/>; a missing one is skipped with a
/// single warning, and if NONE resolve (or registration throws) the whole patch degrades to a
/// strict no-op — the options window is then merely as blocked as vanilla, never worse.
/// Registered by <c>WorldUIModule.Init</c> through <see cref="EnsureRegistered"/>; removed with
/// everything else by <c>Plugin.OnDestroy → UnpatchSelf()</c>.
/// </summary>
[HarmonyPatch]
internal static class SettingsClickExemption
{
    /// <summary>The five InteractabilityManager click gates every gated widget type routes
    /// through (ExtendedButton, ExtendedToggle, TrackedButton, TrackedToggle, UITab — sibling
    /// classes, each with its own static gate method).</summary>
    private static readonly string[] GateMethodNames =
    {
        "ShouldAllowClickForExtendedButton",
        "ShouldAllowClickForExtendedToggle",
        "ShouldAllowClickForTrackedButton",
        "ShouldAllowClickForTrackedToggle",
        "ShouldAllowClickForTab",
    };

    private static bool _registered;
    private static bool _degraded;

    /// <summary>Next unscaled time the exemption Info line may log — the veto fires per
    /// pointer event (down + click of one press), so the proof line is throttled.</summary>
    private static float _nextLogAt;

    /// <summary>
    /// Idempotent registration, called from <c>WorldUIModule.Init</c>. Resolves the gate methods
    /// first and only PatchAll's when at least one exists, so a game update that renames the
    /// whole gate degrades to a warning instead of an Init-breaking Harmony throw.
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (_registered)
            return;
        _registered = true; // set first: a throw must not retry
        try
        {
            Harmony? harmony = VRSession.Harmony;
            if (harmony == null)
            {
                Degrade("no shared Harmony instance");
                return;
            }
            List<MethodBase> targets = ResolveTargets();
            if (targets.Count == 0)
            {
                Degrade("no InteractabilityManager gate method resolved");
                return;
            }
            harmony.PatchAll(typeof(SettingsClickExemption));
            VRLog.Info("WorldUI",
                $"SettingsClickExemption: registered — {targets.Count}/{GateMethodNames.Length} " +
                "InteractabilityManager click gates postfixed; widgets inside the floated options " +
                "window can no longer be vetoed by tutorial interaction profiles " +
                "(user ruling 2026-08-02: the settings menu is never input-blocked).");
        }
        catch (Exception e)
        {
            Degrade($"registration threw: {e.Message}");
        }
    }

    /// <summary>Resolve the gate methods, warning once per missing name (game-update drift).</summary>
    private static List<MethodBase> ResolveTargets()
    {
        var targets = new List<MethodBase>(GateMethodNames.Length);
        foreach (string name in GateMethodNames)
        {
            MethodInfo? method = AccessTools.Method(typeof(InteractabilityManager), name);
            if (method != null)
                targets.Add(method);
            else
                VRLog.Warn("WorldUI",
                    $"SettingsClickExemption: method not found: InteractabilityManager.{name} — " +
                    "that widget family stays vanilla-gated.");
        }
        return targets;
    }

    /// <summary>Harmony asks this for the methods to patch (bulk form of the WallFadeDisable
    /// null-TargetMethod degrade — resolution already ran in <see cref="EnsureRegistered"/>,
    /// which guarantees this is non-empty before PatchAll is ever called).</summary>
    private static IEnumerable<MethodBase> TargetMethods() => ResolveTargets();

    /// <summary>
    /// Runs after each gate. <c>__0</c> is the widget under test (ExtendedButton /
    /// ExtendedToggle / TrackedButton / TrackedToggle / UITab — all Components; injected by
    /// index because the five parameters have different names). A TRUE verdict is never touched;
    /// a FALSE one is flipped only for widgets inside the floated options window.
    /// </summary>
    private static void Postfix(Component __0, ref bool __result)
    {
        if (__result || _degraded)
            return; // never narrow an allow; a degraded patch is a strict no-op
        try
        {
            if (!VRSession.IsRunning && !Plugin.DevMode.Value)
                return; // desktop play: vanilla verdict
            if (__0 == null)
                return;
            if (!ModalFallback.IsUnderFloatedSettingsWindow(__0.transform))
                return; // every other surface: byte-identical blocking semantics

            __result = true;
            if (Time.unscaledTime >= _nextLogAt)
            {
                _nextLogAt = Time.unscaledTime + 1f;
                VRLog.Info("WorldUI",
                    $"SETTINGS CLICK EXEMPT: game InteractabilityManager vetoed '{__0.name}' " +
                    $"({__0.GetType().Name}) — overridden: the widget is inside the floated " +
                    "options window, which is never input-blocked (user ruling 2026-08-02).");
            }
        }
        catch (Exception e)
        {
            Degrade($"postfix threw: {e.Message}");
        }
    }

    /// <summary>Log the first failure and thereafter stay silent (WallFadeDisable pattern).</summary>
    private static void Degrade(string reason)
    {
        if (_degraded)
            return;
        _degraded = true;
        VRLog.Warn("WorldUI",
            $"SettingsClickExemption disabled — {reason}. Options-window clicks stay " +
            "vanilla-gated (may be vetoed during scripted tutorials).");
    }
}
