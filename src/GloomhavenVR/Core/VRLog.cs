using BepInEx.Logging;

namespace GloomhavenVR.Core;

/// <summary>How much the mod writes to the log.</summary>
/// <remarks>
/// The tiers are what a READER wants, not what a writer felt like emitting. Reading down the
/// list you give up detail; you never give up the ability to see that something broke, which is
/// why <see cref="VRLogLevel.Error"/> sits directly above "silent" and everything else is layered
/// on top of it.
/// </remarks>
internal enum VRLogLevel
{
    /// <summary>Nothing at all. The mod still runs; it just stops talking.</summary>
    Off = 0,

    /// <summary>Only what actually failed — an exception, a subsystem that disarmed itself.</summary>
    Error = 1,

    /// <summary>Adds what the PLAYER can do something about: the asset bundle missing, VR failing
    /// to start, an update that could not install. Deliberately a short list.</summary>
    Warning = 2,

    /// <summary>Adds what the mod IS and what it just did: build and version, VR up and down, the
    /// room it built, the session it joined. Tens of lines in a session, not hundreds.
    /// <b>This is the default.</b></summary>
    Info = 3,

    /// <summary>Everything every subsystem says about itself. This is EXACTLY what the mod emitted
    /// before the levels were re-decided — thousands of lines, and the level to set before
    /// reproducing anything for a bug report.</summary>
    Debug = 4,
}

/// <summary>
/// Structured logging wrapper over the plugin's BepInEx <see cref="ManualLogSource"/>, with a
/// user-facing verbosity. Adds an optional scope prefix (module name) so log lines are grep-able:
/// <c>[Info   :GloomhavenVR] [Rig] ...</c>.
/// </summary>
/// <remarks>
/// <para><b>THE RE-DECISION (2026-08-30), and why it is a mapping rather than 2 500 edits.</b> The
/// user's complaint was that the log is far too verbose for an ordinary player, and the numbers say
/// he is right: <b>1 705</b> <c>Info</c>, <b>600</b> <c>Warn</c>, <b>95</b> <c>Error</c>, <b>94</b>
/// <c>Debug</c> and <b>11</b> <c>Note</c> call sites. Reading a random sample of the WARNINGS
/// settles what they are — "MODAL WINDOW: one-shot fit found nothing", "DESTINATIONS DISCOVERY
/// BASELINE DISAGREES", "MOD VERSION LABEL: NOT ACHIEVED", "could not read the room's light rig".
/// Every one of those is a subsystem reporting on itself, and not one of them is something a player
/// can act on. They are debug output that happens to be spelled "warning".</para>
///
/// <para>So the re-decision is applied WHERE THE DECISION ACTUALLY LIVES — in what each severity
/// MEANS — instead of by editing two and a half thousand call sites one at a time, which is several
/// hundred judgement calls and as many chances to silence the one line somebody later needs:</para>
/// <list type="table">
///   <listheader><term>Method</term><description>printed as / visible from</description></listheader>
///   <item><term><see cref="Error"/></term><description>LogError · <see cref="VRLogLevel.Error"/> — 95 sites, all genuine (exceptions, self-disarms). Unchanged.</description></item>
///   <item><term><see cref="Alert"/></term><description>LogWarning · <see cref="VRLogLevel.Warning"/> — <b>new.</b> The short list a player can act on.</description></item>
///   <item><term><see cref="Note"/></term><description>LogInfo · <see cref="VRLogLevel.Info"/> — what the mod is and what it did.</description></item>
///   <item><term><see cref="Warn"/></term><description>LogWarning · <see cref="VRLogLevel.Debug"/> — the 600 self-reports. Still printed AS warnings, so a debug log reads exactly as it always did.</description></item>
///   <item><term><see cref="Info"/></term><description>LogInfo · <see cref="VRLogLevel.Debug"/> — the 1 705-line running commentary.</description></item>
///   <item><term><see cref="Debug"/></term><description>LogDebug · <see cref="VRLogLevel.Debug"/> — the per-subsystem chatter.</description></item>
/// </list>
///
/// <para><b>NOTHING WAS DELETED AND NOTHING CHANGED ITS WORDING.</b> Every line the mod has ever
/// written still exists and still prints at its original BepInEx severity; what changed is the
/// level you have to ask for to see it. <c>[General] LogLevel = Debug</c> therefore reproduces the
/// old output exactly, which is the contract the user asked for in those words.</para>
///
/// <para><b>PROMOTION IS THE ONGOING WORK, and it is one word per line.</b> A line that a player
/// genuinely needs moves <c>Warn</c> → <see cref="Alert"/> or <c>Info</c> → <see cref="Note"/>.
/// That direction is safe: the worst case is one line too many in a quiet log. The opposite
/// direction — starting from "everything is important" and cutting — is the one that loses
/// evidence, and it is the reason this was not attempted as a sweep.</para>
///
/// <para>COST: a filtered call still builds its interpolated string, because arguments are
/// evaluated before the call. That is acceptable here — the chatty sites are event-driven and the
/// per-frame ones already throttle themselves — but anything genuinely hot should ask
/// <see cref="Wants"/> first rather than pay for a string nobody reads. That cost is now paid far
/// more often than before, since the default hides most of it; see <see cref="Wants"/>.</para>
/// </remarks>
internal static class VRLog
{
    private static ManualLogSource? _log;

