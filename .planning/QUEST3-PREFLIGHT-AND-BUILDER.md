# Quest 3 preflight blockers and builder maintenance

**Assessment revised 2026-10-02; implementation authorized 2026-10-03.**
The maintainer has now requested a separate branch from current `dev` and work
through a first hardware-testable state. These gates remain requirements for
the complete port. Dated assessment statements below are not implementation
results; see [implementation evidence](QUEST3-IMPLEMENTATION.md) for actual
exports, builds and outstanding blockers. No hardware acceptance is inferred.

This revision records the additional maintainer decisions:

- Embed the Steam logo, source account's display name and stable Steam ID in
  each locally built APK. Use local data, not a personal-avatar service.
- Exclude cloud saves and other Steam/Epic/GOG account/store services on Quest.
  SteamKit, a full Steam client port and provider cloud integration are removed
  from the active design. Local saves and manual campaign transfer remain.
- Let ordinary mod development continue in the same source tree without
  maintaining a separate Quest behavior implementation or per-build patch list.

The Steam-logo instruction replaces the previous local personal-avatar plan.
The embedded identity describes the acquired PC account; it does not certify a
live Steam login. PC-side account capture may use the player's existing client.
Other storefront converter inputs are not prerequisites for this first Steam
profile path; compatibility with their original multiplayer peers remains.

The existing Photon gameplay/voice backend is required for the requested
multiplayer. On 2026-10-02 the maintainer explicitly allowed EOS if required
for the original multiplayer. This narrow exception permits necessary EOS
authentication/session operations, not cloud saves, live profile lookup,
storefront friends/invites or other store services. Prove actual necessity
and legitimate Android authentication; permission is not backend access.

Source review uses `dev` at `f400f30c`, ModBuild 606, and the current generated
patch inventory. References and measurements below are dated observations.
None is a passed Android/device test. Other agents' work remains untouched.

## Feasibility gates

These are conditions for the proposed design, not ten proven impossibilities.
The evidence column distinguishes an existing desktop assumption from a still
unknown feasibility result. Stop expanding the port at the first unmet gate.

