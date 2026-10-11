# Build 543: articulated town faces and shared attention

Status: implementation in progress, based on dev `739b66e8` (build 542).

## Hardware evidence

The maintainer approves the overall new NPC appearance but reports eye texture landmarks
above the corresponding mesh relief. `gesicht1.jpg` shows a pronounced extra physical
crease below the merchant's painted eyes; `gesicht2.jpg` and `gesicht3.jpg` show the same
class of anatomy/texture mismatch. The head images are presentation references, not
proof that the mesh has eyelid or mouth topology. Build 542 explicitly had neither
separate eyes nor a facial rig.

The supplied local `LogOutput.log` and `Player.log` identify build 542. No Error-level
mod entries occur in either supplied file; this does not invalidate the visible asset
defect. The remote log still identifies build 500 and is excluded from current evidence.
The build-542 CI run `35612140232` completed successfully; automated gates did not test
anatomical eye/texture alignment.

Evidence SHA-256:

- LogOutput.log: `4fbbefd52eda908b27dbca3a397f20ca67928fa1f7fb84d24819ac54bf057ac8`
- Player.log: `3a0429c2e4fae90e0f3982e23791d0e795d9a3a20488faa1aadc0d237667413a`
- gesicht1.jpg: `2795f61fb4ed7f6e5b9dadeee8b585508d12856b784f132bd4a3f1511ce3b6e2`
- gesicht2.jpg: `0aac9e41561417f1d288dc4350ee538ee38e5643e9c3d083da468861c01db874`
- gesicht3.jpg: `1de5129d7dc04950e18d14a388ff648d5fd7b6e4170cea390393c7e0f260e9e0`

## Implementation contract

- Fit actual anatomy to reference landmarks, including open eye sockets, separate eyes,
  deformable eyelids and connected lip loops with an oral interior. Preserve the approved
  costumes, body proportions, floor contact and environment lighting.
- Evaluate gaze after existing body animation. Use physiological limits, smooth attention
  changes and stable visitor preference. A shared NPC looks at one actual participant;
  observers must never independently turn the same NPC toward themselves.
- One elected cosmetic author owns gaze and expression time. Shared time drives complete
  blinks and microexpressions; initial/recovery snapshots and bounded small facial updates
  must preserve the existing transport budget. The native transaction flow is untouched.
- Speech uses an explicit cue and clock, with matching mouth motion. No idle jaw flapping,
  repeated greetings on every snapshot, or gameplay progression waiting for audio.
- Inspect neutral, profile, blink-closed and speaking views in actual Unity. A passing
  geometry/runtime test is not a claim of realistic headset facial animation.

Independent workers use current-dev worktrees for facial assets, gaze/network runtime,
and voice-source research. Root integrates and reviews; only root pushes dev.

## Maintainer clarification: original speech only for this build

The maintainer subsequently chose existing game recordings and deferred AI-generated
voices to possible later work. Six test greetings had already been generated before
that answer arrived (listed estimate USD 0.028); this was disclosed immediately.
Their asset commit and bundle-loader addition were reverted. The test audio and
receipts remain private under `.planning/debug/town543-speech/`, outside the shipping
asset tree. No generated greeting, automatic synthetic playback or paid retry belongs
in this build.

The game contains narration. The completed audit found 551 named dialogue nodes for
these three NPCs in the five shipped ruleset archives, with no audio identifier and
no matching clip name in the 3,157-clip inventory. Their service effects are not voices.
See [TOWN-543-VOICE.md](TOWN-543-VOICE.md) for source hashes, native call paths and the
audit's coverage boundary. Playing unrelated narrator recordings from an NPC would
not satisfy this preference.
Eye/head motion, expressions and a physically speakable mouth rig remain in scope.
Without a verified applicable speech source, the resting mouth stays closed and the
speech adapter remains inactive. Do not claim audible NPC speech is implemented merely
because the rig can articulate.

## Integrated runtime checks

Runtime integration: `ebcbfb3a` and `99cb863f`, with the optional adapter already in
`2e6988dc`. The independent review in [TOWN-543-REVIEW.md](TOWN-543-REVIEW.md)
closed authoritative-clock divergence, repeated unsuccessful attention searches,
retired-epoch rollback and legitimate recovery after a temporary network stall.

The complete local guard passes 14 source suites, 50 runtime suites and 258,091
wire assertions. Runtime suites took 421.5 seconds with eight workers. The real
Unity face suite passes 2,076 assertions and 16 compiled negative controls.
Strict Release has zero warnings/errors; bilingual documentation and whitespace
checks pass. Surface counts are 626 configuration keys, 174 tracked Harmony patch entries
and 4,746 log tokens, with no removals. Guard exit 1 is solely the expected compiled
difference from historical baseline `080c505e9`: 106 changed types, 115 added/removed,
no order-only moves. Evidence is in `.planning/debug/town543-final-guard.log` and
`town543-release-build.log`.

These results validate the integrated runtime source. Final model import, bundled
asset rendering and actual-prefab binding checks are tracked separately; successful
runtime fixtures do not approve the exported meshes or the headset picture.

## Import and visual review

The first Unity import exposed incorrect UV-channel removal and material-submesh
ordering. Blender RNA layer references became stale while deleting UV layers;
immutable names fix that operation. Unity's first-used material order also differs
from merely assigning Blender material slots, so the export keeps body polygons
first. A nested FBX instance must be unpacked before parenting the eye pivots to the
animated Head. These are authoring/import corrections, not changes to native UI.

