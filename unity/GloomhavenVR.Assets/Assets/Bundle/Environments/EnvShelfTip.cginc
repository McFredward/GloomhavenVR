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
// h.live * mine is 0, so `phase` is EXACTLY 0, and GhvrShelfTip(0) is exactly 0
// by inspection (fall = 0, down = 0*0, bounce carries step(0.18, 0) = 0, rise =
// smoothstep(0.62, 1, 0) = 0). The angle is therefore exactly zero and every
// rider skips the rotation on a comparison that is UNIFORM across the draw.
//
// That is not a micro-optimisation, it is the correctness requirement: this
// round's zero state has to be bit-identical with the shipped one, and
// `pivot + (p - pivot)` is NOT p in floating point. A rider that "rotated by
// zero" would move the wax by an ulp and lose the md5 proof.
// ============================================================================
#ifndef GHVR_ENV_SHELFTIP_INCLUDED
#define GHVR_ENV_SHELFTIP_INCLUDED

// The schedule. GhvrHauntAt / GhvrHauntEnvelope decide WHEN, and a rider must
// ask the same oracle the shelf asks or the two answer at different seconds.
#include "EnvHaunt.cginc"

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
                    // z 1 = I am a candle flame and may be blown out by the fall
                    // w  spare (0)

/// The bookshelf's tip-over, as a pure function of the event phase.
///
/// IT FALLS FAST AND GETS UP SLOWLY, which is the whole picture: a shelf goes
/// over in about a second under gravity and then stands itself back up over
/// five, against nothing, which is the part that is wrong. Both halves are
/// shaped curves of `phase` and NOTHING IS INTEGRATED — the schedule is the only
/// thing that decides where the shelf is at time t, so a player who joins
/// mid-event, or an event cut off by the slot ending, cannot leave it lying
/// down.
float GhvrShelfTip (float ph)
{
    // 0.00-0.18  the topple: a quadratic, i.e. constant angular acceleration,
    //            which is what a falling body does
    // 0.18-0.24  the landing, held flat with one small bounce
    // 0.24-0.62  it lies there. This is the part that has to be long enough for
    //            somebody to walk over and look at it.
    // 0.62-1.00  it comes back up, slowly and evenly, and eases into standing so
    //            the last degree is not a snap.
    float fall  = saturate(ph / 0.18);
    float down  = fall * fall;
    float bounce = -0.055 * sin(saturate((ph - 0.18) / 0.06) * GHVR_TIP_PI)
                 * step(0.18, ph) * step(ph, 0.24);
    float rise  = smoothstep(0.62, 1.00, ph);
    return saturate(down - rise) + bounce * step(ph, 0.62);
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
/// out by 36; coming back, the same window is met about 14 deg later, so the
/// relight happens near the end of the nine-second recovery instead of half way
/// through it. One term, both behaviours, no state.
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
/// bends; scaled to about 3 cm at the topple's fastest (0.8 rad/s at a 1.3 m
/// arm), which is a flame laid over about 30 degrees.
float3 GhvrTipLag (GhvrTip tip, float3 objPos, float h)
{
    if (tip.live < 0.5) return float3(0, 0, 0);
    float3 vel = cross(tip.axis, objPos - tip.pivot) * tip.vel;
    return -vel * (0.055 * h * h);
}

#endif // GHVR_ENV_SHELFTIP_INCLUDED
