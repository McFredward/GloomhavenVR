using System;
using System.Diagnostics;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Core
{
    internal static partial class PerfMonitor
    {
        internal static void ResetMeasurement(bool active = true)
        {
            // The summary-window roll is a port. Initialize its unconsumed
            // fields explicitly instead of suppressing production warnings.
            foreach (Step step in StepOrder)
            { step.WindowSeconds = step.WindowWorstFrameSeconds = step.SpikeSeconds = 0d; step.WindowFrames = step.WindowCalls = 0; }
            Steps.Clear(); StepOrder.Clear(); _depth = 0; _frameModSeconds = 0d; StepsActive = active;
        }
        internal static double Seconds(string name) => Steps.TryGetValue(name, out Step step) ? step.FrameSeconds : 0d;
        internal static int Depth => _depth;
        internal static int ScopeCount => Steps.Count;
        internal static double Total => _frameModSeconds;
    }

    // Host/lifecycle input only. Real production camera callbacks, bounded stack,
    // completed-frame snapshot and Scope implementation are bound by run.py.
    internal static partial class PerfFrameSplit
    {
        private static bool _active, _logicEndSeen, _renderSeen;
        private static long _logicStart, _logicEnd, _renderFirst, _renderLast;
        private static int _logicFrame;
        internal static void Attach()
        {
            _active = true; _logicEndSeen = true; _renderSeen = false;
            _logicStart = _logicEnd = Stopwatch.GetTimestamp(); _logicFrame = Time.frameCount;
            ResetCameraPasses(Time.frameCount);
            foreach (CamRec camera in CameraOrder)
            { camera.WindowSeconds = camera.WindowCullSeconds = camera.WindowSubmitSeconds = 0d; camera.WindowPasses = camera.WindowFrames = 0; }
            Cameras.Clear(); CameraOrder.Clear();
            Camera.onPreCull += OnPreCull; Camera.onPreRender += OnPreRender; Camera.onPostRender += OnPostRender;
        }
        internal static void Detach()
        {
            Camera.onPreCull -= OnPreCull; Camera.onPreRender -= OnPreRender; Camera.onPostRender -= OnPostRender;
            _active = false;
        }
        internal static int Passes(Camera camera) => Cameras.TryGetValue(camera.GetInstanceID(), out CamRec rec) ? rec.FramePasses : 0;
        internal static double CameraSeconds(Camera camera) => Cameras.TryGetValue(camera.GetInstanceID(), out CamRec rec) ? rec.FrameSeconds : 0d;
        internal static int Faults => _cameraPassFaults;
        internal static int Peak => _cameraPassPeak;
        internal static int OpenPasses => _cameraPassDepth;
        internal static void ObservePost(bool on)
        { if (on) Camera.onPostRender += OnPostRender; else Camera.onPostRender -= OnPostRender; }
        internal static void StalePass(Camera camera) => OpenCameraPass(camera.GetInstanceID(), Time.frameCount - 1, Stopwatch.GetTimestamp());
        internal static void CloseObserved(Camera camera) => OnPostRender(camera);
        internal static string Snapshot(int reportingFrame)
        {
            CaptureCompletedFrame(true, Stopwatch.GetTimestamp(), reportingFrame);
            var text = new StringBuilder(); AppendSpike(text); return text.ToString();
        }
    }
}

public static partial class MenuDepthProgram
{
    private static void PerformanceDiagnostics746()
    {
        VRLog.WantsDebug = true;
        using (var scene = new Scene())
        {
            PerfMonitor.ResetMeasurement(); scene.Capture();
            Check(PerfMonitor.Seconds("MenuDepth.HeadPreCull") > 0d && PerfMonitor.Seconds("MenuDepth.HeadCoordinator") > 0d,
                "actual head pre-cull and coordinator capture are attributed before native render callbacks finish");
            Check(PerfMonitor.Seconds("MenuDepth.HeadPixels") > 0d && PerfMonitor.Seconds("MenuDepth.SourcePrepare") > 0d
                && PerfMonitor.Seconds("MenuDepth.HeadFilter") > 0d && PerfMonitor.Seconds("MenuDepth.HeadResolve") > 0d
                && PerfMonitor.Seconds("MenuDepth.CanvasRebuild") > 0d && PerfMonitor.Seconds("MenuDepth.UiRender") > 0d,
                "real source preparation, allocation, canvas rebuild, camera draw and resolve have distinct diagnostic brackets");
            using var debug = scene.Eye(false, "perf-debug-final-head");
            VRLog.WantsDebug = false; PerfMonitor.ResetMeasurement(); scene.Capture();
            using var normal = scene.Eye(false, "perf-normal-final-head");
            Check(PerfMonitor.ScopeCount == 0,
                "normal logging leaves the new render measurements completely inactive");
            Check(Difference(debug.Image, normal.Image, new RectInt(0, 0, normal.Image.width, normal.Image.height)) == 0,
                "diagnostic gates preserve all actual final head pixels at normal and Debug levels");
            VRLog.WantsDebug = true; PerfMonitor.ResetMeasurement(false); scene.Capture();
            Check(PerfMonitor.ScopeCount == 0, "disabled attribution leaves Debug render scopes inactive");
            PerfMonitor.ResetMeasurement(); scene.Source.ThrowRight = true; scene.Capture();
            Check(PerfMonitor.Depth == 0 && scene.Source.Begins == scene.Source.Ends && scene.Entry.MenuDepthDisplay == null,
                "a real second-eye source exception closes every diagnostic scope and restores native capture ownership");
        }
        PerfMonitor.ResetMeasurement();
        using (PerfMonitor.Scope("outer"))
        {
            using (PerfMonitor.Scope("inner")) BurnClock();
        }
        Check(Math.Abs(PerfMonitor.Total - PerfMonitor.Seconds("outer")) < 1e-9
            && PerfMonitor.Total < PerfMonitor.Seconds("outer") + PerfMonitor.Seconds("inner"),
            "nested native/render intervals contribute once to the outer measured total");
        foreach (bool timingFirst in new[] { true, false }) NestedCameraTiming746(timingFirst, false);
        NestedCameraTiming746(true, true);
        PerfMonitor.ResetMeasurement(); VRLog.WantsDebug = true;
    }

