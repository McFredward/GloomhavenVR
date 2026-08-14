// ============================================================================
//  ELEMENT ART — the six Gloomhaven element infusions, read by the environment.
//
//  This is the ART half of the feature. The SENSING half is
//  src/GloomhavenVR/Core/ElementMood.cs, which owns the contract; this file is
//  the bundle's single quotation of it, so that ten shaders cannot each spell
//  the channel slightly differently.
//
//  THE CHANNEL (verbatim from ElementMood's class doc, "THE PUBLISHED CHANNEL"):
//     _GhvrElemA = float4(Fire,  Ice,  Air,   Earth)   // each 0..1, smoothed
//     _GhvrElemB = float4(Light, Dark, Master, Peak)
//  The six are NOT pre-multiplied by the master, so every consumer owes exactly
//  one multiply — which is what GhvrElems() below does, once, for everyone.
//
//  THE FIVE RULES THIS FILE EXISTS TO ENFORCE
//  ------------------------------------------
//  1. ONE SWITCH. Everything is `element * _GhvrElemB.z`. With the master at 0
//     nothing an element does can survive, no matter how a material is authored.
//  2. NOTHING WHEN NOTHING IS UP. Every consumer brackets its element block with
//     `if (e.live > 0)` — e.live is peak x master, i.e. "is ANY element up at
//     all", the one number ElementMood publishes for exactly this purpose. Two
//     things follow, and both are requirements rather than optimisations:
//     the standing cost with all six inert is one uniform compare, and the
//     ZERO STATE IS BIT-IDENTICAL to the build before this feature existed —
//     not "visually identical", identical, because the code does not run.
//     (Inside the block the same holds by construction: every term is either
//     `x * intensity` added or `1 + k*intensity` multiplied, both exact at 0.)
//  3. THE SHARED CLOCK. Anything animated rides `_Time.y + _GhvrTimeOfs`, the
//     mod's shared environment epoch, exactly as the rat, the flames and the
//     haunts do — so two players in one scenario see one element do one thing at
//     one instant. Element STATE is already bit-identical on every client (the
//     game replicates and desync-checks it; ElementMood adds no wire), so the
//     clock is the only thing that could have differed.
//  4. NO PER-CLIENT ANYTHING. No Random, no per-instance state, and no sin()
//     inside a hash: its last bits differ between GPU vendors, and a hash whose
//     last bits differ picks a different branch. (A sin() as an ANIMATION curve
//     is fine and is used freely below — a phase error of an ulp is invisible;
//     a hash error of an ulp is a different spark.)
//  5. THE PERIPHERY, NEVER THE BOARD. Consumers apply the periphery ramp (see
//     GhvrRim below) so that an element blooms at the walls, the tree line and
//     the sky and fades toward the play space. The board must stay readable with
//     all six elements at full strength.
//
//  MIXTURES MUST NOT CANCEL — and the split that proves it
//  ------------------------------------------------------
//  The elements are LAYERS, not states: several can be Strong at once, and the
//  user's own example is the hard case. Light + Dark must not average to grey.
//  So the two are NOT one axis here. They act on DIFFERENT terms of the same
//  lighting model, in opposite senses:
//
//     AMBIENT (the floor under everything — the term that decides how much of
//     the room exists at all) is lifted by Light and CRUSHED by Dark, and Dark
//     wins: GhvrAmbGain.
//     SOURCES (the candle pools, the moon, the flames, the glows — the things
//     you can point at) are lifted by Light, and Dark does NOT dim them, it
//     HARDENS them: GhvrSrcGain / GhvrSrcHard.
//
//  Light alone: the room lifts. Dark alone: the room falls away and the pools
//  shrink to their flames. BOTH: maximum contrast — a black room with a handful
//  of small, fierce, hard-edged sources in it. That is a picture neither element
//  can produce alone, which is the definition of a mixture that does not cancel.
//  Fire+Ice (embers rising through falling snow) and Air+Earth (a dust storm)
//  are simple additive layering, and they read because each element owns a
//  different sensory channel: colour, particle, motion and place.
// ============================================================================
#ifndef GHVR_ENV_ELEMENT_INCLUDED
#define GHVR_ENV_ELEMENT_INCLUDED

// DELIBERATELY NOT material Properties — a Property of the same name shadows the
// global per material and the one switch would stop switching for that material
// only, which is the worst possible failure mode (it looks like a tuning bug).
// Same reasoning, same words, as EnvHaunt.cginc's _GhvrHaunt.
float4 _GhvrElemA;
float4 _GhvrElemB;

