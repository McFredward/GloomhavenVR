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
        _createRetryAt.Clear();
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
        _lastSentCapPress = -1;        // …and never replays a stale keycap press into a new session
        Cards.BoardCapPress.Clear();   // …including the latch it is diffed against
        _sentBoardPoseValid = false; // and never diffs a new session's pose against a stale one
        _sentSecondFigureValid = false; // nor a new session's second held figure
        _lastSentSecondActorId = 0;
        _sentSecondCardValid = false;   // nor its second held card
        _lastSentStretchPrimary = NetProtocol.HeldStretchCodeNeutral;   // nor a stale stretch
        _lastSentStretchSecondary = NetProtocol.HeldStretchCodeNeutral; // (record 30)
        _loggedStretch = false;
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

        // A DRAGGED SHARED WINDOW RIDES THE SAME FAST CADENCE AS A DRAGGED BOARD (user request 7,
        // 2026-08-22, verbatim: "Die Bewegungen der 'blauen' MP-Fenster, die 1:1 synchronisiert
        // werden sollen, sollen auch die Bewegung und die Position voll übertragen (flüssig, wie bei
        // der Position des Boards auch)!").
        //
        // THE COMPARISON IS THE POINT OF THE REQUEST, so this is the board's own mechanism and not a
        // second one: while the local player carries a blue-barred window, the whole extras packet
        // goes out at the RIG rate (SendRateHz = 15 Hz) instead of the idle ExtrasSendRateHz = 5 Hz,
        // and falls straight back the moment they let go. That is literally the line above, applied
        // to a second moving thing — and the receiving half is the matching easing in
        // WorldUI.GrabbableModal, which is the other thing the board has and the window did not.
        //
        // WHY IT IS A RATE CHANGE AT ALL, given that the receiver was the preferred lever: it is not
        // instead of the receiver, it is the other half. An exponential ease reproduces the motion it
        // is fed; fed five samples a second it converges five times a second, which is smooth but
        // COARSE — the board round already measured this ("Bewegen kommt nicht flüssig an" was
        // answered with 15 Hz + easing, not with easing alone, and defect (e) then showed the same
        // for the scale). BANDWIDTH IS A SHARED BUDGET and this is why the cost is bounded on three
        // sides: it never PRE-EMPTS (>= fastInterval, so a 90 Hz drag cannot become a 90 Hz packet
        // stream — the same guard every motion term here carries); it is true only while a hand is
        // actually on a shared window's bar, which is a human-paced act of at most a few seconds; and
        // AnyGrabbedHere() is false — after one Singleton test — for every scenario without a story
        // box open and for every client with the 3D map switched off, so an ordinary session's packet
        // cadence is byte-for-byte what it was.
        //
        // "GRABBED" RATHER THAN "THE POSE CHANGED": see SharedWindows.AnyGrabbedHere for why there is
        // no single last-sent pose to diff here and why the grab is the right superset.
        bool sharedWindowDue = WorldUI.SharedWindows.AnyGrabbedHere()
                               && _extrasAccumulator >= fastInterval;

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
            trayNow, CardsGameApi.ActiveHand(), out bool slotOrderSwapped);
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
                _useBarFlagsSample, _useBarCountSample, _useBarSlotSample);
        bool useBarsChanged = useBarMask != _lastSentUseBarMask;
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
        string? itemUseLabelNow = trayNow != null ? trayNow.ItemUseCapLabel : null;
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

        if (_extrasAccumulator < interval && !fxPending && !countsChanged && !browseChanged
            && !maskSizeChanged && !boardStyleChanged && !handScaleChanged
            && !poseDue && !boardUiChanged && !boardSnapDue && !capPressChanged && !highlightDue
            && !secondChanged && !secondDue && !secondCardChanged && !secondCardDue
            && !stretchDue
            && !tooltipChanged && !slotCardSizeChanged
            && !pileCountsChanged && !halfHoverDue && !halfSelChanged && !slotOrderChanged
            && !trackHoverDue
            && !wallFadesDue
            && !decisionChanged && !decisionStateChanged && !decisionWidgetChanged
            && !useBarsChanged
            && !capLabelsChanged && !focusChanged && !trackSelChanged && !trackOrderChanged
            && !emptyFanHintChanged && !tuningChanged && !itemClipChanged
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
            && !RemoteMapRoom.SendDue && !RemoteMapStory.SendDue
            // …and a shared window being CARRIED here raises the cadence to the rig rate for as long
            // as the hand is on it, exactly as a carried board does (see sharedWindowDue above).
            // Note this sits beside RemoteMapStory.SendDue and does NOT duplicate it: that getter is
            // about EDGES (a page turned, the quest window opened) and pre-empts outright; this one
            // is about MOTION and is capped at the rig interval.
            && !sharedWindowDue)
            return;
        _extrasAccumulator = 0f;
        _lastSentHandCount = handNow;
        _lastSentItemCount = itemsCount;

        var extras = default(PresenceState);

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
        // DECISION LINES (extension record 12): written on every packet WHILE a decision row is
        // docked; omitted otherwise, so an idle packet stays byte-identical to the previous
        // build's. The sampler already excluded everything that is not a pressable widget label.
        if (!string.IsNullOrEmpty(decisionNow))
        {
            extras.HasDecisionLines = true;
            extras.DecisionLinesText = decisionNow;
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
                  $"(UTF8, capped {NetProtocol.DecisionLinesMaxBytes} B: pressable-widget labels " +
                  "only, NO card identity); peers render them as inert plates at their copy's " +
                  "decision seat.");
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
                var opts = new System.Text.StringBuilder(48);
                for (int o = 0; o < decisionOptions; o++)
                {
                    if (o > 0)
                        opts.Append(", ");
                    byte f = _decisionOptionSample[o];
                    opts.Append('#').Append(o).Append('=')
                        .Append((f & NetProtocol.DecisionOptionOfferedBit) != 0 ? "OFFERED" : "greyed");
                    if ((f & NetProtocol.DecisionOptionDimmedBit) != 0)
                        opts.Append("+dim");
                    if ((f & NetProtocol.DecisionOptionChosenBit) != 0)
                        opts.Append("+CHOSEN");
                    if ((f & NetProtocol.DecisionOptionHoveredBit) != 0)
                        opts.Append("+HOVER");
                    if ((f & NetProtocol.DecisionOptionPressedBit) != 0)
                        opts.Append("+PRESS");
                }
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
                var roles = new System.Text.StringBuilder(48);
                for (int o = 0; o < decisionRoles; o++)
                {
                    if (o > 0)
                        roles.Append(", ");
                    roles.Append('#').Append(o).Append('=').Append(_decisionRoleSample[o]);
                }
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
                              "which used to read as a plain undo / 'USE' on every peer's board.");
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
            VRLog.Info("Net", slotOrder
                ? $"Slot order SENT: LEFT recess (slot 1) holds the " +
                  $"{(slotOrderSwapped ? "NON-INITIATIVE" : "INITIATIVE")} round card, RIGHT " +
                  $"recess (slot 2) the other — extension record 18, one bit against " +
                  "CCharacterClass.InitiativeAbilityCard (a replicated reference every client " +
                  "resolves to the same card, so this is an ORDER and not an identity). Peers stop " +
                  "re-deriving the pair's left/right from their own CardsHandUI.cardsUI sort, which " +
                  "is what put the two cards the wrong way round on somebody else's screen."
                : "Slot order SENT: none — record omitted (fewer than two resolvable round cards " +
                  "in our recesses, or neither is the initiative card). Peers keep their own " +
                  "initiative-first derivation, exactly as before this record existed.");
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
                }
                catch (Exception e) { LogPhaseError($"Apply extras packet from player {kv.Key}", e); }
            }
            _pendingExtras.Clear();
        }

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
    /// environment (or the game) hands the clock back within a breath.</summary>
    private const float EnvClockStaleSeconds = 3f;

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
                Board.CharacterFocus.ForgetPeer(id);   // …and their focus outline goes with them
                _peerEnv.Remove(id);                   // …and they stop being a clock/host candidate
                RemoteTestTriggers.ForgetPeer(id);     // …and any debug override they owned is released
            }
        }
    }

    /// <summary>Immediate teardown entry point for a future <c>PlayerRegistry.OnPlayerLeft</c>
    /// hook (staleness already handles it after <see cref="NetProtocol.StaleTimeoutSeconds"/>).</summary>
    public void RemovePlayer(int playerId)
    {
        _pending.Remove(playerId);
        _pendingExtras.Remove(playerId);
        _peerEnv.Remove(playerId); // a departed peer must not keep owning the environment clock
        if (_avatars.TryGetValue(playerId, out RemoteAvatar avatar))
        {
            avatar.Destroy();
            _avatars.Remove(playerId);
            NetFigures.ReleaseRemote(playerId);
            NetPlayerActors.ForgetAvatarFetch(playerId);
            Board.CharacterFocus.ForgetPeer(playerId);
        }
        // OUTSIDE the avatar branch on purpose: a peer can own a debug override without this client
        // ever having built an avatar for them (construction can fail and back off), and an override
        // that outlives the person holding it is the one failure this record may not have.
        RemoteTestTriggers.ForgetPeer(playerId);
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
