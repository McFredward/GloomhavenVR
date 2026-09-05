// GloomhavenVR — THE HAUNT SCHEDULE. HAUNT: the one source of truth for when a
// creepy easter egg happens, which one, and how far into it we are.
//
// USER REQUEST, 2026-08-14: "Ich will noch ein weiteres Feature:
// 'Grusel-Easter-Eggs' in den Umgebungen. Also grusilige Animationen (ohne
// sound) die ab und zu auftreten in den Umgebungen (nur Wald und Keller). Es
// soll deaktivierbar sein. Die Animation sollen nicht aufdringlich sein, eher
// im Hintergrund aber einen ordnelichen Gruselfaktor auslösen ... Die easter
// eggs sollen niemals den Spielfluss stören eher im Hintergrund sein. Wie die
// anderen events auch sollen sie synchron von allen Spielern an den selben
// Stellen sichtbar sein."
//
// WHY A SHARED INCLUDE AND NOT FOUR COPIES. Four shaders need this schedule and
// they must agree to the BIT, because three of them react to an event the
// fourth one draws:
//   * EnvHaunt   — draws the apparitions themselves;
//   * EnvBeam    — the cellar moonbeam DIMS while the thing at the window is
//                  leaning in front of it;
//   * EnvRoomCutout — the cobwebs SHIVER on the tremble event;
//   * EnvCritter — the rat may stop and turn its head toward the room, but only
//                  in a slot the haunt schedule has left quiet, so the two
//                  can never fire at once.
// The rat's own schedule is duplicated between EnvCritter.shader and the C#
// builder on purpose (the builder has to MEASURE it — see AssertRatSchedule).
// Duplicating it a further four times inside the bundle would be a different
// thing entirely: those four all run on the GPU, in the same frame, on the same
// numbers, and a one-character drift between them would show up as a beam that
// dims a second before the face arrives. So the GPU side is ONE file, and the
// C# mirror in BuildEnvironmentRooms (HauntH / HauntCardOfSlot / ...) stays the
// single deliberate duplicate, for the single reason the rat's is.
//
// ============================================================================
// THE SCHEME, and why it is the rat's and not a new one.
//
// Time is cut into fixed SLOTS of _HauntPeriod seconds. slot = floor(t/period)
// is an exact integer, and everything about the slot — whether anything happens
// at all, WHICH of the room's events it is, when inside the slot it starts, and
// how long it runs — is a hash of that one integer. The only input is
//     t = _Time.y + _GhvrTimeOfs
// which is the shared environment epoch (SkyAlternative.EnvClockSeconds), so
// two clients do not compute a similar schedule, they compute the same bits.
//
// NO sin() IN THE HASH. The usual frac(sin(x)*43758.5) idiom leans on exactly
// the bits that differ between GPU vendors. H() below is multiply, add and frac
// on values under 200 — every one of those is a correctly-rounded IEEE-754
// single-precision operation on every GPU this mod runs on. Character for
// character the rat's cascade (EnvCritter.shader), for the same reason and with
// the same measured quality.
//
// NO Random, no per-client state, no per-instance seed, no frame history, and —
// the sharpest rule in this feature — NO camera or head input ANYWHERE. See the
// VR SAFETY block in EnvHaunt.shader.
// ============================================================================
//
// ============================================================================
// THE TWO THINGS THAT ARE NOT ALLOWED TO DEPEND ON A LOCAL SETTING, and how the
// dial is built so that they do not.
//
// The user's requirement is that every player sees the same event in the same
// place at the same time, AND that the feature has a frequency dial. Those pull
// against each other: a dial that fed into the hash would reshuffle the
// schedule, and two players on different dial positions would then see
// DIFFERENT events — which is the requirement, broken.
//
// So the dial may not select the schedule, only a SUBSET of it. The master
// schedule is fixed and dense; the dial is a monotone gate on top:
//
//      shown(slot) = ( H(slot, RATE) < freq )
//
// with freq the local dial (0..1). Because the gate is a comparison of a
// setting-independent hash against a threshold, a player at freq = 0.9 sees a
// strict SUPERSET of what a player at freq = 0.4 sees. Neither of them ever
// sees an event the other does not have in their own schedule at the same
// second and the same place — one of them simply misses some. That is the
// strongest guarantee a per-client dial can have, and it is exactly the one the
// user asked for. (freq = 1 shows every scheduled slot; the shipped default is
// 0.5, so the dial has room in BOTH directions without ever having to invent an
// event out of thin air, which is the thing that would break the guarantee.)
//
// The element board modulates freq too (Dark more often, Light less). That is
// legal by the same test: element state is scenario-wide, replicated by the
// game and desync-checked every round (see Core/ElementMood.cs), so the factor
// is bit-identical on every client and the superset property survives.
// ============================================================================
//
// ============================================================================
// NEVER THE SAME EVENT TWICE RUNNING — by construction, not by rejection.
//
// The obvious rule ("re-roll if it equals the previous slot's pick") is not
// available: resolving slot n needs the RESOLVED pick of slot n-1, which needs
// n-2, and a shader cannot walk a history. Bounding the recursion at two levels
// leaves a ~1/N^2 chance per slot of a repeat, which is not "never".
//
// So the slots are PARTITIONED instead. The room's events are laid out so that
// event k belongs to group (k mod 3), every group holds the same number of
// events, and slot n may only draw from group (n mod 3). Consecutive slots are
// in different groups, therefore consecutive slots are different events —
// exactly, in O(1), with one hash and one floor, and with no history at all.
// The bake asserts that the card count is a multiple of three and that each
// group mixes duration classes, so the partition cannot become a rhythm.
// ============================================================================
#ifndef GHVR_ENV_HAUNT_INCLUDED
#define GHVR_ENV_HAUNT_INCLUDED

