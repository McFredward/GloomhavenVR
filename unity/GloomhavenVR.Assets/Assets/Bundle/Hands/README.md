# Bundle content: Hands/

Intended assets (Phase 2 `feat/hands` consumes these from `gloomhavenvr.bundle`):

| Asset | Description |
|---|---|
| `Valve/vr_glove_left_model_slim.fbx` | Left hand, skinned, SteamVR skeleton — imported from the SteamVR Unity Plugin (BSD-3-Clause), see `unity/HANDS.md` |
| `Valve/vr_glove_right_model_slim.fbx` | Right hand, same source |
| `Valve/Materials/vr_glove_color.mat` + `vr_glove_color.jpg` + `vr_glove_normal.png` | Glove material/textures. The material uses the built-in Standard shader — the mod rebinds it at runtime against the game's shader set (built-in shaders are never bundled, TOOLCHAIN.md §4.1) |
| `Valve/LICENSE-Valve.txt` | Valve's BSD-3-Clause license text — REQUIRED attribution, keep next to the assets and in release zips |
| `VRHand_L.prefab` / `VRHand_R.prefab` | Our own prefabs: glove mesh + named bone hierarchy for the FingerCurler (3 joints per finger driven directly — no Animator). Do NOT import Valve's `*RenderModel*.prefab`s; they reference SteamVR MonoBehaviours we do not ship |

Loader contract (implemented in `src/GloomhavenVR/Hands/HandVisuals.cs`, frozen in
`docs/INTERFACES-P2.md`): the mod probes these asset paths, first hit wins:

1. `Assets/Bundle/Hands/VRHand_L.prefab` / `Assets/Bundle/Hands/VRHand_R.prefab`  ← canonical names
2. `Assets/Bundle/Hands/HandLeft.prefab` / `Assets/Bundle/Hands/HandRight.prefab` (legacy alias)

Rig mapping inside the prefab, by child transform name (first match wins):

- Anchors (preferred, add as empty children): `Anchor_Wrist`, `Anchor_Palm`
  (+Y must point OUT of the palm), `Anchor_IndexTip`, `Anchor_Grab`
- Finger joints: `Anchor_{Thumb|Index|Middle|Ring|Pinky}_{Root|Mid|Tip}`
- Fallback: SteamVR bone names (`finger_index_0_r`, `finger_index_1_r`,
  `finger_index_2_r`, … with `_l`/`_r` suffix) are found automatically
- Anything missing is synthesized at default positions — a bare mesh still works,
  it just won't articulate where unmapped

Conventions:
- Prefab root at wrist, +Z pointing along fingers, +Y out of the back of the hand
  (matches the OpenXR `pointerPosition/pointerRotation` grip convention closely
  enough; final offsets live in mod code, not in the asset).
- Finger joints must curl toward the palm under POSITIVE local-X rotation
  (the FingerCurler applies `Quaternion.Euler(maxAngle * curl, 0, 0)` on top of the
  authored local rotation).
- No hand prefab in the bundle is fine: the mod falls back to a procedural
  primitive hand with the identical transform contract.
- Keep polycount as-is (the "slim" gloves are already low-poly).
- Everything in this folder except `*.md` / `*.txt` / dotfiles is packed into
  `gloomhavenvr.bundle` by `Assets/Editor/BuildBundles.cs`.

## Alternative hand styles (selectable at runtime)

Three skinned hand SETS ship in the bundle; the mod's `[Hands] HandStyle` setting
(VR settings panel, live rebuild) picks the pair, and the choice is synced to other
VR players (`Net.AvatarState.HandStyle`, additive wire-v3 trailing byte):

| Style  | Prefab pair | Source |
|---|---|---|
| Glove (default) | `VRHand_L/R.prefab` | **artist-authored** leather glove — mesh, rig, weights and UV atlas all hand-made, delivered as a single LEFT-hand FBX and adopted by `import_glove_fbx.py` (ModBuild 169; replaced the AI-generated `Hand_prepped.glb` + `rig_hand.py` glove) |
| Plate  | `VRHandPlate_L/R.prefab` | Hunyuan3D plate-armor gauntlet (`hunyuan3d-a22a9142…glb`) |
| Arcane | `VRHandArcane_L/R.prefab` | Hunyuan3D arcane-runes mage glove (`hunyuan3d-31b7b393…glb`) |

Pipeline for the alternative sets (Blender 4.2 headless, `unity/hand-prep/`):

1. `prepare_hand.py <raw.glb> <HandPlate|HandArcane>` — canonicalize orientation
   (mirror to the LEFT-hand contract frame), pre-decimate global weld, decimate,
   de-lean, wrist/finger landmark detection -> `ressources/hands/<name>_prepped.glb`
   (gitignored) + `unity/hand-prep/<name>_joints.json` (committed) + the loose
   `VRHand<Style>_albedo.png` here.
