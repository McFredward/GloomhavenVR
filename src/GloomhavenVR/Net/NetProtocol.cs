namespace GloomhavenVR.Net;

/// <summary>
/// Wire constants for the VR embodiment side-channel (multiplayer VR embodiment epic).
///
/// The mod piggybacks the game's existing Photon-Bolt "side action" event
/// (<c>FFSNet.Synchronizer.SendSideAction</c> → <c>NetworkActionEvent</c>) to carry a
/// purely-cosmetic rig update (head + two hands). Nothing here mutates authoritative
/// game state.
///
/// GRACEFUL DEGRADATION — three independent safety nets so flat / non-modded / vanilla
/// peers are never disturbed (goal 1: no desync, plays fine with flat players):
///   1. <see cref="SentinelTargetPlayerId"/> is stuffed into the side action's
///      TargetPlayerID. Vanilla <c>ActionProcessor.ProcessSideAction</c> only calls
///      <c>GameAction.Execute()</c> when TargetPlayerID == 0 or == my own PlayerID
///      (decompiled ActionProcessor.cs:188); a value no real player can own means every
///      vanilla peer hits the "Ignoring SideAction" branch and NEVER executes → no desync.
///   2. <see cref="SentinelActionTypeId"/> is an out-of-range GameActionType. On a MODDED
///      peer our Harmony prefix on <c>ProcessSideAction</c> recognises it and consumes the
///      packet BEFORE the vanilla body runs (returns false).
///   3. <see cref="Magic"/> + <see cref="Version"/> prefix every payload; the reader
///      bails on mismatch. A stray/foreign packet can never be misparsed.
///
/// The payload rides inside a <c>FFSNet.CustomDataToken</c> — a token class the game
/// already registers with Bolt (decompiled NetworkCallbacks.cs:22), so even vanilla peers
/// deserialize it without error before discarding it (they only carry a <c>byte[]</c>).
/// </summary>
internal static class NetProtocol
{
    /// <summary>Payload magic: ASCII "GVR1" (Gloomhaven VR, wire rev 1). First 4 bytes of every packet.</summary>
    public const uint Magic = 0x47565231u; // 'G' 'V' 'R' '1'

    /// <summary>Wire format version (bump on any layout change; readers reject mismatches).
    /// v2 added the 1-byte head-mask id (0..2) to the fixed header.
    /// v3 inserts a 1-byte MESSAGE TYPE (<see cref="MsgRig"/>/<see cref="MsgExtras"/>) right after
    /// the version, extends the rig packet with an optional held-figure block + a dominant-hand
    /// flag, and adds a second EXTRAS packet (board pose + hand-card count).</summary>
    public const byte Version = 3;

    // ---- message types (byte right after the version) -------------------------------------

    /// <summary>Rig packet (head + two hands, ~15 Hz). Extended in v3 with an optional held-figure
    /// block and a dominant-hand mirror flag. This is the original v2 avatar packet.</summary>
    public const byte MsgRig = 0;

    /// <summary>Extras packet (~5 Hz + on-change): remote control-board world pose + hand-card
    /// count + dominant-hand flag. Purely cosmetic; carries NO card identities.</summary>
    public const byte MsgExtras = 1;

    /// <summary>Extras (board + hand-count) send rate (Hz). Slower than the rig stream — the board
    /// moves rarely and the hand count changes on card play only.</summary>
    public const float ExtrasSendRateHz = 5f;

    /// <summary>
    /// Sentinel GameActionType id carried by our side actions. Deliberately far outside the
    /// real enum range (max real value ~126) so it can never be confused with a game action.
    /// Cast to <c>FFSNet.GameActionType</c> at the call site.
    /// </summary>
    public const int SentinelActionTypeId = 60123;

    /// <summary>
    /// Sentinel TargetPlayerID. No real player owns <see cref="int.MaxValue"/> (real ids are
    /// small connection ids, host == 1), so vanilla peers ignore the action without executing.
    /// </summary>
    public const int SentinelTargetPlayerId = int.MaxValue;

    /// <summary>Local sampling / send rate (Hz). ~15 Hz unreliable, interpolated on the receiver.</summary>
    public const float SendRateHz = 15f;

    /// <summary>Receiver interpolation sharpness (used as <c>1 - exp(-k·dt)</c> lerp factor).</summary>
    public const float InterpolationSharpness = 15f;

    /// <summary>
    /// Drop a remote avatar if no rig packet arrived for this long (seconds). Covers a peer
    /// that stopped sending (switched to flat, minimised, hitched) without a Bolt player-left.
    /// </summary>
    public const float StaleTimeoutSeconds = 3f;

    // ---- payload flag bits ----------------------------------------------------------------

    public const byte FlagHeadValid    = 1 << 0;
    public const byte FlagLeftTracked  = 1 << 1;
    public const byte FlagRightTracked = 1 << 2;
    public const byte FlagHasFingers   = 1 << 3;

    /// <summary>Rig packet: a held-figure block (actorId + world pose) follows the hand poses.</summary>
    public const byte FlagHeldFigure    = 1 << 4;

    /// <summary>Rig / extras packet: the sender's dominant hand is the RIGHT hand (mirror flag so
    /// remote card fans / boards sit on the correct non-dominant side).</summary>
    public const byte FlagDominantRight = 1 << 5;

    /// <summary>Rig packet: a 1-byte HAND STYLE (0 Glove / 1 Plate / 2 Arcane) trails the packet
    /// (after the optional held-figure block). ADDITIVE v3 extension — no version bump: the v3
    /// reader validates only the bytes its known flags demand and ignores both unknown flag bits
    /// and trailing bytes, so peers built before this flag parse the packet unchanged and simply
    /// render the default Glove hands. Readers that DO know the flag get the sender's choice and
    /// clamp it to the shipped style range.</summary>
    public const byte FlagHandStyle = 1 << 6;

    /// <summary>Rig packet: a 20-byte HELD-CARD world pose trails the packet AFTER the
    /// hand-style byte (a single card physically grip-held in a hand — plucked from the fan or
    /// a pile viewer — rendered by peers as one card-BACK slab; no card identity ever rides the
    /// wire). ADDITIVE v3 extension exactly like <see cref="FlagHandStyle"/>: placed after every
    /// field older readers know so they still find the style byte where they expect it, ignore
    /// the unknown flag bit and the trailing bytes, and simply don't show the held card.</summary>
    public const byte FlagHeldCard = 1 << 7;

    // ---- THE RIG FLAG BYTE IS FULL — bits 0..7 are all spent, up to FlagHeldCard above. ----
    //      There is no free rig flag bit. Do not look for one here; there isn't one, and adding
    //      a ninth would move the fixed header and cost a wire-version bump that breaks every
    //      peer in the wild (that is what v2 cost — see Version above).
    //      Room for new fields now lives in the EXTENSION TAIL behind PileBrowseExtensionBit
    //      (extras trailing-block byte A, bit 7), declared at the bottom of this file. It is
    //      type-length-value, so it does not run out and adding a field costs no bit at all.

    // ---- extras (type 1) flag bits --------------------------------------------------------

    /// <summary>Extras packet: a control-board pose (pos+rot+scale) is present.</summary>
    public const byte FlagHasBoard = 1 << 0;

