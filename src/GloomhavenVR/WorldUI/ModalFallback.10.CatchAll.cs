using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// ModalFallback part 10 (see the split rules in ModalFallback.1.Core.cs:13-22): the
// UNKNOWN-WINDOW CATCH-ALL + the two deadlock-insurance enrollments (reward showcase,
// GlobalErrorMessage). NOTE on compile order: the ordinal filename sort lands "…10…"
// BETWEEN parts 1 and 2 ('.' < '0'), not after part 9 — which is fine, and the reason
// this part may exist at all: it contributes ONLY NEW members and no nested types, so
// the relative member order of the original parts 1-9 (the load-bearing property the
// numbering protects) is unchanged wherever this file sorts.

internal static partial class ModalFallback
{
    // =====================================================================================
    // CATCH-ALL for unknown scenario windows (deadlock INSURANCE).
    //
    // THE HOLE THIS CLOSES: FallbackIds tracks the 24 enum-known modal IDs and three polls
    // cover the known ID-less deadlockers — but any UIWindow whose UIWindowID is
    // scene-serialized to a value this class does not know (usually the enum default,
    // None) opens on the HIDDEN 2D stack while the game waits for its click: a SILENT
    // deadlock with no VR-side symptom at all. This exact class of bug shipped once
    // already (ItemCardPicker, the item-surrender flow — see CardsGameApi.OpenItemPicker).
    // Enumerating windows can never be complete against future game patches, so the
    // choke point (UIWindow_Transition_Patch sees EVERY window transition) now feeds a
    // catch-all: an unknown window that stays open in a scenario is floated generically
    // after a short grace. The DurabilityPanel rule is the design intent: a wrongly-
    // floated window is recoverable (grab bar, X, escape chord, attributable Warn log);
    // a dropped one is a silent deadlock.
    //
    // RACE HANDLING (the grace): explicit handlers must always win. A freshly shown
    // window may be claimed by the decision dock, adopted by a surface conversion or
    // picked up by the Cards item flow one or more ticks AFTER its Show transition
    // (DecisionDockSurface converts level-triggered with its own ClaimGraceSeconds;
    // the Cards driver reads the pickers in its own update). So an unknown window only
    // joins the generic open set once it has been continuously open for
    // CatchAllGraceTicks ticks AND passes every exclusion LEVEL-TRIGGERED on the tick it
    // would join — a claim/adoption that arrives later still wins, because the
    // exclusions are re-checked every tick and windows already floated release the
    // moment they leave OpenWindows (the part-4 release loop).
    // =====================================================================================

    /// <summary>Ticks an unknown window must stay open before the catch-all floats it —
    /// explicit handlers (decision dock, surfaces, Cards flows) claim within this window.</summary>
    private const int CatchAllGraceTicks = 2;

    /// <summary>Unknown shown windows → the Time.frameCount of their Show transition.</summary>
    private static readonly Dictionary<UIWindow, int> UnknownShown = new();

    /// <summary>Per-window-name Warn latch: ONE "enroll it explicitly" line per window type.</summary>
    private static readonly HashSet<string> CatchAllWarned = new();

    /// <summary>Scratch for pruning <see cref="UnknownShown"/> (allocation-free steady state).</summary>
    private static readonly List<UIWindow> UnknownScratch = new(4);

