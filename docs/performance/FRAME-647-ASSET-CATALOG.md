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
Closed floor meshes additionally pin their complete outer XZ perimeter.

Every accepted floor derivative retains all original covered cells and holes on
an 18x18 projected lattice. Maximum sampled top-height displacement is bounded
by the greater of 1.5 cm in original mesh units or 20% of native height extent.
The focused checker also verifies three intermediate morph states. This finite
certificate is not a proof of every possible eye position or a replacement
collision surface. Original colliders and gameplay heights are unchanged.

| Certified strong tier | Families with a meaningful derivative | Source triangles | Derivative triangles | Reduction |
|---|---:|---:|---:|---:|
| Structure |505|510,018|60,903|88.1%|
| Floor |68|14,157|4,907|65.3%|

On 356 structural families shared with the former strong bank, the same-family
comparison is 76,365→53,485 triangles, a further 30.0% reduction. Floor correctness
is deliberately stricter than legacy generated streams that were never admitted
as floors: some new floor derivatives use more triangles and others remain exact
originals. When no safe meaningful derivative exists, the native shape remains.

`SE_Rot_01_Floor_Under` illustrates why a floor name is insufficient to infer
geometry. Its 48 original triangles are vertical side skirts, with no horizontal
footprint. The catalog retains its floor role and byte-exact tier 100 for exact
floor grouping; it has no reduced tier and is never flattened into a plane.

## Package and validation

The independent Windows bank uses game-exact Unity 2021.3.5f1, Gamma, all existing
instancing/fog programs and unchanged shader sources. It contains 4,040 geometry
streams, one index and two shaders, occupying 70,975,207 bytes. SHA256:
`cfcc2dee4121e7ab1a2881cb033dd5fb50fada00797d527d347b7bd4dc8d3116`.

The first minimal private pack omitted the built-in AssetBundle module. Unity
reported successful output but stripped the AssetBundle container. The independent
package check rejected it; enabling the module alone retained the invalid cached
serialization. The builder now requires the module, forces regeneration, verifies
all input stream hashes and actually loads the built bank to check every asset
path. The real loader fixture also explicitly enables the module. The rejected
output and its causal logs remain in private evidence.

Focused evidence under `.planning/debug/frame647-assets/`:

- `complete/catalog-proof.json`: all 129 source bundle hashes, all 1,434 certified
  identities, every derivative seam/boundary, 152 floor derivatives, all three
  intermediate floor morph states, and three actual-source causal controls.
- `bank-check-final.log`: every packaged stream/hash and independently extracted
  original byte/provenance, actual compiled shader instancing/fog programs.
- `bank-load/run-bgbzckz6/`: actual Unity production decoder loads all 4,040 streams,
  3,296,386 vertex slots and1,741,930 triangles across all packaged tiers; complete
  index and both shader asset paths are available. The graphics device is Null.
- `preparation-guards.log`: seven destructive/malformed-original controls pass.
- `pack-final.log`: final builder hash/preflight, forced build and actual loaded
  asset-container validation pass. An inherited potentially-uninitialized
  `OriginalAlbedo` shader compiler warning belongs to the unchanged shader source;
  this is not a claim of a warning-free shader build.

Automated geometry and package checks establish source coverage and software
safety, not a headset picture, multiplayer acceptance or a measured net FPS gain.
The integration's fully loaded Frame test still needs to measure its actual
admission, retained sources/draw groups and frame times at fixed settings.
