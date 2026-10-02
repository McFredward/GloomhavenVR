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
    internal const bool DesktopMirrorLeftEye = false;

    // The game's native Fantastic profile uses a 900 MB streaming budget. This minimum is
    // inert under the fresh native Fastest profile, which has streaming disabled; unlike the
    // PC's 4096 MB floor, it does not reserve a desktop-sized budget if a user changes preset.
    internal const int TextureStreamingBudgetMB = 900;

    // Build603 separates grass from other decoration; fresh Frame profiles request no
    // eligible vegetation. BepInEx retains the earlier saved25% or any user's own value.
    internal const int ScenarioSceneryDensityPercent = 0;

    // Build 600's narrow grass rule masked only 27 of 6,560 renderers in the measured scenario.
    // The maintainer now requests essential scenery on standalone Frame (2026-10-01).
    // This seeds only the new key; all platforms retain the same reversible live control.
    internal const int ScenarioDecorationDensityPercent = 0;
    internal const int ScenarioVegetationDensityPercent = 0;
    internal const int ScenarioPlayerFigureDetailPercent = 0;
    internal const int ScenarioEnemyFigureDetailPercent = 0;
    internal const int ScenarioFigureEffectsDensityPercent = 0;
    internal const bool ScenarioFigureClothSimulation = false;
    internal const bool ReduceScenarioGenerationDetail = true;
    internal const bool ScenarioStaticBatching = true;
    internal const bool ScenarioSimpleEnvironmentShading = true;
    internal const int ScenarioEnvironmentEffectsDensityPercent = 0;

    // A 4 s wall-rescan cadence was active in the tested Frame configuration. It reduces
    // rescan frequency, not the duration of an individual rescan.
    internal const float WallRescanIntervalSeconds = 4f;
}
