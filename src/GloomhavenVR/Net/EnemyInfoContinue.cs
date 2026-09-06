using FFSNet;
using GloomhavenVR.Board.Patches;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

/// <summary>
/// ANY PLAYER MAY END THE ENEMY-INFORMATION REVEAL — and the HOST is still the only writer.
///
/// <para>USER REQUEST (2026-09-05, verbatim, mid-round): <i>"Eine neue Sache: Ich mag diese
/// Abhängigkeit vom Host nicht. Bei der Begegnung haben wir das schon geändert, ich möchte es auch
/// bei der Bestätigung die notwendig ist damit es weitergeht nachdem die Gegeninfo präsentiert
/// wurde ändern. Der Fortfahren knopf soll auf allen Controllboards erscheinen - klickt einer
/// darauf soll es weitergehen, egal wer. Eventuell machst du hier die selbe Strategie das die
/// anderen den buttonpress an den host senden und der host übergibt es dem Spiel wie du es bereits
/// implementiert hattest"</i></para>
///
/// <para><b>THE PHASE.</b> <c>CPhase.PhaseType.MonsterClassesSelectAbilityCards</c> on the rules
/// side, <c>ActionPhaseType.EnemyCardReveal</c> on the wire — the monster ability cards revealed
/// inline on the initiative track, ended by the shared <c>ReadyButton</c> standing in
/// <c>EREADYBUTTONCONTINUE</c> with the <c>GUI_CONTINUE</c> wording ("Fortfahren"). See
/// <see cref="EnemyInfoPhaseSkip"/>, which already documents that phase in full and whose
/// <c>Clickable</c> predicate this file REUSES rather than restating.</para>
///
/// <para><b>WHAT THE GAME DOES TODAY, AND IT IS THE DEFECT THE USER IS REPORTING.</b> A non-host
/// client is locked out of that button three times over: the Choreographer arms it with
/// <c>interactable: !FFSNetwork.IsClient</c> (Choreographer.cs:3717),
/// <c>ReadyButton.CheckButtonInteractability</c> re-forces <c>readyButton.interactable = false</c>
/// for the whole phase (ReadyButton.cs:500-501), and <c>OnClickInternal</c> early-returns on the
/// same test even if a click got through (ReadyButton.cs:255-258). The mod's board keycap follows
/// that honestly — <c>CardsGameApi.CanConfirm</c> reads <c>ReadyButton.IsInteractable</c>, so
/// <c>show = canConfirm || confirmed</c> in <c>PlayTray.TickStatus</c> is false on every peer and
/// the cap is not drawn at all. The 447 hardware logs say exactly this, on both machines and four
/// times each: the host prints <c>[EnemyInfo] ENEMY-INFO PHASE: NOT skipped — 3 enemy row(s)…</c>
/// while the peer prints <c>…this client is a PEER, so it decides NOTHING here and simply
/// follows.</c></para>
///
/// <para><b>THE SHAPE IS THE ENCOUNTER'S, DELIBERATELY — see <see cref="EncounterChoice"/>.</b> A
/// client's press advances NOTHING locally. It travels to the host as ONE side action; the host
/// validates it against its OWN live button and then presses that button through
/// <c>ReadyButton.OnClickInternal(networkActionIfOnline: true)</c>, the seam every press path
/// converges on. From there not one line of this file is involved: the game's unmodified body
/// sends the real <c>Synchronizer.SendGameAction(GameActionType.ConfirmAction,
/// ActionProcessor.CurrentPhase)</c> to every client exactly as a human host press would, each
/// client replays it through <c>Choreographer.ProxyConfirmAction</c> →
/// <c>OnClickInternal(networkActionIfOnline: false)</c>, and the host's queued
/// <c>FinishAbilityCardsAnimation</c> / <c>ScenarioRuleClient.StepComplete()</c> runs on the host
/// alone. <b>No game state is written by the mod, and the host remains the only machine that
/// decides.</b></para>
///
/// <para><b>WHY A SIDE ACTION AND NOT A QUEUED GAME ACTION</b> — the same measured reason the
/// encounter has, one screen over. <c>Choreographer</c> puts the HOST into
/// <c>ActionProcessor.SetState(ActionProcessorStateType.Halted, ActionPhaseType.EnemyCardReveal)</c>
/// for the whole reveal (Choreographer.cs:3735), and a <c>GameActionEvent</c> arriving at a Halted
/// host is ENQUEUED and never processed (<c>readyToProcessNextAction = false</c>,
/// ActionProcessor.cs:150-152). It would then fail <c>TargetPhaseID != currentState.PhaseType</c>
/// once the phase moved on — the "incorrect action detected" countdown that ends in a throw and the
/// player's "Desynchronization occurred" dialog. <c>ActionProcessor.ProcessSideAction</c> calls
/// <c>action.Execute()</c> DIRECTLY (ActionProcessor.cs:175-199): no queue, no phase test, no
/// <c>readyToProcessNextAction</c>.</para>
///
/// <para><b>WHY IT RIDES THE MOD'S OWN SENTINEL AND NOT A REAL <c>GameActionType</c>, which is the
/// one place this file improves on the encounter's version.</b> <see cref="EncounterChoice"/> had
/// to borrow <c>GameActionType.ContinueRoadEvent</c> because it needed the vanilla dispatch to
/// deliver its request somewhere patchable; the price is a whole paragraph explaining why an
/// UNMODDED host must never be sent one (it would read the unset <c>SupplementaryDataIDMed</c> as
/// option 0 and press the wrong thing, or throw into <c>FFSNetwork.HandleDesync</c>). This request
/// needs no such paragraph, because the mod ALREADY OWNS a side-action channel with a stronger
/// safety property: <see cref="NetProtocol.SentinelActionTypeId"/> carried with
/// <see cref="NetProtocol.SentinelTargetPlayerId"/> in <c>TargetPlayerID</c>. Vanilla
/// <c>ProcessSideAction</c> only reaches <c>Execute()</c> when <c>TargetPlayerID == 0 ||
/// == MyPlayer.PlayerID</c> (ActionProcessor.cs:188), and no real player owns
/// <c>int.MaxValue</c> — so a flat or unmodded host hits the "Ignoring SideAction" branch and
/// executes NOTHING. There is no failure mode to gate against here, only one to prefer.</para>
///
/// <para><b>AND IT ADDS NO PATCH AND NO WIRE FIELD.</b> The receive seam is the mod's EXISTING
/// Harmony prefix on <c>FFSNet.ActionProcessor.ProcessSideAction</c>
/// (<see cref="FfsNetTransport"/>), which already sees every inbound side action and already
/// consumes the sentinel type; this file adds one branch to it rather than a second prefix on the
/// same method (two prefixes on one method are order-dependent, because a prefix returning FALSE
/// suppresses the ones after it). The payload rides <c>NetworkAction.DataInt</c> /
/// <c>DataInt2</c> / <c>DataBoolean</c> — two ints and a bool the GAME's own token already
/// serialises unconditionally (NetworkAction.cs:44-53). <b>The GVR1 packet is untouched: zero bytes
/// added to the mod's own wire budget.</b></para>
///
/// <para><b>TWO PLAYERS PRESSING AT ONCE ADVANCE THE GAME EXACTLY ONCE, and the term that
/// guarantees it is the GAME'S, not a flag of ours.</b> The reveal's button is armed
/// <c>glowingEffect: true, hideOnClick: true</c> (Choreographer.cs:3716), so
/// <c>OnClickInternal</c> takes its <c>showEffects</c> branch and sets
/// <c>ButtonComponent.enabled = false</c> SYNCHRONOUSLY, in the same call, before it returns
/// (ReadyButton.cs:336-346) — the real advance (<c>StepComplete</c>) is deferred into
/// <c>HideDelayed</c> behind the button FX. <c>ProcessSideAction</c> is synchronous, so the host
/// applies request A completely before request B is dispatched, and B then fails the
/// <c>component.enabled</c> term inside <c>EnemyInfoPhaseSkip.Clickable</c>. A LATE duplicate fails
/// two further, independent terms: the round stamp it carries, and <c>PhaseManager.PhaseType</c>
/// having left <c>MonsterClassesSelectAbilityCards</c>. Nothing here dequeues, latches or clears
/// anything, so there is no bookkeeping of ours that can disagree with the picture — and every
/// refusal is logged with the term that made it, because two people pressing together is the
/// NORMAL case, not an error.</para>
///
/// <para><b>A LOST OR REFUSED REQUEST IS NEVER A DEAD END.</b> The send is
/// <c>canBeUnreliable: false</c>, i.e. Bolt <c>ReliableOrdered</c>. The client's press is not
/// consumed either way: nothing local changes, the cap stays up and pressable for as long as the
/// reveal stands, and pressing again re-runs the whole path. The host's own press is untouched in
/// every case, so the worst outcome of this entire file failing is the flat game's host-only
/// behaviour — which is what shipped in 447.</para>
///
/// <para><b>THE 1:1 MIRROR NEEDS NOTHING NEW.</b> The cap this file arms is the board's EXISTING
/// CONFIRM keycap, shown through <c>PlayTray.TickStatus</c>'s own <c>show</c> test and published to
/// peers by the records that already carry it — <c>PlayTray.ConfirmControlShown</c> →
/// <c>NetProtocol.BoardUiConfirmBit</c>, and <c>PlayTray.ConfirmControlLabel</c> →
/// <c>ExtIdCapLabels</c> bit 0, both sampled off the very flag the local renderer obeys. So a peer
/// sees the owner's "Fortfahren" cap appear at the same instant, in the same place, with the same
/// wording, and no drawing path, record or field was added to make that true.</para>
/// </summary>
internal static class EnemyInfoContinue
{
    private const string Scope = "Net";

