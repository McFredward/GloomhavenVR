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
/// <para>WHAT IS SKIPPED IS NAMED, NOT COUNTED. The scan line reports how many systems were
/// adopted, how many of those sit on figures, and — with the names of the first few — how many
/// were skipped as game-managed. A number alone ("K skipped") cannot tell anybody whether the
/// effects they wanted to feel are in that K; the names can, on the first hardware run.</para>
///
/// <para>PURELY LOCAL, LIKE THE CURTAINS. Particle effects were never on the wire, so every player
/// disturbs their own copy of the room and nothing can diverge. No wire field, no sync, no
/// authority.</para>
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

    /// <summary>How close the hand must get to a system's drawn bounds before it is adopted.</summary>
    private const float ReachRealMeters = 0.30f;

    /// <summary>Release hysteresis — leaving costs 1.5x the reach, so a hand hovering on the
    /// boundary cannot adopt and restore the same system every other frame.</summary>
    private const float ReleaseFactor = 1.5f;

    /// <summary>Hard cap per hand. Collision quality High raycasts PER PARTICLE, so this is the
    /// dial that keeps a hand waved through a dense effect off the frame budget.</summary>
    private const int MaxSystemsPerHand = 3;

    /// <summary>A system with more live particles than this is never adopted, whatever the
    /// distance: the per-particle cost is the cost. Raised from 400 to 900 once figures came into
    /// scope — a character's spell wash is exactly the sort of dense effect a player will put a
    /// hand into on purpose, and silently dropping it is the "no silent caps" failure. Every drop
    /// is logged with the system's name and its particle count, so the number can be argued with
    /// from hardware instead of from here.</summary>
    private const int MaxParticlesToAdopt = 900;

    /// <summary>Registry refresh, in seconds. FindObjectsOfType is the project's default suspect
    /// for a per-frame cost that grows with the scene; this runs it on a cadence and never in a
    /// frame path.</summary>
    private const float RescanSeconds = 3f;

    // How the particles react. Deliberately gentle: this should read as "the air moved", not as
    // a squash ball. Bounce keeps a little energy, dampen removes some speed, and the small
    // lifetime loss stops deflected particles from lingering in a wrong-looking cloud.
    private const float Bounce = 0.35f;
    private const float Dampen = 0.45f;
    private const float LifetimeLoss = 0.10f;

    private static readonly Probe Left = new(HandSide.Left);
    private static readonly Probe Right = new(HandSide.Right);

    private static readonly List<ParticleSystem> _scene = new(32);
    private static float _nextScanAt;
    private static int _scansTimed;

    /// <summary>The AUTHORED collision settings, captured once per system on the first adoption by
    /// either hand and written back verbatim when the last hand lets go. Keyed by the system
    /// because both hands can be in the same plume and there is one authored setting to restore.
    /// </summary>
    private static readonly Dictionary<ParticleSystem, Captured> _original = new(8);

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

        Rescan();
        Left.Tick(left);
        Right.Tick(right);
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
        _nextScanAt = 0f;
        _scansTimed = 0;
        _loggedFirstAdopt = false;
        _tooDense.Clear();
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
            _original.Remove(drop[i]);
    }

    /// <summary>Capture-once, then apply. Returns false when the system must not be touched.</summary>
    private static bool Adopt(ParticleSystem ps)
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
        // HIGH is not a quality preference, it is the only setting that consults real colliders;
        // Medium and Low collide against a cached plane set, which a moving hand is not in.
        c.quality = ParticleSystemCollisionQuality.High;
        c.collidesWith = VRLayers.ModLayerMask;
        c.sendCollisionMessages = false;   // see the class doc: no handler may ever fire from us
        c.bounce = Bounce;
        c.dampen = Dampen;
        c.lifetimeLoss = LifetimeLoss;
        c.enabled = true;

        if (!_loggedFirstAdopt)
        {
            _loggedFirstAdopt = true;
            VRLog.Info("Hands", $"Hands disturb VFX: first system adopted ('{ps.name}', "
                + $"{ps.particleCount} live particles). Collision is switched on with "
                + $"collidesWith = the mod layer ({VRLayers.ModLayer}) and messages OFF, so "
                + "particles deflect off the hand and nothing can be spawned. Authored settings "
                + "are restored when the hand leaves.");
        }
        return true;
    }

    /// <summary>A dropped effect names itself once. A cap that drops silently reads, in a log, as
    /// "there was nothing there" — which is the one thing it does not mean.</summary>
    private static void NoteTooDense(ParticleSystem ps)
    {
        if (!_tooDense.Add(ps.name))
            return;
        VRLog.Info("Hands", $"Hands disturb VFX: '{ps.name}' was NOT adopted — "
            + $"{ps.particleCount} live particles is over the {MaxParticlesToAdopt} cap that keeps "
            + "per-particle collision off the frame budget. Reported once per effect name; if this "
            + "names an effect you wanted to feel, the cap is the dial.");
    }

    private static readonly HashSet<string> _tooDense = new(8, StringComparer.Ordinal);

    private static void Rescan()
    {
        float now = Time.unscaledTime;
        if (now < _nextScanAt && _scene.Count > 0)
            return;
        _nextScanAt = now + RescanSeconds;

        var watch = _scansTimed < 3 ? System.Diagnostics.Stopwatch.StartNew() : null;
        ParticleSystem[] found = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
        _scene.Clear();
        int gameManaged = 0;
        int onFigures = 0;
        int gameManagedOnFigures = 0;
        System.Text.StringBuilder? skipped = watch != null ? new System.Text.StringBuilder(128) : null;
        for (int i = 0; i < found.Length; i++)
        {
            ParticleSystem ps = found[i];
            if (ps == null || !ps.gameObject.activeInHierarchy)
                continue;
            // "Does this effect belong to a figure?" is a RELATEDNESS question, which is the one
            // thing GetComponentInParent actually answers. It is a CENSUS term only — it changes
            // no decision below, because a figure's effects are adopted exactly like the room's.
            bool onFigure = ps.GetComponentInParent<ActorBehaviour>() != null;
            // The two RFX4 collision scripts are the game's own collision management. See the
            // class doc for why adopting one is both a spawn hazard and an unwinnable write war.
            if (HasGameCollisionScript(ps))
            {
                gameManaged++;
                if (onFigure)
                    gameManagedOnFigures++;
                if (skipped != null && gameManaged <= 6)
                    skipped.Append(skipped.Length == 0 ? "" : ", ").Append('\'').Append(ps.name)
                           .Append('\'').Append(onFigure ? " (figure)" : "");
                continue;
            }
            if (onFigure)
                onFigures++;
            _scene.Add(ps);
        }

        if (watch != null)
        {
            watch.Stop();
            _scansTimed++;
            VRLog.Info("Hands", $"VFX scan #{_scansTimed}: {_scene.Count} adoptable particle "
                + $"system(s) of {found.Length} found — {onFigures} of them ON FIGURES (auras, "
                + "casts, monster effects), which are adopted exactly like the room's. "
                + $"{gameManaged} skipped as game-managed collision ({gameManagedOnFigures} of "
                + "those on figures)"
                + (skipped != null && skipped.Length > 0 ? $": {skipped}" : "")
                + $". Scan took {watch.Elapsed.TotalMilliseconds:F3} ms and runs every "
                + $"{RescanSeconds:0.#} s while a hand is tracked, never per frame.");
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

        internal Probe(HandSide side) => _side = side;

        internal bool Holds(ParticleSystem ps) => _held.Contains(ps);

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

            // Drop what we have left, using the wider release radius (hysteresis).
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                ParticleSystem ps = _held[i];
                if (ps == null || !ps.gameObject.activeInHierarchy || Distance(ps, at) > release)
                    _held.RemoveAt(i);
            }

            // Adopt what we have entered, nearest first, up to the cap.
            for (int i = 0; i < _scene.Count && _held.Count < MaxSystemsPerHand; i++)
            {
                ParticleSystem ps = _scene[i];
                if (ps == null || !ps.gameObject.activeInHierarchy || _held.Contains(ps))
                    continue;
                if (ps.particleCount > MaxParticlesToAdopt)
                {
                    NoteTooDense(ps);
                    continue;
                }
                if (Distance(ps, at) > reach)
                    continue;
                if (Adopt(ps))
                    _held.Add(ps);
            }

            RestoreUnheld();
        }

        /// <summary>Distance to what the system DRAWS, not to its transform origin: an effect's
        /// pivot is routinely at the emitter while the plume is a metre away.</summary>
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

        internal void Release() => _held.Clear();

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
