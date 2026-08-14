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
//     HARDENS them: GhvrSrcGain / GhvrSrcHard. INDOORS BOTH OF THOSE ARE THE
//     IDENTITY — the cellar's sources are candles and the user has ruled them
//     untouchable; see "AND THE CANDLES ARE UNTOUCHABLE INDOORS" below.
//
//  Light alone: the room lifts. Dark alone: the room falls away and the pools
//  shrink to their flames. BOTH: maximum contrast — a black room with a handful
//  of small, fierce, hard-edged sources in it. That is a picture neither element
//  can produce alone, which is the definition of a mixture that does not cancel.
//  Fire+Ice (embers rising through falling snow) and Air+Earth (a dust storm)
//  are simple additive layering, and they read because each element owns a
//  different sensory channel: colour, particle, motion and place.
//
//  ...AND INDOORS, LIGHT DOES NOT LIFT THE ROOM AT ALL — it lifts the MOON
//  ---------------------------------------------------------------------
//  USER VERDICT, ModBuild 146 (hardware, cellar, verbatim):
//    "Der 'Hell'-Effekt im Keller gefällt mir noch nicht, es soll wirklich den
//     Mondschein heller machen statt den ganzen Raum."
//
//  This is NOT a reversal of ModBuild 143's cellar ruling ("Bei Licht sollte
//  auch der Mondschein aus dem Fenster viel intensiver sein UND DEN RAUM MEHR
//  ERHELLEN, anstatt die Kerzenscheine"); read together, the two sentences name
//  a MECHANISM. The room may get brighter, but only BY the moonlight — the beam
//  through the window, the pool it lands in, the faces it falls on, the puddle
//  it is mirrored in. What it may not do is lift the AMBIENT FLOOR, because a
//  lifted floor brightens the far corners, the ceiling and the shadowed side of
//  every barrel at once, which is a room being turned up rather than a moon
//  coming out from behind cloud. Both sentences are satisfied by moving the
//  gain from GhvrAmbGain into GhvrDirGain, and by nothing else.
//
//  The forest wants the OPPOSITE and its ruling is equally binding (ModBuild
//  143, verbatim): "Licht und Dunkelheit beeinflussen zwar den Mond aber nicht
//  die Lichtverhältnisse in der Lichtung. Bei Dunkelheit soll auch entsprechend
//  die Lichtung dunkler werden ... Bei Helligkeit sollten diese Dinge
//  intensiver werden." A clearing under an open sky IS lit by its own sky, so
//  there the ambient floor is a moonlight term and it must follow the moon.
//  That is why GhvrIndoor() exists (see the uniform below): one set of gains,
//  two biologies of light, and neither ruling has to lose.
//
//  AND THE CANDLES ARE UNTOUCHABLE INDOORS — the third standing ruling, now
//  enforced HERE instead of one call site at a time (ModBuild 143, verbatim):
//    "anstatt die Kerzenscheine, die sollten identisch bleiben."
//    "Auch bei Dunkelheit sollte es keinen Einfluss auf den Kerzenschein haben."
//  EnvRoom.shader answered that in ModBuild 144 by deleting its own two knobs,
//  which fixed the walls and left every OTHER reader of the source gain still
//  moving the candles: the halos (EnvGlow), the drips (EnvDrip), the reflected
//  shard in the puddle (EnvPuddle) and the tipping bookshelf (EnvHaunt) all
//  brightened by 2.10x under Light in a room whose walls no longer did. So
//  GhvrSrcGain and GhvrSrcHard are now the identity INDOORS, exactly, for every
//  consumer at once — and the moon, which is a source but is not in the room,
//  is served by GhvrSkySrcGain below instead.
//
//  THE MOON IS A CONTRACT, not a picture — read MOON PHASE at the bottom
//  ---------------------------------------------------------------------
//     float GhvrMoonLight();   // 1.0 = the moon is unobstructed and at its
//                              // authored size; below 1 under the HELD Dark
//                              // eclipse, above 1 while Light swells it.
//                              // Multiply any MOONLIGHT term by this:
//                              //   dirGain = GhvrDirGain(e) * GhvrMoonLight();
//  Dark holds the Earth's umbra over the disc and Light swells it, and both
//  have to reach the ROOMS — the cellar's beam, the wood's shafts, the moon rim
//  on the trunks — or the eclipse is a picture of an eclipse. One multiply buys
//  the whole coupling: no uniform, no timing and no state at the call site.
//  RANGE: 0.05 at full Dark .. 1.0 at rest .. 1.34 at full Light. The floor is
//  deep on purpose and the argument for it is under THE FLOOR IS 0.05 below.
// ============================================================================
#ifndef GHVR_ENV_ELEMENT_INCLUDED
#define GHVR_ENV_ELEMENT_INCLUDED

