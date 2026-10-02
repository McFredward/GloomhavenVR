# Quest 3 critical solution paths

This document turns the risks in the [Quest 3 port plan](QUEST3-PORT-PLAN.md)
into concrete future work. Each path has an initial experiment, a deliverable,
an acceptance condition and a response to failure. None of these experiments
has been implemented or passed. Implementation remains deferred until Steam
Frame acceptance and the maintainer's subsequent instruction.

**Planning revision: 2026-10-02.** The latest maintainer decision is a locally
built, sideloaded APK with the original PC account profile embedded. There will
never be a registered Meta app for this project. No Horizon services, registered
profile helper, device binding or new ownership server are part of the design.
Personalization and simple installation/provider checks are intentionally a
small sharing hurdle; modifying the open-source builder can bypass them.

Source inspection started from ModBuild 605 and was refreshed while other
agents advanced `dev` through `370b9ef5`. This is a dated assessment, not an
Android compatibility result. All repository game references remain read-only.
Tool and component names below describe candidates and contracts, not new
implemented classes or a finalized dependency lock.

## Recommended approach and order

Build a genuine Unity Android ARM64 player from locally reconstructed assets,
preserved original managed logic and mod behavior integrated during the build.
Use the original PC runtime as the first procedural export host. Embed the PC
profile and route device capabilities through a separate Quest target adapter.
Keep the original Photon gameplay protocol. Use native OpenXR passthrough in
the existing rig.

Resolve the expensive dependencies with small experiments before scaling up:

| Order | Experiment | Stop expanding until |
| --- | --- | --- |
| 1 | Import one Steam account profile and build a minimal ARM64/OpenXR probe. | The profile works offline, passthrough works on Quest, and the selected editor/toolchain is recorded. |
| 2 | Recover one original scene, its UI and one animated character. | Original scripts and serialized asset references load correctly; key shaders work in both eyes. |
| 3 | Export one complete scenario from the owned PC runtime. | A baked room, door, prop and reveal lifecycle work without the Windows Apparance library. |
| 4 | Connect the minimal real Android game to an unmodified PC host. | Original admission, character assignment, several rounds and save/rejoin succeed. |
| 5 | Prove representative mod patches integrated before IL2CPP compilation. | Prefix/postfix/transpiler behavior, startup and network receive semantics agree with desktop. |
| 6 | Measure the combined scenario, existing VR features, MR and voice. | Memory and frame time support continued investment; discrepancies have a concrete cause. |
| 7 | Expand campaign coverage, special mechanics and the multiplayer matrix. | Export coverage is complete and missing combinations have an explicit disposition. |

Orders 1 and 2 can discover independent blockers. The network test should run
as soon as the original game bootstrap is usable; it need not wait for every
VR subsystem or all campaign exports. The master plan's phases still govern
release acceptance.

## Importing the PC profile and adding the sharing hurdle

### Preferred path

Capture the actual account context during conversion on the PC, then write an
immutable profile asset into the generated APK. A generated JSON/resource or
ScriptableObject is sufficient; a C# string literal is unnecessary. The asset
is personalized build input and never belongs in the public repository.

The payload should contain the source provider, complete account ID, display
name, optional decoded avatar, provenance, schema version and source build.
Store no passwords, launcher cookies, access tokens, authentication tickets or
publisher credentials. An account ID and name are presentation/association
data, not an authentication credential.

Use these acquisition paths in order:

| Source | First path to investigate | Limit |
| --- | --- | --- |
| Steam | Read the owned player's `PlatformUserData` in its normal Steam account context: `UserName`, full `PlatformPlayerID`, account fields and completed avatar callback. | `SteamClient.Name`/Steam ID come from the PC client, not generic asset files. Preserve the distinction between a full Steam ID and its 32-bit account number. |
| Epic | Inspect the player's actual Epic build/provider and its successful existing user-info path. Capture its display name and correct account ID. | The inspected Steam build's Steam getters are not an Epic implementation. An EOS product-user ID and Epic account ID are different identifiers. |
| GOG | Inspect the actual GOG build/Galaxy provider; obtain its signed-in account's persona name and Galaxy ID where available. | A DRM-free installation without Galaxy login may contain no reliable account identity or authenticated ownership evidence. |

Steam documents the current persona name and avatar access through its client;
GOG documents its persona-name getter. These are candidate PC-side routes, not
headset SDK dependencies. [Steam profile API][steam-profile]
[GOG profile API][gog-profile]

The first supported converter input should be one inspected Steam game build.
Add Epic/GOG converter inputs after their profile and content paths are proved.
This does not restrict the requirement to play with compatible Epic/GOG peers.
Store compatibility and multiplayer peer compatibility are separate work.

If a supported provider cannot supply a usable name or ID, stop that import
stage with a useful explanation. A known local profile record can be an
alternative if its provider/version and account association are established.
Do not silently select the most recently cached account, the operating-system
login, an arbitrary save owner or a manually typed name as verified identity.
Any manual-name fallback for a DRM-free source remains a later explicit product
decision. A missing optional avatar can retain the original placeholder.

Perform simple local installation and available provider ownership/DLC checks
before export. On Steam, prefer the owned original player's legitimate account
context and its existing checks; Steam documents client subscription and
installed-DLC checks. Record unavailable or ambiguous evidence rather than
inventing a receipt. Generic hashes identify a supported build; they do not
prove purchase. [Steam application/DLC checks][steam-apps]

After conversion, ordinary offline play requires no PC or platform account
service. A refreshed local build updates a changed display name or avatar while
retaining the stable account identity, saves, package name and signing key.
PC storefront overlays cannot operate unchanged on Quest; route required
account/invite entry points through the target adapter and classify any
unavailable feature explicitly rather than leaving a nonfunctional button.

### Initial experiment and acceptance

Capture one actual PC profile without touching the original install or cloud
saves, show it in a minimal Quest menu and relaunch offline. Refresh the name
or image without changing save ownership. Exercise a second PC account to
prove account selection is deliberate. Confirm that the generated APK contains
the profile and no authentication material.

