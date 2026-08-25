// BOTH DIRECTIONS OF A RESCALE, and a picture of each.
//
// Run with --bench=dir.
//
// Every arm before this round scaled a cloth UP. The ModBuild 289 user report is about the way
// DOWN ("Beim kleiner machen wird da ein Polygon matsch draus"), and a table that only contains
// growth agrees with every broken build. This bench runs the SAME arms in both directions against
// the same two positive controls, and it renders each settled arm to a PNG, because a distance
// statistic cannot see "Polygon-Matsch".
//
// The two directions are stated as (A -> B): the fabric is COOKED at scale A and the root is then
// driven to scale B.
//   GROW   1.000 -> 1.345   fabric rest lengths TOO SHORT for the body  -> taut, no folds ("steif")
//   SHRINK 1.345 -> 1.000   fabric rest lengths TOO LONG  for the body  -> slack, self-intersecting
//
// Controls, because a new instrument's first output is a hypothesis:
//   NULL      the reference arm run twice -> the instrument floor
//   POSITIVE  a cloth BORN at the target scale (fabric cooked there, coefficients authored x it)
//
// Metrics. Three of them are shape statistics and the fourth is a picture:
//   mean/worst edge      solved edge length over authored edge length, in the normalised frame
//   crushed              fraction of edges under 0.6x authored -- BUNCHING, which is what mush is
//   incoherence          mean (1 - dot) over adjacent triangle normal pairs; a smooth drape is ~0
//   close pairs          non-neighbour vertex pairs closer than 0.35x the authored edge -- the
//                        direct count of "polygons sitting inside each other"
//   PNG                  a software wireframe rasterisation, no shader dependency at all

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class DirectionArmsBench : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        bool want = false;
        foreach (string a in Environment.GetCommandLineArgs()) if (a == "--bench=dir") want = true;
        if (!want) return;
        var go = new GameObject("DirectionArmsBench");
        DontDestroyOnLoad(go);
        go.AddComponent<DirectionArmsBench>();
    }

    private const int Grid = 29;              // 841 cloth vertices -- his widest cape is 771
    private const float SlackMax = 0.35f;     // painted maxDistance at the free edge, metres
    private const int SettleFrames = 300;
    private const float Big = 1.345f;         // the factor his ModBuild 289 log settled at
    private const float PinFloorFraction = 1e-3f;

    private readonly List<string> _out = new List<string>();
    private void Say(string s) => _out.Add(s);

    private GameObject _root = null!;
    private SkinnedMeshRenderer _smr = null!;
    private Cloth _cloth = null!;
    private ClothSkinningCoefficient[] _pristine = Array.Empty<ClothSkinningCoefficient>();
    private Vector3[] _authored = Array.Empty<Vector3>();
    private Mesh _bake = null!;
    private string _note = "";
    private string _reaction = "";

    private enum Arm
    {
        Born,            // POSITIVE: born at the target scale. Also the NULL when run twice.
        BornAgain,
        Stale,           // fabric cooked at A, coefficients rescaled to B, simulating. NO pin, NO cook.
        Pinned,          // the pin: maxDistance driven to the floor. What is on screen MID-gesture.
        Cooked,          // the shipped settle: upload full coefficients, down, up. Fabric re-cooked at B.
        NoStretch100,    // NEW ARM: stretchingStiffness = 0, coefficients x B x 1.00
        NoStretch050,    // NEW ARM: stretchingStiffness = 0, coefficients x B x 0.50
        NoStretch025,    // NEW ARM: stretchingStiffness = 0, coefficients x B x 0.25
        NoStretch010,    // NEW ARM: stretchingStiffness = 0, coefficients x B x 0.10

        // THE PIN IS NOT INNOCENT ON THE WAY DOWN. maxDistance is a SOFT constraint that competes
        // with the fabric's stretching constraint. Cook a fabric at 1.345 and drive the body to 1
        // and the rest lengths are a third too long for the surface the pin is trying to flatten
        // them onto: the surplus length has to go somewhere and it buckles. These arms take the
        // fabric out of that argument while the pin is held.
        PinnedNoStretch, // pin floor AND stretchingStiffness = 0
        PinnedNoBend,    // pin floor AND bendingStiffness = 0
        PinnedNoTether,  // pin floor AND useTethers = false

        // "inklusive der Reaktion": a UNIFORM absolute wander bound in metres, scale-corrected, with
        // the fabric switched out of the argument. Every vertex may travel this far from its skinned
        // position and no further, so there is motion without any dependence on a rest length.
        React025,        // stretchingStiffness = 0, maxDistance = 0.25 x authored edge x B, uniform
        React050,        // ... 0.50 x authored edge
        React100,        // ... 1.00 x authored edge
    }

    private static readonly Arm[] Arms =
    {
        Arm.Born, Arm.BornAgain, Arm.Stale, Arm.Pinned, Arm.Cooked,
        Arm.NoStretch100, Arm.NoStretch050, Arm.NoStretch025, Arm.NoStretch010,
        Arm.PinnedNoStretch, Arm.PinnedNoBend, Arm.PinnedNoTether,
        Arm.React025, Arm.React050, Arm.React100,
    };

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;
        Say($"Unity {Application.unityVersion} | grid {Grid}x{Grid} ({Grid * Grid} verts) | painted "
            + $"maxDistance 0 at the pinned edge -> {SlackMax} m at the free edge | {SettleFrames} settle "
            + "frames | ALL positions divided by the root scale before comparison");

        // (A -> B): fabric cooked at A, root driven to B.
        var dirs = new[]
        {
            new { Name = "GROW  ", A = 1f,  B = Big },
            new { Name = "SHRINK", A = Big, B = 1f  },
        };

        foreach (var d in dirs)
        {
            Say("");
            Say("================================================================================");
            Say($"{d.Name}   fabric cooked at {d.A:0.###}  ->  root driven to {d.B:0.###}");
            Say("================================================================================");

            Vector3[] gold = Array.Empty<Vector3>();
            foreach (Arm arm in Arms)
            {
                Vector3[] shape = null;
                _note = "";
                _reaction = "";
                yield return RunArm(arm, d.A, d.B, r => shape = r);
                if (arm == Arm.Born) gold = shape;
                Report(d.Name.Trim(), arm, shape, gold, d.A, d.B);
                Render($"{d.Name.Trim().ToLowerInvariant()}-{arm}", shape);
            }
        }

        Debug.Log("[DIR] ==================== RESULTS ====================");
        for (int i = 0; i < _out.Count; i++) Debug.Log("[DIR] " + _out[i]);
        Debug.Log("[DIR] ==================== END ====================");
        Application.Quit(0);
    }

    private static string Title(Arm arm, float a, float b) => arm switch
    {
        Arm.Born         => $"POSITIVE  cloth BORN at {b:0.###} (fabric cooked at {b:0.###}, coefficients authored x {b:0.###})",
        Arm.BornAgain    => "NULL      the POSITIVE arm run a second time (instrument floor)",
        Arm.Stale        => $"STALE     fabric still cooked at {a:0.###}, coefficients x {b:0.###}, SIMULATING (no pin, no cook)",
        Arm.Pinned       => "PINNED    maxDistance driven to the pin floor -- what is on screen MID-gesture",
        Arm.Cooked       => $"COOKED    the shipped settle: coefficients x {b:0.###}, enabled=false, enabled=true",
        Arm.NoStretch100 => $"NEW k1.00 stretchingStiffness = 0, coefficients x {b:0.###} x 1.00",
        Arm.NoStretch050 => $"NEW k0.50 stretchingStiffness = 0, coefficients x {b:0.###} x 0.50",
        Arm.NoStretch025 => $"NEW k0.25 stretchingStiffness = 0, coefficients x {b:0.###} x 0.25",
        Arm.NoStretch010 => $"NEW k0.10 stretchingStiffness = 0, coefficients x {b:0.###} x 0.10",
        Arm.PinnedNoStretch => "PIN+NOSTR pin floor held AND stretchingStiffness = 0",
        Arm.PinnedNoBend    => "PIN+NOBND pin floor held AND bendingStiffness = 0",
        Arm.PinnedNoTether  => "PIN+NOTTH pin floor held AND useTethers = false",
        Arm.React025 => $"REACT .25 stretchingStiffness = 0, UNIFORM maxDistance = 0.25 x edge x {b:0.###}",
        Arm.React050 => $"REACT .50 stretchingStiffness = 0, UNIFORM maxDistance = 0.50 x edge x {b:0.###}",
        _            => $"REACT 1.0 stretchingStiffness = 0, UNIFORM maxDistance = 1.00 x edge x {b:0.###}",
    };

    private void Report(string dir, Arm arm, Vector3[] shape, Vector3[] gold, float a, float b)
    {
        Say("");
        Say(Title(arm, a, b));
        if (_note.Length > 0) Say("    " + _note);
        Say($"    sag (normalised lowest y) {MinY(shape):F5}   edge/authored: mean {EdgeRatio(shape, false):F4}"
            + $"  worst {EdgeRatio(shape, true):F4}");
        Say($"    crushed edges (<0.6x authored) {Crushed(shape) * 100f:F1}%   normal incoherence "
            + $"{Incoherence(shape):F4}   close non-neighbour pairs {ClosePairs(shape)}");
        if (gold.Length > 0 && arm != Arm.Born)
            Say($"    vs POSITIVE: worst per-vertex {Dev(shape, gold):F5} m   mean {DevMean(shape, gold):F5} m");
        if (_reaction.Length > 0) Say("    " + _reaction);
    }

    /// <summary>
    /// Build a cloth, cook its fabric at scale <paramref name="a"/>, let it settle there, then drive
    /// the root to scale <paramref name="b"/> and apply the arm. Every arm sees the same history, so
    /// the only thing that differs between two rows is the arm.
    /// </summary>
    private IEnumerator RunArm(Arm arm, float a, float b, Action<Vector3[]> done)
    {
        if (_root != null) Destroy(_root);
        yield return null;
        _root = BuildClothObject(Grid, out _authored);
        _smr = _root.GetComponent<SkinnedMeshRenderer>();

        // The POSITIVE (and the NULL, which is the POSITIVE again) is born at the TARGET, so it
        // never sees scale a at all -- that is what makes it the definition of "correct".
        float birth = arm == Arm.Born || arm == Arm.BornAgain ? b : a;
        _root.transform.localScale = Vector3.one * birth;

        _cloth = _root.AddComponent<Cloth>();
        _cloth.useGravity = true;
        ClothSkinningCoefficient[] c = _cloth.coefficients;
        for (int i = 0; i < c.Length; i++)
        {
            float t = (i / Grid) / (float)(Grid - 1);     // row 0 is the pinned edge
            c[i].maxDistance = SlackMax * t;
            c[i].collisionSphereDistance = 0.01f * t;
        }
        _pristine = (ClothSkinningCoefficient[])c.Clone();
        Upload(1f, birth, 1f);
        yield return null;

        for (int i = 0; i < SettleFrames; i++) yield return null;

        if (arm == Arm.Born || arm == Arm.BornAgain)
        {
            _note = $"[state] cloth vertices {_cloth.coefficients.Length}, mesh vertices {_smr.sharedMesh.vertexCount}";
            Vector3[] bornShape = Snapshot(b);
            yield return Reaction(b);
            done(bornShape);
            yield break;
        }

        // ---- the gesture: a -> b over 45 frames, exponential, FigureStretch's shape -----------
        // Every arm is PINNED across the moving window, because that is what ships. The arms differ
        // only in what happens at the END of it.
        float smoothed = a, weight = 1f;
        for (int f = 0; f < 45; f++)
        {
            smoothed = Mathf.Lerp(smoothed, b, 0.12f);
            _root.transform.localScale = Vector3.one * smoothed;
            weight = Mathf.MoveTowards(weight, 0f, Time.unscaledDeltaTime / 0.12f);
            Upload(weight, smoothed, 1f);
            yield return null;
        }
        _root.transform.localScale = Vector3.one * b;

        bool holdPin = arm == Arm.Pinned || arm == Arm.PinnedNoStretch
                    || arm == Arm.PinnedNoBend || arm == Arm.PinnedNoTether;
        if (holdPin)
        {
            // Hold the pin. This is the state the player looks at for the whole moving window --
            // and on the way DOWN it is NOT a rigid skinned mesh, which is the finding of this
            // round. The three variants take one fabric constraint at a time out of the argument.
            if (arm == Arm.PinnedNoStretch) _cloth.stretchingStiffness = 0f;
            if (arm == Arm.PinnedNoBend) _cloth.bendingStiffness = 0f;
            if (arm == Arm.PinnedNoTether) _cloth.useTethers = false;
            for (int i = 0; i < SettleFrames; i++) { Upload(0f, b, 1f); yield return null; }
            _note = "[state] the pin is HELD for the whole settle";
            Vector3[] pinShape = Snapshot(b);
            yield return Reaction(b);
            done(pinShape);
            yield break;
        }

        if (arm == Arm.React025 || arm == Arm.React050 || arm == Arm.React100)
        {
            float rk = arm == Arm.React025 ? 0.25f : arm == Arm.React050 ? 0.50f : 1.00f;
            float edge = (_authored[1] - _authored[0]).magnitude;
            _cloth.stretchingStiffness = 0f;
            UploadUniform(edge * rk * b);
            _note = $"[state] stretchingStiffness = 0, UNIFORM maxDistance {edge * rk * b:F4} m "
                  + $"({rk:0.00} x the authored edge {edge:F4} m), fabric NOT re-cooked";
            for (int i = 0; i < SettleFrames; i++) yield return null;
            _note += $" | cloth vertices {_cloth.coefficients.Length}";
            Vector3[] reactShape = Snapshot(b);
            yield return Reaction(b);
            done(reactShape);
            yield break;
        }

        if (arm == Arm.Cooked)
        {
            // FigureCloth.StepCook, verbatim: frame A uploads the FULL coefficients and takes the
            // component down; frame B brings it back up, and that enable is the cook.
            Upload(1f, b, 1f);
            _cloth.enabled = false;
            yield return null;
            _cloth.enabled = true;
            _cloth.ClearTransformMotion();
            _note = "[state] fabric re-cooked at the target scale";
        }
        else if (arm == Arm.Stale)
        {
            // No cook at all: release the pin to the full rescaled coefficients and simulate against
            // the fabric that is still sized for scale a. This is the ModBuild 285 shipped state,
            // and -- the point of this round -- it is ALSO the state the shipped ModBuild 289 code
            // leaves the cape in for every frame between one cook and the next re-pin.
            Upload(1f, b, 1f);
            _note = "[state] fabric NOT re-cooked; coefficients rescaled";
        }
        else
        {
            float k = arm switch
            {
                Arm.NoStretch100 => 1.00f,
                Arm.NoStretch050 => 0.50f,
                Arm.NoStretch025 => 0.25f,
                _                => 0.10f,
            };
            // THE UNTESTED COMBINATION. stretchingStiffness = 0 was measured ALONE in ModBuild 286
            // and tore open (mean edge 1.308, worst 5.3x). It has never been measured together with
            // a maxDistance that is both RESCALED to the new size and SMALL. With the fabric no
            // longer enforcing stale rest lengths the only thing holding a vertex is its distance
            // from the skinned pose -- which is in world metres and IS scale-correctable.
            _cloth.stretchingStiffness = 0f;
            Upload(1f, b, k);
            _note = $"[state] stretchingStiffness = 0, slack multiplier {k:0.00}, fabric NOT re-cooked";
        }

        for (int i = 0; i < SettleFrames; i++) yield return null;
        _note += $" | cloth vertices {_cloth.coefficients.Length}, enabled at snapshot {_cloth.enabled}";
        Vector3[] shape = Snapshot(b);
        yield return Reaction(b);
        done(shape);
    }

    /// <summary>
    /// "inklusive der Reaktion" — DOES THE CAPE STILL MOVE? Swing the root sideways and record how
    /// far the cloth's own local shape departs from where it was at rest. A rigid follower's local
    /// coordinates do not change under a translation at all, so a pinned cloth reads ~0 here no
    /// matter how large the swing; anything that lags behind the body reads its lag in metres.
    /// A shape statistic cannot answer this question — every arm can be beautifully draped and
    /// completely dead — so it is measured separately and reported in its own column.
    /// </summary>
    private IEnumerator Reaction(float scale)
    {
        Vector3[] rest = Snapshot(scale);
        float peak = 0f;
        for (int f = 0; f < 60; f++)
        {
            _root.transform.position = new Vector3(0.25f * Mathf.Sin(f * 0.35f), 0f, 0f) * scale;
            yield return null;
            float d = Dev(Snapshot(scale), rest);
            if (d > peak) peak = d;
        }
        _root.transform.position = Vector3.zero;
        for (int f = 0; f < 90; f++) yield return null;
        Vector3[] after = Snapshot(scale);
        _reaction = $"reaction under a 0.25 m swing: peak local departure {peak:F5} m; "
                  + $"recovery to the pre-swing shape {Dev(after, rest):F5} m";
    }

    /// <summary>The shipped FigureCloth.BuildInto write, with one extra knob: a slack multiplier the
    /// new arm uses to shrink maxDistance without changing which vertices are constrained.</summary>
    private void Upload(float weight, float factor, float slackK)
    {
        var next = new ClothSkinningCoefficient[_pristine.Length];
        float bound = _smr.sharedMesh.bounds.extents.magnitude;
        float floor = bound * PinFloorFraction;
        for (int v = 0; v < _pristine.Length; v++)
        {
            float m = _pristine[v].maxDistance, s = _pristine[v].collisionSphereDistance;
            if (weight >= 1f)
            {
                float mv = m >= float.MaxValue ? m : m * factor * slackK;
                float sv = s >= float.MaxValue ? s : s * factor * slackK;
                next[v].maxDistance = mv > floor || m >= float.MaxValue ? mv : floor;
                next[v].collisionSphereDistance = sv > floor || s >= float.MaxValue ? sv : floor;
            }
            else if (weight <= 0f)
            {
                next[v].maxDistance = floor;
                next[v].collisionSphereDistance = floor;
            }
            else
            {
                float k = factor * weight * slackK, ramped = bound * weight;
                float mv = m >= float.MaxValue ? ramped : m * k;
                float sv = s >= float.MaxValue ? ramped : s * k;
                next[v].maxDistance = mv > floor ? mv : floor;
                next[v].collisionSphereDistance = sv > floor ? sv : floor;
            }
        }
        _cloth.coefficients = next;
    }

    /// <summary>Every vertex gets the SAME maxDistance, in metres, already multiplied by the target
    /// scale. Nothing here reads an authored rest length, which is the whole point: a bound that is
    /// a distance from the skinned pose is scale-correctable and a rest length is not.</summary>
    private void UploadUniform(float metres)
    {
        var next = new ClothSkinningCoefficient[_pristine.Length];
        for (int v = 0; v < _pristine.Length; v++)
        {
            // The authored profile still decides WHICH vertices are held hard: a vertex the artist
            // pinned to the body (maxDistance 0) stays pinned, so the collar of a cape does not
            // start floating away from the shoulders.
            float m = _pristine[v].maxDistance;
            float scaled = m <= 0f ? 0f : metres;
            next[v].maxDistance = scaled;
            next[v].collisionSphereDistance = scaled;
        }
        _cloth.coefficients = next;
    }

    // ---- metrics ---------------------------------------------------------------------------

    private Vector3[] Snapshot(float scale)
    {
        Vector3[] v;
        if (_cloth != null && _cloth.enabled) v = _cloth.vertices;
        else
        {
            if (_bake == null) _bake = new Mesh();
            _smr.BakeMesh(_bake);
            v = _bake.vertices;
        }
        var outv = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) outv[i] = v[i] / scale;   // BOTH report in the SCALED frame
        return outv;
    }

    private static float MinY(Vector3[] v)
    {
        float m = 0f;
        for (int i = 0; i < v.Length; i++) if (v[i].y < m) m = v[i].y;
        return m;
    }

    private float EdgeRatio(Vector3[] v, bool worst)
    {
        double sum = 0d, mx = 1d; int n = 0;
        for (int z = 0; z < Grid; z++)
        for (int x = 0; x < Grid - 1; x++)
        {
            int i = z * Grid + x;
            if (i + 1 >= v.Length) continue;
            double solved = (v[i + 1] - v[i]).magnitude;
            double authored = (_authored[i + 1] - _authored[i]).magnitude;
            if (authored <= 0d) continue;
            double r = solved / authored;
            sum += r; n++;
            if (Math.Abs(r - 1d) > Math.Abs(mx - 1d)) mx = r;
        }
        return worst ? (float)mx : (float)(sum / Math.Max(1, n));
    }

    /// <summary>Fraction of edges solved to under 0.6x their authored length. A fabric whose rest
    /// lengths are too LONG for the body it is on has nowhere to put the surplus, so it bunches --
    /// and bunching, not sagging, is what "Polygon matsch" looks like.</summary>
    private float Crushed(Vector3[] v)
    {
        int hit = 0, n = 0;
        for (int z = 0; z < Grid; z++)
        for (int x = 0; x < Grid - 1; x++)
        {
            int i = z * Grid + x;
            if (i + 1 >= v.Length) continue;
            double authored = (_authored[i + 1] - _authored[i]).magnitude;
            if (authored <= 0d) continue;
            n++;
            if ((v[i + 1] - v[i]).magnitude < 0.6d * authored) hit++;
        }
        return n == 0 ? 0f : hit / (float)n;
    }

    /// <summary>Mean (1 - dot) between the normals of horizontally adjacent quads. A smooth drape
    /// has neighbouring quads facing almost the same way, so this sits near 0; a surface folded back
    /// through itself does not.</summary>
    private float Incoherence(Vector3[] v)
    {
        double sum = 0d; int n = 0;
        for (int z = 0; z < Grid - 1; z++)
        for (int x = 0; x < Grid - 2; x++)
        {
            Vector3 a = QuadNormal(v, x, z), b = QuadNormal(v, x + 1, z);
            if (a == Vector3.zero || b == Vector3.zero) continue;
            sum += 1d - Vector3.Dot(a, b);
            n++;
        }
        return n == 0 ? 0f : (float)(sum / n);
    }

    private Vector3 QuadNormal(Vector3[] v, int x, int z)
    {
        int i = z * Grid + x;
        if (i + Grid + 1 >= v.Length) return Vector3.zero;
        Vector3 n = Vector3.Cross(v[i + 1] - v[i], v[i + Grid] - v[i]);
        return n.sqrMagnitude > 1e-18f ? n.normalized : Vector3.zero;
    }

    /// <summary>Vertex pairs that are FAR apart in the grid but CLOSE together in space -- the
    /// direct count of the surface sitting inside itself. Bucketed on a uniform grid so it is O(n)
    /// rather than O(n^2); the bucket size is the search radius, so every pair within the radius is
    /// in this cell or one of its 26 neighbours.</summary>
    private int ClosePairs(Vector3[] v)
    {
        double authored = (_authored[1] - _authored[0]).magnitude;
        if (authored <= 0d) return -1;
        float r = (float)(0.35d * authored);
        var buckets = new Dictionary<long, List<int>>(v.Length);
        for (int i = 0; i < v.Length; i++)
        {
            long key = Key(v[i], r);
            if (!buckets.TryGetValue(key, out List<int> l)) buckets[key] = l = new List<int>(4);
            l.Add(i);
        }
        int count = 0;
        float r2 = r * r;
        for (int i = 0; i < v.Length; i++)
        {
            int zi = i / Grid, xi = i % Grid;
            int cx = Mathf.FloorToInt(v[i].x / r), cy = Mathf.FloorToInt(v[i].y / r), cz = Mathf.FloorToInt(v[i].z / r);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!buckets.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out List<int> l)) continue;
                for (int k = 0; k < l.Count; k++)
                {
                    int j = l[k];
                    if (j <= i) continue;
                    int zj = j / Grid, xj = j % Grid;
                    if (Math.Abs(zi - zj) <= 2 && Math.Abs(xi - xj) <= 2) continue;   // grid neighbours
                    if ((v[i] - v[j]).sqrMagnitude < r2) count++;
                }
            }
        }
        return count;
    }

    private static long Key(Vector3 p, float r) =>
        Key(Mathf.FloorToInt(p.x / r), Mathf.FloorToInt(p.y / r), Mathf.FloorToInt(p.z / r));

    private static long Key(int x, int y, int z) =>
        ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

    private static float Dev(Vector3[] a, Vector3[] b)
    {
        double w = 0d; int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) { double d = (a[i] - b[i]).magnitude; if (d > w) w = d; }
        return (float)w;
    }

    private static float DevMean(Vector3[] a, Vector3[] b)
    {
        double s = 0d; int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) s += (a[i] - b[i]).magnitude;
        return (float)(s / Math.Max(1, n));
    }

    // ---- the picture -------------------------------------------------------------------------

    /// <summary>
    /// A SOFTWARE wireframe rasterisation of the settled mesh, straight into a Texture2D. Not a
    /// Camera render: a built player only carries the shaders that were packed into it, and a
    /// bench that silently draws magenta -- or nothing -- is exactly the instrument that agrees with
    /// every broken build. Every edge of the grid is drawn, so a surface folded through itself
    /// reads as a scribble and a clean drape reads as a mesh.
    /// </summary>
    private void Render(string name, Vector3[] v)
    {
        const int W = 420, H = 420;
        var px = new Color32[W * H];
        var bg = new Color32(12, 12, 16, 255);
        for (int i = 0; i < px.Length; i++) px[i] = bg;

        // Fit the whole cloth into the frame in the x/y plane, seen from the front.
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i].x < minX) minX = v[i].x; if (v[i].x > maxX) maxX = v[i].x;
            if (v[i].y < minY) minY = v[i].y; if (v[i].y > maxY) maxY = v[i].y;
        }
        float spanX = Mathf.Max(1e-4f, maxX - minX), spanY = Mathf.Max(1e-4f, maxY - minY);
        float s = 0.86f * Mathf.Min(W / spanX, H / spanY);
        float ox = W * 0.5f - 0.5f * (minX + maxX) * s;
        float oy = H * 0.5f - 0.5f * (minY + maxY) * s;

        for (int z = 0; z < Grid; z++)
        for (int x = 0; x < Grid; x++)
        {
            int i = z * Grid + x;
            if (i >= v.Length) continue;
            // Depth-tint so a fold toward the camera is distinguishable from one away from it.
            byte tint = (byte)Mathf.Clamp(60 + 195 * (1f - (z / (float)(Grid - 1))), 0, 255);
            var col = new Color32(tint, (byte)(tint / 2 + 90), 255, 255);
            if (x + 1 < Grid && i + 1 < v.Length)
                Line(px, W, H, v[i], v[i + 1], s, ox, oy, col);
            if (z + 1 < Grid && i + Grid < v.Length)
                Line(px, W, H, v[i], v[i + Grid], s, ox, oy, col);
        }

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply(false);
        try
        {
            Directory.CreateDirectory("shots");
            File.WriteAllBytes(Path.Combine("shots", name + ".png"), tex.EncodeToPNG());
        }
        catch (Exception e) { Say("    [render] FAILED " + e.Message); }
        Destroy(tex);
    }

    private static void Line(Color32[] px, int w, int h, Vector3 a, Vector3 b,
                             float s, float ox, float oy, Color32 col)
    {
        float x0 = a.x * s + ox, y0 = a.y * s + oy, x1 = b.x * s + ox, y1 = b.y * s + oy;
        int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))), 1, 4096);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int xi = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
            int yi = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
            if (xi < 0 || yi < 0 || xi >= w || yi >= h) continue;
            px[(h - 1 - yi) * w + xi] = col;
        }
    }

    // ---- the cloth: a sheet pinned along one edge, draping under gravity --------------------

    private static GameObject BuildClothObject(int n, out Vector3[] authored)
    {
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var weights = new BoneWeight[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            verts[i] = new Vector3(x / (float)(n - 1) - 0.5f, 0f, -z / (float)(n - 1));
            normals[i] = Vector3.up;
            weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        }
        var tris = new List<int>((n - 1) * (n - 1) * 6);
        for (int z = 0; z < n - 1; z++)
        for (int x = 0; x < n - 1; x++)
        {
            int i = z * n + x;
            tris.Add(i); tris.Add(i + n); tris.Add(i + 1);
            tris.Add(i + 1); tris.Add(i + n); tris.Add(i + n + 1);
        }
        var mesh = new Mesh { name = "clothgrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = tris.ToArray();
        mesh.boneWeights = weights;
        mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.RecalculateBounds();
        authored = verts;

        var root = new GameObject("clothroot");
        var bone = new GameObject("bone");
        bone.transform.SetParent(root.transform, false);
        var smr = root.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.bones = new[] { bone.transform };
        smr.rootBone = bone.transform;
        smr.updateWhenOffscreen = true;
        return root;
    }
}
