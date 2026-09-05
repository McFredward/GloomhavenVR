using BepInEx.Logging;
using UnityEngine;

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


/// <summary>
/// A CHANGE-GATED LINE THAT CANNOT GO SILENT — the shared throttle for a periodic census.
///
/// <para><b>WHY THIS EXISTS (redundancy survey R42, 2026-09-05).</b> The mod carries 20+ hand-rolled
/// log rate-limiters in at least four incompatible shapes (a deadline, a seconds delta, a
/// change-gated signature, a count cap) and <see cref="VRLog"/> offered no throttle at all, so
/// every new instrument invented one. The shape that actually carries a hazard is the CHANGE-GATED
/// one, and the hazard has a name in this project's ledger: <i>a held instrument reads as dead</i>.
/// A line that prints only when its signature changes prints ONCE in the steady state and then
/// looks exactly like a tick that has stopped — and "the instrument went quiet" is the reading that
/// has cost this project a build. Of the four change-gated censuses in the tree only
/// <c>QuestJourneyCurtain</c> carried the heartbeat that answers it, and it had to write the
/// heartbeat itself.</para>
///
/// <para><b>THE THREE GATES, AND WHY THEY ARE ONE OBJECT.</b> A census that is worth writing wants
/// all three and they interact:</para>
/// <list type="number">
///   <item><b>CHANGE</b> — print when the signature moves. What the reader is actually waiting
///   for.</item>
///   <item><b>HEARTBEAT</b> — print anyway every <c>heartbeatSeconds</c>, so silence means "the
///   tick stopped", never "nothing changed". This is the gate the hand-rolled copies kept
///   forgetting, and it is the reason this type exists rather than a lint.</item>
///   <item><b>BUDGET</b> — stop after <c>maxLines</c>, so a signature that oscillates cannot flood
///   a session. Optional; 0 means no cap.</item>
/// </list>
///
/// <para><b>WHAT IT DOES NOT DO, deliberately: the CADENCE.</b> A cadence gate bounds how often the
/// census is COMPUTED and belongs in front of the walk that builds the signature, where the caller
/// can see it. Folding it in here would put it after the expensive part and would repeat the defect
/// fixed in <c>ActorPropBody</c> on 2026-09-05, where a change test returned before its own
/// deadline was advanced and a 0.5 Hz walk ran at 90 Hz for the rest of the session. The two gates
/// answer two different questions — the cadence bounds what is WALKED, this bounds what is
/// PRINTED — and keeping them apart is what makes that hard to get wrong again.</para>
///
/// <para><b>THE VERDICT IS PART OF THE LINE.</b> <see cref="Why"/> hands the caller a clause saying
/// WHICH gate opened and how long the signature has stood, so a heartbeat line is visibly a
/// heartbeat and not a new event, and the last line before the budget runs out says so instead of
/// simply being the last one. Append it; that is the whole point of the type over a bare bool.</para>
///
/// <para>Not thread-safe and not meant to be: every caller is a Unity main-thread tick.</para>
/// </summary>
internal sealed class VRLogThrottle
{
    private readonly float _heartbeatSeconds;
    private readonly int _maxLines;

    private string _key = string.Empty;
    private long _longKey;
    private float _now;
    private bool _everEmitted;
    private float _lastAt;
    private int _lines;
    private int _suppressed;
    private string _why = string.Empty;

    /// <param name="heartbeatSeconds">Seconds after which an UNCHANGED signature prints anyway.
    /// Pick it against how long a reader will stare at a log before concluding the tick died — 15 s
    /// is the value <c>QuestJourneyCurtain</c> arrived at and a reasonable default.</param>
    /// <param name="maxLines">Cap on lines for the life of this throttle; 0 = uncapped. A capped
    /// throttle says so on its LAST line rather than simply stopping.</param>
    internal VRLogThrottle(float heartbeatSeconds, int maxLines = 0)
    {
        _heartbeatSeconds = heartbeatSeconds;
        _maxLines = maxLines;
    }

    /// <summary>Lines emitted so far.</summary>
    internal int Emitted => _lines;

    /// <summary>Is the budget spent? Ask it in FRONT of the work that builds the signature: this
    /// type gates PRINTING, and a census whose lines are all spent should stop paying for the walk
    /// as well. Always false for an uncapped throttle.</summary>
    internal bool Exhausted => _maxLines > 0 && _lines >= _maxLines;

