using System.Collections.Generic;
using GloomhavenVR.Core.Events;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and
// reasoning: FlatScreen.1.Core.cs). The nine parts concatenate back into the
// original member order — including the two NESTED types (DecisionDock in part 2,
// WindowPanel in part 3), which are ordered the same way.
//
// Tick() is in part 4 and stays WHOLE. It is a numbered step sequence and the
// registry cites its steps by name ("step 5b"); its statement order is
// load-bearing in at least four entries. Do not split it across files.

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
///    LevelMessageUILayoutGroup.cs:33; NOT trusted alone: the flag's end-of-frame reset
///    clobbers a same-frame re-show, so the poll ORs in the group windows' own
///    IsOpen/IsVisible and debounces the close — tutorial deadlock #2, see the poll in
///    part 4 and the helpers in part 7) and <c>UIManager.dialogPopup.IsOpen()</c>
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
/// While any of these hold in a scenario, this class makes the window operable, per
/// <c>[WorldUI] ModalStyle</c>. It does NOT assert ModalUI for every such window:
/// floating a menu and asserting <see cref="VRModeStateMachine.SetAuxModal"/> are two
/// SEPARATE wants (item 3b). `SetAuxModal` follows `wantLock`, which counts only genuine
/// BLOCKERS — a converted window that is not one of the player-reachable
/// <see cref="NonBlockingMenus"/>; see <see cref="BlockingWindowModalActive"/> for the
/// full rule. Coupling the two made the pause/Options menu invisible and blocked card
/// grabbing behind it, and was reverted the same round it was introduced. Do not
/// "simplify" this back to a blanket assert:
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
/// The manual chord ([WorldUI] ManualScreenChordSeconds, <see cref="FlatScreen"/>) stays the
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
internal static partial class ModalFallback
{
    /// <summary>Floating-window distance in front of the HMD, real meters (reading distance).</summary>
    private const float WindowDistanceMeters = 1.2f;

    /// <summary>
    /// Closer float distance for the LEVEL-MESSAGE family (tutorial boxes / help-text action
    /// strips), real meters. User report (torbogen screenshot, hardware log 2026-08-02): the
    /// tutorial windows read as "too far away" at the shared 1.2 m — they are small (a 644x90 px
    /// strip / a ~0.45 m box) and text-dense, so they get a dedicated reading distance while
    /// every other modal family keeps <see cref="WindowDistanceMeters"/> untouched.
    /// </summary>
    private const float LevelMessageDistanceMeters = 0.95f;

    /// <summary>Extra shrink on the host scale (a full-screen-wide window subtends ~60° at 1.2 m).</summary>
    private const float WindowScaleFactor = 0.7f;

    /// <summary>
    /// Item 1 (size): board-relative DEFAULT width for a floated menu, real meters — the same
    /// order as the control board, so the pause/Options menu opens at a comfortable, board-sized
    /// default instead of the ~1.3 m full-screen slab that read "too big".
    /// The widths are deliberately INDEPENDENT tunables and are NOT in sync: this one is 0.80,
    /// `PlayTray.BoardW` is 0.64. Do not "re-sync" this to a number quoted from another file.
    /// This is a REAL-world target: the diorama WorldScale cancels out (position still uses it),
    /// so table zoom does not grow/shrink it.
    /// Applied as a CAP on <see cref="WindowScaleFactor"/> — small dialogs (confirmations) keep
    /// the 0.7 factor; only windows wider than the board are shrunk to it. The user's two-hand
    /// resize (<see cref="PanelGrabHandle.MinScale"/>–<see cref="PanelGrabHandle.MaxScale"/>,
    /// 0.15×–2×) still rides on top of this smaller default.
    /// </summary>
    private const float ModalTargetWidthMeters = 0.80f;

    /// <summary>Floor for the derived per-window scale so a very wide window never collapses.</summary>
    private const float MinWindowScaleFactor = 0.15f;