Close-up review additionally found dark eyelid/nostril/mouth artifacts. A constant
clay material and explicit single-LOD render separated atlas-border sampling and
inconsistent blendshape normals from actual holes or duplicate LOD rendering.
The atlas is now baked against the subdivided surface. Facial base/deformation
normals use the same neutral weld map, with zero costume deltas. The remaining broad
eyelid band was traced to another stale Blender RNA handle: adding a color attribute
relocated the UV storage, so writes through the previous LidSkin handle did not reach
the intended layer. Reacquiring the layer by name fixes the actual stored UVs. The
authoring assertion checks those values, and the v8 closed-eye renders show skin
across the lids instead of the original photographic eye stripe.

An independent Unity 2021.3.5 shader probe rules out a suspected transparent-pass
lighting limitation: with pixelLightCount zero and a ForceVertex point light, both
the transparent and opaque diagnostic receive vertex-light data and produce the
same real highlight. The original diagnostic bundle's cornea also produces a
highlight over its globe, with a peak of 243/255. Private reproducible probe sources
and pictures are archived under `.planning/debug/town543-cornea-probe/` (the original
temporary project is `/tmp/town543-cornea-review/`). That bundle was deliberately
an older intermediate asset; this is a shader-path diagnosis, not final asset
acceptance. No painted catchlight or artificial emission is introduced.

Review of the fourth source render caught an additional deformation defect despite
passing bounds checks: a generic height-only Head/Neck weight transition crossed the
merchant's beard and the enchantress's lower jaw. Large gaze angles stretched these
features instead of preserving the mandible. The asset correction uses the anatomical
skin weights supplied with the same CC0 source topology. A trial collar cylinder also
produced visible texture bands through extrapolated UV coordinates and was rejected;
existing costume geometry must not be redesigned to hide an incorrect face fit.
The corrected weights are validated against both actual rendered poses and an
independent actual-prefab skull/jaw preservation check.

The v8 source review passes 468 asset assertions and six visual negative controls.
Root inspected neutral, closed-blink, small oral deformation and extreme-gaze views;
the previously stretched jaws and broad dark eyelid bands are corrected. Existing
irregular costume cut edges are retained and are not claimed fixed by this facial work.
Final bundled rendering and runtime binding are recorded separately below.

The unchanged environment was checked against the v8 imported triangle indices and
CPU-skinned pose exports: 16 poses (11 unique) per resident at 360 bearings, with the
actual forest foot height. Minimum distances to the original alpha-surviving canopy
samples are 91.608 mm for merchant, 94.662 mm for priestess and 103.061 mm for enchantress.
All exceed the unchanged 28.52 mm wind bound documented in
[TOWN-542-SETTING.md](TOWN-542-SETTING.md). This is a sampled geometry check, not a
headset visual guarantee. Input hashes, reproducible script and results are archived
in `.planning/debug/town543-canopy-*`; no environment geometry was changed.

## Final artifact acceptance

- Loaded Linux asset bundle: 469 assertions and six visual negative controls.
- Actual production rig on those final prefabs: 9862 assertions, three compiled
  negative controls and nine deliberately corrupted Head/Neck jaw-weight cases.
  All three residents retain three facial LODs and eleven channels per LOD.
  Maximum sampled skull/jaw rigidity residual is 0.001864 mm; eye-target angular
  error is at most 0.02798 degrees. This validates the bound samples, not visual realism.
- The first actual-prefab attempt exposed a fixture-only JsonUtility nested-array
  deserialization limitation. Explicit DataContract JSON handling fixes both metadata
  reading and evidence writing. The asset and production runtime were unchanged.
- Windows town bundle: 99,211,283 bytes; SHA256
  `6df700ed2374c7f0f8ab4652363bce54a70ab54e33ee67c6879a889f533a08e3`.
  Both bundles use UnityFS format 7 / Unity 2021.3.5f1; the main bundle is unchanged.
  Root compared all 119 final asset/build-input hashes to the reviewed worker inputs.
- Full runtime/wire guard results above remain applicable: subsequent production edits
  only clarified build-note comments. Final source gates pass 14/14, strict Release
  remains zero warnings/errors, and bilingual documentation and whitespace checks pass.
  Asset and actual-prefab checks cover the subsequent authoring/export changes.
- Final render evidence and packed Blender authoring sources are archived in the main
  checkout at `.planning/debug/npc-authoring543/`. Runtime binding evidence is recorded
  in [TOWN-543-GAZE.md](TOWN-543-GAZE.md); asset provenance, costs and limitations are
  in [TOWN-543-FACES.md](TOWN-543-FACES.md).

This is a development hardware candidate, not a release. It requires the matching
DLL and town bundle; use the full installation package. No audible NPC speech is claimed.

## Hardware acceptance after final asset validation

- Visit all three residents in each map environment. Inspect eyelid/brow alignment,
  open eyes, complete blinks and the skin/costume boundary at close range. Move to
  both sides and change viewing height; the jaw must preserve its shape and eyes
  must remain inside their sockets. Observe a return toward neutral when leaving.
- With another enabled VR participant, visit and leave the same resident. Compare
  which participant the NPC follows, the head direction and blink/expression timing.
  A shared NPC must not appear to look independently at every observer.
- Switch immersive town services off and back on, leave/re-enter the map and test
  a peer reconnect. Original service windows and transaction continuation remain
  authoritative; cosmetic facial state must never block them.
- Inspect the forest clearance and lighting, especially eyes at grazing angles and
  heads looking upward. Measure the real headset cost with all three residents visible.
- No new NPC speech is expected in this build. The oral rig is prepared and tested,
  but no verified original recording is available for these residents and no generated
  voice or silent speech animation is enabled.
