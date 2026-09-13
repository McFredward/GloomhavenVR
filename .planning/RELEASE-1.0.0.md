# Release 1.0.0 — ModBuild 498

> Historical build-498 release record. The 2026-09-13 build-499 hotfix is tracked in
> [REST-499.md](REST-499.md); it keeps version 1.0.0 but does not alter this published tag.

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

Local candidate checks passed: all 17 guard checkers; 253,579 wire assertions; production
card capture 18,206, native playback 466 and board refresh 1,216 assertions; all 12 runtime
negative controls. Strict Release: zero warnings/errors. EN/DE documentation, all 16
metadata-only reference assemblies and the exact-editor bundle check passed.

Compiled comparison against `21648697`: 13 changed types, none added/removed. An exact
normalized comparison proves all changes are embedded version 0.9.1 to 1.0.0 and ModBuild
497 to 498 constants. Configuration keys 625, patch surface 152 and log tokens 4,716 remain.

A separate fixture compiles the unchanged production updater from tag `v0.9.0`: it accepts
both upgrade comparisons and the new nested license paths, and rejects a license at the
ZIP root. The complete `Core/SelfUpdate` source tree is unchanged since 0.9.0.

Hosted publication is verified from the actual Release run and its tag/archive. No standalone
dev release is used. Hardware application/restart is not covered by the local fixture.

## Updater test

The updater queries the public `/releases/latest` endpoint without credentials. The
repository must be public before starting the test. Restart the game after changing visibility
and remain in the VR main menu; a visibility change alone does not trigger another request.
Start from an installed older version:
0.9.0 release checks normally; a 0.9.1 development build needs
`[Dev] UpdateCheckOnDevBuilds = true` in `BepInEx/config/dev.gloomhavenvr.cfg`.
Do not install 1.0.0 manually first when testing discovery of the upgrade to 1.0.0.

Actual headset download/apply/restart and full-party performance remain hardware checks.
Keep `BepInEx/LogOutput.log` and `BepInEx/GloomhavenVR-update/update.log` if the update fails. The earlier rare long-rest board visibility
report remains unresolved; publication does not change that evidence.
