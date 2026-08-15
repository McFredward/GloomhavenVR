// GloomhavenVR — FIRE REAL. What a fire looks like, and the light it throws,
// as ONE set of numbers used by both halves.
//
// USER VERDICT, hardware, ModBuild 144 (cellar, verbatim): "Das Feuer im Keller
// sieht eher aus wie viele Kerzenflammen statt wirklich ein bedrohliches
// Brennen der Möbel! Überarbeite das Feuer nochmal komplett, es soll realistisch
// und bedrohlich wirken und auch die Lichtverhältnisse entsprechend anpassen."
// And (forest): "Feuer im Wald ist noch nicht implementiert, Teile der Bäume
// sollen brennen!"
//
// ============================================================================
// WHY THE THIRD ATTEMPT IS A DIFFERENT SHAPE OF PROBLEM FROM THE FIRST TWO.
//
// Both earlier rounds built a fire out of MORE CANDLE FLAMES: the same laminar
// teardrop sprite, more of them, bigger, leaning. The renders of ModBuild 144
// show exactly what he described — a row of tall amber spikes with black
// between them, standing on a crate, each one with the smooth closed silhouette
// of a candle. Six of those is six candles, and no amount of tuning the count
// or the size changes the category.
//
// The five things that separate a burning object from a big candle, in the
// order they matter, and where each one lives:
//   1. A BED. A fire's brightest, densest part is a low incandescent mass at
//      the seat, WIDER THAN IT IS TALL, sitting ON the object and not floating
//      above it. Everything else grows out of that. (FireMesh: bed cards, and
//      GHVR_FKIND_BED below, which never surges and never wanders.)
//   2. TONGUES THAT DETACH AND DIE. A candle's flame is attached at all times;
//      a fire throws pieces of itself upward that separate, rise, redden and go
//      out. Nothing reads as "fire" faster, and nothing was in the previous two
//      builds at all. (GHVR_FKIND_PUFF, a card that travels its own cycle.)
//   3. A VERTICAL TEMPERATURE GRADIENT with three stops, not two: white-blue in
//      the seat, orange through the body, dark red where the tongues tear off.
//      (GhvrFireRamp.)
//   4. TURBULENCE AT 3-8 Hz. This is the measurement that condemned the shipped
//      fire outright: its surge ran at 2.9 and 4.7 radians per unit of
//      t * _Rate * _LickRate with _Rate ~ 1.19 and _LickRate 1.15, i.e. 0.63 Hz
//      and 1.03 Hz. A one-hertz sway IS the motion of a candle in a draught. A
//      fire's tongues turn over several times a second.
//   5. IT LIGHTS WHAT IT STANDS ON, brightly and unsteadily. Half of this was
//      already built (the _FirePos/_FireCol/_FireRate receiving term in EnvRoom
//      and EnvGround); nothing wrote it. Both halves are here now.
// ============================================================================
//
// ============================================================================
// THE FLAME AND ITS LIGHT ARE ONE ANIMATION — the standing rule in
// EnvFlame.shader's header ("a flame and the light it casts must share _Rate,
// or they drift apart"), honoured this time by construction rather than by two
// materials being handed the same number.
//
// GhvrFireFlicker() below is called by the FLAME (EnvFlame's bonfire branch,
// for its brightness) and by the LIGHT (GhvrFireSeats, for the wash on the
// stone), with the same clock, the same Hz and the same phase. There is no
// second waveform to keep in step. The frequencies are stated in HERTZ because
// the argument to GhvrWave4 is in cycles — which is also what makes the 3-8 Hz
// requirement above a thing that can be read off the source instead of derived.
//
// NO sin(). GhvrWave4 (EnvGrowth.cginc) is the cubic-smoothed triangle every
// tree shader ships instead of a sine: four independent waves for the price of
// one, bounded in [-1,1] BY CONSTRUCTION — which is what lets `depth` below be
// a depth in the plain sense and lets a fire's peak brightness be a number the
// builder can state rather than measure.
// ============================================================================
//
// ============================================================================
// USER VERDICT, hardware, ModBuild 145 (verbatim):
//   "Das Feuer zappelt viel zu schnell und ist damit nicht sehr immersiv."
//
// THIS IS NOT A REQUEST TO SLOW THE FIRE DOWN, and reading it as one walks
// straight back into "das sind Kerzenflammen", which he has now rejected twice.
// The 4.6 Hz clock is measured and defensible and IS NOT TOUCHED by this round.
// What was wrong is WHICH BAND MOVES WHICH STRUCTURE, and the diagnosis is a
// table rather than an opinion.
//
// THE MEASUREMENT. A buoyant diffusion flame does not have "a" frequency; it
// has a cascade, and the frequency of a structure is set by its SIZE. The pool-
// fire puffing correlation (Cetegen & Ahmed 1993, and it is the one every fire-
// safety text quotes) is
//                        f = 1.5 / sqrt(D)   [Hz, D in metres]
// so a band's frequency names the structure it belongs to, D = (1.5/f)^2:
//
//     band     ratio   at FireHz 4.6    the structure it MOVES
//     w.w      0.235      1.08 Hz       1.9 m   — the whole fire, the swell
//     w.y      0.610      2.81 Hz       29 cm   — ONE TONGUE
//     w.x      1.000      4.60 Hz       11 cm   — a tongue's tip, a bed cell
//     w.z      1.730      7.96 Hz       3.6 cm  — texture detail, a wrinkle
//
// ...and the fires that are actually built (BuildEnvironmentRooms, both rooms)
// are 0.48 to 1.24 m across, i.e. their whole-flame puffing rate is 1.35 to
// 2.17 Hz — the w.w/w.y end of that table, not the w.x/w.z end.
//
// THE FAULT. Before this round the assignment was INVERTED end to end:
//   * the tongue's LENGTH (a 30-60 cm structure) was driven 58 % at 4.6 Hz and
//     42 % at 8 Hz — the two bands that belong to 11 cm and 3.6 cm — and
//     NOTHING drove it at its own 1.4-2.8 Hz. Amplitude-weighted mean 6.0 Hz.
//   * the tongue's whole LATERAL WANDER (h^2, so the entire card) was driven on
//     X by w.z alone: 10.4 cm of sideways travel at 8 Hz, 3.6 m/s rms, 183
//     m/s^2 rms, sixteen direction reversals a second. That single line is the
//     loudest "zappeln" in the shader.
//   * meanwhile the ONE genuinely small-scale term in the whole effect — the
//     fragment's UV wobble, which moves texture detail a few millimetres — ran
//     at 2.1 Hz and 1.16 Hz, the frequencies that belong to a 1-2 m structure.
//   * and 19 % of the brightness — of the FLAME and of the WASH ON THE WALLS —
//     sat at 8 Hz, which is the peak of human temporal contrast sensitivity
//     (de Lange): the one frequency at which a large dim-adapted field is most
//     visibly unsteady. On the cellar's wash (depth 0.45) that is +-8.6 % of
//     the whole pool of light, on the forest's (0.55) +-10.5 %.
//
// THE FIX, in one sentence: every band keeps its frequency and the CLOCK IS
// UNCHANGED; what changes is that each band is now applied to the structure
// whose size it names, and the amplitudes roll off with frequency the way a
// turbulent spectrum does instead of being flat. The fire still turns over four
// to eight times a second in its small parts. Its tongues now take a third of a
// second to grow and fall, as a 30 cm tongue must.
//
// REJECTED, and why:
//   * Lowering FireHz. That is the ModBuild 144 fire again — 1 Hz is a candle
//     in a draught, measured, and it is the thing he rejected first. It also
//     would have slowed the small structures, which were the one part that was
//     right.
//   * Lowering _Lick / _Sway (the amplitudes). Cuts the fire's size and life
//     without touching its nervousness: the same jitter, smaller. Amplitude is
//     not what he complained about — nothing in "zappelt zu schnell" is about
//     how far anything moves.
//   * Damping (an exponential lag on the surge). Costs per-vertex state a
//     vertex shader does not have, and a low-pass of a flat spectrum is just a
//     re-weighting done expensively and without saying so.
// ============================================================================
#ifndef GHVR_ENV_FIRE_INCLUDED
#define GHVR_ENV_FIRE_INCLUDED

