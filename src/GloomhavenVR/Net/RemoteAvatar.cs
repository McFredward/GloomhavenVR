using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Visual proxy for one remote VR player: a floating head "mask" + two floating hands
/// (Demeo style — no humanoid IK), anchored in the SHARED world frame and eased toward the
/// latest received pose with Lerp/Slerp (never snapped). Purely cosmetic; touches no game
/// state. Built lazily by <see cref="NetAvatarDriver"/> on the first rig packet from a peer
/// and destroyed on player-left or staleness.
///
/// Hands reuse the mod's own <see cref="HandVisuals"/> (bundle glove or procedural fallback)
/// so remote hands look exactly like the local ones. The head mask is the one the SENDER
/// picked in their VR settings (<see cref="AvatarState.MaskId"/>): it loads
/// <c>Assets/Bundle/Head/Mask_&lt;id&gt;.prefab</c> from the ALREADY-LOADED mod bundle via
/// <see cref="HeadMaskLibrary"/> (see PLAN.md "mask asset contract"); until the masks ship, a
/// low-poly placeholder head stands in so the feature is testable now. A peer changing their
/// mask at runtime rebuilds only the head (cheap; only on change).
///
/// Sender scale: each part holder is scaled by the sender's rig <c>WorldScale</c> so a remote
/// player's 15 cm hand reads the same physical size above the shared board regardless of the
/// sender's diorama zoom. Positions are absolute world (game units).
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY — this is the type that HOLDS the wire-sourced state. Head and
/// hand poses, finger curls, world scale, head-mask id + size, hand style, dominant hand, ghost
/// strength, held figure and held card exist nowhere in the game model, so every one of them costs
/// wire bytes (Part I §3–§4 of INVARIANTS-Net-Rig.md). Note the type-system tell: <c>RemoteAvatar</c>
/// is the wire-sourced type, and it appears in exactly ONE remote-widget signature
/// (<c>RemoteBoardFurniture.Refresh</c>) — everywhere else the widgets take a <c>CPlayerActor</c>,
/// which is the model-sourced one. See INVARIANTS-Net-Rig.md "Net — content
/// classification".</remarks>
internal sealed class RemoteAvatar
{
    private readonly GameObject _root;
    private readonly Transform _headHolder;
    private readonly Transform _leftHolder;
    private readonly Transform _rightHolder;
    private readonly Color _tint;

    // Not readonly: rebuilt in place when the sender switches their hand style.
    private HandRig? _leftRig;
    private HandRig? _rightRig;
    private FingerCurler? _leftCurler;
    private FingerCurler? _rightCurler;

    private readonly RemoteHandFan _handFan;
    private readonly RemoteControlBoard _controlBoard;
    private readonly RemoteItemFan _itemFan;   // report 5: the peer's equipped-item fan

    /// <summary>The mirror of their "Keine Handkarten" placard (record 14, byte 1 bit 4) — the
    /// one control-board-adjacent display that had no mirror at all before it.</summary>
    private readonly RemoteEmptyFanHint _emptyFanHint;
    private readonly RemoteCardFx _cardFx;     // report 6: replayed card animations
    private readonly RemoteBrowserFan _browserFan; // the peer's discard/burnt pile-browse reading fan
    private readonly RemoteNameTag _nameTag;   // username + Steam avatar floating above the mask

    // Card-FX de-duplication. The event byte is re-sent for redundancy on the unreliable extras
    // stream, so the flight plays only when the SEQUENCE changes. _fxSeqInit exists because the
    // very first extras packet from a peer may already carry an old event (they were mid-flight
    // when we joined) — playing that would fire a stray card across the table on join.
    private byte _lastFxSeq;
    private bool _fxSeqInit;

    // Ghost hand (extras FlagExtrasGhostHand): when the sender fades the hand carrying their open
    // fan, OUR copy of that hand must fade too — otherwise the sender's view of themselves and
    // everyone else's view of them disagree. ONE ghost is enough: the fan is always on the
    // sender's non-dominant hand, and HandGhost restores the old rig by itself when that side
    // flips (dominant-hand switch) or when BuildHands hands it a new rig instance.
    private readonly HandGhost _ghostLeft;
    private readonly HandGhost _ghostRight;

    private AvatarState _target;
    private bool _hasTarget;
    private float _appliedScale = -1f;
    private float _appliedStyleScale = -1f; // per-style visual scale currently on the hand visual roots
    private int _appliedMaskId = -1; // which HeadMaskLibrary mask the head currently shows
    private int _appliedHandStyle = -1; // which HandStyle the hand holders currently wear

    // Head-mask SIZE (extras block, byte A bit 4). The size lives on the "HeadVisual" CHILD of the
    // head holder, never on the holder itself — the holder carries the sender's diorama scale and
    // would stomp it (see HeadMaskLibrary.BuildHead). _appliedMaskSize is the change latch so the
    // per-frame check is free until the sender actually turns their stepper.
    private Transform? _headVisual;
    private float _appliedMaskSize = -1f;
    private float _loggedMaskSize = -1f; // one log line per received CHANGE, never per packet
    private int _loggedBoardStyle = -1;  // ditto for the received control-board style
    private float _loggedSlotCardWidth = -1f; // ditto for the received slot-card size (record 11)
    private bool _loggedPinned;          // ditto for the received FOLLOW/PIN state (first packet + changes)
    private bool _loggedSlots;           // ditto for the received card-slot occupancy nibble

    // Held-card slab (additive FlagHeldCard wire field): one both-faces-back card slab eased
    // toward the sender's held-card pose — a card in a peer's HAND, distinct from their fan.
    // Backs only, mirroring the fan's anti-cheat stance (no card identity is ever on the wire).
    private Transform? _heldCardHolder;
    private Mesh? _heldCardMesh;
    private bool _heldCardBillboardLogged; // one-line confirm the receiver-side billboard fired

    // SECOND held-card slab (extras extension record NetProtocol.ExtIdSecondHeldCard): the card
    // in the sender's OTHER hand while both hands hold one. The SAME machinery as the first slab
    // — same lazy build, same easing, same head billboard — driven from the extras stream instead
    // of the rig packet's FlagHeldCard block. Record absent ⇒ slab hidden; peer stale/left ⇒ the
    // whole root (both slabs) is destroyed; no state survives a scenario load because the avatar
    // itself does not.
    private Transform? _secondCardHolder;
    private Mesh? _secondCardMesh;
    private bool _hasSecondHeldCard;
    private RigPose _secondHeldCardPose;

    /// <summary>Seconds since the last accepted packet (staleness bookkeeping).</summary>
    public float TimeSinceUpdate { get; private set; }

    public int PlayerId { get; }

    // ---- PUBLIC SEAM (read-only) — the feature workers (ghost fan / control board) build off
    //      these holders + received data without reaching into RemoteAvatar's privates. -------

    /// <summary>The avatar's root transform (world origin of the whole proxy).</summary>
    public Transform Root => _root.transform;

    /// <summary>Head-mask holder transform.</summary>
    public Transform HeadHolder => _headHolder;

    /// <summary>
    /// The last RECEIVED head world position, i.e. the interpolation TARGET rather than
    /// <see cref="HeadHolder"/>'s eased pose. False before the first rig packet with a valid head.
    ///
    /// <para>WHY THE TARGET AND NOT THE HOLDER: the only consumer is
    /// <see cref="Rig.SpawnRing"/>, which runs in the first seconds after a join — exactly when a
    /// freshly created avatar's holder may still be inactive or mid-ease (see
    /// <c>UpdatePart</c>'s first-activation snap). The target is the peer's true position from the
    /// very first packet, which is what a "where is everyone standing?" question needs.</para>
    /// </summary>
    public bool TryGetHeadWorld(out Vector3 position)
    {
        if (_hasTarget && _target.HeadValid)
        {
            position = _target.Head.Position;
            return true;
        }
        position = Vector3.zero;
        return false;
    }

    /// <summary>Left-hand holder transform.</summary>
    public Transform LeftHandHolder => _leftHolder;

    /// <summary>Right-hand holder transform.</summary>
    public Transform RightHandHolder => _rightHolder;

    /// <summary>The holder for the sender's NON-dominant hand (where the card fan sits). When the
    /// sender is right-dominant the non-dominant hand is the Left hand, and vice versa.</summary>
    public Transform NonDominantHandHolder => DominantRight ? LeftHandHolder : RightHandHolder;

    /// <summary>
    /// The sender's PALM anchor for a hand HOLDER (<c>HandRig.PalmCenter</c>: +Y is the palm normal
    /// OUT of the palm, +Z along the fingers), or null before the hands are built.
    ///
    /// WHY THIS EXISTS: the holder transform carries the sender's <c>Rig.Root</c> pose verbatim (that
    /// is what <see cref="LocalRigSampler"/> puts on the wire), and by the HandRig contract the hand
    /// ROOT's +Y points out of the BACK of the hand — the palm normal is the PALM anchor's +Y, which
    /// for the procedural hand is a 180° Z-flip of the root and for a glove prefab is whatever
    /// <c>Anchor_Palm</c> was authored as. Anything the OWNER hangs off <c>Rig.PalmCenter</c> — the
    /// card fan, the item fan, the pile browser — therefore has to be reproduced off THIS transform
    /// on the receiver, not off the holder, or it floats out of the wrong face of the peer's hand.
    /// Costs no wire: we build the peer's hand from the SAME <see cref="HandVisuals"/> rig they do,
    /// so their palm anchor is already sitting here, exact.
    /// </summary>
    public Transform? PalmAnchorFor(Transform holder)
    {
        HandRig? rig = ReferenceEquals(holder, _leftHolder) ? _leftRig
            : ReferenceEquals(holder, _rightHolder) ? _rightRig
            : null;
        Transform? palm = rig != null ? rig.PalmCenter : null;
        return palm != null ? palm : null; // Unity-null collapse: a destroyed anchor reads as null
    }

    /// <summary>Stable per-player tint (matches the head/hand tint).</summary>
    public Color Tint => _tint;

    /// <summary>The sender-scale currently applied to the part holders (1 until the first packet).</summary>
    public float AppliedScale => _appliedScale > 0f ? _appliedScale : 1f;

    // ---- received extras / rig data (world frame) --------------------------------------------

    /// <summary>True when the sender broadcast a control-board world pose.</summary>
    public bool HasBoard { get; private set; }

    /// <summary>Control-board world position (valid when <see cref="HasBoard"/>).</summary>
    public Vector3 BoardPosition { get; private set; }

    /// <summary>Control-board world rotation (valid when <see cref="HasBoard"/>).</summary>
    public Quaternion BoardRotation { get; private set; } = Quaternion.identity;

    /// <summary>Control-board uniform scale (valid when <see cref="HasBoard"/>).</summary>
    public float BoardScale { get; private set; } = 1f;

    /// <summary>How many cards are in the sender's hand fan (rendered as backs only).</summary>
    public int HandCardCount { get; private set; }

    /// <summary>How many cards are in the sender's open ITEM fan (0 = closed / sender predates the
    /// field). Rendered as backs only by <see cref="RemoteItemFan"/>.</summary>
    public int ItemCardCount { get; private set; }

    /// <summary>True when that item fan is hand-held rather than anchored above their board.</summary>
    public bool ItemFanHeld { get; private set; }

    /// <summary>True when the hand-held item fan rides the sender's LEFT hand.</summary>
    public bool ItemFanLeftHand { get; private set; }

    /// <summary>
    /// Which position of that item fan lies CLIPPED IN the sender's item-USE recess right now, or
    /// -1 (extension record 26). An index into the very same ordered fan
    /// <see cref="ItemCardCount"/> counts — never an item identity; the face, as always, is resolved
    /// locally from the replicated inventory behind <see cref="RevealGate"/>.
    ///
    /// <para>-1 for a sender that predates the record AND for one whose recess is empty, which is
    /// correct in both cases: the card is then drawn in the arc, exactly as every build before this
    /// one drew it. The value is NOT range-checked here — <see cref="RemoteItemFan"/> clamps it
    /// against its own live slab count, the only place the bound is really known.</para>
    /// </summary>
    public int ItemUseClipIndex { get; private set; } = -1;