// ---------------------------------------------------------------- the channel
// Published by Core/Haunt.cs, exactly as ElementMood publishes _GhvrElemA/B and
// SkyAlternative publishes _GhvrTimeOfs: ONE writer, NaN-rejected, zeroed on
// teardown. DELIBERATELY NOT a material Property — a Property of the same name
// would shadow the global per material and the switch would stop switching.
//
//   _GhvrHaunt.x = MASTER. 0 = the feature is off. Nothing else is needed to
//                  turn it off: every card collapses to a point in the vertex
//                  shader and no fragment is ever shaded.
//   _GhvrHaunt.y = FREQUENCY, 0..1. The monotone gate above. 0 = never.
//   _GhvrHaunt.zw reserved (0).
float4 _GhvrHaunt;

// ---------------------------------------------------- the ON-DEMAND channel
// HAUNT DREAD. "Feuere jedes Easter Egg auf Knopfdruck aus dem Advanced-Menü."
// The C# half publishes this global; every shader that reads the schedule
// honours it, here, in one place.
//
//   _GhvrHauntForce.x = FORCED EVENT ID PLUS ONE. 0 means nothing is forced.
//                       The id is the card index inside its room's catalogue —
//                       the same index EnvBeam and EnvRoomCutout are already
//                       handed, and the same one BuildEnvironmentRooms prints.
//   _GhvrHauntForce.y = the SHARED-CLOCK time at which the forced event began,
//                       i.e. _Time.y + _GhvrTimeOfs sampled when the button was
//                       pressed. phase = (clock - y) / duration, clamped.
//   _GhvrHauntForce.zw reserved (0).
//
// THE ID TABLE the button labels need is in BuildEnvironmentRooms.cs, next to
// the catalogues themselves (grep HAUNT FORCE ID TABLE) — it has to live beside
// the card arrays, because the ids ARE the array order and a table kept
// anywhere else would be a copy that could drift from it.
//
// WHAT FORCING DOES, exactly:
//   * the named card plays, from `y`, at its authored duration with NO per-slot
//     jitter (durMul = 1) — the tester asked for THAT event, not for a random
//     stretch of it;
//   * every OTHER card in the room is hidden, because `h.card` is the forced id
//     and each card draws only when h.card is its own index. That falls out of
//     the existing test rather than needing a second one;
//   * the normal schedule is suppressed for as long as x stays set, since the
//     slot's own pick never gets computed. Leaving x set therefore fails SAFE:
//     the room goes quiet, it never doubles up.
//   * the three shaders that merely REACT to a haunt follow automatically. They
//     call GhvrHauntAt too, so forcing the cellar's window card dims the
//     moonbeam and forcing its tremble card shivers the cobwebs — which is
//     exactly what a tester pressing those two buttons has to see.
// The cost when nothing is forced is one uniform compare, and it is a uniform
// branch, so it is coherent across the whole draw.
//
// ONE GLOBAL, TWO ROOMS: ids are per-room and only one room is ever loaded, so
// a single channel is unambiguous. The C# lane must clear x to 0 when it stops
// forcing.
float4 _GhvrHauntForce;