    /// <summary>Unconditional liveness counter for the HW-VERIFY lines: how many presses of the
    /// reveal's Continue this process has seen, on either seat, dispatched or refused. A report
    /// whose press line never appears at all is a different defect from one where it appears and
    /// says REFUSED, and this number is what separates them.</summary>
    private static int _pressesSeen;

    /// <summary>
    /// WHY THE CAP IS OR IS NOT OFFERED, as a small closed set — and the change-gate is on THIS
    /// rather than on the bare armed/not-armed bool.
    ///
    /// <para><b>THE BOOL WENT SILENT IN THE ONE SESSION THIS FEATURE HAD TO EXPLAIN ITSELF IN.</b>
    /// Against a FLAT host the predicate answers false outside the reveal (<see cref="NotAClient"/>)
    /// and false inside it (<see cref="HostNotModded"/>), so the bool never transitions, so nothing
    /// printed — for the whole session. "The gate refused, correctly" and "the gate never ran" were
    /// the same reading. Capping per VERDICT CLASS instead of per verdict is this project's own
    /// standing answer to that, and the set is closed and tiny, so the cadence is still at most a
    /// couple of lines per reveal and never one per frame: the free-text
    /// <see cref="ArmedContinueButton"/> reasons all fold into <see cref="ControlNotStanding"/> and
    /// cannot chatter.</para>
    /// </summary>
    private enum ArmClass
    {
        /// <summary>Not an online client inside the enemy-information reveal — the ordinary state.</summary>
        NotAClient,

