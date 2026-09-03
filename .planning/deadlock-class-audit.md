# The deadlock class — audit

**Date: 2026-09-03. Base: `origin/dev`, ModBuild 371 (`Net/NetProtocol.cs:419`). READ-ONLY audit; nothing here was changed.**

User ruling, 2026-09-03: *"Das Problem hat allerhöchste Priorität - es darf keine Deadlocks dieser Art geben."*
That is a ruling about a CLASS. Four instances have been fixed one at a time (361, 364, 370, 371).
This document is the attempt to find the rest of the class before he does.

**An audit is a snapshot.** Every claim below names the file and line it was read in on 2026-09-03.
Re-check before acting; two builds is enough to make a line stale.

---

## 0. The failure shape, and the one thing that makes it a deadlock

Three terms must all hold:

1. the GAME will only leave its current state when the player acts on ONE specific widget;
2. that widget is, on this client, not drawn / not reachable / taken and handed back;
3. no other reachable control gets the player out.

The audit's finding is that term (1) is **enumerable and already named in the game's own fields**,
term (2) has **two structurally distinct causes** (the mod cannot SEE the widget; or the mod takes it
and gives it back), and term (3) is what the map's `LockOptionsInteraction` mask manufactures.

---

## A. CENSUS OF (1) — where the game parks on player UI

### A.1 The two scalar "the game is parked" fields (these are the watchdog's input)

| Field | Where | Why it is the right field |
|---|---|---|
| `AdventureMapUIManager.lockInteractionRequests` | `decompiled/GH.Runtime/AdventureMapUIManager.cs:363-383` | A `Dictionary<Component,bool>`; `lockMapInteractionMask.SetActive(count > 0)` at `:381` IS term (3) on the map. **The keys name the blocker by Component.** |
| `ActionProgressionManager.currentAction` | `decompiled/GH.Runtime/ActionProgressionManager.cs:46,74-82` | The map's SERIAL queue. `currentAction.ID` is a string — `"PersonalQuestCompleted"`, `"TempleDevotionLevelUp"`, `"CharacterLevelledUp"`, `"RetireCharacter"`. A stalled promise freezes the whole queue. |

Two more public getters, free to read:
`UIDistributeRewardManager.IsDistributing` (`decompiled/GH.Runtime/UIDistributeRewardManager.cs:24`) and
`UIAdventureRewardsManager.IsShowingRewards` (`decompiled/GH.Runtime/UIAdventureRewardsManager.cs:7`).

Scenario side: `ScenarioRuleClient.IsProcessingOrMessagesQueued` + `Choreographer.s_Choreographer.m_MessageQueue.Count`
+ `Choreographer.m_WaitState.m_State` (`decompiled/GH.Runtime/Choreographer.cs:2285`), read together exactly as
`SkipButton.CheckButtonInteractability` reads them (`decompiled/GH.Runtime/SkipButton.cs:162`).

### A.2 `WaitUntil` / `WaitWhile` — all 12 real ones in `GH.Runtime`

| Site | Waits on | Player-UI resolved? |
|---|---|---|
| `MapChoreographer.cs:2470` `WaitDistributionEnds` | `!UIDistributeRewardManager.IsDistributing` | **YES** — the distribute popups. This is the ModBuild 370/371 gate. |
| `MapChoreographer.cs:2461` `WaitAllRetired` | `!ExistsCharacterToRetire() && QueuedRetirements.Count == 0` | **YES** — retirement flow UI. |
| `UIRetirementManager.cs:67` `WaitAllPlayersUnready` | every peer's ready state | MP only; peers' UI. |
| `CampaignRewardsManager.cs:147` `CheckInteraction` | `interactable != interactionChecker()` | **`while(true)` loop, never exits**; stopped only by `StopCheckInteraction` (`:154`). Not itself a park. |
| `WorldspacePanelUIController.cs:724,732` | `FlowControlActive() \|\| m_HealthBar.IsAnimated` | animation, not UI. |
| `CardsHandUI.cs:1102`, `Choreographer.cs:14474`, `AttackModBar.cs:428` | animation counters | animation. |
| `PhaseBannerHandler.cs:151`, `LevelEventsController.cs:855` | `TransitionManager.TransitionDone` | transition. |
| `SaveData.cs:989` | `GlobalErrorMessage.ShowingMessage` | **YES** — the error box. Already polled by the mod (`ModalFallback.10.CatchAll.cs:1553`). |
| `SceneController.cs:1260,1287,1300` | `FFSNetwork.IsShuttingDown` | network. |
| `StoryImageViewer.cs:278` | image load | IO. |