2. `rig_hand.py` with `RIG_HAND_SRC/RIG_HAND_NAME/RIG_HAND_JOINTS/RIG_HAND_EMBED=0`
   -> `VRHand<Style>_L/R_rig.fbx` here (same 19-bone contract rig as the glove; the
   default no-env invocation builds the original glove).
   2026-07 fist fix (ALL styles, glove included): finger-tube verts are skinned
   GEOMETRICALLY (arc-length assignment to their own Root/Mid/Tip segment with ±15 %
   smoothstep joint blends; palm/back stays wrist-rigid) instead of the old
   inverse-distance blend that left the palm dragging along ("only fingertips move"),
   and the detected fingertip landmark is converted to an anatomical DIP joint so the
   Tip bone actually owns the distal phalanx (it used to start AT the fingertip —
   the runtime's 65° tip rotation moved ~2 mm of mesh). Verified via
   RIG_HAND_DIAG=1 weight stats + rigid-follow test + the rendered pose matrix.
3. PLATE ONLY, 2026-07 palm fix — the gauntlet's palm shipped with a hard diagonal
   fold and a radial star of flat wedges. Two different defects, in two different
   places, and both were mis-diagnosed as vertex positions for five attempts:
   - `palm_smooth.py <L|R_rig.fbx>` — the FOLD is in the CUSTOM SPLIT NORMALS the FBX
     carries (a fossil of the pre-decimation mesh: 16.3° from auto-smooth on average,
     up to 176°). It rewrites them over the palm and moves NO vertex, so the rig, the
     silhouette and the skin weights cannot change.
   - `palm_repaint.py <L_rig.fbx> <R_rig.fbx> <albedo.png> <out.png>` — the STAR is
     pigment, baked into the albedo off the coarse original. It repaints the palm in
     SURFACE space (3-D blur + 3-D value-noise grain, so the palm's 370 UV islands
     have no seam) and fills the atlas's black inter-island gutters, which is what
     used to bleed into every seam as a dark hairline at mip 1 and beyond. It asserts
     that no texel any other part of the hand samples changed.
   - `palm_leather.py <L_rig.fbx> <R_rig.fbx> <albedo.png> <out.png>` — 2026-07, the
     pass that gives the palm a DESIGN. palm_repaint left it clean and featureless;
     this authors it as a gauntlet's leather palm: a hide panel let into the steel,
     with a rolled steel lip and contact shadow (painted depth — BoardLit is albedo
     only), a stitched seam, ten gold rivets echoing the studs on the back, two flex
     creases and burnished wear on the pads the rig's own landmarks locate. It also
     cleans the MCP transition, which palm_repaint's weight-proportional blend left
     showing 30-100 % of the original dark stipple.
   - `palm_region.py` is the shared definition of "this is the palm" all three use;
     `palm_atlas.py` is the shared machinery (UV rasteriser, gutter fill, the
     proof that nothing outside the palm changed, the rig's landmarks).
   Run in that order, from the pre-repaint atlas:
     `palm_smooth` (once, on each FBX) -> `palm_repaint` -> `palm_leather`.
   TWO THINGS THE 2026-07 PASS FOUND AND FIXED IN THE MACHINERY, both of which had
   silently degraded every earlier attempt:
   - the rasteriser wrote a 3-D position only where a triangle IMPROVED the palm
     weight, and the weight accumulator is shared between L and R — so 63 % of the
     palm (the whole full-weight middle) carried no position at all and every
     surface-space pass found it in one grid cell at the origin. That is *why* the
     repainted palm came out a single flat grey with no tonal variation anywhere.
   - the palm is not uniformly 3 texels/mm. The decimator's big middle triangles have
     badly stretched islands where one texel step spans up to 40 mm of hand, and any
     detail finer than that combs. Every painted feature is now faded out where the
     atlas cannot resolve it, against a per-triangle footprint measured as the top
     singular value of the texel->surface Jacobian (the area ratio hides anisotropy
     and reported 1.7 mm where the truth was 10).
   Verification helpers, all read-only: `hand_audit.py` (mesh/UV/topology stats),
   `render_hand.py` (clay / emission-on-magenta / lit / **game** renders — `game`
   reproduces BoardLit's arithmetic exactly, pure Lambert with no specular, and is
   the only mode that shows what the player actually sees), `count_holes.py`
   (see-through pixels inside the silhouette), `leak_scan.py` (the same over a sphere
   of 42 directions), `palm_crease_metric.py` (gradient energy across the palm plate),
   `compare_fbx.py` (re-import two FBXs and diff them — never trust a script's log).
4. GLOVE ONLY, 2026-07 fist-splay fix — the glove's fist read "weirdly spread", the
   pinky curling off to the side. Cause, measured on the shipped FBX
   (`splay_check.py`): the runtime rotates each joint about its LOCAL +X, and a hinge
   only bends a segment IN A PLANE when its axis is perpendicular to that segment. The
   glove's four finger chains run dead straight along world +Y while its mesh digits
   lean out of that line, so local +X sat **+15.2° (pinky), −10.5° (index), −9.0°
   (thumb)** away from perpendicular to the digit it drives (middle/ring < 1°): those
   fingers swept a CONE, keeping their sideways offset all the way into the fist. The
   Plate rig, which never drew this complaint, is within ±6° because its chains were
   refit to its mesh. The fix is `aim_curl_axes.py` — it re-frames the finger bone
   nodes (local +X := the digit's measured hinge axis, local Y := the digit direction)
   and re-derives the cluster + bind-pose matrices, while **every joint head keeps its
   world position and the mesh/normals/UVs/weights are copied through byte-for-byte**,
   so the accepted open hand renders pixel-identically and only the curl axis moved.
   It is NOT a bone-roll change (roll can only spin the axis within the plane
   perpendicular to the bone, which is why the earlier round called this
   geometrically impossible and compensated one finger at runtime instead — see
   `FingerCurler.DefaultGlovePinkyCounterAbductionDeg`). `verify_aim.py <before> <after>`
   asserts the whole list; `fist_metrics.py` reports the fist the rig actually makes.
   NOTE: none of §1–4 applies to the GLOVE any more. As of ModBuild 169 the glove is
   an artist-authored asset, not a generated one — see the separate pipeline below.
5. `Assets/Editor/BuildHands.cs` assembles all three prefab pairs (BoardLit material,
   per-set loose albedo, `_Cull` per set — see below) and verifies every contract
   bone per prefab.

### Pipeline for the GLOVE (`import_glove_fbx.py`)

The glove arrives as ONE left-hand FBX that already carries a hand-built armature on
the 19-name contract, hand-painted weights and a matching 2048² albedo. The script
therefore *adopts* rather than rigs: re-running `rig_hand.py` against this mesh would
throw all three away for generated equivalents.

```
/home/claw/blender-4.2/blender --background --python unity/hand-prep/import_glove_fbx.py
# GLOVE_SRC=<fbx>  GLOVE_OUT=<dir>  GLOVE_NAME=<base>  to point it elsewhere
```

It does exactly two things the delivered file cannot do for itself:

1. **Promotes `Anchor_IndexTip`.** The contract names nineteen transforms; a rig built
   from the finger chains alone has eighteen. The nineteenth is not a joint but the
   poke point at the end of the index finger, which Blender already writes as the leaf
   `Anchor_Index_Tip_end`. It becomes a real bone, axis-aligned the way the previous
   shipped rig had it (local +Y along the fingers, +X = world +X, so local +Z is the
   palm normal) rather than inheriting the last joint's tilt. Every other `*_end` leaf
   is dropped — no weights, never resolved, one GameObject and one skin bone each.
2. **Mirrors the right hand** as the conjugation `M' = S·M·S`, `S = diag(-1,1,1)`. That
   is a proper rotation, so it keeps each bone's local +X and negates local +Y/+Z —
   which is exactly what makes the SAME positive local-X rotation the runtime applies
   produce the mirror-image tuck. A plain "negate X" would flip the frames' handedness
   and curl the right hand's fingers out of the palm.

Four hard gates run before anything is written, and all four have caught a real defect
in this project before:

| Gate | What it asserts |
|---|---|
| CONTRACT | all 19 names resolve, per hand |
| CURL AXIS | each finger bone's local +X ⟂ its own finger plane, so a curl adds no abduction (the 2026-07 pinky splay). Shipped glove: Index ≤1.8°, Middle ≤0.6°, Ring ≤0.3°, Pinky ≤3.8°; thumb reported only (deliberate 45° tuck) |
| SHELL | signed volume > 0 (closed and wound OUTWARD) and the mirrored hand's volume + UV area equal the left's. Shipped glove: **+297.98 cm³, 0 boundary edges, 0 non-manifold edges** on both hands |
| FLEXION | posing every joint +40° actually pulls the fingertip toward the palm, measured on the EVALUATED mesh — the skinning, not the skeleton |
| MIRROR | the right rig sits at the left's X-negated positions (every other gate above is mirror-invariant and would pass a hand that was never mirrored) |
| ROUND TRIP | the written FBX is read back and compared bone by bone against what was verified — export settings that silently rescale a rig have cost this project a build (`rig_hand.py`'s armature-100× bug) |

**`_Cull` is set from that shell measurement, per set** (`BuildHands.cs` `HandSets`).
`Cull Off` is a repair for fragmented AI shells, not a look: it makes a hole show the
surface behind it instead of a black void, and it costs a second shaded fragment over
the whole hand in both eyes every frame. The glove is closed (0/0 above) so it renders
single-sided; Plate (540 boundary / 1072 non-manifold) and Arcane (869 / 1680) are open
shells and keep `Cull Off`.

Missing styled prefabs (old bundle) degrade to the Glove pair at runtime; no bundle
at all still degrades to the procedural hand.
