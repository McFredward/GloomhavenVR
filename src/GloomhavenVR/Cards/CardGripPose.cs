using UnityEngine;

// BLOCK NAMESPACE AND C# 9, DELIBERATELY — the whole file is symlinked into the companion Unity
// project as Assets/Editor/CardGripPoseLink.cs so the preview station renders THE POSE THE GAME
// USES rather than a re-typed copy of it (the same arrangement, and the same reason, as
// Core/GrabBar.cs ↔ Assets/Editor/GrabBarMeshLink.cs). Unity 2021.3 compiles C# 9, which has no
// file-scoped namespaces, so this file cannot use the form the rest of src/ does.
//
// FOR THE SAME REASON IT DEPENDS ON NOTHING BUT UnityEngine: no BepInEx, no CardsConfig, no
// Defaults. Every tunable arrives as a PARAMETER; the config reads live in the callers
// (Cards.VRCard, Cards.ItemsPile.ItemChip) and in Cards.HeldCardGrip.
namespace GloomhavenVR.Cards
{
    /// <summary>
    /// THE MODELLED IN-HAND GRIP: where a card sits when it is physically held in the fist, and
    /// how the five fingers close on it.
    ///
    /// <para>This is the geometry half of the second card-holding mode (user, 2026-08-29: "Ich
    /// möchte daher einen neuen Modus bauen: Wenn man die Greiftaste gedrückt hält und dann
    /// trigger drückt um eine Karte zu nehmen, soll man die Karte wirklich 'in die Hand nehmen'
    /// und vollständig rotieren können … Modellier dafür für jede Hand auch eine ideale
    /// Greifposition, die die Karte physisch greift an dem unteren Rand der Karte ohne zu viel zu
    /// verdecken."). The MODE machine — when this pose applies, and who else needs to know — is
    /// <see cref="HeldCardGrip"/>.</para>
    ///
    /// <para>WHAT MAKES THIS DIFFERENT FROM THE POSE THAT ALREADY EXISTED. The normal mode
    /// (<c>VRCard.TickHeldPose</c>) re-billboards the card to the head every frame: the face
    /// always stares at the holder's eyes, whatever the wrist does. That is a reading pose and it
    /// is deliberately NOT rigid. This one IS rigid — it is the pose of a card that has been taken
    /// into the hand, and turning the wrist turns the card, which is the entire point of the mode
    /// (you can aim the face at another player). So it has to survive being looked at from every
    /// side, which the billboard pose never did, and that is why the two are separate solvers
    /// instead of one with a flag.</para>
    ///
    /// <para>FRAMES. Everything below is in the GRAB ANCHOR's frame, the hand-rig contract shared
    /// by the procedural hand and every bundle glove (<c>Hands.HandRig</c>; Anchor_Grab is a child
    /// of Anchor_Palm at identity rotation): <b>+Y out of the palm, +Z along the fingers</b>, and
    /// ±X laterally — which is the THUMB side on the right hand and the PINKY side on the left,
    /// because the two anchors are anatomical mirrors. The card's own frame is the project-wide
    /// one: <b>+Z away from the reader</b> (so the readable face points at −Z) and <b>+Y the card
    /// top</b>.</para>
    ///
    /// <para>THE POSE IS ANATOMICALLY SYMMETRIC, and that is worth stating because the billboard
    /// pose next door is not. That one puts the card top on the ±X axis and therefore needs its
    /// tuned lateral offset mirrored by hand (see <c>VRCard.GetHeldPose</c>, and the 2026-08-04
    /// report that caused it). This one is built from +Y and +Z only — the two axes that mean the
    /// same thing on both hands — so the left hand needs no sign flip anywhere and cannot acquire
    /// the same defect. The lateral offset is the one term that is still handed, and the caller
    /// mirrors it for exactly the same reason.</para>
    /// </summary>
    internal static class CardGripPose
    {
        /// <summary>
        /// How far up the card, as a fraction of its height, the fingers close on it. The user's
        /// requirement is "am unteren Rand … ohne zu viel zu verdecken", so this is deliberately
        /// SMALLER than the billboard pose's 0.12: the pinch sits in the bottom twentieth of the
        /// card, which is card border on every ability and item face in the game — the fingers
        /// cover frame, never art.
        /// </summary>
        internal const float GripFraction = 0.05f;