| Gate | Evidence and risk | Required resolution before full implementation |
| --- | --- | --- |
| B1 Automatic patch and helper integration | The generated inventory lists 169 patch classes / 254 methods. Actual code also registers patches directly and uses reference-returning `AccessTools.FieldRefAccess`. Ordinary Android IL2CPP does not provide JIT/Reflection.Emit. | Generate complete effective hooks and required helper bindings from the chosen mod build. Prove current prefixes, postfixes, finalizers, dynamic registration/disablement and private-field helpers without hand-porting each patch. If normal changes require repeated manual Quest equivalents, the maintenance requirement fails. |
| B2 Original asset/script reconstruction | Original Mono DLLs and decompiled code are available, but no original Unity project is established. Prefab/scene script references, serialized UnityEvents, Addressables and shader source must survive recovery. | Rebuild one real original menu/scene and animated model with valid callbacks and asset/type identities. Unrecoverable essential assets or behavior would block a faithful port. |
| B3 Original multiplayer with only necessary EOS | `PlatformLayer.Init` exits when Steam is absent; initialization also constructs EOS. `SaveData.InitialiseDataManagers` can start EOS from persisted `Global.EpicLogin`. Multiplayer admission calls platform privilege providers. | Route local readiness and unavailable store capabilities at the platform boundary; join/host the original Photon session without Valve services. Audit whether EOS/Hydra/auth tickets are compulsory. If EOS is required, prove a legitimate Android flow accepted by the original backend. EOS is authorized for this purpose; the embedded Steam ID is still no credential. A compulsory excluded store service or unavailable authentication route would block crossplay. |
| B4 Finite bake coverage that survives mod changes | Authored scenarios still call procedural presentation. Seeds, quality, neighbouring rooms, runtime changes and the mod's generation-detail choices affect outputs. Excluding Guildmaster does not prove a finite visual catalogue. | Establish the real campaign state/seed/quality space and lifecycle contract. Prove export or deterministic assembly for the required combinations. If complete coverage needs unbounded geometry generation, the pure-bake approach must be reconsidered. Do not fix seeds silently. |
| B5 APK content paths and original rule hashes | `RootSaveData.CoreRulebasePath` is based on `Application.streamingAssetsPath`; `SceneController` enumerates/read-checks ordinary rule files. Mod loaders use `Assembly.Location` and `File` next to desktop plugin DLLs. Android StreamingAssets can be APK/JAR URLs. | Provide a coherent local content root/manifest and loader bridge. Prove original rulebase enumeration, DLC lookup, hashes, asynchronous asset loads and all mod banks. Packaging bytes is not proof that callers can read them. |
| B6 Original serialization and AOT closure | Actual save loading uses original binary/reflection-based serializers; the mod's new mesh index uses `DataContractJsonSerializer`. Private members, generated protocol tokens and generic methods need compiled code/metadata. | Preserve complete reflected types and generic instantiations, then prove PC save -> Quest progress -> PC continuation and original network token round trips. Replacing the save format or protocol would break the requested compatibility. |
| B7 Editor, XR and graphics intersection | The original editor is Unity 2021.3.5f1. Desktop XR is dynamically loaded/publicized; the current bundle builder explicitly targets Windows64. Native passthrough and shader/capture behavior need Android equivalents. | Find one pinned editor/package/NDK/JDK matrix supporting recovered content, ARM64/IL2CPP, OpenXR and passthrough. Prove both-eye card/UI/ghost transparency and captures. An upgrade that breaks original content is not a solved matrix. |
| B8 Sustained performance and memory | Frame606's nearly playable report is useful but not Quest evidence. The 49 figure banks occupy 294,017,137 installed bytes; loaded banks/meshes are retained for process lifetime. Baked scenarios may add more storage and resident geometry. | Measure an actual combined Quest scenario, voice/MR and repeated scene changes. Separate disk size from native/GPU RAM. Account for resident banks, texture decompression, room loading, thermal behavior and retained references. The accepted Frame result alone cannot clear this gate. |
| B9 Player-operated build/install/update | No clean ordinary-player Android conversion has run. Unity activation, actual editor availability, converter inputs, export duration, APK/content size and signing/update behavior are unresolved. | Produce the small build on a clean supported PC with only documented public tools and the player's game. Measure time/disk/memory, install locally, interrupt/resume and update without losing saves. Required private caches or an unavailable toolchain would defeat the proposed distribution workflow. |
| B10 Coordinated mod releases | `ModBuild` equality is enforced for modded peers. Quest code is compiled into its player; the desktop DLL replacement workflow is not automatically a Quest update mechanism. | Rebuild/install a Quest APK from the selected mod revision automatically, retaining package/signing identity and data. Prove same-build multiplayer and explicit mismatch handling. Resilient conversion does not remove the need to rebuild for code changes. |

### B1 includes runtime code generation outside patch installation

Moving Harmony patches before IL2CPP is insufficient if shared mod code still
constructs dynamic field-access delegates. `TooltipWindowPatches`,
`HexHighlightFix`, `HexHoverClear` and `HoverPickPatch` have such callers.
Upstream HarmonyX's `ReflectionTools.FieldRefAccess` creates a
`DynamicMethodDefinition` and emits the field-address access. This proves a
candidate dependency mechanism, not that an uninspected installed version has
identical internals. Pin and inspect the actual packaged helper version.
[HarmonyX field helper source][harmony-fields]

Generate equivalent typed accessors for discoverable fields during conversion,
including reference-return and mutation semantics. Keep AOT-compatible ordinary
reflection for metadata queries where suitable. A `link.xml` retains metadata;
it does not implement missing runtime code generation. Compile-time publicized
reference assemblies also do not automatically make the real target assemblies
valid at runtime. Check private-access bindings and the actual assembly graph.
[Unity AOT restrictions][unity-aot]

The current source search found no `CodeInstruction`/transpiler implementation
in `src/GloomhavenVR`; the real first fixtures should therefore include a
finalizer and direct registration instead of inventing an existing transpiler.
Future transpilers/reverse patches still need an explicit supported contract
or a precise build-time incompatibility report. Arbitrary future C# and dynamic
patch targets cannot be promised automatically portable to AOT.