        /// <summary>The user chose to join as a flat player, so every mod net path is off.</summary>
        FlatNetMode,

        /// <summary>The host has never sent a GVR1 packet, i.e. it is running the unmodded game.</summary>
        HostNotModded,

        /// <summary>The reveal's shared Continue control is not standing on this machine.</summary>
        ControlNotStanding,

        /// <summary>The cap is offered and a press will travel to the host.</summary>
        Armed,
    }

    /// <summary>Change-gate for the ARMED/DISARMED line. The availability predicate is read once
    /// per frame by <c>PlayTray.TickStatus</c>, so the line may only print on a TRANSITION. Paired
    /// with <see cref="VersionGuard.SessionEpoch"/> so a second session in the same process
    /// re-states its own verdict instead of inheriting the first one's silence.</summary>
    private static ArmClass _armClass;
    private static int _armEpoch = -1;

    // ---------------------------------------------------------------------------- the predicate --

    /// <summary>The enemy-information reveal, by the game's own phase enum. One property read.</summary>
    internal static bool InEnemyInfoPhase() =>
        PhaseManager.PhaseType == CPhase.PhaseType.MonsterClassesSelectAbilityCards;

    /// <summary>
    /// The reveal's Continue button on THIS machine, or null if the control the user is asking for
    /// is not standing here right now — with the term that refused it.
    ///
    /// <para>Every term is <c>EnemyInfoPhaseSkip.Clickable</c>'s: the predicate the empty-phase skip
    /// already uses to decide the host may press, which is itself <c>ReadyButton.OnClick</c>'s own
    /// guard (ReadyButton.cs:188-202) plus the one <c>OnClickInternal</c> checks first (:246-249).
    /// Deliberately NOT <c>CardsGameApi.CanConfirm</c>: that one is a faithful mirror of the game's
    /// press guard, which is exactly why it cannot be asked here — it reads
    /// <c>ReadyButton.IsInteractable</c>, and the game forces that false on a client for this
    /// phase, so it would answer "is the host allowed" when the question is "is the control up".</para>
    ///
    /// <para><paramref name="requireInteractable"/> carries that distinction: FALSE on the CLIENT,
    /// which is asking whether a reveal is standing in front of it, TRUE on the HOST, which is
    /// about to press. See <c>EnemyInfoPhaseSkip.Clickable</c> for the argument in full.</para>
    /// </summary>
    private static ReadyButton? ArmedContinueButton(bool requireInteractable, out string why)
    {
        why = "";
        Choreographer? choreographer = Choreographer.s_Choreographer;
        ReadyButton? button = choreographer != null ? choreographer.readyButton : null;
        if (button == null)
        {
            why = "the Choreographer has no ReadyButton on this machine";
            return null;
        }
        if (!button.gameObject.activeInHierarchy)
        {
            why = "the ReadyButton GameObject is not active in the hierarchy — the game arms it "
                + "with Toggle(!InputManager.GamePadInUse, …), so a gamepad seat has no shared "
                + "Continue control at all";
            return null;
        }
        return EnemyInfoPhaseSkip.Clickable(button, out why, requireInteractable) ? button : null;
    }

