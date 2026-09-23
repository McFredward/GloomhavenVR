# Build 548: stable NPC detail and merchant identity

The new hardware logs identify build 547. The maintainer's visible mesh/lighting
switch is consistent with the actual shipped actor LOD groups: independent full
skinned surfaces switch abruptly at screen-relative heights 0.50, 0.20 and 0.04.
Their default fade mode is none. Vertex-lit practicals also interpolate on the
changed skin topology; geometry, normals and lighting therefore change together.
This is a source-confirmed mechanism, not a measured headset frame capture.

Actual build-547 Windows bundle mesh census (UnityPy, not an estimate):

| Actor | LOD0 vertices | LOD0 triangles | LOD1 triangles | LOD2 triangles |
|---|---:|---:|---:|---:|
| Merchant | 107656 | 136794 | 49559 | 31627 |
| Priestess | 102816 | 135910 | 47747 | 29083 |
| Enchantress | 114331 | 141114 | 49674 | 29844 |

Each actor also has 3680 globe and 1200 cornea triangles, yielding 428458
triangles for all three residents at maximum detail. Furniture and cards are
additional. Extra player workspaces do not instantiate more NPCs. This fixed
budget avoids visible distance switches; hardware timing still requires review.
Scenery LOD and the game's global settings are unchanged. The practical lights
remain vertex lights, avoiding an unrelated multiplication of pixel-light passes.

The shipping prefabs retain one full-detail skin and four optical renderers,
without LODGroup components. The source authoring files retain their three
levels so editable topology is not discarded. A one-time actor-scoped runtime
compatibility path also pins older installed bundles to the full-detail skin,
disables lower surfaces and retains eyes shared by all levels. It neither scans
native scenery nor changes any global LOD setting.

The merchant is refitted against the original flat illustration rather than
narrowing the latest generated head again. A fresh original-referenced angle
sheet replaces the aged/gaunt albedo; explicit eye, mouth, crown and beard
landmarks replace cumulative width factors. The beard is a full rounded volume,
the nose and mouth are aligned to the revised reference, and real spherical eyes,
closed lids, oral anatomy, skull/jaw weights and wrist continuity remain intact.
The original side and rear projection method is retained: a frontal photograph
is not wrapped around the entire skull. Separate clean-skin UV samples prevent
below-eye wrinkles from being projected onto the upper lids.

Review images and rig inputs live in the worker's private
`.planning/debug/town548-faces`. Compare the actual assembled front/oblique and
head-turn views to the original portrait; generated references and passing
landmark checks alone do not establish likeness. Final Unity/D3D stereo appearance
and runtime performance remain hardware outcomes.

Facial landmark checks pass twenty assertions including eight registration negative
controls. These measure eye spacing, compact beard proportions and UV placement,
not likeness. Focused runtime compatibility checks cover 1886 production assertions and sixteen
compiled negative controls (two new controls restore LOD switching and overlapping
low-detail surfaces). The exact-prefab facial suite now requires one full-detail
skin with all original expression channels and both real eyes/corneas, with no
LOD group. The original three-skin synthetic binding case remains useful to test
compatibility with an older bundle. Final bundle validation is recorded by the
integrator after facial and motion authoring are combined.
