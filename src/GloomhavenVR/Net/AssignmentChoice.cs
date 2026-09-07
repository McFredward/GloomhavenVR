using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.Net.Desync;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// ANY PLAYER MAY OPERATE THE ASSIGNMENT WINDOW — and the HOST still decides it.
///
/// <para><b>USER REQUEST (2026-09-07, item 3, verbatim):</b> <i>"Das Zuweisungsfenster (zB wer
/// welchen Gegenstand verliert, Gold erhält etc), soll ein Multiplayerfenster werden und jeder soll
/// es bedienen können (wie bei der Story und der Begegnung auch)."</i></para>
///
/// <para><b>THE PRECEDENT HE CITES IS OUR OWN FIX, WHICH IS WHY THIS FILE IS A COPY AND NOT AN
/// INVENTION.</b> The "Begegnung" is host-only in the unmodified game —
/// <c>UIEventPanel.ContinueEvent</c>:600 and <c>CompleteEvent</c>:722 are both inside
/// <c>if (FFSNetwork.IsHost)</c>, and <c>EventButton</c> is
/// <c>[RequireComponent(typeof(ClientButtonLocker))]</c> whose <c>TryLockButton</c> writes
/// <c>button.interactable = false</c> whenever <c>FFSNetwork.IsClient</c>
/// (ClientButtonLocker.cs:22-24). It is operable for him because
/// <see cref="EncounterChoice"/> granted exactly this request on 2026-09-05. Item 3 is that request
/// again for the next window, so this file follows that one term for term: the same channel, the
/// same "the mod carries the intent, the game makes the decision" shape, the same refusal lines.
/// The scenario STORY box, by contrast, is genuinely everyone-operable in vanilla —
/// <c>StoryController</c> has no <c>IsHost</c> term on its advance path at all — so it is a
/// precedent for a shared DISPLAY and not for this.</para>
///
/// <para><b>WHAT WAS WRONG, IN ONE SENTENCE.</b> The assignment family gates operability on WHO IS
/// HOST, in a game that already knows WHOSE CHARACTER IT IS
/// (<c>CMapCharacter.IsUnderMyControl</c>, CMapCharacter.cs:107), and it nevertheless makes every
/// client sit and watch: <c>UIDistributeRewardManager.Process</c>:34-41 runs an all-player ready-up
/// through <c>UIMapMultiplayerController.ShowRewardsMultiplayer</c> (:108-132,
/// <c>GUI_WAIT_PLAYERS_CONFIRM_TIP</c>) before <c>ProcessSP</c>, and nothing on the raise path is
/// host-gated. Both ModBuild 474 hardware logs agree — <c>DISTRIBUTE POPUP FLOATED: 'UI Distribute
/// Items Rewards Popup'</c> occurs exactly once on the host and exactly once on the peer, with no
/// <c>NOT FLOATED</c> line on either. See <c>WorldUI/Modal/AssignmentWindows.cs</c> for the census
/// that reads this on hardware; this file is the remedy it named.</para>
///
/// <para><b>THE FOUR GATES THIS FILE UNDOES, AND IT UNDOES THEM ONLY IN THE VIEW.</b> All four are
/// <c>FFSNetwork.IsHost</c> and all four are independent, which is why relaxing one would not have
/// been a fix:
/// <list type="number">
/// <item><c>UIDistributePointsSlot.EnableAddPoints</c>:249 / <c>EnableRemovePoints</c>:260 — the
/// +/- buttons, scoped to this family by <c>m_IsRewards</c> (set only where
/// <c>UIDistributeReward.Distribute</c>:61 passes <c>isRewards: true</c>).</item>
/// <item><c>UIDistributeReward.SetButtonInteractable</c>:110 — the confirm button; on a gamepad it
/// is <c>SetActive(false)</c>, i.e. absent rather than greyed.</item>
/// <item>every point send is inside <c>if (FFSNetwork.IsHost)</c> — DistributeGoldProcess:100/139,
/// DistributeItemsProcess:72/90/110, DistributeAttackModifierProcess:57/75,
/// DistributeConditionsProcess:57/75, DistributeGoldBagProcess:72/90/105 — and so is the confirm
/// send, <c>UIDistributeReward.OnConfirmClick</c>:142.</item>
/// <item><c>UIDistributePointsPopup.ShowHotkeys</c>:50-63 — the gamepad keycaps. NOT touched here:
/// VR is not gamepad mode, and a keycap hint is not a control.</item>
/// </list>
/// Gates 1 and 2 are re-opened by a POSTFIX that re-applies the game's OWN answer — the
/// <c>enable</c> / <c>interactable</c> argument those methods were already handed, which is
/// <c>service.CanAddPointsTo(actor)</c> and <c>service.AvailablePoints == 0</c> respectively. So an
/// option the RULES refuse stays refused; only the host-ness term is dropped. Gate 3 is never
/// touched at all: a client's press does not send anything of the game's, it sends a REQUEST.</para>
///
/// <para><b>THE CHANNEL, AND WHY IT IS THE SENTINEL RATHER THAN A REAL <c>GameActionType</c>.</b>
/// A client may NOT originate <c>SendGameAction(DistributeUIAddPoint, …)</c> here. An arriving
/// <c>GameActionEvent</c> is ENQUEUED (<c>NetworkCallbacks.OnEvent → ActionProcessor.QueueUpAction</c>)
/// and runs only while <c>readyToProcessNextAction</c>; the three distribute actions carry
/// <c>ActionPhaseType.NONE</c>, so <c>TryProcessNextAction</c>'s
/// <c>action.TargetPhaseID != (int)currentState.PhaseType</c> test ends in the "incorrect action
/// detected" countdown and a throw into <c>FFSNetwork.HandleDesync</c> — the player's
/// "Desynchronization occurred" dialog and a killed campaign. That is
/// <see cref="EncounterChoice"/>'s recorded finding and it transfers unchanged. So the request rides
/// the mod's own side-action sentinel (<see cref="NetProtocol.SentinelActionTypeId"/> with
/// <see cref="NetProtocol.SentinelTargetPlayerId"/>), tagged
/// <see cref="NetProtocol.SideRequestAssignmentPress"/> in <c>DataInt</c> — the same channel
/// <see cref="EnemyInfoContinue"/> uses, and it is strictly the better one:
/// <c>ActionProcessor.ProcessSideAction</c> reaches <c>Execute()</c> only when
/// <c>TargetPlayerID == 0 || == MyPlayer.PlayerID</c> (:188), and no real player owns
/// <c>int.MaxValue</c>, so <b>a FLAT or unmodded host hits the "Ignoring SideAction" branch and
/// executes nothing at all.</b> There is no mis-read to gate against, only a dead control — which
/// <see cref="MayOperateHere"/> prevents by keeping the buttons greyed against such a host.</para>
///
/// <para><b>NO NEW PATCH ON A NETWORK RECEIVER, AND NO WIRE BYTES.</b> The receive seam is the
/// mod's EXISTING prefix on <c>FFSNet.ActionProcessor.ProcessSideAction</c> in
/// <see cref="FfsNetTransport"/>, which already consumes the sentinel; this file adds ONE branch
/// there rather than a second prefix (two prefixes on one method are order-dependent, because a
/// prefix returning FALSE suppresses the ones after it). The payload rides
/// <c>NetworkAction.DataInt</c> / <c>DataInt2</c> / <c>DataBoolean</c>, three fields the GAME's own
/// token already serialises on every side action (NetworkAction.cs:44-53). <b>Zero bytes are added
/// to the GVR1 packet, so the mod's own wire worst case is unchanged at 1747 of
/// <c>PresenceSerializer.MaxSize</c> 2100, and no extension id is consumed — 45 is still next
/// free.</b></para>
///
/// <para><b>SO THE SHAPE IS: THE CLIENT ASKS, THE HOST PRESSES, THE GAME BROADCASTS.</b> A client's
/// press advances NOTHING locally and sends one request naming the process type, the slot and the
/// operation. The host validates it against its OWN live popup and then presses its OWN widget —
/// <c>UIDistributePointsSlot.AddPoint(isProxyCall: false)</c> or
/// <c>UIDistributeReward.OnConfirmClick()</c>, both public and both the very methods the host's own
/// button is wired to. From there not one line of this file is involved: the game's unmodified
/// <c>service.AddPoint</c> takes its <c>if (FFSNetwork.IsHost)</c> branch and sends the real
/// <c>DistributeUIAddPoint</c>/<c>DistributeUIConfirm</c> to every client, which replay it through
/// <c>UIDistributeRewardManager.ProxyAddPoints</c>/<c>ProxyConfirmClick</c> (:118/:131/:145, wired
/// at GameAction.cs:678-696). <b>No game state is written by the mod, no second source of truth
/// exists, and the host is still the only machine that decides an assignment.</b></para>
///
/// <para><b>THE REPLAY CANNOT PING-PONG, AND THE TERM IS THE GAME'S.</b>
/// <see cref="EncounterChoice"/> needed a replay-depth counter because the far side's replay
/// re-enters the same callback. This one does not: <c>ProxyAddPoint</c> calls
/// <c>m_Service.AddPoint(actor)</c> DIRECTLY (DistributeItemsProcess.cs:240-247 and the four
/// siblings) and never goes through <c>UIDistributePointsSlot.AddPoint</c>, so the method this file
/// prefixes is reached by a local HAND and by nothing else. The <c>isProxyCall</c> parameter is
/// passed through untouched anyway, so a future replay that did use it is already correct.</para>
///
/// <para><b>TWO PLAYERS PRESSING AT ONCE.</b> <c>ProcessSideAction</c> is synchronous, so the host
/// applies request A completely — its own press and its own broadcast included — before request B
/// is dispatched, and B is then judged against the POST-A state by the game's own
/// <c>addPointButton.interactable</c> (which <c>RefreshAssignedPoints</c> has just rewritten from
/// <c>service.CanAddPointsTo</c>). A request that no longer holds is refused by a named term and
/// nothing is stranded: the winner's press was broadcast to everybody, and the loser's button is
/// never consumed locally, so pressing again is always available. For +/- there is usually no race
/// to lose at all — two players assigning different points is the feature, not a conflict.</para>
///
/// <para><b>WHAT IS DELIBERATELY NOT COVERED, and each is a decision rather than an oversight:</b>
/// <list type="bullet">
/// <item><b>The scenario "who burns a card to prevent damage" select popup</b>
/// (<c>DistributeSelectPlayerActorService</c>, host-gated at :113/:120/:145). Its PRESS is the same
/// mechanism and would be the same three patches — but its COMMIT is not in this window at all: it
/// is the board's Ready button, and <c>Choreographer</c>:12087 arms that with
/// <c>readyButton.SetInteractable(interactable: true, FFSNetwork.IsOnline &amp;&amp;
/// FFSNetwork.IsHost)</c>. Unlocking the slots without that button would hand a client a selection
/// it can make and never confirm — the dead control this project's rulings forbid — and the button
/// lives on <c>Choreographer</c>, a type <c>scripts/check-desync-surface.py</c> lists as a network
/// receiver (27 dispatched actions), so patching it needs a <c>docs/NET-ACTION-SURFACE.md</c> row.
/// It is one commit's work on top of this one and it needs that ledger row, which is why it is
/// named here rather than half-shipped.</item>
/// <item><b>The scenario redistribute-damage popup</b> (<c>DistributeDamageService</c>). ALREADY
/// per-player-owned rather than host-owned — <c>caster.IsUnderMyControl</c> at :153/:162 and the
/// send at :183. It is the shape the user is asking for, not an instance of the defect, and
/// touching it would break a flow that works.</item>
/// <item><b>The reward SHOWCASE</b> (<c>UICampaignRewardWindow</c> / <c>UIRewardsManager</c>) — it
/// displays what was won and decides nothing, so there is nothing to operate.</item>
/// <item><b>The corner NETWORK BADGE and the synced POSE</b> — the other half of
/// "Multiplayerfenster". Refused deliberately: <c>WorldUI.SharedWindowKind</c> is resolved by
/// <c>SharedWindows.KindOf(UIWindow)</c> and this popup carries NO <c>UIWindow</c> (its Show/Hide
/// are a bare <c>window.SetActive</c>, UIDistributePointsPopup.cs:124/141), while the pose publisher
/// reaches its handle through <c>ModalFallback.TryGetGrabFor</c>, which returns a
/// <c>GrabbableModal</c> — and this window's handle is a <c>SurfaceGrabBar</c>. A kind byte added
/// to <c>NetProtocol</c> today would therefore have no publisher and no applier: a dead kind that
/// reads as shipped. What it needs first is a <c>GrabbableModal</c> for a surface-floated panel,
/// which is <c>WorldUI/Surfaces/FloatingDecisionSurfaces.cs</c>, <c>WorldUI/Grab/GrabbableModal.cs</c>
/// and <c>Net/Remote/RemoteMapStory.cs</c> — three files, none of them this one's.</item>
/// </list></para>
/// </summary>
internal static class AssignmentChoice
{
    private const string Scope = "Net";

