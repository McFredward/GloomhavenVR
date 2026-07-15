using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Physicalized UI: canvas conversion to world space, physical buttons, initiative track,
/// element board, wrist HUD, floating 2D screen + virtual-mouse fallback.
/// Phase 3c (feat/world-ui). Key seams: UIWindowManager, InputManager.CreateVirtualMouse.
/// </summary>
internal sealed class WorldUIModule : IVRModule
{
    public string Name => "WorldUI";

    public void Init() => VRLog.Debug(Name, "stub initialized (Phase 3c implements world-space UI).");
}
