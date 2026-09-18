using System;
using GloomhavenVR.Core;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string message)
    {
        _assertions++;
        if (!value) throw new Exception(message);
    }

    public static void Main()
    {
        foreach (string name in new[] { "GloomhavenVR.MrBacking", "GloomhavenVR.MrUnseenRim",
                     "GloomhavenVR.MrUnseenFill", "GloomhavenVR.MrUnseenUnderlay", "VRFigureGhost", "VROverlay" })
            Check(ModVisualOwnership.IsName(name), "mod visual excluded from native scenery: " + name);
        foreach (string name in new[] { "Simple Tile", "FR_Floor_LargeBush_06", "Wall 1", "EN_Unseen_FloorHex_Edge_Damage_03_PR", "", "Gloomhaven", "NativeVRRock" })
            Check(!ModVisualOwnership.IsName(name), "native scenery ownership retained: " + name);
        foreach (string name in new[] { "Simple Tile", "Simple Tile(Clone)" })
        {
            Check(MrUnseenRegionEligibility.Allows(name, false), "native fog cliff retained");
            Check(!MrUnseenRegionEligibility.Allows(name, true), "cutout silhouettes never filled");
        }
        foreach (string name in new[] { "FR_Floor_LargeBush_06", "FR_Floor_PlantsBushes_02 (1)",
                     "FR_Cliff_01", "GloomhavenVR.MrBacking", "VRFigureGhost", "Simple Tile Extra", "" })
        {
            Check(!MrUnseenRegionEligibility.Allows(name, false), "overlap does not imply fog ownership: " + name);
            Check(!MrUnseenRegionEligibility.Allows(name, true), "cutout neighboring foliage stays authored: " + name);
        }
        Console.WriteLine($"MR scenario ownership: {_assertions} production assertions passed.");
    }
}

namespace GloomhavenVR.Core
{
    internal static class VRLayers
    {
        internal const string ModOwnedNamePrefix = "VR";
        internal const string ModOwnedQualifiedPrefix = "GloomhavenVR.";
    }
}