    // ---------------------------------------------------------------------------------------
    // The live window. Two public seams, no reflection into private fields, no scanning.
    // ---------------------------------------------------------------------------------------

    /// <summary>The <c>UIDistributeReward</c> currently distributing, or null. Written by the
    /// postfix on its own public <c>Distribute</c> and cleared when its popup hides. There is at
    /// most one: <c>UIDistributeRewardManager.Distribute</c> chains
    /// <c>callbackPromise.Then(() =&gt; process.Process(rewards))</c>, so the five processes run
    /// strictly one after another and never overlap.</summary>
    private static UIDistributeReward? _liveReward;

    /// <summary>The popup that reward is showing — <c>UIDistributeReward.PopUp</c>, a public
    /// property. Held separately because <c>Hide</c> is what tells us the flow ended, and because
    /// <c>UIDistributePointsSlot</c> is shared with the SCENARIO popups: membership of THIS list is
    /// what scopes every patch below to the reward family.</summary>
    private static UIDistributePointsPopup? _livePopup;

    /// <summary>Which of the five processes is distributing, as the game's own enum value. Rides
    /// the request so the host can refuse one that answers a process it has already left.</summary>
    private static DistributeRewardProcess.EDistributeRewardProcessType _liveType;

    /// <summary>The game's own "all points are assigned" answer, captured at its source: it is the
    /// <c>interactable</c> argument <c>UIDistributeReward.SetInteractable</c> hands to
    /// <c>SetButtonInteractable</c>, i.e. <c>service.AvailablePoints == 0</c>. Read on the host to
    /// judge a confirm request rather than re-deriving the rule, and captured rather than read off
    /// the button because in gamepad mode the game writes <c>SetActive</c> instead of
    /// <c>interactable</c> and the field would be stale.</summary>
    private static bool _confirmReady;