### B5 and B8 include the new figure-bank workflow

`ScenarioFigureMeshBank` locates its JSON and split banks using its assembly's
filesystem location and computes source keys from mesh metadata, bounds,
readability and bindposes. Re-importing for Android can change relevant import
properties. A copied bank/index can therefore fail to load or fail to match its
new source even if the filenames are preserved.

Export explicit original-to-target mesh associations and rebuild permitted mod
content for the actual Android target. Preserve the intended skin/UV/LOD and
identity contract; do not match replacement meshes only by name. Confirm that
the immutable cache policy fits the combined Quest memory budget. Any lifetime
change must account for local/remote held figures, home ghosts and other live
references. Failure must identify missing converted content rather than quietly
claiming that original-only fallback proves the performance feature works.

Unity documents platform-specific bundle payloads and Android StreamingAssets
access. A filesystem staging/copy scheme can be appropriate, but must preserve
save isolation, source file hashes and loading behavior. No store downloader
or remote proprietary-content service is part of the fallback.
[Unity AssetBundle API][unity-bundles]
[Android StreamingAssets][unity-streaming]

## Builder architecture for ordinary mod development

### One mod source, a narrow platform layer and generated integration

The desired maintenance boundary is a single normal mod source tree plus a
small maintained Quest platform/bootstrap layer. Card, UI, town, rig,
multiplayer and optimization behavior must not have a second handwritten Quest
copy. The builder consumes a selected coherent mod revision and derives its
conversion inputs; it does not contain a list of individual historical builds.

Use these future inputs/contracts:

| Input | How to obtain/update it | Avoided manual maintenance |
| --- | --- | --- |
| Mod code and resources | Evaluate the selected project's actual Compile, EmbeddedResource, references and generated build-info items, then build against the chosen target references or use its suitable compiled IL. | No second list of `.cs` files, embedded artwork or copied gameplay/presentation methods. Reuse compiled IL is a candidate, not proven net472/Android compatibility. |
| Patch hooks | Generate a union of attributes, target selectors, actual module registrations and direct `Patch`/`Unpatch` calls, with priorities, ordering and activation semantics. | No manually updated per-patch Quest mirror. The current Markdown inventory alone omits some dynamically registered operations. |
| Reflection/AOT requirements | Derive referenced types, serializers, UnityEvents, field/delegate helpers and generic closure; use conservative preservation for unresolved reflection initially. | New ordinary reflected members do not each require a hand-edited preservation list. Unknown closure remains a reported gate. |
| Mod assets and banks | Use one versioned content-provider contract for source assets, generated mod assets, bank/index membership and runtime resource keys. Rebuild all selected outputs for Android. | No Quest list pinned to today's three main banks and 49 figure parts; additions participate in the same manifest. |
| Config, localization and protocol | Reuse current Bind/Loc/serializer code, keys and defaults. The Quest layer adds only explicit target capability/default substitutions. | No duplicate options catalogue, translation table or forked wire definitions. |
| Game/procedural export | Use a versioned exporter recipe with actual source input hashes, requested state/quality variants and source-to-target identities. | Normal unrelated UI/card/logging edits do not require a new manually edited scenario export recipe. |

A content-provider contract may need an initial refactor during authorized
implementation. Neither the contract nor automatic discovery exists today.
That initial work belongs in the feasibility assessment; it is not a promise
that the current desktop package is already a universal converter input.

### Generate effective behavior rather than just scanning attributes

`FfsNetTransport` installs its receive hook directly. `PerfNativeLoopProbe`
resolves a fixed target population at runtime. `TownServiceNativeAudioSilence`
registers prefix/finalizer combinations directly. Ignoring these paths would
produce an APK with missing behavior while the attribute inventory looked complete.

Investigate a build-time hook registry with pre-integrated candidate targets.
Runtime registration/disablement can then control which hooks are active through
an AOT-compatible facade, retaining configuration/lifecycle and order. Prove
multiple hooks, original-method skipping, by-reference arguments/results,
state association, exceptions and removal. Unknown target resolution or
unbounded patch variants must fail conversion with the responsible call site.
Do not execute arbitrary mod initialization on the builder's PC merely to
discover patches: scene/input-dependent registration may require Unity/VR and
can have side effects. Resolve pure metadata selectors in a controlled host;
report the rest until a supported mechanism exists.

