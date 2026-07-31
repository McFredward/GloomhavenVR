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

    private void OnEnable()
    {
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
        }
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
                              + $"{NetProtocol.ExtrasSendRateHz:0} Hz to all peers.");
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
        if (!VRSession.IsRunning || !_transport.IsOnline || _transport.LocalPlayerId <= 0)
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

        if (_extrasAccumulator < interval && !fxPending && !countsChanged && !browseChanged
            && !maskSizeChanged && !boardStyleChanged && !handScaleChanged)
            return;
        _extrasAccumulator = 0f;
        _lastSentHandCount = handNow;
        _lastSentItemCount = itemsCount;

        var extras = default(PresenceState);

        Transform? board = PlayTray.Current?.Root;
        if (board != null)
        {
            _anchor.ToAnchor(board.position, board.rotation, out Vector3 bp, out Quaternion br);
            extras.HasBoard = true;
            extras.Board.Position = bp;
            extras.Board.Rotation = br;
            float scale = board.lossyScale.x;
            extras.BoardScale = scale > 0f ? scale : 1f;
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
                    // Convert the shared-frame poses to world here so RemoteAvatar stays world-only.
                    ToWorld(ref state);
                    _pending[senderId] = state; // dedup: keep only the newest
                }
                break;

            case NetProtocol.MsgExtras:
                if (PresenceSerializer.TryRead(buffer, length, out PresenceState extras))
                {
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

                    // Cosmetic figure sync: mirror the sender's held figure (no-op stub in foundation).
                    if (s.HasHeldFigure)
                        NetFigures.ApplyRemoteHeld(kv.Key, s.HeldFigureActorId, s.HeldFigurePose.Position, s.HeldFigurePose.Rotation);
                    else
                        NetFigures.ReleaseRemote(kv.Key);
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
        }
    }

    private void DestroyAllAvatars()
    {
        foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
        {
            kv.Value.Destroy();
            NetFigures.ReleaseRemote(kv.Key);
        }
        _avatars.Clear();
    }

    private void OnDestroy() => DestroyAllAvatars();

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
    }
}
