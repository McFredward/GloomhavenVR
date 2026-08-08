using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The cosmetic "extras" a VR player broadcasts alongside their rig at
/// <see cref="NetProtocol.ExtrasSendRateHz"/>: their control-board world pose (so others can see
/// it where the owner placed it) and how many cards are in their hand fan (rendered as BACKS
/// only — never card identities). Carries NO game state and no card faces; the wire packet is
/// message TYPE <see cref="NetProtocol.MsgExtras"/>.
/// </summary>
internal struct PresenceState
{
    /// <summary>True when <see cref="Board"/>/<see cref="BoardScale"/> carry a valid control-board
    /// world pose (the owner has a live <c>PlayTray</c>). False → no remote board this packet.</summary>
    public bool HasBoard;

    /// <summary>Control-board root world pose (shared world frame, meaningful only when
    /// <see cref="HasBoard"/>).</summary>
    public RigPose Board;

    /// <summary>Uniform world scale of the sender's control board (meaningful only when
    /// <see cref="HasBoard"/>). 1 when unknown.</summary>
    public float BoardScale;

    /// <summary>How many cards are in the sender's hand fan (0..255). Rendered as backs only.</summary>
    public byte HandCardCount;

    /// <summary>True when the sender's dominant hand is the RIGHT hand (mirror of the rig flag).
    /// Defaults true (right-dominant) when unknown.</summary>
    public bool DominantRight;

    /// <summary>
    /// True when the sender has the "ghost hand" active this frame: their [Hands] GhostHandOnFan
    /// toggle is on AND their card fan is open, so the fan-carrying (= non-dominant) hand is
    /// faded locally and must read the same way on our copy of their avatar. Wire flag
    /// <see cref="NetProtocol.FlagExtrasGhostHand"/>; false for peers that predate the field.
    /// </summary>
    public bool GhostHand;

    /// <summary>Quantized ghost transparency STRENGTH (0..255 ⇒ 0..1; higher = more see-through),
    /// meaningful only when <see cref="GhostHand"/> is set. The SENDER's strength is transmitted
    /// on purpose — their ghost hand must look the same to everyone, exactly like their chosen
    /// hand style and head mask.</summary>
    public byte GhostStrength;

    /// <summary>
    /// True when the sender's ITEM fan (<c>Cards.ItemsPile</c>) is open this packet (wire flag
    /// <see cref="NetProtocol.FlagItemFan"/>). When set, <see cref="ItemCardCount"/> carries how
    /// many item cards it holds. Rendered as BACKS only — no item identity rides the wire, exactly
    /// like the ability fan.
    /// </summary>
    public bool HasItemFan;

    /// <summary>How many item cards are in the sender's open item fan (meaningful only when
    /// <see cref="HasItemFan"/>).</summary>
    public byte ItemCardCount;

    /// <summary>True when that item fan is HAND-HELD (above the grabbing palm) rather than
    /// board-anchored above the sender's control board (<see cref="NetProtocol.FlagItemFanHeld"/>).</summary>
    public bool ItemFanHeld;

    /// <summary>True when that hand-held item fan rides the sender's LEFT hand
    /// (<see cref="NetProtocol.FlagItemFanLeft"/>); false = right hand.</summary>
    public bool ItemFanLeftHand;

    /// <summary>
    /// True when a CARD-FX event rides this packet (<see cref="NetProtocol.FlagCardFx"/>): the
    /// sender's VR just launched a card animation and peers should play the same flight locally.
    /// </summary>
    public bool HasCardFx;

    /// <summary>Wrapping event counter. The receiver plays the event only when this CHANGES, so the
    /// same event may be re-sent for redundancy (unreliable transport) without playing twice.</summary>
    public byte FxSeq;

    /// <summary>Packed endpoints: low nibble = FROM <see cref="CardFxAnchor"/>, high nibble = TO.</summary>
    public byte FxEndpoints;

    /// <summary>
    /// True when the sender has a control-board PILE BROWSER open this packet (the "Abgelegt" /
    /// "Verbrannt" reading fan — <c>Cards.PileBrowser</c>), wire flag
    /// <see cref="NetProtocol.FlagPileBrowse"/>. When set, the four fields below describe it.
    /// Rendered as BACKS only, exactly like the hand and item fans — no card identity on the wire.
    /// </summary>
    public bool HasPileBrowse;

    /// <summary>Which pile that browser is reading: <see cref="NetProtocol.PileBrowseKindDiscard"/>,
    /// <see cref="NetProtocol.PileBrowseKindBurnt"/> or <see cref="NetProtocol.PileBrowseKindItems"/>
    /// (meaningful only when <see cref="HasPileBrowse"/>).</summary>
    public byte PileBrowseKind;

    /// <summary>How many cards the open browse fan holds (meaningful only when
    /// <see cref="HasPileBrowse"/>).</summary>
    public byte PileBrowseCardCount;

    /// <summary>True when that browse fan is a HAND-HELD reading fan (the sender pinch-grabbed the
    /// stack) rather than floating above their control board.</summary>
    public bool PileBrowseHeld;

    /// <summary>True when the hand-held browse fan rides the sender's LEFT hand; false = right.</summary>
    public bool PileBrowseLeftHand;

    /// <summary>
    /// True when the sender transmits a non-default HEAD-MASK SIZE this packet (trailing-block
    /// byte A <see cref="NetProtocol.PileBrowseMaskSizeBit"/>); then <see cref="MaskSizeCode"/>
    /// carries it. False means "the default 1.00×" — either the sender wears the default size or
    /// they predate the field; both render identically, which is the whole point of only sending
    /// the byte when it differs.
    /// </summary>
    public bool HasMaskSize;

    /// <summary>Quantized head-mask size multiplier (hundredths — see
    /// <see cref="NetProtocol.EncodeMaskSize"/>), meaningful only when <see cref="HasMaskSize"/>.
    /// The SENDER's chosen size is transmitted on purpose: their mask must read the same to
    /// everyone, exactly like their chosen mask style, hand style and ghost strength.</summary>
    public byte MaskSizeCode;

    /// <summary>
    /// True when this packet carries a non-default per-style HAND SCALE in the extension tail
    /// (<see cref="NetProtocol.ExtIdHandScale"/>). False means "1.00x" — either the sender wears
    /// the default size or predates the field; both render identically, which is why the record is
    /// only written when it differs.
    /// </summary>
    public bool HasHandScale;

    /// <summary>Quantized hand-scale multiplier (hundredths), meaningful only with
    /// <see cref="HasHandScale"/>. The SENDER's value on purpose: their hands must read the same
    /// size to everyone, exactly like their chosen hand style.</summary>
    public byte HandScaleCode;

    /// <summary>True when the packet says WHICH hands are ghosted (extension record
    /// <see cref="NetProtocol.ExtIdGhostSides"/>). Without it receivers fall back to the legacy
    /// inference (ghost = the non-dominant hand).</summary>
    public bool HasGhostSides;

    /// <summary>Bitmask of ghosted hands (<see cref="NetProtocol.GhostSideLeftBit"/> /
    /// <see cref="NetProtocol.GhostSideRightBit"/>); meaningful when <see cref="HasGhostSides"/>.</summary>
    public byte GhostSidesMask;

    /// <summary>
    /// True when this packet carries the sender's MOD VERSION in the extension tail
    /// (<see cref="NetProtocol.ExtIdModVersion"/>). Every peer on a build that HAS the
    /// handshake sends it on every extras packet; false therefore means "this peer's build
    /// predates the version handshake" and the receiver treats it as <c>ModBuild 0</c> —
    /// which is a MISMATCH by definition (see <see cref="NetProtocol.ModBuild"/>).
    /// </summary>
    public bool HasModVersion;

    /// <summary>The sender's <see cref="NetProtocol.ModBuild"/> — the monotonic comparison key
    /// of the version handshake (meaningful only when <see cref="HasModVersion"/>).</summary>
    public ushort ModBuild;

    /// <summary>The sender's human-readable version tag (their <c>MyPluginInfo.PLUGIN_VERSION</c>;
    /// display only, never compared). Null/empty when absent. Capped on both ends at
    /// <see cref="NetProtocol.ModVersionTextMaxBytes"/> UTF8 bytes.</summary>
    public string? ModVersionText;

    /// <summary>
    /// The sender's chosen CONTROL-BOARD STYLE (<c>Cards.ControlBoard</c> id: 0 Oak / 1 Steel /
    /// 2 Bronze), carried in trailing-block byte A bits 5..6 — see
    /// <see cref="NetProtocol.PileBrowseBoardStyleShift"/>. NO extra byte and no presence flag: 0
    /// is the default board, so "absent" (older peer, or a peer on Oak) and "Oak" render the same,
    /// and the packet length is unchanged.
    ///
    /// Transmitted because picking the board is now a normal user-facing choice sitting next to the
    /// head mask and the hand style, and those already travel: a peer's board must read in the
    /// material that peer actually chose (<see cref="RemoteControlBoard"/> tints its frame from
    /// this), not in whatever this client happens to use.
    /// </summary>
    public byte BoardStyleCode;

    /// <summary>
    /// True when this packet carries the sender's live BOARD-UI STATE (extension record
    /// <see cref="NetProtocol.ExtIdBoardUi"/>): which board controls their own PlayTray currently
    /// shows, the wanted-slot glow mask and which of the two card slots physically hold a card.
    /// Sent on EVERY packet that carries a board pose, so
    /// "record present, all bits clear" (owner's board shows no dynamic controls) is
    /// distinguishable from "sender predates the field" (receiver keeps the legacy always-drawn
    /// furniture).
    /// </summary>
    public bool HasBoardUi;

    /// <summary>Visible-controls bitmask (<see cref="NetProtocol.BoardUiConfirmBit"/> …),
    /// meaningful only when <see cref="HasBoardUi"/>.</summary>
    public byte BoardButtonsMask;

    /// <summary>Overlay byte: bits 0..1 are the wanted-slot glow mask
    /// (<see cref="NetProtocol.BoardUiWantedMask"/>), bit 2 is the FOLLOW/PIN state
    /// (<see cref="NetProtocol.BoardUiPinnedBit"/>), bits 3..4 are the live CARD-SLOT OCCUPANCY
    /// (<see cref="NetProtocol.BoardUiSlotMask"/>) and bit 5 says that nibble is state rather than
    /// a pre-field sender's zeroes (<see cref="NetProtocol.BoardUiSlotsValidBit"/>); the rest is
    /// reserved (0). Masked with <see cref="NetProtocol.BoardUiOverlayMask"/> on write AND on read.
    /// Meaningful only when <see cref="HasBoardUi"/>.</summary>
    public byte BoardOverlayMask;

    /// <summary>
    /// True when this packet carries the board-local anchor POSITION of the sender's open
    /// BOARD-ANCHORED fan (item or pile-browse — extension record
    /// <see cref="NetProtocol.ExtIdFanAnchor"/>). Absent ⇒ receivers use the authored default
    /// spot, exactly what pre-record peers render.
    /// </summary>
    public bool HasFanAnchor;

    /// <summary>The fan root's position in the sender's control-board LOCAL frame (meaningful
    /// only when <see cref="HasFanAnchor"/>). Receivers apply it as
    /// <c>boardPos + boardRot · (this × boardScale)</c>.</summary>
    public Vector3 FanAnchorLocal;

    /// <summary>
    /// True when this packet names the sender's HIGHLIGHTED cards (extension record
    /// <see cref="NetProtocol.ExtIdCardHighlight"/>). Written only while at least one fan really
    /// has a card singled out, so an idle player's packet stays byte-identical to the previous
    /// build's; absence means "nothing highlighted", which is what pre-record peers render.
    /// </summary>
    public bool HasCardHighlight;

    /// <summary>Index of the highlighted card in the sender's HAND fan, or
    /// <see cref="NetProtocol.CardHighlightNone"/>. A POSITION, never an identity.</summary>
    public byte HandHighlightIndex;

    /// <summary>Index of the highlighted card in the sender's open BOARD fan (item fan or pile
    /// browser — at most one is ever open), or <see cref="NetProtocol.CardHighlightNone"/>.</summary>
    public byte FanHighlightIndex;

    /// <summary>
    /// True when this packet carries the sender's PICK-STATUS line (extension record
    /// <see cref="NetProtocol.ExtIdPickBanner"/>) — the placard above their control board, e.g.
    /// "Barbar: Wähle 1 Karte(n) zum Verlieren". Written only while a placard is really shown, so
    /// an idle packet stays byte-identical to the previous build's; absence means "no placard".
    /// </summary>
    public bool HasPickBanner;

    /// <summary>The sender's pick-status line, in THEIR language (meaningful only when
    /// <see cref="HasPickBanner"/>). An actor name and a count — never a card identity.</summary>
    public string? PickBannerText;

    /// <summary>
    /// True when the sender is holding a SECOND board figure — one mini per hand — and this packet
    /// carries it (extension record <see cref="NetProtocol.ExtIdSecondFigure"/>). The FIRST figure
    /// keeps riding the rig packet's <see cref="NetProtocol.FlagHeldFigure"/> block at the full
    /// 15 Hz; this record carries the other one, and the sender promotes the whole extras packet to
    /// the rig rate while it is carried so both minis stream at the same cadence (see the record
    /// doc for the full argument). Absent ⇒ at most one figure is held, which is exactly what peers
    /// predating the record render.
    /// </summary>
    public bool HasSecondFigure;

    /// <summary>Stable cross-client id (<c>CActor.ID</c>) of that second figure — the same id space
    /// the rig packet's held figure uses. Meaningful only when <see cref="HasSecondFigure"/>.</summary>
    public int SecondFigureActorId;

    /// <summary>World-frame pose of the second held figure (meaningful only when
    /// <see cref="HasSecondFigure"/>). Same encoding as every other pose on this wire.</summary>
    public RigPose SecondFigurePose;

    /// <summary>True when the SECOND figure rides the sender's LEFT hand
    /// (<see cref="NetProtocol.SecondFigureLeftBit"/>).</summary>
    public bool SecondFigureLeftHand;

    /// <summary>True when the FIRST figure — the one in the rig packet — rides the sender's LEFT
    /// hand (<see cref="NetProtocol.SecondFigurePrimaryLeftBit"/>). Carried here because the rig
    /// flag byte has no bit left, and meaningful only while <see cref="HasSecondFigure"/> is set:
    /// it is what lets a reader prove the two figures are in DIFFERENT hands.</summary>
    public bool PrimaryFigureLeftHand;

