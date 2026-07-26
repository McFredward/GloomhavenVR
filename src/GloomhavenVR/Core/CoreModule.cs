using System.Runtime.CompilerServices;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// XR bootstrap: runtime-dependency loading, OpenXR init (pre-flight, runtime
/// failover, diagnostics) and clean teardown. Sets <see cref="VRSession.IsRunning"/>
/// which every other module keys off. While VR runs it also hosts the
/// <see cref="VRHeartbeat"/> on the mod's own hardened root GO (freeze diagnosis
/// net — hardware test #4).
/// </summary>
internal sealed class CoreModule : IVRModule
{
    public string Name => "Core";

    /// <summary>True once RuntimeDeps are loaded — gate for JITing XR-typed methods.</summary>
    private static bool _depsLoaded;

    private GameObject? _hostGo;

    public void Init()
    {
        // Localization helper: subscribe to the engine's language-change event and fan out
        // to Loc.OnChanged so all mod-authored VR text follows the game's language live.
        // Runs regardless of VR availability (safe before I2 is loaded) so it is always
        // torn down cleanly on hot-reload.
        Loc.Init();

        // Order matters: nothing referencing Unity.XR.* types may be JIT-compiled
        // before LoadAll() has put those assemblies into the AppDomain. Init() itself
        // only *calls* the (non-inlined) methods that use them.
        if (!RuntimeDepsLoader.LoadAll())
        {
            VRLog.Warn(Name, "VR unavailable this session (RuntimeDeps missing) — game continues flat.");
            return;
        }

        _depsLoaded = true;
        StartVR();

        // Own hardened root (same pattern as every other driver host): the heartbeat and the
        // performance monitor must outlive scene sweeps and never ride a game-visible GO.
        // Created UNCONDITIONALLY (it used to be gated on VRSession.IsRunning) because the
        // performance instrumentation is worth having on the flat screen too — a session that
        // never reached VR is exactly the one whose frame times we want in the log.
        _hostGo = new GameObject("GloomhavenVR.Core");
        Object.DontDestroyOnLoad(_hostGo);
        _hostGo.hideFlags = HideFlags.HideAndDontSave;

        // Frame-pacing instrumentation (2026-07 perf pass). Cheap and self-disabling: with
        // [Perf] Enabled = false its host costs one bool test per frame and nothing else.
        PerfMonitor.Install(_hostGo);
        VRLog.Info(Name, "Performance monitor installed — grep the log for '[Perf] FRAME' "
                         + "(pacing/GC/XR summary), '[Perf] STEPS' (mod subsystems ranked by cost) "
                         + "and '[Perf] SPIKE' (individual over-budget frames). Configure in "
                         + "dev.gloomhavenvr.perf.cfg or under Einstellungen › Leistung.");

        if (VRSession.IsRunning)
        {
            _hostGo.AddComponent<VRHeartbeat>();
            _hostGo.AddComponent<VRPresenceWatch>();
            VRLog.Info(Name, "Heartbeat installed — one [Core] status line every 10 s " +
                             "(frames, rig driver, head pose, display/input subsystems, tracking). " +
                             "Presence watch installed — HMD doff/don raises the session-resume " +
                             "recovery sweep (test #17).");
        }
    }

    public void Shutdown()
    {
        Loc.Dispose(); // detach the engine localization event (hot-reload teardown)
        PerfMonitor.Shutdown(); // drop the sampling host + every step record before the GO dies

        if (_hostGo != null)
        {
            Object.Destroy(_hostGo);
            _hostGo = null;
        }
        if (_depsLoaded)
            StopVR();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StartVR()
    {
        string? runtimeOverride = Plugin.RuntimeOverride.Value;
        if (string.IsNullOrWhiteSpace(runtimeOverride))
            runtimeOverride = null;

        if (!OpenXRBootstrap.Start(runtimeOverride, Plugin.RuntimePriority.Value, Plugin.SkipRuntimeCandidates.Value))
            VRLog.Warn(Name, "VR unavailable this session — game continues flat.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StopVR() => OpenXRBootstrap.Stop();
}
