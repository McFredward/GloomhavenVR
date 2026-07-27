namespace GloomhavenVR.Cards;

/// <summary>
/// Which control-board pile stack a browse request refers to (test #21).
///
/// WIRE CONSTANT — THE NUMERIC VALUES OF THESE MEMBERS ARE TRANSMITTED.
/// <c>Net.NetAvatarDriver.TickExtrasSend</c> casts this enum straight onto the extras packet's
/// trailing-block byte A (bits 0..1); the receiver decodes it against
/// <c>Net.NetProtocol.PileBrowseKindDiscard</c> / <c>…Burnt</c> / <c>…Items</c>. Nothing in the
/// compiler links these two files, so the values are APPEND-ONLY: never renumber, never insert in
/// the middle. Adding a FOURTH pile at the end is fine (the field is 2 bits — four values).
///
/// A mistake here is invisible: no compiler error, no single-player symptom, and the sender's own
/// screen stays correct, because a sender never parses its own packet. It shows up only as every
/// OTHER player rendering the wrong pile's fan.
///
/// The explicit <c>= 0/1/2</c> is part of the defence, not decoration: with the values written
/// down, sorting these members alphabetically is harmless. A deliberate renumber or a mid-list
/// insertion is caught at compile time by <c>Net.NetAvatarDriver.PileKindWireOrderGuard</c>.
/// See <c>.planning/refactor/INVARIANTS-Net-Rig.md</c> Part I §4c.
/// </summary>
internal enum PileKind
{
    /// <summary>The discard pile (<c>CCharacterClass.DiscardedAbilityCards</c>).</summary>
    Discard = 0,

    /// <summary>The burnt pile (lost + permanently lost, the 2D "burnt" header union).</summary>
    Burnt = 1,

    /// <summary>The acting character's ITEM cards (<c>PlayerActor.Inventory.AllItems</c>).</summary>
    Items = 2,
}
