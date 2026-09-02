using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// WHY A HELD PROP'S OWN ANIMATION RUNS SLOWER IN THE HAND — the A/B instrument, and NOTHING ELSE.
///
/// <para><b>THE REPORT (2026-09-03), verbatim.</b> <i>"Die Truhe spielt so eine 'aufblitz-animation'
/// ab, damit der Spieler sie besser sieht. Das ist vom Spiel selber. Ich will das diese Animation
/// auch normal weiter abspielt wenn man die Truhe in der Hand hat. Aktuell ist die Animation dort
/// etwas kaputt - deutlich langsamer und kommt mir auch nicht so flüssig vor."</i></para>
///
/// <para><b>THIS CLASS SHIPS NO REMEDY, DELIBERATELY.</b> The project's standing lesson is that a
/// fix gated behind the instrument shipped to test it never executes, so "no improvement" carries
/// zero information. Nothing here writes anything the game reads: it reads fields, accumulates
/// numbers and prints one line. If the next hardware round says the numbers are identical in both
/// windows, that is an ANSWER — it kills every candidate below at once and moves the question to
/// the one term this cannot see (named at the end of the verdict).</para>
///
/// <para><b>WHAT IS ALREADY KNOWN, so the round is not spent re-deriving it.</b></para>
/// <list type="bullet">
///   <item>The chest is the ONLY prop with an Animator. The ModBuild 356 hardware log's own
///   census says so: <c>MINIATURE AUDIT 8: ''Chest' Chest' (… 1 Animator(s) with a controller
///   under the actor)</c>, against <c>0 Animator(s)</c> for <c>'OneHexObstacle' Obstacle</c> and
///   <c>'GoldPile' MoneyToken</c>. So the flash is Animator-driven and the Animator terms below
///   are the ones that decide it.</item>
///   <item>The flash does NOT add a renderer while the prop is held. The same log's hold watch
///   read <c>present=4..4 renderer(s)</c> and <c>rebuilt=0</c> across 560 frames, so nothing
///   appeared under the chest for the whole hold. That matters because
///   <c>SpawnObjectAnimateMaterial_SMB.OnStateEnter</c> does
///   <c>Object.Destroy(Object.Instantiate(particles, animator.transform), lifetime)</c> — a
///   particle prefab parented under the prop, which IS a renderer. Either that state was never
///   entered during the hold, or that SMB is not what plays this flash. Hence
///   <see cref="_smb"/>: the watch reads the SMB directly rather than inferring it.</item>
///   <item>CHRONOS IS ALMOST CERTAINLY POSITION-BLIND IN THIS GAME. <c>Chronos.Timeline.Update</c>
///   (Timeline.cs:154-197) computes <c>timeScale = clock.timeScale</c> and then multiplies in
///   <c>IAreaClock.TimeScale(this)</c> for every area clock it is inside — and
///   <c>AreaClock.TimeScale</c> (AreaClock.cs:154-176) evaluates a curve over
///   <c>timeline.gameObject.transform.position</c> in the <c>PointToEdge</c> and
///   <c>DistanceFromEntry</c> modes. That is the only position-dependent term in the whole
///   framework, and moving a chest from its hex into a palm is the largest position change it
///   could get. BUT: no game code anywhere references <c>Chronos.Timeline</c>,
///   <c>AreaClock</c>, <c>AreaClock2D/3D</c>, <c>LocalClock</c> or <c>GlobalClock</c> as types —
///   105 files <c>using Chronos</c> and every one of them touches only
///   <c>Timekeeper.instance.m_GlobalClock</c>. Code cannot prove absence (both are
///   <c>[AddComponentMenu]</c> components and could be attached in scene data), so
///   <see cref="CensusScene"/> asks the runtime ONCE per session and settles it for good.</item>
///   <item>The two prop SMBs latch <c>animator.speed = Timekeeper.instance.m_GlobalClock.timeScale</c>
///   ONCE, in <c>OnStateEnter</c>, and never refresh it
///   (SpawnObjectAnimateMaterial_SMB.cs:24, DelayedDeactivatePropAnimSMB.cs:58). The global clock
///   is <c>0.25</c> during slow-mo (<c>TimeManager.SlowMoTimeScale</c>) and <c>0</c> while frozen.
///   So a state ENTERED inside a slow-mo window keeps a 4x-slow animator for the whole state, with
///   no position involved at all. That is why the animator SPEED and the global-clock terms are
///   sampled in BOTH windows as min/max ranges and not merely once at the grab.</item>
/// </list>
///
/// <para><b>THE TWO WINDOWS, AND WHY THEY ARE HOLD AND POST-LANDING.</b> A rate cannot be read off
/// one sample, so both halves of the comparison need frames. HAND is the hold itself. HOME is a
/// window that opens <see cref="HomeSettleFrames"/> frames AFTER the release glide lands — the same
/// prop, on its own hex, seconds later, with the Apparance thaw (<c>GrabbableProp.ThawDelayFrames</c>
/// = 3) already consumed. A hover window before the grab was considered and dropped: it is not
/// guaranteed to exist, it is not guaranteed to contain a flash, and while it lasts
/// <c>FigureHighlight</c> has parented glow clones under the very subtree this walks. Post-landing
/// has none of those problems and is a genuine home-cell measurement of the identical object.</para>
///
/// <para><b>EVERY TERM IS PRINTED FOR BOTH WINDOWS, INCLUDING THE ONES THAT DID NOT MOVE.</b> A
/// term that reads the same at the hex and in the palm is not the cause, and saying so in the line
/// is worth as much as finding the one that changed — it is what stops the next round re-testing
/// a candidate this round already killed.</para>
///
/// <para><b>COST.</b> One <c>GetComponentsInChildren</c> into a shared scratch list per frame while
/// a window is open, over a prop subtree of under two dozen nodes, for at most ONE prop at a time
/// and at most <see cref="Budget"/> verdicts per session — after which nothing arms and
/// <see cref="Tick"/> returns on its first field test. No allocation per frame. The scene census
/// in <see cref="CensusScene"/> is ONE <c>Resources.FindObjectsOfTypeAll</c> pair, once per
/// session, on the first arm; it is a scene sweep and it is named as one, and it buys the single
/// measurement that kills or confirms the Chronos candidate for every future round.</para>
///
/// <para><b>MULTIPLAYER.</b> Read-only and local: a diagnostic that samples this client's own
/// components and prints. Nothing goes on the wire and no peer behaviour changes.</para>
/// </summary>
internal static class PropAnimWatch
{
    /// <summary>How many verdicts this session prints. Two is one chest grabbed twice, or a chest
    /// and a second animated prop — enough to see whether the reading repeats, and few enough that
    /// the per-frame walk stops for good early in a session.</summary>
    private const int Budget = 2;

