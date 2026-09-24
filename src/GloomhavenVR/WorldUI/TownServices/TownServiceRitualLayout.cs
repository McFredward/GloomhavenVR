using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Native enhancement folio and temple offerings on the existing 1.5 m worktop. Coordinates returned here are
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
    internal const int MaxOfferings = 7;

    /// <summary>The native enhancement folio. Size is a maximum readable width/height;
    /// TownServiceSurface fits uniformly without changing native row layout or scrolling.</summary>
    internal static Placement Folio(ushort section) => section switch
    {
        10 => new Placement(new Vector3(.32f, .33f, -.16f), Quaternion.identity, new Vector2(.60f, .55f)),
        11 => new Placement(new Vector3(-.32f, .018f, -.03f), Quaternion.Euler(90f, 0f, 0f), new Vector2(.29f, .43f)),
        13 => new Placement(new Vector3(0f, .032f, .35f), Quaternion.Euler(90f, 0f, 0f), new Vector2(.28f, .065f)),
        14 => new Placement(new Vector3(0f, .033f, .25f), Quaternion.Euler(90f, 0f, 0f), new Vector2(.28f, .085f)),
        15 => new Placement(new Vector3(.19f, .036f, -.20f), Quaternion.Euler(90f, 0f, 0f), new Vector2(.18f, .055f)),
        16 => new Placement(new Vector3(.43f, .036f, -.20f), Quaternion.Euler(90f, 0f, 0f), new Vector2(.18f, .055f)),
        _ => throw new ArgumentOutOfRangeException(nameof(section))
    };

    internal static Placement Offering(int index, int count)
    {
        Validate(index, count, MaxOfferings, "temple offerings");
        // Campaign has one native blessing; Guildmaster can expose several. Keep every
        // native choice as a separate labelled purse over the same hand, never omit one.
        int row = index / 4, column = index % 4;
        int rowCount = Math.Min(4, count - row * 4);
        return new Placement(new Vector3((column - (rowCount - 1) * .5f) * .145f,
            .07f + row * .17f, .03f), Quaternion.identity, new Vector2(.125f, .15f));
    }

    private static void Validate(int index, int count, int maximum, string kind)
    {
        if (count > maximum)
            throw new InvalidOperationException("Physical town worktop cannot safely hold " + count + " " + kind
                + "; restore the complete original service window instead of omitting entries.");
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