    /// <summary>Unconditional liveness counter for the HW-VERIFY lines: how many local presses this
    /// process has seen on an assignment control, host or client, dispatched or refused. A report
    /// whose press line never appears at all is a different defect from one where it appears and
    /// says REFUSED.</summary>
    private static int _pressesSeen;

    internal static void NoteLive(UIDistributeReward? reward,
                                  DistributeRewardProcess.EDistributeRewardProcessType type)
    {
        _liveReward = reward;
        _livePopup = reward != null ? reward.PopUp : null;
        _liveType = type;
        _confirmReady = false;
    }

    internal static void NoteConfirmReady(bool ready) => _confirmReady = ready;

    /// <summary>Clear when the popup that hid is the one we were tracking. Guarded on identity so a
    /// SCENARIO popup hiding cannot drop the map-side flow's registration.</summary>
    internal static void NoteHidden(UIDistributePointsPopup? popup)
    {
        if (popup == null || !ReferenceEquals(popup, _livePopup))
            return;
        _liveReward = null;
        _livePopup = null;
        _confirmReady = false;
    }

    /// <summary>Is this slot one of the LIVE REWARD popup's? The scope test for every patch below.
    /// <c>UIDistributePointsSlot</c> is also used by the two scenario popups, which this feature
    /// deliberately does not cover, and membership of the live list is the only test that
    /// distinguishes them without a second copy of the game's own taxonomy.</summary>
    internal static bool IsRewardSlot(UIDistributePointsSlot? slot, out int index)
    {
        index = -1;
        if (slot == null || _livePopup == null || _livePopup.Slots == null)
            return false;
        for (int i = 0; i < _livePopup.Slots.Count; i++)
        {
            if (ReferenceEquals(_livePopup.Slots[i], slot))
            {
                index = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>Is this the LIVE reward UI? The scope test for the confirm patches.</summary>
    internal static bool IsLiveReward(UIDistributeReward? reward) =>
        reward != null && ReferenceEquals(reward, _liveReward);

    // ---------------------------------------------------------------------------------------
    // The unlock predicate
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// MAY THIS CLIENT'S ASSIGNMENT CONTROLS BE GIVEN BACK? — a DEAD-CONTROL gate, not a safety one.
    ///
    /// <para>Unlike <see cref="EncounterChoice.HostCanHonourRequests"/> there is no mis-read to
    /// protect against: the request rides the sentinel <c>TargetPlayerID</c>, so an unmodded or
    /// flat host ignores it without executing (ActionProcessor.cs:188). What remains is the
    /// project's own ruling — a control the player can press that could never do anything is a dead
    /// end this mod does not ship — so the buttons stay greyed, exactly as in the unmodded game,
    /// unless there is a modded host on the far side that will honour a press.</para>
    ///
    /// <para>Offline and on the host it is vacuously irrelevant: the game already grants those
    /// machines the buttons, and every patch below returns early for them.</para>
    /// </summary>
    internal static bool MayOperateHere()
    {
        if (!FFSNetwork.IsOnline || !FFSNetwork.IsClient)
            return false;                     // the host and single player already have the buttons
        bool may = !NetSession.FlatNetMode
                   && VersionGuard.IsModdedPeer(PlayerRegistry.HostPlayerID);
        NoteUnlockVerdict(may);
        return may;
    }

    /// <summary>Change-gate for <see cref="NoteUnlockVerdict"/>. The two Enable* methods run once
    /// per slot per refresh — a handful per press — so the verdict may only print on a TRANSITION.
    /// Paired with <see cref="VersionGuard.SessionEpoch"/> so a second session in the same process
    /// re-states its own verdict instead of inheriting the first one's silence.</summary>
    private static bool _unlockVerdict;
    private static int _unlockVerdictEpoch = -1;

    private static void NoteUnlockVerdict(bool may)
    {
        if (_unlockVerdictEpoch == VersionGuard.SessionEpoch && _unlockVerdict == may)
            return;
        _unlockVerdictEpoch = VersionGuard.SessionEpoch;
        _unlockVerdict = may;
        // HW-VERIFY: were this CLIENT's assignment controls given back, and if not, by which term?
        // THE FALSIFIER: in a MODDED-ONLY session this must read UNLOCKED — a LOCKED verdict there
        // means the host's handshake is not arriving, which is a bug and not a flat player. Absent
        // altogether means no assignment window was ever raised on this machine this session, which
        // proves nothing either way.
        VRLog.Note(Scope, may
            ? "ASSIGNMENT CONTROL UNLOCK: this CLIENT's +/- and CONFIRM controls on the loot/gold "
              + "assignment window are UNLOCKED — the mod re-applies the game's OWN answer "
              + "(service.CanAddPointsTo / AvailablePoints == 0) after dropping only the "
              + "FFSNetwork.IsHost term, because the HOST is a modded peer that will honour a "
              + "request. A press advances NOTHING locally; it travels to the host, which presses "
              + "its own copy and the game's real DistributeUIAddPoint/DistributeUIConfirm goes to "
              + "everyone. An option the RULES refuse is still refused."
            : "ASSIGNMENT CONTROL UNLOCK: REFUSED — this CLIENT keeps the game's own host-only "
              + "greying on the assignment window (UIDistributePointsSlot.EnableAddPoints:249 and "
              + "UIDistributeReward.SetButtonInteractable:110 run unmodified), by term: "
              + (NetSession.FlatNetMode
                  ? "NetSession.FlatNetMode — this player chose to join as a flat player, so every "
                    + "mod net path is off"
                  : $"the HOST (player {PlayerRegistry.HostPlayerID}) is not a modded peer, so a "
                    + "request would be ignored by its ActionProcessor (the sentinel "
                    + "TargetPlayerID is not its player id) and the button would be a dead control")
              + ". THIS IS NOT A STALL: the controls are visibly greyed exactly as in the unmodded "
              + "game, and the host completes the assignment for the table as it always did.");
    }

    // ---------------------------------------------------------------------------------------
    // The payload
    // ---------------------------------------------------------------------------------------

    /// <summary>Add a point to a slot.</summary>
    internal const int OpAdd = 0;

    /// <summary>Take a point back off a slot.</summary>
    internal const int OpRemove = 1;

    /// <summary>Press the window's CONFIRM.</summary>
    internal const int OpConfirm = 2;

    /// <summary>
    /// The whole request in one int, carried in <c>NetworkAction.DataInt2</c>.
    ///
    /// <para>Layout, low bits first: 4 bits slot index (0-15, and a party is at most 4), 2 bits
    /// operation, 4 bits process type (the game's own <c>EDistributeRewardProcessType</c>, 1-5),
    /// then a fixed marker bit. The marker is what makes the packed value ALWAYS NON-ZERO, which
    /// the seam leans on exactly as <see cref="EnemyInfoContinue"/> does: the transport pre-boxes
    /// both data ints at 0 for every cosmetic rig/extras packet it will ever send, so a zero can
    /// never be read as a request.</para>
    /// </summary>
    internal static int Pack(int slot, int op, int processType) =>
        (slot & 0xF) | ((op & 0x3) << 4) | ((processType & 0xF) << 6) | (1 << 10);

    internal static void Unpack(int packed, out int slot, out int op, out int processType)
    {
        slot = packed & 0xF;
        op = (packed >> 4) & 0x3;
        processType = (packed >> 6) & 0xF;
    }

    private static bool SendRequest(int packed) =>
        SideActionRequest.Send((GameActionType)NetProtocol.SentinelActionTypeId,
                               NetProtocol.SentinelTargetPlayerId,
                               dataInt: NetProtocol.SideRequestAssignmentPress,
                               dataInt2: packed,
                               dataBool: true);

    private static string OpName(int op) => op switch
    {
        OpAdd => "ADD a point",
        OpRemove => "REMOVE a point",
        OpConfirm => "CONFIRM",
        _ => $"<unknown op {op}>",
    };

    // ---------------------------------------------------------------------------------------
    // The local press
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Runs for EVERY press of an assignment control on this machine. Returns TRUE to let the
    /// game's own callback run (host, offline, or a control outside this family) and FALSE to hold
    /// it back while the request travels to the host.
    /// </summary>
    internal static bool OnLocalPress(int slotIndex, int op, string what)
    {
        // Offline, or the host: the flat game is already correct and this file has no opinion.
        if (!FFSNetwork.IsOnline || !FFSNetwork.IsClient)
            return true;

        _pressesSeen++;

        int playerId = NetPlayerActors.LocalPlayerId();
        int type = (int)_liveType;
        int packed = Pack(slotIndex, op, type);

        // WHY THESE TERMS AND NOT A try/catch AROUND THE SEND. PlayerRegistry.MyPlayer is
        // dereferenced inside Synchronizer.SendSideAction to build the NetworkAction, and the whole
        // send is dropped once the session has desynchronised; MayOperateHere is the dead-control
        // gate. Deciding here rather than downstream is what lets the log name the term. Holding
        // the local advance back in every one of them is the SAFE half: a client that advanced on
        // its own would write its OWN service's AvailablePoints/AssignedPoints and tell nobody,
        // and service.Apply() runs unconditionally on every client (UIDistributeReward.cs:81) — a
        // silent, PERSISTED divergence, which is exactly what this file exists to avoid.
        bool haveMe = playerId > 0;
        bool sent = haveMe && !FFSNetwork.HasDesynchronized && MayOperateHere()
                    && SendRequest(packed);

        if (!sent)
        {
            // HW-VERIFY: a press this line names never reached the host, and it says which term
            // stopped it. A stuck-assignment report carrying this line is answered by that term
            // alone — nothing further down ever ran.
            VRLog.Note(Scope, $"ASSIGNMENT PRESS #{_pressesSeen}: CLIENT (player {playerId}) "
                            + $"pressed {OpName(op)} on {what} of the {_liveType} assignment "
                            + $"(slot {slotIndex}, packed 0x{packed:X}) — REFUSED BEFORE SENDING, "
                            + "by term: "
                            + (!haveMe
                                ? "this client has no PlayerRegistry.MyPlayer yet"
                                : FFSNetwork.HasDesynchronized
                                    ? "FFSNetwork.HasDesynchronized — the session is already down"
                                    : NetSession.FlatNetMode
                                        ? "NetSession.FlatNetMode — this player chose to join as a "
                                          + "flat player, so every mod net path is off"
                                        : !VersionGuard.IsModdedPeer(PlayerRegistry.HostPlayerID)
                                            ? "the HOST is not a modded peer "
                                              + $"(VersionGuard.IsModdedPeer("
                                              + $"{PlayerRegistry.HostPlayerID}) is false) — it "
                                              + "would ignore the request's sentinel TargetPlayerID "
                                              + "without executing, so the control should not have "
                                              + "been pressable at all; read the ASSIGNMENT "
                                              + "CONTROL UNLOCK line above"
                                            : "FFSNet.Synchronizer.SendSideAction did not resolve "
                                              + "on this game build")
                            + ". NOTHING was written locally and nothing went on the wire. THE "
                            + "PRESS IS NOT CONSUMED — the control stays as it was and pressing "
                            + "again re-runs this whole path, so this is never a dead end. Presses "
                            + $"seen this session: {_pressesSeen}.");
            return false;
        }

        // HW-VERIFY: the client half of the press. WHO pressed, WHICH control, WHETHER it was
        // dispatched, and WHAT THE OTHER SIDE SHOULD DO. Pair it with the host's ASSIGNMENT REQUEST
        // line carrying the same packed value: this line present with no host line means the side
        // action did not arrive; both present with the host saying REFUSED names the game's own
        // term that refused it.
        VRLog.Note(Scope, $"ASSIGNMENT PRESS #{_pressesSeen}: CLIENT (player {playerId}) pressed "
                        + $"{OpName(op)} on {what} of the {_liveType} assignment (slot "
                        + $"{slotIndex}, packed 0x{packed:X}) — DISPATCHED to the host as a "
                        + "Synchronizer.SendSideAction(sentinel, sendToHostOnly: true) REQUEST, "
                        + "which is the game's own client-to-host channel and the only one the "
                        + "distribute actions' ActionPhaseType.NONE does not turn into a desync. "
                        + "THE LOCAL PRESS IS HELD BACK ON PURPOSE: nothing moved on this machine "
                        + "and nothing was written to the game — which matters here more than it "
                        + "did for the encounter, because service.Apply() runs on EVERY client and "
                        + "would have persisted a local-only assignment. WHAT THE OTHER SIDE IS "
                        + "EXPECTED TO DO: the host validates the request against its own popup, "
                        + "presses its OWN copy of that control, and the game's unmodified code "
                        + "sends the real DistributeUIAddPoint/DistributeUIConfirm to everyone — "
                        + "including back to this machine, which is what moves this window. If it "
                        + "does NOT move, read the host's ASSIGNMENT REQUEST line for the refusing "
                        + $"term. Presses seen this session: {_pressesSeen}.");
        return false;
    }

    // ---------------------------------------------------------------------------------------
    // The host's judgement
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A request has arrived on the mod's side-action channel. Called from
    /// <see cref="FfsNetTransport"/>'s existing <c>ProcessSideAction</c> prefix with the raw
    /// <c>object</c> that prefix was handed. Returns TRUE when the action was OURS and has been
    /// dealt with (the caller then skips vanilla), FALSE for anything else — including
    /// <see cref="EnemyInfoContinue"/>'s requests and the transport's own rig/extras packets.
    /// </summary>
    internal static bool TryHandleSideAction(object? action)
    {
        if (action is not GameAction ga)
            return false;
        if (!ga.SupplementaryDataBoolean
            || ga.ActionTypeID != NetProtocol.SentinelActionTypeId
            || ga.DataInt != NetProtocol.SideRequestAssignmentPress
            || ga.DataInt2 == 0)                   // the marker bit forces this non-zero
            return false;
        // CONSUMED EITHER WAY. The action is ours, so vanilla must not see it; a throw inside the
        // judgement is named by DispatchGuard and costs the requesting player one press, which they
        // can simply repeat because nothing about it was consumed on their machine.
        DispatchGuard.Run("AssignmentChoice.ApplyRequest", () => ApplyRequest(ga));
        return true;
    }

    /// <summary>
    /// Validate a client's request against the HOST's own live assignment window and, if it holds,
    /// press the host's own control. Everything after that call is the game's code.
    /// </summary>
    private static void ApplyRequest(GameAction action)
    {
        int fromPlayer = action.PlayerID;
        Unpack(action.DataInt2, out int slotIndex, out int op, out int wantType);

        bool amHost = FFSNetwork.IsOnline && !FFSNetwork.IsClient;
        int haveType = (int)_liveType;
        // Captured into locals ONCE and read from there: the judgement below asks several questions
        // of the same two objects, and a static field re-read after an intervening call is what
        // makes the compiler's flow analysis give up on it.
        UIDistributeReward? reward = _liveReward;
        UIDistributePointsPopup? popup = _livePopup;
        System.Collections.Generic.List<UIDistributePointsSlot>? slotList =
            popup != null ? popup.Slots : null;
        int slots = slotList != null ? slotList.Count : 0;
        string refusal;
        UIDistributePointsSlot? target = null;

        if (!amHost)
        {
            // Unreachable over the host-only channel, and refused rather than asserted anyway:
            // a client that somehow saw this must never write.
            refusal = "this machine is not the host, and only the host may hand the press to the "
                    + "game";
        }
        else if (reward == null || popup == null || slotList == null)
        {
            // The host has already finished this distribution — a press that was in flight while
            // it did. Refusing is the whole point: the far side is being advanced by the host's
            // own broadcast anyway.
            refusal = "the host has no assignment window open any more (the distribution finished "
                    + "or moved to the next process)";
        }
        else if (wantType != haveType)
        {
            // THE STALENESS TERM. The five processes run strictly one after another, so a request
            // naming a process the host has left is answering a window that no longer stands.
            refusal = $"the request answers process type {wantType} and the host is now on "
                    + $"{haveType} ({_liveType}) — the distribution has moved on";
        }
        else if (op == OpConfirm)
        {
            if (!_confirmReady)
                // THE GAME'S OWN TERM, captured where the game computes it: SetInteractable is
                // called with service.AvailablePoints == 0. Not re-derived here.
                refusal = "the host's CONFIRM is not armed — the game's own "
                        + "UIDistributeReward.SetInteractable(service.AvailablePoints == 0) last "
                        + "said false, i.e. there are still points left to assign";
            else
                refusal = string.Empty;
        }
        else if (slotIndex < 0 || slotIndex >= slots)
        {
            refusal = $"slot index {slotIndex} is outside the host's popup, which has {slots} "
                    + "slot(s)";
        }
        else
        {
            target = slotList[slotIndex];
            refusal = target == null ? $"the host's slot {slotIndex} is null" : string.Empty;
        }

        bool pressed = false;
        if (refusal.Length == 0)
        {
            // THE PRESS ITSELF, and the ONLY thing this file does to the game. Both entry points
            // are the game's own PUBLIC methods and are the very ones the host's own button is
            // wired to.
            //
            // isProxyCall: FALSE ON PURPOSE. It is what makes the game's own
            // addPointButton.interactable — which RefreshAssignedPoints has just rewritten from
            // service.CanAddPointsTo(actor) — the validator for this request. A request the RULES
            // refuse is therefore refused by the RULES, here, with no second copy of the predicate
            // anywhere in this file. It is also the term that settles a race: ProcessSideAction is
            // synchronous, so a second request is judged against the state the first one left.
            if (op == OpConfirm)
            {
                reward!.OnConfirmClick();
                pressed = true;
            }
            else if (op == OpAdd)
            {
                target!.AddPoint(isProxyCall: false);
                pressed = true;
            }
            else if (op == OpRemove)
            {
                target!.RemovePoint(isProxyCall: false);
                pressed = true;
            }
            else
            {
                refusal = $"unknown operation {op}";
            }
        }

        // HW-VERIFY: the host half of a remote press. WHO pressed, WHICH control, whether the host
        // DISPATCHED or REFUSED it and by which term, and what the other side is expected to do
        // next. A stuck-assignment report is decided here.
        VRLog.Note(Scope, $"ASSIGNMENT REQUEST: HOST received a CLIENT (player {fromPlayer}) "
                        + $"request to {OpName(op)} on slot {slotIndex} of process type "
                        + $"{wantType} (packed 0x{action.DataInt2:X}). HOST STATE: live process "
                        + $"{_liveType} ({haveType}) with {slots} slot(s), confirm armed="
                        + $"{_confirmReady}. "
                        + (pressed
                            ? "DISPATCHED — the host pressed its OWN copy of that control "
                              + (op == OpConfirm
                                  ? "(UIDistributeReward.OnConfirmClick)"
                                  : "(UIDistributePointsSlot.AddPoint/RemovePoint with "
                                    + "isProxyCall:false, so the game's own interactable — i.e. "
                                    + "service.CanAddPointsTo — had the last word)")
                              + ", so the game's unmodified code ran here and sent the real "
                              + "DistributeUIAddPoint/DistributeUIRemovePoint/DistributeUIConfirm "
                              + "to every client. WHAT THE OTHER SIDE IS EXPECTED TO DO: replay it "
                              + "through UIDistributeRewardManager.ProxyAddPoints/ProxyRemovePoints/"
                              + "ProxyConfirmClick, exactly as it does for a host press. NOTHING "
                              + "WAS WRITTEN BY THE MOD — the press is the game's. NOTE: a press "
                              + "the game's own interactable refused looks identical to this line, "
                              + "because the refusal is inside UIDistributePointsSlot.AddPoint and "
                              + "not visible from here; the far side not moving with THIS line "
                              + "present means exactly that, and is correct behaviour."
                            : $"REFUSED, by term: {refusal}. NOTHING was pressed and nothing went "
                              + "on the wire from here. WHAT THE OTHER SIDE IS EXPECTED TO DO: "
                              + "nothing — its control was never consumed, so the player may "
                              + "simply press again.")
                        + $" Presses seen on this machine this session: {_pressesSeen}.");
    }

    // ---------------------------------------------------------------------------------------
    // Re-applying the game's own answer without the host term
    // ---------------------------------------------------------------------------------------

    // The three buttons are private [SerializeField]s on the game's own components, so reaching
    // them is one cached FieldInfo each. They are written and nothing else is: an ExtendedButton's
    // `interactable` and its fade are PRESENTATION, never rules state, and the value written is the
    // one the game itself computed one statement earlier.
    private static FieldInfo? _addButtonField;
    private static FieldInfo? _removeButtonField;
    private static FieldInfo? _confirmButtonField;
    private static bool _fieldsResolved;

    private static void ResolveFields()
    {
        if (_fieldsResolved)
            return;
        _fieldsResolved = true;
        _addButtonField = AccessTools.Field(typeof(UIDistributePointsSlot), "addPointButton");
        _removeButtonField = AccessTools.Field(typeof(UIDistributePointsSlot), "removePointButton");
        _confirmButtonField = AccessTools.Field(typeof(UIDistributeReward), "confirmButton");
        if (_addButtonField == null || _removeButtonField == null || _confirmButtonField == null)
            // HW-VERIFY: the un-grey could not be installed on this game build, so the feature is
            // ABSENT rather than half-present — the flat game's host-only behaviour stands, which
            // is always a correct fallback. Once per process.
            VRLog.Note(Scope, "ASSIGNMENT CONTROL UNLOCK: NOT INSTALLED — one of the game's own "
                            + "button fields did not resolve on this build (addPointButton="
                            + $"{_addButtonField != null}, removePointButton="
                            + $"{_removeButtonField != null}, confirmButton="
                            + $"{_confirmButtonField != null}). No client's assignment control is "
                            + "given back and no request can be raised from one; the host "
                            + "completes assignments for the table exactly as in the unmodded "
                            + "game. This is a game-build mismatch, not a session fault.");
    }

    /// <summary>Re-apply the game's own <c>enable</c> to a +/- button after its host gate refused
    /// it. Called from the postfix on the very method that computed <c>enable</c>, so the value is
    /// <c>service.CanAddPointsTo(actor)</c> / <c>CanRemovePointsFrom(actor)</c> and nothing
    /// else.</summary>
    internal static void ReapplySlotButton(UIDistributePointsSlot? slot, bool enable, bool add)
    {
        if (slot == null || !IsRewardSlot(slot, out _) || !MayOperateHere())
            return;
        ResolveFields();
        FieldInfo? field = add ? _addButtonField : _removeButtonField;
        if (field?.GetValue(slot) is not ExtendedButton button)
            return;
        button.interactable = enable;
        button.ToggleFade(!enable);
    }

    /// <summary>Re-apply the game's own <c>interactable</c> to the CONFIRM button after its host
    /// gate refused it, and record that same value for the host-side judgement.</summary>
    internal static void ReapplyConfirmButton(UIDistributeReward? reward, bool interactable)
    {
        if (!IsLiveReward(reward))
            return;
        NoteConfirmReady(interactable);
        if (!MayOperateHere())
            return;
        ResolveFields();
        if (_confirmButtonField?.GetValue(reward) is not ExtendedButton button)
            return;
        // The game writes SetActive in gamepad mode and interactable otherwise. VR is never gamepad
        // mode, but the host might be, so both halves are restored rather than assumed.
        if (!button.gameObject.activeSelf && interactable)
            button.gameObject.SetActive(true);
        button.interactable = interactable;
    }
}

// ---------------------------------------------------------------------------------------------
// The patches
// ---------------------------------------------------------------------------------------------

/// <summary>
/// WHICH assignment is being distributed right now. A postfix on the public
/// <c>UIDistributeReward.Distribute(service, type, …)</c>
/// (decompiled/GH.Runtime/UIDistributeReward.cs:47), the one call every one of the five processes
/// makes to raise its popup. Records the reward UI, its popup (<c>PopUp</c>, public) and the
/// process type, so no patch below has to scan for them or reach through a private field.
/// </summary>
[HarmonyPatch(typeof(UIDistributeReward), nameof(UIDistributeReward.Distribute))]
internal static class UIDistributeReward_Distribute_Patch
{
    [HarmonyPostfix]
    private static void Postfix(UIDistributeReward __instance,
                                DistributeRewardProcess.EDistributeRewardProcessType type) =>
        DispatchGuard.Run("UIDistributeReward.Distribute(AssignmentChoice)",
            () => AssignmentChoice.NoteLive(__instance, type));
}

/// <summary>
/// The distribution ended. A postfix on <c>UIDistributePointsPopup.Hide</c>
/// (decompiled/GH.Runtime/UIDistributePointsPopup.cs:127), which
/// <c>UIDistributeReward.Distribute</c>'s resolved promise calls at :80. A POSTFIX and not a
/// prefix deliberately: <c>WorldUI/Surfaces/SurfaceCloseEdge</c> already holds a prefix on this
/// method, and a second prefix would be an ordering hazard — a prefix returning false suppresses
/// the ones after it. Postfixes do not have that property.
/// </summary>
[HarmonyPatch(typeof(UIDistributePointsPopup), nameof(UIDistributePointsPopup.Hide))]
internal static class UIDistributePointsPopup_Hide_AssignmentPatch
{
    [HarmonyPostfix]
    private static void Postfix(UIDistributePointsPopup __instance) =>
        DispatchGuard.Run("UIDistributePointsPopup.Hide(AssignmentChoice)",
            () => AssignmentChoice.NoteHidden(__instance));
}

/// <summary>
/// Give a CLIENT its "+" button back. A postfix on
/// <c>UIDistributePointsSlot.EnableAddPoints(bool enable)</c>
/// (decompiled/GH.Runtime/UIDistributePointsSlot.cs:247), whose <c>else</c> branch writes
/// <c>addPointButton.interactable = false</c> for every non-host once <c>m_IsRewards</c> is set.
/// The postfix re-applies the SAME <c>enable</c> the game computed — which is
/// <c>service.CanAddPointsTo(actor)</c>, passed in by <c>RefreshAssignedPoints</c> — so only the
/// host term is dropped and a point the rules refuse stays refused. Scoped to the live reward
/// popup's own slots, so the two SCENARIO popups are untouched.
/// </summary>
[HarmonyPatch(typeof(UIDistributePointsSlot), nameof(UIDistributePointsSlot.EnableAddPoints))]
internal static class UIDistributePointsSlot_EnableAddPoints_Patch
{
    [HarmonyPostfix]
    private static void Postfix(UIDistributePointsSlot __instance, bool enable) =>
        DispatchGuard.Run("UIDistributePointsSlot.EnableAddPoints(AssignmentChoice)",
            () => AssignmentChoice.ReapplySlotButton(__instance, enable, add: true));
}

/// <summary>The "−" button's twin of
/// <see cref="UIDistributePointsSlot_EnableAddPoints_Patch"/>; the game's value here is
/// <c>service.CanRemovePointsFrom(actor)</c> (UIDistributePointsSlot.cs:260).</summary>
[HarmonyPatch(typeof(UIDistributePointsSlot), nameof(UIDistributePointsSlot.EnableRemovePoints))]
internal static class UIDistributePointsSlot_EnableRemovePoints_Patch
{
    [HarmonyPostfix]
    private static void Postfix(UIDistributePointsSlot __instance, bool enable) =>
        DispatchGuard.Run("UIDistributePointsSlot.EnableRemovePoints(AssignmentChoice)",
            () => AssignmentChoice.ReapplySlotButton(__instance, enable, add: false));
}

/// <summary>
/// Give a CLIENT its CONFIRM button back, and capture the game's own "all points assigned" answer
/// for the host-side judgement. A postfix on the private
/// <c>UIDistributeReward.SetButtonInteractable(bool interactable)</c>
/// (decompiled/GH.Runtime/UIDistributeReward.cs:108), whose body is
/// <c>interactable &amp;&amp; (!FFSNetwork.IsOnline || FFSNetwork.IsHost)</c>. The argument is
/// <c>service.AvailablePoints == 0</c>, handed down from <c>SetInteractable</c> at :52/:59.
/// </summary>
[HarmonyPatch(typeof(UIDistributeReward), "SetButtonInteractable")]
internal static class UIDistributeReward_SetButtonInteractable_Patch
{
    [HarmonyPostfix]
    private static void Postfix(UIDistributeReward __instance, bool interactable) =>
        DispatchGuard.Run("UIDistributeReward.SetButtonInteractable(AssignmentChoice)",
            () => AssignmentChoice.ReapplyConfirmButton(__instance, interactable));
}

/// <summary>
/// A "+" press. A prefix on the public <c>UIDistributePointsSlot.AddPoint(bool isProxyCall)</c>
/// (decompiled/GH.Runtime/UIDistributePointsSlot.cs:164), the method the slot's own
/// <c>addPointButton</c> is wired to through <c>AddPointDelegate</c> (:151).
///
/// <para>Reached by a local HAND and by nothing else: the game's replay path
/// (<c>DistributeRewardProcess.ProxyAddPoint</c>) calls <c>m_Service.AddPoint(actor)</c> directly
/// and never comes through here (DistributeItemsProcess.cs:240-247 and the four siblings). The
/// <c>isProxyCall</c> parameter is still honoured, so a future replay that did use it stays
/// correct.</para>
/// </summary>
[HarmonyPatch(typeof(UIDistributePointsSlot), nameof(UIDistributePointsSlot.AddPoint))]
internal static class UIDistributePointsSlot_AddPoint_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(UIDistributePointsSlot __instance, bool isProxyCall)
    {
        if (isProxyCall || !AssignmentChoice.IsRewardSlot(__instance, out int index))
            return true;    // a replay, or a control outside this family: the game decides
        // Read OUTSIDE the guard, because the guard's fallback differs by side. On the HOST a throw
        // must fall back to vanilla (true) so the host's own press still works; on a CLIENT it must
        // NOT (false), because vanilla on a client mutates its own service and tells nobody — and
        // service.Apply() then PERSISTS that divergence.
        bool amClient = FFSNetwork.IsOnline && FFSNetwork.IsClient;
        return DispatchGuard.Run("UIDistributePointsSlot.AddPoint(AssignmentChoice)",
            () => AssignmentChoice.OnLocalPress(index, AssignmentChoice.OpAdd, $"slot {index}"),
            onThrow: !amClient);
    }
}

/// <summary>The "−" press; the twin of <see cref="UIDistributePointsSlot_AddPoint_Patch"/> on
/// <c>UIDistributePointsSlot.RemovePoint(bool)</c> (:156).</summary>
[HarmonyPatch(typeof(UIDistributePointsSlot), nameof(UIDistributePointsSlot.RemovePoint))]
internal static class UIDistributePointsSlot_RemovePoint_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(UIDistributePointsSlot __instance, bool isProxyCall)
    {
        if (isProxyCall || !AssignmentChoice.IsRewardSlot(__instance, out int index))
            return true;
        bool amClient = FFSNetwork.IsOnline && FFSNetwork.IsClient;
        return DispatchGuard.Run("UIDistributePointsSlot.RemovePoint(AssignmentChoice)",
            () => AssignmentChoice.OnLocalPress(index, AssignmentChoice.OpRemove, $"slot {index}"),
            onThrow: !amClient);
    }
}

/// <summary>
/// The CONFIRM press that ends the assignment. A prefix on the public
/// <c>UIDistributeReward.OnConfirmClick()</c> (decompiled/GH.Runtime/UIDistributeReward.cs:139),
/// which resolves the distribution promise and then sends the real
/// <c>GameActionType.DistributeUIConfirm</c> <c>if (FFSNetwork.IsHost)</c>. Same rule as a point
/// press: the client asks, the host presses, the game's own record closes the window everywhere.
/// </summary>
[HarmonyPatch(typeof(UIDistributeReward), nameof(UIDistributeReward.OnConfirmClick))]
internal static class UIDistributeReward_OnConfirmClick_Patch
{
    [HarmonyPrefix]
    private static bool Prefix(UIDistributeReward __instance)
    {
        if (!AssignmentChoice.IsLiveReward(__instance))
            return true;
        bool amClient = FFSNetwork.IsOnline && FFSNetwork.IsClient;
        return DispatchGuard.Run("UIDistributeReward.OnConfirmClick(AssignmentChoice)",
            () => AssignmentChoice.OnLocalPress(0, AssignmentChoice.OpConfirm, "the CONFIRM button"),
            onThrow: !amClient);
    }
}
