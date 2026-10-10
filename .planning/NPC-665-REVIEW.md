# NPC Build 665 hardware follow-up

Integration base: `c9462f78b` (`dev`, Build 664).

## Hardware evidence and accepted behavior

The 2026-10-10 paired host/client `LogOutput.log` banners both identify Build 664.
The new inputs are preserved, with SHA-256 and byte lengths, in the main checkout's
gitignored `.planning/debug/npc665/inputs/manifest.json`. The supplied
`zauberin_overlay_offset.jpg` was inspected before assigning a cause: small summon
enhancement rectangles are vertically displaced while the broad lower action
rectangle is correctly aligned. The screenshot alone does not identify the
native coordinate calculation responsible.

The maintainer accepts the time until the remote card and UI become visible, and
confirms that the physical backing and printed face now move together. These are
hardware-confirmed outcomes of the preceding changes. This follow-up preserves
the loading/admission policy and does not reopen the latency work. The previously
accepted stable enhancement overlay and merchant return artwork remain contracts.

## Requested scope

1. Smooth the offered card's owner-relative yaw at the enchantress and inspect
   the corresponding merchant path. Preserve authored facing, card/overlay
   geometry and the approved native hover waveform.
2. Repair the small summon enhancement areas locally and remotely, without
   moving already correct full-width action rectangles.
3. Have the remotely displayed enchantress card itself begin its return flight;
   prevent a flying copy alongside the still parked offered card. Preserve the
   accepted merchant return behavior.

Workers start from the current integration commit in separate initialized
worktrees with disjoint production ownership. Shared metadata, final integration
and publication belong to the primary agent. The three production lanes have
been reviewed and composed; validation receipts below distinguish the initial
complete attempt from its focused repairs.

## Causal review

The Build 664 source publishes the actual enchantress face/backing in the private
service-3 lane while separately preparing the same source transforms in the
independent service-1 Stock lane. Each lane has its own session and module IDs.
The native return cohort activates the Stock modules; retiring the private
offering follows a separate census. This is an ownership transition between two
observer objects, not a flight-clock or card-axis defect. A transfer must retain
the already displayed object and identify its exact source lifetime; a template
address or card ID is insufficient when a card is offered again.

A production sampling/render probe reproduces uneven yaw on unchanged Build 664
without packet loss. A native UI header arrives between physical root samples;
the root's sample then appears older than `LastFrame.SampleTime` and loses its
own interpolation. The enchantress has a dedicated root clock, while the merchant
also needs a scoped offered-root clock rather than a general UI-host tween.
The first corrected probe preserves the header-free frame increments in both
paths. This is numerical runtime evidence, not a headset smoothness verdict.

The imported native summon hierarchy places its name TMP layout element above
the small stat cells. The old print mask disables that layout provider. The
unchanged native layout reproduces an upward shift of about 22.35 card units in
the editor-font fixture; this is a measurement, not a hardcoded correction.
Keeping the actual `SummonContainer.SummonNameText` enabled while suppressing
its renderer restores the native cell positions. The broad action rows have
a separate geometry path and remain unchanged. Initially inactive native
enhancement-area images are also excluded from the print mask by their actual
component ancestry, preserving them when the branch becomes active.

The return origin is an immutable, optional 20-byte TLV116 payload naming the
private service/session/module/structure/claim and native preparation revision.
The transport supplies the owner. Both lanes advertise the exact same native
source before its return; a complete due113 cohort can then rekey the existing
observer module rather than display a second Stock card. Address or card-model
matching never grants transfer authority. Cumulative deltas and pooled native
first pictures must retain exactly one origin record. Existing113/115 records
and frames without116 retain their original byte format.

## Reviewed worker proof

- [NPC-665-YAW.md](NPC-665-YAW.md): 194,636 assertions, eight owner/header/delivery
  cases, two causal expected failures, 32 GPU images and 16 silhouette comparisons
  with zero changed pixels. The anisotropic unsent-native-layout comparison remains
  a separately recorded limit; tolerances were not increased to hide it.
- [NPC-665-OVERLAY.md](NPC-665-OVERLAY.md): 818 assertions, five causal expected
  failures, four stat cells, two physical scales, native inactive/disabled/repool
  cases and unchanged broad areas. All 12 isolated hidden-label readbacks contain
  zero ink; visible physical-label positive controls pass.
- [NPC-665-RETURN.md](NPC-665-RETURN.md): the integrated positive reaches 44,639
  assertions and nine compiling causal controls. Six 108-render transitions
  retain the identical observer Host/Binding, original sprite/texture/material,
  and one visible body/front. Tests include cold originals after native flight
  completion, source closure, dropped first events, late repairs, disconnect,
  reoffers through preparation epoch 132, and a delayed old113 arriving before
  the new preparation's Stock metadata. The actual next native flight then
  adopts that same newly offered object. A composed yaw check rejects old
  preparation roots while accepting the actual current published root.

The apparent TMP renderer-reset diagnosis was disproved by correctly isolating
the complete canvas. The first camera also included legitimately visible artwork
from another remote canvas. The smaller renderer-only repair passes; the temporary
additional Graphic-alpha change was removed. Failed probes and the escaped
diagnostic control are retained and explicitly described, not counted as passing
negative controls.

