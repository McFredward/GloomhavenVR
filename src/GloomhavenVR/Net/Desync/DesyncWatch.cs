using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FFSNet;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Net.Desync;

/// <summary>
/// Multiplayer desynchronisation: an observer, a warning, and one local dial. NO Harmony patch
/// anywhere in this file — every seam it uses is a public static the game already exposes.
///
/// <para>WHY (user, 2026-08-30: "Das Basisspiel selber ist bekannt dafuer im Multiplayer immer mal
/// wieder desyncs zu haben die einen Zwingen neu zu laden"). The full evidence is in
/// <c>.planning/multiplayer/DESYNC-ANALYSIS.md</c>; the two findings this file acts on:</para>
///
/// <para>1. <b>"Desynchronization" is the game's catch-all for any exception thrown while applying
/// a network message.</b> <c>ActionProcessor.TryProcessNextAction</c> wraps the whole dispatch in
/// <c>catch (Exception ex) { FFSNetwork.HandleDesync(ex); }</c>, and 60 of the ~121 entries in
/// <c>GameAction</c>'s dispatch table dereference a <c>Singleton&lt;T&gt;.Instance</c> that is
/// plain <c>_instance</c> — null before <c>Awake</c>, null again after <c>OnDestroy</c> — with no
/// null check at the call site. A window that has not been built yet is therefore reported to the
/// player as a desynchronisation. The state was never compared.</para>
///
/// <para>2. <b>There is a five-second deadline nobody can see.</b> When the head of the action
/// queue does not belong to the local phase, the processor retries it every
/// <c>ActionQueueProcessingInterval</c> (0.3 s) and counts; at
/// <c>MaxConsecutiveIncorrectActionsAllowed</c> (round(5.0 / 0.3) = 17) it throws, and that throw
/// goes to the same catch. Any local stall — an animation, an asset load, a frame spike — spends
/// that budget, and in VR the local client is measurably the slower one.</para>
///
/// <para>WHAT THIS DOES, in the order it matters:</para>
/// <list type="number">
/// <item><b>Records.</b> <c>FFSNetwork.OnDesyncDetected</c> is a public static multicast delegate
/// (<c>Delegate.Combine</c>d in SceneController.cs:578) so the mod can subscribe with no patch and
/// the game's own dialog still runs. On fire we emit one block naming the phase, the pending queue
/// with each action's target phase, the players, and — the honest part —
/// <b>whether GloomhavenVR appears in the stack</b>. Half the <c>HandleDesync</c> call sites
/// construct <c>new Exception("…")</c> with no stack at all, so today the artefact is one line.</item>
/// <item><b>Warns, and names the blocker.</b> The five seconds above are silent. From
/// <see cref="Tick"/> we compare the head action's target phase with the current one and, once the
/// mismatch has stood for <see cref="StallWarnSeconds"/>, say so — with the action's name, the
/// processor's own state word, and <b>which of the two stalls this is</b>. ModBuild 351: those two
/// are not the same event and the difference is the whole story. While the processor is READY the
/// game's incorrect-action counter really is running and the budget really does expire. While it
/// is <c>Halted</c> the head action is never even looked at (ActionProcessor.cs:279 returns before
/// the counter), so no deadline exists, no desynchronisation will ever be declared, and the action
/// waits for ever with nothing anywhere reporting it — the shape that killed the ModBuild 348
/// two-player session. Through ModBuild 350 this line claimed the deadline in both cases and
/// printed exactly once, which is indistinguishable in a log from a stall that cleared; the
/// terminal one now repeats (bounded by <see cref="MaxStallRepeats"/>).</item>
/// <item><b>Buys patience.</b> <c>ActionProcessor.MaxConsecutiveIncorrectActionsAllowed</c> is a
/// public static setter. Raising it is <b>strictly local</b>: each client counts its own retries,
/// the action is still executed only when the phase matches, nothing is applied early or twice,
/// and a client cannot make a peer desynchronise by being patient. The only cost of being wrong is
/// that a genuinely stuck session takes longer to admit it.</item>
/// </list>
///
/// <para>WHAT IT DELIBERATELY DOES NOT DO: suppress. Patching <c>HandleDesync</c> to swallow, or
/// clearing <c>AutoShutdownUponDesynchronization</c>, would let a session run past a REAL state
/// divergence and write a corrupt campaign save. At the throw site nothing here can tell a false
/// positive from a real one, so the only honest lever is patience — never suppression.</para>
/// </summary>
internal static class DesyncWatch
{
    private const string Name = "Net";