    /// <summary>
    /// True when this packet carries the BOARD TOOLTIP the sender is reading (extension record
    /// <see cref="NetProtocol.ExtIdBoardTooltip"/>) — the game's hover tooltip while it is parked
    /// in their control board's tooltip area. Written only while such a tooltip is really shown
    /// AND its content is already public to peers (the identity gate lives on the SENDER, in
    /// <c>WorldUI.WorldTooltips</c> — see the record doc); absence means "no tooltip", which is
    /// what peers predating the record render. An idle packet stays byte-identical.
    /// </summary>
    public bool HasBoardTooltip;

    /// <summary>The sender's board-tooltip text, in THEIR language, shown verbatim at the remote
    /// board's tooltip area (meaningful only when <see cref="HasBoardTooltip"/>). Capped on both
    /// ends at <see cref="NetProtocol.TooltipTextMaxBytes"/> UTF8 bytes.</summary>
    public string? BoardTooltipText;

    /// <summary>
    /// True when the sender physically holds a card in EACH hand and this packet carries the
    /// second one (extension record <see cref="NetProtocol.ExtIdSecondHeldCard"/>). The FIRST
    /// held card keeps riding the rig packet's <see cref="NetProtocol.FlagHeldCard"/> block
    /// exactly as before (the sampler's left-first preference is unchanged); this record carries
    /// the OTHER hand's card, and the sender promotes the whole extras packet to the rig rate
    /// while it moves so both slabs stream at the same cadence (see the record doc). Absent ⇒
    /// at most one card is held, which keeps an idle packet byte-identical to build 49 and is
    /// exactly what peers predating the record render.
    /// </summary>
    public bool HasSecondHeldCard;

    /// <summary>World-frame pose of the second held card (meaningful only when
    /// <see cref="HasSecondHeldCard"/>). The shared 20-byte pose encoding — and the ENTIRE
    /// payload: no hand byte, because the receiver renders the slab at this absolute pose and
    /// never parents it to a hand (see the record doc), and no identity, ever.</summary>
    public RigPose SecondHeldCardPose;

    /// <summary>
    /// True when this packet carries the sender's SLOT-CARD SIZE (extension record
    /// <see cref="NetProtocol.ExtIdSlotCardSize"/>): the board-local widths their own board renders
    /// its slot FRAME overlays and a parked CARD at — local config
    /// (<c>[Cards] CardWidth</c>/<c>SlotCardFill</c>) that is not derivable from anything already
    /// synced. False means "the legacy constant" (<see cref="NetProtocol.SlotCardWidthLegacy"/>) —
    /// either the sender's config really lands on it or they predate the record; both render
    /// identically, which is why the record is only written when the sizes differ.
    /// </summary>
    public bool HasSlotCardSize;

    /// <summary>Wire code (tenth-mm) of the sender's slot FRAME width — <c>CardWidth × SlotScale</c>,
    /// what the wanted-glow/frame overlays are sized from. Meaningful only when
    /// <see cref="HasSlotCardSize"/>.</summary>
    public ushort SlotFrameWidthCode;

    /// <summary>Wire code (tenth-mm) of the width a CARD parked in the sender's recess renders at —
    /// <c>CardWidth × SlotScale × SlotCardFill</c>. Meaningful only when
    /// <see cref="HasSlotCardSize"/>.</summary>
    public ushort SlotCardWidthCode;

    /// <summary>
    /// True when this packet carries the sender's displayed PILE COUNTS (extension record
    /// <see cref="NetProtocol.ExtIdPileCounts"/>) — the numbers their own three stack labels show.
    /// Written on every packet while those stacks are displayed, so "present, all zeros" (empty
    /// piles) is distinguishable from "sender predates the field" (absent ⇒ the receiver keeps the
    /// legacy model-read counts). See the record doc for the session evidence that the model read
    /// is not timely on the receiver.
    /// </summary>
    public bool HasPileCounts;

    /// <summary>The sender's displayed DISCARD ("Abgeworfen") stack count (meaningful only when
    /// <see cref="HasPileCounts"/>).</summary>
    public byte PileDiscardCount;

    /// <summary>The sender's displayed BURNT ("Verbrannt") stack count.</summary>
    public byte PileBurntCount;

    /// <summary>The sender's displayed ITEMS ("Gegenstände") stack count.</summary>
    public byte PileItemsCount;

    /// <summary>
    /// True when this packet names the HALF the sender's laser/fingertip is hovering in their
    /// ACTION-SELECTION layout (extension record <see cref="NetProtocol.ExtIdHalfHover"/>): which
    /// board slot's docked round card, top or bottom half. Written ONLY while a half really is
    /// lit, so an idle packet stays byte-identical; absence means "no half lit", which is what
    /// pre-record peers render. A POSITION (slot + half), never a card identity.
    /// </summary>
    public bool HasHalfHover;

    /// <summary>True when byte 0 of the record carries a live HOVER (meaningful only when
    /// <see cref="HasHalfHover"/>). False = the record rides for a SELECTION alone; byte 0 then
    /// holds the <see cref="NetProtocol.HalfHoverNoneSlot"/> sentinel.</summary>
    public bool HalfHoverActive;

    /// <summary>Board slot (0 = left/Slot1, 1 = right/Slot2) of the card whose half the sender is
    /// hovering (meaningful only when <see cref="HalfHoverActive"/>).</summary>
    public byte HalfHoverSlot;

    /// <summary>True when the hovered half is the TOP action, false = bottom (meaningful only when
    /// <see cref="HalfHoverActive"/>).</summary>
    public bool HalfHoverTop;

    /// <summary>Slot 0's persistently SELECTED half — the game's own steady click highlight:
    /// <see cref="NetProtocol.HalfSelectNone"/> / <see cref="NetProtocol.HalfSelectTop"/> /
    /// <see cref="NetProtocol.HalfSelectBottom"/> (meaningful only when
    /// <see cref="HasHalfHover"/>).</summary>
    public byte HalfSelect0;

    /// <summary>Slot 1's persistently selected half, same encoding as <see cref="HalfSelect0"/>.</summary>
    public byte HalfSelect1;

    /// <summary>
    /// The sender's "Keine Handkarten" placard is on screen RIGHT NOW (record 14, byte 1,
    /// <see cref="NetProtocol.HalfEmptyFanHintBit"/>) — the hand-anchored ghost plate
    /// <c>Cards.EmptyFanHint</c> raises when the palm gate opens onto a genuinely empty hand.
    ///
    /// <para>It rides record 14 because both flag bytes are full and byte 1's bits 4..7 were
    /// reserved from the day that record shipped. Setting it also OPENS record 14 on its own: the
    /// record's write gate is now "a half is hovered OR selected OR this bit is set", so the
    /// placard travels even when nothing is hovered. With all three clear the record is still not
    /// written at all, so an idle packet stays byte-identical to the previous build's.</para>
    ///
    /// <para>A LEVEL, not an edge: the receiver runs its OWN 1.5 s fade from its own rising edge
    /// (synced state, locally animated — the wanted-glow blink's contract), and follows this bit
    /// down if the owner's placard is cut short.</para>
    /// </summary>
    public bool EmptyFanHint;

    /// <summary>
    /// True when this packet names the INITIATIVE-TRACK entry the sender is hovering (extension
    /// record <see cref="NetProtocol.ExtIdTrackHover"/>). Written ONLY while they hover one, so an
    /// idle packet stays byte-identical; absence means "no hover", which is what pre-record peers
    /// render. The entry is named by its stable actor id (the ActorGuid hash,
    /// <c>NetFigures.StableActorId</c> — the same id space the held figures use; the per-class
    /// <c>CActor.ID</c> would collide across enemy classes)
    /// because the track's DISPLAY order is per-client during the selection phase
    /// (vanilla sorts own/foreign players differently), so a display index would lift the wrong
    /// portrait on the other side.
    /// </summary>
    public bool HasTrackHover;

    /// <summary>Stable id (<c>NetFigures.StableActorId</c>) of the hovered initiative-track entry (meaningful
    /// only when <see cref="HasTrackHover"/>; never 0 — 0 is "none" everywhere in this system).</summary>
    public int TrackHoverActorId;

    /// <summary>True when the sender's hover has the entry's info popup open (the enemy
    /// round-action preview — public info; player entries have no popup in VR). Meaningful only
    /// when <see cref="HasTrackHover"/>.</summary>
    public bool TrackHoverPopup;

    /// <summary>The sender's currently-faded wall set rides this packet (extension record
    /// <see cref="NetProtocol.ExtIdWallFades"/> — MP wall-fade sync). Written only while the
    /// set is non-empty, so an idle packet stays byte-identical to the previous build's.</summary>
    public bool HasWallFades;

    /// <summary>Number of valid entries in <see cref="WallFadesKeys"/> (≤
    /// <see cref="NetProtocol.WallFadesMaxKeys"/> after clamping on both ends).</summary>
    public int WallFadesCount;

    /// <summary>The faded walls' cross-machine stable keys (see the record doc for the
    /// derivation), sorted ascending. May be longer than <see cref="WallFadesCount"/> (the
    /// sender passes its persistent sample buffer); only the first count entries go on the
    /// wire.</summary>
    public uint[]? WallFadesKeys;

    /// <summary>
    /// True when this packet carries the BUTTON LABELS of the sender's docked decision row
    /// (extension record <see cref="NetProtocol.ExtIdDecisionLines"/>). Written only while a
    /// decision row is really docked on their board — absence means "no docked decision", which
    /// is what peers predating the record render (the empty drawer via the board-UI bit).
    /// </summary>
    public bool HasDecisionLines;

    /// <summary>The docked decision row's button labels, one per line ('\n'-joined), in the
    /// SENDER's language (meaningful only when <see cref="HasDecisionLines"/>). Pressable-widget
    /// labels ONLY — never a dialog's description text, which could name a card (see the record
    /// doc). Capped on both ends at <see cref="NetProtocol.DecisionLinesMaxBytes"/> UTF8 bytes.</summary>
    public string? DecisionLinesText;

    /// <summary>
    /// True when this packet carries the STATE of the sender's docked decision display (extension
    /// record <see cref="NetProtocol.ExtIdDecisionState"/>): which prompt is docked, which prompt
    /// TEXT variant it shows, and per option offered / dimmed / chosen. Written on exactly the
    /// same gate as <see cref="HasDecisionLines"/> (a row really docked AND visible on the owner's
    /// board), so the wordings and their states can never disagree. Absence renders as the
    /// pre-record look: mirrored plates with no state and no prompt text.
    /// </summary>
    public bool HasDecisionState;

    /// <summary>Which prompt is docked — one of <see cref="NetProtocol.DecisionKindNone"/> …
    /// <see cref="NetProtocol.DecisionKindDialogPopup"/> (meaningful only when
    /// <see cref="HasDecisionState"/>).</summary>
    public byte DecisionPromptKind;

    /// <summary>Which prompt-TEXT variant the owner's tip window shows — one of
    /// <see cref="NetProtocol.DecisionTextNone"/> … <see cref="NetProtocol.DecisionTextMandatoryUse"/>.
    /// A NUMBER, never the text: the receiver composes the line from its own localization table
    /// (see the record doc for why the composed string may never ride the wire).</summary>
    public byte DecisionTextVariant;

    /// <summary>Number of valid entries in <see cref="DecisionOptionFlags"/> (≤
    /// <see cref="NetProtocol.DecisionStateMaxOptions"/> after clamping on both ends), index-aligned
    /// with <see cref="DecisionLinesText"/>'s '\n'-separated lines.</summary>
    public int DecisionOptionCount;

    /// <summary>Per-option state bytes (<see cref="NetProtocol.DecisionOptionOfferedBit"/> …
    /// <see cref="NetProtocol.DecisionOptionChosenBit"/>). May be longer than
    /// <see cref="DecisionOptionCount"/> — the sender passes its persistent sample buffer, exactly
    /// like <see cref="WallFadesKeys"/>; only the first count entries go on the wire.</summary>
    public byte[]? DecisionOptionFlags;

    /// <summary>
    /// True when this packet names what the sender's CONFIRM board cap actually reads (extension
    /// record <see cref="NetProtocol.ExtIdCapLabels"/>, mask bit 0). Absence keeps the receiver's
    /// neutral GUI_CONFIRM fallback — exactly what peers predating the record render.
    /// </summary>
    public bool HasConfirmCapLabel;

    /// <summary>The sender's live CONFIRM cap wording ("Fortfahren", "✓ BEREIT", a pick-flow
    /// override…), in THEIR language (meaningful only when <see cref="HasConfirmCapLabel"/>).
    /// Capped at <see cref="NetProtocol.CapLabelMaxBytes"/> UTF8 bytes.</summary>
    public string? ConfirmCapLabel;

    /// <summary>
    /// True when this packet names what the sender's docked SKIP button actually reads (extension
    /// record <see cref="NetProtocol.ExtIdCapLabels"/>, mask bit 1). Absence keeps the receiver's
    /// neutral GUI_SKIP_MOVEMENT fallback.
    /// </summary>
    public bool HasSkipCapLabel;

    /// <summary>The sender's live SKIP wording ("Bewegen überspringen", "Angriff überspringen"…),
    /// in THEIR language (meaningful only when <see cref="HasSkipCapLabel"/>). Capped at
    /// <see cref="NetProtocol.CapLabelMaxBytes"/> UTF8 bytes.</summary>
    public string? SkipCapLabel;

    /// <summary>
    /// True when this packet names the character the sender is currently FOCUSED on (extension
    /// record <see cref="NetProtocol.ExtIdCharFocus"/>). Written only while a focus is actually
    /// known (a non-zero actor id); absence means "no focus known", which renders as no
    /// focus/turn outline at all — exactly what peers predating the record show.
    /// </summary>
    public bool HasCharFocus;

    /// <summary>Stable id (<c>NetFigures.StableActorId</c> — the ActorGuid hash, the only id space
    /// that agrees across machines) of the character the sender is looking at. Meaningful only when
    /// <see cref="HasCharFocus"/>; never 0 — 0 is "none" everywhere in this system.</summary>
    public int CharFocusActorId;