// ------------------------------------------------- the REPLACEMENT channel
// HAUNT FIGURES. Published by Core/HauntFigures.cs, whose class doc is the ONE
// canonical statement of this contract; nothing else in the mod may write it.
//
//   _GhvrHauntFigures.x = SUPPRESS MASK. A bitmask of THIS room's card indices,
//                         carried as an exact small integer in a float (0..63).
//                         Bit k set means "card k is being played by one of the
//                         GAME'S OWN enemy models right now, so the apparition
//                         this bundle would draw for it must not be drawn".
//   _GhvrHauntFigures.yzw reserved (0).
//
// WHY THIS EXISTS AT ALL. USER, after testing the shader-drawn apparitions:
//   "Ich hab getestet und mir gefallen die Figuren und animationen gar nicht.
//    Ich habe eine andere Idee. Ich möchte dass du die Gegner-Figuren aus dem
//    Spiel nimmst (insbesondere die grusiligen), die die eingebauten passenden
//    Animationen ausführen (zB vorbeilaufen)."
// Four of the twelve cards are now real, rigged, animated monsters spawned by
// the C# side and walking on their own legs. The other eight are untouched and
// are still drawn here — including the two that are not creatures at all, the
// TOPPLING BOOKSHELF and the TREMBLE card that draws nothing and only shivers
// the cobwebs. If the mask were ever to reach those, a prop would stop falling.
//
// ZERO IS THE SAFE STATE AND IS ALSO THE UNWRITTEN STATE. An unset global reads
// as 0, which suppresses nothing, which is byte-for-byte the behaviour this
// bundle had before this channel existed. Every failure on the C# side — no
// bundle, no such monster, an exception — ends with 0 published, so the player
// keeps getting apparitions rather than an empty room. That is why the
// suppression is a runtime mask and not a bake-time deletion of the cards.
float4 _GhvrHauntFigures;

/// Is card `c` currently being played by a real figure instead of by us?
/// Extracted with exp2/floor rather than with integer ops: `c` arrives as a
/// float from a mesh UV lane or a material property and has usually been through
/// an interpolator, so it is rounded first — the same defensiveness the rest of
/// this file applies to card comparisons (`abs(a-b) < 0.5`, never `==`).
float GhvrHauntReplaced (float c)
{
    float k = floor(c + 0.5);
    if (k < 0.0 || k > 15.0) return 0.0;          // nothing outside a card index
    float bit = floor(_GhvrHauntFigures.x / exp2(k));
    return step(0.5, bit - 2.0 * floor(bit * 0.5));
}

// The element mood, for flavour and for readability compensation. It comes from
// EnvElement.cginc, which is the bundle's ONE quotation of Core/ElementMood.cs's
// contract.
//
// THIS FILE USED TO DECLARE _GhvrElemA/_GhvrElemB AND A `GhvrElems` STRUCT OF
// ITS OWN, and that was an integration accident worth recording: two lanes built
// the two halves of the element channel in the same round, each declared the
// uniforms it needed, and each picked the obvious name. The result compiled
// everywhere EXCEPT in a shader that wanted both — EnvBeam, which needs the
// haunt schedule (the apparition at the window dims the beam) AND the element
// mood (Air stirs it, GhvrMoonLight() dims it during an eclipse). There
// `GhvrElems` was a struct in one header and a function in the other, and the
// uniforms were declared twice. Including EnvElement here instead is the fix:
// one declaration of the channel, one struct, and any shader may take both
// headers. Include guards on both make the double include harmless.
#include "EnvElement.cginc"

// ------------------------------------------------------------- the constants
// Every one of these is mirrored in BuildEnvironmentRooms.cs, which measures
// the resulting schedule and prints it in the bake log. Change one, change both.
#define GHVR_HAUNT_GROUPS   3.0
#define GHVR_HAUNT_STARTLO  0.15    // earliest start, as a fraction of the slot
#define GHVR_HAUNT_STARTSP  0.40    // ...and the span it may start within
#define GHVR_HAUNT_DURLO    0.85    // per-slot duration scale, lo
#define GHVR_HAUNT_DURSP    0.30    // ...and span

