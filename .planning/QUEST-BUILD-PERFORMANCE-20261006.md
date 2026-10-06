# Quest builder performance review — 2026-10-06

This is a source review of every scheduled Wizard stage and build operation,
not an end-to-end Windows qualification. It reviews feature checkpoint
`8cfbcfc77d775db2b1d6e7745a9a1492260974aa` and identifies this round's
separately implemented reference-audit work (`dffe401eb`). New staging work in
the integrator's checkout is distinguished from recommendations below.

## Actual Windows witness and limits

The final support package is `quest-build-support-20261006T213551Z-be492738.zip`.
Its release identifies ModBuild 627/source `5dce06841e30d1befbe9761223e6e188a6b81adc`.
The failed build attempt lasted **7,356.197 seconds** in total; this is not the
duration of bundle export alone. All 16 original package groups completed.
The subsequent raw reference audit took **933.023 seconds**, the source recheck
**45.883 seconds**, final checkpoint publication **2.204 seconds**, and the
3,255-container CAB index **0.316 seconds**. The final checkpoint contains
258,301 records and 204,339,293 bytes. Post-export helper loading then failed
with `ModuleNotFoundError: No module named 'recover'`. It did not reach derived
staging, Unity import, native Addressables or the Android Player build.

The completed prerequisite stage durations were tools 29.207 s, source 34.581 s,
Unity 11.280 s, profile 0.128 s and inspection 30.391 s. Several were retries;
these numbers do not describe fresh prerequisite downloads/Editor installation.
The bounded progress log retains 5,179 valid records and three clipped/stitch
fragments; missing earlier spans must not be treated as zero work.

The audit worker's 2,040-file/9.37 MB Linux fixture retains exactly the old
reference result while reducing median time from **5.430 to 2.955 seconds**.
This establishes a fixture improvement, not a 46% Windows whole-build speedup.
No Unity import, APK, exhaustive shader run or hardware test was performed for
this review. Later Windows stages remain unmeasured.

## All seven Wizard stages

| Stage | Necessary work and source owner | Observed evidence | Avoidable work / safe change |
| --- | --- | --- | --- |
| `tools` | `provision.tools`: pinned Git/.NET8/.NET10 downloads, member extraction, executable/support-file qualification; host memory/disk admission. | 29.207 s on this retained run. | Range continuation and file-level extraction resume already exist. Reuse a verified target hash within one invocation; two bounded independent downloads may overlap, but avoid concurrent `Supervisor.run` calls until its ownership model supports them. Do not repeatedly redownload matching archives. |
| `source` | `provision.release_source`/`source_checkout`: selected current source, authored art, declared XR dependencies, exact source manifest. | 34.581 s; three attempts. | Matching release targets currently hash twice in the copy loop. Preserve its first accepted digest. `release.verified_source_inventory` hashes the pinned public converter archive at entrance and again in its inventory; reuse the first actual result while still validating the mandatory exact manifest row. Git/source selection and derived dependency identities must remain explicit. |
| `unity` | `unity_setup.setup`: exact Editor/Android modules, Hub interaction, real license/prerequisite probe. | 11.280 s on an installed Editor. | Keep the small existing probe and installed Editor/modules. Account interaction is a visible wait, not build progress. Reuse confirmed prerequisites for the same actual Editor/tool identities; never automate an unapproved license/account choice. No recovered game import belongs in this stage. |
| `profile` | `wizard.stage_profile`: static logo, selected local identity, owned DLC evidence. | 0.128 s. | Already small; local files and pinned logo should remain reusable. Changing display name/ID must invalidate generated profile/Player outputs, not original asset export. No Horizon/store/cloud round trip is required. |
| `inspect` | `builder.inspect_inputs`: hash selected owned game/source once, resolve ownership, publish immutable input manifest. | 30.391 s for 5,228 original files/17.755 GB on this installation. | Build immediately repeats `inspect_inputs`. A future explicit inspect-to-build manifest handoff can reuse hashes only after retaining source mutation guards/known ownership. Do not silently substitute a stale manifest or rely on size alone. |
| `build` | Snapshot, original conversion, platform adaptation, original Android import/banks, current mod AOT, signed Player and delivery contract. | 7,356.197 s; successful raw recovery then helper import failure. | Main opportunities are removal of repeated reads, retained qualified raw/derived outputs, narrower derivative keys and bounded compiler scheduling. Breakdown below. Fix/preflight the helper import before launching expensive conversion. |
| `install` | `quest-installer/installer.py`: authorized ADB connection, package update, exact current data files and launch identity. | Pending in this failed run. | Current file-backed installation resumes in batches and skips matching completed remote files; bounded shell batches already avoid Windows command-length failure. Keep changed-file transfer and existing saves. First installation still transfers the owned bank. Measure bytes/rate separately from PC conversion. |

