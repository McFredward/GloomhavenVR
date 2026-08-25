// SETTLED-SIMULATING SHAPE under a transform rescale.
//
// ModBuild 285 validated the PINNED pose (maxDistance = 0) against a DISABLED cloth and shipped.
// It never measured the state the user is looking at in klamotten_problem.jpg: the cloth SETTLED
// and SIMULATING at the new size. This bench measures exactly that state and nothing else.
//
// Controls, because a new instrument's first output is a hypothesis:
//   NULL      two independent runs of the same arm -> the instrument floor
//   POSITIVE  a cloth BORN at scale S (cooked at S, coefficients authored x S) -> "correct" by
//             construction: the fabric and the coefficients agree with the mesh.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class ScaleShapeBench : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (a == "--bench=cost" || a == "--bench=sync" || a == "--bench=cook") return;
        var go = new GameObject("ScaleShapeBench");
        DontDestroyOnLoad(go);
        go.AddComponent<ScaleShapeBench>();
    }

    private const int Grid = 41;                 // 1681 cloth vertices
    private const float SlackMax = 0.35f;        // painted maxDistance at the free edge, metres
    /// <summary>The scale factor under test. Overridable with --S=1.04 so the RESIDUAL of a
    /// sub-threshold factor drift can be measured rather than extrapolated.</summary>
    private static readonly float S = ReadS();
    private static float ReadS()
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (a.StartsWith("--S=") && float.TryParse(a.Substring(4),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0f) return v;
        return 1.345f;
    }
    private static bool MinArms()
    {
        foreach (string a in Environment.GetCommandLineArgs()) if (a == "--arms=min") return true;
        return false;
    }
    private const int SettleFrames = 300;
    private const float FadeSeconds = 0.12f;

    private readonly List<string> _out = new List<string>();
    private void Say(string s) => _out.Add(s);
    private static double Ms(Stopwatch sw) => sw.ElapsedTicks * 1000d / Stopwatch.Frequency;

    private GameObject _root = null!;
    private SkinnedMeshRenderer _smr = null!;
    private Cloth _cloth = null!;
    private ClothSkinningCoefficient[] _pristine = Array.Empty<ClothSkinningCoefficient>();
    private Vector3[] _authored = Array.Empty<Vector3>();
    private Mesh _bake = null!;
    private string _cost = "";
    private string _trace = "";
    private float _pinFloor;

    private enum Arm
    {
        RefScale1, RefScale1Again, BornAtS,
        PinNoCook,            // what ModBuild 285 ships
        PinFadeCook,          // pinned, then SetEnabledFading(false) -> ... -> SetEnabledFading(true)
        PinNoStretchStiff,    // no cook: stretchingStiffness = 0
        PinNoTethers,         // no cook: useTethers = false
        PinNeither,           // no cook: both of the above
        ForeignEnableZeroPin, // the game's ForceSetLocoIntermediateTarget lands mid-gesture, pin = 0
        ForeignEnableEpsPin,  // the same, with the pin at a tiny epsilon instead of exactly 0
        PinFadeCookOrdered,   // pinned -> fade out -> upload FULL coefficients -> fade in
        PinHardCookOrdered,   // pinned -> enabled=false -> upload FULL coefficients -> enabled=true
        ShippedNew,           // EXACTLY what FigureCloth.StepCook does: upload+down on one frame, up on the next
        OldFading,            // builds 137-283
    }

    private static readonly Arm[] Arms =
    {
        Arm.RefScale1, Arm.RefScale1Again, Arm.BornAtS, Arm.PinNoCook, Arm.PinFadeCook,
        Arm.PinNoStretchStiff, Arm.PinNoTethers, Arm.PinNeither,
        Arm.ForeignEnableZeroPin, Arm.ForeignEnableEpsPin,
        Arm.PinFadeCookOrdered, Arm.PinHardCookOrdered, Arm.ShippedNew, Arm.OldFading,
    };

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;
        Say($"Unity {Application.unityVersion} | grid {Grid}x{Grid} ({Grid * Grid} verts) | painted "
            + $"maxDistance 0 at the pinned edge -> {SlackMax} m at the free edge | S = {S} | "
            + $"{SettleFrames} settle frames | ALL positions divided by the root scale before comparison");

        Vector3[] refShape = Array.Empty<Vector3>();
        Vector3[] gold = Array.Empty<Vector3>();

        foreach (Arm arm in Arms)
        {
            if (MinArms() && arm != Arm.RefScale1 && arm != Arm.BornAtS && arm != Arm.PinNoCook)
                continue;
            Vector3[] shape = null;
            _cost = ""; _trace = "";
            yield return RunArm(arm, r => shape = r);
            if (arm == Arm.RefScale1) refShape = shape;
            if (arm == Arm.BornAtS) gold = shape;
            Report(arm, shape, refShape, gold);
        }

        Debug.Log("[SHAPE] ==================== RESULTS ====================");
        for (int i = 0; i < _out.Count; i++) Debug.Log("[SHAPE] " + _out[i]);
        Debug.Log("[SHAPE] ==================== END ====================");
        Application.Quit(0);
    }

    private static string Title(Arm arm) => arm switch
    {
        Arm.RefScale1         => "REF          scale 1, simulating, settled",
        Arm.RefScale1Again    => "NULL         scale 1 again, identical setup (instrument floor)",
        Arm.BornAtS           => $"POSITIVE     cloth BORN at {S}: fabric cooked at {S}, coefficients authored x {S}",
        Arm.PinNoCook         => $"285 SHIPPED  pin ramp, coefficients x {S}, NO re-cook",
        Arm.PinFadeCook       => "CANDIDATE    285 plus SetEnabledFading(false)->(true) at the settle, WHILE PINNED",
        Arm.PinNoStretchStiff => "NO-COOK ALT  285 plus stretchingStiffness = 0",
        Arm.PinNoTethers      => "NO-COOK ALT  285 plus useTethers = false",
        Arm.PinNeither        => "NO-COOK ALT  285 plus stretchingStiffness = 0 AND useTethers = false",
        Arm.ForeignEnableZeroPin=> "HAZARD A     the game disables+re-enables the cloth mid-gesture while our pin is EXACTLY 0",
        Arm.ForeignEnableEpsPin => "HAZARD B     the same foreign enable, but our pin floor is 1e-3 x the cape extent",
        Arm.PinFadeCookOrdered=> "ORDERED A    pinned -> SetEnabledFading(false,0) -> upload FULL coefficients -> SetEnabledFading(true,0)",
        Arm.PinHardCookOrdered=> "ORDERED B    pinned -> enabled=false -> upload FULL coefficients -> enabled=true",
        Arm.ShippedNew        => "SHIPPED NEW  FigureCloth.StepCook verbatim: frame A upload FULL + enabled=false, frame B enabled=true",
        _                     => $"OLD 137-283  SetEnabledFading(false) -> coefficients x {S} -> SetEnabledFading(true)",
    };

    private void Report(Arm arm, Vector3[] shape, Vector3[] refShape, Vector3[] gold)
    {
        Say("");
        Say(Title(arm));
        if (_cost.Length > 0) Say("    " + _cost);
        if (_trace.Length > 0) Say("    " + _trace);
        Say($"    max sag (normalised, lowest y) {MinY(shape):F5}   |   mean |y| {MeanAbsY(shape):F5}");
        Say($"    edge / authored edge (normalised): mean {EdgeRatio(shape, false):F5}  most-deviating {EdgeRatio(shape, true):F5}");
        if (refShape.Length > 0)
            Say($"    vs REF     : worst per-vertex {Dev(shape, refShape):F5} m   mean {DevMean(shape, refShape):F5} m");
        if (gold.Length > 0 && arm != Arm.BornAtS && arm != Arm.RefScale1 && arm != Arm.RefScale1Again)
            Say($"    vs POSITIVE: worst per-vertex {Dev(shape, gold):F5} m   mean {DevMean(shape, gold):F5} m");
    }

    private IEnumerator RunArm(Arm arm, Action<Vector3[]> done)
    {
        float scale = arm == Arm.RefScale1 || arm == Arm.RefScale1Again ? 1f : S;

        if (_root != null) Destroy(_root);
        yield return null;
        _root = BuildClothObject(Grid, out _authored);
        _smr = _root.GetComponent<SkinnedMeshRenderer>();
        if (arm == Arm.BornAtS) _root.transform.localScale = Vector3.one * S;

        _cloth = _root.AddComponent<Cloth>();
        _cloth.useGravity = true;
        ClothSkinningCoefficient[] c = _cloth.coefficients;
        for (int i = 0; i < c.Length; i++)
        {
            float t = (i / Grid) / (float)(Grid - 1);   // row 0 is the pinned edge
            c[i].maxDistance = SlackMax * t;
            c[i].collisionSphereDistance = 0.01f * t;
        }
        _pristine = (ClothSkinningCoefficient[])c.Clone();
        if (arm == Arm.BornAtS)
            for (int i = 0; i < c.Length; i++) { c[i].maxDistance *= S; c[i].collisionSphereDistance *= S; }
        _cloth.coefficients = c;
        yield return null;

        for (int i = 0; i < SettleFrames; i++) yield return null;

        if (arm == Arm.RefScale1 || arm == Arm.RefScale1Again || arm == Arm.BornAtS)
        {
            done(Snapshot(scale));
            yield break;
        }

        // ---- the gesture: 1 -> S over 45 frames, exponential, FigureStretch's shape ----------
        var sw = new Stopwatch();
        double cookMs = 0d;
        float smoothed = 1f, weight = 1f;
        _pinFloor = arm == Arm.ForeignEnableEpsPin ? 1e-3f * _smr.sharedMesh.bounds.extents.magnitude : 0f;

        for (int f = 0; f < 45; f++)
        {
            smoothed = Mathf.Lerp(smoothed, S, 0.12f);
            _root.transform.localScale = Vector3.one * smoothed;
            if (arm == Arm.OldFading)
            {
                if (f == 0) _cloth.SetEnabledFading(false, FadeSeconds);
                if (f == 12 && _cloth.enabled) _cloth.enabled = false;
            }
            else
            {
                weight = Mathf.MoveTowards(weight, 0f, Time.unscaledDeltaTime / FadeSeconds);
                Upload(weight, smoothed);
                // The game's ForceSetLocoIntermediateTarget disables every m_Clothes entry and its
                // LateUpdate re-enables them two frames later. Reproduce that, landing inside our
                // pin window, because that is exactly when it happens on a figure being carried.
                if (arm == Arm.ForeignEnableZeroPin || arm == Arm.ForeignEnableEpsPin)
                {
                    if (f == 20) _cloth.enabled = false;
                    if (f == 22) _cloth.enabled = true;
                }
            }
            yield return null;
        }

        // ---- the settle ---------------------------------------------------------------------
        if (arm == Arm.OldFading)
        {
            Upload(1f, S);
            sw.Restart();
            _cloth.SetEnabledFading(true, FadeSeconds);
            if (!_cloth.enabled) _cloth.enabled = true;
            sw.Stop(); cookMs = Ms(sw);
            _cloth.ClearTransformMotion();
        }
        else
        {
            if (arm == Arm.PinFadeCook)
            {
                // The cloth is PINNED here (maxDistance == 0 everywhere), so it renders exactly as
                // the skinned mesh. Suspending and resuming it is therefore invisible, and the fade
                // times can be 0 for the same reason.
                _cloth.SetEnabledFading(false, 0f);
                for (int f = 0; f < 3 && _cloth.enabled; f++) yield return null;
                _cost = $"[state] cloth.enabled after SetEnabledFading(false, 0) + 3 frames = {_cloth.enabled}; ";
                sw.Restart();
                _cloth.SetEnabledFading(true, 0f);
                if (!_cloth.enabled) _cloth.enabled = true;
                sw.Stop(); cookMs = Ms(sw);
            }
            else if (arm == Arm.ShippedNew)
            {
                // StepCook, verbatim. The coefficients go up while the component is still ENABLED,
                // then it goes down in the same frame; the enable that cooks is the NEXT frame.
                Upload(1f, S);
                _cloth.enabled = false;
                weight = 1f;
                yield return null;
                sw.Restart();
                _cloth.enabled = true;
                sw.Stop(); cookMs = Ms(sw);
                _cloth.ClearTransformMotion();
                _cost = "[state] StepCook ordering; ";
            }
            else if (arm == Arm.PinFadeCookOrdered || arm == Arm.PinHardCookOrdered)
            {
                // The cloth is PINNED here (maxDistance == 0 everywhere), so it renders exactly as
                // the skinned mesh and suspending it is invisible. THE ORDER IS THE EXPERIMENT: the
                // full coefficients are uploaded while the component is DOWN, so the fabric that is
                // cooked on the way back in is cooked against a cloth that has somewhere to move.
                if (arm == Arm.PinFadeCookOrdered) _cloth.SetEnabledFading(false, 0f);
                else _cloth.enabled = false;
                for (int f = 0; f < 3 && _cloth.enabled; f++) yield return null;
                bool down = !_cloth.enabled;
                Upload(1f, S);
                weight = 1f;
                sw.Restart();
                if (arm == Arm.PinFadeCookOrdered) _cloth.SetEnabledFading(true, 0f);
                else _cloth.enabled = true;
                sw.Stop(); cookMs = Ms(sw);
                _cost = $"[state] component was DOWN before the enable = {down}; ";
                _cloth.ClearTransformMotion();
            }

            if (arm == Arm.PinNoStretchStiff || arm == Arm.PinNeither) _cloth.stretchingStiffness = 0f;
            if (arm == Arm.PinNoTethers || arm == Arm.PinNeither) _cloth.useTethers = false;

            for (int f = 0; f < 12; f++)
            {
                weight = Mathf.MoveTowards(weight, 1f, Time.unscaledDeltaTime / FadeSeconds);
                Upload(weight, S);
                yield return null;
            }
            _cloth.ClearTransformMotion();
        }

        var trace = new List<string>();
        for (int i = 0; i < SettleFrames; i++)
        {
            yield return null;
            if (i == 0 || i == 9 || i == 49 || i == 149 || i == SettleFrames - 1)
                trace.Add($"+{i + 1}f {MinY(Snapshot(scale)):F5}");
        }
        _cost += $"[cost] the enable transition on this arm: {cookMs:F3} ms at {Grid * Grid} verts | enabled at snapshot = {_cloth.enabled}";
        _trace = "settle trace (normalised lowest y): " + string.Join("  ", trace);
        done(Snapshot(scale));
    }

    /// <summary>The shipped ModBuild 285 coefficient write, verbatim.</summary>
    private void Upload(float weight, float factor)
    {
        var next = new ClothSkinningCoefficient[_pristine.Length];
        float bound = _smr.sharedMesh.bounds.extents.magnitude;
        for (int v = 0; v < _pristine.Length; v++)
        {
            float m = _pristine[v].maxDistance, s = _pristine[v].collisionSphereDistance;
            if (weight >= 1f)
            {
                next[v].maxDistance = m >= float.MaxValue ? m : m * factor;
                next[v].collisionSphereDistance = s >= float.MaxValue ? s : s * factor;
            }
            else if (weight <= 0f) { next[v].maxDistance = _pinFloor; next[v].collisionSphereDistance = _pinFloor; }
            else
            {
                float k = factor * weight, ramped = bound * weight;
                next[v].maxDistance = m >= float.MaxValue ? ramped : m * k;
                next[v].collisionSphereDistance = s >= float.MaxValue ? ramped : s * k;
            }
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

    private static float MeanAbsY(Vector3[] v)
    {
        double s = 0d;
        for (int i = 0; i < v.Length; i++) s += Math.Abs(v[i].y);
        return (float)(s / Math.Max(1, v.Length));
    }

    /// <summary>Solved edge length over AUTHORED edge length, in the normalised frame. The direct
    /// test of the fabric-rest-length hypothesis: a fabric cooked at scale 1 and driven at scale S
    /// holds world-space rest lengths sized for 1, so normalised edges come out near 1/S.</summary>
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

    // ---- the cloth: a horizontal sheet pinned along one edge, draping under gravity ---------

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
