using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// PUT A HAND IN THE SMOKE AND THE SMOKE GOES ROUND IT — the scenario's own particle effects
/// (fire, smoke, embers, dust, spell wash) deflect off the player's hands while they are inside,
/// and are restored to the authored settings, field for field, the moment they leave.
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
/// The effect is therefore purely kinematic — particles bounce, slow and die a little early — and
/// it cannot spawn a single object.</para>
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

    // How the particles react. Deliberately gentle: this should read as "the air moved", not as
    // a squash ball. Bounce keeps a little energy, dampen removes some speed, and the small
    // lifetime loss stops deflected particles from lingering in a wrong-looking cloud.
    private const float Bounce = 0.35f;
    private const float Dampen = 0.45f;
    private const float LifetimeLoss = 0.10f;

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

        internal Entry(ParticleSystem ps, ParticleSystemRenderer? renderer, bool onFigure)
        {
            Ps = ps;
            Renderer = renderer;
            OnFigure = onFigure;
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

        internal Captured(ParticleSystem.CollisionModule c)
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
        }

        internal void RestoreTo(ParticleSystem.CollisionModule c)
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
        bool scanned = Rescan();
        Left.Tick(left);
        Right.Tick(right);
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
        _nextScanAt = 0f;
        _loggedFirstAdopt = false;
        _tooDense.Clear();
        _gameOwned.Clear();
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
                pair.Value.RestoreTo(ps.collision);
            (drop ??= new List<ParticleSystem>(4)).Add(ps!);
        }
        if (drop == null)
            return;
        for (int i = 0; i < drop.Count; i++)
        {
            _original.Remove(drop[i]);
            _applied.Remove(drop[i]);
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
    private static bool Adopt(ParticleSystem ps, int live, float scale)
    {
        if (!_original.ContainsKey(ps))
        {
            ParticleSystem.CollisionModule capture = ps.collision;
            if (capture.enabled)
                return false;   // the game configured this one; it is not ours to change
            _original[ps] = new Captured(capture);
        }

        ParticleSystem.CollisionModule c = ps.collision;
        c.type = ParticleSystemCollisionType.World;
        c.mode = ParticleSystemCollisionMode.Collision3D;
        c.collidesWith = VRLayers.ModLayerMask;
        c.sendCollisionMessages = false;   // see the class doc: no handler may ever fire from us
        c.bounce = Bounce;
        c.dampen = Dampen;
        c.lifetimeLoss = LifetimeLoss;
        ApplyQuality(ps, c, live, scale);
        c.enabled = true;

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
                + "is where what was in reach, and by which term it was refused, is written down.");
        }
        return true;
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
            + "approximate plane cache is the term that failed — not the reach, not the cap.");
    }

    private static readonly HashSet<string> _tooDense = new(8, StringComparer.Ordinal);
    private static readonly HashSet<string> _gameOwned = new(8, StringComparer.Ordinal);
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
            _scene.Add(new Entry(ps, ps.GetComponent<ParticleSystemRenderer>(), onFigure));
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
                   + "set, so a dense effect is graded down rather than dropped).");
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
                 .Append("' (").Append(live).Append(" live, ").Append(QualityFor(live)).Append(')');
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
                ApplyQuality(ps, ps.collision, liveHeld, scale);
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
                ParticleSystem ps = _scene[best].Ps;
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
                if (!Adopt(ps, live, scale))
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

            RestoreUnheld();
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
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
            _palm = null;
            _tip = null;
            _palmSphere = null;
            _tipSphere = null;
        }

        private bool Ensure()
        {
            if (_root != null && _palmSphere != null && _tipSphere != null)
                return true;
            if (_root != null)
                UnityEngine.Object.Destroy(_root);

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
