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
        try { return Switch(__instance, newMode, ref ___currentMode); }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return true; // Discovery failed before any mutation; retain native handling.
        }
    }

    private static bool Switch(UIGuildmasterHUD hud, EGuildmasterMode newMode,
                               ref EGuildmasterMode currentMode)
    {
        if (TownWindowCloseScope.Active) return true;
        if (_switchDepth != 0 && currentMode == newMode
            && GuildmasterDestinations.IsMapSurfaceMode(newMode)) return false;
        if (!MapRoomDriver.Active || !WorldUIConfig.ConversionActive
            || !GuildmasterDestinations.IsMapSurfaceMode(newMode)
            || currentMode is not (EGuildmasterMode.Merchant or EGuildmasterMode.Temple
                or EGuildmasterMode.Enchantress)) return true;
        var window = GuildmasterDestinations.ModeWindow(currentMode);
        if (window == null || !window.IsOpen || !ModalFallback.FloatIsLive(window)) return true;
        MapChoreographer? choreographer = MapRoomDriver.Choreographer;
        if (choreographer == null) return true;
        EGuildmasterMode service = currentMode;
        _switchDepth++;
        try
        {
            currentMode = newMode;
            if (newMode == EGuildmasterMode.City) choreographer.OpenCityMap(transition: false);
            else choreographer.OpenWorldMap(transition: false);
            GuildmasterDestinations.RememberMapSurface(newMode);
            if (VRLog.WantsDebug)
                VRLog.Debug("WorldUI", "Map surface changed to " + newMode
                    + " without exiting the open " + service + " window.");
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
        finally
        {
            currentMode = service;
            _switchDepth--;
        }
        return false;
    }

    private static void ReportFailure(Exception ex)
    {
        if (_reportedFailure) return;
        _reportedFailure = true;
        VRLog.Alert("WorldUI", "Map surface switch failed; the open town service was retained: " + ex);
    }
}
