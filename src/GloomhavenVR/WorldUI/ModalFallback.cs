using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// CATCH-ALL MODAL FALLBACK (P6, hardware test #8 critical fix; P8 window-style
/// rework, test #12): during a scenario the game opens 2D windows — events,
/// tutorials, take-damage choices, rewards, the ESC menu, confirmation variants not
/// owned by the P3c <see cref="Surfaces.DialogSurface"/> — that are INVISIBLE in VR
/// (the head camera never renders screen-space UI). The game then waits for a click
/// the player cannot give: a deadlock.
///
/// Detection, three layers (whichever fires first wins; when in doubt the window shows):
/// 1. <see cref="VREvents.WindowVisibility"/> — every <c>UIWindow</c> transition (single
///    choke-point patch, see <c>UIWindow_Transition_Patch</c>) — matched against the
///    <see cref="FallbackIds"/> set of in-scenario interactive/blocking window IDs.
/// 2. Live polls for the ID-less deadlockers whose <c>UIWindowID</c> is scene-serialized
///    (not visible in code): <c>StoryController.IsVisible</c> (the scenario story/subtitle
///    box 'UI Story Box' — decompiled StoryController.cs:85; while shown it blocks the
///    game via AddUpdateBlocker/LockProcessingAction, StoryController.cs:216-217 — the
///    hardware-test-#10 scenario-start lock), <c>LevelMessageUILayoutGroup.IsShown</c>
///    (static; tutorial/level messages incl. 'UILevelMessageBoxFixed' — decompiled
///    LevelMessageUILayoutGroup.cs:33) and <c>UIManager.dialogPopup.IsOpen()</c>
///    (scenario choice dialogs — decompiled DialogPopup.cs:426, UIManager.cs:94).
///    Polls are level-triggered per frame, so they also cover windows that opened
///    during loading, before the mode machine settled. Every poll source resolves to
///    a concrete <c>UIWindow</c> instance for the window-style conversion:
///    StoryController's serialized <c>window</c> field (StoryController.cs:65-66),
///    the layout groups' <c>window</c> (GetComponent in Awake,
///    LevelMessageUILayoutGroup.cs:8/37, reached via the public
///    <c>LevelMessagesUIHandler.s_Instance.LevelMessage{Box,HelpText}LayoutGroup</c>
///    fields, LevelMessagesUIHandler.cs:25-29) and <c>DialogPopup.Window</c>
///    (public property, DialogPopup.cs:96).
/// 3. The pre-existing <c>UIManager.ToggleLockUI</c> observation (UiLockChanged →
///    ModalUI) still covers everything that locks the UI outright.
///
/// While any of these hold in a scenario, this class asserts
/// <see cref="VRModeStateMachine.SetAuxModal"/> (mode → ModalUI) and makes the window
/// operable, per <c>[WorldUI] ModalStyle</c>:
///
/// - "window" (default, P8): each open fallback window's root RectTransform is moved
///   onto a world-space host via <see cref="CanvasConversion"/> and floated in front
///   of the HMD at reading distance (~1.2 m, the DialogSurface pattern). The host is
///   registered with UguiPokeSurfaces, so the fingertip poke AND the dominant-hand
///   laser (RayUguiDriver) click the REAL uGUI elements — e.g. the story box's
///   full-area skip button ('UI Story Box' carried the IPointerClickHandler in the
///   test-#10 DirectClick log; UICharacterStoryBox.skipButton → Skip(),
///   UICharacterStoryBox.cs:44/81). On close/scene change/hot reload the window is
///   restored to its exact 2D home (CanvasConversion restore records). If a specific
///   window FAILS to convert, the reason is logged and the full flat screen rises
///   for it instead (automatic per-window fallback).
/// - "screen": pre-P8 behavior — <see cref="ScreenWanted"/> shows the full 2D
///   composite (<see cref="FlatScreen"/>) while any fallback window is open.
///
/// The manual chord ([WorldUI] ManualScreenChord, <see cref="FlatScreen"/>) stays the
/// universal rescue: while it forces the screen, all window conversions are RELEASED
/// (a converted window would be missing from the screen's RT composite) and re-applied
/// when the chord toggles the screen off again.
///
/// MODAL ESCAPE CHORD (test #17 hard-lock guarantee): while a window FLOATS here,
/// the same non-dominant A/X hold closes the TOP modal through the game's own escape
/// path instead — see <see cref="TickEscapeChord"/>. Even under an unknown future
/// input failure, no modal can hard-lock a session: the chord runs on XR controller
/// state and the close runs on game methods, independent of the whole pointer stack.
///
/// CONVERTED vs FALLBACK window sets — see docs/TESTING-P3C.md (P6 section). Converted/
/// passive windows (ConfirmationBox → DialogSurface, ActorStatPanel /
/// EnemyCurrentTurnStatPanel → StatPanelSurface, CombatLog panel, CardHolder → Cards
/// module, QuestTracker/MapObjectiveManager HUD, hover popups TrapInfoPanel /
/// DoorInfoPanel / MapNodeInfoPanel, the passive HelpBox hint strip — test #16 —
/// and the hover prop-info cards TextInfoPanel / UIPropInfoPanel →
/// Surfaces.PropInfoSurface — test #18) deliberately do NOT trigger the fallback.
///
/// DECISION-DOCK CLAIMS (test #21, generalized test #22): a third window class
/// between "converted" and "fallback" — decision/confirmation prompts whose REAL
/// INTERACTIVE WIDGETS <see cref="Surfaces.DecisionDockSurface"/> docks in a reserved
/// zone BELOW the two cards on the control board while the window is open (the
/// <see cref="DecisionDock"/> registry). While a claim holds, the generic path stands
/// down COMPLETELY for that window: no float, no screen, and crucially NO ModalUI —
/// these prompts have follow-up flows that need the normal interactors (the
/// take-damage burn choice continues in the card fan, which the ModalUI palm-gate
/// shutdown killed in test #21; the burn-confirm dialog FOLLOWS a LoseCard pick). The
/// claim is consulted level-triggered every tick AGAINST THE WINDOW INSTANCE, so it
/// covers both ID-tracked windows (TakeDamagePanel, which STAYS in
/// <see cref="FallbackIds"/>) and the ID-less poll-tracked dialogPopup; the moment it
/// breaks (surface off, tray gone, conversion-failure grace expired) the window is
/// handled generically again — a wrongly-floated window is recoverable, a dropped one
/// is a silent deadlock (the DurabilityPanel rule).
/// </summary>
internal static class ModalFallback
{
    /// <summary>Floating-window distance in front of the HMD, real meters (reading distance).</summary>
    private const float WindowDistanceMeters = 1.2f;

    /// <summary>Extra shrink on the host scale (a full-screen-wide window subtends ~60° at 1.2 m).</summary>
    private const float WindowScaleFactor = 0.7f;

