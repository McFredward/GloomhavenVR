using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// THE HANDS DISTURB THE SCENERY — curtains, hangings and every other piece of simulated cloth in
/// the room move out of the way when you reach through them, with EITHER hand, holding anything or
/// nothing.
///
/// <para>USER REQUEST (2026-08-29): of the six candidates in the interaction survey, "Ich will die
/// 6" — the environment reacting to the hands. This is that.</para>
///
/// <para>HARDWARE VERDICT, ModBuild 430 (user): "Ich meine mich zu erinnern, dass du auch eine
/// Hand-Interaktion mit Flaggen/Fahnen im Level eingebaut hast - auch das scheint noch nicht zu
/// funktionieren." What that build's log actually proves, and what it does not, is recorded in
/// THE ModBuild-430 READING below, because three separate things were wrong with the way this
/// class reported on itself and one more with the way it CHOSE.</para>
///
/// <para>THE ART IS ALREADY THERE. This adds no assets and authors no cloth: the game's own
/// scenario scenery ships real <c>UnityEngine.Cloth</c> — <c>EN_CR_Curtain_Cloth</c> and
/// <c>EN_CR_Hanging_01_Cloth_Post</c> both appear by name in the wall-fade census
/// (<c>Core/WallFade/WallSegmentFade.*</c>), which is how this was confirmed without a headset.
/// Whatever a room has, this finds; a room with none costs a dictionary lookup and a return.</para>
///
/// <para>THE GAME NEVER TOUCHES THE ARRAY WE WRITE, and that is now established rather than
/// assumed. A full sweep of <c>decompiled/</c> finds exactly two Cloth writes in the entire game:
/// <c>ActorBehaviour</c> (its own actors' <c>clothSolverFrequency</c> at spawn, and <c>enabled</c>
/// around a teleport) and <c>PhysicsController.SetClothesFrequency</c> (<c>clothSolverFrequency</c>
/// again, from two methods with no call sites). There is no occurrence anywhere of
/// <c>sphereColliders</c>, <c>capsuleColliders</c>, <c>coefficients</c>, <c>useGravity</c>,
/// <c>worldVelocityScale</c>, <c>stiffness</c> or <c>damping</c>. So a mod-appended pair cannot be
/// discarded by a game-side rewrite, and "the game re-writes the array each frame" is DEAD as an
/// explanation for anything.</para>
///
/// <para>IT IS THE SAME PROBE <see cref="Board.FigureGrab.FigureClothHands"/> ALREADY USES, pointed
/// somewhere else. A <see cref="ClothSphereColliderPair"/> is a conic capsule between two spheres,
/// which is the shape of a hand from palm to fingertip; the pair sits on a scene-root object at
/// unit scale on Unity's Ignore Raycast layer, so a radius written in world units IS that radius in
/// the world and nothing can pick it. That class measured the cost and the numbers carry over
/// unchanged, because it is the same operation on the same array: assigning
/// <c>sphereColliders</c> is 0.008–0.017 ms and — the part that settles it — FLAT in vertex count,
/// while a cloth re-cook is linear in it. Assigning does not re-cook.</para>
///
/// <para>WHAT IS DIFFERENT HERE, and each difference is a hazard the figure version does not have:</para>
/// <list type="bullet">
/// <item><description>TWO HANDS, not one. The figure probe exists only while exactly one hand holds
/// a mini, so it can be a single static probe. Here either hand may be doing anything, so there are
/// two probes and a cloth may be touched by BOTH at once. That makes the capture/restore
/// bookkeeping the hard part of this file — see <see cref="Rewrite"/>.</description></item>
/// <item><description>MANY CLOTHS, not one figure's. Capped at
/// <see cref="MaxClothsPerHand"/> per hand, NEAREST FIRST — see <c>Probe.Acquire</c>, which is
/// where that long-documented property finally became true.</description></item>
/// <item><description>NO OWNER TO HANG THE SCAN OFF. A held figure gives the other class a root to
/// walk; a room does not, so this keeps a slowly-refreshed registry — see
/// <see cref="RescanSeconds"/>.</description></item>
/// </list>
///
/// <para>ACTOR CLOTH IS EXCLUDED, STRUCTURALLY. Any cloth with an <c>ActorBehaviour</c> above it
/// belongs to a figure, and figures already have an owner for this
/// (<see cref="Board.FigureGrab.FigureClothHands"/>). Both classes write the WHOLE
/// <c>sphereColliders</c> array, so two owners on one cloth would clobber each other's capture and
/// leave a stale probe in it forever. The exclusion is the fix, and it is also simply what the user
/// asked for: this one is about the environment. It is the same set the game itself calls actor
/// cloth: <c>ActorBehaviour.cs:122</c> collects <c>m_Clothes</c> with
/// <c>GetComponentsInChildren&lt;Cloth&gt;()</c> from the actor's Animator object.</para>
///
/// <para>MULTIPLAYER — ZERO WIRE BYTES, and the same reasoning the figure version records: cloth
/// vertex positions have never been a wire field, every client simulates its own scenery, and each
/// player's own hands disturb their own copy. Nothing here is authoritative, nothing desyncs, and a
/// peer who is not running this build sees their curtains hang still.</para>
///
/// <para>SCALE. Every radius and every reach is REAL METRES AT THE HAND multiplied by
/// <c>VRHand.WorldScale</c> — the convention <c>ProximityGrabber.ReachMeters</c> and
/// <c>FigureGrabConfig.PickRadiusRealMeters</c> already follow, and the one the user's own ruling
/// demands ("der Bereich nicht größer wird mit dem zoomen sondern an der Hand bleibt"). The mod's
/// zoom scales the RIG, so the room keeps its world size while the player grows: a hand-anchored
/// reach is the only kind that still means "my hand is in the curtain" at every zoom. Because this
/// project has ALSO shipped a bound named <c>…Meters</c> clamped against a WORLD-unit product at
/// 198x rig scale, the census prints every one of these numbers in BOTH units with the scale beside
/// them, and lets the numbers argue instead of this paragraph.</para>
///
/// <para><b>THE ModBuild-430 READING.</b> Four defects, each named here with what PROVED it, so
/// that the next round does not re-litigate them:</para>
/// <list type="number">
/// <item><description>THE CENSUS NEVER MEASURED A SCENARIO. It was gated on
/// <c>_scansTimed &lt; 3</c> — the first three sweeps EVER — and those three always land in the
/// menu, before a room exists. The 430 log carries six of them (two runs of three, the counter
/// being reset by <see cref="Shutdown"/>) and every single one reads
/// <c>0 simulating scenery cloth(s) of 0 found</c>. The instrument answered a question nobody asked
/// and then went silent for the whole session. It is now change-triggered with a heartbeat and an
/// unconditional sweep counter, so silence means STOPPED and never means EMPTY.</description></item>
/// <item><description>IT LOGGED BELOW THE DEFAULT LEVEL. Both lines were <c>VRLog.Info</c>, which
/// the ModBuild-331 mapping puts on the DEBUG tier. The 430 log happens to have been captured at
/// Debug so the lines survive — but at the shipped default this class is mute, which is the exact
/// failure <c>scripts/check-hw-verify.py</c> exists to catch. Both are <c>Note</c> and marked
/// now.</description></item>
/// <item><description>THE REGISTRY SWEEP RAN EVERY FRAME WHENEVER THE ROOM HAD NO CLOTH. The
/// cadence test read <c>if (now &lt; _nextScanAt &amp;&amp; _scene.Count &gt; 0) return;</c>, so an
/// EMPTY registry — the entire menu, and every room without a curtain — bypassed the throttle and
/// ran <c>FindObjectsOfType&lt;Cloth&gt;</c> once per frame. That is this project's top frame-cost
/// suspect, and the 430 log corroborates it: the perf spikes at frames 843/856/881 attribute
/// 0.01–0.02 ms to <c>Hands.SceneCloth</c> in the menu, where the registry is empty and the class
/// should have been doing nothing at all. The population term is gone.</description></item>
/// <item><description>IT ARMED THE FIRST FOUR CLOTHS IN RANGE, NOT THE NEAREST FOUR. The acquire
/// loop walked <c>_scene</c> in registry order — i.e. <c>FindObjectsOfType</c> order, which is
/// arbitrary — and stopped at <see cref="MaxClothsPerHand"/>. With a reach of 0.25 real metres and
/// a row of banners 0.9 world units apart (the 430 wall-fade census measures
/// <c>EN_CR_Hanging_01_Cloth_Post</c> at exactly that width), a hand inside ONE banner can have
/// more than four in range, and the four it arms need not include the one it is touching. This is a
/// candidate root cause for "nothing reacts" that costs nothing to remove, and the class doc had
/// claimed "nearest first" all along. <c>Probe.Acquire</c> now selects by distance.</description></item>
/// </list>
///
/// <para><b>WHAT IS STILL OPEN, AND WHY NO SECOND MECHANISM IS BUILT HERE.</b> That the scenario
/// contains real, non-actor, simulating <c>Cloth</c> and that a hand reached one is PROVEN by the
/// 430 log: the first-contact line fired. WHICH object it was is NOT proven, and the banners the
/// user is asking about may not be that object. The wall-fade census places
/// <c>EN_CR_Hanging_01_Cloth_Post</c> under <c>Wall 1/Generated Content/PCG_CR_Banner_Grey/</c>
/// with an AABB of <c>s(0.9, 0.1, 0.1)</c> at <c>y 2.5..2.5</c> — a flat slab at the hanging RAIL
/// with no vertical extent, beside a sibling <c>EN_CR_Hanging_01_Mesh</c>. If that renderer bound
/// is what this class's distance gate reads, then the gate's own stated justification ("the fabric
/// is metres of it below, so an origin distance would arm the probe only when the hand was up at
/// the rail") is defeated by the BOUNDS being at the rail too. The census now prints each cloth's
/// renderer bounds and its hierarchy path so one hardware run settles it. These props come out of
/// Apparance (<c>ProceduralProp</c>), whose prop names live in native data and not in managed code,
/// so <c>decompiled/</c> cannot answer it and neither can this comment.</para>
///
/// <para>A CLOTH PINNED RIGID CANNOT MOVE HOWEVER CORRECT THE COLLIDER IS. That is why the census
/// profiles each cloth once — vertex count, how many vertices have <c>maxDistance == 0</c>, the
/// stiffnesses, damping, gravity, the world velocity/acceleration scales and the authored sphere
/// count. Reading <c>Cloth.coefficients</c> allocates one struct per vertex per read, so it is read
/// through <c>GetCoefficients(List&lt;&gt;)</c> into one reused buffer, ONCE per cloth, never per
/// frame.</para>
/// </summary>
internal static class SceneClothHands
{
    /// <summary>Fallback palm sphere before the config binds, real metres at the hand. The wide end
    /// of the conic capsule, and THE strength dial: a fatter hand shoves more fabric. The shipped
    /// value lives in <c>Defaults.SceneryClothHandRadiusMillimeters</c> and reaches here through
    /// <see cref="HandsConfig.SceneryClothHandRadiusRealMeters"/>.</summary>
    private const float PalmRadiusRealMeters = 0.035f;

