using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE KEYCAP-PRESS EDGE, published for the multiplayer extras sampler.
///
/// <para>WHY THIS EXISTS. The 2026-08-08 ruling requires every interaction, ANIMATION and display of
/// the control board to mirror onto that player's remote board on every peer's machine. Almost all
/// of the keycap animation needs no wire at all: a cap's dust dissolve, its materialize-from-dust
/// and its state colours are FUNCTIONS of state already synced by the board-UI record, so a
/// receiver plays them off transitions it can already see. The PRESS is the exception — it is an
/// EVENT, it lasts about 170 ms (<c>BoardButton._press</c> decaying at 6/s) and at the 5 Hz extras
/// cadence it can fall entirely between two packets. Five bits of a record that already exists
/// (<see cref="Net.NetProtocol.ExtIdHalfHover"/> byte 0 bits 3..7) carry it.</para>
///
/// <para>WHY A STATIC LATCH AND NOT AN EVENT. The sampler runs on the net driver's own tick, which
/// is not the frame a press happens on, so the press has to survive until it is read. It is held
/// for <see cref="HoldSeconds"/> — long enough that the record rides several packets and a dropped
/// datagram still delivers the press, short enough that an idle board stops writing the record and
/// its packets go back to byte-identical with the previous build's. Repeat presses of the SAME cap
/// are distinguished by a 2-bit SEQUENCE, because the receiver animates the field CHANGING (it is
/// a latch, not a pulse) and two identical latch values would otherwise be one event.</para>
///
/// <para>WHO REPORTS. The two press commit points on the board:
/// <c>PlayTray.BoardButton.Press</c> (CONFIRM / UNDO / item-USE / the two rest discs / the
/// FOLLOW-PIN toggle) and <c>WorldUI.ButtonCluster.PhysicalButton.Fire</c> (the turn-flow SKIP cap).
/// Both call at the point where the press has already passed every gate — disabled, activation
/// guard, debounce, grip chord — so a refused attempt is never mirrored. Caps that are not on the
/// wire report <see cref="Net.NetProtocol.CapPressNone"/> and are ignored here.</para>
///
/// <para>NOTHING ABOUT THIS MAKES A REMOTE BOARD INTERACTIVE. What crosses is "this cap was
/// pressed"; the receiver replays the owner's dip on a colliderless copy and invokes nothing. See
/// <c>Net.RemoteBoardFurniture</c>'s inertness contract.</para>
/// </summary>
internal static class BoardCapPress
{
    /// <summary>
    /// How long a reported press stays readable. Comfortably above the 5 Hz extras interval (0.2 s)
    /// so the record rides ~3 packets, and below the per-cap press debounce
    /// (<c>WorldUI.ButtonTuning.PokePressCooldownSeconds</c> = 0.4 s) plus the send pre-emption, so
    /// two presses can never collapse into one latch value.
    /// </summary>
    internal const float HoldSeconds = 0.6f;

    private static byte _cap = Net.NetProtocol.CapPressNone;
    private static byte _seq;
    private static float _until = float.NegativeInfinity;

    /// <summary>
    /// Report that <paramref name="wireCap"/> has just been pressed. A
    /// <see cref="Net.NetProtocol.CapPressNone"/> (a cap that is not mirrored) is a no-op, so the
    /// call site never has to branch. The sequence advances on EVERY reported press, including a
    /// repeat of the same cap — that is the whole point of it.
    /// </summary>
    internal static void Report(byte wireCap)
    {
        if (wireCap == Net.NetProtocol.CapPressNone || wireCap > Net.NetProtocol.CapPressMaxId)
            return;
        _cap = wireCap;
        _seq = (byte)((_seq + 1) & 0x03);
        _until = Time.unscaledTime + HoldSeconds;
        Core.VRLog.Debug("Net", $"Board cap press REPORTED: wire cap {wireCap}, sequence {_seq} — " +
                                $"held {HoldSeconds:F2}s on extras record 14 byte 0 bits 3..7 so every " +
                                "peer's mirrored copy of this cap dips and springs back with it.");
    }

    /// <summary>
    /// The press currently in its hold window, if any. False (and the outputs are the "none"
    /// sentinel / 0) once the window has elapsed, which is what lets an idle board's packets stay
    /// byte-identical to the previous build's.
    /// </summary>
    internal static bool TrySample(out byte wireCap, out byte sequence)
    {
        if (Time.unscaledTime <= _until && _cap != Net.NetProtocol.CapPressNone)
        {
            wireCap = _cap;
            sequence = _seq;
            return true;
        }
        wireCap = Net.NetProtocol.CapPressNone;
        sequence = 0;
        return false;
    }

    /// <summary>Drop any live press (board teardown / scenario exit) so a stale latch can never
    /// animate a cap on a board that has since been rebuilt.</summary>
    internal static void Clear()
    {
        _cap = Net.NetProtocol.CapPressNone;
        _until = float.NegativeInfinity;
    }
}