// hash channels. Numbers are load-bearing: the C# mirror reads the same ones.
#define GHVR_HC_RATE   0
#define GHVR_HC_PICK   1
#define GHVR_HC_START  3
#define GHVR_HC_DUR    4
#define GHVR_HC_VARA   5
#define GHVR_HC_VARB   6
#define GHVR_HC_VARC   7

/// The schedule's only source of variety. n is the slot index (an exact
/// non-negative integer), k selects one of eight decorrelated channels.
/// Multiply/add/frac only — see the header for why sin() is banned here.
/// The +1.0 is not cosmetic: without it channel 0 of slot 0 starts at frac(0),
/// and 0 is this cascade's one fixed point.
float GhvrHauntH (float n, float k)
{
    float x = frac((n + 1.0 + k * 7.13) * 0.7548776662);
    x = frac(x * (x + 31.70));
    x = frac(x * (x + 17.31));
    return frac(x * (x + 43.19));
}

/// The element mood, already folded with ElementMood's own master, so a haunt
/// shader never has to remember to multiply. All zero when the element feature
/// is off, which makes every use below a no-op — that is why nothing here needs
/// an "is the element channel live" branch.
/// Kept as a NAME, not as a second implementation: <c>GhvrElems()</c> from
/// EnvElement.cginc already folds the master in and already returns zero for
/// every field when the feature is off. This alias exists so the haunt shaders
/// keep reading in their own vocabulary, and so a reader who greps for
/// "GhvrHauntElems" still lands somewhere that explains the relationship.
/// The returned <c>GhvrElem</c> carries one field the old local struct did not:
/// <c>live</c> (peak x master), which is the single value to branch on.
GhvrElem GhvrHauntElems ()
{
    return GhvrElems();
}

/// One slot of the schedule, fully decided.
struct GhvrHaunt
{
    float slot;    // the slot index (exact integer)
    float sIn;     // seconds into this slot
    float card;    // which of the room's events this slot belongs to, 0..cards-1
    float live;    // 1 = this slot fires on THIS client (master + dial + elements)
    float start;   // seconds into the slot at which it begins
    float durMul;  // per-slot scale on the event's authored duration
    float varA;    // free per-slot hashes for the event to shape itself with
    float varB;
    float varC;
};