    /// <summary>
    /// THE FLICKER FIX (recurring): sortingOrder for a floated modal's host canvas.
    ///
    /// Root cause — NOT per-frame churn (the logs prove every floated modal is converted,
    /// adopted, floated and content-fit EXACTLY ONCE, its host world-rect stable until it
    /// re-opens; the prior <see cref="ApplyMenuSelectionGuard"/> focusOnMouseHover tweak was
    /// a genuine no-op, the flag was already false). It is RENDER ORDERING:
    /// <see cref="CanvasConversion.Convert"/> created every host canvas at Unity's default
    /// <c>sortingOrder = 0</c>, so during a scenario the modal shares order 0 with EVERY other
    /// world-space host (initiative track, actor bars, combat log, objectives, dialogs…).
    /// Unity depth-sorts equal-order WORLD-space canvases by camera distance, and the head
    /// micro-moves every frame (heartbeats: moved=Y). A full-screen modal is a ~32×17 m plane
    /// placed 1.2 m in front of the head that spans ~13 m of depth and OVERLAPS all those
    /// panels, so the equal-order distance tie between the modal and whatever it overlaps
    /// resolves differently frame-to-frame → the modal alternately draws in front of / behind
    /// them → the reported flicker. Small content-fit panels are spatially separated, so only
    /// full-screen modals visibly hit it (ESC menu AND the content-fit Results panel alike —
    /// different conversion paths, same order-0 tie).
    ///
    /// Fix: float every modal host at a dominant order so it composites unambiguously ON TOP,
    /// removing the tie. Adopted nested canvases keep <c>overrideSorting</c> cleared, so they
    /// inherit this order and stay ordered with the host. World-space UI still ZTests against
    /// opaque depth, so a hand held in front still occludes the modal (sortingOrder only
    /// orders transparent UI among itself). Poke/laser clicks (geometric + per-graphic
    /// raycast) and the content fit are unaffected. 1000 clears the game's own canvas orders
    /// (seen: −1, 0, 1, 40).
    /// </summary>
    private const int ModalHostSortingOrder = 1000;

    /// <summary>
    /// Window IDs that demand user interaction when opened during a scenario and have
    /// no world-space VR conversion → they float as windows (or raise the screen).
    /// IDs verified against decompiled GH.Runtime/UIWindowID.cs (45 members).
    ///
    /// TEST #18 AUDIT: every ID below was re-checked against the decompiled sources
    /// for the failure class of tests #16/#18 — a passive HOVER/info surface listed
    /// here becomes a self-sustaining ModalUI lock (ModalUI stops the board hover
    /// that is the surface's only hide path). Verdict per ID inline: MODAL = shown
    /// by game flow (turn events, menu buttons, network actions) and carrying
    /// elements the player must click (buttons/confirm) or game-blocking behavior;
    /// NONE of them is hover-shown. Panels with internal hover behavior (PartyPanel
    /// slot dimming NewPartyDisplayUI.cs:396, EquipmentItemsPanel item tooltips)
    /// only style content — the hover never calls the window's Show. The hover-
    /// driven passives stay OUT of this set: HelpBox (test #16), TextInfoPanel
    /// (test #18) and UIPropInfoPanel — the trap/hazard/difficult-terrain/quest-
    /// item card, ID TrapInfoPanel by name-match (the only class showing trap info
    /// through a UIWindow; the enum member appears nowhere else in the decompile).
    /// All four UIPropInfoPanel.Show* methods fire exclusively from hover:
    /// ShowTrap/ShowHazardousTerrain/ShowDifficultTerrain from IHoverable
    /// OnCursorEnter (UnityGameEditor{Trap,HazardousTerrain,DifficultTerrain}
    /// Prop.cs:14, Hide in OnCursorExit) and ShowQuestItem from the hover-tooltip
    /// path (WorldspaceStarHexDisplay.cs:3602). No buttons, no blockers — rendered
    /// passively by <see cref="Surfaces.PropInfoSurface"/> instead.
    /// </summary>
    private static readonly HashSet<UIWindowID> FallbackIds = new()
    {
        // Scenario flow blockers (story/event/tutorial/choice popups).
        // MODAL: UIEventPanel — event choice buttons (EventButton.cs:11, UI_SUBMIT :47).
        UIWindowID.EventsPanel,
        // MODAL: UIMessage — close/page buttons (UIMessage.cs:22-31, onClick→Hide :45),
        // queued by the MessageHandler singleton (MessageHandler.cs:30).
        UIWindowID.Message,
        // NOT UIWindowID.HelpBox (test #16 root cause of the dead card fan): the
        // HelpBox is the game's PASSIVE bottom hint strip (HelpBoxLine tooltips —
        // GUI_TOOLTIP_*, controller tips, HighlightWarning; a ~478x32 px line),
        // never interactive and never blocking. The game showed it right after hero
        // placement and it stays open indefinitely — listed here it held the mode
        // machine in ModalUI for the rest of the session: palm gate disabled (fan
        // could never open), board pick inactive (clicks dead). Passive HUD → no
        // fallback, no ModalUI.
        // NOT UIWindowID.TextInfoPanel (test #18 hard lock — the HelpBox lesson
        // repeated): 'Text Info Panel' is the game's PASSIVE hover-driven prop-info
        // popup (closed doors, chests… — UITextInfoPanel.cs:58-86 shows/hides its
        // UIWindow and nothing else; shown and hidden exclusively by the board hover
        // path, WorldspaceStarHexDisplay.cs:3574/3607/3612 via Update:408/484).
        // Listed here it was SELF-SUSTAINING: asserting ModalUI stopped our board
        // pick/hover injection, so the hover-leave path — the ONLY caller of Hide()
        // — never ran again: panel open forever → ModalUI forever (card fan gate
        // disabled, board clicks dead; log evidence test #18). It shows as a passive
        // world card instead (Surfaces.PropInfoSurface) and the game hides it itself
        // on the next hover change, which keeps running because ModalUI never rises.
        //
        // MODAL: FTUE/concept screens shown by flow (UIIntroductionManager via
        // LevelMessageUILayoutGroup; e.g. UIUnlockLocationFlowManager.cs:121).
        UIWindowID.IntroductionScreen,
        // MODAL: UIRewardsManager — UIBlackOverlay blocker (:148) + confirm-action
        // hold (:164/178), waits for the click (:190).
        UIWindowID.RewardsPanel,
        // MODAL: UIResultsManager — end-of-scenario; its IsShown gates/suppresses
        // the rest of the UI (CardsHandManager.cs:1214, BaseButtons.cs:68).
        UIWindowID.ResultsPanel,
        // MODAL: TakeDamagePanel — burn-card choice + confirm button (:60), networked
        // confirmation (Choreographer.cs:5505, SendGameAction :774). Test #21/#22:
        // normally CLAIMED by the DecisionDock (widget row docked below the cards, no
        // float/ModalUI — the burn follow-up needs the live card fan); kept here so
        // the generic float takes over whenever the claim breaks.
        UIWindowID.TakeDamagePanel,
        // UNMAPPED (audit): NO owning class anywhere in the decompile — the string
        // exists only in UIWindowID.cs, so its behavior is unprovable. Kept MODAL
        // deliberately: a wrongly-floated window is recoverable (escape chord,
        // test #17) and attributable (window transition log); a blocking dialog
        // dropped from this list would be an invisible silent deadlock.
        UIWindowID.DurabilityPanel,
        // MODAL: UIQuestPopup — confirm-travel flow (UIQuestPopup.cs:19,
        // UILoadoutManager.cs:321).
        UIWindowID.QuestPopup,
        // MODAL: unlock flow gated on its continue button (UIUnlockLocationFlow-
        // Manager.cs:31/78/142, MapChoreographer.ShowUnlockedQuests :3914).
        UIWindowID.UnlockQuestPopup,
        // MODAL (inferred — no dedicated class): adventure-end results, nearest
        // owner UINewAdventureResultsManager (a UIResultsManager subclass).
        UIWindowID.AdventureCompletionPanel,
        // MODAL: UILevelUpWindow — card pick onClick (:120), shown by flow (:166).
        UIWindowID.HeroLevelUpPanel,
        // Menus reachable mid-scenario (all flow/key-driven, fully interactive).
        // MODAL: ESCMenu — continue/options/exit… buttons (ESCMenu.cs:28-43).
        UIWindowID.ESCMenu,
        // MODAL: UIOptionsWindow — option tab windows (:188).
        UIWindowID.Options,
        // MODAL: UISubmenuGOWindow — interactive settings submenu (:62).
        UIWindowID.OptionsSubmenu,
        // MODAL (name-only mapping): same UISubmenuGOWindow family, scene variant.
        UIWindowID.ViceOptionsSubmenu,
        // MODAL: UIDifficultySelector — difficulty tabs + selection event (:99).
        UIWindowID.DifficultyPanel,
        // MODAL: CompendiumWindow — section/back buttons (:67-89/:187).
        UIWindowID.CompendiumPanel,
        // MODAL: NewPartyDisplayUI — toggle group + per-character selection (:73);
        // its slot HOVER only dims content (:396), it never Shows the window.
        UIWindowID.PartyPanel,
        // MODAL: UIPartyItemInventoryDisplay — equip/remove item slots (:136);
        // internal hover is tooltips only, never the window's Show.
        UIWindowID.EquipmentItemsPanel,
        // Unconverted confirmation variants + multiplayer panels — confirm/cancel
        // buttons each (audit: all flow/network-shown, none hover-driven).
        UIWindowID.MutiplayerConfirmationBox,   // MODAL: UIMultiplayerConfirmationBox (:132, buttons :17-23/:155-165)
        UIWindowID.CharacterConfirmationBox,    // MODAL: UIAdventureCharacterConfirmationBox (:86, UI_SUBMIT :41)
        UIWindowID.MainMenuConfirmationBox,     // MODAL: ConfirmationBox + MainMenuConfirmationBoxState (:217/:260)
        UIWindowID.MutiplayerHeroAssignPanel,   // MODAL (scene-only owner): NetworkHeroAssignService.cs:85
        UIWindowID.MutiplayerPlayerPicker,      // MODAL: UIMultiplayerSelectPlayerScreen (:311)
        UIWindowID.MultiplayerFriendList,       // MODAL: MultiplayerFriendList (:54)
    };

