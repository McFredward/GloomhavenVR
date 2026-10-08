# NPC645 motion and hover proof audit

The new hardware report is a Build643 observer-picture defect, not another
missing-card first-picture report. Both frozen `LogOutput.log` banners name643.
The host uses Direct3D11 on an RTX4090 and the peer an RTX5070Ti. The frozen
receipt under `.planning/debug/npc645/inputs/receipt.json` binds the paired logs
and supplied `pristerin_menu.mp4` to base
`29d4b6dd85e6ce3b3d19b531bde79d2fcc44e587`.

The video shows enhancement rows changing their relative vertical positions;
the Wound row overlaps the Confusion row while the surrounding panel remains
continuous. This is separate from the intended native hover tint/scale. A local
capture does not expose numeric packets or exact ring sample intervals. Logs
report complete original enhancement pictures and do not encode every rendered
row corner or angular velocity. Neither is a source of per-frame timing proof.

## Why inherited green evidence did not cover this report

The639 first-picture proof remains useful for native assets, finite transport
admission and complete offered-card/list/confirmation rendering. Its subsequent
continuous check samples a constructed ring at125 degrees per second and checks
visibility, square dimensions, plane alignment and total travel. The assertion
`checks >= 8 && remoteRingTravel > 45` does not bound stationary intervals,
angular jumps, reverse steps or velocity changes. Several large corrections
with long pauses can pass that assertion. Its constructed source animation is
also much faster than the game's authored20-second ring revolution.

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

`TownServiceBinding.Read` captures RectTransform `anchoredPosition3D`; its
`Apply` restores the same anchored coordinates. `TownServiceMotion.Read` and
`Write` instead cache/interpolate `localPosition`. Unity derives local position
from a rect's anchors, pivot and parent dimensions. A changed parent can thus
change a child's local position without changing its anchored coordinates.
Binding order is stable identity order, not hierarchy order. Cached local
positions must not become new owner targets when a subsequent parent rect
write changes that derived reference. A parent-first pass alone would hide
this example without fixing the incorrect coordinate contract.

`TownServiceMotion` originally uses one sample/start clock for all properties
of a node. A simultaneous scale/color/layout sample can overwrite the clock
used to interpolate that node's sparse ring rotation. Its pure-spin test is
against the current displayed intermediate state, so an unfinished unrelated
property can also force a shorter mechanical duration. The composed frame's
maximum sample time is not necessarily the ring property's own source time.

The final offered native pose also has a second writer:
`TownServiceMirror.ApplyRemoteMotion` runs `ApplyOfferedFrames` after individual
native motion. `PrepareOfferedFrameMotion` interpolates kind9 print-relative
rotation. Detached native Aura partitions can therefore have their final pose
written by this path. A passing isolated `TownServiceMotion` test is insufficient
unless the actual partition/capture/apply path proves that the final writer
retains the same continuous owner rotation and exact print relation.

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

Final worker results and the independent review of their exact checked source
will be recorded here before handoff. A focused local Unity/Mono pass will not
be described as a new complete-suite pass, an actual WAN latency guarantee or
a rendered-HMD animation acceptance.
