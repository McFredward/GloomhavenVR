# Quest 3 critical solution paths

This document turns the risks in the [Quest 3 port plan](QUEST3-PORT-PLAN.md)
into concrete work. Each path has an initial experiment, a deliverable,
an acceptance condition and a response to failure. The maintainer subsequently
authorized implementation on 2026-10-03 in a branch from current `dev`.
[Implementation evidence](QUEST3-IMPLEMENTATION.md) distinguishes actual results
from the remaining full-game and hardware gates. The older Frame prerequisite
is superseded by that explicit instruction, not by an inferred hardware pass.

**Planning revision: 2026-10-02.** The latest maintainer decision is a locally
built, sideloaded APK with Steam logo, name and ID embedded, without cloud or
other store services. Required EOS multiplayer operations are explicitly
allowed by the maintainer's 2026-10-02 clarification; this does not restore
cloud, profile or storefront features. The earlier personal-avatar/live-service
options are superseded. There will never be a registered Meta app for this
project. No Horizon services, registered profile helper, device binding or
new ownership server are part of the design.
Personalization and simple installation/provider checks are intentionally a
small sharing hurdle; modifying the open-source builder can bypass them.

Source inspection started from ModBuild 605 and was refreshed while other
agents advanced `dev` through `370b9ef5`. The additional maintenance/blocker
audit uses ModBuild 606 at `f400f30c` and is recorded in
[Quest preflight and builder maintenance](QUEST3-PREFLIGHT-AND-BUILDER.md).
This is a dated assessment, not an Android compatibility result.
All repository game references remain read-only.
Tool and component names below describe candidates and contracts, not new
implemented classes or a finalized dependency lock.

## Recommended approach and order

Build a genuine Unity Android ARM64 player from locally reconstructed assets,
preserved original managed logic and mod behavior integrated during the build.
Use the original PC runtime as the first procedural export host. Embed the PC
Steam logo/name/ID and route device capabilities through a separate Quest target
adapter.
Keep the original Photon gameplay protocol. Use native OpenXR passthrough in
the existing rig.

Resolve the expensive dependencies with small experiments before scaling up:

| Order | Experiment | Stop expanding until |
| --- | --- | --- |
| 1 | Import one Steam account profile and build a minimal ARM64/OpenXR probe. | The profile works offline, passthrough works on Quest, and the selected editor/toolchain is recorded. |
| 2 | Recover one original scene, its UI and one animated character. | Original scripts and serialized asset references load correctly; key shaders work in both eyes. |
| 3 | Prove generated mod hooks and helper bindings before IL2CPP compilation. | Current prefix/postfix/finalizer/direct-registration and field-helper behavior, startup and network receive semantics agree with desktop; ordinary N -> N+1 mod changes need no builder edits. |
| 4 | Connect the minimal real Android client to an unmodified PC host. | Actual authentication, session lookup, original admission/save exchange and lobby reconnect work; complete rounds and scenario reconnect are proved after the baked scene is usable. |
| 5 | Export one complete scenario from the owned PC runtime. | A baked room, door, prop and reveal lifecycle work without the Windows Apparance library. |
| 6 | Measure the combined scenario, existing VR features, MR and voice. | Memory and frame time support continued investment; discrepancies have a concrete cause. |
| 7 | Expand campaign coverage, special mechanics and the multiplayer matrix. | Export coverage is complete and missing combinations have an explicit disposition. |

Orders 1 and 2 can discover independent blockers. The network test should run
as soon as the original game bootstrap is usable; it need not wait for every
VR subsystem or all campaign exports. The master plan's phases still govern
release acceptance.

## Importing the Steam identity and adding the sharing hurdle

### Preferred path

Capture the actual Steam account context during conversion on the PC, then
write an immutable identity resource into the generated APK. Embed the source
account's display name, full Steam ID, consistent original account fields,
source provenance/schema/build and the Steam logo resource. A generated
JSON/resource is sufficient; user-specific C# string literals are unnecessary.
Use the Steam logo rather than fetching or embedding a personal avatar, as
requested in the latest maintainer decision. Never substitute the mod's own
Steam Frame library artwork for the actual Steam platform logo.