    /// <summary>Frames after the release glide lands before the HOME window opens. Past
    /// <c>GrabbableProp.ThawDelayFrames</c> (3), so the Apparance <c>MonitorMovement</c> restore
    /// and the one bounds re-sync it consumes are outside the measurement.</summary>
    private const int HomeSettleFrames = 8;

    /// <summary>Frames the HOME window stays open — about four seconds at 90 Hz. Generous on
    /// purpose: the flash is periodic, and a window too short to contain one would report an
    /// honest zero rate that reads exactly like a stopped animation.</summary>
    private const int HomeWindowFrames = 360;

    /// <summary>Frames between re-resolutions of the component set. The set is re-read rather than
    /// trusted because a prop's generated content can be destroyed and re-instantiated under us —
    /// that is the whole of ModBuild 349 — and a watch holding dead references would report a
    /// stopped animation for a prop that is animating fine.</summary>
    private const int RescanFrames = 30;

    /// <summary>How many animators / SMBs / timelines are reported. A prop has one animator; the
    /// cap is here so a pathological prefab cannot turn one log line into forty.</summary>
    private const int ReportCap = 3;

    private enum Phase
    {
        Idle,
        Hand,
        HomePending,
        Home,
    }

    // ---- the scratch lists, shared and cleared per use (no per-frame allocation) ---------------
    private static readonly List<Animator> AnimScratch = new(4);
    private static readonly List<ParticleSystem> ParticleScratch = new(8);
    private static readonly List<Chronos.Timeline> TimelineScratch = new(4);

    private static int _left = Budget;
    private static Phase _phase = Phase.Idle;
    private static GameObject? _target;
    private static string _label = string.Empty;
    private static int _phaseFrame;

    // --- the resolved component set, re-read every RescanFrames.
    private static Animator[] _animators = System.Array.Empty<Animator>();
    private static SpawnObjectAnimateMaterial_SMB[] _smb = System.Array.Empty<SpawnObjectAnimateMaterial_SMB>();
    private static Chronos.Timeline[] _timelines = System.Array.Empty<Chronos.Timeline>();
    private static int _nextRescan;

    // --- the SCENE census, taken once per session on the first arm (see CensusScene).
    private static bool _sceneCensusDone;
    private static int _sceneAreaClocks = -1;
    private static int _sceneTimelines = -1;

    /// <summary>
    /// One window's worth of accumulated terms. Two of these exist — HAND and HOME — and the whole
    /// verdict is a field-by-field diff of them. Everything is a min/max or a rate rather than a
    /// last-seen value, because the question is about a rate and a snapshot of a rate is a coin
    /// toss (the same mistake the ModBuild 340 one-shot census made about the flicker).
    /// </summary>
    private sealed class Window
    {
        internal int Frames;
        internal float RealSeconds;

        // per-animator (index-aligned with _animators, capped at ReportCap)
        internal readonly float[] SpeedMin = NewFilled(float.MaxValue);
        internal readonly float[] SpeedMax = NewFilled(float.MinValue);
        internal readonly int[] CullModes = new int[ReportCap];   // bitmask over AnimatorCullingMode
        internal readonly int[] UpdateModes = new int[ReportCap]; // bitmask over AnimatorUpdateMode
        internal readonly int[] EnabledFrames = new int[ReportCap];
        internal readonly float[] NormAdvance = new float[ReportCap];  // sum of d(normalizedTime)
        internal readonly float[] NormSeconds = new float[ReportCap];  // real seconds those deltas covered
        internal readonly int[] ZeroFrames = new int[ReportCap];       // frames the clip did not advance at all
        internal readonly int[] StateChanges = new int[ReportCap];
        internal readonly int[] Restarts = new int[ReportCap];         // normalizedTime went BACKWARDS