    /// <summary>Prefix every recorded line carries, so one grep pulls the whole event out of a run.</summary>
    private const string Tag = "DESYNC";

    /// <summary>
    /// How long the head action may sit in the wrong phase before we say so. Well under the
    /// game's own 5 s budget, so the warning arrives while there is still time to be useful —
    /// and, if the session dies anyway, the player knows it was not instantaneous.
    /// </summary>
    private const float StallWarnSeconds = 1.5f;

    /// <summary>
    /// How often a TERMINAL stall (the processor is Halted, so no deadline is running and the
    /// action can never be played) repeats itself. A stall that will never clear must not look
    /// like one that did: through ModBuild 350 this watch printed exactly one line and then went
    /// quiet, which is what a cleared stall also looks like from the log
    /// (memory: "a held instrument reads as dead").
    /// </summary>
    private const float StallRepeatSeconds = 20f;

    /// <summary>Hard cap on those repeats — the instrument must never become the flood.</summary>
    private const int MaxStallRepeats = 8;

    /// <summary>Most queued actions listed in a report. A desync with 200 pending actions is a
    /// story the first dozen already tell.</summary>
    private const int MaxQueueLines = 12;

    /// <summary>Seconds between re-arm checks of our subscription. Only the invocation-list walk
    /// allocates, and only at this cadence, and only while online.</summary>
    private const float ReArmInterval = 10f;

    private static bool _installed;
    private static bool _subscribed;
    private static DesyncDetectedEvent? _handler;

    private static float _stallSince;
    private static bool _stallAnnounced;
    private static int _stallActionType = -1;

    /// <summary>Wall clock at which a terminal (halted) stall re-announces; 0 = no repeat armed.</summary>
    private static float _stallRepeatAt;

    /// <summary>Repeats already spent on the current stall (capped by <see cref="MaxStallRepeats"/>).</summary>
    private static int _stallRepeats;
    private static float _nextReArm;

    /// <summary>The value we last wrote, so <see cref="Tick"/> can tell "the game reset it"
    /// (NetworkManager.OnEnable recomputes it from its serialized fields) from "nothing changed"
    /// with one int compare per tick.</summary>
    private static int _appliedMaxRetries;