    /// <summary>
    /// IDs the catch-all must NEVER float because the mod already handles them
    /// deliberately elsewhere (the converted/passive window sets, class doc of
    /// ModalFallback.1.Core.cs). The four hover-driven passives carry post-mortems:
    /// HelpBox (test #16) and TextInfoPanel (test #18) each became a SELF-SUSTAINING
    /// ModalUI lock when treated as modal — asserting ModalUI stops the board hover
    /// that is their ONLY hide path — and TrapInfoPanel is the UIPropInfoPanel
    /// trap/hazard/terrain/quest-item hover card (all four Show* methods fire
    /// exclusively from hover, see the FallbackIds audit). DoorInfoPanel /
    /// MapNodeInfoPanel are the same hover-popup family. The remainder are windows
    /// other VR conversions own: ConfirmationBox (DialogSurface; the Dialogs=off case
    /// is already routed by IsFallbackWindow), the stat panels (StatPanelSurface),
    /// CardHolder (Cards module), QuestTracker/MapObjectiveManager (HUD conversions).
    /// Town windows (Village/Shop/EnhancementShop/…) are deliberately NOT excluded:
    /// they cannot legitimately open mid-scenario, and if one ever does, floating it
    /// is the recoverable outcome (the DurabilityPanel rule).
    /// </summary>
    private static readonly HashSet<UIWindowID> CatchAllKnownHandled = new()
    {
        UIWindowID.HelpBox,
        UIWindowID.TextInfoPanel,
        UIWindowID.TrapInfoPanel,
        UIWindowID.DoorInfoPanel,
        UIWindowID.MapNodeInfoPanel,
        UIWindowID.ConfirmationBox,
        UIWindowID.ActorStatPanel,
        UIWindowID.EnemyCurrentTurnStatPanel,
        UIWindowID.CardHolder,
        UIWindowID.QuestTracker,
        UIWindowID.MapObjectiveManager,
    };

    /// <summary>
    /// Observe EVERY window transition (called from <see cref="OnWindow"/>, one line
    /// there): track unknown shown windows so the per-tick catch-all can grace-float
    /// them. Tracking is UNCONDITIONAL like the Open set (test #10 lesson — windows
    /// opened during loading must survive into the scenario); every judgement call
    /// (scenario gate, exclusions, config) is level-triggered in
    /// <see cref="TickCatchAll"/> so nothing is lost to an edge-time race.
    /// </summary>
    private static void CatchAllObserve(UIWindow window, bool shown)
    {
        if (!shown)
        {
            UnknownShown.Remove(window);
            return;
        }
        if (IsFallbackWindow(window.ID))
            return; // the explicit path owns it
        if (!UnknownShown.ContainsKey(window))
            UnknownShown[window] = Time.frameCount;
    }

    /// <summary>
    /// Per-tick catch-all step (called from Tick after the three explicit polls, BEFORE
    /// the sticky/convert logic reads <see cref="OpenWindows"/>): prune dead/closed
    /// unknown windows, then append every eligible one to <see cref="OpenWindows"/> so
    /// the ENTIRE existing machinery treats it like any tracked modal — float via
    /// TryConvertWindow (grab bar + X), Failed → flat screen, ModalUI (unknown IDs are
    /// never in <see cref="NonBlockingMenus"/>, so they count as blocking), escape
    /// chord, release-on-close. Also runs the two explicit enrollment polls (reward
    /// showcase, GlobalErrorMessage) so their comments live next to the mechanism.
    /// </summary>
    private static void TickCatchAll(bool inScenario)
    {
        // Part 2 enrollment #2 — mid-scenario reward showcase (see AddRewardShowcaseWindow).
        AddRewardShowcaseWindow(inScenario);

        if (UnknownShown.Count == 0)
            return;

        // Prune: destroyed or game-closed windows leave the tracker (level-triggered —
        // a Hide transition already removed most in CatchAllObserve; this catches
        // scene unloads / ForceHideWindows that destroy without a clean transition).
        UnknownScratch.Clear();
        foreach (KeyValuePair<UIWindow, int> kv in UnknownShown)
        {
            if (kv.Key == null || !kv.Key.IsOpen)
                UnknownScratch.Add(kv.Key!);
        }
        for (int i = 0; i < UnknownScratch.Count; i++)
            UnknownShown.Remove(UnknownScratch[i]);
        UnknownScratch.Clear();

        // Kill-switch + scenario gate: menus/town keep current behavior (the pre-scenario
        // Menu2D mode auto-shows the full flat screen anyway). Tracking above stays live
        // so a window that opened during loading floats the moment the scenario settles.
        if (!inScenario || !WorldUIConfig.CatchAllModals.Value || !WorldUIConfig.ConversionActive)
            return;

        int now = Time.frameCount;
        foreach (KeyValuePair<UIWindow, int> kv in UnknownShown)
        {
            UIWindow window = kv.Key;
            if (window == null || now < kv.Value + CatchAllGraceTicks)
                continue; // grace: explicit handlers win same-open races
            if (!CatchAllEligible(window))
                continue;
            if (ContainsWindow(OpenWindows, window))
                continue; // already carried (e.g. its serialized ID IS a tracked one)
            OpenWindows.Add(window);
            // ONE Warn per window type — the hardware log drives future EXPLICIT
            // enrollment (add the ID/poll, then this line disappears for that window).
            if (CatchAllWarned.Add(window.name))
                VRLog.Warn("WorldUI", $"CATCH-ALL: unknown scenario window '{window.name}' " +
                                      $"(ID {window.ID}) floated — enroll it explicitly.");
        }
    }

