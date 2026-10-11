# NPC668: enhancement list nearer the enchantress

The maintainer requests the existing enhancement-options list farther toward the
enchantress, locally and for observers, so reading the held card does not require
stepping back to see the list. Move only ritual section10 along station +Z;
retain its size, height, upright orientation, native rows, scrolling and input.

The worker starts from dev `9b0a4f0f579c16af301e579b3d4440be1d888791`
(Build667) in its separately initialized `worker/npc668-layout` worktree.

## Placement and clearance

The station authoring source puts the resident at Z=.65 and the visitor anchor at
Z=-.42. Ritual origin is `(0,.978,-.08)`, so changing section10's relative Z from
`-.16` to `+.07` advances it **23 cm toward the resident**. Its station center
becomes `(.32,1.308,-.01)`.

`TownServiceSurface` fits the original widget uniformly inside the unchanged
`.60 × .55` maximum rectangle. The existing placement fixture conservatively
models a 3 mm panel envelope: X `[.02,.62]`, Y `[1.033,1.583]`, Z
`[-.0115,-.0085]`. A native widget with a different aspect ratio is smaller along
one of these axes; the placement change never compresses or rearranges its rows.

The root's ignored `native-prop-bounds.py` reads original mesh AABBs and every
copied local transform from the read-only PCG bundles. It matches
`TownServiceDecor.Copy`/`Build`, including selected lantern subtree, flame/glow
exclusion during size fitting, height normalization for the lantern, maximum
dimension normalization for other ornaments and centering around each seat.
The worker independently reviews this computation and its normalized outputs.

| Original ornament | Normalized size, metres | Relevant clearance |
| --- | --- | --- |
| Right near `AlchemyLab.Clutter.Shelf.Individual#3` | `.090608 × .13 × .090608` | Seat Z=.06 minus half-depth `.045303833` gives front Z=`.014696167`; **23.196 mm** beyond the panel's rear envelope. |
| Right rear `AlchemyLab.Clutter.Shelf.Individual#11` | `.085224 × .15 × .085223` | Seat Z=.26 gives front Z=`.217388296`, farther behind the list. |
| Selected `CR_INT_Lantern_01_b` | `.118747 × .32 × .117539` | Actual supported seat Z=.16 gives front Z=`.101230593`, leaving 109.731 mm. |

The near jug overlaps the panel's X and lower Y ranges, making its measured front
the real tabletop bottleneck. A 20 mm engineering clearance would allow relative
Z=.073196; round inward to `.07` rather than touching that limit. This is measured
AABB nonintersection, not a pixel or headset appearance claim.

The integrator separately reads the shipped `prebuilt/ghvr-town.bundle`
workbench's 18 mesh AABBs with their transforms: none intersects the proposed
maximum list envelope. Applying the unchanged production `FurnitureSupport`
selection rules chooses `enchantress_Brass` for both lanterns and seats them at
`(.53,.96271212,.16)` and `(-.52,.96271212,.42)`. Thus the support clamp does not
bring the right lantern forward into the list. Root receipts are retained in
ignored `npc668/workbench-clearance.{py,json}`. The centered book at Z=.22 is
fitted to a maximum dimension of .32, giving a conservative front limit Z=.06;
this is behind the list even without relying on the book's small actual height.

## Local and remote behavior

The owner converts and places the unchanged native enhancement-shop widget using
this ritual coordinate. Observers receive its already authored surface transform
through the existing town original presentation; they do not recompute placement
from their camera or resident position. No network record, configuration key,
observer adjustment or additional motion is introduced.

All other ritual coordinates, card highlights, merchant and enchantress card
hover/ring effects, and return flights remain unchanged. The two existing layout
negative-control literals are rebound to the new coordinate. Their defects,
assertions and original clearance thresholds remain unchanged; no new test
infrastructure is required for this bounded placement edit.

## Focused worker validation

- Existing `town-ritual-layout`: **253 production runtime assertions and all 14
  genuine compiled negative controls**, 29.4 s. Original thresholds and control
  semantics remain unchanged. Receipt:
  `.planning/debug/npc668-layout/focused/results.json`; detailed native census,
  source hashes, compiled variants and Unity output:
  `.planning/debug/town-ritual-layout/run-pmmy0_42/`.
- Complete source group: **16/16**, 19.5 s; receipt:
  `.planning/debug/npc668-layout/source/results.json`.
- Strict Debug build: **0 errors, 0 warnings**; retained log:
  `.planning/debug/npc668-layout/strict-debug.log`.
- `git diff --check` passes. The diagnostic offered-card layout PNG is visually
  inspected; it renders maximum placement boxes, not the actual native artwork.

This is a bounded, focused repair, not a new complete local-gate pass. The
integrator records its independent network/build/wire checks. These checks
establish the scoped source and Unity geometry contracts; actual headset
visibility and comfort require the next hardware observation.

## Integration review

The integrator cherry-picks worker `838333294` onto Build667, reserves ModBuild668
and reviews the complete diff: one production placement coordinate, its explanatory
comment, two unchanged negative controls rebound to that coordinate, build notes
and developer evidence. The fixture Program.cs is byte-identical to Build667.
The shared publication still captures `surface.Panel.Target` as `enchant.inventory`;
the owner has already applied the ritual transform before remote capture. Its
publisher/codec/playback source is unchanged, with no second observer offset.

Fresh integration checks: source16/16, strict Debug and Release zero errors and
warnings, direct golden vectors300510, bilingual documentation and shipped bundle
format checks. The existing full `town-service-mirror` focused suite passes on the
integrated source (115.7 s), including its original compiled negative controls;
receipt: `.planning/debug/npc668/mirror/results.json`, detailed run `run-ltqtn5w0`.
This direct golden run
does not invoke the complete `wire-tests.sh`/215-suite umbrella. Unchanged Build667
coverage and surfaces666/235/4795 remain inherited, not freshly re-established.

Ignored integration evidence is copied to the main checkout's
`.planning/debug/npc668/`: native prop/workbench read-only probes and receipts,
worker layout/source receipts, the complete archived layout runtime, integration
checks and SHA256 manifest. Original game bundles and shipped town geometry are
read only throughout. Hardware comfort and stereoscopic appearance remain
unverified.
