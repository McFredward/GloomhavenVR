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
