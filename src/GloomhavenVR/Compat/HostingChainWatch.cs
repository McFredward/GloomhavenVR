using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FFSNet;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace GloomhavenVR.Compat;

/// <summary>
/// INSTRUMENT for the failure class behind the ModBuild 232 "Multiplayer im Szenario startet nicht"
/// report (see <see cref="LoadoutHostingGuard"/> for that specific defect).
///
/// <para><b>THE CLASS.</b> <c>UnityEngine.Events.UnityEvent.Invoke()</c> walks its listener list in
/// a plain loop with NO per-listener try/catch. One listener that throws therefore silently
/// AMPUTATES every listener registered after it. Nothing in the game reports this: the exception is
/// logged once, attributed to the listener, and the reader has no way to know that eight other
/// subsystems were supposed to run and did not. On <c>NetworkManager.HostingStartedEvent</c> that
/// is the difference between "the host button did nothing" and a working session — the button's own
/// completion callback (<c>UIMultiplayerEscSubmenu.OnHostingStartedCallback</c>) is registered LAST,
/// so it is the first thing lost. This class makes that visible forever.
///
/// <para><b>WHAT IT PRINTS — one line per event edge, never per frame.</b></para>
/// <list type="bullet">
/// <item><b>Healthy edge</b> (Info): <c>[Net] HOSTING STARTED — 9 listener(s) on
///   NetworkManager.HostingStartedEvent, chain COMPLETED: 1. GHNetworkCallbacks…</c> — the full
///   roster in invocation order, each entry naming the listener's declaring type, method, and
///   whether its target object is alive / inactive / a DESTROYED UnityEngine.Object still held by
///   the delegate (which is itself the signature of a leaked listener).</item>
/// <item><b>Amputated edge</b> (Warning): <c>[Net] HOSTING CHAIN AMPUTATED — listener 2/9
///   'UILoadoutManager.OnSwitchedToMultiplayer' threw NullReferenceException at
///   UILoadoutManager.MPConfirmEnterScenario; 7 listener(s) after it were SKIPPED by
///   UnityEvent.Invoke: …</c> plus the result of re-running them (see below).</item>
/// <item><b>Any OTHER UnityEvent chain</b> (Warning, once per distinct throwing listener, capped):
///   the same diagnosis without the roster, because only the two hosting events are tracked.</item>
/// </list>
///
/// <para><b>IT ALSO REPAIRS THE EDGE.</b> When an amputation is detected on a tracked event, the
/// skipped listeners are re-invoked, each inside its own try/catch, on the next Update — outside the
/// log callback, so nothing re-enters Unity's logger. This is a behaviour RESTORATION, not a
/// change: those listeners were supposed to run in that order and did not. Safety rails: the
/// throwing listener must be locatable in the pre-invoke roster (no match ⇒ diagnose only, never
/// replay), only entries STRICTLY AFTER it are considered, and each must still be registered at
/// replay time — so a listener that already ran and unregistered itself can never be run twice.
///
/// <para><b>HOW IT SEES ANY OF THIS WITHOUT PATCHING THE NETCODE.</b> The project forbids patching
/// <c>FFSNet.NetworkManager</c> and Photon Bolt, and <c>UnityEvent.Invoke</c> is far too hot to
/// patch wholesale. So this class only OBSERVES:
/// <list type="number">
/// <item><c>Application.logMessageReceived</c> — an <c>Exception</c> log whose stack contains
///   <c>UnityEngine.Events.InvokableCall…Invoke</c> IS an amputation, and the frame directly above
///   that one names the listener. The project has restored the game's stack traces since ModBuild
///   136 (<c>Core.ExceptionTraces</c>), which is what makes the frame readable at all.</item>
/// <item>The event edge itself is read from <c>NetworkManager.SessionID</c> (NetworkManager.cs:92):
///   <c>CreateSession</c> sets it immediately before <c>HostingStartedEvent.Invoke()</c>
///   (:291-296) and <c>Reset</c> clears it (:162), so empty→set is the hosting-started edge and
///   set→empty the hosting-ended edge. No patch, no polling of Bolt.</item>
/// <item>The roster comes from reflection over <c>UnityEventBase.m_Calls.m_RuntimeCalls</c>. It is
///   re-read only when the listener COUNT changes, and the snapshot kept is the one from before
///   the invoke — which matters, because one-shot listeners unregister themselves as their first
///   statement and the post-invoke list no longer contains them.</item>
/// </list>
/// Every reflection handle is resolved once; if any of them cannot be found (a Unity upgrade
/// renaming a private field) the whole class degrades to a strict no-op after ONE Warning.
/// </summary>
internal static class HostingChainWatch
{
    private const string Scope = "Net";

