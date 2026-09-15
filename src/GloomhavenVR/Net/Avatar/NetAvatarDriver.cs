using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
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
internal sealed partial class NetAvatarDriver : MonoBehaviour
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
    private readonly byte[] _animationBuffer = new byte[UseBarAnimationCodec.MaxSize];
    private UseBarAnimationState[]? _lastSentAnimations;
    private UseBarAnimationSnapshot? _lastAnimationSnapshot;
    private float _nextAnimationRefresh;
    private float _lastAnimationSourceFrameTime;
    private readonly Dictionary<int, List<UseBarAnimationSnapshot>> _pendingAnimations = new();

    /// <summary>Called after the owner's native animation and dock placement in LateUpdate.</summary>
    internal static void PublishUseBarAnimations(UseBarAnimationState[]? states, NativeUseBarState?[] native)
    {
        NetAvatarDriver? driver = _instance;
        if (driver == null || !driver.isActiveAndEnabled) return;
        try { driver.TickAnimationSend(states); }
        catch (Exception e) { driver.LogPhaseError("TickAnimationSend", e); }
        try { driver.TickNativePresentationSend(native); }
        catch (Exception e) { driver.LogPhaseError("TickNativePresentationSend", e); }
        if (!NetSession.FlatNetMode && VRSession.IsRunning && driver._transport.IsOnline
            && driver._transport.LocalPlayerId > 0 && driver._transport is FfsNetTransport ffs)
            ffs.TickFragments(Time.unscaledTime);
    }

    private void TickAnimationSend(UseBarAnimationState[]? states)
    {
        if (NetSession.FlatNetMode || !VRSession.IsRunning || !_transport.IsOnline
            || _transport.LocalPlayerId <= 0) return;
        using var timing = PerfMonitor.Scope("Net.Presentation.BonusSend");
        float now = Time.unscaledTime;
        bool changed = !ReferenceEquals(states, _lastSentAnimations);
        if (changed || _lastAnimationSnapshot == null)
        {
            // A changed-only stream needs the actual held frame just before motion resumes.
            // Otherwise a receiver could interpolate the new value across seconds of idle time.
            if (_lastAnimationSnapshot != null && now - _lastAnimationSnapshot.SampleTime > 0.25f
                && _lastAnimationSourceFrameTime > _lastAnimationSnapshot.SampleTime)
            {
                var predecessor = new UseBarAnimationSnapshot(_lastAnimationSourceFrameTime,
                    _lastAnimationSnapshot.States);
                int previousLength = UseBarAnimationCodec.Write(predecessor, _animationBuffer);
                _transport.Send(_animationBuffer, previousLength, predecessor);
            }
            _lastAnimationSnapshot = new UseBarAnimationSnapshot(now,
                states ?? Array.Empty<UseBarAnimationState>());
            _lastSentAnimations = states;
        }
        // Repeat the final sample for loss recovery and late joiners, retaining its source time
        // so a duplicate can never restart interpolation on the receiving original widget.
        if (changed || now >= _nextAnimationRefresh)
        {
            int length = UseBarAnimationCodec.Write(_lastAnimationSnapshot, _animationBuffer);
            _transport.Send(_animationBuffer, length, _lastAnimationSnapshot);
            _nextAnimationRefresh = now + 0.5f;
        }
        _lastAnimationSourceFrameTime = now;
    }


    // Sticky last card-FX event (report 6): re-sent on every extras packet for redundancy on the
    // unreliable side-channel. The receiver de-dupes on the sequence byte, so repeats cost 2 bytes
    // and never play twice.
    private byte _lastFxEndpoints;
    private byte _lastFxSeq;
    private byte _lastFxVisibilityFlags;
    private CardFlightSource? _lastFxSource;
    private NativeDecisionHighlightState? _lastSentDecisionHighlight, _decisionHighlightSnapshot;
    private DamageDecisionPreviewState? _lastSentDamageDecisionPreview;
    private int _lastSentDecisionActor;
    private bool _lastSentDecisionPending, _lastSentDecisionVisible;
    private bool _hasFx;

    // Last broadcast fan sizes — an on-change extras send keeps a peer's fan appearing/disappearing
    // with the gesture instead of up to one 5 Hz interval later (see TickExtrasSend).
    private int _lastSentHandCount = -1;

    /// <summary>Persistent sample buffer for extension record 44 (the fan arc order) — sized to
    /// the record's own seat cap so the sampler can never be asked to write past it.</summary>
    private readonly int[] _fanArcOrderBuf = new int[NetProtocol.FanArcOrderMaxSeats];
    private readonly FanPresentationSendState _fanPresentationSent = new();
    private int _lastSentItemCount = -1;

    // ITEM-USE CLIP (extension record 26): the last broadcast fan index of the chip lying in our own
    // item-USE recess, -1 = the recess is empty. Placing a card there and taking it back out are
    // both discrete, HUMAN-PACED edges that the receiver ANIMATES from (it replays the 0.28 s settle
    // on the frame the index appears — see NetProtocol.ExtIdItemUseClip), so both pre-empt the 5 Hz
    // gate exactly like the fan counts and the card-FX events do. One byte on a gesture nobody makes
    // twice a second; it cannot become a stream.
    private int _lastSentItemClip = -1;

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

    // HALF SELECTION (record 14 byte 1): the last broadcast per-slot selection nibble
    // (sel0 | sel1<<2), so a CLICK — and its undo — pre-empts the 5 Hz gate OUTRIGHT (the
    // pile-counts rule: a click is discrete and human-paced, it can never become a stream;
    // the steady highlight must land with the click, not up to 200 ms later).
    // int.MinValue = never sent.
    private int _lastSentHalfSelect = int.MinValue;

    // ROUND-CARD SLOT ORDER (record 18): the last broadcast order (-1 not knowable, 0 the
    // initiative card is in the LEFT recess, 1 swapped). A card landing in a recess is one
    // discrete, human-paced event, so an order CHANGE pre-empts the 5 Hz gate OUTRIGHT — the
    // pile-counts rule — and no peer's mirrored pair sits reversed for a visible moment.
    // int.MinValue = never sent.
    private int _lastSentSlotOrder = int.MinValue;

    // EMPTY-FAN PLACARD (record 14, byte 1 bit 4): whether our own "Keine Handkarten" plate was
    // up in the last packet. A placard is a discrete, human-paced EDGE (a palm gate opening), so
    // it pre-empts the 5 Hz gate OUTRIGHT — the pile-counts rule — and the plate lands on every
    // peer's screen with the gesture instead of up to 200 ms after a 1.5 s animation started.
    private bool _lastSentEmptyFanHint;

    /// <summary>Persistent scratch the COMPLETE tuning field list is sampled into (see
    /// <see cref="BoardTuningSampler.Sample"/>). One allocation for the process, sized from the
    /// field-id space rather than from any packet budget.</summary>
    private readonly byte[] _tuningBuffer = new byte[BoardTuningSampler.MaxPayloadBytes];

    /// <summary>
    /// BOARD TUNING (extension record 28): the pager that splits that field list into pages and
    /// hands out ONE per extras packet, cycling forever (see <see cref="BoardTunePageSender"/>).
    ///
    /// <para>It also IS the change detector — <c>Update</c> byte-compares against the list it holds
    /// and returns true only on a real change — which is why the old <c>_lastSentTuningKey</c> hash
    /// is gone: keeping a separate key would have let the log and the cycle-restart disagree about
    /// when the tuning changed, and the cycle restart is what makes the convergence bound measurable
    /// from the drag rather than from wherever the cursor happened to be.</para>
    /// </summary>
    private readonly BoardTunePageSender _tuningPager = new BoardTunePageSender();

    /// <summary>Persistent scratch ONE record-28 page is built into. 255 bytes is the extension
    /// tail's per-record ceiling and therefore a page's maximum by definition — the one place that
    /// number still legitimately appears on this path.</summary>
    private readonly byte[] _tuningPageBuffer = new byte[255];

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

    /// <summary>Last sent CHARACTER FOCUS (extension record 22): the stable id of the character we
    /// are looking at, the stable id of the character the game is WAITING ON (0 = not one of ours)
    /// and whether we own that character. int.MinValue = never sent, so the first real focus of a
    /// session is always an edge.</summary>
    private int _lastSentFocusActor = int.MinValue;
    private int _lastSentFocusAttentionActor;
    private bool _lastSentFocusOwnsAttention;

    /// <summary>TRACK SELECTION (extension record 23): the ids of the entries our OWN initiative
    /// track is framing with vanilla's selection frame, sampled straight off the live widget. A
    /// selection change is DISCRETE and human-paced (a portrait click, a turn hand-off, the
    /// round-start auto-select), so like the half-SELECTION latch it pre-empts the 5 Hz gate
    /// OUTRIGHT rather than at the capped rig interval — a frame that lands on a peer's mirrored
    /// track 200 ms late reads as "not synced". -1 = never sent, so the first sample of a session
    /// is always an edge.</summary>
    private readonly int[] _trackSelectionSample = new int[NetProtocol.TrackSelectionMaxIds];
    private readonly int[] _lastSentTrackSelection = new int[NetProtocol.TrackSelectionMaxIds];
    private int _lastSentTrackSelectionCount = -1;

    /// <summary>TRACK ORDER (extension record 27): the on-screen order of our OWN track's PLAYER
    /// entries plus which of them we control. The order only changes when vanilla re-sorts the
    /// track — a per-round event, not a pointer event — and the owned mask only when the host
    /// hands out characters, so a plain change edge on the 5 Hz extras cadence is enough and no
    /// pre-emption is warranted (unlike the hover and the selection frame, which follow a click).
    /// -1 = never sent, so the first sample of a session is always an edge.</summary>
    private readonly int[] _trackOrderSample = new int[NetProtocol.TrackOrderMaxIds];
    private readonly int[] _lastSentTrackOrder = new int[NetProtocol.TrackOrderMaxIds];
    private int _lastSentTrackOrderCount = -1;
    private byte _lastSentTrackOrderOwned;

    /// <summary>Comma-joined id list for a change-gated log line (never per frame — only on the
    /// edge that already decided to log). Kept tiny and allocation-honest: the caller logs at most
    /// a handful of ids and only when the state really moved.</summary>
    private static string DescribeIds(int[] ids, int count)
    {
        if (ids == null || count <= 0)
            return string.Empty;
        var sb = new System.Text.StringBuilder(count * 12);
        for (int i = 0; i < count && i < ids.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(ids[i]);
        }
        return sb.ToString();
    }

    /// <summary>Which use bar a record-25 BAR INDEX names, for the change-gated log line only —
    /// the wire carries the bit, never a name (see <see cref="NetProtocol.ExtIdUseBars"/>).</summary>
    private static string UseBarName(int bar) => bar switch
    {
        0 => "activeBonus",
        1 => "abilities",
        2 => "augments",
        _ => "items",
    };

    // WALL FADES (extension record 17, MP wall-fade sync): the set of walls the LOCAL fade
    // decision currently hides, as sorted cross-machine keys. A set CHANGE is an edge with
    // the capped pre-emption (fade flips are dwell-paced — a few per minute, never a
    // stream). ALWAYS broadcast regardless of the local [WallFade] SyncPeerFades toggle:
    // bytes are cheap and the RECEIVER's setting decides application, so one player
    // toggling mid-session needs no renegotiation (documented on the ExtId const).
    private readonly uint[] _wallFadeSample = new uint[NetProtocol.WallFadesMaxKeys];
    private readonly uint[] _lastSentWallFades = new uint[NetProtocol.WallFadesMaxKeys];
    private int _lastSentWallFadeCount = -1;
    /// <summary>Last decision-button lines put on the wire (extension record 12; null = no row
    /// docked). Dock, undock and a re-label are EDGES that pre-empt the 5 Hz gate — a decision
    /// row that appears on the peer's copy 200 ms after the owner's reads as "not synced", the
    /// board-UI-edge argument. Human-paced (a prompt opening), never a stream.</summary>
    private string? _lastSentDecisionLines;

    /// <summary>Last decision DISPLAY STATE put on the wire (extension record 24,
    /// <see cref="NetProtocol.ExtIdDecisionState"/>), packed as
    /// <c>flags | count &lt;&lt; 8 | option bytes &lt;&lt; 16…</c> for the change test alone;
    /// −1 = no record was written. The option states change on a CLICK (a toggle flips, the game
    /// re-asserts a gate), which is exactly the human-paced edge the decision lines already
    /// pre-empt the 5 Hz gate for — a mirrored plate that lights 200 ms after the owner's reads as
    /// "not synced".</summary>
    private long _lastSentDecisionState = -1;

    /// <summary>Sample buffer for the per-option state bytes (extension record 24) — a persistent
    /// array handed to the serializer with a live count, the <see cref="_wallFadeSample"/>
    /// pattern, so the 5 Hz path allocates nothing while a prompt is docked.</summary>
    private readonly byte[] _decisionOptionSample = new byte[NetProtocol.DecisionStateMaxOptions];

    /// <summary>Last decision WIDGET IDENTITY put on the wire (extension record 29), packed as
    /// <c>flags | damage &lt;&lt; 8 | count &lt;&lt; 16 | role codes &lt;&lt; 24…</c> for the change
    /// test alone; −1 = no record was written. It moves on exactly the edges record 24 moves on
    /// (a shield toggle changes the damage number the owner reads on the button), so it shares
    /// their pre-emption of the 5 Hz gate.</summary>
    private long _lastSentDecisionWidgets = -1;

    /// <summary>Sample buffer for the per-option ROLE codes (extension record 29) — the
    /// <see cref="_decisionOptionSample"/> pattern, index-aligned with it by construction (one
    /// sampler walk in DecisionDockSurface fills both).</summary>
    private readonly byte[] _decisionRoleSample = new byte[NetProtocol.DecisionStateMaxOptions];

    /// <summary>Sample buffers for the USE-BAR drawer (extension record 25) — persistent arrays
    /// handed to the serializer with a live mask, the <see cref="_wallFadeSample"/> pattern, so the
    /// 5 Hz path allocates nothing while bars are docked. Flags/counts are per BAR INDEX; the
    /// states are flat with a fixed <c>UseBarsMaxSlots</c> stride per bar.</summary>
    private readonly byte[] _useBarFlagsSample = new byte[NetProtocol.UseBarsCount];
    private readonly byte[] _useBarCountSample = new byte[NetProtocol.UseBarsCount];
    private readonly byte[] _useBarSlotSample =
        new byte[NetProtocol.UseBarsCount * NetProtocol.UseBarsMaxSlots];

    /// <summary>Per-slot identity ids for record 45, same stride and same subscripts as
    /// <see cref="_useBarSlotSample"/>, so a slot's state and its id are one index apart from
    /// nothing.</summary>
    private readonly ushort[] _useBarIdSample =
        new ushort[NetProtocol.UseBarsCount * NetProtocol.UseBarsMaxSlots];

    /// <summary>Owner scratch for <see cref="AllVisibleUseBarsAreForeign"/> (one call per packet,
    /// single-threaded).</summary>
    private static readonly System.Collections.Generic.List<CActor> UseBarOwnerScratch = new(4);

    /// <summary>
    /// True when EVERY bar in <paramref name="mask"/> belongs exclusively to characters under
    /// another player's control — see the long note at the call site for the boots defect this
    /// guards and for why withholding such a bar cannot deadlock anyone.
    ///
    /// <para>Read straight off the game's own bar singletons (the same fields
    /// <c>UseBarsSurface</c>'s own owner resolvers read) rather than through that surface, because
    /// this half of the fix lives on the NET side of the file boundary. A summon maps to its
    /// <c>Summoner</c>, the same mapping every other owner test in this mod uses. FAIL-OPEN: a bar
    /// whose owner cannot be resolved at all counts as NOT foreign, so an unknown future bar keeps
    /// riding exactly as it does today.</para>
    /// </summary>
    private static bool AllVisibleUseBarsAreForeign(byte mask)
    {
        try
        {
            bool anyOwnerSeen = false;
            for (int b = 0; b < NetProtocol.UseBarsCount; b++)
            {
                if ((mask & (1 << b)) == 0)
                    continue;
                UseBarOwnerScratch.Clear();
                switch (b)
                {
                    case 0 when Singleton<UIActiveBonusBar>.IsInitialized:
                    {
                        System.Collections.Generic.List<CActor>? actors =
                            Singleton<UIActiveBonusBar>.Instance.actors;
                        if (actors != null)
                        {
                            for (int i = 0; i < actors.Count; i++)
                                UseBarOwnerScratch.Add(actors[i]);
                        }
                        break;
                    }
                    case 1 when Singleton<UIUseAbilitiesBar>.IsInitialized:
                        UseBarOwnerScratch.Add(Singleton<UIUseAbilitiesBar>.Instance.actor);
                        break;
                    case 2 when Singleton<UIUseAugmentationsBar>.IsInitialized:
                        UseBarOwnerScratch.Add(Singleton<UIUseAugmentationsBar>.Instance.actor);
                        break;
                    case 3 when Singleton<UIUseItemsBar>.IsInitialized:
                        UseBarOwnerScratch.Add(Singleton<UIUseItemsBar>.Instance.actor);
                        break;
                }
                for (int i = 0; i < UseBarOwnerScratch.Count; i++)
                {
                    CPlayerActor? owner = UseBarOwnerScratch[i] switch
                    {
                        CPlayerActor player => player,
                        CHeroSummonActor summon => summon.Summoner,
                        _ => null,
                    };
                    if (owner == null)
                        continue;
                    anyOwnerSeen = true;
                    if (!Board.CharacterFocus.IsForeign(owner))
                        return false; // one bar this player can actually answer ⇒ publish everything
                }
            }
            return anyOwnerSeen;
        }
        catch (System.Exception)
        {
            return false; // attribution is presentation: never withhold on a half-torn model
        }
    }

    /// <summary>The USE-BAR drawer last put on the wire (extension record 25): the mask (−1 = no
    /// record was written yet) plus a byte-for-byte copy of what went out. A slot toggling on, a
    /// sub-picker opening and a bar appearing/vanishing are all human-paced EDGES that pre-empt the
    /// 5 Hz gate — a drawer that lands on the peer's copy 200 ms late reads as "not synced", the
    /// same board-UI-edge argument the decision records make. Compared BYTE-EXACT rather than
    /// through a packed fingerprint: 40 bytes is cheaper than reasoning about hash collisions on a
    /// change gate that decides whether a peer sees a click at all.</summary>
    private int _lastSentUseBarMask = -1;
    private readonly byte[] _lastSentUseBarFlags = new byte[NetProtocol.UseBarsCount];
    private readonly byte[] _lastSentUseBarCounts = new byte[NetProtocol.UseBarsCount];
    private readonly byte[] _lastSentUseBarSlots =
        new byte[NetProtocol.UseBarsCount * NetProtocol.UseBarsMaxSlots];
    private readonly ushort[] _lastSentUseBarIds =
        new ushort[NetProtocol.UseBarsCount * NetProtocol.UseBarsMaxSlots];

    /// <summary>Last CONFIRM cap label put on the wire (extension record 13 bit 0; null = no
    /// confirm control shown). Same edge pre-emption as the decision lines.</summary>
    private string? _lastSentConfirmLabel;

    /// <summary>Last SKIP label put on the wire (extension record 13 bit 1; null = skip not
    /// shown on the board). Same edge pre-emption as the decision lines.</summary>
    private string? _lastSentSkipLabel;

    /// <summary>Last UNDO cap label put on the wire (extension record 13 bit 2; null = no undo
    /// control shown). Same edge pre-emption as its two neighbours.</summary>
    private string? _lastSentUndoLabel;

    /// <summary>Last item-USE cap label put on the wire (extension record 13 bit 3; null = the cap
    /// is not up). Same edge pre-emption.</summary>
    private string? _lastSentItemUseLabel;

    /// <summary>Board-UI cap-state byte, CONFIRM cap, for the change-gated diagnostic. Confirmed
    /// beats accent exactly as <c>BoardButton.StateColor</c> resolves them.</summary>
    private static string DescribeConfirmCapState(int boardUi) =>
        ((boardUi >> 16) & NetProtocol.BoardUiCapConfirmReadyBit) != 0 ? "CONFIRMED"
        : ((boardUi >> 16) & NetProtocol.BoardUiCapConfirmAccentBit) != 0 ? "accent"
        : "idle";

    /// <summary>Board-UI cap-state byte, one rest disc, for the change-gated diagnostic.</summary>
    private static string DescribeRestCapState(int boardUi, byte enabledBit, byte accentBit) =>
        (((boardUi >> 16) & enabledBit) != 0 ? "enabled" : "DIMMED")
        + (((boardUi >> 16) & accentBit) != 0 ? "+accent" : string.Empty);

    /// <summary>Last cap PRESS put on the wire (record 14 byte 0 bits 3..7, packed
    /// <c>cap | seq &lt;&lt; 8</c>; -1 = none in flight). A NEW value pre-empts the 5 Hz gate
    /// OUTRIGHT — the mirrored dip has to land with the click — while the rest of the hold window
    /// only rides packets that were going out anyway.</summary>
    private int _lastSentCapPress = -1;

    // SLOT-CARD SIZE (extension record 11, user report "Die Kartengröße am fremden Board stimmt
    // nicht 1:1"): the last broadcast (frameCode | cardCode << 16), so a live config edit (the
    // debug menu's per-board sliders) is an EDGE that pre-empts the 5 Hz gate and the confirmation
    // log fires once per change. int.MinValue = never sent — the first packet with a live tray
    // always states the sizes (or their deliberate omission) explicitly.
    private int _lastSentSlotCardSize = int.MinValue;

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

    // HELD-FIGURE STRETCH (extension record 30) — the last QUANTIZED codes sent, compared instead
    // of the raw floats so the change test asks about the very bytes that would go on the wire
    // (a sub-milli wobble that quantizes identically is not a change). Neutral = record omitted.
    private int _lastSentStretchPrimary = NetProtocol.HeldStretchCodeNeutral;
    private int _lastSentStretchSecondary = NetProtocol.HeldStretchCodeNeutral;
    private bool _loggedStretch;
    private bool _loggedSecondFigure;

    // HELD PROPS (record 37) — the send-side change detector, one entry per wire slot. Grab/release/
    // swap are EDGES and pre-empt the cadence; carrying is a MOTION and rides the fast interval,
    // exactly like the second figure beside it. "…Valid false" = nothing sent yet this session.
    private bool _sentHeldPropValid;
    private bool _sentSecondPropValid;
    private int _lastSentHeldPropId;
    private int _lastSentSecondPropId;
    private Vector3 _lastSentHeldPropPos;
    private Quaternion _lastSentHeldPropRot = Quaternion.identity;
    private Vector3 _lastSentSecondPropPos;
    private Quaternion _lastSentSecondPropRot = Quaternion.identity;
    private int _lastSentHeldPropSize = NetProtocol.HeldStretchCodeNeutral;
    private int _lastSentSecondPropSize = NetProtocol.HeldStretchCodeNeutral;
    private bool _loggedHeldProp;

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
    // Extension record 34: the held-card grip mask most recently put on the wire. 0 = both held
    // cards billboard, which is also the pre-session value, so the first packet of a session that
    // never uses the gesture is unchanged.
    private byte _lastSentCardGripMask;

    /// <summary>Last per-item usable mask put on the wire (record 35), so the edge log fires on a
    /// CHANGE rather than on every extras packet. 0 is both the initial value and "nothing is
    /// playable", which is the state the record is omitted in.</summary>
    private ushort _lastSentUsableMask;

    /// <summary>Last held-card FACE codes put on the wire (record 36), one per POSE SLOT, for the
    /// same change-gating reason. The COUNT bytes deliberately take no part in the edge: the list a
    /// held card sits in can grow or shrink under it while the same card stays held, and an edge on
    /// that would turn a physical grab into a stream of log lines.</summary>
    private byte _lastSentFaceCode0;
    private byte _lastSentFaceCode1;
    private byte _lastSentFaceCount0, _lastSentFaceCount1;
    private int _lastSentSecondFaceActor;
    private uint _lastSentSecondMapKey;
    private byte _lastSentSecondMapArcSeat;
    private ushort _lastSentSecondMapSeat, _lastSentSecondMapCount;

    /// <summary>Last SACRIFICE SEAT codes put on the wire (record 39), one per ROUND RECESS. Same
    /// change-gating rule and same exclusion of the COUNT bytes as the held-card codes above: the
    /// owner's discard pile can grow under a sacrifice that is still lying in the same recess (a
    /// teammate's card landing there does not move it), and an edge on the length would turn one
    /// short rest into a stream of lines. A REDRAW changes the code, so it does print — which is
    /// the one mid-rest event a hardware log has to be able to see.
    ///
    /// <para>INITIALISED TO AN IMPOSSIBLE VALUE, LIKE <see cref="_lastSentSpentMask"/> BESIDE IT,
    /// AND THAT IS A BUG FIX. These were <c>byte</c>s starting at 0 — the SAME value the sampler
    /// writes when it names nothing — so a sampler that never once succeeded never crossed an edge
    /// and printed NOTHING AT ALL. The 2026-09-06 session contains a complete short rest and zero
    /// <c>SHORT REST SEAT</c> lines in either log, which read as "no short rest happened" and was
    /// really "the instrument goes silent exactly when the thing it measures fails". A first line
    /// is now unconditional and it carries the sampler's own REASON, so "wrote nothing" is a
    /// reading instead of a silence.</para></summary>
    private UseBarWidgetState[]? _lastSentUseBarWidgets;
    private bool _lastSentShortRest;
    private int _lastSentSeatCode0 = -1;
    private int _lastSentSeatCode1 = -1;

    /// <summary>Change edge for the SPENT-HALF record (41). Initialised to an impossible mask so
    /// the first sample of a session always prints, including the all-zero one — "nothing of mine
    /// is dimmed" is the reading that separates "the sampler ran and saw nothing" from "the sampler
    /// never ran", and those are different defects.</summary>
    private int _lastSentSpentMask = -1;

    /// <summary>Change gate for the FAN SOURCE line (record 43). -1 so the first sample prints
    /// whatever it is, the ordinary "my fan is my hand" reading included — a first line that states
    /// the resting value is what makes a later silence readable as "nothing changed" rather than as
    /// "the instrument never ran".</summary>
    private int _lastSentFanSource = -1;

    /// <summary>Popcount of a 16-bit mask — how many items record 35 is framing. Used by the edge
    /// log only; the wire carries the mask itself.</summary>
    private static int CountBits(ushort mask)
    {
        int n = 0;
        while (mask != 0)
        {
            mask &= (ushort)(mask - 1);
            n++;
        }
        return n;
    }

    /// <summary>One held-card FACE slot as a log phrase — the list it names, the seat in it and the
    /// length of that list. Names a POSITION, never a card, exactly like the wire byte it
    /// describes.</summary>
    private static string DescribeHeldFace(byte code, byte count)
    {
        byte list = NetProtocol.HeldFaceList(code);
        if (list == NetProtocol.HeldFaceListNone)
            return "nothing";
        string where = list switch
        {
            NetProtocol.HeldFaceListHand => "hand fan",
            NetProtocol.HeldFaceListDiscard => "discard pile",
            NetProtocol.HeldFaceListBurnt => "burnt pile",
            NetProtocol.HeldFaceListItems => "items (AllItems raw index)",
            NetProtocol.HeldFaceListMapLoadout => "map-room loadout",
            NetProtocol.HeldFaceListActive => "ACTIVE pile (public in every phase)",
            _ => "list " + list,
        };
        byte at = NetProtocol.HeldFaceIndex(code);
        return at == NetProtocol.HeldFaceIndexUnknown
            ? $"{where}, seat UNKNOWN of {count}"
            : $"{where}, seat {at} of {count}";
    }
    private bool _loggedCardGrip;

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
    /// read seam for <see cref="CollectPeerHeads"/> and <see cref="TryGetPeerRigScale"/> — nothing
    /// writes through it.
    /// </summary>
    private static NetAvatarDriver? _instance;

    /// <summary>
    /// A peer's LIVE rig scale — their diorama zoom — or false when that peer has no avatar yet.
    ///
    /// <para>Same shape and same argument as <see cref="CollectPeerHeads"/>: strictly read-only,
    /// strictly local, and NO NEW WIRE. The number is already here — <c>Rig.LocalRigSampler</c>
    /// samples <c>rigRoot.lossyScale.x</c> into every rig packet and it lands as
    /// <see cref="RemoteAvatar.AppliedScale"/> at rig-packet rate, i.e. in lockstep with the pose
    /// the held figure is eased toward.</para>
    ///
    /// <para>Its consumer is <c>Net.NetFigures.EaseSlot</c> (2026-08-11). A held mini now keeps the
    /// size it had at the MOMENT OF THE GRAB instead of following its holder's zoom — that is the
    /// user's requirement, "die Größe soll nur abhängig sein wann sie greift und dann fix in der
    /// Hand sein". The receiver must reproduce the same ratio or the two machines disagree for as
    /// long as the holder zooms mid-hold, which the 1:1 ruling forbids. Both numbers the ratio needs
    /// are already on this side, so the parity costs zero bytes.</para>
    /// </summary>
    internal static bool TryGetPeerRigScale(int playerId, out float scale)
    {
        scale = 1f;
        NetAvatarDriver? driver = _instance;
        if (driver == null || !driver._avatars.TryGetValue(playerId, out RemoteAvatar avatar) || avatar == null)
            return false;
        scale = avatar.AppliedScale;
        return scale > 0f;
    }

    /// <summary>
    /// A peer's own <c>[WorldUI] WindowLegibility</c> — how large THEY draw a floated window — or
    /// false when that peer has no avatar yet (joining, not embodied).
    ///
    /// <para>Same shape and same argument as <see cref="TryGetPeerRigScale"/>: strictly read-only,
    /// strictly local, and no new wire. The number already rides extension record 28 as
    /// <see cref="NetProtocol.TuneWindowLegibility"/> (id 180) and lands re-clamped to
    /// <c>ModalFallback.WindowLegibilityMin</c>/<c>Max</c> on
    /// <see cref="RemoteAvatar.BoardTuning"/>; this only hands it to a caller that has a PLAYER ID
    /// and no avatar reference.</para>
    ///
    /// <para>Its consumer is <c>Net.RemoteMapRoom.Placards</c> (2026-08-28). A peer's map-room hover
    /// placard is now sized from the OWNER's dial rather than the viewer's — user ruling, verbatim:
    /// <i>"Auch hier soll die 1:1 Regel gelten, also die Größe des Besitzers."</i> That class is
    /// static and keyed by player id, so without this accessor the only route to the owner's tuning
    /// would be a scene sweep or a second copy of the avatar table; the alternative both siblings
    /// above name — <c>GameObject.Find("GloomhavenVR.RemoteAvatar[id]")</c> — is the one this
    /// project has already lost a frame budget to.</para>
    ///
    /// <para>ALWAYS A USABLE NUMBER WHEN IT ANSWERS TRUE, INCLUDING BEFORE RECORD 28 ARRIVES: the
    /// <see cref="RemoteAvatar"/> constructor seeds <c>BoardTuning</c> from an EMPTY payload, so
    /// every dial the peer has not sent resolves to this client's shipped default. A caller
    /// therefore needs no "has it arrived yet" state of its own — it reads per frame and the value
    /// corrects itself the frame after the record lands.</para>
    /// </summary>
    internal static bool TryGetPeerWindowLegibility(int playerId, out float legibility)
    {
        legibility = Defaults.WindowLegibility;
        NetAvatarDriver? driver = _instance;
        if (driver == null || !driver._avatars.TryGetValue(playerId, out RemoteAvatar avatar) || avatar == null)
            return false;
        legibility = avatar.BoardTuning.WindowLegibility;
        return legibility > 0f;
    }

    /// <summary>
    /// The owner's own <c>[WorldUI] CanvasScaleMm</c>, off record 28, for a surface that has to
    /// size a peer's content the way that peer sizes it.
    ///
    /// <para>WHY THIS EXISTS. A mirror must never read the viewer's dial, and
    /// <c>RemoteMapRoom</c> was doing exactly that: it multiplied THIS client's
    /// <c>CanvasScaleMm</c> by the OWNER's scale factor, under a comment saying the factor was the
    /// owner's — which was true of the factor and false of the product. The owner's millimetres
    /// have ridden record 28 since ModBuild 306; there was simply no accessor, so the nearest
    /// available number got used. Same shape as <see cref="TryGetPeerWindowLegibility"/> directly
    /// above, deliberately, so the two cannot drift.</para>
    ///
    /// <para>Returns false and leaves <paramref name="mm"/> at the shipped default when the peer is
    /// unknown or has not published a tuning block yet. A caller that gets false is sizing against
    /// the default, NOT against the viewer's dial — that is the fail direction 1:1 wants, because a
    /// wrong shared number is recoverable and a viewer-local one is a permanent disagreement.</para>
    /// </summary>
    internal static bool TryGetPeerCanvasScaleMm(int playerId, out float mm)
    {
        mm = Defaults.CanvasScaleMm;
        NetAvatarDriver? driver = _instance;
        if (driver == null || !driver._avatars.TryGetValue(playerId, out RemoteAvatar avatar) || avatar == null)
            return false;
        mm = avatar.BoardTuning.CanvasScaleMm;
        return mm > 0f;
    }

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

    /// <summary>
    /// ONE peer's HEAD HOLDER transform, by player id -- the node the mask hangs off, live rather
    /// than a sampled position.
    ///
    /// <para><see cref="TryGetPeerHead"/>'s sibling, and it exists because that one answers with a
    /// POSITION and the consumer needs the NODE. Spatial voice chat (<c>Voice/VoiceSpatial.cs</c>,
    /// ModBuild 297) anchors a peer's voice AudioSource at their mask every frame: a Vector3 read
    /// once would be a snapshot, and re-reading TryGetPeerHead per frame would hand back the last
    /// RECEIVED head target rather than the eased holder pose the mask is actually drawn at -- so
    /// the sound would lead or lag the face it comes from by up to one interpolation constant.</para>
    ///
    /// <para>Read-only and strictly local, like its sibling. Answers false for a peer with no
    /// avatar, and for one whose head holder is currently inactive (joining, not embodied, or head
    /// pose invalid); the caller's contract is then to leave that voice NON-spatial rather than
    /// place it somewhere wrong -- see the class doc of <c>Voice/VoiceSpatial.cs</c>, "THE FEATURE
    /// NEVER TRADES AUDIBILITY FOR POSITION".</para>
    ///
    /// <para>The alternative it replaces is the same one <see cref="TryGetPeerHead"/> names:
    /// <c>GameObject.Find("GloomhavenVR.RemoteAvatar[id]")</c>, a whole-scene sweep per tick. Not
    /// again.</para>
    /// </summary>
    internal static bool TryGetPeerHeadHolder(int playerId, out Transform holder)
    {
        holder = null!;
        NetAvatarDriver? driver = _instance;
        if (driver == null || !driver._avatars.TryGetValue(playerId, out RemoteAvatar avatar) || avatar == null)
            return false;
        Transform? h = avatar.HeadHolder;
        if (h == null || !h.gameObject.activeInHierarchy)
            return false;
        holder = h;
        return true;
    }

    /// <summary>
    /// ONE peer's last RECEIVED head world position, by player id.
    ///
    /// <para><see cref="CollectPeerHeads"/>'s sibling, and it exists because that one deliberately
    /// DROPS the id: the spawn ring only needs to know where bodies are, so a bag of positions is
    /// the right shape for it. ModBuild 226 produced the first consumer that needs the OTHER
    /// question — a peer's hover placard on the campaign map is turned to face the player it
    /// belongs to, and that facing is now the only thing that says whose placard it is (the name row
    /// and the Steam picture were removed on the user's ruling: "es soll 1:1 so aussehen wie es für
    /// den Spieler auch aussieht"). Without an id there is no way to ask.</para>
    ///
    /// <para>The alternative it replaces was <c>GameObject.Find("GloomhavenVR.RemoteAvatar[id]")</c>
    /// — a whole-scene sweep, reaching a private naming convention from outside, in a class that has
    /// the object in a dictionary. This project has already lost an entire frame budget to one
    /// scene sweep per tick, so the accessor is worth its four lines. Read-only and strictly local;
    /// a peer with no valid head pose yet (joining, not embodied) answers false, and the caller's
    /// contract is to leave the placard's facing alone rather than aim it somewhere wrong.</para>
    /// </summary>
    internal static bool TryGetPeerHead(int playerId, out Vector3 head)
    {
        head = Vector3.zero;
        NetAvatarDriver? driver = _instance;
        return driver != null
               && driver._avatars.TryGetValue(playerId, out RemoteAvatar avatar)
               && avatar != null
               && avatar.TryGetHeadWorld(out head);
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
        _pendingAnimations.Clear();
        ResetNativePresentation();
        _lastAnimationSnapshot = null;
        _lastSentAnimations = null;
        _nextAnimationRefresh = 0;
        _lastAnimationSourceFrameTime = 0;
        _createRetryAt.Clear();
        _rxRejectLogged.Clear();   // a new session re-reports a peer it cannot parse
        _rxRejected = 0;
        _sendGateState = -1; // next session logs its join window from scratch
        NetCardFx.Reset(); // never carry a queued card animation into the next session
        _hasFx = false;
        _lastSentBoardUi = -1;      // next session re-states the board UI from scratch
        _lastSentPileCounts = int.MinValue;   // and re-states the pile counts…
        _lastSentHalfHover = int.MinValue;    // …the half hover…
        _lastSentHalfSelect = int.MinValue;   // …the clicked halves…
        _lastSentSlotOrder = int.MinValue;    // …the round-card slot order (record 18)…
        _lastSentEmptyFanHint = false;        // …the empty-fan placard (record 14 bit 4)…
        _tuningPager.Reset();                 // …and our own board tuning (record 28)…
        _lastSentTrackHoverActor = int.MinValue; // …and the track hover from scratch
        _lastSentWallFadeCount = -1;             // …and the synced wall-fade set
        _lastSentFocusActor = int.MinValue;       // …and the character focus (record 22)
        _lastSentFocusAttentionActor = 0;         // …including who the game was waiting on
        _lastSentTrackSelectionCount = -1;        // …and the track's selection frames (record 23)
        _lastSentTrackOrderCount = -1;            // …and the track's per-viewer player order (27)
        _lastSentTrackOrderOwned = 0;
        Board.CharacterFocus.Reset();             // …including every peer's synced focus
        _lastSentDecisionLines = null; // next session re-states the docked decision row afresh
        _lastSentDecisionState = -1;   // …including its option states + prompt-text variant
        _lastSentDecisionWidgets = -1; // …and the widget roles + damage numbers (record 29)
        _lastSentUseBarMask = -1;      // …and the use-bar drawer below it (record 25)
        _lastSentConfirmLabel = null;  // and the live cap labels
        _lastSentSkipLabel = null;
        _lastSentUndoLabel = null;
        _lastSentItemUseLabel = null;
        RemoteStorySync.Reset();       // …and never carries a story page/pose into a new session
        RemoteMapRoom.Reset();         // …nor a peer's map-room placard (which owns a GameObject)
        RemoteMapStory.Reset();        // …nor a map story page or a shared window pose
        RemoteVideoPlayback.Reset();  // …nor a cosmetic movie decoder or prior playback identity
        _lastSentCapPress = -1;        // …and never replays a stale keycap press into a new session
        Cards.BoardCapPress.Clear();   // …including the latch it is diffed against
        _fanPresentationSent.Reset();  // new peers need a complete fan presentation sample
        _sentSecondFigureValid = false; // nor a new session's second held figure
        _lastSentSecondActorId = 0;
        _sentSecondCardValid = false;   // nor its second held card
        _lastSentCardGripMask = 0;      // nor which of them was held rigidly (record 34)
        _loggedCardGrip = false;
        _lastSentStretchPrimary = NetProtocol.HeldStretchCodeNeutral;   // nor a stale stretch
        _lastSentStretchSecondary = NetProtocol.HeldStretchCodeNeutral; // (record 30)
        _loggedStretch = false;
        _sentHeldPropValid = false;     // nor a new session's held map items (record 37)
        _sentSecondPropValid = false;
        _lastSentHeldPropId = 0;
        _lastSentSecondPropId = 0;
        _lastSentHeldPropSize = NetProtocol.HeldStretchCodeNeutral;
        _lastSentSecondPropSize = NetProtocol.HeldStretchCodeNeutral;
        _loggedHeldProp = false;
        // …and every prop a peer was carrying goes back on its hex. NOT optional and NOT symmetric
        // with the figure teardown one line up: a released figure is re-authored by the game's own
        // Update, a released prop is re-authored by NOTHING, so a driver teardown mid-hold would
        // strand a chest in mid-air for the rest of the scenario.
        NetProps.Clear();
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
                // The prop mirror eases on the same frame, with the same sharpness, behind its own
                // catch: "mach da keinen Unterschied zwischen Figuren und Props" is a statement
                // about the picture, and identical cadence plus identical easing is the only way to
                // get identical motion.
                try { NetProps.Tick(); }
                catch (Exception e) { LogPhaseError("NetProps.Tick", e); }
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
                _pendingAnimations.Clear();
                ResetNativePresentation();
                DestroyAllAvatars();
                PlayerBadges.RestoreAll();
                // The two world-wide peer tables go with the avatars: nothing of a peer's may
                // outlive the mode that says their packets do not exist. The debug override's Reset
                // deliberately leaves this client's OWN latches standing — they are the tester's.
                _peerEnv.Clear();
                Core.Haunt.ClearHostFrequency("flat-net mode: no peer's environment applies here");
                RemoteTestTriggers.Reset();
                // …and the story table, for the same reason. Leaving nothing standing here also
                // means a flat-net client never drives its own story box from a peer it has
                // stopped believing: it clicks through its own narrative, exactly as before the
                // record existed.
                RemoteStorySync.Reset();
                // …and the two map-room tables, for the same reason. Nothing of a peer's may
                // outlive the mode that says their packets do not exist, and RemoteMapRoom's
                // teardown also destroys every placard GameObject it built.
                RemoteMapRoom.Reset();
                RemoteMapStory.Reset();
                RemoteVideoPlayback.Reset();
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

        // Board and mask are one atomic motion sample. Sending the board only in large
        // fragmented presence snapshots gave it a different delivery clock, even with the
        // same nominal rate and receiver easing. Includes follow, fixed, grabs and scaling;
        // no inferred attachment to the head can distort the owner's actual board pose.
        state.HasBoardPose = true;
        Transform? board = PlayTray.Current?.Root;
        state.HasBoard = board != null;
        if (board != null)
        {
            _anchor.ToAnchor(board.position, board.rotation,
                out state.BoardPose.Position, out state.BoardPose.Rotation);
            state.BoardScale = Mathf.Max(board.lossyScale.x, 1e-4f);
        }
        int len = AvatarSerializer.Write(in state, _sendBuffer);
        _transport.Send(_sendBuffer, len);
    }

    // ---- extras send (board pose + hand count, slower) ----------------------------------

    /// <summary>
    /// Take a covered card's IDENTITY out of one extension-record-13 cap wording before it goes on
    /// the wire — the same mask, the same predicate and the same tick as record 12's.
    ///
    /// <para>WHY BITS 0 AND 2 NEED IT. A confirm/undo cap normally reads a generic "Bestätigen" /
    /// "Rückgängig", but during a modal card pick <c>PlayTray</c> overrides both with the live
    /// <c>DialogPopup</c> option wording, which for a burn prompt NAMES the card. That made record
    /// 13 a second, unnamed and ungated channel for exactly the content record 12 was ruled about,
    /// and a fix that masked only record 12 would have been theatre.</para>
    ///
    /// <para>A REFUSAL BECOMES <c>null</c>, WHICH IS A SAFE PICTURE HERE AND NOT A HOLE: the
    /// receiver renders an absent cap label as its own <c>GUI_CONFIRM</c> ("Confirm"/"Bestätigen",
    /// in the VIEWER's language — <c>RemoteBoardFurniture.SetCapLabels</c>), so the cap still reads
    /// as a button and simply stops quoting the option. Record 12 cannot degrade that way — its
    /// labels are index-aligned with two other records — which is why the two callers handle a
    /// refusal differently.</para>
    /// </summary>
    private static string? MaskCapLabel(string? label)
    {
        if (string.IsNullOrEmpty(label))
            return label;
        string masked = DecisionLabelMask.Apply(label!, out int names);
        return names < 0 ? null : masked;
    }

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
        // FAN ARC ORDER (extension record 44, report item 2 of 2026-09-06 and again of 2026-09-07):
        // the left-to-right order of our OWN arc, read HERE and not anywhere else — on the same call
        // and the same frame as the count above, because a permutation that travels one packet apart
        // from the count it permutes describes a different fan. Returns false (and writes no record)
        // only where it cannot honestly describe the arc, and it says which exit fired: see
        // LocalRigSampler.ReportFanArcOrder, grep token FAN ARC ORDER SENT.
        //
        // IT IS HANDED OUR PLAYER ID AND NOT A HAND, and that IS the 2026-09-07 fix. This call used
        // to pass CardsGameApi.ActiveHand() — CardsHandManager.CurrentHand, the tab the game last
        // switched to, which is NOT the hand CardsDriver builds the arc from (that is
        // CharacterFocus.ResolveHand(DecidingHand() ?? ActiveHand())). Whenever the two differed the
        // sampler derived its indices against another character's widgets, refused, and the fan
        // diverged for every watcher with nothing in either log to say so. The sampler now asks the
        // ARC which hand lists it, and refuses unless that hand belongs to the very actor a watcher
        // resolves for us — NetPlayerActors.ActorFor(this id), the receiver's own expression.
        bool fanArcOrder = LocalRigSampler.SampleFanArcOrder(
            _transport.LocalPlayerId, _fanArcOrderBuf, out int fanArcOrderCount);
        int fanInsertionGap = CardFan.Current?.InsertionGap ?? -1;
        if (fanInsertionGap > handNow || fanInsertionGap > 16) fanInsertionGap = -1;
        bool fanPresentationChanged = _fanPresentationSent.HasChanged(
            fanArcOrder, fanArcOrderCount, _fanArcOrderBuf, fanInsertionGap);
        ItemsPile? itemsNow = ItemsPile.Current;
        int itemsCount = itemsNow != null && itemsNow.IsOpen ? itemsNow.Chips.Count : 0;
        bool countsChanged = handNow != _lastSentHandCount || itemsCount != _lastSentItemCount;

        // ITEM-USE CLIP (extension record 26, 2026-08-09): WHICH fan position currently lies clipped
        // in our own item-use recess. Sampled here, beside the count it indexes into, and clamped
        // against that very count — a chip can only be reported at a position the peer's arc really
        // has, and the two numbers must come from the same frame or a peer could clip a slab it has
        // not built yet. -1 = the recess is empty ⇒ no record at all (see the field's note for why
        // both edges pre-empt the rate gate).
        //
        // READ OFF RecessOwner, NOT OFF Current (user report 2026-08-09, round 2: "eine Karte die im
        // Overlay liegt verschwindet auf jedem anderen Board sobald der Besitzer den Fächer
        // zuklappt"). Current is the OPEN arc and is null the instant the owner clicks their fan
        // away — which is the state they spend most of the decision in, because clicking away IS the
        // ordinary dismiss and the whole point of the recess is that the card stays put. So this line
        // published "recess empty" while the owner sat looking at the card, ItemsPile's own
        // closed-arc answer (ClippedChipIndex's !IsOpen branch, which returns the survivor's arc
        // position) was never once asked for, and the receiver's detached-recess path could not even
        // arm itself (RemoteItemFan.BeginCollapse needs this record to still name a slab at the close
        // edge). RecessOwner is the pile that owns the recess whether or not its arc is up; it says
        // nothing about a fan existing, which is exactly why the count above still reads Current.
        //
        // IT GOES DARK EXACTLY WHEN THE OWNER'S CARD DOES, because the property it asks is the same
        // single scan the owner's own recess is driven from — a stale index would leave a slab lying
        // in a recess that is empty on the owner's board, which is worse than none.
        ItemsPile? recessNow = ItemsPile.RecessOwner;
        int itemClip = recessNow != null ? recessNow.ClippedChipIndex : -1;
        // THE CARD OUTLIVES THE FAN (user report 2026-08-09; local half in ItemsPile._keptClip). The
        // clamp used to be unconditional, which quietly encoded "no fan => no card in the recess" —
        // and that is precisely the state the owner now spends most of the decision in: they lay the
        // card in, click the fan away, and go on playing while the card lies there. With itemsCount
        // 0 the clamp turned every such frame into -1, so a peer saw the card leave their mirrored
        // recess the instant the owner put their fan down.
        //
        // So the clamp is what it always meant: an index may not exceed the ARC it indexes into —
        // and only while there IS an arc. With the arc closed the index is the position the card
        // held when it folded away, which is the same slab the receiver already has parented to
        // their recess (RemoteItemFan keeps it out of the fold-in); it identifies an OBJECT, not a
        // seat in a fan that no longer exists.
        if (itemsCount > 0 && itemClip >= itemsCount)
            itemClip = -1;
        bool itemClipChanged = itemClip != _lastSentItemClip;

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
        // Board motion rides the rig packet directly; it no longer raises the cadence of
        // this much larger content snapshot. Other interactive edges keep their own gates.
        float fastInterval = 1f / NetProtocol.SendRateHz;

        // A DRAGGED SHARED WINDOW RIDES THE SAME FAST CADENCE AS A DRAGGED BOARD (user request 7,
        // 2026-08-22, verbatim: "Die Bewegungen der 'blauen' MP-Fenster, die 1:1 synchronisiert
        // werden sollen, sollen auch die Bewegung und die Position voll übertragen (flüssig, wie bei
        // der Position des Boards auch)!").
        //
        // Shared windows retain their existing 15 Hz active-grab presence gate. The board
        // moved to an atomic rig-tail pose in 497; this separate window route and all of its
        // interactive pre-emption remain unchanged. Motion is bounded by the rig interval,
        // never the owner's headset frame rate, and returns to idle cadence after release.
        //
        // "GRABBED" RATHER THAN "THE POSE CHANGED": see SharedWindows.AnyGrabbedHere for why there is
        // no single last-sent pose to diff here and why the grab is the right superset.
        bool sharedWindowDue = WorldUI.SharedWindows.AnyGrabbedHere()
                               && _extrasAccumulator >= fastInterval;

        // BOARD-UI (defects 4 + 5): which controls the owner's board shows RIGHT NOW plus the
        // wanted-slot glow mask — read off the same objects that drive the local rendering, so
        // the wire state is the rendered state by construction. -1 = no live tray this frame.
        int boardUiNow = SampleBoardUi(trayNow);
        // THE SNAP FIELD IS RATE-CAPPED, THE REST OF THE RECORD IS NOT. Every other bit of this
        // record moves on a game-state edge — a control appears, a rest is selected, a card lands —
        // so a change pre-empts the 5 Hz gate OUTRIGHT and lands in the next frame. The snap field
        // is different in kind: it follows the owner's HAND, and a held card hovering the boundary
        // of a recess can toggle it at frame rate. Splitting the change test gives it the same
        // capped pre-emption the card highlight and the half hover already use (at most one packet
        // per rig interval), so the telegraph still lands with the gesture but a jittering hand can
        // never turn this record into a stream.
        const int snapKeyMask = NetProtocol.BoardUiSnapMask << 8;
        bool boardUiChanged = (boardUiNow & ~snapKeyMask) != (_lastSentBoardUi & ~snapKeyMask);
        bool boardSnapChanged = (boardUiNow & snapKeyMask) != (_lastSentBoardUi & snapKeyMask);
        bool boardSnapDue = boardSnapChanged && _extrasAccumulator >= fastInterval;

        // SLOT-CARD SIZE (extension record 11, defect "Kartengröße am fremden Board nicht 1:1"):
        // the widths the local board renders its slot overlays and a parked card at — the exact
        // factor chain PlayTray uses (CardWidth × SlotScale for the frame metric, × SlotOverlayScale
        // for the card; see PlayTray.4.Slots.SlotCardScale). Local config, not derivable from
        // anything already synced, so it must ride the wire like the board style does. Sampled
        // before the rate gate so a live config edit reaches peers on the next frame.
        ushort slotFrameCode = NetProtocol.EncodeSlotWidth(
            CardsConfig.CardWidth.Value * PlayTray.SlotScale);
        ushort slotCardCode = NetProtocol.EncodeSlotWidth(
            CardsConfig.CardWidth.Value * PlayTray.SlotScale * PlayTray.SlotCardScale);
        int slotCardSizeNow = trayNow != null ? slotFrameCode | (slotCardCode << 16) : -1;
        bool slotCardSizeChanged = slotCardSizeNow != _lastSentSlotCardSize;

        // CARD HIGHLIGHT (extension record 6, defect (f) "das Hervorheben von Karten ist gar nicht
        // synchronisiert"): WHICH card in the hand fan and in the open board fan the owner is
        // singling out. Read as a bare INDEX off the fans' own highlight predicate — never a card
        // identity, which is the standing rule for this wire. -1 = nothing highlighted there.
        int handHl = CardFan.Current?.HighlightedIndex ?? -1;
        // BOARD FAN = the pile browser OR the item fan — at most one is open (Cards-layer mutual
        // exclusion), so one wire field covers both. The item fan was missing here, which is why a
        // peer never saw an item chip lift (user report 2026-08-03).
        //
        // BOTH sources cover BOTH local hover paths — hand sweep AND laser beam — because both
        // predicates now read the chip/card's own combined pop state (VRCard.IsHighlighted /
        // ItemsPile.ItemChip.IsHighlighted). Until the MP test 2026-08-07 the ITEM side read the
        // hand-sweep winner index alone, so a laser hover over an item chip lifted it locally and
        // reached no peer ("Beim Hovern mit dem Laser wird das Highlight nicht synchronisiert; mit
        // der Hand schon"). The fix is entirely inside ItemsPile.HighlightedIndex — this stays one
        // bare index on record 6, and the change gate below still collapses a hand→laser handover
        // on the same chip to zero packets.
        int fanHl = PileBrowser.Current?.HighlightedIndex ?? -1;
        string fanHlSource = fanHl >= 0 ? "pile browser" : "none";
        if (fanHl < 0)
        {
            fanHl = ItemsPile.Current?.HighlightedIndex ?? -1;
            if (fanHl >= 0)
                fanHlSource = "item fan";
        }
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

        // HALF SELECTION (record 14 byte 1, follow-up defect "Ich will auch sehen, welche Hälfte
        // der Mitspieler GEKLICKT hat"): the persistently selected half of each docked round
        // card, read off the game's own per-half latch (FullAbilityCardAction.isSelected — the
        // exact state its steady ShowSelected highlight renders, cleared by the game's undo).
        // Unlike the hover this pre-empts the gate OUTRIGHT: a click/undo is discrete and
        // human-paced (the pile-counts rule), and the steady highlight must land WITH the click.
        HalfSelection.SampleLocalSelection(out int halfSel0, out int halfSel1);

        // STANDARD-ACTION QUALIFIER (record 14 byte 2 — user defect 2026-08-15 item 6: "Wenn
        // jemand die standart Aktion ausgewählt hat oder drüber hovered wird trotzdem der große
        // untere bzw obere Bereich der Karte bei den remote boards angezeigt/gehighlighted").
        // The two samplers above cannot answer this: the hover tap sits on the card's NON-default
        // funnel (which the geometric laser resolve fires for a chip too, because the chip lies
        // inside the half's zone rect) and the selection sampler ORs the two game latches into one
        // value. LocalBoardSlots splits both apart off the owner's own recesses; see its doc for
        // why the recesses are read from the scene and for the agreement guard.
        LocalBoardSlots.SampleDefaults(trayNow, halfHover, halfSlot, halfTop, halfSel0, halfSel1,
                                       out bool halfHoverDef,
                                       out bool halfSel0Def, out bool halfSel1Def);
        int halfSelNow = halfSel0 | (halfSel1 << 2)
                         | (halfSel0Def ? 1 << 4 : 0) | (halfSel1Def ? 1 << 5 : 0);
        bool halfSelChanged = halfSelNow != _lastSentHalfSelect;
        // The qualifier rides byte 0's own change gate too: moving the beam from a half onto that
        // half's chip changes NOTHING in byte 0 (same slot, same half) but changes the picture
        // completely, so the hover key has to carry it or the edge would be invisible.
        int halfHoverNow = halfHover
            ? (halfSlot | (halfTop ? 1 << 8 : 0) | (halfHoverDef ? 1 << 9 : 0))
            : -1;
        bool halfHoverChanged = halfHoverNow != _lastSentHalfHover;
        bool halfHoverDue = halfHoverChanged && _extrasAccumulator >= fastInterval;

        // ROUND-CARD SLOT ORDER (record 18 — user defect 2026-08-15 item 8: "Die Position der
        // Karten (linke Karte/rechte Karte) war in einem Test verdreht … Die Reihenfolge MUSS
        // zwingend identisch sein wie es der jenige Spieler auch sieht"). WHICH of our two round
        // cards is physically in the LEFT recess, so no peer has to guess it. A SELECTION-phase
        // read as well as an action-phase one: the recesses are the same two transforms in both.
        bool slotOrder = LocalBoardSlots.TrySampleSlotOrder(
            trayNow, CardsGameApi.ActiveHand(), out bool slotOrderSwapped,
            out string slotLeftName, out string slotRightName);
        int slotOrderNow = slotOrder ? (slotOrderSwapped ? 1 : 0) : -1;
        bool slotOrderChanged = slotOrderNow != _lastSentSlotOrder;

        // CAP PRESS (record 14 byte 0 bits 3..7 — the one keycap ANIMATION that is not derivable
        // from already-synced state; see Cards.BoardCapPress for why). WHICH cap the owner has just
        // pressed plus a 2-bit sequence, latched for a short hold window so the record rides a few
        // packets and a lost datagram still delivers it. A NEW press pre-empts the gate OUTRIGHT —
        // a click is discrete and human-paced (the pile-counts rule) and the mirrored dip has to
        // land WITH it, not up to 200 ms later. The hold window itself does NOT pre-empt: it only
        // rides packets that were going out anyway.
        bool capPress = BoardCapPress.TrySample(out byte capPressCap, out byte capPressSeq);
        int capPressNow = capPress ? capPressCap | (capPressSeq << 8) : -1;
        bool capPressChanged = capPressNow >= 0 && capPressNow != _lastSentCapPress;

        // TRACK HOVER (extension record 16, user defect "die Mouseover der Initiativreihenfolge
        // sind nicht synchronisiert"): which initiative-track entry OUR pointer is on (the STABLE
        // actor id, NetFigures.StableActorId — not the per-class CActor.ID; the display order is
        // per-client, see the record doc) plus whether its info
        // popup is open. Same capped pre-emption: a laser can sweep the whole track in under a
        // second.
        bool trackHover = InitiativeHoverSampler.TrySample(out int trackActorId, out bool trackPopup);
        int trackHoverActorNow = trackHover ? trackActorId : -1;
        bool trackHoverChanged = trackHoverActorNow != _lastSentTrackHoverActor
                                 || (trackHover && trackPopup != _lastSentTrackHoverPopup);
        bool trackHoverDue = trackHoverChanged && _extrasAccumulator >= fastInterval;

        // CHARACTER FOCUS (extension record 22, feature "free character focus"): which character
        // we are LOOKING at, plus the facts no receiver can derive — whether the character THE GAME
        // IS WAITING ON is ours (IsUnderMyControl is a local flag) and, when that is somebody other
        // than the one we are looking at, WHICH character it is (a decision we owe is hidden on
        // every other machine, TakeDamagePanel.cs:1133). All three change on human timescales (a
        // portrait click, a turn hand-off, a prompt opening), so a plain change edge is enough; no
        // pre-emption.
        Board.CharacterFocus.Sample(out int focusActorNow, out int attentionActorNow,
                                    out bool focusOwnsAttentionNow);
        bool focusChanged = focusActorNow != _lastSentFocusActor
                            || attentionActorNow != _lastSentFocusAttentionActor
                            || focusOwnsAttentionNow != _lastSentFocusOwnsAttention;

        // TRACK SELECTION (extension record 23, user ruling "auch die highlights der
        // Initiativreihenfolge auf dem remote board, so wie der Spieler sie sieht"): which entries
        // our OWN track is FRAMING, read straight off vanilla's selectionObject. Deliberately not
        // derived from record 22 — the focus and the frame are different facts and come apart the
        // moment an enemy is at turn or a mod focus is taken (see NetProtocol.ExtIdTrackSelection).
        int trackSelCount = InitiativeHoverSampler.SampleSelectedActorIds(_trackSelectionSample);
        bool trackSelChanged = trackSelCount != _lastSentTrackSelectionCount;
        if (!trackSelChanged)
        {
            for (int k = 0; k < trackSelCount; k++)
            {
                if (_trackSelectionSample[k] != _lastSentTrackSelection[k])
                {
                    trackSelChanged = true;
                    break;
                }
            }
        }

        // TRACK ORDER (extension record 27, the third defect of the 1:1 board audit): the
        // ON-SCREEN ORDER of our own track's PLAYER entries, plus which of them we control.
        // Vanilla sorts player entries by IsUnderMyControl while online AND in the card-selection
        // phase (InitiativeTrackActorBehaviour.cs:160-171), so in that window — and ONLY in that
        // window, which is why the sampler returns 0 outside it — my index 3 really is your index
        // 5, and a peer's mirrored track was showing the OBSERVER's arrangement. No pre-emption:
        // the order moves when the track re-sorts, which is a per-round event.
        int trackOrderCount = InitiativeHoverSampler.SampleTrackOrder(_trackOrderSample,
                                                                     out byte trackOrderOwned);
        bool trackOrderChanged = trackOrderCount != _lastSentTrackOrderCount
                                 || trackOrderOwned != _lastSentTrackOrderOwned;
        if (!trackOrderChanged)
        {
            for (int k = 0; k < trackOrderCount; k++)
            {
                if (_trackOrderSample[k] != _lastSentTrackOrder[k])
                {
                    trackOrderChanged = true;
                    break;
                }
            }
        }

        // WALL FADES (extension record 17): sample the local fade decision's ON set as
        // sorted keys and diff against the last sent set — see the field block's doc.
        int wallFadeCount = Core.WallSegmentFade.SampleFadedWallKeys(_wallFadeSample);
        bool wallFadesChanged = wallFadeCount != _lastSentWallFadeCount;
        if (!wallFadesChanged)
        {
            for (int k = 0; k < wallFadeCount; k++)
            {
                if (_wallFadeSample[k] != _lastSentWallFades[k])
                {
                    wallFadesChanged = true;
                    break;
                }
            }
        }
        bool wallFadesDue = wallFadesChanged && _extrasAccumulator >= fastInterval;

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

        // HELD PROPS (extension record 37): the map items in the player's hands — a chest, a gold
        // pile, a loose obstacle. Sampled BEFORE the rate gate so grab and release can pre-empt it,
        // and converted to the shared anchor frame here (once) so the change test compares the very
        // bytes that go on the wire — the same shape the second figure above uses, for the same
        // reasons, because the user's ruling is that there is to be no difference between the two.
        //
        // THE SIZE IS SAMPLED WITH THE POSE and quantized here, so the change test compares wire
        // codes rather than floats: the factor is a FIELD of this record, not a record of its own
        // (the standing "es syncht voll oder gar nicht" ruling, and record 30's two slots are
        // figure-aligned and could not have carried it anyway).
        bool heldProp = NetProps.TrySampleHeldSlot(
            NetProps.SlotPrimary, out int heldPropId, out Vector3 propWorldPos,
            out Quaternion propWorldRot, out bool propLeftHand);
        Vector3 propPos = default;
        Quaternion propRot = Quaternion.identity;
        int propSizeCode = NetProtocol.HeldStretchCodeNeutral;
        if (heldProp)
        {
            _anchor.ToAnchor(propWorldPos, propWorldRot, out propPos, out propRot);
            propSizeCode = NetProtocol.EncodeHeldStretch(NetProps.SampleHeldStretch(NetProps.SlotPrimary));
        }

        // Sampled UNCONDITIONALLY and narrowed below rather than short-circuited behind
        // `heldProp`: a && with out-parameters leaves them definitely-unassigned on the false path,
        // and the narrowing reads better as one place that states every reason slot 2 is dropped.
        bool secondProp = NetProps.TrySampleHeldSlot(
            NetProps.SlotSecondary, out int secondPropId, out Vector3 secondPropWorldPos,
            out Quaternion secondPropWorldRot, out bool secondPropLeftHand) && heldProp;
        Vector3 secondPropPos = default;
        Quaternion secondPropRot = Quaternion.identity;
        int secondPropSizeCode = NetProtocol.HeldStretchCodeNeutral;
        if (secondProp)
        {
            // The reader rejects a pair that agrees on the hand or on the prop, so the sender does
            // not write one: two slots naming one hand is a state no grab registry can produce, and
            // emitting it would cost the receiver its whole record rather than one slot.
            secondProp = secondPropId != heldPropId && secondPropLeftHand != propLeftHand;
        }
        if (secondProp)
        {
            _anchor.ToAnchor(secondPropWorldPos, secondPropWorldRot, out secondPropPos, out secondPropRot);
            secondPropSizeCode = NetProtocol.EncodeHeldStretch(NetProps.SampleHeldStretch(NetProps.SlotSecondary));
        }

        // Grab, release and a different item in that hand are EDGES: they pre-empt the gate outright
        // so a peer sees the item appear and vanish with the gesture rather than up to 200 ms later.
        bool propChanged = heldProp != _sentHeldPropValid
                           || secondProp != _sentSecondPropValid
                           || (heldProp && heldPropId != _lastSentHeldPropId)
                           || (secondProp && secondPropId != _lastSentSecondPropId);
        // Carrying it — or resizing it in the hand — is a MOTION, capped at the rig interval exactly
        // like a carried second figure, so a 90 Hz hand drag cannot become a 90 Hz packet stream.
        bool propMoving =
            (heldProp && _sentHeldPropValid && heldPropId == _lastSentHeldPropId
             && ((propPos - _lastSentHeldPropPos).sqrMagnitude > 1e-8f
                 || Quaternion.Angle(propRot, _lastSentHeldPropRot) > 0.05f
                 || propSizeCode != _lastSentHeldPropSize))
            || (secondProp && _sentSecondPropValid && secondPropId == _lastSentSecondPropId
                && ((secondPropPos - _lastSentSecondPropPos).sqrMagnitude > 1e-8f
                    || Quaternion.Angle(secondPropRot, _lastSentSecondPropRot) > 0.05f
                    || secondPropSizeCode != _lastSentSecondPropSize));
        bool propDue = propMoving && _extrasAccumulator >= fastInterval;

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

        // HELD-CARD GRIP (extension record 34): which of the two held-card pose slots is being held
        // RIGIDLY in the fist rather than billboarded at this player's head. A DISCRETE HUMAN ACT —
        // a grip press on a card already in hand — whose entire purpose is that somebody else looks
        // at it, so it PRE-EMPTS the cadence outright rather than riding the fast interval: up to
        // 200 ms of "I turned the card and you did not see it turn" is exactly the latency this
        // gesture cannot afford. It is also two bits, so pre-empting costs three bytes.
        byte cardGripMask = LocalRigSampler.SampleHeldCardGripMask();
        bool cardGripChanged = cardGripMask != _lastSentCardGripMask;

        // HELD-FIGURE STRETCH (extension record 30): the manual two-hand resize factor of each
        // held-figure slot, quantized HERE so the change test compares wire codes, not floats.
        // A mid-gesture drag is a MOTION, not an edge — it rides the fast interval exactly like a
        // carried second figure (15 Hz while the codes keep changing), never pre-empting outright,
        // or a 90 Hz hand drag would become a 90 Hz packet stream. Appear/disappear needs no edge
        // treatment either: the factor grows continuously from 1.0 and eases back to it, so there
        // is nothing discrete for a peer to miss. Both slots neutral ⇒ record omitted (the writer's
        // own gate), which is every idle player and every build before this one.
        int stretchPrimaryCode = NetProtocol.EncodeHeldStretch(
            NetFigures.SampleHeldStretch(NetFigures.SlotPrimary));
        int stretchSecondaryCode = NetProtocol.EncodeHeldStretch(
            NetFigures.SampleHeldStretch(NetFigures.SlotSecondary));
        bool stretchChanged = stretchPrimaryCode != _lastSentStretchPrimary
                              || stretchSecondaryCode != _lastSentStretchSecondary;
        bool stretchDue = stretchChanged && _extrasAccumulator >= fastInterval;

        // BOARD TOOLTIP (extension record 9): the text of the board-owned tooltip the owner is
        // reading, ALREADY identity-gated by WorldTooltips (only content public to peers ever
        // reaches this read — see NetProtocol.ExtIdBoardTooltip). Sampled before the rate gate so
        // its appearance/disappearance/text-change edges pre-empt it like the pick banner's do.
        string? tooltipNow = WorldUI.WorldTooltips.WireText;
        bool tooltipChanged = tooltipNow != _lastSentTooltip;

        // DECISION LINES (extension record 12): the labels of the decision row currently docked
        // below the owner's board, published by DecisionDockSurface (pressable-widget labels
        // only — the identity gate lives in the sampler, see WireButtonLines). Sampled before
        // the rate gate so dock/undock/re-label edges pre-empt it like the board-UI bits do.
        string? decisionNow = WorldUI.Surfaces.DecisionDockSurface.WireButtonLines;
        bool decisionChanged = decisionNow != _lastSentDecisionLines;

        // DECISION STATE (extension record 23, user ruling 2026-08-08 "die Schadensabfrage 1:1 …
        // wie der Spieler es auch sieht"): WHICH prompt is docked, which prompt-TEXT variant the
        // owner is reading, and per option offered / greyed / chosen. Rides record 12's own gate
        // (only while a row is docked AND visible), so wordings and states can never disagree; the
        // text VARIANT is a number the receiver localizes itself — the composed line never rides
        // the wire, it can embed active-bonus card names. Sampled before the rate gate: a toggle
        // flip is exactly the human-paced edge the labels already pre-empt for.
        byte decisionKind = 0;
        byte decisionText = 0;
        int decisionOptions = 0;
        if (!string.IsNullOrEmpty(decisionNow))
        {
            decisionKind = WorldUI.Surfaces.DecisionDockSurface.WirePromptKind;
            decisionText = WorldUI.Surfaces.DamageTooltipSurface.WireTextVariant;
            decisionOptions = WorldUI.Surfaces.DecisionDockSurface.CopyWireOptionStates(
                _decisionOptionSample);
        }
        long decisionStateNow = -1;
        if (!string.IsNullOrEmpty(decisionNow))
        {
            decisionStateNow = NetProtocol.EncodeDecisionFlags(decisionKind, decisionText)
                               | ((long)decisionOptions << 8);
            for (int o = 0; o < decisionOptions; o++)
                decisionStateNow |= (long)_decisionOptionSample[o] << (16 + o * 3);
        }
        bool decisionStateChanged = decisionStateNow != _lastSentDecisionState;

        // DECISION WIDGETS (extension record 29, user report 2026-08-09 "Die Entscheidungsbuttons
        // sollen auch 1:1 aussehen … hier sollen auch die Spielicons/Text etc. genutzt werden"):
        // WHICH game widget each option IS, plus the take-damage option's damage number and its
        // lethal / shielded / mandatory picture. Rides record 12's own gate, so the roles can never
        // describe a row the wordings do not. Sampled before the rate gate for the same reason the
        // states are: a shield toggle changes the number the owner reads on the button.
        byte decisionWidgetFlags = 0;
        byte decisionDamage = 0;
        int decisionRoles = 0;
        if (!string.IsNullOrEmpty(decisionNow))
        {
            decisionWidgetFlags = WorldUI.Surfaces.DecisionDockSurface.WireWidgetFlags;
            decisionDamage = WorldUI.Surfaces.DecisionDockSurface.WireDamageAmount;
            decisionRoles = WorldUI.Surfaces.DecisionDockSurface.CopyWireOptionRoles(
                _decisionRoleSample);
        }
        long decisionWidgetNow = -1;
        if (!string.IsNullOrEmpty(decisionNow))
        {
            decisionWidgetNow = decisionWidgetFlags | ((long)decisionDamage << 8)
                                | ((long)decisionRoles << 16);
            for (int o = 0; o < decisionRoles; o++)
                decisionWidgetNow |= (long)_decisionRoleSample[o] << (24 + o * 3);
        }
        bool decisionWidgetChanged = decisionWidgetNow != _lastSentDecisionWidgets;

        // USE BARS (extension record 25, the same 2026-08-08 ruling): the SECOND drawer below the
        // decision row — which of the four use bars are docked AND VISIBLE on the owner's board,
        // how many slots each shows, whether it has an element/option sub-picker open, and per slot
        // offered / dimmed / chosen. The bars are HUD singletons raised on ONE client, so none of
        // this exists anywhere else and a peer saw nothing there at all. A bar the owner
        // render-hid because they are looking at another character is already out of the mask the
        // surface publishes, so the hide travels with the drawer. Sampled before the rate gate: a
        // slot click is exactly the human-paced edge the decision records already pre-empt for.
        byte useBarMask = WorldUI.Surfaces.UseBarsSurface.WireBarMask;
        // …AND A BAR THAT BELONGS ONLY TO SOMEBODY ELSE'S CHARACTERS NEVER LEAVES THIS MACHINE.
        //
        // USER REPORT 2026-08-13, verbatim: "Die Stiefel-Entscheidungen waren im Test nur bei einem
        // Character zu tun, aber mein Mitspieler hat die die selbe Entscheidung bei einem anderen
        // Character angezeigt, obwohl er der character sie nicht hat und diese Entscheidung auch
        // nicht treffen muss. Warum wurde sie fälschlicherweise auch noch bei einem anderen
        // Character angezeigt der das item gar nicht hatte?"
        //
        // WARUM (proven from the game's own code and both ModBuild-137 logs): the boots prompt is
        // not a decision-dock prompt at all — it is the ACTIVE-BONUS bar for Boots of Speed's
        // initiative adjustment. Choreographer.CheckForInitiativeAdjustments calls
        // UIActiveBonusBar.ShowActiveBonus(msg.m_ActorSpawningMessage, …) on EVERY client and gates
        // only the ready BUTTON on IsUnderMyControl — the bar itself is not gated at all. So the
        // teammate's client raised Cryonaris's bar as a local HUD singleton, the mod docked it on
        // THAT machine's own board (which was showing Hilde Die 2Te), and their log says both
        // things in the same second:
        //     "USE BARS: bonus-bar split KEPT the decision-area row for 'ITEM_NAME_BootsofSpeed'"
        //     "USE BARS: 'UseBarActiveBonus' VISIBLE — owner 'Cryonaris' is the character in view"
        //     "Board: CONFIRM/UNDO keycaps … owner 'Hilde Die 2Te' is in view"
        // The rule that is supposed to stop this (one character owns a decision) compares the bar's
        // owners against Board.CharacterFocus.Focused — the EXPLICIT focus override, which is null
        // whenever the player is simply following the game, i.e. almost always. It is a
        // single-client rule with no multiplayer half, and the take-damage prompt only escapes it by
        // accident (the game hides its own window on non-controlling clients; UIActiveBonusBar does
        // not).
        //
        // THIS GUARD IS THE HALF THAT LIVES ON MY SIDE OF THE FILE FENCE, and it is worth having on
        // its own merits: it stops the bogus bar from being BROADCAST, which is the copy the user
        // himself saw ("bei einem anderen Character angezeigt" on his mirrored view of the
        // teammate's board). A bar every one of whose owners is a foreign character can never be
        // answered here — the peer's own log proves it, the slot arrived "#0=greyed+dim" there and
        // "#0=OFFERED" on its real owner's machine — so withholding it cannot deadlock anybody, and
        // single player is untouched (IsForeign is false whenever the game is offline).
        // The REMAINING half — the bar must not be docked on the local board either — is one
        // predicate in WorldUI/Surfaces/UseBarsSurface.cs, which this lane does not own. REPORTED.
        if (useBarMask != 0 && AllVisibleUseBarsAreForeign(useBarMask))
        {
            if (_lastSentUseBarMask != 0)
                VRLog.Info("Net", "USE BARS withheld: every visible bar on this board belongs to a " +
                                  "character under ANOTHER player's control, so record 25 stops " +
                                  "riding — a peer must not see somebody else's decision drawn a " +
                                  "second time on this player's board. See the boots report of " +
                                  "2026-08-13.");
            useBarMask = 0;
        }
        if (useBarMask != 0)
            WorldUI.Surfaces.UseBarsSurface.CopyWireBars(
                _useBarFlagsSample, _useBarCountSample, _useBarSlotSample, _useBarIdSample);
        UseBarWidgetState[]? useBarWidgets = WorldUI.Surfaces.UseBarsSurface.WireWidgetStates;
        bool useBarsChanged = useBarMask != _lastSentUseBarMask
                              || !UseBarWidgetState.Equivalent(useBarWidgets, _lastSentUseBarWidgets);
        for (int b = 0; !useBarsChanged && useBarMask != 0 && b < NetProtocol.UseBarsCount; b++)
        {
            if (_useBarFlagsSample[b] != _lastSentUseBarFlags[b]
                || _useBarCountSample[b] != _lastSentUseBarCounts[b])
                useBarsChanged = true;
        }
        for (int s = 0; !useBarsChanged && useBarMask != 0 && s < _useBarSlotSample.Length; s++)
        {
            if (_useBarSlotSample[s] != _lastSentUseBarSlots[s])
                useBarsChanged = true;
        }
        // AN IDENTITY CHANGE IS A CHANGE. The game re-decorates a slot in place — one item spent
        // and the next offered into the same seat — without the mask, the counts or a single state
        // byte moving, and record 45 rides the same edge record 25 does. Without this term the peer
        // would keep the previous symbol until some unrelated byte happened to shift.
        for (int s = 0; !useBarsChanged && useBarMask != 0 && s < _useBarIdSample.Length; s++)
        {
            if (_useBarIdSample[s] != _lastSentUseBarIds[s])
                useBarsChanged = true;
        }

        // (see MaskCapLabel below for records 13 bits 0/2)
        // CAP LABELS (extension record 13): what the owner's CONFIRM cap and docked SKIP button
        // actually read. Null while the control is hidden, so the record's presence tracks the
        // board-UI visibility bits; appearance/disappearance/re-wording are edges.
        string? confirmLabelNow = trayNow != null && trayNow.ConfirmControlShown
            ? trayNow.ConfirmControlLabel
            : null;
        string? skipLabelNow = trayNow != null ? trayNow.SkipCapLabel : null;
        // …and the two wordings that never travelled (mask bits 2/3): the UNDO cap, whose pick-flow
        // override turns it into the confirm dialog's CANCEL, and the item-USE cap, whose
        // surrender-demand override must never read as an ordinary "USE" on a peer's screen. Same
        // shape as the pair above — null while the control is hidden, so the record's presence
        // tracks the board-UI visibility bits.
        string? undoLabelNow = trayNow != null && trayNow.UndoControlShown
            ? trayNow.UndoControlLabel
            : null;
        // …AND THE ITEM-USE ONE IS THE WHOLE AREA'S WORDING SINCE ModBuild 378, which breaks the
        // "null while the control is hidden" sentence directly above FOR THIS FIELD ONLY. The
        // engraved caption under the item recess carries the same per-flow word as the cap and is
        // up from the FIRST frame of a demand, while the cap only appears once the picker reports
        // the selection ready — i.e. after the card is already in the recess. So bit 3 is present
        // for the whole demand and its presence tracks the item-use BERTH, not the cap. Correcting
        // the claim rather than the code: this is a widening of an existing field's meaning inside
        // its declared width (48 B), not a new field, and it is what lets a peer read the owner's
        // "ITEM ABGEBEN" under their mirrored recess at the same moment the owner does.
        string? itemUseLabelNow = trayNow != null ? trayNow.ItemUseCapLabel : null;
        // ─── THE SECOND CHANNEL FOR THE SAME CARD NAME, AND IT HAD NO GATE AT ALL ────────────────
        // Found by the 2026-09-07 sweep that item 6's fix owed. Bits 0 and 2 do NOT carry a generic
        // "Confirm"/"Undo" during a pick flow: PlayTray.ConfirmControlLabel / UndoControlLabel fall
        // through to Cards.CardsGameApi.PickDialogOptionLabel, which reads
        // `button.ExtendedButton.buttonText.text` straight off the live DialogPopup option — THE
        // SAME STRING FAMILY AS RECORD 12, i.e. `<sprite name="LOST"> Verbrennen "Nagende Horde"`.
        // A peer renders it on their mirrored confirm cap (RemoteBoardFurniture.SetCapLabels →
        // InertCap.SetLabel), so masking record 12 alone would have moved the leak one surface over
        // and left the fix as theatre — which is the exact failure this project has recorded as
        // "the blind spot is the lead".
        //
        // IT IS MASKED HERE AND NOT AT PickDialogOptionLabel: that accessor also feeds the OWNER's
        // OWN keycap (PlayTray.7.Nested), and the owner must go on reading their own card's name.
        // The wire is the boundary, so the wire is where the identity comes out — same predicate,
        // same file, same tick as record 12's.
        confirmLabelNow = MaskCapLabel(confirmLabelNow);
        undoLabelNow = MaskCapLabel(undoLabelNow);
        bool capLabelsChanged = confirmLabelNow != _lastSentConfirmLabel
                                || skipLabelNow != _lastSentSkipLabel
                                || undoLabelNow != _lastSentUndoLabel
                                || itemUseLabelNow != _lastSentItemUseLabel;

        // EMPTY-FAN PLACARD (record 14, byte 1 bit 4): our own "Keine Handkarten" plate, read off
        // the seam the RENDERER publishes (Cards.EmptyFanHint.CurrentlyShown — set and cleared by
        // the same statements that show and hide the plate, so the bit and the picture cannot
        // disagree). It is deliberately NOT inferred from the hand-card count: 0 cards with the fan
        // closed is the state of every idle player, and the count therefore cannot say whether the
        // gate opened onto an empty hand (the trap recorded in INVARIANTS-Net-Rig.md). Pre-empts
        // the gate OUTRIGHT — a palm gate opening is discrete and human-paced, and a 1.5 s fade
        // that starts 200 ms late is a visibly different animation.
        bool emptyFanHintNow = Cards.EmptyFanHint.CurrentlyShown;
        bool emptyFanHintChanged = emptyFanHintNow != _lastSentEmptyFanHint;

        // BOARD TUNING (extension record 28): OUR OWN dial positions for the board, its mesh and
        // the hand fan, sampled sparsely — only the dials that differ from the shipped default for
        // our current board style. Sampling walks ~118 config entries and allocates nothing, so it
        // runs on the send path rather than needing a config-change hook; a player who has tuned
        // nothing produces a ZERO-length payload and therefore no record at all, which is what
        // keeps an untuned packet byte-identical to the previous build's. A change is an edge
        // (someone dragging a slider in the debug menu wants to see the result), so it pre-empts
        // the gate, but only ONCE per real change — the key below is the change detector.
        // Null-guarded exactly like LocalRigSampler.LocalBoardStyle: a packet can go out before
        // CardsConfig.Bind has completed (scene load), and CurrentBoard would NRE there. Oak is the
        // right pre-bind answer for the same reason it is the default wire code.
        Cards.ControlBoard tuningBoard = Cards.CardsConfig.Board != null
            ? Cards.ControlBoards.Clamp((int)Cards.CardsConfig.Board.Value)
            : Cards.ControlBoard.Oak;
        int tuningLength = BoardTuningSampler.Sample(tuningBoard, _tuningBuffer);
        bool tuningChanged = _tuningPager.Update(_tuningBuffer, tuningLength);

        // ─── THE FOUR HELD-CARD RECORDS ARE SAMPLED HERE SO THEIR EDGES CAN PRE-EMPT ────────────
        // Records 36 (held-card face), 39 (sacrifice seat), 41 (spent half) and 43 (fan source)
        // used to be sampled AFTER this gate, inside FillHeldCardRecords, and were therefore the
        // only discrete human-paced edges in this method that could not pre-empt it: a pluck that
        // changed the held face reached peers up to 200 ms after the RIG packet that had already
        // moved the slab, so the slab showed a BACK for that window (REVIEW-net.md N7). Every other
        // edge here pre-empts, each with a paragraph saying why 200 ms matters.
        //
        // THE USER'S RULING, 2026-09-08: 1:1 covers TIMING. These four are human-paced and
        // therefore rare, so the extra packet per edge costs practically nothing — chosen over
        // spending a hardware round to measure the delay first.
        //
        // THE SAMPLE IS TAKEN ONCE AND HANDED TO THE WRITER. FillHeldCardRecords now takes these
        // as parameters instead of re-reading the samplers, because a second read is a second
        // answer — the defect shape record 35's comment below refuses in exactly these words. The
        // record payloads, their order in the packet and their four SENT lines are byte-for-byte
        // what they were; only the moment the packet leaves changes.
        //
        // ALL FOUR ARE DISCRETE STATE, NOT A CONTINUOUS VALUE, which is what makes them safe to put
        // in a gate. 36 keys on Grabber.Held — a grab or a release. 39 keys on CardsDriver's
        // _shortRestCard / _fieldCards latches. 41 keys on FullAbilityCardAction.canvasGroup.alpha,
        // written in exactly ONE place in the whole game — `alpha = (active ? 1f : 0.5f)`,
        // FullAbilityCardAction.cs:334, a two-state assignment and never a tween. (The LeanTween
        // pulse on that surface animates CardActionHighlight's OWN CanvasGroup, a different
        // component on a different object; it cannot reach this alpha.) 43 keys on CardsDriver's
        // _fanSourcePile enum. So none of the four can chatter at frame rate, and none can turn the
        // 5 Hz cadence into a stream — which is the one thing that would have made this ruling cost
        // something.
        //
        // SAMPLING EVERY TICK RATHER THAN EVERY 5th COSTS NULL CHECKS. Each sampler early-returns
        // on the state that holds almost all the time (neither hand holds a card shape; no short
        // rest is presented and _fieldCards is empty; no HalfSelection is visible). The deep paths
        // run only while a card really is in a fist or a rest is really up, are bounded by the hand
        // and the discard arc, and allocate nothing — LocalRigSampler.s_heldFaceBuf is a reused
        // buffer whose own note says this send path must stay allocation-free.
        LocalRigSampler.SampleHeldCardFaces(out byte faceCode0, out byte faceCount0,
                                            out byte faceCode1, out byte faceCount1, out _, out int secondFaceActor);
        LocalRigSampler.SampleSacrificeSeats(out byte seatCode0, out byte seatCount0,
                                             out byte seatCode1, out byte seatCount1,
                                             out string seatReason);
        Cards.HalfSelection.SampleLocalSpent(out byte spentMask);
        byte fanSource = LocalRigSampler.SampleFanSource();
        // THE CHANGE DETECTORS ARE THE SAME EXPRESSIONS FillHeldCardRecords ALREADY GATES ITS FOUR
        // SENT LINES ON, against the same _lastSent* latches — deliberately. A Changed term whose
        // latch is never updated pre-empts on EVERY tick and turns the cadence into a per-frame
        // stream; that is excluded here by construction rather than by inspection, because each
        // latch is written UNCONDITIONALLY inside FillHeldCardRecords, FillHeldCardRecords runs on
        // every packet that goes out, and a true term forces a packet out. So a term that opens the
        // gate is cleared by the very packet it forced, on the same tick.
        bool secondMapCard = LocalRigSampler.SampleHeldMapCard(2, out uint secondMapKey, out ushort secondMapSeat, out ushort secondMapCount, out byte secondMapArcSeat);
        bool heldFaceChanged = faceCode0 != _lastSentFaceCode0 || faceCode1 != _lastSentFaceCode1
            || faceCount0 != _lastSentFaceCount0 || faceCount1 != _lastSentFaceCount1
            || secondFaceActor != _lastSentSecondFaceActor || secondMapKey != _lastSentSecondMapKey
            || secondMapSeat != _lastSentSecondMapSeat || secondMapCount != _lastSentSecondMapCount
            || secondMapArcSeat != _lastSentSecondMapArcSeat;
        bool shortRestInProgress = CardsDriver.ShortRestInProgress;
        bool shortRestChanged = shortRestInProgress != _lastSentShortRest;
        NativeDecisionHighlightState? decisionHighlight = _decisionHighlightSnapshot;
        bool decisionHighlightChanged = !ReferenceEquals(decisionHighlight, _lastSentDecisionHighlight);
        DamageDecisionPreviewState? damagePreview = WorldUI.Surfaces.DamageDecisionPreview.SampleLocal();
        bool damagePreviewChanged = !DamageDecisionPreviewState.SamePicture(damagePreview, _lastSentDamageDecisionPreview);
        WorldUI.Surfaces.UseBarsSurface.SampleDecisionAttribution(out int decisionActor, out bool decisionPending, out bool decisionVisible);
        bool decisionAttributionChanged = decisionActor != _lastSentDecisionActor
            || decisionPending != _lastSentDecisionPending || decisionVisible != _lastSentDecisionVisible;
        bool sacrificeSeatChanged = seatCode0 != _lastSentSeatCode0
                                    || seatCode1 != _lastSentSeatCode1;
        bool spentHalfChanged = spentMask != _lastSentSpentMask;
        bool fanSourceChanged = fanSource != _lastSentFanSource;
        // Did one of the four FORCE this packet, or is it merely riding a cadence tick? Read here
        // because _extrasAccumulator is zeroed the moment the gate opens, so after it the answer is
        // no longer recoverable.
        bool heldEdgePreempt = _extrasAccumulator < interval
            && (heldFaceChanged || sacrificeSeatChanged || spentHalfChanged || fanSourceChanged);
        float heldEdgeEarlyMs = (interval - _extrasAccumulator) * 1000f;

        if (_extrasAccumulator < interval && !fxPending && !countsChanged && !browseChanged
            && !maskSizeChanged && !boardStyleChanged && !handScaleChanged && !fanPresentationChanged
            && !boardUiChanged && !boardSnapDue && !capPressChanged && !highlightDue
            && !secondChanged && !secondDue && !secondCardChanged && !secondCardDue
            && !propChanged && !propDue
            && !cardGripChanged
            && !stretchDue
            && !tooltipChanged && !slotCardSizeChanged
            && !pileCountsChanged && !halfHoverDue && !halfSelChanged && !slotOrderChanged
            && !trackHoverDue
            && !wallFadesDue
            && !decisionChanged && !decisionStateChanged && !decisionWidgetChanged
            && !useBarsChanged
            && !capLabelsChanged && !focusChanged && !trackSelChanged && !trackOrderChanged
            && !emptyFanHintChanged && !tuningChanged && !itemClipChanged
            // THE FOUR HELD-CARD EDGES (records 36, 39, 41, 43) — the N7 ruling; see the
            // sampling block above for why each is discrete and what it costs.
            && !heldFaceChanged && !sacrificeSeatChanged && !spentHalfChanged
            && !fanSourceChanged && !shortRestChanged && !decisionHighlightChanged && !damagePreviewChanged && !decisionAttributionChanged
            // A DEBUG PRESS PRE-EMPTS THE CADENCE. It is a discrete, human-paced act whose entire
            // purpose is to be looked at, so up to 200 ms of cadence latency between two headsets is
            // exactly the "did that work?" the test page exists to remove. Also true throughout the
            // explicit-release burst, so the ALL-ZERO record is repeated rather than sent once into
            // an unreliable stream.
            && !RemoteTestTriggers.SendDue
            // THE 3D MAP ROOM (20) AND ITS SHARED WINDOWS (21) PRE-EMPT THE CADENCE, and each for
            // an edge that is a discrete human act: entering the room, pressing the world/city cap,
            // clicking an icon (record 20), and turning a page of the map story or opening the
            // quest window (record 21). "Klickt einer weiter ist es für alle im 3d-Worldmap-Raum
            // weitergeklickt worden" is judged on whether the other player's page turns when yours
            // does, so 200 ms of cadence latency is the feature failing rather than merely lagging.
            // Both getters return false outright while MapRoomDriver.Active is off, so a client
            // with the 3D map switched off evaluates two bools and is otherwise untouched.
            && !RemoteMapRoom.SendDue && !RemoteMapStory.SendDue && !RemoteVideoPlayback.SendDue
            // …and a shared window being CARRIED here raises the cadence to the rig rate for as long
            // as the hand is on it, exactly as a carried board does (see sharedWindowDue above).
            // Note this sits beside RemoteMapStory.SendDue and does NOT duplicate it: that getter is
            // about EDGES (a page turned, the quest window opened) and pre-empts outright; this one
            // is about MOTION and is capped at the rig interval.
            && !sharedWindowDue)
            return;
        _extrasAccumulator = 0f;
        _lastSentHandCount = handNow;
        _fanPresentationSent.MarkSent(fanArcOrder, fanArcOrderCount, _fanArcOrderBuf, fanInsertionGap);
        _lastSentItemCount = itemsCount;

        var extras = default(PresenceState);

        FillEnvironmentRecords(ref extras);
        RemoteVideoPlayback.Sample(ref extras, _transport.LocalPlayerId);

        if (board != null)
        {
            extras.HasBoard = true;
            extras.Board.Position = boardPos;
            extras.Board.Rotation = boardRot;
            extras.BoardScale = boardScale;
        }

        extras.HandCardCount = (byte)Mathf.Clamp(handNow, 0, 255);
        extras.HasFanInsertionGap = true;
        extras.FanInsertionGap = fanInsertionGap;
        // …AND THE ORDER THOSE SLABS GO IN (record 44). Written beside the count it permutes, from
        // the sample taken on the same frame. Absent whenever the sampler could not answer or the
        // arc is already in the derived order — silence means "keep the order you have", which is
        // exactly what every build before this one did.
        if (fanArcOrder && fanArcOrderCount > 0)
        {
            extras.HasFanArcOrder = true;
            extras.FanArcOrderCount = fanArcOrderCount;
            extras.FanArcOrder = _fanArcOrderBuf;
        }
        extras.DecisionHighlight = decisionHighlight;
        extras.DamageDecisionPreview = damagePreview;
        extras.HasDecisionAttribution = true;
        extras.DecisionActorId = decisionActor;
        extras.DecisionPending = decisionPending;
        extras.DecisionVisible = decisionVisible;
        _lastSentDecisionActor = decisionActor;
        _lastSentDecisionPending = decisionPending;
        _lastSentDecisionVisible = decisionVisible;
        _lastSentDamageDecisionPreview = damagePreview;
        _lastSentDecisionHighlight = decisionHighlight;
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
        // ITEM-USE CLIP (record 26): WHICH position is lying in our own use recess right now.
        //
        // OUTSIDE the fan branch since 2026-08-09, and that is the receiver-side half of "die
        // abgelegte Gegenstandskarte verschwindet wenn ich den Fächer schliesse". It used to sit
        // INSIDE it on the argument that "an index is meaningless without the count it indexes
        // into" — true of a seat in an arc, false of the card in the RECESS, which is exactly the
        // thing that is not in the arc. The owner's card now stays lying there after the fan folds
        // away, so the record has to be able to describe a recess with no fan behind it; the
        // receiver (RemoteItemFan) reads it the same way — the slab it already holds on the recess
        // is kept OUT of the fold-in instead of being collapsed with the arc.
        //
        // Still additive TLV and still ONE index byte with no item identity: a reader that ignores
        // record 26 steps over it by its own length exactly as before.
        if (itemClip >= 0)
        {
            extras.HasItemUseClip = true;
            extras.ItemUseClipIndex = (byte)itemClip;
        }

        // PER-ITEM USABLE MASK (record 35 — the 2026-09-02 report's item 5, "die highlighting
        // Animation … ist aber nicht beim remote board … sichtbar"): WHICH equipped items are
        // wearing the gold "you can play this NOW" frame on THIS board right now.
        //
        // THE VALUE IS READ, NOT RE-DERIVED. Cards.PileViewer publishes the very ushort its own
        // TickItemsUsableHighlight framed its chips from this frame, in the same statement group as
        // the closed stack's cue — so the mirrored frames and the owner's frames come out of one
        // pass and cannot drift apart. A second read of the predicate here would be a second
        // answer, which is exactly the shape of defect the pile counts were moved onto the wire to
        // end. Outside the fan branch on purpose: the mask describes the INVENTORY, and the stack
        // beats with it whether or not the arc is up.
        //
        // Written only while non-zero (PresenceSerializer's own gate uses the same test), and it is
        // zero for every off-turn player by construction, so nearly every packet is unchanged.
        ushort usableMask = Cards.PileViewer.ItemsUsableMask;
        if (usableMask != 0)
        {
            extras.HasItemUsable = true;
            extras.ItemUsableMask = usableMask;
        }
        if (usableMask != _lastSentUsableMask)
        {
            _lastSentUsableMask = usableMask;
            // HW-VERIFY: ModBuild 352 item 5 — the SENDER edge of the mirrored per-item pulse. The
            // co-player runs at the shipped default level and either tester can be the owner, so
            // this edge has to be readable on both machines; it is a change edge on a human-paced
            // value, so it is a handful of lines per scenario. Pair it with the receiver's
            // "Remote ITEM usable" line to decide sender-vs-mirror in one log.
            VRLog.Note("Net", $"Item usable mask SENT: 0x{usableMask:X4} (record 35, "
                + $"{CountBits(usableMask)} item(s) framed) over Inventory.AllItems RAW index — the "
                + "value THIS board framed its own chips from this frame (Cards.PileViewer."
                + "ItemsUsableMask). 0x0000 means nothing is playable here and the record is "
                + "omitted, which is what an off-turn board always sends.");
        }
        if (itemClipChanged)
        {
            _lastSentItemClip = itemClip;
            // BOTH EDGES, WITH THE ARC STATE AND THE REASON ON THE LINE (grep: "Item-use clip SENT").
            // The whole defect this round fixed was invisible in the log precisely because the two
            // edges never said which ARC they happened under: "recess EMPTY" at the moment the fan
            // closed reads exactly like a legitimate cancel, and there was nothing to tell the two
            // apart afterwards. Every appearance now names the arc it appeared under, and every
            // disappearance names WHY the pile stopped reporting one — so a single hardware log
            // proves the peer-side lifetime (this line + RemoteItemFan's "Remote item recess") with
            // no second round of guessing. Built only on the change edge, which is human-paced.
            string arcNow = recessNow == null ? "no item pile at all"
                          : recessNow.IsOpen ? $"arc UP ({itemsCount} chip(s))"
                          : "arc DOWN";
            string why = itemClip >= 0
                ? (itemsCount > 0
                    ? "the owner clipped it in with their fan up"
                    : "the card is LYING in the recess with the fan folded away (ItemsPile._keptClip) " +
                      "— the state that used to publish nothing at all, which is what made a peer's " +
                      "copy leave their recess the instant the owner clicked their fan away")
                : recessNow == null
                    ? "the item pile is gone (board teardown / scene change)"
                    : recessNow.IsOpen
                        ? "the arc is up and no chip is clipped — taken back out, cancelled, or the USE finished"
                        : recessNow.HasPlacedCardWhileClosed
                            ? "the arc is down and the survivor is IN A HAND right now — our own recess is " +
                              "empty this frame too, so a peer's must be (it comes back the moment the " +
                              "card is released over the recess again)"
                            : "the arc is down and nothing lies in the recess — the placement was cancelled, " +
                              "confirmed, or play moved on";
            VRLog.Info("Net", itemClip < 0
                ? $"Item-use clip SENT: recess EMPTY [{arcNow}] — record 26 omitted; {why}. Peers " +
                  "bring their slab home (into the arc while it is up, into their items stack while " +
                  "it is not — RemoteItemFan.TickDetachedRecess)."
                : $"Item-use clip SENT: fan position {itemClip} [{arcNow}] lies in our item-use recess " +
                  $"— {why}. Extension record 26, ONE index byte and NO item identity. Peers " +
                  "re-parent that same slab onto their mirrored recess and replay the 0.28 s settle " +
                  "from this edge, so the card is seen ARRIVING rather than teleporting.");
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
            // BYTE 2 WAS SAMPLED, LOGGED — AND NEVER COPIED OUT. This line is the whole of two
            // hardware defects.
            //
            // The sampler above packs the cap-state byte into bits 16..23 of boardUiNow and the
            // "Board UI SENT" diagnostic prints it ("CAP STATES (byte 2) = 0x…", "ITEM-PILE CUE =
            // BEATING") — the ModBuild 137 logs show 0x14 / 0x41 / 0x80 / 0xC0 going past that
            // line on both machines. But the only two bytes ever handed to PresenceState were 0 and
            // 1, so the serializer (PresenceState: "[buttons][overlays][cap states]") wrote a THIRD
            // byte of 0 on every packet, with the record LENGTH still saying "byte 2 is valid".
            // Both receivers therefore latched HasCapStates = true, mask = 0x00, once, and never
            // logged a change again — exactly what the two ModBuild-137 logs show, symmetrically:
            //   "Cap states RECEIVED from player 2: 0x00"   (host, one line, whole session)
            //   "Cap states RECEIVED from player 1: 0x00"   (peer, one line, whole session)
            // A DIAGNOSTIC THAT READS THE SAMPLE RATHER THAN THE PACKET CANNOT SEE THIS — the send
            // line was truthful about what was measured and silent about what was transmitted.
            //
            // What the missing byte cost, both user-reported this round:
            //   • bit 7 = the closed items pile's "something in here is playable" heartbeat. The
            //     receiver's ItemsPileUsableCue could only ever be false, so RemoteControlBoard's
            //     ember/ring emitters were never asked to run — "Die Item-Animation auf dem Pile …
            //     wird nicht synchronisiert". The mirror code for it has shipped since ModBuild 121
            //     and had simply never been reachable.
            //   • bits 0..6 = the cap STATE colours. mask 0x00 reads as "skip DISABLED, rests
            //     DISABLED, confirm un-accented", so every mirrored SKIP cap was painted through
            //     the disabled path — wood-lerped face and label alpha 0.35 — while the owner's was
            //     enabled at alpha 1. That is "die Überspringen Knöpfe … der Text ist etwas
            //     transparenter" ("the Skip buttons … the text is somewhat more transparent"),
            //     measured: 0.35 vs 1.0.
            extras.BoardCapStateMask =
                (byte)((boardUiNow >> 16) & NetProtocol.BoardUiCapStateDefinedMask);
        }
        // SLOT-CARD SIZE (extension record 11): written only while a live tray exists AND either
        // width differs from the legacy assumption every pre-record receiver hardcodes
        // (NetProtocol.SlotCardWidthLegacy = 82.55 mm). At the SHIPPED defaults it always differs
        // — SlotOverlayScale defaults to 1.9 on Oak/Steel and 1.65 on Bronze, so an untuned player's
        // card renders at ~157 mm (Bronze ~136 mm) while every peer used to draw 82.55 mm; that
        // near-2x gap is the reported defect. A sender whose
        // config lands exactly on the legacy constant omits the record and stays byte-identical
        // to the previous build.
        ushort legacyCode = NetProtocol.EncodeSlotWidth(NetProtocol.SlotCardWidthLegacy);
        if (trayNow != null && (slotFrameCode != legacyCode || slotCardCode != legacyCode))
        {
            extras.HasSlotCardSize = true;
            extras.SlotFrameWidthCode = slotFrameCode;
            extras.SlotCardWidthCode = slotCardCode;
        }
        if (slotCardSizeChanged)
        {
            _lastSentSlotCardSize = slotCardSizeNow;
            VRLog.Info("Net", trayNow == null
                ? "Slot-card size SENT: no live tray — record omitted."
                : $"Slot-card size SENT: frame {slotFrameCode / 10f:0.0} mm, card " +
                  $"{slotCardCode / 10f:0.0} mm (board-local tenth-mm, extension record 11) — " +
                  (extras.HasSlotCardSize
                      ? "peers render our slot cards at exactly this size (1:1 rule)."
                      : "equals the legacy 82.55 mm assumption, record omitted (identical render)."));
        }
        // PICK BANNER (extension record 7): the placard line above the owner's board, so a peer's
        // remote board carries the same sentence at the same seat. Written only while a placard is
        // really shown — an idle packet stays byte-identical to the previous build's.
        //
        // ─── THIS CHANNEL IS UNMASKED, AND THE SENTENCE BELOW USED TO ASSERT IT WAS SAFE ─────────
        // Record 7 is the only one of this driver's five string channels that goes out neither
        // masked (DecisionLinesText and the two cap labels take Net.DecisionLabelMask) nor
        // identity-gated at its source (BoardTooltipText at WorldTooltips.ContentPublicToPeers,
        // DecisionNamesText in DamageTooltipSurface). Its send log said "an actor and a count, NO
        // card identity" — which is the IDENTICAL sentence record 12's send log made, in the
        // identical place, about a string it does not inspect, immediately before the ModBuild 477
        // identity leak. The claim is now a statement about the four composers rather than about
        // this string, because that is the only thing this site can honestly say:
        //   * PlayTray.SetPickStatus has FOUR composers, all in Cards/Driver/CardsDriver.6.Flows.cs
        //     (:740 item demand, :821 floating panel, :1048 card pick, :1247 board exhausted) plus
        //     WorldUI/Surfaces/UseBarsSurface.cs:1946, and each was read for what it CONCATENATES:
        //     an actor label, a verb, counts, and Core.Loc keys.
        //   * The one that quotes a game string is the panel composer at :821, whose title comes
        //     from CardsGameApi.DistributePanelState. Its comment used to concede the title could be
        //     "the redistribute card's name"; that was checked against the decompiled game and is
        //     false — see the derivation at that site, which names the eight GetTitleText bodies and
        //     the two Choreographer call sites that can reach them.
        // SO: A CARD NAME MAY NOT BE PUT ON THIS CHANNEL. If a composer ever needs to quote one, it
        // gets Net.DecisionLabelMask like the other four, and this paragraph is what says so.
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
                  $"{NetProtocol.PickBannerTextMaxBytes} B). UNMASKED CHANNEL: this line quotes the " +
                  "banner VERBATIM precisely because nothing inspects it on the way out — the five " +
                  "composers are the guarantee (see the block comment at the send site), not this " +
                  "sentence. A card NAME appearing in the quoted text above is therefore a finding " +
                  "and not a footnote; it is the ModBuild 477 shape, and the remedy is " +
                  "Net.DecisionLabelMask at the composer. Peers show it on the remote board at the " +
                  "same board-local seat.");
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
        // DECISION LINES (extension record 12): written on every packet WHILE a decision row is
        // docked; omitted otherwise, so an idle packet stays byte-identical to the previous
        // build's. The sampler already excluded everything that is not a pressable widget label.
        //
        // ─── A LABEL CAN CONTAIN A CARD NAME, AND THE NAME IS NOW TAKEN OUT AT THE SAMPLER ───────
        // The note here and the log line below both used to read "pressable-widget labels only, NO
        // card identity". The first half is a true statement about what the SAMPLER SELECTS; the
        // second was read as a statement about what those labels CONTAIN, and it is false. The
        // 2026-09-05 session put `<sprite name="Lost"> Verbrennen "In die Nacht"` on the wire from
        // both clients — the confirm button of a burn prompt, which names the card being burnt, in
        // plain text, with no RevealGate term anywhere on this path.
        //
        // THAT WAS A RULED EXEMPTION FOR TWO BUILDS AND IS NOW A RULED MASK. User, 2026-09-07:
        // "wegen dem Anti-Cheat-System in der Auswahlphase muss hier ein genehmigte Ausnahme der
        // 1:1 Regel greifen, der Name der Karte in dem Dialog im remote board muss ausgeblendet
        // werden." WorldUI.Surfaces.DecisionDockSurface's sampler now runs every label through
        // Net.DecisionLabelMask BEFORE it becomes this string, so a covered card's identity does not
        // reach this line, this record, or the wire — see DecisionLabelMask for why masking at the
        // sender is the only place that makes the claim true, and RevealGate.PeerCardPopulation
        // .DecisionRowWording for the ruling. WHAT THE MASK OWES IN RETURN IS STILL A MEASUREMENT
        // rather than a promise, and it is now TWO lines: the mask's own DECISION LABEL MASK line
        // (what it replaced) and the line below (what actually went out, quoted whole).
        if (!string.IsNullOrEmpty(decisionNow))
        {
            extras.HasDecisionLines = true;
            extras.DecisionLinesText = decisionNow;
        }
        // …and SAY SO whenever a label travels inside the window the backs-only rule exists for.
        // Change-gated on the label itself, so a prompt that stands for ten seconds prints once.
        if (decisionChanged && !string.IsNullOrEmpty(decisionNow)
            && !RevealGate.PeersSeeOurCardFronts)
        {
            // HW-VERIFY: grep token DECISION LABEL INSIDE THE SECRET WINDOW. This line exists
            // because an exemption nobody can see is indistinguishable from a leak, and because the
            // claim it replaces ("NO card identity") was an assertion the instrument could not make.
            // READ IT LIKE THIS, AND THE READING INVERTED ON 2026-09-07: the quoted text is what
            // LEFT this client, after Net.DecisionLabelMask ran. ANY card name still in it is a
            // DEFECT now, not an exemption. The expected reading is a wording carrying the
            // in-world sealed-card phrase in place of the name.
            //
            // AND THE FIRST GUESS AT *WHY* A NAME WOULD SURVIVE WAS WRONG, so it is corrected here
            // rather than left to be re-guessed. This comment used to say "the mask walks the hand,
            // discard and round lists of every character we control, so a name that survives means
            // it came from a list the mask does not walk". It did not: on 2026-09-07 (report item
            // 7, the short rest) the card WAS in a walked list and the mask still missed it,
            // because it compared the wrong STRING FORM — CAbilityCard.Name is the YML/localization
            // KEY (ABILITY_CARD_SpareDagger) and the wording carries the TRANSLATED title
            // ("Zusatzdolch"). Check the FORM before the LIST.
            //
            // THE FALSIFIER: this line never appearing at all does NOT mean nothing travels — it
            // means no decision row was docked during a selection phase this session, so the
            // question was not put. And a card name here is the ONLY convicting reading: the same
            // name inside 'Decision lines SENT' or 'Cap labels SENT' without this line beside it is
            // the ACTION phase, where every peer already draws our fronts by ruling.
            VRLog.Note("Net", "DECISION LABEL INSIDE THE SECRET WINDOW: record 12 is publishing "
                + $"\"{decisionNow!.Replace('\n', '|')}\" while RevealGate.PeersSeeOurCardFronts is "
                + "SHUT — i.e. online, during the game's own SelectAbilityCardsOrLongRest phase, the "
                + "window in which every OTHER surface in this mod refuses a peer a card front. This "
                + "record carries the wording of a pressable widget, and a burn prompt's wording "
                + "NAMES THE CARD; the record's own note used to claim it never carried a card "
                + "identity and that claim was false. THE ROW is a ruled exemption; THE NAME INSIDE "
                + "IT IS NOT, and that half changed on 2026-09-07 (\"Kurze Rast = Auswahlphase = "
                + "verdeckt\") — see RevealGate.PeerCardPopulation.DecisionRowWording. What is "
                + "printed above is the whole of what went out, so this line is checkable and the "
                + "check is now simple: A CARD NAME QUOTED HERE IS THE DEFECT. Net.DecisionLabelMask "
                + "should have replaced it with the in-world sealed-card wording. It failed to for "
                + "one whole session because it searched for CAbilityCard.Name — the YML KEY "
                + "'ABILITY_CARD_SpareDagger' — inside a wording that carries the TRANSLATED title "
                + "'Zusatzdolch', so read the STRING FORM before suspecting the pile or the phase. "
                + "The superseded ruling this line used to quote (2026-09-05 item 15, \"he wants to "
                + "see which card is at stake\") is dead INSIDE the window and alive outside it: a "
                + "burn prompt in the action phase names its card on purpose, and this line does not "
                + "print there.");
        }
        // DECISION NAMES (extension record 33): the card-name KEYS the mandatory-use hint is
        // prefixed with. The sampler fills this ONLY for that one text variant and ONLY while
        // RevealGate.PeersSeeOurCardFronts is open, so it is null on essentially every packet and
        // costs nothing — see DamageTooltipSurface.SampleMandatoryNames for both gates.
        string? decisionNames = WorldUI.Surfaces.DamageTooltipSurface.WireMandatoryNames;
        if (!string.IsNullOrEmpty(decisionNames))
        {
            extras.HasDecisionNames = true;
            extras.DecisionNamesText = decisionNames;
        }
        if (decisionChanged)
        {
            _lastSentDecisionLines = decisionNow;
            VRLog.Info("Net", string.IsNullOrEmpty(decisionNow)
                ? "Decision lines SENT: row undocked or hidden for another character's focus — " +
                  "record omitted (peers drop the mirrored buttons, exactly as the owner's own " +
                  "board drops them)."
                : $"Decision lines SENT: \"{decisionNow!.Replace('\n', '|')}\" — extension record 12 " +
                  $"(UTF8, capped {NetProtocol.DecisionLinesMaxBytes} B: the labels of pressable " +
                  "widgets ONLY, never a dialog's description text — and a label CAN name a card, " +
                  "which a burn prompt's does, so the identity is stripped at the sampler by " +
                  "Net.DecisionLabelMask while the face rule says that card is covered; what is " +
                  "quoted above is the masked string that actually went out. See RevealGate" +
                  ".PeerCardPopulation.DecisionRowWording for the ruling, 'DECISION LABEL MASK' for "
                  + "what was replaced and 'DECISION LABEL INSIDE THE SECRET WINDOW' for the "
                  + "measurement); " +
                  "peers render them as inert plates at their copy's decision seat.");
        }
        // DECISION STATE (extension record 24, NetProtocol.ExtIdDecisionState — the two log lines
        // below still say "record 23"; that wording is a shipped grep token, the ID they name is
        // wrong): written on exactly the gate record 12 rides, so a peer can never hold states for
        // a row whose wordings it does not have (or the reverse).
        if (decisionStateNow >= 0)
        {
            extras.HasDecisionState = true;
            extras.DecisionPromptKind = decisionKind;
            extras.DecisionTextVariant = decisionText;
            extras.DecisionOptionCount = decisionOptions;
            extras.DecisionOptionFlags = _decisionOptionSample;
        }
        if (decisionStateChanged)
        {
            _lastSentDecisionState = decisionStateNow;
            if (decisionStateNow < 0)
            {
                VRLog.Info("Net", "Decision state SENT: no visible decision row — record 23 omitted " +
                                  "(peers drop the prompt text and the option states with the plates).");
            }
            else
            {
                // The SENDER's rendering of the run and the RECEIVER's are one expression now
                // (NetProtocol.DescribeDecisionOptionStates) — the two lines exist to be diffed
                // against each other, so two spellings would make the diff a guess.
                string opts = NetProtocol.DescribeDecisionOptionStates(_decisionOptionSample,
                                                                       decisionOptions);
                VRLog.Info("Net", $"Decision state SENT: prompt kind {decisionKind}, text variant " +
                                  $"{decisionText}, {decisionOptions} option(s) [{opts}] — extension " +
                                  "record 23 (flags + one byte per option, index-aligned with record " +
                                  "12's lines). The prompt TEXT itself is NOT on the wire: peers " +
                                  "compose the same line from their own localization, so the " +
                                  "mandatory-use variant's active-bonus CARD NAMES never travel.");
            }
        }
        // DECISION WIDGETS (extension record 29): written on exactly the gate records 12 and 24
        // ride, so a peer can never hold roles for a row whose wordings or states it does not have.
        // This is the record that turns a peer's mirrored decision from a mod-drawn description
        // into the GAME's own widgets — see NetProtocol.ExtIdDecisionWidgets.
        if (decisionWidgetNow >= 0)
        {
            extras.HasDecisionWidgets = true;
            extras.DecisionWidgetFlags = decisionWidgetFlags;
            extras.DecisionDamageAmount = decisionDamage;
            extras.DecisionRoleCount = decisionRoles;
            extras.DecisionRoles = _decisionRoleSample;
        }
        if (decisionWidgetChanged)
        {
            _lastSentDecisionWidgets = decisionWidgetNow;
            if (decisionWidgetNow < 0)
            {
                VRLog.Info("Net", "Decision widgets SENT: no visible decision row — record 29 omitted " +
                                  "(peers drop the mirrored game widgets with the plates).");
            }
            else
            {
                // Same expression as the receiver's line, for the same reason as the option states
                // above: these two lines exist to be diffed against each other.
                string roles = NetProtocol.DescribeDecisionRoles(_decisionRoleSample, decisionRoles);
                VRLog.Info("Net", $"Decision widgets SENT: {decisionRoles} role(s) [{roles}], damage " +
                                  $"{((decisionWidgetFlags & NetProtocol.DecisionWidgetDamageValidBit) != 0 ? decisionDamage.ToString() : "n/a")}" +
                                  $", flags 0x{decisionWidgetFlags:X2} — extension record 29 " +
                                  "([flags][damage][n][n × role], index-aligned with records 12 and " +
                                  "24). A ROLE NAMES A GAME WIDGET, never a card: every client owns " +
                                  "the same TakeDamagePanel prefab (the game raises it on all of " +
                                  "them and calls ShowOtherPlayer on the non-deciding ones), so a " +
                                  "peer clones the REAL button — its art, its icons, its wording in " +
                                  "THEIR OWN language — instead of drawing a lookalike from our text.");
            }
        }
        // USE BARS (extension record 25): written on every packet while at least one bar is docked
        // AND visible on the owner's board; omitted otherwise, so an idle packet stays
        // byte-identical to the previous build's and a peer's mirrored drawer empties in the same
        // frames the owner's does.
        if (useBarMask != 0)
        {
            extras.HasUseBars = true;
            extras.UseBarsMask = useBarMask;
            extras.UseBarFlags = _useBarFlagsSample;
            extras.UseBarSlotCounts = _useBarCountSample;
            extras.UseBarSlotStates = _useBarSlotSample;
            // RECORD 45 RIDES ONLY WITH RECORD 25, and only when something is actually
            // identifiable. The writer drops it to nothing when every id is NoIdentity, so a
            // drawer of abilities/augment slots — and every drawer before this build — puts not one
            // byte on the wire.
            extras.HasUseBarSlotIds = true;
            extras.UseBarSlotIds = _useBarIdSample;
        }
        if (useBarsChanged)
        {
            _lastSentUseBarMask = useBarMask;
            for (int b = 0; b < NetProtocol.UseBarsCount; b++)
            {
                _lastSentUseBarFlags[b] = useBarMask != 0 ? _useBarFlagsSample[b] : (byte)0;
                _lastSentUseBarCounts[b] = useBarMask != 0 ? _useBarCountSample[b] : (byte)0;
            }
            for (int s = 0; s < _lastSentUseBarSlots.Length; s++)
                _lastSentUseBarSlots[s] = useBarMask != 0 ? _useBarSlotSample[s] : (byte)0;
            for (int s = 0; s < _lastSentUseBarIds.Length; s++)
                _lastSentUseBarIds[s] = useBarMask != 0
                    ? _useBarIdSample[s] : UseBarSlotIdentity.NoIdentity;

            if (useBarMask == 0)
            {
                VRLog.Info("Net", "Use bars SENT: no bar docked and visible — record 25 omitted " +
                                  "(peers drop the mirrored bar drawer, including when the bars are " +
                                  "still docked but render-hidden because the owner is looking at " +
                                  "another character).");
            }
            else
            {
                var bars = new System.Text.StringBuilder(96);
                int totalSlots = 0;
                for (int b = 0; b < NetProtocol.UseBarsCount; b++)
                {
                    if ((useBarMask & (1 << b)) == 0)
                        continue;
                    if (bars.Length > 0)
                        bars.Append("; ");
                    bars.Append(UseBarName(b)).Append('=').Append(_useBarCountSample[b])
                        .Append(" slot(s)");
                    totalSlots += _useBarCountSample[b];
                    byte f = _useBarFlagsSample[b];
                    if ((f & NetProtocol.UseBarElementPickerBit) != 0)
                        bars.Append(" +element picker OPEN");
                    if ((f & NetProtocol.UseBarOptionPickerBit) != 0)
                        bars.Append(" +option picker OPEN");
                    int at = b * NetProtocol.UseBarsMaxSlots;
                    for (int s = 0; s < _useBarCountSample[b]; s++)
                    {
                        byte st = _useBarSlotSample[at + s];
                        bars.Append(" #").Append(s).Append('=')
                            .Append((st & NetProtocol.UseSlotOfferedBit) != 0 ? "OFFERED" : "greyed");
                        if ((st & NetProtocol.UseSlotDimmedBit) != 0)
                            bars.Append("+dim");
                        if ((st & NetProtocol.UseSlotChosenBit) != 0)
                            bars.Append("+CHOSEN");
                    }
                }
                VRLog.Info("Net", $"Use bars SENT: mask 0x{useBarMask:X2}, {totalSlots} slot(s) " +
                                  $"[{bars}] — extension record 25 (bar mask + per bar: sub-picker " +
                                  "flags, slot count, one state byte per slot). NO slot identity is " +
                                  "on the wire: the game's use slots carry no label at all, only card " +
                                  "ART, so peers caption each bar from the BAR BIT and draw anonymous " +
                                  "state-painted tiles.");
            }
        }
        // CAP LABELS (extension record 13): written on every packet while a confirm/skip control
        // is shown with a known label; omitted otherwise (peers fall back to the neutral wording,
        // which is also what pre-record peers render).
        if (!string.IsNullOrEmpty(confirmLabelNow))
        {
            extras.HasConfirmCapLabel = true;
            extras.ConfirmCapLabel = confirmLabelNow;
        }
        if (!string.IsNullOrEmpty(skipLabelNow))
        {
            extras.HasSkipCapLabel = true;
            extras.SkipCapLabel = skipLabelNow;
        }
        if (!string.IsNullOrEmpty(undoLabelNow))
        {
            extras.HasUndoCapLabel = true;
            extras.UndoCapLabel = undoLabelNow;
        }
        if (!string.IsNullOrEmpty(itemUseLabelNow))
        {
            extras.HasItemUseCapLabel = true;
            extras.ItemUseCapLabel = itemUseLabelNow;
        }
        if (capLabelsChanged)
        {
            _lastSentConfirmLabel = confirmLabelNow;
            _lastSentSkipLabel = skipLabelNow;
            _lastSentUndoLabel = undoLabelNow;
            _lastSentItemUseLabel = itemUseLabelNow;
            // Single quotes around each wording: a nested escaped quote inside an interpolation
            // hole trips the patch-inventory source scanner (the StateLine rule).
            string Cap(string? v) => string.IsNullOrEmpty(v) ? "<hidden>" : "'" + v + "'";
            VRLog.Info("Net", $"Cap labels SENT: confirm={Cap(confirmLabelNow)}, " +
                              $"skip={Cap(skipLabelNow)}, undo={Cap(undoLabelNow)}, " +
                              $"itemUse={Cap(itemUseLabelNow)} — " +
                              $"extension record 13 (UTF8, {NetProtocol.CapLabelMaxBytes} B cap per label, " +
                              "sender language verbatim); peers letter their mirrored caps with " +
                              "EXACTLY these words instead of a re-localized GUI_CONFIRM / " +
                              "GUI_SKIP_MOVEMENT / GUI_UNDO / GUI_USE. The UNDO and item-USE slots " +
                              "(mask bits 2/3) are new this build: they carry the pick flow's " +
                              "dialog-CANCEL wording and an item-SURRENDER demand's wording, both of " +
                              "which used to read as a plain undo / 'USE' on every peer's board. " +
                              "itemUse is the whole ITEM-USE AREA's wording (cap AND the caption " +
                              "engraved under the recess), so it is present for the whole demand " +
                              "and not only while the cap is up. CONFIRM AND UNDO ARE IDENTITY-" +
                              "MASKED (2026-09-07): during a modal card pick both fall through to " +
                              "the live DialogPopup option wording, which for a burn prompt NAMES " +
                              "the card, so Net.DecisionLabelMask strips the identity while the " +
                              "face rule says that card is covered — what is quoted above is the " +
                              "masked text that actually went out. A label the mask had to " +
                              "withhold is sent as <hidden>, which peers letter with their own " +
                              "GUI_CONFIRM / GUI_UNDO. READ A CARD NAME IN THIS LINE AGAINST THE " +
                              "PHASE AND NOT ON ITS OWN, and that correction cost the 2026-09-07 " +
                              "round a wrong diagnosis: this string used to end 'a card name still " +
                              "in it is a DEFECT', which is true only INSIDE the secret window. " +
                              "Outside it every peer is already drawing our fronts (the census " +
                              "prints POLICY=FRONTS EVERYWHERE), so a burn prompt naming the card " +
                              "is 1:1 and is what the ruling ASKS for — a take-damage burn or a " +
                              "long rest lands there. The reading that convicts is the sender's " +
                              "own 'DECISION LABEL INSIDE THE SECRET WINDOW' line, which prints " +
                              "only while RevealGate.PeersSeeOurCardFronts is SHUT; a card name " +
                              "quoted THERE is the defect. And the mask having nothing to do looks " +
                              "identical to the mask being broken, so the third reading is " +
                              "'DECISION LABEL MASK' itself: zero of those lines across a whole " +
                              "session with a burn prompt inside the window is what a dead mask " +
                              "reads like, and is exactly how item 7 shipped.");
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
                                  "card identity rides this wire. " +
                                  $"CAP STATES (byte 2, new this build) = 0x{(boardUiNow >> 16) & 0xFF:X2} — " +
                                  $"confirm={DescribeConfirmCapState(boardUiNow)}, " +
                                  $"shortRest={DescribeRestCapState(boardUiNow, NetProtocol.BoardUiCapShortRestEnabledBit, NetProtocol.BoardUiCapShortRestAccentBit)}, " +
                                  $"longRest={DescribeRestCapState(boardUiNow, NetProtocol.BoardUiCapLongRestEnabledBit, NetProtocol.BoardUiCapLongRestAccentBit)}, " +
                                  $"skip={(((boardUiNow >> 16) & NetProtocol.BoardUiCapSkipEnabledBit) != 0 ? "enabled" : "DIMMED")} — " +
                                  "peers paint their mirrored caps in exactly these state colours " +
                                  "instead of the single colour the cap was built with. " +
                                  $"ITEM-PILE CUE (byte 2 bit 7, new this build) = " +
                                  $"{(((boardUiNow >> 16) & NetProtocol.BoardUiCapItemPileUsableBit) != 0 ? "BEATING" : "off")}" +
                                  " — the closed items stack's ember puffs and outward rings now " +
                                  "run on every peer's copy too, on their own clock from this " +
                                  "bit's edges. It is the last free bit in record 4.");
            }
            else
            {
                VRLog.Info("Net", "Board UI SENT: no live tray — record omitted (peers keep the last board state).");
            }
        }
        if (boardSnapChanged && (boardUiNow >= 0 || _lastSentBoardUi >= 0))
        {
            int snapValue = boardUiNow >= 0
                ? (boardUiNow >> (8 + NetProtocol.BoardUiSnapShift)) & 0x03
                : NetProtocol.BoardUiSnapNone;
            int snapSlot = NetProtocol.DecodeSnapSlot(snapValue);
            VRLog.Info("Net", snapSlot >= 0
                ? $"Snap-glow hover SENT: recess {snapSlot + 1} — board-UI record byte 1 bits 6..7, " +
                  "ZERO extra bytes. Peers light the gold snap rim on the SAME recess at the same " +
                  "moment the owner does, i.e. BEFORE the drop, which is what the telegraph is for; " +
                  "the old occupancy-edge flash fired after it, and not at all for a hover that " +
                  "ended without a drop. A recess POSITION, no card identity."
                : "Snap-glow hover SENT: none — peers clear the gold rim (the held card left snap " +
                  "range, or the drop committed).");
        }
        _lastSentBoardUi = boardUiNow;

        // CAP PRESS (extension record 14 byte 0 bits 3..7): written while a press is in its hold
        // window; absent otherwise, so an idle packet stays byte-identical to the previous build's.
        if (capPress)
        {
            extras.HasCapPress = true;
            extras.CapPressCap = capPressCap;
            extras.CapPressSeq = capPressSeq;
        }
        if (capPressChanged)
        {
            _lastSentCapPress = capPressNow;
            VRLog.Info("Net", $"Cap press SENT: wire cap {capPressCap} (sequence {capPressSeq}) — " +
                              "extension record 14 byte 0 bits 3..7, ZERO extra bytes. The edge " +
                              "PRE-EMPTED the extras gate, so the mirrored cap on every peer's copy " +
                              "of this board sinks and springs back WITH the press. An ANIMATION " +
                              "only: their copy stays colliderless and registered nowhere.");
        }

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
                              $"board fan index {(fanHl >= 0 ? fanHl.ToString() : "none")} (source: {fanHlSource}; " +
                              "hand-sweep AND laser hover both feed this) — " +
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

        // HALF HOVER + SELECTION (extension record 14): written while a half is hovered OR
        // clicked — "absent" and "nothing lit, nothing selected" render identically, so an idle
        // packet stays byte-identical.
        if (halfHover || halfSel0 != NetProtocol.HalfSelectNone
                      || halfSel1 != NetProtocol.HalfSelectNone)
        {
            extras.HasHalfHover = true;
            extras.HalfHoverActive = halfHover;
            extras.HalfHoverSlot = halfHover
                ? (byte)Mathf.Clamp(halfSlot, 0, NetProtocol.BoardUiSlotCount - 1)
                : (byte)0;
            extras.HalfHoverTop = halfHover && halfTop;
            extras.HalfSelect0 = (byte)halfSel0;
            extras.HalfSelect1 = (byte)halfSel1;
            // BYTE 2 (item 6): is each of those regions the half's STANDARD-ACTION chip? All three
            // false ⇒ the writer omits the byte entirely and the record stays two bytes long.
            extras.HalfHoverDefault = halfHoverDef;
            extras.HalfSelect0Default = halfSel0Def;
            extras.HalfSelect1Default = halfSel1Def;
        }
        // ROUND-CARD SLOT ORDER (extension record 18): written on every extras packet while we can
        // really answer, omitted otherwise — a peer that hears nothing keeps its own derivation.
        if (slotOrder)
        {
            extras.HasSlotOrder = true;
            extras.SlotOrderSwapped = slotOrderSwapped;
        }
        if (slotOrderChanged)
        {
            _lastSentSlotOrder = slotOrderNow;
            // HW-VERIFY: report item 4 (2026-09-06). Grep token: Slot order SENT.
            //
            // PROMOTED TO Note AND GIVEN THE TWO CARD NAMES because the BIT alone cannot answer the
            // question the co-player asked. He reported that after confirming he had the wrong card
            // as his initiative, one he had not laid on the left. This line printed
            // "LEFT recess holds the INITIATIVE round card" for all three of his turns in the
            // 2026-09-06 session and could not have printed anything else: CardsDriver
            // .ReconcileInitiative drives the GAME's initiative to follow recess 0, so the bit
            // agrees with itself no matter how the card got there. THE NAMES ARE THE FALSIFIER —
            // he knows which card he meant to put on the left, and this now says which one is
            // actually in it. Nothing new is on the wire; record 18 is byte-identical.
            VRLog.Note("Net", slotOrder
                ? $"Slot order SENT: LEFT recess (slot 1) holds {slotLeftName} and RIGHT recess "
                  + $"(slot 2) holds {slotRightName}; the left one is the "
                  + $"{(slotOrderSwapped ? "NON-INITIATIVE" : "INITIATIVE")} round card, RIGHT " +
                  $"recess (slot 2) the other — extension record 18, one bit against " +
                  "CCharacterClass.InitiativeAbilityCard (a replicated reference every client " +
                  "resolves to the same card, so this is an ORDER and not an identity). Peers stop " +
                  "re-deriving the pair's left/right from their own CardsHandUI.cardsUI sort, which " +
                  "is what put the two cards the wrong way round on somebody else's screen. THE "
                  + "TWO NAMES ARE THE POINT (report item 4, 2026-09-06): the bit cannot tell a "
                  + "card the PLAYER dropped in the left recess from one something else seated "
                  + "there, because ReconcileInitiative then makes the game's initiative follow "
                  + "whichever it is. Compare the left name with the card the player believes he "
                  + "laid down; a mismatch is the defect and this line is the only place it shows."
                : $"Slot order SENT: none — record omitted (recesses hold {slotLeftName} / "
                  + $"{slotRightName}: fewer than two resolvable round cards, or neither is the "
                  + "initiative card). Peers keep their own initiative-first derivation, exactly "
                  + "as before this record existed.");
        }
        // EMPTY-FAN PLACARD (record 14 byte 1 bit 4): the same record, one bit. Setting it also
        // OPENS record 14 when nothing is hovered or selected — that is the write gate's third
        // arm, and it is why the placard travels at all (it is hand-anchored, so no board record
        // could carry it).
        extras.EmptyFanHint = emptyFanHintNow;
        if (emptyFanHintChanged)
        {
            _lastSentEmptyFanHint = emptyFanHintNow;
            VRLog.Info("Net", emptyFanHintNow
                ? "Empty-fan placard SENT: our palm gate opened on a hand the GAME model says is " +
                  "empty, so the \"Keine Handkarten\" plate is up — extension record 14, byte 1 " +
                  "bit 4 (ONE bit, no payload; a real edge, never inferred from the hand-card " +
                  "count, which cannot tell an empty hand from a closed fan). The edge PRE-EMPTED " +
                  "the extras gate, so peers start their own 1.5 s fade with the gesture."
                : "Empty-fan placard SENT: down — bit clear (peers end their mirrored plate).");
        }

        // BOARD TUNING (extension record 28): only the dials we have MOVED, and only ONE PAGE of
        // them per packet — the pager cycles through the pages forever so that a peer who joins
        // mid-cycle, or who loses a packet, converges on the next pass with no handshake at all
        // (see BoardTunePages for the convergence guarantee this implements). A zero-length sample
        // means every dial is at its shipped default, and then no record is written at all — the
        // receiver's own compiled constants are already the right answer, byte-identically to every
        // build before this record.
        int tuningPage = _tuningPager.NextPage(_tuningPageBuffer);
        if (tuningPage >= NetProtocol.BoardTuneMinRecordBytes)
        {
            extras.HasBoardTuning = true;
            extras.BoardTuningBytes = _tuningPageBuffer;
            extras.BoardTuningLength = tuningPage;
        }
        if (tuningChanged)
        {
            if (_tuningPager.Overflowed)
            {
                // THE ONE FAILURE THIS DESIGN REFUSES TO HAVE QUIETLY. Paging removed the capacity
                // ceiling, so the only way here is a malformed field list (an id outside the
                // declared ranges). Say so at ERROR: a dial that cannot ride is a broken 1:1
                // guarantee, and the whole point of the rework is that it can never be silent.
                VRLog.Error("Net", "Board tuning CANNOT BE SENT: the sampled field list is malformed " +
                                   $"({_tuningBuffer.Length}-byte scratch, {tuningLength} bytes sampled) — " +
                                   "an id outside NetProtocol's declared Tune* ranges, or one repeated. " +
                                   "NOTHING is sent rather than something torn, so peers draw this board at " +
                                   "the shipped defaults. See NetProtocol.BoardTuneMaxFields.");
            }
            else
            {
                VRLog.Info("Net", _tuningPager.PageCount > 0
                    ? $"Board tuning SENT: {_tuningPager.FieldCount} dial(s) differ from the shipped " +
                      $"'{tuningBoard}' defaults — extension record 28, {_tuningPager.FieldBytes} field " +
                      $"byte(s) across {_tuningPager.PageCount} page(s) " +
                      $"(generation {_tuningPager.Signature:X4}; [id][value] pairs in ascending id order, " +
                      "quantized to 0.1 mm / 0.001 / 0.01°). One page rides each extras packet and the " +
                      "cycle restarted at page 0 on this change, so every peer holds the complete new " +
                      $"picture within {_tuningPager.PageCount} packet(s) — at most " +
                      $"{_tuningPager.PageCount * 1000f / NetProtocol.ExtrasSendRateHz:0} ms — and until " +
                      "then keeps the last complete one rather than flickering to the defaults."
                    : $"Board tuning SENT: every dial is at the shipped '{tuningBoard}' " +
                      "default — record OMITTED entirely, so this packet is byte-identical to what " +
                      "previous builds emitted and peers use their own identical constants.");
            }
        }

        if (halfHoverChanged)
        {
            _lastSentHalfHover = halfHoverNow;
            // REGION, not just half (item 6): "TOP half" and "TOP standard-action field" are two
            // different rectangles on the same card, and the whole defect was that the wire could
            // only say the first. Both sides of the session now print the SAME two words, so the
            // owner's line and the observer's "Remote board half-hover" line compare without any
            // arithmetic.
            VRLog.Info("Net", halfHover
                ? $"Half hover SENT: slot {halfSlot + 1}, {(halfTop ? "TOP" : "BOTTOM")} " +
                  $"{(halfHoverDef ? "STANDARD-ACTION field" : "half")} — extension record 14 " +
                  $"byte 0{(halfHoverDef ? " + byte 2 bit 0" : string.Empty)} (a slot POSITION, a " +
                  "half and which of that half's two regions — no card identity); peers pulse the " +
                  "same region of the same docked round card."
                : "Half hover SENT: none (byte 0 sentinel / record omitted — peers clear the pulse).");
        }
        if (halfSelChanged)
        {
            _lastSentHalfSelect = halfSelNow;
            string Sel(int v, bool def) => v == NetProtocol.HalfSelectTop
                ? (def ? "TOP standard-action field" : "TOP half")
                : v == NetProtocol.HalfSelectBottom
                    ? (def ? "BOTTOM standard-action field" : "BOTTOM half")
                    : "none";
            VRLog.Info("Net", $"Half selection SENT: slot 1 = {Sel(halfSel0, halfSel0Def)}, " +
                              $"slot 2 = {Sel(halfSel1, halfSel1Def)} — extension record 14 byte 1 " +
                              "(the game's own per-half click latch, undo included) + byte 2 bits " +
                              "1..2 (which of the half's two regions that latch is on: isSelected " +
                              "= the big half, isSelectedDefaultAction = the chip). Positions " +
                              "only. The edge PRE-EMPTED the extras gate, so the steady highlight " +
                              "lands with the click on every peer's board.");
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

        // CHARACTER FOCUS (extension record 22): written only while a focus is actually known.
        // Actor id 0 is "none" everywhere, so a scenario-less / spectating client emits a packet
        // byte-identical to a pre-record sender.
        if (focusActorNow != 0)
        {
            extras.HasCharFocus = true;
            extras.CharFocusActorId = focusActorNow;
            extras.CharFocusOwnsAttention = focusOwnsAttentionNow;
            extras.CharFocusAttentionActorId = attentionActorNow;
        }
        if (focusChanged)
        {
            _lastSentFocusActor = focusActorNow;
            _lastSentFocusAttentionActor = attentionActorNow;
            _lastSentFocusOwnsAttention = focusOwnsAttentionNow;
            bool focusTail = focusOwnsAttentionNow && attentionActorNow != 0
                             && attentionActorNow != focusActorNow;
            VRLog.Info("Net", focusActorNow != 0
                ? $"Character focus SENT: looking at actor {focusActorNow}, " +
                  $"ownsAttention={focusOwnsAttentionNow}, game waiting on actor " +
                  $"{attentionActorNow} — extension record 22, " +
                  $"{(focusTail ? "9 B (bit0 + bit1: the attention actor is NOT the one we are " +
                                  "looking at, so its id rides the flag-guarded tail — this is the " +
                                  "'wrong character' state, and it is the case a receiver cannot " +
                                  "derive during a decision)"
                                : "5 B (bit0 only: the attention actor IS the one we are looking " +
                                  "at, so the focus id already names it and no tail is sent)")}. " +
                  "The mark peers draw is a pure function of these fields — no receiver re-derives " +
                  "it from its own turn read any more. NO card identity: peers colour an outline " +
                  "from this and read any card they draw from the host-replicated model through " +
                  "RevealGate, exactly as before."
                : "Character focus SENT: none — record omitted (peers show us no attention outline).");
        }

        // TRACK SELECTION (extension record 23): written only while our own track really shows a
        // selection frame, so an idle packet stays byte-identical to the previous build's.
        if (trackSelCount > 0)
        {
            extras.HasTrackSelection = true;
            extras.TrackSelectionCount = trackSelCount;
            extras.TrackSelectionIds = _trackSelectionSample;
        }
        if (trackSelChanged)
        {
            _lastSentTrackSelectionCount = trackSelCount;
            System.Array.Copy(_trackSelectionSample, _lastSentTrackSelection, trackSelCount);
            VRLog.Info("Net", trackSelCount > 0
                ? $"Track selection SENT: {trackSelCount} framed entr(y/ies) " +
                  $"[{DescribeIds(_trackSelectionSample, trackSelCount)}] — " +
                  "extension record 23 (the stable ActorGuid hashes of the entries OUR OWN track " +
                  "is framing with vanilla's selectionObject, read off the live widget's active " +
                  "flag rather than re-derived). PLAYERS, ENEMIES and OBJECTS alike — this is what " +
                  "lets a peer's mirrored track finally frame a selected MONSTER, which the " +
                  "character-focus record (22) structurally cannot name. The edge PRE-EMPTED the " +
                  "extras gate, so the frame lands with the click. NO card identity: a public " +
                  "track entry and nothing else; the initiative NUMBER inside the frame stays " +
                  "behind vanilla's own online gate on every client."
                : "Track selection SENT: none — record omitted (peers draw no selection frame on " +
                  "our mirrored track, which is exactly what our own track shows).");
        }

        // TRACK ORDER (extension record 27): written only inside vanilla's own per-viewer window
        // (online + card selection), so outside the selection phase an idle packet stays
        // byte-identical to the previous build's — and its ABSENCE is what releases a receiver's
        // order override back to the mirrored arrangement.
        if (trackOrderCount > 0)
        {
            extras.HasTrackOrder = true;
            extras.TrackOrderCount = trackOrderCount;
            extras.TrackOrderOwnedMask = trackOrderOwned;
            extras.TrackOrderIds = _trackOrderSample;
        }
        if (trackOrderChanged)
        {
            _lastSentTrackOrderCount = trackOrderCount;
            _lastSentTrackOrderOwned = trackOrderOwned;
            System.Array.Copy(_trackOrderSample, _lastSentTrackOrder, trackOrderCount);
            VRLog.Info("Net", trackOrderCount > 0
                ? $"Track order SENT: {trackOrderCount} player entr(y/ies) in OUR display order " +
                  $"[{DescribeIds(_trackOrderSample, trackOrderCount)}], owned mask 0x" +
                  $"{trackOrderOwned:X2} — extension record 27. Vanilla sorts player entries by " +
                  "IsUnderMyControl while online AND in the card-selection phase " +
                  "(InitiativeTrackActorBehaviour.cs:160-171), so OUR index 3 is a peer's index 5 " +
                  "and a mirrored clone of our widget was showing them OUR arrangement. The ids " +
                  "are read off the live widget's sibling order — the pixel, not a re-derivation " +
                  "of a sort whose comparator is inconsistent. The mask names the characters WE " +
                  "control (a local flag), which is also what re-decides the mirrored " +
                  "'still has to choose' ring; whether each has COMMITTED is NOT sent — the " +
                  "receiver reads that from the replicated RoundAbilityCards. NO card identity, " +
                  "and the initiative NUMBER stays behind vanilla's own online gate."
                : "Track order SENT: none — record omitted (outside the online card-selection " +
                  "phase every client's track sorts identically, so there is nothing per-viewer " +
                  "to carry and peers keep the mirrored arrangement).");
        }

        // WALL FADES (extension record 17): written only while the local decision fades at
        // least one wall; the receiver's [WallFade] SyncPeerFades decides application.
        if (wallFadeCount > 0)
        {
            extras.HasWallFades = true;
            extras.WallFadesCount = wallFadeCount;
            extras.WallFadesKeys = _wallFadeSample;
        }
        if (wallFadesChanged)
        {
            _lastSentWallFadeCount = wallFadeCount;
            System.Array.Copy(_wallFadeSample, _lastSentWallFades, wallFadeCount);
            VRLog.Info("Net",
                $"Wall fades SENT: {wallFadeCount} faded wall(s) — extension record 17 " +
                "(sorted cross-machine keys; peers with [WallFade] SyncPeerFades ON fade the " +
                "same walls with the same animation, dwell-free).");
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
        // redundancy on an unreliable stream (FfsNetTransport sends with canBeUnreliable: true, and
        // the reliable channel exists and is used where loss is unacceptable — see
        // EnemyInfoContinue). The receiver only plays on a sequence CHANGE, so a repeat is free.
        //
        // …AND THE SENTENCE THAT USED TO CLOSE THIS PARAGRAPH WAS FALSE. It read "a single lost
        // packet still lands within 200 ms", which holds only while nothing else is queued behind
        // the event. _lastFxEndpoints is a ONE-SLOT memory: the next dequeue overwrites it, so an
        // event that has already been superseded exists nowhere and a lost packet loses it for
        // good. A turn-clear ALWAYS dispatches two events into consecutive packets, and the FIRST
        // of that pair is the one the redundancy does not cover.
        //
        // MEASURED (ModBuild 461, both hardware logs): six [Cards] FLIGHT ORIGIN events on the
        // owner, four Remote card FX ... playing on the observer, zero SKIPPED, and in BOTH losses
        // it was the first of a pair. NOT FIXED HERE — every remedy changes how a shipped field is
        // consumed on an unreliable channel and risks a duplicate flight or one replayed late out
        // of an already-empty recess, which is report item 7's own symptom. The loss is now
        // Record62 now retains the last eight dispatches across coalescing and retransmission.
        // The original single-event prefix remains byte-for-byte compatible.
        if (NetCardFx.TryDequeue(out byte fxEndpoints, out byte fxSeq, out byte fxVisibilityFlags, out CardFlightSource? fxSource))
        {
            _lastFxEndpoints = fxEndpoints;
            _lastFxSeq = fxSeq;
            _lastFxVisibilityFlags = fxVisibilityFlags;
            _lastFxSource = fxSource;
            _hasFx = true;
        }
        extras.FlightHistory = NetCardFx.History;
        if (_hasFx)
        {
            extras.HasCardFx = true;
            extras.FxSeq = _lastFxSeq;
            extras.FxEndpoints = _lastFxEndpoints;
            extras.HasCardFxVisibility = true;
            extras.FxVisibilitySeq = _lastFxSeq;
            extras.FxVisibilityFlags = _lastFxVisibilityFlags;
            extras.FlightSource = _lastFxSource;
            extras.FlightSourceSeq = _lastFxSeq;
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

        // HELD PROPS (extension record 37): the map items in the player's hands, with the hand,
        // the pose and the measured held size of each. Written only while a prop is really held, so
        // an empty-handed player emits the exact bytes previous builds emitted.
        if (heldProp)
        {
            extras.HasHeldProp = true;
            extras.HeldPropId = heldPropId;
            extras.HeldPropPose.Position = propPos;
            extras.HeldPropPose.Rotation = propRot;
            extras.HeldPropLeftHand = propLeftHand;
            extras.HeldPropStretchCode = (ushort)propSizeCode;
            if (secondProp)
            {
                extras.HasSecondHeldProp = true;
                extras.SecondHeldPropId = secondPropId;
                extras.SecondHeldPropPose.Position = secondPropPos;
                extras.SecondHeldPropPose.Rotation = secondPropRot;
                extras.SecondHeldPropLeftHand = secondPropLeftHand;
                extras.SecondHeldPropStretchCode = (ushort)secondPropSizeCode;
            }
        }
        if (propChanged)
        {
            if (heldProp)
            {
                VRLog.Info("Net", $"Held prop SENT: prop {heldPropId} in the "
                    + $"{(propLeftHand ? "LEFT" : "RIGHT")} hand at "
                    + $"{propSizeCode / 1000f:0.###}x its board size"
                    + (secondProp
                        ? $", and prop {secondPropId} in the {(secondPropLeftHand ? "LEFT" : "RIGHT")} "
                          + $"hand at {secondPropSizeCode / 1000f:0.###}x"
                        : "")
                    + $" — extension record 37 ({(secondProp ? 2 : 1) * NetProtocol.HeldPropSlotBytes} B: "
                    + "hand + stable prop id + pose + held size, per slot). While it moves the extras "
                    + $"packet rides at {NetProtocol.SendRateHz:0} Hz, the SAME cadence a held figure "
                    + "gets, so peers see a carried chest move exactly like a carried mini.");
                _loggedHeldProp = true;
            }
            else if (_loggedHeldProp)
            {
                _loggedHeldProp = false;
                VRLog.Info("Net", $"Held prop SENT: released (was prop {_lastSentHeldPropId}) — "
                    + "record omitted; peers put that item back on its hex themselves, because "
                    + "nothing in the game ever re-authors a prop's transform.");
            }
        }
        _sentHeldPropValid = heldProp;
        _sentSecondPropValid = secondProp;
        _lastSentHeldPropId = heldProp ? heldPropId : 0;
        _lastSentSecondPropId = secondProp ? secondPropId : 0;
        _lastSentHeldPropPos = propPos;
        _lastSentHeldPropRot = propRot;
        _lastSentSecondPropPos = secondPropPos;
        _lastSentSecondPropRot = secondPropRot;
        _lastSentHeldPropSize = propSizeCode;
        _lastSentSecondPropSize = secondPropSizeCode;

        // HELD-FIGURE STRETCH (extension record 30): the manual two-hand resize factors, written
        // only while at least one slot is non-neutral — an unstretched hold and every idle player
        // emit the exact bytes previous builds emitted (the serializer re-checks the same gate).
        if (stretchPrimaryCode != NetProtocol.HeldStretchCodeNeutral
            || stretchSecondaryCode != NetProtocol.HeldStretchCodeNeutral)
        {
            extras.HasHeldStretch = true;
            extras.HeldStretchPrimaryCode = (ushort)stretchPrimaryCode;
            extras.HeldStretchSecondaryCode = (ushort)stretchSecondaryCode;
            if (!_loggedStretch)
            {
                _loggedStretch = true;
                VRLog.Info("Net", "Held-figure stretch SENT: factors "
                    + $"{stretchPrimaryCode / 1000f:0.###} / {stretchSecondaryCode / 1000f:0.###} "
                    + "(primary/secondary slot) — extension record 30 (4 B: two u16 milli-factors). "
                    + $"While a factor changes the extras packet rides at {NetProtocol.SendRateHz:0} Hz; "
                    + "peers multiply it into their own copy of the figure's board scale and ease it "
                    + "like the pose. Logged once per episode, so this is the FIRST value only — the "
                    + "SIZE SYNC OWNER line carries the whole curve.");
            }
        }
        else if (_loggedStretch)
        {
            _loggedStretch = false;
            VRLog.Info("Net", "Held-figure stretch SENT: back to neutral — record 30 omitted again; "
                + "peers ease the mini back to boardSize × zoom ratio.");
        }
        _lastSentStretchPrimary = stretchPrimaryCode;
        _lastSentStretchSecondary = stretchSecondaryCode;

        // SECOND HELD CARD (extension record 10): the card in the player's OTHER hand — pose
        // only, no hand byte (the receiver renders the slab at the absolute pose, never parented
        // to a hand) and no identity, ever. Written only while BOTH hands really hold a card, so
        // a one-card hold — and every idle player — emits the exact bytes build 49 emitted.
        if (secondCard)
        {
            extras.HasSecondHeldCard = true;
            extras.SecondHeldFaceActorId = secondFaceActor;
            extras.HasHeldMapCard = secondMapCard; extras.HeldMapKey = secondMapKey;
            extras.HeldMapPoolSeat = secondMapSeat; extras.HeldMapPoolCount = secondMapCount; extras.HeldMapArcSeat = secondMapArcSeat;
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

        // HELD-CARD GRIP (extension record 34): one flag byte, bit per held-card pose slot. Written
        // only while a bit is set (PresenceSerializer's own gate), so a player who never squeezes
        // the grip on a held card emits the exact bytes the previous build emitted.
        if (cardGripMask != 0)
        {
            extras.HasHeldCardGrip = true;
            extras.HeldCardGripMask = cardGripMask;
        }
        if (cardGripChanged)
        {
            if (cardGripMask != 0)
            {
                _loggedCardGrip = true;
                VRLog.Info("Net", $"Held-card grip SENT: mask 0x{cardGripMask:X2} (record 34) — "
                    + "the named slot(s) are RIGID in this player's fist, so peers keep the "
                    + "transmitted rotation for them instead of re-deriving the billboard at this "
                    + "head. The finger pose needs nothing: curls already ride every rig packet.");
            }
            else if (_loggedCardGrip)
            {
                _loggedCardGrip = false;
                VRLog.Info("Net", "Held-card grip SENT: released — record omitted; peers go back to "
                    + "billboarding every held slab at this player's head, exactly as before.");
            }
        }
        _lastSentCardGripMask = cardGripMask;

        if (heldEdgePreempt)
        {
            // HW-VERIFY: the N7 ruling's own falsifier, and the ONLY line that can prove the
            // pre-emption ran on hardware. Grep token: HELD-CARD EDGE PRE-EMPT. Note tier because
            // the co-player runs at the shipped default level (Info is below it since ModBuild 331)
            // and either tester can be the one plucking a card.
            //
            // WORKING = this line naming a term, immediately followed by that term's own SENT line
            // (Held-card face SENT / SHORT REST SEAT / SPENT HALF SENT / FAN SOURCE SENT), and the
            // peer's matching receive line (Remote held card FRONT / SHORT REST SEAT / SPENT HALF)
            // within a packet of it — with the 'early' figure below showing the delay that is no
            // longer being paid.
            //
            // INERT = a whole session of the four SENT lines with NOT ONE of these beside them.
            // Then every one of those edges happened to land on a cadence tick, which for a human
            // hand is a 1-in-5 coincidence repeated N times — i.e. the terms are not in the gate at
            // all and this fix never ran.
            //
            // NOISY = this line at anything approaching frame rate. Then one of the four IS
            // chattering despite the sampling block's argument that all four are discrete, the term
            // it names is the one to pull, and the 5 Hz cadence has become a stream.
            VRLog.Note("Net", "HELD-CARD EDGE PRE-EMPT: extras packet forced out "
                + $"{heldEdgeEarlyMs:F0} ms before its 5 Hz slot by"
                + (heldFaceChanged ? " HELD-CARD FACE (record 36)" : "")
                + (sacrificeSeatChanged ? " SACRIFICE SEAT (record 39)" : "")
                + (spentHalfChanged ? " SPENT HALF (record 41)" : "")
                + (fanSourceChanged ? " FAN SOURCE (record 43)" : "")
                + " — before ModBuild 481 these four rode the cadence and were the only discrete "
                + "human-paced edges in this method that did not pre-empt it, so the peer's slab "
                + "kept the face it had for up to one 200 ms interval after the rig packet had "
                + "already moved it (REVIEW-net.md N7). NO byte of any record changed: this line is "
                + "about WHEN the packet leaves, never what is in it. The term's own SENT line "
                + "follows immediately and carries the value.");
        }
        _lastSentFaceCount0 = faceCount0; _lastSentFaceCount1 = faceCount1;
        _lastSentSecondFaceActor = secondFaceActor;
        _lastSentSecondMapArcSeat = secondMapArcSeat;
        _lastSentSecondMapKey = secondMapKey; _lastSentSecondMapSeat = secondMapSeat; _lastSentSecondMapCount = secondMapCount;
        FillHeldCardRecords(ref extras, faceCode0, faceCount0, faceCode1, faceCount1,
                            seatCode0, seatCount0, seatCode1, seatCount1, seatReason,
                            spentMask, fanSource);

        // The short-rest privacy term must survive a missing widget or an unresolvable seat.
        // Send the absolute state on every packet; either edge pre-empts the 5 Hz cadence.
        extras.UseBarWidgetStates = useBarWidgets;
        _lastSentUseBarWidgets = useBarWidgets;
        extras.ShortRestInProgress = shortRestInProgress;
        if (shortRestChanged)
            VRLog.Note("Net", $"SHORT REST STATE SENT: choosing={shortRestInProgress}; record 46 "
                             + "is independent of sacrifice-seat resolution.");
        _lastSentShortRest = shortRestInProgress;

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

    /// <summary>
    /// Records 36 (held-card face), 39 (sacrifice seat), 41 (spent half) and 43 (fan source),
    /// sampled and written onto <paramref name="extras"/> with their SENT edge lines. Lifted
    /// VERBATIM out of <see cref="TickExtrasSend"/> in the 2026-09 refactor (pure motion): the
    /// block read nothing from the sampling half of that method — only the samplers, the
    /// <c>_lastSent*</c> fields and the packet being filled — so it moves as a unit. Its position
    /// in the packet is unchanged (last before the version record).
    ///
    /// <para>THE SAMPLE IS NOW TAKEN BY THE CALLER, IN FRONT OF THE PRE-EMPTION GATE, and handed
    /// here as parameters — the N7 ruling of 2026-09-08. These four were the only discrete,
    /// human-paced edges in <see cref="TickExtrasSend"/> that could not pre-empt the 5 Hz cadence,
    /// because they were sampled AFTER the gate and so could only be read on a tick where a packet
    /// was already leaving; a pluck therefore reached peers up to 200 ms after the rig packet that
    /// had moved the slab, and the slab showed a BACK for that window. Nothing about the records
    /// themselves moved: the payload writes, the four SENT lines, their change detectors and their
    /// order in the packet are byte-for-byte what they were. They are parameters rather than a
    /// second read of the samplers because a second read is a second answer.</para>
    /// </summary>
    private void FillHeldCardRecords(ref PresenceState extras,
                                     byte faceCode0, byte faceCount0,
                                     byte faceCode1, byte faceCount1,
                                     byte seatCode0, byte seatCount0,
                                     byte seatCode1, byte seatCount1,
                                     string seatReason,
                                     byte spentMask, byte fanSource)
    {
        // HELD-CARD FACE (extension record 36 — the 2026-09-02 report's item 6, "Die Vorderseite
        // SOLL man sehen auch von Karten die ein Spieler gerade in der Hand hat"): WHICH card each
        // held-card POSE SLOT is showing, as a source-list id plus a POSITION in a host-replicated
        // list the receiver already draws the whole fan from. No card identity, ever — the index is
        // meaningless to anybody who does not hold the list, and the receiver still refuses to draw
        // a front while RevealGate.ShowRoundCardFronts is closed.
        //
        // Sampled here rather than in the rig path because it belongs with the second card's slot
        // and the grip mask: all three describe the same two POSE SLOTS, filled by the same
        // left-first rule, and a sampler that disagreed with them would name the other hand's card.
        if (faceCode0 != 0 || faceCode1 != 0)
        {
            extras.HasHeldCardFace = true;
            extras.HeldFaceCode = faceCode0;
            extras.HeldFaceCount = faceCount0;
            extras.SecondHeldFaceCode = faceCode1;
            extras.SecondHeldFaceCount = faceCount1;
        }
        // SHORT-REST SACRIFICE SEAT (extension record 39 — report item 15, "Bei einer kurzen Rast
        // soll es sichtbar sein welche Karte dort liegt"): WHICH round recess the sacrifice is
        // lying in, and WHERE that card sits in this character's discard arc. Same encoding as
        // record 36 above and the same length belt; no card identity, ever.
        //
        // Sampled here beside the held-card faces rather than with the board-UI record because it
        // answers the same KIND of question they do — a seat in a host-replicated list — and shares
        // their encoder. The occupancy nibble that says a card LIES there is record 4's; this says
        // WHICH card, and only for the one population the reveal gate carves out.
        if (seatCode0 != 0 || seatCode1 != 0)
        {
            extras.HasSacrificeSeat = true;
            extras.SacrificeSeatCode0 = seatCode0;
            extras.SacrificeSeatCount0 = seatCount0;
            extras.SacrificeSeatCode1 = seatCode1;
            extras.SacrificeSeatCount1 = seatCount1;
        }
        if (seatCode0 != _lastSentSeatCode0 || seatCode1 != _lastSentSeatCode1)
        {
            _lastSentSeatCode0 = seatCode0;
            _lastSentSeatCode1 = seatCode1;
            // HW-VERIFY: report item 15, the SENDER edge. Grep token: SHORT REST SEAT. Note tier
            // because the co-player runs at the shipped default level and either tester can be the
            // one resting — one grep across BOTH logs then settles the 1:1 question, because the
            // receiver prints the same token with the card it resolved.
            //
            // FALSIFIERS, and they are now readable because the line ALWAYS prints once — the
            // latch starts at -1 rather than at the sampler's own failure value, which is why the
            // whole 2026-09-06 session produced zero of these lines across a complete short rest.
            // (1) Exactly ONE line, all session, reading 'names nothing ... SAMPLER SAYS: nothing
            // was lying in either recess': no short rest and no modal pick happened, and this round
            // says NOTHING about item 15 — silence is not success, but it is now a stated silence.
            // (2) A line whose SAMPLER SAYS clause names one of the three refusals while a
            // "SHORT REST SACRIFICE" line stands beside it: the SAMPLER is the half that failed and
            // the clause says WHICH term did it. (3) This line naming a seat while the receiver's
            // census still reads 'record 39 named NO seat': the mirror failed, not the sampler.
            VRLog.Note("Net", "SHORT REST SEAT: "
                + $"recess1 {DescribeHeldFace(seatCode0, seatCount0)}, "
                + $"recess2 {DescribeHeldFace(seatCode1, seatCount1)} — SAMPLER SAYS: {seatReason}. "
                + "(record 39) — a source-list id "
                + "plus a POSITION in this character's DISCARD arc, never a card id and never a "
                + "card name. The sacrifice is in that list the whole time it lies in the recess: "
                + "CardsHandUI.PerformShortRest indexes DiscardedAbilityCards and removes nothing, "
                + "and only FinalizeShortRest (on accept) moves it. The 'd=' count on the board is "
                + "SMALLER because extension record 15 carries the RENDERED stack label, which nets "
                + "off cards in flight — and our own PresentShortRestCard makes this one of them. "
                + "Peers draw the front through RevealGate.PeerCardPopulation.SacrificedCard, which "
                + "is a carve-out for the SACRIFICE and for nothing else — a pick candidate in a "
                + "recess is still the secret the selection phase protects and is never named here.");
        }
        // WHICH HALF OF WHICH ROUND CARD IS ALREADY USED (record 41, report item 8). Sampled here
        // beside the other per-slot records because it describes the same two recesses they do.
        // It is the owner's OWN PICTURE — the half's CanvasGroup alpha, which is what "grau" means
        // — and never a model of when a half counts as played; see HalfSelection.SampleLocalSpent.
        if (spentMask != 0)
        {
            extras.HasRoundHalfSpent = true;
            extras.RoundHalfSpentMask = spentMask;
        }
        if (spentMask != _lastSentSpentMask)
        {
            _lastSentSpentMask = spentMask;
            // HW-VERIFY: report item 8, the SENDER edge. Grep token: SPENT HALF. Note tier because
            // either tester can be the one playing a card, and the RECEIVER prints the same token —
            // one grep across BOTH logs then settles the 1:1 question with no arithmetic.
            //
            // PROOF: this line naming a half, and the peer's "SPENT HALF [player n]" line naming
            // THE SAME recess and the same half within a packet or two of it.
            //
            // FALSIFIERS, and they say different things. (1) Only the all-zero first line, all
            // session, while the owner demonstrably played cards: the SAMPLER is the half that
            // failed — either the dock read READ-ONLY (we were watching another character) or the
            // game stopped writing the alpha this reads, and CardHalfTone's census is the next
            // instrument. (2) This line naming a half while the peer's line is absent: the record
            // never arrived or the mirror never armed. (3) Both lines present and agreeing while
            // the user still cannot tell the halves apart: the bits are right and the APPLIER is
            // wrong — CardHalfTone.Normalize is re-brightening the clone, and its mirrored-dim hold
            // is the term to check.
            VRLog.Note("Net", $"SPENT HALF SENT: mask=0x{spentMask:X2} — recess1 top="
                + $"{NetProtocol.RoundHalfIsSpent(spentMask, 0, top: true)}, recess1 bottom="
                + $"{NetProtocol.RoundHalfIsSpent(spentMask, 0, top: false)}, recess2 top="
                + $"{NetProtocol.RoundHalfIsSpent(spentMask, 1, top: true)}, recess2 bottom="
                + $"{NetProtocol.RoundHalfIsSpent(spentMask, 1, top: false)} (record 41, one byte). "
                + "TRUE means the owner's OWN screen draws that half at alpha 0.5 — the game's "
                + "FullAbilityCardAction.SetInteractable(false) writes exactly that and nothing "
                + "else, which is what the user calls 'grau weil sie schon benutzt wurde'. NO card "
                + "id and no card name rides here, only which of four regions is dimmed. A mask of "
                + "0x00 omits the record entirely, so a round before anything is played costs no "
                + "bytes. The BURN half of item 8 is a different mechanism and is already shipped: "
                + "RemoteBurnFx resolves the burnt card out of the host-replicated LostAbilityCards "
                + "and replays the game's own char ramp on it — grep BURN CARD, not this token.");
        }
        // WHICH PILE THE FAN THIS PLAYER IS HOLDING UP IS DRAWN FROM (record 43, report item 7).
        // Sampled here beside the held-card face because the two describe ONE arc: the card in the
        // fist came out of the fan beside it, and before this build the two disagreed about which
        // list that was.
        if (NetProtocol.IsFanSourcePile(fanSource))
        {
            extras.HasFanSource = true;
            extras.FanSourceList = fanSource;
        }
        if (fanSource != _lastSentFanSource)
        {
            _lastSentFanSource = fanSource;
            // HW-VERIFY: report item 7, the SENDER edge. Grep token: FAN SOURCE. Note tier because
            // either tester can be the one long-resting and the co-player runs at the shipped
            // default level; the RECEIVER prints the same token, so one grep across BOTH logs
            // settles it with no arithmetic.
            //
            // WORKING = a "FAN SOURCE SENT: the DISCARD pile" line at the start of a long rest burn
            // step, the observer naming DISCARD in its own FAN SOURCE line within a packet or two,
            // and a PEER CARD FACE CENSUS whose hand-fan row then reads N FRONT / 0 BACK with the
            // rule naming a PICK FAN.
            //
            // INERT = this line stays on "the HAND" for the whole of a long rest whose burn step is
            // in the same log ("Long rest: BURN step active"). Then CardsDriver never wrote
            // _fanSourcePile, the record is empty by construction, and the SAMPLER is the half that
            // failed rather than the mirror.
            //
            // STILL BEYOND THE INSTRUMENT = this line names DISCARD, the receiver names DISCARD,
            // and the census STILL reads BACKs with LENGTH BELT beside them. Then the two clients
            // disagree about the CONTENTS of that pile rather than about which pile it is, the belt
            // is doing exactly its job, and the two counts it prints are the next reading — no part
            // of this record can move them.
            VRLog.Note("Net", "FAN SOURCE SENT: "
                + (NetProtocol.IsFanSourcePile(fanSource)
                    ? (fanSource == NetProtocol.HeldFaceListBurnt ? "the BURNT pile" : "the DISCARD pile")
                      + " - this player's fan is a MODAL PICK over that pile right now (a long "
                      + "rest's burn step, an avoid-damage burn, a card-limit discard, a recover), "
                      + "not their hand"
                    : "the HAND - the ordinary fan, which is the default every receiver already "
                      + "resolves, so NO record is written for it and this packet is byte-identical "
                      + "to ModBuild 458's")
                + " (record 43, one list-id byte). NO card identity: it names a LIST, never a card "
                + "and never a position in one. Peers resolve every face out of THEIR OWN copy of "
                + "that host-replicated pile, refuse it unless their copy is exactly as long as the "
                + "fan on the wire, and ask RevealGate first with the SAME phase term a hand fan "
                + "gets (RevealGate.PeerCardPopulation.PickFan). Before this record every observer "
                + "resolved this arc as the HAND, which during a long rest is a list of a different "
                + "length - the length belt then refused every face in the fan and the whole arc "
                + "went to BACKS, which is report item 7.");
        }
        if (faceCode0 != _lastSentFaceCode0 || faceCode1 != _lastSentFaceCode1)
        {
            _lastSentFaceCode0 = faceCode0;
            _lastSentFaceCode1 = faceCode1;
            // HW-VERIFY: ModBuild 352 item 6 — the SENDER edge of the held-card front. Either tester
            // can be the one holding the card and the co-player runs at the shipped default level,
            // so this has to print on both machines. It is a change edge on a physical grab, so it
            // is one line per pluck. Pair it with the receiver's "Remote held card FRONT" line: this
            // line naming a list and a seat while that one says nothing means the mirror, not the
            // sampler, is the half that failed.
            VRLog.Note("Net", "Held-card face SENT: "
                + $"slot1 {DescribeHeldFace(faceCode0, faceCount0)}, "
                + $"slot2 {DescribeHeldFace(faceCode1, faceCount1)} (record 36) — a source-list id "
                + "plus a POSITION in a list every client already holds, never a card id. Peers draw "
                + "the front only while RevealGate.CardFaces names a source for the character "
                + "this board presents, and only while their own copy of that list is exactly as "
                + "long as the count above. 'map-room loadout' is the MAP-ROOM list (source id 5, "
                + "report item 5a): there is no CPlayerActor and no items pile there, so this "
                + "sampler used to give up and name nothing at all — which is why a card held in "
                + "the map room could only ever be a back while the fan beside it showed fronts. "
                + "'ACTIVE pile' is source id 6 (report item 9): an active card its owner picks up "
                + "has CardType==Active, so it is in no hand fan and in no pile arc and this "
                + "sampler named NOTHING for it in EVERY phase — a 'slot1 nothing' here while a "
                + "card really is in that fist is the shape that defect took, and the 2026-09-06 "
                + "session's logs stand at 11 of 22 on the host and 24 of 48 on the co-player.");
        }
    }

    /// <summary>
    /// The world-wide records that ride whatever extras packet is going out — the shared
    /// environment clock (31, with the haunt frequency), the debug test-trigger override (32),
    /// the story window (19) and the 3D map room with its shared windows (20, 21). Lifted
    /// VERBATIM out of <see cref="TickExtrasSend"/> in the 2026-09 refactor (pure motion): none
    /// of them reads a local of the sampling half, and each writes nothing unless its state
    /// really stands, so the packet bytes are what they were. Order within the packet is the
    /// serializer's, not this method's.
    /// </summary>
    private static void FillEnvironmentRecords(ref PresenceState extras)
    {
        // SHARED ENVIRONMENT CLOCK (extension record 31). USER REQUEST, verbatim: "Mond und
        // Lichtstrahlen sollen im Multiplayer (falls beide Spieler die selbe Umgebung ausgewählt
        // haben) auch synchronisiert werden. Das gilt generell für alle Effekt zB auch die Maus.
        // Ich will das alle Spieler sie gleichzeitig sehen (wenn die spieler es an haben)."
        //
        // NO EDGE DETECTOR AND NO EXTRA PACKET: the clock is a monotone reading, so it simply rides
        // whatever extras packet the cadence above already sends — five bytes at ≤5 Hz, and only
        // while an environment with animated content really stands. SkyAlternative reports style 0
        // for the game's own sky, for OffBlack and under MR, and a 0 writes no record at all, so
        // every player who is not in the cellar or the swamp emits the exact bytes previous builds
        // emitted. The two reads are a bool/enum compare and one float add (SkyAlternative).
        byte envStyle = Core.SkyAlternative.WireStyleCode;
        if (envStyle != 0)
        {
            extras.HasEnvClock = true;
            extras.EnvClockStyle = envStyle;
            extras.EnvClockMillis = Core.SkyAlternative.EnvClockMillis;
            // …AND ITS SIXTH BYTE, THE HAUNT FREQUENCY (user ruling 2026-08-15: "Die Haeufigkeit von
            // Easter Eggs (da alle es ja synchron sehen sollen) soll vom HOST genommen werden im
            // MP"). It rides the clock record rather than one of its own so that "the host" and "the
            // clock owner" can never be two different clients: the frequency is a threshold over a
            // hash of exactly the clock this record negotiates. We publish OUR OWN dial here — the
            // election below is what decides whose is actually used, and a follower's byte is simply
            // never read by anyone.
            extras.HasEnvClockFrequency = true;
            extras.EnvClockFrequencyCode =
                NetProtocol.EncodeHauntFrequency(Core.Haunt.Frequency != null
                                                     ? Core.Haunt.Frequency.Value
                                                     : Defaults.HauntFrequency);
        }

        // DEBUG TEST-TRIGGER OVERRIDE (extension record 32). USER RULING, verbatim: "Auch wenn
        // jemand im Debugmenu ein Event startet sollte dies auch von ALLEN im Multiplayer sichtbar
        // sein statt nur lokal, also synchronisiert werden." Writes nothing at all unless THIS
        // client owns a standing override or is stating its explicit release, so every packet of
        // every session in which nobody opened the debug page is byte-identical to build 149's.
        RemoteTestTriggers.Sample(ref extras);

        // STORY WINDOW SYNC (extension record 19). USER REQUEST, verbatim: "Das Geschichte Fenster
        // und damit der ganze Dialog sollen synchron sein … Wenn durch den Dialog geklickt wurde,
        // wurde folglich fuer ALLE entsprechend durchgeklickt und es gibt keinen Lock mehr."
        // Writes nothing at all unless a story box really stands here or has just finished, so
        // every packet of every session without a narrative on screen is byte-identical to build
        // 156's. Full contract in RemoteStorySync / NetProtocol.ExtIdStorySync.
        RemoteStorySync.Sample(ref extras);

        // THE 3D MAP ROOM (extension record 20) and ITS SHARED WINDOWS (record 21). USER REQUEST,
        // verbatim: "Multiplayer für die 3D-Map: a) Welche Map angezeigt wird … b) Welche Quest
        // gerade angeklickt ist … c) Die mouseover Infotafeln … d) … das erscheinende Fenster soll
        // voll synchronisiert werden … genauso wie die darauffolgendene Story-Fenster."
        // Both write NOTHING AT ALL unless this client's own 3D map room is really standing
        // (MapRoomDriver.Active), so every packet of every scenario session, every flat-map session
        // and every player who has the 3D map switched off is byte-identical to build 221's. Full
        // contracts in RemoteMapRoom / RemoteMapStory and NetProtocol.ExtIdMapRoom /
        // NetProtocol.ExtIdSharedWindow.
        RemoteMapRoom.Sample(ref extras);
        RemoteMapStory.Sample(ref extras);
    }

    /// <summary>
    /// Record 4's three bytes packed into one change key — buttons | overlays &lt;&lt; 8 |
    /// cap states &lt;&lt; 16 — read off the objects that drive the local rendering, or -1 when
    /// there is no live tray. Lifted VERBATIM out of <see cref="TickExtrasSend"/> in the 2026-09
    /// refactor (pure motion): the block read only <paramref name="trayNow"/> and statics.
    /// </summary>
    private static int SampleBoardUi(PlayTray? trayNow)
    {
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
            // THE SKIP BIT MEANS EXACTLY WHAT IT ALWAYS MEANT — "a turn-flow skip control is
            // visible on this board" — and is read off the same cap for the same reason; only its
            // OWNER moved. It used to be WorldUI.ButtonCluster's static, because that cap belonged
            // to a separate cluster whose instance was a private of WorldUIModule. The cap is a
            // generic board keycap on the board's third recess now, so the fact is a tray property
            // like every other bit in this byte, and it is guarded by the same `trayNow != null`.
            if (trayNow.SkipCapShown) buttons |= NetProtocol.BoardUiSkipBit;
            // THE DECISION DRAWER BIT — "a prompt is docked AND the owner can see it".
            //
            // The second half is new (user ruling 2026-08-08: "generell gilt die Regel, das man
            // alle Interaktionen, Animationen und Anzeigen des Controllboards in MP auch
            // synchronisieren soll … so wie der Spieler sie sieht"). While the owner has focused
            // ANOTHER character, their own decision row is render-hidden and their board shows
            // NOTHING at that seat (DecisionDockSurface.ApplyFocusHide) — so a peer drawing the
            // drawer would be showing furniture the owner does not have. The bit therefore tracks
            // the RENDERED state, not the model state: prompt open, row not focus-hidden.
            if (WorldUI.ModalFallback.DecisionDock.ActivePrompt() != null
                && !WorldUI.Surfaces.DecisionDockSurface.RowFocusHidden)
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
            // SNAP-GLOW HOVER TELEGRAPH (byte 1 bits 6..7 — the "cannot be reproduced" defect).
            // WHICH recess the owner's own gold rim is lit on, read straight off the field the
            // local glow is driven from (PlayTray.HighlightedSlot ← CardsDriver
            // .UpdateSlotHighlight), so the mirrored rim lights on the same recess in the same
            // frames — BEFORE the drop, where the telegraph belongs, and it goes out again when the
            // hover ends without one.
            overlays |= (NetProtocol.EncodeSnapSlot(trayNow.HighlightedSlot)
                         << NetProtocol.BoardUiSnapShift) & NetProtocol.BoardUiSnapMask;
            // CAP STATES (byte 2): the ACCENT/CONFIRMED/ENABLED flags the owner's own caps are
            // painted from — every one of them read off the flag the local renderer obeys, never
            // re-derived from the game rules, so the mirror cannot disagree with the original.
            // UNDO and the item-USE cap are absent on purpose: every SetState call on them in the
            // whole mod is a constant, so their look is a build fact and costs no bit (see
            // NetProtocol.BoardUiCapConfirmAccentBit).
            byte capStates = 0;
            if (trayNow.ConfirmCapAccent) capStates |= NetProtocol.BoardUiCapConfirmAccentBit;
            if (trayNow.ConfirmCapConfirmed) capStates |= NetProtocol.BoardUiCapConfirmReadyBit;
            if (RestControls.ShortRestEnabled) capStates |= NetProtocol.BoardUiCapShortRestEnabledBit;
            if (RestControls.ShortRestAccent) capStates |= NetProtocol.BoardUiCapShortRestAccentBit;
            if (RestControls.LongRestEnabled) capStates |= NetProtocol.BoardUiCapLongRestEnabledBit;
            if (RestControls.LongRestAccent) capStates |= NetProtocol.BoardUiCapLongRestAccentBit;
            if (trayNow.SkipCapEnabled) capStates |= NetProtocol.BoardUiCapSkipEnabledBit;
            // …AND THE ONE BIT IN THIS BYTE THAT IS NOT A CAP (bit 7, the last free bit in the whole
            // record): the owner's CLOSED items pile is wearing its "something in here is playable"
            // cue. It rides here rather than in a record of its own because it has to arrive in the
            // SAME packet as byte 0's item-recess and item-USE bits — the three of them are one
            // picture of the item flow, and a peer that gets them in different frames paints half of
            // it against the other half's state. Read off PileViewer's own published render answer,
            // never re-derived from the inventory, so the mirrored stack cannot beat while the
            // owner's is dark (or the reverse). See NetProtocol.BoardUiCapItemPileUsableBit.
            if (PileViewer.ItemsUsableCueOn) capStates |= NetProtocol.BoardUiCapItemPileUsableBit;
            boardUiNow = buttons | ((overlays & NetProtocol.BoardUiOverlayMask) << 8)
                         | ((capStates & NetProtocol.BoardUiCapStateDefinedMask) << 16);
        }
        return boardUiNow;
    }

    // ---- receive ------------------------------------------------------------------------

    /// <summary>Packets accepted since the last receive summary (diagnostic only).</summary>
    private int _rxCount;
    private float _rxNextReport;
    private bool _rxFirstLogged;

    /// <summary>Packets REFUSED since the last receive summary — a header that is not ours, or a
    /// body shorter than its own flags demand. Counted separately because the two populations
    /// answer different questions and one summary number covering both would hide the interesting
    /// one.</summary>
    private int _rxRejected;

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
                              + $"{_pending.Count} rig + {_pendingExtras.Count} extras pending"
                              + (_rxRejected > 0
                                     ? $"; {_rxRejected} REJECTED (magic/version/type or a length "
                                       + "its own flags demanded and the packet did not carry)"
                                     : string.Empty)
                              + ".");
            _rxCount = 0;
            _rxRejected = 0;
        }

        // Ignore our own echo and unparseable/foreign packets.
        if (senderId != 0 && senderId == _transport.LocalPlayerId)
            return;

        // Route by message type without fully parsing (also rejects magic/version mismatches).
        int type = NetPacket.PeekType(buffer, length);
        // A PACKET WE REFUSE NOW SAYS SO. Every rejection path here — an unroutable type from
        // PeekType, or a TryRead that fails its own length contract — used to fall out of this
        // method with the RX counter already incremented and nothing else written down, so a peer
        // whose packets this build cannot parse looked EXACTLY like a healthy one: they count sends,
        // we count receives, and nobody appears. (VersionGuard catches a mismatched build only via
        // an extras packet that PARSED, so it cannot cover this.) One line for the first refusal
        // from each sender, then the count on the 10 s summary above.
        bool parsed = false;
        switch (type)
        {
            case NetProtocol.MsgRig:
                if (AvatarSerializer.TryRead(buffer, length, out AvatarState state))
                {
                    parsed = true;
                    VersionGuard.NotePacket(senderId); // any valid mod packet ⇒ a modded peer
                    // Convert the shared-frame poses to world here so RemoteAvatar stays world-only.
                    ToWorld(ref state);
                    _pending[senderId] = state; // dedup: keep only the newest
                }
                break;

            case NetProtocol.MsgUseBarAnimation:
                if (UseBarAnimationCodec.TryRead(buffer, length, out UseBarAnimationSnapshot? animation)
                    && animation != null)
                {
                    parsed = true;
                    VersionGuard.NotePacket(senderId);
                    if (!_pendingAnimations.TryGetValue(senderId, out List<UseBarAnimationSnapshot>? samples))
                    {
                        if (_pendingAnimations.Count >= 8) break;
                        samples = new List<UseBarAnimationSnapshot>(4);
                        _pendingAnimations.Add(senderId, samples);
                    }
                    // Preserve the first rendered transition when Bolt delivers a batch after a
                    // slow frame. Memory remains bounded; redundant middle samples may coalesce.
                    if (samples.Count == 0 || animation.SampleTime > samples[samples.Count - 1].SampleTime)
                        PresentationPending.Append(samples, animation, PresentationPending.SameBonusIdentity);
                }
                break;

            case NetProtocol.MsgCardPlume:
            case NetProtocol.MsgNativeUseBar:
            case NetProtocol.MsgNativeBoard:
            case NetProtocol.MsgCardAppearance:
            case NetProtocol.MsgNativeDecisionPrompt:
                parsed = QueueNativePresentation(senderId, buffer, length);
                if (parsed) VersionGuard.NotePacket(senderId);
                break;

            case NetProtocol.MsgExtras:
                if (PresenceSerializer.TryRead(buffer, length, out PresenceState extras))
                {
                    parsed = true;
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

        if (!parsed)
            NoteRejectedPacket(senderId, type, length);
    }

    /// <summary>Senders this driver has already reported a refusal for — one line each, because a
    /// build that cannot parse a peer at all cannot parse them 15 times a second either.</summary>
    private readonly HashSet<int> _rxRejectLogged = new();

    /// <summary>
    /// A packet this build refused, counted and (once per sender) named.
    ///
    /// <para>The type tells the two causes apart without any further parsing: −1 is
    /// <see cref="NetPacket.PeekType"/> refusing the HEADER (wrong magic, wrong wire version, or
    /// fewer than six bytes — a foreign side action, or a peer on a different wire version), and a
    /// valid 0/1 means the header was ours and the BODY did not carry the length its own flag byte
    /// demanded (a truncated or corrupt packet).</para>
    /// </summary>
    private void NoteRejectedPacket(int senderId, int type, int length)
    {
        _rxRejected++;
        if (!_rxRejectLogged.Add(senderId))
            return;
        VRLog.Note("Net", $"PACKET REJECTED from player {senderId}: {length} B, type "
                          + (type < 0
                                 ? "unreadable — the header is not ours (magic/version mismatch, or "
                                   + "under 6 bytes). A foreign side action reads exactly like this "
                                   + "and is harmless; a MODDED peer reading like this is on a "
                                   + "different wire version, which no ModBuild handshake can "
                                   + "report because that handshake rides a packet we just refused"
                                 : $"{type} — our header, but the body was shorter than its own flag "
                                   + "byte demands, so it was dropped whole rather than half-applied")
                          + ". One line per sender; the 10 s RX summary carries the running count.");
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

                    // HELD PROPS (extension record 37): the map items in the sender's hands, each
                    // with its hand, its pose and its measured held size. Absence of the record
                    // means "no prop held", which is also what every peer predating it transmits,
                    // so the else branches release. Slot 2 is released independently of slot 1 for
                    // the same reason the figure slots are: a peer putting down one of two items
                    // must not lose the other.
                    if (p.HasHeldProp)
                    {
                        NetProps.ApplyRemoteHeld(kv.Key, NetProps.SlotPrimary, p.HeldPropId,
                                                 p.HeldPropPose.Position, p.HeldPropPose.Rotation,
                                                 p.HeldPropLeftHand,
                                                 NetProtocol.DecodeHeldStretch(p.HeldPropStretchCode));
                        if (p.HasSecondHeldProp)
                            NetProps.ApplyRemoteHeld(kv.Key, NetProps.SlotSecondary, p.SecondHeldPropId,
                                                     p.SecondHeldPropPose.Position,
                                                     p.SecondHeldPropPose.Rotation,
                                                     p.SecondHeldPropLeftHand,
                                                     NetProtocol.DecodeHeldStretch(p.SecondHeldPropStretchCode));
                        else
                            NetProps.ReleaseRemoteSlot(kv.Key, NetProps.SlotSecondary);
                    }
                    else
                    {
                        NetProps.ReleaseRemoteSlot(kv.Key, NetProps.SlotPrimary);
                        NetProps.ReleaseRemoteSlot(kv.Key, NetProps.SlotSecondary);
                    }

                    // HELD-FIGURE STRETCH (extension record 30): the holder's manual two-hand
                    // resize factors for both held-figure slots. Absence means BOTH are 1.0 —
                    // exactly what an old sender transmits and what an unstretched hold means —
                    // so the else branch RESETS rather than leaves the last factor standing: a
                    // sender whose gesture returned to neutral stops writing the record, and the
                    // peer's copy must follow it home. Both paths are eased by NetFigures.Tick
                    // (same k as the pose), never snapped.
                    if (p.HasHeldStretch)
                        NetFigures.ApplyHeldStretch(kv.Key,
                            NetProtocol.DecodeHeldStretch(p.HeldStretchPrimaryCode),
                            NetProtocol.DecodeHeldStretch(p.HeldStretchSecondaryCode));
                    else
                        NetFigures.ResetHeldStretch(kv.Key);

                    // CHARACTER FOCUS (extension record 22): which character this peer is looking
                    // at, plus their local-only "the character the game is waiting on is mine" bit
                    // and (when it differs) that character's id. Applied HERE rather than through
                    // RemoteAvatar because the consumer is a static cue table (CharacterFocus) that
                    // several renderers read — the peer's board frame, their Steam-avatar ring,
                    // their mirrored initiative track — and not a property of the avatar itself. A
                    // packet WITHOUT the record clears the peer's entry, which is exactly what an
                    // older build transmits and what "no outline" must mean.
                    Board.CharacterFocus.ApplyPeer(kv.Key, p.HasCharFocus, p.CharFocusActorId,
                                                   p.CharFocusOwnsAttention,
                                                   p.CharFocusAttentionActorId);

                    // SHARED ENVIRONMENT CLOCK (record 31). The reading is kept HERE and not on the
                    // RemoteAvatar for the same reason CharacterFocus is: its consumer is a static
                    // world-wide effect (the local environment's shader clock), not a property of
                    // the peer's body. A packet WITHOUT the record forgets the peer's entry, which
                    // is what a player who switched to the game's own sky, to OffBlack, to MR or to
                    // an older build transmits — and "forgotten" is exactly "has nothing to share".
                    if (p.HasEnvClock)
                        _peerEnv[kv.Key] = new PeerEnvClock(p.EnvClockStyle, p.EnvClockMillis,
                                                            Time.unscaledTime,
                                                            Time.timeSinceLevelLoad,
                                                            p.HasEnvClockFrequency,
                                                            p.EnvClockFrequencyCode);
                    else
                        _peerEnv.Remove(kv.Key);

                    // DEBUG TEST-TRIGGER OVERRIDE (record 32). Kept in a static table for the same
                    // reason the environment clock is: its consumers are two world-wide channels
                    // (Haunt and ElementMood), not properties of this peer's body. A packet WITHOUT
                    // the record forgets the peer's entry, which is what every player who is not
                    // holding a debug latch transmits.
                    RemoteTestTriggers.ApplyPeer(kv.Key, in p);

                    // STORY WINDOW SYNC (record 19). Kept in a static table for the same reason:
                    // its consumer is the LOCAL game's own story box, not a property of this peer's
                    // body. A packet WITHOUT the record forgets the peer's entry, which is what
                    // every player with no narrative on screen — and every pre-record build —
                    // transmits, and "forgotten" is exactly "has no story to sync".
                    RemoteStorySync.Observe(kv.Key, in p);

                    // THE 3D MAP ROOM (record 20) and ITS SHARED WINDOWS (record 21). Kept in
                    // static tables for the same reason the story sync is: their consumers are the
                    // LOCAL map room and the LOCAL game's own map story box, not properties of this
                    // peer's body. A packet WITHOUT either record forgets that peer's entry, which
                    // is what every player who is not in the 3D map room — and every pre-record
                    // build — transmits, and "forgotten" is exactly "not standing at this table".
                    RemoteMapRoom.Observe(kv.Key, in p);
                    RemoteMapStory.Observe(kv.Key, in p);
                    RemoteVideoPlayback.Observe(kv.Key, in p);
                }
                catch (Exception e) { LogPhaseError($"Apply extras packet from player {kv.Key}", e); }
            }
            _pendingExtras.Clear();
        }

        foreach (KeyValuePair<int, List<UseBarAnimationSnapshot>> kv in _pendingAnimations)
        {
            if (kv.Value.Count == 0) continue;
            try
            {
                RemoteAvatar? avatar = GetOrCreate(kv.Key);
                if (avatar != null) avatar.SetUseBarAnimation(kv.Value[0]);
                kv.Value.RemoveAt(0);
            }
            catch (Exception e) { LogPhaseError($"Apply animation packet from player {kv.Key}", e); }
        }

        ApplyNativePresentation();
        ResolveEnvClock();
        RemoteTestTriggers.Resolve(_transport != null ? _transport.LocalPlayerId : 0);
        // The story advance is resolved once per frame, not once per packet: two peers publishing
        // page 3 in the same frame must drive the local box once.
        RemoteStorySync.Resolve();
        // Same rule for the map room's two records — and both return after ONE comparison
        // (MapRoomDriver.Active) for every client that has the 3D map switched off, which is what
        // keeps that client's picture byte-for-byte the pre-record one.
        RemoteMapRoom.Resolve();
        RemoteMapStory.Resolve();
        RemoteVideoPlayback.Resolve(_transport != null ? _transport.LocalPlayerId : 0);
    }

    /// <summary>A peer's last environment-clock reading (extension record 31) and when it
    /// arrived.</summary>
    private readonly struct PeerEnvClock
    {
        public PeerEnvClock(byte style, uint millis, float at, float localAt,
                            bool hasFrequency, byte frequencyCode)
        {
            Style = style;
            Millis = millis;
            At = at;
            LocalAt = localAt;
            HasFrequency = hasFrequency;
            FrequencyCode = frequencyCode;
        }

        /// <summary>Whether this peer's clock record carried its sixth byte. False for a peer built
        /// before 2026-08-15 — and then the local dial governs here, which is exactly what those
        /// builds did.</summary>
        public readonly bool HasFrequency;

        /// <summary>The peer's haunt frequency in hundredths. Adopted ONLY from the elected clock
        /// owner: a peer that merely sent one is not the host of anything.</summary>
        public readonly byte FrequencyCode;

        public readonly byte Style;
        public readonly uint Millis;

        /// <summary>Unscaled arrival time — the staleness clock, which must keep running when the
        /// game's own time does not.</summary>
        public readonly float At;

        /// <summary>The LOCAL shader clock (<c>Time.timeSinceLevelLoad</c>, i.e. what <c>_Time.y</c>
        /// reads) at the same instant. The offset the follower wants is
        /// <c>Millis/1000 − LocalAt</c>, and it must be computed from THIS pair and not from the
        /// live clock: both readings advance at one second per second, so the difference is
        /// constant — but pairing an OLD reading with the CURRENT local time would drag the target
        /// backwards by one second per second between packets.</summary>
        public readonly float LocalAt;
    }

    private readonly Dictionary<int, PeerEnvClock> _peerEnv = new();

    /// <summary>Seconds after which a peer's environment-clock reading is treated as gone. Extras
    /// go out at ≤5 Hz, so this is a dozen missed packets — long enough that a hitch or a dropped
    /// unreliable packet cannot cost a clock owner, short enough that a peer who quits the
    /// environment (or the game) hands the clock back within a breath. Aliased to
    /// <see cref="NetProtocol.StaleTimeoutSeconds"/>, the window this same driver drops the peer's
    /// AVATAR on, so a peer cannot own the clock a frame longer than it exists.</summary>
    private const float EnvClockStaleSeconds = NetProtocol.StaleTimeoutSeconds;

    private int _loggedEnvOwner = int.MinValue;
    private byte _loggedEnvStyle = 255;

    /// <summary>
    /// Elect the environment clock owner and hand the local environment its offset.
    ///
    /// <para>THE RULE, and why it needs no host and no handshake: the owner is the LOWEST player id
    /// among everyone who currently reports the SAME environment style, the local player included.
    /// Every client evaluates that over the same set and therefore reaches the same answer; the
    /// lowest id keeps offset 0 and is the reference, everyone else walks to its reading. A peer on
    /// a DIFFERENT style is not a candidate at all — "falls beide Spieler die selbe Umgebung
    /// ausgewählt haben" is the user's own condition, and a peer's choice must never override the
    /// local dial (it is local presentation, and MR has to be able to force it off regardless).</para>
    ///
    /// <para>Runs once per frame over a dictionary that is empty in single player and has one entry
    /// in a 1:1 game.</para>
    /// </summary>
    private void ResolveEnvClock()
    {
        byte localStyle = Core.SkyAlternative.WireStyleCode;
        if (localStyle == 0)
        {
            // Nothing shown here (the game's own sky, OffBlack, MR, no scenario): there is nothing
            // to synchronise. SkyAlternative's own teardown already returns the shader global to 0;
            // this keeps the bookkeeping honest while an environment is merely paused.
            if (_loggedEnvOwner != int.MinValue)
            {
                _loggedEnvOwner = int.MinValue;
                _loggedEnvStyle = 255;
                VRLog.Info("Net", "ENV SYNC: no environment shown locally — nothing to synchronise " +
                                  "(the peer's choice never overrides the local one).");
            }
            // NO ENVIRONMENT, NO HOST FREQUENCY. The local dial governs again immediately — which is
            // also what happens the moment this client leaves the session.
            Core.Haunt.ClearHostFrequency("no environment is shown locally");
            return;
        }

        int localId = _transport != null ? _transport.LocalPlayerId : 0;
        int owner = 0;                 // 0 = us
        uint ownerMillis = 0;
        float ownerLocalAt = 0f;
        bool ownerHasFreq = false;
        byte ownerFreqCode = 0;
        int matching = 0, differing = 0;
        float now = Time.unscaledTime;
        foreach (KeyValuePair<int, PeerEnvClock> kv in _peerEnv)
        {
            if (now - kv.Value.At > EnvClockStaleSeconds)
                continue;
            if (kv.Value.Style != localStyle)
            {
                differing++;
                continue;
            }
            matching++;
            // Lowest id wins. A peer only beats us if its id is BELOW ours, and beats another peer
            // the same way — so the winner is the same on every machine that sees the same set.
            if (kv.Key >= localId && localId > 0)
                continue;
            if (owner != 0 && kv.Key >= owner)
                continue;
            owner = kv.Key;
            ownerMillis = kv.Value.Millis;
            ownerLocalAt = kv.Value.LocalAt;
            ownerHasFreq = kv.Value.HasFrequency;
            ownerFreqCode = kv.Value.FrequencyCode;
        }

        if (owner != 0)
            Core.SkyAlternative.FollowEnvClock(owner, ownerMillis, ownerLocalAt);
        else
            Core.SkyAlternative.OwnEnvClock();

        // THE HAUNT FREQUENCY COMES FROM THE SAME CLIENT AS THE CLOCK (user ruling 2026-08-15: "Die
        // Haeufigkeit von Easter Eggs (da alle es ja synchron sehen sollen) soll vom HOST genommen
        // werden im MP"), and that is not a coincidence to be tidied away later: the dial is a
        // THRESHOLD over a hash of the very clock elected above, so a frequency taken from one client
        // while the seconds come from another would be a schedule evaluated against somebody else's
        // time. There is deliberately no second notion of host anywhere in this feature.
        //
        // THE ELECTION ALREADY DOES THE SCOPING THE RULING ASKS FOR. Only peers reporting the SAME
        // style are candidates, so a mismatched peer's byte is never read here — there is nothing to
        // skip and no bytes wasted, because the record it rides is written anyway for the clock.
        //
        // WE OWN IT ⇒ NO HOST VALUE APPLIES. A client that is the reference uses its own dial, which
        // is also the single-player case and the "no peer has the same environment" case — the same
        // call, for the same reason SkyAlternative.OwnEnvClock is.
        if (owner != 0 && ownerHasFreq)
            Core.Haunt.SetHostFrequency(owner, NetProtocol.DecodeHauntFrequency(ownerFreqCode));
        else
            Core.Haunt.ClearHostFrequency(owner == 0
                                              ? "this client owns the environment clock, so its own "
                                                + "dial is the reference"
                                              : $"the clock owner (player {owner}) is an older build "
                                                + "that does not transmit a frequency");

        if (owner == _loggedEnvOwner && localStyle == _loggedEnvStyle)
            return;
        _loggedEnvOwner = owner;
        _loggedEnvStyle = localStyle;
        VRLog.Info("Net", $"ENV SYNC: local style {localStyle} as player {localId} — " +
                          $"{matching} peer(s) on the SAME environment, {differing} on another " +
                          $"(they are left alone, by the user's own condition). " +
                          (owner != 0
                              ? $"Clock owner is player {owner} (lowest id): this client follows its " +
                                $"reading {ownerMillis / 1000f:F2}s."
                              : "This client OWNS the clock (lowest id present) and runs at offset 0.") +
                          " Shared: the rat, the drip and its puddle rings, the candle flicker and " +
                          "glow, the canopy sway, the water glints, the shafts'/beam shimmer. Already " +
                          "identical without the wire: the moon (fixed _MoonDir on a board-derived " +
                          "yaw). Deliberately unsynchronised: star rotation and twinkle, shooting " +
                          "stars, fireflies, dust, fog.");
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

        // THE PEER-CARD FACE CENSUS' cadence, driven from the one loop that visits every peer's
        // surfaces. It is a SAMPLER on a fixed interval rather than a change edge — see
        // PeerCardFaceCensus for why the per-surface edge lines could not answer "is a peer's card
        // showing a front right now". Called BEFORE the avatars tick so the line it prints is last
        // frame's settled picture rather than a half-updated one.
        PeerCardFaceCensus.PrintIfDue();

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
            try { WorldUI.Surfaces.DamageDecisionPreview.TickRemote(avatar, avatar.DamageDecisionPreview); }
            catch (Exception e) { LogPhaseError($"Remote damage preview for player {kv.Key}", e); }
            // Teardown on staleness (covers Bolt player-left, a peer switching to flat, or a
            // long network stall — more robust than a single player-left callback).
            if (avatar.TimeSinceUpdate > NetProtocol.StaleTimeoutSeconds)
                _scratchIds.Add(kv.Key);
        }

        for (int i = 0; i < _scratchIds.Count; i++)
            ForgetPeer(_scratchIds[i]);
    }

    /// <summary>
    /// EVERYTHING THIS CLIENT HOLDS FOR ONE PEER, dropped in one place — the peer's avatar and
    /// every SIDE TABLE keyed by their player id.
    ///
    /// <para>WHY IT EXISTS. There were three teardown paths (the staleness sweep,
    /// <see cref="RemovePlayer"/> and <see cref="DestroyAllAvatars"/>) and they listed DIFFERENT
    /// subsets of the same union: only the staleness sweep told
    /// <c>PeerCardFaceCensus</c> the peer was gone, only two of the three cleared
    /// <see cref="_peerEnv"/>, and none of them dropped the construction back-off. Nothing visible
    /// was broken by that — every consumer of those tables tests staleness itself, which is why
    /// this is a structural fix and not a defect — but the fourth table added to this driver would
    /// have been the one that was only remembered twice. A cascade clears only what it lists.</para>
    ///
    /// <para>The construction back-off goes with the peer on purpose: a rejoining player must get a
    /// fresh attempt budget rather than inherit the 5 s window of the avatar that failed before
    /// they left, which is the same reasoning <c>NetPlayerActors.ForgetAvatarFetch</c> already
    /// carried.</para>
    /// </summary>
    private void ForgetPeer(int playerId)
    {
        if (_transport is FfsNetTransport ffs)
            ffs.ForgetPeer(playerId);
        if (_avatars.TryGetValue(playerId, out RemoteAvatar avatar))
        {
            WorldUI.Surfaces.DamageDecisionPreview.ResetRemote(avatar);
            CharacterDecisionPresentation.Remove(avatar);
            avatar.Destroy();
            _avatars.Remove(playerId);
        }
        _pending.Remove(playerId);             // nothing queued for a peer we no longer hold
        _pendingExtras.Remove(playerId);
        _pendingAnimations.Remove(playerId);
        ForgetNativePresentation(playerId);
        _createRetryAt.Remove(playerId);       // a rejoin gets a fresh construction budget
        NetFigures.ReleaseRemote(playerId);    // drop any figure this peer was holding
        NetProps.ReleaseRemote(playerId);      // …and put any map item they carried back on its hex
        NetPlayerActors.ForgetAvatarFetch(playerId); // …and a rejoin gets a fresh fetch budget
        Board.CharacterFocus.ForgetPeer(playerId);   // …and their focus outline goes with them
        _peerEnv.Remove(playerId);                   // …and they stop being a clock/host candidate
        RemoteTestTriggers.ForgetPeer(playerId);     // …and any debug override they owned is released
        PeerCardFaceCensus.ReportPeerGone(playerId); // …and their card-face rows stop being reported
    }

    /// <summary>Immediate teardown entry point for a future <c>PlayerRegistry.OnPlayerLeft</c>
    /// hook (staleness already handles it after <see cref="NetProtocol.StaleTimeoutSeconds"/>).</summary>
    public void RemovePlayer(int playerId) => ForgetPeer(playerId);

    private void DestroyAllAvatars()
    {
        _pendingAnimations.Clear();
        ResetNativePresentation();
        _lastAnimationSnapshot = null;
        _lastSentAnimations = null;
        _nextAnimationRefresh = 0;
        _lastAnimationSourceFrameTime = 0;
        if (_transport is FfsNetTransport ffs)
            ffs.ResetFragments();
        // THE IDS FIRST, because ForgetPeer removes from _avatars and a dictionary cannot be
        // written while it is being enumerated. _scratchIds is free here: the only other user is
        // TickAvatars, which has finished with it before any teardown path can run.
        _scratchIds.Clear();
        foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
            _scratchIds.Add(kv.Key);
        for (int i = 0; i < _scratchIds.Count; i++)
            ForgetPeer(_scratchIds[i]);
        _avatars.Clear();   // belt: ForgetPeer emptied it entry by entry
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
        if (state.HasBoardPose && state.HasBoard)
        {
            _anchor.ToWorld(state.BoardPose.Position, state.BoardPose.Rotation,
                out Vector3 p, out Quaternion r);
            state.BoardPose.Position = p;
            state.BoardPose.Rotation = r;
        }
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
        // Both held-prop poses are SHARED-FRAME poses exactly like the held figures', so they
        // convert here for the same reason: NetProps drives world transforms only.
        if (p.HasHeldProp)
        {
            _anchor.ToWorld(p.HeldPropPose.Position, p.HeldPropPose.Rotation,
                            out Vector3 pp, out Quaternion pr);
            p.HeldPropPose.Position = pp;
            p.HeldPropPose.Rotation = pr;
        }
        if (p.HasSecondHeldProp)
        {
            _anchor.ToWorld(p.SecondHeldPropPose.Position, p.SecondHeldPropPose.Rotation,
                            out Vector3 sp2, out Quaternion sr2);
            p.SecondHeldPropPose.Position = sp2;
            p.SecondHeldPropPose.Rotation = sr2;
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
