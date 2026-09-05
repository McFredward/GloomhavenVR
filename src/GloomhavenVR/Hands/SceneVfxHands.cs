using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// PUT A HAND IN THE SMOKE AND THE SMOKE GOES ROUND IT — the scenario's own particle effects
/// (fire, smoke, embers, dust, spell wash) flow around the player's hands while they are inside,
/// and are restored to the authored settings, field for field, the moment they leave.
///
/// <para><b>=== THE MECHANISM CHANGED IN ModBuild 431, AND THIS PARAGRAPH IS WHY. ===</b> The
/// user, 2026-09-05, testing ModBuild 430 — the build in which the hands first reached the effects
/// at all: "Der Rauch reagiert nun auf die Hände aber überhaupt nicht immersiv — er weicht einfach
/// super schnell unnatürlich zurück. Ich will das er sich um die Hand bzw dem Finger legt — wie
/// wenn man einmal durch den Rauch 'weht'." That is not a tuning report. A COLLISION MODULE
/// REFLECTS A PARTICLE OFF A SURFACE, and a reflection is a billiard ball: it can be made slower
/// or faster, but there is no coefficient at which a reflection becomes a flow, because they are
/// different operations. Everything below the collision paragraphs in this doc was correct and
/// answered the wrong question.</para>
///
/// <para>Unity ships the other half and this codebase was not using it:
/// <c>ParticleSystemForceField</c>, a volume that ACCELERATES the particles that opt into it
/// through their <c>externalForces</c> module. Its fields are the vocabulary "the air moved" is
/// written in — a soft radial falloff, a directional push, a vortex about an axis, and a drag with
/// multipliers for particle size and velocity. A hand now carries such a field on the palm and a
/// smaller one on the fingertip, and their three live terms are:</para>
/// <list type="bullet">
///   <item>a DIRECTIONAL PUSH driven by the hand's own velocity, so a still hand disturbs nothing
///   and a swipe wafts — which is literally the "wie wenn man einmal durch den Rauch weht" in the
///   report;</item>
///   <item>a VORTEX about the direction the hand is travelling (palm) and about the direction the
///   finger points (tip), which is the term that makes smoke CURL AROUND a finger instead of
///   dodging it — no reflection can produce this and no drag can either;</item>
///   <item>a DRAG that makes the effect hang around the hand rather than flee it, scaled by both
///   particle size and particle velocity so a big soft puff feels the hand, a spark does not, and
///   nothing is frozen in mid-air.</item>
/// </list>
/// <para>COLLISION IS KEPT AND INVERTED. It is no longer the mechanism; it is the backstop that
/// stops a particle passing straight through the palm. So bounce goes to near zero (a bounce IS
/// the reported defect, and it is left as a dial precisely because it is the term that decides
/// between the two feels), dampen goes high, and lifetime loss goes to zero on everything but
/// flame. A particle that touches a hand now loses its speed there and is picked up by the vortex,
/// which is the wrap.</para>
///
/// <para><b>THAT RETIRES A STANDING HARDWARE QUESTION, and it should be said plainly.</b> The
/// ModBuild 430 revision left open "whether Medium/Low collision quality still deflects off a
/// moving hand", because at that point the deflection WAS the feature. A force field does not
/// consult a collider at all — it is a volume, not a surface — so the wrap, the cling and the waft
/// are identical at every collision quality and on a system whose collision never resolves. What
/// collision quality can still cost is the crispness of the backstop on a dense effect, which is a
/// second-order look, not the reaction. The graded adoption stays exactly as ModBuild 430 shipped
/// it; what changes is that nothing about the FEEL now hangs off it.</para>
///
/// <para><b>AND IT IS DESIGNED PER EFFECT, which was the other half of the request</b> ("Das Selbe
/// soll auch für andere Partikeleffekte gelten … überlege dir für jedes System ein immersives
/// Erlebnis wie genau die Hand damit interagiert"). Five classes, five force profiles, in
/// <see cref="VfxFlowProfile.Of"/>: smoke wraps and settles back, a flame is carved open and
/// springs up again but is never blown sideways, sparks scatter, dust hangs in the wake, an aura
/// is disturbed in the near field and stays owned by its figure. WHICH CLASS AN EFFECT IS IN IS
/// DECIDED BY WHAT IT DOES, NOT BY WHAT IT IS CALLED — lifetime, travel in its own widths, real
/// diameter, simulation space and actor ownership — and both the class and the term that decided
/// it are printed with the numbers beside them, so a misclassified effect names itself with its
/// own evidence on the first hardware run. The reasoning, including why the material's blend mode
/// was considered and deliberately left out, is at the top of <c>Hands/VfxFlow.cs</c>.</para>
///
/// <para><b>=== AND ModBuild 432 IS THE ROUND THAT BOUNDED IT. ===</b> The user on 431: "Beim Rauch
/// sieht man wie es immer noch super ruckartig zurückweicht — nicht sehr immersiv. Schlimmer ist es
/// bei der Flamme beim Altar: Dort glitcht die Flamme in der Gegend rum." Two symptoms, three
/// causes, all three readable in the ModBuild 431 log rather than guessed at:</para>
/// <list type="number">
///   <item><b>NOTHING EVER TOOK BACK THE SPEED A FIELD GAVE.</b> A force field is an acceleration.
///   A particle that passes through one leaves carrying the velocity it picked up and coasts on it
///   until it dies — no drag acts outside the field and most of these systems author none. The log
///   makes the magnitude unarguable: 'PrimeAltar_FX', the altar effect itself, is a Sparks system
///   with a 5.00 s lifetime, and Sparks' push of 2.2 m/s² against its drag of 0.18 has a terminal
///   speed of 12 m/s. Twelve metres a second for five seconds is sixty metres. Meanwhile the pale
///   wash the video drags across the room is 'GroundFogFar', ONE PARTICLE 17.1 REAL METRES ACROSS,
///   whose centre only has to come within a palm's reach for the whole billboard to be moved. The
///   remedy is a ceiling per system, derived from its own size and lifetime and capped absolutely
///   in real metres — <see cref="VfxFlow.SpeedCapRealPerSecond"/> and HandsVfxDriftMeters — and
///   enforced through the field's drag, because terminal speed IS acceleration over drag.</item>
///   <item><b>THE WAKE WAS A STEP FUNCTION ON A NOISY INPUT.</b> <c>hand.PalmVelocity</c> was read
///   raw once a frame and used for two things: the strength of the push (<c>_wake</c>, which rose
///   INSTANTLY to whatever that frame's value was) and the AXIS the entire vortex turns about
///   (<c>_wakeDirection</c>, rewritten every frame the hand moved faster than 1e-4 world units per
///   second, which is every frame there is). A tracked hand's per-frame velocity is a difference of
///   two poses over a frame time and it jitters; the first path turned that jitter into a stutter
///   and the second turned it into the thin curved streaks the smoke video shows, because a vortex
///   whose axis moves every frame drags each particle along a different arc. Both now come off one
///   exponentially smoothed vector, the rise is ramped over HandsVfxWakeAttackSeconds, and the
///   direction is held unless the hand is really going somewhere.</item>
///   <item><b>THE SIX SLOTS WERE SPENT ON FOG.</b> "Eine Interaktion mit Rauch, Funken etc. von
///   Fackeln in der Map gibt es bisher nicht" — and the premise behind it is INVERTED by the
///   census, which reports 201 adoptable systems of 202 with 0 of them on figures. Every effect in
///   that scenario is scenery and the torches were adopted. What the log then says is why they were
///   never felt: <c>nearest refused: 'p_fire_torch (10)' by CapFull</c>, <c>'p_fire_torch (8)' by
///   CapFull</c>, <c>'PrimeAltar_FX' by CapFull</c> — and the systems that WON those slots are
///   'P_SewerFog', 'MeshEmitterFog', 'Fog (3)', 'Cloud (4)'. Selection ranks on distance to the
///   drawn bounds, and a room fog's bounds CONTAIN the hand, so its distance is 0.0 and it wins
///   every comparison against a torch ten centimetres away. See <see cref="EmitterTieWeight"/>: the
///   cap of six is not the defect and is not raised.</item>
///   <item><b>AND A FOURTH, FOUND WHILE PROVING THE FIRST THREE: THE VERDICT DEPENDED ON THE
///   ZOOM.</b> Exactly one of the classifier's six terms knows about the room — the particle's
///   diameter in REAL metres, an authored world size divided by the rig scale, tested against
///   <see cref="VfxFlow.BulkDiameterMetres"/>. And the rig scale is not a property of the room: it
///   is the player's LIVE ZOOM, and the ModBuild 431 census walks it from 23.49 down to 2.14 world
///   units per real metre inside one session. Five assets in that log therefore carry two
///   different verdicts — 'distort' is Motes at 23.49 and Flame at 3.72, 'Fog (3)' is Flame at
///   3.72 and Motes at 8.46, 'ElemGust' and 'ElemEmbers' and 'HexHighlight(Clone)' each swap
///   between Motes and Smoke — and each pair recovers the SAME authored world size to within
///   0.9 %, which is what proves it is the zoom rather than two instances of different size. The
///   registry always re-measured on its one-second cadence, so an unheld system was never stale;
///   what was stale is the class a hand STAMPED into <see cref="_flowClass"/> at adoption and the
///   ceiling in <see cref="_driftCap"/> beside it, both held untouched for as long as the hand
///   held on. Both are now re-derived whenever the scale drifts past
///   <see cref="ReclassifyScaleRatio"/>, and a verdict that moves says so with the scale on both
///   sides. Re-classifying is the correct answer rather than a compromise: "is a single particle
///   big enough to see the shape of" is a question about the player's eye, and its honest answer
///   changes when the player zooms.</item>
/// </list>
///
/// <para>THE GROUNDWORK WAS ALREADY THERE. Unity's particle systems carry a COLLISION MODULE that
/// the game leaves switched off on ordinary effects, so nothing needs to be simulated, cooked or
/// added to a scene object: switching that module on with a mask that names the hand's own sphere
/// is the whole mechanism. This is candidate 3b of <c>.planning/scene-interactables-PARKED.md</c>,
/// which called it the cheapest of the three — and it is, but not for the reason recorded there.
/// See the next paragraph, which contradicts it.</para>
///
/// <para><b>THE PARKED SURVEY'S ROUTE IS THE ONE THIS DELIBERATELY DOES NOT TAKE.</b> It pointed at
/// the RFX4 effects, whose collision module the game switches on for a one-second window and whose
/// <c>RFX4_ParticleCollisionHandler.OnParticleCollision</c> spawns an impact effect at the contact
/// point — "a visible reaction with zero cook". Reading that handler in full shows why it must be
/// left alone: it does <c>Instantiate</c> ONCE PER COLLISION EVENT PER PREFAB, every frame, with no
/// cap and no rate limit. A hand parked in a burning brazier is an unbounded spawn loop, and this
/// project has paid for that shape twice already. The companion script settles it a second way:
/// <c>RFX4_CollisionPropertyDeactiavtion.Update</c> writes <c>collisionModule.enabled = false</c>
/// EVERY FRAME once its delay has passed — no guard flag, no early-out — so adopting one of those
/// systems is a write war that cannot be won, whatever one thinks of the spawning. So:
/// <list type="bullet">
///   <item>Systems that carry <c>RFX4_ParticleCollisionHandler</c> or
///   <c>RFX4_CollisionPropertyDeactiavtion</c> are SKIPPED ENTIRELY. The second one also writes
///   <c>collision.enabled</c> from its own <c>Update</c>, so adopting it would be a write war —
///   and the rule in this codebase is to concede the flag rather than fight for it.</item>
///   <item>Only systems whose collision module is ALREADY OFF are adopted, so the game's own
///   configured collision — wherever it exists — is never altered, and "restore" is exact.</item>
///   <item><c>sendCollisionMessages</c> is forced FALSE on everything adopted. Belt and braces:
///   even if some other handler existed on a system this class touches, our colliders cannot make
///   it fire.</item>
/// </list>
/// The effect is therefore purely kinematic — particles are pushed, slowed and swirled — and it
/// cannot spawn a single object.</para>
///
/// <para><b>THE SAME RULE, EXTENDED TO THE EXTERNAL FORCES MODULE (ModBuild 431).</b> "Only touch
/// a module the game leaves off" now has a second module to answer for, and the two cases are
/// handled differently on purpose:
/// <list type="bullet">
///   <item>External forces OFF (the overwhelming majority): the module is switched on with
///   <c>influenceFilter = List</c> and a list holding exactly the hand fields for this effect's
///   class. An explicit list, never a layer mask — Unity's default filter is a LAYER MASK set to
///   Everything, and "scoped to our own layer" is not a thing a mask set to Everything can be
///   scoped by.</item>
///   <item>External forces ALREADY ON: the game is using this module and its configuration is not
///   ours to replace. The takeover is ADDITIVE instead — the filter becomes
///   <c>LayerMaskAndList</c> (a union, so whatever the game's mask selected it still selects) and
///   our fields are APPENDED to whatever list was there. Nothing the game had is removed, and the
///   effect still reacts to a hand rather than being silently refused. It is NAMED IN THE LOG, once
///   per effect, together with the module's <c>multiplier</c> — because that multiplier is the
///   game's and we leave it alone, so on those systems the hand's push is scaled by a number this
///   class did not choose. That is a small visible imperfection in preference to a silent
///   alteration of somebody else's settings.</item>
/// </list>
/// Both paths capture the module UNCONDITIONALLY and restore it field for field — enabled,
/// multiplier, filter, mask and the influence list itself, entry by entry — for the same reason the
/// collision capture is unconditional: a held system can be re-graded while it is held, so "we did
/// not write it this time" is not a property a restore may lean on.</para>
///
/// <para><b>WHAT A FORCE FIELD CANNOT BE SCOPED AGAINST, said out loud.</b> A particle system that
/// opts into external forces BY LAYER MASK with the mask at Everything is reachable by every force
/// field in the scene, including ours, whether this class adopted it or not — no layer choice can
/// prevent that, because Everything includes every layer. The bound on it is therefore a different
/// one and it is enforced in <see cref="VfxFlowField.Idle"/>: a field for a class no hand is
/// currently holding carries ZERO in every one of its force terms. So the only systems that can
/// ever feel a non-zero hand field are the ones a hand is holding, plus the explicitly logged
/// population above that already uses external forces and happens to be within a palm's reach of a
/// hand holding an effect of the same class.</para>
///
/// <para>THE MASK NAMES ONE LAYER, and it is the mod's own. The probes sit on
/// <see cref="VRLayers.ModLayer"/> — the first unnamed layer, so the game never authored anything
/// on it — and <c>collidesWith</c> is exactly that layer's bit. Nothing about this depends on the
/// "Ignore Raycast" convention holding for particle queries, which is not a question this machine
/// can answer. Other mod-owned colliders on that layer would also deflect particles; in practice
/// the mod's interaction colliders are TRIGGERS, which particle collision ignores, so the probes
/// are what particles meet.</para>
///
/// <para>EFFECTS ON FIGURES ARE INCLUDED, and nothing had to be added for that (user, 2026-08-29:
/// "Gilt 3 auch für Partikel die von dem Figuren kommen?"). A character's aura, a monster's smoke,
/// the wash of a cast — <c>CastEffectsSMB</c> spawns those as ordinary particle systems on and
/// around the actors, and this class adopts by DISTANCE, not by ownership. That is the deliberate
/// difference from <see cref="SceneClothHands"/>, which must skip actor cloth because
/// <c>FigureClothHands</c> owns it and two writers on one authored array corrupt each other. No
/// second owner exists for a particle system's collision module, so there is nothing to yield to
/// — and a figure carried in the hand brings its aura with it, right where the other hand is.</para>
///
/// <para><b>WHY THAT PARAGRAPH WAS STILL ONLY A PROMISE AFTER THE FIRST HARDWARE RUN.</b> The user,
/// 2026-09-05: "es gibt viele Charactere die Effekte um sich herum haben — aber bisher reagieren
/// nur die Klammotten auf die Berührung der Hände." The ModBuild 429 log says exactly that, and it
/// names four separate defects — three PROVEN by the log, one read straight out of this file:
/// <list type="number">
///   <item>ONE system was adopted in the entire session and it was <c>'Waypoint_Path'</c> with
///   <b>0 live particles</b>. A system with no particles in it cannot produce a visible reaction,
///   so the feature had never once done anything a player could see.</item>
///   <item>The two effects a hand actually reached were REFUSED BY THE DENSITY CAP:
///   <c>'MeshEmitterBits (1)'</c> at 998 live particles and <c>'MeshEmitterBits'</c> at 985,
///   against a cap of 900. The cap was refusing precisely the effects worth feeling.</item>
///   <item>The census could not show any of it. It logged only while <c>_scansTimed &lt; 3</c>, and
///   all three of those scans read "0 adoptable of 0 found" — those lines sit between the menu rig
///   build and the main-menu video, i.e. all three were spent in the MAIN MENU before a scenario's
///   effects existed. Every later scan was silent, so nobody had ever seen the real population.
///   (The unrelated <c>[Perf] PARTICLES</c> line reads "0 system(s)" in the same log for the same
///   reason; defect 2 proves the scene was not empty.)</item>
///   <item>The cadence could not hold at all: the early-out read <c>now &lt; _nextScanAt
///   <b>&amp;&amp; _scene.Count &gt; 0</b></c>, so a scene whose adoptable population was empty
///   re-ran <c>FindObjectsOfType</c> EVERY FRAME. That is also why the three timed scans burned out
///   in three consecutive frames and why their cost fell 0.012 → 0.002 → 0.001 ms across them.
///   FindObjectsOfType is this project's default performance suspect and it had an unguarded
///   per-frame lane here.</item>
/// </list>
/// A fifth defect was in <c>Adopt</c> and had never printed a single character: a system whose
/// collision module the game had already switched on is refused with NO log line. It is a named
/// census population now. A sixth was in the adoption loop's own comment — it claimed "nearest
/// first" while walking the registry in <c>FindObjectsOfType</c> order, so which effects a hand got
/// was decided by the order Unity happened to return them in.</para>
///
/// <para><b>DENSE EFFECTS ARE PAID FOR DIFFERENTLY, NOT REFUSED.</b> The old cap existed because
/// <c>ParticleSystemCollisionQuality.High</c> raycasts PER PARTICLE PER FRAME, and the answer to
/// that is not a bigger cap — it is to stop buying per-particle accuracy on an effect made of a
/// thousand specks. Unity's <c>Medium</c> and <c>Low</c> resolve particles against a CACHED set of
/// raycasts (a voxel grid of collision planes) rather than raycasting each particle, so their cost
/// stops scaling with the particle count. Adoption is therefore GRADED: a thin effect keeps
/// <c>High</c>, a dense one is adopted at <c>Medium</c> or <c>Low</c>, and only something
/// pathological is refused. The chosen quality is printed with every adoption and the voxel grid is
/// sized from the palm sphere so the cache is fine enough to contain a hand — WHICH IS THE ONE
/// THING ONLY HARDWARE CAN CONFIRM, and it is marked as the open question on that line. The
/// previous revision of this file asserted the opposite ("Medium and Low collide against a cached
/// plane set, which a moving hand is not in") without ever having measured it, and that assertion
/// is what the 900 cap was defending.</para>
///
/// <para>WHAT IS SKIPPED IS NAMED, NOT COUNTED. The scan line reports how many systems exist, how
/// many are adoptable, how many are skipped and BY WHICH TERM (game-managed collision script, the
/// game's own collision already on, over the hard ceiling, inactive), how many sit on figures with
/// the first several NAMED and their live particle counts and bands, and — per hand — how many
/// systems were in reach against how many were actually held, with the refusals by term, so "the
/// cap was full" is visible instead of invisible. It is CHANGE-GATED with a floor: it prints when
/// the population materially changes and at least every <see cref="CensusFloorSeconds"/> seconds,
/// so it can neither flood a log nor go silent for a session the way the three-scan version did.
/// </para>
///
/// <para>COST IS MEASURED, NOT ASSERTED. The registry sweep is timed on EVERY scan (not on the
/// first three). This class's own per-frame work is timed and reported as a mean over the census
/// window. And the frame time is split into the frames in which at least one system was held and
/// the frames in which none was — an UPPER BOUND on what the feature costs, because those two
/// populations are not matched (a hand is inside an effect exactly when that effect is also on
/// screen and busy). What no stopwatch in this file can see is Unity's own collision work inside
/// the particle simulation; the instrument that decides that is the hardware <c>[Perf] SPLIT</c>
/// line, read against the held load this class prints.</para>
///
/// <para>PURELY LOCAL, LIKE THE CURTAINS. Particle effects were never on the wire, so every player
/// disturbs their own copy of the room and nothing can diverge. No wire field, no sync, no
/// authority — MP-safe by construction.</para>
/// </summary>
internal static class SceneVfxHands
{
    /// <summary>Palm sphere radius in REAL metres — a whole hand's worth of obstruction. Every
    /// radius here is multiplied by the hand's world scale at use time, the mod's standing
    /// convention for a real length under a scaled rig.</summary>
    private const float PalmRadiusRealMeters = 0.045f;

