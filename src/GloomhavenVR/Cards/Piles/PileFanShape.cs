using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE ONE DEFINITION OF A BOARD PILE FAN'S SHAPE AND FACING — the arc's arch depth and roll gain,
/// the reading pitch, the head billboard and the no-board fallback pose. <see cref="PileBrowser"/>
/// (discard / burnt / any ability pile) and <see cref="ItemsPile"/> (the item chips) are the same
/// arc drawn from two files, and their mirrors on a peer's board
/// (<c>Net.RemoteBrowserFan</c> / <c>Net.RemoteItemFan</c>, through <c>Net.RemotePileFronts</c>)
/// draw the third and fourth copies of it.
///
/// <para><b>WHY IT IS ITS OWN FILE.</b> Every one of these terms had already been copied and every
/// copy had already cost something:</para>
/// <list type="bullet">
/// <item><b>The facing.</b> <c>ItemsPile.FaceHead</c> was documented as the "mirror of
/// PileBrowser.Tick's facing math" — an admitted copy that got one GUARD wrong: the billboard write
/// sat INSIDE <c>if (_boardAnchored)</c>, so with no control board the item arc went edge-on the
/// moment the player stepped sideways and stayed there, while the discard arc beside it turned to
/// follow. One expression, called from both, cannot have two guards.</item>
/// <item><b>The reading pitch.</b> Both mirrors dropped the trailing −12° and a peer's two arcs
/// stood 12 degrees more upright than the ones their owner was reading, at the shipped defaults,
/// with nobody having tuned anything. <c>Net.RemotePileFronts</c> gave the two mirrors ONE copy of
/// it; this gives all four the same one.</item>
/// <item><b>The arch depth and the roll gain.</b> <c>0.55</c> and <c>0.85</c> were inline literals
/// in both owner files and named constants on the mirror side, which is the arrangement
/// <c>scripts/check-mirrors.sh</c> cannot see at all: its extractor reads named constants, so a
/// retune of an inline literal is invisible to every checker the project has.</item>
/// <item><b>The no-board fallback pose.</b> <c>PlaceAtHead</c> was twenty character-identical lines
/// in both files.</item>
/// <item><b>The board anchor and the board-top pose.</b> <c>BoardFloatHeight</c>,
/// <c>BoardFloatProudZ</c>, <c>BoardAnchorBase</c> and <c>PlaceAboveBoard</c> were a fourth copied
/// set — and the copy had already drifted by one term: <c>ItemsPile.Tick</c> re-poses the open arc
/// at <c>BoardAnchorBase + BrowseFanOffset + ItemFanOffset</c> while its own
/// <c>PlaceAboveBoard</c> dropped the item nudge, so the arc opened at the browse fan's spot and
/// was moved to its own on the next tick. That is the whole argument for this file in miniature:
/// two spellings of one pose, and the one that ran less often was the one that was wrong.</item>
/// </list>
///
/// <para><b>THESE ARE AUTHORED CONSTANTS, NOT DIALS, AND DELIBERATELY NOT THE HAND FAN'S.</b> The
/// hand fan reads <c>[Cards] FanFlatCurvatureFactor</c> / <c>FanTiltFactor</c> live; the board pile
/// fans have never had a config entry for their arc shape. The numbers are equal today and they are
/// not the same number — pointing these at the hand fan's entries would make a hand-fan retune
/// silently reshape two arcs the owner's own retune leaves alone, which is the "wrong entry"
/// mistake <c>scripts/check-remote-defaults.py</c> exists to catch. If the owner's pile arcs ever
/// gain a dial, these become wire-overridable fields like the fan radius already is.</para>
/// </summary>
internal static class PileFanShape
{
    /// <summary>
    /// THE BOARD FANS' READING PITCH, in degrees about the fan root's own local X — the trailing
    /// <c>* Quaternion.Euler(-12f, 0f, 0f)</c> appended to the head billboard. A card's +Z points
    /// AWAY from the viewer, so this tilts the arc back a touch toward the reader.
    /// </summary>
    internal const float ReadingPitchDegrees = -12f;

    /// <summary>
    /// THE BOARD FANS' ARCH DEPTH — the <c>0.55</c> in <c>(cos(rad) - 1) * radius * 0.55f</c>, which
    /// bends the arc around a pivot BELOW the root without dropping the ends the full sagitta.
    /// </summary>
    internal const float ArchFactor = 0.55f;

    /// <summary>THE BOARD FANS' ROLL GAIN — the <c>0.85</c> in <c>Euler(0, 0, -angle * 0.85f)</c>,
    /// so a card leans slightly less than its own arc angle.</summary>
    internal const float TiltFactor = 0.85f;

