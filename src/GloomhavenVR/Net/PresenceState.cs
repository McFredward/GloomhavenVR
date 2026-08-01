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
///                        packet; its absence marks a pre-handshake peer, see NetProtocol.ModBuild)
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
    /// (mod version, the largest record) = 70, rounded up to 96 for headroom. Local buffer
    /// bound only — nothing on the wire depends on it.</summary>
    public const int MaxSize = 96;

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
        bool extensions = state.HasHandScale || state.HasGhostSides || state.HasModVersion;
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
                buffer[countAt] = records;
            }
        }
        return i;
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
                    i += len; // known or not, the record's own length is how we move past it
                }
            }
        }
        return true;
    }
}
