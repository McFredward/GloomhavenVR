using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Patches;

// ---------------------------------------------------------------------------
// THE CHARACTER-DISPLAY REFCOUNT — REPAIRED IN FULL, AND THE 186 DIAGNOSIS RETRACTED.
//
// FIRST, WHAT IS NO LONGER CLAIMED. ModBuild 186 reported that `Beautify` on the 'GUI 3D Camera'
// was toggling every few frames and that this was the flicker the user sees. ModBuild 187 patched
// the refcount on that basis and the warnings came back unchanged — because RenderTargetProbe's
// A-B-A test was INVERTED against its own doc comment (it required `now == t-1 && now != t-2`,
// which is a transition that HELD, and is never satisfied by a value that alternates every frame).
// The 187 hardware log settles it by position alone: the assembly screen opened once and closed
// once all session, and the six warnings sit exactly on those two frames —
//
//     4297  UIWindow SHOWN:  'Campaign Adventure Party Assembly Variant' (ID PartyAssemblyWindow)
//     4305-4307  RENDER TARGET ALTERNATION ... enabled-bits 0xD↔0xF     (x3: three RawImages,
//     4364-4365  UIWindow hidden: 'Campaign Adventure Party Assembly Variant'   one RenderTexture)
//     4367-4369  RENDER TARGET ALTERNATION ... enabled-bits 0xF↔0xD     (x3)
//
// — one legitimate `beautify.enabled = true` from Display() when the window opened and one
// legitimate `= false` from Hide() when it closed. Beautify was never flickering. See
// WorldUI/RenderTargetProbe for the fixed instrument.
//
// SECOND, WHY THIS PATCH STAYS AND GROWS ANYWAY. The refcount defect it repairs is real, is
// provable from the decompiled source, and is reachable ONLY by the map room:
//
//     public void Display(Component request, ECharacter character, string skin, string anim)
//     {
//         isHidden = false;
//         beautify.enabled = true;
//         if (character3D != null && character3D.TypeCharacter == character && character3D.Skin == skin)
//         {
//             character3D.Show(anim);
//             return;                                   // <-- RETURNS WITHOUT TAKING A REFERENCE
//         }
//         showRequests.Add(request);
//         ...
//     }
//
//     public void Hide(Component request)                        // Character3DDisplayManager.cs:292
//     {
//         showRequests.Remove(request);
//         isHidden = true;                                       // <-- UNGATED
//         if (showRequests.Count == 0) beautify.enabled = false;  //     gated, correctly
//         if (character3D != null) character3D.Hide();            // <-- UNGATED: SetActive(false)
//     }                                                          //     on every model
//
//     public void HideAll(Component request)                     // ...cs:306 — same shape:
//     {                                                          //     the SetActive(false) block
//         showRequests.Remove(request);                          //     is outside the count test
//         isHidden = true;
//         if (showRequests.Count == 0) { gameObject.SetActive(false); beautify.enabled = false; }
//         if (character3D != null) { character3D.Hide(); ... }
//     }
//
// Exactly ONE of the four things Hide() does is gated on the refcount. The other three — isHidden,
// the model SetActive(false), and the unload in HideAll — fire for ANY requester, so the first
// window to close blanks the character out of a render texture a second still-open window is
// showing, and leaves isHidden = true so the async LoadCharacter coroutine's `if (!isHidden)
// Show()` will not put it back. A refcount that only one of its consumers respects is not a
// refcount.
//
// THE FLAT GAME CANNOT REACH THIS. Its single-window discipline means the party display and the
// party-assembly screen are never open together, so there is never a second requester and the
// ungated lines are indistinguishable from the gated one. The map room's whole point is that they
// ARE open together (user ruling, ModBuild 180: "Anders als in Flat soll es hier möglich sein
// mehrere Fenster parallel offen zu haben"). We created the multi-requester case; we own making the
// game's bookkeeping survive it.
//
// SO THE PATCH IS THE MISSING GATE, IN THREE PARTS:
//   * Display  — take the reference the early-return path skips (HashSet.Add is idempotent, so on
//                the other path this is a harmless duplicate).
//   * Hide     — if any OTHER requester still holds a reference, drop only this one and SKIP the
//                original entirely: beautify, isHidden and the models all belong to the requester
//                that is still there. The last requester out runs vanilla, untouched.
//   * HideAll  — the same gate, for the same reason.
// With a single requester every one of these is byte-identical to vanilla, which is why it is safe
// everywhere outside the map room.
//
// AND A DEAD-REQUESTER SWEEP, because a gate that can be held open forever is worse than no gate:
// a requester Component destroyed without calling Hide would pin the display on for the rest of the
// session. Every call prunes references whose Component has been destroyed (Unity's overloaded ==,
// not ReferenceEquals) before the gate is evaluated, and says so when it finds any.
//
// FINALLY, THE ATTRIBUTION. Because the 186 measurement turned out to name the wrong thing, this
// class also OBSERVES: it logs the first call of each kind with its actual CALLER (a stack walk, so
// the log names UIAdventurePartyAssemblyWindow.PreviewCharacterInfo rather than "something called
// Display"), and then one cadence line per second of activity counting Display/Hide/HideAll calls,
// the requesters that made them, and how many times beautify.enabled and the model count actually
// changed. One ON in a second is a window opening; ten ON/OFF pairs in a second is the flicker, and
// the log will now say which of the two happened instead of leaving it to be inferred.
//
// DEGRADES SAFELY: every private member is resolved through AccessTools once. If any is missing the
// class logs ONE Warn naming the consequence and stands completely down — no gate, no counting,
// vanilla behaviour, nothing thrown. Nothing here touches a rule library, Bolt, or the wire.
// ---------------------------------------------------------------------------

