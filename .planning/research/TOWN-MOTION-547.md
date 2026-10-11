# Build 547 occupation choreography

The hardware report rejects the merchant's imaginary typing and the enchantress's
small periodic wrist bob. These were positional hand targets without object-contact
phases or palm orientation; a coin followed a guessed grip offset, while an invented
pen oscillated over the ledger. None of that established credible tool use.

## Implementation

- Merchant: a 21.6-second authored sorting sequence transfers three original-game
  coins from individual seats into a stack and back. Each transfer has separate
  reach, grasp dwell, lift, inspection hold, deposit dwell, release and recovery.
  The index/thumb pinch is the IK contact target. Other fingers stay relaxed.
  Coin attachment changes only during a stationary contact dwell. Held coins stay
  attached when a visitor interrupts work; ungripped coins remain on the worktop.
  The imaginary pen/writing behavior is removed.
- Enchantress: a 20.4-second keyed performance alternates studying, anticipation,
  a broad two-arm spell, shaping/compression, settling and a quieter single-palm
  inspection. Torso rotation, shoulder/forearm roll, finger curl and palm position
  share the same sampled pose. Arm roll distributes the palm turn over the upper
  arm and forearm rather than twisting the wrist alone.
- Work gaze follows the actual coin grip or spell centre after this frame's activity
  sample. The elected author still supplies the smoothed head/eye pose to every peer.
  Render measurements also corrected a 35 mm pinch gap around the 26 mm coin:
  thumb opposition now solves the actual pad triangle to a 24 mm contact allowance.
- An approaching visitor interrupts work through the existing analytic shared
  attention transition. The enchantress presents her right palm upward. The marker
  `ActivityOfferingPalm` (also `TownServiceActivityRig.OfferingPalm`) follows the
  actual anatomical contact after IK: `up` is the palm normal; `forward` points
  toward the fingers. Use this marker for card handoff, not a fixed counter point.
  Its nominal station target is (-0.18, 1.14, 0.23) metres, selected against actual
  arm reach including the existing +/-3.5 cm grounding stress.
- Native candle glow artwork forms a larger bounded spell volume (one core and
  24 orbit sprites), anchored above the actual palm normal. Its clock comes from
  the shared occupation, so pausing work also pauses the spell. The native material
  still receives the station fade multiplied by the authored spell envelope.

No game state or wire schema changes. Existing authority-authored occupation clocks,
attention transitions, correlated face playback and authority handover remain in
charge. Handover interpolates evaluated hand/contact/prop poses, not distant clocks.
The priestess's prayer behavior is retained.

## Furniture contract

The worktop remains at 0.955 m. Reserve merchant station X +0.06..+0.34,
Z +0.28..+0.42 for counting. The centered native ledger must be moved outside that
region by the new decoration layout. Coin bottom seats are 0.970 m and increase by
3 mm per stacked coin. These are actual native meshes/materials, not UI samples.

## Evidence and limits

Source tests cover phase continuity, unchanged replicated timing, interruption,
coin attachment, loop continuity, palm direction and reach. The actual build-545
Linux diagnostic bundle is used provisionally to exercise imported bones and render
the authored contact poses. The final build-547 bundle must be checked again after
face/furniture integration; the old bundle does not prove the new asset picture.

`scripts/check-town-activity.py --render <folder> --sequence --bundle <linux-bundle>`
renders the actual CPU-skinned merchant/enchantress sequence at 8 fps. Diagnostic
coins are labeled contact cylinders; production uses the native decoration coin.
The pre-integration visual review exposed wrist/cuff gaps in the old bundle and
reported them to the face/mesh lane. Close stereo quality and headset timing still
require hardware review.

Pre-integration checks: strict Release zero warnings/errors; actual-bundle activity
157,544 assertions with 14 compiled negative controls, portable activity 58,297 with
six negatives, and native decoration 61 with seven negatives. The gaze suite adds a
moving-work-target regression check; final integration should rerun against new assets.

## Cold catalogue transport audit

The final catalogue can contain 161 stock and 512 owned entries, each with a
native face, physical body and original quantity/price row. The previous 2,048
module ceiling could omit overhead modules, and the original codec accidentally
serialized manifest counts as one byte. The ceiling is now 4,096; body version 2
writes a ushort count and still reads version 1. Stream 65,534 is reserved for
bounded bundles; 65,535 remains the manifest.

The previous queue alternated modules after each fragment and transmitted every
repeated manifest ahead of stock. Large snapshots could expire unfinished, while
held cards waited behind the entire catalogue. The queue now finishes an active
snapshot, coalesces unchanged manifests, and gives moving modules two turns per
background turn. One small module supplies session liveness. Closing manifests
retain the short retry cadence. Completion schedules the next unchanged refresh
and baseline, so unsent original snapshots do not accumulate replacement baselines.

Cold ordinary modules are compressed together, retaining their complete original
packets byte for byte. A bundle contains at most 32 packets and 60,000 bytes and
is strictly length/session validated before delivery. Additive record 84 carries the version-3 bundle body using ordinary length-prefixed
TLV chunks; regular single-module records remain unchanged. Record 78 never
becomes an escape for an unframed payload. No template defaults, text or native properties
are inferred. Held modules remain independent and can interrupt background work.
The existing 864-byte/50-ms global budget is unchanged. Town-only Optimal Deflate
reduces a 49,852-byte test bundle to 1,996 bytes versus 2,885 with Fastest. Replacing
the old bitwise CRC with its identical 256-entry lookup cuts measured desktop
compression from roughly 1.8ms to 0.33ms; an independent bitwise checksum verifies
wire compatibility. These numbers are desktop harness timings, not headset FPS.

