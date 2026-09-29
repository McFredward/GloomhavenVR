using System;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Keeps the already selected, locally owned map character selected while the native
/// guildmaster HUD changes its map options. The game calls DisableMapOptions or
/// EnableMapOptions at each town destination boundary. Both close character subwindows,
/// then also deselect the portrait through Escape/OnClick or the tab's DeselectCurrent.
/// The latter is inappropriate for the permanent VR character UI: Build 590's map log
/// records 12 selection drop edges (10 inside the old 4 Hz polling window) while walking
/// between residents. Restoring the character on the next Tick still lets a rendered
/// frame or selection callback observe nobody.
///
/// Only the two native map-options calls establish this synchronous scope. Their window
/// and tab cleanup still runs; only the redundant portrait click and deselection callback
/// are skipped for a slot which remains assigned to and selected by this local player.
/// User clicks, roster edits, unassigned/foreign slots, map tutorials and non-VR maps
/// retain native behavior. A finalizer clears the scope even if native UI throws.
/// </summary>
[HarmonyPatch]
internal static class MapSelectionTransition
{
    private const string Scope = "WorldUI";

    [ThreadStatic] private static int _mapOptionsDepth;
    private static bool _reported;
    private static bool _failed;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NewPartyDisplayUI), nameof(NewPartyDisplayUI.DisableMapOptions))]
    private static void BeginDisable(ref bool __state) => Begin(ref __state);

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(NewPartyDisplayUI), nameof(NewPartyDisplayUI.DisableMapOptions))]
    private static Exception? EndDisable(Exception? __exception, bool __state)
        => End(__exception, __state);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NewPartyDisplayUI), nameof(NewPartyDisplayUI.EnableMapOptions))]
    private static void BeginEnable(ref bool __state) => Begin(ref __state);

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(NewPartyDisplayUI), nameof(NewPartyDisplayUI.EnableMapOptions))]
    private static Exception? EndEnable(Exception? __exception, bool __state)
        => End(__exception, __state);

    internal static void Begin(ref bool state)
    {
        state = false;
        try
        {
            if (!MapRoomDriver.Active || !WorldUIConfig.ConversionActive
                || MapFTUEManager.IsPlaying || MapCharacterSelection.Selected == null)
                return;
            _mapOptionsDepth++;
            state = true;
        }
        catch (Exception ex)
        {
            WarnOnce(ex);
        }
    }

    internal static Exception? End(Exception? exception, bool state)
    {
        if (state && _mapOptionsDepth > 0)
            _mapOptionsDepth--;
        return exception;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NewPartyCharacterUI), nameof(NewPartyCharacterUI.OnClick))]
    internal static bool BeforePortraitClick(NewPartyCharacterUI __instance)
        => Permit(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NewPartyDisplayUI), "OnCharacterSelect")]
    internal static bool BeforeSelectionChange(NewPartyDisplayUI __instance, bool __0,
                                               NewPartyCharacterUI __1)
        => __0 || Permit(__1, __instance);

    private static bool Permit(NewPartyCharacterUI slot, NewPartyDisplayUI? panel = null)
    {
        try
        {
            if (_mapOptionsDepth == 0 || !MapRoomDriver.Active || !WorldUIConfig.ConversionActive)
                return true;
            NewPartyDisplayUI? currentPanel = panel ?? NewPartyDisplayUI.PartyDisplay;
            if (currentPanel == null || !ReferenceEquals(currentPanel.SelectedUISlot, slot)
                || !ReferenceEquals(MapCharacterSelection.Selected, slot.Data))
                return true;

            if (!_reported)
            {
                _reported = true;
                VRLog.Info(Scope, "MAP SELECTION TRANSITION: retained the locally owned selected "
                    + "portrait while native map options changed. Character subwindow and tab "
                    + "cleanup still ran; ordinary user selection remains native. The next "
                    + "selection census should have no drop edge for this mode change.");
            }
            return false;
        }
        catch (Exception ex)
        {
            WarnOnce(ex);
            return true; // A failed guard must not block a native UI callback.
        }
    }

    private static void WarnOnce(Exception ex)
    {
        if (_failed)
            return;
        _failed = true;
        VRLog.Warn(Scope, $"MAP SELECTION TRANSITION failed; native selection remains active "
            + $"({ex.GetType().Name}: {ex.Message}).");
    }
}