    /// <summary>Fingertip sphere. Smaller, so a finger drawn through a plume cuts a finger-sized
    /// channel rather than a fist-sized one.</summary>
    private const float TipRadiusRealMeters = 0.014f;

    /// <summary>How close the hand must get to a system's drawn bounds before it is adopted.
    ///
    /// <para>RE-DERIVED rather than trusted because its name ends in "Meters" — this project has
    /// shipped a bound named <c>…Meters</c> that was clamped against a WORLD-unit product at 198x
    /// rig scale. The ModBuild 429 log fixes the scenario rig at <b>9.05 world units per real
    /// metre</b> ([WallSegmentFade]: "In real metres at rig scale 9.05 wu per metre"), in a board
    /// volume x -13.0..20.2 by z -8.2..25.0 whose walls stand 4.09 wu = 0.45 m tall. So 0.30 real
    /// metres is 2.7 world units there: two thirds of a wall's height, several hexes across, and
    /// far larger than the aura on a single figure it has to find. The reach is NOT what kept the
    /// character effects out and it is left alone. The census now prints the live
    /// palm-to-nearest-effect distance in BOTH units beside this number, so the next hardware run
    /// can contradict that arithmetic instead of being asked to trust it.</para></summary>
    private const float ReachRealMeters = 0.30f;

    /// <summary>Release hysteresis — leaving costs 1.5x the reach, so a hand hovering on the
    /// boundary cannot adopt and restore the same system every other frame.</summary>
    private const float ReleaseFactor = 1.5f;

    /// <summary>Below this fraction of HandsVfxWakeSpeed, the hand is not going anywhere and the
    /// wake keeps the direction it already had. A tenth of "full push" is a hand at about 6 cm/s,
    /// which is slower than a hand a player believes is still. The term this replaces was a
    /// 1e-4 world-unit epsilon, i.e. "not exactly frozen" — see the comment at its use site.
    /// </summary>
    private const float DirectionGateFraction = 0.1f;

    /// <summary>HOW A LARGE EFFECT IS DEMOTED BEHIND A SMALL ONE THE HAND IS ACTUALLY AT, and it is
    /// the whole answer to "eine Interaktion mit Rauch, Funken etc. von Fackeln in der Map gibt es
    /// bisher nicht".
    ///
    /// <para>THE MEASUREMENT FIRST. The ModBuild 431 census refuses, by name and by term:
    /// <c>nearest refused: 'p_fire_torch (10)' by CapFull</c>, <c>'p_fire_torch (8)' by CapFull</c>,
    /// <c>'PrimeAltar_FX' by CapFull</c>, <c>'ElemEmbers' by CapFull</c>. The torches were adopted
    /// by the sweep — the scan line says 201 adoptable of 202 and 0 of them on figures, so the
    /// user's premise that only characters react is inverted — and then the six slots were spent
    /// before the torch was reached. WHAT SPENT THEM is readable in the same lines: the systems
    /// that survived selection are 'P_SewerFog', 'MeshEmitterFog', 'Fog (3)', 'Cloud (4)'. Room
    /// fog. And a room fog's DRAWN BOUNDS CONTAIN THE HAND, so its palm-to-bounds distance is
    /// exactly 0.0 — the smallest number there is. "Nearest first" handed all six slots to the
    /// systems the hand happens to be standing inside before it ever considered a torch ten
    /// centimetres away.</para>
    ///
    /// <para>SO DISTANCE-TO-BOUNDS CANNOT ORDER THE THINGS IT TIES. Once two candidates both read
    /// zero, the term that separates them is how far the hand is from the EMITTER — where the
    /// particles are actually born. This weight is small enough that it only ever breaks such a
    /// tie (a fog whose emitter is five metres away is demoted by 0.25 real m; a torch whose
    /// emitter is ten centimetres away by 0.005) and it is dimensionless, so it needs no rig
    /// scale. It changes the ORDER only: eligibility is still the true palm-to-bounds distance
    /// against the reach, so the fog is still adopted — after the fire.</para>
    ///
    /// <para>THE CAP OF SIX STAYS. A brazier is flame + smoke + embers + haze, which is four, and
    /// six was already two of those. Raising it would buy the frame time to hold more fog.</para>
    /// </summary>
    private const float EmitterTieWeight = 0.05f;

    /// <summary>Systems one hand may hold at once.
    ///
    /// <para>SIX, NOT THREE. The user's subject is "viele Charactere die Effekte um sich herum
    /// haben", and one character with an aura, a cast wash and ground dust is ALREADY three — so at
    /// three, a hand between two characters was silently full before it reached the second one, and
    /// nothing anywhere said so. Six is two such characters. It bounds the WORST CASE rather than
    /// describing a budget that is normally spent, and the scan line now prints in-reach against
    /// held per hand, so a cap that bites is readable on the first hardware run instead of being
    /// inferred from silence.</para></summary>
    private const int MaxSystemsPerHand = 6;

    /// <summary>Up to this many live particles, a system is collided at
    /// <c>ParticleSystemCollisionQuality.High</c> — the mode that raycasts PER PARTICLE and is the
    /// only one that consults colliders exactly.
    ///
    /// <para>400 is the value this feature shipped with before it was raised to 900 in the same
    /// change that brought figures into scope: raised, that is, without a measurement, which is the
    /// move that produced the cap the hardware log then tripped over. It is put back because this
    /// number's only remaining job is to say where per-particle accuracy stops being worth buying —
    /// everything above it is now adopted at a cheaper quality instead of being dropped, so being
    /// wrong in either direction costs accuracy on a medium effect or a little frame time, never a
    /// silently missing reaction. The census prints the band each system landed in and the frame
    /// split that decides whether it was affordable.</para></summary>
    private const int HighQualityMaxParticles = 400;

    /// <summary>Up to this many live particles, a system is collided at
    /// <c>ParticleSystemCollisionQuality.Medium</c>; above it, <c>Low</c>.
    ///
    /// <para>DERIVED FROM THE LOG: the two effects a hand actually reached in the ModBuild 429
    /// session carried 985 and 998 live particles. 4000 is four times that, so the effects this
    /// user is complaining about sit well inside the middle band, with headroom for a denser
    /// scenario than the one that was measured and nowhere near a refusal.</para></summary>
    private const int MediumQualityMaxParticles = 4000;

    /// <summary>The HARD CEILING: a system with more live particles than this is not adopted at any
    /// quality, and says so by name.
    ///
    /// <para>20000 is twenty times the ~1000-particle effects the log measured, and that distance
    /// is the point: this guards against something pathological (a scene-wide weather system, a
    /// runaway emitter), not against a character's aura. Even the approximate qualities do
    /// per-particle bookkeeping against the cached planes, so SOME ceiling has to exist; this one
    /// sits where nothing yet observed can reach it. A held system that grows past it is released
    /// and restored on the same frame.</para></summary>
    private const int MaxParticlesToAdopt = 20000;

    /// <summary>Collision shapes the approximate qualities may consider. Unity's approximate modes
    /// build their plane cache from a bounded set of shapes and ignore the excess, so this must not
    /// be so small that our own probes fall out of it: the mod puts exactly FOUR non-trigger
    /// colliders on the mod layer (a palm and a tip per hand) and <c>collidesWith</c> names that
    /// layer alone, so 16 is four times the population that can possibly qualify. Below Unity's
    /// default of 256 because every shape considered is work.</summary>
    private const int ApproximateCollisionShapes = 16;

    /// <summary>Registry refresh, in seconds. FindObjectsOfType is the project's default suspect for
    /// a per-frame cost that grows with the scene; this runs it on a cadence and never in a frame
    /// path — the <c>&amp;&amp; _scene.Count &gt; 0</c> clause that used to defeat that cadence in
    /// an empty scene is gone.
    ///
    /// <para>ONE SECOND, DOWN FROM THREE, and the reason is the subject: a cast wash lives a second
    /// or two, so a three-second cadence can let an entire effect be born and die between two
    /// scans, never entering the registry and never being adoptable at all. At one second, an
    /// effect that lasts a second appears in at least one scan. The cost of that is not asserted:
    /// the sweep is timed on EVERY scan now and the figure is printed in the census line.</para>
    /// </summary>
    private const float RescanSeconds = 1f;

    /// <summary>The census may not go quiet for longer than this even when nothing changes. The
    /// three-scan version went silent for an entire session; a change-gated line over a stable
    /// population would do the same thing by a different route.</summary>
    private const float CensusFloorSeconds = 30f;

