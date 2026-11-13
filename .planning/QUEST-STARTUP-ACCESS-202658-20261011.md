# Quest startup access and existing-APK defaults — 2026-10-11

## Current hardware evidence

`quest-capture-20261011T202658Z-02d5b39f.zip` confirms installed B665, input
`9207a0cf49519b717a85c4d3e6f13f9dade1b1cbb2e3bb460e4484365899dbef`, APK
SHA256 `0a585c616387ae5b3eca83a490d3a990c39fc7eccc122d7273a9dceeaa1a094f`.
Its current startup snapshot fails at `mod-content-delivery` with an
`UnauthorizedAccessException` for `quest-mod-resources/StreamingAssets`.
The original scene, real mod activation, Addressables and engine execution have
not begun. The copied mod archive is 245,766,594 bytes; preparation remains 0/5.

The restored startup/hardware/procedural log files are older October 3/6 records,
including B625. They must not be interpreted as current B665 engine failures.
The current state is written through temporary-file replacement, whereas the
log constructor opens/rewrites existing log files. That explains why the fresh
failure snapshot can coexist with unreadable/unwritable old logs.

## Source-proven boundary and repair

The former restoration path reused source capture access. For a shell-readable
external root, `tar -xof` extracted under the shell identity, retaining restrictive
file/directory modes. Its byte/type comparison could succeed without proving
that the replacement app could read those files. Distinct-UID Linux process
controls reproduce this mechanism; the actual Quest FUSE ownership/mode state
was not included in the capture and remains a hardware boundary.

External saves/settings now restore through a freshly proved replacement-app
storage view. Shell capture and app extraction remain separate. The guarded
debugger-wait/process namespace is used only when direct app access is unavailable,
and is closed before normal gameplay.

Installed content reuse additionally opens the compact completed receipt and
three representative payload files from the app's view. A denied bank receives
permission repair only for its exact manifested directories and files, followed
by another app-context open proof. No recursive census, full payload hash, bank
copy, or save overwrite is part of this access repair. Partial receipts cannot
claim completion through either installer reuse shortcut.

Previously shell-owned small logs/settings can also be repaired after the bank
view closes. Only seven exact diagnostic names and a matching source/device-bound
prior non-bank inventory are eligible. Healthy files receive one-byte open checks;
denied files use their current bytes, independent shell/app stat and SHA witnesses,
and an atomic private app-owned replacement. The limit is 2 MiB per file and
64 MiB total. Historical save archives are never replayed and unknown save paths
are not traversed. Parent write bits and successful replacements with a lost ADB
acknowledgement remain preserved. A reconnect retries this bounded operation.

`Quest-Repair-Access.cmd` is the shortest next B665 test on Windows. It uses
`%USERPROFILE%\.ghvrq\build` and the remembered `.ghvrq\device` connection by
default. A custom workspace/APK can be supplied to `scripts/repair-quest-access.ps1`
with `-StateRoot`/`-Apk`. The Linux counterpart is `Quest-Repair-Access.sh`.
The repair admits only the matching installed APK, using its version, package
path and one exact APK checksum read. It does not reinstall the APK, run Unity,
or scan/copy the external game bank. Normal USB-first/same-headset connection
rules still apply. Bounded logs and an access-repair receipt remain in `device`.

## APK updates and release ancestry

The wizard now suggests an existing completed APK from `.ghvrq` for the mod/name
update workflows. Discovery reads small completion metadata and file size only;
the usual update preflight still qualifies the selected APK and owned PC game.
Explicit custom/restored paths take precedence. Updates compile changed IL2CPP
code with its matching metadata and retain unchanged game payloads. Native
program/interop drift remains an explicit compatibility gate; this capture does
not contain the B665 capsule inventory, so exact update admission is not claimed.

The user requested a rebase onto the release branch. The repository names that
branch `main`; no remote `master` exists. `origin/main` and `v1.1.0` both designate
`5d6701d261a608ffb919b4de8fd4d3687c809303`, already an ancestor of the Quest
checkpoint. A merge-preserving, no-fork-point rebase completed at the same
`ab8f08af74d18cb6282b1b04a4f5b266b1fc56b1` source/history checkpoint. No force push
or dev integration is required. Runtime remains release ModBuild671 for new
builds; this installer-only test retains B665.

Fresh focused checks, independent process controls, the exact source/ZIP audit
and cleanup measurements are recorded beside the private handoff receipt.
No local game/APK build is performed this round. Headset startup and picture,
Windows end-to-end execution and native generation remain unverified outcomes.
