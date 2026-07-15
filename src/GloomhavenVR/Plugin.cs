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
    /// Empty = use the system's active runtime; consumed by Core in Phase 1.
    /// </summary>
    internal static ConfigEntry<string> RuntimeOverride = null!;

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
            "Sets XR_RUNTIME_JSON before XR init. Leave empty to use the system's active OpenXR runtime.");

        if (!Enabled.Value)
        {
            VRLog.Info("Disabled via config ([General] Enabled = false) — game runs vanilla.");
            return;
        }

        // Created up front so all modules/patch classes share one instance.
        // No patches are applied in Phase 0.
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

        RegisterModules();
        InitModules();

        VRLog.Info($"v{MyPluginInfo.PLUGIN_VERSION} loaded — chainload OK, {_modules.Count} module stubs initialized.");
    }

    /// <summary>
    /// ScriptEngine (F6 hot reload) calls OnDestroy on the old instance before loading
    /// the new one — undo everything we did so reloads stay clean (TOOLCHAIN §3.3).
    /// </summary>
    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
        _modules.Clear();
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
