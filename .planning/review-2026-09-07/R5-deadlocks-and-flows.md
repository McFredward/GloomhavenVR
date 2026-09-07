# R5 — Deadlocks and flows

**Date: 2026-09-07. Base: `origin/dev` `9f646c86`, ModBuild 479 (`Net/NetProtocol.cs:451`). READ-ONLY review; not one line of `src/` was changed.**

Question, verbatim: *"Prüfe auf mögliche Deadlocks: Sind alle Flows gegen Deadlocks abgesichert? Machen die Flows
(zB Verbrennen, Kurze Rast, Lange Rast) das was sie sollen?"*
Standing ruling: *"Das Problem hat allerhöchste Priorität — es darf keine Deadlocks dieser Art geben."* — a ruling about a CLASS.

Ground truth is `decompiled/GH.Runtime/` (read at `/home/claw/gloomhaven_vr/decompiled/`; gitignored in this worktree).
The prior audit `.planning/deadlock-class-audit.md` is dated 2026-09-03 / ModBuild 371 and says so. **Every claim taken from it
was re-derived at 479**, and §5 lists where it is now wrong.

---

## 0. The one-sentence result

The five fixed instances were each *a mod gate that read a SUBSET of the multi-term expression the game itself uses for the same
question*. That shape is still present in four more places at 479 — and the sharpest of them was made **reachable by this
build's own fix**: ModBuild 479 opened the "pick up your damaged character" grab, and the bar-hide path that grab triggers
guards one of the game's two coroutine-holding terms and not the other.

---

## 1. FINDINGS, in rank order

### F1 — CONFIRMED. Hiding a held figure's bar latches `HealthBar.IsAnimated` true for ever, and can strand the scenario's own end-of-game callback

* **Where.** `src/GloomhavenVR/WorldUI/ActorBars.cs:596` (the guard) and `:664-666` (`panel.HostGo.SetActive(!hide);`).
  The host holds the game's LIVE controller: `ActorBars.Adopt` runs
  `CanvasConversion.Convert(controller.transform as RectTransform, "ActorBar", pokeable: false, …)`, i.e. the
  `WorldspacePanelUIController` **and its `m_HealthBar` child** are reparented under `panel.HostGo`.

* **Mechanism.** `WorldspacePanelUIController.FlowControlActive()` is `return m_AttackModBar.IsFlowActive;` and **nothing else**
  (`decompiled/GH.Runtime/WorldspacePanelUIController.cs:674-677`). The game's own expression, at all five of its own sites, is
  `FlowControlActive() || m_HealthBar.IsAnimated` — `:683`, `:698`, `:711`, and the two `WaitWhile`s at `:724`
  (`DestroyDelayed`) and `:732` (`WaitEndAnimation`). The mod's guard reads the first term only.
  `HealthBar.UpdateHealth` writes `m_IsAnimActive = true` **before** `StartCoroutine`, and the ONLY writer of `false` is the
  `AnimateSlider` completion delegate (`decompiled/GH.Runtime/WorldspaceUI/HealthBar.cs:292-306`, `:319-336`).
  Deactivating an ancestor kills a coroutine permanently, and `StartCoroutine` on an already-inactive GameObject never runs at
  all — **so no race is needed.** `ActorBars.cs:573-576` states this exact rule in the mod's own words, for the AttackModBar.

* **FAILURE SCENARIO.** The player picks up a miniature — the interaction ModBuild 479 was shipped to enable
  (`fee2b3b3`, user item 7: *"ich will ihn aufheben um infos zu bekommen"*). `hide` goes true (`ActorBars.cs:566`), the host is
  deactivated. While it is held, that actor's HP changes: an enemy hits it, a retaliate, an AoE, a poison/wound tick, or the
  damage decision he picked it up to think about resolves. `m_IsAnimActive` latches **true permanently**. Then:
  1. every later `UpdateHealth` for that actor enqueues into `pendingActions` and returns (`HealthBar.cs:248-254`) — the bar
     shows stale HP for the rest of the scenario, and each update leaks a closure;
  2. `Show()` / `Hide()` can never complete: they take the `StartCoroutine(WaitEndAnimation(…))` branch whose `WaitWhile` at
     `:732` never ends;
  3. **when that actor dies**, `Choreographer`'s `ActorDead` handler runs
     `ActorBehaviour.GetActorBehaviour(gameObject28)?.m_WorldspacePanelUI.Destroy(onDestroy);`
     (`decompiled/GH.Runtime/Choreographer.cs:9259-9260`). `Destroy` takes the `StartCoroutine(DestroyDelayed(onDestroy))`
     branch (`WorldspacePanelUIController.cs:691`) whose `WaitWhile` at `:724` never ends — so **`onDestroy` is never invoked
     and the bar is never destroyed.** `onDestroy` is `checkActorsDeadAction` (`Choreographer.cs:9194-9215`), the delegate that
     calls `WinScenario()` / `LoseScenario()` once every player is dead.

* **On screen.** A dead character's health bar still hanging over the board; every other character's HP frozen at the value it
  had when the mini was picked up — and, if the latched actor was the last player alive, a party that is dead and a scenario
  that never ends. **Releasing the mini does not heal it:** the latch is on the game's field, not on the mod's hide.

* **Reachable on hardware this round: YES. Second player: NOT needed.**

* This is the ModBuild 107 deadlock's own guard (`ActorBars.cs:568-580` quotes that report verbatim) taking one of the two terms
  the game writes side by side in five places. Same shape as the fix at HEAD, one file over. Note also that the mod has already
  learned the "a killed or refused coroutine latches a flag" lesson twice — `BurnCommitRescue.EnsureHandCanRunItsCoroutine`
  (`Cards/Patches/DamageFlowPatches.cs:52`) exists for exactly that on `CardsHandUI`. F1 is the third member of that family and
  the only unguarded one.

### F2 — CONFIRMED. `ItemActionResolving` reads the bare busy flag, and its doc-comment names the three game sites that do not

* **Where.** `src/GloomhavenVR/Cards/CardsGameApi.cs:1874`
  (`if (ScenarioRuleClient.IsProcessingOrMessagesQueued) return true;`), doc at `:1816-1823`.