    /// <summary>
    /// The mirrored item-USE RECESS of this peer's board (null while their board is not built) —
    /// the frame <see cref="RemoteItemFan"/> lays <see cref="ItemUseClipIndex"/>'s slab into, so it
    /// LIES IN the recess and rides the board rather than billboarding to a head.
    /// </summary>
    internal UnityEngine.Transform? ItemUseRecess => _controlBoard.ItemUseRecess;

    /// <summary>True while the sender has a control-board PILE BROWSER open (the "Abgelegt" /
    /// "Verbrannt" reading fan). False for peers that predate the field — they simply show no fan.
    /// Rendered by <see cref="RemoteBrowserFan"/> as backs only.</summary>
    public bool PileBrowseOpen { get; private set; }

    /// <summary>Which pile that browser reads (<see cref="NetProtocol.PileBrowseKindDiscard"/> /
    /// <c>…Burnt</c> / <c>…Items</c>); meaningful only when <see cref="PileBrowseOpen"/>.</summary>
    public byte PileBrowseKind { get; private set; }

    /// <summary>How many cards are in that open browse fan (0 when closed).</summary>
    public int PileBrowseCardCount { get; private set; }

    /// <summary>True when that browse fan is a hand-held reading fan rather than board-anchored.</summary>
    public bool PileBrowseHeld { get; private set; }

    /// <summary>True when the hand-held browse fan rides the sender's LEFT hand.</summary>
    public bool PileBrowseLeftHand { get; private set; }

    /// <summary>True when the sender's dominant hand is the RIGHT hand (default true).</summary>
    public bool DominantRight { get; private set; } = true;

    /// <summary>Uniform SIZE multiplier the sender chose for their head mask (1 = authored size).
    /// 1 for peers that predate the field or wear the default size — both mean "unchanged look",
    /// which is why the wire only carries the byte when it differs.</summary>
    public float MaskSize { get; private set; } = 1f;

    /// <summary>The SENDER's per-style hand scale (1 = they are on the default / predate the field).</summary>
    public float HandScale { get; private set; } = 1f;

    /// <summary>
    /// The CONTROL BOARD this peer actually uses (their <c>Cards.ControlBoard</c> choice, received
    /// in the extras block's byte A bits 5..6). Oak for peers that predate the field or use the
    /// default board — both mean "the default look", which is why the wire spends no presence bit
    /// on it. Consumed by <see cref="RemoteControlBoard"/> to render our copy of their board from
    /// the REAL prefab of the board they picked (<see cref="RemoteTrayVisual"/>).
    /// </summary>
    public Cards.ControlBoard BoardStyle { get; private set; } = Cards.ControlBoard.Oak;

    /// <summary>
    /// The LIVE board-local layout of this peer's board — the real prefab's measured recess
    /// anchors once the 3D visual is built, the authored defaults before. The card-FX flights and
    /// the board-anchored fans resolve their board targets through THIS (world = board pose ×
    /// this × board scale) so they land on the rendered board, whatever style the peer runs.
    /// </summary>
    internal UnityEngine.Vector3 BoardAnchorLocal(CardFxAnchor anchor) =>
        _controlBoard.AnchorLocalLive(anchor);

    /// <summary>
    /// True when the sender broadcasts their live BOARD-UI state (extension record 4): which
    /// controls their own board currently shows + the wanted-slot glow mask. False for peers that
    /// predate the field — <see cref="RemoteBoardFurniture"/> then keeps the legacy always-drawn
    /// furniture, so an old peer's board looks exactly as before.
    /// </summary>
    public bool HasBoardUi { get; private set; }

    /// <summary>
    /// Board-local width of the sender's slot FRAME metric — their
    /// <c>CardWidth × PlayTray.SlotScale</c>, what the wanted-glow/frame overlays are sized from
    /// (extension record 11). 0 while unknown (sender predates the record, or their config equals
    /// the legacy constant) — consumers then fall back to
    /// <see cref="NetProtocol.SlotCardWidthLegacy"/>, the exact constant every build before the
    /// record hardcoded, so an old peer renders precisely as it always did.
    /// </summary>
    public float SlotFrameWidth { get; private set; }

    /// <summary>Board-local width a CARD parked in the sender's recess renders at — their
    /// <c>CardWidth × SlotScale × SlotCardFill</c> (extension record 11). Same 0-means-legacy
    /// contract as <see cref="SlotFrameWidth"/>. This is the size the user's 1:1 rule is about:
    /// the card-to-board ratio on the remote board must equal what the owner sees.</summary>
    public float SlotCardWidth { get; private set; }

    /// <summary>
    /// The owner's OWN DIAL POSITIONS for their board, board mesh and hand fan (extension record
    /// 28), resolved against this client's shipped defaults for every dial they have NOT moved.
    ///
    /// <para>Always valid: a peer who has tuned nothing — and a peer predating the record — yields
    /// the shipped layout for their synced style, which is exactly what every remote visual seated
    /// itself from before this record existed. Rebuilt only when the payload actually changes, so
    /// the per-frame consumers read plain floats.</para>
    /// </summary>
    public RemoteBoardTuning BoardTuning { get; private set; }

    /// <summary>Bumped every time <see cref="BoardTuning"/> is re-resolved (a dial moved on the
    /// owner's side, or their board style changed). Constructor-sized visuals latch it and rebuild
    /// on a mismatch — the same teardown-rebuild trigger the slot-card size uses.</summary>
    public int BoardTuningRevision { get; private set; }

    /// <summary>
    /// THIS PEER'S record-28 PAGE ACCUMULATOR — the receiving half of the paging that removed the
    /// board-tuning record's capacity ceiling (see <see cref="BoardTunePages"/>).
    ///
    /// <para>It holds the pages of the generation currently arriving and publishes an assembled
    /// <c>[n][fields]</c> payload only when it holds ALL of them, which is what guarantees that this
    /// client never draws a peer's board from half a tuning. Its own byte compare is also the change
    /// detector, and that compare is load-bearing for a second reason:
    /// <c>PresenceSerializer.DecodeBoardTuning</c> keeps ONE cache entry for the whole process, so
    /// with two differently-tuned peers at the table their pages evict each other and every packet
    /// hands back a fresh array. A reference compare would read as "the tuning changed" on every
    /// packet and tear down and rebuild both control boards five times a second.</para>
    /// </summary>
    private readonly BoardTunePageAssembler _tuningPages = new BoardTunePageAssembler();

    /// <summary>The board style the published assembly was last RESOLVED for. A style change
    /// re-resolves the same bytes against different shipped defaults, so it is a change even when
    /// not one page moved.</summary>
    private int _tuningStyle = -1;

    /// <summary>One-shot guard for the page-refusal error line — it would otherwise repeat at the
    /// packet rate for as long as the offending peer is at the table.</summary>
    private bool _loggedTuningRefusal;

    /// <summary>True while the owner's "Keine Handkarten" placard is on their screen (record 14,
    /// byte 1 bit 4). A LEVEL: the mirror runs its own fade from its own rising edge and follows
    /// this down. Absent/clear for every peer predating the bit, which renders as no placard —
    /// what those peers produced anyway.</summary>
    public bool EmptyFanHint { get; private set; }

    /// <summary>The owner's PICK-STATUS line (extension record 7), or null while their placard is
    /// down — including for a sender that predates the record, which renders identically.</summary>
    public string? PickBannerText { get; private set; }

    /// <summary>The BOARD TOOLTIP the owner is reading (extension record 9, identity-gated on
    /// their side), or null while none is shown — including for a sender that predates the
    /// record, which renders identically. Shown at the remote board's tooltip area
    /// (<see cref="RemoteBoardTooltip"/>).</summary>
    public string? TooltipText { get; private set; }

    /// <summary>The BUTTON LABELS of the owner's docked decision row (extension record 12), one
    /// per '\n'-separated line, or null while no row is docked — including for a sender that
    /// predates the record, which then renders the plain drawer via the board-UI decision bit.
    /// Rendered as inert plates at the remote board's decision seat
    /// (<see cref="RemoteBoardFurniture"/>).</summary>
    public string? DecisionLines { get; private set; }

    /// <summary>WHICH prompt the owner has docked (extension record 23 flags bits 0..2, one of
    /// <see cref="NetProtocol.DecisionKindNone"/> …). <see cref="NetProtocol.DecisionKindNone"/>
    /// while no state record rides — including for a sender that predates the record, whose
    /// mirrored plates then carry no states and no prompt line, exactly as before.</summary>
    public byte DecisionPromptKind { get; private set; }

    /// <summary>WHICH prompt-TEXT variant the owner is reading (record 23 flags bits 3..5). The
    /// receiver composes that line from its OWN localization — the composed text never rides the
    /// wire, see <see cref="NetProtocol.ExtIdDecisionState"/>.</summary>
    public byte DecisionTextVariant { get; private set; }

    /// <summary>Per-option state bytes of the owner's docked row (record 23), index-aligned with
    /// <see cref="DecisionLines"/>'s lines; null while no state record rides. Offered / dimmed /
    /// chosen — the three facts that make a mirrored row read like the owner's.</summary>
    public byte[]? DecisionOptionStates { get; private set; }

    /// <summary>Which USE BARS are docked AND visible on the owner's board (extension record 25),
    /// as <see cref="NetProtocol.UseBarActiveBonusBit"/> … <see cref="NetProtocol.UseBarItemsBit"/>
    /// in the owner's own stack order. 0 while no bar rides — including for a sender predating the
    /// record, whose remote board then shows nothing below the decision row, exactly as
    /// before.</summary>
    public byte UseBarsMask { get; private set; }

    /// <summary>Per-bar sub-picker flags (record 25), indexed by BAR INDEX 0..3; null while no bar
    /// rides. <see cref="NetProtocol.UseBarElementPickerBit"/> /
    /// <see cref="NetProtocol.UseBarOptionPickerBit"/>.</summary>
    public byte[]? UseBarFlags { get; private set; }

    /// <summary>Per-bar visible slot counts (record 25), indexed by bar index; null while no bar
    /// rides.</summary>
    public byte[]? UseBarSlotCounts { get; private set; }

    /// <summary>Per-slot state bytes (record 25) — offered / dimmed / chosen, bar <c>b</c> at
    /// <c>[b * NetProtocol.UseBarsMaxSlots …)</c>; null while no bar rides. NO slot identity is or
    /// can be here: the game's use slots carry no label at all, only card art, which never rides
    /// this wire (see <see cref="NetProtocol.ExtIdUseBars"/>).</summary>
    public byte[]? UseBarSlotStates { get; private set; }

    /// <summary>What the owner's CONFIRM cap actually reads (extension record 13 bit 0), or null
    /// — the receiver then letters the mirrored cap with the neutral GUI_CONFIRM fallback,
    /// exactly what pre-record senders get.</summary>
    public string? ConfirmCapLabel { get; private set; }

    /// <summary>What the owner's docked SKIP button actually reads (extension record 13 bit 1),
    /// or null — neutral GUI_SKIP_MOVEMENT fallback, same contract as
    /// <see cref="ConfirmCapLabel"/>.</summary>
    public string? SkipCapLabel { get; private set; }

    /// <summary>What the owner's UNDO keycap actually reads (extension record 13 bit 2) — their
    /// live undo wording, or the pick flow's dialog-CANCEL override — or null for the neutral
    /// GUI_UNDO fallback. Same contract as <see cref="ConfirmCapLabel"/>.</summary>
    public string? UndoCapLabel { get; private set; }

    /// <summary>What the owner's item-USE cap actually reads (extension record 13 bit 3) — "USE",
    /// or an item-SURRENDER demand's own wording — or null for the neutral uppercased GUI_USE
    /// fallback. A widget label, never an item name.</summary>
    public string? ItemUseCapLabel { get; private set; }

    /// <summary>Visible-controls bitmask (<see cref="NetProtocol.BoardUiConfirmBit"/> …),
    /// meaningful only when <see cref="HasBoardUi"/>.</summary>
    public byte BoardButtonsMask { get; private set; }

    /// <summary>The sender's wanted-slot glow mask (bit0 = left slot, bit1 = right), meaningful
    /// only when <see cref="HasBoardUi"/>. The blink itself is animated locally at the shared
    /// period — synced state, local clock.</summary>
    public int WantedGlowMask { get; private set; }

