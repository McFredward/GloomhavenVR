# ShaderOcclusionPatcher — legacy recovery tool

> Current installations use runtime rendering fixes and do not patch game shader files. This
> tool remains for detecting and restoring backups from older installations. The installer and
> uninstaller use its verification/recovery paths. Do not run `patch` as a current installation step.
> The original implementation notes below describe the retired approach.

Fixes VR depth-occlusion bleed in Gloomhaven Digital: several of the game's
shaders are compiled with a hardcoded `ZTest Always` in their **serialized pass
render state**, so their geometry (fire/torch/candle flames and glows, hex
decals, X-ray floor tiles, moths, ...) draws on top of walls. Tolerable on a
flat screen, immersion-breaking in VR.

The fix patches ONLY the serialized pass state inside the compiled `Shader`
assets — `zTest` `8` (Always) → `4` (LEqual) — leaving the compiled shader
programs **byte-identical**. No recompilation, no visual drift, no runtime
toggling: everything still looks exactly the same, it just stops rendering
through walls.

## Target shaders (exactly these 7)

| Shader | Bleed symptom |
|---|---|
| `VFX/ParticleMasterUnlitAdd_Shd` | fire/torch/candle flames + glows |
| `SimpleParticleAlphaDFade` | particle fades |
| `VFX/GPU_Bits_Shd` | GPU particle bits |
| `VFX/HexWaypointPath_Shd` | hex waypoint decals |
| `Amp_Basic_Unseen` | X-ray floor tiles |
| `VFX/WingFlap_Shd` | moths |
| `OmniDecal_Shd` | omni decals |

`KriptoFX/RFX4/DistortionParticles` is deliberately **excluded**.

Patch rule: only passes whose serialized `zTest.val == 8` are changed to `4`.
`0` (unset → platform default LEqual) and every other value stay untouched, as
does all other render state. A target shader with **no** `zTest == 8` pass is
flagged in the manifest and left alone (its bleed would need re-analysis).

## Usage

```
dotnet run -c Release --project tools/ShaderOcclusionPatcher -- <command> <options>

scan    --game-data <Gloomhaven_Data> [--manifest-out <file>]   read-only report
patch   --game-data <Gloomhaven_Data> --backup-dir <dir>        backup + patch in place (idempotent)
verify  --game-data <Gloomhaven_Data>                           exit 0 = fully patched
restore --game-data <Gloomhaven_Data> --backup-dir <dir>        copy backups back
dump    --game-data <Gloomhaven_Data> [--shaders a,b,c] [--manifest-out <file>]
```

`dump` is a read-only diagnostic: for every matched shader it prints **all**
subshaders (LOD + tags) and **all** passes (tags incl. LightMode/RenderType,
zTest/zWrite/cull/blend state), plus the set of global names the compiled
programs reference (per-pass `m_NameIndices` keys + shader props + keyword
names; LZ4 blob strings-scan as fallback when a layout exposes no names) with
matches against a depth-fade watchlist (`_CameraDepthTexture`,
`unity_GUIZTestMode`, `ToggleWallFade`, `_InvFade`, `_DepthFade`, ...). It also
looks inside `Resources/unity_builtin_extra` / `unity default resources` so
Unity built-ins like `UI/Default` resolve (scan/patch scope stays untouched).
Results also land in `dump-manifest.json`. `--shaders` (also valid for `scan`)
overrides the target list for diagnostics only — the patch set stays curated.

- Scan scope: `globalgamemanagers(.assets)`, `resources.assets`,
  `sharedassets*.assets`, `level0..N`, and **all**
  `StreamingAssets/aa/**/*.bundle` (shaders duplicated across multiple bundles
  are all patched).
- Bundles are repacked with the **same compression** they shipped with
  (detected per bundle; LZ4/LZMA/None) and swapped in via write-to-temp +
  atomic rename.
- `patch` copies each file's pristine original into the backup dir (relative
  layout preserved) **before** the first write and **never overwrites an
  existing backup** — re-running install keeps the originals safe.
- A JSON manifest (`patch-manifest.json`, written next to the backup dir)
  records tool version, per-file before/after SHA-256, and every pass's
  old→new `zTest`.

## Findings on the shipped build (2026-07 scan of pristine GH_Data)

Full scan (loose serialized files + all 3255 Addressables bundles):

| File | Shader | Pass | zTest (serialized) | zWrite |
|---|---|---|---|---|
| resources.assets | **OmniDecal_Shd** | 0/0 `Unlit` | **8 = Always** | Off |
| resources.assets | VFX/GPU_Bits_Shd | 0/0, 0/1 `FORWARD` | 4 = LEqual | Off |
| resources.assets | SimpleParticleAlphaDFade | 0/0 `Unlit` | 4 = LEqual | Off |
| resources.assets | VFX/ParticleMasterUnlitAdd_Shd | 0/0 | 4 = LEqual | Off |
| sharedassets6.assets | VFX/HexWaypointPath_Shd | 0/0 `Unlit` | 4 = LEqual | Off |
| misc_high_shaders_assets_all.bundle | Amp_Basic_Unseen | 0/0, 0/1 `FORWARD` | 4 = LEqual | On/Off |
| misc_shaders_assets_all.bundle | VFX/WingFlap_Shd | 0/0-0/2 | 4 = LEqual | On/Off/On |
| misc_shaders_assets_all.bundle | VFX/ParticleMasterUnlitAdd_Shd, SimpleParticleAlphaDFade, VFX/GPU_Bits_Shd | all | 4 = LEqual | Off |

