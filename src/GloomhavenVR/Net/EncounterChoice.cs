using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.Net.Desync;
using HarmonyLib;
using MapRuleLibrary.YML.Events;

namespace GloomhavenVR.Net;

/// <summary>
/// ANY PLAYER MAY ANSWER A CITY/ROAD ENCOUNTER — and the HOST still decides it.
///
/// <para>USER REQUEST (2026-09-05, verbatim, after a multiplayer session): <i>"Andere Spieler, die
/// nicht der Host sind, können keine Option in der Begegnung wählen — im Basisspiel kann das auch
/// nur der Host, das möchte ich hier aber ändern. Jeder Spieler kann auf eine Option drücken und
/// sie wird dann angenommen, egal ob Host oder nicht."</i></para>
///
/// <para><b>THE GAME'S PATH, READ FIRST.</b> The encounter window is <c>UIEventPanel</c>. Each
/// option is an <c>EventButton</c> built in <c>UpdateScreen</c>
/// (decompiled/GH.Runtime/UIEventPanel.cs:355-390); its click callback is
/// <c>ContinueEvent(button)</c> (:598) and the final "Leave" button's is <c>CompleteEvent(button)</c>
/// (:720). Both open with <c>if (FFSNetwork.IsHost)</c> and only then reach
/// <c>Synchronizer.SendGameAction(GameActionType.ContinueRoadEvent, ActionPhaseType.MapEvent, …)</c>
/// (:606, :610, :724). Every other client replays that action through
/// <c>UIEventPanel.ClientContinueRoadEvent</c> (:869), which looks the id up in its own
/// <c>eventButtons</c> and calls <c>EventButton.Click()</c> — i.e. the far side re-runs the SAME
/// callback locally. A client's press is refused one step earlier still:
/// <c>ClientButtonLocker.TryLockButton</c> writes <c>button.interactable = false</c> whenever
/// <c>FFSNetwork.IsClient</c>, and <c>EventButton</c> is
/// <c>[RequireComponent(typeof(ClientButtonLocker))]</c> (EventButton.cs:7, :88).</para>
///
/// <para><b>WHY THE OBVIOUS IMPLEMENTATION IS A CAMPAIGN-KILLER, MEASURED AND NOT ASSUMED.</b> The
/// clean-looking move is to let a client originate the game's own action — a client may legally do
/// that, <c>Synchronizer.ReplicateAction</c> ends with
/// <c>if (FFSNetwork.IsClient) SendGameActionToPlayer(gameAction, PlayerRegistry.HostPlayer);</c>
/// (Synchronizer.cs:189-191), and <c>UIReadyToggle.ReadyUpPlayer</c> does exactly that from a
/// client (UIReadyToggle.cs:664). <b>It does not work for THIS action, and the host's own log says
/// why.</b> A <c>GameActionEvent</c> arriving at the host is ENQUEUED
/// (<c>NetworkCallbacks.OnEvent → ActionProcessor.QueueUpAction</c>, NetworkCallbacks.cs:183) and
/// only runs while <c>readyToProcessNextAction</c> — and <c>UIEventPanel.ShowEvent</c> puts the
/// HOST into <c>ActionProcessor.SetState(ActionProcessorStateType.Halted, ActionPhaseType.MapEvent)</c>
/// for the whole encounter (:210). In <c>.planning/debug/second_logs/Player.log</c> the host prints
/// <c>STATE: Halted @ MapEvent</c> once at line 34422 and its NEXT state line is
/// <c>STATE: ProcessFreely @ MapLoadoutScreen</c> at 45384 — nothing in between, with both of its
/// own <c>ContinueRoadEvent</c> sends (#11 at 42440, #12 at 43234) inside that window. A queued
/// client action would therefore sit unprocessed until the phase moved on, and then fail
/// <c>action.TargetPhaseID != (int)currentState.PhaseType</c> in
/// <c>TryProcessNextAction</c> — the "incorrect action detected" countdown that ends in a THROW,
/// which <c>ActionProcessor</c> hands to <c>FFSNetwork.HandleDesync</c> and the player sees as the
/// game's "Desynchronization occurred" dialog.</para>
///
/// <para><b>THE CHANNEL THAT DOES WORK IS THE GAME'S OWN CLIENT-TO-HOST REQUEST CHANNEL.</b>
/// <c>Synchronizer.SendSideAction(…, sendToHostOnly: true)</c> travels as a
/// <c>NetworkActionEvent</c> and is delivered by <c>ActionProcessor.ProcessSideAction</c>
/// (ActionProcessor.cs:175-199), which calls <c>action.Execute()</c> DIRECTLY — no queue, no phase
/// test, no <c>readyToProcessNextAction</c>. That is the same channel the game uses for every
/// "client tells the host something happened on my machine" message it has:
/// <c>GameLoadedAndClientReady</c> (StoryController.cs:138), <c>ReadyForAssignment</c>
/// (MapChoreographer.cs:3091), <c>EndOfTurnClientReady</c> (Choreographer.cs:14196),
/// <c>EndOfRoundCompareClientFinished</c> (GHClientCallbacks.cs:838), <c>PingHex</c> with a payload
/// token (UIScenarioMultiplayerController.cs:345).</para>
///
/// <para><b>SO THE SHAPE IS: THE MOD CARRIES THE INTENT, THE GAME MAKES THE DECISION.</b> A
/// client's press sends ONE side action naming the option it wants and the screen it was looking
/// at, and advances NOTHING locally. The host validates it and, if it holds, calls the host's own
/// <c>EventButton.Click()</c>. From there not one line of this file is involved: the game's
/// unmodified <c>ContinueEvent</c> runs on the host, sends its own real
/// <c>GameActionType.ContinueRoadEvent</c> to every client exactly as a host press would, and every
/// machine — the one that pressed included — advances through
/// <c>ClientContinueRoadEvent</c>. <b>No game state is written by the mod, no second source of
/// truth exists, and the host is still the only machine that decides an encounter.</b></para>
///
/// <para><b>TWO PLAYERS PRESSING AT ONCE: FIRST-IN-WINS, AND THE LOSER SEES THE OUTCOME.</b>
/// <c>ProcessSideAction</c> is synchronous, so the host applies request A completely — its own
/// screen advance and its own broadcast included — before request B is dispatched. B then names a
/// screen the host has left, and/or an option id that is no longer in <c>eventButtons</c>, and is
/// REFUSED by a named term. Nothing is stranded: the winner's advance is broadcast to everybody,
/// so the losing player's window moves to the same new screen. And because the client's button is
/// never consumed locally, a request that is refused or lost can simply be pressed again — there
/// is no state here that can dead-end.</para>
///
/// <para><b>WHAT IS NOT CLAIMED.</b> That <c>GameActionType.ContinueRoadEvent</c> ever travels as a
/// side action in the unmodified game — it does not, which is exactly why the request is
/// recognisable: <c>GameAction(NetworkActionEvent)</c> (GameAction.cs:1022-1032) leaves
/// <c>TargetPhaseID</c> at 0, while all three of the game's own senders pass
/// <c>ActionPhaseType.MapEvent</c> (= 2). <see cref="IsRemotePressRequest"/> requires that zero
/// AND the boolean marker AND a non-zero screen stamp, so a vanilla arrival can never match and
/// the game's own replay path is untouched.</para>
/// </summary>
internal static class EncounterChoice
{
    private const string Scope = "Net";

