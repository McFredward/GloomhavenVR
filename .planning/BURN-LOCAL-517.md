# Native burn replay audit — build 517

Base: `1915fd42` (`dev`, build 516). Worker: `/tmp/gvr-burn-local517`.

## Evidence and source-proven cause

The latest supplied hardware files still identify local build 515 and remote build 500;
there is no new build-516 trace proving which exact refresh produced the user's repeated
short-rest burn. The native source contains the complete replay mechanism:

- `CardsHandUI.FinalizeShortRest` calls `ToggleEffect(true, BurnCard)` before committing
  `ScenarioRuleClient.ShortRestPlayer`. The subsequent lost-pile refresh calls
  `FullAbilityCard.SetPile(Lost)` → `ToggleEffect(true, LostMode)` on the same face.
- Both task names run the same native animated timeline. `ToggleEffect` calls `RestoreCard`
  first; `ToggleAdditiveEffect` unconditionally stops the previous coroutine. Thus a second
  request clears the shader to its blue starting state and starts the ramp again.
- Short-rest preview exit also calls `RefreshPile`, whose false effect request resets the
  card and runs the native no-ramp arm. A latched task alone did not protect playback.
- Long rest and damage sacrifices converge on native lost-pile updates; played losses use
  `TryPlayBurnAnimation(BurnCard)`. All share these destructive effect entry points.
- Historical hand construction initializes original widgets as Hand even when their model
  is already lost. A later `SetType(Lost)` starts another native burn. This is reconstruction,
  not a new loss.
- Native item `Show` and forced `UpdateState` can both request Consumed. Item effects do not
  store a coroutine handle, so these requests can start overlapping writers. Its native ramp
  is only 0.001 seconds; this is a distinct one-frame/reset issue, not a second two-second burn.

## Changes

- Preserve one original ability burn iterator across BurnCard/LostMode aliases, additive
  requests, false refreshes and standalone restores. Keep native task membership sufficient
  for the existing completion/readout path. No rule-library model or gameplay callback is skipped.
- Retire per-face playback before actual pool recycle or scene teardown (root integration).
  Distinguish disposed/failed playback from successful completion. Obsolete callbacks cannot
  recreate retired ownership or modify the current floor bookkeeping.
- Resolve adopted identity through `CardFace.OwnerOf`, then a validated original parent, then
  native full-card metadata. Actor hand/lost collections override stale `CurrentCardPile` stamps.
- Keep completed loss history weakly keyed by the original card across widget replacement.
  New widgets initialized for an already-lost model receive the game's original no-ramp
  output. Classification never finishes an existing running original. A no-ramp settle must
  actually paint before it receives the completed per-face guard. Real recovery retires history.
  A replacement created during an existing burn waits for that original and then settles; it
  never runs another ramp. Each wait validates original identity, model epoch and exact widget
  ownership. Retirement/recovery releases waiters; obsolete callbacks cannot paint a new card.
- Defer a standalone active-card reset requested mid-burn until the original iterator ends;
  the native final appearance does not depend on another `SetPile` call afterward.
- Register burn timeline observation on its first native step, not when an unused iterator is
  merely constructed. Mod interception failures preserve the original enumerator; native
  execution exceptions remain native.
- Protect item effect reset/alias entry points, completion and original item identity. Retire
  item playback on native pool return. Newly hosted already-consumed items are classified
  immediately before their first `Show` (driver integration), then paint the native no-ramp
  arm exactly once. Missing original artwork permits a later successful first paint.

## Validation

- `scripts/burn-replay-tests.sh`: 496 runtime assertions, 3 production bindings and 13 runtime
  negative controls. Executes extracted production interception methods and original wrapper/
  episode classes against explicit native reset/start adapters. Covers four ability origins,
  repeated aliases, completion, recovery, replacement, authoritative stale stamps, registry
  identity, historical creation, standalone reset, native bail, disposal, obsolete callback,
  deferred active reset and exception identity.
- `scripts/item-burn-tests.sh`: 225 runtime assertions and 11 runtime negative controls.
  Extends the existing native-timeline/lifetime harness with duplicate effect entry points,
  recovery, pool retirement, historical host paint and missing-art retry.
- Strict Release build: zero warnings and errors.
- Full integration guards, module registration and multiplayer validation belong to the root.
  `scripts/burn-completion-tests.sh` binding follows the driver's `CompleteBurnClaim(widget)`.

No headset result is claimed. Short/long rest, damage sacrifice, played loss, active-card
completion and consumed items still need the next local/remote hardware test, including
character changes and later browsing/recovery. No asset bundle or wire-format change.
