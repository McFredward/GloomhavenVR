# Window continuation audit — dev after 1.0.7

Source baseline: `e63fb284` on `dev`, ModBuild 546. This lane starts from that
integration commit, without NPC code. Native references are the read-only
`decompiled/GH.Runtime` tree in the main checkout. Implementation is split between
the integrator (generic conversion/close policy), reward and announcement lanes,
and this audit lane (message/dialog semantic close and focused regression fixtures).

## Evidence boundary

The supplied third-party report describes post-quest story/reward, level-up card
reveal, and a third unidentified window. It supplies no matching log or screenshot.
The maintainer confirmed release 1.0.7, primarily Campaign; SP/MP remains unknown.
The existing local banner is **551 / 1.1.0**, remote **500 / 1.0.0**. Neither is a
capture of this report. Findings below are source-proven failure paths; they do not
claim to identify which of them the reporting player encountered.

The census finds 152 native source files referring to UIWindow, including passive
widgets, managers and window base classes. A window's presence in that census is
not proof it is a blocking popup. Read callback bodies and the caller's wait state,
not just whether an `onHidden` subscription exists.

## Confirmed defects and integration owners

1. **Level-up card reveal uses raw input, not uGUI.**
   `UILevelUpWindow.ShowCard` enables `nextCardTracker`; `OnCardShown` advances the
   reward card sequence before native inventory choice is enabled. The tracker is
   `ClickTrackerExtended.Update`, which polls InControl mouse edges and screen-space
   rectangles. Ordinary world-space pointer delivery is insufficient. Announcement
   lane owns a scoped visible Continue affordance using the real tracker callback
   only when native reveal/animation readiness permits it. Native card selection
   and its confirmation must remain intact.
2. **Map generic confirmations have no conversion owner.**
   `UILevelUpWindow.ConfirmSelectCard` marks `isOpenConfirmationBox`, then opens the
   original `UIConfirmationBoxManager`. `DialogSurface.OnShown` rejects a null
   scenario `Choreographer`, but map uses a separate `MapChoreographer`. Meanwhile
   catch-all reserves ConfirmationBox for DialogSurface and explicit fallback only
   admits it when Dialogs is disabled. The normal map configuration therefore has
   no visible owner for this required decision. Integrator fixes map eligibility,
   attachment after Show, repeat openings, and a conversion-failure handoff to the
   generic fallback. Level-up, perks, purchases and reset confirmations share it.
3. **Generic UIMessage close bypasses two continuations.**
   `UIMessage.Awake` subscribes `Hide` to the original close event; Hide invokes
   `onClose`. Independently, `MessageHandler.Start` subscribes `ShowNext` to that
   same event. Bare UIWindow.Hide invokes neither; direct UIMessage.Hide invokes
   only the first. `MessageWindowContinuation` invokes the eligible original
   button event and consumes the generic close request, preserving pagination and
   synchronous reuse of the same UIWindow for the next queued message. A callback
   reentrancy guard is released even when native code throws. Do not set the old
   float's UserClosing flag after this callback: the next message may already own it.
4. **Dismissible DialogPopup close can bypass cleanup/cancel.**
   Native `DialogPopup.Hide` returns borrowed content, clears option controls,
   unregisters input and destroys its controller area. Native `Cancel` invokes the
   selected cancel option or action as well. Bare UIWindow.Hide does none of this.
   The new helper resolves the exact serialized Window, prefers an eligible native
   cancellation, and otherwise uses TryHide for a genuinely dismissible popup.
   `allowHide=false` remains under mandatory-decision policy; a disabled cancel is
   never permission to hide forcibly. The text overload does not clear an earlier
   content overload's cancelAction: the current contentText visibility prevents
   invoking that stale callback when the pooled dialog becomes a textual notice.
5. **Character creation must not be generically hidden.**
   `UICharacterCreatorWindow.Build` sets MapParty.IsCreatingCharacter. OnHidden
   resolves/cancels its promise but does not clear that map flag. Native Cancel and
   committed personal-quest selection own the reset. Integrator protects creator
   identity from generic X/chord Hide; the actual step's Back/Cancel stays usable.
6. **Scenario lesson close chord can bypass its continuation.**
   Conversion withholds X for the scenario LevelMessage group, but the generic
   chord originally protected introduction identity only. The group's native
   HideWindow does not call onCloseButtonPressed, and bare Hide stops the autoclose
   coroutine. Integrator protects the LevelMessageUILayoutGroup identity; the
   original layout Continue/action callback remains authoritative.
