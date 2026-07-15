using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR;

/// <summary>
/// GloomhavenVR entry point. Binds config, and — only when enabled — creates the
/// Harmony instance and initializes all feature modules. With
/// <c>[General] Enabled = false</c> the plugin is a strict no-op and the game
/// runs 100% vanilla.
/// </summary>
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    /// <summary>Master switch. When false the plugin does nothing at all.</summary>
    internal static ConfigEntry<bool> Enabled = null!;

    /// <summary>
    /// Optional path to an OpenXR runtime JSON (sets <c>XR_RUNTIME_JSON</c>).
    /// Empty = enumerate installed runtimes (registry + well-known paths) with failover.
    /// </summary>
    internal static ConfigEntry<string> RuntimeOverride = null!;

    /// <summary>
    /// Diorama scale: game world units per real-world meter (the VR rig is scaled up
    /// by this factor so the board reads as a table).
    /// 0 = auto: derived from the hex tile size (UnityGameEditorRuntime.s_TileSize).
    /// </summary>
    internal static ConfigEntry<float> WorldScale = null!;

    /// <summary>Disable PPv2 (PostProcessLayer/PostProcessVolume) while VR runs (P1 default: on).</summary>
    internal static ConfigEntry<bool> DisablePostProcessing = null!;

    /// <summary>Disable VolumetricFogAndMist.VolumetricFog while VR runs.</summary>
    internal static ConfigEntry<bool> DisableVolumetricFog = null!;

    /// <summary>Extra comma-separated component type full names to disable while VR runs.</summary>
    internal static ConfigEntry<string> DisableComponents = null!;

    private Harmony? _harmony;

    /// <summary>
    /// Feature module registry. Order matters: Core first (XR bootstrap),
    /// Compat last (fixups on top of everything else).
    /// </summary>
    private readonly List<IVRModule> _modules = [];

    private void Awake()
    {
        VRLog.Init(Logger);

        Enabled = Config.Bind(
            "General", "Enabled", true,
            "Master switch. Set to false to run the game completely vanilla (the mod does nothing).");
        RuntimeOverride = Config.Bind(
            "General", "RuntimeOverride", "",
            "Optional path to an OpenXR runtime JSON file (e.g. SteamVR's steamxr_win64.json). " +
            "Sets XR_RUNTIME_JSON before XR init and is tried first. Leave empty to auto-detect " +
            "(active runtime from the registry, then all available runtimes, then well-known paths).");
        WorldScale = Config.Bind(
            "Rig", "WorldScale", 0f,
            "Diorama scale: game world units per real-world meter (the rig is scaled by this, " +
            "making the board read as a table). 0 = auto from the hex tile size (~10-20 typical).");
        DisablePostProcessing = Config.Bind(
            "Compat", "DisablePostProcessing", true,
            "Disable PostProcessing v2 (PostProcessLayer/PostProcessVolume) while VR is active. " +
            "Phase 1 default: true (PPv2 is unverified under stereo rendering).");
        DisableVolumetricFog = Config.Bind(
            "Compat", "DisableVolumetricFog", true,
            "Disable the VolumetricFogAndMist.VolumetricFog image effect while VR is active.");
        DisableComponents = Config.Bind(
            "Compat", "DisableComponents", "",
            "Extra comma-separated component type full names (optionally 'FullName, Assembly') " +
            "to disable while VR is active, e.g. 'BeautifyEffect.Beautify'.");

        if (!Enabled.Value)
        {
            VRLog.Info("Disabled via config ([General] Enabled = false) — game runs vanilla.");
            return;
        }

        // Created up front so all modules/patch classes share one instance.
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        VRSession.Harmony = _harmony;

        RegisterModules();
        InitModules();

        VRLog.Info($"v{MyPluginInfo.PLUGIN_VERSION} loaded — {_modules.Count} modules initialized, " +
                   $"VR {(VRSession.IsRunning ? $"RUNNING on '{VRSession.RuntimeName}'" : "not running")}.");
    }

    /// <summary>
    /// ScriptEngine (F6 hot reload) calls OnDestroy on the old instance before loading
    /// the new one — undo everything we did so reloads stay clean (TOOLCHAIN §3.3):
    /// modules shut down in reverse order (Core stops XR last),
    /// then all Harmony patches are removed.
    /// </summary>
    private void OnDestroy()
    {
        for (int i = _modules.Count - 1; i >= 0; i--)
        {
            try
            {
                _modules[i].Shutdown();
            }
            catch (Exception e)
            {
                VRLog.Error(_modules[i].Name, $"Shutdown failed: {e}");
            }
        }
        _modules.Clear();

        _harmony?.UnpatchSelf();
        _harmony = null;
        VRSession.Harmony = null;
    }

    private void RegisterModules()
    {
        _modules.Add(new Core.CoreModule());
        _modules.Add(new Rig.RigModule());
        _modules.Add(new Hands.HandsModule());
        _modules.Add(new Cards.CardsModule());
        _modules.Add(new Board.BoardModule());
        _modules.Add(new WorldUI.WorldUIModule());
        _modules.Add(new Compat.CompatModule());
    }

    private void InitModules()
    {
        foreach (IVRModule module in _modules)
        {
            try
            {
                module.Init();
            }
            catch (Exception e)
            {
                VRLog.Error(module.Name, $"Init failed: {e}");
            }
        }
    }
}