### A.3 `CallbackPromise` chains whose ONLY resolver is a UI callback

`CallbackPromise` (`decompiled/GH.Runtime/Assets.Script.Misc/CallbackPromise.cs:88-96`) has no timeout and no
self-cancel. `Resolve()` fires once; if nothing calls it, the chain is parked forever.

| Promise | Created | ONLY resolver | Widget the player must act on | Type shape |
|---|---|---|---|---|
| `UIDistributeReward.promise` | `UIDistributeReward.cs:76` | `OnConfirmClick` (`:139`) ← `confirmButton`; or `ProxyConfirmClick` (`:148`) ← a host GameAction | `confirmButton` on `UIDistributeReward`; points via `UIDistributePointsPopup` | **`Singleton<UIDistributePointsPopup>` + `[SerializeField] GameObject window` + `SetActive`** (`UIDistributePointsPopup.cs:14,17,124,141`) — the blind shape |
| `UICampaignRewardWindow.continueAction` | `CampaignRewardsManager.cs:130` (`EnableButton(closeKey, Confirm, …)`) | `OnContinueButtonClick` (`UICampaignRewardWindow.cs:~265`) | `continueButton` | `MonoBehaviour` + `GetComponent<UIWindow>()` (`:62`) |
| `UIGuildmasterAdventureRewardsManager.onClosed` | `UIRetirementManager.cs:163`, `MapChoreographer.cs:848`, `MapChoreographer.cs:~993` | `OnHidden` (`:180-184`) ← `window.Hide()` ← `closeButton` (`:64`) or `KeyAction.UI_SUBMIT` (`:79`) | `closeButton` | `[RequireComponent(typeof(UIWindow))]` (`:13`) |
| `UICompletedPersonalQuestWindow.promise` | `:59` | `OnHidden` (`:96-105`) ← `window.onTransitionComplete` Hidden ← `closeButton.onClick → window.Hide` (`:44`) | `closeButton` | `MonoBehaviour` + `[SerializeField] UIWindow window` (`:12`) |
| `UIUnlockLocationFlowManager` per-location | `:148-149` (`continueAction = callbackPromise.Resolve`) | `Continue()` (`:162`) ← `continueButton.onClick` (`:48`) or `UI_SUBMIT` handler (`:52`) | `continueButton` | `Singleton<>` + `[SerializeField] UIWindow window` (`:34`) |
| `UIRetirementManager.RetireCharacter` | `:152-157` | `retireConfirmationBox.ShowConfirmationBox(…, callbackPromise.Resolve)` | confirm button | `[RequireComponent(typeof(UIWindow))]` (`UIRetireCharacterConfirmationBox.cs:9`) |
| `UIRetirementManager.ShowStory` | `:173-176` | `MapStoryController.Show(…, resolve)` | story next/close | `Singleton<>` + `[SerializeField] UIWindow window` (`MapStoryController.cs:42`) |
| `UIRetirementManager.ShowUnlockedLocations` | `:188-189` | `MapChoreographer.ShowUnlockedQuests(list, resolve)` | unlock-flow continue | as above |
| `UITownRecordsWindow` ×3 | `:104,130,154` | window close | `[RequireComponent(typeof(UIWindow))]` (`:15`) |
| `UIPersonalQuestResultManager` queue | `:57,170,226,247,260` | notification `RegisterToOnHidden` — **has a duration, self-heals** (`:143`, `:274-281`) | — | `Singleton<>`, no window |

### A.4 The scenario side — `ScenarioRuleClient.StepComplete()` gates

The pattern is uniform: `Choreographer.ProcessMessage` shows a picker, sets
`readyButton.SetInteractable(false)`, and the rule engine will not step until a click.

* **Doom pickers** — `Choreographer.cs:10898-10960` and `:10969-11035`.
  Widget: `UIAbilityCardPicker` — **`Singleton<UIAbilityCardPicker>` + `[SerializeField] GameObject window` + `SetActive`** (`UIAbilityCardPicker.cs:10,13,69,126`). The SECOND instance of the blind shape.
  Escape: `m_SkipButton` (`:10927`) — real, but `SkipButton.CheckButtonInteractability` (`SkipButton.cs:162`) requires `ThisPlayerHasTurnControl` and an idle actor.
