using System;

namespace GloomhavenVR.Core;

/// <summary>Both established mod naming conventions. "GloomhavenVR." does not start with "VR".
/// Native-layer MR geometry must not become a wall rider or enter the native scenery census.</summary>
internal static class ModVisualOwnership
{
    internal static bool IsName(string name) =>
        name.StartsWith(VRLayers.ModOwnedNamePrefix, StringComparison.Ordinal)
        || name.StartsWith(VRLayers.ModOwnedQualifiedPrefix, StringComparison.Ordinal);
}