    /// <summary>
    /// Would a heartbeat print RIGHT NOW if the signature had not changed? A read-only probe that
    /// changes nothing, for a caller whose signature is expensive to BUILD: it can run a cheap
    /// prefilter (a hash, a count) and skip the formatting entirely when the prefilter says
    /// "unchanged" AND this says "no heartbeat due". Without it a prefilter in front of this
    /// throttle would swallow the heartbeat and put back exactly the silence the heartbeat exists
    /// to prevent.
    /// </summary>
    internal bool HeartbeatDue(float now) =>
        !Exhausted && (!_everEmitted || now - _lastAt >= _heartbeatSeconds);

    /// <summary>
    /// The clause describing WHY this line is being printed, valid immediately after a
    /// <see cref="Wants(string,float)"/> that returned true. Append it to the line: it is what
    /// separates "something changed" from "nothing changed and the tick is alive", and it carries
    /// how many samples were suppressed in between.
    /// </summary>
    internal string Why => _why;

    /// <summary>
    /// Should this line print? <paramref name="signature"/> is every term whose change is worth a
    /// line and nothing else — a counter that ticks every sample belongs in the line, not in the
    /// key, or the change gate degenerates into no gate at all.
    /// </summary>
    /// <param name="now">Unscaled time, passed in so the caller's own cadence and this share one
    /// reading of the clock.</param>
    internal bool Wants(string signature, float now)
    {
        if (Exhausted)
            return false;

        _now = now;
        bool changed = !_everEmitted || signature != _key;
        float held = _everEmitted ? now - _lastAt : 0f;
        if (!changed && held < _heartbeatSeconds)
        {
            _suppressed++;
            return false;
        }

        _key = signature;
        Emit(changed, held);
        return true;
    }

    /// <summary>Commit one emission: the verdict clause, the clock, the suppressed count and the
    /// budget. Shared by both <c>Wants</c> overloads so the two cannot word a verdict differently
    /// — which is the whole complaint this type answers, one level down.</summary>
    private void Emit(bool changed, float held)
    {
        _why = !_everEmitted
            ? "first reading"
            : changed
                ? $"CHANGED after {held:0.0} s ({_suppressed} unchanged sample(s) since the last line)"
                : $"UNCHANGED for {held:0.0} s — this is the HEARTBEAT, not a new event; the line is "
                  + $"here to prove the tick is alive ({_suppressed} sample(s) since the last line)";

        _everEmitted = true;
        _lastAt = _now;
        _suppressed = 0;
        _lines++;
        if (_maxLines > 0 && _lines >= _maxLines)
            _why += $". THIS IS THE LAST LINE FROM THIS CENSUS — its budget of {_maxLines} is now "
                    + "spent, so later silence is the CAP and not the tick";
    }

    /// <summary>
    /// Integer-signature overload, for a census that folds its terms into a bit field rather than a
    /// string. Same three gates, and ALLOCATION-FREE: it compares the long directly instead of
    /// formatting it, so a caller on a tight cadence pays nothing on a suppressed sample. A single
    /// throttle should be asked with one KIND of signature; mixing them is a bug the compiler
    /// cannot see, and the two key stores below are kept apart so a mixed caller degenerates into
    /// "always changed" rather than into a silent false match.
    /// </summary>
    internal bool Wants(long signature, float now)
    {
        if (Exhausted)
            return false;

        _now = now;
        bool changed = !_everEmitted || signature != _longKey;
        float held = _everEmitted ? now - _lastAt : 0f;
        if (!changed && held < _heartbeatSeconds)
        {
            _suppressed++;
            return false;
        }

        _longKey = signature;
        Emit(changed, held);
        return true;
    }

    /// <summary>Convenience: <see cref="Wants(string,float)"/> against
    /// <see cref="Time.unscaledTime"/>.</summary>
    internal bool Wants(string signature) => Wants(signature, Time.unscaledTime);

    /// <summary>Forget everything — a new scenario is a new question, so the budget refills and the
    /// first line of the next one reads as a first reading rather than as an unchanged
    /// signature.</summary>
    internal void Reset()
    {
        _key = string.Empty;
        _longKey = 0;
        _everEmitted = false;
        _lastAt = 0f;
        _lines = 0;
        _suppressed = 0;
        _why = string.Empty;
    }
}