    /// <summary>True when the character THE GAME IS WAITING ON — at turn, or (nobody at turn) the
    /// one owing an open decision — is under the SENDER's control. Only the owning client can
    /// evaluate either half (<c>CActor.IsUnderMyControl</c> is a local flag, and a remote player's
    /// decision panel is hidden on every other machine), which is why it travels. Meaningful only
    /// when <see cref="HasCharFocus"/>.</summary>
    public bool CharFocusOwnsAttention;

    /// <summary>
    /// Stable id of the character the game is waiting on (0 = nobody, or nobody the sender owns).
    /// The receiver's whole answer: the mark is <c>this == CharFocusActorId</c> ? green : red, and
    /// the mirrored initiative track rings THIS entry. Meaningful only when
    /// <see cref="HasCharFocus"/> and <see cref="CharFocusOwnsAttention"/>.
    ///
    /// <para>ON THE WIRE ONLY WHEN IT DIFFERS from <see cref="CharFocusActorId"/> (record 22 flags
    /// bit 1): when the sender is looking AT the character being waited on, the focus id already
    /// carries it. The serializer applies that rule; both sides of it read this one field, so a
    /// caller never has to know which form went out.</para>
    /// </summary>
    public int CharFocusAttentionActorId;


    /// <summary>
    /// True when this packet names the initiative-track entries the sender's OWN track is currently
    /// framing with vanilla's selection frame (extension record
    /// <see cref="NetProtocol.ExtIdTrackSelection"/>). Written only while at least one frame really
    /// stands on their screen; absence means "no frame", which is exactly what peers predating the
    /// record render. Read off the live <c>selectionObject.activeSelf</c>, so it is what the owner
    /// SEES rather than a re-derivation — see the record doc for the three states in which the
    /// character-focus record (22) answers a different question.
    /// </summary>
    public bool HasTrackSelection;

    /// <summary>Number of valid entries in <see cref="TrackSelectionIds"/> (≤
    /// <see cref="NetProtocol.TrackSelectionMaxIds"/> after clamping on both ends). Meaningful only
    /// when <see cref="HasTrackSelection"/>.</summary>
    public int TrackSelectionCount;

    /// <summary>Stable ids (<c>NetFigures.StableActorId</c> — the ActorGuid hash, the one id space
    /// that agrees across machines) of the framed track entries. PLAYERS, ENEMIES and OBJECT actors
    /// alike: the frame is a track fact, not a character fact. May be longer than
    /// <see cref="TrackSelectionCount"/> (the sender passes its persistent sample buffer); only the
    /// first count entries go on the wire.</summary>
    public int[]? TrackSelectionIds;

    /// <summary>
    /// True when this packet carries the sender's OWN TUNING of their board, fan and board mesh
    /// (extension record <see cref="NetProtocol.ExtIdBoardTuning"/>). Written ONLY while at least
    /// one dial differs from the shipped default for their synced board style — an untuned player
    /// (the overwhelmingly common case) emits the exact bytes the previous build emitted.
    /// </summary>
    public bool HasBoardTuning;

    /// <summary>The record's complete payload — <c>[field count][field…]</c>, already quantized and
    /// in ascending id order. It is carried PRE-ENCODED rather than as ~49 named fields for the
    /// same reason the text records carry pre-encoded bytes: the values change on a config edit,
    /// not per packet, so the sender builds this once on a change edge and the 5 Hz write path is a
    /// pure copy. On the receive side it is the raw record bytes, read with
    /// <see cref="NetProtocol.BoardTuneVector"/> and friends against each caller's own shipped
    /// default.</summary>
    public byte[]? BoardTuningBytes;

    /// <summary>Valid length of <see cref="BoardTuningBytes"/> (the buffer may be longer — the
    /// sender keeps a persistent one). Meaningful only when <see cref="HasBoardTuning"/>.</summary>
    public int BoardTuningLength;
}

/// <summary>
/// Compact, allocation-free (de)serialization of a <see cref="PresenceState"/> (wire message
/// TYPE <see cref="NetProtocol.MsgExtras"/>). Reuses <see cref="AvatarSerializer"/>'s little-endian
/// + pose primitives so both packets encode identically. Never throws — magic / version / type
/// are all gated on read.
///
/// Layout (little-endian), wire v3 type 1:
///   [0..3] uint32 magic | [4] version | [5] type(==MsgExtras) | [6] flags
///     flags: bit0 hasBoard, bit1 dominantRight, bit2 ghostHand, bit3 itemFan,
///            bit4 itemFanHeld, bit5 cardFx, bit6 itemFanLeftHand, bit7 pileBrowse
///   if hasBoard: pose(pos 12 + rot 8 = 20) + scale(float32 = 4) → 24 bytes
///   [.] byte handCardCount
///   if ghostHand:  byte ghostStrength (0..255 ⇒ 0..1)             → 1 byte   (ADDITIVE)
///   if itemFan:    byte itemCardCount                             → 1 byte   (ADDITIVE)
///   if cardFx:     byte fxSeq + byte fxEndpoints                  → 2 bytes  (ADDITIVE)
///   if pileBrowse: byte kindFlags + byte browseCardCount          → 2 bytes  (ADDITIVE)
///                  kindFlags: bits0..1 pile kind (0 discard / 1 burnt / 2 items),
///                             bit2 hand-held, bit3 left hand,
///                             bit4 MASK-SIZE byte follows,
///                             bits5..6 CONTROL-BOARD STYLE (0 Oak / 1 Steel / 2 Bronze),
///                             bit7 reserved (0)
///     if kindFlags bit4: byte maskSizeCode (size × 100 ⇒ 0.25×..2.55×)  → 1 byte  (ADDITIVE,
///                        INSIDE the block — this is the reserved-bit extension path)
///     if kindFlags bit7: EXTENSION TAIL [count]([id][len][payload])* — current record ids:
///                        1 hand scale (1 B), 2 ghost sides (1 B),
///                        3 MOD VERSION ([u16 build LE][UTF8 display ≤ 20 B] — sent on EVERY
///                        packet; its absence marks a pre-handshake peer, see NetProtocol.ModBuild),
///                        4 BOARD UI ([buttons][overlays] — sent on every packet with a board pose;
///                        see NetProtocol.ExtIdBoardUi for the bit layout),
///                        5 FAN ANCHOR (3 × f32 LE board-local position of the open board-anchored
///                        fan; absent = the authored default spot, see NetProtocol.ExtIdFanAnchor),
///                        6 CARD HIGHLIGHT ([hand-fan index][board-fan index], 255 = none — only
///                        written while something is highlighted, see NetProtocol.ExtIdCardHighlight),
///                        7 PICK BANNER (UTF8 placard line, capped — see NetProtocol.ExtIdPickBanner),
///                        8 SECOND HELD FIGURE ([hand flags][int32 actorId LE][pose 20] = 25 B — the
///                        mini in the sender's OTHER hand; the first one rides the rig packet, see
///                        NetProtocol.ExtIdSecondFigure),
///                        9 BOARD TOOLTIP (UTF8 text of the tooltip parked in the sender's board
///                        tooltip area, capped and IDENTITY-GATED on the sender — only content
///                        already public to peers is ever written; see NetProtocol.ExtIdBoardTooltip),
///                        10 SECOND HELD CARD (the shared 20-byte pose of the card in the sender's
///                        OTHER hand — pose only, no hand byte and no identity; written ONLY while
///                        both hands hold a card, see NetProtocol.ExtIdSecondHeldCard),
///                        11 SLOT-CARD SIZE ([u16 slotFrameWidth LE][u16 slotCardWidth LE], both
///                        board-local tenth-mm — the sizes the sender's own board renders its slot
///                        overlays / a parked card at; written only while a board exists AND either
///                        differs from the legacy 82.55 mm assumption, see
///                        NetProtocol.ExtIdSlotCardSize),
///                        12 DECISION LINES (UTF8 blob, one docked decision-button label per
///                        '\n'-separated line, capped — pressable-widget labels only, never a
///                        dialog's description text; written ONLY while a decision row is docked,
///                        see NetProtocol.ExtIdDecisionLines),
///                        13 CAP LABELS ([mask][per set bit: len + UTF8] — the live wording of the
///                        sender's CONFIRM cap (bit0) and docked SKIP button (bit1), each capped;
///                        written ONLY while a cap is visible with a known label, see
///                        NetProtocol.ExtIdCapLabels),
///                        14 HALF HOVER + SELECTION + EMPTY-FAN HINT (2 B: byte0 hover — bits0..1
///                        board slot with 3 = no hover, bit2 top half; byte1 the persistent CLICK
///                        state — one 2-bit none/top/bottom field per slot — the game's steady half
///                        highlight after a click, cleared by undo, PLUS bit4 = the sender's
///                        "Keine Handkarten" placard is on screen (NetProtocol.HalfEmptyFanHintBit,
///                        bits 5..7 still reserved); written while a half is hovered OR selected OR
///                        that placard is up, see NetProtocol.ExtIdHalfHover),
///                        15 PILE COUNTS ([discard][burnt][items] — the numbers the sender's own
///                        stack labels display; sent on EVERY packet while those stacks are shown,
///                        absence = pre-record peer ⇒ legacy model-read counts, see
///                        NetProtocol.ExtIdPileCounts),
///                        16 TRACK HOVER ([flags][int32 actorId LE] — the initiative-track entry
///                        the sender hovers, by the stable ActorGuid hash (NetFigures.StableActorId;
///                        display order is per-client); flags bit0 = info popup open; only while
///                        hovering, see NetProtocol.ExtIdTrackHover),
///                        17 WALL FADES ([count][count × u32 wall key LE] — the sender's
///                        currently-faded wall set by cross-machine stable key, ≤24, sorted;
///                        only while non-empty; receiver-gated by [WallFade] SyncPeerFades,
///                        see NetProtocol.ExtIdWallFades),
///                        22 CHARACTER FOCUS ([flags][int32 focusActorId LE] — the character the
///                        sender is currently LOOKING at, by the stable ActorGuid hash; flags bit0
///                        = the sender OWNS the character at turn (a local-only fact, hence on the
///                        wire); written only while a focus is known; drives the green/red
///                        control-board + Steam-avatar outlines, see NetProtocol.ExtIdCharFocus.
///                        Ids 18..21 are reserved for records developed in parallel.),
///                        23 TRACK SELECTION ([count][count × int32 actorId LE] — the initiative-track
///                        entries the sender's OWN track is framing with vanilla's selectionObject,
///                        by the stable ActorGuid hash; players, ENEMIES and objects alike; ≤4 ids
///                        because an extra-turn actor can leave a second frame standing; written
///                        only while a frame really stands, see NetProtocol.ExtIdTrackSelection)
///                        28 BOARD TUNING ([n][n × [id][value]] — the SPARSE set of the sender's
///                        own dials that differ from the shipped default for their synced board
///                        style: dock offsets/scales, furniture seats, the board MESH pose and the
///                        hand-fan geometry. The id's RANGE fixes the value width (vec3 6 B /
///                        length 2 B / factor 2 B / angle 2 B / count 1 B), fields ascend by id,
///                        and the record is omitted ENTIRELY when nothing is tuned — the common
///                        case, byte-identical to the previous build. Ids 25..27 belong to records
///                        developed in parallel; 18..21 stay reserved.
///                        See NetProtocol.ExtIdBoardTuning),
///                        24 DECISION STATE ([flags][n][n × option byte] — which prompt is docked
///                        (flags bits 0..2), which prompt-TEXT variant it shows (bits 3..5, a
///                        NUMBER the receiver localizes itself; the composed text never rides the
///                        wire because it can embed active-bonus card names), and per option
///                        offered / dimmed / chosen, index-aligned with record 12's lines; written
///                        on record 12's own gate, see NetProtocol.ExtIdDecisionState)
///
/// The four additive blocks are written and read in FLAG-BIT ORDER (ghost, item fan, card FX, pile
/// browse). That single rule is what lets independently developed extensions share one packet: each
/// block only has to be appended behind every field a pre-existing reader knows, and the bit order
/// then fixes the layout without any writer/reader having to know about the others.
///
/// FLAG BITS ARE EXHAUSTED (bit 7 = pile browse is the last one). That is deliberate and it is why
/// the pile-browse block spends its bit on "a block follows" rather than on a single boolean: its
/// byte A carries reserved bits, so the NEXT extras extension can be appended inside this block —
/// still additive, still no wire-version bump — instead of running out of flag byte. The HEAD-MASK
/// SIZE is the first user of that room (byte A bit 4 + trailing byte C, see
/// <see cref="NetProtocol.PileBrowseMaskSizeBit"/>); the CONTROL-BOARD STYLE is the second and
/// cheapest possible one (byte A bits 5..6, <see cref="NetProtocol.PileBrowseBoardStyleShift"/> —
/// no trailing byte at all, so the packet length does not even change). Because of them, flag bit 7
/// now means "a trailing
/// BLOCK follows", not "a browse fan is open": a size-only packet writes the block with the
/// pile-browse sub-fields zeroed and byte B (count) = 0, and every reader that ever understood bit 7
/// requires count &gt; 0 before it renders a fan — so it sees no fan and ignores byte C.
///
/// BACKWARD COMPATIBILITY CONTRACT (all additive blocks): they are appended AFTER every field a
/// pre-existing reader knows, in flag-bit order, and that reader validates only the length ITS
/// known flags demand — so it parses the packet exactly as before, ignores the unknown flag bits
/// and the trailing bytes, and simply shows no item fan / no card flights / no browse fan. No
/// version bump, no compat break in either direction: a NEW reader receiving an OLD packet just
/// sees the flags clear.
/// </summary>
internal static class PresenceSerializer
{
    /// <summary>Upper bound on an encoded extras packet: header 7 + board 24 + count 1 +
    /// ghost strength 1 + item-fan 1 + card-fx 2 + pile-browse 2 + mask size 1 = 39 — plus the
    /// extension tail: 1 count byte + 3 (hand scale) + 3 (ghost sides) + up to 2+2+20 = 24
    /// (mod version, the largest record) + 4 (board UI) + 14 (fan anchor) + 4 (card highlight)
    /// + 98 (pick banner: 2 + its 96-byte cap) + 27 (second held figure: 2 + 25)
    /// + 194 (board tooltip: 2 + its 192-byte cap) + 22 (second held card: 2 + 20)
    /// + 6 (slot-card size: 2 + 4) + 162 (decision lines: 2 + its 160-byte cap)
    /// + 101 (cap labels: 2 + mask 1 + 2 × (len 1 + 48-byte cap)) + 4 (half hover+select: 2 + 2)
    /// + 5 (pile counts: 2 + 3) + 7 (track hover: 2 + 5)
    /// + 99 (wall fades: 2 + count 1 + 4 × its 24-key cap)
    /// + 11 (character focus: 2 + its 9-byte maximum — the 5-byte form plus the flag-guarded
    /// attention-actor id)
    /// + 19 (track selection: 2 + count 1 + 4 × its 4-id cap)
    /// + 12 (decision state: 2 + flags 1 + count 1 + its 8-option cap)
    /// + 211 (BOARD TUNING: 2 + count 1 + every one of its 50 fields at once —
    /// 15 vec3 × 7 + 12 length × 3 + 16 factor × 3 + 5 angle × 3 + 2 count × 2 = 208) = 1070.
    ///
    /// <para>THAT 211 IS A CEILING NO REAL PACKET REACHES: the board-tuning record carries only the
    /// dials a player has MOVED, and an untuned player writes no record at all. It is stated at its
    /// maximum here because the buffer must survive the pathological sender, not the typical one.
    /// Margin left: 1280 − 1070 = 210 bytes, still more than the largest single record on the wire
    /// (194, the board tooltip), which is the rule below. The sum before it was 859.</para>
    ///
    /// <para>RAISED 848 → 1280 on 2026-08-08, deliberately and ahead of need rather than on a crash.
    /// Three records landed in one round (22's attention tail, 23 track selection, 24 decision state)
    /// and the worst case went 824 → 859; at the old 848 bound the margin had been ONE byte, less
    /// than any record, so the next additive record would have discovered the ceiling by overflowing
    /// it. The 1:1 mirroring ruling still has the four USE BARS on the roadmap. This constant sizes
    /// ONE local send buffer (<c>NetAvatarDriver._sendBuffer</c>) and appears in no packet, no header
    /// and no contract, so raising it is invisible to every peer including older builds: what
    /// actually goes out is the byte count each writer returns. Raising it does NOT authorise bigger
    /// packets — every record still bounds-checks against the real buffer before writing a byte — it
    /// only stops the cap itself from being the thing that silently drops a record.</para>
    ///
    /// <para>THE RULE THAT COMES WITH IT: every new record adds its worst case to the sum above IN
    /// ITS OWN COMMIT, and keeps a margin of at least one record's worth.</para></summary>
    public const int MaxSize = 1280;

