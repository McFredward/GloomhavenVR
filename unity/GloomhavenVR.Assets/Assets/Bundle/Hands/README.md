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
| Glove (default) | `VRHand_L/R.prefab` | original leather glove (`Hand_prepped.glb`, hardcoded joints in `rig_hand.py`) |
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
   - `palm_region.py` is the shared definition of "this is the palm" both use.
   Verification helpers, all read-only: `hand_audit.py` (mesh/UV/topology stats),
   `render_hand.py` (clay / emission-on-magenta / lit renders), `count_holes.py`
   (see-through pixels inside the silhouette), `leak_scan.py` (the same over a sphere
   of 42 directions), `palm_crease_metric.py` (gradient energy across the palm plate),
   `compare_fbx.py` (re-import two FBXs and diff them — never trust a script's log).
4. `Assets/Editor/BuildHands.cs` assembles all three prefab pairs (BoardLit material,
   `_Cull Off`, per-set loose albedo) and verifies every contract bone per prefab.

Missing styled prefabs (old bundle) degrade to the Glove pair at runtime; no bundle
at all still degrades to the procedural hand.
