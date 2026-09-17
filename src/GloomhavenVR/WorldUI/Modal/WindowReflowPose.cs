using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static class WindowReflowPose
{
    // A converted host can have an off-centre native hit rect. Pack the measured rectangle, then
    // recover the frame origin; assigning the packed centre directly would preserve its offset.
    internal static Vector3 FramePositionForCentre(Vector3 framePosition, Quaternion frameRotation,
        Vector3 measuredCentre, Vector3 targetCentre, Quaternion targetRotation)
    {
        Vector3 centreOffset = Quaternion.Inverse(frameRotation) * (measuredCentre - framePosition);
        return targetCentre - targetRotation * centreOffset;
    }
}
