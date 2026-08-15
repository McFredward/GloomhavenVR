// GloomhavenVR — SHELF RIDERS. The bookshelf's pose, and everything that is
// standing on the bookshelf when it goes over.
//
// USER VERDICT, hardware, ModBuild 144 (verbatim): "Die Kerzen und das Feuer,
// die auf dem Bücherregal stehen, kippen nicht mit - das musst du beheben das
// ist ein echter Bug. Sie müssen auf jeden Fall mitkippen."
//
// He is right and the shipped build knew it: BuildTippingShelf's own summary
// already carried the defect as a KNOWN COST ("the room's second candle GROUP
// stands ON this shelf and does not fall with it, because its wax, its flame
// cards and its halo are three other shaders"). This file is that cost paid.
//
// ============================================================================
// ONE SOURCE OF TRUTH FOR THE POSE, and why it is this file and not four.
//
// The shelf is drawn by EnvHaunt (kind 4, the PROP). The things standing on it
// are drawn by three OTHER shaders — the wax by EnvRoom, the candle flame and
// the two seated fires by EnvFlame, the halos by EnvGlow — and every one of
// them has to land on the SAME hinge, the SAME axis and the SAME angle, to the
// bit. A candle that takes a rotation of its own is a candle a centimetre
// inside the shelf it is standing on, and at 88 degrees of tip a centimetre of
// disagreement at the hinge is four centimetres at the top board.
//
// So GhvrShelfTip() and GhvrTipNow() live here, ONCE, and the shelf itself is
// a rider like any other: EnvHaunt's prop path calls GhvrTipNow() too. The
// mechanism is the one that already ships twice — EnvBeam reads the haunt
// schedule for card 0 so the moonbeam dims behind the thing at the window, and
// EnvRoomCutout reads it for card 3 so the cobwebs shiver — except that what a
// rider applies is not a brightness but the identical RIGID TRANSFORM.
// ============================================================================
//
// ============================================================================
// WHERE A RIDER'S POSITION RELATIVE TO THE HINGE COMES FROM.
//
// It does not come from anywhere: it is already there. Every rider is drawn
// from geometry that is authored where the rider really stands, so the vector
// from the hinge to any of its vertices is (vertex - hinge) in the rider's own
// object space, and the only thing that has to be transported is the hinge
// itself. That is EXACTLY the problem ApplyRig has already solved for the three
// candle positions — a light at a world point, expressed in each lit material's
// object space with Transform.InverseTransformPoint — so the hinge and the axis
// go out through the same door, per material, at bake time:
//
//   _TipPivot.xyz  the hinge, in THIS material's object space
//   _TipAxis.xyz   the hinge axis, in the same space
//
// THE THREE ALTERNATIVES, and why each is worse:
//   * A VERTEX CHANNEL. This is what the shipped shelf mesh did (UV2 = pivot,
//     UV3 = axis + angle). It puts a copy of one constant on every vertex —
//     12 bytes each of a value that never varies — and, far worse, it puts a
//     SECOND copy of the hinge in the bundle. The bake derives the hinge from
//     the placed shelf's measured bounds; a mesh baked in one build and a
//     material written in another would then disagree, silently, by exactly the
//     amount the prop moved. The shelf's own mesh lanes are therefore GONE this
//     round: it reads the material like everyone else.
//   * A MATERIAL PROPERTY IN WORLD SPACE, rotated in world space. Every lit
//     thing in this bundle is shaded in object space on purpose (EnvRoom's
//     header), several riders sit under transforms of their own, and the welded
//     wax mesh sits under the identity — so a world-space path would need a
//     matrix per rider that object space gives for free.
//   * RE-DERIVING THE HINGE PER RIDER from the shelf's bounds. That is the
//     "two copies of a rotation" the whole feature is written to avoid.
//
// The transforms involved are rigid with uniform scale, so a direction survives
// InverseTransformDirection unchanged and an angle in radians is space-free.
// The builder asserts the round trip (see AssertShelfRiders).
// ============================================================================
//
// ============================================================================
// THE ZERO STATE IS ONE UNIFORM COMPARE.
//
// With the haunt master down, or in any slot whose event is not the shelf,
// h.live * mine is 0, so `phase` is EXACTLY 0, and GhvrShelfTip(0) must be
// exactly 0 — not 1e-9, not a denormal. The angle is then exactly zero and
// every rider skips the rotation on a comparison that is UNIFORM across the
// draw.
//
// That is not a micro-optimisation, it is the correctness requirement: this
// round's zero state has to be bit-identical with the shipped one, and
// `pivot + (p - pivot)` is NOT p in floating point. A rider that "rotated by
// zero" would move the wax by an ulp and lose the md5 proof.
//
// The ModBuild 144 curve got this by inspection, because every one of its terms
// was written to vanish at 0 (fall = 0 -> down = 0*0; bounce carried
// step(0.18, 0) = 0; rise = smoothstep(0.62, 1, 0) = 0). The curve below is a
// TRANSCENDENTAL and cannot be read that way — 4*atan(U0*exp(0)) is whatever
// the GPU's atan says, to within an ulp, and an ulp is still not zero.
// It is pinned instead, by construction, in two places:
//   * the fall term is `saturate((4*atan(...) - PHI0B) * INVSPAN)` where PHI0B
//     is the release angle plus a 1e-5 rad BIAS, so the argument at ph = 0 is
//     -6.5e-6 * INVSPAN — negative by three orders of magnitude more than the
//     ~4e-9 rad an atan implementation can be wrong by on an argument this
//     small — and saturate() pins it to a literal 0.0. The cost is that the
//     first 1e-5 rad (0.0006 degrees) of the topple is clipped, which is
//     18 micrometres at the top board.
//   * the landing term is a parabola 4w(1-w) in a SATURATED w, so it is
//     identically 0 outside its window at both ends — the shipped sin() form
//     was only exactly 0 at its start, because sin(pi) in float is -8.7e-8.
// GhvrShelfTip(1.0) is exactly 0 for the same two reasons (see the recovery
// note in the function), which is what makes the end of the event a seam
// instead of a pop.
// ============================================================================
#ifndef GHVR_ENV_SHELFTIP_INCLUDED
#define GHVR_ENV_SHELFTIP_INCLUDED

