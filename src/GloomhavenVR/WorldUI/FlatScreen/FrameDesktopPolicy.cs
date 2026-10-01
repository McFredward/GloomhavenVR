namespace GloomhavenVR.WorldUI;

/// <summary>
/// Steam Frame's VR-only library entry has no useful desktop spectator view. Even if an
/// existing PC-style config disabled the left-eye mirror, its game cameras must not keep
/// drawing a second flat scenario and native UI to the SteamVR theater backbuffer.
/// The regular PC preference is unchanged, and the native 2D menu still renders into
/// FlatScreen's captured texture for the in-headset menu.
/// </summary>
internal static class FrameDesktopPolicy
{
    internal static bool MirrorLeftEye(bool configured, bool frameStandalone, bool vrRunning) =>
        configured || (frameStandalone && vrRunning);

    internal static bool ScrubGameCameras(bool mirrorLeftEye, bool vrRunning, bool flatScreenVisible) =>
        mirrorLeftEye && vrRunning && !flatScreenVisible;
}