* **Mechanism.** The doc claims *"`ReadyButton.cs:501`, `UndoButton.cs:294` and `SkipButton.cs:162` all gate their
  interactability on exactly this"* and *"**It cannot stick — a dedicated worker thread drains the queue.**"*
  Both sentences are falsified by the commit at HEAD (`fee2b3b3`): those three sites gate on the **three-term** expression
  `(!IsProcessingOrMessagesQueued || WaitingForPlayerToSelectDamageResponse || WaitingForPlayerActorToAvoidDamageResponse)`,
  and `GameState.ActorDamaged` / `ContinueActorDamagedAfterSelectingPlayerActorToBurnCards` park the SRL work thread in
  `while (!s_Recieved…Response) { ThreadIsSleeping = true; Thread.Sleep(100); }`
  (`decompiled/…/GameState.cs:1187-1207`, `:1245-1273`) for the whole length of the prompt.
  The ModBuild 478 logs show the flag true on **both** machines during one player's decision.

* **FAILURE SCENARIO.** Any player's take-damage decision stands. A used item card is lying on the board's item-use recess.
  `TickUseResolving` (`Cards/Piles/ItemsPile.cs:2719`) keeps answering "still resolving", so the card stays seated, and
  `RefuseResolvingRemoval` (`:2751`) springs it back into the recess every time the player lasers it toward the pile — for the
  whole length of a co-player's think.

* **On screen.** A card lying in the item recess that will not go back on the pile, with a visible spring-back each try.

* **Severity: a stuck WIDGET, not a stuck game** — it self-heals the moment the prompt is answered. Reported because it is the
  *same term, the same fix, one consumer over*, and finding these one at a time is what the ruling forbids.

* **Reachable this round: YES** (own damage prompt, single player). **Worse with a second player** (a peer's long think).

### F3 — CONFIRMED mechanism, PLAUSIBLE reach. The take-damage repair re-arms, every frame, controls the game deliberately withdrew

* **Where.** `src/GloomhavenVR/Cards/Driver/CardsDriver.6.Flows.cs:1639-1700`; `ReassertDamageOption` at `:1704-1732`.

* **Mechanism.** The gate is `panel.IsOpen && panel.actorBeingAttacked != null`. `SelectItemState.Enter`
  (`decompiled/GH.Runtime/Script.GUI.SMNavigation.States.ScenarioStates/SelectItemState.cs:41-44`) leaves both true while it
  calls `_savedState = Singleton<TakeDamagePanel>.Instance.SetDisableVisualState();`, which zeroes `canvasGroupVisbility.alpha`
  and clears all three `interactable` flags (`TakeDamagePanel.cs:1078-1092`); `Exit` (`:55`) restores the snapshot.
  **That is a symmetric save/restore — not a latch and not a race.** The mod's own log text calls it
  *"SelectItemState save/restore or a ResetAndHide race"* and repairs it anyway: within one frame it writes `alpha = 1f`,
  `interactable = wantInteractable` on both toggles and on `takeDamageButton`, and forces
  `takeDamageButton.gameObject.SetActive(true)` — undoing `DisplayButtons(false)` as well.

* **FAILURE SCENARIO.** The player's character is attacked; he opens an OnAttacked shield/retaliate item from the docked
  use-bar, entering `SelectItemState` (which also sets `UIManager.Instance.ToggleBurnCardBlock(isBlocked: true)`). The game has
  greyed and alpha-hidden the three damage answers on purpose. One frame later the mod has them lit and pressable again. He
  presses "Schaden erhalten" from **inside** the item sub-state: `ResetAndHide(Lockin: true)` runs
  `actorBeingAttacked.Inventory.LockInSelectedItemsAndResetUnselected()` (`TakeDamagePanel.cs:1049-1050`) while the item
  selection is still in flight, and `SelectItemState.Exit` then writes the stale `_savedState` back onto a panel whose decision
  has already been answered.

* **THE OPEN TERM, stated rather than assumed:** whether the VR item path enters `SelectItemState` at all. The mod contains
  **zero** references to `SelectItemState` and no gate for it (`grep -rn SelectItemState src/` → nothing). The defeat of the
  game's own double-entry guard is confirmed in code; the reachability is the reading to take.

* **Second player: not needed.**

### F4 — PLAUSIBLE. The decision dock deactivates the host the game's row is adopted under, and its own escape is zeroed by a successful convert

* **Where.** `src/GloomhavenVR/WorldUI/Surfaces/DecisionDockSurface.cs:1034-1038` (`Panel.HostGo.SetActive(false)` when
  `rect.width < 1f || rect.height < 1f`), grace reset `:930` (`if (Panel != null) { _wantSince = 0f; … }`), grace `:955-968`,
  class doc `:108-118`.

* **Mechanism.** `Panel.HostGo` is the mod host the isolated ROW — game-owned `ExtendedButton` transforms taken from
  `DialogPopup.optionButtons` (`ModalFallback.2.DecisionDock.cs:88-112`) — has been reparented under. `SetActive(false)` on it
  raises `OnDisable` on those widgets, which the class doc says at `:110-113` is *"exactly why SetActive is NOT the mechanism"*.
  And because `_wantSince` is reset to 0 on **every tick a panel exists**, the 1.5 s `ClaimGraceSeconds` →
  `ModalFallback.DecisionDock.MarkGaveUp` hand-back can never fire once the row has converted. Meanwhile
  `SuppressWindowGroup(_activeWindow)` (`:829`) keeps the source window invisible and the dock's live claim keeps the generic
  modal fallback standing down.

* **FAILURE SCENARIO.** A short-rest burn/redraw prompt. `CardsHandUI.PerformShortRest` shows `UIManager.dialogPopup` with
  **`allowHide: false`** (`decompiled/GH.Runtime/CardsHandUI.cs:846`) — the game cannot be dismissed out of it, and
  `PerformShortRest`'s one-way teardown at `:777-793` (`shortRest.Hide()`, `longRestCard.SetActive(false)`, `ShowTabs(false)`,
  `readyButton.ToggleVisibility(false)`, `UIReadyToggle.ToggleVisibility(false)`, four `DISPLAY_CARDS_HERO_*` hotkeys disabled)
  is restored **only** by the `onFinish` delegate, which only a dialog-option press runs. The dock claims and converts the row,
  but `IsolateRow` returns a common ancestor still measuring 0×0 (the options are POOLED `InputButton`s whose layout group has
  not run). `Place()` deactivates the host. Nothing draws; both the laser and the fingertip skip a canvas that is not
  `isActiveAndEnabled` (the class doc says so at `:116-118`); `TickFit` cannot grow a rect on an inactive host
  (`LayoutRebuilder` skips `!IsActive()`); the grace is zero.

