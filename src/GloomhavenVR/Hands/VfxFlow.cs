using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// WHAT AN EFFECT IS, AND WHAT THE AIR AROUND A HAND SHOULD DO TO IT — the classification and the
/// force profiles behind <see cref="SceneVfxHands"/>. This file holds no state and touches no
/// particle system: it answers two questions and is otherwise inert.
///
/// <para><b>WHY IT EXISTS AT ALL.</b> The user, 2026-09-05, on ModBuild 430: "Der Rauch reagiert
/// nun auf die Hände aber überhaupt nicht immersiv — er weicht einfach super schnell unnatürlich
/// zurück. Ich will das er sich um die Hand bzw dem Finger legt — wie wenn man einmal durch den
/// Rauch 'weht'." Two separate statements. The first is about the MECHANISM: a collision module is
/// a reflection, and a reflection is a billiard ball — no amount of tuning a bounce coefficient
/// makes a bounce into a flow. The second is about DESIGN: "Das Selbe soll auch für andere
/// Partikeleffekte gelten … überlege dir für jedes System ein immersives Erlebnis." One behaviour
/// for every effect in the game was never going to be right, because fire and dust do not move
/// alike.</para>
///
/// <para><b>CLASSIFY BY WHAT MOVES, NOT BY WHAT IT IS CALLED.</b> Every term below is a number the
/// particle system itself carries. No asset name, no prefab prefix, no shader name is consulted —
/// this project has shipped a name-substring partition twice on doors and paid for it both times,
/// and a name is a claim about one asset kit rather than a property of an effect. The blend mode
/// was considered and DELIBERATELY LEFT OUT for the same family of reason: "is this material
/// additive" is answered by reading <c>_DstBlend</c>, a property the legacy particle shaders in a
/// 2015-era kit do not declare at all, and a property-existence test that silently flips is exactly
/// the defect that killed the ghost hand for forty builds. What is used instead is kinematics —
/// how long a particle lives, how far it travels in its own widths, how big it is in REAL metres,
/// whether it is simulated in its emitter's frame, and whether an <c>ActorBehaviour</c> owns it.
/// Those are all readable, all meaningful, and none of them can be renamed.</para>
///
/// <para><b>THE ONE SCALE-FREE TRICK, and it is worth naming.</b> "Travel" is
/// <c>startSpeed x startLifetime / startSize</c>: how many of its OWN WIDTHS a particle crosses in
/// its life. Every one of those three numbers scales with the emitter's transform in the same way,
/// so the ratio does not — it reads the same in a x4 scenario and a x198 map room, and it needs no
/// rig scale at all. It is the difference between a spark (hundreds of widths) and a smoke puff
/// (about two). Only the DIAMETER test needs the rig scale, and it gets it from the hand.</para>
///
/// <para><b>SIX RULES, FIVE CLASSES, AND THE RULE THAT FIRED IS PRINTED.</b> A classifier nobody
/// can argue with is a classifier nobody can fix. <see cref="VfxFlow.Classify"/> hands back the term
/// decided, in words, and <see cref="SceneVfxHands"/> prints it with the measured numbers beside
/// it — so an effect in the wrong class names itself, with its own evidence, on the first hardware
/// run rather than after a round of guessing.</para>
/// </summary>
internal enum VfxFlowClass
{
    /// <summary>Smoke and fog. The headline case: it wraps, curls and settles back.</summary>
    Smoke,

    /// <summary>Fire and flame. Buoyant and anchored to its source: a hand carves a brief void and
    /// the flame leans away and recovers. It is never blown sideways.</summary>
    Flame,

    /// <summary>Embers and sparks. Light and quick: they scatter and drift.</summary>
    Sparks,

    /// <summary>Dust and motes. They hang in the wake of a hand and settle slowly.</summary>
    Motes,

    /// <summary>A character's aura or the wash of a cast. Bound to its owner: the near field is
    /// disturbed and springs straight back.</summary>
    Aura,
}

