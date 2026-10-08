# NPC 645: continuous native offered-card ring

## Scope and evidence

The paired hardware logs are Build 643. They do not contain a numeric per-node
ring trace, and the supplied movie principally shows the enhancement menu. The
ring diagnosis is source-proven and reproduced with the shipped native effect;
it is not a measured packet chronology claimed from those logs. Headset visual
acceptance remains pending.

The maintainer's explicit 2026-10-08 exception permits a different intrinsic ring
phase, while requiring the same authored speed/direction and shared card yaw,
orientation, size, center, alpha, content and selectable overlays. That exception
also permits intrinsic offered-card bob phase for merchant/enchantress at the
same amplitude and rate; this change does not alter their bob or actual flights.

## Cause and earlier blind spots

The native `UIEnchantressEffect.Rotate` uses a 20-second revolution at speed 1:
18 degrees/second, on Unity's normal scaled clock. The native fitting mask can
place the Aura on a principal stretch axis and carry its remaining phase into
its original Graphic children. This combines a root registration and child
rotation. Generic `TownServiceMotion` classified a continuous spin only when
rotation was the sole changed complete-state property. Correlated scale/fit or
alpha updates selected the short interpolation clock and produced stops between
packets. The final kind-9 offered-frame writer also applied a separate root
rotation clock after generic child interpolation.

Earlier green tests did not reproduce that composition. One spun a synthetic
Highlight at 125 degrees/second and accepted aggregate travel, which permits
pauses and jumps. Another tested an unrelated root update instead of a native
pulse/fit on the rotating branch. A standalone independent rotation-channel
experiment reproduced 164 stalled frames on the original implementation and
passed 509 assertions after a tentative repair, but the complete final-writer
path still jumped. That tentative `TownServiceMotion` repair was removed.

A direct live-template prototype exposed another false-green risk: real
`NativeTemplates.Freeze` strips `UIEnchantressEffect` before Warm/Resolve calls
RegisterTemplate. Timing must be retained before that strip and supplied by both
registration paths, including a retained bank after network teardown.

## Final implementation

Freeze records the exact native serialized rate and the owning original Aura
transform in immutable Part provenance. Both Warm and Resolve pass it to the
mirror; ordinary templates are unaffected. Observer callbacks, controllers and
animators remain stripped. A final presentation clock rotates only the three
original Aura graphics after all native numeric/print fitting. It uses
`Time.timeAsDouble`, respects pause/time scale, stays idempotent within one engine
frame, and wraps its local angle to preserve long-session precision.

The final fit resolves the actual parent matrix, native principal stretches and
print plane. It does not use a quaternion product to approximate an anisotropic
matrix. The unsplit route derives size from the current complete authored Aura
TRS instead of a rendered interpolation. The detached Aura publishes actual
transformed XY axis lengths in existing kind-9 scale fields, avoiding Unity's
approximate 3D `lossyScale`. No wire field or protocol grammar was added. Native
center, size, pulse, alpha, card orientation and other overlay geometry remain
authored. Withdrawal, inactive offers, reset/new print and module retirement end
or reset the clock. Native Stop in the real game is followed by hiding the holder;
an arbitrary manual Stop while keeping the same visible offer is not an exposed
native workload and is not inferred from an independent local clock.

## Verification

Run the focused suite with:

```sh
python3 scripts/npc-ring645-runtime/run.py
```

`--case production|partitioned|reversed|normal-clock` resumes one affected case.
The suite imports the actual eleven-node original highlighter, all three original
Aura sprites, native serialized effect/loop metadata, and actual game animation
classes. Card-model construction is an explicit external boundary. Freeze,
neutralization, partitioning, Warm/Resolve, source mask, production capture,
codec, receiver and final render writers execute. Source/fixture/DLL hashes and
native provenance are recorded beside the results.

Deterministic geometry cases bind only Unity's scaled clock boundary to the same
manual time used by the actual source LeanTween. The separate normal-clock case
compiles the production `Time.timeAsDouble` line unchanged and runs the original
normal-delta LeanTween in real Unity play mode. It covers pause, half/double speed
and same-frame repeated final writers. The deterministic clock starts at one
billion seconds to exercise large-clock anchoring. Cases cover moving print
yaw/pitch, nonuniform ancestors, principal 0/29/90/-29-degree boundaries,
irregular/drop intervals and older artwork heartbeats. Every render must match
its exact authored signed increment (0.2 degrees at 90 Hz for ordinary speed),
without packet-sized pauses/jumps. Shape checks require the native print plane,
square orthogonal ink and matching center. After settling, **all three original
sprites** must match the authored diameter within 60 micrometers. This strict
check caught and repaired an additional 0.74% unsplit and 0.125% detached error.

The transient comparison to the current *unpublished* owner remains separately
bounded at 5% under deliberately delayed/dropped changes; the measured maximum
was 3.041644%. This is sampled interpolation lag, not an approved persistent size
change. The strict settled check prevents a uniformly wrong-size circle passing.
Hidden-interval tests advance the scaled clock and assert that cached offered
clocks are retired; cached bindings reopen at the authored rate.

Final focused receipts (worker-local paths):

- `.planning/debug/npc-ring645-final3/run-8gn9lbsr`: production, reversed and real
  normal clock pass; both compiled negative controls fail the exact per-render
  rate assertion as expected. The subsequently repaired detached-size case was
  the only failure in that run.
- `.planning/debug/npc-ring645-partition-size/run-j76y9lv2`: repaired actual
  partition passes, including strict diameters and withdrawal/reopen.
- `.planning/debug/npc-ring645-existing/run-mir4otuy`: existing production
  offered-orientation suite, 602 assertions. The native-only final axis-length
  correction does not enter its unmodified synthetic template route.

The compiled controls remove the final native clock (retaining the old sampled
writers), or omit frozen metadata from Warm while Resolve sees an already cached
inert template. Both reject the former false green at frame 2: expected 0.2
native degrees, observed zero. The fixtures are not a WAN/headset latency or
visual acceptance test. No new complete-gate result is claimed.
