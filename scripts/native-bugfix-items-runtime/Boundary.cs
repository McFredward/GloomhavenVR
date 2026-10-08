namespace GloomhavenVR.Core;

// The only mod boundary: VR activation and logging. Game actors, item predicates,
// ownership, inventories, token types, UIUseItemsBar and replay bodies are shipped.
internal static class VRSession { internal static bool IsRunning = true; }
internal static class VRLog
{
    internal static int Warnings;
    internal static void Warn(string area, string message) { Warnings++; }
}
