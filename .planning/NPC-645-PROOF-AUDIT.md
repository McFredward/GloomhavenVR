# NPC645 motion and hover proof audit

The new hardware report is a Build 643 observer-picture defect, not another
missing-card first-picture report. Both frozen `LogOutput.log` banners name 643.
The host uses Direct3D11 on an RTX 4090 and the peer an RTX 5070 Ti. The frozen
receipt under `.planning/debug/npc645/inputs/receipt.json` binds the paired logs
and supplied `pristerin_menu.mp4` to base
`29d4b6dd85e6ce3b3d19b531bde79d2fcc44e587`.

The maintainer's explicit 2026-10-08 follow-up permits different intrinsic
ring-spin phases between viewers, provided the ring keeps the same native
direction/speed and remains smooth. Its facing/yaw must still follow the
offered card exactly. This narrow phase exception does not cover card, row,
overlay, effect-state, facing or transaction UI differences.
The same explicit follow-up permits different timing phases for the intrinsic
up/down float of offered merchant/enchantress cards, while preserving native
amplitude, waveform and speed. It does not permit arbitrary position offsets,
different card-facing/content, displaced overlays or unsynchronized flights.
This report introduces no separate bob-motion repair merely because that
narrow phase exception is now available.

The video shows enhancement rows changing their relative vertical positions;
the Wound row overlaps the Confusion row while the surrounding panel remains
continuous. This is separate from the intended native hover tint/scale. A local
capture does not expose numeric packets or exact ring sample intervals. Logs
report complete original enhancement pictures and do not encode every rendered
row corner or angular velocity. Neither is a source of per-frame timing proof.

## Why inherited green evidence did not cover this report

The 639 first-picture proof remains useful for native assets, finite transport
admission and complete offered-card/list/confirmation rendering. Its subsequent
continuous check samples a constructed ring at 125 degrees per second and checks
visibility, square dimensions, plane alignment and total travel. The assertion
`checks >= 8 && remoteRingTravel > 45` does not bound stationary intervals,
angular jumps, reverse steps or velocity changes. Several large corrections
with long pauses can pass that assertion. Its constructed source animation is
also much faster than the game's authored 20-second ring revolution.

The earlier offered-orientation proof retains intermediate corners and native
planes during gaze turns and separately verifies settled geometry. It samples
regularly and tests final static pixels after the independently sampled target
finishes. This is not a guarantee of angular continuity between independently
scheduled pulse, geometry, artwork and rotation updates.

The earlier numeric-hover test changes a simple text child's anchored position
and root pose, then waits for convergence. It does not reproduce the native
stretched row hierarchy with independently changing parent rects and hover
geometry. An exact settled endpoint does not establish that intermediate
anchored child positions stay valid.

## Runtime seams requiring direct causal controls

The actual full-path hover reproduction narrows the cause further than the
initial coordinate hypothesis. `Binding.Apply` always centered the original
root's anchors at the end. Original artwork and a fresh numeric root then
immediately restored the native parented layout and header pose, hiding this
write. A newer caption/artwork frame can make the retained unchanged numeric
root older than `LastFrame.SampleTime`; the normal receiver correctly omits
that older root on a later child-only hover. That pass no longer has a final
header pose to mask the unwanted recentering. The original row moves through
its neighbours while its own native child coordinates can remain valid.

The bounded repair retains the original native root anchors/extent throughout
Binding and preserves the separately authored root position when rect
anchor/pivot values are written. Actual detached hosts keep their existing
explicit detached-root pinning. The simple 26-node fit/hover fixture passed
with the old source, so it is retained as positive coverage and **not** used
as a causal control. A first fixture NullReference is likewise not evidence
of the hardware cause. The later full capture/codec/motion/receiver fixture
first performs a genuine caption/artwork refresh, then a child-only hover
against the stationary root. That is the needed causally different workload.

`TownServiceBinding.Read` captures RectTransform `anchoredPosition3D`; its
`Apply` restores the same anchored coordinates. `TownServiceMotion.Read` and
`Write` instead cache/interpolate `localPosition`. Unity derives local position
from a rect's anchors, pivot and parent dimensions. A changed parent can thus
change a child's local position without changing its anchored coordinates.
Binding order is stable identity order, not hierarchy order. Cached local
positions must not become new owner targets when a subsequent parent rect
write changes that derived reference. A parent-first pass alone would hide
this example without fixing the incorrect coordinate contract.

For example, a top-anchored child with anchoredY 0 has localY 50 inside a 100px
parent and localY 80 inside a 160px parent. During an unfinished parent tween,
restoring cached localY 80 first can assign a nonzero anchoredY; restoring the
parent afterwards then moves the child again. An unchanged native child
property is correctly skipped by Binding, so the displaced derived local
position can be retained as a new motion target. In anchored coordinates,
the unchanged native intent remains 0 throughout both parent writes.
This coordinate-space concern is documented as a candidate seam, not as the
proven cause or a shipped Motion coordinate change. The final repair does not
change Motion's coordinate contract merely because this simpler hypothesis
initially appeared plausible.

