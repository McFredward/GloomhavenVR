using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static bool WantsDebug => false;
        internal static void Debug(string category, string message) { }
    }
}

namespace GloomhavenVR.Hands
{
    internal sealed class ProbeRig
    {
        internal Transform PalmCenter = null!;
        internal Transform IndexTip = null!;
    }

    internal sealed class VRHand
    {
        internal bool HasPose;
        internal float WorldScale;
        internal ProbeRig Rig = null!;
    }

    internal static class VRHands
    {
        internal static VRHand? Left;
        internal static VRHand? Right;
    }
}

namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver
    {
        internal static Transform? RigRoot;
        internal static float BaseWorldScale;
        internal static Camera? HeadCamera;
    }
}

namespace GloomhavenVR.Net
{
    internal struct TownClothRunnerState
    {
        internal Vector2 Left;
        internal Vector2 Right;
        internal Vector2 LeftVelocity;
        internal Vector2 RightVelocity;
    }

    internal static class NetAvatarDriver
    {
        internal static void CollectTownFacePeers(List<int> peers) { }
        internal static bool TryGetTownClothHandProbes(int peer, out Vector3 left,
            out Vector3 leftDirection, out Vector3 right, out Vector3 rightDirection,
            out bool leftValid, out bool rightValid)
        {
            left = leftDirection = right = rightDirection = default;
            leftValid = rightValid = false;
            return false;
        }

        internal static bool TryGetTownFaceHead(int peer, out Vector3 head)
        {
            head = default;
            return false;
        }
    }
}
