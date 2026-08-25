// WHAT A RE-COOK COSTS, and WHETHER ASSIGNING COLLIDER ARRAYS COSTS ONE TOO.
//
// Run with --bench=cost. Every timed body is measured on its own frame, N times, with a NULL
// control (an empty timed body) and a KNOWN-POSITIVE control (AddComponent<Cloth>, which must cook
// the fabric from scratch), because a new instrument's first output is a hypothesis.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class CookCostBench : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        bool want = false;
        foreach (string a in Environment.GetCommandLineArgs()) if (a == "--bench=cost") want = true;
        if (!want) return;
        var go = new GameObject("CookCostBench");
        DontDestroyOnLoad(go);
        go.AddComponent<CookCostBench>();
    }

    private const int Reps = 8;
    private static readonly int[] Grids = { 41, 61, 81 };   // 1681 / 3721 / 6561 vertices

    private readonly List<string> _out = new List<string>();
    private void Say(string s) { _out.Add(s); }
    private static double Ms(Stopwatch sw) => sw.ElapsedTicks * 1000d / Stopwatch.Frequency;

    private readonly Stopwatch _sw = new Stopwatch();
    private GameObject _root = null!;
    private SkinnedMeshRenderer _smr = null!;
    private Cloth _cloth = null!;
    private ClothSkinningCoefficient[] _full = Array.Empty<ClothSkinningCoefficient>();

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;
        Say($"Unity {Application.unityVersion} | {Reps} reps per cell, median reported | Linux desktop, NOT a Quest 3");

        foreach (int g in Grids)
        {
            Say("");
            Say($"--- {g}x{g} = {g * g} cloth vertices ---------------------------------------");

            yield return Build(g);
            Say($"    NULL control (empty timed body) .................. {Fmt(yield_null())}");

            // KNOWN POSITIVE: a first cook.
            var pos = new List<double>();
            for (int r = 0; r < Reps; r++)
            {
                yield return BuildNoCloth(g);
                _sw.Restart();
                _cloth = _root.AddComponent<Cloth>();
                _sw.Stop();
                pos.Add(Ms(_sw));
                yield return null;
            }
            Say($"    POSITIVE control: AddComponent<Cloth> first cook . {Fmt(pos)}");

            // The re-cook this lane needs: coefficients uploaded while DOWN, then enabled.
            var cook = new List<double>();
            var down = new List<double>();
            for (int r = 0; r < Reps; r++)
            {
                yield return Build(g);
                for (int i = 0; i < 20; i++) yield return null;
                _sw.Restart(); _cloth.enabled = false; _sw.Stop(); down.Add(Ms(_sw));
                yield return null;
                _cloth.coefficients = _full;              // full coefficients BEFORE the enable
                _sw.Restart(); _cloth.enabled = true; _sw.Stop(); cook.Add(Ms(_sw));
                yield return null;
            }
            Say($"    enabled = false (the way down) ................... {Fmt(down)}");
            Say($"    enabled = true  (THE RE-COOK) .................... {Fmt(cook)}");

            // ITEM 2: does assigning the collider arrays cook?
            yield return Build(g);
            for (int i = 0; i < 20; i++) yield return null;

            var sphereGo = new GameObject("handSphereA");
            var sphereGo2 = new GameObject("handSphereB");
            var sa = sphereGo.AddComponent<SphereCollider>(); sa.radius = 0.05f;
            var sb = sphereGo2.AddComponent<SphereCollider>(); sb.radius = 0.05f;
            sphereGo.transform.position = new Vector3(0f, -0.2f, -0.5f);
            sphereGo2.transform.position = new Vector3(0f, -0.2f, -0.7f);

            var assign = new List<double>();
            var reassign = new List<double>();
            var move = new List<double>();
            var pairs = new[] { new ClothSphereColliderPair(sa), new ClothSphereColliderPair(sa, sb) };
            for (int r = 0; r < Reps; r++)
            {
                _sw.Restart(); _cloth.sphereColliders = pairs; _sw.Stop();
                (r == 0 ? assign : reassign).Add(Ms(_sw));
                yield return null;
                _sw.Restart();
                sphereGo.transform.position += new Vector3(0.001f, 0f, 0f);
                _sw.Stop(); move.Add(Ms(_sw));
                yield return null;
            }
            Say($"    sphereColliders = pairs, FIRST assignment ........ {Fmt(assign)}");
            Say($"    sphereColliders = pairs, RE-assignment ........... {Fmt(reassign)}");
            Say($"    moving an assigned collider's transform ......... {Fmt(move)}");

            var caps = new List<double>();
            var capGo = new GameObject("handCapsule");
            var cc = capGo.AddComponent<CapsuleCollider>(); cc.radius = 0.05f; cc.height = 0.2f;
            for (int r = 0; r < Reps; r++)
            {
                _sw.Restart(); _cloth.capsuleColliders = new[] { cc }; _sw.Stop();
                caps.Add(Ms(_sw));
                yield return null;
            }
            Say($"    capsuleColliders = one capsule .................. {Fmt(caps)}");

            var clear = new List<double>();
            for (int r = 0; r < Reps; r++)
            {
                _cloth.sphereColliders = pairs;
                yield return null;
                _sw.Restart(); _cloth.sphereColliders = Array.Empty<ClothSphereColliderPair>(); _sw.Stop();
                clear.Add(Ms(_sw));
                yield return null;
            }
            Say($"    sphereColliders = empty (clearing them) ......... {Fmt(clear)}");

            Destroy(sphereGo); Destroy(sphereGo2); Destroy(capGo);
            yield return null;
        }

        Debug.Log("[COST] ==================== RESULTS ====================");
        for (int i = 0; i < _out.Count; i++) Debug.Log("[COST] " + _out[i]);
        Debug.Log("[COST] ==================== END ====================");
        Application.Quit(0);
    }

    private List<double> yield_null()
    {
        var l = new List<double>();
        for (int r = 0; r < Reps; r++) { _sw.Restart(); _sw.Stop(); l.Add(Ms(_sw)); }
        return l;
    }

    private static string Fmt(List<double> v)
    {
        v.Sort();
        return $"median {v[v.Count / 2],9:F4} ms   min {v[0],9:F4}   max {v[v.Count - 1],9:F4}";
    }

    private IEnumerator BuildNoCloth(int g)
    {
        if (_root != null) Destroy(_root);
        yield return null;
        _root = Build(g, out _);
        _smr = _root.GetComponent<SkinnedMeshRenderer>();
        yield return null;
    }

    private IEnumerator Build(int g)
    {
        yield return BuildNoCloth(g);
        _cloth = _root.AddComponent<Cloth>();
        _cloth.useGravity = true;
        ClothSkinningCoefficient[] c = _cloth.coefficients;
        for (int i = 0; i < c.Length; i++)
        {
            float t = (i / g) / (float)(g - 1);
            c[i].maxDistance = 0.35f * t;
            c[i].collisionSphereDistance = 0.01f * t;
        }
        _cloth.coefficients = c;
        _full = c;
        yield return null;
    }

    private static GameObject Build(int n, out Vector3[] authored)
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
