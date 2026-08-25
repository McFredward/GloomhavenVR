// "INKLUSIVE DER REAKTION" — CAN THE FABRIC FOLLOW THE SIZE WHILE THE GESTURE IS RUNNING?
//
// Run with --bench=throttle.
//
// ModBuild 290 answered this question NO, on the strength of one number: "a cook is 25-165 ms on
// his rig". The counter that shipped in the same build says his session's mean cook was 103
// particles and its worst was 143, and FigureGrab.Cloth.Cook does not appear anywhere on a STEPS
// TAIL line whose floor is 1.0 ms/s -- seventeen cooks together cost under 30 ms. So the premise
// the NO rested on is gone and the question is open again.
//
// This bench is the state machine, transcribed from FigureCloth.cs, with one new arm: a THROTTLED
// mid-gesture re-cook. Every P frames a cloth uploads its coefficients at the CURRENT size, goes
// down for one frame and comes back up -- and that enable bakes a fabric that matches the body it
// is on. Between cooks the cape is not pinned at all: it simulates, it drapes, and it reacts.
//
// TWO METRICS, NOT ONE, and the second is the one that can kill the idea:
//
//   INCOHERENCE   mean (1 - dot) between neighbouring quad normals -- "Polygon matsch" as a number.
//
//   JITTER        mean per-vertex movement between consecutive frames, in the scale-normalised
//                 frame. A cloth is DISABLED for one frame of every cook, and a disabled
//                 SkinnedMeshRenderer draws the bare skinned pose with no drape at all. If that
//                 reads as a pop, the throttle is unusable however good its drape statistic is,
//                 and a shape-only table would have agreed with it enthusiastically. Reported as
//                 the mean over the window AND the worst single frame.
//
// Script: grow 1 -> 2.5 (his end stop), pause, shrink 2.5 -> 1, release. Three cloths, because the
// stagger needs more than one to be a stagger.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class ThrottleBench : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        bool want = false;
        foreach (string a in Environment.GetCommandLineArgs()) if (a == "--bench=throttle") want = true;
        if (!want) return;
        var go = new GameObject("ThrottleBench");
        DontDestroyOnLoad(go);
        go.AddComponent<ThrottleBench>();
    }

    private const int Grid = 29;
    private const int Cloths = 3;
    private const float SlackMax = 0.35f;
    private const float Big = 2.5f;            // HIS factor. The gesture clamp is [0.417 .. 2.503].
    private const float FactorEpsilon = 0.002f;
    private const int SettleFrames = 12;
    private const float FadeSeconds = 0.12f;
    private const float PinFloorFraction = 1e-3f;

    private readonly List<string> _out = new List<string>();
    private void Say(string s) => _out.Add(s);

    private sealed class Tracked
    {
        public readonly List<Cloth> Cloths = new List<Cloth>();
        public readonly List<ClothSkinningCoefficient[]> Pristine = new List<ClothSkinningCoefficient[]>();
        public readonly List<ClothSkinningCoefficient[]> Scratch = new List<ClothSkinningCoefficient[]>();
        public readonly List<float> RampBound = new List<float>();
        public readonly List<float> PristineStretch = new List<float>();
        public readonly List<float> CookedFactor = new List<float>();
        public readonly List<bool> LiveDown = new List<bool>();   // this cloth is mid throttled cook
        public float Factor = 1f, SeededFactor = 1f, Weight = 1f, StretchWeight = 1f;
        public int QuietFrames;
        public bool Suspended, Dirty, Cooking, CookDown;
        public int CookIndex, CookCount;
        public int Cooks, Aborts, Seeds, LiveCooks;
        public readonly List<string> Events = new List<string>();
    }

    // arm 0 = ModBuild 289, arm 1 = ModBuild 290 shipped, arm 2 = 290 again (NULL), arm >= 3 = throttled
    private int _period;          // 0 = no throttle
    private bool _new;
    private int _frame;
    private Tracked _t = null!;
    private GameObject _root = null!;
    private readonly List<SkinnedMeshRenderer> _smrs = new List<SkinnedMeshRenderer>();
    private Mesh _bake = null!;

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;
        Say($"Unity {Application.unityVersion} | {Cloths} cloths x {Grid * Grid} particles | "
            + $"grow 1 -> {Big:0.###}, pause, shrink {Big:0.###} -> 1, release");
        Say("JITTER is mean per-vertex movement between consecutive frames in the scale-normalised");
        Say("frame. A rigid pinned cape reads ~0. A cape that is disabled for one frame per cook");
        Say("reads that frame as a spike in the WORST column -- that is the pop, and it is the");
        Say("number that decides whether a throttled re-cook is shippable.");
        Say("");
        Say("arm                      | GROW inc worst/mean | SHRINK inc worst/mean | GROW jit mean/worst | SHRINK jit mean/worst | SETTLED inc | react m | cooks");

        var arms = new List<(string name, bool isNew, int period)>
        {
            ("289 pin only",            false, 0),
            ("290 SHIPPED",             true,  0),
            ("290 SHIPPED (NULL rerun)",true,  0),
            ("throttle P=6",            true,  6),
            ("throttle P=9",            true,  9),
            ("throttle P=12",           true, 12),
            ("throttle P=18",           true, 18),
            ("throttle P=30",           true, 30),
        };

        foreach (var arm in arms)
        {
            _new = arm.isNew;
            _period = arm.period;
            yield return RunGesture(arm.name);
        }

        Debug.Log("[THR] ==================== RESULTS ====================");
        for (int i = 0; i < _out.Count; i++) Debug.Log("[THR] " + _out[i]);
        Debug.Log("[THR] ==================== END ====================");
        Application.Quit(0);
    }

    private IEnumerator RunGesture(string tag)
    {
        if (_root != null) Destroy(_root);
        yield return null;
        Build();
        _t = new Tracked();
        for (int i = 0; i < _smrs.Count; i++)
        {
            Cloth c = _smrs[i].gameObject.AddComponent<Cloth>();
            c.useGravity = true;
            ClothSkinningCoefficient[] co = c.coefficients;
            for (int v = 0; v < co.Length; v++)
            {
                float f = (v / Grid) / (float)(Grid - 1);
                co[v].maxDistance = SlackMax * f;
                co[v].collisionSphereDistance = 0.01f * f;
            }
            c.coefficients = co;
            _t.Cloths.Add(c);
            _t.Pristine.Add((ClothSkinningCoefficient[])co.Clone());
            _t.Scratch.Add(new ClothSkinningCoefficient[co.Length]);
            _t.RampBound.Add(_smrs[i].sharedMesh.bounds.extents.magnitude);
            _t.PristineStretch.Add(c.stretchingStiffness);
            _t.CookedFactor.Add(1f);
            _t.LiveDown.Add(false);
        }
        for (int i = 0; i < 150; i++) yield return null;
        _frame = 0;

        var growInc = new List<float>();
        var shrinkInc = new List<float>();
        var growJit = new List<float>();
        var shrinkJit = new List<float>();
        Vector3[] worstShrink = null;
        float worstShrinkV = -1f;

        float smoothed = 1f;
        Vector3[] prev = Snapshot(smoothed);

        for (int f = 0; f < 30; f++)
        {
            smoothed = Mathf.Lerp(smoothed, Big, 0.12f);
            _root.transform.localScale = Vector3.one * smoothed;
            Note(smoothed);
            Tick();
            yield return null;
            Vector3[] s = Snapshot(smoothed);
            growInc.Add(Incoherence(s));
            growJit.Add(MeanStep(s, prev));
            prev = s;
        }
        for (int f = 0; f < 14; f++) { Note(smoothed); Tick(); yield return null; prev = Snapshot(smoothed); }

        for (int f = 0; f < 45; f++)
        {
            smoothed = Mathf.Lerp(smoothed, 1f, 0.12f);
            _root.transform.localScale = Vector3.one * smoothed;
            Note(smoothed);
            Tick();
            yield return null;
            Vector3[] s = Snapshot(smoothed);
            float inc = Incoherence(s);
            shrinkInc.Add(inc);
            shrinkJit.Add(MeanStep(s, prev));
            prev = s;
            if (inc > worstShrinkV) { worstShrinkV = inc; worstShrink = s; }
        }

        _root.transform.localScale = Vector3.one;
        for (int f = 0; f < 250; f++) { Note(1f); Tick(); yield return null; }
        Vector3[] settled = Snapshot(1f);

        float react = 0f;
        yield return Reaction(1f, r => react = r);

        Say(string.Format(CultureInfo.InvariantCulture,
            "{0,-24} |    {1:F4} / {2:F4}    |     {3:F4} / {4:F4}    |   {5:F5} / {6:F5}   |   {7:F5} / {8:F5}   |   {9:F4}    | {10:F5} | {11,3}",
            tag, Max(growInc), Mean(growInc), Max(shrinkInc), Mean(shrinkInc),
            Mean(growJit), Max(growJit), Mean(shrinkJit), Max(shrinkJit),
            Incoherence(settled), react, _t.Cooks + _t.LiveCooks));

        string slug = tag.Replace(' ', '-').Replace('(', '_').Replace(')', '_').Replace('=', '-');
        Render($"thr-{slug}-worst-shrink", worstShrink);
        Render($"thr-{slug}-settled", settled);
        if (_t.Events.Count > 0 && _period > 0)
            Say("    trace: " + string.Join("  ", _t.Events.GetRange(0, Mathf.Min(14, _t.Events.Count))));
    }

    /// <summary>Does the cape still move when the body does? Zero for a rigid follower however big
    /// the swing, so it is the column that answers "inklusive der Reaktion" rather than "intakt".</summary>
    private IEnumerator Reaction(float scale, Action<float> done)
    {
        Vector3[] rest = Snapshot(scale);
        float peak = 0f;
        for (int f = 0; f < 60; f++)
        {
            _root.transform.position = new Vector3(0.25f * Mathf.Sin(f * 0.35f), 0f, 0f) * scale;
            Note(scale);
            Tick();
            yield return null;
            Vector3[] now = Snapshot(scale);
            float d = 0f;
            int n = Mathf.Min(now.Length, rest.Length);
            for (int i = 0; i < n; i++) { float m = (now[i] - rest[i]).magnitude; if (m > d) d = m; }
            if (d > peak) peak = d;
        }
        _root.transform.position = Vector3.zero;
        for (int f = 0; f < 60; f++) { Note(scale); Tick(); yield return null; }
        done(peak);
    }

    // ---- FigureCloth, transcribed, plus the throttle -------------------------------------------

    private void Note(float factor)
    {
        if (Mathf.Abs(factor - _t.Factor) > _t.Factor * FactorEpsilon)
        {
            _t.Factor = factor;
            _t.QuietFrames = 0;
            if (!_t.Suspended) _t.Suspended = true;
        }
    }

    private void Tick()
    {
        _frame++;
        if (_t.Suspended && _t.QuietFrames >= SettleFrames) Release();
        Advance();
        _t.QuietFrames++;
    }

    private void Release()
    {
        float factor = _t.Factor;
        if (Mathf.Abs(factor - 1f) <= FactorEpsilon) factor = 1f;
        _t.SeededFactor = factor;
        _t.Suspended = false;
        if (NeedsCook(factor))
        {
            _t.Cooking = true; _t.CookIndex = 0; _t.CookDown = false;
            _t.CookCount = _t.Cloths.Count; _t.Dirty = false;
        }
        else _t.Dirty = true;
        // Any cloth left mid throttled cook has to be brought back up before the settle sequence
        // takes over, or it stays a plain skinned mesh for the life of the figure.
        for (int i = 0; i < _t.Cloths.Count; i++) FinishLive(i);
    }

    private void Advance()
    {
        if (_t.Cooking)
        {
            if (!_t.Suspended) { StepCook(); return; }
            if (!_new) { StepCook(); return; }
            AbortCook();
        }

        float target = _t.Suspended ? 0f : 1f;
        if (_t.Weight != target)
        {
            float step = Time.unscaledDeltaTime / FadeSeconds;
            float before = _t.Weight;
            _t.Weight = Mathf.MoveTowards(_t.Weight, target, step);
            if (_t.Weight != before) _t.Dirty = true;
        }

        // ---- THE THROTTLE ----------------------------------------------------------------
        // While the size is moving, hand each cloth in turn a re-cook at the CURRENT size instead
        // of pinning it. The cloths are phase-offset by two frames each so no two of them are ever
        // down on the same frame -- FigureGrab.ClothCooks' `worst frame` must stay 1.
        if (_period > 0 && _t.Suspended)
        {
            for (int i = 0; i < _t.Cloths.Count; i++)
            {
                if (_t.LiveDown[i]) { FinishLive(i); continue; }
                if ((_frame - 2 * i) % _period != 0) continue;
                if (!NeedsCookAt(i, _t.Factor)) continue;
                StartLive(i);
            }
            _t.Dirty = false;   // the throttle owns the coefficient array while it runs
            return;
        }

        if (_new) ApplyStretch(_t.Weight);

        if (!_t.Dirty) return;
        _t.Dirty = false;
        for (int i = 0; i < _t.Cloths.Count; i++)
        {
            if (_t.Cloths[i] == null || !BuildInto(i, _t.Weight, _t.SeededFactor)) continue;
            _t.Cloths[i].coefficients = _t.Scratch[i];
            _t.Seeds++;
        }
    }

    /// <summary>Frame one of a throttled cook: full coefficients at the size the body is at RIGHT
    /// NOW, the authored stiffness back (the fabric must be baked at what will enforce it), and the
    /// component down.</summary>
    private void StartLive(int i)
    {
        Cloth c = _t.Cloths[i];
        if (c == null) return;
        if (BuildInto(i, 1f, _t.Factor)) { c.coefficients = _t.Scratch[i]; _t.Seeds++; }
        c.stretchingStiffness = _t.PristineStretch[i];
        _t.StretchWeight = -1f;
        c.enabled = false;
        _t.LiveDown[i] = true;
        if (_t.Events.Count < 40) _t.Events.Add($"f{_frame} live-down c{i}@{_t.Factor:0.00}");
    }

    /// <summary>Frame two: the enable, which is the cook.</summary>
    private void FinishLive(int i)
    {
        if (!_t.LiveDown[i]) return;
        Cloth c = _t.Cloths[i];
        _t.LiveDown[i] = false;
        if (c == null) return;
        c.enabled = true;
        c.ClearTransformMotion();
        _t.CookedFactor[i] = _t.Factor;
        _t.LiveCooks++;
        if (_t.Events.Count < 40) _t.Events.Add($"f{_frame} live-up c{i}");
    }

    private void StepCook()
    {
        if (_t.CookIndex >= _t.CookCount || _t.CookIndex >= _t.Cloths.Count)
        {
            _t.Cooking = false; _t.Weight = 1f; _t.Dirty = false;
            return;
        }
        Cloth c = _t.Cloths[_t.CookIndex];
        if (_new && !_t.CookDown && !NeedsCookAt(_t.CookIndex, _t.SeededFactor))
        { _t.CookIndex++; return; }
        if (!_t.CookDown)
        {
            if (BuildInto(_t.CookIndex, 1f, _t.SeededFactor)) { c.coefficients = _t.Scratch[_t.CookIndex]; _t.Seeds++; }
            if (_new) { c.stretchingStiffness = _t.PristineStretch[_t.CookIndex]; _t.StretchWeight = -1f; }
            c.enabled = false;
            _t.CookDown = true;
            return;
        }
        c.enabled = true;
        _t.Cooks++;
        c.ClearTransformMotion();
        _t.CookedFactor[_t.CookIndex] = _t.SeededFactor;
        _t.CookDown = false;
        _t.CookIndex++;
    }

    private bool NeedsCook(float factor)
    {
        for (int i = 0; i < _t.Cloths.Count; i++) if (NeedsCookAt(i, factor)) return true;
        return false;
    }

    private bool NeedsCookAt(int i, float factor)
    {
        if (i < 0 || i >= _t.CookedFactor.Count) return true;
        float cooked = _t.CookedFactor[i];
        return Mathf.Abs(factor - cooked) > cooked * FactorEpsilon;
    }

    private void AbortCook()
    {
        if (_t.CookDown && _t.CookIndex < _t.Cloths.Count)
        {
            Cloth c = _t.Cloths[_t.CookIndex];
            if (c != null && !c.enabled)
            {
                c.enabled = true; _t.Cooks++; c.ClearTransformMotion();
                _t.CookedFactor[_t.CookIndex] = _t.SeededFactor;
            }
        }
        _t.Cooking = false; _t.CookDown = false; _t.Dirty = true; _t.Aborts++;
    }

    private void ApplyStretch(float weight)
    {
        if (_t.StretchWeight == weight) return;
        _t.StretchWeight = weight;
        for (int i = 0; i < _t.Cloths.Count; i++)
        {
            Cloth c = _t.Cloths[i];
            if (c == null) continue;
            c.stretchingStiffness = weight >= 1f ? _t.PristineStretch[i] : _t.PristineStretch[i] * weight;
        }
    }

    private bool BuildInto(int i, float weight, float factor)
    {
        ClothSkinningCoefficient[] p = _t.Pristine[i], next = _t.Scratch[i];
        if (p == null || next == null || p.Length == 0 || next.Length != p.Length) return false;
        float bound = _t.RampBound[i], floor = bound * PinFloorFraction;
        if (weight >= 1f)
            for (int v = 0; v < p.Length; v++)
            {
                float m = p[v].maxDistance, s = p[v].collisionSphereDistance;
                next[v].maxDistance = m >= float.MaxValue ? m : m * factor;
                next[v].collisionSphereDistance = s >= float.MaxValue ? s : s * factor;
            }
        else if (weight <= 0f)
            for (int v = 0; v < p.Length; v++) { next[v].maxDistance = floor; next[v].collisionSphereDistance = floor; }
        else
        {
            float k = factor * weight, ramped = Mathf.Max(bound * weight, floor);
            for (int v = 0; v < p.Length; v++)
            {
                float m = p[v].maxDistance, s = p[v].collisionSphereDistance;
                float mv = m >= float.MaxValue ? ramped : m * k;
                float sv = s >= float.MaxValue ? ramped : s * k;
                next[v].maxDistance = mv > floor ? mv : floor;
                next[v].collisionSphereDistance = sv > floor ? sv : floor;
            }
        }
        return true;
    }

    // ---- metrics ------------------------------------------------------------------------------

    private Vector3[] Snapshot(float scale)
    {
        Cloth c = _t.Cloths[0];
        Vector3[] v;
        if (c != null && c.enabled) v = c.vertices;
        else { if (_bake == null) _bake = new Mesh(); _smrs[0].BakeMesh(_bake); v = _bake.vertices; }
        var o = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) o[i] = v[i] / scale;
        return o;
    }

    private static float MeanStep(Vector3[] a, Vector3[] b)
    {
        if (a == null || b == null) return 0f;
        int n = Mathf.Min(a.Length, b.Length);
        if (n == 0) return 0f;
        double s = 0d;
        for (int i = 0; i < n; i++) s += (a[i] - b[i]).magnitude;
        return (float)(s / n);
    }

    private static float Max(List<float> v) { float m = 0f; for (int i = 0; i < v.Count; i++) if (v[i] > m) m = v[i]; return m; }
    private static float Mean(List<float> v) { double s = 0d; for (int i = 0; i < v.Count; i++) s += v[i]; return v.Count == 0 ? 0f : (float)(s / v.Count); }

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

    private void Build()
    {
        _root = new GameObject("figureroot");
        _smrs.Clear();
        for (int k = 0; k < Cloths; k++)
        {
            var verts = new Vector3[Grid * Grid];
            var normals = new Vector3[Grid * Grid];
            var weights = new BoneWeight[Grid * Grid];
            // BYTE-FOR-BYTE GestureBench's MESH — same vertices, same winding, same index format.
            // This bench's whole claim is a comparison against the 289 and 290 state machines that
            // GestureBench measured, and two benches on two different sheets cannot be cited in the
            // same table. (Both the first cut of this bench and of FactorArmsBench built a vertical
            // curtain instead of the horizontal flag the ModBuild 290 tables were run on, and the
            // 1.345 rows disagreed with the published numbers for that reason alone.)
            for (int z = 0; z < Grid; z++)
            for (int x = 0; x < Grid; x++)
            {
                int i = z * Grid + x;
                verts[i] = new Vector3(x / (float)(Grid - 1) - 0.5f, 0f, -z / (float)(Grid - 1));
                normals[i] = Vector3.up;
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            }
            var tris = new List<int>((Grid - 1) * (Grid - 1) * 6);
            for (int z = 0; z < Grid - 1; z++)
            for (int x = 0; x < Grid - 1; x++)
            {
                int i = z * Grid + x;
                tris.Add(i); tris.Add(i + Grid); tris.Add(i + 1);
                tris.Add(i + 1); tris.Add(i + Grid); tris.Add(i + Grid + 1);
            }
            var mesh = new Mesh { name = "cape" + k, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.triangles = tris.ToArray();
            mesh.boneWeights = weights;
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.RecalculateBounds();

            var go = new GameObject("cape" + k);
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = new Vector3(k * 1.4f, 0f, 0f);
            var bone = new GameObject("bone" + k);
            bone.transform.SetParent(go.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = new[] { bone.transform };
            smr.rootBone = bone.transform;
            smr.updateWhenOffscreen = true;
            _smrs.Add(smr);
        }
    }
}
