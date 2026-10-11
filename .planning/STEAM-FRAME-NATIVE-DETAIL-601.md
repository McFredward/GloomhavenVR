# Native scenario detail: source and asset findings for the next Frame lever

## Fastest does not mean lowest generated geometry

The provided game assets use **Fastest Unity graphics settings with HighQuality
Apparance generation on the Standalone platform**. Build 600's Player lines
160–162 confirm Steam / Standalone / Fastest. This is not a Linux platform
misidentification: native `PlatformLayer.GetCurrentPlatform()` returns Standalone.

Read-only inspection of `ressources/GH_Data/sharedassets0.assets` (136 KiB) found:

| Original asset | `_qualityLevel` | Disable wall clutter | Show underground | Disable surface features | Detail disabling level |
| --- | ---: | --- | --- | --- | --- |
| HighQualityApparanceSettings | 1 | false | true | false | None (0) |
| LowQualityApparanceSettings | 0 | true | false | true | Highest (4) |
| LowQualityApparanceSettingsShowUnderground | 0 | true | true | true | Highest (4) |
| MediumQualityApparanceSettings | 0 | true | false | true | Medium (2) |

All four leave `_disableWallTorchesGeneration=false`. The actual
`PlatformSetting_Standalone` object names Fastest and references
HighQualityApparanceSettings; its per-map Apparance override reference is null.
Thus the native generation/detail profile is not inferred merely from asset
names: the serialized source-order fields and references establish the linkage.

The small asset SHA-256 is
`d8c401e678a776ee5fb07d0b1351258e1e3fd88cd6a30cf366dae18eb9b38dc6`.
Exact header/script identities, field offsets, values, raw object bytes and the
decode schema are retained in the gitignored main-checkout evidence manifest
`.planning/debug/steam-frame-evidence/build600-20261001/native-detail-assets.json`.
UnityPy reads the base MonoBehaviour headers; custom fields were decoded against
the decompiled serialized field order and verified MonoScript classes/assemblies.
No game files were changed. Asset path IDs are evidence addresses, **not runtime
constants to embed in the mod**. The manifest describes these supplied references;
future builds must inspect the actual runtime profile rather than assume the
installed game's version has identical values.

## Native source flow

`PlatformLayer.SetDeviceSpecificQualitySettings()` applies the PlatformSetting's
Unity quality name. Separately, `PlatformSetting.GetApparenceSettingByCurrentLevel()`
returns its Apparance ScriptableObject or a per-quest override, selected using
quest name and target-frame-rate class. It does not map Unity quality index zero
to the low Apparance profile.

`ProceduralMapTile.WriteExtraParameters` passes `_qualityLevel` as an integer to
the native procedural engine. `ProceduralWall.WriteExtraParameters` passes that
integer and `_showUnderground`. This is a plausible physical renderer-population
lever: less generated geometry avoids creating/submitting those renderers, rather
than only masking them afterwards. The native procedural graph is not decompiled;
source and asset labels do not quantify how many renderers quality zero removes.
The provided managed source has no readers for the three clutter/surface/torch
disable flags beyond their declarations. Their existence alone does not prove
that toggling each flag changes generated content.

There is another authoritative decoration path. `DetailsDisabler.Start()` invokes
each `IDetailDisablerProvider.StartDisable()`. `DetailLevelDisableProvider` obtains
`_detailsDisablingLevel` from the effective Apparance profile and reads a private
`SM.SerializableDictionary<DetailsDisablingLevel, DetailsModel> _detailsLevel`.
Each public `DetailsModel._objectsToDisable` is an exact native GameObject list.
The dictionary inherits Dictionary and can be read using one bounded FieldInfo
lookup and typed dictionary access. None/Low/Medium/High/Highest lists need not be
nested sets; inspect actual entries rather than assume monotonic membership.

Those lists could provide positive, author-selected decoration provenance for the
renderer budget, including assets missed by family names. Read the lists and
classify their mesh descendants with native prop/actor/door/floor/collider guards;
**do not call** StartDisable, EnableAll or ToggleDetailsLevel. Those methods write
SetActive and can stop callbacks/colliders. The actual provider prefab lists were
not inspected in this bounded metadata read, so their size, coverage and safety
remain unknown. Do not promise a saving from a provider which has no eligible
runtime targets.

## Preserve the proven reveal fix

`Core/Environment/ApparanceDetailFocus.cs` records the earlier missing-floor/wall
regression: VR parks ScenarioCamera, but Apparance formerly generated around that
parked Camera.main. Raw head focus still produced coarse or absent revealed-room
geometry when the headset was far from the new room. Inactive Apparance entities
are destroyed and recreated on reveal, so this was not only an initial-load bug.

The current driver keeps a board-level gaze focus, prioritizes a busy building
tile until synthesis finishes, damps movement, and restores the original engine
focus on teardown/VR-off. Keep this mechanism while experimenting with generation
quality. Moving the focus away to suppress detail, turning it off, or reverting
to raw head distance could reintroduce missing functional floors/walls. The native
low profile with underground disabled is also an unsuitable blind default; the
authored ShowUnderground variant provides a safer starting point, not proof that
every essential renderer remains present in VR.

## Reversible setting options, not yet implemented

1. **Native generation quality**: expose Original versus Reduced as a setting on
   every platform, with a distinct Frame default only after validation. Use a
   transient copy of the effective per-quest ScriptableObject; preserve the source
   object, per-quest overrides, underground behavior and unrelated platform flags.
   Change only source-proven presentation parameters initially. Restore the native
   getter result when disabled. Do not change `_detailsDisablingLevel` before the
   corresponding lists are audited, since native Start would deactivate them.
2. **Native decoration-list provenance**: retain renderer-only, reversible masks,
   but expand candidates using audited native quality-disable lists rather than
   broad name guessing. Keep incidental-collider representation checks and native
   gameplay identity vetoes. This does not require changing game object activity.
3. **Combine verified generation reduction with the configurable renderer budget**
   for remaining decoration. Initial generation changes should apply on the next
   scenario load; changing native parameters mid-turn can trigger expensive
   Apparance rebuilding and needs a separate readiness path before live support.

Before enabling a generation profile, verify initial load, door/reveal rebuilds,
retry, zoom/rotation and both multiplayer clients. Count actual active/visible
renderers after loading; preserve floor/wall continuity, native gameplay props and
every local/remote interactive surface. A lower parameter value or a passing
classifier fixture alone is not hardware acceptance or measured FPS improvement.