/// The mood, already folded with ElementMood's master. Nothing downstream ever
/// has to remember to multiply, and nothing has to test whether the channel is
/// live: when the feature is off every field is 0 and every use is a no-op.
struct GhvrElem
{
    float fire, ice, air, earth, light, dark;
    // peak x master: "is anything up at all". The ONE value to branch on.
    float live;
};

GhvrElem GhvrElems ()
{
    GhvrElem e;
    float m = _GhvrElemB.z;
    e.fire  = _GhvrElemA.x * m;
    e.ice   = _GhvrElemA.y * m;
    e.air   = _GhvrElemA.z * m;
    e.earth = _GhvrElemA.w * m;
    e.light = _GhvrElemB.x * m;
    e.dark  = _GhvrElemB.y * m;
    e.live  = _GhvrElemB.w * m;
    return e;
}

/// A zeroed mood, for the `else` half of the live branch. Returning this rather
/// than leaving the modifiers uninitialised is what makes the zero state exact.
GhvrElem GhvrElemsZero ()
{
    GhvrElem e;
    e.fire = 0; e.ice = 0; e.air = 0; e.earth = 0;
    e.light = 0; e.dark = 0; e.live = 0;
    return e;
}

// ------------------------------------------------------------- THE SPLIT
// The three functions the Light+Dark split lives in. All three return EXACTLY
// 1.0 when nothing is up, so a consumer can call them unconditionally and still
// be bit-identical — the branch is for cost, not for correctness.

/// Ambient/hemisphere gain: the room's own floor of light. Light lifts it only
/// while Dark is not up (that is the (1 - dark) factor); Dark crushes it to a
/// fifth. REJECTED: letting Light win the ambient too — then Light+Dark is a
/// wash, i.e. exactly the grey average the brief forbids.
float GhvrAmbGain (GhvrElem e)
{
    return max(1.0 + 0.85 * e.light * (1.0 - e.dark) - 0.80 * e.dark, 0.0);
}

/// Source gain: candle pools, moon, flames, halos. Light drives it hard; Dark
/// does not subtract at all, and the small light*dark term is what makes the
/// split read as "fewer, BRIGHTER sources" rather than merely "fewer".
float GhvrSrcGain (GhvrElem e)
{
    return 1.0 + 1.10 * e.light + 0.35 * e.light * e.dark;
}

/// Near-field hardness multiplier for a point light's falloff (EnvRoom/_PtHard).
/// Dark collapses the lit pool toward its flame — the corners swallow light —
/// and Light softens it slightly so a lit room does not read as spot-lit.
float GhvrSrcHard (GhvrElem e)
{
    return max(1.0 + 2.0 * e.dark - 0.25 * e.light, 0.05);
}

/// Directional (moon) gain. The moon is a SOURCE, so Dark only takes it away
/// while Light is not up: under the split the moon survives and the ambient
/// does not, which is what turns the moonbeam into the only thing in the room.
float GhvrDirGain (GhvrElem e)
{
    return max(1.0 + 0.90 * e.light - 0.45 * e.dark * (1.0 - e.light), 0.0);
}

// --------------------------------------------------------- THE PERIPHERY
/// The periphery ramp: 0 at the middle of the play space, 1 out at the walls /
/// the tree line. `r` is the horizontal distance from the room centre and
/// `rad` the room's authored outer radius (both in OBJECT space — the room
/// meshes are authored around the origin, and props get their own centre
/// through _ElemCentre, written by EnvRoomBuilder.ApplyRig).
///
/// Everything an element does is weighted by this. It is the single mechanism
/// behind the permanent ruling that the board must stay readable: an element at
/// full strength owns the walls and touches the middle of the table hardly at
/// all. `lo` lets a consumer keep even more of the middle clear.
float GhvrRim (float r, float rad, float lo)
{
    return smoothstep(lo, 1.0, saturate(r / max(rad, 0.01)));
}

/// A slow warm breath for ember/flare terms — the same shape a fire has and
/// nothing like the candles' own buzzing flicker, so a fire-lit wall does not
/// read as one more candle. Phase varies with position so the warmth travels.
/// `t` MUST be the shared clock (_Time.y + _GhvrTimeOfs).
float GhvrEmberBreath (float t, float phase)
{
    return 0.82 + 0.30 * sin(t * 1.9 + phase) + 0.12 * sin(t * 4.7 + phase * 1.7);
}

#endif // GHVR_ENV_ELEMENT_INCLUDED