    /// <summary>
    /// Would a press on THIS machine have to travel to the host? True only on an online CLIENT,
    /// inside the reveal, with the Continue control actually standing, and with a host that will
    /// honour the request. It is BOTH the cap's extra show term and the press router's, on purpose:
    /// one predicate, so the control the player sees and the path the press takes cannot disagree.
    ///
    /// <para>The host and offline are not this method's business and it answers false for them —
    /// there the game's own <c>CardsGameApi.CanConfirm</c> is already true and the cap already
    /// shows, which is the ModBuild 85 behaviour the user asked to keep.</para>
    ///
    /// <para>The last term is <see cref="EncounterChoice.HostCanHonourRequests"/> — the SAME gate
    /// the encounter unlock uses, shared rather than restated. It is
    /// <c>!NetSession.FlatNetMode &amp;&amp; VersionGuard.IsModdedPeer(PlayerRegistry.HostPlayerID)</c>,
    /// i.e. "the host has been seen sending GVR1 packets". That is also the exact condition under
    /// which the host's <see cref="FfsNetTransport"/> receive prefix — the seam that handles this
    /// request — is installed and running, so the fact that decides whether to SHOW the cap is the
    /// fact that decides whether pressing it can work. A control the player can press that could
    /// never do anything is the dead end this project does not ship.</para>
    /// </summary>
    internal static bool LocalPressIsRequest() =>
        // ISOLATED BECAUSE OF ITS PER-FRAME CALLER. PlayTray.TickStatus runs this every frame from
        // a Unity Update, and this project's standing lesson there is blunt: an unguarded Update
        // that throws starves VR input for the rest of the session. onThrow: false is the safe
        // half — a frame that cannot decide offers no cap and routes no press, which is the flat
        // game's behaviour, and DispatchGuard names the seam once instead of per frame.
        Desync.DispatchGuard.Run("EnemyInfoContinue.LocalPressIsRequest", CoreDelegate,
                                 onThrow: false);

    /// <summary>Cached so the per-frame guard above converts no method group and allocates no
    /// delegate — a lambda or method group per frame is a steady-state allocation, which is the
    /// rule <c>FocusDriver</c>'s cached delegates and <c>InitiativeTrack_Update_TickSkip</c>'s
    /// no-closure postfix already follow.</summary>
    private static readonly System.Func<bool> CoreDelegate = LocalPressIsRequestCore;

