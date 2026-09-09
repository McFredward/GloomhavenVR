using System;

namespace GloomhavenVR.Hands.Interact;

/// <summary>Shared distance and gesture arbitration for a world target behind UI. A visual
/// beam clamp alone is not permission to dispatch through the surface that clamped it.</summary>
internal static class LaserPointerPolicy
{
    internal static bool OwnsCarryFrame(bool carrying, int observedCarryFrame, int frame) =>
        carrying || (observedCarryFrame >= 0 && observedCarryFrame == frame);

    internal static bool AllowFallback(bool primaryHasBeam, bool primaryCarryOwnsFrame) =>
        !primaryHasBeam && !primaryCarryOwnsFrame;

    internal static float PickLimit(float reach, float solid, float bar, float panel, bool carryOwnsFrame) =>
        carryOwnsFrame ? 0f : Math.Min(Math.Min(reach, solid), Math.Min(bar, panel));

    internal static bool TargetBeforeBlocker(float target, float limit) =>
        target >= 0f && target < limit;

    internal static bool AllowClick(bool triggerDown, bool carryOwnsFrame, bool clickingHandHasTarget) =>
        triggerDown && !carryOwnsFrame && clickingHandHasTarget;
}