// SHELF RIDERS: two of the cellar's six fires are seated ON the bookshelf that
// topples, so a seat is not necessarily a fixed point. The pose comes from the
// one place it exists (EnvShelfTip.cginc) and is applied to the seats that say
// they ride, here, so that no consumer of the wash has to know about furniture.
// Include ORDER is load-bearing: EnvShelfTip pulls in EnvHaunt and with it the
// element channel, which EnvGrowth #errors without.
#include "EnvShelfTip.cginc"
#include "EnvGrowth.cginc"   // GhvrWave4

// ---------------------------------------------------- the card kinds
// Carried per card in the fire mesh's UV1.y (EnvRoomBuilder.FireMesh). The
// numbers are mirrored there; change one, change both.
#define GHVR_FKIND_BED    0.0   // the seat: wide, low, crowded, nearly steady
#define GHVR_FKIND_TONGUE 1.0   // rises out of the bed, surges, leans, tears
#define GHVR_FKIND_PUFF   2.0   // the plume top: rises out of the body, reddens, dies

// ...and how hard a puff COOLS over its own life, as a multiplier on `age`
// where it enters GhvrFireRamp's cooling axis (EnvFlame's fragment).
//
// ModBuild 150, and it is the colour half of "sie schweben und gehören nicht
// dazu". A parcel that has left the luminous zone of a fire is not flame
// -coloured for long — it drops out of incandescence within a few tens of
// centimetres — so the one thing a detached card may not do is stay orange
// while it climbs. The piece now dies at 0.55 of its cycle (EnvFlame's
// envelope), so at the old implicit weight of 1.0 it never got past the middle
// of the ramp; 1.90 x 0.55 = 1.05 lands it on the tip stop, which is a third of
// the body's luminance and almost monochrome red, before it goes out.
//
// It is exactly 0 on bed and tongue cards (their `age` is 0), so this constant
// cannot reach the flame body.
#define GHVR_PUFF_COOL 1.90

// ------------------------------------------------- HOW HIGH A FIRE MAY GO
// USER, hardware, ModBuild 149: "Feuer zieht nun solche Fäden bis ganz weit
// nach oben, das sieht nicht realistisch aus." Everything a fire draws is
// authored as a fraction of its own height, so nothing in the shader ever knew
// what the WHOLE thing added up to — and the terms compose multiplicatively:
// a puff born at 0.55 of the height, rising another 0.95 of it, stretched by
// `tall` (up to 1.55 under Fire) and carrying its own 0.49 of card is at
// 2.9 fire-heights before a single element pairing is up, and Fire+Light's
// smoke term took it past four. On a 1.05 m snag fire that is four metres of
// something in the air over a burning tree, which is what feuer4.jpg shows.
//
// So there is now ONE number that bounds the lot, applied once, in the fire's
// own frame, after every displacement. It is a SOFT knee and not a clamp: a
// clamp would flatten every card that reached it into a straight horizontal
// edge, which is the "stack of quads" artefact this shader has paid for twice
// already. Below GHVR_FIRE_SOFT fire-heights nothing is touched at all (the
// map is the exact identity and its derivative is 1 at the join); above it the
// remaining rise is compressed exponentially toward GHVR_FIRE_CAP, which it
// approaches and never reaches.
//
// 1.05 / 1.75: the tallest tongue of an unstretched fire tops out near 0.8 of
// its height, so the knee is well clear of the flame proper and only detached
// pieces and hard surges ever enter it, and 1.75 fire-heights is 1.8 m over the
// snag and 0.74 m over a log fire — a piece of burning bark going up over a
// bonfire, rather than a thread reaching into the canopy.
#define GHVR_FIRE_SOFT 1.05
#define GHVR_FIRE_CAP  1.75

// ---------------------------------------------------- the light channel
// Written per material, in THAT material's object space, by
// EnvRoomBuilder.ApplyRig — the same door the three candle positions go
// through, for the same reason (EnvRoom.shader's header on object-space
// lighting). Up to three SITES per room, not per fire: the cellar burns in six
// places grouped into three seats, because a crate top and the litter at its
// foot are one fire as far as the wall behind them is concerned.
//
// The defaults are a hard off in three independent ways — a black colour, a
// zero flicker depth and a caller that only enters the term at all when
// e.fire > 0 — so a build in which nothing writes them is bit-identical.
float4 _FirePos0, _FirePos1, _FirePos2;   // xyz seat (OBJECT space), w = 1/range
fixed4 _FireCol;                          // rgb wash colour, a = flicker depth
float _FireRate;                          // Hz — the SAME Hz the flames burn at
float4 _FireRide;                         // xyz: 1 = this seat stands on the shelf