        /// <summary>
        /// Rest tilt of the card out of the palm plane, in degrees about the anchor's lateral
        /// (±X) axis — the pinch axis. The shipped default of
        /// <c>Defaults.InHandPitch</c>; the runtime reads the dial and passes it to
        /// <see cref="Solve"/>, and the preview station renders this value.
        ///
        /// <para>0° lays the card flat in the palm pointing along the fingers, which is the one
        /// value that must NOT be used: the hand is then directly behind the card from the palm
        /// side and directly in front of it from the back, so one of the two people looking at it
        /// always sees a hand instead. 90° stands it straight up out of the pinch, clear of every
        /// finger but the two holding it. The default leans it back from there toward the wrist,
        /// which is what a hand actually does with a card it is about to read.</para>
        /// </summary>
        internal const float DefaultPitchDegrees = 72f;

        // ------------------------------------------------------------------- the finger pose --

        /// <summary>
        /// THE MODELLED GRIP, one curl per finger, indexed exactly like <c>Hands.Finger</c>
        /// (0 Thumb, 1 Index, 2 Middle, 3 Ring, 4 Pinky). 0 = straight, 1 = fully curled — the
        /// <c>Hands.FingerCurler</c> contract, which turns each into three joint rotations against
        /// that style's full-curl angles.
        ///
        /// <para>WHY THIS HAS TO BE MODELLED AT ALL, rather than left to the controller. In this
        /// mode the GRIP BUTTON IS HELD DOWN — that is the gesture that selects the mode — and the
        /// grip is also what drives middle/ring/pinky toward a full fist. So the un-overridden hand
        /// would be a closed fist wrapped around a card standing out of it, which is neither what a
        /// hand holding a card looks like nor something you can see the card past. The hand is also
        /// NOT ghosted in this mode (the user's ruling: the ghost belongs to the reading mode), so
        /// it is fully opaque and fully in the picture.</para>
        ///
        /// <para>THE THUMB AND INDEX ARE MEASURED, NOT CHOSEN. The card is placed at the MIDPOINT
        /// of those two fingertip joints (<c>HeldCardGrip.TryPose</c>), so "is this a pinch" is
        /// literally the distance between them, and the first pass got it badly wrong by reasoning
        /// about it: 0.58/0.52 looked like a firm pinch written down and measured 48 mm apart on the
        /// shipped rig — an open hand with a card floating above it, which is exactly what the first
        /// render showed. The pair below is the result of sweeping both curls over all three hand
        /// rigs (unity/hand-prep/curl_check.py replays these very rotations offline) and reading the
        /// gap:</para>
        /// <code>
        ///                            unscaled rig     at the shipped [Hands] style scale
        ///   thumb 0.80 / index 0.88   glove  19 mm     23 mm  (x1.12)
        ///                             plate  29 mm     18 mm  (x0.62)
        ///                             arcane  8 mm      5 mm  (x0.62)
        /// </code>
        /// <para>Those are JOINT separations — the fleshy tips reach a good centimetre further — so
        /// all three close ON the card, the arcane glove hardest and the plate gauntlet in a loose
        /// armoured pinch. The plate's raw rig is the floor and that is a property of the asset, not
        /// a tuning failure: its minimum over the whole sweep is 27 mm at 0.70/0.80, and curling
        /// harder from there OPENS it again as the armoured thumb swings past the index. One pair
        /// serves all three styles on purpose — this is a hand pose, not a per-asset rig.</para>
        ///
        /// <para>AND ONE IDEA THE SAME MEASUREMENT KILLED, recorded so it is not re-proposed: deriving
        /// the card's face normal from the actual THUMB-TO-INDEX AXIS instead of from
        /// <see cref="DefaultPitchDegrees"/>, so the card plane would pass exactly between the two
        /// fingers on any rig. It sounds strictly better and it is not. Measured in the grab-anchor
        /// frame at these curls the axis is (-0.30, +0.63, +0.72) on the glove, (-0.39, -0.92, -0.07)
        /// on the plate and (-0.64, -0.54, +0.55) on the arcane glove — the glove's palm-normal
        /// component points the OPPOSITE way to the plate's, because the thumb passes the index on
        /// the other side. A normal built from that would flip the card by roughly 180 degrees
        /// between two hand models, i.e. the same wrist would show a peer the face or the back
        /// depending on which glove you had picked. A dial the player can see beats a derivation
        /// that can invert.</para>
        ///
        /// <para>The other three follow the pinch in the cascade a hand really falls into, each a
        /// little more closed than the one before. They are NOT relaxed: at the first pass's gentle
        /// curls all three stood up straight PAST the card's bottom edge and into its art, which the
        /// render showed at once. Curled, they stay below the grip and out of the picture.</para>
        ///
        /// <para>Render-checked, not eyeballed: see PreviewCardGrip.cs in the companion project,
        /// which poses the shipped rig at these five numbers and puts the card at
        /// <see cref="Solve"/>'s answer.</para>
        /// </summary>
        internal static readonly float[] Curls = { 0.80f, 0.88f, 0.72f, 0.80f, 0.86f };

