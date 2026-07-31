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
2b. `refine_shell.py` (2026-07, PLATE applied) — post-process on the exported
   `*_rig.fbx`, rerunnable: `blender -b -P unity/hand-prep/refine_shell.py -- <fbx>`.
   The collapse decimator in step 1 is curvature-driven and strips the flattest regions
   hardest, so the armoured gauntlet shipped its PALM PLATE and CUFF BAND as a handful of
   giant smooth-shaded triangles (measured on `VRHandPlate_R_rig.fbx`: single facets with
   70.6 / 63.6 / 54.0 mm edges on a 190 mm hand, mean edge 5.8 mm) — visible triangle
   creases in-headset, plus a texture that reads as stretched because the affine UV map
   spreads those facets' texels 4-15x thinner than the neighbouring shell
   (uvArea/faceArea 0.16-0.66 vs a region median of 2.41). The script adaptively
   subdivides only the coarse patches (linear split of every non-boundary edge over 8 mm)
   and rounds them with a coarseness-weighted Taubin low-pass, leaving fine detail,
   the open wrist rim, the silhouette and the 19-bone contract untouched.
   Plate: 9 660 -> 26 659 tris, longest edge 89.7 -> 24.1 mm, all vertices keep a
   normalised weight set. **Run `scripts/build-bundles.sh` afterwards** — the mod loads
   the hands from `gloomhavenvr.bundle`, so an edited FBX alone changes nothing.
2c. `palm_reunwrap.py` + `palm_paint.py` (2026-07, PLATE only) — the palm's "star" of flat
   wedges was **paint, not polygons**: 2b fixed the geometry (a clay render of the palm has
   no star at all) but the albedo had been baked from the ORIGINAL coarse mesh, so each of
   the giant palm triangles had one near-flat colour burned into it. An emission-only
   render — no lights, every pixel literally a texel — showed the star at full strength.
   Measured on `VRHandPlate_R_rig.fbx`: hand median 3.24 texels/mm, the palm patch
   1.07 texels/mm — 13 % of the surface living on 0.58 % of the atlas, shredded into
   3355 UV islands, ~10 texels per face. Nothing can be filtered out of ten texels, and
   two wedges that touch on the hand can sit anywhere in the atlas, so no image-space
   repair (blur, gain, luma match, per-face flatten) can even see them together.
   The fix gives the palm real texel density:
   ```
   blender -b -P unity/hand-prep/palm_reunwrap.py -- \
       Assets/Bundle/Hands/VRHandPlate_R_rig.fbx out_R.fbx R.npz --origin 0.52 0.02
   blender -b -P unity/hand-prep/palm_reunwrap.py -- \
       Assets/Bundle/Hands/VRHandPlate_L_rig.fbx out_L.fbx L.npz --origin 0.02 0.52
   python3 unity/hand-prep/palm_paint.py \
       Assets/Bundle/Hands/VRHandPlate_albedo.png out_4096.png R.npz L.npz --gain 1.1
   ```
   - The palm PLATE is selected geometrically, not by a density threshold (a threshold
     picks only the worst faces and leaks onto the back of the hand — that regression
     shipped once): flood-fill from a starved seed across faces that face the palm, sit
     above the cuff rim and are not skinned to a finger bone. The rig's own weights draw
     the boundary, so it lands on the MCP creases and the cuff rim. 2 675 faces, 28 983 mm².
   - The atlas grows 2048 -> 4096 and every OLD uv is multiplied by 0.5, so the old image
     sits 1:1 in the bottom-left quadrant and every untouched island samples the same
     texels at the same mip (0.5·du · 4096 == du · 2048). Nothing outside the palm is
     resampled; both scripts assert it (uv exactly halved, quadrant bit-identical).
   - The palm is unwrapped as ONE island at 4.0 texels/mm (median 3.91, p5 2.08) and
     filled with the old palm colour blurred σ≈30 mm **in island space** — the step no
     earlier attempt could take — plus two octaves of luminance high-pass grain quilted
     from the atlas's own fully-covered plate areas. `png.meta maxTextureSize` must be
     4096 (bundle cost: 2.7 -> 10.7 MB for this texture).
   **Rebuild the bundle afterwards with the game-exact editor** (`/home/claw/unity-2021.3.5`,
   NOT 2021.3.45 — that writes UnityFS format 8, which the game cannot read) and re-run
   `scripts/check-bundle-format.sh`.
3. `Assets/Editor/BuildHands.cs` assembles all three prefab pairs (BoardLit material,
   `_Cull Off`, per-set loose albedo) and verifies every contract bone per prefab.

Missing styled prefabs (old bundle) degrade to the Glove pair at runtime; no bundle
at all still degrades to the procedural hand.