    // ---- write --------------------------------------------------------------------------

    /// <summary>Serialize <paramref name="state"/> into <paramref name="buffer"/> (must be &gt;=
    /// <see cref="MaxSize"/>). Returns the byte count written. No heap allocation.</summary>
    public static int Write(in PresenceState state, byte[] buffer)
    {
        int i = 0;
        AvatarSerializer.WriteU32(buffer, ref i, NetProtocol.Magic);
        buffer[i++] = NetProtocol.Version;
        buffer[i++] = NetProtocol.MsgExtras;

        byte flags = 0;
        if (state.HasBoard) flags |= NetProtocol.FlagHasBoard;
        if (state.DominantRight) flags |= NetProtocol.FlagExtrasDominantRight;
        if (state.GhostHand) flags |= NetProtocol.FlagExtrasGhostHand;
        if (state.HasItemFan) flags |= NetProtocol.FlagItemFan;
        if (state.HasItemFan && state.ItemFanHeld) flags |= NetProtocol.FlagItemFanHeld;
        if (state.HasItemFan && state.ItemFanHeld && state.ItemFanLeftHand) flags |= NetProtocol.FlagItemFanLeft;
        if (state.HasCardFx) flags |= NetProtocol.FlagCardFx;
        // Bit 7 is the trailing-BLOCK header, not "a browse fan is open": the block also carries
        // the head-mask size in its reserved byte-A bits, so it goes out whenever EITHER rides
        // this packet (see the layout doc + NetProtocol.PileBrowseMaskSizeBit).
        // The board STYLE rides the same block's byte A (bits 5..6) and therefore also decides
        // whether the block goes out — but only when it is NON-default, so a player on the default
        // board still emits the exact bytes previous builds did.
        bool boardStyle = state.BoardStyleCode != NetProtocol.BoardStyleDefaultCode;
        bool extensions = state.HasHandScale || state.HasGhostSides || state.HasModVersion
                          || state.HasBoardUi || state.HasFanAnchor || state.HasCardHighlight
                          || state.HasSecondFigure || state.HasSecondHeldCard
                          || state.HasSlotCardSize
                          || state.HasPileCounts
                          // Record 14 also rides for the EMPTY-FAN placard alone (byte 1 bit 4),
                          // so the tail gate is the same three-way OR its writer uses.
                          || state.HasHalfHover || state.EmptyFanHint
                          // An all-default player writes NO tuning record, so it must not open the
                          // tail either — that is what keeps an untuned packet byte-identical to
                          // the previous build's, and it is the whole economic case for record 28.
                          || (state.HasBoardTuning && state.BoardTuningLength
                              >= NetProtocol.BoardTuneMinRecordBytes
                              && state.BoardTuningBytes != null)
                          // A zero actor id writes no record (0 = "none" everywhere), so it must
                          // not open the tail either — same rule as the empty pick-banner line.
                          || (state.HasTrackHover && state.TrackHoverActorId != 0)
                          // An EMPTY wall-fade set writes no record, so it must not open the
                          // tail either (idle packets stay byte-identical to the last build's).
                          || (state.HasWallFades && state.WallFadesCount > 0
                              && state.WallFadesKeys != null)
                          // A zero actor id writes no focus record (0 = "none" everywhere), so it
                          // must not open the tail either — same rule as the track-hover record.
                          || (state.HasCharFocus && state.CharFocusActorId != 0)
                          // An EMPTY selection writes no record, so it must not open the tail
                          // either — same rule as the wall-fade set.
                          || (state.HasTrackSelection && state.TrackSelectionCount > 0
                              && state.TrackSelectionIds != null)
                          // An EMPTY line writes no record, so it must not open the tail either —
                          // that is what keeps an idle packet byte-identical to the last build's.
                          || (state.HasPickBanner && !string.IsNullOrEmpty(state.PickBannerText))
                          || (state.HasBoardTooltip && !string.IsNullOrEmpty(state.BoardTooltipText))
                          || (state.HasDecisionLines && !string.IsNullOrEmpty(state.DecisionLinesText))
                          // The decision STATE record rides record 12's own gate; with nothing to
                          // state (no kind, no text variant, no options) it writes no record, so it
                          // must not open the tail either.
                          || (state.HasDecisionState && DecisionStatePayload(in state) > 0)
                          || (state.HasConfirmCapLabel && !string.IsNullOrEmpty(state.ConfirmCapLabel))
                          || (state.HasSkipCapLabel && !string.IsNullOrEmpty(state.SkipCapLabel));
        bool block = state.HasPileBrowse || state.HasMaskSize || boardStyle || extensions;
        if (block) flags |= NetProtocol.FlagPileBrowse;
        buffer[i++] = flags;

        if (state.HasBoard)
        {
            AvatarSerializer.WritePoseShared(buffer, ref i, in state.Board);
            float scale = state.BoardScale > 0f ? state.BoardScale : 1f;
            AvatarSerializer.WriteF32(buffer, ref i, scale);
        }

        buffer[i++] = state.HandCardCount;

        // ---- ADDITIVE trailing blocks (MUST stay after handCardCount and in FLAG-BIT ORDER) ----
        // Order is the contract (see the layout doc): ghost strength, item-fan count, card FX,
        // pile browse. Pre-extension readers keep finding handCardCount at the offset they expect,
        // and each block is invisible to a reader whose flags do not include it.
        if (state.GhostHand)
            buffer[i++] = state.GhostStrength;
        if (state.HasItemFan)
            buffer[i++] = state.ItemCardCount;
        if (state.HasCardFx)
        {
            buffer[i++] = state.FxSeq;
            buffer[i++] = state.FxEndpoints;
        }
        if (block)
        {
            // Byte A packs everything that is NOT a count: the pile kind (2 bits), the two
            // placement bits, and — first user of the reserved room the last flag bit was spent
            // to create — the "a mask-size byte follows" bit. Bits 5..6 are the SECOND user of
            // that room, the control-board style, written by the last statement in this block.
            // Bit 7 (NetProtocol.PileBrowseExtensionBit) is now the EXTENSION TAIL flag: set it
            // and a type-length-value tail follows byte C. That is what replaced "the last free
            // bit" with room that does not run out.
            //
            // When only the mask size rides this packet, the pile-browse sub-fields are written
            // ZEROED and byte B (count) is 0: that is precisely how a reader — new or old — is
            // told "no browse fan" (all of them gate the fan on count > 0), so the block can
            // carry the size without inventing a fan on anybody's screen.
            byte kindFlags = state.HasPileBrowse ? (byte)(state.PileBrowseKind & 0x03) : (byte)0;
            if (state.HasPileBrowse && state.PileBrowseHeld) kindFlags |= NetProtocol.PileBrowseHeldBit;
            if (state.HasPileBrowse && state.PileBrowseHeld && state.PileBrowseLeftHand)
                kindFlags |= NetProtocol.PileBrowseLeftBit;
            if (state.HasMaskSize) kindFlags |= NetProtocol.PileBrowseMaskSizeBit;
            // Board style: two bits IN this byte, no trailing byte — the cheapest extension the
            // reserved room allows. Zero (= Oak, the default board) is what an older sender writes
            // here anyway, so the value space and the "field absent" case coincide by construction.
            kindFlags |= (byte)((NetProtocol.EncodeBoardStyle(state.BoardStyleCode)
                                 << NetProtocol.PileBrowseBoardStyleShift)
                                & NetProtocol.PileBrowseBoardStyleMask);
            if (extensions) kindFlags |= NetProtocol.PileBrowseExtensionBit;
            buffer[i++] = kindFlags;
            buffer[i++] = state.HasPileBrowse ? state.PileBrowseCardCount : (byte)0;
            if (state.HasMaskSize)
                buffer[i++] = state.MaskSizeCode;

            // ---- EXTENSION TAIL: [count] then count x [id][len][payload] ----------------------
            // Written LAST so every offset above is exactly where a pre-extension reader expects
            // it, and self-describing so a reader that does not know an id can step over it.
            if (extensions)
            {
                int countAt = i++;
                byte records = 0;
                if (state.HasHandScale)
                {
                    buffer[i++] = NetProtocol.ExtIdHandScale;
                    buffer[i++] = 1;
                    buffer[i++] = state.HandScaleCode;
                    records++;
                }
                if (state.HasGhostSides)
                {
                    buffer[i++] = NetProtocol.ExtIdGhostSides;
                    buffer[i++] = 1;
                    buffer[i++] = state.GhostSidesMask;
                    records++;
                }
                if (state.HasModVersion)
                {
                    // MOD VERSION: [u16 build LE][UTF8 display bytes]. Sent on EVERY packet (unlike
                    // the "only when non-default" records above) because its absence IS the signal:
                    // a modded peer whose extras never carry this record predates the handshake and
                    // reads as ModBuild 0 = mismatch. The display bytes come pre-encoded + capped
                    // (EncodeModVersionText) so this hot path stays allocation-free.
                    byte[] text = state.ModVersionText == null
                        ? System.Array.Empty<byte>()
                        : EncodeModVersionText(state.ModVersionText);
                    buffer[i++] = NetProtocol.ExtIdModVersion;
                    buffer[i++] = (byte)(2 + text.Length);
                    buffer[i++] = (byte)(state.ModBuild & 0xFF);
                    buffer[i++] = (byte)(state.ModBuild >> 8);
                    for (int b = 0; b < text.Length; b++)
                        buffer[i++] = text[b];
                    records++;
                }
                if (state.HasBoardUi)
                {
                    // BOARD UI: [buttons][overlays]. Like the mod version it is written whenever
                    // its source exists (a live PlayTray) rather than only when non-default: the
                    // receiver must tell "the owner's board shows no dynamic controls" apart from
                    // "the sender predates the field" — the latter keeps the legacy furniture.
                    buffer[i++] = NetProtocol.ExtIdBoardUi;
                    buffer[i++] = 2;
                    buffer[i++] = state.BoardButtonsMask;
                    // Masked to the DEFINED overlay bits (wanted glow + FOLLOW/PIN + card-slot
                    // occupancy + its validity bit): an undefined bit must never be pre-claimed by
                    // garbage, or widening the mask later would decode old packets as if they had
                    // opted into the new state.
                    buffer[i++] = (byte)(state.BoardOverlayMask & NetProtocol.BoardUiOverlayMask);
                    records++;
                }
                if (state.HasFanAnchor)
                {
                    // FAN ANCHOR: 3 × f32 LE, the open board-anchored fan's board-local position.
                    buffer[i++] = NetProtocol.ExtIdFanAnchor;
                    buffer[i++] = 12;
                    AvatarSerializer.WriteF32(buffer, ref i, state.FanAnchorLocal.x);
                    AvatarSerializer.WriteF32(buffer, ref i, state.FanAnchorLocal.y);
                    AvatarSerializer.WriteF32(buffer, ref i, state.FanAnchorLocal.z);
                    records++;
                }
                if (state.HasCardHighlight)
                {
                    // CARD HIGHLIGHT: [hand-fan index][board-fan index], 255 = none. Unlike the
                    // board-UI record this one is written ONLY while something is highlighted —
                    // "absent" and "none" render identically, so an idle packet stays as small
                    // (and as byte-identical to the previous build) as it always was.
                    buffer[i++] = NetProtocol.ExtIdCardHighlight;
                    buffer[i++] = 2;
                    buffer[i++] = state.HandHighlightIndex;
                    buffer[i++] = state.FanHighlightIndex;
                    records++;
                }
                if (state.HasPickBanner && !string.IsNullOrEmpty(state.PickBannerText))
                {
                    // PICK BANNER: UTF8 bytes of the placard line, capped and truncated on a
                    // character boundary. Written only while a placard is shown (see the record
                    // doc); the encode cache keeps this hot path allocation-free for the common
                    // case of the same line riding several packets in a row.
                    byte[] text = EncodePickBannerText(state.PickBannerText!);
                    if (text.Length > 0 && i + 2 + text.Length <= buffer.Length)
                    {
                        buffer[i++] = NetProtocol.ExtIdPickBanner;
                        buffer[i++] = (byte)text.Length;
                        for (int b = 0; b < text.Length; b++)
                            buffer[i++] = text[b];
                        records++;
                    }
                }
                if (state.HasSecondFigure && i + 2 + NetProtocol.SecondFigureRecordBytes <= buffer.Length)
                {
                    // SECOND HELD FIGURE: [hand flags][int32 actorId LE][pose 20]. The mini in the
                    // sender's OTHER hand — the first one rides the rig packet's held-figure block.
                    // Written ONLY while a second figure is really held, so a one-handed hold (and
                    // an idle player) emits the exact bytes previous builds emitted. Appended LAST,
                    // behind every record that already existed, per the tail's id-order contract.
                    byte hands = 0;
                    if (state.SecondFigureLeftHand) hands |= NetProtocol.SecondFigureLeftBit;
                    if (state.PrimaryFigureLeftHand) hands |= NetProtocol.SecondFigurePrimaryLeftBit;
                    buffer[i++] = NetProtocol.ExtIdSecondFigure;
                    buffer[i++] = (byte)NetProtocol.SecondFigureRecordBytes;
                    // Masked to the DEFINED hand bits so a future bit cannot be pre-claimed by
                    // garbage — same discipline as the board-UI overlay byte.
                    buffer[i++] = (byte)(hands & NetProtocol.SecondFigureHandMask);
                    AvatarSerializer.WriteI32(buffer, ref i, state.SecondFigureActorId);
                    AvatarSerializer.WritePoseShared(buffer, ref i, in state.SecondFigurePose);
                    records++;
                }
                if (state.HasBoardTooltip && !string.IsNullOrEmpty(state.BoardTooltipText))
                {
                    // BOARD TOOLTIP: UTF8 bytes of the tooltip parked in the sender's board
                    // tooltip area, capped and truncated on a character boundary. Written only
                    // while such a tooltip is shown AND already passed the sender-side identity
                    // gate (WorldUI.WorldTooltips — see the record doc: content that could name a
                    // hidden card never reaches this writer). Appended LAST, behind every record
                    // that already existed, per the tail's id-order contract; the encode cache
                    // keeps the hot path allocation-free while the same text rides many packets.
                    byte[] text = EncodeBoardTooltipText(state.BoardTooltipText!);
                    if (text.Length > 0 && i + 2 + text.Length <= buffer.Length)
                    {
                        buffer[i++] = NetProtocol.ExtIdBoardTooltip;
                        buffer[i++] = (byte)text.Length;
                        for (int b = 0; b < text.Length; b++)
                            buffer[i++] = text[b];
                        records++;
                    }
                }
                if (state.HasSecondHeldCard
                    && i + 2 + NetProtocol.SecondHeldCardRecordBytes <= buffer.Length)
                {
                    // SECOND HELD CARD: the shared 20-byte pose, nothing else — no hand byte (the
                    // receiver renders the slab at this absolute pose, never parented to a hand;
                    // see the record doc) and no identity, ever (peers draw an anonymous BACK).
                    // Written ONLY while both hands physically hold a card, so a one-card hold —
                    // and an idle player — emits the exact bytes build 49 emitted. Appended LAST,
                    // behind every record that already existed, per the tail's id-order contract.
                    buffer[i++] = NetProtocol.ExtIdSecondHeldCard;
                    buffer[i++] = (byte)NetProtocol.SecondHeldCardRecordBytes;
                    AvatarSerializer.WritePoseShared(buffer, ref i, in state.SecondHeldCardPose);
                    records++;
                }
                if (state.HasSlotCardSize
                    && i + 2 + NetProtocol.SlotCardSizeRecordBytes <= buffer.Length)
                {
                    // SLOT-CARD SIZE: [u16 slotFrameWidth LE][u16 slotCardWidth LE], board-local
                    // tenth-mm. Written ONLY while a board exists and either width differs from
                    // the legacy assumption (NetProtocol.SlotCardWidthLegacy), so a sender whose
                    // config lands exactly on the old constant stays byte-identical to the
                    // previous build. Appended in id order (record 11, before 12..16).
                    buffer[i++] = NetProtocol.ExtIdSlotCardSize;
                    buffer[i++] = (byte)NetProtocol.SlotCardSizeRecordBytes;
                    buffer[i++] = (byte)(state.SlotFrameWidthCode & 0xFF);
                    buffer[i++] = (byte)(state.SlotFrameWidthCode >> 8);
                    buffer[i++] = (byte)(state.SlotCardWidthCode & 0xFF);
                    buffer[i++] = (byte)(state.SlotCardWidthCode >> 8);
                    records++;
                }
                if (state.HasDecisionLines && !string.IsNullOrEmpty(state.DecisionLinesText))
                {
                    // DECISION LINES: UTF8 blob of the docked decision row's button labels, one
                    // per '\n'-separated line, capped and truncated on a character boundary.
                    // Written only while a row is really docked (see the record doc — pressable
                    // labels only, never a dialog's card-naming description). Same one-entry
                    // encode cache as the pick banner: the labels are constant for the whole
                    // prompt while the record rides every 5 Hz packet.
                    byte[] text = EncodeDecisionLines(state.DecisionLinesText!);
                    if (text.Length > 0 && i + 2 + text.Length <= buffer.Length)
                    {
                        buffer[i++] = NetProtocol.ExtIdDecisionLines;
                        buffer[i++] = (byte)text.Length;
                        for (int b = 0; b < text.Length; b++)
                            buffer[i++] = text[b];
                        records++;
                    }
                }
                {
                    // CAP LABELS: [mask][per set bit, in mask-bit order: len + UTF8] — what the
                    // sender's CONFIRM cap and docked SKIP button actually read. Written only
                    // while at least one label exists, so an idle packet stays byte-identical.
                    // Each label runs through its own one-entry encode cache (they change on
                    // game-state edges, not per packet).
                    byte[] confirm = state.HasConfirmCapLabel && !string.IsNullOrEmpty(state.ConfirmCapLabel)
                        ? EncodeConfirmCapLabel(state.ConfirmCapLabel!)
                        : System.Array.Empty<byte>();
                    byte[] skip = state.HasSkipCapLabel && !string.IsNullOrEmpty(state.SkipCapLabel)
                        ? EncodeSkipCapLabel(state.SkipCapLabel!)
                        : System.Array.Empty<byte>();
                    int payload = 1 + (confirm.Length > 0 ? 1 + confirm.Length : 0)
                                  + (skip.Length > 0 ? 1 + skip.Length : 0);
                    if ((confirm.Length > 0 || skip.Length > 0) && payload <= 255
                        && i + 2 + payload <= buffer.Length)
                    {
                        buffer[i++] = NetProtocol.ExtIdCapLabels;
                        buffer[i++] = (byte)payload;
                        byte capMask = 0;
                        if (confirm.Length > 0) capMask |= NetProtocol.CapLabelConfirmBit;
                        if (skip.Length > 0) capMask |= NetProtocol.CapLabelSkipBit;
                        buffer[i++] = (byte)(capMask & NetProtocol.CapLabelDefinedMask);
                        if (confirm.Length > 0)
                        {
                            buffer[i++] = (byte)confirm.Length;
                            for (int b = 0; b < confirm.Length; b++)
                                buffer[i++] = confirm[b];
                        }
                        if (skip.Length > 0)
                        {
                            buffer[i++] = (byte)skip.Length;
                            for (int b = 0; b < skip.Length; b++)
                                buffer[i++] = skip[b];
                        }
                        records++;
                    }
                }
                if ((state.HasHalfHover || state.EmptyFanHint)
                    && i + 2 + NetProtocol.HalfHoverRecordBytes <= buffer.Length)
                {
                    // HALF HOVER + SELECTION + EMPTY-FAN HINT (14): [byte0 hover][byte1 state].
                    // Byte 0 is the transient pointer hover — board slot (bits 0..1, the
                    // HalfHoverNoneSlot sentinel when the record rides without one) + top-half bit.
                    // Byte 1 is the persistent CLICK state (one 2-bit none/top/bottom field per
                    // slot) PLUS bit 4, the "Keine Handkarten" placard — a hand-anchored display
                    // that has no board record of its own and that the hand-card count cannot
                    // imply (0 cards is also every idle player). Slot POSITIONS, halves and one
                    // boolean; never a card identity. Written while a half is hovered OR selected
                    // OR the placard is up, so an idle packet stays byte-identical to the previous
                    // build's. Appended in id order behind every existing record.
                    byte half = state.HasHalfHover && state.HalfHoverActive
                        ? (byte)(state.HalfHoverSlot & NetProtocol.HalfHoverSlotMask)
                        : NetProtocol.HalfHoverNoneSlot;
                    if (state.HasHalfHover && state.HalfHoverActive && state.HalfHoverTop)
                        half |= NetProtocol.HalfHoverTopBit;
                    byte select = state.HasHalfHover
                        ? (byte)(NetProtocol.EncodeHalfSelect(state.HalfSelect0)
                                 | NetProtocol.EncodeHalfSelect(state.HalfSelect1)
                                   << NetProtocol.HalfSelectBitsPerSlot)
                        : (byte)0;
                    select &= NetProtocol.HalfSelectDefinedMask;
                    if (state.EmptyFanHint)
                        select |= NetProtocol.HalfEmptyFanHintBit;
                    buffer[i++] = NetProtocol.ExtIdHalfHover;
                    buffer[i++] = (byte)NetProtocol.HalfHoverRecordBytes;
                    buffer[i++] = (byte)(half & NetProtocol.HalfHoverDefinedMask);
                    buffer[i++] = (byte)(select & NetProtocol.HalfSelectByteDefinedMask);
                    records++;
                }
                if (state.HasPileCounts
                    && i + 2 + NetProtocol.PileCountsRecordBytes <= buffer.Length)
                {
                    // PILE COUNTS (15): the numbers the sender's own stack labels display. Written
                    // on every packet while those stacks are shown (the board-UI presence
                    // contract: "present, all zeros" must be distinguishable from "pre-record
                    // sender", whose receiver keeps the legacy model-read counts).
                    buffer[i++] = NetProtocol.ExtIdPileCounts;
                    buffer[i++] = (byte)NetProtocol.PileCountsRecordBytes;
                    buffer[i++] = state.PileDiscardCount;
                    buffer[i++] = state.PileBurntCount;
                    buffer[i++] = state.PileItemsCount;
                    records++;
                }
                if (state.HasTrackHover && state.TrackHoverActorId != 0
                    && i + 2 + NetProtocol.TrackHoverRecordBytes <= buffer.Length)
                {
                    // TRACK HOVER (16): [flags][int32 actorId LE]. The hovered initiative-track
                    // entry by stable CActor.ID (display order is per-client — see the record
                    // doc); the flags byte is masked to the defined bits. Written only while an
                    // entry is hovered; actor id 0 is "none" everywhere and is never emitted.
                    byte thFlags = 0;
                    if (state.TrackHoverPopup)
                        thFlags |= NetProtocol.TrackHoverPopupBit;
                    buffer[i++] = NetProtocol.ExtIdTrackHover;
                    buffer[i++] = (byte)NetProtocol.TrackHoverRecordBytes;
                    buffer[i++] = (byte)(thFlags & NetProtocol.TrackHoverDefinedMask);
                    AvatarSerializer.WriteI32(buffer, ref i, state.TrackHoverActorId);
                    records++;
                }
                if (state.HasWallFades && state.WallFadesKeys != null
                    && state.WallFadesCount > 0)
                {
                    // WALL FADES (17): [count][count × u32 key LE], keys pre-sorted by the
                    // sender. Count is clamped to the cap AND the caller's buffer before a
                    // single byte goes out; an empty set was already excluded above.
                    int n = state.WallFadesCount;
                    if (n > NetProtocol.WallFadesMaxKeys)
                        n = NetProtocol.WallFadesMaxKeys;
                    if (n > state.WallFadesKeys.Length)
                        n = state.WallFadesKeys.Length;
                    if (n > 0 && i + 2 + 1 + 4 * n <= buffer.Length)
                    {
                        buffer[i++] = NetProtocol.ExtIdWallFades;
                        buffer[i++] = (byte)(1 + 4 * n);
                        buffer[i++] = (byte)n;
                        for (int k = 0; k < n; k++)
                            AvatarSerializer.WriteU32(buffer, ref i, state.WallFadesKeys[k]);
                        records++;
                    }
                }
                // CHARACTER FOCUS (22): [flags][int32 focusActorId LE]( [int32 attentionActorId] ).
                // The trailing attention id rides ONLY when the character the game is waiting on is
                // not the one the sender is looking at (the RED state) — otherwise the focus id
                // already names it, so the record keeps the 5 bytes it has always had. The length
                // is therefore computed BEFORE the buffer check, not assumed.
                bool cfAttentionTail = state.CharFocusOwnsAttention
                                       && state.CharFocusAttentionActorId != 0
                                       && state.CharFocusAttentionActorId != state.CharFocusActorId;
                int cfBytes = cfAttentionTail
                    ? NetProtocol.CharFocusMaxRecordBytes
                    : NetProtocol.CharFocusRecordBytes;
                if (state.HasCharFocus && state.CharFocusActorId != 0
                    && i + 2 + cfBytes <= buffer.Length)
                {
                    // Flags bit0 says the sender owns the character THE GAME IS WAITING ON (its
                    // turn, or an open decision it owes) and bit1 announces the tail — the two
                    // facts no receiver can evaluate for itself, because IsUnderMyControl is a
                    // local flag and a remote player's decision panel is hidden on every other
                    // machine (TakeDamagePanel.cs:1133). The ids are stable ActorGuid hashes (the
                    // per-class CActor.ID collides — see NetFigures). The flags byte is masked to
                    // the defined bits; actor id 0 is "none" everywhere and is never emitted, so a
                    // spectating / scenario-less client stays byte-identical to a pre-record sender.
                    byte cfFlags = 0;
                    if (state.CharFocusOwnsAttention)
                        cfFlags |= NetProtocol.CharFocusOwnsAttentionBit;
                    if (cfAttentionTail)
                        cfFlags |= NetProtocol.CharFocusAttentionIdBit;
                    buffer[i++] = NetProtocol.ExtIdCharFocus;
                    buffer[i++] = (byte)cfBytes;
                    buffer[i++] = (byte)(cfFlags & NetProtocol.CharFocusDefinedMask);
                    AvatarSerializer.WriteI32(buffer, ref i, state.CharFocusActorId);
                    if (cfAttentionTail)
                        AvatarSerializer.WriteI32(buffer, ref i, state.CharFocusAttentionActorId);
                    records++;
                }
                if (state.HasTrackSelection && state.TrackSelectionIds != null
                    && state.TrackSelectionCount > 0)
                {
                    // TRACK SELECTION (23): [count][count × int32 actorId LE] — the entries the
                    // sender's OWN initiative track is framing right now (vanilla's
                    // selectionObject, read as an active flag off the live widget). A LIST because
                    // an extra-turn actor can leave a second frame standing (InitiativeTrack.cs:340).
                    // Count is clamped to the cap AND the caller's buffer before a byte goes out;
                    // an empty selection was already excluded above, so an idle packet is
                    // byte-identical to a pre-record sender's.
                    int n = state.TrackSelectionCount;
                    if (n > NetProtocol.TrackSelectionMaxIds)
                        n = NetProtocol.TrackSelectionMaxIds;
                    if (n > state.TrackSelectionIds.Length)
                        n = state.TrackSelectionIds.Length;
                    if (n > 0 && i + 2 + 1 + 4 * n <= buffer.Length)
                    {
                        buffer[i++] = NetProtocol.ExtIdTrackSelection;
                        buffer[i++] = (byte)(1 + 4 * n);
                        buffer[i++] = (byte)n;
                        for (int k = 0; k < n; k++)
                            AvatarSerializer.WriteI32(buffer, ref i, state.TrackSelectionIds[k]);
                        records++;
                    }
                }
                if (state.HasDecisionState)
                {
                    // DECISION STATE (23): [flags][n][n × option byte]. flags bits 0..2 name the
                    // docked prompt, bits 3..5 the prompt-TEXT variant (a NUMBER — the receiver
                    // localizes the line itself; the composed string may never ride this wire, it
                    // can embed active-bonus card names), and each option byte says whether that
                    // option is offered / dimmed / chosen. Options are index-aligned with record
                    // 12's lines and clamped to the record's own cap before a byte goes out. Both
                    // byte kinds are masked to their DEFINED bits so an undefined bit can never be
                    // pre-claimed by garbage. Written on record 12's gate only, so an idle packet
                    // stays byte-identical to the previous build's; appended in id order, last.
                    int payload = DecisionStatePayload(in state);
                    if (payload > 0 && i + 2 + payload <= buffer.Length)
                    {
                        int n = payload - 2;
                        buffer[i++] = NetProtocol.ExtIdDecisionState;
                        buffer[i++] = (byte)payload;
                        buffer[i++] = (byte)(NetProtocol.EncodeDecisionFlags(
                            state.DecisionPromptKind, state.DecisionTextVariant)
                            & NetProtocol.DecisionStateDefinedMask);
                        buffer[i++] = (byte)n;
                        for (int o = 0; o < n; o++)
                            buffer[i++] = (byte)(state.DecisionOptionFlags![o]
                                                 & NetProtocol.DecisionOptionDefinedMask);
                        records++;
                    }
                }
                if (state.HasBoardTuning && state.BoardTuningBytes != null)
                {
                    // BOARD TUNING (28): [n][n × [id][value]] — the SPARSE set of the sender's own
                    // dials that differ from the shipped default for their synced board style. The
                    // payload arrives PRE-ENCODED (built on a config-change edge, not per packet)
                    // so this hot path is a bounded copy; the length is re-clamped against the TLV
                    // ceiling AND the caller's buffer before a byte goes out. An all-default player
                    // never reaches here at all — the tail gate above already excluded them — which
                    // is exactly what keeps an untuned packet byte-identical to the previous build.
                    // Appended LAST, in id order behind every existing record.
                    int payload = state.BoardTuningLength;
                    if (payload > state.BoardTuningBytes.Length)
                        payload = state.BoardTuningBytes.Length;
                    if (payload >= NetProtocol.BoardTuneMinRecordBytes && payload <= 255
                        && i + 2 + payload <= buffer.Length)
                    {
                        buffer[i++] = NetProtocol.ExtIdBoardTuning;
                        buffer[i++] = (byte)payload;
                        for (int b = 0; b < payload; b++)
                            buffer[i++] = state.BoardTuningBytes[b];
                        records++;
                    }
                }
                buffer[countAt] = records;
            }
        }
        return i;
    }

