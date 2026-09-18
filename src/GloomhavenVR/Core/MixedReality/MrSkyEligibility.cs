using System;

namespace GloomhavenVR.Core;

/// <summary>Native map furniture must survive MR before the initial map seat is ready.</summary>
internal static class MrSkyEligibility
{
    internal static bool IsMapFurniture(string name) =>
        name.StartsWith("GH_Map_Table", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("GH_Map_Bench", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("GH_Map_Barrel", StringComparison.OrdinalIgnoreCase);
}