    /// <summary>
    /// DECISION DOCK registry (test #22, generalizes the test-#21 take-damage claim):
    /// the ID/predicate → dock map of in-scenario decision/confirmation prompts whose
    /// REAL interactive widgets <see cref="Surfaces.DecisionDockSurface"/> docks in the
    /// reserved zone BELOW the two cards on the control board. Adding a prompt is a
    /// single <see cref="Prompt"/> entry in <see cref="Prompts"/>: a name, its
    /// <c>UIWindow</c>, its open predicate, and how to isolate its actionable widget
    /// row. While a prompt is claimed the generic modal path (float + ModalUI) stands
    /// down COMPLETELY for its window — these prompts have follow-ups that need the
    /// normal interactors (the burn choice continues in the card fan). The claim is
    /// consulted level-triggered every tick against the WINDOW INSTANCE (not the ID),
    /// so it covers BOTH the ID-tracked TakeDamagePanel AND the ID-less poll-tracked
    /// dialogPopup, and the moment a claim breaks (surface off, tray gone, grace
    /// expired) the window is handled generically again — a wrongly-floated window is
    /// recoverable, a dropped one is a silent deadlock (the DurabilityPanel rule).
    ///
    /// See the report for the SYSTEMATIC dock-vs-float verdict on every scenario
    /// decision window/popup. Now wired (test #24 item 5): <c>YesNoDialog</c> — the
    /// short-rest confirmation ("Bist du sicher?"; serialized yesButton/noButton) that
    /// the game shows next to the 2D short-rest button (a HUD dialogHolder, mislocated
    /// and unpressable in VR → a deadlock); its Yes/No row now docks at the same board
    /// spot as every other confirm. Deliberately NOT dockable: <c>UIEventPanel</c> (a scroll-list of
    /// variable event options + rewards — no discrete widget row to isolate; stays
    /// floating), and every passive info popup (no choice row at all).
    /// </summary>
    internal static class DecisionDock
    {
        /// <summary>
        /// One dockable prompt. Two predicates that DIFFER on purpose (the take-damage
        /// burn flow, test #22): <see cref="IsOpen"/> — the window is OPEN — gates the
        /// CLAIM (generic float + ModalUI stand down, keeping the card fan live for the
        /// follow-up), and STAYS TRUE while the game alpha-hides the panel during the
        /// LoseCard pick + burn-confirm (TakeDamagePanel.ToggleVisibility only drives an
        /// inner CanvasGroup, not the window). <see cref="IsActive"/> — the prompt's
        /// widgets are the ones to DOCK RIGHT NOW (open AND visible AND topmost) —
        /// gates what the surface converts, so an alpha-hidden panel behind an open
        /// burn-confirm dialog is not docked over it. All delegates are cheap and
        /// allocation-free in steady state.
        /// </summary>
        internal sealed class Prompt
        {
            internal readonly string Name;
            internal readonly Func<UIWindow?> Window;
            internal readonly Func<bool> IsOpen;
            internal readonly Func<bool> IsActive;
            internal readonly Func<RectTransform?> FindRow;

            internal Prompt(string name, Func<UIWindow?> window, Func<bool> isOpen,
                Func<bool> isActive, Func<RectTransform?> findRow)
            {
                Name = name;
                Window = window;
                IsOpen = isOpen;
                IsActive = isActive;
                FindRow = findRow;
            }
        }

        /// <summary>Reused between per-tick row isolations (single active prompt — see the surface).</summary>
        private static readonly List<Transform> RowScratch = new(4);

        /// <summary>A window handed back to the generic float after the surface's grace expired.</summary>
        private static UIWindow? _gaveUp;