Acceptance means the local profile works without a Steam client on Quest or a
Horizon request. It does not mean purchase authentication or sharing prevention
has been achieved. A copied APK would show the imported account; that is the
intended inconvenience. Duplicate imported identities in the same session must
follow the original admission behavior, not a new identity-based DRM rule.

## Recovering assets and an Android project

### Preferred path

Use a pinned AssetRipper release as a candidate for recovering the player's
static Unity content into a local staging project. Its official project
documents conversion into Unity's native asset format and version-dependent
support quality. That is a starting tool, not proof of a complete rebuild.
[AssetRipper project][assetripper]

Separate recovery into three manifests:

- **Original assets:** source container/path ID to recovered asset GUID and
  local file ID, including scene objects, materials, textures, animation,
  fonts, audio, prefabs and MonoScript references.
- **Runtime-generated presentation:** procedural output from the PC exporter,
  linked to the corresponding original room/prop and source asset provenance.
- **Android build outputs:** target editor, graphics API, texture settings,
  bundle names, dependencies and address-to-asset mappings.

Keep original game assemblies as local build inputs where suitable. Do not
import those assemblies and also compile an extracted copy of their classes.
Configure recovery to retain original MonoScript assembly/namespace/type
identity, or repair the generated script-reference mapping explicitly. Verify
serialized references to fields, UnityEvents and components after import.
Recovered YAML and a C# compilation alone do not prove a functioning scene.

Inventory actual Addressables keys/catalogue entries and legacy bundle loads
used by startup and the chosen scene. Rebuild Android payloads and catalogue
locations while preserving keys expected by the original code. Add a targeted
loader adapter only where the original Windows path cannot address the new
layout. Preserve asynchronous completion, lifetime and failure behavior; avoid
replacing every original loader with a new synchronous implementation.

Rebuild the mod's own bundles for Android from its existing asset project.
Keep desktop bundles and their exact-editor gates independent. Do not copy a
Windows bundle or rewrite only its header and treat that as Android content.

Create a material conversion table: source shader, property names, textures,
alpha/depth/blend behavior, animation-driven properties and Android equivalent.
First prove original/recovered shader source compilation. If only a desktop
compiled shader survives, implement an equivalent for that observed shader
family and validate it; extracting D3D bytecode does not recover portable
shader source automatically. Preserve the built-in render pipeline initially,
rather than coupling reconstruction to a URP migration.

### Initial experiment and acceptance

Recover the smallest complete original menu/scene slice and one animated
character with its controller, materials and UI dependencies. Start with the
original editor version where the selected Android/XR packages support it.
Render on Quest using one explicitly selected graphics API; evaluate Vulkan
and OpenGLES3 separately if their results differ.

Record unresolved script/asset references, source/recovered object counts,
successful original UI callbacks, animation state and both-eye pixels. A pass
requires usable native UI, a correct animated model, audio where applicable,
save/reload and no missing dependency hidden by a generic fallback.

If whole-project recovery fails, keep the successful dependency slice and use
targeted recovery plus a small bootstrap scene referencing original components.
For unrecoverable presentation assets, investigate runtime extraction of the
owned player's already loaded objects. Neither alternative authorizes
recreating gameplay, copying proprietary content into the repository or
silently substituting different artwork.

The deliverable is a reproducible recovery recipe and dependency manifest,
not a tracked recovered game project. Pin tool licenses and redistribution
conditions separately from locally exported game content.

## Building the original logic and mod for ARM64

### Preferred path

Use the standard Unity ARM64/IL2CPP pipeline. Unity documents that its Android
Mono backend does not support ARM64. A custom Mono runtime is a separate engine
port and is not the proposed shortcut. [Unity ARM64 requirement][unity-arm64]

Keep suitable original managed assemblies local, retaining type identities,
serialization and original networking code. Compile the mod's shared behavior
against the actual target Unity references. Split its desktop bootstrap from
an integrated Quest bootstrap that runs before the original game's platform
startup, so no Steam/Apparance Windows entry point executes first.

The existing mod relies on more than Harmony: `Plugin` derives from
`BaseUnityPlugin`, and shared code uses BepInEx configuration and logging.
Inventory these usages. Reuse proven AOT-compatible configuration/logging
components where possible, or provide a narrow compatibility layer retaining
the existing keys, defaults, events and normal/Debug logging policy. Replace
the desktop loader lifecycle, not all gameplay/presentation modules.

Translate the existing patch inventory into a build-time patch manifest for
the player's staged assemblies. Mono.Cecil is a candidate for inspecting and
rewriting CIL before Unity converts it to native code. Its documented editing
capability does not automatically reproduce Harmony semantics. [Cecil][cecil]

For each patch, record the original target/signature/hash, patch priority and
ordering, target platform, required injected fields and expected behavior:

- Prefixes must preserve argument changes, `__result`, `__state`, and the
  exact condition that skips the original method.
- Postfixes and finalizers must preserve execution/exception behavior and
  state association. Preserve multiple-patch ordering explicitly.
- Transpilers must match the expected original instructions and fail on an
  unsupported input rather than applying a guessed edit.
- Dynamic `Patch`/`Unpatch`, reverse patches and optional-feature guards need
  individual static equivalents. Build-time integration cannot rely on those
  desktop runtime operations still being available.

Use a bootstrap/bridge arrangement whose assembly reference graph and
initialization order are valid in the selected IL2CPP build. Validate that
graph before committing to injected direct mod calls. Preserve script-facing
type identities and do not assume renaming or merging the game assembly is safe.

All rewriting occurs on disposable local staging copies during future
conversion. Do not modify `ressources/`, `libs/`, `decompiled/`, the source
installation, original rules or Photon Bolt. Keep `ScenarioRuleLibrary` and
`FFSNet.NetworkManager` unchanged; route platform calls through their existing
providers and use the existing mod transport seam. Unsupported game builds
must fail their patch-manifest checks.