    /// <summary>
    /// Current verbosity. Starts at <see cref="VRLogLevel.Info"/> — the shipped default — so the
    /// handful of lines written before config binds are the ones worth having either way;
    /// <c>[General] LogLevel</c> takes over as soon as config binds, and every later change is
    /// live.
    /// </summary>
    internal static VRLogLevel Level { get; set; } = VRLogLevel.Info;

    internal static void Init(ManualLogSource log) => _log = log;

    /// <summary>True when a message at <paramref name="level"/> would be written. Ask before
    /// building an expensive message; do not bother for an ordinary one.</summary>
    internal static bool Wants(VRLogLevel level) => Level >= level;

    /// <summary>True when the debug tier is on, i.e. when a subsystem's self-reporting will
    /// actually be printed. The one-word guard for a site that is expensive to FORMAT.</summary>
    internal static bool WantsDebug => Level >= VRLogLevel.Debug;

    // ---- the player-facing tiers ------------------------------------------------------------

    /// <summary>Something failed. Always the highest tier above silence.</summary>
    internal static void Error(string message)
    {
        if (Level >= VRLogLevel.Error) _log?.LogError(message);
    }

    internal static void Error(string scope, string message)
    {
        if (Level >= VRLogLevel.Error) _log?.LogError($"[{scope}] {message}");
    }

    /// <summary>
    /// A warning the PLAYER can act on — the asset bundle is missing, VR did not start, an update
    /// could not install. Printed as a warning and visible from <see cref="VRLogLevel.Warning"/>.
    ///
    /// <para>Distinct from <see cref="Warn"/> on purpose. Of the 600 warning call sites in this mod,
    /// almost all report that a subsystem did not achieve something and quietly degraded; those are
    /// debug output. This one is for the few where the player's log is the only place they would
    /// ever learn that something they care about is not working.</para>
    /// </summary>
    internal static void Alert(string message)
    {
        if (Level >= VRLogLevel.Warning) _log?.LogWarning(message);
    }

    internal static void Alert(string scope, string message)
    {
        if (Level >= VRLogLevel.Warning) _log?.LogWarning($"[{scope}] {message}");
    }

    /// <summary>A line worth keeping at <see cref="VRLogLevel.Info"/> — what the mod is, and what
    /// it just did that a reader would want confirmed. Emitted as Info, so the log FORMAT is
    /// unchanged.</summary>
    internal static void Note(string message)
    {
        if (Level >= VRLogLevel.Info) _log?.LogInfo(message);
    }

    internal static void Note(string scope, string message)
    {
        if (Level >= VRLogLevel.Info) _log?.LogInfo($"[{scope}] {message}");
    }

    // ---- the debug tier: every existing call site keeps working, unchanged ------------------

    /// <summary>A subsystem reporting on itself. Printed as a warning (so a debug log reads as it
    /// always did) but visible only from <see cref="VRLogLevel.Debug"/> — see the class remarks for
    /// why 600 of these are not player-facing.</summary>
    internal static void Warn(string message)
    {
        if (Level >= VRLogLevel.Debug) _log?.LogWarning(message);
    }

    internal static void Warn(string scope, string message)
    {
        if (Level >= VRLogLevel.Debug) _log?.LogWarning($"[{scope}] {message}");
    }

    internal static void Info(string message)
    {
        if (Level >= VRLogLevel.Debug) _log?.LogInfo(message);
    }

    internal static void Info(string scope, string message)
    {
        if (Level >= VRLogLevel.Debug) _log?.LogInfo($"[{scope}] {message}");
    }

    internal static void Debug(string message)
    {
        if (Level >= VRLogLevel.Debug) _log?.LogDebug(message);
    }

    internal static void Debug(string scope, string message)
    {
        if (Level >= VRLogLevel.Debug) _log?.LogDebug($"[{scope}] {message}");
    }
}