    /// <summary>Fingertip sphere as a SHARE of the palm sphere — the authored 10 mm against 35 mm.
    /// Kept as a ratio rather than a second dial so the taper survives every setting of the one
    /// dial there is: two numbers of one family must move together.</summary>
    private const float TipShareOfPalm = 10f / 35f;

    /// <summary>How close the palm must come to a cloth's BOUNDS before its probe is attached, in
    /// real metres at the hand. Generous compared with the figure pick radius on purpose: this
    /// arms a collider, it does not grab anything, so being early costs a few microseconds and
    /// being late costs the hand passing through a curtain untouched.</summary>
    private const float ReachRealMeters = 0.25f;

    /// <summary>The reach never drops below this multiple of the palm sphere. Without it, a player
    /// who turns the hand up to its maximum thickness would get a probe that arms barely before it
    /// is already inside the fabric — the gate must stay ahead of the thing it gates.</summary>
    private const float ReachAtLeastTimesRadius = 3f;

    /// <summary>Hysteresis on the release side of that gate — the same 1.5 the figure probe uses,
    /// so a hand hovering on the boundary cannot churn the assignment every frame.</summary>
    private const float ReleaseFactor = 1.5f;

    /// <summary>Most cloths one hand arms at once. A hand cannot physically be inside more than a
    /// couple; the cap is what stops a room full of banners turning one reach into a sweep. It is
    /// also why <c>Probe.Acquire</c> must take the NEAREST rather than the first four — see defect
    /// 4 in the class doc.</summary>
    private const int MaxClothsPerHand = 4;

