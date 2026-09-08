using System;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using TMPro;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// THE MOD'S MOST EXPENSIVE STEP, REMOVED (hardware evidence, multiplayer test 2026-08-02).
///
/// <para><see cref="VRKeyboard"/> needs to notice a text field that the GAME focuses by itself, with
/// no click for <c>VRKeyboard.NoticeClick</c> to see. Its only way of noticing was a fallback sweep —
/// <c>FindObjectsOfType&lt;TMP_InputField&gt;()</c> — throttled to one run every 0.2 s. In an empty
/// menu that sweep was the 2.3 ms the original comment measured. In a LOADED SCENARIO it is not:</para>
/// <code>
/// [Perf] STEPS 30.0s ... VRKeyboard 1.481ms avg, worst 19.25ms, 86.0ms/s, frames 1742 | ...
/// [Perf] SPIKE frame 66660: 25.70ms = 2.3x the 11.11ms budget | mod 19.92ms of it |
///        worst steps: VRKeyboard 17.59ms, CanvasConversion 1.00ms, ...
/// </code>
/// <para>That is the mod's NUMBER ONE cost in the whole session — 86 ms of every second spent
/// searching the scene graph for a keyboard nobody had asked for, arriving as a ~18 ms stall five
/// times per second. The frame budget at 90 Hz is 11.1 ms, so every single sweep dropped a frame,
/// and 1353 of the logged spikes have this step at the top. <c>FindObjectsOfType</c> walks every
/// live object in the scene; the scenario scene is enormous (550 MB heap at that point), which is
/// why the same call that cost 2.3 ms in the menu costs eight times that in play.</para>
///
/// <para>THE SWEEP EXISTS BECAUSE FOCUS HAS NO EVENT — so this gives it one. TMP routes EVERY way a
/// field can take focus through <c>TMP_InputField.ActivateInputField()</c>: the pointer click
/// handler, <c>OnSelect</c> when <c>shouldActivateOnSelect</c>, and any code calling it directly.
/// Likewise <c>DeactivateInputField()</c> is the single exit. A postfix on each turns the poll into
/// a push: the watch simply remembers the field TMP last activated, and the sweep is gone.</para>
///
/// <para>WHY THIS IS NOT A BEHAVIOUR CHANGE. The sweep's acceptance test was
/// <c>candidate.isFocused &amp;&amp; candidate.IsInteractable()</c>, and <c>isFocused</c> is TMP's own
/// <c>m_AllowInput</c>, which is set by <c>ActivateInputFieldInternal</c> — reached only from
/// <c>ActivateInputField</c>. So the set of fields the sweep could ever return is exactly the set
/// this watch records. <see cref="VRKeyboard"/> still re-checks <c>isFocused</c> and
/// <c>IsInteractable()</c> on the remembered field before attaching, so a field that was activated
/// and has since gone stale is rejected the same way it always was. Note that activation is deferred
/// by one frame inside TMP (<c>m_ShouldActivateNextUpdate</c>); the reference is sticky, so the
/// keyboard simply attaches on the frame focus actually lands — the same frame the 0.2 s sweep would
/// have found it at best, and up to 0.2 s EARLIER at worst.</para>
///
/// <para>DEGRADES TO THE OLD BEHAVIOUR, NEVER TO A BROKEN ONE. <see cref="Installed"/> is false until
/// both postfixes are actually applied; while it is false <see cref="VRKeyboard"/> keeps running the
/// old sweep, so a TMP version without these methods costs frame time again but never loses the
/// fallback opener. Nothing here touches game state: two postfixes that only write a static
/// reference, no prefix, no return-value change, no skipped original.</para>
/// </summary>
internal static class InputFieldFocusWatch
{
    private const string Name = "InputFieldFocusWatch";

    private static bool _registered;
    private static bool _degraded;
    private static TMP_InputField? _focused;

    /// <summary>
    /// True once both postfixes are live. False means <see cref="VRKeyboard"/> must keep sweeping —
    /// slow, but never blind.
    /// </summary>
    internal static bool Installed { get; private set; }

