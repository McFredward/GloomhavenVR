# Original actor animation for trailer staging

`scripts/trailer/export_actor_motion.py` reconstructs original generic animation
from selected read-only Unity banks. It keeps the original hierarchy, normalized
quaternion transforms, skin weights, bind matrices and weapon joint attachments.
This provides an exact source animation for a cinematic camera shot; it is not a
runtime capture, a scenario simulation or evidence that a selected card causes
this particular attack animation.

The exporter samples the skeletal Transform tracks. It does not run Animator
state blending, animation event callbacks, runtime cloth, shader/VFX tracks or
combat rules. Those aspects require separate staging or genuine capture.

The Brute's `HE_Brute_Melee_Attack_v001` lasts 2.0333335 seconds and
`HE_Brute_Heavy_Melee_Attack_v001` lasts 2.4000001 seconds. Living Bones has
`LivingBones_idle` (2 seconds), `LivingBones_OnHit_v001` (3.1000001 seconds),
`LivingBones_Melee_Attack_v001` (3.3333335 seconds) and
`LivingBones_Melee_Attack_v002` (2.3666670 seconds). The complete inspected clip
inventory is retained privately under `.planning/debug/trailer/motion/`.

The six default exports are the Brute's original idle and two melee clips and Living Bones'
idle, hit and second melee clips. All selected sources use streamed cubic
Transform curves plus constants; they have no dense sample curves. Every scalar
stream is evaluated as `a*dt^3 + b*dt^2 + c*dt + value`, using its latest original
key. The evaluator checks that each polynomial reaches the next original key and
that sampled time zero agrees with the previous proven native idle evaluator.
It rejects unsupported Euler Transform bindings rather than guessing their
order. No animation is generated, retargeted, stretched or time-warped.

Run it from a checkout with UnityPy 1.25.2 and NumPy:

```sh
/home/claw/gloomhaven_vr/.planning/debug/trailer/motion/venv/bin/python \
  scripts/trailer/export_actor_motion.py \
  --game-base /home/claw/gloomhaven_vr/ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64 \
  --native-scene /home/claw/gloomhaven_vr/.planning/debug/trailer/native-scene \
  --output /home/claw/gloomhaven_vr/.planning/debug/trailer/motion
```

Use `--actor brute --clip HE_Brute_Heavy_Melee_Attack_v001` to select a single
original clip. The default frame rate is 24. Samples include every complete
1/24-second interval within the original clip; the script does not stretch the
last partial interval to the next frame. The default Brute idle lasts 1.6666667
seconds and has 41 frames; its melee clips have 49 and 58 frames. Living Bones'
idle, hit and second melee clips have 49, 75 and
57 frames respectively.

Each compressed NPZ contains:

| Key | Format and meaning |
| --- | --- |
| `positions` | float32 `[frame, vertex, 3]`, Blender coordinates in original units |
| `times` | float64 original clip sample times in seconds |
| `mesh_offsets`, `mesh_names` | Original mesh ranges within the combined vertex array |
| `triangles` | int32 source triangles with reversed winding for the coordinate conversion |
| `uv` | float32 original UV coordinates, in the same vertex order |
| `triangle_materials`, `material_names` | Exact original OBJ material ordering |
| `attachment_world_unity` | float32 original animated weapon-joint matrices, before Blender conversion |
| `fps` | Integer sampling frame rate |

The coordinate conversion matches the native scene export: Unity `(x,y,z)`
becomes Blender `(-x,-z,y)`, Z-up with anatomical forward along `-Y`. Every JSON
manifest records the bank SHA-256 values, clip path ID, original duration,
sample times, part ranges, base OBJ and manifest hashes, numerical checks and
NPZ hash. The Brute retains all 12,034 original vertices, including cloak,
skirt, sword and shield. Living Bones retains all 13,663 vertices, including its
sword and cloth.

Import the matching idle OBJ into Blender without splitting its original mesh
objects, preserving its UVs and material slots:

```python
bpy.ops.wm.obj_import(
    filepath=base_obj,
    forward_axis="Y", up_axis="Z",
    use_split_objects=False, use_split_groups=False,
)
data = np.load(animation_npz, allow_pickle=False)
assert len(actor.data.vertices) == data["positions"].shape[1]
actor.data.vertices.foreach_set("co", data["positions"][sample_index].ravel())
actor.data.update()
```

Shape keys can use the same arrays if animated rendering is preferable. Keep the
actor object's cinematic position/rotation/scale separate from these source
vertex positions. The default OBJ importer otherwise splits the six Brute mesh
parts into separate objects, which cannot accept the combined array directly.

Validation on 2026-10-12 compared both actors' original idle poses against the
already inspected native OBJ: maximum absolute error was below `5e-9` original
units. All six NPZ exports have identical triangles and material indices to
that OBJ, finite positions, exact 24 fps times and matching UVs within `4.8e-9`.
The maximum scalar streamed-key boundary error across the six clips was
`1.01e-7`. Blender's combined import retained vertex order with maximum position
error below `1.2e-7` (float32 conversion).

Five 1280×720 studio renders at 0, 0.5, 1, 1.5 and 2 seconds were inspected. The
original heavy Brute attack raises its attached sword overhead, delivers the
downstroke while crouching and recovers; its sword and shield stay attached.
Living Bones' original hit animation recoils and returns. Its hit onset can be
staged approximately 0.65 seconds after the Brute starts to align the visible
recoil with the downstroke. This timing is an editorial staging decision; no
original card-to-animation mapping is claimed.

Private evidence lives under `.planning/debug/trailer/motion/`: clip inventory,
per-clip manifests, `numeric-validation.json`, `blender-import-qc.json`, source
logs, QC PNGs and the rendering script. Owned Python bytecode is removed after
the runs stop; original banks and retained source/export evidence remain.
