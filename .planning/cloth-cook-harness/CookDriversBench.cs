// WHAT ACTUALLY DRIVES THE COST OF A FABRIC COOK.
//
// Run with --bench=drivers.
//
// THE NUMBER THIS BENCH EXISTS FOR. His ModBuild 289 log says the figure's three cloths carry
// 1413 coefficients in total, widest 771 -- and that the enable that cooks them costs
// 32.2 / 53.5 / 44.3 / 33.6 ms on average across four 30 s windows, worst 167.51 ms. The earlier
// round's law is ~5.3 us per cloth vertex, which predicts about FOUR milliseconds for a 771-vertex
// cloth. That is a factor of forty unaccounted for, and until it is named nobody can say whether a
// mid-gesture re-cook is affordable.
//
// So: hold the vertex count fixed near his widest cape (29x29 = 841) and vary ONE property of the
// Cloth at a time, timing the enable transition that cooks. Whatever multiplies it by forty is the
// answer; if nothing does, the 167 ms is not the cook and the scope is catching something else.
//
// Controls: a NULL (an empty timed body) and a KNOWN POSITIVE (AddComponent<Cloth>, which must cook
// from scratch) in every block, because a new instrument's first output is a hypothesis.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class CookDriversBench : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        bool want = false;
        foreach (string a in Environment.GetCommandLineArgs()) if (a == "--bench=drivers") want = true;
        if (!want) return;
        var go = new GameObject("CookDriversBench");
        DontDestroyOnLoad(go);
        go.AddComponent<CookDriversBench>();
    }

    private const int Reps = 8;
    private const int Base = 29;               // 841 cloth vertices -- his widest cape is 771

    private readonly List<string> _out = new List<string>();
    private void Say(string s) => _out.Add(s);
    private static double Ms(Stopwatch sw) => sw.ElapsedTicks * 1000d / Stopwatch.Frequency;

    private readonly Stopwatch _sw = new Stopwatch();
    private GameObject _root = null!;
    private SkinnedMeshRenderer _smr = null!;
    private Cloth _cloth = null!;
    private ClothSkinningCoefficient[] _full = Array.Empty<ClothSkinningCoefficient>();
    private readonly List<GameObject> _colliderGos = new List<GameObject>();

    /// <summary>What a cell does to the cloth before its fabric is cooked.</summary>
    private delegate void Knob(Cloth c);

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;
        Say($"Unity {Application.unityVersion} | {Reps} reps per cell, MEDIAN reported | "
            + "Linux desktop, NOT his rig -- read the RATIO between rows, not the absolute");

        // ---- block 1: re-derive the vertex law on THIS machine ------------------------------
        Say("");
        Say("--- the vertex law, re-derived on this machine (baseline cloth, nothing else set) ---");
        foreach (int g in new[] { 21, 29, 41, 61, 81 })
        {
            yield return Build(g, null);
            List<double> cook = null;
            yield return Cook(g, null, r => cook = r);
            Say($"    {g,2}x{g,-2} = {g * g,5} verts  cook {Fmt(cook)}   "
                + $"({Median(cook) * 1000d / (g * g):F2} us per vertex)");
        }

        // ---- block 2: the NULL and the KNOWN POSITIVE ---------------------------------------
        Say("");
        Say($"--- controls, at {Base}x{Base} = {Base * Base} verts ---");
        {
            var nul = new List<double>();
            for (int r = 0; r < Reps; r++) { _sw.Restart(); _sw.Stop(); nul.Add(Ms(_sw)); yield return null; }
            Say($"    NULL control (empty timed body) .................. {Fmt(nul)}");

            var pos = new List<double>();
            for (int r = 0; r < Reps; r++)
            {
                yield return Build(Base, null, addCloth: false);
                _sw.Restart();
                _cloth = _root.AddComponent<Cloth>();
                _sw.Stop();
                pos.Add(Ms(_sw));
                yield return null;
            }
            Say($"    POSITIVE control: AddComponent<Cloth> first cook . {Fmt(pos)}");
        }

        // ---- block 3: ONE property at a time, vertex count held fixed -----------------------
        Say("");
        Say($"--- one property at a time, all at {Base}x{Base} = {Base * Base} cloth vertices ---");
        Say("    (the ratio column is against the 'baseline' row directly below the controls)");

        var cells = new (string Name, Knob K)[]
        {
            ("baseline (nothing set)",                    null),
            ("selfCollisionDistance 0.005",               c => { c.selfCollisionDistance = 0.005f; c.selfCollisionStiffness = 0.2f; }),
            ("selfCollisionDistance 0.02",                c => { c.selfCollisionDistance = 0.02f;  c.selfCollisionStiffness = 0.2f; }),
            ("selfCollisionDistance 0.05",                c => { c.selfCollisionDistance = 0.05f;  c.selfCollisionStiffness = 0.2f; }),
            ("useVirtualParticles = true",                c => c.useVirtualParticles = 1f),
            ("useVirtualParticles = false",               c => c.useVirtualParticles = 0f),
            ("useTethers = false",                        c => c.useTethers = false),
            ("enableContinuousCollision = true",          c => c.enableContinuousCollision = true),
            ("clothSolverFrequency 300 (Unity default)",  c => c.clothSolverFrequency = 300f),
            ("clothSolverFrequency 30",                   c => c.clothSolverFrequency = 30f),
            ("useGravity = false",                        c => c.useGravity = false),
            ("stretchingStiffness = 0",                   c => c.stretchingStiffness = 0f),
            ("bendingStiffness = 1",                      c => c.bendingStiffness = 1f),
        };

        double baseline = 0d;
        foreach ((string name, Knob knob) in cells)
        {
            List<double> cook = null;
            yield return Cook(Base, knob, r => cook = r);
            double m = Median(cook);
            if (name.StartsWith("baseline")) baseline = m;
            Say($"    {name,-42} {Fmt(cook)}   {(baseline > 0d ? (m / baseline).ToString("F2") + "x" : "")}");
        }

        // ---- block 4: colliders, which a cape on a figure certainly has ---------------------
        Say("");
        Say($"--- assigned colliders, at {Base}x{Base} ---");
        foreach (int n in new[] { 0, 2, 4, 8, 16 })
        {
            int count = n;
            List<double> cook = null;
            yield return Cook(Base, c => AttachSpheres(c, count), r => cook = r);
            Say($"    {n,2} sphere collider pair(s) ....................... {Fmt(cook)}"
                + (baseline > 0d ? $"   {Median(cook) / baseline:F2}x" : ""));
        }
        foreach (int n in new[] { 2, 8 })
        {
            int count = n;
            List<double> cook = null;
            yield return Cook(Base, c => AttachCapsules(c, count), r => cook = r);
            Say($"    {n,2} capsule collider(s) ........................... {Fmt(cook)}"
                + (baseline > 0d ? $"   {Median(cook) / baseline:F2}x" : ""));
        }

        // ---- block 5: is the driver the CLOTH vertex count or the MESH vertex count? --------
        // A real cape's mesh has UV/normal seams, so its mesh vertex count is well above the number
        // of welded cloth particles. His census reports the COEFFICIENT array length, which is the
        // welded count. If the cook scales with the MESH instead, that census under-reports the
        // driver -- and this is the one hypothesis in the round brief that is decidable here.
        Say("");
        Say("--- welded cloth particles vs raw mesh vertices (the census reports the FORMER) ---");
        foreach (int split in new[] { 1, 2, 4 })
        {
            int s = split;
            List<double> cook = null;
            int clothVerts = 0, meshVerts = 0;
            yield return Cook(Base, null, r => cook = r, s, (cv, mv) => { clothVerts = cv; meshVerts = mv; });
            Say($"    seam split x{s} -> {clothVerts,5} cloth particles, {meshVerts,6} mesh vertices  "
                + $"cook {Fmt(cook)}");
        }

        // ---- block 6: does the COEFFICIENT CONTENT change the cook cost? --------------------
        Say("");
        Say("--- what the coefficients say when the enable happens ---");
        foreach ((string name, float slack) in new (string, float)[]
                 { ("all pinned (maxDistance 0)", 0f), ("pin floor (1e-3 x extent)", -1f),
                   ("authored slack", 1f), ("authored slack x 4", 4f) })
        {
            float sl = slack;
            List<double> cook = null;
            yield return Cook(Base, null, r => cook = r, 1, null, sl);
            Say($"    {name,-42} {Fmt(cook)}");
        }

        // ---- block 7: what a LIVE stiffness write costs --------------------------------------
        // The ModBuild 290 fix writes stretchingStiffness every ramp frame on an ENABLED cloth. If
        // that setter re-cooks the fabric the fix is unaffordable and must not ship, so it is timed
        // against the two known anchors in the same run: a coefficients upload (cheap, ~0.007 ms in
        // the ModBuild 284 table) and an enable transition (the cook).
        Say("");
        Say($"--- writing a property on a LIVE, ENABLED cloth, at {Base}x{Base} ---");
        {
            yield return Build(Base, null);
            for (int i = 0; i < 12; i++) yield return null;

            var stiffChanged = new List<double>();
            var stiffSame = new List<double>();
            var coeff = new List<double>();
            for (int r = 0; r < Reps; r++)
            {
                float v = 0.1f * (r + 1);
                _sw.Restart(); _cloth.stretchingStiffness = v; _sw.Stop();
                stiffChanged.Add(Ms(_sw));
                yield return null;
                _sw.Restart(); _cloth.stretchingStiffness = v; _sw.Stop();   // same value again
                stiffSame.Add(Ms(_sw));
                yield return null;
                _sw.Restart(); _cloth.coefficients = _full; _sw.Stop();
                coeff.Add(Ms(_sw));
                yield return null;
            }
            Say($"    stretchingStiffness = a NEW value ................ {Fmt(stiffChanged)}");
            Say($"    stretchingStiffness = the SAME value ............. {Fmt(stiffSame)}");
            Say($"    coefficients = full array (the known anchor) ..... {Fmt(coeff)}");
            Say($"    cloth still enabled afterwards = {_cloth.enabled}");
        }

        Debug.Log("[DRV] ==================== RESULTS ====================");
        for (int i = 0; i < _out.Count; i++) Debug.Log("[DRV] " + _out[i]);
        Debug.Log("[DRV] ==================== END ====================");
        Application.Quit(0);
    }

    /// <summary>One cell: build a fresh cloth, apply the knob, let it settle, take the component
    /// down, upload the coefficients while it is down, and time the enable that cooks. Repeated
    /// <see cref="Reps"/> times on <see cref="Reps"/> fresh cloths, because a cook is a first-time
    /// cost and re-timing the same component measures a warm one.</summary>
    private IEnumerator Cook(int grid, Knob knob, Action<List<double>> done,
                             int seamSplit = 1, Action<int, int> census = null, float slack = 1f)
    {
        var times = new List<double>(Reps);
        for (int r = 0; r < Reps; r++)
        {
            yield return Build(grid, knob, seamSplit: seamSplit, slack: slack);
            if (r == 0) census?.Invoke(_cloth.coefficients.Length, _smr.sharedMesh.vertexCount);
            for (int i = 0; i < 12; i++) yield return null;
            _cloth.enabled = false;
            yield return null;
            _cloth.coefficients = _full;      // full coefficients BEFORE the enable -- the shipped order
            _sw.Restart();
            _cloth.enabled = true;
            _sw.Stop();
            times.Add(Ms(_sw));
            yield return null;
        }
        done(times);
    }

    private IEnumerator Build(int grid, Knob knob, bool addCloth = true, int seamSplit = 1, float slack = 1f)
    {
        if (_root != null) Destroy(_root);
        for (int i = 0; i < _colliderGos.Count; i++) if (_colliderGos[i] != null) Destroy(_colliderGos[i]);
        _colliderGos.Clear();
        yield return null;

        _root = BuildClothObject(grid, seamSplit);
        _smr = _root.GetComponent<SkinnedMeshRenderer>();
        if (!addCloth) yield break;

        _cloth = _root.AddComponent<Cloth>();
        _cloth.useGravity = true;
        knob?.Invoke(_cloth);

        ClothSkinningCoefficient[] c = _cloth.coefficients;
        float bound = _smr.sharedMesh.bounds.extents.magnitude;
        for (int i = 0; i < c.Length; i++)
        {
            float t = (i / (float)Math.Max(1, c.Length - 1));
            float m = slack < 0f ? bound * 1e-3f : 0.35f * t * slack;
            c[i].maxDistance = m;
            c[i].collisionSphereDistance = 0.01f * t * Math.Max(0f, slack);
        }
        _full = (ClothSkinningCoefficient[])c.Clone();
        _cloth.coefficients = c;
        yield return null;
    }

    private void AttachSpheres(Cloth c, int n)
    {
        if (n == 0) { c.sphereColliders = Array.Empty<ClothSphereColliderPair>(); return; }
        var pairs = new ClothSphereColliderPair[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("s" + i);
            _colliderGos.Add(go);
            var sc = go.AddComponent<SphereCollider>();
            sc.radius = 0.05f;
            go.transform.position = new Vector3(0.1f * i - 0.3f, -0.2f, -0.5f);
            pairs[i] = new ClothSphereColliderPair(sc);
        }
        c.sphereColliders = pairs;
    }

    private void AttachCapsules(Cloth c, int n)
    {
        var caps = new CapsuleCollider[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("c" + i);
            _colliderGos.Add(go);
            var cc = go.AddComponent<CapsuleCollider>();
            cc.radius = 0.05f;
            cc.height = 0.3f;
            go.transform.position = new Vector3(0.1f * i - 0.3f, -0.2f, -0.5f);
            caps[i] = cc;
        }
        c.capsuleColliders = caps;
    }

    private static string Fmt(List<double> v)
    {
        if (v == null || v.Count == 0) return "n/a";
        var s = new List<double>(v);
        s.Sort();
        return $"{Median(v),8:F3} ms  (min {s[0]:F3} max {s[s.Count - 1]:F3})";
    }

    private static double Median(List<double> v)
    {
        if (v == null || v.Count == 0) return 0d;
        var s = new List<double>(v);
        s.Sort();
        return s[s.Count / 2];
    }

    /// <summary>A sheet pinned along one edge. <paramref name="seamSplit"/> duplicates every vertex
    /// that many times over, which is what UV and normal seams do to a real cape's mesh: Unity welds
    /// coincident positions into ONE cloth particle, so the coefficient array stays the same length
    /// while the mesh gets larger.</summary>
    private static GameObject BuildClothObject(int n, int seamSplit)
    {
        var verts = new List<Vector3>(n * n * seamSplit);
        var normals = new List<Vector3>(n * n * seamSplit);
        var weights = new List<BoneWeight>(n * n * seamSplit);
        for (int copy = 0; copy < seamSplit; copy++)
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            verts.Add(new Vector3(x / (float)(n - 1) - 0.5f, 0f, -z / (float)(n - 1)));
            normals.Add(Vector3.up);
            weights.Add(new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        }

        var tris = new List<int>((n - 1) * (n - 1) * 6);
        for (int z = 0; z < n - 1; z++)
        for (int x = 0; x < n - 1; x++)
        {
            // Triangles reference the FIRST copy only; the duplicates exist purely to inflate the
            // mesh vertex count the way a seam does, without changing the surface.
            int i = z * n + x;
            tris.Add(i); tris.Add(i + n); tris.Add(i + 1);
            tris.Add(i + 1); tris.Add(i + n); tris.Add(i + n + 1);
        }

        var mesh = new Mesh { name = "clothgrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.boneWeights = weights.ToArray();
        mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.RecalculateBounds();

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
