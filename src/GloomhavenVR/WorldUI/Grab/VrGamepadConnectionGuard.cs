using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Steam Frame exposes an XInput controller to the flat game while OpenXR is running.
/// InControl then opens GamepadConnectionBox on the native ScreenSpaceOverlay canvas:
/// Build 593 Player.log shows the box opening in CardSelection immediately before
/// "Attached native device: XInput Controller". That canvas is absent from the VR
/// room but is painted into SteamVR's flat theater view. More seriously, the game's
/// GamepadConnectionBox.Update sends any controller press through
/// SelectInputDeviceBox.ReloadSceneWithChangeDevice(true) (or LoadMainMenu online).
/// VR deliberately uses mouse-mode game scenes and buttons, so neither the prompt nor
/// that scene-changing path has a valid VR action. Suppress the source, not a whole
/// overlay canvas that may carry actual game notifications or modal decisions.
/// </summary>
[HarmonyPatch(typeof(GamepadConnectionBox), nameof(GamepadConnectionBox.Activate))]
internal static class VrGamepadConnectionActivateGuard
{
    private static bool _noted;

    private static bool Prefix()
    {
        if (!VRSession.IsRunning)
            return true;

        if (!_noted)
        {
            _noted = true;
            VRLog.Info("WorldUI", "VR gamepad connection prompt suppressed; VR stays in mouse-mode UI.");
        }
        return false;
    }
}

/// <summary>
/// A connection coroutine may already have started before VR initialized. If its
/// window is still open, close it on the first VR Update and never let a late XInput
/// press reload the scene. The unchanged native Update runs in flat mode.
/// </summary>
[HarmonyPatch(typeof(GamepadConnectionBox), "Update")]
internal static class VrGamepadConnectionUpdateGuard
{
    private static bool Prefix(GamepadConnectionBox __instance)
    {
        if (!VRSession.IsRunning)
            return true;

        if (__instance._uiWindow != null && __instance._uiWindow.IsOpen)
            __instance.Deactivate();
        return false;
    }
}
