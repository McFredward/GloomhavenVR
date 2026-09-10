using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>Arrival heading is solved from the actual clamped seat, never from a saved grab yaw.</summary>
internal static class BoardPlacementPose
{
    internal static Quaternion FacePlayer(Vector3 head, Vector3 board, Vector3 fallbackForward,
        float pitchDegrees)
        => Quaternion.LookRotation(Heading(head, board, fallbackForward), Vector3.up)
            * Quaternion.Euler(pitchDegrees, 0f, 0f);

    internal static Vector3 Heading(Vector3 head, Vector3 board, Vector3 fallbackForward)
    {
        Vector3 heading = board - head;
        heading.y = 0f;
        if (heading.sqrMagnitude < 1e-8f)
        {
            heading = fallbackForward;
            heading.y = 0f;
        }
        if (heading.sqrMagnitude < 1e-8f) heading = Vector3.forward;
        return heading.normalized;
    }
}