* **Item lose / refresh pickers** — `ItemRewardLosePicker.cs:54`, `ItemCardRefreshPicker.cs:33`, both parking
  `Choreographer.m_WaitState.m_State` on `WaitingForLoseGoalChestRewardSelection` / `WaitingForItemRefresh`
  (`ItemRewardLosePicker.cs:90`, `ItemCardRefreshPicker.cs:99,124`).
  Widget: `ItemCardPicker` — `[RequireComponent(typeof(UIWindow))]` (`ItemCardPicker.cs:9`).
* **End-of-scenario results** — `UINewAdventureResultsManager`, `UIWindowID.ResultsPanel`.

### A.5 THE SUB-CLASS THE CENSUS TURNED UP: an escape control gated behind an animation callback

This is new, it is not one of the four already fixed, and it is on the hot path of every scenario end.

`GUIAnimator.Stop()` fires `OnAnimationStopped`, **never** `OnAnimationFinished`
(`decompiled/GH.Runtime/GUIAnimator.cs:53-73`). `HeaderHighlight.Hide()` is `highlightAnimator.Stop(goToEnd: true)`
(`HeaderHighlight.cs:66-69`) — still `OnAnimationStopped`. So any interruption of a reveal animation
permanently strands whatever its `OnAnimationFinished` was going to enable.

Four windows show their ONLY escape control this way:

1. **`UINewAdventureResultsManager`** — `Show()` does `buttonsContainer.SetActive(false)`
   (`decompiled/GH.Runtime/UINewAdventureResultsManager.cs:~128`). The buttons come back ONLY in
   `OnFinishedOpenAnimations()` (`:113-119`), reached through **two chained** animator callbacks:
   `myWindow.onTransitionComplete`→Shown → `header.Show(cb)` (`:74`) → `HeaderHighlightText.OnFinished`
   (`HeaderHighlightText.cs:55-58`) → `ContinueShowStats()` (`:96`) → `showStatsAnimation.Play()` →
   `showStatsAnimation.OnAnimationFinished` (`:87`) → `OnFinishedOpenAnimations()`.
   Break either link and the end-of-scenario window has **no buttons, forever**.
2. **`UICampaignRewardWindow`** — `EnableContinueButton` does `continueButton.gameObject.SetActive(false)`
   (`:165`); it returns only inside the `ShowRewards` **coroutine** (`:~257`), which a
   `SetActive(false)` on the window kills outright. `animationBlocker.SetBlock(true)` is set at the top of
   that coroutine and cleared only at its end, so the `UI_SUBMIT` hotkey is blocked too.
   `OnDisable()` → `StopAnimations()` → `header.Hide()` (`:~200`).
3. **`UIGuildmasterAdventureRewardsManager`** — `ShowRewards` sets
   `rewardsPopupCanvasGroup.alpha = 0f; interactable = blocksRaycasts = false` (`:110-113`) and
   `animationBlocker.SetBlock(true)` (`:116`); both are undone only in `OnFinishHeaderAnimation()` (`:125-131`),
   fired by `header.Show(…)` (`:117`). `OnDisable()` calls `header.Hide()` (`:177`).
4. **`UICompletedPersonalQuestWindow`** — `closeButton.gameObject.SetActive(false)` at `:64`, restored only in
   `OnFinishedCompleteAnimation()` (`:90-94`). `OnDisable()` calls `completedAnimation.Stop()` (`:108-111`).

**Status: a latent game bug, NOT currently triggered by the mod.** Verified: the conversion path
reparents (`CanvasConversion.1.Core.cs:273`) but never calls `SetActive(false)` on the target — no
`.SetActive(` anywhere in `WorldUI/Conversion/*.cs` outside a comment. The pre-convert blackout
disables `Canvas` components and writes `CanvasGroup.alpha`, not `activeSelf`
(`ModalFallback.11.PreConvertHide.cs:192,208`), neither of which raises `OnDisable`. **The trigger that
WOULD fire it is a mod path that deactivates or destroys the subtree — i.e. section C.**

### A.6 Two input-mode traps found while reading A