        // DialogPopup FIRST: it is the follow-up confirm layered ON TOP of the
        // take-damage panel, so when both windows are open it is the active prompt.
        internal static readonly Prompt[] Prompts =
        {
            // UIManager.dialogPopup (ID None — poll-tracked): the burn-confirm and the
            // short-rest lose-card confirms (CardsHandUI.cs:826/850/909/2069). Its
            // option buttons are pooled InputButtons under horizontal/verticalOptions-
            // Holder; the actionable widgets are their ExtendedButton transforms. The
            // embedded card lives under contentHolder — NOT part of the row (a parallel
            // worker renders the card); it is suppressed with the rest of the window.
            // A modal dialog is active whenever it is open (nothing layers over it).
            new("DialogPopup",
                static () => DialogPop()?.Window,
                static () => { DialogPopup? d = DialogPop(); return d != null && d.IsOpen(); },
                static () => { DialogPopup? d = DialogPop(); return d != null && d.IsOpen(); },
                static () =>
                {
                    DialogPopup? d = DialogPop();
                    if (d == null || d.Window == null)
                        return null;
                    RowScratch.Clear();
                    List<InputButton>? buttons = d.optionButtons;
                    if (buttons != null)
                    {
                        for (int i = 0; i < buttons.Count; i++)
                        {
                            InputButton ib = buttons[i];
                            if (ib == null || !ib.gameObject.activeInHierarchy)
                                continue;
                            ExtendedButton eb = ib.ExtendedButton;
                            if (eb != null)
                                RowScratch.Add(eb.transform);
                        }
                    }
                    return IsolateRow(d.Window, RowScratch);
                }),

            // TakeDamagePanel (UIWindowID TakeDamagePanel): two burn toggles + the
            // take-damage button (all serialized on the Singleton). IsOpen (claim) is
            // window-open ALONE — it must stay claimed through the whole burn flow so
            // the generic float/ModalUI never wakes and the LoseCard fan stays live —
            // but IsActive (dock) also requires the panel be VISIBLE: while the game
            // alpha-hides it (canvasGroupVisbility → 0) behind the LoseCard pick + the
            // burn-confirm dialog, its row must NOT dock over the dialog.
            new("TakeDamagePanel",
                static () => TakeDamage()?.myWindow,
                static () => { TakeDamagePanel? p = TakeDamage(); return p != null && p.myWindow != null && p.myWindow.IsOpen; },
                static () =>
                {
                    TakeDamagePanel? p = TakeDamage();
                    if (p == null || p.myWindow == null || !p.myWindow.IsOpen)
                        return false;
                    CanvasGroup? cg = p.canvasGroupVisbility; // game's inner visibility toggle
                    return cg == null || cg.alpha > 0.01f;
                },
                static () =>
                {
                    TakeDamagePanel? p = TakeDamage();
                    if (p == null || p.myWindow == null)
                        return null;
                    RowScratch.Clear();
                    if (p.burnAvailableCardsToggle != null)
                        RowScratch.Add(p.burnAvailableCardsToggle.transform);
                    if (p.burnDiscardedCardsToggle != null)
                        RowScratch.Add(p.burnDiscardedCardsToggle.transform);
                    if (p.takeDamageButton != null)
                        RowScratch.Add(p.takeDamageButton.transform);
                    return IsolateRow(p.myWindow, RowScratch);
                }),

            // YesNoDialog (ID scene-serialized, poll-tracked): the short-rest
            // confirmation ("GUI_SHORT_REST_CONFIRMATION", ShortRest.cs:100/244 — a
            // YesNoDialog shown NEXT TO the 2D short-rest button in a HUD dialogHolder,
            // mislocated + unpressable in VR → the test #24/#25 deadlock). It carries
            // serialized yesButton/noButton (ExtendedButton : Button, standard onClick
            // wired on window.onShown, YesNoDialog.cs:112-113 → OnYes/OnNoClickHandle →
            // the game's own short-rest confirm/cancel callbacks, ShortRest.cs:100-119)
            // AND a descriptionText, all children of the serialized dialog container
            // `box` (moved by YesNoDialog.Show).
            //
            // WHOLE-WINDOW DOCK (test #25 items 1b/1c — THE deadlock fix): dock the
            // ENTIRE dialog box, not the isolated yes/no row. Isolating only the common
            // ancestor of the two buttons dropped the question text (a sibling of the
            // button row under `box`) so the player could not read what they were
            // confirming (1b), AND the button-only row measured as EMPTY content — the
            // content-fit found "nothing visible", so the docked poke/laser plane
            // collapsed to a degenerate rect and the ExtendedButton clicks never landed
            // (1c, the dead Ja/Nein). `box` is a strict descendant of the window root
            // (YesNoDialog is [RequireComponent(UIWindow)] — window sits on the root,
            // box is its child), so it satisfies the surface's descendant contract,
            // brings the readable question along, and gives the fit real graphics to
            // size the interactive plane on. The buttons' onClick already fired on
            // onShown, so a laser/poke ExecuteEvents click on the docked box drives the
            // real handlers. A modal popup is active whenever its window is open
            // (window.IsPopUp, nothing layers over it); the window remainder (mislocated
            // 2D frame/backdrop) is suppressed like every docked prompt — harmless to the
            // docked box, which has been reparented out onto our host. Reached via the
            // active hand's ShortRest.yesNoDialog (CardsGameApi.ShortRestDialog).
            new("YesNoDialog",
                static () => { YesNoDialog? d = ShortRestYesNo(); return d != null ? d.window : null; },
                static () => { YesNoDialog? d = ShortRestYesNo(); return d != null && d.window != null && d.window.IsOpen; },
                static () => { YesNoDialog? d = ShortRestYesNo(); return d != null && d.window != null && d.window.IsOpen; },
                static () =>
                {
                    YesNoDialog? d = ShortRestYesNo();
                    if (d == null || d.window == null)
                        return null;
                    // Whole-window: the dialog box carries the question text + both
                    // buttons together (a strict descendant of the window root).
                    RectTransform? box = d.box;
                    if (box != null && !ReferenceEquals(box, d.window.transform)
                        && box.IsChildOf(d.window.transform))
                        return box;
                    // Fallback (box unexpectedly null): isolate the yes/no row so at
                    // least the buttons dock rather than deadlocking on a missing target.
                    RowScratch.Clear();
                    if (d.yesButton != null)
                        RowScratch.Add(d.yesButton.transform);
                    if (d.noButton != null)
                        RowScratch.Add(d.noButton.transform);
                    return IsolateRow(d.window, RowScratch);
                }),
        };

        private static TakeDamagePanel? TakeDamage() =>
            Singleton<TakeDamagePanel>.IsInitialized ? Singleton<TakeDamagePanel>.Instance : null;

        /// <summary>The active hand's short-rest confirmation YesNoDialog (test #24 item 5), or null.</summary>
        private static YesNoDialog? ShortRestYesNo() => Cards.CardsGameApi.ShortRestDialog();

        private static DialogPopup? DialogPop()
        {
            UIManager? m = UIManager.Instance;
            return m != null ? m.dialogPopup : null;
        }

        /// <summary>The decision-dock feature is live (config + conversion + in a scenario).</summary>
        private static bool FeatureEnabled =>
            WorldUIConfig.DecisionDock.Value && WorldUIConfig.ConversionActive
            && Choreographer.s_Choreographer != null;

        /// <summary>Clear a stale hand-off once its window closed, so the next prompt re-arms.</summary>
        private static void PruneGaveUp()
        {
            // Unity-null (destroyed) trips the '!= null' guard already; a live-but-closed
            // window clears here so the same prompt can re-dock on its next open.
            if (_gaveUp != null && !_gaveUp.IsOpen)
                _gaveUp = null;
        }

        /// <summary>
        /// The prompt whose widgets to dock right now: the first ACTIVE (open + visible
        /// + topmost) prompt. Skips a window the grace handed back to the generic float.
        /// DialogPopup is listed first, so a burn-confirm layered over the alpha-hidden
        /// take-damage panel wins.
        /// </summary>
        internal static Prompt? ActivePrompt()
        {
            PruneGaveUp();
            if (!FeatureEnabled)
                return null;
            for (int i = 0; i < Prompts.Length; i++)
            {
                Prompt p = Prompts[i];
                UIWindow? w = p.Window();
                if (w == null || ReferenceEquals(w, _gaveUp))
                    continue;
                if (p.IsActive())
                    return p;
            }
            return null;
        }

