using System;
using System.IO;

namespace GloomhavenVR;

/// <summary>
/// The Steam Frame installation leaves this marker beside the preloader. Only that installation
/// requires an explicit VR launch argument; ordinary PC installations retain their historical
/// startup behavior. The preloader and plugin compile this source independently so both can
/// reject the original Steam launch before touching XR, graphics settings, config or patches.
/// </summary>
internal static class FrameLaunchOptIn
{
    internal const string Argument = "--gloomhavenvr";
    internal const string MarkerFileName = "frame-launch-opt-in.marker";

    internal static string MarkerPath(string bepinexRoot) =>
        Path.Combine(bepinexRoot, "patchers", "GloomhavenVR", MarkerFileName);

    internal static bool MarkerExists(string bepinexRoot) => File.Exists(MarkerPath(bepinexRoot));

    internal static bool AllowsVr(bool markerExists, string[]? args)
    {
        if (!markerExists)
            return true;

        if (args == null)
            return false;

        // An exact argv token cannot accidentally match an unrelated launch option or path.
        foreach (string arg in args)
            if (string.Equals(arg, Argument, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    internal static bool AllowsCurrentLaunch(string bepinexRoot)
    {
        if (!MarkerExists(bepinexRoot))
            return true;

        try { return AllowsVr(markerExists: true, Environment.GetCommandLineArgs()); }
        catch { return false; } // A marked Frame install must never opt in by accident.
    }
}
