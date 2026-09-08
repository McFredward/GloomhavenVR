namespace GloomhavenVR.Net;

/// <summary>The original slot-local glow bases converted into board metres. The recess anchor
/// is already in board space and is added by the caller without scaling it again.</summary>
internal static class RemoteSlotGlowDepth
{
    internal static float Wanted(float ownerOverlayZ, float slotScale) =>
        (-0.004f + ownerOverlayZ) * slotScale;

    internal static float Snap(float ownerOverlayZ, float slotScale) =>
        (-0.006f + ownerOverlayZ) * slotScale;
}
