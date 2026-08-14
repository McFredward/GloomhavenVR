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
#define GHVR_FKIND_PUFF   2.0   // detaches, rises, reddens, dies

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
//              DEEPER flicker — on the FLAME and on the light it casts at once,
//              because they share a rate by construction. Deeper and not
//              faster: ModBuild 147 cut the rate multiplier from 1.55 to 1.15
//              on the "Zucken des Feuers" verdict, and the +70 % depth, the
//              tear, the lean and the embers carry the whole pairing. See
//              GhvrFireHz.
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

/// FIRE+AIR, on the rate. This is the number the FLAME and the WASH both take,
/// so the two cannot come apart under wind any more than they can at rest.
///
/// ================= 0.55 -> 0.15, ModBuild 147 ("Zucken ... des Feuers") =====
/// USER VERDICT, hardware, verbatim:
///   "Wind führt zu einem sehr hektischen unrealistischen Zucken der Pflanzen,
///    des Feuers und der Bäume - mach das es sich mehr random und immersiver im
///    Wind bewegt, nicht so hektisch, so Mikrozuckungen hat."
///
/// The ModBuild 145 lane that fixed "das Feuer zappelt viel zu schnell" left
/// this exact line as a written hand-off: "Under full Fire+Air GhvrFireHz x1.55
/// puts the fast band at 12.3 Hz; now harmless at weight 0.08, but THAT IS THE
/// KNOB if wind ever draws the same complaint." It has.
///
/// WHY 1.55x WAS THE WRONG SHAPE OF ANSWER, and it is the same fault as the one
/// in EnvGrowth's GhvrWind this round: an element strength was multiplying a
/// FREQUENCY. The puffing rate of a buoyant fire (f = 1.5/sqrt(D), Cetegen &
/// Ahmed — the correlation the whole ModBuild 145 band table is built on) is set
/// by BUOYANCY and by the fire's DIAMETER. It is famously insensitive to cross-
/// flow: a wind tilts the plume, tears its edges, entrains more air and drags
/// more of it downwind, and at strong cross-flow it disrupts the puffing cycle
/// rather than accelerating it. Nothing in the literature turns a 4.6 Hz fire
/// into a 7.1 Hz one by blowing on it — and 7.1 Hz drags the whole ladder with
/// it, so the 3.6 cm band lands at 12.3 Hz and the WASH ON THE STONE, the
/// largest and dimmest field in the cellar, carries 8 % of its modulation just
/// above the 8-10 Hz peak of human temporal contrast sensitivity (de Lange) —
/// the band a large dim-adapted field is most visibly unsteady in, and the same
/// argument ModBuild 145 used to take that weight from 0.19 down to 0.08 in the
/// first place. Raising the clock 55 % put a chunk of it back. That is the
/// "Mikrozuckung" in the room's own LIGHT rather than in a shape, which is also
/// why it is felt in the whole cellar and not only where the fire is.
///
/// 0.15 rather than 0.0: cross-flow does perturb the cycle a little and the
/// measurements scatter either side of the still-air value, so a 15 % lift is
/// inside what a real fire does — and Fire+Air keeps a rate response, so the
/// rule that a flame and its light share one clock still has something to say
/// under wind. What "flackert wenn Wind an ist" is actually MADE OF is
/// unchanged and is all amplitude: GhvrFireDepth's +70 %, EnvFlame's tear
/// (x2.9), the whole-fire lean, embers thrown 3x further and alive for three
/// quarters of their cycle instead of a third.
///
/// MEASURED, 200 s at 4 kHz on the exact arithmetic, at full Fire+Air:
///   tongue tip   f_mean 8.18 -> 6.07 Hz, rms accel 187 -> 103 m/s^2,
///                power above 6 Hz 67.6 % -> 38.9 % (= the Fire-only figure),
///                direction reversals 18.5 -> 13.7 /s, EXCURSION IDENTICAL
///   brightness   f_mean 6.78 -> 5.03 Hz, |dB/dt| 1075 -> 798 %/s,
///                power above 6 Hz 64.1 % -> 14.3 % (= the Fire-only figure),
///                reversals 15.1 -> 11.2 /s, SWING IDENTICAL at +-93 %
/// Nothing about how far anything moves or how much anything brightens changed;
/// the fire under wind is exactly as violent and is no longer in a hurry.
///
/// ...and on the RENDERED PIXELS, from the same throwaway phase harness the
/// wind half of this round used (48 frames one 72 Hz frame apart, cellar,
/// Fire-only and Fire+Air). The quantity is the temporal structure function:
/// of everything that changes over 0.2 s, how much has happened after one
/// frame?
///     FireCrate  Fire only 18.6 %   Fire+Air BEFORE 26.0 %   AFTER 22.5 %
///     FireRoom   Fire only 14.7 %   Fire+Air BEFORE 23.1 %   AFTER 19.5 %
/// ...with the total change over 2 s unchanged (+3 % and +1 %), i.e. the fire
/// under wind is doing the same amount of moving in a way that is 20-24 % less
/// abrupt frame to frame. The Fire-only rows came back byte-identical before
/// and after, which is what "this round touches only the Air response" looks
/// like when it is measured rather than asserted. The distinction is still
/// enormous: Fire+Air changes the picture 2.1x as much over two seconds as
/// Fire alone does (FireCrate), 1.5x on the whole-room view.
///
/// REJECTED: lowering _FireHz itself. It is 4.6 and it MUST stay 4.6 — see the
/// ModBuild 145 block at the top of this file. Fire+Air is a modulation of that
/// clock and this is the modulation, not the clock.
float GhvrFireHz (GhvrFirePair p, float hz)
{
    return hz * (1.0 + 0.15 * p.air);
}

