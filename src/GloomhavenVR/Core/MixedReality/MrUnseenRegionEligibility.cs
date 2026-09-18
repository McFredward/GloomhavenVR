using System;

namespace GloomhavenVR.Core;

/// <summary>The supplemental fog route exists for the native "Simple Tile" cliff block whose
/// shader does not identify itself as unseen. Spatial overlap alone never proves fog ownership:
/// revealed forest foliage and cliff meshes occupy the same band below adjacent unseen hexes.
/// A textureless copy fills cutout foliage rectangles; its generated rim adds solid box faces.
/// Keep the documented cliff kit, and leave every other native renderer authored.</summary>
internal static class MrUnseenRegionEligibility
{
    internal static bool Allows(string objectName, bool hasCutoutMaterial) =>
        !hasCutoutMaterial && (string.Equals(objectName, "Simple Tile", StringComparison.Ordinal)
            || string.Equals(objectName, "Simple Tile(Clone)", StringComparison.Ordinal));
}