/// Decide the slot that contains time `t`, WITHOUT the figure-replacement mask.
/// `cards` is the room's event count and MUST be a positive multiple of
/// GHVR_HAUNT_GROUPS (the bake asserts it).
///
/// THIS IS THE SCHEDULE, and the schedule is not affected by who draws an event.
/// Only GhvrHauntPresence reads it — see the block on GhvrHauntAt below for the
/// whole argument about why the two are separate.
GhvrHaunt GhvrHauntAtRaw (float t, float period, float cards)
{
    GhvrHaunt h;
    float per = max(period, 1.0);
    h.slot = floor(t / per);
    h.sIn  = t - h.slot * per;

    // WHICH EVENT — the group partition (see the header). slot mod 3, exact for
    // an integer well inside float32's integer range.
    float grp = h.slot - GHVR_HAUNT_GROUPS * floor(h.slot / GHVR_HAUNT_GROUPS);
    float inGroup = max(floor(cards / GHVR_HAUNT_GROUPS + 0.5), 1.0);
    // min() rather than trusting the hash: frac() can return exactly 0 but a
    // future edit to H() that let it reach 1.0 would index one card past the end
    // and pick an event that does not exist — i.e. draw nothing, silently, for
    // one slot in a few thousand. Cheaper to make it impossible.
    float j = min(floor(GhvrHauntH(h.slot, GHVR_HC_PICK) * inGroup), inGroup - 1.0);
    h.card = grp + GHVR_HAUNT_GROUPS * j;

    // WHETHER IT FIRES HERE. The elements bend the rate: Dark makes the room
    // more haunted, Light less. Deterministic (element state is bit-identical on
    // every client) and monotone in the dial, which is what keeps two players'
    // schedules nested rather than merely similar.
    GhvrElem e = GhvrHauntElems();
    float freq = saturate(_GhvrHaunt.y) * saturate(1.0 + 0.60 * e.dark - 0.35 * e.light);
    h.live = step(GhvrHauntH(h.slot, GHVR_HC_RATE), freq) * step(0.0001, _GhvrHaunt.x);

    h.start  = per * (GHVR_HAUNT_STARTLO
                      + GHVR_HAUNT_STARTSP * GhvrHauntH(h.slot, GHVR_HC_START));
    // ICE HOLDS IT LONGER — the room is frozen, and so is whatever is in it.
    //
    // AND THIS IS THE ONE PLACE IN THE BUNDLE WHERE AN ELEMENT DIVIDES A CLOCK-DERIVED PHASE:
    // durMul stretches the envelope, and GhvrHauntEnvelope below computes phase = (sIn - start)
    // divided by the stretched duration. It is NOT the project's frequency-scrub class — the
    // numerator is `sIn`, bounded to ONE slot period, so the worst error is 0.35 of a phase and
    // never the hundreds of cycles an unbounded _Time.y would give. It was nevertheless a real
    // juddering source while the element channel breathed at Waning: e.ice swung 0.28..0.52 every
    // 2.4 s, so a running Ice event walked forward and backward through its own curve. Since
    // 2026-09-06 ElementMood publishes a CONSTANT at every column (NO ENVIRONMENT EFFECT MAY BLINK,
    // ElementMood.cs), so durMul is piecewise constant, the phase advances at one rate, and what is
    // left is an event that simply lasts longer while Ice is up. Reviewed and deliberately kept.
    h.durMul = (GHVR_HAUNT_DURLO + GHVR_HAUNT_DURSP * GhvrHauntH(h.slot, GHVR_HC_DUR))
               * (1.0 + 0.35 * e.ice);
    h.varA = GhvrHauntH(h.slot, GHVR_HC_VARA);
    h.varB = GhvrHauntH(h.slot, GHVR_HC_VARB);
    h.varC = GhvrHauntH(h.slot, GHVR_HC_VARC);

    // ---- ON DEMAND (see the _GhvrHauntForce block at the top). One uniform
    // compare when nothing is forced.
    if (_GhvrHauntForce.x > 0.5)
    {
        // The forced run is expressed in the SAME terms as a scheduled one —
        // sIn measured from a start of 0 with no jitter — so the envelope, the
        // phase and every drawing decision downstream take exactly the path
        // they take in the shipped schedule. A second "forced" code path is how
        // a debug mode ends up being the thing that was tested.
        h.card   = _GhvrHauntForce.x - 1.0;
        h.live   = 1.0;
        h.start  = 0.0;
        h.durMul = 1.0;
        h.sIn    = t - _GhvrHauntForce.y;
        // The per-slot variety hashes still have to come from somewhere, and it
        // has to be the same somewhere on every client: the slot the forced
        // event STARTED in. (The press itself is replicated by the C# lane; the
        // shader never sees a local decision.)
        float fs = floor(_GhvrHauntForce.y / per);
        h.varA = GhvrHauntH(fs, GHVR_HC_VARA);
        h.varB = GhvrHauntH(fs, GHVR_HC_VARB);
        h.varC = GhvrHauntH(fs, GHVR_HC_VARC);
    }
    return h;
}

