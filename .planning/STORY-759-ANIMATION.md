# Build759: finite recovery flight for a lost progression story

Scope B, based on `ea6b5ac1c`: the previously approved three-second, measured-playfield
story rescue now flies instead of teleporting. No ordinary-window recall, native
confirmation, progression choice, game-state write or new wire field is introduced.

## Change and ownership

- One `StoryRecoveryFlight` belongs to its existing `WindowPanel`. It captures the
  current **drawn** grab-frame pose and one host-facing destination when the original
  three-second dwell admits recovery. The destination formula and shared reference
  scale are unchanged. The headset is never followed afterwards.
- Over 0.62 seconds the same frame, visible picture and hit geometry receive eased
  position/rotation samples with a small vertical arc, through the original
  `SnapFrameTo` API and the existing `PanelPoseWatch.Writer.StoryRecovery` permission.
- Active flights sample **before** the 250 ms admission poll. An active flight does
  not rewalk the native playfield; ordinary lost-story footprint work remains at
  its original maximum four polls per second and only for eligible stories.
- Native close/hide, replaced source/host/grab, local grip, accepted remote grip,
  new opening/claim, placement-author loss, transport/session handover and current
  opening-time room-making cancel the flight at its last visible sample. There is
  no pose restoration, user-grab release, native cancellation or auto-confirmation.
- Online register claims are checked only for a flight admitted with an active
  ordered transport. Offline and native/unmodded transport fallback retain the
  original one-author recovery. Entering or leaving ordered transport interrupts
  an existing flight rather than carrying an old permission into the new mode.
- The existing automatic claim and ordinary story movement sampler publish the
  changing pose; existing shared pose tracking interpolates it for observers.
  The move register deliberately retains automatic claims until their normal
  lifecycle supersedes them. Calling `Released` would risk releasing a newer
  manual grip and is therefore absent.
- No detach change is required: state owns no Unity resources and is retained only
  by the existing window entry, disappearing with that entry's native lifecycle.

Production files: `ModalFallback.StoryRecovery.cs`, one field in
`ModalFallback.3.WindowPanel.cs`, and `StoryRecoveryFlight.cs`. Dwell timing,
shared codecs/consumer implementation, configuration and general modal Tick are
unchanged.

## Focused verification

Final `multiplayer-progression-runtime`: **93 actual Unity assertions and all 16
compiled causal controls pass** (30.3 s, Unity2021.3.5f1/llvmpipe). The fixture binds
complete shipping recovery/dwell/flight logic, the exact original `SnapFrameTo`
and `ReadRoomMakingStart` methods, and complete original `SharedWindowMoveRegister`
and `SharedWindowPoseTrack`. Native UI existence, the final grab presentation Tick
and transport dispatch are explicit boundary ports.

Cases cover pre-dwell refusal, one author, refused initial remote claim, multiple
intermediate frames before a poll, monotonic finite motion, real mesh pixels and
Unity collider bounds at a mid-flight sample, no native close/confirm, exact fixed
endpoint after host motion, no recurring footprint scan, existing remote track
interpolation and nine live interruption cases. Offline, online without transport,
and changes of online/ordered-transport state are explicitly covered. An accepted
remote manual claim remains Held after cancellation.

The added compiled controls remove flight duration, render-cadence sampling,
local/remote grip gates, native close, exact panel identity, reflow priority or
transport-transition ownership. Existing eight progression controls remain.
Final pictures are geometry witnesses, not screenshots of native story text or a
headset. Actual Photon transport/headset timing and native material pixels are
not certified by this fixture; their existing implementations are unchanged.

Strict **entire production plugin Compile target**, Release: **0 warnings,
0 errors**, 13.6 s compiler / 13.8 s invocation. Original references/resources and
analyzers are used; post-compile movie/content copying is excluded. The integrator
owns the final solution build and source-gate union; no complete-gate claim is made
for this worker and no release archive is built.

## Failures, repairs and retained evidence

- First focused run: a synchronously created/moved fixture collider had not passed
  Unity's physics synchronization boundary; add `Physics.SyncTransforms` before
  measuring it. The observer assertion also needed to reject flight admission,
  rather than waiting for the first rendered movement. Both repairs affect only
  the fixture; the complete containing suite then passes.
- Strict production compile found the new helper missing the actual native
  `UnityEngine.UI` import. Fix that real import and give the fixture's native
  UIWindow port its correct namespace, so the fixture can no longer conceal this
  mismatch. Production-only confirmation and the complete final suite pass after
  the repair. This failure is retained rather than reclassified as success.
- Earlier positive runs remain separately labelled; the final namespace-bound
  full suite and final strict compile are the delivery evidence.

Evidence is under ignored `.planning/debug/story759-animation/` and
`.planning/debug/multiplayer-progression-runtime/`, including full source hashes,
mutation inputs, results, failure logs, mid-flight picture and cleanup receipts.
All owned processes are reaped and canonical `cleanup_completed_run` removes
regenerable project caches after successful and failed invocations. Existing
qualified Build758 gate results are inherited only for unchanged inputs; final
integration validates the union with the parallel workers.
