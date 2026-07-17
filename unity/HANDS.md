# Hand models — sourcing plan

> Decision: ship the **SteamVR Unity Plugin glove hands** (BSD-3-Clause,
> GPL-3.0-compatible) in `gloomhavenvr.bundle`. Verified 2026-07 against the
> primary sources below.
>
> **Interim (shipping NOW, hardware test #13):** upgraded **procedural hands**
> (see §0) — the bundle step needs the human's Windows machine, and no
> runtime-loadable alternative passed the license/feasibility check.

## 0. Runtime (no-bundle) options — researched 2026-07, test #13

The bundle build is a human/Windows step, so we evaluated what can be
integrated at RUNTIME from the plugin folder, honestly:

1. **Static-pose rigid hand meshes (OBJ/glTF) split into per-segment parts**,
   parented to the existing `HandRig` finger joints. A minimal OBJ loader is
   ~100 lines and would slot into `HandVisuals.Build` cleanly. **Blocked on
   assets, not code:** searches across Kenney, Quaternius, OpenGameArt,
   BlendSwap, Sketchfab (2026-07) found **no CC0/permissive hand mesh that is
   already split at the phalanx joints**. What exists is either (a) monolithic
   static hands — cannot curl fingers, worse than what we have — or
   (b) skinned/rigged hands (`.blend`/FBX), which require Unity's asset
   pipeline (SkinnedMeshRenderer + serialized bind poses), i.e. the bundle
   path. Splitting a monolithic mesh at the joints is offline mesh surgery
   (Blender), which is exactly the human step we cannot take here.
   Candidates checked: BlendSwap "Low Poly Hand (Rigged)" (CC0, `.blend`,
   rigged → bundle path), Quaternius packs (CC0, no standalone hand),
   Kenney (CC0, no hand mesh), Sketchfab low-poly hands (mixed licenses,
   monolithic).
2. **Markedly better procedural hands — CHOSEN, shipped in
   `Hands/HandVisuals.cs`:** rounded palm via bevel-approximation stacking
   (two interpenetrating chamfer slabs + knuckle-ridge capsule + thenar mound
   + wrist heel), per-finger radii with per-segment taper, joint spheres for
   continuous knuckle bends, squashed fingertip caps and lighter-tinted nail
   hints. No assets, no licenses, hot-reload safe, zero per-frame cost, and
   the frozen `HandRig` joint contract is untouched.

**Upgrade path stays §1 below** (SteamVR gloves via `gloomhavenvr.bundle`,
already contracted): skinned gloves + direct bone rotation replace the
procedural visuals with zero code changes on the rig-consumer side — the
loader already probes the bundle first and falls back to procedural.

## 1. Primary source: SteamVR Unity Plugin hands

- **Repo:** https://github.com/ValveSoftware/steamvr_unity_plugin
- **License:** **BSD-3-Clause** — verified at
  https://github.com/ValveSoftware/steamvr_unity_plugin/blob/master/LICENSE
  (raw: https://raw.githubusercontent.com/ValveSoftware/steamvr_unity_plugin/master/LICENSE).
  First line: *"Copyright (c) Valve Corporation. All rights reserved."*
  followed by the standard three BSD clauses (source-notice retention,
  binary-notice reproduction, no-endorsement). The license sits at repo root
  and covers the plugin including its `Assets/SteamVR/Models` art; no
  per-asset license overrides exist in the repo.
  **BSD-3 → GPL-3.0 is one-way compatible** (our mod is GPL-3.0,
  ARCHITECTURE.md §9); obligations: keep Valve's copyright notice + license
  text with the assets and in release archives.
- **Latest release:** v2.8.0 (2024-01-15), asset `steamvr_2_8_0.unitypackage`
  — https://github.com/ValveSoftware/steamvr_unity_plugin/releases/tag/2.8.0

### Exact files to take (paths verified against the master git tree)

Models & rig (`Assets/SteamVR/Models/`):

| Path | What |
|---|---|
| `Assets/SteamVR/Models/vr_glove_left_model_slim.fbx` | **Left glove, skinned, low-poly — the one to ship** |
| `Assets/SteamVR/Models/vr_glove_right_model_slim.fbx` | **Right glove — the one to ship** |
| `Assets/SteamVR/Models/vr_glove_model.fbx` | full-detail left glove (higher poly; optional) |
| `Assets/SteamVR/Models/vr_hand_grabposes.fbx` | hand with grab-pose animation clips (pose reference) |
| `Assets/SteamVR/Models/vr_glove_graspPoses.controller` | AnimatorController for the grasp poses (optional — we drive bones directly) |

Materials & textures (`Assets/SteamVR/Models/Materials/`):

| Path | What |
|---|---|
| `Assets/SteamVR/Models/Materials/vr_glove_color.mat` | glove material (built-in **Standard** shader — see shader note) |
| `Assets/SteamVR/Models/Materials/vr_glove_color.jpg` | albedo |
| `Assets/SteamVR/Models/Materials/vr_glove_normal.png` | normal map |
| `Assets/SteamVR/Models/Materials/vr_glove_color_red.mat` / `.jpg` | red variant (optional, e.g. hover tint) |

Do **NOT** import (SteamVR-script-dependent, not needed):

- `Assets/SteamVR/InteractionSystem/Core/Prefabs/LeftRenderModel Slim.prefab`
  / `RightRenderModel Slim.prefab` — reference Valve `RenderModel`/`Hand`
  MonoBehaviours we don't ship (would import as missing scripts).
- `Assets/SteamVR/InteractionSystem/Poses/fallback_{relaxed,point,fist}.asset`
  — `SteamVR_Skeleton_Pose` ScriptableObjects, depend on Valve scripts. Open
  them in a scratch project as **pose value reference** for our FingerCurler
  defaults if useful, but don't ship the assets.
- Anything under `Assets/SteamVR/Input`, `Assets/SteamVR/Scripts`, plugins/
  OpenVR binaries.

### Import procedure (companion project)

1. Download `steamvr_2_8_0.unitypackage` from the 2.8.0 release (or
   `git clone --depth 1 https://github.com/ValveSoftware/steamvr_unity_plugin`
   and copy files — cleaner, avoids the import dialog entirely).
2. If using the unitypackage: Assets → Import Package → Custom Package →
   **deselect all**, then tick only the Models/Materials files listed above.
   If using the clone: copy the files (with their `.meta`s, keeps FBX→material
   GUID wiring) into
   `unity/GloomhavenVR.Assets/Assets/Bundle/Hands/Valve/`.
3. Copy the repo-root `LICENSE` file to
   `Assets/Bundle/Hands/Valve/LICENSE-Valve.txt` (attribution requirement;
   the bundle builder excludes `.txt` from the bundle but it stays in git and
   goes into release zips).
4. FBX import settings: Rig → **Generic** (no humanoid mapping needed),
   Read/Write off, no animation import for the slim gloves.
5. Build our own `VRHand_L.prefab` / `VRHand_R.prefab` on top (canonical names the
   mod probes — see `Assets/Bundle/Hands/README.md` for the full loader contract):
   glove mesh + bone hierarchy exposed. Finger animation is **direct bone rotation**
   (LCVR `FingerCurler` pattern — 2 bones per finger, trigger→index,
   grip→middle/ring/pinky, thumb-touch→thumb; VR-PRIOR-ART.md §4). No Animator
   required; the SteamVR skeleton bone naming
   (`wrist_r` → `finger_index_meta_r` → `finger_index_0_r` …) maps 1:1.

### Shader note (pink-material trap)

`vr_glove_color.mat` uses built-in **Standard**, which is never included in
AssetBundles — at load it resolves against the *game's* Standard with only the
variants Gloomhaven didn't strip (TOOLCHAIN.md §4.1). Mitigations, in order:
1. Runtime: rebind the glove material to one of the game's own loaded
   materials/shaders (guaranteed variants, visually consistent).
2. Or replace with a small self-contained lit shader shipped in the bundle.
Keep the textures either way.

## 2. Fallback options (if the gloves disappoint)

| Option | License | Verdict |
|---|---|---|
| **Meta/Oculus sample hands** (`OVRHandPrefab`, Interaction SDK hands) | [Oculus SDK License](https://developers.meta.com/horizon/licenses/oculussdk/) | **Unusable.** Verified 2026-07: the agreement forbids using the SDK "in any manner that would cause the SDK (or any portion thereof) … to become subject to the terms of any open source license", and redistribution is limited to designated sample code with Meta copyright headers. Do not ship in a GPL mod. |
| [RoboHands-UnityXR](https://github.com/InfernoDigital/RoboHands-UnityXR) | check repo license file before use | stylized robot hands; acceptable aesthetic fallback if its license text checks out (VR-PRIOR-ART flagged it "free/open" but verify per-file) |
| CC0 rigged hands (Sketchfab/Quaternius/Poly Haven searches) | CC0 | no specific vetted asset found during research; requires per-asset license verification and usually re-rigging — budget hours, not minutes |
| **Author own low-poly hands in Blender** | ours (GPL) | guaranteed clean; a simple mitten→5-finger glove with 15 bones is ~0.5–1 day; solid plan C |
| Simple "floating glove" primitives (capsule palm + finger capsules built in-editor) | ours | zero-risk placeholder so Phase 2 never blocks on art |

## 3. Redistribution checklist (release zip)

- [ ] `gloomhavenvr.bundle` contains the glove meshes/textures.
- [ ] `LICENSE-Valve.txt` (BSD-3 text incl. Valve copyright line) present in
      the release archive and referenced from the README credits section.
- [ ] Mod's own GPL-3.0 `LICENSE` unaffected (BSD-3 content aggregated, noted
      in credits).
- [ ] No Oculus/Meta SDK content anywhere in the repo or bundle.
