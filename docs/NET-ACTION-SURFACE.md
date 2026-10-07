# Mod patches on types that receive network actions

> Verified by `scripts/check-desync-surface.py` (run from `refactor-guard.sh check`).
> A patch class on a receiver type that is **not** in the table below fails the gate.
> The verdict column is a human judgement, recorded once; the check is that no new
> patch slips in unjudged.

## Why this document exists

`ActionProcessor.TryProcessNextAction` wraps the entire game-action dispatch in

```csharp
catch (Exception ex) { FFSNetwork.HandleDesync(ex); }
```

and `GHNetworkControllable`'s eight Bolt state callbacks do the same. So an exception
thrown anywhere under that dispatch — **including out of one of our patch bodies** — is
not logged as a mod bug. It is shown to the player as the *game's* "Desynchronization
occurred" dialog, and the session is shut down with a single Main Menu button.

`check-desync-surface.py` identifies receiver types reachable from the native action
dispatch and prints the current patch/verdict counts. The table below is the reviewed
classification; use the checker output for totals as patches are added.

Frequently reached receivers include `Choreographer`, `CardsHandManager`,
`NewPartyDisplayUI` and `UIReadyToggle`. `SceneController` is included as of build 516:
its load-iterator hook must preserve native loading and callback behavior even when
mod presentation cleanup fails. Dispatch frequency alone does not determine safety;
even a rarely invoked handler can end a multiplayer session if an exception escapes.

Full analysis, with the evidence:
[`.planning/multiplayer/DESYNC-ANALYSIS.md`](../.planning/multiplayer/DESYNC-ANALYSIS.md).

## The verdicts

| verdict | meaning |
|---|---|
| **ISOLATED** | The body is wrapped in `Net.Desync.DispatchGuard`. A throw is logged with its stack and never reaches the game's dispatch. |
| **SELF-GUARDED** | The body carries its own `try/catch` with a stated fallback. |
| **GUARDED-DEEPER** | The body's only real work is one call that is already guarded one level down (today: `VREvents.Invoke`, whose own doc says a subscriber exception must never propagate into the game's message pump). Wrapping it again would add noise, not safety. |
| **CANNOT-THROW** | The body is a constant expression or a single field write. There is nothing in it that can throw. |
| **WAIVED** | Judged exposed but deliberately left unguarded, with the reason in the note. There are none today. |

## The table

