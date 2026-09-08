using System;

namespace GloomhavenVR.Net;

/// <summary>Native ExtendedButton scale targets and uGUI's linear unscaled color-fade progress.
/// These functions choose presentation only; they never grant a disabled button an action.</summary>
internal static class RemoteDecisionMotion
{
    internal static float HoverTarget(bool offered, bool scaleNonInteractable,
        bool hovered, bool pressed, float factor) =>
        !offered && !scaleNonInteractable ? 1f
        : offered && pressed ? (factor + 1f) * 0.5f
        : hovered ? factor : 1f;

    internal static float FadeProgress(float elapsed, float duration) =>
        duration <= 0f ? 1f : Math.Max(0f, Math.Min(1f, elapsed / duration));
}
