using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Physicalized UI (Phase 3c, R4 "physical interface"): canvas-conversion framework,
/// physical Ready/Undo/Skip cluster, world panels for initiative track / element
/// board / combat log / objectives, world-modal confirmation dialogs, world-space
/// monster stat panels, true world-space actor bars, wrist HUD, floating 2D screen +
/// virtual-mouse pointer, gamepad-mode guard and world tooltips. Built on the
/// Phase-2 seed (<see cref="VirtualMouse"/>). The phase banner is NOT converted
/// (test #18: the converted banner never received its onHidden — the box froze in
/// space and its soft lock kept every host raycaster disabled for the rest of the
/// scenario); the round number lives on the control board instead (PlayTray).
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
        VREvents.SessionResumed += OnSessionResumed; // doff/don recovery sweep (test #17)
        ModalFallback.Attach(); // catch-all modal fallback (P6): UIWindow visibility → ModalUI + screen

        _driverGo = new GameObject("GloomhavenVR.WorldUIDriver");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<WorldUIDriver>();

        VRLog.Info(Name, "WorldUI driver installed (virtual mouse, canvas conversion, " +
                         "button cluster, panels, actor bars, wrist HUD, flat screen).");

        LogKillSwitchState("startup");
        // These two switches blank the ENTIRE VR interface when off (test #11: an
        // accidentally persisted Master=false read as "menu no longer loads" — the
        // HMD shows only void + hands). Log every flip loudly and re-warn at startup.
        WorldUIConfig.Master.SettingChanged += (_, _) => LogKillSwitchState("setting changed");
        WorldUIConfig.FlatScreen.SettingChanged += (_, _) => LogKillSwitchState("setting changed");
    }

    private void LogKillSwitchState(string reason)
    {
        bool master = WorldUIConfig.Master.Value;
        bool screen = WorldUIConfig.FlatScreen.Value;
        if (!master || !screen)
        {
            VRLog.Warn(Name, $"UI SHELL DISABLED BY CONFIG ({reason}): [WorldUI] Master={master}, " +
                             $"FlatScreen={screen} — the HMD will show only the void, hands and lasers. " +
                             "Fix: set both to true in BepInEx/config/dev.gloomhavenvr.worldui.cfg " +
                             "(or delete the file to restore defaults).");
        }
        else
        {
            VRLog.Info(Name, $"UI shell active ({reason}): Master=true, FlatScreen=true.");
        }
    }

    public void Shutdown()
    {
        VREvents.UiLockChanged -= OnUiLock;
        VREvents.SessionResumed -= OnSessionResumed;
        ModalFallback.Detach();
        NonDominantHold.Reset();

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
    /// Presence-regained recovery sweep (test #17): the HMD standby can hand pointer
    /// currency to the frozen physical mouse and leave a floating modal stranded at
    /// the pre-doff head pose. Re-assert the virtual mouse immediately (the Tick
    /// keep-alive would also catch it, but the user is looking NOW) and re-float any
    /// open modal window in front of the current head pose.
    /// </summary>
    private static void OnSessionResumed(SessionResumedEvent e)
    {
        string? mouse = VirtualMouse.ForceReassert("session resume");
        if (mouse != null)
            e.Recovered.Add(mouse);

        int refloated = ModalFallback.RefloatOpenWindows();
        if (refloated > 0)
            e.Recovered.Add($"{refloated} modal window(s) re-floated at the current head pose");
    }

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
        private readonly StatPanelSurface _statPanels = new();
        private readonly WristHud _wristHud = new();
        private readonly FlatScreen _flatScreen = new();
        private readonly SettingsPanel _settingsPanel = new();
        private readonly WorldTooltips _tooltips = new();
        private readonly DevPanels _devPanels = new();

        private void Start()
        {
            CameraInventory.Attach();
            // End-of-frame loop for the FlatScreen desktop mirror: runs AFTER Unity's
            // XR mirror-view blit, so the RT copy is what the monitor actually shows.
            StartCoroutine(EndOfFrameLoop());
        }

        private System.Collections.IEnumerator EndOfFrameLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                _flatScreen.OnEndOfFrame();
            }
        }

        private void Update()
        {
            VirtualMouse.Tick();
            InputModeGuard.Tick();
            CameraInventory.Tick();
            NonDominantHold.Tick();  // before its consumers (settings panel, flat screen)
            ModalFallback.Tick();    // before the flat screen reads ScreenWanted

            _buttons.Tick();
            for (int i = 0; i < _slotSurfaces.Length; i++)
                _slotSurfaces[i].Tick();
            _dialogs.Tick();
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
            CameraInventory.Detach();
            _buttons.Shutdown();
            for (int i = 0; i < _slotSurfaces.Length; i++)
                _slotSurfaces[i].Shutdown();
            _dialogs.Shutdown();
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
