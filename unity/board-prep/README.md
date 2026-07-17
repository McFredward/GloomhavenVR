# Control-board 3D asset — from generated GLB to the bundle

You generated `ressources/boards/replicate-prediction-*.glb` (Hunyuan3D via Replicate) from
concept prompt A. It is **usable and on-theme** (aged oak + brass + parchment, full 4K PBR:
baseColor + metallic/roughness + normal). Two things must happen before it loads: **mesh prep**
(Blender, this folder) and **bundle build** (Unity, same as the hands). The mod already probes
`Assets/Bundle/Table/PlayTray.prefab` and falls back to the procedural board if anything is off.

## Verified about the raw GLB
- Geometry: **500,078 tris** (way too dense for VR — decimate) / 278k verts, 1 mesh, 1 material.
- Material: full PBR — baseColor, packed metallic-roughness, normal (3× 4096² PNG).
- Bounds: **1.097 × 0.547 × 0.056 units** → aspect already ~2:1, so scaling the long edge to
  **0.64 m** gives ~**0.32 m** depth automatically (exactly the contract footprint).
- Y-up node rotation present (standard glTF) — the prep script normalizes it.

## Step 1 — Blender prep (~2 min)
```
blender --background --python unity/board-prep/prepare_playtray.py -- \
    "ressources/boards/replicate-prediction-mvxgmjp9ehrmy0cze7xrkq87hg.glb" \
    "unity/board-prep/PlayTray_prepped.glb"
```
This decimates to ~20k tris, scales to 0.64 × 0.32 m, orients (board in XY, decorated face toward
−Z, body z>0, pivot centred), downscales textures to 2K, and adds the six named empty anchors
(`Slot1 Slot2 ShortRestToken LongRestToken ConfirmButton UndoButton`).

**Eyeball once:** open `PlayTray_prepped.glb` in Blender. The decorated top face should point −Z
(toward the viewer when the board is later tilted up). If it points the wrong way, set
`FLIP_TOP_FACE = True` (or `UPRIGHT_FROM_YUP = False` if the board came in already Z-up) at the top
of the script and re-run. Then drag each anchor empty so it sits over the matching painted recess
(the two card slots, the rest pads, the button pads) — the mod finds them by name, position is up to you.

## Step 2 — Unity bundle build (same as the hands, `docs/ANLEITUNG-HAENDE.md`)
> Unity 2021.3 does NOT import `.glb` natively and the companion project has no glTF importer, so use
> the **`.fbx`** the prep script now also writes (`PlayTray_prepped.fbx` + its textures) — the same
> native-FBX path the hands use. (Alternative: add the `glTFast` package via Package Manager and use
> the .glb; FBX is the proven route.)
1. Import **`PlayTray_prepped.fbx`** (and the textures written next to it) into
   `unity/GloomhavenVR.Assets/Assets/Bundle/Table/`.
2. Drag it into a scene. **Orientation — VERIFIED from the exported FBX (`PlayTray_prepped.fbx`):**
   the board imports lying flat (horizontal, thin axis = Y, decorated face −Y). The mod homes each
   card onto its slot anchor with **identity** local rotation, so the board must sit in the prefab's
   **local XY plane with the decorated face toward −Z**. Apply exactly this: set the imported model's
   **Rotation X = +90°** (Euler `90, 0, 0`). That stands the board into XY with the nice face toward
   −Z and all six anchors on the z=0 card plane (slots centre, rest left, buttons right — checked).
   **If the nice face ends up pointing AWAY from you, use X = −90° instead** (the one face-side the
   geometry can't disambiguate). Wrap the model under an empty named **`PlayTray`** and save it as the
   prefab **`Assets/Bundle/Table/PlayTray.prefab`** (exact name); confirm the six anchor empties are
   child transforms with the exact names. **Definitive test after building:** the two cards sit flat
   in the slots, face-up toward you.
3. **Material/shader (avoid the pink trap):** the imported material must use a shader that ships in
   the bundle. Easiest reliable options:
   - Assign the mod's bundled lit/unlit board shader if present, OR
   - Use Unity's **Standard** shader but add it to **Project Settings → Graphics → Always Included
     Shaders** so its variants ship (the built-in game is BiRP). Plug baseColor→Albedo,
     metallic-roughness→Metallic (glTF packs metal=B, rough=G), normal→Normal Map.
   - If the diorama lighting makes it read dark, an **unlit** shader showing the baseColor is the
     safe fallback (the AI albedo already carries baked detail) — matches how the hands avoid the
     no-lights black-out.
4. Select the prefab (and its GLB/textures) → Inspector bottom → **AssetBundle = `gloomhavenvr`**.
5. Menu **GloomhavenVR → Build AssetBundles** → `Build/Bundles/gloomhavenvr.bundle`.
6. Copy to `…/Gloomhaven/BepInEx/plugins/GloomhavenVR/gloomhavenvr.bundle`. Start the game; the log
   shows the tray visual loading from the bundle (else it silently stayed procedural — recheck the
   prefab path/name and that Slot1/Slot2 exist).

## If you'd rather I do more
- Hand me the **`PlayTray_prepped.glb`** (or any decimated + 0.64 m-scaled GLB) and I'll verify the
  contract (scale/orientation/anchors/material) and write you the exact per-click Unity prefab setup.
- I can't run Unity or Blender in my environment, so the bundle **build** is always your side — but
  everything up to it I can check/prepare.

## Reuse
The same pipeline later produces `CardBacking.prefab` and the button prefabs (bundle README lists
them). See `.planning/research/CONTROL-BOARD-ASSET.md` for the full generation guide + CC0 fallbacks.