Generate conservative initial `link.xml` preservation for original reflected
types, serializers, protocol tokens, UI callbacks and native delegates.
Enumerate required generic instantiations and remove/replace actual runtime
code-generation paths. Later shrink preservation only with evidence. Unity
documents reflection stripping, generic AOT edges and the lack of
`Reflection.Emit` on AOT; reflection itself remains usable when code survives.
[AOT restrictions][unity-aot] [Managed stripping][unity-stripping]

### Initial experiment and acceptance

Integrate three real examples: a prefix that suppresses a native path, a
postfix with state and a transpiler. Include the mod's action receive prefix
early. Compare their observable behavior against the same desktop mod and
exercise toggles, cancellation and exceptions where relevant.

Then start the real game, deserialize a save and exchange original tokens in
an IL2CPP development build. Acceptance requires preserved patch behavior,
working reflected data and no unsupported desktop/native initialization.
Only then translate the complete patch inventory.

If a patch cannot be represented faithfully, redesign that specific seam as
an explicit port callback and validate it against the original behavior. Do
not install another runtime detour package and assume parity, or recompile the
entire decompiled game to solve one integration point.

## Replacing Apparance with exported presentation

### Preferred path

Drive the original owned Windows player in an isolated export workspace so its
existing Apparance runtime, assets and game-specific procedure library create
the presentation. This avoids needing an independently reconstructed editor
host for the first experiment. Run it with the renderer needed by its assets;
do not assume a graphics-free batch invocation can produce everything.

After native content placement completes, capture the generated object graph
below each procedural owner. Preserve `Generated Content`, `Preview`, hierarchy
order, layers, local transforms and original gameplay-facing anchors. Capture
mesh buffers, normals/tangents/UVs, submeshes, material recipes, shared asset
references, colliders and necessary animated/effect presentation. Deduplicate
immutable assets by content hash; keep movable doors/props separately addressable.

Do not clone a running scene's gameplay controllers into a second active copy.
The retained original room/prop owner remains responsible for game behavior;
the export supplies its presentation and proven required components. Define
an explicit component whitelist and dependency references rather than dumping
every MonoBehaviour or flattening everything into one renderer.

Identify each export by the actual procedure and parameter inputs, not only
scenario number: source/procedure version, object identity, geometry, biome,
sub-biome/theme/tone, scenario/room/object seeds, quality, neighbouring-room
context and applicable overrides. `ProceduralStyle.WriteStyleParameters` also
writes sibling index/count and symmetry group; these belong in the key or must
be proved irrelevant. Preserve runtime hierarchy ordering where it affects them.

The Quest adapter suppresses native synthesis and supplies the exact matching
presentation. It must honor this proposed lifecycle:

| Step | Required action |
| --- | --- |
| Start | Call the owner's existing placement-start path and acquire an operation/generation identity. |
| Load | Resolve and instantiate the matching presentation asynchronously; cancelled/stale work cannot attach to a replacement room. |
| Prepare | Restore hierarchy, materials, colliders and visibility dependencies before declaring readiness. |
| Complete | Invoke the owner's original virtual `NotifyContentPlacementComplete()` exactly once for the valid operation. |
| Reveal/change | Let original room/door visibility and effect controllers operate on the restored objects; use explicit exported variants only where required. |
| Remove | Honor removal-start/completion, invalidate pending work and release instance ownership without destroying shared dependencies. |

The completion call is essential: `ProceduralBase` sets readiness and notifies
ancestor `IProceduralContentMonitor`s; `ProceduralMapTile` restores layer 10,
generation state and visibility; `ProceduralProp` invokes
`PlacementCompleteAction`. Setting one ready flag or hiding the loading screen
would bypass real continuations.

### Establishing finite coverage

Inspect all campaign/DLC definitions' actual `RandomiseOnLoad`, scenario seeds,
Apparance overrides and scripted room/prop changes through the original data
model. A sample `.lvldat` is a serialized `CCustomLevelData`, not plain JSON;
archive counts or string searches are not a complete semantic inventory.
Exercise more than one party size and scenario entry/save state.

Partition observed outputs by what is genuinely required:

| Category | Treatment |
| --- | --- |
| Fixed authored inputs | Export the exact keyed result, preserving every shared presentation dependency. |
| Finite door/prop/style states | Export reusable parts and the finite state/attachment rules. Preserve intermediate animation. |
| Cosmetic seed changes affecting only placement of existing pieces | Investigate a deterministic managed assembler reproducing the original selection and transforms. This is a small runtime presentation component, not native synthesis. |
| Seed/context changes producing new geometry | Prove a finite catalogue or an equivalent parametric generator; samples do not establish exhaustive coverage. |

Keep gameplay RNG consumption, original seeds and state untouched. A managed
assembler must use separately reconstructed presentation semantics, not
consume random values from the authoritative game sequence. Do not map unknown
keys to a visually similar baked room silently.

### Initial experiment and acceptance

Use one ordinary multi-room scenario, then one with unusual scripted geometry.
Capture original parameter keys and outputs. On Quest, open every door, reveal
rooms, create/remove props, leave/reload and resume a save taken after changes.
Compare fixed inputs with the original hierarchy, visible geometry, picking
and continuation trace. Verify that no Apparance Windows import is invoked.

The pass includes a complete lifecycle and a defensible coverage model, not
just an attractive screenshot. Before exporting 95 base scenarios, report
unique keyed asset sizes and the first unexpected key classes.

If the seed space prevents complete offline representation, investigate a
compatible vendor-provided Android runtime before proposing a fixed cosmetic
seed. Apparance's official platform page lists shipped custom runtimes and
directs mobile support enquiries to its team; it does not promise an available
Gloomhaven-compatible Android binary. ABI, procedure version and licensing
would all need resolution. This plan sends no enquiry. [Apparance platforms][apparance]
An equivalent narrow generator is another research path. Neither is assumed
easy; freezing appearance would require an explicit later parity decision.

## Original multiplayer and platform authentication

### Preferred path

