using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Per-frame owner of the VR embodiment sync: samples + broadcasts the local rig at
/// <see cref="NetProtocol.SendRateHz"/>, receives peers' packets, and maintains one
/// <see cref="RemoteAvatar"/> per remote VR player (lazy-created on first packet — RepoXR
/// pattern — and torn down on staleness). Everything is guarded so single-player, offline,
/// netcode-absent and non-modded-peer scenarios are strict no-ops.
///
/// THREADING: Bolt dispatches events on the Unity main thread, so
/// <see cref="OnPacketReceived"/> (invoked from the Harmony prefix inside
/// <c>ProcessSideAction</c>) runs on the main thread. To avoid creating GameObjects while
/// Bolt is mid-event-iteration, received states are parked in <see cref="_pending"/> and
/// applied in <see cref="Update"/>.
/// </summary>
internal sealed class NetAvatarDriver : MonoBehaviour
{
    // Fingers are cheap (10 B) and improve presence; on by default. No shared-config edit.
    private const bool IncludeFingers = true;

    // COMPILE-TIME WIRE GUARD — do not delete, this is the only mechanism that turns a silent
    // multiplayer corruption into a build failure.
    //
    // Cards.PileKind's member ORDER is a wire constant: TickExtrasSend casts it straight onto the
    // extras packet's trailing-block byte A (bits 0..1) at
    //     extras.PileBrowseKind = (byte)browseKind;
    // and every peer decodes it against NetProtocol.PileBrowseKind*. Nothing in the compiler
    // otherwise links Cards/ to Net/, so a renumber or a mid-list insertion over there would
    // corrupt every peer's browse fan with NO error, NO single-player symptom and nothing wrong on
    // the sender's own screen — a sender never parses its own packet.
    //
    // If the two orders ever diverge, the divisor below is 0 and THIS LINE STOPS COMPILING with
    // CS0020 "Division by constant zero". Values are append-only: adding a FOURTH pile at the end
    // is fine and this guard deliberately permits it.
    private const int PileKindWireOrderGuard = 1 / (
        (int)PileKind.Discard == NetProtocol.PileBrowseKindDiscard &&
        (int)PileKind.Burnt == NetProtocol.PileBrowseKindBurnt &&
        (int)PileKind.Items == NetProtocol.PileBrowseKindItems ? 1 : 0);

    private INetTransport _transport = new NullNetTransport();
    private IBoardAnchor _anchor = WorldAnchor.Instance;

    // One buffer serves both packet types; sized to the larger of the two so either fits.
    private readonly byte[] _sendBuffer = new byte[Mathf.Max(AvatarSerializer.MaxSize, PresenceSerializer.MaxSize)];
    private float _sendAccumulator;
    private float _extrasAccumulator;

    // Sticky last card-FX event (report 6): re-sent on every extras packet for redundancy on the
    // unreliable side-channel. The receiver de-dupes on the sequence byte, so repeats cost 2 bytes
    // and never play twice.
    private byte _lastFxEndpoints;
    private byte _lastFxSeq;
    private bool _hasFx;

    // Last broadcast fan sizes — an on-change extras send keeps a peer's fan appearing/disappearing
    // with the gesture instead of up to one 5 Hz interval later (see TickExtrasSend).
    private int _lastSentHandCount = -1;
    private int _lastSentItemCount = -1;

    // Pile-browse (Abgelegt / Verbrannt reading fan): the last broadcast (kind, count) so opening,
    // switching and closing a browser also pre-empts the 5 Hz gate — the fan's EMERGE animation is
    // driven by the receiver's open transition, so a late packet would show the emerge after the
    // owner already finished reading. -1 = no browser open.
    private int _lastSentBrowseKind = -1;
    private int _lastSentBrowseCount = -1;
    private bool _loggedBrowseOpen;

    // Head-mask SIZE: the last wire code we broadcast, so a stepper edit pre-empts the 5 Hz gate
    // (the user resizes their mask while watching a peer's mirror/avatar — a 200 ms lag reads as
    // "the slider does nothing on their screen") and so the confirmation log fires once per CHANGE
    // instead of five times a second. -1 = never sent.
    private int _lastSentMaskSizeCode = -1;
    private int _lastSentHandScaleCode = -1;

    /// <summary>One-shot send-gate diagnostics: -1 silent/offline, 0 = "online, waiting for our
    /// player id" logged, 1 = "broadcast live" logged. See TickSend.</summary>
    private int _sendGateState = -1;

    // CONTROL-BOARD STYLE: same contract as the mask size one row up. Switching the board is a
    // deliberate, human-paced act the user performs while looking at their board, so the edge
    // pre-empts the 5 Hz gate (a peer must see the new material immediately, not up to 200 ms
    // later) and the confirmation log fires once per CHANGE, never per packet. -1 = never sent.
    private int _lastSentBoardStyleCode = -1;

    // BOARD-UI record (extension id 4): the last broadcast (buttons | overlays << 8), so a
    // button appearing/disappearing or the wanted-glow flipping pre-empts the 5 Hz gate — these
    // are the exact edges the receiver renders, and a 200 ms-late glow reads as "not synced"
    // (user defect 4/5). -1 = never sent. Human-paced changes; cannot become a stream.
    private int _lastSentBoardUi = -1;
    /// <summary>Last pick-banner line put on the wire (null = placard hidden) — change-gated log.</summary>
    private string? _lastSentPickBanner;

    /// <summary>Last board-tooltip text put on the wire (extension record 9; null = no tooltip /
    /// identity-gated). Appearance, disappearance and a text change are EDGES that pre-empt the
    /// 5 Hz gate — a tooltip that arrives 200 ms after the peer's hand stopped over the thing it
    /// explains reads as "not synced", same argument as the pick banner and the board-UI edges.
    /// Human-paced (a hover), so it can never become a stream.</summary>
    private string? _lastSentTooltip;

    // CARD HIGHLIGHT (extension record 6): the last broadcast (handIndex | fanIndex << 16), so a
    // lift moving from card to card pre-empts the 5 Hz gate (capped at the rig interval — see the
    // send site) and the confirmation log fires once per CHANGE. int.MinValue = never sent.
    private int _lastSentHighlight = int.MinValue;

    // PILE COUNTS (extension record 15, user defect "die Stapel-Zahlen müssen sofort
    // synchronisiert werden"): the last broadcast (discard | burnt<<8 | items<<16, -1 = stacks
    // hidden). A COUNT CHANGE pre-empts the 5 Hz gate OUTRIGHT — a discard is a discrete,
    // human-paced event and "sofort" is the requirement; it can never become a stream.
    // int.MinValue = never sent.
    private int _lastSentPileCounts = int.MinValue;

    // HALF HOVER (extension record 14): the last broadcast (slot | top<<8, -1 = none), so the
    // hover moving between halves pre-empts the 5 Hz gate (capped at the rig interval — a laser
    // can flick between halves several times a second) and the log fires once per CHANGE.
    // int.MinValue = never sent.
    private int _lastSentHalfHover = int.MinValue;

    // TRACK HOVER (extension record 16): the last broadcast (actorId, with bit 31 abused is not
    // safe — the popup flag is tracked alongside in the bool), -1 = none. Same capped pre-emption
    // as the half hover: a laser can sweep the whole track in under a second. int.MinValue =
    // never sent.
    private int _lastSentTrackHoverActor = int.MinValue;
    private bool _lastSentTrackHoverPopup;

    // BOARD POSE MOTION (defect 7 "Bewegen kommt nicht flüssig an"): the last SENT board pose in
    // the shared anchor frame. While the pose is CHANGING (the owner drags/scales their board),
    // extras go out at the RIG rate (SendRateHz, 15 Hz) instead of the idle 5 Hz — the receiver's
    // exponential easing then gets the same sample density the head/hands get, which is exactly
    // the smoothness bar the avatars already meet. Idle boards keep the flat 5 Hz cadence, so
    // this costs nothing while nobody moves a board. Chosen over a receive-side interpolation
    // buffer because it reuses the proven avatar pipeline unchanged (no new latency, no new
    // wire field, no second interpolation scheme to maintain).
    private bool _sentBoardPoseValid;
    private Vector3 _lastSentBoardPos;
    private Quaternion _lastSentBoardRot = Quaternion.identity;
    private float _lastSentBoardScale = 1f;

    // SECOND HELD FIGURE (user report: "Wenn ein Mitspieler zwei Figuren in der Hand haelt soll auch
    // dies vollstaendig synchronisiert werden"). The mini in the player's OTHER hand rides extension
    // record 8 on THIS packet, and it gets the SAME treatment the board pose above gets, for the
    // same reason and through the same mechanism: while it is moving, extras go out at the RIG rate
    // (SendRateHz), so the second figure is streamed at exactly the cadence the first one gets in
    // the rig packet and the receiver's identical easing then produces identical motion. Picking it
    // up / putting it down / swapping which mini it is are EDGES that pre-empt the gate outright, so
    // the second figure appears and disappears on the frame it happens rather than up to an extras
    // interval later. A perfectly still second figure falls back to the idle 5 Hz — nothing moves,
    // so nothing is observable there. _sentSecondFigureValid false = nothing sent yet this session.
    private bool _sentSecondFigureValid;
    private int _lastSentSecondActorId;
    private Vector3 _lastSentSecondPos;
    private Quaternion _lastSentSecondRot = Quaternion.identity;
    private bool _loggedSecondFigure;

