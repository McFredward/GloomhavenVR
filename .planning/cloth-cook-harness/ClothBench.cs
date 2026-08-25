// ROUND 6 — END-TO-END VALIDATION of the shipped ModBuild 284 FigureCloth algorithm.
//
// Rounds 1-5 measured individual calls. This round runs the ACTUAL logic, transcribed from
// src/GloomhavenVR/Board/FigureGrab/FigureCloth.cs, against a scripted stretch gesture, and
// compares it head to head with the logic it replaces (builds 137-283) in the same harness on the
// same cloth. What it reports is the OUTCOME, not the readiness of the mechanism:
//   * worst SINGLE-FRAME cost of the whole per-frame job — the number the user feels
//   * total cost across the gesture
//   * how many coefficient uploads each arm does
//   * how many Cloth ENABLE transitions each arm causes (the thing that cooks)
//   * the drift of the cape from its skinned pose while the size is moving, against a
//     DISABLED-cloth reference arm — i.e. does the new mechanism still deliver the ModBuild 137
//     guarantee that every part of the figure scales exactly
//   * whether the settled coefficients come out equal to pristine x factor

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class ClothBench : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        // This bench boots by DEFAULT and calls Application.Quit when it finishes, so it must stand
        // down for every other --bench flag or it silently runs (and ends) somebody else's run. It
        // did exactly that once: a --bench=drivers invocation produced a clean exit 0, 502 frames
        // and not one line of the bench that was asked for.
        foreach (string a in Environment.GetCommandLineArgs())
            if (a != null && a.StartsWith("--bench=")) return;
        var go = new GameObject("ClothBench");
        DontDestroyOnLoad(go);
        go.AddComponent<ClothBench>();
    }

    // --- transcribed constants (must match FigureCloth.cs) --------------------------------
    private const int SettleFrames = 3;
    private const float FactorEpsilon = 0.002f;
    private const float FadeSeconds = 0.12f;

    private const int Grid = 61;                 // 3721 cloth vertices
    private const float CapeSlack = 0.05f;
    private const float TargetFactor = 1.345f;   // the factor from his ModBuild 283 log
    private const int GestureFrames = 45;
    private const int TailFrames = 60;

    private readonly List<string> _out = new List<string>();
    private readonly Stopwatch _sw = new Stopwatch();
    private static double Ms(Stopwatch sw) => sw.ElapsedTicks * 1000d / Stopwatch.Frequency;
    private void Say(string s) => _out.Add(s);

    private Cloth _cloth = null!;
    private GameObject _root = null!;
    private ClothSkinningCoefficient[] _pristine = Array.Empty<ClothSkinningCoefficient>();
    private ClothSkinningCoefficient[] _scratchArr = Array.Empty<ClothSkinningCoefficient>();
    private Vector3[] _authored = Array.Empty<Vector3>();
    private float _rampBound = 1f;

    // --- per-arm tracked state -----------------------------------------------------------
    private float _factor, _seededFactor, _weight;
    private int _quiet;
    private bool _suspended, _dirty;
    private float _fadeEnds;                     // OLD arm only
    private int _uploads, _enableTransitions;
    private double _worstFrameMs, _totalMs, _worstDrift;
    private readonly List<double> _frameMs = new List<double>();

    private enum Arm { New, Old, DisabledReference }

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;
        Say($"Unity {Application.unityVersion} / grid {Grid}x{Grid} ({Grid * Grid} verts) / cape slack "
            + $"{CapeSlack} m / gesture 1 -> {TargetFactor} over {GestureFrames} frames, then {TailFrames} quiet");

        yield return Run(Arm.DisabledReference);
        yield return Run(Arm.Old);
        yield return Run(Arm.New);

        Debug.Log("[BENCH] ==================== RESULTS ====================");
        for (int i = 0; i < _out.Count; i++)
            Debug.Log("[BENCH] " + _out[i]);
        Debug.Log("[BENCH] ==================== END ====================");
        Application.Quit(0);
    }

    private IEnumerator Run(Arm arm)
    {
        yield return Rebuild();
        _cloth.useGravity = true;
        yield return Settle(60);

        _factor = 1f; _seededFactor = 1f; _weight = 1f;
        _quiet = 0; _suspended = false; _dirty = false; _fadeEnds = 0f;
        _uploads = 0; _enableTransitions = 0;
        _worstFrameMs = 0d; _totalMs = 0d; _worstDrift = 0d; _frameMs.Clear();

        var driftAt = new List<string>();
        float smoothed = 1f;

        for (int f = 0; f < GestureFrames + TailFrames; f++)
        {
            // the gesture: exponential approach, exactly the shape FigureStretch produces
            float target = f < GestureFrames ? TargetFactor : TargetFactor;
            smoothed = f < GestureFrames ? Mathf.Lerp(smoothed, target, 0.12f) : smoothed;
            _root.transform.localScale = Vector3.one * smoothed;

            _sw.Restart();
            switch (arm)
            {
                case Arm.New: NoteNew(smoothed); TickNew(); break;
                case Arm.Old: NoteOld(smoothed); TickOld(); break;
                case Arm.DisabledReference:
                    if (f == 0 && _cloth.enabled) _cloth.enabled = false;
                    break;
            }
            _sw.Stop();
            double ms = Ms(_sw);
            _totalMs += ms;
            if (ms > _worstFrameMs) _worstFrameMs = ms;
            if (ms > 0.001d) _frameMs.Add(ms);

            yield return null;

            double d = Drift(smoothed);
            if (f < GestureFrames && d > _worstDrift) _worstDrift = d;
            if (f == 5 || f == 20 || f == GestureFrames - 1 || f == GestureFrames + 20)
                driftAt.Add($"f{f} {d:F5}");
        }

        Say(arm switch
        {
            Arm.New => "NEW (ModBuild 284: pin via maxDistance, ramped, no enable transition)",
            Arm.Old => "OLD (builds 137-283: SetEnabledFading + hard enable/disable)",
            _ => "REFERENCE (cloth simply DISABLED for the whole gesture — 'correct' by construction)"
        });
        Say($"    worst SINGLE frame {_worstFrameMs,9:F4} ms   total over {GestureFrames + TailFrames} frames {_totalMs,9:F4} ms");
        Say($"    coefficient uploads {_uploads,4}   cloth ENABLE transitions {_enableTransitions}");
        _frameMs.Sort();
        if (_frameMs.Count > 0)
            Say($"    working frames only (n={_frameMs.Count}): min {_frameMs[0]:F4}  p50 {_frameMs[_frameMs.Count / 2]:F4}  "
                + $"p90 {_frameMs[(int)(_frameMs.Count * 0.9)]:F4}  max {_frameMs[_frameMs.Count - 1]:F4} ms");
        Say($"    drift from the skinned pose, worst during the gesture {_worstDrift:F5} "
            + $"(cape slack at final size = {CapeSlack * TargetFactor:F5}) | samples: {string.Join("  ", driftAt)}");

        if (arm == Arm.New)
        {
            ClothSkinningCoefficient[] final = _cloth.coefficients;
            double worstErr = 0d;
            for (int i = 0; i < final.Length; i++)
            {
                double want = _pristine[i].maxDistance * _seededFactor;
                double err = Math.Abs(final[i].maxDistance - want);
                if (err > worstErr) worstErr = err;
            }
            Say($"    settled coefficients vs pristine x settled factor {_seededFactor}: worst absolute error {worstErr:E3} "
                + $"(pristine {_pristine[0].maxDistance}, want {_pristine[0].maxDistance * _seededFactor}, "
                + $"got {final[0].maxDistance})");
        }
    }

    // ================= NEW: transcribed from FigureCloth.cs (ModBuild 284) =================

    private static bool IsUnconstrained(float v) => v >= float.MaxValue || float.IsInfinity(v);

    private void NoteNew(float factor)
    {
        if (Mathf.Abs(factor - _factor) > _factor * FactorEpsilon)
        {
            _factor = factor;
            _quiet = 0;
            if (!_suspended)
                _suspended = true;         // Suspend(): rescan only, no cloth write
        }
    }

    private void TickNew()
    {
        if (_suspended && _quiet >= SettleFrames)
        {
            float factor = _factor;
            if (Mathf.Abs(factor - 1f) <= FactorEpsilon) factor = 1f;
            _seededFactor = factor;
            _suspended = false;
            _dirty = true;
        }
        AdvanceNew();
        _quiet++;
    }

    private void AdvanceNew()
    {
        float target = _suspended ? 0f : 1f;
        if (_weight != target)
        {
            float step = Time.unscaledDeltaTime / FadeSeconds;
            float before = _weight;
            _weight = Mathf.MoveTowards(_weight, target, step);
            if (_weight != before) _dirty = true;
        }
        if (!_dirty) return;
        _dirty = false;

        bool settled = !_suspended && _weight >= 1f;
        float factor = _seededFactor, weight = _weight, bound = _rampBound;
        if (weight >= 1f)
        {
            for (int v = 0; v < _pristine.Length; v++)
            {
                float m = _pristine[v].maxDistance, sph = _pristine[v].collisionSphereDistance;
                _scratchArr[v].maxDistance = m >= float.MaxValue ? m : m * factor;
                _scratchArr[v].collisionSphereDistance = sph >= float.MaxValue ? sph : sph * factor;
            }
        }
        else if (weight <= 0f)
        {
            for (int v = 0; v < _pristine.Length; v++)
            {
                _scratchArr[v].maxDistance = 0f;
                _scratchArr[v].collisionSphereDistance = 0f;
            }
        }
        else
        {
            float k = factor * weight, ramped = bound * weight;
            for (int v = 0; v < _pristine.Length; v++)
            {
                float m = _pristine[v].maxDistance, sph = _pristine[v].collisionSphereDistance;
                _scratchArr[v].maxDistance = m >= float.MaxValue ? ramped : m * k;
                _scratchArr[v].collisionSphereDistance = sph >= float.MaxValue ? ramped : sph * k;
            }
        }
        _cloth.coefficients = _scratchArr;
        _uploads++;
        if (settled) _cloth.ClearTransformMotion();
    }

    private static float Value(float authored, float factor, float weight, float bound)
    {
        bool unconstrained = IsUnconstrained(authored);
        if (weight >= 1f) return unconstrained ? authored : authored * factor;
        if (weight <= 0f) return 0f;
        return (unconstrained ? bound : authored * factor) * weight;
    }

    // ================= OLD: transcribed from FigureCloth.cs at e9805fd1 ====================

    private void NoteOld(float factor)
    {
        if (Mathf.Abs(factor - _factor) > _factor * FactorEpsilon)
        {
            _factor = factor;
            _quiet = 0;
            if (!_suspended) SuspendOld();
        }
    }

    private void SuspendOld()
    {
        _suspended = true;
        _fadeEnds = Time.unscaledTime + FadeSeconds;
        bool was = _cloth.enabled;
        _cloth.SetEnabledFading(false, FadeSeconds);
        if (!was && _cloth.enabled) _enableTransitions++;
    }

    private void TickOld()
    {
        if (_suspended)
        {
            if (_quiet >= SettleFrames) ResumeOld();
            else if (Time.unscaledTime >= _fadeEnds) { if (_cloth.enabled) _cloth.enabled = false; }
        }
        _quiet++;
    }

    private void ResumeOld()
    {
        float factor = _factor;
        bool identity = Mathf.Abs(factor - 1f) <= FactorEpsilon;
        var next = new ClothSkinningCoefficient[_pristine.Length];
        for (int v = 0; v < _pristine.Length; v++)
        {
            float max = _pristine[v].maxDistance;
            float sphere = _pristine[v].collisionSphereDistance;
            next[v].maxDistance = identity || float.IsInfinity(max) ? max : max * factor;
            next[v].collisionSphereDistance = identity || float.IsInfinity(sphere) ? sphere : sphere * factor;
        }
        _cloth.coefficients = next;
        _uploads++;

        bool was = _cloth.enabled;
        _cloth.SetEnabledFading(true, FadeSeconds);
        if (!was && _cloth.enabled) _enableTransitions++;
        if (!_cloth.enabled) { _cloth.enabled = true; _enableTransitions++; }
        _cloth.ClearTransformMotion();

        _suspended = false;
        _fadeEnds = Time.unscaledTime + FadeSeconds;
    }

    // ================= harness ============================================================

    private Mesh? _bake;

    private double Drift(float scale)
    {
        Vector3[] v;
        if (_cloth.enabled)
        {
            v = _cloth.vertices;
        }
        else
        {
            _bake ??= new Mesh();
            _smr.BakeMesh(_bake);
            v = _bake.vertices;
        }
        // BOTH cloth.vertices and BakeMesh report in the SCALED frame (round 4 arms A and B both
        // read 0.48790 = |authored| * 0.345 at the corner, which is exactly authored * 1.345
        // measured against unscaled authored). Round 6 divided only the baked branch and so
        // reported the scale itself as cloth drift. Normalise both.
        for (int i = 0; i < v.Length; i++) v[i] /= scale;
        double worst = 0d;
        int n = Mathf.Min(v.Length, _authored.Length);
        for (int i = 0; i < n; i++)
        {
            double d = (v[i] - _authored[i]).magnitude * scale;
            if (d > worst) worst = d;
        }
        return worst;
    }

    private IEnumerator Settle(int frames)
    {
        for (int i = 0; i < frames; i++) yield return null;
    }

    private SkinnedMeshRenderer _smr = null!;

    private IEnumerator Rebuild()
    {
        if (_root != null) Destroy(_root);
        yield return null;
        _root = BuildClothObject(Grid, out _authored);
        _smr = _root.GetComponent<SkinnedMeshRenderer>();
        _cloth = _root.AddComponent<Cloth>();
        ClothSkinningCoefficient[] c = _cloth.coefficients;
        for (int i = 0; i < c.Length; i++)
        {
            c[i].maxDistance = CapeSlack;
            c[i].collisionSphereDistance = 0.01f;
        }
        _cloth.coefficients = c;
        _pristine = c;
        _scratchArr = new ClothSkinningCoefficient[c.Length];
        Mesh m = _smr.sharedMesh;
        _rampBound = m.bounds.extents.magnitude;
        yield return null;
    }

    private static GameObject BuildClothObject(int n, out Vector3[] authored)
    {
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var weights = new BoneWeight[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int i = y * n + x;
            verts[i] = new Vector3(x / (float)(n - 1), -y / (float)(n - 1), 0f);
            normals[i] = Vector3.back;
            weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        }
        var tris = new List<int>((n - 1) * (n - 1) * 6);
        for (int y = 0; y < n - 1; y++)
        for (int x = 0; x < n - 1; x++)
        {
            int i = y * n + x;
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
