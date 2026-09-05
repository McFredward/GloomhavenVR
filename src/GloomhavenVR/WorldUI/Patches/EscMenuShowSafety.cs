using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// THE OPTIONS MENU IS NEVER INPUT-BLOCKED — the structural half (user ruling, absolute:
/// "Das soll zu keinem Zeitpunkt jemals blockiert sein, es MUSS immer möglich sein das
/// Optionsmenu zu öffnen"). The 2026-08-02 ruling was already in the code and this defect
/// walked straight past it, because it does not block a CLICK — it blocks the SHOW.
///
/// <para>THE MECHANISM, read off <c>UIWindow</c> (decompiled UIWindow.cs:540-548):</para>
/// <code>
/// protected virtual void EvaluateAndTransitionToVisualState(VisualState state, bool instant)
/// {
///     if (onTransitionBegin != null)
///         onTransitionBegin.Invoke(this, state, ...);   // &lt;-- a listener THROWS here
///     OnTransitionStarted(state, instant);
///     m_CurrentVisualState = state;                     // &lt;-- NEVER RUNS
///     ...
/// }
/// </code>
/// <c>UnityEvent.Invoke</c> has NO per-listener try/catch, so one throwing listener does two
/// things at once: it amputates every listener registered after it, AND it unwinds out of
/// <c>EvaluateAndTransitionToVisualState</c> before <c>m_CurrentVisualState = state</c>. The
/// second is the decisive one. <c>UIWindow.IsOpen</c> is <c>m_CurrentVisualState == Shown</c>,
/// so the window is not merely half-drawn — by its own accounting it never opened at all, and
/// the next <c>Show()</c> repeats the identical failure forever. That is exactly what ModBuild
/// 289 recorded: about twenty X taps in a row, every one of them throwing, not one of them
/// opening anything.
///
/// <para>Wrapping the MOD's <c>OptionsToggle.OpenMenu</c> in a try/catch cannot fix this —
/// by the time the exception reaches the mod the transition has already been abandoned
/// half-applied. The guard has to sit INSIDE the throwing listener, so that
/// <c>UnityEvent.Invoke</c> returns normally and line 548 runs. That is what this file is.</para>
///
/// <para>TWO SEAMS, innermost first:</para>
/// <list type="number">
/// <item><description><see cref="EscMenuMultiplayerCheckFinalizer"/> — the precise fix for the
/// observed throw. <c>ESCMenu.CheckMultiplayerButton</c> and both overrides are a "may the
/// multiplayer button be used?" predicate, so <c>false</c> with a null tooltip is safe BY
/// CONSTRUCTION: the button greys out and the rest of the page — Options, Compendium, Continue,
/// Main Menu, Exit — is untouched. This lets <c>OnShow</c> keep running past the throw instead of
/// merely surviving it.</description></item>
/// <item><description><see cref="EscMenuTransitionFinalizer"/> — the CATCH-ALL, and the one that
/// carries the ruling. <c>ESCMenu.OnTransitionBegin</c> IS the registered
/// <c>onTransitionBegin</c> listener (ESCMenu.cs:90), i.e. it is the exact frame in the ModBuild
/// 289 stack at which the exception crossed into <c>UnityEvent.Invoke</c>. Swallowing here makes
/// Invoke return normally whatever <c>OnShow</c>/<c>OnHide</c> did, so
/// <c>m_CurrentVisualState = state</c> always runs and the window ALWAYS opens (and always
/// closes). It covers every present and future unguarded singleton dereference in the whole
/// OnShow/OnHide family, of which the decompiled source has many:
/// <c>UIScenarioEscMenu.OnShow</c> opens on <c>Singleton&lt;UINavigation&gt;.Instance.StateMachine</c>,
/// <c>ESCMenu.OnShow</c> ends on <c>Singleton&lt;UIReadyToggle&gt;.Instance.CanBeToggled</c>,
/// <c>UIMapEscMenu.OnShow</c> reads <c>Singleton&lt;MapChoreographer&gt;.Instance.PartyAtHQ</c> and
/// <c>NewPartyDisplayUI.PartyDisplay.TabInput</c> — none of them null-checked.</description></item>
/// </list>
///
/// <para>WHY A FINALIZER AND NOT A PREFIX. A prefix that skips the original would silence the
/// game's own bookkeeping on EVERY open, not only the broken ones — the pause menu would stop
/// disabling HIGHLIGHT input, stop focusing its buttons and stop raising
/// <c>EscMenuStateChanged</c>. A finalizer is byte-identical to vanilla on the clean path (it
/// returns the original's own null exception and touches nothing) and only intervenes on the
/// throw. HarmonyX finalizer semantics, verified against the already-shipped
/// <see cref="SettingsClickExemption"/> which does the same thing on the same Harmony instance:
/// the finalizer's RETURN VALUE decides. Returning the incoming <c>__exception</c> rethrows it
/// (vanilla); returning <c>null</c> suppresses it and the patched method returns normally to its
/// caller — here, to <c>InvokableCall`3.Invoke</c>, which then walks on to the next listener and
/// lets <c>EvaluateAndTransitionToVisualState</c> finish. A <c>void</c> finalizer that merely
/// "returns normally" would NOT suppress anything; the null return is the whole mechanism.</para>
///
/// <para>Both patches are strictly no-ops while VR is off (desktop play is 100% vanilla) and
/// degrade to a strict no-op with one warning if a game update renames a target
/// (<c>WallFadeDisable</c>/<c>EscMenuInputBlock</c> pattern). Registration is from
/// <see cref="EscMenuInputBlock.EnsureRegistered"/>, which already owns the lazy PatchAll for
/// this family. MULTIPLAYER: nothing here writes game state, sends, or gates on network role —
/// it only decides whether an exception on the LOCAL presentation path is rethrown.</para>
/// </summary>
internal static class EscMenuShowSafety
{
    /// <summary>
    /// One record per (method, exception type) pair. Change-gated on the TYPE, so a
    /// swallow that starts failing for a NEW reason opens a fresh line instead of
    /// disappearing into an old counter.
    /// </summary>
    private sealed class Swallow
    {
        public long Count;
        public float LastLog;
    }