* **On screen.** An empty board where the burn confirmation should be; no hand tabs, no ready button, hero hotkeys dead.
  Only escape: the A/X flat-screen chord — which exists in a scenario but, by the standing note, **not on the campaign map**.

* **Why PLAUSIBLE and not confirmed:** the host is seeded with the target's size at `CanvasConversion.1.Core.cs:259`, so the
  degenerate case needs the isolated row *itself* to measure 0. **The discriminating reading:** a session in which the dock
  docks and the row is invisible with **no** `DECISION DOCK: … row not converted within 1.5s` warn line — that combination is
  unreachable by construction unless this is what happened.

* **Second player: not needed.**

### F5 — CONFIRMED code fact. The ownership term the mod itself argues is the only correct one reaches 6 sites; the decision-hand resolvers still read the stale-able flag

* **Where.** `CardsGameApi.LocalControlsActor` (`Cards/CardsGameApi.cs:410`) is called from exactly six places:
  `CardsGameApi.cs:317` (`IsLocalHand`), `Net/RevealGate.cs:122`, `Net/DecisionLabelMask.cs:339`,
  `Board/SelectionOwnershipFallback.cs:84`, `Board/CharacterFocus.cs:1239`, `:1366`.
  The raw `CActor.IsUnderMyControl` is still the gate in **`CardsGameApi` alone** at:
  `:157` `SelectionHandDrift()`, `:1129` `ItemPickHand()`, `:2404` `LongRestTurnHand()`, `:3658` `DrivableTakeDamageSubject()`,
  `:3689` `TakeDamageHand()`, `:3728` `InitiativeAdjustHand()`, plus `:223`, `:3499`, `:3849`, `:3911`; and outside it at
  `Board/CharacterFocus.cs:725` (`LocalMark`, the red wrong-character cue) and `:1939` (`Sample` — what every PEER sees of that
  mark).

* **Mechanism, quoted from the mod's own doc** (`CardsGameApi.cs:258-278`): `CharacterManager.OnControlAssigned` partitions the
  SET (`MyPlayer.PlayerID == controller.PlayerID`); `OnControlReleased` is `if (IsUnderMyControl) IsUnderMyControl = false;`
  with **no identity test**, so the CLEAR is not partitioned. *"After a reassignment the flag can be stale TRUE on a client
  that does not own the character and stale FALSE on the client that does — both at once."*

* **FAILURE SCENARIO.** After a control reassignment the owning client reads `IsUnderMyControl == false`. `TakeDamageHand()`
  returns null → no burn fan for his own damage decision; `LongRestTurnHand()` returns null → `PumpLongRestTurn` never drives
  the turn; `InitiativeAdjustHand()` returns null → the boots ± prompt has no hand. And `LocalMark` reads `None`, so the one cue
  that would send him back to the hidden decision is dark **on his board and on every peer's mirror**. Vanilla's only
  structural enforcement — `SwitchHand` → `Hide()` → `gameObject.SetActive(false)` — is suppressed by
  `Cards.Patches.HandSuppression`, which `CardsGameApi.cs:283-291` says in as many words makes this predicate *"the whole of
  the replacement"*.

* **Reachable: needs a SECOND player** and a control reassignment. Observed once already (maintainer item 10, 2026-09-07).

* Fix shape: route these through `LocalControlsActor` exactly as `IsLocalHand` does. One helper, ten call sites.

### F6 — CONFIRMED, and the mod already knows: `AnimateCardsLost` is an untimed park that the mod detects and deliberately does not repair

* **Where.** `Cards/Patches/DamageFlowPatches.cs:255` (`AnimatingStuckSeconds = 20f`), alert at `:428-450`.
  Game side: `CardsHandUI.AnimateCardsLost` (`:1004`) → `yield return new WaitUntil(() => animations.Count == 0);` (`:1102`).

* **Mechanism, in the mod's own alert text (verbatim):** *"AnimateCardsLost is parked, and its only bound is
  `yield return new WaitUntil(() => animations.Count == 0)` (CardsHandUI.cs:1103) — a wait on a LeanTween list with no timeout.
  Everything the flow needs comes AFTER it: the UI-lock release, onCompleteCallback (GameState.PlayerAvoidingDamage + Hide(),
  or ShowLongRested) and AnimatingLostCards = false. So the card is gone, the answer is given, and nothing advances. …
  Reported once per commit; no remedy is shipped, deliberately."*

* **Why it is in this report rather than in the clean list.** This is the ONE park inside the burn and rest flows (§2b), it has
  no timeout in the game, the mod's watchdog is an ALERT with no action, and in multiplayer the halted `ActionProcessor` queues
  every peer's copy behind a phase that never opens. **A deadlock whose only handling is a log line is, by the standing ruling,
  a defect.** The mod's reason for not repairing it is sound and should stand (writing the game's animation list is
  presentation code mutating game state) — so the fix belongs on the *entry* side: never let a `CardsHandUI` be inactive or
  mid-teardown when the commit runs, which is what `BurnCommitRescue` already does for one route. Whether it covers all of them
  is the next question, and `BURN ANIM STUCK` is the reading that answers it.

* **Reachable: yes, and it has been.** `DesyncWatch.cs:349-356` records the known case. **Second player amplifies it** to the
  whole table.

---

## 2. PARK CENSUS, re-derived at 479

### 2a. `WaitUntil` / `WaitWhile` in `GH.Runtime` — **17, not 12**

The audit's "all 12 real ones" matches today's tree at neither the total (17) nor the `WaitUntil`-only subset (11).

