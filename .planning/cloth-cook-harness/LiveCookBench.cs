// WHAT A COOK ACTUALLY COSTS AT THE SIZES HIS FIGURES ACTUALLY ARE, and whether the ModBuild 290
// stretchingStiffness remedy survives a cloth configured like the GAME's rather than like a clean
// procedural sheet.
//
// Run with --bench=live.
//
// WHY THIS EXISTS. ModBuild 290 shipped on the strength of a sentence — "a cook is 25-165 ms on his
// rig, so a mid-gesture re-cook cannot fit in 11.11 ms" — and the counter that shipped WITH it
// says the opposite. His ModBuild 290 log reads:
//
//     FigureGrab.ClothCooks     total 17
//     FigureGrab.ClothCookVerts total 1754, worst frame 143
//
// 103 particles per cook, and the largest cape cooked in the whole session was 143. In the same
// 30 s window FigureGrab.Cloth.Cook does not appear on the [Perf] STEPS line OR on the STEPS TAIL
// line, whose floor is 1.0 ms/s — so seventeen cooks cost less than 30 ms BETWEEN THEM. That is a
// hard upper bound of 1.76 ms per cook on his hardware, not 32-53 ms.
//
// So this bench asks the two questions that decides what ships next:
//
//   PART 1  Where does the cook cost curve actually sit at 81 / 144 / 441 / 784 / 1444 / 3364
//           particles? The previous round only measured 441 and up and fitted a slope through it;
//           if there is a large CONSTANT term the small end is not where the extrapolation puts
//           it, and his capes live entirely at the small end.
//
//   PART 2  The ModBuild 290 remedy was measured on ONE cloth configuration: a 29x29 grid, uniform
//           linear slack, stretchingStiffness at Unity's authored default of 1, no colliders, no
//           self-collision, no tethers touched. A Gloomhaven cape is an authored asset. If the
//           artist shipped it at stretchingStiffness 0.2, `authored x weight` is a much smaller
//           lever; if most of its coefficients are float.MaxValue the pin itself behaves
//           differently. Every cell here re-runs the SHRINK arms PINNED and PINNED+NOSTRETCH on a
//           differently-configured cloth, so "the write is inert on the real configuration" is a
//           row in a table rather than a worry.
//
// Every timed body is measured on its own frame, N reps, MEDIAN, with a NULL control (a body that
// writes nothing) and a POSITIVE control (AddComponent<Cloth>, a known cook) in the SAME run.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class LiveCookBench : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        bool want = false;
        foreach (string a in Environment.GetCommandLineArgs()) if (a == "--bench=live") want = true;
        if (!want) return;
        var go = new GameObject("LiveCookBench");
        DontDestroyOnLoad(go);
        go.AddComponent<LiveCookBench>();
    }

    private const float SlackMax = 0.35f;
    private const float PinFloorFraction = 1e-3f;
    private const int Reps = 9;

    private readonly List<string> _out = new List<string>();
    private void Say(string s) => _out.Add(s);

    private GameObject _root = null!;
    private SkinnedMeshRenderer _smr = null!;
    private Cloth _cloth = null!;
    private ClothSkinningCoefficient[] _pristine = Array.Empty<ClothSkinningCoefficient>();
    private int _grid;

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;
        Say($"Unity {Application.unityVersion} | {Reps} reps, MEDIAN, each timed body on its own frame");
        Say("HIS ModBuild 290 LOG, for reference: ClothCooks 17, ClothCookVerts total 1754 / worst 143");
        Say("  -> mean 103 particles per cook; Cloth.Cook absent from a STEPS TAIL whose floor is");
        Say("     1.0 ms/s over 30.0 s, so all 17 cooks together cost < 30 ms on his rig.");

        yield return Part1CostCurve();
        // BOTH factors, and the 1.345 pass is the load-bearing one. At 2.5 every arm saturates —
        // see FactorArmsBench — so a 2.5-only configuration table cannot tell a configuration that
        // defeats the remedy from a factor that defeats it. 1.345 is the condition under which the
        // ModBuild 290 remedy is KNOWN to work (0.459 -> 0.037), so it is the only condition under
        // which "does this configuration break it?" is a question with a visible answer.
        yield return Part2Configurations(1.345f);
        yield return Part2Configurations(2.5f);

        Debug.Log("[LIV] ==================== RESULTS ====================");
        for (int i = 0; i < _out.Count; i++) Debug.Log("[LIV] " + _out[i]);
        Debug.Log("[LIV] ==================== END ====================");
        Application.Quit(0);
    }

    // =========================================================================================
    // PART 1 — the cost curve, with the small end measured rather than extrapolated into.
    // =========================================================================================

    private IEnumerator Part1CostCurve()
    {
        Say("");
        Say("================================================================================");
        Say("PART 1 — COOK COST vs PARTICLE COUNT (the enable transition that bakes the fabric)");
        Say("================================================================================");
        Say("grid   particles |   NULL   |  ENABLE  |   us/particle  |  ADD<Cloth>  |  COEFF UPLOAD  |  STIFFNESS");

        int[] grids = { 9, 12, 21, 28, 38, 58 };
        foreach (int g in grids)
        {
            yield return BuildSheet(g, 1f, cfg: default);
            for (int i = 0; i < 60; i++) yield return null;
            int n = _cloth.coefficients.Length;

            // NULL: the same loop shape, a body that reads instead of writing. Its median is the
            // instrument's own floor and every other cell must be read against it.
            var nullMs = new List<double>();
            for (int r = 0; r < Reps; r++)
            {
                yield return null;
                var sw = Stopwatch.StartNew();
                bool read = _cloth.enabled;
                sw.Stop();
                if (read) nullMs.Add(sw.Elapsed.TotalMilliseconds);
            }

            // THE COOK. Down on one frame, up on the next — a false/true pair inside a single frame
            // does not cook at all (ModBuild 289 measured 2.6-3.1 ms against 14-21 ms).
            var cookMs = new List<double>();
            for (int r = 0; r < Reps; r++)
            {
                _cloth.enabled = false;
                yield return null;
                var sw = Stopwatch.StartNew();
                _cloth.enabled = true;
                sw.Stop();
                cookMs.Add(sw.Elapsed.TotalMilliseconds);
                _cloth.ClearTransformMotion();
                yield return null;
            }

            // A coefficient upload and a stiffness write, in the SAME run, as the two anchors that
            // say what "cheap" means on this machine today.
            var upMs = new List<double>();
            var stMs = new List<double>();
            var arr = (ClothSkinningCoefficient[])_pristine.Clone();
            for (int r = 0; r < Reps; r++)
            {
                yield return null;
                var sw = Stopwatch.StartNew();
                _cloth.coefficients = arr;
                sw.Stop();
                upMs.Add(sw.Elapsed.TotalMilliseconds);
                yield return null;
                float v = 1f - r * 0.05f;
                sw = Stopwatch.StartNew();
                _cloth.stretchingStiffness = v;
                sw.Stop();
                stMs.Add(sw.Elapsed.TotalMilliseconds);
            }
            _cloth.stretchingStiffness = 1f;

            // POSITIVE: a fresh AddComponent<Cloth> on an identical mesh — a cook that cannot be
            // confused with anything else, so a near-zero ENABLE column can be told apart from a
            // stopwatch that is not running.
            var addMs = new List<double>();
            for (int r = 0; r < 3; r++)
            {
                var go = BuildSheetObject(g, out _);
                go.transform.localScale = Vector3.one;
                yield return null;
                var sw = Stopwatch.StartNew();
                Cloth c = go.AddComponent<Cloth>();
                sw.Stop();
                addMs.Add(sw.Elapsed.TotalMilliseconds);
                if (c == null) Say("    [warn] AddComponent<Cloth> returned null");
                Destroy(go);
                yield return null;
            }

            double cook = Median(cookMs);
            Say(string.Format(CultureInfo.InvariantCulture,
                "{0,2}x{0,-2}  {1,6}    | {2,7:F4} | {3,7:F3} | {4,10:F2}     | {5,9:F3}  | {6,11:F4}   | {7,9:F4}",
                g, n, Median(nullMs), cook, cook * 1000d / Mathf.Max(1, n), Median(addMs),
                Median(upMs), Median(stMs)));
        }

        Say("");
        Say("READ THIS AGAINST THE COUNTER. His session's mean cook was 103 particles and its worst");
        Say("was 143. Whatever the us/particle column says at 81 and 144, THAT is the size of the");
        Say("unit of work a mid-gesture re-cook would have to fit into a frame -- not the 841 the");
        Say("ModBuild 290 arm table was built on, and not the 771 the ModBuild 289 census printed.");
    }

    // =========================================================================================
    // PART 2 — does the remedy survive a cloth configured like an authored game asset?
    // =========================================================================================

    private struct Cfg
    {
        public string Name;
        public float AuthoredStretch;     // what the artist shipped; the remedy's lever is THIS x weight
        public float SolverFrequency;     // the game writes 120 explicitly (PhysicsController.cs:53)
        public bool NoTethers;
        public float VirtualParticles;      // Unity types this as a float weight, not a bool
        public float SelfCollisionDistance;
        public float Damping;
        public float Friction;
        public float WorldVelocityScale;
        public float WorldAccelerationScale;
        public float Bending;
        public bool UnconstrainedProfile; // most vertices authored float.MaxValue rather than a ramp
        public bool Colliders;            // a capsule through the middle, like a body under a cape
    }

    private static Cfg Base() => new Cfg
    {
        Name = "BASE (the ModBuild 290 arm table's sheet)",
        AuthoredStretch = 1f,
        SolverFrequency = 120f,
        VirtualParticles = 1f,
        Damping = 0f,
        Friction = 0.5f,
        WorldVelocityScale = 0.5f,
        WorldAccelerationScale = 0.5f,
        Bending = 1f,
    };

    private IEnumerator Part2Configurations(float from)
    {
        Say("");
        Say("================================================================================");
        Say($"PART 2 — IS THE stretchingStiffness WRITE INERT ON A GAME-LIKE CLOTH?   SHRINK {from:0.###} -> 1");
        Say("================================================================================");
        Say("Each row: the SAME configuration measured PINNED (ModBuild 289) and PINNED+NOSTRETCH");
        Say("(ModBuild 290). If the 290 column does not beat the 289 column on a row, the remedy is");
        Say("inert for a cloth configured that way and that row is the answer to the user's report.");
        Say("REFERENCE POINTS from FactorArmsBench on the BASE configuration, same 29x29 sheet:");
        Say("  1.345 -> 1   POSITIVE 0.027 | PINNED 0.459 | PIN+NOSTRETCH 0.037   (the remedy works)");
        Say("Read every row below against those, not against zero.");
        Say("");
        Say("configuration                                  | 289 pinned | 290 nostretch | ratio | cook ms");

        var cfgs = new List<Cfg>();
        cfgs.Add(Base());

        Cfg c;
        c = Base(); c.Name = "authored stretchingStiffness 0.50"; c.AuthoredStretch = 0.50f; cfgs.Add(c);
        c = Base(); c.Name = "authored stretchingStiffness 0.20"; c.AuthoredStretch = 0.20f; cfgs.Add(c);
        c = Base(); c.Name = "authored stretchingStiffness 0.00 (remedy is a NO-OP by construction)"; c.AuthoredStretch = 0f; cfgs.Add(c);
        c = Base(); c.Name = "useTethers = false"; c.NoTethers = true; cfgs.Add(c);
        c = Base(); c.Name = "useVirtualParticles = 0"; c.VirtualParticles = 0f; cfgs.Add(c);
        c = Base(); c.Name = "selfCollisionDistance 0.02"; c.SelfCollisionDistance = 0.02f; cfgs.Add(c);
        c = Base(); c.Name = "clothSolverFrequency 30 (PlatformSetting.SimplifyPhysics)"; c.SolverFrequency = 30f; cfgs.Add(c);
        c = Base(); c.Name = "clothSolverFrequency 300"; c.SolverFrequency = 300f; cfgs.Add(c);
        c = Base(); c.Name = "damping 0.5, friction 0.1"; c.Damping = 0.5f; c.Friction = 0.1f; cfgs.Add(c);
        c = Base(); c.Name = "world velocity/acceleration scale 0"; c.WorldVelocityScale = 0f; c.WorldAccelerationScale = 0f; cfgs.Add(c);
        c = Base(); c.Name = "bendingStiffness 0.2"; c.Bending = 0.2f; cfgs.Add(c);
        c = Base(); c.Name = "UNCONSTRAINED profile (most vertices float.MaxValue)"; c.UnconstrainedProfile = true; cfgs.Add(c);
        c = Base(); c.Name = "a capsule collider through the cape"; c.Colliders = true; cfgs.Add(c);

        foreach (Cfg cfg in cfgs)
        {
            float pinned = 0f, nostretch = 0f, cookMs = 0f;
            yield return ShrinkArm(cfg, from, nostretch: false, r => pinned = r);
            yield return ShrinkArm(cfg, from, nostretch: true, r => nostretch = r);
            yield return CookCostOf(cfg, r => cookMs = r);
            string ratio = nostretch > 1e-6f ? (pinned / nostretch).ToString("F1", CultureInfo.InvariantCulture) + "x"
                                             : "n/a";
            Say(string.Format(CultureInfo.InvariantCulture,
                "{0,-46} |   {1:F4}   |    {2:F4}     | {3,5} | {4,6:F3}",
                Trim(cfg.Name, 46), pinned, nostretch, ratio, cookMs));
        }

        Say("");
        Say("A ROW WHOSE RATIO IS 1.0x MEANS THE WRITE DID NOTHING FOR THAT CONFIGURATION. The one");
        Say("row that is 1.0x BY CONSTRUCTION is `authored stretchingStiffness 0.00`, because");
        Say("`authored x weight` is 0 either way -- it is in the table as the built-in positive");
        Say("control for 'inert', so any OTHER row landing near it is reading the same thing.");
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "~";

    /// <summary>One SHRINK arm at 2.5 -> 1 on a configured cloth, held pinned for the settle, and
    /// its normal incoherence returned. Identical history in both arms; the ONLY difference is
    /// whether the stretching constraint is taken out of the argument.</summary>
    private IEnumerator ShrinkArm(Cfg cfg, float from, bool nostretch, Action<float> done)
    {
        float A = from, B = 1f;
        yield return BuildSheet(29, A, cfg);
        for (int i = 0; i < 240; i++) yield return null;

        float smoothed = A, weight = 1f;
        for (int f = 0; f < 45; f++)
        {
            smoothed = Mathf.Lerp(smoothed, B, 0.12f);
            _root.transform.localScale = Vector3.one * smoothed;
            weight = Mathf.MoveTowards(weight, 0f, Time.unscaledDeltaTime / 0.12f);
            if (nostretch) _cloth.stretchingStiffness = cfg.AuthoredStretch * weight;
            Upload(weight, smoothed);
            yield return null;
        }
        _root.transform.localScale = Vector3.one * B;
        if (nostretch) _cloth.stretchingStiffness = 0f;
        for (int i = 0; i < 240; i++) { Upload(0f, B); yield return null; }
        done(Incoherence(Snapshot(B)));
    }

    private IEnumerator CookCostOf(Cfg cfg, Action<float> done)
    {
        yield return BuildSheet(29, 1f, cfg);
        for (int i = 0; i < 60; i++) yield return null;
        var ms = new List<double>();
        for (int r = 0; r < 5; r++)
        {
            _cloth.enabled = false;
            yield return null;
            var sw = Stopwatch.StartNew();
            _cloth.enabled = true;
            sw.Stop();
            ms.Add(sw.Elapsed.TotalMilliseconds);
            yield return null;
        }
        done((float)Median(ms));
    }

    // ---- scaffolding -------------------------------------------------------------------------

    private IEnumerator BuildSheet(int grid, float scale, Cfg cfg)
    {
        if (_root != null) Destroy(_root);
        yield return null;
        _grid = grid;
        _root = BuildSheetObject(grid, out _);
        _smr = _root.GetComponent<SkinnedMeshRenderer>();
        _root.transform.localScale = Vector3.one * scale;

        if (cfg.Colliders)
        {
            var body = new GameObject("body");
            body.transform.SetParent(_root.transform, false);
            var cap = body.AddComponent<CapsuleCollider>();
            cap.radius = 0.08f;
            cap.height = 0.8f;
            body.transform.localPosition = new Vector3(0.5f, -0.4f, -0.12f);
        }

        _cloth = _root.AddComponent<Cloth>();
        _cloth.useGravity = true;
        if (cfg.Name != null)
        {
            _cloth.stretchingStiffness = cfg.AuthoredStretch;
            _cloth.bendingStiffness = cfg.Bending;
            _cloth.clothSolverFrequency = cfg.SolverFrequency;
            _cloth.useTethers = !cfg.NoTethers;
            _cloth.useVirtualParticles = cfg.VirtualParticles;
            _cloth.selfCollisionDistance = cfg.SelfCollisionDistance;
            _cloth.damping = cfg.Damping;
            _cloth.friction = cfg.Friction;
            _cloth.worldVelocityScale = cfg.WorldVelocityScale;
            _cloth.worldAccelerationScale = cfg.WorldAccelerationScale;
            if (cfg.Colliders)
            {
                var caps = _root.GetComponentsInChildren<CapsuleCollider>();
                _cloth.capsuleColliders = caps;
            }
        }

        ClothSkinningCoefficient[] co = _cloth.coefficients;
        for (int i = 0; i < co.Length; i++)
        {
            float t = (i / grid) / (float)(grid - 1);     // row 0 is the pinned edge
            if (cfg.UnconstrainedProfile)
            {
                // The artist pinned the collar hard and left everything below it free. That is a
                // real authoring style and the mod's ramp treats float.MaxValue completely
                // differently from a finite metre value, so it has to be a row of its own.
                co[i].maxDistance = t < 0.15f ? 0f : float.MaxValue;
                co[i].collisionSphereDistance = t < 0.15f ? 0f : float.MaxValue;
            }
            else
            {
                co[i].maxDistance = SlackMax * t;
                co[i].collisionSphereDistance = 0.01f * t;
            }
        }
        _cloth.coefficients = co;
        _pristine = (ClothSkinningCoefficient[])co.Clone();
        Upload(1f, scale);
        yield return null;
    }

    /// <summary>FigureCloth.BuildInto, transcribed.</summary>
    private void Upload(float weight, float factor)
    {
        var next = new ClothSkinningCoefficient[_pristine.Length];
        float bound = _smr.sharedMesh.bounds.extents.magnitude;
        float floor = bound * PinFloorFraction;
        for (int v = 0; v < _pristine.Length; v++)
        {
            float m = _pristine[v].maxDistance, s = _pristine[v].collisionSphereDistance;
            if (weight >= 1f)
            {
                next[v].maxDistance = m >= float.MaxValue ? m : m * factor;
                next[v].collisionSphereDistance = s >= float.MaxValue ? s : s * factor;
            }
            else if (weight <= 0f)
            {
                next[v].maxDistance = floor;
                next[v].collisionSphereDistance = floor;
            }
            else
            {
                float k = factor * weight, ramped = Mathf.Max(bound * weight, floor);
                float mv = m >= float.MaxValue ? ramped : m * k;
                float sv = s >= float.MaxValue ? ramped : s * k;
                next[v].maxDistance = mv > floor ? mv : floor;
                next[v].collisionSphereDistance = sv > floor ? sv : floor;
            }
        }
        _cloth.coefficients = next;
    }

    private Mesh _bake = null!;

    private Vector3[] Snapshot(float scale)
    {
        Vector3[] v;
        if (_cloth != null && _cloth.enabled) v = _cloth.vertices;
        else { if (_bake == null) _bake = new Mesh(); _smr.BakeMesh(_bake); v = _bake.vertices; }
        var o = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) o[i] = v[i] / scale;
        return o;
    }

    private float Incoherence(Vector3[] v)
    {
        double sum = 0d; int n = 0;
        for (int z = 0; z < _grid - 1; z++)
        for (int x = 0; x < _grid - 2; x++)
        {
            Vector3 a = QuadNormal(v, x, z), b = QuadNormal(v, x + 1, z);
            if (a == Vector3.zero || b == Vector3.zero) continue;
            sum += 1d - Vector3.Dot(a, b); n++;
        }
        return n == 0 ? 0f : (float)(sum / n);
    }

    private Vector3 QuadNormal(Vector3[] v, int x, int z)
    {
        int i = z * _grid + x;
        if (i + _grid + 1 >= v.Length) return Vector3.zero;
        Vector3 n = Vector3.Cross(v[i + 1] - v[i], v[i + _grid] - v[i]);
        return n.sqrMagnitude > 1e-18f ? n.normalized : Vector3.zero;
    }

    private static double Median(List<double> v)
    {
        if (v.Count == 0) return 0d;
        var c = new List<double>(v);
        c.Sort();
        return c[c.Count / 2];
    }

    private static GameObject BuildSheetObject(int grid, out Vector3[] authored)
    {
        var go = new GameObject("sheet");
        var verts = new Vector3[grid * grid];
        var normals = new Vector3[grid * grid];
        var weights = new BoneWeight[grid * grid];
        for (int z = 0; z < grid; z++)
        for (int x = 0; x < grid; x++)
        {
            int i = z * grid + x;
            verts[i] = new Vector3(x / (float)(grid - 1), -z / (float)(grid - 1), 0f);
            normals[i] = Vector3.back;
            weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        }
        var tris = new List<int>((grid - 1) * (grid - 1) * 6);
        for (int z = 0; z < grid - 1; z++)
        for (int x = 0; x < grid - 1; x++)
        {
            int i = z * grid + x;
            tris.Add(i); tris.Add(i + 1); tris.Add(i + grid);
            tris.Add(i + 1); tris.Add(i + grid + 1); tris.Add(i + grid);
        }
        var mesh = new Mesh { name = "sheet" };
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = tris.ToArray();
        mesh.boneWeights = weights;
        mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.RecalculateBounds();

        var bone = new GameObject("bone");
        bone.transform.SetParent(go.transform, false);
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.bones = new[] { bone.transform };
        smr.rootBone = bone.transform;
        smr.updateWhenOffscreen = true;
        authored = verts;
        return go;
    }
}