// DELIBERATELY NOT material Properties — a Property of the same name shadows the
// global per material and the one switch would stop switching for that material
// only, which is the worst possible failure mode (it looks like a tuning bug).
// Same reasoning, same words, as EnvHaunt.cginc's _GhvrHaunt.
float4 _GhvrElemA;
float4 _GhvrElemB;

// WHICH ROOM IS STANDING: 1 in the CELLAR, 0 in the forest, 0 when nothing
// stands. A global, written by Core/SkyAlternative.ApplyIndoor whenever the
// style changes, for the same reason and by the same rules as the two lines
// above — see that method for the full argument.
//
// WHY THE ROOMS HAVE TO BE TELLABLE APART AT ALL. The gains below are shared by
// both rooms, and the user has ruled OPPOSITE things for the two of them. The
// forest clearing is supposed to brighten under Light ("Licht und Dunkelheit
// beeinflussen zwar den Mond aber nicht die Lichtverhältnisse in der Lichtung",
// ModBuild 144). The cellar is not ("Der 'Hell'-Effekt im Keller ... es soll
// wirklich den Mondschein heller machen statt den ganzen Raum", ModBuild 146):
// indoors, Light must go into the moonlight coming through the window and leave
// the room's own darkness — and its candles, which he ruled untouchable a round
// earlier — exactly where they are. Without this float one of those two rulings
// has to lose.
//
// It is a PRESENTATION constant, never a wire value: every client derives it
// from its own style dial, exactly as it derives which room to instantiate.
float _GhvrIndoor;

/// 1 indoors (the cellar), 0 outdoors (the forest) — and 0 when no environment
/// stands, so a shader that is somehow still resident falls back to the outdoor
/// behaviour, which is the one that matches the game's own lighting.
float GhvrIndoor ()
{
    return saturate(_GhvrIndoor);
}

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
// The functions the Light+Dark split lives in. All of them return EXACTLY 1.0
// when nothing is up — in EITHER room, since every room-dependent factor below
// multiplies an element value that is itself 0 — so a consumer can call them
// unconditionally and still be bit-identical. The branch is for cost, not for
// correctness.
//
// THE FOUR CONSTANTS THE TWO ROOMS DISAGREE ABOUT, named rather than inlined,
// because they are the whole of two competing user rulings and the next round
// will want to find them in one place. See "...AND INDOORS" in the header.
#define GHVR_AMB_LIFT_OUT 0.85  // the clearing lifts under Light: it is lit by
                                // the sky, and the sky is where the moon is.
#define GHVR_AMB_LIFT_IN  0.00  // the cellar does NOT. Exactly zero, so the
                                // indoor ambient under full Light is the SAME
                                // BITS as the resting ambient and no amount of
                                // Light can raise the room's black level.
