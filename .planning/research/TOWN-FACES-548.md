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

Facial landmark checks pass twenty-two assertions including nine registration negative
controls. These measure eye spacing, compact beard proportions and UV placement,
not likeness. Focused runtime compatibility checks cover 1886 production assertions and sixteen
compiled negative controls (two new controls restore LOD switching and overlapping
low-detail surfaces). The exact-prefab facial suite now requires one full-detail
skin with all original expression channels and both real eyes/corneas, with no
LOD group. The original three-skin synthetic binding case remains useful to test
compatibility with an older bundle. Final bundle validation is recorded by the
integrator after facial and motion authoring are combined.

## Imported face review and serialization

The first combined motion export omitted the unskinned optical meshes and their
pivots. The corrected exporter selects the complete armature, meshes and empties.
A second misleading preview imported the new FBX underneath old serialized
prefabs: the skin updated, but the reparented eye transforms still held build-547
positions. Rebuilding only the bundle cannot repair that mismatch. Every final
face export must pass through `RefreshFacialRig` before presentation/fixed-detail
finalization and the asset-bundle build. Do not move anatomically correct source
eyes to compensate for stale prefab coordinates.

Actual Unity inspection then identified a profile ear printed behind the actual
helix, excessive lip volume, overly wide canthi and saturated red iris pigment.
The final merchant registers the profile's ear landmarks independently of the
front portrait, locally compresses the lips, and brings the canthi within the
28 mm physical globe envelope. Only red pigment inside the measured iris receives
the shader's brown correction; sclera, pupil, green iris and corneal optics retain
their own data. Source portraits and provider textures remain unchanged.

The render gate now tests each physical iris centre individually in neutral and
fully closed-lid masks. A visible centre must also be covered by its actual lid.
This rejects both empty sockets and misplaced eyes protruding through a cheek;
a global visible-eye pixel count alone could accept those defects. A deliberately
misplaced pair supplies the corresponding rendered negative control for each NPC.
The retained global brightness and aperture checks have not been weakened.

An additional unlit magenta-background collar probe at neutral, yaw +/-45 and
pitch +/-22 found no background visible through the merchant's neck/collar.
Dark angular patches in the earlier lit view were recessed garment surfaces,
not open geometric seams. This does not establish final headset appearance.

The closed-lid gate exposed an additional inherited enchantress defect: the
corneal apex penetrated the fully closed lid. Offline clearance fitting changes
only BlinkLeft/BlinkRight vertices in the physical corneal envelope (60 vertices
per full-detail lid, maximum displacement 1.719 mm). Neutral geometry, all other
shape keys and every skin weight are protected by a before/after binary digest.
The standard assembler invokes the same repair for future enchantress rebuilds;
a second repair changes zero vertices. Full optical objects and protected leg
weights are retained in the final combined export.