The generic mechanism must cover new ordinary prefixes/postfixes/finalizers
without changes to the builder recipe. Introducing a fundamentally new runtime
patch model or native library can still require platform-layer work. That is
the honest limit of the requirement; no design can guarantee every future
desktop-only API or GPU feature works on Quest unchanged.

### Immutable input snapshots and dependency-aware caching

Select one mod revision, actual game input build and toolchain lock. Snapshot
the inputs before conversion so parallel development cannot mix DLLs, shaders,
source, generated resources and `ModBuild` from different moments. A supported
developer input can identify a dirty tree by its content hash; it must not
pretend to be the clean commit. Public builds use coherent release inputs.

Separate these cache boundaries:

1. Original asset recovery depends on original containers and recovery tooling.
2. Procedural export depends on the original generator, relevant exporter/mod
   generation inputs, configuration/state space and extraction recipe.
3. Android mod assets depend on source/generated content, importer settings and
   the selected platform/shader/editor recipe.
4. Patch/helper integration and AOT compilation depend on current mod IL/code,
   actual target assemblies and compatibility-layer/toolchain versions.
5. Personalization/signing depends on the locally captured identity and stable
   player key; it does not invalidate unrelated recovered assets.

A normal log/text/UI fix should normally reuse game recovery and procedural
outputs while rebuilding changed code. A shader/bank change should rebuild its
asset closure. A generation-policy change must invalidate every affected export
variant. When the dependency closure cannot be established, invalidate more
rather than reuse stale data. Content hashes must reflect actual relevant
inputs; excluding the new generation code to keep a cache warm is incorrect.

Neither the mod SHA alone nor `ModBuild` alone is a sufficient cache key.
Ignoring the mod entirely misses generation changes; invalidating the complete
campaign for every log change makes ordinary iteration impractical. Generated
timestamps/build banners should not churn unrelated pure-content caches.

Failed stages leave no success receipt. Resume only from hash-verified completed
stages. Never mutate the original install/reference tree or overwrite another
agent's build inputs. Preserve the player's key, package name and saves across
APK updates; ordinary AOT code changes still require a new APK installation.

## Proof that the maintenance requirement is met

Before calling the builder maintainable, run an N -> N+1 development rehearsal
after implementation is authorized. Keep the builder recipe unchanged while
making representative ordinary mod changes:

| Change | Expected generated result |
| --- | --- |
| Edit card/window behavior or logging in an existing shared class. | New code appears in Quest; unrelated game recovery/export is reused. |
| Add a normal patch class or direct-registration target using supported semantics. | Discovery and integration include it with correct activation/order; no handwritten Quest counterpart. |
| Add a config option, EN/DE strings and embedded artwork through the normal project. | All items reach Quest through the evaluated project/resource inputs. |
| Add/change a shader, prefab or split-bank entry. | Android content and its runtime manifest update automatically; no desktop-only bank slips through. |
| Add an additive wire record and bump ModBuild through normal mod development. | Quest compiles the same definitions and matches the coordinated desktop build; golden vectors still pass. |
| Change procedural detail inputs. | The relevant export set is invalidated; unaffected source recovery is reused. |

Include negative controls: an unresolved helper field, an unexpected native
dependency, missing generated bank, mismatched DLL/assets/source revisions and
unknown dynamic target must each produce a bounded useful failure. The builder
must name the file/member/stage and remedy. Merely refusing every newer ModBuild
is not resilience; silently skipping unsupported behavior is not compatibility.

Some changes will still expose new portability work. The target is automatic
handling of ordinary supported development plus early, precise detection of
new architectural dependencies, not a guarantee of zero maintenance forever.

## Order before investing in the complete port

First complete the read-only dependency/patch/helper inventories, actual
serialized-content audit and the original multiplayer/EOS dependency audit.
Record the proposed toolchain and content contracts. These can reduce uncertainty now without
running a port experiment.

