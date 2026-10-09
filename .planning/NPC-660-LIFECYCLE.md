# NPC660 native layout and retained-mount review

Worker base: current `dev` Build659, `c0b5e9a1b`. Supplied owner and observer
banners both identify Build658, assembly1.1.0.0. The immutable paired inputs are
in the main checkout's `.planning/debug/npc660/inputs`; that directory's manifest
records all four log hashes. No new screenshot/video was supplied in this round.
This lane owns native mirror lifecycle/interpolation, not asset resolution,
publication queues, return clocks or the already stable enhancement depth fix.

## What the logs establish

The host observes peer2's enchantress transaction. The original author remains
visible in its native-output traces. The observer's admission reports can have
all required originals received and still wait on an ambiguous native texture.
For example, host `Player.log:19913` names `T_sphere_norm` with23/23 received;
`:33313` names `Default-Particle` with30/30 received. This is an asset validation
failure, not evidence of slow transport. The root integration owns that repair.

In the bounded observer visibility traces, sampled controls remain
`headerVisible=True`, `hostActive=True`. Native inactive `Top button/Content`
branches correctly have no ink on both sides. Those bounded samples do not
capture every frame and contain no world coordinates. They cannot establish
the cause of the user's one-frame button disappearance or measure sideways
motion. Neither unchanged admission counters nor these samples prove a stable
headset picture.

## Two source-proven geometry defects

The published632-era repair (`0918c1ac7`) preserves a native root's local position
while `TownServiceBinding.ApplyRootLayout` changes anchors/pivot/extent. That
protection is necessary, but the subsequent `TownServiceMotion.Write` repeats
those RectTransform setters during its render interpolation. If the sampled
positions before/after are identical, the old writer skips its position setter.
Unity nevertheless moves the root when those layout setters execute. The
binding pass is correct and the subsequent render tick moves the row sideways.

The new writer also reapplies the sampled interpolated position when layout
changes. It preserves original anchors, pivot, extent and native timing. It
does not substitute viewer gaze or relocate the workspace. The exact published
interpolator failed the new per-render position assertion in
`motion/run-n0lqtg4t`. The repaired writer passes; independently removing only
this position protection fails the same assertion.

The646 census repair preserves a retained native child with
`Host.SetParent(...,true)` before disposing a retired pooled holder. That preserves
the current image, but a running interpolation of the child's enclosing Canvas
host still has local from/to values belonging to the old holder. On its next
render the old writer replays those coordinates below the new parent.

`TownServiceMotion.Reparent` now rebases only that enclosing host's retained
from/to/before TRS into the surviving parent before disposal. The existing
per-node start times/durations, child transforms, original colors and alpha
targets remain. The continuing world path is retained, not canceled or snapped
to its endpoint. `PreserveCensusChildren` uses this method. True removal and
session closure continue to dispose their original modules.

The root-approved dimensioned `map.cardbody.` address family also survives the
existing independent released-card path, alongside legacy `map.cardbody|`.
Dimensions and original procedural geometry are authored by the delivery lane;
this lane changes only that existing presentation classification.

## Why earlier checks did not reject these paths

| Earlier work | Useful coverage | Missing challenge in this lane |
| --- | --- | --- |
|622 final native capture|Owner-authored final pose; no observer-facing controller|A correct capture does not protect a later RectTransform tween.|
|625/632 native template, partition and row repairs|Exact original properties, parented row anchors, independent ring clocks and print relation|Root position protection was tested at binding/registration; motion layout setters could move an otherwise correct root afterwards.|
|638/639 original dependency/admission work|Complete original dependencies, late original/cumulative deltas and receipts|Admission/readiness is not a render-position invariant.|
|646/655 retained census proof|Actual retirement, Camera/ReadPixels, unchanged child root and independent Graphic/color clocks|The animated native root child stayed below a static Canvas host. A running animation of the Canvas host itself was not challenged.|
|658 first-picture, return and border work|Exact complete render deadline under the recorded asset boundary; stable original border paint order|No new anchor-changing root tween or active enclosing-Canvas retirement challenge; this hardware round also exposes independent real-asset collisions.|

The existing646 test remains unchanged and passes934 assertions, including its
native root/color continuation and every-frame actual card readback. The new
test explicitly animates the enclosing host instead. It exercises nonuniform
Canvas scales and differently rotated surviving parents, checks the exact world
path throughout retirement, and checks original enabled/alpha/Canvas output
plus actual Camera.Render/ReadPixels at the authored panel center on every
checked frame. This guards one-frame disappearance in these reconstructed
layout/retirement cases; it does not identify every possible reported flicker.

## Validation and boundaries

`scripts/npc660-lifecycle-runtime/run-motion.py` binds the complete unmodified
production interpolation class into Unity2021.3.5f1. The fixture's ordinary
Image/RectTransform panel is an explicit engine boundary, not a substituted
version of the full game's enchantress card/controller/material output.
Source hashes, controlled compiled-source hashes, all build/editor logs and six
render readbacks are retained under `.planning/debug/npc660-lifecycle/`.

- `motion/run-e6x_i642`:80 assertions; constant header position through every
  anchor/pivot/size interpolation render; continuing authored Canvas-host path
  through retirement; button ink remains present in both cases.
- `motion/run-4juxa6t9`: layout protection omitted; named unchanged per-render
  position assertion fails as required.
- `motion/run-kbtfz3l4`: host animation rebasing omitted; named unchanged
  continuing world-path assertion fails as required.
- Existing offered orientation:602 assertions and six causal controls pass.
  Existing fast visitor motion:843 assertions and five causal controls pass.
  Existing actual native census/paint continuity:934 assertions pass.
- Final three affected registered scopes pass together:
  `.planning/debug/test-runs/20261009-212849-496e7d54/results.json`.
- Source16 passes; strict Debug has0 warnings/errors. This is focused worker
  evidence, not a new complete local-gate pass or headset acceptance.

The stable `TownServiceDepthOrder` implementation is unchanged. No new native
controller, gameplay callback, viewer-facing pose, wire record, config key,
periodic layout recall or diagnostic stream is introduced. The integration must
rerun its final affected/native asset checks and build/push the single660 stamp.
