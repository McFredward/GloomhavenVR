using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Shared spawn / re-place clamp for movable world panels (the in-VR settings panel
/// and the combat log). Given the head pose it either produces a fresh spawn pose or
/// HEALS an existing/persisted pose into one GUARANTEED to sit inside the player's
/// forward field of view at a comfortable reading distance:
///
/// - yaw clamped to ±<see cref="MaxYawDeg"/> around world up,
/// - pitch clamped to [<see cref="MinPitchDeg"/>, <see cref="MaxPitchDeg"/>] (biased
///   slightly downward for reading),
/// - distance clamped to [<see cref="MinDistance"/>, <see cref="MaxDistance"/>] real
///   meters × the diorama scale,
/// - upright (zero roll/pitch on the panel itself), facing the head.
///
/// Item 2 (test #24): panels sometimes spawned far outside the view — e.g. the combat
/// log stranded off to the side after a Mixed-Reality toggle re-derived it from a stale
/// persisted offset. Routing EVERY show / first-place / re-derive through this clamp
/// makes that impossible: a candidate already in view passes through unchanged (the
/// clamp is idempotent inside the cone), an out-of-view one is pulled back in front of
/// the head. uGUI fronts render along −forward, so the returned rotation points the
/// panel's +Z AWAY from the head (same convention as the surfaces' own FaceHead).
/// </summary>
internal static class PanelPlacement
{
    /// <summary>Comfortable reading distance for a fresh spawn (real meters × diorama scale).</summary>
    internal const float SpawnDistance = 0.75f;

    /// <summary>A fresh spawn sits this far below eye level (real meters × diorama scale).</summary>
    internal const float SpawnDrop = 0.12f;

    /// <summary>Nearest a panel may sit from the head after a heal (real meters).</summary>
    internal const float MinDistance = 0.45f;

    /// <summary>Farthest a panel may sit from the head after a heal (real meters).</summary>
    internal const float MaxDistance = 1.4f;

    /// <summary>Half-angle of the horizontal keep-in-view cone (degrees).</summary>
    internal const float MaxYawDeg = 35f;

    /// <summary>Bottom of the vertical keep-in-view band (degrees; − = below eye level).</summary>
    internal const float MinPitchDeg = -30f;

    /// <summary>Top of the vertical keep-in-view band (degrees; + = above eye level).</summary>
    internal const float MaxPitchDeg = 20f;

    /// <summary>A heal that moves the panel less than this (real meters) is treated as a no-op.</summary>
    internal const float HealEpsilon = 0.02f;

    /// <summary>
    /// Fresh spawn pose: a comfortable reading distance straight in front of the head,
    /// slightly below eye level, upright and facing the head.
    /// <paramref name="worldScale"/> is the diorama scale (1 outside a scenario).
    /// </summary>
    internal static void Spawn(Camera head, float worldScale,
        out Vector3 position, out Quaternion rotation)
    {
        Transform h = head.transform;
        Vector3 fwd = Flatten(h.forward);
        position = h.position + fwd * (SpawnDistance * worldScale)
                              - Vector3.up * (SpawnDrop * worldScale);
        rotation = Facing(position, h.position);
    }

    /// <summary>
    /// Heal a candidate world pose into the forward FOV. Returns true when the pose was
    /// actually relocated (the candidate was outside the yaw/pitch cone or the distance
    /// band — callers persist the healed pose); false when it was already comfortably in
    /// view and passes through unchanged. <paramref name="rotation"/> always comes back
    /// upright, facing the head.
    /// </summary>
    internal static bool ClampIntoView(Camera head, float worldScale,
        ref Vector3 position, out Quaternion rotation)
    {
        Transform h = head.transform;
        Vector3 headPos = h.position;
        Vector3 headFwd = Flatten(h.forward);

        Vector3 toPanel = position - headPos;
        float dist = toPanel.magnitude;
        Vector3 dir = dist > 1e-4f ? toPanel / dist : headFwd;

        // Decompose into signed yaw (around world up) and pitch relative to the head.
        Vector3 flat = toPanel;
        flat.y = 0f;
        float yaw = flat.sqrMagnitude > 1e-6f
            ? Vector3.SignedAngle(headFwd, flat.normalized, Vector3.up)
            : 0f;
        float pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;

        float clampedYaw = Mathf.Clamp(yaw, -MaxYawDeg, MaxYawDeg);
        float clampedPitch = Mathf.Clamp(pitch, MinPitchDeg, MaxPitchDeg);
        float clampedDist = Mathf.Clamp(dist, MinDistance * worldScale, MaxDistance * worldScale);

        // Rebuild a unit direction from the clamped yaw/pitch, then re-seat at the clamped distance.
        Vector3 horiz = Quaternion.AngleAxis(clampedYaw, Vector3.up) * headFwd;
        float pitchRad = clampedPitch * Mathf.Deg2Rad;
        Vector3 newDir = horiz * Mathf.Cos(pitchRad) + Vector3.up * Mathf.Sin(pitchRad);
        Vector3 newPos = headPos + newDir * clampedDist;

        bool moved = (newPos - position).sqrMagnitude > (HealEpsilon * worldScale) * (HealEpsilon * worldScale);
        position = newPos;
        rotation = Facing(newPos, headPos);
        return moved;
    }

    /// <summary>Upright orientation facing the head (uGUI front toward the viewer → +Z away).</summary>
    private static Quaternion Facing(Vector3 panelPos, Vector3 headPos)
    {
        Vector3 away = Flatten(panelPos - headPos);
        return Quaternion.LookRotation(away, Vector3.up);
    }

    /// <summary>Horizontal unit projection (falls back to world forward when degenerate).</summary>
    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
    }
}
