using System;
using System.Collections.Generic;
using System.Text;
using FFSNet;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>
/// INSTRUMENT for the multiplayer scenario-entry deadlock reported on 2026-09-05: <i>"Mein
/// Mitspieler konnte nicht erfolgreich in das Szenario laden. […] das Ladesymbol ist bei ihm nicht
/// verschwunden und in flat sah man 'Waiting for other players' war stuck und ging nicht weiter."</i>
///
/// <para><b>THE DEFECT IS THE GAME'S, AND IT IS A LOST-MESSAGE RACE.</b> Entering a scenario runs a
/// two-sided loading handshake in <c>FFSNet.PlayerRegistry</c>:</para>
/// <list type="number">
/// <item>Each client calls <c>StartWaitingForPlayers()</c> (PlayerRegistry.cs:377-392), which sets
///   <c>WaitForOtherPlayers = true</c> AND — one statement later — <c>PlayersFinishedLoading.Clear()</c>.</item>
/// <item>When its own scenario load finishes, each client broadcasts the
///   <c>NotifyLoadingFinished</c> side action and adds ITSELF to <c>PlayersFinishedLoading</c>
///   (UnityGameEditorRuntime.cs:435-442).</item>
/// <item><c>CheckLoadingFinished()</c> (PlayerRegistry.cs:408-414) releases the wait only when
///   <c>Participants.Count &lt;= PlayersFinishedLoading.Count</c>. Until then
///   <c>SceneController.WaitForPlayers()</c> (SceneController.cs:2572-2581) spins on
///   <c>yield return null</c> behind the <c>WaitingForPlayers</c> loading screen — the
///   <c>GUI_LOADING_WAITING_FOR_PLAYERS</c> string the player read.</item>
/// </list>
///
/// <para><b>THE INVARIANT THAT BREAKS IT.</b> <c>WaitForOtherPlayers</c> has exactly ONE
/// true-assignment in the whole game (PlayerRegistry.cs:385) and it lives inside
/// <c>StartWaitingForPlayers()</c>, immediately followed by the <c>Clear()</c>. Therefore:
/// <b>every <c>NotifyLoadingFinished</c> that is processed while <c>WaitForOtherPlayers</c> is
/// FALSE is unconditionally discarded.</b> <c>CheckLoadingFinished()</c> is a no-op while the flag
/// is false, and the list entry it just added cannot survive the next <c>Clear()</c>. Nothing
/// re-requests it and nothing re-sends it, so the client that lost the message waits forever.</para>
///
/// <para><b>HOW THAT HAPPENS IN PRACTICE.</b> The host runs <c>MapChoreographer.EnterScenario()</c>
/// (MapChoreographer.cs:2230-2246) — which opens ITS wait window — and only then sends the
/// <c>EnterScenario</c> game action. The client must first drain its action queue before it reaches
/// that action and opens its OWN window. If the host's whole scenario load finishes inside that
/// queue lag, the host's notification lands in the client's <i>closed</i> window and is wiped.
/// In the 2026-09-05 logs the margin was about 0.8 s: the host broadcast ~0.97 s after entering,
/// the client dequeued <c>EnterScenario</c> ~1.76 s after receiving it, and the client's
/// <c>Processing SideAction</c> line sits NINE lines above its own <c>WaitForOtherPlayers</c> flip.</para>
///
/// <para><b>THIS CLASS ALSO REPAIRS IT, AND THAT IS AN EXPLICIT EXCEPTION.</b> Re-delivery means
/// writing <c>PlayerRegistry.PlayersFinishedLoading</c> — game network state, which this project
/// otherwise forbids. The user was asked and approved it for this one repair, on one condition
/// that is also the boundary this code holds: <b>we are not inventing a fact, we are restoring one
/// the game itself established and then discarded through its own race.</b> So the only thing ever
/// handed back is a <c>NetworkPlayer</c> reference the GAME put in that list, and the game puts one
/// there only from a <c>NotifyLoadingFinished</c> a peer really sent. Nothing is constructed,
/// resolved by id, or inferred from a peer's silence; the LOCAL player is never restored, because
/// this client's own readiness is a fact only its own load path may assert. Still NO Harmony patch
/// on anything, and still NO time limit — the repair fires on the ORDERING, in the frame it is
/// detected (<i>"Ich will gar keine Zeitlimits dieser Art."</i>). See
/// <see cref="RestoreDiscardedNotifications"/> for the idempotence argument and
/// <see cref="SnapshotDoomed"/>'s call site for why a wrong assumption yields inaction.</para>
///
/// <para><b>WHY IT READS THE LOG STREAM AND NOT THE STATE.</b> A per-frame state probe cannot see
/// this defect at all. The side action is processed on Bolt's event pump and the flag is flipped by
/// the action queue, both inside the same <c>Update</c> — so <c>PlayersFinishedLoading</c> can go
/// 0 → 1 → 0 between two samples and every poll reads zero. The game logs both edges through
/// <c>FFSNet.Console</c>, which calls <c>UnityEngine.Debug.Log</c> (Console.cs:32-48), so
/// <c>Application.logMessageReceived</c> delivers them IN ORDER with no sampling at all. The
/// ordering IS the verdict.</para>
///
/// <para><b>WHAT IT PRINTS</b> — one line per handshake edge, never per frame. Every line carries
/// the unconditional liveness field <c>[watch: ticks=… edges=… notifies=…]</c>, so a watch that is
/// alive and has nothing to say is distinguishable from one that never armed or died.</para>
/// <list type="bullet">
/// <item><b>WAIT OPENED</b> — the window opened; how many notifications had already arrived and
///   were therefore just discarded. A non-zero count there IS the deadlock, named at the instant
///   it is created rather than after the player has waited.</item>
/// <item><b>WAIT CLOSED</b> — the healthy edge, with how long it took.</item>
/// <item><b>REPAIR</b> / <b>REPAIR FAILED</b> — which participant was restored, how many of how
///   many the roster then held, and (on the close) whether the loading screen actually came down.</item>
/// <item><b>STILL WAITING</b> — past a plain observation bound, naming the participants that are
///   NOT in <c>PlayersFinishedLoading</c> and what the player can do about it. If the repair fired
///   and the client is STILL waiting, this line says so in those words — a repair that ran and did
///   not release must never read as silence.</item>
/// </list>
///
/// <para><b>THE ESCAPE ROUTE STAYS.</b> Everything <see cref="Escape"/> says about the host leaving
/// and about the mod's options button is still printed on every failure line. A repair that works
/// is not a reason to delete the way out.</para>
/// </summary>
internal static class ScenarioEntryWatch
{
    private const string Scope = "Net";

