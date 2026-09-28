# Merchant sleeve topology feasibility

This is an analysis checkpoint, not a shipping merchant asset. The source FBX
remains unchanged. The P9 pose gives plausible hands-on-belly contact but has
visible sleeve and hand intersections in the actual Unity skin; do not use its
action or corrective as a finished visitor pose.

`p9-visible-faces.csv` maps the 32 original FBX polygons implicated by the
actual intersection-line visibility scan to Unity triangles. Its source was
`/tmp/npc580-merchant-geometry/p9-visible-sleeve-raw.csv` plus the exact
Unity-to-FBX polygon match. `p9-bones-frame90.csv` is the P9 actor-space bone
pose measured against the real 56-bone Unity import. `unity-bindposes.csv`
contains the corresponding imported bind matrices. These compact inputs are
tracked here so the test does not depend on temporary analysis files.

From the repository root, run:

```sh
/home/claw/blender-4.2/blender -b -noaudio \
  --python scripts/merchant-visitor/topology-feasibility.py -- \
  --fbx unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Actors/merchant/merchant_rig.fbx \
  --face-map scripts/merchant-visitor/p9-visible-faces.csv \
  --pose-bones scripts/merchant-visitor/p9-bones-frame90.csv \
  --bindposes scripts/merchant-visitor/unity-bindposes.csv \
  --report /tmp/merchant-topology-p9.json
```

The script checks that local subdivision around the seven visible sleeve
patches preserves existing physical boundaries, all shape keys and UV sets,
skin weights, face orientation and material surfaces in both rest and P9
poses. The source SHA-256 is checked before import. This establishes enough
topology to attempt a closed inner-sleeve profile. It does **not** establish
collision clearance, contact over the animation, or acceptable visuals.