    /// <summary>
    /// Greater than zero while this machine is REPLAYING somebody else's press — i.e. inside
    /// <c>UIEventPanel.ClientContinueRoadEvent</c>, which drives <c>EventButton.Click()</c> and so
    /// re-enters <c>ContinueEvent</c>/<c>CompleteEvent</c> through the ordinary callback. Without
    /// it a client would answer the host's broadcast with a fresh request and the encounter would
    /// ping-pong. A DEPTH and not a bool because the pairing is what matters, and it is released
    /// from a Harmony FINALIZER so the vanilla throw at UIEventPanel.cs:878 ("No button with ID …")
    /// cannot leave it latched — a leaked latch here would silently turn every later press on this
    /// machine back into the flat game's host-only behaviour.
    /// </summary>
    private static int _replayDepth;

    /// <summary>Unconditional liveness counter for the HW-VERIFY lines: how many local presses this
    /// process has seen on an encounter option, host or client, dispatched or refused. A report
    /// whose press line never appears at all is a different defect from one where it appears and
    /// says REFUSED, and this number is what separates them.</summary>
    private static int _pressesSeen;

    internal static void EnterReplay() => _replayDepth++;

    internal static void ExitReplay()
    {
        if (_replayDepth > 0)
            _replayDepth--;
    }

    /// <summary>
    /// IS THE MACHINE THAT WOULD HAVE TO HONOUR A REQUEST ACTUALLY RUNNING THIS MOD? — and this
    /// is a SAFETY gate, not a preference.
    ///
    /// <para>The request travels as <c>GameActionType.ContinueRoadEvent</c> on the side-action
    /// channel, and an UNMODDED host would run the vanilla
    /// <c>UIEventPanel.ClientContinueRoadEvent</c> on it, whose <c>SupplementaryDataIDMed</c> a
    /// side action leaves at 0 — so it would press the FIRST option of whatever screen it is on, or
    /// throw <c>"No button with ID 0 found"</c> straight into <c>FFSNetwork.HandleDesync</c>. That
    /// is a rules divergence and a killed campaign, so a flat host must never be sent one. The
    /// registry that answers this is the mod's own: <see cref="VersionGuard.IsModdedPeer"/> holds
    /// every peer a valid GVR1 packet has been seen from, and a flat player is never in it (a
    /// MISMATCHING mod build is a different matter and already has its own blocking dialog).</para>
    ///
    /// <para>The host is <c>PlayerRegistry.HostPlayerID</c>, which the game fixes at 1
    /// (decompiled PlayerRegistry.cs:37). <see cref="NetSession.FlatNetMode"/> is the user's own
    /// "join as a flat player" choice and turns every mod net path off for the session, this one
    /// included.</para>
    ///
    /// <para>The SAME term gates the button unlock, deliberately: a control the player can press
    /// that could never do anything is the dead end this project does not ship. Offline it is
    /// vacuously true and nothing about single player changes, because
    /// <c>ClientButtonLocker</c> only ever locks while <c>FFSNetwork.IsClient</c>.</para>
    /// </summary>
    /// <remarks>
    /// <b>THE <c>FFSNetwork.IsHost</c> TERM IS NOT A CONVENIENCE — WITHOUT IT THIS METHOD CALLS THE
    /// HOST UNMODDED ON THE HOST'S OWN MACHINE.</b> <see cref="VersionGuard.Peers"/> is fed only
    /// from RECEIVED packets and the receive path drops the local echo
    /// (<c>NetAvatarDriver.OnPacketReceived</c>: <c>if (senderId != 0 &amp;&amp; senderId ==
    /// _transport.LocalPlayerId) return;</c>), so a machine never registers ITSELF and
    /// <c>IsModdedPeer(PlayerRegistry.HostPlayerID)</c> — the host is fixed at player 1,
    /// PlayerRegistry.cs:37 — is false on the host forever. No BEHAVIOUR depended on that: the
    /// send path returns at <c>!FFSNetwork.IsClient</c> long before it asks, and the game's own
    /// <c>ClientButtonLocker.TryLockButton</c> writes nothing unless <c>FFSNetwork.IsClient</c>, so
    /// this term flipping true on a host changes no button on any machine. <b>THE INSTRUMENT IS
    /// WHAT WAS BROKEN, AND THAT IS WHY IT MATTERS:</b> <c>VersionGuard.NoteMixedSession</c> reads
    /// this method for its consequence clause while reading the ROSTER rows through
    /// <c>isLocal || IsModdedPeer(id)</c>, so the host's own census printed
    /// <c>player 1 (HOST) (THIS CLIENT) = MODDED</c> and then "THE HOST IS NOT A MODDED PEER" in
    /// the same line — and <c>VersionGuard</c>:279-283 names that clause as THE FALSIFIER for "the
    /// handshake is not arriving". A hardware round would have been spent chasing a handshake that
    /// was working.
    /// </remarks>
    internal static bool HostCanHonourRequests() =>
        !NetSession.FlatNetMode
        && (!FFSNetwork.IsOnline
            || FFSNetwork.IsHost                 // I AM the host, so the host is running this mod
            || VersionGuard.IsModdedPeer(PlayerRegistry.HostPlayerID));