    /// <summary>Extras packet: the sender's dominant hand is the RIGHT hand (mirror of the rig
    /// <see cref="FlagDominantRight"/>, laid out at bit1 in the extras flag byte per the wire spec).</summary>
    public const byte FlagExtrasDominantRight = 1 << 1;

    /// <summary>
    /// Extras packet: the sender has the "ghost hand" active — the hand carrying their open card
    /// fan is faded ([Hands] GhostHandOnFan) — and a 1-byte transparency STRENGTH (0..255 ⇒
    /// 0..1) trails the hand-card count so the receiver fades that hand of the sender's remote
    /// avatar by exactly the amount the sender chose.
    ///
    /// WHY the extras packet and not the rig packet: the rig flag byte is FULL (bits 0..7 are all
    /// taken, up to <see cref="FlagHeldCard"/>) — extending it would cost a wire-version bump and
    /// break every existing peer. The extras packet already carries the fan-related cosmetics
    /// (hand-card count, dominant hand) at 5 Hz, which is plenty for a fade that only changes when
    /// a fan opens or closes. ADDITIVE and backward-compatible exactly like
    /// <see cref="FlagHandStyle"/>: the strength byte is placed AFTER every field older readers
    /// know, and those readers validate only the length their own known flags demand — they ignore
    /// this flag bit and the trailing byte and simply render solid hands.
    /// </summary>
    public const byte FlagExtrasGhostHand = 1 << 2;

    /// <summary>
    /// Extras packet: a 1-byte ITEM-fan card count trails the packet (the sender's equipped-item fan
    /// — <c>Cards.ItemsPile</c> — is open with that many item cards). ADDITIVE extension exactly
    /// like the rig packet's <see cref="FlagHandStyle"/>: appended AFTER every field older readers
    /// know, so a peer built before this flag ignores the unknown bit and the trailing byte and
    /// simply shows no item fan. Absent flag ⇒ no item fan this packet.
    /// </summary>
    public const byte FlagItemFan = 1 << 3;

    /// <summary>Extras packet: the sender's item fan is HAND-HELD (floating above the grabbing
    /// palm) rather than board-anchored above their control board. Meaningful only together with
    /// <see cref="FlagItemFan"/>; costs no payload bytes (pure flag).</summary>
    public const byte FlagItemFanHeld = 1 << 4;

    /// <summary>
    /// Extras packet: a 2-byte CARD-FX event block trails the packet — <c>seq</c> (wrapping
    /// counter) + <c>endpoints</c> (two 4-bit <see cref="CardFxAnchor"/> ids, from in the low
    /// nibble, to in the high nibble). Lets a peer PLAY the card animation locally (a card-back
    /// slab arcing between two anchors) instead of us streaming per-frame transforms: 2 bytes per
    /// animation vs ~20 bytes × 15 Hz for its whole duration. ADDITIVE, appended last; older peers
    /// ignore the bit and the trailing bytes and simply see no flight.
    /// </summary>
    public const byte FlagCardFx = 1 << 5;

    /// <summary>Extras packet: a HAND-HELD item fan (<see cref="FlagItemFanHeld"/>) is held in the
    /// LEFT hand. Pure flag, no payload — without it the receiver would have to guess a hand and
    /// would hang the fan off the wrong arm half the time.</summary>
    public const byte FlagItemFanLeft = 1 << 6;

    /// <summary>
    /// Extras packet: a 2-byte PILE-BROWSE block trails the packet — the sender has one of their
    /// control-board pile browsers open (<c>Cards.PileBrowser</c>: the "Abgelegt" / "Verbrannt"
    /// reading fan) and peers should render the same fan over that player's board or palm.
    ///
    /// WHY THE LAST FLAG BIT PLUS A PAYLOAD BLOCK, and not more flag bits: this is bit 7 — the LAST
    /// free bit in the extras flag byte (bits 0..6 are hasBoard / dominantRight / ghostHand /
    /// itemFan / itemFanHeld / cardFx / itemFanLeft). The state to express is "which of three piles"
    /// plus "how many cards" plus "held in which hand", which is far more than one bit; so the bit
    /// says only PRESENT, and everything else moves into trailing payload bytes where there is no
    /// scarcity. Spending the last bit on a block header rather than on a single boolean is what
    /// keeps the packet extensible: any future extras field can be appended INSIDE this block's
    /// reserved bits instead of needing a wire-version bump.
    ///
    /// LAYOUT (2 bytes, appended LAST because bit 7 is the highest flag bit — see the additive
    /// block contract in <c>PresenceState</c>):
    ///   byte A: bits0..1 pile kind (<see cref="PileBrowseKindDiscard"/> /
    ///           <see cref="PileBrowseKindBurnt"/> / <see cref="PileBrowseKindItems"/>),
    ///           bit2 hand-held (else board-anchored), bit3 held in the LEFT hand,
    ///           bit4 <see cref="PileBrowseMaskSizeBit"/> (a byte C follows), bits5..6 the
    ///           control-board style (<see cref="PileBrowseBoardStyleMask"/>), bit7
    ///           <see cref="PileBrowseExtensionBit"/> — an extension TAIL follows byte C.
    ///           All four "reserved" bits are now claimed, and the last of them was spent on
    ///           unbounded room rather than on a field.
    ///   byte B: card count in the fan (0..255, clamped)
    ///
    /// ADDITIVE and backward-compatible exactly like every block before it: appended behind every
    /// field a pre-existing reader knows, and such a reader validates only the length ITS OWN known
    /// flags demand — so it parses the packet unchanged, ignores this bit and these two bytes, and
    /// simply shows no browse fan. No version bump in either direction.
    /// </summary>
    public const byte FlagPileBrowse = 1 << 7;

    // ---- THE EXTRAS FLAG BYTE IS FULL — bits 0..7 are all spent, up to FlagPileBrowse above. ----
    //      Both flag bytes are now exhausted. If you came here looking for a free bit for a new
    //      extras feature, THERE IS NONE, and the answer is not a version bump: add an
    //      EXTENSION-TAIL record (PileBrowseExtensionBit, trailing-block byte A bit 7, at the
    //      bottom of this file). Bit 7 above was deliberately spent on "a block follows" rather
    //      than on a boolean precisely so this path would exist — head-mask size, control-board
    //      style and now the tail itself have all shipped through it with no version bump.

    /// <summary>Pile-browse block, byte A bits 0..1: the DISCARD ("Abgelegt") pile. Wire constants —
    /// they mirror <c>Cards.PileKind</c>'s member order; append only, never renumber.</summary>
    public const byte PileBrowseKindDiscard = 0;

    /// <summary>Pile-browse block, byte A bits 0..1: the BURNT ("Verbrannt") pile.</summary>
    public const byte PileBrowseKindBurnt = 1;

    /// <summary>Pile-browse block, byte A bits 0..1: the ITEMS ("Gegenstände") pile. Currently the
    /// item fan rides its OWN older flag (<see cref="FlagItemFan"/>, which also carries the held-hand
    /// bits and predates this block), so a sender never emits this value today — the receiver still
    /// understands it, so a later unification of the two fans needs no wire change.</summary>
    public const byte PileBrowseKindItems = 2;