    private static bool LocalPressIsRequestCore()
    {
        if (!FFSNetwork.IsOnline || !FFSNetwork.IsClient || !InEnemyInfoPhase())
            return NoteArmState(ArmClass.NotAClient, "this seat is not an online client inside the "
                                                   + "enemy-information reveal");
        if (!EncounterChoice.HostCanHonourRequests())
            return NetSession.FlatNetMode
                ? NoteArmState(ArmClass.FlatNetMode,
                    "NetSession.FlatNetMode — this player chose to join as a flat player, so every "
                    + "mod net path is off")
                // A constant string, not an interpolation: this branch can hold for the whole
                // length of a reveal (an unmodded host), and it is read once per frame.
                : NoteArmState(ArmClass.HostNotModded,
                    "the HOST is not a modded peer (VersionGuard.IsModdedPeer("
                    + "PlayerRegistry.HostPlayerID) is false) — it is running the unmodded game, so "
                    + "nothing on the far side would honour a request and the cap must not be "
                    + "offered. THIS IS THE FLAT-HOST CASE AND IT IS NOT A STALL: the host's own "
                    + "Continue works exactly as vanilla and this machine follows its "
                    + "ConfirmAction, which is what the unmodded game does for every client");
        // requireInteractable: false — see ArmedContinueButton. The client's own button is dead by
        // the game's design and this feature exists BECAUSE of that; what is asked here is whether
        // the reveal is standing in front of this player.
        ReadyButton? button = ArmedContinueButton(requireInteractable: false, out string why);
        return button != null
            ? NoteArmState(ArmClass.Armed, "")
            : NoteArmState(ArmClass.ControlNotStanding, why);
    }

    /// <summary>
    /// The change-gated half of <see cref="LocalPressIsRequest"/>. Returns whether the cap is
    /// armed, so the caller reads as one expression.
    /// </summary>
    private static bool NoteArmState(ArmClass cls, string why)
    {
        bool armed = cls == ArmClass.Armed;
        int epoch = VersionGuard.SessionEpoch;
        if (_armEpoch == epoch && _armClass == cls)
            return armed;
        bool first = _armEpoch != epoch;
        _armEpoch = epoch;
        _armClass = cls;
        // The very first evaluation of a session is the ORDINARY state — no session, or a seat
        // outside the reveal — and says nothing. Every other class does print, including the two
        // that used to be swallowed because the bool they folded into had not moved: a flat host
        // and flat-net mode both hold "not armed" from before the reveal to after it.
        if (first && cls == ArmClass.NotAClient)
            return armed;
        // HW-VERIFY: does the Continue cap exist on THIS client's control board at all? Grep both
        // logs for ENEMY-INFO CONTINUE ARMED — the peer must print it once per reveal. THE
        // FALSIFIER: its absence while the same machine's [EnemyInfo] ENEMY-INFO PHASE line IS
        // present means the feature never armed there, and the DISARMED line's term says which
        // clause refused. In a MODDED-ONLY session the classes seen are NotAClient → Armed →
        // NotAClient per reveal; a DISARMED line naming the HOST term in such a session means the
        // handshake is not arriving, which is a bug and not a flat player. Change-gated on the
        // VERDICT CLASS, so this is at most a couple of lines per reveal and never one per frame.
        VRLog.Note(Scope, armed
            ? $"ENEMY-INFO CONTINUE ARMED: this CLIENT (player {NetPlayerActors.LocalPlayerId()}) "
              + "is inside the enemy-information reveal "
              + "(CPhase.PhaseType.MonsterClassesSelectAbilityCards), the game's shared Continue "
              + "control is standing here, and the host is a modded peer that will honour a "
              + "request — so the 'Fortfahren' keycap is OFFERED ON THIS BOARD and its press will "
              + "travel to the host. Vanilla would have hidden it: ReadyButton.interactable is "
              + "forced false for the whole of this phase on a client (ReadyButton.cs:500-501). "
              + "NOTHING has been written to the game by arming it."
            : "ENEMY-INFO CONTINUE DISARMED: the 'Fortfahren' keycap is NOT offered on this "
              + $"client's board, by term: {why}. The host's own control is untouched and works "
              + "exactly as vanilla; this machine simply follows whatever the host confirms.");
        return armed;
    }