| # | site | predicate | resolved by a player widget? |
|---|---|---|---|
| 1 | `CampaignRewardsManager.cs:147` | `interactable != interactionChecker()` | no — `while(true)` polling loop |
| 2 | `WorldspacePanelUIController.cs:724` `DestroyDelayed` | `FlowControlActive() \|\| m_HealthBar.IsAnimated` | **no — and F1 is why that matters** |
| 3 | `WorldspacePanelUIController.cs:732` `WaitEndAnimation` | same | same |
| 4 | `UIRetirementManager.cs:67` | every peer's ready state | MP peers' UI |
| 5 | `LevelMessageUILayoutGroup.cs:80` `Autoclose` | caller-supplied `checker` | **new to this census — see N6** |
| 6 | `SaveData.cs:989` | `GlobalErrorMessage.ShowingMessage` | YES — the error box |
| 7 | `MapChoreographer.cs:2461` `WaitAllRetired` | retirement queue empty | YES — retirement UI |
| 8 | `MapChoreographer.cs:2470` `WaitDistributionEnds` | `!IsDistributing` | YES — the 370/371 gate |
| 9 | `Choreographer.cs:14474` | `hand.AnimatingLostCards` | animation (long-rest bonus chain) |
| 10 | `CardsHandUI.cs:1102` `AnimateCardsLost` | `animations.Count == 0` | animation — **the ONLY one inside any rest/burn flow (F6)** |
| 11 | `PhaseBannerHandler.cs:151` | `TransitionDone` | transition |
| 12 | `LevelEventsController.cs:855` | `TransitionDone` | transition |
| 13 | `StoryImageViewer.cs:278` | image load | IO |
| 14-16 | `SceneController.cs:1260,1287,1300` | `FFSNetwork.IsShuttingDown` | network |
| 17 | `WorldspaceUI/AttackModBar.cs:428` | `finishedReveal` | animation |

Nine further `WaitUntil` *name* matches are not yield instructions (`ConnectionState.WaitUntilSavePoint`,
`GameToken.WaitUntilSavePoint`, and three `IEnumerator<float>` MEC coroutine method names) and must not be counted.

### 2b. The parks the flows actually use — and only one of them is a `WaitUntil`

| flow | where the game parks | released by | drawn / reachable / left alone in VR? |
|---|---|---|---|
| Short rest | **no park at all** — dialog-callback driven; `UIManager.dialogPopup` with `allowHide: false` (`CardsHandUI.cs:846`) | one of two `DialogOption` buttons | dock-claimed + converted row (F4); `RestControls` caps for the entry |
| Improved short rest | none | `readyButton` `EREADYBUTTONIMPROVEDSHORTREST` → a `LoseCard` pick | `TryAdvanceLongRestTurn` drives it (2D toggle + ReadyButton unreachable in VR) |
| Long rest | `WaitingForProgressChoreographer` | `LongRestConfirmationButton` toggle **and** `readyButton` `GUI_PERFORM_LONG_REST` | **neither is reachable in VR** — `PumpLongRestTurn` is the whole answer |
| Voluntary burn | none (end-of-turn bookkeeping, `CCharacterClass.MoveAbilityCardToPile:418-487`) | — | — |
| Damage-negation burn | **the SRL WORK THREAD**, `GameState.cs:1257-1269` `Thread.Sleep(100)` — not a `WaitUntil` at all | `TakeDamagePanel`'s two toggles + `takeDamageButton` | docked row (F3/F4); `TakeDamagePanelSafety` guards double-entry |
| …then, on every commit | `AnimateCardsLost`'s `WaitUntil` (`CardsHandUI.cs:1102`) | nothing — no timeout (**F6**) | mod alerts, ships no remedy |
| Lost-card recovery | `WaitingForCardSelection` (`Choreographer.cs:7785`) → `WaitingForProgressChoreographer` | `readyButton` `EREADYBUTTONRECOVERCARD` | pick fan + board keycaps |

**The finding this table carries:** the two flows the maintainer named are held by the two shapes no `WaitUntil` census can see —
a dialog callback with no timeout, and a blocked OS thread. An audit that enumerates `WaitUntil` sites is looking in the wrong
place for exactly the burn and the rest.

### 2c. The scalar "parked" fields and the map surfaces

* `AdventureMapUIManager.lockInteractionRequests` and `ActionProgressionManager.currentAction` — unchanged in the game, and
  still read by nothing in `src/`.
* **Audit B.2 HOLDS verbatim at 479:** `WorldSurface.WantConverted` still carries `Choreographer.s_Choreographer != null`
  (`WorldUI/Surfaces/WorldSurface.cs:35-36`), and `DistributeRewardSurface` is still the only surface that restates it without
  that term (`WorldUI/Surfaces/FloatingDecisionSurfaces.cs:545-548`). Every other `WantConverted` override calls
  `base.WantConverted`. 17 of 18 surfaces are structurally dead on the campaign map.
* **Parks the audit listed that are still uncovered at 479, with evidence:** `UIGuildmasterAdventureRewardsManager` and
  `UICompletedPersonalQuestWindow` have **zero references anywhere in `src/`**. Both carry a real `UIWindow`
  (`[RequireComponent(typeof(UIWindow))]`, `UIGuildmasterAdventureRewardsManager.cs:13`), both open on the map (quest
  completion, temple devotion, achievement claim, retirement), and the catch-all IS live in the 3D map room
  (`Core/Events/VRModeStateMachine.cs:146`: `TableInFrontOfPlayer => ScenarioBoardExists || _modRoom`; gate at
  `ModalFallback.4.Tick.cs:2739` → `ModalFallback.10.CatchAll.cs:252`). So their entire coverage is the catch-all — behind the
  session-permanent churn fuse whose stated escape does not exist there (W2).

---

## 3. FLOW TRACES

Notation: **VR** interaction → **patch** → **game call** → **model** → **local visual** → **mirror**.

### 3a. Burning a card — VOLUNTARY (playing a lost action)

VR half-card poke → `CardsDriver.OnPlayRequested` (`CardsDriver.6.Flows.cs:2511`) → `CardsGameApi.PlayHalf` → the card's
`SelectedAction.CardPile` → at end of turn `CCharacterClass.MoveAbilityCardToPile` (`:418`) sends it to `LostAbilityCards`
(`:483-487`) — **but only `if (abilityCard.ActionHasHappened)`; an unresolved lost action falls back to Discarded
(`:437-438`)**, and a card with active bonuses is diverted to `m_ActivatedCards` first (`:440-442`).
Local visual: `BurnCardFx` + `TryTakeBurnFlightSlot`'s artwork hold (`CardsDriver.4.Rebuild.cs:3409`), release term
`BurnArtworkActive`, bounded by `BurnEffectStartGraceSeconds 0.5f` / `BurnEffectMaxHoldSeconds 3f`, then the flight.
Mirror: `RemoteBurnFx` + record 7.

* **Model/picture can disagree:** the three-way `ActionHasHappened` / active-bonus / Lost split means a card the player saw
  "burn" can end in Discarded. The burnt-wash rules were fixed for this at `143e6eed` and `f9c33c88`.