    /// <summary>The game tokens this watch keys on, from the decompiled sources. These are the
    /// GAME's strings, never reworded, and no line this class emits contains any of them — a mod
    /// line that quotes another instrument's grep token counts itself.</summary>
    private const string FlagToken = "WaitForOtherPlayers set to: ";
    private const string FlagTrue = "WaitForOtherPlayers set to: True";
    private const string SideActionToken = "Processing SideAction (NotifyLoadingFinished)";

    /// <summary>Skip anything this mod itself wrote, so the watch can never observe its own output.
    /// BepInEx's logger does not route into Unity's, but the callback is cheap enough to say so
    /// explicitly rather than rely on that.</summary>
    private const string SelfToken = "GloomhavenVR";

    /// <summary>First observation line once a window has been open this long. Purely a reporting
    /// cadence — nothing about the wait changes when it elapses.</summary>
    private const float FirstReportSeconds = 20f;

    /// <summary>Spacing of the follow-up observation lines, and the slower spacing taken after
    /// <see cref="LoudLinesBeforeSlowing"/> of them. It slows down; it never goes silent — a cap
    /// that stops printing turns a standing deadlock into an empty log.</summary>
    private const float RepeatSeconds = 30f;
    private const float SlowRepeatSeconds = 300f;
    private const int LoudLinesBeforeSlowing = 10;

    // ---- state --------------------------------------------------------------------------------
    private static Driver? _driver;
    private static bool _hooked;

    /// <summary>Authoritative from the log stream, NOT from polling the property — the ordering
    /// against the side-action line is the whole measurement.</summary>
    private static bool _waitOpen;
    private static bool _seeded;

    private static float _openedAt;
    private static float _nextReportAt;
    private static int _loudLines;

    /// <summary>Notifications processed while the window was CLOSED, i.e. the ones the game's own
    /// <c>Clear()</c> is guaranteed to discard. Reset when a window opens (it is reported there).</summary>
    private static int _discarded;

    /// <summary>Notifications processed while the window was OPEN — the ones that count.</summary>
    private static int _inWindow;

    // ---- liveness (unconditional, printed on every line) ---------------------------------------
    private static long _ticks;
    private static int _edges;
    private static int _notifies;

    /// <summary>Set from the log callback, consumed by the next Update. Nothing logs from inside
    /// the callback — that would re-enter Unity's logger.</summary>
    private static int _pendingOpen;
    private static int _pendingClose;
    private static bool _emitting;

    /// <summary>Scratch for the missing-participant list. Only ever touched on an edge or on the
    /// observation cadence, never on the per-frame path.</summary>
    private static readonly List<string> Missing = [];