Retain the installed Photon/Bolt versions, generated event/state IDs, token
registration, serialization and real game network version. Recover the
player's original configuration locally without publishing service credentials.
Use original session codes as the first entry path and preserve the current
crossplay settings. A new Photon application or protocol would not join the
unchanged game's sessions.

Route the original platform service requests through a Quest target adapter:
local profile, file access, owned/available DLCs, connectivity, privileges,
account UI and session/invitation lifecycle. Readiness must finish or fail with
an actionable outcome; it cannot wait forever for a removed desktop SDK.
An imported profile must not be represented as a successful live Steam login.

The installed source separates these concerns:

- `NetworkManager.GetUserToken` reads existing `PlatformLayer` profile/provider
  fields. Its normal session flow calls the platform privilege services.
- `GHNetworkCallbacks.ConnectRequest` compares the real network version,
  capacity and crossplay flags before basic admission checks.
- `PlatformLayer.InitialiseOtherPlatformEndpoints` waits for EOS authorization.
  `PlatformUserData.StartLoginFlow` attempts persistent/exchange-code Epic
  authentication. This needs a complete call-site/dependency audit even though
  the initial gameplay connection uses Photon.

Determine whether EOS/Hydra is required by startup, gameplay admission,
console interoperability, voice or only a particular invite/social route.
Do not force an authorization flag to true. For required EOS operations, find
a permitted Android implementation and a supported original authentication
flow; an imported Steam ID supplies neither a ticket nor an EOS identity.
For genuinely unused startup endpoints, provide a target-specific completed
capability result at the provider boundary, with all consumers checked.
Existing features that are actually required cannot be silently discarded.

### Initial experiment and acceptance

First produce original token round-trip evidence using captured legitimate
structure/version metadata, without copying account secrets into test vectors.
Then use an unmodified PC host with crossplay enabled and a Quest client:
join by code, receive the campaign/save, assign characters, finish ability
selection, execute several rounds, open a door, return to town and rejoin.
Repeat with Quest as host. Log the stage of any failure separately: local
provider readiness, Photon startup, session lookup, original admission,
save transfer or game desynchronization.

Add a second modded client behind the unmodified host and prove the existing
side-action relay on devices. Keep `GVR1`/Version 3 and coordinated ModBuild
compatibility; no new wire ID is reserved by this plan. Existing same-build
guard and explicit flat-net behavior remain.

Acceptance requires actual play and save/reconnect parity, not only opening a
socket. If a publisher-controlled backend requires an authentication mechanism
that cannot be supplied legitimately on Android, document that exact gate.
Do not bypass it or claim that a separate Quest server still meets the original
cross-platform requirement. Keep later Epic/GOG/console tests separate from
the first Steam success.

## Reusing native Steam, Epic or GOG APIs

### What ARM64 does and does not solve

A matching CPU instruction set is only one requirement. Check the binary's
operating system, ABI, runtime dependencies, exported interface versions and
service authentication. A macOS ARM64 library is not an Android plugin, and a
Linux ARM64 library may depend on Linux services/libc absent from an Android
application. An Android binary still needs its expected platform services.

Valve's API overview describes `steam_api` as an interface to the running Steam
client: the client implements the services and carries their backend connection.
It explicitly requires the client for initialization. The documented macOS
library includes ARM64; that is not proof of a Quest-compatible implementation.
[Steamworks initialization and architecture][steam-sdk]

Steam Frame runs SteamOS and supports APKs through Lepton. Valve even documents
Android Auto-Cloud configuration for APKs published through Steam. These are
valuable Frame capabilities, but do not establish that a sideloaded Quest APK
has the Steam client, Lepton's integration or the game's configured Android
cloud path. [Steam Frame execution models][steam-frame]
[APK distribution and Android cloud paths][steam-apk]

No complete Android Steamworks/client package for this Quest use was established
by this review. Do not treat that as proof that future Valve support is
impossible. Before selecting a binary, inspect the actual authorized SDK package
and document what provides its client/service dependency.

### Candidate paths and first experiments

| Candidate | Useful result | First proof and limit |
| --- | --- | --- |
| Official Steam ARM64 library | Reuse original managed bindings if the exact Android ABI and services are supported. | Check the actual SDK, then initialization, callbacks and one real service on a clean Quest without a PC. A library loading successfully is insufficient. |
| Port the full Steam client/environment | In principle retain much more of the existing platform behavior. | Requires client components, authentication, IPC, OS/runtime integration and permitted distribution, rather than recompiling one public API wrapper. No supported complete Quest route was established; keep this outside the first native-game approach. |
| Implement the platform functions Gloomhaven actually calls | Retain game-facing contracts with local profile/files, compatible gameplay networking and separately authenticated services. | Inventory callers and callback ordering; prove readiness, invite/join, profile display and save ownership. This remains the preferred game port architecture. |
| Add an authenticated Steam-network client for selected services | Potentially support current account information and direct Steam Cloud without a desktop client on Quest. | Prove legitimate account login and a read-only original-game cloud listing first; then Android/AOT compatibility and conflict handling. This is an optional additional feature, not a prerequisite for offline campaign play. |
| Use native EOS Android | Reuse required original EOS services without replacing their backend. | Check the installed version/API and Android bindings, then the original product/deployment's accepted authentication and multiplayer flow. Do not infer service access from a successful `EOS_Initialize`. |
| Use Galaxy on Android | Potentially retain original GOG account services if an appropriate supported package exists. | Obtain evidence of the correct SDK/platform support and authentication. The consulted public API lists Windows/macOS libraries, not an established Android ARM64 route. |

SteamKit is a concrete open-source candidate for selected Steam-network
operations. It provides its own network client, rather than a drop-in native
`steam_api`. Its source includes cloud request handlers; that does not prove a
complete save-sync workflow. The inspected project targets .NET 8 and .NET 10,
so direct integration into this Unity 2021.3 player is unresolved. A future
PC-side read-only experiment can establish protocol behavior before considering
a narrow port or separate compatible Android component. Login, Steam Guard,
session renewal, application ownership, simultaneous sessions and backend
changes remain real work. [SteamKit project][steamkit]
[SteamKit runtime targets][steamkit-targets]
[SteamKit cloud handler][steamkit-cloud]