    /// <summary>
    /// Seconds between registry rescans.
    ///
    /// <para>THE SCAN IS THE ONLY REAL COST IN THIS FILE and it is a
    /// <c>FindObjectsOfType</c>, which this project has been burned by twice — "near-free is false
    /// and has shipped twice", and once it owned 12.6 ms of an 11.11 ms budget. So it does NOT run
    /// per frame: it runs at this cadence and only while a hand is tracked. It used to SAY that and
    /// not do it, because an empty registry bypassed the test — see defect 3 in the class doc. Every
    /// sweep is timed and the count is printed with it, so the next hardware log carries the real
    /// number instead of this comment's assurance.</para>
    /// </summary>
    private const float RescanSeconds = 3f;

    /// <summary>How long the census may stay quiet while nothing about the POPULATION changes. It
    /// is a heartbeat and not a cadence: the line is emitted whenever the population signature
    /// moves, and otherwise at this interval, so a reader can tell a stopped sweep from an empty
    /// room. Deliberately NOT driven by the per-hand numbers — a hand waving in and out of a
    /// curtain would then emit on every sweep and rebuild the flood ModBuild 331 removed.</summary>
    private const float CensusHeartbeatSeconds = 30f;

    /// <summary>How many cloths the census names in full. A truncated list is not absence, so the
    /// line always states the total it was drawn from.</summary>
    private const int CensusNamed = 6;

    /// <summary>Unity's built-in Ignore Raycast layer — invisible to
    /// <c>Physics.DefaultRaycastLayers</c>, so neither the mod's picking nor the game's can see the
    /// probe spheres.</summary>
    private const int IgnoreRaycastLayer = 2;

    private static readonly Probe Left = new(HandSide.Left);
    private static readonly Probe Right = new(HandSide.Right);

    /// <summary>One registry row: the cloth and the renderer whose BOUNDS the distance gate reads.
    /// The renderer used to be fetched with a <c>GetComponent</c> inside the gate, i.e. twice per
    /// cloth per hand per frame; the class doc had described this cache since the day it was
    /// written and the code had never built it.</summary>
    private readonly struct Entry
    {
        internal readonly Cloth Cloth;
        internal readonly Renderer? Renderer;

        internal Entry(Cloth cloth, Renderer? renderer)
        {
            Cloth = cloth;
            Renderer = renderer;
        }
    }

    // The registry: every scenery cloth in the scene with its renderer. Rebuilt on a slow cadence;
    // entries whose cloth died are dropped on use.
    private static readonly List<Entry> _scene = new(16);
    private static float _nextScanAt;

    // LIVENESS. Incremented by EVERY sweep, whatever the sweep finds, and printed in every census
    // line. A frozen number means the sweep stopped; it can never mean "the room is empty".
    private static int _sweeps;

    // Census gating: the last emitted POPULATION signature and when it was emitted.
    private static int _lastPopulation = -1;
    private static float _lastCensusAt = float.NegativeInfinity;
    private static float _lastSweepMs;
    private static int _lastFound;
    private static int _lastActorOwned;
    private static int _lastNotSimulating;

    // ORIGINAL collider arrays, captured ONCE per cloth on the first attach by either hand and
    // written back verbatim when the last hand lets go. Keyed by cloth because a curtain may be
    // touched by both hands at the same time and there is exactly one authored array to restore.
    private static readonly Dictionary<Cloth, ClothSphereColliderPair[]> _original = new(8);

    // Per-cloth profile, captured once and kept for as long as the cloth stays in the registry.
    private static readonly Dictionary<Cloth, Profile> _profile = new(16);
    private static readonly List<Cloth> _profileDrop = new(8);

    private static readonly System.Text.StringBuilder _census = new(1024);

    private static bool _loggedFirstAttach;

    /// <summary>The feature switch ([Hands] HandsDisturbScenery); false before the config is bound,
    /// which is the same defensive shape <c>HandGhosts.Enabled</c> uses and for the same reason —
    /// this is called from a per-frame path that runs before and after the config's lifetime.</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                return HandsConfig.HandsDisturbScenery != null
                       && HandsConfig.HandsDisturbScenery.Value;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Per-frame step, called from <see cref="HandsDriver"/> under its own
    /// <see cref="TickGuard"/>. Strict no-op when the feature is off or no hand is tracked.</summary>
    internal static void Tick()
    {
        if (!Enabled)
        {
            Clear();
            return;
        }

        VRHand? left = VRHands.Left;
        VRHand? right = VRHands.Right;
        bool anyTracked = (left != null && left.IsTracked) || (right != null && right.IsTracked);
        if (!anyTracked)
        {
            Clear();
            return;
        }

        bool swept = Rescan();
        Left.Tick(left);
        Right.Tick(right);
        // The census runs AFTER both probes so its per-hand fields are THIS frame's, not the
        // previous frame's. A reader comparing "in reach" against "armed" is then comparing two
        // numbers taken at the same instant, which is the only way that comparison means anything.
        if (swept)
            Census();
    }

