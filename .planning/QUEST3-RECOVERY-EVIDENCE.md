# Quest 3 owned-game recovery evidence

Date: 2026-10-03. Implementation starts from `dev`
`5344a5504adc4158ba466e7a9fbf460dedd5808f`, ModBuild607. The maintainer explicitly
requested a new Quest branch and implementation, superseding the earlier
documentation-only phase. This record covers the asset-recovery lane; Unity
Android player compilation and headset behavior are separate evidence.

## Actual export and input preservation

The official AssetRipper 2.0.0 Linux x64 release was obtained and executed locally.
Its archive SHA-256 is
`6f5d4352d36795b944243eb3ec790ec37dbca6d6756e52605bc8616e042d384e`.
The official Windows x64 archive was also downloaded and hashed for the lock,
but Windows execution was not tested. No proprietary exports or original DLLs
were added to the repository.

`scripts/recover-quest.py` performed a real fresh export into
`/home/claw/quest3-local/recovery/validated-project`. The selected input was the
read-only original `ressources/GH_Data`, plus a bounded four-bundle actor/shader
closure. Both complete before/after inventories contain 5,227 files totaling
17,754,574,362 bytes; their fingerprint remains
`6c51fbe7ddfafaa61ef724fa5785747f999a3a8d59b76ef27d922048c5db396f`.
Original input bytes were not modified. The subsequent `--resume` invocation
verified input/recipe/tool identity and every output hash successfully.

That coherent export uses recovery checkpoint `6074e9bd` and recipe fingerprint
`f0cf2e1102e090e40c07aeef9f5e828889e4e903d725b198813fd011666212e8`.
The later separate Addressables inventory tool extends the recipe fingerprint;
this receipt records the actual tested extraction checkpoint, not a claim that
new code ran in the preceding export.

The reconstructed project has 17,954 recorded output files totaling
2,126,344,586 bytes. Detailed receipts, original shader parsed forms, source
identities and logs remain external under the local recovery workspace.

| Result | Actual evidence |
| --- | --- |
| Original editor | Exported project targets Unity2021.3.5f1. |
| Original build scenes | All 13 original scenes and build order retained: Bootstrap, Gloomhaven_unified, MainMenu, MainMenu_gamepad, NewAdventureMap, NewAdventureMap_gamepad, Game, Game_gamepad, ProcGen, CampaignMap, CampaignMap_gamepad, EmptyScene and Intro. |
| Managed assemblies | 75 non-engine/non-framework plugins retained as byte-identical original DLLs; Editor import enabled. Original player Managed contains 169 assemblies in total. No duplicate decompiled game C# types generated. |
| Script identities | Metadata-only PE reader independently resolves 62,972 of 62,973 exported script references. No newly unresolved identities; all build-scene script references resolve. |
| Original orphan | The original serialized `StompyRobot.SRDebugger:ScrollRectPatch` identity has no corresponding original managed type. Its BugReportPopover prefab reference remains missing and is explicitly reported. No stub or invented replacement was added. |
| Asset GUID closure | 122,032 checked GUID references, no duplicate GUIDs, one unresolved exporter sentinel GUID across 23 assets. This includes optional actor cubemap/material references and the original AreaEffectSpriteAtlas reference. |
| Shader metadata | 319 original parsed shader instances exported with properties, state, keywords and program metadata. The 247 emitted shader files contain dummy passes, which remain a rendering blocker. |
| Original bundled content | 3,255 Unity bundle inputs identified by their actual headers. Four actor/shader bundles recovered; 3,251 bundles still deferred. |

Original startup is `Bootstrap.Start` -> `PlatformLayer.InitialisePlatformLayer`
-> original filesystem/log and input setup -> Intro -> Gloomhaven_unified ->
MainMenu. The platform boundary must be adapted before trying this startup on
Android; the recovered scene is not a demonstration that Steam/EOS readiness has
already been replaced.

## Serialized behavior gaps

