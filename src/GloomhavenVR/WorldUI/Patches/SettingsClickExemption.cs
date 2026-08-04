using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// SETTINGS NEVER INPUT-BLOCKED, round 4 (user ruling 2026-08-02, re-escalated: "Das
/// Optionsmenue soll NIEMALS blockiert sein, egal was gerade im Spiel passiert"): while the game
/// waited for a movement-confirm (waypoint created, Ready button armed — a scripted level with
/// LevelEventsController active, VRMode.BoardTargeting), tab presses inside the ALREADY-OPEN
/// options window were dead again, and at times the options window could not even be OPENED.
///
/// ROOT CAUSE round 4 (proven from the ModBuild-52 hardware log): the round-3 postfix was
/// BYPASSED because the game's gate did not return false — it THREW. Every dead press logged
/// the triplet: "SETTINGS CLICK TRACE: ... 'Cat.5' ... (press delivered) ... gameGate=probe
/// threw NullReferenceException" followed by two bare NullReferenceException lines — the
/// gate throwing again inside ExtendedToggle.OnPointerDown and OnPointerClick, aborting both
/// handlers before base.OnPointerClick could run (Player.log 11447-13912: the state persisted
/// for MINUTES; even the plain game tabs and the VR tab at 13673+ died the same way, while the
/// ungated plain-uGUI corner X at 12470 kept working — "gameGate=ARMED, ungated widget type").
/// A Harmony POSTFIX does not run when the original throws, so the round-3 exemption never got
/// the chance to flip anything: the exception propagated into the click handler and the press
/// died. READ FROM SOURCE (InteractabilityManager.cs): the gate bodies dereference game data
/// with no guard — a null element in <c>m_CurrentlyLoadedProfile.ControlsToAllow</c>
/// (<c>c.ControlBehaviour</c>) or a null entry in an isolated control's <c>*ToAllow</c> list
/// (<c>toggle.gameObject</c>) both produce exactly this genuine NullReferenceException; which
/// of the two held the null is not attributable from the log (Unity stack traces are not
/// captured), and the fix deliberately does not depend on that: a FINALIZER neutralizes every
/// throw site inside the gates at once.
///
/// SECOND FAILURE covered (options not OPENABLE): the only VR path that opens the options
/// window is the "Options" ExtendedToggle inside the floated pause menu — a widget behind the
/// very same five gates. The round-3 predicate covered ONLY Options/OptionsSubmenu floats, so
/// a throwing (or vetoing) gate on the ESC-menu float killed the opening press itself. The
/// predicate is therefore widened to the pause menu float (UIWindowID.ESCMenu) — see
/// <see cref="ModalFallback.IsUnderFloatedPauseOrSettingsWindow"/> for the precise scope and
/// what is deliberately NOT covered (decision/targeting confirms, quit confirmations,
/// multiplayer, compendium — all byte-identical vanilla).
///
/// ROUND-3 HISTORY (still true, still handled — the veto-without-throw case):
/// Round 3: while a scripted tutorial instruction was open, clicking the floated options
/// window's tabs did nothing — "ein Klick reagiert einfach nicht".
/// ROOT CAUSE round 3 (proven from decompiled source + the ModBuild-15 hardware log): the click is NOT
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
/// THE FIX (round 4 mechanism): FINALIZE all five static gate methods
/// (<c>ShouldAllowClickForExtendedButton/-ExtendedToggle/-TrackedButton/-TrackedToggle/-Tab</c>).
/// A Harmony finalizer runs whether the original returned or THREW, receives the exception,
/// and can suppress it — the one patch shape that covers both failure modes: a clean FALSE
/// verdict is flipped to TRUE, and a throwing gate is answered TRUE with the exception
/// swallowed, exactly when the widget lives under the mod-floated pause/options windows
/// (<see cref="ModalFallback.IsUnderFloatedPauseOrSettingsWindow"/> — Options / OptionsSubmenu /
/// ESCMenu floats only). Nothing else changes:
/// <list type="bullet">
/// <item>Blocking semantics for every OTHER surface are byte-identical — the finalizer only
/// ever widens a verdict (and only swallows an exception) for descendants of the floated
/// pause/options windows; for everything else the original verdict AND the original exception
/// pass through untouched, so tutorial isolation of cards, board tiles, bottom bar, decision
/// confirms, dialogs etc. behaves exactly as vanilla — including vanilla's own throw.</item>
/// <item>No state is mutated (no allow-list edits, no CanvasGroup writes), so the "restore on
/// close" path is intrinsic: the predicate goes false the moment the float releases
/// (window closed / UserClosing / conversion dropped) and every verdict is vanilla again.</item>
/// <item>Local-only UI: the pause/options windows exist only on this client; nothing here is
/// networked, ModBuild untouched.</item>
/// <item>Desktop play is 100% vanilla: the finalizer early-outs unless VR (or dev conversion)
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
                "InteractabilityManager click gates FINALIZED (round 4: covers a vetoing AND a " +
                "throwing gate); widgets inside the floated pause/options windows can no longer " +
                "be vetoed by interaction profiles or killed by a gate NullReferenceException " +
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
    /// Harmony FINALIZER — runs after each gate whether it returned or THREW (round 4: the
    /// round-3 Postfix never ran on the throwing gate, see the class doc's log evidence).
    /// <c>__0</c> is the widget under test (ExtendedButton / ExtendedToggle / TrackedButton /
    /// TrackedToggle / UITab — all Components; injected by index because the five parameters
    /// have different names). <c>__exception</c> is the original's escaped exception (null on a
    /// clean return); when the original threw, <c>__result</c> holds default(bool) == false.
    /// Contract:
    /// <list type="bullet">
    /// <item>clean TRUE verdict: never touched (returns null — nothing to rethrow);</item>
    /// <item>clean FALSE verdict or a THROW, widget under the floated pause/options windows:
    /// verdict forced TRUE and the exception (if any) SUPPRESSED by returning null — the click
    /// handler proceeds as if the gate had allowed it;</item>
    /// <item>everything else: the original exception object is returned unchanged, which makes
    /// Harmony rethrow it — byte-identical vanilla, including vanilla's own crash-log line.</item>
    /// </list>
    /// </summary>
    private static Exception? Finalizer(Exception? __exception, Component __0, ref bool __result)
    {
        if (_degraded)
            return __exception; // degraded patch is a strict no-op: verdict AND throw vanilla
        if (__exception == null && __result)
            return null; // never narrow an allow
        try
        {
            if (!VRSession.IsRunning && !Plugin.DevMode.Value)
                return __exception; // desktop play: vanilla verdict and vanilla throw
            if (__0 == null)
                return __exception;
            if (!ModalFallback.IsUnderFloatedPauseOrSettingsWindow(__0.transform))
                return __exception; // every other surface: byte-identical blocking semantics

            __result = true;
            if (Time.unscaledTime >= _nextLogAt)
            {
                _nextLogAt = Time.unscaledTime + 1f;
                string cause = __exception == null
                    ? "vetoed"
                    : $"THREW {__exception.GetType().Name} on";
                VRLog.Info("WorldUI",
                    $"SETTINGS CLICK EXEMPT: game InteractabilityManager {cause} '{__0.name}' " +
                    $"({__0.GetType().Name}) — overridden (exception suppressed, verdict TRUE): " +
                    "the widget is inside the floated pause/options windows, which are never " +
                    "input-blocked (user ruling 2026-08-02).");
            }
            return null; // suppress the gate's exception so the click handler proceeds
        }
        catch (Exception e)
        {
            Degrade($"finalizer threw: {e.Message}");
            return __exception;
        }
    }

    /// <summary>Log the first failure and thereafter stay silent (WallFadeDisable pattern).</summary>
    private static void Degrade(string reason)
    {
        if (_degraded)
            return;
        _degraded = true;
        VRLog.Warn("WorldUI",
            $"SettingsClickExemption disabled — {reason}. Pause/options-window clicks stay " +
            "vanilla-gated (may be vetoed during scripted tutorials, or killed by a gate " +
            "NullReferenceException during confirm waits).");
    }
}