    private static void BurnClock()
    {
        long until = Stopwatch.GetTimestamp() + Math.Max(1L, Stopwatch.Frequency / 500L);
        while (Stopwatch.GetTimestamp() < until) { }
    }

    private static void NestedCameraTiming746(bool timingFirst, bool missingChildPost)
    {
        var parentGo = new GameObject("perf actual parent camera"); var parent = parentGo.AddComponent<Camera>();
        var childGo = new GameObject("perf actual nested camera"); var child = childGo.AddComponent<Camera>();
        var parentTarget = new RenderTexture(32, 32, 24); parentTarget.Create();
        var childTarget = new RenderTexture(32, 32, 24); childTarget.Create();
        foreach (Camera camera in new[] { parent, child })
        { camera.enabled = false; camera.cullingMask = 0; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.red; }
        parent.targetTexture = parentTarget; child.targetTexture = childTarget;
        int completedChildren = 0;
        Camera.CameraCallback nested = camera =>
        {
            if (!ReferenceEquals(camera, parent)) return;
            if (missingChildPost) PerfFrameSplit.ObservePost(false);
            try { child.Render(); completedChildren++; }
            finally { if (missingChildPost) PerfFrameSplit.ObservePost(true); }
        };
        Camera.CameraCallback clock = camera => { if (ReferenceEquals(camera, child)) BurnClock(); };
        try
        {
            if (timingFirst) { PerfFrameSplit.Attach(); Camera.onPreCull += nested; }
            else { Camera.onPreCull += nested; PerfFrameSplit.Attach(); }
            Camera.onPreRender += clock;
            parent.Render();
            Check(completedChildren == 1 && PerfFrameSplit.Passes(parent) == 1
                && PerfFrameSplit.Passes(child) == (missingChildPost ? 0 : 1),
                "real nested Camera.Render retains both parent and child callback intervals");
            Check(PerfFrameSplit.OpenPasses == 0, "actual parent post-render closes its bounded callback stack");
            if (missingChildPost)
                Check(PerfFrameSplit.Faults == 1,
                    "a missing child post-render is reported while the actual parent still completes");
            else if (timingFirst)
                Check(PerfFrameSplit.Peak == 2 && PerfFrameSplit.CameraSeconds(parent) >= PerfFrameSplit.CameraSeconds(child),
                    "parent callback timing includes the nested real camera and is not an additive independent cost");
            else
                Check(PerfFrameSplit.Peak == 1,
                    "a coordinator subscribed first completes its nested cameras before the parent timer begins");
            string completed = PerfFrameSplit.Snapshot(Time.frameCount + 1);
            Check(completed.Contains("completed frame " + Time.frameCount + " seams") && completed.Contains("inclusive and overlap"),
                "same-frame spike seams name inclusive camera provenance without adding parent and child intervals");
            Check(PerfFrameSplit.Snapshot(Time.frameCount + 2).Contains("n/a"),
                "a skipped reporting frame cannot relabel old seams as a completed frame");
            using var final = new TextureOwner(Read(parentTarget, "perf-nested-" + timingFirst + "-" + missingChildPost));
            Check(final.Image.GetPixel(16, 16).r > .9f && final.Image.GetPixel(16, 16).g < .1f,
                "observing nested camera callbacks preserves actual rendered head target pixels");
            PerfFrameSplit.StalePass(parent); PerfFrameSplit.CloseObserved(parent);
            Check(PerfFrameSplit.OpenPasses == 0 && PerfFrameSplit.Passes(parent) == 1,
                "a stale callback cannot turn a cross-frame interval into camera work");
        }
        finally
        {
            Camera.onPreCull -= nested; Camera.onPreRender -= clock; PerfFrameSplit.Detach();
            parent.targetTexture = child.targetTexture = null;
            parentTarget.Release(); childTarget.Release();
            Object.DestroyImmediate(parentTarget); Object.DestroyImmediate(childTarget);
            Object.DestroyImmediate(parentGo); Object.DestroyImmediate(childGo);
        }
    }
}
