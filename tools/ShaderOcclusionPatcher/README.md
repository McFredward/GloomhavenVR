# ShaderOcclusionPatcher

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
```

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
