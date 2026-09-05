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
