using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

/// <summary>One analytic mechanism for owner and observers. Cards only change while both
/// shutter leaves are shut; the aperture is never used as a gameplay continuation gate.</summary>
internal static class TownCassetteMotion
{
    internal const byte RecordId = 86;
    private static float Ease(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }
    internal static void Sample(float progress, out float depth, out float open)
    {
        progress = Mathf.Clamp01(progress);
        depth = .32f * (progress < .5f ? Ease(progress / .30f) : 1f - Ease((progress - .70f) / .30f));
        open = progress < .5f ? 1f - Ease((progress - .30f) / .15f) : Ease((progress - .55f) / .15f);
    }
    internal static void Apply(Transform housing, float progress)
    {
        Sample(progress, out float depth, out float open);
        Transform? cassette = housing.Find("Cassette"), upper = housing.Find("Shutter/Upper"), lower = housing.Find("Shutter/Upper/Lower");
        if (cassette != null) cassette.localPosition = new Vector3(0f, 0f, depth);
        if (upper != null) upper.localRotation = Quaternion.Euler(-90f * open, 0f, 0f);
        if (lower != null) lower.localRotation = Quaternion.Euler(180f * open, 0f, 0f);
    }
}