// The schedule. GhvrHauntAt / GhvrHauntEnvelope decide WHEN, and a rider must
// ask the same oracle the shelf asks or the two answer at different seconds.
#include "EnvHaunt.cginc"

// Kept although the ModBuild 145 curve no longer needs it: the landing was a
// half-sine and is now a parabola (see the TIME CURVE block), but this is a
// public define of a shared header and the rider shaders that #include this
// file are another lane's this round.
#define GHVR_TIP_PI 3.14159265

// ---------------------------------------------------------------- the channel
// Written per material by EnvRoomBuilder.WriteShelfTip — ONE writer, so a rider
// cannot be given a hinge the shelf does not have. All five default to zero,
// which means "this material has never heard of the bookshelf", so everything
// in both rooms that is not involved is untouched and unmeasurably unchanged.
//
// KNOWING THE POSE AND TAKING IT ARE TWO DIFFERENT THINGS, and the split is the
// reason for _TipUse. The cellar's WALL does not ride the shelf — but the wall
// is lit by a fire that is standing on it, and by a candle that is standing on
// it, so the wall needs the hinge in ITS OWN object space in order to move a
// light it is being lit by. Every deferred material in the room therefore
// carries the pose; what each one DOES with it is _TipUse's four answers.
float4 _TipPivot;   // xyz hinge (OBJECT space), w = 1 -> the pose here is valid
float4 _TipAxis;    // xyz hinge axis (OBJECT space), w = max angle (radians)
float4 _TipSched;   // x slot period (s), y cards in the room, z this card's index
float4 _TipEnv;     // x reveal, y hold, z fade  (the event's authored envelope)
float4 _TipUse;     // x 1 = move MY OWN geometry with the shelf
                    // y  which baked light slot rides it, -1 = none
                    // z 1 = I GO OUT WHILE THE SHELF IS OVER. Two readers, and
                    //     they are deliberately the same flag: EnvFlame's candle
                    //     flame, which gutters and relights (GhvrTipFlameLife),
                    //     and EnvParticleAdd's two spark emitters standing on the
                    //     burning bookcase, which fade with the pose
                    //     (GhvrTipUpright). One flag because the question a
                    //     material is answering is one question — "does the topple
                    //     put me out?" — and how it goes out is its own business.
                    // w  spare (0)