    /// <summary>
    /// Payload size record <see cref="NetProtocol.ExtIdDecisionState"/> would occupy for
    /// <paramref name="state"/> — <c>2 + option count</c>, with the count clamped to the record cap
    /// AND to the caller's buffer length (the sender passes a persistent buffer that may be longer
    /// than the live count). Returns 0 when there is nothing to state at all — no kind, no text
    /// variant, no options — so an empty record can never open the extension tail and an idle
    /// packet stays byte-identical to the previous build's.
    /// </summary>
    private static int DecisionStatePayload(in PresenceState state)
    {
        int n = state.DecisionOptionCount;
        if (n > NetProtocol.DecisionStateMaxOptions)
            n = NetProtocol.DecisionStateMaxOptions;
        if (state.DecisionOptionFlags == null || n < 0)
            n = 0;
        else if (n > state.DecisionOptionFlags.Length)
            n = state.DecisionOptionFlags.Length;
        byte flags = (byte)(NetProtocol.EncodeDecisionFlags(
            state.DecisionPromptKind, state.DecisionTextVariant)
            & NetProtocol.DecisionStateDefinedMask);
        return flags == 0 && n == 0 ? 0 : 2 + n;
    }

    // ---- mod-version text (en/de)coding caches ------------------------------------------
    // The version string is CONSTANT for a given sender, but the record rides every 5 Hz
    // extras packet — a naive Encoding.UTF8 call would allocate per packet on both ends of a
    // serializer whose header promises "allocation-free". One-entry caches fix that: the
    // sender always encodes the same string (one alloc per process), and a receiver decodes
    // a given byte run once and then recognises it (peers on the SAME build — the only case
    // without a mismatch dialog — share one entry; a transient mixed-version lobby costs a
    // few small allocs while the dialog is already on its way up).

