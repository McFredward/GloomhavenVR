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
        RefreshFeelGeneration();
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
        _nextScanAt = 0f;
        _loggedFirstAdopt = false;
        _tooDense.Clear();
        _gameOwned.Clear();
        _gameForces.Clear();
        _flowNamed.Clear();
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
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Hands", $"Hands disturb VFX: '{ps.name}' is treated as {e.Flow} because "
            + $"{e.FlowTerm}. Measured: lifetime {e.Traits.LifeSeconds:F2} s, travel "
            + $"{e.Traits.TravelWidths:F1} of its own widths per life, particle "
            + $"{e.Traits.DiameterMetres:F3} real m across, gravity multiplier "
            + $"{e.Traits.GravityModifier:F2}, simulation space "
            + $"{(e.Traits.LocalSpace ? "Local" : "World/Custom")}, on a figure: "
            + $"{(e.Traits.OnFigure ? "yes" : "no")} ({VfxFlow.Thresholds()}). It therefore gets "
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
            + "harder to name — HandsVfxClingStrength is the dial for it.");
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
            _distance = new float[Mathf.NextPowerOfTwo(_scene.Count)];

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
                    continue;
                }
                float d = Distance(in e, at);
                _distance[i] = d;
                if (float.IsNaN(_nearestWorldUnits) || d < _nearestWorldUnits)
                {
                    _nearestWorldUnits = d;
                    _nearest = e.Ps;
                }
                if (d <= reach)
                    _inReach++;
                if (_held.Contains(e.Ps))
                    _distance[i] = float.MaxValue;   // already ours: not a candidate, not a refusal
            }

            // NEAREST FIRST, and this time actually. The old loop walked the registry in
            // FindObjectsOfType order and stopped at the cap, so which systems a hand got was
            // decided by the order Unity happened to return them in rather than by which ones the
            // hand was in. Selection is O(cap x n) with cap = 6 over a list of tens.
            while (_held.Count < MaxSystemsPerHand)
            {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (_distance[i] < bestDistance)
                    {
                        bestDistance = _distance[i];
                        best = i;
                    }
                }
                if (best < 0 || bestDistance > reach)
                    break;
                _distance[best] = float.MaxValue;
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
                for (int i = 0; i < n; i++)
                {
                    if (_distance[i] > reach)
                        continue;
                    _refusedCapFull++;
                    ParticleSystem? capped = _scene[i].Ps;
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
        ///   <item>THE WAKE. The hand's own palm velocity, converted to real metres per second and
        ///   divided by the speed dial, is how hard the hand is currently wafting. It rises the
        ///   instant the hand moves and decays over HandsVfxSettleSeconds, and its DIRECTION is
        ///   held through the decay — so a hand that sweeps past and stops leaves the smoke still
        ///   travelling the way it was pushed, instead of stopping with the hand.</item>
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
        /// what a force field cannot be scoped against.</para></summary>
        private void UpdateFlow(VRHand hand, Transform palm, Transform tip, float scale)
        {
            float push = HandsConfig.HandsVfxPushStrengthSafe();
            float cling = HandsConfig.HandsVfxClingStrengthSafe();
            float curl = HandsConfig.HandsVfxCurlStrengthSafe();
            float settle = HandsConfig.HandsVfxSettleSecondsSafe();
            float radius = HandsConfig.HandsVfxReachMetersSafe() * scale;

            // PalmVelocity is world units per second and the rig scale is world units per real
            // metre, so the quotient is real metres per second — the unit the dial is written in,
            // and the one that means the same thing at every rig scale.
            Vector3 velocity = hand.PalmVelocity;
            float speedWorld = velocity.magnitude;
            float speedReal = speedWorld / scale;
            float target = Mathf.Clamp01(speedReal / HandsConfig.HandsVfxWakeSpeedSafe());
            if (speedWorld > 1e-4f)
                _wakeDirection = velocity / speedWorld;
            if (target >= _wake)
            {
                _wake = target;
            }
            else
            {
                // An exponential fall with a time constant the player owns. Frame-rate independent
                // by construction (the exponent carries dt), which a raw Lerp factor is not.
                float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
                _wake = settle <= 1e-3f
                    ? target
                    : Mathf.Lerp(_wake, target, 1f - Mathf.Exp(-dt / settle));
            }

            _liveClasses = 0;
            for (int i = 0; i < _held.Count; i++)
            {
                ParticleSystem ps = _held[i];
                if (ps != null && _flowClass.TryGetValue(ps, out VfxFlowClass held))
                    _liveClasses |= 1 << (int)held;
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
                    _palmFields[c].Idle();
                    _tipFields[c].Idle();
                    continue;
                }
                VfxFlowProfile p = VfxFlowProfile.Of((VfxFlowClass)c);
                if (!EnsureFields(c))
                    continue;
                float r = radius * p.Radius;
                _palmFields[c].Apply(in p, palmAt, _wakeDirection, _wakeDirection, r, _wake, scale,
                                     push, cling, curl, tip: false);
                _tipFields[c].Apply(in p, tip.position, fingerAxis, _wakeDirection,
                                    r * VfxFlowField.TipRadiusFraction, _wake, scale,
                                    push, cling, curl, tip: true);
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