`PlatformUserData.UserName` and `PlatformPlayerID` already read the original
PC client. Capture values in the player's deliberate active account context;
generic game asset files do not reliably contain the owner's name. Keep the
full Steam ID distinct from the original 32-bit account number. No PC password,
launcher cookie, user token/ticket or publisher server-side secret goes into
the embedded identity or APK. The original client's ordinary service
configuration is a separate local converter input, not proof of authentication.
An ID/name is association data, not an authentication credential.
[Steam persona API][steam-profile]

Use the original Steam build as the initial converter input. Additional
Epic/GOG profile-import recipes are removed from the active scope; original
peer interoperability remains separate. A supported, verified local account
record may be an alternative to PC client capture, but do not guess the active
account from arbitrary cached records or save owners.

Perform proportionate local installation and available original PC ownership/
DLC checks. Generic hashes identify an input build rather than proving purchase.
Unavailable account information should fail this capture stage clearly, rather
than silently selecting the OS login or inventing a verified identity.
[Steam source application checks][steam-apps]

Quest reads the embedded logo/name/IDs offline. No account refresh, avatar
lookup, friends, achievements, storefront invites, cloud sync, SteamKit or
native Steam/Galaxy account SDK is added on the headset. Updating the name
requires a new local APK build; retain the account association, save ownership,
package name and signing key. Original multiplayer session codes and local
saves remain independent of store services.

### Initial experiment and acceptance

After implementation is authorized, capture one PC identity, show its Steam
logo/name in the Quest menu and relaunch offline. Test deliberate selection of
a second PC account and a name change while retaining a stable ID. Confirm no
PC account secrets are embedded. Offline startup, save/load and profile display
must work without provider requests; any necessary EOS authentication belongs
to the original multiplayer path. Original PC files remain untouched.

A copied APK keeps its builder's identity; this is the intended small sharing
hurdle. It does not prove purchase or prevent modified builders. Duplicate
imported identities follow original session admission, not a new DRM rule.

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

Generate a build-time patch manifest from the effective mod patch surface,
including direct registrations, for the player's staged assemblies.
Mono.Cecil is a candidate for inspecting and
rewriting CIL before Unity converts it to native code. Its documented editing
capability does not automatically reproduce Harmony semantics. [Cecil][cecil]

Generate a record for each patch with its original target/signature/hash,
priority and ordering, target platform, required injected fields and expected
behavior:

- Prefixes must preserve argument changes, `__result`, `__state`, and the
  exact condition that skips the original method.
- Postfixes and finalizers must preserve execution/exception behavior and
  state association. Preserve multiple-patch ordering explicitly.
- Transpilers must match the expected original instructions and fail on an
  unsupported input rather than applying a guessed edit.
- Dynamic `Patch`/`Unpatch` and optional-feature guards need generated target
  hooks plus equivalent AOT activation/order semantics. New ordinary hooks must
  not require a handwritten Quest counterpart. Future reverse/transpiler kinds
  need supported generic integration or a precise compatibility failure.

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

Integrate real current examples: a prefix suppressing a native path, a postfix
with state, a finalizer, direct registration and a reference-return field helper.
A current source search found no implemented transpiler; test that contract
when a real supported input introduces one. Include the mod's action receive prefix
early. Compare their observable behavior against the same desktop mod and
exercise toggles, cancellation and exceptions where relevant.

