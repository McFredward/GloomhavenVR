# Build 647 architecture asset preparation

This is the offline asset half of the maintainer's approved proposal 2: broader,
stronger private 3D architecture and floor derivatives across the original game
and its installed DLCs. Runtime admission, optional density, room grouping and
profile defaults are documented by the integration report. This preparation
does not implement Single-Pass, native batching, a 2.5D board or persistent room
state. Publisher files remain read-only.

## Coverage and identity

The fresh census reads all 129 original PCG database bundles and 8,864 local
MeshFilter mesh occurrences, including every original use and actual ancestor
chain. The losslessly compressed `tools/environment-mesh/catalog.json.gz`
retains original bundle SHA256, mesh path IDs, names/bounds/submeshes, component
classes, resolved script names, authored local transforms, and precise collider,
rigid-body, light and animation/controller facts. It includes negative evidence,
not just approved examples. The runtime index does not contain this large census.

Seven ambiguous metadata identities with different original geometry are
excluded. The final bank contains 1,434 exact originals: 133 certified floor
families, 531 structural families and 770 protected legacy identities. Exact
originals remain available to existing exact-copy consumers; a protected identity
does not authorize the new floor/architecture admission. Approved families occur
in 61 source bundles, including 13 DLC bundles. There is no tested-scenario-only
list or arbitrary runtime mesh-name match.

Every use of a family participates in certification. An unknown/gameplay script,
animated/effect/rigid-body ancestor, water/door/gameplay ancestry or incompatible
use protects that entire family. Native material/detail/shadow/fade/AutoLOD
management is recognized as rendering provenance, not interactive authorization.
Live native ownership, materials, rendering state, fades and held/registered
props still require runtime validation.

The independent closed ornament catalog contains 21 signatures/20 names,
representing 43,310 unique-source triangles. It covers separate tendrils, selected
wall trim/top/frame and ossuary floor ornaments. It never permits hiding a
complete wall, pillar, walking surface or furniture core. One shelf-pot detail
also belongs to the older small-dressing policy. These counts are catalog
coverage, not resident or visible scenario draw counts. Native colliders remain;
the optional runtime density path must prove a retained collision core or exact
native tile/wall ownership and retain all interaction vetoes.

## Strong geometry with reversible morphs

All generated streams keep format GHEM1 and tiers 100/50/0. Tier 100 is byte-exact
independently extracted original geometry. Derivatives retain every original
vertex slot, normal/tangent/color/UV channel, material submesh and original-index
triangle winding. The runtime can use its existing same-index continuous morph.

Strong clustering begins at two cells per axis instead of the former four;
medium begins at six instead of twelve. Every original open seam is fixed in
three dimensions, and the actual original axis bounds remain represented.
Closed floor meshes additionally pin every original position along their convex
projected XZ hull, including collinear edge points and every authored height.
This protects diagonal outer edges that axis bounds alone do not identify.

Every accepted floor derivative retains original covered and empty samples on
an 18x18 projected lattice. Maximum sampled top-height displacement is bounded
by the greater of 0.015 original mesh units or 20% of native height extent.
The focused checker also verifies three intermediate morph states. This finite
certificate does not prove exact arbitrary concave footprint unions, tiny holes
between samples or every possible eye position. Original open boundaries remain
fixed, but unsampled closed interior features are a remaining proof limit.
Original colliders and gameplay heights are unchanged.

| Certified strong tier | Families with a meaningful derivative | Source triangles | Derivative triangles | Reduction |
|---|---:|---:|---:|---:|
| Structure |505|510,018|60,903|88.1%|
| Floor |80|15,643|5,855|62.6%|

On 356 structural families shared with the former strong bank, the same-family
comparison is 76,365→53,485 triangles, a further 30.0% reduction. Floor correctness
is deliberately stricter than legacy generated streams that were never admitted
as floors: some new floor derivatives use more triangles and others remain exact
originals. When no safe meaningful derivative exists, the native shape remains.