        /// <summary>
        /// Does the decision dock currently own this window? Consulted by
        /// <see cref="ModalFallback"/> (stand the generic path down) AND by the
        /// surface's WantConverted — so the surface docks EXACTLY what the generic
        /// path releases. Instance-based (not ID) so it covers both the ID-tracked
        /// TakeDamagePanel and the poll-tracked dialogPopup.
        /// </summary>
        internal static bool ClaimsWindow(UIWindow? window)
        {
            if (window == null || !FeatureEnabled)
                return false;
            PruneGaveUp();
            if (ReferenceEquals(window, _gaveUp))
                return false;
            for (int i = 0; i < Prompts.Length; i++)
            {
                Prompt p = Prompts[i];
                UIWindow? w = p.Window();
                if (w != null && ReferenceEquals(w, window) && p.IsOpen())
                    return true;
            }
            return false;
        }

        /// <summary>Hand a window back to the generic float (the surface's grace expired for it).</summary>
        internal static void MarkGaveUp(UIWindow window) => _gaveUp = window;

        /// <summary>Drop the hand-off state (surface shutdown / module detach).</summary>
        internal static void Reset() => _gaveUp = null;

        /// <summary>
        /// The interactive ROW: deepest common ancestor of the actionable widgets that
        /// is a STRICT descendant of the window root — found structurally, never by
        /// name. Null (→ retry, grace running) when there are no widgets, or the
        /// ancestor is the window root itself / outside it (docking that would drag the
        /// vignette + card along). A single-widget prompt lifts to the widget's layout
        /// container so a proper row (not a lone control) docks.
        /// </summary>
        internal static RectTransform? IsolateRow(UIWindow window, List<Transform> widgets)
        {
            if (window == null || widgets.Count == 0)
                return null;
            Transform? ca = null;
            for (int i = 0; i < widgets.Count; i++)
                ca = ca == null ? widgets[i] : CommonAncestor(ca, widgets[i]);
            if (ca == null)
                return null;
            if (widgets.Count == 1 && ca.parent != null)
                ca = ca.parent;
            if (ReferenceEquals(ca, window.transform) || !ca.IsChildOf(window.transform))
                return null;
            return ca as RectTransform;
        }

        /// <summary>Deepest common ancestor of two transforms (null-tolerant).</summary>
        private static Transform? CommonAncestor(Transform? a, Transform? b)
        {
            if (a == null || b == null)
                return null;
            int da = Depth(a), db = Depth(b);
            while (da > db) { a = a!.parent; da--; }
            while (db > da) { b = b!.parent; db--; }
            while (a != null && b != null && !ReferenceEquals(a, b))
            {
                a = a.parent;
                b = b.parent;
            }
            return a != null && ReferenceEquals(a, b) ? a : null;
        }

        private static int Depth(Transform t)
        {
            int d = 0;
            for (Transform? p = t.parent; p != null; p = p.parent)
                d++;
            return d;
        }
    }

    /// <summary>Fallback windows currently open (tracked instances; pruned per tick).</summary>
    private static readonly HashSet<UIWindow> Open = new();
    private static readonly List<UIWindow> Scratch = new(8);

    /// <summary>All open modal windows this tick (ID-tracked + poll sources), rebuilt per tick.</summary>
    private static readonly List<UIWindow> OpenWindows = new(8);

    /// <summary>
    /// One floated window: the game window + its world-space host. Content fitting
    /// (test #13/#14) is centralized in <see cref="CanvasConversion"/> — every
    /// pokeable host is fitted and growth-re-fitted there; the story window's
    /// narrower content root is passed via <see cref="ConvertedPanel.FitContentRoot"/>.
    /// </summary>
    private sealed class WindowPanel
    {
        public UIWindow Window = null!;
        public ConvertedPanel Panel = null!;

        /// <summary>True when this floated window is a full-screen menu (ESC / options
        /// family) — the ones whose gamepad-nav selection highlight flickers and that the
        /// selection guard applies to (P6 flicker fix, <see cref="ApplyMenuSelectionGuard"/>).</summary>
        public bool FullScreenMenu;
    }

    private static readonly List<WindowPanel> Converted = new(4);

    /// <summary>Open windows whose conversion failed → the screen covers them (retry on re-open).</summary>
    private static readonly List<UIWindow> Failed = new(2);

    private static bool _attached;

    // Per-source live-poll states (separate flags so every transition is attributable).
    private static bool _storyOpen;
    private static bool _levelMsgOpen;
    private static bool _dialogPopupOpen;
    private static bool _lastWant;

    // Modal escape chord state (test #17) — per-press latches, reset on release.
    private static bool _escapeChordFired;
    private static bool _escapeArmingLogged;

    // Full-screen-menu selection guard state (P6 flicker fix) — see ApplyMenuSelectionGuard.
    private static bool _hoverFocusSuppressed;
    private static bool _savedHoverFocus;

    /// <summary>True while the flat screen must show because a fallback window is open.</summary>
    internal static bool ScreenWanted { get; private set; }