    private static string? _encCachedText;
    private static byte[] _encCachedBytes = System.Array.Empty<byte>();

    /// <summary>UTF8-encode a version display string, capped at
    /// <see cref="NetProtocol.ModVersionTextMaxBytes"/> bytes (cap applied on whole chars via
    /// truncation-retry so no split surrogate ships). Cached on the last input.</summary>
    // ---- pick-banner text (en/de)coding caches ------------------------------------------
    // Same one-entry cache discipline as the mod-version text above, and for the same reason:
    // the placard line is CONSTANT for many packets in a row (it only changes when the pick step
    // does), while the record rides every 5 Hz extras packet.

    private static string? _bannerEncText;
    private static byte[] _bannerEncBytes = System.Array.Empty<byte>();
    private static byte[] _bannerDecBytes = System.Array.Empty<byte>();
    private static string _bannerDecText = string.Empty;

    /// <summary>UTF8-encode the pick-status line, capped at
    /// <see cref="NetProtocol.PickBannerTextMaxBytes"/> on a CHARACTER boundary (shortening by
    /// chars, never by bytes, so a multi-byte glyph can never be cut in half).</summary>
    internal static byte[] EncodePickBannerText(string text)
    {
        if (ReferenceEquals(text, _bannerEncText) || text == _bannerEncText)
            return _bannerEncBytes;
        string source = text;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(source);
        while (bytes.Length > NetProtocol.PickBannerTextMaxBytes && source.Length > 0)
        {
            source = source.Substring(0, source.Length - 1);
            bytes = System.Text.Encoding.UTF8.GetBytes(source);
        }
        _bannerEncText = text;   // key on the ORIGINAL string: the caller hands us the same one
        _bannerEncBytes = bytes;
        return bytes;
    }

