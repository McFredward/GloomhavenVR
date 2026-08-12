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
    /// <see cref="FlagItemFan"/>; costs no payload bytes (pure flag).
    ///
    /// <para>INERT SINCE THE WHOLE-FAN GRAB WAS REMOVED (user ruling 2026-08-02), and CONFIRMED
    /// inert on 2026-08-09 while record 26 was being placed. The item fan has exactly one anchoring
    /// now: <c>Cards.ItemsPile.IsHandHeld</c> is a hard <c>false</c> (and
    /// <c>IsHeldByLeftHand</c> with it), <c>NetAvatarDriver</c> is the only writer and fills these
    /// two fields from those properties, and <c>PresenceSerializer.Write</c> gates both bits behind
    /// <see cref="FlagItemFan"/> — so NO sender emits either bit and no packet in the wild carries
    /// one. Together with <see cref="FlagItemFanLeft"/> that is TWO DEAD BITS in a flag byte that is
    /// otherwise full, and the next feature should know it.</para>
    ///
    /// <para>THEY ARE NOT RECLAIMED, and that is a decision rather than an oversight. Redefining a
    /// shipped bit is legal on this wire (peers must share a <see cref="ModBuild"/> to play at all —
    /// see <see cref="ExtIdCharFocus"/>), so the bar is not compatibility, it is honesty of the
    /// layout: a bit called "the item fan is hand-held" that means something else is the second
    /// encoding of one fact this protocol's notes repeatedly reject. They stay as a WIRE SEAM for
    /// the interaction that may come back — the receivers still render both modes — and a genuinely
    /// new field belongs in the TLV tail, where it costs no bit at all. That is where the item-use
    /// clip went (<see cref="ExtIdItemUseClip"/>), and it could not have used these bits anyway: an
    /// arc INDEX does not fit in a boolean.</para></summary>
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
    /// would hang the fan off the wrong arm half the time. INERT for the same reason and since the
    /// same ruling as <see cref="FlagItemFanHeld"/> — it is only ever written when that bit is, and
    /// that bit is never written; see the note there for why neither is reclaimed.</summary>
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
    public const ushort ModBuild = 130;
    // Build 130: the composites actually LOAD now, the board is FIXED in the room, the cellar
    // gets the night sky. No wire change.
    //
    // (1) DUAL-ROUTE MAP LOADING: 129's 'Map ABHM'/'Map DDM' exist on the user's install (his
    // Player.log boot dump lists 113 'Map *' prefabs always-loaded) — only the full-path
    // Addressables key failed for composites. Each preference-list candidate (Cellar
    // ABHM→GI→A, Swamp DDM→LML→A, all five verified in his dump) now tries the game's own
    // always-loaded asset store first, then the Addressables path; a MAP CATALOG census line
    // names what the install carries.
    //
    // (2) BOARD FIXED IN THE ROOM ("Die Höhe sowie die Position soll fix sein"): the frame
    // splits — ROOM branch (map + ground fog + fireflies) is world-fixed after placement,
    // sized 2.75× the board's larger extent, board center = main-room center, room floor =
    // board underside (the board can never sink under the map); SKY branch (star dome,
    // shooting stars, dust motes) keeps the perceived-constant pivot algebra. Zoom changes
    // the room's perceived size only — the board/room geometry is bit-frozen.
    //
    // (3) CELLAR NIGHT SKY ("sonst ist der Himmel einfach nur schwarz"): Env_Cellar gains the
    // same StarDome node as the swamp (shared mesh/material by GUID, no texture duplication).
    // Bundle rebuilt — 130 NEEDS it.
    //
    // Build 129: REAL authored game maps as environments, invisible staging, zero red cubes,
    // and a real astrophoto night sky. No wire change.
    //
    // (1) REAL MAPS ("zwei echte interesannte fertige Räume aus dem Spiel"): Map A template
    // replaced by authored multi-room composites — Cellar = 'Map ABHM' (4 rooms), SwampNight =
    // 'Map DDM' (3 rooms) — with their AUTHORED styles kept (only unset axes filled; swamp
    // forces night Tone). Main room normalized to 10 m, map capped at 24 m, placed by the main
    // room's center. Fallback chain: composite → Map A → FX shell.
    //
    // (2) INVISIBLE STAGING ("miniaturversion ... neben dem spielfeld"): the 128 staging depth
    // was fixed world units — 36 cm real at rig scale 137. Now ≥ 50 REAL meters down AND beyond
    // the head camera's far plane, plus a runtime-verified hidden layer.
    //
    // (3) ZERO RED CUBES ("rote boxen"): misses are TERMINAL per decompile (a loaded category
    // list lacking a piece mints a session-poisoning placeholder no packet can serve). Warmup
    // from the map's own styles + placeholder purge + HEAL pass (donor art by piece-suffix,
    // alias injected into the game's own list) + removal fallback — census prints names,
    // '0 fallback' by construction.
    //
    // (4) ASTROPHOTO SKY (style round 3, "VIEL VIEL hochauflösender und realistischer ... Such
    // im Internet"): 16k 'Rogland Clear Night' (Poly Haven, CC0), 8192x2560 BC7 sky-band dome
    // texture, float pipeline + TPDF dither (banding complaint), painted moon kept, fog stays
    // HorizontalBillboard. Bundle rebuilt (+12 MB) — 129 NEEDS it.
    //
    // Build 128: the generated room RENDERS now, the ally banner's clipper, the painted sky.
    // No wire change.
    //
    // (1) ROOM RENDERING ("Boden ohne Texturen, aber nur schwarze Wände"): four causes — the
    // 127 'freeze' (disable ApparanceEntity) let the engine DESTROY all generated content one
    // tick after placement (CheckEntity/DestroyEntity on a disabled-but-alive entity; the user
    // saw the template skeleton); missing Apparance resource packets placed session-poisoning
    // 'Red Cube' fallbacks; DynamicAmbience light rigs are cloned at intensity 0 and only the
    // real scenario ever blends them in; and WallSegmentFade adopted the room's doors. All
    // four neutralized (detach-then-disable, packet WARMUP + poison purge, SetLightLevel(1)
    // + mod-layer light masks + range scaling, tile machinery destroyed at finalize). Proof
    // lines: grep 'ROOM CENSUS' (placement census, lights, T+3s survival).
    //
    // (2) BOARD CENTERED ("das SPiefeld absolut mittig"): the room's horizontal origin is the
    // scenario's tile-bounds center (all tiles incl. hidden — reveals never re-center);
    // player-point fallback re-centers when the board appears.
    //
    // (3) ALLY BANNER ROUND 4 ("unverändert"): all three prior mechanisms provably ran in the
    // 127 log with zero effect — the occluder is UI CLIPPING (the initiative ScrollRect's
    // game-owned clipper; the banner is the only popup part crossing its top edge; enemies
    // have no banner). UnmaskedUiGraphics detaches shown popups from ancestor clippers
    // (maskable=false + RecalculateClipping, both load-bearing in uGUI 1.0.0), value-checked
    // restore. Proof line: grep 'BANNER-CLIP DIAG'.
    //
    // (4) PAINTED SKY ("viel zu Low-Poly ... passt nicht in den Styl"): the swamp dome is a
    // hand-painted night sky (milky-way band, 4200 PSF stars with halos, repainted moon +
    // layered halo, comet-streak shooting stars, bokeh fireflies, wispier fog — fog stays
    // HorizontalBillboard per the standing ruling). Bundle rebuilt — 128 NEEDS it.
    //
    // Build 127: environments are now SCENARIO-ONLY and built from the game's own art.
    // No wire change — [Sky] stays local presentation.
    //
    // (1) SCENARIO-ONLY SCOPE ("Ich WILL garnicht das die Umgebung im Menu rendert - sondern
    // nur im Szenario so wie es die Default originale Umgebung auch macht"): SkyAlternative
    // gates on VRModeStateMachine.ScenarioBoardExists — menu and world map keep the game's
    // default look, ever; leaving a scenario stands the feature down next tick.
    //
    // (2) GAME-BUILT ROOMS (Apparance, per the 126 investigation + probe verdict): a
    // non-Default style loads the game's 'Map A' template via Addressables, stages it 50 wu
    // below the viewpoint, writes the style vocabulary (Cellar = Dungeon/StoneRooms/
    // Candlelight; SwampNight = Forest/Marsh/StillWaters/ForestMoonlight), borrows the detail
    // focus for the ≤30 s build window, waits for the renderer census to settle, then freezes
    // the result (components disabled, colliders stripped, mod layer) and places it into the
    // ambient frame normalized to ~9 real meters, floor-aligned. Generation failure = one-shot
    // warn, FX shell alone. GameEnvProbe retired — mission complete, logic lives in
    // SkyAlternative.MapGen.cs.
    //
    // (3) FX SHELLS REBUILT from own shaders (third-party low-poly art deleted per style
    // ruling): Swamp = star dome (baked moon + twinkle) + shooting stars + fireflies + ground
    // fog as HORIZONTAL billboards (user fog ruling: never re-orient with head movement);
    // Cellar = dust motes. Bundle rebuilt.
    //
    // Build 126: environments become WORLD PLACES (free movement), the game-asset probe, the
    // ally banner's THIRD and depth-side fix. No wire change.
    //
    // (1) FREE MOVEMENT ("ich möchte mich auch in den umgebungen frei bewegen und drehen
    // können"): every mod locomotion writes RigRoot itself, and the 125 environment hung
    // under RigRoot — the room rode every write. Now unparented/world-anchored (spawned at
    // the player's floor point + horizon-projected gaze, re-seated on RigPoseVersion bumps =
    // recenter/ring seat), and the two rig-SCALE writers notify NotifyRigScaled(pivot,
    // before, after) so the env rescales AROUND THE SAME PIVOT — algebraic proof in the class
    // doc that zoom rescales the diorama around the player while the room stays bit-frozen in
    // their real frame. Fly/turn/drag/walk all move THROUGH the room now.
    //
    // (2) STYLE PLAN ("orientiert am Styl von Gloomhaven selber ... Assets aus dem Spiel
    // direkt nutzen"): investigation (.planning/game-env-assets.md) picked Apparance
    // micro-generation (instantiate 'Map A', write Dungeon/StoneRooms/Candlelight resp.
    // Forest/Marsh/StillWaters styles, let the game dress it). De-risked by GameEnvProbe in
    // THIS build: passive Addressables-catalog + engine-availability dumps every session,
    // and the one-shot [Sky] EnvProbe menu experiment (German instructions in its config
    // description) whose ENV PROBE VERDICT line decides approach B vs scenario-time capture.
    // The low-poly environments stay as placeholders until the styled rebuild.
    //
    // (3) ALLY BANNER, ROUND 3: round 2's canvas lift WORKED (his log line 529, card fully
    // visible) — the remaining occluder is WORLD DEPTH: the control board's raised wooden
    // rail pokes in front of the canvas plane exactly where the banner reaches up (enemies
    // have no banner — matching every observation). Fix: while a hover popup is shown, every
    // Graphic in its subtree swaps onto a session-cached clone of ITS OWN material with
    // ZTest Always (OnTopUiGraphics, the ActorBars mechanism inverted; TMP via
    // fontSharedMaterial to avoid per-read copies; CardEffects-family subtrees excluded;
    // reference-checked restore on release/stand-down). Queue untouched: board on-top
    // widgets and ray visuals still paint over the popup.
    //
    // Build 125: 3D ENVIRONMENTS replace the panoramas; mouse eradication round 2 (incl. an
    // integrator-incident restore); ally hover banner. No wire change.
    //
    // (1) ENVIRONMENTS (user: "statt so eine Skybox will ich am Besten einen wirklichen
    // 'Keller' mit samt 3D assets ... wie kleine VRChats worlds"): [Sky] Style is now
    // Default / Cellar / SwampNight ("Umgebung": Standard / DnD-Keller / Sumpfnacht). Two
    // bundled prefabs, built from CC0 Quaternius packs and reviewed for atmosphere on renders
    // by both the builder and the integrator before shipping: Env_Cellar (8x8 m stone room,
    // torch flames, candelabra, chests/books/potions, stairs into darkness; 53k tris) and
    // Env_Swamp (2200-star dome with baked moon + twinkle shader, moon-glint water, willow/
    // dead-tree ring, ground-fog banks, fireflies, glowing mushrooms, shooting stars; 29k
    // tris). ALL world-anchored (his rule: nothing may ride the head), no scripts in the
    // bundle (Shuriken + shader time only), no colliders. Spawned under VRRigDriver.RigRoot
    // at identity — the room stands still in REAL space, keeps perceived size under diorama
    // zoom, rides recenters; zero per-frame writes. MR on = environment always off. The
    // panorama machinery and its three textures are REMOVED from code and bundle (bundle now
    // 30,060,345 bytes — 125 NEEDS this bundle for the environments).
    //
    // (2) MOUSE ROUND 2 + RESTORE: the visible cursor was the real OS cursor riding
    // Mouse.WarpCursorPosition — hidden every frame while VR runs; the virtual pointer PARKS
    // off-screen whenever the laser is not actively driving it, so a stale pixel can never
    // hover anything again (the game's own CursorLockMode.Locked path proves off-screen
    // pointers exit hover cleanly). AND: the 123 head-hover cut + grip fall-through had been
    // silently REVERTED by the 8594ebd merge (stale-base diff swept in reversions — an
    // integrator merge error, now a recorded hazard); both restored, so 125 is the FIRST
    // build that actually ships them. (3) ALLY HOVER BANNER: 'VERBÜNDETER' was clipped by
    // two mod-introduced occluders over the initiative band (merged-canvas paint order +
    // MR backing plate depth rejection via the unflattened ancestor chain) — both closed.
    //
    // Build 124: SKY ALTERNATIVES. No wire change (sky is local presentation).
    //
    // User request: three alternatives beside the game's own (mod-defanged) sky, selectable
    // when MR is off; MR on = sky always off. Shipped: [Sky] Style (Default / Night / Sunset /
    // Cellar — DE: Standard / Sternenhimmel / Abendrot / Gewölbekeller), curated in Grafik ▸
    // Darstellung with a localized dropdown, live-switchable. Assets: three CC0 Poly Haven
    // panoramas (dikhololo_night, kloppenheim_06, drachenfels_cellar) at 4096x2048 in the
    // rebuilt 39 MB bundle, plus GloomhavenVR/SkyPanoramic — an equirect shader sampling the
    // VIEW DIRECTION (mesh-UV-independent, Cull Front, ZWrite Off, Queue Background): a
    // non-occluding backdrop by construction, the same contract SkyBackdrop enforces on the
    // game sphere. Runtime (Core/SkyAlternative.cs): non-Default hides GH_SkySphere
    // (renderer.enabled=false — the safe half of SkyBackdrop's documented trap) and shows a
    // mod-layer inverted sphere that center-follows the head (rotation fixed, radius 0.9x
    // farClip); lazy bundle load on first selection, textures kept for the session (~5 MB
    // DXT1 each). MR precedence: MR-on stands the alternative down BEFORE the chroma sweep so
    // MR records a clean sphere; the dial re-applies on MR-off. Teardown restores everything.
    // BUNDLE NOTE: 124 requires the NEW bundle (39,284,602 bytes) — an old 29 MB bundle logs
    // a one-shot warn and leaves the game sky untouched.
    //
    // Build 123: the seven-item hardware round after the border victory. No wire change.
    //
    // (1) MOUSE DEAD, HEAD CANNOT HOVER: the game's EventSystem pointer swept world-space UI
    // with the head (parked virtual-mouse pixel through a moving head camera = a world ray);
    // MouseWorldSurfaceCut postfixes EventSystem.RaycastAll and strips every game-pointer hit
    // on world-space root canvases — only mod pointer ids (laser/poke/flat-screen) pass.
    // (2) GRIP FALLS THROUGH a trigger-only highlight to the nearest GrabWithGrip target — the
    // tray bar is grabbable beside a highlighted card (grip=bar, trigger=card).
    // (3) MP SETTINGS consolidated in Avatar & Mehrspieler (SyncPeerFades moved).
    // (4) SLIDER ROWS show the live formatted value (the '50/50' was the donor volume row's
    // baked caption on a label whose rebind target died in a deferred Destroy).
    // (5) DEPENDENT OPTIONS fold out under their parent (VROptionsTab.8.Dependencies —
    // TurnMode/Flight/WorldGrab chains, BoardMoveMode=LimitedPitch -> tilt limits,
    // RevealMode=tilt -> angles, StretchLimits -> Min/Max, Net/Enabled -> MP rows, section
    // masters). (6) ELEVEN DIALS whose OFF breaks/deadlocks/blinds the game are REMOVED,
    // features always on (PileViewer, ActivePile, WorldUI Master, FlatScreen(+AutoShow),
    // UseBars, DoomPicker, DistributePanel, CatchAllModals, MenuPopupFloat, ManualScreenChord;
    // the Seconds dial survives). None rode the wire.
    // (7) HELD-FIGURE PERSISTENCE: the auto-release is PER-FIGURE now — a held idle figure
    // stays in the hand whatever animates elsewhere ("egal was passiert"); it returns to the
    // board only when ITS OWN bar/flow/wait/animator leaves idle — and via the normal 0.28 s
    // release glide, never a snap (the instant-when-busy branch is gone; authoritative-move
    // release stays instant by design). Grab gate unchanged (idle-only pickup); the skeleton
    // deadlock cannot recur: the deactivation edge exists only at grab time, a flow cannot
    // latch on a hidden bar, WaitingForPlayerIdle names its actor and releases that frame,
    // and the 8 s watchdog stays as the last-resort net.
    //
    // Build 122: the border saga CLOSED — bottom-edge fix, the great cleanup, tab wrapping.
    // No wire change.
    //
    // (1) BOTTOM EDGE ("Der untere Rand von allen Karten ist teilweise transparent"): the
    // contour still derived from the v5 footprint, learned from PUNCHED pixels intersected
    // with the frame-era CardOutline bands — the body stopped higher than the stock art draws.
    // Now the capture stamps the STOCK art's own designed alpha and nothing else (drop-shadow
    // trim, dark-border peel and OUTLINE CLIP deleted); CacheVersion 5 -> 6 forces a relearn,
    // after which body and face coincide by construction (largest-closed-loop rule keeps
    // interior holes from piercing the body — "nicht transparent innerhalb des outlines").
    //
    // (2) THE GREAT CLEANUP (user: "räum den code auf ... der durch die vielen Versuche
    // entstanden ist"): net -7,200 LOC. Deleted: CardShapeMask (incl. the Net no-op call
    // sites), CardDissolveFloor + its dial, CardShaderProbe, CardBandPainter,
    // CardBandPixelCapture, CardFaceCrop, CardOutline, all punched/cropped mint factories and
    // per-placement machinery in CardFaceMipBake, the FramePunch sweep + band diagnostics in
    // CardFace, the cutout material bake in CardMesh, and the inert [Cards] GrabButton dial
    // (with its dead ProximityGrabber branch and menu rows). KEPT: CardContour + AttachBody
    // (the solution), stock-alpha silhouette capture v6, mip bake, emission floor, umber
    // EdgeColor, SlotSeatLiner + Net mirror, FaceBlackout (serves the stock path). The full
    // 17-round history lives in these build notes (105-121) and git.
    //
    // (3) TAB CAPTIONS wrap instead of shrinking (FitTabCaption: rect over the widget height,
    // word wrap, fontSizeMax capped at donor size; explicit breaks at '&' for 'Brett &
    // Karten' / 'Avatar & Mehrspieler', DE+EN; same treatment for the Erweitert chooser).
    //
    // Build 121: the border's attempt SEVENTEEN — the user's own design, implemented literally.
    // No wire change. His ruling (verbatim in CardContour/CardFaceMipBake docs): the card FACE
    // must render completely correct again, and the card MESH must be punched out to the
    // surface's outline. (Note: his "120" log was actually a 119 run — the 120 deciders never
    // executed; geometry makes the open shader-variant question moot anyway.)
    //
    // ORDER A — face restored: punched/cropped sprite serving retired at the mint
    // (PunchServingEnabled=false, CardFaceCrop.Enabled=false, live state restored through the
    // existing contracts). Every face everywhere — local, item, peer — draws its stock
    // mip-baked art again, incl. the designed printed frame; the broken card bottom dies with
    // its cause. The FramePunch sweep lives on solely to DERIVE the outline; the footprint
    // capture's OUTLINE CLIP is now load-bearing.
    //
    // ORDER B — body truly punched out: CardContour (new) extracts the outline contour from the
    // cached footprint (marching squares 0.5 iso, closed-loop DP <= 120 verts, ear-clip front,
    // mirrored back, extruded rim, bounds pinned to the full card box) and CardMesh.AttachBody
    // serves the shaped mesh to every registered body the moment a kind's contour is known
    // (rounded slab until then; one-step ReshapeBodies swap; warm cache = shaped from first
    // draw). Geometry needs no shader cooperation: cutout baking retired
    // (CutoutMaterialsEnabled=false); Standard + umber EdgeColor + emission floor stay. All
    // metrics consumers verified to read the full card box, never the mesh. ALL SIX card-body
    // mirrors adopted AttachBody in the same build (RemoteHandFan/ItemFan/BrowserFan/CardFx/
    // Avatar + WorldUI AvatarMirror; submesh count 2 -> back material listed twice; shared
    // cached meshes are never Object.Destroy'd by mirrors anymore) — peers see the same
    // punched-out bodies, per the 1:1 ruling.
    //
    // Build 120: the border's attempt SIXTEEN — the DIFFERENTIAL. No wire change; pure
    // instrumentation on the border plus nothing else. 119 was the partial success that proved
    // the painter chain (band now renders the slab's umber+light instead of unlit black); what
    // remains is a paradox: the cutout texture's alpha is provably transparent in the bands and
    // the material state is provably correct, yet the band renders opaque. The code audit
    // exonerates everything local (ApplyEmissionFloor writes only emission state on every path;
    // _Cutoff = 0.5; ConfigureCutout is authoritative last everywhere), leaving ONE chief
    // suspect: the game build's Standard shader may ship WITHOUT the _ALPHATEST_ON variant —
    // EnableKeyword is only a request, a stripped variant never calls clip(), and the slab then
    // draws its full envelope in EdgeColor: exactly the measured band. This build decides it:
    // (1) CARD BAND DIFF — the same probe framing re-rendered once per candidate with exactly
    // one suppressed (Backing renderer / edge submesh via sharedMaterials swap / SlotSeatLiner /
    // face canvas), per-strip verdict names the painter; (2) CARD BAND TEX-VS-RENDER — live GPU
    // texture alpha (Blit readback; the baked texture is non-readable on CPU) RLE'd along the
    // scan row+column beside at-capture material state of both slots; (3) CUTOUT CLIP PROBE —
    // the live edge material drawn with UVs pinned to a verified alpha-0 texel vs the opaque
    // centre over a sentinel: "clip EXECUTES" / "DOES NOT EXECUTE (variant stripped)". If the
    // probe says DOES NOT EXECUTE, the fix lane is a clip-capable bundled shader (BoardLit +
    // clip()) or true mesh trimming to the outline — not more texture/keyword work.
    //
    // Build 119: the border's attempt FIFTEEN — the LIGHTING conviction. No wire change.
    //
    // WHY FOURTEEN COLOR/TEXTURE ROUNDS WERE INVISIBLE: the card slab renders through stock
    // 'Standard', which multiplies albedo by incoming light — and the VR scenes are dark. The
    // karten4 band measured (4,4,3), ~16 % of even the OLD near-black EdgeColor's lit value:
    // every albedo change was multiplied by ~0 before reaching the eye. The board never had
    // this problem because it ships its own 'GloomhavenVR/BoardLit' (ambient-floored) — but
    // BoardLit hard-codes alpha 1.0 and has no clip(), so it cannot carry the silhouette
    // cutout (bundle source read to prove it; a shader edit needs a bundle rebuild — declined
    // scope). Fix: CardMesh.ApplyEmissionFloor — _EMISSION on, _EmissionMap = own albedo,
    // _EmissionColor = tint x 1.0 (factor documented against BoardLit's ambient math) on the
    // shared Ability/Item/Neutral pairs and both baked cutout textures; every Net mirror
    // borrows these exact instances, so peers are fixed for free. Same floor applied to every
    // other Standard-shaded near-card surface (procedural fallback board now BoardLit-first,
    // ItemsPile fallback slab, PileViewer stack slabs, fallback button caps with tint
    // re-syncs). AND THE INSTRUMENT IS HEALED: the 118 capture was blind because it copied
    // Camera.main's mask (game ScenarioCamera, 0x700FFF17) which excludes the mod layer (27) —
    // now the mod HeadCamera's mask unioned with the card subtree's actual layers,
    // centre-as-canary retry with ~0, honest INSTRUMENT FAILURE verdict, plus a full
    // mid-height RLE SCAN line (width + face-x + mean RGB per run) — the analysis that
    // convicted the recess floor from screenshots, now automatic in every log. Caveat named
    // in code: a stripped _EMISSION variant would no-op silently — the scan is the arbiter.
    //
    // Build 118: the border's attempt FOURTEEN (two unit/color convictions + the pixel
    // instrument), the per-side mirror ghost hands, and trigger-only cards. No wire change.
    //
    // (1) THE BORDER, ATTEMPT FOURTEEN. The 117 run proved the liner idea right but the liner
    // WRONG-SIZED: it was a factor of the BASE card box while the seated card renders at the
    // live [Cards] SlotOverlayScale dial (~1.7 on his rig) — coverage ~1.10x of the seated card
    // vs a ~1.30-1.40x well, so the floor stayed visible all round ("unverändert"). Now sized
    // seated-card x SlotLinerSeatRatio (1.45) off the SAME dial, z re-derived from the tuning
    // chain, on both sides of the wire (RemoteBoardFurniture takes tuning field 171 into the
    // product; the superseded SlotLinerScale const is deleted). SECOND CONVICTION: the thin
    // NEUTRAL near-black ring on fan cards is the mod's own slab — CardMesh.EdgeColor
    // (0.10,0.09,0.08) is baked as the RGB of every silhouette-surviving Edge texel; retinted
    // to warm umber (0.42,0.33,0.23), no geometry/clip change. Verdict logic tightened (a
    // candidate must touch the actual band ring — no more rest-button convictions). And the
    // guessing-ender ships: CardBandPixelCapture renders the REAL card in situ from a probe
    // camera at latch/re-arm and logs mean RGBA per band strip classified against known
    // signatures (recess floor / liner wood / frame print / green screen) — the maroon print
    // band the 117 log still showed (91/264 punched-header, 27-28/36 unpunched action-default
    // probes) gets convicted per placement in the next log instead of re-deduced.
    //
    // (2) MIRROR GHOST HANDS: AvatarMirror drove its per-side HandGhost pair from the legacy
    // single-side HandGhosts.LocalSide (fan-side preferred) — taking a card from the fan
    // ghosted both real hands but only one in the glass. Now driven from LocalLeft/LocalRight
    // (the wire-mask truth; peers were already correct). (3) TRIGGER-ONLY CARDS (user: "Die
    // Karten sollen nur mit dem trigger nehmbar sein"): the grip proximity fallback is removed —
    // cards and figures grab on trigger only, grip keeps panels/tray/world-drag and logs a named
    // throttled refusal near a card. [Cards] GrabButton is now inert — retirement candidate.
    //
    // Build 117: FOUR lanes — the border SOLVED (attempt 13: it was the board, not the card),
    // the dead-dial purge, the full VR options menu restructure, and the stretch/info settings
    // from 116 (already noted below). No wire change.
    //
    // (1) THE BORDER, ATTEMPT THIRTEEN — the conviction. 116's CARD BAND PAINTER ran on the
    // user's rig and ACQUITTED every card layer while his screenshot still showed the band; the
    // reach was wrong, not the machinery. The painter is the bundled tray asset's own authored,
    // AO-baked-dark RECESS FLOOR — an ancestor mesh ~1.2x the card that no card-root sweep could
    // see. Pixel proof: the band is WARM (r-b +7..+21, tray wood) while every card layer is COOL
    // (-11..-18). The 111 "0.94" finding stands (the 132 % slab spans in the 116 log were a
    // world-AABB artifact of tilted cards — the diagnostic now measures local-tight). Punch,
    // crop and silhouette were working for rounds; every erased pixel simply rendered in
    // recess-floor black — including the bottom "kaputt" (bright kept ornaments floating on
    // black). Fix: SlotSeatLiner — an opaque rounded CardMesh slab, 1.78x the card box, keycap
    // carved-grain wood, in every recess; mirrored 1:1 on peer boards (RemoteBoardFurniture).
    // The floor margin AND every punched pixel now read as board wood. CardBandPainter upgraded:
    // local-tight rects, ancestor/sibling subtree sweep with backdrop verdicts, throttled
    // state-change re-arm — if any residue survives (hand fan?), the log names it.
    //
    // (2) DEAD-DIAL PURGE (menu audit, user order "alte Einstellungen ... entferne diese"):
    // 48 keys removed after per-key re-verification — WristHud twins, [Rig] WorldScale, the
    // 4+12 hand-seat family, RayAlwaysOn (redundant read), ModalRayConeDegrees (gate retired),
    // 18 disproven map-capture strategy keys, the HeldScale family, ForceFarMode. Audit
    // corrections kept ScrollWithStickOnly (live reader UguiPointer.cs:505), Experimental3DMap
    // (charter), the parked WorldTilt family. ScreenLeftMirrorFallback's description no longer
    // claims "no reader" while gating the black-map probe. Wire tests 1529 -> 1500 (removed pins).
    //
    // (3) MENU RESTRUCTURE (user: "Setze erstmal alle Vorschläge zu den Settings deinerseits so
    // um"): everyday tabs Komfort / Grafik / Brett & Karten (new) / Tafeln / Avatar & Mehrspieler
    // (merged) / Erweitert (renamed from Debug); ~30 promotions incl. EyeResolutionScale, MSAA,
    // PixelLightCount, hand size, panel toggles, accessibility full-grip; hand-built trees for
    // the two oversized topics (VROptionsTab.7.TopicTrees.cs); localized auto group headings
    // (GroupWordLabel); naming pass; new [Cards] CardSoundsEnabled master switch gating the five
    // sound strings (playback AND, strings untouched). Wire tests 1509 (+9 explicit step pins).
    //
    // Build 116: THREE lanes — the border's attempt TWELVE (the second full-card layer), the
    // stretch size-bounds settings, and the held-info toggle. No wire change.
    //
    // (1) THE BORDER, ATTEMPT TWELVE. The 115 probe EXONERATED the card shader (verdict (a):
    // alpha honored at rest — the punched pixels were genuinely invisible), which convicts a
    // layer no inventory ever named. Found in source: FullAbilityCard.unfocusedMask — ShowCard
    // loads the SAME background sprite into headerImage AND this full-card dimming copy, drawn
    // over the front at its own slightly different rect (the 115 two-placement warning measured
    // it: y offset 0.02, height 0.97), toggled by SetUnfocused and ACTIVE IN MULTIPLAYER for
    // every presented character not under local control (CardsHandUI.cs:1615) — the 115 session
    // was online. Un-cut, it repaints the printed frame the header rounds erased; served the
    // header-rect punch copy (the 115 serving), it erases ~2 % into the card BOTTOM at its own
    // rect — the mechanical explanation for "der untere Teil der Karten ist der Rand nun etwas
    // kaputt". Shipped: the mask is a SIXTH named plate (punched AND rect-cropped against ITS
    // OWN drawn rect); CardFaceMipBake mints PER-PLACEMENT punch copies keyed (source, mapping)
    // so a second materially different rect can never wear the wrong copy again; the dissolve
    // floor defaults to 0 (probe says it cures nothing, and _Dissolve_VerticalGradient=0.2 means
    // even an epsilon discards bottom-first on never-punched art — the other bottom suspect);
    // and CardBandPainter latches a full painter inventory (every Graphic AND every Renderer
    // under a tray card and a fan card, material identity, cull state, a rest-output GPU probe
    // per custom shader — the CardEffects fgFx 'UIFX_Overlay' spans 120 % of the face and was
    // never probed). If the band survives 116: read CARD BAND PAINTER — it names the painter.
    //
    // (2) STRETCH SIZE BOUNDS (report items 2+3, quotes in FigureGrabbable/StatPanelSurface):
    // StretchScaleMin/Max now bound the TOTAL held size relative to the default-zoom board size;
    // a deep-zoom grab enters the hand clamped AT the bound; the gesture envelope is rebased per
    // hold (Min/ratio .. Max/ratio). New [FigureGrab] StretchLimits master switch (off = only a
    // 0.01x technical floor; the wire already clamps outgoing factors to [0.10 .. 8.0] so
    // limits-off cannot trip the fail-closed decode). New [FigureGrab] HeldFigureInfo gates the
    // pickup-docked ActorStatPanel at the registration seam; mid-hold flip closes an open panel.
    // All four dials in the VR settings (Hands topic), localized DE/EN.
    //
    // Build 115: TWO lanes again — the border's attempt ELEVEN (the first SHADER-side one) and the
    // stretch gesture's hardware-test fixes. No wire change; the bump is here because 115 ships.
    //
    // (1) THE BORDER, ATTEMPT ELEVEN. The 114 log closed the case file on the texture side: the
    // rect crop provably applied (294x450 -> 291x436) and the user still saw an identical band,
    // and the CARD SHADER IDENTITY line named the painter — every face Image renders through a
    // per-image clone of 'GUI_CardEffect_Mat' / 'GUI/AbilityCard_Shd' with no readable blend
    // state. The user's own report is the tell: the band turns TRANSPARENT during the game's
    // dissolve animation, i.e. a clip of the shape clip(f(tex.a) - _Dissolve*..) that discards
    // nothing at the rest value 0 and paints alpha-0 pixels opaque black — which retroactively
    // explains all nine texture rounds at once. Two pieces ship: CardShaderProbe (once per
    // session, renders the punch's exact alpha-0 pixels through a CLONE of the live material at
    // _Dissolve 0 and 0.004 into an RT and reads the pixels back — the CARD SHADER PROBE line's
    // verdict PROVES or REFUTES the mechanism in the same log the user drops) and
    // CardDissolveFloor (the fix: hold _Dissolve at [Cards] DissolveFloorFraction = 0.004 at
    // rest, never lowering an animated value, on every card-FX material the mod manages —
    // ability faces via CardArtWatch, hosted ITEM cards via ItemsPile.ItemChip, and remote peer
    // fronts for free because RemoteCardArt already rides CardArtWatch; selection by material
    // signature (_Dissolve + _PosAndBounds), full-restore on yield/recycle). Harmless under every
    // probe verdict, decisive under the suspected one. Punch/crop machinery unchanged — the floor
    // needs the punched alpha-0 pixels to discard.
    //
    // (2) STRETCH FIXES from the 114 hardware test (verbatim quotes in FigureStretch.cs): the
    // capture zone was a fixed 80 mm sphere around the mini's CENTRE, so a stretched mini could
    // not be shrunk — its body sat outside its own zone. Capture is now surface-based (min
    // closest-point over the held visual's renderer AABBs, cached per hold, sanity-clamped at
    // 0.5 m real with centre-distance fallback) and therefore scales with the figure by
    // construction; d0/d deliberately stay centre-based. Plus the requested zone-entry haptic:
    // the figure-hover pulse (HapticPreset.HoverTick, same rate limit), edge-triggered on
    // entering, silent inside, re-armed by leaving. No wire impact; record 30 unchanged.
    //
    // Build 114: TWO lanes — the border's attempt TEN, and a new feature, the held-figure stretch.
    // WIRE CHANGE: extension record 30 (ExtIdHeldStretch) — additive TLV, old readers skip by
    // length, absence means neutral, an unstretched player is byte-identical to 113.
    //
    // (1) THE BORDER, ATTEMPT TEN. The 113 log narrowed it to two failures. The action plates had
    // escaped eligibility heuristics twice (Image.Type.Simple in round 8, the 90 % span gate in
    // round 9: "1 layer(s) eligible"), so heuristics are gone: the five face layers are resolved BY
    // IDENTITY from CardEffects' own serialized fields (_headerImage, _topButton, _bottomAction,
    // _topDefAction, _botDefAction — verified against decompiled source at merge). And the punched
    // background produced no visible change even where only it paints, which leaves one explanation
    // class: erasing to transparent black does not change what his renderer draws — the CardEffects
    // custom material's shader may ignore alpha, which would retroactively explain all nine
    // texture-side rounds AND the band vanishing during the game's own dissolve animation. Two
    // answers ship together: CARD SHADER IDENTITY (one latched line per shader, blend state and a
    // verdict clause — that answer stands whatever else happens) and the shader-AGNOSTIC fix: the
    // punched sprite is cropped to the outline's extent and the layer's RectTransform shrunk to
    // exactly the face rect the kept pixels cover, so no rasterized geometry exists in the band
    // under ANY alpha semantics. Fallbacks (peer clones, item faces, unmappable layers) keep the
    // named alpha punch and latch a line naming their gate.
    //
    // (2) HELD-FIGURE STRETCH (user request, verbatim in FigureStretch.cs): while one hand holds a
    // mini, the OTHER hand's trigger near it starts a ratio-based stretch — outward grows, inward
    // shrinks, s0 x (d/d0), clamped [FigureGrab] StretchScaleMin/Max, distances in real metres so a
    // mid-gesture zoom cannot masquerade as hand travel, measured to the mini's root so the output
    // cannot feed the input. Collisions closed from source: the held mini cannot be steal-grabbed
    // (CanGrab false while held), the gesture hand's election is suppressed and TryLaserGrab
    // early-outs while engaged, a hand hovering a CARD does not capture (the visible highlight
    // keeps its trigger), and the busy gate is untouched — scaling is presentation-only and stays
    // allowed mid-attack. Scope: THIS hold only; release restores board size on every path (the
    // HeldScale config family is retired LEGACY, so no persistence was invented). MP per the 1:1
    // ruling: record 30 carries two u16 milli-factors (slot-aligned with record 8), sent at the
    // extras fast gate only while non-neutral; the receiver eases it into the existing
    // zoom-ratio product and every release path still restores HomeLocalScale. +40 wire test
    // vectors (1526 total), incl. byte-exact layout, neutral omission, fail-closed decode and the
    // ConfigSteps pins for the three new dials.
    //
    // Build 113: the black card border, attempt NINE — the punch region is now GEOMETRY, not luma.
    // No wire change; the bump is here because 113 goes to the friend.
    //
    // WHAT 112'S OWN DIAGNOSTICS ESTABLISHED. The BAND INVENTORY named the culprits outright: the
    // punched background STILL contributed 102 of 264 near-black band probes (the luma-BFS ate only
    // 24 px and stopped — the frame is interrupted by brighter decoration), and the two ACTION-HALF
    // plates contributed 55 of their 72 probes while never being punched at all. The integrator then
    // profiled the user's screenshot across a card edge and found the structural reason every
    // luma-threshold approach failed three times: card CONTENT sits at luma 55-65, the frame below
    // 40, the threshold was 48 — a knife edge — while the card's gold TRIM line sits at ~213, a
    // robust landmark.
    //
    // SO THE DEFINITION FLIPPED: derive the card's TRUE OUTLINE once per kind from the bright trim
    // contour (first-bright row/column scans, interpolation across AA gaps up to 3 % of an axis,
    // 5-tap median, central-IQR validation, a 140->110->80 threshold ladder), then erase EVERYTHING
    // outside it on EVERY layer that maps there. One geometry, three consumers: sprite punch, mesh
    // footprint (now footprint ∩ outline), and the still-disabled CardShapeMask. Validated against
    // the screenshot's measured band widths before shipping; a derivation that fails its own gates
    // REFUSES with a logged line and the behaviour stays exactly ModBuild 112.
    //
    // THE DECISIVE DISCOVERY: the action halves were never excluded by the 70 % coverage gate — they
    // fell one filter EARLIER, at Image.Type.Simple. They are prefab-serialized 9-SLICED Button
    // plates, and a sliced image does not map its sprite uniformly onto its rect, so the punch now
    // replicates uGUI's GenerateSlicedSprite mapping (borders, multiplied PPU, clamp-scaled, live
    // values). First time in nine attempts those two plates are touched at all. Eligibility is a
    // span gate (a layer spanning >= 90 % of one face axis) rather than "everything": the three
    // measured contributors all pass, while a centred icon, a gold-edged protrusion or a shared glow
    // sprite structurally cannot.
    //
    // CacheVersion 4 -> 5 (fourth load-bearing bump). Restore audit re-done: both punch flavours
    // register in s_originalByReplacement, all three restore paths route through RestoreSprites,
    // shared atlas readbacks are never mutated. Peers ride the shared content-keyed cache — zero
    // Net/ changes.
    //
    // KNOWN RESIDUAL, pre-existing (STATE §5e): a hovered action half renders the game's
    // Image.overrideSprite state sprite, which no bake has ever covered — the frame can transiently
    // reappear inside a plate WHILE hovered. Not new to this round.
    //
    // IF THIS FAILS: the log separates the failure modes in one read. "CARD OUTLINE refused" =>
    // derivation refused, behaviour is 112. Outline validated but band survives => BAND INVENTORY
    // names the painting graphic AND prints the derived per-edge bands beside it: bands disagreeing
    // with the visible band => the derivation is wrong and its line says so; agreeing => a consumer
    // escaped and the sweep line counts which.
    //
    // Build 112: the black card border, attempt EIGHT — the first that edits the layer that
    // actually PAINTS the black. No wire change; the bump is here because 112 goes to the friend.
    //
    // HOW THE SEARCH SPACE CLOSED. Round 7's falsifier fired on the 111 run: ART RECT reports the
    // art drawing on 100.0 % x 100.0 % of the face rect — no preserveAspect letterbox exists on his
    // art, so that cause is absent. What remained is the one POSITIVE measurement of the series,
    // now confirmed twice: the dark-border PEEL hit its depth cap in both runs (110: 11 of 11
    // texels; 111: 18 of 18, mean luma 22). There is an opaque, near-black, boundary-connected ring
    // IN THE ART'S OWN PIXELS, at least ~5 mm deep — a PRINTED frame. Every prior mechanism either
    // manipulated the mesh (invisible behind opaque art) or clipped the face to an outline captured
    // BY OPACITY, which includes the printed frame by definition (why round 5's verified stencil
    // clip changed nothing).
    //
    // THE FIX: the mod already owns the textures the face renders — CardFaceMipBake swaps every
    // card-face sprite onto mod-owned per-sprite mip-baked copies (the 'VR-mip' names in the log).
    // PunchedReplacementFor mints frame-erased copies: BFS erosion seeded from the sprite's own
    // border and transparent-adjacent texels, eroding opaque (a >= 128) near-black (luma <= 48)
    // pixels, with the depth LEARNED rather than guessed (two guessed caps in a row were too
    // small) — sanity ceiling 15 % of the sprite's short side, all-or-nothing discard above 30 % of
    // the opaque area ("dark CARD, not dark FRAME"). Eroded pixels get alpha 0; uGUI alpha-blends;
    // behind them the mesh is clipped by the same measurement, so the board shows through. Only
    // sprites whose DRAWN rect spans >= 70 % of their own face may be punched — icons, buttons and
    // portraits can never pass. Every refusal logs its gate and numbers.
    //
    // MESH/FACE AGREEMENT BY CONSTRUCTION: the punch sweep runs BEFORE the capture inside Offer, so
    // the footprint is stamped FROM the punched sprites — the punched texture is the footprint's
    // pixel source, and the mesh cannot peek out where the face stopped painting. CacheVersion
    // 3 -> 4 (a v3 mask still calls the frame band "card"; third time this bump has been
    // load-bearing). Restore audit: punched copies register in s_originalByReplacement like every
    // replacement, so all existing restore paths hand the game its originals unchanged; the shared
    // atlas readbacks are never mutated (fresh row-slices only). Peers get the punched copies for
    // free through the shared bake cache (RemoteCardArt drives Rescan + Offer on its clones).
    //
    // Round 4 had considered exactly this and declined it as "pixel surgery … unjustified against a
    // speculative cause". The cause stopped being speculative when the peel measured the ring —
    // twice. The record is the lesson: a mechanism rejected on risk grounds becomes the right
    // mechanism when the evidence arrives; re-weigh parked options when new measurements land.
    //
    // IF THIS FAILS TOO: the black is painted by a face graphic that escaped the punch gate or
    // carries the band outside its sprite pixels — and the new CARD FRAME BAND INVENTORY line names
    // every drawn graphic contributing opaque near-black probes to the outer 8 % band, with counts.
    // Grep CARD FRAME BAND INVENTORY: the culprit is named, not guessed.
    //
    // Build 111: the black card border, attempt SEVEN — and the first one that MEASURED instead of
    // inferring. No wire change; the bump is here because 111 is what goes to the friend.
    //
    // THE UNLOCK WAS A SCREENSHOT. Six rounds were spent reasoning about art that could not be read
    // offline (ressources/ is Managed/*.dll only). The user supplied .planning/debug/karten.png and
    // the defect fell out in one pass of pixel measurement:
    //   • the band is TOP and BOTTOM (4.76 % / 6.6 % of the card height), NOT left/right (0.8 % / 2.4 %)
    //   • its corner is a clean ~12 px arc = CardMesh.CornerRadius, and its colour is a flat (4,4,3)
    //     identical on a fan card at a different orientation ⇒ it is the mod's MESH, not the art
    // Both the integrator's aspect hypothesis and its axis were wrong; the arithmetic that produced
    // it was right about the numbers and inverted about which edge they landed on. Measuring settled
    // in minutes what six rounds of deduction could not.
    //
    // THE CAUSE. VRCard.SetCanvasSize rescales the slab NON-UNIFORMLY to facePixels × fit ×
    // VisibleFaceFraction, i.e. exactly the face rect (294:450 = 0.6533), and CardFace draws the face
    // at the same 0.94 — so slab and face rect coincide and round 5's "0.94 refutation" stands. What
    // nobody checked is what the game draws INSIDE that rect: the ability art is poker-shaped
    // (0.7216) and Image.preserveAspect letterboxes it to 0.6533/0.7216 = 90.54 % of the rect's
    // height, leaving 4.73 % dead top and bottom. Measured: 4.76 %.
    //
    // WHY SIX WORKING CLIPS CHANGED NOTHING. The capture normalised each candidate by its
    // GetWorldCorners LAYOUT rect and stamped the sprite across all of it — so the mask was stretched
    // ~10 % vertically and declared the body "card" in precisely the two bands where the art draws
    // nothing. Every clip was correct and aimed 10 % away from the edge it was looking for. The 110
    // log stated the consequence without naming it: footprint bbox 95.5 % of the face at 0.915 fill,
    // while the art visibly occupies 88.6 %.
    //
    // THE FIX is a pure coordinate correction, because the user rejected all three ways of correcting
    // the aspect: "Ich möchte gerne an den aktuellen Proportionen festhalten. Ich will es also so wie
    // es jetzt ist und sich verhält - nur eben ohne die schwarzen Ränder." CardFace.DrawnLocalRect
    // replicates uGUI's PreserveSpriteAspectRatio byte-for-byte — including the PIVOT re-anchoring,
    // not a centred shrink — from live values only (img.preserveAspect, sprite.rect,
    // rectTransform.rect/pivot), so a tuned CardWidth, a different face resolution and the item
    // card's own near-square art all fall out of the same three lines. No transform, mesh, collider
    // or config value is written: CardWidth/CardHeight, CardMesh.Get, SetCanvasSize, WorldWidth,
    // record 11 and SlotOverlayScale are untouched. Only the rectangle the alpha is stamped into
    // changed, and the band goes because the body stops painting where the art does not.
    //
    // The PEEL was at its cap on the 110 run (max depth 11 of 11 texels = 3.1 mm against a ~4.6 mm
    // band), so it could not have reached the frame at any luma threshold; depth 0.05 -> 0.08 and area
    // 0.12 -> 0.20 (the two long edges alone were 10.7 %, i.e. it sat ON the old all-or-nothing
    // ceiling). All four guards kept.
    //
    // PEER SLABS had the mirror of this bug — their 0..1 is the CARD, not the face rect, so today's
    // export already stretched the mask ~10 % over them, invisible only while the mask was near-
    // rectangular. Fixed inside Cards/ by cropping the export to the art rect, so Net/ needs no edit
    // and with no letterbox the export is byte-identical to today.
    //
    // CacheVersion 2 -> 3, load-bearing: a v3 mask is transparent where v2 said card. The last bump
    // proved the point — the 110 log opens with "header mismatch … version 1 want 2", and that is the
    // only reason that run tested round five instead of round four.
    //
    // CardShapeMask stays DISABLED (dfe54e0): a uGUI Mask forces children through a SNAPSHOT material
    // and freezes the shader properties CardEffects animates, which was the black-flash regression.
    //
    // Build 110: attempt FIVE at the black card border, and the first that clips the layer the
    // black is actually on. No wire change; the bump is here because 110 is what goes to the friend.
    //
    // WHAT FOUR ATTEMPTS GOT WRONG, because it is the generalisable part: every one of them acted on
    // the card BODY. The 109 log finally proved the body cannot be the answer — its own new
    // diagnostic refuted the standing hypothesis (there is exactly ONE sprite-less quad on a face and
    // it is WHITE: 'UIFX_Overlay', luma 1.00, kept), and the shape line reported a captured outline
    // whose bounding box is 95.5 % of the face at 0.928 fill, i.e. with real corner cuts. If the
    // clipped body were what bounds the visible card, the cards would have STOPPED looking
    // rectangular in attempt 2. The user's report is unchanged: rectangles with a black rim. So the
    // FACE paints over the body's whole footprint and the rim is on the face.
    //
    // (A 0.94 coordinate-mismatch hypothesis was raised by the integrator and REFUTED by arithmetic:
    // the host rect is the face rect, so ComputeFitScale is 1 and both the art inset and the slab
    // carry the same 0.94. UV 0..1 on the body is the full face. Written into SetSilhouette's doc as
    // do-not-re-test.)
    //
    // THE FIX: Cards/CardShapeMask.cs stencil-clips the adopted FACE itself to the same footprint —
    // a mod-owned wrapper between host and face, at the face's sibling index, sized to the face's
    // RENDERED rect, with showMaskGraphic = false so nothing of ours ever draws.
    //
    // The reason this was not simply done in round 1 is real and is the interesting part:
    // MaskUtilities.FindRootSortOverrideCanvas stops at the first ancestor Canvas with
    // overrideSorting, FullAbilityCard carries its own Canvas, and the game TOGGLES its sorting
    // (CardsHandUI.ToggleFullCardCanvasSorting) — so a mask above it can silently render a plain
    // rectangle while every log line claims success, which is precisely how attempt 1 failed. Hence:
    // overrideSorting is forced false on EVERY canvas inside the wrapped face (each one's original
    // recorded, because a toggle means a canvas reading false at install can read true two frames
    // later), re-asserted per frame, re-scanned as the game grows the hierarchy, restored exactly on
    // release. And the install VERIFIES itself by making the same two calls MaskableGraphic makes —
    // FindRootSortOverrideCanvas then GetStencilDepth — requiring depth >= 1. It logs INSTALLED and
    // VERIFIED, REFUSED and REMOVED (wrapper lifted out, canvases restored, latched per kind), or
    // INCONCLUSIVE (a card parked under the inactive pool root has no active canvas to resolve
    // against). A mechanism that can silently do nothing does not go into this file again.
    //
    // Verified by the integrator against the local uGUI package rather than taken on trust:
    // Mask.IsRaycastLocationValid filters by RectangleContainsScreenPoint — the RECT only, never the
    // alpha — so the game's raycasts, on-card highlight and half-selection zones are untouched; and
    // StencilMaterial sets useAlphaClip = operation != Keep && writeMask > 0, so the mask really does
    // clip on the sprite's alpha rather than its rect.
    //
    // Fail-closed is structural: if the mask graphic ever stops writing, the children's stencil depth
    // collapses to 0 in the same walk and they render UNMASKED. There is no state where this clips a
    // card away. The one way it could have — a wrapper on a layer the head camera culls, testing
    // against a buffer nothing wrote — is closed by taking the face's own layer.
    //
    // Alongside it, PeelDarkBorder erodes an opaque near-black boundary band out of the captured
    // footprint (dark-only, depth-capped at 5 % of the short side, whole peel DISCARDED past 12 % of
    // the opaque area, boundary-seeded so a mid-card ornament is unreachable). On its own that was
    // cosmetically inert because the mesh sits behind opaque art; with the face clipped to the same
    // footprint it is what removes a PRINTED black frame. Its safety argument inverted with its
    // effect, so the threshold was retightened.
    //
    // CacheVersion 1 -> 2, and this is load-bearing rather than hygiene: the 109 run wrote a v1 file,
    // the cache is applied before the first card body exists, and a mid-session refresh is refused by
    // design — left at v1 the user would have tested round 5 and seen round 4.
    //
    // Peers take the same clip (Net/RemoteCardArt: wrapped at the build seam so a peer's card is
    // shaped from its FIRST drawn frame, again on art arrival for the cold-cache case, and RELEASED
    // before the clone is destroyed — required, not tidiness, since the wrapper becomes its parent).
    //
    // Build 109: the card SILHOUETTE round, plus three fixes whose root causes were each one layer
    // away from the symptom. No wire change; the bump is here because 109 is what goes to the friend.
    //
    // (1) THE BLACK CARD BORDER, fourth attempt and the first that names the right layer. The mesh
    // clip has worked since 496671e — the user's own report proved it: during a character switch the
    // loader takes the art down, nothing paints, and the border VANISHES, because the mesh behind it
    // is already clipped away. A clipped mesh cannot un-clip itself, so the remaining black is
    // painted by the adopted uGUI FACE, not by the mod's card body. Prime suspect, evidenced by the
    // capture's own count: 22 enabled sprite-less Images on ONE face, which uGUI draws as plain
    // colour quads — rectangles, so they can never carry the card's shape. Those are now muted, in
    // the SAME frame the art arrives (CardFace.Offer(artJustArrived:true), the seam CardFaceMipBake
    // already uses to swap sprites before a card's first drawn pixel), with the 1 s pass demoted to
    // a self-heal backstop.
    //   The user then narrowed the requirement: every placement, no visible transition, item cards
    //   too, and every card a PEER draws. The transition is answered by not learning the shape in
    //   that session at all — the footprint is PERSISTED to BepInEx/config and re-applied from
    //   inside the card body's own material factory, before any renderer that will draw it has a
    //   material. First launch after installing still has to learn it once; every launch after that
    //   is correct from the first pixel, on every path, because they all pass through that factory.
    //   Two uncovered local paths were found and fixed (the burn-fallback slab asked for the
    //   never-clipped Neutral pair; the authored-prefab branch could silently bypass the clip while
    //   every APPLIED line still printed). An inherited capture gate that would have latched a WRONG
    //   outline was removed: "after 12 fruitless offers take the largest candidate" is spendable in
    //   one frame, because a fan adopts up to 24 cards at once and each is an offer — and the
    //   largest candidate at that moment is an action half.
    //   MULTIPLAYER: two mirror sites the previous round missed (RemoteAvatar's HELD card slab, and
    //   RemoteBoardCard, which re-wraps only .mainTexture and needed the shape BOUND rather than
    //   fetched, since a spectator can see a peer's board before ever building a card). And the peer
    //   FRONT, which was the bigger hole and needed no wire: RemoteCardArt's pump passes the CANVAS,
    //   whose GameObject carries no card component — the clone is its child — so Offer's root-only
    //   classification never reached it and peers kept the full black rectangle.
    //
    // (2) HELD FIGURE SIZE followed the zoom. TickHeldScale pinned the mini's WORLD size, re-deriving
    // the anchor-local scale from the LIVE anchor every frame — and the diorama zoom IS the rig
    // scale the anchor hangs under, while the player's eyes scale with the same rig. Log: three grabs
    // at boardWorld=1 against anchorScale 41.368 / 41.368 / 10.149. The scale is latched once at the
    // grab now. Receive side reproduces the same ratio (zero wire bytes — the holder's rig scale
    // already rides every rig packet), and every release path restores the board-cell scale, because
    // the game re-authors a released figure's position and rotation but NEVER its scale.
    //
    // (3) THE SLOT A CARD LANDS IN was ranked by min(cardDistance, handDistance) — a correct
    // ELIGIBILITY test used as a RANKING key. A right hand leading a card to the LEFT recess is
    // nearer the right one, so the right recess's HAND sample undercut the left recess's CARD sample.
    // Eligibility is unchanged (reach moves 0 mm); the winner among eligible recesses is now the
    // card's nearest. Glow and drop cannot disagree: one decision function, two readers.
    //
    // (4) THE BOOT SPINNER is the GAME's symbol now, not the mod's ring. The previous round's
    // "LoadingScreen is serialized inside the scene being loaded" was true of that OBJECT and wrong
    // as a conclusion: the game shows a SECOND copy from the Intro scene, switched on a full frame
    // before the mod's arming edge (IntroPlayer.ShowLogos line 98 then 99), and the Intro scene
    // survives the window. Persisted copy → live Intro widget → procedural ring, in that order.
    //
    // Build 108: SIX reports in one round, run as six parallel agents on disjoint file sets. The
    // headline is the DEADLOCK; the pattern worth carrying forward is that three of the six were
    // defects in a layer nobody had looked at, not in the layer the symptom pointed at.
    //
    // (1) THE DEADLOCK — the round's critical item ("ich habe ein Skelet hochgehoben während es
    // dran war, dann ist plötzlich nichts mehr passiert"). No exception; a wait that never
    // completes, so nothing was ever logged. ActorBars hides a figure's worldspace-panel HOST
    // while its mini is held; AttackModBar's flow is a coroutine started ON that controller; Unity
    // kills a coroutine permanently when its GameObject is deactivated; FinalizeFlow — the only
    // writer of IsFlowActive = false — is that coroutine's last statement, so the flag latches
    // true; and Choreographer's WaitingForPlayerIdle waits on it with NO tick timeout, unlike
    // every neighbouring wait state. Turn machine dead, session over. Log: the rule engine's last
    // word is Player.log:18635, the attack at :18640 is the only one of seven with no matching
    // "finish", the grabs are at :18660 and :18695. THE FIND THAT SHAPED THE FIX: CAbilityAttack
    // waits on the attack's TARGET, not the acting figure — a guard that refused only "the figure
    // whose turn it is" would have missed this entirely. Now prevented structurally (FigureBusy),
    // the mechanism removed as well as the trigger (ActorBars no longer hides a bar whose flow is
    // live), and a watchdog behind both that repairs an untimed wait via the game's own UI seam.
    //
    // (2) MR PLATE FLICKER on the pile captions. ModBuild 107 fixed ONE label by moving its rank
    // into the Update pass; that does not scale, because the order writer is usually not the
    // registrant — the pile captions are ranked by the board FURNITURE band, which runs in
    // LateUpdate and must. Their plates were one frame stale BY CONSTRUCTION, and so were four
    // remote-board labels, the item-use caption and the keycap engravings. MrBacking now re-syncs
    // every plate from its content as the last act of TickPanelOrder, phase-blind.
    //
    // (3) PILE SYMBOLS — a SECOND defect at the same place, closed rather than distinguished on
    // the next run. Plates cannot reach them (opaque slabs, no plate on digits or cue). What they
    // share is PileViewer.SetVisible, hit by the hand == null branch during the pooled-hand
    // re-bind window — the same window builds 105 and 107 each had to defend a different surface
    // against. Hide debounced 2 frames, show immediate.
    //
    // (4) CARD X SPACING. HalfSelection docked its round cards through the spread-FREE slot-home
    // accessor — its only caller — while every other path takes the spread. A docked pair sat
    // 5.5 mm wider than the same pair placed during selection: small absolutely, but the gap
    // BETWEEN the cards is what the eye judges and that changed 42 %. The accessor is retired. A
    // peer's cards had the same omission; fixed by converting the seat through the board root,
    // because the local slot transform carries SlotScale and the remote prefab anchor does not.
    //
    // (5) NO TURNING WHILE SCROLLING. Two comments asserted turn and scroll cannot contend
    // because they read different AXES — true about the axes, false about the thumb: the shipped
    // mode is Smooth with a 0.2 deadzone. New ScrollTurnGate releases on the turn axis returning
    // to rest, not on the scroll ending, so the still-deflected stick cannot fire the very turn
    // being complained about 200 ms later. Fails open by construction. 42 new wire tests.
    //
    // (6) CARD SILHOUETTE. The alpha clip that removes the black rim had shipped in 6d7f1bb and
    // had NEVER RUN: the capture demanded img.isActiveAndEnabled while cards adopt their art
    // parked under an inactive pool root, and the rejecting branch logged nothing, so its retry
    // budget was burnt blind in the first few frames. Fixed, made trim-correct, and given a
    // per-KIND material set so item cards get their own outline; the peer mirrors opt in too,
    // because the 1:1 rule covers a card's SHAPE.
    //
    // Also: the startup freeze was MEASURED rather than assumed — 21.3 ms of a 2667 ms stall is
    // the mod, 62 % is the game parsing YML on the main thread, and the hands necessarily freeze
    // with it. The spinner now covers the window (it stands still at those frames too, and the
    // user was told so). The mod's OWN 981 ms boot stall is one LZMA inflate of the 58 MB bundle
    // and is now prewarmed onto Unity's loading thread. Full record: .planning/startup-freeze.md,
    // including the bundle rebuild the user declined.
    //
    // NO WIRE CHANGE in this build. The bump is here because 108 is what goes to the friend.
    //
    // Build 107: two defects the PREVIOUS round had already claimed to fix, both of which turned
    // out to have been fixed one layer away from where they actually live. That is the lesson of
    // this build and it is why both entries name the layer, not just the cause.
    //
    // (1) THE FIGURE HIGHLIGHT FLASHED ON FIGURE AFTER FIGURE. FigureGrabDriver does not raise that
    // highlight at all — ProximityGrabber does, on the nearest grabbable inside its CARD-sized 13 cm
    // palm reach that still passes AllowsHand. The driver's election is only a VETO, and the veto was
    // DISTANCE-GATED (`suppressed = inReach && !winner`), so every figure outside the palm sphere was
    // actively written back to ALLOWED. The grabber ticks in a different driver and reads those flags
    // one frame in arrears, so on the frame a figure CROSSED INTO the sphere it was the only allowed
    // candidate and won "nearest" by default: one frame of amber on the figure at the FAR EDGE of the
    // reach, walking from figure to figure as a hovering hand drifts. Hence "verschiedene Figuren",
    // and hence "zu weit weg" — the leak fired at 130 mm, not at the 40 mm pick radius. The hysteresis
    // and 6-frame dwell of build 106 stabilise the ELECTION, which had never lit those figures: the
    // log of the reported run shows 137 highlight-ENGAGED lines against 9 elections. The veto is
    // unconditional now and fails closed; ClearSuppression is gone with a note saying why.
    //
    // (2) THE PERSONAL-QUEST TEXT VANISHED FOR EXACTLY ONE FRAME. Build 106 hardened the POLL path,
    // but the text is re-derived every 0.5 s, so no poll-driven blank can last one frame — the user's
    // own wording ruled that layer out. Two one-frame RENDER paths were open and BOTH are closed
    // rather than told apart on the next run: (a) the label's draw order was written in the Update
    // pass AND again in LateUpdate, while its opaque MR backing plate copies that order in Update
    // only — so whenever the late value ranked lower, the plate (ZWrite on, TMP writes no depth)
    // painted over the glyphs for the frame in between. The rank is now written in the Update pass
    // exclusively, with a once-per-session runtime assertion that fires if the plate ever ends a tick
    // ranked apart from its label. (b) a single frame in which the tray mount reads
    // !activeInHierarchy takes the whole host panel down with the text; the hide is now debounced by
    // 2 frames (33 ms at 90 Hz) while the SHOW path stays immediate.
    //
    // No wire change in this build. The bump is here because 107 is what goes to the friend.
    //
    // Build 106: TWO rounds in one bump — 105 shipped, then a fix round rode on top unbumped
    // (76daf29) because it never reached the friend's machine. Both are in here.
    //
    // THE ROUND'S SUBJECT: the two blinking rectangles that mark where a hand card may be laid, and
    // the card that then lands in them, were sized by two unrelated numbers that could not be
    // brought into register. The card took [Cards] SlotCardFill = 1.45; the overlays took CODE
    // LITERALS off the card metric — the teal wanted-pulse 1.36, the gold snap glow 1.24 — so on
    // shipped defaults, in board metres, the card was 119.7 mm inside a 112.3 mm rectangle and
    // overhung it on all four sides by 6.6 %. There was no dial for the overlay at all, which is why
    // the one the user found ([Cards] ActiveCardScale_*) appeared to do nothing: that one sizes the
    // ACTIVE PILE through ActivePileViewer, and the active pile does not exist during selection.
    //
    // The POSITION half of this coupling had been built already (SlotOverlayOffset/Spacing feed both
    // the glows and SlotHomeOffsetFor); only the SIZE half was missing. It is now one per-board dial,
    // [Cards] SlotOverlayScale_{board}, seeded with SlotCardFill's 1.45 — so the card does not move
    // or resize by a hair, the teal grew 1.36 → 1.45 to meet it exactly ("exakt ausfüllen"), and the
    // gold keeps its shipped 1.24/1.36 = 0.912 ratio to the teal so it still reads INSIDE it when
    // both show. SlotCardFill is retired (ConfirmUndoSize pattern); no migration marker is needed
    // because the successor key is new and BepInEx binds it at its default.
    //
    // WHY IT NEEDS WIRE (field 171, FACTOR range): record 11 already carries the card's finished
    // WIDTH, but the peer's glows are not cards — RemoteBoardFurniture builds them from the recess
    // metric times a factor, and it needs the factor, not the product. Recovering it by dividing
    // record 11 would rest on a second dial travelling in step. The two literals over there are gone
    // with the local ones; the ratio is now a single shared constant, PlayTray.SnapGlowRatio.
    //
    // Also in this bump, from the unbumped round: quest text no longer blanks on a single empty poll
    // (CardsGameApi.ActiveHand() is null while the game re-binds a pooled hand, so "no goal" and
    // "ask again" were indistinguishable), and the figure-grab highlight no longer flashes (a bare
    // radius is a step function and the hand was drifting ON the boundary — now enter/exit hysteresis
    // plus a 6-frame dwell).
    //
    // Build 105: a multiplayer round, and its centre of gravity is that a peer's board was being
    // DESCRIBED to the other client instead of being SHOWN to it.
    //   * THE DECISION ROW. What crossed was a label string — the hardware log has it verbatim,
    //     "1 verfügbare Karte verbrennen|Schaden erhalten|2 abgeworfene Karten verbrennen" — from
    //     which the receiver rebuilt three flat plates. The owner's own widget dump in the same
    //     session lists what he actually sees: a damage icon, a fatal-damage icon, the damage
    //     AMOUNT ("4"), the toggle art and a mandatory highlight. None of that can live in a
    //     string. The reframing came from the game: TakeDamagePanel is a per-client Singleton and
    //     the game calls ShowOtherPlayer on every non-deciding client, so every peer ALREADY owns
    //     the whole widget tree, laid out and localised into its OWN language. So nothing about the
    //     look needs to travel — only what the game does not repaint there. New record 29 carries
    //     ROLES (which of three decisions each button is), four state bits and the damage number;
    //     the receiver resolves its own widgets and paints them. Each client therefore reads in its
    //     own language, which is more correct than shipping the sender's rendered text.
    //     Deliberately NO roles for pooled Yes/No or dialog buttons: those do not exist on a peer
    //     in the state their owner sees, so a role would resolve to the wrong object — they keep
    //     the plates. That is the FanCloseDuration trap avoided by construction.
    //   * THE ORDER BAND. Everything on a mirrored board sat on a STATIC sub-ladder (0/4/8) while
    //     the panel ladder starts at 100 — so a peer's board could never rank against anything,
    //     and its own internal order was equally fixed: the synced tooltip is authored 2.8-5.0 cm
    //     BEHIND the initiative mirror on Oak and Steel and in front of it on Bronze, and one
    //     constant cannot answer both. The board now joins the furniture-cluster machinery with a
    //     depth tier per element, so the Steam picture, the remote tooltips and the initiative
    //     track resolve against each other AND against the panels, in both directions.
    //   * THE CARD ALIASING was not the bake being slow — it was the SWAP being late. The mod
    //     rescanned for mipped art on a 1 s timer (MipRescanInterval), which is the reported
    //     second, to the constant. The swap now happens in the frame the loader assigns the sprite,
    //     which is a frame where the loader still holds the Image DISABLED — so the bake runs where
    //     the player cannot see it without anyone having to predict which cards are coming.
    //   * THE REFUSAL SOUND was keyed on OWNERSHIP while free character focus had made looking at
    //     another player's character a SUCCESS. The peer's log has the three lines in a row: click
    //     allowed, read-only view opened, refusal sound played. It is keyed on the click's OUTCOME
    //     now — and a second, unreported defect fell out with it: on a genuine refusal the mod was
    //     stacking its sound ON TOP of the game's own.
    //   * A placed item card now owns the generic cluster (only USE remains) by SUPPRESSION rather
    //     than save-and-restore: the game may add or withdraw a decision while the card lies in the
    //     recess, and restoring a snapshot would resurrect a dead button or swallow a new one.
    // Wire: Version stays 3, record 29 is additive TLV, no card identity. Assertions 1395 -> 1441.
    // Build 104: the zoomed-out performance round, and the whole of it is invisible by ruling —
    // the user declined every strategy that trades look for speed, so nothing here may change a
    // position, a paint order, a fade or a timing.
    //   * FIVE PERIODIC SWEEPS WERE ONE DEFECT. Object.FindObjectsOfType<T>() is O(every loaded
    //     object), not O(objects of that type), and three independent hardware readings price ONE
    //     call in the big room at 10-15 ms: UnseenTiles.Rescan is 20.2 ms and carries one, plus two
    //     short subtree walks; Compat.LoaderHeal is 23 ms and carries one; WallFade's rescan is
    //     50-97 ms and carries THREE. Seven calls a second is the ~89 ms/s the analysis measured,
    //     delivered as 20-100 ms hitches - the judder itself, as opposed to the frame rate. Now
    //     three self-maintaining registries (Core/SceneRegistry.cs) filled by Harmony postfixes on
    //     the components' own lifecycle methods, seeded at install and applying the SAME active +
    //     hideFlags filters, so a read can neither miss nor widen a caller's set. Fail-open: a
    //     registry whose patch did not take falls back to the sweep it replaced.
    //   * And two QUADRATIC bodies inside the wall rescan, which no registry would have touched:
    //     the figure test ran three GetComponentInParent walks for each of ~3000 renderers (now
    //     memoised per pass, exact because the answer is a property of the chain and no game code
    //     runs inside a synchronous pass), and the segment-listed test was O(candidates x segments
    //     x list) (now one hash index built per pass from the same five lists).
    //   * The world UI's per-frame work: change gates that skip a write only on BIT-IDENTICAL
    //     values (no epsilon anywhere - an epsilon would hold a permanent sub-epsilon error, which
    //     is a look change however small), frame-constant reads hoisted, the ladder's per-frame
    //     instance-id hash moved behind the 1.5 s throttle it only ever fed, and ~20 bars' 2 s
    //     depth rescans de-synchronised - they all started at zero and stepped by the same amount,
    //     so they landed in ONE frame forever. The stagger SUBTRACTS, so no bar is ever scanned
    //     later than before.
    //   * THE INSTRUMENT COULD NOT SEE ZOOM AT ALL, which is why the analysis had to correlate 10 s
    //     heartbeats against 30 s windows by hand. FRAME/SPLIT now carry a viewpoint axis and
    //     bucket each window's frames into near/middle/far thirds with their own p50s. The visible-
    //     renderer count was ONE instant per 30 s window (1925 vs 2129 is noise, not signal); it is
    //     now seeded by that same walk and refreshed 64 renderers per frame, round-robin.
    // No wire change; the bump is the handshake key. Wire assertions 1395.
    // Build 103: six hardware reports, and three of them were the SAME defect class wearing
    // different clothes — a transparent that decides its paint order from something that moves.
    //   * The tooltip that flashed on the board while the laser swept the hand fan was never raised
    //     by the laser. The game's EventSystem MOUSE runs every frame at the PARKED desktop pixel,
    //     and its RaycastAll reaches the mod's WORLD-SPACE panels, whose camera is the HEAD. A fixed
    //     pixel through a moving head is a world ray that sweeps the room with no user input at all;
    //     every crossing of a tooltip target raised the game's one shared tooltip. The log proves it
    //     twice over: the beam was PROVABLY off the panels those seconds ('occluded by the raised
    //     card fan') while tooltips published anyway, with no hover ENTER from any mod pointer. Same
    //     defect CardFaceRaycaster was written for on card faces, one surface family later. Guarded
    //     at the RAISE: on a world-space canvas only a mod pointer may raise a tooltip, and while a
    //     beam is on ANY fan nothing may raise one at all.
    //   * The character quest line drew at order 0 — it is a scene-ROOT object that pose-follows the
    //     objectives host, so the furniture adopter, which walks the tray SUBTREE, never saw it. It
    //     was the last unranked transparent on the board; the info card it lost to sits at 148..276.
    //   * The fog tiles still shimmered because round 2 fixed the fight INSIDE a hex and never asked
    //     about the fight BETWEEN hexes. Per-hex settle gates let neighbours re-seat on different
    //     frames, and two hexes answering two different snapshots can invert — a visible seam, since
    //     the kit's hexes overlap in screen space and write depth. The field now decides as a FIELD:
    //     one snapshot, every hex, applied atomically, so inversion is unrepresentable. Plus: a
    //     mid-swap ladder is CONTRADICTORY (higher order to something farther) and was being
    //     answered instead of waited out, and the ladder's 2 cm dead band is NARROWER than a seated
    //     player's head sway, so the query point is now held until the eye really travels 8 cm.
    //   * The invisible buttons, third report, were never an animation defect — both earlier fixes
    //     were right and both stayed SILENT in this log (zero heal lines). The cap FACE is tinted by
    //     [ButtonColors]; the WELL it sits in is not. At this user's tint of 0.5 a disabled face
    //     lands at 0.105 against a 0.15 well: darker than the hole it sits in, no silhouette left,
    //     and the label — a separate renderer no tint touches — keeps drawing. Pure arithmetic
    //     (0.21 x t < 0.15 for any t < 0.714), which is why it is a steady state and survived three
    //     builds. A cap face is now floored against its own well; the contrast step is READ OFF the
    //     shipped palette (the darkest authored face already beats the well by 1.37x).
    //   * The decision area's 2 s collapse was the shared fit machinery's SHRINK damping (0.5 s
    //     stable + 1.5 s min interval + throttle); growth was always immediate. The dock opts out —
    //     it holds the fit frozen on layout truth, which is strictly better than a clock. Its
    //     'starts too low' was a second, independent defect: the fit pads the content by 12 px and
    //     centres it, and three surfaces seated themselves from the PADDED edge, each handing the
    //     error to the next — the same sag three times over.
    //   * The figure pick volume was the interactor's 13 cm PALM reach — a hand-span, chosen for the
    //     card fan, where a card is a hand-span wide. A mini is not. It was correctly anchored to the
    //     hand already; what it was not is small. 40 mm from the pinch point now, [FigureGrab]
    //     PickRadiusMillimeters, and 130 restores the old reach exactly.
    // No wire change; the bump is the handshake key. Wire assertions 1389 -> 1395.
    // Build 102: the arm HUD turned around, and the previous round's turn-around was half a turn
    // short. Build 101 replaced the base rotation with the IDENTITY on the finding that wrist +Z
    // points out of the palm - which the prefab YAML confirms three times over (Anchor_Middle_Root
    // 9.6 cm up wrist +Y to the knuckle; Anchor_Palm half way along that line with Anchor_Grab a
    // centimetre further along +Z; and Anchor_Palm's own half turn about (0,1,1), which maps the
    // +Y the Hands README requires to point OUT of the palm onto wrist +Z). What it got wrong is
    // WHICH FACE OF A CANVAS READS: uGUI reads from -Z, not +Z. An identity plate therefore aimed
    // its readable face out of the BACK of the hand while the new gate revealed it from the PALM
    // side, so the player was shown the canvas's back - and uGUI does not cull it, it draws it
    // mirrored. Hence 'ich sehe das HUD jetzt spiegelverkehrt'. Base is now a half turn about
    // wrist +Y (readable -Z out of the palm, text top still on the fingers) and the look-at gate
    // reads that same readable face, so the two cannot drift apart again.
    //   * The 'readable +Z' note it trusted came from commit 3cc7ac8, which measured a plate whose
    //     +Z pointed along the FINGERS - a screenshot from there cannot tell the two sides apart.
    //     Three independent sources say -Z: PanelPlacement.Facing, VRCard, and Unity's own default
    //     (identity canvas, camera at negative z). So do the user's own pre-turn-around trims:
    //     LookRotation(up, forward) with pitch -102 / yaw -180 composes to within 12 degrees of the
    //     identity, i.e. THAT plate's +Z pointed out of the palm, away from a player reading it off
    //     the back of their hand. The build log now prints readable-dot-palmOut (+1 correct, -1
    //     mirrored) so the next report is answerable from the numbers.
    //   * 'Ich finde die Offsets nicht mehr wo sie vorher waren': the rows the user had been
    //     reaching for were the six [WorldUI] WristHud* twins, retired last round because they were
    //     decoys. The live per-style rows are in the HANDS topic and always were; their block was
    //     headed 'Handgelenk' while every row in it reads 'Arm-HUD: ...'. The heading is now the
    //     widget's name in both languages.
    // No wire change; the bump is the handshake key. Wire assertions 1389.
    // Build 101: three reports that all came back to ONE resolver, plus the fog tile and the
    // tooltip plate.
    //   * 'der X-Offset beim Arm-HUD hat keinen Einfluss' was not an orphaned dial - it was an
    //     UNSTEPPABLE one. ConfigSteps.TryUnit matched the unit word as a SUFFIX, so any key ending
    //     in a bare axis letter never matched and fell back to 'a fiftieth of the shipped default'.
    //     GloveOffsetX ships -0.003 and stepped 0.05 mm; its sibling OffsetY ships -0.053 and
    //     stepped 1 mm. Third report in this family, so the resolver was fixed rather than one more
    //     set of keys: 73 of 351 dials changed step, sibling disagreement within a vector 28 -> 4.
    //     [RoundButtons] OffsetX - the dial reported two rounds ago - went 1 mm -> 10 mm.
    //   * The arm HUD moved to the palm. HandRig.Wrist is NOT in the Root frame and four rounds of
    //     that file assumed it was: the anchor carries +90 about X, so wrist +Z is out of the PALM.
    //     That is why every per-style trim was a ~-90 pitch - undoing the missing 90 by hand.
    //   * The fog hex takes ONE decision per hex instead of one per renderer (the kit puts three on
    //     each, with AABBs 10-20 cm apart, so two surfaces of one hex straddled a ladder breakpoint
    //     and swapped paint order - that flickers with a still head), and it no longer parks in MR,
    //     where the opaque backings sat at order 0 against a board band at 143..291: an opaque
    //     surface painted BEFORE what it should hide hides nothing.
    //   * The tooltip's MR plate outlived its tooltip by the fade plus the placement latch - 0.4 +
    //     0.5 s - and collapsed into a full-width, near-zero-height bar when the game reset the
    //     rect. That is the reported streak. It now fades with the box.
    //   * The avatar mask is a dropdown: MaskId is an int bounded by a RANGE, and a range is not a
    //     value LIST, so the row classifier drew a three-position slider labelled 0/1/2.
    // No wire change; the bump is the handshake key. Wire assertions 1121 -> 1389.
    // Build 100: the multiplayer performance collapse, and it was TWO defects that had been
    // invisible to the instruments rather than one expensive feature.
    //   * THE ESCALATION was an unbounded ring leak on the mirrored initiative track: the hover
    //     cache is invalidated every content tick, EnsureHoverCache built two rings per entry every
    //     time, and StructureMatches only walks the SOURCE - so a ring added to the CLONE had no
    //     detector and the old pair was merely dropped from a List. 8 entries x 2 x 4 Hz over ~20
    //     min is ~77k objects at ~1.5 KB = ~115 MB, against +112 MB measured in the post-GC heap
    //     floor. The scene census missed it because it counts Renderer and a uGUI Image is a
    //     CanvasRenderer; the frame split missed it because canvas rebuilds run in
    //     PostLateUpdate, after the tail LateUpdate and before the camera callbacks - in NEITHER
    //     measured span, so the whole cost fell into the remainder labelled 'blocked'.
    //   * THE FLOOR was the input-field watch, dead since the commit that added it: it asked
    //     Harmony for DeactivateInputField() with no parameters, but this game ships TMP 3.0.x
    //     where the only declaration takes a bool. The lookup returned null and degraded both
    //     seams, so the FindObjectsOfType sweep it existed to KILL never stopped running - 90-99
    //     ms of every second, on both machines, with or without a peer.
    // The reported host/peer asymmetry is not in the data: the peer's own log ends at 105 ms mean
    // frametime after the identical slide from 68. Both mirrored, both leaked, both collapsed.
    // Also this build: a see-through tile now shows the WHOLE board behind it (the unseen material
    // is queue 3000 at sortingOrder 0 with a HARDCODED ZWrite, so it painted first and stamped
    // depth over everything drawn later); my own focus ring stops being cloned onto peer boards;
    // and the hand fan's character-swap exchange finally mirrors - its replay machinery was
    // complete, but the receiver's trigger read an id that deliberately does not move during the
    // secret card-selection phase, which is exactly when a two-character player swaps hands.
    // Build 99: the shipped defaults are re-based onto the user's tuned setup (21 values, board
    // seat and tray pose dominating). Three reports, two of them dials with a hole on one side:
    //   * 'die Offsets bei den Ueberspringen-Tasten haben keinen Einfluss' — the [RoundButtons]
    //     offsets were live all along; the dial he was turning is [Cards] ClusterOffset in the
    //     same debug block, and THAT was orphaned: the rigid-dock lag fix reparented the cluster
    //     from its mount to the tray root and carried the mount's rotation and scale but not its
    //     TRANSLATION. The mount kept moving; it had stopped having children. It is now a delta
    //     off the mount's own transform (wire id 16). Fell out with it: the cluster no longer
    //     tears itself down on EVERY tuning move — that teardown was a cap vanishing and
    //     reappearing with no crumble and no assemble.
    //   * the card lying in the item-use recess now lifts and highlights like a slot-docked card.
    //     Both contact signals already reached it; ItemChip.Update returned before any pose work
    //     while PendingUse was set, so it was the one card that could be hovered and never showed
    //     it. The pop rides IGrabHighlight, so buzz and lift are the SAME event.
    //   * and it stops VANISHING from peers when the owner closes the fan: record 26 was derived
    //     from ItemsPile.Current, which Close() nulls. The receiver had been built to survive that
    //     close — so its whole _clipDetached re-adoption path had never once run.
    // Build 98: keycap COLOURS and SHAPES ride the wire (user: "so dass das remote Board 1:1 das
    // anzeigt was der Spieler sieht"), and the audit note that had filed them as "a look decision,
    // not a wire gap" turned out to be hiding the real defect: the mirror read NONE of the colour
    // dials, so the shipped 0.5 cap-face TINT — which the local cap multiplies its palette by — was
    // never applied and EVERY mirrored keycap was twice as bright as the cap it copies, for two
    // untuned players. Three more drifts alongside it, the worst being that the label keyline was
    // read from the VIEWER's config, so recolouring your own grew every team-mate's, on your screen.
    // A new COLOUR width was carved out of the vec range's unused tail — 6 ids / 24 bytes against
    // 18 / 54 — because the id space is the only bound this record still has. Shapes were wired WITH
    // their renderer, never before it, retiring last round's blocker. PENDING debts 46 -> 16.
    // Also: the item pile follows the FOCUSED character (the ModBuild 89 display/action split
    // surviving in the one stack it missed), and the offered-bonus cue COULD LEAK ACROSS CHARACTERS
    // — it matched on CItem.ID, which is the item CARD id shared by every copy of that item; the
    // game's own by-id lookup is safe only because it searches ONE inventory. Now scoped by the
    // bonus's own Actor. Page count unmoved; convergence still <=400 ms.
    // Build 97: the Skip cap is not a PlayTray BoardButton but a ButtonCluster member, so the
    // per-character ownership machinery of builds 84-91 had never applied to it — it now computes
    // the SAME four facts from the same sources as ConfirmCapsForeignView, nuance included. Its dial
    // family ([RoundButtons], which already WAS its family — a second section would have meant two
    // sections driving one cap) moved onto the board topic and, with three sibling families, onto
    // the wire: 19 dials, PENDING debt 65 -> 46, page count unchanged so convergence stays <=400 ms.
    // The item chips now lift, glow and buzz like ability cards — the chip's lift had no UPWARD
    // component at all and its haptic hung off the grabber's highlight rather than the fan election,
    // so buzz and lift landed on different chips at different moments and read as nothing.
    // The laser stand-down reaches every fan: the browse arcs were structurally dead, their election
    // grab-gated twice down to IsLocalHand — so reaching into ANOTHER PLAYER'S discard pile could
    // never stand a beam down. And the contact test's lateral slack got an absolute floor: it was
    // purely proportional, which silently made the near-square item card (2.2 mm of rim) harder to
    // touch than an ability card (3.5 mm), and 9 of 26 logged contacts were captured under 1.5 mm —
    // grazes, not holds. Tracking noise does not shrink because the card did.
    // No wire FORMAT change; record 28 grew inside the paging scheme built in build 96.
    // Build 96: the item area gets its animations back and the 1:1 tuning guarantee gets teeth.
    // LOCAL: the item-flow caps were DESTROYED and RECREATED on every clip-in (33 board builds in
    // one session, 29 of them within three lines of a clip-in) — construction is the one transition
    // an authored crumble/assemble can never cover; the caps now live and animate. A cap assembles
    // out of warm dust instead of ramping its colour up from a 0.15 floor, whose product with the
    // user's 0.5 tint was black on a black board — the invariant that replaces the floor is a
    // per-channel max with the rest colour, so NO tint can render a cap darker than its own rest.
    // The placed item card stays in the recess until the item's action has actually resolved (four
    // live reads, no ledger — chief among them that UseItemService does not resolve anything, it
    // ENQUEUES). The usable-pile cue runs on a heartbeat with a REST in it and throws rings, because
    // peripheral vision is a transient detector and passthrough contains nothing that changes SIZE.
    // The item berth's dark plate is gone: it hangs BELOW the board's slab, so in MR its backdrop is
    // the player's room and a dark plate is a hole, not a rectangle.
    // WIRE: record 28 is RANGE-PAGED — pages are complete statements about an id range, tiling the
    // space, so absence still means "at default", a lost page self-repairs, and there is no ceiling
    // left to hit. Convergence: complete state by T + pageCount x 200 ms (≤400 ms today). The audit
    // behind it found 11 dials that never rode at all and one bug that hit EVERY player: the
    // mirrored item fan's radius was re-typed from the wrong default, 12 % too wide. New guard
    // scripts/check-wire-coverage.py fails the build when a board-affecting dial has neither wire
    // coverage nor an annotated opt-out — it caught the ten new item-cue dials on its first run.
    // Also new on the wire: the usable-pile cue (board-UI record 4 byte 2 bit 7 — the LAST free bit
    // in that record) and the re-arted mirrored berth.
    // Build 95: the last item BUTTONS are gone — an active bonus whose BaseCard is a CItem and
    // which needs no further option is now PLACED (card into the recess, USE presses the game's own
    // row through ToggleActiveBonus) instead of pressed, and taking the card back out is the
    // un-click. Build 94 had kept these on the grounds that UseItemService refuses passive items;
    // the user rejected that and was right — it is a statement about ONE seam, and the bonus is
    // built off the item card. What stays in the decision area: the three option-bearing kinds
    // (initiative ±N, forgo-for-companion, choose-ability), an element consume, a mandatory bonus,
    // and every bonus with no card at all. No wire change — ToggleActiveBonus sends the game's own
    // ClickActiveBonusSlot when online and ProxyUseActiveBonus replays it.
    // Build 94: hardware round, 13 reports, no wire format change at all (the first such round in
    // a while — every fix landed in rendering, input policy or local state). The four that were
    // mis-diagnosed before and are now proven from the log: the mip-bake budget counted SLOTS, so
    // menu chrome ate all 48 sprite plates before the card FRAME art loaded (AC_*_Background,
    // AC_Enemy) and 32/32 left four enemy portraits mipless — the budget is bytes now; the item
    // recess CANCEL called UnclipChip on a chip a hand already held, re-parenting it out of the
    // hand so TickHeldPose pinned it a few cm off the fan root for as long as the trigger was down;
    // BoardTargeting's interactor policy granted Ray|Poke but not PalmGate, so 'fanBuffer=8,
    // gateEnabled=False' — eight cards built with no way to reveal them; and the keycap appear fade
    // ran on the SCALED clock with its only exit inside its own countdown, so a cap stranded at
    // 15 % of its colour stayed black-on-black until the game state happened to flip.
    // Also: TurnActor follows whose TURN it is rather than which figure acts (a hero summon is not
    // a CPlayerActor, so nobody's cards docked); pile counts follow the card's ARRIVAL, ledger-free;
    // menu tooltips pin their frame instead of trusting the game's pixel offset read as metres; the
    // tooltip backdrop gets an opaque plate under the game's own frame art; a seam wall belongs to
    // BOTH rooms it separates; and low water is permanently exempt from the fade.
    // Build 93: hardware round — the item chip's LEFT-hand pose finally gets ba70e43's mirror (its
    // copy of GetHeldPose predated the fix); a hand in physical contact PLUCKS the placed item card
    // instead of running the far-laser's put-it-back branch through the shared IPokeable entry; the
    // placed card outlives the fan close and cancels through one state machine with three animated
    // entrances; the hand fan EXCHANGES hands on a character switch (there was no swap animation at
    // all — the park sweep teleported the old cards away in the same frame); and the game's own
    // refusal sound answers a click on an unplayable initiative portrait.
    // WIRE: record 28 gains the 8 fan-swap dials and is now at EXACTLY 255 payload bytes — the
    // extension tail's one-byte per-record length ceiling. THE NEXT FIELD IN RECORD 28 MUST FREE
    // BYTES FIRST; the sampler logs loudly and refuses the record rather than truncating it, and
    // two wire tests pin the 255-out / 256-dropped boundary. Wire Version still 3.
    // (The note list below lapsed around build 36 while the counter kept climbing; resumed here
    // because this build claims a wire id, and a claim nobody wrote down is how record 23 got
    // taken twice. Notes are newest-first.)
    // Build 92: hardware round — the laser stands down only on a REAL card touch (an election is
    // reach-scored to 13 cm, not a touch); ONE grip chord for every physical fingertip press,
    // gated before the ARMING so a deliberate-withdrawal dock cannot fire without it; the MR plate
    // measures only what the panel FIT judged visible (it was unioning back the culled/faint rows
    // the fit had rejected) and free-floating card cues rank against the plate ladder by measured
    // eye distance; the item-use recess seats its card square and hands it back to either hand;
    // the item fan opens with presence. WIRE: record 28 gains the item fan's 8 animation dials
    // (the 1:1 ruling names animations), and NEW RECORD 26 carries the fan index of the chip
    // clipped into the use recess so peers see it LYING there. Additive TLV, wire Version still 3.
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
    /// Extension record id: the sender's live BOARD-UI STATE — 3 bytes,
    /// <c>[byte0 buttons][byte1 overlays][byte2 cap states]</c>. This is what makes a peer's copy
    /// of a control board show EXACTLY the controls its owner currently sees (user requirement:
    /// the remote board used to draw ALL buttons permanently), plus the "wanted slot" glow state
    /// so the teal blink is synced (the blink ANIMATION stays local-clock driven at the shared
    /// period — synced state, locally animated, zero per-frame traffic).
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
    ///   bits6..7 <see cref="BoardUiSnapMask"/> — the SNAP-GLOW HOVER telegraph, i.e. which recess
    ///            the owner's own gold "the held card lands here on release" rim is lit on
    ///            (<see cref="BoardUiSnapNone"/> 0 = none, 1 = slot 0, 2 = slot 1, 3 invalid).
    /// byte 2 (cap STATES — <see cref="BoardUiCapStateDefinedMask"/>; see
    ///   <see cref="BoardUiCapConfirmAccentBit"/> for the whole argument and the layout).
    ///
    /// Unlike the "only when non-default" records, this one is written on EVERY extras packet
    /// that also carries a board pose: the receiver must distinguish "the owner's board shows
    /// no dynamic controls" (record present, byte0 = 0) from "the sender predates the field"
    /// (record absent → the receiver keeps the legacy always-drawn furniture, so a build-1 peer
    /// looks exactly as before). ~5 bytes at 5 Hz. No card identity is derivable from any bit —
    /// the wanted mask reveals only "slot still empty during selection", the occupancy nibble
    /// only its complement (both already visible through the board's own card backs and vanilla's
    /// ready tracker), and the snap field only WHICH RECESS a card the peer is already watching
    /// the owner carry is about to land in.
    /// </summary>
    public const byte ExtIdBoardUi = 4;

    /// <summary>Payload length of <see cref="ExtIdBoardUi"/> BEFORE the cap-state byte — the
    /// length every build up to ModBuild 88 wrote, and the minimum a reader requires.</summary>
    public const int BoardUiRecordBytesLegacy = 2;

    /// <summary>Payload length of <see cref="ExtIdBoardUi"/> WITH the cap-state byte. The LENGTH
    /// is this record's validity flag for byte 2 — see
    /// <see cref="BoardUiCapConfirmAccentBit"/> for why it needed one and why no bit was
    /// spent on it.</summary>
    public const int BoardUiRecordBytes = 3;

    // Board-UI record byte 0 (buttons) bit assignments — wire constants, append-only.
    public const byte BoardUiConfirmBit = 1 << 0;
    public const byte BoardUiUndoBit = 1 << 1;
    public const byte BoardUiItemRecessBit = 1 << 2;
    public const byte BoardUiItemUseCapBit = 1 << 3;
    public const byte BoardUiShortRestBit = 1 << 4;
    public const byte BoardUiLongRestBit = 1 << 5;
    public const byte BoardUiSkipBit = 1 << 6;

    /// <summary>Board-UI byte 0 bit 7 — the decision DRAWER is out on the owner's board: a prompt
    /// is docked AND the owner can actually see it. The second half is not pedantry: since the
    /// 2026-08-08 ruling ("… so wie der Spieler sie sieht") a row render-hidden for another
    /// character's focus clears this bit, so a peer's copy shows the same bare seat the owner's
    /// board shows rather than an empty drawer for a decision that is not on screen.</summary>
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

    /// <summary>
    /// Board-UI record byte 1, bits 6..7 — the SNAP-GLOW HOVER TELEGRAPH: which recess the owner's
    /// own gold "the held card lands HERE on release" rim is lit on right now
    /// (<c>PlayTray.SetHighlightedSlot</c>, driven from <c>CardsDriver.UpdateSlotHighlight</c>).
    /// <see cref="BoardUiSnapNone"/> (0) = no rim lit; 1 = slot 0; 2 = slot 1; 3 is invalid and
    /// reads as none (never trust the wire).
    ///
    /// <para>WHY IT IS ON THE WIRE, having been declared IMPOSSIBLE. The previous revision of
    /// <c>RemoteBoardFurniture.Refresh</c> lit the mirrored rim on the OCCUPANCY edge instead —
    /// empty→occupied, then a 0.6 s fade — with the note "a remote hover is not reproduced (and
    /// cannot be)". Both halves of that were wrong. It is not the same event: the owner sees the
    /// glow BEFORE the drop (it is the telegraph that tells them where the card will go, and it
    /// tracks their hand across the two recesses and back to none), a peer saw it AFTER, and a
    /// hover that ended without a drop produced no glow on the peer's board at all. And it was
    /// never impossible: the hovered slot is one small integer that the owner's own board already
    /// renders, and the record it belongs in had two reserved bits sitting in the very byte the
    /// wanted-glow mask rides. It costs ZERO extra bytes.</para>
    ///
    /// <para>ANTI-CHEAT: the field names a RECESS, never a card — the same class of fact as the
    /// occupancy nibble two bits below it, and strictly less than that nibble reveals (the peer is
    /// already watching the owner carry the card; this says which of two public recesses it is
    /// heading for). A sender that predates the field writes 0, which reads as "no rim", i.e. the
    /// receiver falls back to its legacy occupancy-edge flash exactly as before.</para>
    /// </summary>
    public const int BoardUiSnapShift = 6;

    /// <summary>Mask of the snap-glow field in the board-UI record's byte 1 (bits 6..7).</summary>
    public const byte BoardUiSnapMask = (byte)(0x03 << BoardUiSnapShift);

    /// <summary>Snap-glow field value: no recess rim is lit on the owner's board. Also what an
    /// out-of-range value (3) and a sender predating the field decode to.</summary>
    public const byte BoardUiSnapNone = 0;

    /// <summary>Encode a highlighted slot index (-1 = none) into the board-UI snap field's VALUE
    /// (before shifting): none → 0, slot i → i + 1. A slot the two-recess board does not have
    /// degrades to none rather than lighting the wrong rim.</summary>
    public static byte EncodeSnapSlot(int slot) =>
        slot >= 0 && slot < BoardUiSlotCount ? (byte)(slot + 1) : BoardUiSnapNone;

    /// <summary>Decode the board-UI snap field's VALUE back to a slot index, or -1 for none /
    /// the invalid value 3 / a recess this board shape does not have.</summary>
    public static int DecodeSnapSlot(int value) =>
        value >= 1 && value <= BoardUiSlotCount ? value - 1 : -1;

    /// <summary>Every DEFINED bit of the board-UI record's byte 1 (wanted glow + pinned + slot
    /// occupancy + its validity bit + the snap-glow hover field). The writer masks with this so
    /// undefined bits can never be pre-claimed by garbage, and the reader masks again (never trust
    /// the wire). Widening it is how the next overlay bit ships — and it is why old readers, which
    /// mask with the narrower <see cref="BoardUiWantedMask"/> (or with this constant's previous
    /// 0x07 / 0x3F values), ignore the new bits instead of mis-reading them.</summary>
    public const byte BoardUiOverlayMask =
        (byte)(BoardUiWantedMask | BoardUiPinnedBit | BoardUiSlotMask | BoardUiSlotsValidBit
               | BoardUiSnapMask);

    /// <summary>
    /// Board-UI record BYTE 2, bit 0 — the owner's CONFIRM keycap is ACCENTED.
    ///
    /// <para>WHAT THE WHOLE BYTE IS FOR. <c>PlayTray.BoardButton.SetState(enabled, accent,
    /// confirmed)</c> gives every board keycap three independent visual states, painted onto the
    /// cap's three submesh materials by <c>SetCapColor</c>: DISABLED (dark wood), IDLE (warm
    /// parchment), ACCENT (the cap's own authored accent) and CONFIRMED (worn brass). A peer's
    /// mirror rendered exactly ONE of them — the colour the cap was BUILT with — so a greyed-out
    /// rest disc, a brass-accented pick-flow CONFIRM and a gold readied CONFIRM were all the same
    /// picture on every other screen. That is the "alles … so wie der Spieler sie sieht" rule
    /// failing on the most-looked-at furniture on the board.</para>
    ///
    /// <para>WHY IT IS A THIRD BYTE ON RECORD 4 AND NOT A NEW RECORD. These bits QUALIFY the
    /// visibility bits of byte 0 — "the CONFIRM cap is shown" and "the CONFIRM cap is readied" must
    /// never arrive in different packets, or a peer paints a state onto a cap that is not there
    /// (or worse, misses the state edge of a cap that just appeared). One record, one packet, one
    /// atomic write. It also costs 1 byte where a new record costs 3 (id + len + payload).</para>
    ///
    /// <para>WHY NO VALIDITY BIT. Unlike the occupancy nibble, this byte gets its validity for
    /// free from the TLV LENGTH: a sender that predates it writes
    /// <see cref="BoardUiRecordBytesLegacy"/> and the receiver, which requires
    /// <see cref="BoardUiRecordBytes"/> before it trusts byte 2, keeps the legacy
    /// built-colour look. An OLD reader is symmetrically safe — it validates <c>len &gt;= 2</c> and
    /// steps over the record by its own length byte, so the extra byte is invisible to it rather
    /// than shifting its tail walk.</para>
    ///
    /// <para>WHAT IS NOT HERE, and why. The UNDO keycap and the item-USE cap are CONSTANT-state
    /// controls at their source — every <c>SetState</c> call on them in the whole mod is
    /// <c>(enabled: true, accent: false)</c> for UNDO (PlayTray.5.Status) and
    /// <c>(enabled: true, accent: true)</c> for USE (PlayTray.6.Build) — so their look is a build
    /// fact, not a state fact, and the mirror reproduces it with zero bits. Likewise CONFIRM is
    /// never DISABLED: the board HIDES an unpressable confirm rather than greying it ("wenn es
    /// nicht drückbar ist dann soll es dort auch nicht erscheinen"), which byte 0 bit 0 already
    /// carries. And the FOLLOW/PIN cap's accent is byte 1 bit 2, where it has ridden since the
    /// pinned bit shipped.</para>
    /// </summary>
    public const byte BoardUiCapConfirmAccentBit = 1 << 0;

    /// <summary>Board-UI byte 2, bit 1 — the owner's CONFIRM keycap is in the CONFIRMED (readied,
    /// worn-brass) state, i.e. pressing it REVOKES. Beats the accent bit exactly as
    /// <c>BoardButton.StateColor</c> does.</summary>
    public const byte BoardUiCapConfirmReadyBit = 1 << 1;

    /// <summary>Board-UI byte 2, bit 2 — the owner's SHORT-rest disc is ENABLED (a short rest is
    /// available: <c>CardsGameApi.CanShortRest</c>). Clear while the disc is up but dead, which the
    /// owner sees as the dark-wood disabled cap — the state <c>RestControls.TickStatus</c> paints
    /// when a rest is SELECTED but no longer available.</summary>
    public const byte BoardUiCapShortRestEnabledBit = 1 << 2;

    /// <summary>Board-UI byte 2, bit 3 — the owner's SHORT-rest disc is ACCENTED (that rest is
    /// SELECTED — the commitment readout).</summary>
    public const byte BoardUiCapShortRestAccentBit = 1 << 3;

    /// <summary>Board-UI byte 2, bit 4 — the owner's LONG-rest disc is ENABLED. Same contract as
    /// <see cref="BoardUiCapShortRestEnabledBit"/>.</summary>
    public const byte BoardUiCapLongRestEnabledBit = 1 << 4;

    /// <summary>Board-UI byte 2, bit 5 — the owner's LONG-rest disc is ACCENTED (selected).</summary>
    public const byte BoardUiCapLongRestAccentBit = 1 << 5;

    /// <summary>Board-UI byte 2, bit 6 — the owner's turn-flow SKIP cap is INTERACTABLE. The
    /// cluster's own disabled look is not a palette swap but a 0.75 lerp of the accent toward dark
    /// wood (<c>ButtonCluster.PhysicalButton.SetState</c>), plus a 0.35-alpha label; the mirror
    /// reproduces both.</summary>
    public const byte BoardUiCapSkipEnabledBit = 1 << 6;

    /// <summary>
    /// Board-UI record BYTE 2, bit 7 — AT LEAST ONE EQUIPPED ITEM IS USABLE RIGHT NOW on the
    /// owner's board, i.e. their CLOSED items pile is wearing its "something in here is playable"
    /// cue (<c>PileViewer.ItemsUsableCueOn</c> ← <c>ItemsPile.UsableCount &gt; 0</c>).
    ///
    /// <para>WHY IT IS ON THE WIRE AT ALL. It was a straight, pre-existing breach of the standing
    /// 1:1 ruling ("alle Interaktionen, <b>Animationen</b> und Anzeigen des Controllboards"): the
    /// owner's items stack drifts gold embers and throws rings of light on the shared item
    /// heartbeat while an item can be played, and a peer's mirrored stack showed NOTHING — three
    /// inert slabs and a number. The whole point of that cue is to be readable without looking at
    /// it; on a peer's board it did not exist at any amplitude. It is also the last piece of the
    /// item flow that had no wire: the recess (byte 0 bit 2), the USE cap (byte 0 bit 3), the fan
    /// (records 5/26) and the placed card (record 26) all already travel.</para>
    ///
    /// <para>WHY BYTE 2 AND NOT A NEW RECORD, and why this byte although it is documented as the
    /// CAP-STATE byte. Three reasons, in the order they decided it. (1) ATOMICITY: this bit is read
    /// beside byte 0's item-recess and item-USE bits by the same receiver pass, and the item cue,
    /// the recess and the cap must never arrive in different packets or a peer paints one half of
    /// the item flow against the other half's state — exactly the argument
    /// <see cref="BoardUiCapConfirmAccentBit"/> makes for putting the cap states here rather than in
    /// a record of their own. (2) COST: this byte is already written on every packet that carries a
    /// board pose, so the bit is FREE, where a new record costs 3 bytes (id + len + payload) at
    /// 5 Hz for one boolean. (3) VALIDITY FOR NOTHING: the record's TLV LENGTH already gates byte 2
    /// (<see cref="BoardUiRecordBytes"/>), so a sender that predates this build reads as "no cue",
    /// which is precisely what every earlier build's receiver drew. Byte 0 and byte 1 are FULL
    /// (0xFF / <see cref="BoardUiOverlayMask"/> == 0xFE with the last bit spent), so this was the
    /// only free bit in the record — and it is now the LAST one. The next board-UI flag needs a
    /// fourth byte, not a bit.</para>
    ///
    /// <para>ANTI-CHEAT: no identity, and nothing that is not already public. It says "this player
    /// could play some item now" — one boolean about a state vanilla already publishes far more of,
    /// since any player may open ANY other player's full card overview straight off the initiative
    /// track (<c>InitiativeTrackPlayerAvatar.OnClick</c> → <c>CardsHandManager.ToggleViewAllCards</c>),
    /// which lists the equipped items themselves. WHICH item is usable never travels.</para>
    /// </summary>
    public const byte BoardUiCapItemPileUsableBit = 1 << 7;

    /// <summary>Every DEFINED bit of the board-UI record's byte 2. The byte is FULL as of
    /// <see cref="BoardUiCapItemPileUsableBit"/> — the mask is 0xFF and there is no reserved bit
    /// left here. Same discipline as <see cref="BoardUiOverlayMask"/>: the writer masks so an
    /// undefined bit can never be pre-claimed by garbage, and the reader masks again (an OLD
    /// reader, whose mask is the previous 0x7F, therefore DROPS the item-cue bit instead of
    /// mis-rendering it — the same cross-version contract that let the snap field widen byte 1).</summary>
    public const byte BoardUiCapStateDefinedMask =
        (byte)(BoardUiCapConfirmAccentBit | BoardUiCapConfirmReadyBit
               | BoardUiCapShortRestEnabledBit | BoardUiCapShortRestAccentBit
               | BoardUiCapLongRestEnabledBit | BoardUiCapLongRestAccentBit
               | BoardUiCapSkipEnabledBit | BoardUiCapItemPileUsableBit);

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

    /// <summary>
    /// Extension record id: HELD-FIGURE STRETCH — the manual in-hand scale factor of the sender's
    /// held figure(s). 4 bytes: <c>[u16 primaryFactor LE][u16 secondaryFactor LE]</c>, each a
    /// milli-factor (1000 = 1.0×), slot-aligned with the two held-figure slots (primary = the rig
    /// packet's <see cref="FlagHeldFigure"/> block, secondary = record <see cref="ExtIdSecondFigure"/>).
    ///
    /// <para>RECORD-ID CLAIM, 2026-08-11: this change takes id 30 — the first of the free range the
    /// record-29 note left open. Declared beside record 8 because the held-figure records belong
    /// together. Ids in use are now 1..17, 22..30; 18..21 stay reserved; 31+ are free.</para>
    ///
    /// <para>WHY IT EXISTS (user request 2026-08-11, verbatim: "Ich möchte, dass die Größe der
    /// Figur in der Hand änderbar ist. Dabei stelle ich mir vor, dass ich mit der anderen Hand zu
    /// der Figur gehe und dann Trigger gedrückt halte und nach innen oder außen schiebe (nach außen
    /// heißt größer, nach innen kleiner) und somit die Größe der Figur skaliert."). The receive side
    /// already reconstructs a held mini's size as boardSize × the holder's zoom ratio with ZERO wire
    /// bytes (<c>NetFigures.EaseSlot</c> — both numbers arrive anyway). A MANUAL stretch gesture has
    /// no such luck: the factor exists only in the holder's hand motion, is derivable from nothing
    /// already on the wire, and the 1:1 ruling (§3) forbids the two machines disagreeing about the
    /// size for the whole hold. So the factor itself travels, and nothing else does — the receiver
    /// multiplies it into the ratio it already applies.</para>
    ///
    /// <para>WHY ONE RECORD FOR BOTH SLOTS: the gesture needs a free hand, so at most ONE figure can
    /// be stretched at a time — but its factor persists for the REST of the hold (the gesture can be
    /// repeated, and the other hand can grab a second mini afterwards), so both slots must be
    /// statable at once. Two fixed u16 fields beat a flags byte + variable layout: the neutral value
    /// 1000 already means "no stretch", so there is nothing a presence flag would add, and a fixed
    /// 4-byte record keeps the golden vectors hand-checkable.</para>
    ///
    /// <para>Written ONLY while at least one factor differs from <see cref="HeldStretchCodeNeutral"/>
    /// after quantization, so an unstretched hold — and every idle player — emits the exact bytes
    /// previous builds emitted. Absence means BOTH factors are 1.0: an old sender reads as neutral
    /// on a new peer, an old peer steps over the record by its length and keeps rendering
    /// boardSize × zoom ratio (the pre-record picture), and a new receiver resets to neutral the
    /// moment the record stops arriving. While the factor is CHANGING (the holder is mid-gesture)
    /// the sender promotes the extras packet to the rig rate, exactly like a carried second figure —
    /// same cadence, same receive-side easing, so the peer watches the stretch as motion, not as
    /// steps.</para>
    ///
    /// <para>VALIDATION IS FAIL-CLOSED TO NEUTRAL: a code outside
    /// [<see cref="HeldStretchCodeMin"/>, <see cref="HeldStretchCodeMax"/>] decodes to 1.0, never to
    /// a clamped extreme — a garbage byte must render the pre-record picture, not a figure at 6.5×
    /// or an invisible one at 0. (A legitimate sender clamps BEFORE quantizing, so nothing real is
    /// ever in that range.) The config dials bounding the local gesture
    /// (<c>[FigureGrab] StretchScaleMin/Max</c>) are deliberately INSIDE this wire envelope, and the
    /// receiver applies the SENDER's factor unclamped-by-local-config: it is the holder's hand and
    /// the holder's board, so their bounds govern (the 1:1 ruling again).</para>
    /// </summary>
    public const byte ExtIdHeldStretch = 30;

    /// <summary>Payload length of <see cref="ExtIdHeldStretch"/>: two u16 milli-factors. A reader
    /// requires at least this much before it trusts the record.</summary>
    public const int HeldStretchRecordBytes = 4;

    /// <summary>The neutral held-stretch milli-factor: 1000 = 1.0× = "no manual stretch". The
    /// writer omits the record when both slots quantize to this, so absence and neutrality are the
    /// same statement.</summary>
    public const int HeldStretchCodeNeutral = 1000;

    /// <summary>Smallest sane held-stretch milli-factor a peer will believe (0.10×). Below it the
    /// code reads as garbage and decodes to neutral — never to a near-invisible figure.</summary>
    public const int HeldStretchCodeMin = 100;

    /// <summary>Largest sane held-stretch milli-factor a peer will believe (8.0×). Above it the
    /// code reads as garbage and decodes to neutral. Both bounds deliberately ENCLOSE the config
    /// dials' own ranges, so no legitimately tuned sender can ever be rejected.</summary>
    public const int HeldStretchCodeMax = 8000;

    /// <summary>Quantize a held-stretch factor to its wire milli-code, clamped to the sane
    /// envelope. NaN/non-finite degrade to neutral (never trust a float either).</summary>
    public static ushort EncodeHeldStretch(float factor)
    {
        if (float.IsNaN(factor) || float.IsInfinity(factor))
            return (ushort)HeldStretchCodeNeutral;
        int code = UnityEngine.Mathf.RoundToInt(factor * 1000f);
        return (ushort)UnityEngine.Mathf.Clamp(code, HeldStretchCodeMin, HeldStretchCodeMax);
    }

    /// <summary>Decode a held-stretch milli-code. Out-of-envelope codes (including 0) FAIL CLOSED
    /// to 1.0 — the pre-record picture — rather than clamping to an extreme.</summary>
    public static float DecodeHeldStretch(int code)
        => code < HeldStretchCodeMin || code > HeldStretchCodeMax ? 1f : code / 1000f;

    /// <summary>
    /// Extension record id: the BOARD TOOLTIP the sender is reading right now — the game's hover
    /// tooltip while it is parked in the control board's TOOLTIP AREA (top-left of the board,
    /// <c>WorldUI.WorldTooltips</c>) — as UTF8 bytes, capped at <see cref="TooltipTextMaxBytes"/>.
    /// Written ONLY while a board-owned tooltip is actually shown, so an idle packet stays
    /// byte-identical to the previous build's; absence means "no tooltip", which is exactly what
    /// peers predating this record render. Receivers show it at the REMOTE board's own tooltip
    /// area (<c>RemoteBoardTooltip</c>), in the SENDER's language, verbatim.
    ///
    /// <para>THE IDENTITY GATE (the reason this record is the one text record that is NOT sent
    /// unconditionally while its source is visible): a tooltip CAN carry card identity — hovering
    /// an ability card surfaces its keywords and effect text, which names the card as surely as
    /// its face does. The standing rule is absolute: no card identity on the wire, ever; reveals
    /// only through <see cref="RevealGate"/>. So the SENDER (<c>WorldUI.WorldTooltips</c>, the
    /// only place that knows what the tooltip is anchored to) transmits the text ONLY when the
    /// hovered thing is already public to peers:</para>
    ///
    /// <list type="bullet">
    /// <item><description>Board FURNITURE — keycaps, decision buttons, pile stacks, the element
    /// board — and the map's cursor-anchored hex tooltips: always public (every client renders
    /// these from replicated state), always sent.</description></item>
    /// <item><description>A hover that resolves to a CARD (a <c>Cards.VRCard</c> ancestor of the
    /// tooltip anchor): sent ONLY when that card is physically parked in a round-card SLOT — the
    /// one place peers render our cards face-up — AND the reveal phase shows fronts to peers
    /// (the inverse of the <see cref="RevealGate"/> secret-selection rule). A hand-fan, item-fan,
    /// pile-browser or held card is BACKS-ONLY on every peer forever, so its tooltip is never
    /// sent.</description></item>
    /// <item><description>AMBIGUOUS ownership — a card face whose VRCard cannot be resolved, or
    /// any anchor the classifier does not positively recognise as furniture: NOT sent. The
    /// failure direction is suppression, always: a missing remote tooltip is cosmetic, a leaked
    /// card identity is a broken game rule.</description></item>
    /// </list>
    ///
    /// <para>ADDITIVE TLV exactly like every record before it: an older peer steps over it by its
    /// length and simply shows no remote tooltip.</para>
    /// </summary>
    public const byte ExtIdBoardTooltip = 9;

    /// <summary>UTF8 byte cap for <see cref="ExtIdBoardTooltip"/>. Tooltips are longer than the
    /// one-sentence pick placard (<see cref="PickBannerTextMaxBytes"/>) — a keyword hint is a
    /// title plus a short paragraph — so the cap is doubled; anything past it is truncated on a
    /// UTF8 CHARACTER boundary (never mid-sequence) and re-clamped on read (never trust the
    /// wire). Must stay well under the 255-byte TLV length ceiling.</summary>
    public const int TooltipTextMaxBytes = 192;

    /// <summary>
    /// Extension record id: the sender's SECOND held card — the card-back slab for the card in
    /// their OTHER hand. 20 bytes: the shared pose encoding (pos 3 x f32 LE + quantized quat),
    /// nothing else. POSE ONLY, never an identity — peers render an anonymous card BACK, the
    /// standing rule of this wire.
    ///
    /// <para>THE COMPROMISE IT REMOVES (user ruling 2026-08-04: "Alles soll synchronisiert
    /// werden - auch die Karten in der jeweiligen Hand. Wenn Karten in beiden Haenden sind, soll
    /// das auch synchronisiert werden!"). Since the both-hands card ruling either hand can
    /// physically hold a card — hand-to-hand transfer, gate hand allowed on all cards — but the
    /// rig packet's <see cref="FlagHeldCard"/> block carries exactly ONE 20-byte pose and the rig
    /// flag byte is FULL (see the note at <see cref="FlagHeldCard"/>), so the sampler
    /// deterministically preferred the LEFT hand and the right hand's card was invisible to
    /// peers. That documented compromise is now rejected: the rig packet keeps carrying exactly
    /// what it always carried (the sampler's left-first preference, byte-unchanged for every
    /// old reader), and THIS record carries the other hand's card.</para>
    ///
    /// <para>WHY THERE IS NO HAND BYTE, unlike <see cref="ExtIdSecondFigure"/>: the receiver
    /// does not parent the slab to a hand. <c>RemoteAvatar.UpdateHeldCard</c> places the held-card
    /// slab at the ABSOLUTE transmitted world pose (a holder under the avatar root, driven by
    /// <c>UpdatePart</c> from the wire pose) and then re-derives its rotation by billboarding to
    /// the sender's synced head — a hand transform is never consulted, so a hand id would be a
    /// dead byte. The second-figure record needed its hand bits because <c>NetFigures</c> DOES
    /// attach real board minis to hands and had to reject a contradictory pair; no such
    /// contradiction is expressible for a slab rendered at an absolute pose. Which hand LOOKS
    /// occupied is already told by the finger curls and the ghost-sides mask that ride the wire
    /// anyway.</para>
    ///
    /// <para>POSE RATE: same argument, same mechanism as <see cref="ExtIdSecondFigure"/> (read
    /// its doc for the full cadence argument) — while the second card is held and moving the
    /// sender promotes the whole extras packet to the rig rate (<see cref="SendRateHz"/>), so
    /// both cards stream at the same 15 Hz and are eased by the same
    /// <see cref="InterpolationSharpness"/>; identical sample density plus identical easing is
    /// identical motion by construction. Grab and release are EDGES that pre-empt the send gate
    /// outright.</para>
    ///
    /// <para>Written ONLY while TWO cards are physically held (one per hand), so a one-card hold
    /// — and an idle player — emits the exact bytes build 49 emitted. Older peers step over the
    /// record by its length and keep showing the one slab they always showed. ADDITIVE TLV
    /// exactly like every record before it.</para>
    /// </summary>
    public const byte ExtIdSecondHeldCard = 10;

    /// <summary>Payload length of <see cref="ExtIdSecondHeldCard"/>: the shared 20-byte pose
    /// (pos 12 + quantized quat 8), nothing else. A reader requires at least this much before it
    /// trusts the record.</summary>
    public const int SecondHeldCardRecordBytes = 20;

    /// <summary>
    /// Extension record id: the sender's SLOT-CARD SIZE — 4 bytes,
    /// <c>[u16 slotFrameWidth LE][u16 slotCardWidth LE]</c>, both board-local WIDTHS in
    /// TENTH-MILLIMETRES (see <see cref="EncodeSlotWidth"/>; heights are derived — every card
    /// surface in this project is the fixed 63.5:88 poker aspect).
    ///
    /// <para>THE DEFECT IT FIXES (user, hardware MP test 2026-08-04: "Die Kartengröße am fremden
    /// Board stimmt nicht 1:1 — ich sehe sie kleiner"). The LOCAL board renders a card parked in a
    /// recess at <c>CardsConfig.CardWidth × PlayTray.SlotScale × PlayTray.SlotCardScale</c> —
    /// the last factor is <c>[Cards] SlotOverlayScale_{board}</c>, whose DEFAULT is 1.45 (it was the
    /// global <c>SlotCardFill</c> until that dial's 2026-08-11 retirement) — while the remote
    /// mirror hardcoded <c>Defaults.CardWidth × SlotScale</c> and dropped the fill entirely, so
    /// even two default-configured clients disagreed by 31 %: everyone's remote cards rendered at
    /// 82.6 mm where their owner sees 119.7 mm. Both factors are LOCAL CONFIG on the sender and
    /// therefore not derivable from anything already synced; the effective sizes must ride the
    /// wire, exactly like the board style and the hand scale before them.</para>
    ///
    /// <para>WHY TWO WIDTHS: the board's slot visuals are TWO independent sizes layered from the
    /// same config — the recess/glow FRAME metric (<c>CardWidth × SlotScale</c>, what
    /// <c>PlayTray.4.Slots</c> sizes the wanted-glow/frame quads from) and the CARD occupying it
    /// (that × the overlay scale). Transmitting only the card width would leave the receiver unable
    /// to reproduce the frame (the scale is not recoverable from one number), so the glow overlays
    /// would mis-frame the very card the record just fixed.</para>
    ///
    /// <para>2026-08-11: the two are no longer independent — one dial sizes the wanted-glow and the
    /// card alike (<see cref="TuneSlotOverlayScale"/>), so the CARD width here equals the glow's.
    /// The FRAME metric stays, and stays separate: it is the recess the peer's overlays are seated
    /// against, and the record predates the coupling in shipped builds that are still out there.</para>
    ///
    /// <para>NO IDENTITY, NO GAMEPLAY: two cosmetic lengths. Written only while a control board
    /// exists AND at least one of the two differs from the legacy assumption
    /// (<see cref="SlotCardWidthLegacy"/> — the constant every pre-record receiver hardcodes), so
    /// a sender whose effective sizes equal that constant stays byte-identical to the previous
    /// build. ADDITIVE TLV exactly like every record before it: an older peer steps over it by
    /// length and keeps the legacy constant — today's look, never a broken one.</para>
    /// </summary>
    public const byte ExtIdSlotCardSize = 11;

    /// <summary>Payload length of <see cref="ExtIdSlotCardSize"/>: two u16 widths. A reader
    /// requires at least this much before it trusts the record.</summary>
    public const int SlotCardSizeRecordBytes = 4;

    /// <summary>
    /// The board-local slot width every receiver ASSUMES when <see cref="ExtIdSlotCardSize"/> is
    /// absent — <c>Defaults.CardWidth (0.0635) × PlayTray.SlotScale (1.3)</c>, the constant the
    /// remote board hardcoded before the record existed (both for the card and for the frame).
    /// Kept as the shared fallback so an old sender renders exactly as it always did.
    /// </summary>
    public const float SlotCardWidthLegacy = 0.0635f * 1.3f;

    /// <summary>Smallest wire code <see cref="DecodeSlotWidth"/> accepts: 5 mm. Below it (garbage,
    /// zero) the decoder degrades to "record absent" — the legacy width — rather than collapsing a
    /// peer's cards to a sliver.</summary>
    public const ushort SlotWidthMinCode = 50;

    /// <summary>Quantize a board-local slot/card width (metres) to its u16 wire code — TENTH
    /// MILLIMETRES, clamped. 0.1 mm is far below what the eye resolves on an ~120 mm card and the
    /// u16 ceiling (6.55 m) is far above any board; tenths keep the value readable in a hardware
    /// log (1197 = 119.7 mm).</summary>
    public static ushort EncodeSlotWidth(float meters)
    {
        if (float.IsNaN(meters) || float.IsInfinity(meters))
            return (ushort)UnityEngine.Mathf.RoundToInt(SlotCardWidthLegacy * 10000f);
        return (ushort)UnityEngine.Mathf.Clamp(
            UnityEngine.Mathf.RoundToInt(meters * 10000f), SlotWidthMinCode, ushort.MaxValue);
    }

    /// <summary>Decode a slot-width code back to metres; 0 for an invalid code, which every
    /// consumer treats as "use <see cref="SlotCardWidthLegacy"/>".</summary>
    public static float DecodeSlotWidth(ushort code) =>
        code < SlotWidthMinCode ? 0f : code / 10000f;

    /// <summary>
    /// Extension record id: the sender's ACTION-SELECTION half states — 2 bytes,
    /// <c>[byte0 hover][byte1 selection]</c>.
    ///
    /// byte 0 — the transient HOVER (which half their pointer is ON right now):
    ///   bits 0..1 the board SLOT of the docked round card (<see cref="HalfHoverSlotMask"/>;
    ///             the same slot indices the board-UI occupancy nibble uses).
    ///             <see cref="HalfHoverNoneSlot"/> (3) = NO hover this packet — needed since the
    ///             record also rides for a selection-only state; slot 2 (a recess the two-slot
    ///             board lacks) is rejected on read and degrades to "no hover" too.
    ///   bit 2     set = the TOP action half (<see cref="HalfHoverTopBit"/>), clear = bottom.
    ///   bits 3..5 the BOARD KEYCAP the owner has just PRESSED (<see cref="CapPressCapMask"/>,
    ///             <see cref="CapPressNone"/> 0 = no press riding this packet);
    ///   bits 6..7 that press's 2-bit SEQUENCE (<see cref="CapPressSeqMask"/>) — see
    ///             <see cref="CapPressNone"/> for why an edge needs a counter.
    /// byte 1 — the persistent SELECTION (which half of each slot card is CLICKED/committed,
    ///   the game's own steady half highlight after a click, cleared again by undo):
    ///   bits 0..1 slot 0's selected half (<see cref="HalfSelectNone"/> 0 / <see
    ///             cref="HalfSelectTop"/> 1 / <see cref="HalfSelectBottom"/> 2; value 3 is
    ///             invalid and reads as none — never trust the wire),
    ///   bits 2..3 slot 1's selected half, same encoding,
    ///   bits 4..7 reserved (written 0, masked on read).
    ///
    /// <para>THE DEFECT PAIR IT FIXES (hardware MP test 2026-08-04: "Die Overlay-Auswahl auf
    /// Karten beim Hovern ist nicht synchronisiert" + the follow-up "Ich will auch sehen,
    /// welche Hälfte der Mitspieler GEKLICKT hat — die wird dauerhaft hervorgehoben, und auch
    /// das Abwählen muss sichtbar sein"). Locally, pointing at a docked round card's half
    /// lights the game's PULSING hover overlay (<c>CardActionHighlight.ShowHover</c>, alpha
    /// 1↔0.3), and CLICKING it latches the STEADY selected overlay
    /// (<c>FullAbilityCardAction.ToggleSelect → CardActionHighlight.ShowSelected</c>, undone by
    /// the game's undo path). Neither state was on the wire, so the acting player's two most
    /// watched gestures — "this half?" and "THIS half." — did not exist on anyone else's
    /// screen. Peers now draw the same two-state glow on the same halves of the mirrored board:
    /// pulsing for the hover, steady for the selection, exactly the presentation split the
    /// owner's own card makes.</para>
    ///
    /// <para>WHY SLOT INDICES AND NOT CARDS: the standing rule — no card identity on this wire.
    /// A slot POSITION reveals nothing: the round cards are docked in the two public board slots
    /// and their faces are already rendered to peers by the reveal gate's own rules; during the
    /// action phase they are public anyway (the committed half also reaches every client through
    /// the authoritative action stream — this record only makes it visible at the board).</para>
    ///
    /// <para>Written ONLY while a half is hovered OR selected OR a keycap press is in its hold
    /// window, so an idle packet stays byte-identical to the previous build's. Hover edges pre-empt
    /// the extras gate capped at the rig interval (a drifting beam can flick halves several times a
    /// second); SELECTION and PRESS edges pre-empt it OUTRIGHT (a click is discrete and
    /// human-paced — the pile-counts rule), so the steady highlight and the cap dip land with the
    /// click. This record has never shipped in a distributed build, so the 1→2-byte extension costs
    /// no compatibility case: the ModBuild handshake gates every peer to the same build.</para>
    /// </summary>
    public const byte ExtIdHalfHover = 14;

    /// <summary>
    /// Half-hover byte 0, bits 3..5 — WHICH board keycap the owner has just PRESSED, as one of
    /// <see cref="CapPressConfirm"/> … <see cref="CapPressFollowPin"/>, or this value (0) for
    /// "no press rides this packet".
    ///
    /// <para>WHY THE SENTINEL IS ZERO AND THE SEVEN CAP IDS ARE 1..7 — the same rule the PINNED bit
    /// and the snap field follow: a sender that predates a field writes its bits as ZERO, so zero
    /// must decode to the behaviour those builds already produced, which here is "no press, animate
    /// nothing". Numbering the caps from 0 would have made a pre-field sender's empty byte read as
    /// "the CONFIRM cap was pressed". Seven caps in three bits with zero reserved is an exact fit.</para>
    ///
    /// <para>WHY A PRESS NEEDS A WIRE FIELD AT ALL, when almost every other keycap ANIMATION does
    /// not. A cap's dissolve-away, its materialize-from-dust and its state colours are all
    /// FUNCTIONS of state that is already synced (record 4's visibility bits and cap-state byte),
    /// so a receiver plays them from the transition it can already see — zero wire. The press is
    /// the one that is not: it is an EVENT with no state behind it. The cap dips to the bottom of
    /// its travel and springs back over ~170 ms (<c>BoardButton._press</c> decaying at 6/s), the
    /// button's action may or may not change anything the wire carries, and at the 5 Hz extras
    /// cadence the whole animation can fall between two packets. Five bits reproduce it exactly.
    /// </para>
    ///
    /// <para>WHY THE SEQUENCE COUNTER. The field is a LATCH, not a pulse: the sender keeps writing
    /// the last press for a short hold window so a lost or late packet still delivers it. A
    /// receiver therefore cannot animate "the field is set" — it would replay the same press on
    /// every packet of the window. It animates the field CHANGING, and two identical consecutive
    /// presses of the SAME cap differ only in the counter. Two bits are ample: presses on one cap
    /// are debounced <c>0.4 s</c> apart (<c>ButtonTuning.PokePressCooldownSeconds</c>) and every
    /// press pre-empts the send gate outright, so four unobserved presses in a row cannot happen.
    /// </para>
    ///
    /// <para>NOT INTERACTIVITY. What crosses is "this cap was pressed", and what the receiver does
    /// with it is play the owner's own dip on a colliderless copy. No callback, no registration,
    /// nothing on the remote board becomes pressable — see <c>RemoteBoardFurniture</c>'s inertness
    /// contract, which the mirrored animation is explicitly inside.</para>
    /// </summary>
    public const byte CapPressNone = 0;

    /// <summary>Bit position of the pressed-cap field in the half-hover record's byte 0.</summary>
    public const int CapPressShift = 3;

    /// <summary>Mask of the pressed-cap field (bits 3..5) IN PLACE.</summary>
    public const byte CapPressCapMask = (byte)(0x07 << CapPressShift);

    /// <summary>Bit position of the press SEQUENCE field in the half-hover record's byte 0.</summary>
    public const int CapPressSeqShift = 6;

    /// <summary>Mask of the press SEQUENCE field (bits 6..7) IN PLACE.</summary>
    public const byte CapPressSeqMask = (byte)(0x03 << CapPressSeqShift);

    /// <summary>Pressed-cap id: the CONFIRM keycap. Wire ids are append-only and are mapped from
    /// the Cards layer's own cap enum by an explicit switch in <c>NetAvatarDriver</c>, so a
    /// re-ordering there cannot silently renumber the wire.</summary>
    public const byte CapPressConfirm = 1;

    /// <summary>Pressed-cap id: the UNDO keycap.</summary>
    public const byte CapPressUndo = 2;

    /// <summary>Pressed-cap id: the item-use USE keycap (the dynamic third cluster member).</summary>
    public const byte CapPressItemUse = 3;

    /// <summary>Pressed-cap id: the SHORT-rest disc.</summary>
    public const byte CapPressShortRest = 4;

    /// <summary>Pressed-cap id: the LONG-rest disc.</summary>
    public const byte CapPressLongRest = 5;

    /// <summary>Pressed-cap id: the turn-flow SKIP cap.</summary>
    public const byte CapPressSkip = 6;

    /// <summary>Pressed-cap id: the FOLLOW/PIN dashboard toggle — the last id the 3-bit field
    /// holds. An eighth cap would need a new field, not a renumbering.</summary>
    public const byte CapPressFollowPin = 7;

    /// <summary>Highest cap id the field defines. A receiver rejects anything above it (there is
    /// nothing above it today, which is the point of asserting the bound anyway) and treats
    /// <see cref="CapPressNone"/> as "animate nothing" — never trust the wire.</summary>
    public const byte CapPressMaxId = CapPressFollowPin;

    /// <summary>Payload length of <see cref="ExtIdHalfHover"/>: hover byte + selection byte. A
    /// reader requires at least this much before it trusts the record.</summary>
    public const int HalfHoverRecordBytes = 2;

    /// <summary>Half-hover byte 0: mask of the SLOT index bits (0..1).</summary>
    public const byte HalfHoverSlotMask = 0x03;

    /// <summary>Half-hover byte 0 slot-field sentinel: NO hover rides this packet (the record is
    /// selection-only). 3 rather than 2 so the one remaining slot value stays free for a
    /// hypothetical third recess.</summary>
    public const byte HalfHoverNoneSlot = 0x03;

    /// <summary>Half-hover byte 0, bit 2 — the hovered half is the TOP action (clear = bottom).</summary>
    public const byte HalfHoverTopBit = 1 << 2;

    /// <summary>Every DEFINED bit of the half-hover byte 0 (slot + top-half + the cap-press cap and
    /// sequence fields — the byte is now FULL). Masked on write AND read so a future bit cannot be
    /// pre-claimed by garbage — the same discipline as every masked byte here.</summary>
    public const byte HalfHoverDefinedMask =
        (byte)(HalfHoverSlotMask | HalfHoverTopBit | CapPressCapMask | CapPressSeqMask);

    /// <summary>Selection field (2 bits per slot in byte 1): no half of this slot's card is
    /// selected. Also what a reader assumes for the invalid value 3.</summary>
    public const byte HalfSelectNone = 0;

    /// <summary>Selection field: the TOP action half is selected (steady highlight).</summary>
    public const byte HalfSelectTop = 1;

    /// <summary>Selection field: the BOTTOM action half is selected.</summary>
    public const byte HalfSelectBottom = 2;

    /// <summary>Bit width of one slot's selection field in byte 1 (slot i lives at
    /// <c>i * HalfSelectBitsPerSlot</c>).</summary>
    public const int HalfSelectBitsPerSlot = 2;

    /// <summary>Mask of one slot's selection field (before shifting).</summary>
    public const byte HalfSelectFieldMask = 0x03;

    /// <summary>Every DEFINED bit of the SELECTION FIELDS in byte 1 (two 2-bit fields for the
    /// board's two slots — widens with <see cref="BoardUiSlotCount"/> if the board ever grows a
    /// recess). Bits 4..7 are NOT selection: see <see cref="HalfEmptyFanHintBit"/> and
    /// <see cref="HalfSelectByteDefinedMask"/>.</summary>
    public const byte HalfSelectDefinedMask =
        (byte)((1 << (BoardUiSlotCount * HalfSelectBitsPerSlot)) - 1);

    /// <summary>
    /// Record 14, byte 1, bit 4 — THE "KEINE HANDKARTEN" PLACARD IS ON THE SENDER'S SCREEN RIGHT
    /// NOW (<c>Cards.EmptyFanHint</c>: the ghost plate that appears at the would-be fan spot when
    /// the palm-roll gate opens onto a genuinely empty hand, and fades over ~1.5 s).
    ///
    /// <para>THE GAP IT CLOSES: the placard was the one control-board-adjacent display that was
    /// not mirrored at all. It is HAND-anchored rather than board furniture, so none of the board
    /// records carried it, and a peer raising an empty hand simply produced nothing on anyone
    /// else's screen — while their own view answers the gesture with a plate. Under the standing
    /// 1:1 ruling that is a display of the owner's, and it travels.</para>
    ///
    /// <para>WHY A REAL BIT AND NOT AN INFERENCE FROM THE HAND-CARD COUNT. The count alone cannot
    /// tell "the gate opened onto an empty hand" (placard) from "the fan is closed" (nothing) —
    /// both are <c>HandCardCount == 0</c> with no fan flag, which is also the state of every
    /// player standing idle with their palm down. That ambiguity is recorded in
    /// <c>.planning/refactor/INVARIANTS-Net-Rig.md</c>; deriving the placard from the count would
    /// have flashed one on every peer's hand every time they lowered an empty hand. One bit states
    /// the fact instead.</para>
    ///
    /// <para>WHY BYTE 1 OF RECORD 14 AND NOT A RECORD OF ITS OWN: both flag bytes are full, and a
    /// TLV record for ONE bit would cost 3 bytes of header for it. Byte 1's bits 4..7 were
    /// declared "reserved (written 0, masked on read)" from the day the record shipped, which is
    /// exactly the room this needs. It costs ZERO bytes whenever the record is already riding for
    /// a hover or a selection, and 4 bytes (the record) when the placard is the only thing to say
    /// — a state that lasts ~1.5 s and happens on an edge.</para>
    ///
    /// <para>THE WRITE GATE WIDENS WITH IT: record 14 used to ride only while a half was hovered
    /// OR selected. It now also rides while this bit is set, and the hint EDGE pre-empts the extras
    /// send gate (a placard is discrete and human-paced — the pile-counts rule), so the plate lands
    /// with the gesture instead of up to 200 ms after it. A sender with nothing hovered, nothing
    /// selected and no placard still writes no record at all, so an idle packet stays byte-identical
    /// to the previous build's.</para>
    ///
    /// <para>WHY NO TEXT RIDES WITH IT (unlike the pick banner, record 7): the placard's line is a
    /// FIXED two-word string with an exact local equivalent on every client ("Keine Handkarten" /
    /// "No hand cards"), not a composed sentence carrying an actor and a count. The receiver
    /// therefore renders it from its OWN localization — the same choice record 24's prompt-TEXT
    /// VARIANT makes, and for the same reason: a number (here a bit) is cheaper than a string
    /// whenever the receiver can rebuild the string exactly. The one consequence is deliberate and
    /// documented: a German player's placard reads "No hand cards" to an English peer. Nothing
    /// about a hand's CONTENTS is expressible here — the bit says only that the hand is empty,
    /// which the already-synced <c>HandCardCount</c> of 0 says too.</para>
    /// </summary>
    public const byte HalfEmptyFanHintBit = 1 << 4;

    /// <summary>Every DEFINED bit of record 14's byte 1 — the two selection fields plus the
    /// empty-fan-hint bit. Writer and reader both mask with it, so bits 5..7 stay genuinely
    /// reserved and a future sender's extra bits can never light a meaning here.</summary>
    public const byte HalfSelectByteDefinedMask =
        (byte)(HalfSelectDefinedMask | HalfEmptyFanHintBit);

    /// <summary>Clamp a selection value onto the wire field: anything outside
    /// none/top/bottom (including the invalid 3) degrades to none.</summary>
    public static byte EncodeHalfSelect(int value) =>
        value == HalfSelectTop || value == HalfSelectBottom ? (byte)value : HalfSelectNone;

    /// <summary>
    /// Extension record id: the sender's displayed PILE COUNTS — 3 bytes,
    /// <c>[discard][burnt][items]</c>, each the number the corresponding stack label on their OWN
    /// board shows this frame (clamped 0..255).
    ///
    /// <para>THE DEFECT IT FIXES (hardware MP test 2026-08-04: "Wenn der Mitspieler Karten
    /// ablegt und sich die Stapel-Zahlen ändern, muss das sofort synchronisiert werden" — the
    /// session screenshot shows a peer's 'ABGEWORFEN 0' standing while that peer's own board
    /// already read 2). A peer's stack counts were a pure MODEL read on the receiver
    /// (<c>CCharacterClass.Discarded/Lost/PermanentlyLostAbilityCards</c> + the inventory), and
    /// the session logs prove that read is NOT timely: the discarding player's own model showed
    /// discard=2 (their LogOutput 17919) while the observer's replicated copy still derived
    /// 0/0 for the whole rest of that turn (observer count edge ~1300 log lines later) — the
    /// observer's model only catches up as the game's choreographer plays the turn back. The
    /// OWNER's displayed numbers therefore ride the wire, and the receiver prefers them.</para>
    ///
    /// <para>PUBLIC INFO, NO GATE: vanilla lets anyone open any player's full card overview from
    /// the initiative track (<c>CardsHandManager.ToggleViewAllCards</c>), so a count on a stack
    /// reveals nothing — the exact argument the remote board's model-read counters already
    /// documented. No card identity, ever: three integers.</para>
    ///
    /// <para>PRESENCE CONTRACT (the board-UI record's, not the "only when non-default" one):
    /// written on EVERY extras packet while the sender's own pile stacks are displayed
    /// (<c>PileViewer.CurrentCounts</c> non-null), so "record present, all zeros" (a fresh hand,
    /// genuinely empty piles) is distinguishable from "sender predates the field" (record absent
    /// ⇒ the receiver keeps the legacy model-read counts, exactly as every build before this
    /// one). A count CHANGE pre-empts the 5 Hz extras gate outright — that is the "sofort".</para>
    /// </summary>
    public const byte ExtIdPileCounts = 15;

    /// <summary>Payload length of <see cref="ExtIdPileCounts"/>: one byte per stack
    /// (discard, burnt, items). A reader requires at least this much before it trusts the record.</summary>
    public const int PileCountsRecordBytes = 3;

    /// <summary>
    /// Extension record id: the INITIATIVE-TRACK entry the sender's pointer is hovering — 5 bytes,
    /// <c>[flags][int32 actorId LE]</c>. flags bit 0 (<see cref="TrackHoverPopupBit"/>) = the
    /// entry's INFO POPUP is open on the sender's screen (the enemy round-action preview,
    /// <c>MonsterBaseUI.TogglePreview</c>); bits 1..7 reserved (written 0, masked on read).
    ///
    /// <para>THE DEFECT PAIR IT FIXES (hardware MP test 2026-08-04: "Die Mouseover der
    /// Initiativreihenfolge sind (a) nicht synchronisiert und (b) aktuell sehe ich auf dem
    /// Remote-Brett 1:1 DASSELBE wie auf meinem eigenen"). The mirrored track
    /// (<c>RemoteInitiativeTrack</c>/<c>RemoteWidgetMirror</c>) is a live per-frame clone of the
    /// LOCAL client's own track widget, so every hover artefact of the LOCAL player — the widened
    /// entry, the opened monster preview — was copied onto the PEER's board (that is (b)), while
    /// the peer's actual hover existed nowhere (that is (a)). This record carries the hover; the
    /// mirror now suppresses the local hover artefacts and re-applies the PEER's synced ones, so
    /// each remote board shows exactly what THAT player is doing.</para>
    ///
    /// <para>WHY A STABLE ACTOR ID AND NOT A TRACK INDEX: the track's DISPLAY order is
    /// PER-CLIENT during the online selection phase — vanilla's
    /// <c>InitiativeTrackActorBehaviour.CompareTo</c> sorts entries by <c>IsUnderMyControl</c>
    /// there, so my index 3 can be your index 5 and an index would lift the wrong portrait.
    /// The id is the FNV-1a hash of the game's replicated <c>CActor.ActorGuid</c>
    /// (<c>NetFigures.StableActorId</c>) — the same cross-client id space the held-figure
    /// records ride. Deliberately NOT <c>CActor.ID</c>: that one is only unique WITHIN a class
    /// (StandeeID pools restart at 1 per monster/summon class — the exact ambiguity that
    /// mis-resolved held summons), so two enemy entries on the track could collide and lift the
    /// wrong portrait.
    /// NO CARD IDENTITY: the id names a public track entry (every client renders the same track),
    /// and the popup CONTENT is not transmitted — the receiver shows its own client's copy of
    /// that public widget, which vanilla already gates identically on every client.</para>
    ///
    /// <para>Written ONLY while an entry is hovered, so an idle packet stays byte-identical to
    /// the previous build's; hover edges pre-empt the extras gate (capped at the rig interval —
    /// a laser can sweep the whole track in under a second). Older peers step over the record by
    /// its length and simply keep the un-hovered track.</para>
    /// </summary>
    public const byte ExtIdTrackHover = 16;

    /// <summary>Track-hover record, flags bit 0: the hovered entry's info popup is open.</summary>
    public const byte TrackHoverPopupBit = 1 << 0;

    /// <summary>Every DEFINED bit of the track-hover flags byte (masked on write and read).</summary>
    public const byte TrackHoverDefinedMask = TrackHoverPopupBit;

    /// <summary>Payload length of <see cref="ExtIdTrackHover"/>: 1 flags byte + 4 actor id. A
    /// reader requires at least this much before it trusts the record.</summary>
    public const int TrackHoverRecordBytes = 5;

    /// <summary>
    /// Extension record id: the sender's CURRENTLY-FADED WALL SET — <c>[count][count × u32
    /// wall key LE]</c>, at most <see cref="WallFadesMaxKeys"/> keys, keys sorted ascending
    /// (deterministic wire bytes, cheap set diff on the sender).
    ///
    /// <para>MP WALL-FADE SYNC (user request 2026-08-07: "Optional (schaltbar): die Wall-Fades
    /// der Mitspieler sollen synchronisiert werden — Wände, die wegen eines anderen Spielers
    /// gefadet sind, sollen auch für alle anderen Spieler faden, die die Einstellung aktiv
    /// haben"). The record carries WHICH walls the sender's local decision currently fades
    /// (gate columns included; doorway/arch segments never fade anywhere and are never
    /// listed). It is ALWAYS written while the set is non-empty, regardless of the sender's
    /// own <c>[WallFade] SyncPeerFades</c> toggle — bytes are cheap and the RECEIVER's
    /// setting decides application, so one player toggling mid-session needs no
    /// renegotiation. An empty set writes no record (idle packets stay byte-identical to the
    /// previous build's); absence therefore means "no faded walls" AND covers pre-record
    /// senders identically.</para>
    ///
    /// <para>WALL KEY: wall segments are scene-local objects, so both clients derive the key
    /// independently — FNV-1a-32 (the <c>NetFigures.StableActorId</c> hash discipline) over
    /// <c>"{anchorName}|{roomLabel}|{qx}|{qz}"</c>: the segment's anchor name ('Wall 2',
    /// 'ThickDoor : (guid)' — generation-deterministic, door/tile names carry replicated
    /// GUIDs), the wall's logical-room label (the round-4 CMap identity: RoomName /
    /// MapInstanceName with the replicated MapGuid), and the anchor transform's world XZ
    /// quantized to 0.5 wu (the scenario world is replicated at identical coordinates — the
    /// mod moves only the VR rig, never the game world). A key the receiver cannot resolve
    /// is silently ignored: never a wrong wall (the un-resolvable direction is the designed
    /// failure).</para>
    ///
    /// <para>RECEIVER: a remote-fade source composed inside the wall-fade decision loop —
    /// effective fade target = max(local decision, any live peer set containing the key),
    /// dwell-free (the deciding peer already dwelled), same ramp and delivery as a local
    /// fade, gated by the receiver's <c>[WallFade] SyncPeerFades</c>. A peer's set empties
    /// when a packet arrives without the record; packet GAPS are bridged by a ~1s linger
    /// (a dropped packet produces no read at all, so loss can never fake a close).</para>
    /// </summary>
    public const byte ExtIdWallFades = 17;

    /// <summary>Key cap of <see cref="ExtIdWallFades"/> — a scene rarely fades more walls
    /// simultaneously; the sender logs when the cap truncates. Bounds the record at
    /// 1 + 4×24 = 97 payload bytes, well under the 255-byte TLV ceiling.</summary>
    public const int WallFadesMaxKeys = 24;

    /// <summary>Minimum payload of <see cref="ExtIdWallFades"/> (the count byte). The count
    /// is re-clamped on read against the record length and <see cref="WallFadesMaxKeys"/> —
    /// never trust the wire.</summary>
    public const int WallFadesMinRecordBytes = 1;

    // ---- record 22: CHARACTER FOCUS ---------------------------------------------------------
    // Ids 18..21 are DELIBERATELY SKIPPED here: they are reserved for records developed in
    // parallel with this one (a record id, once shipped, can never be renumbered, so two workers
    // must not both take "the next free id"). This record therefore starts at 22 rather than 18.
    // Ids 23 and 24 are TAKEN since the 1:1 mirroring round — 23 INITIATIVE-TRACK SELECTION FRAME
    // (declared just below) and 24 DECISION DISPLAY STATE (declared beside record 12 further down,
    // because the two decision records belong together). 18..21 remain free.
    //
    // THE RESERVATION ABOVE EARNED ITSELF: both of those records were written in parallel and BOTH
    // authors independently took "the next free id", 23. Caught at merge and 24 renumbered before
    // either shipped — a record id, once out, can never be renumbered. If you are about to claim an
    // id while other work is in flight, take one of 18..21 or say in your report which you took.
    //
    // Id 26 was the one hole between 25 and 27 and is TAKEN since 2026-08-09: ITEM-USE CLIP
    // (declared beside record 25, because the two item-flow records belong together). The claim was
    // stated in that change's report per the rule above. Id 29 was claimed on the same day by the
    // DECISION WIDGET IDENTITY record (declared beside records 12/24, because the three decision
    // records belong together). Id 30 was claimed 2026-08-11 by the HELD-FIGURE STRETCH record
    // (declared beside record 8, because the held-figure records belong together; the claim is
    // stated in that change's report per the rule above). Ids 18..21 remain free, and so does 31+.
    //
    // THE SAME RULE APPLIES TO BITS, NOT ONLY TO RECORD IDS, and a bit was claimed on 2026-08-09:
    // BOARD-UI RECORD BYTE 2, BIT 7 (BoardUiCapItemPileUsableBit — "at least one equipped item is
    // usable right now", the closed items pile's heartbeat cue). It was the LAST free bit anywhere
    // in record 4: byte 0 is full (bits 0..7 all named), byte 1 is full (BoardUiOverlayMask ==
    // 0xFE, and 0x01 of it is the two-bit wanted mask's low bit — nothing spare), and byte 2 is now
    // 0xFF. A worker who needs another board-UI flag must add a FOURTH byte to the record (the TLV
    // length gates it exactly as it gates byte 2) — there is nothing left to take here.

    /// <summary>
    /// Extension record id: WHICH CHARACTER THE SENDER IS CURRENTLY LOOKING AT — their EFFECTIVE
    /// character focus — plus the two facts only they can know: whether THE CHARACTER THE GAME IS
    /// WAITING ON is one of theirs, and (when that character is not the one they are looking at)
    /// WHICH character that is.
    ///
    /// <para>WHY IT RIDES THE WIRE (feature "free character focus"): in VR a player may focus any
    /// character during the action phase to read that character's hand, piles and played cards.
    /// The rest of the table must be able to see (a) WHO owns the character the game is waiting on
    /// and (b) whether that player is actually LOOKING at the character they have to play — the
    /// green / red control-board and Steam-avatar outlines. (a) is derivable from the replicated
    /// model on every client only via the FFSNet controllable registry, which is <em>reflection</em>
    /// and only authoritative on the owning client (<c>CActor.IsUnderMyControl</c> is a LOCAL flag —
    /// it is false on every other machine); (b) is a purely local VR presentation choice the game
    /// model knows nothing about. So exactly those facts travel, and nothing else.</para>
    ///
    /// <para><b>WHY BIT 0 GREW, AND WHY A SECOND FIELD HAD TO FOLLOW (2026-08-08).</b> Bit 0 used
    /// to mean "the sender owns the actor AT TURN", and the receiver re-derived the green/red mark
    /// by comparing the sender's focus id against ITS OWN read of
    /// <c>Choreographer.CurrentPlayerActor</c>. That worked only because both machines agree about
    /// the TURN — and that assumption is exactly what a pending DECISION breaks. The local cue is
    /// now driven by <c>Board.CharacterFocus.AttentionActor</c> = "at turn, or (nobody at turn) the
    /// character owing an OPEN DECISION"; a take-damage prompt is raised inside an ENEMY's action,
    /// where <c>CurrentPlayerActor</c> is null on EVERY client. And the receiver cannot repair that
    /// locally, by the GAME's own design: <c>UIScenarioMultiplayerController.RefreshDamagePhase</c>
    /// routes a remote player's prompt through <c>TakeDamagePanel.ShowOtherPlayer</c>, which ends in
    /// <c>myWindow.Hide(instant: true)</c> (TakeDamagePanel.cs:1133), so <c>IsOpen</c> is false and
    /// both <c>Cards.CardsGameApi.DecidingHand</c> and
    /// <c>WorldUI.Surfaces.DecisionDockSurface.PromptOwner</c> answer null on an observing client.
    /// The SENDER must therefore state the answer; the receiver may not compute it.</para>
    ///
    /// <para>RE-DEFINING BIT 0's MEANING IS LEGAL HERE, and only here, because peers must run the
    /// SAME <see cref="ModBuild"/> to play together at all — the version handshake raises the
    /// mismatch dialog before any packet is interpreted (see <see cref="ExtIdModVersion"/>), so no
    /// build that understands the old meaning can ever receive a packet written with the new one.
    /// The wire <see cref="Version"/> byte therefore stays 3 and the change is purely additive.</para>
    ///
    /// <para>LAYOUT — <see cref="CharFocusRecordBytes"/> = 5 bytes, or
    /// <see cref="CharFocusMaxRecordBytes"/> = 9 when flags bit 1 is set:
    /// <c>[flags][int32 focusActorId LE]( [int32 attentionActorId LE] )</c>.
    /// <list type="bullet">
    /// <item><c>flags</c> bit0 = <see cref="CharFocusOwnsAttentionBit"/>: the sender OWNS the
    ///   character THE GAME IS WAITING ON — its turn, or an open decision it owes. Masked to
    ///   <see cref="CharFocusDefinedMask"/> on write AND on read.</item>
    /// <item><c>flags</c> bit1 = <see cref="CharFocusAttentionIdBit"/>: a trailing
    ///   <c>attentionActorId</c> follows, because the character being waited on is NOT the one in
    ///   <c>focusActorId</c> (the sender is looking at somebody else — the RED state). When the bit
    ///   is clear and bit 0 is set, the character being waited on IS <c>focusActorId</c> (the GREEN
    ///   state), so the id is already present and is not sent twice. A trailing field guarded by a
    ///   flag bit is the same forward-compatible shape every other record uses: a reader validates
    ///   only what ITS flags demand and steps over the rest by the record's own length byte.</item>
    /// <item><c>focusActorId</c> = <c>NetFigures.StableActorId</c> of the focused character — the
    ///   FNV-1a-32 hash of the replicated <c>CActor.ActorGuid</c>, the one id space that agrees
    ///   across machines (the per-class <c>CActor.ID</c> collides and must never be used). It is
    ///   the SAME id space records 8 (second figure) and 16 (track hover) already ride.</item>
    /// </list></para>
    ///
    /// <para>THE MARK IS A PURE FUNCTION OF THIS RECORD, and that is the point:
    /// <c>attention = bit0 ? (bit1 ? trailingId : focusActorId) : 0</c>, and then
    /// <c>mark = attention == 0 ? None : (attention == focusActorId ? green : red)</c> — no local
    /// turn read enters it, so the two machines cannot disagree. A separate "mark" bit was
    /// deliberately NOT added: it would be a THIRD statement of something these two fields already
    /// determine exactly, and two encodings of one fact is the class of defect this change exists
    /// to remove. The id is needed anyway (the mirrored initiative track has to know WHICH entry
    /// wears the cue), so the mark comes for free.</para>
    ///
    /// <para>WHEN BIT 0 IS CLEAR the attention actor is NOT sent, and it does not need to be: with
    /// no owned decision, <c>DecidingHand</c> is null by its own local-control gate, so the sender's
    /// attention actor is exactly <c>Choreographer.CurrentPlayerActor</c> — replicated, and read
    /// identically on every client. A receiver therefore substitutes its own turn actor there and
    /// still reproduces the sender's steady gold "somebody else is up" ring byte for byte.</para>
    ///
    /// <para>NO CARD IDENTITY, BY CONSTRUCTION: the payload names CHARACTERS and flags, never a
    /// card — that is unchanged by the second id, which is another actor in the same id space. What
    /// a receiver draws from it is an outline colour. A peer that wants to render the focused
    /// character's cards reads them from the host-replicated <c>CPlayerActor.CharacterClass</c>
    /// through <see cref="RevealGate"/>, exactly as <c>RemoteAbilityCardSource</c> already does —
    /// this record adds no new disclosure channel of any kind.</para>
    ///
    /// <para>Written ONLY when the focus is known (a non-zero actor id); an actor id of 0 is
    /// "none" everywhere in this system and is never emitted, so a client with no scenario — or a
    /// player who is merely spectating — emits a packet byte-identical to the previous build's.
    /// Absence means "no focus known", which renders as no outline at all: the pre-record
    /// behaviour. The 9-byte form appears ONLY in the red "looking at the wrong character" state,
    /// so a table where everybody is looking at the right character still emits the 5-byte record
    /// this record has always been.</para>
    /// </summary>
    public const byte ExtIdCharFocus = 22;

    /// <summary>Flags bit 0 of <see cref="ExtIdCharFocus"/>: the SENDER owns the character THE GAME
    /// IS WAITING ON — the one at turn, or (nobody at turn) the one owing an open decision. Only
    /// the owning client can evaluate either half (<c>IsUnderMyControl</c> is a local flag, and a
    /// remote player's decision panel is hidden on every other machine), which is precisely why it
    /// travels. Widened from "owns the actor at turn" on 2026-08-08 — legal because peers must
    /// share a <see cref="ModBuild"/>; see the record doc.</summary>
    public const byte CharFocusOwnsAttentionBit = 1 << 0;

    /// <summary>Flags bit 1 of <see cref="ExtIdCharFocus"/>: a trailing <c>int32 attentionActorId</c>
    /// follows the focus id, because the character the game is waiting on is NOT the one the sender
    /// is looking at. Clear (with bit 0 set) means "it IS the focus id" — the common case, which
    /// keeps the record at its original 5 bytes.</summary>
    public const byte CharFocusAttentionIdBit = 1 << 1;

    /// <summary>Every flag bit <see cref="ExtIdCharFocus"/> defines today. Writer and reader both
    /// mask with it, so a future sender's extra bits can never light a meaning here.</summary>
    public const byte CharFocusDefinedMask = CharFocusOwnsAttentionBit | CharFocusAttentionIdBit;

    /// <summary>Minimum (and most common) payload size of <see cref="ExtIdCharFocus"/>: 1 flags
    /// byte + 4 focus-actor-id bytes. Also the reader's length floor — a record shorter than this
    /// is not delivered.</summary>
    public const int CharFocusRecordBytes = 5;

    /// <summary>Payload size of <see cref="ExtIdCharFocus"/> with the
    /// <see cref="CharFocusAttentionIdBit"/> tail: <see cref="CharFocusRecordBytes"/> + 4
    /// attention-actor-id bytes.</summary>
    public const int CharFocusMaxRecordBytes = CharFocusRecordBytes + 4;

    // ---- record 23: INITIATIVE-TRACK SELECTION FRAME ----------------------------------------

    /// <summary>
    /// Extension record id: WHICH INITIATIVE-TRACK ENTRIES THE SENDER'S OWN TRACK IS CURRENTLY
    /// FRAMING — vanilla's <c>InitiativeTrackActorAvatar.selectionObject</c>, read off the live
    /// widget as an ACTIVE-FLAG fact rather than re-derived. <c>[count][count × int32 actorId LE]</c>,
    /// at most <see cref="TrackSelectionMaxIds"/> ids.
    ///
    /// <para>WHY RECORD 22 CANNOT ANSWER THIS, which is what ModBuild 84–86 assumed. That build
    /// forced the mirrored frame onto the peer's record-22 focus id, on the argument that the focus
    /// IS "the character this player has selected". It is not the same fact, and the two come apart
    /// in the three most common states at the table:
    /// <list type="bullet">
    /// <item>an ENEMY or a foreign player is at turn — vanilla auto-selects
    ///   <c>Choreographer.m_CurrentActor</c> (<c>InitiativeTrack.UpdateInitiativeTrack</c>,
    ///   InitiativeTrack.cs:620), so the owner's own track frames that entry, while record 22
    ///   carries their <c>PresentedActor</c> (their own character, or nothing at all);</item>
    /// <item>the owner has taken a MOD focus — <c>Board/Patches/SelectionGuardPatches</c>
    ///   suppresses vanilla's entire <c>OnClick</c> once <c>CharacterFocus.TryFocus</c> succeeds,
    ///   so their vanilla frame STAYS on the at-turn actor and only the mod's own blue-white ring
    ///   moves. Painting the frame on the focused entry showed a peer a state the owner's own
    ///   track was not in;</item>
    /// <item>the owner's selection is an ENEMY or an OBJECT entry — record 22 is a CHARACTER
    ///   focus by construction and cannot name one. That was the "KNOWN LIMIT" the previous build
    ///   recorded; this record retires it, because an id is an id and the enemy entries ride the
    ///   very same <c>NetFigures.StableActorId</c> space record 16 (track hover) already uses.</item>
    /// </list></para>
    ///
    /// <para>WHY IT IS GENUINELY PER-VIEWER and therefore worth wire bytes at all (the standing
    /// preference is zero-wire). Most of vanilla's selects ARE global — the round-start auto-select
    /// and <c>Choreographer</c>'s initiative-adjustment select run identically on every client off
    /// the same replicated message stream. But not all: during
    /// <c>SelectAbilityCardsOrLongRest</c> the focus gate is shut, so vanilla's own portrait click
    /// runs again and a player leafs through THEIR OWN characters
    /// (<c>InitiativeTrackPlayerAvatar.OnClick</c>), and <c>Choreographer.TileHandler</c>'s
    /// card-selection branch selects a player from a MINIATURE click. Both are one client's
    /// pointer, on one client's screen. A receiver cannot derive them, and under the 1:1 ruling it
    /// must not guess.</para>
    ///
    /// <para>WHY A LIST AND NOT ONE ID: <c>InitiativeTrack.Select</c> skips the deselect of the
    /// previous entry while the incoming actor <c>IsTakingExtraTurn</c> (InitiativeTrack.cs:340),
    /// so two frames can legitimately stand at once. One id would have had to pick a winner and
    /// would have been wrong half the time in exactly the state the user watches most closely.</para>
    ///
    /// <para>NO CARD IDENTITY: the payload names PUBLIC TRACK ENTRIES — the same widget, in the
    /// same id space, that record 16 already names for hover, and every client renders the whole
    /// track already. What a receiver does with it is switch a frame on. The initiative NUMBER
    /// inside that frame is NOT touched and stays behind vanilla's own online gate (a foreign
    /// player reads "?" during card selection) — that is the sanctioned card-front exception, and
    /// this record does not widen it by a byte.</para>
    ///
    /// <para>Written ONLY while the sender's own track really shows at least one frame, so an idle
    /// packet stays byte-identical to the previous build's; absence means "no frame", which is
    /// exactly what peers predating the record render. ADDITIVE TLV, appended in id order behind
    /// record 22 — an older reader steps over it by its length.</para>
    /// </summary>
    public const byte ExtIdTrackSelection = 23;

    /// <summary>Id cap of <see cref="ExtIdTrackSelection"/>. Vanilla can stand at most two frames
    /// at once (the normal selection plus an extra-turn actor that was never deselected); four is
    /// headroom, and it bounds the record at 1 + 4×4 = 17 payload bytes. Clamped on BOTH ends —
    /// the reader re-clamps against the record length as well, never trusting the wire.</summary>
    public const int TrackSelectionMaxIds = 4;

    /// <summary>Minimum payload of <see cref="ExtIdTrackSelection"/> (the count byte alone). A
    /// reader requires at least this much before it looks at the record.</summary>
    public const int TrackSelectionMinRecordBytes = 1;

    /// <summary>
    /// Extension record id: the BUTTON LABELS of the sender's DOCKED DECISION ROW — the real game
    /// widgets their <c>WorldUI.Surfaces.DecisionDockSurface</c> currently docks below their
    /// control board ("Verbrennen", "Ja"/"Nein", the take-damage burn choices, …) — as ONE UTF8
    /// blob, one label per line ('\n'-separated), capped at
    /// <see cref="DecisionLinesMaxBytes"/> bytes.
    ///
    /// <para>WHY IT RIDES THE WIRE (user report 2026-08-04: "die remote decision buttons ...
    /// sollen 1:1 angezeigt werden"): the board-UI record's <see cref="BoardUiDecisionBit"/> only
    /// says THAT a prompt is docked, so peers drew a generic empty "ENTSCHEIDUNGEN" drawer — a
    /// picture of furniture, not of the decision. The docked widgets themselves are LOCAL UI
    /// (TakeDamagePanel / UIManager.dialogPopup / the short-rest YesNoDialog exist only on the
    /// deciding player's client), so the only way a peer can render the owner's actual choices is
    /// for their labels to travel. Receivers render one inert, antique-styled button plate per
    /// line at the same board seat the owner's dock uses.</para>
    ///
    /// <para>NO CARD IDENTITY, BY CONSTRUCTION: only the labels of PRESSABLE widgets (TMP texts
    /// under a <c>Selectable</c>) are ever sampled — action verbs and counts authored from generic
    /// GUI_* keys ("Verbrennen", "2 abgelegte Karten verbrennen", "Ja"). The prompt/question TEXT
    /// of a dialog is deliberately NOT sent: a confirm dialog's description can embed the card it
    /// is about, and the standing rule is absolute (reveals only through <see cref="RevealGate"/>;
    /// suppression is the designed failure direction).</para>
    ///
    /// <para>Written ONLY while a decision row is really docked AND VISIBLE on the owner's board,
    /// so an idle packet stays byte-identical to the previous build's; absence means "no decision
    /// on show", which is what peers predating the record render (the drawer, via the board-UI
    /// bit). ADDITIVE TLV exactly like every record before it.</para>
    ///
    /// <para>THE "AND VISIBLE" HALF IS NEW (user ruling 2026-08-08: "generell gilt die Regel, das
    /// man alle Interaktionen, Animationen und Anzeigen des Controllboards in MP auch
    /// synchronisieren soll … so wie der Spieler sie sieht"). This record used to keep riding while
    /// the owner's row was RENDER-HIDDEN for another character's focus, on the principle that "a
    /// local view change may never edit what other machines see" — so peers showed a decision its
    /// owner could not. Under the ruling that is backwards: a remote board is a picture of ITS
    /// OWNER'S board, and while the row is hidden their board shows nothing at that seat. The
    /// board-UI decision bit clears on the same condition, so the drawer does not stand in for the
    /// withdrawn row either.</para>
    /// </summary>
    public const byte ExtIdDecisionLines = 12;

    /// <summary>UTF8 byte cap for <see cref="ExtIdDecisionLines"/> (the '\n'-joined label blob).
    /// A decision row holds at most a handful of short verbs; the cap bounds the record and is
    /// re-clamped on read (never trust the wire). Truncation on a UTF8 CHARACTER boundary, never
    /// mid-sequence; a label that gets cut simply renders shortened on the peer. Must stay well
    /// under the 255-byte TLV length ceiling.</summary>
    public const int DecisionLinesMaxBytes = 160;

    // ---- record 24: DECISION DISPLAY STATE --------------------------------------------------

    /// <summary>
    /// Extension record id: the STATE of the sender's docked decision display — which prompt is
    /// docked, which prompt TEXT variant it is showing, and per option whether it is OFFERED,
    /// GREYED or CHOSEN. Record 12 carries the option WORDINGS; this one carries everything about
    /// them that is not a word, so a peer's mirrored dock reads the way the deciding player's own
    /// dock reads instead of showing three equally-live-looking plates.
    ///
    /// <para>WHY (user ruling 2026-08-08, verbatim: "Ich möchte das die Schadensabfrage 1:1 beim
    /// remote-board so angezeigt wird wie der Spieler es auch sieht. generell gilt die Regel, das
    /// man alle Interaktionen, Animationen und Anzeigen des Controllboards in MP auch
    /// synchronisieren soll."). The take-damage prompt is a THREE-WAY choice whose options are
    /// individually gated by the game's own formula (hand &gt; 0, discard &gt; 1, take-damage
    /// control — <c>Cards.CardsDriver.TickTakeDamageOptions</c>, log line "DECISION SURFACE
    /// (take-damage)"), and one of them may already be toggled ON. Those three facts are LOCAL UI
    /// on the deciding client (the whole <c>TakeDamagePanel</c> widget row lives only there —
    /// every other client's game called <c>ShowOtherPlayer</c>, which hides the window), so they
    /// can only reach a peer over the wire.</para>
    ///
    /// <para>LAYOUT — <c>[flags][n][n × option byte]</c>, at least
    /// <see cref="DecisionStateMinRecordBytes"/> bytes:
    /// <list type="bullet">
    /// <item><c>flags</c> bits 0..2 = <see cref="DecisionPromptKindMask"/>, WHICH prompt is docked
    ///   (<see cref="DecisionKindNone"/> / <see cref="DecisionKindTakeDamage"/> /
    ///   <see cref="DecisionKindShortRestYesNo"/> / <see cref="DecisionKindDialogPopup"/>);
    ///   bits 3..5 = <see cref="DecisionTextVariantMask"/>, WHICH prompt-text variant the owner's
    ///   HelpBox is showing; bits 6..7 reserved, masked to
    ///   <see cref="DecisionStateDefinedMask"/> on write AND on read.</item>
    /// <item><c>n</c> = number of option bytes, clamped to <see cref="DecisionStateMaxOptions"/> on
    ///   both ends. The options are INDEX-ALIGNED with record 12's '\n'-separated lines — same walk,
    ///   same order, sampled in the same pass — so option <c>i</c> describes line <c>i</c>.</item>
    /// <item>option byte: <see cref="DecisionOptionOfferedBit"/> (the widget is interactable — the
    ///   owner can press it), <see cref="DecisionOptionDimmedBit"/> (the game's 0.7-alpha "your
    ///   character cannot do this" dim), <see cref="DecisionOptionChosenBit"/> (a toggle that is
    ///   currently ON). Masked to <see cref="DecisionOptionDefinedMask"/> both ways.</item>
    /// </list></para>
    ///
    /// <para>NO CARD IDENTITY, BY CONSTRUCTION — AND NO PROMPT TEXT EITHER. The record carries
    /// three small enumerations and a bitfield; not one byte of it is authored content. The prompt
    /// TEXT is NOT sent: the receiver COMPOSES it from its own localization table, because every
    /// input of the game's own branch selection except the branch itself is already replicated to
    /// every client (<c>TakeDamagePanel.ShowOtherPlayer</c> hands each peer the attacked actor, the
    /// damaging ability and the damage numbers). Sending the composed string instead would have put
    /// ACTIVE-BONUS CARD NAMES on the wire — <c>ShowDamageTooltip</c> embeds them in the mandatory-use
    /// variant (TakeDamagePanel.cs:325-333) — and the standing rule is absolute: no card identity,
    /// ever; reveals only through <see cref="RevealGate"/>. So the variant travels as a number and
    /// the mandatory-use hint renders on the peer WITHOUT the card names: a deliberate, documented
    /// omission in the safe direction.</para>
    ///
    /// <para>Written ONLY while a decision row is really docked AND VISIBLE on the owner's board —
    /// exactly the gate record 12 rides, so the two can never disagree — which also means an idle
    /// packet stays byte-identical to the previous build's. Absence renders as the pre-record look:
    /// mirrored plates with no state, no prompt text. ADDITIVE TLV exactly like every record before
    /// it.</para>
    /// </summary>
    public const byte ExtIdDecisionState = 24;

    /// <summary>Smallest payload <see cref="ExtIdDecisionState"/> can have: the flags byte + the
    /// option count. A shorter record is not trusted (never trust the wire).</summary>
    public const int DecisionStateMinRecordBytes = 2;

    /// <summary>Option cap of <see cref="ExtIdDecisionState"/>. A decision row is a handful of
    /// widgets (the take-damage panel's three, a Yes/No pair, a DialogPopup's option list); the cap
    /// bounds the record at 2 + 8 = 10 payload bytes and is re-clamped on read against the record's
    /// own length.</summary>
    public const int DecisionStateMaxOptions = 8;

    /// <summary>Decision-state flags bits 0..2 — WHICH prompt is docked.</summary>
    public const byte DecisionPromptKindMask = 0x07;

    /// <summary>Prompt kind: none / not attributable (also what a zero flags byte means).</summary>
    public const byte DecisionKindNone = 0;

    /// <summary>Prompt kind: the take-damage burn choice (<c>TakeDamagePanel</c>).</summary>
    public const byte DecisionKindTakeDamage = 1;

    /// <summary>Prompt kind: the short-rest confirmation (<c>YesNoDialog</c>).</summary>
    public const byte DecisionKindShortRestYesNo = 2;

    /// <summary>Prompt kind: a <c>DialogPopup</c> (the burn/redraw or pick confirm).</summary>
    public const byte DecisionKindDialogPopup = 3;

    /// <summary>Shift of the text-variant field inside the decision-state flags byte.</summary>
    public const int DecisionTextVariantShift = 3;

    /// <summary>Decision-state flags bits 3..5 — WHICH prompt-text variant the owner is reading.</summary>
    public const byte DecisionTextVariantMask = 0x38;

    /// <summary>Text variant: no prompt text is shown (the prompt has none — a short-rest Yes/No,
    /// a pick confirm — or the owner's tip window is down). The peer draws no text line.</summary>
    public const byte DecisionTextNone = 0;

    /// <summary>Text variant: the plain take-damage instruction — the game's
    /// <c>GUI_TOOLTIP_DEAL_DAMAGE</c> under the <c>GUI_TOOLTIP_TITLE_DEAL_DAMAGE</c> title
    /// ("Schadensphase: Erleide entweder Schaden, verbrenne …").</summary>
    public const byte DecisionTextDealDamage = 1;

    /// <summary>Text variant: the WOUND wording (<c>GUI_TOOLTIP_PLAYER_WOUNDED</c>, formatted with
    /// the attacked actor's name — which the receiver reads from its OWN replicated model).</summary>
    public const byte DecisionTextWounded = 2;

    /// <summary>Text variant: the SUMMON wording (<c>GUI_TOOLTIP_DEAL_DAMAGE_SUMMON</c> under its
    /// own title).</summary>
    public const byte DecisionTextSummon = 3;

    /// <summary>Text variant: the COMPANION-summon wording
    /// (<c>GUI_TOOLTIP_DEAL_DAMAGE_COMPANION</c>, formatted with the summon's and the summoner's
    /// names — both read from the receiver's own model).</summary>
    public const byte DecisionTextCompanion = 4;

    /// <summary>Text variant: the MANDATORY-USE hint
    /// (<c>GUI_TOOLTIP_DEAL_DAMAGE_MANDATORY_USE</c>). The owner's own line PREFIXES it with the
    /// names of the non-selected mandatory active bonuses; the mirror renders the hint alone,
    /// because those names are card names and card identity never rides this wire.</summary>
    public const byte DecisionTextMandatoryUse = 5;

    /// <summary>Every bit <see cref="ExtIdDecisionState"/>'s flags byte defines today (kind +
    /// variant). Writer and reader both mask with it, so a future sender's extra bits can never
    /// light a meaning here — the board-UI overlay discipline.</summary>
    public const byte DecisionStateDefinedMask = DecisionPromptKindMask | DecisionTextVariantMask;

    /// <summary>Option byte bit 0: the owner can actually PRESS this option right now
    /// (<c>Selectable.IsInteractable()</c>). Clear = greyed.</summary>
    public const byte DecisionOptionOfferedBit = 1 << 0;

    /// <summary>Option byte bit 1: the option is DIMMED — the game's 0.7-alpha "the character does
    /// not have what this option needs" look (<c>UpdateCardRemovalOptionVisuals</c>), which is a
    /// different picture from a merely non-interactable option and must not be collapsed into it.</summary>
    public const byte DecisionOptionDimmedBit = 1 << 1;

    /// <summary>Option byte bit 2: this option is CHOSEN — a toggle that is currently ON (the burn
    /// choice the owner has already picked, before they commit it).</summary>
    public const byte DecisionOptionChosenBit = 1 << 2;

    /// <summary>Every option-byte bit defined today; masked on write AND on read.</summary>
    public const byte DecisionOptionDefinedMask =
        DecisionOptionOfferedBit | DecisionOptionDimmedBit | DecisionOptionChosenBit;

    /// <summary>Pack a prompt kind + text variant into the decision-state flags byte. Both fields
    /// are clamped into their own field width, so a caller can never spill one into the other or
    /// into the reserved bits.</summary>
    public static byte EncodeDecisionFlags(byte kind, byte textVariant) =>
        (byte)((kind & DecisionPromptKindMask)
               | ((textVariant << DecisionTextVariantShift) & DecisionTextVariantMask));

    /// <summary>The prompt kind carried by a decision-state flags byte.</summary>
    public static byte DecodeDecisionKind(byte flags) => (byte)(flags & DecisionPromptKindMask);

    /// <summary>The prompt-text variant carried by a decision-state flags byte.</summary>
    public static byte DecodeDecisionTextVariant(byte flags) =>
        (byte)((flags & DecisionTextVariantMask) >> DecisionTextVariantShift);

    // ---- record 29: DECISION WIDGET IDENTITY -------------------------------------------------
    // RECORD-ID CLAIM, 2026-08-09 (the "die Entscheidungsbuttons sollen auch 1:1 aussehen" round):
    // this change takes id 29 — the first of the free range the record-28 note left open. Ids in
    // use are now 1..17 and 22..30 (30 = HELD-FIGURE STRETCH, claimed 2026-08-11, declared beside
    // record 8); 18..21 stay reserved for the parallel round that claimed them,
    // 31+ are free. No existing record was widened: record 24 has a fixed shape and squeezing a
    // role field into its option byte would have spent its last reserved bits on something that is
    // not a state.

    /// <summary>
    /// Extension record id: WHICH GAME WIDGET each option of the sender's docked decision row IS —
    /// one small ROLE code per option — plus the runtime numbers the take-damage option paints on
    /// itself (the damage amount, and whether that damage is lethal / shielded).
    ///
    /// <para>WHY IT EXISTS (user report 2026-08-09, verbatim: "Die Entscheidungsbuttons sollen auch
    /// 1:1 aussehen … Es sah so aus als wären die Buttons und der Text eigens nachgebaut und hier
    /// nicht die Spielelemente genutzt. Das soll nicht sein — hier sollen auch die Spielicons/Text
    /// etc. genutzt werden"). Records 12 (wordings) and 24 (states) let a peer draw a faithful
    /// DESCRIPTION of the owner's row — and a description is exactly what the mirror then built:
    /// mod-drawn quads carrying the sender's already-rendered label strings. The owner's own dock
    /// draws no description; it docks the GAME's real widgets, with their icons, their damage
    /// number and their art. So the peer's copy has to be those same widgets, and the one thing a
    /// receiver cannot derive is WHICH of them the owner is showing. That is all this record is.</para>
    ///
    /// <para>WHY A ROLE CODE RATHER THAN PIXELS OR WORDS. Every client owns the same game assets
    /// and the same localization tables and — for the take-damage prompt — the same LIVE widget
    /// tree: the game raises <c>TakeDamagePanel</c> as a <c>Singleton</c> on EVERY client and calls
    /// <c>ShowOtherPlayer</c> on the non-deciding ones, which populates it from the same replicated
    /// damage message and then hides the window (TakeDamagePanel.cs:1102-1134). A peer therefore
    /// already HAS the button art, the burn icons, the damage icon, the HUD font and the wording —
    /// in THEIR OWN LANGUAGE, which is more correct than the sender's rendered string could ever
    /// be. A number that NAMES the widget lets them mirror the real thing; a picture or a string
    /// could only ever approximate it.</para>
    ///
    /// <para>LAYOUT — <c>[flags][damage][n][n × role byte]</c>, at least
    /// <see cref="DecisionWidgetMinRecordBytes"/> bytes:
    /// <list type="bullet">
    /// <item><c>flags</c> — <see cref="DecisionWidgetLethalBit"/> (the game is showing its FATAL
    ///   damage icon instead of the normal one), <see cref="DecisionWidgetShieldedBit"/> (the
    ///   amount is painted in the shield colour because items/bonuses reduced it),
    ///   <see cref="DecisionWidgetMandatoryBit"/> (the mandatory-use highlight is lit),
    ///   <see cref="DecisionWidgetDamageValidBit"/> (the <c>damage</c> byte means something).
    ///   Masked to <see cref="DecisionWidgetDefinedMask"/> on write AND on read.</item>
    /// <item><c>damage</c> — the number the take-damage option displays, 0..255, meaningful only
    ///   with <see cref="DecisionWidgetDamageValidBit"/>. A DISPLAYED HP NUMBER, which every
    ///   client's health bars and damage previews already show; not an identity of any kind.</item>
    /// <item><c>n</c> — role count, clamped to <see cref="DecisionStateMaxOptions"/> on both ends
    ///   and INDEX-ALIGNED with record 12's lines and record 24's option bytes (ONE sampler walk
    ///   fills all three, so option <c>i</c> names the same widget in every one of them).</item>
    /// <item>role byte — one of the <c>DecisionRole*</c> codes, clamped with
    ///   <see cref="ClampDecisionRole"/>. <see cref="DecisionRoleUnknown"/> means "this build's
    ///   sampler could not attribute the widget" (a short-rest Yes/No, a <c>DialogPopup</c>'s
    ///   pooled options — see <see cref="DecisionRoleMax"/> for why those are deliberately not
    ///   coded), which a receiver renders with the mod-drawn plate and record 12's wording —
    ///   exactly what every build before this one drew.</item>
    /// </list></para>
    ///
    /// <para>NO CARD IDENTITY, BY CONSTRUCTION: the payload is four small enumerations and a damage
    /// number. Not one byte of it is authored content, a term, a sprite or a name — a role says
    /// "this is the burn-one-available-card toggle", never which card would burn.</para>
    ///
    /// <para>Written ONLY while a decision row is really docked AND VISIBLE on the owner's board —
    /// exactly the gate records 12 and 24 ride, so the three can never disagree — which also means
    /// an idle packet stays byte-identical to the previous build's. Absence renders as the
    /// pre-record look: the mod-drawn plates. ADDITIVE TLV, appended in id order behind record 28;
    /// an older reader steps over it by its length.</para>
    /// </summary>
    public const byte ExtIdDecisionWidgets = 29;

    /// <summary>Smallest payload <see cref="ExtIdDecisionWidgets"/> can have: the flags byte, the
    /// damage byte and the role count. A shorter record is not trusted (never trust the wire).</summary>
    public const int DecisionWidgetMinRecordBytes = 3;

    /// <summary>Widget flags bit 0: the take-damage option is showing the game's FATAL damage icon
    /// (<c>TakeDamagePanel.fatalDamageObject</c>) rather than the normal one — the picture that
    /// tells the owner this damage kills.</summary>
    public const byte DecisionWidgetLethalBit = 1 << 0;

    /// <summary>Widget flags bit 1: the damage amount is painted in the game's
    /// <c>shieldAppliedColor</c> — the owner has toggled shield items / bonuses and the number they
    /// are reading is the REDUCED one.</summary>
    public const byte DecisionWidgetShieldedBit = 1 << 1;

    /// <summary>Widget flags bit 2: the game's mandatory-use highlight
    /// (<c>TakeDamagePanel.mandatoryTakeDamageHighlight</c>) is lit on the take-damage option.</summary>
    public const byte DecisionWidgetMandatoryBit = 1 << 2;

    /// <summary>Widget flags bit 3: the <c>damage</c> byte carries a real number. Clear = the
    /// sender could not read one (no take-damage option in the row, an unparsable label), and the
    /// receiver then leaves its own widget's number alone rather than painting a guessed 0.</summary>
    public const byte DecisionWidgetDamageValidBit = 1 << 3;

    /// <summary>Every bit <see cref="ExtIdDecisionWidgets"/>'s flags byte defines today; masked on
    /// write AND on read, so a future sender's extra bits can never light a meaning here.</summary>
    public const byte DecisionWidgetDefinedMask =
        DecisionWidgetLethalBit | DecisionWidgetShieldedBit
        | DecisionWidgetMandatoryBit | DecisionWidgetDamageValidBit;

    /// <summary>Role: the sampler could not attribute this option to a known game widget (a
    /// <c>DialogPopup</c>'s pooled option button, a prompt a later build adds). The receiver falls
    /// back to the mod-drawn plate with record 12's wording — the pre-record look.</summary>
    public const byte DecisionRoleUnknown = 0;

    /// <summary>Role: <c>TakeDamagePanel.burnAvailableCardsToggle</c> ("burn one available card").</summary>
    public const byte DecisionRoleBurnAvailable = 1;

    /// <summary>Role: <c>TakeDamagePanel.burnDiscardedCardsToggle</c> ("burn two discarded cards").</summary>
    public const byte DecisionRoleBurnDiscarded = 2;

    /// <summary>Role: <c>TakeDamagePanel.takeDamageButton</c> ("take the damage" — the option that
    /// carries the damage icon and the amount).</summary>
    public const byte DecisionRoleTakeDamage = 3;

    /// <summary>
    /// Highest role code this build defines — and deliberately NO HIGHER. Ids 4+ are free for the
    /// short-rest <c>YesNoDialog</c> and the <c>DialogPopup</c> when a receiver can actually
    /// resolve them; they are NOT reserved here, because a role a receiver does not consume is a
    /// wire field with no consumer, which is the failure this project has already shipped once
    /// (see the <c>[Cards] FanCloseDuration</c> note in <c>scripts/check-wire-coverage.py</c>).
    ///
    /// <para>WHY THOSE TWO PROMPTS ARE NOT COVERED THIS ROUND, stated rather than silently skipped:
    /// the take-damage panel is a per-client <c>Singleton</c> the game populates on EVERY machine
    /// (<c>ShowOtherPlayer</c>), so a receiver owns the very widgets it is asked to mirror. The
    /// short-rest <c>YesNoDialog</c> belongs to a HAND (<c>ShortRest.yesNoDialog</c>) and the
    /// <c>DialogPopup</c>'s option buttons are POOLED and labelled at runtime — neither exists on a
    /// peer in the state the owner is looking at, so a role for them could only be resolved to a
    /// different object or to nothing. Those prompts therefore keep the mod-drawn plates and
    /// record 12's wording, which is what every build so far drew for all three.</para>
    ///
    /// Writer and reader both clamp with this, so an unknown code from a later build degrades to
    /// <see cref="DecisionRoleUnknown"/> — the mod-drawn plate — rather than resolving to the wrong
    /// widget.
    /// </summary>
    public const byte DecisionRoleMax = DecisionRoleTakeDamage;

    /// <summary>Clamp a role code to what this build can resolve; anything above the highest
    /// defined code becomes <see cref="DecisionRoleUnknown"/>. Applied on write AND on read.</summary>
    public static byte ClampDecisionRole(byte role) =>
        role <= DecisionRoleMax ? role : DecisionRoleUnknown;

    // ---- record 25: USE BARS ------------------------------------------------------------------

    // ---- record 28: BOARD TUNING (the owner's OWN dial positions) ----------------------------
    // Ids 25, 26 and 27 are taken by records developed in parallel with this one; 18..21 remain
    // reserved. A shipped record id can never be renumbered, so this one is 28 by assignment,
    // not by "the next free number" (that habit already produced one id collision — see the note
    // above record 22).
    //
    // RECORD-ID RESERVATION, 2026-08-09 (the paging round). The un-ceiling of this record took NO
    // NEW RECORD ID (29 has since been claimed by the decision-widget record). That is deliberate,
    // because the obvious cheap fix — "spill into record 29" — was considered and REJECTED: a
    // continuation record only doubles the ceiling, it is the same wall a bit further away, and it
    // spends a scarce id every time the wall is reached again. Paging keeps one id and has no wall
    // at all. Ids in use today: 1..17 and 22..29. FREE: 30..255 (18..21 stay reserved for the
    // parallel round that claimed them).

    /// <summary>
    /// Extension record id: THE OWNER'S OWN TUNING OF THEIR CONTROL BOARD, HAND FAN AND BOARD MESH
    /// — a SPARSE set of <c>[field id][value]</c> entries carrying ONLY the dials whose live value
    /// differs from the SHIPPED default for the sender's synced board style.
    ///
    /// <para>THE GAP IT CLOSES. Every mirrored dock, cap, overlay and fan on a peer's board was
    /// seated from the AUTHORED per-board constants keyed by the synced board style —
    /// <c>RemoteBoardLayout</c>, <c>RemoteBoardFurniture</c>, <c>RemoteTrayVisual</c>,
    /// <c>RemotePickBanner</c>, <c>RemoteBoardTooltip</c>, <c>RemoteHandFan</c> — with a
    /// DELIBERATELY-NOT note in each saying that the owner's private re-tuning stays local. That
    /// was defensible while the board was a rough picture. It is not defensible under the standing
    /// ruling ("alle Interaktionen, Animationen und Anzeigen des Controllboards … so wie der
    /// Spieler sie sieht", 2026-08-08): a player who drags their objectives dock 40 mm up, shrinks
    /// their piles or widens their hand fan is looking at a board that no other client draws. It
    /// was invisible only while nobody re-tuned — and the shipped defaults themselves are a REBASE
    /// of one player's tuning (<c>scripts/rebase-defaults.py</c>), which is proof that the dials
    /// get moved.
    ///
    /// <para>WHY IT COSTS NOTHING IN THE COMMON CASE, which is what makes it payable at all. These
    /// values are config entries a player edits once and then never touches; the overwhelmingly
    /// common state is "every dial is at its shipped default". The record follows
    /// <see cref="MaskSizeDefaultCode"/>'s precedent exactly: a field is written ONLY when it
    /// differs from the compiled default, and when NO field differs the record is not written at
    /// all — the extension tail does not even open for it. An untuned player therefore emits the
    /// exact bytes the previous build emitted, byte for byte. A player who has moved ONE dock pays
    /// 10 bytes (2 TLV header + 1 count + 1 id + 6 value) on a 5 Hz packet — about 50 B/s.</para>
    ///
    /// <para>WHY "DIFFERENT FROM THE DEFAULT" IS A SOUND TEST ACROSS MACHINES: peers must run the
    /// same <see cref="ModBuild"/> to play together at all (the handshake raises the mismatch
    /// dialog before a packet is interpreted), so both ends compile the SAME
    /// <c>Defaults</c>/<c>CardsConfig.BoardDefaults</c> tables. "Field absent" therefore means
    /// exactly "the value the receiver already has", never "some other build's idea of it".</para>
    ///
    /// <para>ONE BOARD ONLY. The dials are per-board-style, but the sender's style already rides
    /// the extras block (byte A bits 5..6), so only the CURRENT style's values are transmitted.
    /// Switching board style re-samples the record.</para>
    ///
    /// <para>PAGED, SINCE 2026-08-09 — READ <see cref="BoardTunePages"/> BEFORE ANYTHING ELSE HERE.
    /// The record's payload is ONE PAGE of the sender's tuning, not the whole of it:
    /// <c>[pageIndex][pageCount][sig u16 LE][idLo][idHi][n][n × field]</c>, a COMPLETE STATEMENT
    /// about the field-id range [idLo, idHi]. The sender cycles one page per extras packet and the
    /// receiver publishes only when it holds every page of one generation. That removed the
    /// record's CAPACITY CEILING — the tail writes a record's length in one byte, the payload had
    /// reached exactly 255, and the eleventh dial of the next feature simply could not ride. The
    /// 1:1 ruling ("Ändert ein Spieler also die Positionen für sich selber, so sollen alle anderen
    /// diese Position bei seinem board auch sehen", 2026-08-09) does not admit a capacity ceiling,
    /// so the ceiling had to go rather than be moved further away. The paging took NO new record
    /// id — everything below still rides id 28.</para>
    ///
    /// <para>LAYOUT OF A PAGE'S FIELD RUN — <c>[n][n × field]</c> in the ASSEMBLED payload, and the
    /// same <c>n × field</c> run behind a page header on the wire; fields in ASCENDING ID ORDER
    /// (deterministic bytes; a reader may early-out). A field is <c>[id][value]</c> and THE ID'S
    /// RANGE FIXES THE VALUE WIDTH, which is what keeps the record self-describing without spending
    /// a length byte per field:
    /// <list type="bullet">
    /// <item><see cref="TuneVecIdMin"/>..<see cref="TuneVecIdMax"/> — 6 bytes, a Vector3 as
    ///   3 × i16 LE in TENTH-MILLIMETRES (<see cref="EncodeTuneLength"/>).</item>
    /// <item><see cref="TuneColorIdMin"/>..<see cref="TuneColorIdMax"/> — 3 bytes, an RGB COLOUR as
    ///   3 × uint8 (<see cref="EncodeTuneColorChannel"/>). NO ALPHA — see the range's own note.</item>
    /// <item><see cref="TuneLengthIdMin"/>..<see cref="TuneLengthIdMax"/> — 2 bytes, one i16 LE
    ///   tenth-millimetre LENGTH (a scalar measured in metres).</item>
    /// <item><see cref="TuneFactorIdMin"/>..<see cref="TuneFactorIdMax"/> — 2 bytes, one i16 LE
    ///   dimensionless FACTOR in THOUSANDTHS (<see cref="EncodeTuneFactor"/>).</item>
    /// <item><see cref="TuneAngleIdMin"/>..<see cref="TuneAngleIdMax"/> — 2 bytes, one i16 LE
    ///   ANGLE in HUNDREDTH-DEGREES (<see cref="EncodeTuneAngle"/>).</item>
    /// <item><see cref="TuneCountIdMin"/>..<see cref="TuneCountIdMax"/> — 1 byte, an integer
    ///   COUNT of cards.</item>
    /// <item>ids above <see cref="TuneCountIdMax"/> are RESERVED and have no defined width. A
    ///   reader that meets one cannot skip it safely, so it keeps the fields it has already parsed
    ///   and ABANDONS THE REST of the record — suppression, the designed failure direction here as
    ///   everywhere. It cannot happen between same-build peers; it exists so that it cannot
    ///   corrupt anything if it ever does.</item>
    /// </list></para>
    ///
    /// <para>QUANTIZATION AND ITS WORST-CASE VISIBLE ERROR. Tenth-millimetres for everything
    /// measured in metres (±3.2767 m, error ≤0.05 mm — the board is ~0.4 m wide and every offset
    /// config range is inside ±0.5 m); thousandths for dimensionless factors (±32.767 with 8×
    /// headroom over the widest config range, error ≤0.0005, i.e. ≤0.25 mm on a 0.5 m dock);
    /// hundredth-degrees for angles (±327.67°, error ≤0.005°, i.e. ≤0.04 mm at the edge of a 0.5 m
    /// board); 8 BITS PER COLOUR CHANNEL, which is not an approximation of a colour but the exact
    /// container the display, the sprite art and every hex swatch a human writes already use — a
    /// UI colour quantized to 1/255 is VISUALLY LOSSLESS, not merely close. Every one of those is an
    /// order of magnitude below what an eye resolves at arm's length, and every code is readable
    /// as-is in a hardware log.</para>
    ///
    /// <para>NO IDENTITY, NO GAMEPLAY: the payload is a list of the sender's own cosmetic dial
    /// positions. Nothing here consults a card, an actor or the game model.</para>
    ///
    /// <para>ADDITIVE TLV exactly like every record before it: an older peer steps over it by its
    /// length and keeps seating everything at the shipped defaults — today's look, never a broken
    /// one.</para>
    /// </summary>
    public const byte ExtIdBoardTuning = 28;

    /// <summary>
    /// Structural field cap of <see cref="ExtIdBoardTuning"/>: THE SIZE OF THE FIELD-ID SPACE — 247
    /// usable ids (47 vec + 16 colour + 64 length + 64 factor + 32 angle + 24 count; 248..255 stay
    /// reserved).
    /// Each id may appear at most once in an assembled tuning, so this is simultaneously the largest
    /// number of dials the record can ever carry and the proof that the assembled payload's
    /// single-byte field count can never overflow (247 &lt; 255).
    ///
    /// <para>THIS IS NO LONGER A BYTE BUDGET, AND THAT IS THE POINT. Until 2026-08-09 this constant
    /// read 66 and existed to keep the record's worst case at exactly 255 — the extension tail's
    /// one-byte per-record length ceiling — because <c>PresenceSerializer.Write</c> refuses a bigger
    /// payload silently, and only for the player who had moved every dial: the least likely person
    /// to be testing and the hardest case to reproduce. The swap dials had already been squeezed
    /// into a narrower container purely to fit (see <see cref="TuneFanSwapSpin"/>), and the next ten
    /// dials could not ride the record at all. Under the 1:1 ruling that is not a tight budget, it
    /// is a broken guarantee, so the record was PAGED instead (see <see cref="BoardTunePages"/>):
    /// per-packet size stays bounded by the tail, total capacity is bounded only by the id space
    /// above — which is a BUILD-TIME resource, since a new dial needs a new <c>Tune*</c> constant.
    /// Running out is therefore something a human reads at a compiler, never a drop a player never
    /// sees; <c>scripts/check-wire-coverage.py</c> fails the guard while any range approaches
    /// full.</para>
    /// </summary>
    public const int BoardTuneMaxFields = 247;

    /// <summary>
    /// Worst-case byte length of a COMPLETE field run — every usable id present at its own width:
    /// 47×7 + 16×4 + 64×3 + 64×3 + 32×3 + 24×2 = 921. Sizes the sender's sample buffer and bounds
    /// the receiver's accumulator, so neither is ever sized from a number the wire supplied.
    ///
    /// <para>IT READ 969 UNTIL THE COLOUR RANGE WAS CARVED (2026-08-09). Sixteen ids moved out of the
    /// vec range's unused tail into <see cref="TuneColorIdMin"/>..<see cref="TuneColorIdMax"/> at
    /// 3 bytes instead of 6, so the id COUNT is unchanged at 247 and the worst case got SMALLER. It
    /// is stated as an arithmetic expression rather than a literal for exactly this reason: a range
    /// boundary moves in one place and every bound derived from it follows.</para>
    /// </summary>
    public const int BoardTuneMaxFieldBytes = 47 * 7 + 16 * 4 + 64 * 3 + 64 * 3 + 32 * 3 + 24 * 2;

    /// <summary>Header of one record-28 PAGE, in bytes:
    /// <c>[pageIndex][pageCount][sig lo][sig hi][idLo][idHi][fieldCount]</c>.</summary>
    public const int BoardTunePageHeaderBytes = 7;

    /// <summary>Offsets inside a page header (see <see cref="BoardTunePageHeaderBytes"/>).</summary>
    public const int BoardTunePageIndexAt = 0;
    public const int BoardTunePageCountAt = 1;
    public const int BoardTunePageSigAt = 2;
    public const int BoardTunePageIdLoAt = 4;
    public const int BoardTunePageIdHiAt = 5;
    public const int BoardTunePageFieldCountAt = 6;

    /// <summary>Field bytes one page may carry: the tail's one-byte length ceiling (255) less the
    /// page header. THIS is where 255 still bites — and it is now a PER-PAGE bound with another page
    /// behind it, not a cap on how much a player may tune.</summary>
    public const int BoardTunePageMaxFieldBytes = 255 - BoardTunePageHeaderBytes;

    /// <summary>
    /// Pages a generation may claim. DERIVED, not guessed: a greedy split at field boundaries wastes
    /// at most 6 bytes per page (a 7-byte vec3 that will not fit a 6-byte remainder), so every page
    /// but the last carries ≥242 field bytes and <see cref="BoardTuneMaxFieldBytes"/> = 921 needs at
    /// most ⌈921 / 242⌉ = 4. Eight leaves headroom for a future widening of the id space while still
    /// bounding the receiver's accumulator at 8 slices — the point being that the bound comes from
    /// the id space rather than from the wire, so a corrupt sender cannot size an allocation here.
    /// </summary>
    public const int BoardTuneMaxPages = 8;

    /// <summary>Minimum payload of <see cref="ExtIdBoardTuning"/>: one page header. A page with a
    /// ZERO field count is legal and meaningful — it says "nothing is tuned in my id range" — so the
    /// reader's floor is the header, never the presence of a field.</summary>
    public const int BoardTuneMinRecordBytes = BoardTunePageHeaderBytes;

    /// <summary>Minimum length of an ASSEMBLED tuning payload (the field-count byte alone) — what
    /// <see cref="FindBoardTuneField"/> and <see cref="RemoteBoardTuning"/> read. Deliberately NOT
    /// the same number as <see cref="BoardTuneMinRecordBytes"/>: one is a wire page, the other is
    /// the receiver's assembled result, and conflating them is how a paging bug hides.</summary>
    public const int BoardTuneAssembledMinBytes = 1;

    // ---- field id RANGES (the range is the value width — see the record doc) ------------------

    /// <summary>First / last id whose value is a Vector3 (6 bytes, 3 × i16 tenth-mm).</summary>
    public const byte TuneVecIdMin = 1;
    public const byte TuneVecIdMax = 47;

    /// <summary>
    /// First / last id whose value is an RGB COLOUR — 3 bytes, one uint8 per channel
    /// (<see cref="EncodeTuneColorChannel"/>).
    ///
    /// <para>THE RANGE WAS CARVED OUT OF THE VEC RANGE'S UNUSED TAIL (2026-08-09), which is why
    /// <see cref="TuneVecIdMax"/> moved 63 → 47. The vec range spends 7 bytes per field and had used
    /// 15 of its 63 ids in the record's whole life; the colour dials needed a container and the
    /// alternative — three FACTOR fields per colour — would have cost 18 ids and 54 bytes for the
    /// six colours a keycap set has. THE ID SPACE IS THE SCARCE RESOURCE HERE (247 total), so the
    /// answer that spends 6 ids and 24 bytes wins on the axis that actually binds. The id COUNT is
    /// unchanged (47 + 16 = the 63 the vec range used to claim); only the width of the top sixteen
    /// changed, and <see cref="BoardTuneMaxFieldBytes"/> got smaller as a result.</para>
    ///
    /// <para>NO ALPHA, CHECKED RATHER THAN ASSUMED. All six colours this range carries are built
    /// with a hardwired opaque alpha on the LOCAL side — <c>WorldUI.ButtonTuning.LabelColor</c> /
    /// <c>LabelOutlineColor</c> / <c>Tint3</c> each close with <c>1f</c>, and the 21 [ButtonColors]
    /// config entries have no A channel to bind. An alpha byte would therefore be a fourth byte per
    /// field carrying the constant 255 forever. The one alpha near this family (the disabled cluster
    /// label's 0.35 fade) is a renderer constant, not a dial. If a [ButtonColors] A entry is ever
    /// added, it needs a NEW range — widening this one would silently re-cut every shipped field.</para>
    ///
    /// <para>8 BITS PER CHANNEL IS NOT A COMPROMISE. The caps are drawn from 8-bit sprite art through
    /// an 8-bit-per-channel framebuffer, and the defaults themselves are authored as hex swatches
    /// (#FBF3E0). The quantized code IS the colour a display can show, so the round trip is visually
    /// lossless — and, as everywhere in this record, "differs from the shipped default" is decided
    /// on that CODE and never on the float, so a config file's text round-trip cannot make an
    /// untuned player start emitting a colour field forever.</para>
    /// </summary>
    public const byte TuneColorIdMin = 48;
    public const byte TuneColorIdMax = 63;

    /// <summary>First / last id whose value is a metre LENGTH (2 bytes, i16 tenth-mm).</summary>
    public const byte TuneLengthIdMin = 64;
    public const byte TuneLengthIdMax = 127;

    /// <summary>First / last id whose value is a dimensionless FACTOR (2 bytes, i16 thousandths).</summary>
    public const byte TuneFactorIdMin = 128;
    public const byte TuneFactorIdMax = 191;

    /// <summary>First / last id whose value is an ANGLE in degrees (2 bytes, i16 hundredth-deg).</summary>
    public const byte TuneAngleIdMin = 192;
    public const byte TuneAngleIdMax = 223;

    /// <summary>First / last id whose value is an integer COUNT (1 byte).</summary>
    public const byte TuneCountIdMin = 224;
    public const byte TuneCountIdMax = 247;

    // ---- field ids — WIRE CONSTANTS. Append only inside a range; never renumber. ---------------
    // VECTOR3 (6 B): board-local / mount-local offsets, tray-root metres.

    /// <summary>[Cards] ObjectivesOffset_{board} — the objectives dock's seat.</summary>
    public const byte TuneObjectivesOffset = 1;
    /// <summary>[Cards] ElementsOffset_{board} — the element column's seat.</summary>
    public const byte TuneElementsOffset = 2;
    /// <summary>[Cards] InitiativeOffset_{board} — the initiative-track dock (a bare offset, no base).</summary>
    public const byte TuneInitiativeOffset = 3;
    /// <summary>[Cards] PileOffset_{board} — the discard/burnt/items stack column.</summary>
    public const byte TunePileOffset = 4;
    /// <summary>[Cards] ActiveOffset_{board} — the active/persistent card column.</summary>
    public const byte TuneActiveOffset = 5;
    /// <summary>[Cards] ReadoutOffset_{board} — the "Runde N" readout.</summary>
    public const byte TuneReadoutOffset = 6;
    /// <summary>[Cards] PickBannerOffset_{board} — the pick-status placard above the board.</summary>
    public const byte TunePickBannerOffset = 7;
    /// <summary>[Cards] HoverHintOffset_{board} — the board's tooltip AREA corner.</summary>
    public const byte TuneHoverHintOffset = 8;
    /// <summary>[Cards] ConfirmUndoOffset_{board} — the confirm/undo keycap column.</summary>
    public const byte TuneConfirmUndoOffset = 9;
    /// <summary>[Cards] RestButtonOffset_{board} — the short/long rest disc pair.</summary>
    public const byte TuneRestButtonOffset = 10;
    /// <summary>[Cards] PinOffset_{board} — the FOLLOW/PIN keycap.</summary>
    public const byte TunePinOffset = 11;
    /// <summary>[Cards] ItemUseSlotOffset_{board} — the dedicated item-use recess.</summary>
    public const byte TuneItemUseSlotOffset = 12;
    /// <summary>[Cards] DecisionOffset_{board} — the docked decision row.</summary>
    public const byte TuneDecisionOffset = 13;
    /// <summary>[Cards] SlotOverlayOffset_{board} — the slot glow / resting card offset.</summary>
    public const byte TuneSlotOverlayOffset = 14;
    /// <summary>[Cards] AssetOffset_{board} — the BOARD MESH's own pose offset inside the board
    /// root (<c>PlayTray.SetAssetPose</c>). Note the bronze board ships a non-zero default, so this
    /// is a live field, not a hypothetical one.</summary>
    public const byte TuneAssetOffset = 15;
    /// <summary>
    /// [Cards] ClusterOffset_{board} — the turn-flow button cluster's per-board seat, i.e. where
    /// the docked SKIP cap ("Bewegung überspringen") stands on the board.
    ///
    /// <para>THIS ID IS A DEBT BEING PAID, NOT A NEW DIAL. It stood in
    /// <c>scripts/check-wire-coverage.py</c> as a NO-OP exemption reading "ButtonCluster.
    /// AttachDocked reads the mount's ROTATION and SCALE only — the position never moves the
    /// rendered cluster, locally or remotely", which was TRUE and was itself the bug the user
    /// reported (ModBuild 97: "Die Offsets bei den Überspringen-Tasten haben keinen Einfluss").
    /// The rigid-dock lag fix had reparented the cluster off the mount and dropped the mount's
    /// translation. Now that <c>ButtonCluster.AttachDocked</c> consumes it again, the exemption is
    /// gone and the dial rides record 28 like its ClusterScale twin (id 133) — the alternative
    /// being a wire field whose receiver draws nothing, which is the trap
    /// <c>FanCloseDuration</c> was un-wired to avoid.</para>
    ///
    /// <para>Covers [Cards] ClusterOffset_{board} for every style.</para>
    /// </summary>
    public const byte TuneClusterOffset = 16;

    // COLOUR (3 B, uint8 R / G / B): THE [ButtonColors] FAMILY — what a player's keycaps are
    // lettered and tinted in.
    //
    // WHY THESE ARE HERE AT ALL. An audit note used to classify the 21 [ButtonColors] entries as
    // "a look decision, not a wire gap", on the ground that RemoteBoardFurniture never read them
    // even at their defaults. The user overruled it, verbatim (2026-08-09): "Bitte implementier
    // auch die Farben und Formen der Knöpfe, dass sie über die Leitung gehen - so dass das remote
    // Board 1:1 das anzeigt was der Spieler sieht". The audit note had the causality backwards —
    // a mirror that reads NONE of a dial family is not evidence the family is local, it is a
    // second defect stacked on the first, and it was: the shipped cap TINT is 0.5 grey, so every
    // mirrored keycap was drawn at TWICE its owner's brightness for two completely untuned
    // players. The renderer reads the family now (RemoteBoardFurniture's cap-palette block) and
    // these six fields carry the owner's own values on top of it.
    //
    // SIX FIELDS FOR EIGHTEEN CHANNELS. The 21 [ButtonColors] entries are 18 colour channels
    // (6 colours) + 2 bools + 1 width; a colour is ONE field of three bytes here, never three
    // fields of two, because the id space is what binds (see TuneColorIdMin). The bools and the
    // width ride the count and factor ranges below with the rest of their kind.

    /// <summary>[ButtonColors] Label{rgb} — the engraved keycap LABEL's fill colour (LabelR/G/B),
    /// the bright warm parchment every cap letter is drawn in.</summary>
    public const byte TuneLabelColor = 48;
    /// <summary>[ButtonColors] LabelOutline{rgb} — the dark keyline ringing those letters
    /// (LabelOutlineR/G/B). Its WIDTH is a factor field and its on/off a count field.</summary>
    public const byte TuneLabelOutlineColor = 49;
    /// <summary>[ButtonColors] BoardCapTint{rgb} — the Confirm / Undo / item-USE cap FACE tint, a
    /// multiplier over the shared state palette (shipped 0.5 grey, i.e. half brightness).</summary>
    public const byte TuneBoardCapTint = 50;
    /// <summary>[ButtonColors] DashCapTint{rgb} — the gear / Fixiert (follow-pin) plate face tint.</summary>
    public const byte TuneDashCapTint = 51;
    /// <summary>[ButtonColors] ClusterCapTint{rgb} — the round-phase cluster (turn-flow SKIP) cap
    /// face tint.</summary>
    public const byte TuneClusterCapTint = 52;
    /// <summary>[ButtonColors] RestCapTint{rgb} — the short / long REST keycap face tint.</summary>
    public const byte TuneRestCapTint = 53;

    // LENGTH (2 B, tenth-mm): scalars measured in metres.

    /// <summary>[Cards] PileSpacing_{board} — distance between two stack centres.</summary>
    public const byte TunePileSpacing = 64;
    /// <summary>[Cards] RestButtonDiameter_{board}.</summary>
    public const byte TuneRestButtonDiameter = 65;
    /// <summary>[Cards] RestButtonSpacing_{board}.</summary>
    public const byte TuneRestButtonSpacing = 66;
    /// <summary>[Cards] GenericButtonSpacing_{board} — the confirm/undo pair spacing.</summary>
    public const byte TuneGenericButtonSpacing = 67;
    /// <summary>[Cards] SlotOverlaySpacing_{board} — the glow pair's spread inside a recess.</summary>
    public const byte TuneSlotOverlaySpacing = 68;
    /// <summary>[Cards] DecisionGap_{board} — the decision text↔button gap, in metres by design.</summary>
    public const byte TuneDecisionGap = 69;
    /// <summary>[Cards] CardWidth — the card slab width every remote card visual is built from.</summary>
    public const byte TuneCardWidth = 70;
    /// <summary>[Cards] FanPalmOffset — how far up the palm normal the hand fan floats.</summary>
    public const byte TuneFanPalmOffset = 71;
    /// <summary>[Cards] FanEffectiveRadius — the hand fan's arc radius.</summary>
    public const byte TuneFanEffectiveRadius = 72;
    /// <summary>[Cards] FanSideDepthCurve — the depth bow at the ends of a full hand.</summary>
    public const byte TuneFanSideDepthCurve = 73;
    /// <summary>[Cards] FanSplitMultiplier — base sideways slide of a split neighbour.</summary>
    public const byte TuneFanSplitMultiplier = 74;
    /// <summary>[Cards] FanSelectedPopForward — how far a highlighted card comes toward the viewer.</summary>
    public const byte TuneFanSelectedPopForward = 75;
    /// <summary>[Cards] ItemFanOpenArc — how far an emerging item chip bows toward its viewer at
    /// mid-flight. Part of the ITEM-FAN ANIMATION set (ids 76 / 144..149 / 197), which is on the
    /// wire for the reason the standing 1:1 ruling states outright: "alle Interaktionen,
    /// **Animationen** und Anzeigen des Controllboards" are what a peer must see, so an owner who
    /// re-tunes how their item fan opens must be seen re-tuning it.</summary>
    public const byte TuneItemFanOpenArc = 76;

    // The HAND FAN's CHARACTER-SWAP EXCHANGE (ids 77..78 here, plus 150..153 and 226..227). Same
    // reason as the item-fan set above — the ruling names ANIMATIONS — and the same shape: written
    // only when the owner has moved the dial, absent for everybody else.

    /// <summary>[Cards] FanSwapTravel — how far past the arc's end the swap's gather/deal point sits.</summary>
    public const byte TuneFanSwapTravel = 77;
    /// <summary>[Cards] FanSwapArc — the swap's mid-flight depth amplitude (leaver back, arriver forward).</summary>
    public const byte TuneFanSwapArc = 78;

    // ---- THE FIRST DIALS THE OLD 255-BYTE CEILING HAD LOCKED OUT (2026-08-09, the paging round).
    // Ids 79 and 154..160 / 198..200 are the pile/browse-fan geometry and the HAND fan's reveal
    // animation. They are ordinary dials with ordinary consumers on the remote side; the ONLY reason
    // they were not on the wire is that the record was full — the remote fans held their values as
    // BARE LITERALS (`Radius = 0.16f * 1.7f`, `MaxStepDegrees = 10f`), which is precisely the failure
    // scripts/check-remote-defaults.py exists to catch and which the 1:1 ruling forbids outright.

    /// <summary>[Cards] FanRadius — the base arc radius the BOARD PILE fans (items / discard /
    /// burnt) multiply by their own per-pile factor. Not the hand fan's, which has had its own
    /// effective radius since <see cref="TuneFanEffectiveRadius"/>.</summary>
    public const byte TuneFanRadius = 79;

    /// <summary>[Cards] ItemBerthRingThickness — the outline thickness of the card-shaped item-use
    /// recess, in metres. The id was RESERVED here by the paging round for the item-use BERTH round
    /// developed in parallel with it, so that the merge would be a sampler line and a struct member
    /// and never an id negotiation: an id, once shipped, can never be renumbered, and two workers
    /// picking "the next free number" is exactly how this file already collected one collision (see
    /// the note above record 22). The reservation was CLAIMED in that merge — this is a live field,
    /// written by <c>BoardTuningSampler.Sample</c> and resolved by <c>RemoteBoardTuning</c>.</summary>
    public const byte TuneItemBerthRingThickness = 80;

    // ---- THE KEYCAP GEOMETRY FAMILY (ids 81..98 + 228), 2026-08-09 ------------------------------
    //
    // WHY THESE EIGHTEEN LENGTHS AND ONE ENUM ARRIVE TOGETHER. They are the [RoundButtons] /
    // [BoardButtons] / [BoardDashboard] / [RestButtons] cap dials — the sizes, seats and press
    // travels of every 3D keycap standing on a control board — and until this build EVERY ONE of
    // them was mirrored on a peer's board as a FROZEN CONSTANT in RemoteBoardFurniture
    // (BoardCapW/H/D, PinCapW, DashCapH/D, RestCapD, TransientCapR/D and the four *Travel copies,
    // all pinned by scripts/check-remote-defaults.py). scripts/check-wire-coverage.py carried the
    // whole set as PENDING with one shared reason: "the renderer is owned by a parallel round —
    // wire it when that lands". That round HAS landed, so the reason expired and the debt is paid
    // here rather than restated.
    //
    // THE REPORT THAT FORCED THE ISSUE (user, hardware ModBuild 96, on the SKIP cap): "Weiterhin
    // vermisse ich die Einstellungen im Debug Menu für genau diese 'Überspringen'-Tasten (offsets,
    // Form, Größe, etc..)". The [RoundButtons] set IS that cap's dial family — the docked cluster
    // shows only its Skip member (ButtonCluster.Tick forces the Ready/Undo twins off) — so making
    // those dials findable and making them REACH THE PEERS are the same task under the 1:1 ruling:
    // a player who re-shapes their skip cap must be seen re-shaping it, and a peer's copy currently
    // draws the shipped square regardless.
    //
    // WHY LENGTHS RATHER THAN ONE VEC3 FOR THE OFFSETS. [RoundButtons] OffsetX/Y/Z are three
    // SEPARATE config entries (ButtonTuning binds them individually; only the accessor composes a
    // Vector3), and scripts/check-wire-coverage.py resolves coverage per (section, key) from a
    // field id's own doc comment — one id can name exactly one key. Three length fields therefore
    // cost one byte more than a vec3 and buy honest per-dial coverage; the vec3 range is for dials
    // that are ONE config entry holding three components.

    /// <summary>[RoundButtons] OffsetX — sideways seat of the docked turn-flow cap group (the SKIP
    /// cap), tray-root-local metres. Added to <c>ButtonCluster</c>'s fixed column anchor.</summary>
    public const byte TuneRoundOffsetX = 81;
    /// <summary>[RoundButtons] OffsetY — up-board seat of the same group.</summary>
    public const byte TuneRoundOffsetY = 82;
    /// <summary>[RoundButtons] OffsetZ — how far out of the board face the group is seated, on top
    /// of the depth-correct proud lift.</summary>
    public const byte TuneRoundOffsetZ = 83;
    /// <summary>[RoundButtons] CapSize — the turn-flow cap RADIUS ceiling (its exact radius while a
    /// single cap occupies the column, which docked is always the case).</summary>
    public const byte TuneRoundCapSize = 84;
    /// <summary>[RoundButtons] Width — the turn-flow cap's width while its shape is Square.</summary>
    public const byte TuneRoundCapWidth = 85;
    /// <summary>[RoundButtons] Height — the same cap's height while Square.</summary>
    public const byte TuneRoundCapHeight = 86;
    /// <summary>[RoundButtons] Depth — the turn-flow cap's extrusion toward the player.</summary>
    public const byte TuneRoundCapDepth = 87;
    /// <summary>[RoundButtons] Travel — how far the turn-flow cap sinks under a press. A peer's
    /// mirrored cap dips this far on the synced press edge, so a re-tuned press looks the same on
    /// every screen.</summary>
    public const byte TuneRoundCapTravel = 88;

    /// <summary>[BoardButtons] Width — the Confirm/Undo keycap width.</summary>
    public const byte TuneBoardCapWidth = 89;
    /// <summary>[BoardButtons] Height — the Confirm/Undo keycap height.</summary>
    public const byte TuneBoardCapHeight = 90;
    /// <summary>[BoardButtons] Depth — the Confirm/Undo keycap extrusion.</summary>
    public const byte TuneBoardCapDepth = 91;
    /// <summary>[BoardButtons] Travel — the Confirm/Undo press travel.</summary>
    public const byte TuneBoardCapTravel = 92;

    /// <summary>[BoardDashboard] PinWidth — the follow/pin ('Fixiert') plate width.</summary>
    public const byte TuneDashPinWidth = 93;
    /// <summary>[BoardDashboard] Height — the gear / follow-pin plate height.</summary>
    public const byte TuneDashCapHeight = 94;
    /// <summary>[BoardDashboard] Depth — the gear / follow-pin plate extrusion.</summary>
    public const byte TuneDashCapDepth = 95;
    /// <summary>[BoardDashboard] Travel — the gear / follow-pin press travel.</summary>
    public const byte TuneDashCapTravel = 96;

    /// <summary>[RestButtons] Depth — the short/long rest disc thickness (both shapes).</summary>
    public const byte TuneRestCapDepth = 97;
    /// <summary>[RestButtons] Travel — the short/long rest press travel.</summary>
    public const byte TuneRestCapTravel = 98;

    // THE REST CAPS' SQUARE SIDE LENGTHS. These two were a stated PENDING debt for one build with a
    // precise reason — "the mirrored rest cap is hardwired round (no Square branch), so sampling
    // them would put bytes on the wire no receiver reads", the FanCloseDuration rule — and the
    // reason is gone: RemoteBoardFurniture branches Round/Square on the owner's [Cards]
    // RestButtonShape_{board} now (id 231), and the Square branch needs exactly these. They apply
    // ONLY while that shape is Square, exactly as on the local board (RestControls.EnsureBuilt: a
    // round disc keeps the per-board DIAMETER and the square cap takes [RestButtons] W/H).

    /// <summary>[RestButtons] Width — the rest keycap width while the shape is Square.</summary>
    public const byte TuneRestCapWidth = 99;
    /// <summary>[RestButtons] Height — the rest keycap height while the shape is Square.</summary>
    public const byte TuneRestCapHeight = 100;

    // FACTOR (2 B, thousandths): dimensionless multipliers.

    /// <summary>[Cards] ObjectivesScale_{board}.</summary>
    public const byte TuneObjectivesScale = 128;
    /// <summary>[Cards] ObjectivesWidth_{board} — the wrap-column width multiplier.</summary>
    public const byte TuneObjectivesWidth = 129;
    /// <summary>[Cards] ElementsScale_{board}.</summary>
    public const byte TuneElementsScale = 130;
    /// <summary>[Cards] PileScale_{board}.</summary>
    public const byte TunePileScale = 131;
    /// <summary>[Cards] ActiveCardScale_{board}.</summary>
    public const byte TuneActiveCardScale = 132;
    /// <summary>[Cards] ClusterScale_{board} — the docked turn-flow button cluster.</summary>
    public const byte TuneClusterScale = 133;
    /// <summary>[Cards] DecisionScale_{board}.</summary>
    public const byte TuneDecisionScale = 134;
    /// <summary>[Cards] FanFlatCurvatureFactor.</summary>
    public const byte TuneFanFlatCurvatureFactor = 135;
    /// <summary>[Cards] FanTiltFactor.</summary>
    public const byte TuneFanTiltFactor = 136;
    /// <summary>[Cards] FanFaceViewer — per-card toe-in gain.</summary>
    public const byte TuneFanFaceViewer = 137;
    /// <summary>[Cards] FanCurvePower — bow exponent.</summary>
    public const byte TuneFanCurvePower = 138;
    /// <summary>[Cards] FanGazeApexFollow — gaze relief amplitude.</summary>
    public const byte TuneFanGazeApexFollow = 139;
    /// <summary>[Cards] FanSplitFalloff.</summary>
    public const byte TuneFanSplitFalloff = 140;
    /// <summary>[Cards] FanHoverSplitScale.</summary>
    public const byte TuneFanHoverSplitScale = 141;
    /// <summary>[WorldUI] HoverInfoScale — the size dial the board tooltip renders at.</summary>
    public const byte TuneHoverInfoScale = 142;
    /// <summary>[WorldUI] CanvasScaleMm — the other factor in the board tooltip's world scale
    /// (<c>WorldTooltips</c>: <c>CanvasScaleMm × 0.001 × boardScale × 0.5 × sizeDial</c>). Sent
    /// with <see cref="TuneHoverInfoScale"/> because sending only one of a product would leave the
    /// mirror wrong for anyone who tuned the other.</summary>
    public const byte TuneCanvasScaleMm = 143;

    // The ITEM FAN's OPEN/CLOSE ANIMATION (ids 144..149 here, plus 76 and 197). Four of the six are
    // DURATIONS IN SECONDS carried in the FACTOR range, which is deliberate and not a category
    // error: the id range states the VALUE WIDTH, not the unit (see the record doc), and the factor
    // width is an i16 in thousandths — millisecond resolution over ±32 s, which is finer than any
    // animation dial can be tuned and far wider than any of them can be set.

    /// <summary>[Cards] ItemFanOpenDuration — seconds one item chip takes to fly out of the stack.</summary>
    public const byte TuneItemFanOpenDuration = 144;
    /// <summary>[Cards] ItemFanOpenStagger — the deal-out ripple's per-place delay, seconds.</summary>
    public const byte TuneItemFanOpenStagger = 145;
    /// <summary>[Cards] ItemFanSeedScale — the size a chip starts the fly-out at (and ends the
    /// collapse at), as a fraction of its seated size.</summary>
    public const byte TuneItemFanSeedScale = 146;
    /// <summary>[Cards] ItemFanSettleOvershoot — the back-ease strength shared by the fly-out's
    /// overshoot and the collapse's wind-up.</summary>
    public const byte TuneItemFanSettleOvershoot = 147;
    /// <summary>[Cards] ItemFanCloseDuration — seconds one item chip takes to fall back in.</summary>
    public const byte TuneItemFanCloseDuration = 148;
    /// <summary>[Cards] ItemFanCloseStagger — the reverse ripple's per-place delay, seconds.</summary>
    public const byte TuneItemFanCloseStagger = 149;

    // The HAND FAN's CHARACTER-SWAP EXCHANGE (ids 150..153 here, plus 77..78 and 226..227). Two of
    // the four are SECONDS in the factor range, for the reason stated above the item-fan block: the
    // id range fixes the value WIDTH, not the unit.

    /// <summary>[Cards] FanSwapDuration — seconds one card takes to leave or join during a swap.</summary>
    public const byte TuneFanSwapDuration = 150;
    /// <summary>[Cards] FanSwapStagger — the exchange wipe's per-card delay along the arc, seconds.</summary>
    public const byte TuneFanSwapStagger = 151;
    /// <summary>[Cards] FanSwapSeedScale — a card's size at the swap's gather/deal point.</summary>
    public const byte TuneFanSwapSeedScale = 152;
    /// <summary>[Cards] FanSwapSettleOvershoot — the swap's shared back-ease strength.</summary>
    public const byte TuneFanSwapSettleOvershoot = 153;

    // The HAND FAN's REVEAL animation and the BOARD PILE fans' geometry — the dials the 255-byte
    // ceiling had locked out (see the block at id 79). The reveal set is on the wire for the same
    // reason the item fan's and the swap's are: the 1:1 ruling names ANIMATIONS outright, and
    // RemoteHandFan drew every peer's reveal at this client's own compiled constants. Seconds in the
    // FACTOR range is the established convention here — an id range fixes the value WIDTH, never the
    // unit (see the item-fan block above).

    /// <summary>[Cards] FanOpenDuration — seconds one hand-fan card takes to appear on reveal.</summary>
    public const byte TuneFanOpenDuration = 154;
    /// <summary>[Cards] FanOpenStagger — the reveal ripple's per-card delay outward from the middle.</summary>
    public const byte TuneFanOpenStagger = 155;
    /// <summary>[Cards] FanCloseDuration — seconds the hand fan takes to collapse.</summary>
    public const byte TuneFanCloseDuration = 156;
    /// <summary>[Cards] CardLerpSpeed — the exponential rate every card flies to its slot at, board
    /// pile fans included. A peer's cards arrived at this client's rate, not the owner's.</summary>
    public const byte TuneCardLerpSpeed = 157;
    /// <summary>[Cards] FanRadiusFactor_Items — the items pile fan's radius multiplier over
    /// <see cref="TuneFanRadius"/>.</summary>
    public const byte TuneFanRadiusFactorItems = 158;
    /// <summary>[Cards] FanRadiusFactor_Discard.</summary>
    public const byte TuneFanRadiusFactorDiscard = 159;
    /// <summary>[Cards] FanRadiusFactor_Burnt.</summary>
    public const byte TuneFanRadiusFactorBurnt = 160;

    // The USABLE-ITEM CUE and the ITEM-USE BERTH (ids 161..169 here, plus 80). The paging round
    // RESERVED these ids for the re-art round developed in parallel with it (see the note at id 80);
    // the merge CLAIMED them, so all ten are live fields now — sampled, resolved and consumed by
    // RemoteControlBoard's mirrored pile cue and RemoteBoardFurniture's mirrored berth. They landed
    // in that parallel round as FROZEN constants for one reason only: record 28 stood at exactly its
    // 255-byte ceiling and could not carry an eleventh dial. Paging removed the ceiling, so the
    // reason expired and the 1:1 ruling ("Ändert ein Spieler also die Positionen für sich selber, so
    // sollen alle anderen diese Position bei seinem board auch sehen", 2026-08-09) applies with
    // nothing left to weigh against it.
    //
    // SECONDS AND PER-SECOND RATES IN THE FACTOR RANGE is the established convention here and not a
    // category error: an id range fixes the value WIDTH, never the unit (see the record doc and the
    // item-fan block above). The one dial that does NOT fit the factor width's own NUMERIC range is
    // called out at its id.

    /// <summary>[Cards] ItemCueBeatSeconds — the usable-item cue's heartbeat period, seconds.</summary>
    public const byte TuneItemCueBeatSeconds = 161;
    /// <summary>[Cards] ItemCueRingReach — how far a cue ring travels off the pile, as a multiple of
    /// the pile's own footprint.</summary>
    public const byte TuneItemCueRingReach = 162;
    /// <summary>[Cards] ItemCueRingAlpha — peak opacity of those rings.</summary>
    public const byte TuneItemCueRingAlpha = 163;

    // ID 164 IS THE ONE DIAL ON THIS RECORD WHOSE OWN CONFIG RANGE DOES NOT FIT ITS WIDTH, so it is
    // carried in TENTHS of its unit and the reason is written here rather than left to arithmetic.
    // The FACTOR width is an i16 in thousandths and therefore saturates at 32.767, while
    // [Cards] ItemCueEmberRate accepts 0..60 embers per second. Sampled RAW, a player at 40
    // embers/s would be CLAMPED to 32.767 on every peer's screen — a silent divergence, which is
    // precisely the failure the 1:1 ruling and scripts/check-wire-coverage.py exist to stop, and one
    // that is invisible from inside the owner's own headset. Scaled by a tenth the whole config
    // range codes to ≤6000 at 0.01 embers/s of resolution (finer than a particle emitter can
    // express), and the id STAYS where the paging round reserved it: an id is a wire constant and
    // can never be renumbered, whereas the unit a field is carried in is just a convention the two
    // ends share — which the record already relies on for seconds in the factor range and for whole
    // percent in the count range.

    /// <summary>[Cards] ItemCueEmberRate — embers per second off the closed items pile, carried in
    /// TENTHS of an ember per second (see the note above and
    /// <see cref="TuneItemCueEmberRateScale"/>).</summary>
    public const byte TuneItemCueEmberRate = 164;

    /// <summary>The unit <see cref="TuneItemCueEmberRate"/> is carried in: TENTHS of an ember per
    /// second. Named once rather than typed twice, because a sender and a receiver that disagreed
    /// about it would disagree by a factor of ten with nothing failing. It lives in exactly two
    /// places — <c>BoardTuningSampler.Sample</c> and <c>RemoteBoardTuning</c> — so every consumer
    /// still reads a plain embers-per-second float and no renderer learns what it crossed in.</summary>
    public const float TuneItemCueEmberRateScale = 0.1f;

    /// <summary>[Cards] ItemCueEmberSize — ember size multiplier.</summary>
    public const byte TuneItemCueEmberSize = 165;
    /// <summary>[Cards] ItemBerthGlow — brightness of the additive warm field inside the recess.</summary>
    public const byte TuneItemBerthGlow = 166;
    /// <summary>[Cards] ItemBerthPingSeconds — period of the inward "put it here" ping, seconds.</summary>
    public const byte TuneItemBerthPingSeconds = 167;
    /// <summary>[Cards] ItemBerthPingReach — where outside the card rect that ping starts, as a
    /// multiple of the card.</summary>
    public const byte TuneItemBerthPingReach = 168;
    /// <summary>[Cards] ItemBerthRevealSeconds — grow-in / collapse-out time of the recess, seconds.</summary>
    public const byte TuneItemBerthRevealSeconds = 169;

    /// <summary>[ButtonColors] LabelOutlineWidth — the keycap label keyline's width as a fraction of
    /// the SDF spread (0..1). A dimensionless fraction, so it rides the FACTOR width with the rest of
    /// its kind; its COLOUR is id 49 and its on/off switch id 229.</summary>
    public const byte TuneLabelOutlineWidth = 170;

    /// <summary>
    /// [Cards] SlotOverlayScale_{board} — ONE size for the two blinking slot overlays AND for the
    /// card that comes to rest in them (user 2026-08-11: "exakt ausfüllen"). Dimensionless multiple
    /// of the authored card metric, hence the FACTOR width.
    ///
    /// <para>WHY IT NEEDS A FIELD OF ITS OWN even though the card's own size already travels. The
    /// receiver's card metric arrives on extension record 11 as the finished product
    /// <c>CardWidth × SlotScale × SlotCardScale</c> — a LENGTH, and one that the peer's mirrored
    /// cards consume. The GLOWS are not cards: <c>RemoteBoardFurniture</c> builds them from the
    /// board's own recess anchors times a factor, so it needs the FACTOR, and dividing record 11 by
    /// the owner's CardWidth to recover it would rest on a second dial travelling in step. The
    /// factor is also what preserves the gold/teal ratio on the far side.</para>
    ///
    /// <para>Predecessor: the retired GLOBAL SlotCardFill dial, which never had a tuning field
    /// because record 11 already carried its product. Retiring it for a per-board dial is what makes
    /// this field necessary — and per-board is right, because the recess it fills is board geometry.
    /// (Written without its brackets on purpose: check-wire-coverage.py resolves a field to a dial
    /// by the NEAREST bracketed key above the declaration, so a second one here would name the dead
    /// dial as this field's subject.)</para>
    ///
    /// <para>[Cards] SlotOverlayScale_{board}.</para>
    /// </summary>
    public const byte TuneSlotOverlayScale = 171;

    // ANGLE (2 B, hundredth-degrees).

    /// <summary>[Cards] AssetPitchDegrees_{board} — the board MESH's pitch inside the board root.</summary>
    public const byte TuneAssetPitch = 192;
    /// <summary>[Cards] AssetYawDegrees_{board}.</summary>
    public const byte TuneAssetYaw = 193;
    /// <summary>[Cards] AssetRollDegrees_{board}.</summary>
    public const byte TuneAssetRoll = 194;
    /// <summary>[Cards] FanArcSweepDegrees — the hand fan's total sweep.</summary>
    public const byte TuneFanArcSweep = 195;
    /// <summary>[Cards] FanPerCardStepDegrees — angular step between two fan cards.</summary>
    public const byte TuneFanPerCardStep = 196;
    /// <summary>[Cards] ItemFanOpenSpinDegrees — the roll an emerging item chip unwinds from.</summary>
    public const byte TuneItemFanOpenSpin = 197;

    /// <summary>[Cards] FanStepDegrees_Items — the angular step between two neighbouring cards of the
    /// ITEMS pile fan. One of the dials the 255-byte ceiling had locked out (see id 79).</summary>
    public const byte TuneFanStepDegreesItems = 198;
    /// <summary>[Cards] FanStepDegrees_Discard.</summary>
    public const byte TuneFanStepDegreesDiscard = 199;
    /// <summary>[Cards] FanStepDegrees_Burnt.</summary>
    public const byte TuneFanStepDegreesBurnt = 200;

    // COUNT (1 B).

    /// <summary>[Cards] FanMaxHandForCurve — hand size at which the fan's curvature saturates.</summary>
    public const byte TuneFanMaxHandForCurve = 224;
    /// <summary>[Cards] FanCurveMinCards — hands at or below this stay flat.</summary>
    public const byte TuneFanCurveMinCards = 225;

    // The last two dials of the HAND FAN's CHARACTER-SWAP EXCHANGE. They are here rather than in
    // the angle / factor ranges FOR A BYTE EACH, and that byte is not a micro-optimisation: with
    // them at three bytes the record's worst case would be 257, and the extension tail writes a
    // record's length as a SINGLE BYTE (PresenceState's board-tuning writer refuses payload > 255)
    // — so a player who had moved every dial would have had their WHOLE tuning record silently
    // dropped. Both quantities survive integer resolution with room to spare, which is what makes
    // the narrower container honest rather than a squeeze: see each field.

    /// <summary>[Cards] FanSwapSpinDegrees — the roll the hand fan's two swap halves counter-rotate
    /// through, in WHOLE degrees (0..180). One degree on a ~58° roll is far below what an eye
    /// resolves on a 6 cm card at arm's length; the angle range's hundredth-degrees would be
    /// spending two extra digits on nothing.</summary>
    public const byte TuneFanSwapSpin = 226;

    /// <summary>[Cards] FanSwapOverlap — how much of a slot's departure its arrival overlaps, in
    /// WHOLE PERCENT (0..100). The dial is a 0..1 fraction and the shipped value is 0.66; one
    /// percent of the per-card flight time is ~2 ms, i.e. under a frame at 90 Hz, so the
    /// quantisation is invisible by construction. Same convention note as the item-fan seconds
    /// above: an id range fixes the value WIDTH, never the unit.</summary>
    public const byte TuneFanSwapOverlapPercent = 227;

    // AN ENUM IN THE COUNT RANGE, DELIBERATELY. An id range fixes the value WIDTH and never the
    // unit — the same convention the seconds-valued dials in the factor range carry — and a
    // two-member shape needs exactly one byte. The receiver never indexes an enum with a wire
    // number: RemoteBoardTuning maps an unrecognised code back to the shipped member, which is the
    // standing rule for every code this protocol reads.
    //
    // IT USED TO SAY WHY IT WAS THE ONLY SHAPE ON THE WIRE: the per-board rest and generic shape
    // dials were "hardwired in the remote RENDERER (a peer's rest discs are always drawn round), so
    // a field for them would be bytes no receiver reads". That was the right call at the time and
    // it is why the debt was stated instead of paid. THE RENDERER GREW THE BRANCHES (2026-08-09,
    // under "Bitte implementier auch die Farben und Formen der Knöpfe"), so ids 231/232 below carry
    // them and the FanCloseDuration rule is satisfied on all three: every shape that rides this
    // record has a mirror that can actually DRAW both of its members.

    /// <summary>[RoundButtons] Shape — whether the docked turn-flow (SKIP) cap is a ROUND puck
    /// (code 0) or a SQUARE keycap (code 1): <c>Cards.ButtonShape</c> as its integer value.</summary>
    public const byte TuneRoundCapShape = 228;

    /// <summary>[ButtonColors] LabelOutline — whether the dark keyline is drawn around keycap
    /// letters at all (0 = off, 1 = on). A BOOL in the one-byte count width, which is the same
    /// container the shape enums use: the range fixes the WIDTH, never the type.</summary>
    public const byte TuneLabelOutlineOn = 229;

    /// <summary>[ButtonColors] LabelUnderlay — whether the soft dark drop-shadow is drawn under
    /// keycap letters (0 = off, 1 = on).</summary>
    public const byte TuneLabelUnderlayOn = 230;

    /// <summary>[Cards] RestButtonShape_{board} — ROUND disc (0) or SQUARE keycap (1) for the
    /// short/long rest pair, for the sender's OWN board style (the style rides the extras block, so
    /// only the current one is transmitted, exactly like every other per-board dial here).</summary>
    public const byte TuneRestCapShape = 231;

    /// <summary>[Cards] GenericButtonShape_{board} — ROUND disc (0) or SQUARE keycap (1) for the
    /// Confirm / Undo / item-USE column.</summary>
    public const byte TuneGenericCapShape = 232;

    /// <summary>Payload width of a board-tuning field with this id — 6 / 3 / 2 / 1, or 0 for a
    /// RESERVED id whose width this build does not know (the reader then abandons the rest of the
    /// record rather than mis-parsing it; see the record doc).</summary>
    public static int BoardTuneFieldWidth(byte id)
    {
        if (id >= TuneVecIdMin && id <= TuneVecIdMax) return 6;
        if (id >= TuneColorIdMin && id <= TuneColorIdMax) return 3;
        if (id >= TuneLengthIdMin && id <= TuneAngleIdMax) return 2;
        if (id >= TuneCountIdMin && id <= TuneCountIdMax) return 1;
        return 0;
    }

    /// <summary>Quantize a metre length to its i16 wire code — TENTH MILLIMETRES, clamped to
    /// ±3.2767 m. 0.1 mm is an order of magnitude below what an eye resolves on a 0.4 m board, and
    /// tenths keep the value readable in a hardware log (−1100 = −110.0 mm).</summary>
    public static short EncodeTuneLength(float meters)
    {
        if (float.IsNaN(meters) || float.IsInfinity(meters))
            return 0;
        return (short)UnityEngine.Mathf.Clamp(
            UnityEngine.Mathf.RoundToInt(meters * 10000f), short.MinValue, short.MaxValue);
    }

    /// <summary>Decode a tenth-millimetre length code back to metres.</summary>
    public static float DecodeTuneLength(short code) => code / 10000f;

    /// <summary>Quantize a dimensionless factor to its i16 wire code — THOUSANDTHS, clamped to
    /// ±32.767 (8× headroom over the widest tunable range, which is 0.2..4).</summary>
    public static short EncodeTuneFactor(float factor)
    {
        if (float.IsNaN(factor) || float.IsInfinity(factor))
            return 0;
        return (short)UnityEngine.Mathf.Clamp(
            UnityEngine.Mathf.RoundToInt(factor * 1000f), short.MinValue, short.MaxValue);
    }

    /// <summary>Decode a thousandths factor code.</summary>
    public static float DecodeTuneFactor(short code) => code / 1000f;

    /// <summary>Quantize an angle in degrees to its i16 wire code — HUNDREDTH DEGREES, clamped to
    /// ±327.67° (the widest tunable range is −85..180).</summary>
    public static short EncodeTuneAngle(float degrees)
    {
        if (float.IsNaN(degrees) || float.IsInfinity(degrees))
            return 0;
        return (short)UnityEngine.Mathf.Clamp(
            UnityEngine.Mathf.RoundToInt(degrees * 100f), short.MinValue, short.MaxValue);
    }

    /// <summary>Decode a hundredth-degree angle code.</summary>
    public static float DecodeTuneAngle(short code) => code / 100f;

    /// <summary>Quantize one 0..1 colour channel to its uint8 wire code — the display's own
    /// container, so the round trip is visually lossless (see <see cref="TuneColorIdMin"/>). Out-of-
    /// range and NaN saturate rather than wrap: a nonsense config value may only ever produce a
    /// duller or brighter cap on a peer's board, never a different hue.</summary>
    public static byte EncodeTuneColorChannel(float channel)
    {
        if (float.IsNaN(channel))
            return 0;
        return (byte)UnityEngine.Mathf.Clamp(
            UnityEngine.Mathf.RoundToInt(channel * 255f), 0, 255);
    }

    /// <summary>Decode a uint8 colour channel code back to 0..1.</summary>
    public static float DecodeTuneColorChannel(byte code) => code / 255f;

    /// <summary>
    /// Find field <paramref name="id"/> inside a board-tuning payload and return the offset of its
    /// VALUE, or −1 when the field is absent. Walks the sparse list by the width its own id range
    /// declares, so an unknown-width RESERVED id ends the walk (the payload's remaining bytes are
    /// unparseable, and guessing is how a wire reader corrupts a screen).
    ///
    /// <para>Absent is the NORMAL answer: the record only ever carries the dials the sender moved,
    /// so every caller pairs this with the shipped default it already compiles in. Never throws —
    /// every read is bounded by <paramref name="len"/>, which the record's own TLV length fixed.</para>
    /// </summary>
    public static int FindBoardTuneField(byte[]? payload, int offset, int len, byte id)
    {
        // NOTE THE BOUND: an ASSEMBLED payload, not a wire page (see BoardTuneAssembledMinBytes).
        // Callers hand this the receiver's assembled `[n][fields]` buffer, never a record-28 page.
        if (payload == null || len < BoardTuneAssembledMinBytes
            || offset < 0 || offset + len > payload.Length)
            return -1;
        int n = payload[offset];
        if (n > BoardTuneMaxFields)
            n = BoardTuneMaxFields;
        int i = offset + 1;
        int end = offset + len;
        for (int f = 0; f < n; f++)
        {
            if (i >= end)
                break;
            byte fieldId = payload[i];
            int width = BoardTuneFieldWidth(fieldId);
            if (width == 0 || i + 1 + width > end)
                break;                       // reserved id / truncated tail — stop, never guess
            if (fieldId == id)
                return i + 1;
            i += 1 + width;
        }
        return -1;
    }

    /// <summary>Read a Vector3 field out of a board-tuning payload, or
    /// <paramref name="fallback"/> (the receiver's own shipped default) when it is absent.</summary>
    public static UnityEngine.Vector3 BoardTuneVector(
        byte[]? payload, int offset, int len, byte id, UnityEngine.Vector3 fallback)
    {
        int at = FindBoardTuneField(payload, offset, len, id);
        if (at < 0)
            return fallback;
        return new UnityEngine.Vector3(
            DecodeTuneLength((short)(payload![at] | (payload[at + 1] << 8))),
            DecodeTuneLength((short)(payload[at + 2] | (payload[at + 3] << 8))),
            DecodeTuneLength((short)(payload[at + 4] | (payload[at + 5] << 8))));
    }

    /// <summary>Read an RGB COLOUR field, or <paramref name="fallback"/> (the receiver's own shipped
    /// colour) when it is absent. ALPHA COMES FROM THE FALLBACK, never from the wire — the range
    /// carries no alpha byte (see <see cref="TuneColorIdMin"/>), and taking the caller's own opaque
    /// 1f is what keeps an absent field meaning "the value you already have" in all four
    /// channels.</summary>
    public static UnityEngine.Color BoardTuneColor(
        byte[]? payload, int offset, int len, byte id, UnityEngine.Color fallback)
    {
        int at = FindBoardTuneField(payload, offset, len, id);
        if (at < 0)
            return fallback;
        return new UnityEngine.Color(
            DecodeTuneColorChannel(payload![at]),
            DecodeTuneColorChannel(payload[at + 1]),
            DecodeTuneColorChannel(payload[at + 2]),
            fallback.a);
    }

    /// <summary>Read a metre LENGTH field, or <paramref name="fallback"/> when absent.</summary>
    public static float BoardTuneLength(byte[]? payload, int offset, int len, byte id, float fallback)
    {
        int at = FindBoardTuneField(payload, offset, len, id);
        return at < 0 ? fallback : DecodeTuneLength((short)(payload![at] | (payload[at + 1] << 8)));
    }

    /// <summary>Read a dimensionless FACTOR field, or <paramref name="fallback"/> when absent.</summary>
    public static float BoardTuneFactor(byte[]? payload, int offset, int len, byte id, float fallback)
    {
        int at = FindBoardTuneField(payload, offset, len, id);
        return at < 0 ? fallback : DecodeTuneFactor((short)(payload![at] | (payload[at + 1] << 8)));
    }

    /// <summary>Read an ANGLE field (degrees), or <paramref name="fallback"/> when absent.</summary>
    public static float BoardTuneAngle(byte[]? payload, int offset, int len, byte id, float fallback)
    {
        int at = FindBoardTuneField(payload, offset, len, id);
        return at < 0 ? fallback : DecodeTuneAngle((short)(payload![at] | (payload[at + 1] << 8)));
    }

    /// <summary>Read an integer COUNT field, or <paramref name="fallback"/> when absent.</summary>
    public static int BoardTuneCount(byte[]? payload, int offset, int len, byte id, int fallback)
    {
        int at = FindBoardTuneField(payload, offset, len, id);
        return at < 0 ? fallback : payload![at];
    }

    /// <summary>
    /// Append one board-tuning field to <paramref name="payload"/> at <paramref name="i"/> IF the
    /// live value differs from <paramref name="shipped"/>, comparing the QUANTIZED codes rather
    /// than the floats: two values that land on the same wire code are the same picture, and a
    /// float compare would emit a field for a config round-trip that changed nothing visible.
    /// Returns true when a field was written (the caller counts them).
    /// </summary>
    public static bool WriteTuneLengthField(
        byte[] payload, ref int i, byte id, float live, float shipped)
    {
        short code = EncodeTuneLength(live);
        if (code == EncodeTuneLength(shipped) || i + 3 > payload.Length)
            return false;
        payload[i++] = id;
        payload[i++] = (byte)(code & 0xFF);
        payload[i++] = (byte)((code >> 8) & 0xFF);
        return true;
    }

    /// <summary>Vector3 counterpart of <see cref="WriteTuneLengthField"/> — one field, written only
    /// when any of the three quantized components differs from the shipped default.</summary>
    public static bool WriteTuneVectorField(
        byte[] payload, ref int i, byte id, UnityEngine.Vector3 live, UnityEngine.Vector3 shipped)
    {
        short x = EncodeTuneLength(live.x), y = EncodeTuneLength(live.y), z = EncodeTuneLength(live.z);
        if ((x == EncodeTuneLength(shipped.x) && y == EncodeTuneLength(shipped.y)
             && z == EncodeTuneLength(shipped.z))
            || i + 7 > payload.Length)
            return false;
        payload[i++] = id;
        payload[i++] = (byte)(x & 0xFF); payload[i++] = (byte)((x >> 8) & 0xFF);
        payload[i++] = (byte)(y & 0xFF); payload[i++] = (byte)((y >> 8) & 0xFF);
        payload[i++] = (byte)(z & 0xFF); payload[i++] = (byte)((z >> 8) & 0xFF);
        return true;
    }

    /// <summary>
    /// COLOUR counterpart of <see cref="WriteTuneLengthField"/> — one 3-byte field, written only
    /// when any of the three QUANTIZED CHANNELS differs from the shipped colour.
    ///
    /// <para>The quantized-code comparison is not a detail here, it is the whole reason an untuned
    /// player still emits no record: the [ButtonColors] defaults are decimal floats (0.984, 0.953,
    /// 0.878) that a config file round-trips through text, and a float compare would start emitting
    /// three colour fields forever for a player who never opened the debug menu. On the uint8 code
    /// 0.984 and 0.98400001 are the same byte, because they are the same pixel.</para>
    ///
    /// <para>ALPHA IS NOT COMPARED AND NOT WRITTEN. The range carries none (see
    /// <see cref="TuneColorIdMin"/>); every colour it transports is opaque on both sides.</para>
    /// </summary>
    public static bool WriteTuneColorField(
        byte[] payload, ref int i, byte id, UnityEngine.Color live, UnityEngine.Color shipped)
    {
        byte r = EncodeTuneColorChannel(live.r);
        byte g = EncodeTuneColorChannel(live.g);
        byte b = EncodeTuneColorChannel(live.b);
        if ((r == EncodeTuneColorChannel(shipped.r) && g == EncodeTuneColorChannel(shipped.g)
             && b == EncodeTuneColorChannel(shipped.b))
            || i + 4 > payload.Length)
            return false;
        payload[i++] = id;
        payload[i++] = r;
        payload[i++] = g;
        payload[i++] = b;
        return true;
    }

    /// <summary>FACTOR counterpart of <see cref="WriteTuneLengthField"/>.</summary>
    public static bool WriteTuneFactorField(
        byte[] payload, ref int i, byte id, float live, float shipped)
    {
        short code = EncodeTuneFactor(live);
        if (code == EncodeTuneFactor(shipped) || i + 3 > payload.Length)
            return false;
        payload[i++] = id;
        payload[i++] = (byte)(code & 0xFF);
        payload[i++] = (byte)((code >> 8) & 0xFF);
        return true;
    }

    /// <summary>ANGLE counterpart of <see cref="WriteTuneLengthField"/>.</summary>
    public static bool WriteTuneAngleField(
        byte[] payload, ref int i, byte id, float live, float shipped)
    {
        short code = EncodeTuneAngle(live);
        if (code == EncodeTuneAngle(shipped) || i + 3 > payload.Length)
            return false;
        payload[i++] = id;
        payload[i++] = (byte)(code & 0xFF);
        payload[i++] = (byte)((code >> 8) & 0xFF);
        return true;
    }

    /// <summary>COUNT counterpart of <see cref="WriteTuneLengthField"/> (1 byte, clamped 0..255).</summary>
    public static bool WriteTuneCountField(byte[] payload, ref int i, byte id, int live, int shipped)
    {
        byte code = (byte)UnityEngine.Mathf.Clamp(live, 0, 255);
        if (code == (byte)UnityEngine.Mathf.Clamp(shipped, 0, 255) || i + 2 > payload.Length)
            return false;
        payload[i++] = id;
        payload[i++] = code;
        return true;
    }

    /// <summary>
    /// Extension record id: the sender's docked USE-SLOT BARS — the SECOND drawer below their
    /// decision row (<c>WorldUI.Surfaces.UseBarsSurface</c>): which of the four bars are up, how
    /// many slots each shows, whether that bar has an element / option SUB-PICKER open, and per
    /// slot whether it is offered, dimmed or chosen.
    ///
    /// <para>WHY (user ruling 2026-08-08, the standing 1:1 rule — "alle Interaktionen, Animationen
    /// und Anzeigen des Controllboards in MP auch synchronisieren … so wie der Spieler sie sieht").
    /// The four bars (<c>UIActiveBonusBar</c> / <c>UIUseAbilitiesBar</c> /
    /// <c>UIUseAugmentationsBar</c> / <c>UIUseItemsBar</c>) are HUD singletons that exist only on
    /// the client the game raised them for — every other client's bar is empty — so a peer saw
    /// NOTHING below the decision row while the owner was looking at a whole drawer of slots. By
    /// area this was the largest missing surface of the control board.</para>
    ///
    /// <para>LAYOUT — <c>[barMask]</c> then, for EVERY SET bar bit in BIT ORDER,
    /// <c>[barFlags][n][n × slot byte]</c>. At least <see cref="UseBarsMinRecordBytes"/> bytes:
    /// <list type="bullet">
    /// <item><c>barMask</c> — <see cref="UseBarActiveBonusBit"/> / <see cref="UseBarAbilitiesBit"/> /
    ///   <see cref="UseBarAugmentsBit"/> / <see cref="UseBarItemsBit"/>, in the owner's own stack
    ///   order (top to bottom). Bits 4..7 reserved, masked to <see cref="UseBarsDefinedMask"/> on
    ///   write AND on read. A bar that is DOCKED but render-hidden for another character's focus is
    ///   NOT in the mask — see the hide note below.</item>
    /// <item><c>barFlags</c> — <see cref="UseBarElementPickerBit"/> (an <c>UIElementPicker</c> popup
    ///   stands open in this bar) and <see cref="UseBarOptionPickerBit"/> (an <c>UIOptionPicker</c>
    ///   does). Masked to <see cref="UseBarFlagsDefinedMask"/> both ways.</item>
    /// <item><c>n</c> — visible slots in that bar, clamped to <see cref="UseBarsMaxSlots"/> on both
    ///   ends AND, on read, against the record's own remaining length.</item>
    /// <item>slot byte — <see cref="UseSlotOfferedBit"/> (the owner can click it),
    ///   <see cref="UseSlotDimmedBit"/> (the slot's <c>CanvasGroup</c> is below full alpha — the
    ///   game's own <c>UIUseSlot.disabledAlpha</c> 0.25 "not yours / not now" look) and
    ///   <see cref="UseSlotChosenBit"/> (<c>UIUseSlot.IsSelected()</c> — the toggle is ON). Same
    ///   three bit POSITIONS and the same meanings as record 24's option byte, so a receiver paints
    ///   a bar tile and a decision plate through one code path. Masked to
    ///   <see cref="UseSlotDefinedMask"/> both ways.</item>
    /// </list></para>
    ///
    /// <para>THERE IS NO SLOT LABEL ON THIS WIRE, AND NOT BECAUSE IT WAS SUPPRESSED — the game's
    /// use-slot widgets HAVE no label. <c>UIUseSlot&lt;T&gt;</c> carries only a button, a selected
    /// mask and two highlight objects; every concrete slot decorates itself with a SPRITE
    /// (<c>UIUseItemScenario.imageItem</c> ← <c>UIInfoTools.GetItemConfig(item.YMLData.Art)
    /// .miniIcon</c>, <c>UIUseActiveBonus.icon</c> ← <c>bonus.GetIcon()</c>,
    /// <c>UIUseAbility.icon</c> ← <c>ability.Icon</c>, <c>UIUseAugmentation</c>'s element
    /// glyphs). The only words on a slot live in its hover TOOLTIP (<c>UIItemTooltip</c> /
    /// <c>UIAbilityTooltip</c> / <c>UIAugmentTooltip</c>), which is not part of the docked strip.
    /// So the choice was never "string or variant id": an item slot's identity is its CARD ART, and
    /// putting it on the wire in any form — sprite name, item id, art key — would be card identity,
    /// which never rides this wire. The receiver captions each bar from the BAR BIT (a number it
    /// localizes itself, the record-24 text-variant solution) and draws the slots as anonymous,
    /// state-painted tiles: the same "structure and state travel, identity is resolved locally or
    /// not at all" rule <see cref="RemoteItemCardSource"/> already ships.</para>
    ///
    /// <para>THE OWNER'S FOCUS HIDE TRAVELS WITH IT. <c>UseBarsSurface</c> render-hides a bar whose
    /// owner is not the character the player is looking at (<c>BarDock.ApplyFocusHide</c>, driven by
    /// the same rule <c>DecisionDockSurface.PromptFocus</c> rolls up). A hidden bar is dropped from
    /// the mask, so the peer's copy of that bar empties in the same frames the owner's does — the
    /// rule records 12 and 24 already follow. All four hidden ⇒ mask 0 ⇒ NO RECORD at all, so an
    /// idle packet stays byte-identical to the previous build's and absence renders as the
    /// pre-record look (nothing below the decision row).</para>
    ///
    /// <para>ADDITIVE TLV, appended in id order behind record 24; an older reader steps over it by
    /// its own length. Worst case 2 + 1 + 4 × (1 + 1 + 8) = 43 bytes.</para>
    /// </summary>
    public const byte ExtIdUseBars = 25;

    /// <summary>Smallest payload <see cref="ExtIdUseBars"/> can have: the bar mask alone. A shorter
    /// record is not trusted (never trust the wire).</summary>
    public const int UseBarsMinRecordBytes = 1;

    /// <summary>How many use bars exist (the four the surface docks). Fixed by the game, not by the
    /// wire — the mask has one bit per bar and the reserved bits are masked away.</summary>
    public const int UseBarsCount = 4;

    /// <summary>Per-bar slot cap of <see cref="ExtIdUseBars"/>. A raised bar shows a handful of
    /// slots (the items bar only docks its SUB-CHOICE slots at all — the place-to-use split), and
    /// the cap bounds the record at 1 + 4 × 10 = 41 payload bytes. Clamped on write AND on read,
    /// where it is additionally clamped against the record's own remaining length.</summary>
    public const int UseBarsMaxSlots = 8;

    /// <summary>Use-bar mask bit 0: the ACTIVE BONUS bar (<c>UIActiveBonusBar</c>) — top of the
    /// owner's stack.</summary>
    public const byte UseBarActiveBonusBit = 1 << 0;

    /// <summary>Use-bar mask bit 1: the ABILITIES bar (<c>UIUseAbilitiesBar</c>) — the element
    /// infusion / choose-ability pickers, including the end-of-ability "Any" infusion that blocks
    /// the owner's turn until it is answered.</summary>
    public const byte UseBarAbilitiesBit = 1 << 1;

    /// <summary>Use-bar mask bit 2: the AUGMENTATIONS bar (<c>UIUseAugmentationsBar</c>).</summary>
    public const byte UseBarAugmentsBit = 1 << 2;

    /// <summary>Use-bar mask bit 3: the usable ITEMS bar (<c>UIUseItemsBar</c>) — bottom of the
    /// stack.</summary>
    public const byte UseBarItemsBit = 1 << 3;

    /// <summary>Every bar bit defined today. Writer and reader both mask with it, so a future
    /// sender's extra bits can never light a fifth bar here (the board-UI overlay discipline).</summary>
    public const byte UseBarsDefinedMask =
        UseBarActiveBonusBit | UseBarAbilitiesBit | UseBarAugmentsBit | UseBarItemsBit;

    /// <summary>Bar-flags bit 0: an ELEMENT sub-picker (<c>UIElementPicker.IsOpen</c>) stands open
    /// in this bar — the popup the owner is mid-choice in, which is why their bar host grew.</summary>
    public const byte UseBarElementPickerBit = 1 << 0;

    /// <summary>Bar-flags bit 1: an OPTION sub-picker (<c>UIOptionPicker.IsOpen</c>) stands open in
    /// this bar (initiative ±, forgo, choose-ability).</summary>
    public const byte UseBarOptionPickerBit = 1 << 1;

    /// <summary>Every bar-flags bit defined today; masked on write AND on read.</summary>
    public const byte UseBarFlagsDefinedMask = UseBarElementPickerBit | UseBarOptionPickerBit;

    /// <summary>Slot byte bit 0: the owner can actually CLICK this slot right now. Clear = greyed.
    /// Deliberately the SAME bit position as <see cref="DecisionOptionOfferedBit"/>.</summary>
    public const byte UseSlotOfferedBit = 1 << 0;

    /// <summary>Slot byte bit 1: the slot is DIMMED — a <c>CanvasGroup</c> between it and the bar
    /// root holds it below full alpha, which for these widgets is the game's own
    /// <c>UIUseSlot.SetInteractable</c> writing <c>disabledAlpha</c> (0.25). Same bit position as
    /// <see cref="DecisionOptionDimmedBit"/>.</summary>
    public const byte UseSlotDimmedBit = 1 << 1;

    /// <summary>Slot byte bit 2: the slot is CHOSEN — <c>UIUseSlot.IsSelected()</c>, the toggle the
    /// owner has switched on. Same bit position as <see cref="DecisionOptionChosenBit"/>.</summary>
    public const byte UseSlotChosenBit = 1 << 2;

    /// <summary>Every slot bit defined today; masked on write AND on read. Bit 3 was considered for
    /// the MANDATORY highlight and deliberately left reserved: <c>UIUseSlot.mandatoryHiglight</c> is
    /// private serialized state, and the fact it telegraphs ("a mandatory bonus still owes a pick")
    /// already reaches every peer through the pick-banner record 7, which
    /// <c>UseBarsSurface.UpdateWaitingHint</c> publishes on exactly that condition.</summary>
    public const byte UseSlotDefinedMask = UseSlotOfferedBit | UseSlotDimmedBit | UseSlotChosenBit;

    /// <summary>
    /// Extension record id: WHICH FAN POSITION IS CLIPPED INTO THE SENDER'S ITEM-USE RECESS —
    /// ONE byte, an index into the very same ordered item fan the <see cref="FlagItemFan"/> count
    /// already describes. Written ONLY while a card really lies in that recess.
    ///
    /// <para>THE DEFECT (found while the local recess was reworked, 2026-08-09; the standing
    /// multiplayer ruling of 2026-08-08 is "generell gilt die Regel, das man alle Interaktionen,
    /// Animationen und Anzeigen des Controllboards in MP auch synchronisieren soll" — with the
    /// secret quest and the selection-phase card FACES as the only exceptions). The owner places an
    /// item card in their board's use recess; it re-parents onto the recess and settles into it. On
    /// every OTHER machine that same card was still drawn out in the fan arc and the mirrored recess
    /// stood empty, because the wire said only HOW MANY item cards are up
    /// (<see cref="FlagItemFan"/> + its count byte) and <see cref="BoardUiItemRecessBit"/> said only
    /// that the recess is VISIBLE. A card lying in a recess on the owner's board is an ANZEIGE of
    /// that control board in the ruling's plain sense, so it has to travel.</para>
    ///
    /// <para>WHY AN INDEX AND NOT THE ITEM. The standing rule for this wire is that no card identity
    /// ever rides it, and none has to: the receiver already draws the peer's item fan from the
    /// host-replicated <c>CPlayerActor.CharacterClass</c> → <c>Inventory.AllItems</c>
    /// (<see cref="RemoteItemFan"/> via <see cref="RemotePileFronts"/>, gated by
    /// <see cref="RevealGate"/>). Naming a POSITION in the arc it is already rendering therefore
    /// tells it exactly which of the chips it already holds to move, and discloses strictly less
    /// than the arc itself — the identical argument that made <see cref="ExtIdCardHighlight"/> an
    /// index, and the same one that lets <see cref="BoardUiSnapMask"/> name a recess.</para>
    ///
    /// <para>WHY THE SETTLE IS NOT STREAMED. The placement is a 0.28 s ANIMATION (the chip
    /// re-parents keeping its world pose and eases into the recess frame — <c>ItemsPile.ItemChip</c>
    /// ClipIntoSlot/TickClipSettle), and the ruling names animations outright, so a peer must see it
    /// ARRIVE rather than teleport. It is replayed from the EDGE — the receiver knows the frame this
    /// index appears — exactly as the item fan's whole open/close deal-out already is: the same one
    /// re-parent, the same ease, the same duration, on the receiver's own clock. Streaming a pose
    /// would cost ~20 B × 15 Hz for the duration of a movement both machines can derive from a
    /// single byte.</para>
    ///
    /// <para>WHY A RECORD AND NOT A BIT. There is no bit: BOTH flag bytes are full, and the
    /// pile-browse block's byte A is full too (bit 4 mask size, bits 5..6 board style, bit 7 the
    /// extension tail itself). The two INERT extras bits are no help either —
    /// <see cref="FlagItemFanHeld"/> and <see cref="FlagItemFanLeft"/> describe a hand-held item FAN
    /// and are dead since the whole-fan grab was removed (<c>ItemsPile.IsHandHeld</c> is hard-false),
    /// but they are one bit each and this is a 0..N index, not a boolean. The TLV tail is where a
    /// field of this shape belongs and it costs no bit at all.</para>
    ///
    /// <para>ABSENCE MEANS "NOTHING IS CLIPPED", which is exactly what every build before this one
    /// rendered, so a packet from an owner with an empty recess — i.e. nearly every packet — stays
    /// byte-identical to the previous build's. No sentinel value is defined and none is needed: the
    /// record is simply omitted. The index is NOT range-checked here; like
    /// <see cref="ExtIdCardHighlight"/> it is clamped by the RENDERER against its own live slab
    /// count, which is the only place the bound is really known (a packet may legitimately arrive a
    /// frame either side of a fan resize).</para>
    ///
    /// <para>ADDITIVE TLV, appended in id order between records 25 and 27; an older reader steps
    /// over it by its own length and keeps drawing the chip in the arc, as it always did.</para>
    /// </summary>
    public const byte ExtIdItemUseClip = 26;

    /// <summary>Payload length of <see cref="ExtIdItemUseClip"/>: the one index byte. A shorter
    /// record is not trusted (never trust the wire).</summary>
    public const int ItemUseClipRecordBytes = 1;

    /// <summary>
    /// Extension record id: the SENDER'S OWN ON-SCREEN ORDER OF THE INITIATIVE TRACK'S PLAYER
    /// ENTRIES, plus which of them they control — <c>[count][ownedMask][count × int32 actorId
    /// LE]</c>, at most <see cref="TrackOrderMaxIds"/> ids. Read off the LIVE widget (ascending
    /// sibling index under the track holder, which is where vanilla's <c>UpdateSortingOrder</c>
    /// writes the display order), never re-derived.
    ///
    /// <para>THE DEFECT. <c>Net/RemoteInitiativeTrack</c> mirrors the track by CLONING THIS
    /// CLIENT'S OWN widget, and its class doc listed "the ENTRY SET and their on-screen ORDER" as
    /// GLOBAL — bit-identical on every client, zero wire. The entry SET is. The ORDER is NOT, and
    /// vanilla says so in one branch: <c>InitiativeTrackActorBehaviour.CompareTo</c>
    /// (decompiled GH.Runtime/InitiativeTrackActorBehaviour.cs:160-171) short-circuits the whole
    /// initiative comparison while
    /// <c>FFSNetwork.IsOnline &amp;&amp; PhaseManager.CurrentPhase.Type ==
    /// SelectAbilityCardsOrLongRest</c> and both sides are player actors and at least one is NOT
    /// <c>IsUnderMyControl</c> — the foreign one sorts FIRST, unconditionally. So during the card
    /// selection phase every client's track reads <c>[the players I do not control][the players I
    /// do]</c>, i.e. MY index 3 is genuinely YOUR index 5, and a clone of my widget put MY
    /// arrangement on every peer's board. The mod already knew this and had written it down twice
    /// — see <see cref="ExtIdTrackHover"/>'s "WHY A STABLE ACTOR ID AND NOT A TRACK INDEX" and
    /// <c>Net/InitiativeHoverSampler</c>'s copy of the same paragraph — which is exactly why the
    /// hover and selection records name entries by id: an INDEX would already have lifted the
    /// wrong portrait. The ordering itself was never carried.</para>
    ///
    /// <para>WHY THE WIDGET AND NOT A RE-DERIVATION, which is the whole reason this costs bytes.
    /// The receiver has every INPUT: the actor list is replicated, <c>CPlayerActor.Initiative()</c>
    /// is a pure model read (CPlayerActor.cs:156 — <c>RoundAbilityCards</c> /
    /// <c>InitiativeAbilityCard</c> / <c>LongRest</c>, no ownership gate), and FFSNet's
    /// <c>NetworkPlayer.MyControllables</c> would even name the sender's characters. What it does
    /// NOT have is the SORT: <c>CompareTo</c> is an INCONSISTENT comparator (two foreign players
    /// compare 0 while each compares −1 against one of mine), so the result of
    /// <c>List&lt;T&gt;.Sort()</c> depends on introsort's pivot choices and on the pre-sort input
    /// order, neither of which is contract. Re-deriving it would be the ModBuild-84 mistake again
    /// (see <see cref="ExtIdTrackSelection"/>'s "WHY RECORD 22 CANNOT ANSWER THIS"): a plausible
    /// derivation that is wrong in states the user watches every single round. The ids ARE the
    /// pixel.</para>
    ///
    /// <para>THE OWNED MASK, and why it is one byte rather than a second record. Bit k names
    /// ids[k] as a character the SENDER controls (<c>CActor.IsUnderMyControl</c> — a purely local
    /// flag, CActor.cs:751, set from the save state and never replicated). It pays for the OTHER
    /// per-viewer thing this widget shows: <c>WorldUI/Surfaces/TablePanelSurfaces</c>'
    /// <c>InitiativeSelectionGlow</c>, the amber "still has to choose" ring, which
    /// <c>Board/SelectionReadyHighlighter</c> builds ONLY for actors the LOCAL player controls and
    /// which therefore rode the clone onto every peer's board wearing the OBSERVER's set. Whether
    /// a named character has COMMITTED is not sent and does not need to be: the game replicates
    /// <c>CCharacterClass.RoundAbilityCards</c> live through the selection phase
    /// (<c>ProxySetStartRoundDeckState</c> — the very list <c>Net/RemoteControlBoard.SeatSlots</c>
    /// already draws a peer's played card backs from), so the receiver derives "pending" itself.
    /// One byte buys the half that cannot be derived and nothing more.</para>
    ///
    /// <para>NO CARD IDENTITY, AND NO WIDENING OF THE NUMBER EXCEPTION. The payload is a
    /// permutation of PUBLIC track entries in the same <c>NetFigures.StableActorId</c> space
    /// records 16 and 23 already use, plus one ownership bit per entry — the same fact vanilla
    /// broadcasts anyway (the multiplayer ready tracker shows a per-character ready marker for the
    /// whole selection phase, and the hand tabs print every player's live "selected/2" count with
    /// no <c>IsUnderMyControl</c> gate). The initiative NUMBER stays behind vanilla's own online
    /// gate — a foreign player reads "?" during card selection — and this record does not touch
    /// it. Note the ordering itself already told every observer "these are the entries that player
    /// does not control"; carrying the owner's ordering discloses no more than the observer's did.
    /// </para>
    ///
    /// <para>Written ONLY inside vanilla's own divergence window (online AND
    /// <c>SelectAbilityCardsOrLongRest</c> AND at least one player entry), which is precisely the
    /// condition at CompareTo:160 — so outside the card-selection phase an idle packet is
    /// byte-identical to a pre-record sender's, and its ABSENCE is what releases the receiver's
    /// override back to the mirrored arrangement. ADDITIVE TLV, appended in id order behind record
    /// 24; an older reader steps over it by its length and keeps showing what it always did.</para>
    /// </summary>
    public const byte ExtIdTrackOrder = 27;

    /// <summary>Id cap of <see cref="ExtIdTrackOrder"/>. A Gloomhaven party is four mercenaries;
    /// the track can carry a few more player rows at once because exhausted heroes are appended
    /// (<c>InitiativeTrack.UpdateInitiativeTrack</c> adds <c>ExhaustedPlayers</c>), so six is
    /// headroom over every real table. It bounds the record at 2 + 6×4 = 26 payload bytes, and it
    /// also bounds <see cref="TrackOrderOwnedMask"/>'s meaning: bit k for k &lt; 6, so the mask can
    /// never be asked about an id that does not exist. Clamped on BOTH ends — the reader re-clamps
    /// against the record length as well, never trusting the wire.</summary>
    public const int TrackOrderMaxIds = 6;

    /// <summary>Minimum payload of <see cref="ExtIdTrackOrder"/>: the count byte and the owned
    /// mask. A reader requires at least this much before it looks at the record.</summary>
    public const int TrackOrderMinRecordBytes = 2;

    /// <summary>Every bit <see cref="ExtIdTrackOrder"/>'s owned mask can define, given
    /// <see cref="TrackOrderMaxIds"/>. Masked on write AND on read, so a longer future cap can
    /// never make an old receiver read ownership into an id it never got.</summary>
    public const byte TrackOrderOwnedDefinedMask = (1 << TrackOrderMaxIds) - 1;

    /// <summary>
    /// Extension record id: the LIVE LABELS of the sender's board caps — what their CONFIRM
    /// keycap, their docked SKIP button, their UNDO keycap and their item-USE cap ACTUALLY read
    /// right now — as <c>[byte mask][per set bit, in mask-bit order: byte len + UTF8]</c>, each
    /// label capped at <see cref="CapLabelMaxBytes"/> bytes. Mask bits:
    /// <see cref="CapLabelConfirmBit"/>, <see cref="CapLabelSkipBit"/>,
    /// <see cref="CapLabelUndoBit"/>, <see cref="CapLabelItemUseBit"/>.
    ///
    /// <para>WHY (user report 2026-08-04: "Mein Mitspieler las 'Fortfahren', ich sehe
    /// 'Bestätigen'"): the remote furniture used to label the mirrored caps from the RECEIVER's
    /// localization at the neutral GUI_CONFIRM / GUI_SKIP_MOVEMENT wording, while the owner's own
    /// caps re-read the game's live button text every tick (16 <c>ReadyButton.EButtonState</c>
    /// wordings, GUI_SKIP_ABILITY/GUI_SKIP_ATTACK swaps, pick-flow overrides, "✓ READY"). Those
    /// states are peer-local UI, so the only 1:1 rendering is the sender's own displayed string,
    /// verbatim, in THEIR language — the same argument as the pick banner.</para>
    ///
    /// <para>Public UI text only — button labels never name a card, and nothing here consults
    /// one. Written ONLY while at least one of the two caps is visible with a known label, so an
    /// idle packet stays byte-identical; absence keeps the receiver's neutral-label fallback,
    /// which is exactly what peers predating the record render. ADDITIVE TLV.</para>
    /// </summary>
    public const byte ExtIdCapLabels = 13;

    /// <summary>Cap-labels record, mask bit 0: a CONFIRM label block follows.</summary>
    public const byte CapLabelConfirmBit = 1 << 0;

    /// <summary>Cap-labels record, mask bit 1: a SKIP label block follows (after the confirm
    /// block when both are present — mask-bit order, the same rule the extras flag blocks use).</summary>
    public const byte CapLabelSkipBit = 1 << 1;

    /// <summary>
    /// Cap-labels record, mask bit 2: an UNDO label block follows.
    ///
    /// <para>The UNDO keycap has TWO wordings and only one of them ever reached a peer. Its normal
    /// text is the game's live undo string (<c>CardsGameApi.UndoLabel()</c>), but during the
    /// EVENT-DISCARD pick flow the driver overrides it with the confirm dialog's own cancel option
    /// (<c>PlayTray.SetPickStatus</c> → <c>_pickUndoLabel</c>, e.g. "Wähle eine andere Karte") —
    /// the reachable stand-in for the 2D popup's second button. The mirror wrote a flat
    /// <c>GUI_UNDO</c>, so on every other screen the owner's cancel affordance read "Rückgängig".
    /// Exactly the defect record 13 was created for, on the cap next to the one it fixed.</para>
    /// </summary>
    public const byte CapLabelUndoBit = 1 << 2;

    /// <summary>
    /// Cap-labels record, mask bit 3: an item-USE label block follows.
    ///
    /// <para>Same class of defect, higher stakes: the USE cap normally reads "USE", but an
    /// item-surrender demand (event malus) overrides it with a demand-specific wording
    /// (<c>PlayTray.SetItemUseConfirmVisible(…, label)</c> — "the user must never read a surrender
    /// as an ordinary use"). The mirror hardcoded <c>Loc.Game("GUI_USE")</c>, so a peer watching a
    /// player hand over an item saw them apparently USE it.</para>
    /// </summary>
    public const byte CapLabelItemUseBit = 1 << 3;

    /// <summary>Every DEFINED bit of the cap-labels mask byte — masked on write AND on read so an
    /// undefined bit can never be pre-claimed by garbage (the board-UI overlay discipline). An OLD
    /// reader masks with its own narrower value (0x03 before this build) and therefore stops after
    /// the two blocks it knows — which is why the new bits are the HIGH ones and their blocks ride
    /// LAST.</summary>
    public const byte CapLabelDefinedMask =
        CapLabelConfirmBit | CapLabelSkipBit | CapLabelUndoBit | CapLabelItemUseBit;

    /// <summary>How many label slots <see cref="ExtIdCapLabels"/> can carry (one per defined mask
    /// bit) — the sizing input for the record's worst case.</summary>
    public const int CapLabelSlotCount = 4;

    /// <summary>UTF8 byte cap PER LABEL in <see cref="ExtIdCapLabels"/>. Button wordings are a few
    /// words ("Lange Rast durchführen"); truncation on a UTF8 CHARACTER boundary, re-clamped on
    /// read. Four labels + mask + lengths stay far under the 255-byte TLV ceiling
    /// (1 + 4 × (1 + 48) = 197).</summary>
    public const int CapLabelMaxBytes = 48;

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
