# ParticleMasterUnlitAdd_Shd — fragment occlusion logic (DXBC disassembly)

**Goal:** extract the ACTUAL fragment-shader logic of `VFX/ParticleMasterUnlitAdd_Shd` to see exactly
how it decides a fragment's visibility/occlusion — to end the "why do flames render through walls in VR"
guessing loop.

**Status: SOLVED.** The occlusion is done entirely inside the fragment shader, gated by the global
float `_EnableOcclusionMap`, sampling the screen-space texture `_TilesOcclusionMap`. Both are published
*by the game's `TilesOcclusionGenerator` command buffer from whichever camera runs it*. In VR they are
either not set for the head camera, or set from the wrong viewpoint — so the shader's own gate collapses
the occlusion term to 1 (fully visible) and flames shine through walls.

---

## How this was obtained (reproducible)

The preferred AssetRipper route did NOT work: AssetRipper GUI Free 1.3.14's `ShaderExportMode=Decompile`
emits only dummy JSON for these shaders (the DirectX disassembler is not in the free build); the game's
own shaders even came out as empty `UnreadableShader_*.json`. So a manual pipeline was built:

1. `tools/ShaderDisasm` (net8, AssetsTools.NET 3.0.4 — same loader as `tools/ShaderOcclusionPatcher`):
   loads the Shader asset (ClassID 48), dumps `m_ParsedForm` (properties, keyword table, per-pass
   sub-program -> blob-index/keyword mapping, `m_NameIndices` name->index table), LZ4-decompresses the
   `compressedBlob` per platform, and **carves the raw `DXBC` blobs** by their magic + internal size
   field (offset 24). Each fragment/vertex program is a real D3D11 `ps_4_0`/`vs_4_0` DXBC.
2. DXBC -> assembly via **DXDecompiler** (github spacehamster/DXDecompiler, netstandard2.0 lib;
   `new BytecodeContainer(bytes).ToString()`). HLSL decompile fails (NRE) because Unity **strips the
   RDEF chunk** — only `ISGN`/`OSGN`/`SHDR` survive — but the SHDR assembly disassembles cleanly.
3. Register->name binding: RDEF is gone, so texture/cbuffer *member* names are not in the DXBC. They were
   recovered from (a) the pass `m_NameIndices` table, (b) the per-sub-program constant-buffer table in
   the decompressed blob (gives cbuffer names + sizes: `$Globals`=128 bytes/8 regs, `UnityPerDraw` holds
   `unity_ObjectToWorld`, etc.), (c) **cross-checking against decompiled game C#** (`Shader.SetGlobalInt`
   / `SetGlobalFloat` calls), and (d) the semantic role of each slot in the assembly.

Commands (game data is read-only at `ressources/GH_Data`):
```
dotnet ShaderDisasm.dll <out> "VFX/ParticleMasterUnlitAdd_Shd" \
   .../StreamingAssets/aa/StandaloneWindows64/misc_shaders_assets_all.bundle
dotnet disrun.dll asm <out>/VFX_ParticleMasterUnlitAdd_Shd.*.blob02.PIXEL.dxbc   # DXDecompiler runner (scratch)
```
Evidence files are generated locally in ignored `evidence/` (full disassembly and parsed-form
reports); see [evidence/README.md](evidence/README.md). They are not distributed with this source tree.

---

## Shader shape (from `m_ParsedForm`)

- One SubShader, **one pass**, `LIGHTMODE=FORWARDBASE`, `Queue=Transparent`, **`zTest=4` (LEqual)**,
  `zWrite=0`. The zTest is a hard-coded literal (`zTestProp='<noninit>'`), NOT `ZTest Always(8)` — so
  the wall occlusion is **not** a ZTest issue and `ShaderOcclusionPatcher` (which flips 8->4) does nothing
  to this shader.
- **No occlusion/wall-fade keywords exist.** The shader keyword table is only
  `STEREO_*`, `INSTANCING_ON`. The two fragment sub-programs differ ONLY by `INSTANCING_ON`; both contain
  the identical occlusion logic. **Occlusion is therefore a runtime uniform branch, not a shader variant.**
- Textures (from the assembly + `m_NameIndices`): `t0 = _TilesOcclusionMap`, `t1 = _CameraDepthTexture`,
  `t2 = _MainTex`. Constant buffers: `cb0 = $Globals`, `cb1 = UnityPerFrame/Camera` (`_ZBufferParams`,
  `_WorldSpaceCameraPos`, `_ScreenParams`, `_Time`), `cb2 = unity_ObjectToWorld`, `cb3 = unity_MatrixVP`.