#define GHVR_DIR_LIFT_OUT 0.90  // the wood's moon, unchanged since ModBuild 143.
#define GHVR_DIR_LIFT_IN  1.40  // the cellar's moon takes the ambient's share as
                                // well as its own. Composite with the swell:
                                // (1 + 1.40) * 1.34 = 3.22x on the beam, on the
                                // pool it lands in, on the puddle's mirror and
                                // on every moonlit face — against 2.55x before,
                                // with the room's own floor now held at 1.00.
//
// WHERE 1.40 COMES FROM, because "how much" is the whole of this round's verdict
// and a number picked by eye would be the fourth complaint. It is a TRANSFER,
// measured off the cellar itself rather than chosen: the preview harness was run
// with the room's three lighting terms separated (candles / ambient / moon) by
// solving the three-frame system {rest, old full Light, new full Light} per
// pixel percentile over seven cellar views. Across the room the ambient and the
// moon contribute in a ratio near A/D = 0.75. The gain being taken away is the
// ambient's 0.85, so the gain the moon has to be given to carry the same light
// is
//       dL = 0.85 * A / (1.34 * D) = 0.85 * 0.75 / 1.34 = 0.48
// on top of the outdoor 0.90 — i.e. 1.38, rounded to 1.40. At that value the
// measured room is NOWHERE brighter under full Light than it was in the build
// the user rejected (the largest overshoot is +6% at the 25th percentile of the
// darkest corner, which is 0.4 of an 8-bit step at that level), while every
// pixel the moon does NOT reach falls all the way back to its resting value —
// and that second clause is the one that has no exception in it: with the
// ambient lift at exactly 0.00 and the candles at exactly 1.00, a pixel in this
// room can only brighten under Light BECAUSE THE MOON REACHES IT. Before, every
// pixel in the room brightened, by up to 85%, whatever the moon was doing.
//
// REJECTED: giving EnvBeam an extra indoor-only Light factor of its own, so the
// visible shaft could go to 2x while the surfaces stayed at 1.16x. It is the
// obvious way to make the headline number bigger and it breaks the one design
// rule three files in this bundle state in the same words — the beam, the pool
// it lands in and the disc in the sky are ONE event, computed from ONE
// expression, so that the player reads one cause and not three effects. A shaft
// twice as bright as the pool underneath it is a shaft that is lying about where
// its light goes. If the next verdict wants more moonlight, this constant is the
// single place to raise it, and everything moves together.
// REJECTED: also crushing the indoor ambient under Light (a negative lift), to
// buy contrast by deepening the shadows. It reads well in a still and it is
// indefensible in a sentence: "the Light element makes the room darker" is not
// something this feature can be asked to explain.
//
// WHAT THIS ROUND COULD NOT FIX, and it belongs to the bake lane rather than to
// the shading: the cellar's moon is an UNOCCLUDED directional. It lights every
// north-east-facing surface in the room at the same N.L whether or not the
// window can see it, so the moon's share is 50% even at the 50th percentile of
// the darkest corner in the room (measured). No coefficient here can tell the
// wall the beam falls on from the wall behind the stair, because the shader is
// not told which is which. If the next round wants Light to reach ONLY the
// window's throw, what is needed is a per-material or per-vertex moon-visibility
// mask on _DirCol in the cellar — the forest already has exactly that
// (EnvGround's _CsMap canopy shadow), and the cellar has nothing.

/// Ambient/hemisphere gain: the room's own floor of light. Light lifts it only
/// while Dark is not up (that is the (1 - dark) factor) AND only OUTDOORS (see
/// GHVR_AMB_LIFT_IN); Dark crushes it to a fifth in both rooms, which is the
/// cellar's own ruling ("den Raum insgesamt deutlich dunkler") and the wood's.
/// REJECTED: letting Light win the ambient too — then Light+Dark is a wash,
/// i.e. exactly the grey average the brief forbids.
/// REJECTED, and this is the one worth recording: leaving a SMALL indoor lift
/// (0.15-0.20) on the grounds that a brighter beam really does bounce off the
/// flagstones and fill the room a little. It is physically true and it is
/// exactly what the user rejected twice — the bounce is a whole-room term and
/// he can see it as one. The beam's own landing pool carries that light instead,
/// through GhvrDirGain, where it stays attached to the thing casting it.
float GhvrAmbGain (GhvrElem e)
{
    float lift = lerp(GHVR_AMB_LIFT_OUT, GHVR_AMB_LIFT_IN, GhvrIndoor());
    return max(1.0 + lift * e.light * (1.0 - e.dark) - 0.80 * e.dark, 0.0);
}