/// <summary>The five kinematic terms a class is decided from, all read off the particle system
/// itself. A struct so measuring one allocates nothing on the adoption path.</summary>
internal readonly struct VfxTraits
{
    /// <summary>Authored particle lifetime, in seconds. Frame-rate free and scale free.</summary>
    internal readonly float LifeSeconds;

    /// <summary>How many of its OWN WIDTHS a particle crosses over its life. Scale free by
    /// construction — see the file header.</summary>
    internal readonly float TravelWidths;

    /// <summary>Authored particle size converted to REAL metres through the live rig scale. The
    /// only term here that needs to know how big the room is.</summary>
    internal readonly float DiameterMetres;

    /// <summary>Unity's dimensionless gravity multiplier. Carried for the log, not for a rule: a
    /// buoyant flame and a still-air flame differ by a hair here and the classification must not
    /// hang off that.</summary>
    internal readonly float GravityModifier;

    /// <summary>Does an <c>ActorBehaviour</c> own this effect? A component-ancestry test, which is
    /// the one question <c>GetComponentInParent</c> actually answers.</summary>
    internal readonly bool OnFigure;

    /// <summary>Is it simulated in its emitter's own frame? That is what makes a halo a halo: the
    /// particles travel WITH the figure instead of being left behind by it.</summary>
    internal readonly bool LocalSpace;

    internal VfxTraits(float lifeSeconds, float travelWidths, float diameterMetres,
                       float gravityModifier, bool onFigure, bool localSpace)
    {
        LifeSeconds = lifeSeconds;
        TravelWidths = travelWidths;
        DiameterMetres = diameterMetres;
        GravityModifier = gravityModifier;
        OnFigure = onFigure;
        LocalSpace = localSpace;
    }
}

/// <summary>What the air around a hand does to ONE class of effect. Every number is a designed
/// feel, and every one of them is multiplied by a config dial the player can turn — see
/// <see cref="HandsConfig"/>'s HandsVfx* group.</summary>
internal readonly struct VfxFlowProfile
{
    /// <summary>How much the effect CLINGS. Unity's force-field drag, applied with both of its
    /// multipliers on (by particle size and by particle velocity), so a big slow puff feels the
    /// hand and a small fast spark barely does — which is the physical answer as well as the
    /// pretty one. This is the term that makes smoke HANG around a hand instead of fleeing it.
    /// </summary>
    internal readonly float Drag;

    /// <summary>How fast particles are carried AROUND the field's axis — the vortex. This is
    /// "es legt sich um die Hand": a reflection cannot produce it and a drag cannot either.
    /// </summary>
    internal readonly float Vortex;

    /// <summary>How hard particles are pulled ONTO that circular path. Low = a lazy swirl; high =
    /// they orbit and stay, which is what makes an aura feel bound to its owner.</summary>
    internal readonly float Attract;

    /// <summary>Directional push, in real metres per second squared at FULL hand speed. Driven by
    /// the hand's own velocity, so a slow hand barely disturbs and a swipe wafts. Scaled by the rig
    /// at use time, the standing convention for a real length under a scaled rig.</summary>
    internal readonly float Push;

    /// <summary>Outward push from the centre of the field, in real metres per second squared. It
    /// carves the pocket the hand occupies. Small for smoke (which should wrap, not part) and large
    /// for flame (which should be pushed aside and spring back).</summary>
    internal readonly float Repel;

    /// <summary>Multiplies the field radius dial. A mote field reaches further than a flame field
    /// because dust is disturbed by the wake and fire is disturbed by the hand.</summary>
    internal readonly float Radius;

    /// <summary>Collision DAMPEN for this class: how much speed a particle that actually touches
    /// the hand loses. High everywhere, because a particle that slides along a hand and is then
    /// picked up by the vortex is the wrap; a particle that keeps its speed is the billiard ball.
    /// </summary>
    internal readonly float Dampen;

    /// <summary>Collision LIFETIME LOSS. Zero on everything except flame, where a small loss is
    /// what makes the carved void read as a void rather than as a dent.</summary>
    internal readonly float LifetimeLoss;

    private VfxFlowProfile(float drag, float vortex, float attract, float push, float repel,
                           float radius, float dampen, float lifetimeLoss)
    {
        Drag = drag;
        Vortex = vortex;
        Attract = attract;
        Push = push;
        Repel = repel;
        Radius = radius;
        Dampen = dampen;
        LifetimeLoss = lifetimeLoss;
    }

    /// <summary>The designed feel of each class, in one table.
    ///
    /// <para>READ IT AS FIVE SENTENCES, because that is how it was written:</para>
    /// <list type="bullet">
    ///   <item><b>Smoke</b> — heavy drag, strong vortex, gentle push, almost no repel. The hand
    ///   does not part the smoke; the smoke slows against it, is carried around it and drifts on.
    ///   This is the case the report was about.</item>
    ///   <item><b>Flame</b> — the only class with more repel than push. Fire is buoyant and
    ///   anchored to its fuel, so a hand must never blow it sideways; it opens a pocket, the flame
    ///   leans out of it, and its own upward emission fills the pocket back in within a breath.
    ///   The small lifetime loss is what makes the pocket visible.</item>
    ///   <item><b>Sparks</b> — the lightest thing in the table: almost no drag and the largest
    ///   push, so they take the hand's motion immediately, scatter and drift. The quickest class
    ///   to react and the shortest-lived reaction.</item>
    ///   <item><b>Motes</b> — the most drag of anything here and the widest field. Dust does not
    ///   dodge a hand; it is dragged into the wake behind it and takes a long time to settle. The
    ///   most rewarding one to get right, and the one where a wake that lingers
    ///   (HandsVfxSettleSeconds) is most visible.</item>
    ///   <item><b>Aura</b> — the strongest rotation attraction and the smallest push, so the near
    ///   field swirls where the hand is and the rest of the halo does not care. It has to look
    ///   OWNED: you disturb it, you do not take it with you.</item>
    /// </list>
    /// <para><b>THESE ARE STARTING POINTS AND THE FIRST HARDWARE ROUND IS A TUNING ROUND.</b> Two
    /// of the four force terms have units no reading of the API settles — Unity documents "drag"
    /// and "rotation speed" without saying against what — so the RATIOS between the five rows are
    /// the designed part and the absolute magnitudes are a first guess with a dial on each of them.
    /// The drag column in particular was chosen deliberately low: a drag that is too high freezes
    /// an effect in mid-air, which looks every bit as wrong as the bounce this replaces, and it is
    /// the harder failure to recognise from a description. HandsVfxClingStrength is the dial, and
    /// each adopted effect prints the numbers it was given.</para>
    /// </summary>
    internal static VfxFlowProfile Of(VfxFlowClass c) => c switch
    {
        //                            drag  vortex attract push  repel radius dampen loss
        VfxFlowClass.Smoke  => new(   1.50f, 2.40f,  0.60f, 1.00f, 0.12f, 1.35f, 0.90f, 0.00f),
        VfxFlowClass.Flame  => new(   0.60f, 0.50f,  0.25f, 0.15f, 0.90f, 0.90f, 0.75f, 0.06f),
        VfxFlowClass.Sparks => new(   0.18f, 0.90f,  0.15f, 2.20f, 0.30f, 1.10f, 0.35f, 0.00f),
        VfxFlowClass.Motes  => new(   3.00f, 1.60f,  0.40f, 1.60f, 0.05f, 1.60f, 0.95f, 0.00f),
        _                   => new(   1.10f, 3.00f,  1.40f, 0.25f, 0.10f, 0.80f, 0.80f, 0.00f),
    };
}

