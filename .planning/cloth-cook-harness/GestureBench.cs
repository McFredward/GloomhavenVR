// THE THING ITSELF: ModBuild 289's state machine against ModBuild 290's, over a whole gesture.
//
// Run with --bench=gesture.
//
// A settled-state table is worst-case for one question and best-case for the next. What the user is
// complaining about is not a settled state at all -- "Sobald ich loslasse ist alles wieder ok" -- it
// is the MOVING window, so the moving window is what this bench samples, every frame, on THREE
// cloths, because the stagger that leaves an already-cooked cloth unpinned needs more than one.
//
// The script is his gesture: grow 1 -> 1.345, PAUSE long enough to settle (which starts a re-cook),
// then shrink 1.345 -> 1 starting while that re-cook is still running -- which is exactly what he
// does in figure_scale_problem.mp4 -- then release.
//
// The metric is normal INCOHERENCE (mean 1 - dot between neighbouring quad normals), sampled every
// frame of the moving window. A smooth cape sits near 0; a surface folded back through itself does
// not. Reported as the worst frame and the mean over the window, per direction, plus a rendered
// wireframe of the worst frame of each arm -- the complaint is visual and a distance statistic
// cannot see "Polygon-Matsch".
//
// NOTE ON WALL CLOCK. A cook here is ~11 ms; on his rig it is 25-165 ms. The frame COUNT of the
// unpinned window is the same either way, but the SECONDS he spends looking at it are three to
// fifteen times longer than the frame count suggests.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class GestureBench : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        bool want = false;
        foreach (string a in Environment.GetCommandLineArgs()) if (a == "--bench=gesture") want = true;
        if (!want) return;
        var go = new GameObject("GestureBench");
        DontDestroyOnLoad(go);
        go.AddComponent<GestureBench>();
    }

    private const int Grid = 29;
    private const int Cloths = 3;             // his figure carries three
    private const float SlackMax = 0.35f;
    private const float Big = 1.345f;
    private const float FactorEpsilon = 0.002f;
    private const int SettleFrames = 12;
    private const float FadeSeconds = 0.12f;
    private const float PinFloorFraction = 1e-3f;

    private readonly List<string> _out = new List<string>();
    private void Say(string s) => _out.Add(s);

    // ---- the transcribed state machine ------------------------------------------------------

    private sealed class Tracked
    {
        public readonly List<Cloth> Cloths = new List<Cloth>();
        public readonly List<ClothSkinningCoefficient[]> Pristine = new List<ClothSkinningCoefficient[]>();
        public readonly List<ClothSkinningCoefficient[]> Scratch = new List<ClothSkinningCoefficient[]>();
        public readonly List<float> RampBound = new List<float>();
        public readonly List<float> PristineStretch = new List<float>();
        public readonly List<float> CookedFactor = new List<float>();   // 290: PER CLOTH
        public float LegacyCookedFactor = 1f;                          // 289: one per figure
        public float Factor = 1f, SeededFactor = 1f, Weight = 1f, StretchWeight = 1f;
        public int QuietFrames;
        public bool Suspended, Dirty, Cooking, CookDown;
        public int CookIndex, CookCount;
        public int Cooks, Aborts, Seeds;
        public readonly List<string> Events = new List<string>();
    }

    private bool _new;                        // true = ModBuild 290, false = ModBuild 289
    private int _frame;                       // gesture frame number, for the event trace
    private Tracked _t = null!;
    private GameObject _root = null!;
    private readonly List<SkinnedMeshRenderer> _smrs = new List<SkinnedMeshRenderer>();
    private Vector3[] _authored = Array.Empty<Vector3>();
    private Mesh _bake = null!;

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;
        Say($"Unity {Application.unityVersion} | {Cloths} cloths of {Grid * Grid} vertices each | "
            + "incoherence = mean (1 - dot) over neighbouring quad normals, sampled EVERY FRAME of "
            + "the moving window, on cloth 0 (the first the stagger cooks and therefore the one "
            + "left unpinned longest)");

        // TWO SCRIPTS, because they exercise different halves of the change, and the second one has
        // to be TIMED rather than guessed, and getting there cost two runs.
        //
        // Script A (grow 45, pause 20) lets the whole stagger finish before the shrink begins, so
        // only the pin-versus-fabric fight is under test.
        //
        // Script B (grow 30, pause 14) puts the shrink INSIDE the stagger, which is the only way to
        // reach the abort path. Both halves of it are load-bearing:
        //   * the grow must be SHORT. Note's relative epsilon is measured against the last ACCEPTED
        //     factor, so while the gesture is moving fast the un-accepted difference crosses it
        //     again every two or three frames and QuietFrames never reaches 12. But an exponential
        //     approach flattens: by frame ~37 of a 45-frame grow the per-frame change is under
        //     1e-4 relative, QuietFrames climbs uninterrupted, and the settle fires DURING the grow
        //     -- so a 45-frame grow has already cooked before any pause starts.
        //   * a "no pause at all" script does not work either, for the opposite reason: nothing
        //     ever settles, nothing ever cooks, the fabric stays correct for scale 1, and the arm
        //     reads a flawless 0.0000 that says nothing about anything.
        foreach (var script in new[] { new { Grow = 45, Pause = 20 }, new { Grow = 30, Pause = 14 } })
        foreach (bool isNew in new[] { false, true })
        {
            _new = isNew;
            yield return RunGesture(script.Grow, script.Pause);
        }

        Debug.Log("[GST] ==================== RESULTS ====================");
        for (int i = 0; i < _out.Count; i++) Debug.Log("[GST] " + _out[i]);
        Debug.Log("[GST] ==================== END ====================");
        Application.Quit(0);
    }

    private IEnumerator RunGesture(int growFrames, int pauseFrames)
    {
        string tag = (_new ? "290 NEW" : "289 OLD") + (pauseFrames >= 20 ? "  [long grow + pause: the stagger finishes first]" : "  [short grow + pause: the shrink starts MID-STAGGER]");
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
        }
        for (int i = 0; i < 120; i++) yield return null;   // settle at scale 1
        _frame = 0;

        var grow = new List<float>();
        var shrink = new List<float>();
        Vector3[] worstGrow = null, worstShrink = null;
        float worstGrowV = -1f, worstShrinkV = -1f;

        // ---- phase 1: grow, then pause long enough to settle and start a re-cook -------------
        float smoothed = 1f;
        for (int f = 0; f < growFrames; f++)
        {
            smoothed = Mathf.Lerp(smoothed, Big, 0.12f);
            _root.transform.localScale = Vector3.one * smoothed;
            Note(smoothed);
            Tick();
            yield return null;
            Vector3[] s = Snapshot(smoothed);
            float inc = Incoherence(s);
            grow.Add(inc);
            if (inc > worstGrowV) { worstGrowV = inc; worstGrow = s; }
        }
        // The pause, if this script has one.
        for (int f = 0; f < pauseFrames; f++) { Note(smoothed); Tick(); yield return null; }

        // ---- phase 2: THE SHRINK, begun while the re-cook is still in flight -----------------
        for (int f = 0; f < 45; f++)
        {
            smoothed = Mathf.Lerp(smoothed, 1f, 0.12f);
            _root.transform.localScale = Vector3.one * smoothed;
            Note(smoothed);
            Tick();
            yield return null;
            Vector3[] s = Snapshot(smoothed);
            float inc = Incoherence(s);
            shrink.Add(inc);
            if (inc > worstShrinkV) { worstShrinkV = inc; worstShrink = s; }
        }

        // ---- phase 3: release, and let everything finish ------------------------------------
        _root.transform.localScale = Vector3.one;
        for (int f = 0; f < 200; f++) { Note(1f); Tick(); yield return null; }
        Vector3[] settled = Snapshot(1f);

        Say("");
        Say($"=== {tag} ===============================================================");
        Say($"    GROW   incoherence: worst {Max(grow):F4}  mean {Mean(grow):F4}   over {grow.Count} frames");
        Say($"    SHRINK incoherence: worst {Max(shrink):F4}  mean {Mean(shrink):F4}   over {shrink.Count} frames");
        Say($"    SETTLED after release: incoherence {Incoherence(settled):F4}   sag {MinY(settled):F5}");
        Say($"    cooks {_t.Cooks}   aborts {_t.Aborts}   coefficient uploads {_t.Seeds}");
        Say("    trace: " + string.Join("  ", _t.Events));
        string slug = (_new ? "290" : "289") + (pauseFrames >= 20 ? "-pause" : "-midcook");
        Render($"gesture-{slug}-worst-grow", worstGrow);
        Render($"gesture-{slug}-worst-shrink", worstShrink);
        Render($"gesture-{slug}-settled", settled);
    }

    // ---- FigureCloth, transcribed ------------------------------------------------------------

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
        bool owed = _new ? NeedsCook(factor)
                         : Mathf.Abs(factor - _t.LegacyCookedFactor) > _t.LegacyCookedFactor * FactorEpsilon;
        if (owed)
        {
            _t.Cooking = true; _t.CookIndex = 0; _t.CookDown = false;
            _t.CookCount = _t.Cloths.Count; _t.Dirty = false;
            _t.Events.Add($"f{_frame} settle@{factor:0.000} COOK");
        }
        else { _t.Dirty = true; _t.Events.Add($"f{_frame} settle@{factor:0.000} nocook"); }
    }

    private void Advance()
    {
        if (_t.Cooking)
        {
            if (!_t.Suspended) { StepCook(); return; }
            if (!_new) { StepCook(); return; }   // ModBuild 289: the ramp never ran during a cook
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

    private void StepCook()
    {
        if (_t.CookIndex >= _t.CookCount || _t.CookIndex >= _t.Cloths.Count)
        {
            _t.Cooking = false; _t.LegacyCookedFactor = _t.SeededFactor; _t.Weight = 1f; _t.Dirty = false;
            return;
        }
        Cloth c = _t.Cloths[_t.CookIndex];
        if (_new && !_t.CookDown && !NeedsCookAt(_t.CookIndex, _t.SeededFactor))
        { _t.Events.Add($"f{_frame} skip c{_t.CookIndex}"); _t.CookIndex++; return; }
        if (!_t.CookDown)
        {
            if (BuildInto(_t.CookIndex, 1f, _t.SeededFactor)) { c.coefficients = _t.Scratch[_t.CookIndex]; _t.Seeds++; }
            if (_new)
            {
                c.stretchingStiffness = _t.PristineStretch[_t.CookIndex];
                _t.StretchWeight = -1f;
            }
            c.enabled = false;
            _t.CookDown = true;
            return;
        }
        c.enabled = true;
        _t.Cooks++;
        _t.Events.Add($"f{_frame} cook c{_t.CookIndex}@{_t.SeededFactor:0.000}");
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
                // Logged, because a cook that happens inside an abort is still a cook and an
                // instrument that hides it makes the totals stop reconciling with the trace.
                _t.Events.Add($"f{_frame} cook c{_t.CookIndex}@{_t.SeededFactor:0.000} (abort must re-enable it)");
            }
        }
        _t.Cooking = false; _t.CookDown = false; _t.Dirty = true; _t.Aborts++;
        _t.Events.Add($"f{_frame} ABORT at c{_t.CookIndex}");
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

    // ---- metrics and scaffolding -------------------------------------------------------------

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

    private static float Max(List<float> v) { float m = 0f; for (int i = 0; i < v.Count; i++) if (v[i] > m) m = v[i]; return m; }
    private static float Mean(List<float> v) { double s = 0d; for (int i = 0; i < v.Count; i++) s += v[i]; return v.Count == 0 ? 0f : (float)(s / v.Count); }

    private static float MinY(Vector3[] v) { float m = 0f; for (int i = 0; i < v.Length; i++) if (v[i].y < m) m = v[i].y; return m; }

    private float Incoherence(Vector3[] v)
    {
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
            for (int z = 0; z < Grid; z++)
            for (int x = 0; x < Grid; x++)
            {
                int i = z * Grid + x;
                verts[i] = new Vector3(x / (float)(Grid - 1) - 0.5f, 0f, -z / (float)(Grid - 1));
                normals[i] = Vector3.up;
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            }
            if (k == 0) _authored = verts;
            var tris = new List<int>();
            for (int z = 0; z < Grid - 1; z++)
            for (int x = 0; x < Grid - 1; x++)
            {
                int i = z * Grid + x;
                tris.Add(i); tris.Add(i + Grid); tris.Add(i + 1);
                tris.Add(i + 1); tris.Add(i + Grid); tris.Add(i + Grid + 1);
            }
            var mesh = new Mesh { name = "cape" + k, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.normals = normals; mesh.triangles = tris.ToArray();
            mesh.boneWeights = weights; mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.RecalculateBounds();

            var go = new GameObject("cape" + k);
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = new Vector3(k * 2f, 0f, 0f);
            var bone = new GameObject("bone");
            bone.transform.SetParent(go.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh; smr.bones = new[] { bone.transform }; smr.rootBone = bone.transform;
            smr.updateWhenOffscreen = true;
            _smrs.Add(smr);
        }
    }
}