// ============================================================================
// ModBuild 148 ADDS NO CHANNEL, AND THAT IS A CONSTRAINT RATHER THAN A VIRTUE.
//
// The two new quantities this round's light needs — the fire's own SIZE at a
// seat (for the inverse-square core of the wash) and the colour and strength
// of its COALS — would naturally be two more uniforms written by ApplyRig
// beside _FirePos0..2. They are not, because a Unity material can only carry a
// uniform its shader DECLARES IN ITS PROPERTIES BLOCK, the readers of this
// header are EnvRoom.shader and EnvGround.shader, and both of those belong to
// other lanes this round. Adding a property to a file this lane does not own,
// in a round whose entire subject is a fix that was left half-applied across
// two files, is the wrong trade at any price.
//
// So both are DERIVED from channels that already exist, here, once:
//
//   THE CORE RADIUS  = range * GHVR_FIRE_CORE_K. A seat's range is already a
//     per-seat number that the bake sizes from the fire standing there, so the
//     two were never independent; making the dependence explicit costs a
//     constant and removes a channel that could have gone stale. The bake
//     CHECKS it (EnvRoomBuilder measures the real spread of the fires at each
//     site and fails the build if range * K disagrees with it by more than a
//     factor of two), so the constant cannot quietly stop describing the fires.
//
//   THE COAL COLOUR  = _FireCol.rgb * GHVR_GLUT_TINT. Coals are the same fire
//     seen without its flame: the same emitter, redder and dimmer, because what
//     you are looking at is 1000 K carbon rather than 1300 K gas. Deriving it
//     means a room cannot end up with orange flames and yellow embers, which is
//     a mistake two independent colours would eventually make.
// ============================================================================
// 0.26: on the cellar's 2.5-2.8 m seats that is a 0.65-0.73 m core against
// sites 0.7 m across, and on the wood's 3.6-4.2 m seats 0.94-1.09 m against
// sites 1.2-1.4 m across. See the RANGE/CORE line in each room's bake log.
#define GHVR_FIRE_CORE_K 0.26
// ...and the coals: redder than the flame and well under it in green and blue.
// (1.15, 0.55, 0.22) on the cellar's (0.80,0.32,0.10) wash is (0.92,0.18,0.02),
// which is the colour of carbon at about 1000 K and is deliberately nowhere
// near the (1.78,1.32,0.86) the seat of the FLAME is: coals under a fire have
// to be visibly cooler than the fire, or they read as a second fire lying down.
#define GHVR_GLUT_TINT float3(1.15, 0.55, 0.22)
// how bright they are, against the wash.
//
// ---- ModBuild 149: 0.85 -> 1.90, AND THE USER HAS NOW ASKED THREE TIMES -----
// "am Besten darunter eine Glut sichtbar sein, dass es glaubwürdig aussieht"
// (the burning tree) and "auch hier fehlt mir die glut auf dem asset" (the
// cellar shelf). Two rounds have shipped this term and he cannot see it.
//
// IT WAS MEASURED RATHER THAN GUESSED THIS TIME. The forest previews were
// rendered twice, once as shipped and once with this constant at 0, and the
// difference IS the coal term in isolation — there is no other way to separate
// it from the wash, which lands on the same surfaces through the same three
// distances. On the bark of the burning snag, in linear light:
//
//     GHVR_GLUT_K   coals add (r,g,b)          lum      p99/p50 of the term
//     0.85 (ship)   (+0.007,+0.000,+0.000)   +0.002     1.0x  (a flat wash)
//     1.90 (here)   (+0.020,+0.001,+0.000)   +0.005     6.4x  (structure)
//
// The LEVEL is only half of it and was never the half that mattered: at 0.85
// with no crust the term was a smooth orange ellipse on wood, which is what a
// LIGHT SHINING ON WOOD looks like and is a second, weaker copy of the wash
// already there. What makes it read is that p99/p50 — 61x on the deadfall's
// bark, 21x on the crate lid, 6.4x on the vertical trunk — i.e. bright cracks
// between dark lumps rather than an even glow. See GhvrGlutCoals.
//
// The gain is what carries the peak of that structure high enough to be seen
// on a surface the wash has already lit. It is bounded by the same window and
// dies to +0.0000 one and a half metres up the same trunk, measured.
#define GHVR_GLUT_K 1.90
// ...and how far they reach, in core radii. 1.45 is the ModBuild 148 number
// and it is KEPT: the light seats are pushed out of the thing they light (a
// point inside a trunk lights none of it — see AddForestFire's snagSeat), so a
// tighter window than this stops reaching the bark at all. What changed is the
// EXPONENT, below: the reach is the same and the shape inside it is not.
#define GHVR_GLUT_REACH 1.45
// ...and the coals lie in a COLUMN, not in a ball: half the length of that
// column, in core radii, measured along WORLD UP about the seat.
//
// ============ "LASS DEN STAMM AN DIESER STELLE EINFACH GLÜHEN" ==============
// USER, hardware, ModBuild 150, about the fire climbing the burning snag's
// bark. The flame cards that used to stand on that trunk are gone (see
// AddForestFire); what has to replace them is charred wood with heat coming out
// of it, over the height the fire reached — and the coal term could not do that,
// because its window was a SPHERE about a point.
//
// The arithmetic, on the snag as the bake really builds it. The seat has to
// stand OUT of the trunk or N.L is negative on every triangle of it
// (AddForestFire's snagSeat), so it sits 0.40 m clear of the bark and 0.55 m
// above the litter, and the coal window is 1.45 x 1.09 = 1.58 m. gq^5 down the
// bark, against height above the ground:
//
//     y            0.00   0.55   1.00   1.50   2.00
//     sphere       0.361  0.719  0.458  0.064  0.000
//     capsule      0.719  0.719  0.719  0.502  0.085
//
// The sphere's whole usable band was the half metre either side of the seat: a
// bright patch at knee height with the trunk dark above it, which is a LIGHT
// SHINING ON WOOD and is the second copy of the wash this term was condemned for
// being in ModBuild 149.
//
// A fire is a vertical structure and so is the char it leaves. So the coal
// distance is now measured to a SEGMENT — the seat, extended GHVR_GLUT_COL core
// radii up and down along world up — instead of to the seat point. On the snag
// that is +-0.55 m, so the window is a capsule that holds the bark at full
// strength from the litter to 1.10 m, is still at 70 % of that at 1.50 m and is
// out by 2.4 m. The root flare KEEPS its coals — it GAINS them, because the
// segment reaches down as well, which is why it is symmetric — and the column
// above it is what the user asked for: brightest where the fire is, fading up
// the trunk, and on the mesh's own surface at every pixel.
//
// 0.50 AND NOT MORE, and the cellar is the bound. The same capsule applies to
// every seat in both rooms, in proportion to that seat's own core: 0.34 m on the
// crate stack, 0.40 m on the casks, 0.33 m on the bookshelf. That is char
// running a hand's breadth further up the masonry behind a burning crate, which
// is what a burning crate does to a wall — at 1.0 the crate's capsule plus its
// 0.98 m window would have reached the cellar's ceiling, which is a chimney and
// not a fire.
//
// The WASH is deliberately NOT a capsule: it is a point light, its
// inverse-square core is tuned as one, and stretching it would put a fire's pool
// on surfaces it does not reach. Only `gq` sees the segment.
#define GHVR_GLUT_COL 0.50
// The size of a lump of charcoal, in cells per metre. 1/22 m = 4.5 cm, which
// is a piece of a burnt board; the second octave at 3.1x is the 1.5 cm fissure
// between two of them. See GhvrGlutCoals.
#define GHVR_GLUT_CELL 22.0