/// <summary>The classifier and its thresholds. Static, stateless, allocation-free.</summary>
internal static class VfxFlow
{
    /// <summary>How many classes there are — the width of the per-class field arrays a hand keeps.
    /// Derived from the enum so it cannot drift from it.</summary>
    internal const int ClassCount = 5;

    /// <summary>A particle that lives this long is not burning and is not a spark. Nothing in a
    /// game's fire effect lasts three seconds; a dust mote in a light shaft lasts ten.</summary>
    private const float HangSeconds = 3.0f;

    /// <summary>…and if, over that whole life, it crosses fewer than this many of its own widths,
    /// it is not going anywhere: it HANGS. Six rather than one or two because a mote's start speed
    /// is tiny but its size is tinier, and the ratio of two small numbers is not small.</summary>
    private const float HangWidths = 6.0f;

    /// <summary>Cross this many of its own widths and the particle is being THROWN rather than
    /// drifting. Twelve is comfortably above a smoke plume (about two) and far below a spark
    /// (hundreds), so the gap this number sits in is an order of magnitude wide in both
    /// directions — which is the only honest reason to trust a threshold nobody has measured on
    /// hardware yet.</summary>
    private const float FlyWidths = 12.0f;

    /// <summary>A particle this many REAL metres across is a puff or a tongue of flame, not a
    /// speck. Ten centimetres is about half a palm: below it, a single particle is not something
    /// you can see the shape of, which is exactly when the mote treatment (drag it into the wake)
    /// reads better than the smoke treatment (wrap it around the hand).</summary>
    private const float BulkDiameterMetres = 0.10f;

    /// <summary>A BIG particle that lives less than this is a flame tongue; longer, and it is
    /// smoke. Flame effects run at roughly a second per particle and smoke at several, so 1.8 s
    /// sits between two populations rather than inside one.</summary>
    private const float FlameSeconds = 1.8f;