    /// <summary>
    /// The field TMP most recently activated, or null when none is live. Unity's null check covers
    /// the destroyed case (a window closing takes its fields with it), so a stale reference resolves
    /// to null on its own without a per-frame liveness scan.
    /// </summary>
    internal static TMP_InputField? Focused => _focused != null ? _focused : null;

    /// <summary>Record an activation (called from the postfix; tolerant of a null instance).</summary>
    internal static void Notice(TMP_InputField? field)
    {
        if (field != null)
            _focused = field;
    }

    /// <summary>Forget a deactivation, but only if it is the field we are holding.</summary>
    internal static void Forget(TMP_InputField? field)
    {
        if (field != null && ReferenceEquals(field, _focused))
            _focused = null;
    }

    /// <summary>
    /// Idempotent self-registration, called from <see cref="VRKeyboard.Tick"/>. Registered EARLY —
    /// unlike <see cref="KeyboardAutoHideBlock"/>, which may wait until a keyboard is wanted — because
    /// an activation that happens before the postfix is applied is an event that simply never
    /// arrives, and the watch would start life already behind the game.
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (_registered)
            return;

        Harmony? harmony = VRSession.Harmony;
        if (harmony == null)
            return;

        _registered = true; // set first: a throw must not retry-spam every frame

        // THE TWO SEAMS ARE REGISTERED INDEPENDENTLY, AND ONLY THE FIRST ONE DECIDES.
        //
        // WHY (2026-08-09 regression). They used to share one try block and one Installed flag, so
        // the DEACTIVATE seam failing to resolve — which is exactly what happened on this game's
        // TMP, see InputFieldDeactivateWatch — took the ACTIVATE seam's benefit down with it and
        // silently restored the FindObjectsOfType sweep for the whole session (measured at
        // 90-99 ms/s in the hardware log, the mod's single most expensive step).
        //
        // That coupling was never necessary. Forget() is a pure OPTIMISATION: VRKeyboard re-tests
        // isFocused && IsInteractable() on the remembered field before it attaches, so a field that
        // was deactivated without us hearing about it is rejected on the very next tick anyway. The
        // ACTIVATE postfix alone is therefore sufficient to retire the sweep, and it is the one that
        // Installed now reports. A missing deactivate seam costs one stale reference and nothing
        // else — it must never cost 9 % of the frame budget again.
        try
        {
            harmony.PatchAll(typeof(InputFieldActivateWatch));
            Installed = true;
        }
        catch (Exception e)
        {
            Degrade($"ActivateInputField registration threw: {e.Message}");
            return;
        }

        bool forgetLive;
        try
        {
            harmony.PatchAll(typeof(InputFieldDeactivateWatch));
            forgetLive = true;
        }
        catch (Exception e)
        {
            // NOT a Degrade: the sweep stays retired (see above). One line so the log still says
            // the deactivate seam is missing, because a stale reference is a real, if harmless,
            // difference in behaviour.
            forgetLive = false;
            VRLog.Warn(Name,
                $"the DeactivateInputField seam did not register ({e.Message}). This is not the "
                + "expensive half: the activation push is live, so the FindObjectsOfType sweep stays "
                + "retired. The only effect is that a field which loses focus without being "
                + "deactivated through TMP stays remembered until the next activation — and "
                + "VRKeyboard re-checks isFocused/IsInteractable before attaching, so it is rejected "
                + "there.");
        }

        VRLog.Info("WorldUI",
            $"{Name}: registered — TMP_InputField activation is now pushed to the VR keyboard" +
            (forgetLive ? " (and deactivation clears it)" : " (deactivation seam absent — see above)") +
            ". This replaces the 0.2 s FindObjectsOfType<TMP_InputField> fallback sweep, which the " +
            "hardware log measured at 90-99 ms/s (worst 23 ms in a single frame) inside a loaded " +
            "scenario — the mod's most expensive step by a wide margin, on BOTH machines of the " +
            "multiplayer session, with or without a peer.");
    }

    /// <summary>Log the first failure and thereafter stay silent.</summary>
    internal static void Degrade(string reason)
    {
        Installed = false;
        if (_degraded)
            return;
        _degraded = true;
        // HW-VERIFY (2026-09 refactor, F-74) — the fallback is the sweep this file records the
        // hardware log measuring at 90-99 ms/s, worst 23 ms in one frame, the mod's most expensive
        // step by a wide margin. Latched by _degraded: one line per session.
        VRLog.Alert(Name,
            $"disabled — {reason}. The VR keyboard falls back to its FindObjectsOfType sweep, so a " +
            "game-focused text field is still found; it costs frame time again inside a scenario.");
    }

    /// <summary>Drop the remembered field (module shutdown / hot reload).</summary>
    internal static void Clear() => _focused = null;
}

