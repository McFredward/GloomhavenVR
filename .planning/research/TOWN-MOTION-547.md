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
