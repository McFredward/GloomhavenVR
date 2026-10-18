using System;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Quest uses native passthrough and has no desktop mirror. The B619 hardware
/// review therefore excludes its key-colour and monitor-eye rows only in the
/// configured Quest player. Free-form component suppression is also inert on
/// Quest: the maintainer's B679 review forbids disabling essential game systems
/// through VR settings. Keep persisted entries/defaults in the live
/// catalog; desktop installs and newly added options retain the generic menu.
/// </summary>
internal static class QuestOptionVisibility
{
    internal static bool IsOffered(string section, string key, bool questStandalone) =>
        !questStandalone
        || !(string.Equals(section, "MixedReality", StringComparison.Ordinal)
             && string.Equals(key, "KeyColor", StringComparison.Ordinal))
           && !(string.Equals(section, "WorldUI", StringComparison.Ordinal)
                && string.Equals(key, "DesktopMirrorLeftEye", StringComparison.Ordinal))
           && !(string.Equals(section, "Compat", StringComparison.Ordinal)
                && string.Equals(key, "DisableComponents", StringComparison.Ordinal));
}
