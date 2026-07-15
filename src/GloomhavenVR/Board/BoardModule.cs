using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// Board touch/ray targeting (Phase 3a, R5 "touch the board"):
///
/// - <b>Picking</b>: prefixes on <c>MF.FindInteractableAtMousePosition</c> and
///   <c>InputManager.get_CursorPosition</c> substitute the VR pick (near fingertip
///   touch / far hand ray, <see cref="BoardPick"/>). <c>HoverRegisterer</c> and the
///   pooled <c>HexSelect_Control</c> highlight pipeline run unchanged on top.
/// - <b>Click commit</b>: <see cref="BoardClickDriver"/> + a postfix on
///   <c>Controller.CommonLoop</c> drive the game's own click path
///   (LateUpdate → ShowNormalInterface → TileBehaviour.s_Callback) — never
///   ScenarioRuleClient directly, so undo/MP/second-click-confirm stay intact.
/// - <b>AoE</b>: <see cref="AoeControl"/> — thumbstick flick →
///   <c>WorldspaceStarHexDisplay.RotateAOEClockwise</c> + star redraw + haptic.
/// - <b>UX</b>: <see cref="TargetingUx"/> — valid-target hover haptics; hex-center
///   cursor snap via config; enemy stat popup rides the game's hover flow.
///
/// Mode integration: the VRModeStateMachine defaults already cover Phase 3a —
/// BoardTargeting = Ray|Poke and all five targeting wait-states are mapped
/// (VRModeStateMachine.cs), so no MapMessage/SetTargetingState/SetInteractorPolicy
/// extension calls are needed here. Near-touch additionally works in every mode
/// whose policy includes Poke (e.g. character placement during CardSelection).
///
/// Active when VR runs, and in Dev mode ([Dev] Enabled) so the whole pick/click
/// pipeline is exercisable flat via [Dev] SimulateHands (+ [Board] ForceFarMode).
/// </summary>
internal sealed class BoardModule : IVRModule
{
    public string Name => "Board";

    private GameObject? _driverGo;
    private bool _active;

    public void Init()
    {
        // Bind [Board] config against the plugin's ConfigFile (BaseUnityPlugin.Config
        // is public in BepInEx 5; the instance is findable during Plugin.Awake because
        // AddComponent registers the component before Awake runs). Bound even when the
        // module stays dormant, so the section always shows up in the cfg file.
        ConfigFile? config = ResolvePluginConfig();
        if (config == null)
        {
            VRLog.Warn(Name, "Plugin ConfigFile not reachable — [Board] config not bound, module disabled.");
            return;
        }
        BoardConfig.Bind(config);

        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — board targeting not installed.");
            return;
        }

        _active = true;

        VRSession.Harmony?.PatchAll(typeof(Patches.MF_FindInteractableAtMousePosition_Patch));
        VRSession.Harmony?.PatchAll(typeof(Patches.InputManager_CursorPosition_Patch));
        VRSession.Harmony?.PatchAll(typeof(Controller_CommonLoop_Patch));

        _driverGo = new GameObject("GloomhavenVR.Board");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<BoardDriver>();

        VRLog.Info(Name, "Board targeting installed (pick + cursor + click patches, AoE stick control).");
    }

    public void Shutdown()
    {
        if (_driverGo != null)
        {
            Object.Destroy(_driverGo);
            _driverGo = null;
        }

        if (_active)
        {
            _active = false;
            // Hot-reload hygiene: everything here is static.
            BoardPick.Reset();
            BoardClickDriver.Reset();
            AoeControl.Reset();
            TargetingUx.Reset();
        }
        BoardConfig.Reset();
        // Harmony patches are removed collectively by Plugin.OnDestroy (UnpatchSelf).
    }

    private static ConfigFile? ResolvePluginConfig()
    {
        Plugin? plugin = Object.FindObjectOfType<Plugin>();
        if (plugin != null)
            return plugin.Config;

        // Fallback (e.g. exotic hot-reload host): the chainloader registry.
        if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(MyPluginInfo.PLUGIN_GUID, out var info)
            && info.Instance is Plugin registered)
        {
            return registered.Config;
        }
        return null;
    }
}
