using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Native passthrough availability follows the current OpenXR session.
/// Preserve the user's saved preference while keeping unavailable choices inert and explained.
/// </summary>
internal static partial class VROptionsTab
{
    private static readonly List<Action> MixedRealityAvailabilityRefreshers = new();
    private static int _mixedRealityAvailabilitySignature = -1;
    private static (bool Companion, bool KeyColor)? _mixedRealityRowVisibility;

    private static bool SteamLinkCompanionVisible =>
        !FrameNativePassthrough.Standalone && Core.SteamLink.SteamLinkConnection.Available;

    private static bool MixedRealityKeyColorVisible =>
        !FrameNativePassthrough.Required && !SteamLinkCompanionVisible;

    private static (bool Companion, bool KeyColor) MixedRealityRowVisibility =>
        (SteamLinkCompanionVisible, MixedRealityKeyColorVisible);

    private static bool MixedRealityCanEnable =>
        !FrameNativePassthrough.Required || FrameNativePassthrough.IsAvailable;

    private static bool IsEnvironmentChoice(ConfigCatalog.ConfigItem item) =>
        item.Section == "Sky" && item.Key == "Style";

    private static bool IsMixedRealitySwitch(ConfigCatalog.ConfigItem item) =>
        item.Section == "MixedReality" && item.Key == "Enabled";

    private static bool CanEditMixedRealitySwitch(ConfigCatalog.ConfigItem item) =>
        MixedRealityCanEnable || item.Entry.BoxedValue is true;

    private static string MixedRealityHint()
    {
        if (!FrameNativePassthrough.Required)
            return SteamLinkCompanionVisible ? Loc.Mod("mr_frame_companion_selected")
                : Loc.ConfigHelpForPlayers("MixedReality", "Enabled") ?? string.Empty;
        if (MixedRealityCanEnable)
            return Loc.Mod("mr_frame_available");
        if (FrameNativePassthrough.Status == FrameNativePassthroughStatus.Checking)
            return Loc.Mod("mr_frame_checking");
        return Loc.Mod(FrameNativePassthrough.Standalone
            ? "mr_frame_update_required" : "mr_frame_stream_update_required");
    }

    private static void RefreshMixedRealitySwitch(ConfigCatalog.ConfigItem item, Toggle toggle, TMP_Text? title)
    {
        if (toggle == null)
            return;
        toggle.interactable = CanEditMixedRealitySwitch(item);
        string hint = MixedRealityHint();
        // Keep the caption's normal player help and the unavailable switch itself hoverable.
        if (title != null)
            AttachHoverHint(title, hint, item.Key);
        AttachHoverHint(toggle.gameObject, hint, item.Key);
    }

    private static void CaptureMixedRealityRowVisibility() =>
        _mixedRealityRowVisibility = MixedRealityRowVisibility;

    private static void RegisterMixedRealityAvailabilityRefresh(Action refresh)
    {
        MixedRealityAvailabilityRefreshers.Add(refresh);
        refresh();
    }

    /// <summary>Only a changed capability/session result repaints controls in an already open pane.</summary>
    private static void RefreshMixedRealityAvailability()
    {
        if (!IsOpen)
            return;
        var visibility = MixedRealityRowVisibility;
        bool changed = _mixedRealityRowVisibility.HasValue && _mixedRealityRowVisibility.Value != visibility;
        _mixedRealityRowVisibility = visibility;
        if (changed)
        {
            // The OpenXR capability and the companion connection can resolve independently.
            // Rebuild only when their result changes which rows belong in this pane.
            TickGuard.Run("VROptionsTab.RebuildSteamLinkCapability", Rebuild, "WorldUI");
            return;
        }
        if (MixedRealityAvailabilityRefreshers.Count == 0)
            return;
        int signature = (int)FrameNativePassthrough.Status * 4
            + (FrameNativePassthrough.Required ? 2 : 0) + (MixedRealityCanEnable ? 1 : 0);
        if (signature == _mixedRealityAvailabilitySignature)
            return;
        _mixedRealityAvailabilitySignature = signature;
        for (int i = 0; i < MixedRealityAvailabilityRefreshers.Count; i++)
            MixedRealityAvailabilityRefreshers[i]();
    }

    private static void ClearMixedRealityAvailabilityRefreshers()
    {
        MixedRealityAvailabilityRefreshers.Clear();
        _mixedRealityAvailabilitySignature = -1;
        _mixedRealityRowVisibility = null;
    }
}
