# Quest Build741 update audit against unfinished dev740

Status: preparatory audit and bounded host integration, 2026-10-28/29. Do not
integrate the actual rebase until the dev integrator explicitly reports its final
ready commit. The initial audit was read-only. The later authorized host delta
is described below; no existing bank or imported game was changed.

## Bound observations

The worker starts at dev `9cc11f5f4`; the published Quest735 checkout is
`7975c7e8c95d70138a515599246a6682fe1f6aa9`. The initial dev740 environment worker
head is `21701006b6df880d987b0a9b41222f75bf014a20`, with unfinished/untracked sky
inputs. These are observations, not the final Build740 release identity.
Machine-readable source sizes/hashes and the actual published release selector
results are retained privately in
`quest-dev740-rebase-20261028/update/readonly-audit.json`.

The tracked dev delta from finalized dev734
`fdbd372e30ed3ab49fb3e3e69af267f181edb800` to `9cc11f5f4` contains 12 C# inputs:
`SkyAlternative`, `NetProtocol`, `ConfirmationOnOwner`, `LevelUpComposite`,
`FlatScreen.8.MenuDepth`, `MenuDepthCardPlane`, `MenuDepthCharacterSource`,
`MenuDepthDisplay`, `MenuDepthGraphic`, the new `MenuDepthLevelUpHoverObserver`,
`DialogSurface` and `WorldUIModule`. There are no changed `unity/` or `tools/`
production inputs in that interval. The expected update is compact code with
unchanged art, native procedural helper, figure, audio, movie and loose Story
inputs. This must be repeated against the final integrated Quest candidate and
actual retained source manifest; no final classifier pass is claimed here.

The emerging desktop DefaultSky export has a 2,219,991-byte bank and a public
numeric/hash provenance JSON. It retains material 4, textures 6/8, mesh 12 and
AMP shader 18 from `sharedassets8.assets`, two original builtin fallback shaders,
and a new isolated container. All three shaders retain D3D11 platform 4. The two
texture streams total 5,767,200 bytes and the mesh stream is 73,876 bytes. The
runtime consumer requires logical resource `GloomhavenVR.DefaultSky.bundle`,
`native/default-sky/mesh`, `native/default-sky/material` and shader name
`AMP_SkyShader`. Final placement and export inputs remain owned by dev.

## Source release and compiler closure

`tools/quest-builder/release.py:selected` already rejects the derived desktop
`.bundle` and accepts the provenance JSON and `NativeDefaultSky.cs`. Keep the
bank out of `AUTHORED_RESOURCES`: it contains original game shader, texture and
mesh bytes. The actual selector currently rejects `tools/default-sky-native/`
and a proposed new Quest sky Editor tool root. Add the exact public exporter,
provenance, new producer and Editor source to the closed required release roster
after their final names are agreed. Do not publish native extraction outputs.

The resource lane proposes independent logical provenance
`GloomhavenVR.DefaultSky.provenance.json`, `sky_resources.py` and
`cache/default-sky-resources/<recipeKey>/<producerKey>`. Its recipe must bind the finite
DefaultSky recipe/exporter/Editor plus all consumed shader/converter locks and
helpers. It must not change the existing MenuDepth producer key. Include this
exact source union in `update_driver._update_source_rows`, including a cold
compiler snapshot without any source tree outside that qualified union.

`NativeDefaultSky.cs`, the csproj resource consumer and real weave/compiler
changes remain code inputs. `update_driver._art_rows` should remain unchanged:
the independent embedded bank is not any of the four authored mod-content bank
recipes. `package_mod_content`, original preparation steps and game-bank source
recipes must remain unchanged. Preserve unknown-source rejection; normalize
only complete reviewed orchestration identities whose original producer AST is
actually unchanged. No broad source-prefix/compiler alias is justified.

## Cold Quest-only B735 to B741 access boundary

A B735 APK has no separately embedded DefaultSky resource yet. Cache and exact
current-key embedded resource recovery cover later warm updates, but do not
solve that first transition. The retained Mono compiler seed contains only
`Managed/*.dll`; `compilerGameRoot` is not a native sky asset source.

First inspect the already selected signed APK for the finite qualified existing
Android assets. If the resource lane establishes their exact existing Player
locations, extract only their dependency closure with its original identities.
Do not search by shader/material display name or accept an unrelated fallback.

For assets in the existing external Android content bank,
`portable_package.content_source` plus `open_content` already provide a bounded,
seekable reader, including a nested portable archive. They do not materialize or
hash the whole bank. The selected APK's signed
`assets/Quest/installation-manifest.json` binds each installed StreamingAssets
entry to its size and SHA. Read and verify only the finite entries proved by
the sky recipe; avoid `qualify_content`, archive extraction and original game
conversion. The resource lane subsequently found the exact native Android
dependencies inside an existing B625 Player APK: nine of 29 sharedasset splits
(9,044,768 bytes), a 191,452-byte AMP member and 904,724-byte builtin data, plus
small Player/scene metadata. It reports Android ETC mip chains, target 13 and
matching original material/mesh values. These are resource-lane observations;
this host audit does not establish the final B735 input or final bank proof.