    /// <summary>Decode a pick-status line off the wire (one-entry cache; never throws).</summary>
    internal static string DecodePickBannerText(byte[] buffer, int offset, int count)
    {
        if (count <= 0)
            return string.Empty;
        if (count == _bannerDecBytes.Length)
        {
            bool same = true;
            for (int b = 0; b < count; b++)
            {
                if (buffer[offset + b] != _bannerDecBytes[b])
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return _bannerDecText;
        }
        var copy = new byte[count];
        System.Buffer.BlockCopy(buffer, offset, copy, 0, count);
        _bannerDecBytes = copy;
        _bannerDecText = System.Text.Encoding.UTF8.GetString(copy);
        return _bannerDecText;
    }

    // ---- board-tuning payload decode cache -----------------------------------------------
    // The tuning payload is CONSTANT for a given sender (it only changes when they move a dial)
    // while the record rides every extras packet, so a fresh copy per packet would be steady
    // garbage on a hot path whose header promises allocation-free. Same one-entry cache
    // discipline as the text codecs: a given byte run is copied once and then recognised, so a
    // stable sender allocates nothing after the first packet.

    private static byte[] _tuneDecBytes = System.Array.Empty<byte>();

    /// <summary>Copy a board-tuning payload off the wire, reusing the last copy when the bytes are
    /// identical (the normal case — nobody edits a config mid-packet). Returns null for an empty
    /// or out-of-range run, which every caller treats as "record absent" ⇒ shipped defaults.</summary>
    internal static byte[]? DecodeBoardTuning(byte[] buffer, int offset, int count)
    {
        if (count <= 0 || offset < 0 || offset + count > buffer.Length)
            return null;
        if (count == _tuneDecBytes.Length)
        {
            bool same = true;
            for (int b = 0; b < count; b++)
            {
                if (buffer[offset + b] != _tuneDecBytes[b])
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return _tuneDecBytes;
        }
        var copy = new byte[count];
        System.Buffer.BlockCopy(buffer, offset, copy, 0, count);
        _tuneDecBytes = copy;
        return copy;
    }

    // ---- board-tooltip text (en/de)coding caches ----------------------------------------
    // Same one-entry cache discipline as the pick-banner text above, for the same reason: a
    // tooltip stays constant for the whole hover (many packets in a row) while the record rides
    // every extras packet, so a naive Encoding.UTF8 call would allocate per packet on both ends.

    private static string? _tooltipEncText;
    private static byte[] _tooltipEncBytes = System.Array.Empty<byte>();
    private static byte[] _tooltipDecBytes = System.Array.Empty<byte>();
    private static string _tooltipDecText = string.Empty;

    /// <summary>UTF8-encode the board-tooltip text, capped at
    /// <see cref="NetProtocol.TooltipTextMaxBytes"/> on a CHARACTER boundary (shortening by
    /// chars, never by bytes, so a multi-byte glyph can never be cut in half).</summary>
    internal static byte[] EncodeBoardTooltipText(string text)
    {
        if (ReferenceEquals(text, _tooltipEncText) || text == _tooltipEncText)
            return _tooltipEncBytes;
        string source = text;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(source);
        while (bytes.Length > NetProtocol.TooltipTextMaxBytes && source.Length > 0)
        {
            source = source.Substring(0, source.Length - 1);
            bytes = System.Text.Encoding.UTF8.GetBytes(source);
        }
        _tooltipEncText = text;  // key on the ORIGINAL string: the caller hands us the same one
        _tooltipEncBytes = bytes;
        return bytes;
    }

    /// <summary>Decode a board-tooltip text off the wire (one-entry cache; never throws).</summary>
    internal static string DecodeBoardTooltipText(byte[] buffer, int offset, int count)
    {
        if (count <= 0)
            return string.Empty;
        if (count == _tooltipDecBytes.Length)
        {
            bool same = true;
            for (int b = 0; b < count; b++)
            {
                if (buffer[offset + b] != _tooltipDecBytes[b])
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return _tooltipDecText;
        }
        var copy = new byte[count];
        System.Buffer.BlockCopy(buffer, offset, copy, 0, count);
        _tooltipDecBytes = copy;
        _tooltipDecText = System.Text.Encoding.UTF8.GetString(copy);
        return _tooltipDecText;
    }

    // ---- capped-UTF8 codec (decision lines + cap labels) --------------------------------
    // The pick-banner / board-tooltip codecs above predate this type and keep their shipped
    // field pairs untouched (wire-test vectors pin their behaviour); the three text records of
    // the decision-mirror round share ONE reusable one-entry cache type instead of a third and
    // fourth copy of the same four fields. Same contract: cap on a CHARACTER boundary (never
    // mid-glyph), one alloc per text change on either end, never throws.

    private sealed class CappedUtf8Codec
    {
        private readonly int _maxBytes;
        private string? _encText;
        private byte[] _encBytes = System.Array.Empty<byte>();
        private byte[] _decBytes = System.Array.Empty<byte>();
        private string _decText = string.Empty;

        public CappedUtf8Codec(int maxBytes) => _maxBytes = maxBytes;

        public byte[] Encode(string text)
        {
            if (ReferenceEquals(text, _encText) || text == _encText)
                return _encBytes;
            string source = text;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(source);
            while (bytes.Length > _maxBytes && source.Length > 0)
            {
                source = source.Substring(0, source.Length - 1);
                bytes = System.Text.Encoding.UTF8.GetBytes(source);
            }
            _encText = text;   // key on the ORIGINAL string: the caller hands us the same one
            _encBytes = bytes;
            return bytes;
        }

        public string Decode(byte[] buffer, int offset, int count)
        {
            if (count <= 0)
                return string.Empty;
            if (count == _decBytes.Length)
            {
                bool same = true;
                for (int b = 0; b < count; b++)
                {
                    if (buffer[offset + b] != _decBytes[b])
                    {
                        same = false;
                        break;
                    }
                }
                if (same)
                    return _decText;
            }
            var copy = new byte[count];
            System.Buffer.BlockCopy(buffer, offset, copy, 0, count);
            _decBytes = copy;
            _decText = System.Text.Encoding.UTF8.GetString(copy);
            return _decText;
        }
    }

    private static readonly CappedUtf8Codec DecisionLinesCodec = new(NetProtocol.DecisionLinesMaxBytes);
    private static readonly CappedUtf8Codec ConfirmLabelCodec = new(NetProtocol.CapLabelMaxBytes);
    private static readonly CappedUtf8Codec SkipLabelCodec = new(NetProtocol.CapLabelMaxBytes);

    /// <summary>UTF8-encode the '\n'-joined decision-button labels, capped at
    /// <see cref="NetProtocol.DecisionLinesMaxBytes"/> on a character boundary.</summary>
    internal static byte[] EncodeDecisionLines(string text) => DecisionLinesCodec.Encode(text);

    /// <summary>UTF8-encode the live CONFIRM cap label, capped at
    /// <see cref="NetProtocol.CapLabelMaxBytes"/> on a character boundary.</summary>
    internal static byte[] EncodeConfirmCapLabel(string text) => ConfirmLabelCodec.Encode(text);

    /// <summary>UTF8-encode the live SKIP label, capped at
    /// <see cref="NetProtocol.CapLabelMaxBytes"/> on a character boundary.</summary>
    internal static byte[] EncodeSkipCapLabel(string text) => SkipLabelCodec.Encode(text);

    internal static byte[] EncodeModVersionText(string text)
    {
        if (ReferenceEquals(text, _encCachedText) || text == _encCachedText)
            return _encCachedBytes;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        while (bytes.Length > NetProtocol.ModVersionTextMaxBytes)
        {
            // Rare path (a runaway string): shorten by chars until the byte cap holds.
            text = text.Substring(0, text.Length - 1);
            bytes = System.Text.Encoding.UTF8.GetBytes(text);
        }
        _encCachedText = text;
        _encCachedBytes = bytes;
        return bytes;
    }

    private static byte[] _decCachedBytes = System.Array.Empty<byte>();
    private static string _decCachedText = string.Empty;

    /// <summary>Decode a version display byte run (cached on the last input; see above).</summary>
    private static string DecodeModVersionText(byte[] buffer, int offset, int count)
    {
        if (count <= 0)
            return string.Empty;
        if (count == _decCachedBytes.Length)
        {
            bool same = true;
            for (int b = 0; b < count; b++)
            {
                if (buffer[offset + b] != _decCachedBytes[b])
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return _decCachedText;
        }
        var copy = new byte[count];
        System.Buffer.BlockCopy(buffer, offset, copy, 0, count);
        _decCachedBytes = copy;
        _decCachedText = System.Text.Encoding.UTF8.GetString(copy);
        return _decCachedText;
    }

    // ---- read ---------------------------------------------------------------------------

    /// <summary>Parse an extras packet. Returns false (and leaves <paramref name="state"/>
    /// defaulted) on any magic/version/type mismatch or truncation — never throws.</summary>
    public static bool TryRead(byte[] buffer, int length, out PresenceState state)
    {
        state = default;
        if (buffer == null || length < 7)
            return false;

        int i = 0;
        if (AvatarSerializer.ReadU32(buffer, ref i) != NetProtocol.Magic) return false;
        if (buffer[i++] != NetProtocol.Version) return false;
        if (buffer[i++] != NetProtocol.MsgExtras) return false;

        byte flags = buffer[i++];
        bool hasBoard = (flags & NetProtocol.FlagHasBoard) != 0;
        bool ghost = (flags & NetProtocol.FlagExtrasGhostHand) != 0;
        state.DominantRight = (flags & NetProtocol.FlagExtrasDominantRight) != 0;
        bool itemFan = (flags & NetProtocol.FlagItemFan) != 0;
        bool cardFx = (flags & NetProtocol.FlagCardFx) != 0;
        bool pileBrowse = (flags & NetProtocol.FlagPileBrowse) != 0;

        // Validate ONLY the bytes our own known flags demand — the contract that keeps this reader
        // working against a future sender that appends yet another block behind ours.
        int need = (hasBoard ? 24 : 0) + 1 + (ghost ? 1 : 0) + (itemFan ? 1 : 0) + (cardFx ? 2 : 0)
                   + (pileBrowse ? 2 : 0);
        if (length < i + need)
            return false;

        if (hasBoard)
        {
            state.HasBoard = true;
            AvatarSerializer.ReadPoseShared(buffer, ref i, out state.Board);
            state.BoardScale = AvatarSerializer.ReadF32(buffer, ref i);
            if (!(state.BoardScale > 0f) || float.IsNaN(state.BoardScale) || float.IsInfinity(state.BoardScale))
                state.BoardScale = 1f;
        }

        state.HandCardCount = buffer[i++];

        // ---- ADDITIVE trailing blocks, read in FLAG-BIT ORDER exactly as written ----
        // (absent flag = the sender has it off OR predates the field; both mean the safe default:
        // solid hands / no item fan / no flight.)
        if (ghost)
        {
            state.GhostHand = true;
            state.GhostStrength = buffer[i++];
        }
        if (itemFan)
        {
            state.HasItemFan = true;
            state.ItemCardCount = buffer[i++];
            state.ItemFanHeld = (flags & NetProtocol.FlagItemFanHeld) != 0;
            state.ItemFanLeftHand = (flags & NetProtocol.FlagItemFanLeft) != 0;
        }
        if (cardFx)
        {
            state.HasCardFx = true;
            state.FxSeq = buffer[i++];
            state.FxEndpoints = buffer[i++];
        }
        if (pileBrowse)
        {
            state.HasPileBrowse = true;
            byte kindFlags = buffer[i++];
            state.PileBrowseKind = (byte)(kindFlags & 0x03);
            state.PileBrowseHeld = (kindFlags & NetProtocol.PileBrowseHeldBit) != 0;
            state.PileBrowseLeftHand = (kindFlags & NetProtocol.PileBrowseLeftBit) != 0;
            state.PileBrowseCardCount = buffer[i++];

            // Control-board style (byte A bits 5..6): pure bit extraction, no length to validate —
            // which is exactly why it was put here rather than behind another trailing byte. Zero
            // means the default board (Oak), whether the sender chose it or predates the field.
            state.BoardStyleCode = NetProtocol.DecodeBoardStyle(kindFlags);

            // Head-mask size: the block's own reserved-bit extension. Its length is validated
            // HERE and not in the `need` sum above, because the bit that demands it lives inside
            // byte A — which we could not read before. Same contract, one level down: we validate
            // exactly what OUR known flags demand and nothing else, so a sender that appends yet
            // another field behind byte C still parses cleanly here.
            if ((kindFlags & NetProtocol.PileBrowseMaskSizeBit) != 0)
            {
                if (length < i + 1)
                    return false;
                state.HasMaskSize = true;
                state.MaskSizeCode = buffer[i++];
            }
            // else: the sender wears the default size OR predates the field — identical rendering.

            // ---- EXTENSION TAIL ------------------------------------------------------------
            // [count] then count x [id][len][payload]. THE SKIP IS THE POINT: a record whose id
            // this build does not know is stepped over by its own length, so a newer peer may add
            // fields without this reader being taught about them and without breaking. Every read
            // is bounds-checked first; a truncated tail abandons the tail and keeps everything
            // parsed above it, because the fields above are complete and independently valid.
            if ((kindFlags & NetProtocol.PileBrowseExtensionBit) != 0 && length >= i + 1)
            {
                int records = buffer[i++];
                for (int r = 0; r < records; r++)
                {
                    if (length < i + 2)
                        break;
                    byte id = buffer[i++];
                    int len = buffer[i++];
                    if (length < i + len)
                        break;

                    if (id == NetProtocol.ExtIdHandScale && len >= 1)
                    {
                        state.HasHandScale = true;
                        state.HandScaleCode = buffer[i];
                    }
                    else if (id == NetProtocol.ExtIdGhostSides && len >= 1)
                    {
                        state.HasGhostSides = true;
                        state.GhostSidesMask = buffer[i];
                    }
                    else if (id == NetProtocol.ExtIdModVersion && len >= 2)
                    {
                        // MOD VERSION: [u16 build LE][UTF8 display bytes]. The display length is
                        // re-clamped on OUR side (never trust the wire) — a hostile/corrupt length
                        // is already bounds-checked above, this only caps what we turn into text.
                        state.HasModVersion = true;
                        state.ModBuild = (ushort)(buffer[i] | (buffer[i + 1] << 8));
                        int textLen = System.Math.Min(len - 2, NetProtocol.ModVersionTextMaxBytes);
                        state.ModVersionText = DecodeModVersionText(buffer, i + 2, textLen);
                    }
                    else if (id == NetProtocol.ExtIdBoardUi && len >= 2)
                    {
                        state.HasBoardUi = true;
                        state.BoardButtonsMask = buffer[i];
                        // Mask to the bits THIS build defines (wanted glow + FOLLOW/PIN + card-slot
                        // occupancy and its validity bit). A future sender's extra overlay bits are
                        // dropped here rather than mis-rendered, which is the same contract that let
                        // this build add the occupancy nibble without the peers that predate it
                        // noticing — they mask it away with their own narrower 0x07.
                        state.BoardOverlayMask = (byte)(buffer[i + 1] & NetProtocol.BoardUiOverlayMask);
                    }
                    else if (id == NetProtocol.ExtIdFanAnchor && len >= 12)
                    {
                        int j = i;
                        float fx = AvatarSerializer.ReadF32(buffer, ref j);
                        float fy = AvatarSerializer.ReadF32(buffer, ref j);
                        float fz = AvatarSerializer.ReadF32(buffer, ref j);
                        // Never let wire garbage place a fan at NaN/∞ — degrade to "record absent"
                        // (the authored default spot) instead.
                        if (!float.IsNaN(fx) && !float.IsInfinity(fx)
                            && !float.IsNaN(fy) && !float.IsInfinity(fy)
                            && !float.IsNaN(fz) && !float.IsInfinity(fz))
                        {
                            state.HasFanAnchor = true;
                            state.FanAnchorLocal = new Vector3(fx, fy, fz);
                        }
                    }
                    else if (id == NetProtocol.ExtIdCardHighlight && len >= 2)
                    {
                        // CARD HIGHLIGHT: two fan-local indices, never a card identity. No
                        // validation beyond the length — the RENDERERS clamp against their own
                        // live card count, which is the only place the bound is actually known
                        // (a packet can legitimately arrive one frame before/after a fan resize).
                        state.HasCardHighlight = true;
                        state.HandHighlightIndex = buffer[i];
                        state.FanHighlightIndex = buffer[i + 1];
                    }
                    else if (id == NetProtocol.ExtIdPickBanner && len >= 1)
                    {
                        // PICK BANNER: UTF8 text. The length is re-clamped on OUR side (never
                        // trust the wire; the record was already bounds-checked above), and a
                        // decode that yields nothing degrades to "record absent" = no placard.
                        int textLen = System.Math.Min(len, NetProtocol.PickBannerTextMaxBytes);
                        string? line = DecodePickBannerText(buffer, i, textLen);
                        if (!string.IsNullOrEmpty(line))
                        {
                            state.HasPickBanner = true;
                            state.PickBannerText = line;
                        }
                    }
                    else if (id == NetProtocol.ExtIdSecondFigure
                             && len >= NetProtocol.SecondFigureRecordBytes)
                    {
                        // SECOND HELD FIGURE: [hand flags][int32 actorId LE][pose 20].
                        //
                        // THREE THINGS ARE VALIDATED HERE, and each of them is a way two minis could
                        // otherwise end up wrong on a peer's screen:
                        //   * the two hand bits must DISAGREE. They name the hand of the second and
                        //     of the first (rig-packet) figure; equal bits mean the packet claims
                        //     both minis are in one palm, which no local grab can produce (a hand
                        //     holds one object) and which a stale/corrupt record can. Dropped.
                        //   * actor id 0 is "none" everywhere in this system, so it can never
                        //     identify a figure.
                        //   * a NaN/infinite position would fling a real board figure out of the
                        //     world — the same guard the fan anchor carries, for the same reason.
                        // A rejected record reads as "no second figure", i.e. exactly what a peer
                        // predating this build renders. Never a half-applied hold.
                        byte hands = (byte)(buffer[i] & NetProtocol.SecondFigureHandMask);
                        bool secondLeft = (hands & NetProtocol.SecondFigureLeftBit) != 0;
                        bool primaryLeft = (hands & NetProtocol.SecondFigurePrimaryLeftBit) != 0;
                        int j = i + 1;
                        int secondId = AvatarSerializer.ReadI32(buffer, ref j);
                        AvatarSerializer.ReadPoseShared(buffer, ref j, out RigPose secondPose);
                        Vector3 sp = secondPose.Position;
                        if (secondLeft != primaryLeft && secondId != 0
                            && !float.IsNaN(sp.x) && !float.IsInfinity(sp.x)
                            && !float.IsNaN(sp.y) && !float.IsInfinity(sp.y)
                            && !float.IsNaN(sp.z) && !float.IsInfinity(sp.z))
                        {
                            state.HasSecondFigure = true;
                            state.SecondFigureActorId = secondId;
                            state.SecondFigurePose = secondPose;
                            state.SecondFigureLeftHand = secondLeft;
                            state.PrimaryFigureLeftHand = primaryLeft;
                        }
                    }
                    else if (id == NetProtocol.ExtIdBoardTooltip && len >= 1)
                    {
                        // BOARD TOOLTIP: UTF8 text. The length is re-clamped on OUR side (never
                        // trust the wire; the record was already bounds-checked above), and a
                        // decode that yields nothing degrades to "record absent" = no tooltip.
                        // The IDENTITY GATE is a sender-side duty (see the record doc) — a
                        // receiver can only render what arrived, so the guarantee that nothing
                        // secret arrives lives entirely in the writer's gate.
                        int textLen = System.Math.Min(len, NetProtocol.TooltipTextMaxBytes);
                        string? tip = DecodeBoardTooltipText(buffer, i, textLen);
                        if (!string.IsNullOrEmpty(tip))
                        {
                            state.HasBoardTooltip = true;
                            state.BoardTooltipText = tip;
                        }
                    }
                    else if (id == NetProtocol.ExtIdHalfHover
                             && len >= NetProtocol.HalfHoverRecordBytes)
                    {
                        // HALF HOVER + SELECTION + EMPTY-FAN HINT: [byte0 hover][byte1 state],
                        // both masked. Byte 0's slot is validated against the board's structural
                        // slot count — a slot the board does not have (a corrupt byte, or a future
                        // board shape this build predates) and the HalfHoverNoneSlot sentinel both
                        // read as "no hover", never as a glow on the wrong recess. Byte 1's
                        // per-slot fields decode through EncodeHalfSelect, so the invalid value
                        // 3 degrades to "none" (never trust the wire). A record whose hover AND
                        // both selections all decode to nothing is dropped whole — identical to
                        // "record absent", which is what the writer emits for that state anyway.
                        //
                        // BIT 4 IS READ INDEPENDENTLY of that drop: the record now also rides for
                        // the "Keine Handkarten" placard ALONE, and that state decodes to no hover
                        // and no selection by construction. Folding it into HasHalfHover would have
                        // meant a placard-only record set a half-hover state nobody is in.
                        byte half = (byte)(buffer[i] & NetProtocol.HalfHoverDefinedMask);
                        int slot = half & NetProtocol.HalfHoverSlotMask;
                        bool hover = slot < NetProtocol.BoardUiSlotCount;
                        byte stateByte = (byte)(buffer[i + 1] & NetProtocol.HalfSelectByteDefinedMask);
                        byte select = (byte)(stateByte & NetProtocol.HalfSelectDefinedMask);
                        byte sel0 = NetProtocol.EncodeHalfSelect(
                            select & NetProtocol.HalfSelectFieldMask);
                        byte sel1 = NetProtocol.EncodeHalfSelect(
                            (select >> NetProtocol.HalfSelectBitsPerSlot)
                            & NetProtocol.HalfSelectFieldMask);
                        if (hover || sel0 != NetProtocol.HalfSelectNone
                                  || sel1 != NetProtocol.HalfSelectNone)
                        {
                            state.HasHalfHover = true;
                            state.HalfHoverActive = hover;
                            state.HalfHoverSlot = hover ? (byte)slot : (byte)0;
                            state.HalfHoverTop = hover && (half & NetProtocol.HalfHoverTopBit) != 0;
                            state.HalfSelect0 = sel0;
                            state.HalfSelect1 = sel1;
                        }
                        state.EmptyFanHint = (stateByte & NetProtocol.HalfEmptyFanHintBit) != 0;
                    }
                    else if (id == NetProtocol.ExtIdBoardTuning
                             && len >= NetProtocol.BoardTuneMinRecordBytes)
                    {
                        // BOARD TUNING: [n][n × [id][value]]. The payload is kept RAW and each
                        // consumer reads the field it needs against its own shipped default
                        // (NetProtocol.BoardTuneVector and friends), which is what makes "field
                        // absent" mean "the value you already have" rather than needing ~49
                        // decoded members here. Validation is therefore structural and happens on
                        // read-out: the walk is bounded by the record's own length, an unknown-width
                        // reserved id stops it, and a payload whose field count is nonsense simply
                        // yields fewer fields — every caller then keeps its default. A record with
                        // a zero field count is dropped (identical to "record absent", which is
                        // what an untuned sender emits anyway).
                        if (buffer[i] > 0)
                        {
                            byte[]? tune = DecodeBoardTuning(buffer, i, len);
                            if (tune != null)
                            {
                                state.HasBoardTuning = true;
                                state.BoardTuningBytes = tune;
                                state.BoardTuningLength = len;
                            }
                        }
                    }
                    else if (id == NetProtocol.ExtIdPileCounts
                             && len >= NetProtocol.PileCountsRecordBytes)
                    {
                        // PILE COUNTS: three plain bytes — nothing further to validate (any value
                        // 0..255 is a drawable count; the renderers clamp their own display).
                        state.HasPileCounts = true;
                        state.PileDiscardCount = buffer[i];
                        state.PileBurntCount = buffer[i + 1];
                        state.PileItemsCount = buffer[i + 2];
                    }
                    else if (id == NetProtocol.ExtIdWallFades
                             && len >= NetProtocol.WallFadesMinRecordBytes)
                    {
                        // WALL FADES: [count][count × u32 key LE]. The count is re-clamped
                        // against the record LENGTH and the cap (never trust the wire); zero
                        // surviving keys degrade to "record absent" — no peer wall fades,
                        // exactly what a pre-record sender produces.
                        int n = buffer[i];
                        int fit = (len - 1) / 4;
                        if (n > fit)
                            n = fit;
                        if (n > NetProtocol.WallFadesMaxKeys)
                            n = NetProtocol.WallFadesMaxKeys;
                        if (n > 0)
                        {
                            var keys = new uint[n];
                            int j = i + 1;
                            for (int k = 0; k < n; k++)
                                keys[k] = AvatarSerializer.ReadU32(buffer, ref j);
                            state.HasWallFades = true;
                            state.WallFadesCount = n;
                            state.WallFadesKeys = keys;
                        }
                    }
                    else if (id == NetProtocol.ExtIdCharFocus
                             && len >= NetProtocol.CharFocusRecordBytes)
                    {
                        // CHARACTER FOCUS: [flags][int32 focusActorId LE]( [int32 attentionActorId] ).
                        // Actor id 0 is "none" everywhere in this system and can never name a
                        // character, so a zero id degrades to "record absent" = no outline, exactly
                        // what pre-record peers render. The flags byte is re-masked to the bits this
                        // build defines, so a newer sender's extra bits can never light a meaning
                        // here.
                        //
                        // THE TRAILING ATTENTION ID is read only when bit 1 demands it AND the
                        // record is really long enough — "validate only what MY flags demand" cuts
                        // both ways, so a truncated tail degrades to the 5-byte meaning (the
                        // character being waited on IS the focus) rather than to a torn read.
                        byte cfFlags = (byte)(buffer[i] & NetProtocol.CharFocusDefinedMask);
                        int j = i + 1;
                        int focusActor = AvatarSerializer.ReadI32(buffer, ref j);
                        if (focusActor != 0)
                        {
                            bool ownsAttention =
                                (cfFlags & NetProtocol.CharFocusOwnsAttentionBit) != 0;
                            int attentionActor = ownsAttention ? focusActor : 0;
                            if (ownsAttention && (cfFlags & NetProtocol.CharFocusAttentionIdBit) != 0
                                && len >= NetProtocol.CharFocusMaxRecordBytes)
                            {
                                int tail = AvatarSerializer.ReadI32(buffer, ref j);
                                if (tail != 0)
                                    attentionActor = tail;
                            }
                            state.HasCharFocus = true;
                            state.CharFocusActorId = focusActor;
                            state.CharFocusOwnsAttention = ownsAttention;
                            state.CharFocusAttentionActorId = attentionActor;
                        }
                    }
                    else if (id == NetProtocol.ExtIdTrackSelection
                             && len >= NetProtocol.TrackSelectionMinRecordBytes)
                    {
                        // TRACK SELECTION: [count][count × int32 actorId LE]. The count is
                        // re-clamped against the record LENGTH and the cap (never trust the wire),
                        // and ids of 0 are dropped — 0 is "none" everywhere in this system and can
                        // never name a track entry. Zero surviving ids degrade to "record absent"
                        // = no selection frame, which is exactly what a pre-record sender produces.
                        int n = buffer[i];
                        int fit = (len - 1) / 4;
                        if (n > fit)
                            n = fit;
                        if (n > NetProtocol.TrackSelectionMaxIds)
                            n = NetProtocol.TrackSelectionMaxIds;
                        if (n > 0)
                        {
                            var ids = new int[n];
                            int j = i + 1;
                            int kept = 0;
                            for (int k = 0; k < n; k++)
                            {
                                int actorId = AvatarSerializer.ReadI32(buffer, ref j);
                                if (actorId != 0)
                                    ids[kept++] = actorId;
                            }
                            if (kept > 0)
                            {
                                state.HasTrackSelection = true;
                                state.TrackSelectionCount = kept;
                                state.TrackSelectionIds = ids;
                            }
                        }
                    }
                    else if (id == NetProtocol.ExtIdTrackHover
                             && len >= NetProtocol.TrackHoverRecordBytes)
                    {
                        // TRACK HOVER: [flags][int32 actorId LE]. Actor id 0 is "none" everywhere
                        // in this system, so it can never name an entry — a zero id degrades to
                        // "record absent" = no hover, exactly what pre-record peers render.
                        byte thFlags = (byte)(buffer[i] & NetProtocol.TrackHoverDefinedMask);
                        int j = i + 1;
                        int hoverActor = AvatarSerializer.ReadI32(buffer, ref j);
                        if (hoverActor != 0)
                        {
                            state.HasTrackHover = true;
                            state.TrackHoverActorId = hoverActor;
                            state.TrackHoverPopup = (thFlags & NetProtocol.TrackHoverPopupBit) != 0;
                        }
                    }
                    else if (id == NetProtocol.ExtIdSecondHeldCard
                             && len >= NetProtocol.SecondHeldCardRecordBytes)
                    {
                        // SECOND HELD CARD: the shared 20-byte pose. The one validation is the
                        // NaN/infinity guard every wire position carries (fan anchor, second
                        // figure): garbage must degrade to "record absent" — the slab the peer
                        // predating this build renders — never to a slab flung out of the world.
                        // No hand byte to validate: the slab is rendered at this absolute pose
                        // and never attached to a hand, so no hand contradiction is expressible
                        // (see the record doc for the receiver evidence).
                        int j = i;
                        AvatarSerializer.ReadPoseShared(buffer, ref j, out RigPose cardPose);
                        Vector3 cp = cardPose.Position;
                        if (!float.IsNaN(cp.x) && !float.IsInfinity(cp.x)
                            && !float.IsNaN(cp.y) && !float.IsInfinity(cp.y)
                            && !float.IsNaN(cp.z) && !float.IsInfinity(cp.z))
                        {
                            state.HasSecondHeldCard = true;
                            state.SecondHeldCardPose = cardPose;
                        }
                    }
                    else if (id == NetProtocol.ExtIdSlotCardSize
                             && len >= NetProtocol.SlotCardSizeRecordBytes)
                    {
                        // SLOT-CARD SIZE: two u16 widths, tenth-mm. Validation = the decoder's own
                        // 5 mm floor (never trust the wire): a garbage code degrades to "record
                        // absent" — the legacy width — rather than collapsing a peer's cards to a
                        // sliver. Both must be sane; a half-valid pair is dropped whole, so the
                        // card and its frame can never disagree about which build sized them.
                        ushort frame = (ushort)(buffer[i] | (buffer[i + 1] << 8));
                        ushort card = (ushort)(buffer[i + 2] | (buffer[i + 3] << 8));
                        if (frame >= NetProtocol.SlotWidthMinCode
                            && card >= NetProtocol.SlotWidthMinCode)
                        {
                            state.HasSlotCardSize = true;
                            state.SlotFrameWidthCode = frame;
                            state.SlotCardWidthCode = card;
                        }
                    }
                    else if (id == NetProtocol.ExtIdDecisionLines && len >= 1)
                    {
                        // DECISION LINES: UTF8 blob, one button label per '\n'-separated line.
                        // The length is re-clamped on OUR side (never trust the wire; the record
                        // was bounds-checked above), and a decode that yields nothing degrades to
                        // "record absent" = no docked decision content.
                        int textLen = System.Math.Min(len, NetProtocol.DecisionLinesMaxBytes);
                        string lines = DecisionLinesCodec.Decode(buffer, i, textLen);
                        if (!string.IsNullOrEmpty(lines))
                        {
                            state.HasDecisionLines = true;
                            state.DecisionLinesText = lines;
                        }
                    }
                    else if (id == NetProtocol.ExtIdDecisionState
                             && len >= NetProtocol.DecisionStateMinRecordBytes)
                    {
                        // DECISION STATE: [flags][n][n × option byte]. The claimed option count is
                        // re-clamped against the record's OWN length AND the cap (never trust the
                        // wire), so a hostile n can neither overrun the record nor bleed into the
                        // next one; the flags byte and every option byte are masked to their
                        // DEFINED bits, so a newer sender's extra bits can never light a meaning
                        // here. A record that survives all of that still delivers only enumerations
                        // and a bitfield — there is no text and no identity in it to leak.
                        byte dsFlags = (byte)(buffer[i] & NetProtocol.DecisionStateDefinedMask);
                        int n = buffer[i + 1];
                        if (n > NetProtocol.DecisionStateMaxOptions)
                            n = NetProtocol.DecisionStateMaxOptions;
                        if (n > len - 2)
                            n = len - 2;
                        if (n < 0)
                            n = 0;
                        state.HasDecisionState = true;
                        state.DecisionPromptKind = NetProtocol.DecodeDecisionKind(dsFlags);
                        state.DecisionTextVariant = NetProtocol.DecodeDecisionTextVariant(dsFlags);
                        state.DecisionOptionCount = n;
                        if (n > 0)
                        {
                            byte[] opts = new byte[n];
                            for (int o = 0; o < n; o++)
                                opts[o] = (byte)(buffer[i + 2 + o]
                                                 & NetProtocol.DecisionOptionDefinedMask);
                            state.DecisionOptionFlags = opts;
                        }
                    }
                    else if (id == NetProtocol.ExtIdCapLabels && len >= 2)
                    {
                        // CAP LABELS: [mask][per set bit, mask-bit order: len + UTF8]. Every
                        // sub-read is bounds-checked against the record's OWN length, so a
                        // hostile length can neither overrun the record nor bleed into the next
                        // one; a malformed block simply delivers nothing (the neutral-label
                        // fallback, the designed failure direction).
                        int j = i;
                        int end = i + len;
                        byte capMask = (byte)(buffer[j++] & NetProtocol.CapLabelDefinedMask);
                        if ((capMask & NetProtocol.CapLabelConfirmBit) != 0 && j < end)
                        {
                            int l = buffer[j++];
                            if (l > 0 && j + l <= end)
                            {
                                string label = ConfirmLabelCodec.Decode(buffer, j,
                                    System.Math.Min(l, NetProtocol.CapLabelMaxBytes));
                                if (!string.IsNullOrEmpty(label))
                                {
                                    state.HasConfirmCapLabel = true;
                                    state.ConfirmCapLabel = label;
                                }
                            }
                            j += l;
                        }
                        if ((capMask & NetProtocol.CapLabelSkipBit) != 0 && j < end)
                        {
                            int l = buffer[j++];
                            if (l > 0 && j + l <= end)
                            {
                                string label = SkipLabelCodec.Decode(buffer, j,
                                    System.Math.Min(l, NetProtocol.CapLabelMaxBytes));
                                if (!string.IsNullOrEmpty(label))
                                {
                                    state.HasSkipCapLabel = true;
                                    state.SkipCapLabel = label;
                                }
                            }
                            j += l;
                        }
                    }
                    i += len; // known or not, the record's own length is how we move past it
                }
            }
        }
        return true;
    }
}
