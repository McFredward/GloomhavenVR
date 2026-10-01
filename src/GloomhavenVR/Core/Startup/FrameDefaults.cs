using BepInEx;

namespace GloomhavenVR;

/// <summary>
/// Defaults for a fresh Steam Frame standalone VR configuration. BepInEx keeps every existing
/// entry on Bind, so this profile never replaces a player's saved choices. The Frame setup
/// marker is absent on Windows PC installs and on ordinary unmodified game installs.
/// </summary>
internal static class FrameDefaults
{
    internal static bool Active => FrameLaunchOptIn.MarkerExists(Paths.BepInExRootPath);

    // Build 592 standalone hardware run. 3408 pixels per eye was selected in SteamVR,
    // outside the mod; the mod's scale remains 1.00 and does not hard-code that resolution.
    internal const int MsaaLevel = 0;
    internal const float EyeResolutionScale = 1f;
    internal const bool ForceAnisotropic = true;
    internal const bool ForceFullTextureResolution = true;
    internal const bool ForceTextureStreamingOff = false;
    internal const int PixelLightCount = 0;

    // The game's native Fantastic profile uses a 900 MB streaming budget. This minimum is
    // inert under the fresh native Fastest profile, which has streaming disabled; unlike the
    // PC's 4096 MB floor, it does not reserve a desktop-sized budget if a user changes preset.
    internal const int TextureStreamingBudgetMB = 900;

    // A 4 s wall-rescan cadence was active in the tested Frame configuration. It reduces
    // rescan frequency, not the duration of an individual rescan.
    internal const float WallRescanIntervalSeconds = 4f;
}
