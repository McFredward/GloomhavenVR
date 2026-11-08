# Build764 independent scenario-start continuation review

Read-only review of the Results worker's `MapStoryLifecycle.ScenarioStart.cs`,
its lifecycle caller and `MapStoryOpeningLedger.EnrollCompletedScenarioStart`.
The reviewer changes no other worktree and adds no production behavior. Exact
reviewed files and native originals are preserved under
`.planning/debug/mp764-start-review` and the integration's review proof archive.
The integrator must check final source hashes or inspect any subsequent delta.
This is source/native-consumer review, not a new runtime/HMD pass.

## Finding and repair

The first candidate required a weak native DialogInfo subject to remain alive.
Original StoryController.ShowNext dequeues the completed message; its
m_CurrentMessage retains it only until a later room/round story. A collection
therefore could discard valid completed-intro proof before a later visitor joins.
Both integrator and independent reviewer identified this lifetime edge. The
worker's revised episode-scoped descriptor saves the original exact ledger token
at successful finish. An offline completion with no ledger token materializes
one recipient-free completed descriptor only after a matching live visitor
request. No old native callback or scenario graph is retained strongly.

The worker added forced-GC schedules for both offline and online completion.
This reviewer inspected their source but does not count their assertions as
independently executed proof. The affected complete worker container owns those
runs and compiling controls.

## Authority and lifetime audit

- The native CLevelTrigger ScenarioStart constructor sets event type2, subtype1,
  context1 and round1. The new classifier also rejects UI-event and dismiss
  triggers, so scenario end/type30 and room reveal/type29 do not enroll. The
  original successful native finish postfix, not a UI disappearance or failed
  callback, establishes Finished.
- Native ScenarioManager.Load allocates a new CScenario. The memo is
  scoped to that exact current object; no current scenario, a different episode,
  or MapStoryLifecycle.Reset retires it. Token values are local to the same
  ledger instance and the shared reset clears both ledger and descriptors.
- Only the online native host may enroll. Its local ID must equal the original
  native player's ID. The sender must be in the actual continuation roster and
  hold a receipt tied by weak identity to the current native player entity and
  VersionGuard session epoch. Reusing a numeric player ID alone is insufficient.
- MultiplayerCheats.NoteVersionReceipt forwards to NativeCheatQueue without a
  CheatsEnabled gate. The helper's name does not make normal progression depend
  on test cheats. Disabling cheats retains the continuation path; unmodded or
  mismatched visitors never obtain this mod completion capability.
- The exact successful start must match semantic saved-run identity, original
  content hash and page count. The ledger additionally requires valid wire
  grammar, reciprocal host membership, direction, current stored peer epoch,
  current token and unchanged predecessor. Retired epochs remain rejected.
- A later local or remote occurrence of the same semantic story prevents a
  delayed request granting an older completion. Repeated requests cannot add
  the same recipient again. Separate original intro messages retain separate
  descriptors and original FIFO/token binding rules.
- Generic ledger Update still refuses every completed history entry. The new
  method has one narrowly guarded caller; ordinary map/end/room story history
  gains no retroactive recipient path. Existing shared actor/pose authority and
  card concealment remain untouched.

## Native consumer and practical limits

The new code does not directly assign ActionProcessor state, force a ready
message, fabricate Results or click an unrelated confirmation. It publishes a
proven completion on existing record83. The visitor's existing ledger resolves
only its matching, actually open native message after its first native page is
available. UICharacterStoryBox.ShowLine reaches original OnFinishShow/ShowNext;
the game's own path releases its update/action locks and, when applicable,
sends GameLoadedAndClientReady and reapplies controllable states. This is shared
catch-up for an already completed exact intro occurrence, not general automatic
choice confirmation or a timer that dismisses a mandatory window.

No remaining source blocker was found in the revised token-based candidate.
Fresh runtime controls must still qualify wrong host/current entity/epoch,
Cheats-off, offline-finish-to-online-join, same-text successors, GC and replay;
the complete integration gate and hardware behavior remain the integrator's
responsibility. This source audit alone cannot establish Photon timing, native
scenario bootstrap or a correct headset picture.

## Final guard-order refinement

The final review re-pins the worker after its current-live-request prevalidation
and ordinary-traffic performance refinement. Semantic/content/pages, direction
and the stored current-epoch reciprocal opening are rejected cheaply before
native roster/player reflection. Every native authority gate still precedes
creating an offline marker or extending a completed entry's recipients.

The reviewer found one additional admission-order edge: a codec-valid
bidirectional request could create a recipient-free offline completion before
the ledger rejected its direction. This could not grant a recipient or execute
native gameplay, but still materialized unnecessary83 history. The revised
caller rejects bidirectional scenario-start requests before materialization;
the worker added a refusal requiring the ledger to remain empty.

Exact base-to-candidate member comparison verifies **15 unchanged original
members**, including generic ledger Open/Update/Finish/Sample/Observe/ForgetPeer/
Resolve/Bind and native lifecycle Participants/pose/Refresh/Sample/ResolveBox.
The existing83codec file also retains its original behavior. Current source pins
and guard-order comparison receipt are preserved separately in
`mp764-start-review-final`; the earlier reviewed snapshot remains intact.
No additional runtime invocation or Unity process was started by this reviewer.
The worker's final full runtime container, controls and actual counts remain its
proof obligation. No remaining source blocker was found in the final candidate.