    /// <summary>Measure the six terms. Allocation-free; every read is a native property get and
    /// none of them is a string.</summary>
    /// <param name="ps">The system to measure.</param>
    /// <param name="onFigure">Whether an <c>ActorBehaviour</c> owns it (already computed by the
    /// registry sweep; passed in rather than re-derived per call).</param>
    /// <param name="wuPerRealMetre">The live rig scale. 1 before a hand has ever ticked, which
    /// affects only <see cref="VfxTraits.DiameterMetres"/> and self-corrects on the next scan.
    /// </param>
    internal static VfxTraits Measure(ParticleSystem ps, bool onFigure, float wuPerRealMetre)
    {
        ParticleSystem.MainModule main = ps.main;
        float life = Mathf.Abs(main.startLifetimeMultiplier);
        float speed = Mathf.Abs(main.startSpeedMultiplier);
        float size = Mathf.Abs(main.startSizeMultiplier);

        // TRAVEL IS COMPUTED FROM THE RAW MULTIPLIERS ON PURPOSE. Whatever the emitter's transform
        // does to a particle's size it also does to its speed, so the scale cancels out of the
        // ratio and this line needs no scaling mode, no lossyScale and no rig. A system whose size
        // is driven entirely by a curve reads 0 here; that falls to "it hangs", which is the
        // gentlest class in the table, rather than to "it flies", which is the sharpest.
        float travel = size > 1e-5f ? speed * life / size : 0f;

        // THE DIAMETER IS THE ONE TERM THAT NEEDS THE ROOM. startSize is in the system's own units,
        // and which transform (if any) scales it is Unity's scalingMode — Hierarchy uses the full
        // world scale, Local only the object's own, and Shape scales the EMITTER but not the
        // particle. Getting this wrong by a factor of the rig scale would move every effect across
        // the Smoke/Motes line at once, which is why all three cases are written out.
        float factor = main.scalingMode switch
        {
            ParticleSystemScalingMode.Hierarchy => Largest(ps.transform.lossyScale),
            ParticleSystemScalingMode.Local => Largest(ps.transform.localScale),
            _ => 1f,
        };
        float metres = size * factor / Mathf.Max(wuPerRealMetre, 1e-4f);

        return new VfxTraits(life, travel, metres, main.gravityModifierMultiplier, onFigure,
                             main.simulationSpace == ParticleSystemSimulationSpace.Local);
    }

    private static float Largest(Vector3 v)
        => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

    /// <summary>Which class, and WHICH TERM DECIDED. The out parameter is always a string literal,
    /// so this allocates nothing and can be called from anywhere.
    ///
    /// <para>Six rules, first match wins, ordered from the most specific observation to the least:
    /// something that hangs is dust whatever else it is; something that flies is a spark whatever
    /// else it is; something a figure carries in its own frame is a halo; a big short-lived
    /// particle is a flame; a big long-lived one is smoke; and anything small with no other term
    /// gets the gentlest treatment in the table rather than a guess.</para></summary>
    internal static VfxFlowClass Classify(in VfxTraits t, out string decidedBy)
    {
        if (t.LifeSeconds >= HangSeconds && t.DiameterMetres < BulkDiameterMetres
            && t.TravelWidths <= HangWidths)
        {
            decidedBy = "it HANGS (small, long-lived, and it crosses only a few of its own widths)";
            return VfxFlowClass.Motes;
        }
        if (t.TravelWidths >= FlyWidths)
        {
            decidedBy = "it FLIES (it crosses many times its own width in one life)";
            return VfxFlowClass.Sparks;
        }
        if (t.OnFigure && t.LocalSpace)
        {
            decidedBy = "a FIGURE carries it, in the figure's own simulation space";
            return VfxFlowClass.Aura;
        }
        if (t.DiameterMetres >= BulkDiameterMetres && t.LifeSeconds <= FlameSeconds)
        {
            decidedBy = "each particle is BIG and SHORT-LIVED";
            return VfxFlowClass.Flame;
        }
        if (t.DiameterMetres >= BulkDiameterMetres)
        {
            decidedBy = "each particle is BIG and LONG-LIVED";
            return VfxFlowClass.Smoke;
        }
        decidedBy = "no term above fired; small particles fall to the gentlest profile";
        return VfxFlowClass.Motes;
    }

