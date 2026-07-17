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

        if (VRSession.IsRunning)
        {
            // Own hardened root (same pattern as every other driver host): the
            // heartbeat must outlive scene sweeps and never ride a game-visible GO.
            _hostGo = new GameObject("GloomhavenVR.Core");
            Object.DontDestroyOnLoad(_hostGo);
            _hostGo.hideFlags = HideFlags.HideAndDontSave;
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
