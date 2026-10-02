# Local Quest builder

`scripts/build-quest.py` runs real recovery, static integration, native compilation,
Unity Android builds, APK validation and optional ADB installation. Python 3.9+
and the documented external tools are required. Original game files, frozen
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
The explicitly selected `--target probe` is an OpenXR/passthrough/identity hardware
diagnostic, not evidence that the original game or complete mod works. For the
maintainer's explicitly authorized local development tests only, `--dummy-profile`
uses ID **0** and the visible name **Quest Local Test (DUMMY)**; ordinary player
builds never silently select it. The static Steam PNG is still required.

`prepare` produces the generated project without invoking the Android editor.
`build` also performs preparation. Each command captures current tracked mod
sources/resources/tools, new ordinary source/assets and declared local XR
dependencies, with content hashes. Ignored output and credentials are excluded.
Dirty developer inputs retain content provenance. No per-ModBuild patch list is
maintained. Original recovery caches depend on original input and recovery tools;
ordinary mod edits rebuild their affected project/integration/player outputs.
Failed stages have no success receipt. Completed stage outputs are hash-verified
before reuse; partially written snapshots are discarded atomically.

Install the verified latest APK with ADB (one authorized device or `--serial`):

```sh
python3 scripts/build-quest.py install --output-root /path/to/private/quest-output
python3 scripts/build-quest.py report --output-root /path/to/private/quest-output
```

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
python3 -m unittest discover -s tests/quest-builder -v
```

These tests verify mapping, ambiguity, process failures, immutable snapshots,
receipt invalidation, path isolation, APK contracts and installation safety with
negative controls. They do not certify Quest hardware rendering or multiplayer.
