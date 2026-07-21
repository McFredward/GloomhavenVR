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
   default no-env invocation still builds the original glove unchanged).
3. `Assets/Editor/BuildHands.cs` assembles all three prefab pairs (BoardLit material,
   `_Cull Off`, per-set loose albedo) and verifies every contract bone per prefab.

Missing styled prefabs (old bundle) degrade to the Glove pair at runtime; no bundle
at all still degrades to the procedural hand.