// ============================================================================
// THE TIME CURVE — ModBuild 145. Why the shape below is not the shape that
// shipped.
//
// USER VERDICT, hardware, verbatim: "Mir gefällt wie das Regal fällt, aber es
// fällt aktuell so schräg und bleibt dann unrealistisch auf einer Kante liegen,
// es sollte realistisch Fallen. Außerdem — wenn sich das Regal resetet sollte
// es sich in einer ANimation rückwärts wieder aufstellen."
// ("I like HOW the shelf falls, but it currently falls at such an angle and
// then rests unrealistically on an edge — it should fall realistically. Also:
// when the shelf resets it should stand itself back up in an ANIMATION, played
// BACKWARDS.")
//
// THE COMPLAINT HAS TWO CAUSES IN TWO FILES and only the second is here.
// "Rests on an edge" is geometry: the bake stops the swing at TipDeg = 88, two
// degrees short of flat, and tips it along the diagonal (-0.8, 0, 0.6) so the
// shelf comes to rest balanced on a corner of its own carcass. That is
// BuildEnvironmentRooms.cs and is being fixed there. What is fixed HERE is the
// other half of "es sollte realistisch Fallen" — the TIME CURVE — plus the
// reset, which is entirely this function's.
//
// ---------------------------------------------------------------------------
// WHY A QUADRATIC IS THE WRONG CURVE FOR A TOPPLE.
//
// The shipped fall was `down = (ph/0.18)^2`: an angle quadratic in time, i.e.
// CONSTANT angular acceleration. That is the right law for a body in free fall
// — a dropped brick — and the wrong law for a body ROTATING ABOUT A HINGE,
// which is what a shelf going over is. A shelf pivoting on its base edge is a
// physical pendulum standing on its unstable equilibrium, and the torque that
// turns it is not constant: it is
//
//        I * theta'' = m g d sin(theta)          theta = 0 is upright
//
// so the torque is ZERO at the top and maximum at 90 degrees. A real shelf
// therefore leans away almost imperceptibly, accelerates through the middle as
// its own weight gets a longer and longer lever on it, and arrives at the floor
// at its highest speed of the whole event. A quadratic does the opposite of the
// interesting part: at ph = 0 it is already moving, and by half the fall it has
// covered a QUARTER of the angle where the real thing has covered an eighth.
// The measured difference, at 88 degrees of swing:
//
//     fraction of the fall TIME     10%    30%    50%    70%    90%   100%
//     shipped quadratic, degrees   0.88   7.92  22.00  43.12  71.28  88.00
//     this curve,        degrees   0.94   4.37  11.76  27.65  60.90  88.00
//
// The last tenth of the fall is 27 degrees under the new curve and 17 under the
// old one; the arrival is 1.78x faster. That is the difference between reading
// as "it fell over" and reading as "it was lowered".
//
// ---------------------------------------------------------------------------
// THE CURVE IS THE EXACT SOLUTION, NOT A FIT TO IT.
//
// theta'' = w^2 sin(theta) released from rest at the top has a closed form —
// the pendulum's SEPARATRIX, the one trajectory whose total energy is exactly
// the energy of standing upright:
//
//        theta(t) = 4 * atan( e^(w t) )
//
// Check it, because a formula this small looks like a guess: with u = e^(wt),
// theta' = 4 w u / (1+u^2), so (1/2) theta'^2 = 8 w^2 u^2 / (1+u^2)^2; and with
// a = atan(u), cos(theta) = cos(4a) = 1 - 8 sin^2(a) cos^2(a) = 1 - 8u^2/
// (1+u^2)^2, so w^2 (1 - cos theta) is the same expression. Energy conserved,
// zero total, released from the top. It is exact.
//
// It is also ASYMPTOTIC at both ends, which is the one thing that has to be
// handled rather than admired: a shelf balanced perfectly takes forever to
// start. So the animation is the window of that exact trajectory between a
// RELEASE ANGLE and the swing's end — the shelf is nudged to 2 degrees and let
// go, which is what a haunt does — and because t appears only inside e^(wt),
// that window is a straight line in log(u). One exp, one atan:
//
//        u(x)  = U0 * (U1/U0)^x  = U0 * e^(K x),   K = ln(U1/U0)
//        U0    = tan(2 deg / 4)     the release
//        U1    = tan(90 deg / 4)    the end of the swing
//        angle = 4 * atan(u(x))
//
// with x the fraction of the fall window. Eleven instructions, no table, no
// integration, no state — which is the standing requirement on this file: the
// pose is a pure function of the clock so that a player who joins mid-event
// sees the shelf where everyone else sees it.
//
// THE TWO CONSTANTS AND WHAT THEY BUY.
//   * 2 DEGREES of release. This alone sets how creepy the start is: it is the
//     only thing in the model that decides how long the shelf hangs there
//     before it commits. At 5 degrees the fall is nearly a quadratic again; at
//     0.5 degrees the first three seconds are visually dead and the last one is
//     a snap. At 2 degrees the shelf is at 2.3 deg after a fifth of the fall,
//     11.8 deg at the half way point, and 27 of the remaining 88 happen in the
//     last half-second.
//   * 90 DEGREES for the end of the swing. THIS IS A COUPLING TO THE BAKE and
//     is stated so it can be found: the physics is a function of the ABSOLUTE
//     angle (sin theta), so the shape depends on where the swing stops, and
//     the bake's TipDeg is the authority on that. It is deliberately written as
//     the flat-on-the-floor 90 rather than the shipped 88 because the other
//     half of this user report is moving TipDeg to at least 90. The
//     sensitivity is tiny and measured: TipDeg 88 -> 92 moves K by 1.3%, i.e.
//     under a third of a degree anywhere on the curve, so this constant does
//     not have to be chased. If TipDeg ever leaves the 85-95 band, recompute
//     U1, K and INVSPAN together.
//
// WHAT THE MODEL SAYS ABOUT ITSELF. K/T_fall is the pendulum's own w: 3.860 /
// 4.68 s = 0.825 rad/s, against sqrt(3g/2L) = 2.86 rad/s for a 1.8 m shelf. So
// this is the real trajectory played at 3.5x SLOW MOTION — the same 88 degrees
// would take a real bookcase 1.35 s, and 1.35 s is exactly the figure a real
// toppling bookcase takes. The slow motion is deliberate and was not touched:
// he said he likes how it falls, and the 4.7 s fall window is what he was
// looking at. What changed is the SHAPE inside that window, not its length.
//
// ---------------------------------------------------------------------------
// THE LANDING IS A BALLISTIC REBOUND, AND IT IS SMALLER AND SHORTER THAN THE
// ONE THAT SHIPPED.
//
// The shipped bounce was a half-sine 4.8 degrees deep over 1.56 seconds. That
// is not a bounce, it is a wallow: a second and a half is long enough to watch,
// and anything you can watch you can see is not physics. It was also
// inconsistent with the new arrival, which is nearly twice as fast.
//
// A body that hits and rebounds leaves at e * (its impact speed) and is pulled
// straight back down by a torque that, at 88 degrees, is at its MAXIMUM and
// effectively constant over a two-degree excursion. Constant acceleration for
// the whole of a short flight is a parabola in time — which is why the term is
// literally the projectile arc 4w(1-w) and not a sine. Both numbers then follow
// from the one free parameter, the coefficient of restitution:
//
//     impact rate      1.166 rad/s        (4 K U1/(1+U1^2) / T_fall)
//     alpha at 88 deg  0.680 rad/s^2      ( = w^2 sin(88 deg), w = K/T_fall )
//     window 0.624 s   -> rebound rate alpha*D/2 = 0.212 rad/s
//                      -> restitution 0.182, which is a wooden carcass full of
//                         books onto stone: mostly inelastic, one settle
//                      -> peak alpha*D^2/8 = 1.90 degrees
//
// so LAND = 0.624/26.0 = 0.024 of phase and BAMP = 1.90/90 = 0.021 of the
// swing. On a 1.8 m shelf that is a 6 cm kick at the top board, over six tenths
// of a second. ONE settle and no second bounce: a shelf full of books is a
// near-inelastic bag, the second rebound would be 0.18^2 = 3% of the first
// (0.07 degrees, two millimetres) and drawing it would be drawing a lie. A
// hard arrival is precisely the case where a bounce is RIGHT — it is a soft
// ease-out that must not have one — so the term stays; it is only re-derived.
//
// ---------------------------------------------------------------------------
// THE RESET IS THE FALL RUN BACKWARDS. Literally, not approximately.
//
// This is the explicit request and the shipped build did not do it. `rise =
// smoothstep(0.62, 1, ph)` is a symmetric ease that SUBTRACTS the angle back
// out: it leaves the floor gently, is fastest in the middle, and eases into
// upright. Nothing about that is the fall reversed, because the fall is not a
// smoothstep, and a symmetric curve cannot be the reverse of an asymmetric one.
// It reads as a mechanism lowering a prop back into place.
//
// What is here instead is the arc itself, evaluated backwards:
//
//        GhvrShelfTip(0.62 + 0.38 q) == GhvrShelfTip(ARC (1 - q))
//
// which is an identity of the code below and not a tuning — the recovery
// branch does not have a curve of its own, it feeds a reversed argument into
// the same GhvrTipArc(). There is exactly one shape in this file. The reverse
// of a hard arrival is a hard DEPARTURE: the shelf leaps off the floor, sheds
// speed the whole way up, and creeps through the last two degrees over the
// final second — the exact time-reverse of the two seconds it spent creeping
// away from upright at the start. It settles into standing asymptotically
// rather than arriving there, which is the unnerving read he is asking for and
// is the reason a reversed film of anything looks wrong.
//
// The mirrored landing comes with it, for free and correctly: 0.6 s into the
// recovery the shelf ROCKS UP 1.9 degrees, settles back flat, and only then
// lifts off. That is a rebound played backwards and it is the single frame that
// tells a player this is footage running the wrong way.
//
// THE RATE. The arc is 5.30 s and the recovery window is 9.88 s, so the reverse
// plays at 0.537x. That is deliberate and it is what keeps "IT FALLS FAST AND
// GETS UP SLOWLY" true: at half speed the departure from the floor peaks at
// 0.61 rad/s against the impact's 1.12, so nothing about the recovery reads as
// a fall in reverse-gear — it reads as something invisible lifting it.
//
// ---------------------------------------------------------------------------
// REJECTED, and why.
//   * A HAND-SHAPED EASE (pow(x, 3.5), or an exponential) fitted to look like
//     the pendulum. It would be two instructions cheaper and it would be a
//     number nobody can ever check. The closed form costs an exp and an atan
//     ONCE per GhvrTipNow (three times counting the central difference) on
//     what is already a per-vertex haunt schedule, and in exchange every
//     constant in it is a physical quantity with a unit.
//   * INTEGRATING theta'' = w^2 sin(theta) per frame. Forbidden by the file's
//     first rule: it is per-client state, two peers who joined at different
//     times would drift, and a player entering mid-event would see the shelf
//     stand up from wherever their integrator happened to start.
//   * A SECOND CURVE FOR THE RECOVERY, mirrored by hand. That is two sources of
//     truth for one motion — the exact failure this whole file exists to
//     prevent (see GhvrTipSlope on why even the derivative is not written out).
//   * MAKING THE FALL FASTER instead of resharpening it. He likes the fall. The
//     complaint was that it does not look like falling, not that it is slow.
// ============================================================================

