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

`check-desync-surface.py` knows **37** such receiver types, weighted by how many of the
~121 dispatch entries reach each one. The mod patches **seven** of them, and the table
below carries **17** patch classes (3 CANNOT-THROW, 4 GUARDED-DEEPER, 6 ISOLATED,
4 SELF-GUARDED) — the gate prints those figures every run, so read them from it rather
than from here.

The seven, with their weight: `Choreographer` (27 actions — the heaviest in the game),
`CardsHandManager` (13), `NewPartyDisplayUI` (10), `UIReadyToggle` (8),
`TakeDamagePanel` (3), `UIAbilityCardPicker` (2), `UIEventPanel` (1). Note that this is
*not* "the top seven": `MapChoreographer` (11) outweighs four of them and the mod does
not patch it. Weight is how much traffic a receiver sees, not how exposed our patch is —
`UIEventPanel` is one action and is the most carefully guarded row in the table.

Full analysis, with the evidence:
[`.planning/multiplayer/DESYNC-ANALYSIS.md`](../.planning/multiplayer/DESYNC-ANALYSIS.md).

## The verdicts

| verdict | meaning |
|---|---|
| **ISOLATED** | The body is wrapped in `Net.Desync.DispatchGuard`. A throw is logged with its stack and never reaches the game's dispatch. |
| **SELF-GUARDED** | The body carries its own `try/catch` with a stated fallback, written before this ledger existed. |
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
| `Choreographer_ProcessMessage_Patch` | Choreographer | **GUARDED-DEEPER** | Null-checks the message, then `VREvents.Raise`. Runs inside Choreographer's ~8 ms/frame pump, so it must also stay cheap. |
| `Choreographer_SetChoreographerState_Patch` | Choreographer | **GUARDED-DEEPER** | A single `VREvents.Raise` of the enum argument. |
| `Choreographer_TileHandler_OwnershipGuard` | Choreographer | **SELF-GUARDED** | Full `try/catch` returning `true` — "a throwing guard must never eat the game's click dispatch". The model the other verdicts are measured against. |
| `InitiativeHoverCardBlock` | CardsHandManager | **CANNOT-THROW** | A config bool, a throttled `VRLog.Debug`, and one null-guarded `GetPrefabName()`. |
| `MapQuestReadyPress` | UIReadyToggle | **SELF-GUARDED** | A **diagnostic** postfix on `UIReadyToggle.ReadyUp(bool, bool)` — the method every quest/city-event/rewards ready-up press funnels through, on a type that receives the `ReadyUpPlayer` / `AllPlayersReady` / `ReadyProceed` actions. No return value and no `__result`, so it cannot change what the press did. Its whole body is inside its own `try/catch` with an empty handler and a stated reason: this line exists so a hardware round can tell a press the game REFUSED from a press that never arrived, and a diagnostic must never be able to take the ready-up — or somebody's multiplayer evening — down with it. |
| `PartyPanelStackingHide` | NewPartyDisplayUI | **ISOLATED** | A *skip* prefix on `NewPartyDisplayUI.Hide`, on a type that receives 10 actions. Wrapped in ModBuild 334; on a throw it returns `true`, so vanilla `Hide` runs — the behaviour this suppression refines, not one it may depend on. |
| `Placement_Click_Diagnostics` | Choreographer | **ISOLATED** | A **diagnostic** prefix on the heaviest receiver in the game. Wrapped in ModBuild 334: a log line must never be able to end somebody's multiplayer evening. |
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
