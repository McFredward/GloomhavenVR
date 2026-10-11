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
The host's seven original admission/display pairs and their `age=0` describe
the local admission clock, not end-to-end wire latency. A painted count below a
required dependency count does not, on its own, establish a missing visible
widget. The peer's merchant-stock NullReference occurs after hosting ended and
loading began; it is not attributed to the active hover symptom. Neither paired
log contains a fast-motion exception or per-node angular/row sample trace.

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

## Confirmed runtime causes and bounded repairs

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

The initial hypothesis concerned Binding's native `anchoredPosition3D` and
Motion's derived `localPosition` under changing parent rects and identity-sorted
children. The simple native fixture passed unchanged source and did not confirm
that hypothesis. It is not attributed to the hardware report; no Motion
coordinate change is shipped. The full-path root-layout control above determines
the bounded repair instead.

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
rate now survives the real freeze/partition/registration path as inert Part
metadata, without preserving or running the gameplay controller. Both eager
Warm and later Resolve pass that metadata: repairing only Resolve would leave
an already cached inert template without its clock. Retained frozen parts can
restore it after template/network teardown.

The source clock is also part of the native contract. The original
`UIEnchantressEffect.Rotate` tween uses LeanTween's default normal delta,
ultimately `Time.deltaTime`, rather than an explicit unscaled override. A local
clock driven solely by the network's unscaled receipt time would keep rotating
during a native pause and use the wrong speed at other time scales. A manual
LeanTween pump is useful for deterministic geometry but does not prove this
default clock. The final observer uses `Time.timeAsDouble`; coverage includes
normal scaled time at 0, 0.5 and 2, and repeated observer draws in the same Unity
frame do not multiply progress.
The actual lifecycle stops through native shop/card-holder hide or offering
removal; a hypothetical manual stop while still visibly offered is not claimed
as a gameplay case.

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

## Independently reviewed final ring result

The final repair changes only `TownServiceMirror.Offerings.cs`,
`TownServiceMirror.cs`, `NativeTemplates.cs` and
`NativeTemplates.EnhancementPreparation.cs`. `TownServiceMotion.cs` is unchanged.
The new final presentation clock runs after the generic numeric and offered
writers. It rotates the original three Aura Graphics in the current shared
print plane at the native signed rate, with a viewer-local phase under the
maintainer's explicit exception. The game-authored 20-second revolution remains
18 degrees per second. The original controller stays stripped on observers.

The native receipt contains the actual eleven-node holder and all three original
Aura sprites. Its five-node Aura owns the original controller and native
Highlight/Buy/Sell graphics; the clock metadata belongs only to that owning
non-Graphic mount. Original level-up/frame and selectable enhancement graphics
outside Aura do not join this clock. Both the normal unsplit holder, where Aura
has a nonzero binding index, and a forced detached Aura partition are exercised.
Freeze, neutralization, partitioning, Warm/Resolve, native mask, capture, codecs,
receiver and final writers all run. Constructing the external card model remains
an explicit fixture boundary.

An initial independent-rotation-channel repair passed 509 standalone assertions
but the full final-writer route still produced approximately 3.25-times the
expected per-frame rotation. It was rejected. A later quaternion-based fit
produced an ellipse under anisotropic ancestry. The final implementation instead
uses the observer's actual parent matrix and the native principal-stretch basis.
It then expresses each Graphic's angle in that corrected parent plane. Current
complete authored Aura TRS sets the unsplit diameter; the detached native Aura
publishes its exact transformed XY axis lengths in the existing kind-9 fields.
That special case is guarded by the known native Aura being the partition root;
other offered roots keep their original numeric and scale-flag behavior.

The strict settled-size audit also caught persistent errors which the initial
5% transient bound would have allowed: approximately 0.74% in the unsplit route
and 0.125% in the detached route. Both are repaired. The final three-sprite
endpoint assertion requires owner/observer diameter difference below 60
micrometers. Passing predicates establish that bound; the receipts do not print
each final absolute diameter, so no more precise endpoint measurement is claimed.

The final worker-local evidence is:

- `npc-ring645-final3/run-8gn9lbsr`: production, reversed native rate and the
  unaltered Unity normal-clock case pass. The detached strict-size check fails
  there and is explicitly superseded by the affected rerun below.
- `npc-ring645-partition-size/run-j76y9lv2`: the repaired actual detached route
  passes the strict diameter and withdrawal/reopen checks. Its production
  `TownServiceMirror.Offerings.cs` SHA256 is
  `37ea7f72197443a376b2ee413734c40e4f7923b91a700caafa082f1c0ddc2787`,
  independently matched to the final reviewed source.
- `npc-ring645-existing/run-mir4otuy`: existing offered-orientation coverage
  passes 602 assertions. The final native-only XY-axis correction does not enter
  that synthetic, non-Aura route.

Each deterministic case renders 540 frames with native LeanTween, nonuniform
ancestry, moving print yaw/pitch, principal-axis changes, irregular/dropped
packets and older artwork heartbeats. It binds only the scaled-clock boundary
to the fixture timeline, starting at one billion seconds. Every render checks
the exact signed increment, ordinarily 0.2 degrees at 90 Hz, rather than total
travel. Recorded final production and detached runs have zero stationary or
reverse steps; double-speed increments peak at approximately 0.4 degrees. The
separate real Unity case leaves the production `Time.timeAsDouble` call unchanged
and runs normal-delta LeanTween through pause, half/double speed and repeated
same-frame final writers. Native plane, orthogonal square basis, center and all
three strict settled diameters are checked.

Two compiled controls reject the previous blind spots at frame 2: disabling the
final local ring clock, or losing frozen metadata on Warm before Resolve sees
the cached template, gives 0 degrees where the native increment is 0.2 degrees.
They are targeted behavioral mutations, not claims that the entire old revision
was recompiled unchanged. Hidden-interval checks advance the bound scaled clock
and verify cached offered clocks are retired; an inert cached binding reopens at
the native rate. Source Stop is tested with the actual following holder hide and
offering withdrawal, rather than an unsupported visible manual-pause workload.

The source review of game clock setters establishes the regular multiplayer
boundary: gameplay speed controls change Chronos's global clock rather than
Unity `Time.timeScale`; native UI LeanTween continues on Unity's normal clock.
The game's Unity-time debug pause is explicitly disabled online. Other discovered
Unity-time setters belong to demo or offline capture code. This does not assert
support for an external mod arbitrarily changing only one viewer's Unity clock.

The largest measured transient diameter difference from the current
*unpublished* owner is 3.041644% during deliberately delayed/dropped updates.
This remains sampled geometry interpolation lag, not an approved persistent
size divergence. Native alpha/pulse and other properties retain their existing
sampled transport and interpolation; this change does not make their phase local
or establish pixel equality at every WAN instant. Card-facing, center, content,
selectable overlays and flights retain their existing shared ownership. The
ring phase exception does not weaken those contracts.

No new wire fields, protocol grammar, publication cadence or diagnostic stream
is added. Visitor-local pre-drop guides and inert native callbacks remain intact.
This bounded audit accepts the source repairs and causal local controls. It does
not claim a new complete gate, WAN latency measurement or HMD visual acceptance;
the integrator records its combined focused checks and inherited gate separately.