    /// <summary>
    /// Which recess the sender's own GOLD SNAP GLOW is lit on (0/1), or -1 for none — the HOVER
    /// telegraph "the card I am holding lands here on release" (board-UI record byte 1 bits 6..7).
    /// -1 both when no rim is lit and for a sender that predates the field;
    /// <see cref="RemoteBoardFurniture"/> tells the two apart by <see cref="HasBoardUi"/> and keeps
    /// its legacy occupancy-edge flash for the latter.
    /// </summary>
    public int SnapGlowSlot { get; private set; } = -1;

    /// <summary>
    /// True when the board-UI record carried its cap-STATE byte (record 4 byte 2). False for a
    /// sender that predates it — <see cref="RemoteBoardFurniture"/> then leaves every mirrored cap
    /// at the colour it was BUILT with, exactly what those builds rendered.
    /// </summary>
    public bool HasCapStates { get; private set; }

    /// <summary>The owner's live cap-STATE bits (<see cref="NetProtocol.BoardUiCapConfirmAccentBit"/>
    /// …), meaningful only when <see cref="HasCapStates"/>.</summary>
    public byte CapStateMask { get; private set; }

    /// <summary>
    /// TRUE while at least one of the owner's equipped items can be played RIGHT NOW — i.e. while
    /// their own closed items pile is wearing its heartbeat cue (board-UI record byte 2 bit 7,
    /// <see cref="NetProtocol.BoardUiCapItemPileUsableBit"/>).
    ///
    /// <para>Resolves to FALSE for a sender that predates the bit, which is exactly what every
    /// earlier build's receiver drew (nothing at all on the mirrored stack). The peer's copy of the
    /// cue is animated entirely on THIS client's clock from the edges of this flag — the same
    /// synced-state / locally-animated split the wanted-slot glow and the keycap dust use, so a cue
    /// that beats twice a second costs no traffic beyond the two edges.</para>
    /// </summary>
    public bool ItemsPileUsableCue { get; private set; }

    /// <summary>
    /// The owner's most recent board keycap PRESS as a (cap id, sequence) pair packed
    /// <c>cap | seq &lt;&lt; 8</c>, or -1 while none is in flight. The renderer plays the mirrored
    /// dip when this value CHANGES — the field is a latch that rides several packets, so "set" is
    /// not an event but "changed" is (see <see cref="NetProtocol.CapPressNone"/>).
    /// </summary>
    public int CapPressKey { get; private set; } = -1;

    /// <summary>
    /// True when the sender tells us which of its CARD SLOTS hold a card (board-UI record byte 1
    /// bit 5). False for a sender that predates the nibble — <see cref="RemoteControlBoard"/> then
    /// falls back to rendering the slots purely from the host-replicated model, exactly as every
    /// build before this one did, which is why "no cards" and "old peer" can never be confused.
    /// </summary>
    public bool SlotOccupancyKnown { get; private set; }

    /// <summary>
    /// Which of the owner's two card slots PHYSICALLY hold a card right now (bit0 = left/Slot1,
    /// bit1 = right/Slot2), meaningful only when <see cref="SlotOccupancyKnown"/>. A POSITION, never
    /// an identity: the receiver draws a card BACK there and resolves the actual card, if it may be
    /// shown at all, from the replicated model behind <see cref="RevealGate"/> exactly as before.
    /// </summary>
    public int BoardSlotMask { get; private set; }

    /// <summary>
    /// True when the sender's control board is PINNED (world-anchored) rather than FOLLOWing their
    /// rig — the live state of the FOLLOW/PIN keycap on their board (board-UI record byte 1 bit 2).
    /// False both for a peer who really is in FOLLOW mode and for one that predates the bit, which
    /// is deliberate: FOLLOW is the un-accented default look every previous build already drew.
    /// </summary>
    public bool TrayPinned { get; private set; }

    /// <summary>Index of the card the sender is SINGLING OUT in their hand fan, or -1. A position,
    /// never an identity (extension record 6); -1 both when nothing is lifted and when the sender
    /// predates the record — identical rendering either way.</summary>
    public int HandHighlightIndex { get; private set; } = -1;

    /// <summary>Index of the card the sender is SINGLING OUT in their open BOARD fan (item fan or
    /// pile browser — at most one is open), or -1. Same contract as
    /// <see cref="HandHighlightIndex"/>.</summary>
    public int FanHighlightIndex { get; private set; } = -1;

    /// <summary>
    /// True when the sender broadcasts the counts their OWN pile-stack labels display (extension
    /// record 15). False for peers that predate the field — <see cref="RemoteControlBoard"/> then
    /// keeps the legacy model-read counts, exactly as every build before this one (which the
    /// session logs proved to lag a whole choreographer turn behind the owner's board).
    /// </summary>
    public bool HasPileCounts { get; private set; }

    /// <summary>The sender's displayed DISCARD stack count (meaningful only when
    /// <see cref="HasPileCounts"/>).</summary>
    public int PileDiscardCount { get; private set; }

    /// <summary>The sender's displayed BURNT stack count (with <see cref="HasPileCounts"/>).</summary>
    public int PileBurntCount { get; private set; }

    /// <summary>The sender's displayed ITEMS stack count (with <see cref="HasPileCounts"/>).</summary>
    public int PileItemsCount { get; private set; }

    /// <summary>Board slot (0/1) whose docked round card the sender is half-hovering in their
    /// action-selection layout, or -1 — including for senders that predate record 14, which
    /// render identically (no glow). A slot POSITION, never a card identity.</summary>
    public int HalfHoverSlot { get; private set; } = -1;

    /// <summary>True when the hovered half is the TOP action (meaningful only while
    /// <see cref="HalfHoverSlot"/> is not -1).</summary>
    public bool HalfHoverTop { get; private set; }

    /// <summary>The persistently CLICKED half of the sender's slot-0 round card (record 14
    /// byte 1 — the game's own steady click highlight, cleared by their undo):
    /// <see cref="NetProtocol.HalfSelectNone"/> / <c>…Top</c> / <c>…Bottom</c>. None when the
    /// record is absent. Rendered steady and distinct from the pulsing hover.</summary>
    public int HalfSelect0 { get; private set; } = NetProtocol.HalfSelectNone;

    /// <summary>Slot 1's persistently clicked half, same contract as <see cref="HalfSelect0"/>.</summary>
    public int HalfSelect1 { get; private set; } = NetProtocol.HalfSelectNone;

    /// <summary>Stable <c>CActor.ID</c> of the initiative-track entry the sender is hovering, or
    /// 0 (none / pre-record-16 sender — both render an un-hovered track). Consumed by
    /// <see cref="RemoteInitiativeTrack"/>, which lifts the matching entry on ITS copy of the
    /// public track widget.</summary>
    public int TrackHoverActorId { get; private set; }

    /// <summary>True when the sender's track hover has the entry's info popup open (meaningful
    /// only while <see cref="TrackHoverActorId"/> is non-zero).</summary>
    public bool TrackHoverPopup { get; private set; }

    /// <summary>How many entries the sender's OWN initiative track is framing with vanilla's
    /// selection frame (extension record 23); 0 = no frame, which is also what a pre-record-23
    /// sender reads as. Consumed by <see cref="RemoteInitiativeTrack"/>, which switches the SAME
    /// frame on ITS clone of the public track widget — and only there.</summary>
    public int TrackSelectionCount { get; private set; }

    /// <summary>Stable ids (the shared ActorGuid hash) of those framed entries — players, ENEMIES
    /// and object actors alike. Only the first <see cref="TrackSelectionCount"/> entries are
    /// meaningful; the array is replaced wholesale on every packet that carries the record, so a
    /// reader never sees a half-updated set.</summary>
    public int[] TrackSelectionIds { get; private set; } = System.Array.Empty<int>();

    /// <summary>How many PLAYER entries the sender's OWN initiative track shows (extension record
    /// 27); 0 = no record, which means "outside the online card-selection phase" — the window in
    /// which vanilla's <c>CompareTo</c> sorts player entries by <c>IsUnderMyControl</c> and the
    /// only one in which the order is per-viewer at all. Consumed by
    /// <see cref="RemoteInitiativeTrack"/>, which re-deals the row x of ITS clone.</summary>
    public int TrackOrderCount { get; private set; }

    /// <summary>Bit k = <see cref="TrackOrderIds"/>[k] is a character the SENDER controls — the
    /// local flag no receiver can evaluate, and the gate on whose portraits the mirrored
    /// selection-phase "still has to choose" ring may appear.</summary>
    public byte TrackOrderOwnedMask { get; private set; }

    /// <summary>Stable ids (the shared ActorGuid hash) of the sender's PLAYER track entries, in
    /// THEIR on-screen order. Only the first <see cref="TrackOrderCount"/> entries are meaningful;
    /// the array is replaced wholesale on every packet that carries the record, so a reader never
    /// sees a half-updated order.</summary>
    public int[] TrackOrderIds { get; private set; } = System.Array.Empty<int>();

    /// <summary>True when the sender transmitted the board-local anchor of their open
    /// BOARD-ANCHORED fan (extension record 5). Absent ⇒ the authored default spot.</summary>
    public bool HasFanAnchor { get; private set; }

    /// <summary>The open board-anchored fan's position in the sender's board-LOCAL frame
    /// (meaningful only when <see cref="HasFanAnchor"/>). Applied as
    /// <c>BoardPosition + BoardRotation · (this × BoardScale)</c>.</summary>
    public Vector3 FanAnchorLocal { get; private set; }

    /// <summary>True while the sender's fan-carrying hand is faded ("ghost hand"). False for peers
    /// that predate the field — their hands simply stay solid.</summary>
    public bool GhostHand { get; private set; }

    /// <summary>True when the sender named WHICH hands are ghosted (extension record); without it
    /// the legacy inference below applies.</summary>
    public bool HasGhostSides { get; private set; }

    /// <summary>Bitmask of the sender's ghosted hands (meaningful when <see cref="HasGhostSides"/>).</summary>
    public byte GhostSidesMask { get; private set; }

    /// <summary>Material alpha the sender's ghost hand should be drawn at (1 = opaque), decoded
    /// from the transmitted strength byte. Meaningful only when <see cref="GhostHand"/>.</summary>
    public float GhostAlpha { get; private set; } = 1f;

    /// <summary>True when the sender is physically holding a figure this frame.</summary>
    public bool HasHeldFigure { get; private set; }

    /// <summary>Stable id of the held figure (valid when <see cref="HasHeldFigure"/>).</summary>
    public int HeldFigureActorId { get; private set; }

    /// <summary>Held-figure world position (valid when <see cref="HasHeldFigure"/>).</summary>
    public Vector3 HeldFigurePosition { get; private set; }

    /// <summary>Held-figure world rotation (valid when <see cref="HasHeldFigure"/>).</summary>
    public Quaternion HeldFigureRotation { get; private set; } = Quaternion.identity;

    /// <summary>
    /// True when the sender is holding a SECOND board figure — one mini per hand (extras extension
    /// record <see cref="NetProtocol.ExtIdSecondFigure"/>). The mini itself is driven by
    /// <see cref="NetFigures"/> off the same record; this seam mirrors the first figure's one so
    /// anything that wants to draw or reason about a peer's carried minis sees BOTH.
    /// </summary>
    public bool HasSecondHeldFigure { get; private set; }

    /// <summary>Stable id of the second held figure (valid when
    /// <see cref="HasSecondHeldFigure"/>).</summary>
    public int SecondHeldFigureActorId { get; private set; }

    /// <summary>Second held figure's world position (valid when
    /// <see cref="HasSecondHeldFigure"/>).</summary>
    public Vector3 SecondHeldFigurePosition { get; private set; }

    /// <summary>Second held figure's world rotation (valid when
    /// <see cref="HasSecondHeldFigure"/>).</summary>
    public Quaternion SecondHeldFigureRotation { get; private set; } = Quaternion.identity;

    /// <summary>True when that second mini rides the sender's LEFT hand. Meaningful only when
    /// <see cref="HasSecondHeldFigure"/>.</summary>
    public bool SecondHeldFigureLeftHand { get; private set; }

    /// <summary>True when the FIRST held figure rides the sender's LEFT hand. Only the
    /// second-figure record carries hands (the rig flag byte has no bit left), so this is
    /// meaningful only while <see cref="HasSecondHeldFigure"/> — which is the only time the two
    /// hands have to be told apart.</summary>
    public bool HeldFigureLeftHand { get; private set; }