    /// <summary>
    /// How far ABOVE the control board's top edge a pile fan's pivot floats, in board-local metres —
    /// high enough to clear the top edge and the initiative track drawn over it. The pivot lives in
    /// board-local space, so it scales with the board automatically. Raise it to lift the whole fan.
    /// </summary>
    internal const float BoardFloatHeight = 0.26f;

    /// <summary>The same pivot's proud offset toward the viewer (board-local −Z is out of the board
    /// face), metres.</summary>
    internal const float BoardFloatProudZ = -0.05f;

    /// <summary>
    /// The pile fans' FIXED board-local base anchor. The live anchor is this plus the debug-menu
    /// tunable <c>[Cards] BrowseFanOffset</c> (and, for the item fan, its own <c>ItemCardOffset</c>
    /// nudge) — re-read every tick while board-anchored, so the Piles 'Browse X/Y/Z' steppers move
    /// an OPEN fan immediately.
    /// </summary>
    internal static Vector3 BoardAnchorBase =>
        new(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);

    /// <summary>
    /// The board-anchored reading pose: a spot a comfortable reading height above the control
    /// board, centred on its long axis. The root is a child of the board root, so this board-LOCAL
    /// offset inherits the board's live scale and pose — the fan tracks a two-hand board resize and
    /// a board switch — and the arc hangs downward from it, clearing the top edge and the
    /// initiative track (see <see cref="BoardFloatHeight"/>).
    ///
    /// <para>POSITION is written here and re-read every tick (live tuning); the FACING is
    /// billboarded here too, so an arc is never presented for one frame un-billboarded.</para>
    /// </summary>
    /// <param name="t">The fan root, already parented under the board root.</param>
    /// <param name="extraOffset">The fan's OWN nudge on top of <c>[Cards] BrowseFanOffset</c> —
    /// zero for the ability piles, <c>ItemCardOffset</c> for the item fan. It is a parameter rather
    /// than a second copy of this method because it is the only term that differs, and because the
    /// item fan's own copy used to omit it on the open path and add it on the tick path.</param>
    internal static void PlaceAboveBoard(Transform? t, Vector3 extraOffset)
    {
        if (t == null)
            return;
        t.localPosition = BoardAnchorLive(extraOffset);
        t.localRotation = Quaternion.identity; // the billboard below owns the WORLD rotation
        FaceHead(t);
    }

    /// <summary>The live board-local anchor — the base plus <c>[Cards] BrowseFanOffset</c> plus the
    /// fan's own nudge. Shared with the per-tick re-read so the OPEN pose and the TICK pose cannot
    /// resolve to two different spots, which is exactly how the item fan's two spellings drifted.</summary>
    internal static Vector3 BoardAnchorLive(Vector3 extraOffset)
        => BoardAnchorBase + CardsConfig.BrowseFanOffset.Value + extraOffset;

    /// <summary>
    /// Billboard a fan root toward the local head, with the reading pitch on top. UNGUARDED by
    /// anchoring on purpose: an arc is re-faced whether it hangs off the control board or off the
    /// head fallback, which is the defect this file's header records. No-op when there is no head
    /// camera yet or the root sits on the eye point.
    /// </summary>
    internal static void FaceHead(Transform? t)
    {
        if (t == null)
            return;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Vector3 away = t.position - head.transform.position;
        if (away.sqrMagnitude < 1e-6f)
            return;
        // +Z points away from the viewer (uGUI/sprites read from -Z); tilt back a touch.
        t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up)
                     * Quaternion.Euler(ReadingPitchDegrees, 0f, 0f);
    }

    /// <summary>
    /// The fixed head-relative reading pose used when there is no control board to hang the fan
    /// off — placed ONCE per open (deliberately no per-frame follow); the FACING is then
    /// re-billboarded every tick through <see cref="FaceHead"/>. Scale comes from the root's parent
    /// so the standoff is in the same real metres the board path uses.
    /// </summary>
    internal static void PlaceAtHead(Transform? t)
    {
        if (t == null)
            return;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Transform headT = head.transform;
        Vector3 flatForward = headT.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        float scale = t.parent != null ? t.parent.lossyScale.x : 1f;
        Vector3 pos = headT.position
                      + flatForward * (CardsConfig.TrayForward.Value * 0.9f * scale)
                      + Vector3.up * (-(CardsConfig.TrayDown.Value - 0.22f) * scale);
        t.position = pos;
        t.rotation = Quaternion.LookRotation(flatForward, Vector3.up)
                     * Quaternion.Euler(ReadingPitchDegrees, 0f, 0f);
    }
}
