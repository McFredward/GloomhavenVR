using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Physicalized UI (Phase 3c, R4 "physical interface"): canvas-conversion framework,
/// physical Ready/Undo/Skip cluster, world panels for initiative track / element
/// board / combat log / objectives, HMD toasts for phase banners, world-modal
/// confirmation dialogs, world-space monster stat panels, true world-space actor
/// bars, wrist HUD, floating 2D screen + virtual-mouse pointer, gamepad-mode guard
/// and world tooltips. Built on the Phase-2 seed (<see cref="VirtualMouse"/>).
///
/// Everything is reversible: surfaces restore the panels they moved, patches gate on
/// <see cref="WorldUIConfig.ConversionActive"/> (vanilla when VR is off), and
/// <see cref="Shutdown"/> (ScriptEngine hot reload) puts the whole 2D UI back.
/// </summary>
internal sealed class WorldUIModule : IVRModule
{
    public string Name => "WorldUI";

    private GameObject? _driverGo;

    public void Init()
    {
        WorldUIConfig.Bind();

        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — WorldUI driver not installed.");
            return;
        }

        // Own Harmony patch classes (ROADMAP conflict containment): all prefixes
        // gate on WorldUI state and are vanilla otherwise.
        VRSession.Harmony?.PatchAll(typeof(WorldspaceDisplayPanelBase_Patches));
        VRSession.Harmony?.PatchAll(typeof(InputManager_SetGamepadInputDevice_Patch));
        VRSession.Harmony?.PatchAll(typeof(InputManager_AssignGamepadBindings_Patch));

        VREvents.UiLockChanged += OnUiLock;

        _driverGo = new GameObject("GloomhavenVR.WorldUIDriver");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<WorldUIDriver>();

        VRLog.Info(Name, "WorldUI driver installed (virtual mouse, canvas conversion, " +
                         "button cluster, panels, actor bars, wrist HUD, flat screen).");
    }

    public void Shutdown()
    {
        VREvents.UiLockChanged -= OnUiLock;

        if (_driverGo != null)
        {
            Object.Destroy(_driverGo); // driver OnDestroy shuts every feature down
            _driverGo = null;
        }

        CanvasConversion.ReleaseAll();
        WorldUIAssets.Reset();
        InputModeGuard.Reset();
        VirtualMouse.Reset();
        // Harmony patches are removed collectively by Plugin.OnDestroy (UnpatchSelf).
    }

    private static void OnUiLock(UiLockEvent e) => CanvasConversion.SetUiLocked(e.Locked);

    /// <summary>
    /// Per-frame service for all WorldUI features. Update: lifecycle/conversion
    /// decisions and the virtual-mouse bridge. LateUpdate: transform placement
    /// (after the game's own LateUpdate writers ran or were prefix-skipped).
    /// </summary>
    private sealed class WorldUIDriver : MonoBehaviour
    {
        private readonly ButtonCluster _buttons = new();
        private readonly WorldSurface[] _slotSurfaces =
        {
            new InitiativeTrackSurface(),
            new ElementBoardSurface(),
            new CombatLogSurface(),
            new ObjectivesSurface(),
        };
        private readonly DialogSurface _dialogs = new();
        private readonly PhaseBannerSurface _banner = new();
        private readonly StatPanelSurface _statPanels = new();
        private readonly WristHud _wristHud = new();
        private readonly FlatScreen _flatScreen = new();
        private readonly SettingsPanel _settingsPanel = new();
        private readonly WorldTooltips _tooltips = new();
        private readonly DevPanels _devPanels = new();

        private void Update()
        {
            VirtualMouse.Tick();
            InputModeGuard.Tick();

            _buttons.Tick();
            for (int i = 0; i < _slotSurfaces.Length; i++)
                _slotSurfaces[i].Tick();
            _dialogs.Tick();
            _banner.Tick();
            _statPanels.Tick();
            _wristHud.Tick();
            _flatScreen.Tick();
            _settingsPanel.Tick();
            _devPanels.Tick();

            CanvasConversion.Tick();
        }

        private void LateUpdate()
        {
            ActorBars.Tick();
            ActorBars.LateTick();
            _tooltips.LateTick();
        }

        private void OnDestroy()
        {
            _buttons.Shutdown();
            for (int i = 0; i < _slotSurfaces.Length; i++)
                _slotSurfaces[i].Shutdown();
            _dialogs.Shutdown();
            _banner.Shutdown();
            _statPanels.Shutdown();
            _wristHud.Shutdown();
            _flatScreen.Shutdown();
            _settingsPanel.Shutdown();
            _tooltips.Shutdown();
            _devPanels.Shutdown();
            ActorBars.ReleaseAll();
        }
    }
}
