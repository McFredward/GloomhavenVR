# Stereo video calibration — Build 683

The entry/delivery contract below is historical. Build685 replaces argv activation
with the Advanced-menu action and ships both movies through normal install ZIPs;
see [the follow-up](STEREO-VIDEO-MENU-685.md). Shader/media evidence remains valid.

## Authorized first milestone

The maintainer assigned the stereoscopic in-game video work to this thread and
said to begin after the trailer-agent handoff on 2026-10-17. The accepted order is
eye-routing calibration, three short converted samples and headset review, then
the remaining films. This milestone implements the calibration stage. It does
not convert or replace an ordinary film.

The native PC inventory contains 19 film subjects / 354.8 seconds: one campaign
intro, eleven character videos and seven ambient backgrounds. The current
mod-local, notice-free intro remains the source for its later conversion.
The serialized native startup/promotion inventory is recorded separately below.

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
  Opening and manual closure use a 150ms CanvasGroup fade on the shared update
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

The closing-fade regression then removed the decoder's prepared texture/frame
after a valid picture was clicked. The old readiness gate trapped that picture
instead of finishing its close. Closing now advances on its own UI clock before
decoder readiness is consulted. The failing and repaired receipts are retained
under `decoder-close-regression/` and `decoder-close-final/`; the repaired whole
owner passes169 assertions/12 production processes/28 causal controls. The
large local run was already active; this bounded repair repeats its affected
lifecycle and final source checks while retaining unchanged-area evidence.

### Integrated validation

The complete local gate before the parallel Steam Link retirement passed
226/226 suites, all16 source suites and300,518 golden assertions. Its manifest,
source inputs, stdout and completion/cleanup receipts are retained under the
integration worktree evidence (relocated to the main checkout at completion):
`integration683/guard.log` and `integration683/worktree-debug/test-runs/`.
The guard returned1 only for the expected compiled changes; no test failed.
The private baseline is dev6455c3f21; its additional fan/version differences are
Build682 ancestry, not extra stereo behavior. Config667 and patches238 were
unchanged; the sole stereo token addition is STEREO VIDEO CALIBRATION.

The final renderer suite passes31 GPU/lifecycle assertions and10 causal controls
on GL llvmpipe. The worker also passes31 on Intel RPL-P Vulkan, inheriting GL's
controls. Binary inspection passes12 checks for the actual UnityFS7/2021.3.5f1
bundle, all three D3D11/GL/Vulkan shader banks, four UI variants per bank and
embedded license. This inspects the D3D11 bank; it does not execute a D3D11
headset. The chart validator measures1620 decoded eye pictures,35,640 header
cells and4860 marker centres with zero pixel error,16 real seeks and12 causal
controls. An independent regeneration is byte-identical with the same tools.
The final lifecycle owner passes169 assertions across12 production processes
and28 controls. Opening/closing and the decoder-independent close are included.

The subsequent dev merge includes the maintainer's separately authorized removal
of the synthetic Steam Link test and two obsolete suites. Current inventory is
224 local /99 CI /16 source suites. After that merge, strict Release compilation
passes with zero warnings/errors; source16/16, four affected local suites
(vr-options, vr-options-close, player-settings-help, stereo-video-calibration),
inventory19 unit tests and a fresh300,518 golden assertions pass. The compiled
comparison against the completed226-suite version changes only the seven
expected retired-test owners/project files, removes25 obsolete types/resources
and adds the unhooked ScenePayloadDraft. All four stereo types and the stereo
bundle remain byte-identical to the complete-gate artifacts. Later dev changes
through2d456d4a5 contain only documentation/offline research tooling, with no
production source or suite-manifest change. This is composed validation after
the bounded parallel retirement, **not a new complete224-suite pass**.

The merged surface is config666 /patches238 /log tokens4797: one exact approved
configuration retirement, three approved log retirements and our one addition.
The existing path-bound authorization ledger covers those removals. Exact logs,
exit codes, current surface and compiled delta are in `integration683/merged-*`.
Stopped project caches are removed through cleanup_completed_run; retained
reports, generated source, source pins, captures and result manifests remain.
The shared game references and other agents' active inputs are never swept.

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