/// The gain for a source that is OUTSIDE the room: the moon's disc and its halo,
/// painted by EnvStars. Light drives it hard; Dark does not subtract at all, and
/// the small light*dark term is what makes the split read as "fewer, BRIGHTER
/// sources" rather than merely "fewer".
///
/// It is deliberately NOT room-aware, and that is the entire reason it is a
/// separate function: it is the SAME SKY from both rooms (the cellar sees it
/// through the barred window), so a moon that swelled and brightened over the
/// clearing and merely swelled over the cellar would be two moons.
float GhvrSkySrcGain (GhvrElem e)
{
    return 1.0 + 1.10 * e.light + 0.35 * e.light * e.dark;
}

/// The gain for a source INSIDE the room: candle pools, flames, halos, drips,
/// the shard of candlelight in the puddle. Outdoors — where the "candles" are a
/// wisp, a far lantern and a firefly swarm, none of which anybody has ruled on
/// — it is GhvrSkySrcGain. Indoors it is EXACTLY 1.0, at every strength of every
/// element, because the user has ruled the candlelight untouchable twice (see
/// "AND THE CANDLES ARE UNTOUCHABLE INDOORS" in the header).
///
/// lerp(x, 1, ind) rather than a branch: with nothing up GhvrSkySrcGain is
/// exactly 1.0 and lerp(1,1,ind) = 1 + ind*0 = 1.0 for any ind, so the zero
/// state is bit-identical in both rooms and there is nothing to predicate.
float GhvrSrcGain (GhvrElem e)
{
    return lerp(GhvrSkySrcGain(e), 1.0, GhvrIndoor());
}

/// Near-field hardness multiplier for a point light's falloff (EnvRoom/_PtHard).
/// Dark collapses the lit pool toward its flame — the corners swallow light —
/// and Light softens it slightly so a lit room does not read as spot-lit.
///
/// INDOORS IT IS EXACTLY 1.0, for the same ruling and by the same arithmetic as
/// GhvrSrcGain: collapsing a candle pool is an INFLUENCE on the candlelight,
/// and "keinen Einfluss auf den Kerzenschein" does not have a shape exception
/// in it. What Dark does in the cellar instead is take the moon away
/// (GhvrDirGain x GhvrMoonLight down to 0.0275x), which leaves the three candle
/// pools standing as the only well-lit places in the room without a single
/// number on them having moved — the picture he asked for, arrived at by
/// subtracting the competition rather than by re-lighting the candles.
float GhvrSrcHard (GhvrElem e)
{
    float k = 1.0 - GhvrIndoor();
    return max(1.0 + k * (2.0 * e.dark - 0.25 * e.light), 0.05);
}