    /// <summary>The game's <c>NetworkPlayer</c> references read out of <c>PlayersFinishedLoading</c>
    /// in the instant before the game clears it — held as <see cref="object"/> because the type
    /// derives from a Bolt type this project does not reference. Filled ONLY from the log callback
    /// on a window-opening edge, consumed ONLY by the next <c>Tick</c>, and emptied either way.</summary>
    private static readonly List<object> Doomed = [];

    /// <summary>How the repair went for the CURRENT window. One window, one attempt.</summary>
    private enum RepairState
    {
        /// <summary>Nothing was discarded, or the repair has not run yet.</summary>
        None,
        /// <summary>At least one entry was put back.</summary>
        Restored,
        /// <summary>Entries were discarded but none could be put back — the loud failure.</summary>
        Failed
    }

    private static RepairState _repairState;
    private static int _restoredCount;
    private static int _restoreDropped;
    private static string _restoredNames = string.Empty;
    private static string _restoreFailure = string.Empty;

    /// <summary>The game's own "the curtain came down" edge (SceneController.cs:1123), counted so a
    /// repair can report whether the loading screen ACTUALLY lifted afterwards rather than only
    /// that a list got longer.</summary>
    private const string LoadingScreenDownToken = "Disabling loading screen.";
    private static int _screenDownSeen;
    private static int _screenDownAtOpen;

    // ---- lifecycle -----------------------------------------------------------------------------

    internal static void Install()
    {
        if (_driver != null)
            return;

        try
        {
            var go = new GameObject("GloomhavenVR.ScenarioEntryWatch") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<Driver>();

            Application.logMessageReceived += OnLogMessage;
            _hooked = true;
        }
        catch (Exception e)
        {
            VRLog.Alert(Scope, "SCENARIO ENTRY WATCH could not arm — "
                + $"{e.GetType().Name}: {e.Message}. The multiplayer loading handshake is "
                + "UNOBSERVED this session, so a client that hangs on the waiting-for-players "
                + "loading screen will produce no line naming why.");
            return;
        }

        VRLog.Note(Scope, "SCENARIO ENTRY WATCH armed — the game's multiplayer loading handshake "
            + "is read from its own ordered log stream (never polled, because the two edges can "
            + "fall in one frame). It reports the moment the wait window opens, the moment it "
            + "closes, and — the defect this exists for — any peer 'finished loading' "
            + "notification that arrived BEFORE the window opened and was therefore discarded by "
            + "the game's own PlayersFinishedLoading.Clear(). One such notification is a "
            + "permanent hang: nothing re-sends it, so this watch hands it back — to the game's "
            + "OWN PlayerRegistry.NotifyLoadingFinished, with the very reference the game had "
            + "discarded. That single write is an approved exception to 'never write game state': "
            + "it restores a fact the game established, never invents one, and is refused for this "
            + "client's own player. Grep 'SCENARIO ENTRY'. No Harmony patch, no time limit and no "
            + "deadline anywhere in it — the repair fires on the ordering, not on a clock.");
    }

    internal static void Uninstall()
    {
        if (_hooked)
        {
            Application.logMessageReceived -= OnLogMessage;
            _hooked = false;
        }
        if (_driver != null)
        {
            UnityEngine.Object.Destroy(_driver.gameObject);
            _driver = null;
        }
        _waitOpen = false;
        _seeded = false;
        _pendingOpen = _pendingClose = 0;
        _discarded = _inWindow = 0;
        _loudLines = 0;
        _repairState = RepairState.None;
        _restoredCount = _restoreDropped = 0;
        _restoredNames = _restoreFailure = string.Empty;
        _screenDownSeen = _screenDownAtOpen = 0;
        Missing.Clear();
        // Nothing restored needs undoing: every entry handed back was one the game itself had put
        // in that list, and the list is the game's own transient loading bookkeeping, cleared by
        // PlayerRegistry on the next window either way.
        Doomed.Clear();
    }

    // ---- the observation ------------------------------------------------------------------------

    private sealed class Driver : MonoBehaviour
    {
        private Action? _tick;
        private void Awake() => _tick = Tick;
        private void Update() => TickGuard.Run("Compat.ScenarioEntryWatch", _tick!, Scope);
    }

