using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MenuDepthCharacterBoundsCost729
{
    private delegate void Project(Bounds bounds, Matrix4x4 projection, ref bool any, ref float x0, ref float x1, ref float y0, ref float y1);
    private static int _checks;
    private static void Check(bool value, string text) { _checks++; if (!value) throw new Exception(text); }
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static int BoundsCost729(string output)
    {
        _checks = 0;
        Type type = typeof(MenuDepthCharacterSource).GetNestedType("BoundsClipper", BindingFlags.NonPublic)!;
        object clipper = Activator.CreateInstance(type, true)!;
        Project project = (Project)Delegate.CreateDelegate(typeof(Project), clipper, type.GetMethod("Encapsulate", Private)!);
        MethodInfo configure = type.GetMethod("Configure", Private)!;
        var camera = new GameObject("explicit orthographic viewport input").AddComponent<Camera>(); camera.enabled = false;
        Matrix4x4 ortho = Matrix4x4.Ortho(-2, 2, -2, 2, .1f, 10);
        Action<Vector3, Quaternion> setup = (offset, rotation) =>
        {
            Matrix4x4 native = Matrix4x4.Translate(-offset);
            var view = new MenuDepthCharacterProjection.View(offset, Quaternion.identity, native, ortho,
                .1f, 10f, false, native, new Vector4(0, 0, 1, -offset.z));
            configure.Invoke(clipper, new object[] { view,
                new MenuDepthEye(false, Vector3.zero, camera, worldToHeadClip: ortho * Matrix4x4.Rotate(Quaternion.Inverse(rotation))),
                Matrix4x4.identity, false });
        };
        setup(Vector3.zero, Quaternion.identity);
        Vector3[] originalA = (Vector3[])type.GetField("_a", Private)!.GetValue(clipper)!;
        Vector3[] originalB = (Vector3[])type.GetField("_b", Private)!.GetValue(clipper)!;
        Vector3 sentinel = new Vector3(12345, 23456, 34567); originalA[0] = sentinel;
        Bounds inside = new Bounds(new Vector3(0, 0, -2), Vector3.one);
        Bounds outside = new Bounds(new Vector3(80, 0, -2), Vector3.one);
        Bounds partial = new Bounds(new Vector3(1.9f, 0, -2), Vector3.one);
        Probe(project, inside, ortho, out bool any, out Rect bounds);
        Check(any && Mathf.Abs(bounds.xMin + .25f) < 1e-6f && Mathf.Abs(bounds.xMax - .25f) < 1e-6f,
            "fully admitted bounds retain the independently specified original corner projection");
        Check(originalA[0] == sentinel,
            "fully visible native bounds use the eight-corner fast path without polygon scratch");
        Probe(project, outside, ortho, out any, out _);
        Check(!any && originalA[0] == sentinel, "wholly outside native bounds reject before polygon clipping");
        Probe(project, partial, ortho, out any, out bounds);
        Check(any && Mathf.Abs(bounds.xMin - .7f) < 1e-5f && Mathf.Abs(bounds.xMax - 1f) < 1e-5f,
            "only the intersecting actual viewport plane clips the native volume");
        Check(((Vector3[])type.GetField("_a", Private)!.GetValue(clipper)!)[0] != sentinel,
            "the boundary cost control exercises real polygon clipping scratch");

        long fast = Timed(project, inside, ortho), clipped = Timed(project, partial, ortho);
        File.WriteAllText(Path.Combine(output, "warm-bounds-cost.json"),
            "{\"measuredCallsPerPath\":300000,\"fastNanosecondsPerCall\":" + fast + ",\"clippedNanosecondsPerCall\":" + clipped
            + ",\"fastPathUsesPolygonScratch\":false,\"outsideUsesPolygonScratch\":false,\"intersectingUsesPolygonScratch\":true}\n");
        // Large native-camera placement changes float interpolation precision.
        // The original world-space scene is translated, not the physical panel.
        // Clip several planes through the volume and check every projected result
        // against the independent head viewport, including fully-contained caps.
        Vector3 translation = new Vector3(12000, -10000, 16000);
        Quaternion rotation = Quaternion.Euler(11, 23, -7); setup(translation, rotation);
        Matrix4x4 actualHead = ortho * Matrix4x4.Rotate(Quaternion.Inverse(rotation)) * Matrix4x4.Translate(-translation);
        for (int sample = 0; sample < 4096; sample++)
        {
            float phase = sample * .017f;
            Bounds translated = new Bounds(translation + new Vector3(Mathf.Sin(phase) * 2.1f, Mathf.Cos(phase * 1.7f) * 2.1f, -2),
                new Vector3(1.4f, 1.7f, 2.3f));
            Probe(project, translated, actualHead, out any, out bounds);
            Check(!any || (Finite(bounds.xMin) && Finite(bounds.xMax) && Finite(bounds.yMin) && Finite(bounds.yMax)
                && bounds.xMin >= -1.004f && bounds.xMax <= 1.004f && bounds.yMin >= -1.004f && bounds.yMax <= 1.004f),
                "translated native volumes retain finite actual-frustum bounds without cap storage overflow");
        }
        Vector3[] afterA = (Vector3[])type.GetField("_a", Private)!.GetValue(clipper)!;
        Vector3[] afterB = (Vector3[])type.GetField("_b", Private)!.GetValue(clipper)!;
        Check((ReferenceEquals(afterA, originalA) && ReferenceEquals(afterB, originalB))
            || (ReferenceEquals(afterA, originalB) && ReferenceEquals(afterB, originalA)),
            "warm native bounds reuse the same two fixed scratch arrays across every query");
        Object.DestroyImmediate(camera.gameObject); return _checks;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Probe(Project project, Bounds bounds, Matrix4x4 projection, out bool any, out Rect result)
    {
        any = false; float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        project(bounds, projection, ref any, ref x0, ref x1, ref y0, ref y1);
        result = any ? Rect.MinMaxRect(x0, y0, x1, y1) : default;
    }
    private static long Timed(Project project, Bounds bounds, Matrix4x4 projection)
    {
        for (int i = 0; i < 1000; i++) Probe(project, bounds, projection, out _, out _);
        long best = long.MaxValue;
        for (int repeat = 0; repeat < 6; repeat++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 50000; i++) Probe(project, bounds, projection, out _, out _);
            long elapsed = Stopwatch.GetTimestamp() - start; best = Math.Min(best, elapsed);
        }
        return (long)(best * 1000000000.0 / Stopwatch.Frequency / 50000.0);
    }
}
