# Local Quest builder

`scripts/build-quest.py` runs real recovery, static integration, native compilation,
Unity Android builds, APK validation and optional ADB installation. Python 3.11+
and the documented external tools are required; build hosts are Windows x64 and Linux x86_64.
The wireless installer has its own independent Python/ADB provisioning. Original game files, frozen
sources, private profiles, signing material and APKs remain local. This directory
contains no original game payload.

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

Installation uses `adb install -r`, never automatic uninstall. Keep the output's
`signing/quest.keystore` and private `signing/local-key.json` for updates; replacing
the key prevents data-preserving APK updates. These files are never included in
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
