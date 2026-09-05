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
/// <para><b>WHY THIS CLASS ONLY WATCHES.</b> Repairing it means writing
/// <c>PlayerRegistry.PlayersFinishedLoading</c> — game network state, which this project forbids
/// the mod from touching. So nothing here writes anything: no Harmony patch, no game field, no
/// behaviour change, and deliberately NO time limit that gives up on a wait (the user's standing
/// ruling is <i>"Ich will gar keine Zeitlimits dieser Art."</i>). An instrument may observe a long
/// wait; a guard may not silently abandon one.</para>
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
/// <item><b>STILL WAITING</b> — past a plain observation bound, naming the participants that are
///   NOT in <c>PlayersFinishedLoading</c> and what the player can do about it. Observation only.</item>
/// </list>
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
            + "permanent hang: nothing re-sends it. Grep 'SCENARIO ENTRY'. This watch WRITES "
            + "NOTHING and imposes no time limit — it only says what is true.");
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
        Missing.Clear();
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

            if (condition.IndexOf(FlagToken, StringComparison.Ordinal) < 0)
                return;

            bool open = condition.IndexOf(FlagTrue, StringComparison.Ordinal) >= 0;
            if (open == _waitOpen)
                return;

            _waitOpen = open;
            _edges++;
            if (open)
            {
                _openedAt = Time.realtimeSinceStartup;
                _nextReportAt = _openedAt + FirstReportSeconds;
                _loudLines = 0;
                _inWindow = 0;
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

    private static void ReportClosed(float held, int counted)
    {
        VRLog.Note(Scope, "SCENARIO ENTRY: the loading handshake window CLOSED after "
            + $"{held:F1}s — every participant was accounted for. {counted} peer "
            + "notification(s) arrived while it was open and counted. " + Liveness());
    }

    private static void ReportStillWaiting(int sequence, float held)
    {
        string missing = MissingParticipants();

        VRLog.Alert(Scope, $"SCENARIO ENTRY STILL WAITING ({sequence}) — this client has "
            + $"held the multiplayer loading handshake open for {held:F0}s. THE TERM THAT IS "
            + $"NOT SATISFIED: {missing} The wait is released only by "
            + "Participants.Count <= PlayersFinishedLoading.Count, and nothing re-sends a "
            + $"notification that was already consumed. {_discarded} notification(s) were "
            + "discarded before this window opened"
            + (_discarded > 0
                ? " — that is the known lost-message race and this wait cannot end on its own."
                : ", so the peer has genuinely not reported in yet; a slow load still ends "
                  + "normally.")
            + " " + Escape()
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
            + $"seen={_notifies}, window {(_waitOpen ? "OPEN" : "closed")};{err} reading the "
            + "game's own log stream, writing nothing]";
    }
}