    /// <summary>
    /// TRUE for the GameObject of an encounter option button, and for nothing else. IS-A and not
    /// containment: <c>EventButton</c> is <c>[RequireComponent(typeof(ClientButtonLocker))]</c>, so
    /// the two components sit on the SAME GameObject and <c>GetComponent</c> here answers "this
    /// locker belongs to an encounter option", never "something near it does".
    /// <c>ClientButtonLocker</c> can be wired onto other prefabs through its serialized
    /// <c>button</c> field with no code reference, so the test is on identity rather than on the
    /// component's type alone.
    /// </summary>
    internal static bool IsEncounterOptionButton(ClientButtonLocker? locker) =>
        locker != null && locker.GetComponent<EventButton>() != null;

    /// <summary>The whole unlock predicate, kept as one named thing so the patch body reads as the
    /// question it asks: leave the game's client lock in place UNLESS this is an encounter option
    /// AND there is a host on the far side that would honour the press.</summary>
    internal static bool MayUnlock(ClientButtonLocker? locker)
    {
        if (!IsEncounterOptionButton(locker))
            return false;   // not our button; the game's own lock decides, and there is nothing to say
        bool may = HostCanHonourRequests();
        // SAY IT ONLY WHERE THERE IS A LOCK TO SPEAK ABOUT. The verdict is about the game's own
        // client lock, and ClientButtonLocker.TryLockButton writes nothing unless
        // FFSNetwork.IsClient — so on the HOST and offline no line here can be true: the old
        // REFUSED text named a host that is this very machine, and the UNLOCKED text would claim
        // "this CLIENT's buttons" on a machine that is not a client. Absent is the honest reading
        // for both, and NoteUnlockVerdict's own doc now says so.
        if (FFSNetwork.IsOnline && FFSNetwork.IsClient)
            NoteUnlockVerdict(may);
        return may;
    }

    /// <summary>Change-gate for <see cref="NoteUnlockVerdict"/>. <c>TryLockButton</c> runs on every
    /// encounter option button's <c>OnEnable</c> — four or so per screen, every screen of every
    /// encounter — so the verdict may only print on a TRANSITION. Paired with
    /// <see cref="VersionGuard.SessionEpoch"/> so a second session in the same process re-states
    /// its own verdict instead of inheriting the first one's silence.</summary>
    private static bool _unlockVerdict;
    private static int _unlockVerdictEpoch = -1;

    /// <summary>
    /// SAY WHY THE ENCOUNTER OPTIONS ARE, OR ARE NOT, PRESSABLE ON THIS CLIENT.
    ///
    /// <para><b>THE REFUSAL USED TO BE INVISIBLE, AND IT IS THE FLAT-HOST CASE.</b> Against an
    /// unmodded host <see cref="HostCanHonourRequests"/> is false, so this method's caller leaves
    /// the game's own <c>ClientButtonLocker</c> in place and the buttons stay dead — correctly. But
    /// the only line this file had was inside <see cref="OnLocalPress"/>, which a LOCKED button can
    /// never reach, so the whole session said nothing at all about a control the player can see and
    /// not press. This is the observation half; <c>VersionGuard</c>'s MIXED SESSION CENSUS is the
    /// roster half, and neither is derived from the other.</para>
    /// </summary>
    private static void NoteUnlockVerdict(bool may)
    {
        if (_unlockVerdictEpoch == VersionGuard.SessionEpoch && _unlockVerdict == may)
            return;
        _unlockVerdictEpoch = VersionGuard.SessionEpoch;
        _unlockVerdict = may;
        // HW-VERIFY: were this client's encounter option buttons given back, and if not, by which
        // term? THE FALSIFIER: in a MODDED-ONLY session this must read UNLOCKED — a LOCKED verdict
        // there means the host's handshake is not arriving, which is a bug and not a flat player.
        // Absent altogether means one of three things and proves none of them: no encounter option
        // button was built on this machine this session, OR this machine is the HOST, OR the
        // session is offline — the caller only speaks while FFSNetwork.IsClient, because the game's
        // lock this line is about is never applied anywhere else. Change-gated, so it is at most a
        // couple of lines per session and never one per button.
        VRLog.Note(Scope, may
            ? "ENCOUNTER OPTION UNLOCK: this CLIENT's encounter option buttons are UNLOCKED — the "
              + "mod skips the game's own ClientButtonLocker for them because the HOST is a modded "
              + "peer that will honour a request. A press advances NOTHING locally; it travels to "
              + "the host, which presses its own copy and broadcasts the game's real "
              + "GameActionType.ContinueRoadEvent to everyone."
            : "ENCOUNTER OPTION UNLOCK: REFUSED — this CLIENT's encounter option buttons keep the "
              + "game's own client lock (ClientButtonLocker.TryLockButton runs unmodified), by "
              + "term: "
              + (NetSession.FlatNetMode
                  ? "NetSession.FlatNetMode — this player chose to join as a flat player, so every "
                    + "mod net path is off"
                  : $"the HOST (player {PlayerRegistry.HostPlayerID}) is not a modded peer — it is "
                    + "running the unmodded game, which would read our request's unset "
                    + "SupplementaryDataIDMed as option 0 and throw 'No button with ID 0 found' "
                    + "straight into FFSNetwork.HandleDesync, killing the session for everybody")
              + ". THIS IS NOT A STALL AND NOT A DEAD CONTROL: the buttons are visibly greyed, "
              + "exactly as in the unmodded game, and the host answers the encounter for the table "
              + "as it always did.");
    }