// -- the exact separatrix, sampled between a 2 degree release and a 90 degree
//    stop. Recompute all four together if either angle moves (see above).
#define GHVR_TIP_U0      0.0087268678   // tan(2 deg / 4), the release
#define GHVR_TIP_K       3.8599751733   // ln(tan(90/4) / tan(2/4))
#define GHVR_TIP_PHI0B   0.0349165850   // 2 deg + 1e-5 rad of bias (see ZERO STATE)
#define GHVR_TIP_INVSPAN 0.6510926427   // 1 / (90 deg - PHI0B)
// -- the schedule of the arc, in phase. 26.002 s of event, so:
#define GHVR_TIP_FALL    0.18           // 4.68 s of topple
#define GHVR_TIP_LAND    0.024          // 0.62 s of rebound
#define GHVR_TIP_ARC     0.204          // FALL + LAND: the whole of the fall
#define GHVR_TIP_RISE    0.62           // 9.88 s of lying there, then the reset
#define GHVR_TIP_BAMP    0.021          // 1.9 deg of rebound at 90 deg of swing

/// THE ARC: everything the shelf does on its way to lying still, as a function
/// of seconds-into-the-fall expressed in phase. Held FLAT for any s beyond the
/// arc (both terms saturate), which is what lets the caller feed it an
/// unclamped argument for the lie-down and a reversed one for the recovery
/// without a single extra branch.
///
/// Returns 0 at s = 0 and 1 at s >= ARC, both EXACTLY — see the ZERO STATE
/// block at the top of the file, which is the whole reason for the bias in
/// PHI0B and for the parabola in place of the shipped sin().
float GhvrTipArc (float s)
{
    // the topple. x is the fraction of the fall window; u walks the separatrix
    // geometrically, because time enters the exact solution only as e^(wt).
    float x = saturate(s * (1.0 / GHVR_TIP_FALL));
    float n = saturate((4.0 * atan(GHVR_TIP_U0 * exp(GHVR_TIP_K * x))
                        - GHVR_TIP_PHI0B) * GHVR_TIP_INVSPAN);
    // the landing. w is the fraction of the rebound window; 4w(1-w) is the
    // projectile arc of a body thrown up under constant acceleration, which is
    // what a rebound off a hard floor at maximum gravitational torque is.
    float w = saturate((s - GHVR_TIP_FALL) * (1.0 / GHVR_TIP_LAND));
    return saturate(n - GHVR_TIP_BAMP * 4.0 * w * (1.0 - w));
}

