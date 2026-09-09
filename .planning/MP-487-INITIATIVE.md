# MB487 — stable remote initiative order during selection

## Observation and hardware evidence

The user reports that placing a card during selection briefly swaps the remote initiative portraits, then puts them back. Hidden `?` initiatives must not reorder the row; action phases retain the original order and animation.

Both supplied `LogOutput.log` files identify **ModBuild 486** at line17. The clearest causal interval is in `.planning/debug/remote/LogOutput.log`:

- Line8575 explicitly reports `SELECTION PHASE` with a covered round card.
- Line8577: `Remote initiative track order override: the track is mid-reorder` — the receiver stops applying the owner's arrangement.
- Line8585: `INITIATIVE REORDER SLIDE skipped` because the original re-sort left **every portrait in its own slot**.
- Line8599: the receiver again re-deals the rows into the peer's order.

The owner-order override hands off and resumes 29/30 times in the host log and 26/27 times in the remote log. These counts include phase transitions; they are not counts of visually confirmed swaps. The host also records the handoff/resume pair at17917/17943. No screenshot is needed to distinguish this ordering mechanism, and no rendered-pixel result is claimed from these logs.

## Source cause

`InitiativeTrack.UpdateActors` starts `AnimateInitiativeReorder` for a refresh even when the sort produces zero travel. During online selection, `InitiativeTrackActorBehaviour.CompareTo` uses client-local control membership, so the owner's and viewer's player arrangements differ. `RemoteInitiativeTrack.ApplyOrderOverride` formerly returned while the **viewer's** `track.isAnimating` was true, exposing that different arrangement for the entire window. It resumed the owner permutation afterward. This directly explains a remote swap and swap-back while the actual original portraits did not move.

The old permutation also took live tweened slot X coordinates each frame and accepted new record27 permutations throughout selection. The new selection policy must not read hidden initiative values or allow either transient input to change a settled selection picture. `ApplyNativeDepth` is not the horizontal writer: it excludes entry roots and writes descendant Z only.

`Refresh` additionally omitted the order override after synchronizing the clone. The normal board caller later calls `TickLive` in the same tick, so that omission is a source-ownership gap, not independently proven headset flicker. Both synchronization entry points now share the complete final owner-override sequence.

## Fix

`InitiativeSelectionOrder` latches the owner's first complete order and the original settled player slots for a selection window. Subsequent source sibling permutations, tween poses, owner snapshot reorders and temporary absent record27 data cannot alter that actor-to-slot assignment. The latch resets on leaving selection, a new round or a changed visible actor set. An initial or changed set is captured only when the actual original layout is settled; no intermediate tween pose is invented as a destination.

The receiver excludes inactive pooled player rows. A read-only `InitiativeTrackSurface.ReorderInProgress` seam includes the adopted slide, which can outlast the game's Chronos `isAnimating` flag. This readiness check only gates initial capture; it never suspends an existing selection latch. Outside selection, the receiver immediately stops assigning row X even if a stale record27 remains in flight, so original action order and native motion retain ownership. No game state, controller, callback, initiative value or wire grammar changes.

This is the user's explicit selection-stability policy, not a historical performance exception. The patch does not establish that all cross-client native action animation frames have identical source times; it preserves the existing original action-motion path.

## Validation

Strict Release build: **0 errors, 0 warnings**. New source-linked `InitiativeSelectionOrderVectors`: **26 assertions** covering zero/partial/complete native reorders, source and owner permutations, missing record27, action handoff with stale data, next-round capture, membership changes, atomic refusal and actual render call-site ordering.

Two actual mutations were restored after testing: handing off whenever the source is animating produces **6 failures**; re-capturing the order every frame also produces **6 failures**. The source guards strip comments and string literals before examining executable call order. Parent integration owns registration and full gates. These tests do not execute Unity layout or prove headset pixels; the next multiplayer pass should repeatedly place/revise cards during selection and then watch the action-phase reorder through completion.
