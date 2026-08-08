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
    public const ushort ModBuild = 92;
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

    /// <summary>Every DEFINED bit of the board-UI record's byte 2 (bit 7 reserved, written 0 and
    /// masked on read). Same discipline as <see cref="BoardUiOverlayMask"/>.</summary>
    public const byte BoardUiCapStateDefinedMask =
        (byte)(BoardUiCapConfirmAccentBit | BoardUiCapConfirmReadyBit
               | BoardUiCapShortRestEnabledBit | BoardUiCapShortRestAccentBit
               | BoardUiCapLongRestEnabledBit | BoardUiCapLongRestAccentBit
               | BoardUiCapSkipEnabledBit);

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
    /// the last factor is <c>[Cards] SlotCardFill</c>, whose DEFAULT is 1.45 — while the remote
    /// mirror hardcoded <c>Defaults.CardWidth × SlotScale</c> and dropped the fill entirely, so
    /// even two default-configured clients disagreed by 31 %: everyone's remote cards rendered at
    /// 82.6 mm where their owner sees 119.7 mm. Both factors are LOCAL CONFIG on the sender and
    /// therefore not derivable from anything already synced; the effective sizes must ride the
    /// wire, exactly like the board style and the hand scale before them.</para>
    ///
    /// <para>WHY TWO WIDTHS: the board's slot visuals are TWO independent sizes layered from the
    /// same config — the recess/glow FRAME metric (<c>CardWidth × SlotScale</c>, what
    /// <c>PlayTray.4.Slots</c> sizes the wanted-glow/frame quads from) and the CARD occupying it
    /// (that × <c>SlotCardFill</c>). Transmitting only the card width would leave the receiver
    /// unable to reproduce the frame (the fill is not recoverable from one number), so the glow
    /// overlays would mis-frame the very card the record just fixed.</para>
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
    // stated in that change's report per the rule above. Ids 18..21 remain free, and so does 29+.

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

    // ---- record 25: USE BARS ------------------------------------------------------------------

    // ---- record 28: BOARD TUNING (the owner's OWN dial positions) ----------------------------
    // Ids 25, 26 and 27 are taken by records developed in parallel with this one; 18..21 remain
    // reserved. A shipped record id can never be renumbered, so this one is 28 by assignment,
    // not by "the next free number" (that habit already produced one id collision — see the note
    // above record 22).

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
    /// <para>LAYOUT — <c>[n][n × field]</c>, fields in ASCENDING ID ORDER (deterministic bytes; a
    /// reader may early-out). A field is <c>[id][value]</c> and THE ID'S RANGE FIXES THE VALUE
    /// WIDTH, which is what keeps the record self-describing without spending a length byte per
    /// field:
    /// <list type="bullet">
    /// <item><see cref="TuneVecIdMin"/>..<see cref="TuneVecIdMax"/> — 6 bytes, a Vector3 as
    ///   3 × i16 LE in TENTH-MILLIMETRES (<see cref="EncodeTuneLength"/>).</item>
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
    /// board). Every one of those is an order of magnitude below what an eye resolves at arm's
    /// length, and every code is readable as-is in a hardware log.</para>
    ///
    /// <para>NO IDENTITY, NO GAMEPLAY: the payload is a list of the sender's own cosmetic dial
    /// positions. Nothing here consults a card, an actor or the game model.</para>
    ///
    /// <para>ADDITIVE TLV exactly like every record before it: an older peer steps over it by its
    /// length and keeps seating everything at the shipped defaults — today's look, never a broken
    /// one.</para>
    /// </summary>
    public const byte ExtIdBoardTuning = 28;

    /// <summary>Field cap of <see cref="ExtIdBoardTuning"/> — the number of dials the record can
    /// name (see the id table below; 58 are defined today, the last eight being the item fan's
    /// open/close ANIMATION set added on the presence pass). It bounds the record at
    /// 1 + 15×7 + 13×3 + 22×3 + 6×3 + 2×2 = 233 payload bytes, still under the 255-byte TLV
    /// ceiling, and is re-clamped on read against the record's own length.</summary>
    public const int BoardTuneMaxFields = 58;

    /// <summary>Minimum payload of <see cref="ExtIdBoardTuning"/> (the field-count byte alone). A
    /// reader requires at least this much before it looks at the record.</summary>
    public const int BoardTuneMinRecordBytes = 1;

    // ---- field id RANGES (the range is the value width — see the record doc) ------------------

    /// <summary>First / last id whose value is a Vector3 (6 bytes, 3 × i16 tenth-mm).</summary>
    public const byte TuneVecIdMin = 1;
    public const byte TuneVecIdMax = 63;

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

    // COUNT (1 B).

    /// <summary>[Cards] FanMaxHandForCurve — hand size at which the fan's curvature saturates.</summary>
    public const byte TuneFanMaxHandForCurve = 224;
    /// <summary>[Cards] FanCurveMinCards — hands at or below this stay flat.</summary>
    public const byte TuneFanCurveMinCards = 225;

    /// <summary>Payload width of a board-tuning field with this id — 6 / 2 / 1, or 0 for a
    /// RESERVED id whose width this build does not know (the reader then abandons the rest of the
    /// record rather than mis-parsing it; see the record doc).</summary>
    public static int BoardTuneFieldWidth(byte id)
    {
        if (id >= TuneVecIdMin && id <= TuneVecIdMax) return 6;
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
        if (payload == null || len < BoardTuneMinRecordBytes
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