/// <summary>
/// Makes <c>Character3DDisplayManager</c>'s show/hide idempotent for multiple requesters, so two
/// windows floated in parallel can display the same character without switching each other's render
/// off — and logs who calls it, how often, and what actually changed. Registered by
/// <c>WorldUIModule</c>.
/// </summary>
[HarmonyPatch]
internal static class Character3DDisplayRefcount
{
    private const string Scope = "WorldUI";

    /// <summary>A cadence line covers at most this much activity, so a flicker is reported as a
    /// RATE and never as a single event.</summary>
    private const float WindowSeconds = 1f;

    // ---- reflected private state ----------------------------------------------------------

    private static FieldInfo? _showRequests;
    private static FieldInfo? _beautify;
    private static FieldInfo? _holder;
    private static bool _resolved;

    /// <summary>Set when reflection failed: the class then does nothing at all, forever.</summary>
    private static bool _standDown;

    // ---- accounting -----------------------------------------------------------------------

    private static readonly List<Component> DeadScratch = new(4);
    private static readonly Dictionary<string, int> Callers = new(4);
    private static readonly StringBuilder Sb = new(256);
    private static readonly HashSet<string> TracedKinds = new();

    private static float _windowStart = -1f;
    private static int _displays;
    private static int _hides;
    private static int _hideAlls;
    private static int _gated;
    private static int _pruned;
    private static int _beautifyOn;
    private static int _beautifyOff;
    private static int _modelSwings;
    private static bool _lastBeautify;
    private static int _lastModelActive = -1;
    private static bool _sampled;

    // ---- the three seams ------------------------------------------------------------------