    /// <summary>HOW FAR THE RIG SCALE MAY DRIFT BEFORE EVERY HELD SYSTEM IS RE-CLASSIFIED, as a
    /// ratio either way. Ten per cent.
    ///
    /// <para><b>THE DEFECT, AND IT IS MEASURED RATHER THAN SUSPECTED.</b> Exactly one of the
    /// classifier's six terms depends on the room: <c>DiameterMetres</c>, which is an authored
    /// world size divided by the live rig scale, tested against
    /// <c>VfxFlow.BulkDiameterMetres</c> = 0.10 real m. THE RIG SCALE IS A LIVE PLAYER-DRIVEN
    /// ZOOM — WorldGrab pivots on the hand midpoint — and the ModBuild 431 census walks it from
    /// 23.49 down to 2.14 wu per real metre inside one session: 23.49, 15.49, 7.42, 3.72, 8.46,
    /// 3.32, 7.16, 2.14. So the same asset lands on both sides of the bulk line depending on
    /// nothing about the effect. Five assets in that log do, and each pair recovers the SAME
    /// authored world size to within 0.9 %, which is what proves it is the zoom and not two
    /// different instances: 'distort' Motes 0.043 m at 23.49 and Flame 0.269 m at 3.72 (1.010 and
    /// 1.001 world units); 'Fog (3)' Flame 0.215 at 3.72 and Motes 0.095 at 8.46 (0.800, 0.804);
    /// 'ElemGust' Motes 0.036 at 23.49 and Smoke 0.256 at 3.32 (0.846, 0.850); 'ElemEmbers' Motes
    /// 0.049 at 23.49 and Smoke 0.542 at 2.14 (1.151, 1.160); 'HexHighlight(Clone)' Motes 0.043 at
    /// 23.49 and Smoke 0.269 at 3.72 (1.010, 1.001).</para>
    ///
    /// <para><b>THE NUMBER IS DERIVED FROM THE CLASSIFIER'S OWN BOUNDARY, NOT FROM THE SCALE.</b>
    /// A scale drift of a ratio r multiplies every measured diameter by 1/r, so a verdict can only
    /// be stale for a system whose diameter is within a factor r of the 0.10 m bulk line. At r =
    /// 1.10 that band is 0.091 m to 0.110 m — nine millimetres wide, around a line ten centimetres
    /// from zero. A system inside that band is genuinely marginal and either verdict is
    /// defensible; a system anywhere else cannot be misclassified by a drift this small. An epsilon
    /// on the scale would have said nothing about any of that, which is the whole reason this
    /// constant is expressed against the boundary.</para>
    ///
    /// <para>THE COST IS BOUNDED BY THE CAP, NOT BY THE ZOOM. A re-classification walks the held
    /// systems, of which there are at most twelve (two hands, six each), and a continuous zoom from
    /// 23.49 to 2.14 crosses this threshold about twenty-five times in total. Twenty-five walks of
    /// twelve systems over a whole zoom gesture is not a per-frame path and is not measured as one.
    /// </para></summary>
    private const float ReclassifyScaleRatio = 1.10f;

    // HOW A PARTICLE THAT ACTUALLY TOUCHES THE HAND BEHAVES. Not how the effect reacts — that is
    // the force fields — but what happens in the one case the fields cannot cover: a particle on
    // a trajectory straight through the palm. ModBuild 430 shipped bounce 0.35 / dampen 0.45 /
    // lifetime loss 0.10 and those three numbers ARE the report ("weicht super schnell unnatürlich
    // zurück"): a third of the speed returned along the normal is a ball off a bat, and a tenth of
    // a life removed on contact means the cloud thins where a hand has been.
    //
    // So: the bounce is a DIAL, near zero by default (a particle slides along the hand and the
    // vortex takes it), the dampen is PER CLASS (VfxFlowProfile.Dampen — a spark keeps more of its
    // speed than a smoke puff does), and the lifetime loss is zero everywhere except flame, where a
    // trace of it is what makes the carved pocket read as a pocket. Nothing here is a feel in its
    // own right any more; all three exist so the backstop does not contradict the flow.
    private static float BounceNow() => HandsConfig.HandsVfxBounceSafe();

    private static readonly Probe Left = new(HandSide.Left);
    private static readonly Probe Right = new(HandSide.Right);

    /// <summary>One adoptable system plus the two things a per-frame path would otherwise re-fetch:
    /// its renderer (for the DRAWN bounds) and whether it belongs to a figure. Caching the renderer
    /// is not a micro-optimisation — <c>GetComponent</c> per candidate per hand per frame is
    /// exactly the shape this project keeps finding at the top of its frame cost.</summary>
    private readonly struct Entry
    {
        internal readonly ParticleSystem Ps;
        internal readonly ParticleSystemRenderer? Renderer;
        internal readonly bool OnFigure;

        /// <summary>Which of the five force profiles this effect gets, decided on the registry
        /// sweep from what the system itself does (see <see cref="VfxFlow.Classify"/>). Carried on
        /// the entry so the census can report the class distribution of the whole adoptable
        /// population without measuring anything twice.</summary>
        internal readonly VfxFlowClass Flow;

        /// <summary>The term that decided <see cref="Flow"/>, in words. A string LITERAL out of the
        /// classifier, so holding it costs a reference and no allocation.</summary>
        internal readonly string FlowTerm;

        internal readonly VfxTraits Traits;

        internal Entry(ParticleSystem ps, ParticleSystemRenderer? renderer, bool onFigure,
                       VfxFlowClass flow, string flowTerm, in VfxTraits traits)
        {
            Ps = ps;
            Renderer = renderer;
            OnFigure = onFigure;
            Flow = flow;
            FlowTerm = flowTerm;
            Traits = traits;
        }
    }

    private static readonly List<Entry> _scene = new(32);

    /// <summary>Palm-to-bounds distance per registry entry, one entry per adoptable system. Sized on
    /// the rescan and reused every frame so the per-frame selection allocates nothing.</summary>
    private static float[] _distance = new float[32];

    /// <summary>The SELECTION ORDER key per registry entry — palm-to-bounds distance plus a small
    /// share of palm-to-emitter distance, see <see cref="EmitterTieWeight"/>. Kept apart from
    /// <see cref="_distance"/> because that array answers "is this in reach", which is a question
    /// about the drawn effect, and this one answers "which of the things in reach did the hand
    /// come here for", which is a different question that the same number cannot answer once
    /// several candidates read zero. Sized with its twin on the rescan; never allocated per frame.
    /// </summary>
    private static float[] _rank = new float[32];

    private static float _nextScanAt;

    /// <summary>The AUTHORED collision settings, captured once per system on the first adoption by
    /// either hand and written back verbatim when the last hand lets go. Keyed by the system
    /// because both hands can be in the same plume and there is one authored setting to restore.
    /// </summary>
    private static readonly Dictionary<ParticleSystem, Captured> _original = new(8);

    /// <summary>The quality this class currently has APPLIED to each adopted system, so a system
    /// whose particle count climbs out of its band (a cast wash ramping from nothing to a thousand
    /// specks) is re-graded rather than left at per-particle High for the rest of its life.
    /// </summary>
    private static readonly Dictionary<ParticleSystem, ParticleSystemCollisionQuality> _applied =
        new(8);

    /// <summary>The force profile each ADOPTED system was put in, so the per-frame path can find a
    /// held system's class without re-measuring it and without depending on it still being in the
    /// registry (a held system whose emitter went inactive for one scan must not silently change
    /// class). Same lifetime as <see cref="_original"/>: written on adoption, dropped on restore.
    /// </summary>
    private static readonly Dictionary<ParticleSystem, VfxFlowClass> _flowClass = new(8);

    /// <summary>THE DISPLACEMENT BOUND FOR EACH ADOPTED SYSTEM, in real metres per second — the
    /// fastest a hand field may ever get one of its particles moving, computed once at adoption
    /// from that system's own measured particle size and lifetime (see
    /// <see cref="VfxFlow.SpeedCapRealPerSecond"/>).
    ///
    /// <para>STORED RATHER THAN RECOMPUTED, for the same reason <see cref="_flowClass"/> is: a held
    /// system that drops out of the registry for one scan must not silently lose its ceiling. Same
    /// lifetime as <see cref="_original"/>: written on adoption, dropped on restore.</para>
    /// </summary>
    private static readonly Dictionary<ParticleSystem, float> _driftCap = new(8);

    /// <summary>Which hand fields a system's influence LIST currently names, as a small packed
    /// signature: bit 0 = the left hand's fields are listed, bit 1 = the right hand's, and the
    /// class shifted up by two. Rebuilding a list is cheap but it is not free and it is not
    /// idempotent-looking in a profiler, so it happens only when this signature changes — i.e. when
    /// a hand joins or leaves, never on the frames in between.</summary>
    private static readonly Dictionary<ParticleSystem, int> _influence = new(8);

    /// <summary>Which generation of the COLLISION FEEL (the bounce dial plus the class's dampen and
    /// lifetime loss) each held system currently carries. The dials are live-tunable, so a value
    /// the user changes mid-session has to reach the systems a hand is already holding; a
    /// generation counter does that with one integer compare per held system per frame instead of
    /// three native property writes.</summary>
    private static readonly Dictionary<ParticleSystem, int> _feelAt = new(8);

    private static int _feelGeneration;
    private static float _feelBounce = float.NaN;

    /// <summary>The rig scale as of the last frame a hand ticked, read by the registry sweep — the
    /// one term in <see cref="VfxFlow.Measure"/> that needs to know how big the room is. Sampled
    /// BEFORE the sweep in <see cref="Tick"/> rather than after, so the sweep never classifies
    /// against a scale from a different frame.</summary>
    private static float _rigScale = 1f;

    /// <summary>Has a TRACKED HAND ever reported a scale into <see cref="_rigScale"/>? The sweep
    /// refuses to classify until it has — see <see cref="Rescan"/>.
    ///
    /// <para>THIS GUARD CANNOT FIRE TODAY AND IS NOT DECORATION. <see cref="Tick"/> returns through
    /// <c>Clear()</c> before it reaches the sampling line whenever neither hand is tracked, and it
    /// samples before calling <see cref="Rescan"/> — so the 1.0 seed above is, as things stand,
    /// unreachable, and every one of the 33 verdicts in the ModBuild 431 log was taken at a real
    /// measured scale (23.49 down to 2.14). The flag exists so that stays true under a refactor
    /// that moves either line, because the failure it would cause is silent: a sweep at scale 1.0
    /// reads every particle as its raw world size, which puts the whole room above the 0.10 m bulk
    /// line at once and gives every effect in the game the Flame or Smoke profile. A guard whose
    /// premise is currently true is still the thing that notices when it stops being.</para>
    /// </summary>
    private static bool _rigScaleSampled;

    /// <summary>The scale <see cref="_flowClass"/> and <see cref="_driftCap"/> were last decided
    /// at, for the drift test in <see cref="RefreshScaleGeneration"/>. Seeded NaN so the first
    /// sample is a change by definition rather than by comparison against a made-up number.
    /// </summary>
    private static float _classifiedAtScale = float.NaN;

    private static bool _loggedFirstAdopt;

    private readonly struct Captured
    {
        internal readonly bool Enabled;
        internal readonly ParticleSystemCollisionType Type;
        internal readonly ParticleSystemCollisionMode Mode;
        internal readonly ParticleSystemCollisionQuality Quality;
        internal readonly LayerMask CollidesWith;
        internal readonly bool SendMessages;
        internal readonly ParticleSystem.MinMaxCurve Bounce;
        internal readonly ParticleSystem.MinMaxCurve Dampen;
        internal readonly ParticleSystem.MinMaxCurve LifetimeLoss;
        internal readonly float VoxelSize;
        internal readonly int MaxCollisionShapes;

        // ---- the external forces module (ModBuild 431) ---------------------------------------
        // Captured on exactly the same terms as the collision module above: unconditionally, every
        // field, including the influence LIST itself entry by entry. A list is the one thing here
        // that cannot be restored from a scalar, so it is the one thing here that allocates — once,
        // on the first adoption of a system, never on a frame path, and only when the game had put
        // something in it (which is almost never).
        internal readonly bool ForcesEnabled;
        internal readonly float ForcesMultiplier;
        internal readonly ParticleSystemGameObjectFilter ForcesFilter;
        internal readonly LayerMask ForcesMask;
        internal readonly ParticleSystemForceField[]? ForcesInfluences;

        internal Captured(ParticleSystem.CollisionModule c, ParticleSystem.ExternalForcesModule f)
        {
            Enabled = c.enabled;
            Type = c.type;
            Mode = c.mode;
            Quality = c.quality;
            CollidesWith = c.collidesWith;
            SendMessages = c.sendCollisionMessages;
            Bounce = c.bounce;
            Dampen = c.dampen;
            LifetimeLoss = c.lifetimeLoss;
            // The two fields the APPROXIMATE qualities read. Captured unconditionally even though
            // they are only written in the Medium/Low bands: a system can be re-graded while it is
            // held, so "we did not write it this time" is not a property the restore may lean on.
            VoxelSize = c.voxelSize;
            MaxCollisionShapes = c.maxCollisionShapes;

            ForcesEnabled = f.enabled;
            ForcesMultiplier = f.multiplier;
            ForcesFilter = f.influenceFilter;
            ForcesMask = f.influenceMask;
            int influences = f.influenceCount;
            if (influences > 0)
            {
                var list = new ParticleSystemForceField[influences];
                for (int i = 0; i < influences; i++)
                    list[i] = f.GetInfluence(i);
                ForcesInfluences = list;
            }
            else
            {
                ForcesInfluences = null;
            }
        }

        internal void RestoreTo(ParticleSystem.CollisionModule c,
                                ParticleSystem.ExternalForcesModule f)
        {
            // Order matters only in that `enabled` goes last: the other writes are inert while the
            // module is off, and leaving it off until the settings are back means no frame can
            // ever be simulated with a half-restored configuration.
            c.type = Type;
            c.mode = Mode;
            c.quality = Quality;
            c.collidesWith = CollidesWith;
            c.sendCollisionMessages = SendMessages;
            c.bounce = Bounce;
            c.dampen = Dampen;
            c.lifetimeLoss = LifetimeLoss;
            c.voxelSize = VoxelSize;
            c.maxCollisionShapes = MaxCollisionShapes;
            c.enabled = Enabled;

            // The list is rebuilt from scratch rather than diffed: RemoveInfluence shifts the
            // indices under a loop, and a restore is not the place to be clever about it. A field
            // destroyed while it was listed (a hot reload) comes back null and is simply not
            // re-added, which is the only sane thing to do with a reference to nothing.
            f.RemoveAllInfluences();
            if (ForcesInfluences != null)
            {
                for (int i = 0; i < ForcesInfluences.Length; i++)
                {
                    ParticleSystemForceField? had = ForcesInfluences[i];
                    if (had != null)
                        f.AddInfluence(had);
                }
            }
            f.influenceFilter = ForcesFilter;
            f.influenceMask = ForcesMask;
            f.multiplier = ForcesMultiplier;
            f.enabled = ForcesEnabled;
        }
    }

