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
        /// How far ABOVE THE CARD'S BOTTOM EDGE the hand closes on it, in metres — the user's
        /// "am unteren Rand … ohne zu viel zu verdecken".
        ///
        /// <para>A DISTANCE, NOT A FRACTION, and that correction came from the item cards. It was
        /// 0.14 of the card's HEIGHT, which is the same thing for one card size and wrong for two:
        /// an item card is near-square (ItemsPile fits them to a native ~300x300 rect) and 40 mm
        /// shorter than an ability card, so the same fraction lifted its bottom edge 5 mm and the
        /// glove's thumb — which sits at v -0.98 on an ability card, right at the edge — fell off
        /// it entirely. The station reported the thumb clearance as Infinity, meaning no thumb joint
        /// inside the card at all.</para>
        ///
        /// <para>The grip point is a fact about the HAND, so its distance to the card's edge is a
        /// hand distance and does not scale with the card. Stated that way it is right for both
        /// sizes at once, and it reproduces the tall-card geometry that was already
        /// render-verified: 20 mm of a 140.8 mm held ability card is 14.2 %, which is the fraction
        /// it replaces.</para>
        ///
        /// <para>The clamp is the one guard: on a card small enough that 20 mm would be past its
        /// middle, the grip moves back to a third of its height so the hand can never hold a card
        /// by its centre.</para>
        /// </summary>
        internal const float GripBelowMetres = 0.020f;

        /// <summary>The grip's distance below the card's centre line, for a card of
        /// <paramref name="cardHeight"/> metres — <see cref="GripBelowMetres"/>, clamped so it can
        /// never pass the card's middle.</summary>
        internal static float GripBelow(float cardHeight) =>
            Mathf.Min(GripBelowMetres, cardHeight * 0.33f);

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
        /// How long the hand takes to CLOSE on the card, in seconds — the shipped
        /// <c>Defaults.InHandGraspSeconds</c>.
        ///
        /// <para>The user's requirement, verbatim: "die Handposition [soll] auch nicht von einem
        /// Frame zum anderen sofort in die Position gehen sondern die Finger sollen sich aktiv in
        /// die Position begeben (Animation) genau wie die Karte selber auch. Das soll schon schnell
        /// gehen, aber eben mit der Animation." Both halves matter: it is an animation, and it is a
        /// short one.</para>
        ///
        /// <para>EASED, NOT DECAYED, and that is the difference between "actively moving into
        /// position" and "settling". The five curls and the card pose already rode exponential
        /// smoothers (FingerCurler at 18/s, the card at ~21/s, both ~95 % converged in 0.15 s), so
        /// nothing ever snapped — but an exponential starts at full speed and decelerates, which
        /// reads as a spring relaxing rather than as a hand deciding to take hold of something. This
        /// duration drives a <see cref="Ease"/>d 0..1 progress instead, so the motion starts from
        /// rest, accelerates, and arrives at rest. The old smoothers still ride on top and cost
        /// nothing: they are now smoothing a signal that is already smooth.</para>
        ///
        /// <para>ONE progress drives EVERYTHING — the five finger curls, the card's position and its
        /// rotation, on the local hand, in the mirror and on every peer. That is not tidiness: two
        /// timers would let the fingers arrive before the card, which is a hand closing on empty air
        /// and then a card appearing in it.</para>
        /// </summary>
        internal const float DefaultGraspSeconds = 0.22f;

        /// <summary>Smoothstep — the ease every part of the grasp shares. Starts and ends at rest,
        /// fastest in the middle.</summary>
        internal static float Ease(float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }

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
        /// <para>NOTE the thumb entry here is the GLOVE's; the two gauntlets take theirs from
        /// <see cref="ThumbCurlByStyle"/>, which is the one value this pose could not share. The
        /// thumb is also the value that had to be MEASURED, and the target is not "as far
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
        ///   the shipped thumb per style      24 mm    27 mm    25 mm   (0.40 / 0.20 / 0.20)
        ///   a single 0.22 everywhere         33 mm    27 mm    25 mm
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

        /// <summary>
        /// THE THUMB IS THE ONE VALUE THAT IS PER HAND STYLE, indexed as <c>Hands.HandStyle</c>
        /// (0 Glove, 1 Plate, 2 Arcane). Everything else in <see cref="Curls"/> is shared.
        ///
        /// <para>This is a concession, and it was forced by measurement rather than chosen. The user
        /// on ModBuild 318: "beim Arcane und Platte ist der Daumen unnatürlich gebogen." Rendering
        /// the whole family — the same hold at thumb 0.00 to 0.80, all three styles, through the
        /// preview station's CARDGRIP_THUMB override — shows two curves that do not intersect:</para>
        /// <code>
        ///           0.20        0.25        0.30        0.40
        ///   glove   thumb OFF   thumb OFF   thumb OFF   on the card, 12.0 mm
        ///           the card    the card    the card    and straight
        ///   plate   straight    straight    tip hooks   tip clearly crooked
        ///   arcane  straight    slight      tip hooks   pronounced hook
        /// </code>
        /// <para>"Thumb OFF the card" is not a judgement: the station reports the glove's thumb
        /// clearance as INFINITY below 0.40, meaning no thumb joint lands inside the card's
        /// rectangle at all. So the glove needs 0.40 to touch its card and the two gauntlets are
        /// already over-flexed there. One number cannot do both.</para>
        ///
        /// <para>WHY THE STYLES DIFFER, since it is not obvious: the curl is a FRACTION of a fixed
        /// full-curl angle (FingerCurler's 25/45/60 for the thumb), and the three assets have
        /// different thumb rest poses and different segment lengths. The glove's thumb starts
        /// further from the card and has to travel; the gauntlets' start closer and arrive early,
        /// after which the same fraction keeps bending the distal joint into a hook. FingerCurler
        /// drives one flexion axis per joint, so there is no abduction to trade against it.</para>
        ///
        /// <para>The four FINGERS are unaffected — they measured within a couple of degrees of each
        /// other on all three rigs (unity/hand-prep/splay_check.py), which is why only the thumb
        /// gets a table.</para>
        /// </summary>
        internal static readonly float[] ThumbCurlByStyle = { 0.40f, 0.20f, 0.20f };

        /// <summary>The modelled curl of one finger, indexed as <c>Hands.Finger</c>, for a hand of
        /// <paramref name="style"/> (indexed as <c>Hands.HandStyle</c>). Out-of-range indices return
        /// the shared value rather than throwing: this is read from a per-frame hand tick, and a
        /// style this table has not been extended for should get a hand pose, not an
        /// exception.</summary>
        internal static float CurlFor(int finger, int style)
        {
            if (finger == 0 && style >= 0 && style < ThumbCurlByStyle.Length)
                return ThumbCurlByStyle[style];
            return finger >= 0 && finger < Curls.Length ? Curls[finger] : 0f;
        }

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
                                                 cardHeight * 0.5f - GripBelow(cardHeight), 0f);
        }

        /// <summary>
        /// THE READING POSE — the OTHER card hold, and the one this file's header contrasts itself
        /// against: a card pinched between thumb and index with its face leaned back toward the
        /// wrist so it reads at rest without a supination. (This solves the FIXED local rotation;
        /// the per-frame billboard that <c>VRCard.TickHeldPose</c> runs on top of it is the
        /// caller's, and it is what makes this a reading pose and the <see cref="Solve"/> next door
        /// a rigid one.)
        ///
        /// <para><b>WHY IT LIVES HERE NOW.</b> It was written twice: <c>VRCard.GetHeldPose</c> for
        /// an ability card and <c>ItemsPile.ItemChip.GetHeldPose</c> for an item card, the second
        /// an admitted verbatim copy of the first whose own comment listed everything that had to
        /// "stay byte-identical" — and the 2026-08-09 report is what that list cost: the left-hand
        /// mirror was fixed in one copy and not the other, and the item card sat 11 cm out for five
        /// days. The header above records that the lateral rule "HAS NOW BEEN BROKEN TWICE IN THIS
        /// FILE'S SHORT LIFE"; the reading pose broke it a third time, next door. The 2026-09-05
        /// round gave the figures' and props' held poses one home
        /// (<c>Board.FigureGrab.HeldPoseMirror</c>) and the two card poses did not join it. They
        /// join here, beside the grip they are the alternative to.</para>
        ///
        /// <para><b>THE HEIGHT IS THE ONLY LICENSED DIFFERENCE</b> between the two callers, and it
        /// is a parameter for exactly that reason: an item card is near-square and 40 mm shorter
        /// than an ability card, so it must grip its own height or the pinch lands in the wrong
        /// place on it. Everything else is one expression.</para>
        /// </summary>
        /// <param name="faceBiasDegrees">How far the face normal leans back from the palm normal
        /// (+Y) toward the wrist (−Z) — [Cards] HeldFaceBias. At the ~65° default the face points
        /// at your eyes in a relaxed grip with no wrist twist.</param>
        /// <param name="thumbSide">+1 on the RIGHT hand, −1 on the left. The card TOP points to the
        /// thumb side, which is world-up in a relaxed grip, so the card stands out of the pinch
        /// like a really held playing card. The two grab anchors are anatomical mirrors, so this is
        /// the only term that can be handed.</param>
        /// <param name="pinchLocal">Where the hand closes on the card, in anchor-local metres — the
        /// midpoint of the thumb and index TIPS. The caller has already added the tuning nudge
        /// ([Cards] HeldPinchOffset) and mirrored its lateral term on the left hand, for the same
        /// reason this method takes a side at all.</param>
        /// <param name="cardHeight">The card's height in metres AT ITS HELD SCALE.</param>
        /// <param name="gripFraction">How far up the card the fingers grip, as a fraction of its
        /// height — the card CENTRE then sits (0.5 − this) × height above the pinch.</param>
        /// <param name="pos">Card centre, anchor-local.</param>
        /// <param name="rot">Card rotation, anchor-local.</param>
        internal static void ReadingPose(float faceBiasDegrees, float thumbSide, Vector3 pinchLocal,
                                         float cardHeight, float gripFraction,
                                         out Vector3 pos, out Quaternion rot)
        {
            // GrabAnchor frame: +Y out of the palm, +Z along the fingers, ±X thumb side. The FACE
            // NORMAL is the palm normal leaned faceBias° back toward the wrist (−Z): in a relaxed
            // grip the fingers point forward/slightly down, so −Z runs back and UP toward the head.
            float bias = faceBiasDegrees * Mathf.Deg2Rad;
            var faceNormal = new Vector3(0f, Mathf.Cos(bias), -Mathf.Sin(bias));
            // Card +Z (away from the viewer) = −faceNormal; card top (+Y) = thumb side.
            rot = Quaternion.LookRotation(-faceNormal, new Vector3(thumbSide, 0f, 0f));
            pos = pinchLocal + rot * new Vector3(0f, cardHeight * (0.5f - gripFraction), 0f);
        }
    }
}