Then start the real game, deserialize a save and exchange original tokens in
an IL2CPP development build. Acceptance requires preserved patch behavior,
working reflected data and no unsupported desktop/native initialization.
Only then prove complete generated coverage, including direct registrations
and code-generation helpers outside patch setup, as required by the
[builder maintenance contract](QUEST3-PREFLIGHT-AND-BUILDER.md#builder-architecture-for-ordinary-mod-development).

If a new architectural patch pattern cannot be represented faithfully, report
the unsupported contract and investigate a shared integration seam. Validate
any proposed callback against the original behavior. A growing collection of
handwritten Quest equivalents for normal patches fails the maintenance gate.
Do not install another runtime detour package and assume parity, or recompile
the entire decompiled game to solve one integration point.

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
Do not force an authorization flag to true. The maintainer explicitly permits
EOS when required for the original multiplayer. Establish the actual original
authentication requirement and prove an accepted Android route, with only
necessary authentication/session operations enabled. An imported Steam ID
supplies neither a ticket nor an EOS identity. A requirement for an unavailable
credential or an excluded store service remains a possible crossplay blocker.
For genuinely unused startup endpoints, provide a target-specific completed
capability result at the provider boundary, with all consumers checked.
Existing features that are actually required cannot be silently discarded.

### Necessary EOS authentication, if the audit requires it

Android is an official EOS SDK target. The upstream Unity plugin documents
Account Portal -> Persistent Auth and Auth -> Connect flows, including Android.
That establishes a candidate implementation route, not acceptance by
Gloomhaven's configured product. [EOS SDK targets][eos-sdk]
[EOS Unity authentication][eos-auth]

The source already falls back from persistent authentication to Account Portal,
then calls `StartConnectLoginWithEpicAccount`. This is the first route to
investigate if EOS is genuinely required; a PC launcher's exchange code and
Steam session ticket cannot be baked into a usable permanent Quest login.

1. Pin compatible Android native libraries/C# bindings and preserve the
   original client's product, sandbox, deployment and callback/type contracts
   from the player's input. Do not create a separate EOS/Photon product as a
   substitute for access to the original sessions. Do not publish the source
   client's configuration or add publisher server-side secrets.
2. Prove an actual Account Portal login on the headset, return to the same
   Unity/OpenXR session and complete the required Connect login. Keep refresh
   state local through the supported SDK. Test cancel, expiry, suspend/resume
   and offline operation. Imported Steam display/save identity and authenticated
   EOS identity are separate fields; neither should impersonate the other.
3. The desktop path requests `BasicProfile | FriendsList | Presence` and creates
   a friends manager. Determine the minimal auth scopes actually required for
   multiplayer. A necessary authentication consent is distinct from fetching
   a profile for display. Do not enable friends, presence, profile queries,
   overlay invitations or cloud features merely because the SDK includes them.
4. Prove original session admission and reconnect. If the original client
   policy/platform configuration rejects the supported Android flow, or needs
   an excluded live Steam credential, record the exact failure as a crossplay
   blocker. A fresh guest/device identity or forced success flag is no evidence
   that the original backend admits the player.

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

## Store services removed from the active target

The maintainer now excludes cloud and other Steam/Epic/GOG account/store
services on Quest. Do not implement a complete native Steam API/client port,
SteamKit login, cloud OAuth, live profile lookup or Galaxy substitution.
Earlier research is retained in Git at `c8a460e8`; it is no longer a roadmap.

Instead provide the original game-facing local identity, file access, acquired
DLC availability and readiness through a narrow target adapter. Inventory every
native/store caller, including shutdown, scene transitions and restored PC
preferences. The original `PlatformLayer.Init` quits when Steam is absent;
removing the native library alone would not produce a usable player.

Classify EOS/Hydra separately from Photon. The maintainer's explicit exception
allows EOS only if required for original multiplayer, including its necessary
authentication/session operations. Prove session-code play and actual original
authorization requirements; use the exception only where necessary. An
unavailable Android authentication route remains a possible blocker; setting
a provider flag does not create access. Other store services stay excluded.
See the [preflight gates](QUEST3-PREFLIGHT-AND-BUILDER.md#feasibility-gates).

## Embedded identity and peer presentation

Use the real imported Steam ID/provider fields where their inspected contracts
allow it, separating source account identity from Quest device capabilities.
No fabricated ID, authentication ticket or successful live Steam-login assertion
is part of this design. Preserve session IDs, save ownership and voice mapping.

The Quest local profile uses the static Steam logo by explicit maintainer
instruction. The unchanged PC token has no image bytes. An unmodified Steam
peer may display that genuine account's personal avatar through its own Steam
client; the Quest app must not contact Valve to reproduce that lookup locally.
Test platform icons and field semantics independently from account readiness.

Retain peer names/IDs and already available presentation through the original
or existing mod channel. No new avatar lookup service or mandatory avatar TLV
is justified by the simplified identity requirement. Missing unknown flat-peer
pictures cannot be reconstructed from an ID alone. Document the actual fallback
and any remaining peer-image requirement; the local-logo decision is not a
blanket exception to gameplay/card/UI multiplayer visual parity.

Acceptance includes offline local display, genuine IDs in unmodified/matching
modded sessions, duplicate-identity behavior, relaunch and stable save/voice
association, with no store service request from Quest.

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

## Local campaign saves and manual transfer

Cloud synchronization is out of scope by the latest maintainer decision.
There is no direct Steam HTTP/SteamKit client, PC cloud bridge or other-provider
cloud implementation. Original PC/cloud files and settings stay untouched.

`PathsManager.PersistionDataPath` returns `Application.persistentDataPath`.
`PlatformFileSystem` uses local files. `RootSaveData` selects the release save
root (`GloomSaves` for a release), campaign folders, root/global metadata,
checkpoints and backups. The Android root changes; original type identities,
serialization, owning-account fields and relative structure should remain
compatible. A renamed format is not a substitute for PC compatibility.

Offer a future explicit local campaign import/export operation, initially over
USB/ADB, without a PC required during ordinary play:

1. Select the account/campaign and verify source version, original serialization
   and DLC requirements. Include corresponding root/global data using the
   original indexing contract instead of replacing all global preferences.
2. Close the games and finish queued/asynchronous writes. A future live exporter
   must hold a genuine idle snapshot across the save queue and saving thread.
3. Back up, stage, hash-check and deserialize the complete consistent snapshot
   before activating it; retain a recovery copy if the next load fails.
4. Compare a return transfer against the last transferred snapshot. Preserve both
   versions on divergent progress and let the player choose; do not merge binary
   campaign state or use timestamps alone.
5. Preserve unrelated campaigns, Guildmaster files and shared metadata. Mode
   exclusion does not authorize converting or deleting unsupported saves.

Acceptance is a backed-up PC campaign -> Quest continuation/reload -> PC
continuation round trip, followed by interruption, account/DLC mismatch and
APK update tests. Stable package/signing identity preserves app data on updates;
uninstall/clear-data requires an external save backup. No cloud service test
or account token is needed.

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
[steam-apps]: https://partner.steamgames.com/doc/api/ISteamApps
[eos-sdk]: https://onlineservices.epicgames.com/sdk?lang=en-US
[eos-auth]: https://github.com/EOS-Contrib/eos_plugin_for_unity/blob/stable/com.playeveryware.eos/Documentation~/player_authentication.md
[assetripper]: https://github.com/AssetRipper/AssetRipper
[unity-arm64]: https://docs.unity3d.com/Packages/com.unity.xr.arcore@5.1/manual/project-configuration-arcore.html#target-architecture
[unity-aot]: https://docs.unity3d.com/2021.3/Documentation/Manual/ScriptingRestrictions.html
[unity-stripping]: https://docs.unity3d.com/2021.3/Documentation/Manual/ManagedCodeStripping.html
[cecil]: https://github.com/jbevain/cecil
[apparance]: https://apparance.uk/products/products.htm
[openxr-feature]: https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.13/api/UnityEngine.XR.OpenXR.Features.OpenXRFeature.html
[passthrough]: https://developers.meta.com/vr/documentation/native/android/mobile-passthrough/
[voice]: https://doc.photonengine.com/voice/v2/getting-started/voice-intro