    private static readonly Dictionary<string, Swallow> Swallows = new();

    /// <summary>Repeat-summary cadence, matching <see cref="TickGuard"/>'s throttle.</summary>
    private const float RepeatSeconds = 10f;

    private static bool _degraded;

    /// <summary>
    /// True while the mod owns the pause menu and a game-side throw must not be allowed to
    /// abandon a window transition. Deliberately wider than
    /// <see cref="EscMenuInputBlock.ShouldSuppress"/>: the dev-mode conversion path opens the
    /// same windows without an HMD, and the ruling has no exception for it.
    /// </summary>
    internal static bool ShouldGuard => VRSession.IsRunning || Plugin.DevMode.Value;

    /// <summary>True once a target could not be resolved — every patch then stays vanilla.</summary>
    internal static bool Degraded => _degraded;

    /// <summary>Log the first failure and thereafter stay silent (WallFadeDisable pattern).</summary>
    internal static void Degrade(string reason)
    {
        if (_degraded)
            return;
        _degraded = true;
        // HW-VERIFY
        VRLog.Alert("EscMenuShowSafety",
            $"disabled — {reason}. A throw inside the pause menu's own OnShow/OnHide can once " +
            "again abandon UIWindow.EvaluateAndTransitionToVisualState before it sets " +
            "m_CurrentVisualState, which leaves the options menu permanently unopenable.");
    }

    /// <summary>
    /// Record and report ONE swallowed exception. The first occurrence of each
    /// (method, exception type) pair is logged WITH the full stack — a swallowed
    /// throw must never be silent, because the swallow is a symptom report, not a fix.
    /// Repeats for the same pair are summarised at most once per <see cref="RepeatSeconds"/>,
    /// so a per-frame storm cannot flood the log.
    ///
    /// <para><b>AT THE ALERT TIER, NOT Warn (ModBuild 439, survey item B3).</b> The sentence above
    /// was written when Warn printed. ModBuild 331 re-decided what each severity MEANS —
    /// <c>VRLog.Warn</c> and <c>VRLog.Info</c> both gate on <c>Level >= VRLogLevel.Debug</c>, so at
    /// the shipped default these lines printed NOTHING and the doc's own requirement was false for
    /// eight builds. The consequence being reported is the pause menu becoming permanently
    /// unopenable, which is a standing user ruling and therefore player-facing: the documented
    /// definition of <c>Alert</c>. The wordings are untouched; only the tier moved.</para>
    /// </summary>
    internal static void Report(string method, Exception ex, string consequence)
    {
        string key = method + "/" + ex.GetType().Name;
        if (!Swallows.TryGetValue(key, out Swallow s))
        {
            s = new Swallow { Count = 1, LastLog = UnityEngine.Time.unscaledTime };
            Swallows[key] = s;
            // HW-VERIFY
            VRLog.Alert("WorldUI",
                $"ESC MENU SHOW SAFETY: the game's own {method} threw {ex.GetType().Name} " +
                $"({ex.Message}) and the exception was SWALLOWED. {consequence} Without this " +
                "guard the throw would escape UnityEvent.Invoke, abandon " +
                "UIWindow.EvaluateAndTransitionToVisualState before 'm_CurrentVisualState = state', " +
                "and leave the pause/options menu permanently unopenable (user ruling: it must " +
                $"ALWAYS be possible to open the options menu).\n{ex.StackTrace}");
            return;
        }

        s.Count++;
        float now = UnityEngine.Time.unscaledTime;
        if (now - s.LastLog < RepeatSeconds)
            return;
        s.LastLog = now;
        // HW-VERIFY
        VRLog.Alert("WorldUI",
            $"ESC MENU SHOW SAFETY: {method} is still throwing {ex.GetType().Name} " +
            $"({s.Count} time(s) so far) — still swallowed, the menu still opens.");
    }
}