/// The bookshelf's tip-over, as a pure function of the event phase.
///
/// IT FALLS FAST AND GETS UP SLOWLY, which is the whole picture: a shelf goes
/// over in about a second under gravity and then stands itself back up over
/// five, against nothing, which is the part that is wrong. Everything is a
/// shaped curve of `phase` and NOTHING IS INTEGRATED — the schedule is the only
/// thing that decides where the shelf is at time t, so a player who joins
/// mid-event, or an event cut off by the slot ending, cannot leave it lying
/// down.
///
///   0.000-0.180  the topple, on the pendulum's exact separatrix: 2.3 deg by a
///                fifth of the way through, 11.8 by half, and 27 of the 88 in
///                the last half second.
///   0.180-0.204  the landing: one ballistic rebound, 1.9 deg over 0.62 s.
///   0.204-0.620  it lies there, for 10.8 s. This is the part that has to be
///                long enough for somebody to walk over and look at it.
///   0.620-1.000  the same arc, RUN BACKWARDS at 0.537x over 9.88 s.
float GhvrShelfTip (float ph)
{
    // ONE LINE IS THE WHOLE SCHEDULE, and it is written this way so that the
    // recovery cannot possibly be a different curve from the fall.
    //
    //   min(ph, ARC)     the fall for ph < ARC; pinned at the end of the arc
    //                    afterwards, which is the lie-down (GhvrTipArc is flat
    //                    past ARC, so this needs no branch of its own)
    //   * (1 - q)        q is 0 until GHVR_TIP_RISE and then walks 0 -> 1, so
    //                    this REWINDS the pinned argument from ARC back to 0.
    //
    // Hence GhvrShelfTip(RISE + 0.38 q) == GhvrShelfTip(ARC (1 - q)) as an
    // algebraic identity: the recovery IS the fall, addressed backwards. And
    // at ph = 1, q = 1, so the argument is a literal 0.0 * ARC = 0.0 and the
    // event ends on the same exact zero it started on.
    float q = saturate((ph - GHVR_TIP_RISE) * (1.0 / (1.0 - GHVR_TIP_RISE)));
    return GhvrTipArc(min(ph, GHVR_TIP_ARC) * (1.0 - q));
}

/// Rodrigues, about `axis` through `pivot`. RIGID — no per-vertex weight, no
/// skinning: a bookshelf and everything on it is one body while it falls, and
/// that is precisely why one transform can serve four shaders.
float3 GhvrTipRot (float3 p, float3 pivot, float3 axis, float ang)
{
    float3 q = p - pivot;
    float s, c; sincos(ang, s, c);
    return pivot + q * c + cross(axis, q) * s + axis * dot(axis, q) * (1.0 - c);
}

/// The shelf's pose right now, decided once and handed to whoever asks.
struct GhvrTip
{
    float live;    // 1 = the pose is valid AND the shelf is off its rest angle.
                   //     THE ONE THING TO BRANCH ON: it is 0 for every material
                   //     in both rooms at every instant outside the event, and
                   //     it is a UNIFORM, so the branch is coherent.
    float ang;     // radians about `axis` through `pivot`; exactly 0 at rest
    float vel;     // dang/dt in rad/s — what makes a flame lag instead of turn
    float phase;   // 0..1 through the event, for anything that wants the shape
    float3 pivot;  // object space, straight off the material
    float3 axis;
};

/// THE DERIVATIVE IS TAKEN FROM THE POSE ITSELF, not from a hand-differentiated
/// copy of it. A flame that lags needs dang/dt, and the moment that is written
/// out as a second formula it is a second source of truth for the same motion —
/// the exact failure this file exists to prevent. A central difference of the
/// real curve costs two evaluations of eleven instructions and cannot drift.
/// h is in PHASE, and 1/400 of an event is 65 ms of a 26 s one: fine enough to
/// resolve a 4.7 s topple and coarse enough to step over the landing bounce's
/// own corner instead of resolving a spike there.
///
/// e = 0.0025 SURVIVED THE ModBuild 145 RESHARPENING, and this is the one place
/// in the file where that had to be checked rather than assumed. The new fall
/// arrives 1.78x faster, so the honest worry was that a 65 ms window would
/// either miss the peak or turn the impact corner into a spike. Measured
/// against the curve itself: the sampled peak is 1.122 rad/s at ph = 0.1775
/// (the last window lying wholly inside the fall) against a true instantaneous
/// 1.166 — a 4% UNDER-read, which is the right direction of error, because this
/// number drives a LAG and a lag that overshoots pokes a flame through the
/// board it is standing on. At the corner itself (ph = 0.18) the window
/// straddles the impact and reads 0.48 rad/s, i.e. it smooths the discontinuity
/// instead of resolving it, which is what the original choice of e was for.
/// Nothing here needs to change; nothing here may be SHRUNK without
/// re-measuring the lag, since a smaller e would sharpen both.
float GhvrTipSlope (float ph)
{
    const float e = 0.0025;
    return (GhvrShelfTip(ph + e) - GhvrShelfTip(ph - e)) / (2.0 * e);
}

