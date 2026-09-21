# Build 544 — NPC hardware corrections and resident activities

## Hardware evidence

The maintainer supplied six screenshots under `.planning/debug/npc_probleme/` and
then current `LogOutput.log` / `Player.log`, both identifying ModBuild 543. The remote
folder still identifies build 500 and is not evidence about this NPC test. File hashes
are recorded privately in `.planning/debug/town544-hardware-sources.json`.

All six screenshots were inspected. They show black eye apertures on all three NPCs,
a longer/narrower merchant face, a polygonal grey rear-scalp patch, open or ragged
collar/hood joins during head motion, and priestess clothing covering the lower face.
No Error/Fatal entries occur in the supplied mod log. The separate `Player.log`
contains one stackless `NullReferenceException` during application teardown, after
`Bootstrap.OnDestroy` / UI-navigation deselection (line 9697). Its owner is not
established; it is not treated as proof of an NPC failure. Clean mod error counters
do not establish visual correctness.

## Source-proven defects

- The shipped Windows eye/cornea fragment programs do not include their practical
  point-light loop. `VERTEXLIGHT_ON` is a vertex-stage keyword in the compiled D3D
  forward-base variants. Skin already evaluates its practical lights in the vertex
  stage. The isolated OpenGL fixture did not reveal this platform-specific omission.
  Carrying light selection as an interpolator retains the native four-light bindings
  in the Windows fragment programs, without an emissive floor or additional lights.
  See [eye evidence](TOWN-544-EYES.md).
- The anatomical merchant chin was fitted to the reference beard tip, elongating the
  jaw rather than retaining a separate beard silhouette. Rear-head projection sampled
  neutral image background instead of scalp. Costume cut surfaces were not sufficient
  for the newly introduced moving head. These are asset-authoring defects, not privacy
  or lighting policies.
- Combining work poses with head motion exposed further costume deformation. Abrupt
  per-vertex support changes stretched cape and sleeve triangles into ribbons; connected
  garment weights now distribute support while preserving hands and digits. Original
  cloth edges retain their own UVs, and lower neck rings continue underneath the blouse.
  These changes preserve the original clasps, chains and costume instead of covering the
  defects with an extra collar. Review captures bake the current skinned mesh per pose;
  repeated editor camera renders alone had retained stale GPU skinning.
- The native UI contains two different textures with identical name, dimensions,
  format and mip count (`Black_Backdrop`). Their decoded pixels differ. Descriptor-only
  lookup therefore rejects temple/confirmation art after merchant art is registered.
  The logs show the caught preload warning; they do not prove a failed multiplayer
  confirmation in this session. Original-template provenance must distinguish them.

## Resident activities

The merchant counts a held native coin and writes in an original open ledger; the
priestess prays; the enchantress studies an original open book and performs a small
hand-casting gesture. The latter reuses the original candle glow as an effect. The
standalone reed pen is authored geometry because no standalone writing tool was found
in the supplied PCG catalogs; it is not a replacement for existing game decoration.

Arm/hand contacts are solved against each actual rig. A nearby player or current
visitor causes a smooth interruption, hand settling and head/eye attention. Work clocks
slow and stop through the transition and resume without restarting the loop. The same
resident authority publishes intermediate activity state to observers. Native service
selection, confirmations and transaction continuation remain owned by the game.

Transport is additive TLV81 / message22, version3 unchanged. An active record is 54
bytes including its TLV header. Message22 combines the unchanged face80 record and
activity81 atomically in 156 bytes, replacing the separate face21 transmission for new
builds. At 15 Hz this is 2340 raw bytes/second for all three residents (810 more than
the prior face stream). Existing records79/80 and the message21 reader remain byte-identical. Worst-case presence is 7136 bytes against the 7168-byte cap;
only 32 bytes remain, so future additions require another explicit budget review.

## Validation status

Runtime integration passes all 14 source suites, all 51 local runtime suites and
260310 wire assertions. Strict Release has zero warnings/errors; bilingual player
documentation checks pass. The compiled guard reports the expected old-baseline
differences (106 changed and 126 added/removed types, no order-only moves), with no
removed public config/log/patch surfaces. Final asset review and packaging remain in
progress; this is not hardware acceptance or completed shipping validation.

The first contact render caught reversed anatomical left/right targets, unreachable
writing contact and incorrect palm orientation despite passing mathematical IK tests.
The first head orbit caught stretched collar UVs and an incomplete lower neck. These
findings are why actual renders and final packaged assets must be reviewed, not just
source assertions.

Subsequent thumb calibration and the source-only CI fixture were checked separately:
the activity suite passes 92107 assertions and nine compiled negative controls against
the prior real bundle, and the portable phase/network suite passes 24031 assertions
and five negative controls without game assemblies. These establish runtime behaviour;
they do not replace the pending validation against the final build-544 assets.

A separate returning-author review found that a network partition could leave the
old and temporary NPC authorities at different work phases. Recovery now blends
the displayed contact positions, chest/wrist pose, casting intensity and face angles
over 0.35 seconds. It never interpolates distant clocks through whole work loops.
An interrupted recovery starts from its current intermediate pose. Focused tests
include an immediate-author-snap negative control; no wire bytes or gameplay gates
were added for this presentation reconciliation.

## Animation services

[Service evaluation](TOWN-544-ANIMATION-SERVICES.md) covers FAL Hunyuan Motion, FAL Meshy
animation presets and Meshy's direct Text to Motion API. No paid generation was used
for this round; generated body motion remains a possible future input to the existing
rig/contact system. No generated voices are introduced.

## Hardware acceptance still required

- Install the matching DLL and complete asset package on every VR peer. Check all
  three residents from the front, side and above, including the head-turn limits.
  Eyes must retain their lit iris/sclera and neck/costume seams must stay closed.
- Watch each work loop from outside attention range, then approach and leave during
  writing, prayer and casting. Hands, tools, clothing and head motion must settle and
  resume smoothly. Compare both observers during a multiplayer visit.
- Reconnect a peer while a resident is working or attending a visitor; verify that
  a returning authority does not restart the loop or snap the displayed pose.
- Open and complete each native service flow, and toggle immersive services off/on.
  Cosmetic motion must never block native selection, confirmation or continuation.

These are validation requests, not reported headset outcomes. The working loops are
authored contact poses on the existing rigs, not newly generated motion-capture clips.
