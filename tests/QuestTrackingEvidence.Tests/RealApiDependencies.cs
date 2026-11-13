using UnityEngine;

// This compilation boundary supplies only other mod owners. Every Unity/XR type
// and member comes from the actual pinned Editor, never from TrackingStubs.cs.
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled => true; }
    internal enum VRLogLevel { Debug }
    internal static class VRLog { internal static bool Wants(VRLogLevel level) => true; }
}
namespace GloomhavenVR.Hands
{
    internal sealed class HandRig { internal Transform Root => null!; }
    internal sealed class VRHand
    {
        internal Transform transform => null!;
        internal bool IsTracked => true;
        internal HandRig Rig => null!;
    }
    internal static class VRHands
    {
        internal static VRHand? Left => null;
        internal static VRHand? Right => null;
    }
}
namespace GloomhavenVR.Rig
{
    internal sealed partial class VRRigDriver
    {
        private Camera? _camera => null;
        private GameObject? _rigRoot => null;
    }
}