## All 21 scheduled build operations

`P0` means an immediate bounded repair; `P1` a high-value derivative/cache
change needing qualification; `P2` a lower-priority measured improvement.
An operation's required semantic output cannot be removed merely because its
current implementation scans too much.

| Operation | Necessary result and concrete source | Work already reduced / remaining safe opportunity | Evidence and priority |
| --- | --- | --- | --- |
| `game-inputs` | Exact owner installation identity; `builder.inspect_inputs`, `storage.inventory`. | Repeated Wizard inspect/build hash is removable with a qualified immutable handoff. Source changes must still be detected before freezing. | Inspection 30.391 s; build's individual input span not fully retained. P1. |
| `mod-inputs` | Current selected source/art/tool/dependency identity and ModBuild; `builder.source_inventory`, `release.verified_source_inventory`. | Reuse the same invocation's public-converter digest; never pin an old gameplay DLL or rebuild original data because an unrelated mod source changes. | Duplicate public-archive read source-proven; whole stage time not isolated. P0. |
| `profile-inputs` | Consistent local profile/ownership contract; `selected_profile`, `dlcs.capture`. | Local account/DLC record capture is small. Retain provider-independent file evidence; avoid repeated remote store queries. | Profile stage 0.128 s; build-specific span not isolated. P2. |
| `source-snapshot` | Frozen current source; `snapshot_inputs`, `storage.snapshot`. | Hash while copying; retained copies require one qualification. Preserve cross-file edit-window checks. Do not repeat accepted per-file target digests. | Current matching-release double digest source-proven; source snapshot itself unmeasured. P0/P1. |
| `game-snapshot` | Stable original data for hours-long conversion; `storage.snapshot`. | Fuse copy/hash, bound large-file progress, retain interrupted known files. Future block cloning requires read-only source/snapshot isolation; hard links permit owner updates to mutate the snapshot and are unsuitable here. | 17.755 GB input; fresh copying/target verification not timed separately. P1. |
| `snapshot-check` | Detect installation/source edits during freeze; `snapshot_inputs`. | Use an owned copy transaction with guarded source identities and a witnessed stream hash. Removing a whole scan requires preserving cross-file mutation detection, not replacing it with unqualified mtime checks. | Full game source inventory currently repeated; duration not isolated. P1. |
| `recovery` | All original scenes/catalog objects, original native identities, retained resumable batches and derived staging; `full_recovery.prepare`, `bundle_recovery.run_recovery`, `full_assets.stage`. | Raw batches already retained; repeated growing-output sweeps/native index rewrites reduced. This round makes reference audit measurable and avoids a second metadata read/node graph. Staging worker adds measured copies and writer proofs. More detail below. | 16 groups complete; raw audit 933.023 s; final CAB index only 0.316 s. P0/P1. |
| `project-files` | Quest overlays and current profile/scene/package contracts without losing original assets; `prepare.generate_files`, `import_workspace`. | Preserve stable game/import-keyed Unity `Library`. Copy only changed generated overlays on mod updates and narrow native-content derivative keys; avoid rebuilding an identical original asset tree because a source-only mod file changed. | Windows run never reached this operation. Existing stable Library key is source-proven. P1. |
| `native-runtime` | Android Proton/FEX launchers, unchanged owner's engine DLL, multiplayer voice/native bridge; `campaign_native.stage`, `proton_runtime.build`. | Proton artifacts already use a source/backend/NDK key, separate from general gameplay source. Keep pinned packages and compiled small launchers. Do not rebuild upstream Wine/FEX from source when qualified packages already supply them. | Not reached on Windows. No wholesale new native runtime build recommended. P2. |
| `audio` | Preserve original compressed Vorbis packets/channels/rate; `full_audio.stage`. | Container SHA currently repeated per clip. Cache one actual container digest per immutable invocation. Later group objects by container and release decompressed environments instead of retaining every loaded container. | Hash repetition source-proven; decoding time not measured. P0 digest; P1 grouping. |
| `textures` | Correct cubes, ordinary images, original float/HDR/mips/sampler state and packed sprite geometry; `full_textures`, `full_texture2d`, `full_sprites`, `native_stage`. | Cache repeated cube/platform-image container hashes. `full_sprites` and ordinary texture auditing already hash/group per container and release closures. Keep only formats that actually require Android reconstruction; avoid re-decoding already portable outputs. | Not reached on Windows; format fidelity remains required. P0 digest; P1 derivative reuse. |
| `graphics` | Original compute kernels and portable shader instructions; `campaign_compute.stage`, `campaign_shaders.stage`. | Shader cache path is game-keyed, but overlays are deleted/reproduced; compute cache uses broad `inputKey`. Use exact original instruction/recipe/converter keys and retain verified overlay outputs. Reuse the recovered physical CAB map instead of indexing all owned containers again. | Not reached. Ordinary builds already exclude exhaustive compiler validation; actual Android compilation remains required. P1. |
| `mod-banks` | Current authored VR resources; `package_mod_content`. | Existing key selects authored asset files plus Editor/full-game mode, independent of gameplay code. Preserve its Editor `Library` when art recipe changes; hash and pack in one pass, reuse an invocation's accepted bundle receipt. A changed art input must still rebuild its bank. | Not reached; independent current cache scope already source-proven. P1/P2. |
| `weave` | Compile current mod and statically adapt original managed assemblies; `weave`. | Reuse the compiled weaver executable across `weave`, `standalone` and `package-api` instead of repeating `dotnet run` build checks. Future narrow key separates current mod compilation from generated profile adaptation; do not reuse old mod code. | Not reached. Necessary adaptation is not optional. P1/P2. |
| `package-api` | Real Android package SDK signatures and audited adapted plugin ABI; `bind_startup_package_apis`. | It always launches the same recovered Editor project before receipt lookup; retain a SDK witness keyed to Editor/packages/SDK compilation recipe, qualify actual bytes and avoid a second startup/import when unchanged. Combine with the later Editor session only through an explicit process handshake. | Not reached; first import can be expensive, retained subsequent launches should be cheaper. P1. |
| `unity-import` | Unity's genuine Android import and Editor-script compilation; `QuestBuildConcurrency`, `import_workspace`. | Stable game/Unity/platform Library identity already exists. Preserve import settings before first import and avoid source-only writes/mtime changes to identical assets. Respect RAM-bounded import concurrency. Never run simultaneous Editors on the same project. | Not reached on Windows; first Android texture/import cost remains unavoidable. P1. |
| `unity-validation` | Required scene/script/import/texture/alias contracts; `QuestBuild.PrepareOriginalStartup`. | Minimum mode already skips exhaustive shader compiler/decode/reflection work. Consolidate repeated reads using invocation-local imported objects and invalidation for actual importer/asset changes; preserve pre/post-bank checks that inspect different produced outputs. | Not reached; distinguish mandatory native build from optional exhaustive review. P1/P2. |
| `content-bank` | Actual Android Addressables, original keys/labels and one delivered owned bank; `QuestStartupAddressablesBuild`, `native_content_pack.pack`. | Native `.bundle` entries already use ZIP_STORED, avoiding useless recompression. Keep Scriptable Build Pipeline cache/Library, qualified unchanged bank reuse and streaming write/hash. Existing raw original-PC bundles cannot replace Android outputs. | Not reached; first native bank cost unknown. P1. |
| `player` | Complete ARM64 IL2CPP/Gradle APK with current mod/profile and stable signing key; `QuestBuild.BuildPlayer`, `host_resources`. | Existing LLD selection and memory/commit-bounded Bee workers remain. Preserve IL2CPP/Bee/Gradle caches; do not recreate a clean project for every mod update. Unrestricted all-core compilation can cause paging/OOM and make the build slower. | Prior host measured ~26 GiB frontend peak, admission reserves 32 GiB first worker; Windows whole Player still unqualified. P1. |
| `delivery` | Genuine signed APK, actual required native programs/banks and bounded delivered graphics/compute checks; `validate_apk`, delivered validators. | Bank SHA and installation contract already avoid another entry sweep. Share an actual invocation-local read/digest result across graphics/compute readers when formats permit; retained receipts cannot replace validation of newly built outputs. | Not reached. P1. |
| `output-verify` | Atomic final Builder/Wizard receipt and correct installable output identity; `storage.Stages.run`, `Store.publish`, `verified_latest_build`. | Fresh bank can be hashed in `validate_apk`, stage publication, `verified_latest_build` and Wizard publication. Carry bounded producer proofs across owned process handoff or reuse one cold qualification in each process. Keep tamper/mutation detection and actual signed delivery binding. | Duplicate large-bank reads source-proven; Windows costs not measured. P1. |