The exporter completed but logged 16 MonoBehaviour structure errors. They remain
explicit blockers rather than being hidden behind the successful export status:

| Original component | Failed reads |
| --- | --- |
| Script.GUI.PartyDisplay.DimmerUIElements | 8 |
| UIFollowMapLocationInsideArea | 4 |
| UILocationMapMarker | 2 |
| UIQuestMapMarker | 2 |

The map-follow inheritance contains `SerializeReference` target objects; the
dimmer contains serializable model lists. These source mechanisms help narrow
the investigation, but they do not prove the exact exporter defect. Required
serialized fields must be recovered using a verified reader or original Unity
serialization evidence before claiming full map/menu parity. Correct MonoScript
identity alone does not restore fields that the exporter failed to read.

## Original Addressables keys

The exported project contains 1,359 serialized `m_AssetGUID` strings, representing
1,135 unique original keys. None directly equals the newly exported asset GUIDs.
Copying original strings into a new project therefore requires catalog aliases
or an explicit generated key/target map; asset presence alone is insufficient.

`tools/quest-recovery/catalog.py` was run against the actual original catalog.
Its compact layout was verified against the official Unity package source for
Addressables1.19.19, matching the version in original `aa/settings.json`.
It decodes 16,626 keys and 14,814 resource locations. The bounded recovery yields
86 exact resource-location associations across 72 unique source key/path pairs,
with original resource types, providers and transitive bundle dependencies.
It does not guess based on matching filenames, execute original game code or
claim to have rebuilt an Android catalog.

The original BanditGuard model key maps to the recovered
`Assets/Content/Characters/Monsters/BanditGuard/MO_BanditGuard.prefab` by its exact
source path. Its actual complete catalog dependency closure contains 20 bundles;
the first probe deliberately recovers only the required native visual subset.
The remaining source dependencies include original weapons and VFX materials.

## Portable owned-asset hardware slice

The external slice at `/home/claw/quest3-local/recovery/probe-assets` contains
`Resources/quest-original-model.prefab` and its native dependency closure. It was
generated from the genuinely recovered BanditGuard visual prefab, rather than a
substitute geometric figure.

The 24 native assets total 20,181,079 bytes before metadata/report overhead.
They retain six original meshes, skin/bones/bindposes, original Avatar,
Animator/controller and eight original animation clips. Mesh, Avatar and clip
payload hashes are byte-identical to the actual recovered source. Available
original diffuse, normal and MRAO textures are retained.

For this diagnostic alone, the extraction removes game/state callbacks
(ClassID114), native capsule physics (136) and cloth (183). It contains no C# or
DLL files. Original clip events remain unchanged; the diagnostic Animator must
set `fireEvents=false` because their gameplay receivers are intentionally absent.
This is a native asset/animation test, not a playable monster or campaign.

Two unavailable original `_EmissiveMap` cubemap references are cleared only in
the diagnostic and recorded in its report. `Amp_CharShader` and
`Amp_CharShader_2Side` require the separate probe material conversion; their
original `_Diffuse` property is populated, while `_MainTex` is not the body's
original texture property. Shader fidelity is explicitly false. This does not
authorize a rendering difference in the eventual playable port.

## Validation and limits

The lane's 15 focused contract tests pass. Negative controls exercise incomplete
inputs, source/output overlap, unsafe archives, equal-size input changes,
changed/missing/additional cache files, missing script identities, an essential
missing native mesh, serialized-layout errors, truncated catalog tables,
unsupported key kinds, cyclic dependencies and remote bundle IDs. The metadata
helper builds under .NET8 with warnings treated as errors. Whitespace checks pass.

The first complete recovery and hash-verified resume both ran successfully;
the separate native slice and original catalog tools also ran against real owned
inputs. No Unity editor import, Android build, multiplayer session, campaign
continuation or headset rendering result is established by this lane. The receipt
sets `fullGameReady=false` and preserves every unresolved blocker.