    // SECOND HELD CARD (user ruling: "Alles soll synchronisiert werden - auch die Karten in der
    // jeweiligen Hand. Wenn Karten in beiden Haenden sind, soll das auch synchronisiert werden!").
    // The card in the player's OTHER hand rides extension record 10 on THIS packet and gets the
    // SAME treatment the second figure above gets, for the same reason and through the same
    // mechanism: while it moves, extras go out at the RIG rate (SendRateHz), so the second card is
    // streamed at exactly the cadence the first one gets in the rig packet's FlagHeldCard block
    // and the receiver's identical easing then produces identical motion. Grab and release are
    // EDGES that pre-empt the gate outright, so the second slab appears and vanishes with the
    // gesture rather than up to an extras interval later. A perfectly still second card falls
    // back to the idle 5 Hz — nothing moves, so nothing is observable there.
    // _sentSecondCardValid false = nothing sent yet this session.
    private bool _sentSecondCardValid;
    private Vector3 _lastSentSecondCardPos;
    private Quaternion _lastSentSecondCardRot = Quaternion.identity;
    private bool _loggedSecondCard;

    private readonly Dictionary<int, RemoteAvatar> _avatars = new();
    // Latest world-frame state per sender, awaiting apply on the next Update (dedup: only the
    // newest matters for an unreliable stream).
    private readonly Dictionary<int, AvatarState> _pending = new();
    // Latest world-frame EXTRAS (board + hand count) per sender, same dedup contract.
    private readonly Dictionary<int, PresenceState> _pendingExtras = new();
    private readonly List<int> _scratchIds = new();

    /// <summary>True while <see cref="OnPacketReceived"/> is subscribed to <see cref="_transport"/>.</summary>
    private bool _subscribed;

    /// <summary>
    /// Wire the driver to its transport + anchor.
    ///
    /// <para>CONFIGURE OWNS THE SUBSCRIPTION, and that is the whole fix for the first multiplayer
    /// test, in which neither player saw anything of the other. <c>AddComponent</c> runs
    /// <c>OnEnable</c> SYNCHRONOUSLY, one line before <see cref="NetModule"/> got here — so the
    /// driver subscribed to the field-initialised <see cref="NullNetTransport"/>, whose event had
    /// empty accessors and dropped the handler on the floor, and the real transport arriving a line
    /// later was never subscribed to at all. Sending worked perfectly the whole time; nothing was
    /// ever listening. Moving the subscription here makes it independent of call order and
    /// idempotent under re-configure.</para>
    /// </summary>
    public void Configure(INetTransport transport, IBoardAnchor anchor)
    {
        SetTransport(transport ?? new NullNetTransport());
        _anchor = anchor ?? WorldAnchor.Instance;
    }

    /// <summary>Swap transports, carrying the subscription across exactly once.</summary>
    private void SetTransport(INetTransport transport)
    {
        if (ReferenceEquals(transport, _transport))
            return;

        Unsubscribe();
        _transport = transport;
        if (isActiveAndEnabled)
            Subscribe();
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;
        _transport.PacketReceived += OnPacketReceived;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        _transport.PacketReceived -= OnPacketReceived;
        _subscribed = false;
    }

    /// <summary>
    /// The live driver, or null (networking off / offline / module shut down). Exists ONLY as the
    /// read seam for <see cref="CollectPeerHeads"/> — nothing writes through it.
    /// </summary>
    private static NetAvatarDriver? _instance;

    /// <summary>
    /// Append every peer's last RECEIVED head world position to <paramref name="into"/> and return
    /// how many were added.
    ///
    /// <para>WHY THIS SEAM EXISTS: the VR spawn ring (<see cref="Rig.SpawnRing"/>) has to know
    /// where the other players are standing before it can seat a joining player in the largest
    /// free wedge around the table. That information is ALREADY here — it rides the rig packets
    /// the embodiment sync receives anyway — so the feature needs no new wire field, no new packet
    /// and no extra traffic. Strictly read-only and strictly local; a peer with no valid head pose
    /// yet is simply not counted.</para>
    /// </summary>
    internal static int CollectPeerHeads(List<Vector3> into)
    {
        NetAvatarDriver? driver = _instance;
        if (driver == null || into == null)
            return 0;

        int added = 0;
        foreach (KeyValuePair<int, RemoteAvatar> kv in driver._avatars)
        {
            if (kv.Value != null && kv.Value.TryGetHeadWorld(out Vector3 head))
            {
                into.Add(head);
                added++;
            }
        }
        return added;
    }

    private void OnEnable()
    {
        _instance = this;
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        _pending.Clear();
        _pendingExtras.Clear();
        _createRetryAt.Clear();
        _sendGateState = -1; // next session logs its join window from scratch
        NetCardFx.Reset(); // never carry a queued card animation into the next session
        _hasFx = false;
        _lastSentBoardUi = -1;      // next session re-states the board UI from scratch
        _lastSentPileCounts = int.MinValue;   // and re-states the pile counts…
        _lastSentHalfHover = int.MinValue;    // …the half hover…
        _lastSentTrackHoverActor = int.MinValue; // …and the track hover from scratch
        _sentBoardPoseValid = false; // and never diffs a new session's pose against a stale one
        _sentSecondFigureValid = false; // nor a new session's second held figure
        _lastSentSecondActorId = 0;
        _sentSecondCardValid = false;   // nor its second held card
        // Version handshake is session state; badges are reversible game-UI decoration — both
        // must not survive a driver teardown (hot reload / module shutdown).
        VersionGuard.Reset();
        PlayerBadges.RestoreAll();
        _flatApplied = false;
        DestroyAllAvatars();
    }

