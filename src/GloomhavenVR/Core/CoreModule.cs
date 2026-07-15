namespace GloomhavenVR.Core;

/// <summary>
/// XR bootstrap, runtime-dependency loading, config plumbing, diagnostics.
/// Phase 1 (feat/xr-bootstrap) implements: RuntimeDeps Assembly.LoadFile,
/// OpenXR pre-flight check, XR Management init, runtime failover.
/// </summary>
internal sealed class CoreModule : IVRModule
{
    public string Name => "Core";

    /// <summary>
    /// Compile-time proof that the publicized game references resolved:
    /// <c>InitiativeOption</c> is an <c>internal class</c> in GH.Runtime and
    /// <c>initiativeIncrease</c> is one of its <c>private</c> fields — both are only
    /// visible because BepInEx.AssemblyPublicizer rewrote the reference assembly.
    /// nameof() is compile-time only, so this adds no runtime dependency.
    /// </summary>
    private const string PublicizerProbe = nameof(InitiativeOption) + "." + nameof(InitiativeOption.initiativeIncrease);

    public void Init()
    {
        VRLog.Debug(Name, $"stub initialized (Phase 1 implements XR bootstrap); publicizer probe OK: {PublicizerProbe}");
    }
}