/// <summary>
/// CATCH-ALL. Finalizer on <c>ESCMenu.OnTransitionBegin(UIWindow, UIWindow.VisualState, bool)</c>
/// — the method <c>ESCMenu.Awake</c> registers on <c>myWindow.onTransitionBegin</c>
/// (ESCMenu.cs:90) and therefore the exact listener frame in the ModBuild 289 stack.
/// <c>ESCMenu.OnTransitionBegin</c> is <c>protected</c> and NON-virtual, so this single target
/// covers <c>UIMapEscMenu</c> and <c>UIScenarioEscMenu</c> alike; and because it is only ever
/// reached through the registered <c>UnityAction</c> delegate there is no JIT-inlining hazard.
///
/// <para>Suppressing here is strictly better than letting the throw out, in BOTH directions:
/// on a show, the window finishes opening with a partially-applied <c>OnShow</c> (an options
/// menu with, say, a stale multiplayer button) instead of not opening at all; on a hide, the
/// window finishes closing instead of latching open. The ruling admits no state in which the
/// menu cannot be opened, and "some of OnShow did not run" is not one.</para>
/// </summary>
[HarmonyPatch]
internal static class EscMenuTransitionFinalizer
{
    /// <summary>Resolved once in <see cref="EscMenuInputBlock.EnsureRegistered"/> so PatchAll is
    /// never called with an empty target set (Harmony throws on that).</summary>
    internal static MethodBase? Resolve()
    {
        try
        {
            MethodInfo? m = AccessTools.DeclaredMethod(typeof(ESCMenu), "OnTransitionBegin");
            if (m == null)
                EscMenuShowSafety.Degrade("method not found: ESCMenu.OnTransitionBegin(UIWindow, VisualState, bool)");
            return m;
        }
        catch (Exception e)
        {
            EscMenuShowSafety.Degrade($"ESCMenu.OnTransitionBegin resolution threw: {e.Message}");
            return null;
        }
    }

    private static MethodBase? TargetMethod() => Resolve();

    /// <summary>
    /// Returning <c>null</c> SUPPRESSES; returning <paramref name="__exception"/> rethrows it
    /// byte-identically to vanilla. <c>__instance</c> and <c>__1</c> (the target VisualState)
    /// are only read for the log line.
    /// </summary>
    private static Exception? Finalizer(Exception? __exception, ESCMenu __instance,
                                        UnityEngine.UI.UIWindow.VisualState __1)
    {
        if (__exception == null)
            return null; // clean path: nothing to rethrow, nothing to do
        if (EscMenuShowSafety.Degraded || !EscMenuShowSafety.ShouldGuard)
            return __exception; // desktop play / degraded patch: vanilla throw

        try
        {
            string menuType = __instance != null ? __instance.GetType().Name : "<null>";
            EscMenuShowSafety.Report(
                $"{menuType}.OnShow/OnHide (via ESCMenu.OnTransitionBegin -> {__1})",
                __exception,
                "The window transition therefore COMPLETES: the pause menu opens (or closes) with " +
                "a partially-applied OnShow, which is the safe side of the ruling.");
            return null; // suppress: UnityEvent.Invoke returns, m_CurrentVisualState is assigned
        }
        catch (Exception e)
        {
            EscMenuShowSafety.Degrade($"transition finalizer threw: {e.Message}");
            return __exception;
        }
    }
}