* **`InputManager.GamePadInUse`** = `PlatformLayer.Instance.IsConsole || isUseGamepadInPc`
  (`decompiled/GH.Runtime/InputManager.cs:114-122`; `isUseGamepadInPc` written at `:1216`/`:1229`).
  Three windows wire their close/continue button **only** in the `!GamePadInUse` branch of `Awake` — a
  ONE-SHOT decision: `UIGuildmasterAdventureRewardsManager.cs:62-65`, `UIUnlockLocationFlowManager.cs:46-49`,
  `UICampaignRewardWindow.cs:~58-64`. If the mod's VR input ever makes `isUseGamepadInPc` true before those
  `Awake`s run, **clicking the button does nothing** and the only escape is the `UI_SUBMIT` KeyActionHandler.
  *Unverified — I did not establish what the mod's input path does to `isUseGamepadInPc`. It is a cheap grep and worth doing.*
* **`UIConfirmationBoxManager.CurrentBox`** switches between TWO different GameObjects on the same flag
  (`decompiled/GH.Runtime/UIConfirmationBoxManager.cs:26-36`) and is re-evaluated per call. Floating one while
  the game shows the other is a deadlock by construction.

---

## B. CROSS-CHECK AGAINST THE MOD — what floats what

### B.1 The mod's three collectors are all typed on `UIWindow`

All feed `ModalFallback.OpenWindows` (`ModalFallback.3.WindowPanel.cs:17`):

* **ENROLLED** — `ModalFallback.4.Tick.cs:2555-2579`, backed by `HashSet<UIWindow> Open`
  (`ModalFallback.3.WindowPanel.cs:13`), fed only by the postfix on
  `UIWindow.EvaluateAndTransitionToVisualState` (`Core/Events/GameEventPatches.cs:99-107`), and further
  filtered to `FallbackIds` (`ModalFallback.1.Core.cs:353`).
* **CATCH-ALL** — `ModalFallback.10.CatchAll.cs:133` (`CatchAllObserve(UIWindow, bool)`), `Dictionary<UIWindow,int>`,
  same patch. Room-gated: `if (!inScenario || !WorldUIConfig.ConversionActive) return;` (`:236`) where
  `inScenario` is `VRModeStateMachine.TableInFrontOfPlayer` (`ModalFallback.4.Tick.cs:2463`) — so it IS live in
  the 3D map room, and dead in the flat menu/town.
* **POLL / GROUP** — `ModalFallback.7.Close.cs:371,379`; five hand-named singletons
  (`ModalFallback.4.Tick.cs:2580-2596`, plus `ModalFallback.10.CatchAll.cs:1466,1553`).

**There is no shape-based detection anywhere in the mod.** No "a full-screen raycast-blocking graphic
appeared", no `FindObjectsOfType<Canvas>()`, no `UIWindowID` string match. Every one of the ~14 surfaces
resolves ONE hardcoded `Singleton<T>.Instance`. That is the structural blind spot; `UIDistributePointsPopup`
was one instance of it and the five existing hand-written polls are five prior instances, each patched individually.

The mod already knows this and says so: `GameEventPatches.cs:81-83` calls `UIWindow` "the game's ONLY window
primitive" — that is the false premise — and `ModalFallback.10.CatchAll.cs:1498-1507` records the one
counter-example (`GlobalErrorMessage`) it had already paid for.

### B.2 The `Choreographer` term kills 17 of 18 surfaces outside a scenario

`WorldSurface.cs:35-36`:
```csharp
protected virtual bool WantConverted =>
    ConfigEnabled && WorldUIConfig.ConversionActive && Choreographer.s_Choreographer != null;
```
`Choreographer.s_Choreographer` is the SCENARIO choreographer; the map runs `MapChoreographer`. Every surface
except `DistributeRewardSurface` (`FloatingDecisionSurfaces.cs:309-311`, which drops the term deliberately) is
structurally dead on the campaign map and in town. Restated by hand in five more places:
`DialogSurface.cs:66-67`, `TrayControlDockSurface.cs:142-146`, `EnemyRevealSurface.cs:330-331`,
`StatPanelSurface.cs:487-488`, `ModalFallback.2.DecisionDock.cs:216`.

### B.3 RANKED — what can still deadlock

Ranking is by (reachability in normal play) × (nothing floats it) × (no other escape).

