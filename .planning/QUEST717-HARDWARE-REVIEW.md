# Quest B717 hardware follow-up — 2026-10-21

## Evidence and decision

The maintainer reports early initial spinner loss, ping and attack-shield
artifacts, and poor pacing after opening the second room. The matching supplied
capture is `quest-capture-20261021T164143Z-7d3ed94e.zip`; its manifest and current
startup banner both identify B716 / assembly1.2.0 / input `d4e76da00ec2`.
The previous startup log is B712, not another current run. The supplied
308.36second video `dev.gloomhavenvr.quest-20261021-182718-0.mp4` starts inside
the scenario: pings are near the beginning and the pale shield rectangle near
302seconds. It does not record the initial spinner interval.

Original inputs, SHA receipts, watched frame sheets, actual source snapshots,
failed exploratory controls and final focused evidence are retained in ignored
`/home/claw/quest3-local/build/evidence/quest717-review-20261021/`.
This is a compact hardware candidate, not a declaration of device acceptance.

## Initial spinner: failed assumptions and next experiment

[The complete chronology](QUEST717-LOADING-REVIEW.md) distinguishes the repaired
source defects from the persistent headset symptom. In brief:

1. B680/B681 corrected missing vertex COLOR and native sprite geometry.
2. B685 stopped trusting cached PNG/readback artwork. B686 tried a GPU boot
   copy; B687 returned to the exact native atlas used by later working loads.
3. B688/B689 retained the renderer through head replacement. B690–B692 removed
   exact covering native overlays, preserving native loading and animator work.
4. B693/B712 retained presentation beyond native loading flags and menu-instance
   creation, until a real head consumer and original menu submit.
5. B713 rejected blank/foreign menu proofs and reserved bounded evidence.
   B716 retained this through the requested release main1.2.0 rebase.

All those releases still received a negative initial-spinner hardware report.
Several repaired actual independent defects; none proves the headset symptom
resolved. Earlier mono-camera, owner-state and active-renderer evidence cannot
be promoted to final-eye proof.

B716's current spinner remains admitted at alpha1 for50seconds and retires at
its90second safety limit. Thus simply extending lifetime again is contradicted
by the evidence. Its old48x32 whole-eye probe can miss a thin rotating ring;
actual native rendering reproduces that false zero. A native masked menu image
also needs its parent stencil context, which the private eligibility target
did not contain. Neither zero is reliable evidence of a dead atlas or invisible
real menu.

B717 keeps native art, UVs, animation, tint and premultiplied RGBA, but explicitly
places only the initial Quest spinner in the foreground with depth writes off
and depth test Always. Real depth-writer and late blank-capture controls hide
the old draw while retaining the new one. The source probe fits the consumer
bounds; private menu eligibility neutralizes only its missing private stencil
comparison. Pending private readback targets survive unused-asset collection.
The tiny verified Android/Vulkan shader is embedded in the mod assembly, leaving
all four existing authored asset banks intact. Desktop and scenario spinners
retain their established path. No extra loading delay or new timeout is added.

This is the best next rendering experiment, not another lifetime hypothesis.
If the headset gap remains, inspect the actual final eye region after composition
rather than adding flags, trusting private source pixels, or extending the timer.

## Ping and shield

[FX review](QUEST717-FX-REVIEW.md) records why B712's forceRenderingOff approach
froze native Pause-culling clocks, and why merely narrowing native streams in
B711/B716 did not repair the hardware artifact. B717 uploads the exact live
native billboard/shield mesh geometry with explicit Float32 COLOR. The original
renderer remains submitted with a private black-texture copy of its original
additive shader during the head draw, preserving native culling and playback.
The visible upload retains original material, property block, depth and timing;
the native material is restored afterward and when pooled or superseded.

The shield correction admits only the exact native TargetShield prefab.
The video cannot conclusively distinguish this from DefaultShieldHitEffect or
an actor override. The separate exact default hit prefab therefore gets one
bounded Debug observation; its unqualified distortion/fog remains untouched.
The integrator corrected its direct logging call to fully qualified
`UnityEngine.Debug.Log`, avoiding the original conditional global Debug facade.
No generic particle rewrite, shader substitution or animation redesign is used.