Epic officially lists Android SDK support, but its social overlay is unavailable
on Android. Generic EOS Sessions with an application UI are distinct from the
full branded crossplay overlay. SDK support also does not grant access to
Gloomhaven's product or supply a valid launcher exchange code on Quest.
Preserve the original backend/configuration and prove its accepted login;
a new unrelated EOS product would not restore original-game interoperability.
[EOS supported platforms][eos-platforms]
[EOS crossplay and overlay limits][eos-crossplay]

Galaxy supports different operational states and authentication providers;
do not claim that every SDK feature always needs the desktop client. Its
automatic cloud synchronization depends on the client/authenticated state.
The absence of a proven Android package and service path makes a wholesale
Galaxy substitution unsuitable as the initial assumption.
[Galaxy libraries and game credentials][gog-sdk]
[Galaxy service availability][gog-states]

The deliverable is a call-site matrix: original operation, actual caller,
required behavior, local/online implementation, prerequisites and measured
result. Retain exact relevant callbacks and real failure handling. A facade
can preserve the subset the game uses without claiming all Steamworks, EOS or
Galaxy functionality. Imported identity and optional live authentication remain
separate; no account secret goes into the personalized APK.

## Profile identity and avatars across platforms

### Preferred path

Separate three concepts: original source-account identity, local presentation
snapshot and Quest device/platform capabilities. Do not select Steam native
functions merely because a serialized account-provider field says Steam.

For the first Steam-derived build, test retaining the genuine imported Steam
ID and existing source-provider fields. The inspected unmodified Steam avatar
loader checks `PlatformName == "Steam"` and parses the original account's full
ID before requesting its image from Steam. That provides a concrete path for
an unchanged Steam peer to display the correct original account avatar.

The candidate uses a real imported account, never a fabricated Steam ID.
Confirm the field contract for permissions, duplicate identities, platform
icons, save ownership, recent-player records and console account handling.
It is an offline imported identity, not cryptographic proof of who is running
the APK. If original admission requires live authentication, use its legitimate
flow or report the blocker. If the provider fields cannot be retained
truthfully/compatibly, test the original standalone-provider route with
crossplay enabled and record its actual presentation limits.

For compatible modded peers, transfer each owner's actual bounded avatar bytes
through an additive presentation record. Proposed starting bounds are 128 by
128 pixels, 32 KiB compressed input and one update per changed content hash;
these are initial experiment budgets, not approved reductions to original
image quality. Increase them if the native presentation requires it. Reuse the
existing bounded transport envelope after proving it cannot starve card/window
streams. Validate sender
association, dimensions and decoded size before making a Unity texture, cache
by provider/account/hash and release it when no longer referenced.

An unmodified remote peer cannot send a new mod avatar record. On Quest,
investigate official provider lookup separately for such peers. Steam's
`GetPlayerSummaries` Web API documents a key requirement; do not embed a shared
maintainer key or promise an anonymous lookup. A permitted user-supplied
resolver or PC-imported known-player cache is an alternative to evaluate,
not a mandatory new service. [Steam profile Web API][steam-web]
Epic/GOG/console picture access requires provider-specific evidence.

### Initial experiment and acceptance

Test four directions independently: local imported picture, Quest picture on
unmodified Steam, Quest picture on a modded PC and remote picture on Quest.
Include a genuine Steam profile rename/image refresh, no avatar, unknown
provider and duplicate account instance. Measure bounded texture memory and
wire use alongside existing animation traffic.

If remote images cannot be obtained for unchanged peers, preserve usable
gameplay and document the precise presentation gap. Ordinary native placeholders
do not establish full parity for a profile that has an available picture.
No image gap is approved merely by removing Horizon services.

## Native passthrough without Horizon services

### Preferred path

Use the existing Unity OpenXR session and add a focused Android passthrough
feature. Prefer a compatible existing composition integration; otherwise
implement a small native composition bridge. Unity exposes OpenXR feature
lifecycle and interception hooks; they are an integration seam, not a guarantee
that a managed handle alone submits a layer. [Unity OpenXR feature API][openxr-feature]

The bridge must negotiate `XR_FB_passthrough` before instance creation,
resolve its functions and attach passthrough resources to the Unity-owned
session. Declare the documented Android passthrough feature. Add a reconstruction
underlay before the application's projection layer and preserve its source
alpha through Unity's render targets/postprocessing. Quest's frame environment
blend mode remains the documented opaque mode; transparent projection pixels
and ordered composition expose the underlay. Do not start a second XR session
or submit a second independent `xrEndFrame`. [Meta native passthrough][passthrough]

Tie the existing MR toggle to layer start/pause and application camera/backdrop
ownership. When the layer becomes ready, clear eligible backgrounds with
transparent alpha while retaining figures, cards, UI and actual board content.
Use the existing approved UI-only backings. Keep the old chroma-key setting
persisted and inert for this backend. Restore ordinary VR on disable or a
recoverable error; scope lifecycle to XR session creation/destruction and
headset pause/resume.

This path uses device-native XR composition. It does not require a Meta
Platform account call or a registered Horizon app. Profile and MR probes are
separate so a profile failure cannot be mistaken for missing XR support.

### Initial experiment and acceptance

Use a minimal locally signed APK with one opaque object, one transparent card
and one UI surface. Enable/disable passthrough repeatedly, sleep/resume and
reload a scene. Verify both eyes, alpha/depth behavior, absence of a green
flash and resource recovery. Then repeat with the existing mod's capture,
particles, ghosts and MR board rules.

If the chosen OpenXR package cannot integrate composition correctly, test a
compatible focused native bridge or pinned XR package in the isolated Quest
project. Change the editor only after the compatibility evidence requires it.
No registration/helper-app alternative is part of the fallback.

## Voice and remaining native dependencies