    /// <summary>Pile-browse block, byte A bit 2: the fan is a HAND-HELD reading fan pinned to the
    /// grabbing palm (pinch-grabbed the stack) rather than floating above the sender's board.</summary>
    public const byte PileBrowseHeldBit = 1 << 2;

    /// <summary>Pile-browse block, byte A bit 3: that hand-held fan rides the sender's LEFT hand.</summary>
    public const byte PileBrowseLeftBit = 1 << 3;

    /// <summary>
    /// Trailing-block byte A bit 4 — the FIRST of the four bits that
    /// <see cref="FlagPileBrowse"/> deliberately reserved: a 1-byte HEAD-MASK SIZE (byte C) follows
    /// the block's two bytes. This is the extension path the block header was created for, used
    /// exactly as documented: no new flag bit (both flag bytes are full), no wire-version bump.
    ///
    /// WHY THE MASK SIZE IS TRANSMITTED AT ALL: the project's multiplayer rule is "everything the
    /// user sees, every peer sees the same way". The mask STYLE already rides the rig packet, so a
    /// mask scaled to half size locally but drawn at full size on every other client would be the
    /// same class of disagreement the ghost-hand strength byte exists to prevent.
    ///
    /// WHY THE BLOCK HEADER MAY NOW BE SET WITHOUT A BROWSE FAN: bit 7 was spent on "a block
    /// follows", not on a boolean, precisely so later fields could ride inside it. A size-only
    /// packet therefore emits the block with the pile-browse sub-fields ZEROED (kind 0, not held,
    /// byte B count = 0). Every reader that knows <see cref="FlagPileBrowse"/> — including every
    /// build that ever shipped it — requires <c>count &gt; 0</c> before it renders a browse fan
    /// (RemoteAvatar.SetExtras / RemoteBrowserFan), so a zero-count block reads as "no fan" there
    /// and the trailing byte C is simply ignored along with this unknown bit. Backward compatible
    /// in both directions: an OLD sender sets no bit 4, and absence means the DEFAULT size
    /// (<see cref="MaskSizeDefaultCode"/> ⇒ 1.00×), never a broken one.
    ///
    /// ENCODING (byte C): the size multiplier in HUNDREDTHS — <c>code = round(size × 100)</c>,
    /// clamped to 25..255 ⇒ 0.25×..2.55× in 0.01 steps. A byte is plenty for a cosmetic scale
    /// (0.01 is far below what the eye resolves on a head-sized object) and hundredths make the
    /// value readable as-is in a hardware log. The size is sent ONLY when it differs from
    /// <see cref="MaskSizeDefaultCode"/>, so a default-size player's packet stays byte-identical
    /// to what previous builds emitted.
    /// </summary>
    public const byte PileBrowseMaskSizeBit = 1 << 4;

    /// <summary>Wire code of the DEFAULT head-mask size (1.00× ⇒ 100 hundredths). A packet without
    /// <see cref="PileBrowseMaskSizeBit"/> means exactly this value.</summary>
    public const byte MaskSizeDefaultCode = 100;

    /// <summary>Smallest / largest transmittable head-mask multiplier (the byte's 25..255 range in
    /// hundredths). The config entry's AcceptableValueRange stays inside this window, so a config
    /// value can never be clipped by the wire.</summary>
    public const float MaskSizeMin = 0.25f;
    public const float MaskSizeMax = 2.55f;

