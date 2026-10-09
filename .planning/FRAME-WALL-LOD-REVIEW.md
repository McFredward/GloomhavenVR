# Frame wall threshold and nearby pillar follow-up

The new hardware capture reports ModBuild 654 / deb989570 in both build banners.
The Frame directory contains logs only, without supplied screenshots. Read-only
input hashes, exact lines, policy events, all pacing windows and terrain candidate
counters are retained in `.planning/debug/frame-wall-lod/hardware-audit.json` and
`HARDWARE-AUDIT.md` in the main checkout.

## Evidence and bounded repair

Auto now fires three times, at 15, 28 and 10 FPS (LogOutput.log:4026, 6238, 8018).
This is hardware liveness evidence for Build 654, not an independent test of every
former veto. The capture contains 53 live threshold MARKs. Changes after a latch
leave walls hidden until the later mode change, matching the new reported defect.

The threshold binding, slider range and effective getter now cap at 30 FPS.
Minimum 5, default 15, stored key and integer stepping remain. Effective edits while
Auto is active restore masks and original consumer/wire paths through the existing
recovery, then restart the half-second grace and full two-second observation.
Manual Hide all and unchanged effective values retain their state. Visible EN/DE
help explains the reset. Detail: `docs/performance/FRAME-WALL-THRESHOLD-RESET.md`.

The configured terrain near and distant details are both 0% in this capture.
The head/hand protection endpoint therefore supplies exact geometry up close.
Its original world-AABB head distance changes around a column at a fixed radius.
The repair uses an enclosing cylinder for positively admitted pillar identities,
with authored mesh bounds and current source matrix. Head yaw does not enter the
calculation; source tilt, offsets and affine scale are included. Wall bodies keep
the existing distance rule; existing hand-touch protection and all percentages,
ranges, hysteresis, morph timing and presentation ownership remain.

The early one-room terrain windows have at most six candidates per application
frame and zero source-budget deferral. Budget exhaustion only appears in late
all-room windows. The source cap/frustum policy is retained; this repair does not
claim to eliminate every far-source native budget fallback. Protected nearby
pillars must use exact geometry regardless of admission versus native fallback.
No pillar-specific pose/mesh trace exists in the supplied logs, so the distance
cause is source-proven and compatible with the report, not uniquely assigned by
the hardware capture. Native scene-wide LOD census is not a pillar attribution.

The final post-Auto summaries are 67.79/68.69ms mean, 63.55/65.46ms median (about
14.6–14.8 application frames/s). Reveal/hitch/pose differences prevent assigning
a controlled FPS ratio. These changes fix requested controls and quality behavior;
they do not claim a new Steam Frame performance gain.

## Validation and handoff

Production checkpoints in the composed review worktree are `218473e38`,
`b5d5efc33` (threshold/reset and actual bilingual help), and `2627d3c8b` (radial
pillar source). `ed92597b4` and `978afbade` add focused proof/documentation.
They start from current dev `041c42f3d`, including the main agent's completed
caption changes. The final documentation checkpoint makes no runtime changes.

Primary combined affected validation passes all three scopes: wall-performance-runtime,
wall-options and terrain-budget-runtime (129.4 seconds, explicitly PARTIAL).
The final cases retain 730 real Unity wall assertions and all 27 controls,
77 original BepInEx/options assertions across 16 variants, and 818 terrain
assertions plus all 91 controls (92 terrain variants). All ten admitted original
pillar definitions are independently read and byte/topology verified; actual
Unity camera silhouettes at eight orbit directions are checked. The regression
controls fail the specified semantic assertions; old hidden/requeue contracts
remain. Original-game scene/collector/config/bank-delivery controllers and the
software graphics context are explicit model boundaries. These are not a
connected multiplayer or Steam Frame hardware test. Detail and added CPU cost:
`docs/performance/FRAME-PILLAR-RADIAL-LOD.md`.

Primary source checks pass 16/16. Strict Debug and Release report zero errors and
warnings. Rebuilt direct wire golden execution passes 299715 assertions; it is
not a new complete `scripts/wire-tests.sh` gate. Five bilingual player-document
pairs pass. Source surfaces remain 665 keys, 235 Harmony-text entries and 4794 log
tokens, without additions/removals. Exact private compiled comparison preserves
1247 types; only Loc, PerfConfig, WallSegmentFade and ScenarioTerrainBudget
change. No build number has been bumped in this isolated worker branch. Existing
asset banks, native MR, NPC and wire behavior outside those types are unchanged.
Unchanged contracts inherit the recorded 654 integration and caption evidence;
the main agent's subsequent 655 must supply its own multiplayer evidence.

The source/combined-suite/build/surface/compiled receipts are retained in the
main checkout under `.planning/debug/frame-wall-lod/`. Worker archives are
`worker-archives/auto.tar.gz` and `lod.tar.gz`, each byte-verified against its
SHA-256 file manifest. Failed/unqualified worker attempts remain archived too.
The worker trees and their build caches are removed after receipt verification;
shared baselines, references, other agents' trees and supplied hardware logs remain.

The parallel main agent retains integration ownership for Build 655. This reviewed
Frame change is handed over for integration after that work, normally Build 656.
The integrator must preserve 655's changes, add the next build note/version and
run the final affected checks for the actual merged source. This report records
an isolated, source-reviewed candidate rather than claiming it is already on dev.
Next hardware acceptance: edit the threshold while Auto is latched, then circle
a nearby pillar at a fixed radius with one and all rooms open.