An independently captured representative set of 16 actual native floor families
shows the practical limit: 18,288 original triangles become 16,768, an 8.3%
reduction, and only two families receive a meaningful derivative. The earlier
candidate yielded 16,734 triangles (8.5%); projected-rim pinning is a correctness
repair, not an additional gain for that native sample. The rim-only prototype
was rejected as a claimed performance expansion. Across all 133 floor families,
including exact-original fallbacks, the equivalent comparison is 53,962 original
triangles, 44,712 in the earlier candidate and 44,174 after the correction (18.1%
from original). These source-family counts do not predict a resident scenario's
triangle count or FPS.

The final independent gift-wrapping audit rejected 94 of the earlier 152 floor
derivatives for moving original projected hull-edge slots. All 169 final floor
derivatives preserve those slots exactly. An actual diagonal closed-floor slot,
missed by both axis extrema and open-edge checks, supplies a causal negative
control. The preparation keeps the original footprint/height tolerances.

`SE_Rot_01_Floor_Under` illustrates why a floor name is insufficient to infer
geometry. Its 48 original triangles are vertical side skirts, with no horizontal
footprint. The catalog retains its floor role and byte-exact tier 100 for exact
floor grouping; it has no reduced tier and is never flattened into a plane.

## Package and validation

The independent Windows bank uses game-exact Unity 2021.3.5f1, Gamma, all existing
instancing/fog programs and unchanged shader sources. It contains 4,057 geometry
streams, one index and two shaders, occupying 70,999,748 bytes. SHA256:
`c3648422dedaf3197ae1738ccbb4eff50aad0491113bbd263855fe6c04ea1802`.

The first minimal private pack omitted the built-in AssetBundle module. Unity
reported successful output but stripped the AssetBundle container. The independent
package check rejected it; enabling the module alone retained the invalid cached
serialization. The builder now requires the module, forces regeneration, verifies
all input stream hashes and actually loads the built bank to check every asset
path. The real loader fixture also explicitly enables the module. The rejected
output and its causal logs remain in private evidence.

Focused evidence under `.planning/debug/frame647-assets/`:

- `complete/catalog-projected-rim-proof.json`: all 129 source bundle hashes, all
  1,434 certified identities, every derivative seam/boundary, 169 floor
  derivatives, all three intermediate floor morph states, and four actual-source
  causal controls.
- `projected-rim-final-audit.json`: separate supporting-hull construction verifies
  all original collinear rim slots, rejects the earlier immutable bank and passes
  every final derivative. The old commit and source/variant hashes are recorded.
- `native16-floor-rim-result.json` and
  `floor-source-equivalent-rim-comparison.json`: representative native and full
  source-equivalent floor totals, including original fallbacks.
- `projected-prototype-result.json`: rejected rim-only performance expansion;
  the subsequent accepted change repairs footprint correctness.
- `bank-check-projected-rim.log`: every packaged stream/hash and independently extracted
  original byte/provenance, actual compiled shader instancing/fog programs.
- `bank-load-projected-rim/run-2ydmyq2e/`: actual Unity production decoder loads all
  4,057 streams, 3,297,619 vertex slots and 1,742,924 triangles across all packaged tiers; complete
  index and both shader asset paths are available. The graphics device is Null.
- `preparation-projected-rim-guards.log`: seven destructive/malformed-original controls pass.
- `pack-projected-rim.log`: final builder hash/preflight, forced build and actual loaded
  asset-container validation pass. An inherited potentially-uninitialized
  `OriginalAlbedo` shader compiler warning belongs to the unchanged shader source;
  this is not a claim of a warning-free shader build.

Automated geometry and package checks establish source coverage and software
safety, not a headset picture, multiplayer acceptance or a measured net FPS gain.
The integration's fully loaded Frame test still needs to measure its actual
admission, retained sources/draw groups and frame times at fixed settings.
