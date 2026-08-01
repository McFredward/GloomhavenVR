using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>Which controller button grabs a card (Demeo grabs with the index/trigger; test-#22 Demeo-parity pass).</summary>
internal enum CardGrabButton
{
    Grip,
    Trigger,
}

/// <summary>
/// Which control-board (PlayTray) prefab is loaded from the asset bundle. Switchable
/// live from the VR settings panel; the bundle asset paths are mapped in
/// <see cref="VRCardFactory.GetTrayPrefab"/>. Oak is the original bundled board (default);
/// a selected prefab that is not yet in the bundle falls back to Oak, then to the
/// procedural board, so this compiles and runs before the new bundle ships.
///
/// WIRE CONSTANT — THE NUMERIC VALUES OF THESE MEMBERS ARE TRANSMITTED.
/// <c>Net.LocalRigSampler.LocalBoardStyle</c> casts this enum through
/// <c>Net.NetProtocol.EncodeBoardStyle</c> onto the extras packet's trailing-block byte A
/// (bits 5..6), and the receiver casts the decoded code straight back to a
/// <see cref="ControlBoard"/>. Nothing in the compiler links these two files, so the values are
/// APPEND-ONLY: never renumber, never insert in the middle. The field is 2 bits — a FOURTH board
/// appends for free, a FIFTH needs a trailing wire byte (see <c>NetProtocol.BoardStyleMaxCode</c>).
///
/// Oak MUST stay 0: <c>BoardStyleDefaultCode == 0</c> is what makes "style bits absent" and "Oak"
/// render identically, which is the entire reason this field cost no presence bit and no version
/// bump. A mistake here is invisible locally — the sender never parses its own packet — and shows
/// up only as every OTHER player seeing the wrong board material.
///
/// The explicit <c>= 0/1/2</c> makes an alphabetising sort harmless; Oak's anchoring at 0 and the
/// two-bit range are checked at compile time by
/// <c>Net.LocalRigSampler.ControlBoardWireOrderGuard</c>.
/// See <c>.planning/refactor/INVARIANTS-Net-Rig.md</c> Part I §4c.
/// </summary>
internal enum ControlBoard
{
    Oak = 0,
    Steel = 1,
    Bronze = 2,
}

/// <summary>
/// Helpers over <see cref="ControlBoard"/>, mirroring <c>Hands.HandStyles</c> — the reference
/// implementation for a USER-FACING style choice in this codebase (count / clamp / display name).
///
/// WHY THIS EXISTS AT ALL: picking the control board is a normal-user FEATURE, exactly like the
/// hand style and the head mask (user: "Genau wie die Hände und die Maske soll auch das Board an
/// sich außerhalb des Debug-Menüs umgestellt werden können"). A user-facing control must show a
/// LOCALIZED NAME, not the raw C# enum member, and the number of boards must come from ONE place
/// so the cycle button, the wire clamp and any future fourth board all agree. Both of those used
/// to be open-coded at every call site (<c>Board.Value.ToString()</c> and a hardcoded <c>% 3</c>),
/// which is precisely the kind of duplication that lets a new board be added everywhere but one.
/// </summary>
internal static class ControlBoards
{
    /// <summary>Number of selectable boards (wire values and cycle steps clamp to [0, Count-1]).</summary>
    public const int Count = 3;

    /// <summary>Clamp an arbitrary (config / wire) value to a valid board.</summary>
    public static ControlBoard Clamp(int value) =>
        (ControlBoard)Mathf.Clamp(value, 0, Count - 1);

    /// <summary>The next board in the cycle (wraps) — the single definition of the cycle order.</summary>
    public static ControlBoard Next(ControlBoard b) => (ControlBoard)(((int)b + 1) % Count);

    /// <summary>
    /// LOCALIZED display name ("Eiche" / "Stahl" / "Bronze"), for every user-facing readout. The
    /// enum member name stays the config/log/wire identity — only what the PLAYER reads is
    /// translated, the same split the mask/hand-style pickers use.
    /// </summary>
    public static string DisplayName(ControlBoard b) => b switch
    {
        ControlBoard.Steel => Core.Loc.Mod("board_steel"),
        ControlBoard.Bronze => Core.Loc.Mod("board_bronze"),
        _ => Core.Loc.Mod("board_oak"),
    };
}

/// <summary>
/// How the handle-bar grab may MOVE the control board (item 12, [Cards] BoardMoveMode) —
/// selectable in the normal settings (localized labels "Frei"/"Begrenzt"/"Begrenzt mit Neigung",
/// see <c>VROptionsTab.TryBuildSpecialRow</c>). Purely LOCAL cosmetics like the rest of the
/// board pose: peers receive whatever world pose results via the extras stream, unchanged.
///
/// The member ORDER is the settings-dropdown index map (Free=0/Limited=1/LimitedPitch=2) — the
/// preset row casts the dropdown index straight to this enum. Values are config-file identity
/// only; nothing goes over the wire.
/// </summary>
internal enum BoardMoveMode
{
    /// <summary>Fully free: the board follows the grabbing hand in ALL axes, 1:1.</summary>
    Free = 0,

    /// <summary>Today's behavior (default): position + yaw only, kept level for the player.</summary>
    Limited = 1,

    /// <summary>Like <see cref="Limited"/>, plus the grab may PITCH the board inside the
    /// per-board [Cards] BoardPitchMin_&lt;board&gt;..BoardPitchMax_&lt;board&gt; window
    /// (debug-menu tunable).</summary>
    LimitedPitch = 2,
}

/// <summary>
/// Cap shape of a control-board button group (in-VR debug menu, per board). Round = the
/// flattened-cylinder puck that drops into a round notch (today's Rest look); Square = the
/// boxy 3D keycap (today's Confirm/Undo look). Selectable per group so a board with square
/// rest pads or round confirm buttons can match its art. Drives which cap
/// <see cref="PlayTray.BoardButton.Create"/> builds (round vs boxy) instead of hardcoding it.
/// </summary>
internal enum ButtonShape
{
    Round,
    Square,
}
