# CONTROL-BOARD-ASSET — a real artful 3D PlayTray for the bundle (research, 2026-07-17)

Goal: replace the current **procedural** control board (`PlayTray.BuildProceduralBoard`)
with a **beautiful themed 3D mesh + PBR textures**, loaded through the mod's existing
AssetBundle pipeline — exactly like the hands. The mod keeps building the interactive
bits (buttons, tokens, canvases, labels) at runtime; the asset is a **cosmetic themed
board body** with slot recesses, a rest zone and button pads. This doc covers (1) the
hard integration contract, (2) a recommended creation path, (3) ready-to-paste
generation input, (4) the deliverable spec, (5) a CC0 fallback, (6) the forward pointer
to the other bundle assets.

The user has ComfyUI and explicitly asked about open-source models and ComfyUI flows.

---

## 1. Integration contract — HARD requirements (the asset MUST satisfy these)

Grounded in `unity/GloomhavenVR.Assets/Assets/Bundle/Table/README.md` and the runtime
consumer `src/GloomhavenVR/Cards/{VRCardFactory,PlayTray}.cs`. If any of these is wrong,
the mesh either fails to load or silently falls back to the procedural slab.

**Load / packing**
- [ ] Prefab bundle path is **exactly** `Assets/Bundle/Table/PlayTray.prefab`
      (`VRCardFactory.TrayAssetPaths`, line 25-28). Loaded by name from
      `gloomhavenvr.bundle` (`BundleFileName`, line 20); same bundle the hands ship in.
- [ ] The whole asset (mesh, textures, materials, prefab) lives under
      `unity/GloomhavenVR.Assets/Assets/Bundle/Table/`. Everything there except
      `*.md`/`*.txt`/dotfiles is packed by `Assets/Editor/BuildBundles.cs`.
- [ ] Build with **Unity 2021.3.45f2**, companion project `unity/GloomhavenVR.Assets`,
      menu **GloomhavenVR → Build AssetBundles** → copy `gloomhavenvr.bundle` to
      `BepInEx/plugins/GloomhavenVR/` (same flow as `unity/HANDS.md`).

**Scale — REAL WORLD, 1 Unity unit = 1 m**
- [ ] Board footprint **~0.64 m × 0.32 m** (2:1). This is the desk/lectern the player
      leans a hand of cards on. The rig does the diorama scaling — author at real size,
      do **not** pre-scale.
- [ ] Poker-card slots must physically fit a 63.5 × 88 mm card resting flat.

