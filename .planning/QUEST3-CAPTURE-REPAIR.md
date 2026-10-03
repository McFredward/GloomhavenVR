# Quest diagnostic capture repair (2026-10-03)

## Confirmed Windows transfer defect

The supplied private capture `quest-capture-20261003T105506Z-23bea50b.zip`
records successful Windows ADB pulls in `adb-commands.log`. ADB returned exit
code zero and wrote `1 file pulled, 0 skipped` to stderr for these allowlisted
files:

| File | Transferred bytes |
| --- | ---: |
| `quest-hardware.log` | 11,346 |
| `quest-hardware-state.json` | 10,728 |
| `quest-hardware-storage.json` | 74 |

The collector inspected stdout alone, rejected those successful transfers,
deleted the local files, and then attempted its fixed-path `run-as` fallback.
That fallback was unavailable. The resulting ZIP contains logcat and metadata,
but none of the three app files. The `.previous` log was genuinely missing.
The deleted contents cannot be reconstructed from this ZIP.

The collector now reads both status streams for `adb pull` while keeping stderr
out of captured app/log content. It requires a successful single-file receipt,
an existing bounded local file, a size matching the remote pre-transfer probe,
and a matching receipt byte count when ADB provides one. Failed, empty,
incomplete, or inconsistent transfers remain reported gaps and are discarded;
the existing bounded read-only fallback remains available.

## Captured installed identity and hardware feedback

The capture directly reports the installed APK as 30,969,125 bytes with SHA256:

```text
1751bc9c2301ad9c292eff5b53f0ffaae7d0a91cc2378a2225d3a5bfb9075857
```

Both the local handoff and local install receipt match that installed APK hash.
The B611 diagnostic handoff carries input key:

```text
d10caf94208a3bcf7b42958e20624f13be4f433ca1ffc6731aa4b2e065e72bd3
```

This associates the installed bytes with the local B611 handoff. Android's
`versionCode=1` / `versionName=0.1.0` alone does not identify the mod build.
The capture found no current PID or app banner, so those are not additional
runtime build evidence. This remains a diagnostic APK using the visibly marked
dummy profile, rather than a playable full-game port.

The user's hardware feedback explicitly accepts the diagnostic animation,
lighting, textures, and working buttons. The user also requests vertical camera
movement from right-stick up/down and reports occasional strong aliasing on
text/window borders. Those observations do not establish a cause or authorize
optimization polish in the startup/menu implementation round.

Because the app files were discarded, this capture cannot establish their
recorded render/controller/storage state or timing measurements. Short logcat
samples from the small diagnostic scene do not establish full-game capacity.
A future hardware collection is needed to verify repaired file retention.

## Optional original-game startup diagnostics

The collector also accepts exactly `quest-startup.log`,
`quest-startup.previous.log`, and `quest-startup-state.json` from the
original-game bootstrap. The previous log retains the rotated previous run.
They use the same
fixed app directory, 2 MiB per-file bound, verified pull receipts and fixed-path
read-only fallback. No other save/configuration files are requested.

These three files are optional for older/probe APKs. If neither the bounded direct
probe nor fallback can access them, the manifest records
`optionalFilesUnavailable` separately; this does not turn an otherwise complete
probe collection into a failure. A known-present empty, oversized or failed/
inconsistent transfer remains an error. Startup logs alone can provide usable
capture evidence when the app has already exited and no probe log/PID exists.

## Focused validation

The collector suite passes 37 tests with zero skips, including real portable
PowerShell Legacy-mode wrapper checks. New controls cover successful stderr
receipts (including the three captured file sizes), nonzero exits, zero-exit
transfer errors, missing/empty/partial files, mismatched metadata/receipt sizes,
and absent success receipts. These checks prove the source repair and failure
handling; they do not claim a new headset capture has been performed.
Startup controls cover stderr success, rotated previous-run retention, older APK absence, bounded allowlisted
fallback, real transfer failure, oversized state and capture after process exit.