    /// <summary>Quantize a head-mask size multiplier to its wire byte (hundredths, clamped).</summary>
    public static byte EncodeMaskSize(float size)
    {
        if (float.IsNaN(size) || float.IsInfinity(size))
            return MaskSizeDefaultCode;
        return (byte)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(size * 100f), 25, 255);
    }

    /// <summary>Decode a head-mask size byte back to a multiplier (hundredths). A zero/garbage code
    /// degrades to the default rather than collapsing a peer's head to nothing.</summary>
    public static float DecodeMaskSize(byte code) =>
        code < 25 ? MaskSizeDefaultCode / 100f : code / 100f;

    /// <summary>
    /// Trailing-block byte A bits 5..6 — the SECOND user of the reserved room
    /// <see cref="FlagPileBrowse"/> was spent to create: the sender's chosen CONTROL-BOARD STYLE
    /// (<c>Cards.ControlBoard</c> — 0 Oak / 1 Steel / 2 Bronze), carried IN the existing byte with
    /// NO extra byte at all. Bit 7 stays reserved for whatever comes after this.
    ///
    /// WHY IT IS TRANSMITTED: the board style became a normal user-facing choice (VR settings →
    /// Avatar → Kontrollbrett, next to the head mask and the hand style), and the project's
    /// standing multiplayer rule is that what one player sees, every player sees the same way. The
    /// mask style, mask size, hand style and ghost strength all ride the wire for exactly this
    /// reason; a peer whose board is bronze on their own screen and default-dark on everyone
    /// else's would be the same disagreement.
    ///
    /// WHY NO PRESENCE BIT AND NO EXTRA BYTE: code 0 is the DEFAULT board (Oak), so "absent" and
    /// "Oak" render identically — the same argument that lets the mask size be omitted at 1.00×.
    /// A sender only sets the block header for the style when the style is NON-default, so a
    /// default-board player's packet stays byte-identical to what previous builds emitted, and an
    /// old sender (which always writes these bits as 0) reads as Oak, never as garbage. Two bits
    /// hold four boards; a FIFTH board would need a trailing byte behind byte C, appended under the
    /// same additive contract.
    ///
    /// BACKWARD COMPATIBLE BOTH WAYS: the packet LENGTH does not change, so a pre-existing reader
    /// parses it exactly as before and simply ignores these two unknown bits (it masks byte A with
    /// 0x03 for the pile kind and tests bits 2/3/4 individually — none of them is touched here).
    /// No new flag bit, no wire-version bump.
    /// </summary>
    public const int PileBrowseBoardStyleShift = 5;

    /// <summary>Mask of the board-style field inside trailing-block byte A (bits 5..6).</summary>
    public const byte PileBrowseBoardStyleMask = 0x60;

    /// <summary>Wire code of the DEFAULT control board (Oak). A packet whose byte-A style bits are
    /// zero — including every packet from a peer that predates the field — means exactly this.</summary>
    public const byte BoardStyleDefaultCode = 0;

    /// <summary>Largest board id the two reserved bits can carry (four boards, 0..3).</summary>
    public const byte BoardStyleMaxCode = 3;

    /// <summary>Quantize a <c>Cards.ControlBoard</c> id to its 2-bit wire code (clamped, so a
    /// hypothetical fifth board degrades to a drawable one instead of corrupting byte A).</summary>
    public static byte EncodeBoardStyle(int board) =>
        (byte)UnityEngine.Mathf.Clamp(board, BoardStyleDefaultCode, BoardStyleMaxCode);

    /// <summary>Extract the board-style code from trailing-block byte A (0 = default/older peer).</summary>
    public static byte DecodeBoardStyle(byte kindFlags) =>
        (byte)((kindFlags & PileBrowseBoardStyleMask) >> PileBrowseBoardStyleShift);

    /// <summary>
    /// Trailing-block byte A, bit 7 — AN EXTENSION TAIL FOLLOWS.
    ///
    /// <para>This was the last free bit in the protocol, and it is spent the way its own
    /// reservation note said to spend it: on "a further sub-block follows", not on a boolean.
    /// Spending it on one field would have ended the protocol's extensibility and made the next
    /// feature a version bump plus a coordinated release of every peer.</para>
    ///
    /// <para>THE TAIL IS TYPE-LENGTH-VALUE, which is what makes it unbounded: one count byte, then
    /// that many records of <c>[id][len][len bytes]</c>. A reader parses the ids it knows and SKIPS
    /// the rest by their length — so a newer sender may add fields freely and an older reader
    /// neither breaks nor has to be told. That is the property the fixed bits never had: every
    /// previous field cost a bit nobody could reclaim.</para>
    ///
    /// <para>Rules for adding one: pick the next free <c>ExtId*</c>, never renumber an existing id,
    /// and only write the record when the value differs from what a peer would assume in its
    /// absence — an untuned player's packet then stays byte-identical to the previous build's.</para>
    /// </summary>
    public const byte PileBrowseExtensionBit = 1 << 7;

    /// <summary>Extension record id: the sender's per-style HAND SCALE (hundredths, 1 byte).
    /// Their hands must read the same size to everyone, exactly like their hand style.</summary>
    public const byte ExtIdHandScale = 1;

    /// <summary>
    /// Extension record: WHICH hands are ghosted (bit0 left, bit1 right). Needed the moment a
    /// HELD CARD may ghost a hand: the legacy flag carries no side, and receivers inferred "the
    /// non-dominant hand" — always right for the fan, wrong half the time for a held card, and
    /// unable to say "both". Pre-extension peers skip the record by length and keep the old
    /// inference; that is a cosmetic cross-version mismatch, not a desync.
    /// </summary>
    public const byte ExtIdGhostSides = 2;
    public const byte GhostSideLeftBit = 0x01;
    public const byte GhostSideRightBit = 0x02;

    /// <summary>
    /// Extension record id: the sender's MOD VERSION — <c>[u16 ModBuild LE][UTF8 display string]</c>
    /// (string capped at <see cref="ModVersionTextMaxBytes"/> bytes). Unlike every record before
    /// it this one is written UNCONDITIONALLY on every extras packet: its ABSENCE is itself the
    /// signal ("this modded peer predates the version handshake" ⇒ treated as ModBuild 0 ⇒
    /// mismatch), so there is no default value whose omission could keep the packet smaller.
    /// </summary>
    public const byte ExtIdModVersion = 3;

    // =====================================================================================
    //  MOD BUILD NUMBER — THE VERSION-HANDSHAKE COMPARISON KEY. READ THIS BEFORE SHIPPING.
    // =====================================================================================
    //  BUMP THIS BY +1 ON EVERY BUILD THAT IS HANDED TO ANOTHER PLAYER. This is the number
    //  the whole multiplayer version handshake compares: two peers whose ModBuild differ get
    //  the blocking "version mismatch" dialog (VersionGuard) and one of them drops to flat-net
    //  mode or leaves. The DISPLAY string next to it (MyPluginInfo.PLUGIN_VERSION, stamped in
    //  by the driver — never referenced here, this file is compiled into the wire tests
    //  without BepInEx) is for humans only and takes no part in the comparison.
    //
    //  Rules:
    //   * monotonic, never reused, never reset — +1 per distributed build, nothing fancier;
    //   * peers whose packets carry NO version record read as ModBuild 0 (pre-handshake wild
    //     builds) and therefore always mismatch — that is deliberate;
    //   * this is NOT the wire Version byte above: the wire format is unchanged (still v3,
    //     the record is an additive TLV that old readers skip by length).
    // =====================================================================================
    /// <summary>Monotonic mod build number, the version-handshake comparison key (see the
    /// block comment above — bump by +1 on every build handed to another player).
    /// Build 2: remote-board 1:1 parity round (board-UI record 4, fan-anchor record 5,
    /// 15 Hz board pose while moving).</summary>
    public const ushort ModBuild = 43;
    // Build 36: per-pile fan spread/radius (items, discard, burnt) in the debug menu; the ITEM fan
    // finally feeds the card-highlight record so peers see a lifted chip; the duplicate hand<->slot
    // card flights peers were replaying are gone. Local + existing wire record — no format change.
    // Build 35: origin-guard REGRESSION fix — it fired on the menu→scenario rig rebuild and
    // dragged the fresh rig 21 m to put the head at the menu's world origin; plus configurable
    // min/max APPARENT board size, enforced every frame. Local-only — no wire changes.
    // Build 34: shipped defaults re-based onto the user's tuned cfg (17 values — board layout,
    // Steel pitch window/scale, decision dock offset/size/gap, pick-banner offset, table scale,
    // default mask). Values only — no behaviour and no wire change.
    // Build 33: a burned card now stays on the board until its own burn artwork has played and
    // only THEN flies to the pile (the pile-count watch used to fly it first, so the artwork
    // replayed on the board afterwards); hover tooltips are laid on ANY floated window, not only
    // full-screen menus, so they stop cutting through it at the board's angle. Local-only.
    // Build 32: the control board's FIRST placement of a scenario is a fixed spot beside the head
    // on the left (not the last drag's saved layout), and the GAME's own card particles are pinned
    // off through its low-spec switch — the end-of-turn card sweep was spraying sparks across the
    // whole play field. Local-only — no wire changes.
    // Build 31: single player uses the spawn ring too (no more spawning ON the board); only the
    // ACTIVE hand carries a laser; the decision dock is revealed at its final size and place
    // instead of popping; an item fan with 0 items refuses to open; the card dust/spark burst is
    // off by default. Local-only — no wire changes.
    // Build 30: a runtime re-origin (HMD doff/don) no longer moves the player — the rig is shifted
    // back so the head lands where it was; option names never end in "…" any more (overflow +
    // shrink instead of clip, and all 362 names shortened to fit); the decision text↔button gap is
    // its own per-board setting in METRES, so resizing or moving the dock cannot change it.
    // Build 29: control board never moves or resizes on its own (the lost-board watchdog and the
    // two distance-based re-seats are gone; a two-handed resize now round-trips exactly), the
    // pick-status placard and the hover hints are position-tunable, and the placard rides the wire
    // as ADDITIVE extension record 7 so a peer's remote board shows it at the same seat.
    // Build 28: wall dressing round 3 — skinned meshes (hanging cloth and its hardware) can ride
    // a fade, the size cap needs TWO fat axes so long-thin dressing is not rejected, and every
    // structural skip (wrong renderer family / already owned / fade-capable) is now logged when
    // it stands airborne near a wall. Local-only rendering — no wire changes.
    // Build 27: wall dressing round 2 — props now DISSOLVE with their wall instead of popping at
    // the end (alpha/cutoff ramp + particle emission/size/start-colour ramp on the same fade
    // curve), particle props are judged by their EMITTER instead of their drifting live bounds,
    // and ownership is sticky while faded (that pair was the "candles blink" bug).
    // Local-only rendering — no wire changes.
    // Build 26: wall-mounted dressing (torch/candle flames left floating when a wall faded) now
    // rides its wall's fade — airborne renderers hugging the wall slab are hidden via
    // renderer.enabled ONLY; Lights are never touched, so the lighting is unchanged.
    // Local-only rendering — no wire changes.
    // Build 25: the left-edge flash on opening a modal — the game's own 2D window was rendered
    // by the head camera for one frame before conversion; its canvases are now blacked out from
    // inside the game's Show() until Convert takes over. Local-only UI — no wire changes.
    // Build 24: cold-open menu ROOT CAUSE — the game re-drives the converted target after
    // Convert pins it (localScale 0.14, rect height 1080→2040); the frame is now maintained,
    // the fit uses only the LIVE union, and the design-height cap applies to the target rect.
    // Local-only UI — no wire changes.
    // Build 23: cold-open menu round 6 — the window was FROZEN mid show-animation (15% scale),
    // so no rect could be right: land the animation, then fit to a re-measured fixed point.
    // Reveal flip moved to LateUpdate and the MR backing plate gap converted to real metres
    // (one-eye flicker class). Local-only UI — no wire changes.
    // Build 22: cold-open menu — the authored union is now clamped to the same canvas design
    // height Convert clamps the host to (open #1 fitted 407x2040 vs 412x1080 warm: a host twice
    // as tall as its content). Local-only UI — no wire changes.
    // Build 21: MP round 2 — join seat actually runs (session flag + peer poses), remote board
    // pin state/dock heights/lit round plate/overlay offsets/eased scale/synced card highlight
    // (additive records: board-UI pinned bit, ExtIdCardHighlight 6), pile+item fans sweep like
    // the hand fan, cold-menu fit ignores the show animation, name tag centred on its ink.
    // Build 20: MP round — remote board mirrors the REAL initiative track + objectives and
    // seats every dock at the owner's own mount offsets, Steam picture on both roles, join
    // seat on a ring at the widest gap to peers, item fan usable under MP roster windows
    // (whole-fan grab removed). No wire changes (Version stays 3, zero new records).
    // Build 19: fingertip tile touch actually reachable (ForceFarMode retired — tuned cfgs
    // shipped it dead), pile fan no longer sweeps the board's played cards, self-verifying
    // one-shot menu fit (cold-open tiny/misplaced menu), VR burnt-card + named-card tutorial
    // texts. Local-only rendering/input/UI — no wire changes.
    // Build 18: tutorial grab step is sequential (scripted chain held until a figure was
    // picked up), floated windows re-placed at their final fitted geometry, cold-open menu
    // fit fixed (layout flush order). Local-only UI — no wire changes.
    // Build 17: complete pre-reveal window hide (grab bar/depth masks/MR plate), grip-gated
    // fingertip tile touch, figure-grab intent peek + extra tutorial step. Local-only
    // rendering/input — no wire changes, same-build enforcement.
    // Build 16: settings window exempt from the game's tutorial-wide InteractabilityManager
    // click veto (root cause of dead tabs, proven) + floated windows reveal only at their
    // final pose/scale (no more spawn jump). Local-only UI — no wire changes.
    // Build 15: static batching REMOVED entirely (root cause of invisible revealed rooms —
    // Apparance clones of batched sources are born without material slots; user ruling),
    // no mod X on tutorial windows, settings menu never input-blocked (laser fall-through).
    // Local-rendering/UI only — no wire changes.
    // Build 14: MaterialLoaderHeal round 8 — heal foreign-disabled tile renderers too
    // (materials assigned, disabled by another system; stale static-batch state cleared),
    // census order fix + staticBatch/mat0 forensics. Local-rendering only.
    // Build 13: MaterialLoaderHeal round 7 — Harmony registry at the source (FindObjectsOfType
    // is blind to Apparance's HideAndDontSave containers; loaders now enroll themselves in
    // the LoadMaterials postfix), deep includeInactive tile-scan seed, loader-topology
    // forensics in the census. Local-rendering only — no wire changes.
    // Build 12: MaterialLoaderHeal round 6 — scene-wide includeInactive loader scan (the
    // per-tile downward scan provably missed the stuck floor loaders), done-stuck healed
    // IMMEDIATELY (no observation delay), forensic strand line. Local-rendering only.
    // Build 11: MaterialLoaderHeal round 5 — done-stuck heal path unblocked (per-state
    // gate + materials-already-assigned foreign-disable discriminator + door-prop skip).
    // Local-rendering only — no wire changes, same-build enforcement.
    // Build 10: doorways never fade (door-state gating + hard-hide removed, user ruling) +
    // MaterialLoaderHeal watchdog for stranded reveal-time material loads. Local-rendering
    // only — no wire changes, same-build enforcement.
    // Build 9: Apparance gaze/busy-tile synthesis focus (reveal-room detail) + per-child
    // maptile diagnostics. Local-rendering only — no wire changes, same-build enforcement.
    // Build 4: tutorial VR bridge + MR readability + settings overhaul + fan press fix +
    // per-pixel panel depth. No wire changes — same-build enforcement.
    // Build 3: item-flow round (deciding-actor hand, place-to-use split, fan occlusion,
    // use-bars fit hold + row spacing, hand-switch watchdog, per-board pitch window).
    // No wire changes — bumped anyway: the handshake's job is same-BUILD enforcement.

    /// <summary>
    /// Extension record id: the sender's live BOARD-UI STATE — 2 bytes,
    /// <c>[byte0 buttons][byte1 overlays]</c>. This is what makes a peer's copy of a control
    /// board show EXACTLY the controls its owner currently sees (user requirement: the remote
    /// board used to draw ALL buttons permanently), plus the "wanted slot" glow state so the
    /// teal blink is synced (the blink ANIMATION stays local-clock driven at the shared period —
    /// synced state, locally animated, zero per-frame traffic).
    ///
    /// byte 0 (buttons — 1 = that control is VISIBLE on the owner's board right now):
    ///   bit0 CONFIRM keycap        bit1 UNDO keycap
    ///   bit2 item-use RECESS       bit3 item-use USE cap
    ///   bit4 SHORT-rest disc       bit5 LONG-rest disc
    ///   bit6 turn-flow SKIP disc   bit7 decision drawer OCCUPIED (a prompt is docked)
    /// byte 1 (overlays):
    ///   bits0..1 the wanted-slot glow mask (bit0 = left slot, bit1 = right slot — the exact
    ///            mask the owner's PlayTray.SetWantedSlots currently shows);
    ///   bit2     <see cref="BoardUiPinnedBit"/> — the owner's board is PINNED (world-anchored)
    ///            rather than FOLLOWing their rig;
    ///   bits3..4 <see cref="BoardUiSlotMask"/> — the owner's live CARD-SLOT OCCUPANCY
    ///            (bit3 = left slot holds a card, bit4 = right slot holds a card);
    ///   bit5     <see cref="BoardUiSlotsValidBit"/> — the sender KNOWS its slot occupancy, i.e.
    ///            bits 3..4 are state and not "a sender that predates the field";
    ///   bits6..7 reserved (written 0, ignored on read).
    ///
    /// Unlike the "only when non-default" records, this one is written on EVERY extras packet
    /// that also carries a board pose: the receiver must distinguish "the owner's board shows
    /// no dynamic controls" (record present, byte0 = 0) from "the sender predates the field"
    /// (record absent → the receiver keeps the legacy always-drawn furniture, so a build-1 peer
    /// looks exactly as before). ~4 bytes at 5 Hz. No card identity is derivable from any bit —
    /// the wanted mask reveals only "slot still empty during selection" and the occupancy nibble
    /// only its complement, which the board's own card backs (and vanilla's ready tracker) already
    /// show.
    /// </summary>
    public const byte ExtIdBoardUi = 4;

    // Board-UI record byte 0 (buttons) bit assignments — wire constants, append-only.
    public const byte BoardUiConfirmBit = 1 << 0;
    public const byte BoardUiUndoBit = 1 << 1;
    public const byte BoardUiItemRecessBit = 1 << 2;
    public const byte BoardUiItemUseCapBit = 1 << 3;
    public const byte BoardUiShortRestBit = 1 << 4;
    public const byte BoardUiLongRestBit = 1 << 5;
    public const byte BoardUiSkipBit = 1 << 6;
    public const byte BoardUiDecisionBit = 1 << 7;

    /// <summary>Board-UI record byte 1: mask of the wanted-slot glow bits (bits 0..1).</summary>
    public const byte BoardUiWantedMask = 0x03;

    /// <summary>
    /// Board-UI record byte 1, bit 2 — the owner's control board is PINNED (world-anchored,
    /// <c>[Cards] TrayFollow == false</c>) rather than FOLLOWing their rig. It is the state of the
    /// FOLLOW/PIN keycap on their board: label "PINNED"/"FIXIERT" + the accented brass cap when
    /// set, label "FOLLOW"/"FOLGEN" + the parchment idle cap when clear.
    ///
    /// WHY IT IS ON THE WIRE AT ALL, having been declared DELIBERATELY-NOT before: the earlier note
    /// called the toggle "a private VR preference", so peers drew it in one fixed look. But it is a
    /// LABELLED, TWO-STATE control on a board the user requires to read 1:1 like its owner's — a
    /// board reading "FOLGEN" on every peer's screen while its owner's reads "FIXIERT" is exactly
    /// the class of disagreement the wanted-glow and button-visibility bits were added to end.
    ///
    /// WHY BIT 2 MEANS *PINNED* AND NOT *FOLLOW*: a sender that predates this bit writes byte 1
    /// with the reserved bits zeroed, so 0 must be the state those senders were already drawn in —
    /// which is FOLLOW (the un-accented default look every previous build rendered). Cross-version
    /// compatible in both directions, with no presence flag and no extra byte, exactly like the
    /// board-style bits.
    /// </summary>
    public const byte BoardUiPinnedBit = 1 << 2;

    /// <summary>
    /// How many CARD SLOTS a control board has — TWO, and that is a structural fact of the board,
    /// not a tunable: <c>PlayTray</c> allocates exactly two slot anchors and two occupant refs
    /// (<c>PlayTray.1.Core.cs</c>: <c>_slots = new Transform?[2]</c> / <c>_occupants = new VRCard?[2]</c>,
    /// bound to the prefab recesses named "Slot1" and "Slot2"), every slot loop in the Cards layer
    /// is <c>for (i = 0; i &lt; 2; i++)</c>, and the remote mirror draws exactly two
    /// (<c>RemoteControlBoard._cards</c>). The wire therefore spends exactly two bits. If the board
    /// ever grew a third recess this constant, <see cref="BoardUiSlotMask"/> and
    /// <see cref="BoardUiOverlayMask"/> all widen together, in that order — and an older peer would
    /// keep seeing the first two slots, which is the whole point of masking on read.
    /// </summary>
    public const int BoardUiSlotCount = 2;

    /// <summary>Bit position of slot 0 inside the board-UI record's byte 1. The occupancy nibble is
    /// written as <c>(occupancy &amp; ((1 &lt;&lt; BoardUiSlotCount) - 1)) &lt;&lt; BoardUiSlotShift</c>,
    /// so the slot COUNT — not a hand-written pair of bit names — is what fixes the layout.</summary>
    public const int BoardUiSlotShift = 3;

    /// <summary>
    /// Board-UI record byte 1, bit 3 — the owner's LEFT card slot (Slot1) currently holds a card.
    ///
    /// WHY IT IS ON THE WIRE (user report, hardware MP test: "Ich will auch sehen wenn eine Karte
    /// abgelegt wurde auf dem controllboard (mit der rueckseite)"). A peer's board slots used to be
    /// drawn PURELY from the host-replicated model (<c>CCharacterClass.RoundAbilityCards</c>), so a
    /// slot could only show something once the GAME had replicated the selection. Everything the
    /// player does physically before that — laying a card into a recess, taking it back out again,
    /// docking the round cards for their own turn, laying a burn/discard candidate into a recess —
    /// is a VR-ONLY fact that exists nowhere in the game model, and therefore existed on nobody
    /// else's screen. Two bits make the remote board's slots agree with the owner's at all times.
    ///
    /// WHY IT LEAKS NOTHING. It is a POSITION, not an identity: "a card lies here" and nothing
    /// more, which is exactly what the receiver renders (a card BACK — fronts stay strictly behind
    /// <see cref="RevealGate"/>, unchanged). Vanilla already broadcasts the same fact twice over
    /// during the only phase where it could matter: the multiplayer ready tracker
    /// (<c>UIScenarioMultiplayerController.ShowReadyTracker</c>) and the hand tabs' live
    /// "selected/2" count (<c>CardsHandManager.OnSelectedCardsNumberChanged</c>) both show, with no
    /// owner gate, how far each player has got. The complement of this mask is also the
    /// wanted-slot glow that already rides bits 0..1.
    ///
    /// WHY THE NIBBLE NEEDS A VALIDITY BIT AND THE PINNED BIT DID NOT. "PINNED" could get away
    /// with "0 = the look old builds already drew", because FOLLOW genuinely was that look. Slot
    /// occupancy has no such lucky default: 0 has to mean BOTH SLOTS EMPTY (that is half the user's
    /// requirement — "wo aktuell eine Karte liegt UND WO NICHT"), and a sender that predates the
    /// field also writes 0 while its board may well have two cards on it. Rendering those two
    /// cases the same would wipe an old peer's round cards off this client's screen — a hard
    /// cross-version regression. <see cref="BoardUiSlotsValidBit"/> separates them: set = "these
    /// two bits are state", clear = "this sender knows nothing about its slots", and the receiver
    /// then falls back to the model-only rendering it has always done. Symmetrically, an old READER
    /// masks byte 1 down to its own <see cref="BoardUiWantedMask"/> / its own narrower
    /// <see cref="BoardUiOverlayMask"/> (0x07 before this build) and never sees any of these bits.
    /// Additive in both directions, no length change, no new record, wire
    /// <see cref="Version"/> untouched.
    /// </summary>
    public const byte BoardUiSlot0Bit = 1 << BoardUiSlotShift;

    /// <summary>Board-UI record byte 1, bit 4 — the owner's RIGHT card slot (Slot2) currently holds
    /// a card. Same contract as <see cref="BoardUiSlot0Bit"/>.</summary>
    public const byte BoardUiSlot1Bit = 1 << (BoardUiSlotShift + 1);

    /// <summary>The card-slot OCCUPANCY nibble of the board-UI record's byte 1 (bits 3..4 —
    /// <see cref="BoardUiSlotCount"/> bits at <see cref="BoardUiSlotShift"/>). Shift it back down by
    /// <see cref="BoardUiSlotShift"/> to get a plain slot-indexed mask. Meaningful ONLY together
    /// with <see cref="BoardUiSlotsValidBit"/>.</summary>
    public const byte BoardUiSlotMask =
        (byte)(((1 << BoardUiSlotCount) - 1) << BoardUiSlotShift);

    /// <summary>
    /// Board-UI record byte 1, bit 5 — the sender KNOWS its own card-slot occupancy, i.e. bits 3..4
    /// carry state rather than "this build had no such field". Set on every board-UI record a
    /// build with slot occupancy writes, INCLUDING when both slots are empty; clear only for a
    /// sender that predates the nibble. See <see cref="BoardUiSlot0Bit"/> for why the nibble cannot
    /// use the "0 is the old look" trick the FOLLOW/PIN bit used.
    /// </summary>
    public const byte BoardUiSlotsValidBit = 1 << (BoardUiSlotShift + BoardUiSlotCount);

    /// <summary>Every DEFINED bit of the board-UI record's byte 1 (wanted glow + pinned + slot
    /// occupancy + its validity bit). The writer masks with this so undefined bits can never be
    /// pre-claimed by garbage, and the reader masks again (never trust the wire). Widening it is how
    /// the next overlay bit ships — and it is why old readers, which mask with the narrower
    /// <see cref="BoardUiWantedMask"/> (or with this constant's previous 0x07 value), ignore the new
    /// bits instead of mis-reading them.</summary>
    public const byte BoardUiOverlayMask =
        (byte)(BoardUiWantedMask | BoardUiPinnedBit | BoardUiSlotMask | BoardUiSlotsValidBit);

    /// <summary>
    /// Extension record id: the board-local ANCHOR POSITION of the sender's open BOARD-ANCHORED
    /// fan (item fan or pile-browse fan — at most one is ever open, the Cards layer enforces the
    /// mutual exclusion) — 12 bytes, 3 × float32 LE, in the sender's control-board LOCAL frame
    /// (same axes the slot/pile anchors use; the receiver applies it as
    /// <c>boardPos + boardRot · (local × boardScale)</c>).
    ///
    /// WHY: the receivers used to place both fans at the AUTHORED default spot above the board
    /// (BoardTopLocalY + 0.26), but the owner's own fan sits at that base PLUS their per-board
    /// <c>[Cards] BrowseFanOffset / ItemCardOffset</c> tuning — local config that never crossed
    /// the wire, so a tuned player's fan floated somewhere else on every other screen. Syncing
    /// the actual anchor (the fan ROOT's live board-local position) reproduces the owner's real
    /// placement for zero guessing. Rotation is deliberately NOT synced: both sides billboard the
    /// fan to the owner's (synced) head every frame, so the rotation is already derived state.
    ///
    /// Written only while a board-anchored fan is actually open; absent means "use the authored
    /// default spot", which is exactly what pre-record peers render. HELD fans (riding the palm)
    /// keep the existing hand-relative placement and never write this record.
    /// </summary>
    public const byte ExtIdFanAnchor = 5;

    /// <summary>
    /// Extension record id: WHICH CARD IS HIGHLIGHTED in the sender's open fans — 2 bytes,
    /// <c>[hand-fan index][board-fan index]</c>, <see cref="CardHighlightNone"/> (255) meaning
    /// "nothing highlighted in that fan".
    ///
    /// WHY IT EXISTS: locally, pointing at a card lifts it toward the viewer, enlarges it and
    /// splits its neighbours apart (<c>VRCard</c>'s pop + <c>CardFan.SetHovered</c>'s whole-fan
    /// split, and the same pop on a pile-browse card via <c>PileBrowser</c>'s hand sweep / laser
    /// hover). Peers rendered every fan flat, so the single most visible thing a player does with
    /// an open fan — singling a card out, which is what the other players are watching when
    /// someone says "this one?" — did not exist on anyone else's screen.
    ///
    /// WHY AN INDEX AND NOT A CARD: the standing rule is that no card IDENTITY ever rides this
    /// wire (peers render backs), and a POSITION reveals nothing an observer cannot already see —
    /// the fan itself, with its card count, is already rendered. One byte per fan also keeps the
    /// cost at 4 bytes on a packet that only carries the record while something is actually
    /// highlighted; an idle player's packet stays byte-identical to the previous build's.
    ///
    /// WHY TWO INDICES AND NOT THREE: at most ONE board fan (item fan or pile browser) can be open
    /// at a time — the Cards layer enforces that mutual exclusion, which is the same reason
    /// <see cref="ExtIdFanAnchor"/> needs only one anchor — so byte 1 addresses whichever of the
    /// two the sender currently has open, and byte 0 addresses the hand fan independently.
    ///
    /// ADDITIVE TLV exactly like every record before it: an older peer steps over it by its
    /// length and simply renders flat fans.
    /// </summary>
    public const byte ExtIdCardHighlight = 6;

    /// <summary>
    /// Extension record id: the sender's PICK-STATUS line — the placard above their control board
    /// that reads e.g. "Barbar: Wähle 1 Karte(n) zum Verlieren" — as UTF8 bytes, capped at
    /// <see cref="PickBannerTextMaxBytes"/>. Written ONLY while a placard is actually shown, so an
    /// idle packet stays byte-identical to the previous build's; absence means "no placard", which
    /// is exactly what peers predating this record render.
    ///
    /// NO CARD IDENTITY: the line names an ACTOR and a COUNT ("choose 1 card to lose"), never
    /// which card — the same thing the game's own turn banner already tells every player. It is
    /// composed in the SENDER's language and shown verbatim, because it is their board.
    /// </summary>
    public const byte ExtIdPickBanner = 7;

    /// <summary>UTF8 byte cap for <see cref="ExtIdPickBanner"/>. The composed line is one short
    /// sentence; the cap bounds a single extras record and is re-clamped on read (never trust the
    /// wire). Truncation is on a UTF8 CHARACTER boundary, never mid-sequence.</summary>
    public const int PickBannerTextMaxBytes = 96;

    /// <summary>
    /// Extension record id: the sender's SECOND held figure — the mini in their OTHER hand.
    /// 25 bytes: <c>[hand flags][int32 actorId LE][pose 20]</c>.
    ///
    /// <para>THE DEFECT IT FIXES (hardware MP test: "Wenn ein Mitspieler zwei Figuren in der Hand
    /// haelt soll auch dies vollstaendig synchronisiert werden — aktuell sieht man immer nur eine
    /// einzige Figur maximal"). A VR player can grab a figure with EACH hand — the local grab
    /// registry has always been a SET (<c>Board.FigureGrab.HeldFigures</c>) and both minis really
    /// ride their hands on the grabber's own screen. The wire had exactly ONE held-figure slot
    /// (rig flag <see cref="FlagHeldFigure"/>), so peers saw at most one of the two, and WHICH one
    /// they saw flipped with the grab order.</para>
    ///
    /// <para>WHY THE EXTRAS TAIL AND NOT THE RIG PACKET, where the first figure rides: the rig flag
    /// byte is FULL (bits 0..7, up to <see cref="FlagHeldCard"/> — see the note there), and a rig
    /// packet field with no flag bit to announce it is not expressible. The rig reader's
    /// forward-compat contract is literally "validate only what MY flags demand, ignore trailing
    /// bytes"; a second held-figure block appended without a bit would be indistinguishable from
    /// the junk that contract exists to tolerate, so a receiver could never tell "a second figure"
    /// from "a future sender's unrelated tail". The TLV tail has no such scarcity: it is
    /// self-describing, so this record costs no bit at all.</para>
    ///
    /// <para>POSE RATE — WHY THIS IS NOT THE 5 Hz COMPROMISE IT LOOKS LIKE. The extras cadence is
    /// <see cref="ExtrasSendRateHz"/> only while nothing in it is MOVING: the sender already
    /// promotes extras to the full rig rate (<see cref="SendRateHz"/>) while a live field changes,
    /// which is exactly how a dragged control board was made to arrive as smoothly as a hand
    /// (NetAvatarDriver's board-pose motion gate). The second figure joins that gate, so while it
    /// is carried its pose goes out at 15 Hz — the SAME cadence as the first figure's rig block —
    /// and both are eased by the same <see cref="InterpolationSharpness"/> in NetFigures.Tick.
    /// Same sample density plus the same easing is identical motion by construction, which is the
    /// standing requirement ("keine Kompromisse"); a still figure falls back to 5 Hz, where a
    /// slower stream of an unchanging pose is not observable.</para>
    ///
    /// <para>WHY THE HAND BITS: the record names the hand of BOTH minis (the rig packet has no room
    /// to say which hand its own figure is in). Without them a receiver has no way to tell a
    /// legitimate two-hand hold from a contradictory pair of records — and a contradiction is
    /// exactly what a stale/duplicated packet looks like — so it would happily drive two minis into
    /// one palm. With them the reader can REJECT the record when both figures claim the same hand
    /// (<see cref="SecondFigureLeftBit"/> == <see cref="SecondFigurePrimaryLeftBit"/>), which is
    /// what stops the two figures swapping hands or doubling up on a peer's screen.</para>
    ///
    /// <para>Written only while a SECOND figure is really held, so a one-handed hold — and an
    /// empty-handed player — emits the exact bytes previous builds emitted. Older peers step over
    /// the record by its length and keep showing the one figure they always showed.</para>
    /// </summary>
    public const byte ExtIdSecondFigure = 8;

    /// <summary>Second-figure record, hand byte bit 0: the SECOND figure rides the sender's LEFT
    /// hand (clear = right).</summary>
    public const byte SecondFigureLeftBit = 1 << 0;

    /// <summary>Second-figure record, hand byte bit 1: the FIRST figure — the one in the rig
    /// packet's <see cref="FlagHeldFigure"/> block — rides the sender's LEFT hand (clear = right).
    /// It lives here rather than in the rig packet because the rig flag byte has no bit left; it is
    /// meaningful only while this record is present, which is the only time the two hands have to be
    /// told apart.</summary>
    public const byte SecondFigurePrimaryLeftBit = 1 << 1;

    /// <summary>Every DEFINED bit of the second-figure hand byte. Masked on write AND on read so an
    /// undefined bit can never be pre-claimed by garbage — the same discipline as
    /// <see cref="BoardUiOverlayMask"/>.</summary>
    public const byte SecondFigureHandMask = 0x03;

    /// <summary>Payload length of <see cref="ExtIdSecondFigure"/>: 1 hand byte + 4 actor id +
    /// 20 pose. A reader requires at least this much before it trusts the record.</summary>
    public const int SecondFigureRecordBytes = 25;

    /// <summary>Card-highlight record: "no card highlighted in this fan". Also what a receiver
    /// assumes when the record is absent, so absence and this value render identically.</summary>
    public const byte CardHighlightNone = 0xFF;

    /// <summary>Clamp a local fan index onto the wire byte: a negative/absent index and anything at
    /// or past the 255-card ceiling both become <see cref="CardHighlightNone"/>, so a garbage index
    /// can never single out the wrong card on a peer's screen.</summary>
    public static byte EncodeHighlightIndex(int index) =>
        index >= 0 && index < CardHighlightNone ? (byte)index : CardHighlightNone;

    /// <summary>Defensive cap on the version DISPLAY string's UTF8 bytes (the record also
    /// carries the 2-byte build). Plenty for "0.1.0"-style tags; a runaway string is truncated
    /// on write and a longer claimed length is clamped on read, so neither side can bloat or
    /// overrun the packet.</summary>
    public const int ModVersionTextMaxBytes = 20;

    /// <summary>Hand-scale code standing for "1.00x" — the value assumed when the record is absent.</summary>
    public const byte HandScaleDefaultCode = 100;

    /// <summary>
    /// Quantize a per-style hand scale to hundredths. Clamped to 0.20x..2.55x: the byte cannot
    /// carry more, and <c>HandVisuals.StyleScale</c>'s own ceiling of 3.0x is far past any hand
    /// anyone wears (the shipped styles sit at 0.62 and 1.00).
    /// </summary>
    public static byte EncodeHandScale(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale))
            return HandScaleDefaultCode;
        return (byte)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(scale * 100f), 20, 255);
    }

    /// <summary>Decode a hand-scale byte. Garbage degrades to 1.00x rather than to a hand of nothing.</summary>
    public static float DecodeHandScale(byte code) =>
        code < 20 ? HandScaleDefaultCode / 100f : code / 100f;

    /// <summary>How long a remote card-FX flight takes (seconds) — matched to the LOCAL
    /// <c>CardsDriver.FlyToPileSeconds</c> so a peer's flight lasts as long as the real one.</summary>
    public const float CardFxSeconds = 0.4f;
}

/// <summary>
/// Endpoints a remote card-FX flight can start from / land on, encoded as a 4-bit id on the wire
/// (see <see cref="NetProtocol.FlagCardFx"/>). Deliberately SEMANTIC (which piece of the sender's
/// VR furniture), never a world position: the receiver resolves each anchor against the sender's
/// OWN synced hand / control-board pose, so the flight lands where that player's board actually is
/// and costs 4 bits instead of 12 bytes. Values are wire constants — append only, never renumber.
/// </summary>
internal enum CardFxAnchor : byte
{
    /// <summary>The sender's hand card fan (non-dominant palm).</summary>
    HandFan = 0,

    /// <summary>Left play slot of the sender's control board.</summary>
    Slot0 = 1,

    /// <summary>Right play slot of the sender's control board.</summary>
    Slot1 = 2,

    /// <summary>The sender's DISCARD stack.</summary>
    Discard = 3,

    /// <summary>The sender's BURNT (lost) stack.</summary>
    Burnt = 4,

    /// <summary>The sender's ITEM stack.</summary>
    Items = 5,

    /// <summary>The sender's control board in general (origin unknown/board centre).</summary>
    Board = 6,
}
