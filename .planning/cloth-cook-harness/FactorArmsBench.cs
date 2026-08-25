// THE DIRECTION ARMS AT THE FACTOR HE ACTUALLY USES.
//
// Run with --bench=factor.
//
// Every arm table this lane has ever produced was measured at 1.345x, which is the factor his
// ModBuild 289 log happened to settle at. His ModBuild 290 log settles at 2.5x, and his stretch
// gesture is clamped at [0.417 .. 2.503] -- so 2.5 is not an unusual reach, it is the END STOP, and
// the log shows him driving to it over and over (LivingBonesID 1 -> 2.503 -> 1.187; BanditGuardID
// 1 -> 2.426 -> 0.698 -> 2.152 -> 0.875 -> 2.347 -> 1.053).
//
// The stale-fabric error is a RATIO between the fabric's rest lengths and the body they are being
// stretched over, so it grows with the factor. A table at 1.345 is not evidence about 2.5 and this
// bench exists to stop that substitution being made a third time.
//
// Arms trimmed to the load-bearing set (the full fifteen are in DirectionArmsBench at 1.345):
//   Born / BornAgain   POSITIVE and NULL -- the instrument's own floor at THIS factor
//   Stale              no pin, no cook: what the fabric alone does
//   Pinned             ModBuild 289: what the player saw in figure_scale_problem.mp4
//   PinnedNoStretch    ModBuild 290: what shipped
//   Cooked             the settle -- must stay indistinguishable from Born at every factor

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class FactorArmsBench : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        bool want = false;
        foreach (string a in Environment.GetCommandLineArgs()) if (a == "--bench=factor") want = true;
        if (!want) return;
        var go = new GameObject("FactorArmsBench");
        DontDestroyOnLoad(go);
        go.AddComponent<FactorArmsBench>();
    }

    private const int Grid = 29;
    private const float SlackMax = 0.35f;
    private const int SettleFrames = 300;
    private const float PinFloorFraction = 1e-3f;

    private readonly List<string> _out = new List<string>();
    private void Say(string s) => _out.Add(s);

    private GameObject _root = null!;
    private SkinnedMeshRenderer _smr = null!;
    private Cloth _cloth = null!;
    private ClothSkinningCoefficient[] _pristine = Array.Empty<ClothSkinningCoefficient>();
    private Vector3[] _authored = Array.Empty<Vector3>();
    private Mesh _bake = null!;
    private float _reaction;

    private enum Arm { Born, BornAgain, Stale, Pinned, PinnedNoStretch, Cooked }

    private static readonly Arm[] Arms =
        { Arm.Born, Arm.BornAgain, Arm.Stale, Arm.Pinned, Arm.PinnedNoStretch, Arm.Cooked };

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;
        Say($"Unity {Application.unityVersion} | grid {Grid}x{Grid} ({Grid * Grid} particles) | "
            + $"{SettleFrames} settle frames | every position divided by the root scale before comparison");
        Say("incoherence = mean (1 - dot) between neighbouring quad normals. reaction = peak local");
        Say("departure under a 0.25 m swing: a cape that is pinned rigid reads ~0 no matter how big");
        Say("the swing, so it is the column that answers 'inklusive der Reaktion'.");

        // 1.345 is the CROSS-CHECK against the published ModBuild 290 table (SHRINK: POSITIVE
        // 0.0267, PINNED 0.4589, PIN+NOSTRETCH 0.0372). 2.5 is HIS factor. 1.7 and 2.0 are in
        // because the ModBuild 290 remedy helps at 1.345 and hurts at 2.5, and "where does it
        // invert" is a question a two-point table cannot answer — and a threshold set without
        // those points would be a guess wearing a measurement's clothes. 3.5 is past the gesture
        // clamp and is there for the trend only.
        float[] factors = { 1.345f, 1.7f, 2.0f, 2.5f, 3.5f };
        foreach (float big in factors)
        foreach (bool grow in new[] { true, false })
        {
            float a = grow ? 1f : big, b = grow ? big : 1f;
            Say("");
            Say("================================================================================");
            Say($"{(grow ? "GROW  " : "SHRINK")}  fabric cooked at {a:0.###}  ->  root driven to {b:0.###}"
                + (Mathf.Approximately(big, 2.5f) ? "     <- HIS ModBuild 290 SESSION" : ""));
            Say("================================================================================");
            Say("arm                 | mean edge | worst edge | incoherence | close pairs | vs POSITIVE worst/mean m | reaction m");

            Vector3[] gold = Array.Empty<Vector3>();
            foreach (Arm arm in Arms)
            {
                Vector3[] shape = null;
                _reaction = 0f;
                yield return RunArm(arm, a, b, r => shape = r);
                if (arm == Arm.Born) gold = shape;
                Say(string.Format(CultureInfo.InvariantCulture,
                    "{0,-19} |  {1,7:F4}  |  {2,8:F4}  |   {3,7:F4}   |    {4,4}     |    {5,7:F5} / {6,7:F5}   |  {7,7:F5}",
                    Label(arm), EdgeRatio(shape, false), EdgeRatio(shape, true), Incoherence(shape),
                    ClosePairs(shape),
                    arm == Arm.Born ? 0f : Dev(shape, gold),
                    arm == Arm.Born ? 0f : DevMean(shape, gold),
                    _reaction));
                Render($"f{big:0.0}-{(grow ? "grow" : "shrink")}-{arm}", shape);
            }
        }

        Debug.Log("[FAC] ==================== RESULTS ====================");
        for (int i = 0; i < _out.Count; i++) Debug.Log("[FAC] " + _out[i]);
        Debug.Log("[FAC] ==================== END ====================");
        Application.Quit(0);
    }

    private static string Label(Arm a) => a switch
    {
        Arm.Born            => "POSITIVE born at B",
        Arm.BornAgain       => "NULL positive twice",
        Arm.Stale           => "STALE no pin no cook",
        Arm.Pinned          => "PINNED (289)",
        Arm.PinnedNoStretch => "PIN+NOSTRETCH (290)",
        _                   => "COOKED (the settle)",
    };

    private IEnumerator RunArm(Arm arm, float a, float b, Action<Vector3[]> done)
    {
        if (_root != null) Destroy(_root);
        yield return null;
        _root = BuildClothObject(Grid, out _authored);
        _smr = _root.GetComponent<SkinnedMeshRenderer>();

        float birth = arm == Arm.Born || arm == Arm.BornAgain ? b : a;
        _root.transform.localScale = Vector3.one * birth;

        _cloth = _root.AddComponent<Cloth>();
        _cloth.useGravity = true;
        ClothSkinningCoefficient[] c = _cloth.coefficients;
        for (int i = 0; i < c.Length; i++)
        {
            float t = (i / Grid) / (float)(Grid - 1);
            c[i].maxDistance = SlackMax * t;
            c[i].collisionSphereDistance = 0.01f * t;
        }
        _cloth.coefficients = c;
        _pristine = (ClothSkinningCoefficient[])c.Clone();
        Upload(1f, birth);
        yield return null;
        for (int i = 0; i < SettleFrames; i++) yield return null;

        if (arm == Arm.Born || arm == Arm.BornAgain)
        {
            Vector3[] bornShape = Snapshot(b);
            yield return Reaction(b);
            done(bornShape);
            yield break;
        }

        float smoothed = a, weight = 1f;
        for (int f = 0; f < 45; f++)
        {
            smoothed = Mathf.Lerp(smoothed, b, 0.12f);
            _root.transform.localScale = Vector3.one * smoothed;
            weight = Mathf.MoveTowards(weight, 0f, Time.unscaledDeltaTime / 0.12f);
            if (arm == Arm.PinnedNoStretch) _cloth.stretchingStiffness = weight;
            Upload(weight, smoothed);
            yield return null;
        }
        _root.transform.localScale = Vector3.one * b;

        if (arm == Arm.Pinned || arm == Arm.PinnedNoStretch)
        {
            if (arm == Arm.PinnedNoStretch) _cloth.stretchingStiffness = 0f;
            for (int i = 0; i < SettleFrames; i++) { Upload(0f, b); yield return null; }
            Vector3[] pinShape = Snapshot(b);
            yield return Reaction(b);
            done(pinShape);
            yield break;
        }

        if (arm == Arm.Cooked)
        {
            Upload(1f, b);
            _cloth.stretchingStiffness = 1f;
            _cloth.enabled = false;
            yield return null;
            _cloth.enabled = true;
            _cloth.ClearTransformMotion();
        }
        else
        {
            Upload(1f, b);   // STALE: coefficients rescaled, fabric never re-cooked
        }

        for (int i = 0; i < SettleFrames; i++) yield return null;
        Vector3[] shape = Snapshot(b);
        yield return Reaction(b);
        done(shape);
    }

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
        _reaction = peak;
    }

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

    // ---- metrics -----------------------------------------------------------------------------

    private Vector3[] Snapshot(float scale)
    {
        Vector3[] v;
        if (_cloth != null && _cloth.enabled) v = _cloth.vertices;
        else { if (_bake == null) _bake = new Mesh(); _smr.BakeMesh(_bake); v = _bake.vertices; }
        var o = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) o[i] = v[i] / scale;
        return o;
    }

    private float EdgeRatio(Vector3[] v, bool worst)
    {
        if (v == null || v.Length < Grid + 1) return 0f;
        float authored = (_authored[1] - _authored[0]).magnitude;
        double sum = 0d; int n = 0; float mx = 0f;
        for (int z = 0; z < Grid; z++)
        for (int x = 0; x < Grid - 1; x++)
        {
            int i = z * Grid + x;
            if (i + 1 >= v.Length) continue;
            float r = (v[i + 1] - v[i]).magnitude / authored;
            sum += r; n++;
            if (r > mx) mx = r;
        }
        return worst ? mx : (n == 0 ? 0f : (float)(sum / n));
    }

    private float Incoherence(Vector3[] v)
    {
        if (v == null) return 0f;
        double sum = 0d; int n = 0;
        for (int z = 0; z < Grid - 1; z++)
        for (int x = 0; x < Grid - 2; x++)
        {
            Vector3 a = QuadNormal(v, x, z), b = QuadNormal(v, x + 1, z);
            if (a == Vector3.zero || b == Vector3.zero) continue;
            sum += 1d - Vector3.Dot(a, b); n++;
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

    /// <summary>Non-neighbour vertex pairs sitting inside each other: polygons through polygons,
    /// which a normal statistic can miss and an eye cannot.</summary>
    private int ClosePairs(Vector3[] v)
    {
        if (v == null) return 0;
        float authored = (_authored[1] - _authored[0]).magnitude;
        float r = 0.35f * authored;
        var buckets = new Dictionary<long, List<int>>();
        for (int i = 0; i < v.Length; i++)
        {
            long k = Key(v[i], r);
            if (!buckets.TryGetValue(k, out List<int> list)) buckets[k] = list = new List<int>();
            list.Add(i);
        }
        int count = 0;
        foreach (KeyValuePair<long, List<int>> kv in buckets)
        {
            List<int> list = kv.Value;
            for (int a = 0; a < list.Count; a++)
            for (int b = a + 1; b < list.Count; b++)
            {
                int i = list[a], j = list[b];
                int xi = i % Grid, zi = i / Grid, xj = j % Grid, zj = j / Grid;
                if (Mathf.Abs(xi - xj) <= 1 && Mathf.Abs(zi - zj) <= 1) continue;
                if ((v[i] - v[j]).sqrMagnitude < r * r) count++;
            }
        }
        return count;
    }

    private static long Key(Vector3 p, float r) =>
        ((long)Mathf.FloorToInt(p.x / r) * 73856093) ^
        ((long)Mathf.FloorToInt(p.y / r) * 19349663) ^
        ((long)Mathf.FloorToInt(p.z / r) * 83492791);

    private static float Dev(Vector3[] a, Vector3[] b)
    {
        if (a == null || b == null) return 0f;
        float m = 0f;
        int n = Mathf.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) { float d = (a[i] - b[i]).magnitude; if (d > m) m = d; }
        return m;
    }

    private static float DevMean(Vector3[] a, Vector3[] b)
    {
        if (a == null || b == null) return 0f;
        double s = 0d;
        int n = Mathf.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) s += (a[i] - b[i]).magnitude;
        return n == 0 ? 0f : (float)(s / n);
    }

    // ---- scaffolding -------------------------------------------------------------------------

    /// <summary>
    /// BYTE-FOR-BYTE THE MESH <c>DirectionArmsBench</c> BUILDS, and that is not a stylistic
    /// preference. The ModBuild 290 arm table — the one this bench exists to extend to 2.5× — was
    /// measured on a HORIZONTAL sheet: vertices at <c>(x, 0, −z)</c>, normals up, that winding, and
    /// a flag draping off one edge under gravity. The first cut of this bench built a VERTICAL
    /// curtain instead, and its 1.345× row disagreed with the published one (POSITIVE incoherence
    /// 0.0000 against 0.0267, PINNED 0.2175 against 0.4589) for no reason other than the geometry.
    /// Two instruments that disagree cannot both be cited, and the claim this round makes — that
    /// the ModBuild 290 remedy INVERTS between 1.345 and 2.5 — is worth nothing unless its 1.345
    /// row reproduces the table it is contradicting. So the 1.345 rows here are a built-in
    /// cross-check of the instrument against the published numbers, and they can only be that if
    /// the mesh is the same mesh.
    /// </summary>
    private static GameObject BuildClothObject(int grid, out Vector3[] authored)
    {
        var go = new GameObject("cape");
        var verts = new Vector3[grid * grid];
        var normals = new Vector3[grid * grid];
        var weights = new BoneWeight[grid * grid];
        for (int z = 0; z < grid; z++)
        for (int x = 0; x < grid; x++)
        {
            int i = z * grid + x;
            verts[i] = new Vector3(x / (float)(grid - 1) - 0.5f, 0f, -z / (float)(grid - 1));
            normals[i] = Vector3.up;
            weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        }
        var tris = new List<int>((grid - 1) * (grid - 1) * 6);
        for (int z = 0; z < grid - 1; z++)
        for (int x = 0; x < grid - 1; x++)
        {
            int i = z * grid + x;
            tris.Add(i); tris.Add(i + grid); tris.Add(i + 1);
            tris.Add(i + 1); tris.Add(i + grid); tris.Add(i + grid + 1);
        }
        var mesh = new Mesh { name = "clothgrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
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

    private void Render(string name, Vector3[] v)
    {
        if (v == null) return;
        const int W = 420, H = 420;
        var px = new Color32[W * H];
        var bg = new Color32(12, 12, 16, 255);
        for (int i = 0; i < px.Length; i++) px[i] = bg;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i].x < minX) minX = v[i].x; if (v[i].x > maxX) maxX = v[i].x;
            if (v[i].y < minY) minY = v[i].y; if (v[i].y > maxY) maxY = v[i].y;
        }
        float s = 0.86f * Mathf.Min(W / Mathf.Max(1e-4f, maxX - minX), H / Mathf.Max(1e-4f, maxY - minY));
        float ox = W * 0.5f - 0.5f * (minX + maxX) * s, oy = H * 0.5f - 0.5f * (minY + maxY) * s;
        for (int z = 0; z < Grid; z++)
        for (int x = 0; x < Grid; x++)
        {
            int i = z * Grid + x;
            if (i >= v.Length) continue;
            byte tint = (byte)Mathf.Clamp(60 + 195 * (1f - z / (float)(Grid - 1)), 0, 255);
            var col = new Color32(tint, (byte)(tint / 2 + 90), 255, 255);
            if (x + 1 < Grid && i + 1 < v.Length) Line(px, W, H, v[i], v[i + 1], s, ox, oy, col);
            if (z + 1 < Grid && i + Grid < v.Length) Line(px, W, H, v[i], v[i + Grid], s, ox, oy, col);
        }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply(false);
        try { Directory.CreateDirectory("shots"); File.WriteAllBytes(Path.Combine("shots", name + ".png"), tex.EncodeToPNG()); }
        catch (Exception e) { Say("    [render] FAILED " + e.Message); }
        Destroy(tex);
    }

    private static void Line(Color32[] px, int w, int h, Vector3 a, Vector3 b, float s, float ox, float oy, Color32 col)
    {
        float x0 = a.x * s + ox, y0 = a.y * s + oy, x1 = b.x * s + ox, y1 = b.y * s + oy;
        int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))), 1, 4096);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int xi = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), yi = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
            if (xi < 0 || yi < 0 || xi >= w || yi >= h) continue;
            px[(h - 1 - yi) * w + xi] = col;
        }
    }
}