After Steam Frame acceptance and explicit authorization, prove B1/B2/B3 with
small real fixtures, then one complete scenario/state cycle for B4/B5/B6/B7.
Measure B8 on that combined device build before bulk campaign conversion.
Rehearse N -> N+1 plus a clean-PC build/install/update for B9/B10. A full campaign
export and polished builder UI come after these gates.

No gate is cleared by this planning update. Some require execution on Android
and a headset; source inspection alone cannot settle them. The main documents
remain the [overall plan](QUEST3-PORT-PLAN.md) and
[critical solution paths](QUEST3-CRITICAL-SOLUTIONS.md).

## Source references

- [Current patch inventory](../docs/PATCH-INVENTORY.md),
  [mod project](../src/GloomhavenVR/GloomhavenVR.csproj),
  [RuntimeDepsLoader](../src/GloomhavenVR/Core/Startup/RuntimeDepsLoader.cs),
  [TooltipWindowPatches](../src/GloomhavenVR/WorldUI/Patches/TooltipWindowPatches.cs),
  [FfsNetTransport](../src/GloomhavenVR/Net/FfsNetTransport.cs),
  [PerfNativeLoopProbe](../src/GloomhavenVR/Core/Perf/PerfNativeLoopProbe.cs) and
  [town audio registration](../src/GloomhavenVR/WorldUI/Modal/ModalFallback.TownServices.cs).
- [PlatformLayer](../decompiled/GH.Runtime/PlatformLayer.cs),
  [PlatformUserData](../decompiled/GH.Runtime/PlatformUserData.cs),
  [SaveData](../decompiled/GH.Runtime/SaveData.cs),
  [RootSaveData](../decompiled/GH.Runtime/RootSaveData.cs),
  [SceneController](../decompiled/GH.Runtime/SceneController.cs) and
  [original NetworkManager](../decompiled/GH.Runtime/FFSNet/NetworkManager.cs).
- [ScenarioFigureMeshBank](../src/GloomhavenVR/Core/Perf/ScenarioFigureMeshBank.cs),
  [Windows bundle builder](../unity/GloomhavenVR.Assets/Assets/Editor/BuildBundles.cs),
  [HandVisuals](../src/GloomhavenVR/Hands/HandVisuals.cs),
  [Frame606 analysis](../docs/performance/FRAME-606-ANALYSIS.md) and
  [generation detail](../src/GloomhavenVR/Core/Perf/ScenarioGenerationDetail.cs).

Primary external documentation/source was checked on 2026-10-02. Recheck the
actual installed versions at implementation time.

[harmony-fields]: https://github.com/BepInEx/HarmonyX/blob/master/Harmony/Internal/Util/ReflectionTools.cs
[unity-aot]: https://docs.unity3d.com/2021.3/Documentation/Manual/ScriptingRestrictions.html
[unity-bundles]: https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AssetBundle.html
[unity-streaming]: https://docs.unity3d.com/2021.3/Documentation/Manual/StreamingAssets.html

## 2026-10-05: novice Windows Wizard and interruption recovery

The maintainer requested a Wizard that provisions its own tools, uses the
player's locally owned Steam/Epic/GOG installation, and resumes after cancellation
or a subsequent restart. This is a required future delivery workflow. The
complete Campaign APK remains the current implementation objective; the Wizard
and the optimizations below are not implemented or validated by this audit.

