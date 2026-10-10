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
and publication belong to the primary agent. Detailed cause, proof and validation
will be recorded after reviewing the worker changes.

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
The composed return review, complete integration gate and publication are pending.

Automated source/runtime proof does not establish the final headset image or
subjective yaw smoothness. Those outcomes require the next paired hardware run.