    /// <summary>
    /// Level-triggered exclusion check, re-evaluated EVERY tick the window would join
    /// the generic set — so a claim/adoption that starts later than the grace still
    /// stands the catch-all down (and its float releases through the normal part-4
    /// release loop the moment the window leaves OpenWindows).
    /// </summary>
    private static bool CatchAllEligible(UIWindow window)
    {
        // Known-handled IDs (passives with post-mortems + surface-owned windows).
        if (CatchAllKnownHandled.Contains(window.ID))
            return false;
        // The three explicit polls resolve their own window instances — those join
        // OpenWindows through their polls (with their special handling: story-box X
        // exclusion, level-message groups), never through the catch-all.
        if (IsPollWindow(window))
            return false;
        // Decision-dock claims stand the generic path down completely (test #21/#22).
        if (DecisionDock.ClaimsWindow(window))
            return false;
        // Cards flows own the item picker window while a surrender/refresh/lose pick is
        // live (the ItemCardPicker deadlock got its OWN VR flow — item fan + banner +
        // item-use slot; floating the hidden 2D picker over it would double-handle).
        if (IsCardsOwnedItemPickerWindow(window))
            return false;
        // A window whose subtree is already adopted by ANY live conversion (a surface
        // docked its row, a host carries its root) is owned elsewhere this instant.
        if (IsAdoptedByConversion(window))
            return false;
        // Never float world-space UI: the generic float is a screen-space→world
        // conversion; a genuinely world-space window is already visible in VR.
        var rect = window.transform as RectTransform;
        if (rect == null)
            return false;
        Canvas? canvas = window.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
            return false;
        return true;
    }

    /// <summary>Is this instance one of the three explicit polls' windows (story box,
    /// level-message groups, dialogPopup)? Instance compare — their IDs are scene-serialized.</summary>
    private static bool IsPollWindow(UIWindow window)
    {
        if (Singleton<StoryController>.IsInitialized)
        {
            StoryController sc = Singleton<StoryController>.Instance;
            if (sc != null && ReferenceEquals(sc.window, window))
                return true;
        }
        LevelMessagesUIHandler? lm = LevelMessagesUIHandler.s_Instance;
        if (lm != null)
        {
            if (lm.LevelMessageBoxLayoutGroup != null
                && ReferenceEquals(lm.LevelMessageBoxLayoutGroup.window, window))
                return true;
            if (lm.LevelMessageHelpTextLayoutGroup != null
                && ReferenceEquals(lm.LevelMessageHelpTextLayoutGroup.window, window))
                return true;
        }
        UIManager? manager = UIManager.Instance;
        if (manager != null && manager.dialogPopup != null
            && ReferenceEquals(manager.dialogPopup.Window, window))
            return true;
        return false;
    }

