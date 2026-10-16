# Local Quest builder

`scripts/build-quest.py` runs real recovery, static integration, native compilation,
Unity Android builds, APK validation and optional ADB installation. Python 3.11+
and the documented external tools are required; build hosts are Windows x64 and Linux x86_64.
The wireless installer has its own independent Python/ADB provisioning. Original game files, frozen
sources, private profiles, signing material and APKs remain local. This directory
contains no original game payload.

Both editing modes accept an APK or the portable Quest installation ZIP and
use the last completed APK in the selected `.ghvrq` workspace by default.
An alternative APK/ZIP path is optional when that completed output exists. A
typed or browsed path remains authoritative; cancelled/incomplete outputs are
not selected. Metadata-only discovery supports complete original-build reports
and completed updates; selecting a default never hashes the whole game bank.
Mod updates select the source included in the current
Builder archive, including its Quest runtime/weaver patches, rather than the mod
DLL in the owner's PC game folder. Profile-only edits retain the installed mod.

Normal builds now omit optional whole-project and whole-bank diagnostic readback.
The required asset conversions, native compilation, package/ABI contracts, owned
PC game/DLC admission and APK signature checks remain. Developers can request the
full audit with `--full-validation`; this also enables imported-scene/texture QA
and whole-reference audits. Audit receipts distinguish omitted work from passed
work and never claim headset rendering was verified.

Recovery uses bounded parallel exporter processes with private immutable input
views, per-process environments and CPU quotas. Successful batches merge in the
original order and publish independent durable receipts. Their temporary exports
and duplicate recipe YAML are deleted immediately after canonical ownership
closes; a failure retains earlier committed batches. Tight memory reduces
concurrency and allocation-pressure retries the unfinished batch. Parallelism
never launches two Unity Editors against the same Library.

File snapshots compute hashes while transferring bytes. Reliable file-change
witnesses carry a previously measured hash across compatible receipt owners;
unknown or changed metadata falls back to a real read. Native bank repacking
copies qualified compressed ZIP records directly, including ZIP64, and skips
whole-entry decompression on unchanged reuse. Initial file-backed packaging is
atomic and uses fast Deflate; existing native bundle compression remains intact.

See `.planning/QUEST-BUILD-STORAGE-TIME-20261012.md` for the storage/time review,
measured fixture scope, and remaining expensive required work. No fresh Windows
end-to-end duration or fixed disk-space ceiling has been established by these
source and fixture changes.

The initial tile view opens one operation at a time. Build, mod update and name
editing show only their setup/progress. Export, Quest connection/log capture and
storage management have separate views. Navigation preserves their existing
operation state and does not start another build; shared workspace locks still
prevent concurrent mutations. The completed build's export shortcut opens the
same export view. The resume tile appears only for an unfinished selected run;
a successful latest run does not revive an older failed run's resume prompt.

Code-compatible updates retain the original game payload and compile the current
managed/native code pair. If changed Quest scenes, shaders, native programs or
asset recipes require a new build, the update pauses with the concrete reason.
The user must explicitly confirm a new build before original conversion can
start. Choosing Update mod never silently authorizes that work. Independently
compatible recovery, preparation and Unity imports stay owned; unknown producer
changes never authorize reuse of obsolete generated content. Original PC inputs,
current profile/DLC selection and the source APK signature are still qualified.

Capture153412 exposed a false fallback after the compact compiler could not load
generated plugins. A missing serialized script is not an asset incompatibility
proof while Unity reports unresolved/self assembly references. Such import
failures retain the APK, game banks and completed compiler results for a targeted
producer repair. Even a genuine incompatible registry requires a user decision;
neither a current error nor an old migration receipt starts a full build itself.

The 2026-10-12 support capture114856 proves a false full migration from B665 to
B671: the raw original export alone took 6,531.928 seconds (about109 minutes),
followed by copies of189,701 recovered and191,488 prepared files. The changed
Builder orchestration and compute/shader checks were reviewed observer/audit
changes. Older signed compatibility capsules did not carry the newer producer
AST field, although their signed source inventories already identify every file.
Finite reviewed historical source hashes now supply that missing comparison;
unknown producer/ABI/native changes still request the actual required migration.