The reproducible synthetic inventory has 16 native-like nodes per ordinary module
and eight 128-node modules, with material/text properties. At an 18fps sender:

| Inventory / load | Cold completion | Town bytes including warm follow-up | Warm held maximum delay |
| --- | ---: | ---: | ---: |
| 161 stock +30 owned, 64 overhead, idle map | 6.94s | 110,656 | 0.056s |
| 161 stock +512 owned, 64 overhead, idle map | 21.67s | 334,612 | 0.222s |
| Same maximum, six continuously saturated other streams | 369.94s | 637,630 | 2.167s |

The last case deliberately maintains six independent maximum-rate noisy streams;
it demonstrates progress under the shared cap, not an acceptable hardware promise.
Without batching the maximum idle catalogue took 124.28s, and saturation took
1,399s. Actual native hierarchy sizes and simultaneously active streams determine
hardware latency. No extra ordinary-level log stream was introduced. Native
sampling still observes every update; the unpublished frame probe reuses arrays
without mutating queued output. Further sampler CPU work must preserve animation
and hierarchy changes rather than merely skip unchanged-looking modules.

Validation: strict Release zero warnings/errors; complete Unity mirror render and
negative-control suite; wire vectors require the actual large manifest, every
module, exact bundled bytes, parser truncation/overflow rejection, cold-load
bounds, moving-card baseline expansion and steady-state delivery. The integration
branch's new publisher extraction still requires its own combined guard run.

An in-flight cold bundle does not own a grabbed card's dependency exclusively: its
exact immutable initial baseline is copied to the urgent lane before the pose
delta. Duplicate sequence reception remains idempotent. A focused regression
requires both to arrive before the background bundle completes.

## Final authored asset verification

Re-ran the actual Unity activity and face suites against the newly generated
Linux bundle, SHA256 `9ae804bf3d9e119b648003424b189f0252f7b8653f53c48163e7a53796c14ebb`
(103,860,264 bytes), using integration runtime sources. Activity: 157,544 runtime
assertions and 14 effective negative controls. Face: 11,941 assertions and 17
negative controls. Rendered 174 frames per merchant/enchantress occupation at 8fps,
including actual new NPC rigs, face meshes and counter furniture. Selected lift,
contact, release, palms-up and recovery images were visually inspected. The held
merchant pinch is 24mm across the 26mm coin, consistent through the contact phase.

The fixture's coin is explicitly a diagnostic contact cylinder; it does not load
the game's asynchronous Addressables dressing or candle-derived spell artwork.
Those appearances, full station lighting and headset animation quality therefore
remain hardware checks. The final runtime dressing positions place the merchant
ledger at X=-.28/Z=.16, outside the positive-X coin work region; the prayer bowl
and side lamps likewise sit outside the authored hand trajectories. The dark
rings seen around cloth cuffs were reviewed with the face author: the new skin
has a continuous 70mm forearm insertion and sealed hidden cap, so these are sleeve
interiors rather than missing wrist geometry. Hardware contrast may still affect
how clearly that continuity reads.

## Delayed-census race review

Independent urgent modules can overtake an older fragmented manifest. Previously,
an older census (or old-session close) pruned a newer already received module
baseline, and fragment-level pruning could discard a newer partial assembly.
That left current pose deltas without their original dependency until refresh.
Pruning now respects the presentation sequence: newer pending frames/baselines
remain buffered, while existing session membership still gates their display.
Fragment lanes cannot inspect a partial snapshot's presentation sequence, so they
expire independently after bounded inactivity instead of being removed by an older
census. The pool remains capped; a once-per-second sweep reclaims inactive lanes.

Adversarial regressions send baseline101 before census100, reopened-session121
before old close120, and the first fragment of a new module before an excluding
manifest. They require preservation and eventual complete delivery; a later close
still prunes old complete data, and idle fragment lanes are reclaimed. Targeted
Unity lifecycle checks and wire vectors cover the actual production paths.

The follow-up review also separated session liveness (`LastSeenTime`) from the
manifest's age anchor (`ReceivedTime`). Ordinary urgent packets refresh liveness
without rewinding `SessionAge + now - ReceivedTime`, preventing repeated opening
age/fade when censuses are less frequent. Population and attention stale checks
use the independent liveness value. A regression holds the manifest anchor 20s
old while delivering a current module: the visitor remains active and its age
continues unchanged.

The no-bundle activity gate now checks thumb articulation throughout actual grasp
intervals. Its previous sole phase-8 check had zero merchant curl, allowing the
excessive-thumb negative control to escape despite actual-prefab tests detecting
it. Default validation now passes 58,481 assertions and 13 effective negatives.
The final lower-tier furniture bundle, SHA256
`6e62641e2853e3b87a7e5179d0b75953baa8a85a48b948a6b963ef05bc729fbf`,
also passed the positive 157,544-contact suite and was rendered/inspected again.
