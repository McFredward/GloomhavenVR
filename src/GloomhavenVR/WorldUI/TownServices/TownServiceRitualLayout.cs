using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Physical stock on the existing 1.5 m worktop. Coordinates returned here are
/// relative to Ritual's (.0,.978,-.08) mount, not the resident or the native flat window.</summary>
internal static class TownServiceRitualLayout
{
    internal readonly struct Placement
    {
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly Vector2 Size;
        internal Placement(Vector3 position, Quaternion rotation, Vector2 size)
        { Position = position; Rotation = rotation; Size = size; }
    }
    internal static readonly Vector3 Origin = new(0f, .978f, -.08f);
    internal const int MaxCards = 64, MaxRunes = 48, MaxOfferings = 7;
    private const float Tilt = 65f, Front = -.330f, Back = .100f;
    private const float SupportedEdge = .959f, BodyHalfThickness = .0015f;

    internal static Placement Card(int index, int count)
    {
        Validate(index, count, MaxCards, "ability cards");
        int rows = Mathf.Max(8, (count + 3) / 4), column = index / rows, row = index % rows;
        // Preserve the original physical card aspect ratio. Four narrow files clear the
        // original book at x +/- .16, the working card at +/- .125 and native rear props.
        return File(new Vector2(.13f, .22f * .13f / .145f), -.65f + column * .14f, row, rows, .035f);
    }

    internal static Placement Rune(int index, int count)
    {
        Validate(index, count, MaxRunes, "enhancement samples");
        int rows = Mathf.Max(10, (count + 2) / 3);
        return File(new Vector2(.17f, .17f), .255f + (index % 3) * .18f, index / 3, rows, .025f);
    }

    private static Placement File(Vector2 size, float x, int row, int rows, float preferredStep)
    {
        float angle = Tilt * Mathf.Deg2Rad;
        float halfDepth = size.y * .5f * Mathf.Sin(angle) + BodyHalfThickness * Mathf.Cos(angle);
        float halfHeight = size.y * .5f * Mathf.Cos(angle) + BodyHalfThickness * Mathf.Sin(angle);
        float step = Mathf.Min(preferredStep, (Back - Front - 2f * halfDepth) / (rows - 1));
        // Every lower edge actually touches the worktop. Raising later rows without an
        // authored stepped rack would leave them floating. Parallel faces stay separated
        // by their depth stride, including their real millimetre-thick backs and rims.
        Vector3 world = new(x, SupportedEdge + halfHeight, Front + halfDepth + row * step);
        return new Placement(world - Origin, Quaternion.Euler(Tilt, 0f, 0f), size);
    }

    internal static Placement Mode(bool sell)
    {
        // The native buy/remove samples sit on the existing front brass apron. They cannot
        // steal space from the selected card or overlap the first row of physical stock.
        return new Placement(new Vector3(sell ? .12f : -.12f, .884f, -.364f) - Origin,
            Quaternion.identity, new Vector2(.10f, .06f));
    }

    internal static Placement Offering(int index, int count)
    {
        Validate(index, count, MaxOfferings, "temple offerings");
        Vector3 world = index == 0 ? new Vector3(-.42f, .958f, .08f)
            : new Vector3(.25f + ((index - 1) % 3) * .14f, .958f, -.18f + ((index - 1) / 3) * .14f);
        // Piece's native coin child is rotated -90 degrees. This makes the actual coin
        // lie flat while its original inscription faces upwards; no hovering tilted disk.
        return new Placement(world - Origin, Quaternion.Euler(90f, 0f, 0f), Vector2.one * .075f);
    }

    private static void Validate(int index, int count, int maximum, string kind)
    {
        if (count > maximum)
            throw new InvalidOperationException("Physical town worktop cannot safely hold " + count + " " + kind
                + "; restore the complete original service window instead of omitting entries.");
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
