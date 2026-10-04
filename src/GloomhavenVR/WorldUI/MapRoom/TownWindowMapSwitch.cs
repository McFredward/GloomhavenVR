using System;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>A map-surface switch is independent of an already floated town service.
/// Native UpdateCurrentMode exits that service, clearing temple rows and changing its
/// borrowed banner. Keep its controller alive; run only the original map switch with
/// a scoped map mode so native location visibility still sees the requested surface.</summary>
[HarmonyPatch(typeof(UIGuildmasterHUD), nameof(UIGuildmasterHUD.UpdateCurrentMode))]
internal static class TownWindowMapSwitch
{
    [ThreadStatic] private static int _switchDepth;
    private static bool _reportedFailure;

    [HarmonyPrefix]
    internal static bool Prefix(UIGuildmasterHUD __instance, EGuildmasterMode newMode,
                                ref EGuildmasterMode ___currentMode)
    {
        if (_switchDepth != 0) return false;
        if (!MapRoomDriver.Active || !WorldUIConfig.ConversionActive
            || !GuildmasterDestinations.IsMapSurfaceMode(newMode)
            || ___currentMode is not (EGuildmasterMode.Merchant or EGuildmasterMode.Temple
                or EGuildmasterMode.Enchantress)) return true;
        var window = GuildmasterDestinations.ModeWindow(___currentMode);
        if (window == null || !window.IsOpen || !ModalFallback.FloatIsLive(window)) return true;
        MapChoreographer? choreographer = MapRoomDriver.Choreographer;
        if (choreographer == null) return true;
        EGuildmasterMode service = ___currentMode;
        _switchDepth++;
        try
        {
            ___currentMode = newMode;
            if (newMode == EGuildmasterMode.City) choreographer.OpenCityMap(transition: false);
            else choreographer.OpenWorldMap(transition: false);
            GuildmasterDestinations.RememberMapSurface(newMode);
            if (VRLog.WantsDebug)
                VRLog.Debug("WorldUI", "Map surface changed to " + newMode
                    + " without exiting the open " + service + " window.");
        }
        catch (Exception ex)
        {
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                VRLog.Note("WorldUI", "Map surface switch failed; the open town service was retained: " + ex);
            }
        }
        finally
        {
            ___currentMode = service;
            _switchDepth--;
        }
        return false;
    }
}
