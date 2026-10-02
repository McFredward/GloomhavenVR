# Owned-game recovery for Quest

This tool performs a real, local AssetRipper export of the player's owned
Gloomhaven installation. It writes the reconstructed project and all proprietary
content outside the repository. It never modifies the original player, references,
or decompiled files, and does not distribute a reconstructed game.

Requirements are Python 3.12+, a .NET 8 SDK, sufficient local disk space and a
Linux x64 or Windows x64 host. The original Unity editor version is 2021.3.5f1.
Recovery does not launch Unity; Android import, conversion and player validation
are separate stages. The pinned official AssetRipper 2.0.0 release is downloaded
from [its upstream release](https://github.com/AssetRipper/AssetRipper/releases/tag/2.0.0)
and verified against `tool-lock.json`. Extracted executables and dependencies are
also hash-verified before cache reuse. The Linux path has been exercised; the
Windows archive hash was obtained, but execution on Windows remains unverified.

```bash
python3 scripts/recover-quest.py \
  --game-data /path/to/owned/GH_Data \
  --output-project /path/to/local/recovered-project \
  --tool-root /path/to/local/public-tool-cache
```

`--game-root` is an alias and can identify the parent of a single matching
`*_Data` directory. `--manifest` records the hash of the builder's immutable input
manifest. A receipt at `quest-recovery-report.json` records the complete original
file hashes, recovery recipe/tool hashes, output hashes, managed identities,
original scene order, shader recipes and remaining blockers.

`--resume` verifies matching original bytes, recipe/tool identity, selected
bundles and every recorded output. Missing, modified or additional output content
invalidates reuse. Conversion should clone a verified recovery cache before
adding platform code or changing its shaders. Interrupted/failed exports retain
diagnostics in a separate scratch directory and produce no success receipt.

The initial core export includes all original build-scene containers and managed
metadata. It explicitly stages inputs rather than loading the complete game
directory: AssetRipper's `StreamingAssetsMode=Ignore` does not stop the loader from
discovering bundled StreamingAssets. Loading all bundles at once can consume
large amounts of memory. Recover a bounded dependency closure with repeated
`--bundle` arguments, using paths relative to `GH_Data`:

```bash
python3 scripts/recover-quest.py \
  --game-data /path/to/owned/GH_Data \
  --output-project /path/to/local/animated-recovery \
  --tool-root /path/to/local/public-tool-cache \
  --bundle StreamingAssets/aa/StandaloneWindows64/npc_banditguard_assets_all.bundle \
  --bundle StreamingAssets/aa/StandaloneWindows64/misc_shaders_assets_all.bundle \
  --bundle StreamingAssets/aa/StandaloneWindows64/misc_high_shaders_assets_all.bundle \
  --bundle StreamingAssets/aa/StandaloneWindows64/7c79e4fec988dbd61eedc6188355414e_unitybuiltinshaders.bundle
```

The retained assemblies are original DLL bytes and metadata tokens, with the
exported MonoScript GUID/fileID references. The exporter does not generate a
duplicate decompiled game source tree. Its DLL importers are enabled for Editor
inspection. `ManagedInventory` reads PE metadata without loading/executing game
code, and independently validates Unity's script fileID hashing. A matching type
identity establishes metadata closure; it does not prove Unity has imported the
scene correctly or that Android supports every native dependency.

Original serialized missing scripts are distinguished from new export defects
using their actual original MonoScript identities. They remain missing and are
reported; the converter does not invent replacement components. Newly unresolved
script identities fail recovery. Serialized layout/import errors remain explicit
full-game blockers even when AssetRipper finishes exporting other assets.

An exit code of zero means the original core project and its receipt were
recovered. **It does not mean a playable Quest game exists.** In particular,
`audit.readiness.fullGameReady` is false, and `audit.blockers` lists missing Unity
import/player evidence, shader reconstruction, deferred bundle conversion,
serialization losses and asset-reference gaps. A full-game builder must resolve
these gates in subsequent validated stages instead of treating export success as
campaign readiness.

The free AssetRipper export contains dummy shader passes. The recovery preserves
original parsed shader properties, pass state, keywords and program metadata
under `QuestRecovery/ShaderRecipes`; it does not present those dummy passes as
faithful original rendering. Existing repository shader investigations are
documented under `tools/ShaderDisasm`, including the stripped DXBC metadata and
occlusion/depth dependencies. Android shader source reconstruction remains a
separate conversion task.

For the labelled hardware diagnostic, a real animated model can be extracted
without importing original gameplay assemblies:

```bash
python3 tools/quest-recovery/probe_slice.py \
  --project /path/to/local/animated-recovery \
  --prefab Assets/Content/Characters/Monsters/BanditGuard/MO_BanditGuard.prefab \
  --output-assets /path/to/local/probe-assets
```

Copy those contents under the diagnostic project's `Assets/Quest/Recovered`.
`Resources.Load<GameObject>("quest-original-model")` finds the original model.
The generated closure retains the native skin, meshes, rig, avatar, Animator,
original clips and available textures; it strips gameplay callbacks and native
physics solely for this diagnostic. Essential missing mesh/avatar/controller
references fail extraction. Optional unavailable references are cleared and
reported, and the probe material conversion must visibly disclose shader gaps.
The probe Animator must set `fireEvents=false`, since unchanged original clips
still contain events that require the omitted gameplay controllers. The slice
contains no C# files or DLLs. Its report explicitly states `playableGame=false`
and `originalShaderFidelity=false`.

Focused contract validation:

```bash
python3 -m unittest discover -s tests/quest-recovery -v
```

Negative controls cover missing original inputs, source/output overlap, archive
path traversal, equal-size changed inputs, stale/modified/extra cache outputs,
unresolved scripts, missing essential model assets and serialized-layout errors.
