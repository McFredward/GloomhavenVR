# Short-rest burn continuity review — build 528

## Build-529 logging correction

The maintainer requires sparse ordinary player logs. The build-528 Info-tier continuity
records below are historical: build 529 moves all three `BURN PRESENTATION` records to
Debug and skips their material/progress observer entirely unless `VRLog.WantsDebug`.
Re-enabling diagnostics starts a fresh observation. The four-anomaly-per-episode bound
remains. Table and pool self-reports already use Debug-gated `VRLog.Info`/`Warn`.
For the next targeted continuity capture, explicitly select `[General] LogLevel = Debug`;
normal logs no longer claim to provide that detailed evidence. Gameplay/effects are unchanged.

## Evidence and limits

The current local hardware log identifies release 1.0.4 / ModBuild 527 and an offline
run. The user confirms the flash occurs on the local board. Remote logs remain build
500 and cannot validate current multiplayer presentation.

`LogOutput.log:1203` records the short-rest Trample burn waiting 2.01 seconds;
`:2826` records Swift Bow waiting 2.00 seconds. Both finish with `_GreyOut = 1`.
These timings do **not** establish a restarted two-second coroutine. A shader/material
reset can flash while the same iterator keeps running. There is no per-frame material
or burn-start evidence at the supplied Info log level, and no burn screenshot/video.
The observed headset defect is therefore not claimed conclusively resolved by logs.

## Source findings and changes

- `CardHalfTone.IsModOwnedCopy` previously inferred clone ownership from absence of an
  adoption record and an `AbilityCardUI` ancestor. Native card dialogs reparent original
  full faces outside that hierarchy, and yield/release removes adoption membership.
  `NormalizeCardFx` can then replace native private materials with zeroed rest copies.
  A running native burn only initializes its constant burn/tint/noise values at the first
  iterator step, so a mid-timeline material replacement loses those values. Classification
  now also rejects live native `CardEffects` (serialized field or component) and the
  original-owner registry. This protects originals at transient ownership boundaries;
  stripped remote clones still normalize initially and retain owner-driven output.
- Native short-rest hover calls `ToggleEffect(false, BurnCard)`, meaning a fully burnt
  no-ramp preview; confirmation restores that preview and starts the real burn. The
  existing `DialogPopup_Show_HoverStrip` already removes the normal hover listeners, so
  this is **not a confirmed active cause in build 527**. A narrow effect-level guard
  protects the same native pending discarded `ShortRestedCard` if a hover callback escapes
  that earlier stripping. It retains the spent offer and leaves confirmation, redraw,
  cancellation, unrelated previews and later recovered-card burns native.
- Info-tier `BURN PRESENTATION START`, `END` and bounded `DISCONTINUITY` readings distinguish
  native raw progress from the retained spent display floor and identify header-material
  replacement independently of a progress rewind. Start/end are event-driven; anomalies
  log at most four times per observed episode. This fills the current evidence gap.

No gameplay callback, card model, burn duration, native yield, flight completion, face
visibility, wire record or bundle changes. Remote presentation continues to use owner
material output. The original build-517 guards still preserve the first running burn
across repeated effect aliases and defer layout until its completion.

## Validation

- `scripts/burn-material-ownership-tests.sh`: 543 assertions execute production ownership,
  `Observe` and normalization methods; 2 production write-route bindings and 5 negative
  controls reject original-material replacement and overwritten remote burn output.
- `scripts/burn-replay-tests.sh`: 534 assertions, 3 bindings, 14 negative controls. Added
  repeated pending hover, native confirmation, post-confirmation exit, redraw, ordinary
  discarded refresh, cancellation, unrelated preview and recovery followed by another rest.
- `scripts/remote-burn-sequencing-tests.sh`: 108 assertions, 21 negative controls; unchanged
  owner completion, absent sample retention, historical discovery and remote release logic.
- Strict Release build: zero warnings and errors. Integration gates run on the root tree.

Next hardware evidence: confirm a short rest after hovering its button, then move off the
button during burning; repeat with redraw and a current remote peer. The card should keep
its spent base and one continuous burn before its pile flight. If a flash remains, retain
the new `BURN PRESENTATION` lines and a short capture: clean progress/material readings would
point beyond the native material/iterator layer rather than justify another guessed delay.