- `$Globals` (cb0) slot map (occlusion slots cross-checked against decompiled globals — see below):

  | slot        | name                  | kind          | role                                             |
  |-------------|-----------------------|---------------|--------------------------------------------------|
  | cb0[4].xyzw | `_TintColor`          | material vec4 | multiplied into output color                     |
  | **cb0[5].x**| **`_EnableOcclusionMap`** | **global float** | **MASTER GATE (0 => occlusion bypassed)**  |
  | cb0[5].y    | `_ToggleWallfade`     | material float| scales the wall-fade factor                      |
  | cb0[5].z    | `ToggleWallFade`      | global **int**| integer enable read via `itof` in the blend      |
  | cb0[5].w    | `_Toggle_DepthFade`   | material float| enables the soft-particle depth fade             |
  | cb0[7].x    | `_DepthFade_Distance` | material float| depth-fade distance divisor                      |
  | cb0[7].y    | `_Apply_EdgeMask`     | material float| enables the edge mask                            |

---

## Occlusion and wall-fade interpretation

The inspected program projects the particle through the rendering camera's view/projection
matrices, samples the tile-occlusion texture in screen space, and combines its depth/coverage
channels with the material's wall-fade setting. The global occlusion switch can bypass that result.
Independent soft-particle depth fading and the edge mask further modulate the output alpha.

These are engineering observations from locally extracted evidence. Full shader instructions
belong with the developer's private game references and are intentionally not reproduced here.

### Answers to the specific questions

- **Does it sample `_TilesOcclusionMap`? With what UV?** Yes — `t0`, sampled at the **screen-space**
  projective UV `v4.xy / v4.w` (`ComputeScreenPos`). It does NOT sample `_ObjectOcclusion` in this pass
  (the generator publishes `_ObjectOcclusion` too, but this shader only reads `_TilesOcclusionMap`).
