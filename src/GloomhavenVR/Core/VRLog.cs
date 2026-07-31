using BepInEx.Logging;

namespace GloomhavenVR.Core;

/// <summary>How much the mod writes to the log.</summary>
/// <remarks>
/// The tiers are what a READER wants, not what a writer felt like emitting. Reading down the
/// list you give up detail; you never give up the ability to see that something broke, which is
/// why <see cref="VRLogLevel.Errors"/> sits directly above "silent" and everything else is
/// layered on top of it.
/// </remarks>
internal enum VRLogLevel
{
    /// <summary>Nothing at all. The mod still runs; it just stops talking.</summary>
    Off = 0,

    /// <summary>Only what actually failed.</summary>
    Errors = 1,

    /// <summary>Failures plus what degraded silently — a missing asset, a fallback that engaged.
    /// This is the floor at which a bug report is still worth reading.</summary>
    Warnings = 2,

    /// <summary>Adds the handful of lines that say what the mod IS: build, VR state, which
    /// runtime, modules up and down. Enough to answer "is it running, and as what".</summary>
    Normal = 3,

    /// <summary>Adds the running commentary every subsystem writes — several hundred lines a
    /// session. This is what the mod has always emitted.</summary>
    Verbose = 4,

    /// <summary>Everything, including the per-subsystem debug chatter.</summary>
    Trace = 5,
}

/// <summary>
/// Structured logging wrapper over the plugin's BepInEx <see cref="ManualLogSource"/>, with a
/// user-facing verbosity. Adds an optional scope prefix (module name) so log lines are grep-able:
/// <c>[Info   :GloomhavenVR] [Rig] ...</c>.
/// </summary>
/// <remarks>
/// <para>THE MAPPING IS DELIBERATELY BLUNT, and that is what makes it safe. There are 632
/// <c>Info</c>, 167 <c>Warn</c>, 48 <c>Error</c> and 54 <c>Debug</c> call sites. Re-classifying
/// them one at a time would be several hundred judgement calls, each one a chance to silence
/// exactly the line someone later needs. So the existing severities keep their meaning and simply
/// gain a tier: <c>Error</c> → Errors, <c>Warn</c> → Warnings, <c>Info</c> → Verbose,
/// <c>Debug</c> → Trace. At the default (<see cref="VRLogLevel.Trace"/>) the output is exactly
/// what it was before this existed — no call site changed.</para>
///
/// <para><see cref="Note"/> is the one addition: the "Normal" tier would otherwise be empty,
/// because nothing in the mod was ever written as "a user should see this". It is used for a
/// deliberately tiny set of lines — the build banner, VR coming up and going down. Promoting more
/// is a one-word edit per line, and cheaper to do on demand than to guess at now.</para>
///
/// <para>COST: a filtered call still builds its interpolated string, because arguments are
/// evaluated before the call. That is acceptable here — the chatty sites are event-driven and the
/// per-frame ones already throttle themselves — but anything genuinely hot should ask
/// <see cref="Wants"/> first rather than pay for a string nobody reads.</para>
/// </remarks>
internal static class VRLog
{
    private static ManualLogSource? _log;

    /// <summary>
    /// Current verbosity. Defaults to <see cref="VRLogLevel.Trace"/> so the mod behaves exactly as
    /// it always has until something says otherwise; <c>[General] LogLevel</c> takes over as soon
    /// as config binds, and every later change is live.
    /// </summary>
    internal static VRLogLevel Level { get; set; } = VRLogLevel.Trace;

    internal static void Init(ManualLogSource log) => _log = log;

    /// <summary>True when a message at <paramref name="level"/> would be written. Ask before
    /// building an expensive message; do not bother for an ordinary one.</summary>
    internal static bool Wants(VRLogLevel level) => Level >= level;

    // ---- severities: every existing call site keeps working unchanged -----------------------

    internal static void Debug(string message)
    {
        if (Level >= VRLogLevel.Trace) _log?.LogDebug(message);
    }

    internal static void Info(string message)
    {
        if (Level >= VRLogLevel.Verbose) _log?.LogInfo(message);
    }

    internal static void Warn(string message)
    {
        if (Level >= VRLogLevel.Warnings) _log?.LogWarning(message);
    }

    internal static void Error(string message)
    {
        if (Level >= VRLogLevel.Errors) _log?.LogError(message);
    }

    /// <summary>A line worth keeping at <see cref="VRLogLevel.Normal"/> — what the mod is and
    /// whether it is running. Emitted as Info, so the log FORMAT is unchanged.</summary>
    internal static void Note(string message)
    {
        if (Level >= VRLogLevel.Normal) _log?.LogInfo(message);
    }

    internal static void Debug(string scope, string message)
    {
        if (Level >= VRLogLevel.Trace) _log?.LogDebug($"[{scope}] {message}");
    }

    internal static void Info(string scope, string message)
    {
        if (Level >= VRLogLevel.Verbose) _log?.LogInfo($"[{scope}] {message}");
    }

    internal static void Warn(string scope, string message)
    {
        if (Level >= VRLogLevel.Warnings) _log?.LogWarning($"[{scope}] {message}");
    }

    internal static void Error(string scope, string message)
    {
        if (Level >= VRLogLevel.Errors) _log?.LogError($"[{scope}] {message}");
    }

    internal static void Note(string scope, string message)
    {
        if (Level >= VRLogLevel.Normal) _log?.LogInfo($"[{scope}] {message}");
    }
}