    /// <summary>Module shutdown / hot reload / scene change: restore every cloth and destroy the
    /// probes. The arrays are written back FIRST — a scene that is merely being re-entered keeps
    /// its curtains, and a curtain left holding a destroyed collider is a curtain that never
    /// simulates correctly again.</summary>
    internal static void Shutdown()
    {
        Clear();
        Left.Destroy();
        Right.Destroy();
        _scene.Clear();
        _profile.Clear();
        _nextScanAt = 0f;
        _loggedFirstAttach = false;
        // _sweeps is NOT reset. It is the liveness field, and a counter that restarts at zero
        // cannot distinguish a fresh scene from a stalled sweep — which is the one thing it is
        // for. The census names the population separately.
        _lastPopulation = -1;
        _lastCensusAt = float.NegativeInfinity;
    }

    private static void Clear()
    {
        Left.Release();
        Right.Release();
        RestoreAll();
    }

    /// <summary>Rebuild the registry if the cadence is due. Returns true when a sweep actually
    /// ran.</summary>
    private static bool Rescan()
    {
        float now = Time.unscaledTime;
        // NO POPULATION TERM. It used to read `now < _nextScanAt && _scene.Count > 0`, which made
        // an empty registry bypass its own throttle and run FindObjectsOfType every frame — for
        // the whole menu, and for every room without a curtain. See defect 3 in the class doc.
        if (now < _nextScanAt)
            return false;
        _nextScanAt = now + RescaleGuard(RescanSeconds);
        _sweeps++;

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Cloth[] found = Object.FindObjectsOfType<Cloth>();
        _scene.Clear();
        int actorOwned = 0;
        int notSimulating = 0;
        for (int i = 0; i < found.Length; i++)
        {
            Cloth c = found[i];
            if (c == null)
                continue;
            if (!c.enabled)
            {
                // COUNTED, NOT SILENTLY DROPPED. "There is no cloth here" and "there is cloth here
                // and it is switched off" are different verdicts with different fixes, and the old
                // census could not tell them apart.
                notSimulating++;
                continue;   // a cloth that is not simulating cannot be disturbed by anything
            }
            // ACTOR CLOTH IS SOMEBODY ELSE'S. See the class doc: FigureClothHands owns a held
            // figure's cape, both classes write the whole array, and two owners on one cloth
            // corrupt each other's capture. "Is this cloth part of an actor?" is a RELATEDNESS
            // question, which is the one thing GetComponentInParent actually answers.
            if (c.GetComponentInParent<ActorBehaviour>() != null)
            {
                actorOwned++;
                continue;
            }
            _scene.Add(new Entry(c, c.GetComponent<Renderer>()));
        }

        _lastSweepMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
                               / System.Diagnostics.Stopwatch.Frequency);
        _lastFound = found.Length;
        _lastActorOwned = actorOwned;
        _lastNotSimulating = notSimulating;