    /// <summary>
    /// Does this dispatched action carry a VR client's option request rather than the game's own
    /// replicated press? Pure property reads on a plain C# object — it cannot throw, which is why
    /// it is evaluated OUTSIDE the dispatch guard: the guard's fallback has to differ between the
    /// two cases (see <see cref="UIEventPanel_ClientContinueRoadEvent_Patch"/>).
    /// </summary>
    internal static bool IsRemotePressRequest(GameAction? action) =>
        action != null
        && action.TargetPhaseID == 0        // a side action; the game always sends MapEvent (= 2)
        && action.SupplementaryDataBoolean  // our marker, set nowhere else
        && action.DataInt2 != 0;            // the screen stamp, forced non-zero by ScreenStamp

    /// <summary>
    /// WHICH SCREEN OF WHICH ENCOUNTER THE PRESSER WAS LOOKING AT, as one non-zero int — the term
    /// that decides the two-players-press-at-once race on the host.
    ///
    /// <para>A stamp and not the strings themselves because the mod holds NO compile-time reference
    /// to the Photon-Bolt assemblies (see <see cref="FfsNetTransport"/>: everything FFSNet goes
    /// through reflection so no csproj edit and no bolt.dll dependency exists), and the game's own
    /// <c>RoadEventToken</c> is an <c>IProtocolToken</c> — a bolt type. <c>NetworkAction</c> already
    /// carries two plain ints and a bool, which is all this needs.</para>
    ///
    /// <para>FNV-1a over the event id and the screen name, which are YAML keys
    /// (<c>CRoadEvent.ID</c>, <c>CRoadEventScreen.Name</c>) and therefore byte-identical on every
    /// machine regardless of UI language — never a translated string. Folded to a non-zero int so
    /// that "no stamp" (0) stays distinguishable from a real one, which is what
    /// <see cref="IsRemotePressRequest"/> leans on. A collision would let a stale press through in
    /// the one case where the option id ALSO still matches; the id check runs anyway, so a
    /// collision costs a wrong-screen press at ~2^-32, not a desync.</para>
    /// </summary>
    internal static int ScreenStamp(string? eventId, string? screenName)
    {
        unchecked
        {
            uint h = 2166136261u;
            for (int i = 0; i < (eventId?.Length ?? 0); i++)
            {
                h ^= eventId![i];
                h *= 16777619u;
            }
            h ^= 1u;      // the separator, so ("ab","c") and ("a","bc") differ
            h *= 16777619u;
            for (int i = 0; i < (screenName?.Length ?? 0); i++)
            {
                h ^= screenName![i];
                h *= 16777619u;
            }
            int stamp = (int)h;
            return stamp == 0 ? 1 : stamp;
        }
    }

    // -----------------------------------------------------------------------------------------
    // The one FFSNet call, reached the way every other FFSNet call in this module is
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Send the request. FALSE when FFSNet could not be resolved on this build — the caller then
    /// holds the press back and says so, rather than pretending it travelled.
    ///
    /// <para>The reflected call itself lives in <see cref="SideActionRequest"/>, shared with
    /// <see cref="EnemyInfoContinue"/>: there are two client-to-host request features now, and
    /// <c>SendSideAction</c>'s eight parameters are positional and untyped through reflection, so a
    /// second copy of that argument ORDER is a duplicate that would go wrong silently.
    /// <c>targetPlayerID: 0</c> is this feature's own value and stays here — an encounter request
    /// must be PROCESSED by the receiving host, which vanilla only does for 0 or its own id
    /// (ActionProcessor.cs:188), whereas the enemy-info request rides the mod's sentinel and wants
    /// exactly the opposite.</para>
    /// </summary>
    private static bool SendRequest(int optionId, int screenStamp) =>
        SideActionRequest.Send(
            GameActionType.ContinueRoadEvent, // actionType — the game's own, not an invented one
            targetPlayerId: 0,                // 0 = the receiver processes it
            dataInt: optionId,
            dataInt2: screenStamp,
            dataBool: true);                  // the marker IsRemotePressRequest reads