// ============================================================================
// THE FIVE PAIRINGS THAT INVOLVE FIRE.
//
// USER REQUEST: "Schau dir auch jede mögliche Kombination der Elemente an und
// schau das jede der Effekte in beiden Umgebungen entsprechend sinnvoll
// miteinander interagiert. So zB das das Feuer der brennenden Bäume noch mehr
// Glut wirft und flackert wenn Wind an ist etc."
//
// THE COMPOSITION RULE, and it is the whole of why fifteen pairs do not become
// an unreadable mess:
//
//   A PAIR TERM MAY ONLY MODULATE A PARAMETER ONE OF ITS TWO ELEMENTS ALREADY
//   OWNS. It may never add a visual layer of its own.
//
// So Fire+Air does not draw "wind-blown fire" on top of a fire; it turns up the
// numbers the fire and the draught already have — the flicker rate, the tear,
// how far a detached piece travels along _GustDir. Fire+Air+Dark is then ONE
// fire that is windblown and alone, because both terms landed on the same
// dozen parameters, rather than three effects stacked on one another. It also
// bounds the cost exactly: five pairings add no draw call, no material, no
// vertex and no texture read.
//
// EVERY TERM IS A PRODUCT OF THE TWO STRENGTHS, so it is exactly zero the
// moment either element is down — which is what keeps the zero state and the
// six single-element states the ones that were tuned. There is no "pair mode"
// to be in: at Fire 1 / Air 0 every `p.air` below is 0.0 and the arithmetic is
// the single-element arithmetic.
//
// WHAT EACH PAIR MEANS, and where its half of the work is:
//   FIRE+AIR   (his own example) more glut, thrown further and downwind, and a
//              harder faster flicker — on the FLAME and on the light it casts
//              at once, because they share a rate by construction.
//   FIRE+DARK  the fire becomes the only light there is. The wash gains reach
//              and strength as the moon is crushed, so the cellar is read
//              entirely by firelight. Nearly free and the strongest of the five.
//   FIRE+LIGHT SMOKE, and only here. Smoke is invisible unless something lights
//              it, so a swollen moon is exactly the condition under which a
//              burning object should show a plume — and it is what stops a fire
//              merely looking weaker next to a brighter moon, which would read
//              as a bug rather than as a choice.
//   FIRE+ICE   STEAM where they meet: the detached pieces go white, low and
//              slow at the seat instead of red and high, and the flame's own
//              tips cool sooner because something is taking heat out of them.
//   FIRE+EARTH it SMOULDERS. Wet growth on burning wood does not flame cleanly:
//              shorter, redder, duller tongues and more of the energy in the
//              glowing bed. More glow than tongue.
// ============================================================================

/// The five products, computed once. Fields are named for the OTHER element;
/// every one of them is e.fire * e.<that>, so `p.air` reads as "how much
/// Fire-and-Air there is".
///
/// DELIBERATELY NOT IN EnvElement.cginc. That header is another lane's this
/// round (it is taking the ten pairs that do not involve Fire, under a
/// `GhvrPair*` prefix), and the last time two lanes independently named the
/// same thing in a shared header it compiled everywhere except in the one
/// shader that wanted both — the accident is written up in EnvHaunt.cginc's
/// own header. `GhvrFirePair` lives here, in the file that owns the flames.
struct GhvrFirePair
{
    float air, dark, light, ice, earth;
};

GhvrFirePair GhvrFirePairs (GhvrElem e)
{
    GhvrFirePair p;
    float f = saturate(e.fire);
    p.air   = f * saturate(e.air);
    p.dark  = f * saturate(e.dark);
    p.light = f * saturate(e.light);
    p.ice   = f * saturate(e.ice);
    p.earth = f * saturate(e.earth);
    return p;
}

/// FIRE+AIR, on the rate — AND IT IS NOW THE IDENTITY. Read the block below
/// before restoring the term; it is a standing project rule, not a taste.
///
/// ======== AN ELEMENT STRENGTH MULTIPLIES AN AMPLITUDE, NEVER A FREQUENCY ====
/// USER VERDICT, ModBuild 145: "Das Feuer zappelt viel zu schnell und ist damit
/// nicht sehr immersiv." That round re-pointed every band in this file at the
/// SIZE of structure whose frequency it really is (the table above), measured
/// the mean frequency down from 4.26 Hz to 3.13 Hz, and the user accepted it.
///
/// This function then multiplied that same clock by 1.55 the moment Air came
/// up: 4.6 Hz -> 7.13 Hz on the flame, on the erosion scroll and on the wash on
/// the ground, i.e. a windy fire went back to being FASTER than the fire the
/// user had already rejected as too fast, and the mean frequency with it
/// (3.13 -> 4.85 Hz). Every hour spent on ModBuild 145 was undone by one
/// element. That is exactly the failure the rule exists to prevent, and it is
/// why the rule is absolute rather than a preference: a frequency is a property
/// of the SIZE of the thing moving, and an element does not change the size of
/// a flame's eddies — it changes how hard they are fed.
///
/// So the rate is now flat and the whole of Fire+Air's energy is in AMPLITUDE:
/// the depth below (harder), the lean (EnvFlame, harder), the outward tear
/// (EnvFlame, unchanged) and the embers it sheds (EnvFlame, more of them and
/// alive for longer). The user's own example — "das Feuer der brennenden Bäume
/// noch mehr Glut wirft und flackert wenn Wind an ist" — is a request for MORE
/// GLUT and MORE FLICKER, and both of those are amplitudes. Neither of them is
/// a request for a faster fire.
///
/// ======== ...AND IT WAS ALSO A PHASE SCRUB, WHICH IS THE HARDER BUG ========
/// The rule above is about the LOOK. There is a second, purely mechanical fault
/// in the same expression, it is the one the elements lane found in GhvrWind and
/// EnvBeam, and it is why "just tune the coefficient down" would not have been
/// enough.
///
/// Every consumer evaluates the wave at `t * GhvrFireHz(...)`, and `t` is the
/// SHARED ENVIRONMENT CLOCK — it reaches thousands of seconds in a session. So
/// the air term did not scale a rate, it scaled a PHASE: during the one-second
/// element ramp (ElementMood.RampSeconds = 1.0) the argument moved by
/// 0.55 * _FireHz * t cycles, which at t = 3600 s is nine thousand periods
/// inside that one second. The waveform is swept through hundreds of cycles per
/// frame and the fire — and the wash on the ground, and the glut, which take
/// the same `hz` through GhvrFireSeatOne — flash white-black for the length of
/// the ramp. The user reported the vegetation half of this as "zucken die Bäume
/// extrem unnatürlich ... für ca 1s"; the fire had the identical fault and he
/// did not separate them.
///
/// MEASURED — worst per-frame step in the wash's brightness at 90 Hz across the
/// ramp, forest _Flicker 0.55, against the step the same fire has while Air is
/// simply HELD at 1.0 (i.e. the motion he has already seen and accepted):
///
///     clock t0     shipped     lerp fix    THIS (flat)   held at air=1
///        0 s        0.308       0.216        0.153           0.273
///       30 s        1.021       0.185        0.161           0.302
///      600 s        0.599       0.195        0.164           0.320
///     1800 s        0.515       0.195        0.165           0.295
///     3600 s        0.902       0.265        0.164           0.291
///
/// The shipped column is 3.4x the accepted motion at t = 30 s and depends on
/// the wall clock; note that it is very nearly CORRECT AT t = 0, which is the
/// signature that identifies this bug and the reason it survives every test
/// that starts a fresh scene.
///
/// The elements lane's fix — lerp(wave(hz), wave(1.55*hz), air) — is sound and
/// is what GhvrWind and EnvBeam use, because those effects genuinely want a
/// rate response and only need its phase made continuous. THIS function does
/// not want one at all (see the rule above), so it takes the stronger form:
/// with no air in the argument there is no phase to scrub, the ramp costs less
/// than the held state at every offset, and the numbers do not move with the
/// clock at all. Both fixes are correct; this one is also the shape the rule
/// asks for, and it is the only one of the three whose column is flat.
///
/// The signature is unchanged on purpose. This is still the ONE function the
/// flame, the erosion scroll and the wash all take their rate through — so
/// EnvRoom.shader and EnvGround.shader are fixed by this line without being
/// touched, the standing "a flame and the light it casts share a rate" rule
/// keeps its single enforcement point, and a future round that wants a rate
/// response has one line to change and this block to answer first. If it ever
/// does come back, it comes back as the lerp and never as a factor on `t`.
float GhvrFireHz (GhvrFirePair p, float hz)
{
    return hz;
}