Build a dependency matrix with every `DllImport`, library ABI, managed caller,
startup requirement and Android equivalent. Inspect actual references and
installed versions; filenames alone do not prove a library is unused.

For Photon Voice, retain the original room/protocol settings and compatible
codec behavior. Obtain matching permitted ARM64 Opus/audio-processing plugins
and use an Android microphone path with the original speaker association,
spatial placement, mute and permission behavior. Photon documents Android and
IL2CPP support, but that does not establish that today's newest SDK can replace
the installed one unchanged. [Photon Voice platform support][voice]

Test a real Quest-to-PC conversation during gameplay: permission first-run,
denial/retry, headset pause, reconnect, mute, speaker mapping and simultaneous
board activity. Silence or shifted playback is a failed voice test even when
game actions work. Removing WebRTC processing also needs demonstrated equivalent
behavior, not only the absence of a native load error.

For input, use the selected Quest OpenXR action bindings while preserving the
existing hands/controllers, haptics and tutorial transitions. Route optional
desktop XInput/capture calls only after classifying their purpose. Rebuild or
replace actual required native functionality; eliminate only proven unused
target branches. Retain movie cutscenes/audio and any original media-dependent
continuations with a working Android decoder.

If a matching binary is unavailable, consider a permitted source build or a
compatible narrow replacement. Retest protocol and visible/audio behavior if
SDK upgrades are necessary. Do not copy a Windows DLL into `arm64-v8a` or remove
an unknown dependency to make the player launch.

## Campaign, DLC and save boundaries

Generate a dependency closure rooted in the 95 authored base campaign
definitions, map, town, menu, tutorial and common character/item assets. Add
the 25 Jaws of the Lion and 17 Solo definitions only from acquired/available
content when those converter inputs are supported. Excluding Guildmaster does
not permit deleting shared resources packaged with it.

Create a manifest entry for every scenario plus its geometry/style/state
coverage and special mechanics. Select representative fixtures for summons,
spawn/remove props, unusual doors, pressure plates, scripted geometry changes,
campaign progression and DLC mechanics. Run static coverage before expensive
exports, then real device tests for each distinct mechanic family. Enumerating
all scenario names is not proof that every state can load.

Use one Quest-only availability provider for both menu styling and actual
entry callbacks. Disabled Guildmaster/Workshop entries retain native order and
hover hit targets. Guard continue/import/invite/join paths before incompatible
save or Workshop loading, preserving unrelated native focus and callbacks.

Preserve original save serialization and owning-account semantics. Keep Quest
saves separate from PC/cloud paths. Offer an explicit copy/import operation
for supported campaign saves, retaining the original files; do not migrate a
Guildmaster save into a campaign by altering its mode ID. Cross-account save
ownership and DLC-mismatch behavior must follow an explicit inspected contract.

Acceptance requires an ordinary campaign continuation, town-to-scenario flow,
changed-state reload, a correctly rejected Guildmaster invite and the requested
grey entries/tooltips in both languages. The optional DLC phase also requires
valid no-DLC startup and original multiplayer admission behavior.

## Save import and optional Steam Cloud synchronization

### What the inspected game proves

`PathsManager.PersistionDataPath` returns `Application.persistentDataPath`.
`PlatformFileSystem` performs local file operations, and `RootSaveData` uses
the release-dependent save root (`GloomSaves` for a release), `Campaign`,
`Guildmaster`, `GloomSaven.dat`, `GlobalData.dat`, checkpoints and backups.
`SaveData` includes queued/asynchronous writes and original binary
deserialization paths. Account fields participate in save ownership decisions.

No `ISteamRemoteStorage` calls were found in the inspected game source. Local
file access is consistent with Steam Auto-Cloud, but does not prove which files
the released Steam application actually synchronizes. Verify the original
App ID's cloud configuration, actual file mapping and a real client sync before
choosing a transport. Do not derive a cloud manifest from folder names alone.

The Android data directory changes, while the original relative save structure,
types, game version and account semantics should remain compatible. APK updates
with the same package/signing identity must preserve it. Uninstalling or clearing
app data needs an external backup; a personalized APK is not a save backup.

### Recommended first path: campaign import/export and optional PC sync

Keep local saves usable without any service login. Extend the future converter
with a separate, explicit campaign transfer operation; ordinary game play does
not depend on an always-running PC. USB/ADB is the initial transport candidate;
a later local-network transfer can reuse the same snapshot contract.

1. Select the source account, supported game/mod version and campaign. Confirm
   required DLCs and that the campaign can be opened by the target. Resolve
   the corresponding global/root metadata with original serialization logic;
   do not blindly copy or replace all global preferences and save lists.
2. Close both games and establish that saving is finished. If a future in-game
   exporter is used, wait for both save queue and saving thread to become idle
   and prevent another write during snapshotting. Record a manifest with relative
   paths, hashes, versions, owning provider/account and DLC requirements.
3. Create a dated local backup, transfer into staging, verify hashes and original
   deserialization, then activate the complete consistent snapshot. Keep the
   previous version recoverable if activation or the next load fails.
4. On a return transfer, compare both sides to the last synchronized snapshot.
   If only one side changed, propose that complete campaign version. If both
   changed, preserve both and let the player choose. Never merge arbitrary
   binary campaign state or select a winner solely by wall-clock timestamp.
5. For optional Steam synchronization, restore the accepted campaign to its
   verified PC save location and let the original Steam client perform its
   configured sync. Prove upload and later download through a real game/client
   cycle. Copying a file while Steam is idle is not proof of cloud upload.

Steam documents Auto-Cloud synchronization around game launch/exit, its cloud
configuration and diagnostics. Check account/game cloud settings and actual
client completion; do not modify `remotecache.vdf` or fabricate Steam's sync
metadata. [Steam Cloud behavior and configuration][steam-cloud]

The transfer manifest must describe the closure needed by one campaign. Preserve
unrelated PC campaigns, Guildmaster saves, excluded Workshop content and cloud
files. If shared global metadata mixes campaign and excluded modes, determine
the original serialization/index update needed for a selected campaign before
supporting upload. Mode exclusion does not authorize cloud deletion, conversion
or loss of Guildmaster records. Preserve recovery checkpoints/backups according
to their actual role; avoid importing stale temporary writes as newer saves.

