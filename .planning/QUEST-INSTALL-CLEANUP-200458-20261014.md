# Quest content-transfer repair and device scratch cleanup

Capture: `quest-build-support-20261014T200458Z-0852d543.zip`.
Integration branch: `feature/quest3-standalone`; runtime remains B672 / 1.1.1.

## Observed failure

The report identifies Builder `2a9147fc2388013faf3f8354723cef95a4b37e95`,
the completed B672 APK with input `d4e76da00ec2` and SHA-256
`191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
The authorized USB Quest completes the explicitly selected clean installation.
Recovery reaches `reset-installed`; original PC backups remain retained and
are deliberately not restored. This is no longer the preceding signing failure.

The following recursive ADB push fails with `remote secure_mkdirs failed:
Operation not permitted`. Shell directory creation and a small receipt upload
succeed. No device free-space observation or ENOSPC failure establishes either
accumulated bytes or a full Quest. The two current game banks total
11,755,896,182 bytes; their canonical paths are reused rather than allocating
another complete bank for every attempt.

## Minimal continuation

Create the current batch's exact manifest parent directories through the already
working shell access before ordinary SYNC. AOSP's
[SYNC implementation](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/daemon/file_sync_service.cpp)
performs ownership changes on directories it creates. Preexisting directories
avoid that particular metadata operation. If the same narrowly recognized
ownership error persists, use the existing atomic shell-v2 binary uploader.
Qualify only potentially completed files in the interrupted batch and journal
accepted files before continuing; never hash the entire installed game bank.

Keep the existing APK, completed build, reset journal and accepted content.
Retry does not need Unity, another APK export, another uninstall or old-save
restoration. Exact metadata and app-origin content checks remain enforced.
Real Quest firmware behavior still requires the next hardware test.

## Cleanup ownership and presentation

The existing device-tools page offers a bounded temporary-file preview and an
explicit confirmation bound to that preview and the physical Quest. New jobs,
cancellation, changed evidence or a different device invalidate authorization.
Existing USB-first selection, wireless fallback, job exclusion and progress are
reused; no build session is required for cleanup.

Inspect only reserved UUID upload names under the two known StreamingAssets
roots and exact scratch paths justified by device-bound installation evidence.
Deletion requires a stopped app, ordinary ancestors, unchanged file metadata
and unchanged host ownership receipts. Do not follow links or recursively
delete unknown directories. Larger orphan accumulations are processed in
bounded previews; remaining inventory is explicitly reported as unknown.

Keep canonical game content, accepted/pending resume receipts, saves, settings,
PC original backups and unproved legacy recovery parking. Inaccessible data is
unknown rather than zero. Quantities describe confirmed logical file bytes,
not a measured change in the filesystem's free blocks. The completion wording
therefore reports removed data rather than asserting physical free space.

After successful installation, a separate best-effort hook may remove only the
current terminal recovery's exact small helper/private stage. It scans neither
history nor game banks, never changes debugger state and cannot invalidate a
completed installation. Running-game or inaccessible scratch remains deferred
to the explicit device-tools action. Existing upload ownership already cleans
the active atomic stage after successful transfer.

Support exports retain bounded cleanup counts, unknown quantities and current
device logs without exporting save member names or backup contents. A successful
reset's retained-backup banner can now bind the recovery summary when a later
content failure occurs outside the signing-error wrapper; full build identities
must still match.

## Validation and limits

Focused tests exercise real local shell/filesystem operations, actual Adb
command construction and binary sockets, HTTP authorization/job exclusion,
one-shot confirmation, cancellation and real Chrome UI interactions. Fixtures
use tiny payloads with representative bank metadata, not an eleven-gigabyte
headset transfer. Failed intermediate fixtures and one timed-out worker test
group are retained with their subsequent scoped passing reruns.

The handoff validation records exact source hashes, previous-release producer
compatibility, the actual extracted game-free delivery and completion cleanup.
This is a host installer/Wizard repair; no new game build, Windows end-to-end
export, complete runtime/wire gate or successful Quest installation is claimed.

Evidence: `/home/claw/quest3-local/build/evidence/B672-install-cleanup-200458-20261014/`.