GhvrTip GhvrTipNow (float t)
{
    GhvrTip o;
    o.pivot = _TipPivot.xyz;
    o.axis  = _TipAxis.xyz;
    o.live = 0.0; o.ang = 0.0; o.vel = 0.0; o.phase = 0.0;
    if (_TipPivot.w < 0.5) return o;

    GhvrHaunt h = GhvrHauntAt(t, _TipSched.x, _TipSched.y);
    float phase;
    GhvrHauntEnvelope(h.sIn, h.start, _TipEnv.x, _TipEnv.y, _TipEnv.z, h.durMul, phase);
    // abs()<0.5 rather than ==, exactly as GhvrHauntPresence does it: a card
    // index carried in a material float and compared against one carried in a
    // mesh UV is not a value to test for equality.
    float mine = step(abs(h.card - _TipSched.z), 0.5) * h.live;
    // ...and the multiply, not a branch, is what makes the rest state EXACT:
    // phase is then the literal 0 that GhvrShelfTip maps to the literal 0.
    phase *= mine;

    o.phase = phase;
    o.ang = _TipAxis.w * GhvrShelfTip(phase);
    // dphase/dt = 1 / (the event's real duration). The envelope scales all three
    // authored parts by the slot's durMul, so the run is (rev+hold+fade)*durMul
    // seconds long — the same denominator GhvrHauntEnvelope divides by.
    float dur = max((_TipEnv.x + _TipEnv.y + _TipEnv.z) * h.durMul, 1e-3);
    o.vel = _TipAxis.w * GhvrTipSlope(phase) * mine / dur;
    // EXACTLY zero at rest, so this is a real test and not a tolerance: see the
    // ZERO STATE block at the top of the file for why `rotate by zero` is not
    // an acceptable substitute for `do not rotate`.
    o.live = (o.ang != 0.0) ? 1.0 : 0.0;
    return o;
}

/// Move a point with the shelf — the whole of what a rider does to its own
/// geometry, so that no shader has to spell out the rotation a second time.
float3 GhvrTipPoint (GhvrTip tip, float3 p)
{
    return (tip.live < 0.5) ? p : GhvrTipRot(p, tip.pivot, tip.axis, tip.ang);
}

/// ...and a direction (a normal, a tangent): the same rotation about the origin.
float3 GhvrTipDir (GhvrTip tip, float3 d)
{
    return (tip.live < 0.5) ? d : GhvrTipRot(d, float3(0, 0, 0), tip.axis, tip.ang);
}

/// HOW MUCH OF ITSELF SOMETHING STANDING ON THE SHELF STILL HAS: 1 upright, 0
/// flat on the floor, and the whole way down it is the POSE ITSELF and not a
/// curve fitted to it.
///
/// USER, hardware, ModBuild 151 (verbatim): "Beim umgekippten Bücherregal kippt
/// die Funkenquelle nicht mit um, wenn Feuer an ist." and then, cutting the
/// round down: "Um es einfach zu halten: Deaktivier die Funken einfach
/// (ausfaden) wenn das Regal kippt."
///
/// The sparks off the burning bookcase are a Shuriken population simulated in
/// world space on the CPU, and this bundle has no scripts, so their emitter
/// cannot be moved — see the block above EnvRoomBuilder's `Sparks`. What CAN be
/// done from a material is to take them away while the thing they come off is
/// not there any more, and this is that factor.
///
/// IT CONTAINS NO CONSTANTS AT ALL, which is the point of writing it here rather
/// than as a threshold in the shader that consumes it:
///   * `tip.ang / _TipAxis.w` IS GhvrShelfTip(phase) — the same eleven
///     instructions the shelf's own vertices take, divided back out by the same
///     uniform they were multiplied by. There is no second curve to keep in step
///     and no phase landmark re-typed anywhere.
///   * it is therefore EXACTLY 0 at the arrival (phase 0.180, the sparks are
///     gone the frame the carcass reaches the floor and stay gone for the whole
///     10.8 s it lies there) and EXACTLY 1 at both ends of the event, so the
///     zero state is the shipped one bit for bit.
///   * and the return is the fall's own identity run backwards (see
///     GhvrShelfTip's one-line schedule), so "they come back when it stands up"
///     is not a second behaviour that could be tuned apart from the first — it
///     is the same expression addressed from the other side. The righting at
///     phase 0.620 is where they start coming back, because that is where the
///     angle starts coming off.
///
/// MEASURED, at the shipped TipDeg = 90 over a 26.002 s event: 1.00 at t = 0,
/// 0.96 at 1.3 s, 0.83 at 2.6 s, 0.73 at 3.1 s, 0.34 at 4.2 s, 0.00 at 4.68 s
/// (the arrival), and the mirror of that from 16.1 s to 26.0 s. The consumer
/// multiplies an ALPHA by it and EnvParticleAdd premodulates, so the drawn
/// energy goes as the square: a spark population at a 15 degree lean is already
/// down to 69 % of its energy, which is what makes this read as the fire being
/// taken away rather than as a dimmer being turned.
float GhvrTipUpright (GhvrTip tip)
{
    if (tip.live < 0.5) return 1.0;
    return saturate(1.0 - tip.ang / max(_TipAxis.w, 1e-4));
}

