# Stereo video calibration — Build 683

## Authorized first milestone

The maintainer assigned the stereoscopic in-game video work to this thread and
said to begin after the trailer-agent handoff on 2026-10-17. The accepted order is
eye-routing calibration, three short converted samples and headset review, then
the remaining films. This milestone implements the calibration stage. It does
not convert or replace an ordinary film.

The native PC inventory contains 19 film subjects / 354.8 seconds: one campaign
intro, eleven character videos and seven ambient backgrounds. The current
mod-local, notice-free intro remains the source for its later conversion.
Embedded startup/promotion videos still require a complete asset census.

A subsequent read-only census of native scene/sharedasset files found eight
serialized VideoPlayers and two additional embedded VideoClips: `gameplay_trailer`
(1920x1080, 6501frames/30fps, 216.7s) and the Twin Sails logo (1920x1080,
240frames/30fps, 8s). The private source-hashed inventory is
`.planning/debug/stereo-video/serialized-video-inventory.json`. These are two
additional candidate film sources; their live activation and addressable content
still need classification before claiming complete in-game coverage.

## Implementation and ownership

- The exact `--ghvr-stereo-video-calibration` argv token opts in once. The normal
  launch does not inspect the calibration files or load the stereo shader.
- An independent APIOnly VideoPlayer is silent and loops the nine-second chart.
  It runs only in the actual offline MainMenu/MainMenu_gamepad scene with VR and
  world UI active. A click closes it; leaving that context or shutting down also
  releases its owned resources. No native movie or continuation is borrowed.
  Opening and manual closure use a150ms CanvasGroup fade on the shared update
  clock. Errors, context loss and shutdown still release immediately; cosmetic
  continuation never holds native flow.
- The fixed, yaw-only picture is 1.2 real metres wide, 1.6 metres ahead at spawn.
  It retains that pose. Full SBS has 1920x1080 per eye; the display aspect is 16:9.
- A small embedded Windows64 UI shader bundle is built with Unity 2021.3.5f1.
  Actual MultiPass eye callbacks select the packed half, preserve UI masks/alpha
  and prevent filtering across the eye seam. Paired callbacks restore the outer
  eye after nested camera renders. Mono cameras select the left half.
  Unsupported shader/render-mode preflight selects the independent mono chart.
- The existing native intro, heroes, ambient compositor, audio, skip/completion,
  gameplay, wire, configuration and defaults are unchanged. No inference or paid
  service is used. The calibration decoder is never a shared movie publisher.

Workers started at dev6455c3f21 with initialized, separate worktrees. Root's
integration includes the completed Build682 repair from dev01102dfd6. Calibration
tooling and renderer/fixture work have separate owned files. The private guard
baseline is local to the integration worktree; no shared baseline was written.

## Evidence and known limits

Media authorities, failed attempts, source hashes, pixel witnesses and cleanup
receipts are under the main checkout's ignored `.planning/debug/stereo-video/`.
Generated movies have 540 frames / 60fps / nine seconds, cuts at180/360, explicit
eye/orientation codes and known +12/0/-12 pixel disparities. They are test
geometry, not inferred film depth. Their physical presentation determines the
angular disparity; a pixel count alone is not a comfort certification.

Lifecycle tests execute the complete production owner/policy/localization with
narrow Unity/native API substitutes. GPU tests use actual Camera.Render, RawImage,
the embedded bundle, UI masks/filtering and readback; only the active-eye
observation is supplied because the fixture has no XR provider. Decoded chart
measurements, these substituted lifecycle tests and actual GPU shader results
must remain distinct from headset evidence.

The lifecycle test reproduced destroyed-object equality accepting late queued
Prepared/Failed callbacks after teardown. Live-object checks and exact reference
identity now reject those callbacks. Managed surface cleanup also runs after
external Unity root/player destruction; a completed attempt cannot restart.

A real nested-camera pixel test also reproduced a right-eye draw becoming the
left picture after an auxiliary mono camera rendered inside the head callback.
Paired pre/post callbacks now restore the enclosing eye. Root reviewed both
failed red and repaired green readbacks, and the per-eye aspect witness.

The initial integrated compile caught a missing interaction-namespace import
which the lifecycle fixture's substitute namespace had hidden. Both the import
and the fixture namespace were corrected; final production compilation remains
an independent requirement alongside substituted lifecycle tests.

Final integration checks and exact counts are recorded below after completion.

## Required headset review before conversion rollout

Install the Debug calibration update on the ordinary complete mod installation,
then add the explicit calibration launch argument. Record the Build683 banner,
actual XR mode and graphics API. Check each eye separately: LEFT must be in the
left eye, RIGHT in the right. Grid/text stay at the screen plane, cyan is nearer
and yellow farther, orientation is upright and each eye retains the 16:9 picture.
Observe both frame counters through the cuts and loop; inspect actual temporal
stereo stability and decoder performance. Click to close, enter another scene,
and restart without the calibration argument to confirm the ordinary flow.

This automated milestone does not establish headset fusion, actual decoded-eye
timing, D3D11 headset pictures, game-film quality or multiplayer stereo playback.
The later native/ambient integration also needs a versioned media/fallback and
peer-selection contract before any ordinary film is activated as stereo.
