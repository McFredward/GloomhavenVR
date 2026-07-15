namespace GloomhavenVR.Core;

/// <summary>
/// Minimal lifecycle contract for GloomhavenVR feature modules.
/// Modules are registered in <see cref="Plugin"/> and initialized in order once,
/// after config is bound and only when VR is enabled.
/// Later phases will extend this (e.g. Shutdown for ScriptEngine hot-reload,
/// per-VR-mode enable/disable) — keep the surface frozen until then (ROADMAP:
/// shared surfaces change only via orchestrator after Phase 2).
/// </summary>
internal interface IVRModule
{
    /// <summary>Human-readable module name used for logging/diagnostics.</summary>
    string Name { get; }

    /// <summary>One-time initialization, called from <see cref="Plugin.Awake"/>.</summary>
    void Init();
}
