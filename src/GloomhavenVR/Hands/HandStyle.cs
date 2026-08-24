namespace GloomhavenVR.Hands;

/// <summary>
/// Selectable hand-model style. The LOCAL choice lives in <c>[Hands] HandStyle</c>
/// (bound in <see cref="Plugin"/>, picked in the VR settings panel, live-rebuild via
/// <see cref="HandsDriver"/>) and rides the avatar wire
/// (<see cref="Net.AvatarState.HandStyle"/>) so other VR players render your chosen
/// hands on your remote avatar — exactly like the head-mask id.
///
/// Wire contract: the numeric values are serialized as a byte; NEVER renumber. New
/// styles must be appended (and <see cref="HandStyles.Count"/> bumped) so old peers
/// clamp unknown values back to a shipped style.
/// </summary>
internal enum HandStyle
{
    /// <summary>Original leather glove (default; also the fallback when a styled prefab
    /// is missing from an old bundle).</summary>
    Glove = 0,

    /// <summary>Plate-armor gauntlet. Artist-authored mesh, rig and atlas since ModBuild 243,
    /// adopted through unity/hand-prep/import_glove_fbx.py; it was the last Hunyuan3D-generated
    /// hand and its replacement retired that pipeline from everything that ships.</summary>
    Plate = 1,

    /// <summary>Arcane-runes mage glove (artist-authored since ModBuild 171).</summary>
    Arcane = 2,
}

/// <summary>Style table shared by the loader, the settings UI and the net layer.</summary>
internal static class HandStyles
{
    /// <summary>Number of selectable styles (wire values are clamped to [0, Count-1]).</summary>
    public const int Count = 3;

    /// <summary>
    /// Asset base name for a style inside the bundle: prefabs are
    /// <c>Assets/Bundle/Hands/&lt;BaseName&gt;_L.prefab</c> / <c>_R.prefab</c>
    /// (see BuildHands.cs, which assembles all three pairs).
    /// </summary>
    public static string BaseName(HandStyle style) => style switch
    {
        HandStyle.Plate => "VRHandPlate",
        HandStyle.Arcane => "VRHandArcane",
        _ => "VRHand",
    };

    /// <summary>Clamp an arbitrary (wire) byte to a valid style.</summary>
    public static HandStyle Clamp(int value) =>
        (HandStyle)UnityEngine.Mathf.Clamp(value, 0, Count - 1);
}
