using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Physicalized UI (Phase 3c, R4 "physical interface"): canvas-conversion framework,
/// physical Ready/Undo/Skip cluster, world panels for initiative track / element
/// board / combat log / objectives, world-modal confirmation dialogs, world-space
/// monster stat panels, the transient enemy round-reveal float over the board
/// (test #20), true world-space actor bars, wrist HUD, floating 2D screen +
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

        // User request 2026-08-22: "VR mod Version soll im Hauptmenu unten links neben der
        // Versionnummer vom Spiel sichtbar sein." Deliberately ABOVE the VR-not-running early-out
        // — the main menu is reachable on the flat screen too, and there the label just keeps the
        // game's own font size. No patch, no scene query: it watches MainMenuUIManager.Instance.
        // Needs WorldUIConfig.Bind() to have run (ScreenWidth/ScreenDistance are the reading
        // geometry it reports its legibility against).
        ModVersionLabel.Attach();

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
        VRSession.Harmony?.PatchAll(typeof(UITextInfoPanel_Show_Patch)); // test #18 attribution diagnostic
        VRSession.Harmony?.PatchAll(typeof(Patches.TakeDamagePanelSafety)); // test #23 item 6: burn-two NRE/deadlock guard + MP #10a mandatory-bonus auto-use
        VRSession.Harmony?.PatchAll(typeof(Patches.InitiativeHoverCardBlock)); // MP #10b: no room-sized card on player-entry hover
        VRSession.Harmony?.PatchAll(typeof(Patches.TooltipRaiseGuard)); // 2026-08-09: no head-swept mouse tooltips on world surfaces, none at all while a beam is on a card fan
        // 2026-08-11 (ModBuild 123), RE-APPLIED 2026-08-12: world-space UI answers ONLY mod
        // pointers — no head-swept mouse hover on floated menus. The original registration and
        // the patch file were accidentally reverted one minute after landing by a parallel
        // worker's commit (8594ebd, the config-dial removal) that was based on a stale tree, so
        // the tested ModBuild 124 never carried the fix — round-2 report 2026-08-12: "manchmal
        // Tabs im Optionsmenu durch Kopfbewegungen gehighlighted [...] (wie ein mouseover)".
        VRSession.Harmony?.PatchAll(typeof(Patches.MouseWorldSurfaceCut));
        Patches.SettingsClickExemption.EnsureRegistered(); // user ruling 2026-08-02: settings menu never input-blocked (tutorial InteractabilityManager veto)
        // ModBuild 178 (3D map room phase 4): the game's own map hover raycasts the frozen map
        // camera's screen centre and would cancel the laser's hover every frame. Pure early-out,
        // keyed on MapRoomDriver.Active — inert in every other scene.
        VRSession.Harmony?.PatchAll(typeof(Patches.MapLocationSelectorGate));
        // CHEATS (temporary), 2026-09-03: the "every scenario loadable" test toggle on the Cheats
        // page. Both patches are inert while the toggle is off (a static bool, off at every start)
        // and while FFSNetwork.IsOnline. Removal: step 6 in VROptionsTab.Cheats.cs's header.
        VRSession.Harmony?.PatchAll(typeof(Patches.CQuestStateExtensions_CheckRequirements_Patch)); // CHEATS (temporary)
        VRSession.Harmony?.PatchAll(typeof(Patches.UnityGameEditorRuntime_LoadScenario_Patch)); // CHEATS (temporary)
        // User request 2026-09-03: "Bitte deaktiviere die animationen für das mouseover im
        // Kartenraum wenn ich über ein Kartensymbol hovere - an der Stelle möchte ich es nicht."
        // MapLocation.Highlight pops the symbol to 1.2x and starts a NodeHoverIndicator particle
        // effect the instant a hover begins; the postfix puts both back and leaves the highlight
        // sprite, the highlighted material, the quest preview card and the navigation alone.
        // Gated on MapRoomDriver.Active and on [MapRoom] HoverAnimation (ships off = his request),
        // so it is inert on the flat 2D map and in every other scene.
        // See MapLocationHoverAnimationGate and WorldUI/MapRoom/MapIconHoverAnimation.
        VRSession.Harmony?.PatchAll(typeof(Patches.MapLocationHoverAnimationGate));
        // ModBuild 188: the game's character-display refcount is respected by exactly ONE of the
        // four things Hide() does — beautify.enabled. isHidden, the model SetActive(false) and
        // HideAll's unload all fire for any requester, and Display's early-return path never takes
        // a reference at all. The map room's parallel windows are the first thing that ever creates
        // a second requester, so we own the repair. NOT the ModBuild 186 flicker diagnosis: that
        // rested on an inverted A-B-A test and is retracted in the patch header and in
        // RenderTargetProbe. See Character3DDisplayRefcount.
        VRSession.Harmony?.PatchAll(typeof(Patches.Character3DDisplayRefcount));
        // ModBuild 191 (user report 2026-08-21, the merchant's item mouseovers): the SECOND tooltip
        // family — UIPartyItemInventoryTooltip / UILocalTooltip / UITempleSlotTooltip / TooltipUI —
        // never touches CanvasManager.tooltipCanvas, so WorldTooltips had never heard of it. Each of
        // them ends its placement with DeltaWorldPositionToFitTheScreen against the FROZEN map
        // camera, which on a floated window is a world X/Y translation of hundreds of units. See
        // TooltipOnWindow for the whole diagnosis; these patches are the seams.
        VRSession.Harmony?.PatchAll(typeof(Patches.TooltipWindowPatches));
        VRSession.Harmony?.PatchAll(typeof(HintProducerScopePatch));
        VRSession.Harmony?.PatchAll(typeof(HintHighlightScopePatch));
        VRSession.Harmony?.PatchAll(typeof(HintRewardScopePatch));
        VRSession.Harmony?.PatchAll(typeof(HintMessageOriginPatch));
        VRSession.Harmony?.PatchAll(typeof(HintDirectConceptScopePatch));
        // ModBuild 203: the mercenary roster's own hover animation moves the slot out from under
        // a stationary VR laser (OnPointerEnter grows it and then ScrollToFit scrolls the list),
        // so enter->exit->enter closes into a loop and BOTH edges rebuild the character sheet —
        // 28 Display() calls in 1.01 s in the 202 log. The pointer-side half of the repair lives
        // in Hands.Interact.UguiPointer's exit hysteresis; this is the belt-and-braces half,
        // which skips a PreviewCharacterInfo that would redisplay the character already shown.
        // Base type on purpose: the campaign override reaches it through base. and keeps its own
        // delete-button bookkeeping. See PartyPreviewStorm.
        VRSession.Harmony?.PatchAll(typeof(Patches.PartyPreviewStorm));
        // User request 2026-08-22: "Ich möchte das ein Klick auf den Character nur den aktuell
        // ausgewählten Character für die Handkarten ändert nicht direct das Characterinfo-Sub-Menu
        // öffnet, das soll wirklich nur dann passieren, wenn man auf das entsprechende Symbol (das
        // 1. mit der abgebildeten 'Person') in der Leiste klickt." NewPartyCharacterUI.OnClick
        // selects the character (:812) and THEN force-opens the class panel (:821) whenever
        // autoOpenDefaultPanel is set. The flag is not ours to hold — GuildmasterDestinations
        // re-arms it to true once per tick for as long as the map room stands — so the patch lowers
        // it for the duration of one click only and raises it again. See CharacterClickSelectsOnly.
        VRSession.Harmony?.PatchAll(typeof(Patches.CharacterClickSelectsOnly));
        // User request 2026-08-22 #5: "ich möchte von dir, dass du im Hauptmenu das original Logo
        // damit ersetzt (so das es genauso aussieht wie jetzt nur mit dem neuen Logo)". A sprite
        // assignment on the scene's own Image under MainMenuUIManager._logo — no new GameObject,
        // so rect/anchors/canvas order/colour fades all stay as authored. The artwork is an
        // EmbeddedResource in this DLL (the asset bundle stays byte-identical on purpose).
        // See MainMenuLogoSwap.
        VRSession.Harmony?.PatchAll(typeof(Patches.MainMenuLogoSwap));
        // NO EXIT CONTROL MAY BE LEFT LATCHED (user, 2026-09-02, his SECOND deadlock in two
        // days: "Ich konnte aus dem Tutorial heraus nicht mehr ins Hauptmenu zurückkehren, da
        // das Fenster das mich normalerweise fragt nicht mehr angezeigt wurde"). UIMenuOption
        // .Select() opens with `if (isSelected) return;` and only THEN invokes the action, and
        // isSelected is cleared only by Deselect() — which the game wires as the confirmation
        // dialog's CANCEL. Any route that closes that confirmation without running the game's
        // own "Nein" leaves the row latched, and every later click on it is a silent no-op with
        // no log and no exception. The mod adds two such routes to that window (the modal X
        // plate and the escape chord), and they are the rescue for a floated window, so they
        // stay. The latch is what goes. See MenuExitLatchGuard.
        VRSession.Harmony?.PatchAll(typeof(Patches.ESCMenu_OnShow_LatchGuard_Patch));
        // A CLICK ON A CONFIRM CONTROL MUST NEVER PRODUCE NOTHING (user, 2026-09-03, his THIRD
        // deadlock in three days: "ich habe auf 'Quest verwerfen' in einem Szenario geklickt und
        // es nichts weiter erschienen. Das DARF NICHT PASSIEREN. Somit komm ich jetzt nicht aus
        // dem Szenario raus." — and, rejecting a per-route workaround: "SO etwas darf niemals
        // auftreten, auch nicht bei 'Hauptmenu'"). ConfirmationBox.ShowGenericConfirmation ran on
        // a box whose GameObject the mod's own float host had deleted two scene loads earlier,
        // threw out of an async void, and swallowed the exit whole. The CAUSE is fixed in
        // CanvasConversion (scene-pinned hosts, verified detach, no destroy over game content);
        // these three patches are the invariant that must hold anyway: a prefix diverts when the
        // box is provably dead, a finalizer diverts when it throws for any other reason, and both
        // put a mod-owned stand-in up carrying the CALLER'S OWN title, question and
        // confirm/cancel UnityActions. Every exit row of the pause menu funnels through them.
        // See ConfirmationBoxRescue.
        // Each of the three resolves its own target by SHAPE at registration time, and Harmony
        // throws when a TargetMethod() comes back null. That must never take the whole WorldUI
        // module down with it — a deadlock guard that cannot find its hook is a degraded guard,
        // not a dead mod — so the three registrations are guarded and the failure is REPORTED.
        try
        {
            VRSession.Harmony?.PatchAll(typeof(Patches.ConfirmationBox_ShowGenericConfirmation_Pair_Rescue_Patch));
            VRSession.Harmony?.PatchAll(typeof(Patches.ConfirmationBox_ShowGenericConfirmation_Single_Rescue_Patch));
            VRSession.Harmony?.PatchAll(typeof(Patches.ConfirmationBox_ShowGenericSpendConfirmation_Rescue_Patch));
        }
        catch (Exception e)
        {
            VRLog.Error(Name, "CONFIRMATION RESCUE NOT REGISTERED: a ConfirmationBox show-path hook "
                + $"could not be resolved — {e.GetType().Name}: {e.Message}. The 2026-09-03 deadlock "
                + "guard is INERT or PARTIAL for this build of the game: a confirmation raised "
                + "through the unhooked path can still fail to appear and leave a click producing "
                + "nothing. The CanvasConversion-side cause fix is unaffected and still applies.");
        }
        // User report 2026-08-22 #9: "Wenn das Fenster mit der Liste der in-Ruhestand-Charaktere
        // geöffnet wird, verschwindet das Fenster der Character-UI, das nicht geschlossen werden
        // darf." The retired-characters list is the guildmaster bar's Mercenary Log destination, and
        // MercenaryLogMode.Enter opens with NewPartyDisplayUI.PartyDisplay.Hide(this, instant: true)
        // (:33) — one explicit call, not a window-manager stacking sweep. Right for a flat screen
        // that shows one destination at a time, wrong for a room whose character UI is permanent and
        // has no X. The prefix swallows that one call whole (including its hideRequests token, so
        // nothing is left holding the panel down) while the map room stands. See
        // PartyPanelStackingHide.
        VRSession.Harmony?.PatchAll(typeof(Patches.PartyPanelStackingHide));

        VREvents.UiLockChanged += OnUiLock;
        VREvents.SessionResumed += OnSessionResumed; // doff/don recovery sweep (test #17)
        ModalFallback.Attach(); // catch-all modal fallback (P6): UIWindow visibility → ModalUI + screen

        _driverGo = new GameObject("GloomhavenVR.WorldUIDriver");
        UnityEngine.Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<WorldUIDriver>();

        VRLog.Info(Name, "WorldUI driver installed (virtual mouse, canvas conversion, " +
                         "button cluster, panels, actor bars, wrist HUD, flat screen).");

        // The [WorldUI] Master / FlatScreen kill switches are GONE (user ruling 2026-08-11):
        // an accidentally persisted false blanked the ENTIRE VR interface (test #11 — the HMD
        // showed only void + hands). The UI shell is unconditional now, so the loud
        // kill-switch-state logging that guarded that footgun is gone with them.
        VRLog.Info(Name, "UI shell active (unconditional — the former Master/FlatScreen kill " +
                         "switches were removed 2026-08-11).");
    }

    public void Shutdown()
    {
        VREvents.UiLockChanged -= OnUiLock;
        VREvents.SessionResumed -= OnSessionResumed;
        ModalFallback.Detach();
        NonDominantHold.Reset();

        if (_driverGo != null)
        {
            UnityEngine.Object.Destroy(_driverGo); // driver OnDestroy shuts every feature down
            _driverGo = null;
        }

        // ModBuild 189: BEFORE ReleaseAll, because the sampling probe restores its mip-baked
        // graphics through each panel's Target and the release is what sends those Targets home.
        // It also drains any AsyncGPUReadback still in flight and frees the mod-owned 32x32
        // downsample target — the uninstall discipline this codebase holds every RenderTexture to.
        // See RenderTargetProbe.Shutdown and PanelSamplingProbe.
        RenderTargetProbe.Shutdown();

        Patches.ConfirmationRescue.Reset(); // the stand-in must never outlive the mod
        CanvasConversion.ReleaseAll();
        WorldUIAssets.Reset();
        NativeButtonSkin.Reset();
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
        private readonly WorldSurface[] _slotSurfaces =
        {
            new InitiativeTrackSurface(),
            new ElementBoardSurface(),
            new CombatLogSurface(),
            new ObjectivesSurface(),
            // AFTER the objectives, and the order matters: ScenarioRulesSurface seats itself from
            // ObjectivesSurface.DockedHeightMeters, which the objectives dock republishes in its
            // own Place(). Ticking it second means the rules read THIS frame's anchor instead of
            // last frame's — the same reason the docked panels re-place in LateTick at all.
            new ScenarioRulesSurface(),
        };
        private readonly DialogSurface _dialogs = new();
        private readonly StatPanelSurface _statPanels = new();
        private readonly PropInfoSurface _propInfo = new();
        private readonly EnemyRevealSurface _enemyReveal = new();
        private readonly DecisionDockSurface _decisionDock = new();
        private readonly UseBarsSurface _useBars = new();
        // Flows 2-4: the doom UIAbilityCardPicker + the distribute-points popups are plain
        // GameObject windows (no UIWindow) — invisible to ModalFallback/DecisionDock, so each
        // gets its own polling surface (a silent rule-engine deadlock otherwise).
        private readonly DoomPickerSurface _doomPicker = new();
        private readonly DistributePointsSurface _distributePoints = new();
        // Flow 5: the MAP-side UIDistributeRewardManager popups (road/city event rewards, scenario
        // completion rewards, personal-quest rewards, town records). Same shape as flows 2-4 — a
        // plain GameObject window, invisible to every UIWindow-typed mechanism — but it opens
        // OUTSIDE a scenario, which is why the Choreographer term in WorldSurface.WantConverted
        // kept the existing surfaces off it. ModBuild 367's travel-event deadlock.
        private readonly DistributeRewardSurface _distributeRewards = new();
        private readonly DamageTooltipSurface _damageTooltip = new();
        private readonly DamagePreviewSurface _damagePreview = new();
        private readonly TrayControlDockSurface _trayControls = new();
        private readonly WristHud _wristHud = new();
        private readonly LoadingIndicator _loadingIndicator = new();
        private readonly FlatScreen _flatScreen = new();
        // Local self-preview mirror (Net feature, but ticked here so it works even with the
        // networking hook off — it is a purely local cosmetic, independent of the net send).
        private readonly AvatarMirror _avatarMirror = new();
        private readonly OptionsToggle _optionsToggle = new();
        private readonly WorldTooltips _tooltips = new();
        private readonly HexHintFacing _hexHintFacing = new();
        private readonly DevPanels _devPanels = new();

        /// <summary>
        /// Per-frame Update / LateUpdate steps, each run through <see cref="TickGuard"/> so
        /// a throw in ONE subsystem can never abort the rest of the frame's ticks. This is
        /// the reopen guarantee (P6): the pause-menu tap consumer (<see cref="OptionsToggle"/>,
        /// fed by <see cref="NonDominantHold"/>) sits mid-chain, so before this isolation an
        /// unhandled per-frame NullReferenceException upstream (e.g. a post-modal card-fan
        /// rebuild) silently starved it — the X tap produced nothing and no line was logged.
        /// Built once in <see cref="Start"/> (cached delegates → zero per-frame allocation).
        /// </summary>
        private (string name, Action fn)[] _updateSteps = Array.Empty<(string, Action)>();
        private (string name, Action fn)[] _lateSteps = Array.Empty<(string, Action)>();

        private void Start()
        {
            BuildTickSteps();
            CameraInventory.Attach();
            // End-of-frame loop for the FlatScreen desktop mirror: runs AFTER Unity's
            // XR mirror-view blit, so the RT copy is what the monitor actually shows.
            StartCoroutine(EndOfFrameLoop());
        }

        /// <summary>
        /// Assemble the ordered tick lists. Order is load-bearing and matches the original
        /// Update()/LateUpdate() sequence exactly (VirtualMouse → InputModeGuard →
        /// CameraInventory → NonDominantHold → ModalFallback → OptionsToggle → …).
        /// </summary>
        private void BuildTickSteps()
        {
            var update = new List<(string, Action)>
            {
                ("VirtualMouse", VirtualMouse.Tick),
                ("InputModeGuard", InputModeGuard.Tick),
                ("CameraInventory", CameraInventory.Tick),
                ("NonDominantHold", NonDominantHold.Tick),  // before its consumers (modal escape chord, flat screen, options toggle)
                // Compat feature ticked here like AvatarMirror (this driver is the mod's only
                // per-frame seam): heals a scripted level message stuck invisible by the game's
                // IsShown hide→show clobber (tutorial deadlock #2). BEFORE ModalFallback so the
                // level-message poll reads the healed state in the same tick.
                ("Compat.LevelMessageHeal", Compat.LevelMessageHeal.Tick),
                // Held mini ⇒ its initiative-track avatar is highlighted, which runs the game's
                // OWN portrait-hover display path (monster round-action preview). BEFORE the
                // tutorial step below, which reads FigureIntentPeek.Active as its "the player
                // performed the taught action" signal in the same tick.
                ("FigureIntentPeek", FigureIntentPeek.Tick),
                // The mod-owned extra VR tutorial step (show + every dismissal path). After the
                // peek, before ModalFallback so its show/dismiss is reflected by the level-message
                // poll in the same tick, exactly like a scripted message would be.
                ("Compat.TutorialGrabStep", Compat.TutorialGrabStep.Tick),
                ("ModalFallback", ModalFallback.Tick),      // before the flat screen reads ScreenWanted
                ("OptionsToggle", _optionsToggle.Tick),     // reads the settled short-tap edge (after the hold arbiters)
                ("VROptionsTab", VROptionsTab.Tick),        // after OptionsToggle: the pause menu it opens is where the tab is reached
                ("VRMenuEntry", VRMenuEntry.Tick),          // after the tab: the pause-menu row opens the window and then SELECTS that tab
                ("VRKeyboard", VRKeyboard.Tick),            // reads the settled uGUI focus, so after the windows have had their say
            };
            for (int i = 0; i < _slotSurfaces.Length; i++)
            {
                WorldSurface surface = _slotSurfaces[i];
                update.Add(($"Surface:{surface.GetType().Name}", surface.Tick));
            }
            update.Add(("DialogSurface", _dialogs.Tick));
            update.Add(("StatPanelSurface", _statPanels.Tick));
            update.Add(("PropInfoSurface", _propInfo.Tick));
            update.Add(("EnemyRevealSurface", _enemyReveal.Tick));
            update.Add(("DecisionDockSurface", _decisionDock.Tick)); // after ModalFallback.Tick
            update.Add(("UseBarsSurface", _useBars.Tick)); // after the dock: reads RowDocked for the same tick
            update.Add(("DoomPickerSurface", _doomPicker.Tick));           // flow 2: doom slot/transfer picker
            update.Add(("DistributePointsSurface", _distributePoints.Tick)); // flows 3+4: select/assign popups
            update.Add(("DistributeRewardSurface", _distributeRewards.Tick)); // flow 5: map-side reward distribution
            // After the dock in the UPDATE pass, and it must stay there: this surface can only
            // convert while the dock reports DockingTakeDamage, and the cross-surface focus roll-up
            // (PromptFocus.Flush, at the top of the dock's own Tick) is only one settled frame if
            // every decision surface reports after it. The area's top-down GEOMETRY order is applied
            // in the LateTick pass below, which writes the poses that actually render.
            update.Add(("DamageTooltipSurface", _damageTooltip.Tick));
            update.Add(("DamagePreviewSurface", _damagePreview.Tick)); // after the dock: mirrors flat HP-cost preview onto the adopted bar (bug #3)
            update.Add(("TrayControlDockSurface", _trayControls.Tick));
            update.Add(("WristHud", _wristHud.Tick));
            update.Add(("LoadingIndicator", _loadingIndicator.Tick)); // before FlatScreen: it reads the fresh suppress gate
            update.Add(("FlatScreen", _flatScreen.Tick));
            update.Add(("AvatarMirror", _avatarMirror.Tick));
            update.Add(("DevPanels", _devPanels.Tick));
            // The confirmation stand-in only ever exists while the game's own box has failed, and
            // it must stay in front of the player until he answers it — a dialog he can turn away
            // from is the same deadlock in a different shape. Cheap: one null test per frame.
            update.Add(("ConfirmationRescue", Patches.ConfirmationRescue.Tick));
            update.Add(("CanvasConversion", CanvasConversion.Tick));
            update.Add(("MrBacking", MrBacking.Tick)); // after CanvasConversion: host plates read post-fit rects
            _updateSteps = update.ToArray();

            var late = new List<(string, Action)>
            {
                // The VR menu row's SEAT (MenuRowSeat): after every Update writer the game has —
                // hover handlers, LeanTween, the row animators — so the rendered frame is the
                // seated one. In-canvas anchoredPositions only; it moves no host, so it sits
                // before every pose writer below.
                ("VRMenuEntry.Late", VRMenuEntry.LateTick),
                ("ActorBars", ActorBars.Tick),
                ("ActorBars.Late", ActorBars.LateTick),
                ("WorldTooltips.Late", _tooltips.LateTick),
                ("CanvasConversion.Late", CanvasConversion.LateTick), // test #21: 2D flatten after the game's tween writers
                // ModBuild 191: the LOCAL tooltip family (item card hints and friends) hangs inside
                // floated windows, which ModalFallback converts WITHOUT flatten2D — so a pooled item
                // card's leftover local rotation renders as literal 3D. Rotation + local z only; the
                // in-plane position is settled from the game's own placement calls (see
                // TooltipOnWindow). After CanvasConversion.Late so a panel that flattens itself has
                // already had its say, and before CanvasConversion.Order, which must stay last.
                ("TooltipOnWindow.Late", TooltipOnWindow.LateTick),
                // Task #4: re-face the hover hex-hint panels (PropInfoSurface docks them at a
                // fixed cached-seat pose) to the LIVE head. Runs last so nothing re-rotates the
                // host afterward; PropInfoSurface's Update placement already ran (position kept).
                ("HexHintFacing.Late", _hexHintFacing.LateTick),
            };
            // BOARD-DOCKED PLACEMENT, LAST IN THE FRAME (user, hardware MP test: "Die
            // Initiativreihenfolge über dem board und der Aufgabentext links ziehen immer ein wenig
            // nach wenn man das board hin und her schleudert. Rechts die piles sind zB wie
            // angewurzelt - das soll auch so sein ... allgemein bei allen Elementen die an dem
            // Controllboard dran sind"). Every one of these hosts POSE-FOLLOWS a PlayTray mount
            // instead of being parented under the tray (the mount-seam contract: they carry
            // game-owned canvases, which must never be destroyed by a tray teardown), so their pose
            // is a per-frame copy — and the board's own carry writer, PanelGrabHandle.Update, is an
            // ordinary MonoBehaviour Update with NO execution-order relation to this driver's
            // Update. Whenever it runs later in the frame, the copy is last frame's board pose and
            // the panel visibly drags behind a flung board; the card piles never did because they
            // are real children of the tray root. Unity runs every LateUpdate after every Update,
            // so re-placing here is ordering-proof by rule. These run AFTER the passes above (the
            // flatten/facing writers touch rotation inside the hosts, never the docked pose) and
            // write ONLY panel hosts — the board itself is never moved or rescaled from here.
            for (int i = 0; i < _slotSurfaces.Length; i++)
            {
                WorldSurface surface = _slotSurfaces[i];
                late.Add(($"Surface:{surface.GetType().Name}.Late", surface.LateTick));
            }
            // THE DECISION AREA IS WRITTEN TOP-DOWN IN THIS PASS (ModBuild 91), because that is the
            // order it is laid out in: the prompt TEXT takes the area's ceiling (a height derived
            // from the mount alone, so it depends on nothing here), the widget ROW hangs one
            // DecisionGap under the text's freshly measured bottom edge, and the use-bar drawer hangs
            // under the row's freshly measured bottom edge. Running the text last — as this list did
            // while the area was anchored bottom-up — would seat the row on LAST frame's line on a
            // moving board, which is exactly the staleness this whole LateTick pass exists to remove.
            late.Add(("DamageTooltipSurface.Late", _damageTooltip.LateTick));
            late.Add(("DecisionDockSurface.Late", _decisionDock.LateTick)); // after the text: seats off its re-placed bottom
            late.Add(("UseBarsSurface.Late", _useBars.LateTick)); // after the dock: reads its re-placed row edge
            late.Add(("TrayControlDockSurface.Late", _trayControls.LateTick));
            // MIP BAKE ARRIVALS (ModBuild 192). A floated panel's art is addressable-loaded on hover:
            // the game creates the Image DISABLED, the load lands later, and the sprite is assigned
            // from a sync-context continuation in Update. Swapping it for the mipmapped copy HERE —
            // after every Update, before uGUI builds this frame's canvas — is what makes the shop
            // item card's FIRST rendered frame the clean one ("1 Sekunde mit dem starken aliasing",
            // ModBuild 191). It moves no host, so its place before the transparency round is free.
            // THIS IS THE NATURAL OWNER: PanelMipBake also self-installs a hidden LateUpdate pump for
            // the scenario path, and TickArrivals guards on the frame number, so the two can never
            // double-swap. Before 192 the ONLY thing baking a floated window was PanelSamplingProbe —
            // a measuring instrument, on a 30-frame scan, blind to inactive graphics by construction.
            // FRAME-ORDER WorldUIModule.LateTail [PanelMipBake.TickArrivals, GrabBarTween.TickAll, CanvasConversion.TickPanelOrder]
            //   Locked (.planning/refactor/FRAME-ORDER.lock) because the tween's place is an
            //   ordering, not a preference: it must run AFTER every writer of a grab bar's target
            //   (GrabbableModal.SyncBar in ModalFallback's Update tick, SurfaceGrabBar.SyncBar in the
            //   surfaces' ticks, and the surfaces' own LateTicks above) and BEFORE the transparency
            //   round, so the rod that is drawn this frame is the eased one and nothing measures a
            //   half-moved rod. Moving it ABOVE the surfaces would present last frame's target.
            late.Add(("PanelMipBake.Arrivals", PanelMipBake.TickArrivals));
            // GRAB BAR TRANSITIONS (2026-09-03, "es ploppt"). Every writer of a window's grab bar
            // sets a TARGET on GrabBarTween during Update; this is the one step that moves the
            // DRAWN rod — its root, its length, its laser capsule and the palm zone — toward it.
            // After every writer, before the transparency round; it moves no host.
            late.Add(("GrabBarTween.Late", GrabBarTween.TickAll));
            // TRANSPARENCY ROUND, and it must stay LAST. CanvasConversion.TickPanelOrder assigns
            // every converted panel's draw order from its measured eye distance (far = painted
            // first), which is what makes panels occlude each other by perspective now that none of
            // them writes depth. It reads panel POSES, and every board-docked surface above re-places
            // its host in its own LateTick — measuring before them would order the panels from last
            // frame's geometry. Nothing after it may move a host.
            late.Add(("CanvasConversion.Order", CanvasConversion.TickPanelOrder));
            _lateSteps = late.ToArray();
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

        // ROUND 7: the tick steps run inside a marked FRAME PHASE. On a MultiPass rig the only safe
        // moment to change a panel's visibility is the main-thread phase — both eye passes render
        // after every Update and LateUpdate — and `Camera.current` alone cannot prove we are in it
        // (Unity leaves it pointing at the last camera that rendered, which produced a false
        // one-eye alarm on ModBuild 23). Being inside these markers IS that proof; see
        // CanvasConversion.BeginFramePhase.
        private void Update()
        {
            CanvasConversion.BeginFramePhase("Update");
            try
            {
                var steps = _updateSteps;
                for (int i = 0; i < steps.Length; i++)
                    TickGuard.Run(steps[i].name, steps[i].fn, "WorldUI");
            }
            finally
            {
                CanvasConversion.EndFramePhase();
            }
        }

        private void LateUpdate()
        {
            CanvasConversion.BeginFramePhase("LateUpdate");
            try
            {
                var steps = _lateSteps;
                for (int i = 0; i < steps.Length; i++)
                    TickGuard.Run(steps[i].name, steps[i].fn, "WorldUI");
            }
            finally
            {
                CanvasConversion.EndFramePhase();
            }
        }

        /// <summary>
        /// EVERY STEP IS ISOLATED, because this method is nothing but restore contracts and it
        /// used to be one unguarded run: a single throw in any Shutdown() — and a teardown is
        /// exactly where a stale Unity reference bites — silently skipped every restore BELOW it.
        /// The tail is the expensive half (MrBacking puts every opacified alpha back, ActorBars
        /// releases the adopted bars), so a throw at, say, _flatScreen.Shutdown() left the game's
        /// UI permanently modified with no line in the log naming the cause. TickGuard isolates
        /// the throw, names the step and logs its stack — the same contract the Update/LateUpdate
        /// passes above already run under. ORDER IS PRESERVED VERBATIM; only isolation is added.
        /// </summary>
        private void OnDestroy()
        {
            TickGuard.Run("WorldUI.Shutdown.CameraInventory", CameraInventory.Detach, "WorldUI");
            for (int i = 0; i < _slotSurfaces.Length; i++)
            {
                WorldSurface surface = _slotSurfaces[i];
                TickGuard.Run($"WorldUI.Shutdown.Surface:{surface.GetType().Name}",
                    surface.Shutdown, "WorldUI");
            }
            TickGuard.Run("WorldUI.Shutdown.Dialogs", _dialogs.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.StatPanels", _statPanels.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.PropInfo", _propInfo.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.EnemyReveal", _enemyReveal.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.DecisionDock", _decisionDock.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.UseBars", _useBars.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.DoomPicker", _doomPicker.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.DistributePoints", _distributePoints.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.DistributeRewards", _distributeRewards.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.DamageTooltip", _damageTooltip.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.DamagePreview", _damagePreview.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.TrayControls", _trayControls.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.WristHud", _wristHud.Shutdown, "WorldUI");
            // restores backgroundLoadingPriority defensively
            TickGuard.Run("WorldUI.Shutdown.LoadingIndicator", _loadingIndicator.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.FlatScreen", _flatScreen.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.OptionsTab", VROptionsTab.Shutdown, "WorldUI");
            // The pause-menu row is a clone parented into the GAME's menu, so it has to go
            // back with the tab -- otherwise a torn-down mod leaves a dead entry behind.
            TickGuard.Run("WorldUI.Shutdown.MenuEntry", VRMenuEntry.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.Keyboard", VRKeyboard.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.AvatarMirror", _avatarMirror.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.Tooltips", _tooltips.Shutdown, "WorldUI");
            // ModBuild 191: hand back every local rotation / local z TooltipOnWindow clamped inside
            // a floated window's tooltip subtree (same mutate-and-restore contract as the other two
            // flatteners).
            TickGuard.Run("WorldUI.Shutdown.TooltipOnWindow", TooltipOnWindow.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.HexHintFacing", _hexHintFacing.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.DevPanels", _devPanels.Shutdown, "WorldUI");
            // destroys the MR plates, restores every opacified alpha
            TickGuard.Run("WorldUI.Shutdown.MrBacking", MrBacking.Shutdown, "WorldUI");
            TickGuard.Run("WorldUI.Shutdown.ActorBars", ActorBars.ReleaseAll, "WorldUI");
        }
    }
}