    /// <summary>The thresholds, for the log. One string, built once per named effect, so the
    /// numbers a verdict was measured against travel with the verdict.</summary>
    internal static string Thresholds()
        => $"thresholds: hangs at >= {HangSeconds:0.#} s and <= {HangWidths:0.#} widths, "
         + $"flies at >= {FlyWidths:0.#} widths, big at >= {BulkDiameterMetres:0.##} real m, "
         + $"flame below {FlameSeconds:0.#} s";
}

/// <summary>
/// ONE FORCE FIELD: a sphere of moving air carried on a hand, for one effect class.
///
/// <para><b>WHY A FORCE FIELD AND NOT A COLLIDER.</b> A collision module reflects a particle off a
/// surface. That is a bounce, and a bounce is the "weicht super schnell unnatürlich zurück" the
/// user reported — no coefficient makes a reflection into a flow, because the two are different
/// operations. <c>ParticleSystemForceField</c> is Unity's other half of this: a volume that
/// ACCELERATES particles that opt into it, with a soft falloff, a directional term, a vortex and a
/// drag. Those four terms are the vocabulary "the air moved" is written in, and none of them exists
/// on a collider.</para>
///
/// <para><b>ONE FIELD PER CLASS, NOT ONE PER HAND.</b> A force field carries one set of numbers, so
/// a single field per hand would have to average smoke and fire together and would be wrong for
/// both. Each hand therefore lazily builds a field per class it actually meets — typically one or
/// two in a scenario, never more than <see cref="VfxFlow.ClassCount"/> — and each adopted system
/// lists only the fields for its own class. A field nothing lists costs nothing: Unity walks each
/// system's influence list, not a global field list.</para>
///
/// <para><b>A FIELD WITH NOTHING TO PUSH CARRIES ZERO FORCE</b> (<see cref="Idle"/>). That is a
/// safety property, not tidiness. Unity's default influence filter is a LAYER MASK set to
/// Everything, so any particle system in the game that has its own external forces switched on
/// would feel these fields whether this mod adopted it or not — a layer cannot hide from
/// "Everything". Zeroing an unused field means the only systems that can ever feel a non-zero
/// force are the ones a hand is currently holding, plus the small explicitly-logged population that
/// already uses external forces and is near a hand of the same class.</para>
/// </summary>
internal sealed class VfxFlowField
{
    /// <summary>How much of the palm field's radius the fingertip field gets. Smaller, so a single
    /// finger drawn through a plume makes a finger-sized curl rather than a fist-sized one — which
    /// is the "bzw dem Finger" half of the request.</summary>
    internal const float TipRadiusFraction = 0.45f;

    /// <summary>The fingertip swirls harder than the palm at equal strength. A small field has less
    /// room to turn a particle in, so without this the finger's curl reads as a nudge.</summary>
    internal const float TipVortexBoost = 1.4f;

    /// <summary>…and pushes less, because a fingertip is not a fan.</summary>
    internal const float TipPushFactor = 0.7f;

    /// <summary>A trace of randomness in the vortex. Without it every particle in the field turns
    /// in lockstep and the swirl reads as a mechanism rather than as air.</summary>
    private static readonly Vector2 RotationRandomness = new(0.15f, 0.15f);

    private GameObject? _go;
    private Transform? _transform;
    private ParticleSystemForceField? _field;
    private bool _idle;

    /// <summary>The component other systems list. Null until <see cref="Ensure"/> has run.</summary>
    internal ParticleSystemForceField? Field => _field;

    /// <summary>Is the carrier built? Tested by the caller BEFORE it composes a name, because a
    /// name is a string and a string on a per-frame path is an allocation — the whole reason this
    /// property exists rather than letting <see cref="Ensure"/>'s own early-out do the work.
    /// </summary>
    internal bool Ready => _field != null && _transform != null;