    /// <summary>An entry of a UnityEvent's runtime listener list, in invocation order.</summary>
    private readonly struct Listener
    {
        internal readonly UnityAction? Action;
        /// <summary>"DeclaringType.Method" — the key the stack trace is matched against.</summary>
        internal readonly string Key;
        /// <summary>Key plus target liveness, for the printed roster.</summary>
        internal readonly string Description;

        internal Listener(UnityAction? action, string key, string description)
        {
            Action = action;
            Key = key;
            Description = description;
        }
    }

    // ---- reflection handles (resolved once) -------------------------------------------------
    private static FieldInfo? _fCalls;        // UnityEventBase.m_Calls        : InvokableCallList
    private static FieldInfo? _fRuntimeCalls; // InvokableCallList.m_RuntimeCalls : List<BaseInvokableCall>
    private static FieldInfo? _fDelegate;     // InvokableCall.Delegate        : UnityAction
    private static bool _resolved;
    private static bool _degraded;

    // ---- state --------------------------------------------------------------------------------
    private static Driver? _driver;
    private static bool _hooked;

    private static NetworkManager? _manager;
    private static string _sessionId = string.Empty;

    private static readonly List<Listener> StartedRoster = [];
    private static readonly List<Listener> EndedRoster = [];
    private static int _startedCount = -1;
    private static int _endedCount = -1;

    /// <summary>Set from the log callback, consumed on the next Update. Never touched elsewhere.</summary>
    private static string? _pendingCondition;
    private static string? _pendingStack;
    private static bool _pendingIsHosting;

    /// <summary>One line per distinct throwing listener for NON-hosting UnityEvents, capped.</summary>
    private static readonly HashSet<string> SeenForeignThrowers = [];
    private const int MaxForeignThrowers = 20;

    /// <summary>Scratch, reused — this runs from Update.</summary>
    private static readonly List<Listener> Scratch = [];

    /// <summary>The two argument-list brackets, written as one balanced literal on purpose
    /// (scripts/patch-inventory.py's bracket scanner reads string literals as code).</summary>
    private static readonly char[] ParenChars = "()".ToCharArray();

    // ---- lifecycle ----------------------------------------------------------------------------

    internal static void Install()
    {
        if (_driver != null)
            return;
        if (!Resolve())
            return;

        var go = new GameObject("GloomhavenVR.HostingChainWatch") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<Driver>();

        Application.logMessageReceived += OnLogMessage;
        _hooked = true;

        VRLog.Info(Scope, "HOSTING CHAIN WATCH armed — NetworkManager.HostingStartedEvent and "
            + "HostingEndedEvent are rostered, every edge prints ONE line, and a listener that "
            + "throws (which silently amputates every listener after it inside UnityEvent.Invoke) "
            + "is named and the remainder re-run. Grep 'HOSTING STARTED' / 'HOSTING CHAIN AMPUTATED'.");
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
        StartedRoster.Clear();
        EndedRoster.Clear();
        _startedCount = _endedCount = -1;
        _manager = null;
        _sessionId = string.Empty;
        _pendingCondition = _pendingStack = null;
    }