Update compilation follows consumed runtime/compiler and authored-art inputs,
rather than every Wizard/installer source file. A host-only update retains the
compiled native code and metadata and prepares signing tools without installing,
licensing or launching Unity. A code change still requires a matched IL2CPP
code/metadata pair. Its private compiler project retains Unity/Bee work across
updates; its first compilation has no measured Windows duration in this repair.
Code-only updates capture a separately owned small source subset, omitting unused
prebuilt converters, procedural-runtime archives and unchanged authored banks.
The signed update still records the complete actual mod source identity.

| Phase | Required work and reuse policy |
| --- | --- |
| Host tools | Reuse closed Windows Git/.NET receipts across sessions with actual accepted-file witnesses; unchanged archives are not rehashed/extracted. Legacy ownership is seeded after a closed tool stage. Linux system Git keeps its ordinary qualification. |
| Owned PC inputs and source | Retain exact inventories/snapshots; changed files receive targeted byte checks. Owned game/DLC qualification remains mandatory for every edit. |
| Original export and recovery | Needed for changed real original/producer inputs or missing results. Compact updates perform none of this work. Bound completed raw batches and prepared owners remain reusable. |
| Textures, audio, shaders and banks | Retain closed phases and repair changed/damaged outputs. The optional exhaustive shader audit remains off by default. Mutable Unity projects do not share hardlinks with recovery sources. |
| Preparation restart | Do not rewrite an unchanged large journal. Closed backup-writer witnesses avoid an extra full backup read before interrupted rollback; modified or unsupported witnesses still qualify actual backup bytes. |
| Unity and native compilation | Run only for changed code/art or real payload migration. Retain the stable compiler cache and existing resource-aware job admission. Never substitute an unmatched native binary/metadata pair. |
| APK publication and signing | Rewrite the APK once with the selected replacements and qualify its signature. Retain the existing complete external content-bank locator; do not copy/reconvert that bank for a compact update. |
| Connection and runtime captures | Count actual capture observations and show command activity/elapsed time. Known download/log/archive sizes receive measured byte progress; installed-APK hashes remain necessary for exact build attribution. |

The capture contains an old completed project and a new generated project with a
different full PC inventory key. It therefore proves additional generated trees,
not their exact allocated Windows disk usage or a complete duplication of every
file. Support now includes the selected route and bounded old/new inventory
differences when the matching fallback manifests exist. After the run has stopped,
use the storage-management preview to remove regenerable projects/recovery/Library
caches while preserving the completed APK, required game bank or exported ZIP,
signing identity and saves. Removing conversion/import caches saves space but
makes a future genuine full rebuild slower; compact code/profile updates can use
their retained finished payload without those original conversion caches.

When renewed qualification permits a compact update, the progress overview drops
an earlier false fallback's full-build task list while preserving the visible
overall progress floor. Only current update events close its new operations.
Device support evidence adds only the explicitly current utility log, bounded
activity history and a redacted summary; no APK, game assets or saves are exported.