## Recovery substeps and derived staging

| Substep | Current minimum / recommendation | Limit |
| --- | --- | --- |
| Source/catalog | Original source inventory already feeds catalog hash planning. Exact raw workspace identity depends on game plus recovery recipe, not all mod gameplay source. | A changed actual recipe needs compatibility qualification; current recipe selection includes every `tools/quest-recovery/` file, including documentation, so do not edit it casually. |
| Core export | Completed core identity and merged-output qualification already avoid scanning unused old core again. | A cold resume still needs one accepted qualification of actual retained output. |
| Package export | Real exporter collection counters and exact 16-group schedule already available; retain completed exports and do not re-export them on helper/UI changes. | Groups share canonical identity/checkpoint state. Export independent temporary groups only with separate ownership and a memory gate; merge sequentially. Two unrestricted decompressed games can exhaust RAM. |
| Merge/native recipes | Actual writer proofs, index parse reuse and no unchanged index rewrites are implemented. | Checkpoint/journal publication must remain atomic; avoid parallel shared-index mutations. |
| Raw reference closure | Worker uses one file walk/open, candidate PPtr streaming parse, real file/large-file counters and exact fallback semantics. Reuse the completed audit when raw bytes/identities are unchanged. | 933.023 s witness is old code; fixture gain is not a full Windows timing. |
| Source recheck | Current full input recheck took 45.883 s. Future eliminate its second scan only after introducing a stable owned-source proof covering the entire conversion interval. | Owner input updates must never produce a mixed-version catalog. |
| Final checkpoint/CAB map | Writer already streams/hash-publishes 204 MB in 2.204 s; CAB indexing takes 0.316 s. Reuse its exact physical map in later audio/graphics stages. | These measured small phases do not justify another large refactor this turn. |
| Derived copy/layout/native stage | Staging worker supplies explicit substeps, stream copy/hash and invocation-local copied-file proofs. Existing whole-stage keys/failed-stage fresh output otherwise repeat this work. | Checkpoint original inputs remain immutable. Canonical GUID/layout/pointer restoration genuinely changes assets. |
| Derived closure | `native_stage.restore` performs a whole reference audit and `full_assets.stage` performs another after TMP source/JSON writes. Add an opt-out of the intermediate audit only for the caller that guarantees the final mandatory closure audit. | Default direct/native-stage callers must keep closure qualification. TMP writes must remain limited to ShaderLab/includes with unchanged metas; requalify if future adaptations add serialized pointers. |
| Report/stage receipt | `full_assets.stage` hashes all final report files and `Stages.run` hashes them again. Reuse accepted writer proofs for unchanged files; hash changed/generated outputs once after their final mutation. | Cross-process/cold reuse requires actual qualification rather than trusting old size/mtime indefinitely. |