    /// <summary>
    /// The ItemCardPicker window while the Cards module's item-surrender/refresh flow
    /// (or the reward lose-item flow) presents it — identified exactly like
    /// <c>CardsGameApi.OpenItemPicker</c>: the pickers' publicized <c>picker.window</c>
    /// (ItemCardRefreshPicker.cs:14 / ItemRewardLosePicker.cs:15, ItemCardPicker.cs:27).
    /// Both pickers are checked because either may drive the (possibly shared) picker
    /// component; while open, the Cards item fan + banner + item-use slot are the VR
    /// affordance for it.
    /// </summary>
    private static bool IsCardsOwnedItemPickerWindow(UIWindow window)
    {
        if (Singleton<ItemCardRefreshPicker>.IsInitialized)
        {
            ItemCardRefreshPicker rp = Singleton<ItemCardRefreshPicker>.Instance;
            if (rp != null && rp.picker != null && ReferenceEquals(rp.picker.window, window))
                return true;
        }
        if (Singleton<ItemRewardLosePicker>.IsInitialized)
        {
            ItemRewardLosePicker lp = Singleton<ItemRewardLosePicker>.Instance;
            if (lp != null && lp.picker != null && ReferenceEquals(lp.picker.window, window))
                return true;
        }
        return false;
    }

