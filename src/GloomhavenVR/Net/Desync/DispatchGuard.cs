using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net.Desync;

/// <summary>
/// Exception isolation for Harmony patch bodies that sit on a type the game dispatches NETWORK
/// ACTIONS into. Same contract as <see cref="TickGuard"/> — isolate, attribute, throttle, never
/// rethrow — but a separate seam, because the reason is different and so is the stake.
///
/// <para>THE STAKE. <c>ActionProcessor.TryProcessNextAction</c> wraps the entire game-action
/// dispatch in <c>catch (Exception ex) { FFSNetwork.HandleDesync(ex); }</c>, and
/// <c>GHNetworkControllable</c>'s eight Bolt state callbacks do the same. So an exception thrown
/// anywhere under that dispatch — including out of one of OUR patch bodies — is not logged as a
/// mod bug. It is shown to the player as the GAME's "Desynchronization occurred" dialog, and the
/// session is shut down with a single Main Menu button. A cosmetic patch, or worse a DIAGNOSTIC
/// one, must never be able to do that. See <c>.planning/multiplayer/DESYNC-ANALYSIS.md</c>.</para>
///
/// <para>WHY THIS IS NOT SUPPRESSION. The analysis is explicit that swallowing the GAME's
/// exceptions would be dangerous — a session that runs past a real state divergence writes a
/// corrupt campaign save. Nothing here touches the game's exceptions. This only stops OUR
/// additions from raising one, which is the opposite move: it removes a cause rather than hiding
/// an effect.</para>
///
/// <para>WHY NOT <see cref="TickGuard"/>. That one feeds every step to <see cref="PerfMonitor"/>
/// for the per-frame STEPS ranking, whose whole meaning is "the mod's per-frame cost". Patch
/// bodies fire on the game's cadence, not ours, and folding them into that ranking would make the
/// perf instrument answer a question nobody asked. Identical isolation, no perf coupling.</para>
///
/// <para>A patch on a receiver type does NOT automatically need this — see
/// <c>docs/NET-ACTION-SURFACE.md</c>, where each one is classified. A body that reads only mod
/// state, or whose one call is already guarded deeper (<c>VREvents.Invoke</c>), is safe as it
/// stands, and wrapping it would add noise, not safety.</para>
/// </summary>
internal static class DispatchGuard
{
    private sealed class Entry
    {
        public long Count;
        public float LastLog;
        public bool Opened;
    }

    /// <summary>Seconds between repeat lines for one name. Long, because a patch body that throws
    /// every dispatch would otherwise become the flood it exists to prevent.</summary>
    private const float RepeatSeconds = 10f;

    private static readonly Dictionary<string, Entry> State = new();

    /// <summary>
    /// Run a void patch body (a postfix, or a prefix that does not skip). A throw is isolated,
    /// logged at Error WITH its stack the first time for this <paramref name="name"/>, and
    /// summarized at most once per 10 s afterwards. Never rethrows — that is the whole point.
    /// </summary>
    public static void Run(string name, Action body, string scope = "Net")
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Report(name, scope, ex);
        }
    }

    /// <summary>
    /// Run a bool-returning patch body (a prefix that decides whether the original runs).
    /// <paramref name="onThrow"/> is what to return when it throws — pass <c>true</c> unless
    /// there is a stated reason not to, because <c>true</c> means "let the game's own method
    /// run", i.e. fall back to vanilla behaviour. A guard that cannot decide must not also
    /// silently cancel the thing it was guarding.
    /// </summary>
    public static bool Run(string name, Func<bool> body, bool onThrow, string scope = "Net")
    {
        try
        {
            return body();
        }
        catch (Exception ex)
        {
            Report(name, scope, ex);
            return onThrow;
        }
    }

    private static void Report(string name, string scope, Exception ex)
    {
        Entry entry;
        if (!State.TryGetValue(name, out entry!))
        {
            entry = new Entry();
            State[name] = entry;
        }
        entry.Count++;

        float now = Time.unscaledTime;
        if (!entry.Opened)
        {
            entry.Opened = true;
            entry.LastLog = now;
            // Full stack, once: this names the exact patch that would otherwise have been
            // reported to the player as the game's own desynchronisation.
            VRLog.Error(scope, $"DISPATCH GUARD: patch body '{name}' threw and was ISOLATED. " +
                               "Left unguarded this would have surfaced as the GAME's " +
                               $"\"Desynchronization occurred\" dialog. {ex}");
            return;
        }

        if (now - entry.LastLog < RepeatSeconds)
            return;
        entry.LastLog = now;
        VRLog.Alert(scope, $"DISPATCH GUARD: '{name}' is still throwing ({entry.Count} times so " +
                           $"far). Latest: {ex.GetType().Name}: {ex.Message}");
    }
}
