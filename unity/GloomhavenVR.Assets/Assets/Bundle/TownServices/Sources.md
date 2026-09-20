# Town-service source and license record

Furniture geometry and bounded station rig/actions are authored by the project scripts.
Wood and stone textures reuse Poly Haven CC0 dark_wooden_planks and monastery_stone_floor.
See the existing Assets/Bundle/Environments/License.md acquisition record.

NPCs derive from the selected Hunyuan results. Selected sources intentionally use specularColorFactor 1;
the priestess selected source includes the reviewed normal-atlas seam repair.
No additional roughness floor, atlas repainting, anatomy repair, or source smoothing was applied here.
Normalized 500k source meshes and raw GLBs remain outside the bundle and source control.

The rig uses only bounded station motions. No general locomotion, grasping, facial animation,
eye tracking, or individually validated finger articulation is claimed. Fine source/material seams
remain visible in strong close views; headset review is still required.

Source and derivative SHA-256 values:

## merchant
- Selected model.glb: `27a85d8b6e4217219b475a5685072a0431444c0233b65a0eef56e9ff6822cf28`
- Preparation manifest: `3741214298199a6128aed88e612120fa8cbcfc51dc8bbacc28abcdd4e4646d1b`
- Rig manifest: `1d65532f7ea50c97c7260bf6929fb8986626dc6effffab8a704b840ec5f2610c`
- Bundled rig FBX: `cdee2817445e7db12a8ea9a99be81ca2e0cce8544e4951d9149171805683a76a`
- Source triangles: 499470; final LOD triangles: 80000 / 30000 / 10000.
- Derived source weld: 313656 to 249691 vertices at 1e-6 m, unchanged faces, corner UVs and material assignments.

## priestess
- Selected model.glb: `b6956c95689bde14127ccdda2ead71022d4def2521e266c120634aeec095bb2a`
- Preparation manifest: `0b04af068422658946af33bdc90993b40a4ce1d749631413c7ff29d6d9d38994`
- Rig manifest: `c1c0703f7ac1dd2bd2f0937a7a774c7b2c76aabdcc1052137cd20bfe0316773c`
- Bundled rig FBX: `8021247913ea142cc7799ff4255afd1d32a793b49dba086480dd3fcf2c134ac5`
- Source triangles: 499374; final LOD triangles: 80000 / 30000 / 10000.
- Derived source weld: 298714 to 249649 vertices at 1e-6 m, unchanged faces, corner UVs and material assignments.

## enchantress
- Selected model.glb: `6923aceada531767169e9071c07057379a372f11dbb242d3843e50424f8f8f0e`
- Preparation manifest: `ae963e04bb536e24e458ca0dd4fbda1d2ce4cb7d07a4b24b7a9d6e533f881a1b`
- Rig manifest: `34a26e281e8a310f2cca52e2fa2e4a07e7b559c470795bcb209ffed01ebe2fb9`
- Bundled rig FBX: `0dee94e1ea1163785e2d7caa6a136b02b01e0246808f593d7dd3a52e096a5fcd`
- Source triangles: 499768; final LOD triangles: 80000 / 30000 / 10000.
- Derived source weld: 312027 to 249812 vertices at 1e-6 m, unchanged faces, corner UVs and material assignments.

## Toolchain
- Blender: 4.2.22 LTS
- Rig script: `095c7e6b75c566acf4714439a43718d7a7f5a8f5397bb765932e1599c1b9afdc`
- Preparation / weld helper: `6fb2f5c40bed041efcaba840f42bb83bc50f3817eba7a2ca3307bafe6930ad82`
- Unity 2021.3.5f1, StandaloneWindows64, TypeTrees enabled.
- Standalone GPU textures: 4K BC7 base colour, 4K BC5 normals, 4K DXT5 metallic/smoothness.
- Shared self-contained Standard PBR shader uses Cull Off and _TownVisibility dissolve.

Furniture tile maps import at 1K; the nine NPC maps remain 4K.
Conservative Unity key reduction measured 0.125 mm maximum posed vertex displacement
against the unreduced clips over 108 sampled pose times.
