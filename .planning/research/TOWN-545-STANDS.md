# Town 545: original props, practical lighting and hand contact

## Evidence and fixes

The maintainer's 544 log records `Original decoration material unavailable` at
`LogOutput.log:1254`; the log does not identify the underlying Addressables failure.
Source review proves that the former global material gate propagated one failed
lamp dependency to unrelated pending books and vessels. Each piece now waits only
for its own dependencies. Material requests release failed handles, retry at most
three times, time out after 30 seconds, and report bounded contextual failures.
Failure remains cosmetic and never gates a native service continuation.

Original `Library.Clutter.Shelf.Individual#7` is `PCG_CR_ST_Shelf_Book_07`, using
`CR_ST_Shelf_Books_Sparse_MAT` (GUID `27f2d68f4348ef248839912f9cfa2843`). Its native
`Amp_Basic_WallFade` shader uses `_UVTiling=1`, while its serialized Standard
`_MainTex_ST` scale remains `.5,.5`. Its mesh UVs already address the printed-page
atlas region. Copying the stale Standard transform sampled the blank grey lower
quadrant; native alchemy props have the same mismatch. The owned material adapter
maps the actual Amp tiling/tint and preserves Standard transforms, original source
materials and textures. Private read-only catalog/mesh/atlas evidence is in
`.planning/debug/town545-native/`.

The former candle holders were original wall sconces without walls. The new
practicals use the freestanding native `CR_INT_Lantern_01_b` body/glass subtree of
`Gaslight.Lighting.Torch.Wall#1`, excluding its separate `CR_INT_Lantern_Fixture`.
Their bases sit on the worktop. Original `CandlePivot` flame/glow visuals are
copied into the lantern interior, without native scripts or particle controllers.
Each resident has two visible practical sources, with six bounded point lights
and one shared environment directional light in total. Lights keep the original
room/moon policy and do not introduce an ambient floor or modify native lights.
The enchantress right lantern requires the integrator's rear support at
`(.68,.957,.70)`; other lanterns stand at `(+/-.68,.957,.20)`.

Decoration uses native balance, coins, open books, scrolls, offering bowl and
alchemy vessels. The priestess ledger is centred `(-.33,.957,-.12)`, footprint
approximately `.30 m`; inscription height must account for the uneven pages.
The enchantress's front-right rune inventory stays clear of her vessels.

`TownServiceDecor.CoinTemplate` is an inert, hidden native coin, normalized to
five centimetres. The priestess decor owns its meshes/material handles; disposal
clears its static lookup before releasing materials. A network asset-registry reset
rebinds the live template textures on the next bounded loader tick. The integrator registers the
`ritual.coin` presentation template. Texture provenance derives from this exact
native source hierarchy, not asynchronous completion order or instance IDs.

The enchantress's native glow artwork supplies a core and twelve small orbiting
sparks. Their bounded closed-form orbit uses the existing shared performance clock
and cast envelope, with no independent multiplayer particle simulation, sound,
or free-running Unity animation.

## Verification

- `scripts/check-town-decor.py`: genuine Unity 2021.3.5 runtime, 61 assertions;
  seven compiled negative controls demonstrate independent failure/recovery,
  bounded timeout/retry, coin provenance/lifetime and original atlas mapping.
- `scripts/check-town-service-lighting.py`: 805 production lifecycle/ownership
  assertions and five compiled negative controls; same-layer/name native lights
  remain untouched, failed/unloaded practicals stay dark, all owned lights release.
- These checks establish source behavior, not headset appearance. Final station
  geometry, native prop composition and anatomical hand contact require the
  integrated asset/runtime rendering pass and hardware review.

## Additional visitor workspaces

`StaticPropSource`, `StaticPropCount`, `TryStaticProp` expose stable sparse indices
from the permanent station, excluding moving tools, magic and the hidden coin.
The source transform changes on resident recreation; disposing an older instance
cannot invalidate its replacement. Addresses are `decor.{service}.{index}`.
`TryPractical` supplies the original flame point in the prop's own local frame
and its normalization-to-station distance scale.

