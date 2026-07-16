using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// CATCH-ALL MODAL FALLBACK (P6, hardware test #8 critical fix): during a scenario the
/// game opens 2D windows — events, tutorials, take-damage choices, rewards, the ESC
/// menu, confirmation variants not owned by the P3c <see cref="Surfaces.DialogSurface"/> —
/// that are INVISIBLE in VR (the head camera never renders screen-space UI). The game
/// then waits for a click the player cannot give: a deadlock.
///
/// Detection, three layers (whichever fires first wins; when in doubt the screen shows):
/// 1. <see cref="VREvents.WindowVisibility"/> — every <c>UIWindow</c> transition (single
///    choke-point patch, see <c>UIWindow_Transition_Patch</c>) — matched against the
///    <see cref="FallbackIds"/> set of in-scenario interactive/blocking window IDs.
/// 2. Live polls for the ID-less deadlockers whose <c>UIWindowID</c> is scene-serialized
///    (not visible in code): <c>LevelMessageUILayoutGroup.IsShown</c> (static; tutorial/
///    story level messages — decompiled LevelMessageUILayoutGroup.cs:33) and
///    <c>UIManager.dialogPopup.IsOpen()</c> (scenario choice dialogs — decompiled
///    DialogPopup.cs:426, UIManager.cs:94).
/// 3. The pre-existing <c>UIManager.ToggleLockUI</c> observation (UiLockChanged →
///    ModalUI) still covers everything that locks the UI outright.
///
/// While any of these hold in a scenario, this class asserts
/// <see cref="VRModeStateMachine.SetAuxModal"/> (mode → ModalUI) and
/// <see cref="ScreenWanted"/> (the <see cref="FlatScreen"/> shows the full 2D composite
/// with the ray pointer — the dialog is GUARANTEED visible and clickable there). When
/// the window closes, both drop and the mode machine returns to the previous flow mode
/// by composition. Every trigger and release is logged with window ID + name.
///
/// The last line of defense is the manual chord ([WorldUI] ManualScreenChord,
/// <see cref="FlatScreen"/>): hold the non-dominant A/X to force the screen regardless
/// of any detection.
///
/// CONVERTED vs FALLBACK window sets — see docs/TESTING-P3C.md (P6 section). Converted/
/// passive windows (ConfirmationBox → DialogSurface, ActorStatPanel /
/// EnemyCurrentTurnStatPanel → StatPanelSurface, CombatLog panel, CardHolder → Cards
/// module, QuestTracker/MapObjectiveManager HUD, hover popups TrapInfoPanel /
/// DoorInfoPanel / MapNodeInfoPanel) deliberately do NOT trigger the screen.
/// </summary>
internal static class ModalFallback
{
    /// <summary>
    /// Window IDs that demand user interaction when opened during a scenario and have
    /// no world-space VR conversion → only the flat screen makes them operable.
    /// IDs verified against decompiled GH.Runtime/UIWindowID.cs (45 members).
    /// </summary>
    private static readonly HashSet<UIWindowID> FallbackIds = new()
    {
        // Scenario flow blockers (story/event/tutorial/choice popups).
        UIWindowID.EventsPanel,
        UIWindowID.Message,
        UIWindowID.HelpBox,
        UIWindowID.TextInfoPanel,
        UIWindowID.IntroductionScreen,
        UIWindowID.RewardsPanel,
        UIWindowID.ResultsPanel,
        UIWindowID.TakeDamagePanel,
        UIWindowID.DurabilityPanel,
        UIWindowID.QuestPopup,
        UIWindowID.UnlockQuestPopup,
        UIWindowID.AdventureCompletionPanel,
        UIWindowID.HeroLevelUpPanel,
        // Menus reachable mid-scenario.
        UIWindowID.ESCMenu,
        UIWindowID.Options,
        UIWindowID.OptionsSubmenu,
        UIWindowID.ViceOptionsSubmenu,
        UIWindowID.DifficultyPanel,
        UIWindowID.CompendiumPanel,
        UIWindowID.PartyPanel,
        UIWindowID.EquipmentItemsPanel,
        // Unconverted confirmation variants + multiplayer panels.
        UIWindowID.MutiplayerConfirmationBox,
        UIWindowID.CharacterConfirmationBox,
        UIWindowID.MainMenuConfirmationBox,
        UIWindowID.MutiplayerHeroAssignPanel,
        UIWindowID.MutiplayerPlayerPicker,
        UIWindowID.MultiplayerFriendList,
    };