- **How does the sampled value gate the output?** Two channels are used: `occ.a` is a depth-like value —
  `wallFactor = (occ.a >= particleClipDepth) ? 1 : (1 - occ.r)` — i.e. if the map says the tile in front
  is nearer than the particle, the particle is faded by `occ.r`. That factor is **multiplied into the
  fragment alpha** (there is NO `clip()`/discard; it's a smooth multiply/fade).
- **Role of `_EnableOcclusionMap`:** it is the **master runtime gate** (`cb0[5].x`). The whole occlusion
  term is wrapped in `movc r0.x, (_EnableOcclusionMap != 0), computed, 1` — **if it is 0, the occlusion
  factor is forced to 1 and the flame is fully visible** regardless of the map. It is a *global float*,
  set to `1f` by `TilesOcclusionGenerator` in its command buffer, alongside `SetGlobalTexture("_TilesOcclusionMap", ...)`.
- **Role of `ToggleWallFade` / `_ToggleWallfade`:** `ToggleWallFade` is a **global int** (`cb0[5].z`, read
  via `itof`) and `_ToggleWallfade` is a **material toggle** (`cb0[5].y`, a scale). They tune/enable the
  wall-fade term but they are downstream of the master `_EnableOcclusionMap` gate. There is no
  `_TOGGLEWALLFADEOFF_ON` keyword — nothing here is keyword-gated.
- **Does it read `_CameraDepthTexture` (soft-particle depth fade)?** Yes — `t1`, sampled at screen UV,
  linearized via `_ZBufferParams`, divided by `_DepthFade_Distance`, gated by `_Toggle_DepthFade`
  (`cb0[5].w`). Independent of the occlusion-map path.

---

## Cross-check: decompiled game C# (authoritative)

`decompiled/GH.Runtime/TilesOcclusionGenerator.cs` (~L155-191) builds a `CommandBuffer` on the game camera
at `CameraEvent.BeforeGBuffer`: draws room renderers with an occlusion material into an
`R8G8B8A8_SNorm` RT **with a 24-bit depth buffer**, pixel-corrects (needs the rendering camera's view
matrices) and blurs, then publishes the generated texture and enables the occlusion-map global.
`ToggleWallFade` is a **global int** toggled elsewhere: `Shader.SetGlobalInt("ToggleWallFade", 1/0)`
(`Main.cs`, `ActivateWallFadeInGame.cs`, `DebugMenu.cs`, `ToggleWallTransparencyGlobal.cs`) — matching the
`itof cb0[5].z` in the shader exactly. This confirms the slot binding above.

The mod already has `src/GloomhavenVR/Compat/TilesOcclusionMirror.cs`, whose header documents precisely
this mechanism and mirrors the command buffer onto the VR head camera. This disassembly **validates that
design** end-to-end (the shader really does read `_TilesOcclusionMap` in screen space, gated by
`_EnableOcclusionMap`, comparing against the particle's own `unity_MatrixVP` depth).

---

## Brief checks on the two sibling shaders

- **`SimpleParticleAlphaDFade`** (present in both `resources.assets` pathId 597 and the bundle): FORWARDBASE,
  `zTest=4`, `zWrite=0`. Its `m_NameIndices` contain **no `_TilesOcclusionMap`, no `_EnableOcclusionMap`,
  no `ToggleWallFade`.** The fragment only does the soft-particle **depth fade**: samples
  `t1 = _CameraDepthTexture` at screen UV (`v4.xyz/v4.w`), linearizes both depths via `_ZBufferParams`,
  and divides the difference by `_DepthFadeDistance` (`cb0[8].x`), gated by `_DepthFade` (`cb0[6]`).
  **It does NOT use the occlusion-map / wall-fade path at all** — so it can only hide behind *opaque*
  geometry (via zTest) and fade near surfaces (via `_CameraDepthTexture`); it never self-hides behind
  faded/transparent walls. In VR its only occlusion dependency is a valid head-camera `_CameraDepthTexture`.
- **`UI/Default`** (`Resources/unity_builtin_extra`, pathId 10770): Pass `Default`, and its ZTest is indeed
  **driven by the standard Unity global `unity_GUIZTestMode`** (`zTestProp='unity_GUIZTestMode'`, default
  val 0). No occlusion-map logic — health bars etc. depend only on `unity_GUIZTestMode` for depth behavior.

---

## Conclusion — what makes a flame hide behind a wall, and what VR must provide

**In the flat game**, a flame fragment is hidden behind a wall by the FRAGMENT shader, not by ZTest
(zTest is LEqual with zWrite off, and walls are frequently rendered faded/transparent so they no longer
occlude via the depth buffer — which is exactly why this occlusion map exists). The shader:
1. samples `_TilesOcclusionMap` at its **screen-space** position,
2. compares the map's stored depth (`occ.a`) to the particle's own clip depth from `unity_MatrixVP`,
3. fades the particle's alpha by `1 - occ.r` when a nearer tile occludes it,
4. **but only if the global `_EnableOcclusionMap != 0`** — otherwise the whole term is forced to 1.

That map + globals are published by `TilesOcclusionGenerator`'s command buffer **from the camera it runs
on**, using **that camera's view/projection**. Screen-space UV + `unity_MatrixVP` depth mean the map is
only valid for the exact viewpoint that generated it.

**Therefore, for the SAME shader logic to hide flames from the VR head camera, the VR render must provide,
for the head camera and matching its VP:**
- **`_EnableOcclusionMap` (global float) = 1** at the time the VFX pass runs. If this is 0 in the VR
  render (because the head camera has no generator, or the game camera's command buffer set it for the
  wrong camera and it was cleared/overwritten), the shader **bypasses occlusion entirely (factor = 1) and
  flames always show through walls** — this is the single most likely cause.
- **`_TilesOcclusionMap` (global texture)** = an occlusion map rendered **from the VR head camera's
  viewpoint** (room renderers -> occlusion material -> depth+color RT -> pixel-correct with the head
  camera's inverse-view/view matrices -> blur), so that its screen-space UV and stored `occ.a` depth line
  up with the head camera's `unity_MatrixVP` (which the flame vertex/fragment uses for both screen UV and
  its own depth). A map generated for the offscreen game camera is geometrically wrong and produces
  garbage occlusion.
- (Optional) **`ToggleWallFade` (global int)** and the material `_ToggleWallfade` control whether the
  wall-fade term is applied at all; keep them as the game sets them.

The mod's existing `TilesOcclusionMirror` (replicate the generator's command buffer on the head camera,
with per-camera material copies so each camera re-points the globals to its own correct map at execution
time) is exactly the right input, and drives exactly the right globals (`_TilesOcclusionMap` +
`_EnableOcclusionMap=1`) at the right viewpoint. This disassembly confirms it is the correct and complete
fix for `ParticleMasterUnlitAdd_Shd` (and any VFX shader that reads `_TilesOcclusionMap`). Note that
`SimpleParticleAlphaDFade` is NOT one of them — it needs only a valid head-camera `_CameraDepthTexture`.