/// <summary>
/// PRECISE FIX for the ModBuild 289 throw. Finalizer on all three
/// <c>CheckMultiplayerButton(out string tooltip)</c> declarations —
/// <c>ESCMenu</c> and its only two subclasses, <c>UIMapEscMenu</c> and
/// <c>UIScenarioEscMenu</c> (verified: those are the complete set).
///
/// <para>WHAT THREW, NAMED. ModBuild 289 line 5384:
/// <c>UIMapEscMenu.CheckMultiplayerButton [0x00010]</c>. The method's release IL is
/// <c>ldarg.0; ldarg.1; call ESCMenu::CheckMultiplayerButton (0x0002); brtrue.s (0x0007);
/// ldc.i4.0; ret (0x0009-0x000a); call Singleton&lt;MapChoreographer&gt;::get_Instance (0x000b);
/// callvirt MapChoreographer::get_PartyAtHQ (0x0010)</c>. Offset <c>0x00010</c> is that
/// <c>callvirt</c>, and a <c>callvirt</c> NREs on a null <c>this</c>. The null field is
/// <c>Singleton&lt;MapChoreographer&gt;._instance</c> — <c>Singleton&lt;T&gt;.Instance</c> is a
/// bare <c>=&gt; _instance</c> with no guard (Singleton.cs:7), and inside a scenario there is no
/// MapChoreographer. It is not "some singleton": it is <c>MapChoreographer.PartyAtHQ</c>.</para>
///
/// <para>WHY <c>false</c> + a null tooltip IS THE SAFE VALUE. The caller is
/// <c>ESCMenu.RefreshMultiplayerButton</c>, whose entire body is
/// <c>multiplayerButton.IsInteractable = CheckMultiplayerButton(out tooltip)</c> followed by
/// <c>multiplayerButton.SetTooltip(tooltip.IsNOTNullOrEmpty(), tooltip)</c>. A <c>false</c>
/// verdict greys exactly one button; <c>IsNOTNullOrEmpty()</c> is an extension method and is
/// null-safe, so a null tooltip simply means "no tooltip". Nothing else on the page changes,
/// and the Options button — the one the ruling is about — is not touched.</para>
/// </summary>
[HarmonyPatch]
internal static class EscMenuMultiplayerCheckFinalizer
{
    /// <summary>Resolved once in <see cref="EscMenuInputBlock.EnsureRegistered"/>; empty means
    /// "do not PatchAll this class" rather than a Harmony exception.</summary>
    internal static List<MethodBase> Resolve()
    {
        var targets = new List<MethodBase>(3);
        try
        {
            // DECLARED only: AccessTools.Method walks up the hierarchy, which would hand back the
            // base declaration a second time for any subclass that did not override, and Harmony
            // would then patch the same method twice in one PatchAll.
            Type[] owners = { typeof(ESCMenu), typeof(UIMapEscMenu), typeof(UIScenarioEscMenu) };
            foreach (Type owner in owners)
            {
                MethodInfo? m = AccessTools.DeclaredMethod(owner, "CheckMultiplayerButton");
                if (m != null)
                    targets.Add(m);
                else
                    VRLog.Warn("WorldUI",
                        $"EscMenuShowSafety: method not found: {owner.Name}.CheckMultiplayerButton(out string) — " +
                        "that menu's multiplayer predicate stays unguarded (the catch-all " +
                        "ESCMenu.OnTransitionBegin finalizer still keeps the window openable).");
            }
        }
        catch (Exception e)
        {
            EscMenuShowSafety.Degrade($"CheckMultiplayerButton resolution threw: {e.Message}");
            targets.Clear();
        }
        return targets;
    }

    private static IEnumerable<MethodBase> TargetMethods() => Resolve();

    /// <summary>
    /// <c>__0</c> is the <c>out string tooltip</c> parameter, injected by INDEX rather than by
    /// name so a game update that renames it cannot silently drop the injection. It is declared
    /// <c>ref</c> because Harmony passes <c>out</c>/<c>ref</c> parameters by reference.
    /// On a throw the original never assigned it, so writing null here also satisfies the
    /// caller's definite-assignment expectation.
    /// </summary>
    private static Exception? Finalizer(Exception? __exception, MethodBase __originalMethod,
                                        ref string? __0, ref bool __result)
    {
        if (__exception == null)
            return null; // clean verdict: never narrowed, never widened
        if (EscMenuShowSafety.Degraded || !EscMenuShowSafety.ShouldGuard)
            return __exception; // desktop play / degraded patch: vanilla throw

        try
        {
            __result = false; // "the multiplayer button may not be used" — safe by construction
            __0 = null;       // no tooltip; SetTooltip's IsNOTNullOrEmpty() is null-safe
            string owner = __originalMethod?.DeclaringType?.Name ?? "ESCMenu";
            EscMenuShowSafety.Report(
                $"{owner}.CheckMultiplayerButton(out string)",
                __exception,
                "The verdict was forced to FALSE with a null tooltip, so the multiplayer button " +
                "is greyed and the REST of ESCMenu.OnShow still runs.");
            return null;
        }
        catch (Exception e)
        {
            EscMenuShowSafety.Degrade($"multiplayer-check finalizer threw: {e.Message}");
            return __exception;
        }
    }
}
