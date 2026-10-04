using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace GloomhavenVR.Core;

/// <summary>
/// Short Debug-only timing capture of selected native game callbacks. The Build 598 Steam Frame
/// large-scenario trace spends ~48 ms in Update and ~22 ms in LateUpdate on sampled frames, but
/// Unity's release player exposes none of the ScriptRunBehaviour profiler markers. The scene
/// census counts callback instances; it cannot tell which callback consumed that time. These
/// Harmony hooks time existing methods without changing arguments, return values or game state.
///
/// A name below is the DECLARING type, not necessarily the concrete component. For example,
/// ProceduralWall inherits ProceduralTileObserver.Update and WorldspacePanelUIController inherits
/// WorldspaceDisplayPanelBase.LateUpdate. The total for a target includes anything it calls,
/// including possible mod patches. It is not exclusive native cost; do not add these timings to
/// each other or subtract them from the full Update/LateUpdate span. Unity engine work and every
/// method not named below remain unmeasured. The next headset log decides whether any candidate
/// is actually expensive. A source count or this patch alone cannot establish a frame gain.
///
/// Harmony's small wrapper remains on the named methods after the 120-frame sample, but the
/// timing body then costs only a bool branch. Patches are installed only once, only after Debug
/// logging and [Perf] FrameSplit are active, and Plugin.OnDestroy removes them with UnpatchSelf.
/// Setup cost is measured and reported; instrumentation's in-call overhead is not isolated.
/// </summary>
internal static class PerfNativeLoopProbe
{
    private sealed class Target
    {
        internal Target(string typeName, string methodName)
        {
            TypeName = typeName;
            MethodName = methodName;
        }

        internal readonly string TypeName;
        internal readonly string MethodName;
        internal string Fault = "not installed";
        internal bool Patched;
        internal long Ticks;
        internal long WorstTicks;
        internal int Calls;

        internal string Label => TypeName + "." + MethodName;
    }

    // These callbacks exist in the game's shipped assemblies. They are sampled because the
    // Build 598 SIM census lists their component families in a large scenario, or because their
    // single scheduler callback can hide many coroutine/tween continuations. The last six are
    // small-count callback candidates omitted by Build 599; unavailable methods report n/a.
    private static readonly Target[] Targets =
    {
        new("MEC.Timing", "Update"),
        new("MEC.Timing", "LateUpdate"),
        new("LeanTween", "Update"),
        new("ProceduralTileObserver", "Update"),
        new("WorldspaceDisplayPanelBase", "LateUpdate"),
        new("ActorBehaviour", "Update"),
        new("ActorBehaviour", "LateUpdate"),
        new("ExtendedButton", "Update"),
        new("ThreadSyncService", "Update"),
        new("Choreographer", "Update"),
        new("PlatformLayer", "Update"),
        new("WorldspaceUITools", "Update"),
        new("ApparanceEngine", "Update"),
        new("ApparanceResources", "Update"),
        new("Updater", "Update"),
    };

    private static readonly Dictionary<MethodBase, Target> ByMethod = new(Targets.Length);
    private static readonly StringBuilder Sb = new(1200);
    private struct ProbeState
    {
        internal long Started;
        internal int Capture;
    }

    private static bool _installed;
    private static bool _sampling;
    private static int _captureGeneration;
    private static int _sampleFrames;
    private static double _setupMs;
    private static double _bodyUsPerCall;

    internal static void Start()
    {
        if (!VRLog.WantsDebug || VRSession.Harmony == null)
            return;

        if (!_installed)
            Install();
        foreach (Target target in Targets)
        {
            target.Ticks = 0;
            target.WorstTicks = 0;
            target.Calls = 0;
        }
        _sampleFrames = 0;
        _captureGeneration++;
        _sampling = true;
    }

    internal static void Stop(int frames)
    {
        _sampling = false;
        _sampleFrames = frames;
    }

    internal static void Shutdown()
    {
        _sampling = false;
        _sampleFrames = 0;
        // The shared plugin Harmony removes these patches during Plugin.OnDestroy. Clear the
        // mapping here so a hot reload cannot retain references to old game methods.
        ByMethod.Clear();
        foreach (Target target in Targets)
        {
            target.Patched = false;
            target.Fault = "not installed";
        }
        _installed = false;
    }

