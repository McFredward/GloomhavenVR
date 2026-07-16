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
///    (not visible in code): <c>StoryController.IsVisible</c> (the scenario story/subtitle
///    box 'UI Story Box' — decompiled StoryController.cs:85; while shown it blocks the
///    game via AddUpdateBlocker/LockProcessingAction, StoryController.cs:216-217 — the
///    hardware-test-#10 scenario-start lock), <c>LevelMessageUILayoutGroup.IsShown</c>
///    (static; tutorial/level messages — decompiled LevelMessageUILayoutGroup.cs:33) and
///    <c>UIManager.dialogPopup.IsOpen()</c> (scenario choice dialogs — decompiled
///    DialogPopup.cs:426, UIManager.cs:94). Polls are level-triggered per frame, so they
///    also cover windows that opened during loading, before the mode machine settled.
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

    // Per-source live-poll states (separate flags so every transition is attributable).
    private static bool _storyOpen;
    private static bool _levelMsgOpen;
    private static bool _dialogPopupOpen;
    private static bool _lastWant;

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
        _storyOpen = _levelMsgOpen = _dialogPopupOpen = false;
        _lastWant = false;
        ScreenWanted = false;
        VRModeStateMachine.SetAuxModal(false);
    }

    private static void OnWindow(WindowVisibilityEvent e)
    {
        // Test #10: EVERY window transition is logged at Debug — a lock caused by a
        // window this class does not know is then attributable from the log alone.
        VRLog.Debug("WorldUI", $"UIWindow {(e.Shown ? "SHOWN" : "hidden")}: '{e.Window?.name}' " +
                               $"(ID {e.Id}, scenario={VRModeStateMachine.ScenarioBoardExists}, " +
                               $"mode={VRModeStateMachine.CurrentMode}).");

        if (e.Window == null)
            return;
        if (!e.Shown)
        {
            if (Open.Remove(e.Window))
                VRLog.Info("WorldUI", $"Modal fallback: window '{e.Window.name}' (ID {e.Id}) hidden — untracked.");
            return;
        }
        if (!IsFallbackWindow(e.Id))
            return;
        // Test #10: track fallback windows EVEN outside a scenario. The scenario-start
        // story/intro windows open during loading, BEFORE the mode machine's scenario
        // signal (Choreographer alive) settles — an edge-triggered event gated on
        // ScenarioBoardExists lost them forever, and the screen never rose. Tracking is
        // unconditional; the SCENARIO gate is applied level-triggered in Tick() (pre-
        // scenario Menu2D auto-shows the full screen anyway, so want stays false there).
        if (Open.Add(e.Window))
            VRLog.Info("WorldUI", $"MODAL FALLBACK: window '{e.Window.name}' (ID {e.Id}) opened without a " +
                                  $"VR conversion (scenario={VRModeStateMachine.ScenarioBoardExists}) → " +
                                  "ModalUI + flat screen while it stays open in a scenario.");
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
        // own state (UIWindow.cs:317). Deliberately NOT cleared outside scenarios
        // (test #10): windows opened during the loading transition must survive into
        // the scenario, where they become the lock.
        if (Open.Count > 0)
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

        // ID-less deadlockers (scene-serialized UIWindowIDs) — cheap live polls, level-
        // triggered every frame so they cover the loading/early-scenario transition too:
        //
        // 1. StoryController.IsVisible (test #10 — THE scenario-start lock: the story/
        //    subtitle box "UI Story Box" on 'Story Canvas'. Decompiled StoryController.cs:85
        //    `public bool IsVisible => window.IsVisible`; while shown it BLOCKS the game:
        //    StoryController.cs:216-217 AddUpdateBlocker + LockProcessingAction — invisible
        //    in VR = hard deadlock).
        // 2. LevelMessageUILayoutGroup.IsShown (static; tutorial/level messages incl. the
        //    'UILevelMessageBoxFixed' box — decompiled LevelMessageUILayoutGroup.cs:33).
        // 3. UIManager.dialogPopup.IsOpen() (scenario choice dialogs — DialogPopup.cs:426).
        bool story = false, levelMsg = false, dialog = false;
        if (WorldUIConfig.ConversionActive)
        {
            story = Singleton<StoryController>.IsInitialized
                    && Singleton<StoryController>.Instance.IsVisible;
            levelMsg = LevelMessageUILayoutGroup.IsShown;
            UIManager manager = UIManager.Instance;
            dialog = manager != null && manager.dialogPopup != null && manager.dialogPopup.IsOpen();
        }
        LogPollTransition(ref _storyOpen, story, "story box (StoryController.IsVisible)");
        LogPollTransition(ref _levelMsgOpen, levelMsg, "level message (LevelMessageUILayoutGroup.IsShown)");
        LogPollTransition(ref _dialogPopupOpen, dialog, "dialog popup (UIManager.dialogPopup.IsOpen)");

        bool want = inScenario && (Open.Count > 0 || story || levelMsg || dialog);
        if (want != _lastWant)
        {
            _lastWant = want;
            VRLog.Info("WorldUI", $"MODAL FALLBACK {(want ? "ASSERTED" : "RELEASED")}: " +
                                  $"windows={Open.Count}, story={story}, levelMsg={levelMsg}, " +
                                  $"dialogPopup={dialog}, scenario={inScenario}, " +
                                  $"mode={VRModeStateMachine.CurrentMode} → " +
                                  $"{(want ? "ModalUI + flat screen" : "screen released")}.");
        }
        ScreenWanted = want;
        VRModeStateMachine.SetAuxModal(want); // idempotent
    }

    private static void LogPollTransition(ref bool state, bool now, string what)
    {
        if (now == state)
            return;
        state = now;
        VRLog.Info("WorldUI", now
            ? $"MODAL FALLBACK poll: {what} OPEN → ModalUI + flat screen until it closes."
            : $"Modal fallback poll: {what} closed.");
    }
}