        Profiles();
        return true;
    }

    /// <summary>The rescan cadence, never below a frame — a defensive clamp so a future dial or a
    /// zero constant cannot turn the registry sweep into a per-frame one.</summary>
    private static float RescaleGuard(float seconds) => Mathf.Max(0.5f, seconds);

    /// <summary>Capture the once-per-cloth profile for anything new in the registry and forget
    /// anything that left it. Runs on the rescan cadence, never per frame, and never twice for the
    /// same cloth — which is the point, because reading <c>Cloth.coefficients</c> marshals one
    /// struct per vertex and for a curtain that is thousands.</summary>
    private static void Profiles()
    {
        for (int i = 0; i < _scene.Count; i++)
        {
            Cloth c = _scene[i].Cloth;
            if (c == null || _profile.ContainsKey(c))
                continue;
            _profile[c] = Profile.Capture(c, _scene[i].Renderer);
        }

        if (_profile.Count <= _scene.Count)
            return;
        _profileDrop.Clear();
        foreach (KeyValuePair<Cloth, Profile> kv in _profile)
        {
            bool live = false;
            for (int i = 0; i < _scene.Count && !live; i++)
                live = ReferenceEquals(_scene[i].Cloth, kv.Key);
            if (!live)
                _profileDrop.Add(kv.Key);
        }
        for (int i = 0; i < _profileDrop.Count; i++)
            _profile.Remove(_profileDrop[i]);
        _profileDrop.Clear();
    }

    /// <summary>Rewrite one cloth's collider array from the ORIGINAL plus whichever hands are
    /// currently on it, and restore + forget it when neither is.
    ///
    /// <para>This is the whole reason the captures live in a dictionary keyed by cloth rather than
    /// in each probe. With two hands the naive per-probe capture is wrong in a way that does not
    /// show up until both touch the same curtain: hand B captures an array that ALREADY contains
    /// hand A's pair, and when B restores it, A's pair is written back into a cloth A has long
    /// since left — permanently, because A restored its own (correct) capture first. One capture
    /// per cloth, taken by whoever arrives first, makes that unrepresentable.</para></summary>
    private static void Rewrite(Cloth? c)
    {
        if (ReferenceEquals(c, null))
            return;
        Cloth cloth = c;
        if (cloth == null)
        {
            // DESTROYED, but still a live C# reference — a curtain torn down while a hand was
            // inside it. There is nothing left to restore, and leaving the capture in the
            // dictionary keeps a dead Unity object alive as a key for the rest of the session.
            // The Unity-null test and the reference test disagree here on purpose; that
            // disagreement IS the destroyed case.
            _original.Remove(cloth!);
            _profile.Remove(cloth!);
            return;
        }
        bool l = Left.Holds(c);
        bool r = Right.Holds(c);
        if (!_original.TryGetValue(c, out ClothSphereColliderPair[] original))
        {
            if (!l && !r)
                return;
            original = c.sphereColliders ?? System.Array.Empty<ClothSphereColliderPair>();
            _original[c] = original;
        }

        if (!l && !r)
        {
            c.sphereColliders = original;
            _original.Remove(c);
            return;
        }

        int extra = (l ? 1 : 0) + (r ? 1 : 0);
        var next = new ClothSphereColliderPair[original.Length + extra];
        for (int k = 0; k < original.Length; k++)
            next[k] = original[k];          // APPENDED — the authored body colliders stay
        int at = original.Length;
        if (l)
            next[at++] = Left.Pair;
        if (r)
            next[at] = Right.Pair;
        c.sphereColliders = next;

        if (!_loggedFirstAttach)
        {
            _loggedFirstAttach = true;
            _profile.TryGetValue(c, out Profile p);
            float scale = Mathf.Max(Left.LastScale, Right.LastScale, 1e-4f);
            float radius = HandsConfig.SceneryClothHandRadiusRealMeters;
            // HW-VERIFY
            VRLog.Note("Hands", "Scenery cloth: a hand is inside a curtain/hanging for the first "
                + "time — its authored colliders are kept and the hand's sphere pair is APPENDED. "
                + "Restored verbatim when the last hand leaves. No wire field: every client "
                + "simulates its own scenery and its own hands. "
                + $"SWEEP {_sweeps}. THE CLOTH: '{c.name}' at '{p.Path}', "
                + $"{original.Length} authored pair(s) + {extra} hand pair(s) = {next.Length}; "
                + $"{p.Vertices} vertex/vertices of which {p.Pinned} are PINNED "
                + "(maxDistance 0 — those cannot move however correct the collider is), "
                + $"{p.Free} free, maxDistance {p.MinMaxDistance:0.###}..{p.MaxMaxDistance:0.###}; "
                + $"gravity={p.UseGravity} stretch={p.Stretching:0.###} bend={p.Bending:0.###} "
                + $"damping={p.Damping:0.###} worldVel={p.WorldVelocity:0.###} "
                + $"worldAcc={p.WorldAcceleration:0.###} friction={p.Friction:0.###} "
                + $"solver={p.SolverFrequency:0.#}Hz sleep={p.SleepThreshold:0.###}; "
                + $"renderer bounds centre {Fmt(p.BoundsCenter)} size {Fmt(p.BoundsSize)} wu. "
                + $"HAND: rig scale x{scale:0.###} ({scale:0.##} world units per real metre), "
                + $"palm sphere {radius * 1000f:0.#} mm real = {radius * scale:0.###} wu. "
                + "IF THIS LINE APPEARS AND NOTHING VISIBLY MOVED, the collider reached the solver "
                + "and the answer is in the numbers above — a fully pinned cloth, a sphere far "
                + "smaller than the fabric, or bounds that are not where the fabric is. "
                + "Dial: [Hands] SceneryClothHandRadiusMillimeters.");
        }
    }

    private static void RestoreAll()
    {
        if (_original.Count == 0)
            return;
        foreach (KeyValuePair<Cloth, ClothSphereColliderPair[]> pair in _original)
        {
            if (pair.Key != null)
                pair.Key.sphereColliders = pair.Value;
        }
        _original.Clear();
    }

    private static string Fmt(Vector3 v) => $"({v.x:0.##},{v.y:0.##},{v.z:0.##})";

    /// <summary>
    /// THE LINE THAT DECIDES THE FEATURE, and the reason it is shaped the way it is.
    ///
    /// <para>It is emitted whenever the POPULATION signature moves and otherwise every
    /// <see cref="CensusHeartbeatSeconds"/>, so it can go quiet only while nothing changes and
    /// never for a whole session. Every line carries the sweep counter, which moves on EVERY sweep
    /// whatever the picture is doing: a frozen counter means the sweep stopped, an unchanged
    /// population with a moving counter means the room really is empty. The old census could say
    /// neither, because it printed the first three sweeps ever — all three in the menu — and then
    /// never spoke again.</para>
    ///
    /// <para>Every distance and radius appears in BOTH world units and real metres with the rig
    /// scale beside them, because this project has already shipped a bound named <c>…Meters</c>
    /// clamped against a world-unit product at 198x scale, and the only defence against that is
    /// printing both and letting the numbers argue.</para>
    /// </summary>
    private static void Census()
    {
        int population = (_lastFound * 397) ^ (_scene.Count * 131) ^ (_lastActorOwned * 31)
                         ^ _lastNotSimulating;
        float now = Time.unscaledTime;
        bool due = population != _lastPopulation || now - _lastCensusAt >= CensusHeartbeatSeconds;
        if (!due)
            return;
        _lastPopulation = population;
        _lastCensusAt = now;

        float scale = Mathf.Max(Left.LastScale, Right.LastScale, 1e-4f);
        float radius = HandsConfig.SceneryClothHandRadiusRealMeters;
        float reach = ReachOf(radius);

        _census.Length = 0;
        // The opening sentence is the ModBuild-430 token, kept word for word so a grep that found
        // the old line still finds this one. Everything new is APPENDED.
        _census.Append("Scenery cloth scan #").Append(_sweeps).Append(": ")
               .Append(_scene.Count).Append(" simulating scenery cloth(s) of ").Append(_lastFound)
               .Append(" found (").Append(_lastActorOwned).Append(" skipped as actor cloth) in ")
               .Append(_lastSweepMs.ToString("F3")).Append(" ms. This runs every ")
               .Append(RescanSeconds.ToString("0.#"))
               .Append(" s while a hand is tracked, never per frame.");
        _census.Append(" NOT SIMULATING: ").Append(_lastNotSimulating)
               .Append(" cloth(s) exist with Cloth.enabled false — a switched-off cloth and no "
                       + "cloth at all are different verdicts and this separates them.");
        _census.Append(" SWEEP ").Append(_sweeps)
               .Append(" (this counter moves on EVERY sweep whatever is found, so a frozen number "
                       + "means the sweep STOPPED and never means the room is empty).");
        _census.Append(" SCALE x").Append(scale.ToString("0.###"))
               .Append(" world units per real metre: reach ")
               .Append((reach * 1000f).ToString("0.#")).Append(" mm real = ")
               .Append((reach * scale).ToString("0.###")).Append(" wu; palm sphere ")
               .Append((radius * 1000f).ToString("0.#")).Append(" mm real = ")
               .Append((radius * scale).ToString("0.###")).Append(" wu; tip ")
               .Append((radius * TipShareOfPalm * 1000f).ToString("0.#")).Append(" mm real = ")
               .Append((radius * TipShareOfPalm * scale).ToString("0.###")).Append(" wu.");

        CensusHand(Left, "LEFT");
        CensusHand(Right, "RIGHT");

        _census.Append(" CLOTHS (naming ").Append(Mathf.Min(CensusNamed, _scene.Count))
               .Append(" of ").Append(_scene.Count)
               .Append(", and a truncated list is not absence)");
        if (_scene.Count == 0)
        {
            _census.Append(": none. With SWEEP moving, this is a room with no simulated scenery "
                           + "cloth at all — not a stalled instrument.");
        }
        for (int i = 0; i < _scene.Count && i < CensusNamed; i++)
        {
            Cloth c = _scene[i].Cloth;
            if (c == null)
                continue;
            _profile.TryGetValue(c, out Profile p);
            _census.Append(i == 0 ? ": " : "; ")
                   .Append('\'').Append(c.name).Append("' at '").Append(p.Path).Append("' ")
                   .Append(p.Vertices).Append(" vert, ").Append(p.Pinned)
                   .Append(" PINNED (maxDistance 0, cannot move however correct the collider is), ")
                   .Append(p.Free).Append(" free, maxDistance ")
                   .Append(p.MinMaxDistance.ToString("0.###")).Append("..")
                   .Append(p.MaxMaxDistance.ToString("0.###"))
                   .Append(" | gravity=").Append(p.UseGravity)
                   .Append(" stretch=").Append(p.Stretching.ToString("0.###"))
                   .Append(" bend=").Append(p.Bending.ToString("0.###"))
                   .Append(" damping=").Append(p.Damping.ToString("0.###"))
                   .Append(" worldVel=").Append(p.WorldVelocity.ToString("0.###"))
                   .Append(" worldAcc=").Append(p.WorldAcceleration.ToString("0.###"))
                   .Append(" friction=").Append(p.Friction.ToString("0.###"))
                   .Append(" solver=").Append(p.SolverFrequency.ToString("0.#")).Append("Hz")
                   .Append(" | ").Append(p.AuthoredSpheres).Append(" authored sphere pair(s)")
                   .Append(" | bounds c").Append(Fmt(p.BoundsCenter))
                   .Append(" size").Append(Fmt(p.BoundsSize)).Append(" wu")
                   .Append(" | palm L ").Append(DistanceText(Left, c))
                   .Append(" R ").Append(DistanceText(Right, c));
        }
        _census.Append(" READ IT LIKE THIS: 'found' 0 means the room has no UnityEngine.Cloth at "
                       + "all and no radius can help; 'found' non-zero with inReach 0 on both "
                       + "hands means the hand never gets close enough and the reach or the BOUNDS "
                       + "are wrong (a hanging whose renderer bounds sit at the rail arms only "
                       + "when the hand is up at the rail, which is exactly the failure the bounds "
                       + "gate was supposed to prevent — compare the bounds size against the "
                       + "fabric you can see); armed non-zero with nothing visibly moving means "
                       + "the collider reached the solver and the cloth's own coefficients or "
                       + "stiffnesses are the answer. Dial: [Hands] "
                       + "SceneryClothHandRadiusMillimeters.");
        // HW-VERIFY
        VRLog.Note("Hands", _census.ToString());
    }

    private static void CensusHand(Probe probe, string label)
    {
        _census.Append(' ').Append(label).Append(": tracked=").Append(probe.LastTracked)
               .Append(" scale=x").Append(probe.LastScale.ToString("0.###"))
               .Append(" inReach=").Append(probe.LastInReach)
               .Append(" armed=").Append(probe.HeldCount)
               .Append(" nearest=");
        Cloth? nearest = probe.LastNearest;
        if (nearest == null)
        {
            _census.Append("none");
            return;
        }
        _census.Append('\'').Append(nearest.name).Append("' at ")
               .Append(probe.LastNearestDistance.ToString("0.###")).Append(" wu = ")
               .Append((probe.LastNearestDistance / Mathf.Max(probe.LastScale, 1e-4f) * 1000f)
                       .ToString("0.#")).Append(" mm real");
    }

    /// <summary>One hand's distance to one cloth, in both units. The real-metre half is divided by
    /// THIS hand's own scale and never by a scale shared with the other hand: the two hands can sit
    /// on different rig scales for a frame after a zoom, and a distance converted by the wrong one
    /// is a number that looks right and is not.</summary>
    private static string DistanceText(Probe probe, Cloth c)
    {
        float d = probe.DistanceTo(c);
        if (d < 0f)
            return "n/a";
        return $"{d:0.###} wu = {d / Mathf.Max(probe.LastScale, 1e-4f) * 1000f:0.#} mm real";
    }

    /// <summary>The attach reach for a given palm radius. Never less than
    /// <see cref="ReachAtLeastTimesRadius"/> times the sphere, so turning the hand up cannot leave
    /// the gate behind the thing it gates.</summary>
    private static float ReachOf(float radius)
        => Mathf.Max(ReachRealMeters, radius * ReachAtLeastTimesRadius);

    /// <summary>One cloth's simulation configuration, captured once. Every field here is a reason a
    /// visibly correct collider can produce no visible motion, which is why they are printed
    /// together rather than guessed at one hardware round at a time.</summary>
    private readonly struct Profile
    {
        internal readonly string Path;
        internal readonly int Vertices;
        internal readonly int Pinned;
        internal readonly int Free;
        internal readonly float MinMaxDistance;
        internal readonly float MaxMaxDistance;
        internal readonly int AuthoredSpheres;
        internal readonly bool UseGravity;
        internal readonly float Stretching;
        internal readonly float Bending;
        internal readonly float Damping;
        internal readonly float WorldVelocity;
        internal readonly float WorldAcceleration;
        internal readonly float Friction;
        internal readonly float SolverFrequency;
        internal readonly float SleepThreshold;
        internal readonly Vector3 BoundsCenter;
        internal readonly Vector3 BoundsSize;

        private Profile(string path, int vertices, int pinned, int free, float minMax, float maxMax,
                        int authored, bool gravity, float stretching, float bending, float damping,
                        float worldVelocity, float worldAcceleration, float friction,
                        float solverFrequency, float sleepThreshold, Vector3 center, Vector3 size)
        {
            Path = path;
            Vertices = vertices;
            Pinned = pinned;
            Free = free;
            MinMaxDistance = minMax;
            MaxMaxDistance = maxMax;
            AuthoredSpheres = authored;
            UseGravity = gravity;
            Stretching = stretching;
            Bending = bending;
            Damping = damping;
            WorldVelocity = worldVelocity;
            WorldAcceleration = worldAcceleration;
            Friction = friction;
            SolverFrequency = solverFrequency;
            SleepThreshold = sleepThreshold;
            BoundsCenter = center;
            BoundsSize = size;
        }

        internal static Profile Capture(Cloth c, Renderer? renderer)
        {
            int pinned = 0;
            int free = 0;
            float minMax = float.PositiveInfinity;
            float maxMax = 0f;
            int vertices = 0;
            try
            {
                // ONCE PER CLOTH, NEVER PER FRAME, and that is the whole reason this is a memoized
                // profile rather than a field read at census time. `Cloth.coefficients` is the only
                // public accessor and it marshals a fresh ClothSkinningCoefficient[] on every read
                // — thousands of structs for a curtain. (Unity's allocation-free list overload is a
                // native binding with no public C# surface in 2021.3; the strings in
                // UnityEngine.ClothModule.dll are NativeName attributes, not methods. Checked, not
                // assumed.) The array is walked here and dropped; nothing keeps it.
                ClothSkinningCoefficient[] coefficients = c.coefficients;
                vertices = coefficients.Length;
                for (int i = 0; i < coefficients.Length; i++)
                {
                    float m = coefficients[i].maxDistance;
                    if (m <= 0f)
                    {
                        pinned++;
                    }
                    else if (float.IsInfinity(m) || m >= float.MaxValue)
                    {
                        free++;
                    }
                    else
                    {
                        if (m < minMax) minMax = m;
                        if (m > maxMax) maxMax = m;
                    }
                }
            }
            catch
            {
                // A cloth mid-teardown can throw out of the marshalling. The rest of the profile is
                // still worth having, and an instrument must never be the thing that breaks.
            }
            if (float.IsPositiveInfinity(minMax))
                minMax = 0f;

            int authored;
            try
            {
                ClothSphereColliderPair[]? spheres = c.sphereColliders;
                authored = spheres?.Length ?? 0;
            }
            catch
            {
                authored = -1;
            }

            Bounds b = renderer != null
                ? renderer.bounds
                : new Bounds(c.transform.position, Vector3.zero);
            return new Profile(PathOf(c.transform), vertices, pinned, free, minMax, maxMax,
                               authored, c.useGravity, c.stretchingStiffness, c.bendingStiffness,
                               c.damping, c.worldVelocityScale, c.worldAccelerationScale,
                               c.friction, c.clothSolverFrequency, c.sleepThreshold,
                               b.center, b.size);
        }

        /// <summary>The hierarchy path, capped at six levels above the cloth. It is the only field
        /// that says WHICH prop a cloth belongs to — the 430 wall-fade census names
        /// <c>Wall 1/Generated Content/PCG_CR_Banner_Grey/EN_CR_Hanging_01_Cloth (1)</c> and nothing
        /// in this class could previously be matched against it.</summary>
        private static string PathOf(Transform t)
        {
            string path = t.name;
            Transform? p = t.parent;
            for (int depth = 0; p != null && depth < 6; depth++, p = p.parent)
                path = p.name + "/" + path;
            return path;
        }
    }

    /// <summary>One hand's probe: two spheres at the scene root, the cloths it is currently inside,
    /// and the gate that decides which those are.</summary>
    private sealed class Probe
    {
        private readonly HandSide _side;
        private readonly List<Cloth> _held = new(MaxClothsPerHand);
        private GameObject? _root;
        private Transform? _palm;
        private Transform? _tip;
        private SphereCollider? _palmSphere;
        private SphereCollider? _tipSphere;

        /// <summary>This hand's palm-to-cloth distance per registry row, in world units, refilled
        /// once per Tick and read by the release gate, by <see cref="Acquire"/> and by the census.
        ///
        /// <para>PER PROBE, NOT SHARED. A single static scratch array is the obvious saving and it
        /// is wrong: the two probes tick in sequence, so by the time the census runs the array
        /// holds the RIGHT hand's distances and would report them as the left hand's — a ratio with
        /// two populations, in one buffer. It is grown, never reallocated per frame.</para></summary>
        private float[] _distance = new float[16];

        // Census state, written every Tick and read by Census() in the same frame.
        internal bool LastTracked;
        internal float LastScale = 1f;
        internal int LastInReach;
        internal Cloth? LastNearest;
        internal float LastNearestDistance;

        internal Probe(HandSide side) => _side = side;

        internal ClothSphereColliderPair Pair => new(_palmSphere, _tipSphere);

        internal bool Holds(Cloth c) => _held.Contains(c);

        internal int HeldCount => _held.Count;

        /// <summary>Palm-to-cloth distance in WORLD UNITS for the census, or -1 when this hand is
        /// not tracked or the cloth is not in the registry.</summary>
        internal float DistanceTo(Cloth c)
        {
            if (!LastTracked)
                return -1f;
            for (int i = 0; i < _scene.Count; i++)
            {
                if (ReferenceEquals(_scene[i].Cloth, c))
                    return _distance[i];
            }
            return -1f;
        }

        internal void Tick(VRHand? hand)
        {
            LastTracked = false;
            LastInReach = 0;
            LastNearest = null;
            LastNearestDistance = 0f;

            if (hand == null || !hand.IsTracked || hand.Rig == null || !hand.Rig.IsComplete)
            {
                Release();
                return;
            }
            if (!Ensure())
            {
                Release();
                return;
            }

            LastTracked = true;
            float scale = Mathf.Max(hand.WorldScale, 1e-4f);
            LastScale = scale;
            Vector3 palm = hand.Rig.PalmCenter.position;
            float radius = HandsConfig.SceneryClothHandRadiusRealMeters;
            float reach = ReachOf(radius) * scale;

            // The probe object is at the scene root at unit scale, so these radii ARE world radii.
            _palm!.position = palm;
            _tip!.position = hand.Rig.IndexTip.position;
            _palmSphere!.radius = radius * scale;
            _tipSphere!.radius = radius * TipShareOfPalm * scale;

            // ONE distance pass over the registry, into this probe's own scratch array. Everything
            // below reads it: the release gate, the nearest-first selection and the census. It used
            // to be a GetComponent<Renderer>() per cloth per hand per frame inside the gate itself.
            if (_distance.Length < _scene.Count)
                _distance = new float[Mathf.NextPowerOfTwo(_scene.Count)];
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < _scene.Count; i++)
            {
                Entry e = _scene[i];
                float d = e.Cloth == null ? float.PositiveInfinity : Distance(e, palm);
                _distance[i] = d;
                if (d <= reach)
                    LastInReach++;
                if (d < nearest)
                {
                    nearest = d;
                    LastNearest = e.Cloth;
                    LastNearestDistance = d;
                }
            }
            if (LastNearest == null)
                LastNearestDistance = 0f;

            // DROP what is out of range first, on the WIDER release radius, then take what is in.
            // Doing it in that order means a hand sliding along a row of banners lets go of one
            // before it can be refused the next by the cap.
            float releaseAt = reach * ReleaseFactor;
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                Cloth c = _held[i];
                if (c == null || !c.enabled || DistanceOf(c, palm) > releaseAt)
                {
                    _held.RemoveAt(i);
                    Rewrite(c);
                }
            }

            Acquire(reach);
        }

        /// <summary>
        /// Take the NEAREST cloths in range, up to <see cref="MaxClothsPerHand"/>.
        ///
        /// <para>This used to walk the registry in order and stop at the cap, i.e. it armed the
        /// first four in range rather than the nearest four — and registry order is
        /// <c>FindObjectsOfType</c> order, which is arbitrary. In a room with a row of banners
        /// inside one generous reach, the four it armed need not have included the one the hand was
        /// actually touching, and the feature would look dead while behaving exactly as written.
        /// The class doc had said "nearest first" since the day it was written; this is where that
        /// became true. O(cap x registry) with no allocation, over a registry of a handful.</para>
        /// </summary>
        private void Acquire(float reach)
        {
            while (_held.Count < MaxClothsPerHand)
            {
                int best = -1;
                float bestDistance = float.PositiveInfinity;
                for (int i = 0; i < _scene.Count; i++)
                {
                    Cloth c = _scene[i].Cloth;
                    if (c == null || !c.enabled)
                        continue;
                    float d = _distance[i];
                    if (d > reach || d >= bestDistance)
                        continue;
                    if (_held.Contains(c))
                        continue;
                    best = i;
                    bestDistance = d;
                }
                if (best < 0)
                    return;
                Cloth chosen = _scene[best].Cloth;
                _held.Add(chosen);
                Rewrite(chosen);
            }
        }

        /// <summary>Palm-to-cloth distance, measured against the cloth's RENDERER BOUNDS rather
        /// than its transform origin. A curtain's origin is its hanging point; the fabric is metres
        /// of it below, so an origin distance would arm the probe only when the hand was up at the
        /// rail.
        ///
        /// <para>WHETHER THAT ACTUALLY HELPS DEPENDS ON THE BOUNDS, and the ModBuild-430 wall-fade
        /// census says it may not: <c>EN_CR_Hanging_01_Cloth_Post</c> measures
        /// <c>s(0.9, 0.1, 0.1)</c> at <c>y 2.5..2.5</c>, a flat slab AT the rail. The census prints
        /// these bounds per cloth so the next log settles it rather than this comment.</para></summary>
        private static float Distance(Entry e, Vector3 palm)
        {
            if (e.Renderer == null)
                return Vector3.Distance(e.Cloth.transform.position, palm);
            return Mathf.Sqrt(e.Renderer.bounds.SqrDistance(palm));
        }

        /// <summary>The cached distance for a cloth this hand still holds, falling back to a fresh
        /// measurement for one that has left the registry since it was armed — a held cloth must
        /// still be releasable after a rescan drops it, or its capture never gets written back.</summary>
        private float DistanceOf(Cloth c, Vector3 palm)
        {
            for (int i = 0; i < _scene.Count; i++)
            {
                if (ReferenceEquals(_scene[i].Cloth, c))
                    return _distance[i];
            }
            var r = c.GetComponent<Renderer>();
            return r == null
                ? Vector3.Distance(c.transform.position, palm)
                : Mathf.Sqrt(r.bounds.SqrDistance(palm));
        }

        internal void Release()
        {
            LastTracked = false;
            LastInReach = 0;
            LastNearest = null;
            if (_held.Count == 0)
                return;
            var was = _held.ToArray();
            _held.Clear();
            for (int i = 0; i < was.Length; i++)
                Rewrite(was[i]);
        }

        internal void Destroy()
        {
            Release();
            if (_root != null)
                Object.Destroy(_root);
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
                Object.Destroy(_root);

            _root = new GameObject($"GloomhavenVR.SceneClothProbe.{_side}")
            { layer = IgnoreRaycastLayer };
            Object.DontDestroyOnLoad(_root);
            var palmGo = new GameObject("Palm") { layer = IgnoreRaycastLayer };
            var tipGo = new GameObject("Tip") { layer = IgnoreRaycastLayer };
            palmGo.transform.SetParent(_root.transform, false);
            tipGo.transform.SetParent(_root.transform, false);
            _palm = palmGo.transform;
            _tip = tipGo.transform;
            _palmSphere = palmGo.AddComponent<SphereCollider>();
            _tipSphere = tipGo.AddComponent<SphereCollider>();
            _palmSphere.radius = PalmRadiusRealMeters;
            _tipSphere.radius = PalmRadiusRealMeters * TipShareOfPalm;
            return true;
        }
    }
}
