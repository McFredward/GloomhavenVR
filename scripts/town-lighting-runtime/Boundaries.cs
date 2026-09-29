using UnityEngine;
namespace GloomhavenVR.Core
{
    internal static class VRLayers { internal const int ModLayer = 27; }
    internal static class VRLog
    {
        internal static int Warnings;
        internal static void Warn(string area, string message) { Warnings++; Debug.LogWarning(area + ": " + message); }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class TownLightingEditorLifetime
    {
        static readonly System.Collections.Generic.List<Object> Pending = new System.Collections.Generic.List<Object>();
        internal static void Destroy(Object value) => Pending.Add(value);
        internal static void Flush()
        { foreach (Object value in Pending) if (value != null) Object.DestroyImmediate(value); Pending.Clear(); }
    }
    internal static class SkyAlternative
    {
        internal static bool Moon;
        internal static bool TryRoomMoonDirection(out Vector3 direction, out Color colour, out float intensity)
        { direction = new Vector3(.493f,.643f,.587f); colour = new Color(.70f,.79f,.94f); intensity = .4f; return Moon; }
    }
}
