# Release 0.9.0 — ModBuild 494

## Scope

User-authorized release on `main` (the repository has no `master` release branch),
using the existing GitHub Actions pipeline. Gameplay and native presentation carry
build 493 unchanged. The full package includes the build-483 asset bundle.

The release must be a regular Latest release: the updater queries `/releases/latest`
and skips prereleases. Repository visibility is a separate owner action. A previously
installed dev build needs `[Dev] UpdateCheckOnDevBuilds = true` to test discovery;
the release build enables the existing update check without that override.

## CI repair

Hosted dev run 34405321856 failed compiling the reversed-fragment fixture at
CardAppearanceGroupVectors.cs:68 (CS1579). SDK 10 reproduced the overload selection
of the in-place span Reverse method; SDK 8 compiled the old expression. The fixture
now calls Enumerable.Reverse explicitly, retaining both transport modes. A global.json
selects stable SDK 8.0.4xx with latestPatch; workflows print dotnet --info and execute
the standalone capture, playback and board-refresh harnesses with negative controls.
The hosted wire suite remains compile-only because execution needs real game binaries.

## Local evidence

- All 17 guard checkers pass; wire: 253,055 assertions; capture: 18,206;
  playback: 466; board refresh: 1,216; 12 runtime negative controls.
- Compiled comparison with 36100039: 13 changed types, exclusively version/build
  constants; no added or removed types. Config, patch and log surfaces unchanged.
- Documentation localization and all 16 metadata-only reference assemblies pass.
- Three-release simulation passes, retaining main as an ancestor of dev.

GitHub CI/publication results must be read from the actual Actions runs. Local
checks do not establish successful publication, headset update installation, or
full-party performance. The reported long-rest board disappearance remains open.
