namespace GloomhavenVR.Core;

// The only mod boundary: VR activation and logging. Game actors, item predicates,
// ownership, inventories, token types, UIUseItemsBar and replay bodies are shipped.
internal static class VRSession { internal static bool IsRunning = true; }
internal static class VRLog
{
    internal static int Warnings;
    internal static bool ThrowWarnings;
    internal static string LastMessage = "";
    internal static void Warn(string area, string message)
    {
        Warnings++;
        LastMessage = message;
        if (ThrowWarnings)
            throw new System.InvalidOperationException("Unavailable log sink");
    }
}