    /// <summary>
    /// The per-frame path, and it is deliberately almost nothing: two int compares and — only while
    /// a window is open — one float compare. No allocation, no LINQ, no game call. Everything that
    /// costs anything happens on an edge or on the observation cadence.
    /// </summary>
    private static void Tick()
    {
        _ticks++;

        // Seed once, so a watch installed into an ALREADY-OPEN window does not report a phantom
        // edge later. The property read is the only place polling is used, and it decides nothing
        // except the starting value.
        if (!_seeded)
        {
            _seeded = true;
            try
            {
                _waitOpen = PlayerRegistry.WaitForOtherPlayers;
                if (_waitOpen)
                {
                    _openedAt = Time.realtimeSinceStartup;
                    _nextReportAt = _openedAt + FirstReportSeconds;
                }
            }
            catch (Exception e)
            {
                VRLog.Alert(Scope, "SCENARIO ENTRY WATCH: the initial handshake read threw "
                    + $"{e.GetType().Name} ({e.Message}) — the watch continues from the log "
                    + "stream alone, which is the authoritative source anyway.");
            }
        }

        // EVERY STATE WRITE THIS CLASS MAKES OUTSIDE THE LOG CALLBACK LIVES HERE, in the mechanism,
        // and the three Report* methods below are pure text. That split is not tidiness: a write
        // buried inside a diagnostic is the shape that once nearly latched the wall fade off
        // forever, and scripts/check-instrument-writes.py refuses a new one. It also means every
        // one of those methods could be deleted tomorrow without changing what this watch DOES.
        if (_pendingOpen > 0)
        {
            _pendingOpen = 0;
            int discarded = _discarded;
            _discarded = 0;
            _emitting = true;
            try { ReportOpened(discarded); }
            finally { _emitting = false; }

            // THE REPAIR RUNS HERE, from Update — never from the log callback. Inside the callback
            // we sit BEFORE PlayerRegistry's own Clear(), so anything handed back there would be
            // wiped one statement later by the very call we are observing. By this point the
            // Clear() has run (it is in the same synchronous call stack that produced the log
            // line), so what we put back stays. There is no timer here and no deadline: the repair
            // fires on the ORDERING the watch already proved, in the same frame it detects it.
            int doomed = Doomed.Count;
            if (doomed > 0)
            {
                int restored = RestoreDiscardedNotifications();
                Doomed.Clear();
                _repairState = restored > 0 ? RepairState.Restored : RepairState.Failed;
                _emitting = true;
                try { ReportRepair(doomed, restored); }
                finally { _emitting = false; }
            }
            else
            {
                Doomed.Clear();
            }
        }

        if (_pendingClose > 0)
        {
            _pendingClose = 0;
            float held = Time.realtimeSinceStartup - _openedAt;
            int counted = _inWindow;
            _inWindow = 0;
            _loudLines = 0;
            _emitting = true;
            try { ReportClosed(held, counted); }
            finally { _emitting = false; }
        }

        if (!_waitOpen)
            return;

        if (Time.realtimeSinceStartup < _nextReportAt)
            return;

        _loudLines++;
        _nextReportAt = Time.realtimeSinceStartup
            + (_loudLines >= LoudLinesBeforeSlowing ? SlowRepeatSeconds : RepeatSeconds);
        float openFor = Time.realtimeSinceStartup - _openedAt;
        _emitting = true;
        try { ReportStillWaiting(_loudLines, openFor); }
        finally { _emitting = false; }
    }

    // ---- the log-stream observation ---------------------------------------------------------------