## Immediate implementation candidates

1. Preserve one SHA-256 per immutable original container in `full_audio.stage`
   and cube/platform-image code in `full_textures`; expected reduction is
   `sum((objects_per_container - 1) * container_bytes)` in redundant reads.
   Receipt contents stay identical. Timing and actual clip/container ratios need
   the next successful Windows run.
2. Skip `native_stage.restore`'s intermediate whole audit when called by
   `full_assets.stage`, which retains its final real closure audit. This removes
   one complete derived YAML scan, not the final acceptance check. Coordinate
   with the staging worker and cover default direct callers in focused tests.
3. In `provision.release_source`, retain the first accepted target digest. In
   `release.verified_source_inventory`, reuse the exact pinned converter digest
   for its manifest row. These remove duplicate reads without changing key,
   manifest, ownership or package qualification rules.

These are small source-proven opportunities. They are recommendations until
their commits and focused qualification are recorded by the integrator.

## Next measured priorities and progress requirements

After the owner gets past raw recovery, record separate active durations and
actual bytes/object counts for derived copy, native restoration, audio, textures,
graphics, first import, Addressables, IL2CPP, Gradle/signing and delivery. Do not
announce a whole-build ETA before those phases have a matching prior observation.

Narrow/cache immutable original-derived outputs before increasing concurrency.
Audio and cube stages currently retain environments for every visited container;
grouping/releasing one owning closure is safer than adding a thread pool. Parallel
codec tasks need disjoint temporary paths, capped in-flight payload bytes and an
explicit memory budget. Main-project Editors, canonical merges, manifest writers
and signing/publication remain single-owner operations.

Every long counter must include an observed denominator, current file/container
or package, accepted completion and bounded log activity. For opaque Unity work,
show its actual Editor/import/build log phase and last activity; mark ETA unknown
until observed rates exist. Elapsed time is not completion percentage. A exited
failed child must immediately leave the working presentation and show its cause
and retained continuation action. Repeated substeps must identify their different
owned files/batches; no completed step may silently restart.

The user can resume the qualified completed raw export instead of repeating the
two-hour old build attempt. This does not imply that first Android import and
native Player compilation will take only seconds. No Windows APK has yet been
qualified by this evidence.