/// ...and on the depth, which is where Fire+Air's rate response has gone. Wind
/// -fed fire is unsteady in AMPLITUDE — that is the half that reads across a
/// room, and after the block above it is the only half there is. Bounded,
/// because GhvrWave4 is: at full Fire+Air the pool swings +-88% instead of
/// +-45% (it was +-75% while the rate was also moving).
float GhvrFireDepth (GhvrFirePair p, float depth)
{
    return depth * (1.0 + 0.95 * p.air);
}

/// THE FOUR BANDS OF ONE FIRE, from one wave call. `cycles` is the fire's clock
/// already in CYCLES (t * hz); `phase` decorrelates one fire from another, or
/// one card from another inside a fire.
///
/// Returned as the raw four so that a CONSUMER can choose which band drives
/// which structure — which is the whole content of the ModBuild 145 fix above,
/// and the reason this is a function at all. EnvFlame's vertex shader used to
/// carry a byte-for-byte copy of these four arguments with a comment saying the
/// two had to be kept in step by hand; the copy is gone and the band ratios now
/// exist once, here, next to the table that says what each of them means.
///
/// The ratios themselves are UNCHANGED and deliberately so: 1 / 0.61 / 1.73 /
/// 0.235 are mutually irrational enough that the four never re-phase, which is
/// what stops a fire having a visible period. It is only their weights and
/// their targets that this round touches.
float4 GhvrFireBands (float cycles, float phase)
{
    return GhvrWave4(float4(cycles          + phase,
                            cycles * 0.61   + phase * 1.7 + 0.31,
                            cycles * 1.73   + phase * 0.4 + 0.67,
                            cycles * 0.235  + phase * 0.9 + 0.13));
}

/// The brightness of a fire at time t, in [1-depth, 1+depth].
///
/// Four bands: the base rate, a slower one under it, a fast one over it and a
/// slow breath. At the shipped 4.6 Hz that is 4.6 / 2.8 / 8.0 / 1.1 Hz. `phase`
/// decorrelates two fires in one room; `hz` is shared between a flame and its
/// light and is the whole of the rule this function exists for.
///
/// THE WEIGHTS — ModBuild 145, "das Feuer zappelt viel zu schnell". Both the
/// physics and the eye say the same thing about the old 0.40/0.27/0.19/0.14:
///
///   * A flame's RADIANCE spectrum is not flat. It peaks at the puffing rate
///     (1.4-2.2 Hz for these fires, see the table at the top) and rolls off
///     above it; the old weighting put its peak at 4.6 Hz and gave 8 Hz nearly
///     as much as the 1.1 Hz swell, which is a spectrum no fire has.
///   * 8 Hz is the WORST POSSIBLE PLACE to spend brightness. Human temporal
///     contrast sensitivity peaks near 8-10 Hz for a large field at low mean
///     luminance (de Lange), and this waveform drives the WASH ON THE STONE —
///     the largest, softest, dimmest field in the room. 19 % of a 0.45 depth is
///     +-8.6 % of the whole pool of light, modulated at exactly the frequency
///     the visual system is built to notice. That is "zappeln" in one number.
///
/// New: 0.26 / 0.34 / 0.08 / 0.32, still summing to 1 so `depth` stays exact
/// and every bake value is untouched. Measured over 200 s at 4 kHz:
///     mean frequency        4.26 Hz -> 3.13 Hz
///     power above 6 Hz      12.5 %  ->  2.2 %
///     power below 3 Hz      32.0 %  -> 74.6 %
///     |d(brightness)/dt|    604 %/s -> 408 %/s
///     direction reversals   11.5/s  ->  9.7/s
/// The peak-to-peak swing is IDENTICAL (+-54.6 % at _Flicker 0.55): the fire is
/// exactly as unsteady as it was, and it is unsteady on the timescale of a fire
/// rather than on the timescale of a wasp.
float GhvrFireFlicker (float t, float hz, float phase, float depth)
{
    float4 w = GhvrFireBands(t * hz, phase);
    // the weights sum to 1, so |f| <= 1 and `depth` is exact
    float f = 0.26 * w.x + 0.34 * w.y + 0.08 * w.z + 0.32 * w.w;
    return 1.0 + depth * f;
}