**RANK 1 — `UINewAdventureResultsManager` / the `ResultsPanel` animation chain.**
Reachable at the end of EVERY scenario. `UIWindowID.ResultsPanel` **is** in `FallbackIds`
(`ModalFallback.1.Core.cs:~400`), so the mod floats it — via the enrolled path, one tick after Shown, i.e.
*during* the two-stage animator chain in A.5. Today's conversion does not deactivate, so this is latent; any
future release/re-convert cycle landing inside that ~1 s window strands `buttonsContainer` permanently.
*Caveat, stated because it matters: `UIWindowID` values are SCENE-serialized — no code assigns any window ID
(`ModalFallback.1.Core.cs:~395` says so). That `myWindow` on this class carries `ResultsPanel` is inference, not proof.*

**RANK 2 — the map reward windows: `UIGuildmasterAdventureRewardsManager` / `UICampaignRewardWindow`.**
Reachable on every quest completion, temple devotion level-up, achievement claim and retirement
(`MapChoreographer.cs:848`, `:~993`, `UIRetirementManager.cs:163`). They sit BEHIND
`LockOptionsInteraction(locked: true, …, blur: true)` (`UIDistributeRewardManager.cs:88`) or inside the
`ActionProgressionManager` queue, so term (3) is guaranteed: the map is masked, the only escape is the popup.
On the map, 17 of 18 surfaces are dead (B.2); the catch-all IS alive in the 3D map room but not in flat town;
whether the enrolled path takes them depends on an ID nobody can read from code. **This is the highest-value
thing to instrument, because "is it floated?" is currently unanswerable without a log.**

**RANK 3 — `UIUnlockLocationFlowManager`.** Map, campaign. Camera input disabled (`:84`), Guildmaster HUD
hidden (`:85`), one promise per unlocked location (`:148`). `UIWindowID.UnlockQuestPopup` IS in `FallbackIds`,
and the window shows/hides ONCE for the whole sequence (`:78`, `:117`), so the churn fuse does not reach it.
Downgraded from my first read on that basis.

**RANK 4 — `UICompletedPersonalQuestWindow` + `UIRetireCharacterConfirmationBox` + `UITownRecordsWindow`
+ `MapStoryController`.** All carry a real `UIWindow`, all on the map, all inside the retirement /
personal-quest chain that `MapChoreographer.cs:2461` waits on. Same ID-unknown problem as rank 2.

**RANK 5 — `ItemCardPicker` (lose/refresh).** Scenario, so the surfaces and catch-all are live; and
`CatchAllEligible` has an explicit `IsCardsOwnedItemPickerWindow` exclusion (`ModalFallback.10.CatchAll.cs:~410`),
meaning the Cards system claims it. Edge case, listed for completeness. **Note the mod's own history:
`ModalFallback.10.CatchAll.cs:236` records that switching the catch-all off "restored the ItemCardPicker
silent-deadlock class" — this widget has deadlocked before.**

**RANK 6 — `UIConfirmationBoxManager`'s pc/gamepad box pair.** Edge case, but a total deadlock if it fires.

**Not a hazard (checked, reporting the negative):** `UIAbilityCardPicker` and `UIDistributePointsPopup` are
covered by `DoomPickerSurface` / `DistributePointsSurface` / `DistributeRewardSurface`
(`FloatingDecisionSurfaces.cs:123,158,292`). `UIPersonalQuestResultManager`'s notification promises self-heal
on the notification duration (`:143`, `:274-281`). `ModalFallback.11.PreConvertHide` is bounded and
self-restoring with a named reason (`:352-396`) — it is the MODEL for section D, not a hazard.

---

## C. THE MOD'S OWN RELEASE PATHS AS A HAZARD

The question asked of each: **can this fire for a reason unrelated to the game having finished with the panel?**

### C.1 The structural flaw — a retry condition that is unreachable in exactly the deadlock case

`RefuseEmptyFloat` (`ModalFallback.9.Spawn.cs:2393`) releases outright and files the window in `EmptyRefused`
(`:2439`) or `Failed` (`:2434`). Both mean **"retry only after a close/re-open"**. The clear is
`ClearEmptyRefusal`, called from `CatchAllEligible` and gated on `!window.IsOpen`
(`ModalFallback.10.CatchAll.cs:~413`; the only other clear is `CatchAllReset` at `:1416`).

**A window the game is parked on never closes.** So the retry condition is unreachable precisely when it
matters. One false "empty" verdict is permanent for that open. Same shape on `EmptyHold`
(`ModalFallback.9.Spawn.cs:2765`).

