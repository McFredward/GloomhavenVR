namespace GloomhavenVR.Core;

// Rendering is outside this native-close fixture. Model the active diagnostic output so the
// unchanged production Open method must restore it synchronously before native Show executes.
internal static class SteamLinkMaskRuntime
{
    internal static bool OutputRunning;
    internal static void NotifyOptionsOpening() => OutputRunning = false;
}