An initial acceptance fixture is a copied base campaign: load on Quest, make
progress, reload locally, export to PC, continue with the original game, then
import again. Repeat with the optional DLC combination. Test divergent PC/Quest
progress, interrupted transfer, mismatched account, different supported game
versions and update-in-place. Cloud acceptance additionally needs independent
upload/download evidence, with backed-up test saves and the correct account.

### Direct official Steam Cloud access: possible API, gated authorization

Valve explicitly provides `ICloudService` for non-Steam versions to download
and update the original game's cloud files over HTTP. Access uses user OAuth,
not an embedded name or Steam ID. The documented flow enumerates file metadata
and download URLs; writes use an upload batch, HTTP upload/commit and batch
completion, including completion on failure. This is a genuine direct-cloud
candidate without a Steam client on Quest. [Steam Cloud HTTP API][steam-cloud-http]

Its prerequisite is an OAuth client assigned by Valve with `read_cloud` and/or
`write_cloud` scoped to the original game's App ID. A normal user API key, Steam
OpenID identity, another application's client ID or a copied PC profile does
not supply that permission. No such approved original-game OAuth client is
established for this project. Access would need Valve's authorization and may
require cooperation from the game's publisher; that latter dependency is an
inference, not a claimed universal rule. No registration/contact is undertaken
now. [Steam OAuth and App ID permissions][steam-oauth]

If access becomes available, begin read-only, verify account association and
map actual remote filenames/platform flags into the campaign transfer manifest.
Only then add upload using the same backup/conflict rules and complete original
metadata. Keep tokens in private runtime storage after an optional user login,
never in the generated APK, logs or source. Preserve offline play on denied,
expired or unavailable authorization. Publisher-key-only operations, if any
are introduced, cannot run with a publisher secret embedded in a public client;
the documented cloud endpoints do not all require such a key.

### Alternative direct access: selected Steam client protocol

A legitimately authenticated Steam-network client such as the SteamKit
candidate above is another research path. It does not use the same partner
OAuth-client prerequisite. It still needs the player's genuine account session
and service authorization; copying identity fields cannot replace either.
Cloud support, Android runtime suitability and backend behavior must be proved,
not inferred from the library name or an old cloud sample.

Start with a separate PC-side tool, the player's deliberate login and read-only
listing/download of backed-up test campaign files. Compare the downloaded hashes
and metadata with the ordinary client's result. If that succeeds, investigate
a bounded Android implementation of those operations and secure session
lifecycle, then conflict-safe writes and PC round trips. Do not port the whole
Steam client or all Steamworks interfaces merely to synchronize campaign files.
This path adds authentication/dependency maintenance and may remain optional
or external to the Unity player if direct integration proves unsuitable.

Epic/GOG cloud access requires its own provider assessment. Do not assume a
generic EOS storage API addresses the store's existing cloud-save files.
Their PC installations can initially use the same local transfer workflow with
the verified store client's normal sync. A Quest save does not automatically
belong to Steam Cloud merely because a multiplayer peer owns the Steam game.

These are optional synchronization designs, not claims of working cloud access
or new release blockers. They neither require Horizon services nor introduce
an ownership server, DRM or a PC dependency during ordinary offline play.

## Quest performance and the local build workflow

### Preferred path

Measure the first combined scenario before a full campaign export. A Windows
Frame improvement is useful evidence but does not predict Quest timings.
Target the actual CPU/GPU/memory bottleneck rather than assuming baking makes
the renderer cheap.

| Measured problem | First corrective path | Required preservation |
| --- | --- | --- |
| Many immutable draw submissions | Export shared materials and room-scoped static batches or supported instancing. | Room reveal, picking, hierarchy-dependent continuations and correct source identity. |
| Expensive materials/overdraw | Validate equivalent Android shader variants and measured render ordering/coverage. | Cutout/transparency, original effects, card text and UI readability. |
| Repeated CPU presentation work | Apply demonstrated Frame improvements, remove redundant reads and respect event-driven readiness. | Animation clocks, owner-authored presentation and lifecycle callbacks. |
| Room load/reveal hitches | Preload the next required dependency group and spread preparation across frames. | Native continuation begins only with genuinely ready content; no hidden geometry shown early. |
| Texture/asset peak memory | Deduplicate immutable assets, use measured target texture formats and release unused groups with reference ownership. | Artwork quality, glyphs, alpha and late joins; no unload while another room still references an asset. |
| Stereo/MR overhead | Prove multiview/single-pass compatibility and fixed foveation at a measured configuration. | Both-eye output, readable UI and native passthrough alpha; no unapproved visual reduction. |

Use the master plan's proposed 72 Hz/13.89 ms budget as a discussion target,
then obtain the maintainer's hardware acceptance. Record CPU/GPU frame
distributions, missed frames, steady-state thermal behavior, memory and load
spikes on large maps/town with multiplayer and voice. Quality tradeoffs need
explicit approval; dropping remote animations, secrecy rules or MR backings
is not a performance solution within the current contract.

Package the converter as a staged workflow: inspect/profile/check, recover,
export, convert materials, integrate code, build/sign and install. Lock every
tool version and separate pure conversion caches from the personalized profile
and signed APK. Failed/interrupted stages cannot produce a success manifest.

Use a persistent local keystore and monotonically increasing Android version
code. Back up the player's key locally and test update-in-place with saves.
Do not ask the maintainer to sign player-built game content or distribute a
shared signing secret. A signature protects Android update identity; it is not
the requested ownership proof or an anti-sharing device lock.

Estimate disk space, memory and export/build duration from a measured small
conversion before publishing requirements. Confirm the selected Unity editor's
installation/licensing and redistribution path for ordinary builders; do not
assume everyone has a free activated toolchain. A clean-PC rehearsal must
complete without the developer checkout, private caches, game files on a server
or a PC required during play. No modified game assemblies, recovered assets,
personal profiles or finished game APKs go into project releases.