/// THE SAME FADE, EXPRESSED AS ENERGY INSTEAD OF AS ALPHA — for the terms at the
/// bookshelf's fire site that are drawn WITHOUT premodulation.
///
/// USER, hardware, ModBuild 152 (verbatim): "Da wo die Funken waren und
/// deaktiviert wurden ist aber immer noch eine Lichtquelle die dort scheint. Die
/// muss entweder auch mitwander oder auch deaktiviert werden."
///
/// He named both acceptable answers and the bake had already ruled the first one
/// out in writing: the term he is looking at is C_FireWashShelf, the GLOW ON THE
/// WALL behind the burning bookcase, and riding it would swing a 1.4 m sphere of
/// light through masonry. So it is deactivated — with the pose, exactly as the
/// sparks are, and out of this file so that there is no second fall curve.
///
/// WHY IT IS THE SQUARE AND NOT GhvrTipUpright ITSELF, which is the only decision
/// in this function and is a statement about BLEND MODES, not a tuning:
///   * the sparks multiply an ALPHA by GhvrTipUpright, and EnvParticleAdd
///     PREMODULATES (c.rgb *= c.a) before blending SrcAlpha One — so their drawn
///     energy already goes as GhvrTipUpright SQUARED. That is stated in the
///     function above and measured in the bake log.
///   * EnvGlow does not premodulate: it returns (colour, alpha) into the same
///     SrcAlpha One, so a halo's drawn energy is LINEAR in its alpha.
/// Feeding a halo GhvrTipUpright would therefore give the wall and the sparks the
/// same-shaped alpha and two different-looking fades; feeding it the square makes
/// every term at the site lose THE SAME FRACTION OF ITS DRAWN ENERGY at every
/// instant of the topple. One function, one relationship, and nothing to tune
/// apart.
///
/// IT THEREFORE LEADS THE SPARKS' ALPHA, and that is the direction to err in: the
/// complaint is a term that OUTLIVES the fire, so a wash that is already half gone
/// while the sparks are at 0.71 is answering the complaint rather than restating
/// it. Both still reach exactly 0 at the same instant — the arrival — and both
/// come back on the identity that runs the arc backwards, because they are the
/// same expression.
///
/// MEASURED, at the shipped TipDeg = 90 (the bake prints this table against the
/// fire seat's own measured distance from the wash it feeds): 1.00 upright,
/// 0.69 at a 15 deg lean, 0.44 at 30, 0.25 at 45, 0.11 at 60 and EXACTLY 0.00 at
/// the arrival. Against the same 26.002 s event that is 1.00 at t = 0, 0.92 at
/// 1.3 s, 0.53 at 3.1 s, 0.12 at 4.2 s and 0.00 at 4.68 s.
float GhvrTipUprightEnergy (GhvrTip tip)
{
    float u = GhvrTipUpright(tip);
    return u * u;
}

/// A CANDLE ON A FALLING SHELF GOES OUT, and comes back when the shelf does.
///
/// The alternative was a flame that burns steadily through a 90-degree topple, a
/// landing and ten seconds face-down, which is not a flame, it is a decal. What
/// is here instead is the more unsettling of the two readings and the cheaper
/// one: the candle gutters as the shelf swings, dies somewhere past a quarter
/// turn, is OUT for the whole of the time the shelf lies on the floor — so the
/// east wall loses its light and the room goes darker on that side while
/// something is lying there — and lights itself again, late, on the way back up.
/// Nothing lights a candle. That is the point.
///
/// PURE, and asymmetric by construction rather than by a second curve: the
/// extinguishing threshold is measured against a tilt that is inflated while the
/// shelf is RISING (vel < 0). Going over, the flame survives to 24 deg and is
/// out by 36; coming back, the inflation holds it dark a few degrees longer.
/// One term, both behaviours, no state.
///
/// RE-MEASURED FOR THE ModBuild 145 CURVE, because the threshold is on ANGLE
/// and the curve that reaches that angle changed underneath it. Every number
/// below is sampled off the shipped curve at maxAngle 88 deg, 26.0 s:
///   * going over, the flame is fully out at 35.8 deg — reached at t = 3.58 s
///     instead of the old 2.98 s. The resharpened fall therefore keeps the
///     candle alive LONGER in wall-clock, not shorter: it dies at the same
///     angle, and the new curve takes longer to get there. The failure mode
///     worth naming, "a much faster arrival snuffs the candle far too early",
///     cannot happen, because nothing in this function reads a rate on the way
///     down — max(-vel, 0) is identically 0 while the shelf is falling.
///   * coming back it relights at 31.7 deg (the 4 deg of hysteresis is the
///     vel term), which under the reversed recovery is 35% of the way through
///     it, t = 19.6 s, with 6.4 s of rise still to go. That is EARLIER in the
///     recovery than the old ease put it (59%, 4.0 s left) and it is earlier on
///     purpose-by-consequence: the reversed curve leaps off the floor and then
///     creeps, so the shelf is already near-upright for most of the recovery.
///     The read is better than the old one, not worse — the candle comes back
///     while the shelf is still visibly leaning, and then you notice the last
///     thirty degrees closing by themselves over six seconds.
/// If that is ever judged too early, the lever is the 0.25 here and NOT the
/// curve; raising it delays the relight without touching the fall.
///
/// Returns 0..1. The flame's brightness, its length AND the light it casts all
/// take this same number — a candle whose pool outlives its flame is worse than
/// the bug this file is fixing.
float GhvrTipFlameLife (GhvrTip tip)
{
    if (tip.live < 0.5) return 1.0;
    float tilt = abs(tip.ang) + 0.25 * max(-tip.vel, 0.0);
    return 1.0 - smoothstep(0.42, 0.62, tilt);
}

/// The RE-IGNITION. A wick that catches again flares before it settles, and it
/// is the one moment in the event a player who has looked away is pulled back.
/// Peaks where the flame is half alive and only while the shelf is standing up
/// again (vel < 0), so the going-out is a plain death and the coming-back is an
/// event. Bounded in [1, 2.4].
///
/// THE ONE THING THE ModBuild 145 CURVE COULD HAVE BROKEN HERE, checked: the
/// landing rebound is a stretch of NEGATIVE velocity in the middle of the fall,
/// so saturate(-vel * 6) does go to 1 there — and it did on the shipped curve
/// too. It cannot produce a flare, because `life` is 0 at 88 degrees of tilt
/// and the term is life * (1 - life). Sampled over the whole fall the flare is
/// 1.000 to three places; its only peak in the event is 2.399 at ph = 0.769,
/// inside the recovery, where it belongs. Both call sites multiply by life as
/// well (EnvFlame's `bright *= life * flare`, EnvGlow's `o.life = life *
/// flare`), so this is belt and braces.
float GhvrTipRelightFlare (GhvrTip tip, float life)
{
    if (tip.live < 0.5) return 1.0;
    return 1.0 + 5.6 * life * (1.0 - life) * saturate(-tip.vel * 6.0);
}