    // ---------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Subscribe and arm. Called from <c>NetModule.Init</c> BEFORE the embodiment kill-switch:
    /// this watches the GAME's netcode, not the mod's avatar sync, so turning the avatars off
    /// must not turn the recorder off with them.
    /// </summary>
    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        _appliedMaxRetries = 0;
        Subscribe();
    }

    public static void Uninstall()
    {
        if (!_installed)
            return;
        _installed = false;
        try
        {
            if (_subscribed && _handler != null)
                FFSNetwork.OnDesyncDetected -= _handler;
        }
        catch (Exception e)
        {
            VRLog.Warn(Name, $"{Tag}: unsubscribe threw (harmless at shutdown): {e.Message}");
        }
        _subscribed = false;
        _handler = null;
        ResetStall();
    }

    private static void Subscribe()
    {
        try
        {
            _handler ??= OnDesyncDetected;
            FFSNetwork.OnDesyncDetected += _handler;
            _subscribed = true;
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
            // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
            VRLog.Note(Name, $"{Tag} watch armed (recorder + stall warning; no patch installed).");
        }
        catch (Exception e)
        {
            _subscribed = false;
            // NOT silent: an unarmed recorder looks exactly like a session that never desynced.
            VRLog.Alert(Name, $"{Tag}: could NOT subscribe to FFSNetwork.OnDesyncDetected — a " +
                              $"desynchronisation will go unrecorded. {e.GetType().Name}: {e.Message}");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Per-frame
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Cheap while offline: one static bool read and return. While online it re-applies the
    /// patience dial when the game has reset it, watches the head of the action queue, and
    /// re-arms the subscription at a slow cadence.
    /// </summary>
    public static void Tick()
    {
        if (!_installed)
            return;

        bool online;
        try
        {
            online = FFSNetwork.IsOnline;
        }
        catch
        {
            // Netcode absent / not yet initialised. Nothing to watch, and nothing to report.
            return;
        }

        if (!online)
        {
            if (_stallActionType >= 0 || _appliedMaxRetries != 0)
            {
                ResetStall();
                _appliedMaxRetries = 0;
            }
            return;
        }

        ApplyPatience();
        WatchQueue();

        float now = Time.unscaledTime;
        if (now >= _nextReArm)
        {
            _nextReArm = now + ReArmInterval;
            ReArmIfLost();
        }
    }

    /// <summary>
    /// Keep <c>ActionProcessor.MaxConsecutiveIncorrectActionsAllowed</c> at the configured budget.
    /// It has to be re-applied rather than set once: <c>NetworkManager.OnEnable</c> recomputes it
    /// from the ScriptableObject's serialized fields every time that asset is enabled, and the
    /// standing ruling forbids patching NetworkManager — so we write the property instead and let
    /// one int compare per frame notice when the game has overwritten it.
    /// </summary>
    private static void ApplyPatience()
    {
        try
        {
            float seconds = NetModule.DesyncPatienceSeconds?.Value ?? 0f;
            if (seconds <= 0f)
                return;

            NetworkManager? manager = FFSNetwork.Manager;
            if (manager == null)
                return;

            float interval = manager.ActionQueueProcessingInterval;
            if (interval <= 0f)
                return;

            int want = Mathf.Max(1, Mathf.RoundToInt(seconds / interval));
            if (ActionProcessor.MaxConsecutiveIncorrectActionsAllowed == want)
            {
                _appliedMaxRetries = want;
                return;
            }

            int had = ActionProcessor.MaxConsecutiveIncorrectActionsAllowed;
            ActionProcessor.MaxConsecutiveIncorrectActionsAllowed = want;

            // Log the FIRST application per session and any later re-application at a different
            // value; a silent re-write every frame would be indistinguishable from a dead dial.
            if (_appliedMaxRetries != want)
            {
                _appliedMaxRetries = want;
                // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
                // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
                VRLog.Note(Name, $"{Tag} patience: phase-mismatch retries {had} -> {want} " +
                                 $"({had * interval:0.0}s -> {want * interval:0.0}s at " +
                                 $"{interval:0.00}s/retry). LOCAL ONLY — this client waits longer " +
                                 "before declaring a desync; peers are unaffected and no action is " +
                                 "executed any earlier.");
            }
        }
        catch (Exception e)
        {
            VRLog.Warn(Name, $"{Tag}: could not apply the patience dial: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// The five silent seconds, made audible. When the head of the queue does not belong to the
    /// local phase the game retries it and counts toward a throw; nothing anywhere says so.
    /// </summary>
    private static void WatchQueue()
    {
        try
        {
            Queue<GameAction>? queue = ActionProcessor.ActionQueue;
            if (queue == null || queue.Count == 0)
            {
                AnnounceRecovery();
                return;
            }

            GameAction head = queue.Peek();
            if (head == null)
                return;

            int target = head.TargetPhaseID;
            int current = (int)ActionProcessor.CurrentPhase;

            // TargetPhaseID 0 is NONE — "play me in any phase". Never a mismatch.
            if (target == 0 || target == current)
            {
                AnnounceRecovery();
                return;
            }

            float now = Time.unscaledTime;
            if (_stallActionType != head.ActionTypeID)
            {
                // A different action is now at the head: this is a NEW stall, not a longer one.
                _stallActionType = head.ActionTypeID;
                _stallSince = now;
                _stallAnnounced = false;
                _stallRepeats = 0;
                _stallRepeatAt = 0f;
                return;
            }

            float held = now - _stallSince;
            if (held < StallWarnSeconds)
                return;
            if (_stallAnnounced)
            {
                // Only a HALTED stall re-announces while it remains blocked, and only a bounded
                // number of times: an instrument that repeats for ever is the flood, not the answer.
                if (_stallRepeatAt <= 0f || now < _stallRepeatAt || _stallRepeats >= MaxStallRepeats)
                    return;
                _stallRepeats++;
            }

            _stallAnnounced = true;

            // ModBuild 351 — NAME THE BLOCKER, do not assert a countdown that may not be running.
            // Through 350 this line always said "the game declares a desynchronisation at {budget}s".
            // That is only true while the processor is READY: the phase-mismatch branch that counts
            // toward the throw (ActionProcessor.cs:370-394) is reached ONLY after
            // `readyToProcessNextAction` passes (:279). A HALTED processor returns before it, so the
            // counter is not touched while halted. A native phase/animation completion can resume
            // processing normally; the halted state alone does not prove a terminal session. In
            // the ModBuild 348 session the host sat at `Halted @ NONE` with BurnAvailableCard #40 at
            // the head of the queue, and the single line this watch printed claimed a 15 s deadline
            // that was never going to come. Preserve that distinction without diagnosing every
            // queued phase barrier as the historical failed burn coroutine.
            bool ready = ActionProcessor.ReadyToProcessNextAction;
            float budget = BudgetSeconds();
            string verdict = ready
                ? $"The processor is READY, so the game's incorrect-action counter IS running and it " +
                  $"declares a desynchronisation at {budget:0.0}s. This is a LOCAL wait, not a state " +
                  "disagreement — the client has not caught up yet."
                : "THE PROCESSOR IS HALTED, so the queue is not being drained at all: the game's " +
                  "incorrect-action counter is never reached (ActionProcessor.cs:279 returns before " +
                  "it), NO deadline is running while it remains halted. A native phase or animation " +
                  "completion can resume processing; this snapshot does not prove a dead session. " +
                  "Check for DESYNC STALL CLEARED and continuing phase changes. A historical burn " +
                  "failure left the BURNER stuck in TakeDamageConfirmation; that diagnosis needs an " +
                  "actual Cards 'BURN COMMIT HANG' line on that player's machine.";

            VRLog.Alert(Name, $"{Tag} STALL: action {ActionName(head.ActionTypeID)} has been waiting " +
                              $"{held:0.0}s for phase {PhaseName(target)} while this client is in " +
                              $"{PhaseName(current)} ({queue.Count} queued), processor state " +
                              $"{StateName()}. {verdict}");

            // Keep a bounded reminder while halted; ClearStall reports normal recovery.
            _stallRepeatAt = ready ? 0f : now + StallRepeatSeconds;
        }
        catch (Exception e)
        {
            // One line, then stop trying for this stall: the watcher must never become the noise.
            _stallAnnounced = true;
            _stallRepeatAt = 0f;
            VRLog.Warn(Name, $"{Tag}: queue watch threw: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// The processor's own state word — the term that decides whether the queue is being drained
    /// at all. `Halted` means the head action is not even LOOKED at, which is why a stall in that
    /// state carries no deadline (ActionProcessor.cs:279).
    /// </summary>
    private static string StateName()
    {
        try
        {
            return ActionProcessor.CurrentState.StateType.ToString();
        }
        catch (Exception)
        {
            return "<unreadable>";
        }
    }

    private static void AnnounceRecovery()
    {
        if (_stallActionType < 0)
            return;

        if (_stallAnnounced)
        {
            float held = Time.unscaledTime - _stallSince;
            VRLog.Note(Name, $"{Tag} STALL CLEARED after {held:0.0}s — the phase caught up and " +
                             $"{ActionName(_stallActionType)} was played. No desynchronisation.");
        }
        ResetStall();
    }

    private static void ResetStall()
    {
        _stallActionType = -1;
        _stallAnnounced = false;
        _stallSince = 0f;
        _stallRepeatAt = 0f;
        _stallRepeats = 0;
    }

    /// <summary>Seconds the game will actually wait, from the values in force right now.</summary>
    private static float BudgetSeconds()
    {
        try
        {
            NetworkManager? manager = FFSNetwork.Manager;
            float interval = manager?.ActionQueueProcessingInterval ?? 0.3f;
            return ActionProcessor.MaxConsecutiveIncorrectActionsAllowed * interval;
        }
        catch
        {
            return 5f;
        }
    }

    /// <summary>
    /// SceneController removes and re-combines only its OWN handler, so ours survives a scene
    /// change — but a subscription that has silently gone missing is indistinguishable from a
    /// session that never desynced, which is exactly the failure this whole file exists to stop.
    /// So check, cheaply and rarely, and say so if it had to be restored.
    /// </summary>
    private static void ReArmIfLost()
    {
        try
        {
            DesyncDetectedEvent? current = FFSNetwork.OnDesyncDetected;
            if (_handler == null)
            {
                Subscribe();
                return;
            }

            bool present = false;
            if (current != null)
            {
                foreach (Delegate d in current.GetInvocationList())
                {
                    if (ReferenceEquals(d, _handler) || d.Equals(_handler))
                    {
                        present = true;
                        break;
                    }
                }
            }

            if (present)
                return;

            _subscribed = false;
            Subscribe();
            VRLog.Alert(Name, $"{Tag}: the desync subscription had been dropped and was re-armed.");
        }
        catch (Exception e)
        {
            VRLog.Warn(Name, $"{Tag}: re-arm check threw: {e.GetType().Name}: {e.Message}");
        }
    }

    // ---------------------------------------------------------------------------------------
    // The report
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The listener. <c>OnDesyncDetected</c> is a plain multicast delegate — <c>Invoke</c> has no
    /// per-listener catch — and the game's own handler
    /// (<c>SceneController.ShowDesyncDetectionError</c>, the dialog with the one Main Menu button)
    /// may be behind us in the chain. A throw here would take that dialog with it and leave the
    /// player in VR with a dead session and no window. So: everything, unconditionally, wrapped.
    /// </summary>
    private static void OnDesyncDetected(Exception ex)
    {
        try
        {
            Report(ex);
        }
        catch (Exception own)
        {
            try
            {
                VRLog.Error(Name, $"{Tag}: the recorder itself threw and reported nothing: {own}");
            }
            catch
            {
                // Nothing left to do. Never rethrow: the game's dialog is downstream of us.
            }
        }
    }

    private static void Report(Exception ex)
    {
        string message = ex?.Message ?? "(no message)";
        string stack = ex?.StackTrace ?? string.Empty;
        bool modInStack = stack.IndexOf("GloomhavenVR", StringComparison.Ordinal) >= 0;

        var sb = new StringBuilder(1024);
        sb.Append('\n').Append(Tag).Append(" ==================================================\n");
        sb.Append(Tag).Append("  message : ").Append(message).Append('\n');
        sb.Append(Tag).Append("  verdict : ").Append(Classify(ex, modInStack)).Append('\n');
        sb.Append(Tag).Append("  build   : ModBuild ").Append(NetProtocol.ModBuild)
          .Append("   role: ").Append(Role()).Append('\n');

        AppendProcessor(sb);
        AppendQueue(sb);
        AppendPlayers(sb);

        sb.Append(Tag).Append("  stack   : ");
        sb.Append(string.IsNullOrEmpty(stack)
            ? "(none — the game constructed this Exception without one, which is normal for its\n"
              + Tag + "            timeout paths; the queue above is then the evidence)\n"
            : "\n" + stack + "\n");
        sb.Append(Tag).Append(" ==================================================");

        // Error tier on purpose: this ends the session, and since ModBuild 331 the default log is
        // quiet enough that an Error block is findable without asking for Debug.
        VRLog.Error(Name, sb.ToString());
    }

    /// <summary>
    /// Name the mechanism from the evidence, and say plainly when the mod is implicated. The
    /// classifier reads the game's own exception text; the four families come from enumerating
    /// every <c>HandleDesync</c> call site (see DESYNC-ANALYSIS.md §2-§5).
    /// </summary>
    private static string Classify(Exception? ex, bool modInStack)
    {
        if (modInStack)
            return "MOD-IMPLICATED — 'GloomhavenVR' appears in the stack below. Treat this as OURS "
                 + "until the frame is read: a throw inside a mod patch on a network dispatch path "
                 + "is shown to the player as the GAME's desynchronisation.";

        string m = ex?.Message ?? string.Empty;

        if (m.StartsWith("Error processing action. Timed out after", StringComparison.Ordinal))
            return "PHASE DEADLINE — the local client did not reach the action's phase in time. "
                 + "No state was compared (DESYNC-ANALYSIS.md §3). If the STALL line above fired, "
                 + "it names how long and on what.";

        if (m.IndexOf("Timed out waiting for clients to acknowledge", StringComparison.Ordinal) >= 0
            || m.IndexOf("State revisions do not match", StringComparison.Ordinal) >= 0)
            return "ACK TIMEOUT — the ready-up handshake gave up (§5). The client-side wait also "
                 + "requires ActionProcessor.IsProcessing to be false, so a client stuck mid-action "
                 + "fails this even with identical state.";

        if (ex is NullReferenceException)
            return "RECEIVER NULL — a network message was applied into a UI object that does not "
                 + "exist right now (§2/§4). 60 of ~121 game actions dereference a "
                 + "Singleton<T>.Instance with no null check; this is NOT a state divergence.";

        if (m.IndexOf("the event returns null", StringComparison.Ordinal) >= 0
            || m.IndexOf("the token provided returns null", StringComparison.Ordinal) >= 0)
            return "EVENT/TOKEN NULL — a Bolt event or its token failed to deserialise on arrival.";

        if (m.IndexOf("No controllable exists", StringComparison.Ordinal) >= 0)
            return "UNKNOWN CONTROLLABLE — a state update named a controllable this client has "
                 + "never registered.";

        return "UNCLASSIFIED — not one of the families in DESYNC-ANALYSIS.md. Worth adding.";
    }

    private static string Role()
    {
        try
        {
            if (FFSNetwork.IsHost)
                return "HOST";
            return FFSNetwork.IsClient ? "CLIENT" : "OFFLINE?";
        }
        catch
        {
            return "?";
        }
    }

    private static void AppendProcessor(StringBuilder sb)
    {
        try
        {
            sb.Append(Tag).Append("  phase   : ").Append(ActionProcessor.CurrentPhase)
              .Append("   state: ").Append(ActionProcessor.CurrentState.StateType)
              .Append("   processing: ").Append(ActionProcessor.IsProcessing).Append('\n');
            sb.Append(Tag).Append("  prev    : ").Append(ActionProcessor.PreviousState.StateType)
              .Append(" @ ").Append(ActionProcessor.PreviousState.PhaseType)
              .Append("   saved: ").Append(ActionProcessor.SavedState.StateType)
              .Append(" @ ").Append(ActionProcessor.SavedState.PhaseType).Append('\n');
            sb.Append(Tag).Append("  budget  : ").Append(BudgetSeconds().ToString("0.0"))
              .Append("s (").Append(ActionProcessor.MaxConsecutiveIncorrectActionsAllowed)
              .Append(" retries)").Append('\n');
        }
        catch (Exception e)
        {
            sb.Append(Tag).Append("  phase   : (unreadable: ").Append(e.GetType().Name).Append(")\n");
        }
    }

    /// <summary>
    /// The pending queue with each action's target phase against the current one. This single
    /// block is what tells the phase-deadline family apart from the receiver-null family at a
    /// glance: a queue full of MISMATCH lines is §3, an empty or matching queue is §2.
    /// </summary>
    private static void AppendQueue(StringBuilder sb)
    {
        try
        {
            Queue<GameAction>? queue = ActionProcessor.ActionQueue;
            int count = queue?.Count ?? 0;
            sb.Append(Tag).Append("  queue   : ").Append(count).Append(" pending\n");
            if (queue == null || count == 0)
                return;

            int current = (int)ActionProcessor.CurrentPhase;
            int i = 0;
            foreach (GameAction a in queue)
            {
                if (i >= MaxQueueLines)
                {
                    sb.Append(Tag).Append("            … ").Append(count - MaxQueueLines)
                      .Append(" more not listed\n");
                    break;
                }
                if (a == null)
                {
                    sb.Append(Tag).Append("            #").Append(i).Append(" (null action)\n");
                    i++;
                    continue;
                }
                bool mismatch = a.TargetPhaseID != 0 && a.TargetPhaseID != current;
                sb.Append(Tag).Append("            #").Append(i).Append(' ')
                  .Append(ActionName(a.ActionTypeID))
                  .Append(" @ ").Append(PhaseName(a.TargetPhaseID))
                  .Append(mismatch ? "  << MISMATCH" : "  ok")
                  .Append("   from player ").Append(a.PlayerID).Append('\n');
                i++;
            }
        }
        catch (Exception e)
        {
            sb.Append(Tag).Append("  queue   : (unreadable: ").Append(e.GetType().Name).Append(")\n");
        }
    }

    // NetworkPlayer is Bolt-derived (EntityBehaviour<IPlayerState>), so it is NEVER typed at
    // compile time here — same contract as NetPlayerActors / PlayerBadges. Reflection, resolved
    // once, and every failure degrades to a shorter report rather than a throw.
    private static bool _playerReflectionTried;
    private static FieldInfo? _allPlayersField;
    private static PropertyInfo? _playerIdProp;
    private static PropertyInfo? _usernameProp;
    private static PropertyInfo? _hasDesynchedProp;
    private static PropertyInfo? _isClientProp;

    private static void AppendPlayers(StringBuilder sb)
    {
        try
        {
            if (!_playerReflectionTried)
            {
                _playerReflectionTried = true;
                Type? registry = AccessTools.TypeByName("FFSNet.PlayerRegistry");
                Type? player = AccessTools.TypeByName("FFSNet.NetworkPlayer");
                _allPlayersField = registry == null ? null : AccessTools.Field(registry, "AllPlayers");
                _playerIdProp = player?.GetProperty("PlayerID");
                _usernameProp = player?.GetProperty("Username");
                _hasDesynchedProp = player?.GetProperty("HasDesynched");
                _isClientProp = player?.GetProperty("IsClient");
            }

            if (_allPlayersField?.GetValue(null) is not System.Collections.IEnumerable all)
            {
                sb.Append(Tag).Append("  players : (registry unreadable)\n");
                return;
            }

            sb.Append(Tag).Append("  players :");
            bool any = false;
            foreach (object p in all)
            {
                if (p == null)
                    continue;
                any = true;
                object? id = _playerIdProp?.GetValue(p);
                object? user = _usernameProp?.GetValue(p);
                object? desynched = _hasDesynchedProp?.GetValue(p);
                object? isClient = _isClientProp?.GetValue(p);
                sb.Append(' ').Append(user ?? "?").Append("(#").Append(id ?? "?")
                  .Append(((isClient as bool?) ?? false) ? ", CLIENT" : ", HOST")
                  .Append(((desynched as bool?) ?? false) ? ", DESYNCHED" : "")
                  .Append(')');
            }
            if (!any)
                sb.Append(" (none)");
            sb.Append('\n');
        }
        catch (Exception e)
        {
            sb.Append(Tag).Append("  players : (unreadable: ").Append(e.GetType().Name).Append(")\n");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Names
    // ---------------------------------------------------------------------------------------

    /// <summary>Enum name for a raw action id, falling back to the number. Both enums live in
    /// GH.Runtime and carry no Bolt types, so they are safe to name directly.</summary>
    private static string ActionName(int id)
    {
        try
        {
            return Enum.IsDefined(typeof(GameActionType), id)
                ? ((GameActionType)id).ToString()
                : $"#{id}";
        }
        catch
        {
            return $"#{id}";
        }
    }

    private static string PhaseName(int id)
    {
        try
        {
            return Enum.IsDefined(typeof(ActionPhaseType), id)
                ? ((ActionPhaseType)id).ToString()
                : $"#{id}";
        }
        catch
        {
            return $"#{id}";
        }
    }
}