    /// <summary>Build the carrier if it is not there yet. Returns false only if Unity refused, in
    /// which case the caller simply has no field for that class this frame.</summary>
    internal bool Ensure(string name, Transform parent, int layer)
    {
        if (_field != null && _transform != null)
            return true;
        if (_go != null)
            Object.Destroy(_go);
        _go = new GameObject(name) { layer = layer };
        _transform = _go.transform;
        _transform.SetParent(parent, false);
        _field = _go.AddComponent<ParticleSystemForceField>();
        _field.shape = ParticleSystemForceFieldShape.Sphere;
        // START AT THE CENTRE, FADE OUT AT THE EDGE. Unity ramps a sphere field's strength down
        // from startRange to endRange, and a force that fades in with distance is the whole
        // difference between air and a wall — a hard-edged field snaps particles at its boundary,
        // which is the visual the report was about arriving by a second route.
        _field.startRange = 0f;
        _field.rotationRandomness = RotationRandomness;
        // DRAG BY VELOCITY, DELIBERATELY NOT BY SIZE. By velocity, so drag is a resistance rather
        // than a brake: a fast particle is slowed most and an already-slow one is not frozen in
        // mid-air, which is how "high drag" usually goes wrong and it is the failure mode that
        // would look every bit as unnatural as the bounce this replaces.
        //
        // By SIZE was written first and taken back out, and the reason is this project's oldest
        // trap. Unity multiplies by the particle's WORLD size, and this mod's rigs run from about
        // x4 in a scenario to x198 in the map room — so the same authored smoke would carry fifty
        // times the drag in one room as in the other, from a term nobody had written a scale into.
        // "Big things feel more drag" is kept, but as a per-class number in VfxFlowProfile, where
        // it is a decision instead of a side effect of how big the room happens to be.
        _field.multiplyDragByParticleSize = false;
        _field.multiplyDragByParticleVelocity = true;
        _idle = false;
        return true;
    }

    /// <summary>Point the field, size it, and write this frame's forces.</summary>
    /// <param name="position">World position of the palm or fingertip.</param>
    /// <param name="axis">The VORTEX AXIS, in world space, already normalised. Unity turns
    /// particles about the field's own up direction, so the transform is rotated to put its Y along
    /// this. For the palm it is the direction the hand is travelling — a ring of smoke shed behind
    /// a moving palm, which is what wafting looks like. For the fingertip it is the direction the
    /// finger points, so the plume spirals along the finger.</param>
    /// <param name="radius">Field radius in WORLD units (a real length times the rig scale).</param>
    /// <param name="wake">0..1: how hard the hand is currently moving, already smoothed.</param>
    /// <param name="scale">World units per real metre, for the two accelerations.</param>
    internal void Apply(in VfxFlowProfile p, Vector3 position, Vector3 axis, Vector3 pushDirection,
                        float radius, float wake, float scale,
                        float push, float cling, float curl, bool tip)
    {
        if (_field == null || _transform == null)
            return;
        _idle = false;
        _transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, axis));
        _field.endRange = Mathf.Max(radius, 1e-3f);

        // THE DIRECTIONAL PUSH IS THE HAND'S OWN VELOCITY. A constant push would blow every effect
        // in one direction forever; a push proportional to how fast the hand is moving is what
        // makes a slow hand barely disturb and a swipe waft — which is the sentence in the report.
        float accel = p.Push * push * wake * scale * (tip ? TipPushFactor : 1f);
        Vector3 force = pushDirection * accel;
        _field.directionX = force.x;
        _field.directionY = force.y;
        _field.directionZ = force.z;

        // Negative gravity with the focus at the centre = an outward push, which carves the pocket
        // the hand occupies. Positive would suck the effect INTO the palm. The SIGN is the one
        // thing in this file a reading of the API cannot settle, so it is written down where a
        // hardware round can read it: SceneVfxHands.NoteFlowClass prints this term per effect and
        // carries the marked question. If effects visibly collapse into the palm instead of
        // parting around it, this line is the term to flip and nothing else here is implicated.
        _field.gravityFocus = 0f;
        _field.gravity = -p.Repel * scale;

        _field.drag = p.Drag * cling;
        // A STILL HAND STILL CURLS A LITTLE. Gating the vortex entirely on hand speed would make a
        // hand held motionless in a plume look like a hole cut out of it; a third of the swirl at
        // rest is the difference between "an object is in the smoke" and "an object is ignoring it".
        _field.rotationSpeed = p.Vortex * curl * (0.35f + 0.65f * wake)
                             * (tip ? TipVortexBoost : 1f);
        _field.rotationAttraction = p.Attract * curl;
    }

    /// <summary>Zero every force. Called for a class this hand is not holding — see the class doc
    /// for why that is a safety property and not housekeeping. Writes once and then early-outs, so
    /// an unused field costs one boolean test a frame.</summary>
    internal void Idle()
    {
        if (_field == null || _idle)
            return;
        _idle = true;
        _field.directionX = 0f;
        _field.directionY = 0f;
        _field.directionZ = 0f;
        _field.gravity = 0f;
        _field.drag = 0f;
        _field.rotationSpeed = 0f;
        _field.rotationAttraction = 0f;
    }

    internal void Destroy()
    {
        if (_go != null)
            Object.Destroy(_go);
        _go = null;
        _transform = null;
        _field = null;
        _idle = false;
    }
}
