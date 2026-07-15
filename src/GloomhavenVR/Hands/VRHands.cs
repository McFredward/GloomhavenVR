using System;
using GloomhavenVR.Hands.Interact;

namespace GloomhavenVR.Hands;

/// <summary>
/// Static access to the two hands (FROZEN Phase-2 API).
///
/// Hands exist only while the Phase-1 rig exists (scenario loaded, VR running) or the
/// dev simulation is active — always null-check, or subscribe to <see cref="HandsChanged"/>.
/// All members main-thread only.
/// </summary>
internal static class VRHands
{
    /// <summary>The left hand, or null while hands are torn down.</summary>
    public static VRHand? Left { get; private set; }

    /// <summary>The right hand, or null while hands are torn down.</summary>
    public static VRHand? Right { get; private set; }

    /// <summary>True while both hands exist.</summary>
    public static bool Ready => Left != null && Right != null;

    /// <summary>Fired after hands are created and after they are destroyed.</summary>
    public static event Action? HandsChanged;

    public static VRHand? Get(HandSide side) => side == HandSide.Left ? Left : Right;

    /// <summary>
    /// The dominant hand per <c>[Hands] PrimaryHand</c> config (default right) —
    /// its ray is the default pick source for Phase-3a.
    /// </summary>
    public static VRHand? Primary =>
        string.Equals(Plugin.PrimaryHand.Value, "Left", StringComparison.OrdinalIgnoreCase) ? Left : Right;

    /// <summary>Convenience pick provider: the primary hand's ray (null while hands are down).</summary>
    public static IPickProvider? PrimaryPick => Primary?.Ray;

    internal static void Set(VRHand? left, VRHand? right)
    {
        Left = left;
        Right = right;
        try
        {
            HandsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Hands", $"HandsChanged subscriber threw: {ex}");
        }
    }
}