`TownServiceMotion` originally uses one sample/start clock for all properties
of a node. A simultaneous scale/color/layout sample can overwrite the clock
used to interpolate that node's sparse ring rotation. Its pure-spin test is
against the current displayed intermediate state, so an unfinished unrelated
property can also force a shorter mechanical duration. The composed frame's
maximum sample time is not necessarily the ring property's own source time.

The final offered native pose also has a second writer:
`TownServiceMirror.ApplyRemoteMotion` runs `ApplyOfferedFrames` after individual
native motion. `PrepareOfferedFrameMotion` interpolates kind 9 print-relative
rotation. Detached native Aura partitions can therefore have their final pose
written by this path. A passing isolated `TownServiceMotion` test is insufficient
unless the actual partition/capture/apply path proves that the final writer
retains the same continuous owner rotation and exact print relation.

There are also two legitimate components of the native visible ring phase.
`TownServiceNativeEnhancementCardMask.AlignNativeEffects` compensates stretched
or sheared parents by fitting Aura's principal axes and transferring the
remaining `nativeAngle - angle` into each original Graphic child's local
rotation. The native Aura root's offered registration and its Highlight child's
residual rotation must therefore be reviewed together. Assuming that kind9
always contains the entire visible ring phase would be another proof mismatch.
The old 639 continuous fixture directly rotated the Highlight child, rather
than running the game's actual Aura animation and this compensation.

A further independent audit found a real preparation-path mismatch in the first
local-phase candidate. `NativeTemplates.Freeze` neutralizes its frozen copy
before partitioning it. `Part.Original` is consequently an inert subtree;
`WarmEnhancementBasis` and `Resolve` pass that subtree to `RegisterTemplate`.
Reading `UIEnchantressEffect` there cannot discover its original serialized
rotation settings, because neutralization has already removed that controller.
A fixture which directly registers a live source controller can pass while the
real frozen-template path never enables the intended ring clock. The authored
rate must survive the real freeze/partition/registration path as inert metadata,
without preserving or running the gameplay controller. The final proof must
exercise that path, not only direct live-template registration.

## Required bounded replacement evidence

The focused replacement checks must fail under the exact previous runtime
behavior, rather than under a malformed codec or an unrelated missing asset.
They should retain source hashes, initial failures and final receipts.

- Native slow ring samples interleaved with faster same-node pulse/scale/fit
  updates and unrelated root/hover/artwork updates; per-render progress, no
  stationary gaps or angular reversals, and exact stop/reset behavior.
- Final detached native Aura output through the actual capture/codec/motion
  receiver and offered print registration, including a full rotation wrap.
- Native stretched enhancement rows with a changing parent rect, scroll and
  hover children, comparing anchored positions and world corners throughout
  intermediate playback rather than only after settling.
- Original ring/list artwork, visitor-local pre-drop guides and inert native
  callbacks retained; wire size/cadence and native property formats unchanged.

## Independently reviewed hover result

Worker commit `56837e4d3` contains the bounded `TownServiceBinding` repair and
the focused native hover fixture. The final receipt is
`town-hover645/run-q_9w6k_7` in that worker's private debug directory. Its
`source-hashes.json` binds production `TownServiceBinding.cs` to
`e986208bd3556cb3cc227d55c7f915f3e02546ff1bec2cae3876cd3719fa8589`.

The fixture asserts a real kind 1 compact root in the baseline, a later complete
caption/artwork frame, and only kind 2 child properties in the subsequent hover
pass. All 12 stationary hover render-loop samples retain world Y exactly. The
36 native scroll/hover/parent-fit samples retain all native child Y coordinates
with measured maximum error 0 px; settled root positions retain their row
membership. Four anchor/pivot variants preserve original dimensions and pose
with or without a fresh header; explicit detached hosts retain their existing
pivot pinning. The original 14 px horizontal hover is preserved.

The actual owner and observer 768 x 512 Unity readbacks differ in only 66 pixels
above a 30-level RGB threshold. An independent inspection of both pictures
confirms the same three adjacent original rows and contents. The exact old
root-centering/layout behavior fails the stationary-row assertion with
0.09999996 m of unrelated displacement and 90,778 differing pixels; the negative
readback visibly overlaps Wound with Confusion, matching the reported class of
defect. The production run reports 107,480 assertions, including native corner
and pixel checks. These totals describe checks, not independent hardware trials.

Earlier simple native hover still passed unchanged source, and one initial
fixture failed with a setup NullReference. Those are retained as proof limits.
The intermediate candidate preserving layout position but still centering the
root failed by 0.1 m and was rejected. The final change alters neither motion
coordinate storage nor capture cadence, codecs or transport payloads. Existing
`motion-fast` focused coverage also passes (864 production assertions and all
five meaningful negative controls). This is inherited area coverage, not a new
complete local gate.

The final ring result and its exact checked source will be recorded below
before handoff. A focused local Unity/Mono pass will not be described as a new
complete-suite pass, an actual WAN latency guarantee or a rendered-HMD animation
acceptance.
