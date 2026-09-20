# Short-rest follow-up after the 1.0.5 release

## Evidence

The fresh owner capture is ModBuild 534. The peer capture is historical build 500
and cannot verify the current remote presentation. No new image establishes the
remaining flash's precise pixels.

Owner `LogOutput.log`:

- 9552–9554: one original burn starts on an inactive face, executes its first step
  at raw 0.004, and rejects a duplicate LostMode request. Build 534's disabled
  playback path is working in this capture.
- 9573: at 0.036 seconds, raw progress is 0.008; original and renderer material
  both retain the spent grey/flow floor of 1.000 and dissolve 0.646.
- 9579: at 0.697 seconds, the same base material (-193262) instead contains native
  grey/flow 0.348 and dissolve 0.225. Its CanvasRenderer currently has no material.
- 9581: at 0.703 seconds, the renderer binds that same material with grey 0.356.
- 9589: the one original iterator reaches raw 1.000 and its terminal step at
  2.006 seconds. There is no second begin, permitted reset, original retirement,
  material replacement, or raw-progress rewind in the bounded trace.

This establishes loss of the retained spent appearance during ongoing native
playback. It does not establish another restart, nor the exact frame that retires
the floor.

## Source findings

Build 534's `BurnArtwork.PreserveSpentBurnStart` required `activeInHierarchy` when
ordinary presentation sampling classified a burn as running. Build 534 explicitly
permits the Choreographer-owned original iterator to run while its face is
inactive. For an already Lost card, an inactive sample consequently invokes
`SpentBurnContinuity.Apply` with both `spent` and `burning` false, clearing the
retained floor. Reactivation cannot reconstruct the original spent values from
mid-ramp paint. This is a source-proven contradictory lifetime condition.

A second audit concern is missing presentation binding metadata: the same method
removes the record when the caller's widget/card/owner is missing, even though the
original FullAbilityCard and tracked iterator can remain valid. Production
`ClearRecoveredSpentBurnStart` and `ReleaseSpentBurnStart` also lacked real runtime
coverage: the previous replay harness omitted these methods and substituted a
no-op cleanup stub.

The exact native hierarchy writer at the observed 0.697-second boundary remains
unverified. `CardsHandUI.AnimateCardsLost` refreshes cards/headers after its initial
highlight wait; `AbilityCardUI.UpdateCard` alone does not disable the full card.
Do not claim that method as the deactivation writer without stronger evidence.

## Implementation and focused verification

Ordinary spent presentation now uses the existing `BurnArtwork.Playing` predicate,
which observes the tracked native iterator, independently of face activation.
Explicit recovery, recycle and identity retirement are unchanged. The explicit
native-step path and terminal spent-floor handling are unchanged.

The replay harness now extracts production `ClearRecoveredSpentBurnStart`,
`ReleaseSpentBurnStart`, and `Playing` instead of substituting a no-op cleanup.
An inactive Lost sample between original steps reproduced the pre-fix failure in
an isolated harness: the next native step exposed its raw grey value instead of
retaining the spent floor. The new regression checks grey, flow and dissolve,
separate raw progress, the continuing single original, recovery, idle cleanup and
recycled-model cleanup. Negative controls restore the inactive gate or break each
cleanup branch and must fail on the associated assertion.

The missing-binding concern remains an audit lead, not an established hardware
cause; its production behavior is intentionally unchanged. No new diagnostic
streams or normal-log changes were added. The precise hardware hierarchy writer
and resulting headset appearance still require verification.

Focused validation: `bash scripts/burn-replay-tests.sh` passes with 683 runtime
assertions, seven source bindings, and 36 rejected negative controls (four new).
Integrated validation also passes: the complete source/runtime guard, 254,565
real-runtime wire assertions, flight timing 818, remote burn sequencing 108 and
burn layout 228. Strict Release has zero warnings/errors. Bilingual documentation,
Actionlint, patch inventory and whitespace checks pass. Guard exit 1 only denotes
the expected compiled difference from baseline 080c505e9: 99 changed, 78 added or
removed types and one order-only move. Config/patch/log surfaces remain
625 / 174 / 4,742; inventory remains 132 patch classes / 200 methods.

The correction is on dev 1.0.6 / ModBuild 535. The published 1.0.5 remains unchanged.
A new headset test must still establish whether this eliminates the reported flash.