        // per SpawnObjectAnimateMaterial_SMB — the MATERIAL FLASH term, by name
        internal readonly float[] SmbAdvance = new float[ReportCap];   // sum of d(t)
        internal readonly float[] SmbSeconds = new float[ReportCap];
        internal readonly int[] SmbLiveFrames = new int[ReportCap];    // frames t moved at all

        // per Chronos.Timeline found under the prop — the AreaClock candidate's OUTPUT
        internal readonly float[] TimelineMin = NewFilled(float.MaxValue);
        internal readonly float[] TimelineMax = NewFilled(float.MinValue);

        // global clocks
        internal float ClockMin = float.MaxValue, ClockMax = float.MinValue;
        internal float LocalClockMin = float.MaxValue, LocalClockMax = float.MinValue;
        internal float UnityScaleMin = float.MaxValue, UnityScaleMax = float.MinValue;
        internal int ClockPausedFrames;
        internal int ClockMissingFrames;

        // geometry — proves whether the reparent really preserved the world size
        internal float LossyMin = float.MaxValue, LossyMax = float.MinValue;

        // static census, taken on the window's FIRST sample
        internal bool CensusTaken;
        internal int Particles;
        internal int ParticleScalingLocal;
        internal int ParticleSimWorld;
        internal int ParticlesPlaying;

        // HAND only: frames on which someone else had rewritten the local TRS since our last write
        internal int PoseStomps;

        private static float[] NewFilled(float v)
        {
            var a = new float[ReportCap];
            for (int i = 0; i < a.Length; i++)
                a[i] = v;
            return a;
        }
    }

    private static readonly Window Hand = new();
    private static readonly Window Home = new();

    // --- per-animator carry state across frames (NOT per window: a rate needs the previous sample
    //     whichever window it fell in, and a window boundary simply drops one delta).
    private static readonly int[] PrevStateHash = new int[ReportCap];
    private static readonly float[] PrevNormTime = new float[ReportCap];
    private static readonly bool[] PrevValid = new bool[ReportCap];
    private static readonly float[] PrevSmbT = new float[ReportCap];
    private static readonly bool[] PrevSmbValid = new bool[ReportCap];

    /// <summary>True while the HAND window is open — read by <c>GrabbableProp.ApplyHeldPose</c> so
    /// the pose-stomp comparison costs nothing on a hold nobody is watching.</summary>
    internal static bool WatchingHand => _phase == Phase.Hand;

    /// <summary>Arm the watch on a prop entering a hand. Ignored when a watch is already running,
    /// when the budget is spent, or when the prop has no Animator at all — the question is about an
    /// ANIMATION, and a gold pile has none (the ModBuild 356 census counted zero for it), so
    /// spending one of two verdicts on it would answer nothing.</summary>
    internal static void NotifyGrab(GameObject? visual, string label)
    {
        if (_left <= 0 || visual == null)
            return;

        // RE-GRAB DURING THE RELEASE GLIDE. GrabbableProp.OnGrab lands an in-flight glide first,
        // which runs FinishGlide and therefore NotifyLanded — so by the time this is called the
        // watch has already opened its HOME countdown on a prop that is going straight back into
        // the hand. Without this branch the "home" window would be sampled while the prop rides the
        // palm, and the A/B would compare the hand against itself. Resume the HAND window instead
        // and discard whatever the aborted home window collected.
        if (_phase != Phase.Idle)
        {
            if (!ReferenceEquals(visual, _target))
                return;
            _phase = Phase.Hand;
            _phaseFrame = 0;
            _nextRescan = 0;
            Reset(Home);
            for (int i = 0; i < ReportCap; i++)
            {
                PrevValid[i] = false;
                PrevSmbValid[i] = false;
            }
            return;
        }

        AnimScratch.Clear();
        visual.GetComponentsInChildren(includeInactive: true, AnimScratch);
        if (AnimScratch.Count == 0)
            return;

        CensusScene();
        _target = visual;
        _label = label;
        _phase = Phase.Hand;
        _phaseFrame = 0;
        _nextRescan = 0;
        Reset(Hand);
        Reset(Home);
        for (int i = 0; i < ReportCap; i++)
        {
            PrevValid[i] = false;
            PrevSmbValid[i] = false;
        }
        Resolve();
    }

    /// <summary>The prop has landed back on its hex: start counting down to the HOME window. Called
    /// from <c>GrabbableProp.FinishGlide</c>, which is the one place both release paths converge
    /// on. A landing for a prop this watch is not following is ignored.</summary>
    internal static void NotifyLanded(GameObject? visual)
    {
        if (_phase != Phase.Hand || visual == null || !ReferenceEquals(visual, _target))
            return;
        _phase = Phase.HomePending;
        _phaseFrame = 0;
    }