The verdict itself is well built and I want to record that honestly: `HasAnyDrawnContent`
(`ModalFallback.9.Spawn.cs:2467-2500`) tests **presence, not visibility** — it reads `Graphic.enabled` and rect
size and deliberately ignores alpha, so "a window that is merely faded, masked or fully transparent is NEVER
refused" (`:2405-2409`). The residual exposure is `GetComponentsInChildren(includeInactive: false, …)`: a window
whose entire drawn subtree is `SetActive(false)` during its own reveal reads as empty. That is exactly what the
A.5 windows do to their button containers — **but not to their whole content**, so I could not show the
collision. Label: **plausible, unproven; the discriminating field already exists** —
`EMPTY WINDOW REFUSED: '<name>' (ID <id>, host '<host>')` at `ModalFallback.9.Spawn.cs:2395`.
Timing: the reveal edge is `RevealMaxWaitSeconds = 0.6f` (`CanvasConversion.2.Adopt.cs:44`), up to 1.5 s for a
shared window. **That matches the ModBuild 371 report — "floated correctly for about a second, then released" —
better than any other path, and it is one grep to confirm or kill.**

### C.2 A fuse that counts the player, for the third time

`ModalFallback.10.CatchAll.cs:81,85,338,351-364`:
```csharp
internal const int   ChurnMaxFloats     = 3;
internal const float ChurnWindowSeconds = 60f;
...
if (churn.Count > ChurnMaxFloats) { ChurnSuppressed.Add(window.name); ... continue; }
...
if (!hoverCard && ChurnSuppressed.Contains(window.name)) continue;
```
Keyed by `window.name`, **session-permanent** (cleared only in `CatchAllReset`, `:1426-1427`). Four legitimate
opens of one window name inside a minute and that window is floated NOWHERE for the rest of the session; the
only recovery is the manual A/X chord. Hover cards were already exempted after this fuse ate the player once
("Es kommen nun gar keine Mouseovers mehr", `:247-256`). **The exemption fixed the instance, not the class:
the fuse still cannot tell a hand from a loop.** This is the memory note `a-fuse-cannot-tell-a-hand-from-a-loop`
recurring.

### C.3 Terms in `WantConverted` that have nothing to do with the game

| Term | Where | Fires on |
|---|---|---|
| `Choreographer.s_Choreographer != null` | `WorldSurface.cs:36` | a raw static going null across ANY scene work — one blip releases every surface panel, no debounce |
| `WorldUIConfig.ConversionActive` → `VRSession.IsRunning` | `WorldUIConfig.cs:1054-1055` | headset doff/standby, XR restart |
| `FlatScreen.ManualScreenActive` | six overrides incl. `FloatingDecisionSurfaces.cs:58`, `DecisionDockSurface.cs:560`, `UseBarsSurface.cs:2133` | the player's own rescue chord (arguably correct — the flat screen then shows it) |
| `DecisionDockSurface.RowFocusHidden` | `DamageTooltipSurface.cs:208-217`, set in `DecisionDockSurface.cs:200` | **the player looking at a different character** releases the prompt of a live unanswered damage decision |
| `PlayTray.Current != null` | `TrayControlDockSurface.cs:142-148` | a mod-internal tray rebuild |
| `ConfigEnabled` | `WorldSurface.cs:35` | a live config toggle, no "is a decision pending" term |

Release loop terms are re-evaluated EVERY tick with no edge-latch and no hysteresis
(`ModalFallback.4.Tick.cs:2698,2759,2761-2784`).

### C.4 The reversible ones — recorded so they are not re-litigated

* **Dormancy** (`GoDormant`, `ModalFallback.9.Spawn.cs:3379-3391`; triggered `:3256` after
  `LivenessGraceSeconds 1.5f` + `EmptyHideDwellSeconds 0.35f`) hides render + raycaster. It is a HIDE, not a
  release, it re-measures on a stride and un-hides, and it logs
  `EMPTY WINDOW HIDDEN: '<name>' … DARK — <reason>`. Correct design. The 45 s
  `DormantReleaseSeconds` backstop (`:2703`) is the irreversible tail.
* **`DestroyHostSafely` / `ServiceDeferredHosts`** (`CanvasConversion.4.Lifecycle.cs:349,376`) only ever
  DECLINE to destroy, and leak the host alive rather than delete a game window (`:411-415`). Right call.
