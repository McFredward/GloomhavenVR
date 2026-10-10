# NPC665: continuous native offered-card facing

## Evidence and scope

Worker base: `c9462f78b`, ModBuild 664. Both frozen main and remote log
banners in the parent NPC665 inputs say 664. This round has a user report of
unsmooth offered-card facing but no new yaw video. The supplied screenshot is
assigned to the independent enhancement-overlay lane. The findings below are
source and Unity-runtime evidence, not an attribution of a headset image.

The owner still calls the original `TownServiceOfferingPose.Place` every frame.
Its head-facing direction, small intrinsic yaw, and `.006 * sin(age * 1.8)`
hover are unchanged. No local-facing filter, extrapolation, new wire record,
changed admission deadline, or changed print depth is introduced here.

## Two source causes

`ApplyRemoteMotion` previously rejected an ordinary numeric root whenever its
sample time preceded the most recent native UI frame. A caption/artwork header
therefore invalidated an otherwise current offered root between 15 Hz numeric
samples. Native UI updates and physical motion are separate owner clocks.

The merchant additionally lacked the enchantress's composed offered-root
interpolation. Its private native offered item used the generic host/root tween,
which a newer UI header could reset. Simply exempting its root from the header
timestamp guard made this worse; it needed the existing offered-root clock too.

At 90 Hz rendering with the actual owner `Place`, capture, codec and receiver:

| Source/control | Native owner step | Remote step | Result |
| --- | --- | --- | --- |
| 664 enchantress, UI heartbeat at frame 80 | about 0.4105 degrees | 0.6579 degrees | unchanged speed assertion fails |
| 664 merchant, UI heartbeat at frame 80 | about 0.4104 degrees | 0.1174 to 0.5865 degrees | slowdown then recovery |
| Header exception alone, merchant frame 81 | 0.4104 degrees | 0.8595 degrees | unchanged speed assertion fails |
| Both repairs, same source/render schedule | about 0.41 degrees | about 0.398 to 0.415 degrees | passes |

The native owner remains smooth in all these cases. Filtering its facing would
not correct either source cause.

## Narrow clock authority

`ContinuousOfferedRoot` permits a private, visible lane-0, hand-0 manifest root
only for the exact live module/session/service/structure/claim/binding/census.
Enchantress faces and the native highlighter qualify. Merchant numeric item or
inspection-body roots qualify only while the native transaction is active and
its transaction owner is this peer. Public catalog and stock/wrist fan originals
do not acquire an offered clock from their address.

A newer header still controls visibility, parent alpha, canvas presence,
rectangle, settings, sorting and scale immediately. Pending or live native
returns supersede the offered clock. An expired return slot does not block a
fresh offer until the unrelated three-second slot timeout. A first visible
reoffer starts from its current authored facing rather than interpolating from
the withdrawn original.

The parallel return lane adds immutable `ReturnOrigin` preparation epochs and
`RemoteModule.ReturnOriginChangedAt`. The integrator must reject an offered
numeric root sampled before that floor. This completes rapid cancel/reoffer
without a census gap: a changed origin revokes the old root; an identical-origin
UI heartbeat leaves its clock intact. This baseline-664 worker cannot compile
those new DTO fields, so the integrator owns that additional guard and its
actual 116/rekey test.

## Maintained runtime proof

Run:

```sh
python3 scripts/npc665-yaw-runtime/run.py --controls
```

The new suite uses the actual native offering pose, procedural card backing,
enhancement mask, capture/scheduler/packing/decoder, binding, and remote render
path. Pooled artwork and gameplay-model callbacks are declared fixture inputs.
A hashed deterministic source/render time port changes no production code.

Eight cases cover both NPCs, ordinary and frequent UI headers, and regular or
irregular delivery. Irregular cases include reversal, delay, loss, and reordering.
Assertions inspect actual per-render angular increments and same-time packet
arrival continuity; the expected motion is not derived by reimplementing the
receiver interpolation. Finite drain must end at the latest authored facing.
Every measured render after the initial clock warm-up checks all native backing
vertices against their print affinity; first-visible reoffer is also checked.
Enhancement rows and ring plane use independent actual native corner snapshots
from the latest transmitted relation. The original hover waveform is checked
directly. GPU camera readbacks compare both opposing sides' real silhouettes,
with PNGs and measured ink/difference counts retained. Hidden withdrawal and
first-visible reoffer exercise the real capture/receive path. A real production
return descriptor and decoder verify the live/expired Kind8 ownership boundary;
this boundary check does not replace the independent native return migration
suite.

Negative controls retain the same assertions and fixture:

1. `old-header-clock` removes only the offered header-clock exception. It fails
   the same-time arriving-root/UI-target no-snap assertion for the enchantress.
2. `old-merchant-clock` excludes the merchant from the dedicated root clock. It
   fails the unchanged merchant angular-speed assertion after a native header.

The final nullable-flow correction restores a direct `module.LastFrame == null`
test; it does not change the helper's runtime conditions. Both controls were
rerun against that exact condition.

## Comparison boundary retained explicitly

The anisotropic native canvas test initially compared the observer with the
owner's *current, not yet transmitted* enhancement shape on every 90 Hz frame.
It produced a 0.206 mm corner difference, independently of this yaw repair.
The native mask can change its relative shape as the owner turns between 15 Hz
relation samples. A receiver cannot know those unsent values. The failed source
receipt is retained; no tolerance was increased to hide it. The maintained proof
compares against actual native corner snapshots at the latest accepted source
sample, while body/face coupling remains strict on every rendered frame. It
does not claim exact parity with an as-yet-unsent native owner layout.

## Receipts and validation

Evidence remains under `.planning/debug/npc665-yaw/` in this worker and is
archived without Unity caches under the parent's `.planning/debug/npc665/yaw/`.
Notable receipts:

- `baseline-red/run-xq87gk8j/proof/run-zaokc0n6`: actual 664 header-induced RED.
- `run-ensf1z6z/proof/run-nu4fde73`: private header-exception-only diagnostic,
  separating the merchant clock defect.
- `geometry-positive/run-q0lc17vj/proof/run-iggoqqy3`: preserved unsent-native
  anisotropic corner comparison RED.
- `sampled-geometry/run-um29sfo5/proof/run-g8ww1b7g`: corresponding unsent ring
  comparison RED, retained separately.
- `final-nullguard/run-ky68573m/proof/run-2iwu7m76`: positive and both controls
  pass after the strict compiler-flow repair.
- `final-recorded/run-555yqme_/proof/run-8zv6mq90`: final positive and both
  controls pass: 194,636 assertions, 32 production GPU PNGs, eight render CSVs
  and 16 measured silhouette comparisons (zero changed silhouette pixels).

Strict Debug build passes with zero errors and warnings. All 16 source checks
pass at `source/results.json`. Final runtime totals are retained in the receipt's
`production-evidence/assertions.txt`. The worker
does not claim a new complete local gate; the primary agent runs it on the
composed integration. No Build664 headset yaw outcome has been verified by
these tests.
