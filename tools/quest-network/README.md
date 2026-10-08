# Original multiplayer preflight

Run `scripts/inspect-quest-network.py` with Python 3.9+ and a legitimately owned
local game's `GH_Data`. This tool reads inputs and writes a private JSON report.
It does not connect to Photon/EOS/Hydra, rewrite assemblies, export configuration
values, or prove a playable multiplayer session.

```sh
DOTNET_ROOT=/path/to/dotnet python3 scripts/inspect-quest-network.py \
  --game-data /path/to/Gloomhaven/GH_Data \
  --decompiled /path/to/decompiled \
  --recovered-project /private/recovered-project \
  --ilspy /path/to/ilspycmd \
  --report /private/network-audit.json \
  --require-local-candidate
```

`--ilspy` is a trusted local ILSpy executable. Its bounded subprocess output is
kept in memory, never echoed or copied into the report. With this option the
relevant bodies are obtained from the selected original managed DLLs; existing
decompiled files alone cannot prove correspondence with the installed binaries.
No decompiler or SDK download happens automatically.

`--recovered-project` supplies the genuine recovered
`Assets/Resources/BoltRuntimeSettings.asset`. Only the app ID's presence/UUID
validity and an allowlist of numeric transport settings are reported. The entire
file is hashed. Optional repeatable `--appclient-config` inputs are hash-only:
the tool does not guess their semantics or export credentials. Optional
`--native-candidates` directories provide local libraries to inspect by SHA256
and ELF/PE architecture, without executing them. They do not imply ABI or
runtime compatibility.

Exit 0 means the audit report was written. `--require-local-candidate` returns
2 if the scoped source patterns, required original managed inputs, or private
Bolt configuration are incomplete. Exit 1 means inspection/output failed. All
statuses keep backend acceptance, Android connectivity, and PC-host join false
until separate runtime evidence exists. Source-pattern matching is deliberately
scoped and does not substitute for a full call-graph or IL2CPP proof.

Reports cannot overwrite the selected game, source, tool, recovered project, or
native candidate inputs. Repository-local reports must use `.planning/debug/`
or `.planning/quest3-local/`; the default is
`.planning/debug/quest3/network/network-audit.json`. The input key includes the
auditor/decompiler hashes and source/configuration/library hashes, and inspection
fails if an input changes during the run. Do not commit generated reports or
publisher/player inputs.

The concrete next device test and the narrowly optional EOS route are recorded
in [the network preflight plan](../../.planning/QUEST3-NETWORK-PREFLIGHT.md).

## Explicit desktop connection smoke

This separate command makes a real Photon connection. It copies seven original
unmodified network DLLs and their recovered importers plus the original Bolt
settings into a fresh private Unity project. It invokes the original
`PhotonPlatformConfig`/`PhotonClient`, reaches the original default lobby, then
calls the original client's disable/disconnect method. It never creates or joins
a game room, starts EOS, or reads a store account/ticket. This is desktop editor
evidence and cannot establish Android connectivity or game-host admission.

```sh
python3 scripts/quest-network-smoke.py \
  --game-data /path/to/Gloomhaven/GH_Data \
  --recovered-project /private/recovered-project \
  --unity-editor /path/to/2021.3.5f1/Editor/Unity \
  --output-root /private/network-smoke \
  --timeout 45
```

Keep `unity-private.log`, copied configuration and original DLLs private. Console
output only states coarse outcome and evidence paths. Each fresh project has its
own runtime/result report; receipt flags and process exit must agree before the
command reports a connection. Missing receipts, timeouts, nonzero exits, callback
failure or expanded EOS/room/Android flags fail verification. The timeout bounds
network work to 10–90 seconds, plus 180 seconds for editor startup/import.

## Windows compiler paths

The native voice builder invokes the NDK's real `clang.exe` with the explicit
Android 29 ARM64 target and sysroot. It does not forward owner-selected paths
through the NDK's `.cmd` wrapper. Windows batch files can receive shell parsing
even with Python's default `shell=False`; ordinary `&` and `%` characters must
remain literal file names. The CMake/Ninja dependency pins and original Opus ABI
remain unchanged. See the [NDK direct compiler instructions](https://developer.android.com/ndk/guides/other_build_systems)
and [Python subprocess behavior](https://docs.python.org/3.14/library/subprocess.html#security-considerations).
