# Town arm retargeting and skin continuity — build 549

The build-548 hardware report and all eleven September 24 screenshots were reviewed.
`082029` exposes the enchantress's disconnected-looking wrist/sleeve transitions;
`082136` and `082247` show related merchant transitions. The accompanying log identifies
ModBuild 548, commit `718b191c6`; this is not an older installed bundle.

## Source and asset findings

The previous arm solver targeted positions but assigned the palm a fixed station-space
orientation. Its half-turn used the station forward axis, whereas upper/forearm roll used
each limb's own axis. Those axes disagree when the arm bends. There were no wrist-angle
limits or arm/torso skin intersection checks. The generated motion retained body rotations
and elbow/hand guides, not a complete anatomical arm rotation chain; changing the generated
clip could not repair the downstream overwrite.

Direct FBX sampling in the 12–28 mm proximal wrist interval found merchant cuff weights
averaging 32% Hand / 68% Forearm and enchantress cuff weights 25% / 75%. Their overlapping
replacement skin averaged approximately 4% / 96%. These surfaces consequently separated
under pronation. The previous activity assertions covered markers, contact and continuity,
not the complete deformed skin.

## Revision

- Retarget the palm frame from the real forearm direction with a bounded wrist bend.
  Iterative contact correction includes the changed palm/finger offset. The iteration
  exits when its orientation correction is below the contact tolerance.
- Distribute pronation over three longitudinal forearm supports. Keep the proximal elbow
  free of a full half-turn, and give cuff and hidden anatomical skin one distal transition.
- Keep elbow guidance during attention transitions and route contacts around the actual
  wider merchant coat. The generated human reference is not a body-collision model.
- Extend the merchant's actual right palm when attending to a visitor. The existing
  `ActivityOfferingPalm` transform and `OfferingPalm` property now exist for services 1
  and 3. Position is the actual `PalmContact.R` surface after IK; `up` is its outward normal
  and `forward` its finger direction. Consumers must use this transform, not a fixed pose.
- Preserve shared work clocks and interruption semantics. A held counting coin remains
  attached to the actual pinch; there is no inference or network service at runtime.

No paid API was called. The original four Kimodo clips remain the motion source.
The enchantress's large gesture and fast final recovery are continuously retimed, producing a 12.7-second
loop with matching source velocity at both ends. Her lowered hands clear the actual robe.
The merchant's right resting palm is `(-.32, .959, .35)` and his offered palm is approximately
`(-.20, 1.18, .20)`; the final actual palm transform remains authoritative. Working coins
rest at `(.24, .970, .38 - .026 * index)` and stack at `(.24, .970 + .003 * index, .29)`.
The priestess's palms are raised to chest height with relaxed, aligned fingers. The shared
attention transition remains 0.65 seconds, with unchanged network layout and validation.

The table contact frame no longer disappears as pronation crosses 25 degrees. That old
coupling produced a rapid flexion change even with continuous hand targets. Lateral-axis
transport avoids the upright-forearm singularity of a projected world-down normal.

## Authoring and integration

`author-town-wrist-weights.py` takes the approved build-548 `rig-source.blend` for each NPC.
It changes arm weights and adds six forearm support bones per actor. All other vertex
weights are explicitly compared unchanged; topology, UVs, facial expressions, optical
geometry, portraits and lower-body authoring are retained. The four-influence export
limit is checked and weight reduction is reported per mesh.

The editable final kit is
`/home/claw/worktrees/town549-motion/.planning/debug/town549-motion/rigged`.
Each NPC directory includes its FBX, `rig-source.blend`, `face_albedo.png`,
`facial-rig.json`, and measured `wrist-weights.json`. Reauthor only from the approved
pre-twist source; the helper explicitly rejects applying the operation twice.

The final authoring directory must include each NPC's FBX, `face_albedo.png` and
`facial-rig.json`. Import it with `TownServicesBuilder.RefreshFacialRig`, then run
`RefreshFixedDetail`. Replacing only FBX files leaves old serialized bone/eye/contact
bindings in prefabs and is not a valid import. Root integration owns the final prefabs,
furniture refresh, Windows bundle and package.

## Validation status

- Strict Release build: zero warnings/errors.
- Actual imported activity suite: 268,749 assertions and 17 mutation controls, including
  transformed palm/coin contacts, planted feet, pause/reversal/recovery, wrist flexion,
  longitudinal twist supports, and paired remote timelines.
- Final imported actor asset gate: 2,044 assertions and nine visual negative controls.
  Evidence: `/tmp/town549-motion-assets/final-evidence/result.txt`.
- `ArmGeometry` exports actual imported and deformed arm/torso surfaces through complete
  cycles, greetings/departures started at four phases, and a visit aborted and restarted
  before its transition completes. Export coordinates account for FBX import scale.
- `check-town-arm-mesh.py` checks exact triangles for arm/torso and opposite-arm
  intersections, plus matched cuff/skin seam growth. It injects actual skin penetrations
  and a separated wrist as negative controls. This is not a marker or capsule proxy.
- Final exact-skin gate: zero arm/torso or opposite-arm intersections across 794 merchant,
  546 priestess and 603 enchantress poses (1,943 total). All six injected body-penetration
  and detached-wrist controls were detected. Maximum cuff/skin separation growth was
  0.124 mm, 2.487 mm and 0.636 mm respectively. Reports:
  `/tmp/town549-arm-final-proof.json` and `/tmp/town549-arm-final-fast.json`.
  Conservative bounding-box/tree-reuse optimization produced identical complete reports.
- Final matched source runtime: `/tmp/town549-motion-final-verified/run-8orpjnhf`.
  A new wrapped-support mutation produces the intermediate-bone jump that a hand-only
  velocity check misses. Signed pronation now retains its authored turn count.
- Final desktop render review: `/tmp/town549-renders-final-proof`, showing actual imported
  actors through work and offered-hand states. Body/head/face/optical/leg geometry remains
  the approved source; the fixture still contains the previous furniture, which root
  integration replaces separately. Hand and support velocity checks run at 90 Hz;
  triangle/seam checks sample complete work cycles at 12 Hz and visits/reversals at 30 Hz.
  The full-skin dataset uses nominal ground height; independent contact/foot tests also
  vary resolved terrain height by +/-35 mm.

Automated and desktop-render evidence cannot establish headset lighting or perceived
naturalness. Hardware review should inspect wrist joins from both sides, coin pickup and
release, both offering palms, and approach/leave/reapproach during every occupation.