## Performance: findings, changes and remaining work

[Performance review](QUEST717-PERFORMANCE-REVIEW.md) records17,520scenario frames
over910.358seconds, about19.25sampled frames/second including transitions. This
is not compositor FPS. The latest24slow frames contain no GC collection;
the capture has lost the actual FRAME/STEPS/SPLIT windows and cannot attribute
the sustained cost to CPU, GPU, draw calls, game logic or thermal state.

Figure distance derivatives are actual consumers:102,818→63,774vertices.
Terrain reduction also submits, but its measured PreCull work is about0.062ms
on average. Material and environment driver enablement is not proof of useful
batch coverage; old counters lack the camera and rejection context. Pillar LOD
stays removed. No visibility or material ownership guard is weakened to inflate
coverage, and no new wall hiding or image-quality reduction is introduced.

B717 retains a bounded history of the already-composed performance windows in
the exported state, adds exact world-material refusal/camera context, and binds
the current XR provider GPU timer. Unity reports that timer in seconds; convert
it to milliseconds and keep false/invalid readings unavailable, with the legacy
fallback unchanged. This generic timer repair also ships as source-only dev
commit `25d9c22f0`; the PC build identifier remains715 with no new PC artifact.

One small CPU change caches immutable reflected UI field metadata while reading
live pooled field values on every call. It does not remove the known scene-tree
queries, and no large FPS improvement is claimed. Similar appearance to Demeo
does not establish equivalent assets, shader passes, batches or CPU workload.
Use the next retained CPU/GPU windows and exact material coverage to select a
targeted optimization while preserving the scene. The current evidence does
not justify choosing a particular expensive pass or admitting unknown renderers.

## Qualification and delivery scope

- Strict final Release compilation: zero warnings/errors against the real owned
  game SDK. Actual mod AOT/static registration and original-API emission run on
  the integrated B717 artifact; exact counts/hashes live in the handoff receipt.
- Loading: real native geometry/alpha397,143assertions; GL lifecycle831assertions,
  71spinner checks and197menu observations; Vulkan lifecycle792/71/157.
  Native premultiplied RGBA matches at opacity0.1/0.5/1. Corrected undersampling,
  masking, lifetime and independent bad-composition controls are documented.
- FX:251assertions, twelve native pixel cases with maximum RGB difference0,
  ten automatically playing native systems at six observations. Six earlier
  controls and two targeted layout/scale controls remain distinguished.
- Embedded shader:14actual Android/Vulkan metadata checks,13resource/failure
  cases with64assertions, actual final plugin resource/hash closure. Two fresh
  private compiler projects produce the same16,936byte bank.
- Performance: history16assertions/five controls; native material791/two controls;
  census1,489assertions plus13real engine frames and one retention control.
  GPU provider probe16assertions/four controls with actual SDK shape evidence.
- UI field-cache behavior116assertions/twenty controls; original SDK26ABI checks.
- Source group16/16, followed by the three affected source checks after final FX
  integration. Sixty bundled-shader wire vectors run on the integrated tree.
  The unchanged full300,601wire vectors and unrelated main release/runtime
  evidence are inherited from B716, not rerun as a new complete local gate.

The release archive and actual production update planner must show changed code
with unchanged art, helpers, original conversion and movie/depth recipes. Updating
B716 recompiles matched native code/metadata and republishes/signs the APK;
it does not reconvert Campaign data, rebuild mod banks or regenerate3D movies.
No full local game import or APK build is performed by this delivery round.
Actual extracted launcher/API checks are Linux host evidence; no Windows Unity
end-to-end or new headset acceptance is claimed.

For the next hardware run, check the entire Intro-to-visible-menu interval, native
ping rise and tile, then a shield hit. Keep Standalone settings/scale unchanged;
let room one settle, open room two with walls visible and export while the app
is still running. The bounded histories then provide the missing named CPU/GPU
windows. A visually successful candidate still needs this performance comparison.

After qualification, remove only this round's stopped, clean worker worktrees
and regenerable owned caches. Preserve all hardware inputs, matching managed
artifacts, logs, results, source pins and current deliverables. Measured reclaimed
bytes and final source/archive hashes belong in the handoff receipt.
