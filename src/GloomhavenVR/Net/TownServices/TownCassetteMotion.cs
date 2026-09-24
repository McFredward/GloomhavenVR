using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

/// <summary>One analytic mechanism for owner and observers. Cards only change while both
/// shutter leaves are shut; the aperture is never used as a gameplay continuation gate.</summary>
internal static class TownCassetteMotion
{
    internal const byte RecordId = 86;
    internal const byte RollerRecordId = 87;
    internal const float RollerRadius = .09f, RowPitch = .17f;
    private const float RollerHeight = RowPitch * 2f;
    private const float RollerLength = RollerHeight * 2f + 2f * Mathf.PI * RollerRadius;

    // A complete belt revolution moves all three real holder rows down/up, folds each
    // around the cabinet lip, runs it behind the opaque seat backing, then unfolds it.
    // At half a revolution all three faces point into the cabinet: only there may native
    // page membership change. Owner and delayed/late-joining observers sample one curve.
    internal static void RowPose(int row, float progress, int direction, out Vector3 position, out Quaternion rotation)
    {
        float home = (row - 1) * RowPitch;
        if (direction == 0 || progress <= 0f || progress >= 1f)
        { position = new Vector3(0f, home, 0f); rotation = Quaternion.identity; return; }
        float distance = Mathf.Repeat(row * RowPitch - direction * RollerLength * Ease(progress), RollerLength);
        float y, z, angle;
        if (distance < RollerHeight)
        { y = -RowPitch + distance; z = 0f; angle = 0f; }
        else if (distance < RollerHeight + Mathf.PI * RollerRadius)
        {
            angle = (distance - RollerHeight) / RollerRadius;
            y = RowPitch + Mathf.Sin(angle) * RollerRadius;
            z = RollerRadius * (1f - Mathf.Cos(angle));
        }
        else if (distance < RollerHeight * 2f + Mathf.PI * RollerRadius)
        { y = RowPitch - (distance - RollerHeight - Mathf.PI * RollerRadius); z = 2f * RollerRadius; angle = Mathf.PI; }
        else
        {
            float arc = (distance - RollerHeight * 2f - Mathf.PI * RollerRadius) / RollerRadius;
            y = -RowPitch - Mathf.Sin(arc) * RollerRadius;
            z = RollerRadius * (1f + Mathf.Cos(arc)); angle = Mathf.PI + arc;
        }
        position = new Vector3(0f, y, z); rotation = Quaternion.Euler(angle * Mathf.Rad2Deg, 0f, 0f);
    }
    internal static void Apply(Transform housing, float progress, int direction)
    {
        Apply(housing, direction == 0 ? progress : 1f);
        for (int row = 0; row < 3; row++)
        {
            Transform? holder = housing.Find(row == 0 ? "Cassette/Row0" : row == 1 ? "Cassette/Row1" : "Cassette/Row2");
            if (holder == null) continue;
            RowPose(row, progress, direction, out Vector3 position, out Quaternion rotation);
            holder.localPosition = position; holder.localRotation = rotation;
        }
    }
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
