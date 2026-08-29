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
    /// <para>THE LATERAL AXIS IS THE HANDED ONE, and EVERY term that touches it takes the hand's
    /// sign — the face normal (<see cref="Solve"/>), the corner offset (<see cref="GripAcross"/>)
    /// and the tuning nudge ([Cards] InHandPinchOffset, mirrored by the caller). ±X is the THUMB
    /// side on the right hand and the PINKY side on the left, because the two grab anchors are
    /// anatomical mirrors, so a lateral term added RAW moves the card toward the thumb on one hand
    /// and away on the other — the 2026-08-04 held-card report, in full. +Y and +Z mean the same
    /// thing on both hands and are never flipped.</para>
    ///
    /// <para>THAT RULE HAS NOW BEEN BROKEN TWICE IN THIS FILE'S SHORT LIFE, both times by an
    /// argument that a term "already mirrors itself". It is written above as an unconditional so
    /// there is nothing left to reason about: if it multiplies X, it multiplies
    /// <c>thumbSide</c>.</para>
    /// </summary>
    internal static class CardGripPose
    {
        /// <summary>
        /// How far up the card, as a fraction of its height, the hand closes on it — the user's
        /// "am unteren Rand … ohne zu viel zu verdecken".
        ///
        /// <para>0.05 was the first value and it was too small, for a reason only the instrument
        /// could see. The card is placed relative to the grip point, and the two contacts are 20 mm
        /// apart ALONG the card on some rigs; with the card's bottom edge only 7 mm below the grip
        /// point, the thumb fell OFF THE BOTTOM of the card entirely on the glove — the preview
        /// reported its clearance as infinite, meaning no thumb joint landed inside the card's
        /// rectangle at all. The render looked fine: a card held by four fingers with a thumb just
        /// under its edge is not something the eye flags. At 0.14 every contact on every rig is
        /// inside the card, and the thumb lands at v -0.71 to -0.98 of the half-height — still the
        /// bottom sixth, still card border rather than art.</para>
        /// </summary>
        internal const float GripFraction = 0.14f;

        /// <summary>
        /// How far OFF THE CENTRE LINE the hand takes the card, as a fraction of its half-width —
        /// i.e. toward a bottom CORNER rather than the middle of the bottom edge.
        ///
        /// <para>This exists because of one number the preview station reports and the eye does not:
        /// with the card centred on the grip, the thumb landed at u -0.09 to -0.17 of the card's
        /// half-width, which is dead centre. That is exactly where a Gloomhaven ability card keeps
        /// its INITIATIVE NUMBER, so the tidiest-looking grip was the one that covered the single
        /// most-read glyph on the card. It is also not how a hand holds a card: you take it by a
        /// corner. Shifting the card 0.42 half-widths across puts the thumb around u -0.55 — well
        /// inside the card, well off the number.</para>
        ///
        /// <para>APPLIED WITH THE HAND'S SIGN, and the first cut of this got it wrong in the exact
        /// way this project has a report about. The reasoning was "the card's own right axis already
        /// flips with the hand, so one constant mirrors itself" — and it does flip in WORLD terms,
        /// which is why it sounded right. But the offset is added IN THE CARD'S OWN FRAME, so the
        /// same constant moves the thumb the same way in card space on both hands, while the thumb's
        /// natural landing point is mirrored (u -0.09 right, +0.09 left). Measured: the right hands
        /// came out at u -0.51 and the left at -0.33 — one hand holding its card near a corner and
        /// the other closer to the middle. Multiplied by the side, both land at ±0.55. Same
        /// authored-right-mirrored-left convention as every other lateral term here, and the same
        /// root cause as the 2026-08-04 held-card report.</para>
        /// </summary>
        internal const float GripAcross = 0.46f;

        /// <summary>
        /// How far the card leans back out of the FINGER direction toward the palm normal, in
        /// degrees, about the anchor's lateral (±X) axis. This is the one shape choice left after
        /// the pinch fixes everything else: the card's plane is already decided (it is the plane
        /// the thumb and the fingers close across — see <see cref="Solve"/>), so all that is left
        /// is how far up out of that pinch the card leans. Shipped as
        /// <c>Defaults.InHandPitch</c>; the runtime reads the dial and passes it to
        /// <see cref="Solve"/>, and the preview station renders this value.
        ///
        /// <para>0° points the card straight out past the fingertips, along the fingers. 90° stands
        /// it straight up out of the palm. The default sits between them, where a hand really
        /// carries a card it has pinched: out and up.</para>
        /// </summary>
        internal const float DefaultPitchDegrees = 30f;

        // ------------------------------------------------------------------- the finger pose --

        /// <summary>
        /// THE MODELLED HOLD, one curl per finger, indexed exactly like <c>Hands.Finger</c>
        /// (0 Thumb, 1 Index, 2 Middle, 3 Ring, 4 Pinky). 0 = straight, 1 = fully curled — the
        /// <c>Hands.FingerCurler</c> contract, which turns each into three joint rotations against
        /// that style's full-curl angles.
        ///
        /// <para>WHY AN OVERRIDE IS NEEDED AT ALL. In this mode the GRIP BUTTON IS HELD DOWN — that
        /// is the gesture that selects the mode — and the grip is also what drives middle/ring/pinky
        /// toward a fist. The un-overridden hand would be a closed fist with a card standing out of
        /// it. The hand is also NOT ghosted here (the ghost belongs to the reading mode), so it is
        /// fully opaque and fully in the picture.</para>
        ///
        /// <para>THUMB FLAT ON THE FACE, FOUR FINGERS CURLED BEHIND IT — the one-handed card hold,
        /// and it is the SECOND shape this went through. The first was a thumb-and-index PINCH, and
        /// it could not work on these rigs for a reason that is worth writing down: <b>the rig has no
        /// thumb opposition</b>. <c>FingerCurler</c> drives exactly one flexion axis per joint
        /// ("positive local X curls toward the palm"), so the thumb can only flex in its own plane —
        /// it can never rotate round to face the index across a card. Curl both and they converge
        /// into nearly the SAME lateral plane instead of straddling one. Measured across all three
        /// rigs at the best pinch pair, the thumb tip and index tip were 7 mm apart ACROSS the card
        /// and a finger is about 18 mm thick, so no plane existed with the thumb cleanly on one side
        /// and the index on the other — which is exactly the user's report on that build: "in deinen
        /// Bildern clippen Finger durch die Karte".</para>
        ///
        /// <para>The thumb value is the one that had to be MEASURED, and the target is not "as far
        /// from the fingers as possible". It is a SANDWICH: the card wants roughly two finger
        /// half-thicknesses plus its own — call it 21 mm — between the thumb tip and the knuckle
        /// backing it. Too little and the two converge into one plane and the card cuts through both
        /// (the pinch, above). Too much and the card touches only the thumb and reads as balanced on
        /// it rather than held — which is exactly what the FIRST fix of this defect produced, at
        /// thumb 0.22: a 33 mm gap, no clipping at all, and a card standing on a thumb with the
        /// fingers nowhere near it. Sweeping the thumb against the finger cascade and reading the
        /// smallest lateral gap to any backing knuckle, per rig, at the shipped style scale:</para>
        /// <code>
        ///                                   glove    plate   arcane
        ///   thumb 0.40 (shipped)             24 mm    21 mm    18 mm
        ///   thumb 0.22 (balanced on it)      33 mm    27 mm    25 mm
        ///   the rejected tip pinch            7 mm     7 mm     3 mm
        /// </code>
        /// <para>The four fingers get a slight cascade because a hand closing on something does
        /// that, and the pinky closes hardest.</para>
        ///
        /// <para>Render-checked AND measured, not eyeballed: PreviewCardGrip.cs in the companion
        /// project poses the shipped rigs at these five numbers, places the card through this file's
        /// own <see cref="Solve"/>, and reports every joint that lands inside the card's rectangle
        /// with the SIDE it is on — a thumb in front, a finger behind, and anything on the wrong side
        /// named. All six hands come back with nothing through the card.</para>
        /// </summary>
        internal static readonly float[] Curls = { 0.40f, 0.90f, 0.94f, 0.97f, 1.00f };

        /// <summary>The modelled curl of one finger, indexed as <c>Hands.Finger</c>. Out-of-range
        /// indices return 0 (straight) rather than throwing: this is read from a per-frame hand
        /// tick.</summary>
        internal static float CurlFor(int finger) =>
            finger >= 0 && finger < Curls.Length ? Curls[finger] : 0f;

        // ------------------------------------------------------------------------ the solver --

        /// <summary>
        /// The card's pose in the grab anchor's frame.
        ///
        /// <para>THE CARD'S PLANE IS THE PINCH'S PLANE, and getting that wrong is what the first
        /// attempt got wrong. It put the card's FACE along the palm normal — the card stood up out
        /// of an open palm like a slice of toast in a rack — and the user rejected exactly that
        /// ("Eine Karte hält man auch nicht mit der Handfläche nach oben, sondern zwischen Daumen und
        /// Zeigefinger"), with the clipping as the symptom: a card whose plane lies ACROSS the hand
        /// has every finger passing through it, because the fingers close across the palm and the
        /// card was lying in their way.</para>
        ///
        /// <para>A hold has a plane, and it is the one the thumb closes across. In the grab-anchor
        /// frame the thumb lies on the LATERAL side of the fingers — +X is the thumb side on the
        /// right hand and the pinky side on the left, which is why <paramref name="thumbSide"/> is
        /// the one handed term in this method — so that axis IS the card's face normal: the thumb
        /// ends up flat along the face and every finger joint is behind it, by 25 mm and more on
        /// all three rigs (see <see cref="Curls"/> for the measurement). Nothing closes through the
        /// card, because the fingers curl toward the palm, and the palm is now a direction PARALLEL
        /// to the card rather than across it.</para>
        ///
        /// <para>The reader is on the THUMB side. Hold a card in one hand and look at it: the palm
        /// turns sideways and the thumb is the side facing you, lying across the face. That falls
        /// out of this frame rather than being chosen — and it is also why the first pose could
        /// never have been right, because it required the palm to be turned UP, which the user
        /// rejected in as many words.</para>
        ///
        /// <para>Only <paramref name="pitchDegrees"/> is left to choose, and it rotates the card
        /// within its own plane: 0 points it straight out past the fingertips, 90 stands it up out
        /// of the palm. Because the rotation is about the face normal itself, the plane — and
        /// therefore the whole no-clipping argument — is the same at every value of the dial.</para>
        /// </summary>
        /// <param name="pitchDegrees">Lean out of the finger direction; see
        /// <see cref="DefaultPitchDegrees"/>.</param>
        /// <param name="thumbSide">+1 on the RIGHT hand, -1 on the left — which way the anchor's +X
        /// points anatomically. The two grab anchors are mirrors (+Y out of the palm and +Z along
        /// the fingers on BOTH hands), so this is the only term that can be handed, and getting it
        /// wrong shows the left hand the BACK of its own card.</param>
        /// <param name="pinchLocal">Where the hand closes on the card, in anchor-local metres — the
        /// midpoint of the THUMB TIP and the INDEX KNUCKLE, which are the two things that actually
        /// sandwich a card in this hold (thumb pad on the face, the index's middle phalanx behind
        /// it). Sampled off the LIVE rig by the caller, so the card follows the modelled hold
        /// instead of a guess at where it ended up. The caller has already added the tuning nudge
        /// ([Cards] InHandPinchOffset) and, on the LEFT hand, mirrored its lateral term, for the
        /// same reason this method takes a side at all.</param>
        /// <param name="cardWidth">The card's width in metres AT ITS HELD SCALE, because the grip
        /// sits <see cref="GripAcross"/> of a half-width off its centre line.</param>
        /// <param name="cardHeight">The card's height in metres AT ITS HELD SCALE (the caller has
        /// already applied the close-up magnification), because the offset from the grip to the
        /// card centre is a fraction of it.</param>
        /// <param name="pos">Card centre, anchor-local.</param>
        /// <param name="rot">Card rotation, anchor-local.</param>
        internal static void Solve(float pitchDegrees, float thumbSide, Vector3 pinchLocal,
                                   float cardWidth, float cardHeight,
                                   out Vector3 pos, out Quaternion rot)
        {
            // Card TOP leans from the finger direction (+Z) toward the palm normal (+Y) by the
            // pitch; the card's own +Z — which is AWAY from the reader, the project-wide card
            // convention — is the lateral axis pointing away from the thumb, so the readable face
            // looks back along the thumb side.
            float p = pitchDegrees * Mathf.Deg2Rad;
            var up = new Vector3(0f, Mathf.Sin(p), Mathf.Cos(p));
            var forward = new Vector3(thumbSide >= 0f ? -1f : 1f, 0f, 0f);
            rot = Quaternion.LookRotation(forward, up);
            // The hand holds the card near a BOTTOM CORNER, so the card's centre sits most of a
            // half height above the grip along the card's own up axis, and GripAcross of a
            // half-width to one side of it along its own right axis.
            pos = pinchLocal + rot * new Vector3(thumbSide * cardWidth * 0.5f * GripAcross,
                                                 cardHeight * (0.5f - GripFraction), 0f);
        }
    }
}