A source read directly from the headset currently imports only Package Manager's
exact `base.apk` through `installed_source.acquire_installed`. That helper
deliberately does not copy external game banks or private app data. If the
necessary assets are external, a separate finite read-only port is required:
bind the same hardware, selected APK/input key and complete installation receipt;
allow only the known signed paths under
`/sdcard/Android/data/dev.gloomhavenvr.quest/files/quest-owned-game/`; verify the
received size/SHA and file stability; retain a private owned cache. The existing
`.quest-installation.receipt` decoder and installer receipt qualification are
useful boundaries, not permission to traverse storage. Do not silently widen
the APK-only `_chunk` helper, invoke app-data recovery or read save/profile data.

Missing first-transition source must not force a complete Campaign preparation
or large original recovery. The chosen path needs a causal cold test with no PC
root, no work cache and a B735-style APK lacking the new embedded resource.

## Reuse and cleanup

The independent bank should prefer exact qualified private cache, then matching
bank plus provenance in `GloomhavenVR.dll-resources.dat`, then only the bounded
source access above. Verify Android/Vulkan serialization, native identities,
complete mip chains, material properties and dependency closure before cache
publication. Targeted production must retain original Campaign/mod banks and
the device/compiler seed. A warm call should need no PC input or Unity.

Add the new cache family to Wizard reusable update data and the protected
current/pending dependency set. `update_storage.cache_candidates` needs a finite
DefaultSky ownership branch, not just another family name: its current fallback
branches expect MenuDepth or mod archive receipts. Prune only superseded owned
payloads, retaining provenance/receipts, unknown files, `.keep` pins, final APKs
and evidence. Use the existing strong native file-witness cache to avoid rereads
of unchanged qualified payloads.

Publish `fastUpdateInputs` only when it is complete for all current independent
resources. An absent/incomplete field deliberately protects whole cache families;
an explicit empty array means a complete empty dependency set and can permit
pruning. Warm code/profile-only updates must carry the full current resource
set or omit the field conservatively. Existing MenuDepth resource, Story and
retained mod archive dependencies remain necessary where currently consumed.

## Validation plan and limits

The initial phase ran read-only source/selector observations with bytecode
writing disabled. Root then authorized a bounded host delta in `release.py`,
`update_driver.py`, Wizard `update_storage.py` and associated tests. The public
release now requires the four declared Sky recipe/producer files while excluding
the native desktop bank. The compact snapshot includes the exact `plan.sources`
union. Runtime/recipe changes remain compiler inputs; the original art/payload/
native selectors are unchanged. Full Story dependency publication requires both
current embedded resources, otherwise its optional roster is omitted.

The new two-level cache ownership closes
`recipeKey = value_hash(recipe)` and
`producerKey = value_hash({recipeKey, basis})`, including exact Android/Vulkan,
Unity 2021.3.5f1, MultiPass, typetree and source-row requirements. Current and
pending producer directories survive; superseded proven bank payloads may be
pruned while their metadata/evidence remains. Missing or malformed dependency
metadata protects all four independent cache families.

Focused pinned-Python 3.14.3 results: 68 Builder tests passed in 9.03 seconds
(`test_sky_update_scope`, `test_menu_update_scope`, `test_release`, complete
`test_update_driver`); 50 Wizard tests passed in 0.88 seconds (complete
`test_update_storage`, `test_cleanup`). New controls cover release exclusion,
finite compact closure, recipe-only compact classification, complete/missing
resource dependencies, both owner hash closures, wrong platform/scope/path,
same-recipe distinct producer retention, pending updates and evidence/pins.
Processes were reaped and `cleanup_completed_run` was called after each run;
logs, results and cleanup JSON remain in the private `update/` evidence root.
Tests use tiny private APK/ZIP fixtures; no deliverable APK/ZIP, Unity, ADB or
game conversion ran. Borrowed Quest735 host/test inputs are pinned separately,
with only explicit owned paths eligible for the integration patch.

Full integration is class C; the root integrator owns the final union gate once.
Resource production, real cold/warm recovery and final B735 input binding belong
to the resource lane. A final actual retained-manifest audit must pin the final
source and producer keys and show that original art/media/depth/Campaign inputs
are retained. Serialization and host tests cannot certify final headset pixels.

## Qualified portable resource handoff

Root review found that a portable ZIP selection remains in `args.base_apk` after
preflight resolves its exact APK into the update plan. `_build_code_player` now
sets only `args._quest_resource_base_apk = base` immediately before the existing
`builder.weave` call. The ZIP selection is preserved. Root's resource consumers
read this private handoff for both MenuDepth and Sky, avoiding another import.
An AST comparison against owned checkpoint `62565233e9` proves that this single
assignment is the complete additional production change.