        /// <summary>The modelled curl of one finger, indexed as <c>Hands.Finger</c>. Out-of-range
        /// indices return 0 (straight) rather than throwing: this is read from a per-frame hand
        /// tick.</summary>
        internal static float CurlFor(int finger) =>
            finger >= 0 && finger < Curls.Length ? Curls[finger] : 0f;

        // ------------------------------------------------------------------------ the solver --

        /// <summary>
        /// The card's pose in the grab anchor's frame.
        /// </summary>
        /// <param name="pitchDegrees">Tilt out of the palm plane; see <see cref="DefaultPitchDegrees"/>.</param>
        /// <param name="pinchLocal">Where the fingers close, in anchor-local metres — the midpoint
        /// of the thumb and index tips, which the caller samples off the live rig so the card
        /// follows the modelled grip instead of a guess at where it ended up. The caller has
        /// already added the tuning nudge ([Cards] InHandPinchOffset) and, on the LEFT hand,
        /// mirrored its lateral term: ±X is the thumb side on one hand and the pinky side on the
        /// other, so a raw X would push the card toward the thumb on one and away on the other
        /// (the 2026-08-04 report, in full, on the billboard pose).</param>
        /// <param name="cardHeight">The card's height in metres AT ITS HELD SCALE (the caller has
        /// already applied the close-up magnification), because the offset from the pinch to the
        /// card centre is a fraction of it.</param>
        /// <param name="pos">Card centre, anchor-local.</param>
        /// <param name="rot">Card rotation, anchor-local.</param>
        internal static void Solve(float pitchDegrees, Vector3 pinchLocal, float cardHeight,
                                   out Vector3 pos, out Quaternion rot)
        {
            // Card TOP = the finger direction (+Z) tilted up out of the palm (+Y) by the pitch;
            // the card therefore stands out of the pinch AWAY FROM THE WRIST and past the
            // fingertips, where nothing on the hand is behind it. Card +Z (away from the reader)
            // is the same rotation applied to −Y, so the readable face looks OUT OF THE PALM at
            // pitch 0 and leans back toward the holder as the pitch rises.
            float p = pitchDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(p), s = Mathf.Sin(p);
            var up = new Vector3(0f, s, c);
            var forward = new Vector3(0f, -c, s);
            rot = Quaternion.LookRotation(forward, up);
            // The pinch holds the card's BOTTOM EDGE region, so the centre sits most of a half
            // height further along the card's own up axis.
            pos = pinchLocal + rot * new Vector3(0f, cardHeight * (0.5f - GripFraction), 0f);
        }
    }
}
