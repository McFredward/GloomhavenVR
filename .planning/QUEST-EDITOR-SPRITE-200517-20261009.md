# Quest completed-import Sprite failure — capture200517

This repair stays on `feature/quest3-standalone`, with published dev659 inherited.
It delivers Builder sources, not another locally built whole APK.

## Actual evidence and cause

`quest-build-support-20261009T200517Z-1d99f895.zip` identifies source8063,
Runtime657 and the failed continuation of capture184004's first Editor import.
The initial synchronous refresh completes in5804.059s (96m44s), the Android
Development SDK compiles17 assemblies, and its Editor exits successfully.
Subsequent refreshes take0.25–2.93s. Package ABI qualification also succeeds:
554types,1471members,5rebindings,0issues. The final Editor's initial refresh
takes24.678s. All27 preparation owners are closed without a pending producer.

The final entry fails while validating
`Assets/Sprite/DLC_Promo_JawsOfTheLion.asset`: its imported GUID differs from a
literal taken from our earlier local AssetRipper export. Independent exports
assign different GUIDs. The exported Windows asset bytes are not present in
this support report; no actual Windows GUID value is claimed.

Promotion validation now reads the unique nonzero GUID from the current owned
asset's small `.meta` file and requires AssetDatabase to use that same identity.
It retains source/meta hashes, imported Sprite/texture checks, finite outer UVs
and nonempty sampled crop. Loading-layer identities still come from their exact
original geometry receipt; full Campaign validation separately retains original
collection/pathID, fileID, geometry and texture provenance. No GUIDs are rewritten.

## Retained project and current failure context

An exact reviewed late Editor source profile permits updating only the named
Editor scripts inside the completed27-owner transaction. Four current-input
JSON files and these small scripts are published atomically with their latest
owner records. Existing asset archives, script `.meta` files, original converted
outputs and Unity `Library` remain untouched. Transferred warm byte witnesses
are durable before the transaction plan, so consecutive interruptions before
file publication and after journal publication do not reopen original payloads.
Unknown source, template, game/profile or earlier consumer edits refuse this
compatibility. The raw recovery recipe remains unchanged.

The failure card now follows the explicitly named current Unity launcher log
to its corresponding bounded Editor tail. It displays the actual exception and
asset, rather than only the Python wrapper. Stale, foreign, linked and archived
logs cannot replace current failure context; incidental licensing retry messages
cannot obscure the fatal cause.

The first actual extracted-archive check also caught an isolated Wizard-loader
dependency: its explicit import order omitted the new overlay module. That
module is now registered before preparation identity/metadata and all temporary
aliases restored afterward. Three focused loader cases cover absent/preexisting
modules and the fresh `-I` process, including shared error types. The corrected
archive must pass the real launcher check before replacing the handoff.

## Unity observation and opening the project

Every Unity invocation remains observable through native import/build progress
and the current task label. Known validation schedules report6 configuration
tasks and11 startup/17 full-game validation tasks. Existing loops report actual
scenes, scripts, assets, textures, sprites, compute sources and Shader identities.
Addressables reports actual catalog work; installation-bank copies report bytes;
Player processing reports scenes and native build/compiler/Gradle work. Failure
leaves its current task unfinished. A child's100% cannot prematurely finish its
parent, and final Player completion waits for the final evidence publication.

No extra asset census, conversion, hash pass or Shader matrix is introduced for
the observer. Where Unity exposes no trustworthy total, the Wizard reports real
completed work, current action and elapsed time rather than inventing a total
or a time-based percentage. Full initial import percentages remain unknown.

The captured project key is
`24f29369a96c0f2d6c9947b8abb5a464e563c9963753cfcd737175c04d6bc1df`.
Its location is `<workspace>/build/projects/<key>`; the default Windows root is
`C:\Users\McFredward\.ghvrq`, but the report redacts the chosen absolute root.
The project can be added to Unity Hub and opened with2021.3.5f1 while the Builder
is not using it. `Assets/Scenes/Release/MainMenu.unity` and
`Assets/Scenes/Release/Gloomhaven_unified.unity` can be inspected in the Editor.
Direct Play does not reproduce the Android/headset startup chain. Manual changes
remain qualified changes; use a separate copy for experiments.

## Validation scope

Root integration passes23 completed-preparation/Editor-overlay/Sprite cases,
55 raw-recovery cases,13 preparation-identity cases,30 failure-context cases,
37 Unity observer/SDK/hierarchy cases and8 Node/Chrome cases. SDK checks compile
the actual Sprite validators, five long validators and progress helper against
the pinned Editor APIs; the production observer also executes13 assertions.
Actual-capture replay confirms the fatal Sprite cause reaches the existing UI.
Eleven individual script-publication cuts and consecutive plan/journal cuts
retain27 owners, archives, GUIDs, Library and durable original byte witnesses.
Unknown original/producer/template/helper controls refuse compatibility.

Actual old/new release inventories must preserve the captured27-owner producer
scope and raw recipe; extracted Linux launch/restart checks validate delivered
sources. These delivery receipts are recorded alongside the final archive audit.
The private evidence directory is `B659-failure-200517-20261009`.
No new whole Windows/Linux APK, headset result, complete wire gate or full Shader
matrix is claimed. Existing dev659 gates and unchanged native-memory evidence
are inherited; the user's next Windows continuation remains acceptance work.