**Only `OmniDecal_Shd` carries a serialized `ZTest Always`** (and gets
patched). The other six targets serialize `zTest = 4` as a literal constant
(`name == "<noninit>"`, i.e. NOT driven by a material property), so their
wall-bleed cannot come from serialized pass state — it needs re-analysis
(camera/layer setup, runtime material state, `_ToggleWallfade`-style vertex
tricks, ...). The tool flags them and leaves them untouched by design.

Patching `resources.assets` changes exactly **2 bytes** (the float
`8.0 -> 4.0`); everything else in the 42 MB file is byte-identical.

## Subshader coverage + referenced globals (2026-07 dump of pristine GH_Data)

Audit prompted by a hardware screenshot of a torch flame
(`VFX/ParticleMasterUnlitAdd_Shd`) bleeding through a wall despite its
serialized `ZTest LEqual`:

- **Coverage:** scan/patch/dump always iterated the full `m_SubShaders` and
  `m_Passes` arrays (no `[0]`-only bug). The dump proves every target shader
  ships with **exactly one SubShader** (LOD 0 or 100, single variant), so no
  hidden subshader/pass with a different ZTest exists. **No new `ZTest Always`
  passes surfaced** — `OmniDecal_Shd 0/0` remains the only one.
- **The flame bleed is therefore not serialized pass state.**
  `ParticleMasterUnlitAdd`'s single pass references `_TilesOcclusionMap` /
  `_EnableOcclusionMap` / `ToggleWallFade` cbuffer / `_ToggleWallfade` — the
  game does its wall-hiding **in the fragment shader** via an occlusion-map
  global, so geometry the map doesn't cover (VR walls/camera angles) shows
  through regardless of depth test.

Referenced-globals watchlist per shader (union over all passes; `dump` prints
the full name lists):

| Shader | Depth-fade capable? | Watchlist hits |
|---|---|---|
| `VFX/ParticleMasterUnlitAdd_Shd` | **yes** | `_CameraDepthTexture`, `_DepthFade_Distance`, `_Toggle_DepthFade` (prop, def **0** = off), `ToggleWallFade`, `_ToggleWallfade`, `_TilesOcclusionMap` |
| `SimpleParticleAlphaDFade` | **yes** | `_CameraDepthTexture`, `_DepthFade` (def 0), `_DepthFadeDistance` |
| `OmniDecal_Shd` | **yes** (depth-reconstructing decal) | `_CameraDepthTexture`, `_CameraNormalsTexture` |
| `KriptoFX/RFX4/DistortionParticles` | keyword only | `SOFTPARTICLES_ON`, `_InvFade` (grab-pass distortion) |
| `VFX/GPU_Bits_Shd` | **no** | — |
| `VFX/HexWaypointPath_Shd` | **no** | — |
| `Amp_Basic_Unseen` | **no** | — |
| `Amp_Basic_WallFade` | no (vertex wallfade) | `_ToggleWallFadeOff`, `_TOGGLEWALLFADEOFF_ON` |
| `VFX/WingFlap_Shd` | no (occlusion map) | `ToggleWallFade`, `_ToggleWallfade`, `_TilesOcclusionMap` |
| `UI/Default` (unity_builtin_extra) | n/a | `zTest` **driven by `unity_GUIZTestMode`** (serialized val 0; Unity sets Always for overlay UI at runtime) — health bars ignore depth via this global, not via serialized state |

## Addressables CRC check (investigated)

The Addressables catalog (`StreamingAssets/aa/catalog.json`) stores one
`AssetBundleRequestOptions` JSON per bundle inside the base64
`m_ExtraDataString` blob (UTF-16LE, length-prefixed). A nonzero `m_Crc` there
makes Unity CRC-verify the uncompressed bundle content on load — a patched
bundle would fail to load.

**Finding (shipped catalog, checked byte-wise): all 3255 entries carry
`"m_Crc":0`** — the game does not CRC-check its bundles, so no catalog change
is needed. As a safety net for future game updates the tool still checks at
patch time, and if it ever finds a nonzero CRC for a bundle it patched it
zeroes that value with a length-preserving edit (`12345` → `0` + spaces, so
the length-prefixed binary layout stays valid; catalog backed up first).
Zeroing simply disables the check for that bundle — deliberate, documented
choice over recomputing Unity's CRC.

## Notes

- `classdata.tpk` (Unity class database from UABEA, MIT) ships with the tool:
  player-built serialized files have stripped type trees, so shader layouts
  come from the database. Addressables bundles keep their type trees; those
  are used directly when present.
- Runs on Linux (dev verification) and Windows (user install; wired into
  `scripts/install.ps1`, undone by `scripts/uninstall.ps1`).
- Steam "Verify integrity of game files" is always a fallback restore.
