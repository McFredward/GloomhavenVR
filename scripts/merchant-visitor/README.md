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

## Structural work still required

The true P9 imported skin has 25 left and 31 right camera-visible sleeve
intersection lines, plus six visible right-hand lines. Its palms and finger
pads are substantially closer to the belly than the shipped pose, but this
visible clipping makes P9 unsuitable for release. The real Unity 803-frame
fixture also reports intersections during attention, approach, withdrawal,
offering, and handover.

The mapped intersections form seven original Material0 sleeve patches with
closed boundary cycles of 12, 12, 9, 6, 4, 4, and 5 vertices. In a 25 mm
neighborhood of these patches, the P9 coat-normal probe found hidden sleeve
vertices as deep as 28.2 mm on the left and 22.2 mm on the right. Moving the
whole inner sleeve by the few millimeters visible at an intersection line
cannot resolve that buried wall. It also risks the cuff seam and palm contact.

Two bounded topology experiments were rejected:

- Local support-edge subdivision preserves the mesh contracts documented in
  the JSON reports, but its thin right-sleeve support triangle (about
  0.095 mm²) folds under a useful local corrective.
- A closed center-fan replacement preserves the original patch boundaries,
  shape keys, UV sets, and weights, but the warped 3D boundaries turn some
  faces backwards in both rest and P9 poses. A quality-constrained
  retriangulation has no holes or inverted faces and lowers the visible
  sleeve lines to roughly 17 left and 27 right, but leaves visible clipping
  and the six right-hand lines. A sleeve-only normal-field correction on this
  topology increased visible lines and approached face inversion.

The next asset candidate needs a new, closed inner-sleeve surface with a
deliberately routed cuff lip and enough support geometry to follow the coat
without pulling the exterior sleeve into a shelf. Preserve the existing
neutral/coin-work silhouette, all 11 facial shapes, both UV channels, four
eye renderers, all three source LODs, and the original action takes. The
right-hand skin must also clear the coat while the palm and finger pads stay
outside and in contact. A visitor transit action/corrective must then route
both arms outside the coat through approach, withdrawal, offering, and
handover. Accept only after actual imported-skin signed contact, visible
intersection-line and triangle-orientation scans over all 803 Unity frames,
followed by textured front/side renders. No current merchant FBX passes this
gate; the production FBX is intentionally unchanged.
