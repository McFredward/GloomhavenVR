namespace GloomhavenVR.WorldUI;

/// <summary>
/// The desktop setting controls the spectator image only: left eye or black.
/// The maintainer's 2026-10-02 clarification requires discarded native desktop
/// drawing to be suppressed independently on every platform. Native 2D menus
/// still render into FlatScreen's in-headset capture texture.
/// </summary>
internal static class FrameDesktopPolicy
{
    internal static bool MirrorLeftEye(bool configured, bool frameStandalone, bool vrRunning) =>
        configured;

    internal static bool ScrubGameCameras(bool mirrorLeftEye, bool vrRunning, bool flatScreenVisible) =>
        vrRunning && !flatScreenVisible;
}
