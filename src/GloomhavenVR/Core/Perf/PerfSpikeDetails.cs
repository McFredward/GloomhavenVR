using System;
using System.Text;
using Unity.Profiling;

namespace GloomhavenVR.Core;

/// <summary>
/// Bounded Debug-only evidence for the completed frame, including hitches after the summary's
/// first 120 frames. Neither collection counts nor inclusive engine samples prove causality.
/// Missing release-player markers remain n/a; they must never be reported as zero GPU/CPU cost.
/// </summary>
internal static class PerfSpikeDetails
{
    private sealed class Marker
    {
        internal Marker(string name) => Name = name;
        internal readonly string Name;
        internal ProfilerRecorder Recorder;
        internal bool Created, Valid, HasSample;
        internal long Nanoseconds;
        internal string Fault = "unavailable in player";
    }

    // Fixed cardinality and capacity. Reset after every boundary excludes stale samples from
    // frames in which a sporadic marker did not run. Main-thread-only samples are inclusive;
    // waits are waits, not GPU busy time. No extra camera or profiler deep mode is enabled.
    private static readonly Marker[] Markers =
    {
        new("GC.Collect"), new("Canvas.BuildBatch"), new("Animator.Update"),
        new("Gfx.WaitForPresentOnGfxThread"), new("WaitForTargetFPS"),
    };
    private static bool _on, _seeded;
    private static int _g0, _g1, _g2, _d0, _d1, _d2;

    internal static void RollFrame(bool on)
    {
        on &= VRLog.WantsDebug;
        PerfNativeLoopProbe.RollSpikeFrame(on);
        if (on != _on)
        {
            _on = on;
            _seeded = false;
            foreach (Marker marker in Markers)
            {
                if (!on) Dispose(marker);
                else Create(marker);
            }
        }
        if (!on) return;
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        _d0 = _seeded ? g0 - _g0 : 0;
        _d1 = _seeded ? g1 - _g1 : 0;
        _d2 = _seeded ? g2 - _g2 : 0;
        _g0 = g0; _g1 = g1; _g2 = g2;
        _seeded = true;
        foreach (Marker marker in Markers)
        {
            marker.HasSample = false;
            if (!marker.Valid) continue;
            try
            {
                int count = marker.Recorder.Count;
                if (count > 0)
                {
                    marker.Nanoseconds = marker.Recorder.GetSample(count - 1).Value;
                    marker.HasSample = true;
                }
                marker.Recorder.Reset();
                // Unity 2021 Reset also stops the recorder; resume for the next real frame.
                marker.Recorder.Start();
            }
            catch (Exception error)
            {
                marker.Fault = error.GetType().Name;
                Dispose(marker);
            }
        }
    }

    internal static void Append(StringBuilder sb)
    {
        if (!_on || !VRLog.WantsDebug) return;
        sb.Append(" | GC collections since previous frame ").Append(_d0).Append('/')
            .Append(_d1).Append('/').Append(_d2).Append(" (counts, not pause duration)");
        PerfNativeLoopProbe.AppendSpike(sb);
        sb.Append(" | engine previous-frame markers (Debug, inclusive):");
        for (int i = 0; i < Markers.Length; i++)
        {
            Marker marker = Markers[i];
            sb.Append(i == 0 ? " " : ", ").Append(marker.Name).Append(' ');
            if (!marker.Valid) sb.Append("n/a (").Append(marker.Fault).Append(')');
            else if (!marker.HasSample) sb.Append("n/a (no sample in completed frame)");
            else sb.Append((marker.Nanoseconds / 1_000_000d).ToString("F3")).Append("ms");
        }
    }

    internal static void Shutdown() => RollFrame(false);

    private static void Create(Marker marker)
    {
        marker.HasSample = marker.Valid = false;
        marker.Fault = "unavailable in player";
        try
        {
            marker.Recorder = new ProfilerRecorder(marker.Name, 2,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                | ProfilerRecorderOptions.CollectOnlyOnCurrentThread
                | ProfilerRecorderOptions.StartImmediately);
            marker.Created = true;
            marker.Valid = marker.Recorder.Valid;
            if (marker.Valid && marker.Recorder.UnitType.ToString() != "TimeNanoseconds")
            {
                marker.Fault = "non-time unit";
                Dispose(marker);
            }
        }
        catch (Exception error)
        {
            marker.Fault = error.GetType().Name;
            Dispose(marker);
        }
    }

    private static void Dispose(Marker marker)
    {
        if (marker.Created)
        {
            try { marker.Recorder.Dispose(); }
            catch (Exception) { /* Diagnostic cleanup cannot interrupt gameplay. */ }
        }
        marker.Valid = marker.Created = marker.HasSample = false;
    }
}
