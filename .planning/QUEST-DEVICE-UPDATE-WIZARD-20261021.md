# Installed Quest update source — Wizard lane, 2026-10-21

## Contract and boundaries

The maintainer explicitly permits an installed Quest game to be the update input;
a PC game installation is not required for that source. New full builds keep the
existing purchased-PC-copy flow. Runtime ModBuild remains 717: these are host
workflow changes, not a new game runtime.

The update form now distinguishes a local APK/portable ZIP from an installed
Quest game. Both can retain the APK's existing player identity, provider logo and
DLC ownership with `installedGame: true`. No launcher, Steam identity, DLC query,
logo download or PC game-root qualification runs in that mode. Name-only profile
edits copy the bounded existing profile JSON, preserve its provider/ID and pass
the ordinary `--profile-json` input. No private signing key can be recovered from
an installed APK; the existing explicit signing/migration choices still apply.

`deviceSource` records only `{deviceSerial, versionName}` beside the exact local
`baseApk` selected after acquisition. The actual APK/seed validators remain the
update authority; this display metadata is not ownership, signature or byte
proof. Session keys distinguish installed inputs without changing old PC-flow
keys. Source provisioning uses the qualified seed's Managed references directly,
without pretending that its small directory is a complete PC installation.

## Device discovery and acquisition

Authenticated `/api/device/source-discover` and `/api/device/source-import` reuse
the existing exclusive, cancellable DeviceTools worker and its actual ADB runner.
Opening an update checks already prepared ADB and already authorized devices;
no missing-tool download or absent-headset reconnect starts during automatic
metadata discovery. USB is preferred through the existing identity-checked
selection. An absent Quest does not remove the local APK choice.

Package discovery reads only Android package/version/file metadata. It never
transfers the APK, external game-content banks or saves. Importing the installed
base APK requires the explicit Read Quest installation action, can provision ADB,
reports the helper's actual byte counts, can be cancelled, and resumes the
helper's retained chunks. The temporary APK/compiler/update workspace still
requires PC disk space. The approximately 11 GB installed content is not copied.

Status responses intentionally omit the large input manifest and current mod
file inventory. They expose a small version/input identity, seed availability
and source path only. Polling does not independently inspect the headset or walk
workspace files. Returning home keeps an in-progress import visible and running.

## Older APK migration

Older APKs may lack original Mono compiler references. The import remains useful
and exposes that state. The Wizard offers a one-time matching-PC-copy update;
choosing it opens the input form while preserving the acquired base APK. It does
not launch conversion, silently change the selected update to a build, or claim
that native IL2CPP code contains the original Mono assemblies.

The exact missing-seed and original-derived-assets/player-layout preflight
messages become an `installed_update_pc_required` blocked input choice with a
visible PC-assisted update button. No full build is authorized by that action.
Seeded APK recommendation hints no longer require local external content banks
or a PC installation. Their closed receipts/bounded metadata remain hints;
normal explicit update preflight still validates the actual inputs.

## Focused evidence and limits

Evidence root: `/home/claw/quest3-local/build/evidence/quest-device-update-20261021/wizard`.

- New installed-source engine tests cover explicit update-only selection, stale
  PC-option removal, exact path/session binding, no-PC CLI arguments, retained
  profile/logo, name-only GOG identity, qualified seed reference provisioning,
  disk reserve, missing-seed action and no Steam-logo network requirement.
- DeviceTools tests cover metadata-only discovery, absent-ADB no-download,
  measured transfer callbacks and bounded status projection; actual installer
  byte acquisition/seed validation belongs to the independently reviewed helper.
- Authenticated HTTP tests exercise asynchronous ownership, measured status and
  cancellation without a build session.
- Actual Chrome tests exercise local and Quest update source selection, explicit
  import, byte progress, exact submitted APK/device binding and a visible older
  APK PC-assisted choice. They use declared device fixtures, not a real headset.
- Existing focused APK-update, recommendation, source-copy and related UI/backend
  checks are retained separately. No unrelated full gate is claimed.

No Windows/Quest end-to-end compiler or hardware installation outcome is claimed
by the UI tests. The primary integration must qualify the actual seed/CLI helper
composition and the delivered game-free release package.
