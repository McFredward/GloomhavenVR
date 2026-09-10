# Release 1.0.0 — ModBuild 498

## Authorization and scope

On 2026-09-10 the maintainer requested release 1.0.0 from `main`, retaining existing
license records without further license work. The maintainer will change repository
visibility personally. No visibility change or history rewrite is part of this release.

Prepared from `dev` at `acfe1c65`. Gameplay, network records and visual behavior carry
build 497 unchanged. The full package includes the reviewed build 483 asset bundle
(74,943,763 bytes), installer/uninstaller fixes, explicit local-bundle selection, software
notices and current bilingual guides. ModBuild 498 identifies this shared release.

## Release procedure and verification

The candidate is committed and checked on `dev` before fast-forwarding `main` to that
exact commit. Only the existing main-triggered workflow creates the tag and release;
`GhvrReleaseBuild=true` stamps release identity, and the regular release is marked Latest.
The workflow subsequently advances the version on `dev` to 1.0.1.

Local required gates, compiled changes and hosted publication results must be recorded
from their actual outcomes. The candidate is not a published release until the pipeline
has produced its tag, release page and full archive. No standalone dev release is used.

## Updater test

The updater queries the public `/releases/latest` endpoint without credentials. The
repository must be public before starting the test. Start from an installed older version:
0.9.0 release checks normally; a 0.9.1 development build needs
`[Dev] UpdateCheckOnDevBuilds = true` in `BepInEx/config/dev.gloomhavenvr.cfg`.
Do not install 1.0.0 manually first when testing discovery of the upgrade to 1.0.0.

Actual headset download/apply/restart and full-party performance remain hardware checks.
Keep the affected run's log if the update fails. The earlier rare long-rest board visibility
report remains unresolved; publication does not change that evidence.