* **`ModalFallback.11.PreConvertHide`** — bounded by a frame budget, restores unconditionally, and every
  restore carries a named reason (`:352-396`). This is the pattern D should copy.

### C.5 Two confirmed gaps in the ModBuild 361 fix

The convert-side fix is real: `KeepHostInTargetScene` (`CanvasConversion.1.Core.cs:269`, body `:501-533`) runs
BEFORE the reparent, and `RestoreTargetScene` (`:426-448`) runs on release. But:

1. **`ServiceDeferredHosts` frees stuck game content to the bare active-scene root and never calls
   `RestoreTargetScene`** — `CanvasConversion.4.Lifecycle.cs:395`:
   ```csharp
   stuck.SetParent(null, worldPositionStays: false);
   ```
   A `DontDestroyOnLoad` game window freed this way lands in the ACTIVE scene and dies at the next scene load.
   **That is the ModBuild 361 defect re-created on a second code path.** Rare (needs a refused detach) but it
   is the exact failure that cost a round, and the release-side `RestoreTargetScene` call is guarded by
   `if (detached && restoreParent == null)` (`:196`) which this path does not satisfy.
2. **`KeepHostInTargetScene` returns silently** when `!home.IsValid()` (`:507`) or `!home.isLoaded` (`:525`) —
   only the `catch` warns (`:527-533`). A target whose scene is mid-unload gets a host in the active scene and
   the reparent migrates it, with **no line in the log saying so**.

Also flagged, unverified by me: five composite sites reparent GAME content under a float host without any
scene-membership record — `StoryComposite.cs:4076,4112`, `GuildmasterDestinations.cs:283`,
`LoadoutConfirmPark.cs:849`, `EnchantressComposite.cs:876`, `TooltipOnWindow.cs:1279`. Each restores the
hierarchy but not the scene.

---

## D. THE STANDING GUARD — designed, not built

### D.1 Reject the sketch as stated

The brief's sketch — "a watchdog that knows the parked states and raises an Alert after N seconds with nothing
floated" — has three faults this project has already paid for.

* **"Nothing floated" is the wrong predicate.** It is a mod-side fact. The three deadlocks that mattered were
  (a) not floated, (b) floated then released, (c) floated but dormant with the raycaster off. Only (a) is
  covered. *Cf.* `probe-that-answered-is-spent`: measure the OUTCOME.
* **N seconds alone is a number, not a blocker.** *Cf.* `name-the-blocker-not-the-number`: six rounds tuned a
  fraction; one field naming WHICH renderer blocked the ray ended it.
* **It would be a new instrument asserting a cause.** *Cf.* `instrument-shipped-and-lying`.

### D.2 What to build instead — the PARK WATCHDOG

**One term, four fields, zero new state.** The insight from section A is that the game already stores the
answer: the parked state has a NAME, and the widget has a NAME, and both are readable.

**Where it lives.** A new file, `src/GloomhavenVR/WorldUI/Modal/ParkWatchdog.cs`, ticked from
`WorldUIDriver.BuildTickSteps` (`WorldUIModule.cs:~313`) LAST in the Update list, so every float decision for
the frame has already been made. It must own no state the release paths read — it observes only.

**The park term** (any one true = the game is parked on the player):

| # | Read | Blocker NAME it yields |
|---|---|---|
| 1 | `AdventureMapUIManager.lockInteractionRequests.Count > 0` (private — Harmony postfix on `LockOptionsInteraction`, `decompiled/…/AdventureMapUIManager.cs:363`, and cache the `request.name` strings) | the requesting Components, by name |
| 2 | `ActionProgressionManager.currentAction != null` (private; postfix `AddAction`/`CheckProgressNextAction`) | `currentAction.ID` — already a string |
| 3 | `Singleton<UIDistributeRewardManager>.Instance.IsDistributing` (public) | `"UIDistributeRewardManager"` |
| 4 | `Singleton<UIAdventureRewardsManager>.Instance.IsShowingRewards` (public) | the concrete subclass name |
| 5 | `Choreographer.s_Choreographer.m_WaitState.m_State != <idle>` | the `ChoreographerStateType` enum name |

Prefer the two Harmony postfixes over reflection-per-frame: they turn a private field into a cached string at
the moment it changes, so the per-frame cost is comparing a cached bool.