    public RemoteAvatar(int playerId)
    {
        PlayerId = playerId;

        _root = new GameObject($"GloomhavenVR.RemoteAvatar[{playerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.position = Vector3.zero;
        _root.transform.rotation = Quaternion.identity;
        _root.transform.localScale = Vector3.one;

        _tint = TintFor(playerId);

        _headHolder = new GameObject("Head").transform;
        _headHolder.SetParent(_root.transform, worldPositionStays: false);
        BuildHeadMask(0); // mask 0 until the first packet reports the sender's real choice
        _headHolder.gameObject.SetActive(false);

        _leftHolder = new GameObject("Hand_Left").transform;
        _leftHolder.SetParent(_root.transform, worldPositionStays: false);
        _leftHolder.gameObject.SetActive(false);

        _rightHolder = new GameObject("Hand_Right").transform;
        _rightHolder.SetParent(_root.transform, worldPositionStays: false);
        _rightHolder.gameObject.SetActive(false);

        // The ghosts MUST exist before the first BuildHands call: BuildHands releases them
        // before tearing the old hand objects down. Constructing them after BuildHands NRE'd
        // the whole ctor on every remote player — the second-multiplayer-test bug in which the
        // joiner's driver died in ApplyPending each frame (starving its own TickSend, so the
        // HOST saw nothing either) and no avatar was ever built.
        _ghostLeft = new HandGhost($"remote[{playerId}] L");
        _ghostRight = new HandGhost($"remote[{playerId}] R");

        // Default Glove until the first packet reports the sender's real style —
        // mirrors the mask-0 default above.
        BuildHands(0);

        // Whole subtree onto the mod layer so the owned head camera renders it (no-op when
        // VR is not running, exactly like the local hands).
        VRLayers.Apply(_root);

        // Cosmetic add-ons built off the public seam (ghost card fan + read-only control board).
        // Foundation stubs today; the feature workers fill their bodies. Owned here: ticked from
        // Tick and torn down from Destroy.
        // The tuning must be VALID before the first extras packet: RemoteControlBoard can build on
        // an earlier frame, and a default(RemoteBoardTuning) is all zeros — every dock at the board
        // origin. Seed it with the shipped Oak layout, i.e. exactly what this client rendered before
        // record 28 existed; the first packet re-resolves it for the peer's real style.
        BoardTuning = new RemoteBoardTuning(Cards.ControlBoard.Oak, null, 0);

        _handFan = new RemoteHandFan(this);
        _controlBoard = new RemoteControlBoard(this);
        _itemFan = new RemoteItemFan(this);
        _emptyFanHint = new RemoteEmptyFanHint(this);
        _cardFx = new RemoteCardFx(this);
        _browserFan = new RemoteBrowserFan(this);
        _nameTag = new RemoteNameTag(this); // appended last — never reorder the ctor above (ghosts-before-BuildHands)

        VRLog.Info("Net", $"Remote avatar created for player {playerId}.");
    }

    /// <summary>Accept a freshly-decoded state as the new interpolation target.</summary>
    public void SetTarget(in AvatarState state)
    {
        _target = state;
        _hasTarget = true;
        TimeSinceUpdate = 0f;

        // Carry the rig packet's held-figure + dominant-hand data onto the seam (world frame:
        // the driver has already converted the poses). Consumed by the feature workers.
        DominantRight = state.DominantRight;
        HasHeldFigure = state.HasHeldFigure;
        if (state.HasHeldFigure)
        {
            HeldFigureActorId = state.HeldFigureActorId;
            HeldFigurePosition = state.HeldFigurePose.Position;
            HeldFigureRotation = state.HeldFigurePose.Rotation;
        }
        else
        {
            HeldFigureActorId = 0;
        }

        // Swap the head mask when the sender's choice changes (cheap; only on change).
        if (state.MaskId != _appliedMaskId)
            BuildHeadMask(state.MaskId);

        // Swap the hand pair when the sender's HAND STYLE changes (same pattern; peers
        // without the styled prefab in their bundle degrade to Glove inside HandVisuals).
        if (state.HandStyle != _appliedHandStyle)
            BuildHands(state.HandStyle);

        // Apply sender scale to the part holders when it changes (cosmetic sizing only).
        // The per-STYLE visual scale lives on the "HandVisual" child roots (BuildHands),
        // so this write can no longer stomp it.
        float scale = state.WorldScale > 0f ? state.WorldScale : 1f;
        if (!Mathf.Approximately(scale, _appliedScale))
        {
            _appliedScale = scale;
            _headHolder.localScale = Vector3.one * scale;
            _leftHolder.localScale = Vector3.one * scale;
            _rightHolder.localScale = Vector3.one * scale;
            if (_heldCardHolder != null)
                _heldCardHolder.localScale = Vector3.one * scale;
            if (_secondCardHolder != null)
                _secondCardHolder.localScale = Vector3.one * scale;
        }
    }

    /// <summary>Accept a freshly-decoded EXTRAS packet (board pose + hand count + dominant hand).
    /// Poses are already in world frame (converted by the driver).</summary>
    public void SetExtras(in PresenceState p)
    {
        HasBoard = p.HasBoard;
        if (p.HasBoard)
        {
            BoardPosition = p.Board.Position;
            BoardRotation = p.Board.Rotation;
            BoardScale = p.BoardScale > 0f ? p.BoardScale : 1f;
        }
        HandCardCount = p.HandCardCount;
        DominantRight = p.DominantRight;

        // SECOND HELD FIGURE (extension record 8): the mini in the sender's other hand. Absent ⇒
        // at most one figure held, which is also exactly what a peer predating the record sends —
        // so a plain reset is right in both cases and never strands a phantom second mini here.
        HasSecondHeldFigure = p.HasSecondFigure;
        if (p.HasSecondFigure)
        {
            SecondHeldFigureActorId = p.SecondFigureActorId;
            SecondHeldFigurePosition = p.SecondFigurePose.Position;
            SecondHeldFigureRotation = p.SecondFigurePose.Rotation;
            SecondHeldFigureLeftHand = p.SecondFigureLeftHand;
            HeldFigureLeftHand = p.PrimaryFigureLeftHand;
        }
        else
        {
            SecondHeldFigureActorId = 0;
        }

        // SECOND HELD CARD (extension record 10): the card in the sender's other hand while both
        // hold one, rendered by the same slab machinery the rig packet's held card uses
        // (UpdateCardSlab in Tick). Absent ⇒ at most one card held — which is also exactly what a
        // peer predating the record (or on build 49's documented compromise) sends — so a plain
        // reset is right in both cases: the slab hides, and the FIRST card's slab (rig packet) is
        // untouched.
        _hasSecondHeldCard = p.HasSecondHeldCard;
        if (p.HasSecondHeldCard)
            _secondHeldCardPose = p.SecondHeldCardPose;

        // Head-mask SIZE: the sender's own multiplier rides the wire (trailing-block byte A bit 4),
        // so their mask is the same size on every client — the project's standing MP rule that what
        // one player sees, everyone sees. An ABSENT byte means the default 1.00× (sender predates
        // the field, or simply never tuned it), never a broken size; that is also how a peer
        // stepping back to 1.00× is communicated, since we then stop sending the byte.
        MaskSize = p.HasMaskSize ? NetProtocol.DecodeMaskSize(p.MaskSizeCode) : 1f;
        HandScale = p.HasHandScale ? NetProtocol.DecodeHandScale(p.HandScaleCode) : 1f;
        if (!Mathf.Approximately(MaskSize, _loggedMaskSize))
        {
            VRLog.Info("Net", $"Mask size RECEIVED from player {PlayerId}: {MaskSize:0.00}x " +
                              (p.HasMaskSize
                                  ? $"(wire code {p.MaskSizeCode}, hundredths, extras block bit 4)."
                                  : "(no size byte — default/older peer)."));
            _loggedMaskSize = MaskSize;
        }

        // CONTROL-BOARD STYLE: which board this peer picked in THEIR settings (byte A bits 5..6 of
        // the same trailing block). Code 0 = the default board, which is also what an older peer's
        // zeroed bits read as — so an absent field degrades to today's look, never to a wrong one.
        // Our copy of their board tints itself from this in RemoteControlBoard.
        BoardStyle = Cards.ControlBoards.Clamp(p.BoardStyleCode);
        if ((int)BoardStyle != _loggedBoardStyle)
        {
            _loggedBoardStyle = (int)BoardStyle;
            VRLog.Info("Net", $"Control board style RECEIVED from player {PlayerId}: '{BoardStyle}' " +
                              $"(wire code {p.BoardStyleCode}, extras block byte A bits 5..6) — " +
                              "their board is drawn in the material THEY chose, the same rule the " +
                              "head mask, mask size and hand style already follow.");
        }

        // BOARD TUNING (extension record 28): the owner's OWN dial positions for their board,
        // board mesh and hand fan, arriving ONE PAGE PER PACKET (see BoardTunePages). The
        // accumulator holds the pages of the generation currently in flight and publishes only when
        // it holds ALL of them, so this client is never drawn from half a tuning and never flickers
        // a peer's board back to the shipped defaults while its owner drags a slider — until the
        // new generation is whole, the previous whole one stays in force.
        //
        // ABSENT RECORD ⇒ RESET, and that is not a shortcut: the sender writes a page on EVERY
        // extras packet while anything at all is tuned, so "no record" means exactly "every dial is
        // back at the shipped default". Falling back to this client's own compiled constants is
        // then precisely what it rendered before the record existed.
        bool tuningChanged;
        if (p.HasBoardTuning && p.BoardTuningBytes != null)
        {
            tuningChanged = _tuningPages.Accept(p.BoardTuningBytes, 0, p.BoardTuningLength);
        }
        else
        {
            tuningChanged = _tuningPages.AssembledLength > 0;
            if (tuningChanged)
                _tuningPages.Reset();
        }
        // A STYLE CHANGE IS A CHANGE TOO: the same bytes resolve against a different board's shipped
        // defaults, so every dial they have NOT moved lands somewhere else.
        if (tuningChanged || (int)BoardStyle != _tuningStyle)
        {
            _tuningStyle = (int)BoardStyle;
            BoardTuning = new RemoteBoardTuning(BoardStyle, _tuningPages.Assembled,
                                                _tuningPages.AssembledLength);
            BoardTuningRevision++;
            VRLog.Info("Net", $"Board tuning RECEIVED from player {PlayerId}: {BoardTuning} " +
                              $"(extension record 28, {_tuningPages}) — every dial they have MOVED " +
                              "is applied to their remote board, mesh pose and hand fan; every dial " +
                              "they have not is this client's shipped default, which is the same " +
                              "value.");
        }
        if (_tuningPages.Refused && !_loggedTuningRefusal)
        {
            // LOUD, ONCE. The paging bounds are all derived from the field-id space, so a
            // well-formed peer on our own ModBuild cannot trip one; if this ever fires, a dial is
            // NOT reaching this board and the ruling is being broken — which is exactly the thing
            // that must never happen quietly.
            _loggedTuningRefusal = true;
            VRLog.Error("Net", $"Board tuning REFUSED from player {PlayerId}: a record-28 page was " +
                               "structurally invalid (page index/count, field run, or an id whose " +
                               "width this build does not know). That peer's board is drawn from the " +
                               "last COMPLETE tuning, or from the shipped defaults if there has never " +
                               "been one — never from a half-parsed one. See NetProtocol.ExtIdBoardTuning.");
        }

        // EMPTY-FAN PLACARD (record 14, byte 1 bit 4): the owner's "Keine Handkarten" ghost plate
        // is up right now. A LEVEL — the mirror starts its own 1.5 s fade on the rising edge and
        // follows this down. Absent ⇒ false ⇒ no placard, exactly what a pre-bit peer renders.
        EmptyFanHint = p.EmptyFanHint;

        // SLOT-CARD SIZE (extension record 11): the widths the sender's own board renders its
        // slot overlays / a parked card at. Absent ⇒ 0 ⇒ every consumer falls back to the legacy
        // constant (NetProtocol.SlotCardWidthLegacy) — right both for a pre-record peer and for a
        // sender whose config equals the constant (which is why they omit it). Stateless reset per
        // packet, like the mask size: a peer stepping back to the legacy sizes is communicated by
        // the record disappearing again.
        SlotFrameWidth = p.HasSlotCardSize ? NetProtocol.DecodeSlotWidth(p.SlotFrameWidthCode) : 0f;
        SlotCardWidth = p.HasSlotCardSize ? NetProtocol.DecodeSlotWidth(p.SlotCardWidthCode) : 0f;
        if (!Mathf.Approximately(SlotCardWidth, _loggedSlotCardWidth))
        {
            _loggedSlotCardWidth = SlotCardWidth;
            VRLog.Info("Net", $"Slot-card size RECEIVED from player {PlayerId}: " +
                              (p.HasSlotCardSize
                                  ? $"frame {SlotFrameWidth * 1000f:0.0} mm, card " +
                                    $"{SlotCardWidth * 1000f:0.0} mm (board-local, extension " +
                                    "record 11) — their board's slot cards render here at exactly " +
                                    "the size they see (1:1 rule)."
                                  : "none (legacy 82.55 mm assumption — default config or " +
                                    "pre-record peer)."));
        }

        // Ghost hand: the sender's own strength rides the wire, so their faded hand reads the
        // same on every client. Decoded through the same AlphaFor curve the local hands use.
        GhostHand = p.GhostHand;
        HasGhostSides = p.HasGhostSides;
        GhostSidesMask = p.GhostSidesMask;
        GhostAlpha = p.GhostHand
            ? Hands.HandGhosts.AlphaFor(p.GhostStrength / 255f)
            : 1f;

        // Item fan (additive field): absent flag = the sender predates it OR their fan is closed —
        // both mean "show nothing", so a plain reset is correct in either case.
        ItemCardCount = p.HasItemFan ? p.ItemCardCount : 0;
        ItemFanHeld = p.HasItemFan && p.ItemFanHeld;
        ItemFanLeftHand = ItemFanHeld && p.ItemFanLeftHand;
        // ITEM-USE CLIP (extension record 26): which fan position lies in their use recess.
        // Absent ⇒ -1, i.e. "the recess is empty" — never a stale clip from a card that has since
        // been taken back out, because the sender writes the record on every packet while the card
        // is there and omits it on the frame it leaves.
        //
        // NO LONGER GATED ON HasItemFan (2026-08-09). The gate encoded "an index into a fan that is
        // not open names nothing", which stopped being true when the owner's placed card started
        // OUTLIVING their fan (ItemsPile._keptClip — the user's "sie soll liegen bleiben"): they lay
        // the card in the recess, click the arc away, and go on playing while it lies there, so the
        // fan flag is ABSENT for most of the decision. Read through the old gate, the peer's copy of
        // the card left their mirrored recess on the very frame the owner put their fan down — the
        // receiver-side twin of the defect. With the arc closed the index does not name a seat in a
        // fan; it names the SLAB <see cref="RemoteItemFan"/> is already holding on the recess (that
        // renderer keeps it out of the fold-in and re-adopts it on the next rebuild), and that
        // renderer is still the only place the value is range-checked.
        ItemUseClipIndex = p.HasItemUseClip ? p.ItemUseClipIndex : -1;

        // Board UI (extension record 4): authoritative when present — the furniture then shows
        // EXACTLY the controls the owner sees. Absent = the sender predates the field; the
        // furniture falls back to the legacy always-drawn look (never to "all hidden").
        // PICK BANNER (extension record 7): absent ⇒ null ⇒ the peer's placard is hidden. Never
        // a stale line from a pick step that has since resolved — the sender writes the record on
        // every packet while the placard is up and omits it the moment it comes down.
        string? banner = p.HasPickBanner ? p.PickBannerText : null;
        if (banner != PickBannerText)
        {
            PickBannerText = banner;
            VRLog.Info("Net", string.IsNullOrEmpty(banner)
                ? $"Pick banner RECEIVED from player {PlayerId}: none (placard down)."
                : $"Pick banner RECEIVED from player {PlayerId}: \"{banner}\" — shown on their " +
                  "remote board at the same board-local seat.");
        }

        // BOARD TOOLTIP (extension record 9): absent ⇒ null ⇒ the remote tooltip panel hides.
        // Never a stale text from a hover that ended — the sender writes the record on every
        // packet while the tooltip is up and omits it the moment it goes down (and whenever the
        // sender-side identity gate suppresses it).
        string? tooltip = p.HasBoardTooltip ? p.BoardTooltipText : null;
        if (tooltip != TooltipText)
        {
            TooltipText = tooltip;
            VRLog.Info("Net", string.IsNullOrEmpty(tooltip)
                ? $"Board tooltip RECEIVED from player {PlayerId}: none (hidden)."
                : $"Board tooltip RECEIVED from player {PlayerId}: {tooltip!.Length} chars — " +
                  "shown at their remote board's tooltip area.");
        }

        // DECISION LINES (extension record 12): absent ⇒ null ⇒ the mirrored decision buttons
        // hide (the drawer alone remains while the board-UI decision bit still says a prompt is
        // docked — the pre-record look). Never a stale row from a prompt that has since resolved:
        // the sender writes the record on every packet while a row is docked AND VISIBLE on their
        // board, and omits it the moment it undocks OR they look at another character (user ruling
        // 2026-08-08 — their board shows nothing there, so neither does this copy).
        string? decision = p.HasDecisionLines ? p.DecisionLinesText : null;
        if (decision != DecisionLines)
        {
            DecisionLines = decision;
            VRLog.Info("Net", string.IsNullOrEmpty(decision)
                ? $"Decision lines RECEIVED from player {PlayerId}: none (row undocked or hidden " +
                  "on the owner's own board)."
                : $"Decision lines RECEIVED from player {PlayerId}: " +
                  $"\"{decision!.Replace('\n', '|')}\" — mirrored as inert plates at their remote " +
                  "board's decision seat (labels only, no card identity on this wire).");
        }

        // DECISION STATE (extension record 23): the prompt kind, the prompt-TEXT variant and the
        // per-option offered/dimmed/chosen bytes. Rides record 12's own gate, so it appears and
        // disappears with the labels it describes; absence keeps the pre-record look (plates with
        // no state, no prompt line).
        byte kind = p.HasDecisionState ? p.DecisionPromptKind : NetProtocol.DecisionKindNone;
        byte textVariant = p.HasDecisionState ? p.DecisionTextVariant : NetProtocol.DecisionTextNone;
        byte[]? optionStates = p.HasDecisionState && p.DecisionOptionCount > 0
            ? p.DecisionOptionFlags
            : null;
        if (kind != DecisionPromptKind || textVariant != DecisionTextVariant
            || !SameOptionStates(optionStates, DecisionOptionStates))
        {
            DecisionPromptKind = kind;
            DecisionTextVariant = textVariant;
            DecisionOptionStates = optionStates;
            VRLog.Info("Net", !p.HasDecisionState
                ? $"Decision state RECEIVED from player {PlayerId}: none — their mirrored plates " +
                  "carry no option states and no prompt line (record 23 absent: no visible " +
                  "decision, or a sender predating the record)."
                : $"Decision state RECEIVED from player {PlayerId}: prompt kind {kind}, text " +
                  $"variant {textVariant}, {(optionStates?.Length ?? 0)} option state(s) " +
                  $"[{DescribeOptionStates(optionStates)}] — their remote board greys, dims and " +
                  "lights the mirrored plates exactly as the owner's own dock does, and composes " +
                  "the prompt line locally (the text itself never rides this wire).");
        }

        // USE BARS (extension record 25): the owner's SECOND drawer — which bars are up, each bar's
        // slot count + open sub-picker, and per slot offered/dimmed/chosen. Absent ⇒ mask 0 ⇒ this
        // board draws nothing below the mirrored decision row, which is both "the owner has no bars
        // up" and "the sender predates the record". A bar the owner render-hid because they are
        // looking at another character is already out of their published mask, so it disappears
        // here in the same frames it disappears there.
        byte barMask = p.HasUseBars ? p.UseBarsMask : (byte)0;
        byte[]? barFlags = barMask != 0 ? p.UseBarFlags : null;
        byte[]? barCounts = barMask != 0 ? p.UseBarSlotCounts : null;
        byte[]? barSlots = barMask != 0 ? p.UseBarSlotStates : null;
        if (barMask != UseBarsMask || !SameOptionStates(barFlags, UseBarFlags)
            || !SameOptionStates(barCounts, UseBarSlotCounts)
            || !SameOptionStates(barSlots, UseBarSlotStates))
        {
            UseBarsMask = barMask;
            UseBarFlags = barFlags;
            UseBarSlotCounts = barCounts;
            UseBarSlotStates = barSlots;
            VRLog.Info("Net", barMask == 0
                ? $"Use bars RECEIVED from player {PlayerId}: none — their remote board shows no bar " +
                  "drawer (no bar docked, every bar hidden on their own board for another " +
                  "character's focus, or a sender predating record 25)."
                : $"Use bars RECEIVED from player {PlayerId}: mask 0x{barMask:X2} " +
                  $"[{DescribeUseBars(barMask, barFlags, barCounts, barSlots)}] — mirrored as inert, " +
                  "state-painted tiles below their remote decision row. The wire carried structure " +
                  "and state only; WHICH item/bonus/ability a slot is never travels (those slots " +
                  "have no label at all, only card art).");
        }

        // CAP LABELS (extension record 13): absent ⇒ null ⇒ the neutral-label fallback. The
        // sender writes them while the respective control is shown, so a label can never outlive
        // the cap it letters.
        string? confirmLabel = p.HasConfirmCapLabel ? p.ConfirmCapLabel : null;
        string? skipLabel = p.HasSkipCapLabel ? p.SkipCapLabel : null;
        string? undoLabel = p.HasUndoCapLabel ? p.UndoCapLabel : null;
        string? useLabel = p.HasItemUseCapLabel ? p.ItemUseCapLabel : null;
        if (confirmLabel != ConfirmCapLabel || skipLabel != SkipCapLabel
            || undoLabel != UndoCapLabel || useLabel != ItemUseCapLabel)
        {
            ConfirmCapLabel = confirmLabel;
            SkipCapLabel = skipLabel;
            UndoCapLabel = undoLabel;
            ItemUseCapLabel = useLabel;
            // Single quotes: a nested escaped quote inside an interpolation hole trips the
            // patch-inventory source scanner.
            string Cap(string? v) => string.IsNullOrEmpty(v) ? "<neutral fallback>" : "'" + v + "'";
            VRLog.Info("Net", $"Cap labels RECEIVED from player {PlayerId}: " +
                              $"confirm={Cap(confirmLabel)}, skip={Cap(skipLabel)}, " +
                              $"undo={Cap(undoLabel)}, itemUse={Cap(useLabel)} — " +
                              "their mirrored caps read EXACTLY what the owner's do (record 13, " +
                              "sender language verbatim).");
        }

        HasBoardUi = p.HasBoardUi;
        BoardButtonsMask = p.HasBoardUi ? p.BoardButtonsMask : (byte)0;
        WantedGlowMask = p.HasBoardUi ? p.BoardOverlayMask & NetProtocol.BoardUiWantedMask : 0;
        // SNAP-GLOW HOVER (board-UI record byte 1 bits 6..7): WHICH recess the owner's own gold
        // "lands here on release" rim is lit on. Absent record ⇒ -1, and RemoteBoardFurniture then
        // keeps its legacy occupancy-edge flash for that peer.
        int snapSlot = p.HasBoardUi
            ? NetProtocol.DecodeSnapSlot(
                (p.BoardOverlayMask & NetProtocol.BoardUiSnapMask) >> NetProtocol.BoardUiSnapShift)
            : -1;
        if (snapSlot != SnapGlowSlot)
        {
            SnapGlowSlot = snapSlot;
            VRLog.Info("Net", $"Snap-glow hover RECEIVED from player {PlayerId}: " +
                              (snapSlot >= 0
                                  ? $"recess {snapSlot + 1} — their mirrored board lights the gold " +
                                    "rim there NOW, i.e. while they are still holding the card, " +
                                    "exactly as their own board does"
                                  : "none — their mirrored board clears the gold rim") +
                              " (board-UI record byte 1 bits 6..7; a recess POSITION, no identity).");
        }
        // CAP STATES (board-UI record byte 2 — length-gated, so an absent byte is a pre-record
        // sender and every mirrored cap keeps its built colour).
        bool capStates = p.HasBoardUi && p.HasBoardCapStates;
        byte capStateMask = capStates ? p.BoardCapStateMask : (byte)0;
        if (capStates != HasCapStates || capStateMask != CapStateMask)
        {
            HasCapStates = capStates;
            CapStateMask = capStateMask;
            VRLog.Info("Net", $"Cap states RECEIVED from player {PlayerId}: " +
                              (capStates
                                  ? $"0x{capStateMask:X2} — their mirrored CONFIRM cap, rest discs " +
                                    "and skip cap now wear the owner's own disabled / idle / accent " +
                                    "/ confirmed colours instead of the single colour they were built in"
                                  : "none (record 4 carried no cap-state byte — pre-record sender; " +
                                    "the mirrored caps keep their built colour, as before)") + ".");
        }
        // THE ITEM-PILE CUE rides that same byte (bit 7) but is NOT a cap, so it gets its own field
        // and its own change-gated log line: it is the one bit in the record whose consumer is the
        // pile stack rather than the furniture, and a hardware log has to be able to say which of
        // the two disagreed with the owner.
        bool itemCue = capStates && (capStateMask & NetProtocol.BoardUiCapItemPileUsableBit) != 0;
        if (itemCue != ItemsPileUsableCue)
        {
            ItemsPileUsableCue = itemCue;
            VRLog.Info("Net", $"Item-pile usable cue RECEIVED from player {PlayerId}: " +
                              (itemCue
                                  ? "ON — their mirrored items stack now puffs embers and throws " +
                                    "rings on the shared item heartbeat, exactly as their own does"
                                  : "off — the mirrored stack's rings collapse out and the embers " +
                                    "stop being emitted (the ones in flight finish their fade)") +
                              " (board-UI byte 2 bit 7). A boolean about THIS player's own board: " +
                              "no item identity rides it, and the animation runs on this client's " +
                              "clock from these edges alone.");
        }
        // CAP PRESS (record 14 byte 0 bits 3..7). A LATCH: it rides several packets per press, so
        // the renderer animates on the value CHANGING, never on it being set. -1 = none in flight.
        int pressKey = p.HasCapPress ? p.CapPressCap | (p.CapPressSeq << 8) : -1;
        if (pressKey >= 0 && pressKey != CapPressKey)
        {
            VRLog.Info("Net", $"Cap press RECEIVED from player {PlayerId}: wire cap " +
                              $"{p.CapPressCap} (sequence {p.CapPressSeq}) — the matching cap on " +
                              "their mirrored board sinks and springs back, the owner's own dip " +
                              "replayed. Still a picture: nothing on that board is pressable.");
        }
        if (pressKey >= 0)
            CapPressKey = pressKey;
        // CARD-SLOT OCCUPANCY (board-UI record byte 1 bits 3..4, validity bit 5). Absent record or
        // absent validity bit ⇒ "unknown", which is NOT "empty": the board then keeps rendering its
        // slots from the replicated model alone, the way every build before this one did.
        bool slotsKnown = p.HasBoardUi && (p.BoardOverlayMask & NetProtocol.BoardUiSlotsValidBit) != 0;
        int slotMask = slotsKnown
            ? (p.BoardOverlayMask & NetProtocol.BoardUiSlotMask) >> NetProtocol.BoardUiSlotShift
            : 0;
        if (slotsKnown != SlotOccupancyKnown || slotMask != BoardSlotMask || !_loggedSlots)
        {
            _loggedSlots = true;
            SlotOccupancyKnown = slotsKnown;
            BoardSlotMask = slotMask;
            VRLog.Info("Net", $"Board slot occupancy RECEIVED from player {PlayerId}: " +
                              (slotsKnown
                                  ? $"slot1={((slotMask & 1) != 0 ? "card" : "empty")}, " +
                                    $"slot2={((slotMask & 2) != 0 ? "card" : "empty")} " +
                                    "(board-UI record byte 1 bits 3..4) — their remote board now " +
                                    "shows a card BACK exactly where they have one and an empty " +
                                    "recess where they do not"
                                  : "unknown (no validity bit — sender predates the field); the " +
                                    "slots keep rendering from the replicated model alone") +
                              ". A POSITION only: no card identity rides this wire, and fronts " +
                              "stay behind RevealGate.");
        }
        // FOLLOW/PIN: absent record (or a sender that predates the bit) reads as FOLLOW — the
        // un-accented default look, which is exactly what those builds were already drawn in.
        bool pinned = p.HasBoardUi && (p.BoardOverlayMask & NetProtocol.BoardUiPinnedBit) != 0;
        if (pinned != TrayPinned || !_loggedPinned)
        {
            _loggedPinned = true;
            TrayPinned = pinned;
            VRLog.Info("Net", $"Tray anchor mode RECEIVED from player {PlayerId}: " +
                              $"{(pinned ? "PINNED (world-anchored)" : "FOLLOW (rig-anchored)")} " +
                              (p.HasBoardUi
                                  ? "(board-UI record byte 1 bit 2)"
                                  : "(no board-UI record — pre-record peer, default FOLLOW look)") +
                              " — their FOLLOW/PIN keycap now reads the same on this client.");
        }

        // CARD HIGHLIGHT (extension record 6): which card the sender is lifting in each fan.
        // Absent ⇒ -1/-1, i.e. flat fans — never a stale lift from a fan that has since closed.
        HandHighlightIndex = p.HasCardHighlight && p.HandHighlightIndex != NetProtocol.CardHighlightNone
            ? p.HandHighlightIndex : -1;
        FanHighlightIndex = p.HasCardHighlight && p.FanHighlightIndex != NetProtocol.CardHighlightNone
            ? p.FanHighlightIndex : -1;

        // PILE COUNTS (extension record 15): the numbers the owner's own stack labels display.
        // Present ⇒ authoritative (the receiver's model read is proven to lag the owner's board
        // by up to a whole choreographer turn); absent ⇒ pre-record sender OR their stacks are
        // hidden — the board keeps the legacy model-read counts either way, so an old peer's
        // board renders exactly as before.
        bool hadCounts = HasPileCounts;
        int prevD = PileDiscardCount, prevB = PileBurntCount, prevI = PileItemsCount;
        HasPileCounts = p.HasPileCounts;
        PileDiscardCount = p.HasPileCounts ? p.PileDiscardCount : 0;
        PileBurntCount = p.HasPileCounts ? p.PileBurntCount : 0;
        PileItemsCount = p.HasPileCounts ? p.PileItemsCount : 0;
        if (HasPileCounts != hadCounts || PileDiscardCount != prevD || PileBurntCount != prevB
            || PileItemsCount != prevI)
        {
            VRLog.Info("Net", HasPileCounts
                ? $"Pile counts RECEIVED from player {PlayerId}: discard={PileDiscardCount}, " +
                  $"burnt={PileBurntCount}, items={PileItemsCount} (extension record 15 — the " +
                  "numbers their own stack labels show) — their remote board repaints these on " +
                  "the next content tick instead of waiting for the replicated model."
                : $"Pile counts RECEIVED from player {PlayerId}: none (stacks hidden or " +
                  "pre-record sender) — falling back to the model-read counts.");
        }

        // HALF HOVER + SELECTION (extension record 14): the action half the sender is hovering
        // on their own docked round cards (byte 0, transient) and the halves they have CLICKED
        // (byte 1, persistent until their undo). Absent ⇒ -1 / none ⇒ no glow — never a stale
        // glow from a hover that ended or a selection that was undone.
        HalfHoverSlot = p.HasHalfHover && p.HalfHoverActive ? p.HalfHoverSlot : -1;
        HalfHoverTop = p.HasHalfHover && p.HalfHoverActive && p.HalfHoverTop;
        HalfSelect0 = p.HasHalfHover ? p.HalfSelect0 : NetProtocol.HalfSelectNone;
        HalfSelect1 = p.HasHalfHover ? p.HalfSelect1 : NetProtocol.HalfSelectNone;

        // TRACK HOVER (extension record 16): the initiative-track entry the sender is hovering,
        // by stable actor id. Absent ⇒ 0 ⇒ un-hovered track — never a stale lift.
        TrackHoverActorId = p.HasTrackHover ? p.TrackHoverActorId : 0;
        TrackHoverPopup = p.HasTrackHover && p.TrackHoverPopup;

        // TRACK SELECTION (extension record 23): the entries the sender's OWN track is framing.
        // Absent ⇒ count 0 ⇒ NO frame on their mirrored track — never a stale one, and never the
        // OBSERVER's own frame, which is the whole reason the record exists.
        if (p.HasTrackSelection && p.TrackSelectionIds != null && p.TrackSelectionCount > 0)
        {
            TrackSelectionIds = p.TrackSelectionIds;
            TrackSelectionCount = p.TrackSelectionCount < p.TrackSelectionIds.Length
                ? p.TrackSelectionCount
                : p.TrackSelectionIds.Length;
        }
        else
        {
            TrackSelectionCount = 0;
        }

        // TRACK ORDER (extension record 27): the on-screen order of the PLAYER entries on the
        // sender's OWN track, plus which of them they control. Absent ⇒ count 0 ⇒ NO order
        // override and NO mirrored selection-phase ring, which is right on both counts: outside
        // the online card-selection phase every client's track sorts identically (so the mirrored
        // arrangement is already the owner's), and outside it the cue does not exist. Never a
        // stale permutation — the absence IS the release.
        if (p.HasTrackOrder && p.TrackOrderIds != null && p.TrackOrderCount > 0)
        {
            TrackOrderIds = p.TrackOrderIds;
            TrackOrderCount = p.TrackOrderCount < p.TrackOrderIds.Length
                ? p.TrackOrderCount
                : p.TrackOrderIds.Length;
            TrackOrderOwnedMask = p.TrackOrderOwnedMask;
        }
        else
        {
            TrackOrderCount = 0;
            TrackOrderOwnedMask = 0;
        }

        // WALL FADES (extension record 17): the sender's currently-faded wall set by
        // cross-machine stable key, forwarded to the wall-fade driver — which composes it as
        // a remote fade source gated by the RECEIVER's [WallFade] SyncPeerFades. Absent ⇒
        // the peer has no faded walls (or predates the record) ⇒ their set empties NOW;
        // packet GAPS produce no SetExtras call at all and are bridged by the driver's ~1s
        // linger, so loss can never flicker a wall back.
        Core.WallSegmentFade.SetPeerFadedWalls(PlayerId,
            p.HasWallFades ? p.WallFadesKeys : null,
            p.HasWallFades ? p.WallFadesCount : 0);

        // Fan anchor (extension record 5): where the sender's open board-anchored fan really
        // sits, board-local. Reset when absent — "absent" must mean the authored default spot,
        // never a stale anchor from a fan that has since closed or moved.
        HasFanAnchor = p.HasFanAnchor;
        FanAnchorLocal = p.HasFanAnchor ? p.FanAnchorLocal : Vector3.zero;

        // Pile browse (additive field, same reasoning as the item fan): an absent flag means the
        // sender closed the fan OR predates the field — both mean "no fan", and the receiver's
        // RemoteBrowserFan turns the open→closed transition into the collapse-into-the-stack
        // animation. Because a DROPPED packet produces no SetExtras call at all, a lost packet can
        // never be mistaken for a close; only a packet that really arrived without the flag can.
        PileBrowseOpen = p.HasPileBrowse && p.PileBrowseCardCount > 0;
        PileBrowseKind = PileBrowseOpen ? p.PileBrowseKind : (byte)0;
        PileBrowseCardCount = PileBrowseOpen ? p.PileBrowseCardCount : 0;
        PileBrowseHeld = PileBrowseOpen && p.PileBrowseHeld;
        PileBrowseLeftHand = PileBrowseHeld && p.PileBrowseLeftHand;

        // Card FX (additive field): play ONLY on a sequence change (the same event is deliberately
        // re-sent for redundancy), and never on the first packet we ever see from this peer.
        if (p.HasCardFx)
        {
            if (!_fxSeqInit)
            {
                _fxSeqInit = true;
                _lastFxSeq = p.FxSeq; // adopt without playing — this event predates our joining
            }
            else if (p.FxSeq != _lastFxSeq)
            {
                _lastFxSeq = p.FxSeq;
                _cardFx.Play(p.FxEndpoints);
            }
        }
    }

    /// <summary>Per-frame interpolation toward the latest target. Call from the driver's Update.</summary>
    public void Tick(float deltaTime)
    {
        TimeSinceUpdate += deltaTime;
        // Defensive: if our root was destroyed out from under us (should not happen — it is
        // DontDestroyOnLoad + HideAndDontSave and owned solely by us) skip rather than throw.
        if (_root == null)
            return;

        float dt = Mathf.Max(deltaTime, 0f);

        // Live re-apply of the SENDER's head-mask size. It rides the extras stream (5 Hz + on
        // change), which lands in SetExtras and not in the head build path, so the write happens
        // here on change — the same shape as the style-scale check below. Cheap: one float compare
        // per frame, one transform write per actual size change.
        if (!Mathf.Approximately(MaskSize, _appliedMaskSize))
        {
            _appliedMaskSize = MaskSize;
            HeadMaskLibrary.ApplySize(_headVisual, MaskSize);
        }

        // THE SENDER'S hand scale, not ours. This used to read the RECEIVER's
        // [Hands] {Style}Scale, so a peer who had never touched it drew your hands at their own
        // size — the last per-player choice that did not travel. It arrives in the extras
        // extension tail; a sender who is on the default 1.00x sends no record and HandScale stays
        // 1, which is exactly what the old default rendered.
        if (_leftRig != null)
        {
            float styleScale = HandScale;
            if (!Mathf.Approximately(styleScale, _appliedStyleScale))
            {
                _appliedStyleScale = styleScale;
                if (_leftRig.Root != null)
                    HandVisuals.ApplyStyleScale(_leftRig.Root, _leftRig, styleScale);
                if (_rightRig != null && _rightRig.Root != null)
                    HandVisuals.ApplyStyleScale(_rightRig.Root, _rightRig, styleScale);
            }
        }

        float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * dt);
        if (_hasTarget)
        {
            UpdatePart(_headHolder, _target.HeadValid, in _target.Head, k);
            UpdateHand(_leftHolder, _leftCurler, in _target.Left, _target.HasFingers, k, dt);
            UpdateHand(_rightHolder, _rightCurler, in _target.Right, _target.HasFingers, k, dt);
            UpdateHeldCard(k);
        }

        // SECOND held-card slab (extras extension record 10): driven off the EXTRAS stream, so it
        // ticks OUTSIDE the rig-target guard — the record can legitimately arrive before the first
        // rig packet, and the slab must not wait for one. Same machinery as the first slab; the
        // head billboard inside simply keeps the transmitted rotation until a synced head exists.
        UpdateCardSlab(ref _secondCardHolder, ref _secondCardMesh, "HeldCard2",
                       _hasSecondHeldCard, in _secondHeldCardPose, k);

        // Ghost hands: fade exactly the hands the sender says are faded. The extension mask is
        // authoritative when present (a held card can ghost EITHER hand, or both); a sender too
        // old to write it falls back to the legacy inference "ghost = the non-dominant hand",
        // which is what the single flag always meant. The rig objects are ours (built by
        // BuildHands), so the fade runs on private material copies of THIS avatar only — no other
        // player's hands and no bundle asset is ever touched. Released automatically when the
        // flags clear or the avatar is destroyed.
        bool ghostL, ghostR;
        if (HasGhostSides)
        {
            ghostL = (GhostSidesMask & NetProtocol.GhostSideLeftBit) != 0;
            ghostR = (GhostSidesMask & NetProtocol.GhostSideRightBit) != 0;
        }
        else
        {
            ghostL = GhostHand && DominantRight;
            ghostR = GhostHand && !DominantRight;
        }
        _ghostLeft.Apply(ghostL ? _leftRig : null, GhostAlpha);
        _ghostRight.Apply(ghostR ? _rightRig : null, GhostAlpha);

        // Cosmetic add-ons (own their own guards; stubs today).
        //
        // PER-PEER ATTRIBUTION (2026-08-09 multiplayer perf pass). The hardware log could say the
        // whole of Net.Avatar cost 85-96 ms/s with ONE peer, and nothing about which of these seven
        // calls that was — every diagnosis below the driver was therefore a guess. The scopes are
        // free when the monitor is off (one static bool test each) and cost two Stopwatch reads
        // when it is on, so they can stay in permanently. Grep '[Perf] STEPS' for Net.Board vs
        // Net.Fans: those two answer "is it the mirrored control board or the mirrored hands?"
        // without another hardware round-trip.
        // THE CALL ORDER BELOW IS UNCHANGED — the scopes wrap, they do not regroup. (The hand fan
        // runs before the board because the board reads the fan's resolved geometry; keeping the
        // sequence byte-identical is what makes this a pure measurement change.)
        using (Core.PerfMonitor.Scope("Net.Fans"))
            _handFan.Tick(dt);
        using (Core.PerfMonitor.Scope("Net.Board"))
            _controlBoard.Tick(dt);
        using (Core.PerfMonitor.Scope("Net.Fans"))
        {
            _itemFan.Tick(dt);
            _emptyFanHint.Tick();
        }
        using (Core.PerfMonitor.Scope("Net.AvatarExtras"))
            _cardFx.Tick(dt);
        using (Core.PerfMonitor.Scope("Net.Fans"))
            _browserFan.Tick(dt);
        using (Core.PerfMonitor.Scope("Net.AvatarExtras"))
            _nameTag.Tick();
        Core.PerfMonitor.Count("Net.AvatarTicks");
    }