    /// <summary>
    /// Take the show-request reference the game's own early-return path skips. Prefix, so it is
    /// already counted whichever branch the original takes.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character3DDisplayManager), "Display",
        typeof(Component), typeof(ECharacter), typeof(string), typeof(string))]
    private static void BeforeDisplay(Character3DDisplayManager __instance, Component request)
    {
        if (!Live(__instance))
            return;
        HashSet<Component>? set = Requests(__instance);
        if (set == null)
            return;
        Sample(__instance);
        Prune(set);
        Account("Display", request, __instance);
        if (request == null)
            return;
        set.Add(request); // idempotent; the original's own Add on the other branch is a no-op then
    }

    /// <summary>
    /// The missing gate on <c>Hide</c>. See the header: only <c>beautify.enabled = false</c> is
    /// gated on the refcount in the original; <c>isHidden</c> and <c>character3D.Hide()</c> are not.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character3DDisplayManager), nameof(Character3DDisplayManager.Hide))]
    private static bool BeforeHide(Character3DDisplayManager __instance, Component request)
        => Gate(__instance, request, "Hide");

    /// <summary>The same gate on <c>HideAll</c>, which additionally deactivates the manager's own
    /// GameObject and may unload the character.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character3DDisplayManager), nameof(Character3DDisplayManager.HideAll))]
    private static bool BeforeHideAll(Character3DDisplayManager __instance, Component request)
        => Gate(__instance, request, "HideAll");

    /// <summary>
    /// Returns true to run the original hide, false to swallow it. It is swallowed only when at
    /// least one OTHER requester still holds a reference — the last one out always runs vanilla.
    /// </summary>
    private static bool Gate(Character3DDisplayManager instance, Component request, string kind)
    {
        if (!Live(instance))
            return true;
        HashSet<Component>? set = Requests(instance);
        if (set == null)
            return true;
        Sample(instance);
        Prune(set);
        Account(kind, request, instance);

        int others = set.Count - (request != null && set.Contains(request) ? 1 : 0);
        if (others <= 0)
            return true; // last (or unknown) requester leaving — vanilla hide, everything goes off

        if (request != null)
            set.Remove(request);
        _gated++;
        // Once per kind per session in full; after that the cadence line carries the count, because
        // a gate that fires every frame must not become the log.
        if (TracedKinds.Add(kind + "-GATED"))
            Trace($"{kind}-GATED", request,
            $"{others} other requester(s) still hold the character display, so the original "
            + $"{kind}() was SKIPPED. Vanilla would have run isHidden = true and "
            + "character3D.Hide() — SetActive(false) on every model — for ALL of them, because only "
            + "the beautify.enabled line is gated on showRequests.Count == 0. This requester's "
            + "reference has been dropped; the display goes off when the last one leaves.");
        return false;
    }

    // ---- plumbing -------------------------------------------------------------------------

    /// <summary>VR only, live instance only, and never after a failed reflection.</summary>
    private static bool Live(Character3DDisplayManager instance)
        => !_standDown && VRSession.IsRunning && instance != null;

    /// <summary>The manager's show-request set, or null once the class has stood down.</summary>
    private static HashSet<Component>? Requests(Character3DDisplayManager instance)
    {
        Resolve();
        if (_standDown)
            return null;
        return _showRequests!.GetValue(instance) as HashSet<Component>;
    }

    private static void Resolve()
    {
        if (_resolved)
            return;
        _resolved = true;
        _showRequests = AccessTools.Field(typeof(Character3DDisplayManager), "showRequests");
        _beautify = AccessTools.Field(typeof(Character3DDisplayManager), "beautify");
        _holder = AccessTools.Field(typeof(Character3DDisplayManager), "character3DHolder");
        if (_showRequests != null)
            return;
        _standDown = true;
        // HW-VERIFY (2026-09 refactor, F-74) — a one-shot stand-down whose own text names the
        // player-visible outcome (a blanked model in a window that is still open).
        VRLog.Alert(Scope, "CHARACTER 3D REFCOUNT: the private 'showRequests' set was not found on "
                          + "Character3DDisplayManager — the whole class stands down (no gate, no "
                          + "counting, vanilla behaviour). THE CONSEQUENCE: with two windows floated on "
                          + "the same character, the first one to close runs the game's ungated "
                          + "character3D.Hide() and isHidden = true, which blanks the model out of the "
                          + "render texture the second window is still showing. Nothing else changes.");
    }

    /// <summary>Drop references whose Component has been destroyed — Unity's overloaded ==, because a
    /// destroyed Component is not ReferenceEquals-null and would pin the display on forever.</summary>
    private static void Prune(HashSet<Component> set)
    {
        DeadScratch.Clear();
        foreach (Component c in set)
        {
            if (c == null)
                DeadScratch.Add(c!); // Unity's overloaded ==; the reference itself is alive
        }
        if (DeadScratch.Count == 0)
            return;
        for (int i = 0; i < DeadScratch.Count; i++)
            set.Remove(DeadScratch[i]);
        _pruned += DeadScratch.Count;
    }

    // ---- observation ----------------------------------------------------------------------

    /// <summary>Read the two values only these three methods write, and count every change. Sampling
    /// at call boundaries is exact because the decompiled source shows no other writer.</summary>
    private static void Sample(Character3DDisplayManager instance)
    {
        if (_beautify?.GetValue(instance) is Behaviour b)
        {
            bool on = b.enabled;
            if (_sampled && on != _lastBeautify)
            {
                if (on)
                    _beautifyOn++;
                else
                    _beautifyOff++;
            }
            _lastBeautify = on;
        }
        int active = ModelsActive(instance);
        if (_sampled && active != _lastModelActive)
            _modelSwings++;
        _lastModelActive = active;
        _sampled = true;
    }

    /// <summary>How many of the manager's model objects are switched on right now, or -1 when the
    /// holder is not reflectable.</summary>
    private static int ModelsActive(Character3DDisplayManager instance)
    {
        if (_holder?.GetValue(instance) is not Transform holder)
            return -1;
        int active = 0;
        for (int i = 0; i < holder.childCount; i++)
        {
            Transform child = holder.GetChild(i);
            if (child != null && child.gameObject.activeSelf)
                active++;
        }
        return active;
    }

    private static void Account(string kind, Component request, Character3DDisplayManager instance)
    {
        float now = Time.unscaledTime;
        if (_windowStart < 0f)
            _windowStart = now;

        switch (kind)
        {
            case "Display": _displays++; break;
            case "Hide": _hides++; break;
            default: _hideAlls++; break;
        }
        string who = request != null ? request.GetType().Name : "<null requester>";
        Callers.TryGetValue(who, out int seen);
        Callers[who] = seen + 1;

        if (TracedKinds.Add(kind))
        {
            Trace(kind, request,
                "first call of this kind this session. The CALLER is named above so the next round "
                + "does not have to guess which window drives the display.");
        }

        if (now - _windowStart >= WindowSeconds)
            Flush(instance, now);
    }

    /// <summary>One line per second of activity: what was called, by whom, and what actually moved.</summary>
    private static void Flush(Character3DDisplayManager instance, float now)
    {
        float span = now - _windowStart;
        _windowStart = now;
        Sample(instance); // catch the change the last call in the window made

        Sb.Length = 0;
        foreach (KeyValuePair<string, int> pair in Callers)
        {
            if (Sb.Length > 0)
                Sb.Append(", ");
            Sb.Append(pair.Key).Append(" x").Append(pair.Value);
        }
        int total = _displays + _hides + _hideAlls;
        bool churn = _beautifyOn > 1 || _beautifyOff > 1 || _modelSwings > 2;
        string verdict = churn
            ? "THIS IS A FLICKER RATE: the display was switched more than once inside the window, so "
              + "something is fighting over it — read the requester list above for who."
            : "This is NOT a flicker: at most one switch each way inside the window, which is what a "
              + "window opening or closing looks like.";
        // The live refcount is printed on every line for one reason: the gate below can only be
        // held open by a reference that is never given back, so a count that climbs and never
        // returns to 0 is the one way this patch could misbehave, and it must be visible.
        int held = Requests(instance)?.Count ?? -1;
        VRLog.Info(Scope, $"CHARACTER 3D CADENCE ({span:F2}s): {total} call(s) — Display x{_displays}, "
                          + $"Hide x{_hides}, HideAll x{_hideAlls}; requesters [{Sb}]; "
                          + $"{held} show-request(s) held after the window; "
                          + $"{_gated} hide(s) GATED by this patch (another requester still held the "
                          + $"display); {_pruned} dead requester(s) pruned. MEASURED CHANGES in the "
                          + $"window: beautify.enabled went ON {_beautifyOn}x and OFF {_beautifyOff}x, "
                          + $"the active model count changed {_modelSwings}x (now "
                          + $"{(_lastModelActive < 0 ? "unknown" : _lastModelActive.ToString())} active). "
                          + verdict);

        _displays = _hides = _hideAlls = 0;
        _gated = _pruned = 0;
        _beautifyOn = _beautifyOff = _modelSwings = 0;
        Callers.Clear();
    }

    /// <summary>Name the actual caller. Frames belonging to the manager itself and to this patch are
    /// skipped, so what is printed is the game code that decided to show or hide.</summary>
    private static void Trace(string kind, Component? request, string why)
    {
        string who = request != null ? request.GetType().Name : "<null requester>";
        VRLog.Info(Scope, $"CHARACTER 3D {kind}: requester '{who}', called from {Stack()} — {why}");
    }

    private static string Stack()
    {
        try
        {
            var trace = new StackTrace(2, false);
            var sb = new StringBuilder(160);
            int printed = 0;
            for (int i = 0; i < trace.FrameCount && printed < 4; i++)
            {
                MethodBase? m = trace.GetFrame(i)?.GetMethod();
                Type? t = m?.DeclaringType;
                if (m == null)
                    continue;
                string name = t != null ? t.Name : "?";
                // Both the manager and this patch class start with the same prefix, and Harmony's
                // generated wrapper declares itself on the manager type too.
                if (name.StartsWith("Character3DDisplay", StringComparison.Ordinal))
                    continue;
                if (printed++ > 0)
                    sb.Append(" <- ");
                sb.Append(name).Append('.').Append(m.Name);
            }
            return sb.Length > 0 ? sb.ToString() : "<no managed frames above the manager>";
        }
        catch (Exception e)
        {
            // A stack walk is a diagnostic, never a reason to fail a UI call.
            return $"<stack unavailable: {e.GetType().Name}>";
        }
    }
}
