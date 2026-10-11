using System;
using System.Text;
using GloomhavenVR.Core;
using Unity.Profiling;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// Build 598's large Frame scenario had 5,786 submitted material slots in the scene census,
/// but that is not a draw-call count: Unity may instance or batch them, and the census cannot
/// observe either outcome. The head camera spent about 23 ms/frame in its submit span with
/// depthTextureMode=None, while the game's scenario camera cost under 1 ms. Before changing
/// runtime-generated map meshes or their fade/LOD ownership, read Unity's actual rendering
/// counters in the same scenario. These are scene-wide counters (including UI and all cameras),
/// not head-camera counters and not GPU busy time; their availability is player-dependent.
/// The existing [Perf] SPLIT line supplies the per-camera CPU time for the same window.
///
/// This optional probe runs only at Debug in a scenario, captures at most the first 120 frames
/// of each 20-second window, then disposes its recorders until the next window. Early windows
/// may include loading and must not be compared to steady gameplay. The ordinary player log and
/// every rendering decision are untouched.
/// </summary>
internal static class HeadRenderCounters
{
    private const float WindowSeconds = 20f;
    private const int CaptureFrames = 120;

    private sealed class Counter
    {
        internal Counter(string name) => Name = name;
        internal readonly string Name;
        internal ProfilerRecorder Recorder;
        internal bool Started;
        internal bool Valid;
        internal string Fault = string.Empty;
        internal readonly long[] Samples = new long[CaptureFrames];
        internal int Count;
        internal int PositiveCount;

        internal void Start()
        {
            Fault = string.Empty;
            try
            {
                // Graphics counters may be produced on the render thread. Do not restrict
                // collection to the current thread as the native logic probe does.
                Recorder = new ProfilerRecorder(Name, 1,
                    ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.StartImmediately);
                Started = true;
                Valid = Recorder.Valid;
            }
            catch (Exception e)
            {
                Fault = e.GetType().Name;
            }
        }

        internal void Sample()
        {
            if (!Valid || Count >= CaptureFrames)
                return;
            try
            {
                if (Recorder.Count == 0)
                    return;
                long value = Recorder.LastValue;
                Samples[Count++] = value;
                if (value > 0)
                    PositiveCount++;
            }
            catch (Exception e)
            {
                Valid = false;
                Fault = e.GetType().Name;
            }
        }

        internal void Append(StringBuilder sb)
        {
            sb.Append(Name).Append(' ');
            if (!Valid || Count == 0 || PositiveCount == 0)
            {
                sb.Append("n/a (").Append(Fault.Length != 0 ? Fault :
                    !Valid ? "counter unavailable in player" :
                    Count == 0 ? "no samples" : "only zero samples; counter not trusted")
                  .Append(')');
                return;
            }
            Array.Sort(Samples, 0, Count);
            double sum = 0d;
            for (int i = 0; i < Count; i++)
                sum += Samples[i];
            sb.Append("mean=").Append((sum / Count).ToString("F0"))
              .Append(" p50=").Append(Samples[(Count - 1) / 2])
              .Append(" p95=").Append(Samples[(int)Math.Ceiling(Count * 0.95) - 1])
              .Append(" max=").Append(Samples[Count - 1])
              .Append(" n=").Append(Count);
            if (PositiveCount != Count)
                sb.Append(" (zero samples ").Append(Count - PositiveCount).Append(')');
        }

        internal void ResetSamples()
        {
            Count = 0;
            PositiveCount = 0;
        }

        internal void Stop()
        {
            Pause();
            Fault = string.Empty;
            ResetSamples();
        }

        internal void Pause()
        {
            if (Started)
            {
                try { Recorder.Dispose(); }
                catch (Exception) { /* Optional diagnostic; do not interrupt rig teardown. */ }
            }
            Started = false;
        }
    }

    private static readonly Counter[] Counters =
    {
        new("Draw Calls Count"),
        new("Batches Count"),
        new("SetPass Calls Count"),
        new("Triangles Count"),
    };

    private static bool _active;
    private static float _windowStart;
    private static int _startFrame;
    private static int _frames;
    private static bool _unavailable;

    internal static void Tick(bool scenario)
    {
        if (!scenario || !VRLog.WantsDebug)
        {
            Stop();
            return;
        }
        if (!_active)
        {
            for (int i = 0; i < Counters.Length; i++)
                Counters[i].Start();
            _active = true;
            _windowStart = Time.realtimeSinceStartup;
            _startFrame = Time.frameCount;
            _frames = 0;
            return;
        }
        if (_unavailable)
            return;

        // Update reads the most recently completed frame, before this frame's camera renders.
        // The startup frame has no complete sample from this scenario yet.
        if (Time.frameCount > _startFrame)
        {
            _frames++;
            if (_frames <= CaptureFrames)
            {
                for (int i = 0; i < Counters.Length; i++)
                    Counters[i].Sample();
                if (_frames == CaptureFrames)
                    for (int i = 0; i < Counters.Length; i++)
                        Counters[i].Pause();
            }
        }
        float now = Time.realtimeSinceStartup;
        if (now - _windowStart < WindowSeconds)
            return;

        var sb = new StringBuilder(400);
        sb.Append("HEAD RENDER COUNTERS ").Append((now - _windowStart).ToString("F1"))
          .Append("s, first ").Append(Math.Min(_frames, CaptureFrames))
          .Append(" of ").Append(_frames).Append(" completed frame(s) captured: ");
        bool anyPositive = false;
        for (int i = 0; i < Counters.Length; i++)
        {
            if (i != 0)
                sb.Append(" | ");
            Counters[i].Append(sb);
            anyPositive |= Counters[i].PositiveCount > 0;
        }
        sb.Append(" | Unity scene-wide counters include every camera and UI. Compare matching "
                + "scenario/view windows with [Perf] SPLIT's head cull+submit and [Perf] FRAME; "
                + "material slots are not draw calls and these counters are not GPU busy time.");
        VRLog.Info("Rig", sb.ToString());
        // A stripped player should produce one explicit n/a line, then remain quiet for this
        // scenario. Re-entering a scenario or changing log level resets the latch in Stop().
        _unavailable = !anyPositive;
        for (int i = 0; i < Counters.Length; i++)
        {
            Counters[i].Stop();
            if (!_unavailable)
                Counters[i].Start();
        }
        _windowStart = now;
        _startFrame = Time.frameCount;
        _frames = 0;
    }

    internal static void Stop()
    {
        if (!_active)
            return;
        for (int i = 0; i < Counters.Length; i++)
            Counters[i].Stop();
        _active = false;
        _frames = 0;
        _unavailable = false;
    }
}
