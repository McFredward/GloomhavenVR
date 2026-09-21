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