    /// <summary>The feature switch ([Hands] HandsDisturbVfx); false before the config is bound —
    /// the same defensive shape <see cref="SceneClothHands.Enabled"/> uses, and for the same
    /// reason: this is reached from a per-frame path that runs before and after the config's
    /// lifetime.</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                return HandsConfig.HandsDisturbVfx != null && HandsConfig.HandsDisturbVfx.Value;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Per-frame step, called from <see cref="HandsDriver"/> under its own TickGuard.
    /// Strict no-op with the feature off or no hand tracked.</summary>
    internal static void Tick()
    {
        if (!Enabled)
        {
            Clear();
            return;
        }

        VRHand? left = VRHands.Left;
        VRHand? right = VRHands.Right;
        if (!((left != null && left.IsTracked) || (right != null && right.IsTracked)))
        {
            Clear();
            return;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        // THE RIG SCALE IS SAMPLED BEFORE THE SWEEP, not read out of the probes afterwards. The
        // sweep classifies, one of the classifier's six terms is a length in real metres, and a
        // scale taken from the probes would be the PREVIOUS frame's — which is harmless in a
        // scenario and is a factor of fifty on the frame the map room opens.
        if (left != null && left.IsTracked)
            _rigScale = Mathf.Max(left.WorldScale, 1e-4f);
        else if (right != null && right.IsTracked)
            _rigScale = Mathf.Max(right.WorldScale, 1e-4f);
        _rigScaleSampled = true;
        RefreshFeelGeneration();
        // A HELD SYSTEM'S VERDICT IS THE ONE THAT CAN GO STALE, and it goes stale against the ZOOM.
        // The registry re-measures the whole adoptable population every second at the live scale
        // already, so an UNHELD system's class has always followed the room; what never moved is
        // the class stamped into _flowClass on the frame a hand took hold, and the ceiling in
        // _driftCap beside it, both of which carry a scale-dependent diameter. See
        // ReclassifyScaleRatio for the five assets in the ModBuild 431 log that straddle the bulk
        // line on the zoom alone. This runs BEFORE the sweep and before either hand ticks, so a
        // system re-classified this frame is adopted, driven and listed under its new class in the
        // same frame rather than one behind.
        RefreshScaleGeneration();
        bool scanned = Rescan();
        Left.Tick(left);
        Right.Tick(right);
        SyncInfluences();
        _tickTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
        _tickFrames++;

        // The frame-time split. Time.unscaledDeltaTime read here is the PREVIOUS frame's length, so
        // each sample lags its own frame by one; over a window of hundreds of frames what matters
        // is which population a frame falls into, and that is what this decides.
        float ms = Time.unscaledDeltaTime * 1000f;
        if (Left.HeldCount + Right.HeldCount > 0)
        {
            _msHeld += ms;
            _framesHeld++;
        }
        else
        {
            _msIdle += ms;
            _framesIdle++;
        }

        // THE CENSUS RUNS LAST, and that ordering is load-bearing rather than tidy: its per-hand
        // clause reports what each hand had in reach and what it held, and both are decided by the
        // two Tick calls above. Emitted from inside Rescan — where the previous revision's line
        // lived — the very first line of a session would report two hands that had never run, i.e.
        // "0 in reach" for a scene that had just been found to be full of effects. It is also
        // outside the stopwatch above, so the instrument is not charged to the mechanism.
        if (scanned)
            EmitScanLine(Time.unscaledTime);
    }

    /// <summary>Module teardown / scene change / hot reload: authored settings back FIRST, then the
    /// probes die. A system left pointing at a destroyed collider is a system that collides against
    /// nothing forever.</summary>
    internal static void Shutdown()
    {
        Clear();
        Left.Destroy();
        Right.Destroy();
        _scene.Clear();
        _applied.Clear();
        _flowClass.Clear();
        _influence.Clear();
        _feelAt.Clear();
        _feelGeneration = 0;
        _feelBounce = float.NaN;
        _rigScale = 1f;
        // BOTH OF THESE GO BACK WITH THE SCALE, and the pairing is the point: _rigScale returning
        // to its 1.0 seed without _rigScaleSampled returning to false would leave the guard
        // believing a hand had reported that 1.0, which is the one reading it exists to refuse.
        _rigScaleSampled = false;
        _classifiedAtScale = float.NaN;
        _nextScanAt = 0f;
        _loggedFirstAdopt = false;
        _noScaleNamed = false;
        _tooDense.Clear();
        _gameOwned.Clear();
        _gameForces.Clear();
        _flowNamed.Clear();
        _reclassified.Clear();
        _adoptedAt.Clear();
        _scansRun = 0;
        _scansSinceLine = 0;
        _lastSignature = 0;
        _nextFloorAt = 0f;
        ResetCostWindow();
    }

    private static void Clear()
    {
        Left.Release();
        Right.Release();
        RestoreUnheld();
    }

    /// <summary>Write the authored settings back to every system neither hand is inside any more.
    /// </summary>
    private static void RestoreUnheld()
    {
        if (_original.Count == 0)
            return;
        List<ParticleSystem>? drop = null;
        foreach (KeyValuePair<ParticleSystem, Captured> pair in _original)
        {
            ParticleSystem ps = pair.Key;
            if (ps != null && (Left.Holds(ps) || Right.Holds(ps)))
                continue;
            if (ps != null)
                pair.Value.RestoreTo(ps.collision, ps.externalForces);
            (drop ??= new List<ParticleSystem>(4)).Add(ps!);
        }
        if (drop == null)
            return;
        for (int i = 0; i < drop.Count; i++)
        {
            _original.Remove(drop[i]);
            _applied.Remove(drop[i]);
            _flowClass.Remove(drop[i]);
            _driftCap.Remove(drop[i]);
            _influence.Remove(drop[i]);
            _feelAt.Remove(drop[i]);
        }
    }

    /// <summary>Which collision quality a system with this many live particles is adopted at. High
    /// raycasts per particle per frame; Medium and Low resolve against a cached set of raycasts, so
    /// their cost stops scaling with the particle count. Every threshold's derivation lives on the
    /// constant it names.</summary>
    private static ParticleSystemCollisionQuality QualityFor(int live)
        => live <= HighQualityMaxParticles ? ParticleSystemCollisionQuality.High
         : live <= MediumQualityMaxParticles ? ParticleSystemCollisionQuality.Medium
         : ParticleSystemCollisionQuality.Low;

    /// <summary>Capture-once, then apply. Returns false when the system must not be touched.</summary>
    private static bool Adopt(ParticleSystem ps, int live, float scale, in Entry entry)
    {
        if (!_original.ContainsKey(ps))
        {
            ParticleSystem.CollisionModule capture = ps.collision;
            if (capture.enabled)
                return false;   // the game configured this one; it is not ours to change
            ParticleSystem.ExternalForcesModule forces = ps.externalForces;
            if (forces.enabled)
                NoteGameOwnedForces(ps, forces.multiplier, forces.influenceCount,
                                    forces.influenceFilter);
            _original[ps] = new Captured(capture, forces);
        }
        _flowClass[ps] = entry.Flow;
        // THE CEILING IS COMPUTED HERE, FROM THIS SYSTEM'S OWN NUMBERS, AND ONCE. It cannot be a
        // per-frame read: the traits come off the registry entry, and a held system is allowed to
        // leave the registry. HandsVfxDriftMeters is live-tunable and this is the one place it is
        // sampled, so a change reaches an effect the next time a hand takes hold of it.
        _driftCap[ps] = VfxFlow.SpeedCapRealPerSecond(in entry.Traits, VfxFlowProfile.Of(entry.Flow),
                                                      HandsConfig.HandsVfxDriftMetersSafe());

        ParticleSystem.CollisionModule c = ps.collision;
        c.type = ParticleSystemCollisionType.World;
        c.mode = ParticleSystemCollisionMode.Collision3D;
        c.collidesWith = VRLayers.ModLayerMask;
        c.sendCollisionMessages = false;   // see the class doc: no handler may ever fire from us
        ApplyFeel(ps, c, entry.Flow);
        ApplyQuality(ps, c, live, scale);
        c.enabled = true;
        NoteFlowClass(ps, in entry);

        if (!_loggedFirstAdopt)
        {
            _loggedFirstAdopt = true;
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
            // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
            VRLog.Note("Hands", $"Hands disturb VFX: first system adopted ('{ps.name}', "
                + $"{ps.particleCount} live particles). Collision is switched on with "
                + $"collidesWith = the mod layer ({VRLayers.ModLayer}) and messages OFF, so "
                + "particles deflect off the hand and nothing can be spawned. Authored settings "
                + "are restored when the hand leaves. In the ModBuild 429 log this line named "
                + "'Waypoint_Path' with 0 live particles and was the ONLY adoption of the whole "
                + "session — a system with no particles in it cannot produce a reaction anybody "
                + "can see, so if this line still names an empty system, the VFX scan line below "
                + "is where what was in reach, and by which term it was refused, is written down. "
                + "SINCE ModBuild 431 COLLISION IS NO LONGER THE MECHANISM, only the backstop: the "
                + "reaction is carried by two ParticleSystemForceFields per hand per effect class "
                + "(palm and fingertip), which push, swirl and drag the effect rather than "
                + "reflecting it. That is why the bounce above is now near zero by default. A "
                + "force field consults no collider at all, so the wrap and the cling are the same "
                + "at every collision quality.");
        }
        return true;
    }

    /// <summary>Write the collision BACKSTOP's three feel fields for a system's class, but only
    /// when the generation says they can have gone stale. See <see cref="_feelAt"/>.</summary>
    private static void ApplyFeel(ParticleSystem ps, ParticleSystem.CollisionModule c,
                                  VfxFlowClass flow)
    {
        if (_feelAt.TryGetValue(ps, out int at) && at == _feelGeneration)
            return;
        _feelAt[ps] = _feelGeneration;
        VfxFlowProfile p = VfxFlowProfile.Of(flow);
        c.bounce = BounceNow();
        c.dampen = p.Dampen;
        c.lifetimeLoss = p.LifetimeLoss;
    }

    /// <summary>Re-classify every HELD system when the rig scale has drifted past
    /// <see cref="ReclassifyScaleRatio"/>. One float compare a frame in the common case.
    ///
    /// <para>WHY THE HELD SET AND NOT THE REGISTRY: the registry is rebuilt from scratch once a
    /// second and re-measures at the live scale as it goes, so an unheld system's verdict has never
    /// been able to go stale. What goes stale is the pair a hand STAMPS at adoption —
    /// <see cref="_flowClass"/>, which chooses the force profile, and <see cref="_driftCap"/>,
    /// whose numerator is a scale-dependent diameter — and those persist untouched for as long as
    /// the hand holds on, across any amount of zooming.</para>
    ///
    /// <para>THE CEILING IS REFRESHED EVEN WHEN THE CLASS DOES NOT MOVE, and that is not an
    /// oversight to tidy later. A particle that has doubled in real metres has doubled its own
    /// drift allowance without changing class at all, so a system whose verdict is stable still
    /// carries a ceiling computed for a room it is no longer in.</para></summary>
    private static void RefreshScaleGeneration()
    {
        // The NaN seed makes the first sample a change, which is what it is. `!=` and the ordered
        // comparisons below are all false against NaN, so this needs no separate "first time" flag
        // — the same trick RefreshFeelGeneration uses, for the same reason.
        if (_rigScale <= _classifiedAtScale * ReclassifyScaleRatio
            && _rigScale >= _classifiedAtScale / ReclassifyScaleRatio)
            return;
        float was = _classifiedAtScale;
        _classifiedAtScale = _rigScale;
        if (_original.Count == 0 || float.IsNaN(was))
            return;   // nothing is held, so there is no stamped verdict to go stale
        ReclassifyHeld(was);
    }

    /// <summary>Re-measure and re-decide every held system at the current scale. Walks
    /// <see cref="_original"/>, which holds at most twelve entries, and writes only into other
    /// dictionaries — so the enumerator is never invalidated.</summary>
    private static void ReclassifyHeld(float wasScale)
    {
        float driftCap = HandsConfig.HandsVfxDriftMetersSafe();
        foreach (KeyValuePair<ParticleSystem, Captured> pair in _original)
        {
            ParticleSystem ps = pair.Key;
            if (ps == null || !_flowClass.TryGetValue(ps, out VfxFlowClass was))
                continue;
            bool onFigure = ps.GetComponentInParent<ActorBehaviour>() != null;
            VfxTraits traits = VfxFlow.Measure(ps, onFigure, _rigScale);
            VfxFlowClass now = VfxFlow.Classify(in traits, out string term);
            // ALWAYS, class change or not — see the method doc above.
            _driftCap[ps] = VfxFlow.SpeedCapRealPerSecond(in traits, VfxFlowProfile.Of(now),
                                                          driftCap);
            if (now == was)
                continue;
            _flowClass[ps] = now;
            // The collision backstop's dampen and lifetime loss are PER CLASS, and ApplyFeel
            // early-outs on a generation match — so a system whose class moved has to be made to
            // look stale or it would keep the previous class's two numbers for as long as it is
            // held. The influence list needs no such nudge: its signature already carries the
            // class, so SyncInfluences rebuilds it on the same frame.
            _feelAt.Remove(ps);
            NoteReclassified(ps, was, now, term, in traits, wasScale);
        }
    }

    /// <summary>A verdict that MOVED says so, with the scale on both sides. This project lost six
    /// builds to a number that changed under it without a line, and a classification that silently
    /// swaps an effect's force profile mid-session is the same shape of defect.</summary>
    private static void NoteReclassified(ParticleSystem ps, VfxFlowClass was, VfxFlowClass now,
                                         string term, in VfxTraits traits, float wasScale)
    {
        // Keyed on the TRANSITION, not the name: a player zooming in and back out would otherwise
        // print a line per crossing for the rest of the session. There are five classes, so this
        // set is bounded at twenty entries per effect name and in practice at one or two.
        if (!_reclassified.Add(ps.name + "|" + was + ">" + now))
            return;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Hands", $"Hands disturb VFX: '{ps.name}' IS NO LONGER {was} — it is now {now}, "
            + $"because {term}. THE EFFECT DID NOT CHANGE; THE ROOM DID. This verdict was decided "
            + $"at a rig scale of {wasScale:F2} world units per real metre and has been re-decided "
            + $"at {_rigScale:F2}, a factor of {(_rigScale / Mathf.Max(wasScale, 1e-4f)):0.##}. "
            + "The rig scale is the live zoom, so exactly one of the six classification terms moves "
            + "with it: the particle's diameter in REAL metres, which is an authored world size "
            + $"divided by that scale and now reads {traits.DiameterMetres:F3} m (it was about "
            + $"{(traits.DiameterMetres * _rigScale / Mathf.Max(wasScale, 1e-4f)):F3} m before). "
            + "Everything else about this effect — its lifetime, how far it travels in its own "
            + "widths, its simulation space, whether a figure owns it — is scale-free and did not "
            + "move. RE-CLASSIFYING IS THE CORRECT ANSWER AND NOT A COMPROMISE: 'is a single "
            + "particle big enough to see the shape of' is a question about what the player's eye "
            + "is being shown, and the honest answer to it changes when the player zooms. What was "
            + "wrong before ModBuild 432 was that a hand STAMPED the answer at the moment it took "
            + "hold and never looked again, so the same asset carried two different force profiles "
            + "in one session with nothing anywhere saying so. The new force profile and the "
            + "re-derived drift ceiling are both in force from this frame; the influence list was "
            + "rebuilt with them. If a re-classification lands somewhere you can SEE it — an effect "
            + "visibly changing how it answers your hand as you zoom — the term to argue with is "
            + "the threshold this fired on and it is derived on SceneVfxHands.ReclassifyScaleRatio.");
    }

