# Quest B625 — PC-installed content and original Campaign/Guildmaster runtime

Status: signed full-game build and independent delivery readback passed, 2026-10-06.
Hardware behavior is unverified until a capture identifies this exact package.

## Accepted hardware delivery

The complete Windows hardware archive is `GloomhavenVR-Quest-B625-Windows.zip`,
13,785,192,256 bytes, SHA256
`b4318631c22bdac4cfafc48dd9a20ddead061538a06e55801112de8645668610`.
Its signed APK is 2,871,119,500 bytes, SHA256
`3d32aeb6232fabcb0a0c278f38410426e8e41ee0b8293c6b5f49a98b78e5839a`.
The adjacent original-content archive is 10,912,755,026 bytes, SHA256
`666e62ce39d80e7aa549bf7b0192f4895a3ebd08a27d40567e3dad7fdc077f3a`.
Runtime source is frozen at `bdb7c4aece3532c1fd5675aecf7cfe9900ff9641`;
subsequent Windows builder commits do not change these APK bytes.

Actual `BuildPipeline.BuildPlayer` reports success with zero errors. Independent
readback verifies all archive member CRCs, nested APK/content hashes, all 51
installer source files, all 14 scenes, ARM64/Vulkan, native Release settings,
six quality-level MSAA settings, and final game/mod completion inventories.
The native Player glass shader passes executable Vulkan-program readback.
All 16,715 original catalog locations and dependency/lookup associations survive;
changed bundle metadata has separate executable-byte evidence. Full delivery
retains 688 original shaders / 51,564 aliases and 13 compute shaders / 36 Vulkan
kernels, with zero exhaustive compiler queries. Matching native debug symbols
are retained under Build ID `f1a39afd498544e1` for future crash diagnosis.

The identity in this agent-built hardware package remains an explicit dummy.
This is the final agent-host APK build by maintainer instruction. Future APKs
are built from owned PC files by the separately delivered Windows source Wizard.

## What the B624 capture actually establishes

`quest-capture-20261006T083845Z-4e8b81af.zip` identifies B624 consistently through
its fresh banner and installed package. The supplied report describes an inverted
first intro and a persistently black menu. No matching new screenshot accompanied
this archive, so the precise rendered pixels have not been inferred from older images.

The legacy installer had supplied only 470 files. The headset installed 5,897 more
files during this first full-content run, from 08:28:51 to 08:34:36 UTC. This is a
real device-side archive verification/extraction cost, not proven repeated failure
of a completed warm cache. B625 moves that work into the PC installation.

The procedural worker terminates at its missing glibc guest `__libc_start_main`
entry before entering Wine/Engine.Start. The capture does not prove working
engine geometry generation or mobile performance. An older B623 Vulkan crash
embedded in historical logs is not a new B624 crash. Original unified-data and
native bundle startup also take substantial time after extraction; eliminating
extraction does not yet establish the requested few-second complete startup.

## Changes and their limits

The signed APK contains final game and mod installation inventories. The Windows
installer expands both banks on the PC, transfers changed files in resumable
batches and publishes each native completion receipt only after a complete
transfer. It neither deletes saves nor treats an interrupted transfer as complete.
Full-game startup reads the receipt instead of scanning/hashing all 6,367 game
files or adopting/extracting a bank on the headset. An incomplete installation
stops with localized instructions to rerun the PC installer. Explicit
`--repair-content` verifies and repairs file bytes when needed.

The Android Box64 executable now retains the pinned upstream glibc guest-start
implementation alongside its Bionic entry. Actual ELF exports pass the focused
entry audit and the original DLL is unchanged. This removes a source-proven
missing-entry defect; it does not prove successful Quest engine initialization.

Guildmaster is included again by the maintainer's explicit correction. Original
mode admission, data loading and save validation/loading are retained. Workshop
and provider profile/cloud services remain excluded; required EOS session
transport remains allowed. Campaign, purchased DLC and Guildmaster all need
actual headset scenario/save/multiplayer tests.

Vulkan camera-video UV orientation follows the actual GPU projection sign.
Manual mip generation no longer competes with automatic ownership. A dedicated
Quest glass shader consumes the already-composed transparent menu RGB once.
Targeted GPU probes demonstrate the old second alpha multiplication can erase
valid RGB, but do not establish that this alone caused the B624 black menu.
Two bounded menu handover snapshots and small asynchronous GPU samples now
record producer texture, camera and presentation state without a per-frame
permanent diagnostic stream.

Full-game IL2CPP compilation uses Release native optimization while retaining
Development player diagnostics. Existing Steam Frame defaults and the initial
Vulkan eye allocation remain in force. Original imports and warm shader cache
are reused; no exhaustive shader compiler matrix is rerun for these changes.

The first Release native compile exceeded this host's available memory when
several large translation units ran together. The kernel killed one compiler;
977 of 978 native object outputs remained usable. Replaying that one exact
retained compile action succeeded in 145 seconds without changing its source,
flags or compiler. The continuation uses a recorded, Editor-only recovery entry
point and the actual `BuildPipeline.BuildPlayer` result, with the already passed
content gate and native archive retained. Its extra Editor sources are included
in build provenance; this is not an unmodified build-recipe claim. A private Bee
launcher bounds native compiler concurrency without editing the installed Unity
tools. The observed single large frontend used about 26 GiB across resident and
swapped memory. The final Windows resource policy therefore reserves 32 GiB for
the first native compiler and 28 GiB for each additional one, besides Unity/OS.
Known sufficient Windows commit headroom permits a single paging-assisted job;
unknown or insufficient memory stops before destructive Player-build work.
Paging performance on Windows remains unmeasured.

