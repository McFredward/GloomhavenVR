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
the small stat cells. The current print mask disables artwork components,
including that layout provider. Preserving its layout contribution while hiding
its actual renderer is the narrow repair under test; broader cross-prefab
stat-box remapping is not assumed necessary. The broad action rows have a
separate geometry path and must remain unchanged.

Automated source/runtime proof does not establish the final headset image or
subjective yaw smoothness. Those outcomes require the next paired hardware run.
