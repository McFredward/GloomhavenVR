using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Phase-3b config. Plugin.cs is frozen shared surface, so the Cards module binds its
/// own ConfigFile (<c>BepInEx/config/dev.gloomhavenvr.cards.cfg</c>) instead of adding
/// entries to the main plugin config.
/// </summary>
internal static class CardsConfig
{
    private static ConfigFile? _file;

    /// <summary>Spawn N dummy VR cards (procedural face) without a scenario — exercises fan/tray/grab under [Dev] SimulateHands.</summary>
    internal static ConfigEntry<int> DevFakeHand = null!;

    /// <summary>Fan arc radius in real meters (diorama scale applied automatically).</summary>
    internal static ConfigEntry<float> FanRadius = null!;

    /// <summary>Max total fan arc in degrees.</summary>
    internal static ConfigEntry<float> FanArcDegrees = null!;

    /// <summary>Height of the fan pivot above the palm, real meters.</summary>
    internal static ConfigEntry<float> FanPalmOffset = null!;

    /// <summary>Card width in real meters (poker card = 0.0635); height follows 63.5:88 aspect.</summary>
    internal static ConfigEntry<float> CardWidth = null!;

    /// <summary>Scale factor applied to a card while grabbed/inspected.</summary>
    internal static ConfigEntry<float> InspectScale = null!;

    /// <summary>Tray placement offset from the head, real meters: forward distance.</summary>
    internal static ConfigEntry<float> TrayForward = null!;

    /// <summary>Tray placement offset from the head, real meters: drop below eye height.</summary>
    internal static ConfigEntry<float> TrayDown = null!;

    /// <summary>Tray placement offset, real meters: sideways (+right).</summary>
    internal static ConfigEntry<float> TrayRight = null!;

    /// <summary>Tray tilt toward the player in degrees (0 = flat).</summary>
    internal static ConfigEntry<float> TrayTilt = null!;

    /// <summary>Animation speed for cards flying between fan/tray/half layout (1/s, exponential smoothing).</summary>
    internal static ConfigEntry<float> CardLerpSpeed = null!;

    internal static void Bind()
    {
        if (_file != null)
            return;

        _file = new ConfigFile(Path.Combine(Paths.ConfigPath, "dev.gloomhavenvr.cards.cfg"), true);

        DevFakeHand = _file.Bind("Cards", "DevFakeHand", 0,
            "Spawn this many dummy VR cards (procedural placeholder faces) so the fan/tray/grab " +
            "mechanics are exercisable without a scenario. Requires [Dev] Enabled (+ SimulateHands " +
            "or a real HMD). 0 = off.");
        FanRadius = _file.Bind("Cards", "FanRadius", 0.16f,
            "Palm fan arc radius in real-world meters (diorama scale is applied automatically).");
        FanArcDegrees = _file.Bind("Cards", "FanArcDegrees", 70f,
            "Maximum total fan arc in degrees (cards overlap more as the hand grows).");
        FanPalmOffset = _file.Bind("Cards", "FanPalmOffset", 0.09f,
            "Height of the fan pivot above the palm center, real-world meters.");
        CardWidth = _file.Bind("Cards", "CardWidth", 0.0635f,
            "Physical card width in meters (real poker card = 0.0635). Height keeps the 63.5:88 aspect.");
        InspectScale = _file.Bind("Cards", "InspectScale", 1.6f,
            "Scale multiplier applied to a card while it is held (natural-size inspection).");
        TrayForward = _file.Bind("Cards", "TrayForward", 0.42f,
            "Play tray placement: forward distance from the head at placement time, meters.");
        TrayDown = _file.Bind("Cards", "TrayDown", 0.32f,
            "Play tray placement: drop below eye height, meters.");
        TrayRight = _file.Bind("Cards", "TrayRight", 0.0f,
            "Play tray placement: sideways offset (+right), meters.");
        TrayTilt = _file.Bind("Cards", "TrayTilt", 30f,
            "Play tray tilt toward the player, degrees (0 = lying flat).");
        CardLerpSpeed = _file.Bind("Cards", "CardLerpSpeed", 14f,
            "Card fly animation speed (exponential smoothing constant, 1/s).");
    }

    /// <summary>Card height derived from width (63.5 x 88 mm poker aspect).</summary>
    internal static float CardHeight => CardWidth.Value * (88f / 63.5f);

    internal static Vector3 TrayOffset => new(TrayRight.Value, -TrayDown.Value, TrayForward.Value);
}