    /// <summary>Is the window root inside (or above) any live CanvasConversion target —
    /// i.e. some surface already physicalizes part of it this instant?</summary>
    private static bool IsAdoptedByConversion(UIWindow window)
    {
        Transform root = window.transform;
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            RectTransform target = panels[i].Target;
            if (target == null)
                continue;
            if (ReferenceEquals(target, root) || target.IsChildOf(root) || root.IsChildOf(target))
                return true;
        }
        return false;
    }

    /// <summary>Catch-all state teardown (module detach — mirrors the part-4 Detach resets).</summary>
    private static void CatchAllReset()
    {
        UnknownShown.Clear();
        CatchAllWarned.Clear();
        _rewardShowcaseOpen = false;
        ReleaseErrorFloat("module shutdown");
        ErrorModalOpen = false;
        ErrorScreenWanted = false;
        _errorOpenLogged = false;
        _errorConvertFailedLogged = false;
    }

    // =====================================================================================
    // Part 2 enrollment #1 — the MID-SCENARIO REWARD SHOWCASE (treasure chests).
    //
    // VERIFICATION RESULT (decompiled, 2026-08-01): the chest flow is Choreographer state
    // WaitingForRewardsProcess (Choreographer.cs:9600-9606 sets m_BlockClientMessage-
    // Processing=true, :2411-2432 waits) → ScenarioRewardManager.Show. ScenarioReward-
    // Manager is ABSTRACT with two mode-dependent subclasses:
    //  - CampaignScenarioRewardManager (campaign — the main mode) → CampaignRewards-
    //    Manager.ShowRewards (CampaignScenarioRewardManager.cs:21-47) → UICampaignReward-
    //    Window.Show → its own UIWindow.Show() (UICampaignRewardWindow.cs:46/62/132-139).
    //    That window's UIWindowID is SCENE-SERIALIZED and NOWHERE assigned in code —
    //    UIWindowID.RewardsPanel appears exactly once in the whole decompile (the enum
    //    member), so "this window's ID == RewardsPanel" is NOT provable from code. The
    //    FallbackIds entry (annotated UIRewardsManager) may therefore MISS this window.
    //  - GuildmasterScenarioRewardManager → UIRewardsManager.StartRewardsShowcase
    //    (GuildmasterScenarioRewardManager.cs:8-13) — the class the RewardsPanel
    //    annotation plausibly maps to (UIRewardsManager.cs:17/69/141).
    // Both windows DO pass through UIWindow.Show() → EvaluateAndTransitionToVisual-
    // State, so the transition patch sees them — but with an unknown serialized ID the
    // event path may drop them. ENROLLMENT: a poll on the game's own
    // ScenarioRewardManager.IsShown, resolving the concrete window per subclass —
    // provable from code where the ID is not. If the ID happens to BE RewardsPanel the
    // event path already carries it and AddPollWindow's dedupe makes this a no-op.
    // (The DistributeItemsRewards/DistributeGoldRewards popups never open mid-scenario:
    // CampaignScenarioRewardManager passes showAppliedEffects:false, and they are plain
    // GameObject toggles, not UIWindows — no enrollment needed.)
    // =====================================================================================

    private static bool _rewardShowcaseOpen;

    private static void AddRewardShowcaseWindow(bool inScenario)
    {
        UIWindow? win = null;
        if (inScenario && WorldUIConfig.ConversionActive
            && Singleton<ScenarioRewardManager>.IsInitialized)
        {
            ScenarioRewardManager mgr = Singleton<ScenarioRewardManager>.Instance;
            if (mgr != null && mgr.IsShown)
            {
                // Campaign chest showcase: manager (CampaignScenarioRewardManager.cs:15)
                // → rewardsWindow (CampaignRewardsManager.cs:90) → window
                // (UICampaignRewardWindow.cs:46) — all publicized.
                if (mgr is CampaignScenarioRewardManager campaign
                    && campaign.manager != null && campaign.manager.rewardsWindow != null)
                    win = campaign.manager.rewardsWindow.window;
                // Guildmaster showcase: UIRewardsManager.myWindow (UIRewardsManager.cs:69).
                else if (Singleton<UIRewardsManager>.IsInitialized)
                {
                    UIRewardsManager rm = Singleton<UIRewardsManager>.Instance;
                    if (rm != null)
                        win = rm.myWindow;
                }
            }
        }
        bool open = win != null && (win.IsOpen || win.IsVisible);
        LogPollTransition(ref _rewardShowcaseOpen, open,
            "reward showcase (ScenarioRewardManager.IsShown)");
        if (open)
            AddPollWindow(win);
    }

    // =====================================================================================
    // Part 2 enrollment #2 — GlobalErrorMessage (the Choreographer's ~230 catch blocks).
    //
    // VERIFICATION RESULT (decompiled, 2026-08-01): SceneController.GlobalErrorMessage
    // (SceneController.cs:126/136) is an ErrorMessage — a plain MonoBehaviour + IEscapable
    // (ErrorMessage.cs:22), NOT a UIWindow. It shows via gameObject.SetActive(true)
    // (ErrorMessage.cs:353 and siblings) and NEVER passes through UIWindow.EvaluateAnd-
    // TransitionToVisualState, so the Part-1 catch-all (fed by UIWindow_Transition_Patch)
    // can NOT catch it — verified, hence this dedicated poll. While it shows, the whole
    // game halts logic-level: Choreographer.Update early-outs (Choreographer.cs:2348),
    // SRL message processing stops (:1647/:3406), buttons/camera gate on ShowingMessage —
    // an error box hidden in VR = frozen game with no explanation. Dismissal is ONLY its
    // own buttons (ErrorMessage.cs:614/604 → the passed ErrorDelegate, usually
    // ErrorHandlingUnloadSceneAndLoadMainMenu), so the float carries NO mod X: Hide()
    // without the button's delegate would swallow the error and strand whatever recovery
    // the game intended. Poll reads the publicized backing FIELD _errorMessage — never
    // the GlobalErrorMessage getter, which lazily Addressables-INSTANTIATES the prefab
    // (SceneController.cs:620-631) and must not be triggered from a per-frame poll.
    // =====================================================================================

    /// <summary>True while the error box shows in a scenario → ModalUI (a genuine blocker).</summary>
    internal static bool ErrorModalOpen { get; private set; }

    /// <summary>True when the error box is open but could not be floated → raise the screen.</summary>
    internal static bool ErrorScreenWanted { get; private set; }

    private static ConvertedPanel? _errorPanel;
    private static GrabbableModal? _errorGrab;
    private static bool _errorOpenLogged;
    private static bool _errorConvertFailedLogged;

    /// <summary>
    /// Per-tick error-box service (called from Tick before the lock/screen policy):
    /// level-triggered like the other polls. Floats the ErrorMessage root directly via
    /// CanvasConversion (the window-float pattern minus UIWindow specifics); on
    /// conversion failure ErrorScreenWanted raises the full flat screen instead. Not
    /// gated by the catch-all kill-switch — this is an explicit enrollment, same tier
    /// as the story/level-message/dialog polls.
    /// </summary>
    private static void TickErrorMessage(bool inScenario)
    {
        ErrorMessage? err = null;
        SceneController controller = SceneController.Instance;
        if (controller != null)
            err = controller._errorMessage; // backing field — the getter would instantiate
        bool showing = err != null && err.ShowingMessage;

        if (!_errorOpenLogged && showing)
            VRLog.Warn("WorldUI", "MODAL FALLBACK poll: GlobalErrorMessage SHOWING — the game is " +
                                  "halted until its button is pressed; floating the error box in VR.");
        else if (_errorOpenLogged && !showing)
            VRLog.Info("WorldUI", "Modal fallback poll: GlobalErrorMessage closed.");
        _errorOpenLogged = showing;

        // The error blocks the game wherever it appears, but outside a scenario the
        // Menu2D flat screen already shows the 2D stack — scenario-only, like the polls.
        ErrorModalOpen = showing && inScenario;

        bool wantFloat = ErrorModalOpen && WorldUIConfig.ModalWindowStyle
                         && !FlatScreen.ManualScreenActive && WorldUIConfig.ConversionActive;

        if (!wantFloat || _errorPanel is { IsAlive: false })
        {
            ReleaseErrorFloat(showing ? "float no longer wanted" : "error box closed");
            // style=screen (or float impossible): the screen is the fallback visibility.
            ErrorScreenWanted = ErrorModalOpen && !wantFloat && !WorldUIConfig.ModalWindowStyle;
            if (!showing)
            {
                ErrorScreenWanted = false;
                _errorConvertFailedLogged = false;
            }
            return;
        }
        if (_errorPanel != null)
        {
            // Same one-surface-must-stay-clickable exemption as the floated windows.
            GraphicRaycaster raycaster = _errorPanel.HostRaycaster;
            if (raycaster != null && !raycaster.enabled)
                raycaster.enabled = true;
            _errorGrab?.Tick();
            return;
        }
        if (ErrorScreenWanted)
            return; // conversion already failed this open — screen path holds (retry next open)

        try
        {
            var rect = err!.transform as RectTransform;
            ConvertedPanel? panel = rect == null
                ? null
                : CanvasConversion.Convert(rect, "Modal_GlobalErrorMessage", pokeable: true,
                    sortingOrder: ModalHostSortingOrder, useModLayer: true);
            if (panel == null)
            {
                if (!_errorConvertFailedLogged)
                {
                    _errorConvertFailedLogged = true;
                    VRLog.Warn("WorldUI", "MODAL WINDOW: GlobalErrorMessage could not be floated " +
                                          "(no rect / conversion returned null) — raising the flat " +
                                          "screen for it instead.");
                }
                ErrorScreenWanted = true;
                return;
            }
            float extraScale = DeriveWindowScale(panel);
            PlaceAtHmd(panel, extraScale, Converted.Count);
            var grab = new GrabbableModal();
            grab.Build(panel, extraScale, "GlobalErrorMessage", depthMask: true);
            _errorPanel = panel;
            _errorGrab = grab;
            VRLog.Info("WorldUI", "MODAL WINDOW: GlobalErrorMessage floated in front of the HMD " +
                                  "(grabbable, NO mod X — its own buttons are the only exit; " +
                                  "Hide() would swallow the error and skip the game's recovery).");
        }
        catch (Exception ex)
        {
            if (!_errorConvertFailedLogged)
            {
                _errorConvertFailedLogged = true;
                VRLog.Error("WorldUI", "MODAL WINDOW: floating GlobalErrorMessage FAILED " +
                                       $"({ex.GetType().Name}: {ex.Message}) — raising the flat " +
                                       "screen for it instead.");
            }
            ErrorScreenWanted = true;
        }
    }

    /// <summary>Release the error-box float (restores its exact 2D home; reversible mutation).</summary>
    private static void ReleaseErrorFloat(string reason)
    {
        if (_errorPanel == null)
            return;
        _errorGrab?.Destroy();
        _errorGrab = null;
        CanvasConversion.Release(_errorPanel);
        _errorPanel = null;
        VRLog.Info("WorldUI", $"MODAL WINDOW: GlobalErrorMessage float released ({reason}) — " +
                              "restored to its 2D home.");
    }
}