/// ...and on the depth. Wind-fed fire is unsteady in AMPLITUDE as well as in
/// rate — that is the half that reads across a room. Bounded, because
/// GhvrWave4 is: at full Fire+Air the pool swings +-75% instead of +-45%.
float GhvrFireDepth (GhvrFirePair p, float depth)
{
    return depth * (1.0 + 0.70 * p.air);
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
/// The window is (1 - (d/r)^2)^2, the same one PointLight uses, deliberately
/// WITHOUT its near-field _PtHard divisor: that divisor exists to shrink a
/// candle's pool to its own table (user ruling, ModBuild 134) and would shrink
/// a bonfire to the size of a candle with it.
///
/// The three seats flicker at hz, 0.83*hz and 1.19*hz, so two fires in one room
/// never pulse as a pair — the same three incommensurate factors the three
/// candle slots use, for the same reason.
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
    float3 w;
    {
        float3 lv = s0 - opos; float rw = _FirePos0.w * reach;
        float q = dot(lv, lv) * rw * rw;
        float x = saturate(1.0 - q);
        w.x = x * x * saturate(dot(N, lv * rsqrt(max(dot(lv, lv), 1e-8))));
    }
    {
        float3 lv = s1 - opos; float rw = _FirePos1.w * reach;
        float q = dot(lv, lv) * rw * rw;
        float x = saturate(1.0 - q);
        w.y = x * x * saturate(dot(N, lv * rsqrt(max(dot(lv, lv), 1e-8))));
    }
    {
        float3 lv = s2 - opos; float rw = _FirePos2.w * reach;
        float q = dot(lv, lv) * rw * rw;
        float x = saturate(1.0 - q);
        w.z = x * x * saturate(dot(N, lv * rsqrt(max(dot(lv, lv), 1e-8))));
    }
    // FIRE+AIR reaches the WASH and the FLAME through the same two functions,
    // so a windblown fire and the pool it throws cannot come apart.
    float hz = GhvrFireHz(p, _FireRate);
    float dep = GhvrFireDepth(p, _FireCol.a);
    w.x *= GhvrFireFlicker(t, hz,        0.00, dep);
    w.y *= GhvrFireFlicker(t, hz * 0.83, 0.37, dep);
    w.z *= GhvrFireFlicker(t, hz * 1.19, 0.71, dep);
    return col * ((w.x + w.y + w.z) * gain);
}

#endif // GHVR_ENV_FIRE_INCLUDED