/// <summary>
/// Postfix of <c>TMP_InputField.ActivateInputField()</c> — the single entry point to focus. See
/// <see cref="InputFieldFocusWatch"/> for why the poll became a push.
/// </summary>
[HarmonyPatch(typeof(TMP_InputField), nameof(TMP_InputField.ActivateInputField))]
internal static class InputFieldActivateWatch
{
    private static void Postfix(TMP_InputField __instance) => InputFieldFocusWatch.Notice(__instance);
}

/// <summary>
/// Postfix of <c>TMP_InputField.DeactivateInputField()</c> — the single exit from focus. Scoped by
/// instance, so one field losing focus never clears another's.
///
/// <para>Resolved through <c>TargetMethod</c> rather than the attribute's type-array form because
/// TMP ships this name OVERLOADED ACROSS VERSIONS: TMP 1.x/2.0 declare <c>DeactivateInputField()</c>,
/// TMP 3.0.x declare <c>DeactivateInputField(bool clearSelection = false)</c> and NOTHING ELSE — the
/// no-argument call sites in the game bind to it through the default value, so the method with an
/// empty parameter list does not exist in the assembly at all.</para>
///
/// <para>THE 2026-08-09 REGRESSION THIS FIXES. Asking only for the empty signature is what actually
/// shipped, and on this game's TMP it resolved to null:</para>
/// <code>
/// [Warning] [InputFieldFocusWatch] disabled — method not found: TMP_InputField.DeactivateInputField().
///           The VR keyboard falls back to its FindObjectsOfType sweep …
/// [Perf] STEPS 30.0s … VRKeyboard 99.5ms/s …
/// </code>
/// <para>So the whole point of this class — killing the mod's most expensive step — was silently
/// undone at runtime, and the hardware log measured the sweep back at 90–99 ms of EVERY SECOND on
/// both machines (≈9 % of all frame time at 90 Hz, arriving as a ~20 ms stall five times a second).
/// It is not a multiplayer cost, but it is what turned an MP session — which adds a further
/// ~90 ms/s of mirrored-board work per peer — from heavy into unplayable.</para>
///
/// <para>THE FIX IS TO ASK FOR BOTH SHAPES, most specific first: the no-argument entry point when a
/// TMP version has one, otherwise the <c>(bool)</c> overload, which is the SAME single exit from
/// focus — <c>DeactivateInputField()</c> in TMP 3.0.x IS <c>DeactivateInputField(false)</c>. The
/// postfix takes only <c>__instance</c>, so it binds to either signature unchanged; Harmony passes
/// no argument it does not ask for. If NEITHER exists, this seam is simply skipped with one warning
/// and the sweep still stays retired — see <c>EnsureRegistered</c> for why this half is optional.</para>
/// </summary>
[HarmonyPatch]
internal static class InputFieldDeactivateWatch
{
    private static MethodBase? TargetMethod()
    {
        try
        {
            // Most specific first. Both shapes are the one and only exit from TMP focus, so
            // whichever this TMP declares is the right postfix seam.
            MethodInfo? off = AccessTools.Method(
                                  typeof(TMP_InputField), "DeactivateInputField", Type.EmptyTypes)
                              ?? AccessTools.Method(
                                  typeof(TMP_InputField), "DeactivateInputField", new[] { typeof(bool) });
            return off;
        }
        catch
        {
            // Deliberately silent and deliberately NOT a Degrade: this seam is the optional half
            // (see InputFieldFocusWatch.EnsureRegistered). Returning null makes Harmony throw, and
            // the caller logs the one warning — clearing Installed from in here is precisely the
            // coupling that cost 9 % of the frame budget for a whole session.
            return null;
        }
    }

    private static void Postfix(TMP_InputField __instance) => InputFieldFocusWatch.Forget(__instance);
}