/// THE LIGHT FOLLOWS ITS SOURCE — as far as an object-space baked rig honestly
/// can, which is exactly one slot and no shadows.
///
/// The room's lighting is BAKED per material in object space: three point
/// positions, written once, with no per-frame CPU anywhere (EnvRoom.shader's
/// header — the game scene contributes no usable lights and this bundle ships
/// script-free prefabs). A moving source is therefore not free, and what makes
/// it possible at all is that the shelf candle's motion is the SAME rigid
/// transform the shelf takes: one Rodrigues on one uniform, per material, and
/// the wall the candle used to light is lit from where the candle now is.
///
/// Computed in the VERTEX shader and interpolated, because it is a per-draw
/// constant and evaluating GhvrTipNow() per fragment on every stone in the room
/// would be paying a haunt schedule for a picture that has one candle in it.
///
/// WHAT THIS BUYS: the pool of candlelight travels down the east wall with the
/// candle, sweeps the floor as the shelf comes over, and goes out where the
/// flame goes out — because .w is the same GhvrTipFlameLife the flame itself is
/// drawn with, and the whole term is multiplied by it.
/// WHAT IT DOES NOT BUY, stated plainly rather than hidden: the pool is a point
/// light with no occluder, so the shelf does not shadow it and the candle keeps
/// lighting the wall through the board it is lying on; the two remaining candle
/// slots are unmoved (correctly — they are on the table and the crate); and
/// EnvGlow's halo, which is a real world-space sphere, moves as a whole rather
/// than deforming, which is what a halo is.
///
/// Returns (the ridden slot's object-space position, the flame's life), or w < 0
/// for "nothing is riding right now, do not touch the light at all".
///
/// THE SENTINEL IS THE POINT. This value crosses an interpolator, and a constant
/// carried across a triangle by barycentric weights is not guaranteed to arrive
/// as the same bits — so a "neutral" return of (the light's own position, 1.0)
/// would still perturb the shipped room by an ulp and lose the md5 proof. A
/// negative w is a flag no real life can take, tested with a comparison that a
/// last-bit wobble cannot flip.
float4 GhvrTipLight (GhvrTip tip, float4 l0, float4 l1, float4 l2)
{
    float s = _TipUse.y;
    if (s < -0.5 || tip.live < 0.5) return float4(0, 0, 0, -1);
    float3 p = (s < 0.5) ? l0.xyz : ((s < 1.5) ? l1.xyz : l2.xyz);
    return float4(GhvrTipRot(p, tip.pivot, tip.axis, tip.ang), GhvrTipFlameLife(tip));
}

/// Substitute the ridden slot into a fragment's light, and do NOTHING AT ALL to
/// the other two.
///
/// "Nothing at all" is the specification, not an optimisation: `pos` must come
/// out of this as the same bits _L1Pos went in as, or the room's zero state
/// stops being md5-identical with the shipped one — an interpolated copy of a
/// constant is not the constant. So the substitution is inside the branch, and
/// the branch is a uniform.
void GhvrTipSlot (float4 tipL, float slot, inout float4 pos, inout fixed4 col)
{
    if (tipL.w >= 0.0 && abs(_TipUse.y - slot) < 0.5)
    {
        pos.xyz = tipL.xyz;
        // rgb only: the ALPHA of a light colour is its flicker AMOUNT, and a
        // guttering candle must go dim without going steady.
        col.rgb *= tipL.w;
    }
}

/// THE LAG, as a displacement rather than as a rotation. A flame on a body that
/// is swinging does not turn with the body — it is left behind by it, because
/// the hot gas has momentum the wax does not. The card is already rigidly
/// rotated by the time this is added; what this adds is the trailing.
///
/// The direction is the TANGENTIAL one at the flame's own position, which is
/// what the axis and the arm give for free (v = w x r), so the flame trails
/// along the path it is actually travelling instead of along a guessed axis.
/// Weighted by h*h, so the wick stays welded to the candle and only the plume
/// bends; scaled to about 3 cm at the topple's fastest, which is a flame laid
/// over about 30 degrees.
///
/// THE CONSTANT IS DELIBERATELY UNCHANGED FOR ModBuild 145, and this is the
/// closest call in the file. The resharpened fall raises the peak |vel| the
/// riders see from 0.647 to 1.122 rad/s, so the worst-case lag at a 1.3 m arm
/// goes from 4.6 cm to 8.0 cm. The argument for leaving 0.055 alone rather than
/// scaling it back by 1/1.78:
///   * the term IS velocity-proportional by design. A shelf that really moves
///     faster really does leave its flames further behind, and rescaling the
///     constant to hold the old peak would be asserting the opposite.
///   * the peak is now a SPIKE at the instant of impact rather than the broad
///     plateau the quadratic had, so it is paid for about two tenths of a
///     second and nowhere else. Everywhere the fall is actually watched the new
///     lag is SMALLER than the shipped one: while the candle is still alight
///     (below 36 deg of tilt, i.e. the first 76% of the fall) |vel| never
///     exceeds 0.53 rad/s against the old curve's 0.65.
///   * the only riders awake at the spike are the two SEATED FIRES, which are
///     not candles (_TipUse.z = 0, so they never gutter). A bed of fire on a
///     board that has just slammed into a stone floor splashing sideways for a
///     fifth of a second is the correct picture, not an artefact.
/// If hardware disagrees, 0.055 -> 0.031 restores the shipped peak exactly.
float3 GhvrTipLag (GhvrTip tip, float3 objPos, float h)
{
    if (tip.live < 0.5) return float3(0, 0, 0);
    float3 vel = cross(tip.axis, objPos - tip.pivot) * tip.vel;
    return -vel * (0.055 * h * h);
}

#endif // GHVR_ENV_SHELFTIP_INCLUDED
