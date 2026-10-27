# Multiplayer scenario launch and initiative ownership

User request, 2026-10-27: inspect all three open GitHub issues and their media,
leave level-up issue 23 with the parallel menu agent, reproduce and repair issues
18 and 24. Worker base: `0a87ac8a1` (Build 729).

## Evidence boundary

- [Issue 18](https://github.com/McFredward/GloomhavenVR/issues/18): four VR
  players cannot start a newly selected scenario, especially after a city event.
  Some clients retain a city-event prompt. Returning to the main menu and
  rejoining restores the flow. The latest comment links a 69-second video.
- The entire attached video was inspected using 14 chronological frames. Around
  10 seconds, the Inox Encampment panel explicitly says that Smartypants still
  needs a mercenary assignment. Around 35 seconds, that notice is absent while
  the start button is also absent. The video does not show the preceding event
  completion or reveal the running mod build. A missing native participation
  condition must remain a genuine refusal, independently of a presentation fix.
- Full audio review adds context: the narrator says all four players already have
  characters. Another player toggles assignment as a recovery attempt and reports
  that the city-event offer remains after Cancel. The temporary notice is not
  proof of a user error. Audio was transcribed locally with the already cached
  Whisper model, without an API upload; eight additional full-resolution frames
  at 28–49 seconds were inspected.
- [Issue 24](https://github.com/McFredward/GloomhavenVR/issues/24): the initiative
  track becomes distant or slightly behind the board; in a second occurrence,
  actor selection no longer works. The attached screenshot shows a normally
  sized board and a tiny initiative row above it. A single image cannot establish
  which native instance, parent or fit state caused it.
- Current main hardware logs are Build 729 / assembly 1.2.2 / `0a87ac8a1`;
  remote logs are Build 717 / assembly 1.2.1 / `ab78b3ba2`. These are different
  runs and are not a paired reproduction of either issue.
- Original issue JSON, PNG, MP4, extracted frames and bounded observations are
  preserved under the main checkout's ignored `.planning/debug/issues18-24/`.

## Review constraints

Preserve the game's actor ownership, party assignment, readiness, linked-quest
and callback authority. Do not auto-confirm a decision, clear native players'
votes, mutate replicated quest state, or close mandatory native UI as a recovery.
Unmodded peers must continue receiving only original game actions. Retain shared
window geometry, user board movement, child animation and input ownership.

The implementation and test results below will distinguish a production-code
reproduction from a headset observation. An automated pass does not close the
hardware issue by itself.

## Issue 18: one owner for the reused readiness window

`UIReadyToggle` requires a `UIWindow` on the same GameObject and reuses that
window for city events, quests, rewards and other readiness phases. A city-event
offer can already be converted as a standalone modal when the same original
becomes the quest start control. `MapTravelConfirm.Park` previously recorded its
home as that old mod-created host and moved it into the quest. The old modal
could then release itself, restoring the original away from the quest. The
next production reconciliation interpreted that second mod writer as a native
ownership change and latched `_parkStandDown` until the map room was rebuilt.

Retire the exact old conversion with the existing `ReleaseForComposite` kernel
before recording home or adopting the control. This also drains an exact-target
pending dissolve release; a real user close defers adoption and retries normally.
Decline the map's sticky visibility extension for this exact ready component,
so native `Hide` ends the offer instead of leaving the old inert label visible.
Ordinary destination windows retain their existing sticky/nonblocking policy.
Native explicit Cancel reaches `CancelPreviewCityEvent`, then
`ToggleReadyUpUI(false, CityEvents)`, `UIReadyToggle.ToggleVisibility(false)`,
`UpdateVisiblity` and `window.Hide`. The same lifetime repair therefore also
covers the cancellation described in the audio.

No new vote, callback, assignment, gameplay or network authority is introduced.
In particular, the video's genuine missing-mercenary notice remains enforced.
The native `ResetUI` cleanup ordering has a separate apparent weakness, but the
present repair does not modify it: that hypothesis alone does not establish the
reported visible deadlock and would widen this bounded presentation change.

The new fixture executes production `Reconcile`, `ReconcileCore`, `Park`,
`Unpark` and the real composite release kernel with a modeled native uGUI
boundary. It schedules the old modal release on both sides of adoption and
checks subsequent frames, restoration, refusal, repeated offers and offline
travel. The previous ready-dispatcher tests did not exercise this competing
conversion owner, so their passing evidence could not exclude this defect.

## Issue 24: bind geometry and input to the same original

The general `WorldSurface` intentionally keeps a live conversion until its own
surface chooses to replace it. The initiative surface did not make that choice
when the native `InitiativeTrack.Instance` changed. It kept the previous live
root docked while fitting, depth treatment and portrait input used the new
singleton. This is a reproducible source defect consistent with the report;
the attachment alone does not prove that the reporter experienced this exact
instance replacement.

Capture the exact native track per conversion. Release the old original before
adopting its replacement in both Update and LateUpdate, and reject a portrait
pick while its host and current native source disagree. Route all release
paths through one cleanup which restores the old original's mip/depth/effects
and unregisters its picker. Do not reset the board's position or scale, flatten
the row, or introduce a generic surface-wide retargeting policy.

The real Unity coordinator test also reproduces an independent click failure:
Unity's `Vector3.normalized` returns zero for a vector shorter than `1e-5`.
The previous portrait-plane normal was the cross product of world-space edge
vectors and therefore shrank with **area**. At small board zoom, otherwise valid
millimetre-sized portraits produced a zero normal and rejected every ray.
Normalize the two nondegenerate edges before taking the cross product; retain
the original facing, distance, boundary and native actor-selection gates.

The new test runs the original `WorldSurface`, complete tray-mount coordinator
and initiative lifecycle/picker methods in Unity 2021.3.5f1. Native conversion
and effects are explicit modeled boundaries; hierarchy, world corners and ray
math are actual Unity behavior. It is local-only because hosted CI has no Unity
editor. Existing helper/order tests did not exercise this complete lifetime or
Unity's small-vector normalization behavior.

## Integrated validation and delivery

Build 731 reserves the number after the concurrent menu agent's pending 730.
That independent lane's unpublished menu changes are not included here, and its
main-checkout working files/commits are not rewritten or published by this task.
Issue 23 remains with that lane. Only `origin/dev` is a delivery target.

- The map subset passes 8/8 suites: modal-close, map-flow, map-story-lifecycle,
  map-quest-selection-runtime, map-quest-ready-runtime, map-ready-handoff,
  quest-window and city-event-button. The initiative integration changes no map
  files afterwards; these passing receipts are reused rather than repeated.
- The board subset passes 6/6: initiative-binding-runtime, board-refresh,
  ui-inventory-runtime, round-card, pick-tray and wrist-board-runtime.
- New fixtures pass 52 modeled-boundary coordinator assertions with five
  causal controls, and 146 real-Unity assertions with seven causal controls.
  The latter includes a numeric old/new witness with identical world geometry
  but a repaired small-zoom ray result.
- All 16 source gates, all 306,431 unfiltered golden vectors, strict real Release
  and CI-style metadata Release (both zero warnings/errors), and docs-i18n pass.
- The compiled comparison against private `0a87ac8a1` retains 1,323 types. Only
  ModalFallback, MapTravelConfirm and InitiativeTrackSurface change behavior;
  nine other types carry only the build-number substitution. No new/removed
  type or resource, native action authority or wire-layout change is present.
- Configuration keys remain 670, patch registrations 248; the only new log
  marker is the bounded normal-level `INITIATIVE TARGET REBIND` lifecycle report.

This is a 14-suite focused integration, **not** a new complete 252-suite run.
Unchanged rendering, asset, NPC and other gameplay evidence is inherited from
Build 729. No issue-specific paired four-headset outcome is claimed; the GitHub
reports remain available for hardware confirmation rather than being closed
automatically. Exact source pins, passing/failed results, original media,
compiler logs, numeric captures and cleanup receipts remain under the main
checkout's ignored `debug/issues18-24`. Generated caches and all three owned
worktrees are retired after their processes stop and evidence is secured.