Large-content packaging has a separate Android constraint. Meta recommends an
APK plus required expansion/asset files, installed together by its distribution
service. Its store packaging rule limits the APK to less than 1 GB and additional
files to 4 GB each; generic asset files are recommended to stay below 2 GB.
These are store requirements, not a claim that every sideloaded APK has a 1 GB
limit. See [Meta packaging](https://developers.meta.com/vr/resources/vrc-quest-packaging-5/),
[publishing](https://developers.meta.com/vr/resources/publish-apk/) and
[asset files](https://developers.meta.com/vr/documentation/android-apps/ps-assets/).

The complete retained game bank is about 11 GB. Android 14's v2/v3 APK verifier
rejects ZIP64 archives, and target SDK 30 requires at least a v2 signature.
Host controls qualify the older target-SDK-29/v1 ZIP64 alternative, but no Quest
installation or Unity archive-offset runtime result proves it. The current
Builder therefore does not silently downgrade Android targeting/signatures.
The maintainer approved one portable PC-side ZIP containing the normal signed
APK, its complete content bank and a small transport manifest. The Wizard's
Export action copies those existing bytes once with measured progress; it does
not run Unity, rebuild assets or recompress the game bank. Outer ZIP64 is allowed
without changing Android targeting or the APK signature format. Export requires
enough free space for the resulting transport file and publishes it atomically;
cancelled work preserves the previous completed output.

Updates and name edits accept that ZIP directly. Import copies and qualifies
only the APK, retaining the bank inside the source ZIP. Installation reads needed
game files directly from the nested bank. Signed per-file inventories qualify
delivered bytes; warm installation does not rescan the complete bank. Closed
update receipts retain the original bank locator after optional cache cleanup.
Keep the source ZIP or matching complete `.ghvrq` output until another complete
package has been exported. An APK alone does not contain the external game bank.

Before either editing mode, the Builder requires the matching locally installed
PC game and qualifies its version and DLC selection. Auto signing retains a
matching local key or signs imported APKs with this PC's own local key. Explicit
`--signing-mode original` requires the previous key; `new-owner` selects the
receiver's key. Private signing keys never enter the transport ZIP. Name-only
editing preserves the existing profile ID; an ordinary build uses the locally
selected PC account. Existing signature-replacement recovery preserves mutable
saves/settings before uninstalling; a failed save backup does not authorize
data loss. A shared ZIP is portable content, not an ownership credential.

The CLI can export the last completed workspace build or a selected APK/ZIP:

```sh
python3 scripts/build-quest.py export --output-root /path/to/private/quest-output \
  --export-path /path/to/GloomhavenVR-Quest.zip
python3 scripts/build-quest.py export --output-root /path/to/private/quest-output \
  --base-apk /path/to/completed.apk --export-path /path/to/GloomhavenVR-Quest.zip
```

Use `inspect` before conversion:

```sh
python3 scripts/build-quest.py inspect --game-root /path/to/owned/Gloomhaven \
  --output-root /path/to/private/quest-output
```

The first player build captures the installed Steam client's local account. On
Windows it uses Steam's local active-user marker and corresponding cached name;
an explicit different account is rejected until Steam is switched. Where that
marker is unavailable, multiple remembered accounts require `--steam-id` rather
than guessing from `MostRecent`. The installed Steam directory can be discovered
automatically or supplied through `--steam-root`. Provide a static Steam PNG with
`--steam-logo`; cached identity is not authenticated ownership.
An explicit identity-only JSON can also supply `steamId` as a full decimal string,
`displayName` and optional matching uint32 `accountId`. It rejects token/password
fields. No Quest-side store API or cloud service is used.

```sh
python3 scripts/build-quest.py build --game-root /path/to/owned/Gloomhaven \
  --output-root /path/to/private/quest-output --steam-root /path/to/Steam \
  --steam-id YOUR_FULL_STEAM_ID --steam-logo /path/to/steam-logo.png \
  --unity-editor /path/to/Unity/Editor/Unity
```

The default `--target game` invokes recovery and complete static integration. A
failed recovery, unsupported hook or failed tool invocation blocks that APK.
An exporter exit code of zero is insufficient: its complete original scene/catalog
closure and script bindings must be proven, and static integration must be complete
and free of issues. Complete asset, native ABI, Android shader/compute compilation
and signed delivery checks remain mandatory; headset acceptance is separate.
The full player and all authored Android mod banks use Vulkan. This keeps the
original Windows shader conventions for reversed depth and texture orientation;
the smaller startup/probe targets retain their independently tested GLES route.
Rewritten original DLLs replace their recovered
counterparts in place, retaining the exact `.meta` script mapping; the generated
`link.xml` is deployed for AOT preservation.
The explicitly selected `--target probe` is an OpenXR/passthrough/identity hardware
diagnostic, not evidence that the original game or complete mod works. For the
maintainer's explicitly authorized local development tests only, `--dummy-profile`
uses ID **0** and the visible name **Quest Local Test (DUMMY)**; ordinary player
builds never silently select it. The static Steam PNG is still required.
`--probe-assets /path/to/native-slice` optionally includes recovered native assets
under `Assets/Quest/Recovered`, preserving their GUID metadata and Resources
lookup names. The full slice is hashed and frozen as another input; C#, managed
DLLs, native executables and assembly definitions are rejected. The first local
slice uses the original animated BanditGuard and retains its explicit graphics
limitations in its report. It is not a campaign reconstruction.

`prepare` produces the generated project without invoking the Android editor.
`build` also performs preparation. Each command captures current tracked mod
sources/resources/tools, new ordinary source/assets and declared local XR
dependencies, with content hashes. Ignored output and credentials are excluded.
Dirty developer inputs retain content provenance. No per-ModBuild patch list is
maintained. Original recovery caches depend on original input and recovery tools;
ordinary mod edits rebuild their affected project/integration/player outputs.
Failed stages have no success receipt. Completed stage outputs are hash-verified
before reuse; partially written snapshots are discarded atomically.

Retained preparation uses unchanged file witnesses for fast reuse. Damaged or
missing files are restored individually from immutable recovered assets, frozen
mod sources or existing conversion outputs, with complete recorded byte proof.
Original GUID sidecars and Unity `Library` are retained. Interrupted repairs
resume their own file transaction without reopening completed preparation phases.
This includes scene LightingData rewritten by the Editor from YAML to binary.
If the exact project is open in Unity, the Builder waits visibly until it closes
and then continues automatically. Support exports include bounded repair history;
missing reconstruction sources are reported specifically rather than causing an
automatic whole-project reset. Details are recorded in the developer repair notes.

Completed Player delivery retains independent signing, APK, Shader and Compute
receipts. A failed final gate retries its own incomplete proof without reopening
conversion, the Unity project or successful sibling gates. Ancillary desktop
Compute banks qualify by exact original program/interface and bank identity;
independent export GUIDs and LF/CRLF source formatting follow their actual local
source/meta contract. The complete active Vulkan executable gate remains required.
Support metadata includes bounded owner IDs and original kernel hashes rather
than source programs. See the [Compute ownership evidence](../../.planning/QUEST-COMPUTE-OWNER-092443-20261011.md).

Late Sprite validation also follows the recorded case-path manifest transition
when an older Windows loading receipt predates that legitimate migration. The
exact original bytes, GUID metadata, original bank and imported drawing proof
remain required. A stale imported Sprite is synchronously reimported once for
that path and remeasured, without restarting conversion or clearing `Library`.
Repair progress and the precise failed identity are reported; an unverified
source change is never silently marked complete.

XR compilation references use a recipe/compiler/original-reference cache shared
across Wizard source releases. They do not invalidate the already converted
original assets. Native compilation uses process-local line-table debug metadata
while retaining the existing optimization and ABI. The formerly dominant message
dispatcher now compiles below0.5GiB on the pinned Linux compiler, instead of its
historical26GiB peak. Another measured translation unit needs1.6GiB; scheduling
therefore budgets2GiB per worker plus Unity/transient reserves. Available physical
memory and actual commit/swap capacity determine concurrency, with a single-worker
fallback rather than a fixed minimum-RAM rejection. Genuine allocation failures
retain Bee objects and preparation results, reduce concurrency, and resume after
a visible, cancellable memory backoff. Codec workers similarly retain successful
items and retry a failed item alone. These changes never alter system paging
settings. Whole Windows builds and lower-memory headset delivery remain hardware
acceptance work; the native actions have passed real4GiB address-space limits.

The first full Android SDK invocation imports the project before package API
binding. Before each Unity launch, the Wizard inventories known project and
resolved package asset paths using file metadata only. A fixed denominator and
qualified, persisted unique-path witnesses expose initial asset coverage before
Editor observers can load. Repeated imports never count the same path twice.
Coverage describes those known assets, not Unity's dynamic native import queue;
an actual native queue total takes precedence within its own batch. The finite
owner plan also counts method preparation and successful return, so a child
counter reaching its total cannot finish the entire Unity step.

Public Unity tasks retain their own identities, parent IDs, native fractions or
step totals. Shader passes and Bee compiler graphs keep actual local counters.
The generated Gradle observer reads the actual task graph and publishes live
successful/cached/skipped counts to `<Editor-log>.gradle-progress.jsonl`, avoiding
Unity's buffered Gradle output. An indivisible operation has one logical task;
its finer completion is never guessed from time or a generated-file census.
Historical overview snapshots are compacted while the current UI and detailed
progress log retain their context. API binding and content packing likewise keep
actual pass counters without resetting their parent progress.

Native loading sprites are left byte-identical when their original drawing
geometry is already restored. Older Windows checkpoints that only changed CRLF
to LF qualify through an exact inverse full-file hash, the original object/GUID
and the existing successful loading import receipts. This narrowly scoped
transport repair retains every closed conversion and the imported `Library`;
other source or geometry mismatches still require their specific repair.

The project and imported `Library` stay under
`<workspace>/build/projects/<original-workspace-key>` across compatible mod updates.
An externally installed Editor, global UPM cache and Gradle user cache can occupy
other locations. See [import evidence and storage](../../.planning/QUEST-UNITY-IMPORT-184004-20261009.md).

Install the verified latest APK with ADB (one authorized device or `--serial`):

```sh
python3 scripts/build-quest.py install --output-root /path/to/private/quest-output
python3 scripts/build-quest.py report --output-root /path/to/private/quest-output
```

For repeated Windows tests, run
`scripts/install-quest-wireless.cmd -OutputRoot "D:\Quest builds"` once with the
authorized Quest connected by USB. After that, double-click the same script to
reconnect over Wi-Fi and install this output's
latest verified APK. See the [wireless setup and options](../../.planning/QUEST3-WIRELESS-INSTALL.md).
The portable core is `scripts/install-quest-wireless.py`.

Installation first uses `adb install -r`. A genuine signing conflict uses the
existing save/settings recovery before replacement, retaining owned game content
where Android permits it. If the installed replacement cannot start its safe
storage-restoration process, the Wizard offers a manual Quest-library start
after arming a debugger wait, or an explicitly confirmed clean reinstall.
The latter deletes current and archived Quest saves/settings, retains existing
PC backups without replaying them, and reuses the completed APK. Missing game
banks need normal transfer again; clean reinstall is unavailable without the
complete local content package. Retry never implies reset consent. See the
[capture191308 recovery contract](../../.planning/QUEST-INSTALL-OPTIONS-191308-20261014.md).

Keep the output's `signing/quest.keystore` and private
`signing/local-key.json` to avoid unnecessary signing migration. These files are never included in
the APK, source snapshot, reports or command arguments. On Unix their permissions
are restricted to the local user. Android SDK/NDK/JDK overrides are available as
`--android-sdk`, `--android-ndk` and `--jdk`; they must match the selected Unity
Android support module. The output must be an empty/new directory, an already
marked builder directory, or the repository's ignored `.planning/quest3-local`.

Focused checks:

```sh
/path/to/private/quest-output/tool-cache/build-python/bin/python -m unittest discover -s tests/quest-builder -v
```

These tests verify mapping, active-account capture, ambiguity, process failures,
immutable snapshots, ordinary N -> N+1 code/resource/config/shader discovery,
receipt invalidation, native-slice ingress, retained DLL script identity, path
isolation, APK contracts and installation safety with negative controls. A real
fixture exporter returning zero with 177 placeholder shaders is still rejected
as a full game. They do not certify Quest hardware rendering or multiplayer.

Use the provisioned builder Python for these checks; native image fixtures need
its pinned recovery packages. On Windows the executable is beneath the same
private environment's `Scripts` directory.

The full player follows the current mod's public stereo contract. Its authored
head/environment shaders and per-eye screen callbacks currently require MultiPass;
the independent hardware probe retains SinglePassInstanced. The world-screen
adapter selects actual eye-pass captures in MultiPass and paired GPU sampling only
when the running XR mode draws both eyes together. Desktop session setup is unchanged.
The mod-bank compiler setting is independent of the player session setting.

APK validation requires the passthrough bridge, Unity OpenXR plugin and OpenXR
loader alongside IL2CPP and Unity. Every packaged native library must have an
ELF64 little-endian AArch64 shared-object header, even if its directory claims
ARM64. A mandatory Oculus eye-tracking feature or unused eye-tracking permission
blocks the Quest 3 build; an explicitly optional feature does not.

Native compute and original-null material source validation accepts each platform's
native line endings while retaining complete witnessed source/metadata hashes.
A stale imported compute object is reimported once at its exact path after source
proof, then its original identity and required Android kernel bank are checked
again. This late repair preserves completed preparation, existing GUID companions
and the remaining Unity Library; it does not repeat original conversion or run a
blanket shader sweep. Failed repairs report the asset and exact condition and
never silently reset a completed owner or loop indefinitely.
