namespace GloomhavenVR.Core;

/// <summary>
/// Minimal lifecycle contract for GloomhavenVR feature modules.
/// Modules are registered in <see cref="Plugin"/> and initialized in order once,
/// after config is bound and only when VR is enabled.
/// Extended in Phase 1 with <see cref="Shutdown"/> for the ScriptEngine hot-reload
/// contract (TOOLCHAIN §3.3): undo everything on plugin OnDestroy. Keep the surface
/// frozen otherwise (ROADMAP: shared surfaces change only via orchestrator after Phase 2).
/// </summary>
internal interface IVRModule
{
    /// <summary>Human-readable module name used for logging/diagnostics.</summary>
    string Name { get; }

    /// <summary>One-time initialization, called from <see cref="Plugin.Awake"/>.</summary>
    void Init();

    /// <summary>
    /// Undo everything <see cref="Init"/> did (destroy GameObjects, stop subsystems, …).
    /// Called from <see cref="Plugin.OnDestroy"/> in reverse registration order.
    /// Harmony patches are removed collectively via <c>UnpatchSelf</c> afterwards.
    /// Must never throw.
    /// </summary>
    void Shutdown();
}
