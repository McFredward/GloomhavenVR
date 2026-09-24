# Resident occupation revision — build 551

The eight hardware images under `debug/npc_probleme/` and the accompanying
ModBuild 550 banner supersede the previous naturalness assumptions. In particular,
`144610`, `145526` and `145539` show the priestess's attentive palms on the counter
with elevated elbows and strongly bent wrists. `145117` shows the enchantress's
raised elbows and hanging hands. Passing contact markers and continuity bounds did
not establish that these silhouettes were suitable.

## Source findings and behavior

The attentive pose explicitly moved both priestess palms to 0.959 m and rotated
her prayer frame toward a flat counter frame. That was the reported bracing, not
an accidental animation import. Keep her palms joined, lower them gently to the
sternum when acknowledging a visitor, and retain the same anatomical palm and
thumb frame throughout. The elbow approach stays forward of the robe. The
merchant's free hand now rests loosely near his waist rather than pressing into
the counter; the actual coin-hand contact remains unchanged.

The former mage performance repeated two broad gestures every 12.7 seconds.
Compressing its hand path in build 550 retained its unsuitable elbow and wrist
silhouette. Replace that trajectory with one measured experiment: turn the
supporting palm upward, lift it below shoulder height, shape the spell with the
opposite hand, observe, then recover. Both elbows remain below the shoulders.
Relaxed fingers replace the fully spread attentive free hand. Existing recorded
whole-body movement is blended gently with the same reach/recovery envelope;
there is no independent arm timer or added knee oscillator.

A deterministic counter hash varies each shared-clock block's start, duration
and intensity. Mage experiments occupy 8–11 seconds in a 48-second block and begin
after 12–22 seconds of quiet reading. Merchant pauses vary after complete coin
return, never midway through an airborne transfer. The priestess breathes more
slowly and occasionally lowers her joined hands during prayer. These variations
are pure functions of the existing replicated work clock, with smooth zero-speed
hand boundaries. No client-local random state, new wire record, paid generation,
actor mesh or asset-bundle change is required. Existing authority reconciliation,
attention reversals, native base restoration and planted-foot correction remain.

## Verification and limits

The imported-skin harness now covers long occupation blocks, not only the former
12.7-second mage cycle. It checks separated activity intervals, varying strengths,
low casting elbows, actual upward casting palms, no attentive counter bracing,
contact, interrupted visits, remote shared clocks and joint continuity. Original
contact/skin tolerances remain. One old negative control disabled a level-palm
reference that the new low-elbow geometry no longer needed; its replacement
actually suppresses casting pronation and must fail the actual palm-normal check.

Final counts and geometry evidence are recorded after completion below. Rendered
review uses the actual imported 550 actors and runtime IK, including the fully
attentive state. The diagnostic scene cannot establish headset naturalness or
lighting in the complete game map. No hardware outcome is claimed.

- Final imported positive: **649,733 assertions**. All **24 mutation controls**
  are covered by the complete run `/tmp/town551-motion-full/run-psezyjxj` plus
  the corrected pronation control and final positive repeat at
  `/tmp/town551-pronation-negative-v2/run-9uvmhzqq`. The latter catches an actual
  casting palm with upward dot 0.588, below the unchanged 0.85 gate.
- Final actual-skin triangle evidence: **4,423 poses, zero arm/torso or opposite-arm
  intersections**, with all six penetration/detached-seam controls detected.
  Merchant/enchantress use `/tmp/town551-anatomy-v3`, 1,602 poses each; priestess
  uses `/tmp/town551-anatomy-v4`, 1,219 poses. Reports are
  `/tmp/town551-{merchant,mage}-mesh-v3.json` and
  `/tmp/town551-priest-mesh-v4.json`. Maximum seam growth is respectively
  0.123 / 0.401 / 4.307 mm, below the unchanged 12 mm limit. The first priestess
  attempt exposed three robe/forearm triangles in a lowered pose; moving the
  anatomical elbow approach forward removed them without changing the mesh.
- At 90 Hz, final maximum knee movement per frame is 0.652 mm merchant,
  0.143 mm priestess, and 0.249 mm enchantress. Maximum hand rotation is
  2.956 / 0.396 / 2.960 degrees respectively, including complete greetings and
  reversals. Imported sole drift remains below 0.001 mm.
- Actual rendered review includes `/tmp/town551-priest-renders-v4` and the full
  48-second mage sequence `/tmp/town551-mage-sequence` (8 fps). Individual reading,
  reach, peak and recovery frames were inspected; the encoded convenience video
  is `/tmp/town551-enchantress-motion.mp4`. This is diagnostic actual-skin output,
  not a hardware-performance measurement or a claim that the whole video was watched.
- The portable phase/network run passes 165,876 assertions and eight controls;
  `/tmp/town551-portable-full/run-rwfqdxkq` precedes the added strength-variation
  control. Final portable coverage is rerun during integration. Strict Release
  build passed with zero warnings/errors (`/tmp/town551-motion-build.log`).
