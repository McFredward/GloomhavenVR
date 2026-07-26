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

    // ---- extras (type 1) flag bits --------------------------------------------------------

    /// <summary>Extras packet: a control-board pose (pos+rot+scale) is present.</summary>
    public const byte FlagHasBoard = 1 << 0;

    /// <summary>Extras packet: the sender's dominant hand is the RIGHT hand (mirror of the rig
    /// <see cref="FlagDominantRight"/>, laid out at bit1 in the extras flag byte per the wire spec).</summary>
    public const byte FlagExtrasDominantRight = 1 << 1;

    /// <summary>
    /// Extras packet: a 1-byte ITEM-fan card count trails the packet (the sender's equipped-item fan
    /// — <c>Cards.ItemsPile</c> — is open with that many item cards). ADDITIVE extension exactly
    /// like the rig packet's <see cref="FlagHandStyle"/>: appended AFTER every field older readers
    /// know, so a peer built before this flag ignores the unknown bit and the trailing byte and
    /// simply shows no item fan. Absent flag ⇒ no item fan this packet.
    /// </summary>
    public const byte FlagItemFan = 1 << 2;

    /// <summary>Extras packet: the sender's item fan is HAND-HELD (floating above the grabbing
    /// palm) rather than board-anchored above their control board. Meaningful only together with
    /// <see cref="FlagItemFan"/>; costs no payload bytes (pure flag).</summary>
    public const byte FlagItemFanHeld = 1 << 3;

    /// <summary>
    /// Extras packet: a 2-byte CARD-FX event block trails the packet — <c>seq</c> (wrapping
    /// counter) + <c>endpoints</c> (two 4-bit <see cref="CardFxAnchor"/> ids, from in the low
    /// nibble, to in the high nibble). Lets a peer PLAY the card animation locally (a card-back
    /// slab arcing between two anchors) instead of us streaming per-frame transforms: 2 bytes per
    /// animation vs ~20 bytes × 15 Hz for its whole duration. ADDITIVE, appended last; older peers
    /// ignore the bit and the trailing bytes and simply see no flight.
    /// </summary>
    public const byte FlagCardFx = 1 << 4;

    /// <summary>Extras packet: a HAND-HELD item fan (<see cref="FlagItemFanHeld"/>) is held in the
    /// LEFT hand. Pure flag, no payload — without it the receiver would have to guess a hand and
    /// would hang the fan off the wrong arm half the time.</summary>
    public const byte FlagItemFanLeft = 1 << 5;

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