    // ------------------------------------------------------------------------------ the sending --

    /// <summary>
    /// A press on a CLIENT. Sends ONE request to the host and advances nothing locally. Returns
    /// TRUE when the request went on the wire (which is what the caller reports as "the press was
    /// taken"), FALSE when a named term stopped it — and in BOTH cases the press is not consumed,
    /// so pressing again re-runs this whole path.
    /// </summary>
    internal static bool RequestHostPress()
    {
        _pressesSeen++;
        int playerId = NetPlayerActors.LocalPlayerId();
        int round = CardsGameApi.RoundNumber();
        // Forced non-zero: the host's recogniser leans on "no stamp" (0) staying distinguishable
        // from a real one, exactly as EncounterChoice.ScreenStamp does. The round is a plain int
        // both machines already hold (Choreographer.m_CurrentState.RoundNumber) and is never a
        // translated string, so it cannot disagree across UI languages.
        int stamp = round + 1;

        bool haveMe = playerId > 0;
        bool sent = haveMe
                    && !FFSNetwork.HasDesynchronized
                    && SideActionRequest.Send((GameActionType)NetProtocol.SentinelActionTypeId,
                                              NetProtocol.SentinelTargetPlayerId,
                                              NetProtocol.SideRequestEnemyInfoContinue,
                                              stamp,
                                              dataBool: true);

        if (!sent)
        {
            // HW-VERIFY: a press this line names never reached the host, and it says which term
            // stopped it. A "nothing happened when I pressed Fortfahren" report carrying this line
            // is answered by that term alone — nothing further down ever ran.
            VRLog.Note(Scope, $"ENEMY-INFO CONTINUE PRESS #{_pressesSeen}: CLIENT (player "
                            + $"{playerId}) pressed 'Fortfahren' on the enemy-information reveal "
                            + $"of round {round} — REFUSED BEFORE SENDING, by term: "
                            + (!haveMe
                                ? "this client has no PlayerRegistry.MyPlayer yet"
                                : FFSNetwork.HasDesynchronized
                                    ? "FFSNetwork.HasDesynchronized — the session is already down"
                                    : "FFSNet.Synchronizer.SendSideAction did not resolve on this "
                                      + "game build")
                            + ". NOTHING was written locally and nothing went on the wire; the "
                            + "host is expected to do nothing, because it was never told. THE "
                            + "PRESS IS NOT CONSUMED — the cap stays up and pressing again re-runs "
                            + $"this whole path. Presses seen this session: {_pressesSeen}.");
            return false;
        }

        // HW-VERIFY: the client half of the press. WHO pressed (CLIENT + player id), WHICH reveal
        // (the round), that it was DISPATCHED, and WHAT THE OTHER SIDE SHOULD DO. Pair it with the
        // host's ENEMY-INFO CONTINUE REQUEST line carrying the same round stamp: this line present
        // with no host line means the side action did not arrive; both present with the host saying
        // REFUSED means somebody else's press won the race. Once per press, never per frame.
        VRLog.Note(Scope, $"ENEMY-INFO CONTINUE PRESS #{_pressesSeen}: CLIENT (player {playerId}) "
                        + $"pressed 'Fortfahren' on the enemy-information reveal of round {round} "
                        + $"(stamp {stamp}) — DISPATCHED to the host as a "
                        + "Synchronizer.SendSideAction(sentinel, sendToHostOnly: true, "
                        + "canBeUnreliable: false) REQUEST, which is the channel that is NOT "
                        + "blocked by the host's Halted @ EnemyCardReveal. THE LOCAL ADVANCE IS "
                        + "HELD BACK ON PURPOSE: nothing moved on this machine and nothing was "
                        + "written to the game. WHAT THE OTHER SIDE IS EXPECTED TO DO: the host "
                        + "validates the request, presses its OWN ReadyButton, and the game's "
                        + "unmodified OnClickInternal sends the real GameActionType.ConfirmAction "
                        + "to everyone — including back to this machine, which is what ends the "
                        + "reveal here. If the reveal does NOT end, read the host's ENEMY-INFO "
                        + "CONTINUE REQUEST line for the refusing term. Presses seen this "
                        + $"session: {_pressesSeen}.");
        return true;
    }