| patch class | receiver | verdict | note |
|---|---|---|---|
| `AllCardsViewerBlock` | CardsHandManager | **CANNOT-THROW** | Two skip prefixes: a config bool, then a throttled `Log` whose only game read is null-guarded (`player != null ? player.GetPrefabName() : "<null>"`). |
| `CardsHandManager_ShowAll_Patch` | CardsHandManager | **GUARDED-DEEPER** | `HandSuppression.Arm()` (one bool write) then `VREvents.Raise` with a literal payload. |
| `CardsHandManager_ShowHands_Patch` | CardsHandManager | **ISOLATED** | The one Show postfix that reads live game state (`ActivePlayer`, `m_PushPopCardHandMode`) *before* reaching the guarded `Raise`. Wrapped in ModBuild 334. |
| `CardsHandManager_ShowList_Patch` | CardsHandManager | **GUARDED-DEEPER** | As `ShowAll`: a bool write and a `Raise` whose payload comes from the patched method's own arguments. |
| `Choreographer_HeldFigureAction_Patch` | Choreographer | **SELF-GUARDED** | Main-thread cosmetic hold release before native movement/aim/animation reads. The presentation probe is inside `try/catch`; a failure is logged and the original message always runs. No native queue, arguments or gameplay state are changed. |
| `Choreographer_ProcessMessage_Patch` | Choreographer | **GUARDED-DEEPER** | Null-checks the message, then `VREvents.Raise`. Runs inside Choreographer's ~8 ms/frame pump, so it must also stay cheap. |
| `Choreographer_SetChoreographerState_Patch` | Choreographer | **GUARDED-DEEPER** | A single `VREvents.Raise` of the enum argument. |
| `Choreographer_TileHandler_OwnershipGuard` | Choreographer | **SELF-GUARDED** | Full `try/catch` returning `true` — "a throwing guard must never eat the game's click dispatch". The model the other verdicts are measured against. |
| `InitiativeHoverCardBlock` | CardsHandManager | **CANNOT-THROW** | A config bool, a throttled `VRLog.Debug`, and one null-guarded `GetPrefabName()`. |
| `MapQuestReadyPress` | UIReadyToggle | **SELF-GUARDED** | A **diagnostic** postfix on `UIReadyToggle.ReadyUp(bool, bool)` — the method every quest/city-event/rewards ready-up press funnels through, on a type that receives the `ReadyUpPlayer` / `AllPlayersReady` / `ReadyProceed` actions. No return value and no `__result`, so it cannot change what the press did. Its whole body is inside its own `try/catch` with an empty handler and a stated reason: this line exists so a hardware round can tell a press the game REFUSED from a press that never arrived, and a diagnostic must never be able to take the ready-up — or somebody's multiplayer evening — down with it. |
| `MapQuestDepartureValidation.InitializeSeam` | UIReadyToggle | **SELF-GUARDED** | Reads native network/phase state inside `try/catch`; only a measured online map quest enables the existing departure-validation argument. Probe failure clears mod authorization and leaves the original argument and initialization intact. |
| `MapQuestDepartureValidation.PlayerLeftSeam` | UIReadyToggle | **SELF-GUARDED** | Both native-state capture and the post-handler blocked-state probe catch failures and clear only mod authorization. The original departure handler always runs; no roster, vote, callback or ACK is synthesized. |
| `MapQuestDepartureValidation.ExplicitInputSeam` | UIReadyToggle | **SELF-GUARDED** | A guarded probe admits only the original visible Cancel input for the measured departed-lobby state. The finalizer releases its mod scope even if the native input throws; failed post-input probes clear authorization and the original exception is returned unchanged. |
| `MapQuestDepartureValidation.ProgressEndSeam` | UIReadyToggle | **SELF-GUARDED** | The guarded native-context probe consumes only the captured Cancel animation's authorization. A failed probe clears it and permits native completion; the finalizer releases its mod scope and returns the original exception unchanged. |
| `MapQuestDepartureValidation.ReadyUpSeam` | UIReadyToggle | **SELF-GUARDED** | Exact controller/quest/phase/current-native-roster probes are guarded before changing the native unready-validation argument. Failed probes clear mod authorization and preserve the argument; bounded diagnostic failure cannot interrupt the original ReadyUp. Authorization expires after one actual withdrawal. |
| `MapQuestDepartureValidation.CancelProgressSeam` | UIReadyToggle | **CANNOT-THROW** | `ReferenceEquals` and one mod authorization field write. No native property, collection, Unity object, log or callback is read. |
| `MapQuestDepartureValidation.ResetSeam` | UIReadyToggle | **CANNOT-THROW** | `ReferenceEquals` and clearing private mod references/value fields only. Native Reset has already run and its gameplay state remains untouched. |
| `MapSelectionTransition` | NewPartyDisplayUI | **SELF-GUARDED** | The native map-options prefixes catch game-state lookup errors and fall back to native behavior; the two selection prefixes likewise catch and return `true`. Error reporting cannot throw back into dispatch. Finalizers only decrement the thread-local scope and forward the original exception unchanged. The only skipped callbacks are deselection of the already selected locally owned portrait during a synchronous map-options transition. |
| `TownWindowMapSwitch` | `UIGuildmasterHUD.UpdateCurrentMode` | **SELF-GUARDED** | Presentation-only map switch; discovery catches before mutation, scoped native map visibility restores the service mode in finally, and failure reporting is bounded. No gameplay action or continuation is suppressed. |
| `TownServiceTutorialShopExitPatch` | UIShopItemWindow | **CANNOT-THROW** | Prefix saves the previous thread-local shop reference; finalizer restores it through a default-safe value scope. These are field writes only. Native Exit and its exceptions are neither skipped nor replaced. |
| `TownServiceTutorialShopHiddenPatch` | UIShopItemWindow | **SELF-GUARDED** | Capture and completion each catch mod probe failures and report at most once. The original OnHidden always runs; only an explicitly closing shop's exact pending native BuyItem promise is completed after its selection/listener cleanup. Manager, save, serialized step and promise identities are revalidated; successors and temporary presentation hides remain untouched. |
| `PostQuestRewardQueuePatch` | MapChoreographer | **SELF-GUARDED** | Read-only native reward-opening provenance. Capture failure disables this scope and reports once; the void prefix never suppresses the original reward flow. Its finalizer restores only the prior mod scope and returns the original native exception unchanged. |
| `PostQuestRewardSceneEndPatch` | MapChoreographer | **SELF-GUARDED** | Postfix clears mod reward references/history through a guarded teardown helper. Failure reports once and cannot interrupt native scene destruction; no native queue, reward or callback is changed. |
| `PartyPanelStackingHide` | NewPartyDisplayUI | **ISOLATED** | A *skip* prefix on `NewPartyDisplayUI.Hide`, on a type that receives 10 actions. Wrapped in ModBuild 334; on a throw it returns `true`, so vanilla `Hide` runs — the behaviour this suppression refines, not one it may depend on. |
| `Placement_Click_Diagnostics` | Choreographer | **ISOLATED** | A **diagnostic** prefix on the heaviest receiver in the game. Wrapped in ModBuild 334: a log line must never be able to end somebody's multiplayer evening. |
| `SceneController_LoadScene_CardLifetime` | SceneController | **SELF-GUARDED** | The postfix preserves the original iterator if wrapper construction fails. At first execution, the mod-only ownership release is guarded separately from the native iterator; hover cleanup failures cannot skip returning other borrowed faces. Native `MoveNext`, yields, disposal and exceptions are forwarded unchanged. No native load or mandatory decision is skipped. |
| `SceneController_LoadingComplete_SceneryBudgetPatch` | SceneController | **SELF-GUARDED** | A void prefix on `DisableLoadingScreen()` finishes local decorative presentation only. Its entire preparation is inside `try/catch`; failure reports once and the original loading-screen closure always runs. No native loading flag, iterator, argument, callback, gameplay state or return value is changed. |
| `StructuralInstancing_LoadedPatch` | SceneController | **SELF-GUARDED** | A void prefix prepares only optional native material instancing before loading completion. Both the prefix and driver catch preparation errors; the finite failure owner restores its flags and leaves native rendering and quest continuation available. It never suppresses the original, changes callback arguments, writes loading flags or invokes gameplay actions. |
| `SceneController_Loaded_EnvironmentBudgetPatch` | SceneController | **SELF-GUARDED** | A void prefix finishes only optional floor/structural/ambience preparation before the native loading screen closes. Its `try/catch` invokes the fail-open lifecycle helper: optional work stops, owned rendering restores and one bounded failure report is emitted. The prefix never suppresses native continuation or changes loading flags, callbacks, arguments or gameplay state. |
| `ScenarioRetryStart_Patch` | SceneController | **GUARDED-DEEPER** | The three restart entry points call `TickGuard.Run`; method-name classification and local retry/preserve-only lifetime writes execute inside that guard. The void prefix never skips or changes native restart or ready-up behavior. |
| `ScenarioRetryDestination_Patch` | SceneController | **CANNOT-THROW** | An enum comparison and null-checked access to the live driver reset only initialized local lifetime fields for a non-scenario destination. No game singleton, collection, callback, transform or logging is invoked. The original load iterator remains unchanged. |
| `TakeDamagePanelSafety` | TakeDamagePanel | **SELF-GUARDED** | `Live`/`Allow` are null-safe by construction and the one body that does real work, `AutoUseMandatoryActiveBonuses`, is wholly inside its own `try`. |
| `TakeDamagePanel_BurnHover_Skip` | TakeDamagePanel | **CANNOT-THROW** | Four expression-bodied `=> false`. |
| `UIEventPanel_ClientContinueRoadEvent_Patch` | UIEventPanel | **ISOLATED** | The host's judgement seam for the encounter-choice feature (`Net/EncounterChoice.cs`), and the one patch here that runs INSIDE `ProcessSideAction`, whose own `catch` calls `HandleDesync` **and rethrows**. The whole body is inside `DispatchGuard.Run`. Its `onThrow` is chosen per case and is not a shrug: a VANILLA arrival falls back to `true` so the game's own replay runs untouched; a CLIENT REQUEST falls back to `false`, because letting vanilla run on one would read the side action's unset `SupplementaryDataIDMed` as option 0 and either press the wrong option or throw "No button with ID 0 found" into the dialog this ledger exists to prevent. The replay depth is raised as the prefix's first statement and released from a `[HarmonyFinalizer]` returning `void`, so the game's own exception is preserved exactly and the latch cannot leak through it. |
| `UIEventPanel_CompleteEvent_Patch` | UIEventPanel | **ISOLATED** | The "Leave" press. Same body and same guard as `UIEventPanel_ContinueEvent_Patch`; see that row. |
| `UIEventPanel_ContinueEvent_Patch` | UIEventPanel | **ISOLATED** | The local option press. Wholly inside `DispatchGuard.Run`, with an `onThrow` that differs by side and is the point of the wrapping: on the HOST a throw falls back to `true` so the host's own press still works, on a CLIENT to `false`, because vanilla on a client advances its screen locally and tells nobody — the divergence the feature exists to avoid. Reached from the game's own button callback rather than from the action dispatch, but it re-enters from `ClientContinueRoadEvent` on every replay, i.e. from under the dispatch, which is why it is classified here rather than waived. |
| `UIAbilityCardPicker_Hide_Patch` | UIAbilityCardPicker | **SELF-GUARDED** | A void prefix on `Hide()`, so it cannot skip or alter the game's close. Its whole body is one call to `SurfaceCloseEdge.Publish`, which is wholly inside its own `try/catch` and reports once per process — the close-edge notification that starts a decision panel's vanish must never be able to end somebody's multiplayer evening. |

## Adding a patch

If `check-desync-surface.py` fails on your new patch class: read the body and ask what in
it can throw when the game is mid-dispatch — a `Singleton<T>.Instance` that has not Awoken,
a collection that is empty this frame, a destroyed Unity object behind a non-null C# field.
Then either wrap it in `DispatchGuard` (cheap, and the default answer for anything that
reads game state) or record why it cannot throw. Both are one row here.

`scripts/check-desync-surface.py generate` prints the table rows for the current source.