    // -----------------------------------------------------------------------------------------
    // The local press
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Runs for EVERY press of an encounter option on this machine, from
    /// <c>ContinueEvent</c>/<c>CompleteEvent</c>. Returns TRUE to let the game's own callback run
    /// (host, offline, or a replay of somebody else's press) and FALSE to hold it back while the
    /// request travels to the host.
    /// </summary>
    internal static bool OnLocalPress(UIEventPanel? panel, EventButton? button, bool leaving)
    {
        // A replay is the far side of somebody's press arriving — the game's own callback must run
        // untouched, on host and client alike. Checked FIRST: it is the one case where "we are a
        // client" is true and yet nothing may be sent. Deliberately BEFORE the press counter, so
        // that counter stays a count of HANDS on this machine.
        if (_replayDepth > 0)
            return true;

        // Offline, or the host: the flat game is already correct and this file has no opinion.
        if (!FFSNetwork.IsOnline || !FFSNetwork.IsClient)
            return true;

        if (panel == null || button == null)
            return true;

        _pressesSeen++;

        int optionId = button.ID;
        string optionName = button.Name ?? "<unnamed>";
        CRoadEvent? data = panel.eventData;
        CRoadEventScreen? screen = panel.currentScreen;
        // The game's own ContinueEvent has a branch for exactly this pair being unavailable
        // (UIEventPanel.cs:609) and sends the id alone there; the request keeps the same tolerance
        // by stamping whatever is there, empty strings included.
        string eventId = data != null && data.ID != null ? data.ID : string.Empty;
        string screenName = screen != null && screen.Name != null ? screen.Name : string.Empty;
        int stamp = ScreenStamp(eventId, screenName);

        int playerId = NetPlayerActors.LocalPlayerId();

        // WHY THESE THREE TERMS AND NOT A try/catch AROUND THE SEND. PlayerRegistry.MyPlayer is
        // dereferenced inside Synchronizer.SendSideAction to build the NetworkAction, and the whole
        // send is dropped once the session has desynchronised; HostCanHonourRequests is the safety
        // gate that keeps a flat host from being handed a message it would mis-read. Deciding here
        // rather than downstream is what lets the log name the term. Holding the local advance back
        // in every one of them is the SAFE half: a client that advanced on its own would be exactly
        // the divergence this file exists to avoid.
        bool haveMe = playerId > 0;
        bool sent = haveMe && !FFSNetwork.HasDesynchronized && HostCanHonourRequests()
                    && SendRequest(optionId, stamp);

        if (!sent)
        {
            // HW-VERIFY: a press this line names never reached the host, and it says which term
            // stopped it. A stuck-encounter report carrying this line is answered by that term
            // alone — nothing further down ever ran.
            VRLog.Note(Scope, $"ENCOUNTER OPTION PRESS #{_pressesSeen}: CLIENT (player {playerId}) "
                            + $"pressed option '{optionName}' (id {optionId}, "
                            + $"{(leaving ? "LEAVE" : "choice")}) on screen "
                            + $"'{(screenName.Length > 0 ? screenName : "<unknown>")}' of event "
                            + $"'{(eventId.Length > 0 ? eventId : "<unknown>")}' "
                            + $"(screen stamp 0x{stamp:X8}) — REFUSED BEFORE SENDING, by term: "
                            + (!haveMe
                                ? "this client has no PlayerRegistry.MyPlayer yet"
                                : FFSNetwork.HasDesynchronized
                                    ? "FFSNetwork.HasDesynchronized — the session is already down"
                                    : NetSession.FlatNetMode
                                        ? "NetSession.FlatNetMode — this player chose to join as a "
                                          + "flat player, so every mod net path is off"
                                        : !VersionGuard.IsModdedPeer(PlayerRegistry.HostPlayerID)
                                            ? "the HOST is not a modded peer "
                                              + "(VersionGuard.IsModdedPeer("
                                              + $"{PlayerRegistry.HostPlayerID}) is false) — an "
                                              + "unmodded host would read this request's unset "
                                              + "SupplementaryDataIDMed as option 0 and press the "
                                              + "wrong thing, or desync"
                                            : "FFSNet.Synchronizer.SendSideAction did not resolve "
                                              + "on this game build")
                            + ". NOTHING was written locally and nothing went on the wire; the host "
                            + "is expected to do nothing, because it was never told. THE PRESS IS "
                            + "NOT CONSUMED — the button stays interactable and pressing again "
                            + "re-runs this whole path, so this is never a dead end. Presses seen "
                            + $"this session: {_pressesSeen}.");
            return false;
        }

        // HW-VERIFY: the client half of the press. WHO pressed (CLIENT + player id), WHICH option
        // (name + id + whether it was the Leave button), WHETHER it was dispatched, BY WHICH TERM,
        // and WHAT THE OTHER SIDE SHOULD DO. Pair it with the host's ENCOUNTER OPTION REQUEST line
        // carrying the same option id: this line present with no host line means the side action
        // did not arrive; both present with the host saying REFUSED means somebody else's press
        // won the race. Once per press, never per frame.
        VRLog.Note(Scope, $"ENCOUNTER OPTION PRESS #{_pressesSeen}: CLIENT (player {playerId}) "
                        + $"pressed option '{optionName}' (id {optionId}, "
                        + $"{(leaving ? "LEAVE" : "choice")}) on screen "
                        + $"'{(screenName.Length > 0 ? screenName : "<unknown>")}' of event "
                        + $"'{(eventId.Length > 0 ? eventId : "<unknown>")}' (screen stamp "
                        + $"0x{stamp:X8}, which is the term the host uses to settle a race) — "
                        + "DISPATCHED to the "
                        + "host as a Synchronizer.SendSideAction(ContinueRoadEvent, "
                        + "sendToHostOnly: true) REQUEST, which is the game's own client-to-host "
                        + "channel and the only one that is not blocked by the host's "
                        + "Halted @ MapEvent. THE LOCAL ADVANCE IS HELD BACK ON PURPOSE: nothing "
                        + "moved on this machine and nothing was written to the game. WHAT THE "
                        + "OTHER SIDE IS EXPECTED TO DO: the host validates the request, presses "
                        + "its OWN copy of this button, and its unmodified ContinueEvent sends the "
                        + "real GameActionType.ContinueRoadEvent to everyone — including back to "
                        + "this machine, which is what advances this window. If this window does "
                        + "NOT advance, read the host's ENCOUNTER OPTION REQUEST line for the "
                        + $"refusing term. Presses seen this session: {_pressesSeen}.");
        return false;
    }