/// The schedule AS THE DRAWING SHADER SEES IT: the same slot, with any card that
/// a real game monster is playing this frame marked as "not mine to draw".
///
/// ============================================================================
/// WHY THE MASK IS APPLIED HERE AND NOT INSIDE GhvrHauntPresence, WHICH IS THE
/// ONE NON-OBVIOUS THING ABOUT THIS WHOLE MECHANISM.
///
/// Four shaders read this schedule and only ONE of them draws an apparition:
///   * EnvHaunt      DRAWS   — and must stop, for a replaced card;
///   * EnvBeam       REACTS  — the moonbeam dims while something is at the
///                             cellar window, and it should STILL dim, because
///                             now something really is;
///   * EnvRoomCutout REACTS  — the cobwebs shiver on the tremble card, which is
///                             not replaced at all;
///   * EnvCritter    DEFERS  — the rat only stares in a slot the haunt schedule
///                             left QUIET, and it must keep deferring, or the
///                             rat would cross the floor while a monster walks
///                             past the window. Two events at once is exactly
///                             what the group partition exists to prevent.
///
/// So suppression must reach the drawing and must not reach the reacting or the
/// deferring. The split is what does that, and the shape of the split is forced
/// by a constraint: NO .shader FILE MAY BE EDITED for this (they are owned by
/// other lanes this round), so the mask has to work through a call the drawing
/// shader already makes and the reactors do not.
///
///   GhvrHauntAtRaw  — the schedule. GhvrHauntPresence calls it, so the beam and
///                     the webs are bit-identical to before this existed.
///   GhvrHauntAt     — this. EnvHaunt.shader and EnvCritter.shader call it.
///
/// AND THE MASK IS EXPRESSED AS `card = -1`, not as `live = 0`, which is the
/// second half of the trick. EnvHaunt.shader draws a vertex only when its own
/// card lane matches h.card (`step(abs(h.card - v.uv.x), 0.5)`), so a card index
/// no mesh owns makes every existing test answer "not mine" with no new test and
/// no shader edit. EnvCritter reads only h.live, which is untouched — so the rat
/// still knows the slot is busy even though this file has stopped drawing it.
/// ============================================================================
GhvrHaunt GhvrHauntAt (float t, float period, float cards)
{
    GhvrHaunt h = GhvrHauntAtRaw(t, period, cards);
    // -1 is not a card. lerp rather than a branch: this runs per vertex on every
    // apparition card in the room and the condition is uniform, but a select is
    // free and a branch is only nearly free.
    h.card = lerp(h.card, -1.0, GhvrHauntReplaced(h.card));
    return h;
}

/// The envelope of one event, and the phase through it.
///
/// THE ASYMMETRY IS THE WHOLE TRICK, so it is a parameter rather than a curve:
/// an apparition that fades IN slowly is never caught arriving, and one that
/// vanishes INSTANTLY (fade = 0) leaves the player unable to decide whether it
/// was ever there. Startling is explicitly forbidden by the brief; being unsure
/// is not. Both directions are available per event.
///
/// Returns 0..1 presence. `phase` comes back as 0..1 across the whole event, for
/// the drawing code to shape itself with (a grin that widens while it watches).
float GhvrHauntEnvelope (float sIn, float start, float rev, float hold, float fade,
                         float mul, out float phase)
{
    float r = max(rev * mul, 1e-4);
    float hl = hold * mul;
    float f = fade * mul;
    float x = sIn - start;
    phase = saturate(x / max(r + hl + f, 1e-3));
    float up = smoothstep(0.0, r, x);
    // fade == 0 is the INSTANT VANISH, and it has to be a real step: a
    // smoothstep with a collapsed interval would still be one frame of ramp on
    // some compilers, and "was it there?" needs the frame it was there in to be
    // the last one.
    float down = (f > 1e-4) ? smoothstep(r + hl + f, r + hl, x) : step(x, r + hl);
    return saturate(up * down);
}

/// Presence of ONE named card at time t — the read the shaders that merely
/// REACT to a haunt use (the beam that dims, the webs that shiver). Returns 0
/// unless that card is this slot's event, this slot fires, and we are inside it.
///
/// IT READS THE RAW SCHEDULE, DELIBERATELY. A card played by a real monster is
/// still HAPPENING; it is only being drawn by something else. The moonbeam
/// should dim while a figure crosses the cellar window — more so than before,
/// because now the thing casting the shadow exists — and the cobwebs' card is
/// not replaced at all. See the block on GhvrHauntAt.
float GhvrHauntPresence (float t, float period, float cards, float card,
                         float rev, float hold, float fade)
{
    GhvrHaunt h = GhvrHauntAtRaw(t, period, cards);
    float phase;
    float a = GhvrHauntEnvelope(h.sIn, h.start, rev, hold, fade, h.durMul, phase);
    // abs()<0.5 rather than ==: card indices are small integers carried in
    // material floats and in mesh UVs, and an exact float compare on a value
    // that has been through an interpolator is a bug waiting for a driver.
    return a * h.live * step(abs(h.card - card), 0.5);
}

#endif // GHVR_ENV_HAUNT_INCLUDED