    private static void UpdatePart(Transform holder, bool valid, in RigPose pose, float k)
    {
        if (!valid)
        {
            if (holder.gameObject.activeSelf) holder.gameObject.SetActive(false);
            return;
        }
        if (!holder.gameObject.activeSelf)
        {
            // First activation: snap to avoid a lerp streak from the origin.
            holder.SetPositionAndRotation(pose.Position, pose.Rotation);
            holder.gameObject.SetActive(true);
            return;
        }
        holder.SetPositionAndRotation(
            Vector3.Lerp(holder.position, pose.Position, k),
            Quaternion.Slerp(holder.rotation, pose.Rotation, k));
    }

    private static void UpdateHand(Transform holder, FingerCurler? curler, in HandStateSample hand, bool fingers, float k, float dt)
    {
        UpdatePart(holder, hand.Tracked, in hand.Pose, k);
        if (!hand.Tracked || curler == null)
            return;
        if (fingers)
        {
            curler.SetTarget(Finger.Thumb, hand.Curl0);
            curler.SetTarget(Finger.Index, hand.Curl1);
            curler.SetTarget(Finger.Middle, hand.Curl2);
            curler.SetTarget(Finger.Ring, hand.Curl3);
            curler.SetTarget(Finger.Pinky, hand.Curl4);
        }
        curler.Tick(dt);
    }

