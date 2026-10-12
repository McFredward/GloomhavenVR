# Original game geometry for the trailer

The approved trailer opens with a short CGI scene in which a giant original VR
mask and hand reveal the adventure as a tabletop world. Its figures should be
recognizable original Gloomhaven Digital actors. The private staging exporter
reads only two actor banks and two small PCG architecture pieces. It never
modifies, repacks or checks extracted publisher assets into Git.

## Private deliverables

The maintainer requested all intermediate media under the main checkout's
gitignored `.planning/debug/`. Current native scene sources are in:

```text
/home/claw/gloomhaven_vr/.planning/debug/trailer/native-scene/
  scene_manifest.json
  brute/brute_idle.obj
  brute/brute_idle.mtl
  brute/manifest.json
  brute/native_rig.json
  livingbones/livingbones_idle.obj
  livingbones/livingbones_idle.mtl
  livingbones/manifest.json
  livingbones/native_rig.json
  dungeon/CR_Dungeon_Wall_Stone_01.obj
  dungeon/CR_TC_Floor_01.obj
  dungeon/manifest.json
```

Each actor directory also contains original textures decoded to PNG. Source
bank SHA-256 hashes, native object path IDs, submesh slots, original material
properties, texture identities/dimensions and output hashes are recorded in
the manifests. The rig JSON retains original bind matrices, per-vertex weights
and bone indices, sampled local transforms and original Unity world matrices.
It is a source map for subsequent posing, not an imported Blender armature.

The actors are the original Brute with sword/shield and Living Bones with sword.
Only their high-detail body, cloth and equipment meshes are included. Weapon
mounts follow original `CharacterManager.WeaponLists.AttachToBone` and the
`DoEquip`/`ObjectPool.Spawn` source behavior: the weapon root has its original
local position, and its world orientation follows the attachment bone.

## Reproduce

Use the existing UnityPy 1.25.2 environment and read-only game data:

```bash
/home/claw/unitypy-venv/bin/python scripts/trailer/export_native_scene.py \
  --game-data /home/claw/gloomhaven_vr/ressources/GH_Data \
  --output /home/claw/gloomhaven_vr/.planning/debug/trailer/native-scene \
  --actors brute,livingbones --dungeon
```

`--actors brute` selects only the hero. `--actors '' --dungeon` selects only the
two architecture pieces. The floor is the full flat `CR_TC_Floor_01` hex tile,
not a narrow procedural floor trim/segment. Partial runs preserve manifest links to the current
deliverables for the other selected assets. No unrelated banks are extracted.

The actor exporter reads the original generic animation bindings, matches their
CRC32 paths to original transforms, and samples the original idle clip exactly
at its t=0 keys. Original dense/constant curves participate as present. It
rejects missing t=0 keys, unsupported Euler curves, missing material dependencies
and inconsistent native counts rather than inventing an approximate pose. It
then applies linear skinning with those sampled transforms and original bind
matrices. A single-influence mesh uses Unity's implicit weight of one when the
weights channel is omitted.

## Blender staging

OBJ coordinates are already Blender Z-up. The conversion is Unity `(x,y,z)` to
Blender `(-x,-z,y)`, and face winding is reversed. Use:

```python
bpy.ops.wm.obj_import(filepath=source, forward_axis='Y', up_axis='Z')
```

Original units and origins are preserved. The Brute's sampled height is about
2.135 m; scale `0.04` makes a roughly 8.5 cm miniature. Living Bones is about
1.846 m high. Both actors' **anatomical front points along Blender -Y**. The OBJ
importer's `forward_axis='Y'` preserves the already converted coordinates;
`NEGATIVE_Y` would introduce another half-turn. A camera on the -Y side sees
their faces. This is verified by the private frontal studio preview.

MTL provides diffuse texture links for a convenient first import. The manifests
provide full original shader property and texture mappings for explicit PBR
staging. Cinematic lighting, normal-map strength, opacity behavior and PBR
translation are render decisions; an OBJ import does not evaluate the game's
original custom shaders or effects.

The wall/floor meshes come from `pcg_dungeon.asset.bundle`. Their original PCG
template renderers have empty material slots because the procedural room
builder assigns materials later. The export explicitly pairs them with the
matching original `CR_Dungeon_StoneWall_01_MAT` and
`CR_Dungeon_StoneFloor_01_MAT` banks. This material pairing and any arrangement of
the pieces are cinematic reconstruction, not an evaluated original room.

## Verification and evidence boundary

The exports preserve original topology, UVs, submesh material membership and
decoded source textures; they do not remesh or replace the figures. Counts,
finite skinned coordinates, native binding dimensions and dependency identities
are checked during export. A Blender import/render is inspected separately to
verify the posed parts and equipment visually.

These are **geometry reconstructions, not gameplay footage**. A successful
export or attractive render establishes neither exact original shader output
nor the appearance of the VR mod on a headset. Native gameplay should remain
identified separately in the final edit. Return any held figure to its original
board cell before showing a non-idle native action, as required by the current
mod behavior and the maintainer's ruling.

The exporter has no paid API calls, native gameplay callbacks, test processes or
generated Unity/Blender project caches. Extracted sources, manifests, preview
captures and current output media remain private review evidence.
