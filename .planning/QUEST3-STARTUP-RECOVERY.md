# Original Quest startup recovery

This target stages original owned game code and UI for an intermediate
Bootstrap → Intro → Gloomhaven_unified → MainMenu test. It does not establish
that campaign gameplay, multiplayer, native plugins or original graphics work
on Android. `fullGameReady` stays false and the full recovery gate is retained.

## Reproducible private inputs

The real AssetRipper 2.0.0 recovery is `/home/claw/quest3-local/recovery/startup-source-project`.
It was produced from the immutable owned `ressources/GH_Data`, loading the
original core assets plus the exact dependency union of
`always_loaded_base`, `always_loaded_standalone` and `always_loaded_base_high`.
That union has **17 original bundles, 51,930,049 bytes**. The export reports
260 shader placeholders, 16 serialized behaviour failures and 3,238 deferred
bundles. None of the 16 failing behaviour types occurs in the staged closure.

Stage a fresh private output with:

```sh
python3 tools/quest-recovery/startup.py \
  --source-project /home/claw/quest3-local/recovery/startup-source-project \
  --game-data /home/claw/gloomhaven_vr/ressources/GH_Data \
  --output-project /home/claw/quest3-local/recovery/startup-project-v4 \
  --tmp-source-archive /home/claw/quest3-tools/textmeshpro-3.0.6.tgz
```

Output must be fresh, outside the source recovery and owned game. The pipeline
checks the recovery receipt, managed metadata hashes, each selected output
hash, each retained original DLL against the actual owned Managed file, and
filesystem rule data against the recovery source inventory. It copies the
original assets and metadata; callbacks, scenes, asset GUIDs and managed DLL
bytes are retained. Transitive assembly references, dynamic Resources except
SRDebugger, and original Rulebase/GloomData filesystem inputs are included.
The source projects are never edited.

`quest-startup-report.json` schema 1 declares all output files and hashes,
the four selected scene paths in execution order, all 13 original scene paths,
source/recovery fingerprints, native imports, shader gaps, serialized failures,
and readiness limits. `sourceBuilderFingerprint` uses the builder's canonical
`{files:[{path,size,sha256}]}` format; `sourceFingerprint` retains the original
recovery receipt's canonical list hash.

## SDK script identity replacement

`Assets/QuestOriginalStartup/script-bindings.json` records the exact original
UGUI/InputSystem assembly, namespace/type, GUID and file ID for every affected
serialized SDK reference. The builder disables the original UGUI, InputSystem,
Addressables, ResourceManager and ScriptableBuildPipeline plugins before the
first Unity compilation; their original byte/meta provenance remains available.

`QuestOriginalScriptBindings.RemapAndValidate()` then resolves each affected
type against the **actual imported SDK package MonoScript**, obtaining its
GUID/local file ID through `AssetDatabase`. Missing/ambiguous types and unknown
disabled-plugin script pointers fail. Only `m_Script` pointers may change;
callbacks and all other serialized text must be identical after pointer
normalization. The complete transaction validates before any asset is written.
`QuestStartupEvidence/script-remap.json` records the old/new identities and
per-asset before/after/non-script hashes. This does not establish compatibility
of every InputSystem 1.4/1.7 serialized setting; actual Unity import/build and
runtime input checks remain required.

## Original Addressables associations

`Assets/QuestOriginalStartup/startup-addressables.json` contains `entries[]`.
Each row includes `entryIndex`, `originalAssetPath`, `assetPath`,
`recoveredGuid`, nullable `recoveredFileId`, original `keys[]`, original
`labels[]`, `resourceTypeName`, `provider`, `associationProof`,
`requiredOriginalBundles`, `status`, and `initialObjectLoadEligible`.

The source catalog has 1,713 locations in the selected label union. Most
locations describe serialized value types inside prefabs, rather than native
Unity objects: those are explicitly excluded from `LoadAssetsAsync<Object>`
startup associations. Native object mappings use the original container path
and object type. Standalone atlas sprites additionally prove their original
identity with `m_RenderDataKey`, preserving the original sprite file ID.
Converted JPG texture mapping requires the original sprite render key and its
actual texture pointer. FBX maps only from the exact original container path
to its exported prefab and, when unique, its referenced native Mesh. The
FireBlend shader has a single full container-path case difference on the
original Windows filesystem; this is recorded separately and requires an
unambiguous complete path. No filename-wide matching is used.

A genuine native Android catalog/bundle build remains necessary. Copying the
original Windows catalog or returning synthetic successful preload handles
does not satisfy that requirement. Preserve group label aliases such as
`misc_gui` as well as the three initial preload labels. Texture and Sprite
locations can legitimately share an original key: type-specific locations
must be retained, not reduced to a single untyped GUID dictionary.

## Shader source restoration and remaining blockers

TMP shader source is restored from Unity's official public 3.0.6 package:
`https://packages.unity.com/com.unity.textmeshpro/-/com.unity.textmeshpro-3.0.6.tgz`,
archive SHA256
`1ce172027b906a30be33cefe7b2ee46e1c8d35f729359b8b9785fc120d57b637`.
Only shader/include source is used; no duplicate TMP runtime/package scripts
are installed. Six recovered shader files retain their original paths/meta
GUIDs. All original SDF properties (64 desktop, 34 mobile), Sprite properties,
single-pass blend, cull, depth, stencil and color-mask settings are checked
against the original parsed recipes before restoration. Neutral additions
`_CullMode=0` and `_Sharpness=0`, and inspector range differences, are reported.
Neither source compatibility nor a successful compile proves original pixel
parity. Other custom shaders remain an explicit graphics blocker.

`Assets/QuestOriginalStartup/shader-recipes.json` retains the actual parsed
source shader property/state recipes and hashes for further controlled
restoration. These and all original game exports are private generated data;
no proprietary code/assets are tracked in Git.

The remaining serialized missing GUID is the export sentinel
`0000000deadbeef15deadf00d0000000`, assigned to
`UIInfoTools.AreaEffectSpriteAtlas` in `Gloomhaven_unified`. Source reads occur
in `EnhancedAreaHex.Init/ApplyEnhancement/RemoveEnhancement` and `CreateLayout`
area ability/enhancement rendering, not Bootstrap/Intro/menu startup. It remains
visible in the report and keeps complete `closureReady=false`; a menu-only
diagnostic may explicitly guard the unavailable later feature. No substitute
atlas is fabricated and the original serialized pointer is retained.

Source-proven native imports include ApparanceEngine, steam_api64,
AVProMovieCapture/kernel32, XInput/InControl, Photon voice/socket/encryption,
EOSSDK-Win64-Shipping and EOS native helpers. Their original managed APIs still
need platform-aware startup adapters. Windows native plugins are not staged.

## Verification

Focused Python recovery tests: **24 passed**, including real original TMP
recipe/official source checks for all six variants, negative blend/property
mutations, stale same-size asset detection, GUID collisions, path escapes,
unknown SDK scripts, callback retention, exact source sprite render keys and
ambiguous Windows path case controls. The managed metadata inventory builds
with .NET 8, zero warnings/errors, and reports real original native imports
without loading game code.

Actual Unity import, SDK remap, Android catalog/player builds and headset
results are separate integration evidence. This recovery checkpoint makes no
claim that those later gates have passed.