7. **Repeated interactive unknown windows can trip the HUD churn fuse.**
   The catch-all counted every unknown opening, except hover cards and mod menus,
   and could suppress its fourth opening within a minute for the whole session.
   Opening frequency cannot establish that a window is a passive HUD banner.
   Both fuse entry points now exempt mandatory decisions and native Selectable/
   ClickTracker controls, including ones hidden until an animation finishes.
   The hierarchy check runs at admission; ordinary known-HUD rejection and the
   already-floated fast path retain their cheap behavior. Existing suppression
   by name cannot hide a subsequent interactive use of that name.

## Coverage matrix

Paths below are native class/method names unless prefixed with a mod subsystem.
“Retain” means this audit found an existing semantic continuation, not that every
serialized layout was exercised in a headset.

| Family / trigger | Native continuation and constraint | VR review / decision |
|---|---|---|
| Campaign chest / campaign reward showcase | UICampaignRewardWindow.OnContinueButtonClick; rewards may chain before Confirm completes manager waiter | Reward lane reviews source context, body click/Continue, both native managers; never bare Hide |
| Guildmaster chest / event reward showcase | UIRewardsManager.Process polls processingRewards; EndProcess alone clears it and calls onProcessEnded | Keep scoped real input latch and native animation readiness; mandatory policy already identifies controller |
| Campaign quest completion reward summary | CampaignRewardsManager → campaign reward window; outside ScenarioRewardManager | Existing map owner resolution and native continuation reviewed; no new controller bypass |
| Guildmaster adventure completion reward summary | UIGuildmasterAdventureRewardsManager close button → Hide continuation | Reward lane reviews direct source and readiness |
| Level-up received cards | UILevelUpWindow.nextCardTracker → OnCardShown → ShowCard / OnFinishedShowCards | New visible native Continue route; no early skip during reveal |
| Level-up selected ability confirmation | UIConfirmationBoxManager callbacks call SelectCard or clear isOpenConfirmationBox | Fix map DialogSurface ownership; preserve genuine choice |
| Queued UIMessage / paginated notices | Original closeButton event invokes UIMessage.Hide AND MessageHandler.ShowNext | New semantic close helper; native page buttons untouched |
| Road/city encounter | EventButton → ContinueEvent / CompleteEvent; native action advances deck and peers | Existing shared request routing; mandatory, never generic hide |
| Story / map NPC dialogue / retirement story | UICharacterStoryBox.skipButton → Skip / ShowNextLine; final ShowLine invokes finish | Existing full-panel native skip and RemoteStorySync / RemoteMapStory; no synthetic global submit |
| Newly unlocked locations | UIUnlockLocationFlowManager.Continue resolves location promise; focus animation enables button | Reward lane readiness and Continue review; protect from generic Hide |
| Personal quest step completed | UICompletedPersonalQuestWindow original close enabled after completion animation; Hidden transition resolves promise | Keep original labeled Continue / Announce Retirement; animation and callback retained |
| Retirement presentation | UIRetirementManager → native retireConfirmationBox Confirm / animation / Hide callback | Native confirmation; multiplayer ready/authority sequence must run, never blindly skip |
| Character creation class/name/personal quest | Native UICharacterCreatorStep Confirm/Cancel chain; final personal quest or outer Cancel clears creation flags | Protect creator identity; retain native step input and keyboard |
| Character-created celebration | UICharacterCreatorConfirmationBox show animation calls Hide; onHidden callback | Auto-advance presentation, do not add an early gameplay commit |
| Scenario tutorial / introduction hints | LevelMessageUILayout.CloseButtonPressed or actual task autoclose; UIIntroductionManager queue | Preserve original Continue and task gates; protect group from chord bypass |
| Initial map cinematic / video | UIMapFTUEInitialStep raw tracker / native video completion | Existing NativeVideoWindow scoped video click bridge; lifecycle retains scene callback |
| Victory/defeat/retry and campaign-end results | UIResultsManager ReturnToMap/Retry; UINewAdventureResultsManager ready states and callbacks | Keep original explicit result actions, no close-as-complete; existing result conversion / scroll |
| Quest selection / travel / loadout | UIQuestPopup and UILoadoutManager ConfirmEnterScenario; UIReadyToggle authority | Original confirm/ready controls parked inside correct window; never dismiss committed choice |
| Battle goals | UIBattleGoalPickerWindow / slot select, original onHidden callback | Embedded character panel and native ownership; no random choice via generic close |
| Damage and lost-card choice | TakeDamagePanel sends TakeDamage action; native burn/card choice required | DecisionDock claims, original buttons; mandatory if fallback floats |
| Item-card picker | ItemCardPicker onConfirmPressed/onItemsSelected; OnHidden only clears UI | Existing mandatory identity; no hide-as-confirm |
| Doom/ability/element/options pickers | Plain GameObjects with native picker callbacks; not all are UIWindows | FloatingDecisionSurfaces own non-window roots; no added generic X |
| Distribute items/gold/points rewards | UIDistributeRewardManager process waits for native allocation Confirm | Existing original decision surface and blind-surface rescue, preserve host authority |
| Generic yes/no/spend/reset/perk confirmations | ConfirmationBox callbacks registered per Show; onHidden may cancel | Fix map and missed-open DialogSurface; original affirmative/cancel actions only |
| Dynamic DialogPopup choices / cancelable popup | Native option callback restores content and controller state via Hide; Cancel uses designated option/action | New close helper for dismissible cases; mandatory choices retain native options |
| Shop/trainer/temple/enhancement/records | Guildmaster HUD mode Exit restores character selection | Existing GuildmasterDestinations.LeaveMode route; confirmations remain native |
| Enhancement/donation confirmation | UIEnhancementConfirmationBox native OnConfirm / cancel Hide transition | Existing MapDialogSeat original widget adoption; no generic purchase/donation action |
| Perks/equipment/character inventory | Native selection/confirmation controls; onHidden cleanup | Preserve originals; map generic confirmation fix covers downstream decision |
| Party assembly / multiplayer assignment | Native party/hero assignment actions and readiness | Existing ownership and controller callbacks; no host/peer shortcut |
| Connection/save/DLC/errors | ErrorMessage actual buttons invoke recovery delegate | Dedicated original error float; never hide to swallow recovery |
| Main menu / pause / options / rulesets / compendium | Native actual menu buttons and controller close callbacks | Existing menu capture/fallback; VR Options toggle contract unchanged |
| Notifications | UINotificationManager per-notice action buttons and own hide callbacks | Passive/optional notifications are not evidence of blocked gameplay; do not add ModalUI lock |
| HelpBox / status/prop/door/tooltips / phase banner | Hover exit or timed native dismissal; not progression choices | Keep excluded from modal lock; avoid self-sustaining hover deadlocks |
| MP lock veil / black overlay | Native lock owner releases; actual message is elsewhere | Keep veil non-floating; never free native locks just to clear a picture |