    // -----------------------------------------------------------------------------------------
    // The host's judgement
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A client's option request has arrived at the host, outside the action queue and outside the
    /// <c>Halted @ MapEvent</c> that would have swallowed a queued <c>GameActionEvent</c>. Validate
    /// it against the host's OWN panel and, if it holds, press the host's own button. Everything
    /// after that <c>Click()</c> is the game's code: <c>ContinueEvent</c> on the host sends the real
    /// <c>ContinueRoadEvent</c> to every client exactly as a host press would.
    /// </summary>
    private static void ApplyRemoteRequest(UIEventPanel? panel, GameAction action)
    {
        int fromPlayer = action.PlayerID;
        int optionId = action.DataInt;
        int wantStamp = action.DataInt2;
        int haveStamp = 0;

        string refusal = string.Empty;
        EventButton? target = null;
        string haveEvent = "<no panel>";
        string haveScreen = "<no panel>";
        int offered = 0;
        bool open = false;

        if (NetSession.FlatNetMode)
        {
            // FLAT-NET MODE, AND IT IS CHECKED HERE RATHER THAN AT THE SEAM ON PURPOSE. "Als
            // Flat-Spieler joinen" means every mod net path is off (NetSession.cs:11-23) and this
            // is one — the send side has always had the term (HostCanHonourRequests), the receive
            // side never did, so a flat-mode host still clicked its own buttons on a peer's
            // request and every REFUSED line's claim that "every mod net path is off" was false.
            // IT MUST STILL CONSUME. This request rides the game's REAL
            // GameActionType.ContinueRoadEvent, so declining to recognise it at the transport seam
            // would hand it to the game's dispatch — the unguarded Singleton deref AND a vanilla
            // body that reads the unset SupplementaryDataIDMed as option 0. Refusing by a named
            // term costs the far player one press they can repeat; passing it through costs the
            // campaign. Unreachable in practice, because a flat-mode host sends no GVR1 packets
            // either, so no client ever marks it modded and no request is raised against it — this
            // is the term that makes that a decision instead of a coincidence.
            refusal = "NetSession.FlatNetMode — the player on this machine chose to join as a flat "
                    + "player, so every mod net path is off for the rest of the session and this "
                    + "machine answers no requests";
        }
        else if (panel == null)
        {
            refusal = "there is no UIEventPanel on this machine";
        }
        else
        {
            CRoadEvent? data = panel.eventData;
            CRoadEventScreen? screen = panel.currentScreen;
            haveEvent = data != null && data.ID != null ? data.ID : "<none>";
            haveScreen = screen != null && screen.Name != null ? screen.Name : "<none>";
            haveStamp = ScreenStamp(data != null ? data.ID : null,
                                    screen != null ? screen.Name : null);
            open = panel.IsOpen;
            offered = panel.eventButtons != null ? panel.eventButtons.Count : 0;

            if (!open)
            {
                // The host has already left this encounter — a press that was in flight while it
                // did. Refusing is the whole point: the far side is being advanced by the host's
                // own broadcast anyway.
                refusal = "the host's encounter window is no longer open (UIWindow.IsOpen=false)";
            }
            else if (wantStamp != haveStamp)
            {
                // THE RACE TERM. Two players pressed; somebody else's request was applied first and
                // moved the host on, so this one is answering a screen that no longer stands. It is
                // also the term that catches a request for a DIFFERENT encounter entirely, because
                // the stamp folds the event id and the screen name together.
                refusal = $"the request answers screen stamp 0x{wantStamp:X8} and the host is on "
                        + $"0x{haveStamp:X8} (event '{haveEvent}', screen '{haveScreen}') — either "
                        + "another player's press was applied first, or this one is for a screen "
                        + "the host has already left";
            }
            else if (panel.eventButtons == null)
            {
                refusal = "the host's panel has no option list";
            }
            else
            {
                // The game's own lookup, term for term: UIEventPanel.cs:871. Deliberately NOT a
                // re-implementation of what a matching id means — the same list, the same
                // predicate, so a button this finds is a button the game would have found.
                foreach (EventButton candidate in panel.eventButtons)
                {
                    if (candidate != null && candidate.ID == optionId)
                    {
                        target = candidate;
                        break;
                    }
                }
                if (target == null)
                    refusal = $"no option with id {optionId} is on the host's current screen "
                            + $"('{haveScreen}' offers {offered})";
            }
        }

        if (refusal.Length == 0 && target != null)
        {
            // The press itself, and the ONLY thing this file does to the game. EventButton.Click
            // invokes the callback the game wired in UpdateScreen — ContinueEvent or CompleteEvent
            // — on the HOST, whose FFSNetwork.IsHost branch then does all the real work.
            target.Click();
        }

        // HW-VERIFY: the host half of a remote press. WHO pressed (a CLIENT, with its player id),
        // WHICH option, whether the host DISPATCHED or REFUSED it and by which term, and what the
        // other side is expected to do next. A stuck-encounter report is decided here: DISPATCHED
        // means the game's own ContinueRoadEvent went to every client and the far window should
        // have moved; REFUSED names the exact term, and "another player's press was applied first"
        // is the normal, correct outcome of two people pressing together. Once per request.
        VRLog.Note(Scope, $"ENCOUNTER OPTION REQUEST #{_pressesSeen}: HOST received a CLIENT "
                        + $"(player {fromPlayer}) request for option id {optionId} carrying screen "
                        + $"stamp 0x{wantStamp:X8}. HOST STATE: window open={open}, showing event "
                        + $"'{haveEvent}' screen '{haveScreen}' (stamp 0x{haveStamp:X8}) with "
                        + $"{offered} option button(s). "
                        + (refusal.Length == 0
                            ? "DISPATCHED — the host pressed its OWN copy of that button, so the "
                              + "game's unmodified ContinueEvent/CompleteEvent ran here and sent "
                              + "the real GameActionType.ContinueRoadEvent to every client. WHAT "
                              + "THE OTHER SIDE IS EXPECTED TO DO: advance through its own "
                              + "ClientContinueRoadEvent, exactly as it does for a host press. "
                              + "NOTHING WAS WRITTEN BY THE MOD — the click is the game's."
                            : $"REFUSED, by term: {refusal}. NOTHING was pressed and nothing went "
                              + "on the wire from here. WHAT THE OTHER SIDE IS EXPECTED TO DO: "
                              + "advance anyway, because whoever DID win the race had the host "
                              + "broadcast their choice to everyone; and the refused player's "
                              + "button is never consumed, so pressing again is always available.")
                        + $" Presses seen on this machine this session: {_pressesSeen}.");
    }

    /// <summary>Entry point for the dispatch prefix — see
    /// <see cref="UIEventPanel_ClientContinueRoadEvent_Patch"/>. Returns TRUE to let the game's own
    /// replay run, FALSE when this was a client's request that the host has now judged.</summary>
    internal static bool OnDispatch(UIEventPanel? panel, GameAction? action, bool remote)
    {
        if (!remote || action == null)
            return true;
        ApplyRemoteRequest(panel, action);
        return false;
    }