    /// <summary>The prop died under us (looted, broken, scenario teardown). Emit whatever both
    /// windows hold rather than lose the round's only measurement — an incomplete A/B still names
    /// which terms were sampled and which were not.</summary>
    internal static void NotifyGone(GameObject? visual)
    {
        // NO `visual == null` GUARD, and that is the point: the single caller
        // (GrabbableProp.TickHeld) reaches here precisely BECAUSE the visual is Unity-destroyed, so
        // a null test would swallow every call this method exists for. ReferenceEquals compares the
        // managed references, which survive the engine-side destruction, and answers false for a
        // genuinely null argument against a live target.
        if (_phase == Phase.Idle || !ReferenceEquals(visual, _target))
            return;
        Finish("the prop was DESTROYED before the home window finished");
    }

    /// <summary>One frame. Called from <c>PropGrab.Tick</c> ABOVE the feature gate, so a watch
    /// already running still finishes its home window when the dial is turned off mid-flight.
    /// Returns immediately in the steady state.</summary>
    internal static void Tick()
    {
        if (_phase == Phase.Idle)
            return;
        if (_target == null)
        {
            Finish("the prop was DESTROYED before the home window finished");
            return;
        }

        _phaseFrame++;
        switch (_phase)
        {
            case Phase.Hand:
                Sample(Hand);
                return;

            case Phase.HomePending:
                if (_phaseFrame < HomeSettleFrames)
                    return;
                _phase = Phase.Home;
                _phaseFrame = 0;
                _nextRescan = 0;              // the landing may have rebuilt the subtree
                for (int i = 0; i < ReportCap; i++)
                {
                    PrevValid[i] = false;     // never carry a delta ACROSS the window boundary
                    PrevSmbValid[i] = false;
                }
                return;

            case Phase.Home:
                Sample(Home);
                if (_phaseFrame >= HomeWindowFrames)
                    Finish("home window complete");
                return;
        }
    }

    /// <summary>
    /// Report that the held pose we were about to write was NOT what the transform held — i.e.
    /// something else wrote the prop's local TRS since our last write. Called from
    /// <c>GrabbableProp.ApplyHeldPose</c>, and it is the term that decides whether the mod's own
    /// per-frame pose re-assert is STOMPING an animation that drives the transform. A flash that
    /// scales or bobs the chest would be erased by that write, and "erased every frame" and
    /// "playing slower" can look the same to an eye.
    /// </summary>
    internal static void NotePoseStomp()
    {
        if (_phase == Phase.Hand)
            Hand.PoseStomps++;
    }

    /// <summary>Re-arm for a new scenario (a new scenario is a new hardware question), and drop any
    /// watch mid-flight so a dead prop from the last scenario is never sampled.</summary>
    internal static void Reset()
    {
        _left = Budget;
        _phase = Phase.Idle;
        _target = null;
        _animators = System.Array.Empty<Animator>();
        _smb = System.Array.Empty<SpawnObjectAnimateMaterial_SMB>();
        _timelines = System.Array.Empty<Chronos.Timeline>();
    }

    // ---- the measurement ------------------------------------------------------------------------

    private static void Resolve()
    {
        GameObject? go = _target;
        if (go == null)
            return;

        AnimScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, AnimScratch);
        int n = Mathf.Min(AnimScratch.Count, ReportCap);
        if (_animators.Length != n)
            _animators = new Animator[n];
        for (int i = 0; i < n; i++)
            _animators[i] = AnimScratch[i];

        // The SMBs are instantiated by Unity PER ANIMATOR from the controller asset, so they are
        // reached through the animator and never through GetComponent. Only the first animator's
        // are read: a prop has one, and the array is index-aligned with nothing else.
        _smb = System.Array.Empty<SpawnObjectAnimateMaterial_SMB>();
        if (n > 0 && _animators[0] != null && _animators[0].runtimeAnimatorController != null)
        {
            try
            {
                SpawnObjectAnimateMaterial_SMB[] found =
                    _animators[0].GetBehaviours<SpawnObjectAnimateMaterial_SMB>();
                if (found != null)
                    _smb = found;
            }
            catch
            {
                // An animator with no controller bound yet throws rather than returning empty.
                _smb = System.Array.Empty<SpawnObjectAnimateMaterial_SMB>();
            }
        }