**Pivot / orientation (this is the #1 thing people get wrong)**
- [ ] Pivot at the **center of the board**.
- [ ] Board authored **lying flat in the local XY plane**; board **body at local z > 0**
      (behind the interactive elements). Slot/token anchors sit at the card resting plane
      `z = 0`.
- [ ] **+Z points AWAY from the player**; the HMD is always on the **−Z side**. At runtime
      the mod tilts the whole root to `[Cards] TrayTilt` (default **30°**) from horizontal,
      with **−Z tilting up toward the player**. So the face the player reads is the **−Z
      face** — put the nice top surface, engravings and slot recesses facing **−Z / up**.
- [ ] Y is the board's short axis (the 0.32 m dimension) in local space before tilt.

**Required named child transforms (looked up BY NAME via `FindDeep`, `PlayTray.cs`
324-329) — case-sensitive, must be descendants of the prefab root:**
- [ ] `Slot1` — **initiative** card slot. **LEFT** from the player's view. Marks the card
      **CENTER** at the resting `z = 0` plane. Gets a numbered badge + "INITIATIVE" caption
      at runtime. A shallow recess/inlay here reads best.
- [ ] `Slot2` — second card slot, **RIGHT**. Same convention.
- [ ] `ShortRestToken` — anchor in a **visually separated rest zone (left edge
      recommended)**. Gets a pokeable token widget at runtime — keep **~5 cm clearance**
      around it (no geometry intruding).
- [ ] `LongRestToken` — second rest anchor, same rest zone, same clearance.
- [ ] `ConfirmButton` *(optional)* — anchor for the physical confirm button, **right edge
      recommended**. The mod builds base + travelling cap + label itself, so this is a
      **plain empty transform** — a flat "button pad" area in the mesh under it is enough.
      Suggested built button footprint ~11 × 6 cm.
- [ ] `UndoButton` *(optional)* — anchor for undo, right zone, ~9 × 4 cm built footprint.
      Empty transform; procedural anchors are created if these two are missing.

> The mesh is therefore: **themed board body + two slot recesses (left=initiative,
> right) + a separated rest zone on the left + button pads on the right.** It carries NO
> functional buttons, NO text, NO tokens — the mod adds all of those. Don't model buttons
> that move; don't paint slot labels (runtime TMP covers them). Leave the −Z card-face
> footprints clean.

**Materials / shaders (pink-material trap — `TOOLCHAIN.md §4.1`)**
- [ ] Materials must use a **custom/URP-or-unlit shader that compiles INTO the bundle**.
      Do **NOT** reference built-in **Standard** or **UI** shaders — those are not included
      in AssetBundles and resolve pink at runtime. (Same trap the hands doc calls out for
      `vr_glove_color.mat`.) Ship a small self-contained lit shader in the bundle, or bind
      to one of the game's own loaded shaders at runtime. Keep the textures either way.
- [ ] Prefer **1 material** (single PBR set), 2-3 max. Fewer draw calls; simpler shader
      bundling. An atlas is ideal.

**Poly budget (VR, seated dashboard, one on screen)**
- [ ] Target **~5k–20k triangles** for the board body. It is a hero-ish but static prop
      viewed close; there is exactly one, so up to ~30k is acceptable if it buys nice
      bevels/carvings. Avoid the 100k+ raw marching-cubes output of AI tools — **retopo /
      decimate** before bundling (see §3).
- [ ] Clean **UVs** required (for the PBR maps). Triangulated on export.

**License** (project stance, `unity/CARD-ASSETS.md`): the mod is **GPL-3.0**; anything
shipped in the bundle must be redistributable — **CC0 preferred, CC-BY acceptable with a
credits line**. AI-generated meshes from the open models below are yours to license (no
attribution owed on the geometry itself; check each model's *weights* license for any
commercial clause — Hunyuan3D and TRELLIS both permit this use, see §2).

---

## 2. Recommended path

**Primary recommendation: AI-generate the board in ComfyUI — concept image (Flux) →
image-to-3D (Hunyuan3D-2.1) → PBR paint → export GLB → retopo/anchor in Blender →
author the Unity prefab.**

Why AI-generate rather than kitbash CC0 here:
- The board is a **bespoke themed hero prop** with a very specific layout (two slot
  recesses + rest zone + button pads at Gloomhaven grimdark-fantasy styling). No CC0
  asset matches that layout — you'd be modeling it anyway. AI gets you a unique,
  on-theme, textured base in minutes, which you then trim to the contract.
- The user **already runs ComfyUI** and asked for exactly this.
- Image-to-3D in 2026 produces **PBR-textured** output directly (albedo + metallic +
  roughness + normal), which is what the bundle needs.

**Top ComfyUI 3D tool to use in 2026: Hunyuan3D-2.1** (Tencent), via the
`ComfyUI-Hunyuan3d-2-1` wrapper. Rationale:
- It is the **best current open-source choice for high-fidelity PBR** output — it is a
  fully open framework whose headline feature is **Physically-Based Rendering texture
  synthesis** (separate albedo / metallic / roughness maps, up to 8K), which maps 1:1
  onto what a Unity PBR material wants. ([Yuan-ManX node](https://github.com/Yuan-ManX/ComfyUI-Hunyuan3D-2.1),
  [visualbruno wrapper](https://github.com/visualbruno/ComfyUI-Hunyuan3d-2-1))
- Runs on **consumer VRAM**: shape ~6 GB, shape+texture ~12 GB (mini variant ~5 GB);
  Standard wants 12-16 GB. Comfortable on a single 16 GB+ card.
  ([Hunyuan3D-2 VRAM notes](https://www.triposrai.com/tools/hunyuan3d-free-online/))
- Mature, well-documented ComfyUI path with a dedicated **Texture Synthesis** node
  ([HunyuanImage/3D docs](https://docs.comfy.org/tutorials/3d/hunyuan3D-2),
  [texture node doc](https://comfyai.run/documentation/Hunyuan3DTexureSynthsis)),
  and a game-dev-oriented node bundle ([comfyui-ai-gamedev](https://github.com/mattwilliamson/comfyui-ai-gamedev)).

**Strong alternative: Microsoft TRELLIS 2** (released Dec 2025, 4B params). It won ~68%
of head-to-head visual-quality comparisons vs Hunyuan3D's 24%, needs **no manual retopo
or UV unwrap**, and exports GLB with PBR directly; community builds run it on **8 GB**
(low-VRAM 512³ mode), 16 GB+ for 1024³.
([TRELLIS 2 vs others](https://trellis2.app/blog/best-ai-3d-model-generator),
[ComfyUI-Trellis2 wrapper](https://github.com/visualbruno/ComfyUI-Trellis2),
[install guide](https://trellis2.app/blog/trellis-2-comfyui)). Use TRELLIS 2 if you
value cleaner topology/UVs out of the box; use **Hunyuan3D-2.1 if you want the strongest
control over PBR maps** for a Unity material — for this board I lean Hunyuan3D-2.1 for
the PBR fidelity, with TRELLIS 2 as the swap if its topology comes out cleaner on your
prompt.

**Also current, note but don't lead with:** **Hunyuan3D 3.0** (announced in ComfyUI Feb
2026) runs via **Partner/API nodes** and template workflows — text/image/multi-view,
"professional UVs and PBR", but semantic UV unwrapping is still "coming soon" and it's
not the fully-local open-weights path; use it only if you want the newest fidelity and
accept the partner-node route ([comfy.org blog](https://blog.comfy.org/p/hunyuan-3d-30-in-comfyui-state-of)).
Older/lighter options **TripoSR / Stable Fast 3D** are fast but geometry-only or
lower-fidelity — not ideal for a hero prop.

**Reality check:** every AI tool emits **dense, non-game topology** (marching-cubes
soup, no anchors, arbitrary scale/orientation). The board is small and geometrically
simple, so the **cheapest total path might actually be: AI-generate for the *texture &
silhouette*, then rebuild a clean low-poly board in Blender and bake the AI textures
onto it.** Either way there is a mandatory **Blender finishing step** — that's where you
add the named anchors, fix scale to 0.64 × 0.32 m, and orient +Z. Budget it.

---

## 3. Concrete generation INPUT (ready to paste)

### 3a. Concept-art prompts (Flux.1/Flux.2 in ComfyUI — top-down hero shot of the board)

Flux differs from SDXL: **no negative prompt, different CFG behavior — start from a
Flux-specific template**, don't port an SDXL graph
([Flux ComfyUI guide](https://www.thundercompute.com/blog/flux-comfyui-ai-image-generation)).
Render on a **plain neutral background**, single object, even lighting — image-to-3D
tools want a clean isolated subject. A slight 3/4 top-down angle reconstructs better than
a flat orthographic top.

> **Prompt A (main — ornate wooden tray):**
> "A single ornate fantasy tabletop player control board, aged dark oak wood with visible
> grain and worn edges, rectangular 2:1 desk shape, TWO shallow rectangular card-slot
> recesses inset into the top surface side by side, a separate small tray zone on the left
> with two round recessed pads, two flat blank button pads on the right, tarnished brass
> corner fittings and rivets, faint engraved arcane runes and a weathered parchment inlay,
> grimdark heroic-fantasy board-game aesthetic, muted earthy palette, physically based
> materials, soft even studio lighting, plain flat grey background, centered, slight
> top-down three-quarter view, product shot, high detail, no text, no cards, no tokens"

> **Prompt B (stone + iron lectern variant):**
> "A rugged fantasy adventurer's field lectern / control board, dark weathered wood banded
> with black wrought iron, two recessed card slots, a rune-etched slate rest panel on the
> left, two blank iron button plates on the right, riveted metal edges, dungeon-crawler
> grimdark theme, worn leather trim, PBR materials, neutral background, centered, gentle
> top-down angle, clean product render, no text, no lettering, empty slots"

> **Prompt C (arcane brass + parchment variant):**
> "An ornate arcane player dashboard board, polished aged brass frame around a dark
> stained wood core, two inset parchment-lined card recesses, an engraved rune circle rest
> area on the left side, two blank pressure-plate button pads on the right, filigree
> etching, candle-soot patina, tabletop RPG grimdark fantasy, physically-based textures,
> isolated on plain background, soft frontal-top lighting, no glyphs of text, empty tray"

Tips: generate 4-8, pick the one whose **layout already reads left-slots/right-buttons**
(saves Blender work); keep the two card recesses clearly rectangular and empty. Optional:
a **multi-view / orthographic ControlNet+LoRA** pass (front/back/side) improves 3D
reconstruction — Hunyuan3D-2.1 and TRELLIS 2 both accept **2-4 views** to sharpen
geometry ([orthographic view workflow](https://comfyui.org/en/high-quality-game-character-art-workflow)).

### 3b. ComfyUI pipeline (Hunyuan3D-2.1 path)

**Install / download once:**
1. ComfyUI + ComfyUI-Manager. Install the **`ComfyUI-Hunyuan3d-2-1`** custom node
   (visualbruno) or **`ComfyUI-Hunyuan3D-2.1`** (Yuan-ManX). Portable install:
   `python_embeded\python.exe -m pip install -r ComfyUI\custom_nodes\ComfyUI-Hunyuan3d-2-1\requirements.txt`
   — plus two C++ extensions (custom rasterizer + differentiable renderer) via the
   provided precompiled wheels.
2. Download checkpoints from HuggingFace into the node's model folder:
   `hunyuan3d-dit-v2-1.ckpt` (shape diffusion) and `hunyuan3d-vae-v2-1.ckpt` (VAE).
   Add the texture/paint model files per the node's README.
3. For concept art: a Flux checkpoint (Flux.1-dev or Flux.2-Klein) + VAE/clip in the
   usual ComfyUI folders.

**Node graph outline:**
```
[Flux txt2img: prompt A/B/C] --> board concept PNG (clean bg)
        |
        v
[Load Image] -> [Background Remove / RMBG] -> clean RGBA subject
        |
        v
[Hunyuan3D-2.1 Shape / Image-to-Mesh node]  -> raw mesh (dense, untextured)
        |
        v
[Hunyuan3D Texture Synthesis node]          -> PBR maps (albedo/metallic/roughness[/normal])
        |
        v
[Mesh export: GLB]  (also OBJ if you want)  -> board_raw.glb with textures
```
(TRELLIS 2 swap: replace the two Hunyuan nodes with **TRELLIS Mesh Generator → TRELLIS
Texture Generator → GLB export**; same input image; low-VRAM mode = 512³.)

**Blender finishing step (mandatory — this is where the contract is met):**
1. Import `board_raw.glb`. **Retopo/decimate** the body to ~5k-20k tris (Decimate
   modifier or a quad remesh), keep clean UVs; re-bake the AI PBR maps onto the retopo
   mesh if you remeshed.
2. **Set real scale:** longest edge = **0.64 m** (Blender is metre-native; make the
   footprint 0.64 × 0.32). Apply scale.
3. **Orient:** board lying in XY, body at **z > 0**, and the **decorated top face pointing
   −Z** (that face tilts up toward the player at runtime). Apply rotation. Pivot at board
   center.
4. **Add empty child objects at the anchor points**, named EXACTLY: `Slot1` (left card
   recess center), `Slot2` (right recess center), `ShortRestToken` + `LongRestToken`
   (left rest zone, ~5 cm apart/clear), `ConfirmButton` + `UndoButton` (right pads).
   Put each at the **card resting plane z = 0**. (You can also add these in Unity on the
   imported prefab instead — either is fine; the mod finds them by name anywhere under
   the root.)
5. Export **GLB** (or FBX), **+Y up / -Z forward**, **triangulated**, with the anchor
   empties included, textures embedded/exported alongside.

### 3c. Export settings summary
- Format: **GLB** (preferred; PBR-in-one-file, imports clean to Unity 2021.3) or FBX.
- Scale: **metres, 0.64 × 0.32 m** footprint, apply-transforms.
- Orientation: board in local XY, body z>0, decorated face toward −Z, pivot centered.
- Mesh: **triangulated**, ~5k-20k tris, single UV set, no n-gons.
- Maps: **albedo (baseColor), metallic, roughness, normal** (+ AO optional). 2K is plenty
  for one board; 4K if you want. Pack metallic/roughness as the glTF metalRough channel.
- Anchors: 6 named empties (`Slot1`,`Slot2`,`ShortRestToken`,`LongRestToken`,
  `ConfirmButton`,`UndoButton`) at z=0.

---

## 4. Deliverable spec — what to hand back (pick one)

All three land the same result; they differ in how much of the Unity/Blender finishing
**you** do vs **I** do. In every case the mod code is untouched (it already probes the
bundle and falls back procedurally).

**Option A — hand me the concept image only (least work for you).**
Give me the chosen **Flux PNG** (clean background, on-theme, left-slots/right-buttons
layout). I run image-to-3D + PBR + Blender retopo, set scale/orientation, add the anchors,
author `PlayTray.prefab`, and build the bundle. Fastest for you; I own the 3D step.

**Option B — hand me the raw textured mesh (middle).**
Give me `board_raw.glb` + its PBR maps straight from ComfyUI (no cleanup needed). I do
the Blender retopo, real-scale (0.64 × 0.32 m), +Z orientation, the 6 named anchors, the
bundled custom-shader material, the prefab, and the bundle build.

**Option C — hand me the finished, contract-ready asset (most control for you).**
Give me a **GLB/FBX** that already satisfies §1: ~0.64 × 0.32 m, pivot centered, body
z>0 / decorated face −Z, triangulated ~5-20k tris, single PBR material set, and the 6
named empty anchors at z=0. I import it, bind a bundled custom shader (avoid the Standard
pink trap), author `Assets/Bundle/Table/PlayTray.prefab`, and build
`gloomhavenvr.bundle`. — Note: even here I re-check the material uses a **bundled** shader
and swap it if it references built-in Standard/UI.

> Whichever option, include: the mesh + all texture maps, and note the intended **top face
> direction** so I don't flip −Z/+Z. If you built the anchors, keep the exact names.

---

## 5. CC0 fallback (if you'd rather not generate)

The board layout is bespoke, so there's no perfect drop-in — but these give a themed
body to kitbash into the contract, all **CC0** unless noted (project stance in
`unity/CARD-ASSETS.md`):
- **Poly Haven — `wooden_table_02`** (CC0, blend/glTF/FBX, 196 tris, 4K PBR) —
  https://polyhaven.com/a/wooden_table_02 — a clean wooden surface to cut slot recesses
  into. Already vetted in `CARD-ASSETS.md`.
- **Poly Haven — `wood_table_001`** wood PBR texture set (CC0, up to 16K) —
  https://polyhaven.com/a/wood_table_001 — varnished wood for a custom-modeled board.
- **ambientCG** wood/metal/fabric PBR sets (CC0, 1K-8K) — https://ambientcg.com — brass
  fittings, aged wood, felt rest-zone lining (e.g. Wood*/Metal*/Fabric019).
- **KayKit — Board Game Bits** (Kay Lousberg, CC0, OBJ/FBX/glTF) —
  https://kaylousberg.itch.io/board-game-bits — tokens/coins/dice to steal small
  decorative geometry from.
- **Quaternius** CC0 packs — https://quaternius.com — misc fantasy props/frames.
- **Kenney** (CC0) — https://kenney.nl — 2D rune/parchment textures to inlay.
- **Sketchfab** with the **CC0 / downloadable + CC-BY** filter — search "wooden tray",
  "player board", "fantasy desk". CC-BY needs a credits line; verify per-asset (the
  `CARD-ASSETS.md` rejection list shows how easily Sketchfab license claims mislead).

You'd still model the two slot recesses + rest zone + button pads and add the anchors in
Blender/Unity — so the effort is comparable to finishing an AI mesh, which is why §2
leads with generation.

---

## 6. Forward pointer — same pipeline for the rest of the bundle

The bundle README lists more themed assets the mod probes (all with procedural
fallbacks). The **same Flux → Hunyuan3D-2.1/TRELLIS 2 → Blender-finish → bundle** pipeline
should produce them, matched to their own anchor contracts in
`Assets/Bundle/Table/README.md`:
- **`CardBacking.prefab`** — 63.5 × 88 mm × ~1.5 mm rounded card body; front face flush
  z≈0..+0.0015 (live canvas covers it — **don't paint a front frame**), opaque decorative
  card-back material faces **+Z**, dark rim. AI is overkill for the geometry (it's a
  rounded slab) but great for the **card-back art texture**; see candidate meshes in
  `unity/CARD-ASSETS.md`. Do this one alongside the tray — they share the module and
  orientation convention.
- **`ReadyButton.prefab` / `Button_Ready|Undo|Skip.prefab`** — `Base` + travelling `Cap`
  (BoxCollider on the Cap, its own material instance for runtime tinting), optional
  `LabelAnchor`. Small, high-value AI/kitbash targets; keep caps as separate objects.
- **`PanelFrame` / `WristHud` / `ScreenFrame` (optional cosmetic)** — decorative frames
  with a single named anchor each; nice-to-have, same pipeline.
- **`LaserPointer` / `Dot`** — must use a **self-contained unlit bundled shader**, never
  Standard.

Recommendation: land **PlayTray + CardBacking first** (they're what the player stares at),
then batch the buttons, then the optional frames — reusing one Flux style prompt family so
the whole dashboard reads as one themed set.

---

### Key source pages (2026)
- Bundle contract: `unity/GloomhavenVR.Assets/Assets/Bundle/Table/README.md`;
  consumers `src/GloomhavenVR/Cards/VRCardFactory.cs` (paths L20-28) &
  `PlayTray.cs` (name lookups L319-333).
- Hunyuan3D-2.1 ComfyUI: https://github.com/visualbruno/ComfyUI-Hunyuan3d-2-1 ,
  https://github.com/Yuan-ManX/ComfyUI-Hunyuan3D-2.1 ,
  https://docs.comfy.org/tutorials/3d/hunyuan3D-2 ,
  https://comfyai.run/documentation/Hunyuan3DTexureSynthsis
- Hunyuan3D 3.0 (partner nodes): https://blog.comfy.org/p/hunyuan-3d-30-in-comfyui-state-of
- TRELLIS 2: https://github.com/visualbruno/ComfyUI-Trellis2 ,
  https://trellis2.app/blog/trellis-2-comfyui ,
  https://trellis2.app/blog/best-ai-3d-model-generator
- VRAM: https://www.triposrai.com/tools/hunyuan3d-free-online/
- Flux in ComfyUI: https://www.thundercompute.com/blog/flux-comfyui-ai-image-generation ,
  https://comfyui.org/en/high-quality-game-character-art-workflow
- CC0 sources & licensing stance: `unity/CARD-ASSETS.md`
