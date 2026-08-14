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

/// FIRE+AIR, on the rate. A fire in a draught does not sway more slowly, it
/// turns over faster; this is the number the FLAME and the WASH both take, so
/// the two cannot come apart under wind any more than they can at rest.
float GhvrFireHz (GhvrFirePair p, float hz)
{
    return hz * (1.0 + 0.55 * p.air);
}

/// ...and on the depth. Wind-fed fire is unsteady in AMPLITUDE as well as in
/// rate — that is the half that reads across a room. Bounded, because
/// GhvrWave4 is: at full Fire+Air the pool swings +-75% instead of +-45%.
float GhvrFireDepth (GhvrFirePair p, float depth)
{
    return depth * (1.0 + 0.70 * p.air);
}

/// The brightness of a fire at time t, in [1-depth, 1+depth].
///
/// Four bands: the base rate, a slower one under it, a fast one over it and a
/// slow breath. At the shipped 4.6 Hz that is 4.6 / 2.8 / 8.0 / 1.1 Hz, i.e. the
/// turbulence band the brief asks for plus the swell that stops it reading as
/// buzz. `phase` decorrelates two fires in one room; `hz` is shared between a
/// flame and its light and is the whole of the rule this function exists for.
float GhvrFireFlicker (float t, float hz, float phase, float depth)
{
    float4 w = GhvrWave4(float4(t * hz             + phase,
                                t * hz * 0.61      + phase * 1.7 + 0.31,
                                t * hz * 1.73      + phase * 0.4 + 0.67,
                                t * hz * 0.235     + phase * 0.9 + 0.13));
    // the weights sum to 1, so |f| <= 1 and `depth` is exact
    float f = 0.40 * w.x + 0.27 * w.y + 0.19 * w.z + 0.14 * w.w;
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