    // THE RECALL CONSTANTS ARE GONE, AND THE ABSENCE IS THE INVARIANT.
    //
    // RecallOutOfViewSeconds (6 s dwell), RecallDistanceMeters (4 real m) and RecallViewMargin
    // (0.2 viewport slack) parameterised an envelope that re-placed an open, never-grabbed window
    // in front of the HMD. All three are deleted on the user's ruling of ModBuild 149 — verbatim,
    // and the whole reasoning, in the ruling block at the top of the recall's old home,
    // ModalFallback.6.MenuGuard.cs. Short version: "Die Fenster sollen einmal zu Beginn im
    // Sichtfeld spawnen (was sie tun) dann aber dort dauerhaft fest sitzen wenn sie nicht aktiv
    // verschoben werden."
    //
    // A NEW TUNABLE HERE WOULD BE A RE-ADDED RECALL. The failure the envelope guarded against — a
    // blocking window the player genuinely cannot find — is answered by the modal escape chord
    // (ModalFallback.7.Close.cs), by presence regain (ModalFallback.8.Convert.cs) and by the
    // window's own X, none of which move anything on a timer.

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
    /// The old fix floated every modal host at this DOMINANT order so it composited unambiguously
    /// on top, removing the tie. That cured the flicker and created the bug the transparency round
    /// is about: order beats distance in Unity's transparent sort, so a menu the player had left
    /// BEHIND the initiative track still painted over its portraits.
    ///
    /// <para>WHAT THIS CONSTANT IS NOW. Since the transparency round the DRAW order of every
    /// converted panel is rewritten each LateUpdate from its measured eye distance
    /// (CanvasConversion.8.Order.cs), which removes the tie WITHOUT removing perspective - two
    /// panels are never left at the same order, and the sequence is hysteresis-gated so head
    /// micro-motion cannot swap it. This value survives as the conversion TIER
    /// (<see cref="ConvertedPanel.BaseSortingOrder"/>) and still decides exactly two things:
    /// UguiPointer's cross-raycaster tie-break, which is calibrated against it and must never see
    /// the live ladder value; and the ladder's own INSERTION tie-break, i.e. what happens between
    /// two panels whose distances are indistinguishable (inside the swap margin) - there, and only
    /// there, the modal still wins, which is the "a dialog stays on top of what it is a dialog for"
    /// guarantee. Adopted nested canvases keep <c>overrideSorting</c> cleared, so they inherit the
    /// live order and stay with the host either way. World-space UI still ZTests against opaque
    /// depth, so a hand held in front still occludes the modal.</para>
    /// </summary>
    // Internal, not private: StatPanelSurface pins the at-hand info panels to this SAME tier, so an
    // indistinguishable-distance tie between an info panel and a menu is not silently decided by one
    // of them being a modal. Referencing the constant keeps that from drifting apart.
    internal const int ModalHostSortingOrder = 1000;

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
        // MID-SCENARIO SHOWCASE VERIFICATION (part 10, 2026-08-01): the treasure-chest
        // showcase (Choreographer WaitingForRewardsProcess) does NOT provably open this
        // ID. ScenarioRewardManager is abstract; the campaign subclass routes to
        // CampaignRewardsManager → UICampaignRewardWindow — a DIFFERENT UIWindow whose
        // scene-serialized ID appears nowhere in code (UIWindowID.RewardsPanel exists
        // ONLY as the enum member; no code assigns any window ID). Only the guildmaster
        // subclass reaches UIRewardsManager, the class this annotation maps to. The
        // showcase is therefore enrolled by POLL (ScenarioRewardManager.IsShown →
        // concrete window per subclass, ModalFallback.10.CatchAll.cs) — this entry stays
        // for whatever window IS serialized as RewardsPanel; the poll dedupes if they
        // coincide.
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
        //
        // ModBuild 196 — MEMBERSHIP HERE IS A CLASSIFICATION, NOT A PLACEMENT RULING, AND THIS
        // ENTRY WAS BEING READ AS BOTH. The annotation above answers the test #16/#18 question
        // ("hover surface that would self-lock, or a real interactive window?") for a set this
        // doc-comment scopes to windows that demand interaction DURING A SCENARIO. It never said
        // the equipment panel should be its own window, and the 3D map room did not exist when it
        // was written — but the enrolled poll floats whatever is listed here, and it runs BEFORE
        // the catch-all where "the parent wins" lives. So the equipment TAB of the map room's
        // character screen was the one sub-view of that screen pulled out of it and re-parented
        // onto a world host of its own, while the cards/perks/battle-goal/assembly tabs (none of
        // them listed here) correctly rendered inside it. User report, 2026-08-22 + screenshot
        // .planning/debug/items_transparenz.jpg. The entry STAYS — the classification is still
        // true and still governs the scenario and the flat game — and the placement is decided by
        // ModalFallback.7.Close.cs's RendersInsideFloatedAncestor instead, which carries the
        // decompiled + hardware-log evidence for the nesting.
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
}