    /// <summary>
    /// Runs on Unity's log callback for EVERY message, so it does the cheapest thing possible first
    /// and NEVER logs from in here. The ordering of the two tokens within this stream is the entire
    /// measurement — a state probe cannot reproduce it, because both edges can land in one frame.
    /// </summary>
    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Log || condition == null || condition.Length == 0)
            return;
        if (_emitting)
            return;
        if (condition.IndexOf(SelfToken, StringComparison.Ordinal) >= 0)
            return;

        try
        {
            if (condition.IndexOf(SideActionToken, StringComparison.Ordinal) >= 0)
            {
                _notifies++;
                // THE VERDICT, DECIDED HERE AND NOWHERE ELSE. WaitForOtherPlayers has exactly one
                // true-assignment in the game and it is followed immediately by
                // PlayersFinishedLoading.Clear(), so a notification seen while the window is closed
                // cannot survive to be counted. This is the only place that ordering is visible.
                if (_waitOpen)
                    _inWindow++;
                else
                    _discarded++;
                return;
            }

            if (condition.IndexOf(LoadingScreenDownToken, StringComparison.Ordinal) >= 0)
            {
                _screenDownSeen++;
                return;
            }

            if (condition.IndexOf(FlagToken, StringComparison.Ordinal) < 0)
                return;

            bool open = condition.IndexOf(FlagTrue, StringComparison.Ordinal) >= 0;
            if (open == _waitOpen)
                return;

            _waitOpen = open;
            _edges++;
            if (open)
            {
                // THE ONLY MOMENT THE DOOMED ENTRIES ARE READABLE, and the whole repair hangs on
                // it. StartWaitingForPlayers (PlayerRegistry.cs:377-392) runs
                //     WaitForOtherPlayers = true;      // <- the setter logs, so WE ARE HERE
                //     PlayersFinishedLoading.Clear();  // <- has NOT run yet
                // and Application.logMessageReceived is dispatched synchronously on the main
                // thread, so this callback sits BETWEEN those two statements with nothing else in
                // between. What the list holds right now is exactly what the game is about to
                // throw away — the game's own NetworkPlayer references, put there by its own
                // NotifyLoadingFinished from a side action a peer really sent. We copy the
                // references out; we never construct, look up or infer a player.
                //
                // IF THAT ORDERING IS EVER WRONG the list reads EMPTY and the repair simply never
                // fires. Every way this assumption can break — the Clear() moving ahead of the
                // log, Unity dispatching the callback late, the token changing — lands on
                // "do nothing", never on "release the wait". That one-directional failure is why
                // this seam was chosen over a patch.
                SnapshotDoomed();
                _openedAt = Time.realtimeSinceStartup;
                _nextReportAt = _openedAt + FirstReportSeconds;
                _loudLines = 0;
                _inWindow = 0;
                _repairState = RepairState.None;
                _screenDownAtOpen = _screenDownSeen;
                _pendingOpen = 1;
            }
            else
            {
                _pendingClose = 1;
            }
        }
        catch (Exception e)
        {
            // A guard that swallows an exception on the network path is how a deadlock is
            // manufactured, so it is recorded — but not from inside the callback. The next Update
            // has nothing to consume, so the cheapest honest thing is to drop the watch's claim to
            // this edge rather than pretend it saw one.
            _pendingOpen = _pendingClose = 0;
            _lastCallbackError = e.GetType().Name + ": " + e.Message;
        }
    }

    private static string? _lastCallbackError;

    // ---- the reports -------------------------------------------------------------------------------

    /// <summary>Pure text. Every field it touches is READ; the scheduling and the re-entrancy
    /// guard belong to <see cref="Tick"/>.</summary>
    private static void ReportOpened(int discarded)
    {
        string liveness = Liveness();
        string role = Role();
        string roster = Roster();

        if (discarded == 0)
        {
            VRLog.Note(Scope, "SCENARIO ENTRY: the loading handshake window OPENED on this "
                + $"client ({role}). {roster} No peer notification had arrived before it, so "
                + "nothing was discarded and this entry is on the healthy path. " + liveness);
            return;
        }

        // HW-VERIFY: a non-zero discarded count is the 2026-09-05 deadlock, named at the
        // instant the game creates it. If a client hangs on the waiting-for-players screen and
        // this line did NOT appear, the cause is something else and the diagnosis is wrong.
        VRLog.Note(Scope, "SCENARIO ENTRY DEADLOCK: the loading handshake window OPENED on "
            + $"this client ({role}) and {discarded} peer 'finished loading' notification(s) "
            + "had ALREADY been processed before it opened. The game just discarded every one "
            + "of them: PlayerRegistry.StartWaitingForPlayers sets WaitForOtherPlayers and "
            + "then clears PlayersFinishedLoading one statement later "
            + "(PlayerRegistry.cs:377-392), and that flag has no other true-assignment "
            + "anywhere, so a notification seen while the window was shut cannot survive. "
            + "THIS CLIENT WILL NOW WAIT FOREVER: nothing re-requests the lost notification "
            + "and nothing re-sends it, the release test needs "
            + "Participants.Count <= PlayersFinishedLoading.Count (PlayerRegistry.cs:408-414), "
            + "and SceneController.WaitForPlayers spins on it with no deadline "
            + "(SceneController.cs:2572-2581). " + roster + " " + Escape() + " " + liveness);
    }

    /// <summary>Pure text; <see cref="Tick"/> owns every field it reads.</summary>
    private static void ReportRepair(int doomed, int restored)
    {
        if (restored > 0)
        {
            // HW-VERIFY: the repair fired. If the client STILL sits on the waiting-for-players
            // screen after this line, the re-delivery is not sufficient and the next line to read
            // is SCENARIO ENTRY STILL WAITING, which will say so in those words.
            VRLog.Note(Scope, $"SCENARIO ENTRY REPAIR: restored {restored} of {doomed} discarded "
                + $"notification(s) — {_restoredNames}. {Roster()} Each one was handed back to the "
                + "game's own PlayerRegistry.NotifyLoadingFinished with the SAME NetworkPlayer "
                + "reference the game itself had put in the list, so this restores a fact the game "
                + "established and then discarded through its own race — it does not invent one. "
                + (_restoreDropped > 0
                    ? $"{_restoreDropped} entr(y/ies) were deliberately NOT restored (this client's "
                      + "own player, or no longer a participant); withholding one can only make "
                      + "the wait longer, never shorter. "
                    : string.Empty)
                + "Watch for the window to CLOSE next. " + Liveness());
            return;
        }

        VRLog.Alert(Scope, $"SCENARIO ENTRY REPAIR FAILED: {doomed} notification(s) were discarded "
            + "and NONE could be put back"
            + (_restoreFailure.Length > 0 ? $" — {_restoreFailure}." : ".")
            + (_restoreDropped > 0
                ? $" {_restoreDropped} were withheld on purpose (this client's own player, or no "
                  + "longer a participant)."
                : string.Empty)
            + " THIS CLIENT IS NOW IN THE 2026-09-05 DEADLOCK WITH NO REMEDY LEFT. " + Roster()
            + " " + Escape() + " " + Liveness());
    }

    private static void ReportClosed(float held, int counted)
    {
        VRLog.Note(Scope, "SCENARIO ENTRY: the loading handshake window CLOSED after "
            + $"{held:F1}s — every participant was accounted for. {counted} peer "
            + "notification(s) arrived while it was open and counted."
            + (_repairState == RepairState.Restored
                ? $" THE REPAIR IS WHAT RELEASED IT: {_restoredCount} restored notification(s) "
                  + "carried this window, and the game's loading screen came down "
                  + (_screenDownSeen > _screenDownAtOpen
                      ? "as well — the curtain lifted, which is the outcome the player actually sees."
                      : "NOT YET, so the list is satisfied but the screen is still up: read the "
                        + "next lines rather than calling this fixed.")
                : string.Empty)
            + " " + Liveness());
    }

    private static void ReportStillWaiting(int sequence, float held)
    {
        string missing = MissingParticipants();

        VRLog.Alert(Scope, $"SCENARIO ENTRY STILL WAITING ({sequence}) — this client has "
            + $"held the multiplayer loading handshake open for {held:F0}s. THE TERM THAT IS "
            + $"NOT SATISFIED: {missing} The wait is released only by "
            + "Participants.Count <= PlayersFinishedLoading.Count, and nothing re-sends a "
            + "notification that was already consumed. "
            + _repairState switch
            {
                // The worst outcome this feature can produce: the repair ran, the roster was
                // satisfied, and the player is STILL looking at the curtain. It must never read as
                // silence, so it gets its own sentence and its own tier.
                RepairState.Restored =>
                    $"THE REPAIR ALREADY FIRED THIS WINDOW and put {_restoredCount} notification(s) "
                    + "back, and the wait did NOT end — so re-delivery is not the whole story and "
                    + "something else is holding this client. That is a NEW finding, not the known "
                    + "race. ",
                RepairState.Failed =>
                    "The repair fired and could restore nothing, so this is the known lost-message "
                    + "race with no remedy left. ",
                _ =>
                    "No notification was discarded before this window opened, so the peer has "
                    + "genuinely not reported in yet; a slow load still ends normally. "
            }
            + Escape()
            + " This line is an OBSERVATION on a cadence and changes nothing — no deadline is "
            + "imposed on the wait. It slows to one line every "
            + $"{SlowRepeatSeconds:F0}s after {LoudLinesBeforeSlowing} of them and never stops. "
            + Liveness());
    }

    // ---- shared text -------------------------------------------------------------------------------

    private static string Escape() =>
        "WHAT THE PLAYER CAN DO: the game gives this client no way out by itself — "
        + "UnityGameEditorRuntime.WaitForLoad disables ALL of the game's key actions for the whole "
        + "wait (RequestDisableInput with EKeyActionTag.All, UnityGameEditorRuntime.cs:433) and "
        + "only re-enables them after the loading screen comes down, which this path never "
        + "reaches. The mod's own options button does NOT go through that gate — it is driven by "
        + "the mod's own OpenXR input and opens the pause menu directly — so it is the first thing "
        + "to try. The reliable exit is on the OTHER side: the deadlock is one-sided, the peer "
        + "whose window opened first is NOT stuck, and it can return to the main menu and re-host; "
        + "the stuck client then rejoins and nothing is lost, because the scenario never started.";

    private static string Role()
    {
        try
        {
            if (!FFSNetwork.IsOnline)
                return "offline — this handshake should not be running at all";
            return FFSNetwork.IsHost ? "host" : "client";
        }
        catch (Exception e)
        {
            return $"role unreadable, {e.GetType().Name}";
        }
    }

    // ---- reflected roster access -------------------------------------------------------------------
    //
    // FFSNet.NetworkPlayer derives from Photon Bolt's EntityBehaviour<IPlayerState>, and this
    // project does not reference the bolt assembly (nor may it patch Bolt at all). So the type is
    // never NAMED here: the two rosters are read as IList and each entry's id/name through
    // PropertyInfo, the same seam Net/NetPlayerActors.cs already uses. Resolved once; if any handle
    // is missing the roster degrades to counts, and the verdict — which is decided entirely by the
    // ORDERING of the two log tokens — is unaffected either way.

    private static bool _reflected;
    private static System.Reflection.PropertyInfo? _myPlayerProp;
    private static System.Reflection.MethodInfo? _notifyMethod;
    private static System.Reflection.PropertyInfo? _participantsProp;
    private static System.Reflection.FieldInfo? _finishedField;
    private static System.Reflection.FieldInfo? _allPlayersField;
    private static System.Reflection.PropertyInfo? _playerIdProp;
    private static System.Reflection.PropertyInfo? _usernameProp;

    private static void Reflect()
    {
        if (_reflected)
            return;
        _reflected = true;
        try
        {
            Type registry = typeof(PlayerRegistry);
            _participantsProp = registry.GetProperty("Participants");
            _myPlayerProp = registry.GetProperty("MyPlayer");
            _notifyMethod = registry.GetMethod("NotifyLoadingFinished");
            _finishedField = registry.GetField("PlayersFinishedLoading");
            _allPlayersField = registry.GetField("AllPlayers");

            Type? player = HarmonyLib.AccessTools.TypeByName("FFSNet.NetworkPlayer");
            _playerIdProp = player?.GetProperty("PlayerID");
            _usernameProp = player?.GetProperty("Username");
        }
        catch (Exception e)
        {
            VRLog.Alert(Scope, "SCENARIO ENTRY WATCH: the roster handles could not be resolved — "
                + $"{e.GetType().Name}: {e.Message}. Its lines will still name WHEN the handshake "
                + "opened, closed and how many notifications were discarded; only the player names "
                + "beside those counts are lost.");
        }
    }

    private static System.Collections.IList? ListOf(object? source) => source as System.Collections.IList;

    /// <summary>
    /// Copy the references out of <c>PlayersFinishedLoading</c> in the instant before the game
    /// clears it. Called ONLY from the log callback on a window-opening edge — see the long note at
    /// that call site for why this is the only moment they exist and why a wrong assumption here
    /// yields an empty list rather than a false one.
    /// </summary>
    private static void SnapshotDoomed()
    {
        Doomed.Clear();
        try
        {
            Reflect();
            System.Collections.IList? finished = ListOf(_finishedField?.GetValue(null));
            if (finished == null)
                return;
            foreach (object? entry in finished)
                if (entry != null)
                    Doomed.Add(entry);
        }
        catch (Exception e)
        {
            Doomed.Clear();
            _lastCallbackError = "snapshot " + e.GetType().Name + ": " + e.Message;
        }
    }

    /// <summary>
    /// THE REPAIR. Puts back the notifications the game established and then discarded through its
    /// own race, by handing each doomed reference to the game's OWN entry point,
    /// <c>PlayerRegistry.NotifyLoadingFinished(NetworkPlayer)</c> (PlayerRegistry.cs:399-406).
    ///
    /// <para>WHAT IT WILL AND WILL NOT DO. It restores only a reference the GAME ITSELF put in
    /// <c>PlayersFinishedLoading</c>, which the game does only from a <c>NotifyLoadingFinished</c>
    /// action a peer really sent. It never constructs a player, never resolves one by id, never
    /// treats silence as readiness, and never restores the LOCAL player — this client's own
    /// readiness is a fact only this client's own load path may assert, and it adds itself through
    /// that path anyway. An entry that is no longer a participant, or no longer in
    /// <c>AllPlayers</c>, is dropped and counted rather than restored.</para>
    ///
    /// <para>IDEMPOTENCE, three independent ways. (1) <see cref="Doomed"/> is filled only on a
    /// window-opening edge and emptied here unconditionally, so one window can produce at most one
    /// attempt — <see cref="_repairState"/> then records that it ran. (2) The game's own
    /// <c>NotifyLoadingFinished</c> body is <c>if (!PlayersFinishedLoading.Contains(player))
    /// Add(player)</c>, so a second call for the same reference is a no-op — which is also what
    /// makes racing the game's own late delivery harmless: whichever arrives second does nothing.
    /// (3) The list holds object references and the entries we pass back are the very same
    /// references, so <c>Contains</c> matches by identity and cannot be defeated by a re-created
    /// player object.</para>
    ///
    /// <para>Returns the number restored; the names and the drop count are left in fields for the
    /// report that follows.</para>
    /// </summary>
    private static int RestoreDiscardedNotifications()
    {
        _restoredCount = 0;
        _restoreDropped = 0;
        _restoredNames = string.Empty;
        _restoreFailure = string.Empty;

        try
        {
            Reflect();
            if (_notifyMethod == null)
            {
                _restoreFailure = "PlayerRegistry.NotifyLoadingFinished could not be resolved, so "
                    + "nothing could be handed back";
                return 0;
            }

            object? me = _myPlayerProp?.GetValue(null);
            System.Collections.IList? participants = ListOf(_participantsProp?.GetValue(null));
            var sb = new StringBuilder();
            var one = new object[1];

            foreach (object entry in Doomed)
            {
                if (me != null && ReferenceEquals(entry, me))
                {
                    _restoreDropped++;
                    continue; // never assert our own readiness
                }
                if (participants != null && !participants.Contains(entry))
                {
                    _restoreDropped++;
                    continue; // not a participant now: restoring it would inflate the release test
                }

                one[0] = entry;
                _notifyMethod.Invoke(null, one);
                _restoredCount++;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(Describe(entry));
            }

            _restoredNames = sb.ToString();
            return _restoredCount;
        }
        catch (Exception e)
        {
            _restoreFailure = e.GetType().Name + ": " + e.Message;
            return _restoredCount;
        }
    }

    /// <summary>Counts only — allocates, so it is called from an edge or the cadence, never Tick.</summary>
    private static string Roster()
    {
        Reflect();
        try
        {
            System.Collections.IList? participants = ListOf(_participantsProp?.GetValue(null));
            System.Collections.IList? finished = ListOf(_finishedField?.GetValue(null));
            System.Collections.IList? all = ListOf(_allPlayersField?.GetValue(null));
            return $"Roster now: {participants?.Count ?? -1} participant(s) of "
                + $"{all?.Count ?? -1} player(s), {finished?.Count ?? -1} already marked finished "
                + "(-1 = that list could not be read).";
        }
        catch (Exception e)
        {
            return $"Roster unreadable ({e.GetType().Name}: {e.Message}).";
        }
    }

    private static string MissingParticipants()
    {
        Reflect();
        try
        {
            System.Collections.IList? participants = ListOf(_participantsProp?.GetValue(null));
            System.Collections.IList? finished = ListOf(_finishedField?.GetValue(null));
            if (participants == null || finished == null)
                return "the participant or finished list could not be read, so the missing side "
                    + "cannot be named.";

            Missing.Clear();
            foreach (object? p in participants)
            {
                if (p == null || finished.Contains(p))
                    continue;
                Missing.Add(Describe(p));
            }

            if (Missing.Count == 0)
                return $"NOBODY is missing — {participants.Count} participant(s) against "
                    + $"{finished.Count} finished, so the release test should already have passed "
                    + "and the wait is being held by something outside this handshake.";

            var sb = new StringBuilder();
            sb.Append(Missing.Count).Append(" of ").Append(participants.Count)
              .Append(" participant(s) are NOT in PlayersFinishedLoading: ");
            for (int i = 0; i < Missing.Count; i++)
            {
                sb.Append(Missing[i]);
                sb.Append(i == Missing.Count - 1 ? "." : ", ");
            }
            return sb.ToString();
        }
        catch (Exception e)
        {
            return $"the missing side could not be read ({e.GetType().Name}: {e.Message}).";
        }
    }

    private static string Describe(object player)
    {
        try
        {
            object? id = _playerIdProp?.GetValue(player);
            object? name = _usernameProp?.GetValue(player);
            return $"player {id ?? "?"} '{name ?? "?"}'";
        }
        catch (Exception e)
        {
            return $"a participant whose id could not be read ({e.GetType().Name})";
        }
    }

    /// <summary>Unconditional — printed on every line this class emits, so "the watch is silent"
    /// and "the watch is dead" can never be confused. A held instrument reads as a dead one
    /// otherwise.</summary>
    private static string Liveness()
    {
        string err = _lastCallbackError != null
            ? $" lastCallbackError={_lastCallbackError};"
            : string.Empty;
        return $"[watch: ticks={_ticks}, handshake edges seen={_edges}, peer notifications "
            + $"seen={_notifies}, window {(_waitOpen ? "OPEN" : "closed")}, repair "
            + (_repairState == RepairState.Restored ? "RESTORED " + _restoredCount
                : _repairState == RepairState.Failed ? "FAILED" : "not needed")
            + $";{err} reading the game's own log stream]";
    }
}