    /// <summary>
    /// A press on the seat that may press directly — the HOST, or offline. Says so and returns;
    /// the caller then runs the game's own click path unchanged. This exists so that ONE grep
    /// token answers "who actually handed the action to the game" on either seat rather than two,
    /// and it is a no-op outside the reveal.
    ///
    /// <para>ONLINE ONLY. Offline there is no question to answer — one seat presses its own button
    /// and nothing travels — and a Note line per reveal per scenario would be pure noise in single
    /// player, which is the flood ModBuild 331 removed.</para>
    /// </summary>
    internal static void NoteLocalPress()
    {
        if (!FFSNetwork.IsOnline || !InEnemyInfoPhase())
            return;
        _pressesSeen++;
        // HW-VERIFY: the host's own press of the same control. Together with the CLIENT line above
        // and the REQUEST line below, one grep for `] [Net] ENEMY-INFO CONTINUE` across both logs
        // names the seat that pressed and the seat that wrote, for every advance of every reveal.
        VRLog.Note(Scope, $"ENEMY-INFO CONTINUE PRESS #{_pressesSeen}: HOST "
                        + $"(player {NetPlayerActors.LocalPlayerId()}) pressed 'Fortfahren' on "
                        + $"the enemy-information reveal of round {CardsGameApi.RoundNumber()} — "
                        + "PRESSED LOCALLY through the game's own ReadyButton.OnClickInternal, no "
                        + "request and no mod write. This seat IS the authority, so the game's "
                        + "own SendGameAction(ConfirmAction) goes out from here to every client. "
                        + $"Presses seen this session: {_pressesSeen}.");
    }

    // ---------------------------------------------------------------------------- the judgement --

    /// <summary>
    /// A request has arrived on the mod's side-action channel. Called from
    /// <see cref="FfsNetTransport"/>'s existing <c>ProcessSideAction</c> prefix with the raw
    /// <c>object</c> that prefix was handed, so the transport keeps its reflection-only stance
    /// toward the Bolt token types. Returns TRUE when the action was OURS and has been dealt with
    /// (the caller then skips vanilla), FALSE for anything else — including the transport's own
    /// rig/extras packets, which carry a payload token and none of these markers.
    /// </summary>
    internal static bool TryHandleSideAction(object? action)
    {
        if (action is not GameAction ga)
            return false;
        // THE MARKER IS CHECKED FIRST, and it is the term that separates a request from the
        // transport's own 15 Hz rig/extras stream: FfsNetTransport pre-boxes dataBool FALSE (and
        // both data ints 0) for every cosmetic packet it will ever send, so one bool read rejects
        // them. Deliberately NOT a test on SupplementaryDataToken, which would be the obvious
        // discriminator: its type is Photon.Bolt's IProtocolToken and naming it here would give the
        // mod a compile-time dependency on bolt.dll — the exact dependency FfsNetTransport's whole
        // reflection design exists to avoid. (The compiler said so: CS0012 on this very line.)
        if (!ga.SupplementaryDataBoolean
            || ga.ActionTypeID != NetProtocol.SentinelActionTypeId
            || ga.DataInt != NetProtocol.SideRequestEnemyInfoContinue
            || ga.DataInt2 == 0)                   // the stamp, forced non-zero by the sender
            return false;
        // CONSUMED EITHER WAY. The action is ours, so vanilla must not see it; a throw inside the
        // judgement is named by DispatchGuard and costs the requesting player one press, which they
        // can simply repeat because nothing about it was consumed on their machine.
        Desync.DispatchGuard.Run("EnemyInfoContinue.ApplyRequest", () => ApplyRequest(ga));
        return true;
    }