/// THE GLUT — how bright the coals are at time t, in [0,1].
///
/// USER, hardware, ModBuild 147, and it is his own word and the best idea in
/// the message: "zB muss ja auch eine Glut beim Holz sein wo es brennt damit
/// es immersiv wirkt etc."
///
/// He is right, and the omission is the reason the ModBuild 147 fire reads as
/// stuck ON the log rather than as consuming it: in feuer1.jpg the deadfall is
/// the same cold blue-grey UNDER the flames as it is a metre away. Wood that is
/// alight has incandescent coals — a dull orange-red glow in the surface
/// itself, brightest where the flames stand, breathing slowly, and STILL THERE
/// for a while after a tongue has moved on. That is a surface term, not a
/// particle system, and it costs one more windowed distance per seat in a
/// fragment that is already computing three of them.
///
/// TWO THINGS SEPARATE THIS WAVEFORM FROM THE FLAME'S, and both are physics:
///
///  1. IT IS SLOW. A coal is a lump of carbon with thermal mass; it cannot
///     follow a 4.6 Hz eddy in the gas above it and it does not try to. The
///     weights below put 74 % of the power on the 1.08 Hz swell and the 2.81 Hz
///     puffing band and nothing at all on the 7.96 Hz one. Mean frequency
///     1.62 Hz against the flame's 3.13.
///  2. IT LINGERS. "REMAINING briefly where fire has passed" is the whole point
///     and a fragment shader has no state to remember with — so the memory is
///     put in the CURVE instead of in a variable. `pow(x, 0.45)` on a [0,1]
///     envelope is a monotone bias toward the top: the coals reach their peak
///     with the flame and then come down slowly, spending 66 % of every cycle
///     above their own mean where a symmetric wave would spend 50 %. Rise time
///     0.19 s, fall time 0.43 s, measured over 200 s at 4 kHz. That asymmetry
///     IS the thermal lag, exactly, and it costs one pow instead of a texture
///     of history.
///
/// The clock, the Hz and the phase are the fire's own (the standing rule), so
/// the coals cannot drift out of step with the flames standing in them.
float GhvrGlutBreath (float t, float hz, float phase)
{
    float4 w = GhvrFireBands(t * hz, phase);
    // 0.44 swell + 0.30 puffing + 0.26 base, nothing above 5 Hz. Sums to 1,
    // so x lands in [0,1] exactly and the pow below is well defined.
    float f = 0.44 * w.w + 0.30 * w.y + 0.26 * w.x;
    float x = 0.5 + 0.5 * f;
    // the thermal lag, as a curve. 0.45 and not 0.5: at 0.5 the bias is a
    // sqrt and the coals never fall far enough to make the breathing visible;
    // below about 0.35 they stop breathing at all and read as painted-on.
    return pow(max(x, 1e-4), 0.45);
}