    /// <summary>
    /// One both-faces-back card slab at the sender's held-card pose (additive
    /// <see cref="NetProtocol.FlagHeldCard"/> field — a single card physically held in a peer's
    /// hand, e.g. plucked from their fan or a pile viewer). Built lazily on first use, eased
    /// exactly like the other parts, hidden while the sender holds nothing. Shows a BACK only:
    /// no card identity rides the wire.
    ///
    /// THE ONE CARD SURFACE THE 2026-08-08 RULING COULD NOT REACH, and why that is a data fact rather
    /// than a decision. The ruling ("Die Oberseiten der Karten des remote Spielers soll auch überall
    /// sichtbar sein … NUR in der Auswahlphase … nur die Rückseiten") turned every other remote card
    /// surface face-up outside the secret window, and every one of them could be turned face-up for
    /// FREE because the identities were already on this client in the host-replicated model: the hand
    /// fan reads <c>CCharacterClass.HandAbilityCards</c>, the browse arcs read the discard/lost lists,
    /// the item fan reads <c>Inventory.AllItems</c>, the board slots read <c>RoundAbilityCards</c>. A
    /// HELD card has no such list. "Which of my cards is currently pinched between my fingers" is a VR
    /// fact that exists nowhere in the game model — the model still has the card in whatever pile it
    /// came from — so the only way to name it on a receiver would be to put a card id in a packet,
    /// which is the one thing the wire rule forbids outright. The slab therefore stays a back, and the
    /// card becomes readable again the instant its owner puts it down. <see cref="RemoteCardFx"/>'s
    /// in-flight card slab is backs-only for exactly the same reason and would be fixed by exactly the
    /// same (forbidden) field.
    ///
    /// ORIENTATION (multiplayer half of user report 2): a held card is not rigid in the owner's
    /// hand — <see cref="Cards.VRCard"/>.TickHeldPose re-billboards it to the OWNER's head every
    /// frame, so the owner always reads it face-on however their wrist is turned. The POSITION for
    /// that rule rides the wire already; the ROTATION does not need to. This slab used to simply
    /// slerp toward the transmitted rotation, which breaks the rule on the receiver in two ways:
    /// (a) the rotation is a 16-bit-quantized snapshot taken at the SEND rate and then eased with
    /// <see cref="NetProtocol.InterpolationSharpness"/> INDEPENDENTLY of the head and of the card's
    /// own position, so during head/hand motion the slab visibly lags out of the "facing its owner"
    /// relationship instead of holding it; and (b) after packet loss the last rotation keeps
    /// pointing at where the peer's head WAS. Both vanish if the receiver simply re-derives the
    /// billboard each frame from data it already has: the peer's head is a mandatory part of the
    /// SAME rig packet (<see cref="AvatarState.Head"/>, eased onto <see cref="HeadHolder"/> a few
    /// lines earlier in <see cref="Tick"/>), so <c>LookRotation(slabPos − headPos, head.up)</c>
    /// reproduces exactly what the owner sees — NO new wire field, no flag bit, no version bump.
    /// This is the same receiver-side billboard <see cref="RemoteItemFan"/> and
    /// <see cref="RemoteBrowserFan"/> already run for the peer's fans; the held-card slab was the
    /// one card proxy still trusting the transmitted rotation. The transmitted rotation is still
    /// read and still used as the fallback for a peer whose head is not tracked.
    ///
    /// No extra slerp on top: the billboard is derived from an ALREADY-eased slab position and an
    /// already-eased head, so it inherits their smoothing — easing it again would only re-introduce
    /// the lag this removes.
    /// </summary>
    private void UpdateHeldCard(float k)
        => UpdateCardSlab(ref _heldCardHolder, ref _heldCardMesh, "HeldCard",
                          _target.HasHeldCard, in _target.HeldCardPose, k);

