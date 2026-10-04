# Build620 whole-game scenery coverage

## Hardware evidence

The supplied Gaming PC logs identify Build619 in both sinks. The scenery slider
is connected: decoration-zero intervals report 436 and later 1,012 owned renderer
masks over 4,425 and 4,530 inspected meshes. This is evidence of incomplete visible
coverage, not evidence that the setting did nothing. No supplied screenshot names
or log rows prove every retained paper/skull object in this particular level.
The original prefab census independently demonstrates missed decoration families.

## Original assets and classification

A read-only UnityPy census covers all 2,144 PCG/material bundles, including all
129 nested procedural databases, 9,438 named identities, 47,754 authored mesh
renderer records and 137 projector records. The original 188-bundle/190-model
actor census remains unchanged. No original mesh, texture, bundle or material is
modified. The review and hashes are retained in `NativeDetailProvenance.json` and
the immutable debug evidence; developer reproduction is:

```sh
python3 scripts/audit-scenario-detail-assets.py --scenery-only --scenery-renderers \
  --output .planning/debug/scenery-review.json
```

The classification includes every original scenery name; it is an admission
candidate inventory, separate from runtime ownership and gameplay vetoes:

| Role | Named records |
| --- | ---: |
| Loose dressing candidates | 1,599 |
| Detached composite dressing, including authored instance labels | 69 |
| Grass candidates | 279 |
| Other vegetation candidates | 397 |
| Structural ground/masonry | 2,331 |
| Gameplay identities | 226 |
| Water/effect identities | 214 |
| Generation containers | 923 |
| Anonymous or otherwise unproven names, preserved | 3,400 |

The runtime now accepts loose skulls, bone piles, paper/pages/parchment, scrolls,
coins/cups, carpets/curtains, furniture/chairs, candelabras, chains and cobwebs,
alongside the existing book/pot/scatter/biome families. These names still require
actual scenario-generated ancestry and the complete component/collision checks.
The same skull/book mesh on a real interactable obstacle or reward remains visible.
An unknown name or shader never authorises hiding a structural core.

The missing composite case was a separately authored skull/bone/rubble renderer
inside a floor, wall or pillar. The inherited structural veto kept its ornament
alongside the necessary core. Sixty-four exact original mesh identities now bind
only the ornament renderer, including all authored skull LOD pieces, stone-floor
rubble pieces and separate terrain rubble bits. Floors, wall/pillar cores, terrain
beds, doors, colliders and native interaction remain. Whole-prefab creation is
still deferred only when every original component/mesh proves it is cosmetic;
a mixed floor-plus-skull prefab is not deferred as a unit.

The original generic `CR_OS_Floor_01_PR` through `_04_PR` hierarchies also have
a shared prefab box collider: an anonymous `_New` wrapper contains the solid
floor and a separate bones renderer. Recognising the ornament alone did not
prove that collision belongs to the retained floor. A closed census of 166
actual ground-core mesh identities now establishes that representation. Unknown
floor-like names, cosmetic leaf colliders and native gameplay ancestry still
veto hiding. This does not broaden structural or cosmetic name admission.

Blood/dirt paint uses another real render path: four exact original PCG projector
families account for 122 of the 137 authored projections. The decoration budget
owns only their render-enable changes, retaining original controllers, pose and
material. Magic circles, ice runes, toxic splats, unknown projectors and projections
beneath gameplay props remain. Foreign disabled projections remain disabled on
restoration. Discovery includes inactive room content before reveal; bounded
ancestry monitoring restores reparented/retired owned projections.
Runtime names accept exact Unity `(Clone)` and native numeric duplicate suffixes,
including combined duplicates/clones. Arbitrary suffixes such as `MagicCircle`
or `(MagicCircle)` remain unknown, even after a known family name.

## Verification and limits

The portable complete classifier passes 360 assertions and 18 causal negative
controls, including all 64 independently recorded original composite meshes,
all 166 original retained ground cores, unknown masonry, real floor collision
and gameplay identities. Actual Unity
2021.3.5 executes the complete discovery/ownership driver: 286 assertions cover
all exact mesh identities, zero/100 retuning, mixed floor retention, separate paper
and bone piles, original projector masks and foreign projector state. Six new
causal variants cover omitted composite admissions, accidental broad skull-core
admission, missing projector masks, foreign projector restoration, omitted
generic-floor collision representation and rejected native projector clones.

The first composite negative mutation reached a different earlier terrain assertion;
that raw failure remains preserved. Only that control was resumed using a complete
exact-catalog mutation, which fails the intended skull-layer assertion. Passing
unrelated variants were not repeated. The strict Release build has zero errors and
warnings. All fourteen source scopes pass through the complete attempt plus its
targeted inventory resume. That checker initially counted an opening bracket
inside a string literal; normalising native duplicate suffixes avoids its parser
ambiguity. Projector production and its two affected controls were rerun after
that bounded change; unrelated passing controls were retained. Final review then
found that the initial surrogate used a previously recognised `Basic` floor core
instead of the actual generic `_Floor_01` hierarchy. The follow-up reproduces all
four original generic prefab hierarchies in actual Unity and verifies every
recorded ground core in the portable classifier. Its production and only the
four affected projector/collision causal controls were rerun. This is a corrected
test coverage gap; the earlier pass did not establish the real generic-floor
behaviour. The census generator reproduces every recorded classification and
166-ground-core record from the preserved original read-only audit. The primary agent regenerated the final patch inventory after integration.
The integrated driver attempt subsequently passed production and 44 causal
controls; the old anonymous-solid mutation now reaches the newly added generic
floor proof earlier, so only that exact expectation was resumed. Its original
failure is preserved; production and passing controls were not repeated.

These are classification/lifecycle results, not a headset pixel or FPS benchmark.
The 137 projector masks are verified through real Unity render-enable state; the
harness does not establish native Windows projector-shader pixels. New hardware
must confirm visible skull/paper removal and restoration. Native game interactions,
figure/effect settings, wall-fade paths and all wire records are unchanged here.
