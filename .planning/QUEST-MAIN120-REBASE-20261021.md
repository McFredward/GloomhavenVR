# Quest release-main 1.2.0 integration

Date: 2026-10-21. Requested base: released `main` 1.2.0, not post-release dev
1.2.1. No new hardware capture accompanies this integration.

## Source and history

- Requested main: `4f10ddd60524911bf4cf39777df46f95d7691d83`, release 1.2.0/B715.
- Previous published Quest: `f4539f297a948476b593f6fd0ebc561f04e22a8e`, B713.
- Previous shared base: `09bbef45e926d984754caeb02b83642eb878015e`.
- New Quest: semantic version **1.2.0**, distributed handshake **B716**.
- Current dev at integration time: `fd45d606f` (1.2.1); it is unchanged.

As in the established Quest integration workflow, consolidate the exact net Quest
adaptations from the shared base, then actually rebase that delta onto release
main. Preserve the old published feature ancestry through an ancestry-only `ours`
merge after review. The final feature update is a normal fast-forward push; no
force-push, discarded published history, or Quest integration into dev/main.
`backup/quest-b713-pre-main120-20261021` retains the old feature tip.

## Integration findings and repairs

Release main introduces persistent menu-rig lifetime and a new camera resolver.
Keeping Quest's old partial definition produced a duplicate member. There is now
one combined production resolver: retain release liveness and render-budget-owned
suspended camera enumeration, plus Quest's exact owned capture-target admission
and last-choice native UI camera fallback. The release lifetime/rebind policy is
unchanged. All new Continue permission, live figure pickup/pointer arbitration,
MR backing-depth and movie Cheats permission contracts survive. The independent
review and executable boundaries are in `QUEST120-SHARED-PARITY-AUDIT.md`.

The public source selector no longer requires three Windows debug helpers retired
by release main. All remaining required resources keep their original roles.
Ordinary extraction over B713 leaves fifteen removed files behind, including the
obsolete menu-camera partial. After verifying the entire current inventory and
all extras, the loader removes only those exact historical path/size/SHA records.
Changed, unknown or symlink inputs remain protected. The final deletion rechecks
its witness; a prequalified file already removed by another loader is complete.
Ten focused retirement tests include that concurrent-loader case. Historical
evidence and producer details are in `QUEST120-UPDATE-REBASE-AUDIT.md`.

## Minimal update and delivery

The actual production update planner classifies B713 to B716 as `compact-code`,
with no full rebuild reasons. The native code and metadata must be compiled as
a matched pair. Original Campaign conversion, native procedural helpers,
environment art, figure banks, corrected audio, stereo movie banks and depth
recipes remain unchanged. This integration does not run an original import,
shader compilation, depth estimation or full APK build. APK publication/signing
still writes the updated APK; this is not a managed DLL-only replacement.

Ship the game-free Windows/Linux Builder ZIP. Qualify its actual manifest,
isolated launcher/API, and a real archive overlay with the fifteen exact B713
inputs. The delivered source commit and archive digest belong in the adjacent
audit/validation JSON, without recursive self-pinning in this tracked document.

## Validation scope

Fresh focused integration checks:

- Strict Debug and Release builds: zero warnings/errors; Weaver and wire builds.
- Direct real wire/golden-vector runtime: **300,601 assertions**. This is not a
  claim that the `wire-tests.sh` umbrella or whole local gate was rerun.
- Actual current-mod AOT compatibility: **814 assertions**, 218 live registration
  callsites/classes, 196 runtime activations and two static movie substitutions.
- Actual original-API weave: **488 hooks / 301 targets**, zero issues and complete
  metadata emission against read-only original assemblies.
- Source group **16/16**, suite inventory tests19 (two platform skips), and five
  documentation language pairs. Inventory:257 local /120 CI /16 source suites.
- Selected production runtime suites: reward showcase, announcement Continue,
  MR backing depth, near figure grab and graphics profiles, all passing.
- Actual Unity menu lifecycle: **1,314 assertions /126 Camera.Render callbacks /
  twelve anchor replacements**, plus three compiling causal controls. Actual
  combined managed resolver:26 assertions and eight compiling controls.
- Seven focused host groups108 tests; nine initial retirement tests plus one
  concurrent-loader case. The thirteen existing release contracts passed with
  the repaired verifier. Repeated cases are not additional unique coverage.

The released main implementation inherits its trusted hosted complete CI and
release evidence from `RELEASE-1.2.0.md`. These selected integration checks are
not a new complete 257-suite local gameplay gate. Retain prior evidence for
unchanged loading/ping/asset/viewport pipelines, with their original boundaries.

Canonical ignored evidence:
`/home/claw/quest3-local/build/evidence/quest120-rebase-20261021/`.
Every test process is reaped and uses the common completion cleanup. Preserve
source pins, results, logs, manifests, symbols and captures; remove only owned
regenerable caches and finished worktrees after publication.

No automated result establishes final Quest eye pixels, the complete initial
loading handoff interval, or the separate Adreno ping-tile artifact. Those remain
hardware questions for the next B716 compact update test. This rebase does not
claim to fix them. Pillar LOD/settings/reduced meshes remain removed on both shared
release and Quest; independent figure LOD and exact environment originals remain.