    /// <summary>
    /// One card-slab's whole per-frame life: lazy build on first use, ease toward the transmitted
    /// pose, hide while nothing is held, and re-derive the billboard. Parameterized over the
    /// holder/mesh pair so the FIRST held card (rig packet <see cref="NetProtocol.FlagHeldCard"/>)
    /// and the SECOND one (extras extension record
    /// <see cref="NetProtocol.ExtIdSecondHeldCard"/> — the card in the sender's other hand while
    /// both hold one) share every line of this machinery instead of duplicating it. NOTE the
    /// placement contract this shape encodes, which is also why record 10 needs no hand byte: the
    /// slab is rendered at the ABSOLUTE transmitted pose under the avatar root — it is never
    /// parented to a hand holder, and no hand transform is consulted anywhere below.
    ///
    /// ORIENTATION (multiplayer half of user report 2): a held card is not rigid in the owner's
    /// hand — <see cref="Cards.VRCard"/>.TickHeldPose re-billboards it to the OWNER's head every
    /// frame, so the owner always reads it face-on however their wrist is turned. The POSITION for
    /// that rule rides the wire already; the ROTATION does not need to. A slab that simply slerps
    /// toward the transmitted rotation breaks the rule on the receiver twice: the quantized
    /// rotation snapshot eases INDEPENDENTLY of the head and lags out of the "facing its owner"
    /// relationship during motion, and after packet loss it keeps pointing at where the peer's
    /// head WAS. Both vanish by re-deriving the billboard each frame from data already here: the
    /// peer's head is a mandatory part of the rig packet (eased onto <see cref="_headHolder"/> in
    /// <see cref="Tick"/>), so <c>LookRotation(slabPos − headPos, head.up)</c> reproduces exactly
    /// what the owner sees — no new wire field. Same receiver-side billboard
    /// <see cref="RemoteItemFan"/> and <see cref="RemoteBrowserFan"/> already run. The transmitted
    /// rotation is still read and still the fallback for a peer whose head is not tracked, and no
    /// extra slerp rides on top: the billboard derives from an already-eased position and an
    /// already-eased head, so it inherits their smoothing.
    /// </summary>
    private void UpdateCardSlab(ref Transform? holder, ref Mesh? mesh, string name,
                                bool held, in RigPose pose, float k)
    {
        if (holder == null)
        {
            if (!held)
                return; // never held anything yet — build nothing
            holder = BuildCardSlab(name, out mesh);
        }
        UpdatePart(holder, held, in pose, k);
        if (!held || !holder.gameObject.activeSelf)
            return;
        if (!_hasTarget || !_target.HeadValid || !_headHolder.gameObject.activeSelf)
            return; // no synced head this frame — keep the transmitted rotation

        // Card +Z points AWAY from its reader (CardMesh / BuildBackSlab convention), so the look
        // direction is head → card: the owner sees the face, everyone else sees the back.
        Vector3 away = holder.position - _headHolder.position;
        if (away.sqrMagnitude < 1e-6f)
            return;
        away.Normalize();
        Vector3 up = _headHolder.up; // the OWNER's head-up: their head roll is on the wire too
        if (Mathf.Abs(Vector3.Dot(away, up)) > 0.9995f)
            return; // forward ∥ up — LookRotation undefined; keep the previous rotation
        holder.rotation = Quaternion.LookRotation(away, up);

        if (!_heldCardBillboardLogged)
        {
            _heldCardBillboardLogged = true;
            VRLog.Info("Net", $"Remote held card: billboarding player {PlayerId}'s slab to their SYNCED head "
                + $"(rig-packet head, no new wire field); wire rotation kept only as the untracked-head fallback. "
                + $"delta vs wire rot {Quaternion.Angle(pose.Rotation, holder.rotation):F1} deg.");
        }
    }