/// Directional (moon) gain. The moon is a SOURCE, so Dark only takes it away
/// while Light is not up: under the split the moon survives and the ambient
/// does not, which is what turns the moonbeam into the only thing in the room.
///
/// INDOORS LIGHT DRIVES THIS MORE THAN TWICE AS HARD (GHVR_DIR_LIFT_IN), and
/// that is the ModBuild 146 verdict expressed as one number: the gain the cellar
/// used to spend on its ambient floor is spent here instead. Every moonlight
/// term in the room rides this one function — the beam volume (EnvBeam), the
/// floor and walls the moon falls on (EnvRoom/EnvRoomCutout), the moon's mirror
/// in the puddle (EnvPuddle) and the cold rim on anything with a _RimCol — so
/// they cannot move apart, and there is exactly one place to look when the next
/// verdict says "more" or "less".
float GhvrDirGain (GhvrElem e)
{
    float lift = lerp(GHVR_DIR_LIFT_OUT, GHVR_DIR_LIFT_IN, GhvrIndoor());
    return max(1.0 + lift * e.light - 0.45 * e.dark * (1.0 - e.light), 0.0);
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

// ============================================================== MOON PHASE
//  MOON HELD. The moon does NOT move any more. User verdict, ModBuild 143
//  (verbatim): "6) Mir gefällt der Blutmond sehr, aber lass ihn statisch, das
//  'Vorbeiziehen' gefällt mir nicht gut, lass einen Blutmond statisch solange
//  das aktiv ist."
//
//  The picture was right and the ANIMATION was wrong, so the animation is gone
//  and the picture is held: while Dark is up the moon simply IS a blood moon,
//  at one fixed, composed geometry. It arrives and leaves by FADING with the
//  element's own intensity — which is the only thing that was ever allowed to
//  drive it in the first place (there is no start time to be had; see WHAT
//  COULD NOT BE DONE below, which is now moot rather than merely unsolved).
//
//  ...over the top of the previous round's finding, which is unchanged:
//    "6) Bei Dunkelheit sollte sich etwas vor den Mond Schieben - eher wie eine
//        Mondfinsternis die auch das Licht beeinflusst.
//     7) Bei der Helligkeit sollte der Mond anwachsen und mehr Licht abgeben."
//
//  THE CONTRACT — the one call a room shader owes, and the whole reason this
//  block lives in the SHARED header instead of inside EnvStars:
//
//      float GhvrMoonLight();
//      // 1.0 = the moon is unobstructed and at its authored size; below 1
//      // during the eclipse, above 1 while Light swells it. Multiply any
//      // MOONLIGHT term by this.
//      //   e.g.  dirGain = GhvrDirGain(e) * GhvrMoonLight();
//
//  An eclipse that happened only in the sky would be a PICTURE of an eclipse.
//  The user asked for the one "die auch das Licht beeinflusst", so the sky and
//  the rooms have to fall together, in step, off ONE function: a room shader
//  buys the entire coupling with a single multiply and owns no state, no timing
//  and no uniform of its own. The moonbeam in the cellar, the shafts in the
//  wood and the moon rim on the trunks all darken because the moon is being
//  covered — cause and effect, in one glance.
//
//  SEMANTICS, exactly:
//   * A pure function of _GhvrElemB. No extra uniform, no material property,
//     nothing a consumer has to remember to set — and, since MOON HELD, no
//     dependence on the clock either (the argument survives; see the macro).
//   * EXACTLY 1.0 whenever the master is 0, or Light and Dark are both 0 —
//     including with the other four elements at full strength. It is safe to
//     call unconditionally and the zero state stays bit-identical (rule 2).
//   * DARK: 1.0 -> GHVR_ECL_FLOOR as the element rises, i.e. the disc is held
//     TOTALLY eclipsed and only the STRENGTH of Dark decides how far the light
//     has fallen. The covered fraction is a constant 1 by construction (the
//     held umbra swallows the whole disc, see THE HELD GEOMETRY), so it is now
//     Dark itself that drives the room, on ElementMood's own smoothed ramp.
//   * LIGHT: x (1 + GHVR_MOON_SWELL * light) = 1.34 at full Light — the same
//     number the sprite grows by, NOT the disc's area (1.8x). The caller
//     already multiplies GhvrDirGain (1.90x outdoors, 2.40x indoors at full
//     Light) and 1.90 * 1.80 is a headlight that flattens the night into a grey
//     day; 1.90 * 1.34 = 2.55 is a moon you would call bright with a tree line
//     that is still black, and 2.40 * 1.34 = 3.22 is the cellar's, where the
//     room's own ambient floor no longer moves at all and the whole of Light
//     has to arrive through the window or not arrive (ModBuild 146).
//   * The two compose: a swollen moon still gets eaten (1.34 * 0.05 = 0.067).
//
//  THE FLOOR IS 0.05 AND IT USED TO BE 0.34 — the second half of this round's
//  verdict, and the reason is that 0.34 could not deliver either room's ruling.
//    Forest: "Bei Dunkelheit soll auch entsprechend die Lichtung dunkler
//    werden, also der angeleuchtete Boden und DIE LICHTSTRAHLEN VERSCHWINDEN."
//    Cellar: "bei Dunkelheit ... den Mondschein extrem zu reduzieren, so dass
//    der Raum insgesamt deutlich dunkler wird."
//  A shaft at a third of its brightness is a dimmed shaft, not an absent one:
//  the eye reads a blade of lit mist over a black wood at anything down to a
//  few per cent. The composite a room actually sees is
//      GhvrDirGain(e) * GhvrMoonLight() = 0.55 * 0.05 = 0.0275
//  at full Dark — one thirty-sixth of the authored moonlight, which is under
//  the 8-bit floor for every shaft term in the forest and takes the cellar's
//  beam below its own candles. That is the intended reading and it is now
//  allowed to be dark: in the cellar the beam is the only light BESIDES the
//  candles, and "the room goes very dark and the candles are what is left" is
//  precisely the picture the user asked for. Dark does not touch the candles
//  at all indoors — since ModBuild 146 BOTH GhvrSrcGain and GhvrSrcHard are the
//  exact identity there, so the pools are not even reshaped — and what is left
//  standing is three small pools with not one number on them changed. The
//  split's own promise, finally paid, and paid entirely by subtracting the one
//  competing source rather than by touching the survivors.
//  (Outdoors the hardening stays: the wood's "candles" are a wisp and a far
//  lantern, which nobody has ruled on and which read better collapsed.)
//    It is still NOT 0. A totally eclipsed moon is genuinely still there (a
//  real one loses ~10 magnitudes, i.e. far more than this), the copper disc is
//  still painted in the sky at full strength, and a beam that went to exactly
//  nothing would read as the effect being switched off rather than as the moon
//  being covered.
//
//  WHY THE ECLIPSE DIMS A SOURCE, when the split says Dark never dims sources:
//  because this is not a dimming. It is an OCCLUSION — a shadow crosses in
//  front, the covered part goes copper and the part still in the light stays
//  exactly as bright as it was. The disc keeps its hard limb throughout (Dark
//  still HARDENS it), there is simply less of it left. That difference is the
//  thing the brief asks to be legible, and it is why the attenuation is driven
//  by a geometric coverage and not by e.dark alone.
//
//  WHY A SHADOW AND NOT A BODY. "Etwas" could have been an opaque disc sliding
//  past, which would also have to occult the STARS around the moon. Rejected:
//  the user named the reference himself ("eher wie eine Mondfinsternis"), and
//  in a real lunar eclipse nothing is in front of the moon at all — the moon
//  walks into the Earth's shadow, the sky beside it is untouched, and the disc
//  turns copper-red because the only light still reaching it has been bent
//  through every sunrise on Earth at once. So: no star cull, no silhouette, no
//  hard black edge — a soft curved terminator crossing the disc, and a colour
//  nothing else in this sky can make. It is also the cheaper of the two.
//
//  THE HELD GEOMETRY, and why it is composed the way it is.
//
//  A blood moon is a TOTAL eclipse — that is what makes it copper. A partial
//  one is a bite taken out of a white disc, which is a different (and, once it
//  has stopped moving, a much duller) picture. So the held state is totality,
//  and the whole disc has to sit inside the umbra with the soft terminator
//  clear of the limb, or a stationary shadow edge would be frozen across the
//  face like a scratch on the lens.
//
//  THAT FREES THE UMBRA TO BE THE REAL SIZE. 1.35 moon radii was a TRANSIT
//  number and its comment said so: a small umbra keeps totality short and the
//  curved terminator on the disc for longer, both of which are properties of a
//  shadow that is going somewhere. Nothing is going anywhere now, so the umbra
//  goes to 2.60 R, which is what the Earth's shadow actually measures at the
//  moon's distance. It buys the composition below.
//
//  THE CENTRE IS OFFSET, AND THAT IS THE WHOLE COMPOSITION. With the umbra
//  centred on the disc, the Danjon gradient below is RADIALLY SYMMETRIC about
//  the middle of the moon: dark core, bright rim, all the way round. Held
//  still, that reads as a vignette — as a coloured filter laid over the moon,
//  which is exactly the failure the Danjon note further down was written to
//  avoid. Pushed off-centre by 1.15 R the same gradient becomes a MONOTONE RAMP
//  ACROSS THE FACE: one limb deep grey-brown, the opposite limb bright copper,
//  the terminator nowhere. That is what a photograph of totality looks like,
//  and it is the only arrangement in which a still image still says "there is a
//  shadow here, and it has a near side and a far side".
//
//  The two constraints that fix the number:
//    coverage 1.0  needs |c| <= UMBRA - 1     = 1.60
//    no terminator needs |c| <= UMBRA - 1 - EDGE = 1.515
//  and the gradient wants |c| as large as it can get. 1.15 R takes ~72% of the
//  available offset and leaves 0.37 R of clearance — enough that the swell
//  under Light, which scales the sprite and NOT this offset (both are in moon
//  radii, so the geometry is size-invariant), cannot walk the terminator onto
//  the disc. The direction is arbitrary in physics and not in composition: down
//  and to the right, so the copper limb is the upper left one, away from the
//  horizon and toward the part of the disc a player looking up at it sees first.
//
//  WHAT THE TRANSIT MACHINERY WAS, and it is DELETED rather than switched off:
//  GHVR_ECL_PERIOD, GHVR_ECL_MISS, GHVR_ECL_TRACK, the frac() that swept the
//  centre along the track, the exact 96-periods-per-sky-revolution divisibility
//  argument, EnvStars' penumbra wash, and the "WHAT COULD NOT BE DONE" note
//  that explained why a one-shot eclipse needed a start time nothing could
//  publish. None of it has a meaning any more: there is no phase, so there is
//  nothing for two clients to disagree about and nothing for a clock to drive.
//  The shared-clock rule is not weakened by this, it is satisfied vacuously —
//  and GhvrMoonLight() still takes the clock (see the macro) so that a shader
//  which is NOT on the shared epoch still fails to compile on _GhvrTimeOfs.
// -----------------------------------------------------------------------------
// The geometry is in units of the MOON'S OWN DISC RADIUS, so nothing here knows
// the sprite's size, the sprite's extent or the moon's bearing. EnvStars is the
// single place those meet, and it converts once.
#define GHVR_ECL_UMBRA  2.60    // umbra radius in moon radii — the Earth's real
                                // one at the moon's distance. See THE HELD
                                // GEOMETRY: a static eclipse has no reason to
                                // shrink it and every reason not to.
#define GHVR_ECL_CX     0.862   // the HELD umbra centre, in moon radii, on the
#define GHVR_ECL_CY    -0.759   // sprite's own (right, up) axes. |c| = 1.148,
                                // i.e. inside 1.515 with 0.37 R to spare, so
                                // the disc is wholly umbral and the Danjon ramp
                                // runs straight across it.
#define GHVR_ECL_EDGE   0.085   // terminator softness, in moon radii — an umbra
                                // cast through an atmosphere has no edge. Held
                                // still it is a CLEARANCE rather than a look:
                                // the centre above is chosen so no part of it
                                // reaches the limb.
#define GHVR_ECL_FLOOR  0.05    // moonlight left under full Dark. See THE FLOOR
                                // IS 0.05 above for where the number comes from
                                // and why it is not 0.
#define GHVR_MOON_SWELL 0.34    // disc radius gain at full Light

/// The umbra's centre, in moon-radius units from the disc's centre. A CONSTANT
/// since MOON HELD — kept as a function, and kept in this header, because
/// EnvStars paints the shadow from it and GhvrMoonLightAt weighs the room's
/// light by it: two readers, one number, no chance of the sky and the light
/// standing at different eclipses.
float2 GhvrEclipseCentre ()
{
    return float2(GHVR_ECL_CX, GHVR_ECL_CY);
}

/// Covered fraction of the disc, 0 (clear) .. 1 (total), from the separation of
/// the two centres. A smoothstep between first and last contact rather than the
/// exact circle-circle lens area: the two curves differ by a few percent in the
/// middle of the partial phase — far below what an eye can read off a disc 2.8
/// deg wide — and this is a term every lit pixel in two rooms pays for.
///
/// It survives MOON HELD even though its answer is now the constant 1.0 (|c| =
/// 1.148 is below UMBRA - 1 = 1.60, so the smoothstep clamps to 0 and this
/// returns exactly 1, which the compiler folds away). Deleting it and writing
/// the 1 in by hand would be one more place the held centre and the held
/// coverage could be moved out of step; this way a future round can shift the
/// centre and BOTH the picture and the light follow it, including off totality.
float GhvrEclipseCover (float2 c)
{
    return 1.0 - smoothstep(GHVR_ECL_UMBRA - 1.0, GHVR_ECL_UMBRA + 1.0, length(c));
}

/// Radius scale of the moon's disc, and of its halo with it. EXACTLY 1 with
/// Light down, so the sprite is sampled at its authored extent.
float GhvrMoonSize (GhvrElem e)
{
    return 1.0 + GHVR_MOON_SWELL * e.light;
}

/// THE CONTRACT (see the block above). `t` is the shared clock; the
/// GhvrMoonLight() macro below supplies it, and that is the form to call.
///
/// `t` IS DELIBERATELY UNUSED SINCE MOON HELD, and the parameter stays anyway.
/// Three reasons, none of them inertia: the SIGNATURE is what two other lanes
/// are multiplying into their moon terms this round and it must not move under
/// them; the macro that supplies it is what forces every caller to have
/// _GhvrTimeOfs in scope, which is this header's only compile-time proof that a
/// shader is on the shared epoch; and the day a future round wants the moon to
/// do anything at all again, the clock is already threaded through every call
/// site in the bundle. (The compiler drops the argument; it costs nothing.)
float GhvrMoonLightAt (float t)
{
    GhvrElem e = GhvrElems();
    if (e.live <= 0.0) return 1.0;
    float cov = GhvrEclipseCover(GhvrEclipseCentre());
    return GhvrMoonSize(e) * (1.0 - cov * e.dark * (1.0 - GHVR_ECL_FLOOR));
}

// A function-like MACRO rather than a function, and the reason is mechanical
// rather than stylistic: the contract says GhvrMoonLight() takes no argument
// and reads the shared clock itself, but every Env* shader declares
// _GhvrTimeOfs AFTER it includes this header (see EnvRoom.shader), so a
// function defined here cannot name it — and moving those declarations would
// mean editing a dozen shaders to add one call. A macro expands at the CALL
// SITE, where the uniform is in scope. Callers write GhvrMoonLight() and
// nothing else: the signature they were given is the signature they use.
// (A shader that has no _GhvrTimeOfs fails to compile ON THAT NAME, which is
// the correct diagnosis: it is not on the shared clock yet.)
#define GhvrMoonLight() GhvrMoonLightAt(_Time.y + _GhvrTimeOfs)

#endif // GHVR_ENV_ELEMENT_INCLUDED