## Evidence required before resuming full development

Each feasibility experiment should eventually produce a dated report with
input hashes, exact tools/SDKs, source rationale, generated local output
inventory, automated results and separate headset/session evidence. Reports
must identify the first unmet acceptance condition and the next bounded step.
They must not mark a source hypothesis as a passed device result.

Before implementation, re-read current project instructions/build notes.
During implementation, use the project's focused checks and complete final
`dev` gate, including unchanged golden wire vectors. Desktop regression evidence
and Android/device evidence are separate. Only `dev` is committed/pushed;
unrelated parallel work remains intact. This documentation revision runs
document checks and changes no runtime build number.

## Source and documentation references

The source contracts above were inspected in:

- [PlatformUserData](../decompiled/GH.Runtime/PlatformUserData.cs),
  [PlatformLayer](../decompiled/GH.Runtime/PlatformLayer.cs),
  [PlatformNetworking](../decompiled/GH.Runtime/PlatformNetworking.cs) and
  [PlatformDLC](../decompiled/GH.Runtime/PlatformDLC.cs).
- [PathsManager](../decompiled/Utilities/PathsManager.cs),
  [PlatformFileSystem](../decompiled/GH.Runtime/PlatformFileSystem.cs),
  [RootSaveData](../decompiled/GH.Runtime/RootSaveData.cs),
  [SaveData](../decompiled/GH.Runtime/SaveData.cs) and
  [SaveQueue](../decompiled/GH.Runtime/SaveQueue.cs).
- [ProceduralBase](../decompiled/GH.Runtime/ProceduralBase.cs),
  [ProceduralMapTile](../decompiled/GH.Runtime/ProceduralMapTile.cs),
  [ProceduralProp](../decompiled/GH.Runtime/ProceduralProp.cs),
  [ProceduralStyle](../decompiled/GH.Runtime/ProceduralStyle.cs),
  [CCustomLevelData](../decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary.CustomLevels/CCustomLevelData.cs)
  and [CMapScenarioState](../decompiled/MapRuleLibrary/MapRuleLibrary.MapState/CMapScenarioState.cs).
- [NetworkManager](../decompiled/GH.Runtime/FFSNet/NetworkManager.cs),
  [GHNetworkCallbacks](../decompiled/GH.Runtime/GHNetworkCallbacks.cs),
  [UserToken](../decompiled/GH.Runtime/FFSNet/UserToken.cs),
  [NetworkPlayer](../decompiled/GH.Runtime/FFSNet/NetworkPlayer.cs) and
  [FfsNetTransport](../src/GloomhavenVR/Net/FfsNetTransport.cs).
- [Plugin bootstrap](../src/GloomhavenVR/Plugin.cs),
  [patch inventory](../docs/PATCH-INVENTORY.md),
  [OpenXRBootstrap](../src/GloomhavenVR/Core/Startup/OpenXRBootstrap.cs),
  [MixedReality](../src/GloomhavenVR/Core/MixedReality/MixedReality.cs) and
  [asset package manifest](../unity/GloomhavenVR.Assets/Packages/manifest.json).

External primary documentation was consulted on 2026-10-02. Verify exact
package versions and applicable APIs again when implementation starts. The
Unity ARCore page below is cited only for Unity's ARM64 backend restriction;
this plan does not install ARCore or use it for Quest passthrough.

[steam-profile]: https://partner.steamgames.com/doc/api/ISteamFriends
[gog-profile]: https://docs.gog.com/galaxyapi/classgalaxy_1_1api_1_1IFriends.html
[steam-apps]: https://partner.steamgames.com/doc/api/ISteamApps
[steam-web]: https://partner.steamgames.com/doc/webapi/ISteamUser#GetPlayerSummaries
[assetripper]: https://github.com/AssetRipper/AssetRipper
[unity-arm64]: https://docs.unity3d.com/Packages/com.unity.xr.arcore@5.1/manual/project-configuration-arcore.html#target-architecture
[unity-aot]: https://docs.unity3d.com/2021.3/Documentation/Manual/ScriptingRestrictions.html
[unity-stripping]: https://docs.unity3d.com/2021.3/Documentation/Manual/ManagedCodeStripping.html
[cecil]: https://github.com/jbevain/cecil
[apparance]: https://apparance.uk/products/products.htm
[openxr-feature]: https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.13/api/UnityEngine.XR.OpenXR.Features.OpenXRFeature.html
[passthrough]: https://developers.meta.com/vr/documentation/native/android/mobile-passthrough/
[voice]: https://doc.photonengine.com/voice/v2/getting-started/voice-intro
[steam-sdk]: https://partner.steamgames.com/doc/sdk/api
[steam-frame]: https://partner.steamgames.com/doc/steamhardware/steamframe
[steam-apk]: https://partner.steamgames.com/doc/steamhardware/steamframe/apk_upload
[steamkit]: https://github.com/SteamRE/SteamKit
[steamkit-targets]: https://github.com/SteamRE/SteamKit/blob/master/SteamKit2/SteamKit2/SteamKit2.csproj
[steamkit-cloud]: https://github.com/SteamRE/SteamKit/blob/master/SteamKit2/SteamKit2/Steam/Handlers/SteamCloud/SteamCloud.cs
[eos-platforms]: https://dev.epicgames.com/docs/epic-online-services/platform-support
[eos-crossplay]: https://dev.epicgames.com/docs/epic-online-services/accounts-and-social/crossplay/crossplay-technical-overview
[gog-sdk]: https://docs.gog.com/galaxyapi/
[gog-states]: https://docs.gog.com/sdk-galaxy-feats-and-states/
[steam-cloud]: https://partner.steamgames.com/doc/features/cloud
[steam-cloud-http]: https://partner.steamgames.com/doc/webapi/ICloudService
[steam-oauth]: https://partner.steamgames.com/doc/webapi_overview/OAuth