        TimelineScratch.Clear();
        try
        {
            go.GetComponentsInChildren(includeInactive: true, TimelineScratch);
        }
        catch
        {
            TimelineScratch.Clear();
        }
        int tn = Mathf.Min(TimelineScratch.Count, ReportCap);
        if (_timelines.Length != tn)
            _timelines = new Chronos.Timeline[tn];
        for (int i = 0; i < tn; i++)
            _timelines[i] = TimelineScratch[i];
    }

    private static void Sample(Window w)
    {
        GameObject? go = _target;
        if (go == null)
            return;

        if (_phaseFrame >= _nextRescan)
        {
            _nextRescan = _phaseFrame + RescanFrames;
            Resolve();
        }

        float dt = Time.unscaledDeltaTime;
        w.Frames++;
        w.RealSeconds += dt;

        float lossy = go.transform.lossyScale.x;
        if (lossy < w.LossyMin) w.LossyMin = lossy;
        if (lossy > w.LossyMax) w.LossyMax = lossy;

        float unity = Time.timeScale;
        if (unity < w.UnityScaleMin) w.UnityScaleMin = unity;
        if (unity > w.UnityScaleMax) w.UnityScaleMax = unity;

        SampleGlobalClock(w);
        SampleAnimators(w, dt);
        SampleSmb(w, dt);
        SampleTimelines(w);

        if (!w.CensusTaken)
        {
            w.CensusTaken = true;
            CensusParticles(go, w);
        }
    }

    private static void SampleGlobalClock(Window w)
    {
        try
        {
            Chronos.Timekeeper keeper = Chronos.Timekeeper.instance;
            Chronos.GlobalClock? clock = keeper != null ? keeper.m_GlobalClock : null;
            if (clock == null)
            {
                w.ClockMissingFrames++;
                return;
            }
            float ts = clock.timeScale, local = clock.localTimeScale;
            if (ts < w.ClockMin) w.ClockMin = ts;
            if (ts > w.ClockMax) w.ClockMax = ts;
            if (local < w.LocalClockMin) w.LocalClockMin = local;
            if (local > w.LocalClockMax) w.LocalClockMax = local;
            if (clock.paused)
                w.ClockPausedFrames++;
        }
        catch
        {
            // Chronos is a game singleton with a lazy FindObjectOfType getter; a scene without a
            // Timekeeper is a real state (the Intro scene has none) and must not throw into a hold.
            w.ClockMissingFrames++;
        }
    }

    private static void SampleAnimators(Window w, float dt)
    {
        for (int i = 0; i < _animators.Length && i < ReportCap; i++)
        {
            Animator a = _animators[i];
            if (a == null)
            {
                PrevValid[i] = false;
                continue;
            }

            float sp = a.speed;
            if (sp < w.SpeedMin[i]) w.SpeedMin[i] = sp;
            if (sp > w.SpeedMax[i]) w.SpeedMax[i] = sp;
            w.CullModes[i] |= 1 << (int)a.cullingMode;
            w.UpdateModes[i] |= 1 << (int)a.updateMode;

            if (!a.isActiveAndEnabled || a.runtimeAnimatorController == null || a.layerCount == 0)
            {
                PrevValid[i] = false;
                continue;
            }
            w.EnabledFrames[i]++;

            AnimatorStateInfo info = a.GetCurrentAnimatorStateInfo(0);
            int hash = info.fullPathHash;
            float nt = info.normalizedTime;

            if (!PrevValid[i])
            {
                PrevValid[i] = true;
                PrevStateHash[i] = hash;
                PrevNormTime[i] = nt;
                continue;
            }
            if (hash != PrevStateHash[i])
            {
                w.StateChanges[i]++;
                PrevStateHash[i] = hash;
                PrevNormTime[i] = nt;
                continue;
            }

            float d = nt - PrevNormTime[i];
            PrevNormTime[i] = nt;
            if (d < 0f)
            {
                // Unity does NOT wrap normalizedTime on a looping state (it counts 0,1,2,3…), so a
                // backwards step is a real restart — Animator.Play, a re-entry, or a rewind — and
                // its delta is not a rate sample.
                w.Restarts[i]++;
                continue;
            }
            if (d == 0f)
                w.ZeroFrames[i]++;
            w.NormAdvance[i] += d;
            w.NormSeconds[i] += dt;
        }
    }

    private static void SampleSmb(Window w, float dt)
    {
        for (int i = 0; i < _smb.Length && i < ReportCap; i++)
        {
            SpawnObjectAnimateMaterial_SMB s = _smb[i];
            if (s == null)
            {
                PrevSmbValid[i] = false;
                continue;
            }
            float t = s.t;
            if (!PrevSmbValid[i])
            {
                PrevSmbValid[i] = true;
                PrevSmbT[i] = t;
                continue;
            }
            float d = t - PrevSmbT[i];
            PrevSmbT[i] = t;
            if (d < 0f)
                continue;               // OnStateExit resets t to 0 — that is an exit, not a rate
            if (d > 0f)
                w.SmbLiveFrames[i]++;
            w.SmbAdvance[i] += d;
            w.SmbSeconds[i] += dt;
        }
    }

    private static void SampleTimelines(Window w)
    {
        for (int i = 0; i < _timelines.Length && i < ReportCap; i++)
        {
            Chronos.Timeline t = _timelines[i];
            if (t == null)
                continue;
            float ts = t.timeScale;
            if (ts < w.TimelineMin[i]) w.TimelineMin[i] = ts;
            if (ts > w.TimelineMax[i]) w.TimelineMax[i] = ts;
        }
    }

    private static void CensusParticles(GameObject go, Window w)
    {
        ParticleScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, ParticleScratch);
        for (int i = 0; i < ParticleScratch.Count; i++)
        {
            ParticleSystem p = ParticleScratch[i];
            if (p == null)
                continue;
            w.Particles++;
            ParticleSystem.MainModule main = p.main;
            if (main.scalingMode == ParticleSystemScalingMode.Local)
                w.ParticleScalingLocal++;
            if (main.simulationSpace == ParticleSystemSimulationSpace.World)
                w.ParticleSimWorld++;
            if (p.isPlaying)
                w.ParticlesPlaying++;
        }
    }

    /// <summary>
    /// ONE scene sweep, ONCE per session, on the first arm — and it is named as one.
    ///
    /// <para>This project's standing suspect for a per-frame cost is exactly this call, so it is
    /// worth stating what buys the exception: <c>Chronos.AreaClock</c> is the ONLY position-dependent
    /// term in the game's time framework, no C# anywhere references it, and both it and
    /// <c>Chronos.Timeline</c> are <c>[AddComponentMenu]</c> components that could still be attached
    /// in scene or prefab data that no decompile can see. A count of 0 kills that candidate for
    /// every future round in one number; a count above 0 promotes it to first place. Nothing else
    /// available can answer it, and it runs at most once per session.</para>
    /// </summary>
    private static void CensusScene()
    {
        if (_sceneCensusDone)
            return;
        _sceneCensusDone = true;
        try
        {
            // AreaClock itself is generic (AreaClock<TCollider, TVector>) and cannot be a Type
            // argument here; the two CONCRETE subclasses are the only things a scene can hold, and
            // between them they cover every area clock Chronos can instantiate.
            _sceneAreaClocks = Resources.FindObjectsOfTypeAll(typeof(Chronos.AreaClock3D)).Length
                               + Resources.FindObjectsOfTypeAll(typeof(Chronos.AreaClock2D)).Length;
            _sceneTimelines = Resources.FindObjectsOfTypeAll(typeof(Chronos.Timeline)).Length;
        }
        catch
        {
            _sceneAreaClocks = -1;
            _sceneTimelines = -1;
        }
    }

    // ---- the verdict ----------------------------------------------------------------------------

    /// <summary>
    /// END THE WATCH: disarm first, print second, and NOTHING is written by the printer.
    ///
    /// <para><b>THE SPLIT IS DELIBERATE AND IT IS THE PROJECT'S OWN SCAR.</b> Deleting a spent
    /// <c>Log*</c> method once nearly latched the wall fade off forever, because the method's body
    /// carried a load-bearing write — and <c>scripts/check-instrument-writes.py</c> exists to
    /// notice that shape. A verdict printer that also ran the state machine would be exactly it:
    /// gate the print and the watch never disarms, so the per-frame walk runs for the rest of the
    /// session after the last line it could ever emit. Here the transition happens unconditionally
    /// and first; <see cref="ReportVerdict"/> is a pure function of the two windows.</para>
    /// </summary>
    private static void Finish(string reason)
    {
        _phase = Phase.Idle;
        _target = null;
        if (_left <= 0 || (Hand.Frames == 0 && Home.Frames == 0))
            return;
        _left--;
        ReportVerdict(reason);
    }

    /// <summary>The one line this whole class exists to print — and it prints, full stop. Named
    /// <c>Report*</c> so <c>scripts/check-instrument-writes.py</c> seeds it into the instrument set
    /// (it iterates that set to a fixpoint from the name prefixes), which is what lets the scene
    /// census and the two window accumulators be recognised as diagnostic-only state rather than as
    /// something that can never be switched off.</summary>
    private static void ReportVerdict(string reason)
    {
        var sb = new System.Text.StringBuilder(2048);
        sb.Append("[Props] HELD-PROP ANIMATION A/B for ").Append(_label)
          .Append(" (").Append(reason).Append("): HAND ").Append(Hand.Frames)
          .Append(" frame(s)/").Append(Hand.RealSeconds.ToString("0.00")).Append("s vs HOME ")
          .Append(Home.Frames).Append(" frame(s)/").Append(Home.RealSeconds.ToString("0.00"))
          .Append("s. ");

        if (Home.Frames == 0)
            sb.Append("NO HOME WINDOW WAS SAMPLED, so every figure below is one-sided — read it as "
                      + "a census, not as a comparison. ");

        int animators = Mathf.Min(_animators.Length, ReportCap);
        sb.Append("ANIMATOR(S): ").Append(animators == 0 ? "none" : string.Empty);
        for (int i = 0; i < animators; i++)
        {
            Animator a = _animators[i];
            sb.Append(i > 0 ? " | " : string.Empty)
              .Append('\'').Append(a != null ? a.name : "<destroyed>").Append("' ")
              .Append("clipRate hand=").Append(Rate(Hand.NormAdvance[i], Hand.NormSeconds[i]))
              .Append("/s home=").Append(Rate(Home.NormAdvance[i], Home.NormSeconds[i]))
              .Append("/s; speed hand=").Append(Range(Hand.SpeedMin[i], Hand.SpeedMax[i]))
              .Append(" home=").Append(Range(Home.SpeedMin[i], Home.SpeedMax[i]))
              .Append("; cullingMode hand=").Append(Modes<AnimatorCullingMode>(Hand.CullModes[i]))
              .Append(" home=").Append(Modes<AnimatorCullingMode>(Home.CullModes[i]))
              .Append("; updateMode hand=").Append(Modes<AnimatorUpdateMode>(Hand.UpdateModes[i]))
              .Append(" home=").Append(Modes<AnimatorUpdateMode>(Home.UpdateModes[i]))
              .Append("; evaluating hand=").Append(Hand.EnabledFrames[i]).Append('/').Append(Hand.Frames)
              .Append(" home=").Append(Home.EnabledFrames[i]).Append('/').Append(Home.Frames)
              .Append("; stalled(no advance) hand=").Append(Hand.ZeroFrames[i])
              .Append(" home=").Append(Home.ZeroFrames[i])
              .Append("; stateChanges hand=").Append(Hand.StateChanges[i])
              .Append(" home=").Append(Home.StateChanges[i])
              .Append("; restarts hand=").Append(Hand.Restarts[i])
              .Append(" home=").Append(Home.Restarts[i]);
        }

        sb.Append(". MATERIAL FLASH TERM (SpawnObjectAnimateMaterial_SMB, the state behaviour that "
                  + "drives a named shader float from an AnimationCurve): ");
        int smbs = Mathf.Min(_smb.Length, ReportCap);
        if (smbs == 0)
        {
            sb.Append("NONE on this animator's controller — so this flash is not that SMB, and the "
                      + "clip rate above is the whole story");
        }
        for (int i = 0; i < smbs; i++)
        {
            SpawnObjectAnimateMaterial_SMB s = _smb[i];
            sb.Append(i > 0 ? " | " : string.Empty)
              .Append("animProperty='").Append(s != null ? (s.animProperty ?? "<null>") : "<destroyed>")
              .Append("' animTime=").Append(s != null ? s.animTime.ToString("0.###") : "?")
              .Append(" animStrength=").Append(s != null ? s.animStrength.ToString("0.###") : "?")
              .Append(" particles=").Append(s != null && s.particles != null ? s.particles.name : "<none>")
              .Append("; curveRate hand=").Append(Rate(Hand.SmbAdvance[i], Hand.SmbSeconds[i]))
              .Append("/s home=").Append(Rate(Home.SmbAdvance[i], Home.SmbSeconds[i]))
              .Append("/s; advancing hand=").Append(Hand.SmbLiveFrames[i]).Append('/').Append(Hand.Frames)
              .Append(" home=").Append(Home.SmbLiveFrames[i]).Append('/').Append(Home.Frames);
        }

        sb.Append(". CHRONOS: scene holds ").Append(Count(_sceneAreaClocks)).Append(" AreaClock(s) and ")
          .Append(Count(_sceneTimelines)).Append(" Timeline(s); this prop carries ")
          .Append(Mathf.Min(_timelines.Length, ReportCap)).Append(" Timeline(s)");
        for (int i = 0; i < _timelines.Length && i < ReportCap; i++)
        {
            sb.Append(" [timeScale hand=").Append(Range(Hand.TimelineMin[i], Hand.TimelineMax[i]))
              .Append(" home=").Append(Range(Home.TimelineMin[i], Home.TimelineMax[i])).Append(']');
        }
        sb.Append("; globalClock.timeScale hand=").Append(Range(Hand.ClockMin, Hand.ClockMax))
          .Append(" home=").Append(Range(Home.ClockMin, Home.ClockMax))
          .Append("; localTimeScale hand=").Append(Range(Hand.LocalClockMin, Hand.LocalClockMax))
          .Append(" home=").Append(Range(Home.LocalClockMin, Home.LocalClockMax))
          .Append("; paused frames hand=").Append(Hand.ClockPausedFrames)
          .Append(" home=").Append(Home.ClockPausedFrames)
          .Append("; clock unreachable on hand=").Append(Hand.ClockMissingFrames)
          .Append(" home=").Append(Home.ClockMissingFrames)
          .Append("; Time.timeScale hand=").Append(Range(Hand.UnityScaleMin, Hand.UnityScaleMax))
          .Append(" home=").Append(Range(Home.UnityScaleMin, Home.UnityScaleMax));

        sb.Append(". GEOMETRY AND PARTICLES: lossyScale.x hand=")
          .Append(Range(Hand.LossyMin, Hand.LossyMax)).Append(" home=")
          .Append(Range(Home.LossyMin, Home.LossyMax))
          .Append("; particle system(s) hand=").Append(Hand.Particles).Append(" (")
          .Append(Hand.ParticleScalingLocal).Append(" scalingMode=Local, ")
          .Append(Hand.ParticleSimWorld).Append(" simulationSpace=World, ")
          .Append(Hand.ParticlesPlaying).Append(" playing) home=").Append(Home.Particles).Append(" (")
          .Append(Home.ParticleScalingLocal).Append(" Local, ").Append(Home.ParticleSimWorld)
          .Append(" World, ").Append(Home.ParticlesPlaying).Append(" playing)")
          .Append("; poseStomps=").Append(Hand.PoseStomps).Append(" of ").Append(Hand.Frames)
          .Append(" hand frame(s)");

        sb.Append(". READ IT LIKE THIS, IN THIS ORDER. (1) clipRate is the ground truth — "
            + "normalizedTime advanced per REAL second, so a clip that plays at half speed reads "
            + "half the home value and one that plays normally reads the same number in both "
            + "windows. If clipRate is EQUAL, the Animator is not slow and every candidate below "
            + "that acts through the animator is dead. (2) If clipRate fell, read speed next: "
            + "SpawnObjectAnimateMaterial_SMB.OnStateEnter and DelayedDeactivatePropAnimSMB"
            + ".OnStateEnter both latch animator.speed = Timekeeper.instance.m_GlobalClock.timeScale "
            + "ONCE and never refresh it, and TimeManager.SlowMoTimeScale is 0.25 — so a flash state "
            + "ENTERED during a slow-mo window stays 4x slow for the whole state with no position "
            + "involved. globalClock.timeScale differing between the windows confirms that; equal "
            + "clocks with unequal animator.speed means something else writes speed. (3) evaluating "
            + "below the frame count, or stalled climbing, means the animator is being SKIPPED on "
            + "some frames rather than slowed — read cullingMode (CullCompletely stops an animator "
            + "whose renderers no camera sees) and updateMode (AnimatePhysics advances on the 50 Hz "
            + "fixed step, which at 90 Hz looks choppy and is the 'nicht so flüssig' half without "
            + "being the 'langsamer' half). (4) curveRate is the FLASH ITSELF when the SMB exists: "
            + "it is d(t)/dt for t += m_GlobalClock.deltaTime/animTime, accumulated in OnStateUpdate, "
            + "so it falls if and only if the animator is being updated less often or the global "
            + "clock is slower — a curveRate that fell while clipRate held means OnStateUpdate is "
            + "firing on fewer frames. (5) CHRONOS: AreaClock is the ONLY position-dependent term in "
            + "the game's time framework (AreaClock.cs:154-176 evaluates a curve over the Timeline's "
            + "WORLD POSITION), and moving a chest into a palm is the largest position change it "
            + "could get — but no game code references those types at all, so a scene count of 0 "
            + "AreaClock(s) KILLS that candidate permanently and a count above 0 promotes it to "
            + "first place. (6) poseStomps above ~0 means something rewrote the held prop's local "
            + "TRS between our per-frame re-asserts — i.e. the flash animates the TRANSFORM and "
            + "GrabbableProp.ApplyHeldPose is erasing it every frame; that is a different defect "
            + "from a slow clock and needs the pose write to concede the animated channel. "
            + "(7) EVERYTHING EQUAL IN BOTH WINDOWS is itself the answer: it rules out the clock, "
            + "the animator, Chronos and the pose write together, and moves the question to the one "
            + "term this instrument cannot see — the SHADER's own value of animProperty, which the "
            + "SMB writes through Renderer.material (an INSTANCED material) on every renderer it "
            + "finds with GetComponentsInChildren each frame. The next measurement is then that "
            + "float, read back off the prop's materials by the name this line just printed.")
          .Append(" (").Append(_left).Append(" more animation A/B lines this session.)");

        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    private static void Reset(Window w)
    {
        w.Frames = 0;
        w.RealSeconds = 0f;
        for (int i = 0; i < ReportCap; i++)
        {
            w.SpeedMin[i] = float.MaxValue;
            w.SpeedMax[i] = float.MinValue;
            w.CullModes[i] = 0;
            w.UpdateModes[i] = 0;
            w.EnabledFrames[i] = 0;
            w.NormAdvance[i] = 0f;
            w.NormSeconds[i] = 0f;
            w.ZeroFrames[i] = 0;
            w.StateChanges[i] = 0;
            w.Restarts[i] = 0;
            w.SmbAdvance[i] = 0f;
            w.SmbSeconds[i] = 0f;
            w.SmbLiveFrames[i] = 0;
            w.TimelineMin[i] = float.MaxValue;
            w.TimelineMax[i] = float.MinValue;
        }
        w.ClockMin = float.MaxValue;
        w.ClockMax = float.MinValue;
        w.LocalClockMin = float.MaxValue;
        w.LocalClockMax = float.MinValue;
        w.UnityScaleMin = float.MaxValue;
        w.UnityScaleMax = float.MinValue;
        w.ClockPausedFrames = 0;
        w.ClockMissingFrames = 0;
        w.LossyMin = float.MaxValue;
        w.LossyMax = float.MinValue;
        w.CensusTaken = false;
        w.Particles = 0;
        w.ParticleScalingLocal = 0;
        w.ParticleSimWorld = 0;
        w.ParticlesPlaying = 0;
        w.PoseStomps = 0;
    }

    // ---- formatting -----------------------------------------------------------------------------

    /// <summary>A rate, or "n/a" when the window never collected a usable pair of samples — which
    /// is a real reading (the animator was never evaluating) and must not print as 0.000.</summary>
    private static string Rate(float advance, float seconds)
        => seconds > 1e-4f ? (advance / seconds).ToString("0.000") : "n/a";

    private static string Range(float min, float max)
    {
        if (min > max)
            return "n/a";
        return Mathf.Abs(max - min) < 1e-5f
            ? min.ToString("0.###")
            : $"{min.ToString("0.###")}..{max.ToString("0.###")}";
    }

    private static string Count(int n) => n < 0 ? "?" : n.ToString();

    /// <summary>Decode a bitmask of enum values back into the names, so the line says
    /// "CullCompletely" instead of a number nobody can read on a phone.</summary>
    private static string Modes<T>(int mask) where T : System.Enum
    {
        if (mask == 0)
            return "<none seen>";
        string s = string.Empty;
        System.Array values = System.Enum.GetValues(typeof(T));
        for (int i = 0; i < values.Length; i++)
        {
            object v = values.GetValue(i);
            int iv = System.Convert.ToInt32(v);
            if (iv < 0 || iv >= 31 || (mask & (1 << iv)) == 0)
                continue;
            s = s.Length == 0 ? v.ToString() : s + "+" + v;
        }
        return s.Length == 0 ? "<none seen>" : s;
    }
}