    /// <summary>
    /// Perf attribution (2026-07 perf pass): the networking driver is the only per-frame block
    /// whose cost SCALES WITH THE NUMBER OF PEERS, so it has to be measurable separately — a
    /// single-player capture that looks clean says nothing about a four-player table. Each
    /// sub-step gets its own scope so the multiplayer log can say whether the cost is inbound
    /// (ApplyPending / TickAvatars, the remote board + avatar rebuild) or outbound (TickSend).
    /// </summary>
    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        using (Core.PerfMonitor.Scope("Net.Avatar"))
        {
            // EVERY phase runs behind its own catch, because the phases are INDEPENDENT and an
            // exception must only cost the phase it happened in. The second multiplayer test
            // proved the opposite contract fatal: one deterministic NRE inside ApplyPending
            // (RemoteAvatar ctor) unwound this whole Update on the joiner every frame, so
            // TickSend below it never ran — a RECEIVE bug silenced the SEND path, and the host
            // saw nothing of a player whose own screen gave no hint anything was wrong. The
            // NREs also carried no stack in Player.log; the catch logs the full exception
            // through VRLog (throttled) so the next such bug names its line.
            using (Core.PerfMonitor.Scope("Net.ApplyPending"))
            {
                try { ApplyPending(); }
                catch (Exception e) { LogPhaseError("ApplyPending", e); }
            }
            using (Core.PerfMonitor.Scope("Net.TickAvatars"))
            {
                try { TickAvatars(dt); }
                catch (Exception e) { LogPhaseError("TickAvatars", e); }
            }
            using (Core.PerfMonitor.Scope("Net.Send"))
            {
                try { TickSend(dt); }
                catch (Exception e) { LogPhaseError("TickSend", e); }
                try { TickExtrasSend(dt); }
                catch (Exception e) { LogPhaseError("TickExtrasSend", e); }
            }
            using (Core.PerfMonitor.Scope("Net.Figures"))
            {
                try { NetFigures.Tick(); }
                catch (Exception e) { LogPhaseError("NetFigures.Tick", e); }
            }
            // Version handshake + VR badges: its OWN phase behind its OWN catch, per the driver's
            // isolation contract — a bug in the mismatch dialog or the badge poll must never
            // starve TickSend (the exact failure mode the per-phase catches exist for).
            using (Core.PerfMonitor.Scope("Net.Version"))
            {
                try { TickVersionGuard(); }
                catch (Exception e) { LogPhaseError("VersionGuard", e); }
            }
        }
    }

    /// <summary>True once this driver applied the flat-net teardown for the current
    /// <see cref="NetSession.FlatNetMode"/> episode (one-shot, re-arms when the flag clears).</summary>
    private bool _flatApplied;

    private void TickVersionGuard()
    {
        VersionGuard.Tick(_transport);

        if (NetSession.FlatNetMode)
        {
            // One-shot teardown on entering flat-net mode: drop everything remote via the same
            // paths staleness/offline use. The transport stays installed but inert (send +
            // receive are gated), so leaving flat mode on session end costs nothing.
            if (!_flatApplied)
            {
                _flatApplied = true;
                _pending.Clear();
                _pendingExtras.Clear();
                DestroyAllAvatars();
                PlayerBadges.RestoreAll();
                VRLog.Info("Net", "FLAT-NET MODE ACTIVE: remote avatars/boards torn down; mod "
                                  + "send + receive gated for the rest of the session.");
            }
            return;
        }
        _flatApplied = false;

        PlayerBadges.Tick(
            active: VRSession.IsRunning && _transport.IsOnline,
            localPlayerId: _transport.LocalPlayerId);
    }

    /// <summary>Seconds of silence between phase-failure error lines. A deterministic bug throws
    /// every frame; one line per window keeps the log readable while the STREAK stays visible.</summary>
    private const float PhaseErrorLogInterval = 5f;
    private float _nextPhaseErrorLog;
    private int _phaseErrorsSuppressed;

    private void LogPhaseError(string phase, Exception e)
    {
        _phaseErrorsSuppressed++;
        if (Time.unscaledTime < _nextPhaseErrorLog)
            return;
        _nextPhaseErrorLog = Time.unscaledTime + PhaseErrorLogInterval;
        VRLog.Error("Net", $"{phase} threw ({_phaseErrorsSuppressed} failure(s) in the last "
                           + $"{PhaseErrorLogInterval:0}s window) — phase skipped this frame, all "
                           + $"OTHER net phases keep running: {e}");
        _phaseErrorsSuppressed = 0;
    }

    // ---- send ---------------------------------------------------------------------------

    private void TickSend(float dt)
    {
        // FLAT-NET MODE (accepted version mismatch): the user chose to play this session as a
        // flat player — nothing of ours goes on the wire. Gated here (not in the transport) so
        // the transport stays installed and the mode reverses by simply clearing the flag.
        if (NetSession.FlatNetMode)
        {
            _sendAccumulator = 0f;
            return;
        }

        // Only VR players broadcast; flat/modded peers stay silent (and thus invisible to
        // others), exactly as intended. LocalPlayerId > 0 also gates out the join window
        // where the session is "online" but our NetworkPlayer (and thus MyPlayer, which
        // SendSideAction dereferences) does not exist yet. NOTE this is the ONLY thing the
        // broadcast waits for — character assignment is deliberately NOT required, so two
        // players see each other from the lobby on (user requirement, second MP test).
        if (!VRSession.IsRunning || !_transport.IsOnline || _transport.LocalPlayerId <= 0)
        {
            // Join-window forensics: the second MP test could not say WHY a client never sent.
            // One line when the id is the only thing missing, one when the gate opens (below).
            if (VRSession.IsRunning && _transport.IsOnline && _sendGateState != 0)
            {
                _sendGateState = 0;
                VRLog.Info("Net", "Broadcast WAITING: session online but our NetworkPlayer has "
                                  + "no id yet (join handshake) — sending starts the moment it "
                                  + "exists; character assignment is NOT required.");
            }
            else if (!_transport.IsOnline)
            {
                _sendGateState = -1; // offline: silent, and re-log the next join from scratch
            }
            _sendAccumulator = 0f;
            return;
        }

        if (_sendGateState != 1)
        {
            _sendGateState = 1;
            VRLog.Info("Net", $"Broadcast LIVE as player {_transport.LocalPlayerId} — head+hands "
                              + $"at {NetProtocol.SendRateHz:0} Hz, extras at "
                              + $"{NetProtocol.ExtrasSendRateHz:0} Hz to all peers; advertising "
                              + $"mod build {NetProtocol.ModBuild} ({MyPluginInfo.PLUGIN_VERSION}).");
        }

        _sendAccumulator += dt;
        float interval = 1f / NetProtocol.SendRateHz;
        if (_sendAccumulator < interval)
            return;
        // Drop excess accumulation (don't burst after a hitch).
        _sendAccumulator = 0f;

        if (!LocalRigSampler.TrySample(_anchor, IncludeFingers, out AvatarState state))
            return;

        int len = AvatarSerializer.Write(in state, _sendBuffer);
        _transport.Send(_sendBuffer, len);
    }

    // ---- extras send (board pose + hand count, slower) ----------------------------------

    private void TickExtrasSend(float dt)
    {
        // Same flat-net gate as TickSend — see there.
        if (NetSession.FlatNetMode
            || !VRSession.IsRunning || !_transport.IsOnline || _transport.LocalPlayerId <= 0)
        {
            _extrasAccumulator = 0f;
            return;
        }

        _extrasAccumulator += dt;
        float interval = 1f / NetProtocol.ExtrasSendRateHz;
        // CARD-FX LATENCY (report 6): a card flight lasts 0.4 s, so an event that waits for the
        // next 5 Hz slot could arrive after the real animation already finished. A queued event
        // therefore PRE-EMPTS the rate gate and goes out on the frame it happened; the extras
        // packet is ~10 bytes, and events are human-paced (a handful per turn), so this cannot
        // become a stream. Everything else still rides the normal 5 Hz cadence.
        bool fxPending = NetCardFx.Pending;
        // FAN VISIBILITY (report 6, "Fächer ist sichtbar"): raising/lowering the hand or item fan,
        // and every card that enters or leaves it, is a COUNT change — and a count that waits up to
        // 200 ms makes the peer's fan appear noticeably after the hand gesture that raised it. Send
        // on change too, same argument (and same tiny cost) as the card-FX pre-emption below.
        int handNow = CardFan.Current?.Count ?? 0;
        ItemsPile? itemsNow = ItemsPile.Current;
        int itemsCount = itemsNow != null && itemsNow.IsOpen ? itemsNow.Chips.Count : 0;
        bool countsChanged = handNow != _lastSentHandCount || itemsCount != _lastSentItemCount;

        // PILE BROWSE (user request "Auf-/Zuklappen der Fächer im Multiplayer"): the discard/burnt
        // reading fan. Read through the PileBrowser.Current seam — the browser instance itself is a
        // private of CardsDriver. Gated on Count > 0 because the arc's content only fills in the
        // driver's NEXT rebuild pass: a zero-card block would make the peer emerge an empty fan and
        // then emerge it AGAIN one frame later when the cards arrive.
        PileBrowser? browseNow = PileBrowser.Current;
        int browseKind = browseNow != null && browseNow.IsOpen && browseNow.Kind.HasValue
                         && browseNow.Cards.Count > 0
            ? (int)browseNow.Kind.Value
            : -1;
        int browseCount = browseKind >= 0 ? browseNow!.Cards.Count : -1;
        // Open, CLOSE and pile-switch are all state EDGES the receiver animates, so every one of
        // them pre-empts the rate gate exactly like a card-FX event does. Two extra bytes on a
        // human-paced action; it cannot become a stream.
        bool browseChanged = browseKind != _lastSentBrowseKind || browseCount != _lastSentBrowseCount;

        // HEAD-MASK SIZE (user request "Die Groesse der Maske ... entsprechend so synchronisiert"):
        // quantized to the wire byte FIRST, so the change test is the change the receiver can
        // actually observe (a sub-0.01 config wobble must not trigger a packet).
        byte maskSizeCode = NetProtocol.EncodeMaskSize(LocalRigSampler.LocalMaskSize());
        bool maskSizeChanged = maskSizeCode != _lastSentMaskSizeCode;

        // CONTROL-BOARD STYLE (user: the board is picked in the normal settings "genau wie die
        // Hände und die Maske" — so it must travel like them): the wire code of the board the local
        // player currently uses. Read here, same as the mask size, so a switch in the VR settings
        // pre-empts the rate gate and reaches every peer on the next frame.
        byte boardStyleCode = LocalRigSampler.LocalBoardStyle();
        bool boardStyleChanged = boardStyleCode != _lastSentBoardStyleCode;

        // PER-STYLE HAND SCALE — the last per-player choice that was still drawn from the
        // RECEIVER's config, so a peer who never touched it rendered your hands at their own size.
        // Quantized first, like the mask size, so a sub-0.01 wobble cannot trigger a packet.
        byte handScaleCode = NetProtocol.EncodeHandScale(
            Hands.HandVisuals.StyleScale(Hands.HandVisuals.LocalStyle()));
        bool handScaleChanged = handScaleCode != _lastSentHandScaleCode;

        // BOARD POSE + BOARD-UI STATE (defects 4/5/7), sampled BEFORE the rate gate so both can
        // pre-empt it. The pose is converted to the shared anchor frame HERE (once) and reused in
        // the packet below.
        PlayTray? trayNow = PlayTray.Current;
        Transform? board = trayNow?.Root;
        Vector3 boardPos = default;
        Quaternion boardRot = Quaternion.identity;
        float boardScale = 1f;
        if (board != null)
        {
            _anchor.ToAnchor(board.position, board.rotation, out boardPos, out boardRot);
            float ls = board.lossyScale.x;
            boardScale = ls > 0f ? ls : 1f;
        }
        // "Moving" = the pose left the last SENT sample by more than float noise. While true,
        // extras ride at the RIG rate (15 Hz) so a carried board arrives as smoothly as a hand;
        // the moment it settles, one final exact sample goes out and the cadence falls back to
        // 5 Hz (see the field block for why this path was chosen over an interp buffer).
        bool boardMoving = board != null && _sentBoardPoseValid
            && ((boardPos - _lastSentBoardPos).sqrMagnitude > 1e-8f
                || Quaternion.Angle(boardRot, _lastSentBoardRot) > 0.05f
                || !Mathf.Approximately(boardScale, _lastSentBoardScale));
        float fastInterval = 1f / NetProtocol.SendRateHz;
        bool poseDue = boardMoving && _extrasAccumulator >= fastInterval;

        // BOARD-UI (defects 4 + 5): which controls the owner's board shows RIGHT NOW plus the
        // wanted-slot glow mask — read off the same objects that drive the local rendering, so
        // the wire state is the rendered state by construction. -1 = no live tray this frame.
        int boardUiNow = -1;
        if (trayNow != null)
        {
            byte buttons = 0;
            if (trayNow.ConfirmControlShown) buttons |= NetProtocol.BoardUiConfirmBit;
            if (trayNow.UndoControlShown) buttons |= NetProtocol.BoardUiUndoBit;
            if (trayNow.ItemUseSlotShown) buttons |= NetProtocol.BoardUiItemRecessBit;
            if (trayNow.ItemUseCapShown) buttons |= NetProtocol.BoardUiItemUseCapBit;
            if (RestControls.ShortRestShown) buttons |= NetProtocol.BoardUiShortRestBit;
            if (RestControls.LongRestShown) buttons |= NetProtocol.BoardUiLongRestBit;
            if (WorldUI.ButtonCluster.BoardSkipShown) buttons |= NetProtocol.BoardUiSkipBit;
            if (WorldUI.ModalFallback.DecisionDock.ActivePrompt() != null)
                buttons |= NetProtocol.BoardUiDecisionBit;
            int overlays = trayNow.WantedSlotMask & NetProtocol.BoardUiWantedMask;
            // FOLLOW/PIN (this round's defect (a)): the label AND the accent of the toggle on the
            // owner's board follow [Cards] TrayFollow, so peers must see the same two-state cap
            // rather than one fixed look. Bit set == PINNED, because a sender that predates the
            // bit writes 0 and 0 has to mean the look those senders were already drawn in.
            if (!CardsConfig.TrayFollow.Value)
                overlays |= NetProtocol.BoardUiPinnedBit;
            // CARD-SLOT OCCUPANCY (user report, hardware MP test: "Ich will auch sehen wenn eine
            // Karte abgelegt wurde auf dem controllboard (mit der Rueckseite). Also wo aktuell eine
            // Karte liegt und wo nicht ... soll vollstaendig synchronisiert werden"). The PHYSICAL
            // truth of the two recesses, read off the slot anchors themselves (PlayTray
            // .OccupiedSlotMask) — so every path that parks a card there is covered by one read and
            // none of them can latch a stale bit. The VALIDITY bit rides with it on every packet:
            // "both slots empty" is real state here and must be distinguishable from a sender that
            // predates the nibble, which also writes zeroes.
            overlays |= (trayNow.OccupiedSlotMask << NetProtocol.BoardUiSlotShift)
                        & NetProtocol.BoardUiSlotMask;
            overlays |= NetProtocol.BoardUiSlotsValidBit;
            boardUiNow = buttons | ((overlays & NetProtocol.BoardUiOverlayMask) << 8);
        }
        bool boardUiChanged = boardUiNow != _lastSentBoardUi;

        // CARD HIGHLIGHT (extension record 6, defect (f) "das Hervorheben von Karten ist gar nicht
        // synchronisiert"): WHICH card in the hand fan and in the open board fan the owner is
        // singling out. Read as a bare INDEX off the fans' own highlight predicate — never a card
        // identity, which is the standing rule for this wire. -1 = nothing highlighted there.
        int handHl = CardFan.Current?.HighlightedIndex ?? -1;
        // BOARD FAN = the pile browser OR the item fan — at most one is open (Cards-layer mutual
        // exclusion), so one wire field covers both. The item fan was missing here, which is why a
        // peer never saw an item chip lift (user report 2026-08-03).
        int fanHl = PileBrowser.Current?.HighlightedIndex ?? -1;
        if (fanHl < 0)
            fanHl = ItemsPile.Current?.HighlightedIndex ?? -1;
        int highlightNow = (handHl & 0xFFFF) | (fanHl << 16);
        // A hover is a HUMAN-PACED gesture, but sweeping a hand along a fan can step the index
        // several times a second, so the pre-emption is capped at the rig interval exactly like
        // the board pose: fast enough that the lift lands with the gesture, never a stream.
        bool highlightChanged = highlightNow != _lastSentHighlight;
        bool highlightDue = highlightChanged && _extrasAccumulator >= fastInterval;

        // PILE COUNTS (extension record 15, user defect "die Stapel-Zahlen müssen sofort
        // synchronisiert werden"): the numbers OUR OWN three stack labels display right now,
        // read off the seam the renderer itself publishes (PileViewer.CurrentCounts — the
        // rendered values, not a second model derivation, so the wire state is the displayed
        // state by construction). Null = the stacks are hidden / no hand presented ⇒ record
        // absent ⇒ receivers keep their legacy model-read counts, exactly like a pre-record
        // sender. A change pre-empts the 5 Hz gate OUTRIGHT: discards are discrete, human-paced
        // events and the requirement is "sofort".
        (int discard, int burnt, int items)? pileCountsNow = PileViewer.CurrentCounts;
        int pileCountsKey = pileCountsNow.HasValue
            ? (Mathf.Clamp(pileCountsNow.Value.discard, 0, 255)
               | Mathf.Clamp(pileCountsNow.Value.burnt, 0, 255) << 8
               | Mathf.Clamp(pileCountsNow.Value.items, 0, 255) << 16)
            : -1;
        bool pileCountsChanged = pileCountsKey != _lastSentPileCounts;

        // HALF HOVER (extension record 14, user defect "die Overlay-Auswahl beim Hovern in der
        // Aktionsauswahl ist nicht synchronisiert"): which docked round card's action half OUR
        // pointer is on, sampled off the same registry the local overlay is driven from
        // (HalfSelection — fed by FullAbilityCard.OnPointerEnter/Exit, which BOTH pointer paths
        // call: the geometric laser resolve and the fingertip's uGUI pusher chain). A slot + a
        // half, never a card. Same capped pre-emption as the card highlight: the beam can flick
        // between halves several times a second.
        bool halfHover = HalfSelection.TrySampleLocalHover(out int halfSlot, out bool halfTop);
        int halfHoverNow = halfHover ? (halfSlot | (halfTop ? 1 << 8 : 0)) : -1;
        bool halfHoverChanged = halfHoverNow != _lastSentHalfHover;
        bool halfHoverDue = halfHoverChanged && _extrasAccumulator >= fastInterval;

        // TRACK HOVER (extension record 16, user defect "die Mouseover der Initiativreihenfolge
        // sind nicht synchronisiert"): which initiative-track entry OUR pointer is on (stable
        // CActor.ID — the display order is per-client, see the record doc) plus whether its info
        // popup is open. Same capped pre-emption: a laser can sweep the whole track in under a
        // second.
        bool trackHover = InitiativeHoverSampler.TrySample(out int trackActorId, out bool trackPopup);
        int trackHoverActorNow = trackHover ? trackActorId : -1;
        bool trackHoverChanged = trackHoverActorNow != _lastSentTrackHoverActor
                                 || (trackHover && trackPopup != _lastSentTrackHoverPopup);
        bool trackHoverDue = trackHoverChanged && _extrasAccumulator >= fastInterval;

        // SECOND HELD FIGURE (extension record 8): the mini in the player's OTHER hand. Sampled
        // BEFORE the rate gate so it can pre-empt it, and converted to the shared anchor frame here
        // (once) so the change test compares the very bytes that go on the wire.
        //
        // WHY THE PRIMARY'S HAND IS SAMPLED TOO: the record names the hand of BOTH minis. The rig
        // packet cannot carry its own figure's hand — its flag byte is full — so the only place the
        // two hands can be stated together is here, and stating them together is what lets a
        // receiver reject a contradictory pair instead of stacking two minis in one palm.
        bool secondFigure = NetFigures.TrySampleHeldSlot(
            NetFigures.SlotSecondary, out int secondActorId, out Vector3 secondWorldPos,
            out Quaternion secondWorldRot, out bool secondLeftHand);
        bool primaryLeftHand = false;
        Vector3 secondPos = default;
        Quaternion secondRot = Quaternion.identity;
        if (secondFigure)
        {
            // A second figure without a first is not a state the grab registry can produce, but the
            // record's whole meaning is "the OTHER one" — so it is only ever emitted alongside a
            // first figure, and never with two identical hands.
            secondFigure = NetFigures.TrySampleHeldSlot(
                                NetFigures.SlotPrimary, out int primaryActorId, out _, out _,
                                out primaryLeftHand)
                           && primaryActorId != secondActorId
                           && primaryLeftHand != secondLeftHand;
        }
        if (secondFigure)
            _anchor.ToAnchor(secondWorldPos, secondWorldRot, out secondPos, out secondRot);
        // Grab / release / a different mini in that hand are EDGES: they pre-empt the gate outright
        // so a peer sees the second figure appear and vanish with the gesture.
        bool secondChanged = secondFigure != _sentSecondFigureValid
                             || (secondFigure && secondActorId != _lastSentSecondActorId);
        // Carrying it is a MOTION, handled exactly like the dragged control board above: while the
        // pose keeps changing the whole extras packet rides at the rig rate, so this figure gets the
        // same sample density as the one in the rig packet.
        bool secondMoving = secondFigure && _sentSecondFigureValid
            && secondActorId == _lastSentSecondActorId
            && ((secondPos - _lastSentSecondPos).sqrMagnitude > 1e-8f
                || Quaternion.Angle(secondRot, _lastSentSecondRot) > 0.05f);
        bool secondDue = secondMoving && _extrasAccumulator >= fastInterval;

        // SECOND HELD CARD (extension record 10): the card in the player's OTHER hand, present
        // only while BOTH hands hold one — the rig packet's FlagHeldCard slot keeps carrying the
        // sampler's unchanged left-first pick, so this is deterministically the RIGHT hand's card.
        // Sampled BEFORE the rate gate so it can pre-empt it, and converted to the shared anchor
        // frame here (once) so the change test compares the very bytes that go on the wire. Same
        // edge/motion treatment as the second figure above: grab/release pre-empt outright, and
        // while the card moves the whole extras packet rides at the rig rate so both held cards
        // stream at the same cadence.
        bool secondCard = LocalRigSampler.TrySampleSecondHeldCard(
            out Vector3 secondCardWorldPos, out Quaternion secondCardWorldRot);
        Vector3 secondCardPos = default;
        Quaternion secondCardRot = Quaternion.identity;
        if (secondCard)
            _anchor.ToAnchor(secondCardWorldPos, secondCardWorldRot,
                             out secondCardPos, out secondCardRot);
        bool secondCardChanged = secondCard != _sentSecondCardValid;
        bool secondCardMoving = secondCard && _sentSecondCardValid
            && ((secondCardPos - _lastSentSecondCardPos).sqrMagnitude > 1e-8f
                || Quaternion.Angle(secondCardRot, _lastSentSecondCardRot) > 0.05f);
        bool secondCardDue = secondCardMoving && _extrasAccumulator >= fastInterval;

        // BOARD TOOLTIP (extension record 9): the text of the board-owned tooltip the owner is
        // reading, ALREADY identity-gated by WorldTooltips (only content public to peers ever
        // reaches this read — see NetProtocol.ExtIdBoardTooltip). Sampled before the rate gate so
        // its appearance/disappearance/text-change edges pre-empt it like the pick banner's do.
        string? tooltipNow = WorldUI.WorldTooltips.WireText;
        bool tooltipChanged = tooltipNow != _lastSentTooltip;

        if (_extrasAccumulator < interval && !fxPending && !countsChanged && !browseChanged
            && !maskSizeChanged && !boardStyleChanged && !handScaleChanged
            && !poseDue && !boardUiChanged && !highlightDue
            && !secondChanged && !secondDue && !secondCardChanged && !secondCardDue
            && !tooltipChanged && !pileCountsChanged && !halfHoverDue && !trackHoverDue)
            return;
        _extrasAccumulator = 0f;
        _lastSentHandCount = handNow;
        _lastSentItemCount = itemsCount;

        var extras = default(PresenceState);

        if (board != null)
        {
            extras.HasBoard = true;
            extras.Board.Position = boardPos;
            extras.Board.Rotation = boardRot;
            extras.BoardScale = boardScale;
            _sentBoardPoseValid = true;
            _lastSentBoardPos = boardPos;
            _lastSentBoardRot = boardRot;
            _lastSentBoardScale = boardScale;
        }
        else
        {
            _sentBoardPoseValid = false;
        }

        extras.HandCardCount = (byte)Mathf.Clamp(handNow, 0, 255);
        extras.DominantRight = LocalRigSampler.LocalDominantRight();

        // Ghost hand (cosmetic): whether ANY of our hands is faded, plus the strength WE chose —
        // a peer must see our ghost hands exactly as we do, the same contract as the transmitted
        // hand style / head mask. The legacy flag carries no side (receivers used to infer "the
        // non-dominant hand", which the fan made true); since a HELD CARD can ghost either hand
        // or both, the exact sides ride the extension tail as a bitmask. Pre-extension peers skip
        // it and keep the old inference.
        extras.GhostHand = Hands.HandGhosts.LocalSide != null;
        if (extras.GhostHand)
            extras.GhostStrength = (byte)Mathf.Clamp(
                Mathf.RoundToInt(Hands.HandGhosts.Strength * 255f), 0, 255);
        byte ghostSides = Hands.HandGhosts.LocalSidesMask;
        extras.HasGhostSides = ghostSides != 0;
        extras.GhostSidesMask = ghostSides;

        // ITEM fan (report 5): the equipped-item fan is a completely separate object from the
        // ability fan (Cards.ItemsPile, not Cards.CardFan) and used to be broadcast NOWHERE, so a
        // peer raising their items saw nothing on anyone else's screen. Count + held/board-anchored
        // flag only — backs on the receiver, no item identity.
        if (itemsCount > 0)
        {
            extras.HasItemFan = true;
            extras.ItemCardCount = (byte)Mathf.Clamp(itemsCount, 0, 255);
            extras.ItemFanHeld = itemsNow != null && itemsNow.IsHandHeld;
            extras.ItemFanLeftHand = itemsNow != null && itemsNow.IsHeldByLeftHand;
        }

        // PILE BROWSE block (additive FlagPileBrowse, the LAST free extras flag bit): which pile the
        // sender has open, how many cards the arc holds, and whether it is a hand-held reading fan.
        // Count + placement only — the receiver renders BACKS, so no card identity rides the wire,
        // exactly like the hand and item fans. Note the mutual exclusion the Cards layer already
        // enforces (PileViewer.ItemsOpening / DispatchPoke close the other fan): at most ONE pile
        // fan is ever open, so this block and FlagItemFan can never both describe a fan at once.
        if (browseKind >= 0)
        {
            extras.HasPileBrowse = true;
            // PileKind order == PileBrowseKind* wire order — enforced by PileKindWireOrderGuard.
            extras.PileBrowseKind = (byte)browseKind;
            extras.PileBrowseCardCount = (byte)Mathf.Clamp(browseCount, 0, 255);
            extras.PileBrowseHeld = browseNow!.IsHandHeld;
            extras.PileBrowseLeftHand = browseNow.IsHeldByLeftHand;
        }

        // FAN ANCHOR (extension record 5, defect 6 "die Fächer sitzen woanders"): the open
        // BOARD-ANCHORED fan's real board-local position — the authored base spot PLUS the
        // owner's live per-board offsets, which never crossed the wire before, so a tuned
        // player's fan floated at the untuned default on every peer. At most one of the two
        // fans is ever open (Cards-layer mutual exclusion), so one record covers both; held
        // fans return null and keep the hand-relative placement. Written every packet while
        // open — absence must keep meaning "authored default", never "stale last value".
        Vector3? fanAnchor = itemsCount > 0 && itemsNow != null ? itemsNow.BoardLocalAnchor : null;
        if (fanAnchor == null && browseKind >= 0)
            fanAnchor = browseNow!.BoardLocalAnchor;
        if (fanAnchor.HasValue)
        {
            extras.HasFanAnchor = true;
            extras.FanAnchorLocal = fanAnchor.Value;
        }

        // BOARD UI (extension record 4, defects 4 + 5): written on EVERY packet with a live
        // tray, so a peer can tell "no dynamic controls shown" (record present, bits clear)
        // from "pre-record sender" (record absent ⇒ legacy always-drawn furniture).
        if (boardUiNow >= 0)
        {
            extras.HasBoardUi = true;
            extras.BoardButtonsMask = (byte)(boardUiNow & 0xFF);
            extras.BoardOverlayMask = (byte)((boardUiNow >> 8) & 0xFF);
        }
        // PICK BANNER (extension record 7): the placard line above the owner's board, so a peer's
        // remote board carries the same sentence at the same seat. Written only while a placard is
        // really shown — an idle packet stays byte-identical to the previous build's.
        string? bannerNow = trayNow != null ? trayNow.PickBannerText : null;
        if (!string.IsNullOrEmpty(bannerNow))
        {
            extras.HasPickBanner = true;
            extras.PickBannerText = bannerNow;
        }
        if (bannerNow != _lastSentPickBanner)
        {
            _lastSentPickBanner = bannerNow;
            VRLog.Info("Net", string.IsNullOrEmpty(bannerNow)
                ? "Pick banner SENT: placard hidden — record omitted (peers hide theirs too)."
                : $"Pick banner SENT: \"{bannerNow}\" — extension record 7 (UTF8, capped " +
                  $"{NetProtocol.PickBannerTextMaxBytes} B: an actor and a count, NO card identity); " +
                  "peers show it on the remote board at the same board-local seat.");
        }
        // BOARD TOOLTIP (extension record 9): written on every packet WHILE a board-owned,
        // identity-gate-passed tooltip is shown; omitted otherwise, so an idle packet stays
        // byte-identical to the previous build's. The gate itself lives at the source
        // (WorldUI.WorldTooltips.WireText is null for anything peers may not see).
        if (!string.IsNullOrEmpty(tooltipNow))
        {
            extras.HasBoardTooltip = true;
            extras.BoardTooltipText = tooltipNow;
        }
        if (tooltipChanged)
        {
            _lastSentTooltip = tooltipNow;
            VRLog.Info("Net", string.IsNullOrEmpty(tooltipNow)
                ? "Board tooltip SENT: hidden — record omitted (peers hide theirs too)."
                : $"Board tooltip SENT: {tooltipNow!.Length} chars — extension record 9 (UTF8, " +
                  $"capped {NetProtocol.TooltipTextMaxBytes} B, identity-gated at the source: " +
                  "only content already public to peers); peers show it at the remote board's " +
                  "tooltip area.");
        }

        if (boardUiChanged)
        {
            if (boardUiNow >= 0)
            {
                VRLog.Info("Net", $"Board UI SENT: buttons=0x{boardUiNow & 0xFF:X2} " +
                                  $"(confirm={(boardUiNow & NetProtocol.BoardUiConfirmBit) != 0}, " +
                                  $"undo={(boardUiNow & NetProtocol.BoardUiUndoBit) != 0}, " +
                                  $"recess={(boardUiNow & NetProtocol.BoardUiItemRecessBit) != 0}, " +
                                  $"useCap={(boardUiNow & NetProtocol.BoardUiItemUseCapBit) != 0}, " +
                                  $"shortRest={(boardUiNow & NetProtocol.BoardUiShortRestBit) != 0}, " +
                                  $"longRest={(boardUiNow & NetProtocol.BoardUiLongRestBit) != 0}, " +
                                  $"skip={(boardUiNow & NetProtocol.BoardUiSkipBit) != 0}, " +
                                  $"decision={(boardUiNow & NetProtocol.BoardUiDecisionBit) != 0}), " +
                                  $"wanted-glow mask={(boardUiNow >> 8) & NetProtocol.BoardUiWantedMask}, " +
                                  $"tray={(((boardUiNow >> 8) & NetProtocol.BoardUiPinnedBit) != 0 ? "PINNED" : "FOLLOW")}, " +
                                  $"slots={((boardUiNow >> (8 + NetProtocol.BoardUiSlotShift)) & 0x3)} " +
                                  $"(slot1={(((boardUiNow >> 8) & NetProtocol.BoardUiSlot0Bit) != 0 ? "card" : "empty")}, " +
                                  $"slot2={(((boardUiNow >> 8) & NetProtocol.BoardUiSlot1Bit) != 0 ? "card" : "empty")}) " +
                                  "— peers show EXACTLY these controls (extension record 4; the " +
                                  "FOLLOW/PIN state is byte 1 bit 2, the card-slot occupancy is " +
                                  "byte 1 bits 3..4 with its validity bit 5, new this build). The " +
                                  "occupancy is a POSITION only — peers draw a card BACK there; no " +
                                  "card identity rides this wire.");
            }
            else
            {
                VRLog.Info("Net", "Board UI SENT: no live tray — record omitted (peers keep the last board state).");
            }
        }
        _lastSentBoardUi = boardUiNow;

        // CARD HIGHLIGHT (extension record 6): written ONLY while something really is highlighted,
        // so an idle player's packet stays byte-identical to the previous build's — "record absent"
        // and "both indices none" render the same flat fans on every receiver.
        if (handHl >= 0 || fanHl >= 0)
        {
            extras.HasCardHighlight = true;
            extras.HandHighlightIndex = NetProtocol.EncodeHighlightIndex(handHl);
            extras.FanHighlightIndex = NetProtocol.EncodeHighlightIndex(fanHl);
        }
        if (highlightChanged)
        {
            _lastSentHighlight = highlightNow;
            VRLog.Info("Net", $"Card highlight SENT: hand fan index {(handHl >= 0 ? handHl.ToString() : "none")}, " +
                              $"board fan index {(fanHl >= 0 ? fanHl.ToString() : "none")} — " +
                              (extras.HasCardHighlight
                                  ? "extension record 6 (2 B: positions only, NO card identity); " +
                                    "peers lift the same card and split its neighbours apart."
                                  : "nothing lifted, record omitted (peers render flat fans)."));
        }
        // PILE COUNTS (extension record 15): written on every packet while our stacks are
        // displayed — "present with zeros" must stay distinguishable from "pre-record sender"
        // (see the record doc). The values are the RENDERED ones; the receiver prefers them over
        // its own (session-proven laggy) model read.
        if (pileCountsNow.HasValue)
        {
            extras.HasPileCounts = true;
            extras.PileDiscardCount = (byte)Mathf.Clamp(pileCountsNow.Value.discard, 0, 255);
            extras.PileBurntCount = (byte)Mathf.Clamp(pileCountsNow.Value.burnt, 0, 255);
            extras.PileItemsCount = (byte)Mathf.Clamp(pileCountsNow.Value.items, 0, 255);
        }
        if (pileCountsChanged)
        {
            _lastSentPileCounts = pileCountsKey;
            VRLog.Info("Net", pileCountsNow.HasValue
                ? $"Pile counts SENT: discard={pileCountsNow.Value.discard}, " +
                  $"burnt={pileCountsNow.Value.burnt}, items={pileCountsNow.Value.items} — " +
                  "extension record 15 (3 B, the numbers our own stack labels display; public " +
                  "info, no identity). The change PRE-EMPTED the extras gate, so peers' boards " +
                  "update immediately instead of waiting for their model to replay the turn."
                : "Pile counts SENT: stacks hidden — record omitted (peers fall back to their " +
                  "model-read counts).");
        }

        // HALF HOVER (extension record 14): written only while a half really is lit — "absent"
        // and "nothing lit" render identically, so an idle packet stays byte-identical.
        if (halfHover)
        {
            extras.HasHalfHover = true;
            extras.HalfHoverSlot = (byte)Mathf.Clamp(halfSlot, 0, NetProtocol.BoardUiSlotCount - 1);
            extras.HalfHoverTop = halfTop;
        }
        if (halfHoverChanged)
        {
            _lastSentHalfHover = halfHoverNow;
            VRLog.Info("Net", halfHover
                ? $"Half hover SENT: slot {halfSlot + 1}, {(halfTop ? "TOP" : "BOTTOM")} half — " +
                  "extension record 14 (1 B: a slot POSITION and a half, no card identity); " +
                  "peers glow the same half of the same docked round card."
                : "Half hover SENT: none — record omitted (peers clear the glow).");
        }

        // TRACK HOVER (extension record 16): written only while an entry really is hovered.
        if (trackHover)
        {
            extras.HasTrackHover = true;
            extras.TrackHoverActorId = trackActorId;
            extras.TrackHoverPopup = trackPopup;
        }
        if (trackHoverChanged)
        {
            _lastSentTrackHoverActor = trackHoverActorNow;
            _lastSentTrackHoverPopup = trackHover && trackPopup;
            VRLog.Info("Net", trackHover
                ? $"Track hover SENT: actor {trackActorId}, popup {(trackPopup ? "OPEN" : "closed")} — " +
                  "extension record 16 (5 B: stable actor id — the track's display order is " +
                  "per-client — plus the popup flag; the popup CONTENT is the receiver's own " +
                  "copy of the public track widget). Peers show this hover on OUR mirrored " +
                  "track only."
                : "Track hover SENT: none — record omitted (peers render our track un-hovered).");
        }

        // HEAD-MASK SIZE, riding the SAME trailing block (byte A bit 4 + one trailing byte — the
        // reserved-bit extension path both flag bytes' exhaustion forces us onto, see NetProtocol).
        // Sent ONLY when it differs from the default: absence already means "1.00x" to every
        // reader, so a default-size player's packets stay byte-identical to previous builds and a
        // step back to 1.00x is communicated by the byte disappearing again.
        if (maskSizeCode != NetProtocol.MaskSizeDefaultCode)
        {
            extras.HasMaskSize = true;
            extras.MaskSizeCode = maskSizeCode;
        }
        // Hand scale rides the EXTENSION TAIL, and only when it differs from 1.00x — an untuned
        // player's packet then stays byte-identical to the previous build's.
        if (handScaleCode != NetProtocol.HandScaleDefaultCode)
        {
            extras.HasHandScale = true;
            extras.HandScaleCode = handScaleCode;
        }
        if (handScaleChanged)
        {
            _lastSentHandScaleCode = handScaleCode;
            VRLog.Info("Net", $"Hand scale SENT: {NetProtocol.DecodeHandScale(handScaleCode):0.00}x " +
                              $"(wire code {handScaleCode}, hundredths) — " +
                              (extras.HasHandScale
                                  ? "1 extension-tail record (id 1)."
                                  : "default, record omitted (peers render 1.00x)."));
        }

        if (maskSizeChanged)
        {
            VRLog.Info("Net", $"Mask size SENT: {NetProtocol.DecodeMaskSize(maskSizeCode):0.00}x " +
                              $"(wire code {maskSizeCode}, hundredths) — " +
                              (extras.HasMaskSize
                                  ? "1 additive byte in the extras block (byte A bit 4)."
                                  : "default, byte omitted (peers render 1.00x)."));
        }
        _lastSentMaskSizeCode = maskSizeCode;

        // CONTROL-BOARD STYLE, riding the SAME trailing block's byte A (bits 5..6 — no extra byte
        // at all, see NetProtocol.PileBrowseBoardStyleShift). Assigned unconditionally: the
        // serializer itself omits the whole block when the style is the DEFAULT board and nothing
        // else needs the block, so a default-board player's packet stays byte-identical to what
        // previous builds emitted, and a switch BACK to the default is communicated by the bits
        // reading 0 again.
        extras.BoardStyleCode = boardStyleCode;
        if (boardStyleChanged)
        {
            VRLog.Info("Net", $"Control board style SENT: '{Cards.ControlBoards.Clamp(boardStyleCode)}' " +
                              $"(wire code {boardStyleCode}, extras block byte A bits 5..6) — " +
                              (boardStyleCode != NetProtocol.BoardStyleDefaultCode
                                  ? "0 extra bytes; peers tint their copy of this board to match."
                                  : "default board, block bits read 0 (peers render the default board)."));
        }
        _lastSentBoardStyleCode = boardStyleCode;

        // One log per OPEN/CLOSE/switch edge (never per packet) so a hardware log can prove each of
        // the three piles going out on the wire.
        if (browseChanged)
        {
            if (browseKind >= 0)
            {
                VRLog.Info("Net", $"Pile-browse SENT: {(PileKind)browseKind} fan open, {browseCount} card(s), " +
                                  $"{(extras.PileBrowseHeld ? $"held in the {(extras.PileBrowseLeftHand ? "LEFT" : "RIGHT")} hand" : "anchored above the board")} " +
                                  "— backs only (2-byte additive block, flag bit 7).");
                _loggedBrowseOpen = true;
            }
            else if (_loggedBrowseOpen)
            {
                _loggedBrowseOpen = false;
                VRLog.Info("Net", $"Pile-browse SENT: closed (was {(PileKind)_lastSentBrowseKind}) " +
                                  "— peers collapse the fan back into that stack.");
            }
        }
        _lastSentBrowseKind = browseKind;
        _lastSentBrowseCount = browseCount;

        // CARD-FX event (report 6): pop at most one queued animation per packet and stamp it with a
        // fresh sequence. When nothing is queued the LAST event is re-sent unchanged — deliberate
        // redundancy on an unreliable stream; the receiver only plays on a sequence CHANGE, so a
        // repeat is free and a single lost packet still lands within 200 ms.
        if (NetCardFx.TryDequeue(out byte fxEndpoints, out byte fxSeq))
        {
            _lastFxEndpoints = fxEndpoints;
            _lastFxSeq = fxSeq;
            _hasFx = true;
        }
        if (_hasFx)
        {
            extras.HasCardFx = true;
            extras.FxSeq = _lastFxSeq;
            extras.FxEndpoints = _lastFxEndpoints;
        }

        // SECOND HELD FIGURE (extension record 8): the mini in the player's OTHER hand, with the
        // hand of BOTH minis. Written only while a second figure is really held, so a one-handed
        // hold — and every empty-handed player — emits the exact bytes previous builds emitted.
        if (secondFigure)
        {
            extras.HasSecondFigure = true;
            extras.SecondFigureActorId = secondActorId;
            extras.SecondFigurePose.Position = secondPos;
            extras.SecondFigurePose.Rotation = secondRot;
            extras.SecondFigureLeftHand = secondLeftHand;
            extras.PrimaryFigureLeftHand = primaryLeftHand;
        }
        if (secondChanged)
        {
            if (secondFigure)
            {
                VRLog.Info("Net", $"Second held figure SENT: actor {secondActorId} in the " +
                                  $"{(secondLeftHand ? "LEFT" : "RIGHT")} hand, first figure in the " +
                                  $"{(primaryLeftHand ? "LEFT" : "RIGHT")} — extension record 8 " +
                                  "(25 B: hands + stable actor id + pose). While it moves the extras " +
                                  $"packet rides at {NetProtocol.SendRateHz:0} Hz, the SAME cadence " +
                                  "the first figure gets in the rig packet, so peers see both minis " +
                                  "move alike.");
                _loggedSecondFigure = true;
            }
            else if (_loggedSecondFigure)
            {
                _loggedSecondFigure = false;
                VRLog.Info("Net", $"Second held figure SENT: released (was actor {_lastSentSecondActorId}) " +
                                  "— record omitted; peers hand that mini back to the game, and the " +
                                  "figure still in the other hand is untouched.");
            }
        }
        _sentSecondFigureValid = secondFigure;
        _lastSentSecondActorId = secondFigure ? secondActorId : 0;
        _lastSentSecondPos = secondPos;
        _lastSentSecondRot = secondRot;

        // SECOND HELD CARD (extension record 10): the card in the player's OTHER hand — pose
        // only, no hand byte (the receiver renders the slab at the absolute pose, never parented
        // to a hand) and no identity, ever. Written only while BOTH hands really hold a card, so
        // a one-card hold — and every idle player — emits the exact bytes build 49 emitted.
        if (secondCard)
        {
            extras.HasSecondHeldCard = true;
            extras.SecondHeldCardPose.Position = secondCardPos;
            extras.SecondHeldCardPose.Rotation = secondCardRot;
        }
        if (secondCardChanged)
        {
            if (secondCard)
            {
                VRLog.Info("Net", "Second held card SENT: BOTH hands hold a card — the rig packet " +
                                  "keeps the LEFT hand's card (FlagHeldCard, unchanged), the RIGHT " +
                                  "hand's rides extension record 10 (20 B: pose only, back slab on " +
                                  "peers, no identity). While it moves the extras packet rides at " +
                                  $"{NetProtocol.SendRateHz:0} Hz, the SAME cadence the first card " +
                                  "gets, so peers see both slabs move alike.");
                _loggedSecondCard = true;
            }
            else if (_loggedSecondCard)
            {
                _loggedSecondCard = false;
                VRLog.Info("Net", "Second held card SENT: released — record omitted; peers drop " +
                                  "that slab, and the card still in the other hand is untouched.");
            }
        }
        _sentSecondCardValid = secondCard;
        _lastSentSecondCardPos = secondCardPos;
        _lastSentSecondCardRot = secondCardRot;

        // MOD VERSION (extension-tail record id 3): on EVERY extras packet, deliberately
        // breaking the "only when non-default" rule the other records follow — its ABSENCE is
        // the signal (peers without it read as pre-handshake ModBuild 0 = mismatch), so there
        // is no default whose omission would be equivalent. ~9 bytes at 5 Hz; the display
        // string is byte-capped and its encoding cached (PresenceSerializer), so this stays
        // allocation-free. This is also what implicitly advertises "I am a VR/modded player"
        // to every peer's badge/guard logic.
        extras.HasModVersion = true;
        extras.ModBuild = NetProtocol.ModBuild;
        extras.ModVersionText = MyPluginInfo.PLUGIN_VERSION;

        int len = PresenceSerializer.Write(in extras, _sendBuffer);
        _transport.Send(_sendBuffer, len);
    }

    // ---- receive ------------------------------------------------------------------------

    /// <summary>Packets accepted since the last receive summary (diagnostic only).</summary>
    private int _rxCount;
    private float _rxNextReport;
    private bool _rxFirstLogged;

    /// <summary>Seconds between receive summaries — rare enough to be free, often enough to watch.</summary>
    private const float RxReportInterval = 10f;

    private void OnPacketReceived(int senderId, byte[] buffer, int length)
    {
        // FLAT-NET MODE: received mod packets are not processed either — the session runs as if
        // the mod's net layer did not exist. Returning BEFORE any bookkeeping keeps the mode
        // absolute (no avatars, no version registry churn, no RX noise in the log).
        if (NetSession.FlatNetMode)
            return;

        // THE RECEIVE SIDE SAYS SOMETHING NOW. The mod logged every send and nothing at all on
        // receive, so a driver that was subscribed to a discarded event looked exactly like a
        // healthy one for a whole session: sends counted up, nobody appeared, no line to read.
        // One line on the first packet ever, then a summary every ten seconds.
        _rxCount++;
        if (!_rxFirstLogged)
        {
            _rxFirstLogged = true;
            VRLog.Info("Net", $"FIRST PACKET RECEIVED — from player {senderId}, {length} B, "
                              + $"type {NetPacket.PeekType(buffer, length)} (local id "
                              + $"{_transport.LocalPlayerId}). Receive is wired.");
        }
        else if (Time.unscaledTime >= _rxNextReport)
        {
            _rxNextReport = Time.unscaledTime + RxReportInterval;
            VRLog.Info("Net", $"RX {_rxCount} packet(s) in the last {RxReportInterval:0}s; "
                              + $"{_pending.Count} rig + {_pendingExtras.Count} extras pending.");
            _rxCount = 0;
        }

        // Ignore our own echo and unparseable/foreign packets.
        if (senderId != 0 && senderId == _transport.LocalPlayerId)
            return;

        // Route by message type without fully parsing (also rejects magic/version mismatches).
        int type = NetPacket.PeekType(buffer, length);
        switch (type)
        {
            case NetProtocol.MsgRig:
                if (AvatarSerializer.TryRead(buffer, length, out AvatarState state))
                {
                    VersionGuard.NotePacket(senderId); // any valid mod packet ⇒ a modded peer
                    // Convert the shared-frame poses to world here so RemoteAvatar stays world-only.
                    ToWorld(ref state);
                    _pending[senderId] = state; // dedup: keep only the newest
                }
                break;

            case NetProtocol.MsgExtras:
                if (PresenceSerializer.TryRead(buffer, length, out PresenceState extras))
                {
                    VersionGuard.NotePacket(senderId);
                    // The extras packet is the only one the version record rides — feed the
                    // handshake registry (record present: their build; absent: pre-handshake
                    // build 0, a mismatch by definition).
                    VersionGuard.NoteExtras(senderId, in extras);
                    ExtrasToWorld(ref extras);
                    _pendingExtras[senderId] = extras; // dedup: keep only the newest
                }
                break;
        }
    }

    private void ApplyPending()
    {
        // Per-SENDER catches, and the queues are cleared no matter what: one peer whose state
        // cannot be applied (avatar construction failed, malformed-but-parseable state) must
        // neither block the OTHER peers' packets nor pile its own up for an identical retry
        // next frame — the next 15 Hz packet is a fresh chance anyway.
        if (_pending.Count > 0)
        {
            foreach (KeyValuePair<int, AvatarState> kv in _pending)
            {
                try
                {
                    RemoteAvatar? avatar = GetOrCreate(kv.Key);
                    if (avatar == null)
                        continue; // construction failed recently — packet dropped, retry later
                    AvatarState s = kv.Value;
                    avatar.SetTarget(in s);

                    // Cosmetic figure sync: mirror the sender's FIRST held figure. Only the primary
                    // slot is touched here — the mini in their other hand rides the extras packet
                    // (record 8) and is applied below, so a peer holding two minis keeps both and
                    // dropping one releases exactly that one.
                    if (s.HasHeldFigure)
                        NetFigures.ApplyRemoteHeld(kv.Key, NetFigures.SlotPrimary, s.HeldFigureActorId,
                                                   s.HeldFigurePose.Position, s.HeldFigurePose.Rotation);
                    else
                        NetFigures.ReleaseRemoteSlot(kv.Key, NetFigures.SlotPrimary);
                }
                catch (Exception e) { LogPhaseError($"Apply rig packet from player {kv.Key}", e); }
            }
            _pending.Clear();
        }

        if (_pendingExtras.Count > 0)
        {
            foreach (KeyValuePair<int, PresenceState> kv in _pendingExtras)
            {
                try
                {
                    RemoteAvatar? avatar = GetOrCreate(kv.Key);
                    if (avatar == null)
                        continue; // construction failed recently — packet dropped, retry later
                    PresenceState p = kv.Value;
                    avatar.SetExtras(in p);

                    // Cosmetic figure sync, SECOND slot: the mini in the sender's other hand
                    // (extension record 8). Absence of the record means "at most one figure held",
                    // which is also what a peer predating the record transmits — both release only
                    // the second slot, never the first, so an old peer's single held figure keeps
                    // working exactly as it always did.
                    if (p.HasSecondFigure)
                    {
                        NetFigures.ApplyRemoteHeld(kv.Key, NetFigures.SlotSecondary, p.SecondFigureActorId,
                                                   p.SecondFigurePose.Position, p.SecondFigurePose.Rotation,
                                                   handKnown: true, leftHand: p.SecondFigureLeftHand);
                        // The record is also the only carrier of the FIRST figure's hand (the rig
                        // flag byte has no bit left for one), so stamp it on the primary slot.
                        NetFigures.NotePrimaryHand(kv.Key, p.PrimaryFigureLeftHand);
                    }
                    else
                    {
                        NetFigures.ReleaseRemoteSlot(kv.Key, NetFigures.SlotSecondary);
                    }
                }
                catch (Exception e) { LogPhaseError($"Apply extras packet from player {kv.Key}", e); }
            }
            _pendingExtras.Clear();
        }
    }

    /// <summary>Seconds before re-attempting a FAILED avatar construction for the same player.
    /// A failing ctor leaves a partial root GameObject behind (nothing holds a reference to
    /// destroy), so retrying at packet rate would leak one per packet; at this pace a whole
    /// evening leaks a few hundred empties while still self-healing if the cause was transient.</summary>
    private const float CreateRetryInterval = 5f;
    private readonly Dictionary<int, float> _createRetryAt = new();

    private RemoteAvatar? GetOrCreate(int playerId)
    {
        if (_avatars.TryGetValue(playerId, out RemoteAvatar existing))
            return existing;
        if (_createRetryAt.TryGetValue(playerId, out float retryAt) && Time.unscaledTime < retryAt)
            return null; // recent construction failure — let the backoff window pass
        try
        {
            var avatar = new RemoteAvatar(playerId);
            _avatars[playerId] = avatar;
            _createRetryAt.Remove(playerId);
            return avatar;
        }
        catch (Exception e)
        {
            _createRetryAt[playerId] = Time.unscaledTime + CreateRetryInterval;
            LogPhaseError($"RemoteAvatar construction for player {playerId}", e);
            return null;
        }
    }

    // ---- avatar lifetime ----------------------------------------------------------------

    private void TickAvatars(float dt)
    {
        if (_avatars.Count == 0)
            return;

        _scratchIds.Clear();
        foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
        {
            RemoteAvatar avatar = kv.Value;
            // Steam-avatar fetch pump, BEFORE the visuals and on its own catch: the game's
            // join-time fetch leaves every peer holding its grey placeholder (NetPlayerActors
            // .ClassifyAvatar), so the picture only appears if the mod re-asks. It is driven from
            // HERE — per KNOWN PEER, not per visible tag — so it runs the moment a peer exists,
            // whatever the NameTags config says and whichever role we are.
            try { NetPlayerActors.TickAvatarFetch(kv.Key); }
            catch (Exception e) { LogPhaseError($"Avatar fetch pump for player {kv.Key}", e); }
            // Per-avatar catch: one broken avatar must not stop the OTHERS from ticking, nor
            // block the staleness sweep below it (same isolation contract as ApplyPending).
            try { avatar.Tick(dt); }
            catch (Exception e) { LogPhaseError($"RemoteAvatar.Tick for player {kv.Key}", e); }
            // Teardown on staleness (covers Bolt player-left, a peer switching to flat, or a
            // long network stall — more robust than a single player-left callback).
            if (avatar.TimeSinceUpdate > NetProtocol.StaleTimeoutSeconds)
                _scratchIds.Add(kv.Key);
        }

        for (int i = 0; i < _scratchIds.Count; i++)
        {
            int id = _scratchIds[i];
            if (_avatars.TryGetValue(id, out RemoteAvatar avatar))
            {
                avatar.Destroy();
                _avatars.Remove(id);
                NetFigures.ReleaseRemote(id); // drop any figure this peer was holding
                NetPlayerActors.ForgetAvatarFetch(id); // a rejoin gets a fresh attempt budget
            }
        }
    }

    /// <summary>Immediate teardown entry point for a future <c>PlayerRegistry.OnPlayerLeft</c>
    /// hook (staleness already handles it after <see cref="NetProtocol.StaleTimeoutSeconds"/>).</summary>
    public void RemovePlayer(int playerId)
    {
        _pending.Remove(playerId);
        _pendingExtras.Remove(playerId);
        if (_avatars.TryGetValue(playerId, out RemoteAvatar avatar))
        {
            avatar.Destroy();
            _avatars.Remove(playerId);
            NetFigures.ReleaseRemote(playerId);
            NetPlayerActors.ForgetAvatarFetch(playerId);
        }
    }

    private void DestroyAllAvatars()
    {
        foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
        {
            kv.Value.Destroy();
            NetFigures.ReleaseRemote(kv.Key);
            NetPlayerActors.ForgetAvatarFetch(kv.Key);
        }
        _avatars.Clear();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;
        DestroyAllAvatars();
    }

    // ---- frame conversion ---------------------------------------------------------------

    private void ToWorld(ref AvatarState state)
    {
        if (state.HeadValid)
        {
            _anchor.ToWorld(state.Head.Position, state.Head.Rotation, out Vector3 p, out Quaternion r);
            state.Head.Position = p;
            state.Head.Rotation = r;
        }
        if (state.Left.Tracked)
        {
            _anchor.ToWorld(state.Left.Pose.Position, state.Left.Pose.Rotation, out Vector3 p, out Quaternion r);
            state.Left.Pose.Position = p;
            state.Left.Pose.Rotation = r;
        }
        if (state.Right.Tracked)
        {
            _anchor.ToWorld(state.Right.Pose.Position, state.Right.Pose.Rotation, out Vector3 p, out Quaternion r);
            state.Right.Pose.Position = p;
            state.Right.Pose.Rotation = r;
        }
        if (state.HasHeldFigure)
        {
            _anchor.ToWorld(state.HeldFigurePose.Position, state.HeldFigurePose.Rotation, out Vector3 p, out Quaternion r);
            state.HeldFigurePose.Position = p;
            state.HeldFigurePose.Rotation = r;
        }
        if (state.HasHeldCard)
        {
            _anchor.ToWorld(state.HeldCardPose.Position, state.HeldCardPose.Rotation, out Vector3 p, out Quaternion r);
            state.HeldCardPose.Position = p;
            state.HeldCardPose.Rotation = r;
        }
    }

    private void ExtrasToWorld(ref PresenceState p)
    {
        if (p.HasBoard)
        {
            _anchor.ToWorld(p.Board.Position, p.Board.Rotation, out Vector3 wp, out Quaternion wr);
            p.Board.Position = wp;
            p.Board.Rotation = wr;
        }
        // The second held figure's pose is a SHARED-FRAME pose exactly like the rig packet's first
        // figure, so it converts here for the same reason: NetFigures and RemoteAvatar are world-only.
        if (p.HasSecondFigure)
        {
            _anchor.ToWorld(p.SecondFigurePose.Position, p.SecondFigurePose.Rotation,
                            out Vector3 fp, out Quaternion fr);
            p.SecondFigurePose.Position = fp;
            p.SecondFigurePose.Rotation = fr;
        }
        // The second held card's pose is a SHARED-FRAME pose exactly like the rig packet's held
        // card, so it converts here for the same reason: RemoteAvatar is world-only.
        if (p.HasSecondHeldCard)
        {
            _anchor.ToWorld(p.SecondHeldCardPose.Position, p.SecondHeldCardPose.Rotation,
                            out Vector3 cp, out Quaternion cr);
            p.SecondHeldCardPose.Position = cp;
            p.SecondHeldCardPose.Rotation = cr;
        }
    }
}