    /// <summary>True while at least one fallback window floats as a world-space panel (P8).</summary>
    internal static bool WindowModalActive => Converted.Count > 0;

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
        RestoreMenuSelectionGuard(); // put InControl mouse-hover focus back before we drop the windows
        ReleaseAllWindows("module shutdown");
        Open.Clear();
        OpenWindows.Clear();
        Failed.Clear();
        _storyOpen = _levelMsgOpen = _dialogPopupOpen = false;
        _lastWant = false;
        _escapeChordFired = false;
        _escapeArmingLogged = false;
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
        {
            if (DecisionDock.ClaimsWindow(e.Window))
                VRLog.Info("WorldUI", $"MODAL FALLBACK: window '{e.Window.name}' (ID {e.Id}) opened — " +
                                      "CLAIMED by the control-board decision dock (no float, no ModalUI " +
                                      "while the claim holds; generic fallback resumes if it breaks).");
            else
                VRLog.Info("WorldUI", $"MODAL FALLBACK: window '{e.Window.name}' (ID {e.Id}) opened without a " +
                                      $"VR conversion (scenario={VRModeStateMachine.ScenarioBoardExists}) → " +
                                      "ModalUI + floating window (or screen) while it stays open in a scenario.");
        }
    }

    private static bool IsFallbackWindow(UIWindowID id)
    {
        // ConfirmationBox is normally physicalized by DialogSurface — it needs the
        // fallback only when that surface is switched off.
        if (id == UIWindowID.ConfirmationBox)
            return !WorldUIConfig.Dialogs.Value;
        return FallbackIds.Contains(id);
    }

    /// <summary>
    /// Per-frame service (WorldUI driver): prune dead/closed windows, poll the ID-less
    /// deadlockers, maintain the window conversions, drive the aux-modal mode input.
    /// Allocation-free steady state (WindowPanel records allocate only when a window
    /// is first converted — a rare event).
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
                                          "closed — fallback released.");
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
        UIManager? manager = null;
        if (WorldUIConfig.ConversionActive)
        {
            story = Singleton<StoryController>.IsInitialized
                    && Singleton<StoryController>.Instance.IsVisible;
            levelMsg = LevelMessageUILayoutGroup.IsShown;
            manager = UIManager.Instance;
            dialog = manager != null && manager.dialogPopup != null && manager.dialogPopup.IsOpen();
        }
        LogPollTransition(ref _storyOpen, story, "story box (StoryController.IsVisible)");
        LogPollTransition(ref _levelMsgOpen, levelMsg, "level message (LevelMessageUILayoutGroup.IsShown)");
        LogPollTransition(ref _dialogPopupOpen, dialog, "dialog popup (UIManager.dialogPopup.IsOpen)");

        // Normalize every source to concrete UIWindow instances (P8): the ID-tracked
        // set plus the poll sources' serialized windows (see class doc for the
        // decompiled field/property citations).
        OpenWindows.Clear();
        foreach (UIWindow window in Open)
        {
            if (window == null)
                continue;
            // Test #21/#22: while the decision dock claims this window, it is NOT
            // part of the generic modal path — no float, no screen, no ModalUI
            // (still tracked in Open: the claim is re-checked every tick, so a
            // broken claim hands the window back here level-triggered).
            if (DecisionDock.ClaimsWindow(window))
                continue;
            OpenWindows.Add(window);
        }
        if (story)
            AddPollWindow(Singleton<StoryController>.Instance.window);
        if (levelMsg && LevelMessagesUIHandler.s_Instance != null)
        {
            AddGroupWindow(LevelMessagesUIHandler.s_Instance.LevelMessageBoxLayoutGroup);
            AddGroupWindow(LevelMessagesUIHandler.s_Instance.LevelMessageHelpTextLayoutGroup);
        }
        // The scenario dialogPopup (burn-confirm / short-rest lose-card confirm) is
        // ID-less — it reaches the generic path ONLY through this poll. Skip it while
        // the decision dock claims it (test #22): its option-button row docks below
        // the cards instead, and standing the generic path down here is what keeps the
        // fan live for the LoseCard follow-up (no ModalUI).
        if (dialog && manager != null && manager.dialogPopup != null
            && !DecisionDock.ClaimsWindow(manager.dialogPopup.Window))
            AddPollWindow(manager.dialogPopup.Window);

        bool anyOpen = OpenWindows.Count > 0;
        bool want = inScenario && anyOpen;

        // ---- window-style conversions (P8) ------------------------------------------
        // The manual chord's full screen needs the windows back in the 2D composite;
        // style=screen and scenario exit release everything too.
        bool convertWanted = want && WorldUIConfig.ModalWindowStyle
                             && !FlatScreen.ManualScreenActive
                             && WorldUIConfig.ConversionActive;

        // 1. Release conversions whose window closed/died or that are no longer wanted.
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            bool stillOpen = convertWanted && wp.Window != null && wp.Panel.IsAlive
                             && ContainsWindow(OpenWindows, wp.Window);
            if (stillOpen)
                continue;
            Converted.RemoveAt(i);
            string name = wp.Window != null ? wp.Window.name : "<destroyed>";
            CanvasConversion.Release(wp.Panel); // restores the exact 2D home
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' released — restored to its 2D home " +
                                  $"(open={wp.Window != null && wp.Window.IsOpen}, " +
                                  $"convertWanted={convertWanted}).");
        }

        // 2. Failed windows retry only after a close/re-open (no per-frame spam).
        for (int i = Failed.Count - 1; i >= 0; i--)
        {
            if (Failed[i] == null || !ContainsWindow(OpenWindows, Failed[i]))
                Failed.RemoveAt(i);
        }

        // 3. Convert newly opened windows.
        if (convertWanted)
        {
            for (int i = 0; i < OpenWindows.Count; i++)
            {
                UIWindow window = OpenWindows[i];
                if (IsConverted(window) || ContainsWindow(Failed, window))
                    continue;
                if (!TryConvertWindow(window))
                    Failed.Add(window);
            }
        }

        // 4. Keep the floating modal clickable: the game's UI lock legitimately
        //    disables all host raycasters (CanvasConversion lock mirror), but the
        //    modal window is the one surface that must accept input while modal —
        //    same exemption the DialogSurface applies.
        for (int i = 0; i < Converted.Count; i++)
        {
            GraphicRaycaster raycaster = Converted[i].Panel.HostRaycaster;
            if (raycaster != null && !raycaster.enabled)
                raycaster.enabled = true;
        }

        // (Content fitting — test #13/#14 — is centralized in CanvasConversion.Tick:
        // every pokeable host is fitted after the show animation and periodically
        // re-fitted on content growth.)

        ApplyMenuSelectionGuard(); // P6: stop the gamepad-nav highlight flicker on a floated full-screen menu

        TickEscapeChord(); // test #17: floating modals must always be closable

        if (want != _lastWant)
        {
            _lastWant = want;
            VRLog.Info("WorldUI", $"MODAL FALLBACK {(want ? "ASSERTED" : "RELEASED")}: " +
                                  $"windows={OpenWindows.Count}, story={story}, levelMsg={levelMsg}, " +
                                  $"dialogPopup={dialog}, scenario={inScenario}, " +
                                  $"style={(WorldUIConfig.ModalWindowStyle ? "window" : "screen")}, " +
                                  $"mode={VRModeStateMachine.CurrentMode} → " +
                                  $"{(want ? "ModalUI" : "released")}.");
        }

        // Screen policy: full composite for style=screen; for style=window only the
        // windows that FAILED to convert raise it (per-window automatic fallback).
        // The manual chord path forces the screen inside FlatScreen regardless.
        ScreenWanted = want && (!WorldUIConfig.ModalWindowStyle || Failed.Count > 0);
        VRModeStateMachine.SetAuxModal(want); // idempotent — mode flow unchanged by style
    }

    // ---- full-screen-menu selection guard (P6 flicker fix) ------------------------------

    /// <summary>
    /// SECONDARY measure (NOT the flicker root cause — that is render ordering, fixed by
    /// <see cref="ModalHostSortingOrder"/>). This suppresses a distinct, narrower artefact:
    /// the gamepad-nav SELECTION highlight churning on a floated FULL-SCREEN menu (ESC /
    /// Options family). Root cause (decompiled, verified): in a scenario the game
    /// runs its gamepad UI navigation — <c>ControllerInputArea.Focus</c> selects a button
    /// via <c>EventSystem.SetSelectedGameObject</c> (ControllerInputArea.cs:240) and shows
    /// the "selected" highlight. The mod's pointer keeps the InControl input module's
    /// <c>Mouse.current</c> alive, and that module DE-selects on hover change every frame:
    /// <c>InControlInputModule.ProcessMove</c> does
    /// <c>if (focusOnMouseHover &amp;&amp; pointerEnter changed) SetSelectedGameObject(hoverHandler)</c>
    /// (InControlInputModule.cs:350-354; hoverHandler is null over the floated menu's empty
    /// area). The head-relative screen pointer sweeps the WORLD-fixed menu as the head
    /// moves (and Virtual Desktop injects host-mouse motion + the Mouse.current flip-war),
    /// so the hover target changes every frame → the selection churns null↔button → the
    /// button highlight + its LeanTween fade FLICKER. Fix: while such a menu floats,
    /// disable <c>focusOnMouseHover</c> on the live input module so the pointer can no
    /// longer churn the gamepad-nav selection. Poke and laser clicks are unaffected — they
    /// drive the real widgets through <c>ExecuteEvents</c> (RayUguiDriver / UguiPokeSurfaces),
    /// not the hover-select path. The previous value is saved and restored the moment the
    /// last full-screen menu closes (and on <see cref="Detach"/>).
    /// </summary>
    private static void ApplyMenuSelectionGuard()
    {
        bool wantSuppress = false;
        for (int i = 0; i < Converted.Count; i++)
        {
            if (Converted[i].FullScreenMenu && Converted[i].Panel.IsAlive)
            {
                wantSuppress = true;
                break;
            }
        }

        InControlInputModuleExtended? module = InControlInputModuleExtended.Instance;
        if (module == null)
        {
            // No live input module (early boot / torn down) — nothing to guard; drop the
            // suppression flag so we re-save cleanly when it returns.
            _hoverFocusSuppressed = false;
            return;
        }

        if (wantSuppress)
        {
            if (!_hoverFocusSuppressed)
            {
                _savedHoverFocus = module.focusOnMouseHover;
                module.focusOnMouseHover = false;
                _hoverFocusSuppressed = true;
                VRLog.Info("WorldUI", "MODAL MENU: full-screen menu floated — InControl mouse-hover focus " +
                                      $"disabled (was {_savedHoverFocus}) so the gamepad-nav selection highlight " +
                                      "stops flickering; poke/laser clicks (ExecuteEvents) are unaffected.");
            }
            else if (module.focusOnMouseHover)
            {
                // Belt-and-suspenders: if the game re-enabled it mid-float, pin it back off.
                module.focusOnMouseHover = false;
            }
        }
        else if (_hoverFocusSuppressed)
        {
            module.focusOnMouseHover = _savedHoverFocus;
            _hoverFocusSuppressed = false;
            VRLog.Info("WorldUI", $"MODAL MENU: full-screen menu closed — restored InControl mouse-hover focus " +
                                  $"({_savedHoverFocus}).");
        }
    }

    /// <summary>Restore the InControl mouse-hover focus if we suppressed it (module teardown).</summary>
    private static void RestoreMenuSelectionGuard()
    {
        if (!_hoverFocusSuppressed)
            return;
        InControlInputModuleExtended? module = InControlInputModuleExtended.Instance;
        if (module != null)
            module.focusOnMouseHover = _savedHoverFocus;
        _hoverFocusSuppressed = false;
    }

    // ---- modal escape chord (test #17) --------------------------------------------------

    /// <summary>
    /// Modal escape hatch (test #17 hard-lock guarantee): while a floating modal
    /// window is open, holding the non-dominant A/X to the manual-chord threshold
    /// closes the TOP (most recently floated) modal through the game's own escape
    /// path — <c>UIWindow.Escape()</c> (exactly what the ESC key runs per window,
    /// honors escapeKeyAction), falling back to the public <c>UIWindow.Hide()</c>
    /// when the window opts out of escape. Game state observes the close normally
    /// (OnHide/onHidden fire); nothing is bypassed.
    ///
    /// Chord arbitration: this consumer runs BEFORE <see cref="FlatScreen"/> in the
    /// driver order and CONSUMES the press — one press, one action. While a modal
    /// floats the chord means "close it"; the flat-screen toggle (and the
    /// settings-panel short hold) need a fresh press once no modal floats, so the
    /// universal screen rescue stays reachable.
    /// </summary>
    private static void TickEscapeChord()
    {
        if (Converted.Count == 0 || NonDominantHold.HeldSeconds <= 0f)
        {
            _escapeChordFired = false;
            _escapeArmingLogged = false;
            return;
        }
        if (NonDominantHold.Consumed || _escapeChordFired)
            return;

        float threshold = Mathf.Max(0.5f, WorldUIConfig.ManualScreenChordSeconds.Value);
        if (!_escapeArmingLogged && NonDominantHold.HeldSeconds >= threshold * 0.5f)
        {
            _escapeArmingLogged = true;
            VRLog.Info("WorldUI", $"Modal escape chord ARMING: non-dominant A/X held " +
                                  $"{NonDominantHold.HeldSeconds:F1}s with a floating modal open — " +
                                  $"keep holding to {threshold:F1}s to close the top modal window.");
        }
        if (NonDominantHold.HeldSeconds < threshold)
            return;

        _escapeChordFired = true;
        NonDominantHold.Consumed = true; // one press, one action (screen/settings skip it)
        NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
        CloseTopModal(threshold);
    }

    /// <summary>Close the top (most recently floated) open modal via the game's own path.</summary>
    private static void CloseTopModal(float heldSeconds)
    {
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            UIWindow window = Converted[i].Window;
            if (window == null || !window.IsOpen)
                continue;
            string name = window.name;
            bool escaped = false;
            try
            {
                // Game's per-window ESC path (decompiled UIWindow.cs:706): hides the
                // window when its escapeKeyAction allows it, returns whether it acted.
                escaped = window.Escape();
                if (!escaped && window.IsOpen)
                    window.Hide(); // public close entry — OnHide/onHidden fire normally
            }
            catch (Exception ex)
            {
                VRLog.Error("WorldUI", $"MODAL ESCAPE CHORD: closing '{name}' FAILED " +
                                       $"({ex.GetType().Name}: {ex.Message}).");
                return;
            }
            VRLog.Info("WorldUI", $"MODAL ESCAPE CHORD: force-closed top modal '{name}' (ID {window.ID}) " +
                                  $"via {(escaped ? "UIWindow.Escape()" : "UIWindow.Hide()")} — " +
                                  $"non-dominant A/X held {heldSeconds:F1}s.");
            return;
        }
        VRLog.Info("WorldUI", "MODAL ESCAPE CHORD: no open floating modal left to close.");
    }

    // ---- window gathering helpers (allocation-free) -------------------------------------

    private static void AddPollWindow(UIWindow? window)
    {
        if (window == null || ContainsWindow(OpenWindows, window))
            return;
        OpenWindows.Add(window);
    }

    /// <summary>Add a level-message group's UIWindow when that group is the visible one.</summary>
    private static void AddGroupWindow(LevelMessageUILayoutGroup? group)
    {
        if (group == null)
            return;
        // `window` = GetComponent<UIWindow>() in Awake (LevelMessageUILayoutGroup.cs:37,
        // RequireComponent :8); publicized field.
        UIWindow? window = group.window;
        if (window != null && (window.IsVisible || window.IsOpen))
            AddPollWindow(window);
    }

    private static bool ContainsWindow(List<UIWindow> list, UIWindow window)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], window))
                return true;
        }
        return false;
    }

    private static bool IsConverted(UIWindow window)
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            if (ReferenceEquals(Converted[i].Window, window))
                return true;
        }
        return false;
    }

    // ---- conversion ---------------------------------------------------------------------

    /// <summary>
    /// Float one fallback window in front of the HMD. Returns false (with the reason
    /// logged) when the window cannot be converted — the caller then raises the full
    /// flat screen for it instead.
    /// </summary>
    /// <summary>
    /// True when <paramref name="window"/> is one of the full-screen menus (ESC / options
    /// family) AND its root rect genuinely fills the screen. Only these are exempted from
    /// the central content fit (P6 flicker fix): they are meant to float as a whole screen,
    /// so their host must stay fixed at the window's own rect rather than being re-measured
    /// and re-centered every ~30 frames as their fade/focus animations cross the fit's
    /// alpha threshold. The ID gate keeps every other modal (confirmation boxes, message,
    /// rewards/results, the story box) on the fit path; the geometry gate guards against a
    /// non-full-screen variant of a menu ID being wrongly exempted.
    /// </summary>
    private static bool IsFullScreenMenu(UIWindowID id, RectTransform rect)
    {
        if (id != UIWindowID.ESCMenu && id != UIWindowID.Options
            && id != UIWindowID.OptionsSubmenu && id != UIWindowID.ViceOptionsSubmenu)
            return false;

        // Stretch anchors filling the parent → a full-screen root (resolution-independent).
        Vector2 aMin = rect.anchorMin;
        Vector2 aMax = rect.anchorMax;
        if (aMin.x <= 0.01f && aMin.y <= 0.01f && aMax.x >= 0.99f && aMax.y >= 0.99f)
            return true;

        // Or the rect covers (near) the whole root canvas.
        Canvas? canvas = rect.GetComponentInParent<Canvas>();
        var canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        if (canvasRect != null)
        {
            Vector2 cs = canvasRect.rect.size;
            Vector2 ws = rect.rect.size;
            if (cs.x > 1f && cs.y > 1f && ws.x >= cs.x * 0.9f && ws.y >= cs.y * 0.9f)
                return true;
        }
        return false;
    }

    private static bool TryConvertWindow(UIWindow window)
    {
        string name = window.name;
        try
        {
            var rect = window.transform as RectTransform;
            if (rect == null)
            {
                VRLog.Warn("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) has no RectTransform " +
                                      "root — cannot float it, falling back to the full flat screen.");
                return false;
            }

            // Story-box sanity: the click-to-advance UICharacterStoryBox (serialized
            // separately from the window, StoryController.cs:62-66) must live INSIDE
            // the converted subtree, or the floating panel could never advance the
            // story. Same for its skip button (UICharacterStoryBox.cs:44). While
            // here, remember it as the content root for the host-rect fit — the
            // story window root is a full-screen 1920x1080 stretch rect, the visible
            // dialog is the UICharacterStoryBox subtree.
            RectTransform? contentRoot = null;
            if (Singleton<StoryController>.IsInitialized)
            {
                StoryController sc = Singleton<StoryController>.Instance;
                if (sc != null && ReferenceEquals(sc.window, window) && sc.dialogBox != null)
                {
                    if (!sc.dialogBox.transform.IsChildOf(window.transform))
                    {
                        VRLog.Warn("WorldUI", "MODAL WINDOW: story box dialog (UICharacterStoryBox) is NOT " +
                                              "under the story window root — clicking the floating panel could " +
                                              "not advance the story; falling back to the full flat screen.");
                        return false;
                    }
                    contentRoot = sc.dialogBox.transform as RectTransform;
                }
            }

            // FLICKER FIX (P6): a FULL-SCREEN menu (ESC / options) is meant to float as a
            // whole screen in front of the player — its 1920x1080 stretch root IS the
            // content. Enrolling it in the central content-FIT is actively wrong: as its
            // LeanTween background fade + button-focus animations cross the fit's alpha
            // threshold, the measured visible-Graphic union alternates between the full
            // frame and the smaller button cluster, and each accepted fit re-centers +
            // resizes the host in place → a visible per-frame flicker/jump. Exempt these
            // windows from the fit (host stays fixed at the window's own rect); strip-style
            // windows that genuinely need the fit (story box via contentRoot below, and any
            // non-full-screen dialog) keep it.
            bool fullScreenMenu = IsFullScreenMenu(window.ID, rect);
            // FLICKER FIX: float the modal host at a dominant sortingOrder so it composites
            // ON TOP of every other order-0 world-space host instead of tying with them and
            // swapping render order as the head micro-moves — see ModalHostSortingOrder.
            ConvertedPanel? panel = CanvasConversion.Convert(rect, $"Modal_{name}", pokeable: true,
                fitContent: fullScreenMenu ? (bool?)false : null, sortingOrder: ModalHostSortingOrder,
                diagnostic: true); // FLICKER HUNT: per-frame change-gated host/child/camera diagnostics

            if (fullScreenMenu)
                VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) is a full-screen menu — " +
                                      "exempted from the per-frame content fit (fixed host rect, no flicker).");
            if (panel == null)
            {
                VRLog.Warn("WorldUI", $"MODAL WINDOW: conversion of '{name}' (ID {window.ID}) returned " +
                                      "null (target destroyed?) — falling back to the full flat screen.");
                return false;
            }

            PlaceAtHmd(panel);
            // Narrower measure root for the content fit (story window: the visible
            // UICharacterStoryBox, not the 1920x1080 stretch root). The fit itself
            // runs centrally in CanvasConversion.Tick (test #14 item 1).
            panel.FitContentRoot = contentRoot;
            Converted.Add(new WindowPanel
            {
                Window = window,
                Panel = panel,
                FullScreenMenu = fullScreenMenu,
            });
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) floated in front of the HMD " +
                                  $"({WindowDistanceMeters:F1} m, poke + laser clickable) — " +
                                  "restored to 2D when it closes.");
            return true;
        }
        catch (Exception ex)
        {
            VRLog.Error("WorldUI", $"MODAL WINDOW: conversion of '{name}' FAILED " +
                                   $"({ex.GetType().Name}: {ex.Message}) — falling back to the full " +
                                   "flat screen for this window.");
            return false;
        }
    }

    /// <summary>
    /// Presence-regained recovery (test #17): re-place every floating modal window in
    /// front of the CURRENT head pose. The user may have physically moved while the
    /// HMD was off — a modal stranded out of view is an un-dismissable lock. Returns
    /// how many windows were re-floated (for the "[Core] Session resumed" report).
    /// </summary>
    internal static int RefloatOpenWindows()
    {
        int count = 0;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!wp.Panel.IsAlive)
                continue;
            PlaceAtHmd(wp.Panel);
            count++;
        }
        return count;
    }

    /// <summary>HMD-anchored placement at reading distance (DialogSurface pattern).</summary>
    private static void PlaceAtHmd(ConvertedPanel panel)
    {
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        Vector3 pos = h.position + fwd * (WindowDistanceMeters * scale);
        // Canvas front faces -forward: point +Z away from the viewer.
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        CanvasConversion.PlaceHost(panel, pos, rot, scale * WindowScaleFactor);
    }

    private static void ReleaseAllWindows(string reason)
    {
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            string name = wp.Window != null ? wp.Window.name : "<destroyed>";
            CanvasConversion.Release(wp.Panel);
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' released ({reason}) — restored to its 2D home.");
        }
        Converted.Clear();
    }

    private static void LogPollTransition(ref bool state, bool now, string what)
    {
        if (now == state)
            return;
        state = now;
        VRLog.Info("WorldUI", now
            ? $"MODAL FALLBACK poll: {what} OPEN → ModalUI (floating window or screen) until it closes."
            : $"Modal fallback poll: {what} closed.");
    }
}