    /// <summary>
    /// CONSUME THE REQUEST AT THE TRANSPORT SEAM, BECAUSE THE GAME'S DISPATCH DEREFERENCES A
    /// SINGLETON THAT IS NOT ALWAYS THERE — and no prefix of ours can save it.
    ///
    /// <para><b>THE TRAP, READ IN THE GAME'S OWN SOURCE.</b> This request rides the game's real
    /// <c>GameActionType.ContinueRoadEvent</c>, whose dispatch entry is
    /// <c>Singleton&lt;UIEventPanel&gt;.Instance.ClientContinueRoadEvent(a)</c>
    /// (decompiled/GH.Runtime/FFSNet/GameAction.cs:189-193) with <b>no null test</b>.
    /// <c>UIEventPanel</c> is a plain <c>Singleton&lt;T&gt;</c> — a static field written in
    /// <c>Awake</c> and set back to <c>null</c> in <c>OnDestroy</c> (Singleton.cs:11-19) — and it
    /// carries no <c>DontDestroyOnLoad</c>, so it is null the moment the host leaves the campaign
    /// map scene. A client's press that was in flight while the host left therefore throws a
    /// <c>NullReferenceException</c> at the CALL SITE, i.e. before
    /// <c>ClientContinueRoadEvent</c> is entered, so
    /// <see cref="UIEventPanel_ClientContinueRoadEvent_Patch"/>'s prefix — and with it
    /// <see cref="ApplyRemoteRequest"/>'s own "there is no UIEventPanel on this machine" refusal —
    /// <b>cannot run</b>. <c>GameAction.Execute</c> has no catch of its own (GameAction.cs:1047),
    /// so it lands in <c>ActionProcessor.ProcessSideAction</c>'s
    /// <c>catch { FFSNetwork.HandleDesync(ex); throw; }</c> (ActionProcessor.cs:194-198), and on a
    /// HOST <c>HandleDesync</c> ends in <c>Shutdown()</c> (FFSNetwork.cs:80-83) — the whole table's
    /// session, not just the host's.</para>
    ///
    /// <para><b>SO THE FIX IS THE PATTERN THE TWO SIBLING REQUESTS ALREADY USE:</b> recognise the
    /// request in <see cref="FfsNetTransport"/>'s existing <c>ProcessSideAction</c> prefix and
    /// return FALSE, so <c>Execute()</c> — and with it the unguarded deref — never runs at all. The
    /// judgement then reads <c>Singleton&lt;UIEventPanel&gt;.Instance</c> HERE, where a null is a
    /// value and not a throw, and <see cref="ApplyRemoteRequest"/>'s existing refusal line finally
    /// becomes reachable.</para>
    ///
    /// <para><b>NOTHING IS TAKEN AWAY FROM A MODDED RECEIVER.</b> Today the request reaches
    /// <see cref="ApplyRemoteRequest"/> through <c>Execute()</c> → the dispatch table → our prefix
    /// on <c>ClientContinueRoadEvent</c>; after this it reaches the SAME method one call earlier.
    /// <c>ProcessSideAction</c> contributes nothing else on this path: its
    /// <c>TargetPlayerID == 0 || == MyPlayer.PlayerID</c> test (:188) is satisfied by construction
    /// because <see cref="SendRequest"/> sends <c>targetPlayerId: 0</c>, and <c>Execute</c>'s
    /// return value is discarded by the caller.</para>
    ///
    /// <para><b>AND NOTHING IS TAKEN AWAY FROM AN UNMODDED ONE.</b> The request is only ever SENT
    /// when <see cref="HostCanHonourRequests"/> holds, which requires
    /// <c>VersionGuard.IsModdedPeer(PlayerRegistry.HostPlayerID)</c> — a host is only in that
    /// registry once a valid GVR1 packet has arrived FROM it, and those packets come out of
    /// <see cref="FfsNetTransport"/>. A host whose transport did not install therefore never
    /// appears modded, so no client ever sends it one; the receive seam this method is called from
    /// is guaranteed installed on every machine that can receive a request. It also never travels
    /// to a non-host at all: <see cref="SideActionRequest.Send"/> passes
    /// <c>sendToHostOnly: true</c>, which <c>Synchronizer.SendSideAction</c> turns into
    /// <c>GlobalTargets.OnlyServer</c> (Synchronizer.cs:28) — an unmodded CLIENT never receives
    /// this action in the first place.</para>
    ///
    /// <para><b>THE REPLAY DEPTH IS RAISED HERE FOR THE SAME REASON THE PREFIX RAISED IT</b>: the
    /// judgement drives <c>EventButton.Click()</c>, which re-enters <c>ContinueEvent</c>, and a
    /// machine that somehow judged a request while counting as a CLIENT must run the game's own
    /// advance instead of answering with a fresh request. Released in a <c>finally</c> so a throw
    /// inside the judgement cannot latch it — the same contract the
    /// <c>[HarmonyFinalizer]</c> gives on the other seam.</para>
    ///
    /// <para>Returns TRUE when the action was OURS and has been dealt with (the caller then skips
    /// vanilla), FALSE for anything else — every vanilla side action included. A vanilla
    /// <c>ContinueRoadEvent</c> can never match: the game sends that type only through
    /// <c>Synchronizer.SendGameAction</c> (UIEventPanel.cs:606/:610/:724), never as a side action,
    /// and all three of those pass <c>ActionPhaseType.MapEvent</c> while
    /// <see cref="IsRemotePressRequest"/> demands <c>TargetPhaseID == 0</c>.</para>
    /// </summary>
    internal static bool TryHandleSideAction(object? action)
    {
        if (action is not GameAction ga)
            return false;
        if (ga.ActionTypeID != (int)GameActionType.ContinueRoadEvent || !IsRemotePressRequest(ga))
            return false;
        // CONSUMED EITHER WAY. The action is ours, so the game's dispatch must not see it; a throw
        // inside the judgement is named by DispatchGuard and costs the requesting player one press,
        // which they can simply repeat because nothing about it was consumed on their machine.
        EnterReplay();
        try
        {
            DispatchGuard.Run("EncounterChoice.ApplyRemoteRequest",
                () => ApplyRemoteRequest(Singleton<UIEventPanel>.Instance, ga));
        }
        finally
        {
            ExitReplay();
        }
        return true;
    }
}