    internal static void LogSummary(int currentFrames)
    {
        int frames = currentFrames > 0 ? currentFrames : _sampleFrames;
        if (!VRLog.WantsDebug || frames == 0)
            return;
        Sb.Length = 0;
        Sb.Append("NATIVE first ").Append(frames)
          .Append(" frame(s) of this summary window, selected inclusive game/third-party callbacks "
                  + "(Debug; mean per sampled "
                  + "frame, calls, worst single call; nested values must not be added)");
        foreach (Target target in Targets)
        {
            Sb.Append(" | ").Append(target.Label).Append(' ');
            if (!target.Patched)
            {
                Sb.Append("n/a (").Append(target.Fault).Append(')');
                continue;
            }
            double ticksToMs = 1000d / Stopwatch.Frequency;
            Sb.Append((target.Ticks * ticksToMs / frames).ToString("F3"))
              .Append("ms/frame, ").Append(target.Calls).Append(" call(s), max ")
              .Append((target.WorstTicks * ticksToMs).ToString("F3")).Append("ms");
        }
        int totalCalls = 0;
        foreach (Target target in Targets)
            totalCalls += target.Calls;
        Sb.Append(" | measured probe body ").Append(_bodyUsPerCall.ToString("F2"))
          .Append("us/call (empty 2048-call calibration), estimated ")
          .Append((totalCalls * _bodyUsPerCall / frames / 1000d).ToString("F3"))
          .Append("ms/frame at this call count; Harmony dispatch excluded")
          .Append(" | setup ").Append(_setupMs.ToString("F2"))
          .Append("ms once. Unnamed game/engine callbacks "
                  + "and all Unity internals remain in the SPLIT logic span.");
        // The disk listener in the supplied Build 599 run omitted LogDebug even though the
        // matching Unity Player.log retained it. Keep the Debug guard above, but use the same
        // durable Info sink as FRAME/SPLIT for the maintainer's Debug capture.
        VRLog.Info("Perf", Sb.ToString());
    }

    private static void Install()
    {
        _installed = true;
        long started = Stopwatch.GetTimestamp();
        MethodInfo? prefix = typeof(PerfNativeLoopProbe).GetMethod(nameof(Prefix),
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        MethodInfo? postfix = typeof(PerfNativeLoopProbe).GetMethod(nameof(Postfix),
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (prefix == null || postfix == null)
            return;
        foreach (Target target in Targets)
        {
            try
            {
                Type? type = AccessTools.TypeByName(target.TypeName);
                if (type == null)
                {
                    target.Fault = "type unavailable";
                    continue;
                }
                MethodInfo? method = type.GetMethod(target.MethodName,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method == null)
                {
                    target.Fault = "method unavailable";
                    continue;
                }
                VRSession.Harmony!.Patch(method, prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix));
                ByMethod.Add(method, target);
                target.Patched = true;
                target.Fault = string.Empty;
            }
            catch (Exception e)
            {
                target.Fault = e.GetType().Name;
            }
        }
        // Calibrate the exact two managed hook bodies without executing a game method. This is
        // intentionally a small one-time sample; the live call count turns it into a per-frame
        // estimate. Harmony dispatch and interactions with other patches remain unisolated.
        foreach (MethodBase method in ByMethod.Keys)
        {
            const int trials = 2048;
            _sampling = true;
            long bodyStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < trials; i++)
            {
                ProbeState state = default;
                Prefix(ref state);
                Postfix(method, state);
            }
            _bodyUsPerCall = (Stopwatch.GetTimestamp() - bodyStart)
                             * 1_000_000d / Stopwatch.Frequency / trials;
            _sampling = false;
            break;
        }
        _setupMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
    }

    private static void Prefix(ref ProbeState __state)
    {
        // This hook is installed only on the explicit target methods. Looking the method
        // up here would add work to every call merely to confirm the registration we made once.
        if (!_sampling)
            return;
        __state.Capture = _captureGeneration;
        __state.Started = Stopwatch.GetTimestamp();
    }

    private static void Postfix(MethodBase __originalMethod, ProbeState __state)
    {
        // A settings change can close and restart a window while this very callback is still
        // executing. Do not charge its old prefix timestamp to the new capture.
        if (__state.Started == 0L || __state.Capture != _captureGeneration
            || !ByMethod.TryGetValue(__originalMethod, out Target target))
            return;
        long ticks = Stopwatch.GetTimestamp() - __state.Started;
        target.Ticks += ticks;
        if (ticks > target.WorstTicks)
            target.WorstTicks = ticks;
        target.Calls++;
    }
}
