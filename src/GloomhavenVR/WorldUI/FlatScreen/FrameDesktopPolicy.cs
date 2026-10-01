namespace GloomhavenVR.WorldUI;

/// <summary>
/// Desktop work follows the same live setting on every platform. The maintainer's
/// 2026-10-01 ruling supersedes Build 594's forced Frame mirror: standalone Frame is
/// a defaults profile, not a second runtime policy that ignores the player's choice.
/// The native 2D menu still renders into FlatScreen's in-headset capture texture.
/// </summary>
internal static class FrameDesktopPolicy
{
    internal static bool MirrorLeftEye(bool configured, bool frameStandalone, bool vrRunning) =>
        configured;

    internal static bool ScrubGameCameras(bool mirrorLeftEye, bool vrRunning, bool flatScreenVisible) =>
        mirrorLeftEye && vrRunning && !flatScreenVisible;
}