    /// <summary>The sweep refused to classify because no tracked hand had reported a scale yet.
    /// Named once: see <see cref="_rigScaleSampled"/> for why this cannot currently happen and why
    /// it is still worth a line if it ever does.</summary>
    private static void NoteNoScaleYet()
    {
        if (_noScaleNamed)
            return;
        _noScaleNamed = true;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Hands", "Hands disturb VFX: the registry sweep RAN WITHOUT A RIG SCALE and "
            + "classified nothing. No tracked hand had reported one, so the only scale available "
            + "was the 1.0 seed — and at 1.0 every particle measures its raw world size, which puts "
            + $"the entire room above the {VfxFlow.BulkDiameterMetres:0.##} real-metre bulk line "
            + "at once and would hand every "
            + "effect in the game the Flame or Smoke profile. The registry is therefore left EMPTY "
            + "for this scan, which is the safe state and not a degraded one: nothing is adopted, "
            + "no collision module is touched, no influence list names a hand field, and every "
            + "force field stays at zero in all seven of its terms, so effects behave exactly as "
            + "the unmodded game draws them. IF YOU ARE READING THIS LINE, SOMETHING MOVED: as "
            + "shipped, Tick returns before it reaches the sweep whenever neither hand is tracked "
            + "and samples the scale before calling it, so this is unreachable. Reported once.");
    }

    private static bool _noScaleNamed;

    private static readonly HashSet<string> _reclassified = new(8, StringComparer.Ordinal);

    /// <summary>Bump the feel generation when the live bounce dial has actually moved. One float
    /// compare a frame; every held system then re-reads its three fields exactly once.</summary>
    private static void RefreshFeelGeneration()
    {
        float bounce = BounceNow();
        // The NaN seed makes the first frame of a session a change, which is what it is: nothing
        // has been written yet. `!=` on NaN is true against everything including itself, so this
        // needs no separate "first time" flag.
        if (bounce == _feelBounce)
            return;
        _feelBounce = bounce;
        _feelGeneration++;
    }

    /// <summary>Point every adopted system's influence list at the fields of the hands that are
    /// currently holding it, and at nothing else.
    ///
    /// <para>RUNS AFTER BOTH HANDS HAVE TICKED, because "which hands hold this" is not answerable
    /// until they both have, and because the fields a list names have to EXIST — a probe builds a
    /// class's fields lazily, on the frame it first holds an effect of that class. It walks
    /// <see cref="_original"/>, which holds at most twelve entries (two hands, six each) and is a
    /// concrete Dictionary, so the enumerator is a struct and the walk allocates nothing.</para>
    /// </summary>
    private static void SyncInfluences()
    {
        if (_original.Count == 0)
            return;
        foreach (KeyValuePair<ParticleSystem, Captured> pair in _original)
        {
            ParticleSystem ps = pair.Key;
            if (ps == null)
                continue;
            bool leftHolds = Left.Holds(ps);
            bool rightHolds = Right.Holds(ps);
            if (!leftHolds && !rightHolds)
                continue;   // RestoreUnheld has it; it is about to be handed back untouched
            if (!_flowClass.TryGetValue(ps, out VfxFlowClass flow))
                continue;
            int signature = (leftHolds ? 1 : 0) | (rightHolds ? 2 : 0) | ((int)flow << 2);
            if (_influence.TryGetValue(ps, out int had) && had == signature)
                continue;
            _influence[ps] = signature;

            ParticleSystem.ExternalForcesModule f = ps.externalForces;
            bool gameOwns = pair.Value.ForcesEnabled;
            if (!gameOwns)
            {
                // The ordinary path: the module was off, so the whole of it is ours for as long as
                // we hold it. An explicit LIST and not a layer mask — Unity's default mask is
                // Everything, which is not a scope.
                f.RemoveAllInfluences();
                f.multiplier = 1f;
                f.influenceFilter = ParticleSystemGameObjectFilter.List;
            }
            else
            {
                // The game is using this module. Rebuild its list as (what the game had) + (ours),
                // and widen the filter to the UNION so whatever its mask selected still is. Nothing
                // the game configured is removed and its multiplier is left exactly alone — see the
                // class doc, and NoteGameOwnedForces, which names every system that lands here.
                f.RemoveAllInfluences();
                ParticleSystemForceField[]? had2 = pair.Value.ForcesInfluences;
                if (had2 != null)
                {
                    for (int i = 0; i < had2.Length; i++)
                    {
                        if (had2[i] != null)
                            f.AddInfluence(had2[i]);
                    }
                }
                f.influenceFilter = ParticleSystemGameObjectFilter.LayerMaskAndList;
            }
            if (leftHolds)
                Left.AddFieldsTo(f, flow);
            if (rightHolds)
                Right.AddFieldsTo(f, flow);
            f.enabled = true;
        }
    }

    /// <summary>Set the collision quality for a system's CURRENT particle count, plus the two fields
    /// the approximate qualities read. Writes only on a band change, so a held system costs two
    /// integer compares a frame rather than a native property write.</summary>
    private static void ApplyQuality(ParticleSystem ps, ParticleSystem.CollisionModule c,
                                     int live, float scale)
    {
        ParticleSystemCollisionQuality want = QualityFor(live);
        if (_applied.TryGetValue(ps, out ParticleSystemCollisionQuality have) && have == want)
            return;
        _applied[ps] = want;
        c.quality = want;
        float voxel = 0f;
        if (want != ParticleSystemCollisionQuality.High)
        {
            // THE VOXEL GRID HAS TO BE ABLE TO CONTAIN A HAND. The approximate qualities resolve
            // particles against a cache of collision planes held in a voxel grid measured in WORLD
            // units, so a voxel coarser than the obstruction cannot represent it. One palm radius
            // per voxel puts two voxels across the palm sphere. In numbers: Unity's default is
            // 0.5 world units, and at the ModBuild 429 scenario's 9.05 wu per real metre the palm
            // is 0.045 x 9.05 = 0.41 wu — so the DEFAULT grid is just coarser than the hand, which
            // is the reading that makes this write necessary rather than decorative. It follows the
            // rig scale for the same reason every radius in this file does.
            voxel = Mathf.Max(0.01f, PalmRadiusRealMeters * scale);
            c.voxelSize = voxel;
            c.maxCollisionShapes = ApproximateCollisionShapes;
        }
        NoteAdoptedQuality(ps, want, live, voxel);
    }

    /// <summary>A dropped effect names itself once. A cap that drops silently reads, in a log, as
    /// "there was nothing there" — which is the one thing it does not mean.</summary>
    private static void NoteTooDense(ParticleSystem ps, int live)
    {
        if (!_tooDense.Add(ps.name))
            return;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Hands", $"Hands disturb VFX: '{ps.name}' was NOT adopted — "
            + $"{live} live particles is over the {MaxParticlesToAdopt} cap that keeps "
            + "per-particle collision off the frame budget. Reported once per effect name; if this "
            + "names an effect you wanted to feel, the cap is the dial. NOTE THE SIZE OF THAT "
            + "NUMBER: since ModBuild 430 it is a HARD CEILING against something pathological — "
            + "twenty times the ~1000-particle effects the ModBuild 429 log measured — and "
            + "everything below it is adopted at a CHEAPER COLLISION QUALITY instead of being "
            + $"refused (<= {HighQualityMaxParticles} High, <= {MediumQualityMaxParticles} Medium, "
            + "above that Low). An ordinary effect reaching this line means something is emitting "
            + "far more than any character effect in this game does.");
    }

    /// <summary>The refusal that had never printed a single character. A system whose collision
    /// module the game itself switched on is left alone by design — see the class doc — but until
    /// now it was left alone in complete silence, which in a log is indistinguishable from not being
    /// there at all.</summary>
    private static void NoteGameOwnedCollision(ParticleSystem ps)
    {
        if (!_gameOwned.Add(ps.name))
            return;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Hands", $"Hands disturb VFX: '{ps.name}' was NOT adopted — the GAME already "
            + "has this system's collision module switched ON, and this feature only ever adopts a "
            + "module that is off, so the game's own configuration is never altered and the "
            + "restore stays exact. Reported once per effect name. This refusal was completely "
            + "SILENT before ModBuild 430: if a character effect you wanted to feel is named here, "
            + "the term to argue with is that rule, not the density cap and not the reach.");
    }

    /// <summary>One line per effect per quality band, so "which quality did it actually pick" is
    /// answerable from the log rather than inferred from a particle count.</summary>
    private static void NoteAdoptedQuality(ParticleSystem ps, ParticleSystemCollisionQuality q,
                                           int live, float voxel)
    {
        if (!_adoptedAt.Add(ps.name + "|" + q))
            return;
        string grid = q == ParticleSystemCollisionQuality.High
            ? "per-particle raycasts, no plane cache"
            : $"plane cache on a {voxel:F3} world-unit voxel grid, <= {ApproximateCollisionShapes} shapes";
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Hands", $"Hands disturb VFX: adopted '{ps.name}' at collision quality {q} "
            + $"({live} live particles; bands <= {HighQualityMaxParticles} High, <= "
            + $"{MediumQualityMaxParticles} Medium, <= {MaxParticlesToAdopt} Low, above that "
            + $"refused) — {grid}. High raycasts PER PARTICLE PER FRAME; Medium and Low resolve "
            + "against a cached set of raycasts instead, which is the whole reason a "
            + "~1000-particle effect is now slowed down rather than dropped. THE OPEN HARDWARE "
            + "QUESTION IS WHETHER MEDIUM/LOW STILL DEFLECTS OFF A HAND: if smoke passes straight "
            + "through your hand while this line says Medium or Low for that effect, the "
            + "approximate plane cache is the term that failed — not the reach, not the cap. "
            + "THAT QUESTION IS RETIRED AS OF ModBuild 431 AND THIS PARAGRAPH IS KEPT ONLY SO THE "
            + "RETIREMENT IS READABLE AGAINST IT: the reaction is no longer a deflection. It is "
            + "carried by force fields, which are volumes and consult no collider, so the wrap, "
            + "the cling and the waft are identical at every quality here. What this band can "
            + "still cost is how crisply a particle STOPS at the skin, which is a second-order "
            + "look and not the interaction.");
    }

    /// <summary>Every adopted effect names its class, the term that decided it and the numbers that
    /// term was measured from — once. This is the line that makes the per-effect design ARGUABLE:
    /// "the fire behaves like smoke" stops being a report about a feeling and becomes a report
    /// about a threshold, with the measurement printed beside it.</summary>
    private static void NoteFlowClass(ParticleSystem ps, in Entry e)
    {
        if (!_flowNamed.Add(ps.name + "|" + e.Flow))
            return;
        VfxFlowProfile p = VfxFlowProfile.Of(e.Flow);
        float driftCap = HandsConfig.HandsVfxDriftMetersSafe();
        float drift = VfxFlow.DriftMetres(in e.Traits, in p, driftCap);
        float cap = VfxFlow.SpeedCapRealPerSecond(in e.Traits, in p, driftCap);
        // The widths the budget was actually taken against: the measured diameter, unless that
        // read zero (a size driven entirely by a curve) and the floor stood in for it. Naming
        // which one was used is the difference between a number and a number somebody can check.
        string widths = e.Traits.DiameterMetres >= 0.01f
            ? $"{e.Traits.DiameterMetres:F3} m"
            : "0.010 m (its measured size read 0, so the floor stood in)";
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Hands", $"Hands disturb VFX: '{ps.name}' is treated as {e.Flow} because "
            + $"{e.FlowTerm}. Measured: lifetime {e.Traits.LifeSeconds:F2} s, travel "
            + $"{e.Traits.TravelWidths:F1} of its own widths per life, particle "
            + $"{e.Traits.DiameterMetres:F3} real m across, gravity multiplier "
            + $"{e.Traits.GravityModifier:F2}, simulation space "
            + $"{(e.Traits.LocalSpace ? "Local" : "World/Custom")}, on a figure: "
            + $"{(e.Traits.OnFigure ? "yes" : "no")} ({VfxFlow.Thresholds()}). MEASURED AT A RIG "
            + $"SCALE OF {_rigScale:F2} WORLD UNITS PER REAL METRE, which matters to exactly one of "
            + "those six terms and therefore has to be printed beside them: the diameter is an "
            + "authored world size divided by that number, and the rig scale is the live zoom. "
            + $"This particle's authored size is {(e.Traits.DiameterMetres * _rigScale):F3} world "
            + $"units, so it crosses the {VfxFlow.BulkDiameterMetres:0.##} real-metre bulk line "
            + $"at a rig scale of "
            + $"{(e.Traits.DiameterMetres * _rigScale / VfxFlow.BulkDiameterMetres):0.##} — zoom "
            + $"past that and this "
            + "verdict is re-decided, and a line saying so is printed. It therefore gets "
            + $"drag {p.Drag:0.##}, vortex {p.Vortex:0.##}, rotation attraction {p.Attract:0.##}, "
            + $"push {p.Push:0.##} m/s2 at full hand speed, outward {p.Repel:0.##} m/s2, field "
            + $"radius x{p.Radius:0.##}, collision dampen {p.Dampen:0.##} and lifetime loss "
            + $"{p.LifetimeLoss:0.##}. CLASSIFICATION USES NO ASSET NAME AND NO SHADER NAME — only "
            + "the six numbers above — so if this verdict is wrong, the term named in this line is "
            + "the thing to argue with, and the profile is a set of config dials "
            + "([Hands] HandsVfxPushStrength / ClingStrength / CurlStrength / SettleSeconds / "
            + "ReachMeters / WakeSpeed / Bounce), not a rebuild. THE OUTWARD TERM CARRIES THE ONE "
            + "QUESTION NO READING OF THE API COULD SETTLE: it is written as a NEGATIVE force-field "
            + "gravity with the focus at the centre, which should push particles away from the "
            + "palm. If effects visibly collapse INTO your hand instead of parting around it, that "
            + "sign is inverted (VfxFlowField.Apply) and nothing else in this feature is "
            + "implicated — the push, the swirl and the cling are separate terms. THE DRAG AND "
            + "VORTEX MAGNITUDES ARE A FIRST GUESS, NOT A MEASUREMENT: Unity documents neither "
            + "term's units, so the ratios between the five profiles are the designed part and "
            + "these absolute numbers are what the first hardware round is for. Too much drag "
            + "freezes an effect in mid-air, which is as wrong as the bounce it replaces and "
            + "harder to name — HandsVfxClingStrength is the dial for it. "
            + $"SINCE ModBuild 432 IT ALSO GETS A CEILING, AND THIS EFFECT'S IS {drift:F3} REAL "
            + $"METRES, i.e. no hand field may ever get one of its particles above "
            + $"{cap:F3} real m/s. That is {p.Drift:0.##} of its own {widths} widths against the "
            + $"{HandsConfig.HandsVfxDriftMetersSafe():0.##} m HandsVfxDriftMeters cap, divided by "
            + $"its {e.Traits.LifeSeconds:F2} s lifetime, so a particle carried at the ceiling for "
            + "its WHOLE remaining life still ends up inside that distance of where it would have "
            + "been. THE CEILING EXISTS BECAUSE A FORCE FIELD NEVER TAKES BACK WHAT IT GAVE: a "
            + "particle leaves the field with whatever speed it picked up and coasts on it until "
            + "it dies, which on ModBuild 431's numbers was twelve metres per second for five "
            + "seconds on 'PrimeAltar_FX'. The bound is enforced through the field's DRAG (terminal "
            + "speed is acceleration divided by drag), which is why a class with almost none gets a "
            + "floor of 1.2 whatever the drag column above says. If this effect now feels nailed "
            + "in place, HandsVfxDriftMeters is the dial and this line is the arithmetic. "
            + "THE SECOND OPEN QUESTION ON THAT CEILING IS THE SWIRL: the vortex is clamped to it "
            + "directly and is now multiplied by the rig scale like the push and the outward term "
            + "were already (it was the only force in the feature that was not, so the same "
            + "authored swirl meant twenty times as much of a room at x9 as at x198). If effects "
            + "now merely SLOW DOWN near a hand and never wrap around it, that clamp is what ate "
            + "the wrap — HandsVfxCurlStrength cannot buy past it, HandsVfxDriftMeters can.");
    }

    /// <summary>A system whose EXTERNAL FORCES module the game already uses. Not a refusal — the
    /// takeover is additive there — but it is the one population whose reaction strength is scaled
    /// by a number this class did not choose, so it is named rather than merged into silence.
    /// </summary>
    private static void NoteGameOwnedForces(ParticleSystem ps, float multiplier, int influences,
                                            ParticleSystemGameObjectFilter filter)
    {
        if (!_gameForces.Add(ps.name))
            return;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Hands", $"Hands disturb VFX: '{ps.name}' ALREADY uses the external forces "
            + $"module (multiplier {multiplier:0.##}, filter {filter}, {influences} force field(s) "
            + "listed by the game). It is NOT refused: the hand's own fields are APPENDED to that "
            + "list and the filter is widened to LayerMaskAndList, which is a union — everything "
            + "the game selected it still selects, and nothing it configured is removed. The one "
            + "thing left alone is that multiplier, because it is the game's: on this effect the "
            + "hand's push, swirl and drag are all scaled by it, so if this effect reacts far more "
            + "or far less than the others, that number is the reason and no dial of ours will "
            + "even it out. Everything is restored field for field, list included, on release. "
            + "Reported once per effect name.");
    }

    private static readonly HashSet<string> _tooDense = new(8, StringComparer.Ordinal);
    private static readonly HashSet<string> _gameOwned = new(8, StringComparer.Ordinal);
    private static readonly HashSet<string> _gameForces = new(8, StringComparer.Ordinal);
    private static readonly HashSet<string> _flowNamed = new(8, StringComparer.Ordinal);
    private static readonly HashSet<string> _adoptedAt = new(8, StringComparer.Ordinal);

    // ---- census + cost state -----------------------------------------------------------------
    private static int _scansRun;
    private static int _scansSinceLine;
    private static int _lastSignature;
    private static float _nextFloorAt;
    private static double _lastScanMs;
    private static long _tickTicks;
    private static int _tickFrames;
    private static double _msHeld;
    private static double _msIdle;
    private static int _framesHeld;
    private static int _framesIdle;
    private static readonly System.Text.StringBuilder _line = new(768);

    // The scan's own tallies. Fields rather than eight arguments: written by Rescan (mechanism),
    // read by EmitScanLine (instrument).
    private static int _foundTotal;
    private static int _skipInactive;
    private static int _skipGameManaged;
    private static int _skipGameOwnedCollision;
    private static int _skipOverCeiling;
    private static int _onFigures;
    private static int _gameManagedOnFigures;
    private static int _bandHigh;
    private static int _bandMedium;
    private static int _bandLow;

    /// <summary>Adoptable systems per force profile, indexed by <see cref="VfxFlowClass"/>. Sized
    /// from <see cref="VfxFlow.ClassCount"/> so it cannot fall behind the enum.</summary>
    private static readonly int[] _flowCount = new int[VfxFlow.ClassCount];

    private static void ResetCostWindow()
    {
        _tickTicks = 0;
        _tickFrames = 0;
        _msHeld = 0d;
        _msIdle = 0d;
        _framesHeld = 0;
        _framesIdle = 0;
    }

    /// <summary>The cadence floor: a defensive clamp so a future dial or a zero constant cannot turn
    /// the registry sweep back into a per-frame one. The same shape <see cref="SceneClothHands"/>
    /// uses, and here it guards the exact defect the ModBuild 429 log exposed — a sweep that ran
    /// every frame because its early-out carried a second clause.</summary>
    private static float CadenceGuard(float seconds) => Mathf.Max(0.25f, seconds);

    /// <summary>Rebuild the adoptable registry on the cadence. Returns true on the frames it
    /// actually swept, so the caller can emit the census AFTER both hands have ticked.</summary>
    private static bool Rescan()
    {
        float now = Time.unscaledTime;
        // NO SECOND CLAUSE. The previous "&& _scene.Count > 0" meant an empty adoptable population
        // re-ran FindObjectsOfType EVERY FRAME — and, because the census was gated on a scan COUNT,
        // burned all three of its timed scans in three consecutive frames of the main menu.
        if (now < _nextScanAt)
            return false;
        _nextScanAt = now + CadenceGuard(RescanSeconds);
        // NO CLASSIFICATION WITHOUT A REAL SCALE. See _rigScaleSampled. Returning here leaves the
        // registry EMPTY, and an empty registry is the safe state rather than an unbounded one: a
        // system that is not in it is never a selection candidate, so it is never adopted, never
        // named on an influence list, and never has its collision module touched — and every force
        // field a hand carries is left at Idle(), which is zero in all seven force terms. An effect
        // the hand is standing in simply behaves exactly as the unmodded game draws it.
        if (!_rigScaleSampled)
        {
            NoteNoScaleYet();
            return false;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        ParticleSystem[] found = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
        _scene.Clear();
        _foundTotal = found.Length;
        _skipInactive = 0;
        _skipGameManaged = 0;
        _skipGameOwnedCollision = 0;
        _skipOverCeiling = 0;
        _onFigures = 0;
        _gameManagedOnFigures = 0;
        _bandHigh = 0;
        _bandMedium = 0;
        _bandLow = 0;
        for (int i = 0; i < _flowCount.Length; i++)
            _flowCount[i] = 0;
        for (int i = 0; i < found.Length; i++)
        {
            ParticleSystem ps = found[i];
            if (ps == null || !ps.gameObject.activeInHierarchy)
            {
                _skipInactive++;
                continue;
            }
            // "Does this effect belong to a figure?" is a RELATEDNESS question, which is the one
            // thing GetComponentInParent actually answers. It is a CENSUS term only — it changes
            // no decision below, because a figure's effects are adopted exactly like the room's.
            bool onFigure = ps.GetComponentInParent<ActorBehaviour>() != null;
            // The two RFX4 collision scripts are the game's own collision management. See the
            // class doc for why adopting one is both a spawn hazard and an unwinnable write war.
            if (HasGameCollisionScript(ps))
            {
                _skipGameManaged++;
                if (onFigure)
                    _gameManagedOnFigures++;
                continue;
            }
            // The refusal that used to happen silently inside Adopt, hoisted into the census so it
            // becomes a NAMED population instead of an absence. A system this class has already
            // adopted has its collision on because WE turned it on, so it must not land here.
            if (ps.collision.enabled && !_original.ContainsKey(ps))
            {
                _skipGameOwnedCollision++;
                continue;
            }
            if (ps.particleCount > MaxParticlesToAdopt)
            {
                _skipOverCeiling++;
                continue;
            }
            if (onFigure)
                _onFigures++;
            switch (QualityFor(ps.particleCount))
            {
                case ParticleSystemCollisionQuality.High: _bandHigh++; break;
                case ParticleSystemCollisionQuality.Medium: _bandMedium++; break;
                default: _bandLow++; break;
            }
            // CLASSIFY HERE, ON THE CADENCE, not on the adoption path. Six native property reads
            // per system per second is nothing, and doing it for the WHOLE adoptable population
            // rather than only for the systems a hand reaches is what lets the census report the
            // class distribution of the room — i.e. what lets somebody see that the game has nine
            // Motes systems and no Smoke at all before wondering why the smoke feels wrong.
            VfxTraits traits = VfxFlow.Measure(ps, onFigure, _rigScale);
            VfxFlowClass flow = VfxFlow.Classify(in traits, out string term);
            _flowCount[(int)flow]++;
            _scene.Add(new Entry(ps, ps.GetComponent<ParticleSystemRenderer>(), onFigure,
                                 flow, term, in traits));
        }
        if (_distance.Length < _scene.Count)
        {
            _distance = new float[Mathf.NextPowerOfTwo(_scene.Count)];
            _rank = new float[_distance.Length];
        }

        _lastScanMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d
                      / System.Diagnostics.Stopwatch.Frequency;
        _scansRun++;
        _scansSinceLine++;
        return true;
    }

    /// <summary>Change-gated census with a floor. It prints when the population MATERIALLY changes —
    /// the tallies, or the identity of the adoptable set — and at least every
    /// <see cref="CensusFloorSeconds"/> seconds regardless, so it can neither flood a log nor go
    /// silent for a whole session the way the "first three scans" version did.</summary>
    private static void EmitScanLine(float now)
    {
        int signature = _foundTotal * 397;
        signature ^= _scene.Count * 31;
        signature ^= _skipGameManaged * 17;
        signature ^= _skipGameOwnedCollision * 7;
        signature ^= _skipOverCeiling * 3;
        signature ^= _onFigures;
        for (int i = 0; i < _flowCount.Length; i++)
            signature = unchecked(signature * 13 + _flowCount[i]);
        for (int i = 0; i < _scene.Count; i++)
        {
            ParticleSystem ps = _scene[i].Ps;
            if (ps != null)
                signature = unchecked(signature * 31 + ps.GetInstanceID());
        }
        bool floorDue = now >= _nextFloorAt;
        if (signature == _lastSignature && !floorDue && _scansRun > 1)
            return;
        _lastSignature = signature;
        _nextFloorAt = now + CensusFloorSeconds;

        _line.Length = 0;
        _line.Append("VFX scan #").Append(_scansRun).Append(": ").Append(_scene.Count)
             .Append(" adoptable particle system(s) of ").Append(_foundTotal)
             .Append(" found — ").Append(_onFigures)
             .Append(" of them ON FIGURES (auras, casts, monster effects), which are adopted ")
             .Append("exactly like the room's. ").Append(_skipGameManaged)
             .Append(" skipped as game-managed collision (").Append(_gameManagedOnFigures)
             .Append(" of those on figures)");
        _line.Append(", ").Append(_skipGameOwnedCollision)
             .Append(" skipped because the GAME already has their collision module on, ")
             .Append(_skipOverCeiling).Append(" over the ").Append(MaxParticlesToAdopt)
             .Append("-particle hard ceiling, ").Append(_skipInactive).Append(" inactive.");
        _line.Append(" Adoptable by band: ").Append(_bandHigh).Append(" High, ")
             .Append(_bandMedium).Append(" Medium, ").Append(_bandLow)
             .Append(" Low (High raycasts per particle; Medium/Low resolve against a cached plane "
                   + "set, so a dense effect is graded down rather than dropped). SINCE ModBuild "
                   + "431 THAT BAND DECIDES NOTHING ABOUT THE FEEL: the reaction is carried by "
                   + "force fields, which consult no collider, so a Low-quality effect wraps and "
                   + "clings exactly like a High-quality one.");
        AppendFlowCounts();
        AppendFigureNames();
        Left.AppendReach(_line);
        Right.AppendReach(_line);
        AppendCost();
        _line.Append(" Scan took ").Append(_lastScanMs.ToString("F3"))
             .Append(" ms and runs every ").Append(RescanSeconds.ToString("0.#"))
             .Append(" s while a hand is tracked, never per frame (")
             .Append(_scansSinceLine)
             .Append(" scan(s) since the previous line — this line is change-gated with a ")
             .Append(CensusFloorSeconds.ToString("0.#")).Append(" s floor, so a run of identical "
                   + "scans is summarised by that count rather than reprinted).");
        _scansSinceLine = 0;
        ResetCostWindow();
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Hands", _line.ToString());
    }

    /// <summary>The room's effects by FORCE PROFILE — what the hands will actually do to what is
    /// there. A distribution rather than a list: "nine Motes and no Smoke at all" is the answer to
    /// "why does none of this feel like smoke", and it is not derivable from any other clause on
    /// this line.</summary>
    private static void AppendFlowCounts()
    {
        _line.Append(" By feel: ").Append(_flowCount[(int)VfxFlowClass.Smoke])
             .Append(" Smoke (wraps and settles back), ")
             .Append(_flowCount[(int)VfxFlowClass.Flame])
             .Append(" Flame (carved open, springs back, never blown sideways), ")
             .Append(_flowCount[(int)VfxFlowClass.Sparks]).Append(" Sparks (scatter and drift), ")
             .Append(_flowCount[(int)VfxFlowClass.Motes])
             .Append(" Motes (hang in the wake), ").Append(_flowCount[(int)VfxFlowClass.Aura])
             .Append(" Aura (near field disturbed, stays with its figure). Classified from what "
                   + "each system DOES — lifetime, travel in its own widths, real particle "
                   + "diameter, simulation space, actor ownership — and never from an asset or "
                   + "shader name; each adopted effect prints its own verdict and the term that "
                   + "decided it on the line above.");
    }

    /// <summary>Name the figure-owned effects, with their live counts and bands. A COUNT of effects
    /// on figures cannot tell the user whether the ones he means are in it; the names can.</summary>
    private static void AppendFigureNames()
    {
        int named = 0;
        for (int i = 0; i < _scene.Count && named < 8; i++)
        {
            Entry e = _scene[i];
            if (!e.OnFigure || e.Ps == null)
                continue;
            int live = e.Ps.particleCount;
            _line.Append(named == 0 ? " On figures: " : ", ").Append('\'').Append(e.Ps.name)
                 .Append("' (").Append(live).Append(" live, ").Append(QualityFor(live))
                 .Append(", feels like ").Append(e.Flow).Append(')');
            named++;
        }
        if (named > 0)
            _line.Append(named < _onFigures ? ", + " + (_onFigures - named) + " more." : ".");
    }

    /// <summary>The measured cost of this feature, in the only two terms this file can honestly
    /// observe. What it cannot observe is named as such rather than asserted away.</summary>
    private static void AppendCost()
    {
        double tickMs = _tickFrames > 0
            ? _tickTicks * 1000d / System.Diagnostics.Stopwatch.Frequency / _tickFrames
            : 0d;
        _line.Append(" COST, measured over the last ").Append(_tickFrames)
             .Append(" frame(s): this class's own per-frame work (registry sweep included) "
                   + "averaged ").Append(tickMs.ToString("F4")).Append(" ms/frame.");
        if (_framesHeld > 0 && _framesIdle > 0)
        {
            double held = _msHeld / _framesHeld;
            double idle = _msIdle / _framesIdle;
            _line.Append(" Frame time with at least one system held: ").Append(held.ToString("F2"))
                 .Append(" ms over ").Append(_framesHeld).Append(" frame(s), against ")
                 .Append(idle.ToString("F2")).Append(" ms over ").Append(_framesIdle)
                 .Append(" frame(s) with none held — a difference of ")
                 .Append((held - idle).ToString("+0.00;-0.00;0.00"))
                 .Append(" ms. THAT DIFFERENCE IS AN UPPER BOUND AND NOT THIS FEATURE'S COST: the "
                       + "two populations are not matched, because a hand is inside an effect "
                       + "exactly when that effect is also on screen and busy. Unity's own "
                       + "collision work happens inside the particle simulation, where no "
                       + "stopwatch in this class can see it; the instrument that decides that is "
                       + "[Perf] SPLIT, read against the bands this line prints.");
        }
        else
        {
            _line.Append(" No frame-time split yet (").Append(_framesHeld).Append(" held / ")
                 .Append(_framesIdle).Append(" idle frame(s) in this window) — both populations "
                       + "have to be non-empty before that comparison means anything.");
        }
    }

    /// <summary>Does this system carry either of the game's own collision scripts? Matched BY NAME
    /// because the mod does not reference the game's effect assembly types directly — the names are
    /// verified against <c>decompiled/GH.Runtime/RFX4_*.cs</c>, and a rename in a future game
    /// version fails SAFE: the system stops being skipped only if the script is gone too.</summary>
    private static bool HasGameCollisionScript(ParticleSystem ps)
    {
        MonoBehaviour[] behaviours = ps.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] == null)
                continue;
            string name = behaviours[i].GetType().Name;
            if (name == "RFX4_ParticleCollisionHandler" || name == "RFX4_CollisionPropertyDeactiavtion")
                return true;
        }
        return false;
    }

    /// <summary>Why a system that was IN REACH of a hand was not held by it.</summary>
    private enum Refusal
    {
        None,
        CapFull,
        OverCeiling,
        GameOwnedCollision,
    }

    /// <summary>One hand's presence in the effects: two spheres and the systems it is inside.</summary>
    private sealed class Probe
    {
        private readonly HandSide _side;
        private readonly List<ParticleSystem> _held = new(MaxSystemsPerHand);
        private GameObject? _root;
        private Transform? _palm;
        private Transform? _tip;
        private SphereCollider? _palmSphere;
        private SphereCollider? _tipSphere;

        /// <summary>One force field per effect class, on the palm and on the fingertip. Built
        /// LAZILY — a hand that only ever meets smoke builds two of these, not ten — and never torn
        /// down until the probe is, because building one costs a GameObject and the population of
        /// classes a hand meets in a room is stable.</summary>
        private readonly VfxFlowField[] _palmFields = NewFieldSet();
        private readonly VfxFlowField[] _tipFields = NewFieldSet();

        /// <summary>Which classes this hand is holding THIS frame, as a bit per class. Recomputed
        /// from the held list each frame (at most six dictionary lookups) rather than maintained,
        /// because a held system's class can only change by being re-adopted.</summary>
        private int _liveClasses;

        /// <summary>The smoothed wake: 0 = the hand is still, 1 = it is moving at
        /// HandsVfxWakeSpeed or faster. Rises INSTANTLY and falls over HandsVfxSettleSeconds —
        /// asymmetric on purpose. A push that ramped in would feel like the hand was late; a push
        /// that stopped dead when the hand did is exactly the unnatural snap the report was about,
        /// arriving from the other side.</summary>
        private float _wake;

        /// <summary>The direction the wake is flowing, world space, unit length. HELD while the
        /// wake decays, so a hand that stops still leaves the air moving the way it was going —
        /// which is the whole point of the settle time.</summary>
        private Vector3 _wakeDirection = Vector3.forward;

        /// <summary>THE HAND'S VELOCITY, SMOOTHED, in world units per second — and it is the fix
        /// for "man sieht wie es immer noch super ruckartig zurückweicht".
        ///
        /// <para>ModBuild 431 read <c>hand.PalmVelocity</c> RAW, once a frame, and used it for two
        /// separate things: the strength of the push and the AXIS the whole vortex turns about. A
        /// tracked hand's per-frame velocity is a noisy signal — it is a difference of two poses
        /// divided by a frame time — so both of those jittered at frame rate. The strength jitter is
        /// the snap; the axis jitter is worse, because a vortex whose axis reorients every frame
        /// drags each particle along a different arc each frame, which is exactly the thin curved
        /// white streaks the smoke video shows. One exponential filter, one time constant
        /// (HandsVfxWakeAttackSeconds), and both terms come off the smoothed vector.</para></summary>
        private Vector3 _velocity;

        /// <summary>The ATTACK ENVELOPE per effect class, 0..1. Rises over
        /// HandsVfxWakeAttackSeconds from the frame this hand first holds a system of that class,
        /// so the air fades in rather than switching on between two frames.
        ///
        /// <para>IT RISES ONLY, AND THAT IS DELIBERATE RATHER THAN LAZY. A fall would leave a field
        /// carrying force for a class no hand holds, which is the one safety property
        /// <see cref="VfxFlowField.Idle"/> exists to guarantee — see the class doc. A release ramp
        /// would also be invisible: the released system's influence list stops naming our fields on
        /// the same frame, so nothing would feel the decay. The snap on the way IN is the one a
        /// player can see, and it is the one that is ramped. Dropping to zero on release also means
        /// a hand that leaves and immediately re-enters starts the ramp again from nothing, which is
        /// the "never re-arm within one frame of releasing" rule for free.</para></summary>
        private readonly float[] _gain = new float[VfxFlow.ClassCount];

        /// <summary>The strictest displacement bound among the systems this hand holds of each
        /// class, in real metres per second. A field is shared by every held system of its class,
        /// so the bound it carries has to be the smallest of theirs — a fog and a torch flame in
        /// one hand must not let the fog's ceiling be spent on the flame or the flame's on the fog.
        /// Recomputed each frame from <see cref="_driftCap"/>; sized once, never allocated.</summary>
        private readonly float[] _classCap = new float[VfxFlow.ClassCount];

        private static VfxFlowField[] NewFieldSet()
        {
            var set = new VfxFlowField[VfxFlow.ClassCount];
            for (int i = 0; i < set.Length; i++)
                set[i] = new VfxFlowField();
            return set;
        }

        // Census terms: written by Tick (mechanism), read only by AppendReach (instrument).
        private int _inReach;
        private int _refusedCapFull;
        private int _refusedCeiling;
        private int _refusedGameOwned;
        private float _nearestWorldUnits = float.NaN;
        private float _reachWorldUnits;
        private float _scale = 1f;
        private ParticleSystem? _nearest;
        private ParticleSystem? _refusedExample;
        private Refusal _refusedTerm;

        internal Probe(HandSide side) => _side = side;

        internal bool Holds(ParticleSystem ps) => _held.Contains(ps);

        internal int HeldCount => _held.Count;

        internal void Tick(VRHand? hand)
        {
            if (hand == null || !hand.IsTracked || hand.Rig == null)
            {
                Release();
                return;
            }
            if (!Ensure())
                return;

            float scale = Mathf.Max(hand.WorldScale, 1e-4f);
            Transform palm = hand.Rig.PalmCenter != null ? hand.Rig.PalmCenter : hand.Rig.Root;
            Transform tip = hand.Rig.IndexTip != null ? hand.Rig.IndexTip : palm;
            _palm!.SetPositionAndRotation(palm.position, palm.rotation);
            _tip!.SetPositionAndRotation(tip.position, tip.rotation);
            // Real metres times the live rig scale, read AT USE TIME: the same radius means the
            // same apparent size in a x198 map room and a x4 scenario.
            _palmSphere!.radius = PalmRadiusRealMeters * scale;
            _tipSphere!.radius = TipRadiusRealMeters * scale;

            float reach = ReachRealMeters * scale;
            float release = reach * ReleaseFactor;
            Vector3 at = palm.position;
            _scale = scale;
            _reachWorldUnits = reach;

            // Drop what we have left, using the wider release radius (hysteresis) — and RE-GRADE
            // what we keep, because a cast wash adopted at 40 particles and now running at 900
            // would otherwise stay on per-particle raycasts for the rest of its life.
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                ParticleSystem ps = _held[i];
                if (ps == null || !ps.gameObject.activeInHierarchy)
                {
                    _held.RemoveAt(i);
                    continue;
                }
                int liveHeld = ps.particleCount;
                if (liveHeld > MaxParticlesToAdopt || Distance(ps, at) > release)
                {
                    _held.RemoveAt(i);
                    continue;
                }
                ParticleSystem.CollisionModule heldCollision = ps.collision;
                ApplyQuality(ps, heldCollision, liveHeld, scale);
                // The bounce dial is live-tunable, so a value the user changes while a hand is
                // already inside an effect has to reach that effect. Costs one integer compare a
                // frame per held system when nothing has moved (see _feelAt).
                if (_flowClass.TryGetValue(ps, out VfxFlowClass heldFlow))
                    ApplyFeel(ps, heldCollision, heldFlow);
            }

            _inReach = 0;
            _refusedCapFull = 0;
            _refusedCeiling = 0;
            _refusedGameOwned = 0;
            _nearest = null;
            _nearestWorldUnits = float.NaN;
            _refusedExample = null;
            _refusedTerm = Refusal.None;

            // ONE bounds read per adoptable system per hand per frame, into a preallocated buffer;
            // the selection passes below then work on floats alone. The old loop called
            // GetComponent and recomputed the bounds for every candidate it looked at.
            int n = _scene.Count;
            for (int i = 0; i < n; i++)
            {
                Entry e = _scene[i];
                if (e.Ps == null || !e.Ps.gameObject.activeInHierarchy)
                {
                    _distance[i] = float.MaxValue;
                    _rank[i] = float.MaxValue;
                    continue;
                }
                float d = Distance(in e, at);
                _distance[i] = d;
                if (float.IsNaN(_nearestWorldUnits) || d < _nearestWorldUnits)
                {
                    _nearestWorldUnits = d;
                    _nearest = e.Ps;
                }
                // THE RANK IS NOT THE DISTANCE, and see EmitterTieWeight for the census lines that
                // forced the split. The emitter term is computed ONLY inside the reach, which is
                // both where it can change an outcome and the only place its transform read is
                // affordable: the in-reach population is a dozen systems, the registry is two
                // hundred. Outside the reach the rank IS the distance, so nothing else moves.
                _rank[i] = d;
                if (d <= reach)
                {
                    _inReach++;
                    _rank[i] = d + Vector3.Distance(e.Ps.transform.position, at) * EmitterTieWeight;
                }
                if (_held.Contains(e.Ps))
                {
                    _distance[i] = float.MaxValue;   // already ours: not a candidate, not a refusal
                    _rank[i] = float.MaxValue;
                }
            }

            // NEAREST FIRST, and this time actually. The old loop walked the registry in
            // FindObjectsOfType order and stopped at the cap, so which systems a hand got was
            // decided by the order Unity happened to return them in rather than by which ones the
            // hand was in. Selection is O(cap x n) with cap = 6 over a list of tens.
            while (_held.Count < MaxSystemsPerHand)
            {
                int best = -1;
                float bestRank = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    // ELIGIBILITY IS THE TRUE DISTANCE, ORDER IS THE RANK. Keeping those two apart
                    // is what lets the emitter tie-break demote a fog behind a torch without also
                    // pushing the fog out of reach — a hand standing inside a fog is inside it, and
                    // the reach test still says so.
                    if (_distance[i] <= reach && _rank[i] < bestRank)
                    {
                        bestRank = _rank[i];
                        best = i;
                    }
                }
                if (best < 0)
                    break;
                _distance[best] = float.MaxValue;
                _rank[best] = float.MaxValue;
                Entry chosen = _scene[best];
                ParticleSystem ps = chosen.Ps;
                if (ps == null)
                    continue;
                int live = ps.particleCount;
                if (live > MaxParticlesToAdopt)
                {
                    _refusedCeiling++;
                    NoteRefusal(ps, Refusal.OverCeiling);
                    NoteTooDense(ps, live);
                    continue;
                }
                if (!Adopt(ps, live, scale, in chosen))
                {
                    _refusedGameOwned++;
                    NoteRefusal(ps, Refusal.GameOwnedCollision);
                    NoteGameOwnedCollision(ps);
                    continue;
                }
                _held.Add(ps);
            }

            // Whatever is STILL in reach once the cap has filled is a system this hand is inside
            // and is not disturbing. That was the invisible failure: the cap could be full and
            // nothing anywhere said so.
            if (_held.Count >= MaxSystemsPerHand)
            {
                // "NEAREST REFUSED" USED TO BE THE FIRST IN REGISTRY ORDER. The claim on
                // NoteRefusal was that selection runs nearest-first so the first refusal is the
                // nearest one — true of the loop above, false of this one, which walks the registry
                // in FindObjectsOfType order. The token stays; the mechanism is corrected to make
                // it true, by carrying the best rank rather than taking whatever came first.
                int nearest = -1;
                float nearestRank = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (_distance[i] > reach)
                        continue;
                    _refusedCapFull++;
                    if (_rank[i] < nearestRank)
                    {
                        nearestRank = _rank[i];
                        nearest = i;
                    }
                }
                if (nearest >= 0)
                {
                    ParticleSystem? capped = _scene[nearest].Ps;
                    if (capped != null)
                        NoteRefusal(capped, Refusal.CapFull);
                }
            }

            UpdateFlow(hand, palm, tip, scale);
            RestoreUnheld();
        }

        /// <summary>THE AIR AROUND THIS HAND, once per frame.
        ///
        /// <para>Three decisions live here and each is visible to the player:</para>
        /// <list type="number">
        ///   <item>THE WAKE. The hand's own palm velocity — SMOOTHED FIRST, see <see cref="_velocity"/>
        ///   — converted to real metres per second and divided by the speed dial, is how hard the
        ///   hand is currently wafting. It rises over HandsVfxWakeAttackSeconds and decays over the
        ///   three times longer HandsVfxSettleSeconds, and its DIRECTION is held through the decay
        ///   — so a hand that sweeps past and stops leaves the smoke still travelling the way it
        ///   was pushed, instead of stopping with the hand. ModBuild 431 rose INSTANTLY here, from
        ///   an unsmoothed velocity, and that pair is the "ruckartig" in the report.</item>
        ///   <item>THE PALM AXIS is the direction of travel. Unity turns particles about a force
        ///   field's own up direction, so putting Y along the motion makes the vortex a ring shed
        ///   BEHIND a moving palm — which is what a hand waved through smoke actually leaves.</item>
        ///   <item>THE FINGERTIP AXIS is the finger itself, taken as the palm-to-tip vector rather
        ///   than from a named bone axis: which local axis points along a finger is a property of
        ///   whichever hand mesh is worn, and a vector between two joints is a property of the
        ///   hand. Particles then spiral ALONG the finger, which is the "bzw dem Finger" half of
        ///   the request.</item>
        /// </list>
        /// <para>Every class this hand is NOT holding is zeroed rather than left carrying last
        /// frame's numbers — see <see cref="VfxFlowField.Idle"/> and the class doc's paragraph on
        /// what a force field cannot be scoped against.</para>
        ///
        /// <para>A FOURTH DECISION IS THE DISPLACEMENT BOUND, and it is not a feel: <see
        /// cref="_classCap"/> collects the strictest ceiling among the systems this hand holds of
        /// each class and hands it to the field in world units, where
        /// <see cref="VfxFlowField.Apply"/> spends an acceleration budget against it. Nothing here
        /// decides how far an effect may be carried; that was decided from the effect's own numbers
        /// on the frame it was adopted.</para></summary>
        private void UpdateFlow(VRHand hand, Transform palm, Transform tip, float scale)
        {
            float push = HandsConfig.HandsVfxPushStrengthSafe();
            float cling = HandsConfig.HandsVfxClingStrengthSafe();
            float curl = HandsConfig.HandsVfxCurlStrengthSafe();
            float settle = HandsConfig.HandsVfxSettleSecondsSafe();
            float radius = HandsConfig.HandsVfxReachMetersSafe() * scale;
            float attack = HandsConfig.HandsVfxWakeAttackSecondsSafe();
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);

            // SMOOTH THE VELOCITY VECTOR ITSELF, BEFORE ANYTHING IS DERIVED FROM IT. See _velocity:
            // ModBuild 431 took the strength AND the vortex axis off a raw per-frame hand velocity,
            // so both jittered at frame rate and the second of those is what stretched the wisps
            // into arcs. One exponential filter with a time constant the player owns, frame-rate
            // independent by construction (the exponent carries dt), which a raw Lerp factor is not.
            _velocity = attack <= 1e-3f
                ? hand.PalmVelocity
                : Vector3.Lerp(_velocity, hand.PalmVelocity, 1f - Mathf.Exp(-dt / attack));

            // PalmVelocity is world units per second and the rig scale is world units per real
            // metre, so the quotient is real metres per second — the unit the dial is written in,
            // and the one that means the same thing at every rig scale.
            float speedWorld = _velocity.magnitude;
            float speedReal = speedWorld / scale;
            float wakeSpeed = HandsConfig.HandsVfxWakeSpeedSafe();
            float target = Mathf.Clamp01(speedReal / wakeSpeed);
            // THE DIRECTION GATE IS A REAL SPEED, NOT AN EPSILON. The old test was `> 1e-4` world
            // units per second, which at any rig scale is "the hand is not exactly frozen" — so a
            // hand the player is holding still handed the vortex a new axis every frame, taken from
            // pure tracking noise. A tenth of the speed that means "full push" is a hand that is
            // actually going somewhere; below it the last real direction is kept.
            if (speedReal > wakeSpeed * DirectionGateFraction)
                _wakeDirection = _velocity / speedWorld;
            if (target >= _wake)
            {
                // RISING IS RAMPED NOW, AND THIS IS THE "RUCKARTIG" LINE. ModBuild 431 wrote
                // `_wake = target` on the rise — a step function from a noisy input, applied to the
                // whole effect at once. The old comment defended it ("a push that ramped in would
                // feel like the hand was late"), and the answer to that is a SHORT constant rather
                // than none: the attack is a fraction of the settle time, so the push still arrives
                // with the hand and stops arriving in a single frame.
                _wake = attack <= 1e-3f
                    ? target
                    : Mathf.Lerp(_wake, target, 1f - Mathf.Exp(-dt / attack));
            }
            else
            {
                // An exponential fall with a time constant the player owns. Frame-rate independent
                // by construction (the exponent carries dt), which a raw Lerp factor is not.
                _wake = settle <= 1e-3f
                    ? target
                    : Mathf.Lerp(_wake, target, 1f - Mathf.Exp(-dt / settle));
            }

            _liveClasses = 0;
            for (int c = 0; c < VfxFlow.ClassCount; c++)
                _classCap[c] = float.MaxValue;
            for (int i = 0; i < _held.Count; i++)
            {
                ParticleSystem ps = _held[i];
                if (ps == null || !_flowClass.TryGetValue(ps, out VfxFlowClass held))
                    continue;
                _liveClasses |= 1 << (int)held;
                // The strictest ceiling in the class wins — see _classCap. A system adopted before
                // this dictionary existed (there is no such path, but a null-safe read costs one
                // branch) contributes nothing rather than an unbounded ceiling.
                if (_driftCap.TryGetValue(ps, out float capReal) && capReal < _classCap[(int)held])
                    _classCap[(int)held] = capReal;
            }

            Vector3 palmAt = palm.position;
            Vector3 fingerAxis = tip.position - palmAt;
            // A fingertip that coincides with the palm (a rig with no index bone, so `tip` fell
            // back to `palm`) has no direction. Use the palm's own forward rather than a zero
            // vector, which FromToRotation would answer with an identity and a silently unrotated
            // field.
            fingerAxis = fingerAxis.sqrMagnitude > 1e-8f ? fingerAxis.normalized : palm.forward;

            for (int c = 0; c < VfxFlow.ClassCount; c++)
            {
                if ((_liveClasses & (1 << c)) == 0)
                {
                    // The envelope drops to zero the frame a class stops being held — see _gain for
                    // why this edge is a snap and the other one is a ramp.
                    _gain[c] = 0f;
                    _palmFields[c].Idle();
                    _tipFields[c].Idle();
                    continue;
                }
                VfxFlowProfile p = VfxFlowProfile.Of((VfxFlowClass)c);
                if (!EnsureFields(c))
                    continue;
                _gain[c] = attack <= 1e-3f
                    ? 1f
                    : Mathf.Lerp(_gain[c], 1f, 1f - Mathf.Exp(-dt / attack));
                float r = radius * p.Radius;
                // Real metres per second times world units per real metre. A ceiling of MaxValue
                // means no held system of this class reported one, in which case the field is
                // unbounded exactly as it was before ModBuild 432 rather than silently dead.
                float capWorld = _classCap[c] >= float.MaxValue
                    ? float.MaxValue
                    : _classCap[c] * scale;
                _palmFields[c].Apply(in p, palmAt, _wakeDirection, _wakeDirection, r, _wake, scale,
                                     capWorld, _gain[c], push, cling, curl, tip: false);
                _tipFields[c].Apply(in p, tip.position, fingerAxis, _wakeDirection,
                                    r * VfxFlowField.TipRadiusFraction, _wake, scale,
                                    capWorld, _gain[c], push, cling, curl, tip: true);
            }
        }

        /// <summary>Build this hand's palm and fingertip fields for one class, on the frame it first
        /// holds an effect of that class.</summary>
        private bool EnsureFields(int c)
        {
            // THE READY TEST COMES FIRST AND IT IS NOT A MICRO-OPTIMISATION. Composing the two
            // names below allocates two strings, and this runs once per held class per frame — the
            // exact shape of per-frame garbage this project keeps finding at the top of its frame
            // cost. Past the first frame of a class, this method is two boolean reads.
            if (_palmFields[c].Ready && _tipFields[c].Ready)
                return true;
            if (_root == null)
                return false;
            var which = (VfxFlowClass)c;
            return _palmFields[c].Ensure($"PalmField.{which}", _root.transform, VRLayers.ModLayer)
                 & _tipFields[c].Ensure($"TipField.{which}", _root.transform, VRLayers.ModLayer);
        }

        /// <summary>List this hand's two fields for a class on a system's influence list. Called
        /// only when that list is being rebuilt — see <see cref="SyncInfluences"/>.</summary>
        internal void AddFieldsTo(ParticleSystem.ExternalForcesModule f, VfxFlowClass flow)
        {
            ParticleSystemForceField? palmField = _palmFields[(int)flow].Field;
            ParticleSystemForceField? tipField = _tipFields[(int)flow].Field;
            if (palmField != null)
                f.AddInfluence(palmField);
            if (tipField != null)
                f.AddInfluence(tipField);
        }

        private void NoteRefusal(ParticleSystem ps, Refusal term)
        {
            if (_refusedTerm != Refusal.None)
                return;   // the first refusal IS the nearest one: selection runs nearest-first
            _refusedExample = ps;
            _refusedTerm = term;
        }

        /// <summary>Per-hand reach report: in reach against actually held, the refusals by term, and
        /// the palm-to-nearest-effect distance in BOTH units beside the reach itself — the
        /// measurement that says whether a hand held in a character's smoke is inside the tested
        /// volume, rather than the name of a constant saying so.</summary>
        internal void AppendReach(System.Text.StringBuilder into)
        {
            into.Append(' ').Append(_side).Append(" hand: ").Append(_inReach)
                .Append(" system(s) in reach, ").Append(_held.Count).Append(" held (cap ")
                .Append(MaxSystemsPerHand).Append(')');
            if (_refusedCapFull + _refusedCeiling + _refusedGameOwned > 0)
            {
                into.Append("; refused in reach: ").Append(_refusedCapFull)
                    .Append(" because the cap was already full, ").Append(_refusedCeiling)
                    .Append(" over the hard ceiling, ").Append(_refusedGameOwned)
                    .Append(" because the game owns their collision");
                if (_refusedExample != null)
                    into.Append(" (nearest refused: '").Append(_refusedExample.name)
                        .Append("' by ").Append(_refusedTerm).Append(')');
            }
            // THE ONLY NEW PER-FRAME POPULATION ModBuild 431 ADDED, so it is counted rather than
            // asserted about. Two force fields (palm + fingertip) exist per effect CLASS this hand
            // has ever held, and each costs ten native property writes on a frame its class is
            // live and one boolean test on a frame it is not — so "built" and "live" are both
            // printed, because they are different numbers and the second is the one that costs.
            int built = 0;
            int live = 0;
            for (int i = 0; i < VfxFlow.ClassCount; i++)
            {
                if (_palmFields[i].Ready)
                    built++;
                if ((_liveClasses & (1 << i)) != 0)
                    live++;
            }
            into.Append("; force fields: ").Append(built).Append(" class pair(s) built, ")
                .Append(live).Append(" driven this frame, wake ").Append(_wake.ToString("F2"));
            if (_nearest != null && !float.IsNaN(_nearestWorldUnits))
            {
                float perMetre = Mathf.Max(_scale, 1e-4f);
                into.Append("; nearest effect '").Append(_nearest.name).Append("' at ")
                    .Append(_nearestWorldUnits.ToString("F2")).Append(" wu = ")
                    .Append((_nearestWorldUnits / perMetre).ToString("F3"))
                    .Append(" real m, against a reach of ")
                    .Append(_reachWorldUnits.ToString("F2")).Append(" wu = ")
                    .Append(ReachRealMeters.ToString("0.##")).Append(" real m (rig scale ")
                    .Append(perMetre.ToString("F2")).Append(" wu per real metre)");
            }
            // WHICH SIX, BY NAME. The ModBuild 431 census could say that six slots were full and
            // that a torch had been refused, and could not say what was standing in the torch's
            // place — so "the cap is spent on the wrong systems" was a hypothesis with no field to
            // test it against. It is now one line: if these names are fogs while the refusal beside
            // them is a fire, the ordering is still wrong and EmitterTieWeight is the term.
            if (_held.Count > 0)
            {
                into.Append("; holding");
                for (int i = 0; i < _held.Count; i++)
                {
                    ParticleSystem ps = _held[i];
                    into.Append(i == 0 ? " '" : ", '").Append(ps == null ? "<destroyed>" : ps.name)
                        .Append('\'');
                    if (ps != null && _driftCap.TryGetValue(ps, out float capReal))
                        into.Append(" (bound ").Append(capReal.ToString("F3"))
                            .Append(" real m/s)");
                }
            }
            into.Append('.');
        }

        /// <summary>Distance to what the system DRAWS, not to its transform origin: an effect's
        /// pivot is routinely at the emitter while the plume is a metre away.</summary>
        private static float Distance(in Entry e, Vector3 point)
        {
            ParticleSystemRenderer? renderer = e.Renderer;
            if (renderer == null)
                return Vector3.Distance(e.Ps.transform.position, point);
            Bounds b = renderer.bounds;
            return b.size.sqrMagnitude <= 1e-10f
                ? Vector3.Distance(e.Ps.transform.position, point)
                : Mathf.Sqrt(b.SqrDistance(point));
        }

        /// <summary>The release test, for the systems this hand already holds. Those are kept as
        /// bare systems (both hands share one list of captures), so the renderer is re-fetched —
        /// at most <see cref="MaxSystemsPerHand"/> times a frame, against the whole registry for
        /// the candidate pass above.</summary>
        private static float Distance(ParticleSystem ps, Vector3 point)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
                return Vector3.Distance(ps.transform.position, point);
            Bounds b = renderer.bounds;
            return b.size.sqrMagnitude <= 1e-10f
                ? Vector3.Distance(ps.transform.position, point)
                : Mathf.Sqrt(b.SqrDistance(point));
        }

        internal void Release()
        {
            _held.Clear();
            // ZERO THE AIR, ALWAYS. A hand that stops being tracked, or a feature switched off
            // mid-plume, must not leave ten force fields carrying last frame's numbers where a
            // game system that opts in by layer mask could still find them. Idle() writes once and
            // then early-outs, so this is free on every frame after the first.
            for (int i = 0; i < VfxFlow.ClassCount; i++)
            {
                _palmFields[i].Idle();
                _tipFields[i].Idle();
            }
            _liveClasses = 0;
            _wake = 0f;
            _velocity = Vector3.zero;
            for (int i = 0; i < VfxFlow.ClassCount; i++)
            {
                _gain[i] = 0f;
                _classCap[i] = float.MaxValue;
            }
            _inReach = 0;
            _refusedCapFull = 0;
            _refusedCeiling = 0;
            _refusedGameOwned = 0;
            _nearest = null;
            _refusedExample = null;
            _refusedTerm = Refusal.None;
            _nearestWorldUnits = float.NaN;
        }

        internal void Destroy()
        {
            Release();
            DropFields();
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
            _palm = null;
            _tip = null;
            _palmSphere = null;
            _tipSphere = null;
        }

        /// <summary>Forget every force field. The carriers are CHILDREN of the probe root, so
        /// destroying the root destroys them too — but the wrappers would go on holding references
        /// to destroyed components, and a destroyed ParticleSystemForceField added to an influence
        /// list is a null in somebody else's array. Called from both places the root dies.</summary>
        private void DropFields()
        {
            for (int i = 0; i < VfxFlow.ClassCount; i++)
            {
                _palmFields[i].Destroy();
                _tipFields[i].Destroy();
            }
        }

        private bool Ensure()
        {
            if (_root != null && _palmSphere != null && _tipSphere != null)
                return true;
            if (_root != null)
            {
                DropFields();
                UnityEngine.Object.Destroy(_root);
            }

            int layer = VRLayers.ModLayer;
            _root = new GameObject($"GloomhavenVR.VfxProbe.{_side}") { layer = layer };
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var palmGo = new GameObject("Palm") { layer = layer };
            var tipGo = new GameObject("Tip") { layer = layer };
            palmGo.transform.SetParent(_root.transform, false);
            tipGo.transform.SetParent(_root.transform, false);
            _palm = palmGo.transform;
            _tip = tipGo.transform;
            // NOT triggers, and that is the whole reason these exist rather than reusing an
            // interaction collider: particle collision ignores triggers, and every collider the
            // mod puts in the player's hands for poking and grabbing is one.
            _palmSphere = palmGo.AddComponent<SphereCollider>();
            _tipSphere = tipGo.AddComponent<SphereCollider>();
            _palmSphere.isTrigger = false;
            _tipSphere.isTrigger = false;
            return true;
        }
    }
}
