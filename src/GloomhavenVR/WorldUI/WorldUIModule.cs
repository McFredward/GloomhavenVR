using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Physicalized UI: canvas conversion to world space, physical buttons, initiative track,
/// element board, wrist HUD, floating 2D screen + virtual-mouse fallback.
/// Phase 3c (feat/world-ui) builds on the Phase-2 seed shipped here:
/// <see cref="VirtualMouse"/> (warp/click/drag on the game's own InputSystem virtual
/// mouse) plus a driver that services its deferred click releases.
/// </summary>
internal sealed class WorldUIModule : IVRModule
{
    public string Name => "WorldUI";

    private GameObject? _driverGo;

    public void Init()
    {
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — WorldUI driver not installed.");
            return;
        }

        _driverGo = new GameObject("GloomhavenVR.WorldUIDriver");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<WorldUIDriver>();

        VRLog.Info(Name, "WorldUI driver installed (virtual-mouse bridge available).");
    }

    public void Shutdown()
    {
        if (_driverGo != null)
        {
            Object.Destroy(_driverGo);
            _driverGo = null;
        }
        VirtualMouse.Reset();
    }

    /// <summary>Services the virtual-mouse bridge (deferred click releases).</summary>
    private sealed class WorldUIDriver : MonoBehaviour
    {
        private void Update() => VirtualMouse.Tick();
    }
}
