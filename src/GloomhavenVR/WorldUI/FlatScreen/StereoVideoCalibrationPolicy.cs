using System.IO;

namespace GloomhavenVR.WorldUI;

/// <summary>The explicit, local-only entry contract for the first stereoscopic movie experiment.</summary>
internal static class StereoVideoCalibrationPolicy
{
    internal const int EyeWidth = 1920;
    internal const int Height = 1080;

    internal static bool Allowed(bool vr, bool conversion, string scene, bool online) =>
        vr && conversion && !online && (scene == "MainMenu" || scene == "MainMenu_gamepad");

    internal static string MoviePath(string pluginDirectory, bool stereo) =>
        Path.Combine(pluginDirectory, "Movies", "Calibration", stereo ? "calibration-sbs.mp4" : "calibration-mono.mp4");

    internal static bool ValidDimensions(int width, int height, bool stereo) =>
        width == EyeWidth * (stereo ? 2 : 1) && height == Height;
}