* **Entered twice:** no — the pile move is bookkeeping, not a prompt.
* **Left half-done:** the artwork hold is bounded at both ends; `BoardStillOwnsACardsExit` (`CardsDriver.6.Flows.cs:329`) reads
  `(_burnHoldSince, _flyingToPile)` and is what defers the next selection's overlays (item 7, 2026-09-07). **Clean.**
* **Drop the controller / walk away:** nothing is held; the flight completes on its own callback.

### 3b. Burning a card — DAMAGE NEGATION

Attack lands → `GameState.ActorDamaged` publishes `PlayerSelectingToAvoidDamageOrNot` and **parks the SRL work thread**
(`GameState.cs:1257-1269`) → `TakeDamagePanel` opens → row docked by `DecisionDockSurface` → the player picks the burn option →
`PreviewAvailableCards` (`TakeDamagePanel.cs:530`) / `PreviewDiscardedCards` (`:567`) drives
`CardsHandManager.Show(…, CardHandMode.LoseCard, …)` → the VR fan/tray pick → `CardsHandUI.OnLoseCardClick` (`:2295`) →
`GameState.Lose1HandCardToAvoidAttack` (`:1503`) / `Lose2DiscardCardsToAvoidAttack` (`:1509`) → `PlayerAvoidingDamage` (`:1517`)
sets `s_RecievedSelectedToAvoidDamageResponse = true` and releases the thread. Damage is healed back at `:1290`.

* **Disagree:** F3 — the mod re-arms the three answers while `SelectItemState` has them off.
* **Entered twice:** `TakeDamagePanelSafety.Allow()` swallows every widget entry on a panel whose `actorBeingAttacked` is
  already null; the reliable-click queue's double-press was the reason. **Guarded.**
* **Half-done:** F6, plus `BurnCommitRescue.EnsureHandCanRunItsCoroutine` (`DamageFlowPatches.cs:52`), which exists because a
  commit whose `AnimateCardsLost` coroutine was refused on an inactive hand leaves the burner stuck in
  `TakeDamageConfirmation` — and in MP that halts *every* peer's action queue.
* **Walk away / peer disconnects:** the SRL thread stays parked; no timeout in the game and none in the mod. A peer who drops
  mid-prompt loses only the mod's mirrored picture (3 s staleness sweep, `NetProtocol.cs:109`); the game's own wait is
  untouched, which is correct.

### 3c. SHORT REST — including the sacrifice and the re-draw

**The brief's ground-truth claim, adjudicated precisely.** `CardsHandUI.PerformShortRest` (`:719-875`) indexes
`playerActor.CharacterClass.DiscardedAbilityCards` with a cached RNG index (`:750-754`) and **removes nothing** — TRUE of the
method. The **flow** removes it two hops later: `FinalizeShortRest` → `ScenarioRuleClient.ShortRestPlayer` (`:957`) →
`GameState.PlayerShortRested` → `MoveAbilityCard(card, DiscardedAbilityCards, LostAbilityCards, …)` (`GameState.cs:2615`),
immediately followed by the **entire remaining discard pile** moving Discarded→Hand (`:2616-2619`).
**The mod agrees, and states it correctly in five places** — `Net/RevealGate.cs:522-523`, `:556-558`, `:798-799`, `:852`, and
`Net/NetProtocol.cs:22694` — and it draws the right consequence: *until `FinalizeShortRest` runs, the sacrifice is still a
DISCARD-pile card*, which is why the seat record and the reveal rules index the discard list. `CardsDriver.5.Interactions.cs`
adds the physical half: `SacrificeSeatNow` reports the recess the card is **physically** in (`_tray.SlotIndexOfCard`), never
what the game intends. **No disagreement found here.**

Trace: board short-rest cap → `RestControls.ShortRestRequested` → `CardsDriver.OnShortRestRequested`
(`CardsDriver.6.Flows.cs:2491`) → `CardActionQueue.Enqueue(ToggleShortRest)` → the game's `ShortRest` widget → `YesNoDialog`
(docked) → `PerformShortRest` → `PresentShortRestCard` (`CardsDriver.5.Interactions.cs:1660`) puts the sacrifice display-only in
the LEFT recess → `dialogPopup`, two options → accept = `FinalizeShortRest(loseHp: false)`; redraw = `PerformFinalShortRest`
(`CardsHandUI.cs:877-939`) → single-option popup → `FinalizeShortRest(loseHp: true)` → 1 damage (`GameState.cs:2629-2634`).

* **The redraw cannot land on the same card:** `PerformFinalShortRest` copies the discard list and removes the first card
  **from the copy only** (`:882-883`), then indexes the copy. The mod's `PresentShortRestCard` flies the old sacrifice back to
  the discard pile on the swap edge — correct, that is where it still was.
* **The redraw is only offered when `Health > 1`** (`:823`); one option otherwise. The docked row follows the game's own button
  list, so this is right by construction.
* **Left half-done — REAL, and it is the game's:** `PerformShortRest:777-793` performs a one-way teardown
  (`shortRest.Hide()`, `longRestCard.SetActive(false)`, `ShowTabs(false)`, `readyButton.ToggleVisibility(false)`,
  `UIReadyToggle.ToggleVisibility(false)`, `IsFullDeckPreviewAllowed = false`, four `DISPLAY_CARDS_HERO_*` hotkeys disabled)
  restored **only** by the `onFinish` delegate, which only a dialog-option press runs — and the popup is `allowHide: false`.
  So the popup is the whole of term (1) for this flow. **That is precisely why F4 matters here.**
  Second half-done: `shortRestLostCardID` / `shortRestAlternateLostCardID` are cleared **only** in `FinalizeShortRest`
  (`:945-946`), so a short rest abandoned without finalizing re-uses the same RNG index next time.
* **Entered twice:** guarded — `RestUiOffered` (`Cards/Caps/RestControls.cs:495-499`) is the flat UI's own predicate and the
  caps are AND-ed with it, so a stale `…Selected` latch can never keep a cap up outside the offer window.
* **Peer disconnects mid-short-rest:** nothing of the mod's survives (3 s sweep); the game's dialog is local to the rester.
* See N5 for a latent GAME defect on the redraw path.

### 3d. LONG REST