/// ============================= THE COALS HAVE A SHAPE =======================
/// USER, hardware, ModBuild 149, TWICE IN ONE MESSAGE: "am Besten darunter eine
/// Glut sichtbar sein, dass es glaubwürdig aussieht" (the burning tree) and
/// "auch hier fehlt mir die glut auf dem asset" (the cellar's shelf). The term
/// has shipped for two rounds and he has asked for it three times.
///
/// The reason it does not read is NOT that it is absent — it is measurable in
/// the ModBuild 149 previews — it is that it is a SMOOTH ORANGE WASH. It is a
/// windowed distance times a wrapped Lambert times a slow breath, and every one
/// of those three is a low-order function of position, so what lands on the
/// bark is a soft ellipse of orange with no edge and no grain anywhere in it.
/// The eye reads a soft orange ellipse on wood as A LIGHT SHINING ON WOOD,
/// which is precisely what the wash beside it already is; two copies of the
/// same cue add nothing, and the second one is invisible BECAUSE it is a copy.
///
/// Coals do not look like that. Charred wood is a CRUST — nearly black, matte,
/// and broken into pieces a few centimetres across — and the light comes out of
/// the FISSURES BETWEEN the pieces, where the incandescent interior is exposed.
/// The contrast across one of those fissures is enormous (a bright crack beside
/// a black lump), and it is the contrast, not the mean level, that says "this
/// wood is being consumed" rather than "this wood is lit".
///
/// So the glut is multiplied by a field with that structure: a two-octave value
/// noise on the OBJECT position, thresholded hard so most of the area is dark
/// crust and a minority of it is open crack. Object space and not world space
/// on purpose — the pattern then belongs to the burning prop, so a bookshelf
/// that topples takes its coals over with it and they do not swim across the
/// board (the shelf riders' whole problem, one file over).
///
/// COST, stated because this is one of the heaviest fragments in either room:
/// eight hashes and a trilinear blend, plus one more hash for the fine octave.
/// It is inside a branch on the accumulated glut, which is zero over all but
/// the few square metres of surface actually near something burning, and that
/// branch is spatially coherent — whole tiles of the screen take it or do not.
/// With Fire down `glut` is exactly 0 and not one instruction of this runs.
float GhvrGlutHash (float3 c)
{
    // sin-free (Inigo Quilez's integer-ish frac hash). A sin-based hash costs
    // the same and has a PERIOD, and object coordinates on a 13 m trunk are
    // exactly large enough to find it.
    float3 p = frac(c * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float GhvrGlutNoise (float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);                     // smoothstep, C1 at the cell walls
    float2 e = float2(0.0, 1.0);
    return lerp(lerp(lerp(GhvrGlutHash(i + e.xxx), GhvrGlutHash(i + e.yxx), f.x),
                     lerp(GhvrGlutHash(i + e.xyx), GhvrGlutHash(i + e.yyx), f.x), f.y),
                lerp(lerp(GhvrGlutHash(i + e.xxy), GhvrGlutHash(i + e.yxy), f.x),
                     lerp(GhvrGlutHash(i + e.xyy), GhvrGlutHash(i + e.yyy), f.x), f.y), f.z);
}

/// The crust, as a multiplier on the glut. Mean ~0.95 over a surface, so this
/// redistributes the coal energy rather than adding any: the burnt patch keeps
/// the brightness the window gives it and stops being FLAT.
float GhvrGlutCoals (float3 opos)
{
    float v = GhvrGlutNoise(opos * GHVR_GLUT_CELL);
    // ...and a finer one, uninterpolated in feel because it is three times the
    // frequency: this is the char cracking inside one lump, not a second lump.
    float f = GhvrGlutNoise(opos * (GHVR_GLUT_CELL * 3.1) + 11.3);
    float n = 0.68 * v + 0.32 * f;
    // THE THRESHOLD IS THE WHOLE EFFECT. Trilinear value noise is bell-shaped
    // about 0.5 with a standard deviation near 0.16, so a knee from 0.40 to
    // 0.72 leaves roughly two thirds of the area on the dark crust and opens
    // the top third into fissures. 0.20 is the crust's own floor — charcoal
    // that has just been turned is not black, and a hard 0 there would make the
    // burnt patch read as a hole rather than as a surface.
    return 0.20 + 2.05 * smoothstep(0.40, 0.72, n);
}

/// THE TEMPERATURE RAMP, three stops. `h` is the height up the card, 0 at the
/// seat; `age` is how far a detached puff is through its life (0 for anything
/// still attached), which reddens it further because a piece of fire that has
/// left the fire is cooling.
///
/// WHY THREE AND NOT TWO. The shipped ramp went core -> tip in one lerp, so the
/// hottest thing in the frame was the same amber as the coldest; there was no
/// white in the picture at all, and a fire with no white in it is a light
/// source painted the colour of fire — which is the note the ModBuild 142
/// verdict already made about the round before that one. The base stop is
/// deliberately blue-white rather than white: the blue is only ever a few per
/// cent of the pixel, and it is the single cue that says "this is hot" rather
/// than "this is bright".
float3 GhvrFireRamp (float3 baseCol, float3 bodyCol, float3 tipCol, float h, float age)
{
    float k = saturate(h * 1.15 + age * 0.55);
    // two lerps with the knee at 0.22: the white-hot seat is a THIN band, as it
    // is in a real fire, and the body owns most of the height. 0.22 and not the
    // first bake's 0.34 — at 0.34 the lower third of every tongue was white and
    // the fire read as pale rather than as hot, which is a different way of
    // getting the same "not a fire" answer.
    float3 c = lerp(baseCol, bodyCol, saturate(k / 0.22));
    return lerp(c, tipCol, saturate((k - 0.22) / 0.78));
}

/// The receiving half: up to three seated fires lighting an object-space point.
///
/// ONE IMPLEMENTATION. EnvRoom and EnvGround each carried their own copy of
/// this (EnvGround's said so: "Copied rather than #included ... the bundle ships
/// shaders and nothing else"), and the moment the flicker had to agree with the
/// FLAME as well the two copies became three places to keep in step. A .cginc
/// ships inside the shaders that include it, so nothing about the bundle
/// changes; what changes is that there is one set of numbers.
///
/// ============================================================================
/// THE FALLOFF, AND WHY IT CHANGED — ModBuild 148.
///
/// USER, hardware, verbatim about feuer1.jpg: the fire "sitzt nicht direkt auf
/// den assets", and the picture shows why the LIGHT says the same thing as the
/// geometry: several square metres of forest floor are evenly reddened out to
/// the edge of the frame, at a brightness that barely changes across the whole
/// pool. That is what a coloured AMBIENT looks like. Firelight does not look
/// like that; it picks out what is close to it and leaves everything else
/// alone, and the difference is entirely in the shape of the falloff.
///
/// The shipped window was (1 - (d/r)^2)^2 — PointLight's, and a good window,
/// but a window is not a falloff. It is 0.98 at a tenth of the range and 0.56
/// at half of it: nearly FLAT over the inner half and then a soft shoulder. A
/// real source falls as 1/d^2 from the moment you leave it.
///
/// So the term is now an inverse-square CORE inside the same window:
///
///     att = 1 / (1 + (d/c)^2)  x  (1 - (d/r)^2)^2
///
/// with c the fire's own radius (_FireCore, written per seat from the fire that
/// really stands there — a 1.16 m burning spill has a bigger core than a 0.48 m
/// bung fire, and now says so). The window still takes it to exactly zero at r,
/// which is what keeps a fire from lighting the far wall at all; the core is
/// what makes it fall off where a fire falls off. Against the old shape, at the
/// forest's c/r of about 0.3:
///
///     d/r      0.10   0.25   0.50   0.75
///     old      0.980  0.879  0.563  0.191
///     new      0.887  0.469  0.146  0.028
///
/// i.e. the surface a fire is STANDING ON keeps ~90 % of what it had and the
/// metre-wide flat wash is down by three quarters. The bake's gain compensates
/// the first number and deliberately does not compensate the rest.
///
/// Still deliberately WITHOUT PointLight's near-field _PtHard divisor: that
/// divisor exists to shrink a candle's pool to its own table (user ruling,
/// ModBuild 134) and would shrink a bonfire to the size of a candle with it.
/// The core radius above is the honest version of the same idea — it is a
/// measurement of the fire rather than a constant.
///
/// The three seats flicker at hz, 0.83*hz and 1.19*hz, so two fires in one room
/// never pulse as a pair — the same three incommensurate factors the three
/// candle slots use, for the same reason.
///
/// ============================================================================
/// ...AND THE GLUT RIDES IN THE SAME THREE DISTANCES. See GhvrGlutBreath for
/// what it is and why its waveform is not the flame's. What it is HERE is the
/// cheapest possible spelling of it: the coals are a second, much tighter
/// window on distances this function has already computed, with
///
///   * NO Lambert term, but a WIDE WRAP. A coal is not a reflector, it emits —
///     so a surface facing away from the seat is not black, it is dim. The wrap
///     is (N.L + 0.55) / 1.55, the standard subsurface wrap, and it is the
///     right shape for embers bedded IN wood: the log glows on top where they
///     lie, and its flank still carries a little because the wood between is
///     hot through. A hard N.L here put a crisp terminator across a burning log
///     in the first preview, which is a thing lit by a lamp, not a thing on
///     fire.
///   * A RANGE THAT IS THE FIRE'S OWN SIZE, times 1.45. Coals lie where the
///     fire has been, which is a little wider than where it is; beyond that
///     there is nothing burning and nothing may glow. On the deadfall that is
///     ~50 cm of glowing bark under a 34 cm fire, and the wood a metre along
///     the log is cold — which is the contrast that says something is being
///     CONSUMED rather than lit.
///   * NO albedo dependence and NO pairing terms except the two that are about
///     heat. Fire+Earth (it smoulders) makes the coals the MAIN event — more
///     glut, less flame, which is the pairing's whole content — and Fire+Ice
///     puts them out a little. Fire+Air fans them.
/// ONE SEAT: its share of the wash (returned) and its share of the coals
/// (accumulated into `glut`). `core` is 1/coreRadius in object units, `hzm` the
/// seat's incommensurate rate factor, `ph` its phase.
float GhvrFireSeatOne (float3 seat, float invRange,
                       float3 opos, float3 N, float t, float hz, float hzm,
                       float ph, float dep, float reach, inout float glut)
{
    float3 lv = seat - opos;
    float d2 = max(dot(lv, lv), 1e-8);
    float3 L = lv * rsqrt(d2);
    float rw = invRange * reach;
    float x = saturate(1.0 - d2 * rw * rw);          // the window, to zero at r
    // 1/coreRadius, in the same object units invRange is already in — which is
    // the whole reason the core is derived from the range rather than carried
    // separately: ApplyRig's per-material scale is baked into `invRange` and a
    // second channel would have needed the same conversion applied by hand.
    float ic = invRange / GHVR_FIRE_CORE_K;
    float c2 = ic * ic;
    float wash = x * x * saturate(dot(N, L)) / (1.0 + d2 * c2)
               * GhvrFireFlicker(t, hz * hzm, ph, dep);
    // THE COALS. A far tighter window (GHVR_GLUT_REACH core radii, raised to a
    // high power for a hard rim — coals do not have a soft edge, the burnt
    // patch simply ends), a wrapped Lambert instead of a clamped one, and the
    // slow biased breath.
    //
    // ---- ModBuild 149: THE EXPONENT GOES 3 -> 5, THE REACH DOES NOT MOVE ----
    // "brightest right under the flame and dying out within a hand's width" is
    // what a coal bed does, and a cube over 1.45 core radii is not it: on the
    // forest's 1.09 m core that window is 1.58 m, and gq^3 still carries 43 %
    // of its peak at 0.78 m out — half a metre of forest floor at nearly half
    // brightness, which is the same "evenly reddened to the edge of the frame"
    // read the WASH was condemned for in ModBuild 148 and fixed with an
    // inverse-square core. The coals kept the fault the wash lost.
    //
    // gq^5 over the SAME window: 0.43 -> 0.19 at 0.78 m, 0.14 -> 0.04 at
    // 1.09 m, and 0.68 -> 0.35 at half a core radius. The reach is deliberately
    // unchanged — a light seat is pushed OUT of the thing it lights (a point
    // inside a trunk lights none of it, see AddForestFire's snagSeat), so the
    // bark this term exists to redden is 0.6-0.8 m from the seat and a window
    // tighter than 1.45 would stop reaching it at all. Tightening the SHAPE
    // inside a fixed window is the only move that is available here, and
    // GHVR_GLUT_K carries the peak back up to where it can be seen.
    //
    // ---- ModBuild 150: THE WINDOW IS A CAPSULE, NOT A BALL ------------------
    // See GHVR_GLUT_COL. The distance is taken to a vertical SEGMENT through the
    // seat rather than to the seat, so the char climbs the standing surface the
    // fire is against instead of dying half a metre up it. `upO` is world up in
    // THIS material's object space — three dot products, and it has to be
    // derived rather than assumed, because a prop that was placed with a tilt
    // (or a bookshelf mid-topple) has an object +Y that is not up and would put
    // the column across the wood instead of along it.
    float3 upO = normalize(mul((float3x3)unity_WorldToObject, float3(0.0, 1.0, 0.0)));
    // half the column, in the same object units invRange is already in
    float hc = GHVR_GLUT_COL * GHVR_FIRE_CORE_K / max(invRange, 1e-4);
    // the nearest point of the segment {seat + upO*u : |u| <= hc}
    float3 gv = lv + upO * clamp(-dot(lv, upO), -hc, hc);
    float gd2 = max(dot(gv, gv), 1e-8);
    float gq = saturate(1.0 - gd2 * c2 * (1.0 / (GHVR_GLUT_REACH * GHVR_GLUT_REACH)));
    float gq2 = gq * gq;
    glut += gq2 * gq2 * gq
            * saturate((dot(N, L) + 0.55) * (1.0 / 1.55))
            * GhvrGlutBreath(t, hz * hzm, ph);
    return wash;
}

float3 GhvrFireSeats (float3 opos, float3 N, float t, GhvrTip tip, GhvrElem e)
{
    GhvrFirePair p = GhvrFirePairs(e);
    // ---- FIRE+DARK: the fire becomes the only light in the room -------------
    // The moon is being crushed (GhvrMoonLight) and the candles are ruled
    // untouchable, so with Dark up the ONLY thing left that can show the player
    // a wall is whatever is burning against it. Both halves of a point light
    // are pushed: the strength, and the REACH — a fire in a black room throws
    // further than a fire in a lit one does, because there is nothing else
    // filling in the falloff. 1/(1+0.55) at full Dark is a range half again as
    // long, which takes the cellar's three seats from lighting their own
    // corners to lighting the room between them.
    float reach = 1.0 / (1.0 + 0.55 * p.dark);
    float gain = 1.0 + 1.60 * p.dark;
    // ---- FIRE+EARTH: it smoulders, so the light it throws goes red and low --
    // A smouldering fire is not dimmer everywhere, it is redder: the green and
    // blue come out of the wash and the red stays.
    float3 col = _FireCol.rgb * float3(1.0, 1.0 - 0.45 * p.earth, 1.0 - 0.70 * p.earth);
    gain *= 1.0 - 0.30 * p.earth;
    // ---- FIRE+ICE: steam and a colder edge ---------------------------------
    // Something is taking heat out of this fire. The wash keeps its strength —
    // an ice storm does not put out a burning cask — and loses a little of its
    // red bias, which is what a fire behind a curtain of steam looks like.
    col = lerp(col, col * float3(0.86, 0.94, 1.12), saturate(p.ice));

    float3 s0 = _FirePos0.xyz, s1 = _FirePos1.xyz, s2 = _FirePos2.xyz;
    // ...and a seat that is standing on the bookshelf goes over with it. One
    // uniform compare when the shelf is upright, which is always except for
    // 26 seconds of a haunt slot.
    if (tip.live > 0.5)
    {
        if (_FireRide.x > 0.5) s0 = GhvrTipRot(s0, tip.pivot, tip.axis, tip.ang);
        if (_FireRide.y > 0.5) s1 = GhvrTipRot(s1, tip.pivot, tip.axis, tip.ang);
        if (_FireRide.z > 0.5) s2 = GhvrTipRot(s2, tip.pivot, tip.axis, tip.ang);
    }
    // ---- THE GLUT's colour, and the two pairings that are about heat -------
    // Fire+Earth: a smouldering fire puts its energy into the bed rather than
    // into the flame, so the coals get BRIGHTER while (in EnvFlame) the tongues
    // get shorter — one parameter each, both already owned, no new layer.
    // Fire+Ice: something is quenching them.
    // Fire+Air: a draught over a coal bed is a bellows, and this is the
    // cheapest half of his own "noch mehr Glut".
    float3 glutCol = _FireCol.rgb * GHVR_GLUT_TINT
                     * (GHVR_GLUT_K * (1.0 + 0.85 * p.earth + 0.40 * p.air
                                           - 0.45 * p.ice));
    float glut = 0.0;
    // FIRE+AIR reaches the WASH and the FLAME through the same two functions,
    // so a windblown fire and the pool it throws cannot come apart.
    float hz = GhvrFireHz(p, _FireRate);
    float dep = GhvrFireDepth(p, _FireCol.a);
    // The three seats went from three copies of four lines to three copies of
    // twelve when the core and the coals arrived, which is three places to
    // leave a fix behind — the exact accident this whole round is repairing on
    // the C# side. One function, called three times.
    float3 w;
    w.x = GhvrFireSeatOne(s0, _FirePos0.w, opos, N, t, hz, 1.00, 0.00, dep, reach, glut);
    w.y = GhvrFireSeatOne(s1, _FirePos1.w, opos, N, t, hz, 0.83, 0.37, dep, reach, glut);
    w.z = GhvrFireSeatOne(s2, _FirePos2.w, opos, N, t, hz, 1.19, 0.71, dep, reach, glut);
    // ...AND THE CRUST, once for the three seats rather than once each: the
    // char is a property of the SURFACE, not of which fire is heating it, and a
    // point that two seats reach would otherwise get the pattern squared.
    // Branched on the accumulated glut so the nine hashes are paid for only on
    // the few square metres of surface that are actually near a fire — and not
    // at all with Fire down, where every seat's window is zero.
    if (glut > 0.002) glut *= GhvrGlutCoals(opos);
    return col * ((w.x + w.y + w.z) * gain) + glutCol * glut;
}

#endif // GHVR_ENV_FIRE_INCLUDED
