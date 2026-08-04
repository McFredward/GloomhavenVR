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
    private bool _loggedPinned;          // ditto for the received FOLLOW/PIN state (first packet + changes)
    private bool _loggedSlots;           // ditto for the received card-slot occupancy nibble

    // Held-card slab (additive FlagHeldCard wire field): one both-faces-back card slab eased
    // toward the sender's held-card pose — a card in a peer's HAND, distinct from their fan.
    // Backs only, mirroring the fan's anti-cheat stance (no card identity is ever on the wire).
    private Transform? _heldCardHolder;
    private Mesh? _heldCardMesh;
    private bool _heldCardBillboardLogged; // one-line confirm the receiver-side billboard fired

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

    /// <summary>The owner's PICK-STATUS line (extension record 7), or null while their placard is
    /// down — including for a sender that predates the record, which renders identically.</summary>
    public string? PickBannerText { get; private set; }

    /// <summary>The BOARD TOOLTIP the owner is reading (extension record 9, identity-gated on
    /// their side), or null while none is shown — including for a sender that predates the
    /// record, which renders identically. Shown at the remote board's tooltip area
    /// (<see cref="RemoteBoardTooltip"/>).</summary>
    public string? TooltipText { get; private set; }

    /// <summary>Visible-controls bitmask (<see cref="NetProtocol.BoardUiConfirmBit"/> …),
    /// meaningful only when <see cref="HasBoardUi"/>.</summary>
    public byte BoardButtonsMask { get; private set; }

    /// <summary>The sender's wanted-slot glow mask (bit0 = left slot, bit1 = right), meaningful
    /// only when <see cref="HasBoardUi"/>. The blink itself is animated locally at the shared
    /// period — synced state, local clock.</summary>
    public int WantedGlowMask { get; private set; }

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
        _handFan = new RemoteHandFan(this);
        _controlBoard = new RemoteControlBoard(this);
        _itemFan = new RemoteItemFan(this);
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

        HasBoardUi = p.HasBoardUi;
        BoardButtonsMask = p.HasBoardUi ? p.BoardButtonsMask : (byte)0;
        WantedGlowMask = p.HasBoardUi ? p.BoardOverlayMask & NetProtocol.BoardUiWantedMask : 0;
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

        if (_hasTarget)
        {
            float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * dt);
            UpdatePart(_headHolder, _target.HeadValid, in _target.Head, k);
            UpdateHand(_leftHolder, _leftCurler, in _target.Left, _target.HasFingers, k, dt);
            UpdateHand(_rightHolder, _rightCurler, in _target.Right, _target.HasFingers, k, dt);
            UpdateHeldCard(k);
        }

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
        _handFan.Tick(dt);
        _controlBoard.Tick(dt);
        _itemFan.Tick(dt);
        _cardFx.Tick(dt);
        _browserFan.Tick(dt);
        _nameTag.Tick();
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
    /// no card identity rides the wire (same anti-cheat stance as <see cref="RemoteHandFan"/>).
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
    {
        if (_heldCardHolder == null)
        {
            if (!_target.HasHeldCard)
                return; // never held anything yet — build nothing
            BuildHeldCardSlab();
            if (_heldCardHolder == null)
                return;
        }
        UpdatePart(_heldCardHolder, _target.HasHeldCard, in _target.HeldCardPose, k);
        if (!_target.HasHeldCard || !_heldCardHolder.gameObject.activeSelf)
            return;
        if (!_target.HeadValid || !_headHolder.gameObject.activeSelf)
            return; // no synced head this frame — keep the transmitted rotation

        // Card +Z points AWAY from its reader (CardMesh / BuildBackSlab convention), so the look
        // direction is head → card: the owner sees the face, everyone else sees the back.
        Vector3 away = _heldCardHolder.position - _headHolder.position;
        if (away.sqrMagnitude < 1e-6f)
            return;
        away.Normalize();
        Vector3 up = _headHolder.up; // the OWNER's head-up: their head roll is on the wire too
        if (Mathf.Abs(Vector3.Dot(away, up)) > 0.9995f)
            return; // forward ∥ up — LookRotation undefined; keep the previous rotation
        _heldCardHolder.rotation = Quaternion.LookRotation(away, up);

        if (!_heldCardBillboardLogged)
        {
            _heldCardBillboardLogged = true;
            VRLog.Info("Net", $"Remote held card: billboarding player {PlayerId}'s slab to their SYNCED head "
                + $"(rig-packet head, no new wire field); wire rotation kept only as the untracked-head fallback. "
                + $"delta vs wire rot {Quaternion.Angle(_target.HeldCardPose.Rotation, _heldCardHolder.rotation):F1} deg.");
        }
    }

    private void BuildHeldCardSlab()
    {
        _heldCardHolder = new GameObject("HeldCard").transform;
        _heldCardHolder.SetParent(_root.transform, worldPositionStays: false);
        _heldCardHolder.localScale = Vector3.one * AppliedScale;
        _heldCardHolder.gameObject.SetActive(false);

        // Own mesh (freed in Destroy); SHARED back material (CardMesh caches it — never ours
        // to destroy). Sized to the same defaults the remote fan slabs use.
        _heldCardMesh = RemoteHandFan.BuildBackSlab(
            RemoteHandFan.DefaultCardWidth, RemoteHandFan.DefaultCardHeight);
        var mf = _heldCardHolder.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = _heldCardMesh;
        var mr = _heldCardHolder.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Cards.CardMesh.CreateBackMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        VRLayers.Apply(_heldCardHolder.gameObject);
    }

    public void Destroy()
    {
        // Cloned ghost materials are ASSETS — free them before the hand objects go away.
        _ghostLeft.Release();
        _ghostRight.Release();
        _handFan.Destroy();
        _controlBoard.Destroy();
        _itemFan.Destroy();
        _cardFx.Destroy();
        _browserFan.Destroy();
        _nameTag.Destroy();
        if (_heldCardMesh != null)
            Object.Destroy(_heldCardMesh); // asset — not freed with the GameObject tree
        _heldCardMesh = null;
        _heldCardHolder = null;
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

        for (int i = _leftHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_leftHolder.GetChild(i).gameObject);
        for (int i = _rightHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_rightHolder.GetChild(i).gameObject);

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

    /// <summary>Stable per-player tint so avatars are distinguishable at a glance.</summary>
    private static Color TintFor(int playerId)
    {
        float hue = (playerId * 0.61803398875f) % 1f; // golden-ratio hashing → spread hues
        return Color.HSVToRGB(hue, 0.45f, 1f);
    }
}