The complete 69-test Builder selection passed with the unchanged Quest735
Builder (9.80 seconds). A further regression executes Root's actual frozen
`has_menu_depth` and `weave` functions through `_build_code_player`, stopping at
the first external compiler command. Both producers receive the qualified APK;
all four resource/provenance MSBuild overrides and the private Sky cache path
match. Independent bank, provenance and producer-key mutations each change the
weave key. No entire weave mock or compiler process is used.

With Root's borrowed prospective Builder (160,372 bytes, SHA256
`f5ca0c8f25ca8a7160ed66ff3cb722727d7b7ac6120030b927932526286a9dc9`), the
complete 70-test selection has 69 passes, including the actual handoff, and one
expected finite-identity failure:
`test_actual_legacy_b665_producers_without_new_capsule_field_retain_game_payloads`
still rejects the new complete Builder pending Root's exact producer aliases.
No guard/assertion was weakened. Root owns those final identities and the final
integrated gate. Both runs were reaped and received the common completion
cleanup. The borrowed Builder is explicitly excluded from the owned patch;
its pin and all results remain in private evidence.

The blocker is now closed with Root's exact finite identity snapshot SHA256
`c18bab49ccd7e68ee2852ac6aca2e11696b98edef263a3f32266131961308d4c`
(61,793 bytes), the same `f5ca0c8f...` Builder, resource-lane import snapshot
`9f84b9f4269ec3421f66c9cdc5fd10c40251107d0427c921346ffe51ee26d4ae`
(43,913 bytes), and the unchanged `case_paths.py` fixture dependency from Quest735
`7975c7e8c9`. The frozen owned Driver remains SHA256
`03932d78d74f439d09d45a4e21057fbcbb8e6b37329012113d977653b3eab29e`.
All 91 tests passed in 41.188 seconds: Sky scope 8, Menu scope 4, release 10,
update driver 48, preparation identity 16 and menu preparation identity 5.
This includes the entire prior 70-test selection, actual preparation migration
transactions and rejection of unknown original/producer/argument/scope changes.
No Root source, identity, assertion or owned production code was edited for this
qualification. The earlier failed/setup logs remain retained. The final log,
result, reaped-process cleanup and exact bindings are preserved as
`update/quest741-builder-06.*` and `update/root-identity-qualification-result.json`.
No Unity, ADB or game conversion ran.

## DefaultSky Unity progress visibility

The Wizard now admits only
`default-sky-unity-<recipe12>-<producer12>.log` and
`default-sky-launch-<recipe12>-<producer12>.log`, with lowercase hexadecimal
keys. Both use the existing Unity workplan, import, Shader and job observers;
their initial owner is active `update-code`, otherwise `weave`. Each alias keeps
that owner after closure, so a later tail cannot reopen a different operation.
No new workplan, timer-derived counter or synthetic bank completion is emitted.

The complete focused observer selection passes 107 tests in 4.457 seconds:
work observer, shader progress, shader total, general progress, compact update,
Unity phases, parallel import, observer composition, Unity import and native
memory. New controls exercise actual published plans and Supervisor log tails,
one shared invocation for both aliases, measured variant counts at 60 seconds,
the pending Shader return boundary after a completed pass, closed-owner tails,
and unknown logs with otherwise valid plans. Root alone emits the final 1/1 bank
event after the producer returns. The frozen Driver/Release/Storage and Root
Builder/Identity inputs remain byte-identical. Processes were reaped and common
cleanup completed; `update/quest741-sky-progress-02.*` retains the evidence.

## Short Sky work cache retirement

The PC resource producer's bounded work root is `work/sky-<producer12>`, with
exact schema1 owner `quest-default-sky-work-v1` and full recipe/producer keys.
Cleanup shares the existing finite Sky resource ownership validator before it
considers this work, even when the completed resource pair is protected for the
current update. It binds the final bank's metadata witness without rereading its
payload. Missing/partial banks, invalid owner/hash closures, prefix mismatches
and unknown work contents remain intact.

After that closure, only named generated native-cache subtrees and Unity
`Library`/`Temp`/build caches are candidates. Work owner, original source/overlay,
manifests, shader results and project/root logs remain. Deletion scans each
candidate and removes individually witnessed leaves before empty directories
through the existing cleanup I/O guards; source paths, signing files, `.keep`
pins and links retain their protection. Explicit pending Work/project references,
live Editors and `keep_paths` protect the work. Completed run/latest references
do not make these temporary caches a permanent fast-update dependency, and the
work family is not added to `FAST_AREAS`.

All 99 containing Storage/Cleanup/I/O/performance tests passed in 3.946 seconds,
including closed/current-pair reclamation, partial retention, owner/prefix/
unknown-content rejection, active/pending/explicit protection and stale previews.
Logs, results and completion cleanup are retained under
`update/quest741-sky-work-storage-03.*`. The current borrowed import-only resource
helper is pinned separately at SHA256
`97686fd97f66b9b88169f493973b23684e096750873522d2535ca2592a0d8e93`;
the prior 91-test proof retains its original helper binding. No Unity or frozen
Driver/Release/Processes/Root Builder/Identity edit was performed for this addition.