    /// <summary>
    /// Validate a client's request against the HOST's own live reveal and, if it holds, press the
    /// host's own button. Everything after that <c>OnClickInternal()</c> is the game's code.
    /// </summary>
    private static void ApplyRequest(GameAction action)
    {
        int fromPlayer = action.PlayerID;
        int wantStamp = action.DataInt2;
        int round = CardsGameApi.RoundNumber();
        int haveStamp = round + 1;
        bool amHost = FFSNetwork.IsOnline && !FFSNetwork.IsClient;

        string refusal;
        ReadyButton? target = null;
        if (!amHost)
        {
            // Unreachable over GlobalTargets.OnlyServer, and refused rather than asserted anyway:
            // a client that somehow saw this must never write.
            refusal = "this machine is not the host, and only the host may hand the action to the "
                    + "game";
        }
        else if (!InEnemyInfoPhase())
        {
            // THE LATE-DUPLICATE TERM, independent of the button state below: the reveal this
            // request answers is over. Whoever DID win had the host broadcast their confirm to
            // everyone, so the requesting client is already moving on.
            refusal = "the host has left the enemy-information reveal (PhaseManager.PhaseType is "
                    + $"{PhaseManager.PhaseType}, not MonsterClassesSelectAbilityCards)";
        }
        else if (wantStamp != haveStamp)
        {
            refusal = $"the request answers round stamp {wantStamp} and the host is on {haveStamp} "
                    + $"(round {round}) — this press is for a reveal the host has already left";
        }
        else
        {
            // THE RACE TERM. Two players pressed; the first request was applied and the game's own
            // ReadyButton.OnClickInternal set ButtonComponent.enabled = false in that same
            // synchronous call, so this one finds the control already spent.
            // requireInteractable: TRUE here, and that asymmetry with the client's read is what
            // keeps the host the only seat that may write — this is the copy of the button that is
            // about to be pressed.
            target = ArmedContinueButton(requireInteractable: true, out string why);
            refusal = target != null ? string.Empty : why;
        }

        if (refusal.Length == 0 && target != null)
        {
            // The press itself, and the ONLY thing this file does to the game. Same seam and same
            // overload the empty-phase skip uses (EnemyInfoPhaseSkip.Tick) and the same one a human
            // host press reaches: networkActionIfOnline defaults to TRUE, so the game's own
            // Synchronizer.SendGameAction(ConfirmAction, ActionProcessor.CurrentPhase) goes out to
            // every client from here.
            target.OnClickInternal();
        }

        // HW-VERIFY: the host half. WHO asked (a CLIENT, with its player id), for WHICH reveal,
        // whether the host PRESSED or REFUSED and by which term, and what the other side is
        // expected to do next. A "the reveal did not end for anybody" report is decided here:
        // PRESSED means the game's own ConfirmAction went to every client; REFUSED names the exact
        // term, and "the control is already spent" is the normal, correct outcome of two people
        // pressing together. Once per request.
        VRLog.Note(Scope, $"ENEMY-INFO CONTINUE REQUEST #{_pressesSeen}: HOST received a CLIENT "
                        + $"(player {fromPlayer}) request to end the enemy-information reveal, "
                        + $"carrying round stamp {wantStamp}. HOST STATE: phase "
                        + $"{PhaseManager.PhaseType}, round {round} (stamp {haveStamp}). "
                        + (refusal.Length == 0
                            ? "PRESSED — the host pressed its OWN ReadyButton through the game's "
                              + "ReadyButton.OnClickInternal(networkActionIfOnline: true), so the "
                              + "game's unmodified body sent the real GameActionType.ConfirmAction "
                              + "to every client and ran the host's queued "
                              + "FinishAbilityCardsAnimation / ScenarioRuleClient.StepComplete. "
                              + "WHAT THE OTHER SIDE IS EXPECTED TO DO: leave the reveal through "
                              + "its own Choreographer.ProxyConfirmAction, exactly as it does for "
                              + "a host press. NOTHING WAS WRITTEN BY THE MOD — the click is the "
                              + "game's."
                            : $"REFUSED, by term: {refusal}. NOTHING was pressed and nothing went "
                              + "on the wire from here. WHAT THE OTHER SIDE IS EXPECTED TO DO: "
                              + "leave the reveal anyway, because whoever DID win the race had "
                              + "the host broadcast their confirm to everyone; and the refused "
                              + "player's cap is never consumed, so pressing again is always "
                              + "available while the reveal stands.")
                        + $" Presses seen on this machine this session: {_pressesSeen}.");
    }
}