// ---------------------------------------------------------------------------------------------
// The patches
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Give a CLIENT its encounter option buttons back. <c>ClientButtonLocker.TryLockButton</c> writes
/// <c>button.interactable = false</c> whenever <c>FFSNetwork.IsClient</c>
/// (decompiled/GH.Runtime/ClientButtonLocker.cs:20-26), and it is the ONLY thing standing between a
/// non-host player and the press the user asked for. Skipped for encounter option buttons and for
/// nothing else — the game's own availability write (<c>EventButton.SetAvailable</c>,
/// EventButton.cs:151) still stands, so an option whose CONDITIONS fail stays greyed out.
/// </summary>
[HarmonyPatch(typeof(ClientButtonLocker), "TryLockButton")]
internal static class ClientButtonLocker_TryLockButton_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(ClientButtonLocker __instance) =>
        DispatchGuard.Run("ClientButtonLocker.TryLockButton(EncounterChoice)",
            () => !EncounterChoice.MayUnlock(__instance),
            onThrow: true);
}

/// <summary>
/// The option press. A prefix on <c>UIEventPanel.ContinueEvent(EventButton)</c>
/// (decompiled/GH.Runtime/UIEventPanel.cs:598), the callback every non-final encounter option is
/// wired to at :366 and :381.
/// </summary>
[HarmonyPatch(typeof(UIEventPanel), "ContinueEvent")]
internal static class UIEventPanel_ContinueEvent_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(UIEventPanel __instance, EventButton eventButton)
    {
        // Read OUTSIDE the guard, because the guard's fallback differs by side. On the HOST a
        // throw must fall back to vanilla (true) so the host's own press still works; on a CLIENT
        // it must NOT (false), because vanilla on a client advances the screen locally and tells
        // nobody — the divergence this file exists to avoid. Both are one property read on
        // BoltNetwork and cannot themselves throw.
        bool amClient = FFSNetwork.IsOnline && FFSNetwork.IsClient;
        return DispatchGuard.Run("UIEventPanel.ContinueEvent(EncounterChoice)",
            () => EncounterChoice.OnLocalPress(__instance, eventButton, leaving: false),
            onThrow: !amClient);
    }
}

/// <summary>
/// The "Leave" press that ends the encounter. A prefix on
/// <c>UIEventPanel.CompleteEvent(EventButton)</c> (decompiled/GH.Runtime/UIEventPanel.cs:720), the
/// callback <c>FinalScreen</c> wires at :688. Same rule as the option press: the client asks, the
/// host presses, the game's own <c>ContinueRoadEvent</c> closes the window everywhere.
/// </summary>
[HarmonyPatch(typeof(UIEventPanel), "CompleteEvent")]
internal static class UIEventPanel_CompleteEvent_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(UIEventPanel __instance, EventButton buttonPressed)
    {
        bool amClient = FFSNetwork.IsOnline && FFSNetwork.IsClient;
        return DispatchGuard.Run("UIEventPanel.CompleteEvent(EncounterChoice)",
            () => EncounterChoice.OnLocalPress(__instance, buttonPressed, leaving: true),
            onThrow: !amClient);
    }
}

/// <summary>
/// Two jobs on one seam, <c>UIEventPanel.ClientContinueRoadEvent(GameAction)</c>
/// (decompiled/GH.Runtime/UIEventPanel.cs:869).
///
/// <para>(1) It is a SECOND, unreachable-in-practice line of defence for a CLIENT'S REQUEST. A
/// request is now consumed one call earlier, in <see cref="EncounterChoice.TryHandleSideAction"/>
/// off the mod's existing <c>ProcessSideAction</c> prefix, because the game's dispatch entry for
/// this action type derefences <c>Singleton&lt;UIEventPanel&gt;.Instance</c> unguarded and an NRE
/// there is thrown before this method is ever entered — see that method's own doc comment for the
/// source lines and for why a prefix here cannot help. This branch is kept because it costs one
/// property read and its terms are exact: if a request ever did reach the dispatch table (a game
/// build whose <c>ProcessSideAction</c> the transport could not patch), the vanilla body would read
/// the unset <c>SupplementaryDataIDMed</c> as option 0 and press the wrong thing. A vanilla arrival
/// matches none of the three terms and the original runs untouched.</para>
///
/// <para>(2) It marks the REPLAY WINDOW. The vanilla body calls <c>EventButton.Click()</c>, which
/// re-enters <c>ContinueEvent</c> — and on a client that must run the game's own advance, not send
/// a fresh request. The depth is raised at the very top of the prefix and released in a FINALIZER,
/// which Harmony runs whether the original was skipped, returned, or threw the
/// <c>"No button with ID …"</c> exception at :878. The finalizer returns <c>void</c>, so the game's
/// own exception is preserved exactly — a real desync must still be reported.</para>
/// </summary>
[HarmonyPatch(typeof(UIEventPanel), nameof(UIEventPanel.ClientContinueRoadEvent))]
internal static class UIEventPanel_ClientContinueRoadEvent_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(UIEventPanel __instance, GameAction action)
    {
        // FIRST statement, before anything that could throw, so the finalizer's release is always
        // paired with a raise.
        EncounterChoice.EnterReplay();
        bool remote = EncounterChoice.IsRemotePressRequest(action);
        // onThrow: a vanilla arrival falls back to the game's own replay (true); a client request
        // must NOT (false) — vanilla would read SupplementaryDataIDMed 0 and either press the
        // wrong option or throw into HandleDesync. Dropping it is recoverable; the player presses
        // again and the DISPATCH GUARD line names the bug.
        return DispatchGuard.Run("UIEventPanel.ClientContinueRoadEvent(EncounterChoice)",
            () => EncounterChoice.OnDispatch(__instance, action, remote),
            onThrow: !remote);
    }

    [HarmonyFinalizer]
    private static void Finalizer() => EncounterChoice.ExitReplay();
}