    private Transform BuildCardSlab(string name, out Mesh? mesh)
    {
        var holder = new GameObject(name).transform;
        holder.SetParent(_root.transform, worldPositionStays: false);
        holder.localScale = Vector3.one * AppliedScale;
        holder.gameObject.SetActive(false);

        // Own mesh (freed in Destroy); SHARED back material (CardMesh caches it — never ours
        // to destroy). Sized to the same defaults the remote fan slabs use.
        mesh = RemoteHandFan.BuildBackSlab(
            RemoteHandFan.DefaultCardWidth, RemoteHandFan.DefaultCardHeight);
        var mf = holder.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = holder.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Cards.CardMesh.CreateBackMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        VRLayers.Apply(holder.gameObject);
        return holder;
    }

    public void Destroy()
    {
        // Cloned ghost materials are ASSETS — free them before the hand objects go away.
        _ghostLeft.Release();
        _ghostRight.Release();
        _handFan.Destroy();
        _controlBoard.Destroy();
        _itemFan.Destroy();
        _emptyFanHint.Destroy();
        _cardFx.Destroy();
        _browserFan.Destroy();
        _nameTag.Destroy();
        if (_heldCardMesh != null)
            Object.Destroy(_heldCardMesh); // asset — not freed with the GameObject tree
        _heldCardMesh = null;
        _heldCardHolder = null;
        if (_secondCardMesh != null)
            Object.Destroy(_secondCardMesh); // same asset rule as the first slab's mesh
        _secondCardMesh = null;
        _secondCardHolder = null;
        if (_root != null)
            Object.Destroy(_root);
        VRLog.Info("Net", $"Remote avatar destroyed for player {PlayerId}.");
    }

    // ---- hands -------------------------------------------------------------------------

    /// <summary>(Re)build both hand visuals for the given wire hand-style: clears the hand
    /// holders' children, rebuilds the rigs + curlers with the sender's chosen prefab pair
    /// (Glove fallback inside <see cref="HandVisuals"/>), and re-applies the mod layer.
    /// Cheap and only on change — mirrors <see cref="BuildHeadMask"/>.
    ///
    /// Each hand is built under a "HandVisual" CHILD of the holder so the per-style visual
    /// scale ([Hands] GloveScale/PlateScale/ArcaneScale ≈0.62 for the styled pairs, applied
    /// by <see cref="HandVisuals.ApplyStyleScale"/> onto the transform passed to Build) lands
    /// on that child. The HOLDER carries only the sender scale from <see cref="SetTarget"/> —
    /// previously the sender-scale write stomped the style scale on the holder and a remote
    /// Plate/Arcane hand rendered ~1.6× too big. Attach-ons that size off the holder
    /// (<see cref="RemoteHandFan"/>) are deliberately unaffected by the style scale.</summary>
    private void BuildHands(int handStyle)
    {
        var style = Hands.HandStyles.Clamp(handStyle);
        _appliedHandStyle = handStyle;

        // Restore + free any ghost material clones BEFORE the old hand objects are destroyed
        // (Unity does not free materials with the GameObject that referenced them). Tick
        // re-applies the ghost to the freshly built rig on the very next frame. Null-conditional
        // as a hard floor: the ctor calls this, and a field that has not been assigned yet must
        // degrade to "nothing to release", never to an NRE that kills the driver's Update.
        _ghostLeft?.Release();
        _ghostRight?.Release();

        // Destroy ONLY the rig visuals this method itself created (the "HandVisual" child each
        // build parents the HandVisuals rig under) — NEVER every child of the holder. The holders
        // are also the ATTACHMENT POINTS for cosmetic add-ons that live across hand rebuilds:
        // RemoteHandFan parents its whole card-fan subtree under the non-dominant holder. The old
        // full-children sweep destroyed that fan root and its slabs while the fan's own _cards
        // bookkeeping survived — and because its rebuild was keyed on the card COUNT alone, every
        // subsequent LayoutCards deref'd destroyed slabs (the hundreds of
        // "RemoteAvatar.Tick … RemoteHandFan.LayoutCards" NREs of the 2026-08 MP hardware log).
        // The fan additionally self-heals against ANY external destruction now (see
        // RemoteHandFan.EnsureBuiltAlive), but the mutation-site rule stands: a hand REBUILD must
        // only replace the hand.
        DestroyHandVisuals(_leftHolder);
        DestroyHandVisuals(_rightHolder);

        Transform leftVisual = new GameObject("HandVisual").transform;
        leftVisual.SetParent(_leftHolder, worldPositionStays: false);
        Transform rightVisual = new GameObject("HandVisual").transform;
        rightVisual.SetParent(_rightHolder, worldPositionStays: false);

        _leftRig = HandVisuals.Build(leftVisual, HandSide.Left, style);
        _leftCurler = _leftRig != null ? new FingerCurler(_leftRig, HandSide.Left) : null;
        _rightRig = HandVisuals.Build(rightVisual, HandSide.Right, style);
        _rightCurler = _rightRig != null ? new FingerCurler(_rightRig, HandSide.Right) : null;

        // Build applied OUR config's scale for the sender's style; overwrite it with THEIRS
        // straight away. Leaving it to the live check in Tick would show one frame at the wrong
        // size every time a peer's hands are rebuilt.
        _appliedStyleScale = HandScale;
        if (_leftRig != null && _leftRig.Root != null)
            HandVisuals.ApplyStyleScale(_leftRig.Root, _leftRig, HandScale);
        if (_rightRig != null && _rightRig.Root != null)
            HandVisuals.ApplyStyleScale(_rightRig.Root, _rightRig, HandScale);

        // Keep the whole subtree on the mod layer so the owned head camera renders it.
        VRLayers.Apply(_root);
    }

    /// <summary>Destroy exactly the "HandVisual" rig children of <paramref name="holder"/> —
    /// the objects <see cref="BuildHands"/> creates — leaving every other attachment (the
    /// remote hand fan's root, any future holder-anchored add-on) alive. See the call-site
    /// note for the NRE family the old destroy-all-children sweep caused.</summary>
    private static void DestroyHandVisuals(Transform holder)
    {
        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            Transform child = holder.GetChild(i);
            if (child != null && child.name == "HandVisual")
                Object.Destroy(child.gameObject);
        }
    }

    // ---- head mask ----------------------------------------------------------------------

    /// <summary>(Re)build the head mask for the given mask id: clears any existing head visual,
    /// instantiates the chosen <see cref="HeadMaskLibrary"/> prefab, or stands in the placeholder
    /// head when that mask has not shipped yet. Re-applies the mod layer so the head camera renders
    /// the new mesh.</summary>
    private void BuildHeadMask(int maskId)
    {
        maskId = Mathf.Clamp(maskId, 0, HeadMaskLibrary.MaskCount - 1);
        _appliedMaskId = maskId;

        // Clear any existing head visual (children of the holder).
        for (int i = _headHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_headHolder.GetChild(i).gameObject);

        // The fresh visual is born at the size the sender last told us about, so a mask SWITCH
        // never flashes at 1× for a frame before Tick corrects it.
        _headVisual = HeadMaskLibrary.BuildHead(_headHolder, maskId, _tint, MaskSize);
        _appliedMaskSize = MaskSize;

        // Keep the whole subtree on the mod layer so the owned head camera renders it.
        VRLayers.Apply(_root);
    }

    /// <summary>Human-readable use-bar drawer for the received-log line (diagnostic only). Names
    /// each bar by its BAR INDEX — the wire carries the bit, never a name.</summary>
    private static string DescribeUseBars(byte mask, byte[]? flags, byte[]? counts, byte[]? states)
    {
        var sb = new System.Text.StringBuilder(96);
        for (int b = 0; b < NetProtocol.UseBarsCount; b++)
        {
            if ((mask & (1 << b)) == 0)
                continue;
            if (sb.Length > 0)
                sb.Append("; ");
            int n = counts != null && b < counts.Length ? counts[b] : 0;
            sb.Append(b switch { 0 => "activeBonus", 1 => "abilities", 2 => "augments", _ => "items" })
              .Append('=').Append(n).Append(" slot(s)");
            byte f = flags != null && b < flags.Length ? flags[b] : (byte)0;
            if ((f & NetProtocol.UseBarElementPickerBit) != 0)
                sb.Append(" +element picker OPEN");
            if ((f & NetProtocol.UseBarOptionPickerBit) != 0)
                sb.Append(" +option picker OPEN");
            int at = b * NetProtocol.UseBarsMaxSlots;
            for (int s = 0; s < n && states != null && at + s < states.Length; s++)
            {
                byte st = states[at + s];
                sb.Append(" #").Append(s).Append('=')
                  .Append((st & NetProtocol.UseSlotOfferedBit) != 0 ? "OFFERED" : "greyed");
                if ((st & NetProtocol.UseSlotDimmedBit) != 0)
                    sb.Append("+dim");
                if ((st & NetProtocol.UseSlotChosenBit) != 0)
                    sb.Append("+CHOSEN");
            }
        }
        return sb.ToString();
    }

    /// <summary>Value equality for the per-option state arrays (wire record 23) — the change gate
    /// for the decision-state log, so a toggle flip logs once and a steady prompt logs never.
    /// Reused verbatim by the use-bar drawer's three arrays (record 25): the question is the same
    /// one, "did these bytes move".</summary>
    private static bool SameOptionStates(byte[]? a, byte[]? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    /// <summary>Human-readable per-option states for the received-log line (diagnostic only).</summary>
    private static string DescribeOptionStates(byte[]? states)
    {
        if (states == null || states.Length == 0)
            return "-";
        var sb = new System.Text.StringBuilder(48);
        for (int i = 0; i < states.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            byte f = states[i];
            sb.Append('#').Append(i).Append('=')
              .Append((f & NetProtocol.DecisionOptionOfferedBit) != 0 ? "OFFERED" : "greyed");
            if ((f & NetProtocol.DecisionOptionDimmedBit) != 0)
                sb.Append("+dim");
            if ((f & NetProtocol.DecisionOptionChosenBit) != 0)
                sb.Append("+CHOSEN");
        }
        return sb.ToString();
    }

    /// <summary>Stable per-player tint so avatars are distinguishable at a glance.</summary>
    private static Color TintFor(int playerId)
    {
        float hue = (playerId * 0.61803398875f) % 1f; // golden-ratio hashing → spread hues
        return Color.HSVToRGB(hue, 0.45f, 1f);
    }
}
