# Build 621: original small scenario dressing

Build 620's decoration budget still left numerous floor skulls, loose paper/parchment,
wood debris and small shelf contents visible at 0%. The supplied `viel_deko1.jpg`
and `viel_deko2.jpg` show the remaining categories. This change expands the original
asset proof across the game's PCG bundles; it does not identify an individual runtime
instance from an aggregate renderer count.

## Hardware evidence and cause

The supplied ModBuild 620 `LogOutput.log` and `Player.log` have no GloomhavenVR Debug
lines. Both zero-density windows report 4,530 inspected mesh renderers and 1,106
owned hidden scatter meshes. Those totals confirm the budget ran, but cannot show
which photographed leaf failed classification. Seven original cult-chain material
errors are separate native failures and are not claimed fixed here.

Read-only inspection of the original game assets establishes three concrete gaps:

* `TO_INT_Cathedral_Clutter_Pages_03_PR/TO_INT_Floor_Clutter_Pages_03` owns a native
  box and the game's material/detail/shadow callbacks. Build 620 accepted reversible
  shared collider ownership only for `FR_Default_Bay_*`, so this pure page scatter
  remained visible to avoid leaving an invisible laser blocker.
* `TO_INT_Shelf_Clutter_Default_01_PR/CR_ST_Shelf_Books_01` owns its own box. Some
  original shelf jugs also carry an Animator whose controller pointer is zero.
  Treating every Animator as a figure kept those inert presentation meshes visible.
* Small original meshes are often separate children of a structural floor, shelf or
  torture-board wrapper, sometimes labelled merely `Mesh`. The wrapper's structural
  name is not permission to hide it, but is also not evidence that its detached
  paper, skull, cup or chain must remain visible.

## Closed original asset proof

`audit-scenario-detail-assets.py --scenery-input` verifies all 2,144 stored original
PCG bundle SHA-256 hashes before reusing the complete census of 47,754 mesh-renderer
records, 137 projectors and 9,438 names. It rereads the actual mesh bounds and
component ancestry in 75 candidate bundles. The resulting 276 exact small mesh
identities and 916 native container identities are pinned in
`NativeDetailProvenance.json` and must match the production tables. Representative
rows cover 41 original bundles. The existing 64 detached-composite dressing and 166
retained ground-core identities remain unchanged.

The offline candidate tokens are a review aid, not a runtime name-based hiding rule.
Runtime admission requires a closed original mesh identity plus real ancestry,
component, geometry and collision proof. An exact small identity overrides structural
inheritance only for its own leaf. It cannot hide the enclosing floor, shelf or bay.
Fifty-four exact original furniture/table/chair/bench/shelf cores explicitly remain
structural and can represent their original shared collision while separate small
contents are removed.

The catalogue excludes altars, statues, corpses, crosses, skeleton bodies, coffins,
large furniture cores and prominent standing urns. It additionally rejects three
large library-book-pile variants, four 2.27–2.45-unit-high cave-scatter body/LOD
variants and `EN_CR_Vase_01` (1.425 units high). Accepted page clusters can span
2.35 units horizontally but are only 0.073 units high; loose flat dressing spans up
to 2.64 units. The retained small shelf-jug meshes are at most 0.278 units high.
Broken low urn fragments and detached chains are separate small dressing, not the
whole standing urn or torture-board/skeleton. Corpse/cross/skeleton/coffin ancestry
also protects separately named pieces of those large decorative units.

Every original actor/interactable marker and Animator with a live controller remains
protected through the scene root, including above the native tile boundary. The
global `FigureRendererGuard` is unchanged. Only an exact small original mesh can
distinguish a controller-less decorative Animator. Unknown/missing callbacks,
gameplay props/doors, rigid bodies, trigger collision, native lights, particles and
UI remain protected. Activating a controller after masking restores the mesh.

A decorative collider is owned only if its complete original subtree consists of
proven presentation components and optional meshes. It remains enabled while any
represented member is visible, and a foreign disable is never restored. Mixed bays
retain shared structural/gameplay collision; the real torture-board fixture keeps
its skeleton and box while removing only the detached chain. Pure small prefabs can
use the existing zero-density creation deferral, avoiding original visual callbacks,
meshes and colliders together and restoring the exact original prefab at full detail.

There are no new per-frame or normal-level diagnostic streams, no network changes,
and no changes to native materials, lights, renderer-enabled state or gameplay.

## Validation and limits

Worker evidence is under `.planning/debug/frame621-scenery/`:

* `audit-small-bound.log` and `enriched-census.json`: all original bundle hashes
  verified; final catalogue and actual native hierarchy/bounds recorded.
* `portable-final23.log`: 1,481 production-classifier assertions and 23 causal
  negative controls. The graph is portable CI evidence, not a Unity render proof.
* `runtime-final-resume.log`: Unity 2021.3.5f1 runs the complete production discovery
  driver with 918 assertions. Every new small identity is exercised beneath a
  structural carrier and as a standalone mesh owning a collider. Zero/full detail,
  deterministic 35% density, controller gain/removal, shared mixed-unit collision,
  native loading completion and exact small-page creation/restoration are covered.
* `runtime-global-guard.log`: the real scene-root figure protection has an explicit
  failing mutation. `runtime-final.log` retains the four other completed focused
  controls; only the actually failed bounded scope was resumed after correcting a
  fixture ancestor. Earlier complete controls remain recorded separately.
* `strict-build-final-resume.log`: strict Release build, zero warnings/errors.
* `source-final/results.json`: all 14 source suites passed.

Raw failures remain in the evidence. The first native fixture used an
AnimatorOverrideController without a base controller; Unity returned a null runtime
controller. It was replaced with a real editor-created AnimatorController asset.
A later ancestor control initially attached its actor to the unrelated driver object;
it now attaches to the actual native scenario ancestor. The collider mutation's
expected failure label was also narrowed to the newly earlier page-collision check.
These failed attempts are not counted as successful controls. The primary agent
runs the complete required integration gate on final dev, including all native
variants and wire vectors.

Native tests reproduce original identities, component hierarchies and actual Unity
physics/driver lifetime. Their geometric fixtures are deliberately simple and do not
establish headset pixels, exact photographic instance identity or an FPS gain. The
next hardware run must confirm that the photographed small clutter disappears at
0%, proportionally returns at a low setting, and restores at 100% while large
decorative units, floor/wall collision, actors and native lighting remain intact.
