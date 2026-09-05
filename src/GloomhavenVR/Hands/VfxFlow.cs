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

    /// <summary>HOW FAR A PARTICLE OF THIS CLASS MAY BE CARRIED, in its OWN particle widths, over
    /// its whole remaining life. The anchor of the displacement bound — see
    /// <see cref="VfxFlow.SpeedCapRealPerSecond"/> — and the term that decides whether an effect
    /// stays where it was authored.
    ///
    /// <para>Read as five sentences again: a flame may lean less than one width and is therefore
    /// still visibly attached to its brazier; an aura may move about one, because it is owned; a
    /// smoke puff wafts a few; dust drifts a good many of its own tiny widths, which is what a
    /// wake looks like; a spark is the only thing here that may be thrown.</para></summary>
    internal readonly float Drift;

    private VfxFlowProfile(float drag, float vortex, float attract, float push, float repel,
                           float radius, float dampen, float lifetimeLoss, float drift)
    {
        Drag = drag;
        Vortex = vortex;
        Attract = attract;
        Push = push;
        Repel = repel;
        Radius = radius;
        Dampen = dampen;
        LifetimeLoss = lifetimeLoss;
        Drift = drift;
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
    /// <para><b>THESE WERE STARTING POINTS AND ModBuild 432 IS WHERE THE FIRST HARDWARE ROUND
    /// LANDED ON THEM.</b> The previous revision of this paragraph said the absolute magnitudes
    /// were a first guess because Unity documents neither "drag" nor "rotation speed" against
    /// anything. That guess came back as: "Bei der Flamme beim Altar glitcht die Flamme in der
    /// Gegend rum." The numbers are UNCHANGED — they were never the defect on their own — and what
    /// is new is the <see cref="Drift"/> column and the bound built on it, which converts each of
    /// them from an absolute into a ceiling measured against the effect's own size and lifetime.
    /// The RATIOS are still the designed part; they are now the ratios in which a bounded budget
    /// is spent rather than the ratios of an unbounded one.</para>
    ///
    /// <para><b>THE DRAG COLUMN NO LONGER MEANS WHAT IT SAYS ON THE SPARKS ROW</b>, and that is
    /// worth saying here rather than only at the field. <see cref="VfxFlowField"/> imposes a drag
    /// FLOOR, because the bound is enforced through drag — terminal speed under Unity's
    /// velocity-multiplied drag is acceleration ÷ drag — so a class with almost no drag has no
    /// terminal speed to bound. Sparks at 0.18 was exactly that class, and 'PrimeAltar_FX' in the
    /// ModBuild 431 log is exactly that effect: a 5-second Sparks system whose push of
    /// 2.2 m/s² against a drag of 0.18 has a terminal speed of twelve metres per second. Above the
    /// floor the column reads as written.</para>
    /// </summary>
    internal static VfxFlowProfile Of(VfxFlowClass c) => c switch
    {
        //                            drag  vortex attract push  repel radius dampen loss  drift
        VfxFlowClass.Smoke  => new(   1.50f, 2.40f,  0.60f, 1.00f, 0.12f, 1.35f, 0.90f, 0.00f, 3.0f),
        VfxFlowClass.Flame  => new(   0.60f, 0.50f,  0.25f, 0.15f, 0.90f, 0.90f, 0.75f, 0.06f, 0.8f),
        VfxFlowClass.Sparks => new(   0.18f, 0.90f,  0.15f, 2.20f, 0.30f, 1.10f, 0.35f, 0.00f,10.0f),
        VfxFlowClass.Motes  => new(   3.00f, 1.60f,  0.40f, 1.60f, 0.05f, 1.60f, 0.95f, 0.00f, 6.0f),
        _                   => new(   1.10f, 3.00f,  1.40f, 0.25f, 0.10f, 0.80f, 0.80f, 0.00f, 1.0f),
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
    /// reads better than the smoke treatment (wrap it around the hand).
    ///
    /// <para>INTERNAL RATHER THAN PRIVATE SINCE ModBuild 432, and only for a reader. It is THE ONE
    /// BOUNDARY THE RIG SCALE CAN MOVE AN EFFECT ACROSS — every other term in
    /// <see cref="Classify"/> is scale-free — so <c>SceneVfxHands</c> prints, per effect, the rig
    /// scale at which that effect would cross it. That division needs this number, and a second
    /// copy of it written down at the log site is exactly the mirrored constant
    /// scripts/check-mirrors.sh exists to hunt: the log would go on quoting 0.10 after somebody
    /// moved the line.</para></summary>
    internal const float BulkDiameterMetres = 0.10f;

    /// <summary>A BIG particle that lives less than this is a flame tongue; longer, and it is
    /// smoke. Flame effects run at roughly a second per particle and smoke at several, so 1.8 s
    /// sits between two populations rather than inside one.</summary>
    private const float FlameSeconds = 1.8f;

    /// <summary>Measure the six terms. Allocation-free; every read is a native property get and
    /// none of them is a string.</summary>
    /// <param name="ps">The system to measure.</param>
    /// <param name="onFigure">Whether an <c>ActorBehaviour</c> owns it (already computed by the
    /// registry sweep; passed in rather than re-derived per call).</param>
    /// <param name="wuPerRealMetre">The live rig scale, and the caller guarantees it came from a
    /// TRACKED HAND rather than from a seed — <c>SceneVfxHands.Rescan</c> refuses to classify at
    /// all until one has reported (see its <c>_rigScaleSampled</c> guard), because at a scale of 1
    /// every particle reads its raw world size and the whole room lands above the bulk line at
    /// once. It affects only <see cref="VfxTraits.DiameterMetres"/>, but that is the term the
    /// Smoke/Flame/Motes boundary is drawn on, and the scale is the player's LIVE ZOOM rather than
    /// a property of the room: it walked from 23.49 to 2.14 world units per real metre inside the
    /// ModBuild 431 session. A verdict is therefore only true of the scale it was taken at, which
    /// is why the caller re-runs this for every held system when the scale drifts and prints the
    /// scale on every verdict it logs.</param>
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

    /// <summary>A particle whose authored size is driven entirely by a curve reads 0 metres across
    /// (see <see cref="Measure"/>), and a bound anchored on 0 is a bound of zero — an effect that
    /// can never be felt at all. One centimetre of real particle is the floor, which is about a
    /// fingernail and is smaller than anything in the ModBuild 431 census (the smallest measured
    /// was 'center' at 0.011 real m).</summary>
    private const float MinDiameterMetres = 0.01f;

    /// <summary>…and the same guard on the other term. A lifetime this short divides into the drift
    /// budget to give a speed, so a system reporting a near-zero lifetime would otherwise be handed
    /// an unbounded one. A quarter second is below every lifetime the census measured (the shortest
    /// was 'fx_sparks_drop' at 0.70 s).</summary>
    private const float MinLifeSeconds = 0.25f;

    /// <summary>HOW FAR THE HAND MAY CARRY ONE PARTICLE, in real metres, over its whole remaining
    /// life. THIS IS THE ANSWER TO "die Flamme glitcht in der Gegend rum".
    ///
    /// <para><b>THE ANCHOR THE REPORT ASKED FOR DOES NOT EXIST ON MOST OF THE POPULATION, and that
    /// is the measurement this method is built around.</b> The obvious way to express a force as a
    /// fraction of what an effect already does is its own travel budget — start speed × lifetime.
    /// In the ModBuild 431 log that budget is <b>literally zero on 21 of the 33 classified
    /// systems</b>: 'GroundFog', 'GroundFogFar', 'ElemSpores', 'ElemGust', 'ElemEmbers', 'center',
    /// 'P_CastHealRadial (4)', 'fx_sparks_drop' and the 'Particle System' at the brazier all read
    /// "travel 0.0 of its own widths per life", i.e. <c>startSpeedMultiplier</c> is 0 and every
    /// pixel of their motion comes from a curve, a noise module or nothing at all. A fraction of
    /// zero is zero, so a force scaled that way would make two thirds of the room inert.</para>
    ///
    /// <para><b>SO THE ANCHOR IS THE PARTICLE'S OWN SIZE, WHICH IS NEVER ZERO</b>, times the
    /// class's <see cref="VfxFlowProfile.Drift"/> allowance — and then an ABSOLUTE cap in real
    /// metres on top of it, which is the term that makes the guarantee unconditional. The absolute
    /// cap is what stops 'GroundFogFar', whose single particle measures <b>17.1 real metres
    /// across</b>, from being allowed seventeen metres of travel by a rule about its own widths.
    /// That particle is the pale wash the altar video shows sliding across the room: one enormous
    /// soft billboard whose centre came within a palm's reach of the hand, taking the whole quad
    /// with it.</para>
    ///
    /// <para>Returned in real metres per second, because that is the unit a rig scale converts
    /// cleanly and the unit the bound is enforced in. Allocation-free; four arithmetic ops.</para>
    /// </summary>
    /// <param name="t">The traits measured for this system.</param>
    /// <param name="p">Its class profile, for the <see cref="VfxFlowProfile.Drift"/> allowance.</param>
    /// <param name="absoluteCapMetres">HandsVfxDriftMeters: the unconditional ceiling.</param>
    internal static float SpeedCapRealPerSecond(in VfxTraits t, in VfxFlowProfile p,
                                                float absoluteCapMetres)
        => DriftMetres(in t, in p, absoluteCapMetres) / Mathf.Max(t.LifeSeconds, MinLifeSeconds);

    /// <summary>The drift budget itself, in real metres — the numerator of
    /// <see cref="SpeedCapRealPerSecond"/>, split out so the log can print the distance as well as
    /// the speed.</summary>
    internal static float DriftMetres(in VfxTraits t, in VfxFlowProfile p, float absoluteCapMetres)
        => Mathf.Min(p.Drift * Mathf.Max(t.DiameterMetres, MinDiameterMetres),
                     Mathf.Max(absoluteCapMetres, 0f));

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
/// <para><b>AND SINCE ModBuild 432 EVERY FIELD CARRIES A CEILING IT CANNOT EXCEED.</b> The user,
/// on 431: "Bei der Flamme beim Altar glitcht die Flamme in der Gegend rum." A force field is an
/// ACCELERATION and nothing in Unity's model ever takes back the speed it gave — a particle
/// leaves the field carrying whatever velocity it picked up and coasts on that for the rest of its
/// life, which on a 22-second fog particle is a very long way. The remedy is a bound, not a smaller
/// guess: <see cref="VfxFlow.SpeedCapRealPerSecond"/> turns each system's own measured size and
/// lifetime into the fastest this field may ever get one of its particles moving, and
/// <see cref="Apply"/> spends the resulting acceleration budget in the ratios
/// <see cref="VfxFlowProfile"/> designed. The bound is expressed in REAL metres and converted at
/// use time, so it means the same thing in a x4 scenario and a x198 map room.</para>
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

    /// <summary>THE DRAG FLOOR, in reciprocal seconds, and it is what makes the displacement bound
    /// enforceable rather than aspirational.
    ///
    /// <para>Unity's force-field drag with <c>multiplyDragByParticleVelocity</c> on decelerates a
    /// particle at <c>drag × |v|</c>, so a particle under a constant acceleration <c>a</c> inside
    /// the field asymptotes to <c>a ÷ drag</c> and never exceeds it, whatever the transit time. That
    /// single identity is the whole bound: pick the speed the effect is allowed to reach, multiply
    /// by the drag actually written to the field, and that product is the acceleration budget.</para>
    ///
    /// <para>WITHOUT A FLOOR THE IDENTITY IS USELESS ON EXACTLY THE CLASS THAT NEEDED IT. Sparks
    /// carries drag 0.18, so its terminal speed is five and a half times its acceleration, and
    /// 'PrimeAltar_FX' — the altar effect the user filmed — is a Sparks system with a 5-second
    /// lifetime: 2.2 m/s² ÷ 0.18 = 12 m/s, carried for five seconds, is sixty metres. 1.2 is a
    /// one-second-ish e-fold inside the field, mild enough that a particle is not frozen (the
    /// failure mode the cling paragraph warns about, which needs a drag several times this) and
    /// firm enough that the budget is a real number on every row of the table.</para></summary>
    private const float MinDrag = 1.2f;

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
    /// <param name="speedCapWorld">THE DISPLACEMENT BOUND, in WORLD units per second: the fastest
    /// this field may ever get a particle moving. Comes from
    /// <see cref="VfxFlow.SpeedCapRealPerSecond"/> for the strictest system this hand holds of this
    /// class, times the rig scale.</param>
    /// <param name="gain">0..1 attack envelope, so a class this hand has just started holding fades
    /// its air in over a fraction of a second instead of switching it on between two frames.</param>
    internal void Apply(in VfxFlowProfile p, Vector3 position, Vector3 axis, Vector3 pushDirection,
                        float radius, float wake, float scale, float speedCapWorld, float gain,
                        float push, float cling, float curl, bool tip)
    {
        if (_field == null || _transform == null)
            return;
        _idle = false;
        _transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, axis));
        _field.endRange = Mathf.Max(radius, 1e-3f);

        // THE DRAG IS WRITTEN FIRST BECAUSE EVERYTHING BELOW IS MEASURED AGAINST IT. See MinDrag:
        // terminal speed inside this field is acceleration ÷ drag, so the drag actually written
        // here is the number the budget on the next line is computed from. Writing a floored drag
        // and then budgeting against the UNFLOORED one would be an instrument measuring the
        // bookkeeping instead of the thing.
        float drag = Mathf.Max(p.Drag * cling, MinDrag);
        _field.drag = drag;

        // THE ACCELERATION BUDGET. Everything this field is allowed to do, expressed as the one
        // number that bounds it: a particle held at this acceleration reaches speedCapWorld and
        // stops accelerating. The push and the outward term then SHARE it in the ratio
        // VfxFlowProfile designed, so trimming preserves the character of the class and only its
        // magnitude moves. The vortex is bounded separately and for a different reason — see the
        // paragraph on it below.
        float cap = Mathf.Max(speedCapWorld, 0f);
        float budget = cap * drag;

        // THE DIRECTIONAL PUSH IS THE HAND'S OWN VELOCITY. A constant push would blow every effect
        // in one direction forever; a push proportional to how fast the hand is moving is what
        // makes a slow hand barely disturb and a swipe waft — which is the sentence in the report.
        float accel = p.Push * push * wake * gain * scale * (tip ? TipPushFactor : 1f);
        float repel = p.Repel * gain * scale;
        // The push and the outward term are both radial-ish and can point the same way, so they are
        // trimmed against a SHARED budget rather than one each.
        float wanted = accel + repel;
        if (wanted > budget && wanted > 1e-6f)
        {
            float trim = budget / wanted;
            accel *= trim;
            repel *= trim;
        }
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
        _field.gravity = -repel;

        // A STILL HAND STILL CURLS A LITTLE. Gating the vortex entirely on hand speed would make a
        // hand held motionless in a plume look like a hole cut out of it; a third of the swirl at
        // rest is the difference between "an object is in the smoke" and "an object is ignoring it".
        //
        // TWO THINGS CHANGED HERE IN ModBuild 432 AND BOTH ARE ABOUT SCALE. The term is now
        // multiplied by the rig scale like the other two — it is a world-unit quantity and it was
        // the only force in this method that was not, so the same authored swirl meant twenty times
        // as much of a room at x9 as at x198 — and it is then clamped to the cap DIRECTLY rather
        // than through the budget. Unity's documentation does not settle whether rotationSpeed is a
        // target tangential speed or a tangential acceleration; clamping the raw number at the cap
        // is correct under the first reading and strictly conservative under the second (an
        // acceleration of `cap` against a drag of at least 1.2 asymptotes below `cap`). The swirl
        // is tangential and the push is along the field axis, so the two are orthogonal by
        // construction and their bounds add in quadrature rather than linearly.
        // WHETHER THE SWIRL STILL READS ONCE IT IS BOUNDED is the open question here, and it is
        // asked where a hardware round can answer it rather than in a comment nobody prints:
        // SceneVfxHands.NoteFlowClass carries it, with this effect's own ceiling beside it, on a
        // line the default log level prints. If effects now merely slow down near a hand and never
        // wrap around it, this clamp is the term that ate it and HandsVfxDriftMeters gives it back.
        float vortex = p.Vortex * curl * gain * (0.35f + 0.65f * wake)
                     * (tip ? TipVortexBoost : 1f) * scale;
        _field.rotationSpeed = Mathf.Min(vortex, cap);
        _field.rotationAttraction = p.Attract * curl * gain;
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