The intended flow is game discovery/selection, local profile and purchased-DLC
confirmation where evidence is ambiguous, tool provisioning, conversion/build,
then headset installation. Persist these choices and their immutable input
identities before starting expensive work. Show one overall progress view with
the current phase, completed work, and an explicit resumable cancellation action.
Python, Android platform-tools, converters, and the pinned Unity Editor/modules
should be provisioned automatically. Unity Personal activation requires the
user's account/sign-in and license acceptance; the Wizard must explain that step
and detect completion rather than promise unattended activation. The first ADB
authorization on the headset also remains a visible user action. Official
[Unity installation](https://docs.unity.com/en-us/unity-cli/use-unity-cli) and
[license management](https://docs.unity.com/en-us/hub/manage-license) documentation
was checked on 2026-10-05; the actual pinned 2021.3.5f1 installation path must be
rehearsed on clean Windows before claiming that this flow works.

### Measured first-build costs and cache ownership

The actual complete-content cold import took 5,754.362 seconds (95.9 minutes),
with 131,902 imports and no cache hits. Changing Android settings afterward
caused another 2,965.486-second refresh (49.4 minutes), importing 14,986 assets
without source file changes. These are host build times, not headset startup
times. Moving final settings before the initial import is a concrete optimization
candidate; a shortened total duration has not been measured.

The present project directory includes the complete input key, including mod
revision/profile changes. Preparation excludes Library when copying and removes
an invalid prepared project. Consequently ordinary mod updates can discard
expensive imported game artifacts. Use a stable, exclusively locked local asset
workspace with separate keys for original game recovery, target/import settings,
native tools, mod artwork banks, mod code, profile/entitlement, and packaging.
Apply only owned changed overlays atomically and preserve valid Library/SBP/native
compiler caches. Actual dependency identities and original GUID/localID contracts
remain mandatory. SDK compilation can be isolated into a small pinned-package
project while the complete original-content import and final Player gates remain
required. Repeated full-game hashing and unnecessarily broad cache keys also need
measured review. Do not claim a five-minute first build without cold/warm, mod
update, and DLC-change measurements on Windows.

The Wizard must distinguish caches useful for the next update from superseded
APKs and temporary outputs. Offer retaining the reusable conversion cache or
freeing its disk space after success. Automatic cleanup may remove only owned,
superseded artifacts; original installations, saves, signing identity, active
build workspaces, and other agents' worktrees are never cleanup targets.

### Required resume semantics and present gaps

Verified completed stage receipts and immutable snapshots already provide useful
reuse. They do not yet establish arbitrary interruption recovery. The source
audit found interrupted bundle/core directories that reject on restart, locks
left by hard process death, no complete owned-child shutdown policy, repeated SDK
compilation, extraction/download staging left incomplete, and no persisted Wizard
choices. Addressables repacking also rewrites preparation-hashed content
manifests; a late cancellation can then invalidate preparation and cause the
imported project to be deleted. Original content moved temporarily for a build
needs transactional recovery as well.

Implement durable per-stage states and atomic output publication. On resume,
verify committed outputs, repair only the interrupted stage's owned temporary
outputs, and preserve every still-valid dependency cache. Final repack outputs
must have a separate stage contract rather than invalidate preparation. Validate
a stale lock against its recorded process ownership before recovery; shut down
the owned process tree gracefully on ordinary cancellation. Handle an incomplete
first signing-key/metadata pair explicitly without silently changing the signing
identity. Record content-transfer/APK-install handoff independently so a failed
installation retries without conversion or deleting app data. Test cancellation
and forced death during download, recovery, import, repack, compilation, signing,
and installation. A valid unchanged import must survive each restart.

A universally distributable APK is not established by removing textures: the
current IL2CPP Player contains original game code. Distributable generic tools
and recipes therefore remain separate from locally generated game-derived
outputs. Direct local ASTC/native texture conversion is a narrower first-build
research candidate, not a proven replacement for Unity import; normal-map
channels, mipmaps, sprites, fonts, and precision-sensitive textures require exact
format and visual checks before using it.

### Additional bounded Windows and shader-cache audit

Read-only review of the complete retained Assets tree counts205,024 files, with
maximum project-relative length183 and output-relative length257. Even an output
root directly at `C:\` puts11,656 paths at260 characters or more; `C:\GHQ` gives264
for the largest path, and the example user's Downloads build directory gives307.
The generated shader include basename concatenates two64-character hashes
(`produce.py`, generated DXBC/interface include path), followed by `.hlsl.meta`.
Use one full SHA256 of that unambiguous fixed-width pair as its generated basename,
retain both original identities in the program manifest, and regenerate the
include/program references coherently. This saves65 characters without changing
original shader GUID/meta/localID identities. Rehearse actual Unity/NDK temporary
and generated paths too; this Assets census does not prove every child tool's
long-path support. Merely changing the registry is insufficient for applications
that have not opted in, per Microsoft's
[long-path contract](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation).

The Windows producer currently serializes `str(Path(...))` for generated program
paths; canonical `as_posix()` output and a Windows fixture are required. The C#
consumer already normalizes program paths, so this review does not establish an
end-to-end compiler failure from backslashes alone. The actual Linux Player is
unaffected by these future recipe changes.

`dependencies.python_environment` keys its owned venv only by requirements,
without checking its interpreter/base executable or native-wheel ABI before
adding site-packages to the running process. Persist the pinned interpreter/ABI
identity and verify it before reuse. A mismatch may replace only the owned tool
environment, preserving valid game/import/signing caches. Current full-game
`builder.py` also requires Git and a real checkout; an ordinary source-release
ZIP is not yet sufficient. Provision a pinned portable Git checkout or implement
a separately verified release-source manifest mode rather than exposing Git
configuration as a novice's prerequisite.

For cancellation, supervise owned child processes as one Windows Job Object,
request a graceful stop, then bound termination and wait for children before
releasing the output lock. Recovery/export currently terminates only its direct
child, while compiler/download launches can leave descendants running. Microsoft
[Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
provide process-group ownership; existing unrelated Unity/license processes must
remain outside that ownership. This supplements the durable stage/file recovery
requirements above and is not an implemented Wizard feature.

The shader audit identifies repeated work, without performance measurements:
PrepareVariantCollection and Validate each parse the72.6MB manifest and validate
11,656 includes (approximately610.8MB). The native loop repeats query, SMOL-V
decode, reflection/binding joins and payload hashes for all51,564 original aliases.
Historical complete evidence contains11,212 bank/stage pairs,1,799 vertex payloads
and7,904 fragments, with20,915 unique output files. Duplicate output checks cause
133,777 extra file reads/hashes totaling about3.365GB. Those are counts and byte
volumes, not elapsed-time claims.

Prioritize one dependency-closure validation per immutable stage with a final
before/after identity check; decode/reflection reuse keyed by the actual returned
bank SHA plus exact texture-binding signature; output-file verification once per
immutable output snapshot; and persisted complete local shader evidence keyed by
actual shader/importer/dependencies/settings and exact Editor/compiler/helper
bytes, independently of ordinary mod C# changes. Preserve all688 identities,
material references, every51,564-alias ledger entry and every per-alias original
interface check, with corrupt/stale-cache negatives. Output equivalence cannot
justify omitting an uncompiled input. Root's six retained shader contracts differ
from historical translated inputs; actual Root bytes govern reuse. These
optimizations remain future work pending measured cold/warm Windows builds.

### Maintainer ruling: minimum production builder work

The maintainer clarified on2026-10-05 that the exhaustive51,564-alias shader
matrix is development validation, and must not run routinely in the later
player-facing Builder. The current QuestBuild path still calls that full matrix
unconditionally; this is a present implementation gap, not existing production
behavior. Keep this first complete development Player's gates intact while
recording the future separation explicitly.

Introduce distinct developer-validation and normal-player build contracts. The
normal build still performs the player's necessary real Android conversion and
Unity compilation, retains all required native variants, and checks basic
completeness, recognized input/tool identities and native build errors. It reuses
valid local artifacts and reruns only affected dependencies. Ordinary mod C#,
profile or package changes must not invalidate unrelated game shader/import work.
The exhaustive native query/decode/reflection/driver/readback matrix belongs to
development, release recipe validation and genuinely changed supported shader
closures. Do not describe caching that still repeats the full matrix on every
first player build as satisfying this ruling. This separation is required future
Builder/Wizard work; it is not yet shipped or measured on Windows.

The maintainer further requires expensive shader validation only when necessary
in development as well. An unrelated Player/IL2CPP/package failure must reuse the
completed native shader gate when its relevant graphics closure is unchanged.
Implement a completed actual validation receipt and atomic reuse contract, bound
to exact source/includes/importers/dependencies, graphics/compiler settings and
Editor/compiler/verifier identities, retaining complete alias/material ledgers
and output hashes. Changed relevant inputs or corrupt/partial results require
validation; ordinary mod/profile/package changes do not. The first actual full
imported Android Campaign gate remains in progress and necessary at this
checkpoint. Cache integration must not modify the live build's captured sources
or invent completion before that gate reports success.