    /// <summary>Fallback windows currently open (tracked instances; pruned per tick).</summary>
    private static readonly HashSet<UIWindow> Open = new();
    private static readonly List<UIWindow> Scratch = new(8);

    private static bool _attached;
    private static bool _polledOpen;

    /// <summary>True while the flat screen must show because a fallback window is open.</summary>
    internal static bool ScreenWanted { get; private set; }

    internal static void Attach()
    {
        if (_attached)
            return;
        _attached = true;
        VREvents.WindowVisibility += OnWindow;
    }

    internal static void Detach()
    {
        if (!_attached)
            return;
        _attached = false;
        VREvents.WindowVisibility -= OnWindow;
        Open.Clear();
        _polledOpen = false;
        ScreenWanted = false;
        VRModeStateMachine.SetAuxModal(false);
    }

    private static void OnWindow(WindowVisibilityEvent e)
    {
        if (!e.Shown)
        {
            Open.Remove(e.Window);
            return;
        }
        // Pre-scenario everything is Menu2D — the full flat screen already shows.
        if (!VRModeStateMachine.ScenarioBoardExists || !WorldUIConfig.ConversionActive)
            return;
        if (!IsFallbackWindow(e.Id))
            return;
        if (Open.Add(e.Window))
            VRLog.Info("WorldUI", $"MODAL FALLBACK: window '{e.Window.name}' (ID {e.Id}) opened in " +
                                  "scenario without a VR conversion → ModalUI + flat screen until it closes.");
    }

    private static bool IsFallbackWindow(UIWindowID id)
    {
        // ConfirmationBox is normally physicalized by DialogSurface — it needs the
        // screen only when that surface is switched off.
        if (id == UIWindowID.ConfirmationBox)
            return !WorldUIConfig.Dialogs.Value;
        return FallbackIds.Contains(id);
    }

    /// <summary>
    /// Per-frame service (WorldUI driver): prune dead/closed windows, poll the ID-less
    /// deadlockers, drive the aux-modal mode input. Allocation-free steady state.
    /// </summary>
    internal static void Tick()
    {
        bool inScenario = VRModeStateMachine.ScenarioBoardExists;

        // Prune: scene unloads / ForceHideWindows can close windows without a clean
        // transition reaching us (window destroyed → no event). IsOpen is the game's
        // own state (UIWindow.cs:317).
        if (Open.Count > 0)
        {
            if (!inScenario)
            {
                Open.Clear(); // scenario ended — nothing in-scenario can be open
            }
            else
            {
                Scratch.Clear();
                foreach (UIWindow window in Open)
                {
                    if (window == null || !window.IsOpen)
                        Scratch.Add(window!);
                }
                for (int i = 0; i < Scratch.Count; i++)
                {
                    UIWindow window = Scratch[i];
                    Open.Remove(window);
                    if (window != null)
                        VRLog.Info("WorldUI", $"Modal fallback: window '{window.name}' (ID {window.ID}) " +
                                              "closed — screen released.");
                }
                Scratch.Clear();
            }
        }

        // ID-less deadlockers (scene-serialized UIWindowIDs) — cheap live polls.
        bool polled = false;
        if (inScenario && WorldUIConfig.ConversionActive)
        {
            polled = LevelMessageUILayoutGroup.IsShown;
            if (!polled)
            {
                UIManager manager = UIManager.Instance;
                polled = manager != null && manager.dialogPopup != null && manager.dialogPopup.IsOpen();
            }
        }
        if (polled != _polledOpen)
        {
            _polledOpen = polled;
            if (polled)
                VRLog.Info("WorldUI", "MODAL FALLBACK: level message / dialog popup detected " +
                                      "(LevelMessageUILayoutGroup.IsShown or UIManager.dialogPopup.IsOpen) " +
                                      "→ ModalUI + flat screen until it closes.");
            else
                VRLog.Info("WorldUI", "Modal fallback: level message / dialog popup closed — screen released.");
        }

        bool want = inScenario && (Open.Count > 0 || polled);
        ScreenWanted = want;
        VRModeStateMachine.SetAuxModal(want); // idempotent
    }
}