The primary agent independently re-read and verified all 1,564 yaw archive hashes
and 319 overlay archive payload hashes. Compact worker receipts live under the
main checkout's `.planning/debug/npc665/yaw/ac3395e09` and
`.planning/debug/npc665/overlay/1b4bac8bc`; worker originals remain intact.
The primary agent also verified the SHA-256 of both compact return archives in
`.planning/debug/npc665/return-worker`: the complete proof and the superseded
preparation addendum. Engine/game-model initialization boundaries remain explicit
in the worker documents; there is no claim of running the complete game in CI.

## Integration validation and preserved behavior

Nine preservation anchors match the integration base byte-for-byte: both motion
codecs, native return composition, native depth ordering, merchant transaction
and handoff, enhancement-template preparation, missing-original requests and
native visibility. The receipt is
`.planning/debug/npc665-integration/preserved-source.json`. These source checks
support preservation; they do not independently prove a headset picture.

Strict Debug and Release builds pass with zero errors and warnings. The 16 source
suites pass. Direct complete golden vectors pass 300,466 assertions, including
47 new exact-byte/malformed/lifetime/pool return-origin checks. The complete
`wire-tests.sh` invocation reached its local-scope stage but stopped on that
stage's failures; the direct golden run is recorded separately, not described as
a successful fresh umbrella invocation.

The initial complete local attempt records all 209 scopes: 196 pass and 13 fail.
Its immutable report is
`.planning/debug/test-runs/20261010-122251-b5e061b7/results.json`.
Failures are retained rather than counted as counterproofs:

- Current Frame/codec additions were missing from several standalone test source
  lists. The repairs bind the real production origin type or full codec, and
  record declaration/source hashes; no replacement metadata implementation is
  supplied.
- Three historical source controls predate the added fields/detach entry point.
  Historical latency gets only inert DTO fields. Historical paint and geometry
  retain their old implementations and explicitly throw if the excluded116
  detach route is reached. Their expected old rendering failures must still be
  reached normally. A compile failure cannot satisfy a negative control.
- Two strict source mutants still matched the old single-line motion predicate.
  Their binding now accepts the exact new predicate while preserving the same
  removed guard and all runtime assertions.
- The new summon fixture omitted the post-wait presentation tick used by the
  game and the shared mirror fixture. It retains its existing 0.4-second wait and
  strict local/remote geometry checks, adds one actual post-wait TickRemote, and
  records packet/census/election/clock/pass-count evidence without advancing the
  lease through a diagnostic query. In the serial receipt the holder is already
  admitted before that final tick (first case: 1,171 ticks during the wait).
  The original concurrent failure's scheduling explanation remains a plausible
  source-backed hypothesis, not a proven cause of that failed run.
- The generic mirror had an initial native Unity SIG11 with no assertion verdict;
  its identical serial rerun passes. No production or relevant fixture change
  was used for that rerun, and its crash cause remains unproven.
- The retained first-picture proof exceeded its unchanged one-second desktop
  wall-clock limit (1.127577 seconds) in the concurrent attempt. Its serial rerun
  passes at 0.8853213 seconds with the same limit and full workload; this is not
  evidence of a new hardware latency result. The actual old-source control still
  reaches the named deadline failure.

All 13 focused serial scopes pass, including their required compiling causal
controls. The composed coverage verifier checks the unchanged manifest, all
report/log hashes and exact IDs: **209/209 = 196 inherited passes + 13 serial
repairs**. This is composed coverage, not a fresh green complete umbrella run.
The receipt is `.planning/debug/npc665-integration/composed-coverage.json`;
the focused report is `focused-repairs/results.json` beside it. The standalone
production town codec/delta project additionally passes 50,277 assertions.

The integrated summon proof passes 818 assertions and all five controls. Its
72 native stat target rows cover all four cells, two scales, three starting states
and native refreshes; maximum Y error is 0.0001848 against the unchanged 0.001
tolerance. All 12 pooled-label PNGs have zero ink; four enabled physical-label
controls have 1,758 ink pixels and both disabled controls have zero. The
renderer/layout repair source is identical to the reviewed worker source.

Bundle validation passes: UnityFS7/Unity2021.3.5f1, with 1,594 figure derivatives
in 66 parts. The source surface remains 666 config keys, 235 registered patches
and 4,795 log tokens, with no additions or removals. The private compiled baseline
is `c9462f78b`; reviewed differences consist of the origin DTO, codec/frame/delta,
mirror yaw/return ownership, mask layout repair and protocol/build metadata.
Other compiled consumers contain only the expected inlined Build664-to665
constant changes. The BuildInfo snapshot contains the documented volatile
`c86e625` stamp from before the harness-only commit during decompilation.
An independent worker reviewed the actual decompiled codec/frame/delta/mirror
and found no unexpected functional change. No source preservation anchor changed.

Automated source/runtime proof does not establish the final headset image or
subjective yaw smoothness. Those outcomes require the next paired hardware run.
