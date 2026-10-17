using System;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Core.SteamLink;

/// <summary>
/// Cold connection admission, independent of MR preference. SteamVR alone is
/// insufficient: Virtual Desktop can also use that OpenXR runtime. The isolated
/// reader must identify the connected HMD and its vrlink transport first.
/// </summary>
internal static class SteamLinkConnectionLifecycle
{
    private static bool _watching;
    private static Camera? _head;
    private static InputDevice _headDevice;
    private static bool _hasHeadDevice;

    internal static void Tick()
    {
        bool eligible = VRSession.IsRunning && !FrameNativePassthrough.Required
            && Application.platform == RuntimePlatform.WindowsPlayer
            && string.Equals(VRSession.RuntimeName, "SteamVR/OpenXR", StringComparison.OrdinalIgnoreCase);
        if (!eligible)
        {
            if (_watching) Reset();
            return;
        }
        if (!_watching)
        {
            InputDevices.deviceConnected += DeviceChanged;
            InputDevices.deviceDisconnected += DeviceChanged;
            InputDevices.deviceConfigChanged += DeviceChanged;
            _headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            _hasHeadDevice = _headDevice.isValid;
            _watching = true;
        }
        Camera? head = VRCameraPolicy.AllowedHead;
        if (_head != head)
        {
            _head = head;
            SteamLinkConnection.Invalidate();
        }
        SteamLinkConnection.Tick(head != null && head.isActiveAndEnabled, Time.unscaledTime);
    }

    private static void DeviceChanged(InputDevice device)
    {
        // Device removal can invalidate its characteristics; do not keep a
        // positive snapshot merely because the old HMD is no longer queryable.
        bool hmd = (device.characteristics & InputDeviceCharacteristics.HeadMounted) != 0;
        if (!hmd && (!_hasHeadDevice || !device.Equals(_headDevice))) return;
        if (hmd) { _headDevice = device; _hasHeadDevice = true; }
        SteamLinkConnection.Invalidate();
    }

    internal static void Reset()
    {
        if (_watching)
        {
            InputDevices.deviceConnected -= DeviceChanged;
            InputDevices.deviceDisconnected -= DeviceChanged;
            InputDevices.deviceConfigChanged -= DeviceChanged;
        }
        _watching = false;
        _head = null;
        _headDevice = default;
        _hasHeadDevice = false;
        SteamLinkConnection.Reset();
    }
}
