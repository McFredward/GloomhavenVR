# Custom hands asset — generation input (what YOU generate, what I do)

Same idea as the control board, but with **one hard difference: hands must move.**
The board is a static prop; a hand needs its fingers to bend, which requires a
**skeleton + skin weights (a rig)**. AI 3D tools (Hunyuan3D, TRELLIS, …) output a
**static mesh only** — so the workflow is: **you generate a static hand mesh → I rig
+ skin it in Blender on clawmachine to the exact skeleton the mod already drives.**

The mod's `FingerCurler` bends 3 joints per finger (`Quaternion.Euler(maxAngle*curl,
0, 0)` on local X). Today it drives the SteamVR glove skeleton; I'll build the same
skeleton into your generated hand.

## What to generate

**Generate ONE hand only** (I mirror it for the other side). Because AI text→3D fuses
fingers badly, the reliable route is **image → 3D**:

1. Make (or pick) a clean **reference image** of a single open hand:
   - **Pose: fingers straight and slightly SPREAD, not curled, not touching** — this
     is the single most important thing. Fused/touching fingers cannot be rigged.
   - Palm flat, hand seen roughly **top-down / orthographic**, wrist cut cleanly at
     the base (no forearm).
   - **A worn leather gauntlet / fingerless glove**, not bare skin — it matches the
     aged-oak + brass board, and a glove hides small mesh flaws far better than skin.
   - Neutral grey background, even lighting, no dramatic perspective.
2. Feed that image to an **image-to-3D** model. Recommended (same tier as the board):
   **Hunyuan3D-2.1** (Replicate/ComfyUI, gives PBR maps) or **TRELLIS**. Text-to-3D is
   NOT reliable for hands.

### Ready-to-use concept-image prompt (Flux / SDXL)
> A single open left hand, palm facing down, fingers held straight and slightly
> spread apart with clear gaps between every finger, thumb extended to the side.
> Wearing a worn dark-brown leather fingerless glove / light gauntlet with brass
> studs and stitched seams, aged fantasy adventurer style. Orthographic top-down
> view, hand fills the frame, wrist cleanly cropped at the base, neutral grey
> background, soft even studio light, no forearm, no second hand. Highly detailed,
> game-asset concept.

(Generate a few, pick the one with the **cleanest finger separation** — that decides
how well it rigs.)

### Hard requirements for the mesh you hand me
- **Fingers physically separated** (visible gaps) — the #1 rig blocker.
- Fingers roughly **straight** (a "T-pose" for the hand), thumb out to the side.
- **One** hand, watertight-ish, no forearm.
- PBR textures welcome (baseColor + normal), like the board.
- Any scale/orientation is fine — I normalise it.

## What I do (clawmachine, Blender + Unity — no work for you)
1. Clean + scale the mesh to a real hand (~18–19 cm), orient to the mod contract
   (root at wrist, **+Z along the fingers, +Y out of the back of the hand**).
2. Build a **15-joint hand armature** and NAME the bones to the mod's rig contract
   (`Anchor_Wrist`, `Anchor_Palm`, `Anchor_{Thumb|Index|Middle|Ring|Pinky}_{Root|
   Mid|Tip}`, `Anchor_IndexTip`, `Anchor_Grab`), positioned at the knuckle lines,
   fingers curling under **positive local-X** rotation (what `FingerCurler` applies).
3. **Skin** the mesh to the armature (automatic weights, then fix the finger creases).
4. Mirror to the right hand, export both, build `VRHand_L.prefab` / `VRHand_R.prefab`
   into `gloomhavenvr.bundle` (loader contract: `unity/GloomhavenVR.Assets/Assets/
   Bundle/Hands/README.md`). The FingerCurler then bends the fingers exactly like the
   SteamVR gloves today.

## Realistic expectations + fallback
- AI hands are the hard case — if the generated fingers are fused or lumpy, automatic
  skinning will deform badly. First remedy: **re-generate with better finger
  separation** (that's why the pose above matters). 
- If a clean mesh proves elusive, the safe fallback is to keep the already-rigged
  **SteamVR glove skeleton/mesh and re-skin it with your AI-generated glove texture**
  (custom look, zero rigging risk). We can decide that after seeing the first mesh.

Hand me the generated hand (GLB/FBX, in `ressources/hands/`) and I'll take it from there.