## Windows builder delivery

The public source archive contains the mod, authored Unity inputs, conversion
tools, pinned public converters and EN/DE Wizard, without owned game assets,
game assemblies, credentials, licenses or savegames. `Quest-Builder.cmd` creates
its pinned local Python/venv and opens the Wizard. Required tools are provisioned
automatically where supported; Unity Hub sign-in and eligible license activation
remain guided actions when required by Unity. A version check is not a license
check. The current original-actor fallback retains complete meshes, but the
builder does not yet produce Android derivative LOD banks.

CPU/memory discovery bounds compiler concurrency; actual Bee invocation tests
verify that the thread option precedes its target. Completed receipts and warm
imports survive interruption and source updates when their inputs still match.
An early full-conversion disk estimate distinguishes fresh work from matching
cache candidates. The diagnostic export includes bounded, redacted stage/error
logs, source/build identities, resource choices and timings; it uploads nothing.

On the final integrated builder tree, 426 builder tests, 50 Wizard tests, six
native voice tests and nine UI/browser checks pass (one optional API browser
check is skipped). Actual native apphost/Bee fixtures exercise concurrency.
The exported Git-free package was separately checked against the owned original
installation, including interrupted source-copy resumption and all 27 evaluated
MSBuild references. This is not a completed Windows conversion, fresh XR build,
Windows paging benchmark or headset gameplay qualification.

## Focused verification completed before the build

Release mod compilation passes with zero warnings/errors. Focused checks pass:
393 builder tests; 185 installer tests (23 platform skips); 832 weaver assertions;
3081 platform assertions / 17 defect controls;293 startup assertions / 19 controls;
660 content delivery assertions / 18 controls;103 actual filesystem Editor
exclusion assertions; actual SDK scope 30 assertions. Restored full-scope and
separate diagnostic-scope tests retain their respective native behavior.
The actual Quest glass/video GPU gates exercise Vulkan and GL, and the focused
Android Vulkan delivery gate retains its 28 observed banks / 56 stages. These
are local source/serialization/compilation proofs, not headset picture proof.

The first build attempt exposed native Wine DLLs in the managed-plugin package
audit. The selector now excludes the special `Assets/StreamingAssets` payload
root while retaining genuine managed plugins, including unrelated nested
folders with the same name. The two new regression tests fail before the fix
and pass afterward; native payload bytes are unchanged. The warm retry recovers
its exact archive/manifest transaction before updating the source identity.
The matching Unity Editor selector is tested through the actual production
checker against the real SDK: two valid payload layouts pass and six invalid
layouts fail as required.

Unity's native bundle pipeline can still compile required variants and emits
keyword-space assertions also seen in B624. The separate exhaustive compiler
matrix is disabled. A read-only review identifies native usage-cache lifetime
across asset unloading as a leading hypothesis, not a proved cause or harmless
condition. The existing identity and serialized-variant checks do not prove
live material keyword-space membership or the headset picture.

The Addressables pipeline already uses its internal cache; sampled B625 shader
finish records have local cache hits and zero newly compiled variants. A future
completed-content receipt should additionally avoid invoking the native bundle
builder and rereading unchanged archives for ordinary mod updates. Its key must
cover imported asset dependencies, effective group/schema GUIDs, referenced
managed type/layout dependencies, graphics/import settings and producer versions,
with verified native outputs. The observed native linker inventory contains 583
types across 24 assemblies and excludes the VR mod/player assembly. Current
reprepare preserves `Library` but can regenerate Addressables group identities;
that is a future invalidation hazard, not a proved cause of this build's misses.
This optimization has been reviewed, not implemented in the frozen B625 build.

## Next hardware run

Use the complete new Windows package and its current install script; APK-only
sideloading cannot complete the new expanded-content contract. Existing saves
should survive an update. Retain the install receipt/log as well as the capture.

1. Confirm B625 in the fresh capture. Record time from launch to first intro and
   from last intro to the menu. Repeat one cold application launch after quitting;
   neither run should contain a multi-minute content hash/extraction phase.
2. Observe all intros, their orientation/audio and a visible, usable menu in VR
   and passthrough. If the menu is black, leave it running at least 15 seconds so
   both bounded handover samples are captured. Capture a current screenshot.
3. Load an original Campaign scenario (and a purchased DLC scenario), inspect
   generated walls/terrain/obstacles/materials and interaction, then save, quit
   and reload. Report generation time and visible frame interruptions.
4. Enter Guildmaster, generate a scenario, save/reload and return to the menu.
   This mode is now enabled, not a disabled tooltip entry. Its dynamic generation
   is particularly useful for assessing the actual engine backend.
5. Exercise unchanged input, keyboard, audio and passthrough. If possible test
   an original session-code multiplayer connection with a PC player, recording
   both captures and the same scenario/seed.

Collect with `scripts/collect-quest-logs.cmd`; include the installer log if
installation failed. This run distinguishes backend initialization, source
rendering, presentation and overall startup cost. Success must be reported only
for features the actual headset run confirms.