`TownServiceWorkspacePractical.RebindClone` handles only whole lamp-root modules.
Inactive frozen templates allocate no light. An enabled owner/remote instance
owns one point light outside the published transform tree, so the helper cannot
change native-template topology. It follows the exact local flame point, world
scale, current replaced material and property-block dissolve. Disable/destroy
immediately darkens and deactivates the light before releasing ownership; native
lights and source materials remain untouched. Repeated binds are idempotent.

## Practical calibration

The actual actor review with the original 1.05 practical power remained too dark
at the face's approximately 0.9 m lamp distance. A controlled 2.6-power render at
the same real lantern positions retained facial shading and made the face/beard
legible; the integrator reviewed and accepted that comparison. Permanent and
workspace lanterns now share 2.6 through `PracticalPower`. No scene ambient,
self-emission, painted catchlight or native lighting policy was introduced.
Private comparison: the face worker's `town545-review1` and `town545-review2`.


## Anatomical hand contact

The new authored Hand frame uses wrist-to-middle-MCP local Y and palmar local Z.
Runtime finger articulation uses each new joint's positive local-X hinge, with
bounded local-Z thumb opposition for writing. Legacy marker-free rigs retain their
old conservative fallback. Contact markers are excluded from articulated joints.

An attentive resident smoothly sets tools down and rests relaxed hands on the
counter. The support solve uses the actual palm marker and five distal skin-pad
markers, preserving the palm arch while preventing fingers from entering the wood.
Cached transforms supply all contact calculations; no mesh baking, hierarchy scans
or per-frame collections are needed. Elbow poles remain near the body. Work adds
only a small torso lean and four degrees of neck flexion; bounded additional torso
lean compensates the existing replicated terrain grounding offset.

The immutable Hands545v3 asset (SHA-256
`9dc7c89e3ddfc4fe01157e882e7912c0c36c0d639b252868c44b797c33cac41e`)
passed 125,088 actual-asset/runtime assertions and eleven compiled negative
controls for all three residents, including
terrain offsets, smooth prayer interruption, contact and restoration. Private CPU
renders are `town545-motion-render-v3/service{1,2,3}-phase2-view0.png`; merchant
writing uses the original native book atlas and its pen tip reaches y=.988965 m.
The combined work-gaze render also samples the real head pose, rather than judging
only an upright skeleton. The diagnostic table/lighting are not the final combined
station or proof of hardware appearance.

The focused motion checkpoint builds in strict Release with zero warnings/errors.

## Final actor and native-effect evidence

The immutable final anatomical Linux bundle, SHA-256
`c35244d2df2ab86f33abbe0d5ba726ea33bacbdb6d83ad82a47c4de71f96a422`,
passed the current integration source's 125,088 contact/activity assertions and
all eleven compiled negative controls. Evidence is in the decor worktree's
`town545-final-activity/run-svybeklx`, including exact production source hashes.

The independent CPU pose gallery uses the original open-book geometry/atlas,
original coin, native lantern body/atlas and the actual 2.6-power practical locations
and range. It covers work, attentive palm support and combined work gaze. It is
bounded activity evidence: optional temple bowl/scroll decoration, flame atlas and
interactive stock are not all included in this isolated scene.

That final render exposed a real shared-glow defect: CPU dynamic batching
pretransforms small quads and replaces their individual object origins, while
`TownFlame` uses that origin to construct its billboard. Original materials and
all thirteen meshes were present but invisible. Removing only the billboard branch
made them visible; adding only `DisableBatching=True` restored the unchanged real
native glow in the correct hand position. No emission/brightness/art workaround was
introduced. The fixed gallery is `town545-final-actual-motion-fixed`; the source-negative
comparison is `town545-final-actual-motion`.

The dedicated production shader regression has 4,431 lit pixels for physical
reference quads, 4,380 for correctly billboarding quads and zero for the separately
compiled shader with the single batching guard removed. The whole flame fixture
passes 1,022 assertions, six clock/material negative controls and this original
shader negative (`town-flame/run-e2iup9yc`). The final Windows/Linux bundles must
include the shader checkpoint; this does not change the validated anatomical meshes.