**The reachability term.** For each park, the widget is known statically (the A.3 / A.4 tables). Ask three
questions of it, in this order, and report which one answered:

1. **DRAWN?** — is a `Graphic` under it enabled with a non-degenerate rect and a non-zero inherited alpha?
   (Reuse `HasAnyDrawnContent`'s traversal, `ModalFallback.9.Spawn.cs:2467`, plus the alpha term it
   deliberately omits — the omission is right for `RefuseEmptyFloat` and wrong here.)
2. **FLOATED?** — `ModalFallback.IsConverted(window)` / the owning surface's `Panel != null`.
3. **POKEABLE?** — is the host raycaster enabled, is the panel not `Dormant`, is the specific button
   `interactable && activeInHierarchy`?

**Cost.** Zero per-frame allocation; two cached bools and, only while a park term is true, one traversal every
30 frames. Off the park path it is five field reads. Comparable to `TickPreConvertHide`'s early-out
(`ModalFallback.11.PreConvertHide.cs:356-357`).

**How it avoids false positives during loading.** Two gates, both already available:
`VRModeStateMachine.TableInFrontOfPlayer` must be true (no room, no verdict — the same gate
`TickPreConvertHide` uses at `:377-379`), and the park must have been standing across a **scene-stable**
window: reset the timer on any scene load, on `TransitionManager.s_Instance.TransitionDone` going false, and
while `ScenarioRuleClient.IsProcessingOrMessagesQueued`. Suggested bar: **8 s**, which is far past the ~4 s
load in the report and past the ~1 s inter-announcement gap `UIUnlockLocationFlowManager` takes
(`ModalFallback.9.Spawn.cs:2694` tombstone records both measurements).

**How it avoids reporting a number instead of a blocker.** The Alert is a SENTENCE with four named fields and
no scalar as its subject:

```
PARK WATCHDOG: the game has been parked for 8.3 s on <PARK NAME> — blocker '<blocker name>'.
  The widget it is waiting for is '<widget name>' (<Type>, UIWindow ID <id or "not a UIWindow">).
  DRAWN: no  (no enabled Graphic with a non-degenerate rect under it)
  FLOATED: no  (last mod verdict for this window: EMPTY WINDOW REFUSED at frame 41209)
  POKEABLE: n/a
  Escape controls reachable right now: none.
```

`FLOATED: no` must carry **the mod's last recorded verdict for that window**, not just a bool — `EmptyRefused`,
`Failed`, `ChurnSuppressed`, `FloatRefusalTable`, `Dormant`, "never observed by any collector". Those sets
already exist and already hold the answer; the watchdog's job is to *join* them to the park, which nothing does
today. That join is the whole value: it turns "the player is stuck" into "the player is stuck **because** the
churn fuse suppressed 'Rewards Window' at 14:32".

**One Alert per park episode**, keyed on (park name + widget), re-armed when the park clears. It must be
`VRLog.Alert` and marked `// HW-VERIFY` — *cf.* `quiet-log-silenced-the-backlog`: promote the TIER, never the text.

### D.3 The two cheap fixes the watchdog does not replace

Both are one-liners and both remove a whole branch of the class. They belong to whichever lane owns the files.

1. **Make the `EmptyRefused` / `Failed` retry condition reachable.** Today it is `!window.IsOpen`
   (`ModalFallback.10.CatchAll.cs:~413`), which a parked window never satisfies. Add an OR on the park term
   from D.2: a window the game is parked on gets reconsidered on the next tick regardless.
2. **Call `RestoreTargetScene` on the deferred-host free path**, `CanvasConversion.4.Lifecycle.cs:395`.

---

## E. What I could not establish

Stated so nobody re-derives it as fact:

* **Which `UIWindowID` each map window carries.** IDs are scene-serialized; no code assigns one
  (`ModalFallback.1.Core.cs:~395`). Everything in B.3 that depends on `FallbackIds` membership is therefore
  inference. The watchdog's `UIWindow ID <id>` field settles it in one hardware round.
* **Whether the mod's input path sets `isUseGamepadInPc`** (A.6). One grep.
* **Whether `RefuseEmptyFloat` actually fired on the ModBuild 371 popup.** The log line exists and names the
  window (`ModalFallback.9.Spawn.cs:2395`); grep `EMPTY WINDOW REFUSED` in the 371 hardware log before
  building anything.