## Mouse/gamepad binding review

Native classes frequently bind pointer listeners only when !InputManager.GamePadInUse:
UICharacterStoryBox, EventButton, UIEnhancementConfirmationBox, UICharacterCreatorStep,
UIElementPickerSlot, UILoadoutManager, UIReadyToggle, party/creator/shop/temple/item
slots, unlocked-location and campaign/Guildmaster reward continuations.

This is **not itself a defect in normal VR**. InputModeGuard is active for conversion,
prefixes both the public gamepad switch and the private binding writer, and restores
pre-existing gamepad mode in Tick. Plugin default module initialization is synchronous
in Awake. A deliberately delayed initialization could allow older Awakes to precede
the guard; this audit has no report or log proving that timing. Do not globally force
UI_SUBMIT, invoke every empty button, or add duplicate listeners to all these classes.
The level-up raw tracker defect is independent of this distinction.

## Validation and limits

- `message-continuation-tests.sh`: production helper compiled directly;
  **355 assertions / eight runtime negative controls**. Covers one through twelve
  queued messages, both native callback layers, pagination non-mutation, repeated
  close, unavailable native controls, recursion/throw cleanup, separate popup/window
  objects, disabled cancel, and mandatory popup refusal.
- `dialog-surface-tests.sh`: complete production DialogSurface compiled directly;
  **105 assertions / six runtime negative controls**. Covers map with no scenario
  choreographer, pre-attachment Show, 40 repeated confirm/cancel cycles, immediate
  queued reuse, manual screen release/return, leaving/returning to map, null/throw/
  partial conversion failure, once-only fallback/logging, retry on later opening,
  dedicated conversion disabled, and failed restoration retry without losing owner.
- Both fixtures model Unity/native lifecycle boundaries explicitly; they do not
  instantiate native prefab graphics and do not prove headset layout or multiplayer
  delivery. Native callbacks remain actual game callbacks in production.
- Required hardware sequence: complete a quest with story + rewards + unlocked
  location + level-up, reveal every new card, choose a card and confirm/cancel/retry;
  complete/progress a personal quest; test explicit choice dialogs and tutorial
  Continue; repeat once with a second player. Verify each visible input acts on
  exactly its foreground window and both players progress through native authority.