Cap → `CardsDriver.OnLongRestRequested` (`:2501`) → `ToggleLongRest` → the `CardID == −1` pseudo-card is selected
(`CardsHandUI.cs:1943-1950`) → initiative 99 (`CPlayerActor.cs:156-162`, `:198-203`, `:231-236`) → on the turn,
`Choreographer.cs:3952-3992` parks on the `LongRestConfirmationButton` toggle **plus** the `PERFORM LONG REST` ReadyButton,
**neither reachable in VR** → `CardsDriver.PumpLongRestTurn` (`CardsDriver.6.Flows.cs:1847`) drives the game's own two-step flow
→ burn pick over `CardPileType.Discarded` (**discard, not hand**) → `OnLoseCardClick:2369` → `HandleLongRest` (`:2423`) →
`GameState.PlayerLongRested` (`:2535`) — heal **2** (`:2561`), item refresh (`:2562-2563`), Discarded→Lost + full discard to
hand (`:2555-2559`).

* **Entered twice — this is the round's own defect and it is now guarded, with three independent terms:**
  `PickFlowWatch.AnswerOutstandingFor` (an EDGE at the game's commit choke point), the game's own `AnimatingLostCards` bounded
  by `LongRestAnimatingCeilingSeconds = 20f`, and `LongRestAnswerSettleSeconds`. The retry is spent **only** once
  `TryAdvanceLongRestTurn` reports the READY CLICK (`:2027-2033`), and `BlockingWindowModalActive` is read **above** the answer
  block so a modal DEFERS rather than consumes the window. I found no way past it.
* **Half-done:** `LongRestRetryMaxAttempts = 3` bounds the loop; reaching it prints `LONG REST RE-DRIVE ONCE (attempt 3 of 3)`
  and is a defect report in its own right.
* **Mirror:** `PickFlowWatch` END edge (d) `HandleLongRest` exists precisely because a non-owning client replays the rest via
  `ProxyLongRest` → `HandleLongRest` directly, touching neither `OnLoseCardClick` (edge a) nor `Hide()` (edge b) — a
  structurally reachable permanent arm on every observer, closed at 479. **Verified and correct.**
* **Peer disconnects mid-long-rest:** the answering client's `HandleLongRest` is local; observers' latches are closed by edge
  (d) or by `NoteHandHidden`'s suspend. No peer-keyed leak found.

### 3e. Other flows of the same shape

* **Item activation** — F2. `ItemsPile.TickBonusDecision` drives the active-bonus (Brille) shape frame by frame off the live
  bonus, which is the right stance.
* **Decision prompts** — `TakeDamagePanel` (3b), `dialogPopup` (3c), `ItemCardPicker` (lose/refresh), `UIAbilityCardPicker`
  (doom). The last two are covered by `DoomPickerSurface` and the `MandatoryDecision` exclusion.
* **Enhancement** — `EnhancementCommitPatch`, `HandFanEnhancementRefresh`; the peer's stale fan was closed at `ddcf2c8a`.
* **Level-up / retirement** — map-side, `ActionProgressionManager.currentAction` queue, **not read by the mod at all**.
* **Recovery of a lost card** — `CardHandMode.RecoverLostCard` over `CardPileType.Lost`, parks in `WaitingForCardSelection`
  (`Choreographer.cs:7785`), resolved by `readyButton` `EREADYBUTTONRECOVERCARD` (`CardsHandUI.cs:2131-2133`), which the board
  keycaps drive. Note `CAbilityRecoverLostCards.cs:43`: strength `int.MaxValue` recovers everything with **no picker at all** —
  no park, no widget.

---

## 4. WATCHDOGS, ESCAPES AND FALLBACKS — premise vs measurement

**A watchdog that fires is not a flow that works.** Where a deadlock is survivable ONLY via a watchdog it is marked ✗ and is, by
the standing ruling, still a defect.

| # | watchdog | where | PREMISE | premise MEASURED? | what it does to a player mid-interaction |
|---|---|---|---|---|---|
| W1 | `ClaimGraceSeconds = 1.5f` | `DecisionDockSurface.cs:132`, `:955-968` | "the row failed to ISOLATE" | partly — `_wantSince` is zeroed on every tick `Panel != null` (`:930`), so a converted-but-invisible row can never reach it (**F4**) | hands the whole window to the generic float; no state written. ✗ for F4 |
| W2 | churn fuse `ChurnMaxFloats = 3` / `ChurnWindowSeconds = 60f` | `ModalFallback.10.CatchAll.cs:81`, `:85`, `:351-372` | "decisions open once and wait, so a re-floater is a cycling HUD banner" | NO — it counts FLOATS, not cycles; hover cards had to be exempted by hand after it ate the player twice | **session-permanent** (`ChurnSuppressed`, cleared only in `CatchAllReset:1440`). Its stated escape — *"manual A/X screen chord still reaches it"* (`:378`) — **is false on the campaign map** |
| W3 | `UnpairedArmSeconds = 90f` | `CardsDriver.6.Flows.cs:579`, `:548-577` | "an ARM standing 90 s is worth a line" | YES, and it says so: it deliberately **repairs nothing** | nothing — report only, by design and correctly (standing the banner down would hide a live request) |
| W4 | `LongRestRetryWindowSeconds = 3f` / `MaxAttempts = 3` / `AnimatingCeiling = 20f` | `CardsDriver.6.Flows.cs:1837`, `:1845`, `:1829` | "the game did not act on an answer it was given" | YES — three terms: the commit EDGE, the game's own `AnimatingLostCards`, and the settle floor | re-drives `PERFORM LONG REST`, i.e. re-opens an answered step. Loud, bounded, spent only on the READY CLICK. The best-built one here |
| W5 | `HealOwnerIfRefusingAnOpenPick` | `PickFlowPatches.cs:783-865` | "the mod is refusing a pick the GAME has open" | YES — four live GAME reads (`Mode`, `MaxSelectedCards`, `PickHandIsPresented`, `IsLocalHand`); writes no game state | releases and re-arms the latch in the same frame. Its doc says it **should never fire**; two firings in one session is a new defect report |
| W6 | `BurnCommitWatch` `StuckSeconds = 8f` | `DamageFlowPatches.cs:247`, `:323-375` | "the client is still in `TakeDamageConfirmation` 8 s after a legal commit" | YES — reads `FFSNet.ActionProcessor.CurrentPhase` | **nothing.** `VRLog.Alert` `BURN COMMIT HANG`. ✗ — detection only |
| W7 | `AnimatingStuckSeconds = 20f` | `DamageFlowPatches.cs:255`, `:382-457` | "`AnimatingLostCards` is still true 20 s after the commit" | YES — the game's own flag | **nothing.** `VRLog.Alert` `BURN ANIM STUCK`, closing *"no remedy is shipped, deliberately"*. ✗ — **this is F6** |
| W8 | `DormantReleaseSeconds = 45f` | `ModalFallback.9.Spawn.cs:2703` | "a dormant panel is finished with" | NO — elapsed time only | irreversible release of a floated window. The `GoDormant` hide above it (`:3379-3391`) is reversible and correct; this tail is not |
| W9 | `BlindAlertSeconds = 3f` + the distribute rescue | `FloatingDecisionSurfaces.cs` | "the game is distributing with nothing floated" | YES — `UIDistributeRewardManager.IsDistributing` is the game's own field, and the alert lands before the rescue | forces the 2D composite up; popup restored to 2D. ✗ by the ruling: the map deadlock is survivable only through it |
| W10 | `MandatoryDecision` rescue screen | `MandatoryDecision.cs:433-458` | "a mandatory decision stands with no reachable control" | partly — the latch `FlatScreen.RescueScreenActive` has **two owners** and is not ref-counted (`:451` then `:458` `if (!alreadyStanding) _rescueWindow = window;`). If `DistributeRewardSurface` raised it first, the mandatory window is never recorded and `TickMandatoryRescue` can never release it | its own doc: *"`RequestRescueScreen` has deliberately no timeout, and on the campaign map the player cannot chord it away either"* |
| W11 | `StoryDelayStaleSeconds = 30f` | `CardsGameApi.cs:2736`, `:2739-2761` | "`StoryController.DisplayDelayInEffect` is a stale GAME static" | YES — ANDed with a live controller-existence test | the underlying `public static bool` is cleared only inside a coroutine a scene transition kills, and **nothing resets it on load**. Consequence here is display-only, and the doc says so — but the game static itself is never reset by the mod |
| W12 | `CardArtGuard` `InFlightGraceSeconds = 6f`, `MaxHealsPerAdoption = 3` | `Cards/Art/CardArtGuard.cs:72`, `:94` | "a `ShowCard()` would unload an in-flight sprite" | YES — `ImageAddressableLoader.ReferenceCount` | defers the show; bounded, self-healing, per-adoption reset. Correct |
| W13 | `StaleTimeoutSeconds = 3f` | `NetProtocol.cs:109`, `NetAvatarDriver.cs:4023-4041` | "this peer is gone" | proxy (no update for 3 s) — but the ACTION is purely mod-local: avatar, mirrors, remote figures/props released | nothing game-owned is touched. Correct |
| W14 | `FigureBusy` / `FigureStallWatchdog` | `Board/FigureGrab/FigureBusy.cs:334-338` | "the rule engine is genuinely busy on this figure" | **YES since 479** — three of four clauses are direct reads and the fourth proxy is now corrected by the game's own two damage-wait fields | refuses a grab, with an audible refusal (`PlaySound_UINegativeSelect`). **But it is a grab-START gate only** — nothing re-evaluates while the figure is already held, which is the gap F1 walks through |
| W15 | `DesyncWatch` | `Net/Desync/DesyncWatch.cs:349-356` | "the processor is halted and this action will wait for ever" | it is an INSTRUMENT, not a recovery — and it still names a cause (`AnimateCardsLost` on an inactive hand) that `BurnCommitRescue` now handles | reports only. See N3 |
| W16 | A/X flat-screen chord | `FlatScreen.ManualScreenActive`, six `WantConverted` overrides | "the player can always raise the 2D screen" | **NO — this is the known trap.** Two gates kill it on the campaign map; `DecisionDockSurface.cs:126-127` still asserts *"the universal rescue"* | releases the conversion; correct where it exists |

**Held-ness.** I checked every teardown path for a term about the player's hand. `ItemsPile.TickUseResolving` defers a finish to
the release when `chip.Holder != null` (`:2710-2714`) — correct. `PickFieldSeatNow` excludes `card.IsHeld` — correct.
`BoardStillOwnsACardsExit` holds the next overlays while a card is in flight — correct. `VRCard.Vanish`, `PlayAppear` and
`FlyFromPile` self-guard on `IsHeld`, and `VRCard.Tick:2140-2150` cancels a flight on a re-grab, so a flight launched on a held
card is corrected on the next frame. **`ActorBars.cs:664-666` is the exception:** it deactivates on `HeldFigures.Owns(actor)`
with a term for the AttackModBar flow and none for the health-bar animation. That is F1. No mod path force-releases a held
figure that dies (`grep -rn IsDead src/GloomhavenVR/Board/` → only grab-START gates, `FigureGrabbable.cs:361`).

---

## 5. WHERE THE PRIOR AUDIT AND THE BRIEF ARE NOW WRONG

Stated plainly, because re-deriving is the job.

1. **"the 12 real `WaitUntil`/`WaitWhile` sites"** — there are **17** at today's tree (§2a). And the two flows the maintainer
   named are held by neither: the short rest by an `allowHide: false` dialog callback, the damage burn by a blocked OS thread
   (`Thread.Sleep(100)`). **A `WaitUntil` census cannot see either.** The one `WaitUntil` that *is* in these flows —
   `AnimateCardsLost` — the old audit filed under "animation", and it is F6.
2. **Audit A.5, "a latent game bug, NOT currently triggered by the mod. Verified: no `.SetActive(` anywhere in
   `WorldUI/Conversion/*.cs`"** — that negative measured the wrong population. The two `SetActive(false)` calls on a host holding
   adopted game content are in `WorldUI/ActorBars.cs:666` and `WorldUI/Surfaces/DecisionDockSurface.cs:1036`.
   (Neither reaches A.5's four map windows — those are map-side and both surfaces carry the Choreographer term — so those four
   stay latent. But the general claim "the mod deactivates no adopted subtree" is false, and F1 is what it cost.)
3. **Audit D.3.1 — FIXED.** The `EmptyRefused` retry condition is now reachable: `EmptyRefusedNow`
   (`ModalFallback.9.Spawn.cs:2572-2604`) has a second release on `DrawsAnythingLoose`, with the argument for why it cannot arm
   the churn fuse written out beside it.
4. **Audit D.3.2 / C.5.1 — FIXED.** `ServiceDeferredHosts` now restores scene membership on the free path
   (`CanvasConversion.4.Lifecycle.cs:509-512`).
5. **Audit A.6's open question — ANSWERED: the mod never writes `isUseGamepadInPc`.** Every one of the ~20 hits in `src/` is a
   read or a comment. The three `!GamePadInUse`-branch `Awake` wirings are therefore safe.
6. **Audit C.2 — STILL OPEN**, and worse than recorded: the churn fuse's own stated escape is false on the map (W2).
7. **Audit B.2 — HOLDS verbatim** (§2c).
8. Brief: *"a widget that only the controlling client's code path raises is a deadlock for the other client BY CONSTRUCTION"* —
   correct, and `59efed69` is the instance (`TakeDamagePanel.ShowOtherPlayer` raises neither `UIUseItemsBar.ShowItems` nor
   `UIActiveBonusBar.ShowReduceDamageActiveBonuses`, so `BarBelongsTo` was false by construction on a watcher).
   But the mirror direction is safe by construction: `RemoteWidgetMirror` **clones** (`:618` `Object.Instantiate`) and strips
   every `Selectable`, collider and raycaster (`:1190`, `:1197`), so the mod cannot take a decision widget away from the client
   that owns it through that path, and a peer can never press one.

---

## 6. WHAT I CHECKED AND FOUND CLEAN

* **`PickFlowWatch`** (`Cards/Patches/PickFlowPatches.cs`) — the arm-side control test (`MayThisClientPickFor`, one-way: an
  unclassifiable arm is armed, never declined), the four END edges, `NoteHandHidden`'s tab-switch SUSPEND with its two terms
  (`AnimatingLostCards` first, then `CardsHandManager.CurrentHand`), and `NoteEnd`'s idempotence. I looked for a way to make an
  END edge fire on a live pick, or an ARM stick on a foreign hand, and did not find one.
* **`PumpLongRestTurn`** — three independent hold terms, the retry spent by the STEP and not the clock, the modal check above
  the answer block. No route past it found.
* **`RestControls.RestUiOffered`** — the flat UI's own predicate quoted verbatim, plus the life term; a stale `…Selected` latch
  cannot outlive the game's offer window.
* **Short-rest sacrifice location** — the mod's model matches the game's exactly (§3c), in five places, with the right
  consequence for the reveal rules.
* **Peer answer path** — a peer can watch a decision and can never answer it; the mirror clones and strips input;
  `RemoteDecisionPrompt` composes text only from a 3-bit variant id out of the receiver's own localization table.
* **Peer disconnect** — no peer-keyed in-flight state survives the 3 s staleness sweep (avatar, figures, props, focus, census
  all released at `NetAvatarDriver.cs:4033-4041`). Two hygiene notes in §7.
* **Version mismatch** — not a deadlock and cannot become one: the dialog is mod-owned world-space, self-clamping into the view
  cone, poke- and laser-clickable, closes before it fires; the game runs underneath (`Net/VersionDialog.cs:12-17`, `:100-130`).
* **`TakeDamagePanelSafety`** — the double-press swallow on a panel whose `actorBeingAttacked` is already null.
* **`ModalFallback.11.PreConvertHide`** — still bounded, still restores unconditionally with a named reason.
* **`VRCard` held-ness guards** — `Vanish`, `PlayAppear`, `FlyFromPile`, and the `Tick` re-grab cancel at `:2140-2150`.
* **`ActorBars`' AttackModBar guard** — correct for the term it names; F1 is the term it does not name.
* **`AoeControl`, `PlacementDiagnostics`, `SelectionGuardPatches`, `CharacterFocus`** reads of `m_WaitState` — all read-only.

---

## 7. NOTES (no failure scenario — kept separate, as instructed)

* **N1.** `NetAvatarDriver.RemovePlayer` (`:4048`) has no callers anywhere in `src/`, and is one cleanup call short of the
  staleness path it duplicates (`PeerCardFaceCensus.ReportPeerGone` missing). Harmless while unreachable; a trap the day it is
  wired. Wire it or delete it.
* **N2.** `VersionGuard.Peers` / `Handled` are keyed by peer id and pruned only wholesale in `Reset()`. A leave→rejoin under the
  same id cannot re-raise the mismatch dialog. Bounded by roster; not a deadlock.
* **N3.** `DesyncWatch.cs:354-356` still tells a future reader that a lose/burn commit refused on an inactive hand is the live
  cause, with no mention that `BurnCommitRescue` handles it. An instrument asserting a retired mechanism.
* **N4.** `DecisionDockSurface.cs:126-127` asserts *"The manual A/X screen chord stays the universal rescue"*. True in this
  file's scope (a scenario), false as written — the campaign map kills it with two gates.
* **N5.** GAME defect, recorded so nobody re-derives it: `PerformFinalShortRest` assigns `_shortRestedCard` **before** the
  `LevelEventsController` override that can reassign the card (`CardsHandUI.cs:889` vs `:890-899`) — the opposite order to
  `PerformShortRest` (`:755-765`). On a scripted short-rest event the mod's LEFT recess would show the wrong card, on the redraw
  path only. `cardUI` itself is correct, since it reads the reassigned closure local.
* **N6.** `LevelMessageUILayoutGroup.cs:80` `Autoclose(Func<bool> checker)` is a `WaitUntil` on a caller-supplied delegate and
  appears in no prior census. Not investigated further; listed so the next census does not miss it again.
* **N7.** `StoryController.DisplayDelayInEffect` is a `public static bool` whose only clear sits inside a coroutine a scene
  transition kills, and nothing resets it on load. The mod mitigates with `StoryDelayStaleSeconds = 30f` ANDed with a live
  controller test (W11); the game static itself stays latched. Display-only today.

---

## 8. IF ONLY ONE THING IS DONE THIS ROUND

**F1**, and not as an instance. The rule that generalises all five fixed instances plus F1 and F2 is one sentence:

> Where the mod gates on a state the game also gates on, it must read the GAME'S OWN EXPRESSION IN FULL — every term, from the
> game's own call sites — and never a subset of it.

ModBuild 479 wrote that rule into `FigureBusy` for three fields. `ActorBars.cs:596` needs `|| controller.m_HealthBar.IsAnimated`
beside `FlowControlActive()` — the game writes the pair together in five places. `CardsGameApi.cs:1874` needs the same three
fields `FigureBusy` now reads. A lint that greps for a mod read of `IsProcessingOrMessagesQueued` or `FlowControlActive()` **not**
accompanied by its sibling terms would close the class instead of the next instance.
