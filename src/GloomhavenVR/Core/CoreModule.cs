using System.Runtime.CompilerServices;

namespace GloomhavenVR.Core;

/// <summary>
/// XR bootstrap: runtime-dependency loading, OpenXR init (pre-flight, runtime
/// failover, diagnostics) and clean teardown. Sets <see cref="VRSession.IsRunning"/>
/// which every other module keys off.
/// </summary>
internal sealed class CoreModule : IVRModule
{
    public string Name => "Core";

    /// <summary>True once RuntimeDeps are loaded — gate for JITing XR-typed methods.</summary>
    private static bool _depsLoaded;

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
    }

    public void Shutdown()
    {
        if (_depsLoaded)
            StopVR();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StartVR()
    {
        string? runtimeOverride = Plugin.RuntimeOverride.Value;
        if (string.IsNullOrWhiteSpace(runtimeOverride))
            runtimeOverride = null;

        if (!OpenXRBootstrap.Start(runtimeOverride))
            VRLog.Warn(Name, "VR unavailable this session — game continues flat.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StopVR() => OpenXRBootstrap.Stop();
}