    private static bool Resolve()
    {
        if (_resolved)
            return !_degraded;
        _resolved = true;
        try
        {
            _fCalls = AccessTools.Field(typeof(UnityEventBase), "m_Calls");
            Type? callList = _fCalls?.FieldType;
            _fRuntimeCalls = callList != null ? AccessTools.Field(callList, "m_RuntimeCalls") : null;
            Type? invokable = AccessTools.TypeByName("UnityEngine.Events.InvokableCall");
            _fDelegate = invokable != null ? AccessTools.Field(invokable, "Delegate") : null;

            if (_fCalls == null || _fRuntimeCalls == null || _fDelegate == null)
            {
                _degraded = true;
                VRLog.Warn(Scope, "HOSTING CHAIN WATCH disabled — UnityEvent internals not found "
                    + $"(m_Calls: {(_fCalls != null ? "ok" : "MISSING")}, "
                    + $"m_RuntimeCalls: {(_fRuntimeCalls != null ? "ok" : "MISSING")}, "
                    + $"InvokableCall.Delegate: {(_fDelegate != null ? "ok" : "MISSING")}). "
                    + "A listener that throws will still amputate the hosting chain silently — "
                    + "re-point these three handles at the current Unity version.");
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            _degraded = true;
            VRLog.Warn(Scope, $"HOSTING CHAIN WATCH disabled — reflection setup threw {e.GetType().Name}: {e.Message}.");
            return false;
        }
    }

    // ---- the per-frame observation ------------------------------------------------------------

    private sealed class Driver : MonoBehaviour
    {
        private Action? _tick;
        private void Awake() => _tick = Tick;
        private void Update() => TickGuard.Run("Compat.HostingChainWatch", _tick!, Scope);
    }

    private static void Tick()
    {
        NetworkManager? manager = FFSNetwork.Manager;
        if (manager == null)
        {
            _manager = null;
            return;
        }

        if (!ReferenceEquals(manager, _manager))
        {
            // A fresh NetworkManager brings fresh UnityEvent objects — drop everything.
            _manager = manager;
            _sessionId = manager.SessionID ?? string.Empty;
            _startedCount = _endedCount = -1;
            StartedRoster.Clear();
            EndedRoster.Clear();
        }

        // AN AMPUTATION IS HANDLED FIRST, BEFORE THE ROSTERS ARE REFRESHED — and that order is
        // load-bearing, not tidiness. The invoke has already happened by the time we get here, and
        // the listener that threw removed ITSELF from the list as its first statement (that is the
        // shape of every one of these one-shots), so refreshing first would overwrite the only
        // snapshot that still knows where the chain stopped and what came after it.
        if (_pendingStack != null)
        {
            bool wasHosting = _pendingIsHosting;
            ReportAmputation();
            RefreshRoster(manager.HostingStartedEvent, StartedRoster, ref _startedCount);
            RefreshRoster(manager.HostingEndedEvent, EndedRoster, ref _endedCount);
            if (wasHosting)
            {
                // The amputation line IS this edge's report — one line per edge.
                _sessionId = manager.SessionID ?? string.Empty;
                return;
            }
        }
        else
        {
            // Only when the count moved: the snapshot that matters is the pre-invoke one.
            RefreshRoster(manager.HostingStartedEvent, StartedRoster, ref _startedCount);
            RefreshRoster(manager.HostingEndedEvent, EndedRoster, ref _endedCount);
        }

        string now = manager.SessionID ?? string.Empty;
        if (now == _sessionId)
            return;
        bool started = _sessionId.Length == 0 && now.Length > 0;
        _sessionId = now;

        if (started)
            VRLog.Info(Scope, "HOSTING STARTED — NetworkManager.CreateSession ran and "
                + $"HostingStartedEvent.Invoke() walked {StartedRoster.Count} listener(s), chain "
                + "COMPLETED (no listener threw). Roster in invocation order: "
                + FormatRoster(StartedRoster, -1)
                + " HOW TO READ THIS: UnityEvent.Invoke has no per-listener catch, so if one of "
                + "these throws every listener AFTER it is skipped and the game reports nothing. "
                + "The LAST entries are the ones added by the button press itself "
                + "(NetworkManager.ToggleServer adds the caller's onHostingStarted callback and "
                + "StartVoiceHost, NetworkManager.cs:199-205) — losing them is exactly the "
                + "\"I pressed Host Session and nothing happened\" symptom.");
        else
            VRLog.Info(Scope, "HOSTING ENDED — the session id was cleared and "
                + $"HostingEndedEvent carried {EndedRoster.Count} listener(s), chain COMPLETED. "
                + "Roster in invocation order: " + FormatRoster(EndedRoster, -1));
    }

    private static void RefreshRoster(UnityEvent? evt, List<Listener> into, ref int knownCount)
    {
        if (evt == null)
        {
            into.Clear();
            knownCount = -1;
            return;
        }
        int count = CountOf(evt);
        if (count < 0 || count == knownCount)
            return;
        knownCount = count;
        ReadRoster(evt, into);
    }

    private static int CountOf(UnityEvent evt)
    {
        try
        {
            object? calls = _fCalls!.GetValue(evt);
            if (calls == null)
                return -1;
            return _fRuntimeCalls!.GetValue(calls) is IList list ? list.Count : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static void ReadRoster(UnityEvent evt, List<Listener> into)
    {
        into.Clear();
        try
        {
            object? calls = _fCalls!.GetValue(evt);
            if (calls == null || _fRuntimeCalls!.GetValue(calls) is not IList list)
                return;
            foreach (object? call in list)
            {
                UnityAction? action = null;
                if (call != null && _fDelegate!.DeclaringType!.IsInstanceOfType(call))
                    action = _fDelegate.GetValue(call) as UnityAction;
                into.Add(Describe(action, call));
            }
        }
        catch (Exception e)
        {
            into.Clear();
            VRLog.Warn(Scope, $"HOSTING CHAIN WATCH: roster read threw {e.GetType().Name} ({e.Message}) "
                + "— this edge will be reported without a roster.");
        }
    }

    private static Listener Describe(UnityAction? action, object? call)
    {
        if (action?.Method == null)
        {
            string kind = call?.GetType().Name ?? "null";
            return new Listener(null, "?", $"<unreadable: {kind}>");
        }

        string type = action.Method.DeclaringType?.Name ?? "?";
        string key = type + "." + action.Method.Name;

        string liveness;
        if (action.Target is UnityEngine.Object uo)
        {
            // A delegate keeps its C# target alive even after Unity destroyed the object, and the
            // delegate STILL INVOKES — that is how a listener outlives the screen it belongs to.
            if (uo == null)
                liveness = "target DESTROYED (leaked listener)";
            else if (uo is Behaviour b)
                liveness = b.isActiveAndEnabled ? "target active" : "target INACTIVE";
            else
                liveness = "target alive";
        }
        else
        {
            liveness = action.Target == null ? "static" : "plain object";
        }

        return new Listener(action, key, $"{key} [{liveness}]");
    }

    private static string FormatRoster(List<Listener> roster, int throwerIndex)
    {
        if (roster.Count == 0)
            return "(empty — the roster could not be read).";
        var sb = new StringBuilder();
        for (int i = 0; i < roster.Count; i++)
        {
            sb.Append(i + 1).Append(". ").Append(roster[i].Description);
            if (i == throwerIndex)
                sb.Append(" <== THREW HERE");
            else if (throwerIndex >= 0 && i > throwerIndex)
                sb.Append(" <== SKIPPED");
            sb.Append(i == roster.Count - 1 ? "." : "; ");
        }
        return sb.ToString();
    }

    // ---- the exception observation -------------------------------------------------------------

    /// <summary>
    /// Runs on Unity's log callback for EVERY message — so it does the cheapest possible thing
    /// first (an enum compare) and never logs from in here (that would re-enter the logger).
    /// </summary>
    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception || stackTrace == null)
            return;
        // The signature of an amputation: the throw unwound through a UnityEvent's invocation loop.
        if (stackTrace.IndexOf("UnityEngine.Events.InvokableCall", StringComparison.Ordinal) < 0)
            return;
        if (_pendingStack != null)
            return; // one at a time; the next Update consumes it

        _pendingCondition = condition;
        _pendingStack = stackTrace;
        // Scoping to the two hosting events without patching the netcode: the frame below
        // UnityEvent.Invoke is the invoke SITE, and for both of them it is NetworkManager.
        _pendingIsHosting = stackTrace.IndexOf("FFSNet.NetworkManager", StringComparison.Ordinal) >= 0;
    }

    private static void ReportAmputation()
    {
        string condition = _pendingCondition ?? "(no message)";
        string stack = _pendingStack ?? string.Empty;
        bool hosting = _pendingIsHosting;
        _pendingCondition = _pendingStack = null;

        string thrower = FrameAt(stack, 0);
        string listener = ListenerFrame(stack);

        if (!hosting)
        {
            // Not one of ours — still worth exactly one line per distinct offender, because the
            // amputation class is invisible by construction wherever it happens.
            if (SeenForeignThrowers.Count >= MaxForeignThrowers || !SeenForeignThrowers.Add(listener))
                return;
            VRLog.Warn(Scope, $"UNITYEVENT CHAIN AMPUTATED (not a hosting event): listener "
                + $"'{listener}' threw {Summarize(condition)} at '{thrower}'. UnityEvent.Invoke has "
                + "no per-listener catch, so EVERY listener registered after it was skipped and "
                + "nothing else will report that. This event is not rostered by the mod, so the "
                + "skipped listeners are neither named nor re-run — if this line matters, add the "
                + "event to HostingChainWatch. One line per distinct listener"
                + (SeenForeignThrowers.Count >= MaxForeignThrowers ? " (cap reached)." : "."));
            return;
        }

        List<Listener> roster = StartedRoster;
        int index = IndexOf(roster, listener);
        if (index < 0)
        {
            roster = EndedRoster;
            index = IndexOf(roster, listener);
        }

        string replay;
        if (index < 0)
        {
            replay = "The listener could not be located in either hosting roster, so the skipped "
                + "listeners are NOT named and NOT re-run (a replay that cannot prove where the "
                + "chain stopped could run something twice).";
        }
        else
        {
            int skipped = roster.Count - index - 1;
            replay = skipped == 0
                ? "It was the LAST listener, so nothing was skipped."
                : Replay(roster, index, skipped);
        }

        VRLog.Warn(Scope, "HOSTING CHAIN AMPUTATED — "
            + (index >= 0 ? $"listener {index + 1}/{roster.Count} " : "listener ")
            + $"'{listener}' threw {Summarize(condition)} at '{thrower}'. "
            + "UnityEvent.Invoke walks its listeners in a plain loop with NO per-listener catch, so "
            + "every listener AFTER this one was skipped by the game and nothing in the game says "
            + "so — the session itself is fine (NetworkManager.CreateSession invokes the event "
            + "LAST, NetworkManager.cs:291-296), which is why this reads to the player as \"I "
            + "pressed the button and nothing happened\". " + replay
            + (index >= 0 ? " Roster in invocation order: " + FormatRoster(roster, index) : string.Empty));
    }

    /// <summary>Re-run the listeners the game skipped, each isolated. Only entries that are STILL
    /// registered are run, so a listener that already executed and unregistered itself (the normal
    /// shape of these one-shots) can never be invoked twice.</summary>
    private static string Replay(List<Listener> roster, int throwerIndex, int skipped)
    {
        UnityEvent? evt = ReferenceEquals(roster, StartedRoster)
            ? _manager?.HostingStartedEvent
            : _manager?.HostingEndedEvent;

        var live = new HashSet<UnityAction>();
        if (evt != null)
        {
            ReadRoster(evt, Scratch);
            foreach (Listener l in Scratch)
                if (l.Action != null)
                    live.Add(l.Action);
        }

        int ran = 0, threw = 0, gone = 0;
        var failures = new StringBuilder();
        for (int i = throwerIndex + 1; i < roster.Count; i++)
        {
            UnityAction? action = roster[i].Action;
            if (action == null || !live.Contains(action))
            {
                gone++;
                continue;
            }
            try
            {
                action.Invoke();
                ran++;
            }
            catch (Exception e)
            {
                threw++;
                if (failures.Length > 0)
                    failures.Append("; ");
                failures.Append(roster[i].Key).Append(" -> ").Append(e.GetType().Name);
            }
        }

        // Re-read once so the next edge starts from the post-replay truth.
        _startedCount = _endedCount = -1;

        return $"{skipped} listener(s) after it were SKIPPED by the game; the mod re-ran them: "
            + $"{ran} completed, {threw} threw, {gone} were no longer registered"
            + (failures.Length > 0 ? $" ({failures})." : ".")
            + " THAT REPLAY IS THE SAFETY NET, NOT THE FIX — the listener named above still has to "
            + "stop throwing.";
    }

    private static int IndexOf(List<Listener> roster, string key)
    {
        for (int i = 0; i < roster.Count; i++)
            if (roster[i].Key == key)
                return i;
        return -1;
    }

    // ---- stack-trace parsing --------------------------------------------------------------------

    /// <summary>"Type.Method" of the frame directly above the UnityEvent invocation loop — i.e. the
    /// LISTENER, which is what the roster is keyed by. The throwing method itself (frame 0) can be
    /// several calls deeper.</summary>
    private static string ListenerFrame(string stack)
    {
        string[] lines = stack.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].IndexOf("UnityEngine.Events.InvokableCall", StringComparison.Ordinal) >= 0
                && lines[i].IndexOf(".Invoke", StringComparison.Ordinal) >= 0)
                return i > 0 ? Extract(lines[i - 1]) : "(no frame above InvokableCall.Invoke)";
        }
        return "(InvokableCall.Invoke not found in the stack)";
    }

    private static string FrameAt(string stack, int index)
    {
        string[] lines = stack.Split('\n');
        return index < lines.Length ? Extract(lines[index]) : "(no frame)";
    }

    /// <summary>Reduce one stack line to "Type.Method". Handles both the
    /// <c>Application.logMessageReceived</c> shape ("Ns.Type.Method () (at file:line)") and the
    /// Exception.ToString() shape ("  at Ns.Type.Method () [0x0] in file:line").</summary>
    private static string Extract(string line)
    {
        string s = line.Trim();
        if (s.StartsWith("at ", StringComparison.Ordinal))
            s = s.Substring(3);
        // NOTE: the argument list is found via a BALANCED "()" pair (see ParenChars) rather than a
        // lone '(' literal — scripts/patch-inventory.py's bracket scanner does not blank string or
        // character literals, so an unbalanced one in source breaks the patch manifest.
        int paren = s.IndexOfAny(ParenChars);
        if (paren > 0)
            s = s.Substring(0, paren);
        s = s.Trim();
        if (s.Length == 0)
            return "(unparsed)";
        // Keep the last two dot-separated segments: "Type.Method".
        int last = s.LastIndexOf('.');
        if (last <= 0)
            return s;
        int prev = s.LastIndexOf('.', last - 1);
        return prev < 0 ? s : s.Substring(prev + 1);
    }

    private static string Summarize(string condition)
    {
        int nl = condition.IndexOf('\n');
        string first = nl > 0 ? condition.Substring(0, nl) : condition;
        return first.Length > 160 ? first.Substring(0, 160) + "…" : first;
    }
}
